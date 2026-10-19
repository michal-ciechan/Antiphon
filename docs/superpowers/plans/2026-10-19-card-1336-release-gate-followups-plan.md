# CARD-1336: release gate entrypoint follow-ups

Plan against `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`, CARD-1336
revision 0, for task `eeadbdf1` on `feat/card-task-eeadbdf1`.
Target: **master only**. Two sequential implementation slices, 45 and 60 minutes.
TestDesign is a separate stage; this document is not yet a Code checkpoint manifest.

Owners: [release gates](../../release-gates.md),
[nightly watchdog](../../nightly-watchdog.md),
[testing and build](../../testing-and-build.md), and
[orchestration](../../orchestration-loop.md).

## Outcome and boundaries

Make the repository's inert RC wrapper supply the named long-lived release ref.
Make direct `nightly-run.ps1` calls derive the same candidate identity as
`release-candidate.ps1` from a validated ref and pinned full SHA, before expensive
work. Correct its help. Account for the card's exact-ref and final-tag items through
their existing owners rather than duplicate CARD-1339's implementation.

No new release, CI, or parallel work streams. No session behavior changes.
No tag operations, execution of `publish-release.ps1` or `promote-release.ps1`,
live Windmill reads/writes/triggers, registration, scheduling, publication,
deployment, or rc-branch mutation are needed by this plan or its two slices.
The new tests must not reach those entrypoints indirectly. In particular, do not
run the entire existing release harness or `C1311_PreReleaseTag`, which invokes
both publication entrypoints. Existing source and history establish item (d).

Read-only platform discovery on 2026-10-19 used `GET /api/runner-defaults`
(revision 2, no kind overrides) and `GET /api/session-runners` (eligible Windows
and Linux runners; the configured default itself was unavailable/draining).
That snapshot is not a dispatch reservation. Use current automatic placement,
re-read eligibility at dispatch, omit `-Runner`, and omit `-Platform`; an existing
OS pin can be cleared with `-Platform Any`. No fleet location is part of this plan.
The checkpoint execution lane is the existing **PowerShell script-contract tests
through Antiphon.Tests**, with Bash available for the wrapper fixture. It does
not require the full Windows RC profile or a new platform lane.

## Ground truth

These are source observations, not passing-test claims. Paths and line numbers
refer to the base above unless another commit is named.

| Card assumption or requirement | What the code does | Planning consequence |
|---|---|---|
| (a) The inert Windmill wrapper has no `-Ref`. | `scripts/windmill/antiphon-release-candidates.json` has an empty input schema; its Bash body calls `release-cut.ps1` with schedule slot and job id only. The checked-in schedule has empty args and `enabled: false`. `release-cut.ps1:71-81` refuses an omitted ref with `rc-ref-required`. | Fix the repository wrapper's invocation. Definition files and their prose are not evidence of live registration; live state was not queried. |
| (b) Omitted CandidateId should derive from a long-lived ref and tested SHA. | `Test-ReleaseGateCandidateRef` in `scripts/lib/release-gate.ps1:42-87` already accepts `-Sha` and produces `rc-YYYY-MM-DD-<first 12 lowercase hex>`. `nightly-run-impl.ps1:198-212` calls it without `-Sha`, copies its empty CandidateId, then validates ExpectedSha. | Reuse the existing parser at the run admission boundary; no new identity algorithm or shared-library edit. |
| (b) The run currently executes the profile and earns nothing. | That describes the older path. This base additionally calls `New-ReleaseSelectionAttempt` before tests (`nightly-run-impl.ps1:403-423,492-512`). `Save-ReleaseSelectionAttempt` rejects an empty id with `selection-candidate-identity` (`release-selection.ps1:241`); the worker bridge can wrap that reason. Earlier fixture/contract errors can refuse first. The credit predicate also still refuses an empty id. | The symptom is partly stale, but the admission defect remains. A witness must inspect the missing derived identity, not require the old wasted-profile behavior. Use a valid committed selection fixture so an unrelated policy/setup refusal cannot impersonate the defect. |
| Help describes the actual supported identities and trigger. | `scripts/nightly-run.ps1:33-61` describes CandidateId only as a timestamp, says the schedule never passes Ref, and omits `rc` from Trigger help. | Update Ref, Trigger, ExpectedSha and CandidateId help together in S2. |
| The coordinator already handles identity. | `release-candidate.ps1:164-170` reparses the long-lived ref with its resolved SHA. `release-cut.ps1:152-153` passes both ExpectedSha and CandidateId to nightly-run. | Preserve coordinator behavior and explicit valid ids; fix direct invocation without editing coordinator files. |
| (c) Branch lookups need exact full refs. | `release-candidate.ps1:149,246,295`, `publish-release.ps1:172`, and `promote-release.ps1:54` query bare candidate names and consume the first SHA. Fully qualifying the query alone does not validate the returned ref field. | CARD-1339 S1 owns canonical `refs/heads/<name>` queries and ordinal matching of the complete returned ref, including recovery. CARD-1336 consumes that result and does not implement a second parser. |
| (d) S2 could reserve a final directly for a long-lived id until S3. | Landed commit `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` adds pre-release allocation and promotion. At this base `Get-ReleaseGateTagProposal` branches on a long-lived candidate id and calls `New-ReleaseGatePreReleaseTag` (`release-gate.ps1:596-599`); promotion has a separate final-tag path. | The specific direct-final allocation issue is resolved by S3. No new slice or tag-operation test for it. |
| S3 also fixes every date/allocation/promotion issue. | The publisher still passes `candidate.cutUtc` into allocation (`publish-release.ps1:205-211`), and other authority/reservation gaps remain. CARD-1339 explicitly owns these. | Do not overstate item (d): pre-release naming is fixed; UTC publication-day allocation and promotion hardening are separate CARD-1339 obligations. |
| Master-only fixes are present on the current rc line. | Read-only exact branch lookup found `rc/2026-10-18` at `81cfb267a0af5c0ddb65e8b238af355067bee9cc`. Its wrapper also omits Ref, and its nightly core also parses without Sha. The bootstrap can re-exec the selected checkout's script. | Master landing does not repair an rc-hosted copy or establish which tool version the next gate runs. Backport conditions are explicit below. |

## Decisions

These are selected implementation decisions within the brief, not unresolved
defaults requiring a Decide stage.

| Id | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Add literal `-Ref rc/2026-10-18` to the checked-in RC wrapper, the release line named by this dispatch. Document that it is an explicit line pin and must be reviewed on a later line rollover. The existing empty schema and disabled schedule remain usable without a new configuration mechanism. | Deriving the ref from today's date, selecting the newest remote branch, falling back to master, or adding a new Windmill parameter/schedule-args/registration contract for this small follow-up. The card expressly permits a named long-lived ref. |
| D-2 | Derive an omitted/whitespace CandidateId from `Test-ReleaseGateCandidateRef -Ref $Ref -Sha $ExpectedSha`, after preserving invalid-ref and invalid-full-SHA refusals. ExpectedSha is already the required pin; existing checkout/test SHA checks still establish what was actually tested. | Reimplementing the id formatter, deriving from wall-clock time, querying a moving remote again, or making every direct caller supply an id that is already deterministic. |
| D-3 | Require a supplied CandidateId to equal the parser's canonical id for that ref/pin. A mismatch refuses early with `rc-candidate-id-mismatch`, exit 3, phase `profile`; it is not silently replaced. Matching explicit ids and legacy stamp ids continue to work. | Accepting a foreign candidate's state directory or silently correcting an explicit caller identity. This check belongs at the same identity boundary and uses no publication behavior. |
| D-4 | Keep StateRoot, LogRoot, ReleaseRoot and CoordinationRoot explicit and independently owned in direct-call fixtures/examples. Deriving the id does not auto-select or relocate these roots, create a candidate journal, or grant publication authority. | Defaulting an RC invocation into master state, rewriting old journals, or treating a derived id as release qualification. |
| D-5 | Leave exact refs, UTC tag day, remote occupancy and promotion fixes entirely to CARD-1339. Track that card's S1 as the completion dependency for CARD-1336(c). | Editing shared gate/publisher/promoter files concurrently, duplicating exact-ref tests here, or considering another card's plan to be landed proof. |
| D-6 | Add a separate TUnit class and a fixture script under `tests/fixtures/`. Reuse `ScriptHarness` and the small C487 helpers without changing the shared release harness. | Adding cases to `scripts/test-release-gate.ps1`, which CARD-1339 owns, or adding a standalone nightly script-census job and policy/hash churn. The new cases execute through Antiphon.Tests. |
| D-7 | Land both slices on master only. Any necessary rc backport is separately commissioned and reviewed; its need depends on the actual next gate entrypoint as described below. | Retargeting this task, assuming a master bootstrap guarantees the rc child is fixed, or changing a live schedule to pick up the source change. |

No session that waits for input is introduced or changed, so the conditional
session-release checklist does not apply.

## Coordination with CARD-1339

Read CARD-1339 revision 5 and its Plan/TestDesign artifact at
`docs/superpowers/plans/2026-10-19-card-1339-release-tag-allocator-plan.md`
on `feat/card-task-4432ab0e`, observed remote tip
`9868801d62a6124e2b5e2cd7e9f705b7194c45ed`. That is inspected design, not a
claim of completed Code. The card was InProgress at inspection.

| Item | Owner and handoff | CARD-1336 action |
|---|---|---|
| Exact branch and tag ref parsing | CARD-1339 S1 / `ReleaseTagAllocatorTests.C1339_ExactRemoteRefs`; includes exact full branch names, suffix-only results, reordered responses, malformed/ambiguous matching rows and failed queries. | Require its landed implementation and ordinary evidence before closing (c); no duplicate slice here. |
| Publication day, remote tag occupancy, authority and reservation checks | CARD-1339 S2-S5. | Preserve its ownership; the S3 finding here is limited to the original direct-final issue. |
| Shared production files | CARD-1339 owns `scripts/lib/release-gate.ps1`, `scripts/release-candidate.ps1`, `scripts/publish-release.ps1`, `scripts/promote-release.ps1`, `scripts/lib/release-authority.ps1`. | Consume the existing ref parser; edit none of these files. |
| Shared tests and owner prose | CARD-1339 owns `scripts/test-release-gate.ps1`, its allocator test class, affected authority/branch-model tests, and allocation/promotion prose in `docs/release-gates.md`. | Use the separate fixture/class below, wrapper README and nightly help; avoid all those edit sites. |

S1 and S2 here need no allocator API changes and can proceed independently of
CARD-1339 Code. Before closure, the caller records the actual landed CARD-1339
S1 SHA and its exact-ref receipt. If it is not landed, report (a)/(b) implemented,
(d) already resolved, and (c) still pending that dependency. Do not close the whole
card from this plan alone. A later incompatible ref-parser change requires
TestDesign to reconcile the interface before Code, not a speculative edit here.

## Slices

Each slice commits and pushes one test-only red witness before its fix, then runs
one narrow green selection at the fixed SHA. The red must be the named behavioral
assertion; missing commands, setup errors, timeouts and zero tests are incomplete.
All tests below are proposed, not present or executed at Plan time.

### S1 - Supply the wrapper's explicit RC ref (45 minutes)

Files:

- `scripts/windmill/antiphon-release-candidates.json`: add the explicit Ref to
  the existing SSH command; describe the pinned line in the payload's prose.
- `scripts/windmill/README.md`: document the pin/rollover responsibility and
  retain the unregistered/unqualified status of the repository definition.
- New `tests/Antiphon.Tests/Scripts/ReleaseGateFollowupTests.cs`.
- New `tests/fixtures/card1336-release-gate-followups.ps1`.

Proposed method: `ReleaseGateFollowupTests.C1336_WrapperRef`.

Red-first witness: decode the production JSON `content`, execute that Bash body
inside an owned fixture with an inert `ssh` function installed before it, and
inspect the captured remote command argument. Assert exactly one
`-Ref rc/2026-10-18` is delivered to `release-cut.ps1`. Current code omits it.
Do not call SSH, PowerShell's release-cut, or a live Windmill API. Installing the
capture function must precede evaluation; inability to do that is a fixture
failure, never permission to fall through to a real SSH executable.

With the fix, expand the same method to exercise own/missing/foreign schedule
context, preserve job id forwarding, retain `manual` for unrecognized context,
and preserve SSH exit status on success and failure. Verify the schedule payload
still has `enabled: false`, the same cron/timezone and empty args; check the
master wrapper is unchanged. Assert real captured arguments and exit status,
not merely a source-text substring.

Narrow green: `C1336_WrapperRef` plus
`ReleaseGateRegistrationTests.C599_ScheduleProvenance` (2 TUnit methods). The
existing method uses an offline registrar fixture only. Do not add registration
or publication tests to this selection.

### S2 - Derive and bind direct nightly candidate identity (60 minutes)

Files:

- `scripts/lib/nightly-run-impl.ps1`: supply ExpectedSha to the existing parser,
  validate the canonical id at profile admission, and set both the local id and
  result identity before locks, sync, selection, or test work.
- `scripts/nightly-run.ps1`: correct Ref/Trigger/ExpectedSha/CandidateId help,
  describe both id shapes, and include a direct RC example with private roots.
- Extend the new TUnit class and fixture from S1 only.

Proposed method: `ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity`.

Preserve refusal precedence: malformed ref -> `rc-ref`; missing/invalid full
ExpectedSha -> `rc-expected-sha`; a supplied foreign id ->
`rc-candidate-id-mismatch`. Leave the master-root guard and existing checkout
SHA mismatch checks intact. The parser already supports `refs/heads/` and legacy
stamp refs. Do not change its formatting or public API.

Red-first witness: invoke the production nightly wrapper/core against a valid,
owned local candidate fixture, with `-Profile rc -Trigger rc`, a long-lived ref,
a committed full ExpectedSha and no CandidateId. Assert the returned id equals
`rc-<ref date>-<first 12 of ExpectedSha>`. The old core leaves it empty. Arrange a
decisive assertion before any additional assertions about downstream success;
an unrelated selector refusal must not be the red witness.

Narrow green: the new method plus
`ReleaseGateRunTests.C599_CreditVerdicts` (2 TUnit methods). The new method covers:

- Omitted and whitespace ids, bare and `refs/heads/` refs, repeat identity at the
  same pin, and a new id for a different pinned SHA.
- Matching explicit ids, a mismatched explicit id refused before sync/selection/
  workload, missing/short ExpectedSha, malformed refs, and legacy stamp derivation.
- A valid synced run and a continuation/NoSync run: the same derived id reaches
  the selection request, child arguments, attempt state and completion result.
  Keep full-SHA agreement as the existing test admission condition.
- The RC fixture's green, when the synthetic workload/report succeeds, lives
  under the derived candidate directory. Master attempt/green/monitor/qualification
  sentinels stay byte-identical. NoReport cannot earn credit; a moved pinned SHA
  cannot become green. These are fixture state transitions, not native release
  evidence or publication permission.
- The help names both id formats, ExpectedSha's role and Trigger=rc, without
  promising master readiness or publication from a direct invocation.

Use the committed selection-fixture pattern in
`scripts/test-change-aware-release.ps1:New-Fx` as a reference, without importing
or executing that test script. Its real temporary Git repository provides the
policy/catalog/selector/runtime blobs the current selection path requires.
Use local-only Git transport, fixture workload/report I/O and private state/
release/coordination directories. Do not replace the parser, credit predicate or
selection identity validator with a test implementation. No release-cut,
publisher, promoter, GitHub, provider, broker or live reporting call is necessary.

## TestDesign handoff

Add the required `## Verification design` and executable `### Checkpoints` in
the separate stage. Bind the actual assertion roster, exact method filters,
execution counts, guard inventory, post-land controls and costs before Code.
The brief did not fold that work into Plan. The following is checkpoint intent,
not an executable manifest and not permission for a broad suite run.

| Intended checkpoint | After | Existing execution lane | Selection | Expected TUnit count |
|---|---|---|---|---:|
| CP-1 | S1 test-only witness | Antiphon.Tests PowerShell script-contract | `/*/*/ReleaseGateFollowupTests/C1336_WrapperRef` | 1 executed, 1 expected assertion failure |
| CP-2 | S1 fix | Same | New wrapper method + `ReleaseGateRegistrationTests.C599_ScheduleProvenance` | 2 executed, 0 failed/skipped |
| CP-3 | S2 test-only witness | Same | `/*/*/ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity` | 1 executed, 1 expected assertion failure |
| CP-4 | S2 fix | Same | New identity method + `ReleaseGateRunTests.C599_CreditVerdicts` | 2 executed, 0 failed/skipped |

TestDesign must bind the fixture through `ScriptHarnessOptions.ScriptPath` (or
the existing path-capable harness overload), with `Category("Integration")` and
`ParallelLimiter<ProcessSpawnLimit>`. Retain owned child cleanup and finite
timeouts. Bash availability is an explicit fixture prerequisite, never a skipped
green. Use the same production wrapper bytes, decoded from JSON, in the wrapper
test; changing text inside the harness to emulate the desired command is not proof.

The two green groups are the whole intended ordinary test scope. No Unit-wide,
assembly-wide, full release harness, actual nightly/RC profile, or tag-operation
selection is required. Check existing narrow cases at the base if they fail;
do not hide inherited fixture failures or widen timeouts. The new selection fixture
must work with the current real selection contract, unlike older fake `.git`
directories that do not contain the required committed blobs.

Execution uses the checkpoint tool and host build-slot gate, one isolated build
and one exact filter per row. Use forward-slash `bin-<name>/` outputs, keep receipts
ignored, wait for every foreground run, and remove owned output directories.
Code/Review also run the required evidence-diff guard. Historical red receipts
remain red and cite their test-only SHAs; the fixed tip runs the narrow green
rows, not all historical red rows again. Ordinary Review is still required.

The authoring/verification envelopes are 45 + 60 = 105 estimated minutes, not
measured cost. TestDesign must itemize the four builds/runs, bootstrap, fixture
setup and any post-land method-scoped controls within a realistic budget; host
slot wait is additional. No test/build cost or saving was measured in Plan.

## Before the next gate on rc/2026-10-18

**Neither slice is an unconditional prerequisite for every already-explicit
manual gate invocation. Both are prerequisites for the entrypoints they repair.**

| Next invocation | Needed before relying on it | Must this work reach rc first? |
|---|---|---|
| Current dormant Windmill wrapper, if later registered/enabled | S1's explicit Ref must be in the definition actually registered; registration/qualification remains a separate operator task. | Only if the definition is sourced from the rc checkout. A master-only file change neither updates a live job nor repairs an old rc copy. |
| Direct nightly-run on the rc checkout, omitting CandidateId | S2 must be present in the executing core and any self-reexec copy. | **Yes:** a separately authorized, reviewed S2 backport to `rc/2026-10-18` is needed before exercising that omitted-id path. |
| Explicit `release-cut.ps1 -Ref rc/2026-10-18`, or nightly-run with the correct explicit id | These callers already supply the necessary identities; S1/S2 need not delay that invocation solely for (a)/(b). | No unconditional CARD-1336 backport requirement for that shape. It still needs CARD-1339's exact-ref/allocation/promotion prerequisites before publication; this is not an approval to run a gate or create a tag. |

For every later authorized gate, record the actual script source SHA and the
candidate checkout's source, including the self-update hop. Do not infer them
from master land or a healthy service. If the repaired behavior is needed from rc,
commission the relevant backport with its tests and fresh evidence; this task
does not mutate that branch. Re-evaluate the literal wrapper pin if the caller
has advanced the release line by then. Existing release qualification and
hash-bound authority requirements are unchanged.

## Plan validation

Read both cards, the release/watchdog/testing/orchestration owners, the current
entrypoints and tests, and CARD-1339's committed Plan/TestDesign. Checked the
assigned branch/base, read runner defaults/catalogue, and compared the named
remote branch tips without querying or changing tags. No tests, builds,
publication entrypoints, live Windmill operations or release operations ran.
The task report records the plan commit, pushed remote SHA and merge-tree result.

## Verification design

TestDesign at `9b8c00af81e5faa2d8c22ec3d1ac0d6ecc85d210`, task `6386e779`.
This appendix binds the preceding design; it does not change D-1 through D-7,
master-only targeting, CARD-1339 S1 ownership/dependency, or the conditional rc
backport table. No implementation or execution evidence is claimed here.

### Inspection

- Read `ReleaseGateRegistrationTests.cs`, `ReleaseGateRunTests.cs`, and their exact
  `Test-C599_ScheduleProvenance` / `Test-C599_CreditVerdicts` bodies in
  `scripts/test-release-gate.ps1`, including `New-C599State`, registration fixture
  construction and invocation | V-1/R-1, V-4/R-4. These are non-parameterized
  methods: 1 execution each, respectively 16 and 24 named PowerShell rows.
- Read `ScriptHarness.cs`, `ScriptHarnessOptions` and ownership/validation flow in
  `ScriptHarnessProcess.cs`, `ProcessSpawnLimit`, `scripts/lib/c487-harness.ps1`,
  and the adjacent `Scripts/Fixtures/script-harness-process.ps1` | recipes A/B.
  The nearest relevant functional fixture for the new file is
  `scripts/test-change-aware-release.ps1:New-Fx`; read its committed Git setup,
  `New-Request`, `Bind-Request`, `Invoke-Workload`, and parameter-hop test.
  `tests/fixtures/` currently contains unrelated CARD-0490 native assets; its
  README was read and that guest lane is excluded.
- Read decoded RC wrapper/schedule, master wrapper pins, wrapper README, nightly
  wrapper, core admission/sync/NoSync/selection/hop/state/completion bodies,
  `release-selection.ps1` commit/contract/save/read/worker/hop bodies, ref parser,
  credit/state helpers, and nightly-tests SHA refusal | V-1 through V-4/R-1
  through R-4. The selection hop precedes the ordinary StartProcess seam.
- Read release-gates credit/ownership/hop owner sections, nightly-watchdog receipt
  tiers, testing/build checkpoint schema, CARD-0403 filters, slot and Mutation
  rules, and orchestration stage order/D-2 | delivery exclusions and checkpoints.
  Registration profile/token/pre-enabled guards were read for the collateral PCs.

**Binding convention.** W = `ReleaseGateFollowupTests.C1336_WrapperRef`,
N = `ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity`,
S = `ReleaseGateRegistrationTests.C599_ScheduleProvenance`, and
C = `ReleaseGateRunTests.C599_CreditVerdicts`, all in `Antiphon.Tests.Scripts`.
W/N use the new `tests/fixtures/card1336-release-gate-followups.ps1` through
`ScriptHarnessOptions.Default with { ScriptPath = <absolute fixture path> }`,
`Category("Integration")`, and `ParallelLimiter<ProcessSpawnLimit>`.
Keep the default 300-second invocation / 10-second cleanup budgets, fail missing
Bash/pwsh/Git prerequisites, and await children. No skipped green or increased timeout.

**Recipe A (W).** Decode the actual JSON content to an owned UTF-8/LF file without
rewriting it. Invoke Bash by resolved absolute executable with `--noprofile
--norc`; an owned launcher installs a readonly `ssh` function before sourcing the
body. The function writes every argument with a NUL delimiter to a private file
using Bash builtins and returns a fixture-selected status. Supply no real SSH
executable in the child PATH; launch fails closed if the function cannot be
installed. Read the last captured argument as the remote command, count exact
`-Ref` tokens, and compare its value and ScheduleSlot/JobId arguments. Job fixture
is `c1336-job`, without shell metacharacters. Set/clear both WM variables explicitly.
No remote shell, release-cut or registration is executed by W.

W's final roster is **9 named C487 rows**, all required by the C# harness:

| Case / variant | Oracle (row label is prefixed `C1336 W `) | Reachability / classification |
|---|---|---|
| `own-0`, `own-23` | One ssh invocation; one `-Ref rc/2026-10-18`; ScheduleSlot `u/lndcobra/antiphon_release_candidates`; JobId `c1336-job`; exit respectively 0/23. | Real Bash evaluates unchanged wrapper bytes; offline behavioral contract. |
| `missing-0`, `missing-23` | Same ref/job/count; ScheduleSlot `manual`; exact status. | WM_SCHEDULE_PATH absent; offline negative provenance. |
| `foreign-0`, `foreign-23` | Same ref/job/count; ScheduleSlot `manual`; exact status. | WM_SCHEDULE_PATH is the master schedule path; offline negative provenance. |
| `schedule` | Boolean enabled=false, cron `0 30 8,16 * * *`, timezone `Europe/London`, empty args and empty input properties/required. | Parse production definition/schedule; configuration contract. |
| `master-unchanged` | Master script/schedule Git blob IDs remain `4073816bbfb057c4dcf12e051862d32441d68dc4` / `781f8559ea2598750eb130da49bb979f09e079c0`. | Independent frozen baseline; byte preservation. |
| `documentation` | RC description and README name literal pin/rollover and retain inert/unqualified status, without control characters. | Static documentation contract, no registration proof. |

CP-1's test-only commit contains just `own-0` (expectedRows=1). Its decisive
failure is the captured command's missing Ref. S1 fix expands W to all 9 rows;
CP-2 therefore expects 9 + 16 = 25 internal C487 rows across **2 TUnit executions**.
Do not count Bash variants or C487 lines as TUnit results.

**Recipe B (N).** Adapt New-Fx without dot-sourcing its test script: create a
private seed, local bare origin and independent owned clone, two real commits A/B,
production scripts/lib, nightly wrappers, selector, actual execution/selection
policy and global.json blobs, project-universe files and catalog source inputs.
Validate policy/catalog hashes and commit availability before the red assertion.
Use local Git transport only. Pin `rc/2026-10-18` to A; B has a different first
12 hex prefix (assert this prerequisite). A benign committed wrapper comment
forces the production self-reexec edge. Copy source under test into each fresh
seed, including during Mutation; never use a previously built fixture copy.
Keep StateRoot, LogRoot, ReleaseRoot and CoordinationRoot independent, explicit
and below the invocation scratch root. Put four master sentinels in a fifth
private root. The fixture SeamsPath installs a script-scope override of
`Get-NightlyDefaultStateRoot` returning that private root (using
`Set-Item Function:script:Get-NightlyDefaultStateRoot`); it changes no identity,
selection or credit function. Reinstall it in each real child via SeamsPath. Never
create or write `C:\Antiphon\nightly` on the host.

Run the real nightly wrapper/core and real selection worker/validator. Synthetic
native workload I/O may emit deterministic summary/coverage data; report I/O is
an inert local seam only. Use the real selection-bound self-hop. If child argument
observation is needed, append a recorder after the copied wrapper's param block,
before its production body; it records bound parameters to an owned file and then
continues the unmodified body. Do the same for the workload wrapper. This observer
must not construct identity or bypass selection/admission. A report command, if
seen, is handled by the inert seam and records only local receipt data. Every
unexpected process target fails the fixture. No publish/promote/release-cut call.

The internal data contract is joined by `(candidateId, full SHA, runId, attempt)`:
selection-attempt.json + its digest, selection.json + its digest, child-bound
parameters, final last-run.json and the terminal JSON result must agree. Inspect
child-produced summary/attempt state after child exit, not just launcher arguments.
The terminal completion record intentionally empties identity on a refusal; the
red identity witness therefore reads the returned PassThru object's CandidateId,
**before** downstream success assertions, not the refusal completion JSON.

N's final roster is **25 named C487 rows**, all required in C#; labels are prefixed
`C1336 N `. Each row emits once even when it checks several fields:

| Case / variant | Oracle | Reachability / classification |
|---|---|---|
| `omitted` | Returned CandidateId = literal `rc-2026-10-18-` + A.Substring(0,12). | Valid committed fixture, absent argument; behavioral red witness. Expected value is not computed by calling the parser. |
| `whitespace` | Same canonical id for whitespace input. | Admission branch; positive canonicalization. |
| `full-ref` | Same id for `refs/heads/rc/2026-10-18`. | Real parser; supported shape. |
| `repeat` | Fresh runId/state at A retains candidate id. | Repeat invocation; deterministic identity. |
| `different-pin` | Ref at B produces B's prefix and differs from A. | Second committed local pin; identity separation. |
| `explicit` | Matching explicit id survives unchanged, successful admission. | Supplied-id branch; compatibility. |
| `foreign` | exit 3, phase profile, refusal rc-candidate-id-mismatch; no lock/attempt, Git sync, selection files or workload receipt. | Valid ref/SHA, foreign id; early refusal. |
| `missing-sha`, `short-sha`, `invalid-sha` | exit 3/profile/rc-expected-sha, no admission side effects. Also supply foreign id so SHA precedence is observable. | Empty, 7-hex, and 40 non-hex ExpectedSha; negative admission. |
| `bad-ref` | exit 3/profile/rc-ref and no admission side effects with invalid calendar ref, missing SHA and foreign id. | Competing refusals; ref precedence. |
| `legacy` | `release/rc-20260923T083000Z` -> `rc-20260923T083000Z`, both omitted and matching explicit. | Legacy local branch at A; compatibility. |
| `sync-selection` | Real saved selection request/attempt and digest bind canonical id/A/runId; no foreign directory. | Normal sync path reaches real selection worker; persisted identity. |
| `nosync-selection` | Same binding with NoSync and a fresh runId; no fetch/reset. | Separate NoSync selection branch; persisted identity. |
| `hop-child` | Copied nightly wrapper records canonical CandidateId, ExpectedSha=A, Ref, Profile=rc, ContinueRunId, roots; child completes with the same selection binding. | Forced real self-hop, not StartProcess stub; child consumption. |
| `workload-child` | Workload wrapper records canonical CandidateId, ExpectedSha=A and matching selection digests, then produces matching summary. | Real selection hop with inert workload; child consumption. |
| `completion` | Successful terminal JSON and final attempt both carry canonical id/A/runId, agreeing with child summary. | Parse last completion JSON only after joined child exit; state/result consistency. |
| `rc-green` | Successful synthetic run writes only `<ReleaseRoot>/candidates/<id>/complete-green.json` with matching tuple and rc-release credit. All configured roots stay as supplied. | Production credit writer, synthetic workload/report inputs; state transition only. |
| `master-unchanged` | All four master sentinel hashes unchanged after success and refusal variants. | Private master default seam; isolation. |
| `master-root` | StateRoot equal to private default-master root refuses rc-master-state-root at profile, before any writes. | Valid identity prevents earlier masking; negative isolation. |
| `noreport` | Even a synthetic summary claiming reportDelivered=true gives result/state reportDelivered=false, no credit/green and no report invocation. | NoReport branch; fail-closed credit. |
| `moved-sync` | Origin at B with ExpectedSha=A refuses candidate-moved; no workload and no green. | Production sync SHA guard before selection; pin protection. |
| `moved-nosync` | Clone HEAD=B with ExpectedSha=A reaches nightly-tests expected-sha-mismatch, no native workload and no green. | Fresh selection for B avoids a stale-selection mask; separate workload guard. |
| `help` | Ref/Trigger/ExpectedSha/CandidateId help covers both id shapes, Trigger=rc, full pin and example's private roots. | Static contract; no gate run. |
| `no-authority` | Derivation creates no candidate.json/publication journal/authority; fixture green grants no external qualification. | Inventory private release tree; scope boundary. |

CP-3 starts with only `omitted` (expectedRows=1). CP-4 expands N to 25; together
with C's 24 this is 49 internal rows, **2 TUnit executions**. Fixture prerequisite
failures, selection-policy errors, child exceptions and timeouts are not the
expected red witness. The test-only commit must be fixed as a fixture before
proceeding if any of those masks the missing CandidateId assertion.

### Delivery inventory

**Changed asynchronous delivery paths: zero.** Neither slice adds/changes a queue,
message recipient, reporter, retry worker or session input. Existing reporting and
Windmill registration remain external operating acceptance, excluded here.

| Path | Producer -> destination | Persistence / recovery / observable receipt | Evidence limit |
|---|---|---|---|
| RC definition invocation | Bash wrapper -> inert ssh capture | Private argv file, one invocation, exact exit; no durable delivery/retry identity exists here. | W proves command construction, never SSH arrival, release-cut execution or live job registration. |
| Local selection and self/workload hop | Core -> real local worker / child wrapper | `(candidateId,SHA,runId,attempt)` in selection files/digests, child-bound receipt, child summary, final attempt/result; NoSync uses the same immutable selection. Existing synchronous owner awaits children. | N proves local identity consumption; no external queue or crash-recovery delivery claim. |
| Synthetic report outcome | Core -> inert report I/O seam | Local receipt and summary correlated to the run; NoReport remains non-delivered. | Deliberately substitutes report transport. Exit zero/reportDelivered/green never proves a recipient received anything. |

Consequently real-queue busy-recipient/already-eligible/crash/enqueue-failure tests
are not applicable to these two slices; no delivery/recovery guard is newly
introduced. Do not convert these substitutes into delivery evidence. If Code
changes an asynchronous handoff, return to Plan/TestDesign for every producer,
durable boundary and recovery cut through real-queue recipient evidence. Session
input would require the matching **complete UserPrompt transcript**. A send, insert,
event, ack or Sent flag is insufficient.

### Proves it works now

- V-1: explicit named ref and preserved context/status | offline Bash contract |
  CP-1 witness then CP-2 W+S | old own-0 fails for missing Ref; fixed 2/2 pass.
- V-2: canonical admission identity and early refusals | production PowerShell |
  CP-3 witness then CP-4 N | old omitted fails on returned CandidateId; fixed all
  identity, precedence and compatibility rows pass.
- V-3: same identity at actual local child/selection/state boundaries | local
  integration | CP-4 N sync-selection through completion | tuple/digests agree.
- V-4: isolated candidate state and fail-closed credit | script contract |
  CP-4 N+C | candidate green only, master sentinels unchanged, NoReport/moved pins
  cannot earn green; existing 24 credit rows pass.

### Guards the regression

- R-1: omitted/wrong/duplicated wrapper Ref, borrowed schedule provenance or masked
  ssh status | W's six captured-command rows, schedule and master-unchanged;
  CP-2 also runs S's existing 16-row roster.
- R-2: empty/foreign candidate identity or reordered admission | N omitted,
  whitespace, full-ref, repeat, different-pin, explicit, foreign, SHA/ref rows and
  legacy; exact reason/phase/exit and absence of early side effects are decisive.
- R-3: identity lost at either selection branch, self-hop, workload, persisted
  attempt or completion record | N sync-selection/nosync-selection/hop-child/
  workload-child/completion; read artifacts emitted by each consumer.
- R-4: master contamination, false NoReport credit or moved-pin success | N
  rc-green/master-unchanged/master-root/noreport/moved-sync/moved-nosync and C;
  inspect actual files plus refusal/credit verdicts, not return code alone.

### Guard inventory

Safety scope includes the changed entrypoints and the independently bypassable
credit/registration guards exercised by the two mandated collateral methods.
Unchanged lock custody, general selector integrity, publication authority and
transport guards are excluded, not silently claimed tested by these cases.
Each row maps to exactly one distinct PC. No guard is justified as needing none.

| Guard | Plan reference / invariant | Positive control |
|---|---|---|
| G-1 | S1/D-1: exactly one explicit pinned Ref | PC-1 |
| G-2 | S1: only own schedule context receives the scheduled slot | PC-2 |
| G-3 | S1: ssh failure remains wrapper failure | PC-3 |
| G-4 | S1: inert checked-in schedule | PC-4 |
| G-5 | S2/D-2: local id derives from ref and pin | PC-5 |
| G-6 | S2/D-2: returned id is populated at admission | PC-6 |
| G-7 | S2: invalid candidate ref refuses | PC-7 |
| G-8 | S2: a full valid ExpectedSha is required | PC-8 |
| G-9 | S2/D-3: explicit foreign id refuses | PC-9 |
| G-10 | S2: ref refusal precedes SHA refusal | PC-10 |
| G-11 | S2: SHA refusal precedes explicit-id mismatch | PC-11 |
| G-12 | S2/D-4: RC cannot use default master state | PC-12 |
| G-13 | S2: synchronized checkout must match pinned SHA | PC-13 |
| G-14 | S2: NoSync workload must match pinned SHA | PC-14 |
| G-15 | S2: sync selection receives derived id | PC-15 |
| G-16 | S2: NoSync selection receives derived id | PC-16 |
| G-17 | S2: self-hop preserves supplied canonical identity | PC-17 |
| G-18 | S2: workload hop preserves canonical identity | PC-18 |
| G-19 | S2: persisted run state names the canonical candidate | PC-19 |
| G-20 | S2: terminal JSON agrees with persisted candidate | PC-20 |
| G-21 | S2/D-4: green goes to the supplied candidate root | PC-21 |
| G-22 | S2: NoReport never borrows summary delivery | PC-22 |
| G-23 | S2/D-4: master attempt state stays untouched | PC-23 |
| G-24 | R-4 collateral: coverageComplete required | PC-24 |
| G-25 | R-4 collateral: testsPassed required | PC-25 |
| G-26 | R-4 collateral: reportDelivered required | PC-26 |
| G-27 | R-4 collateral: true must be a Boolean | PC-27 |
| G-28 | R-4 collateral: exitCode cannot be missing | PC-28 |
| G-29 | R-4 collateral: nonzero exit cannot earn credit | PC-29 |
| G-30 | R-4 collateral: run identity required | PC-30 |
| G-31 | R-4 collateral: policy identity required | PC-31 |
| G-32 | R-4 collateral: tested SHA must be full | PC-32 |
| G-33 | R-4 collateral: expected SHA cannot be absent | PC-33 |
| G-34 | R-4 collateral: tested and expected SHA equal | PC-34 |
| G-35 | R-4 collateral: candidate identity required for credit | PC-35 |
| G-36 | R-4 collateral: unknown requested credit kind refuses | PC-36 |
| G-37 | R-4 collateral: null state cannot earn credit | PC-37 |
| G-38 | R-4 collateral: master and RC credit cannot substitute | PC-38 |
| G-39 | R-1 collateral: registrar refuses pre-enabled RC payload | PC-39 |
| G-40 | R-1 collateral: registration workspace is constrained | PC-40 |
| G-41 | R-1 collateral: profile cannot contain inline token | PC-41 |
| G-42 | R-1 collateral: apply requires token-file availability | PC-42 |

### Positive controls

All controls are **post-land obligations, not executed at TestDesign**. Code runs
ordinary V/R; separate Review judges this design and ordinary evidence before
land. Mutation runs every PC against the confirmed landed source, baseline green,
compiling defect/red at the specified assertion, exact restoration, fresh build,
green. A fixture/setup/build failure or zero tests is not red. Do not change an
assertion to manufacture detection. Reuse no mutation-phase build output.

Method bindings below expand W/N/S/C exactly as defined above. W/N row labels get
the `C1336 W ` / `C1336 N ` prefix; collateral labels are already complete.
Every control uses `/*/*/<Class>/<ExactMethod>`, MinExecuted=1. No class-wide PC
runs. Where several guards use the same method, each control still gets its own
red/restore/green receipt. SourceLanding mutations may temporarily touch shared
files for detection only; no production repair/commit/push is authorized there.

| PC | Break by this compiling defect | Exact method binding / expected failing assertion |
|---|---|---|
| PC-1 | Remove the added `-Ref rc/2026-10-18` from JSON content, preserving valid JSON/Bash. | W: `own-0` |
| PC-2 | Replace the Bash foreign/missing-context assignment `SLOT=manual` with the own schedule literal. | W: `foreign-0` |
| PC-3 | Replace final `exit $?` with `exit 0`. | W: `own-23` |
| PC-4 | Set schedule JSON `enabled` to boolean true. | W: `schedule` |
| PC-5 | Remove `-Sha $ExpectedSha` at the admission parser call. | N: `omitted` |
| PC-6 | Remove only `$result.CandidateId = $CandidateId` from the derived-id branch. | N: `omitted` |
| PC-7 | Make the admission `if (-not $candidate.Ok)` condition false. | N: `bad-ref` |
| PC-8 | Make only the admission full-SHA refusal condition false. | N: `short-sha` |
| PC-9 | Make the new explicit-id mismatch condition false; preserve derivation. | N: `foreign` |
| PC-10 | Move the unchanged ExpectedSha refusal block before the candidate-ref refusal. | N: `bad-ref` |
| PC-11 | Move the new supplied-id mismatch refusal before the ExpectedSha refusal. | N: `missing-sha` |
| PC-12 | Make only the masterRoot/wantRoot equality refusal false; fixture default remains private. | N: `master-root` |
| PC-13 | Make only the sync `$sha` versus `$ExpectedSha` refusal condition false. | N: `moved-sync` |
| PC-14 | In nightly-tests-impl.ps1 make only expected-sha-mismatch refusal false; preserve real selector. | N: `moved-nosync` |
| PC-15 | Set CandidateId in the sync selectionRequest to literal `foreign`. | N: `sync-selection` |
| PC-16 | Set CandidateId in the NoSync selectionRequest to literal `foreign`. | N: `nosync-selection` |
| PC-17 | Replace the self-hop CandidateId argument value with literal `foreign`, leaving the option present. | N: `hop-child` |
| PC-18 | Replace the testArgs CandidateId value with literal `foreign`. | N: `workload-child` |
| PC-19 | Change stateObj initialization `candidateId = $CandidateId` to `candidateId = "foreign"`. | N: `completion` |
| PC-20 | In ConvertTo-NightlyResultRecord change the matched-state candidateId assignment to literal `foreign`. | N: `completion` |
| PC-21 | In the core RC green branch pass literal `foreign` to Get-ReleaseGateStatePaths CandidateId. | N: `rc-green` |
| PC-22 | Allow summary.reportDelivered to override NoReport by removing only `-not $NoReport -and` from that final summary override condition. | N: `noreport` |
| PC-23 | For RC, change paths.LastAttempt to Join-Path of Get-NightlyDefaultStateRoot and last-run.json; the fixture default is the private sentinel root. | N: `master-unchanged` |
| PC-24 | Remove coverageComplete from Test-ReleaseGateGreenFlags field list. | C: `C599 CreditVerdicts coverageComplete false earns nothing` |
| PC-25 | Remove testsPassed from the same field list. | C: `C599 CreditVerdicts testsPassed false earns nothing` |
| PC-26 | Remove reportDelivered from the same field list. | C: `C599 CreditVerdicts reportDelivered false earns nothing` |
| PC-27 | In Test-ReleaseGateRealBoolean accept string value "true" before the type check. | C: `C599 CreditVerdicts coverageComplete as the STRING "true" earns nothing` |
| PC-28 | Remove only the missing-exitCode reason addition; retain the elseif chain. | C: `C599 CreditVerdicts missing exitCode earns nothing` |
| PC-29 | Remove only the nonzero exit reason addition. | C: `C599 CreditVerdicts non-zero exit earns nothing` |
| PC-30 | Remove only the missing-runId reason addition. | C: `C599 CreditVerdicts missing runId earns nothing` |
| PC-31 | Remove only the missing-policyHash reason addition. | C: `C599 CreditVerdicts missing policyHash earns nothing` |
| PC-32 | Make Test-ReleaseGateFullSha accept exactly seven lowercase hex characters as well as forty, so the short equal pair reaches credit. | C: `C599 CreditVerdicts rc short sha earns nothing` |
| PC-33 | Replace the expected-SHA validation/equality block with one guarded by nonempty expected; empty expected bypasses that block only. | C: `C599 CreditVerdicts rc without a pinned expectedSha earns nothing` |
| PC-34 | Remove only rc-sha-mismatch reason addition. | C: `C599 CreditVerdicts rc sha mismatch earns nothing` |
| PC-35 | Remove only rc-candidate-missing reason addition. | C: `C599 CreditVerdicts rc without a candidate id earns nothing` |
| PC-36 | Change the unknown-kind early return in Test-NightlyCompleteGreenPredicate to true. | C: `C599 CreditVerdicts an invented credit kind earns nothing` |
| PC-37 | Change the null-state early return in Test-NightlyCompleteGreenPredicate to true. | C: `C599 CreditVerdicts a null state earns nothing` |
| PC-38 | After a successful verdict, replace the requested-kind equality return with true. | C: `C599 CreditVerdicts rc green never earns master credit` |
| PC-39 | Make the rc-schedule-preenabled condition false in register-release-gates.ps1. | S: `C599 ScheduleProvenance a pre-enabled rc payload refuses` |
| PC-40 | Make the workspace != mc refusal false in Read-ReleaseGateRegistrationProfile. | S: `C599 ScheduleProvenance a non-mc workspace refuses` |
| PC-41 | Remove only token from the forbidden profile-field list. | S: `C599 ScheduleProvenance an inline token in the profile refuses` |
| PC-42 | Replace only the token-load catch refusal/return with assignment of a fixed fixture token; offline Windmill seam remains installed. | S: `C599 ScheduleProvenance a missing token file refuses the apply` |

**Binding required after Code.** The mutations above bind behavior, function and
statement, not an invented line number in future source. Code records fixed-source
file/statement anchors and exact implemented W/N labels; Mutation binds concrete
patch payloads and verifies each reaches its named assertion. Any payload not yet
bound stays **binding required**, including after Code, until independently bound;
a plan row is not a mutation receipt. Missing/unreachable assertions return for a
separate test/fixture repair, never get reclassified as a passed control.

Inventory at design handoff: **guards=42, mapped=42, missing=0, duplicate PC maps=0**.
Required execution roster is all PC-1 through PC-42, without sampling. Executed
here: **0/42**; all 42 remain pending post-land. Mutation completion requires
**all PCs run**, intended assertion reds, restoration and greens; ordinary Code
completion does not assert PC-clean.

### Out of scope

- No tag operation, publish/promote entrypoint (direct or indirect), live Windmill,
  registration/enablement, external report delivery, actual nightly/RC suite,
  provider/broker, deployment or rc mutation. Synthetic summaries are not native
  qualification, release authority or recipient receipt.
- CARD-1339 S1 owns exact remote-ref behavior. Closure of item (c) requires its
  actual landed SHA and ordinary exact-ref receipt, not this design or a plan tip.
  Allocation/publication/promotion guards stay with CARD-1339. Do not run
  C1311_PreReleaseTag or the whole release harness.
- General shared-lock crash/custody, selector tamper/recovery and watchdog delivery
  are unchanged and not claimed requalified. Real local child consumption in N
  is required; it does not prove native suite correctness or external delivery.
- Master landing does not update an rc-hosted core or registered definition. The
  preceding conditional backport table remains authoritative; separately commission
  the relevant reviewed backport when the next executing entrypoint requires it.

### Checkpoints

Exactly four rows, each one fresh isolated build and one filter. Run CP-1 and CP-3
at their separate committed/pushed test-only SHAs, and CP-2/CP-4 after their fixes.
The two red rows deliberately return failed-test exit 1; retain their raw red
receipts. The checkpoint tool does not convert Expect prose to an expected-failure
success code. Never mark those receipts green, flaky, or rerun them as historical
red at fixed source. Final ordinary Review executes only CP-2 and CP-4 at its fixed
reviewed SHA and inspects the historical red witnesses. Union = V-1..V-4/R-1..R-4.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1336-cp1/` | wrapper-red | `/*/*/ReleaseGateFollowupTests/C1336_WrapperRef` | V-1, R-1 | exactly 1 executed, 1 failed at C1336 W own-0 missing Ref, 0 skipped | 1 | 4 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1336-cp2/` | wrapper-green | `/*/Antiphon.Tests.Scripts/(ReleaseGateFollowupTests*)\|(ReleaseGateRegistrationTests*)/(C1336_WrapperRef*)\|(C599_ScheduleProvenance*)` | V-1, R-1 | exactly W and S, 2 executed, 0 failed/skipped | 2 | 5 |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c1336-cp3/` | identity-red | `/*/*/ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity` | V-2, R-2 | exactly 1 executed, 1 failed at C1336 N omitted CandidateId, 0 skipped | 1 | 6 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1336-cp4/` | identity-green | `/*/Antiphon.Tests.Scripts/(ReleaseGateFollowupTests*)\|(ReleaseGateRunTests*)/(C1336_NightlyCandidateIdentity*)\|(C599_CreditVerdicts*)` | V-2, V-3, V-4, R-2, R-3, R-4 | exactly N and C, 2 executed, 0 failed/skipped | 2 | 7 |

Parenthesized operands and suffix wildcards are CARD-0403 syntax. Escaped pipes
are Markdown syntax; the importer supplies literal pipes to the runner. The
class/method intersections select only the two listed methods. Check actual TRX
class/method names and counts, not discovery output. Adding a third matching
method requires reconciliation, not silently increasing the floor.

For each committed group invoke through the build-slot gate:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1336-checkpoint -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-1336-release-gate-followups-plan.md --rows CP-1 --expected-source-sha <full-committed-sha>
```

Replace only row and exact SHA for the other three groups. If run returns 75,
continue `wait <run-id>` until terminal; never end a turn with children in flight.
The tool takes row slots itself. Retain unedited CHECKPOINT lines, fresh TRX,
source/build receipts and tool status. Do not retry unleased. Validate green
receipts for the tested SHA and run check-evidence-diff.ps1 over base..fixed-tip.
Remove only owned alternate outputs after child completion; keep ignored receipts.
No Unit-wide, assembly-wide, native-profile or extra warm-up test is authorized.
An inherited collateral failure needs only its exact failing method at base,
reported as an additional diagnostic with reason/cost; never hide it with retries.

### Cost

All numbers are **estimated minutes**, not measured execution. TestDesign ran no
build/test, including no optional S1 proof. Its 20-minute dispatch budget is design
authoring, not the later Code/Mutation execution allowance.

- Ordinary V/R floor (Code) = CP-1 4 + CP-2 5 + CP-3 6 + CP-4 7 = **22**.
  Each includes 1 minute isolated build/setup; test/fixture portions are 3, 4, 5,
  6 minutes respectively (includes process launch and fixture construction).
  Checkpoint-tool first bootstrap/setup = **2** additional. Code verification
  total **24**; the original 105-minute envelope therefore leaves **81** for
  authoring/repair. Slot wait and unexpected fixture repairs are additional.
- PC floor (Mutation): exact W filter for PC-1..4, exact N for PC-5..23,
  exact C for PC-24..38, exact S for PC-39..42. Each of the 23 W/C/S controls
  budgets 3 phases x (1 build + 1 method/fixture) = **6**, total **138**.
  Each of 19 N controls budgets 3 x (1 build + 2 method/fixture) = **9**, total
  **171**. Thus all baseline/red/restored-green cycles = **309**, plus **2**
  snapshot/driver setup = **311**. Bind/discover/report time and slot waits are
  additional; do not commission Mutation inside the Code authoring envelope.
- Combined Code + Mutation verification floor = setup **4** + ordinary **22**
  + PC cycles **309** = **335**. Separate ordinary Review at the fixed SHA uses
  CP-2 + CP-4 = **12** (+ **2** cold setup if needed), giving **349** inclusive
  of that cold Review. No measurement or historical outcome is overwritten.
- No claimed wall-clock savings: **0 measured minutes**. Four isolated builds
  are required by the two red-first slices; post-land controls intentionally use
  fresh builds. Method-scoped selections avoid unrelated suites, but their saving
  is not quantified from an unmeasured broad-run baseline.

Design handoff is `next: code`: recipes, exact cases/oracles/reachability,
classification, guard mappings, four checkpoint rows and numeric floors are
bound. Concrete mutation patch payloads remain binding required after Code;
execution remains a separate post-land obligation.
