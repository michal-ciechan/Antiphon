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

/// <summary>The ordered kill list, or a block reason and no pids.</summary>
internal sealed record KillPlan(IReadOnlyList<PlannedKill> Kills, string? BlockReason)
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
/// </summary>
internal static class OwnedProcessTree
{
    /// <summary>Kill list length, including the root. A larger closure is not signalled.</summary>
    internal const int MaxDescendants = 64;

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
                if (kills.Count >= MaxDescendants) return new KillPlan(kills, null);
                accepted[child.Pid] = childTicks;
                kills.Add(new PlannedKill(child.Pid, childTicks, "descendant"));
                pending.Enqueue(child.Pid);
            }
        }
        return new KillPlan(kills, null);
    }

    /// <summary>Read the table until it stops growing, then signal that sealed list.
    /// <paramref name="readStartTicks"/> and <paramref name="kill"/> are the executor's
    /// injected seams. Production opens one process per pid and kills that same object.</summary>
    internal static void Execute(
        Func<ProcessSnapshot> readTable,
        Func<int, long?> readStartTicks,
        Action<int> kill,
        Action killOwnedRoot,
        CapturedProcessIdentity root,
        int serverPid,
        int ownerPid,
        bool rootAlreadyExited,
        Action<string> log)
    {
        if (rootAlreadyExited)
        {
            log(KillFailed(root.Pid, "root_gone"));
            return;
        }

        var accepted = new List<PlannedKill>();
        var seen = new HashSet<int>();
        for (var pass = 0; pass < MaxSnapshotPasses; pass++)
        {
            var snapshot = readTable();
            if (snapshot.FailureReason is not null)
            {
                log(SnapshotFailed(root.Pid, snapshot.FailureReason));
                killOwnedRoot();
                return;
            }

            var plan = Plan(snapshot.Rows, root, serverPid, ownerPid);
            if (plan.BlockReason is not null)
            {
                log(KillFailed(root.Pid, plan.BlockReason));
                if (plan.BlockReason is "root_identity_mismatch") killOwnedRoot();
                return;
            }

            var added = 0;
            foreach (var entry in plan.Kills)
            {
                if (!seen.Add(entry.Pid)) continue;
                if (accepted.Count >= MaxDescendants) break;
                accepted.Add(entry);
                added++;
            }
            if (added == 0) break;
        }

        var signalled = new List<int>();
        foreach (var entry in accepted)
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

        var descendants = 0;
        foreach (var entry in accepted)
            if (entry.Pid != root.Pid) descendants++;
        log($"repository_child_tree_kill root={root.Pid} descendants={descendants} killed={string.Join(',', signalled)}");
    }

    internal static void Kill(Process root)
    {
        int rootId;
        try { rootId = root.Id; }
        catch (InvalidOperationException) { return; }

        if (!RepositoryChildJournal.TryStartTicks(root, out var rootTicks))
        {
            Console.Error.WriteLine(KillFailed(rootId, "root_identity_unreadable"));
            return;
        }

        var rootExited = false;
        if (OperatingSystem.IsLinux())
        {
            try { rootExited = root.HasExited; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Console.Error.WriteLine(KillFailed(rootId, ex.GetType().Name));
                return;
            }
        }

        Process? held = null;
        var heldPid = 0;
        var disposeHeld = false;
        long? heldTicks = null;
        try
        {
            Execute(
                () => ReadSnapshot(rootId),
                ReadHeldTicks,
                KillHeld,
                () => KillOwnedHandle(root, rootId, rootTicks),
                new CapturedProcessIdentity(rootId, rootTicks),
                Environment.ProcessId,
                Environment.ProcessId,
                rootExited,
                static line => Console.Error.WriteLine(line));
        }
        finally { ReleaseHeld(); }

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
                    heldTicks = null;
                    return null;
                }
                disposeHeld = true;
            }
            heldPid = pid;
            try
            {
                if (held.HasExited) { heldTicks = null; return null; }
                heldTicks = RepositoryChildJournal.TryStartTicks(held, out var ticks) ? ticks : null;
                return heldTicks;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                heldTicks = null;
                return null;
            }
        }

        void KillHeld(int pid)
        {
            if (held is null || heldPid != pid) throw new InvalidOperationException("identity_not_held");
            if (held.HasExited) return;
            if (!RepositoryChildJournal.TryStartTicks(held, out var live) || live != heldTicks)
                throw new InvalidOperationException("reused");
            held.Kill(entireProcessTree: false);
        }

        void ReleaseHeld()
        {
            if (disposeHeld) held?.Dispose();
            held = null;
            disposeHeld = false;
            heldTicks = null;
        }
    }

    private static void KillOwnedHandle(Process root, int rootId, long rootTicks)
    {
        try
        {
            if (root.HasExited) return;
            if (!RepositoryChildJournal.TryStartTicks(root, out var live) || live != rootTicks) return;
            root.Kill(entireProcessTree: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine(KillFailed(rootId, ex.GetType().Name));
        }
    }

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
