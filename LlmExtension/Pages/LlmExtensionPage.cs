// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;
using System.ClientModel;
using Microsoft.SemanticKernel.ChatCompletion;
using OllamaSharp;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.HuggingFace;
using Microsoft.SemanticKernel.Connectors.MistralAI;

namespace LlmExtension;

internal sealed partial class LlmExtensionPage : DynamicListPage
{
    /// <summary>
    ///     Supported services.
    /// </summary>
    enum Service
    {
        Ollama,
        OpenAI,
        AzureOpenAI,
        Google,
        Mistral,
    }

    /// <summary>
    ///     User and assistant chat conversation message.
    /// </summary>
    class ChatMessage
    {
        public required string User { get; set; }
        public required string Assistant { get; set; }
    }

    /// <summary>
    ///     User configurations.
    /// </summary>
    class Config
    {
        public Service Service { get; set; } = Service.Ollama;
        public string Url { get; set; } = "";
        public string? Model { get; set; }
        public bool Details { get; set; } = true;
        public string? ApiKey { get; set; }
        public string System { get; set; } =
            "You are an AI assistant, you should provide help to the user with their query. " +
            "Act as if you are human with emotions, use fun and relaxing tone when appropriate. " +
            "However, remember you are still an AI model. Respond in Markdown format if necessary.";

        public int History { get; set; } = 6;
        public bool Debug { get; set; }
    }

    /// <summary>
    ///     Client containing the configuration and the service.
    /// </summary>
    class Client
    {
        public required Config Config;
        private IChatCompletionService? Service { get; set; }
        public IEnumerable<string> MissingConfigs { get; private set; } = new List<string>();

        public void ReinitializeService()
        {
            MissingConfigs = MissingConfigsForService();

            if (MissingConfigs.Any())
            {
                return;
            }

            Service = CreateService(Config);
        }

        async public IAsyncEnumerable<string> Chat(IEnumerable<ChatMessage> messages)
        {
            if (Service == null) yield break;

            ChatHistory history = [];
            history.AddSystemMessage(Config.System);
            foreach (var message in messages)
            {
                if (!string.IsNullOrEmpty(message.User))
                {
                    history.AddUserMessage(message.User);

                    if (history.Count > Config.History + 1)
                    {
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(message.Assistant))
                {
                    history.AddAssistantMessage(message.Assistant);

                    if (history.Count > Config.History + 1)
                    {
                        break;
                    }
                }
            }

            await foreach (var content in Service.GetStreamingChatMessageContentsAsync(history))
            {
                if (string.IsNullOrEmpty(content.Content))
                {
                    continue;
                }

                yield return content.Content;
            }
        }

        public IEnumerable<string> MissingConfigsForService()
        {
            var missingFields = new List<string>();

            if (string.IsNullOrEmpty(Config.Model)) missingFields.Add("model");

            if (Config.Service == LlmExtensionPage.Service.Ollama)
            {
                if (string.IsNullOrEmpty(Config.Url)) missingFields.Add("url");
            }
            else if (Config.Service == LlmExtensionPage.Service.AzureOpenAI)
            {
                if (string.IsNullOrEmpty(Config.ApiKey)) missingFields.Add("apikey");
            }
            else if (Config.Service == LlmExtensionPage.Service.Google)
            {
                if (string.IsNullOrEmpty(Config.ApiKey)) missingFields.Add("apikey");
            }
            else if (Config.Service == LlmExtensionPage.Service.Mistral)
            {
                if (string.IsNullOrEmpty(Config.ApiKey)) missingFields.Add("apikey");
            }    

            return missingFields;
        }

        public static IChatCompletionService CreateService(Config config)
        {
            return config.Service switch
            {
                LlmExtensionPage.Service.Ollama => CreateOllamaService(config),
                LlmExtensionPage.Service.OpenAI => CreateOpenAIService(config),
                LlmExtensionPage.Service.AzureOpenAI => CreateAzureOpenAIService(config),
                LlmExtensionPage.Service.Google => CreateGoogleService(config),
                LlmExtensionPage.Service.Mistral => CreateMistralService(config),
                _ => throw new ArgumentException("Invalid service name")
            };
        }

#pragma warning disable SKEXP0001
        private static IChatCompletionService CreateOllamaService(Config config) => new OllamaApiClient(
                uriString: config.Url,
                defaultModel: config.Model ?? ""
        ).AsChatCompletionService();

        private static OpenAIChatCompletionService CreateOpenAIService(Config config) => new(
            config.Model ?? " ",
            new OpenAI.OpenAIClient(
                new ApiKeyCredential(config.ApiKey ?? " "),
                new OpenAI.OpenAIClientOptions() { Endpoint = string.IsNullOrEmpty(config.Url) ? null : new Uri(config.Url) }));

        private static AzureOpenAIChatCompletionService CreateAzureOpenAIService(Config config) => new(
            config.Model ?? " ",
            new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(config.Url), new ApiKeyCredential(config.ApiKey ?? " ")));

#pragma warning disable SKEXP0070
        private static GoogleAIGeminiChatCompletionService CreateGoogleService(Config config) => new(
            config.Model ?? " ",
            config.ApiKey ?? " ");

        private static MistralAIChatCompletionService CreateMistralService(Config config) => new(
            config.Model ?? " ",
            config.ApiKey ?? " ",
            string.IsNullOrEmpty(config.Url) ? null : new Uri(config.Url));
    }

    private static readonly string ConfigPath = "%USERPROFILE%\\.config\\LlmExtensionForCmdPal\\config.json";
    private static readonly string HelpMessage =
        "## What is this extension\n" +
        "\n" +
        "This extension allows you to chat with LLM models, including Ollama, OpenAI, Azure OpenAI, Google Gemini, or Mistral, either hosted by yourself or by a third party.\n" +
        "\n" +
        "## How to use this extension\n" +
        "\n" +
        "There are two ways to interact with this extension:\n" +
        "\n" +
        "1. **Chat**: Type your message in the search box and press enter to send it to the LLM model. The model will respond with a message.\n" +
        "\n" +
        "2. **Commands**: Type `/` in the search box to see a list of available commands. You can use these commands to configure the extension, such as setting the model, API key, and other options.\n" +
        "\n" +
        "## Setting up the extension\n" +
        "\n" +
        "1. **Setup your LLM model**: You need to have a LLM model running on your local machine or a server. Visit the supported service providers respective websites for more information on how to set them up.\n" +
        "\n" +
        "2. **Configure the extension**: Use commands to setup the connection with the LLM model.\n" +
        "\n" +
        "    - `/service <service>`: Set the API service to call (Ollama, OpenAI, AzureOpenAI, Google, or Mistral).\n" +
        "        - For other services with OpenAPI compatible APIs such as Docker Model Runner, use the `OpenAI` service.\n" +
        "    - `/url <url>`: Set the server URL.\n" +
        "        - For services that do not need a URL, you may enter `/url ` without any arguments.\n" +
        "        - For Ollama, usually `http://localhost:11434/`.\n" +
        "        - For AzureOpenAI, usually `https://your-id.openai.azure.com/`.\n" +
        "        - For Docker Model Runner, usually `https://localhost:your-port/engines/llama.cpp/v1/` with OpenAI service.\n" +
        "    - `/model <model-name>`: Set the model to use.\n" +
        "        - For AzureOpenAI, this is the deployment name.\n" +
        "    - `/apikey <api-key>`: Set the API key.\n" +
        "        - For Ollama, this is not applicable.\n" +
        "\n" +
        "3. **Send a message**: Type your message in the search box and press enter to send it to the LLM model. The model will respond with a message.\n" +
        "\n" +
        "## YouTube Playlist\n" +
        "\n" +
        "There is also a YouTube playlist introducing the usage of the extension! Run the command `/videos` to open the link.";

    private readonly Client _client;
    private readonly IList<ChatMessage> _messages;
    private readonly IDictionary<string, (string?, Func<string>, Func<(string, string)>?, string?, Action<SendMessageCommand, object?, string>?)> _commands;

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
        _client.ReinitializeService();
        _messages = [new() { User = "", Assistant = "" }];
        _commands = new Dictionary<string, (string?, Func<string>, Func<(string, string)>?, string?, Action<SendMessageCommand, object?, string>?)>()
        {
            { "service", (
                "<one-of-[ Ollama | OpenAI | AzureOpenAI | Google | Mistral ]>",
                () => $"Set the API service to call (currently: {_client.Config.Service})",
                null,
                null,
                (sender, args, opts) => {
                    Service? service = opts.ToLowerInvariant() switch
                    {
                        "ollama" => Service.Ollama,
                        "openai" => Service.OpenAI,
                        "azureopenai" => Service.AzureOpenAI,
                        "google" => Service.Google,
                        "mistral" => Service.Mistral,
                        _ => null,
                    };

                    if (service == null)
                    {
                        ErrorToast($"Invalid service '{opts}', expected one of 'Ollama', 'OpenAI', 'AzureOpenAI', 'Google', 'Mistral'");
                        return;
                    }
                    else
                    {
                        _client.Config.Service = service ?? throw new ArgumentException();
                    }

                    RefreshConfigs();
                })
            },
            { "clear", (null, () => $"Clear message history ({_messages.Count} message" + (_messages.Count > 1 ? "s" : "") + ")", null, null, (sender, args, opts) => {
                _messages.Clear();
                _messages.Add(new() { User = "", Assistant = "" });
                SearchText = "";
                RaiseItemsChanged(_messages.Count);
            }) },
            { "url", ("<url>", () => $"Set server URL (current: {_client.Config.Url})", null, null, (sender, args, opts) =>
            {
                _client.Config.Url = opts;
                RefreshConfigs();
            }) },
            { "model", ("<model-name>", () => $"Set the model to use (current: {_client.Config.Model})", null, null, (sender, args, opts) =>
            {
                _client.Config.Model = opts;
                RefreshConfigs();
            }) },
            { "apikey", ("<api-key>", () => $"Set the API key (Ollama not applicable)", null, null, (sender, args, opts) =>
            {
                _client.Config.ApiKey = opts;
                RefreshConfigs();
            }) },
            { "detail", (null, () => $"Toggle full detailed response on the side (current: {_client.Config.Details})", null, null, (sender, args, opts) =>
            {
                _client.Config.Details = !_client.Config.Details;
                RefreshConfigs();
            }) },
            { "system", (
                "<system-prompt>",
                () => $"Set the system prompt",
                () => ("Current System Prompt", _client.Config.System),
                null,
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
                null,
                (sender, args, opts) =>
                {
                    try
                    {
                        var count = int.Parse(opts);

                        if (count <= 0)
                        {
                            ErrorToast($"Invalid history count {count}, expected positive integer");
                            return;
                        }

                        _client.Config.History = count;

                        RefreshConfigs();
                    }
                    catch (FormatException) when (!_client.Config.Debug)
                    {
                        ErrorToast($"Invalid history count '{opts}', expected integer");
                        return;
                    }
                }
            ) },
            { "help", (
                null,
                () => $"Help message on usage of this extension",
                () => ("Help message", HelpMessage),
                null,
                null
            ) },
            { "videos", (
                null,
                () => $"Open the YouTube playlist of introducing the usage of this extension",
                null,
                "https://www.youtube.com/playlist?list=PLtpfYcxJV4LHu0gpKagHWjYR1Lghulnt8",
                null
            ) },
            { "debug", (null, () => $"Toggle printing of the complete exception (current: {_client.Config.Debug})", null, null, (sender, args, opts) =>
            {
                _client.Config.Debug = !_client.Config.Debug;
                RefreshConfigs();
            }) },
            { "reset", (null, () => "Reset all settings", null, null, (sender, args, opts) =>
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
                        ICommand command = c.Value.Item5 != null
                            ? SendMessageCommand.CreateCommand(c.Key, c.Value, SearchText, _client.Config.Debug)
                            : c.Value.Item3 != null
                                ? new MarkdownPage(c.Value.Item3.Invoke().Item1, c.Value.Item3.Invoke().Item2)
                                : c.Value.Item4 != null
                                    ? new OpenUrlCommand(c.Value.Item4)
                                    : new NoOpCommand();

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

            if (_client.MissingConfigs.Any())
            {
                return [new ListItem(new NoOpCommand()) {
                    Icon = new IconInfo("\u26A0"),
                    Title = $"Configuration incomplete for {_client.Config.Service}",
                    Subtitle = $"The missing configurations are: {string.Join(", ", _client.MissingConfigs)}" }];
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

                                    await foreach (var response in _client.Chat(_messages))
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
                                    ErrorToast(
                                        $"Error calling API over HTTP, is there a '{_client.Config.Service}' server running and accepting connections " +
                                        $"at '{_client.Config.Url}' with model '{_client.Config.Model}'? Or perhaps the API key is incorrect?"
                                    );
                                }
                                catch (Exception ex)
                                {
                                    ErrorToast(_client.Config.Debug ? ex.ToString() : "An error occurred when attempting to chat with LLM");
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
            ErrorToast(_client.Config.Debug ? ex.ToString() : "An unexpected error occurred");
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
        _client.ReinitializeService();
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
    internal static void ErrorToast(string message)
    {
        new ToastStatusMessage(new StatusMessage()
        {
            Message = message,
            State = MessageState.Error,
        })
        {
            Duration = 10000,
        }.Show();
    }
}

internal sealed partial class SendMessageCommand : InvokableCommand
{
    public event TypedEventHandler<SendMessageCommand, ICommandResult?>? SendMessage;
    public required bool Debug { get; set; }

    public static SendMessageCommand CreateCommand(string key, (string?, Func<string>, Func<(string, string)>?, string?, Action<SendMessageCommand, object?, string>?) value, string searchText, bool debug)
    {
        var command = new SendMessageCommand() { Debug = debug };
        command.SendMessage += (sender, args) =>
        {
            var opts = "";

            if (!searchText.StartsWith($"/{key}", StringComparison.InvariantCulture))
            {
                LlmExtensionPage.ErrorToast($"Command '{searchText}' not found");
                return;
            }

            if (value.Item1 != null)
            {
                if (searchText.StartsWith($"/{key} ", StringComparison.InvariantCulture))
                {
                    if (searchText.Length > $"/{key} ".Length)
                    {
                        opts = searchText[$"/{key} ".Length..].Trim();
                    }
                }
                else
                {
                    LlmExtensionPage.ErrorToast($"Expected argument '{value.Item1}' for command '/{key}'");
                    return;
                }
            }

            value.Item5?.Invoke(sender, args, opts);
        };

        return command;
    }

    public override ICommandResult Invoke()
    {
        CommandResult? result = null;

        try
        {
            SendMessage?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            LlmExtensionPage.ErrorToast(Debug ? ex.ToString() : "An error occurred when running the command");
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

internal sealed partial class MarkdownPage : ContentPage
{
    public string Content { get; set; }

    public MarkdownPage(string title, string content)
    {
        Title = title;
        Content = content;
    }

    public override IContent[] GetContent()
    {
        return [
            new MarkdownContent(Content),
        ];
    }
}