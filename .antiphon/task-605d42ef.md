# Review 605d42ef — C514 chain final review (feat/card-task-dfd3f49c @ bc05a3f95)

Reviewed source: `bc05a3f958f464c5a16fb23175d849b24f862d37` (base `80b3a59c40cd18dea905829e428d35844cb89b8d`, an ancestor of rc).
Host: server2 (Debian 12, .NET 9.0.22). Read-only: the subject branch is untouched. This report is the only commit on `feat/card-task-605d42ef`.

**Verdict: the product code is SOUND. There is one test-only adequacy defect (D-1).** The production diff is two files
(`RemoteControlRecoveryService.cs`, `AgentSessionRuntime.cs`). Every input path fails closed. No ambiguous write is replayed, and no Enter
can reach a dialog. The three-prompt flake is gone: 121/121 runs passed. The fixture repairs are test-only.
D-1 matters because a regression that splits the composer read and the Enter fence across two **runner** reads passes every committed
witness. Only my scratch probe catches it.

## (A) D-1 (edc0f0b6) closure, read from the code

- `RemoteControlRecoveryService.cs:639-653`: one `TryReadSnapshotAsync` DTO supplies the exact `/remote-control` composer, the generation proof and the
  `LastSequence` fence. The runtime pre-check (`AgentSessionRuntime.cs:2274-2280`) and the runner `Fence` (`SessionRunnerRuntime.cs:1664-1690`)
  refuse with `stale-observation` when output has moved since that frame. `_inputRevision++` runs only inside `WriteBackendAsync`, after the
  Fence (`SessionRunnerRuntime.cs:4148-4153`). So stale means zero backend bytes at every layer. The HTTP client maps transport, parse and problem failures to
  `unknown`, never to stale (`SessionRunnerHttpClient.cs`).
- At the fenced sequence the frame has a readable composer. `ClaudeDialogObservation.Observe` returns Clear for any readable composer (`ClaudeDialogObservation.cs:20`).
  The old "unreadable frame at the fence sequence leads to observation-unknown" path therefore cannot occur. Any later redraw is stale and gets refreshed.
- Refresh condition (`:653`): only `StaleObservation` loops. `Written`, `Unknown`, `observation-unknown`, `modal-blocked` and every other outcome are terminal.
  Before each new attempt the loop top requires the same epoch, the pinned input revision (custody+1), known input history, the same generation and a non-Blocked dialog.
  It also requires an idle transcript before each Enter.
- Time bound: `while (UtcNow() < deadline)`, using `EvidenceTimeoutSeconds`.
- Dialog guard: a Blocked dialog returns `ModalBlocked` at the loop top. The runtime `Request` and the runner `CheckDialogInput` refuse a non-Clear state under the screen lock.

My own probes (scratch only, built into a detached copy of the tip; patch kept at
`/tmp/claude-1654/-work-worktrees-task-605d42ef/b7506e9d-6939-4922-aaa9-383e6c7c80d2/scratchpad/r605-mutations-and-probes.patch`):

| Probe | Tip |
|---|---|
| `R605_Unreadable_redraw_after_composer_snapshot_is_refreshed`: right after the snapshot that proves the composer, output advances to an unreadable mid-redraw frame | PASS: ArmedObserved, exactly `["/remote-control","\r"]` |
| `R605_Permission_dialog_after_composer_snapshot_is_never_entered`: right after the composer snapshot, a permission dialog paints | PASS: ArmUnconfirmed, evidence `modal-blocked`, inputs `["/remote-control"]` only, no submitted body |

## (D) Mutations (env-switched in a scratch copy; one build; Maintenance class 31 = 29 committed + 2 probes)

| Mutation | Result |
|---|---|
| baseline (unset) | 31/31 |
| (a) also refresh after `Unknown` | RED 1: `Enter_refresh(unknown)` with repeated Enter requests |
| (b) refresh every Enter (condition removed) | RED 8: Executor_rechecks_kind, Busy_at_delivery, Running_launch_owner, Composer_redraw, RV514, Enter_refresh(unknown), Arm_without_UserPrompt, plus my probe |
| (c) pre-fix shape: composer from `TryGetLiveSnapshot`, fence from the runner snapshot | RED 1: RV514 (ArmUnconfirmed) |
| (c2) composer from runner snapshot A, fence from a SECOND runner read B | RED 1, **my probe only**; all 29 committed witnesses green (evidence `observation-unknown`) — D-1 |
| (t) time bound removed, method-scoped `Enter_refresh*` | RED 1/5: `(redraw)` TimeoutException after 30.195 s, attributed, no hang |
| (t) time bound removed, whole class | run hung >400 s, killed by my outer timeout; no own process left (A-2) |
| (b2) after a Written Enter, re-pin custody+1 and keep looping | GREEN 31/31 (A-1) |

## (B) Fixture repairs

- Test-only: the whole-chain production diff is exactly `RemoteControlRecoveryService.cs` and `AgentSessionRuntime.cs`, both from 43a5291f9 and 6b16d3394.
  The dfd3f49c commits touch only `RemoteControlPtyLane.cs`, `RemoteControlRecoveryPtyIntegrationTests.cs` and docs.
- Queue wakeup (`DrainQueuedPromptsAsync`): it calls the production `FlushIfIdleAsync`, whose working gate reads the persisted records. In production, `SessionRuntimeActionCoordinator`
  provides the turn-end queue path, and this fixture lacks it. That claim holds.
  Red-first: d4a293992 adds the held-TurnEnd witness with a passive drain (Debug: 1/1 red at 36.6 s), and 1c0eb2f13 fixes it.
  The witness also asserts that nothing is delivered while TurnEnd is uncommitted.
- Health marker ordering (c10010668): this adds a wait-and-assert for the real TurnEnd and production idle before the synthetic marker. It adds an assertion and removes none.
  It has no deterministic red-first witness (A-3).
- No test was removed or skipped, and no assertion, deadline or polling interval was relaxed. The 30 s prompt deadline and the exactly-once/order assertions are unchanged.
- Flake rate of `C514_Idle_modal_clearance_delivers_three_complete_prompts` at the tip (fresh PTY plus fakeclaude per repetition, `[Repeat(39)]` added in the scratch copy only):

| Condition | Passed/Total | Notes |
|---|---:|---|
| alone | 40/40 | 3m31s, 1-min load 22 down to 5.7 |
| loaded | 40/40 | test 20:10:32-20:14:29 fully inside continuous nonincremental `Antiphon.Tests` builds 20:10:02-20:17:55; load 22 at end |
| first "loaded" attempt | 40/40 | NOT counted as loaded: it waited 150 s for a slot and the load build ended first |
| portable selection | 1/1 | |

  At the Debug's 13.3% base failure rate, 0 failures in 80 runs has probability of about 1e-5.

## (C) Portable selection (fresh, clean tip build)

`dotnet run --project tests/Antiphon.Tests/Antiphon.Tests.csproj --no-build --property:OutputPath=bin-r605/ --property:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(RemoteControlMaintenanceQueueTests*)|(RemoteControlRecoveryPtyIntegrationTests*)|(RemoteControlRecoveryTests*)|(ClaudeDialogDeliveryTests*)/*'`
under `scripts/build-slot.ps1`, TEMP=/tmp. Result: **57/57**, 0 failed, 0 skipped (1m44s).
By class from the TRX: Maintenance 29, PTY integration 9 (real UnixPty plus fakeclaude), Recovery 17, Dialog 2.
The build took 177 s in its slot and had 0 errors. These are gated direct runs, not checkpoint-tool receipts. The whole Unit lane was NOT run.

## (E) rc trial pick onto origin/rc/2026-10-27 (335b4fbb3)

- Whole-branch `merge-tree --write-tree`: clean (tree `de4245617`).
- Code-only pick, in order: `b65503281 43a5291f9 c86e6249d 6b16d3394 2bdd51ad9 4dab6ff57 d4a293992 1c0eb2f13 c10010668`.
  The first seven pick clean. `1c0eb2f13` and `c10010668` conflict only on `docs/investigations/2026-10-30-c514-three-prompt-flake-debug.md` (DU), because that doc's creating commits are skipped.
  Drop that path from both picks. The result's server/tests patch-id equals the chain's (`f87d4bc5`).
- Skip these as report/doc-only: `7491a26fe ecc20ef31 126b5e4e5 c20453c6b 4ff763e43 bc05a3f95`. If you want the investigation doc on rc, pick `c20453c6b 4ff763e43` before `1c0eb2f13`.
- `check-evidence-diff.ps1 -BaseRef 80b3a59c4 -HeadRef bc05a3f95`: commits=15, entries=3, violations=0.

## Defects

### D-1 (test adequacy; product unaffected): no committed witness pins the one-snapshot fence against a second runner read
- **Where:** `tests/Antiphon.Tests/Application/RemoteControlMaintenanceQueueTests.cs:261` (RV514), guarding `RemoteControlRecoveryService.cs:639-653`.
- **Failure:** mutation (c2) still reads the composer from snapshot A, but then takes a SECOND `TryReadSnapshotAsync` B for the fence. If a redraw lands between A and B and leaves an unreadable
  frame at B's sequence, the runtime classifies the dialog `Unknown`, which leads to terminal `observation-unknown`. `/remote-control` stays typed and the arm reports a fast `ArmUnconfirmed`.
  That is exactly the original D-1 symptom, and all 29 committed witnesses stay green.
- **Why:** RV514 fires its redraw on the first runner snapshot after the body, before any composer is visible. It only discriminates between the old live and runner source split (mutation c).
  The brief requires mutation (c), "read two snapshots again", to turn a witness red.
- **Fix:** commit `R605_Unreadable_redraw_after_composer_snapshot_is_refreshed`. Its redraw is triggered by the runner snapshot that PROVES the composer; it is red under (c2) and green at the tip.
  Optionally also commit the dialog probe. Source:

```csharp
[Test]
public async Task R605_Unreadable_redraw_after_composer_snapshot_is_refreshed()
{
    await using var h = await RemoteControlRecoveryHarness.CreateAsync();
    await h.ArmProbeAfterWriteAsync();
    var generation = h.Generation;
    var triggered = 0;
    var partialRenders = 0;
    h.Adapter.BeforeRenderedSnapshot = () =>
    {
        if (h.Adapter.RenderedScreenOverride is not null && ++partialRenders > 3)
        {
            h.Adapter.RenderedScreenOverride = null;
            h.Adapter.SnapshotSequence++;
        }
    };
    h.Runner.SnapshotOverride = sessionId =>
    {
        var dto = new SessionRunnerSnapshotDto(sessionId, h.Adapter.SnapshotRawOutput(),
            h.Adapter.SnapshotRenderedScreen(), h.Adapter.SnapshotSequence, generation, generation);
        if (h.Adapter.ConditionalInputs.SequenceEqual(["/remote-control"]) && triggered == 0
            && Antiphon.Agents.Pty.ClaudeScreen.TryReadComposer(dto.RenderedScreen, out var c) && c == "/remote-control")
        {
            triggered = 1;                       // the composer frame is superseded by a mid-redraw frame
            h.Adapter.SnapshotSequence++;
            h.Adapter.RenderedScreenOverride = "Crunched for 1s\n\n";
        }
        return Task.FromResult(dto);
    };
    var result = await h.ReserveAndExecuteAsync();
    result.ShouldBe(RemoteControlArmResult.ArmedObserved);   // (c2): ArmUnconfirmed, evidence observation-unknown
    triggered.ShouldBe(1);
    partialRenders.ShouldBeGreaterThan(3);
    h.Adapter.ConditionalInputs.ShouldBe(["/remote-control", "\r"]);
    h.Runner.RawInputs.ShouldBeEmpty();
}
```

The dialog probe uses the same trigger with `RenderedScreenOverride = PermissionWaitFixture.CapturedFrame`. It asserts inputs `["/remote-control"]`, `ArmUnconfirmed`, evidence containing `modal-blocked`, and no submitted body.

## Advisories
- A-1: mutation (b2) stayed green. After a Written Enter it re-pins custody and keeps looping. The fake adapter clears the composer as soon as the Enter is written, so no witness shows exactly one Enter
  when a delayed frame still shows `/remote-control` after the write. Add one using `RenderedScreenFrames`.
- A-2: with the bound removed, the whole Maintenance class hangs (>400 s). Only `Enter_refresh*` carries `[Timeout]`, and some other composer-budget test has none.
  Consider a class-level timeout so a class run attributes the red.
- A-3: c10010668, the health-marker repair, has no deterministic red-first witness; its attribution is statistical (base 27/30, including 23505).
  The `beforeTranscriptSave` hook can make one: hold the TurnEnd save and insert the marker.
- A-4: these paths stay terminal and fail closed (liveness cost only), unchanged from 5199c783: a snapshot read failure inside the loop (1 s), an unreadable dialog at the loop top, and an unreadable composer that waits out the budget.

## Owed / not done
- Native Windows rows are still OWED: the C514 class once after qualification d47848cc releases its lock, then the failing case ALONE20 and LOAD10. Keep each failing row's MaintenanceEvidence and input revision.
- The whole Unit lane did not run. These runs are scoped to the portable selection, so ordinaryScopeCompleted is Interim.
- PCs stay pending for SourceLanding Mutation.

Cleanup: 70 `bin-r605`/`bin-r605load` directories were removed by literal path. The scratch worktrees (`wt`, `rcpick`) were removed with `git worktree remove`, which also removed the scratch `bin-r605m` outputs.
No `bin-r605*` remains, and no owned process remains.
