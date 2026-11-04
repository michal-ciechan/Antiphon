# rc/2026-11-05 run 766e3df5 group 77: C0824 S4a policy tests time out on Windows

Investigate task 36b3dc05, 2026-11-04. Base master `d4c88ddde554ae9f01fad65f27bc893175271b51`; the failing
cut is rc/2026-11-05 `1470340559889c2985ea722173181ea25c51b47c`.

## Verdict

**SLOW, not STUCK.** The failure set is fixed by how many git processes each case spawns. Every case in the
measured set that starts **≥ 336 git processes** on the cut's code path failed, apart from the two noted below.
Every case that starts **≤ 326** passed. No step waits on a Windows-only blocker:

- the guarded removal has no wait, retry or delay loop;
- libgit2 is not in the cut at all.

Master already halves the per-case process count. Even so, master's own numbers are **not Windows-safe**:

- `First_enable_on_a_large_backlog_is_bounded(execute)` is estimated at about 88-98 s on Windows, against a
  90 s timeout and a 35 s tick assertion;
- `Tick_is_bounded(count)` is estimated at about 355 s, against 180 s.

S4a is a desktop (Windows) feature, so these cases must keep running on Windows. A NativeLinuxOnly exclusion
would be wrong.

## Evidence limits (read first)

- **The Windows TRX and timing root were not readable from this runner.** These are
  `C:\Antiphon\releases\candidates\rc-2026-11-05-147034055988\logs\2026-11-04-1018-766e3df5\antiphon-all-group-77.trx`
  and `...\logs\timing-766e3df59d8347abb6259d3013f1ccd8`.
  - `GET /api/filesystem/browse` lists directories only.
  - `GET /api/agents/{id}/files/content` serves workspace-relative paths, or absolute paths only when they are in
    the agent's file listing (`server/Application/Services/AgentFilesService.cs:392-400`). It returned 404 for
    the TRX path.
  - No agent has a working directory under `C:\Antiphon`.
- So the Windows per-case durations of the **passing** cases are not in this report. The Windows facts used are
  the ones the brief gives:
  - the 9 failing cases;
  - their timeouts: 45 s class default → 46 s, and 180 s → 3m01s;
  - every other case in the class presumably passed (unverified).
- The Windows cost per process (k) below comes from that pass/fail boundary. It is not a direct measurement.
  The TRX's passing-case durations would refine it; see "What would tighten the numbers".

## Measurements (server2, Linux, master d4c88ddde, serial `--maximum-parallel-tests 1`)

Four runs, all green. Each was built once in the worktree's native output with `UseAppHost=false`, and each run
held one build-slot lease.

| Run | Selection | Mode | Result |
|---|---|---|---|
| 1 | The 9 test methods that contain the failing cases and their passing siblings | — | 28/28 green |
| 2 | Same as run 1 | `ANTIPHON_GIT_INPROCESS=off`, which is the cut's spawning, because the cut has no libgit2 reads | 28/28 green in 1m44s |
| 3 | Same as run 1 | Master default, libgit2 in-process reads on | 28/28 green |
| 4 | Whole class | Master default | 120/120 green in 4m18s |

Per-case spans came from `ANTIPHON_TEST_PHASE_ROOT` (process-spawn and process-wait), and each git process
(builtins plus `run_command` children) was counted with `GIT_TRACE`.

### Process count per case: cut-equivalent vs master, with the Windows outcome

"git procs" counts each `git` builtin plus its `run_command` children (`upload-pack`, `receive-pack`,
`pack-objects`, …) inside the case window. Linux ms is from run 3. The Windows outcome is from the brief.

| Case | git procs, cut path (run 2) | git procs, master (run 3) | Linux ms (master) | Windows (cut) |
|---|---:|---:|---:|---|
| Eligible_terminal(Failed, base-only) | 218 | 114 | 601 | pass |
| Eligible_terminal(*, pushed) ×3 | 234 | 129-130 | 846-943 | pass |
| Age_floor(47h59m / null) | 261 | 152-153 | 877-941 | pass |
| Leased_runner_slot(leased) | 261 | 152 | 958 | pass |
| Dirty_or_unpushed ×6 | 266-279 | 154-163 | 805-1071 | pass |
| Removal_re_proves(origin-rewound) | **326** | 191 | 1092 | **pass** |
| Ignored_content(protected) | **336** | 183 | 971 | **FAIL 46 s** |
| Removal_re_proves(commit-after) | **336** | 183 | 962 | **FAIL 46 s** |
| Double_run(sequential) | 396 | 212 | 1600 | pass |
| Age_floor(48h) | 398 | 213 | 1263 | **FAIL 46 s** |
| Ignored_content(evidence-allowlisted) | 398 | 213 | 1230 | **FAIL 46 s** |
| Leased_runner_slot(released) | 398 | 214 | 2392 | **FAIL 46 s** |
| Double_run(residue-sweep-first) | 422 | 220 | 2046 | **FAIL 46 s** |
| Tick_is_bounded(time) | 434 | 249 | 1784 | pass (180 s cap) |
| Removal_re_proves(dirty-after) | 438 | 230 | 1311 | **FAIL 46 s** |
| Double_run(concurrent) | 443 | 232 | 1436 | pass |
| Tick_is_bounded(count) | 4932 | 2536 | 15881 | **FAIL 3m01s (180 s)** |
| First_enable(execute), master body cap 5 | 1206 | 627 | 9985 | **FAIL 3m01s**: the cut body is cap 25, so about 4× the master figure |

How to read it:

- **Every passing case is lighter than every failing 45 s case, with two exceptions.** Both exceptions sit near
  the boundary:
  - `Double_run(sequential)`, 396 processes, at 0.5% below `Age_floor(48h)`'s 398;
  - `Double_run(concurrent)`, 443 processes, whose two ticks run in parallel, so its wall time is shorter than
    its process count implies.
- A STUCK step shared by every removal would also fail the one-removal cases (Eligible_terminal, Age_floor
  47h59m, Dirty_or_unpushed, Leased(leased)). These all do a real guarded removal of the control tree, and they
  passed.
- A hang tied to the second removal is ruled out because `Double_run(sequential)` passed. It runs two removals
  in one tick. `Ignored_content(protected)` failed, and it runs one removal plus one refusal.
- **What a removal costs.** Cut path: `Age_floor(48h)` minus `Age_floor(47h59m)` = **137 git processes per
  guarded removal**. The breakdown for one removal is:
  - 49 rev-parse, 17 show-ref, 13 symbolic-ref, 11 worktree list;
  - 8 upload-pack and 7 remote get-url;
  - 5 ls-remote, 3 fetch;
  - plus status, ls-files, rev-list and merge-base.

  On master, the libgit2 in-process reads (`40c2ab63c`/`4a4945b6c`) answer rev-parse, show-ref, symbolic-ref
  and merge-base. That leaves **61 processes per removal** (213 − 152).

### The Windows cost per process

Here k is the time per git process on Windows under qualification load, assuming time grows in step with the
process count.

| Bound | Case | Calculation | Result |
|---|---|---|---|
| Lower | `Removal_re_proves(commit-after)` failed | 45 s / 336 | k ≥ 134 ms |
| Lower | `Age_floor(48h)` failed | 45 s / 398 | k ≥ 113 ms |
| Upper | `Removal_re_proves(origin-rewound)` passed | 45 s / 326 | k ≤ 138 ms |
| Upper | `Double_run(sequential)` passed | 45 s / 396 | k ≤ 114 ms |

- The two upper and lower bounds cross. That puts the variance between cases at about ±15%, centred on
  **k ≈ 115-140 ms**.
- For comparison, serial Linux is about 4 ms per process (`Age_floor(48h)`: 1635 ms / 398). That is roughly 30×
  less. The Windows run was loaded: the git lane is 4 wide, and other chunks ran at the same time.
- An earlier different-source comparison (CARD-1514, `2026-10-22-card-1514-slow-test-profile.md`) found Linux
  4-11× faster on similar git-heavy classes.
- `Tick_is_bounded(count)`: 4932 processes × 113-140 ms = 557-690 s, consistent with timing out at 180 s.

The estimates below use **k = 140 ms**, the top of the band.

### Master on Windows: estimates (k = 140 ms, whole-class run 4)

| Case (master) | git procs | Est. Windows | Current timeout | Safe? |
|---|---:|---:|---:|---|
| Heaviest case under the class default: Double_run(concurrent) | 233 | 33 s | 45 s | p95 ≈ 1.2 × 33 = 40 s, so not 2× |
| Failures_back_off(backoff-doubles) | 485 | 68 s | 180 s | yes |
| Failures_back_off(exhausted / retry-succeeds / lease-busy) | 318-345 | 45-48 s | 180 s | yes |
| Held_backlog_is_bounded_per_tick… | 292 | 41 s git, plus a non-git share. Linux takes 53.7 s serial with almost no git, so the Windows DB share is unknown | 180 s | **unverified**. It passed on the cut run; read its TRX duration |
| First_enable(execute), whole test | 627 | 88 s, plus about 5 s DB = **93-98 s** | 90 s | **no** |
| First_enable(execute), tick-1 alone | 305 (measured in the tick-1 span) | 43 s, plus 3-7 s DB = **46-50 s** | 35 s assertion (`BacklogTickBudget`) | **no** |
| Tick_is_bounded(count) | 2536 | **355 s** | 180 s | **no** |
| Tick_is_bounded(time) | 249 | 35 s | 180 s | yes |

The master change (cap 5, 90 s timeout, 35 s tick budget, commit `44ba90c9c`) was calibrated on server2 only. The
first Windows run of the next cut will fail `First_enable(execute)` on the 35 s tick assertion and then on its
90 s timeout. `Tick_is_bounded(count)` will fail as before.

## Product side: no Windows-specific blocker

- `GuardedWorktreeRemoval.RemoveDirectoryAsync` at the cut (`server/Infrastructure/Git/GuardedWorktreeRemoval.cs`,
  read at 1470340559) has no wait, poll, retry or delay.
  - A handle inside the tree makes `WorktreeNoFollowDelete.MoveAside` throw. That is caught at once as
    `worktree_remove_failed` (cut file lines 153-162).
  - A failed delete is caught at once as `worktree_removal_incomplete` (lines 182-190).
  - Either outcome fails a test on an assertion, not by a timeout.
- The only time limit is the per-git-command budget of 5 min (`LandingGit.DefaultBudget`,
  `server/Infrastructure/Git/LandingGit.cs:16`). A single hung git would be one process stuck for minutes, and
  the pattern by process count above rules that out as the explanation.
- `DesktopLegacyReleasePolicyService` waits only for `PauseBetweenSeconds`. The test world sets it to 0
  (`tests/Antiphon.Tests/Application/DesktopLegacyWorld.cs:30`), and the fake clock drives it.
- **libgit2 is not in the cut.** `40c2ab63c` (in-process reads) and `4a4945b6c` (reads per call) are not
  ancestors of 1470340559 (`git merge-base --is-ancestor`). The held libgit2 handle of 4a4945b6c's message
  therefore cannot be behind this run.
  - On master, `InProcessGitReads.TryAnswer` opens `using var repo = new Repository(repository)` for each call
    (`server/Infrastructure/Git/InProcessGitReads.cs:49`), and it is disposed before return. No handle outlives
    the call.
  - The test fixture's own readers are `FixtureGit` process runs, not libgit2.
- The S4a removal is slow on Windows because the guarded re-proof is expensive in processes. On master, one
  release costs about 61 git processes, roughly 8 s on the loaded desktop. The product's own bounds absorb this:
  `MaxReleasesPerTick` 25 and `MaxTickMinutes` 20 (`server/Application/Settings/WorktreeLegacyReleaseSettings.cs:26-30`).

## Recommendation (exact changes for the Code task)

Test-only, in `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs` on master. Timeouts are
about 2× the estimated Windows p95, with p95 = 1.2 × the k = 140 ms estimate.

1. **Class default** `[Timeout(45_000)]` → **`[Timeout(90_000)]`**.
   - On master the heaviest class-default case is Double_run(concurrent): 233 processes, est. 33 s, p95 40 s.
   - On the cut it would be about 62 s, which also covers a cut whose libgit2 reads are off
     (`ANTIPHON_GIT_INPROCESS=off`).
2. **`Tick_is_bounded(count)`: cut the work, keep the proof.** Do what V-25 did: run with
   `s => { s.MaxTickMinutes = 20; s.MaxReleasesPerTick = 3; }` and **5 trees, not 30**.
   - Tick 1 retires the 3 oldest; the 2 newest stay untouched with `AssertNoGit`; tick 2 retires those 2.
   - The order and the bound are still proven (oldest first, the cap binds, the rest untouched).
   - Est.: 5 × 84.6 processes per tree (2536/30 measured) + about 46 for the world ≈ 470 processes ≈ 66 s on
     Windows, p95 79 s.
   - **Keep `[Timeout(180_000)]`.** The literals 25/5 in the assertions become the cap and count − cap.
   - Do not instead raise the timeout to the roughly 710-850 s that 30 trees would need.
3. **`First_enable_on_a_large_backlog_is_bounded`**: keep `BacklogCap = 5`.
   - Make `BacklogTickBudget` platform-aware: **35 s on Linux** (the measured basis) and **100 s on Windows**
     (2 × the 50 s estimate for tick 1).
   - Raise the method timeout `[Timeout(90_000)]` → **`[Timeout(240_000)]`** (whole test est. 98 s, p95 118 s).
   - If one budget for both platforms is preferred, lower `BacklogCap` to 3 instead:
     - tick 1 ≈ 3 × 61 + 30 = 213 processes ≈ 30 s + DB, so a 75 s budget;
     - whole test ≈ 460 processes ≈ 70 s, so `[Timeout(150_000)]`.
4. Leave `Failures_back_off…`, `Refused_filing…` and `Filed_release_resets…` at 180 s (est. ≤ 68 s).
   - **Read `Held_backlog_is_bounded_per_tick…`'s Windows duration from the group-77 TRX before deciding.** It
     costs 53.7 s on serial Linux for reasons other than git, and nothing here measures it on Windows.
5. Add no NativeLinuxOnly exclusion or policy row. S4a is the desktop (Windows) release, and these cases are its
   only real-git proof on Windows.

Port to the next rc cut with the batch that carries `44ba90c9c`/`a10c2d898`. The cut has neither.

## What would tighten the numbers

- From the group-77 TRX (or `timing-766e3df5…/timing/tests.csv`), the Windows durations of the **passing**
  C0824DesktopLegacyReleasePolicyTests cases. Fitting k = duration / process count from the table above would
  replace the 115-140 ms band with a measured p95.
- From the same timing root, the chunk's process-wait spans (phase JSONL). They would confirm that a failing
  case's time is spread across many short git waits and not concentrated in one long wait.

The caller on the desktop (or the Code task once the run ends) can read both read-only. Neither is needed to
choose the changes above.

## Not done, noted

- No fix implemented (Investigate).
- A product idea, not part of this card: the guarded removal's 49 rev-parse/17 show-ref per removal on the cut
  (61 processes remain on master) suggests a batched re-proof would make each desktop release about 2× cheaper.
