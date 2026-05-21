using System.Net.WebSockets;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Core;
using SwarmUI.LLMs;
using SwarmUI.Utils;

namespace SwarmUI.WebAPI;

[API.APIClass("API routes for LLM Text Generation and directly related features.")]
public abstract class LLMAPI
{
    public static void Register()
    {
        API.RegisterAPICall(GenerateLLMText, true, Permissions.BasicTextGeneration);
        API.RegisterAPICall(GenerateLLMTextWS, true, Permissions.BasicTextGeneration);
        API.RegisterAPICall(ListLLMModels, false, Permissions.BasicTextGeneration);
    }

    /// <summary>Parses an LLM generation request from raw JSON input.</summary>
    public static LLMParamInput ParseInput(JObject rawInput, Session session)
    {
        LLMParamInput input = new()
        {
            Model = rawInput.Value<string>("model") ?? "",
            SystemPrompt = rawInput.Value<string>("system_prompt") ?? "",
            Temperature = rawInput.Value<double?>("temperature") ?? 1.0,
            MaxTokens = rawInput.Value<int?>("max_tokens") ?? 1024,
            TopP = rawInput.Value<double?>("top_p") ?? 0.9,
            Seed = rawInput.Value<long?>("seed") ?? -1,
            Stream = rawInput.Value<bool?>("stream") ?? true,
            RequestSession = session
        };
        JArray messages = rawInput.Value<JArray>("messages");
        if (messages is not null)
        {
            foreach (JObject msg in messages.OfType<JObject>())
            {
                input.Messages.Add(new LLMMessage()
                {
                    Role = msg.Value<string>("role") ?? LLMRoles.User,
                    Content = msg.Value<string>("content") ?? ""
                });
            }
        }
        if (input.Messages.Count > 0)
        {
            input.UserMessage = input.Messages[^1].Content;
        }
        else
        {
            string prompt = rawInput.Value<string>("prompt") ?? "";
            input.UserMessage = prompt;
            if (!string.IsNullOrEmpty(prompt))
            {
                input.Messages.Add(new LLMMessage() { Role = LLMRoles.User, Content = prompt });
            }
        }
        return input;
    }

    /// <summary>Finds the best running LLM backend for the given model, or throws a readable error.
    /// When multiple backends are running and a model is specified, queries each backend's
    /// <see cref="AbstractLLMBackend.ListModels"/> to find which one owns it.</summary>
    public static async Task<AbstractLLMBackend> GetBackend(string model = null)
    {
        List<AbstractLLMBackend> backends = Program.Backends.RunningBackendsOfType<AbstractLLMBackend>().ToList();
        if (backends.Count == 0)
        {
            throw new SwarmReadableErrorException("No LLM backend is currently running. Please configure and enable an LLM backend first.");
        }
        if (backends.Count == 1 || string.IsNullOrEmpty(model))
        {
            return backends[0];
        }
        foreach (AbstractLLMBackend backend in backends)
        {
            try
            {
                List<LLMModelInfo> models = await backend.ListModels();
                if (models.Any(m => m.Id == model))
                {
                    return backend;
                }
            }
            catch
            {
                // Skip backends that fail to list models
            }
        }
        return backends[0];
    }

    [API.APIDescription("Generate text from an LLM.",
        """
            "result": "Wow an LLM wrote this wee"
        """)]
    public static async Task<JObject> GenerateLLMText(Session session,
        [API.APIParameter("Raw JSON input containing 'model', 'messages' (array of {role, content}), 'system_prompt', 'temperature', 'max_tokens', 'top_p', 'seed'. Alternatively use 'prompt' as a simple string input.")] JObject rawInput)
    {
        LLMParamInput input = ParseInput(rawInput, session);
        AbstractLLMBackend backend = await GetBackend(input.Model);
        string result = await backend.Generate(input);
        return new JObject() { ["result"] = result };
    }

    [API.APIDescription("Generate text from an LLM with streaming via WebSocket.",
        """
            // Chunks as they stream in (direct concat the text from each chunk)
            "chunk": "Wow"
            // Final complete response
            "result": "Wow an LLM wrote this wee"
        """)]
    public static async Task<JObject> GenerateLLMTextWS(WebSocket socket, Session session,
        [API.APIParameter("Raw JSON input containing 'model', 'messages' (array of {role, content}), 'system_prompt', 'temperature', 'max_tokens', 'top_p', 'seed'. Alternatively use 'prompt' as a simple string input.")] JObject rawInput)
    {
        LLMParamInput input = ParseInput(rawInput, session);
        AbstractLLMBackend backend = await GetBackend(input.Model);
        StringBuilder fullResult = new();
        await backend.GenerateLive(input, "0", outputChunk =>
        {
            if (outputChunk.TryGetValue("chunk", out JToken chunkText))
            {
                fullResult.Append($"{chunkText}");
            }
            else if (outputChunk.TryGetValue("result", out JToken resultText))
            {
                fullResult.Append($"{resultText}");
            }
            socket.SendJson(outputChunk, API.WebsocketTimeout).Wait();
        });
        return new JObject() { ["result"] = fullResult.ToString() };
    }

    [API.APIDescription("Lists all available LLM models across all running LLM backends.",
        """
            "models": [
                {
                    "id": "llama3.2",
                    "name": "llama3.2",
                    "provider": "openai_api",
                    "backend_id": 1,
                    "size_bytes": 2000000000,
                    "context_length": 8192,
                    "family": "llama",
                    "quantization": "Q4_K_M",
                    "is_loaded": true,
                    "metadata": {}
                }
            ]
        """)]
    public static async Task<JObject> ListLLMModels(Session session)
    {
        List<AbstractLLMBackend> backends = Program.Backends.RunningBackendsOfType<AbstractLLMBackend>().ToList();
        List<Task<List<LLMModelInfo>>> tasks = [];
        foreach (AbstractLLMBackend backend in backends)
        {
            tasks.Add(backend.ListModels());
        }
        List<LLMModelInfo>[] results = await Task.WhenAll(tasks);
        JArray modelsArray = [];
        foreach (List<LLMModelInfo> backendModels in results)
        {
            foreach (LLMModelInfo model in backendModels)
            {
                modelsArray.Add(ModelInfoToJson(model));
            }
        }
        return new JObject() { ["models"] = modelsArray };
    }

    /// <summary>Converts an <see cref="LLMModelInfo"/> to a JSON object.</summary>
    public static JObject ModelInfoToJson(LLMModelInfo model)
    {
        JObject obj = new()
        {
            ["id"] = model.Id,
            ["name"] = model.Name ?? model.Id,
            ["provider"] = model.Provider ?? "",
            ["backend_id"] = model.BackendId,
            ["is_loaded"] = model.IsLoaded
        };
        if (model.SizeBytes >= 0)
        {
            obj["size_bytes"] = model.SizeBytes;
        }
        if (model.ContextLength >= 0)
        {
            obj["context_length"] = model.ContextLength;
        }
        if (!string.IsNullOrEmpty(model.Family))
        {
            obj["family"] = model.Family;
        }
        if (!string.IsNullOrEmpty(model.Quantization))
        {
            obj["quantization"] = model.Quantization;
        }
        if (model.Metadata.Count > 0)
        {
            JObject meta = new();
            foreach (KeyValuePair<string, string> kvp in model.Metadata)
            {
                meta[kvp.Key] = kvp.Value;
            }
            obj["metadata"] = meta;
        }
        return obj;
    }
}
