# CARD-2050 review, round 1 (Final)

Subject: origin/feat/card-task-644e673c at 03bdadefd9a59ef1c6cad16b70cb0297fe65011f (Codex author, Code task 644e673c-be19-4bcb-beeb-6554ce0277ae). Parent b83ebfd38d4b6a1b1a5346c34603846977693bd4. Master at fetch 3ed773829e584eec0c93ec8b47bf3517a69204d7. Reviewer task ad310231.

Verdict: **one blocker**. The cache behaves as claimed on a repeated output id, and the class rows are green. `scripts/audit-land-verification-graph.ps1 -Check` refuses the new props file, so this tip cannot be approved.

PCs stay pending.

## Blocker

### B-1. The land pre-check refuses the props file that places obj

Where: `tools/Antiphon.Checkpoints/CheckpointIntermediates.props` line 8 sets `BaseIntermediateOutputPath`. `tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs` `GetIntermediateRedirectElement` / `AssertNoDocumentationInput` (around lines 560 and 2033).

Failure: `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` exited 1:

```
definition file redirects the intermediate directory, which the audit cannot evaluate; -Update cannot approve it: tools/Antiphon.Checkpoints/CheckpointIntermediates.props BaseIntermediateOutputPath
audit refused: land graph pre-check refused
```

Why: every tracked `.props`, `.targets`, and project file is a definition file. Any element named `BaseIntermediateOutputPath`, `IntermediateOutputPath`, or `MSBuildProjectExtensionsPath` is refused, including under a `Condition`. `LandNonCodeSkipTests.C1409_IntermediateOutputRedirectRefuses` is that rule. The new file is the production mechanism that redirects obj, and nothing imports it from a csproj; the audit still refuses it because the element is present. `-Update` cannot approve the file, so a land-graph refresh does not clear this.

Fix: keep `BaseIntermediateOutputPath` out of a tracked definition file (generate the props into an untracked path at runtime), or add a narrow audited exception for this file together with a regression next to `C1409_IntermediateOutputRedirectRefuses`. The exception has to prove the file stays inert unless the checkpoint tool passes `AntiphonCheckpointIntermediateRoot`.

## What the runs showed

Server2, native default output, `UseAppHost=false`, `TEMP=/tmp`. Checkpoint `run` leased its own slot (`slot=granted`, `waited=0s`). Source for every receipt: commit `03bdadefd9a59ef1c6cad16b70cb0297fe65011f`, `state=clean`, `buildSource=verified`, `dirty=0`. `BuildStep.AppendSharedLayout` still adds the intermediate root only when `intermediateRoot is not null` (line 52).

| Run | Receipt | Result | Build seconds |
|---|---|---|---|
| Cold warm-proof | `.antiphon/checkpoints/20261121-195157-16a4` | CP-1 executed=1 passed=1 failed=0 skipped=0. Validator exit 0. | 293.6685284 (executed 304.4612839, wall 352.2080439). Output cleanup 270.277 s. |
| Warm repeat, same plan and `bin-c2050-warm/` | `.antiphon/checkpoints/20261121-201643-5a0d` | CP-1 executed=1 passed=1 failed=0 skipped=0. Validator exit 0. | 44.1225187 (executed 58.5600929, wall 108.070679). Output cleanup 293.554 s. |
| Class rows | `.antiphon/checkpoints/20261121-203313-93bd` | CP-1 executed=1 passed=1 failed=0 skipped=0, build=ok. CP-2 executed=284 passed=284 failed=0 skipped=5, build=reused. Tool verdict GREEN exit 0. | 284.149584 (executed 298.5277602, wall 339.3897682). Output cleanup 254.974 s. CP-2 tests wall 242.009 s. |

CoreCompile witness: the cold run created 48 `*.CoreCompileInputs.cache` files (42 projects printed an output path at normal verbosity; that verbosity does not print `Skipping target "CoreCompile"`). The warm repeat left all 48 mtimes unchanged. The class run used `bin-c2050-classes/` and rewrote 22 of those 48. `Prepare` hashes the manifest properties before `AppendSharedLayout` adds `OutputPath` and `ArtifactsPath`, so a new bin id shares the partition and MSBuild recompiles projects whose inputs include that path. The same-id repeat is the warm proof (293.7 s down to 44.1 s, 0 compiles). A different id is not free.

The class receipt's PowerShell validator exited 2 with `skip_identity_unlisted`. The five `NotExecuted` results are pre-existing Windows tests, absent from `tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs` at this SHA: `OutputCleanupTests.windows_set_clock_and_foreign_dacl_are_not_adopted`, `windows_seal_adopts_an_overwrite_before_exit_and_keeps_a_later_write`, `windows_copied_old_change_time_is_adopted`, `windows_sharing_violation_retries_then_adopts`, and `CheckpointOutputConfinementTests.windows_confined_build_leaves_use_app_host_unset`. The tool counts the row green (`failed=0`, `executed>=Min`). This is not a regression in the cache.

Output cleanup is the per-entry delete of the owned bin. During the warm run the owned bin peaked at 2190 files, owned obj files stayed 0, and the cache stayed at 941 files. The author's ~216 s is this phase. The cache leaf is a hex directory, not `bin-*`, and `.antiphon` is outside the isolated-id delete.

## Attack notes that are not the blocker

- Stale binary across commits. With the product stamp (`SourceRevisionId` constant `unknown`, `EnableSourceLink=false`), two SHAs that share one intermediate directory do skip CoreCompile when sources are backdated: a scratch app built commit B's obj, then built commit A's sources into that directory and the dll still printed B's mark. `PathFor` includes the commit, so production partitions differ. The matching partition with the same backdated sources printed the matching mark. Dirty, unknown capture, a mismatched expected SHA, and a missing expected SHA do not open the cache.
- Seal. `Invalidate` truncates `partition.seal` before the build. `Publish` runs only after exit 0, no timeout, clean source, a clean ledger, and a written build receipt. A `kill -9` after invalidate and before publish left seal length 0 and partial files; the next `Prepare` refused the partition. A non-matching seal does not self-heal.
- Lock. `.antiphon/checkpoint-obj/obj/run.lock` is `FileShare.None`. A second open does not share the cache; that run builds in its own output. Process death releases the handle, so a dead holder does not stick. There is no PID or start-time record. The lock is not under the deleted bin directory.
- Properties. Equal sets in different orders, and property-name case, share a key. Value case (`false` vs `False`) and `UseAppHost` false vs true do not. Relative and absolute entry paths share. The scratch `UseAppHost=true` build wrote `apphost-true` and left 0 obj files in the owned output.
- Tests. `CheckpointWarmBuildTests` calls production `BuildStep` and asserts the intermediate-root argument. Inverting `if (intermediateRoot is not null)` to `is null` made that harness print `LAYOUT-MISSING` and exit 1. The file was restored with `git checkout`; a later `--layout` printed `LAYOUT-OK`, and the receipts above are from the restored tree. A scratch props file that set `BaseIntermediateOutputPath` to `$(ArtifactsPath)/obj/` while the root argument was still passed put 18 obj files in the owned artifacts tree, and the argument-list harness stayed green. The committed test does not evaluate the props file.
- Disk and reset. `.gitignore` ignores `.antiphon/`. `WorkspaceSlotResetPolicy` keeps `**/obj/`, so a warm reset keeps `.antiphon/checkpoint-obj/obj/`. A cold reset removes `.antiphon/`. There is no age or count cap. One seal, 941 cache files, after these runs.
- Windows length. Longest relative cache file is 276 characters (301 absolute under `/work/worktrees/ws-04-d2`). The same relative path under `C:\Antiphon` is 288 characters, and under `C:\Antiphon\worktrees\card-task-644e673c` is 317. `Prepare` returns null on `IOException`, so a path that cannot be created falls back to a cold owned build. The diff contains no `.ps1` file.
- OS. The new branch is the MSBuild property `UseAppHost`, which `BuildStep.PropertyArguments` still sets to `false` off Windows. Both values were built on Linux and landed in different partitions. A Windows host run of the tool was not executed.
- Docs. Lines 705–711 state the cache contract. The older sentence in the same section (the real-dotnet rewrite adds `ArtifactsPath` and `UseArtifactsOutput=true` so referenced projects write into the owned directory) does not say that obj now stays in the cache.

## Gates

| Check | Result |
|---|---|
| `scripts/impacted-classes.ps1 -MergeSubset -Range b83ebfd3..HEAD` | exit 0. `migrations=False`. Downgrade selector count 0. Kinds: checkpoint-tool, spawn-limit-rosters, test-lane-category. |
| `scripts/antiphon evidence-diff --base b83ebfd38d4b6a1b1a5346c34603846977693bd4 --head 03bdadefd9a59ef1c6cad16b70cb0297fe65011f` | exit 0. commits=6 entries=0 violations=0. Slot lease 1896b5ee, held 22 s. The inner `scripts/antiphon` build is a shell wrapper, so the slot printed that `-maxcpucount` was not applied to it. |
| `scripts/test-live-lane-run.ps1 -PrintInputDigest` | `2f52dea1c0d7bfa5b2e9df170cd2ccf4adfbcebcf0628d26d167b59a53561f56`, equal to `$script:PinnedInputDigest`. |
| Derived impact index | `dotnet build` of `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` exit 0 (lease 9f44b06a). `-NoBuild` twice: both printed `derived-impact-index unchanged`. Tree clean afterward. |
| `git merge-tree --write-tree origin/master HEAD` | exit 0. Tree `bef632933e209b22c39851310caa1daf16fa0964`. No conflicts against `3ed773829e584eec0c93ec8b47bf3517a69204d7`. |
| Land-graph `-Check` | exit 1. Blocker B-1. The audit tool rebuilt once (17 s) while the 1-minute load was above 14; the refusal is the pre-check text, not the rebuild. |

## Advisories (card them; they do not hold the land)

- A-1. A props mutant that points `BaseIntermediateOutputPath` back at the owned artifacts tree stays green in `CheckpointWarmBuildTests`. The `BuildStep` argument mutant goes red.
- A-2. A bad seal does not self-heal. The partition stays cold until `.antiphon/checkpoint-obj/` is removed.
- A-3. A second concurrent run does not wait and does not refuse the run. It builds cold and does not share the cache. The lock has no PID or start time; process death releases it.
- A-4. No age or count cap. Warm slot reset keeps the cache because the keep list matches any `obj` segment.
- A-5. The fingerprint is commit plus `git status` porcelain. Ignored compile inputs are outside it.
- A-6. A different isolated output id shares the partition and recompiles (22 of 48 input caches, build 284 s versus 44 s for the same id).
- A-7. Windows path length above can fail to create the cache and silently fall back to a cold build.
- A-8. `docs/testing-and-build.md` still has the older "referenced projects write into that owned directory" sentence beside the new cache paragraph.
- A-9. The five Windows skips above are not on `WindowsUnitSkipRoster`, so a class filter that includes them does not pass `validate-checkpoint-receipt.ps1` on Linux.
