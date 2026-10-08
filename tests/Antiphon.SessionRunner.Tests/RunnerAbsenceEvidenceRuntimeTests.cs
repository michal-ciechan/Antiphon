using System.Collections.Concurrent;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1153 S2: the runtime's creation entry points against the evidence service, without a
/// real process. Design: V-5..V-8. Fixture: <c>SessionRunnerRuntime</c> over a temp root with a
/// test-only creation seam that records the order of the Attempted write against the first
/// provider effect (pty <c>LaunchDetachedAsync</c>, herdr <c>ConnectAndValidateAsync</c>,
/// adoption <c>TryAdd</c>), FakeTimeProvider, and barriers for the race.
/// </summary>
[Category("Integration")]
[NotInParallel("SessionLiveness")]
public class RunnerAbsenceEvidenceRuntimeTests
{
    /// <summary>
    /// V-5. The seam observes a durable Attempted record before its first call on every path;
    /// a seam throw leaves the record Attempted after runtime removal and release.
    /// </summary>
    [Test]
    [Arguments("start")]
    [Arguments("attach")]
    [Arguments("adoption")]
    public async Task C1153_Creation_consumes_proof_before_effects(string path)
    {
        await using var world = await RuntimeWorld.CreateAsync();
        var id = Guid.NewGuid();
        (await world.PrepareAsync(id)).Refusal.ShouldBeNull();
        world.ReadState(id).ShouldBe(RunnerAbsenceRecordState.Prepared);

        if (path == "adoption")
        {
            world.SeedDeadManifest(id);
            await using var restarted = await world.RestartAsync();
            restarted.Effects.Select(e => (e.Id, e.Path, e.StateAtEffect))
                .ShouldBe([(id, "adoption", (RunnerAbsenceRecordState?)RunnerAbsenceRecordState.Attempted)],
                    "adoption records the discovered id as attempted before registering it");
            restarted.Runtime.List().ShouldContain(s => s.SessionId == id);
            await restarted.Runtime.ReleaseSlotAsync(id, "c1153-test", TimeSpan.Zero, CancellationToken.None);
            restarted.Runtime.List().ShouldNotContain(s => s.SessionId == id);
            restarted.ReadState(id).ShouldBe(RunnerAbsenceRecordState.Attempted, "release preserves the marker");
            (await restarted.CertifyAsync(id)).Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.IdentityKnown);
            return;
        }

        world.ThrowAtEffect = true;
        var launch = path == "start"
            ? world.Runtime.StartAsync(world.Launch(id), CancellationToken.None)
            : world.Runtime.AttachHerdrAsync(world.Attach(id), CancellationToken.None);

        await Should.ThrowAsync<ProviderEffectForTestException>(launch);

        world.Effects.Select(e => (e.Id, e.Path, e.StateAtEffect))
            .ShouldBe([(id, path, (RunnerAbsenceRecordState?)RunnerAbsenceRecordState.Attempted)],
                "the first provider effect sees a durable Attempted record");
        world.Runtime.List().ShouldNotContain(s => s.SessionId == id, "the failed creation is removed from the runtime");
        world.ReadState(id).ShouldBe(RunnerAbsenceRecordState.Attempted, "a failed start keeps the marker");
        var certify = await world.CertifyAsync(id);
        certify.Value.ShouldBeNull();
        certify.Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.IdentityKnown);
    }

    /// <summary>
    /// V-6. A failing Attempted write latches evidence unavailable before the seam runs; a
    /// later certify refuses with the latched reason; the Prepared record is not usable from
    /// memory; a healthy legacy launch on another id still completes.
    /// </summary>
    [Test]
    public async Task C1153_Store_failure_disables_proof_without_stopping_work()
    {
        await using var world = await RuntimeWorld.CreateAsync();
        var id = Guid.NewGuid();
        (await world.PrepareAsync(id)).Refusal.ShouldBeNull();
        var prepared = world.RecordBytes(id);
        world.Runtime.AbsenceEvidenceReady.ShouldBeTrue();
        world.ThrowAtEffect = true;
        world.MakeRecordUnwritable(id);
        try
        {
            await Should.ThrowAsync<ProviderEffectForTestException>(
                world.Runtime.StartAsync(world.Launch(id), CancellationToken.None));
        }
        finally { world.RestoreRecordWritable(id); }

        world.Effects.Select(e => (e.Id, e.Path)).ShouldBe([(id, "start")],
            "the launch reached its provider effect: a storage fault is not a launch refusal");
        world.Effects[0].LatchedAtEffect.ShouldNotBeNull("the latch is set before the provider effect");
        world.Runtime.List().ShouldNotContain(s => s.SessionId == id);
        world.RecordBytes(id).ShouldBe(prepared, "the old Prepared record is left on disk");
        world.Runtime.AbsenceEvidenceReady.ShouldBeFalse("the capability is withdrawn for this epoch");

        var certify = await world.CertifyAsync(id);
        certify.Value.ShouldBeNull("a Prepared record left behind by a failed marker is not usable");
        certify.Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.Unavailable);
        certify.Refusal.Reason.ShouldContain("latched");

        // A legacy launch on another id is not refused by the latched evidence service.
        var other = Guid.NewGuid();
        await Should.ThrowAsync<ProviderEffectForTestException>(
            world.Runtime.StartAsync(world.Launch(other), CancellationToken.None));
        world.Effects.Select(e => (e.Id, e.Path)).ShouldBe([(id, "start"), (other, "start")]);
        (await world.PrepareAsync(Guid.NewGuid())).Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.Unavailable);
    }

    /// <summary>
    /// V-7. Barrier-ordered: launch-first yields a typed certify refusal; certify-first yields a
    /// typed launch refusal with zero seam calls; never two successes.
    /// </summary>
    [Test]
    [Arguments("launch-first")]
    [Arguments("certify-first")]
    public async Task C1153_Certificate_and_launch_race_is_serialized(string order)
    {
        await using var world = await RuntimeWorld.CreateAsync();
        var id = Guid.NewGuid();
        (await world.PrepareAsync(id)).Refusal.ShouldBeNull();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (order == "launch-first")
        {
            world.EffectBarrier = async () => { entered.TrySetResult(); await release.Task; };
            world.ThrowAtEffect = true;
            var launch = world.Runtime.StartAsync(world.Launch(id), CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var certify = world.CertifyAsync(id);
            await Task.Delay(200);
            certify.IsCompleted.ShouldBeFalse("certify waits for the launch gate the creation holds");
            release.TrySetResult();
            await Should.ThrowAsync<ProviderEffectForTestException>(launch);
            var certified = await certify.WaitAsync(TimeSpan.FromSeconds(10));
            certified.Value.ShouldBeNull("the creation won: no certificate");
            certified.Refusal!.Code.ShouldBe(RunnerAbsenceRefusalCodes.IdentityKnown);
            world.Effects.Count.ShouldBe(1);
            world.ReadState(id).ShouldBe(RunnerAbsenceRecordState.Attempted);
            return;
        }

        world.Runtime.AbsenceDecisionUnderGateForTest = async _ => { entered.TrySetResult(); await release.Task; };
        var first = world.CertifyAsync(id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        world.Runtime.AbsenceDecisionUnderGateForTest = null;
        var delayed = world.Runtime.StartAsync(world.Launch(id), CancellationToken.None);
        await Task.Delay(200);
        delayed.IsCompleted.ShouldBeFalse("the launch waits for the gate the certificate holds");
        world.Effects.ShouldBeEmpty();
        release.TrySetResult();
        var won = await first.WaitAsync(TimeSpan.FromSeconds(10));
        won.Refusal.ShouldBeNull("the certificate won");
        won.Value!.IdentityClosed.ShouldBe(true);
        var refused = await Should.ThrowAsync<SessionIdentityClosedException>(delayed);
        refused.SessionId.ShouldBe(id);
        world.Effects.ShouldBeEmpty("the losing launch never reached a provider effect");
        world.Runtime.List().ShouldNotContain(s => s.SessionId == id);
        world.ReadState(id).ShouldBe(RunnerAbsenceRecordState.ClosedUnused);
    }

    /// <summary>
    /// V-8. After ClosedUnused every entry point refuses with the closed-identity type and the
    /// seam count stays zero; a repeat certificate with a fresh nonce still succeeds.
    /// </summary>
    [Test]
    [Arguments("same-generation-start")]
    [Arguments("newer-generation-start")]
    [Arguments("attach")]
    [Arguments("custody-bound-start")]
    public async Task C1153_Closed_identity_refuses_delayed_creation(string entry)
    {
        await using var world = await RuntimeWorld.CreateAsync();
        var id = Guid.NewGuid();
        (await world.PrepareAsync(id)).Refusal.ShouldBeNull();
        (await world.CertifyAsync(id)).Refusal.ShouldBeNull();
        world.ReadState(id).ShouldBe(RunnerAbsenceRecordState.ClosedUnused);
        var closed = world.RecordBytes(id);
        var custodyRoot = Path.Combine(world.Root, "verification-custody");
        var custodyFiles = Directory.Exists(custodyRoot)
            ? Directory.GetFiles(custodyRoot, "*", SearchOption.AllDirectories).Length : 0;

        var attempt = entry switch
        {
            "same-generation-start" => world.Runtime.StartAsync(world.Launch(id), CancellationToken.None),
            "newer-generation-start" => world.Runtime.StartAsync(
                world.Launch(id) with { AcceptedStartedAt = SessionGeneration.Next(RuntimeWorld.Generation, RuntimeWorld.Generation) },
                CancellationToken.None),
            "attach" => world.Runtime.AttachHerdrAsync(world.Attach(id), CancellationToken.None),
            "custody-bound-start" => world.Runtime.StartAsync(world.Launch(id) with
            {
                VerificationBinding = new VerificationExecutionBinding(
                    Guid.NewGuid(),
                    new VerificationSourceIdentity(Guid.NewGuid(), Guid.NewGuid(), "deadbeef"),
                    new VerificationSessionGeneration(id, RuntimeWorld.Generation),
                    new VerificationCreationCoordinates("r", "g", "w", "wg", "main", Guid.NewGuid()),
                    VerificationCustodyBackends.LinuxCgroup, Guid.NewGuid()),
            }, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, null),
        };

        var refused = await Should.ThrowAsync<SessionIdentityClosedException>(attempt);

        refused.SessionId.ShouldBe(id);
        world.Effects.ShouldBeEmpty("no provider effect for a closed identity");
        world.Runtime.List().ShouldNotContain(s => s.SessionId == id);
        world.Runtime.StartCoreSessionRegistrations.ShouldBe(0);
        File.Exists(PtyHostManifest.PathFor(world.Settings.PtyHostManifestDir, id)).ShouldBeFalse();
        File.Exists(HerdrPaneSidecar.PathFor(world.Root, id)).ShouldBeFalse();
        (Directory.Exists(custodyRoot) ? Directory.GetFiles(custodyRoot, "*", SearchOption.AllDirectories).Length : 0)
            .ShouldBe(custodyFiles, "refused before any custody record or PrepareStart");
        world.RecordBytes(id).ShouldBe(closed, "the closed record is never overwritten");

        var nonce = RunnerAbsenceEvidence.NewNonce();
        var repeat = await world.CertifyAsync(id, nonce);
        repeat.Refusal.ShouldBeNull("a repeat certificate with a fresh nonce is still issued");
        repeat.Value!.RequestNonce.ShouldBe(nonce);
    }
}

internal sealed class ProviderEffectForTestException() : Exception("fixture: provider effect refused before any process");

/// <summary>A runtime over a temp root, adoption complete, with the creation seam recording order.</summary>
internal sealed class RuntimeWorld : IAsyncDisposable
{
    public static readonly DateTime Generation = new DateTime(2026, 10, 8, 3, 4, 5, DateTimeKind.Utc).AddTicks(1230);

    private readonly bool _ownsRoot;
    public string Root { get; }
    public SessionRunnerSettings Settings { get; }
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 8, 4, 0, 0, TimeSpan.Zero));
    public SessionRunnerRuntime Runtime { get; private set; } = null!;
    public List<(Guid Id, string Path, RunnerAbsenceRecordState? StateAtEffect, string? LatchedAtEffect)> Effects { get; } = new();
    public bool ThrowAtEffect { get; set; }
    public Func<Task>? EffectBarrier { get; set; }

    private RuntimeWorld(string root, bool ownsRoot)
    {
        Root = root;
        _ownsRoot = ownsRoot;
        Settings = new SessionRunnerSettings { SessionLogPath = root };
    }

    public static async Task<RuntimeWorld> CreateAsync(string? root = null)
    {
        var world = new RuntimeWorld(root ?? Path.Combine(Path.GetTempPath(), "c1153-rt-" + Guid.NewGuid().ToString("N")), root is null);
        world.Runtime = new SessionRunnerRuntime(Options.Create(world.Settings), NullLogger<SessionRunnerRuntime>.Instance,
            timeProvider: world.Clock);
        world.Runtime.CreationEffectForTest = world.OnEffectAsync;
        await world.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        return world;
    }

    /// <summary>A second runtime (new epoch) over the same root; adoption runs with the seam set.</summary>
    public Task<RuntimeWorld> RestartAsync() => CreateAsync(Root);

    private async Task OnEffectAsync(Guid id, string path)
    {
        var read = Runtime.AbsenceEvidence!.Store.Read(id);
        Effects.Add((id, path, read.Record?.State, Runtime.AbsenceEvidence.LatchReason));
        if (EffectBarrier is { } barrier) await barrier();
        if (ThrowAtEffect) throw new ProviderEffectForTestException();
    }

    public Guid StoreId => Runtime.RunnerStoreId;

    public RunnerAbsenceRequest Request(Guid id, string? nonce = null) =>
        new(RunnerAbsenceEvidence.Version, id, Generation, StoreId, nonce ?? RunnerAbsenceEvidence.NewNonce());

    public Task<RunnerAbsenceOutcome<RunnerAbsencePrepared>> PrepareAsync(Guid id) =>
        Runtime.PrepareAbsenceEvidenceAsync(Request(id), CancellationToken.None);

    public Task<RunnerAbsenceOutcome<RunnerAbsenceCertificate>> CertifyAsync(Guid id, string? nonce = null) =>
        Runtime.CertifyAbsenceAsync(Request(id, nonce), CancellationToken.None);

    public RunnerLaunchRequest Launch(Guid id) => new(
        id, OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh", [], new Dictionary<string, string>(), Root, 80, 24,
        AcceptedStartedAt: Generation);

    public HerdrAttachRequest Attach(Guid id) => new(
        id, "pane-1", HerdrAgentKinds.Claude, TranscriptFormats.Claude, 4242, "workspace", AcceptedStartedAt: Generation);

    public string RecordPath(Guid id) => Path.Combine(RunnerAbsenceEvidenceStore.DirectoryFor(Root), $"{id:N}.json");

    public byte[]? RecordBytes(Guid id) => File.Exists(RecordPath(id)) ? File.ReadAllBytes(RecordPath(id)) : null;

    public RunnerAbsenceRecordState? ReadState(Guid id) => Runtime.AbsenceEvidence!.Store.Read(id).Record?.State;

    public void SeedDeadManifest(Guid id) => new PtyHostManifest
    {
        SessionId = id, PipeName = "fixture-no-process", HostPid = 0, HostStartTimeUtc = Generation,
        CreatedAtUtc = Generation, AcceptedStartedAt = Generation, ExitReason = "fixture", ExitCode = 0,
        ExitedAtUtc = Generation,
    }.SaveAtomic(PtyHostManifest.PathFor(Settings.PtyHostManifestDir, id));

    // The atomic write lands a temp file and renames it over the record; a directory at the
    // temp path makes that write fail without touching the record itself.
    public void MakeRecordUnwritable(Guid id) => Directory.CreateDirectory(RecordPath(id) + ".tmp");

    public void RestoreRecordWritable(Guid id) => Directory.Delete(RecordPath(id) + ".tmp");

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        if (_ownsRoot)
        {
            try { Directory.Delete(Root, recursive: true); } catch (Exception) { }
        }
    }
}
