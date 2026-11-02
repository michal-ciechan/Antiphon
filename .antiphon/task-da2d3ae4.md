# CARD-1629 step 1: stable per-worktree output for direct builds (task da2d3ae4)

Outcome: done. `scripts/build-slot.ps1 -StableOutput [-OutputSuffix <name>]` prints a worktree's one
stable OutputPath. The standing guidance now says to reuse that path for every direct build instead
of choosing a new name per build. Witnesses went red at the test commit, three mutants went red, and
everything was green after restore. No landing (as instructed).

Base: ead93feb00fcaf8bf31018599437f17bee8e934c. Branch: feat/card-task-da2d3ae4.

- 0e164b02df99807507e3b995b7b9d5cc31ae8987: red witnesses
- 58b45b3e8d19468a8bb685e382f77ff2af205e4d: fix
- the commit that carries this report

## Where the old guidance was and what changed

The literal "FRESH output directory name" sentence does **not** exist anywhere in the repository, and
it is not in the live `GET /api/orchestrator-instructions` either. It lives only in the orchestrator's
own brief boilerplate. The repo's per-build-name guidance lived in four places, and all four are
changed:

| Place | Change |
|---|---|
| `server/Bundles/delegate-basics.md` (embedded into every delegate's system prompt) | "BUILD TO AN ALTERNATE OUTPUT PATH ... bin-<name>/" becomes "BUILD TO ONE STABLE ALTERNATE OUTPUT PATH". The bullet now says: every direct build in this worktree uses `--property:OutputPath=<name>`, the `bin-<task>/` that `scripts/build-slot.ps1 -StableOutput` prints, with a FORWARD slash; reuse keeps MSBuild incremental; one build at a time per worktree; delete every `bin-<task>` before you finish (CARD-1629). The C1332 argv budget test is green with the larger bullet. |
| `.claude/skills/antiphon-delegate/SKILL.md` | The harness-rules summary now reads: one stable `--property:OutputPath=bin-<task>/` per worktree from `scripts/build-slot.ps1 -StableOutput`. |
| `.claude/skills/antiphon-orchestrator/SKILL.md` §1 | New paragraph: a brief that restates build rules pastes the BUILD-SLOT RULE sentence below and never asks for a new output name per build. |
| `docs/testing-and-build.md` | The "Building while daemons run" bullet now points to the stable name. The Build slots example now uses `bin-<taskid8>/`. A new paragraph, "Stable output for direct builds (CARD-1629)", has the measurements and the hazard table below. |
| `scripts/build-slot.ps1` header | Usage line and a paragraph for `-StableOutput`/`-OutputSuffix`. |

Unchanged:

- `AGENTS.md`: it has no per-build-name wording, it points to the owner doc, and it has a byte cap.
- `docs/orchestration-loop.md`, `server/Bundles/orchestrator.md`, `server/Bundles/stage-*.md`, `scripts/delegate.ps1`: none has output-directory wording.
- Historical plans and evidence under `docs/superpowers` and `docs/investigations`: they are records, not guidance.

## New BUILD-SLOT RULE sentence for the orchestrator's briefs

> BUILD-SLOT RULE: ONE build-slot lease PER BUILD (scripts/build-slot.ps1 -Label <what> -- dotnet ...); every direct build in your worktree reuses ONE STABLE output, --property:OutputPath=<what scripts/build-slot.ps1 -StableOutput prints>, one build at a time; explicit generous --total-timeout WITH UNITS, UseAppHost=false, TEMP=/tmp. Checkpoint runs and land verification keep their own cold outputs.

Practical note: when a build uses `-p:UseAppHost=false`, the later `dotnet run --no-build` must also
pass `-p:UseAppHost=false`. Without it, run looks for the missing apphost.

## The rule and its hazards (as written in docs/testing-and-build.md)

Naming:

- In a `task-<8hex>` (runner) or `card-task-<8hex>` (desktop) worktree, the name is `bin-<taskid8>/`.
- In any other worktree, it is `bin-wt-<first 8 hex of SHA-256 of the full path>/`. The path is lowercased on Windows.
- `-OutputSuffix <name>` (lowercase letters, digits and hyphens, at most 24 characters) gives `bin-<id>-<name>/`.

Behaviour: it prints one name and exits 0, with no lease and nothing run. It exits 2 outside a git
worktree, for an unsafe or empty suffix, or when a command follows it.

How the stable output handles each hazard of the old rule:

- **Trailing backslash** (CARD-0448): the printed name is always one relative segment that ends in `/`.
- **Different properties**: a different configuration or `-p:` set takes `-OutputSuffix`, so two property sets never share one output.
- **Checkout or commit**: MSBuild rebuilds from timestamps and CoreCompileInputs, so this is correct. The limit is a restored file with an old mtime: set its LastWriteTime (CARD-0412 D4) or use `--no-incremental`.
- **Crashed build**: rerun it. If it still misbehaves, delete the stable directory by exact paths.
- **Concurrent builds**: one build at a time per worktree. The slot broker does NOT serialise by worktree or label; a lease is a host-budget slot. `obj/` was already shared across output names, so the old rule never protected this case either. PC shards keep separate worktrees, each with its own name.
- **Cleanup**: delete each stable directory once at the end of the task, by exact path.

Exceptions that stay cold:

- The checkpoint tool and land verification. Never name the stable directory in a checkpoint plan; `SourceRunGuard` refuses an existing output.
- No direct build while a checkpoint run is in flight in the same worktree.
- Step 2 is excluded.

## Measured on server2 (one lease each, scratch worktree at 0e164b02, tests/Antiphon.Tests, private compiler server -p:SharedCompilationId=c1629)

| Build | MSBuild elapsed | Wall | CPU |
|---|---:|---:|---|
| Cold: fresh worktree, new obj, output `bin-c1629m/`, compiler server starting cold | 216.6 s | 219 s | compiler 2,366 CPU-s including server start; driver not sampled |
| Same output again, unchanged | 15.8 s | 18 s | driver 30.2 + compiler 2 = ~32 CPU-s |
| Same obj, new output name `bin-c1629f/` | 117.4 s | 120 s | driver 151.8 + compiler 1,215 = ~1,367 CPU-s |

The new-name compiler CPU (1,215) matches the investigation's warm-compiler cold reference (1,365):
a new name redoes essentially a whole compile. The scratch worktree was removed with
`git worktree remove --force` on its exact path.

Incidental data point, from my own worktree with the same `bin-da2d3ae4/`:

- Cold build: 236.9 s.
- After the embedded-bundle edit in `Antiphon.Server`: 202.6 s. Server-level changes are expensive, the investigation's d2 class.

## Witnesses and runs (all through build-slot; one build at 0e164b02 served the red and post-fix runs, then one incremental rebuild for the embedded bundle)

TUnit witnesses:

- `BuildSlotScriptTests.C1629_StableOutputName`: 6 harness rows. Stable for the same worktree and from a subdirectory; task id naming; different names across 4 worktrees; relative single `bin-` segment with `/` and no backslash, space or `..`; no lease and no command.
- `BuildSlotScriptTests.C1629_StableOutputSuffixAndRefusals`: 4 rows. Suffix name; unsafe or empty suffix gives exit 2; a command after the switch gives exit 2; outside a git worktree gives exit 2, with `GIT_CEILING_DIRECTORIES` set to the case root.

Doc-consistency witness: `StableBuildOutputDocumentationTests` (Unit, the same pattern as `RunnerBranchContractDocumentationTests`). It has three tests:

- A regex guard against "fresh output directory/dir/name/path" and "fresh bin-" across 11 standing-guidance files.
- A check that 5 guidance files name `-StableOutput` and "stable".
- A check that the owner doc states CARD-1629, `-OutputSuffix`, the one-build-per-worktree rule, the cold exceptions and LastWriteTime.

Results:

| Run | Commit | Result |
|---|---|---|
| Red | 0e164b02 | 19 tests: 8 failed, 11 passed. The 11 are the regex guard, which was already true in the repo because the sentence lives only in briefs. Harness cases: 10/10 FAIL at base. |
| Green | 58b45b3e, C1629 filter | 19/19 |
| Green | 58b45b3e, edited-file doc/bundle classes | 344 total: 335 passed, 0 failed, 9 skipped |
| Green | 58b45b3e, whole `scripts/test-build-slot.ps1` | 118/118 PASS (105 + 10 new + 3 Linux), exit 0 |

The doc/bundle classes in that 344-test run were InstructionBundleTests (including the C1332 argv budget), AgentBundleAttachment, InstructionFragment, DelegateBundleLaunch, DelegateLaunchArgvIntegrity, RolePolicyToolBundle, GrokRulesComposition, D2AdvisoryDocumentation, MechanicalPcContract, PostLandMutationContract, PostLandRetrospectiveContract, RunnerBranchContractDocumentation, OrchestratorInstructionsGuidance, StandingPipelinePolicyDocumentation, BlockedTaskParkProjection, CheckpointManifestDocumentation, CheckpointRepeatDocumentation, InstructionFileStamp, ScopedVerificationInstruction, TaskPlatformGuidance, AdvisoryChangePolicy, WindowsUnitSkipRoster, StableBuildOutputDocumentation and BuildSlotScript. The 9 skips are Windows-only by construction (DelegateLaunchArgvIntegrity backends, C1048).

Mutants (exact-string apply, method-scoped run, restored with `git checkout`):

| Mutant | Run | Result |
|---|---|---|
| M1: per-call random name | `C1629_StableOutputName` | red: 1/1 failed ("named by its task id", "same name every time") |
| M2: absolute path (`Join-Path $full $name`) | `C1629_StableOutputName` | red: 1/1 failed ("named by its task id", "one relative bin- segment") |
| M3: "FRESH output directory name" put back into the orchestrator skill | `C1629_standing_guidance_no_longer_asks_for_a_fresh_output_name` | red: 1 of 11 failed (that file's case) |
| Restored | those methods | 12/12 green |

## Gates

- Land graph `-Check`: approved, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE, count=69. The evaluator was first refused as not built; it was built through build-slot into `bin-land-audit/Release/net10.0/` and then deleted.
- `check-evidence-diff` (base..HEAD at 58b45b3e): commits=2, violations=0.
- `git merge-tree --write-tree origin/master HEAD`: clean. origin/master is still ead93feb.
- Touched paths exclude `tools/` and every land-verification path; the checkpoint tool and land verification are unchanged.
- All `bin-da2d3ae4` (36) and `bin-land-audit` (2) directories were deleted by literal path.

## Not changed, and why

- The orchestrator's actual brief boilerplate is outside the repo. The orchestrator must paste the sentence above.
- Delegates that are already running, and warm pool agents, keep the old bundle until they relaunch. That only happens after this lands and the AppHost or runner restarts.
- AGENTS.md is untouched (see above).

--- next stage ---
next: Review
handoff: Diff-only Review of feat/card-task-da2d3ae4 (0e164b02 red, 58b45b3e fix): docs, bundle and build-slot wrapper change for CARD-1629 step 1; no product code. Then a Merge-role fast-forward of master.
artifact: .antiphon/task-da2d3ae4.md
