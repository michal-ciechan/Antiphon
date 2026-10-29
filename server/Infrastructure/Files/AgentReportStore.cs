using System.Collections;
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

    // Two-wave preparation policy: one default Git allowance plus one more for every remaining command.
    // This is not scaled from configured Git timeouts and is not a measured p95.
    internal static readonly TimeSpan DefaultPreparationBudget = TimeSpan.FromSeconds(30);
    internal TimeSpan PreparationBudget { get; set; } = DefaultPreparationBudget;

    // Report-file I/O only. The timer starts at the existing-file check and is shared through readback.
    internal static readonly TimeSpan DefaultIoBudget = TimeSpan.FromSeconds(2);
    internal TimeSpan IoBudget { get; set; } = DefaultIoBudget;

    public async Task<AgentReportStorageResult> StoreAsync(AgentTask task, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var preparationWatch = Stopwatch.StartNew();
        var ioWatch = new Stopwatch();
        var phases = new List<string>();
        string? outcome = null;
        string? reason = null;
        string? temporary = null;
        long? preparationMs = null;
        long? ioMs = null;
        long? cleanupMs = null;
        var ioStarted = false;
        CancellationTokenSource? preparation = null;
        CancellationTokenSource? io = null;
        try
        {
            preparation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            preparation.CancelAfter(PreparationBudget);
            var prep = preparation.Token;

            Enter("root");
            if (BeforeIoAsync is not null) await BeforeIoAsync("root", prep);
            prep.ThrowIfCancellationRequested();
            var root = await ResolveRootAsync(task, prep);
            prep.ThrowIfCancellationRequested();
            if (root is null) return Refuse("no-durable-root");
            var raw = task.Result ?? "";
            var bytes = new UTF8Encoding(false, true).GetBytes(raw);
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var path = Path.Combine(root, task.Id.ToString("D"), digest + ".md");
            if (path.Length > 1000) return Refuse("path-too-long");
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Enter("custody");
            if (!await EnsureIgnoredAsync(root, path, temporary, prep))
            {
                prep.ThrowIfCancellationRequested();
                return Refuse("not-ignored-or-tracked");
            }
            prep.ThrowIfCancellationRequested();
            if (HasReparseAncestor(path)) return Refuse("redirected-path");
            prep.ThrowIfCancellationRequested();

            preparationWatch.Stop();
            preparationMs = preparationWatch.ElapsedMilliseconds;
            preparation.Dispose();
            preparation = null;

            io = CancellationTokenSource.CreateLinkedTokenSource(ct);
            io.CancelAfter(IoBudget);
            ioStarted = true;
            ioWatch.Start();
            var token = io.Token;
            token.ThrowIfCancellationRequested();

            Enter("existing-read");
            if (BeforeIoAsync is not null) await BeforeIoAsync("existing-read", token);
            if (await IsUsableAsync(path, raw, token))
            {
                token.ThrowIfCancellationRequested();
                outcome = "reused";
                return new(path, null);
            }
            token.ThrowIfCancellationRequested();

            Enter("write");
            if (BeforeIoAsync is not null) await BeforeIoAsync("write", token);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, token);
                await stream.FlushAsync(token);
            }
            Enter("publish");
            if (BeforePublishAsync is not null) await BeforePublishAsync(token);
            if (BeforeIoAsync is not null) await BeforeIoAsync("publish", token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            Enter("readback");
            if (BeforeIoAsync is not null) await BeforeIoAsync("readback", token);
            if (!await IsUsableAsync(path, raw, token)) return Refuse("verification-failed");
            token.ThrowIfCancellationRequested();
            outcome = "stored";
            return new(path, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "caller-canceled";
            reason = "caller-canceled";
            throw;
        }
        catch (OperationCanceledException) when (!ioStarted && preparation is { IsCancellationRequested: true })
        { return Refuse("preparation-budget-expired"); }
        catch (OperationCanceledException) when (ioStarted && io is { IsCancellationRequested: true })
        { return Refuse("io-budget-expired"); }
        catch (OperationCanceledException)
        { return Refuse("canceled"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or InvalidOperationException)
        { return Refuse(ex.GetType().Name); }
        finally
        {
            if (preparationWatch.IsRunning)
            {
                preparationWatch.Stop();
                preparationMs ??= preparationWatch.ElapsedMilliseconds;
            }
            if (ioWatch.IsRunning)
            {
                ioWatch.Stop();
                ioMs = ioWatch.ElapsedMilliseconds;
            }
            if (temporary is not null && File.Exists(temporary))
            {
                var cleanup = Stopwatch.StartNew();
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                cleanupMs = cleanup.ElapsedMilliseconds;
            }
            LogOutcome(task.Id, phases, outcome, reason, preparationMs, ioMs, cleanupMs, watch.ElapsedMilliseconds);
            preparation?.Dispose();
            io?.Dispose();
        }

        void Enter(string name) => phases.Add(name);

        AgentReportStorageResult Refuse(string why)
        {
            outcome = why;
            reason = why;
            return new(null, why);
        }
    }

    private void LogOutcome(Guid taskId, List<string> phases, string? outcome, string? reason,
        long? preparationMs, long? ioMs, long? cleanupMs, long totalMs)
    {
        var phase = phases.Count == 0 ? "none" : phases[^1];
        var succeeded = outcome is "stored" or "reused";
        var resolvedOutcome = outcome ?? "unavailable";
        var message = succeeded
            ? $"Report storage task {taskId:D} phase {phase} duration {totalMs}ms succeeded"
            : $"Report storage task {taskId:D} phase {phase} duration {totalMs}ms unavailable: {reason}";
        var fields = new List<KeyValuePair<string, object?>>
        {
            new("TaskId", taskId),
            new("Phase", phase),
            new("Outcome", resolvedOutcome),
            new("Phases", string.Join(',', phases)),
            new("TotalMs", totalMs),
            new("DurationMs", totalMs),
        };
        if (preparationMs is not null)
            fields.Add(new("PreparationMs", preparationMs.Value));
        if (ioMs is not null)
            fields.Add(new("IoMs", ioMs.Value));
        if (cleanupMs is not null)
            fields.Add(new("CleanupMs", cleanupMs.Value));
        if (reason is not null)
            fields.Add(new("Reason", reason));
        fields.Add(new("{OriginalFormat}", succeeded
            ? "Report storage task {TaskId} phase {Phase} duration {DurationMs}ms succeeded"
            : "Report storage task {TaskId} phase {Phase} duration {DurationMs}ms unavailable: {Reason}"));
        var state = new StoreLogState(message, fields);
        logger.Log(succeeded ? LogLevel.Information : LogLevel.Warning, new EventId(0), state, null,
            static (value, _) => value.Message);
    }

    /// <summary>Structured store outcome. Values are durations and fixed names, never report bytes or paths.</summary>
    private sealed class StoreLogState(string message, List<KeyValuePair<string, object?>> items)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public string Message { get; } = message;
        public int Count => items.Count;
        public KeyValuePair<string, object?> this[int index] => items[index];
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
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
        var excludePath = exclude.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(excludePath)!);
        // Higher-priority ignore rules can defeat this rule; retries must not keep appending it.
        if (!File.Exists(excludePath)
            || !(await File.ReadAllLinesAsync(excludePath, ct)).Contains(pattern, StringComparer.Ordinal))
            await File.AppendAllTextAsync(excludePath, "\n" + pattern + "\n", ct);
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
