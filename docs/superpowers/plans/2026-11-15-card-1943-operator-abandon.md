# CARD-1943 operator abandon follow-ups

Original Code / landing owner: 73e7dfdd-5bff-4cef-b23f-42dee4f7113c.
Base: 64e3d78c487d51570f708492ab67b5db6d0de9a9 (current origin/master at start).
The named g90 Review file is absent at base and at its cited reviewed tip;
the card and brief supply the three accepted follow-ups.

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

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | red | tests/Antiphon.Tests -> bin-c1943-red/ | regression-red | `/*/*/SessionQueueAbandonTests/(Abandon_reason_problem_details*)\|(Current_unconfirmed_listings*)\|(Abandoned_current_rules*)` | V-1,V-2,V-3 | 2 contract passes, 2 intended failures | 4 | 9 |
| CP-2 | green | tests/Antiphon.Tests -> bin-c1943-green/ | abandon-queue | `/*/*/(SessionQueueAbandonTests*)\|(SessionMessageQueueServiceTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingSessionQueueSwitchTests*)/*` | V-1,V-2,V-3,R-1 | all listed, 0 failed | 4 | 9 |
| CP-3 | green | CP-2 | attention | `/*/*/(AttentionServiceTests*)\|(AttentionFeedNoiseTests*)/*` | R-1 | all listed, 0 failed | 2 | 5 |
| CP-4 | green | CP-2 | grok-rules | `/*/*/(GrokRulesInitializationTests*)\|(GrokRulesFailureTests*)\|(GrokRulesCompactionRecoveryTests*)\|(GrokRulesReadyOrderingTests*)\|(GrokRulesQueueBarrierTests*)/*` | R-1,R-2 | all listed, 0 failed | 5 | 5 |
| CP-5 | green | CP-2 | impacted | `/*/*/(AgentTaskMidTurnRefineTests*)\|(CodexCliObservationGapTests*)\|(LandRefusalDeliveryWitnessTests*)\|(PhoneHomeOutageMentionTests*)\|(SessionMessageQueuePhoneHomeDropTests*)/*` | R-2 | all listed, 0 failed | 5 | 5 |
| CP-6 | green | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-3,V-4 | all listed, 0 failed | 11 | 5 |
