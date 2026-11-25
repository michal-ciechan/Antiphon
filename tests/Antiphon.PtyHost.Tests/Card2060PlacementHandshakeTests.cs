using System.IO.Pipes;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

[Category("Integration")]
[NativeLinuxOnly("Linux placement negotiation runs on the Unix request pipe.")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Card2060PlacementHandshakeTests
{
    [Test]
    public async Task OldHostGetsNoTrackedLaunch()
    {
        RequireLinux();
        var launch = TrackedLaunch();
        var sessionId = launch.VerificationBinding!.Generation.SessionId;
        var pipeName = PtyHostProtocol.PipeNameFor(sessionId);
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pipe = Server(pipeName);
        var sent = 0;
        var peer = Task.Run(async () =>
        {
            await HelloAsync(pipe, sessionId, ["verificationCustodyV1"], bound.Token);
            while (await PtyHostFraming.ReadAsync(pipe, bound.Token) is { } message)
            {
                if (message is LaunchMessage)
                {
                    sent++;
                    await PtyHostFraming.WriteAsync(pipe, new LaunchedMessage(1, DateTime.UtcNow), bound.Token);
                }
                else
                {
                    message.ShouldBeOfType<StatusRequestMessage>();
                    await ReplyStatusAsync(pipe, bound.Token);
                    return;
                }
            }
        }, bound.Token);

        try
        {
            await using var client = await PtyHostClient.ConnectAsync(pipeName, TimeSpan.FromSeconds(5), bound.Token);
            string? reason = null;
            try { await client.LaunchAsync(launch, bound.Token); }
            catch (VerificationCustodyException ex) { reason = ex.Code; }

            // The peer reads the status request as a fence after any preceding launch frame.
            await client.GetStatusAsync(bound.Token);
            await peer;
            sent.ShouldBe(0);
            reason.ShouldBe("verification_custody_unsupported_backend");
        }
        finally
        {
            bound.Cancel();
            try { await peer; } catch (OperationCanceledException) when (bound.IsCancellationRequested) { }
        }
    }

    [Test]
    public async Task CancelledClientRetiresSentStream()
    {
        RequireLinux();
        var launch = TrackedLaunch();
        var sessionId = launch.VerificationBinding!.Generation.SessionId;
        var pipeName = PtyHostProtocol.PipeNameFor(sessionId);
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(bound.Token);
        await using var pipe = Server(pipeName);
        var sent = new TaskCompletionSource<LaunchMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var followOn = 0;
        var peer = Task.Run(async () =>
        {
            await HelloAsync(pipe, sessionId, ["verificationCustodyV1", LinuxPlacementLaunch.Feature], bound.Token);
            sent.SetResult((await PtyHostFraming.ReadAsync(pipe, bound.Token)).ShouldBeOfType<LaunchMessage>());
            if (await PtyHostFraming.ReadAsync(pipe, bound.Token) is { } next)
            {
                next.ShouldBeOfType<StatusRequestMessage>();
                followOn++;
                await ReplyStatusAsync(pipe, bound.Token);
            }
        }, bound.Token);

        try
        {
            await using var client = await PtyHostClient.ConnectAsync(pipeName, TimeSpan.FromSeconds(5), bound.Token);
            var pendingLaunch = client.LaunchAsync(launch, request.Token);
            var frame = await sent.Task.WaitAsync(bound.Token);
            frame.VerificationBinding.ShouldBe(launch.VerificationBinding);
            frame.LinuxPlacementDeadline.ShouldNotBeNull();
            frame.LinuxPlacementDeadline!.Value.ShouldBeGreaterThan(0);
            var queued = client.GetStatusAsync(bound.Token);
            queued.IsCompleted.ShouldBeFalse();
            request.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () => await pendingLaunch);
            try { await queued; } catch (EndOfStreamException) { }

            await peer;
            followOn.ShouldBe(0);
        }
        finally
        {
            bound.Cancel();
            try { await peer; } catch (OperationCanceledException) when (bound.IsCancellationRequested) { }
        }
    }

    [Test]
    public void CodecsMatchFrozenProveBytes()
    {
        var binding = new LinuxPlacementBinding(
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"),
            Guid.Parse("21324354-6576-8798-a9ba-cbdcedfe0f10"),
            Guid.Parse("32435465-7687-98a9-bacb-dcedfe0f1021"),
            Guid.Parse("43546576-8798-a9ba-cbdc-edfe0f102132"));
        var challenge = Convert.FromHexString("f0e1d2c3b4a5968778695a4b3c2d1e0f");
        // Frozen RFC 4122 and network-order fixture. The challenge is synthetic.
        var expected = Convert.FromHexString(
            "f0e1d2c3b4a5968778695a4b3c2d1e0f" +
            "00112233445566778899aabbccddeeff" +
            "102132435465768798a9bacbdcedfe0f" +
            "2132435465768798a9bacbdcedfe0f10" +
            "32435465768798a9bacbdcedfe0f1021" +
            "435465768798a9bacbdcedfe0f102132" + "0102030405060708");
        LinuxPlacementAttempt.BuildProveBody(binding, 0x0102030405060708, challenge).ShouldBe(expected);
    }

    [Test]
    public void UnknownStateWithValidNumbersRefuses()
    {
        // /proc/<pid>/stat fields 3-52, including a name with a closing parenthesis.
        var fields = Enumerable.Repeat("0", 50).ToArray();
        fields[0] = "R";
        fields[1] = "1234";
        fields[19] = "654321";
        string Stat() => "4321 (shim ) helper) " + string.Join(' ', fields) + "\n";
        LinuxSpawnCapture.ParseStat(Stat()).ShouldBe((4321, 1234, 654321ul));
        fields[0] = "?";
        LinuxSpawnCapture.ParseStat(Stat()).ShouldBeNull();
    }

    [Test]
    public void PlacementUsesAttachGeneration()
    {
        var (root, tuple, frame) = ProofFixture();
        LinuxPlacementAttempt.Matches(frame, tuple, root).ShouldBeTrue();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(112), root.StartTicks + 1);
        LinuxPlacementAttempt.Matches(frame, tuple, root).ShouldBeFalse();
    }

    [Test]
    public void ObservedNamespaceMustMatch()
    {
        var (root, tuple, frame) = ProofFixture();
        LinuxPlacementAttempt.Matches(frame, tuple, root).ShouldBeTrue();
        var observed = Guid.Parse("99887766-5544-3322-1100-abcdefabcdef");
        observed.TryWriteBytes(frame.AsSpan(80, 16), bigEndian: true, out _).ShouldBeTrue();
        LinuxPlacementAttempt.Matches(frame, tuple, root).ShouldBeFalse();
    }

    private static (LinuxSpawnIdentity Root, byte[] Tuple, byte[] Frame) ProofFixture()
    {
        var data = new LinuxPlacementBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var root = new LinuxSpawnIdentity(4321, 1234, Guid.NewGuid(), 654321, DateTime.UnixEpoch);
        var tuple = LinuxPlacementAttempt.BuildProveBody(data, 0x0102030405060708, new byte[16]);
        var frame = new byte[140];
        tuple.CopyTo(frame, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(104), root.Pid);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(108), root.ParentPid);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(112), root.StartTicks);
        root.BootId.TryWriteBytes(frame.AsSpan(120, 16), bigEndian: true, out _).ShouldBeTrue();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(136), 3);
        return (root, tuple, frame);
    }

    [Test]
    public async Task PlacementIoStartsOnOrdinaryRead()
    {
        RequireLinux();
        var files = new PlacementFiles();
        await using var world = await JournalWorld.StartAsync(files);
        files.Writes.ShouldBe(0);
        (await world.ReadAsync()).State.ShouldBe(VerificationCustodyState.Tracking);
        files.Writes.ShouldBe(1);
        var placement = world.Store.ReadRecord<LinuxPlacementRecord>(world.Binding, "linux-placement.json");
        placement.ShouldNotBeNull();
        placement.Revision.ShouldBe(2);
    }

    [Test]
    public async Task FailedPlacementCannotPromoteLater()
    {
        RequireLinux();
        var files = new PlacementFiles { Refuse = true };
        await using var world = await JournalWorld.StartAsync(files);
        (await world.ReadAsync()).State.ShouldBe(VerificationCustodyState.Unknown);
        files.Writes.ShouldBe(1);
        files.Refuse = false;
        (await world.ReadAsync()).Receipt.ShouldBeNull();
        File.Exists(world.PlacementPath).ShouldBeFalse();
        files.Writes.ShouldBe(1);
    }

    [Test]
    public async Task ConflictingPlacementPreservesFirstBytes()
    {
        RequireLinux();
        await using var world = await JournalWorld.StartAsync(new PlacementFiles());
        var root = world.Store.ReadRecord<CustodyRoot>(world.Binding, "tracking.json")!;
        var identity = world.Store.ReadRecord<VerificationHostIdentity>(world.Binding, "host-identity.json")!;
        var namespaceId = world.Store.ReadRecord<LinuxNamespaceRecord>(world.Binding, "linux-namespace.json")!.NamespaceId;
        var conflicting = new LinuxPlacementRecord(2, root.Pid, root.StartTimeUtc, root.ContainerId, namespaceId,
            world.Store.StoreId, world.Binding.ExecutionId, identity.HostInstanceId, world.Binding.Generation,
            root.LinuxBootId!.Value, root.LinuxStartTicks!.Value + 1, 3);
        world.Store.WriteRecord(world.Binding, "linux-placement.json", conflicting);
        var original = await File.ReadAllBytesAsync(world.PlacementPath);
        (await Should.ThrowAsync<VerificationCustodyException>(async () => await world.ReadAsync()))
            .Code.ShouldBe("verification_custody_integrity_error");
        (await File.ReadAllBytesAsync(world.PlacementPath)).ShouldBe(original);
    }

    private sealed class PlacementFiles : IVerificationCustodyFiles
    {
        private readonly VerificationCustodyFiles _real = new();

        public bool Refuse { get; set; }
        public int Writes { get; private set; }

        public byte[]? Read(string path) => _real.Read(path);

        public void CommitImmutable(string path, byte[] bytes)
        {
            if (Path.GetFileName(path) == "linux-placement.json")
            {
                Writes++;
                if (Refuse)
                    throw new IOException("Injected placement publish refusal.");
            }
            _real.CommitImmutable(path, bytes);
        }
    }

    private sealed class AccountingTree : IPtyCustodyContainment
    {
        public Guid ContainerId { get; } = Guid.NewGuid();
        public PtyTrackedLaunch Place(PtyTrackedLaunch launch) => launch;
        public PtyCustodyPopulation ReadActive() => new(0, []);
        public PtyTerminationObservation Terminate() => throw new InvalidOperationException("An ordinary read terminated the fixture.");
    }

    private sealed class JournalWorld(HostHarness host, PtyHostClient client, VerificationCustodyStore store,
        VerificationExecutionBinding binding, string release, CancellationTokenSource bound) : IAsyncDisposable
    {
        public VerificationCustodyStore Store { get; } = store;
        public VerificationExecutionBinding Binding { get; } = binding;
        public string PlacementPath => Store.PathFor(Binding.ExecutionId, "linux-placement.json");

        internal static async Task<JournalWorld> StartAsync(PlacementFiles files)
        {
            var prior = HostCustodyJournal.LinuxNamespaceForTests.Value;
            HostCustodyJournal.LinuxNamespaceForTests.Value = Guid.NewGuid();
            HostHarness host;
            try
            {
                host = HostHarness.Start(o => o with
                {
                    PtyBackend = "inbox",
                    CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
                    CustodyBackend = VerificationCustodyBackends.LinuxCgroup,
                }, files);
            }
            finally { HostCustodyJournal.LinuxNamespaceForTests.Value = prior; }

            PtyHostClient? client = null;
            var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var release = Path.Combine(host.TempDir, "release");
            try
            {
                host.Session.UseContainmentForTests(new AccountingTree(), supplyPlacementProof: true);
                var store = new VerificationCustodyStore(host.Options.CustodyStoreRoot!, files: files);
                var snapshot = Path.Combine(host.TempDir, "snapshot");
                Directory.CreateDirectory(snapshot);
                var launch = TrackedLaunch();
                var binding = launch.VerificationBinding! with
                {
                    Generation = new(host.SessionId, launch.VerificationBinding.Generation.AcceptedStartedAt),
                    Creation = new(host.TempDir, Path.Combine(host.TempDir, ".git"), snapshot,
                        Path.Combine(host.TempDir, ".git", "worktrees", "snapshot"), "feat/placement-ledger", Guid.NewGuid()),
                    RunnerStoreId = store.StoreId,
                };
                store.Reserve(binding);
                store.WriteRecord(binding, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
                var script = Path.Combine(host.TempDir, "held.sh");
                File.WriteAllText(script, "#!/bin/sh\nwhile [ ! -f \"$1\" ]; do sleep 0.05; done\n");
                client = await PtyHostClient.ConnectAsync(host.Options.PipeName, TimeSpan.FromSeconds(5), bound.Token);
                var launched = await client.LaunchAsync(launch with
                {
                    Cwd = snapshot,
                    Args = [script, release],
                    AnsiLogPath = host.AnsiLogPath,
                    VerificationBinding = binding,
                    RunnerStoreId = store.StoreId,
                }, bound.Token);
                launched.ChildPid.ShouldBeGreaterThan(0);
                return new(host, client, store, binding, release, bound);
            }
            catch
            {
                File.WriteAllText(release, "release");
                if (client is not null) await client.DisposeAsync();
                await host.DisposeAsync();
                bound.Dispose();
                throw;
            }
        }

        public Task<VerificationCustodyStatus> ReadAsync() => client.GetCustodyAsync(Binding, bound.Token);

        public async ValueTask DisposeAsync()
        {
            await File.WriteAllTextAsync(release, "release");
            await client.DisposeAsync();
            await host.DisposeAsync();
            bound.Dispose();
        }
    }

    private static NamedPipeServerStream Server(string pipeName) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static async Task HelloAsync(NamedPipeServerStream pipe, Guid sessionId,
        IReadOnlyList<string> features, CancellationToken ct)
    {
        await pipe.WaitForConnectionAsync(ct);
        (await PtyHostFraming.ReadAsync(pipe, ct)).ShouldBeOfType<HelloMessage>();
        await PtyHostFraming.WriteAsync(pipe, new HelloAckMessage(PtyHostProtocol.Version, "fixture",
            sessionId, PtyHostStatus.WaitingForLaunch, features, Guid.NewGuid()), ct);
    }

    private static ValueTask ReplyStatusAsync(NamedPipeServerStream pipe, CancellationToken ct) =>
        PtyHostFraming.WriteAsync(pipe, new StatusReplyMessage(PtyHostStatus.WaitingForLaunch,
            null, null, 80, 24, 0, null, null), ct);

    private static LaunchMessage TrackedLaunch()
    {
        var store = Guid.NewGuid();
        var binding = new VerificationExecutionBinding(Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(Guid.NewGuid(), SessionGeneration.Normalize(DateTime.UtcNow)),
            new("/tmp", "/tmp/.git", "/tmp/snapshot", "/tmp/.git/worktrees/snapshot",
                "feat/c2060", Guid.NewGuid()), VerificationCustodyBackends.LinuxCgroup, RunnerStoreId: store);
        return new LaunchMessage("/bin/sh", [], new Dictionary<string, string>(), binding.Creation.WorktreePath,
            80, 24, 0, false, "/tmp/ansi", VerificationBinding: binding, RunnerStoreId: store);
    }

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Requires Linux placement negotiation.");
    }
}
