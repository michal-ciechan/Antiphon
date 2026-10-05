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

[Category("Integration")]
[NotInParallel("ClaudeConfigDirEnv")]
public class TerminalSeatReleaseTests
{
    [Test]
    public async Task Activity_resets_the_qualification_window()
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

    [Test]
    public async Task Input_winning_the_gate_invalidates_release()
    {
        await AssertInputOrderingAsync(conditional: false);
    }

    [Test]
    public async Task Conditional_input_invalidates_release()
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
            world.Child.Inputs.Count.ShouldBe(before, "the launch owner must exclude the input writer");
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

    [Test]
    public async Task Release_winning_the_gate_refuses_later_input()
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
                    "release owns the live generation before exit can mask the decision");
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
        world.Child.Inputs.Count.ShouldBe(before, "both public routes must refuse with zero child writes");
        world.AssertRetained(expectedKills: 1);
    }

    [Test]
    public async Task Replacement_generation_is_never_released()
    {
        foreach (var replaceStore in new[] { true, false })
        {
            await using var world = new SeatWorld("Codex");
            var request = await world.QualifyAsync();
            if (replaceStore)
                request = request with { Observation = request.Observation with { ExpectedRunnerStoreId = Guid.NewGuid() } };
            else
                world.Session.BindAcceptedGeneration(request.Observation.ExpectedAcceptedStartedAt.AddSeconds(1));
            (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.GenerationMismatch,
                replaceStore ? "store-only mismatch" : "generation-only replacement");
            world.AssertRetained();
        }
    }

    [Test]
    public async Task Token_for_another_session_is_refused()
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
            .ShouldBe(TerminalSeatReleaseOutcome.StaleObservation);
        (await second.ReleaseAsync(own with { Token = foreign.Token })).Outcome
            .ShouldBe(TerminalSeatReleaseOutcome.StaleObservation);
        first.AssertRetained();
        second.AssertRetained();
    }

    [Test]
    public async Task Working_remains_protected_after_arbitrary_silence()
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
                proof.OutputRevision, world.Clock).ShouldBe(TerminalSeatReleaseOutcome.Working);
            (await world.ReleaseAsync(request)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Working);
            world.AssertRetained();
        }
    }

    [Test]
    public async Task Unknown_backend_custody_refuses_release()
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
                        ? TerminalSeatReleaseOutcome.PendingDelivery : TerminalSeatReleaseOutcome.Unknown, condition);
            }
            finally
            {
                finish.TrySetResult();
                if (pending is not null) await pending;
            }
            world.AssertRetained();
        }
    }

    [Test]
    public async Task Tail_growth_at_final_check_refuses_signal()
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

    [Test]
    public async Task Output_growth_at_signal_boundary_refuses_release()
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

    [Test]
    public async Task Kill_failure_retains_manifest_and_capacity()
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
            result.Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unresolved, failure);
            result.ConfirmsExit.ShouldBeFalse();
            world.AssertRetained(expectedKills: 1);
            world.ReleaseAuditCount.ShouldBe(0);
            (await world.ReleaseAsync(request)).ShouldBe(result, "an unresolved action is not a second kill attempt");
            world.Child.Kills.ShouldBe(1);
        }
    }

    [Test]
    public async Task Duplicate_action_is_idempotent()
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
            "a cached success cannot conceal the replacement generation");
        // A new expected generation cannot reuse the old action identity either.
        (await world.ReleaseAsync(request with { Observation = request.Observation with
        { ExpectedAcceptedStartedAt = request.Observation.ExpectedAcceptedStartedAt.AddSeconds(1) } })).Outcome
            .ShouldBe(TerminalSeatReleaseOutcome.GenerationMismatch);
        world.Child.Kills.ShouldBe(1);
        world.Runtime.LiveSessionCount.ShouldBe(1);
        world.ReleaseAuditCount.ShouldBe(1);
    }

    [Test]
    public async Task Confirmed_exit_forgets_only_the_expected_generation()
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
        world.ReadGenerationFromDisk().ShouldBe(request.Observation.ExpectedAcceptedStartedAt);
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

    [Test]
    public async Task Two_observations_require_the_full_safety_margin()
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
        early.Status.ShouldBe(TerminalSeatQualificationStatus.Waiting, "119.999 seconds is not qualified");
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

    [Test]
    public async Task Old_turn_end_does_not_qualify_a_new_generation()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using var world = new SeatWorld(provider);
            await world.StartAsync();
            // Old history is idle, but none of it follows the current delivery floor.
            var old = await world.ObserveAsync();
            old.Status.ShouldBe(TerminalSeatQualificationStatus.OldPrompt, "old-end-rejected: " + provider);
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

    [Test]
    public async Task Unavailable_observation_discards_qualification()
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
            "recovery at old t+120 must start a new window");
        recovered.StableFor.ShouldBe(TimeSpan.Zero);
        world.Clock.Advance(TimeSpan.FromSeconds(120));
        (await world.ObserveAsync()).Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        world.AssertRetained();
    }

    [Test]
    public async Task Restart_invalidates_volatile_observation_tokens()
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
                "old-epoch proof must fail before a token-cache miss or different object can mask the guard");
        world.Clock.Advance(TimeSpan.FromSeconds(120));
        var qualified = await world.ObserveAsync();
        qualified.Status.ShouldBe(TerminalSeatQualificationStatus.Qualified);
        qualified.Token.ShouldNotBe(before.Token, "a restarted runtime must not recreate an old token");
        world.AssertRetained();
    }

    [Test]
    [Arguments("Claude")]
    [Arguments("Grok")]
    [Arguments("Codex")]
    public async Task Fresh_tail_reads_each_provider(string provider)
    {
        await using var world = new TailWorld(provider);
        await world.StartAsync();
        TranscriptWorkingState.Classify(world.Tailer.Snapshot().Entries)
            .ShouldBe(TranscriptWorkingState.WorkingVerdict.Idle);
        var idle = await world.ObserveAsync();
        idle.Verdict.ShouldBe(TerminalTranscriptVerdict.Idle);

        await world.AppendAsync(world.Prompt("unread prompt", "second"));
        var fresh = await world.ObserveAsync();
        fresh.Verdict.ShouldBe(TerminalTranscriptVerdict.Working,
            $"{provider} unread native prompt must outrank the cached idle turn");
        fresh.Status.ShouldBe(TerminalTranscriptReadStatus.Success);
        fresh.TranscriptRevision.ShouldBeGreaterThan(idle.TranscriptRevision);
        fresh.FileRevision.ShouldNotBe(idle.FileRevision);
        fresh.BindingIdentity.ShouldBe(idle.BindingIdentity);
        fresh.LastPromptRevision.ShouldNotBeNull();
        fresh.LastEndRevision.ShouldNotBeNull();
        fresh.LastPromptRevision.Value.ShouldBeGreaterThan(fresh.LastEndRevision.Value);
        fresh.ConsumedBytes.ShouldBe(new FileInfo(world.Path).Length);
        TranscriptWorkingState.Classify(world.Tailer.Snapshot().Entries)
            .ShouldBe(TranscriptWorkingState.WorkingVerdict.Idle, "fresh reads must not advance ingestion");

        // End that turn, then observe activity without a new prompt. Grok buffers chunks;
        // inspection must flush its private normalizer, never the poller's normalizer.
        await world.AppendAsync(world.End("second"));
        (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Idle);
        await world.AppendAsync(world.Activity("third"));
        (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Working,
            $"{provider} unread output/tool activity must veto idle");
    }

    [Test]
    public async Task Unknown_or_partial_tail_never_authorizes_release()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using (var unbound = new TailWorld(provider))
                (await unbound.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Unknown,
                    $"{provider}: an unbound file is not evidence");

            foreach (var defect in new[] { "empty", "missing", "unreadable", "partial", "malformed", "utf8", "budget" })
            {
                await using var world = new TailWorld(provider);
                await world.StartAsync();
                (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Idle);
                switch (defect)
                {
                    case "empty": File.WriteAllText(world.Path, ""); break;
                    case "missing": File.Delete(world.Path); break;
                    case "unreadable": world.Observer.OpenRead = _ => throw new UnauthorizedAccessException(); break;
                    case "partial": await world.AppendAsync(world.Prompt("partial", "next").TrimEnd('\n')); break;
                    case "malformed": await world.AppendAsync("{invalid json}\n"); break;
                    case "utf8":
                        await using (var stream = new FileStream(world.Path, FileMode.Append))
                            await stream.WriteAsync(new byte[] { 0xff, (byte)'\n' });
                        break;
                    case "budget":
                        using (var stream = new FileStream(world.Path, FileMode.Open))
                            stream.SetLength(TerminalSeatReleaseObservation.MaximumBytes + 1);
                        break;
                }
                (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Unknown,
                    $"{provider}: {defect} must refuse even with an older idle turn");
            }
        }
    }

    [Test]
    public async Task Binding_changes_during_read_refuse_qualification()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        foreach (var change in new[] { "replace", "truncate", "grow", "revoke" })
        {
            if (provider == "Grok" && change == "revoke") continue; // deterministic path, no claim registry
            await using var world = new TailWorld(provider);
            await world.StartAsync();
            (await world.ObserveAsync()).Verdict.ShouldBe(TerminalTranscriptVerdict.Idle);
            world.Observer.AfterRead = async _ =>
            {
                switch (change)
                {
                    case "replace":
                        var replacement = world.Path + ".replacement";
                        // Same byte length and mtime, different contents and creation identity.
                        File.WriteAllText(replacement, File.ReadAllText(world.Path).Replace("hello", "other"));
                        File.SetLastWriteTimeUtc(replacement, File.GetLastWriteTimeUtc(world.Path));
                        File.Move(replacement, world.Path, overwrite: true);
                        break;
                    case "truncate": File.WriteAllText(world.Path, world.End("first")); break;
                    case "grow": await world.AppendAsync(world.Prompt("race", "second")); break;
                    case "revoke":
                        world.Claims.Release(world.Path, world.SessionId);
                        world.Claims.TryClaim(world.Path, Guid.NewGuid()).Claimed.ShouldBeTrue();
                        world.Tailer.NotifyClaimRevoked(world.Path, Guid.NewGuid());
                        break;
                }
            };
            var observed = await world.ObserveAsync();
            observed.Verdict.ShouldBe(TerminalTranscriptVerdict.Unknown,
                $"{provider}: {change} invalidates the consumed binding");
            observed.Status.ShouldBe(TerminalTranscriptReadStatus.StaleObservation);
        }
    }

    [Test]
    public async Task Fresh_observation_does_not_publish_duplicate_entries()
    {
        foreach (var provider in new[] { "Claude", "Grok", "Codex" })
        {
            await using var world = new TailWorld(provider);
            await world.StartAsync();
            var initial = world.DrainTranscript();
            initial.Length.ShouldBeGreaterThan(0);
            var before = world.Tailer.Snapshot().LastSequence;
            await world.AppendAsync(world.Prompt("second prompt", "second") + world.Activity("second") + world.End("second"));
            await world.ObserveAsync();
            await world.ObserveAsync();
            world.DrainTranscript().ShouldBeEmpty("observation must publish zero events/callbacks");
            world.Tailer.Snapshot().LastSequence.ShouldBe(before);
            await world.PollAsync();
            var published = world.DrainTranscript();
            published.Select(e => e.Kind).ShouldBe(new[]
            {
                TranscriptKinds.UserPrompt,
                provider == "Claude" ? TranscriptKinds.ToolCall
                    : provider == "Grok" ? TranscriptKinds.AssistantText : TranscriptKinds.Thinking,
                TranscriptKinds.TurnEnd
            }, "private inspection must preserve every later ingestion part exactly once");
            published.Count(e => e.Kind == TranscriptKinds.UserPrompt && e.Text == "second prompt").ShouldBe(1);
            published.Count(e => e.Kind == TranscriptKinds.TurnEnd).ShouldBe(1);
            published.Select(e => e.Sequence).Distinct().Count().ShouldBe(published.Length);
            published.Select(e => e.Sequence).ShouldBe(Enumerable.Range((int)before + 1, published.Length).Select(i => (long)i));
            await world.PollAsync();
            world.DrainTranscript().ShouldBeEmpty("the next poll must not re-emit the same bytes");
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
        public TailWorld Tail { get; }
        public SeatChild Child { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
        public FakeTimeProvider ServerClock { get; } = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        public SessionRunnerRuntime Runtime { get; private set; }
        public SessionRunnerRuntime.RunnerSession Session { get; private set; } = null!;
        public TerminalSeatObservationRequest Request { get; private set; } = null!;

        public SeatWorld(string provider)
        {
            Tail = new TailWorld(provider);
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
            NullLogger<SessionRunnerRuntime>.Instance, timeProvider: Clock);

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
            return new(Guid.NewGuid(), Request, observed.Token!);
        }

        public Task<TerminalSeatReleaseResult> ReleaseAsync(TerminalSeatReleaseRequest request) =>
            Runtime.ReleaseTerminalSeatAsync(Tail.SessionId, request, TimeSpan.Zero, CancellationToken.None);

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
            await Tail.StartAsync();
            Bind();
            var baseline = await Tail.ObserveAsync();
            Request = new(Runtime.RunnerStoreId, _generation, baseline.BindingIdentity!, baseline.TranscriptRevision);
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

        public TailWorld(string provider)
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
                    knownTranscriptPath: Path, claims: Claims, forkScanInterval: TimeSpan.FromDays(1)),
                "Grok" => new GrokTranscriptTailer(SessionId, Path, hub, NullLogger.Instance,
                    pollInterval: TimeSpan.FromMilliseconds(1)),
                _ => new CodexTranscriptTailer(SessionId, _root, hub, NullLogger.Instance,
                    knownTranscriptPath: Path, sessionsRoot: _root, claims: Claims,
                    pollInterval: TimeSpan.FromMilliseconds(1))
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
