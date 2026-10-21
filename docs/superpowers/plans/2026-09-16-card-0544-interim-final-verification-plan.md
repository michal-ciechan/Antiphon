# CARD-0544: explicit interim rounds and full final verification

Date: 2026-09-16. Stage: Plan. Source inspected: `2c1a7a08613dd25152dff9586d242ffbb214b56c`.
Separate TestDesign follows this plan; this dispatch does not fold that stage.

Current disposition (Plan task 39855344, 2026-09-16): TD-F1 reuses the existing
CARD-0481 notification outbox/recovery owner, with a completion-specific payload
contract. TD-F2 is explicitly deferred to **CARD-0545**, the same-board CARD-0487
S4 follow-up. See [the resolution appendix](#plan-resolution-of-td-f1td-f2), which
supersedes conflicting scope/handoff statements below. Return to TestDesign;
the existing verification appendix is not yet a Code-ready specification.

Introduce an explicitly requested, per-card/per-role interim verification mode for
repair work. Preserve full ordinary verification for the initial baseline and final
pre-land review. Complete and qualify the existing Windmill backstop before allowing
interim dispatches. An interim pass means the selected checks passed; it never means
the full regression obligation was discharged.

This is a design artifact, not activation. No production policy, schedule,
service, source code or test behavior is changed. The resolution dispatch created
the required CARD-0545 tracking card without spawning work. The choices below
are selected within the commissioned brief, including its authorization to choose
the scheduling approach; no unresolved product decision blocks TestDesign.

## Evidence and ground truth

Primary investigation: [CARD-0544 findings](../../investigations/2026-09-16-card-0544-scope-down-per-round-pipeline-verification-batch-full-regression-sweep-nightly-instead-of-per-round.md).
Its retained [evidence directory](../../investigations/2026-09-16-card-0544-evidence)
contains the task census, run timings, Windmill census and CARD-0487 close revision.
The live census belongs to that investigation at its stated observation times;
this Plan did not repeat live queries or assume its checkout was the running server.

| Card assumption / proposed behavior | What this checkout actually does | Consequence |
|---|---|---|
| Verification can already distinguish repair and final rounds. | `stage-code.md` mandates Unit plus named affected integration classes and every V/R; `stage-review.md` independently reruns them. `CreateAgentTaskRequest`, `AgentTask` and `Card` contain no ordinary verification mode. | Add an explicit contract and durable task metadata; prose such as “quick repair” must not select reduced scope. |
| Task success establishes full coverage. | `AgentTaskReplyService` checks progress/report completion, not a test manifest. Initial CARD-0527 Code succeeded with omitted ordinary checks; first Review found two real Unit regressions. | Keep initial/final broad execution and distinguish coverage scope from task success and finding verdict. |
| “Full sweep” means every repository test on each task. | `docs/testing-and-build.md` Fast lane means the whole Unit lane plus all named affected integration classes and the plan's other ordinary V/R, with broader exceptions justified in advance. | Final means this existing affected-scope contract, not an unconditional full-assembly/E2E run. Nightly owns the broad unattended repository universe. |
| The landing verifier proves full ordinary completion. | `AgentTaskLandService.RequestAsync` permits explicit-caller approval without `ReviewEvidenceId`; `VerifyWithObserverAsync` can be build-only. `LandApproval` validates supplied clean Review identity, not scope. | Add a narrow approval guard for work that used interim scope. Preserve existing `-Verify` execution and rebase rules. |
| A clean Review outcome necessarily recommends land. | `StageOutcomeKind.Clean` records the finding; `ReviewEvidence` binds owner/SHA/ref/repository. Neither currently records full versus interim checks. | Store scope separately. A clean interim Review is useful history, but not final approval. |
| PC deferral is the scheduling model for nightly. | SourceLanding is an explicit Worker/Mutation/Worktree dispatch bound to one confirmed landing operation and immutable landed SHA. No tick commissions it. | Keep PCs separate; neither interim checks nor a nightly run can discharge PC obligations. |
| Windmill nightly already protects reduced dispatch. | Investigation found zero nightly scripts/schedules/jobs. CARD-0487 closed with S4 qualification and dispatch-scoping work unfinished. Checked-in payloads and native infrastructure exist. | Commission completion of S4, not a replacement scheduler or a claim that Done means deployed. |
| The nightly execution universe omits three native projects. | The introductory historical audit in `testing-and-build.md` still says this, but current `tests/test-execution-policy.json` includes Antiphon, SessionRunner, PtyHost, Agents.Pty, Messaging, client and scripts. E2E remains manual. | Correct the stale present-tense documentation in the implementation slice; do not rebuild already-shipped suite inventory. |
| A current monitor result permits daytime deferral. | `Test-NightlyMonitorHealth` computes `ReadyForDeferral` using the age of `NativeState.completedAt` against `MonitorStaleMinutes=60`; it does not measure the saved monitor's `RecordedAt` age. | Separate daily run validity from monitor freshness before activation; otherwise permission expires about an hour after the nightly finishes. |
| A server2 tag proves independent monitoring. | The checked-in health wrapper is tagged `default`, but SSHs to the same Windows host to run `nightly-health.ps1`. | Qualification must prove detection/receipt when that host or SSH is unavailable; a tag alone is insufficient. |
| The measured saving is 234 minutes or a known dollar amount. | Nine repair tasks used 233.90 first-matrix minutes within 459.43 task minutes. Later incremental body sums were about 1-4 minutes, excluding standalone startup, smoke, build and final amortization. | This motivates the experiment; it does not establish a counterfactual wall-time or token saving. |

Additional source inspection covered `InstructionBundles.ForDelegate`,
`DelegationReportFormatter.BuildBrief`, task create/follow-up/repair binding,
`CardService.UpdateContentAsync` and revision storage, Review evidence parsing and
settlement, landing admission, nightly monitor logic/payloads, and the existing
approval, parser, CLI-stub and composed-instruction test fixtures. TestDesign must
inspect the remaining test/helper bodies before defining executable cases.

## Decisions

### D-1: full by default; opt-in is both per card and per role

Add `CodeVerificationPolicy` and `ReviewVerificationPolicy` to cards, each
`FullOnly` (default) or `AllowInterim`. These permit an interim request; they do
not make interim the default. Expose them through the existing card content PATCH,
GET and revision history, with normal board scoping and concurrency-token behavior.
`card.ps1 edit` gets corresponding arguments. Tracker imports and new cards retain
`FullOnly`; tracker synchronization must not infer or overwrite this local policy.

Add `-VerificationRound Final|Interim` to `delegate.ps1` and the typed create DTO.
Omitted on Code/Review means Final. Other roles have no ordinary-round profile;
explicit round arguments on them are invalid, including Mutation and SourceLanding.
Initial support is Worker Code/Worktree and Worker Review/ReadOnly for Interim.
Existing Shared/helper/final workflows keep their existing behavior.

Reject a requested Interim when disallowed; do not silently turn it into a more
expensive Final dispatch. A caller can explicitly resubmit Final. An operator
deployment switch is an additional readiness gate, never a global opt-in for cards.
One role can stay FullOnly while the other permits Interim.

**Reason:** the delayed-regression-detection tradeoff must be visible and reversible.
**Rejected:** a global bundle change reducing all Code/Review, policy inferred from
titles/labels, using collision `Scope` as test scope, and silently inheriting an
interim mode onto a fresh follow-up.

### D-2: explicitly mark each round; retain an initial full baseline

Final is the full-verification contract and an intention to become land-ready if
clean, not a prediction that no defect will be found. The first Code and first
Review therefore run Final. An Interim requires a referenced full Review baseline
for the same original Code landing owner, repository/project and card.

Introduce `VerificationSubjectTaskId` and `VerificationBaselineOutcomeId` on the
request/task, exposed as `-VerificationSubject` and `-VerificationBaselineOutcome`
full-GUID CLI arguments. For Interim both are required. The baseline must be a completed
delegate Review with explicit full-scope execution evidence. A Found full Review
can establish the baseline: it may have discovered the very regressions now being
repaired. Found is never approval to land. Failed/canceled/incomplete, legacy
unknown-scope, manually overridden or foreign-owner outcomes cannot be baselines.

Reuse the baseline's reviewed SHA as the delta base. At source preparation verify
it belongs to the same source history; if ancestry cannot be established, require
a new Final review. Do not silently substitute HEAD or another same-card branch.
For repair tasks the subject must equal `RepairSourceTaskId`; for follow-ups it
must resolve to their original landing owner. A review of a repair still names
that original owner. Resolve and authorize these links through existing services.

No automatic “two clean rounds implies final” counter in v1. Clean means no finding
within the selected scope, and retry/parallel-task history is not a reliable
definition of a repair round. The caller explicitly commissions Final when ready.
An optional periodic forced-full heuristic is deferred until there is measured
experience with the explicit policy.

### D-3: interim selection is cumulative changed coverage plus named smoke

The plan/round brief records a selection table with these columns:
`V/R ID`, `project`, `class/method or client/script case`, `reason`, `filter/command`,
`expected expanded rows or checked case inventory`, and `interim/final/manual`.
Record the baseline outcome/SHA and selection artifact revision in the task brief
and final evidence. Code updates the selection when implementation changes it;
Review compares the resulting delta and evidence, rather than trusting test names.

The Interim request carries `VerificationSelection` with `artifactPath` (a
repository-relative `docs/**/*.md` path), `artifactCommitSha` (full object ID) and
`section` (the round selection anchor). `-VerificationSelectionFile` reads that
small JSON object from a caller-owned file; only its contents enter the API. Validate
the reference against the prepared authorized repository, not an arbitrary host path.
Snapshot the initial reference and retain any authored replacement reference in the
raw result; Review checks the replacement and actual delta. Existence/nonempty rows
is an admission check, not a machine judgment of sufficient smoke coverage. Final
uses the full plan's selection and does not require an interim reference.

An Interim executes:

1. Every new or changed ordinary case in the cumulative delta since the full
   baseline, including earlier repair cases, plus tests for all unresolved findings.
2. Exact named smoke methods in immediately adjacent affected classes. Select
   representative success, boundary/failure and persistence/delivery paths according
   to the changed dependency, including a real native/Git capstone when that boundary
   is affected. The smoke list is authored from test bodies, not “one arbitrary test
   per class,” `Slow` exclusion, or unchanged test-file text.
3. Required targeted manual/E2E acceptance when the changed behavior belongs to
   an excluded nightly lane. A required unavailable check remains pending.

It explicitly skips the whole Unit lane and full affected-class sweep unless those
are themselves the smallest defensible affected selection. It does not skip build,
fresh/nonzero execution evidence, guard inventory, or investigation of a new red.
Use exact-method selectors and the established client/script runners; combine
methods only using a selector whose executed identities TestDesign has proved.

Target smoke at roughly 2-5 minutes of test-body work as a planning estimate, not a
timeout or a coverage cap. If an affected shared helper, fixture, schema, admission,
recovery or delivery boundary cannot be bounded, select Final instead of weakening
its guards. Slow tests remain mandatory when they are needed to prove the change.

Report executed IDs and counts separately from `deferred-to-final` IDs/classes;
name the future Final Review obligation. Never mark deferred ordinary work passed
or move it to the PC companion. A zero-test filter or missing parameter rows does
not prove selection. This is a reviewed selection contract, not an AST-based test
impact engine or a server audit of every TRX.

### D-4: final means the complete affected sweep, even after a clean interim

Final Code runs all ordinary V/R, the full Unit lane, full affected integration
classes, applicable client/scripts and required manual acceptance in the plan.
Final Review independently reruns that same complete ordinary scope, including
the rows deferred by earlier rounds. It reviews the cumulative implementation
against the plan and judges pending PCs without executing them.

If an Interim Code or Review unexpectedly finishes the last repair, dispatch a
fresh Final Review on its unchanged candidate SHA. That Review can discharge all
remaining ordinary checks; do not create a no-change Code task that collides with
the existing attributable-progress requirement. Additional production/test edits
invalidate the review identity and require ordinary verification/Review again.

An interim task cannot relabel itself Final in its report or through Continue.
Retries/continues preserve its snapshot; a fresh task defaults to Final. An Interim
Review with no findings returns `next: review` with an explicit Final handoff;
with findings it returns `next: code`. Final clean Review returns `next: land`.
Code still returns `next: review`, carrying mode and pending full-scope work.

**Rejected:** replacing final verification with a scheduled master run, accepting
several clean narrow rounds as equivalent to full coverage, or forcing gratuitous
extra Code changes just to get a Final task. Nightly master is a different source
identity and cannot approve an unlanded candidate.

### D-5: make interim approval impossible to mistake for final approval

Extend the existing raw `--- review evidence ---` block with
`ordinaryScopeCompleted: Full|Interim|None`. Full means the full required selection
actually executed, not that all assertions passed; the existing Clean/Found verdict
still determines success. Missing, duplicate or malformed scope is Unknown and
cannot establish a baseline or satisfy the new final gate. The report must still
give commands, exact SHA, counts, skipped/deferred cases and artifact paths.

Persist the task profile and completed scope on StageOutcome. The scope is limited
by the commissioned profile: an Interim report claiming Full cannot mint a Full
outcome. Parse authorized subject identity for both Found and Clean delegate Review
outcomes, preserving the existing Clean-only approval rule. Manual finding overrides
and backfill never manufacture/copy this new full-scope evidence. Historical rows
remain Unknown, not retroactively certified Full.

Latch `RequiresFinalVerificationReview` on the original owner atomically when any
Interim Code or Review is admitted for it. This survives policy disablement, repair
integration, cancellation and restarts. Serialize with the owner's land-admission
lock. Refuse new Interim work once a land is pending or publication is confirmed;
do not race a verification downgrade against publication.

For a latched owner, fresh land requires `ReviewEvidenceId` for a completed,
unsuperseded, Clean **Final/Full** Review matching the expected source SHA, owner,
ref and repository under existing authorization. No explicit-caller/no-evidence
fallback. Supplying an Interim Review as approval is refused for any owner.
Pending/recovered land requests must validate the persisted requirement/evidence
again before publication; retries cannot evade it. Cleanup-only retries after
confirmed publication retain the existing receipt and require no new test sweep.

Unlatched legacy/full-only flows retain CARD-0488's explicit-caller compatibility.
Do not change `LandVerifyFilter`, default rebase/build verification, publication
identity, or SourceLanding custody. This guard authenticates the scope declaration
and source identity; it does not independently prove that a delegate ran its tests.
Ordinary Review still has to assess the actual evidence.

**Rejected:** a prompt-only interim marker, which could leave a clean narrow Review
usable by the existing approval gate, and a repository-wide new TRX-completeness
engine, which is unnecessary to enforce this scope/identity distinction.

### D-6: finish Windmill S4; do not invent a periodic agent role

Use `u/lndcobra/antiphon_nightly_tests` at 00:30 Europe/London, the dedicated
`C:\Antiphon\nightly\checkout`, and `u/lndcobra/antiphon_nightly_health` every
30 minutes off the desktop queue. Reuse CARD-0487's suite inventory, complete-green
predicate, reporting, clone/lock ownership and execution isolation. Do not reimplement
S1-S3 or activate CARD-0487's earlier blanket reduced-dispatch proposal: this plan's
explicit initial/interim/final contract replaces that unshipped policy.

The caller commissions a bounded Deploy/qualification task for CARD-0487 S4 after
prerequisite fixes land. Link its evidence to CARD-0544. Reconcile the existing card
thread before using a reopened CARD-0487 or linked same-board follow-up; do not create
duplicates or claim this Plan has already commissioned/spent that work. The deployment
task registers/read-backs the checked-in definitions using existing authorized
credentials and permissions. No local Scheduled Task, new role or tick-based spend.

Qualification must record a manual full unattended green, a subsequent real scheduled
master green, all expected suite/case counts, SHA/ref/policy/script hashes, job/run IDs,
wall times, explicit manual exclusions, independent health/failure-recovery evidence,
and actual receipt at the authorized notification destination. Retain the committed
`docs/investigations/<date>-card-0487-nightly-qualification.md` specified by that plan.
Missing credentials or recipient authorization blocks that operational step, not
the design; never create credentials or send an unsolicited test message to bypass it.

Before qualification, repair the observed readiness time conflation. A saved monitor
is fresh only when its `RecordedAt` is no more than 60 minutes old (future timestamps
invalid). Daily run validity is evaluated separately against the London scheduled due
date and existing morning deadline. Before today's 08:00 deadline, the previous due
day's complete scheduled green may bridge while today's run is pending; a known newer
failed/incomplete completed attempt removes permission immediately. At/after 08:00,
today's complete scheduled green is required. A missing/stalled/overdue job remains
unhealthy under existing start/progress rules. A manual/partial run cannot refresh
the scheduled-green identity. Test midnight, deadline and both DST boundaries.

Qualify the production job/status/result adapters, not just seam fixtures. The health
wrapper's server2 placement must detect and deliver an outage when the Windows SSH
hop fails; arrange an independent Windmill failure path or move the evaluator there.
Retain this as S4 acceptance with a prerequisite repair if needed, not an assumed
capability of the current wrapper. Do not change runner watchdogs to manufacture green.

**Why Windmill:** most code and an operations precedent already exist. Remaining work
is repair, deployment and acceptance. An Antiphon-scheduled prompt/card sweep would
still need a suite/evidence collector, health monitor, provider capacity, spend
acceptance and attribution. SourceLanding cannot represent a multi-land master sweep.
That alternative adds failure modes without eliminating qualification work.

### D-7: readiness is checked, recorded and allowed to fail closed

Add typed `InterimVerificationSettings`, disabled by default, bound to an explicitly
configured canonical repository/project and the trusted nightly state root. Other
repositories remain FullOnly until separately qualified. Do not accept state-root
paths, health results or a caller-provided “ready” boolean in task creation.

A small infrastructure reader behind `IInterimVerificationReadinessReader` reads a
deployment-owned qualification receipt plus `last-monitor.json`. The receipt cites
the committed qualification artifact/full commit, repository/project, policy/script
hashes and qualifying run/job/recipient evidence IDs. S4 publishes it only after
acceptance. It is an operator attestation backed by retained artifacts, not proof
derived from a markdown filename. Monitor output must carry the matching identities
and scheduled run ID, in addition to RecordedAt and ReadyForDeferral, so the reader
can reject wrong-repository, stale, missing, malformed or mismatched evidence.

Validate readiness, card role policy and baseline at create and again before a queued
Interim starts. Snapshot the accepted profile, card policy revision, baseline, selection
reference and readiness IDs/time on the task. Use a bounded read, no Windmill network
call or full test run in API admission. Failure at create is a 409 with a stable
reason; loss while queued holds the task using existing blocked/attention behavior.
Never launch a differently scoped task or spend on an automatic Final replacement.

Running tasks finish owned commands normally if health changes. Preserve their Interim
history and mandatory Final obligation; deny subsequent Interim dispatch while the
backstop is unready. Final dispatch does not require nightly health. Disabling interim
policy cannot clear an existing owner's final-review latch.

### D-8: keep delivery, manual acceptance and mutation obligations explicit

No new stage enum, card status, auto-close rule, scheduled agent or automatic Mutation
dispatch. Keep the ordinary same-board post-land companion and explicit SourceLanding
flow. Every PC/variant stays pending until method-scoped Mutation proves it at landed
L; a scheduled ordinary green is not PC evidence.

The nightly universe remains all current unattended suites. E2E, headed/provider/live
cases keep their eligibility and production-runner safeguards; list them separately
and run affected required acceptance before final approval. Expanding unattended E2E
would be its own isolation/eligibility design, not a switch added here.

Brief composition must reach fresh, warm, follow-up and spilled-pointer deliveries.
Put the current task profile in `DelegationReportFormatter.BuildBrief`, not only the
launch-time bundle, because warm agents retain old bundles. Update conflicting bundle
rules, especially `RUN THE FULL SUITE ONCE, THEN TARGET`, to defer to the explicit task
profile while preserving full-default behavior. Keep source files ASCII and stage
bundles within the existing length limit. No new message-delivery channel is needed.

### D-9: ordinary completion recovery reuses the CARD-0481 outbox

For new version-1 Code/Review profiles, commit a `Completion` notification with
the settlement event, raw result and stage outcome. Reuse
`AgentTaskLandNotificationService` and its hosted scanner, unique queue key and
recipient proof. Preserve the semantic completion snapshot separately from its
authorized raw/distilled/pointer rendering; the resolution appendix specifies
the transaction, identity and recovery contract.

**Reason:** this machinery already survives the exact lost-insert/lost-ACK cuts
and validates destination/digest even after the caller stops. **Rejected:** a
second completion outbox/worker, broadening the SourceLanding-only Result scan,
or treating `CompletionNoteQueuedAt`, polling or Sent as recipient receipt.

### D-10: TD-F2 is deferred with an activation-blocking tracking card

CARD-0545 owns the independent watchdog, production recipient reader and full
CARD-0487 S4 qualification. CARD-0544 owns dormant policy/delivery/readiness code
and retains S6; accepted CARD-0545 evidence is required before S6 can start.
The detailed ownership and guard transfer below are required work, not exclusions
from the overall operating policy.

**Reason:** the checked-in health wrapper and receipt-file seam do not supply an
independent Windmill outage path or a production recipient reader. Selecting and
qualifying an external host/transport is a separate operational prerequisite.
**Rejected:** self-monitoring through Windmill's own jobs/on_failure, a server2
tag as proof of independence, a fabricated receipt file, or enabling Interim
while qualification remains deferred. No new default is awaiting human choice;
the brief explicitly permits this tracked deferral.

## Contract and lifecycle summary

| Situation | Ordinary execution | Allowed continuation |
|---|---|---|
| No policy/flag, new card, or Final explicitly requested | Existing full affected sweep | Code -> Review; clean Final Review -> Land |
| First Code/Review on an opted-in card | Full; no eligible baseline yet | Findings can establish a full execution baseline for repairs |
| Explicit Interim with role permission, full baseline and ready backstop | Cumulative changed/new cases + named adjacent smoke + required targeted manual checks | Code -> Review; clean Interim Review -> Final Review; findings -> Code |
| Missing selection or unbounded shared impact | Interim cannot claim completion; commission Final | No land-ready claim |
| Requested Interim but disabled policy/unready backstop/invalid baseline | Refuse before launch, or hold if eligibility changed in queue | Explicit Final resubmission or readiness repair |
| Final Review of a previously interim-tested SHA | All full ordinary obligations, independently executed | Clean full evidence can approve original owner without no-op Code |
| Latched owner and absent/interim/stale/wrong-SHA approval | Refuse land before publication | Fresh matching Final/Full clean Review |
| Confirmed publication | Existing cleanup/deploy/companion flow | Explicit SourceLanding Mutation; periodic ordinary sweep is separate |

Suggested stable refusal codes: `verification_round_role`,
`verification_interim_disallowed`, `verification_baseline_invalid`,
`verification_backstop_unready`, `verification_owner_landing`,
`final_verification_review_required`, and `review_verification_scope_ineligible`.
Use existing typed Validation/Conflict exceptions and caller visibility, not raw HTTP
status construction or a new alert sink. Syntax/type failures are 422; eligibility
and readiness conflicts are 409. GET/CLI status and the task drawer show commissioned
scope, baseline, deferred-final obligation and readiness/refusal reason.

## Implementation slices

Each slice is independently committed/pushed with its actual verification outcome.
S1-S4 ship dormant; S5 must qualify before S6 enables a pilot. These are file and
test targets, not a replacement for the separate TestDesign's executable V/R/PC matrix.

### S1: persist explicit policy and round identity

Files: `server/Domain/Entities/{Card,CardRevision,AgentTask,StageOutcome}.cs`, new
`server/Domain/Enums/VerificationRound.cs` and `CardVerificationPolicy.cs`,
`server/Application/Dtos/{BoardDtos,AgentTaskDtos,StageOutcomeDtos}.cs`,
`server/Application/Services/{CardService,CardRevisionLog,AgentTaskService}.cs`,
`server/Infrastructure/Data/AppDbContext.cs`, CLI-generated migration/model snapshot,
`scripts/{card,delegate}.ps1`, and `client/src/api/{boards,agentTasks}.ts`.

Add task profile version 1, nullable legacy scope, subject/baseline identities,
immutable admission/readiness snapshot and owner latch. New tasks explicitly snapshot
Final when omitted; never migrate historical evidence to Full. Preserve policy in
all relevant card projections/revision reads without publishing internal readiness
paths into generated card descriptions. Omitted PATCH fields preserve values.

Tests: new `tests/Antiphon.Tests/Application/CardVerificationPolicyTests.cs` and
`DelegateScriptVerificationRoundTests.cs`; use existing `DelegateCreateStubApi` /
`DelegateScriptRunner`, `AgentTaskCardBindingTests`, `DelegateScriptRepairSourceTests`
and isolated-schema patterns. Cover board isolation, revision/concurrency behavior,
unknown enum/wrong role, legacy defaults, CLI request/refusal and tracker non-overwrite.

### S2: resolve admission and preserve profiles through every dispatch path

Files: new `server/Application/Services/InterimVerificationPolicy.cs`,
`server/Application/Settings/InterimVerificationSettings.cs`,
`server/Application/Interfaces/IInterimVerificationReadinessReader.cs`,
`server/Infrastructure/Files/InterimVerificationReadinessReader.cs`,
`server/Application/Services/{AgentTaskService,AgentTaskDispatcher,AgentTaskReplyService,DelegationReportFormatter}.cs`,
and `server/Program.cs`/existing composition root wiring as appropriate.

Resolve owner/baseline, serialize latch versus land admission, validate prepared source
history, and recheck queued eligibility. Preserve profile through retry/continue/
reroute/escalation/repair; fresh follow-ups must request Interim again. Include the
resolved profile in every brief and status representation. Do not trust Goal text to
select scope. Use an interface only for the external file read; policy logic is concrete.

Tests: new `InterimVerificationPolicyTests.cs`, `InterimVerificationReadinessTests.cs`
and `VerificationRoundDispatchTests.cs` under `tests/Antiphon.Tests/Application/`;
extend `DelegationReportFormatterTests` in `DelegationUnitTests.cs`, plus relevant
`DelegateBundleLaunchTests` / `CodexDelegateDispatchTests`. Required boundaries include
Found-full baseline, invalid/foreign/legacy baseline, changed ancestry, stale health,
queue policy change, restart, warm reuse, file-spilled brief, and land/create races.

### S3: retain review scope and guard final approval

Files: `server/Application/Services/{ReviewEvidence,AgentTaskReplyService,StageOutcomeService,LandApproval,AgentTaskLandService,AgentTaskLandingProtocol}.cs`;
extend outcome DTO/projections and persisted request fields only where recovery needs
the same immutable approval identity. Preserve CARD-0488 and CARD-0478 boundaries.

Tests: extend `ReviewEvidenceParserTests`, `AgentTaskReviewEvidenceTests`,
`AgentTaskLandApprovalRequestTests`, `AgentTaskLandApprovalPersistenceTests` and
`AgentTaskLandApprovalRecoveryTests`; add `InterimVerificationLandGuardTests.cs` for
the latched-owner flow. Read real fixtures before selecting additional controlled/
real-Git capstones. Prove no request/publication on refused approval, restart replay,
no-evidence and override bypass rejection, fresh Final promotion, changed SHA refusal,
and unchanged cleanup-only retry/legacy full-only admission. A parser-only assertion
cannot prove settlement or landing enforcement.

### S4: author selection/reporting contracts and readiness fixes

D-10 amendment: the independent watchdog/recipient-adapter portion below belongs
to CARD-0545. CARD-0544 retains the clock, identity and readiness-consumer changes;
the resolution appendix gives the exact split and keeps activation disabled.

Files: `server/Bundles/{stage-code,stage-review,stage-test-design,delegate-basics,orchestrator}.md`,
`stage-plan.md` only if needed for folded designs;
`docs/{testing-and-build,orchestration-loop,agent-card-lifecycle,ops-http,antiphon-api}.md`;
`client/src/features/delegations/{TaskDetailBody,TaskDrawer}.tsx` for read-only profile
visibility; `scripts/lib/nightly-health.ps1`, `scripts/nightly-health.ps1`,
`scripts/test-nightly-health.ps1`, and relevant `scripts/windmill/` payloads/README.

Reconcile all unconditional “every V/R now” rules with D-3/D-4 without changing PC
ownership. Supply worked selection examples for narrow repair, shared landing helper,
transcript/manual boundary, missing selection and unready nightly. Fix monitor/native
age conflation, persist matching monitor identities, and implement the independent
outage path if production qualification cannot prove the current one. Keep activation off.

Tests: `ScopedVerificationInstructionTests`, `InstructionBundleTests`, composed brief
tests, `TaskDetailBody.test.tsx`/`TaskDrawer.test.tsx` and the offline nightly health
harness. Update stale text assertions rather than preserving contradictory wording
(e.g. `C487_G142` still expects Code -> Mutation although the current bundle says
Code -> Review). Verify behavior through dispatch/settlement tests, not solely prose
checks. Expand run/report harness selection only if shared nightly helpers change.

### S5: deploy and qualify the existing nightly backstop

D-10 amendment: **CARD-0545** is the commissioned-work tracking owner for S5.
It is Backlog, not an authorized running deployment. S5 remains an acceptance
dependency of CARD-0544 S6 and operating-policy completion.

Commissioned operational slice: deploy/read back the two Windmill definitions, execute
CARD-0487 S4 acceptance, publish its qualification artifact and trusted readiness
receipt. Target existing `scripts/nightly-{run,tests,report,health}.ps1`, their helpers,
`tests/test-execution-policy.json`, and `scripts/windmill/`; change source only through
a separately reviewed prerequisite repair if qualification finds defects.

Acceptance: native suites sequential, fresh result inventories/counts, full manual
and real scheduled greens, monitor hash/run correlation, failure and recovery receipt,
Windows-host outage, due-date/DST behavior, preserved manual exclusions and current
healthy readiness. Inherited test failures must be repaired or separately resolved;
relaxing assertions, adding retries or relabeling required rows excluded cannot qualify.
Allow a 2-6 hour observation window per full run, plus the overnight schedule boundary;
these are inherited planning estimates, not measured durations or permission to extend
test deadlines. Evidence must distinguish offline adapter tests from real receipt.

### S6: explicitly enable a small pilot and record results

After S1-S5 and ordinary Review, deploy the correct server SHA/capability, enable the
repository readiness gate and opt in one selected repair card/role through a revision.
Record the actual flags, baseline, current monitor and final-review owner. Existing
cards remain FullOnly. Use the first pilot with bounded impact; keep native/shared
safety work FullOnly unless its TestDesign gives a defensible smoke selection.

Artifact: `docs/investigations/<date>-card-0544-verification-pilot.md`. Record candidate
SHA and environment; compare interim and full selections on the same committed source
where practical, including build, discovery/setup, execution, smoke, final cost and
counts separately. Reuse retained full results only with exact identity and conditions;
do not claim a timing comparison across unrelated revisions. Token/dollar attribution
requires command-correlated usage; absent that, report wall time only.

Acceptance includes at least one refusal of interim approval followed by a real Final
Review and successful normal land on an isolated test repository, plus production
pilot evidence that deferred cases ran in Final. No deliberate mutation in the shared
pilot source. Roll back by setting role policies FullOnly and disabling new Interim
admission; preserve evidence/latches and require Final for already-reduced work. Keep
nightly running. No destructive data rollback or mass card edits.

## Scope boundaries

Do not split test assemblies, remove retained real-Git/native capstones, change
assertion deadlines, add retry-to-green behavior, expand unattended E2E, auto-commission
Mutation, or replace the existing scheduler. No automatic test-impact inference or
change to what a landed SHA means. Nightly repairs discovered beyond the named
readiness/independence seams need their own bounded repair/review before qualification.

## TestDesign handoff

Append the required `## Verification design` to this file in the next stage. Inspect
test/fixture bodies, then define concrete V/R, guard inventory, one distinct executable
PC per independent safety guard, delivery inventory and numeric ordinary/PC costs.
At minimum cover: default/role/card opt-in, baseline/subject/source binding, current
readiness, queue/retry preservation, owner-latch atomicity, report scope confinement,
approval bypass/replay, monitor freshness versus daily validity, and failure receipt.

Selection must name the full affected classes for this implementation as well as a
possible later interim subset. This card itself stays FullOnly through qualification;
it cannot use its unproven policy to skip its own required evidence. Do not inherit
CARD-0487's entire 143-PC matrix automatically: identify changed guards and preserve
the existing companion's independent unfinished obligations.

Budgeting for design: S1-S4/S6 span DB, dispatch and landing integration and a monitor
adapter; estimate 10-20 minutes build/setup and 15-35 minutes ordinary targeted/full
affected checks per initial/final stage, excluding native capstones whose fixtures
TestDesign must price. Separate the post-land PC floor and S5 operational hours.
These are unmeasured planning ranges, not savings claims or an executable test budget.

Completion of implementation requires the dormant code and full ordinary Review,
qualified Windmill, explicit pilot activation with preserved full final evidence,
and linked post-land PC obligations. Shipping dormant code is a useful slice, but is
not completion of CARD-0544's operating policy. Qualification blockers leave that
activation obligation visible rather than silently switching to an agent scheduler.

## Plan-stage validation

No tests, builds, deliberate controls or operational acceptance ran in this Plan
dispatch. Validation is source/owner tracing and document consistency/path checks.
The next stage is TestDesign, not Code: executable assertions, filters, guard/PC
coverage and measured/estimated verification floors still belong to that dispatch.

--- next stage ---
next: test-design
handoff: Append executable verification to CARD-0544's explicit per-card/per-role Interim/Final plan: preserve initial/final full sweeps, baseline and approval guards, and qualify Windmill S4 before activation; include monitor freshness versus daily-run validity and independent outage receipt.
artifact: docs/superpowers/plans/2026-09-16-card-0544-interim-final-verification-plan.md


## Verification design

TestDesign: 2026-09-16, task 1193e4ae; inspected base 3d45162e. This appendix
leaves D-1 through D-8 and S1 through S6 unchanged. Names beginning C544 below
are **tests to implement**, not tests claimed to exist or pass today. This card
remains FullOnly. Code implements the tests and runs ordinary V/R; separate
ordinary Review judges the evidence before land; sourced Mutation runs the PCs
against the confirmed landed SHA. Neither dormant deployment nor offline monitor
tests complete S5/S6.

This is the retained TestDesign inventory at 5d91dca2. The later D-9/D-10
resolution assigns TD-F2 production work and 13 controls to CARD-0545 and adds
ordinary-completion guards. Its scope/cost counts must be revised by the next
TestDesign before Code; the historical rows below are not a qualification waiver.

### Inspection

The reads below include the stated method/helper bodies, not just filename or
test-name searches. Existing suites listed for regression remain intact; prefer
new C544 classes to weakening historical assertions.

| Test/fixture bodies read | Boundaries -> verification |
|---|---|
| AgentTaskCardBindingTests explicit-GUID, board-scoped identifier, unresolved/ambiguous binding, CreateService/SeedProjectBoardAsync/SeedCardAsync; CardCorrectionIntegrationTests An_edit_supersedes_the_text_and_archives_what_it_replaced, concurrent writers and BuildHarness; ExternalTrackerSyncImportanceProvenanceTests and ExternalTrackerSyncLandingColumnTests NewSut/SeedTrackedBoardAsync/FakeIssueTracker | Card/role defaults, PATCH revision, stale token, board identity and import/update ownership -> V-1, R-1. Historical card harness uses the shared DB; new C544 fixtures must pass an isolated connection everywhere. |
| DelegateScriptRepairSourceTests, DelegateScriptRunner.RunAsync, DelegateCreateStubApi pump/disposal | Real pwsh argv and POST serialization/refusal -> V-2. Stub proves CLI wire shape, not server admission. |
| DelegateBundleLaunchTests TaskFor/CreateHarness; DelegationReportFormatterTests read-only/refocus/spill bodies; ScopedVerificationInstructionTests all bodies | Fresh versus warm composition, brief file/pointer and conflicting scope instructions -> V-3/V-8, R-6. C487_G142 contains obsolete Code -> Mutation text; update its expectation to the commissioned Code -> Review contract. |
| AgentTaskReviewEvidenceTests all bodies; ReviewEvidenceParserTests all bodies; AgentTaskReplyIntegrationTests CreateService, SeedDispatchedTaskAsync, SeedTurnAsync, TestScopeFactory; production RecordDelegateStageOutcomeAsync | Parser is insufficient: C488_SubjectAuthorizationRequired only compares parsed IDs, and C488_SettlementEvidenceAtomic calls manual-finding tests. New settlement tests drive OnTurnEndAsync and observe a new DB context -> V-5, R-4. |
| AgentTaskLandApprovalRequestTests admission/identity/replay bodies and helpers; AgentTaskLandApprovalPersistenceTests all bodies; AgentTaskLandApprovalRecoveryTests standing-child, checkpoint and published-cleanup bodies | Explicit-caller compatibility, approval coordinates, migration and restart -> V-6, R-5. |
| LandingProtocolHarness initialization, real service graph, RequestAsync, RunQueuedAsync, restart, verifier, save/transaction fault interceptors; LandingSafetyHarness initialization/service/restart/crash worker; LandingGitFixture initialization, isolated remotes, observer and child invocation | Controlled Git establishes ordering, never actual Git publication. Retain a real-Git capstone with independent remote observation -> V-6/V-7. |
| TestDbFixture isolation/lifetime; DelegationTestServices registration; BridgeQueueHarness options, actual queue/runtime graph, adapter OnSubmitted, transcript inserts; CheckNoteDeliveryHandoffTests Handoff, whole-prompt assertions, busy/eligible producer tests | Real producer -> real session queue -> independently read complete UserPrompt; fake adapter is an explicit terminal substitute -> V-4, R-3. |
| AgentTaskLandDeliveryE2ETests C467_V22/V23/V25/V26 bodies | Native delivery examples inform crash cuts. Their OptIn fixture is not reused or credited by this card's offline tests; live/native transport remains the stated substitute limitation. |
| scripts/test-nightly-health.ps1 all cases and New-HealthFx/Invoke-HealthFx; scripts/lib/c487-harness.ps1 root/seams/assertion/evidence helpers; scripts/lib/nightly-health.ps1 evaluator, production HTTP adapter, notification store/receipt/recovery; nightly-health.ps1; both checked-in Windmill script payloads and README | Clock boundaries, native/job correlation, receiver evidence and independence -> V-9/V-10/V-11. C487_G111 asserts true for SSH/missing-result; G122 busy/eligible labels do not create busy/eligible recipients. Neither is delivery or outage proof. |
| CompletionNoteWorkHostedService ScanAsync/RecoverMissingSourcedCompletionNotesAsync, CompletionNoteWork queues and AgentTaskReplyService settlement/DeliverToParentAsync; AgentTaskLandNotificationRecoveryTests pre-operation and destination/hosted-handoff bodies | Existing queued-row recovery versus missing ordinary completion insertion -> TD-F1, V-4. The sourced-only scan cannot be credited for Code/Review. |
| NightlyScriptsTests all bodies; scripts/test-client.ps1 actual argv/result handling; TaskDetailBody.test.tsx bodies, TaskDrawer.test.tsx detail/serve and land/receipt cases; client test/utils.ts and mocks/server.ts | Script process ownership/ASCII and UI evidence visibility -> V-2/V-8/V-9. New UI tests use the nearest provider/MSW fixtures. |
| Testing/build fast lane, exact method PC execution and delivery contracts; orchestration stage/landing order; lifecycle card/task separation; session delivery invariants; project conventions; CARD-0487 S4 and delivery inventory | Full initial/final scope, separate PC owner and operational qualification -> all V/R, Cost. |

Missing setup to implement in Code, with no production-policy relaxation:

1. Add a C544 test fixture using TestDbFixture.CreateIsolatedSchemaAsync (a cloned
   database despite its name), FakeTimeProvider and AddDelegationWorktreeGraph.
   Register new settings/policy/reader in every affected hand-built graph. Use
   production CreateAsync, dispatch TickAsync, reply OnTurnEndAsync, RequestAsync,
   SweepAsync and RunRequestAsync. Do not seed StageOutcome as a substitute for the
   settlement cases. Seed source/owner/card/project and full baseline reports through
   their real boundaries; use a controlled readiness reader only in policy tests.
2. Add real temp-file readiness tests against InterimVerificationReadinessReader,
   including bounded/unreadable/torn input. Use full valid object IDs and distinct
   task, outcome, run and job IDs; change one identity at a time. Old helpers' 'sha-1'
   is not valid evidence for these tests.
3. Extend fixture-local save/transaction interception to task+latch, settlement and
   queue handoffs. Two independent contexts plus barriers test races; Task.Delay
   races and passing exceptions from fixture setup are unacceptable. Dispose and
   recreate the provider while retaining the owned database/files for restart tests.
4. Add VerificationRoundDeliveryTests around BridgeQueueHarness. Keep the real
   dispatcher, reply service, queue and recovery; replace only the terminal adapter.
   Give both sessions an ended prior turn, produce the actual messages, and let
   OnSubmitted record receipt. Never pre-insert the expected UserPrompt. Introduce
   child-process crash barriers where abrupt loss differs from a caught exception.
5. Add NightlyVerificationContractTests under tests/Antiphon.Tests/Scripts. Its exact
   C544 methods invoke the matching Test-C544_* case in test-nightly-health.ps1;
   extend default case discovery to include C544 cases and enforce each case's
   nonzero named assertion inventory. Use real production HTTP adapters against an
   owned loopback Windmill stub with a durable queue and separately readable receiver.
   Execute the checked-in bash payload with the installed Git bash (not the Windows
   WSL launcher), fixture-local SSH shim and dummy credentials. Await all children.
   For the independent outage case the Windows shim always fails, Windows state is
   inaccessible, and monitor/receiver state lives in a separate fixture root.
   This tests the independent failure implementation required by D-6; a tag/string
   assertion or directly calling a notification helper does not implement this case.
6. Existing production health receipt input is a local file plus a test-only
   RecipientView seam; the current bash wrapper just exits on SSH failure. Code
   must provide a testable production failure/recipient-readback path for D-6, or
   return next: plan with that seam unresolved. Do not mark these cases passed using
   invented receiver rows. S5 must separately prove the selected deployed path.
7. Add TaskVerificationProfile.test.tsx beside the inspected UI tests, using
   renderWithProviders/MSW. New process-spawning test classes carry
   ParallelLimiter<ProcessSpawnLimit>; queue tests also use the existing MessageQueue
   serialization group. No real Program host may connect to runner port 17204.

Case construction rule: start with one valid Final/Full, Clean, authorized fixture,
then vary exactly the named field for negative cases. Compound negatives otherwise
let a surviving unrelated guard hide the defect. A listed C544 method is one TUnit
method (no parameter expansion); it loops the explicitly enumerated rows, includes
the row identity in every assertion, and retains that checked inventory with its
TRX. This makes the expected C544 execution count exact while preserving boundary
coverage. Additional implementation cases must be added to the selection artifact.

Class aliases used only to shorten the matrices (each denotes one exact type):

| Alias | Exact class and file | Lane |
|---|---|---|
| P | `InterimVerificationPolicyTests` in `tests/Antiphon.Tests/Application/InterimVerificationPolicyTests.cs` | Integration |
| C | `CardVerificationPolicyTests` in `tests/Antiphon.Tests/Application/CardVerificationPolicyTests.cs` | Integration |
| CLI | `DelegateScriptVerificationRoundTests` in `tests/Antiphon.Tests/Application/DelegateScriptVerificationRoundTests.cs` | Integration |
| D | `VerificationRoundDispatchTests` in `tests/Antiphon.Tests/Application/VerificationRoundDispatchTests.cs` | Integration |
| H | `InterimVerificationReadinessTests` in `tests/Antiphon.Tests/Application/InterimVerificationReadinessTests.cs` | Integration |
| L | `InterimVerificationLandGuardTests` in `tests/Antiphon.Tests/Application/InterimVerificationLandGuardTests.cs` | Integration |
| RP | `ReviewEvidenceParserTests` in `tests/Antiphon.Tests/Application/ReviewEvidenceParserTests.cs` | Unit |
| S | `VerificationRoundSettlementTests` in `tests/Antiphon.Tests/Application/VerificationRoundSettlementTests.cs` | Integration |
| I | `VerificationRoundInstructionTests` in `tests/Antiphon.Tests/Application/VerificationRoundInstructionTests.cs` | Unit |
| B | `VerificationRoundBriefTests` in `tests/Antiphon.Tests/Application/VerificationRoundBriefTests.cs` | Unit |
| Q | `VerificationRoundDeliveryTests` in `tests/Antiphon.Tests/Application/VerificationRoundDeliveryTests.cs` | Integration |
| N | `NightlyVerificationContractTests` in `tests/Antiphon.Tests/Scripts/NightlyVerificationContractTests.cs` | Integration |


### Delivery inventory

Acceptance is at the recipient. An accepted request, task success, queue insert,
Sent flag, terminal land event, Windmill job success or transport ACK is insufficient.
For session input retain the matching **complete UserPrompt**, session ID, sequence,
timestamp, queue attempt floor and generation. A pointer prompt proves receipt of
the pointer only: also check its exact file content/hash in the recipient workspace;
it does not prove that an agent read or obeyed that file.

| ID / producer -> destination / durable identity | Persistence and recovery cuts | Observable receipt and test |
|---|---|---|
| DL-1: AgentTaskService/dispatcher -> delegate session; task ID + admitted profile version + execution session/generation + ExecutionTaskId/queue ID | Task/profile/latch commit before launch; task commit -> queue insert; insert -> wakeup; Sent -> confirmation. Fail insert and lose process at each handoff; recreate services and run the real dispatcher/watchdog/queue recovery without editing task status by hand. | Q.C544_BriefHandoffRecovery: fresh, warm and follow-up paths, inline and spilled forms, each busy and eligible (12 rows). Confirm actual task marker/profile-bearing body once after the attempt floor. A busy session gets zero writes before TurnEnd. |
| DL-2: AgentTaskReplyService settlement -> caller session; task ID + immutable Result digest + StageOutcome ID + ParentSessionId, joined to SourceTaskId/ContentDigest/queue ID | Task/result/scope commit -> completion enqueue -> flush wakeup -> typed attempt -> receipt persistence. Fail enqueue, crash before/after insert, lose wakeup, and crash after recipient observation but before acknowledgement. | Q.C544_CompletionReceipt and C544_CompletionRecovery: busy/eligible x Inline/Spill x Raw/Distilled (8 rows per cut); header must preserve Interim/Final, bound evidence and required next stage. Full matching caller UserPrompt is decisive. **TD-F1 below prevents approving the pre-insert recovery case.** |
| DL-3: landing service/protocol -> original caller; LandRequestId + operation ID + terminal event + SourceLandNotificationId | New final-scope refusal during recovered land must use existing terminal transaction/outbox. Cover terminal commit -> note insert -> flush -> receipt with busy and eligible sessions; retain confirmed publication during cleanup-only retries. | L.C544_RecoveryRevalidates plus Q.C544_LandRefusalReceipt: one received refusal naming the saved request/evidence, no publication; Q.C544_LandRefusalRecovery covers before/after queue insert. Use the real LandNotification delivery service, not only a business-event assertion. |
| DL-4: scheduled Windmill wrapper -> native bootstrap -> monitor/qualification reader; workspace + London due date + Windmill job ID + native run ID + SHA/ref + policy/script hashes | Await SSH/native execution and persist run summary. Missing start, host loss, missing/malformed result, lost result publication and monitor write failure must remain unready. Next poll may recover only the same correlated facts; a manual run cannot fill in a scheduled run. | N.C544_ProductionJobAdapter, C544_IndependentOutage and V-11. Job/run correspondence plus fresh full suite inventory is business evidence, not operator receipt. |
| DL-5: nightly report/independent health failure and recovery producer -> notification facility -> authorized recipient; workspace + due date + job/run + SHA + policy hash + failure kind + stable notificationId | Persist intent before enqueue outside the failed host; recover enqueue failure, lost enqueue response, accepted-but-delayed recipient delivery, crash before receipt persistence and healthy transition after outage. Busy means the delivery consumer is held; an already eligible consumer delivers in the same run. No second logical notification on retry. | N.C544_NotificationIntent/Retry/Crash, C544_RecipientEvidence, C544_ReceiptNotificationIdentity/RunIdentity/Destination/WholeBody/AttemptFloor and C544_RecoveryNotification; V-11 repeats through the real facility. For a session destination require its complete UserPrompt. For a channel destination require authorized recipient-side message/readback and matching complete produced content/IDs, not a sender-written receipt file. **TD-F2 below blocks production acceptance.** |
| DL-6: qualified deployment -> trusted readiness files -> queued Interim admission; qualification artifact commit + repository/project + accepted run/job/recipient IDs + monitor RecordedAt | Publish qualification only after V-11; atomically replace monitor file. Fail/truncate/read-deny either file, then recover a fresh matching pair. Queued admission rereads them; running commands retain ownership. | H.C544_* and D.C544_QueueReadinessLoss. A reader green authenticates the local attestation contract; only V-11 proves the attested operational receipt happened. |

Substitutes: controlled Git cannot prove a rebase/push; BridgeQueueHarness's
OnSubmitted transcript cannot prove a real provider/TUI; loopback Windmill queue and
receiver cannot prove live registration, queue placement, credentials, Telegram
delivery or failure-domain independence. File/prompt and instruction-text checks
cannot prove agent obedience or execution of the listed tests. V-7, V-11 and V-12
supply the respective real evidence. Do not replace these with more mock assertions.

**Test-design findings requiring Plan before Code handoff**

- **TD-F1, ordinary completion pre-insert recovery:** PersistDeliverThenReleaseAsync saves
  task/outcome before DeliverToParentAsync. That method catches an enqueue failure;
  CompletionNoteWorkHostedService.ScanAsync can flush existing queue rows, but
  RecoverMissingSourcedCompletionNotesAsync only selects SourceLandingOperationId
  != null. Ordinary Code/Review cannot use SourceLanding. D-8 changes the delivered
  scope/handoff, but S2/S3 do not specify a durable owed-completion/recovery boundary
  for this path. Stored Result is useful recovery material; it is not evidence that
  a worker discovers and delivers it. Plan must name the durable obligation,
  deduplication identity, recovery owner, and preservation of scope/evidence in the
  rebuilt note, or commission a prerequisite repair. Do not silently expand generic
  delivery machinery in a TestDesign appendix.
- **TD-F2, independent Windmill receipt seam:** checked-in health bash only SSHs to
  Windows; its schedule has on_failure=null. Production Get-NightlyRecipientView
  reads local receipt files; its other input is the test-only RecipientView seam.
  No independent deployed failure/recovery producer or recipient readback adapter
  is identified in S4. Plan explicitly allows either an independent Windmill failure
  path or moving evaluation; choose and name the actual component, its off-host
  durable store, authorized receipt source, and crash/retry interface. The live
  destination remains operator-authorized during S5 as D-6 already requires.
  SourceLanding PCs must exercise a local inherited copy, never send snapshot code
  to Windmill or an external executor. Therefore a callable local adapter contract
  is needed before these controls can be commissioned.

These are engineering seams, not a request to the human to choose reduced coverage.
This appendix supplies the exact acceptance tests and failure cuts; it does not
rewrite the fix to select a new delivery architecture. Test-design review rejects
any version that stops DL-2/DL-5 at the producer or transport acknowledgement.

### Proves it works now

"Now" means ordinary verification of the implemented candidate, before land.
All tests named in the PC table are ordinary regression tests first. Run full
classes for this card; the exact-method PC command is reserved for Mutation.

- V-1: local policy/defaults and persistence | isolated PostgreSQL + API/service |
  full C and P classes | new and imported cards FullOnly; 2 roles x 4 policy pairs
  x {omitted, Final, Interim} = 24 admission rows; omitted and explicit Final never
  need nightly readiness. Same identifier on two boards stays isolated. PATCH
  Code-only, Review-only, both and neither; stale token; revision contains prior
  values; upgrade from immediately preceding CLI-generated migration and rerun
  preserve old Unknown outcomes. Unknown enum/string/numeric values give 422;
  eligibility refusals give stable 409 and create no task/launch.
- V-2: actual CLI/API/UI contract | child pwsh + loopback API |
  CLI.C544_RequestRoundTrip, C544_OmittedRound, C544_InvalidArguments,
  C544_ServerRefusal, C544_CardPolicyPatch | exact camelCase body, full GUIDs,
  JSON object content rather than local selection filename, both role policies,
  omitted fields preserved; 409 survives to nonzero CLI exit. Invalid role,
  unsupported workspace, short GUID, malformed JSON and bad enum produce no POST
  or server-side admission. Use a loopback card stub supporting GET token + PATCH
  for C544_CardPolicyPatch; do not point scripts/card.ps1 at the production board.
- V-3: baseline/selection/source and queued lifecycle | production service/dispatcher |
  full P, H, D and B classes | Found-Full and Clean-Full baselines admit repairs;
  P.C544_ExplicitBaselineRequired rejects missing subject, missing baseline, both
  missing and a first Code/Review without any full baseline; never infer the latest
  same-card outcome. These are four separately named rows from an otherwise ready setup.
  scope {Full,Interim,None,Unknown}, status {Succeeded,Failed,Canceled,Queued,Working},
  source {Delegate,Orchestrator,Backfill}, and each owner/card/repo/project mismatch
  tested independently. Superseded baseline, unrelated Git history, altered queued
  policy/health/baseline hold before launch. Retry/Continue/reroute/escalation preserve
  the full snapshot; fresh follow-up defaults Final. Null project matches null only.
  Selection cases: existing committed docs path/anchor/rows; missing/short SHA,
  empty anchor/table, dirty-only file, absolute/traversal/sibling/link escape.
  No implicit HEAD fallback. Failed reads fail closed and Final remains available.
- V-4: both session delivery legs and landing-refusal delivery | real producer/queue
  with controlled terminal | full Q class (C544_BriefHandoffRecovery,
  C544_CompletionReceipt, C544_CompletionRecovery, C544_LandRefusalReceipt,
  C544_LandRefusalRecovery) | DL-1..3 cuts as enumerated above. StageOutcome/full
  transcript correlation is retained. Before eligible delivery assert zero writes;
  after recovery assert one whole intended prompt beyond the attempt floor. Exercise
  enqueue failure, lost wakeup and fresh-provider restart separately. TD-F1 is a
  failing acceptance seam, not an allowed exclusion.
- V-5: parser-to-durable settlement | parser Unit + production reply graph |
  RP.C544_ScopeGrammar/C544_DuplicateScope and full S class | Full/Interim/None
  parse distinctly; missing, empty, malformed and duplicate (equal/conflicting)
  become Unknown. Existing fenced/quoted grammar remains covered. Real marked
  AssistantText + TurnEnd settles Clean and Found with authorized coordinates.
  Failed/canceled/incomplete turns do not certify Full. A report or Continue cannot
  promote Interim. Manual overrides/backfill retain Unknown. Inject before save,
  after save before commit, after commit before completion enqueue; re-enter once
  and observe exactly one committed outcome through a fresh context.
- V-6: final-review approval and recovery | controlled landing protocol + actual DB |
  full L class and existing approval request/persistence/recovery suites |
  Code-Interim and Review-Interim each latch original owner. Race two contexts with
  land-first and interim-first barriers; include pre-commit failure/rollback.
  Latch survives cancel/disable/restart/repair integration. Latched no-evidence and
  Interim approval refuse before a request; explicit-caller fallback remains for
  unlatched full-only/legacy owners. Approval dimensions Final+Full, Clean, completed,
  owner, SHA, ref, repo and supersession vary independently. For recovery cross
  checkpoints {accepted request/no operation, Prepared, Verified, before target
  advancement, before push} x {evidence removed, superseded, scope invalidated,
  owner newly latched with no evidence}: 20 rows; assert no new target/push child,
  not only a refusal string. If publication already happened but its acknowledgement
  was lost, reconcile exact existing publication; never roll it back or repeat push.
  Add L.C544_PublishedCleanupCompatibility: committed publication permits cleanup
  retry with the original receipt and zero new verification/publication.
- V-7: final promotion works through actual Git | isolated local bare remote +
  LandingSafetyHarness | InterimVerificationLandGitTests.C544_FinalPromotionPublishesOnlyReviewedCandidate
  and C544_EditAfterFinalRefuses | complete Found Full baseline -> Interim Code/
  Review -> refused no-evidence/Interim land -> clean Final Full Review of unchanged
  candidate -> normal RequestAsync/RunQueuedAsync publication, independently fetched
  from remote. No no-change Code task. A subsequent real commit on the owner branch
  refuses stale review. Assert unchanged LandVerifyFilter and ordinary rebase/
  verifier invocation; track approved C versus verified L when rebase changes SHA.
  Also run C544_AncestryUsesActualGit against ancestor and unrelated histories.
- V-8: instructions, selection and visible status | composed bundles/brief + client |
  full I/B and ScopedVerificationInstructionTests, InstructionBundleTests,
  DelegationReportFormatterTests; TaskVerificationProfile.test.tsx exact cases
  "C544 renders commissioned scope and final obligation", "C544 does not display
  legacy Unknown as Full", "C544 exposes baseline and readiness refusal" |
  initial/default Final Code and Final Review retain Unit + complete affected classes
  + every ordinary V/R + required manual work. Review is independent; final promotion
  includes every earlier deferred row. Interim contract is cumulative baseline delta
  + unresolved findings + named adjacent smoke. Fresh/warm/spilled text agrees and
  fits existing ASCII/2500 stage-bundle constraints. UI mirrors server facts without
  issuing mutation calls. Static contract tests are not proof of actual selection;
  V-12 supplies that evidence.
- V-9: reader and clock separation | real file reader + offline scripts |
  full H and N classes plus full scripts/test-nightly-health.ps1 |
  RecordedAt ages {-1 tick,0,59m59s,60m,60m+1 tick}, missing/malformed timestamp,
  missing/torn/denied/oversized files; wrong repo/project/artifact/hash/job/run one at
  a time. Healthy/Ready false deny despite freshness. Fresh monitor + today's green
  completed 8 hours ago admits. Yesterday's complete scheduled green bridges before
  08:00 only while today's pending state remains otherwise healthy; newer completed
  red/incomplete denies immediately; stalled/overdue denies independently.
  Daily rows: 23:59:59, 00:00, 00:29:59, 00:30, 00:59:59, 01:00, 07:59:59, 08:00,
  08:00:01 London, with valid/missing/yesterday-only/newer-red native state.
  DST dates 2026-03-28/29/30 and 2026-10-24/25/26 require exact UTC instants, not only
  round-trip local hour. Start-grace equality is overdue at 30 minutes.
- V-10: actual adapter shapes and local delivery/recovery | checked-in entrypoints,
  production HTTP serialization and a fixture-owned queue/receiver |
  N.C544_ProductionJobAdapter and all N notification/outage methods |
  HTTP jobs {queued,running,success,failed}, missing result and crossed due-day/
  run/SHA fixtures, real bool/string fields as returned by the production adapter;
  unknown shapes refuse. Run the failure producer through the owned queue, holding
  then releasing its receiver and separately starting already eligible. Include
  intent commit, enqueue rejection/lost ACK, delayed receipt, crash before receipt
  persistence, duplicate poll and failure-to-recovery transition. Receiver evidence
  must be produced by dequeue/consume; tests may not write expected receipt JSON.
  For SSH 255/timeout/missing-result, invoke the checked-in wrapper with no Windows
  storage access. TD-F2 must name the production seam before this is executable.
- V-11: qualified real backstop | separately commissioned S5, operational/manual |
  execute the deployed u/lndcobra/antiphon_nightly_tests once manually and observe
  a subsequent real 00:30 Europe/London scheduled run; read back both script/schedule
  definitions and antiphon_nightly_health job results | all seven unattended suite
  inventories/counts green at recorded SHA/ref/policy/script hashes, current monitor
  and no hidden required skips. Run real facility failure and recovery notifications
  for an already eligible recipient and a temporarily held delivery consumer; retain
  queue/job/event/recipient IDs and receiver content. Force only the authorized
  qualification Windows hop unavailable (do not kill shared services), prove an
  off-host alert and receipt while that hop remains down, then recovery receipt.
  Include restart/enqueue-failure at each DL-5 handoff. Publish qualification artifact
  and receipt only afterward. Missing credentials/recipient authorization leaves S5
  pending, never green. Follow CARD-0487 ownership; do not commission it from this task.
- V-12: actual selection and final sweep | S6 bounded pilot, plus review worksheet |
  record the dated pilot artifact specified in S6 |
  compare baseline B, repair R1 and repair R2: R1 adds one ordinary case; R2 changes
  a different case. R2 selection still includes R1's case and all unresolved-finding
  tests, with success/failure and persistence/delivery adjacent smoke. Record the
  actual commands and expanded results, not just the table. Then commission fresh
  Final Review on the unchanged candidate and prove every deferred ID/class ran.
  One pilot field edit disables future Interim but leaves the latch. An isolated
  repository demonstrates refusal then successful Final-backed land via V-7.
  The production pilot must retain its own actual Final evidence before landing.

Full scope commands (Code and independent Review), once tests exist:

~~~powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c544/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c544/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c544-unit
~~~

Run each named integration class with the same built output and
--treenode-filter "/*/*/ClassName/*", a fresh result directory and a nonzero TRX.
No unproved method OR expression is needed. Full affected inventory is P, C, CLI, D,
H, L, S, Q, N, InterimVerificationLandGitTests, AgentTaskCardBindingTests,
CardCorrectionIntegrationTests, ExternalTrackerSyncImportanceProvenanceTests,
DelegateScriptRepairSourceTests, DelegateBundleLaunchTests, CodexDelegateDispatchTests,
AgentTaskReuseEnqueueTests, AgentTaskReviewEvidenceTests, AgentTaskLandApprovalRequestTests,
AgentTaskLandApprovalPersistenceTests, AgentTaskLandApprovalRecoveryTests,
CheckNoteDeliveryHandoffTests and NightlyScriptsTests. B/I/RP and the existing
instruction/formatter classes are already included by the Unit lane; run any newly
integration-tagged fixture separately. Code must list changed helper consumers and
add their full classes before running, rather than use a namespace as a shortcut.

~~~powershell
pwsh -NoProfile -File scripts/test-nightly-health.ps1
pwsh -NoProfile -File scripts/test-client.ps1 TaskVerificationProfile TaskDetailBody TaskDrawer -JsonResultPath .antiphon/c544-client.json
~~~

Retain counts per class and per new method. There are 101 distinct G/PC C544 methods,
plus 8 supplemental methods: CLI.C544_OmittedRound/InvalidArguments/ServerRefusal/
CardPolicyPatch, L.C544_PublishedCleanupCompatibility, and the three V-7 methods.
Thus the named new/extended backend floor is **109 executed C544 methods**; loop
inventories above are additional assertions, never misreported as discovered tests.
The new UI floor is three named cases. Existing-suite counts come from the fresh
candidate execution inventory and must not be invented from this design.
N wrappers and direct full script run overlap intentionally: wrappers give exact
PC methods, while the direct run verifies case discovery/aggregation/exit behavior.

For a later bounded Interim selection (not this card's implementation), the selection
artifact must have D-3's exact columns and baseline/outcome/commit anchor. An example
for a scope-parser-only repair retains every C544 method changed since B plus
ReviewEvidenceParserTests.C488_DuplicateBlocksRefuse,
AgentTaskLandApprovalRequestTests.C488_CallerShaWithoutReviewIsValid and the new
S.C544_ProfileCapsScope as explicit success/refusal/settlement smoke. If the patch
also touches queue or shared landing helpers, add the two inspected
CheckNoteDeliveryHandoffTests busy/eligible methods and the V-7 real-Git capstone,
or choose Final when that dependency cannot be bounded. These are reviewed examples,
not a permanent minimal selection or a 5-minute cap. Keep a separate
deferred-to-final table naming the whole Unit lane and every deferred affected class.

### Guards the regression

- R-1: global/title-derived or cross-role opt-in | C/P/CLI exact C544 methods |
  otherwise-ready FullOnly requests still refuse, and omitted mode remains Final.
- R-2: a convenient but foreign/stale baseline or selection silently narrows work |
  P.C544_Baseline* / C544_SubjectLinks / C544_Selection*,
  D.C544_AncestryAtPreparation / C544_Queue* | no launch and no substituted baseline.
- R-3: metadata written but task/parent never receives its scope-bearing input |
  Q exact methods and DL-1..3 cuts | whole matching UserPrompt beyond the attempt
  floor, exactly one logical delivery; busy recipient was not typed into.
- R-4: Clean or a prose "Full" claim becomes approval | RP/S full classes |
  commissioned scope caps settlement; Found Full is a baseline only; manual and
  legacy evidence remain Unknown; header and durable outcome agree.
- R-5: explicit caller, replay, cancellation or restart bypasses final Review |
  P.C544_*Latch*, D.C544_LatchSurvives, L full class and V-7 capstones |
  zero request/publication on refusal; original owner latch survives; matching
  Final Full Clean evidence admits actual publication.
- R-6: selected-only work becomes the initial/final default or loses cumulative cases |
  I/B, scoped instructions, V-12 worksheet and actual pilot results |
  initial/final complete scope, R1 tests still run in R2, adjacent smoke is named,
  deferred rows reappear in Final, PCs/manual acceptance are not credited nightly.
- R-7: fresh daily green expires after one hour, or stale monitor stays trusted |
  H.C544_MonitorAge/FutureMonitor/MonitorVerdict and N.C544_DailyValidity/
  MorningBoundary/NewerFailure/LondonDates/ScheduleHealth | independent clock
  predicates at exact equalities; no future, stale or wrong-due-date permission.
- R-8: Windmill accepted an alert but nobody received it, or the monitor fails with
  Windows | N exact adapter/outage/notification methods and V-11 |
  correlated receiver evidence through real deployed queue; outage receipt exists
  while the Windows hop is unavailable. Offline-only success is rejected.

### Guard inventory

Each line maps one guard to a distinct control. Existing unrelated CARD-0487
lock/native-universe PCs are not reassigned here. No listed safety guard has
"none" as its control; the seven seam-dependent controls below remain explicit
handoff defects, not exclusions.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-1: Omitted round stays Final even on opted-in cards or Goal text suggesting quick work | PC-1 |
| G-2 | D-1: Code opt-in is checked on the resolved card, independently of Review permission | PC-2 |
| G-3 | D-1: Review opt-in is independent of Code permission | PC-3 |
| G-4 | D-1: An explicit round is invalid on other roles; Interim supports only the specified Worker/workspace pairs | PC-4 |
| G-5 | D-1/S1: Policy edits preserve omitted fields and the prior policy in a content revision | PC-5 |
| G-6 | D-1/S1: Stale content tokens cannot change verification policy | PC-6 |
| G-7 | D-1: Same card identifier on another board confers no opt-in | PC-7 |
| G-8 | D-1: Tracker import and synchronization cannot enable or overwrite local policy | PC-8 |
| G-9 | D-2/S1: Migration preserves historical Unknown scope and defaults cards to FullOnly | PC-9 |
| G-10 | D-1/S1: CLI preserves explicit round, full subject/baseline IDs and file-backed selection | PC-10 |
| G-11 | D-2: A baseline must have Full completed scope | PC-11 |
| G-12 | D-2: A baseline must be a completed successful delegate Review task; Found Full is eligible | PC-12 |
| G-13 | D-2/D-5: Manual/overridden/backfilled evidence cannot establish the baseline | PC-13 |
| G-14 | D-2: Baseline subject is the original landing owner | PC-14 |
| G-15 | D-2: Baseline card matches the admitted card | PC-15 |
| G-16 | D-2: Baseline repository matches the authorized prepared repository | PC-16 |
| G-17 | D-2: Baseline project matches, including null project identity | PC-17 |
| G-18 | D-2: Repair and follow-up resolve to the original owner | PC-18 |
| G-19 | D-2: Source preparation requires baseline ancestry in the same history | PC-19 |
| G-20 | D-3: Selection path cannot escape authorized repository docs | PC-20 |
| G-21 | D-3: Selection commit and section bind a real nonempty committed selection | PC-21 |
| G-22 | D-7: Deployment settings are disabled by default and caller data cannot enable them | PC-22 |
| G-23 | D-7: Readiness applies only to the configured canonical repository | PC-23 |
| G-24 | D-7: Readiness applies only to the configured project, with exact null handling | PC-24 |
| G-25 | D-7: Missing, malformed, unreadable or oversized trusted state fails closed | PC-25 |
| G-26 | D-7: Qualification requires accepted manual and scheduled runs plus recipient evidence | PC-26 |
| G-27 | D-7: Qualification artifact full commit identity must be present and valid | PC-27 |
| G-28 | D-7: Monitor and qualification policy hashes must match | PC-28 |
| G-29 | D-7: Monitor and qualification script hashes must match | PC-29 |
| G-30 | D-7: Monitor job/run and qualification identities must correlate | PC-30 |
| G-31 | D-6/D-7: Monitor freshness uses RecordedAt; age over 60 minutes is invalid | PC-31 |
| G-32 | D-6/D-7: A future RecordedAt is invalid | PC-32 |
| G-33 | D-7: A fresh monitor must explicitly be healthy and ReadyForDeferral | PC-33 |
| G-34 | D-7: Queued work rechecks policy before launch | PC-34 |
| G-35 | D-7: Queued work rechecks readiness before launch | PC-35 |
| G-36 | D-2/D-7: Queued work rechecks baseline eligibility before launch | PC-36 |
| G-37 | D-4/D-7: Retry/Continue/reroute/escalation retain commissioned scope; fresh follow-up defaults Final | PC-37 |
| G-38 | D-7: Admission snapshots persist through a fresh provider/context | PC-38 |
| G-39 | D-5: Interim Code admission durably latches the original owner in the same transaction | PC-39 |
| G-40 | D-5: Interim Review admission also durably latches the original owner | PC-40 |
| G-41 | D-5: Opt-out, cancellation and repair integration cannot clear the latch | PC-41 |
| G-42 | D-5: Interim admission serializes with owner land admission | PC-42 |
| G-43 | D-5: Pending or published owners cannot admit new Interim tasks | PC-43 |
| G-44 | D-5: Missing/malformed completed scope remains Unknown | PC-44 |
| G-45 | D-5: Duplicate scope declarations are unusable | PC-45 |
| G-46 | D-4/D-5: Interim reports cannot mint Full outcomes | PC-46 |
| G-47 | D-2/D-5: Found Full settlement binds authenticated original-owner coordinates | PC-47 |
| G-48 | D-5: Settlement cannot bind unauthorized review subjects | PC-48 |
| G-49 | D-5: Outcome and scope are committed atomically and not duplicated after retry | PC-49 |
| G-50 | D-5: Manual override/backfill cannot manufacture or copy Full scope | PC-50 |
| G-51 | D-5: A latched owner cannot use explicit-caller/no-evidence approval | PC-51 |
| G-52 | D-5: An Interim Review is ineligible approval even on an unlatched owner | PC-52 |
| G-53 | D-5: Latched approval requires both commissioned Final and completed Full | PC-53 |
| G-54 | D-5: Land approval must be a completed Clean Review | PC-54 |
| G-55 | D-5: Land approval matches the original owner | PC-55 |
| G-56 | D-5: Land approval matches the expected SHA | PC-56 |
| G-57 | D-5: Land approval matches the source ref | PC-57 |
| G-58 | D-5: Land approval matches the repository | PC-58 |
| G-59 | D-5: Superseded approval is ineligible | PC-59 |
| G-60 | D-5: Recovery revalidates the final requirement/evidence before publication | PC-60 |
| G-61 | D-5: Pending-request replay cannot replace its approval identity | PC-61 |
| G-62 | D-4/D-8: Full initial/default Code includes all ordinary obligations | PC-62 |
| G-63 | D-4/D-8: Final Review independently reruns deferred ordinary coverage | PC-63 |
| G-64 | D-3: Interim selection is cumulative since the full baseline | PC-64 |
| G-65 | D-3: Interim includes named adjacent success/failure and persistence/delivery smoke | PC-65 |
| G-66 | D-3/D-8: Unavailable manual acceptance and deferred PCs remain outstanding | PC-66 |
| G-67 | D-4: Clean Interim handoff requests Final Review and cannot relabel itself | PC-67 |
| G-68 | D-8: Warm/follow-up/spilled task briefs carry the current profile | PC-68 |
| G-69 | D-8: Profile brief handoff survives failed enqueue and process recreation | PC-69 |
| G-70 | D-8: Scope-bearing completion outcome reaches the actual caller | PC-70 |
| G-71 | D-8: Outcome recovery does not lose or double-submit a committed completion | PC-71 |
| G-72 | D-6: Daily validity is separate from the age of completedAt | PC-72 |
| G-73 | D-6: The previous due day's green bridges only before 08:00 London | PC-73 |
| G-74 | D-6: A newer completed red/incomplete attempt revokes the bridge immediately | PC-74 |
| G-75 | D-6: Manual or partial runs cannot advance scheduled-green identity | PC-75 |
| G-76 | D-6: London due date and DST calculations select the correct scheduled day | PC-76 |
| G-77 | D-6: Registration/job adapters reject absent, wrong or incomplete operational facts | PC-77 |
| G-78 | D-6: Windows/SSH outage must be detected outside that failure domain | PC-78 |
| G-79 | D-6/D-7: Nightly failure intent persists before notification enqueue | PC-79 |
| G-80 | D-6: Enqueue failure stays retryable with the original identity | PC-80 |
| G-81 | D-6: Transport/job acceptance is not recipient receipt | PC-81 |
| G-82 | D-6: Receipt matches notification identity | PC-82 |
| G-83 | D-6: Receipt matches run identity | PC-83 |
| G-84 | D-6: Crash after accepted enqueue or recipient observation recovers without duplicate notification | PC-84 |
| G-85 | D-6: Recovery notification is produced and received after an outage clears | PC-85 |
| G-86 | D-6: Unauthorized/missing destination cannot be silently replaced | PC-86 |
| G-87 | D-6: Missing/stalled/overdue scheduled jobs remain unhealthy without killing workers | PC-87 |
| G-88 | D-7: Loss of readiness does not kill already-owned running work or launch an automatic Final | PC-88 |
| G-89 | D-3: Nonzero fresh executed case inventory is required | PC-89 |
| G-90 | D-5: Task admission and owner latch cannot commit separately | PC-90 |
| G-91 | D-4: A fresh follow-up never inherits Interim implicitly | PC-91 |
| G-92 | D-6: Incomplete unattended coverage cannot qualify | PC-92 |
| G-93 | D-6: Tests-red cannot qualify despite fresh complete coverage | PC-93 |
| G-94 | D-6: Missing report receipt cannot qualify a scheduled green | PC-94 |
| G-95 | D-6: Recipient evidence must come from the authorized destination readback | PC-95 |
| G-96 | D-6: Recipient readback must contain the whole produced payload | PC-96 |
| G-97 | D-6: Recipient evidence predating the current notification attempt cannot confirm it | PC-97 |
| G-98 | D-6: Independent outage recovery state must survive Windows being inaccessible | PC-98 |
| G-99 | D-5/D-8: Recovered final-scope refusal owes an atomic terminal outcome notification | PC-99 |
| G-100 | D-5/D-8: Final-scope refusal notification recovers the same queue identity after failed enqueue | PC-100 |
| G-101 | D-2: Interim requires explicitly supplied subject and full baseline IDs; the initial round cannot bypass a missing baseline | PC-101 |

### Positive controls

Each PC-n below belongs only to G-n. Apply the stated small production defect,
keeping the project compilable (or the PowerShell/bash/embedded bundle syntactically
valid), and run only the exact class/method shown. A source-text contract mutation
changes a shipped bundle, never the test's expected text. A stronger unrelated
early failure is not the intended red.

~~~powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c544-pc/ -- --treenode-filter '/*/*/InterimVerificationPolicyTests/C544_DefaultIsFinal' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c544-pc-1-red
~~~

For each subsequent PC use its exact expanded class/method and a distinct red/green
directory. Rebuild after each mutation and restoration; refresh restored timestamps.
The exact method must execute once, and its named row/assertion must fail in red,
then pass after restoration. Zero tests, parser/build errors, infrastructure failure
and another assertion failing first do not count. N methods run only their matching
script case; do not run the whole health harness for a PC. Retain method name, loop
case, assertion, mutation diff, fresh result, landed L, restoration hash and green.

The seven controls PC-71/78/85/95/96/97/98 have precise test/receipt requirements but
their production recovery/receiver boundary is not defined by the landed fix design.
They are **not yet executable PC specifications**. Plan must resolve TD-F1/TD-F2 and
TestDesign must replace their boundary descriptions with the chosen concrete local
entrypoint before Code handoff. Mapping them does not discharge that requirement.
No PC has been run in this TestDesign dispatch.


| Control | Compiling production defect | Exact method | Required intended red assertion |
|---|---|---|---|
| PC-1 | AgentTaskService: change omitted-round resolution to Interim | `InterimVerificationPolicyTests.C544_DefaultIsFinal` | Round == Final |
| PC-2 | InterimVerificationPolicy: read ReviewVerificationPolicy in the Code branch | `InterimVerificationPolicyTests.C544_CodePermission` | FullOnly Code request throws verification_interim_disallowed; task count == 0 |
| PC-3 | InterimVerificationPolicy: read CodeVerificationPolicy in the Review branch | `InterimVerificationPolicyTests.C544_ReviewPermission` | FullOnly Review request throws verification_interim_disallowed; task count == 0 |
| PC-4 | InterimVerificationPolicy: replace the role/workspace eligibility predicate with true | `InterimVerificationPolicyTests.C544_RoleWorkspaceMatrix` | each invalid tuple throws verification_round_role before task or launch creation |
| PC-5 | CardRevisionLog: store FullOnly instead of the previous opted-in policy | `CardVerificationPolicyTests.C544_PolicyRevision` | revision.CodeVerificationPolicy == AllowInterim after disabling Code; omitted Review remains AllowInterim |
| PC-6 | CardService policy edit: skip the existing concurrency-token comparison | `CardVerificationPolicyTests.C544_StalePolicyEdit` | ConflictException and both policies/revision count unchanged |
| PC-7 | AgentTaskService policy lookup: use the other same-identifier card's policy | `CardVerificationPolicyTests.C544_BoardIsolation` | scoped FullOnly card refuses Interim; other board remains untouched |
| PC-8 | ExternalTrackerSyncService: assign FullOnly to both policies during an existing-card update | `CardVerificationPolicyTests.C544_TrackerPreservesPolicy` | existing AllowInterim remains AllowInterim; new imported card defaults FullOnly |
| PC-9 | new migration: backfill historical outcome scope to Full | `CardVerificationPolicyTests.C544_MigrationKeepsUnknown` | legacy OrdinaryScopeCompleted is Unknown/null after upgrade and rerun |
| PC-10 | delegate.ps1: omit verificationRound from the POST body | `DelegateScriptVerificationRoundTests.C544_RequestRoundTrip` | posted verificationRound == Interim with unchanged subject, baseline and selection object |
| PC-11 | InterimVerificationPolicy: accept Interim/Unknown/None baseline scopes | `InterimVerificationPolicyTests.C544_BaselineScope` | each non-Full baseline throws verification_baseline_invalid |
| PC-12 | InterimVerificationPolicy: skip baseline stage-task status/completion validation | `InterimVerificationPolicyTests.C544_BaselineCompletion` | failed, canceled, queued and incomplete rows refuse; Found Full admits |
| PC-13 | InterimVerificationPolicy: remove the delegate-source requirement | `InterimVerificationPolicyTests.C544_BaselineProvenance` | manual, override and backfill rows refuse despite Full-shaped fields |
| PC-14 | InterimVerificationPolicy: skip baseline.SubjectTaskId equality | `InterimVerificationPolicyTests.C544_BaselineOwner` | same-card foreign-owner baseline refuses |
| PC-15 | InterimVerificationPolicy: skip CardId equality | `InterimVerificationPolicyTests.C544_BaselineCard` | foreign-card baseline refuses even in same repository/project |
| PC-16 | InterimVerificationPolicy: skip canonical repository equality | `InterimVerificationPolicyTests.C544_BaselineRepository` | foreign repository refuses |
| PC-17 | InterimVerificationPolicy: treat null project as a wildcard | `InterimVerificationPolicyTests.C544_BaselineProject` | null/non-null and foreign-project rows refuse |
| PC-18 | AgentTaskService verification binding: accept the requested subject without resolving repair/follow-up owner | `InterimVerificationPolicyTests.C544_SubjectLinks` | mismatched RepairSourceTaskId or follow-up owner throws verification_baseline_invalid |
| PC-19 | dispatcher verification preparation: skip the ancestry check | `VerificationRoundDispatchTests.C544_AncestryAtPreparation` | unrelated baseline produces no launch and requests a new Final |
| PC-20 | selection validation: accept Path.GetFullPath of arbitrary supplied paths | `InterimVerificationPolicyTests.C544_SelectionPath` | absolute, traversal, sibling and link-escape rows refuse before reading external content |
| PC-21 | selection validation: read current working tree instead of the requested object/section | `InterimVerificationPolicyTests.C544_SelectionRevision` | wrong object, dirty-only file, missing section and empty rows refuse |
| PC-22 | InterimVerificationSettings: initialize Enabled to true | `InterimVerificationReadinessTests.C544_DisabledByDefault` | default settings deny readiness; request ready/stateRoot fields confer no authority |
| PC-23 | readiness reader: skip configured repository comparison | `InterimVerificationReadinessTests.C544_QualifiedRepository` | wrong repository is unready |
| PC-24 | readiness reader: skip configured project comparison | `InterimVerificationReadinessTests.C544_QualifiedProject` | wrong project or null wildcard is unready |
| PC-25 | readiness reader: return ready from the IOException/JSON/size failure branch | `InterimVerificationReadinessTests.C544_ReadFailure` | every failed read returns unready without launching |
| PC-26 | readiness reader: accept a receipt lacking qualifying recipient evidence IDs | `InterimVerificationReadinessTests.C544_QualificationReceipt` | receipt without recipient evidence is unready; a markdown filename alone is insufficient |
| PC-27 | readiness reader: permit missing/short artifact commit IDs | `InterimVerificationReadinessTests.C544_QualificationRevision` | short or missing qualification commit is unready |
| PC-28 | readiness reader: omit policyHash comparison | `InterimVerificationReadinessTests.C544_PolicyHash` | different policy hash is unready |
| PC-29 | readiness reader: omit script-hash comparison | `InterimVerificationReadinessTests.C544_ScriptHash` | different script hash is unready |
| PC-30 | readiness reader: omit scheduled run/job correlation | `InterimVerificationReadinessTests.C544_RunIdentity` | crossed run or job is unready |
| PC-31 | readiness reader: allow a 61-minute-old monitor | `InterimVerificationReadinessTests.C544_MonitorAge` | age 60m+1 tick is unready; age exactly 60m remains ready |
| PC-32 | readiness reader: remove the negative-age check | `InterimVerificationReadinessTests.C544_FutureMonitor` | RecordedAt one tick in the future is unready |
| PC-33 | readiness reader: use timestamp alone as readiness | `InterimVerificationReadinessTests.C544_MonitorVerdict` | fresh Healthy=false or ReadyForDeferral=false is unready |
| PC-34 | dispatcher: skip the queued card-policy recheck | `VerificationRoundDispatchTests.C544_QueuePolicyRevocation` | revoked role policy holds task; launch count == 0 |
| PC-35 | dispatcher: reuse admission readiness without rereading | `VerificationRoundDispatchTests.C544_QueueReadinessLoss` | stale or failed readiness holds task; launch count == 0 |
| PC-36 | dispatcher: skip baseline revalidation | `VerificationRoundDispatchTests.C544_QueueBaselineLoss` | superseded/ineligible baseline holds task; launch count == 0 |
| PC-37 | AgentTaskService Continue: reset the persisted round to Final | `VerificationRoundDispatchTests.C544_ProfileContinuity` | continued Interim remains Interim and cannot mint Final evidence |
| PC-38 | task persistence: omit the accepted selection commit snapshot | `VerificationRoundDispatchTests.C544_ProfileRoundTrip` | fresh-context selection commit, baseline, policy revision and readiness IDs equal accepted values |
| PC-39 | AgentTaskService: skip RequiresFinalVerificationReview assignment for Code | `InterimVerificationPolicyTests.C544_CodeLatchAtomic` | admitted Code has a committed latched owner; failed transaction leaves no admitted task |
| PC-40 | AgentTaskService: skip latch assignment for Review | `InterimVerificationPolicyTests.C544_ReviewLatchAtomic` | admitted Review has a committed latched owner |
| PC-41 | cancellation path: assign RequiresFinalVerificationReview=false | `VerificationRoundDispatchTests.C544_LatchSurvives` | after cancellation/restart the original owner remains latched and no-evidence land refuses |
| PC-42 | Interim admission: omit the owner lock while leaving the land lock intact | `InterimVerificationLandGuardTests.C544_AdmissionRace` | barrier-driven land-first interleaving never admits Interim; interim-first land refuses without Final |
| PC-43 | InterimVerificationPolicy: skip the pending/publication exclusion | `InterimVerificationPolicyTests.C544_OwnerAlreadyLanding` | both pending land and confirmed publication refuse verification_owner_landing |
| PC-44 | ReviewEvidence parser: default a missing scope to Full | `ReviewEvidenceParserTests.C544_ScopeGrammar` | missing, empty, misspelled and invalid scopes are Unknown |
| PC-45 | ReviewEvidence parser: keep the last duplicate scope value | `ReviewEvidenceParserTests.C544_DuplicateScope` | duplicate equal and conflicting scope lines produce Unknown/unusable scope |
| PC-46 | AgentTaskReplyService: persist reported Full without capping to commissioned Interim | `VerificationRoundSettlementTests.C544_ProfileCapsScope` | real settled Interim claiming Full has OrdinaryScopeCompleted != Full |
| PC-47 | AgentTaskReplyService: restore the Clean-only branch for recording subject evidence | `VerificationRoundSettlementTests.C544_FoundFullSubject` | Found Full outcome has the exact original owner/SHA and is eligible as baseline but not approval |
| PC-48 | AgentTaskReplyService: bypass ReviewSubjectAuthorized and requested-subject checks | `VerificationRoundSettlementTests.C544_SettlementSubject` | foreign-subject report yields no usable bound evidence |
| PC-49 | AgentTaskReplyService: persist a Full outcome before the task settlement transaction | `VerificationRoundSettlementTests.C544_SettlementAtomic` | pre-commit fault leaves zero outcomes; recovery creates exactly one with the settled scope |
| PC-50 | StageOutcomeService: copy prior OrdinaryScopeCompleted on manual override | `VerificationRoundSettlementTests.C544_ManualCannotCertify` | new manual/backfilled rows remain Unknown even after a Full delegate result |
| PC-51 | AgentTaskLandService RequestAsync: skip the latched-owner evidence requirement | `InterimVerificationLandGuardTests.C544_NoEvidenceRefuses` | final_verification_review_required; zero new request and zero publication mutations |
| PC-52 | LandApproval: skip Interim-scope exclusion | `InterimVerificationLandGuardTests.C544_InterimApprovalRefuses` | review_verification_scope_ineligible for latched and unlatched owners |
| PC-53 | LandApproval: accept Final with Unknown/None scope | `InterimVerificationLandGuardTests.C544_FinalFullRequired` | Final Unknown/None/Interim evidence refuses; Final Full admits |
| PC-54 | LandApproval: permit Found Full evidence | `InterimVerificationLandGuardTests.C544_CleanCompletedRequired` | Found Full throws review_evidence_ineligible; no request/publication |
| PC-55 | LandApproval: remove subject.Id comparison | `InterimVerificationLandGuardTests.C544_LandOwnerIdentity` | review_evidence_subject_mismatch |
| PC-56 | LandApproval: remove reviewed/expected SHA equality | `InterimVerificationLandGuardTests.C544_LandShaIdentity` | review_evidence_sha_mismatch |
| PC-57 | LandApproval: remove source-ref equality | `InterimVerificationLandGuardTests.C544_LandRefIdentity` | review_evidence_ref_mismatch |
| PC-58 | LandApproval: remove repository equality | `InterimVerificationLandGuardTests.C544_LandRepositoryIdentity` | review_evidence_repository_mismatch |
| PC-59 | LandApproval: omit the SupersedesId query result check | `InterimVerificationLandGuardTests.C544_SupersededFinal` | review_evidence_superseded |
| PC-60 | AgentTaskLandingProtocol recovery: bypass final-review revalidation | `InterimVerificationLandGuardTests.C544_RecoveryRevalidates` | changed/removed/superseded approval at every unpublished checkpoint gives zero new push/target advance |
| PC-61 | AgentTaskLandService pending branch: overwrite ReviewEvidenceId from repost | `InterimVerificationLandGuardTests.C544_ReplayImmutable` | land_request_identity_conflict; persisted original evidence/SHA/filter unchanged |
| PC-62 | stage-code.md: replace the initial/default full-sweep requirement with selected-only execution | `VerificationRoundInstructionTests.C544_InitialFullContract` | composed Code contract contains Unit, full affected classes and all ordinary V/R for initial/default Final |
| PC-63 | stage-review.md: allow clean Interim results to discharge Final checks | `VerificationRoundInstructionTests.C544_FinalReviewContract` | composed Final Review requires independent full rerun including deferred rows |
| PC-64 | stage-code.md: change cumulative-baseline delta to last-commit delta | `VerificationRoundInstructionTests.C544_CumulativeSelectionContract` | composed interim contract requires earlier repair cases and unresolved findings |
| PC-65 | stage-code.md: replace adjacent smoke with changed tests only | `VerificationRoundInstructionTests.C544_AdjacentSmokeContract` | composed contract requires named adjacent smoke and Final for unbounded shared impact |
| PC-66 | stage-review.md: allow nightly green to satisfy required manual/PC checks | `VerificationRoundInstructionTests.C544_ManualAndPcContract` | composed contract keeps required manual pending and PCs for SourceLanding Mutation |
| PC-67 | completion routing: emit next=land for a clean Interim Review | `VerificationRoundSettlementTests.C544_InterimRouting` | completion header next=review with Final obligation; Found remains next=code |
| PC-68 | DelegationReportFormatter.BuildBrief: omit verification profile for refocus/warm briefs | `VerificationRoundBriefTests.C544_ProfileInEveryBrief` | every inline/spilled brief has the exact current scope, baseline and deferred obligation |
| PC-69 | dispatcher recovery: skip re-enqueue of a committed task whose brief queue insert failed | `VerificationRoundDeliveryTests.C544_BriefHandoffRecovery` | same task's complete correlated UserPrompt eventually exists once |
| PC-70 | AgentTaskReplyService: suppress completion enqueue after settled scope | `VerificationRoundDeliveryTests.C544_CompletionReceipt` | busy and eligible callers each receive one complete matching outcome UserPrompt |
| PC-71 | completion recovery: mark notification complete at enqueue instead of recipient confirmation | `VerificationRoundDeliveryTests.C544_CompletionRecovery` | post-enqueue crash recovers the same identity and exactly one complete caller UserPrompt |
| PC-72 | Test-NightlyMonitorHealth: retain the completedAt <=60m readiness predicate | `NightlyVerificationContractTests.C544_DailyValidity` | today's scheduled green completed eight hours ago with a fresh monitor is ready |
| PC-73 | daily eligibility: allow yesterday's green at or after 08:00 | `NightlyVerificationContractTests.C544_MorningBoundary` | 07:59:59 eligible; 08:00:00 ineligible without today's complete green |
| PC-74 | daily eligibility: ignore a newer failed/incomplete completed attempt | `NightlyVerificationContractTests.C544_NewerFailure` | previous green plus newer red/incomplete yields ReadyForDeferral=false |
| PC-75 | daily eligibility: use latest successful manual run as scheduled green | `NightlyVerificationContractTests.C544_ScheduledIdentity` | manual/partial latest attempt cannot satisfy today's required scheduled run |
| PC-76 | Get-NightlyDueUtcForLondonDate: return local 00:30 as UTC without timezone conversion | `NightlyVerificationContractTests.C544_LondonDates` | exact expected UTC due instants match March/October transition and midnight rows |
| PC-77 | production Windmill API adapter: treat running/unknown result as completed successful | `NightlyVerificationContractTests.C544_ProductionJobAdapter` | queued/running/failed/missing-result job fixtures never qualify as scheduled green |
| PC-78 | checked-in monitor entrypoint: return success immediately when the SSH child exits 255 | `NightlyVerificationContractTests.C544_IndependentOutage` | independent failure event persists and reaches the controlled recipient; no Windows state access is needed |
| PC-79 | nightly monitor notification producer: omit Add-NightlyNotificationEvent before enqueue | `NightlyVerificationContractTests.C544_NotificationIntent` | crash after intent resumes same notification identity and recipient receipt |
| PC-80 | nightly monitor: set transportAccepted on an enqueue exception | `NightlyVerificationContractTests.C544_NotificationRetry` | failed first enqueue is retried and original notificationId is received |
| PC-81 | Test-NightlyNotificationReceipt: return true for transportAccepted or empty recipient view | `NightlyVerificationContractTests.C544_RecipientEvidence` | 200/202/job completion without recipient readback leaves Receipt=false and cannot qualify |
| PC-82 | Test-NightlyNotificationReceipt: omit notificationId equality | `NightlyVerificationContractTests.C544_ReceiptNotificationIdentity` | same run with wrong notificationId stays unreceived |
| PC-83 | Test-NightlyNotificationReceipt: omit nativeRunId equality | `NightlyVerificationContractTests.C544_ReceiptRunIdentity` | same notificationId with wrong nativeRunId stays unreceived |
| PC-84 | notification recovery: allocate a new ID for an existing accepted event | `NightlyVerificationContractTests.C544_NotificationCrash` | recreated monitor imports delayed receipt for original ID; controlled recipient sees one logical event |
| PC-85 | nightly health transition producer: skip the unhealthy-to-healthy recovery notification | `NightlyVerificationContractTests.C544_RecoveryNotification` | one separate correlated recovery event reaches recipient after original failure |
| PC-86 | Get-NightlyNotificationSink: substitute a default destination for empty authorization | `NightlyVerificationContractTests.C544_AuthorizedDestination` | no enqueue when destination is unset; monitor remains unqualified |
| PC-87 | Test-NightlyMonitorHealth: ignore overdue-start while reusing yesterday's green | `NightlyVerificationContractTests.C544_ScheduleHealth` | at grace boundary missing/queued job is unhealthy and no termination is requested |
| PC-88 | queued-readiness failure branch: create a Final replacement automatically | `VerificationRoundDispatchTests.C544_RunningHealthLoss` | running command finishes; replacement count == 0; next Interim refuses |
| PC-89 | stage-review.md: accept exit 0 or list-tests as sufficient selection evidence | `VerificationRoundInstructionTests.C544_ExecutionEvidenceContract` | composed Review explicitly requires fresh executed identities/counts and rejects missing parameter rows |
| PC-90 | AgentTaskService: commit the new Interim task before saving the owner latch | `InterimVerificationPolicyTests.C544_AdmissionTransaction` | injected latch-save failure leaves zero admitted Interim tasks in a fresh connection |
| PC-91 | AgentTaskService follow-up creation: copy the predecessor's Interim round when omitted | `VerificationRoundDispatchTests.C544_FreshFollowUp` | new follow-up Round == Final; continued predecessor remains Interim |
| PC-92 | Test-NightlyMonitorHealth: ignore coverageComplete=false in complete/readiness computation | `NightlyVerificationContractTests.C544_CoverageRequired` | otherwise-valid scheduled row with incomplete coverage is unready |
| PC-93 | Test-NightlyMonitorHealth: ignore testsPassed=false in complete/readiness computation | `NightlyVerificationContractTests.C544_GreenRequired` | otherwise-valid scheduled row with testsPassed=false is unready |
| PC-94 | Test-NightlyMonitorHealth: ignore reportDelivered=false in complete/readiness computation | `NightlyVerificationContractTests.C544_ReportReceiptRequired` | otherwise-valid scheduled row with reportDelivered=false is unready |
| PC-95 | nightly recipient-view adapter: accept evidence for a different destination | `NightlyVerificationContractTests.C544_ReceiptDestination` | same notification/run read from a different destination does not set Receipt |
| PC-96 | nightly recipient-view adapter: treat an ID-only or truncated body as complete receipt | `NightlyVerificationContractTests.C544_ReceiptWholeBody` | matching IDs in a partial body do not qualify receipt |
| PC-97 | nightly recipient-view adapter: ignore receipt attempt floor | `NightlyVerificationContractTests.C544_ReceiptAttemptFloor` | old matching recipient observation leaves Receipt=false |
| PC-98 | independent monitor entrypoint: store outage intent only through the failed Windows SSH hop | `NightlyVerificationContractTests.C544_IndependentState` | restart with Windows unavailable still finds the same intent and delivers it |
| PC-99 | AgentTaskLandService CompleteTerminalLockedAsync: omit the notification for final-scope refusal | `VerificationRoundDeliveryTests.C544_LandRefusalReceipt` | fresh DB has a terminal refusal and its keyed notification; busy/eligible caller later has the matching complete UserPrompt |
| PC-100 | land-notification recovery: skip final-scope refusal notifications whose queue insert is missing | `VerificationRoundDeliveryTests.C544_LandRefusalRecovery` | restarted worker delivers one complete refusal prompt using the original notification ID |
| PC-101 | InterimVerificationPolicy: replace the missing-baseline refusal with admission using an inferred latest same-card outcome | `InterimVerificationPolicyTests.C544_ExplicitBaselineRequired` | missing subject/baseline and first-round cases throw verification_baseline_invalid with zero admitted tasks |

Mutation reports break, intended red, restore and green after land. Code implements
and runs the same tests unmutated as ordinary V/R. Ordinary Review judges their
coverage/evidence before land. All 101 PCs remain pending; the original card and
post-land companion must not claim PC-clean from an ordinary or nightly green.

### Out of scope

- No live deployment, Windmill registration, message send, schedule edit, pilot opt-in
  or credential read in this TestDesign task. S5/S6 are required later acceptance,
  not excluded requirements. Missing recipient authorization blocks that operation.
- This appendix does not select a new general completion outbox or independent
  notification architecture. TD-F1/TD-F2 return to Plan for those seams; replacing
  them with seeded queue rows or sender-created receipt files is prohibited.
- No global AST/TRX impact engine. Automated tests prove policy/scope declarations,
  binding, delivery and guards; ordinary Review and V-12 assess actual cumulative
  coverage and sufficient smoke. No test claims that English instructions guarantee
  an agent obeyed them.
- No repeat of CARD-0487's unrelated 143-PC inventory, scheduler replacement,
  assertion timeout changes, retry-to-green, assembly split, blanket Slow exclusion,
  new stage/status, automatic spend or automatic Mutation. Native project inventory,
  watchdogs and SourceLanding custody stay under their existing plans.
- No blanket Antiphon.Tests assembly rerun in the local floor: named full affected
  classes plus Unit bound this change; broad unattended suites run twice in V-11.
  If implementation changes a shared fixture beyond this bounded set, add its
  consumers and cost before execution. Antiphon.Tests and Agents.Pty never overlap.
- Exhaustive Cartesian products of every invalid identity/clock/state are excluded:
  most refuse at the first gate and hide later guards. Instead run one-field-invalid
  rows from an otherwise valid fixture, the 24 policy/role/mode rows, the 20 land
  checkpoint/evidence rows, the explicit delivery busy/eligible cross-products and
  all exact clock/DST boundaries. Test simultaneous stale-monitor + valid daily green
  and fresh-monitor + invalid daily green explicitly. This isolates every independent
  guard while preserving the meaningful intersections.
- Historical inherited red must be reproduced at the base by exact failing methods.
  Do not widen deadlines, weaken assertions or silently call it flaky. C487_G142's
  observed stale text is a source-inspection finding, not a test run result.

### Cost

All numbers below are **estimates**, not measured test timings. No build, V/R or
PC execution occurred during this document-only dispatch. Prices assume one
foreground owner, owned PostgreSQL clone isolation, existing local Git/pwsh/Git-bash,
one cold setup per stage, and no concurrent Agents.Pty. Exclude engineering/test
authoring time; add that separately to commissioning. Reprice after TD-F1/TD-F2.

| Ordinary V/R floor, per Code or independent Review | Minutes |
|---|---:|
| Setup: restore/build isolated bin-c544, DB fixture preflight, client dependencies | 15 |
| Full Unit lane, including RP/I/B and old instruction/formatter contracts | 8 |
| Named DB/admission/dispatch/settlement/landing/CLI integration classes, excluding delivery/native rows below | 38 |
| Real-Git V-7 capstones | 6 |
| Q plus existing CheckNoteDeliveryHandoffTests delivery/crash rows | 12 |
| Full offline nightly harness, N wrappers, NightlyScriptsTests | 6 |
| Three client files with structured execution report | 3 |
| **Per-stage setup + ordinary V/R** | **88** |

Ordinary execution excluding setup is 73 minutes. Code floor = 88 minutes.
Independent ordinary Review floor = another 88 minutes. This is higher than the
Plan's preliminary 15-35-minute ordinary range because it includes whole existing
affected classes, receipt/restart cuts and real-Git capstones, not just new unit tests.

| PC floor, sequential exact-method red/restore/rebuild/green | Controls | Minutes per cycle | Minutes |
|---|---:|---:|---:|
| Unit parser/instruction/brief (RP/I/B) | 9 | 2 | 18 |
| Readiness file tests (H) | 12 | 2.5 | 30 |
| DB policy/card/dispatch/settlement/landing (P/C/D/S/L) | 51 | 4 | 204 |
| CLI request control | 1 | 3 | 3 |
| Producer-to-session delivery controls (Q) | 5 | 6 | 30 |
| Script/adapter/recipient controls (N) | 23 | 1.5 | 34.5 |
| Mutation setup/build of exact L | - | - | 20 |
| Missing-control discovery, restoration inventory and final evidence | - | - | 15 |
| **Mutation floor, all controls including the seven blocked specifications** | **101** | - | **354.5** |

PC cycles total 319.5 minutes; Mutation setup/discovery totals 35 minutes. Count
every control separately even when one method has several internal cases.
No batch/concurrency saving is assumed: SourceLanding has one managed snapshot;
the seven unexecutable specifications must be resolved, not subtracted.

**Total verification floor = Code setup 15 + ordinary V/R 73 + Mutation setup 20
+ every PC cycle 319.5 + discovery/restoration 15 = 442.5 minutes.**
Adding independent ordinary Review (88) gives **530.5 minutes** for dormant
implementation through post-land verification, before operational S5/S6.

S5 additional estimate: two full unattended runs at 120-360 minutes each, 30 minutes
deployment/readback/preflight, 60 minutes failure/recovery/recipient qualification,
and 15 minutes artifact/receipt reconciliation = **345-825 active minutes**, plus
up to 24 hours awaiting the real schedule boundary. S6 additional **60 minutes**
for bounded pilot selection/run comparison, its actual Final Review and retained
landing/rollback evidence. End-to-end estimated active floor is therefore
**935.5-1415.5 minutes** including independent implementation Review, every PC,
S5 and S6. Overnight wait is elapsed calendar time, not billable execution.

Savings: this card saves **0 verification minutes** by Interim because it is
FullOnly until its policy is qualified; skipping its own controls would be circular.
PC saving assumed **0** because no control is deferred away. For a later measured
pilot, an illustrative nine-repair-round comparison with full F=26 minutes,
interim I=8 minutes (both including setup/smoke), and one extra full Final Review
is 9F=234 versus 9I+F=98: **136 minutes (58.1%) estimated saving**, with common
initial baseline cost canceled from both sides. This is a sensitivity example,
not a claimed saving from the investigation's 233.90 observed minutes. Record
actual same-SHA setup/execution/final amortization; claim no token/dollar saving
without attributable usage.

Code-handoff audit: test/fixture bodies read as inventoried; guards=101, mapped=101,
missing mappings=0, duplicate PC mappings=0. Planned exact C544 methods=109 and
new client cases=3. Executable control specifications=94; seam-dependent
specifications=7 (PC-71/78/85/95/96/97/98). Therefore the required
"all PCs executable" gate **fails** and this is **not a Code-ready verification
design**. The complete guard list makes those gaps visible rather than excluding
them. Plan must close TD-F1/TD-F2, then return to TestDesign to finish the seven
controls and recalculate inventory/cost if the chosen fix adds guards.

--- next stage ---
next: plan
handoff: Historical TestDesign handoff at 5d91dca2; resolved in the following Plan appendix. Its seven seam-dependent controls must receive the dispositions below before Code.
artifact: docs/superpowers/plans/2026-09-16-card-0544-interim-final-verification-plan.md

## Plan resolution of TD-F1/TD-F2

2026-09-16, Plan task **39855344**, inspected checkout **5d91dca2**. The worktree
already had the full TestDesign appendix at its tip; no reset was necessary.
This resolution changes the delivery design and assigns deferred scope. It does
not claim that implementation, ordinary verification, PCs or qualification ran.
The original V/R and G/PC IDs remain an audit inventory; the ownership table below
overrides their earlier assumption that all 101 belong to one implementation.

### Resolution ground truth

| Assumption/question | Code or observed record | Decision/consequence |
|---|---|---|
| CARD-0481 centralized all task settlement recovery. | Its F2-F4 work covers watchdog `DeliveryFailure` obligations through `AgentTaskLandNotifications`. `AgentTaskReplyService.PersistDeliverThenReleaseAsync` still saves and then calls `DeliverToParentAsync`, whose enqueue exception is caught. | Reuse that outbox, but add the ordinary completion producer; this is a required implementation change, not an already-fixed gap. |
| A missing ordinary completion can be found by the completion scanner. | `CompletionNoteWorkHostedService.RecoverMissingSourcedCompletionNotesAsync` requires `SourceLandingOperationId != null`; ordinary tasks do not qualify. Its other scan only sees existing queue rows. | The new producer commits an obligation; the existing notification scanner discovers it even when no queue row exists. Do not widen the sourced scan. |
| A task/root stamp can identify the owed round. | `CompletionNoteStamp` is enqueue/check suppression. `AgentTaskCheckService.HasCompletionNoteAsync` accepts a root-level note or stamp, including an earlier task. | Use the exact settlement event and notification ID for delivery deduplication; root/stamp is never completion receipt. |
| Adding one enum member is all that reuse requires. | `AgentTaskLandNotificationService.ReconcileAsync` matches the immutable `Body`. Queue distillation, polled shrinking and hold cleanup currently exclude every `SourceLandNotificationId`; spill changes queue Body before typing. | Add a kind-specific completion rendering contract. Do not silently disable existing distillation or relax receipt for land/failure notifications. |
| Recovery can ignore callers that have stopped. | CARD-0481 F3/F4 rediscover the keyed row and validate destination/digest before gating new input on Stopped/Failed. | Preserve that order; existing receipt can confirm after the caller stops. No restart/replacement caller is authorized. |
| Windmill's health task detects Windmill loss independently. | `antiphon-nightly-health.json` executes only a Windows SSH command; its schedule has `on_failure: null`. Production notification enqueue also calls Windmill. | A failure in Windmill cannot reliably launch its own detector or send through that queue. CARD-0545 must provide an independent watchdog and transport. |
| Nightly receipt is a production recipient readback. | `Get-NightlyRecipientView` reads `notification-receipts.json` plus a test-only seam. `Test-NightlyNotificationReceipt` compares notification/run IDs only. `Invoke-AntiphonNightlyHealth` produces failures but no healthy-transition recovery event. | Receipt provenance, destination, whole content, attempt floor and recovery producer all belong to the deferred adapter work. |
| CARD-0487 Done proves S4. | Live card read and thread on 2026-09-16 show Done; its close revision records shipped infrastructure and unfinished follow-up. Board title census found no open nightly qualification owner. | Created same-board **CARD-0545**, `b1c1ed0b-2608-40e7-99f5-f6587e9ed416`, Backlog. Preserve CARD-0487's historical closure; no duplicate reopen or task spawn. |

Source anchors: `server/Application/Services/{AgentTaskReplyService,
AgentTaskLandNotificationService,CompletionNoteStamp,AgentTaskCheckService,
SessionMessageQueueService,OutputDistillationService,DataRetentionService}.cs`,
`server/Infrastructure/Orchestration/{CompletionNoteWorkHostedService,
AgentTaskLandNotificationHostedService}.cs`, `server/Domain/Entities/
AgentTaskLandNotification.cs`, `server/Domain/Enums/LandingEnums.cs`, and
`scripts/lib/nightly-health.ps1`. Existing unique indexes are on notification
`SourceEventId` and queue `SourceLandNotificationId` in `AppDbContext`.

### TD-F1: durable obligation and one recovery owner

**Applicability.** New Code and Review tasks with the D-1/D-2 profile version 1
use this path for each terminal reported settlement, whether Interim or Final,
Succeeded or Failed (and a reported Canceled settlement where that path exists).
Nonterminal question/Blocked notes keep their current handling. Session delivery
requires `ReplyTo=Session` and the snapshotted parent. Non-session replies owe no
session prompt. Do not backfill historical ordinary results or change SourceLanding,
specialist, watchdog-failure or landing producers. If another producer already
owns this exact settlement event, it cannot also mint a Completion obligation.

**Atomic producer.** Append `Completion` to `LandNotificationKind`; keep the table
and worker names for compatibility. A small concrete `TaskCompletionNotification`
helper composes the typed snapshot and note; it is not a new transport or worker.
Have reply settlement retain the exact `Completed`/`Failed` event object and its ID
(not the latest event queried afterward: merge-back may add another Completed).
Save the terminal task/result, that event, the StageOutcome where applicable, and
one notification in the same database transaction. Resolve optional display facts
before this write; do not hold a transaction over Git, native operations or model work.
Audit earlier helper saves so a terminal task/outcome cannot escape before its
obligation. The existing status-only `TaskAlreadyPersistedAsync` fallback must verify
the exact committed settlement/event/obligation identity before continuing delivery.
An intent-insert fault rolls back the settlement; notification failure after commit
does not undo its result or strand release ownership.

Add nullable, versioned `CompletionSnapshotJson` and `CompletionDeliveryJson` to
`AgentTaskLandNotification`, with a CLI-generated migration/model snapshot. Other
kinds leave both null and keep their existing immutable-Body rule.

- `CompletionSnapshotJson` is immutable per settlement: task/root/event/outcome
  IDs, parent/reply target, exact raw result plus its SHA-256, existing normalized
  `DelegationNoteDigest`, profile version/round, completed scope, subject/baseline,
  selected artifact revision, pending-final obligation, normalized next/handoff,
  note header, raw fallback body, report-file identity and deliverable coordinates.
  Include the already-computed warning/git/workspace bits; recovery does not infer
  them from current card settings, new task Result, current HEAD or a later outcome.
  Store exact raw bytes/text even if no durable report file was available. The
  notification `Body` is the initial raw fallback; `ContentDigest` for this kind
  is the existing normalized raw-result digest, distinct from a rendered-body hash.
- `CompletionDeliveryJson` records the authorized rendering selected for the
  queue attempt: rendering kind, logical note text, exact submitted wire text and
  hash, queue ID(s) for an existing batch, and any spill path/content hash. It is
  absent until the first attempt is committed. It then freezes the payload for
  replay; the existing queue owns attempt floors/generation and late confirmation.
  Rendered text and immutable raw-result identity are different facts.

Identity is `(SourceEventId, notification.Id, task.Id, ParentSessionId,
raw-result digest)`. The unique `SourceEventId` prevents two obligations for one
settlement; unique `SourceLandNotificationId` prevents two queue rows for that
obligation. A later explicit Continue/Retry and new settlement has a new event,
even if the result text happens to be equal. Re-entry of the same settlement uses
its existing event/obligation. Never use RootTaskId alone as deduplication authority.

**Immediate and restart path.** After the transaction commits, request
`AgentTaskLandNotificationService.ReconcileAsync(notificationId)` using a fresh
scope, then publish/release under existing ownership rules. This is the same path
used by `AgentTaskLandNotificationHostedService` on boot and every scan; optional
checks and the output-distiller worker are not recovery prerequisites. It enqueues
WhenIdle with `SourceLandNotificationId=notification.Id`, `SourceTaskId=task.Id`,
the raw digest, snapshotted header and existing `task:<root>` conversation key.
On insert/lost-ACK/DB-link failure the scanner reuses that same identity. It repairs
the completion enqueue stamp just as DeliveryFailure does, but that stamp cannot
stop reconciliation of an unresolved obligation. The sourced-only scan remains
unchanged and cannot issue a second note for this producer.

Keep CARD-0481's order: discover the existing keyed row; validate destination and
digest; link it; then gate *new* insertion on destination availability. Catch up
the original destination transcript even after Stopped/Failed. Wrong destination,
wrong digest, missing previously linked row or missing destination remains unresolved
with the existing retry/attention state. Do not invent a new caller or replacement
attempted row. Preserve retry backoff, per-row scanner isolation and queue retention.

**Raw, distilled, polled and spilled notes.** Kind-aware checks allow only
`Completion` keyed notes through the existing output-distillation and polled-shrink
paths; Outcome/Conflict/DispatchBase/DeliveryFailure remain excluded. Keep the
finite original distillation deadline, header, canonical report storage, gates and
raw fallback. Crash recovery must not restart the deadline or commission another
model call: recover an existing accepted rendering, otherwise deliver raw after
the original hold expires. A resumed task with a different result cannot lend its
new report to the old notification's distiller.

The queue remains the sole renderer/typer. Before its first typed attempt, validate
the rendering against the immutable snapshot, and persist `CompletionDeliveryJson`
with the queue attempt/floor in one transaction under the existing session/row
locks. Distillation/poll shrinking can update a pending, never-attempted row only;
neither changes immutable profile/evidence/header fields. After a delivery claim,
reject late replacements. For existing size-aware batching, each member stores
its logical note and the same composed wire body/membership; for a spill, preserve
the whole composed content and hash behind the exact pointer. A file write can
precede the DB attempt, but failed publication leaves zero typing. Retry uses the
committed rendering and existing late-confirm brake, not fresh recomposition.

Receipt requires the complete committed wire UserPrompt in the snapshotted caller
after the queue attempt floor, with existing generation safeguards. For a pointer,
also validate the exact referenced content/hash in the recipient workspace. For a
distilled/polled note, receipt proves that exact summary/pointer and its mandatory
header reached the caller; it does not prove the full report was read. A durable
report reference must still resolve to the snapshot's raw result; if the original
file is unavailable, regenerate it from that snapshot through the existing report
store before making a new pointer, never point at a different current task Result.
Unavailable required file evidence leaves delivery unresolved. `ReconcileAsync`
checks this completion rendering; all other kinds still match immutable `Body`.

Never mark Confirmed from queue insertion, Sent, a task API poll, a screen redraw,
an ID-only prompt or a distiller ledger. Once confirmed, retain its receipt/event
identity so retention cannot resurrect the obligation. Preserve unresolved queue,
transcript, snapshot and referenced report/spill evidence until receipt or an
explicit existing disposition; no new generic TTL or automatic cancellation.

### TD-F2: deferred owner and qualification boundary

**Tracking card:** Antiphon **CARD-0545**
(`b1c1ed0b-2608-40e7-99f5-f6587e9ed416`), created and read back in this dispatch,
links CARD-0487 S4 and this plan. It owns the following required work:

| Responsibility | Accountable owner and acceptance |
|---|---|
| Detect Windmill/Windows outage | CARD-0545 Plan/Code supplies an independently supervised watchdog outside the Windows execution host and Windmill scheduler/worker/control-plane failure domain. It actively checks API reachability and expected job/schedule progress. Its timer, durable ledger and alert transport do not depend on the failing Windmill queue or Windows SSH hop. Its Plan selects concrete host/component paths from actual available infrastructure; none is claimed installed here. |
| Persist/recover failure and recovery delivery | That watchdog owns off-host intent before enqueue, stable outage/notification IDs, retry after lost ACK and restart, and a distinct recovery event linked to the outage. An outage before a run uses due-date/outage identity; it never invents a successful native run. An on_failure hook may supplement but cannot be the independent detector. |
| Confirm recipient readback | CARD-0545 Code supplies a production reader of the authorized recipient's own channel/message view or complete session UserPrompt. It validates destination, notification/run-or-outage identity, full content and attempt floor, then imports evidence. Local receipt JSON is a cache of this evidence, never its source. The sender or Windmill job cannot self-certify receipt. |
| Qualify and own morning triage | Its explicitly commissioned Deploy owner records the actual independent host/supervisor/store/transport, authorized destination, reader, credential references and named morning operator. The authorized operator acknowledges the real qualification result. Machine readback establishes receipt; human understanding is not inferred. Missing host/reader/authorization leaves the card pending. |

This is a deliberate **deferred-with-tracking-card** resolution, not a claim that
TD-F2's production adapter is selected or executable in CARD-0544. CARD-0545 starts
with Plan/TestDesign for those concrete components, then Code/Review/land and an
authorized Deploy acceptance. This dispatch creates no schedule, launches no agent,
reads no credential and sends no test notification.

CARD-0545 inherits V-11/S5: manual full unattended green, subsequent real scheduled
green, complete suite and source identities, independent Windows-hop **and Windmill**
outage/recovery tests, busy/eligible recipients and each DL-5 persistence cut. It
must add detection of a stopped independent watchdog (missing heartbeat/freshness)
to its own guard inventory; a healthy last value cannot qualify forever. Deliberate
controls remain local inherited SourceLanding execution. Qualification records
`docs/investigations/<date>-card-0487-nightly-qualification.md`, its card ID, the
CARD-0544 link and actual recipient evidence; only then may it publish D-7's receipt.

CARD-0544 S1-S4 can ship dormant. Keep `InterimVerificationSettings.Enabled=false`,
cards FullOnly, and all existing final-review obligations until CARD-0545 acceptance
and the explicit S6 activation. The readiness reader's qualification contract must
include accepted independent-Windmill outage/recovery and recipient evidence, not
only run/job IDs. Offline fixture values test that contract; they are not a production
receipt. S6 and CARD-0544 operating-policy completion remain pending behind CARD-0545.

### Slice amendments: files and tests

These amend S1-S5; do not add an automatic stage or a second delivery worker.

| Slice | Files | Required ordinary coverage for TestDesign to finish |
|---|---|---|
| S1/S2 completion intent | `server/Domain/Entities/AgentTaskLandNotification.cs`, `server/Domain/Enums/LandingEnums.cs`, `server/Infrastructure/Data/AppDbContext.cs`, CLI-generated migration/snapshot; `AgentTaskReplyService.cs`, new `TaskCompletionNotification.cs` under Application/Services | `VerificationRoundSettlementTests`, `VerificationRoundDeliveryTests`, `AgentTaskReplyIntegrationTests`, `AgentTaskLandNotificationPersistenceTests`: true atomic rollback, exact event selection, profile/raw/outcome snapshot, repeated settlement/Continue identities, non-session and legacy non-replay. |
| S2/S3 shared recovery | `AgentTaskLandNotificationService.cs`, `CompletionNoteStamp.cs`, `AgentTaskCheckService.cs`; existing hosted notification scanner/completion scanner only where needed for kind-aware behavior | Full `AgentTaskLandNotificationRecoveryTests`, `AgentTaskLandReceiptTests`, `ReceiptFailureDeliveryTests`, `DispatchBaseNotificationTests`, `CheckNoteDeliveryHandoffTests`, plus Q: before-insert loss, lost queue ACK, lost wakeup, busy/eligible, stopped/failed caller, wrong destination/digest, retention and repeated restart. Preserve CARD-0481 F2-F4 assertions. |
| S2/S4 rendering/receipt | `SessionMessageQueueService.cs`, `OutputDistillationService.cs`, `DelegationReportFormatter.cs`, `DataRetentionService.cs`, existing report-store implementation if evidence retention needs it; `docs/session-runtime-invariants.md` and `docs/testing-and-build.md` | Full affected `OutputDistillationProducerTests`, `OutputDistillationDeliveryTests`, `OutputDistillationApplyRaceTests`, `OutputDistillationDeadlineTests`, `OutputDistillationCleanupTests`, `PolledCompletionNoteShrinkTests`, `DataRetentionServiceTests`; Q's raw/distilled/inline/spill matrix plus batch membership and immutable-header assertions. TestDesign reads these bodies before finalizing its expanded floor. |
| S4 stays local | D-6/D-7 clock/identity/readiness fixes in `scripts/lib/nightly-health.ps1`, `scripts/test-nightly-health.ps1`, readiness reader/settings and docs | H, N's daily-validity/adapter/coverage/report-result cases, and D readiness loss remain on CARD-0544. Run the full existing offline health harness as regression; do not implement deferred adapter cases with faked receipt files. |
| S4 independence subset + S5 deferred | CARD-0545 owns changes to `scripts/nightly-health.ps1`, `scripts/lib/nightly-health.ps1`, `scripts/windmill/` and the new independent watchdog/recipient adapter selected by its Plan | Inherited N notification/outage cases, new Windmill-down/heartbeat cases and V-11 operational evidence; CARD-0545 must name exact new files/methods before its Code. No guessed adapter implementation in CARD-0544. |

### TestDesign return: preserve IDs and make the seven dispositions explicit

| Existing seam-dependent PC | Resolution and next TestDesign work |
|---|---|
| PC-71 / Q.C544_CompletionRecovery | Now has a concrete producer/owner: atomic reply Completion obligation -> `AgentTaskLandNotificationHostedService` -> `ReconcileAsync` -> keyed queue/rendering -> caller transcript. Finish the method-scoped defect against confirmation-on-enqueue and its intended unconfirmed-before-UserPrompt assertion. Cover both pre-insert recovery and post-observation crash with the same notification ID; add separate controls for independent newly introduced guards. |
| PC-78 / N.C544_IndependentOutage | Deferred required prerequisite on CARD-0545: independent watchdog must operate while Windows SSH fails and while Windmill itself is unavailable. Preserve exact ID/method; finalize the concrete local entrypoint in CARD-0545 TestDesign. |
| PC-85 / N.C544_RecoveryNotification | Deferred on CARD-0545: separate correlated healthy-transition event and real recipient observation. |
| PC-95/96/97 / N.C544_ReceiptDestination/WholeBody/AttemptFloor | Deferred on CARD-0545's production recipient adapter. These remain three distinct guards, with one-field-invalid negatives; no sender-created receipts. |
| PC-98 / N.C544_IndependentState | Deferred on CARD-0545: restart the independent owner with Windows/Windmill unavailable; recover the original off-host obligation. |

Transfer the **whole coupled notification family**, G/PC-78..86 and G/PC-95..98
(13 IDs), plus DL-5, DL-4's independence cases and V-10's notification cases, to
CARD-0545. Keeping seven currently specifiable notification controls in CARD-0544
while their production adapters live elsewhere would divide one safety boundary.
Preserve the original rows above and annotate their new owner; do not delete,
renumber, mark none, or claim them passed. G/PC-77 and 87/92/93/94 stay on CARD-0544
for production job parsing, clock/schedule validity and complete-green predicates.
DL-6/H must continue to reject absent/unaccepted qualification.

The prior 101 controls therefore partition into **88 CARD-0544 controls and 13
deferred prerequisite controls**, before any new guards. Of the seven blocked
specifications, one is resolved for immediate specification and six are explicitly
deferred with their production work. This does **not** make all seven executable.
TestDesign may hand off only the bounded dormant implementation once every
in-scope guard has its executable PC; a handoff claiming qualification or S6 ready
is invalid. Return the dependency and pending IDs in Code/Review reports.

TestDesign must add distinct guard/control rows for atomic completion obligation,
exact settlement identity across Retry/Continue, immutable profile/header through
rendering, persisted wire/spill identity before typing, snapshot/queue destination
and digest checks, distillation deadline across restart, and evidence retention.
Reuse existing assertions where they prove the guard; do not collapse these into
PC-71 or import CARD-0481's whole unrelated capacity suite. Keep existing land/failure
notification regression coverage because the recovery/queue services are shared.

Recalculate commands, expected method counts and numeric costs after reading the
additional fixtures. The earlier 109 methods/101 PCs/88-minute ordinary floor and
530.5-minute total are historical estimates, **not the revised executable budget**.
The old PC allocation assigned 19.5 minutes to the 13 transferred controls
(13 x 1.5); this is moved cost, not a saving. Additional completion rendering and
crash guards add cost. CARD-0545 must separately price independent infrastructure
and qualification; retain S5's two-run/calendar wait obligation. Claim no measured
test-time or dollar saving from this Plan dispatch.

### Resolution validation and handoff

Source tracing and document/identity checks only; no build, ordinary test, PC,
native delivery or live Windmill acceptance executed. CARD-0545 was created in
Backlog and its ID, description and board were independently read back. No source
or test files changed. D-1..D-8 stay authoritative except D-9's completion delivery
extension and D-10's explicit S4/S5 ownership split. The caller's next action is
TestDesign for the amended dormant scope; CARD-0545 is a separate future commission.

--- next stage ---
next: test-design
handoff: Finish TD-F1 PCs using the shared CARD-0481 Completion outbox and rendering contract; retain all IDs, assign the 13 TD-F2 notification controls and S5 to CARD-0545, expand completion regression/guard coverage and recost. Hand off only dormant CARD-0544 Code; Interim activation remains blocked on CARD-0545 qualification and S6.
artifact: docs/superpowers/plans/2026-09-16-card-0544-interim-final-verification-plan.md

## Verification design - D-9/D-10 completion

TestDesign task **cd2866b8**, 2026-09-16; inspected **0c99f7e6**. This appendix
finishes verification for the landed design; it does not revise D-1..D-10. It
supersedes the historical TestDesign's unresolved-seam disposition, delivery DL-2,
execution inventory and Cost. All original V/R and G/PC IDs remain in the document.
The earlier inspection is attributed to task 1193e4ae; the additional body reads
below are this dispatch's own inspection.

**Code-ready scope is dormant CARD-0544 S1-S4 only.** Ship
`InterimVerificationSettings.Enabled=false`, retain FullOnly card defaults and
existing final-review latches. CARD-0545 owns the 13 transferred controls and S5.
CARD-0544 S6 remains a later explicit commission, conditional on accepted CARD-0545
qualification. Offline readiness fixtures confer no production permission. Names
beginning C544 remain tests for Code to implement; no test or PC passed in this
document-only task. Code and independent Review run the full ordinary floor.

### Inspection

| Additional test/fixture bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| AgentTaskLandNotificationPersistenceTests, AgentTaskLandNotificationRecoveryTests and AgentTaskLandReceiptTests: complete bodies, including migration replay, terminal save cuts, recovered destination/digest mismatches, false receipts, stopped callers and retention | Existing non-Completion immutable Body, unique event/queue identities, true transaction rollback and receipt floors -> V-13..17, R-9..13. Keep the historical spilled-land-note refusal. |
| ReceiptFailureDeliveryTests: VerifyCallerFailureAsync, VerifyDeliveryAsync, both interceptors and CreateProvider; CheckNoteDeliveryHandoffTests: Handoff, whole-note/brief assertions and both dispatched busy/eligible tests | Real producer/queue/recipient chain, lost insert/ACK, retained adapter across provider recreation, caller Stopped/Failed after receipt -> V-14, R-10. An interceptor exception plus recreation is not an abrupt process crash. |
| AgentTaskReplyIntegrationTests: initial liveness/release tests, NewDeliveryFactory, AttachTerminal, SeedParentHistoryAsync, AssertParentReceivedNoteAsync, CreateService, SeedDispatchedTaskAsync, SeedFollowUpTaskAsync, SeedSessionAsync, SeedTurnAsync and TestScopeFactory; AgentTaskSettlementRaceTests: BuildHarness, FlushingSessionStopper and SeedSharedTaskAsync | Settlement through OnTurnEndAsync, observable parent history and release ownership -> V-13/14. Helpers default to Docs/Shared and the shared DB; do not copy those defaults into version-1 Code/Review tests. |
| BridgeQueueHarness: complete graph/options, OnSubmitted, transcript/working/generation helpers, runner and disposal; TestDbFixture and DelegationTestServices: complete bodies | Own DB clone, real services, terminal substitute, pre-attempt transcript floor -> V-13..17. BridgeQueueHarness.DisposeAsync deletes rows/files, so it cannot represent just a server restart. |
| OutputDistillationProducerTests, OutputDistillationDeliveryTests (including DeliveryFixture), OutputDistillationApplyRaceTests, OutputDistillationDeadlineTests, OutputDistillationCleanupTests, PolledCompletionNoteShrinkTests and OutputDistillationHarness: complete bodies | Apply/Shadow/off, original deadline, poll/apply/send races, canonical raw storage, headers, UTF-8 ceilings and same-root batch -> V-15/16, R-11/12. Several helpers hard-code the shared database or directly settle the model task; record those substitutes. |
| DataRetentionServiceTests: transcript/queue tests through A_swept_parked_row_is_pruned_after_the_queued_message_retention_window, C508 intent/session/transcript/task-tree and notification-tree tests, CreateService and seeding/cleanup helpers; AgentReportStoreTests and ReportWorkspace: complete bodies | Independent queue/transcript/session/task/file custody and exact raw-byte hash versus normalized digest -> V-17, R-13. ReportWorkspace intentionally uses a persistent owned Git root; a temp-root report would fail custody before the intended assertion. |
| DispatchBaseNotificationTests: lost-ACK, retry, payload/destination, states, NewIntent/seed helpers and CreateProvider | Shared scanner compatibility and no alternate caller -> V-14/17, R-10. Full class remains regression coverage; no claim that its projection-only tests prove recipient delivery. |
| SessionMessageQueueSpillTests: inline/spill, channel batch, file-write failure, Now-mode bodies and ObservableHarnessAsync; SessionMessageQueueWedgedHeadTests: fixture, generation capture/refund, same/changed/legacy generation, replayed marker and failed Enter-only charge bodies | Completion must fail closed on failed spill publication while ordinary UI/channel fallback retains its existing contract; retry must use the original generation/composer rules -> V-15/16, R-12. |
| SessionMessageQueuePtyIntegrationTests: Queued_message_submits_through_the_real_runtime_runner_pty_path, Batched_multiline_body_passes_composer_delivery_verification_and_submits_once, BuildProviderWithVerification, StartSwallowingFakeAsync, SeedIdleClaudeSessionAsync, transcript readers/pump and cleanup; PostLandMutationDeliveryWorker: complete body | Real local runner/ConPTY/FakeClaude transcript capstone and owned child crash pattern -> V-14/16. FakeClaude is not a real provider/model and the pump substitutes runner-to-server ingestion. |
| Production AgentTaskReplyService settlement/event construction, PersistDeliverThenReleaseAsync, TaskAlreadyPersistedAsync and DeliverToParentAsync; notification service/scanner, CompletionNoteWorkHostedService, CompletionNoteStamp, HasCompletionNoteAsync; queue apply/poll/batch/attempt code, DataRetentionService and AgentReportStore | Concrete D-9 seams for all new controls. Status-only fallback, root-level stamps and current task Result are insufficient settlement identities. |

Missing setup, explicitly assigned to Code:

1. Add `tests/Antiphon.Tests/TestHelpers/VerificationRoundCompletionFixture.cs`
   using the inspected BridgeQueueHarness/DeliveryFixture/ReportWorkspace patterns.
   Pass one `CreateIsolatedSchemaAsync` connection to **every** producer, scanner,
   queue, report-store consumer, observer and child. Keep the clone, owned files,
   destination identity and adapter outside the recreatable service provider.
   Register the real reply service, TaskCompletionNotification, notification
   service/hosted scanner, CompletionNoteWorkHostedService, queue, report store,
   distiller and `AddDelegationWorktreeGraph`, plus the new readiness/policy graph.
   Use real version-1 Code/Worktree and Review/ReadOnly task profiles. Give Code
   attributable fixture progress so CompletedWithoutProgress cannot mask a test.
2. Seed a marked report and invoke OnTurnEndAsync; do not seed the expected
   Completion obligation, settlement event, StageOutcome or positive receipt.
   Fault tests may alter a committed row or insert adversarial receipt evidence,
   but delivery acceptance always comes from actual submission. Selectively seed
   legacy/other-kind rows only for negative compatibility tests. The canceled
   report token currently has no separate normal terminal settlement: cancellation
   before a report owes no invented report. If Code exposes a reported-canceled
   path, add it to the same applicability and atomicity rows before Review.
3. Intercept task/outcome/event/notification saves and transaction commit separately.
   A fault after SaveChanges inside a transaction must be observed through another
   connection before rollback/retry; tracker entries alone do not establish atomicity.
   Reuse LandDeliveryBoundary for before-enqueue, queue-inserted, dropped completion
   wakeup, queue-before-typing, queue-before-verdict and receipt-before-save. Add
   fixture-local save/commit and file-publication barriers for the remaining cuts.
   Barriers select task/notification IDs and fire once; no timing-only race.
4. Add `tests/Antiphon.Tests/TestHelpers/VerificationRoundCompletionCrashWorker.cs`
   following PostLandMutationDeliveryWorker. Launch an inherited local child with
   the owned connection/root and exact cut ID; wait for its durable ready marker,
   terminate that owned child, await exit and both output drains, then restart the
   production services over retained state. Do not call graceful Stop/Dispose to
   simulate abrupt loss. Native receipt comes from the child-written JSONL, not a
   queue row copied to TranscriptEntries. No external executor or production runner.
5. Queue time uses System or a real-time offset; direct deadline predicates can use
   FakeTimeProvider with controlled timer advancement. Persist the original request
   deadline even when the completion queue insert has not happened. Distiller replies
   are controlled model outputs through the real RequestAsync/apply path; restarting
   recovery never starts a replacement model call. A report-store fault must leave
   the raw snapshot available even if the canonical file was never written.
6. The new S/Q/H methods below use the original exact class/file aliases and one
   `[Test]` method per name, with named internal rows rather than `[Arguments]`.
   Q/child/native cases carry the assembly ProcessSpawnLimit; queue cases use the
   MessageQueue serialization group. Global shared-store regression fixtures retain
   their existing serialization. Build/stage FakeClaude and the local runner for the
   native capstone. A missing native prerequisite is pending acceptance, not a pass.
7. H extends the trusted qualification receipt fixture with accepted Windows-hop
   outage/recovery, independent Windmill outage/recovery, authorized recipient
   evidence and watchdog-loss detection qualification. These are consumer-contract
   fixtures only. Do not implement CARD-0545's deferred adapter methods or populate a
   production receipt using test data. No second outbox, scanner or TTL is introduced.

### Delivery inventory

DL-1 and DL-3 retain the earlier producer-to-recipient requirements. DL-4 retains
local native/job/clock parsing on CARD-0544; its independent-outage portion moves to
CARD-0545. **All DL-5 and notification/outage V-10 cases move to CARD-0545**, including
busy/eligible recipients and every persistence cut. V-11/S5 moves with them. DL-6
remains CARD-0544's fail-closed readiness consumer, strengthened by V-18 below.

DL-2 is now the following concrete chain. Durable identity throughout is
`(settlement SourceEventId, notification.Id, task.Id, snapshotted ParentSessionId,
normalized raw-result ContentDigest)`; retain the independent exact UTF-8 raw hash,
StageOutcome ID, profile version and selected artifact revision as evidence. An
identical result in a later settlement has a new event/obligation. RootTaskId is
the conversation/batching key, never the deduplication authority.

| Path / producer -> destination | Persistence, recovery and recipient evidence |
|---|---|
| DL-2a: AgentTaskReplyService -> Completion outbox | Save terminal task/result, the exact retained settlement event, StageOutcome where applicable and CompletionSnapshotJson in one transaction. Notification Body is immutable raw fallback. Pre-commit loss leaves none of that settlement durable; retry produces one obligation. A post-commit crash leaves a discoverable obligation. S.C544_CompletionObligationAtomic/CompletionEventIdentity/CompletionPersistedFallback. |
| DL-2b: immediate ReconcileAsync and AgentTaskLandNotificationHostedService -> session queue | Both use the same notification ID as SourceLandNotificationId, the snapshotted destination/header, raw digest and task root key. Recover before-insert loss, rejected insert, lost ACK, failed outbox link and dropped wakeup. Discover and validate an existing keyed row before gating new input on caller availability. Queue insertion and stamps are not receipt. Q.C544_CompletionRecovery/CompletionQueueIdentity/CompletionStoppedReceipt and V-14's full cut matrix. |
| DL-2c: raw/distiller/authenticated poll -> pending queue rendering -> caller | Only Completion is newly eligible for these keyed rendering paths. Before first input persist logical note, exact composed wire text/hash, member IDs, spill identity, attempt floor and generation together. Apply/poll may replace only a never-attempted pending note. On restart use an accepted rendering, otherwise raw at the original deadline; no new model call. Q.C544_CompletionReceipt and the V-15/16 methods carry each variant through the actual queue. |
| DL-2d: report store/spill writer -> recipient workspace -> pointer UserPrompt | Whole composed spill and exact raw report remain independently identified. Failed publication permits zero completion typing. Confirm a pointer only with its complete committed wire UserPrompt plus the exact referenced file/hash at the recipient. Regenerate a lost raw report from the immutable settlement snapshot through AgentReportStore before publishing a new reference. Missing required file evidence remains unresolved. Q.C544_CompletionSpillPublication/CompletionPointerContent/CompletionReportReference/CompletionMissingFile. |
| DL-2e: recipient transcript/catch-up -> notification receipt -> retention | Catch up the original caller even after Stopped/Failed. Confirm only a complete committed wire UserPrompt beyond the attempt floor; preserve receipt/event identity after queue pruning. Crash before receipt save reuses the original evidence and cannot retype. Unresolved queue, transcript, session, snapshot, raw report and spill survive pruning. Q.C544_CompletionRecovery, V-17 and PC-144..154/158. |

Delivery matrix M is **12 rows**: busy/eligible x Raw/Distilled/Polled x Inline/Spill.
Busy means a prior ended turn followed by actual AssistantText with no TurnEnd;
eligible means prior history already ended. First assert zero typing while busy;
then finish that turn and let the real worker/queue deliver. An eligible recipient
must progress without a new artificial TurnEnd. For every row independently read
the complete destination UserPrompt, timestamp/sequence, queue floor/generation,
rendering and file identities. For a batch, compare the whole composed prompt,
not just one member contained in it. Flattened newline receipt is a separate
positive normalization case; ID-only, head-only and head/tail splice are negative.

Q.C544_CompletionRecovery runs M at each of these **eight post-commit cuts**
(96 checked rows): (1) obligation committed before Reconcile; (2) queue insert
rejected; (3) queue committed but ACK lost; (4) queue committed but outbox link save
fails; (5) link committed but wakeup lost; (6) wire/attempt committed before typing;
(7) whole prompt observed before queue verdict save; (8) queue verdict committed
before notification receipt save. Each recovers twice. Cuts 1 and 8 also use abrupt
child loss in Q.C544_CompletionCrashProcess (4 rows: both recipient states at each
cut); cuts 2..7 use faults plus provider recreation. File publication/attempt commit
cuts and distiller apply/poll races are V-15/16. Atomic pre-commit cuts are S's job.
Controls distinguish these substitutes; a caught exception alone proves no crash.

The final receipt assertion is always **one matching complete UserPrompt in the
snapshotted caller after the attempt floor**, with exact spill/raw-report readback
where required. An accepted request, queue insert, task terminal event, polling,
distiller ledger, Sent, screen or transport ACK must never satisfy it. Before
submission each notification must have null ConfirmedAt/ConfirmingPromptSequence.
Summary receipt proves the summary/header/pointer reached the caller, not that the
full report was read or that its instructions were obeyed. BridgeQueueHarness's
adapter proves service composition with controlled terminal evidence; the native
capstone proves local ConPTY/FakeClaude bytes and JSONL. Neither qualifies a real
provider, Windmill independence, live recipient adapter or S5/S6. Test-design Review
must reject producer-only or transport-only acceptance.

### Proves it works now

V-1..9 and V-12 keep their earlier behavioral assertions. V-4 now uses DL-2a..e;
TD-F1 is resolved by D-9, not excluded. V-10 on CARD-0544 is only N's production-job
adapter/local evaluator contract. R-8/V-11 operational receipt remain required on
CARD-0545. Every method in the PC tables is also an ordinary unmutated V/R test.

- V-13: atomic completion identity | isolated PostgreSQL + real OnTurnEndAsync |
  all S methods in PC-102..112 plus S.C544_CompletionMigration | Code/Review x
  Interim/Final x Succeeded/Failed = eight settlements, with/without stage outcome
  where legal; exact raw/profile/evidence/header snapshot. Fail before intent insert,
  after SaveChanges before commit and after commit before reconcile. Pre-commit
  faults leave no terminal result/event/outcome/obligation through a fresh connection.
  Retrying the same report boundary makes one settlement; explicit Continue/Retry
  then a new marked turn makes two even for equal text. Add another Completed event
  during merge-back so a latest-event query fails the exact-event assertion. Migration
  upgrades from its immediately preceding migration, preserves old rows with null
  completion JSON and unchanged enum ordinals, then reruns without replay/backfill.
- V-14: durable recipient recovery | real reply/outbox/scanner/queue |
  Q.C544_CompletionReceipt, CompletionRecovery, CompletionCrashProcess and all Q
  methods PC-113..127 | M and eight cuts above, optional checks and distiller absent,
  stable queue key across restarts, wrong destination/digest independently for both
  linked/unlinked rows and caller Running/Stopped/Failed. Missing/deleted/null caller
  never redirects. An old root stamp does not suppress a new settlement. Poison one
  row before and one after a page boundary; later valid rows still reach recipients.
  Enqueue failure leaves committed result/outcome intact and existing release custody
  complete. No sourced-result scan can produce a second ordinary completion.
- V-15: rendering identity and finite optional work | production distiller/apply,
  authenticated task poll, queue and actual report files | Q methods PC-128..143 and
  PC-159/160 | M plus Apply/Shadow/off; absent/closed/full worker, rejected/empty model
  output; deadline D-1 microsecond, D and D+1 microsecond using PostgreSQL precision;
  restart before and after accepted apply. Race apply-first/claim-first and poll-first/
  claim-first under barriers. Poll the old exact report, a later different report,
  and from a foreign caller; only the matching authenticated old-result poll may
  shrink that still-unattempted note. Header/profile/evidence/next/final obligation
  survive raw, distilled, polled, fallback, inline and spill. Retry uses committed
  wire even after task Result, card policy, current HEAD or current outcome changes.
- V-16: composed bytes and transcript matching | queue, local runner/ConPTY and
  receipt service | Q methods PC-138..149/160 and
  Q.C544_CompletionNativeReceipt | batch same-root two members with distinct settlement
  IDs; different root, origin and noncontiguous member stay separate. Test composed
  UTF-8 ceiling B-1/B/B+1 on inbox and modern profiles with multibyte text, LF and CRLF
  raw text. Every batch member keeps its own logical note plus common wire/membership.
  Fault first and second member persistence: no partial freeze or typing. File write,
  rename/publication and attempt-transaction failure leave zero completion input;
  restart may use only committed rendering. Native method runs busy/eligible x raw
  inline/distilled spill (4 rows) through settlement, outbox and real queue to owned
  FakeClaude JSONL, and independently reads transcript and pointed file. No live
  provider or production runner. The full existing native queue class also runs.
- V-17: receipt/evidence custody | production reconciliation and retention sweeps |
  Q methods PC-150..154/158 and S.C544_CompletionMigration | old unresolved Pending,
  Sent, parked, Truncated and explicitly Canceled rows; expired task/session/transcript
  windows; unrelated eligible controls still prune. Preserve event/snapshot and
  receipt identity even when confirmed queue history prunes. Raw files survive
  worktree removal/provider recreation and later same-task results; same normalized
  digest with different exact bytes cannot overwrite the old raw file. A confirmed
  obligation must not resurrect when queue/transcript disappear. No new TTL or
  silent auto-cancel. Invalid snapshot version and wire hash fail closed.
- V-18: qualification remains an activation prerequisite | real trusted-file reader |
  H.C544_QualificationWindowsOutage, QualificationWindmillOutage,
  QualificationWatchdogLoss plus H.C544_DisabledByDefault/QualificationReceipt and
  D.C544_QueueReadinessLoss | from an otherwise valid fixture, omit or mark unaccepted
  each qualification independently; include failure and distinct correlated recovery
  receipt IDs. Reject missing watchdog-loss/freshness qualification. A fresh monitor
  and scheduled green cannot rescue any missing acceptance. Final remains available;
  fixture readiness never flips the shipped disabled setting or card defaults.

### Guards the regression

- R-9: terminal completion escapes its obligation or binds a merge-back event |
  S.C544_CompletionObligationAtomic/CompletionEventIdentity/CompletionPersistedFallback |
  fresh-context rollback and exact event/outcome/notification join, not status alone.
- R-10: committed completion is stranded, redirected or duplicated |
  Q.C544_CompletionRecovery/CompletionCrashProcess and PC-113..127 methods |
  unconfirmed before input; same notification/queue IDs and one complete caller
  prompt after each recovery, including caller stop and disabled optional work.
- R-11: a late result, policy edit, poll or distillation changes an old completion |
  S.C544_CompletionSnapshotFacts and Q methods PC-128..137 |
  immutable profile/raw/header, original deadline, no new model, no post-claim edit.
- R-12: individual body containment or a pointer falsely certifies the whole batch |
  Q methods PC-138..149/160 and CompletionNativeReceipt |
  committed whole wire, all member identities and exact file readback after the floor;
  invalid evidence stays unconfirmed without retyping an already received prompt.
- R-13: pruning erases an unresolved obligation or reopens a confirmed one |
  Q methods PC-150..154/158 | independent retention predicates preserve the named
  evidence, ordinary unrelated history still prunes, repeated scanners do not resend.
- R-14: deferred qualification silently becomes permission to enable |
  H methods PC-155..157 plus existing PC-22/26/35 | missing or unaccepted independent
  outage/recovery/recipient/watchdog evidence is unready; deployment remains dormant.

### Guard inventory

The original **G-1..101 -> PC-1..101** mappings remain unchanged. The following
ownership ledger annotates every transferred row; none is dropped, renumbered,
passed or assigned "none". Their exact type is NightlyVerificationContractTests in
`tests/Antiphon.Tests/Scripts/NightlyVerificationContractTests.cs`. CARD-0545 must
finish its concrete adapter/entrypoint design before its own Code; these controls
are deliberately **outside this dormant Code handoff**, not executable claims here.

| Preserved guard/control | Preserved exact method | Accountable owner / prerequisite |
|---|---|---|
| G-78 / PC-78 | C544_IndependentOutage | CARD-0545; independent Windows-hop and Windmill outage detector |
| G-79 / PC-79 | C544_NotificationIntent | CARD-0545; off-host intent before enqueue |
| G-80 / PC-80 | C544_NotificationRetry | CARD-0545; retry with original notification identity |
| G-81 / PC-81 | C544_RecipientEvidence | CARD-0545; recipient observation, never transport acceptance |
| G-82 / PC-82 | C544_ReceiptNotificationIdentity | CARD-0545; notification correlation |
| G-83 / PC-83 | C544_ReceiptRunIdentity | CARD-0545; run/outage correlation |
| G-84 / PC-84 | C544_NotificationCrash | CARD-0545; enqueue/observation crash recovery |
| G-85 / PC-85 | C544_RecoveryNotification | CARD-0545; distinct correlated recovery receipt |
| G-86 / PC-86 | C544_AuthorizedDestination | CARD-0545; authorized destination only |
| G-95 / PC-95 | C544_ReceiptDestination | CARD-0545; production destination readback |
| G-96 / PC-96 | C544_ReceiptWholeBody | CARD-0545; complete produced payload |
| G-97 / PC-97 | C544_ReceiptAttemptFloor | CARD-0545; current-attempt observation |
| G-98 / PC-98 | C544_IndependentState | CARD-0545; durable off-host restart recovery |

G/PC-77, 87, 92, 93 and 94 stay here. PC-94 checks the local evaluator's refusal
when reportDelivered=false; it does not validate the deferred receipt producer.
The CARD-0545 owner is tracking card `b1c1ed0b-2608-40e7-99f5-f6587e9ed416`.

Every additional G-n below maps only to PC-n. D-9 is the reference unless D-10 is
explicitly named. Guards with separate bypass paths (including retention, receipt
floors, apply versus shrink, destination versus digest) have separate controls.

| Guard | Safety-critical invariant | Positive control |
|---|---|---|
| G-102 | Settlement/result/event/outcome cannot commit without its Completion obligation, including earlier helper saves | PC-102 |
| G-103 | SourceEventId is the exact settlement event, never the latest later Completed | PC-103 |
| G-104 | Concurrency fallback verifies the committed event/obligation identity, not terminal status alone | PC-104 |
| G-105 | Snapshot retains exact raw bytes/hash independently of normalized ContentDigest | PC-105 |
| G-106 | Recovery reads immutable admitted profile/evidence/display facts rather than current mutable task/card data | PC-106 |
| G-107 | Re-entry of one settlement retains one event/obligation | PC-107 |
| G-108 | A new explicit Retry/Continue settlement creates a distinct obligation even with equal result/root | PC-108 |
| G-109 | Only new version-1 Code/Review profiles enter the Completion producer; no historical replay | PC-109 |
| G-110 | Question/Blocked nonterminal notes cannot create terminal Completion obligations | PC-110 |
| G-111 | An event owned by another notification producer cannot also mint Completion | PC-111 |
| G-112 | Non-session reply snapshots owe no session enqueue | PC-112 |
| G-113 | Missing original destination remains owed; no replacement caller is invented | PC-113 |
| G-114 | Keyed distillation admits Completion only; other notification kinds retain immutable Body | PC-114 |
| G-115 | Keyed poll shrinking admits Completion only | PC-115 |
| G-116 | Kind-aware hold cleanup releases eligible Completion without releasing other keyed kinds | PC-116 |
| G-117 | Recovered queue destination matches the snapshot, whether the row was linked or rediscovered | PC-117 |
| G-118 | Recovered queue raw digest matches the snapshot, independently of body/destination equality | PC-118 |
| G-119 | A missing previously linked queue row is unresolved, never silently replaced | PC-119 |
| G-120 | Caller Stopped/Failed gates new input but cannot prevent existing-row receipt catch-up | PC-120 |
| G-121 | A root/task enqueue stamp is not authority to stop unresolved Completion reconciliation | PC-121 |
| G-122 | Mandatory notification recovery runs without checks or a distiller worker | PC-122 |
| G-123 | Immediate and recovered enqueue share the unique notification key after lost ACK | PC-123 |
| G-124 | Recovery repairs check-suppression stamps without declaring receipt | PC-124 |
| G-125 | Failed enqueue preserves bounded retry/backoff, including the due-time gate | PC-125 |
| G-126 | One corrupt completion cannot starve later scanner rows/pages | PC-126 |
| G-127 | Post-commit notification failure preserves terminal result and release ownership | PC-127 |
| G-128 | Restart never extends the original distillation deadline | PC-128 |
| G-129 | Restart does not commission another model call for the old obligation | PC-129 |
| G-130 | An old obligation's distillation cannot borrow a resumed task's different raw report | PC-130 |
| G-131 | Distillation cannot replace an attempted/claimed completion | PC-131 |
| G-132 | Poll shrinking cannot replace an attempted/claimed completion | PC-132 |
| G-133 | Poll shrinking requires the authenticated matching raw-result digest | PC-133 |
| G-134 | Every authorized rendering preserves the immutable mandatory header/profile/evidence/handoff | PC-134 |
| G-135 | Rendering and queue attempt/floor commit atomically before any completion bytes | PC-135 |
| G-136 | Pre-typing rendering validation rejects a payload inconsistent with the snapshot | PC-136 |
| G-137 | A retry uses the already committed wire payload rather than recomposing | PC-137 |
| G-138 | A batch member retains its own logical completion note | PC-138 |
| G-139 | Every batch member persists the same complete wire/membership atomically | PC-139 |
| G-140 | Failed completion spill publication permits zero typing | PC-140 |
| G-141 | Pointer receipt validates the exact referenced content/hash | PC-141 |
| G-142 | A regenerated raw-report reference resolves to the old snapshot, never current task Result | PC-142 |
| G-143 | Missing/unreadable required report or spill evidence leaves delivery unresolved | PC-143 |
| G-144 | Only recipient UserPrompt evidence can confirm completion | PC-144 |
| G-145 | Receipt requires the whole committed wire body, not matching IDs or fragments | PC-145 |
| G-146 | Sequence receipt must be strictly beyond the queue attempt baseline | PC-146 |
| G-147 | Null-sequence receipt obeys the attempt-time floor and existing tolerance | PC-147 |
| G-148 | Enter-only completion retry is confined to the generation that took the typing | PC-148 |
| G-149 | Late transcript confirmation precedes retyping/recomposition | PC-149 |
| G-150 | Unresolved keyed completion queue rows survive pruning | PC-150 |
| G-151 | Unresolved completion protects the destination's whole transcript | PC-151 |
| G-152 | Completion task tree, event and semantic snapshot survive pruning, including confirmed history | PC-152 |
| G-153 | Unresolved referenced raw-report and spill files survive lifecycle cleanup | PC-153 |
| G-154 | Confirmed receipt/event identity prevents resurrection after queue pruning | PC-154 |
| G-155 | D-10: Windows-hop outage and recovery acceptance is required by the readiness consumer | PC-155 |
| G-156 | D-10: independent Windmill outage and recovery acceptance is separately required | PC-156 |
| G-157 | D-10: qualification includes detection of watchdog loss/stale heartbeat | PC-157 |
| G-158 | Unresolved completion protects the snapshotted destination session independently of mutable task links | PC-158 |
| G-159 | Unsupported/malformed completion snapshot versions fail closed | PC-159 |
| G-160 | Committed wire text must agree with its persisted exact hash before receipt | PC-160 |

### Positive controls

Retain original PC-1..101 except the following executable D-9 clarifications.
PC-70's old direct-enqueue omission is superseded: notification scanning would
correctly repair it, so it is not a decisive mutation of the new design.

| Control | Compiling production defect | Exact method | Required intended red assertion |
|---|---|---|---|
| PC-70 | TaskCompletionNotification: construct a raw fallback without the final-obligation/profile header, keeping valid task/event/digest identity | VerificationRoundDeliveryTests.C544_CompletionReceipt | Independently read caller UserPrompt/logical spill lacks the exact snapshotted mandatory header; equality to the expected admitted profile/header must fail |
| PC-71 | AgentTaskLandNotificationService.ReconcileAsync: in the Completion enqueue/link arm set State=Confirmed and ConfirmedAt=now before any transcript observation | VerificationRoundDeliveryTests.C544_CompletionRecovery | Immediately after enqueue with submission held, ConfirmedAt.ShouldBeNull and ConfirmingPromptSequence.ShouldBeNull; fail here, before eventual receipt checks |

All rows below specify production changes, not changes to expected test text.
S/Q/H expand to the exact classes/files defined in the original alias table. The
exact method column is fully expanded to remove command ambiguity. Start from a
passing unmutated method and an otherwise-valid fixture; isolate one invalid field
at a time. PC red must be the named behavioral assertion, never build/discovery or
fixture failure. Each method executes once and reports its internal row inventory.

| Control | Compiling production defect | Exact method | Required intended red assertion |
|---|---|---|---|
| PC-102 | Commit terminal task/event/outcome before inserting the Completion obligation | VerificationRoundSettlementTests.C544_CompletionObligationAtomic | At the rejected obligation insert, a fresh connection must see zero committed terminal settlements/outcomes; terminal state escaping the transaction fails |
| PC-103 | Select the latest Completed event after merge-back instead of retaining the report-settlement event ID | VerificationRoundSettlementTests.C544_CompletionEventIdentity | notification.SourceEventId.ShouldBe(reportSettlementEventId), with a later merge-back Completed present |
| PC-104 | TaskAlreadyPersistedAsync returns true on Status/CompletedAt alone | VerificationRoundSettlementTests.C544_CompletionPersistedFallback | A terminal row with a different event or missing/wrong obligation must not be accepted as this settlement; acceptedFallback.ShouldBeFalse |
| PC-105 | Store normalized report text as RawReport and hash that instead of the original UTF-8 bytes | VerificationRoundSettlementTests.C544_CompletionRawIdentity | RawReport UTF-8 bytes and exact hash equal the submitted CRLF/trailing-space/non-ASCII report; normalized-equivalent reports retain different raw hashes |
| PC-106 | Reconciliation formats profile/evidence/display facts from the current task/card instead of CompletionSnapshotJson | VerificationRoundSettlementTests.C544_CompletionSnapshotFacts | After independently changing each mutable fact, recovered logical note and recipient header equal the original admitted snapshot |
| PC-107 | Re-entered settlement creates a fresh event and obligation instead of returning the persisted pair | VerificationRoundSettlementTests.C544_CompletionSameSettlement | After duplicate marked-turn callbacks and provider recreation, settlement-event count and Completion obligation count both equal one |
| PC-108 | Deduplicate a new settlement by root/result digest and reuse its predecessor's notification | VerificationRoundSettlementTests.C544_CompletionNewSettlement | Explicit Retry and Continue rows with equal text each produce distinct SourceEventId/notification IDs and each reaches its caller once |
| PC-109 | Remove the version-1 profile requirement from Completion producer eligibility | VerificationRoundSettlementTests.C544_CompletionProfileEligibility | New legacy/null-profile Code/Review settlements and historical terminal rows produce zero Completion obligations; valid version-1 Code/Review controls do produce them |
| PC-110 | Include Blocked/question reports in terminal Completion production | VerificationRoundSettlementTests.C544_CompletionTerminalOnly | A marked question remains nonterminal with zero Completion obligations; later terminal report has exactly one |
| PC-111 | Call the Completion producer even when the event already owns DeliveryFailure | VerificationRoundSettlementTests.C544_CompletionProducerExclusion | Real settlement returns without a uniqueness exception and the event has exactly its one intended notification kind; capture product exception and assert ShouldBeNull |
| PC-112 | Give a snapshotted non-session reply kind initial Queued state instead of NotRequired | VerificationRoundSettlementTests.C544_CompletionNonSession | With a valid parent ID also present, ReplyKind=None/non-session produces zero session input and a NotRequired obligation |
| PC-113 | On missing snapshotted caller, substitute the task's current ParentSessionId | VerificationRoundDeliveryTests.C544_CompletionDestinationMissing | After changing the task to another live caller, original missing/deleted/null destination stays unresolved and both callers have zero new inputs |
| PC-114 | Allow every SourceLandNotificationId kind through TryApplyDistillationAsync's kind gate | VerificationRoundDeliveryTests.C544_CompletionDistillationKind | An otherwise eligible non-Completion keyed note's Body and hold remain unchanged; a Completion control applies and is received |
| PC-115 | Remove the non-Completion kind exclusion from authenticated poll shrinking | VerificationRoundDeliveryTests.C544_CompletionPollKind | Matching authorized poll cannot change any other keyed kind's Body/digest; Completion control shrinks and is received |
| PC-116 | Release all keyed holds during completion cleanup instead of only Completion | VerificationRoundDeliveryTests.C544_CompletionHoldKind | Expired Completion hold clears, while an otherwise identical non-Completion hold remains unchanged |
| PC-117 | Omit recovered queued-message destination equality | VerificationRoundDeliveryTests.C544_CompletionQueueDestination | For linked and rediscovered rows independently, wrong destination with valid digest/whole prompt leaves ConfirmedAt null and creates zero replacement rows |
| PC-118 | Omit recovered queued-message ContentDigest equality | VerificationRoundDeliveryTests.C544_CompletionQueueDigest | For linked and rediscovered rows independently, wrong digest with valid destination/body/whole prompt leaves ConfirmedAt null |
| PC-119 | Clear QueueMessageId when its row is absent, allowing enqueue again | VerificationRoundDeliveryTests.C544_CompletionMissingLinkedRow | After deleting the linked row, repeated reconciliation creates zero queue rows/inputs and retains the unresolved link identity |
| PC-120 | Return for Stopped/Failed caller before existing keyed-row discovery and receipt reconciliation | VerificationRoundDeliveryTests.C544_CompletionStoppedReceipt | A previously submitted complete prompt confirms the same obligation after caller stop/failure; no new input is sent |
| PC-121 | Return early when CompletionNoteEnqueuedAt/root stamp is non-null | VerificationRoundDeliveryTests.C544_CompletionStampIsNotReceipt | A later equal-text settlement with an old root stamp still receives its own complete prompt and confirms its own event |
| PC-122 | Exclude Completion from AgentTaskLandNotificationHostedService's mandatory scan | VerificationRoundDeliveryTests.C544_CompletionMandatoryScanner | With immediate wakeup dropped and optional checks/distiller absent, boot and periodic scans each deliver the owed complete prompt |
| PC-123 | Enqueue recovered Completion without SourceLandNotificationId | VerificationRoundDeliveryTests.C544_CompletionQueueIdentity | Queue row's key equals notification.Id; concurrent immediate/recovery and lost-ACK retries leave exactly one keyed row and one received prompt |
| PC-124 | Skip completion/root check-suppression stamp repair after rediscovering a committed row | VerificationRoundDeliveryTests.C544_CompletionStampRepair | Fresh-context stamps become populated, ConfirmedAt remains null while input is held, and later whole-prompt receipt confirms |
| PC-125 | Remove the future NextAttemptAt gate for an unresolved, unlinked Completion | VerificationRoundDeliveryTests.C544_CompletionRetryBackoff | A scan one microsecond before due adds no attempt; due equality attempts once; successive delays remain 5/10/20/40/80/160/300/300 seconds |
| PC-126 | Break the scanner page loop on one row's reconcile exception | VerificationRoundDeliveryTests.C544_CompletionScannerIsolation | A later valid row in the same page and a row across the page boundary both produce complete recipient prompts despite the poisoned row |
| PC-127 | Return before release custody when post-commit completion enqueue fails | VerificationRoundDeliveryTests.C544_CompletionReleaseAfterEnqueueFailure | Committed result/event/outcome remain intact and worker release ends killed, warm-pooled or standing-owned as the fixture specifies; no unowned live process/lease remains |
| PC-128 | Set a new distillation deadline when recovery recreates the queue row | VerificationRoundDeliveryTests.C544_CompletionOriginalDeadline | At the original deadline, raw fallback is received without waiting a restarted interval; stored deadline equals the original request value |
| PC-129 | Start another distiller RequestAsync while recovering the same completion | VerificationRoundDeliveryTests.C544_CompletionRestartNoModel | Replacement-model-call count after restart is zero; accepted rendering replays, otherwise original-deadline raw fallback reaches the caller |
| PC-130 | Build old completion's distiller input from current task.Result | VerificationRoundDeliveryTests.C544_CompletionDistillerSourceIdentity | After a later different settlement, recorded model input for the first obligation contains its original exact raw report/digest, never the new result |
| PC-131 | Remove delivery-attempt/status claim exclusion from TryApplyDistillationAsync | VerificationRoundDeliveryTests.C544_CompletionApplyClaim | After claim-first barrier, late apply is refused and pending attempted row Body plus frozen rendering remain unchanged; apply-first control delivers accepted summary |
| PC-132 | Remove DeliveryAttempts==0 from poll-shrink eligibility | VerificationRoundDeliveryTests.C544_CompletionPollClaim | Claim-first late poll leaves attempted row Body and frozen wire unchanged; poll-first control shrinks before first input |
| PC-133 | Ignore report digest equality when applying a valid caller's poll shrink | VerificationRoundDeliveryTests.C544_CompletionPollDigest | A poll of a later different result leaves the old obligation Body unchanged; matching old-result poll is the positive row |
| PC-134 | Omit the profile/next/final-obligation header from the distilled and polled builders | VerificationRoundDeliveryTests.C544_CompletionRenderingHeader | Each valid summary/poll branch must apply and deliver its expected snapshotted mandatory header, including spill readback; refusal to deliver is also a failed positive row |
| PC-135 | Save queue attempt/floor before the transaction that saves CompletionDeliveryJson | VerificationRoundDeliveryTests.C544_CompletionAttemptAtomic | Inject delivery-record persistence failure: fresh context has zero claimed attempts and no partial delivery record; terminal inputs remain empty |
| PC-136 | Skip validation that a candidate rendering preserves the semantic snapshot | VerificationRoundDeliveryTests.C544_CompletionRenderingValidation | Replace a mandatory profile/header field and recompute a valid wire hash: the invalid rendering produces zero inputs and no confirmed receipt |
| PC-137 | Recompose wire on retry from current task/policy instead of frozen CompletionDeliveryJson | VerificationRoundDeliveryTests.C544_CompletionFrozenReplay | After current task/policy/result changes, actual retried input and receipt equal the original committed WireText/hash |
| PC-138 | Store the batch head's logical note for every member | VerificationRoundDeliveryTests.C544_CompletionBatchLogicalNotes | Each member's LogicalNote equals its own expected note; distinct settlement two's note must not equal settlement one's |
| PC-139 | Persist the common wire/membership only for the batch head | VerificationRoundDeliveryTests.C544_CompletionBatchWireIdentity | Every member has identical whole WireText/hash and ordered member IDs before typing; fault on member two rolls all attempt/rendering state back |
| PC-140 | On completion spill publication failure use the ordinary raw-body fallback | VerificationRoundDeliveryTests.C544_CompletionSpillPublication | File-write/rename failure yields zero completion inputs and no attempted/confirmed receipt; ordinary UI/channel fallback regression still passes |
| PC-141 | Omit referenced spill content-hash comparison in completion receipt | VerificationRoundDeliveryTests.C544_CompletionPointerContent | Complete matching pointer UserPrompt plus an existing file with one changed byte leaves ConfirmedAt null; exact restored file permits confirmation |
| PC-142 | Regenerate a missing canonical raw report from current task.Result | VerificationRoundDeliveryTests.C544_CompletionReportReference | Readback bytes/hash equal the old immutable snapshot after newer settlement and worktree removal; received pointer resolves to that exact old report |
| PC-143 | Treat a missing/unreadable required report or spill file as receipt-satisfied | VerificationRoundDeliveryTests.C544_CompletionMissingFile | Otherwise valid complete pointer evidence leaves ConfirmedAt null until required file evidence is available and exact |
| PC-144 | Include QueuedUserPrompt in the notification's accepted transcript kinds | VerificationRoundDeliveryTests.C544_CompletionUserPromptOnly | Exact wire in QueuedUserPrompt/AssistantText/screen-only evidence leaves ConfirmedAt and ConfirmingPromptSequence null; actual UserPrompt control confirms |
| PC-145 | Drop PromptSubmissionMatch.IsCompleteIn from completion receipt | VerificationRoundDeliveryTests.C544_CompletionWholeWire | Matching ID/head, head-tail splice and one member of a batch all leave ConfirmedAt null; complete normalized whole wire confirms |
| PC-146 | Change transcript sequence > attempt floor to >= | VerificationRoundDeliveryTests.C544_CompletionSequenceFloor | Exact wire at floor or below is rejected; actual matching UserPrompt at floor+1 confirms |
| PC-147 | Remove timestamp floor filtering for a null-sequence-baseline attempt | VerificationRoundDeliveryTests.C544_CompletionTimeFloor | Exact wire with null timestamp or one microsecond before the configured time floor remains unconfirmed; equality/after-floor controls confirm |
| PC-148 | Treat a changed retained process generation as unchanged for Completion Enter-only recovery | VerificationRoundDeliveryTests.C544_CompletionGeneration | Same generation with matching composer sends only Enter; changed generation cannot finish the old composer's attempt with Enter-only and must use the current owned generation's safe frozen-body path |
| PC-149 | Skip late-confirmation lookup for Completion before retry input | VerificationRoundDeliveryTests.C544_CompletionLateConfirm | With whole prompt already received and composer empty, reconciliation adds zero terminal inputs and retains exactly one matching UserPrompt |
| PC-150 | Omit unresolved Completion from queued-message retention protection | VerificationRoundDeliveryTests.C544_CompletionQueueRetention | Expired unresolved Sent/Pending/parked/Truncated/Canceled queue rows remain present; unrelated eligible queue row is pruned |
| PC-151 | Omit unresolved Completion from transcript retention protection | VerificationRoundDeliveryTests.C544_CompletionTranscriptRetention | Expired original caller transcript, including complete receipt candidate/floor history, survives a sweep; unrelated transcript prunes |
| PC-152 | Omit Completion from protected task-tree/event retention | VerificationRoundDeliveryTests.C544_CompletionSnapshotRetention | Captured sweep exception.ShouldBeNull and fresh task/event/snapshot rows remain, for unresolved and confirmed history; FK violations from the product sweep count as this assertion's red, never fixture setup failure |
| PC-153 | During lifecycle cleanup delete a raw/spill path still referenced by an unresolved Completion | VerificationRoundDeliveryTests.C544_CompletionFileRetention | Both referenced files exist with unchanged exact bytes after sweeps, worktree removal and recreation; File.Exists.ShouldBeTrue for each |
| PC-154 | On a Confirmed obligation clear QueueMessageId and reset State to Queued during reconcile | VerificationRoundDeliveryTests.C544_CompletionConfirmedNoReplay | After allowed queue/transcript pruning, repeated scanner/reconcile creates zero new queue rows/inputs and retains original Confirmed receipt/event identity |
| PC-155 | Ignore Windows-hop outage/recovery acceptance in the trusted qualification reader | InterimVerificationReadinessTests.C544_QualificationWindowsOutage | Otherwise valid receipt with omitted/unaccepted Windows failure or recovery evidence is unready; fully accepted control is ready only under explicit test settings |
| PC-156 | Ignore independent Windmill outage/recovery acceptance in the qualification reader | InterimVerificationReadinessTests.C544_QualificationWindmillOutage | Otherwise valid receipt missing independent Windmill failure/recovery acceptance is unready despite Windows evidence and fresh monitor |
| PC-157 | Ignore watchdog-loss/stale-heartbeat qualification in the readiness reader | InterimVerificationReadinessTests.C544_QualificationWatchdogLoss | Otherwise valid outage/recovery receipt without accepted watchdog-loss detection remains unready |
| PC-158 | Omit unresolved Completion destinations from PruneSessions protection | VerificationRoundDeliveryTests.C544_CompletionSessionRetention | Expired original destination survives after mutable task ParentSessionId changes and no standing-agent link protects it; unrelated session prunes |
| PC-159 | Interpret unsupported completion snapshot versions as version 1 | VerificationRoundDeliveryTests.C544_CompletionSnapshotVersion | Unsupported version and malformed required data each remain unresolved with zero new inputs; supported complete snapshot recovers |
| PC-160 | Ignore stored exact WireHash when reading CompletionDeliveryJson for receipt | VerificationRoundDeliveryTests.C544_CompletionWireHash | Otherwise correct full UserPrompt with wire text/hash disagreement cannot confirm; corrected durable hash allows normal receipt |

For PC-144..147 and PC-160, block actual submission after a real producer/attempt
commit, add just the adversarial evidence, invoke real reconciliation and assert
unconfirmed **before** releasing submission. Then receive an actual complete prompt
as the positive row. Timestamp cases use the production tolerance in the floor,
not a different test-only tolerance. PC-148 uses the existing generation fixture
and tests null, same and changed generations; it never sends to a stale process.
PC-149 starts with actual submitted transcript evidence retained across recreation,
not a hand-authored successful receipt. Receipt floor tests and pointer validation
are independent, so each invalid row has all other fields valid.

The mutations above are bounded edits at the named production guard: for example
PC-153 inserts deletion of the already parsed referenced paths in the real cleanup
path, rather than deleting fixtures from the test. It compiles against the actual
snapshot type landed by Code. Mutation must record the concrete source diff and
the named intended assertion for each control; an alternate assertion needs
ordinary Review justification, not credit for any failure. Race tests must assert
their barrier was reached and intended interleaving ran. Do not count a timeout,
zero discovery, process-start failure or missing DB/file fixture as a killed PC.

### Out of scope

- CARD-0545 owns G/PC-78..86 and 95..98, DL-5, DL-4's independent outage path and
  V-11/S5. Their existing specifications and the explicit owner ledger remain
  binding. Choosing/building the independent deployment, real recipient adapter,
  live notification and watchdog-loss production controls is not dormant S1-S4.
  Local H/N contract fixtures cannot complete those obligations.
- S6 activation is a later explicit CARD-0544 commission after accepted CARD-0545
  qualification. This dispatch and Code do not deploy, enable policy, opt cards in,
  message a live recipient or populate production readiness files.
- The new Completion producer covers newly profiled Code/Review terminal reports.
  Historical/manual/legacy, ordinary question/Blocked and events owned by another
  notification producer are compatibility/refusal cases, not backfill candidates.
  No new automatic cancellation, destination replacement, outbox, scanner or TTL.
- The cancellation-token path before a terminal report is excluded from the eight
  terminal settlement rows because it has no terminal report/event to deliver.
  It remains a zero-obligation assertion in C544_CompletionTerminalOnly. If actual
  Code changes this contract, expand the same tests and guard inventory before land.
- Do not multiply all malformed fields together: a first refusal masks later
  guards. Use one-invalid-field rows plus the explicit policy, settlement, M/cut,
  batch, deadline and state intersections above. Distilled/polled x spill uses a
  low owned fixture threshold/long mandatory header so those paths are exercised
  without depending on an unrealistically long successful model summary.
- Abrupt process loss runs at the atomic-obligation and final receipt handoffs;
  each intervening handoff has deterministic faults and recreated providers, with
  retained store/terminal evidence. These prove recovery logic, not OS durability
  under machine power loss. Native ConPTY/FakeClaude is the bounded transport
  capstone, not proof of real provider behavior or production qualification.
- No full assembly or unrelated CARD-0487 PC repetition is added to the local
  floor. Full affected classes and Unit remain mandatory; a newly changed shared
  helper adds its consumers and cost. SourceLanding mutation is later, isolated
  and post-land, never a substitute for ordinary Code/Review execution.

### Cost

**Estimated, not measured.** No build or test was run in this documentation task.
This replaces the earlier 530.5-minute local total; it is not added on top of it.
Authoring/repair time is excluded. Assume one foreground owner, isolated PostgreSQL
clone, persistent owned report root, local Git/pwsh, staged FakeClaude/runner and no
overlapping Antiphon.Agents.Pty.Tests. A required skip remains incomplete evidence.

The complete ordinary execution inventory is the earlier named full-class list,
with N narrowed to its ten retained controls, plus these **full existing classes**:

- AgentTaskReplyIntegrationTests, AgentTaskSettlementRaceTests,
  AgentTaskLandNotificationPersistenceTests, AgentTaskLandNotificationRecoveryTests,
  AgentTaskLandReceiptTests, ReceiptFailureDeliveryTests and DispatchBaseNotificationTests.
- OutputDistillationProducerTests, OutputDistillationDeliveryTests,
  OutputDistillationApplyRaceTests, OutputDistillationDeadlineTests,
  OutputDistillationCleanupTests and PolledCompletionNoteShrinkTests.
- DataRetentionServiceTests, AgentReportStoreTests, SessionMessageQueueServiceTests,
  SessionMessageQueueDeliveryVerificationTests, SessionMessageQueueInterruptedAttemptTests,
  SessionMessageQueueSpillTests, SessionMessageQueueWedgedHeadTests and
  SessionMessageQueuePtyIntegrationTests.

Further bodies read for these queue regressions: InterruptedAttemptTests complete;
ServiceTests' hold/SendNow tests, CreateHarnessAsync, transcript/queue helpers and
disposal; DeliveryVerificationTests' initial composer/ack/watchdog tests,
ObservableHarnessAsync, swallowed/stale-body/screen-only tests and null-baseline
timestamp/late-confirm tests. Boundaries map to V-15/16 and R-12. Existing degraded
screen-only queue verdicts must remain compatible but cannot confirm Completion.

Run the earlier build, full Unit, full script and three-file client commands.
For each named integration class use the exact filter `/*/*/ClassName/*` with the
isolated built output and a distinct TRX directory. N's ten local methods are
C544_ScriptExitPropagation, C544_ScheduledIdentity, C544_LondonDates,
C544_ProductionJobAdapter, C544_ScheduleHealth, C544_DailyValidity,
C544_CoverageRequired, C544_GreenRequired, C544_ReportReceiptRequired and
C544_SelectionHash. The original PC table is authoritative for their exact names;
the name audit below must match it before Code runs. Do not implement the thirteen
deferred N methods as ignored/passing stubs. Full `test-nightly-health.ps1` means all
implemented local harness cases, with a separate thirteen-control deferral ledger.

There are **158 distinct local C544 backend methods**: 101 original minus 13
transferred plus 59 new controls plus the original eight supplemental methods plus
C544_CompletionMigration, C544_CompletionCrashProcess and
C544_CompletionNativeReceipt. The UI floor remains three named cases. Discovery
must execute all 158; matrix row counts (including 96 recovery rows, four abrupt
crash rows and four native rows) are reported separately, not counted as discovered
methods. Existing-class counts must be recorded from fresh candidate TRX, not
invented here. Ordinary Review repeats this same full floor at the reviewed SHA.

| Ordinary Code V/R floor (also repeated by independent Review) | Minutes |
|---|---:|
| Setup/build: isolated bin-c544 output, DB clone, client dependencies, owned report root, local runner/FakeClaude stage | 22 |
| Full Unit lane, including RP/I/B and existing formatter/instruction contracts | 8 |
| Retained admission/card/dispatch/land/CLI classes, excluding separately priced S/Q delivery and native work | 38 |
| S completion identity/transaction/migration methods and existing reply/settlement/retention regressions | 24 |
| Real-Git V-7 capstones | 6 |
| Full Q controlled delivery/recovery matrix, barriers and abrupt crash child, excluding native capstone | 40 |
| Shared notification, check-handoff and non-native queue regression classes | 26 |
| Full distillation, poll and report-store classes | 24 |
| Full existing native queue class plus Q.C544_CompletionNativeReceipt | 12 |
| Local N wrappers, NightlyScriptsTests and full offline nightly-health harness | 5 |
| Three client files and structured report | 3 |
| **Code setup + ordinary V/R** | **208** |

Ordinary execution alone is **186 minutes**, plus setup/build **22 = 208**.
Independent ordinary Review is another **208 minutes**. Run classes sequentially
and keep source frozen while each run executes. Commit before a long run; retain
SHA, exact filters, discovered/executed/failed/skipped counts and row inventory.
Confirm inherited failures with the exact failing methods at the base revision.

Every PC cycle is method-scoped and includes a compiling defect, intended red,
restoration/rebuild and that same exact method green. Example for PC-102, after
building each corresponding mutation/restoration to its isolated output:

~~~powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c544/ -- --treenode-filter '/*/*/VerificationRoundSettlementTests/C544_CompletionObligationAtomic' --report-trx --report-trx-filename pc102.trx --results-directory .antiphon/c544-pc102-red
~~~

Use a separate green result directory after restoring/rebuilding; substitute each
row's exact class/method for the other controls. Execute/await in the foreground.
No whole-class PC cycles and no swallowed failures. Record break/red/restore/green
per ID after land. Code implements and runs ordinary V/R; independent ordinary
Review assesses that evidence before land. All PC outcomes remain pending until
Mutation runs them. Clean isolated build outputs per the testing owner afterward.

| Mutation PC floor | Local controls | Minutes per complete cycle | Minutes |
|---|---:|---:|---:|
| Unit parser/instruction/brief RP/I/B | 9 | 2 | 18 |
| Readiness H (12 retained + 3 added) | 15 | 2.5 | 37.5 |
| DB P/C/D/S/L (51 retained + 11 added) | 62 | 4 | 248 |
| CLI request | 1 | 3 | 3 |
| Producer-to-session Q (5 retained + 45 added) | 50 | 6 | 300 |
| Local N (23 original - 13 transferred) | 10 | 1.5 | 15 |
| Mutation setup/build at landed revision | - | - | 20 |
| Discovery, restoration inventory and final evidence | - | - | 15 |
| **Mutation floor** | **147** | - | **656.5** |

PC cycles total **621.5 minutes**; with **20** setup/build and **15** evidence,
Mutation is **656.5**. Thus the dormant verification floor is **208 + 656.5 =
864.5 minutes**, or **1,072.5 minutes including independent Review**. The 59 new
cycles add **321.5** minutes; moving 13 old cycles transfers **19.5** historical
estimated minutes to CARD-0545. Two ordinary stages increase by **240** minutes.
Total local increase over 530.5 is therefore **321.5 - 19.5 + 240 = 542** minutes.
These numbers are planning estimates; record actuals and recost additional helper
consumers before execution instead of silently reducing the floor.

The transferred 19.5-minute PC estimate is not a budget for CARD-0545's new
independent facility, adapter or watchdog-loss controls; its TestDesign must cost
those concrete controls and its Code/Review separately. Retain the prior S5 lower
bound: two unattended full runs at 120-360 minutes each, 30 minutes deployment/
readback, 60 receipt/recovery and 15 artifact = **345-825 active minutes**, plus
up to **24 hours** waiting for the scheduled run. Retain later S6 **60 minutes**.
Including only these already-priced deferred obligations gives a lower bound of
**1,497-1,977 active minutes** (1,072.5 + 19.5 + 345-825 + 60), before CARD-0545's
additional implementation/review/controls. It is not an activation cost cap.

Savings for this implementation are **zero minutes of required ordinary or PC
coverage removed**, **zero measured runtime/dollar savings**, and **zero permitted
Interim runs before qualification/S6**. The 13-control transfer moves ownership;
it does not eliminate verification. Dormant FullOnly implementation and the new
recovery guarantees require the complete local floor. A later pilot may measure
cumulative-selection savings under S6; none are assumed to fund this work.

Handoff audit: inspected bodies/fixtures above; **total guards=160, mapped=160,
missing=0, duplicate PC mappings=0**. Local scope is **147 guards/147 distinct
executable PC specifications**; the **13** transferred IDs each retain an explicit
CARD-0545 owner, method and prerequisite, and are not claimed executable/passed in
this scope. Original G/PC-1..101 remain present; PC-70/71 have the D-9 executable
clarifications above. Numeric local and deferred cost boundaries are explicit.
Code must implement the named fixtures/tests; no unimplemented test is evidence.
There is no unresolved local architectural choice and no permission to activate.

--- next stage ---
next: code
handoff: Implement dormant CARD-0544 S1-S4 and the appended D-9/D-10 verification design. Run the 158 local C544 backend methods plus full affected suites; preserve all 160 G/PC IDs and CARD-0545's 13-control ledger. Keep Enabled=false and FullOnly defaults. CARD-0545 qualification/S5 and a later explicit CARD-0544 S6 are required before activation.
artifact: docs/superpowers/plans/2026-09-16-card-0544-interim-final-verification-plan.md
