# CARD-1610 S2/S3 verification Debug

Commission: task 40e35f61, Linux/server2, 2026-10-30. Initial source
be2e50a02ec2a01990b0aaa796558d6b5a16b376. S2 and S3 must ship together.

## Timing assessment before execution

The preceding report's completed CP-27/28 hosts took 232.928/95.637 seconds
(341/382 executions); rows including admission took 278.712/141.528 seconds.
The last producer took 516.457 seconds wall, 426.436 seconds recorded build,
30 seconds admission. Those samples justify first rerunning the repaired
CP-17/28/52 and required CP-16 with one isolated producer, then continuing the
remaining exact rows from that same unchanged output. No repeated broad sweep
solely to obtain statistics is commissioned. This Debug record precedes the
potentially >30-minute aggregate. No desktop or native qualification is used.

Admission is bounded at 25 minutes per acquisition (caller explicitly allows
10–25 minute waits). Rows are serial because inherited integration fixtures share
state; production build occurs once for the selected S2/S3 group. Initial row
bounds retain max(2 minutes, 2x plan estimate), with CP-27/28 reduced to 10 minutes
from the provisional 50 on the above measured host samples. Smaller samples remain
provisional (CARD-1404); build and row custody work and variable load justify the
finite margin. Shared producer's scheduler minimum is 15 minutes. Overall task
budget is 150 minutes; stop before starting work that cannot be joined. Record
actual per-test p95, phases, admission and unattributed remainder after each run.
Unknown component timings remain unknown. Existing 20/90 phase deadlines and test
assertions are not altered for performance. No definition update is authorized.

## Initial failure triage

The historical 45 CP-28 failures belong to DelegationDispatchRecoveryBoundaryTests.
42 current-only cases were repaired by 5039ccb73e5482ef33c89258db00beca36a8afe9:
AbsentLaunchPolicy's strict EF column census needed to classify the new nullable
SourceTaskSupervisionNoticeId and require null for a pristine original brief.
Three C1151_Ceiling_failure_note_has_one_complete_user_prompt cases (eligible,
busy, crash) also failed on S2 tip b588cb5d058d810c1d56e4c6bca0196435f78349;
combined-feature attribution remains unresolved until this Debug reproduces them.
Receipt starvation fix e34fc074266f3e0a168a105880339038857bd976 also needs green.
The preceding custody refusal executed zero tests and remains incomplete evidence.

## Results

Pending. Exact rows: CP-15/16/17/19 and CP-21 through CP-91. S4-only
CP-13/14/18/20 and S1-only runner/native rows are excluded. Historical red
provenance is retained, never rewritten or rebound to this tip.

## S2 baseline reproduction (before repair)

Run 20261030-160625-cf79 on detached S2 tip
b588cb5d058d810c1d56e4c6bca0196435f78349: the exact C1151 method executed
3, passed 0, failed 3, skipped 0; clean source and verified build. All modes
(eligible/busy/crash) failed `await host.OverdueAsync(): expected 1, actual 0`.
The fixture supplies no `CountingRunner` session and no transcript observation.
S2 correctly defers an unknown ceiling observation, including this idle fixture.
This is an S2 integration-fixture incompatibility; it is not proved pre-existing
on master. Preserve the caller-note protection: provide the same seeded idle
session's actual accepted generation and complete two-entry transcript, then
retain every original failure, complete UserPrompt, dedup, busy/crash, decoy,
zero-stop, zero-input assertion. No production weakening is indicated.

Baseline build 326.992s; row wall 75.213s/admission 0s/execution 75.128s;
host startup 58.887s/tests wall 9.770s/teardown 5.413s/host 74.070s.
Run wall 425.106s, charged execution 425.093s, admission-only 0.013s.
Per-component DB/container/IO timing not yet extracted; unknown stays unknown.
Row deadline 4 minutes; source/build receipt and original report/TRX copied under
`.antiphon/c1610-debug/base-evidence/20261030-160625-cf79` before scratch cleanup.
Tool-bootstrap build (unlisted, required to execute the checkpoint CLI): 4.70s
compiler, 0 errors/5 existing warnings; admission 30s, slot held 6s. Import reused
that tool under a separate granted slot (0s admission, held 3s), accepted 92 rows.
The initial `--output` spelling was ignored by the importer; its default
`.antiphon/checkpoints.yaml` is the retained authoritative import. A scratch-cwd
preparation attempt and wrong-root wait refused before any build/test; no credit.

CHECKPOINT CP-28-s2-c1151 commit=b588cb5d058d810c1d56e4c6bca0196435f78349 build=ok filter=/*/*/DelegationDispatchRecoveryBoundaryTests/C1151_Ceiling_failure_note_has_one_complete_user_prompt executed=3 passed=0 failed=3 skipped=0 trx=/tmp/c1610-debug-s2-b588cb5d/.antiphon/checkpoints/20261030-160625-cf79/rows/CP-28-s2-c1151/run.trx slot=granted waited=0s dirty=0 source=b588cb5d058d810c1d56e4c6bca0196435f78349 sourceState=clean buildSource=verified

## Verification amendment and protective intent

C1151's fixture now supplies its actual seeded idle transcript and accepted
Running generation through the real runner-client seams. Every original assertion
is unchanged. CP-28 still selects the full 382-case class.

CP-92 is an explicit six-case plan amendment closing the prior report's untested
retention safeguards. Each case asserts pending debt retains its evidence, an
unrelated eligible control is pruned, and the same evidence becomes prunable after
Told. Removing the corresponding production exclusion would fail the retained
assertion; unconditional retention would fail the control/release assertions.
It adds coverage, not a production change. All shared-setup uses the assembly DB
fixture and per-case isolated schemas. No new process spawn or Slow exemption.

The selected roster runs CP-17/28/52/16 first so repaired rows cannot be hidden
behind lower-priority regressions, then every remaining S2/S3 row. Ordering is a
recorded scheduling deviation; exact original filters and floors remain intact.
The two historical broad classes form the aggregate CP-27 + CP-28 (723 cases).
Do not run another duplicate aggregate: those same complete rows satisfy it.

First combined producer 20261030-161544-a4d6 at d5bda599b1edc88e52fa5d43e4c6114747417422
failed compilation: three CS0246 errors from the fixture's missing
Antiphon.Server.Application.Dtos import. This is introduced by this Debug task,
not inherited. No tests executed and no red/pass witness credit is claimed.
Compiler wall 168.15s; producer recorded 216.070s, execution 216.378s,
wall 227.773s, admission 0s; source clean/build unknown. All 76 rows were
build-failed. Correct only the import and rerun the same roster, with a fresh
isolated output; retain this failed receipt and cost.

## First verified producer and remaining C1151 root cause

Run 20261030-162011-5604 at bbd0a546ec10e4722a4ee386d673d744a7a274e1:
producer compiled with zero errors (compiler 370.91s), clean source and verified
build. Build admission 285s. CP-17 executed/passed 22, failed/skipped 0: the
starvation repair is verified. Row wall 199.832s/admission 105s; startup
50.093s/tests wall 40.156s/teardown 3.788s/host 94.036s.

CP-28 executed 382, passed 379, failed 3, skipped 0. Exact historical identity
comparison proves ALL 42 census failures now pass. The three C1151 modes still
fail the original count assertion, not a relaxed replacement assertion. Runner
and snapshot evidence alone did not complete the fixture: OpenSweep lacks
SessionStateStore, so PersistTranscriptAsync records `Transcript ingestion
requires SessionStateStore` and CatchUpForCeilingAsync defers. Enable the real
SessionStateLoader/Store and action coordinator only for this fixture, preserving
the other statement-budget harnesses and every original C1151 assertion. No
production change is indicated. CP-28 wall 199.500s/admission 75s; startup
34.039s/tests wall 78.174s/teardown 11.040s/host 123.253s.

CP-52 executed/passed 247, failed 0, skipped 2; min 249 is UNCHANGED, so this is
roster-miss, not green. The two platform skips are:
- AgentPinPathTests.V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash:
  `Canonical pin cwd is a Windows drive-rooted path`.
- AgentExecutableResolverTests.Resolves_sibling_flavor_when_configured_one_is_gone:
  `Sibling executable flavors (.exe/.cmd/.bat) are resolved only on Windows`.
No native test is authorized in this Linux task. Do not repeat unchanged Linux
execution or lower the floor: the caller must commission the missing native
coverage separately. All AbsentLaunchPolicyTests and their strict census guard
passed in this row. CP-52 wall 112.027s/admission 105s; startup 4.877s/tests
wall 0.484s/teardown 0.752s/host 6.113s.

Stopped the scheduler before editing source to repair C1151. CP-16 was waiting
for a slot; it executed zero tests. The supported stop returned 0, subsequent
wait returned stopped/6, and executor PID 14421 was absent before edits. This
stopped run has no completed top-level report.json and is not a completed gate;
its state, TRX and original per-row source-bound CHECKPOINT lines are retained.
The next run reruns full CP-28 and all unexecuted rows from one fresh producer.
Unchanged CP-17 is not repeated; CP-52's explicit native gap remains owed.

Real land graph -Check PASSED at bbd0a546ec10e4722a4ee386d673d744a7a274e1,
using its verified producer's staged Antiphon.LandGraphAudit.dll, both evaluation
passes and canary approved, exit 0. Digest
7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99, count 68;
canary /work/worktrees/antiphon-landgraph-canary-74a71fbe2eb04c46bb0cca94caf4213f.
Outer slot waited 90s, held 39s. No -Update or definition-file mutation occurred.
Evidence-history guard over be2e50a..bbd0a546e: four commits, zero violations.

## Completed broad witnesses and newly exposed reds (before repair)

Run 20261030-164314-bf74 at d36e79f432307ff26ab687d06bad5e9a5f92cff1
verified full CP-28 **382/382**, including all 45 historical failure identities
(exact test-name comparison: 45 Passed). CP-27 also passed **341/341**, so the
broad-class aggregate is **723/723**, zero failures/skips. CP-16 met its unchanged
44 floor, CP-15 passed 12, CP-19 passed 22. The remainder's per-row state/TRX and
original CHECKPOINT lines are retained; final tables follow after repairs.

New red witnesses at that same source, before any repair:

| Row/test | Observed failure | Cause and required repair |
|---|---|---|
| CP-22 a_queued_only_prompt_is_no_boot_and_keeps_the_ceiling_failure(working-inherited-mid-turn) | expected Failed, actual Working | Obsolete pre-S2 expectation. Preserve no boot/failure/stop, assert the typed counted Working hold and one Held event. Idle arm still proves exact ceiling failure. |
| same method(terminal-session) | expected Failed, actual Dispatched | Live-ceiling sweep correctly defers a terminal session to dead-session ownership. Assert unchanged terminal-task status, no hold/completion/failure, no boot event and no stop. Existing complete dispatch-recovery class independently covers dead-session reconciliation. |
| CP-23 a_mid_turn_task_closing_on_its_ceiling_is_listed_before_the_sweep_fails_it | missing Closing on the deadline | Production copy collapsed approaching and breached ceilings. Keep the existing temporal assertion and give approaching decisions their own prefix. |
| CP-23 a_breached_deadline_says_the_sweep_is_about_to_fail_it | missing Past its deadline | Restore a distinct breached-decision prefix without promising failure for Working ceilings. Keep assertion and phase copy. |
| CP-26 C1151_Boot_branch_statement_counts(working-first-detection), (operator-escalation), (event-save-fault), (non-working-listed), (absent-proof-refused) | exact SQL budget 29 observed 30 | S2 adds an unnecessary generation SELECT after BootStallFacts already carries SessionStartedAt. Reuse that accepted observation for boot paths, retain non-boot reads and writer generation fences. Do not increase budgets. |
| same method(identity-changed-before-event) | exact SQL budget 23 observed 24 | Same redundant query; retain original exact budget and identity interleaving assertions. |
| CP-26 C1151_Only_an_accepted_prompt_is_protected(queued-only-past-ceiling) | expected overdue count 1, actual 0 | Fixture has a matching accepted runner generation but supplies no complete ceiling observation. Use the established seeded-observation seam for this arm only; keep every original idle-failure/no-boot/no-stop assertion. |

CP-22 executed 36 / passed 34 / failed 2 / skipped 0. CP-23: 167 / 165 / 2 / 0.
CP-26: 59 / 52 / 7 / 0. All are introduced S2 behavior/cost integration issues;
none is blamed on master or waived as inherited red. No assertion budget, timer,
retry or selection is relaxed. Their committed pre-fix tests are the red witness.

CP-46 passed 215, failed 0, skipped 1, min 216 unchanged (roster-miss):
AgentLaunchSpecTests.The_shipped_codex_definition_resolves_to_a_real_executable_on_this_machine
reports `The npm shim layout is Windows-specific`. Retain this native-placement
gap alongside CP-52's two skips; no unchanged Linux rerun is warranted.

Stopped the broad scheduler using its supported stop after CP-48: 27 green rows,
three red rows and CP-46 roster-miss; CP-49 was waiting for a slot (195 seconds,
zero tests), 42 later rows queued. Wait returned stopped/6 and executor PID 25017
was absent before source edits. This prevents leaving the known product defects
unrepaired inside the 150-minute task budget. No stopped run is a completed gate.
The next single producer runs the affected rows first, then the exact unexecuted
roster. Previously passing unrelated rows are not repeated. Historical outcomes
and costs remain intact, including the earlier stopped CP-17 green observation.
