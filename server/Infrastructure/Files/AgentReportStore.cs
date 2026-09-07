using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Files;

/// <summary>
/// Retained exact UTF-8 reports, outside delegate worktrees. Publication is a sibling rename;
/// canonical identity is full task GUID plus exact byte SHA-256, never the normalized note digest.
/// </summary>
public sealed class AgentReportStore(
    GitWorkspaceService git, IOptions<DelegationSettings> settings, ILogger<AgentReportStore> logger)
    : IAgentReportStore
{
    internal Func<CancellationToken, Task>? BeforePublishAsync { get; set; }
    internal Func<string, CancellationToken, Task>? BeforeIoAsync { get; set; }

    public async Task<AgentReportStorageResult> StoreAsync(AgentTask task, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        var token = budget.Token;
        var watch = Stopwatch.StartNew();
        var phase = "root";
        string? temporary = null;
        try
        {
            if (BeforeIoAsync is not null) await BeforeIoAsync(phase, token);
            var root = await ResolveRootAsync(task, token);
            if (root is null) return Unavailable("no-durable-root");
            var raw = task.Result ?? "";
            var bytes = new UTF8Encoding(false, true).GetBytes(raw);
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var path = Path.Combine(root, task.Id.ToString("D"), digest + ".md");
            if (path.Length > 1000) return Unavailable("path-too-long");
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            phase = "custody";
            if (!await EnsureIgnoredAsync(root, path, temporary, token))
                return Unavailable("not-ignored-or-tracked");
            if (HasReparseAncestor(path)) return Unavailable("redirected-path");
            if (await IsUsableAsync(path, raw, token)) return new(path, null);
            phase = "write";
            if (BeforeIoAsync is not null) await BeforeIoAsync(phase, token);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, token);
                await stream.FlushAsync(token);
            }
            phase = "publish";
            if (BeforePublishAsync is not null) await BeforePublishAsync(token);
            if (BeforeIoAsync is not null) await BeforeIoAsync(phase, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            phase = "readback";
            if (BeforeIoAsync is not null) await BeforeIoAsync(phase, token);
            if (!await IsUsableAsync(path, raw, token)) return Unavailable("verification-failed");
            logger.LogInformation("Report storage task {TaskId} phase {Phase} duration {DurationMs}ms succeeded",
                task.Id, phase, watch.ElapsedMilliseconds);
            return new(path, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Unavailable("io-budget-expired"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or InvalidOperationException)
        { return Unavailable(ex.GetType().Name); }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        AgentReportStorageResult Unavailable(string reason)
        {
            // Exception messages and raw text are deliberately excluded from custody diagnostics.
            logger.LogWarning("Report storage task {TaskId} phase {Phase} duration {DurationMs}ms unavailable: {Reason}",
                task.Id, phase, watch.ElapsedMilliseconds, reason);
            return new(null, reason);
        }
    }

    public async Task<bool> IsUsableAsync(string? path, string raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Length > 1000)
            return false;
        try
        {
            var expected = new UTF8Encoding(false, true).GetBytes(raw);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length != expected.Length) return false;
            var actual = new byte[expected.Length];
            await stream.ReadExactlyAsync(actual, ct);
            return actual.AsSpan().SequenceEqual(expected) && Encoding.UTF8.GetString(actual) == raw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException) { return false; }
    }

    private async Task<string?> ResolveRootAsync(AgentTask task, CancellationToken ct)
    {
        foreach (var candidate in new[] { task.RepoPath, task.WorkingDirectory }.Distinct())
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate)) continue;
            var info = await git.GetWorkspaceInfoAsync(candidate, ct);
            if (!info.IsGitRepository || info.RepoRoot is null) continue;
            var main = Path.GetFullPath(info.RepoRoot);
            // GetWorkspaceInfo's odd-layout fallback is not evidence of a primary checkout.
            if (!Directory.Exists(Path.Combine(main, ".git"))) continue;
            var (code, common, _) = await git.RunReadOnlyAsync(main, ct,
                "rev-parse", "--path-format=absolute", "--git-common-dir");
            if (code != 0 || !SamePath(common.Trim(), Path.Combine(main, ".git"))) continue;
            var root = Path.Combine(main, ".antiphon", "reports");
            if (IsPersistent(root, task)) return root;
        }
        var configured = settings.Value.ReportStorageRoot;
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured)) return null;
        var full = Path.GetFullPath(configured);
        if (!IsPersistent(full, task)) return null;
        var existing = ExistingAncestor(full);
        if (existing is null) return null;
        var top = await git.GetRepoToplevelAsync(existing, ct);
        // Even an unrelated linked worktree is not a persistent override.
        if (top is not null && !Directory.Exists(Path.Combine(top, ".git"))) return null;
        return full;
    }

    private static bool IsPersistent(string root, AgentTask task) =>
        !Within(root, Path.GetTempPath())
        && (string.IsNullOrWhiteSpace(task.WorktreePath) || !Within(root, task.WorktreePath))
        && !HasReparseAncestor(root);

    private async Task<bool> EnsureIgnoredAsync(string root, string path, string temporary, CancellationToken ct)
    {
        var existing = ExistingAncestor(root);
        if (existing is null) return false;
        var top = await git.GetRepoToplevelAsync(existing, ct);
        if (top is null) return true;
        var relative = Path.GetRelativePath(top, path).Replace('\\', '/');
        var tempRelative = Path.GetRelativePath(top, temporary).Replace('\\', '/');
        var (trackedCode, tracked, _) = await git.RunReadOnlyAsync(top, ct, "ls-files", "-z", "--", relative, tempRelative);
        if (trackedCode != 0 || tracked.Length != 0) return false;
        if (await IgnoredAsync()) return true;
        var (code, exclude, _) = await git.RunReadOnlyAsync(top, ct,
            "rev-parse", "--path-format=absolute", "--git-path", "info/exclude");
        if (code != 0 || !Path.IsPathFullyQualified(exclude.Trim())) return false;
        var pattern = "/" + Path.GetRelativePath(top, root).Replace('\\', '/').TrimEnd('/') + "/";
        // A root containing glob syntax cannot safely become an exclude rule.
        if (pattern.IndexOfAny(['*', '?', '[', '\n', '\r']) >= 0 || pattern.Contains("../")) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(exclude.Trim())!);
        await File.AppendAllTextAsync(exclude.Trim(), "\n" + pattern + "\n", ct);
        return await IgnoredAsync();

        async Task<bool> IgnoredAsync()
        {
            // Check each independently: check-ignore's success means ANY supplied path was ignored.
            foreach (var item in new[] { relative, tempRelative })
            {
                var (c, _, _) = await git.RunReadOnlyAsync(top, ct, "check-ignore", "--no-index", "-q", "--", item);
                if (c != 0) return false;
            }
            return true;
        }
    }

    private static string? ExistingAncestor(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current)) return current;
        return null;
    }

    private static bool HasReparseAncestor(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool Within(string path, string root) => SamePath(path, root) || Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
