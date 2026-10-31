# Cold vs incremental builds and per-task worktree reuse (2026-10-31)

Task cf8b6982 (Investigate, server2/Linux). The operator asked: "Are we building from scratch or incremental? Maybe we should stop creating fresh worktrees for each card/stage?"

This investigation was read-only on the product and on other tasks' worktrees. It used one scratch worktree, `wt1`, from `origin/rc/2026-10-27` (`e0d49d221513b467f8c853f47587e688e04255e2`). It also used two `git clone --shared` checkouts of the same SHA, `wt2` and `wt3`. These are separate repositories, not registered worktrees, and they existed only to test a second fresh tree and a seeded obj. Every build ran under its own `scripts/build-slot.ps1` lease. Nothing was killed or restarted. Raw logs stay in session scratch (generated evidence rule).

## Verdict

**Builds are mostly cold, but the per-task worktree is not what makes them cold.** All 173 checkpoint-tool builds in the last 24 hours were cold by construction, 11.98 build-hours in all.

- **The checkpoint tool forces it.** It puts every build's obj under a fresh `bin-<id>/artifacts/obj/<Project>`:
  - `ArtifactsPath` and `UseArtifactsOutput=true` are set at `tools/Antiphon.Checkpoints/Execution/BuildStep.cs:42-50`, with `BuildConfinement.cs:88` supplying the path.
  - `SourceRunGuard.Invalidate` (`Evidence/SourceRunGuard.cs:113-118`) refuses any pre-existing output.
  - Cleanup then deletes the outputs, so the next run starts empty.
- **Worktree age makes no difference.** 122 of the 173 builds (71%) ran in a worktree that had already built once. Their p50 was 251 s, against 290 s for first builds.
- **A copied obj does not help.** Seeding a fresh tree with a copied obj and bin was measured to do a full re-restore and recompile: absolute paths invalidate it.
- **The "fresh output directory each run" rule costs almost a cold build.** On a gated direct build, a new `OutputPath` with the same obj cost 97% of cold CPU.

**Top recommendation:** keep per-task worktrees and land verification cold. First stop the waste in the build recipe, not in worktree creation:

1. The checkpoint tool reuses its own verified build when a later run is at the same commit and properties: 32 builds a day.
2. Direct gated builds keep one stable `bin-<name>/` per worktree instead of a fresh name per run.

Measured, these save about 2 to 3.5 build-hours and 9 to 17 CPU-hours a day, roughly 16 to 30% of checkpoint build CPU. That is a few percent of slot capacity, so **the 10 to 25 minute slot queues will not go away by fixing builds alone**. Per 32df662c, those queues come from multi-build loops that hold one lease. Worktree creation costs 2.8 s and is not worth optimising.

## 1. Cold vs warm, measured

Every run built `tests/Antiphon.Tests/Antiphon.Tests.csproj` (40 projects in its graph) at Debug with `--property:OutputPath=bin-<x>/` and the slot grant's `-maxcpucount:6`, on a shared, loaded 24-core host. The columns:

- **Build**: MSBuild "Time Elapsed".
- **CPU**: MSBuild/driver children (via `times`) plus a **private** VBCSCompiler (`-p:SharedCompilationId=cf8b6982`), so that compile CPU is not mixed with other tasks' builds.
- **Peak RSS**: summed over the build's process tree plus that compiler server.

| Run | What | Build | CPU-s | % of cold CPU | Peak RSS |
|---|---|---:|---:|---:|---:|
| a | Cold: fresh worktree, fresh obj and output, compiler server starting cold | 406 s | 2,336 compiler only¹ | (148%) | 15.7 GB (compiler 7.1) |
| e1 | **Cold reference**: fresh clone, fresh obj and output, warm compiler server, warm package cache | **170 s** | **1,579** (driver 214 + compiler 1,365) | 100% | 8.0 GB |
| b | Same build again, same output: no-op | 29 s | 57 (48 + 9.5)² | 4% | 7.2 GB |
| c | Same obj, **fresh output dir name** (`bin-cf8c/` after `bin-cf8a/`) | 203 s | 1,532 (210 + 1,322) | **97%** | 8.0 GB |
| d1 | One-line edit (private const) in an `Antiphon.Tests` file, same output | 114 s | 930 (134 + 795) | 59% | 7.5 GB |
| d2 | One-line **public API** addition in `Antiphon.SessionRunner.Contracts` (referenced by Server and SessionRunner), same output | 210 s | 1,487 (172 + 1,315) | 94% | 8.3 GB |
| e2a | Fresh clone at another path, **seeded** by tar-copying (mtimes kept) every obj/ and `bin-cf8e/` from e1, same output name | 188 s | 1,569 (202 + 1,366) | **99%** | 9.2 GB |

¹ The driver CPU for run a was lost: the first version of the sampler read `times` in a subshell. The compiler figure is that server's whole lifetime CPU at that point. It includes server start, JIT and tier-up, and it ran under heavier contention. e1 is the representative cold cost, because server2's shared compiler server is normally warm.
² Even a no-op spends 9.5 compiler-seconds: some project compiles on every build. This was not chased.

Notes:

- **Restore never hits the network for packages.** `NUGET_PACKAGES=/home/app/.nuget/packages` is a host bind mount: 3.9 GB, 236 packages, shared by every worktree in the container. Each of the 40 project restores took 5 ms to 1.8 s.
- **The only network traffic is the NuGet audit index.** Restore fetched the nuget.org service and vulnerability index files (`~/.local/share/NuGet/http-cache/*/vuln_*.dat`, refreshed 17:19Z). Restore is a few seconds of a cold build, not the cost.
- **A new OutputPath recompiles almost everything (c).** Every project that has a ProjectReference sees new reference paths, so `CoreCompileInputs.cache` changes. Alternating output names on one obj thrashes: each switch recompiles. Both output trees are 720 MB.
- **A test-only edit (d1) still costs 59% of cold.** `Antiphon.Tests` alone is roughly half of the graph's compile: 795 of 1,365 compiler-seconds. Code tasks almost always touch tests.
- **A low-level public API change (d2) costs about as much as a cold build.** Reference assemblies change, so every dependent recompiles.
- **A copied obj at a different absolute path is useless (e2a).** `project.assets.json`, `*.nuget.dgspec.json` and `project.nuget.cache` embed the project's absolute path: 29 occurrences in `tests/Antiphon.Tests/obj/project.assets.json`. All 40 projects re-restored, the assets files were rewritten newer than the outputs, and everything recompiled. The test favoured the seed: the target sources were older than the copied outputs. Real seeding, with sources checked out after the cache, would also lose on timestamps. That variant (e2b) was not run.
- **Worktree creation is cheap.** `git worktree add --detach` took **2.8 s** (0.96 s user) with 260 worktrees registered in `/work/repos/antiphon`. A `--shared` clone plus checkout took 3.8 s. A worktree is 201 MB, with 9,290 tracked files.

## 2. What the current recipe costs (last 24 h)

Source: the 164 `report.json` files under `/work/worktrees/task-*/.antiphon/checkpoints/*/` written between 2026-10-30T17:38Z and 2026-10-31T17:07Z. They hold 603 build entries: 163 ok, 10 failed, 423 unused and 7 skipped. The Markdown reports on the 71 origin task branches touched in the window carry row lines only: `build=ok` 123 times and `build=reused` 426 times, where reused means within one run. They hold no build timings, so `report.json` is the timing source.

| Builds that ran (ok + failed) | n | p50 s | p95 s | max s | build hours | slot wait p50 / p95 s | wait hours |
|---|---:|---:|---:|---:|---:|---:|---:|
| all | 173 | 264 | 509 | 804 | **11.98** | 0 / 495 | 3.78 |
| tests/Antiphon.Tests | 117 | 326 | 579 | 804 | 10.99 | 0 / 315 | 1.75 |
| tests/Antiphon.Agents.Pty.Tests | 37 | 53 | 111 | 153 | 0.60 | 15 / 825 | 1.58 |
| tests/Antiphon.SessionRunner.Tests | 11 | 70 | 135 | 135 | 0.24 | 15 / 270 | 0.27 |
| tests/Antiphon.PtyHost.Tests | 8 | 62 | 118 | 118 | 0.15 | 30 / 390 | 0.17 |
| first build in that worktree | 51 | 290 | 509 | 599 | 4.09 | | |
| later build in the same worktree | 122 | 251 | 563 | 804 | 7.89 | | |

By relation to the previous build of the same project in the same worktree. Ordering by start time puts 171 builds in classes; two tie.

| Class | n | of which Antiphon.Tests | build hours | Cost if warm (from §1) |
|---|---:|---:|---:|---|
| first build in a fresh worktree | 50 | 38 | 3.82 | cold either way, unless the cache has the identical path |
| first build of another project in that worktree | 21 | 10 | 1.26 | partly warm: shared references |
| **same commit, same project** (a re-run) | 32 | 18 | 1.98 | no-op, b: 4% |
| new commit, same project | 68 | 50 | 4.82 | incremental, d1 to d2: 59 to 94% |

**CPU.** At e1's 1,579 CPU-s per cold Antiphon.Tests build, the 117 Antiphon.Tests builds cost about **51 CPU-h**. The other 56 builds, scaled by build-seconds, cost about 5 CPU-h. That is about **56 CPU-h/day**, 2.3 cores averaged over the day.

**How much was avoidable** with warm state at a stable path:

- **Same-commit re-runs:** 18 × (1,579 − 57) plus the rest is about **8 CPU-h**, and 1.98 h becomes about 0.2 h of build time.
- **New-commit builds:** 50 × (1,579 − 930 … 1,487) is **1.3 to 9 CPU-h**, and 0 to 1.6 build-hours.
- **First builds in a worktree:** 0 with copy-seeding (e2a).

The total is **9 to 17 CPU-h of 56 (16 to 30%) and 2 to 3.5 of 12 build-hours.** Against capacity, 4 slots × 24 h = 96 slot-hours, so that is 2 to 4% of slot capacity.

**Not measurable:** direct `build-slot.ps1 -- dotnet build` runs and the in-lease loops. The broker keeps no history (`/build-slots/metrics` is HTTP 405, per 32df662c), and delegates delete their `bin-<name>/` afterwards. Profile 32df662c attributed most of the shared VBCSCompiler's 9.3 cores in its window to two loops of repeated builds under one lease. Those loops reuse obj and output, so they are incremental already. Each of their iterations is a d1/d2-class build.

## 3. Worktree creation cost and what retirement leaves

| Measure (2026-10-31 ~18:10Z) | Value |
|---|---:|
| Registered worktrees in /work/repos/antiphon | 260 (0 prunable; 250 under /work/worktrees, the rest under /tmp scratch) |
| `/work/worktrees/task-*` directories | 250: 125 created < 1 day ago, 125 created 1 to 2 days ago, none older |
| Total size | 87.5 GiB: per worktree p50 0.22 GiB, p95 0.91 GiB, max 1.87 GiB |
| obj/ | 15.7 GiB (18%) |
| bin/ and bin-*/ | 14.9 GiB (17%) |
| .antiphon/ (checkpoint runs, shadow copies) | 9.9 GiB (11%) |
| node_modules | 2.0 GiB |
| checkouts and other | ~45 GiB (≈ 250 × 0.2 GiB) |
| worktrees with < 100 MB of build output | 141 of 250 |
| /work filesystem | 341 of 705 GB used; worktrees are 26% of that |

Retirement works on a roughly two-day horizon (`TaskWorktreeRetirementService`, `WorktreeResidueSweepService`). What it leaves is about 87 GiB, and 35% of that is obj/bin, or 46% counting `.antiphon/`. On server2 that is a disk-hygiene question, not a build-speed one. The desktop's past 1,023 worktrees were not examined: no desktop files were read.

## 4. Existing incremental and output-reuse support

- **`scripts/build-slot.ps1` / `scripts/lib/build-slot.ps1`:** no output handling at all. They pass the command through verbatim, adding `-maxcpucount:N` only when argv[0] is dotnet (`scripts/lib/build-slot.ps1:186-195`). They never delete obj or bin.
- **`tools/Antiphon.Checkpoints`:** reuse exists only **within one run**. Rows share a build (`build=reused`). Across runs:
  - The owned layout is always fresh (`BuildStep.cs:42-50`, `BuildConfinement.cs:81-88`).
  - A pre-existing output or stamp refuses (`SourceRunGuard.cs:113-118`).
  - Cleanup deletes the outputs it created (`Cleanup/OutputCleanup.cs`).
  - The build-source stamp (`SourceRunGuard`: commit, fingerprint, source state, properties, output path) already holds what a cross-run reuse check would need.
- **`scripts/run-checkpoint.ps1`:** the same layout (lines 697-701, 841-842, 1165-1171). It keeps outputs, but each row names a fresh `bin-x/`.
- **Land verification** builds in a fresh verification worktree: `LocalVerificationWorkspace.cs:25`, and `RunnerVerificationWorkspace.cs:76` on the runner. It is independent of task worktrees.
- **A known hazard:** `docs/testing-and-build.md:144` (CARD-0412 D4). Restoring a file with an old mtime lets an incremental build reuse a mutated DLL. This is the concrete way a warm obj can make a receipt prove the wrong binary.

## 5. Options

Savings below come from the §1 and §2 numbers. "Receipt" means a checkpoint `report.json` and its CHECKPOINT lines.

| Option | Expected saving | Custody / isolation risk | Verdict |
|---|---|---|---|
| **A** Keep per-task worktrees; seed obj/bin from a per-base warm cache, share the package cache, demand fresh outputs only for receipt-producing runs | **Seeding by copy: 0** (e2a re-restores and recompiles all 40 projects because of absolute paths and timestamps). Package cache: already shared, saves nothing more. Dropping fresh output names for **non-receipt** direct builds: each repeat build goes from c (97%) to b/d1 (4 to 59%). Count unmeasured. | Low for direct builds: they prove nothing. For receipts, an incremental output can carry a stale binary (`testing-and-build.md:144`). The build-source stamp records inputs, not that the compiler ran. | The **non-seeding half is the first step**. Seeding does not work as designed. |
| **A′** Checkpoint tool: across runs in the same worktree, reuse its own verified build when commit, fingerprint, properties and output all match (stamp already exists) | 32 builds a day: 1.98 → ~0.2 build-h, about 8 CPU-h. Keeping obj per worktree for new-commit builds adds 1.3 to 9 CPU-h. | Same-commit reuse proves exactly what the first build proved: the stamp binds the commit and a clean tree. New-commit incremental needs the same trust in MSBuild incrementality, and land verification stays cold and independent. | **Recommended** after the first step. |
| **B** A small pool of persistent warm worktrees per runner slot; tasks switch or reset to their StartRef | Upper bound ≈ A′ plus the first build: about 5 more build-h, if successive tasks are close commits. Cross-branch switches are often d2-class (94% of cold). | **High.** It breaks the worktree-per-task custody that the code assumes: a mirror is created per task with `worktree add -B <branch>` (`RunnerWorkspaceService.cs:176,308`) and removed per task (`:577`). Retirement, workspace bindings and creation identity (CARD-1032), tamper and ledger baselines of ignored and untracked paths, and settlement of a task's own footprint all assume this. Dirty or ignored state leaks between tasks. One branch cannot be checked out in two worktrees. The "delete nothing outside your scratch" rule fails. A parked task resumed from its pushed branch needs its tree back. | Not now. |
| **C** Shared build output: a remote or seeded artifacts cache, or a stable OutputPath per slot | A stable path per slot is B under another name: incremental needs identical absolute paths (e2a). MSBuild has no content-addressed remote cache. | A read-only seeded artifacts folder carries A's staleness risk without A's simplicity. | Not worth it here. |
| **D** Do nothing; only cap parallelism | 0 build-h. The queue is set by long leases (32df662c: multi-build loops held slots 14 to 38+ min). | None | Do it alongside the first step: one lease per loop iteration matters more for the queue than anything in this table. |

**Does a warm obj change what a land or a receipt proves?**

- **A land proves the same thing whatever the task worktrees do.** It verifies from its own fresh verification worktree, and none of the options touch that, so land proof is unaffected.
- **A same-commit reused receipt** (A′, first half) proves the same thing as the original build.
- **An incremental receipt** (new commit on a warm obj) adds a dependency on MSBuild's up-to-date checks. A wrong check, as with mtime-preserving restores, would test a stale DLL.
- **The builds are not bit-reproducible** (no `ContinuousIntegrationBuild`/`Deterministic` path-map guarantee was checked here). So "same commit" reuse rests on the stamp, not on output hashes.

**Smallest safe first step:**

1. Stop the orchestrator and delegate instruction to use a FRESH output directory name for every non-receipt build. One stable `bin-<task>/` per worktree turns a 97%-of-cold rebuild into a 4 to 59% one.
2. Keep checkpoint receipts and land verification cold until A′ is designed and reviewed.

## Card-ready lines

- **Brief and docs:** direct (non-receipt) gated builds reuse one stable `bin-<task>/` per worktree. A fresh name recompiles 97% of the graph (1,532 vs 1,579 CPU-s cold) and thrashes the shared obj.
- **Checkpoint tool:** across runs in the same worktree, reuse a verified build whose stamp matches commit, fingerprint, properties and project: 32 builds a day, 1.98 build-h, about 8 CPU-h. Keep the fresh layout whenever any field differs.
- **Checkpoint tool (later, needs design):** optionally keep `artifacts/obj` per worktree for new-commit rebuilds: 68 builds a day, 1.3 to 9 CPU-h. Include the mtime-restore hazard (CARD-0412 D4) in the design.
- **Slots:** charge loop builds one lease per iteration (32df662c). That, not cold builds, sets the 10 to 25 min queue.
- **Do not** build a copy-seeded obj cache: it was measured to fully re-restore and recompile at a different absolute path (e2a).
- **Investigate (small):** which project recompiles on a no-op build (9.5 compiler-seconds every time).

## Remaining uncertainties

- The CPU figures are single samples on a loaded shared host, and wall times varied two-fold with load (a: 406 s, e1: 170 s). CPU-seconds are the stable measure.
- Antiphon.SessionRunner.Tests cold and warm were not measured separately. The receipts give its build at p50 70 s.
- Direct gated builds and loop builds have no stored history, so their daily count and share of VBCSCompiler CPU are not measured here.
- e2b (seeded obj with sources newer than the cache) was not run. e2a already invalidates on paths alone.
- The desktop was not examined.

## Not done, noted

Fix idea, one line: stable per-worktree output name for non-receipt builds, plus a stamp-matched cross-run build reuse in `tools/Antiphon.Checkpoints`.
