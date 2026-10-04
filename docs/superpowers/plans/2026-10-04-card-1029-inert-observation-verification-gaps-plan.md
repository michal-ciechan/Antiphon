# CARD-1029: close the inert-observation verification gaps

Status: Plan complete; a narrow reachability investigation precedes TestDesign.
No production change, build, ordinary test, or positive control was run by this
Plan task. The verification-design stage is separate, not folded into this brief.
The checkpoint selection below is the proposed closed scope for that stage to
qualify; it is not permission to claim the missing variants already pass.

## Authority and inspected sources

Read CARD-1029 and CARD-0959 in full through `card.ps1 get -Board Antiphon`.
CARD-1029 owns missing verification, not a CLI compatibility gate. CARD-1023 owns
future known-broken compatibility policy. Preserve the inert observation contract.

Source identities, observed 2026-10-04:

| Identity | SHA / provenance |
|---|---|
| Assigned Plan branch base | `bb18064ba647e0ddb03cae4da437ab60ed447d98` |
| CARD-0959 reviewed source C0959 | `9011b62ca7b5548eeeaa61261b1533a364c6c583` |
| CARD-0959 published source L0959 | `6a88d8ceaedb5934bb3a466802a622a4b56e5b37` |
| Fetched `origin/master` M inspected for this plan | `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd` |

The assigned branch is not rebased or reset. Inspection of M uses pinned Git
objects; this document is the only implementation artifact of the Plan task.
Future authoring starts from a caller-commissioned current-target checkout and
rechecks the listed paths, preserving CARD-1031 and CARD-1022 changes.

Primary references:

- [CARD-0959 plan](2026-10-03-card-0959-runner-codex-version-plan.md), specifically
  the final **Inert observation re-freeze** and its active verification section.
  Earlier gate/override designs are archived.
- [Final continuation evidence](../../investigations/2026-10-04-card-0959-final-verification-859094b8.md)
  and [PATH/dispatch repair](../../investigations/2026-10-04-card-0959-path-and-dispatch-repair.md).
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

GET `/api/runner-defaults` returned revision 2, no per-kind overrides, and a Linux
default. GET `/api/session-runners` showed available eligible Linux and Windows
lanes and an unavailable temporary lane. These are observations, not placement
constants. Read both again at dispatch. Omit `-Runner`; omit `-Platform` for
portable work, use `-Platform Windows` only for native rows. `-Platform Any`
removes an inherited OS constraint. No fleet address belongs in this plan.

## Ground truth

| Card/parent-plan assumption | What landed code/evidence actually establishes | Remaining obligation |
|---|---|---|
| CLI samples might refuse work | CLI admission was removed; metadata and explicit diagnostics remain. Create/Retry auth and model rules are independent. | Test inertness without disabling those rules or introducing CLI gates. |
| V-21 per-kind success is complete | Local Claude/Codex eligible/busy and Codex Shared/Worktree/floor/current run through real producer/queues. Remote fixture hard-codes `RunnerCodexAdapter`, Codex launch kind and pointer formatting. | Local Grok rules-ready; remote Claude/Grok eligible/busy, exact E/W and kind-specific readiness. |
| Enqueue/retry and all negative receipts are covered | Remote ack-only, clipped, sequence-at-baseline, other-session and write-failure variants exist. No PC-191/192/218 assertions; ack-only requires a non-null sequence floor. | Enqueue fault before persistence, retry session identity, old-generation negative, genuinely unobservable screen/degraded branch. |
| V-22 recovery covers remote durable delivery | Current V-22 recreates local DI after claim-before-launch loss, runs the watchdog and explicit Retry, and checks pre-task-save cancellation. Remote fixture owns and disposes schema, recipient and root in one call. | Retain remote DB/workspace/recipient separately from server DI; same queue Id/E/path recovery; actual-input crash and late confirmation; all handoff cuts; PC-246 timestamp adversary. |
| V-23..26 cover all remote paths | `NeverRefusesAsync` covers local/remote Create, local cold/Retry/warm and remote cold eligible/busy. Old-version method adds remote tier cold cases, not remote Retry/warm. | Remote Retry and explicitly qualified retained-row warm/recovery cases for every sample family and model roster below. |
| Fresh remote Create can lead to warm reuse | `CreateAsync` requires remote Worktree and rejects agent pins/follow-ups; `DispatchOneAsync` calls warm reuse only for Shared. `CreateAgentTaskRequest` has no arbitrary exact model field. Existing signed-out warm regression explicitly seeds an already queued remote Shared row. Same guards are present at M. | **Premise mismatch:** measure reachable producer/state paths before treating a seeded warm row as create-to-recipient proof. Do not make remote Worktree reuse legal in this card. |
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
- **D-3 — Resolve remote warm reachability first.** Remote Retry of a real failed
  Worktree task is an ordinary supported path. Remote Shared warm delivery is a
  retained-state dispatcher regression, not a newly admitted task. Before
  TestDesign freezes that matrix, Investigate must establish whether a supported
  producer can persist that state, or document it as legacy-state-only coverage.
  Reject changing Workspace after a successful create and calling it admission
  coverage. Remote exact pinned profiles are not an admitted request shape;
  retain local exact-profile regression and assert exact canonical argv for
  remote High/Medium/Frontier/Low instead.
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
  qualification remain open until evidenced. The only unresolved premise is D-3,
  assigned to investigation, not silently assumed away. TestDesign follows that
  result and writes `## Verification design` before any `next: code` handoff.

## Slices and file ownership

| Slice | Files and work | Tests / exit condition |
|---|---|---|
| I0: reachability prerequisite | Read `server/Application/Services/AgentTaskService.cs`, `AgentTaskDispatcher.cs`, request DTO, warm projection fixture and producer history. Record findings in this plan or a linked investigation. No production writes. | Show remote Shared create refusal, admitted Worktree dispatch path, actual Retry workspace, and provenance/limits of an existing remote Shared warm row. Record whether each can originate from a real producer, with method/branch evidence. |
| S1: descriptor and control qualification | `tests/Antiphon.Tests/Application/RunnerCodexCliEvidenceTests.cs`, `CodexCliObservationTests.cs`; labels/assertion order as needed in the original probe/Windows tests, `ModelAvailabilityCreateTests.cs`, `CodexPhoneHomeCreateTests.cs`. Create `docs/superpowers/plans/2026-10-04-card-1029-control-qualification.md`. | V-13 field matrix; direct V-14 floor/alias assertions; all 192 IDs plus PC-256 receive a manual disposition. No label-only closure. |
| S2: faithful per-kind delivery | `CodexCliRemoteDeliveryFixture.cs`, new `CodexCliObservationGapTests.cs`; extract reusable local arrangement to new `CodexCliLocalDeliveryFixture.cs` only as needed. Borrow setup from `GrokStartupReadyOrderingTests` without editing its production behavior. | New per-kind, enqueue/retry, old-generation and degraded-screen methods; existing eight observation methods remain green. |
| S3: durable handoff recovery | Same fixtures and gap class. Use `BridgeQueueHarness.HarnessOptions.ConfigureDbContext`, preserve/attach options, existing `LandDeliveryBoundary`, launch sink and adapter/phone-home callbacks. Prefer test-local EF interceptors over shared helper changes. | PC-219/220/246 witnesses and all fault cuts below survive disposed/recreated DI with retained schema and recipient. |
| S4: remote inertness matrix | Same gap class/fixtures, sample setters feeding actual capabilities/directory and prompt-counting typed-probe spies. | Four sample-family methods cover remote Retry and the I0-qualified warm/recovery contract; all sample/model rows below. |
| S5: qualification and evidence | This plan, control ledger, optional individual Markdown evidence under `docs/investigations/`; generated receipts/TRX/logs remain ignored. No production edits. | Exact-source ordinary rows, native L0959 obligation, final native C1029 qualification, separate Review, publication, then commissioned SourceLanding Mutation. |

Commit/push each meaningful slice before its long run. S1-S4 should be authored
and committed before the final closed checkpoint selection so every current-source
row binds the same C1029. Slice-specific red triage must be reported explicitly;
there are no repeated green runs for reassurance. Never edit source while a run
is in flight.

## Verification scope for TestDesign

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

Successful remote rows must observe actual framed Transcript requests and one
whole ordinal-equal W on the selected session above the original sequence or
timestamp floor, with the accepted generation retained. Keep existing clipped,
baseline-equal, other-session and write-failure adversaries. Synthetic negative
transcript inputs are allowed; synthesizing the expected positive W is not.

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
Worktree retained-claim success pairs already in V-22; no remote Shared create.
Special PC-219/220/246 witnesses remain distinct because they prove the specific
detecting assertion and original identity, not merely a generic crash outcome.

Faults use an isolated schema, EF save/transaction interceptors, existing claim
boundaries, owned launch/transport callbacks and scripted terminal submission.
TestDesign must bind each cut to an actual symbol and observable before/after
event. If a cut cannot be expressed without a production change, report that
specific seam; do not substitute a mock queue or mark it covered. A recreated
DI graph models application recovery, not power-loss durability.

### Remote sample/model matrix

Every row below runs (a) real failed-task Retry, (b) I0-qualified retained-row
warm reuse, and (c) the same warm handoff across recreated DI, each eligible and
busy. Mutable samples feed actual registered capabilities/status, with successful
auth left enabled. Change a good observation to the row's sample between create
and dispatch, and between watchdog failure and Retry. No typed version operation
may occur in create, Retry, cold dispatch, warm reuse or final launch composition.

H=High, M=Medium (both exact canonical argv `gpt-6.1-sol`), F=Frontier
(`gpt-6-astra`), L=Low (`gpt-5.6-luna`). There is no invented remote profile pin.

| V | Sample | Retry / warm / recreated-warm tier |
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
The warm columns are expressly contingent on the I0 coverage classification;
they must never be reported as real Create-to-warm admission proof.

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

Manual mapping is design qualification, not an executed positive control. Static
plan coverage remains a useful lint and always has reachability unproven. Run all
192 applicable controls/variants plus separately retained PC-256 only after
ordinary Code, separate Review and confirmed publication, as method-scoped
SourceLanding baseline/compiling-defect/intended-red/restore/fresh-green cycles.
Success counts, build errors, wrong assertions and skipped/zero tests are distinct.
Evidence/restoration stays at the assigned external verification root, never
committed from that snapshot. CARD-1031's own control IDs stay separately scoped.

### Checkpoints

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
| CP-11 | all | `tests/Antiphon.Tests -> bin-c1029-failed/` | portable-remote-failed | `/*/*/CodexCliObservationGapTests/C1029_Remote_failed_samples_keep_retry_and_reuse` | V-23 remote matrix | 1 result, 18 vectors, 0 failed/skipped | 1 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | all | `tests/Antiphon.Tests -> bin-c1029-stale/` | portable-remote-stale | `/*/*/CodexCliObservationGapTests/C1029_Remote_stale_samples_keep_retry_and_reuse` | V-24 remote matrix | 1 result, 12 vectors, 0 failed/skipped | 1 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | all | `tests/Antiphon.Tests -> bin-c1029-unknown/` | portable-remote-unknown | `/*/*/CodexCliObservationGapTests/C1029_Remote_unknown_samples_keep_retry_and_reuse` | V-25 remote matrix | 1 result, 36 vectors, 0 failed/skipped | 1 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | all | `tests/Antiphon.Tests -> bin-c1029-old/` | portable-remote-old | `/*/*/CodexCliObservationGapTests/C1029_Remote_old_samples_keep_retry_and_reuse` | V-26 remote matrix | 1 result, 48 vectors, 0 failed/skipped | 1 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | all | `tests/Antiphon.Tests -> bin-c1029-auth/` | portable-existing-auth | `/*/*/(CodexPhoneHomeCreateTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(ModelAvailabilityCreateTests*)\|(ModelAvailabilityDispatcherTests*)/*` | R-1 | 22 results, 0 failed/skipped | 22 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | all | `tests/Antiphon.Tests -> bin-c1029-warm-auth/` | portable-warm-auth-regression | `/*/*/PhoneHomeTaskDispatchProjectionTests/*` | V-27,R-6,PC-256 design baseline | 8 results, 0 failed/skipped | 8 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c1029-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
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

Immediate next stage is **Investigate**, narrowly because the parent plan's
fresh-remote-to-warm path conflicts with the current admission/workspace guards.
Measure the four I0 paths and record the real producer or explicit retained-state
limit; reconcile the old exact-profile wording without altering production.
Then **TestDesign** freezes this plan's closed ordinary roster and all control
bindings, followed by Code -> independent Review -> land -> SourceLanding Mutation.

Plan-stage validation: one active Checkpoints heading, 18 unique 11-column rows,
125 total proposed/historical execution floor, 144 estimated minutes, and 192
unique original PC table rows plus separately recorded PC-256. Referenced existing
test paths exist; whitespace check passed. This was a text/structure check only:
checkpoint importer, builds, tests and deliberate mutations were not executed.
