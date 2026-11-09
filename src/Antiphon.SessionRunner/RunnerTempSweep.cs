using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1677 S2. One registered temp family: an exact regex anchored by <c>^</c> and <c>\z</c> (never <c>$</c>, which also
/// matches before a final line feed) over a top-level temp name (or, with
/// <see cref="Container"/>, over <c>&lt;container&gt;/&lt;child&gt;</c> at depth 2), and the repository file whose
/// <see cref="Literal"/> creates it. <see cref="Producer"/> is null only for the CLI harness family (D-12).
/// </summary>
public sealed record TempFamily(
    string Id, string Pattern, string? Producer, string? Literal, string Sample,
    string? Container = null, bool DefaultEnabled = true, bool RequiresMirrorAbsent = false)
{
    private Regex? _regex;

    public Regex Regex => _regex ??= new Regex(Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}

/// <summary>
/// CARD-1677 S2. Runner-side ceilings. A request's per-tick bounds are clamped to <see cref="MaxEntries"/> and
/// <see cref="MaxBytes"/>; an entry over <see cref="InventoryEntries"/> entries or <see cref="InventoryBytes"/> is refused.
/// </summary>
public sealed record TempSweepLimits(int MaxEntries = 200, long MaxBytes = 21_474_836_480, int InventoryEntries = 100_000,
    long InventoryBytes = 1_073_741_824)
{
    /// <summary>The sweep starts no new removal past this.</summary>
    public TimeSpan TimeBudget { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Kept candidates beyond this are counted, not listed (the response says Truncated).</summary>
    public int MaxReportedKept { get; init; } = 500;

    /// <summary>The bounded byte count of unregistered names, across the whole sweep.</summary>
    public int UnregisteredWalkEntries { get; init; } = 200_000;
}

/// <summary>
/// CARD-1677 S2 (plan §3.5). A bounded sweep of the runner's temp root that can act only on a REGISTERED
/// family name (top level only; <c>claude-scratch</c> at depth 2 under its container), and only when the
/// entry is not denied, its family is switched on, it is not a symlink, the runner uid owns it and every
/// descendant, it is within the inventory limit, its newest regular-file write is older than the request's
/// age, and the fail-closed holder scan is complete and finds no holder. Every other name is reported as
/// <c>unregistered</c> (count and bytes per normalized prefix) and never touched. DryRun never deletes.
/// Deletion goes through <see cref="RunnerWorkspaceNoFollowDelete"/> (descriptor-relative, never follows a
/// link: an inner symlink is unlinked itself). Any doubt keeps the entry with a reason.
/// </summary>
public sealed class RunnerTempSweep
{
    public const string ClaudeScratch = "claude-scratch";
    public const string ClaudeContainer = "claude-1654";

    /// <summary>
    /// The shipped family table (plan §3.5). The <c>pty-test-root</c> alternation equals
    /// <c>TestSessionLogRoot.KnownPrefixes</c> (V-48b); each producer file still contains its literal (V-48).
    /// </summary>
    internal static IReadOnlyList<TempFamily> RegisteredFamilies { get; } =
    [
        new("c487-probe", "^c487-probe-[0-9a-f]{32}\\z",
            "tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs", "\"c487-probe-\"",
            "c487-probe-0123456789abcdef0123456789abcdef"),
        new("c487", "^c487-[0-9a-f]{32}\\z",
            "scripts/lib/c487-harness.ps1", "('c487-' + [guid]::NewGuid().ToString('N'))",
            "c487-0123456789abcdef0123456789abcdef"),
        new("pty-test-root",
            "^antiphon-(cpu-watchdog-tests|liveness-tests|adoption-tests|bufbounds-tests|backend-seam|first-write-race|0180-dto"
            + "|card0395-adoption|card0395-write-fault|card0395-http|c420-barrier|c502-v25|c502-v26|c502-v26-manifest|c502-v26-herdr"
            + "|c502-v27|c514-gen|c514-seq|c514-gate|c514-dest|c649-prompt|c79-stop|c628-token|c710-platform|c1636-adopt"
            + "|c1653-adopt|c1653-launch|c1653-sock|c1653-peer|c1778-trust)-[0-9a-f]{32}\\z",
            "tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs", "$\"antiphon-{prefix}-{Guid.NewGuid():N}\"",
            "antiphon-adoption-tests-0123456789abcdef0123456789abcdef"),
        new("c514-pty", "^antiphon-c514-pty-[0-9a-f]{32}\\z",
            "tests/Antiphon.Tests/TestHelpers/RemoteControlPtyLane.cs", "$\"antiphon-c514-pty-{sessionId:N}\"",
            "antiphon-c514-pty-0123456789abcdef0123456789abcdef"),
        new("c502-exit-cap", "^c502-exit-cap-[0-9a-f]{32}\\z",
            "tests/Antiphon.Tests/Application/SessionGenerationExitTests.cs", "$\"c502-exit-cap-{Guid.NewGuid():N}\"",
            "c502-exit-cap-0123456789abcdef0123456789abcdef"),
        new("c1514", "^c1514-[0-9a-f]{32}\\z",
            "tests/Antiphon.Tests/TestHelpers/C1514Support.cs", "\"c1514-\" + Guid.NewGuid().ToString(\"N\")",
            "c1514-0123456789abcdef0123456789abcdef"),
        new("land-verify", "^antiphon-land-verify-[0-9a-f]{32}\\z",
            "server/Application/Services/AgentTaskLandService.cs", "\"antiphon-land-verify-\" + Guid.NewGuid().ToString(\"N\")",
            "antiphon-land-verify-0123456789abcdef0123456789abcdef"),
        new("bridge-queue", "^antiphon-bridge-queue-[0-9a-f]{32}\\z",
            "tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs", "$\"antiphon-bridge-queue-{Guid.NewGuid():N}\"",
            "antiphon-bridge-queue-0123456789abcdef0123456789abcdef"),
        new("rcspeed", "^rcspeed-[0-9a-f]{32}(\\.ps1)?\\z",
            "tests/Antiphon.Tests/Scripts/RcFullTimingContractTests.cs", "\"rcspeed-\" + Guid.NewGuid().ToString(\"N\")",
            "rcspeed-0123456789abcdef0123456789abcdef.ps1"),
        // D-12: the Claude Code CLI scratchpad keyed by the session cwd (the mirror). Produced by the CLI
        // harness, not by a repository file, so it has no producer pin, is off unless its flag is on, and
        // is eligible only when the mirror it names is gone from the worktree root and the retiring root.
        new(ClaudeScratch, "^claude-1654/-work-worktrees-task-[0-9a-f]{8}\\z", null, null,
            "claude-1654/-work-worktrees-task-0123abcd",
            Container: ClaudeContainer, DefaultEnabled: false, RequiresMirrorAbsent: true),
    ];

    private static readonly string[] DeniedExact =
    [
        "antiphon-pty-hosts", "node-compile-cache",
        // CARD-0826 CleanupFamilyRegistry.IsDenied components, at the top level.
        ".git", ".claude", ".codex", ".grok", "node_modules", "build-slots", "runner-state", "hangfire",
        "postgres", "docker", "reports", "deliverables",
    ];

    private static readonly string[] DeniedPrefixes = ["antiphon-pty-", "CoreFxPipe_", "dotnet-diagnostic-", ".X", "systemd-"];

    private static readonly Regex HexRun = new("[0-9a-f]{8,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DigitRun = new("[0-9]{5,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _tempRoot;
    private readonly IReadOnlyList<TempFamily> _families;
    private readonly IWorkspaceHolderScan _holders;
    private readonly Func<string, int?> _ownerUid;
    private readonly int _runnerUid;
    private readonly TimeProvider _clock;
    private readonly TempSweepLimits _limits;
    private readonly Func<string, bool> _mirrorPresent;
    private readonly string _retiringRoot;

    public RunnerTempSweep(string tempRoot, IReadOnlyList<TempFamily> families, IWorkspaceHolderScan holders,
        Func<string, int?> ownerUid, int runnerUid, TimeProvider clock, TempSweepLimits limits,
        Func<string, bool> mirrorPresent, string retiringRoot = "/work/worktrees-retiring")
    {
        _tempRoot = tempRoot.Replace('\\', '/').TrimEnd('/');
        _families = families;
        _holders = holders;
        _ownerUid = ownerUid;
        _runnerUid = runnerUid;
        _clock = clock;
        _limits = limits;
        _mirrorPresent = mirrorPresent;
        _retiringRoot = retiringRoot.Replace('\\', '/').TrimEnd('/');
    }

    /// <summary>The production sweep: <c>/tmp</c>, the shipped families, the real /proc scan and lstat owner.</summary>
    public static RunnerTempSweep CreateDefault(string retiringRoot) => CreateDefault(retiringRoot, "/tmp");

    /// <summary>The production composition over another temp root (the native witnesses bind it to a test root, never /tmp).</summary>
    internal static RunnerTempSweep CreateDefault(string retiringRoot, string tempRoot) =>
        new(tempRoot, RegisteredFamilies, new ProcWorkspaceSlotProcessProbe(), TempSweepNative.LstatUid,
            TempSweepNative.EffectiveUid(), TimeProvider.System, new TempSweepLimits(), DefaultMirrorPresent, retiringRoot);

    public string TempRoot => _tempRoot;

    /// <summary>Test seam (review 12ba9a25 A-2): runs between the delete-time inventory and the validated delete.</summary>
    internal Action<string>? AfterDeleteInventory { get; set; }

    private sealed record Facts(string Name, string Path, TempFamily Family, bool IsFile, long Bytes, DateTime Newest);

    public async Task<PhoneHomeTempSweepResponse> SweepAsync(PhoneHomeTempSweepRequest request, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var execute = request.Mode == TempSweepModes.Execute;
        if ((!execute && request.Mode != TempSweepModes.DryRun) || request.MinAgeHours < 1 || request.MaxEntries < 1 || request.MaxBytes < 1)
            return new PhoneHomeTempSweepResponse(request.Mode, [], [], Complete: false, Reason: TempSweepCodes.RequestInvalid,
                Detail: "mode must be DryRun or Execute; MinAgeHours, MaxEntries and MaxBytes must be positive", Root: _tempRoot);
        if (_tempRoot.Length < 2 || !_tempRoot.StartsWith('/') && !Path.IsPathFullyQualified(_tempRoot))
            return new PhoneHomeTempSweepResponse(request.Mode, [], [], Complete: false, Reason: TempSweepCodes.RequestInvalid,
                Detail: "temp root must be an absolute path below /", Root: _tempRoot);

        var now = _clock.GetUtcNow().UtcDateTime;
        var cutoff = now - TimeSpan.FromHours(request.MinAgeHours);
        var maxEntries = Math.Min(request.MaxEntries, _limits.MaxEntries);
        var maxBytes = Math.Min(request.MaxBytes, _limits.MaxBytes);
        var kept = new List<TempSweepCandidate>();
        var acted = new List<TempSweepCandidate>();
        var unregistered = new Dictionary<string, (int Count, long Bytes, bool Complete)>(StringComparer.Ordinal);
        var unregisteredBudget = _limits.UnregisteredWalkEntries;
        var eligible = new List<Facts>();
        var keptCount = 0;

        void Keep(TempSweepCandidate candidate)
        {
            keptCount++;
            if (kept.Count < _limits.MaxReportedKept)
                kept.Add(candidate);
        }

        void Unregistered(string name, string path)
        {
            var prefix = Normalize(name);
            var (bytes, complete) = MeasureUnregistered(path, ref unregisteredBudget);
            var current = unregistered.TryGetValue(prefix, out var value) ? value : (0, 0L, true);
            unregistered[prefix] = (current.Item1 + 1, current.Item2 + bytes, current.Item3 && complete);
        }

        List<string> names;
        try
        {
            names = Directory.EnumerateFileSystemEntries(_tempRoot).Select(entry => Path.GetFileName(entry)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PhoneHomeTempSweepResponse(request.Mode, [], [], Complete: false, Detail: ex.GetType().Name, Root: _tempRoot);
        }
        names.Sort(StringComparer.Ordinal);

        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            var containerFamilies = _families.Where(f => f.Container == name).ToList();
            if (containerFamilies.Count > 0)
            {
                var containerPath = _tempRoot + "/" + name;
                var refusal = ContainerRefusal(containerPath);
                if (refusal is not null)
                {
                    Keep(new TempSweepCandidate(name, containerFamilies[0].Id, TempSweepCodes.Kept, refusal));
                    continue;
                }
                List<string> children;
                try
                {
                    children = Directory.EnumerateFileSystemEntries(containerPath).Select(entry => Path.GetFileName(entry)).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Keep(new TempSweepCandidate(name, containerFamilies[0].Id, TempSweepCodes.Kept, TempSweepCodes.UnprovenContent, Detail: ex.GetType().Name));
                    continue;
                }
                children.Sort(StringComparer.Ordinal);
                foreach (var child in children)
                {
                    var relative = name + "/" + child;
                    var family = IsDenied(child) || HasControl(child) ? null : containerFamilies.FirstOrDefault(f => f.Regex.IsMatch(relative));
                    if (family is null)
                        Unregistered(relative, containerPath + "/" + child);
                    else
                        Evaluate(relative, family);
                }
                continue;
            }

            // Review 12ba9a25 D-1: a name carrying a line break or any other control character is never a family
            // candidate, whatever a pattern says (defence in depth beside the \z anchors).
            var match = HasControl(name) ? null : _families.FirstOrDefault(f => f.Container is null && f.Regex.IsMatch(name));
            if (IsDenied(name))
            {
                // Hard deny regardless of family; a denied name is never measured or touched.
                if (match is not null)
                    Keep(new TempSweepCandidate(name, match.Id, TempSweepCodes.Kept, TempSweepCodes.Denied));
                continue;
            }
            if (match is null)
                Unregistered(name, _tempRoot + "/" + name);
            else
                Evaluate(name, match);
        }

        void Evaluate(string name, TempFamily family)
        {
            var enabled = request.Families is not null && request.Families.TryGetValue(family.Id, out var flag) ? flag : family.DefaultEnabled;
            if (!enabled)
            {
                Keep(new TempSweepCandidate(name, family.Id, TempSweepCodes.Kept, TempSweepCodes.FamilyDisabled));
                return;
            }
            if (family.RequiresMirrorAbsent)
            {
                var id8 = name[^8..];
                if (_mirrorPresent("/work/worktrees/task-" + id8) || _mirrorPresent(_retiringRoot + "/task-" + id8 + ".*"))
                {
                    Keep(new TempSweepCandidate(name, family.Id, TempSweepCodes.Kept, TempSweepCodes.MirrorPresent));
                    return;
                }
            }

            var path = _tempRoot + "/" + name;
            var walk = Walk(path);
            if (walk.Reason is not null)
            {
                Keep(new TempSweepCandidate(name, family.Id, TempSweepCodes.Kept, walk.Reason, walk.Bytes, walk.Newest, Detail: walk.Detail));
                return;
            }
            if (walk.Newest is not { } newest)
            {
                Keep(new TempSweepCandidate(name, family.Id, TempSweepCodes.Kept, TempSweepCodes.AgeUnknown, walk.Bytes));
                return;
            }
            if (newest > cutoff)
            {
                Keep(new TempSweepCandidate(name, family.Id, TempSweepCodes.Kept, TempSweepCodes.TooYoung, walk.Bytes, newest));
                return;
            }
            eligible.Add(new Facts(name, path, family, walk.IsFile, walk.Bytes, newest));
        }

        // Oldest first; the bounds and the holder scan apply right before each action.
        var removedBytes = 0L;
        var bounded = false;
        foreach (var facts in eligible.OrderBy(f => f.Newest).ThenBy(f => f.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) > _limits.TimeBudget)
            {
                Keep(Candidate(facts, TempSweepCodes.Kept, TempSweepCodes.TickBudget));
                continue;
            }
            if (bounded || acted.Count >= maxEntries || removedBytes + facts.Bytes > maxBytes)
            {
                bounded = true;
                Keep(Candidate(facts, TempSweepCodes.Kept, TempSweepCodes.TickBound));
                continue;
            }

            var scan = _holders.Scan(facts.Path);
            if (!scan.Complete)
            {
                Keep(Candidate(facts, TempSweepCodes.Kept, TempSweepCodes.HolderScanUnknown));
                continue;
            }
            if (scan.Holders.Count > 0)
            {
                Keep(Candidate(facts, TempSweepCodes.Kept, TempSweepCodes.LiveProcess) with
                {
                    Holders = scan.Holders.Select(h => new WorkspaceRetainHolder(h.Pid, h.Argv0, h.Kind)).ToList(),
                });
                continue;
            }

            if (!execute)
            {
                acted.Add(Candidate(facts, TempSweepCodes.WouldRemove, null));
                removedBytes += facts.Bytes;
                continue;
            }

            var failure = await DeleteAsync(facts, ct);
            if (failure is not null)
            {
                Keep(Candidate(facts, TempSweepCodes.Kept, failure.Value.Reason) with { Detail = failure.Value.Detail });
                continue;
            }
            acted.Add(Candidate(facts, TempSweepCodes.Removed, null));
            removedBytes += facts.Bytes;
        }

        return new PhoneHomeTempSweepResponse(
            request.Mode,
            [.. acted, .. kept],
            unregistered.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new TempSweepUnregistered(pair.Key, pair.Value.Count, pair.Value.Bytes, pair.Value.Complete)).ToList(),
            Complete: true,
            Truncated: keptCount > kept.Count,
            Removed: acted.Count,
            RemovedBytes: removedBytes,
            Root: _tempRoot);
    }

    private static TempSweepCandidate Candidate(Facts facts, string verdict, string? reason) =>
        new(facts.Name, facts.Family.Id, verdict, reason, facts.Bytes, facts.Newest);

    internal static bool IsDenied(string name) =>
        DeniedExact.Contains(name, StringComparer.OrdinalIgnoreCase)
        || DeniedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    internal static bool HasControl(string name) => name.Any(char.IsControl);

    /// <summary>The reported prefix: hex and digit runs folded, then each control character escaped as <c>\xNN</c>.</summary>
    internal static string Normalize(string name)
    {
        var value = DigitRun.Replace(HexRun.Replace(name, "<hex>"), "<n>");
        if (HasControl(value))
            value = string.Concat(value.Select(c => char.IsControl(c) ? "\\x" + ((int)c).ToString("x2", System.Globalization.CultureInfo.InvariantCulture) : c.ToString()));
        return value.Length <= 120 ? value : value[..120];
    }

    private string? ContainerRefusal(string path)
    {
        FileSystemInfo info = new DirectoryInfo(path);
        try
        {
            if (IsLink(info))
                return TempSweepCodes.Symlink;
            if (!info.Attributes.HasFlag(FileAttributes.Directory))
                return TempSweepCodes.UnprovenContent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TempSweepCodes.UnprovenContent;
        }
        var owner = _ownerUid(path);
        return owner is null ? TempSweepCodes.OwnerUnknown : owner != _runnerUid ? TempSweepCodes.ForeignOwner : null;
    }

    private sealed record WalkResult(string? Reason, long Bytes, DateTime? Newest, bool IsFile, string? Detail = null);

    /// <summary>
    /// The bounded, no-follow walk: owner of the entry and every descendant, entry and byte limits, and the
    /// newest regular-file write (the newest directory write only when there is no regular file at all).
    /// A link is never descended; a socket, FIFO or device keeps the entry.
    /// </summary>
    private WalkResult Walk(string path)
    {
        FileSystemInfo root;
        try
        {
            var attributes = File.GetAttributes(path);
            root = attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint)
                ? new DirectoryInfo(path) : new FileInfo(path);
            if (IsLink(root))
                return new WalkResult(TempSweepCodes.Symlink, 0, null, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WalkResult(TempSweepCodes.UnprovenContent, 0, null, false, ex.GetType().Name);
        }

        var entries = 0;
        long bytes = 0;
        DateTime? newestFile = null;
        DateTime? newestDirectory = null;
        var stack = new Stack<(FileSystemInfo Info, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (info, depth) = stack.Pop();
            if (++entries > _limits.InventoryEntries)
                return new WalkResult(TempSweepCodes.InventoryLimit, bytes, null, false, "entries");
            var owner = _ownerUid(info.FullName);
            if (owner is null)
                return new WalkResult(TempSweepCodes.OwnerUnknown, bytes, null, false, Relative(path, info.FullName));
            if (owner != _runnerUid)
                return new WalkResult(TempSweepCodes.ForeignOwner, bytes, null, false, Relative(path, info.FullName));
            try
            {
                if (IsLink(info))
                    continue;
                if (info is DirectoryInfo directory)
                {
                    newestDirectory = Max(newestDirectory, directory.LastWriteTimeUtc);
                    if (depth >= 128)
                        return new WalkResult(TempSweepCodes.UnprovenContent, bytes, null, false, "depth");
                    foreach (var child in directory.EnumerateFileSystemInfos())
                        stack.Push((child, depth + 1));
                    continue;
                }
                var file = (FileInfo)info;
                if (!IsRegular(file))
                    return new WalkResult(TempSweepCodes.UnprovenContent, bytes, null, false, "non-regular " + Relative(path, info.FullName));
                bytes += file.Length;
                if (bytes > _limits.InventoryBytes)
                    return new WalkResult(TempSweepCodes.InventoryLimit, bytes, null, false, "bytes");
                newestFile = Max(newestFile, file.LastWriteTimeUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new WalkResult(TempSweepCodes.UnprovenContent, bytes, null, false, ex.GetType().Name);
            }
        }

        return new WalkResult(null, bytes, newestFile ?? newestDirectory, root is FileInfo);
    }

    private (long Bytes, bool Complete) MeasureUnregistered(string path, ref int budget)
    {
        long bytes = 0;
        try
        {
            FileSystemInfo root = new FileInfo(path);
            if (IsLink(root))
                return (0, true);
            if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                return (root is FileInfo file && IsRegular(file) ? file.Length : 0, true);
            if (budget <= 0)
                return (0, false);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                if (--budget <= 0)
                    return (bytes, false);
                bytes += file.Length;
            }
            return (bytes, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (bytes, false);
        }
    }

    private async Task<(string Reason, string? Detail)?> DeleteAsync(Facts facts, CancellationToken ct)
    {
        try
        {
            if (facts.IsFile)
            {
                // A top-level regular file (rcspeed-<hex>.ps1): re-read it without following a link, then unlink it.
                var file = new FileInfo(facts.Path);
                if (IsLink(file) || !IsRegular(file) || file.Length != facts.Bytes || file.LastWriteTimeUtc != facts.Newest)
                    return (TempSweepCodes.ChangedSinceInventory, null);
                File.Delete(facts.Path);
            }
            else
            {
                var identity = RunnerWorkspaceNoFollowDelete.Identity(facts.Path);
                var entries = await RunnerWorkspaceNoFollowDelete.InventoryAsync(facts.Path, identity, ct, allowLinks: true);
                AfterDeleteInventory?.Invoke(facts.Path);
                await RunnerWorkspaceNoFollowDelete.DeleteOwnedAsync(facts.Path, identity, entries, null, null, ct, allowLinks: true);
            }
        }
        catch (RunnerWorkspaceNoFollowDelete.Refusal refusal)
        {
            var reason = refusal.Message.Contains("(inventory limit)", StringComparison.Ordinal)
                ? TempSweepCodes.InventoryLimit
                : refusal.Reason == TempSweepCodes.ChangedSinceInventory ? TempSweepCodes.ChangedSinceInventory : TempSweepCodes.UnprovenContent;
            return (reason, Tail(refusal.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return (TempSweepCodes.DeleteFailed, ex.GetType().Name + ": " + Tail(ex.Message));
        }

        return Exists(facts.Path) ? (TempSweepCodes.DeleteFailed, "entry still present") : null;
    }

    private static string Relative(string root, string path) =>
        path.Length > root.Length ? path[(root.Length + 1)..] : ".";

    private static string Tail(string text) => text.Length <= 300 ? text : text[^300..];

    private static DateTime Max(DateTime? current, DateTime value) => current is { } c && c >= value ? c : value;

    private static bool IsLink(FileSystemInfo info) =>
        info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null;

    private static bool IsRegular(FileInfo file)
    {
        if (OperatingSystem.IsWindows())
            return !file.Attributes.HasFlag(FileAttributes.Device);
        var mode = TempSweepNative.LstatKind(file.FullName);
        return mode == 0x8000;
    }

    private static bool Exists(string path)
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// A path is present unless it is proven absent; a <c>&lt;dir&gt;/&lt;leaf&gt;.*</c> probe asks whether any entry
    /// of that leaf exists in the directory (a retiring copy). Any error counts as present (fail closed).
    /// </summary>
    internal static bool DefaultMirrorPresent(string path)
    {
        try
        {
            if (path.EndsWith(".*", StringComparison.Ordinal))
            {
                var directory = Path.GetDirectoryName(path)!;
                var prefix = Path.GetFileName(path)[..^1];
                if (!Directory.Exists(directory))
                    return Exists(directory);
                return Directory.EnumerateFileSystemEntries(directory)
                    .Any(entry => Path.GetFileName(entry).StartsWith(prefix, StringComparison.Ordinal));
            }
            return Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}

/// <summary>CARD-1677 S2. lstat-equivalent owner and type (statx with AT_SYMLINK_NOFOLLOW) and the effective uid; Linux only.</summary>
internal static class TempSweepNative
{
    private const int AtFdCwd = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint TypeModeUid = 0x1 | 0x2 | 0x8;

    /// <summary>The owner uid of the path itself (never its link target); null when it cannot be read or off Linux.</summary>
    internal static int? LstatUid(string path)
    {
        if (!OperatingSystem.IsLinux())
            return null;
        if (Statx(AtFdCwd, path, AtSymlinkNoFollow, TypeModeUid, out var stat) != 0 || (stat.Mask & TypeModeUid) != TypeModeUid)
            return null;
        return stat.Uid > int.MaxValue ? null : (int)stat.Uid;
    }

    /// <summary>The file type bits (<c>S_IFMT</c>) of the path itself; -1 when unreadable or off Linux.</summary>
    internal static int LstatKind(string path)
    {
        if (!OperatingSystem.IsLinux())
            return -1;
        if (Statx(AtFdCwd, path, AtSymlinkNoFollow, TypeModeUid, out var stat) != 0 || (stat.Mask & 0x1) == 0)
            return -1;
        return stat.Mode & 0xF000;
    }

    internal static int EffectiveUid() => OperatingSystem.IsLinux() ? (int)GetEuid() : -1;

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();
}
