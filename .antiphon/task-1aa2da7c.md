# Review 1aa2da7c (CARD-1691, round 1 of 3, quick): clean

Subject: feat/card-task-7982c828, owner task 7982c828-f5e0-4025-b21e-5655d1043ea5.
Tested code SHA dd6c01e674126161f6e9e72e6aa0060c84382a30 on base master e56e4d1b6a727b3127b473307b860b7117980f09. The report commit f6c15619f is not code.
Platform: server2 Linux. TEMP=/tmp, UseAppHost=false, native default output, one build-slot lease per build or run.

## Test-only

`git diff --stat e56e4d1b6..dd6c01e67`: 5 files.
- 2 fixture scripts: scripts/test-change-aware-release.ps1 (+17) and scripts/test-nightly-failed-first.ps1 (+19). Both changes are new cases and a switch arm. No existing line changed.
- 3 test classes.
- scripts/lib/** and all production code are untouched.

## Witnesses (read)

- **F1.** `.gitignore`, `.github/workflow.yml`, `foo.git.txt` and `checkout/.gitignore` must be `kind=regular`, copied and byte-equal. `checkout/.git/HEAD` must be `git-dir` and not copied. The archived set must equal the regular rows. A substring match is caught.
- **C8.** The sentinel is written in OwnerFactory before the process starts. The test asserts that the directory exists, the sentinel bytes are intact, the exit is non-zero, and `Test-Bogus` appears in the output.
- **C4/C12.** The first root is deleted inside the case (`New-C1590GoneFirstRoot`), before the `finally`. The passed arm asserts its precondition (`PASS C1313 gone-root-fixture`: gone root, kept root, count 2), exit 0 and the C487 exit line. The failed arm asserts a non-zero exit and its own `c1590-original-failure`. Both arms assert that no `space *` directory or `.git` is left.
- **H.** `Environment.Remove("TEMP")` acts on the child ProcessStartInfo only, so the test process environment is never changed and parallel tests are safe. The probe deletes only its own empty root.

## Mutants (in place, uncommitted; restored with git checkout --; `git diff` is empty afterwards)

One build-slot run, 2/2 red:
- **F1** (`$relative.Contains('.git')` at the archive filter, :1425): C1590_ArchiveKeepsGitNamedFiles fails with kind expected "regular" but was "git-dir".
- **C8** (`if(-not $script:FixtureRoots){Remove-Item -LiteralPath $ResultsDirectory -Recurse -Force}` in the finally): C1590_UnknownCaseKeepsResultsDirectory fails with Directory.Exists(root) False.

## Runs

| Run | Result | Slot |
|---|---|---|
| Build tests/Antiphon.Tests | green | waited 120 s, held 171 s |
| Touched classes: `(RcFailedFirstExecutionTests*)\|(ChangeAwareReleaseTests*)\|(C487HarnessRootDefaultTests*)/*` | 13 total, 12 passed, 1 failed | held 2006 s |
| Base-commit check of the red | 1/1 failed on the same row | held 171 s |
| WindowsUnitPlatformAdmissionTests/C1210_* | 6 total, 5 passed, 1 skipped (the Windows-only `C1210_Windows_linux_only_outcomes_are_exact`), 0 failed | held 34 s |

Touched-class details:
- The one red is JournalCustodySurvivesInterleaving on row `FF ...hostDiscovery`. **It is inherited:** it fails at base e56e4d1b6 on the same row. That run used a detached base worktree, the same test output copied in, and the dll run through the build slot. It is NOT this branch's red.
- Timings of the new tests: ArchiveKeepsGitNamedFiles 4.5 s, UnknownCase 3.3 s, gone-root passed 61.7 s and failed 63.7 s, H 0.6 s.

Other checks:
- `check-evidence-diff -BaseRef e56e4d1b6`, head dd6c01e67: violations 0. Head f6c15619f: entries 1, violations 0.
- `merge-tree --write-tree` gives no conflict in any of the three: against origin/master (still e56e4d1b6), against origin/feat/card-task-4f64e784 (4ca16a502) and against origin/feat/card-task-5aaa94f1 (f422433cd).

## Advisories (no defect)

- **A-1. H on Windows.** The test means something on Windows too. The H1 mutant reads `$env:TEMP`, which is null in the child, so it is red on both platforms. H2 is checked by the repo-prefix test. The "TEMP unset" premise comes from Linux. The test should pass on Windows by construction: TMP stays set, and the TEMP and ROOT lines both come from the child's own GetTempPath, with native separators and a case-insensitive compare. Windows was not run here.
- **A-2. H timeout leak.** On the 60 s CancellationTokenSource timeout, `WaitForExitAsync` throws and the pwsh child is not killed. A hung probe would leak one process. This affects only the hang path.
- **A-3. Gone-root budget.** Both cases use the implicit Default 300 s, which is ~4.7x the 62-64 s seen here (n=2 across worker and review). That margin is ample for Windows, but there is no budget comment like the sibling's "2x measured" note. It is not an explicit 2x-p95 budget.
- **A-4. F1 archive contents.** The archive under the ignored `.antiphon/failfirst/<sha>/portable/` keeps the `.gitignore` and `.github/workflow.yml` copies. This follows the existing retention pattern, and no nested `.git` is kept.
- **A-5. CARD-1687 interaction.** CARD-1687 edits c487-harness.ps1 only in Clear-C487OwnedResultReadOnly, so it does not interact with New-C487Root or with these witnesses.

## Scope note

The brief commissioned a quick round: the touched classes plus C1210. All of them ran. The Final profile's whole Unit lane did not run, so the review evidence says Interim. The caller decides whether the quick scope is enough for this test-only change.

## Cleanup

- Every bin/obj from the build was deleted by literal path, along with `.antiphon/failfirst` and `.antiphon/test-output`.
- The scratch base worktree was removed.
