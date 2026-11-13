# CARD-1892 round 3 Code evidence

Fixed the stale delivery-unknown attention notice after late confirmation, linked recent incidents to their task, and added the Codex interrupted-Sent recovery case. Ordinary round-3 execution passed 619/619 with zero failed/skipped. Red-first proof had nine intended assertion failures. No activation occurred. The standard merge-tree compatibility gate is conflicted by the rebased parent history; the commissioned task delta applies cleanly to master. This report does not authorize landing.

Code task: 4e02b770-30f0-42bf-9d9e-fdca4ce88c86. Original Code/landing owner: 13c4448d-6092-41e2-8ea0-4657c3b70bee.
Branch: feat/card-task-4e02b770. Worktree: /work/worktrees/task-4e02b770.
Assigned task base: 312a3961d2e2e00bd877571cd7bc2bcb0342c842.
Actual green tested SHA: 3574c40f7ae4cb625926b2b0ac797eba1ebd7798.
Plan: /work/worktrees/task-4e02b770/docs/superpowers/plans/2026-11-13-card-1892-round3.md.
Evidence: /work/worktrees/task-4e02b770/docs/superpowers/plans/2026-11-13-card-1892-round3-evidence.md.
Parent plan/evidence: 2026-11-13-card-1892-input-uncertainty.md and 2026-11-13-card-1892-code-evidence.md in this directory. Review caeec914 D-1 / CARD-1905 supplied the finding.

## Changes and protected invariant

AttentionService.BuildRecentIncidentItemsAsync suppresses only DeliveryTransportFailed/input-delivery-unknown incidents whose episode no longer has a charged Sent/null-verdict non-maintenance row. The attempt start must precede the incident, so a newer episode cannot revive an older note. Incident history stays append-only. Another unresolved attempt keeps the notice; unrelated errors of the same kind remain visible.

This projection-only correction is smaller than writing incidents from all late-confirm transactions. It also handles already-confirmed historical rows when the updated server starts. Queue state transitions, recovery, Enter/retype refusal, process ownership, production timeouts and CARD-0079 are unchanged. RecentCriticalIncident.TaskId now selects the latest task attached to that session at incident time, excluding tasks created afterward and null-session matches.

The existing eight-case queue witness calls real AttentionService.GetAsync before and after a matching UserPrompt and checks LateConfirmed, unchanged attempt count, no input writes and retained incident history. A second real-reader witness covers two unknown rows and an unrelated error. A random-task-ID reader test proves temporal task attribution. The Codex pointer test adds interrupted-sent-complete: an aged Sent/null-verdict row with a complete composer must still recover through Enter-only.

## Ordinary results

All current rows used one isolated producing build, serial execution, committed expected SHA 3574c40f7ae4cb625926b2b0ac797eba1ebd7798, dirty=0, sourceState=clean and buildSource=verified. The manifest has a second unused red-build entry; it is not a second executed build.

| Row | Executed/passed | Failed/skipped | Slot | Waited seconds |
|---|---:|---:|---|---:|
| CP-2 | 104/104 | 0/0 | granted | 0 |
| CP-3 | 106/106 | 0/0 | granted | 15 |
| CP-4 | 5/5 | 0/0 | granted | 60 |
| CP-5 | 79/79 | 0/0 | granted | 0 |
| CP-6 | 5/5 | 0/0 | granted | 30 |
| CP-8 | 292/292 | 0/0 | granted | 60 |
| CP-9 | 28/28 | 0/0 | granted | 45 |

Fresh TRX TestDefinitions/TestMethod class identities and result counts were inspected for every intended class, with no missing identity or zero count. CP-2: RunnerTerminalSessionGenerationTests 8; RunnerTerminalSessionInputEncodingTests 5; AgentSessionCancelledTurnTests 20; AgentSessionInterruptedLaunchResumeTests 9; GrokRulesFailureTests 7; GrokRulesInitializationTests 24; PhoneHomeLaunchTransportTests 7; SessionMessageQueueInterruptedAttemptTests 24. All eight C1892_Composerless variants and C1892_Notice_keeps_remaining_unknown_attempt_and_unrelated_error passed.

CP-3: MechanicalPcContractTests 11; TaskPlatformGuidanceTests 5; C1729LayeringGuardTests 9; SlowTestRegistryMergeTests 5; DerivedImpactIndexTests 27; SlowTestTripwireTests 2; TestClassificationPolicyTests 42; TestLaneCategoryGuardTests 1; NativeLinuxPolicyWitnessTests 3; TestClassificationGuardTests 1.

CP-4: GrokRulesReadyOrderingTests 5. CP-5: AgentSessionLaunchFailureTests 79. CP-6: SessionMessageQueuePhoneHomeDropTests 5. CP-8: AttentionFeedNoiseTests 14; AttentionServiceTests 168; ChannelInboundRecoveryTests 49; ChannelInputWithheldIncidentTests 31; StandingBootAttentionTests 30. recent_incident_links_the_task_in_its_session_at_incident_time passed. CP-9: SessionMessageQueueCodexPointerTests 28; C1521_Recovery_reconciles_before_input(interrupted-sent-complete) passed.

| ID | Actual outcome |
|---|---|
| V-1 | Unchanged runner admission invariant; parent Review caeec914 CP-1 passed 157/157 at assigned base 312a3961d2e2e00bd877571cd7bc2bcb0342c842. Not rerun or relabeled as this tip. |
| R-1 | Same retained parent CP-1 proof for phase-B independence, retention protection and serialization; no runner/retention source changed. |
| V-2 | Passed current CP-2/4/5/6/8: unknown delivery retained, late receipt resolves without writes, real attention clears, partial resolution retains attention, unrelated errors survive, task link is correct. |
| R-2 | Passed current CP-2/4/5/6/8/9: genuine launch cleanup, cancellation, generation, encoding, Claude recovery and Codex complete-composer recovery. |
| V-3 | Unchanged timing invariant; retained parent Review CP-1 at its actual base SHA. No new runner timing execution claimed. |
| R-3 | Passed CP-3, including the matched test-lane-category glob and full required guard union; index regeneration changed then remained unchanged after the assertion-call repair. |
| V-4 | Fresh TRX census and exact-source validator passed; evidence-diff and OS inventory passed. Standard merge-tree exited 1 for rebased-parent conflicts; separate task-delta application check exited 0. Compatibility is not represented as a conflict-free merge. |

Manual inspection confirmed unchanged LF normalization/bracketed paste/separate delayed Enter in RunnerTerminalSession.SendLineAsync and unchanged generation fences in late confirmation. No changed OS-selected production branch/factory/property or startup dependency exists. check-os-witnesses exited 0 after correcting the ignored declaration to inventory tests/derived-impact-index.json; its earlier missing-inventory exit 1 is not runtime proof. The lint proves syntax/inventory only. GET /api/runner-defaults and /api/session-runners were read; no placement flags or fleet location were embedded.

No migration, model or downgrade path changed: migration red/green counts 0/0, not applicable. Native Windows and live-session acceptance remain pending with the caller under the parent plan. Restart: server to activate the projection; performed: none. No host, production session or live qualification was restarted or stopped.

## Scope and cost

This is Final for the commissioned closed/touched selection: parent server rows CP-2/3/4/5/6 plus named attention and Codex rows. CP-1 remains unchanged parent proof. No ordinary IDs are deferred to a later Final. The full Unit lane remains nightly/rc; it omits delivery, landing, leases and persistence proof and was not substituted here.

The full task-range impacted selector matched only test-lane-category, already run in CP-3; windowsFilters was empty. Its wider source-derived closure is 38 classes with 21 capped owed classes for AttentionService. Those extra classes are not claimed passed. Closed rows, named integration readers and matched globs bound the commissioned invariant; no unbounded shared change or full-assembly run was introduced. Full selector output remains ignored at .antiphon/c1892-r3-impact-final.json.

Green run 20261113-221635-1d82: wall 1810.377 s (30m10s), charged execution 1510.277 s, admission-only union 300.100 s. Build waited 90 s; MSBuild 239.30 s versus checkpoint build phase 345.8957147 s. Finalization was 147.191 s. All activities were serial; maxConcurrentRows=1 and maxConcurrentBuilds=1. Nested class-duration sums are not wall time or p95. TRX per-test durations and row phase journals remain in the run directory; unavailable individual external-call/fixture phases and host CPU/memory samples remain unknown.

The conservative 37-minute budget triggered an extension of the existing performance Debug request before execution; actual wall also exceeded 30 minutes. Caller commissioning is still owed at docs/investigations/2026-11-13-card-1892-round2-performance-debug-request.md. No duplicate card or extra profiling repeat was created. This delegate cannot sub-delegate. No timeout or assertion condition was widened or loosened.

Unlisted checkpoint activities: none. Direct prerequisites were explicitly required and all gated: derived-index driver build first (waited=300s, granted lease ed84cddc-4a12-4a68-bd81-262d47805c9f); checkpoint-tool native build (waited=90s, lease 440423fa-1753-4599-a437-885fe207936a); index regeneration -NoBuild (waited=15s, lease 82928bc8-a07a-4a52-b711-92a27116bd79, changed); index recheck -NoBuild (waited=90s, lease eeecfaac-c7e0-4acf-ae82-5236571d94c7, unchanged). Grants applied maxcpucount=6 to builds. The script-wrapper cap warnings on the two -NoBuild runs did not conceal an inner build. Direct builds used native default output with UseAppHost=false. No tests ran outside checkpoints.

## Historical failures and publication

The initial manifest invocation at 17008fe90f2357161933dfbfaad456a3f33d7cdb refused the symbolic CP-R3 build reference before creating a run or executing a build/test. Numeric CP-90/91 references were committed before retrying.

Red-first run 20261113-214537-0f17 at 2ee1199f9ab24effebde9442ccf99396438e6fef: CP-90 executed 8, all failed specifically at late-confirm-clears-notice after real UserPrompt/LateConfirmed; CP-91 executed 1, failed at incident-task-link because TaskId was null. No fixture/build error or skipped case counted as red proof. Source was clean and build provenance verified. Report remains RED, unchanged.

First green attempt 20261113-220642-9eac at 8ed9accb951d47e3beb3bdbada84d7a66e77ebc6: compilation failed with three introduced CS1503 errors from positional Shouldly messages in the new safeguard test. No test executed. All seven row counts are unavailable, not zero passes. The repair uses the supported named customMessage parameter without changing assertion conditions and uses a fresh output directory. That failed report remains unchanged, buildSource=unknown.

The red cases were rerun inside the green whole-class selections (CP-90 method in CP-2; CP-91 method in CP-8), with each exact method/variant inspected. No green selection was repeated after success. Deliberate mutants were not run by Code.

All source slices were committed and pushed independently:

- 17008fe90f2357161933dfbfaad456a3f33d7cdb — failing reader tests, Codex case, round-3 plan.
- 2ee1199f9ab24effebde9442ccf99396438e6fef — supported numeric checkpoint references.
- f7264abd26d9d49953c7cc996c0f6ec88fd12c67 — projection fix and partial-resolution safeguard.
- 8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 — regenerated index as its own commit.
- 3574c40f7ae4cb625926b2b0ac797eba1ebd7798 — compile-only assertion-call repair; actual green tested source.

The evidence-only publication commit is reported by its full SHA in the task's final report. No merge, rebase, reset, amend or force push was performed.

git fetch origin master resolved target 1fde603cce208ff8a9147715101b3976c737edd0. Standard git merge-tree --write-tree origin/master HEAD exited 1 (provisional tree c1782610e83315d65fa96c147695dd14d646af91), with conflicts in the existing performance request, mutation candidate, interrupted-attempt test and derived index. The merge base is e4c45f4b4bb58870bb8684087a6d218e1b988cbe: master contains the assigned round-2 content through rebased commits. The assigned base and master have identical interrupted-attempt source, attention source and those two narrative files. An isolated GIT_INDEX_FILE index loaded from master accepted git diff --binary 312a3961d2e2e00bd877571cd7bc2bcb0342c842 HEAD through git apply --cached --check, exit 0, including the index delta. This is task-delta applicability, not a passing standard merge-tree gate. The landing owner must preserve the rebased parent and use only this task's range; this report is not landable approval.

## Pending Mutation and external obligations

Unbound candidate: /work/worktrees/task-4e02b770/docs/superpowers/mutations/2026-11-13-card-1892-input-uncertainty-candidate.md. Every PC and variant is pending caller-commissioned method-scoped SourceLanding Mutation; none passed here:

- PC-1: raw and conditional global-lock admission.
- PC-2: Grok timeout/cancel/disconnect; card timeout/cancel/transport cleanup.
- PC-3: Enter/body-to-Enter/re-Enter attempt reversion; expired rules deadline.
- PC-4: wait, hold and quiet-threshold timing.
- PC-5: external caller cancellation propagation.
- PC-6: uncertain resend, fresh Enter, aged retype; ordinary/rules, visible/absent head.
- PC-7: missing/repeated attention, lost late discovery, Error-to-Warning downgrade.
- PC-8: omitted resolution suppression, premature partial resolution, unrelated-error suppression.
- PC-9: omitted or later-task-misattributed TaskId.
- PC-10: wrongly admitting Codex into the composer-less hold.

Mutation owns deliberate red/restore/green cycles, missing-control discovery and binding to confirmed source. Native Windows/live acceptance, the existing performance Debug commission, and the rebased-parent compatibility gate remain explicitly pending; they are not passed by nightly or these Code results. Next stage is diff-only round-3 Review under the Code stage contract.

## Receipt provenance

Exact-source validation returned: CHECKPOINT SOURCE VALID source=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 rows=7 (exit 0). Each report's generated Markdown SHA-256 was independently matched against its untouched report.json markdownSha256. Fresh TRX identities/counts and SHA-256 hashes are retained below. Generated JSON/TRX/logs/checkpoint outputs stay ignored in their original run directories. Essential CHECKPOINT lines below are copied without edits from those verified report.md files.

### 20261113-214537-0f17

Receipt: /work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-214537-0f17/report.json

SHA-256: 6a0dcf7e60eefcb4f156288d532c4627742a3893ee14cd735f2fba04e91b0cdb

Markdown SHA-256: 425ffd7a8ba8b7c8889644b21f71c0c73868416c5f2726897e007cc97f31a7e0

```text
CHECKPOINT CP-90 commit=2ee1199f9ab24effebde9442ccf99396438e6fef build=ok filter=/*/*/SessionMessageQueueInterruptedAttemptTests/C1892_Composerless_unknown_attempt_waits_without_Enter_or_retyping executed=8 passed=0 failed=8 skipped=0 trx=/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-214537-0f17/rows/CP-90/run.trx slot=granted waited=75s dirty=0 source=2ee1199f9ab24effebde9442ccf99396438e6fef sourceState=clean buildSource=verified
CHECKPOINT CP-91 commit=2ee1199f9ab24effebde9442ccf99396438e6fef build=reused filter=/*/*/AttentionServiceTests/recent_incident_links_the_task_in_its_session_at_incident_time executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-214537-0f17/rows/CP-91/run.trx slot=granted waited=75s dirty=0 source=2ee1199f9ab24effebde9442ccf99396438e6fef sourceState=clean buildSource=verified
```

CP-90 TRX SHA-256: 0c03df56ff1672bb9f05e437ef3677e0289539f809efa237390486e93cd3db2c

CP-91 TRX SHA-256: b4d009112e968c17d060b555d0e3bd73776cc2b4c919223bfdeaa71e730cfb59

### 20261113-220642-9eac

Receipt: /work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-220642-9eac/report.json

SHA-256: 8bc440b4e9b6cf7c3fdcd6032153068949b5263e768c3e6dedb6eb33089dc7a8

Markdown SHA-256: c5582c173f52599ba113e24b7388bfaf5191eb943cf330a89c4439339defec61

```text
CHECKPOINT CP-2 commit=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 build=failed filter=/*/*/(GrokRulesInitializationTests*)|(GrokRulesFailureTests*)|(PhoneHomeLaunchTransportTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(RunnerTerminalSessionInputEncodingTests*)|(RunnerTerminalSessionGenerationTests*)|(AgentSessionCancelledTurnTests*)|(AgentSessionInterruptedLaunchResumeTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 sourceState=clean buildSource=unknown
CHECKPOINT CP-3 commit=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 build=failed filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(DerivedImpactIndexTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 sourceState=clean buildSource=unknown
CHECKPOINT CP-4 commit=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 build=failed filter=/*/*/GrokRulesReadyOrderingTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 sourceState=clean buildSource=unknown
CHECKPOINT CP-5 commit=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 build=failed filter=/*/*/AgentSessionLaunchFailureTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 sourceState=clean buildSource=unknown
CHECKPOINT CP-6 commit=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 build=failed filter=/*/*/SessionMessageQueuePhoneHomeDropTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 sourceState=clean buildSource=unknown
CHECKPOINT CP-8 commit=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 build=failed filter=/*/*/(AttentionServiceTests*)|(AttentionFeedNoiseTests*)|(StandingBootAttentionTests*)|(ChannelInputWithheldIncidentTests*)|(ChannelInboundRecoveryTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 sourceState=clean buildSource=unknown
CHECKPOINT CP-9 commit=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 build=failed filter=/*/*/SessionMessageQueueCodexPointerTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8ed9accb951d47e3beb3bdbada84d7a66e77ebc6 sourceState=clean buildSource=unknown
```

### 20261113-221635-1d82

Receipt: /work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-221635-1d82/report.json

SHA-256: 7d6d29f1de6aa2ed08da9802bd921d80c5c4c5d7355226380f9844078a9c770b

Markdown SHA-256: 6dc9ef929d203a3ce9dcb2c14a39f386721f601906c7090afff63cdace4b0ce9

```text
CHECKPOINT CP-2 commit=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 build=ok filter=/*/*/(GrokRulesInitializationTests*)|(GrokRulesFailureTests*)|(PhoneHomeLaunchTransportTests*)|(SessionMessageQueueInterruptedAttemptTests*)|(RunnerTerminalSessionInputEncodingTests*)|(RunnerTerminalSessionGenerationTests*)|(AgentSessionCancelledTurnTests*)|(AgentSessionInterruptedLaunchResumeTests*)/* executed=104 passed=104 failed=0 skipped=0 trx=/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-221635-1d82/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(DerivedImpactIndexTests*)/* executed=106 passed=106 failed=0 skipped=0 trx=/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-221635-1d82/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 build=reused filter=/*/*/GrokRulesReadyOrderingTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-221635-1d82/rows/CP-4/run.trx slot=granted waited=60s dirty=0 source=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 build=reused filter=/*/*/AgentSessionLaunchFailureTests/* executed=79 passed=79 failed=0 skipped=0 trx=/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-221635-1d82/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 build=reused filter=/*/*/SessionMessageQueuePhoneHomeDropTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-221635-1d82/rows/CP-6/run.trx slot=granted waited=30s dirty=0 source=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 build=reused filter=/*/*/(AttentionServiceTests*)|(AttentionFeedNoiseTests*)|(StandingBootAttentionTests*)|(ChannelInputWithheldIncidentTests*)|(ChannelInboundRecoveryTests*)/* executed=292 passed=292 failed=0 skipped=0 trx=/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-221635-1d82/rows/CP-8/run.trx slot=granted waited=60s dirty=0 source=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 build=reused filter=/*/*/SessionMessageQueueCodexPointerTests/* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-221635-1d82/rows/CP-9/run.trx slot=granted waited=45s dirty=0 source=3574c40f7ae4cb625926b2b0ac797eba1ebd7798 sourceState=clean buildSource=verified
```

CP-2 TRX SHA-256: 98c02855e668c6f5463ecd263df3cd7eb81c8710c81cb86bbb9f6d87540712e8

CP-3 TRX SHA-256: 1a20abe7a9b63d3244213a1134746b78bea17cde648c26879527c20ed3815d62

CP-4 TRX SHA-256: 80c0a912bd38ab62dfa014c3e897fbdf1605bb87d8d1a93226dfcd91f321da51

CP-5 TRX SHA-256: 5efe41be2120d59a9c5db264db0336d2b0eff7252ca7bf6ee0f8d14c6f7fd5df

CP-6 TRX SHA-256: e5c9f34827541e2d9ef857c3dafa1251dd9772f612ed256745d15e13def6e514

CP-8 TRX SHA-256: bdaec12ad3aa37f24b10e6d7cc1a9c0d16d8503ba1cc6e9b955d0fab0a4206a7

CP-9 TRX SHA-256: e8bfd3358c0e75911913a4e77da07ec8c209327887d1273eac616a54170b1a23
