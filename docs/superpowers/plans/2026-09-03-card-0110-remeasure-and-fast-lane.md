# CARD-0110 — re-measure after S2 + CARD-0238, and the next slice

**Date**: 2026-09-03 · **Author**: Plan task fe7498e3 (Frontier) · **Status**: measurement +
revised slice plan. No test/production code changed by this task.
**Supersedes the timing basis of**: `2026-08-21-card-0110-test-suite-speed-plan.md` (its per-test
CSV is now stale — see §2) and the tail-phase framing in
`2026-08-29-card-0165-antiphon-tests-stall-investigation.md` §4 (pre-S2 — see §3).

## 0. Why re-measure

The card's own notes flagged that CARD-0238 (Postgres testcontainer connection exhaustion) made
every prior timing measurement on this card unreliable — some of the "~28 minutes" was retries and
contention from the exhaustion, not real work. CARD-0238 is now **fixed and closed (2026-09-03)**,
and S2 (migrate-once DB template, `a9112991`, 2026-08-30) landed after the last full re-measure.
Both confounds are gone. This document re-measures on clean master and redraws the slice plan on
the fresh numbers. Every figure below was taken on **this worktree, 2026-09-03**, built to an
alternate `OutputPath` with the AppHost + session-runner daemons running as usual and no concurrent
test runs.

## 1. The fresh full-suite number

A full watched run on this worktree today (the CARD-0238 verification run,
`2026-09-03-card-0238-postgres-testcontainer-verification.md`) completed in **25m 27s**:

| Total | Passed | Failed | Skipped | Max Postgres conns |
|---:|---:|---:|---:|---:|
| 3,893 | 3,872 | 16 | 5 | 42 / 100 |

Zero `53300` / `53200` / "too many clients" / "out of shared memory" errors — the exhaustion the
card warned about is gone, and with it the measurement noise. **The suite is ~25.5 min, not ~28**,
and the number is now trustworthy. It is still far over a 10-minute foreground window, so the card's
core problem stands.

## 2. The 2026-08-21 breakdown is now stale by 3-25x for anything DB-touching

Re-timing the classes that plan named as the slow tail, each run alone via `--treenode-filter`
(the ~22 s floor in each row is the assembly's lazy Postgres-container + template-migration
`[Before(Assembly)]`, paid once per exe invocation — see §5):

| Class (plan bucket) | Tests | Wall today | ~Exec | Plan 2026-08-21 | Verdict |
|---|---:|---:|---:|---|---|
| GitDiffSpikeTests (S3 git) | 5 | 0:27 | ~5 s | **124 s** | collapsed ~25x |
| GitServiceTests (S3 git) | 12 | 0:24 | ~2 s | **116 s** | collapsed |
| WorktreeManagerGitIntegrationTests (S3 git) | 19 | 0:37 | ~15 s | **111 s** | collapsed |
| DelegationWorktreeTests (S3 git) | 25 | 1:09 | ~47 s | **111 s** | ~halved |
| AttentionServiceTests (S4 tail) | 115 | 0:43 | ~21 s | **86 s / 30 (2.9 s/test)** | collapsed |
| AgentTaskDeliveryWatchdogTests (S4 tail) | 69 | 0:46 | ~24 s | max 12.2 s/test | collapsed |

The cause is S2 + CARD-0238: those classes each boot a `WebApplicationFactory` and/or take an
isolated schema, which used to pay a 55-migration replay **and** compete for the exhausted
100-connection budget. Migrate-once template cloning made both ~free. **The plan's entire
summed-time model — 2126 test-seconds, git ~515 s, migration ~200 s — no longer describes the
suite.** S3 (git) and the DB-inflated half of S4 have already been delivered, for free, by S2.

## 3. The "15-18 minute sequential tail" was a pre-S2 artefact

CARD-0165 (2026-08-29, **pre-S2**) found the global `[NotInParallel]` tail was 15-18 of the 28
minutes, and recommended prioritising it. That finding is now stale for the same reason as §2: the
tail classes are global-`[NotInParallel]` precisely because they drive a WAF/service graph, and the
WAF boot was most of their cost. Post-S2 the two biggest tail classes run in ~21 s and ~24 s of
real work (§2). The tail is no longer the lever it was; Gotcha #74's "last 15-18 minutes" number
needs correcting (S6).

## 4. What is actually expensive now: the 1-wide `ProcessSpawnLimit` lane

The cost that S2 and CARD-0238 could **not** touch is real process spawning and real ConPTY/PTY
waits. Every such class carries `[ParallelLimiter<ProcessSpawnLimit>]` and serialises into **one
1-wide lane** (CARD-0050 S5). Measured today:

| Class / chunk (spawn lane unless noted) | Tests | Wall | ~Exec | Plan 08-21 | Verdict |
|---|---:|---:|---:|---|---|
| **SessionMessageQueuePtyIntegrationTests** | 9 | **3:52** | **~210 s** | 104 s | GREW — now the biggest single class |
| **RunnerProcessProbeTests** | 94 | **2:17** | **~115 s** | 135 s | unchanged (pwsh spawns) |
| Agents namespace (adapters/pty) | 251 | 1:31 | ~69 s | — | canaries `[Explicit]`-skip correctly |
| Scripts namespace (pwsh script tests) | 38 | 1:19 | ~57 s | — | — |

The lane also holds `AgentSessionServiceIntegrationTests`, `AgentSessionRuntimeTests`,
`RawPtyAdapterTests`, the four `*AdapterLocalShellTests`, `DelegationBriefCeilingPtyTests`,
`SessionMessageQueueGrokPtyIntegrationTests`, and ~25 more. Summed, the lane is on the order of
**9-10 minutes of serial wall** and is now the suite's dominant serial pole. Because it is serial,
every second removed from it comes ~1:1 off the wall clock — the opposite of the parallel-phase
tests, where a second removed is divided by parallelism.

**SessionMessageQueuePtyIntegrationTests (~210 s) is not a safe compression target.** Its waits are
either upper-bound `WaitForRawAsync` timeouts (return on match, so cheap unless the fake is slow) or
the re-press cadences its own comments mark as pinned contract — `ReEnterIntervalSeconds` already
compressed 7→3→2, `TranscriptConfirmTimeoutSeconds = 8 // 3 Enters at 0/2/4s, then give up`. Plan
§6 and CLAUDE.md forbid compressing a wait that pins a real-binary behaviour. Its bulk is inherent
real-ConPTY + `fakeclaude.exe` spawn per test. Leave it in the CI/Integration lane.

## 5. The finding that changes the plan: a category-predicate fast lane already works

The 2026-08-21 plan deferred the assembly split (S7) partly on the claim that *"the filter engine
can't express a fast lane in-place: `--treenode-filter` has no OR"*. That was tested with
class-name globs and ranges. **It was not tested with a category predicate, and a category
predicate works:**

```
Antiphon.Tests.exe --treenode-filter "/*/*/*/*[Category=Unit]"
  -> total: 778   failed: 0   skipped: 1   wall: 1:05
```

Two facts make this the crux of the card:

1. **The suite already tags categories** — 218 `[Category("Integration")]` and 81
   `[Category("Unit")]` source attributes (expanding, with parameterisation/inheritance, to 778
   Unit-tagged test cases). The convention exists; it is just incomplete.
2. **A pure-logic run pays no container cost.** `DelegationUnitTests` — 139 tests — runs in **1
   second**, because a run whose tests touch no DB never triggers the lazy Postgres-container
   `[Before(Assembly)]`. The 778-test Unit lane at 65 s is already foreground-fast; a fully-tagged,
   container-free Unit lane would be faster still.

So the card's actual pain — "can't verify in a 10-minute foreground window" — is solvable **today**
with an existing tag and an existing filter, and **without** a new project, a second build, a
fakeclaude staging rewire, or a "which project does this test go in" tax. That is a materially
cheaper answer than the project-split S7.

## 6. Revised slices

Priorities are redrawn on §1-§5. Drop S3 (delivered by S2). Drop the DB-inflated half of S4
(delivered by S2). The remaining work, highest card-value first:

### S7′ (recommended next slice) — complete the category tags, make the fast lane a one-liner

Not a project split. A tag-completion pass plus a guard.

- **(a) Tag every test class `Unit` xor `Integration`.** `Integration` = needs Postgres / a WAF /
  `ProcessSpawnLimit` / real git / the delegation DI graph (the ~223 must-stay files, ~2646 tests
  measured in §7 below). `Unit` = pure logic (the ~102 candidate files, ~1020 tests). Most of the
  ~3900 tests are currently **untagged** — that is the work.
- **(b) A meta-test guard** that fails if any test carries neither tag or both, so the lane cannot
  silently rot as new tests land (the same shape as the client suite's single-budget guard). This
  is the enforcement the card's architectural principle asks for.
- **(c) Wire both lanes.** Local default loop: `--treenode-filter "/*/*/*/*[Category=Unit]"`
  (~60-90 s foreground). CI keeps the full run. Fold the command into the testing docs (S6).
- **Effort**: mechanical tagging of ~3100 untagged tests, most by class. The guard + docs are
  small. No new csproj, no CI restructure beyond one filtered invocation.
- **Risk**: a test mis-tagged `Unit` that actually needs a container will fail loudly on the first
  Unit-lane run (it cannot reach the DB) — a safe failure mode, not a silent one. The guard makes
  "untagged" itself a failure.
- **Alternative if the tag pass is judged too broad to land at once**: tag only the ~1020
  pure-logic candidates `Unit` incrementally; the lane grows as tagging proceeds. The 778 already
  tagged make it useful from day one.

### S5 (keep, smaller, secondary) — RunnerProcessProbeTests: fewer pwsh spawns

Still valid and still ~115 s of pure serial spawn-lane wall (§4), unchanged by S2 — so ~1:1 off the
CI wall clock. 94 cases, most of them redaction/formatting shapes that can drive the redaction
logic directly (the card's own fake-boundary principle) with a handful of true end-to-end spawn
tests retained. Now secondary to S7′ because it trims the CI/Integration lane, not the local
foreground loop the card exists to restore. Target: ~115 s → ~40 s.

### S6 (keep) — testing docs + slow-test tripwire, and fix the stale gotchas

- Document the §5 fast-lane command and the §5 fact that `[Category=X]` predicates DO work in
  `--treenode-filter` (correcting the 08-21 plan's "no in-place fast lane" claim — a single
  category predicate is not an OR).
- **Correct Gotcha #74**: the suite is ~25.5 min (not 28), the exhaustion is fixed, and the
  "last 15-18 minutes are the sequential tail" claim is pre-S2 (§3).
- Add the duration tripwire (parse a full-run TRX, warn on any test ≥5 s not in a checked-in
  allowlist) so the spawn lane cannot silently regrow.

### Dropped / not-a-slice

- **S3 (git)** — delivered by S2 (§2). Optional 1-line tidy: `[Explicit]` on the finished
  `GitDiffSpikeTests` spike, but it now saves ~5 s, not 124 s, so it is cleanup, not a slice.
- **S4 (waits)** — AttentionService/Watchdog collapsed (§2). The one class that grew,
  SessionMessageQueuePtyIntegrationTests, is inherent ConPTY + pinned cadence and must not be
  compressed (§4). No safe compression work remains.
- **Full project split (original S7)** — superseded by S7′. Revisit only if a category-filtered
  in-process Unit run proves too slow because it still starts the container for the whole assembly
  (it did not for `DelegationUnitTests`; re-check once tagging is complete).

## 7. Split-candidate inventory (for S7′ tagging)

A conservative scan (any of `TestDbFixture`/`CreateIsolatedSchema`/`AntiphonWebAppFactory`/
`WebApplicationFactory`/`ProcessSpawnLimit`/`NotInParallel`/`GetRequiredService`/
`AddDelegationWorktreeGraph`/`DirectSessionRunnerClient`/`SessionRunnerRuntime` ⇒ Integration):

| Bucket | Files | Tests |
|---|---:|---:|
| Must stay Integration | 223 | 2,646 |
| Pure-logic Unit candidates | 102 | 1,020 |

Note the scan is conservative — a few "candidates" still spawn real git without the
`ProcessSpawnLimit` marker (`GitDiffSpikeTests`, `GitServiceTests`, `WorkspaceHookRunnerTests`); tag
those `Integration`. The full candidate list is reproducible with the classifier in this task's
transcript.

## 8. Reproduce

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c110m/     # forward slash
$exe = 'tests\Antiphon.Tests\bin-c110m\Antiphon.Tests.exe'
# fast lane (works today):
& $exe --treenode-filter "/*/*/*/*[Category=Unit]" --no-progress --no-ansi   # ~65s, 778 tests
# per-class timings from §2/§4:
& $exe --treenode-filter "/*/*/SessionMessageQueuePtyIntegrationTests/*" --no-progress --no-ansi
& $exe --treenode-filter "/*/*/RunnerProcessProbeTests/*" --no-progress --no-ansi
# full suite (~25.5 min, will NOT fit a foreground window — use the watchdog):
pwsh -File scripts/run-tests-watched.ps1 -Exe $exe -Tag full -Detailed
Get-ChildItem C:\Antiphon\worktrees\card-task-fe7498e3 -Recurse -Depth 3 -Directory -Filter bin-c110m | Remove-Item -Recurse -Force
```

## 9. Not in scope / unchanged contracts

- No `[NotInParallel]` removal or key-narrowing (plan §2/§6 stands — the incident record outweighs
  the wall cost, and the tail is no longer the lever anyway).
- No compression of any wait that pins a real-binary behaviour (DA1 3 s stall floor, delivery
  ceilings, the 0/2/4 s re-press cadence).
- The real-CLI canaries remain `[Explicit]`/`ANTIPHON_*`-gated and correctly skip in the default
  run (re-confirmed: Agents namespace shows 2 skips, zero un-gated real-CLI tests).
