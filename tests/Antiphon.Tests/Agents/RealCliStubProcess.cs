using System.Diagnostics;
using System.Text;

namespace Antiphon.Tests.Agents;

/// <summary>Shared process launcher for CARD-0168 A-tier canaries.</summary>
internal static class RealCliStubProcess
{
    public sealed record Result(int ExitCode, string Stdout, string Stderr)
    {
        public string Combined => $"exit={ExitCode}\n--- stdout ---\n{Stdout}\n--- stderr ---\n{Stderr}";
    }

    public static async Task<Result> RunAsync(
        string app,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> env,
        TimeSpan timeout,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = app,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            psi.Environment[entry.Key.ToString()!] = entry.Value?.ToString() ?? "";
        foreach (var (k, v) in env)
            psi.Environment[k] = v;

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!proc.Start())
            throw new InvalidOperationException($"Failed to start {app}");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException(
                $"Process {app} did not exit within {timeout}. stdout={stdout} stderr={stderr}");
        }

        return new Result(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
