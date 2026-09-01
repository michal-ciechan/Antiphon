# CARD-0299 — Codex Plan brief typed, Enter never submitted: implement the investigation's three slices

**Date:** 2026-09-01 (Plan pass, task 7aa17d3e — design only; no production code changed)
**Card:** CARD-0299. **Investigation (do not re-open):** `docs/investigations/2026-09-01-card-0299-codex-plan-unsubmitted.md` (`fdc94532`). Incident `4ea068fa` / session `41959e81`.
**Precedent:** CARD-0133 plan (`docs/superpowers/plans/2026-08-27-card-0133-codex-readiness-and-boot-wedge-plan.md`). S1 shipped `09a6a8ba` with a latch hole; S2–S4 did not ship. This card is those remaining pieces plus the S1 hole, **narrowed** to what the investigation named. Do not implement CARD-0133 S4 (`ComposerInputProbe` as ready gate). Do not widen `TranscriptConfirmTimeoutSeconds` (30), `DeliveryFailTimeoutMinutes` (10), or `CodexReadyQuietPeriodMs`.

**Sources (verified this pass):** investigation + card; `SubmitEvidence.cs`; `SessionMessageQueueService.WaitForTranscriptConfirmAsync` (`:1871-2020`) and `HandleDeliveryFailureAsync` (`:2530`); `RunnerCodexAdapter.WaitForReadyAsync` / `CodexReadyDetector`; `FakeAgentProtocolAdapter` composer/submit knobs; `SessionMessageQueueDeliveryVerificationTests` Codex unobservable arms; `CodexMcpBootProbeTests` marker strings; `AgentIncidentKind` live enum (ends at `QueuedInputNeverConverted = 43`); `FailDeadSessionTasksAsync`. Diagnosis is not re-litigated.

---

## Decision

Three slices, investigation order. S1 is the 30-second path. S3 is typing-time. S2 is what happens when S1 correctly returns `NoSubmitOutput` on a frozen child instead of a 10-minute watchdog.

1. **S1 hole.** `sawPositiveSubmit` must not latch emptied-composer on one poll. Require the head fragment gone for `PostEvidenceSettleMs` (already 500) of consecutive snapshots. At the unobservable deadline, re-read the current screen: if `HeadFragmentIsVisible(screenNow, body)`, it is **not** a submit — `NoSubmitOutput`, regardless of any earlier empty frame. Working-indicator remains immediate positive (do not unlatch it).
2. **CARD-0133 S3.** After quiet + trust, both Codex adapters wait until `Starting MCP servers` / `Booting MCP server` have been absent 500 ms, bound `CodexBootStatusMaxWaitMs` (10 s). Bound expiry: Warning and proceed (boot line is not a modal). Ready must not fire because a hung 1 Hz spinner went quiet.
3. **CARD-0133 S2, without the unshipped S0-P4 probe.** `NoSubmitOutput` on a cold Codex delegate first delivery (null baseline, origin Delegation, attempts 1, session Running, task Dispatched on that session) → `BootWedged` + `KillAsync` + one relaunch. **No** ComposerInputProbe at failure time (S0-P4 never shipped a measured clear keystroke; the last frame + three Enters is the proof). The AlwaysOn-kill arm in `HandleDeliveryFailureAsync` does **not** fire for ephemeral pool delegates (`AlwaysOn: false`) — this incident's agent was `task-4ea068fa`. S2 must kill regardless of AlwaysOn.

`disable_paste_burst` stays. `cx.ps1` stays off this path. Model-alias terra vs sol stays a separate question.

---

## Ground truth (checked, not guessed)

### Why S1 shipped and still certified Sent

`WaitForTranscriptConfirmAsync` `:1929-1945`:

```
if (!sawPositiveSubmit) {
    if (kind == Codex)
        sawPositiveSubmit = SubmitEvidence.IsPositive(..., snapshot.RenderedScreen, body);
}
```

`SubmitEvidence.IsPositive` (`SubmitEvidence.cs:17-19`): Working indicator **or** (head visible before Enter **and** not visible now). `HeadFragmentIsVisible` returns false on an empty snapshot (`normalizedScreen.Length == 0`). One mid-redraw empty/ghost/MCP-spinner frame latches true. `mayReEnter` is `observable || !sawPositiveSubmit` (`:1994-1996`), so re-Enter stops. At 30 s, `:1954-1962` takes degraded `Delivered` + `RecordDeliveryUnverifiedAsync`. Production log: *"confirmed by degraded screen-only verdict after 30s … **1 Enter(s) sent**"*.

Existing pin `Codex_unobservable_no_post_enter_output_returns_no_submit_output_after_three_enters` never emits a transient empty frame (`SubmitAck=""`, composer keeps the body on every snapshot), so it never hit the latch.

### Why ready fired while MCP was still on screen

`RunnerCodexAdapter.WaitForReadyAsync` / `CodexReadyDetector` are quiet-after-visible only. A hung MCP line at 1 Hz that then **stops painting** (this session: last ANSI byte 10:30:17Z, spinner frozen at `1s`) looks quiet. CARD-0195: typing during that line is MCP-interrupt / queued-input (`tab to queue message` on the last frame).

### Why the 10-minute watchdog ran

Delegate briefs are `WhenIdle` / `QueuedMessageOrigin.Delegation`, not `SendPromptAsync`. After false-Sent, the delivery watchdog waits `DeliveryFailTimeoutMinutes` (10) for a turn prompt that never comes. Even if S1 had returned `NoSubmitOutput`, `HandleDeliveryFailureAsync` reverts to Pending and kills only `AlwaysOn` idle sessions (`:2661-2664`). A pool delegate is not AlwaysOn. Follow-through: 60 s stranded sweep re-types into the same frozen TUI → park → watchdog. S2 has to own kill+relaunch on that conjunction.

---

## Slices

### S1 — Do not latch emptied-composer on one poll; deadline re-check

**Files:** `SessionMessageQueueService.WaitForTranscriptConfirmAsync`; optionally split emptied vs working in `SubmitEvidence` (pure helpers, existing `IsPositive` can stay as a one-shot OR for tests that want it); `FakeAgentProtocolAdapter`; `SessionMessageQueueDeliveryVerificationTests`.

**Latch (Codex unobservable only):**

- `CodexWorkingIndicator.IsVisible(screenNow)` → `sawPositiveSubmit = true` immediately. Do not unlatch if a later frame lacks Working (the turn started).
- Emptied-composer (`HeadFragmentIsVisible(before, body) && !HeadFragmentIsVisible(now, body)` and not Working): record `emptiedSince ??= now`. Only set `sawPositiveSubmit` when `now - emptiedSince >= PostEvidenceSettleMs` (clamp already 0–3000 in `SettlePostEvidenceAsync`). If a later snapshot shows the head again: `emptiedSince = null` and `sawPositiveSubmit = false` **unless** Working already fired.
- Poll interval is already `PollIntervalMs`; 500 ms settle is ≥1 extra poll at default 250 ms. Do not add a second settle loop.

**Deadline (`!observable`, Codex):** before the degraded-Delivered arm, snapshot again. If `HeadFragmentIsVisible(screenNow, body)` → `NoSubmitOutput` (log: body still in composer; Enters sent). This is the investigation's "durable last frame still holds the body" gate. Working visible and head gone → existing degraded Delivered.

**Fixture (required by the brief):** `FakeAgentProtocolAdapter` gains a post-Enter snapshot sequence, e.g. `EmptyComposerSnapshotsAfterEnter = 1` (or an explicit list). `SnapshotRenderedScreen` for those N calls returns the screen **without** `_composer` (empty / ghost), then returns to `screen + "> " + composer`. Combined with `SwallowSubmits = 99` and `SubmitAck = ""` (Enter never submits, body stays in `_composer`).

New test `Codex_unobservable_transient_empty_frame_does_not_latch_emptied_composer`:
- body echoed, one empty/ghost snapshot, then body again
- **3 Enters**, `ConflictException` containing "submitting Enter produced no output"
- **must not** `Sent` / 1 Enter / `DeliveryUnverified`

Keep green:
- `Codex_unobservable_no_post_enter_output_returns_no_submit_output_after_three_enters`
- `Codex_unobservable_body_trailing_frames_are_not_submit_evidence_and_re_enter_until_no_submit_output`
- `Codex_unobservable_working_indicator_confirms_by_screen` (still 1 Enter, Screen)
- `Claude_unobservable_keeps_advance_based_screen_verdict_after_settled_baseline`

`SubmitEvidenceTests`: add a case that one-shot `IsPositive` is true on empty-after-body (that is the hole); the **queue** test is what forbids latching it. Do not change `IsPositive` to require settle internally (it is a pure screen function with no clock).

### S2 — `NoSubmitOutput` on a cold Codex first delivery: BootWedged + kill + one relaunch

**Files:** `HandleDeliveryFailureAsync`; new `AgentIncidentKind.BootWedged = 44` (re-read the live enum at code time; 43 is shipped); `AgentTask.BootWedgeRelaunchCount` int default 0 + EF migration (`dotnet ef migrations add`, never hand-author); `DelegationSettings.BootWedgeRelaunchLimit` default **1**; `AgentTaskDispatcher.RelaunchWedgedAsync`; `FailDeadSessionTasksAsync` skip.

**Conjunction (all required):**

- `verdict == NoSubmitOutput`
- Codex session, `Status == Running`
- message origin `Delegation` (WhenIdle brief — this path; Mode:Now has no row, skip S2)
- `DeliveryAttempts == 1`, `LastDeliveryBaselineSequence == null`
- an `AgentTasks` row with `AgentSessionId == sessionId` and `Status == Dispatched`
- `BootWedgeRelaunchCount < BootWedgeRelaunchLimit`

Then, **instead of** revert-to-Pending + AlwaysOn-only kill:

1. Incident `BootWedged`, Warning, never Critical (delegate is not channel-bound). Message names ANSI tail / "TUI stopped painting; brief still in composer; MCP boot line still visible" when the snapshot still has `Starting MCP servers`. Dedup per session.
2. Mark the failed queue row **Canceled** (not Pending — CARD-0117: Pending on a dead session is a stranded retry).
3. `KillAsync` the session through the same runner primitive `FailNeverStartedAsync` uses. Do not `Fail` the task. Do not delete the pool agent.
4. `RelaunchWedgedAsync`: spawn a fresh session for the **same** agent (same worktree, same launch spec), re-enqueue the brief pointer (`WhenIdle`, Delegation, new row), `BootWedgeRelaunchCount++`, restamp `DispatchedAt` so the 10-minute watchdog measures the relaunch, event `Warning` "boot-wedge relaunch 1/1".
5. In-memory pending set (like `_deadSessions`) so `FailDeadSessionTasksAsync` skips the task while the old session is Stopping/Stopped **this tick**. After the new session is attached, the skip is unnecessary.

At the limit (second `NoSubmitOutput` on the relaunch): fail **now** with the CARD-0133 sentence: boot prompt could not be delivered; TUI stopped reading after the brief rendered, twice; relaunched once and wedged again. ~40 s after dispatch (30 s confirm + relaunch ready) on a double wedge, not 600.

**Not in S2:** ComposerInputProbe / S0-P4 keystroke. If a later card measures a Codex clear token, it can gate the kill; this incident's child emitted zero bytes for 9.5 minutes after the last frame still holding the body.

**Tests:**

- Queue: Codex unobservable, SwallowSubmits 99, SubmitAck "", origin Delegation, Dispatched task on that session, attempts 1, null baseline → after confirm, incident `BootWedged`, session killed, task still Dispatched, new session + new queue row, `BootWedgeRelaunchCount == 1`, `DispatchedAt` moved. Shared-Postgres: assert on seeded ids.
- Each negated leg: attempts 2 → no kill; baseline non-null → no; origin Ui → no; Claude kind → no; no Dispatched task → today's revert.
- Dispatcher: second wedge → task Failed with the named reason; `FailDeadSessionTasksAsync` does not Fail a relaunch-pending task.
- Existing AlwaysOn `NoSubmitOutput` kill path for channel-bound sessions unchanged.

### S3 — MCP boot line must clear before ready (both Codex adapters)

**Files:** `CodexDetectors.cs` (shared `CodexMcpBoot.IsVisible` + wait helper); `RunnerCodexAdapter.WaitForReadyAsync`; `CodexAdapter` / `CodexReadyDetector.WaitAsync`; `AgentRegistrySettings.CodexBootStatusMaxWaitMs` default 10_000 (0 disables); validator: non-negative; `RunnerTerminalSession` already has `SnapshotScreenAsync`.

After `WaitForQuietAfterVisibleAsync` returns true (and after trust Enter if any): if the rendered screen contains `Starting MCP servers` or `Booting MCP server` (CARD-0195 / `CodexMcpBootProbeTests` strings — match `Starting MCP server` so `(1/2)` forms hit), poll until the line has been **absent** for 500 ms (`PostEvidenceSettleMs` or a local const; do not invent a third settle knob unless tests need it), bounded by `CodexBootStatusMaxWaitMs`. Expiry: log Warning `"Codex MCP boot line still visible after {ms}ms; typing anyway"` and return true. Never fail ready on the boot line (CARD-0133: not a modal).

A hung spinner that stops painting: quiet-after-visible returns in ~`CodexReadyQuietPeriodMs` (1 s) **today**; S3 then waits up to 10 s for the line to leave, then proceeds. S1+S2 own the frozen-TUI aftermath.

**Tests:** Scripted/fake runner that paints `Starting MCP servers (1/2): node_repl (1s  esc to interrupt)` for N snapshots then clears → ready waits until clear + 500 ms. Line never clears → ready true after bound, Warning logged (harness log sink). In-process `CodexAdapter` and `RunnerCodexAdapter` both go through the helper (one test each, or one helper test + a lockstep comment). Existing `CodexAdapterLocalShellTests` ready pins stay green (no MCP line in those scripts).

Do not add `ComposerInputProbe` here (CARD-0133 S4, out of scope).

### S4 — Docs (same PR)

- `docs/session-runtime-invariants.md`: Codex submit evidence is Working **or** emptied-composer **sustained** `PostEvidenceSettleMs`; a body still visible at the unobservable deadline is `NoSubmitOutput`; cold first-delivery `NoSubmitOutput` relaunches once (`BootWedged`).
- `ProviderContractCatalog` Codex `DeliveryVerification` reason: emptied-composer is consecutive snapshots, not one poll.
- Investigation file: one line at the top pointing at this plan (Status: plan landed). Do not rewrite the mechanism.

---

## What this card does not do

- Widen any timeout.
- CARD-0133 S4 ready-time `ComposerInputProbe`.
- S0-P4 headed clear-keystroke canary as a gate (optional later).
- `cx.ps1` / ACP / `CodexSubmitConfirmation` (not on the delegate-brief path).
- Forcing `--model gpt-5.6-sol` vs config.toml terra.
- Disabling MCP servers (`mcp_servers.node_repl.enabled=false`) as the fix (CARD-0195 candidate; not this incident's Enter latch).
- Changing Claude/Grok unobservable advance fallback.

---

## Test matrix

| Layer | Test |
|---|---|
| `Antiphon.Tests` Application | **S1 incident fixture:** echo body, one empty/ghost snapshot, body again, SwallowSubmits 99 → 3 Enters, `NoSubmitOutput`, not Sent |
| `Antiphon.Tests` Application | Existing Codex unobservable 3-Enter / trailing-frames / Working-indicator / Claude advance pins stay green |
| `Antiphon.Agents.Pty.Tests` | `SubmitEvidence` one-shot empty-after-body remains true (documents the hole the queue must not latch) |
| `Antiphon.Tests` Application | **S2 happy:** Delegation + null baseline + attempts 1 + Dispatched Codex → BootWedged, kill, relaunch once, new queue row |
| `Antiphon.Tests` Application | **S2 negations:** attempts 2, non-null baseline, Ui origin, Claude, no task |
| `Antiphon.Tests` Application | **S2 limit:** second wedge Fails the task with the named reason; dead-session sweep skips pending relaunch |
| `Antiphon.Tests` / Pty | **S3:** MCP line N frames then gone → wait; never gone → bound then proceed |
| Inherited red | `SessionMessageQueueDeliveryVerificationTests` two CARD-0195 known-red names: re-run at base before blaming S1 |

Run per `docs/testing-and-build.md`:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0299/ -- --treenode-filter "/*/*/SessionMessageQueueDeliveryVerificationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0299/ -- --treenode-filter "/*/*/AgentTaskDispatcher*/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0299/ -- --treenode-filter "/*/*/SubmitEvidenceTests/*"
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0299/ -- --treenode-filter "/*/*/CodexMcpBoot*"
```

Forward slash on OutputPath. Tests then Pty, sequential. Delete `bin-card0299*` after. No client.

---

## Sequencing and risks

**Order: S1 → S3 → S2 → docs.** S1 without S2 still leaves a correct `NoSubmitOutput` to revert+sweep+watchdog on a pool delegate (better than false-Sent, still 10 minutes). S2 without S1 never fires (production still Sent). S3 is independent and cheap. One PR.

| Risk | Standing |
|---|---|
| Settle 500 ms delays a real emptied composer | Working indicator still confirms immediately. Empty composer without Working waits 500 ms then degraded Delivered as today. |
| Deadline re-check fights a wrap that hides the 40-char head while submitted | Transcript row would have confirmed already on the observable/unobservable pull. Zero-row + head visible is this incident. |
| S2 kills a session that was about to submit | 3 Enters + 30 s + body still visible. Four same-day fresh processes succeeded. Limit 1. |
| FailDeadSession races the kill | In-memory skip + restamped DispatchedAt. Test pins it. |
| MCP wait 10 s on every Codex launch | CARD-0195 worst clear was 3.34 s; typical under 4 s. Bound is a cap, not a sleep. |
| `BootWedged = 44` taken | Re-read `AgentIncidentKind` at code time. Append, never renumber. |

---

## Execution notes

After deploy, `scripts/codex-boot-census.ps1` 0-rows + brief `Sent` should go to zero; a wedge should be a `BootWedged` row and a second session, not a 10-minute Failed with 0 tokens. Do not re-dispatch `4ea068fa`. The four later Codex Plans that day already proved a fresh process works.
