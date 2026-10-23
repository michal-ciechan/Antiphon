# CARD-1529 persisted refocus and brief admission

Code owner/landing task: `684b15a2-e609-4a55-ba8d-5735a44fc15b`.
Base: `410738a522585e07943cabf23c1243790773d153`.
Focused Code slice; Plan/TestDesign stages waived by the brief. Final scope follows
the explicit no-Unit instruction. No assembly-wide run is required.

S1 commits red witnesses against unchanged production. S2 stamps the producer's
refocus row with a reserved `refocus-compact:v1:` ConversationKey binding task,
attempt, session, normalized dispatch/start generations and provider. A shared
positive classifier excludes only matching purpose/body/provider rows from brief
admission and the two recovery selectors. ExecutionTaskId/deadline, FIFO/WhenIdle,
complete UserPrompt receipts and uncertainty holds remain authoritative. Legacy
untagged rows remain uncertain. No automatic replay of blocked tasks.

The key is internal correlation, not a delivery receipt. Existing ConversationKey
consumers use exact keys or distinct prefixes (task-input, released-seat-answer,
check, refinements); queue batching requires equal keys, so the null-key brief
cannot batch into this compact. No schema migration or runner changes.

## Verification design

| ID | Invariant and ordinary witness |
|---|---|
| V-1 | Real producer persists compact and exactly one later brief for pending, completed, and post-persist exception; `C1529_Producer_keeps_compact_and_admits_exactly_one_brief` (3 cases). Full matching UserPrompt above floor, manual boundary, task/deadline and returned brief ID asserted. |
| V-2 | Starting and Running recovery admit the missing brief despite pending/sent compact; `C1529_Recovery_backfills_after_a_persisted_refocus` (3 cases). |
| V-3 | Exact acceptance anchor `C544_BriefHandoffRecovery`: 12 internal fresh/warm/follow-up, inline/spill, busy/idle cells. Master fixture launch-effort stamp set to high to reach unrelated warm reuse; production fix independent of CARD-1491. |
| R-1 | Existing real brief prevents a second insert, unreceived is Reuse rather than Received; `C1529_Real_brief_blocks_a_second_brief_without_a_receipt`. |
| R-2 | Missing/empty/unknown key, foreign task/attempt/generation, wrong provider/body, ambiguous marker and retained payload all hold; `C1529_Only_positive_current_refocus_identity_excludes_evidence` (10 cases). |
| R-3 | Full AgentTaskReuseEnqueueTests, DelegationBriefRecoveryTests and DelegationDispatchRecoveryBoundaryTests retain fault isolation, single-brief races, spill integrity, receipt-floor and recovery behavior. |
| R-4 | Full VerificationRoundDeliveryTests retain delivery and persisted obligation behavior around the acceptance anchor. |
| V-4 | Manual source audit: key consumers, missing session/generation/provider fail closed, server-only activation, RC applicability; merge-tree and full base..HEAD evidence guard. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1529-red/` | red-witnesses | `/*/*/(RefocusBriefIdentityTests*)\|(AgentTaskReuseEnqueueTests*)\|(VerificationRoundDeliveryTests*)/(C1529_*)\|(both_reuse_messages_enqueue_when_nothing_throws*)\|(C544_BriefHandoffRecovery*)` | V-1, V-2, V-3, R-1, R-2 | red at brief admission on unchanged production; inspect each failure | 19 | 5 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1529-final/` | identity-recovery | `/*/*/(RefocusBriefIdentityTests*)\|(AgentTaskReuseEnqueueTests*)\|(DelegationBriefRecoveryTests*)\|(DelegationDispatchRecoveryBoundaryTests*)/*` | V-1, V-2, R-1, R-2, R-3 | all listed classes, 0 failed/skipped | 28 | 7 |
| CP-3 | S2 | CP-2 | delivery-acceptance | `/*/*/VerificationRoundDeliveryTests/*` | V-3, R-4 | all listed methods, 0 failed/skipped | 1 | 5 |

### Cost

Ordinary final estimate 12 minutes plus 5 minutes red proof and authoring, within
the commissioned 60-minute budget. Checkpoint bootstrap is a justified unlisted
gated build of tools/Antiphon.Checkpoints (no prebuilt tool in this checkout).
Use 15-minute row and 20-minute total hard deadlines including slot wait. One
run per committed group; no routine repetitions after green. Remove task-owned
bin outputs between groups. Final source must equal the tested pushed tip.

### Mutation handoff (all pending)

Unbound SourceLanding candidate: `docs/superpowers/mutations/2026-10-23-card-1529-refocus-brief.md`.
PC-1 remove producer stamp (pending/completed/post-persist variants); PC-2 ignore
refocus in admission incorrectly; PC-3 retain compact in Starting recovery;
PC-4 retain compact in Running recovery (pending/sent); PC-5 bypass task binding;
PC-6 bypass attempt/generation binding; PC-7 accept untagged or wrong-body/provider
rows; PC-8 ignore a real existing brief; PC-9 treat compact receipt as brief receipt.
Mutation owns deliberate mutants and red/restore/green, including missing-control
discovery. Ordinary red tests do not discharge PCs.

Activation requires a reviewed server restart on the RC line: future refocus
production and queue/recovery classification change. Runner restart: none.
Already blocked legacy tasks require inspected recovery; restart alone does not
requeue them. Desktop and live Windows qualification are outside this task.

S1 evidence: 41d6821fc916328a268587b2c75380a6aaf5d51a, run
20261023-070713-cb71: 17 executed, 10 passed, 7 expected red. All three
producer variants report dispatch_brief_evidence_uncertain; all three recovery
variants retain only the compact. Initial full-path OR selected only the new
class, so the corrected missing two-method selection ran once, with the same
verified build, as CP-1-anchor-20261023-071500-3e6e (2 red). The existing reuse
witness sees one row rather than two. C544's Linux fresh cell fails earlier on
stale SessionStateStore publication (zero brief UserPrompts); the new real-type
producer fixture is the Linux equivalent isolating CARD-1529. C544 fixture writes
now use the already-landed CARD-1464 StoreAwareTranscript helper, retaining all
assertions and all twelve cells. No production state-store behavior changes.
The corrected table is the rerunnable selection; historic receipts retain the
original filter and do not claim the two missing executions. R-2 additionally
covers a foreign ExecutionTaskId correlated through SourceTaskId (11 cases).
R-1 runs with and without a received compact: its UserPrompt never receipts the
pending brief. R-2 also runs the real evidence types with missing/empty/value
goal, provider and retained payload inputs in
`C1529_Optional_identity_inputs_fail_closed_without_throwing` (5 cases).

First S2 qualification (`68943e6eb35d7f078e2979ab371573a5253d05f1`,
20261023-072139-c90e) is RED: CP-2 415/356/59 and CP-3 20/3/17
(executed/passed/failed, zero skipped). The six focused delivery cases now pass
admission/recovery insertion but lack receipts: fake Claude adapters advertise
no dialog observation by default, so CARD-0680 correctly refuses automated input.
The focused fixture and C544 anchor now explicitly provide the accepted session
generation, supported dialog guard and a real readable Claude composer, using the
existing fake adapter contract. No product guard or assertion is weakened.
Rerun CP-2/CP-3 with baseline comparison against the task base to attribute the
remaining existing-class failures before making any inherited-red claim.
