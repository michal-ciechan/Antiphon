# CARD-1810 Review round 1 (Final, Full) — task 0f994d7f

Subject: Code e9a63e43 (Grok author), origin/feat/card-task-e9a63e43, base 364e271331fc1af83f5b7eabaa956a8c0a289a5f,
red df644dee91, reviewed tip ecc72368510c0fb324e5170ce4ac7dec654766eb. Master 25b347f493cb781b693abc450e7090491c8638ae.

Verdict: CLEAN. No blockers, no regressions. Three advisories (card them; they do not hold the land).

## Runs at ecc72368 (each one build-slot lease, native output, UseAppHost=false)

| Run | Filter | Result |
|---|---|---|
| Build | tests/Antiphon.Tests | exit 0, 0 errors, 247 s |
| ProcessSpawnLimitTests | /*/*/ProcessSpawnLimitTests/* | 52/52, 0 skipped, 16.8 s; source walk test 4.07 s |
| Guard batch | TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests* | 52/52 (41+1+5+2+3, all five classes present), 1m51s |
| Marked classes + fixture caller | the seven [SharedCheckoutChild] classes + Scripts.RunCheckpointRepeatScriptTests | 43/43 (2+3+16+2+10+5+1+4), 8m08s |
| Checkpoint tool receipt | run 20261109-134837-4012, .antiphon extract CP-1 ProcessSpawnLimitTests | executed=52 passed=52, sourceState=clean buildSource=verified; `validate` CHECKPOINT SOURCE VALID rows=1 |
| check-evidence-diff | 364e2713..ecc72368 | commits=2 entries=0 violations=0, exit 0 |
| merge-tree origin/master HEAD | | exit 0, tree 6b9423a54309b82d866b813992a2d25f04d8f6f8 |
| land-graph -Check | merged tree (scratch detached worktree, Debug LandGraphAudit) | approved, exit 0 |

Diff touches 5 files (.cs/.md only); no csproj/props/package change; Microsoft.CodeAnalysis.CSharp 5.9.0 was already referenced.
tests/test-execution-policy.json unchanged.

## Mutant (Review's own)

Removed both `[ParallelLimiter<ProcessSpawnLimit>]` and `[SharedCheckoutChild]` from RerunFilterHostTests (the CARD-1807
A-1 repro) and from CheckpointRepeatFixture, rebuilt: ProcessSpawnLimitTests 51/52, the single red is
C1810_shared_checkout_source_keeps_marker_and_strict_limiter naming both classes. A-1 of CARD-1807 is closed. Sources restored.

Red at df644dee9 for the right reasons (by diff): red commit already carries the walk and the witnesses, the fix only widens
the census predicate and marks CheckpointRepeatFixture, so the 5 reds are nested/sub-namespace unmarked (skipped by the old
top-level-only census), nested/sub-namespace marked (old "marker outside top-level" miss) and the source-walk test
(CheckpointRepeatFixture unmarked). The Code's six mutants were not re-run individually.

## Check answers

1. Source discovery: `CheckpointFixtures.RepoRoot` (DockerStackDocuments.RepoRoot, the existing walker used across the suite)
   + tests/Antiphon.Tests/Checkpoints. Missing dir throws (red); empty/unparsed tree fails the seven-name floor. Cost 4.07 s
   in-suite (1.7 s warm, 117 .cs files).
   Scratch shapes through the built walker (reflection, isolated ALC):
   CAUGHT: object initializer; `new Process { StartInfo = new ProcessStartInfo { WorkingDirectory = RepoRoot } }`;
   local alias; static field alias; `global::`-qualified.
   MISSED: assignment statement `psi.WorkingDirectory = CheckpointFixtures.RepoRoot;` (also with Process.Start(psi));
   `pr.StartInfo.WorkingDirectory = ...`; helper taking the directory as a parameter; copy chain (documented);
   `using static` bare `RepoRoot`; ternary; `Path.GetFullPath(RepoRoot)`.
   No false positives: read-only RepoRoot, other types' WorkingDirectory, other ProcessStartInfo dirs (also witnessed).
2. Nested/sub-namespace: 52/52 green, so no existing class fails; top-level behaviour unchanged; `Checkpoints` + "." prefix
   avoids `CheckpointsExtra` (witnessed).
3. Witnesses: 9 new C1810 tests, all can go red (my mutant + red commit).
4. docs/testing-and-build.md paragraph accurate, apart from A-1 below (it says "assigns ... to a ProcessStartInfo working
   directory" then narrows to "a Roslyn walk of the object creation").

## Advisories

- A-1 (silent hole, cheap to close): the statement form `psi.WorkingDirectory = CheckpointFixtures.RepoRoot;` and
  `process.StartInfo.WorkingDirectory = ...` are not detected, though the docs and the attribute XML say a class that
  "assigns RepoRoot to a ProcessStartInfo working directory" must carry the marker. No live instance under Checkpoints
  today (grep). Fix: also accept a member-access assignment `X.WorkingDirectory = <root>` whose receiver is a local
  declared/created as ProcessStartInfo or a `.StartInfo` access, plus a witness.
- A-2 (transitive helper): the marker on CheckpointRepeatFixture (static helper, no tests) constrains nothing about its
  callers; a new Checkpoints test class calling `CheckpointRepeatFixture.RunScriptAsync` with no limiter passes the census.
  Same for any helper that takes the directory as a parameter. Fix: treat a call to a marked helper as a shared-checkout use,
  or document the limit.
- A-3 (scope mismatch): the walk scans the Checkpoints folder, the attribute census the namespace; a
  `Antiphon.Tests.Checkpoints.Sub` class in a file outside the folder escapes the walk.

Note: the worktree's tests/Antiphon.Tests/bin holds the mutant build (scratch); the checkpoint receipt used its own cold output.
