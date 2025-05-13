// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;
using OllamaBaseApiClient = OllamaSharp.OllamaApiClient;
using OllamaMessage = OllamaSharp.Models.Chat.Message;
using OllamaChatRole = OllamaSharp.Models.Chat.ChatRole;
using OpenAIBaseApiClient = OpenAI.OpenAIClient;
using OpenAIMessage = OpenAI.Chat.ChatMessage;
using OpenAIChatRole = OpenAI.Chat.ChatMessageRole;
using AzureOpenAIBaseApiClient = Azure.AI.OpenAI.AzureOpenAIClient;
using System.ClientModel;

namespace LlmExtension;

internal sealed partial class LlmExtensionPage : DynamicListPage
{
    enum Service
    {
        Ollama,
        OpenAI,
        AzureOpenAI,
    }

    class ChatMessage
    {
        public required string User { get; set; }
        public required string Assistant { get; set; }

        public List<OllamaMessage> OllamaMessages()
        {
            var messages = new List<OllamaMessage>();

            if (!string.IsNullOrEmpty(Assistant))
            {
                messages.Add(new(OllamaChatRole.Assistant, Assistant));
            }

            if (!string.IsNullOrEmpty(User))
            {
                messages.Add(new(OllamaChatRole.User, User));
            }

            return messages;
        }

        public static OllamaMessage OllamaSystemMessage(string prompt) => new(OllamaChatRole.System, prompt);

        public List<OpenAIMessage> OpenAIMessages()
        {
            var messages = new List<OpenAIMessage>();

            if (!string.IsNullOrEmpty(Assistant))
            {
                messages.Add(new OpenAI.Chat.AssistantChatMessage(Assistant));
            }

            if (!string.IsNullOrEmpty(User))
            {
                messages.Add(new OpenAI.Chat.UserChatMessage(User));
            }

            return messages;
        }

        public static OpenAIMessage OpenAISystemMessage(string prompt) => new OpenAI.Chat.SystemChatMessage(prompt);
    }

    class Config
    {
        public Service Service { get; set; } = Service.Ollama;
        public string Url { get; set; } = "http://localhost:11434";
        public string? Model { get; set; }
        public bool Details { get; set; } = true;
        public string KeepAlive { get; set; } = "5m";
        public string? ApiKey { get; set; }
        public string System { get; set; } =
            "You are an AI assistant, you should provide help to the user with their query. " +
            "Act as if you are human with emotions, use fun and relaxing tone when appropriate. " +
            "However, remember you are still an AI model. Respond in Markdown format if necessary.";

        public int History { get; set; } = 6;
        public bool Debug { get; set; }
    }

    class ChatException : Exception
    {
        public required string DisplayMessage { get; set; }
    }

    class ServiceApiKeyException : Exception
    { }

    interface IApiClient
    {
        abstract public static IApiClient Create(Config config);

        public IAsyncEnumerable<string> Chat(IEnumerable<ChatMessage> messages, Config config);
    }

    partial class OllamaApiClient : OllamaBaseApiClient, IApiClient
    {
        public OllamaApiClient(Config config) : base(new HttpClient()
        {
            Timeout = TimeSpan.FromHours(1),
            BaseAddress = new Uri(config.Url)
        })
        { }

        public static IApiClient Create(Config config)
        {
            return new OllamaApiClient(config);
        }

        async public IAsyncEnumerable<string> Chat(IEnumerable<ChatMessage> messages, Config config)
        {
            var ollamaMessages = messages
                .SelectMany(m => m.OllamaMessages())
                .Take(config.History)
                .Reverse()
                .ToList();

            ollamaMessages.Insert(0, ChatMessage.OllamaSystemMessage(config.System));

            await foreach (var response in ChatAsync(
                new() { Messages = ollamaMessages, Model = config.Model, KeepAlive = config.KeepAlive },
                cancellationToken: CancellationToken.None))
            {
                yield return response?.Message.Content ?? "";
            }
        }
    }

    partial class OpenAIApiClient : OpenAIBaseApiClient, IApiClient
    {
        public OpenAIApiClient(Config config): base(
            new ApiKeyCredential(config.ApiKey ?? " "),
            new OpenAI.OpenAIClientOptions() { Endpoint = string.IsNullOrEmpty(config.Url) ? null : new Uri(config.Url) })
        { }

        public static IApiClient Create(Config config)
        {
            return new OpenAIApiClient(config);
        }

        async public IAsyncEnumerable<string> Chat(IEnumerable<ChatMessage> messages, Config config)
        {
            var openAIMessages = messages
                .SelectMany(m => m.OpenAIMessages())
                .Take(config.History)
                .Reverse()
                .ToList();

            openAIMessages.Insert(0, ChatMessage.OpenAISystemMessage(config.System));

            await foreach (var response in GetChatClient(config.Model).CompleteChatStreamingAsync(
                openAIMessages, options: new OpenAI.Chat.ChatCompletionOptions(), cancellationToken: CancellationToken.None))
            {
                foreach (var part in response.ContentUpdate)
                {
                    if (part.Kind != OpenAI.Chat.ChatMessageContentPartKind.Text)
                        continue;

                    yield return part.Text;
                }
            }
        }
    }

    partial class AzureOpenAIApiClient : AzureOpenAIBaseApiClient, IApiClient
    {
        public AzureOpenAIApiClient(Config config) : base(
            new Uri(config.Url),
            new ApiKeyCredential(config.ApiKey ?? " "))
        { }

        public static IApiClient Create(Config config)
        {
            return new AzureOpenAIApiClient(config);
        }

        async public IAsyncEnumerable<string> Chat(IEnumerable<ChatMessage> messages, Config config)
        {
            var openAIMessages = messages
                .SelectMany(m => m.OpenAIMessages())
                .Take(config.History)
                .Reverse()
                .ToList();

            openAIMessages.Insert(0, ChatMessage.OpenAISystemMessage(config.System));

            await foreach (var response in GetChatClient(config.Model).CompleteChatStreamingAsync(
                openAIMessages, options: new OpenAI.Chat.ChatCompletionOptions(), cancellationToken: CancellationToken.None))
            {
                foreach (var part in response.ContentUpdate)
                {
                    if (part.Kind != OpenAI.Chat.ChatMessageContentPartKind.Text)
                        continue;

                    yield return part.Text;
                }
            }
        }
    }

    class Client
    {
        public required Config Config;
        public IApiClient ApiClient { get; private set; } = CreateApiClient(new Config());

        public void ReinitializeApiClient()
        {
            ApiClient = CreateApiClient(Config);
        }

        public static IApiClient CreateApiClient(Config config)
        {
            return config.Service switch
            {
                Service.Ollama => OllamaApiClient.Create(config),
                Service.OpenAI => OpenAIApiClient.Create(config),
                Service.AzureOpenAI => AzureOpenAIApiClient.Create(config),
                _ => throw new ArgumentException("Invalid service name")
            };
        }
    }

    private static readonly string ConfigPath = "%USERPROFILE%\\.config\\LlmExtensionForCmdPal\\config.json";

    private readonly Client _client;
    private readonly IList<ChatMessage> _messages;
    private readonly IDictionary<string, (string?, Func<string>, Func<(string, string)>?, Action<SendMessageCommand, object?, string>)> _commands;

    public LlmExtensionPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\SmallLlmExtensionLogo.png");
        Title = "LLM Chat";
        Name = "Chat with LLM";
        PlaceholderText = "Type here to chat, or start with '/' to use commands";
        ShowDetails = true;

        _client = new Client()
        {
            Config = ReadConfig()
        };
        _client.ReinitializeApiClient();
        _messages = [new() { User = "", Assistant = "" }];
        _commands = new Dictionary<string, (string?, Func<string>, Func<(string, string)>?, Action<SendMessageCommand, object?, string>)>()
        {
            { "service", (
                "<one-of-[ Ollama | OpenAI | AzureOpenAI ]>",
                () => $"Set the API service to call (currently: {_client.Config.Service})",
                null,
                (sender, args, opts) => {
                    Service? service = opts.ToLowerInvariant() switch
                    {
                        "ollama" => Service.Ollama,
                        "openai" => Service.OpenAI,
                        "azureopenai" => Service.AzureOpenAI,
                        _ => null,
                    };

                    if (service == null)
                    {
                        new ToastStatusMessage(new StatusMessage()
                        {
                            Message = $"Invalid service '{opts}', expected one of 'Ollama', 'OpenAI', 'AzureOpenAI'",
                            State = MessageState.Error,
                        })
                        {
                            Duration = 10000,
                        }.Show();
                        return;
                    }
                    else
                    {
                        _client.Config.Service = service ?? throw new ArgumentException();
                    }

                    RefreshConfigs();
                })
            },
            { "clear", (null, () => $"Clear message history ({_messages.Count} message" + (_messages.Count > 1 ? "s" : "") + ")", null, (sender, args, opts) => {
                _messages.Clear();
                _messages.Add(new() { User = "", Assistant = "" });
                SearchText = "";
                RaiseItemsChanged(_messages.Count);
            }) },
            { "url", ("<url>", () => $"Set server URL (current: {_client.Config.Url})", null, (sender, args, opts) =>
            {
                _client.Config.Url = opts;
                RefreshConfigs();
            }) },
            { "model", ("<model-name>", () => $"Set the model to use (current: {_client.Config.Model})", null, (sender, args, opts) =>
            {
                _client.Config.Model = opts;
                RefreshConfigs();
            }) },
            { "keepalive", ("<duration>", () => $"Set the keep-alive duration (Ollama only, current: {_client.Config.KeepAlive})", null, (sender, args, opts) =>
            {
                _client.Config.KeepAlive = opts;
                RefreshConfigs();
            }) },
            { "apikey", ("<api-key>", () => $"Set the API key (OpenAI or AzureOpenAI only)", null, (sender, args, opts) =>
            {
                _client.Config.ApiKey = opts;
                RefreshConfigs();
            }) },
            { "detail", (null, () => $"Toggle full detailed response on the side (current: {_client.Config.Details})", null, (sender, args, opts) =>
            {
                _client.Config.Details = !_client.Config.Details;
                RefreshConfigs();
            }) },
            { "system", (
                "<system-prompt>",
                () => $"Set the system prompt",
                () => ("Current System Prompt", _client.Config.System),
                (sender, args, opts) =>
                {
                    _client.Config.System = opts;
                    RefreshConfigs();
                }
            ) },
            { "history", (
                "<history-count>",
                () => $"Set the message history count (current: {_client.Config.History})",
                null,
                (sender, args, opts) =>
                {
                    try
                    {
                        var count = int.Parse(opts);

                        if (count <= 0)
                        {
                            new ToastStatusMessage(new StatusMessage()
                            {
                                Message = $"Invalid history count {count}, expected positive integer",
                                State = MessageState.Error,
                            })
                            {
                                Duration = 10000,
                            }.Show();
                            return;
                        }

                        _client.Config.History = count;

                        RefreshConfigs();
                    }
                    catch (FormatException) when (!_client.Config.Debug)
                    {
                        new ToastStatusMessage(new StatusMessage()
                        {
                            Message = $"Invalid history count '{opts}', expected integer",
                            State = MessageState.Error,
                        })
                        {
                            Duration = 10000,
                        }.Show();
                        return;
                    }
                }
            ) },
            { "debug", (null, () => $"Toggle printing of the complete exception (current: {_client.Config.Debug})", null, (sender, args, opts) =>
            {
                _client.Config.Debug = !_client.Config.Debug;
                RefreshConfigs();
            }) },
            { "reset", (null, () => "Reset all settings", null, (sender, args, opts) =>
            {
                _client.Config = new Config();
                RefreshConfigs();
            }) },
        };
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged(_messages.Count);

    public override IListItem[] GetItems()
    {
        try
        {
            if (!IsLoading && SearchText.StartsWith('/'))
            {
                var commandText = SearchText[1..];

                if (commandText.Contains(' '))
                {
                    commandText = commandText[..commandText.IndexOf(' ')];
                }

                return _commands
                    .OrderBy(c => Levenshtein(c.Key, commandText))
                    .Select(c =>
                    {
                        var command = new SendMessageCommand() { Debug = _client.Config.Debug };
                        command.SendMessage += (sender, args) =>
                        {
                            var opts = "";

                            if (!SearchText.StartsWith($"/{c.Key}", StringComparison.InvariantCulture))
                            {
                                new ToastStatusMessage(new StatusMessage()
                                {
                                    Message = $"Command '{SearchText}' not found",
                                    State = MessageState.Error,
                                })
                                {
                                    Duration = 10000,
                                }.Show();
                                return;
                            }

                            if (c.Value.Item1 != null)
                            {
                                if (SearchText.StartsWith($"/{c.Key} ", StringComparison.InvariantCulture) && !string.IsNullOrEmpty(SearchText[$"/{c.Key} ".Length..]))
                                {
                                    opts = SearchText[$"/{c.Key} ".Length..].Trim();
                                }
                                else
                                {
                                    new ToastStatusMessage(new StatusMessage()
                                    {
                                        Message = $"Expected argument '{c.Value.Item1}' for command '/{c.Key}'",
                                        State = MessageState.Error,
                                    })
                                    {
                                        Duration = 10000,
                                    }.Show();
                                    return;
                                }
                            }

                            c.Value.Item4.Invoke(sender, args, opts);
                        };

                        var item = new ListItem(command)
                        {
                            Title = $"/{c.Key}",
                            Subtitle = c.Value.Item2.Invoke()
                        };

                        if (c.Value.Item1 != null)
                        {
                            item.Title += $" {c.Value.Item1}";
                        }

                        if (c.Value.Item3 != null)
                        {
                            var details = c.Value.Item3.Invoke();
                            item.Details = new Details()
                            {
                                Title = details.Item1,
                                Body = details.Item2,
                            };
                        }

                        return item;
                    })
                    .ToArray();
            }

            if (_client.Config.Model == null)
            {
                return [new ListItem(new NoOpCommand()) { Icon = new IconInfo("\u26A0"), Title = "No model is set", Subtitle = "please set a model with '/model <model-name>'" }];
            }

            if (!IsLoading)
            {
                _messages[0].User = SearchText;
            }

            return _messages.SelectMany<ChatMessage, ListItem>(m =>
            {
                if (string.IsNullOrEmpty(m.Assistant))
                {
                    if (string.IsNullOrEmpty(m.User))
                    {
                        return [];
                    }
                    else
                    {
                        var command = new SendMessageCommand() { Debug = _client.Config.Debug };
                        if (!IsLoading)
                        {
                            command.SendMessage += async (sender, args) =>
                            {
                                try
                                {
                                    IsLoading = true;

                                    await foreach (var response in _client.ApiClient.Chat(_messages, _client.Config))
                                    {
                                        m.Assistant += response;
                                        RaiseItemsChanged(_messages.Count);
                                    }

                                    SearchText = "";
                                    _messages.Insert(0, new() { User = "", Assistant = "" });
                                    RaiseItemsChanged(_messages.Count);
                                }
                                catch (Exception ex) when (!_client.Config.Debug && (ex is HttpRequestException || ex is ClientResultException))
                                {
                                    new ToastStatusMessage(new StatusMessage()
                                    {
                                        Message = 
                                            $"Error calling API over HTTP, is there a '{_client.Config.Service}' server running and accepting connections " +
                                            $"at '{_client.Config.Url}' with model '{_client.Config.Model}'?",
                                        State = MessageState.Error,
                                    })
                                    {
                                        Duration = 10000,
                                    }.Show();
                                }
                                catch (Exception ex)
                                {
                                    new ToastStatusMessage(new StatusMessage()
                                    {
                                        Message = _client.Config.Debug ? ex.ToString() : "An error occurred when running command",
                                        State = MessageState.Error,
                                    })
                                    {
                                        Duration = 10000,
                                    }.Show();
                                }
                                finally
                                {
                                    IsLoading = false;
                                }
                            };
                        }

                        return [new ListItem(command) { Title = m.User, Subtitle = "Press enter to send" }];
                    }
                }
                else
                {
                    var item = new ListItem(new DetailedResponsePage(m.User, m.Assistant))
                    {
                        Title = m.Assistant,
                        Subtitle = m.User,
                    };

                    if (_client.Config.Details)
                    {
                        item.Details = new Details()
                        {
                            Title = m.User,
                            Body = m.Assistant,
                        };
                    }

                    return [item];
                }
            }).ToArray();
        }
        catch (Exception ex)
        {
            new ToastStatusMessage(new StatusMessage()
            {
                Message = ex.ToString(),
                State = MessageState.Error,
            })
            {
                Duration = 10000,
            }.Show();

            IsLoading = false;

            return [];
        }
    }

    private void SaveConfig()
    {
        var path = Environment.ExpandEnvironmentVariables(ConfigPath);
        var dir = Path.GetDirectoryName(path)!;

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_client.Config, new JsonSerializerOptions() { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static Config ReadConfig()
    {
        var path = Environment.ExpandEnvironmentVariables(ConfigPath);
        var dir = Path.GetDirectoryName(path)!;

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(path))
        {
            var defaultConfig = new Config();
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions() { WriteIndented = true });
            File.WriteAllText(path, json);
            return defaultConfig;
        }

        try
        {
            var fileContent = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<Config>(fileContent) ?? new Config();
            return config;
        }
        catch (JsonException)
        {
            var defaultConfig = new Config();
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions() { WriteIndented = true });
            File.WriteAllText(path, json);
            return defaultConfig;
        }
    }

    private void RefreshConfigs()
    {
        _client.ReinitializeApiClient();
        SaveConfig();
        SearchText = "";
        RaiseItemsChanged(_messages.Count);
    }

    private static int Levenshtein(string a, string b)
    {

        if (string.IsNullOrEmpty(a))
        {
            if (!string.IsNullOrEmpty(b))
            {
                return b.Length;
            }
            return 0;
        }

        if (string.IsNullOrEmpty(b))
        {
            if (!string.IsNullOrEmpty(a))
            {
                return a.Length;
            }
            return 0;
        }

        int cost;
        int[,] d = new int[a.Length + 1, b.Length + 1];
        int min1;
        int min2;
        int min3;

        for (int i = 0; i <= d.GetUpperBound(0); i += 1)
        {
            d[i, 0] = i;
        }

        for (int i = 0; i <= d.GetUpperBound(1); i += 1)
        {
            d[0, i] = i;
        }

        for (int i = 1; i <= d.GetUpperBound(0); i += 1)
        {
            for (int j = 1; j <= d.GetUpperBound(1); j += 1)
            {
                cost = (a[i - 1] != b[j - 1]) ? 1 : 0;

                min1 = d[i - 1, j] + 1;
                min2 = d[i, j - 1] + 1;
                min3 = d[i - 1, j - 1] + cost;
                d[i, j] = Math.Min(Math.Min(min1, min2), min3);
            }
        }

        return d[d.GetUpperBound(0), d.GetUpperBound(1)];

    }
}

internal sealed partial class SendMessageCommand : InvokableCommand
{
    public event TypedEventHandler<SendMessageCommand, ICommandResult?>? SendMessage;
    public required bool Debug { get; set; }

    public override ICommandResult Invoke()
    {
        CommandResult? result = null;

        try
        {
            SendMessage?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            new ToastStatusMessage(new StatusMessage()
            {
                Message = Debug ? ex.ToString() : "An error occurred when running the command",
                State = MessageState.Error,
            })
            {
                Duration = 10000,
            }.Show();
        }
        return CommandResult.KeepOpen();
    }
}

internal sealed partial class DetailedResponsePage : ContentPage
{
    public string User { get; set; }
    public string Assistant { get; set; }

    public DetailedResponsePage(string user, string assistant)
    {
        Title = "Detailed Response";
        User = user;
        Assistant = assistant;
    }

    public override IContent[] GetContent()
    {
        return [
            new MarkdownContent($"**{User}**\n\n---\n\n{Assistant}"),
        ];
    }
}