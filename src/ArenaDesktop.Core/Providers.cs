using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArenaDesktop.Core;

/// <summary>
/// Bring-your-own-key providers (completeness slice B1/B2). Arena has
/// no API; these are the user's own accounts with OpenAI, Anthropic
/// and Google, plus local OpenAI-compatible servers (Ollama, LM
/// Studio). Each provider keeps its real API shape — request building
/// and response/stream parsing here are pure functions so they can be
/// tested against recorded-shape payloads with no live network.
/// </summary>
public enum ChatProviderKind
{
    OpenAI,
    Anthropic,
    Google,
    Ollama,
    LmStudio,
    OpenAiCompatible,
}

/// <summary>One configured provider instance (the user may add several of a kind).</summary>
public sealed class ChatProviderConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public ChatProviderKind Kind { get; set; } = ChatProviderKind.OpenAI;
    public string DisplayName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string DefaultModel { get; set; } = "";

    public bool RequiresKey => Kind is ChatProviderKind.OpenAI or ChatProviderKind.Anthropic
        or ChatProviderKind.Google or ChatProviderKind.OpenAiCompatible;

    public bool IsLocal => Kind is ChatProviderKind.Ollama or ChatProviderKind.LmStudio;
}

public static class ChatProviderCatalog
{
    public const int MaxProviders = 16;

    public static ChatProviderConfig Preset(ChatProviderKind kind) => kind switch
    {
        ChatProviderKind.OpenAI => new() { Kind = kind, DisplayName = "OpenAI", BaseUrl = "https://api.openai.com/v1", DefaultModel = "gpt-4o-mini" },
        ChatProviderKind.Anthropic => new() { Kind = kind, DisplayName = "Anthropic", BaseUrl = "https://api.anthropic.com", DefaultModel = "claude-sonnet-4-5" },
        ChatProviderKind.Google => new() { Kind = kind, DisplayName = "Google AI", BaseUrl = "https://generativelanguage.googleapis.com", DefaultModel = "gemini-2.5-flash" },
        ChatProviderKind.Ollama => new() { Kind = kind, DisplayName = "Ollama (local)", BaseUrl = "http://localhost:11434/v1", DefaultModel = "llama3.1" },
        ChatProviderKind.LmStudio => new() { Kind = kind, DisplayName = "LM Studio (local)", BaseUrl = "http://localhost:1234/v1", DefaultModel = "local-model" },
        _ => new() { Kind = kind, DisplayName = "OpenAI-compatible", BaseUrl = "https://example.com/v1", DefaultModel = "" },
    };

    /// <summary>Repair a hand-edited provider list: drop blanks/dupes, fill defaults, cap the count.</summary>
    public static List<ChatProviderConfig> Sanitize(List<ChatProviderConfig>? providers)
    {
        var result = new List<ChatProviderConfig>();
        if (providers is null) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
        {
            if (p is null || result.Count >= MaxProviders) continue;
            if (!Enum.IsDefined(p.Kind)) p.Kind = ChatProviderKind.OpenAiCompatible;
            if (string.IsNullOrWhiteSpace(p.Id) || !seen.Add(p.Id)) p.Id = Guid.NewGuid().ToString("n");
            seen.Add(p.Id);
            var preset = Preset(p.Kind);
            if (string.IsNullOrWhiteSpace(p.BaseUrl)) p.BaseUrl = preset.BaseUrl;
            p.BaseUrl = p.BaseUrl.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(p.DisplayName)) p.DisplayName = preset.DisplayName;
            p.DisplayName = p.DisplayName.Trim();
            p.DefaultModel = (p.DefaultModel ?? "").Trim();
            result.Add(p);
        }
        return result;
    }

    /// <summary>What a key looks like in UI: never more than its last four characters.</summary>
    public static string MaskKey(string? key) =>
        string.IsNullOrEmpty(key) ? "not set" : key.Length <= 4 ? "••••" : $"••••{key[^4..]}";
}

/// <summary>One chat message; Role is "system", "user" or "assistant".</summary>
public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest(IReadOnlyList<ChatMessage> Messages, string Model, double? Temperature = null, int? MaxTokens = null);

public sealed record ChatReply(string Text, string Model, int? PromptTokens, int? CompletionTokens);

/// <summary>A fully-built HTTP request for a provider: pure data, ready for ChatClient to send.</summary>
public sealed record WireRequest(string Url, IReadOnlyDictionary<string, string> Headers, string JsonBody);

/// <summary>Pure per-provider wire shapes: build requests, parse replies and stream deltas.</summary>
public static class ProviderWire
{
    public const string AnthropicVersion = "2023-06-01";
    public const int DefaultMaxTokens = 1024;

    private static string Base(ChatProviderConfig p) => p.BaseUrl.TrimEnd('/');

    public static WireRequest BuildChatRequest(ChatProviderConfig provider, string? apiKey, ChatRequest request, bool stream)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? provider.DefaultModel : request.Model;
        return provider.Kind switch
        {
            ChatProviderKind.Anthropic => BuildAnthropic(provider, apiKey, request, model, stream),
            ChatProviderKind.Google => BuildGoogle(provider, apiKey, request, model, stream),
            _ => BuildOpenAiStyle(provider, apiKey, request, model, stream),
        };
    }

    private static WireRequest BuildOpenAiStyle(ChatProviderConfig p, string? apiKey, ChatRequest r, string model, bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = stream,
            ["messages"] = new JsonArray(r.Messages.Select(m => (JsonNode)new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content,
            }).ToArray()),
        };
        if (r.Temperature is { } t) body["temperature"] = t;
        if (r.MaxTokens is { } mt) body["max_tokens"] = mt;
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(apiKey)) headers["Authorization"] = $"Bearer {apiKey}";
        return new WireRequest($"{Base(p)}/chat/completions", headers, body.ToJsonString());
    }

    private static WireRequest BuildAnthropic(ChatProviderConfig p, string? apiKey, ChatRequest r, string model, bool stream)
    {
        var system = string.Join("\n\n", r.Messages.Where(m => m.Role == "system").Select(m => m.Content));
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = r.MaxTokens ?? DefaultMaxTokens,
            ["stream"] = stream,
            ["messages"] = new JsonArray(r.Messages.Where(m => m.Role != "system").Select(m => (JsonNode)new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content,
            }).ToArray()),
        };
        if (system.Length > 0) body["system"] = system;
        if (r.Temperature is { } t) body["temperature"] = t;
        var headers = new Dictionary<string, string> { ["anthropic-version"] = AnthropicVersion };
        if (!string.IsNullOrEmpty(apiKey)) headers["x-api-key"] = apiKey;
        return new WireRequest($"{Base(p)}/v1/messages", headers, body.ToJsonString());
    }

    private static WireRequest BuildGoogle(ChatProviderConfig p, string? apiKey, ChatRequest r, string model, bool stream)
    {
        var contents = new JsonArray(r.Messages.Where(m => m.Role != "system").Select(m => (JsonNode)new JsonObject
        {
            ["role"] = m.Role == "assistant" ? "model" : "user",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = m.Content }),
        }).ToArray());
        var body = new JsonObject { ["contents"] = contents };
        var system = string.Join("\n\n", r.Messages.Where(m => m.Role == "system").Select(m => m.Content));
        if (system.Length > 0)
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) };
        var gen = new JsonObject();
        if (r.Temperature is { } t) gen["temperature"] = t;
        if (r.MaxTokens is { } mt) gen["maxOutputTokens"] = mt;
        if (gen.Count > 0) body["generationConfig"] = gen;
        var verb = stream ? "streamGenerateContent?alt=sse" : "generateContent";
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(apiKey)) headers["x-goog-api-key"] = apiKey;
        return new WireRequest($"{Base(p)}/v1beta/models/{model}:{verb}", headers, body.ToJsonString());
    }

    public static string ModelsUrl(ChatProviderConfig p) => p.Kind switch
    {
        ChatProviderKind.Anthropic => $"{Base(p)}/v1/models",
        ChatProviderKind.Google => $"{Base(p)}/v1beta/models",
        _ => $"{Base(p)}/models",
    };

    public static IReadOnlyDictionary<string, string> ModelsHeaders(ChatProviderConfig p, string? apiKey)
    {
        var headers = new Dictionary<string, string>();
        switch (p.Kind)
        {
            case ChatProviderKind.Anthropic:
                headers["anthropic-version"] = AnthropicVersion;
                if (!string.IsNullOrEmpty(apiKey)) headers["x-api-key"] = apiKey;
                break;
            case ChatProviderKind.Google:
                if (!string.IsNullOrEmpty(apiKey)) headers["x-goog-api-key"] = apiKey;
                break;
            default:
                if (!string.IsNullOrEmpty(apiKey)) headers["Authorization"] = $"Bearer {apiKey}";
                break;
        }
        return headers;
    }

    // ───────────────────────── parsing ─────────────────────────

    public static ChatReply ParseResponse(ChatProviderKind kind, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return kind switch
        {
            ChatProviderKind.Anthropic => new ChatReply(
                string.Concat(root.GetProperty("content").EnumerateArray()
                    .Where(b => b.TryGetProperty("type", out var t) && t.GetString() == "text")
                    .Select(b => b.GetProperty("text").GetString() ?? "")),
                Str(root, "model") ?? "",
                Int(root, "usage", "input_tokens"), Int(root, "usage", "output_tokens")),
            ChatProviderKind.Google => new ChatReply(
                string.Concat(root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")
                    .EnumerateArray().Select(part => Str(part, "text") ?? "")),
                Str(root, "modelVersion") ?? "",
                Int(root, "usageMetadata", "promptTokenCount"), Int(root, "usageMetadata", "candidatesTokenCount")),
            _ => new ChatReply(
                Str(root.GetProperty("choices")[0].GetProperty("message"), "content") ?? "",
                Str(root, "model") ?? "",
                Int(root, "usage", "prompt_tokens"), Int(root, "usage", "completion_tokens")),
        };
    }

    public static IReadOnlyList<string> ParseModelList(ChatProviderKind kind, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ids = new List<string>();
        if (kind == ChatProviderKind.Google)
        {
            if (root.TryGetProperty("models", out var models))
                foreach (var m in models.EnumerateArray())
                {
                    var name = Str(m, "name");
                    if (name is not null) ids.Add(name.StartsWith("models/") ? name["models/".Length..] : name);
                }
        }
        else if (root.TryGetProperty("data", out var data))
        {
            foreach (var m in data.EnumerateArray())
            {
                var id = Str(m, "id");
                if (id is not null) ids.Add(id);
            }
        }
        return ids;
    }

    /// <summary>
    /// One streamed delta from one SSE line, or null when the line
    /// carries no text (keepalives, role-only chunks, [DONE], and the
    /// Anthropic event envelope lines around the JSON payloads).
    /// </summary>
    public static string? ParseStreamDelta(ChatProviderKind kind, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var data = line.StartsWith("data:", StringComparison.Ordinal) ? line[5..].Trim() : line.Trim();
        if (data.Length == 0 || data == "[DONE]") return null;
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            return kind switch
            {
                ChatProviderKind.Anthropic =>
                    Str(root, "type") == "content_block_delta" ? Str(root.GetProperty("delta"), "text") : null,
                ChatProviderKind.Google =>
                    root.TryGetProperty("candidates", out var c) && c.GetArrayLength() > 0
                        ? string.Concat(c[0].GetProperty("content").GetProperty("parts").EnumerateArray()
                            .Select(part => Str(part, "text") ?? ""))
                        : null,
                _ =>
                    root.TryGetProperty("choices", out var ch) && ch.GetArrayLength() > 0 &&
                    ch[0].TryGetProperty("delta", out var d)
                        ? Str(d, "content")
                        : null,
            };
        }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static int? Int(JsonElement el, string parent, string name) =>
        el.TryGetProperty(parent, out var p) && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : null;
}
