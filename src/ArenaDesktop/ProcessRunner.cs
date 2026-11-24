using System.Diagnostics;
using System.Text;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// The shell's IProcessRunner (completeness slice C): hidden
/// processes, argument lists (never a composed shell string), stdout
/// and stderr captured and also streamed line-by-line to a sink,
/// cancellation kills the whole process. Used by the Git panel and
/// the terminal; nothing here is reachable from the web view.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, IProgress<string>? outputLines = null, CancellationToken ct = default)
    {
        var start = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in spec.Arguments) start.ArgumentList.Add(arg);
        if (spec.Environment is not null)
            foreach (var (name, value) in spec.Environment) start.Environment[name] = value;

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var gate = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (gate) stdout.AppendLine(e.Data);
            outputLines?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (gate) stderr.AppendLine(e.Data);
            outputLines?.Report(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var cancelled = false;
        using (ct.Register(() =>
        {
            cancelled = true;
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already gone */ }
        }))
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        // Flush async readers.
        await Task.Delay(10);
        lock (gate)
        {
            return new ProcessResult(
                cancelled ? -1 : process.ExitCode,
                stdout.ToString(), stderr.ToString(), cancelled);
        }
    }
}
