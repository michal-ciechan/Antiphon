# CARD-1629 step 1 revised: native build output (task b9729f4b)

Outcome: done. Direct builds now use the worktree's native default output (`bin/`, `obj/`) with no
custom output. The `-StableOutput` helper is deleted. Witnesses went red, then green; four mutants
went red and were green after restore. Not landed.

Base: 7ea938f31dcc7b33e8077e604c792e7b3bab6f2a (feat/card-task-da2d3ae4 tip). Branch: feat/card-task-b9729f4b.

| Commit | What |
|---|---|
| 771d294e1371520159cd1f2fded72f5e1f4e9371 | test: red witnesses. Rewrites StableBuildOutputDocumentationTests and removes the C1629 `-StableOutput` harness cases (test-build-slot.ps1 and BuildSlotScriptTests.cs go back to ead93feb bytes) |
| 1a6ca785e697cbececbbeaa73382c3d3c680cf43 | feat: guidance rewrite; build-slot.ps1 and lib/build-slot.ps1 go back to ead93feb bytes (helper deleted) |
| f5b967e1f0d354157848b74b79b186a2866bb959 | test: custom-output regex narrowed (see below); new case pins the owner's native recipes |
| 8a019e96f493b91348cef9521f9af7d5c898b565 | docs: land-graph evaluator built natively plus `-ToolPath` |
| (this report) | |

## What changed

- `server/Bundles/delegate-basics.md`: "BUILD TO ONE STABLE ALTERNATE OUTPUT PATH" is now "BUILD IN THE WORKTREE'S NATIVE DEFAULT OUTPUT". The bullet says: pass no `-o`, `--output` or `OutputPath`; one build at a time per worktree; use the same properties (`UseAppHost=false`) on the build and the run. Checkpoint runs and land verification keep cold outputs, and any custom output that is still required ends with a forward slash.
- `.claude/skills/antiphon-delegate/SKILL.md`: the harness-rules summary now says native default output, no custom output, one build at a time.
- `.claude/skills/antiphon-orchestrator/SKILL.md` §1: the paragraph and the BUILD-SLOT RULE sentence below.
- `docs/testing-and-build.md`:
  - "Stable output for direct builds" becomes "**Native output for direct builds (CARD-1629)**", with the rule, the measurements and these hazards: a different configuration uses that configuration's default output; another `-p:` set shares the output and recompiles; `UseAppHost=false` goes on both build and run; a restored file with an old mtime needs LastWriteTime or `--no-incremental`; a crashed build is rerun, or rebuilt with `--no-incremental`; one build at a time per worktree; native bin/obj are left in place, and anything else is removed by exact path.
  - The cold exceptions are kept. Land verification is described accurately: it uses `--artifacts-path` outside the checkout.
  - The paragraph lists where a custom output is still used, always with a forward slash (CARD-0448): checkpoint rows, a build in the checkout a daemon runs from, and `test-isolated-helper-build.ps1`/`test-fake-provider-layout.ps1`. It also gives the land-graph evaluator's native route.
  - "Building while daemons run" is now scoped to the daemon's own checkout. The backslash gotcha and inventory text stay.
  - Ordinary recipes moved to the native output: CARD-0459 "Ordinary Code/Review", "Default Code/Review recipe" and its example, coverage-tool bootstrap, CARD-0443 run, Build slots example.
- `AGENTS.md` (one line, still under the 24,576-byte cap): "Direct builds use the worktree's native default output, one build at a time (CARD-1629)". The isolated-output wording becomes custom-output.
- `TESTING.md`: the Unit-lane example no longer passes `OutputPath=bin-c110/`.
- Helper: deleted, with no remaining need found.

Untouched on purpose:

- Preserved Gotcha #75 (`obj/` FileListAbsolute) is historical.
- Checkpoint-row `bin-<id>` text.
- `.claude/skills/antiphon-rc-qualification` Merge `-Check` "isolated OutputPath".
- `scripts/audit-land-verification-graph.ps1`: its default `bin-land-audit/` path. Changing it would be a script change; the doc gives the native `-ToolPath` route instead.
- `.antiphon/task-da2d3ae4.md`: a historical report that still describes the deleted helper.

## New BUILD-SLOT RULE sentence for the orchestrator's briefs

> BUILD-SLOT RULE: ONE build-slot lease PER BUILD (scripts/build-slot.ps1 -Label <what> -- dotnet ...); build in your worktree's NATIVE default output (no -o, --output or OutputPath), one build at a time per worktree, with the same properties on the build and its --no-build run; explicit generous --total-timeout WITH UNITS, UseAppHost=false, TEMP=/tmp. Checkpoint runs and land verification keep their own cold outputs.

## Measured on server2

Setup: a scratch worktree at 771d294e, tests/Antiphon.Tests, one lease each, UseAppHost=false, a private compiler server (`-p:SharedCompilationId=c1629n`, as in step 1), native output, no custom output.

| Build | MSBuild elapsed | Wall |
|---|---:|---:|
| Cold (new worktree, no bin/obj) | 220.3 s | 222.6 s |
| Unchanged rebuild | **15.2 s** | 17.2 s |

Step 1's stable OutputPath took 216.6 s cold and 15.8 s unchanged, so the native output matches it. The scratch worktree was removed with `git worktree remove --force` on its exact path.

Incidental, from my own worktree:

- 140.8 s cold.
- 140.5 s after the embedded-bundle edit (a Server-level change).
- 144.5 s after a one-file test edit.

## Witnesses (all runs through build-slot, native output)

StableBuildOutputDocumentationTests now has 36 cases:

- fresh-name regex: 12 files
- custom-output regex for direct builds: 13 files. It matches `StableOutput`, `OutputSuffix`, `bin-<task` and "alternate output path".
- no OutputPath value: TESTING.md, the bundle and both skills
- the native-output and one-build-at-a-time rules: 4 files
- the exact BUILD-SLOT RULE phrases
- owner phrases: CARD-1629, native default output, one build at a time per worktree, UseAppHost=false, cold exceptions, LastWriteTime, forward slash
- the owner's retired ordinary-build recipes

| Run | Commit | Result |
|---|---|---|
| Red | 771d294e (class) | 35 total, 15 failed, 20 passed |
| Green | 1a6ca785, 27 doc/bundle/build-slot classes | 391 total: 1 failed (my first regex `bin-<` hit the checkpoint `bin-<id>` and Gotcha #75 text; narrowed in f5b967e1) |
| Green | f5b967e1, same 27 classes | 392 total: 383 passed, 0 failed, 9 skipped (Windows-only by construction, as in step 1) |
| Harness | `scripts/test-build-slot.ps1` whole | 108/108 PASS (105 + 3 Linux), exit 0 |
| Final doc run | 8a019e96 (StableBuildOutput, CheckpointManifest, StandingBoot and BootStall documentation classes) | 52/52 |

The 27 classes included InstructionBundleTests (C1332 argv budget), AgentContextContractTests (AGENTS.md cap) and BuildSlotScriptTests.

Mutants (doc edits; no rebuild needed; restored with `git checkout`):

| Mutant | Result |
|---|---|
| M1: "FRESH output directory name per build" in the orchestrator skill | red 1/12 |
| M2: the old `--property:OutputPath=bin-<name>/` bullet back in delegate-basics | red 3/21 (custom-output, OutputPath-value and native-rule cases) |
| M3: step 1's stable-OutputPath BUILD-SLOT RULE back (7ea938f3 skill) | red 3/18 |
| M4: the old "producer-owned isolated output" Code/Review recipe back | red 1/1 |
| Restored, whole class | 36/36 |

## Gates

- Land graph `-Check`: approved, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE. The evaluator was built natively (`dotnet build tools/Antiphon.LandGraphAudit -c Release`, through build-slot) and passed via `-ToolPath`. `-Update` was not run. The canary was removed.
- check-evidence-diff: 7ea938f3..HEAD gives commits=4, violations=0. ead93feb..HEAD gives commits=7, violations=0.
- merge-tree: clean onto both origin/master and origin/rc/2026-11-01. Both are at ead93feb00fcaf8bf31018599437f17bee8e934c.
- No `bin-*` directories were created. Native bin/obj stay in the worktree.

Residue that is not mine: `/tmp/c1629-plain-one` is a detached worktree at 0e164b02, left by step 1 (da2d3ae4). I did not touch it.

--- next stage ---
next: Review
handoff: Diff-only Review of feat/card-task-b9729f4b (base 7ea938f3; red 771d294e, fix 1a6ca785, test narrowing f5b967e1, doc 8a019e96): CARD-1629 step 1 revised to the native build output, helper deleted; docs, bundle, skills and doc-test only, no product code. Then a Merge-role fast-forward of master.
artifact: .antiphon/task-b9729f4b.md
