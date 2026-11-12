# CARD-1854 nightly checkout EOL hardening

Code task / landing owner: 9617f2d7. Base: 75878de2367fa059a16c0184ef297fa1dc7d9681.
The brief and CARD-1854 supply the accepted design; this manifest makes its narrow
test-first implementation and Final verification concrete. No earlier CARD-1854
Plan/TestDesign artifact was supplied or found.

## Invariant and slices

S1 commits the real-Git regression before the fix. S2 filters missing decoded paths
before removal and checkout-index, counts stale C-quoted and missing paths, and
includes skipped=N in the existing nightly sync message, including skipped-only
runs. Existing attribute selection and checkout failures remain strict. Scripts
remain ASCII and PowerShell 5.1 compatible. Invoke-NightlyGit keeps its native
capture behavior; the accepted skip-missing alternative needs no encoding change.

## Verification design

V-1: C1854_EolRepairHardening creates one local seed and core.autocrlf=true
clone before adding the LF attribute. Real ls-files output is decoded through
CP437 for one non-ASCII path and uses Git's own C-quoted form for another.
Assert stale fixture bytes, discriminating inputs, successful mixed repair,
literal space/bracket path LF bytes, unchanged skipped bytes, exact checkout argv,
mixed skipped=2, skipped-only skipped=2 without checkout, correctly decoded
non-ASCII repair, silent no-op, and clean tracked status. CP-1 must be red before
S2; CP-2 must be green after S2. These are behavior/byte assertions, not constants.

V-2: CP-2 runs all NightlyScriptsTests, including the original full-sync real-Git
regression and the ASCII guard. No rostered classification test is modified;
the conditional classification guard union is therefore not required by the brief.

V-3: CP-3 runs LiveLaneRunnerTests, NightlyCoreTests (nightly-live-lane match),
and TestLaneCategoryGuardTests (test-lane-category match). All other
tests/impacted-classes.json globs are unmatched by the task delta. The new C#
method reuses the existing limiter; no limiter attribute is added.

V-4: check-evidence-diff.ps1 covers the full base..HEAD; fetch origin/master and
merge-tree --write-tree must report conflicts truthfully. No merge is performed.

R-1: Native Windows PowerShell 5.1 witness and reused production clone
ls-files --eol timing remain pending for the orchestrator. Run the C1854 harness
under powershell.exe on the committed source, then the original C1313 harness;
inspect all rows and time ls-files on the owned reused clone without mutating a
live run. Linux forced-decoding proof is supplementary, not native credit.

OS audit: no OS-selected branch is changed. The portable repair is called by
Invoke-AntiphonNightlyRunCore after reset and before clean, including release-cut.
The native Git process in Invoke-NightlyGit is the real production dependency;
the seam changes only ls-files stdout, and all Git writes use real Git. Native
Windows decoding is pending under R-1. Startup census includes
scripts/lib/nightly-run-impl.ps1, a nightly bootstrap helper, not AppHost or runner
startup. No server/runner restart is required; no rc activation is commissioned.

PC-1 pending Mutation: remove missing-path exclusion; C1854 must fail the mixed
completion/exact checkout assertions.
PC-2 pending Mutation: remove quoted-path skip accounting; mixed and skipped-only
skipped=2 assertions must fail.
PC-3 pending Mutation: suppress the skipped-only log; its outcome assertion must fail.
All controls are method-scoped to /*/*/NightlyScriptsTests/C1854_EolRepairHardening
on a caller-commissioned SourceLanding candidate. No deliberate mutants in Code.

## Performance budget

One seed/clone per regression method is necessary to test pre-attribute checkout
state. No service, database or container setup. Original C1313 measured 6.2 s in
Review 3997c63a; new C1854 duration is provisional, collected by this task's TRX.
Retain checkpoint build/slot/run timing and harness evidence; detailed Git-call
phase timings are unavailable. The red witness measured 3.50 s test body and
317.80 s isolated build (sample=1, not p95). C1854 gets an explicit provisional
20-second execution budget and the existing 10-second cleanup budget; green TRX
supplies the next bounded sample. No timeout is widened. No full lane/assembly
or unbounded class set is needed. Total green estimate is 7 minutes including
one cold build. The red run cost 5m55s, retained separately; its initial estimate
was 4 minutes. CP-1 is rerun green; CP-2/CP-3 reuse that build. Admission is bounded
by the checkpoint broker defaults; execution uses its derived total allowance.
The method is rerun once as part of its required class; no further green repeats.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | tests/Antiphon.Tests -> bin-c1854-green/ | red-witness | /*/*/NightlyScriptsTests/C1854_EolRepairHardening | V-1 | all listed, 0 failed | 1 | 5 |
| CP-2 | S2 | CP-1 | touched-class | /*/*/NightlyScriptsTests/* | V-1,V-2 | all listed, 0 failed | 7 | 1 |
| CP-3 | S2 | CP-1 | impacted | /*/*/(LiveLaneRunnerTests*)\|(NightlyCoreTests*)\|(TestLaneCategoryGuardTests*)/* | V-3 | all listed, 0 failed | 6 | 1 |
