using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Agents.Pty;

/// <summary>Identity captured from the original Porta attachment before the shim can exec.</summary>
public sealed record LinuxSpawnIdentity(int Pid, int ParentPid, Guid BootId, ulong StartTicks, DateTime StartTimeUtc);

public sealed record LinuxPlacementBinding(Guid StoreId, Guid ExecutionId, Guid HostInstanceId,
    Guid ContainerId, Guid NamespaceId);

public sealed record LinuxPlacementProof(LinuxPlacementBinding Binding, LinuxSpawnIdentity Root, uint ControllerMask);

/// <summary>One listener, one peer, one bounded release decision owned by the original host.</summary>
public sealed class LinuxPlacementAttempt : IDisposable
{
    private const byte Prove = 1, Placed = 2, Go = 3, Releasing = 4;
    private const int TupleSize = 104, ProofSize = 140, ChallengeSize = 16, HeaderSize = 4;
    public const uint V1Controllers = 3;

    private readonly LinuxPlacementBinding _binding;
    private readonly Socket _listener;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _budget;
    private readonly CancellationTokenRegistration _cancel;
    private readonly object _gate = new();
    private bool _decided;
    private bool _exchangeStarted;
    private bool _goMayHaveReachedPeer;
    private LinuxPlacementProof? _released;

    public long Deadline { get; }
    public CancellationToken Token => _budget.Token;

    public LinuxPlacementAttempt(LinuxPlacementBinding binding, long deadline, CancellationToken ct,
        TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _binding = binding;
        Deadline = deadline;
        var now = _clock.GetTimestamp();
        if (deadline <= now || ct.IsCancellationRequested)
            throw new OperationCanceledException("verification_custody_placement_expired", ct);
        if (binding.StoreId == Guid.Empty || binding.ExecutionId == Guid.Empty ||
            binding.HostInstanceId == Guid.Empty || binding.ContainerId == Guid.Empty ||
            binding.NamespaceId == Guid.Empty)
            throw Refused();

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            _listener.Bind(new UnixDomainSocketEndPoint("\0antiphon-custody-v1-" + binding.ContainerId.ToString("D")));
            _listener.Listen(1);
        }
        catch
        {
            _listener.Dispose();
            throw;
        }

        _budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _budget.CancelAfter(_clock.GetElapsedTime(now, deadline));
        _cancel = Token.Register(() => { lock (_gate) _decided = true; });
    }

    public void RequireOpen()
    {
        lock (_gate)
            if (!CanProceed())
                throw new OperationCanceledException("verification_custody_placement_expired", Token);
    }

    internal async Task ExchangeAsync(PortaPtySession session, LinuxSpawnIdentity root,
        Func<int, int, Guid, ulong, CancellationToken, Task>? beforeGo,
        CancellationToken ct)
    {
        lock (_gate)
        {
            if (_exchangeStarted)
                throw Refused();
            _exchangeStarted = true;
        }

        try
        {
            RequireOpen();
            using var peer = await _listener.AcceptAsync(ct);
            _listener.Dispose();
            if (!PeerMatches(peer.SafeHandle, root.Pid) || root.ParentPid != Environment.ProcessId ||
                session.ReadExitObservation() is not null)
                throw Refused();

            var tuple = BuildProveBody(_binding, Deadline, RandomNumberGenerator.GetBytes(ChallengeSize));
            await SendFrameAsync(peer, Prove, tuple, ct);
            var attestation = await ReceiveFrameAsync(peer, Placed, ProofSize, ct);
            if (!Matches(attestation, tuple, root) || session.ReadExitObservation() is not null)
                throw Refused();

            if (beforeGo is not null)
                await beforeGo(root.Pid, root.ParentPid, root.BootId, root.StartTicks, ct).WaitAsync(ct);

            lock (_gate)
            {
                if (!CanProceed() || session.ReadExitObservation() is not null)
                    throw Refused();
                // A failed async write cannot distinguish a fragmented write from a delivered GO.
                _goMayHaveReachedPeer = true;
            }

            var challenge = tuple.AsMemory(0, ChallengeSize);
            await SendFrameAsync(peer, Go, challenge, ct);
            var ack = await ReceiveFrameAsync(peer, Releasing, ChallengeSize, ct);
            if (!ack.AsSpan().SequenceEqual(challenge.Span))
                throw Refused();

            lock (_gate)
                if (CanProceed())
                    _released = new(_binding, root, V1Controllers);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            lock (_gate)
            {
                _decided = true;
                if (!_goMayHaveReachedPeer)
                    throw;
            }
        }
        finally
        {
            _listener.Dispose();
        }
    }

    /// <summary>Called under host admission only after the required running manifest has published.</summary>
    public LinuxPlacementProof? Commit()
    {
        lock (_gate)
        {
            var proof = CanProceed() ? _released : null;
            _decided = true;
            return proof;
        }
    }

    private bool CanProceed() => !_decided && !Token.IsCancellationRequested && _clock.GetTimestamp() < Deadline;

    internal static byte[] BuildProveBody(LinuxPlacementBinding binding, long deadline, ReadOnlySpan<byte> challenge)
    {
        var body = new byte[TupleSize];
        challenge.CopyTo(body);
        binding.StoreId.TryWriteBytes(body.AsSpan(16, 16), bigEndian: true, out _);
        binding.ExecutionId.TryWriteBytes(body.AsSpan(32, 16), bigEndian: true, out _);
        binding.HostInstanceId.TryWriteBytes(body.AsSpan(48, 16), bigEndian: true, out _);
        binding.ContainerId.TryWriteBytes(body.AsSpan(64, 16), bigEndian: true, out _);
        binding.NamespaceId.TryWriteBytes(body.AsSpan(80, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(96), deadline);
        return body;
    }

    private static bool Matches(ReadOnlySpan<byte> attestation, ReadOnlySpan<byte> tuple, LinuxSpawnIdentity root) =>
        attestation[..TupleSize].SequenceEqual(tuple) &&
        BinaryPrimitives.ReadInt32BigEndian(attestation[104..]) == root.Pid &&
        BinaryPrimitives.ReadInt32BigEndian(attestation[108..]) == root.ParentPid &&
        BinaryPrimitives.ReadUInt64BigEndian(attestation[112..]) == root.StartTicks &&
        new Guid(attestation[120..136], bigEndian: true) == root.BootId &&
        BinaryPrimitives.ReadUInt32BigEndian(attestation[136..]) == V1Controllers;

    private static async Task SendFrameAsync(Socket peer, byte kind, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        var frame = new byte[HeaderSize + bytes.Length];
        frame[0] = 1;
        frame[1] = kind;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), (ushort)bytes.Length);
        bytes.CopyTo(frame.AsMemory(HeaderSize));

        for (var sent = 0; sent < frame.Length;)
        {
            var count = await peer.SendAsync(frame.AsMemory(sent), SocketFlags.None, ct);
            if (count == 0) throw Refused();
            sent += count;
        }
    }

    private static async Task<byte[]> ReceiveFrameAsync(Socket peer, byte kind, int length, CancellationToken ct)
    {
        var header = new byte[HeaderSize];
        await ReceiveExactlyAsync(peer, header, ct);
        if (header[0] != 1 || header[1] != kind || BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2)) != length)
            throw Refused();
        var body = new byte[length];
        await ReceiveExactlyAsync(peer, body, ct);
        return body;
    }

    private static async Task ReceiveExactlyAsync(Socket peer, Memory<byte> target, CancellationToken ct)
    {
        while (!target.IsEmpty)
        {
            var count = await peer.ReceiveAsync(target, SocketFlags.None, ct);
            if (count == 0) throw Refused();
            target = target[count..];
        }
    }

    private static bool PeerMatches(SafeSocketHandle handle, int pid)
    {
        uint size = (uint)Marshal.SizeOf<PeerCredentials>();
        return GetSocketOption(handle, 1, 17, out var credentials, ref size) == 0 &&
            size == Marshal.SizeOf<PeerCredentials>() && credentials.Uid == 0 && credentials.Pid == pid;
    }

    private static IOException Refused() => new("verification_custody_placement_refused");

    [StructLayout(LayoutKind.Sequential)]
    private struct PeerCredentials
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [DllImport("libc", EntryPoint = "getsockopt")]
    private static extern int GetSocketOption(SafeSocketHandle fd, int level, int option,
        out PeerCredentials value, ref uint size);

    public void Dispose()
    {
        lock (_gate) _decided = true;
        _listener.Dispose();
        _cancel.Dispose();
        _budget.Dispose();
    }
}
