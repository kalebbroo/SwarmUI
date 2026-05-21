using FreneticUtilities.FreneticDataSyntax;
using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using SwarmUI.LLMs;
using SwarmUI.Utils;
using System.IO;
using System.Net.Http;
using System.Text;

namespace SwarmUI.Backends;

/// <summary>An LLM Backend that connects to the Anthropic Messages API (Claude models).</summary>
public class AnthropicLLMBackend : AbstractLLMBackend
{
    public class AnthropicLLMBackendSettings : AutoConfiguration
    {
        [ConfigComment("The default model to use.\nFor example: claude-sonnet-4-20250514")]
        public string DefaultModel = "claude-sonnet-4-20250514";

        [ConfigComment("Maximum timeout in seconds for API requests.")]
        public int TimeoutSeconds = 120;

        [ConfigComment("The API base URL.\nOnly change this if using a proxy or compatible endpoint.")]
        public string BaseUrl = "https://api.anthropic.com";
    }

    /// <summary>Shared HTTP client for Anthropic API requests.</summary>
    public static HttpClient HttpClient = NetworkBackendUtils.MakeHttpClient();

    /// <summary>The settings for this backend.</summary>
    public AnthropicLLMBackendSettings Settings => SettingsRaw as AnthropicLLMBackendSettings;

    /// <summary>Returns the per-user Anthropic API key from the user's saved keys, or null if not set.</summary>
    public string GetApiKey(LLMParamInput input)
    {
        if (input?.RequestSession is not null)
        {
            return input.RequestSession.User.GetGenericData("anthropic_api", "key");
        }
        return null;
    }

    /// <inheritdoc/>
    public override async Task Init()
    {
        Status = BackendStatus.RUNNING;
    }

    /// <inheritdoc/>
    public override async Task Shutdown()
    {
        Status = BackendStatus.DISABLED;
    }

    /// <summary>Builds an Anthropic Messages API request body from the given LLM input.</summary>
    public JObject BuildRequestBody(LLMParamInput input, bool stream)
    {
        string model = !string.IsNullOrEmpty(input.Model) ? input.Model : Settings.DefaultModel;
        JArray messages = [];
        foreach (LLMMessage msg in input.Messages)
        {
            if (msg.Role == LLMRoles.System)
            {
                continue;
            }
            messages.Add(new JObject() { ["role"] = msg.Role, ["content"] = BuildAnthropicContent(msg) });
        }
        if (messages.Count == 0 && !string.IsNullOrEmpty(input.UserMessage))
        {
            messages.Add(new JObject() { ["role"] = LLMRoles.User, ["content"] = input.UserMessage });
        }
        JObject body = new()
        {
            ["model"] = model,
            ["max_tokens"] = input.MaxTokens,
            ["messages"] = messages,
            ["stream"] = stream
        };
        string systemPrompt = input.SystemPrompt;
        if (string.IsNullOrEmpty(systemPrompt))
        {
            foreach (LLMMessage msg in input.Messages)
            {
                if (msg.Role == LLMRoles.System)
                {
                    systemPrompt = msg.Content;
                    break;
                }
            }
        }
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            body["system"] = systemPrompt;
        }
        if (input.Temperature >= 0)
        {
            body["temperature"] = input.Temperature;
        }
        if (input.TopP >= 0 && input.TopP < 1.0)
        {
            body["top_p"] = input.TopP;
        }
        return body;
    }

    /// <summary>Builds the Anthropic-shaped <c>content</c> field for one message. Returns a plain
    /// string when there's no media (the simpler form Anthropic accepts), or an array of content
    /// blocks (text + image) when the message has attachments.
    /// <para>Image blocks use Anthropic's two source forms: <c>{type: "url"}</c> when the
    /// attachment is an HTTPS URL Anthropic can fetch directly, or <c>{type: "base64"}</c>
    /// otherwise. Local <c>Output/...</c> URLs must already have been resolved to base64 by the
    /// caller — this method does not touch the filesystem.</para></summary>
    private static JToken BuildAnthropicContent(LLMMessage msg)
    {
        if (msg.Media is null || msg.Media.Count == 0)
        {
            return msg.Content ?? "";
        }
        JArray blocks = [];
        if (!string.IsNullOrEmpty(msg.Content))
        {
            blocks.Add(new JObject { ["type"] = "text", ["text"] = msg.Content });
        }
        foreach (LLMMediaAttachment att in msg.Media)
        {
            if (att is null || string.IsNullOrEmpty(att.Data))
            {
                continue;
            }
            JObject source;
            bool isHttpUrl = att.Type == "url" && (att.Data.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || att.Data.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            if (isHttpUrl)
            {
                source = new JObject { ["type"] = "url", ["url"] = att.Data };
            }
            else
            {
                source = new JObject
                {
                    ["type"] = "base64",
                    ["media_type"] = string.IsNullOrEmpty(att.MediaType) ? "image/png" : att.MediaType,
                    ["data"] = att.Data
                };
            }
            blocks.Add(new JObject { ["type"] = "image", ["source"] = source });
        }
        return blocks;
    }

    /// <inheritdoc/>
    public override async Task<string> Generate(LLMParamInput user_input)
    {
        StringBuilder output = new();
        await GenerateLive(user_input, "0", j =>
        {
            if (j.TryGetValue("chunk", out JToken chunk))
            {
                output.Append($"{chunk}");
            }
            else if (j.TryGetValue("result", out JToken result))
            {
                output.Append($"{result}");
            }
        });
        return output.ToString();
    }

    /// <inheritdoc/>
    public override async Task GenerateLive(LLMParamInput user_input, string batchId, Action<JObject> takeOutput)
    {
        string apiKey = GetApiKey(user_input);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new SwarmReadableErrorException("No Anthropic API key set. Go to the User tab to configure your Anthropic API key.");
        }
        string baseUrl = Settings.BaseUrl.TrimEnd('/');
        JObject body = BuildRequestBody(user_input, true);
        HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = Utilities.JSONContent(body)
        };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(Settings.TimeoutSeconds));
        HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync();
            throw new SwarmReadableErrorException($"Anthropic API returned error {response.StatusCode}: {errorBody}");
        }
        using Stream stream = await response.Content.ReadAsStreamAsync();
        using StreamReader reader = new(stream, Encoding.UTF8);
        string currentEvent = "";
        while (true)
        {
            string line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                currentEvent = "";
                continue;
            }
            if (line.StartsWith("event: "))
            {
                currentEvent = line["event: ".Length..];
                if (currentEvent == "message_stop")
                {
                    break;
                }
                continue;
            }
            if (!line.StartsWith("data: "))
            {
                continue;
            }
            string data = line["data: ".Length..];
            try
            {
                JObject parsed = JObject.Parse(data);
                if (currentEvent == "content_block_delta")
                {
                    JObject delta = parsed.Value<JObject>("delta");
                    string text = delta?.Value<string>("text");
                    if (!string.IsNullOrEmpty(text))
                    {
                        takeOutput(new JObject() { ["chunk"] = text });
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.Debug($"[AnthropicLLMBackend] Failed to parse SSE chunk: {ex.Message}");
            }
        }
    }

    /// <summary>Well-known Anthropic model definitions. Updated as new models release.</summary>
    public static readonly List<(string Id, string Name, int ContextLength)> KnownModels =
    [
        ("claude-opus-4-20250514", "Claude Opus 4", 200000),
        ("claude-sonnet-4-20250514", "Claude Sonnet 4", 200000),
        ("claude-haiku-4-20250514", "Claude Haiku 4", 200000),
        ("claude-3-5-sonnet-20241022", "Claude 3.5 Sonnet", 200000),
        ("claude-3-5-haiku-20241022", "Claude 3.5 Haiku", 200000),
        ("claude-3-opus-20240229", "Claude 3 Opus", 200000),
        ("claude-3-haiku-20240307", "Claude 3 Haiku", 200000)
    ];

    /// <inheritdoc/>
    public override async Task<List<LLMModelInfo>> ListModels()
    {
        List<LLMModelInfo> models = [];
        foreach ((string id, string name, int contextLength) in KnownModels)
        {
            models.Add(new LLMModelInfo()
            {
                Id = id,
                Name = name,
                Provider = "anthropic",
                BackendId = AbstractBackendData?.ID ?? -1,
                ContextLength = contextLength,
                Family = "claude",
                IsLoaded = true
            });
        }
        return models;
    }

    /// <inheritdoc/>
    public override IEnumerable<string> SupportedFeatures => ["llm", "remote_llm", "anthropic"];

    /// <inheritdoc/>
    public override async Task<bool> FreeMemory(bool systemRam)
    {
        return false;
    }
}
