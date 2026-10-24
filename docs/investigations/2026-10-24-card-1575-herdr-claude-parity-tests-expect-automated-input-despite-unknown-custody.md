# CARD-1575: Herdr Claude parity tests expect automated input despite Unknown custody

Confirmed by reconstruction from stored executions and source: group 35a's four
Claude/Herdr success expectations conflict with CARD-0680's explicit Unknown
input-custody contract. Created **CARD-1575** for that test-contract debt and
**CARD-1576** for the independently demonstrated group 34b product defect.
Both cards were created in **Backlog**, with separate `rc` and `testing` labels.
Product and test code were read only. No new build or test was run.

Investigation date: 2026-10-24. Investigator task: `2c5573a8`.
Assigned branch: `feat/card-task-2c5573a8`.
Checkout base: `9376a1daacff7b26d2c2788466efca8d9dac0b11`.

## 35a: observed failures

Source/report tip supplied by the caller:
`bbfbdd278d3e024c6169971f90ef333b2c1c27c4`, task `7cd0d43e`.
The producer's stored `build-provenance.json` identifies the actually tested code
as `8cf9943ca1968bd101eac8ce00b377d0f0fa469c`, `clean: true`, platform Linux,
captured `2026-10-24T18:04:16.025Z`. Its recorded test DLL is 68,508,160 bytes,
SHA-256 `f7758962dc0d8a78c71c2a8d4e05ecb8a816e24e918175ddb465be130f173a78`.
The cited Herdr test, runtime, guard and terminal-session files have no diff
between the supplied report tip and this investigation's checkout base.

The four failing **cases** are three methods, one with two argument rows. All
belong to `Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests`:

| Exact case | Stored test ID | Actual failing assertion / requirement |
| --- | --- | --- |
| `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, False)` | `4be1fe45-af31-c863-5e5b-5e94a9440463` | `row.Status.ShouldBe(SessionStatus.Running, row.FailureReason)` gets Failed, `HerdrAlwaysOnChannelParityTests.StandingRecovery.cs:257`, called at `:64`. |
| `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, True)` | `2b47eefa-5188-45a0-a7f5-460a3a511fe8` | Same assertion and call site. |
| `Herdr_launch_definition_starts_adopts_and_exits(ClaudeCode)` | `b3f6912b-ef39-2eb2-7987-8b3718139fe7` | `HerdrAlwaysOnChannelParityTests.cs:462` calls `WaitForPersistentSessionAsync`; `:614-617` requires agent Running and live session Running, then `:631` throws after 20 seconds. The later `row.Status.ShouldBe(Running)` at `:466` is never reached. |
| `Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume` | `fda6dcc0-5889-979d-635a-8c796b1c9f00` | `(detail.Status == AgentStatus.Running).ShouldBeTrue(...)`, `HerdrAlwaysOnChannelParityTests.cs:313`, gets False with status Failed. |

Paths in that table are under `tests/Antiphon.Tests/Application/`.
The final TRX contains the same failure reason in every case:

```text
Automated Claude input withheld: observation-unknown
```

The launch-definition error also reconstructs the persisted state it queried:

```text
Agent 20e6788c-951b-4cc3-81f0-62b2329e29c8 never reached a Running persistent session. agent=Failed persistent=18f3f4ea-7292-4691-8745-3457d70f7948 live= sessions=[18f3f4ea-7292-4691-8745-3457d70f7948:Failed:Automated Claude input withheld: observation-unknown:None::NonQualifying]
```

This is the stored test's database projection, not a fresh query of production.
It demonstrates a Failed launch with a specific guard reason rather than a
missing Herdr binary, native prompt timeout, marker mismatch or missing reply.

Evidence read directly:

- `/work/test-evidence/task-7cd0d43e/final/final.trx`, SHA-256
  `0874dd7eba701a82c233f5ca4a997374e224aa377632c2839cd2b9afc58436df`.
- `/work/test-evidence/task-7cd0d43e/build-provenance.json`.
- `/work/test-evidence/task-7cd0d43e/base/base.trx`.
- Producer report `.antiphon/task-7cd0d43e.md:68-76`.

The base TRX has 22 passed / 8 failed / 32 not executed (skips); final has
26 passed / 4 failed / 32 skips. The final Herdr class has 14 passed / 4 failed /
6 skips. These are inherited execution results, not fresh passes in this task.

## 35a: mechanism and governing decision

1. `SessionRunnerRuntime.GetSnapshotAsync` explicitly branches for a Herdr child
   at `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:3960`; after a successful
   pane read it returns `ProviderDialogState.Unknown` at `:3981-3982`.
   `InputHistoryKnown` defaults to false in
   `src/Antiphon.SessionRunner.Contracts/ProviderDialogObservation.cs:9`.
   Successful screen reads therefore do not grant input custody. The ordinary
   observation path independently requires a non-adopted, non-uncertain
   **PtyHost** backend at `SessionRunnerRuntime.cs:2521` and otherwise yields
   Unknown at `:2527`.
2. Claude startup uses `RunnerClaudeAdapter.WaitForReadyAsync` and the
   `_terminal.WriteStartupAsync` callback at
   `server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs:193-195`.
   `RunnerTerminalSession.WriteStartupAsync` passes that snapshot to
   `ProviderDialogObservationService.Request` at
   `server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs:134`.
   Later prompt input is guarded too: `RunnerClaudeAdapter.cs:89,104` and
   `RunnerTerminalSession.cs:112-116`. The stored failure does not pinpoint the
   exact startup/body key; both boundaries enforce the same refusal.
3. `server/Application/Services/ProviderDialogObservationService.cs:25-27`
   maps absent/unknown input history to `observation-unknown`; `:39-44` throws
   before conditional backend input; `:60` constructs the exact persisted error
   string. The runner also refuses unknown custody for special startup intents
   at `SessionRunnerRuntime.cs:2545`. Changing the fake's painted composer
   cannot turn Herdr custody into known custody.
4. The launch failure catch preserves the exception as Failed session state:
   `server/Application/Services/AgentSessionService.cs:424-440`. That state and
   reason agree with the stored database projection and all four assertions.

The owner contract explicitly says:

> Herdr lacks complete input custody and reports Unknown for this guard.

Source: `docs/session-runtime-invariants.md:1494`, CARD-0680 section.
The preceding decision text at `:1476-1479` is:

> Visible dialogs refuse; absent, unsupported, adopted or uncertain input history
> holds as Unknown. Each backend write attempt advances input revision before I/O,
> including explicit interactive input and a failed write. No raw-input fallback is
> permitted for an unsupported guarded peer.

The same section at `:1486-1487` confines startup trust/effort and other special
intents to matching-layout checks; they do not accept task text. CARD-0680 plan
D-1 requires positive Clear evidence at
`docs/superpowers/plans/2026-10-23-card-0680-claude-dialog-send-guard-plan.md:69-74`;
D-3 fences each automated write and prohibits raw fallback at `:85-92`.
The explicit operational refusal contract is also recorded at
`docs/ops-http.md:859-875`.

**Conclusion:** these are stale acceptance expectations against the deliberate
CARD-0680 contract. This evidence demonstrates no product violation of that
contract. The actual runner used by these fixtures intentionally cannot prove
Herdr input custody; opting a generic adapter fake into Clear, as CARD-1541 did
for different tests, would not prove this backend's safety.

## 34b: independently demonstrated product defect

Created **CARD-1576**, title **Queued local spill loses source bytes before
SendNow durability validation**. Its body explicitly says the repair is landing
via task `8a0ab444` and the card can close once that land is confirmed. It does
not claim a completed land or activation.

The witness is
`SessionMessageQueueServiceTests.SendNow_preserves_a_complete_local_spill_for_a_queued_ui_message`,
`tests/Antiphon.Tests/Application/SessionMessageQueueServiceTests.cs:199-223`.
It enqueues a large UI body while Working with `WhenIdle`, calls SendNow, and
requires Sent/Delivered, exactly one attempt and submitted pointer, exact inbox
file bytes, and a complete matching UserPrompt.

At test-only commit `db72c9dc41e7b74b99d6810c72bb4cfe9457e723`, the exact witness
failed: 0 passed / 1 failed / 0 skipped, exception from
`SessionMessageQueueService.SendNowAsync:1509`, called at test `:211`:

```text
ConflictException: Queued spill pointer has no durable body and the source message is gone.
```

Reconstruction from that commit and the existing repair diff: the original
`HoldChannelSpillBytes` returned immediately for UI/Delegation origins. SendNow
generated a local file, replaced `message.Body` with its pointer, then called
`HoldSpillBodyForDeliveryAsync` without retaining the raw source body. With no
held body, frozen completion or recoverable source, the guard rejected the row.
This is lost **queue source-byte custody before validation**, not evidence that
the local file itself disappeared. The earlier baseline reproduced the same
exception in `OutputDistillationApplyRaceTests.File_validation_does_not_own_delivery_lock`
and `Apply_and_SendNow_preserve_one_complete_body(False)`.

Existing repair `8807223fb81cebc8e9406851660e7178b3807910` generalizes the helper
for owned generated spills of every origin, storing bytes before replacement.
Source: `server/Application/Services/SessionMessageQueueService.cs:210-219`,
`:1504-1509`; durability recovery/refusal at `:226-260`. The diff is seven
additions and seven deletions, retaining the same three call sites. This task
made no repair.

The report `.antiphon/task-8a0ab444.md:31-35` names the witness and mechanism;
`:45-49` records the committed red/green runs. Supplied report tip is
`cf1cbb3d8c89549107044626eefd40923cad7f68`. Stored results read directly:

| Run | Committed source | Passed / failed / skipped | TRX SHA-256 |
| --- | --- | --- | --- |
| Public API witness | `db72c9dc41e7b74b99d6810c72bb4cfe9457e723` | 0 / 1 / 0 | `34a16747b511f3e11d4021cb49df9d86b536a26bdcd146dde2d5d7c2f17ed04a` |
| Existing repair, five selected classes | `8807223fb81cebc8e9406851660e7178b3807910` | 98 / 0 / 0, including that witness | `d2508b9bd648ed689ec83c3000ae778f340b21e2b5af994d8c9fea4c9e24ee0c` |

TRX files are under
`/work/worktrees/task-8a0ab444/.antiphon/8a0ab444-evidence/{witness/witness,fix/fix}.trx`.
No Windows execution, product Review or completed publication is established by
these results. No additional investigation stage is needed for this already
repaired mechanism; publication confirmation remains caller-owned.

## Card ownership and durable board rows

Before creation, complete `card.ps1 search '<phrase>' -Board Antiphon -All`
enumerations returned: `Herdr` 108 cards; `input custody` 0; `spill` 37;
`8a0ab444` 0; `durable body` 0. Full candidate bodies were read with `card.ps1 get`.
CARD-0865 owns earlier prompt/marker/Grok-policy failures; closed CARD-1252,
CARD-1273 and CARD-1276 own distinct stale-screen/marker issues. Closed CARD-1541
owns three other delivery fixture classes; CARD-0680 owns the guard itself.
CARD-1565 concerns converter receipt identity after crash recovery;
closed CARD-0025 concerns the original generic spilling capability; closed
CARD-1056 concerns post-receipt payload release. None owns either exact new
finding above. No existing card was moved or closed.

Create responses recorded these board rows:

| Identifier | Card ID | Status | Created UTC |
| --- | --- | --- | --- |
| CARD-1575 | `687b40c1-a1b1-4a4b-9e53-25b367fb0e65` | Backlog | `2026-10-24T23:32:25.628317Z` |
| CARD-1576 | `f116cbac-1041-4f5f-8d64-a634350830a8` | Backlog | `2026-10-24T23:32:41.855681Z` |

Both are on Antiphon board `8988ca03-7414-47ad-b0b6-51556c701703`, Backlog column
`22a04b2d-609c-45bb-821d-280b948792fa`, and have labels `["rc", "testing"]`.

## Remaining uncertainties and verification handoff

- No new runtime experiment or native Windows run was performed. Confirmation is
  reconstructed from the stored TRX, recorded source provenance and source paths.
- The precise first startup/body input refused in each launch is not captured by
  the stored stack; Unknown custody and its refusal mechanism are established
  independently of which first key reaches that guard.
- Whether supporting fully automated Claude/Herdr input is a future product goal
  is outside this contract investigation. Present custody support is explicitly
  absent. No qualified Herdr custody mechanism was demonstrated here.
- There is no green receipt for corrected 35a expectations. Native qualification
  debt and 35a's other skips remain unresolved. The 34b land is not verified here.

One-red/one-green verification handoff, **not executed by this investigation**:

| Exact selection | Red evidence / requirement | Green requirement |
| --- | --- | --- |
| `/*/*/HerdrAlwaysOnChannelParityTests/Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume` | Stored final TRX has this one case failing the Running assertion with `observation-unknown`; retain a scoped 0 passed / 1 failed pre-change receipt. | The corrected acceptance contract must produce 1 passed / 0 failed / 0 skipped while retaining explicit Unknown refusal and applicable tab assertions. This receipt is owed. |

Any commissioned execution uses `scripts/build-slot.ps1`, a distinct alternate
OutputPath and the exact method filter. The other three affected cases remain
required follow-up coverage; a single green row would not qualify the whole class.

## Not done, noted

Recommend the smallest test-contract update scoped to the two HerdrAlwaysOnChannelParityTests partial files, preserving supported history/receipt/tab proofs and explicit Claude/Herdr Unknown refusal; no product guard change, design or implementation was done here.

--- next stage ---
next: plan
handoff: CARD-1575 has a confirmed stale Claude/Herdr success contract: four cases conflict with CARD-0680 Unknown custody. Plan the scoped acceptance update with explicit refusal and retained supported proofs; the exact one-red/one-green row is recorded above. CARD-1576 records the already repaired 34b spill defect and awaits caller confirmation of task 8a0ab444's land.
artifact: docs/investigations/2026-10-24-card-1575-herdr-claude-parity-tests-expect-automated-input-despite-unknown-custody.md
