# Why Windows rc run 1fe763a9 missed the Git listing warning

Windows qualification `1fe763a9` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` failed `Status_and_ls_files_verdicts_match_git(unreadable-directory)` in `git-in-process-all`. All eight mismatches were Git exit zero plus `warning: could not open directory 'locked/': Permission denied`. Run `b8cbfb24` passed at `2fd61b643ea4231fa732cd4969b5284d43b33f39` with the same Git for Windows 2.50.1.

## Introducing commit

`db4240f2afbd9b677c2a292395967520af193172` extended the status and `ls-files --others` matrix to compare full listings through `SameBytesAsync`. The helper already rejected all stderr when used for index and tree listings. The existing path-set comparison already accepted the directory warning, following `9bcd61113f631ec58f158ff3fb637736fb786fd4`. The unreadable-directory fixture and its ACL setup are identical at the two qualification pins. The first-parent status-listing series ends with the index refresh at `70c59db413f428800660b21b6a6a8c4b8bdbd98b`. No card link for that series was found in the commit records.

## Approving reviews

The three fix commits `7e505938e2e5dcecc8e5bbde4846a4a4c8cac1c9`, `64118274c899aa92be2c8e508809596c8d762c19`, and `153246556f223f63e2a472aa9c5e6f241da641f9` label Review rounds 1–3. They record findings and repairs for ignored rows, copies, and attributes/config agreement. The scoped project task history and repository did not yield an approving Review tied to those SHAs with an exact filter, platform, and Full or Interim scope. The review-round labels alone cannot establish which cases ran. The series appears directly in the first-parent history, with no associated Merge commit or retained Merge receipt for the series.

## Outside the selection

The case is an `Integration` test in `tests/Antiphon.GitInProcess.Tests`, with its own `git-in-process-all` qualification chunk. The [recorded cut selection](2026-11-26-rc-cut-gates-at-0b07453e4.md#linux-repair-union-at-the-graph-refresh-commit) at the subsequent graph refresh and binding repair ran cut guards and named repair witnesses. Its Git-related row selected `LinuxOwnedGitProcessTests` in `Antiphon.Tests`, so it never reached the mirror's differential matrix. The short Merge policy limits execution to each subject's named witness rows. The [impacted-classes mapping](../../tests/impacted-classes.json) already includes the mirror class under `gitinprocess-tests` for changes to its library or test project. The evidence shows a missing execution row in the cut selection. It does not show a missing path mapping.

[CARD-2082 inventories the class](../superpowers/plans/2026-11-26-card-2082-invariant-inventory.md#product-and-fixture-controls-that-stay-with-their-owners) under product and fixture controls that stay with their owners. It is outside the proposed repository-invariant migration workload.

## Cheapest pre-land check

For `src/Antiphon.GitInProcess/**` and `tests/Antiphon.GitInProcess.Tests/LibGit2VsGitWorkingTreeVerdictTests.cs`, explicitly select `/*/Antiphon.Tests.Infrastructure/LibGit2VsGitWorkingTreeVerdictTests/Status_and_ls_files_verdicts_match_git` in `tests/Antiphon.GitInProcess.Tests` and check that the `unreadable-directory` case ran. The Linux runner must be unable to list the fixture after `chmod 000`.

At witness commit `f76cf2fe6242844d134377471bd8d1500f26906d`, server2 ran as `app` (UID 1654) with Git 2.47.3. The source-verified checkpoint executed 62 cases, with 61 passed, one failed, and zero skipped. The one failure reproduced all eight warning-only mismatches and reached the assertion after comparing paths, exit codes, and stdout. The fixture now proves that regular Git exits zero, emits the expected warning, and reports `visible.txt` in its porcelain listing while omitting `locked/inside.txt`. The fix shares a diagnostics check between the verdict and listing helpers. Only this fixture permits the exact warning on a successful command, and both helpers still check the listing. This is a deterministic oracle failure when the directory is unreadable. A root Linux or elevated Windows process that can list the directory hits the fixture's explicit skip.

## Native confirmation and rc backport

Server2 exercised POSIX mode bits. The Windows `icacls` deny and its restoration require a native run. `rc/2026-12-06` needs the witness and test fix before the next qualification. Run the same matrix method in `git-in-process-all` under a Windows account for which the ACL deny is effective. The `unreadable-directory` result must be an executed pass in the TRX, including the Git exit-zero and warning preconditions, the visible stdout row, and the in-process comparison. A privilege-dependent skip cannot confirm the case. The repair touches only a test and this note, so the startup-path rc witness is inapplicable.

## Exemption

None.
