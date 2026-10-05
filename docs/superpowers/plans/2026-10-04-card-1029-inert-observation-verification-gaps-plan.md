# CARD-1029: close the inert-observation verification gaps

Status: Plan amended after I0; ready for separate TestDesign qualification.
No production change, build, ordinary test, or positive control was run by this
Plan task. The verification-design stage is separate, not folded into this brief.
The checkpoint selection below is the proposed closed scope for that stage to
qualify; it is not permission to claim the missing variants already pass.
This amendment resolves the reachability classification, preserves all 192
original controls plus PC-256, and does not plan the separately filed continuation
defect.

## Authority and inspected sources

Read CARD-1029 and CARD-0959 in full through `card.ps1 get -Board Antiphon`.
CARD-1029 owns missing verification, not a CLI compatibility gate. CARD-1023 owns
future known-broken compatibility policy. Preserve the inert observation contract.

Source identities, observed 2026-10-04:

| Identity | SHA / provenance |
|---|---|
| Original Plan branch base | `bb18064ba647e0ddb03cae4da437ab60ed447d98` |
| CARD-0959 reviewed source C0959 | `9011b62ca7b5548eeeaa61261b1533a364c6c583` |
| CARD-0959 published source L0959 | `6a88d8ceaedb5934bb3a466802a622a4b56e5b37` |
| Fetched `origin/master` M inspected for this plan | `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd` |
| Original plan on `feat/card-task-c389db24` | `faa86d73833343ef9225c6aa766be19314848aca` |
| I0 report on `feat/card-task-0293f857`; amendment branch base | `dde45da524a0db70c659c3397626efd4b01b9892` |

The amendment branch `feat/card-task-93eea7de` advances the I0 report commit
without rebase or reset. Inspection of M used pinned Git objects; this document
is the only changed artifact of this Plan task. Reachability mechanisms and the
seeded regression were rechecked at the amendment base; I0's production census
is attributed to that investigation, not a new run or census by this amendment.
Future authoring starts from a caller-commissioned current-target checkout and
rechecks the listed paths, preserving CARD-1031 and CARD-1022 changes.

Primary references:

- [CARD-0959 plan](2026-10-03-card-0959-runner-codex-version-plan.md), specifically
  the final **Inert observation re-freeze** and its active verification section.
  Earlier gate/override designs are archived.
- [Final continuation evidence](../../investigations/2026-10-04-card-0959-final-verification-859094b8.md)
  and [PATH/dispatch repair](../../investigations/2026-10-04-card-0959-path-and-dispatch-repair.md).
- [I0 remote warm reachability](../../investigations/2026-10-04-card-1029-warm-reachability.md)
  at `dde45da524a0db70c659c3397626efd4b01b9892`: request/Retry/dispatch
  mechanisms, five stored Shared follow-up failures, and the seeded pool's
  unproven producer. This supersedes the original D-3/I0 premise below.
- At M, `docs/investigations/2026-10-04-card-1031-code-b491b9c4.md` and the
  actual parser/probe/projection/test diffs. These facts supersede incompatible
  CARD-0959 expectations; historical receipts retain their original SHA.
- Owners read: `docs/project-context.md`, `docs/testing-and-build.md`
  (manifest, coverage, slots, Mutation), `docs/orchestration-loop.md`
  (stages, native routing, publication/Mutation), `docs/ops-http.md`, and
  `docs/session-runtime-invariants.md` (rules readiness, spill, receipt, generation).

Bodies inspected include `CodexCliObservationTests`, `CodexCliRemoteDeliveryFixture`,
`RunnerCodexCliEvidenceTests.C959_Exact_probe_transport_is_bound`, probe resolver,
`CodexCliProbeDescriptor.FromSpec`, `AgentTaskService.CreateAsync/RetryAsync`,
dispatcher cold/reuse/queue handoffs, queue spill binding and confirmation helpers,
`BridgeQueueHarness` retained-schema options, `PhoneHomeTaskDispatchProjectionTests`,
`DurableRunnerSpillReceiptTests`, `GrokStartupReadyOrderingTests`, and the
unobservable-baseline cases in `SessionMessageQueueDeliveryVerificationTests`.

The amendment re-read GET `/api/runner-defaults` and GET `/api/session-runners`
on 2026-10-04 at 12:46 UTC. Defaults remained revision 2, no per-kind overrides,
with the default runner observed as Linux in the catalogue. The catalogue showed
available eligible Linux and Windows lanes and an unavailable temporary lane.
These are observations, not placement constants. Read both again at dispatch.
Omit `-Runner`; omit `-Platform` for
portable work, use `-Platform Windows` only for native rows. `-Platform Any`
removes an inherited OS constraint. No fleet address belongs in this plan.

## Ground truth

| Card/parent-plan assumption | What landed code/evidence actually establishes | Remaining obligation |
|---|---|---|
| CLI samples might refuse work | CLI admission was removed; metadata and explicit diagnostics remain. Create/Retry auth and model rules are independent. | Test inertness without disabling those rules or introducing CLI gates. |
| V-21 per-kind success is complete | Local Claude/Codex eligible/busy and Codex Shared/Worktree/floor/current run through real producer/queues. Remote fixture hard-codes `RunnerCodexAdapter`, Codex launch kind and pointer formatting. | Local Grok rules-ready; remote Claude/Grok eligible/busy, exact E/W and kind-specific readiness. |
| Enqueue/retry and all negative receipts are covered | Remote ack-only, clipped, sequence-at-baseline, other-session and write-failure variants exist. No PC-191/192/218 assertions; ack-only requires a non-null sequence floor. | Enqueue fault before persistence, retry session identity, old-generation negative, genuinely unobservable screen/degraded branch. |
| V-22 recovery covers remote durable delivery | Current V-22 recreates local DI after claim-before-launch loss, runs the watchdog and explicit Retry, and checks pre-task-save cancellation. Remote fixture owns and disposes schema, recipient and root in one call. | Retain remote DB/workspace/recipient separately from server DI; same queue Id/E/path recovery; actual-input crash and late confirmation; all handoff cuts; PC-246 timestamp adversary. |
| V-23..26 cover all remote paths | `NeverRefusesAsync` covers local/remote Create, local cold/Retry/warm and remote cold eligible/busy. Old-version method adds remote tier cold cases, not remote Retry/warm. | CP-11..14 retain 114 vectors: 38 real Worktree Retry, 38 seeded warm, 38 seeded recreated-warm. Classify their claims separately. |
| Fresh remote Create/Retry can produce a warm pool handoff | The explicit remote Create guard requires Worktree and rejects pins/follow-ups; fresh automatic Shared placement excludes remote runners. `RetryAsync/RequeueAsync` retains Workspace and RunnerId. `DispatchOneAsync` enters reuse only for Shared. | Real remote Create/Retry-to-recipient evidence stays on the cold Worktree path. No post-Create Shared rewrite or follow-up substitution qualifies it. |
| All remote Shared rows must be legacy state | With an omitted runner, a live follow-up rewrites workspace to Shared and inherits its existing runner after the explicit-remote guard. I0 found five such stored production rows, all Failed before a session existed. Their predecessor pool agents have task-worktree directories; reuse excludes them and cold fallback requires Worktree. | A real Shared task producer exists, but these failures do not establish an eligible warm-pool producer. Continuation repair is separately filed and outside this plan. |
| The signed-out warm regression establishes production provenance or recipient delivery | `PhoneHomeTaskDispatchProjectionTests.Signed_out_codex_runner_still_claims_and_reuses_a_warm_session` directly seeds Shared plus an unpinned eligible remote Codex pool/session in a non-worktree directory. It asserts claim/reuse/queue identity and zero launches, without a complete recipient UserPrompt. No production or historical producer for that pool state was demonstrated. | CP-16 retains V-27/R-6 and PC-256's claim regression. CP-11..14 must add complete E/W receipt and recreated-DI evidence for explicitly seeded state; neither claim becomes admission or historical-producer proof. |
| Standing adoption can qualify the remote Codex pool matrix | An omitted-runner standing pin can retain a remote Grok/Claude agent and enter `PlaceOnStandingAgentAsync`, a different branch from pool-candidate selection; named remote Codex is excluded. I0 has source reachability, not a positive standing receipt. | Do not replace a Codex pool vector with standing Grok/Claude or local warm coverage. No standing-journey expansion is commissioned. |
| Remote exact-profile pins can provide arbitrary model coverage | `CreateAgentTaskRequest` has no arbitrary exact model field; the remote fixture uses admitted tier requests. | Keep local exact-profile regression and assert remote canonical High/Medium/Frontier/Low models within their actual path, without inventing remote profile admission. |
| V-13 tests every descriptor field at the boundary | Current runner cases include oversized executable/cwd/PATH; equality 32768 only for PATH; placeholder/NUL cases are partial. | Each of five fields at 32768/32769 and both placeholders/NUL, with an outcome that distinguishes validation from incidental filesystem failure. |
| A PC label qualifies its production guard | Example: V-14 labels 126/127 annotate entry model/floor while the ledger describes canonical lookup/alias defects; 124/125 are not directly labelled there. Several delivery PC labels are absent. | Read and map all original 192 rows to actual guard, fixture, first detecting assertion, and variant; do not certify a grep count. |
| Original 192 guards still have identical semantics | M contains CARD-1031: first valid whole stdout banner line is accepted; valid versions survive stderr/truncation with fixed advisory tokens. PC-11/29 old expected rejections conflict; PC-30/31 and diagnostic assertion sites need reconciliation. Repair additionally defines PC-256. | Preserve 192 original IDs as obligations with explicit retarget/supersession dispositions; track PC-256 separately (193 inherited IDs total). No PC is discharged here. |
| Native CP-2 remains wholly unrun | CARD-0959 closure reports Debug 29/29 at the reviewed source; earlier evidence says native verification was pending. No inspected exact-L0959 receipt establishes the card's landed-SHA requirement. | Validate an existing clean exact-L0959 receipt or execute CP-2 there. At M the same class filter selects 30 results because CARD-1031 added one Windows method. |

## Decisions

- **D-1 — Tests and documentation only.** Reuse the real task, queue, transport,
  spill writer and transcript ingestion paths. Script only the isolated recipient,
  clocks and faults. Reject new production hooks, compatibility refusals, new
  retries, changed ceilings, auth bypasses or production deployment. If ordinary
  tests reveal a product defect, preserve its exact base reproduction and return
  a separately scoped repair; do not fix production under a verification brief.
- **D-2 — Preserve existing coverage; add missing witnesses.** Keep the eight
  current observation methods and existing wire/probe/Windows methods. Put new
  delivery/recovery witnesses in `CodexCliObservationGapTests`, using the existing
  remote fixture after separating recipient lifetime from DI lifetime. Reject a
  second implementation of production delivery or a full provider/model/fault
  Cartesian product. A helper may arrange a fault; only a production outcome can
  discharge its assertion.
- **D-3 — Separate producer and seeded-state evidence (I0 resolved).** Real
  failed-task Retry preserves remote Worktree and exercises cold launch. The
  warm and recreated-warm Codex columns deliberately seed Shared task/agent/session
  state, then exercise production dispatch/queue/receipt paths. Retain them because
  they guard those consumers even though no producer for the eligible non-worktree
  remote pool was demonstrated. Do not call this proven legacy admission. A live
  omitted-runner follow-up does produce Shared, but its task-worktree pool agent
  fails reuse and cold fallback; it supplies no positive warm witness. Reject a
  post-Create workspace rewrite as admission evidence, standing-agent substitution,
  and continuation repair under this card. Remote exact pinned profiles are not
  an admitted request shape; retain local exact-profile regression and exact
  canonical remote tier argv on real cold launches. Seeded warm tests verify
  retained tier/model identity and zero new launches, not recomposed launch argv.
- **D-4 — E and W have independent oracles.** Freeze producer task/settings/limits
  before the handoff. E is LF-only `BuildBrief` output containing literal
  `C959 delivery α\nsecond line\nEND-C959`; W is the exact inline/pointer body.
  Never obtain expected E/W from the queued body, written file or transcript.
  A complete current selected-session UserPrompt plus exact spilled E is receipt;
  status, input acknowledgment, readiness and screen advance are not substitutes.
- **D-5 — Keep the 192-ID audit, not obsolete semantics.** Retarget conflicting
  CARD-1031 obligations explicitly with old/new contract and successor evidence.
  Keep PC-256 and its independent auth regression alongside the 192; never claim
  192 means the later repair control was executed or retired. Reject restoring
  old stderr/banner behavior to make historical controls fail.
- **D-6 — Separate source identities and lanes.** CP-2 qualifies L0959 only.
  Final portable/native rows qualify the same new Code source C1029, with the
  current 30-result Windows roster. Publication may produce L1029; Mutation uses
  the recorded L1029 SourceLanding snapshot, not latest master or C1029 relabelled.
  A portable skip never discharges a native row.
- **D-7 — Focused ordinary scope.** Run the changed fixture consumers, original
  feature classes and relevant auth/warm regressions below. The parent whole-Unit
  row is historical implementation scope, not a required replay for this test-only
  follow-up. No all-assembly, provider-spend or live-fleet tests. If TestDesign must
  edit a shared helper outside this scope, add its actual affected consumers with
  a reason before the Code dispatch; do not run an unbounded suite speculatively.
- **D-8 — No hidden acceptance defaults.** All required variants and manual
  qualification remain open until evidenced. I0's coverage classification is
  resolved; the eligible seeded pool's producer remains unproven and is an explicit
  limit, not a prerequisite to testing its consumers. Reject both deleting these
  vectors and claiming a successful production journey. TestDesign qualifies the
  separate claims and writes `## Verification design` before any `next: code`
  handoff; this amendment discharges no ordinary or Mutation obligation.

## Slices and file ownership

| Slice | Files and work | Tests / exit condition |
|---|---|---|
| I0: completed reachability classification | Linked `docs/investigations/2026-10-04-card-1029-warm-reachability.md`; reads `server/Application/Services/AgentTaskService.cs`, `AgentTaskDispatcher.cs`, `DefaultRunnerRoutingPolicy.cs`, `PhoneHomeLaunchPolicy.cs`, warm projection fixture and stored task/events. No production writes. | Explicit remote Shared refusal; Create/Retry Worktree preservation; real inherited-runner Shared follow-ups fail pool/worktree guards; seeded eligible pool producer unproven. Findings classify coverage and execute no tests. No continuation repair slice. |
| S1: descriptor and control qualification | `tests/Antiphon.Tests/Application/RunnerCodexCliEvidenceTests.cs`, `CodexCliObservationTests.cs`; labels/assertion order as needed in the original probe/Windows tests, `ModelAvailabilityCreateTests.cs`, `CodexPhoneHomeCreateTests.cs`. Create `docs/superpowers/plans/2026-10-04-card-1029-control-qualification.md`. | V-13 field matrix; direct V-14 floor/alias assertions; all 192 IDs plus PC-256 receive a manual disposition. No label-only closure. |
| S2: faithful per-kind delivery | `CodexCliRemoteDeliveryFixture.cs`, new `CodexCliObservationGapTests.cs`; extract reusable local arrangement to new `CodexCliLocalDeliveryFixture.cs` only as needed. Borrow setup from `GrokStartupReadyOrderingTests` without editing its production behavior. | New per-kind, enqueue/retry, old-generation and degraded-screen methods; existing eight observation methods remain green. |
| S3: durable handoff recovery | Same fixtures and gap class. Use `BridgeQueueHarness.HarnessOptions.ConfigureDbContext`, preserve/attach options, existing `LandDeliveryBoundary`, launch sink and adapter/phone-home callbacks. Prefer test-local EF interceptors over shared helper changes. | PC-219/220/246 witnesses and all fault cuts below survive disposed/recreated DI with retained schema and recipient. |
| S4: remote inertness matrix | Same gap class/fixtures, sample setters feeding actual capabilities/directory and selected-client typed-probe counters. Keep seeded-state setup visibly separate from the fixture's real Create/Retry entry points. | Four sample-family methods cover 38 real Worktree Retry vectors, 38 seeded remote pool vectors and 38 seeded recreated-pool vectors, with separate labels/receipt claims. CP-16 remains its existing claim/queue regression. All sample/model rows below. |
| S5: qualification and evidence | This plan, control ledger, optional individual Markdown evidence under `docs/investigations/`; generated receipts/TRX/logs remain ignored. No production edits. | Exact-source ordinary rows, native L0959 obligation, final native C1029 qualification, separate Review, publication, then commissioned SourceLanding Mutation. |

Commit/push each meaningful slice before its long run. S1-S4 should be authored
and committed before the final closed checkpoint selection so every current-source
row binds the same C1029. Slice-specific red triage must be reported explicitly;
there are no repeated green runs for reassurance. Never edit source while a run
is in flight.

## Verification scope for TestDesign

### Reachability and evidence boundaries

I0 is complete for this plan's classification. Its source reconstruction and
five scoped production failure rows are not a new successful execution receipt.
The unresolved producer claim would require retained task/agent/session lineage
for an eligible non-worktree remote pool, or an admitted request sequence reaching
it without fixture-only workspace mutation. No such evidence is assumed here;
TestDesign can proceed with the explicit seeded-state boundary.

| Checkpoint | Evidence to qualify | Claim that it cannot discharge |
|---|---|---|
| CP-4 | Existing local warm and real remote Create/cold consumers, with the current sample assertions and original detecting methods. | Remote pool provenance or remote Retry coverage by analogy to local behavior. |
| CP-6, CP-9, CP-10 | Real remote Worktree Create/failure/Retry and the existing enqueue/fault cuts; preserve actual workspace, runner and task identity. Counts/filters unchanged. | A Shared conversion or live follow-up substituted for the failed Worktree task. |
| CP-11..14 Retry columns | Real admitted remote Worktree task, actual failure boundary, same task retried, then cold recipient with complete current W and spilled E. | Warm pool eligibility or a new task passed off as Retry. |
| CP-11..14 warm/recreated-warm columns | Explicitly seeded remote Shared task plus eligible non-worktree Codex pool/session; production reuse, queue, full recipient E/W and preserved identity across recreated DI. | Create/Retry admission, historic pool provenance, standing-agent placement or a new cold launch. |
| CP-16 | Existing eight-result projection class; its signed-out Codex warm method establishes pre-claim non-refusal, claim/reuse, original agent/session/runner, queue handoff, zero new launches and no extra auth call. | Complete recipient receipt, recreated-DI recovery, an admitted Create/Retry sequence or a proven eligible-pool producer. |
| CP-1..3, CP-5, CP-7..8, CP-15, CP-17..18 | Existing scope and counts remain. Native qualification, independent auth rules and durable cold-queue recovery keep their own witnesses. | Reachability-based discharge, retirement or substitution by the warm regression. |

The observed Shared continuation failures are evidence explaining the boundary,
not a new success/failure test target for CARD-1029. Their separately filed defect
has no implementation, acceptance or repair dependency in this plan.

### Per-kind and receipt witnesses

New class below is `CodexCliObservationGapTests` (G). Existing observation class
is O, wire class is `RunnerCodexCliEvidenceTests` (E). These are planned methods,
not an assertion that they exist at the Plan SHA.

| Witness | Exact proposed method | Required vectors and decisive outcome |
|---|---|---|
| V-21 kinds | G.C1029_Per_kind_receipts | Local Grok eligible/busy (2), remote Grok eligible/busy (2), remote Claude inline/spill crossed with eligible/busy (4): eight vectors. Use real rules readiness/ack for Grok, then actual queued task brief. Freeze task E independently of rules text. Assert no task input before readiness; count task submissions separately from rules submissions. |
| V-21 faults | G.C1029_Enqueue_fault_and_retry_keep_identity | Local/remote, failure before queue insert and lost return after committed insert. Preinsert: not Working, no receipt; watchdog failure then explicit Retry. Postinsert: recover original queue identity, no duplicate. New retry must reach only its new session/generation. PC-191/192. |
| V-21 generation | G.C1029_Old_generation_does_not_confirm | Local/remote recipient retains an old-generation whole W; keep current W withheld. Assert no qualifying receipt and durable E retained, then release actual current submission and confirm. Observe generation/floor through real attempt setup, not a helper calling its own matcher. |
| V-21 degraded | G.C1029_Unobservable_screen_retains_spill | Remote null observable baseline, successful body input, screen sequence advance and no UserPrompt. Prove the real degraded Delivered/Screen path was taken (including degraded receipt), retain E, then ingest the selected recipient's held actual W and release E. NoTranscriptRecord on an observable baseline does not qualify PC-218. |
| V-22 persisted spill | G.C1029_Durable_spill_survives_recreated_graph | After durable busy enqueue, dispose all server DI/courier caches, preserve schema/recipient/root. Remove only the owned spill file if it exists. Recover original queue Id, E, path, generation and attempt floor; real durable lookup writes E before first pointer input. No staging or oracle-based restoration. PC-219. |
| V-22 actual input | G.C1029_Post_input_crash_late_confirms_once | Fault after terminal really submitted W, before confirmation/status save. Recreate DI, pull recipient's actual transcript, settle the same queue row through late confirmation. One task-body submission across both graphs, E released only on complete matching receipt. PC-220. |
| V-22 timestamp | G.C1029_Unobservable_timestamp_floor_is_original | Null sequence baseline; original Timestamp older than original attempt start minus configured tolerance, but newly ingested CreatedAt and large sequence. No confirmation/E release; recreation must not move original attempt floor. Then ingest actual current W and late-confirm without retyping. PC-246. |
| V-22 early cuts | G.C1029_Faults_before_durable_queue_recover | F1-F6 below, local and remote, both sides of each seam; explicit no-side-effect or persisted recovery outcome. |
| V-22 late cuts | G.C1029_Faults_after_durable_queue_recover | F7-F11 below, local and remote, both sides; body submission may be zero before recovery, never falsely inferred from acknowledgment; no duplicate after actual submission. |
| V-23 | G.C1029_Remote_failed_samples_keep_retry_and_reuse | Failure rows of remote matrix below. PC-225..227 and zero diagnostic probe invariants. |
| V-24 | G.C1029_Remote_stale_samples_keep_retry_and_reuse | Stale rows below, original checked-at retained and stale=true; PC-228..230. |
| V-25 | G.C1029_Remote_unknown_samples_keep_retry_and_reuse | Unknown rows below and exact uncertainty projections; PC-231..233. |
| V-26 | G.C1029_Remote_old_samples_keep_retry_and_reuse | Old rows below; floor remains metadata, argv/model/runner unchanged; PC-234..236/240/253. |

Keep per-kind ceilings read from the selected delivery profile. Claude inline:
W=E.TrimEnd(), LF-only bracketed paste then a separate CR; inspect separate Enter,
whole W, wrapper in that order. Remote Claude over the conservative ceiling
spills; non-Claude effective inline ceiling stays zero even under a modern
local profile. For spills, actual UTF-8 file bytes equal E; W is exactly
`BuildBriefPointer` with the known message-owned path substitution, no LF/CR.
Check pointer bytes after `.antiphon/inbox/{queueId:D}.md` expansion against the
existing single-write ceiling. Assert file existence before reading bytes and
bytes before aggregate receipt. Do not require literal B inside a pointer.

New successful remote receipt vectors must observe actual framed Transcript
requests and one whole ordinal-equal W on the selected session above the original
sequence or timestamp floor, with the accepted generation retained. Keep existing clipped,
baseline-equal, other-session and write-failure adversaries. Synthetic negative
transcript inputs are allowed; synthesizing the expected positive W is not.
CP-16 keeps its narrower existing claim/queue contract; its green result cannot
stand in for these new recipient assertions.

### Fault cuts and recovery identity

| Cut | Before / after fault positions | Required retained facts and recovery |
|---|---|---|
| F1 | Task SaveChanges / committed task save | Before: zero task/queue/receipt, explicit new create. After: load original task, continue dispatch without inventing a second task. |
| F2 | Claim plus session transaction save / commit | Before: no committed claim/session pair. After lost scheduling: real FailNeverStartedAsync persists Failed/event, then RetryAsync; no manufactured Failed state. |
| F3 | Volatile launch enqueue / accepted enqueue | Dispose the old launch graph without silently replaying held work. Reconcile actual absent/present runner generation; lost launch uses watchdog/Retry. |
| F4 | Adapter/runner start / real started recipient | Retain actual runner receipt and generation. Recreated server attaches/reconciles an existing recipient or fails a proven absent generation; no duplicate start counted as recovery. |
| F5 | Spill staging / staged E before binding | No durable queue yet: never claim E survived a restart. Restore from persisted task through the real producer recovery, not expected test bytes. |
| F6 | Queue enqueue/save / committed Id/E/owned path before return | Fresh DB read proves original queue identity. After restart zero volatile staged entries; durable courier supplies original E/path. |
| F7 | Runner file write / completed write before input | Before failure: zero pointer input. After success/lost reply: same queue-owned path and bytes retained, no replacement queue. |
| F8 | Body input / actual separate submit | Distinguish typed-but-not-submitted from submitted W. Former uses existing composer/generation recovery; latter must not submit a second body. |
| F9 | Recipient transcript append / persisted actual record | Withhold actual submitted text until release; retained recipient log survives server recreation. Ack-only remains unconfirmed. |
| F10 | Transcript pull / server ingestion | Transport/pull loss retains E and attempt identity; later actual pull provides positive evidence, not a test-inserted expected prompt. |
| F11 | Confirmation/status save / committed save with lost return | Precommit: late-confirm original attempt exactly once. Postcommit: read original settled row, no new enqueue or submit, E clearing justified by matching UserPrompt. |

Closed fault set: 11 seams × 2 sides × 2 lanes = 44 vectors. At each seam,
use eligible/old sample for before and busy/unknown for after on local; reverse
these pairings remotely, so every seam covers both availability and sample
conditions without multiplying unrelated dimensions. Run local Shared and
Worktree retained-claim success pairs already in V-22; the remote producer
vectors use Worktree, without a Shared follow-up or fixture conversion.
Special PC-219/220/246 witnesses remain distinct because they prove the specific
detecting assertion and original identity, not merely a generic crash outcome.

Faults use an isolated schema, EF save/transaction interceptors, existing claim
boundaries, owned launch/transport callbacks and scripted terminal submission.
TestDesign must bind each cut to an actual symbol and observable before/after
event. If a cut cannot be expressed without a production change, report that
specific seam; do not substitute a mock queue or mark it covered. A recreated
DI graph models application recovery, not power-loss durability.

### Remote sample/model matrix

Every row below runs (a) real failed-Worktree-task Retry, (b) seeded remote Shared
pool reuse, and (c) the same seeded pool handoff across recreated DI, each eligible
and busy. These are three separate arrangements, not steps of a Create-to-pool
journey. Mutable samples feed actual registered capabilities/status, with successful
auth left enabled. On the producer path, change a good observation to the row's
sample between Create and dispatch, and between watchdog failure and Retry.
On seeded paths, change the registered observation before production claim/reuse
and retain it across recreation; do not invent a preceding Create or Retry.
No typed version operation may occur in Create, Retry, cold dispatch, warm reuse
or final launch composition. Record the selected remote client's counter separately
from the local counter so a remote-only probe cannot escape the oracle.

The Retry setup must call real CreateAsync with Worktree, use the real failure
boundary/watchdog, then RetryAsync on that task Id. Assert Workspace=Worktree and
the same RunnerId before/after Retry, incremented Attempt, one Retried event and
the new recipient session/generation; do not manufacture Failed or mutate Workspace.
The warm setup explicitly seeds the Shared task and eligible pool/session with
matching runner/kind/tier/project/env/reservation in a non-task-worktree directory.
After setup, production dispatch and queue code must select and deliver to it:
same agent/session/generation, zero launch specs, real input and whole W receipt.
Recreated-warm retains the original queue Id/E/path and recipient across disposed
DI. It may not reseed the queue, restage E or manufacture the expected transcript.
TestDesign must locate the eligible/busy transition so it exercises actual queue
handoff without bypassing warm candidate or working-session guards.

H=High, M=Medium (both exact canonical argv `gpt-6.1-sol`), F=Frontier
(`gpt-6-astra`), L=Low (`gpt-5.6-luna`). There is no invented remote profile pin.
Cold Retry asserts actual canonical launch argv; warm vectors assert the seeded
process's retained model/tier and no launch, not an argv composition they never run.

| V | Sample | Worktree Retry / seeded warm / seeded recreated-warm tier |
|---|---|---|
| V-23 | timeout | H / M / F |
| V-23 | nonzero_exit | F / L / H |
| V-23 | cleanup_unconfirmed | M / H / L |
| V-24 | successful age 15m + 1 tick | M / H / F |
| V-24 | successful age 16m | L / F / M |
| V-25 | omitted legacy fields | H / F / M |
| V-25 | null version | M / L / H |
| V-25 | banana version | F / H / M |
| V-25 | missing timestamp | L / M / F |
| V-25 | malformed fingerprint | H / L / M |
| V-25 | checked-at now + 2m | M / F / H |
| V-26 | 0.156.1, 0.159.0, 0.159.1-beta.1 | Each version at both H and M for all three paths; additionally 0.156.1 at F and L for all three paths |

This is 66 non-old vectors plus 48 old vectors = 114; four TUnit methods with
labelled internal cases, **not 114 executions**. Existing local exact 6.1
profile and remote tier cold tests remain. Do not multiply these rows by F1-F11:
the recovery mechanism uses failed/unknown and old representatives above.
I0 fixes the warm columns' classification as seeded retained-state consumers;
they must never be reported as real Create-to-warm admission or historic-producer
proof. Preserve all vectors and the four method filters with this partition:

| Checkpoint / witness | Real Worktree Retry | Seeded warm | Seeded recreated-warm | Total vectors |
|---|---:|---:|---:|---:|
| CP-11 / V-23 | 6 | 6 | 6 | 18 |
| CP-12 / V-24 | 4 | 4 | 4 | 12 |
| CP-13 / V-25 | 12 | 12 | 12 | 36 |
| CP-14 / V-26 | 16 | 16 | 16 | 48 |
| Total | 38 | 38 | 38 | 114 |

Report each vector's path, sample, tier, availability and receipt outcome; four
green TUnit results alone cannot show that all three coverage claims were exercised.

### Descriptor and manual control qualification

V-13 covers Executable, ResolutionCwd, Path, PathExt and CodexJsPrefix separately:
32768/32769 characters, NUL, `${secret:C959}`, `{{key:C959}}`, including casing
recognized by the ordinal-ignore-case predicate. Extend V-19's pure descriptor
checks for valid-boundary acceptance without opening credentials or processes.
Exercise both real local POST and phone-home dispatcher for rejected descriptors
and inspect request/result and unchanged child count.

For guard reachability use a valid owned absolute executable and inert PATH or
PATHEXT payloads: a length/placeholder mutation must reach an observable
different result. Overlong nonexistent filenames alone cannot qualify PC-117.
At-limit descriptor acceptance is not a promise that an OS can open a 32-KiB
filename; exact failure codes and whether validation was passed are separate
oracles. Node-prefix launcher resolution needs the native Windows fixture where
applicable. TestDesign must identify equivalent/masked variants, not add an
unsafe process or a production seam to force them red.

Manual ledger roster (192 original IDs):
`PC-1..14,16..84,89..109,111..120,122..127,153..159,184..188,190..192,194,199..203,205..255`.
Carry `PC-256` from the repair as a separate obligation, never fold it into 192.
Each row records current source SHA, production file/member/predicate, compiling
mutant description, exact method/filter, fixture branch and variant inputs, first
direct detecting assertion/label, earlier assertions that can mask it, lane,
and status (`qualified design`, `missing witness`, `retargeted contract`, or
`blocked seam`). Every missing witness must map to a Code slice before closure.
For affected remote rows, also record whether the arrangement is a real Worktree
producer or seeded pool state, its task/agent/session provenance and the exact
limit of the detecting assertion. No obligation is removed because I0 found no
eligible-pool producer; all 192 original IDs and PC-256 remain pending.

Minimum explicit reconciliations:

- PC-11: old multiline rejection conflicts with CARD-1031. Retarget the ID to
  first-valid-whole-line selection, with a two-distinct-valid-banner oracle and
  a defect choosing the later banner. Preserve whole-line anchor controls.
- PC-29: stderr with valid stdout now preserves version. Map to the advisory
  omission defect and CARD-1031's direct fixed-diagnostic assertion; no obsolete
  empty-stderr admission predicate remains.
- PC-30/31: cap mutations must hit boundary fixtures that distinguish 4096 from
  8192 and assert the advisory/version contract, including complete-line versus
  truncated fragment behavior. Existing CARD-1031 methods supply these inputs.
- PC-26/199: update the source insertion point to the new advisory return; retain
  synthetic diagnostic sentinel exclusion and no raw output/credential leakage.
- PC-124..127: separately assert Sol entry floor, Frontier null floor, canonical
  exact-model lookup and High/Medium alias output; repair misbound labels/order.
- PC-191/192/218/219/220/246: bind to the new exact gap methods above. For original
  never-refuses PCs retain their original detecting methods and list the remote
  gap methods as additional scenario witnesses; a local mutant failure does not
  establish that a remote-only variant was exercised.
- PC-225/228/231/234 (Create): remote Create witnesses must use admitted Worktree
  requests with the advertised sample present at Create. Keep CP-4's original
  detecting methods; seeded warm setup and the matrix's initially good Create
  cannot witness these refusal guards.
- PC-226/229/232/235 (Retry): qualify the real failed Worktree task's Retry in
  CP-11..14, preserving workspace/runner/task identity. A retained Shared seed or
  the separately seeded auth-Retry test is not a real-Create-to-Retry witness.
- PC-227/230/233/236 (dispatch): name separate cold producer-to-recipient and
  seeded pool/recreated-pool variants, with actual selected-client samples and
  complete E/W receipts. A masked/earlier Create failure does not qualify dispatch.
- PC-240 (zero warm probes): retain the original `_runners.Local` mutation and
  its detecting method. Qualify a remote-only selected-client variant separately
  against the seeded warm/recreated-warm rows, naming the actual remote typed
  operation, counter and first assertion. A zero local counter is insufficient.
- PC-253 (final launch): the remote witness is cold Worktree launch, including
  actual final cwd and selected-client probe count. Preserve the original local
  mutation and identify the remote-only variant separately. Zero-launch warm reuse
  never executes this guard and cannot qualify it.
- PC-256: retain the exact historical compiling defect: restore the removed
  pre-claim auth invocation and the helper's internal visibility together. Bind
  only `PhoneHomeTaskDispatchProjectionTests.Signed_out_codex_runner_still_claims_and_reuses_a_warm_session`
  with filter `/*/*/PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session`.
  Its claim-status assertion must fail for signed-out refusal; build failure,
  zero tests or another assertion is not red. This additional control covers
  seeded pre-claim/reuse/queue behavior, not Create/Retry or complete recipient
  delivery. CP-16's ordinary class run is not its method-scoped Mutation cycle.

All other IDs retain their existing obligations and variants; I0 authorizes no
retirement or silent retargeting. In particular archived admission PC-204 is not
revived, and CP-16 cannot discharge the independent delivery/recovery controls.

Manual mapping is design qualification, not an executed positive control. Static
plan coverage remains a useful lint and always has reachability unproven. Run all
192 applicable controls/variants plus separately retained PC-256 only after
ordinary Code, separate Review and confirmed publication, as method-scoped
SourceLanding baseline/compiling-defect/intended-red/restore/fresh-green cycles.
Success counts, build errors, wrong assertions and skipped/zero tests are distinct.
Evidence/restoration stays at the assigned external verification root, never
committed from that snapshot. CARD-1031's own control IDs stay separately scoped.

### Archived checkpoint proposal (superseded by TestDesign)

All current-source rows have `After=all` and bind C1029; CP-2 instead binds
L0959 in its own native Debug checkout. Its prior receipt may satisfy the row
only after exact-SHA/build/clean-source/roster validation. The supplied manifest
is external input to that historical checkout, not a commit on L0959. Keep CP-2
out of the C1029 aggregate certificate. TestDesign must validate import/selection
and finalize the manual ledger before these are dispatched to Code.

Each row deliberately has one isolated build and one exact filter. Lane is
encoded in Group (no unsupported Lane column). Portable means the effective
default test lane, not a fixed runner. Final-native and historical-native rows
require native Windows; they cannot run on Linux. A same-source split into
smaller method rows is required if measured duration exceeds a foreground window,
with a committed manifest amendment before execution, never an omitted vector.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1029-probe/` | portable-probe | `/*/*/CodexCliVersionProbeTests/*` | V-1..V-5, CARD-1031 reconciliation | 8 results, 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | L0959 | `tests/Antiphon.SessionRunner.Tests -> bin-c1029-historical-windows/` | windows-historical-launcher | `/*/*/(CodexCliVersionWindowsTests*)\|(CodexWindowsLaunchPolicyTests*)/*` | V-6..V-8,R-4 at L0959 | >=29 results, full original roster, 0 failed/skipped | 29 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | all | `tests/Antiphon.Tests -> bin-c1029-wire/` | portable-wire | `/*/*/RunnerCodexCliEvidenceTests/*` | V-9..V-13 | 6 results, all variants, 0 failed/skipped | 6 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | all | `tests/Antiphon.Tests -> bin-c1029-existing/` | portable-existing-observation | `/*/*/CodexCliObservationTests/*` | V-14,V-19,V-21..V-26 existing consumers | 8 results, 0 failed/skipped | 8 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | all | `tests/Antiphon.Tests -> bin-c1029-kinds/` | portable-kind-receipts | `/*/*/CodexCliObservationGapTests/C1029_Per_kind_receipts` | V-21 kinds | 1 result, 8 vectors, 0 failed/skipped | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | all | `tests/Antiphon.Tests -> bin-c1029-enqueue/` | portable-enqueue-identity | `/*/*/CodexCliObservationGapTests/C1029_Enqueue_fault_and_retry_keep_identity` | V-21 enqueue/Retry | 1 result, 0 failed/skipped | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | all | `tests/Antiphon.Tests -> bin-c1029-negative/` | portable-negative-receipts | `/*/*/CodexCliObservationGapTests/(C1029_Old_generation_does_not_confirm)\|(C1029_Unobservable_screen_retains_spill)\|(C1029_Unobservable_timestamp_floor_is_original)` | V-21,V-22 receipt adversaries | 3 results, 0 failed/skipped | 3 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | all | `tests/Antiphon.Tests -> bin-c1029-recreate/` | portable-durable-recovery | `/*/*/CodexCliObservationGapTests/(C1029_Durable_spill_survives_recreated_graph)\|(C1029_Post_input_crash_late_confirms_once)` | V-22 identity/late-confirm | 2 results, 0 failed/skipped | 2 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | all | `tests/Antiphon.Tests -> bin-c1029-early-cuts/` | portable-early-faults | `/*/*/CodexCliObservationGapTests/C1029_Faults_before_durable_queue_recover` | V-22 F1-F6 | 1 result, 24 vectors, 0 failed/skipped | 1 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | all | `tests/Antiphon.Tests -> bin-c1029-late-cuts/` | portable-late-faults | `/*/*/CodexCliObservationGapTests/C1029_Faults_after_durable_queue_recover` | V-22 F7-F11 | 1 result, 20 vectors, 0 failed/skipped | 1 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | all | `tests/Antiphon.Tests -> bin-c1029-failed/` | portable-remote-failed | `/*/*/CodexCliObservationGapTests/C1029_Remote_failed_samples_keep_retry_and_reuse` | V-23 Worktree Retry and seeded pool/recreated-pool receipts | 1 result, 18 vectors (6 Retry + 6 seeded warm + 6 seeded recreated-warm), 0 failed/skipped | 1 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | all | `tests/Antiphon.Tests -> bin-c1029-stale/` | portable-remote-stale | `/*/*/CodexCliObservationGapTests/C1029_Remote_stale_samples_keep_retry_and_reuse` | V-24 Worktree Retry and seeded pool/recreated-pool receipts | 1 result, 12 vectors (4 Retry + 4 seeded warm + 4 seeded recreated-warm), 0 failed/skipped | 1 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | all | `tests/Antiphon.Tests -> bin-c1029-unknown/` | portable-remote-unknown | `/*/*/CodexCliObservationGapTests/C1029_Remote_unknown_samples_keep_retry_and_reuse` | V-25 Worktree Retry and seeded pool/recreated-pool receipts | 1 result, 36 vectors (12 Retry + 12 seeded warm + 12 seeded recreated-warm), 0 failed/skipped | 1 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | all | `tests/Antiphon.Tests -> bin-c1029-old/` | portable-remote-old | `/*/*/CodexCliObservationGapTests/C1029_Remote_old_samples_keep_retry_and_reuse` | V-26 Worktree Retry and seeded pool/recreated-pool receipts | 1 result, 48 vectors (16 Retry + 16 seeded warm + 16 seeded recreated-warm), 0 failed/skipped | 1 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | all | `tests/Antiphon.Tests -> bin-c1029-auth/` | portable-existing-auth | `/*/*/(CodexPhoneHomeCreateTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(ModelAvailabilityCreateTests*)\|(ModelAvailabilityDispatcherTests*)/*` | R-1 | 22 results, 0 failed/skipped | 22 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | all | `tests/Antiphon.Tests -> bin-c1029-warm-auth/` | portable-warm-auth-regression | `/*/*/PhoneHomeTaskDispatchProjectionTests/*` | V-27,R-6; PC-256 seeded claim/reuse/queue design baseline, no recipient/provenance claim | 8 results, 0 failed/skipped | 8 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1029-backstop/` | portable-auth-backstop | `/*/*/CodexProviderAuthRoutingTests/*` | R-7 | 1 result, 0 failed/skipped | 1 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1029-final-windows/` | windows-final-launcher | `/*/*/(CodexCliVersionWindowsTests*)\|(CodexWindowsLaunchPolicyTests*)/*` | V-6..V-8,R-4,CARD-1031 native preservation | 30 current results, full roster, 0 failed/skipped | 30 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Counts are TUnit results, not vector or assertion totals. Source-derived current
rosters are probe 8, evidence 6, observation 8, original auth 22, projection 8,
backstop 1 and native 30; 13 proposed new methods. This is 96 current-source
executions plus the distinct historical 29 = 125, subject to a documented
current-target recount at TestDesign. No silent lowering or accepted new skips.

## Execution, cost and exit criteria

TestDesign must append the executable `## Verification design`, move the single
active Checkpoints table into it, bind each fault to its real seam, and manually
qualify every control as above. Do not create two active Checkpoints headings.
Carry the Worktree-producer versus seeded-state classification into the manual
ledger and vector reports; preserve CP-16's narrower assertion contract. Read
the linked I0 report, but do not re-open its resolved classification or plan the
separately filed continuation repair as a prerequisite to this verification work.
Use coverage lint against actual methods/checklist after authoring; include its
full findings and disposition without treating a static pass as mutation proof.

Bootstrap the checkpoint tool once through `scripts/build-slot.ps1` with a unique
forward-slash `bin-c1029-tool/` output; this prerequisite is reported separately.
Use the checkpoint tool `run --plan <this-plan> --expected-source-sha <C1029>`
with `--serial` and the following explicit selection, then terminal `wait`
(exit not 75), one run per committed source group:

- Portable C1029: `--rows CP-1,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-13,CP-14,CP-15,CP-16,CP-17`.
- Native Windows C1029: `--rows CP-18`.
- Historical native Windows L0959: `--rows CP-2`, with expected-source-sha set
  to L0959 and actual native process working directory at that clean checkout.

Never run the whole mixed-source/mixed-platform table without row selection.
All build/test drivers take a host slot. Exit 4 is not run, never an unleased
retry. Preserve unedited CHECKPOINT lines, exact SHAs, clean/source/build binding,
fresh TRX counts and vector outcomes. Verify receipts before Review can assert
`reviewedSourceClean: true`. Run the evidence diff guard over the entire task
range. Clean only owned alternate outputs after every foreground child exits.

Estimated ordinary floor: 144 minutes (sum of all 18 rows), including historical
CP-2; 136 minutes if its existing exact-L0959 receipt validates. Tool setup adds
4 minutes. These are planning estimates, not measurements; authoring, manual
qualification, slot waits, investigation and repairs are additional. Review
repeats the current-source 136-minute scope, with historical evidence validated
separately. The parent Mutation estimate is 1,893 minutes for 192 IDs before its
4-minute setup. Keep that as an inherited lower planning allowance, not a new
claim: retargeted methods, every variant, PC-256 and the new witnesses require a
fresh numeric Mutation estimate in TestDesign. Its combined budget must include
ordinary plus Mutation floors; a single portable green checkpoint is not closure.

Completion of CARD-1029 ordinary work requires every listed gap vector, a reviewed
manual ledger with no unexplained missing/masked controls, and valid historical
and final Windows evidence. Actual PC completion is separately recorded on the
post-land verification companion at L1029, with restoration evidence; ordinary
success cannot close that obligation. No restart is required for tests/docs.

Immediate next stage is **TestDesign**. I0 and this amendment resolve the
reachability premise by separating real Worktree producer evidence, observed
Shared continuation failure, and deliberately seeded eligible-pool coverage.
TestDesign freezes the closed ordinary roster and all 192+1 control bindings,
followed by Code -> independent Review -> land -> SourceLanding Mutation.
Unknown eligible-pool provenance remains a stated coverage limit, not permission
to claim admission or reduce the required matrix.

Amendment validation: one active Checkpoints heading, 18 unique 11-column rows,
unchanged filters/lanes/execution floors/costs, 125 total proposed/historical
execution floor and 144 estimated minutes. The 114 remote matrix vectors remain
38 Worktree Retry + 38 seeded warm + 38 seeded recreated-warm. The 192-ID roster
matches the parent's active control table, with PC-256 separately retained.
Relative document links and referenced existing test paths resolve; whitespace
check passed. These are text/structure checks only: the checkpoint importer,
builds, tests and deliberate mutations were not executed.

## Verification design

TestDesign qualification, 2026-10-04, task `da4026d2`. The operator's explicit
30–60 minute budget supersedes the earlier execution selection and 144-minute
ordinary estimate. It does **not** change D-1..D-8's product contract or discharge
any unexecuted obligation. This section is the active execution authority. The
previous checkpoint proposal and completion text remain historical design input.
The proposed next Code slice has **55 estimated ordinary minutes**, no whole-Unit run,
and one ordinary execution per selected method. No repeat proof is required after
green; at most three total proof rounds, with a documented failure or change
justifying a repeat. Do not run Mutation under the ordinary budget.

The [control qualification ledger](2026-10-04-card-1029-control-qualification.md)
contains every original 192 ID plus PC-256, all paused for post-land Mutation.
It also separates eighteen independently bypassable guards as PC-257..274; these
are additional obligations, not renumbering or replacement of inherited IDs.
No ordinary test or deliberate mutation was run during this TestDesign task.
**Code handoff is held:** the interrupted Grok initialization cut below cannot
be proved by cooperative graph disposal. Next stage is Plan for this specific
fixture seam; it does not reopen I0 or require a human product choice.

### Inspection

Source B is the assigned `90abbba99d431f0ead268a07916470763bf60025` checkout.
Source M is the plan-pinned `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd` Git object.
B predates CARD-1031. The full probe/parser/Windows/child-fixture and observation
projection B-to-M changes were inspected, including M's three new portable
methods and native notice method. A new Code task must use a caller-commissioned
current target containing CARD-1031/CARD-1022, preserving both; it must not author
obsolete stderr or multiline rejection expectations from B. No rebase of this
assigned TestDesign branch is required or authorized.

| Bodies read | Boundaries -> verification / regression or exclusion |
|---|---|
| All eight `CodexCliObservationTests` bodies; `NeverRefusesAsync`, `AssertWarmReuseAsync`, `AssertDeliveryAsync`, `DispatchKit`, `BriefBoundary`, `Factory`, `Kit`, `RecoveryClock`, `BusyEventBus` | V-14/V-19/V-21..26, R-1; independent task-derived E/W, real local watchdog/Retry, actual input, selected-model and no-probe checks. Existing local pinned warm is not the remote pool producer. |
| Entire `CodexCliRemoteDeliveryFixture`: `RunAsync`, `Freeze`, `HeldLaunches`, `RemoteFactory`, `BusyBus`, `Recipient` | V-21/V-22; real Create Worktree/preparer/claim/launch/queue/phone-home writer/Transcript pull. Missing separate lifetimes, other kinds, mutable registration samples, Retry, seeded pool and DI recovery. |
| All six `RunnerCodexCliEvidenceTests` bodies and `ProbeIo`, `ControlledSendSocket` | V-9..13; writer before/after loss, immutable checked-at, generation/store/epoch refusal, routing, correlated diagnostic replies and real local POST. Only exact transport method is changed/run in the active slice; the other five move to F-Observation. |
| Entire B probe tests, `CodexCliVersionTestFixture`, Windows tests, `CodexNpmLayout`, `CodexVersionChild.ps1`; M additions and changed bodies | V-1..8; auth-free bounded child, native selector, first whole banner and stdout/stderr cap reconciliation. F-Observation/F-Native own assertion-order and native missing witnesses, including second node package for PC-206. |
| `PhoneHomeTaskDispatchProjectionTests.Signed_out_codex_runner_still_claims_and_reuses_a_warm_session`, `SeedAsync`, `CreateDispatcher`, `RecordingLaunchSink`; historical inverse diff `c57e1980` | V-27/R-6/PC-256; seeded Shared/non-worktree pool, capacity 2, reservation retained, definite signed-out framed answer, persisted Dispatched, original agent/session, queue and zero launches. No recipient receipt. |
| `CodexPhoneHomeCreateTests.Signed_out_remote_create_refuses_with_codex_problem_details`, `Present_unknown_and_unavailable_probe_admit`, their Kit/directory/client; `ModelAvailabilityCreateTests.Create_without_IgnoreModelDisabled_is_still_409_while_held`, hold/service/workspace helpers | R-1, PC-241..243. Definite signed-out versus null/unavailable/provider-mismatch; active filter selects these three bodies only. Existing shared hold fixture runs serially. |
| `CodexProviderAuthRoutingTests.Composed_router_measures_codex_in_its_own_home_and_gates_the_launch` | R-7; real composed auth probes over synthetic owned homes, cold launch denied/allowed by correct provider; RoutingProviderAuthProbe.ProbeAsync, CodexAuthProbe.AuthPath and RejectSignedOutCodexAsync read for G-270..272. No live credentials. |
| Entire `GrokStartupReadyOrderingTests`, including Factory/ScriptedRunner; `DurableRunnerSpillReceiptTests` bodies; `SessionMessageQueueDeliveryVerificationTests.Card0164_*` unobservable cases | V-21/V-22; readiness before rules before task, screen fallback, actual timestamp versus ingestion time. Borrow readiness frames and rules ACK grammar, **not** that fixture's disabled delivery verification or its nonce-only final assertion. The direct writer and test-inserted prompt variants are lower-layer substitutes, not full journey evidence. |
| `BridgeQueueHarness.HarnessOptions`, DB registration, attach/new-session setup, OnSubmitted and DisposeAsync; `FakeAgentProtocolAdapter.StartAsync/SendInputAsync`, BeforeInput/OnSubmitted and composer handling | S2/S3 setup: isolated schema, save interceptors, explicit ownership, retain roots; AttachSessionId creates a new fake and cannot by itself preserve the remote recipient. Do not edit these shared helpers in this slice. |
| `CodexCliProbeDescriptor.FromSpec`; runner `Resolve/RunAsync/ProbeAsync/CaptureAsync`; `CodexCliVersion.ParseBanner/Parse/CompareTo`; refresh/settings; directory Register/ObserveCodexCli/LatestCapabilities; framed client pre/post ownership checks | V-1..19; all-five-field matrix, two separate validation layers, shared stream cap, early versus late current-connection checks. RoutingSessionRunnerClient's non-session diagnostic correctly uses Local; remote diagnostic control uses RunnerScopedSessionRunnerClient. |
| `AgentTaskDispatcher.DispatchOneAsync`, cold claim/launch/queue catch, `FitBriefForTyping`, pool selection and reused-session handoff; service Create/Retry auth helper; queue staging/binding, attempt save, DeliverAsync, LateConfirm, sequence/timestamp matchers; `RemoteSpillCourier.FindDurableAsync`; runtime transcript persistence/restart boundary; `RunnerCodexAdapter.AttachAsync/DisposeAsync`; launch readiness/rules production | V-21..26 and F1..F11 below. TranscriptEntry has no generation column: old-generation evidence is excluded by original session/floor, not by an invented per-record generation predicate. |
| I0 report; CARD-0959 active guard/PC tables and repair; testing/build checkpoint, source receipt, slots and Mutation owners; project, orchestration and session runtime owners | Keep 193 inherited obligations, current-source identity, scoped ordinary run and native separation. CARD-1037 continuation and eligible pool provenance are excluded claims. |

Missing setup is concrete Code work, not an implicit fixture assumption:

- S1 changes the exact transport body and the two pure observation bodies. Add
  all-five-field descriptor cases, separately decorated local/selected typed-call
  counters, corrected floor/alias labels and direct assertions. A local counter
  alone cannot detect an operation routed exclusively to a remote client.
- S2 makes `CodexCliRemoteDeliveryFixture` an owned recipient world plus disposable
  server graph, keeping its existing RunAsync entry point for all eight original
  consumers. The world owns schema, git origin, mirror, scripted terminal state,
  transcript records and task-submission counts. A graph owns Kestrel/peer,
  directory, adapters, launch queue and courier. Disposal joins graph work and
  stops watchers but must not call world.StopAsync until the final assertion.
  `RunnerCodexAdapter.DisposeAsync` stops its watcher; it does not kill the peer.
  Reattach through the real attachable adapter and runtime, or
  `ResumeInterruptedLaunchAsync` for an actually Starting delegate. Do not start
  the old recipient again or substitute BridgeQueueHarness's attach fake.
- Add proper Claude/Codex/Grok adapters, registry definitions and scripted
  readiness screens. For Grok retain the real rules payload/receipt validation,
  queue, transcript ACK and launch-brief producer. Supply only an isolated
  successful auth probe; do not disable auth or rules. Count rules and task
  submissions independently. Keep queue delivery verification enabled.
- S3 adds test-local EF save/transaction interceptors keyed to exact task/queue
  IDs and state transitions. After-commit faults must first prove the committed
  row from a fresh un-intercepted context. Keep recipient input/transcript release
  latches outside the graph, and await every pending operation on cleanup.
  `ConfigureDbContext` reaches the real harness contexts. No production hook,
  mock queue, overwritten positive transcript or modified shared helper is needed.
- The ledger records 53 missing-witness authoring rows, seven contract retargets
  and 151 qualified designs. F-Observation/F-Native/F-Matrix own the deferred
  edits; active Code must not edit their files and silently skip their checks.

Descriptor boundary combinations: for each of Executable, ResolutionCwd, Path,
PathExt and CodexJsPrefix test 32768, 32769, NUL, literal/mixed-case `${secret:...}`
and literal/mixed-case `{{key:...}}` in the pure FromSpec body (35 vectors).
For each rejected value exercise both real local POST and framed phone-home
command dispatch. Expect launcher_unverified and unchanged owned child count.
The valid absolute executable plus PATH 32768/32769 is the direct runner length
predicate oracle; inert PATHEXT is another feasible positive boundary. Pure
FromSpec equality proves representability for the other fields, not OS support
for a 32-KiB filename. Filesystem/native-node rejection can mask a per-field
runner mutant; do not count it as a positive control. No need to multiply the
five fields by unrelated version samples. Native prefix resolution stays in
F-Native, with real owned complete npm packages and sealed PATH.

### Delivery inventory

E is frozen LF-only BuildBrief output from the persisted task, its settings and
selected delivery ceilings before handoff. It contains the literal three-line
`C959 delivery α\nsecond line\nEND-C959`. W is independently computed inline text
or BuildBriefPointer output using the eventual row Id solely for its owned path.
Never use queue.Body, file contents or the transcript to create expected E/W.
For long pointers assert UTF-8 length after `.antiphon/inbox/{queueId:D}.md`
expansion against the selected single-write ceiling. Assert file existence before
bytes and bytes before aggregate receipt; inline LF wrapper/separate Enter have
ordered input assertions. Do not require the three-line body inside a pointer.

| Path / producer | Destination and durable identity | Persistence boundary / recovery | Observable recipient evidence |
|---|---|---|---|
| Local and remote real Create Worktree -> workspace preparer -> DispatchOneAsync -> launch sink / real launch queue | task Id + Attempt + selected RunnerId/store + agent/session Id + normalized accepted StartedAt | Task and claim transaction, then actual queue Id/E/path and attempt floor. Before durable queue, recover persisted task through actual watchdog failure and explicit Retry; after durable queue, fresh graph flushes original row. | Exactly one ordinal complete UserPrompt W on selected session above original floor; same accepted generation; local or runner UTF-8 file equals E. Remote also observes actual Transcript request frames. CP-4/5/6/8. |
| Grok provider ready -> GrokRulesRefreshService.InitializeAsync -> QueueLaunchBriefAsync -> real queue | same task/session identity plus rules generation/hash/byte count; task row uses SourceTaskId while cold ordinary brief uses ExecutionTaskId | Rules receipt/ACK persisted before durable task brief; recovery via RecoverSessionAsync and unique SourceTaskId existence check. Include withheld readiness, withheld ACK, enqueue failure before save and lost return after save in eligible/busy variants. | Zero writes before provider ready; zero task-associated row before ACK; rules UserPrompt and matching ACK are prerequisites. Task receipt still requires its own complete W and E file. CP-5; G-262/G-263/G-273/G-274 and G-186/G-191/G-220. The interrupted initialization seam below is a release blocker. |
| Real failed remote Worktree task -> RetryAsync -> cold dispatch | same task Id/RunnerId/Workspace, Attempt+1, exactly one Retried event, new session/generation | Failure is produced by lost committed launch and FailNeverStartedAsync, never a seeded Failed row. Old session retained for negative receipt query. | Full W/E only on the new recipient; exact canonical cold launch model/cwd, zero selected typed diagnostic calls. CP-6 now; all 38 matrix Retry vectors in F-Matrix. |
| Explicitly seeded eligible non-worktree remote Shared pool -> production claim/reuse -> real queue | frozen seeded task/agent/session/runner/store/generation, tier/model/project/env/reservation | Seed once before dispatch. Busy variant first qualifies the pool while idle; after the reuse claim commits, a test-local SavedChanges/TransactionCommitted callback appends real scripted activity before the queued brief can flush. Do not make the pool ineligible before selection. | Original session/generation, zero launch specs, pending while busy, then actual TurnEnd makes queue eligible; complete actual W/E. All 38 warm vectors in F-Matrix. Seed is not admission or historical lineage proof. |
| Same seeded reused queue, then disposed/recreated server graph | original queue Id/E/path/floor plus same seeded recipient identity | Already-eligible variant stops at committed queue save before Enqueue continues; busy variant holds until after recreation. New graph has no staged E; durable courier rewrites missing owned file, then real queue delivers/late-confirms. Never reseed the queue or rerun an already-claimed task as if queued. | Actual W UserPrompt and original E/file; one task submission across graphs. All 38 recreated-warm vectors in F-Matrix. This proves queue recovery, not another DispatchOneAsync claim. |
| Probe/refresh snapshot -> heartbeat writer or fresh registration | runner Id/store/boot/epoch + completed-at + selected launcher fingerprint | Bounded completed snapshot survives a failed send in runner memory; serialized writer queues while busy, reconnect republishes. New boot clears memory. No durable outbox is claimed. | Real receiving directory/catalogue holds that exact completed sample after framed receipt. Explicit diagnostic request additionally joins request Id/epoch/operation to result. Existing E/P methods; F-Observation. A write/ack without receiving projection is insufficient. |

Every task-delivery path runs at least one already eligible and one busy recipient
through the real queue. Negative receipt cases keep actual current submission
withheld, prove E retained and no qualifying receipt, then release the actual
captured submission through the recipient transcript and production ingestion.
Ack, request, queue insertion, event, Sent, screen advance or adapter input return
never alone establishes delivery. CP-16 deliberately stops at claim/reuse/queue
and cannot discharge any row above.

Null-baseline setup for CP-7 uses a fresh already eligible recipient with no
transcript records; hold publication of its actual submitted text. Assert the
persisted LastDeliveryBaselineSequence is null **before** the first body input.
Do not insert busy TurnEnd/activity records in this variant. Script Codex's real
working-indicator/settled-screen evidence while withholding UserPrompt; assert
the degraded Screen delivery and DeliveryUnverified evidence, then E retention.
For PC-246 append the old-timestamp adversary only after this null baseline was
saved, with current CreatedAt and large Sequence. Preserve the original attempt
start across recreation. Test timestamp floor minus one tick, equality, plus one
tick and null timestamp; equality/current are positives, older/null are negatives.
Never advance the floor to recreation time. Old-generation CP-7 uses a real prior
session/generation's already-stored whole W at or below the new attempt's original
sequence floor, and the timestamp arm for delayed old records. It does not invent
a generation field on TranscriptEntry or claim arbitrary untagged records can be
attributed to a generation.

The remote sample setter must cross the real directory boundary. For fresh failures,
old versions, null/malformed version or bad fingerprint, publish a framed heartbeat
with an accepted checked-at, then a Health receive-order barrier and assert the
selected directory sample. For stale evidence advance the controlled clock to
15m+tick/16m while retaining the original checked-at; sending an older heartbeat
would be correctly ignored. For omitted fields or missing timestamp, reconnect
with a real legacy/missing-time registration and assert the cleared snapshot
before dispatch; a null-timestamp heartbeat is ignored and is not the test input.
Keep the recipient world alive across connection changes. Change observations
between actual Create and dispatch, and before Retry. Read the advertised sample
back at each operation before accepting its zero-query oracle.

Bind typed-probe counters at two levels: a forwarding selected-client decorator
counts every GetCodexCliVersionAsync (including refusal before a frame); the real
peer counts PhoneHomeOperation.CodexCliVersion and Recipient.ProbeRequests. Record
local separately. Assert zero deltas before receipt/model assertions at Create,
Retry, cold claim, reused-session handoff, retained-queue flush and final cold
BuildLaunchSpec. Never clear counters after those operations. PC-240 keeps its
original `_runners.Local` mutant; remote reuse calls the selected client; independently mapped PC-269 covers
the recreated queue call. PC-253's remote variant observes cold final cwd and launch
argv; zero-launch warm reuse does not execute it.

Substitutes and limits: scripted terminal/provider records only text actually
submitted after a separate CR; it cannot prove a vendor CLI's live behavior.
Local adapter OnSubmitted -> isolated DB proves local queue/encoding integration,
not a phone-home transport or native PTY. Remote frames/dispatcher/writer/pull are
real but in one test host and cannot prove WAN/power-loss durability. DI recreation
proves loss of server services/cache while external schema and recipient survive;
retain and report any cooperative exception cleanup. Synthetic old/clipped/other
session records are negative adversaries only. Native launcher checks remain
separately commissioned Windows evidence. No proxy assertion is promoted to a
complete recipient result.

The full fault census is preserved in F-Faults. Active CP-5/6/8 exercise its key
handoffs now; CP-9/10's exhaustive 44 vectors are explicitly deferred. Each cut
has before/after evidence and an existing injection point, not a new production
hook. All faults are armed for one identified task/queue transition and disarmed
before recovery; independent contexts read committed facts.

| Cut | Concrete injection / before and after evidence | Recovery and guard controls |
|---|---|---|
| F1 | Test-local SaveChangesInterceptor on Added AgentTask: SavingChanges before; SavedChanges after implicit commit, verified in fresh context | No task means explicit new Create; committed task resumes its own dispatch. No phantom receipt. G-191/G-194; CP-9. |
| F2 | LandDeliveryBoundary `dispatch-warning-claim-before-commit` / `dispatch-warning-claim-committed`; explicit transaction interceptor distinguishes save from commit | No committed claim/session versus original committed claim; lost launch -> real FailNeverStartedAsync -> Retry. G-221/G-194; CP-6/9. |
| F3 | Test-owned IAgentTaskLaunchSink before accepting tuple / after accepting it into HeldLaunches; discard only the old held sink on graph recreation | Confirm absent recipient and zero launches before retry; after releasing to real launch queue, its own outcome controls recovery. G-190/G-221; CP-9. |
| F4 | Recipient.StartAsync before terminal start / after actual terminal start and recorded accepted generation, before returning frame; hold/lost response via peer | Inspect real runner List/Get and generation first. Production launch transport-loss reattach or ResumeInterruptedLaunchAsync when Starting; if cooperative cleanup killed it, report absent and real failure/Retry, never count that as surviving-process crash recovery. G-190/G-192/G-221; CP-9. |
| F5 | Before FitBriefForSession: final launch-sink callback; after StageRemoteSpill and before queue binding: test-local ILogger callback for FitBriefForTyping's `brief is ... delivering a pointer` message, throwing OperationCanceledException | Before: no staged E; after: courier.IsStaged true and no queue row. Local counterpart observes owned file at same log boundary. After graph loss regenerate from persisted task via real failure/Retry, never from expected E. G-216/G-219; CP-9. |
| F6 | Queue Added row SavingChanges / SavedChanges plus actual transaction commit where applicable; fresh read joins Id, task/session, E/path | Before insert: no recipient input or Working; explicit real Retry. Committed row: flush original Id once. G-191/G-192/G-213/G-216/G-219; CP-5/6/8/9. |
| F7 | Owned directory at intended spill-file path fails actual WriteSpillAsync; after-write hook is Recipient.BeforeBody entered from PhoneHomeCommandDispatcher.Input | Failure: no pointer input. Completed write/lost input reply: preserve original path/bytes, then recovery receipt. G-211/G-217/G-219; CP-4/8/10. |
| F8 | FakeAgentProtocolAdapter.BeforeInput on separate CR (typed composer held); OnSubmitted after actual composer submit (not body-write ack) | Before submit: real composer evidence/Enter-only or new-generation recovery. After submit: retained actual W, one submission across graphs. G-214/G-215/G-220; CP-8/10. |
| F9 | Recipient.RecordPrompt latch before adding its actual captured W to retained transcript; after append before Transcript response | Append/release only actual submitted text, keeping source timestamp/uuid. E remains until complete ingested current W. G-218/G-222/G-223/G-224/G-246; CP-7/8/10. |
| F10 | Real peer Transcript reply held/dropped; test-local EF interceptor on Added TranscriptEntry before/after save | Pull again through CatchUpTranscriptAsync; retain original attempt and E, deduplicate actual source records. G-220/G-223/G-246; CP-8/10. |
| F11 | Queue verdict transition SavingChanges / committed SavedChanges, armed only after attempt save and actual submission | Precommit: recreate, ingest actual W, late-confirm original row. Postcommit lost return: same settled row, zero re-enqueue/re-submit. Assert settlement AND count. G-218/G-220; CP-8/10. |

F-Faults keeps the original 11 seams × two sides × local/remote = 44 vectors:
F1–F6 24 and F7–F11 20. Its local before/after eligible/busy and old/unknown
pairing, reversed remotely, stays as specified above the appendix. F5's logger
interception is a test observation of production staging, not an alternate spill
implementation. F4's abrupt server-process death witness and the Grok interrupted initialization
case have a concrete unresolved seam, recorded below. DI disposal alone is not
an abrupt crash.

**Plan return P-1: interrupted launch/initialization cannot use the proposed
exception-and-dispose substitute.** The inspected
AgentSessionService.LaunchInteractiveProcessAsync calls InitializeGrokRulesAsync
inside a catch-all `catch (Exception)` that awaits KillAndDisposeAsync before
rethrow. This also catches OperationCanceledException. Therefore an EF/log/peer
exception at Grok QueueLaunchBriefAsync before/after its committed enqueue, or
cancelling a held InitializeAsync while rules ACK is pending, kills the very
recipient that S2/S3 promise to retain. A still-blocked launch cannot simply be
abandoned when disposing DI: its pending operation must be owned and joined.
RecoverSessionAsync also returns while ILaunchOwnership owns the session, so
calling it alongside that blocked live launch is not the crash-recovery witness.
G-274 currently has no valid positive setup under these combined constraints.

The next Plan task must specify a test-owned server-process boundary (existing
real queue/producer in the child, independent schema and recipient in the parent),
with an exact cut acknowledgement after durable commit/before return, process
identity/start-time verified termination of only that owned child, and a fresh
child attaching the same recipient. It must bind fixture executable, startup,
handshake, cleanup and build/filter cost, or demonstrate another existing seam
that lets every old operation terminate without changing the recipient's state.
No production change, suppressed KillAsync, manually rewritten positive state,
synthetic positive UserPrompt or leaked background launch is authorized as the
substitute. Before/after terminal-start F4 needs the same qualification in
F-Faults. Plan must retain the 30–60 minute ordinary budget by explicit slicing
if its process fixture changes these estimates; it may not silently drop cases.
This is an implementation-design blocker, not a human preference or a reason to
repair CARD-1037. The closed table below is the exact proposed roster; do not
start Code from it until P-1 and the PC-274 setup are resolved and reviewed.

For CP-8's ordinary post-input cases, run the actual queue flush after the launch
has returned (hold boot delivery with actual busy state, then emit the real
TurnEnd) so an exception cannot enter launch cleanup. This proves queue/cache
recreation after completed startup. It cannot discharge interrupted-launch P-1.

### Proves it works now

The V identifiers retain CARD-0959/CARD-1029 meaning. These are acceptance cases
for Code, not claims of a TestDesign execution.

- V-13/V-19: all-field diagnostic validation | pure descriptor + real POST and
  framed dispatcher | CP-3 and CP-4 | each boundary outcome distinguished from
  filesystem failure; rejected descriptors create no child, exact routing preserved.
- V-14: model metadata stays inert | pure model selection | CP-4 | Sol floor
  0.159.1, Frontier/Low null floors, canonical lookup and unchanged High/Medium aliases.
- V-21: per-kind complete delivery | producer/real queue/recipient | CP-4/5/6/7 |
  existing Claude/Codex plus eight Grok/remote Claude vectors; full W/E, separate
  Enter, busy holds, actual eligibility release, negative receipts followed by real
  positive receipt. Grok includes the two queue save/return cuts after ACK.
- V-22: durable handoff and late confirmation | isolated Postgres, recreated DI,
  retained remote recipient | CP-6/7/8 | same durable Id/E/path/floor; new Retry
  generation only when real failure requires Retry; settlement plus one submission.
- V-23..26 existing cases: changing diagnostic metadata cannot refuse work |
  original local/remote producer journeys | CP-4 | eight original methods remain,
  zero selected probes, no model or runner drift. This does not complete the 114
  deferred remote matrix vectors.
- V-27: signed-out seeded warm regression | actual dispatcher/queue | CP-16 |
  persisted Dispatched, original agent/session/runner, queue handoff, zero launches,
  auth call count exactly the one independent test observation; no receipt claim.

### Guards the regression

- R-1: independent provider authentication and model holds remain effective |
  CP-15 | exact Should.ThrowAsync<ModelDisabledException> and
  Should.ThrowAsync<ProviderSignInRequiredException>, while unknown/unavailable
  auth yields no captured exception and a persisted task.
- R-2: diagnostic transport cannot borrow another runner or leak unrepresentable
  fields | CP-3 | selected-peer/local typed counters, exact result/descriptor,
  correlation and zero children after refusals.
- R-3: refactoring the delivery fixture preserves all existing consumers | CP-4 |
  all eight original methods and their internal cases, whole recipient E/W rather
  than a queue/status count. The class filter is justified because they all call
  the changed fixture or share its observation arrangement.
- R-6: no pre-claim Codex auth regression for an existing seeded warm process |
  CP-16's exact signed-out method | claim-status assertion detects PC-256; full
  projection class remainder belongs to F-Regression.
- R-7: cold runner launch still checks its own Codex auth home | CP-17 |
  composed probe refusal has zero starts and inverse owned-home case starts once.
- R-4 native launcher and the rest of V-1..12 remain F-Native/F-Observation
  obligations; current scope does not claim their old receipts at the new SHA.

### Guard inventory

The linked ledger's **211 individually enumerated G-n -> PC-n rows** are normative
here. It lists every retained safety/process/delivery/recovery guard, including
untested guards and authoring work. Counts: original guards=192, repair guard=1,
new independent guards=18; **guards=211, mapped=211, missing=0, duplicate PC maps=0**.
Do not omit guards because their checkpoints or Mutation execution are paused.
The ledger splits independently bypassable FromSpec/runner validation, early/late
connection ownership, provider readiness/rules ACK, and Frontier/Low metadata. Recreated-queue sample gates/no-probe checks get
G-265..269; runner launch/router/home auth assertions get G-270..272.
Grok durable producer deduplication and recovery ACK gating get G-273/G-274.

Old-generation CP-7 is a witness for G-223/G-246's session/floor guards; there is
no additional asserted per-transcript generation guard to mutate. Boundary checks
not claiming a new guard are explicitly named limits rather than phantom controls.
Every handoff in the inventory names its existing PC or the additional Grok controls.

### Positive controls

The ledger specifies production member, compiling defect, exact method/filter,
fixture input, direct detecting assertion, masks, missing setup, lane and slice
for every PC. **All 192 original IDs and PC-256 are retained and paused for
post-land Mutation**; PC-257..274 are paused with them. Seven explicit retargets
preserve current CARD-1031 behavior. PC-97's old combined early/late defect is
split between PC-97 and PC-261 so the independently bypassable checks each have a
control. Fifty-three witness changes are authoring prerequisites, not controls
already demonstrated red. The 21 remote-only variants are separately costed.

Mutation runs break/red/restore/green after land; Code runs V/R; Review judges
before land. Baseline once per exact method and landed SHA; each mutation has an
isolated build, exact `/*/*/Class/Method` red, exact restoration and newly built
same-method green. Expected assertion failure is required; build/setup failure,
wrong assertion, skipped/zero tests and a mere timeout are not red. Keep each
phase's source/build/TRX receipt outside the SourceLanding snapshot, await every
child, and leave exact tracked/index bytes restored. Mutation remains paused
until explicitly commissioned; ordinary Code or Review cannot silently run it.

### Out of scope

- CARD-1037 failed Shared continuations: separate producer/dispatcher repair,
  not a way to create a qualifying warm pool or an acceptance dependency here.
- Provenance of the seeded eligible remote pool; neither standing adoption nor
  a post-Create Workspace rewrite is a substitute for the unproven producer.
- Full provider/model/fault Cartesian product, live CLI/provider spend, fleet
  traffic, production deployment and wholesale Unit/assembly reruns. Boundary
  pairings and the named representatives cover the decisions without that product.
- Native Windows and historical exact-L0959 evidence in portable Code. The
  original native obligations are retained in F-Native, not counted as skipped.
- New production hooks, retries, CLI admission gates or changed timeouts/ceilings.
  A genuine product defect is reproduced at base and returned for separate repair.

The following are **named follow-up slices**, not active rows or optional skips.
The original proposed filters remain in the archived proposal for traceability.
Their manifests must be commissioned separately, with current-source/affected
consumer qualification; CARD-1029 remains partially open until they are evidenced.

| Follow-up slice | Former checkpoint(s) and exact scope | Estimated ordinary floor | Still owed |
|---|---|---:|---|
| F-Observation | CP-1 `CodexCliVersionProbeTests/*` (8 at M); CP-3 remainder: RunnerCodexCliEvidenceTests methods `C959_Heartbeat_updates_only_probe_evidence`, `C959_Freshness_boundaries`, `C959_Generation_change_clears_version`, `C959_Catalogue_and_status_project_version`, `C1031_Advisory_diagnostics_preserve_freshness` | 14 | Probe 8 + exact wire remainder 5; masking repairs and early-current PC-261. |
| F-Faults | CP-9 `G/C1029_Faults_before_durable_queue_recover`; CP-10 `G/C1029_Faults_after_durable_queue_recover` | 20 | All 44 vectors, including any separately qualified abrupt-process-death seam. |
| F-Matrix | CP-11 `G/C1029_Remote_failed_samples_keep_retry_and_reuse`; CP-12 `G/C1029_Remote_stale_samples_keep_retry_and_reuse`; CP-13 `G/C1029_Remote_unknown_samples_keep_retry_and_reuse`; CP-14 `G/C1029_Remote_old_samples_keep_retry_and_reuse` | 43 | 18/12/36/48 vectors, partition below; selected-client Mutation variants and ordinary full receipts. |
| F-Regression | CP-15 remaining exact pinned-profile/model-dispatch/auth methods; CP-16 seven projection methods outside the signed-out method | 8 | The old class-wide regression obligation is retained, not replaced by CP-16's receipt-free claim test. |
| F-Native | CP-2 original exact L0959 native filter; CP-18 current-source Windows launcher filter | 16 | Original 29-result L0959 receipt validation/run; current 30-result M-derived roster, plus any newly added cases. Portable absence cannot discharge either. |

Body recount: B has five RunnerCodexCliEvidenceTests methods; the inspected M
CARD-1031 diff adds C1031_Advisory_diagnostics_preserve_freshness, giving six.
Active CP-3 selects only C959_Exact_probe_transport_is_bound. The follow-up owns
the five exact methods listed above. F-Regression retains the archived 22-result
auth class group minus the three active methods (19) and projection class group
minus its signed-out method (7). Those unchanged groups are deferred scope, not
permission for an active broad run; their follow-up manifest will select this
explicit set difference against its commissioned current source.
The 101-minute total is a follow-up planning floor, not permission to run an open
filter or claim a final source certificate from mixed slices.

| Preserved matrix witness | Worktree Retry | Seeded warm | Seeded recreated-warm | Total |
|---|---:|---:|---:|---:|
| CP-11 / failed | 6 | 6 | 6 | 18 |
| CP-12 / stale | 4 | 4 | 4 | 12 |
| CP-13 / unknown | 12 | 12 | 12 | 36 |
| CP-14 / old | 16 | 16 | 16 | 48 |
| Total | **38** | **38** | **38** | **114** |

The sample/tier/eligible/busy combinations above this appendix remain unchanged.
These are four methods with 114 labelled internal outcomes, never MinExecuted=114.
A vector report must name path, sample, tier, eligibility, task/queue/session and
receipt result. No vectors or controls are retired by this staged budget.

### Checkpoints

Code task `bed39f89` budget split (operator brief, 2026-10-04): implement S1
first and select CP-3, CP-4, CP-15, CP-16 and CP-17 from the unchanged closed
table below (27 estimated row minutes plus bootstrap). S2/S3 and CP-5..8 remain
unimplemented/unrun for the next Code slice, not passed or optional. That slice
must rerun the changed fixture consumers at its own committed source. The brief
explicitly overrides the earlier P-1 Code hold: P-1 blocks only PC-274 in paused
post-land Mutation; do not repair it here. Preserve all five named follow-ups and
the 38/38/38 matrix. No whole-Unit or Windows run is commissioned by this split.
CP-15 filter repair: the first Code run selected zero tests with the original
class/method OR literals. Use the owner-documented suffix wildcards for the
pinned TUnit discovery hint extractor; require exactly the same three methods
in the fresh TRX. This changes selection syntax, not assertions or count floors.
The assigned plan branch predates required CARD-1031/CARD-1022; a normal merge of
the plan-pinned M (`9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`) supplies those
prerequisites while preserving the assigned task base as an ancestor. No rebase.

This is the only active Checkpoints heading/table. Active authoring scope is S1
transport/model descriptors plus S2/S3's seven gap methods and existing fixture
consumers. Commit all those test edits before any row; `After=all` binds every row
to one clean C1029 source. F-Observation/F-Matrix/F-Faults/F-Native/F-Regression edits
and their runs are separate commissioned slices. Do not edit source during a run.

Bootstrap the checkpoint tool once through the build-slot gate into
`bin-c1029-tool/` (estimated 4 minutes). Then use its built entry point for
`run --plan docs/superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md
--rows CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-15,CP-16,CP-17 --serial
--expected-source-sha` followed by the actual full committed Code SHA. Await
terminal `wait` (exit not 75), without ending the turn while children run. Each
row builds its own isolated output and selects one exact filter. Slot timeout
exit 4 is not run; never bypass the gate. Preserve unedited CHECKPOINT lines and
validate source/build/clean/roster receipts. Evidence stays ignored. Run the full
Code task-range evidence-diff guard and remove only owned alternate outputs after
all drivers finish. A missing planned method or vector is incomplete, not a skip.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-3 | all | `tests/Antiphon.Tests -> bin-c1029-active-wire/` | active-exact-transport | `/*/*/RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound*` | V-13,R-2 | 1 result; all descriptor/transport cases; 0 failed/skipped | 1 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | all | `tests/Antiphon.Tests -> bin-c1029-active-existing/` | active-fixture-consumers | `/*/*/CodexCliObservationTests/*` | V-14,V-19,V-21..26 existing,R-3 | 8 original methods, all internal cases; 0 failed/skipped | 8 | 9 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | all | `tests/Antiphon.Tests -> bin-c1029-active-kinds/` | active-kind-receipts | `/*/*/CodexCliObservationGapTests/C1029_Per_kind_receipts*` | V-21 | 1 result; 8 kind vectors plus Grok handoff fault subcases; 0 failed/skipped | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | all | `tests/Antiphon.Tests -> bin-c1029-active-enqueue/` | active-enqueue-retry | `/*/*/CodexCliObservationGapTests/C1029_Enqueue_fault_and_retry_keep_identity*` | V-21,V-22 | 1 result; local/remote before/after save; real same-task Retry receipts; 0 failed/skipped | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | all | `tests/Antiphon.Tests -> bin-c1029-active-negative/` | active-receipt-floors | `/*/*/CodexCliObservationGapTests/(C1029_Old_generation_does_not_confirm*)\|(C1029_Unobservable_screen_retains_spill*)\|(C1029_Unobservable_timestamp_floor_is_original*)` | V-21,V-22 | 3 results; all negative then actual-positive variants; 0 failed/skipped | 3 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | all | `tests/Antiphon.Tests -> bin-c1029-active-recovery/` | active-durable-receipts | `/*/*/CodexCliObservationGapTests/(C1029_Durable_spill_survives_recreated_graph*)\|(C1029_Post_input_crash_late_confirms_once*)` | V-22 | 2 results; eligible/busy, original Id/E/path, one actual submit and settled row; 0 failed/skipped | 2 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | all | `tests/Antiphon.Tests -> bin-c1029-active-auth/` | active-independent-refusals | `/*/Antiphon.Tests.Application/(CodexPhoneHomeCreateTests*)\|(ModelAvailabilityCreateTests*)/(Signed_out_remote_create_refuses_with_codex_problem_details*)\|(Present_unknown_and_unavailable_probe_admit*)\|(Create_without_IgnoreModelDisabled_is_still_409_while_held*)` | R-1 | exactly the 3 named methods; 0 failed/skipped | 3 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | all | `tests/Antiphon.Tests -> bin-c1029-active-warm/` | active-warm-claim | `/*/*/PhoneHomeTaskDispatchProjectionTests/Signed_out_codex_runner_still_claims_and_reuses_a_warm_session*` | V-27,R-6 | 1 result; claim/reuse/queue only; 0 failed/skipped | 1 | 3 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1029-active-backstop/` | active-runner-auth | `/*/*/CodexProviderAuthRoutingTests/Composed_router_measures_codex_in_its_own_home_and_gates_the_launch*` | R-7 | 1 result; 0 failed/skipped | 1 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

All figures are estimates, not measured timings. Ordinary Code V/R floor =
8+9+6+6+8+8+4+3+3 = **55 minutes**, including nine isolated row builds and 21
TUnit executions. Bootstrap adds **4**, so active setup/build/V/R = **59 minutes**.
The exact filters and costs are in the closed table. Authoring, host-slot waits,
failure investigation and repairs are additional; stop and amend the manifest if
a measured row exceeds its foreground budget, never drop its vectors.

Separate ordinary Review of this active slice has the same **55-minute** floor;
its setup allowance is **4** if needed. No loaded-repeat allowance or reassurance
rerun is hidden in either floor. At most three proof rounds, with one planned;
failures require targeted diagnosis and exact-base reproduction before attribution.

The paused Mutation floor is **2,453 minutes** for 211 guard IDs plus 21 explicitly
retained remote-only cycles: 38 exact method baselines, each cycle's isolated
red/restored-green build+test, and 0.5-minute edit/restoration per cycle. The ledger
lists every exact filter, control/variant count and phase minutes. Mutation setup
adds **4**, giving **2,457**. Active Code setup/V/R + paused Mutation floor/setup
= **2,516 minutes**; adding active ordinary Review/setup = **2,575**.
These are conditional planning floors: Plan must price P-1's process fixture and
amend the affected row/method estimates before this becomes executable scope.

The five named follow-up ordinary floors sum to **101 minutes**, separate from
active Code and paused Mutation. Completing them as budgeted brings Code plus
follow-up ordinary scope to **156 minutes**, before their extra bootstrap,
current-source consumer reruns or review. This is not a 55-minute claim for full
CARD-1029 closure. Compared with the old 144-minute dispatch, the active closed
scope saves **89 minutes (61.8%) now by explicit deferral**; whole-plan runtime
savings are **zero** claimed (the split adds at least 12 minutes of isolation and
more conservative estimates). No cost is hidden by silently deleting PCs or the
38/38/38 matrix.

Before handoff audit: bodies read as listed; inherited controls=193, additional=18;
guards=211, mapped=211, missing=0, duplicate PC maps=0. All 211 have concrete
compiling-defect/first-assertion recipes; 53 authoring prerequisites are explicit.
Executable-seam qualification is **210/211**, with **PC-274 blocked by P-1**.
This intentionally fails the all-PCs-executable Code handoff gate; next is Plan.
All nine active rows are one build/one filter, estimated 55 minutes, execution
floor 21. Static checks passed for the inherited roster, distinct guard mappings,
source/method references, document links, table shape, costs and whitespace.
The repository checkpoint importer accepted exactly nine rows with repeat=1,
the intended filters, isolated builds and execution floors; no checkpoint ran.
Its tool-only build used the host gate at source
`f1b322a37e905beefde5a63f1fb65714d815d409`, with OutputPath
`bin-c1029-design-import/`: 4.70 seconds, zero errors, one CS8602 warning at the
unchanged TaskOwnerGuard.cs:170. This unlisted build solely validated the plan
format; it was not an ordinary V/R build. Evidence-history validation over the
task range passed. Generated YAML stays ignored; the owned alternate output
was removed after import. No ordinary tests or positive controls ran.

--- next stage ---
next: plan
handoff: Resolve P-1: launch catch-all kills the retained Grok recipient, and launch ownership blocks recovery while initialization is held. Bind an owned server-process crash/reattach fixture and PC-274 setup without production hooks, then requalify the proposed nine-row 55-minute scope. Preserve all 193 inherited PCs, additional guards and 38/38/38 follow-ups; CP-16 stays claim/reuse/queue only.
artifact: docs/superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md

## Code continuation 70cdf499: S2/S3 incomplete (2026-10-05)

The seven CP-5..8 methods are authored on `feat/card-task-70cdf499`; implementation
SHA `1265c9b51371e38cbe6fc7fa9a0bbea6a0147812`. Ordinary verification is incomplete
within the explicit 90-minute/three-repair budget. Full source-qualified receipts,
fresh method counts, baseline diagnostic and pending inventory are stored in
the continuation report (`.antiphon/task-70cdf499.md` at
`da3d1bd30427cdc194e7eb57a7ff4d6d260a8848`). This amendment
changes no checkpoint selection, assertion, timeout, ceiling or PC disposition.

CP-4 passed all eight consumers at the earlier recorded SHAs; another full CP-4
run is required after the final fixture changes. CP-5 is red: the standard short
Claude Worktree brief exceeds the existing inline ceiling, and the new Grok
post-commit arrangement crosses the excluded P-1 survival seam. Repair those
ordinary test arrangements; P-1 still blocks only PC-274 in paused Mutation.
CP-7's Screen-to-transcript spill release assertion also failed against clean
base production dependencies using the documented composite witness. D-1's
separately scoped repair requirement remains in effect.

The next stage remains Code until the report's red and pending ordinary rows are
resolved. F-Observation, F-Faults, F-Matrix, F-Regression and caller-owned F-Native
remain deferred, never passed. All 211 primary PCs and 21 remote-only variants
remain pending SourceLanding Mutation. Original landing owner remains
`bed39f89-24ec-4ef4-a70e-f8af615ae5b2`; no restart is required by this tests/docs slice.

## Current-master port bbc126e0 (2026-10-05)

This commissioned slice supersedes the preceding ownership/status notes. Its
base is `28a97c2ebf0a1e1f37c8dba47aa449c9ac38a2fa`, branch
`feat/card-task-bbc126e0`; Code task `bbc126e0-12db-4c56-8aea-afdeac38e6eb`
is its own Review subject and landing owner. The continuation net diff from
merge-base `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd` was applied with three-way
merge; S1 tip `a50794508bb48683402b402b0468c9852af20a13` is an ancestor of that
continuation. Historical `.antiphon` reports were not imported as tracked files.
Master's slow-test registrations and CARD-1056 spill reconciliation are retained.

The explicit brief commissions exactly CP-3, CP-4, CP-5, CP-6, CP-7, CP-8,
CP-15, CP-16 and CP-17, serially at one final committed source, with a 100-minute
hard task budget and at most three repair rounds. No whole Unit or Windows run
is commissioned. All five follow-ups, the 38/38/38 matrix, 211 primary PCs and
21 remote variants remain pending; P-1 blocks only PC-274. Ordinary completion
of these nine rows goes to Review of this task, not a claim of whole-card closure.

CP-5 arrangement qualification: a remote Worktree brief always includes the
runner branch and reporting contracts, already exceeding the unchanged 900-byte
conservative ceiling before the user body. Both short and long task briefs keep
the full independent BuildBrief E / pointer W / file-byte receipt assertions.
For each short Claude vector, an additional real queued follow-up carries the
literal three-line body below the same ceiling. It independently checks busy
holding, exact complete UserPrompt above its own attempt floor, generation,
LF-only bracketed paste, separate Enter, Delivered and no spill. This proves
inline queue transport; it does not claim an impossible inline Worktree brief.

Grok postcommit vectors observe the actual committed queue insert, finish rules
initialization and startup with the recipient busy, then discard the server graph.
Eligibility changes before/after recreation supply the two ordinary recovery
variants. No exception is thrown inside InitializeAsync, no kill is suppressed,
and original Id/E/path plus one actual task submission remain mandatory.
Preinsert vectors still throw and run the actual watchdog/Retry; their no-Working
assertion now reloads persisted task state after the fault. Interrupted launch
survival remains exclusively the pending P-1/PC-274 seam.

CP-8 arms persistent queue storage failure only after actual input submission.
Both an initial verdict save and a subsequent flush's verdict save must fail;
fresh contexts prove Sent with no verdict and retained E before restoration.
The recipient and original attempt survive graph recreation, and LateConfirmed
plus exactly one submission are still required. No timeout, transport ceiling,
authentication rule or production source is changed. The stored Code report
records exact outcomes and source-qualified CHECKPOINT lines after execution.

First port diagnostic at `529aa9f42542634673e843b1a3d7e61baec397ca`:
CP-3 1/1, CP-4 8/8, CP-6 1/1 and CP-7 3/3 passed. CP-5 was 0/1:
both inline follow-ups late-confirmed because this scripted peer has no live
transcript stream. All 16 task-brief/Grok recovery vectors completed successfully.
After CP-7 the run was stopped and joined before source edits; CP-8's just-started
build was stopped, and CP-8/15/16/17 tests were not run at that source.

The repaired fixture observes a latch set only by actual terminal submission,
then performs a real framed transcript pull during the original confirmation
window, outside the peer's Input callback. It awaits both input and ingestion;
no synthetic positive prompt, longer deadline or broadened verdict assertion is
used. The inline assertion remains exactly Delivered. CP-8 uses the same live
ingestion arrangement to reach the main verdict save, then advances its existing
clock by 37 seconds past the unchanged 36-second interrupted-attempt age before
the second failing save. The two failures and uncommitted verdict stay exact.
Grok idempotency now has a direct one-row assertion before identity reads after
each of two genuine eligibility/recovery opportunities. The remaining singleton
active filters use the same trailing-method wildcard convention as CP-5..8/15;
their exact one-method TRX rosters and count floors are unchanged.
