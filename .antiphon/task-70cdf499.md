# CARD-1029 S2/S3 Code continuation — 70cdf499

Incomplete. Seven gap methods and an owned recipient/recreated-service fixture are authored and pushed, but ordinary verification remains red. This is not a Final certificate and is not ready for Review. No production source, timeout, ceiling or authentication policy was changed.

## Ownership and sources

- Code subject: 70cdf499-a714-4c69-bf1e-06a6748b33a1.
- Original Code task and eventual landing owner: bed39f89-24ec-4ef4-a70e-f8af615ae5b2. Caller adopts this continuation into that owner after a successful Review.
- Branch: feat/card-task-70cdf499; worktree: /work/worktrees/task-70cdf499.
- Task base: a50794508bb48683402b402b0468c9852af20a13. Original owner base: e22dd059a46413bd14dd410836231c26fb2fb91b.
- Authored/pushed slices: b21244efc664b24b7f05aa13f3089a457e36024c (initial implementation, compilation red); 7567247f4d6f32cfd55aeada35991e0924919186 (compile repair and receipt witnesses); fbd5f98b9953b8b16d8bc88191866a2889f3bd19 (capabilities/oracle/crash seam repairs); 1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 (third repair, prior-generation/Grok fault cases/recipient semantics).
- Final source implementation SHA: 1265c9b51371e38cbe6fc7fa9a0bbea6a0147812. The final task message/progress marker gives the later pushed report SHA. Receipts below retain their actual tested SHA.
- Plan: /work/worktrees/task-70cdf499/docs/superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md.
- Control ledger: /work/worktrees/task-70cdf499/docs/superpowers/plans/2026-10-04-card-1029-control-qualification.md.
- Predecessor evidence: /work/worktrees/task-70cdf499/.antiphon/task-bed39f89.md.
- Restart: none. Any future activation is caller-owned; these changes are tests/docs only.

GET /api/runner-defaults and GET /api/session-runners were read before verification. Defaults revision 2 selected Linux; no runner or platform pin was used. ANTIPHON_TASK_TOKEN was present. No delegate/sub-agent or live provider was launched.

The explicit brief limits this continuation to Linux CP-4..8, no whole Unit, at most three repair rounds and 90 minutes total. It overrides the generic Final template. No full-assembly test run occurred. CP-4 passed twice at its recorded SHAs, but was not rerun after the final fixture edits; that final-source consumer check remains pending. The last round targets all four red rows, not a reassurance repeat.

## Implementation

- CodexCliDeliveryWorld.cs separates persistent isolated schema, scratch repositories/workspace and actual scripted terminal/transcript from disposable PhoneHome host, peer, DI graph, courier and adapters. It uses real Create Worktree, dispatcher, preparer, launch queue, provider adapter, delivery queue, framed writer and transcript pull. Fault interceptors name exact task/queue identities. Retry uses the real watchdog and RetryAsync.
- CodexCliRemoteDeliveryFixture.cs preserves existing consumers and adds kind-specific recipient behavior, real rules-file receipt/ACK, retained transcripts, release-slot semantics, Claude visible startup/composer clear behavior and claim-time immutable settings/ceiling snapshots. Expected E and W are not read back from the queue, file or transcript.
- CodexCliObservationGapTests.cs contains all seven named methods: eight ordinary kind vectors plus eight Grok queue handoff fault subcases; local/remote pre/post-insert recovery; prior-recipient negative floor; null-baseline screen; four timestamp boundaries; two durable graph-recreation variants; two actual-input/lost-verdict variants. These authored vector counts are not a claim of complete passing coverage. The four Grok post-commit arrangements currently require the recipient survival excluded by the later P-1 override and need correction as ordinary recovery arrangements; they do not reopen the P-1 production repair or enlarge its PC-274-only blocker. A method passes only if its required assertions pass. Per-kind failures are accumulated to expose every case, never retried or suppressed into green.
- The timestamp oracle counts only qualifying source timestamps when the original sequence baseline is null. PostgreSQL stores microseconds, so the representable boundary steps are minus/plus 10 .NET ticks; equality and null remain separate cases. The original attempt start survives recreation.
- The old-generation method now captures a complete actual prompt from a separately launched earlier recipient, then uses that captured text/timestamp as a permitted synthetic stale arrival at the selected attempt's original floor. It does not invent a generation column on TranscriptEntry. Only the held actual current submission supplies the current positive receipt. Earlier green runs of the former floor-only method did not close the prior-generation obligation.
- The post-input storage fault remains active across the boot-watch save (whose exception is caught) and the final verdict save, then storage is restored for graph recreation. Both failing saves, the durable interrupted row and one actual submission are asserted.
- The new Slow class is registered in slow-tests-allowlist.txt. CP-5..8 selection syntax has trailing method wildcards; selected identities/counts are inspected from fresh TRX.

## Ordinary checkpoint evidence

### Run 20261005-190722-a57f

Actual SHA: b21244efc664b24b7f05aa13f3089a457e36024c; terminal phase=stopped, exit=null.

| Row | Outcome | Fresh TRX count |
|---|---|---|
| CP-4 | build-failed | not completed |
| CP-5 | queued | not completed |
| CP-6 | queued | not completed |
| CP-7 | queued | not completed |
| CP-8 | queued | not completed |

Unedited CHECKPOINT lines:

```text

```

### Run 20261005-191321-3f05

Actual SHA: 7567247f4d6f32cfd55aeada35991e0924919186; terminal phase=done, exit=1.

| Row | Outcome | Fresh TRX count |
|---|---|---|
| CP-4 | green | executed=8, passed=8, failed=0, skipped=0 |
| CP-5 | red | executed=1, passed=0, failed=1, skipped=0 |
| CP-6 | red | executed=1, passed=0, failed=1, skipped=0 |
| CP-7 | red | executed=3, passed=1, failed=2, skipped=0 |
| CP-8 | red | executed=2, passed=1, failed=1, skipped=0 |

Unedited CHECKPOINT lines:

```text
CHECKPOINT CP-4 commit=7567247f4d6f32cfd55aeada35991e0924919186 build=ok filter=/*/*/CodexCliObservationTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-191321-3f05/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=7567247f4d6f32cfd55aeada35991e0924919186 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=7567247f4d6f32cfd55aeada35991e0924919186 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Per_kind_receipts* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-191321-3f05/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=7567247f4d6f32cfd55aeada35991e0924919186 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=7567247f4d6f32cfd55aeada35991e0924919186 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Enqueue_fault_and_retry_keep_identity* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-191321-3f05/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=7567247f4d6f32cfd55aeada35991e0924919186 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=7567247f4d6f32cfd55aeada35991e0924919186 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Old_generation_does_not_confirm*)|(C1029_Unobservable_screen_retains_spill*)|(C1029_Unobservable_timestamp_floor_is_original*) executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-191321-3f05/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=7567247f4d6f32cfd55aeada35991e0924919186 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=7567247f4d6f32cfd55aeada35991e0924919186 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Durable_spill_survives_recreated_graph*)|(C1029_Post_input_crash_late_confirms_once*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-191321-3f05/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=7567247f4d6f32cfd55aeada35991e0924919186 sourceState=clean buildSource=verified
```

Fresh CP-4 method roster:

- C959_Ladder_and_exact_models_share_floor: Passed.
- C959_Launcher_descriptors_are_observations: Passed.
- C959_Compatible_launch_keeps_model_and_runner: Passed.
- C959_Retry_and_cancellation_recheck: Passed.
- C959_Probe_failure_never_refuses: Passed.
- C959_Stale_sample_never_refuses: Passed.
- C959_Unknown_version_never_refuses: Passed.
- C959_Old_version_with_floor_never_refuses: Passed.

Fresh CP-5 method roster:

- C1029_Per_kind_receipts: Failed.

Fresh CP-6 method roster:

- C1029_Enqueue_fault_and_retry_keep_identity: Failed.

Fresh CP-7 method roster:

- C1029_Old_generation_does_not_confirm: Passed.
- C1029_Unobservable_screen_retains_spill: Failed.
- C1029_Unobservable_timestamp_floor_is_original: Failed.

Fresh CP-8 method roster:

- C1029_Durable_spill_survives_recreated_graph: Passed.
- C1029_Post_input_crash_late_confirms_once: Failed.

### Run 20261005-193716-edb8

Actual SHA: fbd5f98b9953b8b16d8bc88191866a2889f3bd19; terminal phase=done, exit=1.

| Row | Outcome | Fresh TRX count |
|---|---|---|
| CP-4 | green | executed=8, passed=8, failed=0, skipped=0 |
| CP-5 | red | executed=1, passed=0, failed=1, skipped=0 |
| CP-6 | red | executed=1, passed=0, failed=1, skipped=0 |
| CP-7 | red | executed=3, passed=2, failed=1, skipped=0 |
| CP-8 | red | executed=2, passed=1, failed=1, skipped=0 |

Unedited CHECKPOINT lines:

```text
CHECKPOINT CP-4 commit=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 build=ok filter=/*/*/CodexCliObservationTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-193716-edb8/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Per_kind_receipts* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-193716-edb8/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Enqueue_fault_and_retry_keep_identity* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-193716-edb8/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Old_generation_does_not_confirm*)|(C1029_Unobservable_screen_retains_spill*)|(C1029_Unobservable_timestamp_floor_is_original*) executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-193716-edb8/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Durable_spill_survives_recreated_graph*)|(C1029_Post_input_crash_late_confirms_once*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-193716-edb8/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=fbd5f98b9953b8b16d8bc88191866a2889f3bd19 sourceState=clean buildSource=verified
```

Fresh CP-4 method roster:

- C959_Ladder_and_exact_models_share_floor: Passed.
- C959_Launcher_descriptors_are_observations: Passed.
- C959_Compatible_launch_keeps_model_and_runner: Passed.
- C959_Retry_and_cancellation_recheck: Passed.
- C959_Probe_failure_never_refuses: Passed.
- C959_Stale_sample_never_refuses: Passed.
- C959_Unknown_version_never_refuses: Passed.
- C959_Old_version_with_floor_never_refuses: Passed.

Fresh CP-5 method roster:

- C1029_Per_kind_receipts: Failed.

Fresh CP-6 method roster:

- C1029_Enqueue_fault_and_retry_keep_identity: Failed.

Fresh CP-7 method roster:

- C1029_Old_generation_does_not_confirm: Passed.
- C1029_Unobservable_screen_retains_spill: Failed.
- C1029_Unobservable_timestamp_floor_is_original: Passed.

Fresh CP-8 method roster:

- C1029_Durable_spill_survives_recreated_graph: Passed.
- C1029_Post_input_crash_late_confirms_once: Failed.

### Run 20261005-200558-1f73

Actual SHA: 1265c9b51371e38cbe6fc7fa9a0bbea6a0147812; terminal phase=done, exit=1.

| Row | Outcome | Fresh TRX count |
|---|---|---|
| CP-5 | red | executed=1, passed=0, failed=1, skipped=0 |
| CP-6 | green | executed=1, passed=1, failed=0, skipped=0 |
| CP-7 | red | executed=3, passed=2, failed=1, skipped=0 |
| CP-8 | red | executed=2, passed=1, failed=1, skipped=0 |

Unedited CHECKPOINT lines:

```text
CHECKPOINT CP-5 commit=1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Per_kind_receipts* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-200558-1f73/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 build=ok filter=/*/*/CodexCliObservationGapTests/C1029_Enqueue_fault_and_retry_keep_identity* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-200558-1f73/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Old_generation_does_not_confirm*)|(C1029_Unobservable_screen_retains_spill*)|(C1029_Unobservable_timestamp_floor_is_original*) executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-200558-1f73/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 build=ok filter=/*/*/CodexCliObservationGapTests/(C1029_Durable_spill_survives_recreated_graph*)|(C1029_Post_input_crash_late_confirms_once*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-70cdf499/.antiphon/checkpoints/20261005-200558-1f73/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 sourceState=clean buildSource=verified
```

Fresh CP-5 method roster:

- C1029_Per_kind_receipts: Failed.

Fresh CP-6 method roster:

- C1029_Enqueue_fault_and_retry_keep_identity: Passed.

Fresh CP-7 method roster:

- C1029_Old_generation_does_not_confirm: Passed.
- C1029_Unobservable_screen_retains_spill: Failed.
- C1029_Unobservable_timestamp_floor_is_original: Passed.

Fresh CP-8 method roster:

- C1029_Durable_spill_survives_recreated_graph: Passed.
- C1029_Post_input_crash_late_confirms_once: Failed.

The initial run 20261005-190722-a57f at b21244efc664b24b7f05aa13f3089a457e36024c hit two introduced compile errors (shadowed db local; nullable Guid assertion overload). No test ran. CP-5's duplicate build had already started when the tool stop was requested; the stop/wait completed with exit 6 (executor stopped without phase=done). The compile errors were repaired before the next committed run. This is not a test/control red. An abbreviated expected-SHA preflight was refused before any build and corrected to the full SHA.

Earlier failures remain recorded: missing fixture local Grok capabilities; remote Retry held at capacity because the recipient omitted ReleaseSlot; empty Claude raw startup output; timestamp helper counted a deliberately stale record; one-shot verdict fault was caught before a later save succeeded. These were fixture/oracle faults, not evidence of a new production regression. No assertion or timeout was relaxed to pass them.

## Base screen-release reproduction

The full new method C1029_Unobservable_screen_retains_spill also failed against production dependencies built from a clean detached checkout of a50794508bb48683402b402b0468c9852af20a13: 1 executed, 0 passed, 1 failed, 0 skipped. The precise failure is the final RemoteSpillBody-null assertion after a complete current transcript receipt. Earlier assertions establish the null original baseline, real Delivered/Screen fallback, DeliveryUnverified incident, E retention before receipt, correct whole actual W and one submission.

The base lacks the new method, so the diagnostic borrowed only Antiphon.Tests.dll/PDB from the clean fbd5f98b9953b8b16d8bc88191866a2889f3bd19 CP-7 build. All 18 production/support Antiphon dependency DLL hashes were recorded from the base build and verified unchanged after execution. Witness DLL SHA256: DDBC4B52395FD7952E4820411D00DE90B8075A04179E51623080956B25692198. This is explicitly a composite baseline diagnostic, not a clean Final checkpoint certificate for the base or current HEAD.

Evidence: /work/worktrees/task-70cdf499/.antiphon/c1029-base-screen/{build.log,build-repaired.log,run.log,run.trx,provenance.json,owned-outputs.json}. The first base build was an argument failure: PowerShell split redundant -nodeReuse:false; 0 tests. The repaired base build had 0 errors (589 existing warnings), 244.28s; exact-method test took 49.832s, test driver exit 2. Both builds and the test held slot=granted waited=0s; leases were released. The detached worktree and its inventoried bin-c1029-base outputs were removed after the run. The raw evidence remains ignored in this task's root.

Production server tree b7b0731106dff9fdb6d6a928284af85ba404b921 and src tree fc47f494e3645e9cef2a3249ddbf379ace9bcb97 are identical at the task base and final implementation. D-1 calls for a separately scoped production repair; none was made here. The ordinary screen-release defect is not attributed to the new fixture without the baseline run above.

## Required outcomes and pending work

- **CP-5 at final implementation SHA: 1 executed, 0 passed, 1 failed, 0 skipped.** All 16 subcases ran: normal Grok local/remote x eligible/busy (4) PASS; Claude long/spilled eligible/busy (2) PASS; Grok pre-insert local/remote x eligible/busy (4) PASS. Both Claude requested-inline cases delivered a complete actual receipt but failed the required no-spill assertion: frozen E is 2,561 UTF-8 bytes and the unchanged inline ceiling is 900. They need a faithful inline arrangement that does not widen the ceiling. Four Grok post-commit cases reached their durable queue, then failed retained-recipient survival (recipientKilled=True). Their current crash/reattach arrangement crosses the explicitly excluded P-1 seam; correct that ordinary test arrangement without treating CARD-1038 as a new Code blocker. No P-1 production repair is requested by this report.
- **CP-6 at final implementation SHA: 1 executed, 1 passed, 0 failed, 0 skipped.** All four local/remote pre/post-insert cases passed. Actual watchdog/Retry retains task/runner/Worktree identity and increments Attempt only for the pre-insert failure; post-insert recovery retains the durable original queue and one complete receipt.
- **CP-7 at final implementation SHA: 3 executed, 2 passed, 1 failed, 0 skipped.** The actual prior-generation witness passed both local and remote variants. All four original-timestamp boundaries (older/null/equal/newer) passed. Screen fallback passed retention and whole actual receipt assertions but failed final durable E release; the same failure reproduced with base production dependencies, as documented above.
- **CP-8 at final implementation SHA: 2 executed, 1 passed, 1 failed, 0 skipped.** Durable spill recovery passed both eligible/busy graph-recreation variants. Post-input crash recovery still fails at the expected FlushAsync InvalidOperationException, which was not thrown, even with the persistent storage-fault arrangement. It stops in the first variant; the second remains unexecuted. This is unresolved fault-fixture evidence, not a proved production regression. The original verdict-save/crash invariant remains unverified.
- **CP-4 final-source status: PENDING.** Its full eight-method class passed twice at the recorded earlier source SHAs. Changes to the fixture in the third repair require another full class run; the 90-minute/three-repair budget prevents claiming an unrun current-source certificate.
- **Static authoring limitation:** GrokFaultAsync's pre-insert no-Working assertion currently reads the frozen pre-launch task object. It should reload the task after the injected fault before guarding that state. The actual Retry and recipient assertions still ran, but this particular assertion is not independent post-fault evidence.
- **Source/receipt limitation:** the seven methods are authored, not all qualified. The final source has not passed the complete assigned CP-4..8 selection. Do not send this continuation to Review until the red cases and final-source consumer run are resolved.

After the next committed repair, rerun the affected red CP rows plus CP-4 when the shared fixture changes, through the tool with the full committed HEAD as expected SHA. The exact command shape is `run --plan docs/superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md --rows CP-4,CP-5,CP-7,CP-8 --serial --expected-source-sha <full-HEAD>`, adding CP-6 only for changes affecting their arrangement or an unresolved red result. Bootstrap the checkpoint executable through build-slot.ps1 if its task-owned output has been cleaned. Call `wait --run <id>` until terminal and inspect every intended method in fresh TRX; no automatic repeat is needed after green.


V-13 and V-19: predecessor S1 PASS at CP-3/4; current CP-4 repeated its existing witnesses. V-14: current CP-4 PASS at its recorded SHA, final-fixture rerun pending. V-21 and V-22: INCOMPLETE/RED; see current gap rows and case details above. V-23, V-24, V-25, V-26: existing CP-4 witnesses PASS at the recorded SHA; the 114 remote matrix vectors remain deferred. V-27: predecessor S1 PASS for claim/reuse/queue only; no receipt or production-lineage claim.

R-1, R-2, R-6, R-7 retain predecessor S1 results at their actual SHAs (CP-15 3/3 at 241adc856d9b63291567b8bbf91fe66e77a0a9d4; CP-3 1/1, CP-16 1/1 and CP-17 1/1 at 491c2efb7301d111029bd8aeddff32feb8441a1c). They were not rerun or relabelled in this continuation. R-3 passed both full CP-4 runs; final-fixture CP-4 is pending. R-4 and other V-1..12 native/wire obligations remain in the named follow-ups. No manual/native acceptance beyond the described source/fixture/evidence inspections is claimed.

Deferred ordinary IDs remain unchanged and are NOT PASSED:

- F-Observation: CP-1 and the other five CP-3 methods.
- F-Faults: CP-9/10, all 44 fault vectors.
- F-Matrix: CP-11..14, exactly 38 real Worktree Retry + 38 explicitly seeded warm + 38 seeded recreated-warm = 114 vectors. Seeded pool success is not admission or historical producer proof.
- F-Regression: the remaining full CP-15/16 class methods.
- F-Native (caller-owned Windows): CP-2 at exact L0959 6a88d8ceaedb5934bb3a466802a622a4b56e5b37, 29-result qualification; CP-18 current-source 30-result roster plus additions. Portable runs discharge neither.

Every one of the 211 primary controls remains pending method-scoped post-land SourceLanding Mutation (Mutation paused): PC-1..14,16..84,89..109,111..120,122..127,153..159,184..188,190..192,194,199..203,205..255 (original 192); PC-256; PC-257..274. All 21 remote-only variants also remain pending: PC-225/228/231/234 Create (4); PC-226/229/232/235 Retry (4); PC-227/230/233/236 cold and seeded warm (8); PC-237/238/239/240/253 (5). PC-274 retains its P-1/CARD-1038 blocker. No deliberate mutant or red/restore/green PC cycle ran. No ordinary result qualifies a PC.

## Run discipline and closure

Only the plan tool bootstrap and the explicitly explained exact-base diagnostic were outside checkpoint rows. Bootstrap used build-slot.ps1 and bin-c1029-tool/: 3.09s, 0 errors, existing CS8602 at TaskOwnerGuard.cs:170; slot=granted waited=15s, held=4s. Every checkpoint build/test used its own gate lease and printed slot/wait fields. Owner-read transient 502/timeouts were left to the tool's guard; no unleased bypass ran. No whole Unit, full assembly, Windows, loaded repetition or provider-spend test ran. Each committed selection ran once; changed/red rows were repaired/reselected within the three-repair budget.

Run 20261005-200558-1f73 finished phase=done, exit=1; wait joined and removed the dead executor shadow copy, and PID 20253 was independently confirmed gone. No command/test remains in flight. The checkpoint clean verb removed 130 manifest-owned output directories; the sole remaining bin-c1029-tool bootstrap directory was inventoried and removed. Base diagnostic cleanup had already removed its 26 output directories and detached worktree.

Current CP-6 tool validation returned `CHECKPOINT SOURCE VALID source=1265c9b51371e38cbe6fc7fa9a0bbea6a0147812 rows=1`. All four current rows, including the red ones, independently passed matching full SHA, zero dirty files, equal start/end fingerprint and verified-build-source receipt checks. Earlier full CP-4 receipts were tool-validated at their recorded SHAs. This validates provenance, not red test outcomes.

Total task duration was about 88 minutes across exactly three repair rounds. The last checkpoint took 14m57s. No further repair/repetition was attempted after the permitted third round.

Generated TRX, logs, JSON receipts, archives and checkpoint payloads remain ignored. This report and a plan status amendment are the only committed evidence summaries. The evidence-history guard is run over full task base..final report HEAD and original owner base..HEAD before settlement; the final task message records its terminal result. The final pushed SHA is verified with git ls-remote origin refs/heads/feat/card-task-70cdf499.

--- next stage ---
next: code
handoff: Finish remaining red S2/S3 cases and final-source CP-4; preserve base screen-release reproduction and the unchanged inline ceiling. Keep P-1 repair outside this slice and all controls pending Mutation. Review this continuation only after ordinary verification is complete; original landing owner remains bed39f89.
artifact: docs/superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md
