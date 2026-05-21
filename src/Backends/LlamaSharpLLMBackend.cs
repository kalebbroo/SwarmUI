using FreneticUtilities.FreneticDataSyntax;
using LLama;
using LLama.Common;
using Newtonsoft.Json.Linq;
using SwarmUI.LLMs;

namespace SwarmUI.Backends;

/// <summary>An LLM Backend powered by local LlamaSharp (Llama.Cpp).</summary>
public class LlamaSharpLLMBackend : AbstractLLMBackend
{
    public class LlamaSharpLLMBackendSettings : AutoConfiguration
    {
        [ConfigComment("(PLACEHOLDER, BAD USER CONTROL APPROACH)\nHow many LLM layers to load to the GPU.")]
        public int GPULoadLayers = 0;

        [ConfigComment("If enabled, the LLM is only loaded while generation requests are going, and unloaded immediately when empty.\nIf false, the model stays loaded in the background even when not in use.")]
        public bool AlwaysFreeMemory = false;

        [ConfigComment("Context size for the loaded model.\nHigher values use more memory but allow longer conversations.")]
        public int ContextSize = 4096;
    }

    /// <summary>The loaded LLamaSharp model weights.</summary>
    public LLamaWeights LoadedModel = null;

    /// <summary>The loaded LLamaSharp context.</summary>
    public LLamaContext LoadedContext = null;

    /// <summary>The loaded LLamaSharp interactive executor.</summary>
    public InteractiveExecutor LoadedExecutor = null;

    /// <summary>The name of the currently loaded model.</summary>
    public string LoadedModelName = null;

    /// <summary>The settings for this backend.</summary>
    public LlamaSharpLLMBackendSettings Settings => SettingsRaw as LlamaSharpLLMBackendSettings;

    /// <inheritdoc/>
    public override async Task Init()
    {
        // Nothing to do until a request comes, we're operating directly in local C#!
    }

    /// <inheritdoc/>
    public override async Task Shutdown()
    {
        Unload();
    }

    /// <summary>Unloads the current model and frees resources.</summary>
    public void Unload()
    {
        LoadedExecutor = null;
        LoadedContext?.Dispose();
        LoadedContext = null;
        LoadedModel?.Dispose();
        LoadedModel = null;
        LoadedModelName = null;
    }

    /// <summary>Loads a model if not already loaded, or reloads if the model name changed.</summary>
    public async Task Load(LLMParamInput user_input)
    {
        if (LoadedModel is not null && LoadedModelName == user_input.Model)
        {
            return;
        }
        if (LoadedModel is not null)
        {
            Unload();
        }
        ModelParams mParam = new(user_input.Model)
        {
            ContextSize = (uint)Settings.ContextSize,
            GpuLayerCount = Settings.GPULoadLayers // TODO: Per-model
            // TODO: other config?
        };
        LoadedModel = await LLamaWeights.LoadFromFileAsync(mParam);
        LoadedContext = LoadedModel.CreateContext(mParam);
        LoadedExecutor = new(LoadedContext);
    }

    /// <summary>Converts SwarmUI LLMMessages to LLamaSharp ChatHistory.</summary>
    public static ChatHistory ConvertToChatHistory(LLMParamInput input)
    {
        ChatHistory history = new();
        if (!string.IsNullOrEmpty(input.SystemPrompt))
        {
            history.AddMessage(AuthorRole.System, input.SystemPrompt);
        }
        foreach (LLMMessage msg in input.Messages)
        {
            AuthorRole role = msg.Role switch
            {
                LLMRoles.System => AuthorRole.System,
                LLMRoles.Assistant => AuthorRole.Assistant,
                _ => AuthorRole.User
            };
            history.AddMessage(role, msg.Content);
        }
        return history;
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
        });
        return output.ToString();
    }

    /// <inheritdoc/>
    public override async Task GenerateLive(LLMParamInput user_input, string batchId, Action<JObject> takeOutput)
    {
        await Load(user_input);
        ChatHistory history = ConvertToChatHistory(user_input);
        ChatSession session = await ChatSession.InitializeSessionFromHistoryAsync(LoadedExecutor, history);
        await foreach (string chunk in session.ChatAsync(new ChatHistory.Message(AuthorRole.User, user_input.UserMessage)))
        {
            takeOutput(new() { ["chunk"] = chunk });
        }
        if (Settings.AlwaysFreeMemory)
        {
            Unload();
        }
    }

    /// <inheritdoc/>
    public override async Task<List<LLMModelInfo>> ListModels()
    {
        List<LLMModelInfo> models = [];
        if (LoadedModel is not null && !string.IsNullOrEmpty(LoadedModelName))
        {
            LLMModelInfo info = new()
            {
                Id = LoadedModelName,
                Name = System.IO.Path.GetFileNameWithoutExtension(LoadedModelName),
                Provider = "llamasharp",
                BackendId = AbstractBackendData?.ID ?? -1,
                ContextLength = Settings.ContextSize,
                IsLoaded = true
            };
            models.Add(info);
        }
        return models;
    }

    /// <inheritdoc/>
    public override IEnumerable<string> SupportedFeatures => ["llm", "local_llm"];

    /// <inheritdoc/>
    public override async Task<bool> FreeMemory(bool systemRam)
    {
        if (systemRam || Settings.GPULoadLayers > 0)
        {
            Unload();
            return true;
        }
        return false;
    }
}
