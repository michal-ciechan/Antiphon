using System.Diagnostics;
using System.Text.Json;

namespace Antiphon.Server.Infrastructure.Agents.Tools;

/// <summary>
/// Executes bash/shell commands with working directory scoped to the worktree (NFR9).
/// </summary>
public sealed class BashTool : IAgentTool
{
    private readonly string _worktreeRoot;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    public BashTool(string worktreeRoot)
    {
        _worktreeRoot = worktreeRoot;
    }

    public string Name => "bash";

    public string Description => "Executes a shell command with the working directory set to the worktree root. Returns stdout and stderr.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "command": { "type": "string", "description": "The shell command to execute." },
            "timeout_ms": { "type": "integer", "description": "Optional timeout in milliseconds (default 120000)." }
          },
          "required": ["command"]
        }
        """;

    public async Task<string> ExecuteAsync(string jsonInput, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(jsonInput);
        var command = doc.RootElement.GetProperty("command").GetString()
            ?? throw new ArgumentException("Missing 'command' parameter.");

        var timeoutMs = doc.RootElement.TryGetProperty("timeout_ms", out var tmProp) && tmProp.TryGetInt32(out var tm)
            ? tm
            : (int)DefaultTimeout.TotalMilliseconds;

        // Determine shell based on OS
        var isWindows = OperatingSystem.IsWindows();
        var shell = isWindows ? "cmd.exe" : "/bin/bash";
        var shellArg = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"";

        var psi = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = shellArg,
            WorkingDirectory = _worktreeRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return "Error: Command timed out.";
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var result = $"Exit code: {process.ExitCode}";
        if (!string.IsNullOrEmpty(stdout))
            result += $"\n--- stdout ---\n{stdout}";
        if (!string.IsNullOrEmpty(stderr))
            result += $"\n--- stderr ---\n{stderr}";

        // Truncate very large output
        if (result.Length > 50_000)
            result = result[..50_000] + "\n... (output truncated)";

        return result;
    }
}
