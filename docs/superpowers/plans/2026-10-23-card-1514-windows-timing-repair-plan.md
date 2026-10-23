# CARD-1514 Windows timing repair verification

Task 78334d64; original landing owner 78334d64. Task-start base
2f6c2e2ac62dd2dc9a3bb0e25d5cb45a01f3976a; instrumentation prerequisite
 a2aedb57fc98422a1c1723f4e1f0d4cef40d70f3 was restored by seven exact fast-forward
cherry-picks, without merging a target branch.

This bounded Final repair follows the operator brief: CP-8 only, no Unit lane or
whole suite. It retains V-1..V-13 and R-1..R-5 of the canonical instrumentation
plan. S3 V-14/V-15/R-6 remain outside this repair. Every PC-1..PC-16 remains pending
for separately commissioned Mutation. No daemon restart.

The original eleven-row manifest refuses git-unavailable before build. Git works
in isolation. This one-row manifest keeps the exact CP-8 filter/count but avoids
preflighting all unselected historical build groups. Both refusals remain evidence.

## Performance budget

One isolated build per committed source group, provisional 6 minutes based on the
Windows preflight build (348s); CP-8 execution provisional 5 minutes, including
five paired >=10-second-per-arm workload samples. Explicit row deadline 12 minutes,
total deadline 25 minutes; no test timeout is widened. External probe launch and
discovery each have 30-second hard limits. Actual-run probe retains nine expanded
results across three classes, including two intentional failures; these are nested
witness results, never nine additional checkpoint results. Setup is assembly-owned;
the db migration test necessarily exercises a fresh lifecycle. Timing artifacts
remain private and ignored, under fresh temp roots or .antiphon.

The repair tests require stable assembly identity and the five actual-run artifacts,
exactly nine joined CSV rows with TRX duration/group. Probe classes without custom
identity output guard normal-test joins. Live reader sharing is tested while the
writer remains open, and subsequent successful writes guard sink preservation.
The writer already uses FileShare.Read; Windows readers must reciprocally share
write access. Keep the buffered per-process writer and its per-line sink lock.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-8 | Windows-repair | `tests/Antiphon.Tests -> bin-c1514t-win-final/` | windows-minimum-preflight | `/*/*/(TestTimingRecorderTests*\|TestTimingLifecycleTests*\|TestTimingChokePointTests*\|TestPhaseSupervisorTests*\|TestPhaseSummaryTests*\|TestPhaseCustodyTests*)/C1514T_*` | V-1..V-13, R-1..R-5 | all 28 executed, 0 failed/skipped; real Windows + PS5.1 | 28 | 12 | true |

Separate authorized manual work: compile the exact repair picks on a detached
scratch checkout of origin/rc/2026-10-20; do not publish rc or tags. Run the evidence
history guard over task-start base..HEAD and inventory native OS witnesses.
## Repair and native witness inventory

Two old-source checkpoint builds exhausted the 12-minute build deadline during
helper staging (no test results); retain both receipts. The bounded real native
probe then failed `actual CSV duration/group/join missing`, with nine actual TRX
results and stale-input-not-joined. The original Windows preflight at rc
12a0b107f4c896979dda94b4afb628842f601883 remains the recorder/overhead red witness:
eight incompatible-reader failures, OFF wall 8847.5505ms <10000ms. Relevant recorder,
summary and overhead source was identical before this repair. These are historical
reds, not checkpoint-tool gate credit. Final CP-8 still requires fresh execution.

Shared recorder and readers are test-only, portable .NET APIs; their Windows
sharing behavior is witnessed by C1514T_IntervalAlgebra and the live-writer summary
in C1514T_PS51AndRerun. That method's real three-class probe also runs the production
native owner and verifies identical PS7/Windows PowerShell 5.1 summaries and nine
joined CSV rows. The unchanged Linux-only affinity helper is outside this Windows
repair; no Linux native claim is made. All changed qualification scripts are test
supervisor tooling; they are not imported by AppHost or the session runner.
Restart: none. Parent phase environment is enabled for final CP-8; the overhead
method explicitly disables OFF so it cannot inherit the parent's recorder.

PC-1..PC-16 remain pending. Additional variants for method-scoped Mutation:
PC-2 live-reader compatibility/disposal containment; PC-10 stable assembly name,
ordinary-test identity marker, CSV duration/group; V-13 minimum-sample continuation
and inherited-parent OFF isolation. Mutation owns deliberate red/restore/green and
missing-control discovery; this repair performs no deliberate mutants.
