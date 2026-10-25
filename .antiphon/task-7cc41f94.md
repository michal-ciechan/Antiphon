Two Windows-sensitive fixture defects and Linux file-use test placement are repaired. Final Linux verification: 208/208 passed, plus 37/37 under forced Windows CRLF checkout settings; zero failures or skips. Native Windows qualification remains owed.

## Identity and limits

- Task: `7cc41f94`; assigned branch: `feat/card-task-7cc41f94`.
- Freshly fetched master at start/base: `e9703a6334df49f87490a0e67c1e5ac124f01aad`.
- Verified executable test source: `89b71226d3a177be36e5f6573c751837c42b0392`. The subsequent report commit changes only this Markdown file.
- Dependency outputs came from the full build at the base. All subsequent source changes are tests/fixtures; production dependency source is identical.
- Tested DLL: 68,698,112 bytes; SHA-256 `2c6d8d6a4e0db1f02486a722522212a29070cea0816e8cccf79f19f2c63eed86`.
- Original qualification `db2c2088`, candidate `a13e544e`, and its desktop TRX were not accessed. The original 50 native failures are caller-supplied counts, not failures observed here. No native test, desktop checkout, qualification lock, native slot, release script, tag, rebase, restart or live process was touched.
- Read the previous `9d2537e0` report and sibling `2a61a460` investigation. Their fixes were not repeated. The sibling's `FixtureFileDiagnostics.cs` is reused byte-for-byte (Git blob `0c3fbcd72808e471b572a2404fb9c7f3ce0bbec5`); its existing completed-card cleanup-result diagnostic pattern is also reused.

## Established mechanisms and repairs

**Completed-card line endings.** `LandingGitFixture.FixtureGit` overrides global Git configuration, but `WorktreeManager` starts native Git with inherited configuration. An owned child-only global config containing `core.autocrlf=true` and `core.eol=crlf` made native worktree creation write `seed\r\n`; the fixture's inspection and assertions require `seed\n`. On the unmodified base, `C1017_SourceShaMustMatch` reproduced the failed setup: cleanup returned without reaching the required `IntentPause`. That is corroborating setup-failure evidence, not the valid red witness.

The committed `C1017_WindowsLineEndingsDoNotDirtyFixtureCheckout` witness at `0f1d2799fdd65f9c4e8f912a815bf4a65a4c456d`, under that same child-only config, failed its actual byte assertion: expected `[115,101,101,100,10]`, observed `[115,101,101,100,13,10]`. The fixture now records repository-local `core.autocrlf=false` and `core.eol=lf` before native verification-worktree creation, so its writers and readers agree. Every existing cleanup, refusal, preserved-byte and deletion-intent assertion remains. The entire 37-case completed-card class subsequently passed under the forced CRLF config.

This establishes a Windows-style configuration mechanism on Linux. It does not establish that this was the desktop's actual configuration or explain all 35 original native failures without its raw results/native rerun.

**Git porcelain paths.** The worktree-manager assertions embedded native backslash paths in raw Git porcelain, whose Windows worktree paths use forward slashes. `Windows_registration_assertions_use_git_separators` forced `C:\fixture\worktrees\card-task-aabbccdd` against `C:/fixture/worktrees/card-task-aabbccdd` and failed the expected containment assertion at the same witness commit. A shared assertion formatter now uses Git separators. Both positive sibling-registration assertions and the previously vacuous negative unregistered-directory assertion use it. The witness and all 30 class cases passed. Native attribution of the second reported worktree-manager failure remains unresolved.

**File-use placement.** Eleven cases explicitly require Linux behavior and now skip before setup on other platforms. No existing assertion was removed or loosened:

| Method | Cases | Linux dependency |
|---|---:|---|
| `C1539_linux_holder` | 2 | `/proc` identities; mapped child explicitly rejects non-Linux; Linux unlink semantics |
| `C1539_absent_inputs` | 5 | `LinuxFileUseProvider` and real proc enrichment |
| `C1539_budget_and_caps` | 1 | Includes real Linux provider/input/resource bounds and persisted Linux evidence |
| `C1539_generation_and_ownership` | 1 | Proc start identities and ancestry |
| `C1539_redaction_before_sinks` | 1 | Reads a file written by the Linux-only evidence writer |
| `C1539_persistence_fail_safe` | 1 | Linux no-follow/owner-only evidence publication |

`C1539_trigger_and_original_error` and `C1539_success_is_inert` remain portable. Expected Windows placement is two executed cases and eleven explicit skips; that expectation is not a native receipt. All thirteen cases executed and passed on Linux. `FileUseWindowsTests`' two injected Restart Manager/CIM contract cases also passed. Product code already supplies a Windows capture provider, while `FileUseEvidenceWriter.Write` explicitly returns null outside Linux as existing S2 debt. This task does not implement or qualify durable Windows evidence publication, and does not claim the injected tests prove real Windows handle capture.

## Diagnostic-only work and unresolved native scope

- Completed-card fixtures print setup/deletion stages and cleanup result/residue. Direct guarded removal prints its complete result and Windows source/set-aside metadata. Root-deletion failure prints the original exception, PID, path length, metadata, Windows ACLs and bounded no-follow discovery of landing locks, then rethrows. Existing custody checks and no-follow deletion authority are unchanged.
- Native non-clean cleanup also retains bounded fixture Git registration/status observations through the existing runner. Diagnostic failure never replaces the cleanup result. These queries inspect the canonical fixture repository and do not alter the tree-status inspection counters asserted by the tests.
- `RepositoryChildRecoveryTests` prints raw, canonical and expected alias paths before the identity assertion, and both PowerShell 7 and Windows PowerShell 5.1 recovery decisions/stdout/stderr. The native alias or shell discrepancy was not guessed away.
- `LandingGitTests` prints expected/actual push endpoint arguments, budget exception/duration, and the live update-ref journal/common-directory sample. Its one original native failure did not reproduce here and receives no speculative behavioral repair.

Inspection found that production `WorktreeNoFollowDelete` already has Windows no-follow handles, reparse-point handling and extended disposition with read-only handling. No read-only, open-handle, ACL, long-path, case, junction, timeout or retry product change was invented without a native witness. The original Windows failures outside the demonstrated fixture mechanisms remain unresolved until the native rows run.

## Build and execution evidence

All build and test drivers used `scripts/build-slot.ps1`; every lease was granted with `waited=0s`, `maxcpucount=6`, and released. All tests used compiled output with `--no-build`. The checkpoint driver was deliberately not used because the test DLL exceeds its 64 MiB limit, as commissioned.

There was one full isolated dependency build, followed by two test-only recompiles in the same output directory using `--no-restore --property:BuildProjectReferences=false`. This is an explicit departure from a literal one-build-invocation budget: the additional test-only compiles were needed to execute the committed witnesses red and the repairs green. No second dependency build was run.

| Source | Build | Outcome | MSBuild elapsed |
|---|---|---|---:|
| `e9703a6334df49f87490a0e67c1e5ac124f01aad` | Full isolated build | 0 errors | 5m21.81s |
| `0f1d2799fdd65f9c4e8f912a815bf4a65a4c456d` | Witness test-only recompile | 0 errors | 2m32.28s |
| `89b71226d3a177be36e5f6573c751837c42b0392` | Final test-only recompile | 0 errors; 1,154 warnings | 4m29.18s |

Build shape:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-7cc41f94-baseline -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-debug-7cc41f94/ --nologo -v:q
```

Every class run used exactly `/*/*/<Class>/*`, a fresh result directory, TRX, and opt-in phase JSONL. No assembly-wide or Unit-category lane was run. Extra selections cover the shared completed-card fixture's consumers and the existing Windows provider seam.

| Class | Base passed | Final passed/failed/skipped | Final driver wall s |
|---|---:|---|---:|
| CompletedCardWorktreeRemovalTests | 36 | 37/0/0 | 64.891 |
| FileUseCaptureTests | 13 | 13/0/0 | 14.166 |
| WorktreeManagerGitIntegrationTests | 29 | 30/0/0 | 15.956 |
| LandingGitTests | 58 | 58/0/0 | 20.097 |
| RepositoryChildRecoveryTests | 15 | 15/0/0 | 67.966 |
| CardDoneWorktreeCleanupTests | not run | 20/0/0 | 39.966 |
| CardWorktreeCleanupRecoveryTests | not run | 6/0/0 | 32.120 |
| CardWorktreeMirrorCleanupTests | not run | 27/0/0 | 182.835 |
| FileUseWindowsTests | not run | 2/0/0 | 5.627 |
| **Total** | **151/151** | **208/0/0** | **443.624** |

Baseline five-class driver wall was 219.370s. An additional final run of `CompletedCardWorktreeRemovalTests` with the owned CRLF global config passed **37/37**, zero failures/skips, 54.142s driver wall. Thus final verification contains 245 passing executions across 208 unique selected cases. These are Linux scoped receipts, not a Windows group-65 or RC qualification receipt.

Both committed red witnesses executed exactly one case each and failed the intended assertion, with zero skips. The earlier base-commit `C1017_SourceShaMustMatch` CRLF probe failed setup and is retained separately, never counted as an assertion-level red witness. No timeout or retry was changed. Largest class selection was 182.835s; none reached 30 minutes. These single samples are provisional observations, not measured p95 budgets. TRX retains case durations, phase JSONL retains available DB/process phases; unavailable filesystem/other attribution remains unknown/unattributed remainder.

Generated evidence remains ignored under `/work/worktrees/task-7cc41f94/.antiphon/evidence-7cc41f94/`: build logs, `baseline/`, `final/`, `final-crlf/`, `red-path/`, `red-crlf-witness/`, `red-crlf/`, source-stamped summaries, phase JSONL, exact output inventory, build provenance and output-cleanup inventory. The initial Python helper launch could not execute because Python is absent; it ran no build/tests and carries no verification credit. A foreground Node driver performed the recorded runs.

All 33 producer-owned `bin-debug-7cc41f94` directories were removed after the test drivers finished; zero remained in the inventory. A rerun must build fresh isolated outputs.

## Native rerun owed

After the live desktop qualification releases its lane, a fresh native owner should build the landed source once and run the exact five assigned classes plus the three changed-fixture consumers and `FileUseWindowsTests` from the table above. Preserve full TRX and stdout/stderr. Example build and one class row:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group65-native-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-group65-native/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group65-native-completed -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-group65-native/ -- --treenode-filter '/*/*/CompletedCardWorktreeRemovalTests/*' --report-trx --report-trx-filename completed.trx --results-directory .antiphon/group65-native/completed
```

Use one exact class filter per row. Priority diagnostic methods are `C1502_NoDirectoryRecoveryLeavesSiblingRegistrationsAlone`, `C452_R2_AliasRecoveryUsesCanonicalCommonDirectory`, and the landing class's printed push/journal samples. Do not assume those are all of the original unnamed native failures. Keep the eleven Linux file-use skips visible; do not count them as Windows acceptance or evidence persistence coverage.

The forced-CRLF Linux profile is also reproducible after rebuilding: set `GIT_CONFIG_GLOBAL` only on the test child to an owned file containing `[core]`, `autocrlf = true`, `eol = crlf`, then run the completed-card class with the command shape above. Do not edit user/system Git configuration. The dedicated committed witness method is `C1017_WindowsLineEndingsDoNotDirtyFixtureCheckout`.

If deletion residue still implicates a handle, capture while that fixture process and path still exist with the already trusted/configured Handle tool and existing rights:

```powershell
& $trustedHandle -nobanner -v $failedPath 1> $handleCsv 2> $handleStderr
$handleExit = $LASTEXITCODE
```

Retain exit, both streams, path metadata/ACLs, and each returned PID's start time/path twice. An empty later census does not establish the original owner. No Handle executable was configured or launched by this task.

## Pushed slices and settlement

- `0f1d2799fdd65f9c4e8f912a815bf4a65a4c456d` — committed Windows checkout/path witnesses.
- `65e9af314ceff37ca3dc5b83febb886085cdf988` — Git-separator assertions.
- `b17c0cc0ffd888160a932ef38fd43f8203d75a2e` — fixture-local line-ending policy.
- `54ec7a14d0128d3584188e2662e5b01d173a79cc` — Linux integration placement.
- `89b71226d3a177be36e5f6573c751837c42b0392` — native diagnostics and exact sibling helper reuse.

Each slice was pushed individually to the assigned branch. `git diff --check`, the base..HEAD evidence-policy audit, fetched-master `git merge-tree --write-tree origin/master HEAD`, and final remote-tip confirmation are settlement checks; the caller-facing completion records their final outcomes. This single optional Markdown report is committed individually; generated evidence directories and receipts remain ignored.

--- next stage ---
next: land
handoff: Fixture-only repairs, witnesses and diagnostics; Linux verification is green. Native Windows rows and unestablished original failure attribution remain owed. No product change or RC qualification is claimed; no Review was run under the commissioned fixture-only profile.
artifact: /work/worktrees/task-7cc41f94/.antiphon/task-7cc41f94.md
