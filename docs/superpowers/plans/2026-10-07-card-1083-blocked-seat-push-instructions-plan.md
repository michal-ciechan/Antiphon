# CARD-1083 blocked-seat and push-before-blocked instructions

Date: 2026-10-07. Stage: Plan with verification design folded in by the brief.
Baseline: origin/master **5a402d6d96da3fff88013faaaf01beb99ce9a98d**, fetched before investigation.
Task branch: feat/card-task-1c7054c1; fast-forwarded from assigned
d36f79f93512ef84589314b32a4fefd24e1bc4ae without rewriting history.
Scope: instruction Markdown, bundle sources and documentation pins only.
Next stage: Code. No runtime, settings, scheduler, session-control, or publication-behaviour changes.

## Outcome and remaining work

Do not close CARD-1083 as already satisfied. CARD-1065 and CARD-1124 delivered the
delegate push instruction, Plan checklist, AGENTS safety text, occupancy explanation
and most owner documentation. The orchestrator bundle still has no blocked-seat
instruction, and the Review bundle still has no input-wait checklist. The orchestrator
skill incorrectly suggests answering releases the seat; the live answer path changes
the task to Working on that session. Complete those narrow gaps and qualify the
default-off checklist without reimplementing parking.

Read the full live CARD-1083, CARD-1065, CARD-1108 and CARD-1124 descriptions using
scripts/card.ps1 get with Board Antiphon. CARD-1108 and CARD-1124 are Done; their close
reasons distinguish shipped/activated code from default-off parking and pending
post-land controls. CARD-1065 remains Review. The original CARD-1083 demand to count
orphan=true as spare capacity and to promise a bounded wait is superseded by code.

## Ground truth

Citations below refer to existing files at the full baseline SHA, not proposed edits.

| Card assumption or surface | Existing text / implementation, file:line | Consequence |
|---|---|---|
| No delegate push-before-blocked instruction | server/Bundles/delegate-basics.md:24 already says "Commit and push all assigned work before reporting blocked", exempts SourceLanding/ReadOnly/CommitOnSettle Never, and says parking will not autosave. DelegationReportFormatter.BuildBrief in server/Application/Services/DelegationReportFormatter.cs:284-289 supplies truthful WIP, non-ignored untracked source, owned-ref and publication-hold wording. | Retain the rule and exclusions; test delivered composition. |
| The generated brief saves work for the delegate | server/Application/Services/DelegationReportFormatter.cs:265-289 separates snapshot, ReadOnly, explicit Commit child, Shared Merge and Never contracts. server/Application/Services/TaskParkPublicationService.cs:247-266 rejects missing custody/commit authority. src/Antiphon.SessionRunner/RunnerWorkspaceParkService.cs:273-284 returns park_dirty or park_index_unverifiable. | No autosave or general push authority. No changes to BuildBrief. |
| No orchestrator bundle guidance | server/Bundles/orchestrator.md:43-50 covers blocked notes/Continue/Reply; :82-97 covers counts/placement; :139-141 describes Refine. None describes the held seat or slots orphan meaning. | Add the missing guidance to the actual launch source. |
| Skill already correct in all details | .claude/skills/antiphon-orchestrator/SKILL.md:25 says "until it is answered or cancelled" and "until an operator enables" release. It already rejects orphan counts and prohibits parking Working sessions. | Replace the two implied release guarantees; keep prompt handling and Working safety. |
| Reply frees the seat | server/Application/Services/AgentTaskReplyService.cs:388-395 first tries released-seat acceptance; :414-467 makes the live task Working and queues a marked answer WhenIdle to the same session. | Distinguish progress from physical release. |
| Continue can answer any Blocked task | server/Application/Services/AgentTaskReplyService.cs:533-567 requires Blocked, Question classification, nonempty StandingAuthority, then calls AnswerAsync. | Use Continue only with both question classification and standing authority. |
| Refine can unblock | server/Application/Services/AgentTaskReplyService.cs:614-683 handles Queued/Dispatched/Working, but returns 409 for Blocked and names Reply. .claude/skills/antiphon-delegate/SKILL.md:332-347 already explains this. | Add the missing restriction to the orchestrator bundle; retain delegate skill text. |
| Blocked is open for both card and concurrency gates | docs/agent-card-lifecycle.md:10-15 already separates these. server/Application/Services/DelegationOpenGate.cs:20-25,114-132 counts Queued/Dispatched/Working only. | Retain card-open / gate-excluded / physical-seat distinction. |
| No WIP occupancy guidance | docs/orchestration-loop.md:786-789 already names MaxOpenTasks, held seat, slots and corrected orphan meaning. | Add answer/deadline qualification, not another occupancy policy. |
| orphan=true means free seats | server/Application/Services/RunnerSlotService.cs:20-32 counts nonempty non-Exited/non-Failed status and defines orphan as missing live desktop session or open owner, excluding warm pool. server/Application/Services/SeatDesktopJoin.cs:66-72 includes Queued and Blocked owners. docs/session-runtime-invariants.md:80-87 describes park projection and release attention. | Inspect slots; never infer free capacity or release permission from orphan. |
| Nothing identifies idle Blocked seats | docs/agent-card-lifecycle.md:13 names SeatIdle (live Blocked, orphan=false, park), SlotOrphan and runner-seat-release:. docs/session-runtime-invariants.md:87 identifies SessionDisagreement with that key. | Retain and pin these names; do not promise every seat immediately raises attention. |
| Parking is enabled or bounded today | server/Application/Settings/BlockedTaskParkingOptions.cs:6-15 defaults Enabled/ReclaimExisting false, interval 120 s, Held backoff 600 s. server/Application/Services/TerminalRunnerSeatReleaseService.cs:17,59-78 defaults AutomaticEnabled false and gates release. docs/session-runtime-invariants.md:69-78 states default-off. | No unconditional release deadline; no setting changes. |
| A timer guarantees release when enabled | server/Application/Services/TerminalRunnerSeatReleaseService.cs:1238-1245 requires fresh legacy proof for at least 120 s using server elapsed time and runner duration. server/Application/Services/BlockedTaskParkingService.cs:178-184 implements Held backoff. docs/session-runtime-invariants.md:112-120 retains limits. | Minimum proof ages and retry cadence are not a maximum session lifetime. |
| Parked is one state and means source is safe | server/Domain/Entities/AgentTaskPark.cs:5-11 names seven park states and four sync states; state alone is not publication/exit evidence. server/Application/Services/BlockedTaskParkingService.cs:216-224 defines transitions. | Document the state/evidence distinction below; no state-machine changes. |
| All released parks can Reply / prerequisite Done resumes automatically | docs/session-runtime-invariants.md:100-108 and docs/orchestration-loop.md:965 contain landed S3 guidance. server/Application/Services/AgentTaskService.cs:3302-3371 binds current attempt, publication, confirmed release, settlement revision and session identity. | Keep remote-pool 422 before Blocked 409 and admission-qualified Reply wording. Explicit Reply after confirmed prerequisite publication; no automatic continuation from prose/card Done. |
| Every park receipt path is proven | docs/session-runtime-invariants.md:110,120 keeps remote-parent CARD-1104, CARD-1097, CARD-1143 and CARD-1144 limits. | Preserve these sentences verbatim; this card neither fixes nor certifies those paths. |
| No AGENTS release rule | AGENTS.md:71-75 already states the process-release invariant, default-off dormant bound, Working veto and separate CARD-0079 exception. | Keep those safeguards; add only deadline qualification. |
| No stage checklist | server/Bundles/stage-plan.md:11 already asks the release/time question; server/Bundles/stage-review.md:1-16 does not. Existing "Today nothing" is less precise than a configured-gate condition. | Qualify Plan and add Review, scoped to changed input waits. |
| Bundles are generated files to patch | server/Antiphon.Server.csproj:19-29 embeds Bundles/*.md, excluding README. server/Application/Services/InstructionBundles.cs:242-279 normalizes LF, trims and hashes sources; :28-31 renders the generated header. InstructionBundleComposer.cs:101-118 generates the composed prompt. | Markdown is the editable source; generated prompt/header/assembly is output. No separate bundle-regeneration script. |
| Every role gets board-api or a skill automatically | InstructionBundles.cs:191-221 maps orchestrators to orchestrator+basics, stage Workers to their stage+basics, specialists to none; BoardApi at :85 is attachment-only. server/Bundles/README.md:32-48 describes this. Actual skills are under .claude/skills/, not a root skills/ directory. | Inspect board-api but leave its card-API scope and role mapping alone. |
| CLAUDE and AGENTS need parallel text | docs/agent-instruction-file-contract.md:11-38 specifies the portable tracked import, not symlink/hardlink; CLAUDE.md:1 is @AGENTS.md. | Edit AGENTS only; retain the pointer and portability owner. |
| Pins already cover every missing instruction | InstructionBundleTests.cs:922-933 and AgentBundleAttachmentTests.cs:404-414 cover basics+Plan only. BlockedTaskParkProjectionTests.cs:106-139 pins skill/AGENTS/Plan, including the misleading old skill sentence. | Extend existing documentation pins; update superseded literals explicitly. |
| Bundle growth is harmless | InstructionBundleTests.cs:629-639 caps orchestrator at 14,310 chars; :664-685 reserves 500 chars of argv headroom; :811-818 caps ASCII stages at 2,500. CheckpointRepeatDocumentationTests.cs:28-31 pins approved orchestrator bytes. | Preserve caps/headroom; approve the intentional byte change with a new literal hash. |

## Decisions

- **D-1 — Finish only the residual instruction gap.** Two 30-60 minute slices below;
  no parking rollout, cancellation, session termination or new recovery policy.
  Reject closing now because orchestrator/Review launch text remains incomplete.
- **D-2 — State defaults and conditions.** A live Blocked task can retain a seat
  indefinitely while parking is off. Enabling parking and automatic release still
  needs identity, publication, idle and release proof; legacy discovery also needs
  ReclaimExisting. Reject the card's unconditional bound and orphan-count recipe.
- **D-3 — Answering is progress, not capacity reclamation.** Reply to the live task
  resumes Working; admitted released-seat answers use retained context/new attempt.
  Continue is limited to questions with standing authority; Refine rejects Blocked.
  Reject advising cancellation or session-stop commands to reclaim capacity.
- **D-4 — Preserve publication custody.** Keep delegate-basics and BuildBrief
  unchanged, including ignored-evidence and SourceLanding/ReadOnly/Never exceptions.
  Reject copying a blanket push order into every skill or changing commit-on-settle.
- **D-5 — Edit source and prove generated delivery.** Change server/Bundles Markdown,
  not stamped prompt output, compiled artifacts or live SystemPromptAppend.
  Extend composition/source parity pins without modifying generator behaviour.
- **D-6 — Retain semantic and byte budgets.** The exact replacements below measure
  orchestrator 13,684 LF characters (baseline 13,638) and Review 2,494 ASCII characters
  (baseline 2,481), including trailing LF. This is a static text measurement, not
  executed argv evidence. Keep the 14,310/2,500 caps and 500-char argv reserve;
  the real composition regression must pass. Reject widening caps or truncation.
- **D-7 — Verification is documentation-scoped.** This brief folds test design into
  Plan and explicitly requests next: code. Execute only the checkpoint selection
  below; no whole-Unit, runtime integration suite, browser or process-spawn tests.
  AgentBundleAttachmentTests is class-tagged Integration, but its one selected
  C1065 method is a pure documentation/composition pin, with no database fixture.
- **D-8 — Use effective placement.** Read GET /api/runner-defaults and
  GET /api/session-runners before dispatch. Both returned successfully during this
  investigation; the catalogue includes an unavailable-for-new-work draining entry,
  so availability alone does not choose placement. All checkpoints use the portable
  documentation lane, executable on Linux or Windows; omit -Runner and -Platform
  for dispatch (Any explicitly unpins). Do not bake fleet IDs or URLs into the plan's
  commands. No OS-only behaviour is introduced.
- **D-9 — Preserve known limits and imported instructions.** Keep the landed S3
  follow-up/422/settlement-revision guidance and Known-limits sentence, the
  CLAUDE.md import, and the agent instruction portability contract. Do not claim
  new parked-answer end-to-end coverage or activation from these text pins.

## Exact instruction edits

Whitespace may wrap paragraphs; sentence content is the pin contract. Retained text
outside named replacements remains intact.

### E-1: orchestrator bundle

In server/Bundles/orchestrator.md replace the paragraph starting "A delegate's own
report" (baseline lines 43-50) with:

```text
A delegate reports `[antiphon-report:<id> done|blocked|failed]`; `report=unmarked` is unverified.
Blocked notes carry `reason:` / `asks:` / `authority:` / `next:`. Use -Continue <id> only for a
Blocked question with standing authority; otherwise -Reply if you can answer, else surface asks:
now; never NO_REPLY a blocked note. Dispatch pre-approved sequences with
-Authority "<the user's own words>"; keep the work delegated.
```

Immediately after it insert:

```text
A live Blocked child keeps its runner seat; Reply resumes work without freeing it.
Parking defaults off (BlockedTaskParking:Enabled=false); do not assume a release deadline.
Answer or surface the decision before leaving. At capacity, read GET /api/session-runners/{id}/slots:
a live Blocked owner reads orphan=false with its park field; orphan=true is not a count of free seats.
```

Replace the baseline lines 139-141 Refine paragraph with:

```text
Steer Queued, Dispatched or Working tasks with -Refine <taskId> "one sentence".
Use -Reply for a Blocked task; -Refine returns 409 there.
```

These compress existing report/authority/refinement rules to pay for the new seat
instruction. Keep the allowed operational-autonomy pointer, stage/report tokens,
delegation requirement and every placement/pipeline rule.

### E-2: orchestrator skill

Replace the complete parking paragraph at
.claude/skills/antiphon-orchestrator/SKILL.md:25 with:

```text
A live Blocked child keeps its runner seat; an accepted Reply resumes work and does not itself free the seat. Answer it within the session or surface the missing decision before leaving; do not leave one overnight. At capacity, read GET /api/session-runners/{id}/slots: orphan=true is not a count of free seats; a live Blocked owner reads orphan=false with its park field. BlockedTaskParking:Enabled and TerminalRunnerSeatRelease:AutomaticEnabled default to false; require confirmed release before counting a seat as free. Parking never stops a Working session (CARD-1083).
```

Append this paragraph immediately below:

```text
Use -Reply for a Blocked answer, -Continue only for a question with standing authority, and -Refine only for Queued, Dispatched or Working tasks. Follow docs/session-runtime-invariants.md for released-park admission and prerequisite publication.
```

### E-3: owner docs and universal instruction

After the WIP occupancy paragraph in docs/orchestration-loop.md:789, and after the
Blocked-seat paragraph in docs/agent-card-lifecycle.md:15, add the same short paragraph:

```text
An accepted Reply changes a live Blocked task to Working; it does not itself free the runner seat. Use -Continue only for a Blocked question with standing authority; -Refine returns 409 on Blocked. While parking is disabled, do not assume an automatic release deadline.
```

After docs/session-runtime-invariants.md:78 add:

```text
Do not infer a release deadline from the 120-second idle proof, the 120-second reclaim interval, or the 600-second Held backoff. For a Blocked input wait, parking supplies no automatic release deadline while disabled; with its gates enabled, publication and conditional release evidence still control release.
```

Append to AGENTS.md's existing default-off session bullet at :73:

```text
Treat the park rule as conditional release, with no automatic deadline while parking is disabled.
```

The existing SeatIdle/SlotOrphan/SessionDisagreement names, process-release invariant,
Working veto, CARD-0079 exception and dormant-gate text stay. No new cancellation,
stop, kill, timeout override or permission rule is introduced.

### E-4: Plan and Review checklist

Replace only server/Bundles/stage-plan.md:11 with:

```text
Checklist, only when the plan adds or changes a session that waits for input: what releases a session that waits for input, and after how long? With parking disabled, no automatic release deadline exists (CARD-1083).
```

In server/Bundles/stage-review.md, replace its SCOPE line with:

```text
SCOPE: Re-run the claimed Unit and named integration checks in one checkpoint-tool run; PCs may remain pending. Match Code's CP-n lines to ### Checkpoints. Defects: missing row, zero count, unlisted build/test run without a reason, build or test driver outside the slot gate, broad run without named invariant/cost, or a test that cannot go red (self-compare, constant, no outcome assertion).
```

Replace its ROUND line with:

```text
ROUND: Follow the brief's verification profile. Final Review reruns the complete ordinary scope, including Interim deferrals. Require fresh identities and nonzero counts; exit 0, --list-tests or missing parameter rows are not evidence. Required manual work stays pending; nightly green cannot satisfy manual or PC checks.
```

Append one blank line followed by:

```text
For changed input waits, ask: what releases a session that waits for input, and after how long? With parking disabled, no automatic release deadline exists (CARD-1083).
```

Compression retains every named checkpoint defect, Full/Interim scope obligation,
nonzero/fresh evidence requirement and pending manual/PC rule. It changes neither
Review's authority nor ordinary scope; this task's narrower verification profile
is explicit in D-7. All stage text remains ASCII.

### E-5: surfaces already satisfied

No new sentences in server/Bundles/delegate-basics.md:24,
server/Bundles/board-api.md, .claude/skills/antiphon-delegate/SKILL.md,
docs/agent-instruction-file-contract.md or CLAUDE.md. Preserve their exact current
rules and include retention assertions for the basics push/exclusions/autosave
sentences, delegate skill Reply/Refine distinction and @AGENTS.md import.
Do not touch generated docs/cards files.

## Parking states and input-wait lifetime

This plan adds no session and changes no lifetime. It improves the checklist for
later designs; the truthful answer to that checklist is:

| State / condition | Interpretation and release boundary |
|---|---|
| Parking disabled (shipping default) | No new parking publication or automatic Blocked release deadline. Retained recovery of accepted answers/receipts continues as documented. A live seat remains occupied. |
| Requested | Current blocked episode registered; source publication is not yet established. |
| Published | Durable source/publication evidence exists; this alone is not physical release. |
| ReleasePending | Conditional release is reserved/in progress; reconcile exact evidence, do not count spare capacity. |
| Parked | Park episode records confirmed release; retain logical task/context/branch. Inspect the matching release receipt rather than only the enum. |
| Held | A prerequisite/evidence/authority refusal deferred parking; report the reason, not a release promise. |
| ResumePending | An accepted answer awaits continuation/admission; receipt and attempt fences still apply. |
| Resumed | The park records continuation into the subsequent attempt. |
| parkSync NotRequired/Pending/Ready/Held | Separate desktop source-sync state, never a substitute for physical release or answer admission. |

With gates enabled, release uses the existing conditional coordinator and exact
runner evidence, not an agent-issued stop. New park release needs Enabled and
AutomaticEnabled; legacy discovery also needs ReclaimExisting and a fresh
120-second proof window. The 120-second reclaim interval and 600-second Held
backoff are defaults, not deadlines. Missing proof can defer indefinitely.
Reply on a live task resumes on that seat; the admission-qualified released-seat
path retains an answer for a new attempt. A prerequisite landing/card Done alone
does not trigger it. No Code task may enable parking to test these instructions.

## Slices

### S1 — qualify owner and skill text (45 minutes)

Apply E-2/E-3. Files: AGENTS.md; .claude/skills/antiphon-orchestrator/SKILL.md;
docs/orchestration-loop.md; docs/agent-card-lifecycle.md;
docs/session-runtime-invariants.md. Extend
tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs with V-1/V-2.
Update only the superseded skill literal in
tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs
C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers; retain its default/gate/known-limit
and S3 pins. Tests: CP-1..CP-4, After=S1. Commit and push this slice before its run;
fix/recommit/rerun only a failing row.

### S2 — deliver missing launch rules and prove source-to-prompt parity (60 minutes)

Apply E-1/E-4 to server/Bundles/orchestrator.md, stage-plan.md and stage-review.md.
Extend InstructionBundleTests.cs and AgentBundleAttachmentTests.cs under
tests/Antiphon.Tests/Application as V-3..V-6 specify. Update the Plan checklist
literal in BlockedTaskParkProjectionTests.cs. Update only the intentionally changed
approved orchestrator hash in
tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs, after comparing
all E-1 replacements and computing the final file SHA-256. Keep the byte-hash assertion
literal and retain all size/headroom thresholds. No generator/service/project changes.
Tests: CP-5..CP-20, After=S2. Commit/push before the run. Preserve per-row source SHA,
counts, skipped/failure details and unedited checkpoint receipt lines.

## Activation and generation

Build/publish the server to regenerate its embedded bundle resources and automatic
content hashes. There is no independent bundle generator command and no checked-in
stamped prompt to regenerate. Any changed server/Bundles source (orchestrator,
stage-plan, stage-review here) requires the serving server/AppHost to load that new
assembly before it can compose the new instructions. After Review and landing,
the caller advances the canonical checkout and follows docs/apphost-runbook.md to
restart AppHost and verify /api/version SHA; never restart from this task worktree.

Fresh delegates then compose the new resource text. Standing agents refresh at
their policy-controlled idle window (Auto/Relaunch; Notify only reports drift; Off
does not refresh). Warm pool delegates keep their launch composition until retirement,
default 60 minutes idle. This is not a new input-wait release deadline. Nothing here
directs an agent to terminate a session or inject a replacement system prompt.
Urgent already-running work can receive the rule in its authorized brief/refinement.

AGENTS/owner/skill Markdown and documentation pins need no AppHost regeneration.
Those files must reach the target checkout and be read/imported by the agent;
existing sessions are not retroactively guaranteed to reread them. Preserve
CLAUDE.md's tracked @AGENTS.md import on every OS.

## Verification design

Evidence proves what agents are instructed and what the composer emits, not that
agents obey or that default-off runtime release has been activated. Existing runtime
tests are read-only truth references, not additions to this task's execution scope.

All text pins use independent expected literals from E-1..E-5 and identify file/key
in assertion messages. Normalize LF and collapse paragraph whitespace only for
wrapped prose; retain case, numbers, negation and command names. Do not derive the
expected sentence from the file under assertion, satisfy it through a plan quotation,
or accept an opposite instruction elsewhere on the same surface. Assert obsolete
misleading sentences absent on the edited surfaces.

| ID | Existing class / method to add or extend | Required assertions / regression purpose |
|---|---|---|
| V-1 | RunnerBranchContractDocumentationTests.C1083_OwnersKeepReplyAndReleaseDistinct (new) | Exact E-3 paragraphs in both owners, runtime timing warning and AGENTS qualification; retain default-off, Working veto, attention names, card-open/gate exclusion. Pin delegate skill's existing Blocked Reply requirement and CLAUDE.md as one @AGENTS.md import. |
| V-2 | RunnerBranchContractDocumentationTests.C1083_OrchestratorSkillKeepsBlockedSeatUntilConfirmedRelease (new) | Every E-2 sentence; reject "until it is answered or cancelled" and "seat stays until an operator enables"; retain orphan=false/park, no free-seat inference, overnight escalation and Working veto. |
| V-3 | InstructionBundleTests.C1083_OrchestratorExplainsBlockedSeatsAndAnswerVerbs (new) | Every E-1 replacement in the embedded orchestrator text; fail if any directive, condition or negation is removed/changed. Preserve report token, blocked-note fields and authority/delegation instruction. |
| V-4 | InstructionBundleTests.C1065_BlockedSeatInstructionsStayTrueWhileParkingIsOff (extend) | Keep each basics push/exclusion/resume/no-autosave pin; update Plan's condition and add Review's exact E-4 question/default clause. Assert old unconditional "Today nothing" absent in Plan/Review. |
| V-5 | AgentBundleAttachmentTests.C1065_BlockedSeatBundleAgreesWithTheBrief (extend) | Retain literal basics/Plan checks; compose real ForDelegate keys for Worker Plan, Worker Review and Orchestrator, plus board-api attachment. Assert literal relevant E-1/E-4 text in each resulting prompt, proper stage then basics order and each bundle once. Pure text/composition; no DB or session launch. |
| V-6 | InstructionBundleTests.C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders (new) | For orchestrator, delegate-basics, stage-plan, stage-review and board-api: read tracked source, normalize exactly LF+Trim, independently SHA-256 UTF-8 and take eight lowercase hex chars; compare resource text/version/header/rendered block and role-composed text. Fail stale embedded assembly/source mismatch. This checks generation, not two aliases of the same loaded object. |
| R-1 | BlockedTaskParkProjectionTests, both C1065 methods | Retain occupancy/gates/sync/reclaim/Working/known-limits/S3 pins while intentionally replacing only old skill and Plan wording. |
| R-2 | RunnerBranchContractDocumentationTests.C1082_settlement_sync_debt_is_documented | Preserve Pending versus Confirmed and lease-busy blocked/reply guidance beside new text. |
| R-3 | InstructionBundleTests existing size, budget, stage, build-slot, delivery-inventory and catalogue methods in CP-9..CP-14, CP-18..CP-19 | Real 500-char argv reserve; LF/simulated CRLF orchestrator cap; six ASCII stage cases; do not weaken old Review obligations or role map. |
| R-4 | CheckpointRepeatDocumentationTests.repeat_budget_reaches_code_briefs_without_bundle_growth | New independently reviewed literal orchestrator byte hash; keep all unrelated Code/repeat policy pins and caps. |
| R-5 | CheckpointManifestDocumentationTests (whole documentation class) | Keep checkpoint-tool, CP-n, every defect and required run/report policy despite Review compression. |
| R-6 | StandingPipelinePolicyDocumentationTests (whole documentation class) | Retain routing, concurrency and operational-autonomy boundaries adjacent to edited text. |

### Positive controls

Leave execution to post-land Mutation; Code/ordinary Review do not claim PC green.
Every control is method-scoped; a missing test, build error or zero tests is not red.
For each changed literal in a V row, delete it and separately invert its critical
clause (for example disabled/enabled, free/not free, orphan false/true, Continue
authority removed, Refine accepts/rejects, commit-before/after). Use the exact method
below without the checkpoint wildcard for each red/restore/green cycle.

| PC | Method | Required failure |
|---|---|---|
| PC-1 | RunnerBranchContractDocumentationTests.C1083_OwnersKeepReplyAndReleaseDistinct | Per-file E-3 removal/inversion and removed import/retained rule fail named owner assertions. |
| PC-2 | RunnerBranchContractDocumentationTests.C1083_OrchestratorSkillKeepsBlockedSeatUntilConfirmedRelease | Per-sentence E-2 deletion, answer-frees-seat or enabling-guarantees-release wording fails. |
| PC-3 | InstructionBundleTests.C1083_OrchestratorExplainsBlockedSeatsAndAnswerVerbs | Rebuilt E-1 deletion/inversion fails the embedded-text assertion. |
| PC-4 | InstructionBundleTests.C1065_BlockedSeatInstructionsStayTrueWhileParkingIsOff | Delete/invert each basics obligation/exemption or either scoped checklist/default clause; rebuilt pin fails. |
| PC-5 | AgentBundleAttachmentTests.C1065_BlockedSeatBundleAgreesWithTheBrief | In the mutation snapshot only, omit a relevant role bundle from the existing role map; composed instruction/order/presence assertion fails, then restore. |
| PC-6 | InstructionBundleTests.C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders | Change a source byte without rebuilding the already-built exact method to prove stale-source detection; separately rebuild with the render header/hash normalization mutated and require header/version failure. Freeze source for each run; restore before fresh green. |
| PC-7 | InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget | Add sufficient source text to exhaust the 500-char reserve; the real guard fails after rebuild. Do not raise its budget. |
| PC-8 | CheckpointRepeatDocumentationTests.repeat_budget_reaches_code_briefs_without_bundle_growth | Change one orchestrator byte with the expected literal unchanged; approved-byte assertion fails. |

### Manual acceptance and cost

Code/Review compare each changed sentence to the cited baseline code; retain the
remote-pool 422 and Known-limits sentences exactly. Check complete candidate diff
with scripts/check-evidence-diff.ps1. Verify source scope contains only the listed
docs/bundles/pins and no runtime/config changes. Record final normalized lengths and
actual hash, plus checkpoint executed/passed/failed/skipped counts; do not substitute
static measurements for runs.

Authoring: 105 minutes across S1/S2; ordinary checkpoint floor: 28 minutes (S1 8,
S2 20), plus host-slot queue time. No repetition after green without a demonstrated
failure or new change. Review runs the complete table against the final committed
source. PCs and AppHost activation remain separate caller work.

Run one checkpoint-tool invocation per committed After group, through the host gate:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-1083-docs -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-07-card-1083-blocked-seat-push-instructions-plan.md --after S1
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-1083-bundles -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-07-card-1083-blocked-seat-push-instructions-plan.md --after S2
```

Follow wait until exit is not 75; own every run to completion. Slot timeout is
not-run/blocked, never an unleased retry. The checkpoint runner gates each row;
use its isolated forward-slash output paths and remove only that run's owned bin
directories after receipts finish. Keep TRX/JSON/log/checkpoint output gitignored.
These commands name no fleet location. Trailing method wildcards below include
TUnit parameter expansion; Expect and Min guard empty or partial selection.

### Checkpoints

All rows use the **portable documentation lane (Linux or Windows; no OS pin)**.
The nine-column manifest has no invented Lane column. CP-1..4 close S1;
CP-5..20 close S2. Each named pin has its own row; selected bundle tests are text
contracts, and the only Integration-class selection is the pure C1065 pin described
above. No whole-Unit or runtime integration run is authorized.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1083-docs/` | owners | `/*/*/RunnerBranchContractDocumentationTests/C1083_OwnersKeepReplyAndReleaseDistinct*` | V-1 | exact 1, 0 failed/skipped | 1 | 5 |
| CP-2 | S1 | CP-1 | skill | `/*/*/RunnerBranchContractDocumentationTests/C1083_OrchestratorSkillKeepsBlockedSeatUntilConfirmedRelease*` | V-2 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-3 | S1 | CP-1 | existing-parking-docs | `/*/*/BlockedTaskParkProjectionTests/*` | R-1 | exact 2, 0 failed/skipped | 2 | 1 |
| CP-4 | S1 | CP-1 | settlement-docs | `/*/*/RunnerBranchContractDocumentationTests/C1082_settlement_sync_debt_is_documented*` | R-2 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1083-bundles/` | orchestrator | `/*/*/InstructionBundleTests/C1083_OrchestratorExplainsBlockedSeatsAndAnswerVerbs*` | V-3 | exact 1, 0 failed/skipped | 1 | 5 |
| CP-6 | S2 | CP-5 | checklist-and-push | `/*/*/InstructionBundleTests/C1065_BlockedSeatInstructionsStayTrueWhileParkingIsOff*` | V-4 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-7 | S2 | CP-5 | delivered-composition | `/*/*/AgentBundleAttachmentTests/C1065_BlockedSeatBundleAgreesWithTheBrief*` | V-5 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-8 | S2 | CP-5 | generated-parity | `/*/*/InstructionBundleTests/C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders*` | V-6 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-9 | S2 | CP-5 | orchestrator-size | `/*/*/InstructionBundleTests/orchestrator_bundle_points_to_operational_autonomy_without_growing*` | R-3 | exact 2, 0 failed/skipped | 2 | 1 |
| CP-10 | S2 | CP-5 | stage-ascii-size | `/*/*/InstructionBundleTests/each_stage_bundle_is_ascii_and_under_the_size_cap*` | R-3 | exact 6, 0 failed/skipped | 6 | 1 |
| CP-11 | S2 | CP-5 | argv-reserve | `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget*` | R-3 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-12 | S2 | CP-5 | approved-bytes | `/*/*/CheckpointRepeatDocumentationTests/repeat_budget_reaches_code_briefs_without_bundle_growth*` | R-4 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-13 | S2 | CP-5 | stage-invariants | `/*/*/InstructionBundleTests/stage_bundle_invariants_are_pinned_by_substring*` | R-3 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-14 | S2 | CP-5 | build-slot-contract | `/*/*/InstructionBundleTests/C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces*` | R-3 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-15 | S2 | CP-5 | delivery-contract | `/*/*/InstructionBundleTests/C467_V21_DeliveryInventoryAndReviewAreMandatory*` | R-3 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-16 | S2 | CP-5 | checkpoint-docs | `/*/*/CheckpointManifestDocumentationTests/*` | R-5 | all listed, 0 failed/skipped | 1 | 1 |
| CP-17 | S2 | CP-5 | pipeline-docs | `/*/*/StandingPipelinePolicyDocumentationTests/*` | R-6 | all listed, 0 failed/skipped | 1 | 1 |
| CP-18 | S2 | CP-5 | catalog-contract | `/*/*/InstructionBundleTests/the_catalog_holds_exactly_the_bundles_that_ship*` | R-3 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-19 | S2 | CP-5 | attachment-only-board | `/*/*/InstructionBundleTests/the_board_api_bundle_is_on_no_role_by_default*` | R-3 | exact 1, 0 failed/skipped | 1 | 1 |
| CP-20 | S2 | CP-5 | updated-parking-docs | `/*/*/BlockedTaskParkProjectionTests/*` | R-1 | exact 2, 0 failed/skipped | 2 | 1 |
