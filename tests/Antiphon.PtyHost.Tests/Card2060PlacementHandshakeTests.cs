using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Card2060PlacementHandshakeTests
{
    [Test]
    [NativeLinuxOnly("Linux placement negotiation runs on the Unix request pipe.")]
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
    [NativeLinuxOnly("Linux placement cancellation retires the Unix request pipe.")]
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
    [NativeLinuxOnly("The original Linux PTY host materializes the committed placement.")]
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
    [NativeLinuxOnly("The original Linux PTY host reads the committed placement.")]
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
    [NativeLinuxOnly("The original Linux PTY host publishes the placement integrity error.")]
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

    // ---- CARD-2060 round 2 (Review 6e6013b4 D-1): reachable host and stage decisions ----

    // PC-5. The host authenticates the exact spawned child before it issues any challenge.
    [Test]
    [NativeLinuxOnly("Linux placement authenticates the stage with Unix peer credentials.")]
    public async Task HostChecksSpawnPeer()
    {
        RequireLinux();
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Control: the fixture peer is the tracked root, so the host challenges it.
        using (var world = HostAttemptWorld.Start(Environment.ProcessId))
        {
            var exchange = world.ExchangeAsync(bound.Token);
            using var peer = world.Connect();
            var prove = await ReadFrameAsync(peer, bound.Token);
            prove.ShouldNotBeNull();
            prove.Value.Kind.ShouldBe(ProveKind);
            peer.Dispose();
            await Should.ThrowAsync<IOException>(async () => await exchange);
        }

        // The same UID with a distinct peer PID gets no challenge.
        using (var world = HostAttemptWorld.Start(Environment.ProcessId + 1))
        {
            var exchange = world.ExchangeAsync(bound.Token);
            using var peer = world.Connect();
            var first = await ReadFrameAsync(peer, bound.Token);
            peer.Dispose();
            (first is { Kind: ProveKind }).ShouldBeFalse("an unauthenticated peer received PROVE");
            first.ShouldBeNull();
            (await Should.ThrowAsync<IOException>(async () => await exchange))
                .Message.ShouldBe("verification_custody_placement_refused");
            world.Attempt.Commit().ShouldBeNull();
        }
    }

    // Q2 host validator fault fixture. A PLACED that differs from the original capture, the
    // challenge or both v1 controllers withholds GO; the stage can never be released.
    [Test]
    [NativeLinuxOnly("Linux placement validates PLACED from a Unix socket peer.")]
    public async Task MismatchedPlacedWithholdsGo()
    {
        RequireLinux();
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var clock = new FakeTimeProvider();

        // Control: the unmodified attestation is released and committed.
        using (var world = HostAttemptWorld.Start(Environment.ProcessId, clock))
        {
            var exchange = world.ExchangeAsync(bound.Token);
            using var peer = world.Connect();
            var go = await world.ProveAndPlaceAsync(peer, _ => { }, bound.Token);
            go.ShouldNotBeNull();
            go.Value.Kind.ShouldBe(GoKind);
            await WriteFrameAsync(peer, ReleasingKind, go.Value.Body, bound.Token);
            await exchange;
            world.Attempt.Commit().ShouldNotBeNull();
        }

        (string Case, Action<byte[]> Mutate)[] cases =
        [
            ("challenge", frame => frame[0] ^= 0x01),
            ("namespace", frame => frame[80] ^= 0x01),
            ("pid", frame => BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(104), Environment.ProcessId + 1)),
            ("parent", frame => BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(108), 1)),
            ("start ticks", frame => BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(112), HostAttemptWorld.Ticks + 1)),
            ("boot", frame => frame[120] ^= 0x01),
            ("single v1 controller", frame => BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(136), 1)),
            ("delayed wrong challenge", frame =>
            {
                // PROVE has arrived and PLACED is still held by the peer. Cross the short
                // timeout without exhausting the host's overall placement budget.
                clock.Advance(TimeSpan.FromMilliseconds(2));
                frame[0] ^= 0x01;
            }),
        ];
        foreach (var (label, mutate) in cases)
        {
            using var world = HostAttemptWorld.Start(Environment.ProcessId, clock);
            var exchange = world.ExchangeAsync(bound.Token);
            using var peer = world.Connect();
            var next = await world.ProveAndPlaceAsync(peer, mutate, bound.Token);
            peer.Dispose();
            (next is { Kind: GoKind }).ShouldBeFalse(label + ": GO released a mismatched PLACED");
            next.ShouldBeNull(label);
            (await Should.ThrowAsync<IOException>(async () => await exchange, label))
                .Message.ShouldBe("verification_custody_placement_refused", label);
            world.Attempt.Commit().ShouldBeNull(label);
        }
    }

    // PC-22. GO may have started the payload. Missing or mismatched RELEASING leaves placement uncommitted.
    [Test]
    [NativeLinuxOnly("Linux placement releases the stage over a Unix socket.")]
    public async Task LostAckRetainsLaunchedChild()
    {
        RequireLinux();
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Control: a timely RELEASING commits the validated offer.
        using (var world = HostAttemptWorld.Start(Environment.ProcessId))
        {
            var exchange = world.ExchangeAsync(bound.Token);
            using var peer = world.Connect();
            var go = await world.ProveAndPlaceAsync(peer, _ => { }, bound.Token);
            await WriteFrameAsync(peer, ReleasingKind, go!.Value.Body, bound.Token);
            await exchange;
            world.Attempt.Commit().ShouldNotBeNull();
        }

        foreach (var sendWrongChallenge in new[] { false, true })
        {
            var label = sendWrongChallenge ? "wrong RELEASING challenge" : "missing RELEASING";
            using var world = HostAttemptWorld.Start(Environment.ProcessId);
            var exchange = world.ExchangeAsync(bound.Token);
            using var peer = world.Connect();
            var go = await world.ProveAndPlaceAsync(peer, _ => { }, bound.Token);
            go.ShouldNotBeNull();
            go.Value.Kind.ShouldBe(GoKind);
            if (sendWrongChallenge)
            {
                var wrongChallenge = go.Value.Body;
                wrongChallenge[0] ^= 0x01;
                await WriteFrameAsync(peer, ReleasingKind, wrongChallenge, bound.Token);
            }
            peer.Dispose();
            // The launch reply stays LaunchedMessage: the exchange does not become a start failure.
            await Should.NotThrowAsync(async () => await exchange, label);
            world.Attempt.Commit().ShouldBeNull(label);
        }
    }

    // PC-4. Each v1 hierarchy independently refuses an extra member.
    [Test]
    [NativeLinuxOnly("The placement stage fixture uses Linux abstract sockets and procfs.")]
    public async Task MixedFreezerCannotAttest()
    {
        RequireLinux();
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await using (var control = await StageDriver.StartAsync(bound.Token))
        {
            await control.ProveAsync(bound.Token);
            await ExpectFrameAsync(control.Peer, PlacedKind, bound.Token);
            control.Peer.Shutdown(SocketShutdown.Send);
            (await control.JoinAsync(bound.Token)).ExitCode.ShouldBe(3);
        }

        foreach (var controller in new[] { "freezer", "pids" })
        {
            await using var stage = await StageDriver.StartAsync(bound.Token);
            await stage.ProveAsync(bound.Token, mixedController: controller);
            var reply = await ReadFrameAsync(stage.Peer, bound.Token);
            stage.Peer.Shutdown(SocketShutdown.Send);
            var joined = await stage.JoinAsync(bound.Token);
            (reply is { Kind: PlacedKind }).ShouldBeFalse($"a mixed {controller} tree produced PLACED");
            reply.ShouldBeNull();
            joined.ExitCode.ShouldBe(3);
            joined.Exec.ShouldBeEmpty();
            joined.Stderr.ShouldContain("C2060_CUSTODY_REFUSED PlacementExchange");
        }
    }

    // PC-13. The stage holds the payload after PLACED until it reads GO.
    [Test]
    [NativeLinuxOnly("The placement stage fixture uses Linux abstract sockets and procfs.")]
    public async Task StageWaitsForGo()
    {
        RequireLinux();
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Decisive arm first: withhold GO while the stage holds the original PID.
        await using (var stage = await StageDriver.StartAsync(bound.Token))
        {
            await stage.ProveAsync(bound.Token);
            await ExpectFrameAsync(stage.Peer, PlacedKind, bound.Token);
            stage.Peer.Shutdown(SocketShutdown.Send);
            var after = await ReadUntilEofAsync(stage.Peer, bound.Token);
            var joined = await stage.JoinAsync(bound.Token);
            after.Count(frame => frame.Kind == ReleasingKind).ShouldBe(0);
            joined.Exec.ShouldBeEmpty();
            joined.ExitCode.ShouldBe(3);
        }

        // Control: the same stage releases on a timely GO.
        await using var control = await StageDriver.StartAsync(bound.Token);
        var challenge = await control.ProveAsync(bound.Token);
        await ExpectFrameAsync(control.Peer, PlacedKind, bound.Token);
        await WriteFrameAsync(control.Peer, GoKind, challenge, bound.Token);
        (await ExpectFrameAsync(control.Peer, ReleasingKind, bound.Token)).ShouldBe(challenge);
        var released = await control.JoinAsync(bound.Token);
        released.ExitCode.ShouldBe(0);
        released.Exec.ShouldBe(StageDriver.ExpectedDrop);
    }

    // PC-14. A RELEASING that cannot be sent leaves the stage unreleased, even after a valid GO.
    [Test]
    [NativeLinuxOnly("The placement stage fixture uses Linux abstract sockets and procfs.")]
    public async Task FailedReleaseSendSuppressesExec()
    {
        RequireLinux();
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await using (var control = await StageDriver.StartAsync(bound.Token))
        {
            var challenge = await control.ProveAsync(bound.Token);
            await ExpectFrameAsync(control.Peer, PlacedKind, bound.Token);
            await WriteFrameAsync(control.Peer, GoKind, challenge, bound.Token);
            await ExpectFrameAsync(control.Peer, ReleasingKind, bound.Token);
            (await control.JoinAsync(bound.Token)).Exec.ShouldBe(StageDriver.ExpectedDrop);
        }

        await using var stage = await StageDriver.StartAsync(bound.Token);
        var go = await stage.ProveAsync(bound.Token);
        await ExpectFrameAsync(stage.Peer, PlacedKind, bound.Token);
        // Refuse only the stage's next write (EPIPE), then deliver a valid, timely GO.
        stage.Peer.Shutdown(SocketShutdown.Receive);
        await WriteFrameAsync(stage.Peer, GoKind, go, bound.Token);
        var joined = await stage.JoinAsync(bound.Token);
        joined.Exec.ShouldBeEmpty();
        joined.ExitCode.ShouldBe(3);
        joined.Stderr.ShouldContain("C2060_CUSTODY_REFUSED PlacementExchange");
    }

    // PC-15. The only exec is the fixed privilege drop, and user argv follows its literal separator.
    [Test]
    [NativeLinuxOnly("The placement stage fixture uses Linux abstract sockets and procfs.")]
    public async Task DropPreservesPayloadBoundary()
    {
        RequireLinux();
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string[] payload = ["/bin/echo", "--reuid=0", "-n", "--", "x"];
        await using var stage = await StageDriver.StartAsync(bound.Token, payload);
        var challenge = await stage.ProveAsync(bound.Token);
        await ExpectFrameAsync(stage.Peer, PlacedKind, bound.Token);
        await WriteFrameAsync(stage.Peer, GoKind, challenge, bound.Token);
        await ExpectFrameAsync(stage.Peer, ReleasingKind, bound.Token);
        var joined = await stage.JoinAsync(bound.Token);
        joined.ExitCode.ShouldBe(0);
        joined.Exec.ShouldBe(StageDriver.FixedDrop.Concat(payload).ToArray());
    }

    private const byte ProveKind = 1, PlacedKind = 2, GoKind = 3, ReleasingKind = 4;

    [Test]
    [NativeLinuxOnly("Linux placement sends GO over the authenticated Unix socket.")]
    public async Task SlowFirstPlacementSucceedsAndStuckPeerExpires()
    {
        RequireLinux();
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var deadline = LinuxPlacementLaunch.ClampAtReceipt(LinuxPlacementLaunch.Stamp(0, 0), 0);
        var budget = Stopwatch.GetElapsedTime(0, deadline);

        var clock = new FakeTimeProvider();
        using (var world = HostAttemptWorld.Start(Environment.ProcessId, clock, budget))
        {
            var exchange = world.ExchangeAsync(bound.Token);
            using var peer = world.Connect();
            var go = await world.ProveAndPlaceAsync(peer, _ => clock.Advance(TimeSpan.FromSeconds(6)), bound.Token);
            if (go is { Kind: GoKind })
                await WriteFrameAsync(peer, ReleasingKind, go.Value.Body, bound.Token);
            await Should.NotThrowAsync(async () => await exchange);
            go.ShouldNotBeNull();
            go.Value.Kind.ShouldBe(GoKind);
            world.Attempt.Commit().ShouldNotBeNull();
        }

        using (var world = HostAttemptWorld.Start(Environment.ProcessId, clock, budget))
        {
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(bound.Token, world.Attempt.Token);
            var exchange = world.ExchangeAsync(ct.Token);
            using var peer = world.Connect();
            (await ReadFrameAsync(peer, bound.Token))!.Value.Kind.ShouldBe(ProveKind);
            clock.Advance(budget - TimeSpan.FromTicks(1));
            ct.IsCancellationRequested.ShouldBeFalse();
            clock.Advance(TimeSpan.FromTicks(1));
            ct.IsCancellationRequested.ShouldBeTrue("a silent peer must hit the absolute placement cutoff");
            await Should.ThrowAsync<OperationCanceledException>(async () => await exchange);
            (await ReadFrameAsync(peer, bound.Token)).ShouldBeNull();
            world.Attempt.Commit().ShouldBeNull();
        }
    }

    /// <summary>The original host's attempt against a fixture socket peer in this process.</summary>
    private sealed class HostAttemptWorld : IDisposable
    {
        public const ulong Ticks = 654321;
        private static readonly Guid Boot = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

        private HostAttemptWorld(LinuxPlacementBinding binding, LinuxSpawnIdentity root, LinuxPlacementAttempt attempt)
        {
            Binding = binding;
            Root = root;
            Attempt = attempt;
        }

        public LinuxPlacementBinding Binding { get; }
        public LinuxSpawnIdentity Root { get; }
        public LinuxPlacementAttempt Attempt { get; }

        public static HostAttemptWorld Start(int rootPid, TimeProvider? clock = null, TimeSpan? budget = null)
        {
            clock ??= TimeProvider.System;
            var binding = new LinuxPlacementBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var root = new LinuxSpawnIdentity(rootPid, Environment.ProcessId, Boot, Ticks, DateTime.UtcNow);
            var duration = budget ?? TimeSpan.FromSeconds(10);
            var deadline = clock.GetTimestamp() + duration.Ticks * clock.TimestampFrequency / TimeSpan.TicksPerSecond;
            return new(binding, root, new LinuxPlacementAttempt(binding, deadline, CancellationToken.None, clock)
            {
                ExpectedPeerUid = GetUid(),
            });
        }

        public Task ExchangeAsync(CancellationToken ct) => Attempt.ExchangeAsync(() => false, Root, null, ct);

        public Socket Connect()
        {
            var peer = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            peer.Connect(new UnixDomainSocketEndPoint("\0antiphon-custody-v1-" + Binding.ContainerId.ToString("D")));
            return peer;
        }

        /// <summary>Reads PROVE, answers with an honest PLACED after <paramref name="mutate"/>, returns the next frame.</summary>
        public async Task<(byte Kind, byte[] Body)?> ProveAndPlaceAsync(Socket peer, Action<byte[]> mutate, CancellationToken ct)
        {
            var prove = await ReadFrameAsync(peer, ct);
            prove.ShouldNotBeNull();
            prove.Value.Kind.ShouldBe(ProveKind);
            prove.Value.Body.Length.ShouldBe(104);
            var placed = new byte[140];
            prove.Value.Body.CopyTo(placed, 0);
            BinaryPrimitives.WriteInt32BigEndian(placed.AsSpan(104), Root.Pid);
            BinaryPrimitives.WriteInt32BigEndian(placed.AsSpan(108), Root.ParentPid);
            BinaryPrimitives.WriteUInt64BigEndian(placed.AsSpan(112), Root.StartTicks);
            Root.BootId.TryWriteBytes(placed.AsSpan(120, 16), bigEndian: true, out _).ShouldBeTrue();
            BinaryPrimitives.WriteUInt32BigEndian(placed.AsSpan(136), LinuxPlacementAttempt.V1Controllers);
            mutate(placed);
            await WriteFrameAsync(peer, PlacedKind, placed, ct);
            return await ReadFrameAsync(peer, ct);
        }

        public void Dispose() => Attempt.Dispose();
    }

    /// <summary>
    /// The test-only build of the real stage source, run unprivileged as a direct child of this
    /// listener, with private cgroup and namespace files and an inert exec recorder.
    /// </summary>
    private sealed class StageDriver : IAsyncDisposable
    {
        public static readonly string[] FixedDrop =
            ["/usr/bin/setpriv", "--reuid=1654", "--regid=1654", "--clear-groups", "--no-new-privs", "--"];
        private static readonly string[] Payload = ["/bin/true", "--payload"];
        public static readonly string[] ExpectedDrop = [.. FixedDrop, .. Payload];

        private readonly DirectoryInfo _root;
        private readonly Process _stage;
        private readonly Task<string> _stdout, _stderr;
        private readonly Guid _container, _namespace;
        private readonly long _deadline;

        private StageDriver(DirectoryInfo root, Process stage, Socket peer, Guid container, Guid ns, long deadline)
        {
            _root = root;
            _stage = stage;
            Peer = peer;
            _container = container;
            _namespace = ns;
            _deadline = deadline;
            _stdout = stage.StandardOutput.ReadToEndAsync();
            _stderr = stage.StandardError.ReadToEndAsync();
        }

        public Socket Peer { get; }

        public static async Task<StageDriver> StartAsync(CancellationToken ct, string[]? payload = null)
        {
            var binary = Environment.GetEnvironmentVariable("ANTIPHON_PLACEMENT_FIXTURE_STAGE") is { Length: > 0 } configured
                ? configured
                : "/opt/antiphon-tests/antiphon-custody-placement-fixture";
            if (!File.Exists(binary))
                throw new SkipTestException("Requires the placement stage fixture build (session-testing image).");

            var root = Directory.CreateTempSubdirectory("c2060-stage-");
            var container = Guid.NewGuid();
            var ns = Guid.NewGuid();
            Directory.CreateDirectory(Path.Combine(root.FullName, "run", "antiphon-custody"));
            File.WriteAllText(Path.Combine(root.FullName, "run", "antiphon-custody", "namespace"), ns.ToString("D") + "\n");
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint("\0antiphon-custody-v1-" + container.ToString("D")));
            listener.Listen(1);

            var deadline = Stopwatch.GetTimestamp() + 15 * Stopwatch.Frequency;
            var start = new ProcessStartInfo(binary)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in (string[])[container.ToString("D"), deadline.ToString(CultureInfo.InvariantCulture), "--", .. payload ?? Payload])
                start.ArgumentList.Add(argument);
            start.Environment["ANTIPHON_PLACEMENT_FIXTURE_ROOT"] = root.FullName;
            var stage = Process.Start(start)!;
            try
            {
                var peer = await listener.AcceptAsync(ct);
                return new(root, stage, peer, container, ns, deadline);
            }
            catch
            {
                try { stage.Kill(); } catch (InvalidOperationException) { }
                await stage.WaitForExitAsync(CancellationToken.None);
                stage.Dispose();
                root.Delete(recursive: true);
                throw;
            }
        }

        /// <summary>Publishes both memberships for the connected stage PID, then sends PROVE.</summary>
        public async Task<byte[]> ProveAsync(CancellationToken ct, string? mixedController = null)
        {
            var stage = _stage.Id.ToString(CultureInfo.InvariantCulture) + "\n";
            var mixed = stage + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n";
            WriteMembers("pids", mixedController == "pids" ? mixed : stage);
            WriteMembers("freezer", mixedController == "freezer" ? mixed : stage);
            var challenge = RandomNumberGenerator.GetBytes(16);
            var binding = new LinuxPlacementBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _container, _namespace);
            await WriteFrameAsync(Peer, ProveKind, LinuxPlacementAttempt.BuildProveBody(binding, _deadline, challenge), ct);
            return challenge;
        }

        private void WriteMembers(string controller, string members)
        {
            var tree = Path.Combine(_root.FullName, "sys", "fs", "cgroup", controller, "antiphon-custody",
                _namespace.ToString("D"), _container.ToString("D"), "tree");
            Directory.CreateDirectory(tree);
            File.WriteAllText(Path.Combine(tree, "cgroup.procs"), members);
        }

        public async Task<(int ExitCode, IReadOnlyList<string> Exec, string Stderr)> JoinAsync(CancellationToken ct)
        {
            await _stage.WaitForExitAsync(ct);
            var exec = (await _stdout).Split('\n')
                .Where(line => line.StartsWith("C2060_FIXTURE_EXEC ", StringComparison.Ordinal))
                .Select(line => line["C2060_FIXTURE_EXEC ".Length..])
                .ToArray();
            return (_stage.ExitCode, exec, await _stderr);
        }

        public async ValueTask DisposeAsync()
        {
            Peer.Dispose();
            if (!_stage.HasExited)
            {
                try { _stage.Kill(); } catch (InvalidOperationException) { }
            }
            await _stage.WaitForExitAsync(CancellationToken.None);
            _stage.Dispose();
            _root.Delete(recursive: true);
        }
    }

    private static async Task WriteFrameAsync(Socket socket, byte kind, byte[] body, CancellationToken ct)
    {
        var frame = new byte[4 + body.Length];
        frame[0] = 1;
        frame[1] = kind;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), (ushort)body.Length);
        body.CopyTo(frame, 4);
        for (var sent = 0; sent < frame.Length;)
            sent += await socket.SendAsync(frame.AsMemory(sent), SocketFlags.None, ct);
    }

    /// <summary>One frame, or null at a clean end of stream before its first byte.</summary>
    private static async Task<(byte Kind, byte[] Body)?> ReadFrameAsync(Socket socket, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactlyAsync(socket, header, ct))
            return null;
        header[0].ShouldBe((byte)1);
        var body = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2))];
        (await ReadExactlyAsync(socket, body, ct)).ShouldBeTrue("truncated frame body");
        return (header[1], body);
    }

    private static async Task<byte[]> ExpectFrameAsync(Socket socket, byte kind, CancellationToken ct)
    {
        var frame = await ReadFrameAsync(socket, ct);
        frame.ShouldNotBeNull("expected frame kind " + kind.ToString(CultureInfo.InvariantCulture) + ", got end of stream");
        frame.Value.Kind.ShouldBe(kind);
        return frame.Value.Body;
    }

    private static async Task<List<(byte Kind, byte[] Body)>> ReadUntilEofAsync(Socket socket, CancellationToken ct)
    {
        var frames = new List<(byte Kind, byte[] Body)>();
        while (await ReadFrameAsync(socket, ct) is { } frame)
            frames.Add(frame);
        return frames;
    }

    private static async Task<bool> ReadExactlyAsync(Socket socket, Memory<byte> target, CancellationToken ct)
    {
        var first = true;
        while (!target.IsEmpty)
        {
            int count;
            try { count = await socket.ReceiveAsync(target, SocketFlags.None, ct); }
            catch (SocketException ex) when (first && ex.SocketErrorCode == SocketError.ConnectionReset) { return false; }
            if (count == 0)
            {
                if (first) return false;
                throw new EndOfStreamException("A placement frame was truncated.");
            }
            first = false;
            target = target[count..];
        }
        return true;
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();
}
