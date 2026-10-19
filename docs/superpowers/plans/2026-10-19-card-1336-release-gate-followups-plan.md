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
