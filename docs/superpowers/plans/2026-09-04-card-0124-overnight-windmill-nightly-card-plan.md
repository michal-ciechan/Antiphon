# CARD-0124 — Overnight Windmill build+test job that files a card on failure

**Date:** 2026-09-04 · **Status:** plan (build pass not started) · **Supersedes:** the GitHub
Actions design in `2026-08-21-card-0124-ci-feasibility-plan.md` and the Telegram-alert half of
`2026-08-21-card-0131-nightly-full-suite-plan.md` · **Reuses:** CARD-0131 S1's
`scripts/nightly-tests.ps1` (commit `3b2502e4`), reshaped.

Scope as confirmed by the operator: a Windmill job (same shape as the existing desktop jobs)
that, every night, syncs an **isolated** checkout of `origin/master`, builds, runs
`Antiphon.Tests`, `Antiphon.Agents.Pty.Tests` and the client vitest suite, and files (or
updates) one Antiphon card when anything is red. Not a fast lane, not build-only, not a merge
gate.

## 0. What already exists, and why it is not enough

| Piece | State on 2026-09-04 | Evidence |
|---|---|---|
| `scripts/nightly-tests.ps1` | Shipped by CARD-0131 S1 (`3b2502e4`). Preflight, four suites sequential, per-suite logs, `summary.json`, `bin-nightly/` cleanup. **Runs in `C:\src\Antiphon`** with a "pull-rebase only if clean master" policy (CARD-0131 D3). | file header; CARD-0131 card "Progress" |
| Windmill script `u/lndcobra/antiphon_nightly_tests` | **Does not exist.** CARD-0131 S2 (alerting) and S3 (schedule) were never dispatched. | `GET /api/w/mc/scripts/exists/p/u/lndcobra/antiphon_nightly_tests` → `false` (queried live, temp token, revoked after) |
| `C:\src\Antiphon\logs\nightly\` | Does not exist — the script has never run on a schedule on this machine. | `ls` on the main checkout |
| Alerting | CARD-0131 designed Telegram to the test group. The redirected CARD-0124 replaces that with "file a card". | CARD-0124 description, 2026-09-04 redirect |

Three things about the existing script have to change, and they are the whole reason this plan
exists rather than "dispatch CARD-0131 S3":

1. **It builds and tests the main checkout.** That is the CARD-0358 class of risk on a
   schedule: the canonical checkout's `master` can sit behind `origin/master` for hours
   (three pushes in a row did exactly that on 2026-09-04), so a main-checkout nightly would
   silently test stale code while reporting a sha that looks current; and any `git pull` it did
   there would move the tree the AppHost builds from, at 02:00, without the operator knowing.
2. **It uses `--property:OutputPath=bin-nightly/`** because the daemons hold `bin/` — only
   necessary in the shared tree, and the source of the ledger/junk-directory gotchas (#30, #31,
   #75 in `docs/testing-and-build.md`).
3. **It reports nothing** ("Alert delivery is intentionally not implemented here").

## 1. Ground truth this plan stands on

Everything below was read or measured on 2026-09-04 unless a date says otherwise.

### 1.1 Windmill on server2 (queried through the API with a temporary superadmin token, revoked)

- Version **CE v1.700.2**. Workspace `mc`. Every Antiphon job is a `bash` script tagged
  `desktop`, run by the single desktop worker container (`C:\windmill\run-container-worker.ps1`
  line 56: `NUM_WORKERS=1` — **one job at a time on this machine**; a long job queues everything
  else tagged `desktop` behind it).
- Existing schedules (6-field cron, seconds first, all `Europe/London`):

  | Path | Cron | When | Notes |
  |---|---|---|---|
  | `antiphon_github_sync` | `0 0 */3 * * *` | every 3 h at :00 | quick; would queue behind a running nightly |
  | `browser_overnight_close` | `0 0 4 * * *` | daily 04:00 | kills every browser; nightly must not need one (it does not — headed tests stay off) |
  | `desktop_heartbeat` | `0 0 8 * * *` | daily 08:00 | |
  | `bob_outlook_sync` | `0 0 8 * * *` | daily 08:00 | |
  | `antiphon_build_junk_cleanup` | `0 0 9 * * 1` | Mon 09:00 | sweeps `bin-*` under `C:\src\Antiphon` only |
  | `claude_session_cleanup` | `0 15 9 * * 1` | Mon 09:15 | |
  | `antiphon_codex_residue_cleanup` | `0 30 9 * * 1` | Mon 09:30 | (the card's "two Monday jobs" is stale — there are three) |
  | `openclaw_sync` | `0 0 8,16 * * *` | disabled | |

  None sets `timeout`, `on_failure` or `on_recovery`. The schedule API on this version accepts
  `on_failure` (path of a script), `on_failure_times`, `on_failure_exact`,
  `on_failure_extra_args`, `on_recovery` (read from `/api/openapi.yaml`).
- The exact shape to copy — `u/lndcobra/antiphon_build_junk_cleanup`, verbatim:

  ```bash
  ssh -i /tmp/windmill/worker_to_windows \
      -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o BatchMode=yes \
      lndco@host.docker.internal \
      'powershell -NoProfile -ExecutionPolicy Bypass -File C:\src\Antiphon\scripts\cleanup-build-junk.ps1'
  ```

  Its last three scheduled runs (08-17, 08-24, 08-31) all succeeded, 1–17 s each.
- `u/lndcobra/telegram_notify` (python3, untagged) takes `text` and `chat_id` (default: the
  operator's DM) and posts through the Antiphon bot token held as a Windmill variable. It is the
  natural `on_failure` handler (D8).
- Registration procedure and the "archive the old hash before re-creating a path" rule:
  `~/.claude/skills/windmill/SKILL.md` and memory `reference_windmill_cleanup_schedule`.

### 1.2 The SSH bridge, probed from this machine with the same key (`C:\windmill\ssh\worker_to_windows` → `lndco@localhost`)

- The session is **session 0, non-interactive** (`[Environment]::UserInteractive = False`), user
  `lndco`. Default `powershell` is Windows PowerShell 5.1; **`pwsh` 7.6.5 is on PATH**, as are
  `dotnet`, `node`, `git`.
- **Docker is reachable** from that session (`docker info` → 29.5.3) — so the
  `Antiphon.Tests` Postgres testcontainer can start.
- **The Antiphon API is reachable at `http://localhost:17202` with no token**
  (`GET /api/cards/limits` answered) — same path `scripts/github-sync.ps1` already takes from the
  bridge every three hours.
- **`git ls-remote origin` works non-interactively** (HTTPS remote, `credential.helper=manager`
  reading the stored credential without a prompt).
- **ConPTY works in session 0.** `Antiphon.Agents.Pty.Tests.exe --treenode-filter
  "/*/*/PtyBackendContractTests/*"` run over the bridge: **9/9 passed**, including
  `Asking_for_the_modern_backend_runs_the_child_under_our_own_OpenConsole` and
  `The_production_write_path_delivers_the_markers_on_the_modern_backend`, which spawn a real
  child under the shipped OpenConsole pair. This was the one open feasibility question for the
  pty suite (the production runner is launched by an *interactive* logon task, so its behaviour
  was no evidence); it is closed.

### 1.3 Test procedures the runner must honour (`docs/testing-and-build.md`)

| Gotcha | Consequence for this job |
|---|---|
| #18 — never co-schedule `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests`; `ProcessSpawnLimit` is per assembly | Strictly sequential suites (already so in the S1 script). |
| #19 — client suite via `scripts/test-client.ps1`, verdict is its exit code / `CLIENT TESTS EXIT CODE:` line, never a Bash pipe | Keep; the runner is PowerShell end to end. |
| #24 — **one `postgres:16-alpine` testcontainer per `Antiphon.Tests` run**, not the dev DB on 17280 | No isolated Postgres instance is needed and nothing in scope touches the shared dev database (D3). |
| #26 — `ProductionRunnerGuard` points `Program` boots at `127.0.0.1:1` and disables the check interpreter | The job never launches against the production runner; nothing extra to do. |
| #30/#31/#75 — alternate `OutputPath`, trailing-space directories, `FileListAbsolute` ledger bloat | All three are artefacts of building in the tree the daemons hold. An isolated clone builds into its own `bin/` and needs **none** of them (D2). |
| #73 — frozen-clock harnesses hang | An authoring rule; no runner impact, but "alive, ~0 CPU, no output" is why the runner needs a per-suite watchdog rather than an outer kill (D5). |
| #74 — a full `Antiphon.Tests` run is ~25–28 min and its last 15–18 min look like a stall | Watchdog budget for that suite is 60 min; do not kill earlier. |

Test counts and timings used for budgets: `Antiphon.Tests` 3,893 tests, ~25–28 min (CARD-0336
plan, Gotcha #74); `Antiphon.Agents.Pty.Tests` 2m36s–3m41s (CARD-0124 measurements); client
vitest 5–7 min full (CARD-0307); `dotnet build` full graph 1m28s warm (Gotcha #75), call it 5 min
cold with restore; `npm ci` 1–2 min from the npm cache. **Typical whole run ≈ 45 min, worst case
under 2 h.**

Current known-red baseline: CARD-0238 (connection exhaustion), CARD-0336 (the 16 non-Postgres
failures), CARD-0128 (pty flake) and CARD-0297 are all **Done**, so master should be near green.
The first scheduled run is the measurement; whatever it files is real signal.

### 1.4 The card API the report step drives (`server/Api/Endpoints/CardEndpoints.cs`, `docs/antiphon-api.md`, `scripts/card.ps1`)

- `GET /api/boards` → board `Antiphon` = `8988ca03-7414-47ad-b0b6-51556c701703`; columns
  Backlog / In Progress / Review / **Done (terminal)** / Needs decision.
- `GET /api/cards?boardId=…&status=Backlog|InProgress|Review|NeedsDecision|Done` →
  `{ cards: [...], truncated: bool }`; each card carries `identifier`, `status`, `labels`,
  `title`, `description`, `updatedAt`, `concurrencyToken`, `terminalReason`, `archivedAt`.
- `POST /api/boards/{id}/cards` body `{ title, description, importance, urgency, labels }`.
- `PATCH /api/cards/{id}/content` body `{ concurrencyToken, reason, title?, description?, labels? }`
  — replaces the description (revision-logged; every write rotates the token, so read first).
- `PATCH /api/cards/{id}` body `{ boardColumnId, concurrencyToken, reason }` — this is how
  `card.ps1 close` closes (first terminal column). `POST /api/cards/{id}/reopen`
  `{ concurrencyToken, reason }`.
- `POST /api/cards/{id}/discussion` `{ body, author }` — the **stored** thread (CARD-0166).
  `POST /api/cards/{id}/comments` is deliberately **not** used: that route injects the text into
  the card's live agent session (`CardEndpoints.cs:197`).
- Limits (`GET /api/cards/limits`): title 300, description 20,000, reason 4,000, actor 200.
- No authentication on any of these; `X-Antiphon-Task-Token` is optional and only scopes card-id
  resolution. Using `boardId` explicitly removes the cross-board ambiguity `card.ps1` handles
  with `cwd`.

### 1.5 Commit velocity (does nightly fit?)

`origin/master`, last 30 days: between 4 and 90 commits **every single day**; last 60 days by
weekday — Mon 180, Tue 143, Wed 207, Thu 217, Fri 120, **Sat 78, Sun 161**. Weekends are not quiet.
Nightly every day is the right cadence; a weekday-only schedule would skip real changes.

## 2. Decisions

### D1 — Run in an isolated, persistent clone of `origin/master`; never in `C:\src\Antiphon`, never in a delegate worktree

- Location `C:\Antiphon\nightly\checkout` (sibling of `C:\Antiphon\worktrees`, **not under it**,
  so `WorktreeManager`'s `git worktree list` never sees it — it is a separate `git clone`, not a
  worktree of the main repo). Logs live outside the checkout at `C:\Antiphon\nightly\logs\<yyyy-MM-dd-HHmm>\`
  so a `git clean` can never eat them; keep 14 days.
- Source of truth is **GitHub** (`https://github.com/michal-ciechan/Antiphon`), fetched over the
  credential the bridge session already has (§1.2). Cloning from `C:\src\Antiphon` would inherit
  exactly the stale-`master` state CARD-0358 documents.
- Sync every run: `git fetch origin master` → `git reset --hard origin/master` →
  `git clean -fdx` (fresh `bin/`, `obj/`, `node_modules/`, `dist/`). Determinism over the 3–4
  minutes a warm tree would save; a nightly has hours of slack. First run clones (~130 MB `.git`,
  784 GB free on `C:`).
- **Guard:** the runner refuses (exit 3) when its `$RepoRoot` canonicalises to the main
  worktree of `C:\src\Antiphon` or to any path under `C:\Antiphon\worktrees\`, unless
  `-AllowSharedTree` is passed. This is the inverse of `apphost-common.ps1`'s
  `Get-AppHostWorktreeClassification` guard (restart scripts refuse *worktrees*; the nightly
  refuses the *shared* tree) and it is pinned by a test (S1).
- **Lock:** `C:\Antiphon\nightly\run.lock` (pid + started-at); a second invocation while one is
  live refuses with exit 3; a lock older than 4 h is stale and replaced. Same shape as
  `logs/apphost.restart.lock`.
- **Skip when nothing changed:** if `origin/master` equals the sha of the previous run *and that
  run was green*, exit 0 with `unchanged since green run at <sha>` and file nothing. A previous
  red run is always re-run (that is the flake series).

### D2 — Build is its own step and its own failure class; no alternate `OutputPath`

1. `npm ci` in `client/` (the clone has no Vite dev server, so CARD-0131 D3's `npm install`
   reason does not apply; `ci` is what a nightly should test).
2. `npm run build` in `client/` — this runs `tsc`; vitest does not, and CARD-0131 S1's very
   first honest run found a production TypeScript regression exactly here. It is "build", and it
   is in scope.
3. `dotnet build Antiphon.sln -c Debug` — the whole graph including all six test projects
   (E2E is built, not run). Then each suite runs **its built exe directly** (the CARD-0336 and
   `run-tests-watched.ps1` pattern), so a build error can never masquerade as a test failure and
   the runner never reaches for `--property:OutputPath`.

A red step 1–3 short-circuits the suites that need it and is reported as `BUILD FAILED` with the
`error CS…` / `error TS…` / npm lines (deduplicated, first 30).

### D3 — Postgres: no isolated instance, no scheduling constraint

Every `Antiphon.Tests` run starts its **own** `postgres:16-alpine` testcontainer
(`TestDbFixture.cs:25`) and tears it down; the pty and client suites use no database; nothing in
scope opens the dev database on 17280 (Gotcha #24, #26). Two concurrent runs — the nightly plus a
delegate's own `Antiphon.Tests` — mean two containers, which is CPU/RAM contention only. The
runner records, in preflight, how many `Antiphon.Tests.exe` / `Antiphon.Agents.Pty.Tests.exe` /
`vitest` processes were alive at start so a red night can be triaged as "under contention" without
guessing; it never waits for them and never kills them.

### D4 — What counts as failure, and what the card says

Outcome classes, in the order they are checked: `PREFLIGHT` (Docker unreachable, disk < 10 GB,
git fetch failed, lock held), `BUILD` (D2 steps), `TESTS` (any suite exit ≠ 0), `TIMEOUT` (a
suite killed by the watchdog, D5), `REPORTING` (the card step itself failed — see D6). Green means
every step exit 0. The run's exit code is non-zero for any class, so the Windmill run is red
independently of the card.

Card **title** (≤ 300): `Nightly red 2026-09-05: Antiphon.Tests 3 failed, client build failed`
— the class and per-suite counts, refreshed on every red night.

Card **description** (≤ 20,000), newest run first, older runs kept below until the cap trims them:

```
## Run 2026-09-05 00:30 Europe/London — RED (TESTS) — 47m — origin/master f6856040
Concurrent test processes at start: 0 · Docker 29.5.3 · clone C:\Antiphon\nightly\checkout

| Step | Result | Detail |
| npm ci / npm run build | pass | 1m12s |
| dotnet build Antiphon.sln | pass | 4m03s |
| Antiphon.Tests | FAIL | 3 failed / 3,887 passed / 3 skipped · 26m40s |
| Antiphon.Agents.Pty.Tests | pass | 0 failed / 232 passed / 40 skipped · 3m21s |
| client (vitest) | pass | 461 passed · 5m50s |

### New since last run (2)
- Antiphon.Tests › AgentSupervisionTests.a_stalled_session_is_flagged_once
  ShouldBe … expected 1, was 2  (first line of the TUnit failure block)
- …
### Still failing (1)  ·  ### Fixed since last run (0)

Logs: C:\Antiphon\nightly\logs\2026-09-05-0030\ (antiphon-tests.log, agents-pty-tests.log, client-tests.log, build.log, summary.json)
Re-run one: tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.exe --treenode-filter "/*/*/AgentSupervisionTests/a_stalled_session_is_flagged_once"
```

- Failing test names come from the `failed <name> (…)` lines (TUnit) and ` FAIL  <file> > <name>`
  / `×` lines (vitest); the "detail" is the first non-empty line after the name, ANSI-stripped,
  cut at 300 chars. Cap 25 names per suite; say `+N more in the log`.
- The **delta** (new / still failing / fixed) is computed against the previous run's
  `summary.json`, which therefore gains a `failedTests: [names]` array per suite. This is the
  single most useful line for triage and the reason the previous summary is kept.
- Counts are parsed as the S1 script already does; when the parse fails the line says
  `exit <code>, counts unparsed - see log` and never invents a number.
- Labels: `nightly`, plus `build` or `tests` for the class. Importance `High` only when the class
  is `BUILD` (master does not compile); otherwise `Normal`.

### D5 — Schedule: daily **00:30 Europe/London** (`0 30 0 * * *`), with inner watchdogs

- Why 00:30: after the 00:00 `antiphon_github_sync` tick, and a typical 45-min run (worst case
  < 2 h) finishes before the 03:00 sync and the 04:00 `browser_overnight_close`; with
  `NUM_WORKERS=1` anything overlapping simply queues behind the nightly, so the window is chosen
  to overlap nothing. Mondays' 09:00/09:15/09:30 jobs are hours away. Daylight-saving is the
  timezone's problem, not the cron's.
- **Per-step watchdogs inside the PowerShell runner**, so the report step always runs:
  `npm ci` 10 min, `npm run build` 10 min, `dotnet build` 20 min, `Antiphon.Tests` **60 min**
  (Gotcha #74: do not kill before ~35 min), `Antiphon.Agents.Pty.Tests` 20 min, client 20 min.
  A step over budget is killed by process tree (`taskkill /T`) and recorded as `TIMEOUT` with
  the log tail. `run-tests-watched.ps1` (stall = flat stdout **and** ~0 CPU, dump before kill)
  stays a manual diagnostic tool — its dump capture needs `dotnet-dump`/`dotnet-stack` and is
  the wrong default for an unattended run; the nightly's watchdog is a plain deadline.
- **Windmill script `timeout` 10,800 s (3 h)** as the outer bound, above the worst case, because
  a Windmill timeout kills the SSH channel and Windows OpenSSH kills the remote tree with it —
  tests *and* the card step (CARD-0131 ground truth). The bash bridge adds
  `-o ServerAliveInterval=60 -o ServerAliveCountMax=10`: the `Antiphon.Tests` quiet phase is
  15–18 min of flat stdout and the channel must survive it.

### D6 — Card creation: direct, unauthenticated, localhost; a dead server is its own failure class

The report step calls `http://localhost:17202` from the bridge session (proven, §1.2), the same
way `github-sync.ps1` does; `ANTIPHON_API` overrides for tests. No token exists to send and none
is needed. If the server is down (AppHost mid-restart at 01:15 is plausible), the composed title
and body are written to `C:\Antiphon\nightly\logs\<stamp>\card.md`, the run exits 3 with
`REPORTING: could not file card, body at …`, and the Windmill run goes red — the operator files
it by hand from that file (`card.ps1 new -DescriptionFile`). Retry once after 60 s before giving
up; do not loop.

### D7 — One open nightly card at a time; reopen within a week; auto-close only what nobody touched

The card asks for de-dupe and points at CARD-0347's close-on-fix. Rules, evaluated in order
against `GET /api/cards?boardId=<Antiphon>&status=<each>` filtered to label `nightly`
(and `archivedAt == null`):

| Tonight | Existing nightly card | Action |
|---|---|---|
| red | open (any non-terminal status) | `PATCH …/content`: new run section on top, title refreshed, labels merged; plus one `POST …/discussion` line `still red on <date>: <counts>` so the change is visible in the thread as well as the revision log. Never a second card. |
| red | none open, but one in Done whose `terminalReason` starts with `[nightly auto-close]` and `updatedAt` ≤ 7 days ago | `POST …/reopen` (reason `red again on <date>`), then the update above. Keeps a flapping test's history in one card instead of a card per night. |
| red | none | `POST /api/boards/{id}/cards`. |
| green | open, **status Backlog**, nobody assigned (`assignedAgentId == null`, `ownerSessionId == null`) | close: `PATCH /api/cards/{id}` to the terminal column, reason `[nightly auto-close] green on <date> at <sha> — reopen if this was a flake you want tracked`. This is the CARD-0347 parallel (state pushed at the moment the fact changes, never from a tick). |
| green | open, any other column or assigned | `POST …/discussion` `green on <date> at <sha>; not closing because the card is <status>/<agent>` — a human or agent is on it; closing under them is the clobber this repo's concurrency rules exist to prevent. |
| green | none | nothing. No green card, no green comment. |

`truncated: true` on a status page with no match is logged and treated as "none" (the Backlog
page is ~50 cards today; if it ever pages, the fix is a `label` filter on the endpoint, not
client-side guessing).

### D8 — If the *job* dies, Windmill is the surface; optionally wire `on_failure` to Telegram

Silent death (worker down, SSH key rotated, machine asleep) looks like green in a card-only
design — CARD-0131 D2's concern. Mitigations, cheapest first: (a) the run always exits non-zero
on any failure so the Windmill run list is red; (b) `C:\Antiphon\nightly\last-run.json` is
written at the end of every run, green or red, and `card.ps1`-style tooling can check its age;
(c) **recommended, operator's call:** set the schedule's `on_failure` to
`u/lndcobra/telegram_notify` with `on_failure_extra_args: { "text": "antiphon nightly job failed (Windmill run red) - check the run log" }`.
The API accepts the field on CE v1.700; whether CE *executes* schedule error handlers is
confirmed at creation time by firing one deliberately (S3). No Telegram message on green, no
Telegram on a red *test* run — that is the card's job.

### D9 — E2E and headed suites stay out by default

`Antiphon.E2E` is not in the confirmed scope; the runner keeps `-Suites e2e` as an opt-in for a
manual run (the code exists and works since CARD-0102's isolated runner) but the schedule passes
the default: `antiphon, agents-pty, client`. `ANTIPHON_HEADED_TESTS` / `ANTIPHON_CODEX_HEADED_TESTS`
are never set — headed canaries drive real Claude/Codex and belong to deliberate manual runs.

## 3. Slices

### S1 — Restructure the scripts (buildable now, in this repo)

Three scripts, one responsibility each, all ASCII-only and parseable under Windows PowerShell 5.1
(the bridge default) while expecting to run under pwsh 7:

1. **`scripts/nightly-tests.ps1` — the runner** (evolve the existing file; keep its
   `Invoke-LoggedNative`, `Get-TestCounts`, `summary.json` shape). Changes: `-RepoRoot`
   (default: its own checkout) and `-LogRoot` parameters; **remove every git operation** (D1 —
   the bootstrap owns sync); add the D2 build steps and the D5 watchdog (`Start-Process` +
   `WaitForExit(budget)` + tree kill); capture `failedTests` per suite into `summary.json`;
   default `-Suites antiphon,agents-pty,client`; remove `bin-nightly/` handling entirely; add
   the shared-tree guard and the preflight concurrent-process census. Exit codes: 0 green,
   1 red, 2 bad arguments, 3 refused (guard/lock).
2. **`scripts/nightly-run.ps1` — the bootstrap Windmill calls** (new, ~80 lines): take the lock;
   clone or sync `C:\Antiphon\nightly\checkout` to `origin/master` (D1); the unchanged-since-green
   short-circuit; **self-update hop** — if the clone's copy of `nightly-run.ps1` differs from the
   running file, re-exec the clone's copy with `-NoSync` and return its exit code, so the
   version that runs is always the one on `origin/master` and a stale `C:\src\Antiphon` (the
   bridge's entry path) cannot pin old logic; then run the clone's `nightly-tests.ps1` and, in a
   `finally`, the clone's `nightly-report.ps1`; write `last-run.json`; release the lock.
   Parameters: `-Suites`, `-NoReport`, `-NoSync`, `-CheckoutRoot`, `-LogRoot`.
3. **`scripts/nightly-report.ps1` — compose and file** (new): input `-Summary <path>`
   (+ `-PreviousSummary`), compose title/body per D4, apply D7 through a small
   `Invoke-Antiphon` with an injectable `-HttpShim` scriptblock (`param($Method,$Uri,$Headers,$Body)`,
   the `cleanup-claude-sessions.ps1` precedent), `-DryRun` prints what it would send and writes
   `card.md`, `-Board` default `Antiphon`, `-Api` default `$env:ANTIPHON_API` or
   `http://localhost:17202`. Exit 0 filed/updated/closed/nothing-to-do, 3 could not reach the API.

Tests, both runnable in a foreground window:

- **`scripts/test-nightly-report.ps1`** (harness shape of `test-cleanup-claude-sessions.ps1`,
  fixtures under `scripts/fixtures/nightly/`): T1 red + no card → one POST with labels and the
  D4 body; T2 red + open card → PATCH content with the new section first and one discussion
  post, no POST; T3 green + open Backlog unassigned → PATCH to the terminal column with the
  `[nightly auto-close]` reason; T4 green + open InProgress → discussion only; T5 red + Done
  card auto-closed 3 days ago → reopen then update; T5b same but 9 days ago → new card;
  T6 description over 20,000 → oldest run sections dropped, newest intact, still names the log
  dir; T7 green + none → zero HTTP writes; T8 failed-name extraction from a captured TUnit log
  tail and a captured vitest tail (fixtures cut from real logs, e.g. the `53300: sorry, too
  many clients` block in `logs/card-0162-antiphon-tests.log`); T9 shim throws on every call →
  exit 3 and `card.md` written; T10 no token is ever sent (headers empty).
- **`tests/Antiphon.Tests/Scripts/NightlyScriptsTests.cs`** (the `TestClientFilterTests` /
  `GithubSyncScriptTests` pin pattern): the three scripts are ASCII-only; `nightly-tests.ps1`
  contains no `git pull`/`git checkout`/`git stash` and no `OutputPath=`; run with
  `-RepoRoot C:\src\Antiphon -Suites client -WhatIf` it exits 3 naming the guard; the bootstrap
  names `C:\Antiphon\nightly\checkout` and `origin/master`.

Verification for S1 itself: `pwsh -File scripts/test-nightly-report.ps1` all green; the pin
tests green (`--treenode-filter "/*/*/NightlyScriptsTests/*"`); one real
`pwsh -File scripts/nightly-run.ps1 -Suites client` from this machine that clones, builds, runs
the client suite and files nothing (green) or files honestly (red).

### S2 — Positive control: prove detection and filing end to end (buildable now; see §4)

### S3 — Windmill script + schedule (needs the operator, or a session with `ssh mc@server2`)

Payloads to create, in this order (the skill's temp-token procedure; JSON built with
`python3 -c 'import json…'` to dodge quoting):

`POST /api/w/mc/scripts/create`
```json
{
  "path": "u/lndcobra/antiphon_nightly_tests",
  "summary": "Antiphon nightly: sync isolated clone, build, run the 3 suites, file a card on red",
  "description": "SSH-bridge run of C:\\src\\Antiphon\\scripts\\nightly-run.ps1 (CARD-0124). Files/updates one 'nightly' card on the Antiphon board when red; exit code mirrors the run.",
  "language": "bash",
  "tag": "desktop",
  "timeout": 10800,
  "content": "# Antiphon nightly build+test (CARD-0124): SSH into the Windows host and run the repo's\n# bootstrap natively. It syncs C:\\Antiphon\\nightly\\checkout to origin/master, builds, runs\n# Antiphon.Tests -> Antiphon.Agents.Pty.Tests -> client vitest, and files a card on red.\n# ServerAlive keeps the channel up through the suite's 15-18 min silent phase.\nssh -i /tmp/windmill/worker_to_windows \\\n    -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o BatchMode=yes \\\n    -o ServerAliveInterval=60 -o ServerAliveCountMax=10 \\\n    lndco@host.docker.internal \\\n    'pwsh -NoProfile -NonInteractive -File C:\\src\\Antiphon\\scripts\\nightly-run.ps1'\n"
}
```

`POST /api/w/mc/schedules/create`
```json
{
  "path": "u/lndcobra/antiphon_nightly_tests",
  "schedule": "0 30 0 * * *",
  "timezone": "Europe/London",
  "script_path": "u/lndcobra/antiphon_nightly_tests",
  "is_flow": false,
  "args": {},
  "enabled": true,
  "summary": "Antiphon nightly build+test (CARD-0124)",
  "on_failure": "u/lndcobra/telegram_notify",
  "on_failure_times": 1,
  "on_failure_extra_args": { "text": "antiphon nightly job FAILED at the Windmill level (not a test failure) - open the run log" }
}
```

If `timeout` or `on_failure*` is rejected on this CE build, drop that field and note it on the
card; D5's inner watchdogs make the outer timeout a backstop only. Then:

1. `POST /api/w/mc/jobs/run_wait_result/p/u/lndcobra/antiphon_nightly_tests` with `{}` once by
   hand and wait for it (up to ~1 h) — proves the bridge path, not the schedule.
2. **Proven to fire:** wait for the first *scheduled* 00:30 execution and record its Windmill
   run id, `C:\Antiphon\nightly\logs\<stamp>\summary.json`, and the card it filed or the "green,
   nothing filed" line. A manual trigger does not substitute.
3. Fire the `on_failure` handler deliberately once (temporarily point the schedule at a script
   that `exit 1`s, or run the bridge with the SSH key path broken) and confirm the Telegram DM
   arrives; restore.

### S4 — Docs and housekeeping (buildable now)

- `docs/testing-and-build.md`: a short "Nightly" section — what runs, where the clone and logs
  live, the label/one-open-card rule, how to re-run one failing test from the card, and that a
  missing morning card is *not* evidence of green (check `last-run.json` / the Windmill run list).
- `docs/bootstrap.md` line ~33 and `AGENTS.md` "Tests and builds": one bullet each naming
  `u/lndcobra/antiphon_nightly_tests` and "do not add a local Scheduled Task".
- `scripts/nightly-tests.ps1` header: drop "Alert delivery is intentionally not implemented
  here; CARD-0131 S2 owns it"; name CARD-0124 and the isolated-clone rule.
- Cards: **CARD-0131** — close as absorbed by CARD-0124 (S1 shipped and reused; the Telegram
  design replaced by card filing; the "proven to fire" bar carried over to S3). **CARD-0010** —
  the comment CARD-0124's original plan promised ("structurally answered by the nightly").
- Memory `reference_windmill_cleanup_schedule` gains the nightly (path, 00:30, what it files).

## 4. Verification design — the positive control

The claim to prove is not "the schedule exists" but "**a real regression on master becomes a
card with enough in it to act on, without a duplicate the next night, and the card can close**".
Run from this machine, foreground, before S3, using the client suite as the vehicle (fastest, and
`test-client.ps1`'s exit-code contract is pinned) and then once with a .NET test:

1. **Baseline.** `pwsh -File scripts/nightly-run.ps1 -Suites client` against real
   `origin/master`. Expect: clone synced, `npm ci` + `npm run build` + `dotnet build` green,
   client green, **no card** (check `GET /api/cards?boardId=…&status=Backlog` filtered to label
   `nightly` — empty), `last-run.json` written, exit 0.
2. **Break it, for real, on the branch the clone sees.** The clone syncs `origin/master`, so the
   break must be reachable there: push a scratch branch `nightly-positive-control` with
   `expect(true).toBe(false)` added to `client/src/features/board/BoardPage.test.tsx`, and run
   the bootstrap with `-Ref nightly-positive-control` (a parameter added for exactly this; the
   schedule never passes it). Do **not** break master itself.
   Expect: client suite exit 1, a card on the Antiphon board titled
   `Nightly red <date>: client 1 failed`, labels `nightly, tests`, body naming
   `BoardPage.test.tsx > … ` with the assertion line, the log dir, and "New since last run (1)".
   Evidence: `card.ps1 get CARD-nnnn` output pasted into this doc's follow-up.
3. **Same failure again.** Re-run step 2 unchanged. Expect: **no second card**; the same card's
   description has a second run section on top, "Still failing (1)", one discussion line; the
   revision count went up by one.
4. **Fix it.** Delete the scratch branch's bad assertion (or run with `-Ref master`). Re-run.
   Expect: green; the card (still in Backlog, unassigned) is **closed** with the
   `[nightly auto-close] green on … at …` reason; `status: Done`.
5. **Flap guard.** Re-break within the same hour and re-run. Expect: the *same* card is reopened
   (not a new one) and updated; then fix and re-run → closed again.
6. **Do-not-clobber.** Reopen the card by hand, move it to In Progress, run green. Expect: the
   card stays In Progress; a discussion line `green on … not closing because the card is
   InProgress`.
7. **Build class.** Introduce a TypeScript error on the scratch branch (a wrong prop type in
   `TaskDrawer.tsx`, the CARD-0131 precedent). Expect: `npm run build` red, the card is
   `BUILD FAILED`, importance High, body lists the `error TS…` line, and no suite ran
   (table says `skipped: build failed`).
8. **.NET class, once.** Same as 2 with a `1.ShouldBe(2)` in a cheap `Antiphon.Tests` class
   (e.g. `TestClientFilterTests`' neighbour `CheckpointTaskScriptTests`) and `-Suites antiphon`
   — this is the ~30-minute run; do it once. Expect the TUnit `failed …` name and message in the
   body and the `--treenode-filter` re-run line.
9. **Reporting failure.** Run with `ANTIPHON_API=http://127.0.0.1:1`. Expect exit 3,
   `card.md` written next to the logs, nothing filed.
10. **Guard.** `pwsh -File scripts/nightly-tests.ps1 -RepoRoot C:\src\Antiphon -Suites client`
    → exit 3 naming the shared-tree guard, nothing built.

After S3, the first scheduled fire (S3 step 2) is the final piece of evidence; the S2 evidence
is what makes waiting for it safe.

Clean-up: delete the scratch branch; archive the positive-control card with reason
`positive control for CARD-0124` so the board does not carry a fake regression.

## 5. Buildable now versus operator action

| | Who | Needs |
|---|---|---|
| S1 scripts + tests, S2 positive control, S4 docs | a Code delegate on this machine | nothing beyond the repo, the running AppHost on 17202, Docker; ~1 day including the two 30-minute suite runs |
| S3 Windmill script + schedule + proven fire + `on_failure` test | the operator, or any session that can `ssh mc@server2` (this plan session could — the probe worked) | temp superadmin token (skill procedure); then one overnight wait |

Nothing in S1/S2/S4 blocks on S3, and S3 is a 10-minute API session once S1 is on master —
because the bootstrap self-updates from `origin/master`, the Windmill script never needs
changing again for logic changes.

## 6. Deliberately not in scope

- GitHub Actions, PR gates, branch protection (card item 5).
- Telegram for test outcomes (CARD-0131's design — replaced by the card; `on_failure` is only for
  the job itself dying).
- `Antiphon.E2E` on the schedule (D9), headed canaries, `Antiphon.SessionRunner.Tests` /
  `Antiphon.PtyHost.Tests` / `Antiphon.Messaging.Tests` (hosted CI covers the last; the other
  two are CARD-0124's old S2 and are a separate decision — they build under D2 but do not run).
- Deflaking anything the first nights surface — each is its own card; the nightly's job is
  truthful detection with a usable body, not a green machine.
- A local Windows Scheduled Task fallback (rejected again; the pattern exists to avoid it).
