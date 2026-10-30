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
