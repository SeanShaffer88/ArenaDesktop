using System.Diagnostics;
using System.Text;

namespace ArenaDesktop.Core;

/// <summary>One side of a scratch comparison: a configured provider + a model on it.</summary>
public sealed record CompareTarget(string Name, ChatProviderConfig Provider, string Model);

/// <summary>One finished side: a reply, or the plain error that stopped it, plus how long it took.</summary>
public sealed record CompareResult(
    CompareTarget Target, string? Text, string? Error, TimeSpan Elapsed,
    int? PromptTokens, int? CompletionTokens)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Scratch comparison (completeness slice B3): one prompt fanned out
/// to several BYOK/local targets at once, results collected side by
/// side. Arena models are deliberately NOT targets — Arena has no
/// API and reading its page is forbidden by the no-script-injection
/// policy; Arena-side comparison stays in Battle Lab. A target that
/// fails lands as an error result; the run itself never throws for
/// one bad target.
/// </summary>
public static class ScratchCompare
{
    public const int MaxTargets = 6;

    public static async Task<IReadOnlyList<CompareResult>> RunAsync(
        string prompt,
        IReadOnlyList<CompareTarget> targets,
        Func<CompareTarget, ChatRequest, CancellationToken, Task<ChatReply>> send,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("The prompt is empty.", nameof(prompt));
        var chosen = targets.Take(MaxTargets).ToList();
        var tasks = chosen.Select(async target =>
        {
            var clock = Stopwatch.StartNew();
            try
            {
                var request = new ChatRequest([new ChatMessage("user", prompt)], target.Model);
                var reply = await send(target, request, ct);
                clock.Stop();
                return new CompareResult(target, reply.Text, null, clock.Elapsed, reply.PromptTokens, reply.CompletionTokens);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                clock.Stop();
                return new CompareResult(target, null, ex.Message, clock.Elapsed, null, null);
            }
        });
        return await Task.WhenAll(tasks);
    }

    /// <summary>The whole comparison as one Markdown document (for saving or pasting elsewhere).</summary>
    public static string RenderMarkdown(string prompt, IReadOnlyList<CompareResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scratch comparison");
        sb.AppendLine();
        sb.AppendLine("**Prompt**");
        sb.AppendLine();
        sb.AppendLine(prompt.Trim());
        foreach (var r in results)
        {
            sb.AppendLine();
            sb.AppendLine($"## {r.Target.Name} ({r.Target.Model}) — {r.Elapsed.TotalSeconds:0.0}s");
            sb.AppendLine();
            sb.AppendLine(r.Succeeded ? r.Text : $"*Failed: {r.Error}*");
        }
        return sb.ToString();
    }
}
