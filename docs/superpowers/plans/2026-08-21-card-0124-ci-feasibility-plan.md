# CARD-0124 — CI feasibility: what can actually gate this repo, and what honestly can't

**Date:** 2026-08-21 · **Status:** plan/report (the card asked for a scope + feasibility answer
before anything is built; no workflow file is created by this pass). Ground truth verified against
master `6e8fbc5` with a clean working tree; every timing below was measured on that commit, on this
dev machine, today.

## Verdict

**Add CI, but scope it to what is provably green and hosted-runner-feasible today, and be honest
that on this repo CI is *detection*, not a merge gate.** Concretely:

1. **S1 (build now):** make `publish-nuget.yml` run `Antiphon.Messaging.Tests` before packing —
   this is the one place in the repo where a *real* gate already structurally exists (the publish
   is the gated action), and today it ships five packages with zero tests despite a green
   129-test suite covering exactly those projects (6.6s test time). Add a `ci.yml` on push to
   master + `feat/**` with an `ubuntu-latest` job: client lint + `tsc -b`/`vite build` + vitest +
   `Antiphon.Messaging.Tests`. All four are green today and total ~5 minutes. The repo is
   **public**, so GitHub-hosted minutes — including Windows — are free; cost is not a constraint,
   only feasibility and flake-noise are.
2. **S2 (build next):** a `windows-latest` job for `Antiphon.PtyHost.Tests` (22 tests, 13s) and
   `Antiphon.SessionRunner.Tests` (135 tests, 75s) — both green, both Docker-free, both fast.
3. **Explicitly OUT of any CI gate:** `Antiphon.Tests` (cannot run on *any* GitHub-hosted runner
   as written — see feasibility findings), `Antiphon.Agents.Pty.Tests` (measurably flaky on an
   idle machine today; a flapping check is worse than no check), and `Antiphon.E2E` (depends on
   this machine's always-on session-runner until CARD-0102's plan is built, plus Playwright, a
   fresh `client/dist`, and the Postgres testcontainer). For these, the honest interim is a
   **nightly scheduled run on this machine with failure alerting** (S3), and the real fix is
   CARD-0110's speed/split work — which is a hard precondition, stated plainly below.

"No CI is a defensible tradeoff" was considered and rejected for S1/S2 specifically: the marginal
cost is near zero (public repo, green suites, ~5 min), and CARD-0010's two-months-stale Playwright
test is the measured cost of silence. The heavyweight suites staying out of the hosted gate *is*
the defensible tradeoff — provided the nightly run and its alert exist, so absence is a decision
with a backstop rather than a silent gap.

## Ground truth this plan stands on

### The repo's actual workflow has no place to hang a merge gate

- `gh pr list --state all` returns `[]` — **zero pull requests, ever**. Work lands as direct
  pushes to master (delegates, docs) and as `feat/card-task-*` branches merged locally and
  pushed. GitHub required status checks only gate *PR merges*; there is no GitHub mechanism that
  makes a direct push wait for a workflow. A ruleset could forbid direct pushes outright, but
  that would break the entire delegate dispatch workflow for a review model nobody here uses.
- Therefore "CI" on this repo means: **a push-triggered workflow whose red is a notification**
  (GitHub emails the pushing actor on a failed run by default), not a thing that stops a merge.
  The one true gate available is the publish workflow itself — tests before `dotnet pack` mean a
  broken Messaging change cannot *ship*, even though it can land on master.
- No git hooks are installed (`.git/hooks` has only samples, no `core.hooksPath`). A pre-push
  hook was considered and rejected: delegates push many times per task from the same working
  tree the daemons run from; a hook running even the 5-minute fast set on every push would
  multiply every task's wall clock, and a hook can always be bypassed (`--no-verify`), so it is
  cost without enforcement. Detection-after-push with alerting fits how work actually lands.

### The existing workflow, verified

`.github/workflows/publish-nuget.yml` is the only workflow (single file under `.github/`). It
packs and pushes the five `Antiphon.Messaging*` packages on a master push touching those paths,
on `ubuntu-latest` — proving the Messaging subtree already restores and builds on Linux — and
runs zero tests. `Antiphon.Messaging.Tests` exists (TUnit + Shouldly only, no containers, no
process spawning), and passed 129/129 in 6.6s today. The gap is exactly one `dotnet run
--project tests/Antiphon.Messaging.Tests` step.

### Suite-by-suite feasibility (all measured today unless noted)

| Suite | Result today | Wall / test time | Hosted-runner fit |
|---|---|---|---|
| client `eslint . --max-warnings 0` | green | 55s | ubuntu — yes |
| client vitest (51 files, 459 tests) | green | ~2m (121.6s vitest-reported, last full run) | ubuntu — yes (jsdom, OS-agnostic) |
| `Antiphon.Messaging.Tests` (129) | green | 6.6s test / 42s wall incl. build | ubuntu — yes (already packs there) |
| `Antiphon.SessionRunner.Tests` (135) | green | 75s test / 2m47s wall | windows-latest — yes; run on Windows because production is Windows (one file OS-checks; Linux viability untested and not worth proving) |
| `Antiphon.PtyHost.Tests` (22) | green | 13s test / 43s wall | windows-latest — yes (Win32 spawner, no DB) |
| `Antiphon.Agents.Pty.Tests` (316, 40 skipped headed) | **flaky**: 2 failed, then 1 failed on consecutive runs | 2m36s–3m41s | windows-latest *mechanically* plausible, **blocked by flakiness** |
| `Antiphon.Tests` (~2000) | not re-run (14m15s at 1878 tests per CARD-0110, exceeds a 10-min foreground window since 2026-08-20) | 14m+ and growing | **no hosted runner fits — see below** |
| `Antiphon.E2E` (13 files) | not run | heavy | **no — see below** |

The pty flake, for the record: run 1 failed 2 tests; run 2 failed only
`ClaudeDetectorsTests.DoneDetector_returns_false_under_continuous_output`
(`ClaudeDetectorsTests.cs:73`, "done should be False but was True") — a quiet-period timing
detector losing its race on an *idle* dev machine. On a cold 2-vCPU CI VM this gets worse, and a
check that flaps trains everyone to ignore red, which is strictly worse than today's no-check.
Deflaking this suite is CARD-0110-adjacent work and a named precondition for S4.

### Why `Antiphon.Tests` cannot run on any GitHub-hosted runner as written

It needs **two things no single hosted runner provides simultaneously**:

- **Windows at runtime** — 15 files carry `ProcessSpawnLimit`, ~23 reference
  ConPTY/fakeclaude; the pty integration tests drive a real Windows pseudoconsole (inbox
  conhost or the shipped OpenConsole per ADR-0002).
- **Linux Docker** — `TestDbFixture` (`tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs:15`)
  hard-codes a `PostgreSqlBuilder` testcontainer (`postgres:16-alpine`, a Linux image) with no
  environment override. GitHub's `windows-latest` runners run Windows containers only; Linux
  images do not run there.

Two escape hatches exist, both real but neither in this card's scope:

1. **Native Postgres on the Windows runner** — the `windows-latest` image ships PostgreSQL
   preinstalled (service stopped; start it in a step). Needs a small `TestDbFixture` change: an
   `ANTIPHON_TEST_PG_CONNECTION`-style env override that skips the testcontainer. Cheap change,
   but pointless until the suite fits a CI window at all — it is 14m+ *on this machine* and
   growing daily (CARD-0110's own numbers), and per-VM it would be slower.
2. **Split the assembly** (CARD-0110's explicit investigation item): a DB/unit half that runs on
   `ubuntu-latest` with a Postgres service container, and a pty half that runs on
   `windows-latest`. This is the right end state and it is CARD-0110's work to design, not this
   card's — **CARD-0110 is the precondition for `Antiphon.Tests` ever having a CI story**, and
   this plan takes a dependency on it rather than duplicating it.

### Why `Antiphon.E2E` is scoped out entirely

Beyond Playwright + the Postgres testcontainer + the stale-`client/dist` rebuild requirement
(all CI-expressible in principle), the fixture pins `SessionRunner:BaseUrl =
http://localhost:17204` — **this machine's always-on daemon**. CARD-0102's committed plan
(`2026-08-21-card-0102-e2e-runner-isolation-plan.md`) gives the fixture its own runner instance;
until that is *built*, E2E cannot run anywhere but here, full stop. Even after it lands, this is
the slowest, most infra-heavy suite in the repo, on a Windows runner, with browser installs — the
honest placement is the nightly local run (S3), revisited only after both CARD-0102 and
CARD-0110 have landed. Scoping it out **explicitly, with the nightly backstop**, is this card's
answer to "the Playwright test rotted for two months": the failure mode wasn't "E2E isn't in CI",
it was "nothing ran it *at all* and nothing alerted".

### The self-hosted-runner trap (why S3 is a Scheduled Task, not a GitHub runner)

The obvious move — register this always-on machine as a self-hosted runner so the heavy suites
run "in CI" — is rejected: **this is a public repo**, and GitHub's own guidance is to never
attach self-hosted runners to public repositories (a workflow triggered by outside code can
execute on the box that holds the Bitwarden relay, the browser profile, and every live agent
session). Fork-PR approval settings reduce but don't eliminate the exposure, and the machine
also actively runs the daemons the tests would contend with (`ProcessSpawnLimit` is 1-wide
precisely because concurrent spawns interfere). A local Scheduled Task (or a Windmill job on
server2 SSHing in, the pattern the weekly build-junk cleanup already uses) gets the same nightly
coverage with zero new attack surface.

## Slices

**S1 — gate the publish, and stand up the cheap detector (one small card).**
- `publish-nuget.yml`: add `dotnet run --project tests/Antiphon.Messaging.Tests` as a step
  before the Pack step. Red tests ⇒ nothing ships. This converts the repo's only workflow from
  0% to 100% tested-before-publish for the code it publishes.
- New `.github/workflows/ci.yml`: `on: push` (master + `feat/**`) + `workflow_dispatch`, one
  `ubuntu-latest` job, four steps: `npm ci`; `npm run lint`; `npm run build` (the `tsc -b` type
  gate — also keeps `client/dist` buildability honest); `npx vitest run`; then
  `dotnet run --project tests/Antiphon.Messaging.Tests`. Expected ~5–6 min total.
- **CARD-0069 trap, encoded as a review rule for the workflow file:** every test step must be
  the *bare* command — never piped through `tail`/`head`/`tee` in a shell that would launder the
  exit code. GitHub steps fail on the command's own exit status; vitest and TUnit both propagate
  correctly when invoked directly (measured in CARD-0069). `scripts/test-client.ps1` exists for
  *interactive* callers whose output gets capped; CI needs the raw command and the raw code.
- Path-filter nothing on `ci.yml` at first: a docs-only push costing 5 free minutes is cheaper
  than a path filter that silently exempts a file someone later makes load-bearing.

**S2 — the Windows-native fast pair (one small card, after S1 has run green for a few days).**
- Add a `windows-latest` job to `ci.yml`: `dotnet run --project tests/Antiphon.PtyHost.Tests`
  then `dotnet run --project tests/Antiphon.SessionRunner.Tests`, sequential (both spawn
  processes; keep the repo's one-at-a-time discipline). Expected ~6–8 min including runner
  provisioning and restore — slower than the ubuntu job, free regardless.
- This is also the cheap probe for the open question "does ConPTY behave on the runner image":
  `PtyHostLauncherTests` exercises the real spawner. If this job turns out stable over a couple
  of weeks, it is the evidence S4 needs; if it flakes, that is a finding about the runner image
  measured on the *small* suite, not the 316-test one.

**S3 — nightly full-suite run on this machine, with alerting (one card).**
- A per-user Scheduled Task (same install pattern as `install-autostart.ps1`'s tasks) or a
  Windmill job on server2 (same pattern as the weekly cleanup — memory says don't re-add local
  tasks for things Windmill already owns, so prefer Windmill) that nightly: pulls master, builds
  to an alternate `OutputPath` (forward slash — the daemons hold `bin/`), runs
  `Antiphon.Tests` then `Antiphon.Agents.Pty.Tests` (never co-scheduled, per CLAUDE.md), then
  the client suite via `scripts/test-client.ps1`, then E2E after `npm run build`.
- **Failure alerting is the entire point**: a red run must produce a Telegram/Slack message
  through the repo's own messaging gateway (or minimally an Antiphon incident) naming the suite
  and count. A nightly run nobody hears fail is the CARD-0010 gap with extra steps.
- E2E joins this run only once CARD-0102's isolation is built; until then the nightly E2E leg
  targets the live daemon deliberately and says so in its alert text.

**S4 — deferred, precondition-gated: `Antiphon.Agents.Pty.Tests` in the hosted Windows job.**
Enter only when (a) S2's Windows job has proven the runner image stable and (b) the suite has
had its timing-sensitive detector tests deflaked or `[Explicit]`-gated (CARD-0110's fake/real
boundary principle: `DoneDetector_returns_false_under_continuous_output` asserting on a live
quiet-period race is exactly the shape that should be tested against a controlled clock). Add
it first as a *non-blocking* scheduled workflow to gather flake data, promote to the push job
only after N consecutive green scheduled runs.

**Explicitly not scheduled, dependency-named:** `Antiphon.Tests` in hosted CI. Blocked on
CARD-0110 (speed + probable assembly split). When CARD-0110 lands a split, its ubuntu-able half
slots into S1's job with a Postgres service container and its Windows half into S2's job — the
workflow shape this plan builds is already the right receptacle.

## Rejected alternatives

- **PR-based required checks / branch protection** — no PRs exist; would force a workflow
  change far larger than the problem. Revisit only if the repo ever adopts PRs for other
  reasons.
- **Pre-push git hook** — bypassable, multiplies every delegate push by the suite time, and the
  push origin is the same machine the daemons run on (bin locks, spawn contention).
- **Self-hosted GitHub runner on this machine** — public repo; unacceptable attack surface for
  zero benefit over a Scheduled Task/Windmill job (S3 gets the same coverage).
- **Forcing every suite into one gate now** — the flaky pty suite and the 14-minute
  `Antiphon.Tests` would make the check either red-by-default or 20+ minutes; both outcomes
  teach people to ignore it. Incremental green-only admission is the design.
- **"No CI, document the discipline instead"** — legitimate per the card, but only defensible
  when the alternative costs something. S1 costs one workflow file and ~5 free minutes per push
  against suites that are already green; the discipline-only answer is the right one *for the
  heavy suites*, and S3 is that answer written down with an alarm attached.

## Deliberately not in scope

- Any change to `TestDbFixture` (the env-override escape hatch is noted for CARD-0110's use).
- Deflaking `Antiphon.Agents.Pty.Tests` (named as S4's precondition; belongs with CARD-0110's
  fake/real boundary work).
- Splitting `Antiphon.Tests` (CARD-0110's investigation item; this plan only reserves where the
  halves land).
- Wiring GitHub webhook → Antiphon incident/Telegram for workflow failures (GitHub's default
  actor email suffices for S1/S2; the gateway alert is specified only for S3 where GitHub isn't
  involved).

## Card housekeeping

- CARD-0124 stays open through S1–S3; this plan answers its investigation questions. S4 and the
  `Antiphon.Tests` slot are dependencies *on* CARD-0110, which should gain a back-reference to
  this plan ("its CI slot is reserved in the CARD-0124 plan").
- CARD-0010's stale-Playwright finding is addressed structurally by S3's nightly-with-alerting,
  not by any hosted gate — worth noting on that card when S3 ships.
- The pty suite flake found today (`ClaudeDetectorsTests.DoneDetector_returns_false_under_continuous_output`,
  intermittent, plus one more failure in the first of two runs) is a real, current master
  finding independent of CI and deserves its own small card if not already tracked.
