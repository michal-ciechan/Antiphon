# g90 round 2: explicit operator abandonment

Code/landing owner: ac7f437c-c7df-4e32-ba07-a57dce8c6eac. Base:
f701b2ba967d503427d7064fe4fffa17b8c1738d. Review 3afa3cfa D-1.
All nine original round-1 commits are retained by cherry-pick -x on current master.

Dedicated POST /api/sessions/{id}/messages/{messageId}/abandon is the smallest
safe addition: DELETE retains ordinary Pending cancellation, and abandonment needs
an operator credential and mandatory reason. Reuse Canceled. Persist an Info
AuditRecord using the existing ToolInvocation kind, keyed by message id with an exact
operator-abandon:<message-id> summary, session, actor, reason and timestamp, atomically
with the status change. Duplicate requests return the original cancellation time
and do not overwrite the audit. No timer, Enter, resend or retype.

GET /api/sessions/{id}/messages includes unresolved charged Sent rows alongside
Pending rows, with status, deliveryUnconfirmed, attempt count and last attempt.
The switch refusal names this actual route, blocking ids and the exact operator
POST (Pending rows still use DELETE). Reuse ParkedMessage attention projected
from durable unresolved rows, including stopped/failed sessions and old attempts;
offer OpenAgent and AbandonMessage, never SendNow for Sent rows.

Late matching UserPrompt after abandonment is retained in the ordinary transcript
and ignored by queue reconciliation. Canceled remains terminal with a null verdict.
The shared queue semaphore serializes abandon with ordinary late confirmation and switch;
the session-row transaction lock also serializes Grok rules reconciliation. Canceled
rules rows remain in the identity lookup but skip judging, so neither a late prompt
nor the rules deadline resurrects them or creates a replacement launch request.
the audit/status commit is transactional. Preserve generation/baseline/attempt data.

## Verification design

V-1: real HTTP queue/abandon, composerless Running/Stopped/Failed variants.
V-2: source switch refusal -> visible ids -> abandon -> switch succeeds; other
session and confirmed rows retain round-1 behavior, plus zero-attempt control.
V-3: operator credential/reason, confirmed/Pending refusal, ordinary cancellation,
idempotence, immutable actor/time/reason audit, wrong-session isolation.
V-4: late prompt after abandon stays terminal, no input; both serialized race orders.
V-5: durable bounded attention with OpenAgent and abandon, gone after resolution.
R-1: all brief-named delivery/standing/control classes, preserving CARD-1924 flush.
R-2: policy/mechanical/platform/layering/category/index/selector guard union.
R-3: impacted-classes rows whose globs match full base..HEAD, plus changed classes.
R-4: evidence-diff full range, merge-tree, OS declaration, fresh TRX nonzero class
and method census, client targeted tests/typecheck; inspect unchanged LF/paste/Enter,
no automatic abandonment or timeout/assertion relaxation. No migration/model change.

PC-1..PC-10 and all variants in the parent CARD-1892 mutation candidate remain
pending SourceLanding Mutation. Additional PC-11 variants: hide unresolved Sent;
drop operator check; allow Pending/confirmed; omit/duplicate audit; reconcile
Canceled; remove shared lock; remove session predicate; offer SendNow on Sent;
expire unresolved attention. Mutation owns deliberate mutants and discovery.

## Performance budget

Build the derived-index driver first (done, native output), regenerate twice with
second unchanged, commit index separately. Bootstrap checkpoint driver separately.
These are unlisted prerequisite builds/runs, each leased individually. Checkpoint
rows run serially with a ten-minute row cap, no full Unit/assembly run. Red API row
is expected to fail assertions before implementation; repeat it green after fix.
One cold build per committed red/green group; later green rows share that build.
Previous Review measured named rows below ten minutes; cold builds historically
336/528 seconds. Estimates are provisional, not p95; retain TRX/phase timings and
loadavg with slot admission separate. CARD-1906 tracks oversubscribed host costs;
CARD-1404/1514 own missing p95 and phase gaps. No new test timeout is introduced.
Postgres/HTTP host is shared per test session, unique row/session identities isolate
new tests; the HTTP host uses a cloned shared test database and a refusing runner.
Unknown fixture/external phases stay unknown. No repetitions required after green.

The full green selection is split into serial named rows, with one server test build
and one E2E contract build. Its conservative combined estimate exceeds 30 minutes;
this is a performance Debug obligation for the caller under CARD-1906/1514,
not permission for broad repetition. No row is expected above ten minutes.
The only matching impacted-classes globs are contracts, test-lane-category and
input-delivery-uncertainty; their complete class lists are included below.
Derived reverse-edge suggestions outside those globs are not ordinary scope.
The runner/retention V-1/R-1/V-3 of the parent plan are unchanged; their earlier
receipts remain at their actual SHAs and are not rerun or relabelled here.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | green | tests/Antiphon.Tests -> bin-ac7f-done/ | abandon-api | `/*/*/SessionQueueAbandonTests/*` | V-1,V-3,V-4,V-5 | all listed, 0 failed | 17 | 8 |
| CP-2 | green | CP-1 | standing-switch | `/*/*/StandingSessionSwitchConcurrencyTests/*` | V-2 | all listed, 0 failed | 31 | 2 |
| CP-3 | green | CP-1 | standing-history | `/*/*/(StandingSessionSelectionTests*)\|(StandingSessionOwnershipTests*)\|(StandingSessionQueueSwitchTests*)\|(StandingSessionRecoveryHttpTests*)/*` | R-1,V-2 | all listed, 0 failed | 54 | 3 |
| CP-4 | green | CP-1 | control-resume | `/*/*/(AgentControlServiceIntegrationTests*)\|(StandingResumeReceiptReadinessTests*)/*` | R-1 | all listed, 0 failed | 34 | 2 |
| CP-5 | green | CP-1 | standing-recovery | `/*/*/(StandingContinuityRecoveryTests*)\|(StandingRestartEscalationTests*)/*` | R-1 | all listed, 0 failed | 63 | 4 |
| CP-6 | green | CP-1 | outage-mention | `/*/*/PhoneHomeOutageMentionTests/*` | R-1 | all listed, 0 failed | 8 | 2 |
| CP-7 | green | CP-1 | rules-recovery | `/*/*/(GrokRulesCompactionRecoveryTests*)\|(GrokRulesReadyOrderingTests*)/*` | R-1,R-3 | all listed, 0 failed | 16 | 2 |
| CP-8 | green | CP-1 | delivery-witnesses | `/*/*/(LandRefusalDeliveryWitnessTests*)\|(AgentTaskMidTurnRefineTests*)/*` | R-1,R-3 | all listed, 0 failed | 63 | 3 |
| CP-9 | green | CP-1 | phone-home-observation | `/*/*/(SessionMessageQueuePhoneHomeDropTests*)\|(CodexCliObservationGapTests*)/*` | R-1,R-3 | all listed, 0 failed | 12 | 2 |
| CP-10 | green | CP-1 | queue-listing | `/*/*/(SessionMessageQueueServiceTests*)\|(SessionQueueReceiptPlumbingTests*)\|(SessionMessageQueueDeliveredSpillRecoveryTests*)/*` | R-1 | all listed, 0 failed | 3 | 3 |
| CP-11 | green | CP-1 | late-confirmation | `/*/*/(SessionMessageQueueInterruptedAttemptTests*)\|(SessionMessageQueueCodexPointerTests*)/*` | V-4,R-1 | all listed, 0 failed | 2 | 3 |
| CP-12 | green | CP-1 | attention | `/*/*/(AttentionServiceTests*)\|(AttentionFeedNoiseTests*)\|(StandingBootAttentionTests*)\|(StandingContinuityAttentionTests*)/*` | V-5,R-1 | all listed, 0 failed | 4 | 3 |
| CP-13 | green | CP-1 | policy-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | R-2 | all listed, 0 failed | 5 | 2 |
| CP-14 | green | CP-1 | contract-guards | `/*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(DerivedImpactIndexTests*)\|(ImpactedClassesSelectorTests*)/*` | R-2,R-3 | all listed, 0 failed | 7 | 3 |
| CP-15 | green | tests/Antiphon.E2E -> bin-ac7f-done-contract/ | api-contract | `/*/*/ContractSnapshotTests/*` | R-3,R-4 | all listed, 0 failed | 1 | 6 |
| CP-20 | green | n/a | abandon-ui | `pwsh -NoProfile -File scripts/test-client.ps1 -TypecheckProject client/tsconfig.app.json SessionMessageQueue AttentionPanel` | R-4 | typecheck/lint and both targeted files green | n/a | 2 |
| CP-16 | green | CP-1 | rules-abandon | `/*/*/SessionQueueAbandonTests/Grok_rules_late_prompt_does_not_resurrect_abandoned_input` | V-4 | all listed, 0 failed | 1 | 1 |
| CP-17 | green | CP-1 | rules-judging | `/*/*/(GrokRulesInitializationTests*)\|(GrokRulesFailureTests*)\|(GrokRulesReceiptTests*)/*` | V-4,R-1 | all listed, 0 failed | 3 | 3 |
| CP-18 | green | CP-1 | rules-transactions | `/*/*/(GrokRulesTransactionTests*)\|(GrokRulesQueueBarrierTests*)\|(GrokRulesCompactionTests*)/*` | V-4,R-1 | all listed, 0 failed | 3 | 3 |
