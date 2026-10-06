using System.Security.Cryptography;
using System.Text;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.BlockedParkWireTests;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NotInParallel("ClaudeConfigDirEnv")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorkspaceSourceVerificationWireTests
{
    [Test]
    public async Task C1065_RepositoryIdentityReadIsBoundToSessionCheckout()
    {
        ((int)PhoneHomeOperation.WorkspaceRepositoryIdentity).ShouldBe(37);
        foreach (var phoneHome in new[] { false, true })
        {
            await using var world = new SeatWorld("Codex");
            await world.StartAsync();
            var surface = new CurrentSurface(world.Runtime);
            await using var wire = await SeatWire.StartAsync(world, surface, phoneHome);
            var request = new WorkspaceRepositoryIdentityRequest(world.Tail.SessionId, world.Source.Mirror,
                world.Runtime.RunnerStoreId, world.Request.ExpectedAcceptedStartedAt);
            var originalSource = await world.Source.SourceSnapshotAsync();
            var originalRemote = await world.Source.RemoteShaAsync();
            var read = await wire.IdentityAsync(request);
            read.Outcome.ShouldBe(WorkspaceRepositoryIdentityOutcome.Read, "V-28 all-valid control");
            var identity = read.Identity.ShouldNotBeNull();
            var common = (await world.Source.GitAsync(world.Source.Mirror, "rev-parse", "--path-format=absolute", "--git-common-dir")).Trim();
            identity.RepositoryIdentity.ShouldBe(Digest(OperatingSystem.IsWindows() ? common.ToUpperInvariant() : common));
            identity.EndpointFingerprint.ShouldBe(Digest(world.Source.Origin), "G-207 runner-observed endpoint");
            identity.EndpointRepository.ShouldBe(world.Source.Origin);
            identity.FullRef.ShouldBe(World.FullRef);
            identity.HeadSha.ShouldBe(await world.Source.HeadAsync());
            identity.RunnerStoreId.ShouldBe(world.Runtime.RunnerStoreId);
            identity.AcceptedStartedAt.ShouldBe(request.ExpectedAcceptedStartedAt);
            world.Source.Commands.ShouldAllBe(c => new[] { "rev-parse", "symbolic-ref", "remote" }.Contains(c[0]), "read-only Git");
            (await world.Source.SourceSnapshotAsync()).ShouldBe(originalSource);
            (await world.Source.RemoteShaAsync()).ShouldBe(originalRemote);
            world.AssertRetained();

            foreach (var missing in new[] { RunnerCapabilityFeatures.WorkspaceRepositoryIdentityV1,
                RunnerCapabilityFeatures.TerminalSeatReleaseV1, "version" })
            {
                surface.MissingCapability = missing;
                var before = surface.IdentityCalls;
                AssertIdentityHeld(await wire.IdentityAsync(request with { Version = missing == "version" ? 2 : 1 }),
                    "identity_unsupported", "G-201");
                surface.IdentityCalls.ShouldBe(before, "G-201 zero runtime calls");
            }
            surface.MissingCapability = null;
            await using (var oldWire = await SeatWire.StartAsync(world, new LegacySurface(world.Runtime), phoneHome))
                AssertIdentityHeld(await oldWire.IdentityAsync(request), "identity_unsupported", "old runner");
            AssertIdentityHeld(await wire.IdentityAsync(request with { ExpectedRunnerStoreId = Guid.NewGuid() }),
                "identity_generation_changed", "store fence");
            AssertIdentityHeld(await wire.IdentityAsync(request with { ExpectedAcceptedStartedAt = request.ExpectedAcceptedStartedAt.AddTicks(SessionGeneration.MicrosecondTicks) }),
                "identity_generation_changed", "generation fence");
            // The HTTP URL is derived independently by SeatWire; absent runtime is tested directly below.
            AssertIdentityHeld(await world.Runtime.ReadWorkspaceRepositoryIdentityAsync(request with { SessionId = Guid.NewGuid() }, CancellationToken.None),
                "identity_session_unknown", "G-208");
            var other = Path.Combine(world.Source.Work, "worktrees", "task-cafebabe");
            await world.Source.GitAsync(world.Source.Repo, "worktree", "add", "-b", "other", other, world.Source.BaseSha);
            AssertIdentityHeld(await wire.IdentityAsync(request with { Path = other }), "identity_path_unowned", "G-203 second owned mirror");
            using (var foreign = await World.CreateAsync())
            {
                world.Session.RetainCheckout(foreign.Mirror);
                AssertIdentityHeld(await wire.IdentityAsync(request with { Path = foreign.Mirror }), "identity_repository_unowned", "G-204");
                world.Session.RetainCheckout(world.Source.Mirror);
            }
            world.Source.BeforeStart = _ => throw new IOException("injected Git start failure");
            var unknown = await wire.IdentityAsync(request);
            unknown.Outcome.ShouldBe(WorkspaceRepositoryIdentityOutcome.Unknown, "G-206");
            unknown.Identity.ShouldBeNull("G-206");
            world.Source.BeforeStart = null;

            // A paused real Prepare owns the same launch gate. Identity cannot enter Git yet.
            var prepareEntered = Signal();
            var continuePrepare = Signal();
            world.ParkBarrier = async (boundary, ct) =>
            {
                if (boundary != WorkspaceParkBoundary.BeforeFinalObservation) return;
                prepareEntered.TrySetResult();
                await continuePrepare.Task.WaitAsync(ct);
            };
            var bound = world.Source.Request with { Binding = world.Source.Request.Binding with
            { SessionId = request.SessionId, RunnerStoreId = request.ExpectedRunnerStoreId, AcceptedStartedAt = request.ExpectedAcceptedStartedAt } };
            var prepare = world.Runtime.ParkWorkspaceAsync(new(request.SessionId, Prepare: bound), CancellationToken.None);
            await prepareEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var pendingRead = world.Runtime.ReadWorkspaceRepositoryIdentityAsync(request, CancellationToken.None);
            try
            {
                (await Task.WhenAny(pendingRead, Task.Delay(150))).ShouldNotBe(pendingRead, "G-205 launch gate");
            }
            finally { continuePrepare.TrySetResult(); await prepare; await pendingRead; }
            (await pendingRead).Outcome.ShouldBe(WorkspaceRepositoryIdentityOutcome.Read);

            // Generation changes at an awaited real read boundary must erase the entire answer.
            world.ParkBarrier = (boundary, _) =>
            {
                if (boundary == WorkspaceParkBoundary.AfterIdentityRead)
                    world.ReplaceTracked(request.ExpectedAcceptedStartedAt.AddSeconds(1));
                return Task.CompletedTask;
            };
            AssertIdentityHeld(await wire.IdentityAsync(request), "identity_generation_changed", "G-202");
            surface.ForceCalls.ShouldBe(0);
            surface.GenerationKillCalls.ShouldBe(0);
            world.Child.Kills.ShouldBe(0);
        }
    }

    [Test]
    public async Task C1065_LocalSourceModesVerifyFreshAtRelease()
    {
        foreach (var phoneHome in new[] { false, true })
        foreach (var mode in new[] { WorkspaceParkSourceMode.Published, WorkspaceParkSourceMode.NoSourceChanges })
        foreach (var branch in new[] { "live", "exited", "absent" })
        {
            await using var world = new SeatWorld("Codex", localLane: true);
            var release = Typed(await world.QualifyAsync(), mode);
            var surface = new CurrentSurface(world.Runtime);
            await using var wire = await SeatWire.StartAsync(world, surface, phoneHome);
            (await wire.CapabilitiesAsync()).Features.ShouldContain(RunnerCapabilityFeatures.WorkspaceParkSourceModesV1);
            world.Child.Kill = _ => { world.Child.Exit(); return Task.FromResult(true); };
            if (branch == "exited") world.Child.Exit();
            if (branch == "absent")
            {
                // Remove through the real runtime, then recreate it: the absent branch must use
                // its durable generation-bound checkout, never the receipt's requested path.
                await world.Runtime.ReleaseSlotAsync(world.Tail.SessionId, "fixture exit", TimeSpan.Zero, CancellationToken.None);
                await world.RestartEmptyAsync();
            }
            world.Source.Commands.Clear();
            // The wire surface must point at the restarted runtime on the absent branch.
            var result = branch == "absent" ? await world.ReleaseAsync(release) : await wire.ReleaseAsync(release);
            result.Outcome.ShouldBe(branch switch
            {
                "exited" => TerminalSeatReleaseOutcome.AlreadyExited,
                "absent" => TerminalSeatReleaseOutcome.AlreadyAbsent,
                _ => TerminalSeatReleaseOutcome.Released
            }, "V-29 " + mode + " " + branch);
            if (branch != "absent") surface.ReleaseReceived.ShouldBe(release, "typed mode and nullable remote SHA survive wire");
            world.AssertReleased(expectedKills: branch == "exited" ? 0 : 1);
            AssertNoPush(world, "G-212 valid proof");
            if (mode == WorkspaceParkSourceMode.NoSourceChanges)
                world.Source.Commands.ShouldNotContain(c => c[0] is "remote" or "ls-remote", "NoSourceChanges reads no endpoint");
        }

        foreach (var mode in new[] { WorkspaceParkSourceMode.Published, WorkspaceParkSourceMode.NoSourceChanges })
        foreach (var branch in new[] { "live", "exited", "absent" })
        foreach (var defect in new[] { "dirty", "sequencer", "ref", "head", "baseline", "path", "mode", "endpoint", "remote" })
        {
            if (mode == WorkspaceParkSourceMode.NoSourceChanges && defect is "endpoint" or "remote") continue;
            await using var world = new SeatWorld("Codex", localLane: true);
            var release = Typed(await world.QualifyAsync(), mode);
            world.Child.Kill = _ => { world.Child.Exit(); return Task.FromResult(true); };
            if (branch == "exited") world.Child.Exit();
            if (branch == "absent")
            {
                await world.Runtime.ReleaseSlotAsync(world.Tail.SessionId, "fixture exit", TimeSpan.Zero, CancellationToken.None);
                await world.RestartEmptyAsync();
            }
            var receipt = release.Publication!;
            var gitDir = (await world.Source.GitAsync(world.Source.Mirror, "rev-parse", "--path-format=absolute", "--git-dir")).Trim();
            switch (defect)
            {
                case "dirty": await File.WriteAllTextAsync(Path.Combine(world.Source.Mirror, "source.txt"), "dirty"); break;
                case "sequencer": await File.WriteAllTextAsync(Path.Combine(gitDir, "MERGE_HEAD"), world.Source.BaseSha); break;
                case "ref": await world.Source.GitAsync(world.Source.Mirror, "checkout", "-b", "wrong"); break;
                case "head": await world.Source.CommitAsync("source.txt", "advanced"); break;
                case "baseline":
                    if (mode == WorkspaceParkSourceMode.NoSourceChanges)
                    {
                        var tip = await world.Source.CommitAsync("source.txt", "advanced");
                        receipt = receipt with { SourceSha = tip };
                    }
                    else receipt = receipt with { Request = receipt.Request with { Binding = receipt.Request.Binding with { BaselineSha = new string('a', 40) } } };
                    break;
                case "path":
                    // Another clean checkout in the SAME common directory is still not this session.
                    var other = Path.Combine(world.Source.Root, "other-checkout");
                    await world.Source.GitAsync(world.Source.Repo, "worktree", "add", "--detach", other, world.Source.BaseSha);
                    receipt = receipt with { Request = receipt.Request with { Path = other } };
                    break;
                case "mode": receipt = receipt with { RemoteSha = mode == WorkspaceParkSourceMode.Published ? null : receipt.SourceSha }; break;
                case "endpoint": await world.Source.GitAsync(world.Source.Mirror, "remote", "set-url", "--push", "origin", world.Source.Origin + "-different"); break;
                case "remote":
                    var advanced = await world.Source.CommitAsync("source.txt", "remote advanced");
                    await world.Source.PushAsync();
                    await world.Source.GitAsync(world.Source.Mirror, "reset", "--hard", world.Source.BaseSha);
                    (await world.Source.RemoteShaAsync()).ShouldBe(advanced);
                    break;
            }
            release = release with { Publication = receipt };
            world.Source.Commands.Clear();
            var outcome = await world.ReleaseAsync(release);
            var guard = defect switch { "mode" => "G-210", "path" => "G-211", "baseline" => "G-213",
                "remote" => "G-214", _ => "G-215" };
            outcome.Outcome.ShouldBe(TerminalSeatReleaseOutcome.StaleObservation, guard + " " + mode + " " + branch + " " + defect);
            if (branch != "absent") world.AssertRetained(expectedLive: branch == "exited" ? 0 : 1);
            else world.Child.Kills.ShouldBe(1, "G-216 absent source verification never signals");
            if (branch == "exited") world.HasManifest.ShouldBeTrue("G-216 exited verification retains metadata");
            if (defect is "mode" or "path") world.Source.Commands.Count.ShouldBe(0, guard + " before Git");
            AssertNoPush(world, "G-212 refusal");
        }

        // A clean unpublished local commit is held, not repaired by a push during release.
        await using (var behind = new SeatWorld("Codex", localLane: true))
        {
            var release = Typed(await behind.QualifyAsync(), WorkspaceParkSourceMode.Published);
            var head = await behind.Source.CommitAsync("source.txt", "unpublished");
            release = release with { Publication = release.Publication! with { SourceSha = head, RemoteSha = head } };
            behind.Source.Commands.Clear();
            (await behind.ReleaseAsync(release)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.StaleObservation, "G-212");
            AssertNoPush(behind, "G-212");
            (await behind.Source.RemoteShaAsync()).ShouldBe(behind.Source.BaseSha);
            behind.AssertRetained();
        }
        foreach (var phoneHome in new[] { false, true })
        {
            await using var world = new SeatWorld("Codex", localLane: true);
            var release = Typed(await world.QualifyAsync(), WorkspaceParkSourceMode.NoSourceChanges);
            var surface = new CurrentSurface(world.Runtime) { MissingCapability = RunnerCapabilityFeatures.WorkspaceParkSourceModesV1 };
            await using var wire = await SeatWire.StartAsync(world, surface, phoneHome);
            (await wire.ReleaseAsync(release)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unsupported, "G-209");
            surface.ReleaseCalls.ShouldBe(0, "G-209");
            surface.MissingCapability = null;
            world.Runtime.LocalWorkspaceVerifier = null;
            (await wire.CapabilitiesAsync()).Features.ShouldNotContain(RunnerCapabilityFeatures.WorkspaceParkSourceModesV1, "G-217");
            (await world.ReleaseAsync(release)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unsupported, "G-209 no verifier");
            surface.ForceCalls.ShouldBe(0, "G-209");
            surface.GenerationKillCalls.ShouldBe(0, "G-209");
            world.AssertRetained();
        }
    }

    private static TerminalSeatReleaseRequest Typed(TerminalSeatReleaseRequest release, WorkspaceParkSourceMode mode) =>
        release with { ParkVersion = 2, Publication = release.Publication! with
        { SourceMode = mode, RemoteSha = mode == WorkspaceParkSourceMode.NoSourceChanges ? null : release.Publication.RemoteSha } };
    private static void AssertIdentityHeld(WorkspaceRepositoryIdentityResult result, string reason, string guard)
    {
        result.Outcome.ShouldBe(WorkspaceRepositoryIdentityOutcome.Held, guard);
        result.Reason.ShouldBe(reason, guard);
        result.Identity.ShouldBeNull(guard);
    }
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void AssertNoPush(SeatWorld world, string guard) => world.Source.Commands.ShouldNotContain(c => c[0] == "push", guard);
}
