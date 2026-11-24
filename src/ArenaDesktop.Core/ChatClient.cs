using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;

namespace ArenaDesktop.Core;

/// <summary>A provider call failed; the message is safe to show (no key, no full URL).</summary>
public sealed class ChatProviderException(string message) : Exception(message);

/// <summary>
/// Sends chat requests for BYOK/local providers. The HTTP handler is
/// injected so tests run entirely against a fake handler — this class
/// never needs a live network to be exercised. API keys travel only
/// in request headers, never in logs or exception messages.
/// </summary>
public sealed class ChatClient(HttpMessageHandler handler) : IDisposable
{
    private readonly HttpClient _http = new(handler, disposeHandler: false)
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public void Dispose() => _http.Dispose();

    private static HttpRequestMessage NewRequest(WireRequest wire, HttpMethod method)
    {
        var request = new HttpRequestMessage(method, wire.Url);
        foreach (var (name, value) in wire.Headers) request.Headers.TryAddWithoutValidation(name, value);
        if (wire.JsonBody.Length > 0)
        {
            request.Content = new StringContent(wire.JsonBody, Encoding.UTF8, "application/json");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        return request;
    }

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "provider";

    private static async Task<ChatProviderException> FailureAsync(ChatProviderConfig provider, HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (body.Length > 300) body = body[..300];
        // A provider error body must never echo a key back into a log or dialog.
        return new ChatProviderException($"{provider.DisplayName} answered HTTP {(int)response.StatusCode}. {body.Trim()}");
    }

    public async Task<ChatReply> CompleteAsync(ChatProviderConfig provider, string? apiKey, ChatRequest request, CancellationToken ct = default)
    {
        var wire = ProviderWire.BuildChatRequest(provider, apiKey, request, stream: false);
        using var message = NewRequest(wire, HttpMethod.Post);
        using var response = await _http.SendAsync(message, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw await FailureAsync(provider, response, ct);
        try { return ProviderWire.ParseResponse(provider.Kind, json); }
        catch (Exception ex) when (ex is not ChatProviderException)
        {
            throw new ChatProviderException($"{provider.DisplayName} sent a reply ArenaDesktop could not read.");
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatProviderConfig provider, string? apiKey, ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var wire = ProviderWire.BuildChatRequest(provider, apiKey, request, stream: true);
        using var message = NewRequest(wire, HttpMethod.Post);
        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw await FailureAsync(provider, response, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            var delta = ProviderWire.ParseStreamDelta(provider.Kind, line);
            if (delta is not null) yield return delta;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(ChatProviderConfig provider, string? apiKey, CancellationToken ct = default)
    {
        var wire = new WireRequest(ProviderWire.ModelsUrl(provider), ProviderWire.ModelsHeaders(provider, apiKey), "");
        using var message = NewRequest(wire, HttpMethod.Get);
        using var response = await _http.SendAsync(message, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw await FailureAsync(provider, response, ct);
        try { return ProviderWire.ParseModelList(provider.Kind, json); }
        catch (Exception)
        {
            throw new ChatProviderException($"{provider.DisplayName} sent a model list ArenaDesktop could not read.");
        }
    }
}
