using System.Net;
using System.Text;
using ArenaDesktop.Core;
using Xunit;

namespace ArenaDesktop.Core.Tests;

/// <summary>Completeness slices B1/B2: provider wire shapes, chat client (fake HTTP), key vault.</summary>
public sealed class ProviderTests
{
    private static readonly ChatRequest Sample = new(
        [new ChatMessage("system", "Be terse."), new ChatMessage("user", "Hello")], "model-x");

    [Fact]
    public void OpenAiRequest_UsesChatCompletionsShape()
    {
        var wire = ProviderWire.BuildChatRequest(ChatProviderCatalog.Preset(ChatProviderKind.OpenAI), "sk-test", Sample, stream: false);
        Assert.Equal("https://api.openai.com/v1/chat/completions", wire.Url);
        Assert.Equal("Bearer sk-test", wire.Headers["Authorization"]);
        Assert.Contains("\"model\":\"model-x\"", wire.JsonBody);
        Assert.Contains("\"role\":\"system\"", wire.JsonBody);
    }

    [Fact]
    public void AnthropicRequest_SplitsSystemAndUsesXApiKey()
    {
        var wire = ProviderWire.BuildChatRequest(ChatProviderCatalog.Preset(ChatProviderKind.Anthropic), "ak-test", Sample, stream: true);
        Assert.Equal("https://api.anthropic.com/v1/messages", wire.Url);
        Assert.Equal("ak-test", wire.Headers["x-api-key"]);
        Assert.Equal(ProviderWire.AnthropicVersion, wire.Headers["anthropic-version"]);
        Assert.Contains("\"system\":\"Be terse.\"", wire.JsonBody);
        Assert.Contains("\"max_tokens\":1024", wire.JsonBody);
        Assert.DoesNotContain("\"role\":\"system\"", wire.JsonBody);
    }

    [Fact]
    public void GoogleRequest_UsesGenerateContentWithModelRole()
    {
        var request = new ChatRequest(
            [new ChatMessage("user", "Hi"), new ChatMessage("assistant", "Hello"), new ChatMessage("user", "Again")], "gemini-x");
        var wire = ProviderWire.BuildChatRequest(ChatProviderCatalog.Preset(ChatProviderKind.Google), "gk", request, stream: false);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-x:generateContent", wire.Url);
        Assert.Equal("gk", wire.Headers["x-goog-api-key"]);
        Assert.Contains("\"role\":\"model\"", wire.JsonBody);
    }

    [Fact]
    public void LocalProviders_NeedNoKeyAndDefaultToLocalhost()
    {
        var ollama = ChatProviderCatalog.Preset(ChatProviderKind.Ollama);
        Assert.False(ollama.RequiresKey);
        Assert.StartsWith("http://localhost:11434", ollama.BaseUrl);
        var wire = ProviderWire.BuildChatRequest(ollama, null, Sample, stream: false);
        Assert.False(wire.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public void ParseResponse_ReadsEachProvidersShape()
    {
        var openAi = ProviderWire.ParseResponse(ChatProviderKind.OpenAI,
            """{"model":"gpt-x","choices":[{"message":{"role":"assistant","content":"hi"}}],"usage":{"prompt_tokens":3,"completion_tokens":1}}""");
        Assert.Equal("hi", openAi.Text);
        Assert.Equal(3, openAi.PromptTokens);

        var anthropic = ProviderWire.ParseResponse(ChatProviderKind.Anthropic,
            """{"model":"claude-x","content":[{"type":"text","text":"yo"}],"usage":{"input_tokens":5,"output_tokens":2}}""");
        Assert.Equal("yo", anthropic.Text);
        Assert.Equal(5, anthropic.PromptTokens);

        var google = ProviderWire.ParseResponse(ChatProviderKind.Google,
            """{"modelVersion":"gemini-x","candidates":[{"content":{"parts":[{"text":"hey"}]}}],"usageMetadata":{"promptTokenCount":7,"candidatesTokenCount":4}}""");
        Assert.Equal("hey", google.Text);
        Assert.Equal(7, google.PromptTokens);
    }

    [Fact]
    public void StreamDeltas_ParsePerProvider_AndIgnoreNoise()
    {
        Assert.Equal("Hel", ProviderWire.ParseStreamDelta(ChatProviderKind.OpenAI,
            """data: {"choices":[{"delta":{"content":"Hel"}}]}"""));
        Assert.Null(ProviderWire.ParseStreamDelta(ChatProviderKind.OpenAI, "data: [DONE]"));
        Assert.Null(ProviderWire.ParseStreamDelta(ChatProviderKind.OpenAI, ": keepalive"));
        Assert.Equal("lo", ProviderWire.ParseStreamDelta(ChatProviderKind.Anthropic,
            """data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"lo"}}"""));
        Assert.Null(ProviderWire.ParseStreamDelta(ChatProviderKind.Anthropic,
            """data: {"type":"message_start","message":{}}"""));
        Assert.Equal("!", ProviderWire.ParseStreamDelta(ChatProviderKind.Google,
            """data: {"candidates":[{"content":{"parts":[{"text":"!"}]}}]}"""));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ChatClient_CompletesAgainstFakeHandler()
    {
        var handler = new FakeHandler(_ => Json(
            """{"model":"gpt-x","choices":[{"message":{"content":"answer"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}"""));
        using var client = new ChatClient(handler);
        var reply = await client.CompleteAsync(
            ChatProviderCatalog.Preset(ChatProviderKind.OpenAI), "sk-secret",
            new ChatRequest([new ChatMessage("user", "q")], "gpt-x"));
        Assert.Equal("answer", reply.Text);
        Assert.Equal("Bearer sk-secret", handler.LastRequest!.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task ChatClient_StreamsSseDeltas()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"a\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"b\"}}]}\n\ndata: [DONE]\n";
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });
        using var client = new ChatClient(handler);
        var parts = new List<string>();
        await foreach (var delta in client.StreamAsync(
            ChatProviderCatalog.Preset(ChatProviderKind.OpenAI), "k",
            new ChatRequest([new ChatMessage("user", "q")], "gpt-x")))
            parts.Add(delta);
        Assert.Equal("ab", string.Concat(parts));
    }

    [Fact]
    public async Task ChatClient_FailureNamesStatusNotKey()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("bad key") });
        using var client = new ChatClient(handler);
        var ex = await Assert.ThrowsAsync<ChatProviderException>(() => client.CompleteAsync(
            ChatProviderCatalog.Preset(ChatProviderKind.OpenAI), "sk-secret",
            new ChatRequest([new ChatMessage("user", "q")], "gpt-x")));
        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain("sk-secret", ex.Message);
    }

    [Fact]
    public async Task ChatClient_ListsModels()
    {
        var handler = new FakeHandler(_ => Json("""{"data":[{"id":"gpt-x"},{"id":"gpt-y"}]}"""));
        using var client = new ChatClient(handler);
        var models = await client.ListModelsAsync(ChatProviderCatalog.Preset(ChatProviderKind.OpenAI), "k");
        Assert.Equal(["gpt-x", "gpt-y"], models);
    }

    [Fact]
    public void Vault_RoundTripsAndNeverStoresClearText()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arena-vault-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "api-keys.json");
            var protector = new PassphraseSecretProtector("correct horse", iterations: 100_000);
            var vault = ApiKeyVault.Load(path, protector);
            vault.SetKey("p1", "sk-very-secret");
            Assert.True(File.Exists(path));
            Assert.DoesNotContain("sk-very-secret", File.ReadAllText(path));

            var reopened = ApiKeyVault.Load(path, protector);
            Assert.Equal("sk-very-secret", reopened.GetKey("p1"));
            Assert.True(reopened.HasKey("p1"));

            var wrong = ApiKeyVault.Load(path, new PassphraseSecretProtector("wrong", iterations: 100_000));
            Assert.Null(wrong.GetKey("p1"));

            reopened.RemoveKey("p1");
            Assert.False(ApiKeyVault.Load(path, protector).HasKey("p1"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Sanitize_RepairsProviderList()
    {
        var list = ChatProviderCatalog.Sanitize(
        [
            new ChatProviderConfig { Id = "a", Kind = ChatProviderKind.OpenAI, BaseUrl = " https://api.openai.com/v1/ " },
            new ChatProviderConfig { Id = "a", Kind = ChatProviderKind.Ollama },
            null!,
        ]);
        Assert.Equal(2, list.Count);
        Assert.Equal("https://api.openai.com/v1", list[0].BaseUrl);
        Assert.NotEqual(list[0].Id, list[1].Id);
        Assert.Equal("••••1234", ChatProviderCatalog.MaskKey("sk-1234"));
        Assert.Equal("not set", ChatProviderCatalog.MaskKey(null));
    }
}

/// <summary>Slice B3: the fan-out engine (kept in this file with the provider tests).</summary>
public sealed class ScratchCompareTests
{
    [Fact]
    public async Task Run_CollectsSuccessesAndFailuresSideBySide()
    {
        var provider = ChatProviderCatalog.Preset(ChatProviderKind.Ollama);
        var targets = new[]
        {
            new CompareTarget("Good", provider, "m1"),
            new CompareTarget("Bad", provider, "m2"),
        };
        var results = await ScratchCompare.RunAsync("prompt", targets, (target, request, ct) =>
            target.Name == "Good"
                ? Task.FromResult(new ChatReply("answer", "m1", 1, 2))
                : Task.FromException<ChatReply>(new ChatProviderException("down")));
        Assert.Equal(2, results.Count);
        Assert.Equal("answer", results[0].Text);
        Assert.Equal("down", results[1].Error);
        var markdown = ScratchCompare.RenderMarkdown("prompt", results);
        Assert.Contains("## Good (m1)", markdown);
        Assert.Contains("*Failed: down*", markdown);
    }

    [Fact]
    public async Task Run_RejectsEmptyPrompt()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ScratchCompare.RunAsync("  ", [],
            (t, r, ct) => Task.FromResult(new ChatReply("", "", null, null))));
    }
}
