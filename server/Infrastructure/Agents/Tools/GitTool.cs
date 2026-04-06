using System.Diagnostics;
using System.Text.Json;

namespace Antiphon.Server.Infrastructure.Agents.Tools;

/// <summary>
/// Executes git operations within the worktree (FR16).
/// Only allows a safe subset of git commands.
/// </summary>
public sealed class GitTool : IAgentTool
{
    private readonly string _worktreeRoot;

    private static readonly HashSet<string> AllowedSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "log", "diff", "show", "branch", "add", "commit", "tag",
        "stash", "checkout", "rev-parse", "ls-files", "blame",
        "clone", "push", "pull", "fetch", "remote", "init", "config"
    };

    private static readonly HashSet<string> BlockedFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--hard", "--no-verify"
    };

    public GitTool(string worktreeRoot)
    {
        _worktreeRoot = worktreeRoot;
    }

    public string Name => "git";

    public string Description => "Executes git commands within the worktree. Supports: status, log, diff, show, branch, add, commit, tag, stash, checkout, rev-parse, ls-files, blame, clone, push, pull, fetch, remote, init, config.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "args": { "type": "string", "description": "Git command arguments (e.g., 'status', 'log --oneline -10', 'diff HEAD')." }
          },
          "required": ["args"]
        }
        """;

    public async Task<string> ExecuteAsync(string jsonInput, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(jsonInput);
        var args = doc.RootElement.GetProperty("args").GetString()
            ?? throw new ArgumentException("Missing 'args' parameter.");

        // Validate subcommand
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "Error: No git subcommand specified.";

        var subcommand = parts[0];
        if (!AllowedSubcommands.Contains(subcommand))
            return $"Error: Git subcommand '{subcommand}' is not allowed. Allowed: {string.Join(", ", AllowedSubcommands)}.";

        // Block dangerous flags
        foreach (var part in parts)
        {
            if (BlockedFlags.Contains(part))
                return $"Error: Flag '{part}' is not allowed for safety reasons.";
        }

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = _worktreeRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

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
            return "Error: Git command timed out.";
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var result = $"Exit code: {process.ExitCode}";
        if (!string.IsNullOrEmpty(stdout))
            result += $"\n{stdout}";
        if (!string.IsNullOrEmpty(stderr) && process.ExitCode != 0)
            result += $"\n--- stderr ---\n{stderr}";

        if (result.Length > 50_000)
            result = result[..50_000] + "\n... (output truncated)";

        return result;
    }
}
