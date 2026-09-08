# CARD-0448 Code rounds: is the delegate's own workflow serialized?

Investigate, 2026-09-08. Read-only observation of the live Code task `4692ec26` and its
predecessors `3996eca6`, `347a3d88`, `c86499fb`. No file in the live worktrees was touched.

## Verdict

**Mostly no.** The agent (Codex `gpt-5.6-sol`) already backgrounds long runs and interleaves
reading/patching/committing against them. Its wall-clock is dominated by an **external,
structural** constraint, not by turn-taking: 50 red-then-green PC controls, each of which must
mutate and revert the same source file in a single working tree, run against test classes
carrying `[ParallelLimiter<ProcessSpawnLimit>]` whose individual tests take **20 s - 2 m 50 s**.

There is **one real, agent-side regression**: round 4 (`3996eca6`) spent **52 % of its wall
clock in poll-only tool calls** — 36 of 92 calls did nothing but tail a log — including a
25-minute stretch where 13 of 19 calls were pure polling. Rounds 3 and 5 do not show this.

## Evidence

### Source of the data

The Antiphon session transcript for a Codex agent records only `AssistantText` (7 entries for
`4692ec26`, no tool rows), so tool-call timing came from the Codex rollouts:
`~/.codex/sessions/2026/09/08/rollout-<local-ts>-<uuid>.jsonl`. Rollout uuid prefixes match the
transcript entry uuids (`01a0827c…` matches `4692ec26`'s prompt uuid), and the local-time
filenames map onto `dispatchedAt` + 1 h (BST):

| Task | Status | Rollout | Calls | Span |
|---|---|---|---|---|
| `c86499fb` round 1 | Failed | `12-30-00-01a080c8` | 121 | 68 min |
| `347a3d88` round 3 | Failed (4 h ceiling) | `15-16-01-01a08160` | 424 | 305 min |
| `3996eca6` round 4 | Succeeded | `19-16-38-01a0823c` | 92 | 67 min |
| `4692ec26` round 5 | Dispatched (live) | `20-26-11-01a0827c` | 38 | 14 min at time of read |

### (1) The agent does overlap work with long runs

`exec_command` yields after `yield_time_ms:1000` and returns a `session_id`; the agent then
polls with `write_stdin` while issuing other commands in the *same* tool call. Concrete
overlaps in the live round 5:

- `19:30:49` starts the fixture suite (`dotnet run --no-build … LandingAdmissionControlTests |
  LandingSourceBoundaryControlTests | RepositoryMutationLeaseTests` into `c4-fixtures-01.log`).
- `19:31:50` — while that is still executing — starts `dotnet build` of a **second checkout**,
  `C:/Antiphon/worktrees/c448-4692ec26-baseline`, to `bin-c448-baseline/`.
- `19:39:41` — with the fixture run still going — starts `npm ci` for the client suite.
- Between those, `19:32:14`-`19:38:11` it reads `AgentTaskDispatcher.cs`,
  `GuardedWorktreeRemoval.cs`, greps removal APIs, rewrites the control-case manifest, and at
  `19:33:09` commits (`1be12848`).

Three concurrent lanes, all launched by the agent unprompted. Multi-command tool calls: 35 % of
round 5's calls, 21 % of round 4's, 8 % of round 3's.

### (2) The measurable exception: round 4 idled on a single log

Classifying each call as poll-only (log tail / `Get-Process` / `Get-Date` only, no source read,
patch, commit or build):

| Round | poll-only calls | poll-only wall clock | longest streak |
|---|---|---|---|
| 1 `c86499fb` | 26/121 (21 %) | 25 of 68 min (36 %) | 9 |
| 3 `347a3d88` | 25/424 (6 %) | 26 of 305 min (9 %) | 5 |
| 4 `3996eca6` | **36/92 (39 %)** | **35 of 67 min (52 %)** | 7 |
| 5 `4692ec26` | 7/38 (18 %) | 3 of 14 min (23 %) | 2 |

Round 4, `18:58:17`-`19:20:48`: 19 consecutive calls, 13 of them literally the same command —
a `Select-String` over `.antiphon/continuation3-controls/continuation3-restored-regression.log`
for `^(passed|failed) `, taking the last 4-8 lines. `19:12:45` to `19:20:48` is five calls,
eight minutes, 100 % polling. The six productive calls in that window were report-writing and
an archive script, not the next defect.

### (3) Why the runs are long — external, not agent-side

- The landing test classes carry the 1-wide lane. On `origin/feat/card-task-c86499fb`,
  `AgentTaskLandPreparationIdentityTests` is declared `[Category("Integration")]` plus
  `[ParallelLimiter<ProcessSpawnLimit>]`; `docs/testing-and-build.md:10` states the lane admits
  at most one test at a time per assembly.
- Individual durations harvested from the rollouts' own run output (38 distinct tests,
  **1 708 s** total): `C448_V15_…OpenAFreshExplicitOperation(verification-f…)` 2 m 50 s,
  `C448_V14_shared_land_first(Fresh)` 2 m 27 s, `C448_V17_MissingRebaseResult…` 2 m 07 s, ten
  more over 20 s. Each drives real `git.exe` against disposable repos plus
  `TestDbFixture.CreateIsolatedSchemaAsync()`.
- The PC protocol is serial **by instruction**. `server/Bundles/stage-code.md`: *"Run each PC-n
  as red-then-green (break, see red, revert, see green)"*. The plan
  (`docs/superpowers/plans/2026-09-08-card-0448-land-publication-safety-plan.md`) has **50 PC
  rows** plus 36 V, 19 R, 24 C, 7 F — 136 items. The agent's own driver,
  `.antiphon/task-3996eca6-evidence/run-continuation3-intent-control.py`, implements it with
  `subprocess.run` in a `try/finally` that rewrites and restores
  `server/Application/Services/AgentTaskLandingProtocol.cs`. Two PCs cannot run concurrently in
  one working tree: they mutate the same file. 50 PCs x 2 runs x 1-3 min is roughly 2-5 h of
  unavoidable serial execution in a single checkout. That is what five Code rounds bought.

### (4) The parallelism that *was* available and not taken

`ProcessSpawnLimit` is assembly-local *per process* (`docs/testing-and-build.md:34`, CARD-0050
S5), and the DB schema is per-test-isolated, so two `dotnet run` invocations of
`Antiphon.Tests` under different `OutputPath`s are not blocked by it — the documented
co-scheduling ban is specifically `Antiphon.Tests` vs `Antiphon.Agents.Pty.Tests` (FakeClaude
rotation), and none of the CARD-0448 landing tests use FakeClaude. Round 3's 38-test set could
plausibly have been split into 2-3 concurrent runs; the PC red/green cycles could have been
sharded across extra worktrees — the agent proved it can create one
(`c448-4692ec26-baseline`) but only ever used it for a baseline build, never to fan PCs out.

Never observed in any of the four rounds: two `dotnet run` test invocations of the same suite
deliberately started to halve wall clock. The only concurrency is *heterogeneous* (test plus
build, test plus npm), never *homogeneous*.

### (5) What the standing instructions say

`server/Bundles/delegate-basics.md`, first rule, verbatim:

> RUN EVERY COMMAND IN THE FOREGROUND AND WAIT for it. Never background a run and end your turn.

The stated rationale is narrow (a backgrounded run at settlement reports nothing), but the
imperative as written is blanket. Codex's `exec_command`/`write_stdin` pattern technically
violates its letter every time it is used well. Nothing in `delegate-basics.md`,
`stage-code.md`, or the round-5 brief (`.antiphon/task-4692ec26-brief.md`) mentions filling a
wait with other work, sharding a suite, or using a second worktree for parallel controls.

`delegate-basics.md` also states `Antiphon.Tests` is **~12 minutes**;
`docs/testing-and-build.md:58` measures **~25.5 minutes** (CARD-0110, 2026-09-03). The
delegate-facing number is half the real one.

## Remaining uncertainties

- Round 2 (`1h44m`) was not located; only four rollouts were matched.
- Whether two concurrent `Antiphon.Tests` processes are *actually* safe for the landing suite is
  reasoned from the isolation primitives (`CreateIsolatedSchemaAsync`, per-fixture temp git
  roots) and the scope of the documented ban — it was not measured, and measuring it would need
  a write-capable task.
- Round 5 was still `Dispatched` at 14 minutes in; its poll-only figure is a partial sample.

## Not done, noted

Fix ideas, not designs: (a) reword `delegate-basics.md` rule 1 to "never *end your turn* with a
run in the background — while one runs, keep reading/patching the next item"; (b) add a
Code-role line permitting a second worktree to shard red-then-green PC controls when a plan has
more than about ten of them; (c) correct the `~12 minutes` figure to ~25.5 min.
