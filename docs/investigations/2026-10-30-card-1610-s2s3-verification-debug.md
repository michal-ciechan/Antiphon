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
