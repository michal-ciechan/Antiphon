# Review e853d02b: CARD-1850 S1 owned-process contract (round 1, Final/Full)

Subject: origin/feat/card-task-ace1a342 at ccdd6f70d472cb12301b87cf10aa6b47866dedc9
(Code owner ace1a342). Merge base 00b0f9c045a89773b52331928ed41141d01639d2.

Verdict: CLEAN. No blockers or regressions. Advisories A1-A5 are listed below and
should be filed as cards. None of them holds the land.

## Ordinary scope rerun (checkpoint tool run 20261113-072729-c784, one run)

| CP | executed/passed/failed | slot | notes |
|---|---|---|---|
| CP-1 | 28/28/0 | granted | all 20 methods (28 cases) present in TRX |
| CP-S1-2 | 14/14/0 | granted | GitIndexLockTests + ServerGitAssemblyTests |
| CP-S1-3 | 127/127/0 | granted | all 10 classes present; tests 275.7 s against the 720 s row cap |
| CP-S1-4 | 5/5/0 | granted | 4 ReleaseImpactEnvelope + TestLaneCategoryGuard |

Total: 174/174. source=clean, buildSource=verified, unlisted none, verdict GREEN exit 0.
Bootstrap builds (each under its own build-slot lease, native output): tools/Antiphon.Checkpoints
and scripts/regenerate-derived-impact-index (the driver that DerivedImpactIndexTests needs).

- CP-S1-3 margin: this run spent 275.7 s executing tests (DerivedImpactIndexTests 149 s,
  TestClassificationPolicyTests 102 s) plus a separate 355 s build. The author's 712.45 s
  is therefore host contention, not a stable cost. The row would go past 720 s if
  DerivedImpactIndexTests' driver/git subprocess work (merge_subset 52 s, committed_index
  29 s, override_file_names 30 s) roughly doubles under a full 4/4 slot load or a cold
  disk. That is advisory A5.
- Guard union: the 9-class union is complete across CP-S1-3 and CP-S1-4. DerivedImpactIndexTests
  is in CP-S1-3.
- Evidence diff, base..tip: commits=10 entries=0 violations=0.
- Merge-tree: clean against cf1794249 (the brief) and against the current origin/master
  ab67e69b77fe400c600529fa7cba7d7507bb0343. Master has since regenerated
  tests/derived-impact-index.json (batch 199), so the land path must regenerate and verify the index.
- Plan row split: the CP-18/18b (5), CP-19/19b (7) and CP-21/21b (12) class sets are
  identical to the original rows (mechanical diff). The table grows from 25 to 31 rows
  (3 splits plus 3 CP-S1 rows); the other rows are unchanged.
- No caller migration: the diff touches only src/Antiphon.Server.Git/OwnedGitProcess.cs (new),
  the contract tests, the ServerGitAssemblyTests census, the index and the plan. server/
  (LandingGit, CardFileRepository, OwnedProcessTree, OwnedChildMarker) is untouched, and
  nothing references the new types. The existing caller tests (CP-16..CP-25) were not run:
  they are E-slice rows deferred by the plan, and the impact selector does not name them.
- Marker: the same `ANTIPHON_REPOSITORY_CHILD` name as OwnedChildMarker.Variable. Dispose never
  signals (tested for both launched and unlaunched owners).

## Hard checks

1. Lifecycle (Prepared, Started, Terminating, Joined|Unconfirmed, Disposed). Start is once-only.
   Dispose is idempotent because of two guards (_disposeRequested and _closed). Dispose during
   termination defers exactly one close, and that close is published under the gate (8cbce853c).
   Termination is explicit only.
2. False success: Joined requires RootExited and Empty. The Empty constructor forbids survivors,
   and an Empty seen past the deadline is coerced to Unknown. An ambiguous or missing root
   never sets exited. Observation failures and OCE give Unknown. I tried counterexamples (late
   descendant, ambiguous start, timeout, cancel racing exit, a probe failure); none produced
   Joined. Descendant discovery itself is S2's census.
3. Linux adapter: it does no reads (the caller owns them), creates no threads, and its
   WaitForExitAsync honors the cleanup token. See A1 for the stream handles. It has no tests
   in S1; CP-2 (S2) owns them.
4. No behavior change for existing callers (point 3 under the scope rerun).
5. Mutants: not executed (operator waiver 2026-11-12; PCs stay pending). By reading the tests:
   - These go red: state skips (State is asserted), Joined := RootExited, removing the deadline
     coercion, and removing either deferred Close.
   - These survive: removing just one of the two Dispose guards (redundant, so equivalent) and
     any Linux adapter mutant (no S1 test).

## Advisories (file as cards; none blocks)

- A1 Linux adapter leaks the std streams on Dispose. LinuxOwnedGitProcess.Dispose only calls
  Process.Dispose. Process.Close does not close StandardOutput/StandardError once they have
  been read synchronously, or StandardInput once accessed ("user's responsibility"). The pipe
  fds therefore outlive the owner. The fake in the contract tests closes them, and the tests
  assert that, so the adapter does not meet the contract that S1 itself tests. Fix in S2:
  dispose the three streams explicitly, and test it in CP-2.
- A2 A non-observation exception from operations.Start escapes and leaves _launch null.
  Example: CaptureRoot throwing FormatException after Process.Start succeeded. The owner stays
  Prepared with a possibly live child: TerminateAndJoinAsync throws owned_git_not_started, and
  a second Start is allowed. Fix: classify any exception after delegation as Ambiguous.
- A3 Possible flake. ManualClock does not override CreateTimer, so the cleanup CTS and
  Task.Delay run on wall-clock time. Root_exit_requires_a_complete_empty_membership(1,True)
  expects Joined with a 1 ms allowance. If cold JIT or preemption takes more than 1 ms
  between creating the CTS and the third ThrowIfCancellationRequested, it reports Deadline.
  Fix: a larger allowance or a manual timer.
- A4 The lifecycle is not strictly monotonic. Joined (NoChild) and Unconfirmed (Ambiguous)
  re-enter Terminating. Two paths have no state assertion: Terminating to Unconfirmed through
  a non-observation exception, and NoChild to Joined.
- A5 The CP-S1-3 cap margin (above).

Native Windows (S3 onward) is still owed. All original V/R outside S1 are deferred to their slices.
