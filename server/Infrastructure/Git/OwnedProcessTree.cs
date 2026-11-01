using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>One row of a process table. Start ticks are
/// <see cref="RepositoryChildJournal.TryStartTicks"/> (UTC <see cref="DateTime.Ticks"/>),
/// the same identity the child journal records at spawn. Null ticks mean the start
/// time could not be read. CARD-1619 <c>LinuxProcessIdentity</c> is not used: it is
/// Linux-only, lives in the runner, and its ticks are /proc jiffies plus a boot id.</summary>
internal readonly record struct ProcessTableRow(int Pid, int ParentPid, long? StartTicks);

/// <summary>The owned root as captured from its process handle at spawn.</summary>
internal readonly record struct CapturedProcessIdentity(int Pid, long StartTicks);

/// <summary>One pid the planner will signal, with the snapshot ticks the kill must re-check.</summary>
internal readonly record struct PlannedKill(int Pid, long StartTicks, string Reason);

/// <summary>A process table read. <see cref="FailureReason"/> set means the table was not read.</summary>
internal readonly record struct ProcessSnapshot(IReadOnlyList<ProcessTableRow> Rows, string? FailureReason)
{
    public static ProcessSnapshot Failure(string reason) => new([], reason);
}

/// <summary>The ordered kill list, or a block reason and no pids.
/// <see cref="Truncated"/> means an acceptable descendant past
/// <see cref="OwnedProcessTree.MaxDescendants"/> was not taken.</summary>
internal sealed record KillPlan(IReadOnlyList<PlannedKill> Kills, string? BlockReason, bool Truncated = false)
{
    public static KillPlan Blocked(string reason) => new([], reason);
}

/// <summary>
/// Kills an owned git root and the descendants captured before the first signal.
/// A child is accepted only when its start ticks are at or after its parent's.
/// Windows does not rewrite a parent pid after the parent exits, so a reused pid
/// must not pull in processes that started earlier. The kill list is sealed
/// before the first signal. Fallback when the table cannot be read: log
/// <c>snapshot_failed</c> and kill only the owned root handle
/// (<c>fallback=owned_root_only</c>), never an unguarded pid walk.
/// The clock is sampled before each snapshot. A row whose start is later
/// than that sample is unreadable and is not walked.
/// The sealed list is signalled first. After those processes are reaped, one
/// later read accepts a process that was not in that seal, whose parent was
/// signalled, and whose start is at or after that parent's. A process
/// reparented outside the signalled set is left alone.
/// </summary>
internal static class OwnedProcessTree
{
    /// <summary>Kill list length, including the root. A larger closure is not signalled.</summary>
    internal const int MaxDescendants = 64;

    /// <summary>One budget for a signalled wave to exit. TerminateProcess does not reap
    /// before it returns, so a later snapshot must wait before it decides who is gone.</summary>
    internal const int ReapBoundMilliseconds = 2000;

    /// <summary>What the kill did, for the journal release line.</summary>
    internal readonly record struct TreeKillReport(
        int SealedDescendants,
        int Signalled,
        int StillAlive,
        bool ReapWaited,
        int Followers,
        int FollowKilled,
        int FollowStillAlive,
        long WaitMilliseconds)
    {
        public static TreeKillReport None { get; } = new(0, 0, 0, false, 0, 0, 0, 0);
    }

    /// <summary>Table reads before the first signal. A later pass picks up a child that
    /// appeared while the table was being read. The list is then sealed.</summary>
    internal const int MaxSnapshotPasses = 4;

    /// <summary>Parent-link candidates whose start ticks are read. The rest are omitted.</summary>
    internal const int MaxCandidateRows = 256;

    internal static string KillFailed(int pid, string reason) =>
        $"repository_child_tree_kill kill_failed pid={pid} reason={reason}";

    internal static string SnapshotFailed(int pid, string reason) =>
        $"repository_child_tree_kill snapshot_failed pid={pid} reason={reason} fallback=owned_root_only event=repository_child_tree_kill_snapshot_failed";

    internal static KillPlan Plan(IReadOnlyList<ProcessTableRow> table, CapturedProcessIdentity root, int serverPid, int ownerPid)
    {
        if (root.Pid <= 1) return KillPlan.Blocked("root_unusable");
        if (root.Pid == serverPid) return KillPlan.Blocked("root_is_server");
        if (root.Pid == ownerPid) return KillPlan.Blocked("root_is_owner");

        var byPid = new Dictionary<int, ProcessTableRow>();
        foreach (var row in table)
        {
            if (row.Pid <= 1 || row.Pid == row.ParentPid) continue;
            byPid[row.Pid] = row;
        }

        // A different start time means this pid is now a stranger. Do not walk it.
        // A missing or unreadable row is not a stranger: Windows orphans keep the
        // dead pid as their parent, and the captured ticks still identify the edge.
        if (byPid.TryGetValue(root.Pid, out var rootRow)
            && rootRow.StartTicks is long live
            && live != root.StartTicks)
            return KillPlan.Blocked("root_identity_mismatch");

        var children = new Dictionary<int, List<ProcessTableRow>>();
        foreach (var row in byPid.Values)
        {
            if (!children.TryGetValue(row.ParentPid, out var list))
                children[row.ParentPid] = list = new List<ProcessTableRow>();
            list.Add(row);
        }
        foreach (var list in children.Values)
            list.Sort(static (left, right) => left.Pid.CompareTo(right.Pid));

        var kills = new List<PlannedKill> { new(root.Pid, root.StartTicks, "root") };
        var accepted = new Dictionary<int, long> { [root.Pid] = root.StartTicks };
        var pending = new Queue<int>();
        pending.Enqueue(root.Pid);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!children.TryGetValue(current, out var list)) continue;
            var parentTicks = accepted[current];
            foreach (var child in list)
            {
                if (child.Pid == root.Pid || child.Pid == serverPid || child.Pid == ownerPid)
                    continue;
                if (accepted.ContainsKey(child.Pid)) continue;
                // Unreadable start time: do not include this pid and do not walk past it.
                if (child.StartTicks is not long childTicks) continue;
                // Started before its parent: the recorded parent pid is stale.
                if (childTicks < parentTicks) continue;
                if (kills.Count >= MaxDescendants) return new KillPlan(kills, null, true);
                accepted[child.Pid] = childTicks;
                kills.Add(new PlannedKill(child.Pid, childTicks, "descendant"));
                pending.Enqueue(child.Pid);
            }
        }
        return new KillPlan(kills, null);
    }

    /// <summary>Read the table until it stops growing, then signal that sealed list.
    /// After those processes are reaped, one later read accepts a process that was
    /// not in the seal, whose parent was signalled, and whose start is at or after
    /// that parent's. <paramref name="readStartTicks"/> and <paramref name="kill"/> are
    /// the executor's injected seams. Production opens one process per pid and kills
    /// that same object. <paramref name="clock"/> is sampled before each snapshot (UTC).
    /// A later start is unreadable. <paramref name="observeWiredPids"/> sees the server
    /// and owner pids the caller passed in. <paramref name="waitForExit"/> returns true
    /// when that pid has exited; null skips the reap. <paramref name="beginReap"/> starts
    /// a fresh wait budget for one wave.</summary>
    internal static TreeKillReport Execute(
        Func<ProcessSnapshot> readTable,
        Func<int, long?> readStartTicks,
        Action<int> kill,
        Action killOwnedRoot,
        CapturedProcessIdentity root,
        int serverPid,
        int ownerPid,
        bool rootAlreadyExited,
        Action<string> log,
        Func<DateTime>? clock = null,
        Action<int, int>? observeWiredPids = null,
        Func<int, bool>? waitForExit = null,
        Action? beginReap = null)
    {
        observeWiredPids?.Invoke(serverPid, ownerPid);
        clock ??= static () => DateTime.UtcNow;
        if (rootAlreadyExited)
        {
            log(KillFailed(root.Pid, "root_gone"));
            return TreeKillReport.None;
        }

        var accepted = new List<PlannedKill>();
        var seen = new HashSet<int>();
        var snapshotted = new HashSet<int>();
        var truncated = false;
        for (var pass = 0; pass < MaxSnapshotPasses; pass++)
        {
            // Before the parent walk and the later start-tick reads. A pid reused
            // inside this snapshot has a start after this sample and is dropped.
            var snapshotTicks = UtcTicks(clock());
            var snapshot = readTable();
            if (snapshot.FailureReason is not null)
            {
                log(SnapshotFailed(root.Pid, snapshot.FailureReason));
                killOwnedRoot();
                return TreeKillReport.None;
            }

            foreach (var row in snapshot.Rows)
                if (row.Pid > 1) snapshotted.Add(row.Pid);

            var plan = Plan(MaskRowsStartedAfter(snapshot.Rows, snapshotTicks), root, serverPid, ownerPid);
            if (plan.BlockReason is not null)
            {
                log(KillFailed(root.Pid, plan.BlockReason));
                if (plan.BlockReason is "root_identity_mismatch") killOwnedRoot();
                return TreeKillReport.None;
            }

            if (plan.Truncated) truncated = true;
            var added = 0;
            foreach (var entry in plan.Kills)
            {
                if (!seen.Add(entry.Pid)) continue;
                if (accepted.Count >= MaxDescendants)
                {
                    truncated = true;
                    break;
                }
                accepted.Add(entry);
                added++;
            }
            if (added == 0) break;
        }

        foreach (var entry in accepted)
            snapshotted.Add(entry.Pid);

        var signalled = Signal(accepted, readStartTicks, kill, log);
        var descendants = 0;
        foreach (var entry in accepted)
            if (entry.Pid != root.Pid) descendants++;
        var outcome = $"repository_child_tree_kill root={root.Pid} descendants={descendants} killed={string.Join(',', signalled)}";
        if (truncated)
            outcome += $" truncated=true count={descendants}";
        log(outcome);

        var parentTicks = new Dictionary<int, long>();
        var signalledSet = new HashSet<int>(signalled);
        foreach (var entry in accepted)
            if (signalledSet.Contains(entry.Pid)) parentTicks[entry.Pid] = entry.StartTicks;

        List<int> stillAlive;
        long waited;
        if (waitForExit is null)
        {
            stillAlive = [];
            waited = 0;
            log(ReapLine(root.Pid, 0, stillAlive, false));
        }
        else
        {
            (stillAlive, waited) = Reap(signalled, waitForExit, beginReap, log);
            log(ReapLine(root.Pid, waited, stillAlive, true));
        }

        var followTicks = UtcTicks(clock());
        var followSnapshot = readTable();
        string? followFailure = null;
        List<PlannedKill> followers;
        if (followSnapshot.FailureReason is not null)
        {
            followers = [];
            followFailure = followSnapshot.FailureReason;
        }
        else
        {
            followers = PlanLateChildren(
                MaskRowsStartedAfter(followSnapshot.Rows, followTicks),
                parentTicks,
                snapshotted,
                serverPid,
                ownerPid,
                accepted.Count);
        }

        var followSignalled = followers.Count == 0
            ? []
            : Signal(followers, readStartTicks, kill, log);
        List<int> followAlive = [];
        long followWaited = 0;
        if (waitForExit is not null && followSignalled.Count > 0)
            (followAlive, followWaited) = Reap(followSignalled, waitForExit, beginReap, log);
        log(FollowLine(root.Pid, followers.Count, followSignalled, followAlive, followFailure));
        return new TreeKillReport(
            descendants,
            signalled.Count,
            stillAlive.Count,
            waitForExit is not null,
            followers.Count,
            followSignalled.Count,
            followAlive.Count,
            waited + followWaited);
    }

    internal static TreeKillReport Kill(
        Process root,
        Func<ProcessSnapshot>? readTable = null,
        Action<int, int>? observeWiredPids = null,
        Func<int, bool>? waitForExit = null,
        Action<string>? log = null)
    {
        int rootId;
        try { rootId = root.Id; }
        catch (InvalidOperationException) { return TreeKillReport.None; }

        Action<string> write = log ?? (static line => Console.Error.WriteLine(line));
        if (!RepositoryChildJournal.TryStartTicks(root, out var rootTicks))
        {
            write(KillFailed(rootId, "root_identity_unreadable"));
            return TreeKillReport.None;
        }

        var rootExited = false;
        if (OperatingSystem.IsLinux())
        {
            try { rootExited = root.HasExited; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                write(KillFailed(rootId, ex.GetType().Name));
                return TreeKillReport.None;
            }
        }

        Process? held = null;
        var heldPid = 0;
        var disposeHeld = false;
        var retained = new List<RetainedProcess>();
        var reapClock = new Stopwatch();
        try
        {
            return Execute(
                readTable ?? (() => CaptureSnapshot(rootId)),
                ReadHeldTicks,
                KillHeld,
                () => KillOwnedHandle(root, rootId, rootTicks),
                new CapturedProcessIdentity(rootId, rootTicks),
                Environment.ProcessId,
                Environment.ProcessId,
                rootExited,
                write,
                observeWiredPids: observeWiredPids,
                waitForExit: waitForExit ?? WaitRetained,
                beginReap: waitForExit is null ? BeginReap : null);
        }
        finally
        {
            ReleaseHeld();
            foreach (var item in retained)
                if (item.Dispose) item.Process.Dispose();
        }

        long? ReadHeldTicks(int pid)
        {
            ReleaseHeld();
            if (pid == rootId)
            {
                held = root;
                disposeHeld = false;
            }
            else
            {
                try { held = Process.GetProcessById(pid); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    held = null;
                    return null;
                }
                disposeHeld = true;
            }
            heldPid = pid;
            try
            {
                if (held.HasExited) return null;
                return RepositoryChildJournal.TryStartTicks(held, out var ticks) ? ticks : null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        void KillHeld(int pid)
        {
            if (held is null || heldPid != pid) throw new InvalidOperationException("identity_not_held");
            var process = held;
            var dispose = disposeHeld;
            held = null;
            disposeHeld = false;
            try
            {
                // StartTime is cached on this Process. A second read compares that cache
                // with itself. The live check is ReadHeldTicks, which opened this object.
                if (!process.HasExited)
                    process.Kill(entireProcessTree: false);
            }
            finally
            {
                // Keep the handle until the follow snapshot. Disposing it lets Windows reuse the pid.
                retained.Add(new RetainedProcess(process, dispose, pid));
            }
        }

        void ReleaseHeld()
        {
            if (disposeHeld) held?.Dispose();
            held = null;
            disposeHeld = false;
        }

        bool WaitRetained(int pid)
        {
            for (var i = retained.Count - 1; i >= 0; i--)
            {
                if (retained[i].Pid != pid) continue;
                var remaining = ReapBoundMilliseconds - (int)reapClock.ElapsedMilliseconds;
                if (remaining < 0) remaining = 0;
                return retained[i].Process.WaitForExit(remaining);
            }
            // No handle: the pid was already gone when the tick check ran.
            return true;
        }

        void BeginReap() => reapClock.Restart();
    }

    /// <summary>The same walk production uses. A test supplies this as <see cref="Kill"/>'s table.</summary>
    internal static ProcessSnapshot CaptureSnapshot(int rootPid) => ReadSnapshot(rootPid);

    private readonly record struct RetainedProcess(Process Process, bool Dispose, int Pid);

    private static List<int> Signal(
        IReadOnlyList<PlannedKill> entries,
        Func<int, long?> readStartTicks,
        Action<int> kill,
        Action<string> log)
    {
        var signalled = new List<int>();
        foreach (var entry in entries)
        {
            long? live;
            try { live = readStartTicks(entry.Pid); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                log(KillFailed(entry.Pid, ex.GetType().Name));
                continue;
            }
            if (live != entry.StartTicks)
            {
                log(KillFailed(entry.Pid, live is null ? "unreadable" : "reused"));
                continue;
            }
            try
            {
                kill(entry.Pid);
                signalled.Add(entry.Pid);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                var reason = ex.Message is "reused" or "unreadable" or "identity_not_held" ? ex.Message : ex.GetType().Name;
                log(KillFailed(entry.Pid, reason));
            }
        }
        return signalled;
    }

    /// <summary>Wait out one signalled wave. An empty wave does not start the budget.
    /// A refused wait counts as still alive.</summary>
    private static (List<int> Alive, long Waited) Reap(
        IReadOnlyList<int> signalled,
        Func<int, bool> waitForExit,
        Action? beginReap,
        Action<string> log)
    {
        if (signalled.Count == 0) return ([], 0);
        beginReap?.Invoke();
        var clock = Stopwatch.StartNew();
        var alive = new List<int>();
        foreach (var pid in signalled)
        {
            try
            {
                if (!waitForExit(pid)) alive.Add(pid);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                alive.Add(pid);
                log(KillFailed(pid, ex.GetType().Name));
            }
        }
        return (alive, clock.ElapsedMilliseconds);
    }

    /// <summary>One pass over processes that were not in the sealed snapshots.
    /// The parent must be a pid this wave signalled, and the start must be at or after that parent's.</summary>
    private static List<PlannedKill> PlanLateChildren(
        IReadOnlyList<ProcessTableRow> table,
        IReadOnlyDictionary<int, long> signalledTicks,
        HashSet<int> snapshotted,
        int serverPid,
        int ownerPid,
        int alreadyAccepted)
    {
        var kills = new List<PlannedKill>();
        if (signalledTicks.Count == 0 || alreadyAccepted >= MaxDescendants) return kills;

        var children = new Dictionary<int, List<ProcessTableRow>>();
        foreach (var row in table)
        {
            if (row.Pid <= 1 || row.Pid == row.ParentPid) continue;
            if (!children.TryGetValue(row.ParentPid, out var list))
                children[row.ParentPid] = list = new List<ProcessTableRow>();
            list.Add(row);
        }
        foreach (var list in children.Values)
            list.Sort(static (left, right) => left.Pid.CompareTo(right.Pid));

        var parents = new List<int>(signalledTicks.Count);
        foreach (var pid in signalledTicks.Keys) parents.Add(pid);
        parents.Sort();

        var accepted = new HashSet<int>();
        foreach (var parentPid in parents)
        {
            if (!children.TryGetValue(parentPid, out var list)) continue;
            var parentTicks = signalledTicks[parentPid];
            foreach (var child in list)
            {
                if (child.Pid == serverPid || child.Pid == ownerPid) continue;
                if (snapshotted.Contains(child.Pid) || accepted.Contains(child.Pid)) continue;
                if (child.StartTicks is not long childTicks) continue;
                // Started before its parent: the recorded parent pid is stale.
                if (childTicks < parentTicks) continue;
                if (alreadyAccepted + kills.Count >= MaxDescendants) return kills;
                accepted.Add(child.Pid);
                kills.Add(new PlannedKill(child.Pid, childTicks, "follower"));
            }
        }
        return kills;
    }

    private static string FormatIds(IReadOnlyList<int> pids) =>
        pids.Count == 0 ? "none" : string.Join(',', pids);

    private static string ReapLine(int rootPid, long waited, IReadOnlyList<int> alive, bool reapWaited) =>
        $"repository_child_tree_kill_reap root={rootPid} waited_ms={waited} still_alive={FormatIds(alive)} reap={(reapWaited ? "waited" : "skipped")}";

    private static string FollowLine(int rootPid, int followers, IReadOnlyList<int> killed, IReadOnlyList<int> alive, string? reason)
    {
        var line = $"repository_child_tree_kill_follow root={rootPid} followers={followers} killed={FormatIds(killed)} still_alive={FormatIds(alive)}";
        if (reason is not null) line += $" reason={reason}";
        return line;
    }

    private static void KillOwnedHandle(Process root, int rootId, long rootTicks) =>
        KillOwnedHandle(root, rootId, rootTicks, static (process, entireProcessTree) => process.Kill(entireProcessTree: entireProcessTree));

    /// <summary>Snapshot-failure fallback. <paramref name="kill"/> is the owned-handle
    /// seam: production passes <c>false</c> so only this handle is signalled.</summary>
    internal static void KillOwnedHandle(Process root, int rootId, long rootTicks, Action<Process, bool> kill)
    {
        try
        {
            if (root.HasExited) return;
            // StartTime is cached on the owned handle, so a second read cannot see a
            // reused pid. HasExited is the guard: the pid stays reserved until the runtime reaps it.
            kill(root, false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine(KillFailed(rootId, ex.GetType().Name));
        }
    }

    /// <summary>A start after the pre-snapshot clock is unreadable. The walk stops at that row.</summary>
    private static IReadOnlyList<ProcessTableRow> MaskRowsStartedAfter(IReadOnlyList<ProcessTableRow> rows, long snapshotTicks)
    {
        ProcessTableRow[]? masked = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.StartTicks is long ticks && ticks > snapshotTicks)
            {
                masked ??= rows.ToArray();
                masked[i] = new ProcessTableRow(row.Pid, row.ParentPid, null);
            }
        }
        return masked ?? rows;
    }

    private static long UtcTicks(DateTime time) =>
        time.Kind == DateTimeKind.Utc ? time.Ticks : time.ToUniversalTime().Ticks;

    private static ProcessSnapshot ReadSnapshot(int rootPid)
    {
        var parents = new Dictionary<int, int>();
        var failure = TryReadParents(parents);
        if (failure is not null) return ProcessSnapshot.Failure(failure);

        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, parent) in parents)
        {
            if (pid <= 1 || pid == parent) continue;
            if (!children.TryGetValue(parent, out var list))
                children[parent] = list = new List<int>();
            list.Add(pid);
        }

        var rows = new List<ProcessTableRow>();
        var seen = new HashSet<int>();
        var pending = new Queue<int>();
        pending.Enqueue(rootPid);
        while (pending.Count > 0 && rows.Count < MaxCandidateRows)
        {
            var current = pending.Dequeue();
            if (!seen.Add(current)) continue;
            var parent = parents.TryGetValue(current, out var recorded) ? recorded : 0;
            rows.Add(new ProcessTableRow(current, parent, ReadTicks(current)));
            if (!children.TryGetValue(current, out var kids)) continue;
            foreach (var kid in kids)
                if (!seen.Contains(kid)) pending.Enqueue(kid);
        }
        return new ProcessSnapshot(rows, null);
    }

    private static long? ReadTicks(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return null;
            return RepositoryChildJournal.TryStartTicks(process, out var ticks) ? ticks : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    private static string? TryReadParents(Dictionary<int, int> parents)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return TryWindowsParents(parents);
            if (OperatingSystem.IsLinux()) return TryLinuxParents(parents);
            return "unsupported_os";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return ex.GetType().Name; }
    }

    private static string? TryLinuxParents(Dictionary<int, int> parents)
    {
        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(entry);
            if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            try
            {
                var stat = File.ReadAllText(Path.Combine(entry, "stat"));
                var end = stat.LastIndexOf(')');
                if (end < 0 || end + 2 >= stat.Length) continue;
                var fields = stat.AsSpan(end + 2);
                var stateEnd = fields.IndexOf(' ');
                if (stateEnd < 0 || stateEnd + 1 >= fields.Length) continue;
                var rest = fields[(stateEnd + 1)..];
                var ppidEnd = rest.IndexOf(' ');
                var ppidText = ppidEnd < 0 ? rest : rest[..ppidEnd];
                if (int.TryParse(ppidText, NumberStyles.None, CultureInfo.InvariantCulture, out var ppid))
                    parents[pid] = ppid;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { }
        }
        return null;
    }

    private static string? TryWindowsParents(Dictionary<int, int> parents)
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcess, 0);
        if (snapshot == InvalidHandle)
            return "invalid_handle:win32_" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
        try
        {
            var entry = new ProcessEntry { dwSize = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry))
                return "process32_first_failed:win32_" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
            do
            {
                var pid = unchecked((int)entry.th32ProcessID);
                var parent = unchecked((int)entry.th32ParentProcessID);
                if (pid > 1) parents[pid] = parent;
                entry.dwSize = (uint)Marshal.SizeOf<ProcessEntry>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally { _ = CloseHandle(snapshot); }
        return null;
    }

    private const uint SnapshotProcess = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
