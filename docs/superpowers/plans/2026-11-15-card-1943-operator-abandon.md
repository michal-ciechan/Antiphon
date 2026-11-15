# CARD-1943 operator abandon follow-ups

Original Code / landing owner: 73e7dfdd-5bff-4cef-b23f-42dee4f7113c.
Base: 64e3d78c487d51570f708492ab67b5db6d0de9a9 (current origin/master at start).
The named g90 Review file is absent at base and at its cited reviewed tip;
the card and brief supply the three accepted follow-ups. Full Review recovered
from cb2210e934b040a69ed4563a4ffa40f4f1b0d91c on the Review task ref and read.

Keep the existing queue and attention envelopes. Add bounded paging metadata and
HTTP parameters. Queue uses the pending-note convention: sequence cursor, default
50, maximum 100 and 256 KiB body budget after the first row. Attention pages the
parked/unconfirmed subset in deterministic session/sequence/id order, maximum
100 rows, with a truncation flag and next offset. The client exposes next/reset
navigation; mutating/streamed queue replies return the first page.

An abandoned current Grok rules read stays terminal. Display an explicit current
attention item directing the operator to a fresh start (fresh=true), with the
generation and no resend/cancel action. A new generation removes the notice;
reconcile, recovery, a late prompt or another compact cannot acknowledge the
abandoned row. No automatic input, migrations or runner changes.

## Verification design

V-1: HTTP 422 pins errors.reason, exact validation text, problem media type,
status and code for blank and 1001-character reasons. The existing validation is
already correct: this contract pin is expected green at base; deliberate mutants
belong to SourceLanding Mutation, so no false claim of baseline red.
V-2: HTTP queue and attention page through 1003 unresolved Sent rows with nonzero
counts, stable ordering, no duplicates or missing identities, capped limits and
truncation/continuation metadata. This must fail against the uncapped base.
V-3: abandon a current rules read, reconcile, require explicit fresh-start
attention, preserve Canceled/null verdict/no input and one row, then change the
generation and require the notice to disappear. This must fail at base.
R-1: whole abandon, queue, attention, Grok rules and standing-switch classes.
R-2: whole classes from matching tests/impacted-classes.json rows over base..HEAD.
R-3: guard union required by brief, C1729 VIOLATIONS=0 LISTED=162, unchanged nightly
pins, regenerated derived index twice with second unchanged (driver built first).
V-4: client paging tests, client Vitest, production client build, fresh TRX roster,
exact-source receipts, evidence-diff, OS inventory and conflict-free merge-tree.
Manual: inspect paging controls, no input operations in listing/abandon changes,
generation-bound attention, retained assertions and no migrations/OS changes.
No whole Unit lane or full assembly. Named bounded classes only.
PC-1 (422 field/limit), PC-2 (queue cap/cursor/byte budget), PC-3 (attention
cap/continuation), PC-4 (current rules action/generation) and all variants remain
pending SourceLanding Mutation; operator waived Code mutation checks.

The first split red run could not build: npm ci created two hardlinks for its
esbuild binary, rejected by checkpoint confinement. After the run exited, the
task-owned ignored link was replaced with identical bytes and mode in a new inode.
That build refusal executed zero tests and is not red proof. Client paging tests
independently failed on both missing navigation buttons.

The split backend red at 9bde41c68a9b25dd77cc5cf422893dcb830af864 executed
5 cases: 2 contract passes, 3 intended failures (queue 1003 rows, attention 1005
rows, absent rules follow-up). The bulk fixtures now insert in reverse sequence
and require deterministic sequence ordering and the complete attention census.

CP-5 at ab376ac258b985743ad5dd2ac416be190edac7dd reached its fixed nine-minute
row limit with no TRX. The diagnostic journal recorded 83 completed cases, 82
passed and one failed; these are not native TRX counts. The failure is
CodexCliObservationGapTests/C1029_Old_generation_does_not_confirm, a cancellation
in AgentSessionLaunchQueue.WaitForPendingAsync. Preserve that red run. CP-11 and
CP-12 replace its combined selection with the same whole classes in bounded
rows, at the same nine-minute ceiling. CP-13 checks the exact failed method at
the task base in an owned detached worktree before any inherited attribution.
No timeout, assertion or production implementation changes accompany this split.
CP-6 passed 114 executed tests with one native Windows child-audit skip. Keep
C1210_Windows_linux_only_outcomes_are_exact pending Windows placement, along with
the windows-only impacted AgentTaskLandDeliveryE2ETests/C488_ReviewDeliveryCrashMatrix_Batch4.
Neither native obligation is claimed passed or deferred to a later Final round.

## Performance budget

Serial rows, one shared isolated server build per red/green group. Existing cold
server observations are 336/528 seconds, provisional rather than measured p95;
rows use a 9-minute execution ceiling under this brief, separate from admission.
Integration fixtures share the assembly database and session HTTP host; cases
isolate identities, and the bulk-row test deletes its rows in finally. No timeout
or assertion relaxation. TRX carries per-case durations; checkpoint JSON retains
build/run/admission timing. Uninstrumented external/fixture phases are unknown.
Any selection over 30 minutes needs caller-owned Debug commissioning. Driver
bootstrap builds are explicitly unlisted prerequisites, through build-slot in
native outputs. No unchanged proof repetitions after green.

Current Final selection: CP-2, CP-3, CP-4, CP-6, CP-8, CP-9, CP-10, CP-11, CP-12.
CP-1/7 are red proof, CP-5 is preserved history replaced by CP-11/12, and CP-13
is the base-only attribution check. Do not rerun the superseded combined row as
Final selection. See [Code evidence](2026-11-15-card-1943-code-evidence.md) for
all actual outcomes and pending native/PC obligations.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | red | tests/Antiphon.Tests -> bin-c1943-red3/ | regression-red | `/*/*/SessionQueueAbandonTests/(Abandon_reason_problem_details*)\|(Current_unconfirmed_listings*)\|(Abandoned_current_rules*)` | V-1,V-2,V-3 | 2 contract passes, 3 intended failures | 5 | 9 |
| CP-2 | green | tests/Antiphon.Tests -> bin-c1943-green/ | abandon-queue | `/*/*/(SessionQueueAbandonTests*)\|(SessionMessageQueueServiceTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingSessionQueueSwitchTests*)/*` | V-1,V-2,V-3,R-1 | all listed, 0 failed | 4 | 9 |
| CP-3 | green | CP-2 | attention | `/*/*/(AttentionServiceTests*)\|(AttentionFeedNoiseTests*)/*` | R-1 | all listed, 0 failed | 2 | 5 |
| CP-4 | green | CP-2 | grok-rules | `/*/*/(GrokRulesInitializationTests*)\|(GrokRulesFailureTests*)\|(GrokRulesCompactionRecoveryTests*)\|(GrokRulesReadyOrderingTests*)\|(GrokRulesQueueBarrierTests*)\|(GrokRulesReceiptTests*)\|(GrokRulesTransactionTests*)\|(GrokRulesCompactionTests*)/*` | R-1,R-2 | all listed, 0 failed | 5 | 5 |
| CP-5 | historical-impact | CP-2 | impacted | `/*/*/(AgentTaskMidTurnRefineTests*)\|(CodexCliObservationGapTests*)\|(LandRefusalDeliveryWitnessTests*)\|(PhoneHomeOutageMentionTests*)\|(SessionMessageQueuePhoneHomeDropTests*)/*` | R-2 | all listed, 0 failed | 5 | 5 |
| CP-6 | green | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-3,V-4 | all listed, 0 failed | 11 | 5 |
| CP-7 | red-client | n/a | paging-ui-red | `pwsh -NoProfile -File scripts/test-client.ps1 SessionMessageQueue AttentionPanel -t pages` | V-4 | 2 intended missing-navigation failures | n/a | 3 |
| CP-8 | client | n/a | paging-ui | `pwsh -NoProfile -File scripts/test-client.ps1 -JsonResultPath .antiphon/c1943-client.json SessionMessageQueue AttentionPanel` | V-4 | both whole client files, 0 failed | n/a | 3 |
| CP-9 | client | n/a | client-bundle | `npm --prefix client run build` | V-4 | production build exit 0 | n/a | 3 |
| CP-10 | contracts | tests/Antiphon.E2E -> bin-c1943-contract/ | api-contracts | `/*/*/ContractSnapshotTests/*` | R-2,V-4 | all listed, 0 failed | 5 | 6 |
| CP-11 | impact-retry | tests/Antiphon.Tests -> bin-c1943-impact/ | codex-observation | `/*/*/CodexCliObservationGapTests/*` | R-2 | all listed, 0 failed | 7 | 9 |
| CP-12 | impact-retry | CP-11 | delivery-impact | `/*/*/(AgentTaskMidTurnRefineTests*)\|(LandRefusalDeliveryWitnessTests*)\|(PhoneHomeOutageMentionTests*)\|(SessionMessageQueuePhoneHomeDropTests*)/*` | R-2 | all listed, 0 failed | 76 | 3 |
| CP-13 | baseline | tests/Antiphon.Tests -> bin-c1943-base/ | old-generation-base | `/*/*/CodexCliObservationGapTests/C1029_Old_generation_does_not_confirm` | R-2,attribution | exact base method; record actual outcome | 1 | 9 |
