# Master's 69 inherited reds after CARD-0680 / CARD-1491

Debug task `eda53c54`, 2026-10-23. **Master is not releasable against these required rows.** The evidence identifies 68 cases blocked by obsolete fake Claude observation setup and one by a missing fake pool effort stamp. These are fixture contract failures, not demonstrated wrongful refusal of a supported production send. There may be additional failures behind the first veto; repair and verification must establish that, not this diagnosis. No production/test source, desktop, Windows qualification, server or runner was changed. No build or test was run.

## Evidence and exact population

| Evidence | Source / observed outcome |
|---|---|
| CARD-1529 full-class CP-2 / CP-3, run `20261023-072943-d79d` | `1a81be39eadaca3f78b811533f786e72350abd0d`: CP-2 415 executed / 362 passed / 53 failed; CP-3 20 / 3 / 17; zero skipped |
| Direct detached original-base CP-90, copied receipts `c1529-baseline-evidence` | `410738a522585e07943cabf23c1243790773d153`: 123 / 53 / 70, zero skipped; all 70 candidate identities also fail at base |
| Final focused CP-4, run `20261023-074222-af6a` | `9e4d2f95b9d70f3a5cbc1571f602884e8a2a019b`: 25 / 25 / 0, including repaired `C544_BriefHandoffRecovery` |
| This report's comparison | Independently parsed baseline and candidate TRX definitions/results; **69 unique exact class + result-name matches are Failed in both** after removing that C544 case: brief recovery **3**, boundary recovery **50**, verification delivery **16** |
| Current assigned master base | `617e1d5db0e8f00b8ddb7480b7c4c4ab9248a6de`; no production-source change from `410738a52`, and none of the three classes or implicated helpers changed. These are carried receipts, not a fresh run at this SHA. |

All CARD-1529 paths below are relative to `/work/worktrees/task-684b15a2/.antiphon/`:
`checkpoints/20261023-072943-d79d/{report.json,rows/CP-2/run.trx,rows/CP-3/run.trx,executor.log}`;
`c1529-baseline-evidence/{report.json,rows/CP-90/run.trx,executor.log}`;
`checkpoints/20261023-074222-af6a/report.json`;
`c1529-baseline-comparison.json`; and `task-684b15a2.md`.
The automatic baseline attempt in `20261023-072943-d79d` has no baseline TRX: its NEW labels are invalid attribution evidence. The copied direct baseline retains the removed `/tmp/antiphon-c1529-base-684b15a2` checkout's provenance. This report does not relabel any red receipt green.

## Root causes, attribution and smallest repair

| Group | Cases | Failure signatures (exact assertion excerpts are in the roster) | Attribution / classification | Smallest repair |
|---|---:|---|---|---|
| D1: brief recovery | 3 | `QueuedMessageStatus.Sent` expected, `Pending` actual | CARD-0680 missing fake dialog evidence; fixture debt | Bind `BriefFixture`'s fake to the actual accepted generation and opt into a recognized Clear rendered frame / guarded conditional writes. |
| D2: boundary functional cases | 45 | Zero submitted bodies/UserPrompts; pending instead of sent/canceled; delivery attempts 0; healthy Working false; post-input injected bookkeeping exception escapes | Same CARD-0680 first blocker; fixture debt. The bookkeeping cases never reach their intended post-input boundary, so their expected custody branch differs. | Update the specific `OpenQueueAsync`, `OpenRecoveryAsync`, `RunResumeBookkeepingAsync` and caller/receipt rigs, including independent fake instances. Preserve all negative evidence cases. |
| D3: boundary statement cases | 4 | existing brief 65/34; missing brief 61/40; watchdog 61/40; repeated discovery expects `Attached=False`, gets True | Same missing dialog evidence changes the exercised path. This is distinct from CARD-1522's older seven stale budgets. | First make the intended successful send happen. Then inspect the exact SQL roster; a legitimate added query needs an explained exact contract update, not a widened ceiling or timeout. |
| D4: verification delivery | 16 | Delivery JSON null; zero submitted bodies/carrying rows; AwaitingReceipt instead of Confirmed; injection count 0; pointer test NullReferenceException | Same CARD-0680 first blocker in `C544DeliveryRig.Caller` / refusal rigs; fixture debt | Opt the fake caller into current evidence and preserve whole-body receipt, batching, busy/idle, failure-cut and restart assertions. Existing store/coordinator debt stays separate. |
| E1: warm-reuse preparation | 1 | `ReuseOutcome.Reused` expected, `SpawnFresh` actual | CARD-1491 explicit effort stamp missing; fixture debt | In `WarmReuseSendsNoPrepareAsync`, stamp the declared Medium Claude process with the catalog's actual launch effort, not a fabricated production backfill. Retain no-second-session / no-prepare assertions. |

**Actual introduction commits, rather than landing/report tips:**

- CARD-0680: `4de01c9a50ceb5874294b70f79deaa718b57b908` introduces `ProviderDialogObservationService`, the queue's `ModalBlockReasonAsync` observation veto and fake opt-in observation. `git blame` points there for `SessionMessageQueueService.cs:5666-5672`. Re-cut equivalent: `dd196f41aad217c6fd00d3dca82c396a91167bd5`; original reviewed equivalent: `28829e18a4a156ce4182a79951c18cfc100c4408`. The brief's `3334e82ae8e52cb075893a7ff74e61aa6aab511f` is a documentation/evidence commit, not the behavioral hunk.
- CARD-1491: `34ffef9df768819988f61cbe8850db7b477d9182` introduces `ClaudeEffortMatchesPool` (`AgentTaskDispatcher.cs:8272-8276`) and both matching sites (`:8146`, `:8179`). `ed96820bde8e914fbe11e340820da54f39050268` is the later fixture/disposal correction, not the original gate. The affected warm fixture at `DelegationDispatchRecoveryBoundaryTests.AbsentEvidence.cs:797-865` seeds Medium/Claude/Idle with no `PoolClaudeEffort`.

`FakeAgentProtocolAdapter.ObserveDialogAsync` (`Agents/FakeAgentProtocolAdapter.cs:384-400`) returns null unless `DialogGuardEnabled` is explicitly enabled. None of these three classes or their independent delivery fakes enables it. `AgentSessionRuntime.ObserveDialogAsync` uses the registered fake directly; the queue preflight converts null to `Claude dialog observation unavailable` before a delivery attempt. Generation-setting alone does not enable observation. A global fake default of Clear would erase intentional unsupported/Unknown coverage; use explicit fixture setup instead.

Previous green evidence is narrower than a bisect. CARD-1514's whole boundary-class receipt at `ffa98a8f76588c12df6b0437d24b541762b95f09` executed 381 / 374 passed / 7 failed. **48 of today's 50 exact boundary result names occur there and passed**; the other two are missing-brief/watchdog budget cases whose argument changed from 60 to 61 in CARD-1522. CARD-1522 subsequently qualified all 14 budget cases at `22e324fe61864daa7370b0f2f6fbed57f8ca2b15`, including all four now red. Thus those four are not simply the old seven budget failures still unresolved. Earlier exact whole-class green receipts for the three brief cases and sixteen delivery cases were not located. Their source-level first-blocker attribution is deterministic; no before/after execution or bisect for those 19 is claimed. No older independent root cause is established among these 69; removing a first blocker can uncover one.

## What the two Reviews actually proved

| Review | Actual receipt | Coverage of these 69 |
|---|---|---|
| CARD-0680 original Review `821380f3-09da-4431-8262-c9aa3da9463e` | `e85f98d18179a0f0c4f02f4757516b8dda1fb451`, run `20261023-023536-7d9b`: CP-1..CP-7 each 1/1. Types: ClaudeDialogObservationTests, ClaudeDialogInputGuardTests, ClaudeDialogDeliveryTests, ExpectationDirectDeliveryTests, WatchdogServiceTests. | **0/69 selected.** The three failing classes were never qualified by that receipt. Native CP-8..CP-11 remain owed. |
| CARD-0680 re-cut confirmation `7d116f10-6c03-444e-9cd8-9ee96b8e8673` | `b813cf597f9c0e3ebb8baaa8ae1f213874ef3e13`, `20261023-041553-1a2a`: same seven exact portable methods passed. | **0/69 selected.** This valid later run completes the earlier re-cut task's build-timeout gap; it adds no whole-class coverage. |
| CARD-1491 Review `856fc540-22bd-483f-a4af-69d2256aafbd` | `747aee9112a0dbd38bfd129a6566457ae9bfbc44`, `checkpoints-c1491-review/20261023-035659-029b`: CP-3..CP-9 counts 27,2,1,8,10,4,2 = **54/54**, zero failed/skipped. | **0/69 selected.** Its fixture audit did add stamps in the listed pool/environment/routing/scheduling fixtures, but omitted this independent boundary warm fixture. It was not a claim that every warm fixture or these classes passed. |

Reports and raw receipts are in the corresponding task's `/work/worktrees/task-<short-id>/.antiphon/` directory. The source evidence document [CARD-1491 code evidence](../superpowers/plans/2026-10-23-card-1491-code-evidence.md) explicitly defers whole affected classes. These scope limits explain the missed fixtures; they do not invalidate the selected green tests or authorize today's incomplete Final gate.

## Production behavior and RC boundary

The production factory constructs `RunnerClaudeAdapter`, not the unused in-process `ClaudeAdapter`. Boot prompts use `RunnerTerminalSession.WriteGuardedAsync`; queue and other automated writers use `AgentSessionRuntime.SendInputAsync` / `SendConditionalInputAsync`. Each obtains fresh snapshot evidence and sends the exact epoch/generation/sequence/revision in the conditional request. HTTP and phone-home clients check `claudeDialogGuardV1`; runner `CheckDialogInput` rechecks at the final write. Non-Claude session kinds bypass this Claude-specific observation veto. The explicit authenticated interactive input route remains usable.

**No, production adapters do not provide usable Clear evidence on every mode/send path.** `SessionRunnerRuntime.ObserveDialog` (`:2513-2536`) marks history known only for non-adopted PtyHost sessions without uncertain input. Herdr snapshots (`:3952-4014`) explicitly report Unknown. Old peers without the capability, missing/unreadable/partial screens, adopted sessions and uncertain writes also refuse. This is the documented intended fail-closed behavior, including real Claude sessions: [runtime invariant](../session-runtime-invariants.md#claude-automated-input-dialog-guard-card-0680), CARD-0680 decisions D-1/D-3. It can prevent automation on an unsupported mode indefinitely; do not describe that mode as fully supported or silently weaken Unknown => refuse. Supporting it would require separately qualified observation/input custody. No evidence here shows a recognized Clear supported live send wrongly refused. Portable fake tests do not establish native detector accuracy; the owed live canaries remain activation gates.

RC source is `5a126f6fc5ae4e16d803b628716b8f0485a37174`. Its queue checks only Remote Control episodes, with no dialog-observation veto, and it has no `PoolClaudeEffort` matching. The three test classes and C544 delivery rig are unchanged between RC and this master except for boundary budget values and additional obligation assertions. **The two diagnosed mechanisms are absent from RC; that does not prove all 69 tests pass on RC.**

| RC question | Evidence-backed answer |
|---|---|
| Which of the 69 are confirmed failed on exact RC? | None established by the locally available receipts. This is **unknown**, not a claim of zero failures. No exact-RC result roster for these 69 was available on this host. |
| What does the archived Windows diagnosis establish? | [Exact RC qualification diagnosis](2026-10-23-rc-5a126f6fc-qualification-reds-debug.md) retains 19 observed failures through its cutoff, including `C544_BriefHandoffRecovery`, which is **excluded** from these 69. It does not preserve a passing roster sufficient to independently match the 69; the run was incomplete. The sixteen verification-delivery cases are **inferred green**, not receipt-validated: RC has the same twenty-method class; ordinary nightly grouping keeps that small class whole (well below the 2,400-second split threshold), group 13 completed, and its exhaustive failure list contains only the excluded C544 case from that class. This inference does not cover the other 53. |
| Are older RC failures possible? | Yes. RC still expects 60 for missing-brief/watchdog where CARD-1522 established 61 on pre-guard master; those are semantic counterparts, not today's exact `(…, 61)` result names. Do not transplant master count edits without the RC SQL roster. CARD-1464/CARD-1492 and CARD-1529 also predate these guards. |
| Can the caller release RC based on this investigation? | These master-only first blockers do not implicate RC, but this report gives **no whole-class RC green or release approval**. Obtain the already-running qualification's archived roster when available; do not interrupt/restart it. If missing, commission the exact unresolved RC selection separately. No broad confirmation was run under this read-only Debug brief. |

## Board disposition and repair order

Complete `card.ps1 search ... -Board Antiphon -All` enumerations were performed for all three class names, `dialog observation`, `effort stamp`, `Claude dialog`, `DialogGuardEnabled`, `warm reuse fixture`, and `WarmReuseSendsNoPrepare`. Relevant hits were read in full: CARD-1522 (old budgets), CARD-1464 (raw transcript/store projection), CARD-1492 (missing coordinator), plus original CARD-0680/CARD-1491. None owns the two new omissions.

| Card | Disposition |
|---|---|
| **CARD-1541**, `be945708-dd25-470c-9af3-be12e772db54` | Created Backlog, Normal/Soon: 68 missing-observation fixture cases; description contains every exact name and baseline signature. |
| **CARD-1542**, `ac01b40f-3e22-4dc7-bbbc-bdbf070ab140` | Created Backlog, Normal/Soon: one missing effort stamp; exact name/signature and repair helper included. |
| CARD-1522 / CARD-1464 / CARD-1492 / CARD-1529 | Existing distinct obligations referenced, not duplicated. The excluded C544 brief acceptance has already been repaired on CARD-1529's pushed branch. |

CARD-1541 creation returned the created ID before the script's local display failed on a Windows `C:` path; `get -Json` confirmed the stored Backlog card and full description. It was not retried as a creation. CARD-1542 used `-Json` and succeeded. No card file was generated or edited.

Cheapest sequence: combine the two fixture repairs in a commissioned Code task with both card references, retain CARD-1529's 25-case green receipt, and qualify the three affected classes once after both repairs. Use an isolated output and the checkpoint/build-slot gate; retain per-class counts. Class filters are `/*/*/DelegationBriefRecoveryTests/*`, `/*/*/DelegationDispatchRecoveryBoundaryTests/*`, and `/*/*/VerificationRoundDeliveryTests/*`. Any residual budget mismatch gets its exact SQL explanation; any distinct residual root cause gets existing-card lookup before filing. Then target only residual failures, ordinary Review, and complete CARD-1529's incomplete R-3/R-4 gate. Do not rerun an assembly or the selected CARD-0680/CARD-1491 Review rows simply to repeat green proof. RC qualification/native activation remain separately owned.

## Exact failing roster

Names below are literal TRX `testName` values; every row has Failed in both direct base CP-90 and candidate CP-2/CP-3. Signatures preserve exception/assertion text before repetitive `Additional Info`/SQL rosters, with newlines rendered as `/`. The three headings provide the class prefix. `Prior` means exact-name match in CARD-1514's earlier whole boundary-class TRX: `pass` is observed, `—` is not established by that receipt. Counts are cases, not unique methods.

### DelegationBriefRecoveryTests (3)

| Exact test name | Root | Failure signature | Prior |
|---|---|---|---|
| `Interrupted_dispatch_backfills_the_missing_brief_on_resume` | D1 | ShouldAssertException: brief.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | — |
| `Launch_enqueue_refusal_after_the_committed_claim_keeps_the_task_dispatched` | D1 | ShouldAssertException: brief.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | — |
| `Resume_never_duplicates_an_existing_brief` | D1 | ShouldAssertException: if (rows.Count == 1) rows[0].Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | — |

### DelegationDispatchRecoveryBoundaryTests (50)

| Exact test name | Root | Failure signature | Prior |
|---|---|---|---|
| `C1149_C1150_Statement_budgets(repeated-discovery-after-delivery, 6)` | D3 | ShouldAssertException: adapter.Attached / should be / False / but was / True | pass |
| `C1149_C1150_Statement_budgets(running-recovery-existing-brief, 65)` | D3 | ShouldAssertException: total / should be / 65 / but was / 34 | pass |
| `C1149_C1150_Statement_budgets(running-recovery-missing-brief, 61)` | D3 | ShouldAssertException: total / should be / 61 / but was / 40 | — |
| `C1149_C1150_Statement_budgets(watchdog-first-recovery, 61)` | D3 | ShouldAssertException: total / should be / 61 / but was / 40 | — |
| `C1149_Caller_note_has_one_complete_user_prompt(busy)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1149_Caller_note_has_one_complete_user_prompt(crash)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1149_Caller_note_has_one_complete_user_prompt(eligible)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1150_Concurrent_producers_ensure_one_brief` | D2 | ShouldAssertException: prompts.Count / should be / 1 / but was / 0 | pass |
| `C1150_Followup_survives_both_queue_orders(brief-before-ui)` | D2 | ShouldAssertException: adapter.SubmittedBodies.Count / should be / 1 / but was / 0 | pass |
| `C1150_Followup_survives_both_queue_orders(ui-before-missing-backfill)` | D2 | ShouldAssertException: ui.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(absolute-native-existing, received)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(absolute-native-existing, working)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(other-task-pointer, received)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(other-task-pointer, working)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-backslash, received)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-backslash, working)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-forward, received)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(relative-forward, working)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(windows-quoted, received)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Inline_brief_mentioning_spill_paths_is_not_held(windows-quoted, working)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Insert_before_flush_cut_reuses_pending_row` | D2 | ShouldAssertException: prompts.Count / should be / 1 / but was / 0 | pass |
| `C1150_Late_ensure_after_remote_receipt_and_payload_release(clipped)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Late_ensure_after_remote_receipt_and_payload_release(complete)` | D2 | ShouldAssertException: sent.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Pre_input_refusal_keeps_the_resume_failure_path(flush-missing-spill)` | D2 | ShouldAssertException: brief.Status / should be / QueuedMessageStatus.Canceled / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Pre_input_refusal_keeps_the_resume_failure_path(may-have-typed-transport-failure)` | D2 | ShouldAssertException: brief.DeliveryAttempts / should be / 1 / but was / 0 | pass |
| `C1150_Readiness_or_persistence_fault_preserves_custody(cancel-after-insert)` | D2 | ShouldAssertException: (await delivered.TranscriptEntries.CountAsync(t => / t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt)) / should be / 1 / but was / 0 | pass |
| `C1150_Readiness_or_persistence_fault_preserves_custody(save-fault)` | D2 | ShouldAssertException: prompts.Count / should be / 1 / but was / 0 | pass |
| `C1150_Real_producers_race_to_one_brief_and_keep_the_followup` | D2 | ShouldAssertException: brief.Status / should be / QueuedMessageStatus.Sent / but was / QueuedMessageStatus.Pending | pass |
| `C1150_Resumed_backfill_event_save_failure_keeps_the_working_recipient` | D2 | ShouldAssertException: outcome.Error / should be null but was / "DbUpdateException: C1150 F10 injected resume bookkeeping failure" | pass |
| `C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(flush-after-ensure-input)` | D2 | ShouldAssertException: outcome.Faults / should be / 1 / but was / 0 | pass |
| `C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(flush-input-then-resumed-event-save)` | D2 | ShouldAssertException: outcome.Error / should be null but was / "DbUpdateException: C1150 F10 injected resume bookkeeping failure" | pass |
| `C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(healthy)` | D2 | ShouldAssertException: outcome.Working / should be / True / but was / False | pass |
| `C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(requeued-event-save)` | D2 | ShouldAssertException: outcome.Error / should be null but was / "DbUpdateException: C1150 F10 injected resume bookkeeping failure" | pass |
| `C1150_Resumed_launch_post_input_bookkeeping_flips_one_step(resumed-event-save)` | D2 | ShouldAssertException: outcome.Error / should be null but was / "DbUpdateException: C1150 F10 injected resume bookkeeping failure" | pass |
| `C1150_Running_cut_is_discovered_and_delivered_once` | D2 | ShouldAssertException: prompts.Count / should be / 1 / but was / 0 | pass |
| `C1150_Watchdog_first_recovers_before_failure` | D2 | ShouldAssertException: prompts.Count / should be / 1 / but was / 0 | pass |
| `C1150_Working_probe_failure_keeps_the_committed_brief` | D2 | ShouldAssertException: prompts.Count / should be / 1 / but was / 0 | pass |
| `C1151_Ceiling_failure_note_has_one_complete_user_prompt(busy)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1151_Ceiling_failure_note_has_one_complete_user_prompt(crash)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1151_Ceiling_failure_note_has_one_complete_user_prompt(eligible)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1153_Only_new_cold_dispatch_prepares_evidence(warm-reuse)` | E1 | ShouldAssertException: await dispatcher.TryReuseWarmAgentAsync(claimed, DateTime.UtcNow, CancellationToken.None) / should be / ReuseOutcome.Reused / but was / ReuseOutcome.SpawnFresh | pass |
| `C1161_Failed_caller_note_has_one_complete_user_prompt(busy)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1161_Failed_caller_note_has_one_complete_user_prompt(crash)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1161_Failed_caller_note_has_one_complete_user_prompt(eligible)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1186_Recovered_failure_preserves_obligation_guidance(busy)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1186_Recovered_failure_preserves_obligation_guidance(historical-decoys)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1186_Recovered_failure_preserves_obligation_guidance(multiple)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1186_Recovered_failure_preserves_obligation_guidance(one)` | D2 | ShouldAssertException: prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)) / should have single item but had / 0 / items and was / [] (System.Linq.Enumerable+ListWhereIterator&#96;1[Antiphon.Server.Domain.Entities.TranscriptEntry]) | pass |
| `C1213_Recovered_brief_keeps_branch_protections_without_git_common(card, feat/card-1213-code-7f3a2b91)` | D2 | ShouldAssertException: prompts.Count / should be / 1 / but was / 0 | pass |
| `C1213_Recovered_brief_keeps_branch_protections_without_git_common(legacy, feat/card-task-7f3a2b91)` | D2 | ShouldAssertException: prompts.Count / should be / 1 / but was / 0 | pass |

### VerificationRoundDeliveryTests (16)

| Exact test name | Root | Failure signature | Prior |
|---|---|---|---|
| `C544_CompletionReceipt` | D4 | ShouldAssertException: TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson) / should not be null but was | — |
| `C544_CompletionReceiptWholeWire` | D4 | ShouldAssertException: rig.Caller.SubmittedBodies.Count / should be greater than or equal to / 1 / but was / 0 | — |
| `C544_CompletionRecovery` | D4 | ShouldAssertException: TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson) / should not be null but was | — |
| `C544_DistillationDeadlineSurvivesRestart` | D4 | ShouldAssertException: rig.Caller.SubmittedBodies.Count / should be / 1 / but was / 0 | — |
| `C544_LandRefusalReceipt` | D4 | ShouldAssertException: carrying.Count / should be / 1 / but was / 0 | — |
| `C544_LandRefusalRecovery` | D4 | ShouldAssertException: carrying.Count / should be / 1 / but was / 0 | — |
| `C544_LateReplacementRejected` | D4 | ShouldAssertException: rig.Fault.Throws / should be / 1 / but was / 0 | — |
| `C544_PointerReceiptRequiresContent` | D4 | NullReferenceException: Object reference not set to an instance of an object. | — |
| `C544_RenderingFrozenBeforeTyping` | D4 | ShouldAssertException: rig.Fault.Throws / should be / 1 / but was / 0 | — |
| `C544_RenderingKeepsHeader` | D4 | ShouldAssertException: TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson) / should not be null but was | — |
| `C544_ReportRegeneratedFromSnapshot` | D4 | ShouldAssertException: TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson) / should not be null but was | — |
| `C544_SameRootBatchMembership` | D4 | ShouldAssertException: a.State / should be / LandNotificationState.Confirmed / but was / LandNotificationState.AwaitingReceipt | — |
| `C544_SingleLogicalNote` | D4 | ShouldAssertException: await rig.CallerPromptsAsync()).Count(p => p.Text!.Contains(header, StringComparison.Ordinal) / should be / 1 / but was / 0 | — |
| `C544_SnapshotRendersRecovery` | D4 | ShouldAssertException: TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson) / should not be null but was | — |
| `C544_StampIsNotReceipt` | D4 | ShouldAssertException: (await rig.NotificationAsync(earlier))!.State / should be / LandNotificationState.Confirmed / but was / LandNotificationState.AwaitingReceipt | — |
| `C544_StoppedCallerReceipt` | D4 | ShouldAssertException: rig.Caller.SubmittedBodies.Count / should be / 1 / but was / 0 | — |

## Receipt provenance retained

These unedited CHECKPOINT lines retain the historical source, filter, counts and build/slot provenance; their red outcome remains red.

```text
CHECKPOINT CP-90 commit=410738a522585e07943cabf23c1243790773d153 build=ok filter=/*/*/(DelegationBriefRecoveryTests*)|(DelegationDispatchRecoveryBoundaryTests*)|(VerificationRoundDeliveryTests*)/(C1149_C1150_Statement_budgets*)|(C1149_Caller_note_has_one_complete_user_prompt*)|(C1150_Concurrent_producers_ensure_one_brief*)|(C1150_Followup_survives_both_queue_orders*)|(C1150_Inline_brief_mentioning_spill_paths_is_not_held*)|(C1150_Insert_before_flush_cut_reuses_pending_row*)|(C1150_Late_ensure_after_remote_receipt_and_payload_release*)|(C1150_Pre_input_refusal_keeps_the_resume_failure_path*)|(C1150_Readiness_or_persistence_fault_preserves_custody*)|(C1150_Real_producers_race_to_one_brief_and_keep_the_followup*)|(C1150_Resumed_backfill_event_save_failure_keeps_the_working_recipient*)|(C1150_Resumed_launch_post_input_bookkeeping_flips_one_step*)|(C1150_Running_cut_is_discovered_and_delivered_once*)|(C1150_Watchdog_first_recovers_before_failure*)|(C1150_Working_probe_failure_keeps_the_committed_brief*)|(C1151_Ceiling_failure_note_has_one_complete_user_prompt*)|(C1153_Only_new_cold_dispatch_prepares_evidence*)|(C1161_Failed_caller_note_has_one_complete_user_prompt*)|(C1186_Recovered_failure_preserves_obligation_guidance*)|(C1213_Recovered_brief_keeps_branch_protections_without_git_common*)|(C544_BriefHandoffRecovery*)|(C544_CompletionReceipt*)|(C544_CompletionReceiptWholeWire*)|(C544_CompletionRecovery*)|(C544_DistillationDeadlineSurvivesRestart*)|(C544_LandRefusalReceipt*)|(C544_LandRefusalRecovery*)|(C544_LateReplacementRejected*)|(C544_PointerReceiptRequiresContent*)|(C544_RenderingFrozenBeforeTyping*)|(C544_RenderingKeepsHeader*)|(C544_ReportRegeneratedFromSnapshot*)|(C544_SameRootBatchMembership*)|(C544_SingleLogicalNote*)|(C544_SnapshotRendersRecovery*)|(C544_StampIsNotReceipt*)|(C544_StoppedCallerReceipt*)|(Interrupted_dispatch_backfills_the_missing_brief_on_resume*)|(Launch_enqueue_refusal_after_the_committed_claim_keeps_the_task_dispatched*)|(Resume_never_duplicates_an_existing_brief*) executed=123 passed=53 failed=70 skipped=0 trx=/tmp/antiphon-c1529-base-684b15a2/.antiphon/checkpoints/20261023-074222-673a/rows/CP-90/run.trx slot=granted waited=0s dirty=0 source=410738a522585e07943cabf23c1243790773d153 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=1a81be39eadaca3f78b811533f786e72350abd0d build=reused filter=/*/*/VerificationRoundDeliveryTests/* executed=20 passed=3 failed=17 skipped=0 trx=/work/worktrees/task-684b15a2/.antiphon/checkpoints/20261023-072943-d79d/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=1a81be39eadaca3f78b811533f786e72350abd0d sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=1a81be39eadaca3f78b811533f786e72350abd0d build=ok filter=/*/*/(RefocusBriefIdentityTests*)|(AgentTaskReuseEnqueueTests*)|(DelegationBriefRecoveryTests*)|(DelegationDispatchRecoveryBoundaryTests*)/* executed=415 passed=362 failed=53 skipped=0 trx=/work/worktrees/task-684b15a2/.antiphon/checkpoints/20261023-072943-d79d/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=1a81be39eadaca3f78b811533f786e72350abd0d sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=9e4d2f95b9d70f3a5cbc1571f602884e8a2a019b build=ok filter=/*/*/(RefocusBriefIdentityTests*)|(VerificationRoundDeliveryTests*)/(C1529_*)|(C544_BriefHandoffRecovery*) executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/task-684b15a2/.antiphon/checkpoints/20261023-074222-af6a/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=9e4d2f95b9d70f3a5cbc1571f602884e8a2a019b sourceState=clean buildSource=verified
```

Raw TRX SHA-256 checks at diagnosis time:

| Receipt | SHA-256 |
|---|---|
| Direct base CP-90 | `1589cca46e6d2b34c84acb4dca4117c71199c9f2e1cf0da9afa17e51b1873a8b` |
| Candidate CP-2 | `1203c97413b4f1cc1bcba0f191639e13c66c3240740cfb8754e4009aa37d6a74` |
| Candidate CP-3 | `669b54f28c085726b47f7f509a99907187350ccbe793270975883fb25feff6f6` |

--- next stage ---
next: land
handoff: Land this diagnosis only. Commission the CARD-1541/CARD-1542 fixture repairs together and retain CARD-1529's incomplete R-3/R-4 gate; exact-RC execution status needs the archived qualification roster, not an inferred green or interruption of the live qualification.
artifact: docs/investigations/2026-10-23-master-69-reds-after-0680-1491-debug.md
