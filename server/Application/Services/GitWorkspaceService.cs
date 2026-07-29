using System.Diagnostics;
using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Thin async wrapper over the git CLI for an agent's workspace — status, HEAD content, and
/// unified diffs. Shells out (no LibGit2) so behaviour matches whatever git the machine has;
/// every call is best-effort: a non-repo workspace or a git failure degrades to "no git info",
/// never an error surfaced to the files UI.
/// </summary>
public sealed class GitWorkspaceService
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(15);
    private readonly ILogger<GitWorkspaceService> _logger;

    public GitWorkspaceService(ILogger<GitWorkspaceService> logger) => _logger = logger;

    public sealed record GitChange(string Path, GitFileStatus Status, string? OldPath);

    public async Task<bool> IsRepositoryAsync(string workingDirectory, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(workingDirectory, ct, "rev-parse", "--is-inside-work-tree");
        return code == 0 && stdout.Trim() == "true";
    }

    /// <summary>Working tree + index changes vs HEAD (porcelain v1 -z), untracked included.</summary>
    public async Task<IReadOnlyList<GitChange>> GetChangesAsync(string workingDirectory, CancellationToken ct)
    {
        var (code, stdout, stderr) = await RunAsync(
            workingDirectory, ct, "status", "--porcelain", "-z", "--untracked-files=all");
        if (code != 0)
        {
            _logger.LogDebug("git status failed in {Dir}: {Err}", workingDirectory, stderr);
            return [];
        }

        var changes = new List<GitChange>();
        var records = stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length < 4)
                continue;
            var index = record[0];
            var work = record[1];
            var path = record[3..];

            string? oldPath = null;
            if (index is 'R' or 'C' && i + 1 < records.Length)
            {
                // Renames/copies emit the ORIGINAL path as the next NUL-separated record.
                oldPath = records[++i];
            }

            changes.Add(new GitChange(path, Classify(index, work), oldPath));
        }
        return changes;
    }

    /// <summary>The file's content at HEAD, or null when it doesn't exist there (new file / no repo).</summary>
    public async Task<string?> GetHeadContentAsync(string workingDirectory, string relativePath, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(workingDirectory, ct, "show", $"HEAD:{relativePath}");
        return code == 0 ? stdout : null;
    }

    /// <summary>Unified diff of the file vs HEAD (staged + unstaged combined); null on failure/no repo.</summary>
    public async Task<string?> GetDiffAsync(string workingDirectory, string relativePath, CancellationToken ct)
    {
        var (code, stdout, _) = await RunAsync(workingDirectory, ct, "diff", "HEAD", "--", relativePath);
        return code == 0 ? stdout : null;
    }

    private static GitFileStatus Classify(char index, char work)
    {
        if (index == '?' || work == '?') return GitFileStatus.Untracked;
        if (index == 'A' || work == 'A') return GitFileStatus.Added;
        if (index == 'D' || work == 'D') return GitFileStatus.Deleted;
        if (index == 'R' || work == 'R') return GitFileStatus.Renamed;
        return GitFileStatus.Modified;
    }

    private async Task<(int Code, string Stdout, string Stderr)> RunAsync(
        string workingDirectory, CancellationToken ct, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process is null)
                return (-1, "", "git failed to start");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(GitTimeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("git {Args} timed out in {Dir}", string.Join(' ', args), workingDirectory);
            return (-1, "", "timeout");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "git {Args} failed in {Dir}", string.Join(' ', args), workingDirectory);
            return (-1, "", ex.Message);
        }
    }
}

public enum GitFileStatus
{
    None = 0,
    Modified = 1,
    Added = 2,
    Deleted = 3,
    Renamed = 4,
    Untracked = 5,
}
