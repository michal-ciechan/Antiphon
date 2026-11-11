# CARD-1830 Review round 1 (task a80b01f9): GitInProcess.Tests mirror

Verdict: CLEAN, no blockers. Advisories A-1..A-5 are for cards and do not hold the land.

- Subject: Code task 024cf048-16cd-4ee3-a2db-8983de904c4f, branch feat/card-task-024cf048, tip
  c538cff06545643a9b5db10fca79e7f73d20359c (Grok Code author). Base and master: 3db1a43b02246706dc28075fce7e7b5e84804ddb.
- Range reviewed: 3db1a43b0..c538cff06 (6 commits). Round 1, Final, Full.

## Verified

- Move: the five classes are 100% renames (`git diff -M`), namespaces stay `Antiphon.Tests.Infrastructure`, and there
  are 31 [Test]s plus the 3 witnesses. The class-level `ParallelLimiter<GitProcessSpawnLimit|ProcessSpawnLimit>` attributes stay. The
  mirror compiles its own linked `ProcessSpawnLimit.cs`/`GitProcessSpawnLimit.cs`, so the limiter is assembly-local.
- Mirror csproj: ProjectReference to the production project (no production source is compiled in). Compile links cover only test helpers. NO
  `DefineConstants`. The shape matches `Antiphon.Resilience.Tests` (same references, `Aliases="global,server"`, and no
  `ReflectionMode` attribute, as in the Resilience mirror).
- Hooks: the mirror `AssemblyHooks` carries ProductionRunnerGuard, PtyBackendEnvGuard, CheckpointTempRootSweep and
  LibGit2ConfigIsolation. Antiphon.Tests keeps its libgit2 isolation through `LibGit2ConfigIsolationAssemblyHook`. The shared helper
  carries no attribute, so it is not double-registered. TestDbFixture is omitted because no mirror test uses the database. This
  deviates from the Resilience hook, and the deviation is correct.
- Registration: solution; `git-in-process` in defaultSuites, the nightly and rc profiles, `suites` and `projectDispositions`; the
  release-authority rc requirement; nightly history, stopped-run seed and C599 suite lists; the C1313 literals 10/12/10;
  rc-project-categories (`git-operations`); impacted-classes kind. Footprint compared with Resilience by grep: every place that registers
  `resilience` now also registers `git-in-process`.
- Pins moved together. `audit-land-verification-graph.ps1 -Check` exits 0 (CP-11, twice). f15c9b88c touches only the two
  LandVerificationBuildGraph records. Derived index: I re-ran `scripts/regenerate-derived-impact-index.ps1` (build-slot)
  at the tip and it printed `derived-impact-index unchanged`. No allow-list file changed (`Antiphon.sln` 310->311 is a count).
- IVT: `Antiphon.GitInProcess` and `server` grant the mirror. The server grant is needed (`LandingGit.ParseRegistrations`/`PathsEqual`),
  follows the Observability.Tests precedent, and only widens test visibility.
- check-evidence-diff -BaseRef 3db1a43b0 -HeadRef c538cff06: commits=6 entries=1 violations=0, exit 0. The one entry is the Code report .md.
- `git merge-tree --write-tree origin/master c538cff06` (origin/master = 3db1a43b0): clean, tree 8e11cff1fdbf55f7553a1ba8fadc4a43383052f0.

## Checkpoints (fresh runs at the reviewed SHA unless stated)

Main run 20261111-223033-113a (/work/worktrees/task-a80b01f9), rows CP-2,3,5..11, unlisted none:

    CHECKPOINT CP-2 ... build=ok executed=26 passed=26 failed=0 skipped=0 slot=granted waited=105s dirty=0 source=c538cff06545643a9b5db10fca79e7f73d20359c sourceState=clean buildSource=verified
    CHECKPOINT CP-3 ... build=reused executed=25 passed=25 failed=0 skipped=0 slot=granted waited=120s ... sourceState=clean buildSource=verified
    CHECKPOINT CP-5 ... build=ok executed=31 passed=31 failed=0 skipped=0 slot=granted waited=0s ... sourceState=clean buildSource=verified
    CHECKPOINT CP-6 ... build=reused executed=77 passed=77 failed=0 skipped=0 slot=granted waited=0s ... sourceState=clean buildSource=verified
    CHECKPOINT CP-7 ... build=reused executed=237 passed=236 failed=1 skipped=1 slot=granted waited=0s ... sourceState=clean buildSource=verified
    CHECKPOINT CP-8 ... build=reused executed=109 passed=109 failed=0 skipped=0 ... sourceState=clean buildSource=verified
    CHECKPOINT CP-9 ... build=reused executed=155 passed=155 failed=0 skipped=0 ... sourceState=clean buildSource=verified
    CHECKPOINT CP-10 ... build=reused executed=7 passed=7 failed=0 skipped=0 slot=granted waited=555s ... sourceState=clean
    CHECKPOINT CP-11 ... build=n/a exit=0 slot=granted waited=165s dirty=0 ... sourceState=clean buildSource=notApplicable

- CP-7 red = environment precondition, not product: `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`
  failed with "The application to execute does not exist: scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll".
  That driver is not in Antiphon.sln. After the regen script built it, a build-slot rerun of that method was 1/1 passed (waited 0s).
  Skip = `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact` (SkipTestException off Windows, as Code reported).
- CP-4 run 20261111-223059-df6d (detached tip worktree):
  `CHECKPOINT CP-4 ... build=ok executed=45 passed=42 failed=3 skipped=0 slot=granted waited=75s dirty=0 source=c538cff0... sourceState=clean buildSource=verified`.
  The three failures are exactly long-path-untracked, same-size-edit-mtime-restored-settled and same-size-edit-mtime-restored-inside-the-index-second.
- Clean receipt run 20261111-232215-b19e (fresh detached tip worktree): CP-2 26/26, CP-3 25/25, CP-11 exit 0, verdict GREEN.
  `validate --expected-source-sha c538cff0...` -> `CHECKPOINT SOURCE VALID source=c538cff06545643a9b5db10fca79e7f73d20359c rows=3`.
  The main receipt validates `report_failed` because of the CP-7 precondition red.
- CP-1 red witness, plus inherited proof, at 510f9cfc3 (= master 3db1a43b0 plus only the witness file; build-slot build waited 150s,
  run waited 60s). Filter `(GitInProcessAssemblyTests*)|(LibGit2VsGitWorkingTreeVerdictTests*)`: total 73, failed 6.
  The 3 witnesses V-1..V-3 failed on Shouldly assertions ("should be ... but was"). The verdict class ran 70 cases with the same 3 Linux
  cases red. V-1..V-3 are therefore red before the move and green after (CP-2).

## Inherited CP-4 reds: inherited and Linux-only

All three are red at master-equivalent source in Antiphon.Tests and red unchanged in the mirror. They fail by construction on Linux:
- long-path-untracked expects libgit2 to fail past Windows MAX_PATH and fall back. On Linux libgit2 answers.
- The two mtime-restored cases encode NTFS stat semantics. Linux ctime/inode make libgit2 see dirty where git says clean
  (settled), and the reverse inside the index second.
Windows 0 failed is owed to the native Windows/rc lane; I could not run it here. No card tracks them (card search 0 hits).

## Advisories (for cards; not blockers)

- A-1 P-GITINPROCESS narrowing. GitInProcess source edits went from P-UNKNOWN (full) to core + `git.inprocess` (16 methods)
  + mirror remainder. Consumers outside that set are not selected: the SessionRunner `RunnerWorkspaceService` and the server
  `WorktreeManager`, `GitWorkspaceService` and `InProcessGitWrites` test classes in Antiphon.Tests. The selection is shadow-only today
  (catalog activation `unqualified`, `executionEvidence=false`, rc `gateCredit=false`), so no gate is weakened. It has the same shape as P-SERVER-GIT/P-RESILIENCE.
  Widen the set before selection qualifies. The csproj files still hit P-BUILD (full).
- A-2 File a card for the three Linux-by-construction LibGit2VsGitWorkingTreeVerdictTests reds: OS-gate them or give them
  Linux expectations. They now sit in the `git-in-process` suite.
- A-3 Plan CP-7 needs the out-of-solution DerivedImpactIndex driver prebuilt. A fresh worktree gets the red above.
- A-4 post-land-unit-shards.json covers only tests/Antiphon.Tests, so the 31 moved tests leave the post-land Unit lane. Nightly/rc now
  own them, the same as the CARD-1546/1829 mirrors (CARD-1729 umbrella decision).
- A-5 V-2 sees only `using Antiphon.GitInProcess;` lines. A fully qualified `Antiphon.GitInProcess.X` use in Antiphon.Tests would
  escape it.

## Pending

PC-1..PC-6 stay pending for method-scoped SourceLanding Mutation. The Windows CP-4 (0 failed) and C1210 Windows-only case
are owed to the native Windows lane.

## Unlisted runs (all build-slot leased, with reasons)

- checkpoint tool build (waited 225s). It is needed to run start/wait/validate.
- S1 Antiphon.Tests build and witness+verdict run at 510f9cfc3 in a scratch worktree. CP-1 needs S1 source, and the same build gives the inherited proof.
- regenerate-derived-impact-index.ps1 (held 221s). It builds the CP-7 driver precondition and checks index freshness. It also built Antiphon.Tests natively.
- CP-7 single-method rerun on that native build (held 15s).
- Clean-receipt runs. 20261111-232012-45f7 refused CP-2/CP-3 `pre-existing-build-source` because the red main run kept `bin-c1830-s2/`. I did not
  delete it (no rm -rf), and reran CP-2/CP-3/CP-11 in a fresh worktree as 20261111-232215-b19e.
- Wall time was about 70 minutes against the 40-minute budget. Build slots were 4/4 occupied for most of the run.
