using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;
using Antiphon.PtyHost.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// Private worlds deliberately follow CARD-0667's real tailer/owned child fixture.
// Every release qualified by this fixture carries a receipt from a real bare remote.
[Category("Integration")]
[NotInParallel("ClaudeConfigDirEnv")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedParkWireTests
{
    [Test]
    public async Task C1065_OldRunnerNeverReceivesFallbackKill()
    {
        ((int)PhoneHomeOperation.Input).ShouldBe(9);
        ((int)PhoneHomeOperation.ReleaseSlot).ShouldBe(23);
        ((int)PhoneHomeOperation.WorkspacePublish).ShouldBe(32);
        ((int)PhoneHomeOperation.CodexCliVersion).ShouldBe(33);
        ((int)PhoneHomeOperation.ObserveTerminalSeat).ShouldBe(34);
        ((int)PhoneHomeOperation.ReleaseTerminalSeat).ShouldBe(35);
        ((int)PhoneHomeOperation.WorkspacePark).ShouldBe(36);

        foreach (var phoneHome in new[] { false, true })
        foreach (var missing in new[] { RunnerCapabilityFeatures.WorkspaceParkV1,
            RunnerCapabilityFeatures.TerminalSeatReleaseV1, "version", "legacy", "none" })
        {
            await using var world = new SeatWorld("Codex");
            var release = await world.QualifyAsync();
            var current = new CurrentSurface(world.Runtime) { MissingCapability = missing };
            var legacy = new LegacySurface(world.Runtime);
            IPhoneHomeRuntimeSurface surface = missing == "legacy" ? legacy : current;
            await using var wire = await SeatWire.StartAsync(world, surface, phoneHome);
            world.Child.Kill = _ => { world.Child.Exit(); return Task.FromResult(true); };
            var command = new WorkspaceParkCommand(world.Tail.SessionId, Prepare: release.Publication!.Request,
                Version: missing == "version" ? 2 : 1);
            var published = await wire.ParkAsync(command);
            if (missing != "none")
            {
                published.Reason.ShouldBe("park_unsupported");
                current.ParkCalls.ShouldBe(0, missing == RunnerCapabilityFeatures.WorkspaceParkV1 ? "G-29" : "G-30");
                (await wire.ReleaseAsync(release with { ParkVersion = command.Version })).Outcome
                    .ShouldBe(TerminalSeatReleaseOutcome.Unsupported);
                current.ReleaseCalls.ShouldBe(0, "G-30");
                current.ForceCalls.ShouldBe(0, "G-31");
                current.GenerationKillCalls.ShouldBe(0, "G-31");
                legacy.ForceCalls.ShouldBe(0, "G-31");
                legacy.GenerationKillCalls.ShouldBe(0, "G-31");
                world.AssertRetained();
                continue;
            }
            published.Outcome.ShouldBe(WorkspaceParkOutcome.Published);
            current.Received.ShouldBe(command, "G-32");
            published.Receipt!.Request.ShouldBe(command.Prepare, "G-32");
            published.Receipt.SourceSha.ShouldBe(await world.Source.RemoteShaAsync(), "G-32");
            var verified = await wire.ParkAsync(new(world.Tail.SessionId, Verify: published.Receipt));
            verified.Receipt.ShouldBe(published.Receipt, "G-32");
            release = release with { Publication = verified.Receipt };
            (await wire.ReleaseAsync(release)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Released);
            current.ReleaseReceived.ShouldBe(release, "G-32");
            current.ReleaseReceived!.Publication!.ReceiptId.ShouldBe(published.Receipt.ReceiptId, "G-32");
            world.AssertReleased();
        }

        // The actual unconfigured local runtime is unsupported, even through its adapter.
        await using var local = new SessionRunnerRuntime(Options.Create(new SessionRunnerSettings
        { SessionLogPath = Path.Combine(Path.GetTempPath(), "c1065-unused-" + Guid.NewGuid().ToString("N")) }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var localAdapter = new PhoneHomeRuntimeAdapter(local, RunnerBuildIdentity.Resolve());
        (await localAdapter.ParkWorkspaceAsync(new(Guid.NewGuid()), CancellationToken.None)).Reason
            .ShouldBe("park_unsupported", "G-31");
    }

    [Test]
    public async Task C1065_ActivityOrReplacementInvalidatesParkRelease()
    {
        await Replacement_generation_is_never_released();
        await Token_for_another_session_is_refused();
        await Restart_invalidates_volatile_observation_tokens();
        await Old_turn_end_does_not_qualify_a_new_generation();
        await Working_remains_protected_after_arbitrary_silence();
        await Two_observations_require_the_full_safety_margin();
        await Unavailable_observation_discards_qualification();
        await Activity_resets_the_qualification_window();
        await AssertInputOrderingAsync(false);
        await AssertInputOrderingAsync(true);
        await Release_winning_the_gate_refuses_later_input();
        await Unknown_backend_custody_refuses_release();
        await Tail_growth_at_final_check_refuses_signal();
        await AssertProofComponentsAsync();
        await AssertFinalFencesAsync();
        await AssertPublicationReservationAsync();
    }

    private async Task AssertProofComponentsAsync()
    {
        await using var world = new SeatWorld("Codex");
        var release = await world.QualifyAsync();
        var proof = world.Runtime.TerminalSeatProofFor(world.Tail.SessionId)!;
        foreach (var component in new[] { "G-39", "G-42", "G-43", "G-44" })
        {
            var transcript = component switch
            {
                "G-39" => proof.Transcript with { Status = TerminalTranscriptReadStatus.Partial,
                    Verdict = TerminalTranscriptVerdict.Unknown },
                "G-42" => proof.Transcript with { BindingIdentity = "other-binding" },
                "G-43" => proof.Transcript with { FileRevision = "other-file" },
                _ => proof.Transcript with { TranscriptRevision = proof.Transcript.TranscriptRevision + 1 }
            };
            var decisionProof = component == "G-39" ? proof with { Transcript = transcript } : proof;
            TerminalSeatQualification.AuthorizeRelease(decisionProof, proof.RuntimeEpoch, proof.Session,
                release, transcript, proof.InputRevision, proof.OutputRevision, world.Clock)
                .ShouldBe(component == "G-39" ? TerminalSeatReleaseOutcome.Unknown
                    : TerminalSeatReleaseOutcome.StaleObservation, component);
        }
        await world.Tail.AppendAsync("{\"partial\":");
        (await world.ReleaseAsync(release)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unknown, "G-39");
        world.AssertRetained();

        await using var attempted = new SeatWorld("Codex");
        var pendingRelease = await attempted.QualifyAsync();
        var before = attempted.Session.BackendInput.Count;
        attempted.Child.Write = _ =>
        {
            attempted.Session.BackendInput.Count.ShouldBe(before + 1, "G-47");
            throw new IOException("failed attempted write");
        };
        await Should.ThrowAsync<IOException>(() => attempted.Runtime.SendInputAsync(
            attempted.Tail.SessionId, "uncertain input", CancellationToken.None));
        attempted.Session.BackendInput.Count.ShouldBe(before + 1, "G-47");
        (await attempted.ReleaseAsync(pendingRelease)).ConfirmsExit.ShouldBeFalse("G-47");
        attempted.AssertRetained();
    }

    private async Task AssertFinalFencesAsync()
    {
        foreach (var guard in new[] { "G-52", "G-53", "G-54", "G-55", "G-56",
            "G-57", "G-58", "G-59", "G-60", "valid" })
        {
            await using var world = new SeatWorld("Codex");
            var release = await world.QualifyAsync();
            world.Child.Kill = _ => { world.Child.Exit(); return Task.FromResult(true); };
            if (guard is "G-57" or "G-58" or "G-59" or "G-60")
                world.Runtime.TerminalParkBeforeVerification = async _ =>
                {
                    switch (guard)
                    {
                        case "G-57":
                            await world.Source.CommitAsync("advanced.txt", "new committed source");
                            await world.Source.PushAsync();
                            break;
                        case "G-58": await File.WriteAllTextAsync(Path.Combine(world.Source.Mirror, "dirty.cs"), "untracked source"); break;
                        case "G-59": await world.Source.GitAsync(world.Source.Mirror, "checkout", "-b", "different-ref"); break;
                        case "G-60":
                            var other = Path.Combine(world.Source.Root, "other.git");
                            await world.Source.GitAsync(world.Source.Root, "clone", "--bare", world.Source.Origin, other);
                            await world.Source.GitAsync(world.Source.Mirror, "remote", "set-url", "--push", "origin", other);
                            break;
                    }
                };
            else
                world.Runtime.TerminalReleaseBeforeSignal = _ =>
                {
                    switch (guard)
                    {
                        case "G-52": world.TrackReplacement(release.Observation.ExpectedAcceptedStartedAt); break;
                        case "G-53": world.Session.BindAcceptedGeneration(release.Observation.ExpectedAcceptedStartedAt.AddSeconds(1)); break;
                        case "G-54": world.Runtime.DetachTerminalTailerForTest(world.Tail.SessionId); break;
                        case "G-55": world.Session.BackendInput.Enqueue("racing input"); break;
                        case "G-56": world.Session.OutputForTest(world.Session.LastSequence + 1); break;
                    }
                    return Task.CompletedTask;
                };
            var result = await world.ReleaseAsync(release);
            if (guard == "valid")
            {
                result.Outcome.ShouldBe(TerminalSeatReleaseOutcome.Released);
                world.AssertReleased();
            }
            else
            {
                result.ConfirmsExit.ShouldBeFalse(guard);
                world.Child.Kills.ShouldBe(0, guard);
                world.AssertRetained();
            }
        }
    }

    private async Task AssertPublicationReservationAsync()
    {
        foreach (var conditional in new[] { false, true })
        {
            await using var world = new SeatWorld("Codex");
            var releaseRequest = await world.QualifyAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            world.Runtime.TerminalParkBeforeVerification = async _ =>
            { entered.TrySetResult(); await finish.Task.WaitAsync(deadline.Token); };
            // Failed exit keeps a live record, so later input is refused by the reservation.
            var releasing = world.ReleaseAsync(releaseRequest);
            Task? writing = null;
            var before = world.Child.Inputs.Count;
            try
            {
                await entered.Task.WaitAsync(deadline.Token);
                writing = conditional ? ConditionalAsync() : world.Runtime.SendInputAsync(
                    world.Tail.SessionId, "\r", deadline.Token);
                writing.IsCompleted.ShouldBeFalse("G-61");
                world.Child.Inputs.Count.ShouldBe(before, "G-61");
            }
            finally
            {
                finish.TrySetResult();
                (await releasing).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unresolved);
                if (writing is not null)
                {
                    if (conditional) await writing;
                    else await Should.ThrowAsync<SessionRunnerRuntime.InputRefusedException>(() => writing);
                }
            }
            world.Child.Inputs.Count.ShouldBe(before, "G-61");
            world.AssertRetained(1);

            async Task ConditionalAsync() =>
                (await world.Runtime.SendConditionalInputAsync(world.Tail.SessionId,
                    new(releaseRequest.Observation.ExpectedAcceptedStartedAt, world.Session.LastSequence, "\r"),
                    deadline.Token)).Outcome.ShouldBe(SessionRunnerRuntime.ReleaseInProgressInputOutcome, "G-61");
        }
    }

    [Test]
    public async Task C1065_ExitUnconfirmedRetainsSeatAndCustody()
    {
        await Kill_failure_retains_manifest_and_capacity();
        await Duplicate_action_is_idempotent();
        await Confirmed_exit_forgets_only_the_expected_generation();
        await using var world = new SeatWorld("Codex");
        var release = await world.QualifyAsync();
        world.RecordGeneration();
        // A restarted runtime has not adopted the still-manifested child. Its empty List
        // cannot turn incomplete inventory into authoritative absence.
        await world.RestartEmptyAsyncForReplacement();
        (await world.ReleaseAsync(release)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unknown, "G-67");
        world.Child.Kills.ShouldBe(0, "G-67");
        world.ReadGenerationFromDisk().ShouldBe(release.Observation.ExpectedAcceptedStartedAt, "G-66");
        world.AssertUnadoptedManifest();

        // Lose the serialized reply after a real release. Retry the same identity only:
        // runtime receipt, durable watermark, transcript and neighboring seat remain intact.
        await using var lost = new SeatWorld("Codex");
        var lostRequest = await lost.QualifyAsync();
        lost.RecordGeneration();
        lost.Child.Kill = _ => { lost.Child.Exit(); return Task.FromResult(true); };
        await using var wire = await SeatWire.StartAsync(lost, new CurrentSurface(lost.Runtime), true);
        _ = await wire.ReleaseAsync(lostRequest);
        (await wire.ReleaseAsync(lostRequest)).ConfirmsExit.ShouldBeTrue("G-64");
        lost.Child.Kills.ShouldBe(1, "G-64");
        lost.AssertReleased();
    }

    private async Task Activity_resets_the_qualification_window()
    {
        await using var world = new SeatWorld("Codex");
        await world.QualifyAsync();
        await world.Tail.AppendAsync(world.Tail.Prompt("next prompt", "next")
            + world.Tail.Activity("next") + world.Tail.End("next"));
        (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Waiting);
        world.Clock.Advance(TimeSpan.FromSeconds(120));
        (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        var proof = world.Runtime.TerminalSeatProofFor(world.Tail.SessionId)!;
        foreach (var component in new[] { "binding", "file", "transcript", "bytes", "prompt", "end", "input", "output" })
        {
            var qualification = new TerminalSeatQualification();
            qualification.Observe(proof.RuntimeEpoch, proof.Session, proof.Request, proof.Transcript,
                proof.InputRevision, proof.OutputRevision, world.Clock).Status.ShouldBe(TerminalSeatQualificationStatus.Waiting);
            world.Clock.Advance(TimeSpan.FromSeconds(120));
            qualification.Observe(proof.RuntimeEpoch, proof.Session, proof.Request, proof.Transcript,
                proof.InputRevision, proof.OutputRevision, world.Clock).Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);

            var request = proof.Request;
            var transcript = component switch
            {
                "binding" => proof.Transcript with { BindingIdentity = "new-binding" },
                "file" => proof.Transcript with { FileRevision = "new-file-revision" },
                "transcript" => proof.Transcript with { TranscriptRevision = proof.Transcript.TranscriptRevision + 1 },
                "bytes" => proof.Transcript with { ConsumedBytes = proof.Transcript.ConsumedBytes + 1 },
                "prompt" => proof.Transcript with { LastPromptRevision = proof.Transcript.LastPromptRevision + 1 },
                "end" => proof.Transcript with { LastEndRevision = proof.Transcript.LastEndRevision + 1 },
                _ => proof.Transcript
            };
            // Keep the prompt evidence valid when changing the bound identity so the
            // qualification reset is observed, rather than masked by OldPrompt.
            if (component == "binding") request = request with { PromptBindingIdentity = transcript.BindingIdentity! };
            var input = proof.InputRevision + (component == "input" ? 1 : 0);
            var output = proof.OutputRevision + (component == "output" ? 1 : 0);
            var changed = qualification.Observe(proof.RuntimeEpoch, proof.Session, request, transcript, input, output, world.Clock);
            changed.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting, component);
            changed.StableFor.ShouldBe(TimeSpan.Zero, component);
            changed.FirstObservedAt.ShouldBe(world.Clock.GetUtcNow(), component);
            changed.Token.ShouldBeNull(component);
            world.Clock.Advance(TimeSpan.FromMilliseconds(119999));
            qualification.Observe(proof.RuntimeEpoch, proof.Session, request, transcript, input, output, world.Clock)
                .Status.ShouldBe(TerminalSeatQualificationStatus.Waiting, component);
            world.Clock.Advance(TimeSpan.FromMilliseconds(1));
            qualification.Observe(proof.RuntimeEpoch, proof.Session, request, transcript, input, output, world.Clock)
                .Status.ShouldBe(TerminalSeatQualificationStatus.Qualified, component);
        }
        world.AssertRetained();
    }

    private async Task Input_winning_the_gate_invalidates_release()
    {
        await AssertInputOrderingAsync(conditional: false);
    }

    private async Task Conditional_input_invalidates_release()
    {
        await AssertInputOrderingAsync(conditional: true);
    }

    private static async Task AssertInputOrderingAsync(bool conditional)
    {
        await using var world = new SeatWorld("Codex");
        var request = await world.QualifyAsync();
        var before = world.Child.Inputs.Count;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task? input = null;
        var gate = world.Runtime.LaunchGateForTest(world.Tail.SessionId);
        await gate.WaitAsync(deadline.Token);
        try
        {
            world.Session.TerminalReleaseInProgress.ShouldBeFalse();
            // Calling the async route synchronously reaches either its gate wait or the
            // fake child. No scheduler delay stands in for evidence of exclusion.
            input = WriteAsync("\r");
            world.Child.Inputs.Count.ShouldBe(before, conditional ? "G-46" : "G-45");
            input.IsCompleted.ShouldBeFalse("input is waiting for the shared launch gate");
        }
        finally
        {
            gate.Release();
            if (input is not null) await input;
        }
        world.Child.Inputs.Skip(before).ShouldBe(new[] { "\r" });
        // Native transcript/output are unchanged, the composer is clear, and only the
        // completed input revision can invalidate this otherwise valid release token.
        (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.StaleObservation);
        world.AssertRetained();

        // Both entry points must preserve the caller's LF/bracketed-paste body and a
        // separate Enter exactly; the launch gate must not normalize or coalesce input.
        const string body = "\u001b[200~first line\nsecond line\u001b[201~";
        await WriteAsync(body);
        await WriteAsync("\r");
        world.Child.Inputs.TakeLast(2).ShouldBe(new[] { body, "\r" });

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Child.Write = async _ =>
        {
            entered.TrySetResult();
            await finish.Task.WaitAsync(deadline.Token);
        };
        var pending = WriteAsync("held composer body");
        Task<TerminalSeatReleaseResult>? release = null;
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            release = world.ReleaseAsync(request);
            release.IsCompleted.ShouldBeFalse("release waits for the active input owner");
            world.Child.Kills.ShouldBe(0);
        }
        finally
        {
            finish.TrySetResult();
            await pending;
            if (release is not null)
                (await release).Outcome.ShouldBe(TerminalSeatReleaseOutcome.PendingDelivery,
                    "the unsubmitted composer still vetoes release after the writer leaves");
            world.Child.Write = null;
        }
        world.AssertRetained();

        async Task WriteAsync(string text)
        {
            if (conditional)
                (await world.Runtime.SendConditionalInputAsync(world.Tail.SessionId,
                    new(request.Observation.ExpectedAcceptedStartedAt, world.Session.LastSequence, text), deadline.Token))
                    .Outcome.ShouldBe(ConditionalInputOutcomes.Written);
            else
                await world.Runtime.SendInputAsync(world.Tail.SessionId, text, deadline.Token);
        }
    }

    private async Task Release_winning_the_gate_refuses_later_input()
    {
        await using var world = new SeatWorld("Codex");
        var request = await world.QualifyAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Child.Kill = async _ =>
        {
            entered.TrySetResult();
            await finish.Task.WaitAsync(deadline.Token);
            return false; // Retain a live generation so Exited/Missing cannot mask PC-37.
        };
        var release = world.ReleaseAsync(request);
        Task? normal = null;
        Task<RunnerConditionalInputResult>? conditional = null;
        var before = world.Child.Inputs.Count;
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            world.Session.HasExited.ShouldBeFalse();
            SessionRunnerRuntime.AuthorizeInputUnderGate(world.Session)
                .ShouldBe(SessionRunnerRuntime.InputAuthorization.ReleaseInProgress,
                    "G-48");
            normal = world.Runtime.SendInputAsync(world.Tail.SessionId, "later normal", deadline.Token);
            conditional = world.Runtime.SendConditionalInputAsync(world.Tail.SessionId,
                new(request.Observation.ExpectedAcceptedStartedAt, world.Session.LastSequence, "later conditional"), deadline.Token);
            normal.IsCompleted.ShouldBeFalse();
            conditional.IsCompleted.ShouldBeFalse();
            world.Child.Inputs.Count.ShouldBe(before);
        }
        finally
        {
            finish.TrySetResult();
            (await release).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unresolved);
            if (normal is not null)
                (await Should.ThrowAsync<SessionRunnerRuntime.InputRefusedException>(() => normal))
                    .Outcome.ShouldBe(SessionRunnerRuntime.InputAuthorization.ReleaseInProgress);
            if (conditional is not null)
                (await conditional).Outcome.ShouldBe(SessionRunnerRuntime.ReleaseInProgressInputOutcome);
        }
        world.Child.Inputs.Count.ShouldBe(before, "G-48");
        world.AssertRetained(expectedKills: 1);
    }

    private async Task Replacement_generation_is_never_released()
    {
        foreach (var replaceStore in new[] { true, false })
        {
            await using var world = new SeatWorld("Codex");
            var request = await world.QualifyAsync();
            if (replaceStore)
                request = request with { Observation = request.Observation with { ExpectedRunnerStoreId = Guid.NewGuid() } };
            else
                world.Session.BindAcceptedGeneration(request.Observation.ExpectedAcceptedStartedAt.AddSeconds(1));
            world.Runtime.TerminalGenerationMatches(world.Session, request.Observation)
                .ShouldBeFalse(replaceStore ? "G-33" : "G-34");
            (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.GenerationMismatch,
                replaceStore ? "G-33" : "G-34");
            world.AssertRetained();
        }
    }

    private async Task Token_for_another_session_is_refused()
    {
        await using var first = new SeatWorld("Codex");
        var foreign = await first.QualifyAsync();
        await using var second = new SeatWorld("Codex");
        var own = await second.QualifyAsync();
        var proof = second.Runtime.TerminalSeatProofFor(second.Tail.SessionId)!;
        TerminalSeatQualification.AuthorizeRelease(proof, proof.RuntimeEpoch, proof.Session, own,
            proof.Transcript, proof.InputRevision, proof.OutputRevision, second.Clock).ShouldBeNull();
        // Same evidence/token, only the session object differs. The token dictionary must not
        // mask a missing object fence in the production authorization decision (PC-19).
        TerminalSeatQualification.AuthorizeRelease(proof, proof.RuntimeEpoch, first.Session, own,
            proof.Transcript, proof.InputRevision, proof.OutputRevision, second.Clock)
            .ShouldBe(TerminalSeatReleaseOutcome.StaleObservation, "G-35");
        (await second.ReleaseAsync(own with { Token = foreign.Token })).Outcome
            .ShouldBe(TerminalSeatReleaseOutcome.StaleObservation);
        first.AssertRetained();
        second.AssertRetained();
    }

    private async Task Working_remains_protected_after_arbitrary_silence()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using var world = new SeatWorld(provider);
            var request = await world.QualifyAsync();
            await world.Tail.AppendAsync(world.Tail.Activity("still-working"));
            world.Clock.Advance(TimeSpan.FromHours(1));
            var proof = world.Runtime.TerminalSeatProofFor(world.Tail.SessionId)!;
            var working = await world.Tail.ObserveAsync();
            working.Verdict.ShouldBe(TerminalTranscriptVerdict.Working);
            // Only Working prevents this otherwise matching aged proof from passing. The
            // end-to-end check below independently rejects activity after an issued idle token.
            TerminalSeatQualification.AuthorizeRelease(proof with { Transcript = working },
                proof.RuntimeEpoch, proof.Session, request, working, proof.InputRevision,
                proof.OutputRevision, world.Clock).ShouldBe(TerminalSeatReleaseOutcome.Working, "G-38");
            (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Working, "G-51");
            world.AssertRetained();
        }
    }

    private async Task Unknown_backend_custody_refuses_release()
    {
        foreach (var condition in new[] { "launch", "adoption", "external", "composer", "failed-input", "pending-input" })
        {
            await using var world = new SeatWorld("Codex");
            var request = await world.QualifyAsync();
            world.Session.SetTerminalBackendStateForTest(
                status: condition == "launch" ? "Starting" : "Running",
                pendingReason: condition == "adoption" ? "AdoptionPending" : null,
                backend: condition == "external" ? SessionBackends.Herdr : SessionBackends.PtyHost);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? pending = null;
            try
            {
                if (condition == "composer")
                    await world.Runtime.SendInputAsync(world.Tail.SessionId, "unsubmitted body", CancellationToken.None);
                if (condition == "failed-input")
                {
                    world.Child.Write = _ => throw new IOException("uncertain input");
                    await Should.ThrowAsync<IOException>(() => world.Runtime.SendInputAsync(
                        world.Tail.SessionId, "uncertain body", CancellationToken.None));
                }
                if (condition == "pending-input")
                {
                    world.Child.Write = _ => { entered.TrySetResult(); return finish.Task; };
                    pending = world.Runtime.SendInputAsync(world.Tail.SessionId, "pending body", CancellationToken.None);
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                var release = world.ReleaseAsync(request);
                if (condition == "pending-input")
                {
                    // S2b serializes release behind the writer. Finish the body first;
                    // the unchanged pending-composer assertion below still owns the veto.
                    release.IsCompleted.ShouldBeFalse();
                    finish.TrySetResult();
                    await pending!;
                }
                (await release).Outcome.ShouldBe(
                    condition is "composer" or "failed-input" or "pending-input"
                        ? TerminalSeatReleaseOutcome.PendingDelivery : TerminalSeatReleaseOutcome.Unknown,
                    condition is "composer" or "failed-input" or "pending-input" ? "G-49" : "G-50");
            }
            finally
            {
                finish.TrySetResult();
                if (pending is not null) await pending;
            }
            world.AssertRetained();
        }
    }

    private async Task Tail_growth_at_final_check_refuses_signal()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using var world = new SeatWorld(provider);
            var request = await world.QualifyAsync();
            world.Runtime.TerminalReleaseBeforeFinalCheck = _ =>
                world.Tail.AppendAsync(world.Tail.Prompt("racing native prompt", "racing"));
            (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Working);
            world.AssertRetained();
        }
    }

    private async Task Output_growth_at_signal_boundary_refuses_release()
    {
        await using var world = new SeatWorld("Codex");
        var request = await world.QualifyAsync();
        world.Runtime.TerminalReleaseBeforeSignal = _ =>
        {
            world.Session.OutputForTest(world.Session.LastSequence + 1);
            return Task.CompletedTask;
        };
        (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.StaleObservation);
        world.AssertRetained();
    }

    private async Task Kill_failure_retains_manifest_and_capacity()
    {
        foreach (var failure in new[] { "throw", "cancel", "non-exit", "unconfirmed-true" })
        {
            await using var world = new SeatWorld("Codex");
            var request = await world.QualifyAsync();
            world.Child.Kill = _ => failure switch
            {
                "throw" => throw new IOException("fixture kill failed"),
                "cancel" => throw new OperationCanceledException(),
                _ => Task.FromResult(failure == "unconfirmed-true")
            };
            var result = await world.ReleaseAsync(request);
            result.Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unresolved, failure is "throw" or "cancel" ? "G-63" : "G-62");
            result.ConfirmsExit.ShouldBeFalse();
            world.AssertRetained(expectedKills: 1);
            world.ReleaseAuditCount.ShouldBe(0);
            (await world.ReleaseAsync(request)).ShouldBe(result, "G-64");
            world.Child.Kills.ShouldBe(1);
        }
    }

    private async Task Duplicate_action_is_idempotent()
    {
        await using var world = new SeatWorld("Codex");
        var request = await world.QualifyAsync();
        world.Child.Kill = _ => { world.Child.Exit(); return Task.FromResult(true); };
        var first = await world.ReleaseAsync(request);
        first.Outcome.ShouldBe(TerminalSeatReleaseOutcome.Released);
        (await world.ReleaseAsync(request)).ShouldBe(first);
        world.Child.Kills.ShouldBe(1);
        world.ReleaseAuditCount.ShouldBe(1);
        world.TrackReplacement(request.Observation.ExpectedAcceptedStartedAt.AddSeconds(1));
        (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.GenerationMismatch,
            "G-65");
        // A new expected generation cannot reuse the old action identity either.
        (await world.ReleaseAsync(request with { Observation = request.Observation with
        { ExpectedAcceptedStartedAt = request.Observation.ExpectedAcceptedStartedAt.AddSeconds(1) } })).Outcome
            .ShouldBe(TerminalSeatReleaseOutcome.GenerationMismatch);
        world.Child.Kills.ShouldBe(1);
        world.Runtime.LiveSessionCount.ShouldBe(1);
        world.ReleaseAuditCount.ShouldBe(1);
    }

    private async Task Confirmed_exit_forgets_only_the_expected_generation()
    {
        await using var world = new SeatWorld("Codex");
        var request = await world.QualifyAsync();
        var foreign = world.TrackForeign();
        world.RecordGeneration();
        world.Child.Kill = _ => { world.Child.Exit(); return Task.FromResult(true); };
        (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Released);
        world.AssertReleased();
        world.Runtime.List().Select(s => s.SessionId).ShouldBe(new[] { foreign });
        world.Runtime.LiveSessionCount.ShouldBe(1);
        world.AssertForeignRetained(foreign);
        world.ReadGenerationFromDisk().ShouldBe(request.Observation.ExpectedAcceptedStartedAt, "G-66");
        // New runtime/store instance reads the real durable watermark after session eviction.
        await world.RestartEmptyAsync();
        world.ReadGenerationFromDisk().ShouldBe(request.Observation.ExpectedAcceptedStartedAt);
        (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.AlreadyAbsent);
        world.Child.Kills.ShouldBe(1);

        await using var exited = new SeatWorld("Codex");
        var exitedRequest = await exited.QualifyAsync();
        exited.Child.Exit();
        (await exited.ReleaseAsync(exitedRequest)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.AlreadyExited);
        exited.AssertReleased(expectedKills: 0);
    }

    private async Task Two_observations_require_the_full_safety_margin()
    {
        await using var world = new SeatWorld("Codex");
        await world.StartAsync();
        await world.DeliverAsync();
        var first = await world.ObserveAsync();
        first.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting);
        first.StableFor.ShouldBe(TimeSpan.Zero);
        first.Token.ShouldBeNull();
        world.ServerClock.Advance(TimeSpan.FromDays(2));
        (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Waiting,
            "the server clock cannot supply runner elapsed time");
        world.Clock.Advance(TimeSpan.FromMilliseconds(119999));
        var early = await world.ObserveAsync();
        early.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting, "G-40");
        early.Token.ShouldBeNull();
        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var qualified = await world.ObserveAsync();
        qualified.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified, "exactly 120 seconds qualifies");
        qualified.StableFor.ShouldBe(TimeSpan.FromSeconds(120));
        qualified.Token.ShouldNotBeNullOrWhiteSpace();
        qualified.FirstObservedAt.ShouldBe(first.FirstObservedAt);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var later = await world.ObserveAsync();
        later.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        later.Token.ShouldBe(qualified.Token, "unchanged evidence retains its opaque token");
        world.AssertRetained();
    }

    private async Task Old_turn_end_does_not_qualify_a_new_generation()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using var world = new SeatWorld(provider);
            await world.StartAsync();
            // Old history is idle, but none of it follows the current delivery floor.
            var old = await world.ObserveAsync();
            old.Status.ShouldBe(TerminalSeatQualificationStatus.OldPrompt, "G-37 " + provider);
            world.Clock.Advance(TimeSpan.FromHours(1));
            (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.OldPrompt);
            await world.DeliverAsync();
            (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Waiting);
            world.Clock.Advance(TimeSpan.FromSeconds(120));
            (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Qualified,
                "a real later delivered prompt and end must be accepted: " + provider);
            world.Child.Inputs.ShouldContain(SeatWorld.TaskPrompt);
            world.AssertRetained();
        }
    }

    private async Task Unavailable_observation_discards_qualification()
    {
        await using var world = new SeatWorld("Grok");
        await world.StartAsync();
        await world.DeliverAsync();
        (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Waiting);
        world.Clock.Advance(TimeSpan.FromSeconds(60));
        world.Tail.Observer.OpenRead = _ => throw new IOException("fixture unavailable");
        var unavailable = await world.ObserveAsync();
        unavailable.Status.ShouldBe(TerminalSeatQualificationStatus.Unknown);
        unavailable.Token.ShouldBeNull();
        world.Tail.Observer.OpenRead = null;
        world.Clock.Advance(TimeSpan.FromSeconds(60));
        var recovered = await world.ObserveAsync();
        recovered.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting,
            "G-41");
        recovered.StableFor.ShouldBe(TimeSpan.Zero);
        world.Clock.Advance(TimeSpan.FromSeconds(120));
        (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        world.AssertRetained();
    }

    private async Task Restart_invalidates_volatile_observation_tokens()
    {
        await using var world = new SeatWorld("Claude");
        await world.StartAsync();
        await world.DeliverAsync();
        await world.ObserveAsync();
        world.Clock.Advance(TimeSpan.FromSeconds(120));
        var before = await world.ObserveAsync();
        before.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        before.Token.ShouldNotBeNullOrWhiteSpace();
        (await world.AuthorizeAsync(before.Token)).Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        (await world.AuthorizeAsync("not-an-issued-token")).Status.ShouldBe(TerminalSeatQualificationStatus.StaleObservation);
        var proof = world.Runtime.TerminalSeatProofFor(world.Tail.SessionId)!;
        await world.RestartAsync();
        (await world.AuthorizeAsync(before.Token)).Status.ShouldBe(TerminalSeatQualificationStatus.StaleObservation,
            "a prior-process token cannot authorize the restarted runtime");
        var restarted = await world.ObserveAsync();
        restarted.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting, "restart requires a new runner window");
        restarted.Token.ShouldBeNull();
        var newEpoch = world.Runtime.TerminalSeatProofFor(world.Tail.SessionId)!.RuntimeEpoch;
        TerminalSeatQualification.Authorize(proof, proof.RuntimeEpoch, proof.Session, proof.Request,
            proof.Transcript, proof.InputRevision, proof.OutputRevision, world.Clock)
            .ShouldBe(TerminalSeatQualificationStatus.Qualified, "otherwise-valid control for the epoch guard");
        TerminalSeatQualification.Authorize(proof, newEpoch, proof.Session, proof.Request,
            proof.Transcript, proof.InputRevision, proof.OutputRevision, world.Clock)
            .ShouldBe(TerminalSeatQualificationStatus.StaleObservation,
                "G-36");
        world.Clock.Advance(TimeSpan.FromSeconds(120));
        var qualified = await world.ObserveAsync();
        qualified.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        qualified.Token.ShouldNotBe(before.Token, "a restarted runtime must not recreate an old token");
        world.AssertRetained();
    }

    private class LegacySurface(SessionRunnerRuntime runtime) : IPhoneHomeRuntimeSurface
    {
        protected IPhoneHomeRuntimeSurface Adapter { get; } = new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve());
        public int ForceCalls { get; private set; }
        public int GenerationKillCalls { get; private set; }
        public virtual RunnerCapabilitiesDto Capabilities() => Adapter.Capabilities() with { Features = [] };
        public string Health() => Adapter.Health();
        public IReadOnlyList<RunnerSessionDto> List() => Adapter.List();
        public Task<RunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => Adapter.GetAsync(id, ct);
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new InvalidOperationException("No provider launch permitted");
        public RunnerBufferDto GetBuffer(Guid id) => Adapter.GetBuffer(id);
        public RunnerSnapshotDto GetSnapshot(Guid id) => Adapter.GetSnapshot(id);
        public RunnerTranscriptDto GetTranscript(Guid id) => Adapter.GetTranscript(id);
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => Adapter.SendInputAsync(id, input, ct);
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid id, RunnerConditionalInputRequest request, CancellationToken ct) => Adapter.SendConditionalInputAsync(id, request, ct);
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => Adapter.ClearLiveBufferAsync(id, ct);
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => Adapter.ResizeAsync(id, cols, rows, ct);
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid id, DateTime generation, CancellationToken ct)
        { GenerationKillCalls++; return Adapter.KillGenerationAsync(id, generation, ct); }
        public Task<RunnerSessionDto> ReleaseSlotAsync(Guid id, string reason, CancellationToken ct)
        { ForceCalls++; return Adapter.ReleaseSlotAsync(id, reason, ct); }
        public int OwnedSessionCount => Adapter.OwnedSessionCount;
    }

    private sealed class CurrentSurface(SessionRunnerRuntime runtime) : LegacySurface(runtime), IPhoneHomeRuntimeSurface
    {
        public override RunnerCapabilitiesDto Capabilities() => Adapter.Capabilities() with
        { Features = Adapter.Capabilities().Features!.Where(x => x != MissingCapability).ToArray() };
        public Task<TerminalSeatObservation> ObserveTerminalSeatAsync(Guid id, TerminalSeatObservationRequest request, CancellationToken ct) =>
            Adapter.ObserveTerminalSeatAsync(id, request, ct);
        public WorkspaceParkCommand? Received { get; private set; }
        public TerminalSeatReleaseRequest? ReleaseReceived { get; private set; }
        public int ParkCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public string? MissingCapability { get; set; }
        public Task<WorkspaceParkResult> ParkWorkspaceAsync(WorkspaceParkCommand request, CancellationToken ct)
        { ParkCalls++; Received = request; return Adapter.ParkWorkspaceAsync(request, ct); }
        public Task<TerminalSeatReleaseResult> ReleaseTerminalSeatAsync(Guid id, TerminalSeatReleaseRequest request, CancellationToken ct)
        { ReleaseCalls++; ReleaseReceived = request; return Adapter.ReleaseTerminalSeatAsync(id, request, ct); }
    }

    private sealed class SeatWire(SeatWorld world, IPhoneHomeRuntimeSurface surface, bool phoneHome) : IAsyncDisposable
    {
        private readonly PhoneHomeCommandDispatcher _dispatcher = new(surface, new PhoneHomeSettings());
        private WebApplication? _app;
        private HttpClient? _http;

        public static async Task<SeatWire> StartAsync(SeatWorld world, IPhoneHomeRuntimeSurface surface, bool phoneHome)
        {
            var wire = new SeatWire(world, surface, phoneHome);
            if (phoneHome) return wire;
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Testing" });
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton(world.Runtime);
                builder.Services.AddSingleton(surface);
                builder.Services.Configure<HerdrSettings>(_ => { });
                builder.Services.Configure<HostStatsSettings>(_ => { });
                wire._app = builder.Build();
                wire._app.MapRunnerCapabilitiesRoute(RunnerBuildIdentity.Resolve());
                wire._app.MapTerminalSeatReleaseRoutes();
                await wire._app.StartAsync();
                var address = wire._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                var uri = new Uri(address);
                uri.IsLoopback.ShouldBeTrue();
                uri.Port.ShouldNotBe(17204);
                wire._http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(10) };
                return wire;
            }
            catch { await wire.DisposeAsync(); throw; }
        }

        public async Task<PhoneHomeFrame> DispatchAsync(PhoneHomeOperation operation, object body)
        {
            var request = new PhoneHomeFrame(PhoneHomeFrameKind.Request, 667, Guid.NewGuid(), operation,
                JsonSerializer.SerializeToElement(body, PhoneHomeFraming.Json));
            // Exercise both frame directions, with production framing JSON (not the HTTP defaults).
            request = JsonSerializer.Deserialize<PhoneHomeFrame>(JsonSerializer.Serialize(request, PhoneHomeFraming.Json), PhoneHomeFraming.Json)!;
            var result = await _dispatcher.DispatchAsync(request, CancellationToken.None);
            result.Epoch.ShouldBe(request.Epoch);
            result.RequestId.ShouldBe(request.RequestId);
            result.Operation.ShouldBe(operation);
            return JsonSerializer.Deserialize<PhoneHomeFrame>(JsonSerializer.Serialize(result, PhoneHomeFraming.Json), PhoneHomeFraming.Json)!;
        }

        public async Task<WorkspaceParkResult> ParkAsync(WorkspaceParkCommand request) => phoneHome
            ? ReadResult<WorkspaceParkResult>(await DispatchAsync(PhoneHomeOperation.WorkspacePark, request))
            : await PostAsync<WorkspaceParkResult>("workspace-park", request);

        public async Task<TerminalSeatObservation> ObserveAsync() => phoneHome
            ? ReadResult<TerminalSeatObservation>(await DispatchAsync(PhoneHomeOperation.ObserveTerminalSeat,
                new PhoneHomeTerminalSeatObservationRequest(world.Tail.SessionId, world.Request)))
            : await PostAsync<TerminalSeatObservation>("terminal-seat-observation", world.Request);

        public async Task<TerminalSeatReleaseResult> ReleaseAsync(TerminalSeatReleaseRequest request) => phoneHome
            ? ReadResult<TerminalSeatReleaseResult>(await DispatchAsync(PhoneHomeOperation.ReleaseTerminalSeat,
                new PhoneHomeTerminalSeatReleaseRequest(world.Tail.SessionId, request)))
            : await PostAsync<TerminalSeatReleaseResult>("release-terminal-seat", request);

        public async Task<RunnerCapabilitiesDto> CapabilitiesAsync() => phoneHome
            ? ReadResult<RunnerCapabilitiesDto>(await DispatchAsync(PhoneHomeOperation.Capabilities, new { }))
            : (await _http!.GetFromJsonAsync<RunnerCapabilitiesDto>("/capabilities"))!;

        public async Task AssertObservationUnsupportedAsync()
        {
            if (phoneHome)
            {
                var refused = await DispatchAsync(PhoneHomeOperation.ObserveTerminalSeat,
                    new PhoneHomeTerminalSeatObservationRequest(world.Tail.SessionId, world.Request));
                refused.Kind.ShouldBe(PhoneHomeFrameKind.Error);
                refused.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedOperation);
                refused.StatusCode.ShouldBe(409);
            }
            else
            {
                using var refused = await _http!.PostAsJsonAsync($"/sessions/{world.Tail.SessionId}/terminal-seat-observation", world.Request);
                refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
                problem.RootElement.GetProperty("type").GetString().ShouldBe(PhoneHomeProblemTypes.UnsupportedOperation);
            }
        }

        private static T ReadResult<T>(PhoneHomeFrame frame)
        {
            frame.Kind.ShouldBe(PhoneHomeFrameKind.Result, frame.ErrorCode);
            return frame.Payload!.Value.Deserialize<T>(PhoneHomeFraming.Json)!;
        }

        private async Task<T> PostAsync<T>(string route, object request)
        {
            using var reply = await _http!.PostAsJsonAsync($"/sessions/{world.Tail.SessionId}/{route}", request);
            reply.StatusCode.ShouldBe(HttpStatusCode.OK, "conditional runtime transport must be wired");
            return (await reply.Content.ReadFromJsonAsync<T>())!;
        }

        public async ValueTask DisposeAsync()
        {
            _http?.Dispose();
            if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        }
    }

    private sealed class World : IDisposable
    {
        public const string Branch = "feat/card-task-deadbeef";
        public const string FullRef = "refs/heads/" + Branch;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c1065-" + Guid.NewGuid().ToString("N"));
        public string Origin => Path.Combine(Root, "origin.git");
        public string Repo => Path.Combine(Root, "repo");
        public string Work => Path.Combine(Root, "work").Replace('\\', '/');
        public string Mirror => Work + "/worktrees/task-deadbeef";
        public string BaseSha { get; private set; } = "";
        public WorkspaceParkRequest Request { get; private set; } = null!;
        public List<string[]> Commands { get; } = [];
        public Action<ProcessStartInfo>? BeforeStart { get; set; }

        public static async Task<World> CreateAsync()
        {
            var w = new World();
            try
            {
                Directory.CreateDirectory(w.Root);
                Directory.CreateDirectory(w.Repo);
                Directory.CreateDirectory(Path.GetDirectoryName(w.Mirror)!);
                await File.WriteAllTextAsync(Path.Combine(w.Root, "empty.gitconfig"), "");
                await w.GitAsync(w.Root, "init", "--bare", "--initial-branch=main", w.Origin);
                await w.GitAsync(w.Repo, "init", "--initial-branch=main");
                await w.GitAsync(w.Repo, "config", "user.name", "c1065");
                await w.GitAsync(w.Repo, "config", "user.email", "c1065@localhost");
                await File.WriteAllTextAsync(Path.Combine(w.Repo, "source.txt"), "base");
                await File.WriteAllTextAsync(Path.Combine(w.Repo, ".gitignore"), "*.generated\n");
                await w.GitAsync(w.Repo, "add", ".");
                await w.GitAsync(w.Repo, "commit", "-m", "base");
                w.BaseSha = (await w.GitAsync(w.Repo, "rev-parse", "HEAD")).Trim();
                await w.GitAsync(w.Repo, "remote", "add", "origin", w.Origin);
                await w.GitAsync(w.Repo, "push", "origin", "main", "main:" + FullRef);
                await w.GitAsync(w.Repo, "worktree", "add", "-b", Branch, w.Mirror, w.BaseSha);
                w.Request = new(w.Mirror, new(Guid.NewGuid(), Guid.NewGuid(),
                    Guid.Parse("deadbeef-0000-4000-8000-000000000001"), 1, Guid.NewGuid(), Guid.NewGuid(),
                    "isolated-runner", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
                    "report-digest", RunnerWorkspaceParkService.RepositoryIdentity(Path.Combine(w.Repo, ".git")),
                    RunnerWorkspaceParkService.Fingerprint(w.Origin), FullRef, w.BaseSha));
                return w;
            }
            catch { w.Dispose(); throw; }
        }

        public RunnerWorkspaceParkService Publisher(Func<WorkspaceParkBoundary, CancellationToken, Task>? barrier = null) =>
            new(new RunnerWorkspaceService(Repo, Work, Origin, psi =>
            {
                Isolate(psi);
                Commands.Add(psi.ArgumentList.ToArray());
                BeforeStart?.Invoke(psi);
                return Process.Start(psi);
            }, TimeSpan.FromSeconds(15))) { BoundaryAsync = barrier };

        public async Task<string> CommitAsync(string file, string content)
        {
            await File.WriteAllTextAsync(Path.Combine(Mirror, file), content);
            await GitAsync(Mirror, "add", file);
            await GitAsync(Mirror, "commit", "-m", "truthful WIP");
            return await HeadAsync();
        }

        public Task<string> PushAsync() => GitAsync(Mirror, "push", "origin", FullRef + ":" + FullRef);
        public async Task<string> HeadAsync() => (await GitAsync(Mirror, "rev-parse", "HEAD")).Trim();
        public async Task<string> RemoteShaAsync() => (await GitAsync(Origin, "rev-parse", FullRef)).Trim();
        public async Task<string> SourceSnapshotAsync() =>
            await GitAsync(Mirror, "diff", "HEAD", "--binary", "--ignore-submodules=none")
            + await GitAsync(Mirror, "status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=none")
            + string.Join("|", Directory.EnumerateFiles(Mirror).Where(p => Path.GetFileName(p) != ".git")
                .OrderBy(p => p).Select(p => Path.GetFileName(p) + ":" + File.ReadAllText(p)));

        public Task<string> GitAsync(string cwd, params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = cwd, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            Isolate(psi);
            return RunAsync(psi);
        }

        private void Isolate(ProcessStartInfo psi)
        {
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            psi.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Root, "empty.gitconfig");
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GIT_CONFIG_COUNT"] = "0";
        }

        public static async Task<string> RunAsync(ProcessStartInfo psi)
        {
            using var process = Process.Start(psi) ?? throw new IOException("owned child did not start");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stdout, stderr);
                throw;
            }
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new IOException("scratch Git failed: " + error);
            return output;
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
    private sealed class SeatWorld : IAsyncDisposable
    {
        internal const string TaskPrompt = "[antiphon-task:c667-s1b] Current generation delivery, complete distinctive task prompt.";
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c667-seat-" + Guid.NewGuid().ToString("N"));
        private readonly SessionRunnerSettings _settings;
        private readonly string _manifest;
        private readonly string _sidecar;
        private readonly DateTime _generation = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        public World Source { get; private set; } = null!;
        public TailWorld Tail { get; }
        public SeatChild Child { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
        public FakeTimeProvider ServerClock { get; } = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        public SessionRunnerRuntime Runtime { get; private set; }
        public SessionRunnerRuntime.RunnerSession Session { get; private set; } = null!;
        public TerminalSeatObservationRequest Request { get; private set; } = null!;

        public SeatWorld(string provider)
        {
            Tail = new TailWorld(provider,
                (path, how) => Session?.RecordTranscriptBinding(path, how),
                () => Session?.RecordTranscriptUnbinding());
            _settings = new SessionRunnerSettings { SessionLogPath = _root };
            _manifest = PtyHostManifest.PathFor(_settings.PtyHostManifestDir, Tail.SessionId);
            _sidecar = TranscriptSidecar.PathFor(_root, Tail.SessionId);
            new PtyHostManifest
            {
                SessionId = Tail.SessionId, PipeName = "fixture-only-no-process", HostPid = 1,
                HostStartTimeUtc = _generation, CreatedAtUtc = _generation, AcceptedStartedAt = _generation
            }.SaveAtomic(_manifest);
            new HerdrPaneSidecar
            {
                SessionId = Tail.SessionId, WorkspaceKey = "fixture", WorkspaceId = "fixture",
                TabId = "fixture", PaneId = "fixture", AcceptedStartedAt = _generation
            }.SaveAtomic(HerdrPaneSidecar.PathFor(_root, Tail.SessionId));
            new TranscriptSidecar
            {
                SessionId = Tail.SessionId, ChildStartUtc = _generation, TranscriptPath = Tail.Path,
                Cwd = System.IO.Path.GetDirectoryName(Tail.Path), UpdatedAtUtc = _generation
            }.SaveAtomic(_sidecar);
            Runtime = CreateRuntime();
        }

        private SessionRunnerRuntime CreateRuntime() => new(Options.Create(_settings),
            NullLogger<SessionRunnerRuntime>.Instance, timeProvider: Clock, workspaceParkService: Source?.Publisher());

        private void Bind()
        {
            Session = new SessionRunnerRuntime.RunnerSession(Tail.SessionId, _settings,
                new SessionRunnerEventHub(), NullLogger.Instance);
            Session.BindChildForTest(Child, Tail.Tailer, _generation);
            Runtime.Track(Session);
        }

        public async Task<TerminalSeatReleaseRequest> QualifyAsync()
        {
            await StartAsync();
            await DeliverAsync();
            (await ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Waiting);
            Clock.Advance(TimeSpan.FromSeconds(120));
            var observed = await ObserveAsync();
            observed.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
            return await BindPublicationAsync(new(Guid.NewGuid(), Request, observed.Token!));
        }

        public async Task<TerminalSeatReleaseRequest> BindPublicationAsync(TerminalSeatReleaseRequest release)
        {
            var source = Source.Request with { Binding = Source.Request.Binding with
            {
                ActionId = release.ActionId, SessionId = Tail.SessionId,
                RunnerStoreId = Runtime.RunnerStoreId, AcceptedStartedAt = release.Observation.ExpectedAcceptedStartedAt
            }};
            var result = await Runtime.ParkWorkspaceAsync(new(Tail.SessionId, Prepare: source), CancellationToken.None);
            result.Outcome.ShouldBe(WorkspaceParkOutcome.Published);
            return release with { Publication = result.Receipt.ShouldNotBeNull() };
        }

        public Task<TerminalSeatReleaseResult> ReleaseAsync(TerminalSeatReleaseRequest request) =>
            Runtime.ReleaseTerminalSeatAsync(Tail.SessionId, request, TimeSpan.Zero, CancellationToken.None);

        public string ReleaseAuditPath => System.IO.Path.Combine(_root, "slot-releases.jsonl");

        public int ReleaseAuditCount => File.Exists(System.IO.Path.Combine(_root, "slot-releases.jsonl"))
            ? File.ReadAllLines(System.IO.Path.Combine(_root, "slot-releases.jsonl")).Length : 0;

        public void TrackReplacement(DateTime generation)
        {
            Session = new SessionRunnerRuntime.RunnerSession(Tail.SessionId, _settings,
                new SessionRunnerEventHub(), NullLogger.Instance);
            Session.BindChildForTest(new SeatChild(), Tail.Tailer, generation);
            Runtime.Track(Session);
        }

        public Guid TrackForeign()
        {
            var id = Guid.NewGuid();
            var foreign = new SessionRunnerRuntime.RunnerSession(id, _settings,
                new SessionRunnerEventHub(), NullLogger.Instance);
            foreign.BindChildForTest(new SeatChild());
            Runtime.Track(foreign);
            new PtyHostManifest
            {
                SessionId = id, PipeName = "fixture-foreign", HostPid = 1,
                HostStartTimeUtc = _generation, CreatedAtUtc = _generation, AcceptedStartedAt = _generation
            }.SaveAtomic(PtyHostManifest.PathFor(_settings.PtyHostManifestDir, id));
            return id;
        }

        public void AssertForeignRetained(Guid id)
        {
            Runtime.List().ShouldContain(s => s.SessionId == id);
            File.Exists(PtyHostManifest.PathFor(_settings.PtyHostManifestDir, id)).ShouldBeTrue();
        }

        private string GenerationDirectory => System.IO.Path.Combine(_root, "launch-generations");
        public void RecordGeneration() => new PhoneHomeLaunchGenerationStore(GenerationDirectory)
            .Record(Tail.SessionId, _generation);
        public DateTime? ReadGenerationFromDisk() => new PhoneHomeLaunchGenerationStore(GenerationDirectory).Read(Tail.SessionId);

        public void AssertUnadoptedManifest() => File.Exists(_manifest).ShouldBeTrue("G-67");
        public void AssertReleased(int expectedKills = 1)
        {
            Runtime.List().ShouldNotContain(s => s.SessionId == Tail.SessionId);
            File.Exists(_manifest).ShouldBeFalse();
            File.Exists(HerdrPaneSidecar.PathFor(_root, Tail.SessionId)).ShouldBeFalse();
            File.Exists(_sidecar).ShouldBeTrue("transcript history is not session custody");
            Child.Kills.ShouldBe(expectedKills);
            ReleaseAuditCount.ShouldBe(1);
        }

        public async Task RestartEmptyAsync()
        {
            await Runtime.DisposeAsync();
            Runtime = CreateRuntime();
        }

        public async Task StartAsync()
        {
            Source = await World.CreateAsync();
            await Runtime.DisposeAsync();
            Runtime = CreateRuntime();
            await Tail.StartAsync();
            Bind();
            var baseline = await Tail.ObserveAsync();
            Request = new(Runtime.RunnerStoreId, _generation, baseline.BindingIdentity!, baseline.TranscriptRevision);
        }

        public void BindUnbound() => Bind();

        public void SetRequest(TerminalSeatObservationRequest request) => Request = request;

        public void UseCapturedRequest() => Request = new(Runtime.RunnerStoreId,
            Session.AcceptedStartedAt!.Value, "", -1, true);

        public void EnableNativeSubmission(bool swallowFirstEnter = false)
        {
            var composer = new StringBuilder();
            var submits = 0;
            Child.Write = async _ =>
            {
                var bytes = Child.Inputs[^1];
                if (bytes != "\r") { composer.Append(bytes); return; }
                if (++submits == 1 && swallowFirstEnter) return;
                if (composer.Length == 0) return;
                var body = composer.ToString().Replace("\u001b[200~", "").Replace("\u001b[201~", "");
                composer.Clear();
                var id = Guid.NewGuid().ToString("N");
                await Tail.AppendAsync(Tail.Prompt(body, id) + Tail.End(id));
            };
        }

        public async Task RestartEmptyAsyncForReplacement()
        {
            Runtime.DetachTerminalTailerForTest(Tail.SessionId);
            await RestartEmptyAsync();
        }

        public async Task DeliverAsync()
        {
            await Runtime.SendInputAsync(Tail.SessionId, TaskPrompt, CancellationToken.None);
            await Runtime.SendInputAsync(Tail.SessionId, "\r", CancellationToken.None);
            await Tail.AppendAsync(Tail.Prompt(TaskPrompt, "current") + Tail.End("current"));
        }

        public Task<TerminalSeatObservation> ObserveAsync() =>
            Runtime.ObserveTerminalSeatAsync(Tail.SessionId, Request, CancellationToken.None);

        public Task<TerminalSeatObservation> AuthorizeAsync(string token) =>
            Runtime.AuthorizeTerminalSeatTokenAsync(Tail.SessionId, Request, token, CancellationToken.None);

        public async Task RestartAsync()
        {
            // Detach the real tailer before runtime disposal; the same native transcript stays
            // owned by TailWorld, like the transcript/child surviving a runner process restart.
            Runtime.DetachTerminalTailerForTest(Tail.SessionId);
            await Runtime.DisposeAsync();
            Runtime = CreateRuntime();
            Runtime.RunnerStoreId.ShouldBe(Request.ExpectedRunnerStoreId);
            Bind();
        }

        public void AssertRetained(int expectedKills = 0)
        {
            Child.Kills.ShouldBe(expectedKills);
            Runtime.LiveSessionCount.ShouldBe(1);
            Runtime.List().ShouldContain(s => s.SessionId == Tail.SessionId);
            File.Exists(_manifest).ShouldBeTrue("read-only qualification retains manifest custody");
            File.Exists(HerdrPaneSidecar.PathFor(_root, Tail.SessionId)).ShouldBeTrue();
            File.Exists(_sidecar).ShouldBeTrue("read-only qualification retains transcript binding");
        }

        public async ValueTask DisposeAsync()
        {
            if (Runtime.List().Any(s => s.SessionId == Tail.SessionId))
                Runtime.DetachTerminalTailerForTest(Tail.SessionId);
            await Runtime.DisposeAsync();
            await Tail.DisposeAsync();
            Source?.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class SeatChild : ISessionChild
    {
        public List<string> Inputs { get; } = [];
        public int Kills { get; private set; }
        public Func<CancellationToken, Task<bool>>? Kill { get; set; }
        public Func<CancellationToken, Task>? Write { get; set; }
        public Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task WriteAsync(string input, CancellationToken ct) { Inputs.Add(input); return Write?.Invoke(ct) ?? Task.CompletedTask; }
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> KillAsync(CancellationToken ct) { Kills++; return Kill?.Invoke(ct) ?? Task.FromResult(false); }
        public Task<ChildScreen?> ReadScreenAsync(CancellationToken ct) => Task.FromResult<ChildScreen?>(null);
        public event Action<ChildExit>? Exited;
        public void Exit() => Exited?.Invoke(new(0, "KilledByRequest"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TailWorld : IAsyncDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c667-" + Guid.NewGuid().ToString("N"));
        private readonly string? _oldClaudeConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        private readonly string _provider;
        private readonly CancellationTokenSource _hubLifetime = new();
        private readonly SemaphoreSlim _pollPermit = new(0);
        private readonly Channel<bool> _pollArrived = Channel.CreateUnbounded<bool>();
        private readonly ChannelReader<RunnerServerSentEvent> _events;
        public Guid SessionId { get; } = Guid.NewGuid();
        public TranscriptClaimRegistry Claims { get; } = new();
        public string Path { get; }
        public ITranscriptTailer Tailer { get; }
        public TerminalSeatReleaseObservation Observer { get; }

        public TailWorld(string provider, Action<string, string>? onBound = null, Action? onUnbound = null)
        {
            _provider = provider;
            Directory.CreateDirectory(_root);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _root);
            Path = System.IO.Path.Combine(_root, "transcript.jsonl");
            File.WriteAllText(Path, Prompt("hello", "first") + End("first"));
            var hub = new SessionRunnerEventHub();
            _events = hub.Subscribe(_hubLifetime.Token);
            // Real sidecar adoption/claim logic, restricted to this fixture's private root.
            Tailer = provider switch
            {
                "Claude" => new TranscriptTailer(SessionId, _root, hub, NullLogger.Instance,
                    knownTranscriptPath: Path, claims: Claims, forkScanInterval: TimeSpan.FromDays(1),
                    onBound: onBound, onUnbound: onUnbound),
                "Grok" => new GrokTranscriptTailer(SessionId, Path, hub, NullLogger.Instance,
                    pollInterval: TimeSpan.FromMilliseconds(1)),
                _ => new CodexTranscriptTailer(SessionId, _root, hub, NullLogger.Instance,
                    knownTranscriptPath: Path, sessionsRoot: _root, claims: Claims,
                    pollInterval: TimeSpan.FromMilliseconds(1), onBound: onBound, onUnbound: onUnbound)
            };
            Observer = Tailer switch
            {
                TranscriptTailer t => t.TerminalObservation,
                GrokTranscriptTailer t => t.TerminalObservation,
                CodexTranscriptTailer t => t.TerminalObservation,
                _ => throw new InvalidOperationException()
            };
            Observer.BeforePoll = async ct =>
            {
                await _pollArrived.Writer.WriteAsync(true, ct);
                await _pollPermit.WaitAsync(ct);
            };
        }

        public async Task StartAsync()
        {
            Tailer.Start();
            await AwaitPollAsync();
            await PollAsync();
        }

        public async Task PollAsync()
        {
            _pollPermit.Release();
            await AwaitPollAsync();
        }

        public void PermitFinalPoll() => _pollPermit.Release();

        private async Task AwaitPollAsync() =>
            await _pollArrived.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        public Task AppendAsync(string text) => File.AppendAllTextAsync(Path, text, new UTF8Encoding(false));
        public Task<TerminalTranscriptObservation> ObserveAsync() => Tailer.ObserveTerminalSeatAsync(CancellationToken.None);

        public RunnerTranscriptEvent[] DrainTranscript()
        {
            var rows = new List<RunnerTranscriptEvent>();
            while (_events.TryRead(out var evt))
                if (evt.EventName == SessionRunnerEventNames.SessionTranscript)
                    rows.Add(JsonSerializer.Deserialize<RunnerTranscriptEvent>(evt.Json)!);
            return rows.ToArray();
        }

        public string Prompt(string text, string id) => _provider switch
        {
            "Claude" => JsonSerializer.Serialize(new { type = "user", uuid = id, message = new { role = "user", content = text } }) + "\n",
            "Grok" => Grok(new { sessionUpdate = "user_message_chunk", content = new { type = "text", text } }, id),
            _ => Codex(new { type = "user_message", message = text }, id)
        };

        public string End(string id) => _provider switch
        {
            "Claude" => JsonSerializer.Serialize(new { type = "assistant", uuid = id + "-end", message = new { role = "assistant", content = Array.Empty<object>(), stop_reason = "end_turn" } }) + "\r\n",
            "Grok" => Grok(new { sessionUpdate = "turn_completed", prompt_id = id, stop_reason = "end_turn" }, id),
            _ => Codex(new { type = "task_complete", turn_id = id }, id)
        };

        public string Activity(string id) => _provider switch
        {
            "Claude" => JsonSerializer.Serialize(new { type = "assistant", uuid = id + "-tool", message = new { role = "assistant", content = new[] { new { type = "tool_use", id, name = "read", input = new { path = "C:\\fixture\\file" } } }, stop_reason = "tool_use" } }) + "\n",
            "Grok" => Grok(new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "still working" } }, id),
            _ => Codex(new { type = "agent_reasoning", text = "still working" }, id)
        };

        private static string Grok(object update, string id) => JsonSerializer.Serialize(new
        {
            method = "session/update", @params = new { update, _meta = new { eventId = Guid.NewGuid().ToString(), promptId = id } }
        }) + "\n";
        private static string Codex(object payload, string id) => JsonSerializer.Serialize(new
        {
            type = "event_msg", timestamp = "2026-10-01T00:00:00Z", id, payload
        }) + "\n";

        public async ValueTask DisposeAsync()
        {
            await Tailer.DisposeAsync();
            await _hubLifetime.CancelAsync();
            _hubLifetime.Dispose();
            _pollPermit.Dispose();
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _oldClaudeConfig);
            Directory.Delete(_root, recursive: true);
        }
    }
}

