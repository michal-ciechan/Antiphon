# CARD-1629 Final Review round 1: native build output guidance (task 3deae49f)

Outcome: Clean. No defects. Reviewed tip `c4a7042913200d95d5f3564bf4f1a6f7706176b9` on `feat/card-task-b9729f4b` (Code owner `b9729f4b-903c-43f9-99ea-098e7351b578`). This review branch is `feat/card-task-3deae49f` and does not move that subject tip.

The commissioned round caps ordinary scope to the diff-only VERIFY list. That selection ran. The whole Unit lane was not run. PCs stay pending.

## Subject

Diff `ead93feb00fcaf8bf31018599437f17bee8e934c..c4a7042913200d95d5f3564bf4f1a6f7706176b9`. Nine files, guidance and `StableBuildOutputDocumentationTests` only. Code report: `.antiphon/task-b9729f4b.md`.

Empty diff (cold) against that base:

- `tools/Antiphon.Checkpoints`
- `scripts/audit-land-verification-graph.ps1`
- `server/Application/Services/LandVerificationBuildGraph.manifest.txt`
- `scripts/build-slot.ps1`, `scripts/lib/build-slot.ps1`, `scripts/test-build-slot.ps1`
- `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`

The `-StableOutput` helper is gone with the scripts. Those four files match `ead93feb0`.

## Hazards

Owner: `docs/testing-and-build.md` "Native output for direct builds (CARD-1629)" (about lines 1154–1183) and "Building while daemons run" (line 189). Bundle: `server/Bundles/delegate-basics.md` lines 55–59. Orchestrator sentence: `.claude/skills/antiphon-orchestrator/SKILL.md` lines 85–93. The three agree.

1. One build at a time per worktree is stated as an instruction, and the owner says a build slot is a host-budget share, not a lock on a worktree. A second build in the same worktree races `bin/` and `obj/`. PC shards use their own worktrees.
2. Daemon boundary: "A task worktree is not that checkout: build there in its native default output with no custom output." Custom output stays for a daemon's own checkout, a checkpoint row, and the two named scripts.
3. The trailing-backslash `OutputPath` gotcha stays on the daemon paragraph and on the custom-output paragraph (forward slash, CARD-0448). Native builds are told to pass no `OutputPath`.
4. A commit or checkout rebuilds from timestamps. A restored file that keeps an old mtime needs `LastWriteTime` or `--no-incremental`. A crashed build is rerun, or rebuilt with `--no-incremental`.
5. `UseAppHost=false` is required on the build and on every `--no-build` run, in the owner and in the bundle.
6. Cleanup: leave native `bin/` and `obj/` (worktree retirement removes them). Delete anything else by its exact path. The bundle does not teach `rm -rf`, a variable-built delete, or deleting `bin-<name>`.

The orchestrator BUILD-SLOT RULE sentence matches the Code report and the bundle: native default output, no `-o` / `--output` / `OutputPath`, one build at a time, same properties on the `--no-build` run, cold outputs kept for checkpoints and land verification.

`fresh output directory name` is absent from the repo after the mutant was restored. `server/Bundles` does not teach `OutputPath=bin-<` for an ordinary build.

## Land graph

`-Update` was not run. Both routes approved the same result.

- Native: `dotnet build tools/Antiphon.LandGraphAudit -c Release` (lease `7725d026-ca7b-445b-aee3-61d8ccefb9e3`), then `scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll`. Approved, count=69, digest `D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE`.
- Script default: build with `OutputPath=bin-land-audit/Release/net10.0/` (lease `4872802e-9c1f-44c8-8392-33018c21e832`), then `-Check` with no `-ToolPath`. Same digest, count 69, approved.

Canary directories for those two checks are gone. Native `tools/Antiphon.LandGraphAudit/bin/Release/net10.0` was left in place.

## Tests

Red at `771d294e1371520159cd1f2fded72f5e1f4e9371`, detached worktree, filter `/*/*/StableBuildOutputDocumentationTests/*`: total 35, failed 15, passed 20, skipped 0. Worktree removed (`remove_exit:0`).

Green at the tip, checkpoint `CP-1629` (lease `1ce705bb-9184-45ef-99f7-5db4345f9ee0`, waited 0s, build 00:03:21, slot held 358s):

`CHECKPOINT CP-1629 commit=c4a7042913200d95d5f3564bf4f1a6f7706176b9 build=ok filter=/*/*/StableBuildOutputDocumentationTests/* executed=36 passed=36 failed=0 skipped=0 trx=/work/worktrees/task-3deae49f/.antiphon/rev-c1629-cp/CP-1629-20261102-070110-221f/run.trx slot=granted waited=0s dirty=0 source=c4a7042913200d95d5f3564bf4f1a6f7706176b9 sourceState=clean buildSource=verified`

`scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/rev-c1629-cp/CP-1629-20261102-070110-221f/source.json -ExpectedSourceSha c4a7042913200d95d5f3564bf4f1a6f7706176b9` printed `CHECKPOINT SOURCE VALID` and exited 0. Receipt root stays gitignored.

Bundle and doc classes the edit touches, one `--no-build` run of 24 classes (InstructionBundleTests through BuildSlotScriptTests, including the argv budget and the AGENTS.md cap): total 363, failed 0, succeeded 354, skipped 9. The 9 skips are Windows-only (CommandLineToArgvW and C1048) and are not in the clean certificate. The worker's 27-class / 392 total figure was not reproduced as a count; the classes the wording and the bundle size touch are in this run and in the 36-case receipt.

Mutant: the orchestrator skill's "never a custom or new output per build" was temporarily "never a FRESH output directory name per build". Filter on the fresh-name cases: total 12, failed 1 (orchestrator skill), then `git checkout` restored the file. Phrase absent. Tracked tree clean. Not committed.

## Gates

- `git fetch origin master rc/2026-11-01`: both tips `ead93feb00fcaf8bf31018599437f17bee8e934c`.
- `git merge-tree --write-tree` onto each: exit 0, tree `ae5afa9d3ea21a0fc06006e74ba4ced0e4a0c3d5`.
- `scripts/check-evidence-diff.ps1 -Base ead93feb00fcaf8bf31018599437f17bee8e934c -Head HEAD`: commits=8, entries=2, violations=0. Repository is not shallow. Code's earlier "commits=7" was before the report commit `c4a704291`.
- Reviewer custom outputs `bin-rev3deae49`, `bin-land-audit`, and `bin-revreceipt` were removed by exact path. Native `bin/` and `obj/` were left. `/tmp/c1629-plain-one` (step 1 residue) was not touched.

## CARD-0824 H-3

Informational, consistent with the operator decision. The unmerged plan still recommends a stable `bin-ws/` per slot. This card uses native `bin/` and `obj/`, which that plan's keep-list already retains. The plan is not in this diff. Current runner behavior is still one worktree per task.

## Advisories (not defects)

- `docs/testing-and-build.md` links `docs/investigations/2026-10-31-incremental-builds-and-worktree-reuse.md`. That file is absent on this tip. The 4% / 59% / 97% figures are also inline in the table.
- The owner says "a pool slot keeps its worktree between tasks" in the present tense. That is the unlanded CARD-0824 shape. It does not change the direct-build command.
- Gotcha text for `ClaudeTrustPromptCanaryTests` still shows `--property:OutputPath=bin-trust/`. Checkpoint rows and the CARD-0222 history paragraph still mention `OutputPath=bin-<name>/` for those cold or custom cases. Historical plans under `docs/superpowers/plans` still contain older `OutputPath=bin-<name>/` recipes. Standing bundle, skills, AGENTS.md, and TESTING.md do not ask for a fresh output name.
- Code's evidence range was commits=7 before `c4a704291`. The reviewed range is commits=8, violations=0.

## Landing

Merge-role fast-forward of master for Code owner `b9729f4b-903c-43f9-99ea-098e7351b578` at `c4a7042913200d95d5f3564bf4f1a6f7706176b9`. `rc/2026-11-01` only if the operator decides. PCs stay pending for method-scoped SourceLanding Mutation. A master land is not activation.

verification=Final; scope=Full; final-review=none
