using SwarmUI.Accounts;

namespace SwarmUI.LLMs;

/// <summary>Role constants for LLM chat messages.</summary>
public static class LLMRoles
{
    /// <summary>System instruction role.</summary>
    public const string System = "system";

    /// <summary>User message role.</summary>
    public const string User = "user";

    /// <summary>Assistant response role.</summary>
    public const string Assistant = "assistant";
}

/// <summary>A single message in an LLM conversation.</summary>
public class LLMMessage
{
    /// <summary>The role of the message author (system, user, assistant).</summary>
    public string Role;

    /// <summary>The text content of the message.</summary>
    public string Content;

    /// <summary>Optional inline media attached to this message (eg images for vision models).
    /// Backends that don't support multimodal input should ignore this field.</summary>
    public List<LLMMediaAttachment> Media;
}

/// <summary>A media attachment on an <see cref="LLMMessage"/>. Today only images are supported,
/// but the type field leaves room for video/audio/etc. as backends add support.</summary>
public class LLMMediaAttachment
{
    /// <summary>How <see cref="Data"/> is encoded: <c>"base64"</c> (raw base64 string, no data
    /// URI prefix) or <c>"url"</c> (HTTPS URL the model can fetch directly).</summary>
    public string Type;

    /// <summary>Either the base64 payload (no data URI prefix) or an HTTPS URL — see <see cref="Type"/>.</summary>
    public string Data;

    /// <summary>MIME type, eg <c>"image/png"</c>. Required for base64; optional for URLs.</summary>
    public string MediaType;
}

/// <summary>Metadata about an available LLM model.</summary>
public class LLMModelInfo
{
    /// <summary>The model ID used when making requests (e.g. "llama3.2", "claude-sonnet-4-20250514").</summary>
    public string Id;

    /// <summary>A human-readable display name for the model, if available.</summary>
    public string Name;

    /// <summary>The backend type that provides this model (e.g. "ollama", "anthropic", "llamasharp").</summary>
    public string Provider;

    /// <summary>The backend ID that provides this model.</summary>
    public int BackendId;

    /// <summary>Size of the model in bytes, if known. -1 if unknown.</summary>
    public long SizeBytes = -1;

    /// <summary>Maximum context length in tokens, if known. -1 if unknown.</summary>
    public int ContextLength = -1;

    /// <summary>The model family or architecture (e.g. "llama", "qwen", "claude"), if known.</summary>
    public string Family;

    /// <summary>The quantization level (e.g. "Q4_K_M", "Q8_0"), if applicable.</summary>
    public string Quantization;

    /// <summary>Whether the model is currently loaded and ready for inference.</summary>
    public bool IsLoaded;

    /// <summary>Any additional provider-specific metadata.</summary>
    public Dictionary<string, string> Metadata = [];
}

/// <summary>Inputs for a request to an LLM.</summary>
public class LLMParamInput
{
    /// <summary>The conversation history as a list of messages.</summary>
    public List<LLMMessage> Messages = [];

    /// <summary>The current user message text (convenience shortcut).</summary>
    public string UserMessage;

    /// <summary>Which model to use (name, path, or API model ID).</summary>
    public string Model;

    /// <summary>System prompt to prepend to the conversation.</summary>
    public string SystemPrompt;

    /// <summary>Controls randomness. 0.0 = deterministic, 2.0 = very creative.</summary>
    public double Temperature = 1.0;

    /// <summary>Maximum number of tokens to generate.</summary>
    public int MaxTokens = 1024;

    /// <summary>Nucleus sampling cutoff (0.0-1.0).</summary>
    public double TopP = 0.9;

    /// <summary>Random seed. -1 = random.</summary>
    public long Seed = -1;

    /// <summary>Whether to stream the response token-by-token.</summary>
    public bool Stream = true;

    /// <summary>The session making the request, if available. Used for per-user API key lookups.</summary>
    public Session RequestSession;
}
