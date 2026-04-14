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
using Microsoft.SemanticKernel.Connectors.MistralAI;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel;
using System.Collections;

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
        public int MaxTokens { get; set; } = 4096;
        public float Temperature { get; set; } = 0.8f;
        public float TopP { get; set; } = 1.0f;
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

            // Build history
            ChatHistory history = [];
            history.AddSystemMessage(Config.System);
            var relevantMessages = messages.Take(Config.History).Reverse();
            foreach (var message in relevantMessages)
            {
                if (!string.IsNullOrEmpty(message.User))
                {
                    history.AddUserMessage(message.User);
                }

                if (!string.IsNullOrEmpty(message.Assistant))
                {
                    history.AddAssistantMessage(message.Assistant);
                }
            }

            // Build execution settings
#pragma warning disable SKEXP0070
            PromptExecutionSettings settings = Config.Service switch
            {
                LlmExtensionPage.Service.Ollama => new OllamaPromptExecutionSettings()
                {
                    Temperature = Config.Temperature,
                    TopP = Config.TopP,
                },
                LlmExtensionPage.Service.OpenAI => new OpenAIPromptExecutionSettings()
                {
                    MaxTokens = Config.MaxTokens,
                    Temperature = Config.Temperature,
                    TopP = Config.TopP,
                },
                LlmExtensionPage.Service.AzureOpenAI => new AzureOpenAIPromptExecutionSettings()
                {
                    MaxTokens = Config.MaxTokens,
                    Temperature = Config.Temperature,
                    TopP = Config.TopP,
                },
                LlmExtensionPage.Service.Google => new GeminiPromptExecutionSettings()
                {
                    MaxTokens = Config.MaxTokens,
                    Temperature = Config.Temperature,
                    TopP = Config.TopP,
                },
                LlmExtensionPage.Service.Mistral => new MistralAIPromptExecutionSettings()
                {
                    MaxTokens = Config.MaxTokens,
                    Temperature = Config.Temperature,
                    TopP = Config.TopP,
                },
            };

            await foreach (var content in Service.GetStreamingChatMessageContentsAsync(history, settings))
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

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
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
    private readonly IDictionary<string, SlashCommand> _commands;

    private (string, ListItem)[] _commandsMemo;
    private ListItem[] _messagesMemo;

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
        _commands = new Dictionary<string, SlashCommand>()
        {
            { "service", new(
                () => $"Set the API service to call (currently: {_client.Config.Service})",
                "<one-of-[ Ollama | OpenAI | AzureOpenAI | Google | Mistral ]>",
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
                },
                "Service configured")
            },
            { "clear", new(
                () => $"Clear message history ({_messages.Count} message" + (_messages.Count > 1 ? "s" : "") + ")",
                Action: (sender, args, opts) => {
                    _messages.Clear();
                    _messages.Add(new() { User = "", Assistant = "" });
                    RaiseItemsChanged();
                },
                ActionToastText: "Message cleared")
            },
            { "url", new(
                () => $"Set server URL (current: {_client.Config.Url})",
                "<url>",
                (sender, args, opts) =>
                {
                    _client.Config.Url = opts;
                    RefreshConfigs();
                },
                "Server URL configured")
            },
            { "model", new(
                () => $"Set the model to use (current: {_client.Config.Model})",
                "<model-name>",
                (sender, args, opts) =>
                {
                    _client.Config.Model = opts;
                    RefreshConfigs();
                },
                "Model configured")
            },
            { "apikey", new(
                () => $"Set the API key (Ollama not applicable)",
                "<api-key>",
                (sender, args, opts) =>
                {
                    _client.Config.ApiKey = opts;
                    RefreshConfigs();
                },
                "API key configured")
            },
            { "detail", new(
                () => $"Toggle full detailed response on the side (current: {_client.Config.Details})",
                Action: (sender, args, opts) =>
                {
                    _client.Config.Details = !_client.Config.Details;
                    RefreshConfigs();
                },
                ActionToastText: "Detailed response toggled")
            },
            { "system", new(
                () => $"Set the system prompt",
                "<system-prompt>",
                (sender, args, opts) =>
                {
                    _client.Config.System = opts;
                    RefreshConfigs();
                },
                "System prompt configured",
                () => ("Current System Prompt", _client.Config.System)
            ) },
            { "history", new(
                () => $"Set the message history count (current: {_client.Config.History})",
                "<history-count>",
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
                },
                "Message history count configured")
            },
            { "maxtokens", new(
                () => $"Set the maximum token count (Ollama not applicable, current: {_client.Config.MaxTokens})",
                "<token-count>",
                (sender, args, opts) =>
                {
                    try
                    {
                        var count = int.Parse(opts);

                        if (count <= 0)
                        {
                            ErrorToast($"Invalid token count {count}, expected positive integer");
                            return;
                        }

                        _client.Config.MaxTokens = count;

                        RefreshConfigs();
                    }
                    catch (FormatException) when (!_client.Config.Debug)
                    {
                        ErrorToast($"Invalid token count '{opts}', expected integer");
                        return;
                    }
                },
                "Maximum token count configured")
            },
            { "temperature", new(
                () => $"Set the model temperature, indicating creativeness (current: {_client.Config.Temperature})",
                "<temperature-in-range-0.0-to-1.0>",
                (sender, args, opts) =>
                {
                    try
                    {
                        var value = float.Parse(opts);

                        if (value < 0.0 || value > 1.0)
                        {
                            ErrorToast($"Invalid temperature {value}, expected floating point number between 0.0 and 1.0");
                            return;
                        }

                        _client.Config.Temperature = value;

                        RefreshConfigs();
                    }
                    catch (FormatException) when (!_client.Config.Debug)
                    {
                        ErrorToast($"Invalid temperature '{opts}', expected floating point number");
                        return;
                    }
                },
                "Temperature configured")
            },
            { "topp", new(
                () => $"Set the model top P, indicating randomness (current: {_client.Config.TopP})",
                "<top-p-in-range-0.0-to-1.0>",
                (sender, args, opts) =>
                {
                    try
                    {
                        var value = float.Parse(opts);

                        if (value < 0.0 || value > 1.0)
                        {
                            ErrorToast($"Invalid top P {value}, expected floating point number between 0.0 and 1.0");
                            return;
                        }

                        _client.Config.TopP = value;

                        RefreshConfigs();
                    }
                    catch (FormatException) when (!_client.Config.Debug)
                    {
                        ErrorToast($"Invalid top P '{opts}', expected floating point number");
                        return;
                    }
                },
                "Top P configured")
            },
            { "help", new(
                () => $"Help message on usage of this extension",
                Details: () => ("Help message", HelpMessage)
            ) },
            { "videos", new(
                () => $"Open the YouTube playlist of introducing the usage of this extension",
                Url: "https://www.youtube.com/playlist?list=PLtpfYcxJV4LHu0gpKagHWjYR1Lghulnt8"
            ) },
            { "config", new(
                () => $"Open the folder containing the configuration file",
                Url: Environment.ExpandEnvironmentVariables("%USERPROFILE%\\.config\\LlmExtensionForCmdPal")
            ) },
            { "debug", new(
                () => $"Toggle printing of the complete exception (current: {_client.Config.Debug})",
                Action: (sender, args, opts) =>
                {
                    _client.Config.Debug = !_client.Config.Debug;
                    RefreshConfigs();
                },
                ActionToastText: "Printing of complete exception configured toggled")
            },
            { "reset", new(
                () => "Reset all settings",
                Action: (sender, args, opts) =>
                {
                    _client.Config = new Config();
                    RefreshConfigs();
                },
                ActionToastText: "All settings reset")
            },
        };

        UpdateCommandsMemo();
        UpdateMessagesMemo();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (string.IsNullOrEmpty(oldSearch) != string.IsNullOrEmpty(newSearch) || oldSearch.StartsWith('/') != newSearch.StartsWith('/'))
        {
            if (!IsLoading)
            {
                _messages[0].User = newSearch;
            }
            UpdateMessagesMemo();
            RaiseItemsChanged();
        }
        else if (newSearch.StartsWith('/'))
        {
            RaiseItemsChanged();
        }
    }

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

                return _commandsMemo
                    .OrderByDescending(c => c.Item1.Zip(commandText).TakeWhile((pair) => pair.First == pair.Second).Count())
                    .Select(c => // Shallow copy so that item selection/highlight won't stick to the previous first item
                    new ListItem(c.Item2)
                    {
                        Title = c.Item2.Title,
                        Subtitle = c.Item2.Subtitle,
                        Details = c.Item2.Details,
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

            return _messagesMemo;
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

        var json = JsonSerializer.Serialize(_client.Config, JsonOptions);
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
            var json = JsonSerializer.Serialize(defaultConfig, JsonOptions);
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
            var json = JsonSerializer.Serialize(defaultConfig, JsonOptions);
            File.WriteAllText(path, json);
            return defaultConfig;
        }
    }

    private void RefreshConfigs()
    {
        _client.ReinitializeService();
        SaveConfig();
        SearchText = "";
        UpdateCommandsMemo();
        UpdateMessagesMemo();
        RaiseItemsChanged();
    }

    private void UpdateCommandsMemo()
    {
        _commandsMemo = _commands.Select(c =>
        {
            ICommand command = c.Value.Action != null
                ? SendMessageCommand.CreateCommand(c.Key, c.Value, () => SearchText, _client.Config.Debug)
                : c.Value.Details != null
                    ? new MarkdownPage(c.Value.Details.Invoke().Item1, c.Value.Details.Invoke().Item2)
                    : c.Value.Url != null
                        ? new OpenUrlCommand(c.Value.Url)
                        : new NoOpCommand();

            if (c.Value.Action != null)
            {
                ((SendMessageCommand)command).SendMessage += (_, _) =>
                {
                    SearchText = "";
                    OnPropertyChanged(nameof(SearchText));
                };
            }

            var item = new ListItem(command)
            {
                Title = $"/{c.Key}",
                Subtitle = c.Value.Description.Invoke()
            };

            if (c.Value.Param != null)
            {
                item.Title += $" {c.Value.Param}";
            }

            if (c.Value.Details != null)
            {
                var details = c.Value.Details.Invoke();
                item.Details = new Details()
                {
                    Title = details.Item1,
                    Body = details.Item2,
                };
            }

            return (c.Key, item);
        })
        .ToArray();
    }

    private void UpdateMessagesMemo()
    {
        _messagesMemo = _messages.SelectMany<ChatMessage, ListItem>((m, index) =>
        {
            if (string.IsNullOrEmpty(m.Assistant))
            {
                if (string.IsNullOrEmpty(m.User))
                {
                    return [];
                }
                else if (index == 0)
                {
                    var command = new SendMessageCommand() { Debug = _client.Config.Debug };
                    if (!IsLoading)
                    {
                        command.SendMessage += async (sender, args) =>
                        {
                            if (IsLoading) return;
                            try
                            {
                                _messages[0].User = SearchText;
                                IsLoading = true;

                                SearchText = "";
                                OnPropertyChanged(nameof(SearchText));

                                UpdateMessagesMemo();
                                RaiseItemsChanged();

                                await foreach (var response in _client.Chat(_messages))
                                {
                                    m.Assistant += response;
                                    UpdateMessagesMemo();
                                    RaiseItemsChanged();
                                }

                                SearchText = "";
                                _messages.Insert(0, new() { User = "", Assistant = "" });
                                UpdateMessagesMemo();
                                RaiseItemsChanged();
                            }
                            catch (Exception ex) when (!_client.Config.Debug && (ex is HttpRequestException || ex is ClientResultException))
                            {
                                ErrorToast(
                                    $"Error calling API over HTTP, is there a '{_client.Config.Service}' server running and accepting connections " +
                                    $"at '{_client.Config.Url}' with model '{_client.Config.Model}'? Or perhaps the API key is incorrect?"
                                );
                            }
                            catch (HttpOperationException ex) when (_client.Config.Debug)
                            {
                                var dataString = "";
                                foreach (DictionaryEntry item in ex.Data)
                                {
                                    dataString += $"{item.Key}: {item.Value}\n";
                                }

                                if (!string.IsNullOrEmpty(dataString))
                                {
                                    dataString = "\n" + dataString;
                                }

                                ErrorToast($"An HTTP error occurred: {ex.Message} with inner exception {ex.InnerException}{dataString}");
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

                    return [new ListItem(command) { Title = "Press enter to send" }];
                }
                else
                {
                    return [new ListItem(new DetailedResponsePage(m.User, "No response received.")) {
                        Title = m.User,
                        Subtitle = "No response received."
                    }];
                }
            }
            else
            {
                var item = new ListItem(new DetailedResponsePage(m.User, m.Assistant))
                {
                    Title = m.User,
                    Subtitle = m.Assistant,
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

internal record SlashCommand(
    Func<string> Description,
    string? Param = null,
    Action<SendMessageCommand, object?, string>? Action = null,
    string? ActionToastText = null,
    Func<(string, string)>? Details = null,
    string? Url = null
);

internal sealed partial class SendMessageCommand : InvokableCommand
{
    public event TypedEventHandler<SendMessageCommand, ICommandResult?>? SendMessage;
    public required bool Debug { get; set; }
    public string? ToastText { get; set; }

    public static SendMessageCommand CreateCommand(
        string key,
        SlashCommand value,
        Func<string> searchTextFunc,
        bool debug)
    {
        var command = new SendMessageCommand() { Debug = debug };
        command.ToastText = value.ActionToastText;
        command.SendMessage += (sender, args) =>
        {
            var searchText = searchTextFunc();
            var opts = "";

            if (!searchText.StartsWith($"/{key}", StringComparison.InvariantCulture))
            {
                LlmExtensionPage.ErrorToast($"Command '{searchText}' not found");
                return;
            }

            if (value.Param != null)
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
                    LlmExtensionPage.ErrorToast($"Expected argument '{value.Param}' for command '/{key}'");
                    return;
                }
            }

            value.Action?.Invoke(sender, args, opts);
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

        return ToastText != null
            ? CommandResult.ShowToast(new ToastArgs { Message = ToastText, Result = CommandResult.KeepOpen() })
            : CommandResult.KeepOpen();
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