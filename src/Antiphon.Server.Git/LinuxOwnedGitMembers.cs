using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Antiphon.Observability;

namespace Antiphon.Server.Infrastructure.Git;

internal sealed record LinuxProcIdentity(int Pid, int ParentPid, long StartTicks, char State)
{
    internal bool Alive => State is not ('Z' or 'X' or 'x');
}

/// <summary>Only external /proc and signal I/O. Null identity means ENOENT, never unreadable.</summary>
internal interface ILinuxProcOperations
{
    bool HidesProcesses();
    IEnumerable<string> EnumerateEntries();
    LinuxProcIdentity? ReadIdentity(int pid);
    ValueTask<byte[]> ReadEnvironmentAsync(int pid, CancellationToken token);
    ValueTask SignalAsync(int pid, CancellationToken token);
}

/// <summary>
/// Cooperative invocation membership, including reparented/setsid children. Not a sandbox:
/// exec with a replacement environment can remove the marker. On legacy kernels there is
/// still a non-atomic check-to-kill PID-reuse interval; a managed Process does not pin a PID.
/// Reachable unmarked children use a freshly validated ancestry chain, never an image/path.
/// </summary>
internal sealed class LinuxOwnedGitMembers(ILinuxProcOperations io) : ILinuxOwnedGitMembers
{
    internal const int MaxEntries = 16_384;
    internal const int MaxMembers = 4_096;
    internal const int MaxEnvironmentBytes = 2 * 1024 * 1024;
    private byte[]? _needle;
    internal LinuxOwnedGitMembers() : this(new LinuxProcOperations()) { }

    public void SetMarker(string marker)
    {
        if (string.IsNullOrEmpty(marker) || marker.Contains('\0'))
            throw new ArgumentException("owned_git_marker_invalid", nameof(marker));
        if (_needle is not null) throw new InvalidOperationException("owned_git_marker_already_set");
        _needle = Encoding.UTF8.GetBytes(OwnedGitProcess.MarkerVariable + "=" + marker + "\0");
    }

    public OwnedGitRoot CaptureRoot(int pid, DateTime? startUtc)
    {
        try { return new(pid, startUtc, io.ReadIdentity(pid)?.StartTicks); }
        catch (Exception ex) when (ObservationFailure(ex)) { return new(pid, startUtc, null); }
    }

    public async ValueTask<OwnedGitMembers> ProbeAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
        CancellationToken cancellationToken)
    {
        var scan = await ScanAsync(root, deadline, cancellationToken).ConfigureAwait(false);
        return new(scan.Unknown ? OwnedGitMembershipState.Unknown
            : scan.Members.Count == 0 ? OwnedGitMembershipState.Empty : OwnedGitMembershipState.Live,
            scan.Members.Count);
    }

    public async ValueTask<int> SignalAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
        CancellationToken cancellationToken)
    {
        var scan = await ScanAsync(root, deadline, cancellationToken).ConfigureAwait(false);
        var attempted = 0;
        var signaled = new HashSet<int>();
        using var phase = AntiphonTrace.Span("git containment signal");
        foreach (var member in scan.Members)
        {
            if (!Within(deadline, cancellationToken)) break;
            try
            {
                if (!await RevalidateAsync(member, scan.Rows, root, signaled, deadline, cancellationToken).ConfigureAwait(false)) continue;
                if (!Within(deadline, cancellationToken)) break;
                attempted++;
                await io.SignalAsync(member.Identity.Pid, cancellationToken).ConfigureAwait(false);
                signaled.Add(member.Identity.Pid);
            }
            catch (Exception ex) when (ObservationFailure(ex)) { /* The next census must prove emptiness. */ }
        }
        phase?.SetTag("git.signals_attempted", attempted);
        return attempted;
    }

    private sealed record Member(LinuxProcIdentity Identity, bool Marked);
    private sealed record Scan(List<Member> Members, Dictionary<int, LinuxProcIdentity> Rows, bool Unknown);

    private async ValueTask<Scan> ScanAsync(OwnedGitRoot? root, OwnedGitDeadline deadline, CancellationToken token)
    {
        if (_needle is null) throw new InvalidOperationException("owned_git_marker_missing");
        using var phase = AntiphonTrace.Span("git containment census");
        var members = new List<Member>();
        var rows = new Dictionary<int, LinuxProcIdentity>();
        var unknown = false;
        var entries = 0;
        try
        {
            if (io.HidesProcesses()) unknown = true;
            foreach (var entry in io.EnumerateEntries())
            {
                if (!Within(deadline, token)) { unknown = true; break; }
                if (!int.TryParse(entry, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0) continue;
                if (++entries > MaxEntries) { unknown = true; break; }
                if (pid == 1 || pid == Environment.ProcessId) continue;
                try
                {
                    var identity = io.ReadIdentity(pid);
                    if (identity is null || !identity.Alive) continue;
                    if (identity.Pid != pid || identity.StartTicks < 0) { unknown = true; continue; }
                    if (root?.LinuxStartTicks is long lower && identity.StartTicks < lower) continue;
                    rows[pid] = identity;
                    var environment = await io.ReadEnvironmentAsync(pid, token).ConfigureAwait(false);
                    if (!Within(deadline, token)) { unknown = true; break; }
                    if (environment.Length > MaxEnvironmentBytes
                        || (environment.Length > 0 && environment[^1] != 0)) { unknown = true; continue; }
                    if (!Carries(environment, _needle)) continue;
                    if (!SameInstance(io.ReadIdentity(pid), identity)) { unknown = true; continue; }
                    if (members.Count == MaxMembers) { unknown = true; break; }
                    members.Add(new(identity, true));
                }
                catch (FileNotFoundException) { /* Gone, not unreadable. */ }
                catch (DirectoryNotFoundException) { }
                catch (Exception ex) when (ObservationFailure(ex)) { unknown = true; }
            }
            // This backstop is supplementary: absence of ancestry never proves Empty.
            // No chain is trusted unless the original root's raw identity is still present.
            var marked = members.Select(m => m.Identity.Pid).ToHashSet();
            foreach (var identity in rows.Values)
            {
                if (!Within(deadline, token)) { unknown = true; break; }
                if (marked.Contains(identity.Pid) || !HasRootChain(identity, rows, root)) continue;
                if (members.Count == MaxMembers) { unknown = true; break; }
                members.Add(new(identity, false));
            }
        }
        catch (Exception ex) when (ObservationFailure(ex)) { unknown = true; }
        if (!Within(deadline, token)) unknown = true;
        phase?.SetTag("git.members", members.Count);
        phase?.SetTag("git.observation", unknown ? "unknown" : members.Count == 0 ? "empty" : "live");
        return new(members, rows, unknown);
    }

    private async ValueTask<bool> RevalidateAsync(Member member, Dictionary<int, LinuxProcIdentity> rows,
        OwnedGitRoot? root, HashSet<int> signaled, OwnedGitDeadline deadline, CancellationToken token)
    {
        var identity = member.Identity;
        if (!SameInstance(io.ReadIdentity(identity.Pid), identity)) return false;
        if (member.Marked)
        {
            var environment = await io.ReadEnvironmentAsync(identity.Pid, token).ConfigureAwait(false);
            if (environment.Length > MaxEnvironmentBytes || (environment.Length > 0 && environment[^1] != 0)
                || !Carries(environment, _needle!)) return false;
        }
        else
        {
            if (!HasRootChain(identity, rows, root)) return false;
            var parent = identity;
            for (var depth = 0; depth < 64; depth++)
            {
                if (!Within(deadline, token)) return false;
                var fresh = io.ReadIdentity(parent.Pid);
                // A parent this wave just signaled may already be gone or a zombie. A
                // reused/unreadable live parent never inherits that sealed authority.
                if (fresh is null ? !signaled.Contains(parent.Pid)
                    : fresh.StartTicks != parent.StartTicks || fresh.ParentPid != parent.ParentPid
                      || (!fresh.Alive && !signaled.Contains(parent.Pid))) return false;
                if (parent.Pid == root!.Pid) break;
                if (!rows.TryGetValue(parent.ParentPid, out parent!)) return false;
            }
        }
        // Environment/ancestor reads are observable replacement boundaries too.
        return Within(deadline, token) && SameInstance(io.ReadIdentity(identity.Pid), identity);
    }

    private static bool HasRootChain(LinuxProcIdentity identity, Dictionary<int, LinuxProcIdentity> rows, OwnedGitRoot? root)
    {
        if (root?.LinuxStartTicks is not long ticks) return false;
        var current = identity;
        for (var depth = 0; depth < 64; depth++)
        {
            if (!current.Alive) return false;
            if (current.Pid == root.Pid) return current.StartTicks == ticks;
            if (!rows.TryGetValue(current.ParentPid, out var parent) || parent.StartTicks > current.StartTicks) return false;
            current = parent;
        }
        return false;
    }

    private static bool SameInstance(LinuxProcIdentity? fresh, LinuxProcIdentity prior, bool checkParent = false) =>
        fresh is not null && fresh.Pid == prior.Pid && fresh.StartTicks == prior.StartTicks && fresh.Alive
        && (!checkParent || fresh.ParentPid == prior.ParentPid);

    internal static bool Carries(ReadOnlySpan<byte> environment, ReadOnlySpan<byte> needle)
    {
        for (var offset = 0; offset < environment.Length;)
        {
            var end = environment[offset..].IndexOf((byte)0);
            if (end < 0) return false;
            if (environment.Slice(offset, end + 1).SequenceEqual(needle)) return true;
            offset += end + 1;
        }
        return false;
    }

    private static bool Within(OwnedGitDeadline deadline, CancellationToken token) =>
        !token.IsCancellationRequested && deadline.Remaining > TimeSpan.Zero;
    private static bool ObservationFailure(Exception ex) => ex is IOException or UnauthorizedAccessException
        or Win32Exception or InvalidDataException;
    public void Dispose() { }
}

internal sealed class LinuxProcOperations(Func<string, byte[]>? readEnvironment = null) : ILinuxProcOperations
{
    public IEnumerable<string> EnumerateEntries() => Directory.EnumerateDirectories("/proc").Select(Path.GetFileName)!;

    public LinuxProcIdentity? ReadIdentity(int pid) => ReadIdentityFile(pid);
    internal static LinuxProcIdentity? ReadIdentityFile(int pid)
    {
        using var phase = AntiphonTrace.Span("git containment stat");
        try
        {
            using var stream = File.OpenRead("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat");
            var buffer = new byte[4096];
            var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (count == buffer.Length) throw new InvalidDataException("owned_git_stat_bound");
            var text = Encoding.UTF8.GetString(buffer, 0, count);
            var close = text.LastIndexOf(')');
            if (close < 0) throw new InvalidDataException("owned_git_stat_invalid");
            var fields = text[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length <= 19 || fields[0].Length != 1
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent)
                || !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
                throw new InvalidDataException("owned_git_stat_invalid");
            return new(pid, parent, ticks, fields[0][0]);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public async ValueTask<byte[]> ReadEnvironmentAsync(int pid, CancellationToken token)
    {
        if (readEnvironment is not null) return readEnvironment("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/environ");
        using var phase = AntiphonTrace.Span("git containment environ");
        await using var stream = new FileStream("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/environ",
            FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        // One extra byte detects truncation. Never read the rest of an oversized environment.
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (result.Length <= LinuxOwnedGitMembers.MaxEnvironmentBytes)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0,
                Math.Min(buffer.Length, LinuxOwnedGitMembers.MaxEnvironmentBytes + 1 - (int)result.Length)), token).ConfigureAwait(false);
            if (count == 0) break;
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    public bool HidesProcesses() => HidesOtherProcesses();
    internal static bool HidesOtherProcesses()
    {
        try
        {
            var found = false;
            foreach (var line in File.ReadLines("/proc/self/mountinfo"))
            {
                var fields = line.Split(' ');
                var separator = Array.IndexOf(fields, "-");
                if (fields.Length < 5 || fields[4] != "/proc" || separator < 0 || separator + 3 >= fields.Length) continue;
                found = true;
                foreach (var option in fields[5].Split(',').Concat(fields[separator + 3].Split(',')))
                    if (option.StartsWith("hidepid=", StringComparison.Ordinal) && option is not ("hidepid=0" or "hidepid=off")) return true;
            }
            return !found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    public ValueTask SignalAsync(int pid, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (pid <= 1 || pid == Environment.ProcessId) throw new InvalidOperationException("owned_git_pid_refused");
        if (Kill(pid, 9) != 0 && Marshal.GetLastPInvokeError() != 3) // ESRCH is already gone.
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return ValueTask.CompletedTask;
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
