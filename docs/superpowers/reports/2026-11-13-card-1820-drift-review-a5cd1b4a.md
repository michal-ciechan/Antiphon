# CARD-1820 Review: S1R6D algorithm drift refusal

Round 1 Final review of owner `2187186f-2c42-4791-aa7b-a73904c8c1bf`, branch `feat/card-task-2187186f`, tip `f3023cf517559700f53ce7f418eb5720fd436a61`. Verdict: clean. No product change.

## What the refusal binds

`Get-ReleaseImpactAlgorithmIdentity` hashes the raw bytes of exactly two files, via `git hash-object --no-filters`, and compares each digest to the committed blob from `git rev-parse --verify HEAD:<path>`:

- `scripts/impacted-classes.ps1`
- `scripts/lib/release-impact.ps1`

The published `algorithmDigest` is SHA-256 over `path + NUL + blob SHA + newline` for those two paths, and only after the raw disk hash equals the blob. A clean Linux checkout of this tip has blob, filtered, and raw hashes equal for both files (no BOM, no CRLF), so the guard does not false-refuse. CRLF, a BOM, trailing whitespace, a case change, or a different Unicode normalization form changes the raw hash. Git text clean filters cannot hide that from `--no-filters`.

A missing, unreadable, or unverifiable file makes Git exit non-zero. `Invoke-ReleaseImpactGit` throws, and the `-ReleaseEvidence` catch calls `Refuse-ReleaseImpact`: exit 2, `ok=false`, `complete=false`, `fallback=Full`, and no `algorithmDigest`. The same catch covers an unsupported HEAD and a hash mismatch (`release-impact algorithm source drift`).

Not in the inventory, and not executed by this release path: the relation builder, the evaluator, any canonicalizer, the derived-index driver, and its project file. Full S1R6 still waits on S1R4/S1R5. This envelope forces `complete=false`, empty classes, null graph digests, and `fallback=Full`, and `ConvertTo-ReleaseImpactJson` refuses a complete envelope. Ordinary selection does not load `scripts/lib/release-impact.ps1`. `scripts/impacted-classes.ps1` is unchanged in this range, so ordinary selection stays byte-identical. No nightly or qualification script reads `-ReleaseEvidence` or `algorithmDigest`.

## M7 witness

`AlgorithmSourceDriftIsRefused` has four arguments: an ordinary comment edit and a CRLF-only edit, each on both admitted files. Each variant checks clean incomplete/Full output, then exit 2, the drift reason, and the absence of `algorithmDigest`. The CRLF variants also require that filtered `hash-object` still matches the blob while `--no-filters` does not, so a green result is not a hidden normalization. `AlgorithmDigestBindsCommittedBlobs` checks the inventory digest against the Git blobs and that a committed algorithm change moves it. `FixtureCleansUpAfterFailure` requires the owned temp directory to disappear when the body throws.

Checkpoint CP-108 at the reviewed tip executed 10 and passed 10, including all four drift variants, the digest binding, and the cleanup case. Removing the mismatch check, hashing only one file, or ignoring the mismatch would leave at least one of those four variants at exit 0. Dropping `--no-filters` fails the two CRLF variants. Leaving the fixture fails `FixtureCleansUpAfterFailure`. Those controls were not executed: operator 2026-11-12 waived mutation execution, and every PC stays pending for SourceLanding. The pre-fix red at `5162524e7` (two CRLF variants, exit 0) remains the historical witness; this review did not replay it.

## Fixtures

Envelope fixtures are `Directory.CreateTempSubdirectory("antiphon-envelope-")` with disposal on success, body failure, and partial setup. No task id or worktree path is hard-coded. After CP-108 and the combined-tree run, `git status --ignored` shows no `.antiphon/selector-evidence` on either tree, and `/tmp` has no `antiphon-envelope-*` directories left.

## Combined tree

`git cherry origin/master` marks five commits as unique. Cherry-pick of those five onto `origin/master` `253bead91b5e735c99167824466b4e947f8f0eaf` applied cleanly, detached tip `745acd865ac8f05e5616f4aaa89b2031e35c151c`. The script and envelope test match the reviewed tip. The generated index differs from the branch index because master moved, and owner regeneration on that tree reported `derived-impact-index unchanged` (windowsOnly 31, linuxOnly 1).

`git merge-tree --write-tree origin/master f3023cf517559700f53ce7f418eb5720fd436a61` still conflicts, and only in `tests/derived-impact-index.json`. Land by rebasing these five commits, not by hand-merging that file, then rerun `committed_index_matches_the_builder`.

CARD-1877 round 2 (`origin/feat/card-task-53320468`) and `origin/feat/card-task-46940554` touch only `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs` and `ReleaseImpactProjectTests.cs`. None of those paths are in this slice.

## Verification

Downgrade selector on `4266477a650bae1075e783dfc2f619b68a1f0d70..f3023cf517559700f53ce7f418eb5720fd436a61`: the migrations row did not match, count 0. Impacted kind is `test-lane-category` only, classes `DerivedImpactIndexTests`, `ReleaseImpactEnvelopeTests`, `TestLaneCategoryGuardTests`.

Evidence diff: commits=5, entries=0, violations=0.

Checkpoint `20261113-113709-42c8` at `f3023cf517559700f53ce7f418eb5720fd436a61`, sourceState=clean, buildSource=verified, one build, unlisted none, exit 0:

| CP | executed | passed | failed | skipped |
|---|---:|---:|---:|---:|
| CP-108 | 10 | 10 | 0 | 0 |
| CP-8 | 16 | 16 | 0 | 0 |
| CP-9 | 5 | 5 | 0 | 0 |
| CP-101 | 78 | 78 | 0 | 0 |
| CP-104 | 57 | 57 | 0 | 0 |
| CP-107 | 4 | 4 | 0 | 0 |
| CP-4 | 4 | 4 | 0 | 0 |
| CP-106 | 4 | 4 | 0 | 0 |

Total 178/178. Wall 17m46s. Receipt: `.antiphon/checkpoints/20261113-113709-42c8/report.md`.

Combined tree `/tmp/card-1820-s1r6d-review-253bead91` at `745acd865ac8f05e5616f4aaa89b2031e35c151c`, leased native build (0 errors) then leased `--no-build` run of the envelope, project, relation, evaluation, and derived-index classes plus the nine-class guard union: 128 passed, 0 failed, 0 skipped, 6m22s. ProcessSpawnLimitTests stayed on the reviewed-SHA checkpoint (57/57) and was not repeated there.

Native Windows W1/V-17/R-5 stays pending. CARD-1866 A-2 and full S1R6 stay pending. No blocker.
