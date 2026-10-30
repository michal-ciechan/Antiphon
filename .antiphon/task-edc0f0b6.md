# Review edc0f0b6 — C514 arm race fix (feat/card-task-5e08c4d4 @ 7491a26fe)

Reviewed source: `7491a26fe5cdad7f6aa861dd8df4b8521040a26c` (base `80b3a59c4`, red `b65503281`, fix `43a5291f9`, doc `7491a26fe`).
Host: server2 (Linux, Debian 12, .NET 9.0.22), load average ~50 during the runs.

Verdict: SAFE BUT INCOMPLETE. The refresh is fenced correctly: no double-send, drop or mis-delivery is possible.
No Enter can be repeated after a write that may have been delivered, and every bound ends in `ArmUnconfirmed`.
The fix does not close one demonstrated sibling redraw race (D-1). That race shows the same C514 symptom:
`/remote-control` stays typed, no Enter bytes are written, and the result is a fast `ArmUnconfirmed`.

## (1) Diagnosis and branch trace

Before the fix, the path was: body write, then `WaitComposerAsync` (`Contains`), then `ObserveAsync` (runner snapshot, seq S1), then a guarded `\r` at S1.
`AgentSessionRuntime.SendConditionalInputAsync` then takes its own `ObserveDialogAsync` (seq S2). When S2 != S1 it returns
`stale-observation` before any write (`AgentSessionRuntime.cs:2274-2275`). The runner fences the same way under its
screen lock before `WriteBackendAsync` (`SessionRunnerRuntime.cs:1645-1690`, Fence and `CheckDialogInput`), and `_inputRevision++` runs only
inside `WriteBackendAsync`, after the Fence. "Stale" means output moved: a redraw advances `LastSequence` and
the guard compares that output sequence. A stale Enter is therefore a definitive zero-byte refusal at every layer: the runtime pre-check,
the runner Fence and the test adapter. The HTTP client maps transport and parse failures to `unknown`, never to stale
(`SessionRunnerHttpClient.cs:760-800`). The diagnosis is correct for that window.

Branch trace of `WaitComposerAndSubmitArmAsync` (`RemoteControlRecoveryService.cs:616-655`):
- Written: returned at once and goes to the bridge probe only. It is never refreshed.
- Unknown (HTTP failure or exception in `SendGuardedAsync`): terminal `ArmUnconfirmed` with `unconfirmed: true`, so the generation veto holds. It is not refreshed.
  Even with a hypothetical POST retry, the pinned `ExpectedInputRevision` (custody +1) refuses a second Enter after a delivered one.
- StaleObservation: the only refreshed outcome. Before each new attempt the loop re-reads the dialog observation and requires the
  same `RuntimeEpoch` and `InputRevision`, `InputHistoryKnown` (so the session is not adopted and input is not uncertain), the same generation,
  no dialog in the Blocked state, a composer that reads exactly `/remote-control`, a proven snapshot generation, and an idle transcript.
- Dialog open: the loop returns `ModalBlocked`. The runtime's `Request` sets `RequireClaudeDialogClear=true`, and the runner
  `CheckDialogInput` refuses under the screen lock. A refreshed Enter therefore cannot reach a permission or question dialog.
- Session restarted: an epoch or generation change makes the loop return `ObservationUnknown` or `GenerationMismatch`.
- A null dialog read (2 s bound) inside the runtime now returns `stale`. It is still zero-byte, and the next loop iteration
  returns `ObservationUnknown` because the dialog is null.
- The runtime change (`AgentSessionRuntime.cs:2276-2280`) refuses a caller pin that differs from the fresh observation instead of
  re-pinning to it. This is strictly tighter.

## (2) Bound

The loop is bounded by time only: `EvidenceTimeoutSeconds` (default 15, lane 10, harness 1), checked at the top of the loop, with
50 ms delays. It has no attempt cap. Each refused attempt writes zero bytes, so an attempt cap adds nothing to safety.
At the deadline the loop returns `last`, which is stale or observation-unknown, and maps to `ArmUnconfirmed`. It does not hang, and it never reports
success silently. The arm's worst-case length is unchanged: composer budget plus bridge budget.

## (3) Witnesses and mutations

Both red witnesses failed at `b65503281` on assertions (Debug CP-RED rows). They drive the real
`RemoteControlRecoveryService` and `AgentSessionRuntime` against the fake adapter and the scripted runner snapshot seam. That is the real service seam,
but not the real runner.

The fix commit edited the redraw red witness by hoisting `h.Generation` into a local. The value is the same, so this is not
a relaxation. No test was removed or skipped. `RemoteControlPtyLane` now also admits Linux UnixPty, which extends coverage.

I built each mutation as a server-only build and swapped the dll into a copied output inside a repo tree:

| Mutation | Result |
|---|---|
| (a) also refresh after `Unknown` | RED: `Enter_refresh...(unknown)` sent 17 Enters (`["/remote-control","\r" x17]`), class 27/29 |
| (b) refresh every Enter (condition removed) | RED: 6 C514 witnesses (`Composer_redraw`, `Enter_refresh(unknown)`, `Arm_without_UserPrompt_submits_one_Enter`, `Executor_rechecks_kind`, `Busy_at_delivery...`, `Running_launch_owner...`), class 22/29 |
| (c) bound removed (`while (... \|\| sessionId != Guid.Empty)`) | Run HUNG: `Enter_refresh*` ran past 240 s and was killed (exit 124, no test completed). The bound is load-bearing, but nothing attributes the red (A-2). |

## D-1 (liveness, fails closed): an unreadable frame at the fence sequence is terminal

- **Where:** `server/Application/Services/RemoteControlRecoveryService.cs:633-649`. The composer is read from
  `TryGetLiveSnapshotAsync` (snapshot A), but the Enter's fence sequence comes from a separate `ObserveAsync` snapshot (B).
- **Failure:** a redraw lands between A and B, and output pauses mid-frame at B's sequence: the composer region is not painted, which
  `ClaudeScreen.TryReadComposer` documents as "a mid-redraw frame". The runtime's dialog observation then matches B's sequence
  and classifies the screen `Unknown`. `ProviderDialogObservationService.Request` throws `observation-unknown`, a zero-byte refusal,
  and the loop treats it as terminal. The result is `/remote-control` left typed and a fast `ArmUnconfirmed`, which is the same symptom as qualification d47848cc.
  The 1 s `ObserveAsync` read-failure branch (`GenerationProven` false, also before any write) is terminal for the same reason.
- **Evidence:** scratch witness `RV514_Unreadable_frame_at_fence_sequence_is_refreshed_not_terminal` (below) at the tip:
  failed, `partialUses` 2 (expected >2). After the single unreadable frame no further observation was taken, so the arm ended
  at the first Enter attempt. With mutation (a) or (b) it was also red.
- **Why:** the fix covers only redraws where output advances between the two observations. The Windows extract has no
  `MaintenanceEvidence`, so it cannot tell `stale-observation` from `observation-unknown`. This branch is equally plausible.
- **Fix:** take the composer evidence and the fence sequence from ONE runner snapshot. Read `TryReadComposer` on the same
  `SessionRunnerSnapshotDto` whose `LastSequence` is sent, so a fenced Enter is only attempted at a sequence whose frame shows a readable composer
  and the runner's dialog is Clear there. Any later output is then `stale` and is refreshed. Optionally, also refresh
  `observation-unknown` when it comes from the pre-write `ProviderDialogInputRefusedException`, since the top of the loop re-validates
  `InputHistoryKnown` and custody. Never refresh `unknown`. Adopt the scratch witness, with the result asserted first, as the red test.

Scratch witness (not committed, because Review is read-only). Insert it into `RemoteControlMaintenanceQueueTests`:

```csharp
[Test]
public async Task RV514_Unreadable_frame_at_fence_sequence_is_refreshed_not_terminal()
{
    await using var h = await RemoteControlRecoveryHarness.CreateAsync();
    await h.ArmProbeAfterWriteAsync();
    var generation = h.Generation;
    var stage = 0;
    var partialUses = 0;
    h.Adapter.BeforeRenderedSnapshot = () =>
    {
        if (h.Adapter.RenderedScreenOverride is not null && ++partialUses > 2)
        {
            h.Adapter.RenderedScreenOverride = null;   // frame completes
            h.Adapter.SnapshotSequence++;
        }
    };
    h.Runner.SnapshotOverride = sessionId =>
    {
        if (h.Adapter.ConditionalInputs.SequenceEqual(["/remote-control"]) && stage == 0)
        {
            stage = 1;                                  // mid-redraw: output advanced, composer unpainted
            h.Adapter.SnapshotSequence++;
            h.Adapter.RenderedScreenOverride = "Crunched for 1s\n\n";
        }
        return Task.FromResult(new SessionRunnerSnapshotDto(sessionId,
            h.Adapter.SnapshotRawOutput(), h.Adapter.SnapshotRenderedScreen(),
            h.Adapter.SnapshotSequence, generation, generation));
    };
    var result = await h.ReserveAndExecuteAsync();
    stage.ShouldBe(1);
    partialUses.ShouldBeGreaterThan(2);
    h.Adapter.ConditionalInputs.ShouldBe(["/remote-control", "\r"]);
    result.ShouldBe(RemoteControlArmResult.ArmedObserved);
}
```

## Advisories

- A-1: `C514_Health_request_reaches_busy_and_idle_recipients` prints only the screen tail when `ArmedObserved` fails.
  Add the row's `MaintenanceEvidence` to the failure message, so the owed native run separates stale (fixed), observation-unknown
  (D-1 or read failure), no-composer and bridge-deadline failures.
- A-2: the bound witness has no `[Timeout]`, so removing the bound hangs the run instead of producing an attributed red. Add a per-test timeout
  to `C514_Enter_refresh_preserves_custody_and_ambiguity_barriers`.
- A-3: when the composer cannot be read (for example a slash-autocomplete menu under it), the new loop waits out the whole budget. The old code
  refused immediately through the dialog guard. The outcome is unchanged (`ArmUnconfirmed`), only slower.

## (4) Other explanations for the Windows failure, and how to separate them

| Explanation | Fixed? | Native discriminator (failing iteration's request row `MaintenanceEvidence`) |
|---|---|---|
| Output advanced between snapshot and runtime/runner fence (Debug) | yes | `stale-observation` before the fix |
| Mid-redraw frame at the fence sequence leaves the dialog `Unknown` (D-1) | no | `observation-unknown`, with input revision = body only |
| 1 s snapshot read timeout before Enter under load | no | `enter-generation-unproven` before the fix; `observation-unknown` after |
| Composer never matched within 10 s | n/a | `no-composer`; ruled out by the ~3 s duration |
| Enter written, child did not submit (ConPTY coalescing or fake) | n/a | `arm-unconfirmed` after the full bridge budget, revision 4; ruled out by the ~3 s duration and revision 3 |

The Debug's native ALONE20 and LOAD10 rows are still the right ones to run. On any red, keep the row's `MaintenanceEvidence` and the runner
input revision. A green 30/30 does not rule out D-1.

## (5) Rerun of the portable selection

Run with `dotnet run --project tests/Antiphon.Tests --no-build` (build `-p:UseAppHost=false`, OutputPath `bin-rv514/`, TEMP=/tmp),
both through `scripts/build-slot.ps1`. The build held its slot for 362 s. The test run waited 150 s for a slot and held it for 99 s.
Filter: `/*/Antiphon.Tests.Application/(RemoteControlMaintenanceQueueTests*)|(RemoteControlRecoveryPtyIntegrationTests*)|(RemoteControlRecoveryTests*)|(ClaudeDialogDeliveryTests*)/*`.
Result: total 55, passed 55, failed 0, skipped 0. The TRX by class: Maintenance 28, PtyIntegration 8 (real UnixPty + fakeclaude),
Recovery 17, DialogDelivery 2. This matches the Debug.

Not run: the whole Unit lane, which the Final profile requires. The run went through build-slot, not the checkpoint tool, so there are no SHA-validated receipts.
Native Windows rows: owed, pending qualification d47848cc.

## (6) Merge checks

- Whole-branch `git merge-tree --write-tree origin/rc/2026-10-27 (335b4fbb3) HEAD`: clean, tree `f7994750a`.
- Per-commit cherry-pick chain (b65503281, 43a5291f9, 7491a26fe): clean. The intermediate tree after the fix is `21fe4270`, which matches the Debug.
  Base `80b3a59c4` is an ancestor of rc.
- `check-evidence-diff.ps1 -BaseRef 80b3a59c4 -HeadRef HEAD`: commits=3 entries=0 violations=0.

Cleanup: deleted the 35 `bin-rv514` directories in this worktree by literal path. The scratch clones are under the session scratchpad only.
