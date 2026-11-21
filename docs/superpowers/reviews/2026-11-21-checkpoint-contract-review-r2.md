# Checkpoint tool project contract, review round 2

Subject: origin/feat/card-task-a320d237 at `dd64b66c1f2e08fd720cc42fc660d63ad925986d` (Code task `a320d237-62d5-46a2-a3b7-6aa503645404`, named as Codex-authored; commit author is `antiphon-server2-runner`). Reviewer task `55022a2d-47a9-4d24-8fde-6155231dbe5d` (Grok). Diff-only range `2bde7a4b4f8f79bd8db9e6fc9187340f5aaac715..dd64b66c1f2e08fd720cc42fc660d63ad925986d`: two commits, three files, +49/-0.

Verdict: **clean**. Round-1 blocker B-1 is closed. The Merge selector row selects `CheckpointToolProjectContractTests` for each of the three project files, and the base table does not. No new blocker. `next: land`.

reviewedSourceClean: false. The commissioned rows ran as leased `dotnet build` / `dotnet run` commands on the native default output. There is no checkpoint-tool receipt and no `validate-checkpoint-receipt.ps1` result. ordinaryScopeCompleted: Full for the commissioned selection (the four class filters, land-graph `-Check`, evidence-diff, pins, the selector proofs, and merge-tree). PCs stay pending. Mutation is waived (operator 2026-11-12). Author and reviewer are different companies.

## Range

| Commit | Subject |
|---|---|
| `275327725b7b95276882749192ad4fa1f14e01da` | `tests/impacted-classes.json` row `checkpoint-tool-project-contract`, plus `docs/superpowers/plans/2026-11-21-checkpoint-contract-r2-checkpoints.md` |
| `dd64b66c1f2e08fd720cc42fc660d63ad925986d` | `tests/derived-impact-index.json` only |

The index diff is two edges and nothing else. A new path `src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj` lists `CheckpointToolProjectContractTests`. The existing `src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj` edge gains that class beside `HostCleanupCheckpointExtractionTests`. `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` already listed `CheckpointToolProjectContractTests` and is unchanged. No production source, migration, package list, or startup input changed.

## B-1

`DerivedImpactIndexTests` at the reviewed SHA: total 27, failed 0, succeeded 27, skipped 0. `committed_index_matches_the_builder` outcome Passed, duration 37.563s (`/tmp/c55022-cp2/cp2.trx`). That case compares the committed index bytes with the builder, so the committed file matches a fresh build. This review did not run the regenerator again and did not rewrite the index.

## Selector row

The row's globs are `tools/Antiphon.Checkpoints/*.csproj`, `src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`, and `src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj`. Audit exit 0: each of those three globs has tracked hit count 1, and the hits are the three project files. The tool pattern is directory-anchored. It is the row from the why-missed note and round-1 A-1, not an unanchored leading filename wildcard.

`-MergeSubset -Range` on a synthetic commit parented on the reviewed tip, each diff touching only one csproj (name-only confirmed):

| Changed path | Table | Contract class | Kind `checkpoint-tool-project-contract` | migrations | windows |
|---|---|---|---|---|---|
| `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` | reviewed | yes | yes | false | 0 |
| `src/Antiphon.GitInProcess/Antiphon.GitInProcess.csproj` | reviewed | yes | yes | false | 0 |
| `src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj` | reviewed | yes | yes | false | 0 |
| same three paths | `2bde7a4b` table | no | no | false | 0 |

On the reviewed table the contract class is inside `CP-merge-subset-tests` for `tests/Antiphon.Tests`. The tool path also keeps kind `checkpoint-tool`. The GitInProcess path also keeps kind `gitinprocess-tests`. The HostCleanup path's only kind is the new row.

The actual task range does not touch a csproj, so it does not match the new row. Non-merge `-Range 2bde7a4b..dd64b66` has no hand kind and names five derived classes: `DerivedImpactIndexTests`, `ImpactedClassesSelectorTests`, `LandGeneratedArtifactTests`, `PostLandEarlyWarningTests`, `ReleaseImpactEnvelopeTests`. Windows filters: 0. `-MergeSubset` on that range adds no kind, sets `migrations=false`, and stays on the fixed high-radius classes. Downgrade selector count is 0.

The first two derived classes are the commissioned rows and ran. The other three were recorded and not re-run. The commissioned round is the selection above.

## OS branch

The range is a plan, the selector table, and the derived index. No OS-selected production behavior changed, so no witness is owed. No declaration file was invented for `scripts/check-os-witnesses.ps1`.

## Advisories

These do not hold the land. Round-1 A-2 (open package lists) and A-3 (unasserted Compile Include into GitInProcess) are unchanged by this diff.

- **A-R2-1. Checkpoint floors are below the counts this run executed.** CP-3 Min is 31 and `ImpactedClassesSelectorTests` executed 50. CP-4 Min is 78 and the nine-class guard filter executed 84. Min is a floor, both rows failed 0 and skipped 0, and `docs/testing-and-build.md` still states the nine-class count as 78. A later drop from 50 to 31, or from 84 to 78, would still meet those floors.

## Evidence at `dd64b66c1f2e08fd720cc42fc660d63ad925986d`

One `tests/Antiphon.Tests` build, then `--no-build` runs. Native default output, `UseAppHost=false`, `TEMP=/tmp`, `ANTIPHON_LINUX_COMPILER_POLICY=0`, `--timeout 570s`. Each command had its own build-slot lease. Load was polled to below 14 before each start.

| Check | Result |
|---|---|
| Build `tests/Antiphon.Tests` | exit 0, 0 errors, 2426 warnings, 3m 10.74s. Lease `53a26962-4893-4c9b-8233-2b2df38d4ed8`, held 193s. Start load 13.91. |
| `/*/Antiphon.Tests.Infrastructure/CheckpointToolProjectContractTests/*` | total 7, failed 0, succeeded 7, skipped 0, 4s 830ms. Lease `f1127e2c-e7e7-4b06-8809-edccfe6cecb0`, waited 45s, held 9s. Start load 13.28. |
| `/*/Antiphon.Tests.Scripts/DerivedImpactIndexTests/*` | total 27, failed 0, succeeded 27, skipped 0, 2m 49s 356ms. Lease `33163310-4c0f-4794-88ac-ee36792daab0`, waited 15s, held 174s. Start load 9.16. |
| `/*/Antiphon.Tests.Scripts/ImpactedClassesSelectorTests/*` | total 50, failed 0, succeeded 50, skipped 0, 5m 00s 176ms. Lease `97dd4332-a50f-4b84-a192-ea3c931e2336`, waited 60s, held 305s. Start load 9.24. |
| Nine-class guard union | total 84, failed 0, succeeded 84, skipped 0, 1m 10s 545ms. Lease `3940c77d-3805-4940-8b9b-d709096a0b3e`, waited 75s, held 75s. Start load 11.50. |
| `NativeLinuxOnly` marker | Not red on this SHA. `62ce9190` is not an object in this clone and is not named in `origin/master` history. |
| `scripts/audit-land-verification-graph.ps1 -Check` | exit 0, `approved`, digest `EF5DF60245B1F7B96F1A74B6E8CB09C355F966193D2431465BE1BBE28BFA33AA`, count=80. Lease `9d76ac19-e7c8-41cc-b40e-9377db630793`, held 35s. The tool was missing and rebuilt in 13s inside the script. The log has no `SatelliteResourceLanguages` refusal. |
| check-evidence-diff `2bde7a4b..dd64b66` | commits=2, entries=0, violations=0, exit 0 |
| Pins | `inputDigest=15137e48291f1b0c70668a7e967965ed1391f64812cddea271387d48ce1a9365` equals the tracked `PinnedInputDigest` |
| merge-tree `origin/master` `6be80dcccb9a9748f46d7c77f9c418d80e32f8bf` vs HEAD | exit 0, tree `e184f06f3a6356cad2628ec9943b00738d602d83`, empty stderr. No index conflict, so the land does not replay `tests/derived-impact-index.json`. |
| Downgrade selector | `migrations=false` on `-MergeSubset -Range` of this range. Count 0. |

The land-graph wrapper is `pwsh`, so the slot printed the known maxcpucount warning and did not pass `-maxcpucount` into the inner rebuild (CARD-1627). The inner rebuild is the script's own cold output, not an extra `-o` on the test build.

Code task `a320d237-62d5-46a2-a3b7-6aa503645404` is Failed. Its failure text is that the repair was pushed and verification was still incomplete at the 25-minute limit. `repairSourceTaskId` is empty. `worktreeBranch` is `feat/card-task-a320d237`, which is the reviewed ref. `-FromTask` is not needed. Because the owner is Failed, land it with `-RecoverReviewedSource` and `-ExpectedSourceSha dd64b66c1f2e08fd720cc42fc660d63ad925986d`. Do not pass `-ReviewEvidenceId`: this review's `reviewedSourceClean` is false, and a false or missing cleanliness flag is `review_evidence_source_not_clean`.

Positive controls remain pending.
