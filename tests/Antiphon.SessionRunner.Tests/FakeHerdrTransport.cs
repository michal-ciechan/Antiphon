using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner.Tests;

/// <summary>A short, private, instance-owned endpoint for one test's Herdr listener.</summary>
internal sealed class FakeHerdrEndpoint : IAsyncDisposable
{
    private int _claimed;
    private readonly string? _directory;
    private readonly string? _marker;
    private readonly string? _leaseId;
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

        lock (ReclaimGate) ReclaimDeadLeases();
        _leaseId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var directory = $"/tmp/ah-{_leaseId}";
        var path = System.IO.Path.Combine(directory, "s");
        if (Encoding.UTF8.GetByteCount(path) + 1 >= 104)
            throw new IOException($"Herdr test endpoint exceeds portable sun_path limit: {path}");
        // mkdir is exclusive at the OS boundary. A failed allocation never unlinks a foreign path.
        if (Mkdir(directory, 448) != 0)
            throw new IOException($"Herdr test endpoint directory could not be reserved: {directory}");
        _directory = directory;
        _marker = System.IO.Path.Combine(directory, "owner");
        WriteMarker(new LeaseMarker(1, "c801", _leaseId, path, Environment.ProcessId,
            Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, NamespaceIdentity(), 0, 0));
        File.SetUnixFileMode(_marker, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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
        WriteMarker(new LeaseMarker(1, "c801", _leaseId, Path, Environment.ProcessId,
            Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, NamespaceIdentity(),
            identity.Device, identity.Inode));
    }

    private void WriteMarker(LeaseMarker marker)
    {
        using var file = new FileStream(_marker!, File.Exists(_marker) ? FileMode.Truncate : FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, marker);
        file.Flush(flushToDisk: true);
    }

    private sealed record LeaseMarker(int Schema, string Fixture, string LeaseId, string SocketPath,
        int OwnerPid, long OwnerStartTicks, string HostPidNamespace, ulong SocketDevice, ulong SocketInode);

    private static string NamespaceIdentity()
    {
        try { return new FileInfo("/proc/self/ns/pid").LinkTarget ?? Environment.MachineName; }
        catch { return Environment.MachineName; }
    }

    public static void ReclaimDeadLeases()
    {
        if (OperatingSystem.IsWindows()) return;
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
                if (new DirectoryInfo(directory).LinkTarget is not null || new FileInfo(markerPath).LinkTarget is not null)
                    continue;
                using var locked = new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var marker = JsonSerializer.Deserialize<LeaseMarker>(locked);
                if (marker is null || marker.Schema != 1 || marker.Fixture != "c801"
                    || marker.LeaseId != System.IO.Path.GetFileName(directory)[3..]
                    || marker.SocketPath != socketPath || marker.OwnerPid <= 0
                    || marker.HostPidNamespace != NamespaceIdentity()) continue;
                if (Directory.GetFileSystemEntries(directory).OrderBy(x => x, StringComparer.Ordinal)
                    .SequenceEqual(new[] { markerPath, socketPath }.OrderBy(x => x, StringComparer.Ordinal)) == false)
                    continue;
                if (!NativeFileIdentity.TryRead(socketPath, out var identity)
                    || identity.Device != marker.SocketDevice || identity.Inode != marker.SocketInode)
                    continue;
                if (new FileInfo(socketPath).LinkTarget is not null) continue;
                try
                {
                    using var owner = Process.GetProcessById(marker.OwnerPid);
                    if (owner.StartTime.ToUniversalTime().Ticks == marker.OwnerStartTicks) continue;
                }
                catch (ArgumentException) { /* positively absent */ }
                catch (InvalidOperationException) { /* exited */ }
                catch { continue; }
                // Recheck the path identities under the exclusive marker lock.
                if (!NativeFileIdentity.TryRead(socketPath, out var still) || still != identity) continue;
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

    public ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _claimed) != 0)
            throw new InvalidOperationException("Dispose the Herdr listener before its endpoint lease.");
        if (_directory is not null)
        {
            if (NativeFileIdentity.TryRead(Path, out _))
                throw new IOException($"Herdr test socket is still bound: {Path}");
            if (_marker is not null) File.Delete(_marker);
            Directory.Delete(_directory);
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>Native listener. One accepted stream belongs to the caller until it closes.</summary>
internal sealed class FakeHerdrTransport(FakeHerdrEndpoint endpoint) : IAsyncDisposable
{
    public Exception? BindFailure { get; set; }
    public Exception? AcceptFailure { get; set; }
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
                    endpoint.RecordBoundSocket(_boundIdentity);
                    File.SetUnixFileMode(endpoint.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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
        _pendingPipe?.Dispose();
        _socket?.Dispose();
        if (_ownsSocketPath && NativeFileIdentity.TryRead(endpoint.Path, out var current)
            && current == _boundIdentity)
        {
            File.Delete(endpoint.Path);
            _ownsSocketPath = false;
        }
        if (_bound) endpoint.Release();
        _bound = false;
        return ValueTask.CompletedTask;
    }
}

internal static class NativeFileIdentity
{
    internal readonly record struct Identity(ulong Device, ulong Inode);

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
            identity = new Identity(device, (ulong)Marshal.ReadInt64(buffer, 8));
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int LStat(string path, IntPtr buffer);
}
