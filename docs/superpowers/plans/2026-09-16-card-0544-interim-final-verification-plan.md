# CARD-0544: explicit interim rounds and full final verification

Date: 2026-09-16. Stage: Plan. Source inspected: `2c1a7a08613dd25152dff9586d242ffbb214b56c`.
Separate TestDesign follows this plan; this dispatch does not fold that stage.

Introduce an explicitly requested, per-card/per-role interim verification mode for
repair work. Preserve full ordinary verification for the initial baseline and final
pre-land review. Complete and qualify the existing Windmill backstop before allowing
interim dispatches. An interim pass means the selected checks passed; it never means
the full regression obligation was discharged.

This is a design artifact, not activation. No production policy, schedule, card,
service, source code or test behavior is changed by this commit. The choices below
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
