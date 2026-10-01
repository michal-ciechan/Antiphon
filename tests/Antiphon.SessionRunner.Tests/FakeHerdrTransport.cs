using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner.Tests;

/// <summary>A short, private, instance-owned endpoint for one test's Herdr listener.</summary>
internal sealed class FakeHerdrEndpoint : IAsyncDisposable
{
    private int _claimed;
    private readonly string? _directory;
    private readonly string? _marker;
    private readonly string? _leaseId;
    internal static AsyncLocal<Func<string, NativeFileIdentity.Identity?>?> ReclaimIdentityOverride { get; } = new();
    internal static AsyncLocal<Func<string, bool>?> ReclaimLinkOverride { get; } = new();
    private static readonly object ReclaimGate = new();
    public string Path { get; }
    public string Session { get; }
    public bool OwnsDirectory => _directory is not null;

    public FakeHerdrEndpoint(string? session = null)
    {
        Session = session ?? $"antiphon-herdr-test-{Guid.NewGuid():N}";
        if (OperatingSystem.IsWindows())
        {
            Path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "herdr", "sessions", Session, "herdr.sock");
            return;
        }

        ReclaimDeadLeases();
        _leaseId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var directory = $"/tmp/ah-{_leaseId}";
        var path = System.IO.Path.Combine(directory, "s");
        if (Encoding.UTF8.GetByteCount(path) + 1 >= SocketPathLimit)
            throw new IOException($"Herdr test endpoint exceeds portable sun_path limit: {path}");
        // mkdir is exclusive at the OS boundary. A failed allocation never unlinks a foreign path.
        if (Mkdir(directory, 448) != 0)
            throw new IOException($"Herdr test endpoint directory could not be reserved: {directory}");
        _directory = directory;
        _marker = System.IO.Path.Combine(directory, "owner");
        lock (ReclaimGate)
        {
            WriteMarker(new LeaseMarker(2, "c801", _leaseId, path, Environment.ProcessId,
                OwnerStartIdentity(), NamespaceIdentity(), GetEuid(), 0, 0, 0, 0, 0));
            File.SetUnixFileMode(_marker, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        Path = path;
    }

    public void Claim()
    {
        if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0)
            throw new IOException($"Herdr test endpoint already has a listener: {Path}");
    }

    public void Release() => Volatile.Write(ref _claimed, 0);

    public void RecordBoundSocket(NativeFileIdentity.Identity identity)
    {
        if (_marker is null || _leaseId is null) return;
        lock (ReclaimGate)
            WriteMarker(new LeaseMarker(2, "c801", _leaseId, Path, Environment.ProcessId,
                OwnerStartIdentity(), NamespaceIdentity(), GetEuid(),
                identity.Device, identity.Inode, identity.Mode, identity.ChangeSeconds, identity.ChangeNanoseconds));
    }

    private void WriteMarker(LeaseMarker marker)
    {
        using var file = new FileStream(_marker!, File.Exists(_marker) ? FileMode.Truncate : FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        using (var writer = new StreamWriter(file, Encoding.ASCII, leaveOpen: true))
            writer.Write(string.Join('\n', new[] {
                marker.Schema.ToString(), marker.Fixture, marker.LeaseId, marker.SocketPath,
                marker.OwnerPid.ToString(), marker.OwnerStartTicks.ToString(), marker.HostPidNamespace,
                marker.OwnerUid.ToString(),
                marker.SocketDevice.ToString(), marker.SocketInode.ToString(), marker.SocketMode.ToString(),
                marker.SocketChangeSeconds.ToString(), marker.SocketChangeNanoseconds.ToString()
            }));
        file.Flush(flushToDisk: true);
    }

    private static LeaseMarker? ReadMarker(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var fields = reader.ReadToEnd().Split('\n');
        if (fields.Length != 13 || !int.TryParse(fields[0], out var schema)
            || !int.TryParse(fields[4], out var pid) || !long.TryParse(fields[5], out var started)
            || !uint.TryParse(fields[7], out var uid)
            || !ulong.TryParse(fields[8], out var device) || !ulong.TryParse(fields[9], out var inode)
            || !uint.TryParse(fields[10], out var mode) || !long.TryParse(fields[11], out var seconds)
            || !long.TryParse(fields[12], out var nanoseconds)) return null;
        return new LeaseMarker(schema, fields[1], fields[2], fields[3], pid, started,
            fields[6], uid, device, inode, mode, seconds, nanoseconds);
    }

    private sealed record LeaseMarker(int Schema, string Fixture, string LeaseId, string SocketPath,
        int OwnerPid, long OwnerStartTicks, string HostPidNamespace, uint OwnerUid, ulong SocketDevice, ulong SocketInode,
        uint SocketMode, long SocketChangeSeconds, long SocketChangeNanoseconds);

    private static string NamespaceIdentity()
    {
        try { return new FileInfo("/proc/self/ns/pid").LinkTarget ?? Environment.MachineName; }
        catch { return Environment.MachineName; }
    }

    internal static AsyncLocal<int?> SocketPathLimitOverride { get; } = new();
    private static int SocketPathLimit => SocketPathLimitOverride.Value ?? 104;

    private static long OwnerStartIdentity()
    {
        if (!OperatingSystem.IsLinux()) return Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        return ReadProcStartTicks(Environment.ProcessId) ?? throw new IOException("Cannot identify fixture owner start time.");
    }

    // /proc/<pid>/stat field 2 is parenthesized and can contain spaces or ')' characters.
    private static long? ReadProcStartTicks(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var close = stat.LastIndexOf(") ", StringComparison.Ordinal);
            if (close < 0 || !int.TryParse(stat.AsSpan(0, stat.IndexOf(' ')), out var parsedPid) || parsedPid != pid)
                return null;
            var fields = stat[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return fields.Length > 19 && long.TryParse(fields[19], out var ticks) && ticks > 0 ? ticks : null;
        }
        catch { return null; }
    }

    private static bool OwnerDefinitelyDead(LeaseMarker marker)
    {
        if (OperatingSystem.IsLinux())
        {
            var observed = ReadProcStartTicks(marker.OwnerPid);
            if (observed.HasValue)
            {
                if (observed.Value == marker.OwnerStartTicks) return false;
                // A reused PID with unreadable or different credentials is uncertain.
                return ReadProcUid(marker.OwnerPid) == marker.OwnerUid;
            }
            // Unreadable proc data is uncertainty. Only an absent PID confirms death.
            try { using var process = Process.GetProcessById(marker.OwnerPid); return false; }
            catch (ArgumentException) { return true; }
            catch { return false; }
        }
        try { using var process = Process.GetProcessById(marker.OwnerPid); return process.StartTime.ToUniversalTime().Ticks != marker.OwnerStartTicks; }
        catch (ArgumentException) { return true; }
        catch { return false; }
    }

    private static uint? ReadProcUid(int pid)
    {
        try
        {
            var line = File.ReadLines($"/proc/{pid}/status").FirstOrDefault(x => x.StartsWith("Uid:", StringComparison.Ordinal));
            var parts = line?[4..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return parts is { Length: >= 2 } && uint.TryParse(parts[1], out var uid) ? uid : null;
        }
        catch { return null; }
    }

    private static bool IsLink(string path) => ReclaimLinkOverride.Value?.Invoke(path) ?? new FileInfo(path).LinkTarget is not null;
    private static bool TryReclaimIdentity(string path, out NativeFileIdentity.Identity identity)
    {
        if (ReclaimIdentityOverride.Value is { } read)
        {
            var result = read(path);
            identity = result ?? default;
            return result.HasValue;
        }
        return NativeFileIdentity.TryRead(path, out identity);
    }

    public static void ReclaimDeadLeases()
    {
        if (OperatingSystem.IsWindows()) return;
        lock (ReclaimGate) ReclaimDeadLeasesCore();
    }

    private static void ReclaimDeadLeasesCore()
    {
        var candidates = Directory.EnumerateDirectories("/tmp", "ah-*").Take(64);
        var inspected = 0;
        foreach (var directory in candidates)
        {
            if (inspected >= 16) break;
            if (!Regex.IsMatch(System.IO.Path.GetFileName(directory), "^ah-[0-9a-f]{32}$", RegexOptions.CultureInvariant)) continue;
            inspected++;
            var markerPath = System.IO.Path.Combine(directory, "owner");
            var socketPath = System.IO.Path.Combine(directory, "s");
            try
            {
                if (IsLink(directory) || IsLink(markerPath) || IsLink(socketPath))
                    continue;
                if (!NativeFileIdentity.TryRead(directory, out var directoryIdentity)
                    || (directoryIdentity.Mode & 0xF000) != 0x4000
                    || (OperatingSystem.IsLinux() && directoryIdentity.Uid != GetEuid())) continue;
                using var locked = new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var marker = ReadMarker(locked);
                if (marker is null || marker.Schema != 2 || marker.Fixture != "c801"
                    || marker.LeaseId != System.IO.Path.GetFileName(directory)[3..]
                    || marker.SocketPath != socketPath || marker.OwnerPid <= 0
                    || marker.HostPidNamespace != NamespaceIdentity() || marker.OwnerUid != GetEuid()) continue;
                if (Directory.GetFileSystemEntries(directory).OrderBy(x => x, StringComparer.Ordinal)
                    .SequenceEqual(new[] { markerPath, socketPath }.OrderBy(x => x, StringComparer.Ordinal)) == false)
                    continue;
                if (!TryReclaimIdentity(socketPath, out var identity)
                    || identity.Device != marker.SocketDevice || identity.Inode != marker.SocketInode
                    || identity.Mode != marker.SocketMode
                    || (OperatingSystem.IsLinux() && identity.Uid != marker.OwnerUid)
                    || (identity.Mode & 0xF000) != 0xC000
                    || identity.ChangeSeconds != marker.SocketChangeSeconds
                    || identity.ChangeNanoseconds != marker.SocketChangeNanoseconds)
                    continue;
                if (!OwnerDefinitelyDead(marker)) continue;
                // Recheck the path identities under the exclusive marker lock.
                if (IsLink(socketPath) || !TryReclaimIdentity(socketPath, out var still) || still != identity
                    || !NativeFileIdentity.TryRead(directory, out var stillDirectory) || stillDirectory != directoryIdentity) continue;
                File.Delete(socketPath);
                locked.Dispose();
                File.Delete(markerPath);
                Directory.Delete(directory);
            }
            catch (Exception) { /* unknown or changed residue is preserved */ }
        }
    }

    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
    private static extern int Mkdir(string path, int mode);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

    public ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _claimed) != 0)
            throw new InvalidOperationException("Dispose the Herdr listener before its endpoint lease.");
        if (_directory is not null)
        {
            try
            {
                if (NativeFileIdentity.TryRead(Path, out _))
                    throw new IOException($"Herdr test socket is still bound: {Path}");
                if (_marker is not null) File.Delete(_marker);
                Directory.Delete(_directory);
            }
            catch (IOException ex) { System.Diagnostics.Trace.TraceWarning($"Herdr endpoint cleanup failed: {ex}"); }
            catch (UnauthorizedAccessException ex) { System.Diagnostics.Trace.TraceWarning($"Herdr endpoint cleanup failed: {ex}"); }
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>Native listener. One accepted stream belongs to the caller until it closes.</summary>
internal sealed class FakeHerdrTransport(FakeHerdrEndpoint endpoint) : IAsyncDisposable
{
    public Exception? BindFailure { get; set; }
    public Exception? AcceptFailure { get; set; }
    // A one-shot constructor fault lets the listener's Windows pipe-capacity policy
    // be tested on Unix without creating a named pipe on this host.
    public Func<IOException?>? PipeCreationFailure { get; set; }
    internal static bool IsPipeInstancesBusy(IOException ex) => ex.HResult == unchecked((int)0x800700E7);
    private Socket? _socket;
    private NamedPipeServerStream? _pendingPipe;
    private bool _bound;
    private bool _ownsSocketPath;
    private NativeFileIdentity.Identity _boundIdentity;
    public string EndpointPath => endpoint.Path;

    public void Bind()
    {
        endpoint.Claim();
        try
        {
            if (BindFailure is { } bindFailure) throw bindFailure;
            if (!OperatingSystem.IsWindows())
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    socket.Bind(new UnixDomainSocketEndPoint(endpoint.Path));
                    _ownsSocketPath = true;
                    if (!NativeFileIdentity.TryRead(endpoint.Path, out _boundIdentity))
                        throw new IOException($"Cannot identify bound Herdr test socket: {endpoint.Path}");
                    File.SetUnixFileMode(endpoint.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    if (!NativeFileIdentity.TryRead(endpoint.Path, out _boundIdentity))
                        throw new IOException($"Cannot identify protected Herdr test socket: {endpoint.Path}");
                    endpoint.RecordBoundSocket(_boundIdentity);
                    socket.Listen(4);
                    _socket = socket;
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            _bound = true;
        }
        catch
        {
            endpoint.Release();
            throw;
        }
    }

    public async Task<Stream> AcceptAsync(CancellationToken ct, Action listening)
    {
        if (!_bound) throw new InvalidOperationException("Herdr test listener is not bound.");
        if (PipeCreationFailure?.Invoke() is { } pipeCreationFailure)
            throw pipeCreationFailure;
        if (AcceptFailure is { } acceptFailure)
        {
            listening();
            throw acceptFailure;
        }
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeServerStream(endpoint.Path, PipeDirection.InOut, 4,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            _pendingPipe = pipe;
            listening();
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                _pendingPipe = null;
                return pipe;
            }
            catch
            {
                _pendingPipe = null;
                await pipe.DisposeAsync();
                throw;
            }
        }
        listening();
        var accepted = await _socket!.AcceptAsync(ct);
        return new NetworkStream(accepted, ownsSocket: true);
    }

    public ValueTask DisposeAsync()
    {
        try { DisposeCore(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"Herdr transport cleanup failed: {ex}"); }
        finally { if (_bound) endpoint.Release(); _bound = false; }
        return ValueTask.CompletedTask;
    }

    private void DisposeCore()
    {
        _pendingPipe?.Dispose();
        string? protectedReplacement = null;
        if (_ownsSocketPath && NativeFileIdentity.TryRead(endpoint.Path, out var beforeClose)
            && beforeClose != _boundIdentity)
        {
            protectedReplacement = endpoint.Path + ".replacement-" + Guid.NewGuid().ToString("N");
            File.Move(endpoint.Path, protectedReplacement);
        }
        try { _socket?.Dispose(); }
        finally
        {
            if (protectedReplacement is not null)
            {
                if (NativeFileIdentity.TryRead(endpoint.Path, out _))
                    throw new IOException($"Herdr replacement endpoint changed during disposal: {endpoint.Path}");
                File.Move(protectedReplacement, endpoint.Path);
            }
        }
        if (_ownsSocketPath && NativeFileIdentity.TryRead(endpoint.Path, out var current)
            && (current.Mode & 0xF000) == 0xC000
            && current == _boundIdentity)
        {
            File.Delete(endpoint.Path);
            _ownsSocketPath = false;
        }
    }
}

internal static class NativeFileIdentity
{
    internal readonly record struct Identity(ulong Device, ulong Inode, uint Mode, long ChangeSeconds, long ChangeNanoseconds, uint Uid = 0);

    internal static bool TryRead(string path, out Identity identity)
    {
        identity = default;
        if (OperatingSystem.IsWindows()) return File.Exists(path);
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if (LStat(path, buffer) != 0) return false;
            var device = OperatingSystem.IsMacOS()
                ? (uint)Marshal.ReadInt32(buffer, 0)
                : (ulong)Marshal.ReadInt64(buffer, 0);
            var mode = (uint)Marshal.ReadInt32(buffer, OperatingSystem.IsMacOS() ? 4 : 24);
            var changedSeconds = OperatingSystem.IsMacOS() ? 0 : Marshal.ReadInt64(buffer, 104);
            var changedNanoseconds = OperatingSystem.IsMacOS() ? 0 : Marshal.ReadInt64(buffer, 112);
            var uid = OperatingSystem.IsLinux() ? (uint)Marshal.ReadInt32(buffer, 28) : 0;
            identity = new Identity(device, (ulong)Marshal.ReadInt64(buffer, 8), mode,
                changedSeconds, changedNanoseconds, uid);
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, IntPtr buffer);
}
