# CARD-0131 — Nightly full-suite run on this machine, with real failure alerting

**Date:** 2026-08-21 · **Status:** plan (build pass not started) · **Parent:** CARD-0124 plan S3
(`2026-08-21-card-0124-ci-feasibility-plan.md`) · **Answers structurally:** CARD-0010 (the
two-months-stale Playwright test nobody heard about).

The card's job: the suites that honestly cannot live in hosted CI — `Antiphon.Tests`,
`Antiphon.Agents.Pty.Tests`, the client vitest suite as a Windows-side backstop, and
`Antiphon.E2E` — run every night on this machine, and a red run produces a Telegram message a
human actually sees, naming the suite and the count. The done-when bar is explicit: the schedule
must be **proven to fire** (not just configured), and the alert path must be **proven to deliver**
via a deliberately-broken test, once.

## Two corrections to the card's text (do not build from the stale assumptions)

1. **CARD-0102 has shipped — all three slices are on master** (`700f084` closed it with acceptance
   evidence). The card still says "the E2E leg targets the LIVE production session-runner daemon
   until CARD-0102's isolation work is built". That is no longer true: `AntiphonAppFixture` now
   starts a dedicated per-fixture runner under `tests/Antiphon.E2E/TestOutput/runner/run-<guid>/`
   and kills it (kill-all → host census → process kill) at teardown, with a crashed-run sweep for
   corpses. The nightly E2E leg targets the isolated runner **from day one**; no alert-text caveat
   about shared production infrastructure is needed, and no later "switch over" step exists. The
   nightly job never touches the always-on daemon on 17204.
2. **Alerting channel is decided (user instruction): Telegram, the `Antiphon-Family` TEST group,
   chat id `-5370465377` — never the live `Family` production group.** Mechanics per
   `docs/telegram-bot-ops.md`: the existing direct-send path `dotnet run scripts/tg-send.cs --
   --to -5370465377 --text "..."` (run from `C:\src\ClaudeBot`) produces a `ChannelReply` straight
   onto Kafka `channels.outbound` at `100.93.77.126:19092` over Tailscale; the always-on
   `am-service` on server2 relays it and logs `[outbound] sent via telegram -> <id>`. The nightly
   alert uses **this same direct-send mechanism** — deliberately NOT the Antiphon agent/channel-
   bridge pipeline (bound agent, session, transcript, delivery verification). That pipeline exists
   for conversational agent replies; routing infra alerts through it would make the alert depend
   on the very stack the tests are judging, which is exactly backwards for a job whose one purpose
   is telling a human the tests failed.

## Ground truth this plan stands on

- **Scheduling pattern is established and reused, not reinvented**: Windmill on server2
  (`server2.tail62cf02.ts.net`, workspace `mc`) already runs the weekly build-junk cleanup —
  script + schedule `u/lndcobra/antiphon_build_junk_cleanup`, bash, worker tag `desktop`, the
  desktop worker container SSHing into Windows (`lndco@host.docker.internal`, key
  `/tmp/windmill/worker_to_windows`) and running `C:\src\Antiphon\scripts\cleanup-build-junk.ps1`.
  CARD-0131 clones this shape with a new script + daily schedule. **No new Windows Scheduled
  Task** — that duplicates a pattern that exists specifically to avoid local-task proliferation,
  and `cleanup-build-junk.ps1`'s own header says so. Windmill API access for wiring: temp
  superadmin token procedure in ClaudeBot memory `windmill-server2`.
- **Hosted CI today** covers client lint/build/vitest + `Antiphon.Messaging.Tests` on every push
  (`.github/workflows/ci.yml`, CARD-0124 S1 — shipped) plus tested-before-publish in
  `publish-nuget.yml`. The CARD-0124 S2 Windows job (PtyHost + SessionRunner tests) is not built
  yet. The nightly deliberately runs only the card's four suites — adding the fast suites here
  would duplicate S1's coverage and blur S2's ownership.
- **Suite timings** (CARD-0124 measurements): `Antiphon.Tests` 14m+ and growing;
  `Antiphon.Agents.Pty.Tests` 2m36s–3m41s, currently flaky; client vitest ~2m warm (~3m cold);
  E2E unmeasured (heaviest; CARD-0102's build pass recorded a wall-time delta worth consulting).
  Whole run with builds and `npm run build`: estimate 45–90 min. Windmill job timeout must be set
  well above worst case (§S3: 4 h) because on Windows OpenSSH a dropped/aborted SSH channel kills
  the remote process tree — a Windmill timeout mid-run would kill the tests *and* the alert step.
- **Exit-code discipline** (CARD-0069): the orchestrator is PowerShell end to end, reads
  `$LASTEXITCODE` from each suite's own process, and never routes a verdict through a Bash pipe.
  Client suite goes through `scripts/test-client.ps1` (which also tees `logs/client-tests.log`
  and prints the unlosable `CLIENT TESTS EXIT CODE: n` line).
- **The daemons hold `bin/`**: all .NET suite builds use
  `--property:OutputPath=bin-nightly/` (forward slash — the trailing-backslash MSB3552 trap is
  documented in CLAUDE.md). One run drops `bin-nightly/` in ~12 project dirs; the orchestrator
  deletes them at the end best-effort, and Monday's Windmill cleanup sweep (`bin-*` wildcard) is
  the backstop for a crashed run.
- **E2E survives the alternate-output build**: `Antiphon.E2E` ProjectReferences the SessionRunner,
  and CARD-0102's plan verified the runner apphost + PtyHost + conpty pair land next to the test
  assembly under `bin-*/` builds too.
- **Headed tests stay skipped**: the nightly does NOT set `ANTIPHON_HEADED_TESTS` — headed
  canaries drive real Claude (cost, interactivity) and belong to deliberate manual runs, not an
  unattended schedule. The 40 skipped in Agents.Pty.Tests and the headed E2E delegation tests are
  out by default, which is correct.
- **Docker** (Antiphon.Tests testcontainer, E2E) is always-on on this machine; **Tailscale**
  (Kafka produce to server2) likewise. Both get a cheap preflight check anyway (§S1).

## Design decisions (resolved here, not deferred)

### D1 — CARD-0128 (flaky Agents.Pty.Tests): ship the nightly NOW; label, don't wait, don't suppress

CARD-0128 is plan-only as of today (`2026-08-21-card-0128-pty-flake-cast-plan.md` — its S1
measurement matrix has not run). The tradeoff:

- **Wait for CARD-0128** buys alert cleanliness on one suite at the price of delaying, for an
  unbounded investigation, the entire alerting backstop for `Antiphon.Tests` (~2000 tests with
  zero CI coverage today), the client suite, and E2E. That inverts the priorities: the CARD-0010
  gap this card exists to close is about the suites nobody runs, not about the one suite whose
  flakiness is already known, carded, and under active work.
- **Ship now and label** accepts some known-flaky red in the alert text, clearly attributed.

**Decision: ship now.** The alert always reports all four suites; a run where the ONLY red is
`Antiphon.Agents.Pty.Tests` is prefixed `KNOWN-FLAKY (CARD-0128)` on that suite's line so the
reader can triage in one glance — but it is still reported red, never swallowed: suppression is
how a real regression in that suite would hide behind the word "flaky", the exact anti-pattern
CLAUDE.md's test rules exist to prevent. Two side benefits make this strictly better than
waiting: each nightly solo run of the pty suite **is** CARD-0128 S1's primary measurement
configuration (machine-as-is, solo), so the nightly generates the flake-frequency series that
card needs for free; and the label has a sunset — **removing it is added to CARD-0128's closing
checklist** (a card comment, §S3 housekeeping), so it cannot quietly become furniture.

### D2 — a green run sends a one-line message too (silent-death insurance)

Failure-only alerting has a failure mode this card was born from: if the schedule stops firing —
Windmill worker down, SSH key rotated, machine asleep — silence looks identical to green, and
nobody watches the Windmill run list. **The nightly always sends**: green is one line
(`nightly GREEN <sha> · 4 suites · NNNN tests · 52m`), red is the full per-suite breakdown. The
missing morning message then becomes the alarm for the pipeline itself dying. One line per night
in a test group that exists for ops verification traffic is acceptable noise; this is the same
"absence must be detectable" principle as the card's own proven-to-fire bar, applied to every
night after the first. If the produce itself fails (tg-send exit 2: broker unreachable), retry
once after 60 s, then write the alert text to the run log and exit non-zero so the Windmill run
goes red as the last-resort surface.

### D3 — git policy in a shared live tree: never disturb in-flight work

The nightly runs in `C:\src\Antiphon` — the same tree delegates and daemons use. At 02:30 a
delegate may be mid-task. Policy: `git fetch origin` always; **pull --rebase only when
`git status --porcelain` is empty AND HEAD is on master**; otherwise run against the tree as it
stands and stamp the report loudly (`DIRTY TREE — ran at <sha> without pull`, or
`ON BRANCH <name>`). Never checkout, never stash someone else's work, never skip the run — a
detection run on slightly-stale code beats no run, and the stamp keeps the alert honest about
what was tested. Same reasoning for npm: **`npm install`, never `npm ci`** — `npm ci` deletes
`node_modules` out from under the always-on Vite dev client on 17203.

### D4 — all four suites run even after an earlier one fails

One alert with the whole picture beats four mornings of peeling the onion. Per-suite failures are
collected, not short-circuiting; strictly sequential throughout (`Antiphon.Tests` then
`Antiphon.Agents.Pty.Tests` is CLAUDE.md's hard never-co-schedule rule; client and E2E also stay
sequential — E2E spawns runners and browsers, and overlap would contaminate CARD-0128's nightly
flake series). Only a *build* failure short-circuits the suites that needed that build, and is
itself reported as the failure.

## Slices

### S1 — the orchestrator: `scripts/nightly-tests.ps1` (suite sequencing, logs, verdict)

New script following the repo's script conventions (comment-block header stating why it exists
and how it is scheduled, ASCII-only per the pwsh-5.1 rule, `$RepoRoot` derived from
`$PSScriptRoot`). Structure:

1. **Preflight**: Docker responsive (`docker info` with timeout), disk space sanity, git policy
   per D3. Record start time, sha, branch, dirty flag into the run context.
2. **Run directory**: `logs/nightly/<yyyy-MM-dd>/` — per-suite logs (`antiphon-tests.log`,
   `agents-pty-tests.log`, `client-tests.log` copy, `e2e-tests.log`), plus `summary.json`
   (per-suite exit code, parsed pass/fail/skip counts, durations, sha, dirty/branch stamps).
   Keep the last 14 days, prune older.
3. **Suites, in order, each a foreground child process whose `$LASTEXITCODE` is the verdict**:
   - `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-nightly/`
   - `dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-nightly/`
   - `pwsh -File scripts/test-client.ps1`
   - `npm run build` in `client/` (E2E precondition — `EnsureClientBundleIsCurrent` hard-fails on
     a stale dist), then `dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-nightly/`
   Failed-count extraction: parse the TUnit/vitest summary line from each log tail; when the
   parse fails, report `exit <code>, counts unparsed — see log` rather than inventing a number.
   The exit code, not the parse, is the red/green verdict.
4. **Cleanup**: delete `bin-nightly/` dirs best-effort
   (`Get-ChildItem -Recurse -Depth 3 -Directory -Filter bin-nightly | Remove-Item ...`), same
   sweep shape as `cleanup-build-junk.ps1`; Monday's Windmill cleanup is the crash backstop.
5. **Exit code**: non-zero iff any suite red or preflight/build failed — this is what Windmill's
   run status reflects, independent of the alert.
6. **Parameters for manual/proof runs**: `-Suites` (subset, e.g. `-Suites client`) and
   `-NoAlert`. The scheduled invocation passes neither.

**S1 verification**: one full manual foreground run from this machine; the honest outcome
recorded (given today's known pty flake, "3 green + Agents.Pty red on its known flaky test" is an
acceptable S1 pass — the orchestrator's job is truthful detection, not a green machine).
`summary.json` fields spot-checked against the logs.

### S2 — alerting: the sender step + the deliberately-broken-test proof

1. **Alert step inside `nightly-tests.ps1`** (runs unless `-NoAlert`), composing from
   `summary.json` per D1/D2. Red format, per the card's requirement to name suite and count:

   ```
   nightly RED  6ef9b8a · 2026-08-22 02:30 · 61m
   Antiphon.Tests            FAIL  3/2011 failed        logs/nightly/2026-08-22/antiphon-tests.log
   Antiphon.Agents.Pty.Tests FAIL  1/276 failed  KNOWN-FLAKY (CARD-0128)
   client (vitest)           pass  459
   Antiphon.E2E              pass  13 files
   ```

   Delivery: `dotnet run scripts/tg-send.cs -- --to -5370465377 --text-file <composed>` with cwd
   `C:\src\ClaudeBot` (`--text-file` sidesteps quoting; the cross-repo call is the decided reuse —
   copying the sender into Antiphon would fork a working tool). Success = tg-send exit 0
   (`produced -> channels.outbound...`); on exit 2, one retry after 60 s, then log-and-exit-nonzero
   per D2. The chat id is passed as a script constant with the "never the live Family group"
   warning beside it.
2. **The proof, per the card's done-when** ("a deliberately-broken test has been used once"):
   on a scratch branch, add a trivially failing client test (`expect(true).toBe(false)`), run
   `pwsh -File scripts/nightly-tests.ps1 -Suites client` — the real orchestrator path end to end:
   suite runs, exit 1 captured, count parsed, message composed, tg-send produces, and the message
   **arrives in Antiphon-Family naming `client` and `1 failed`**. Screenshot/`[outbound]` log line
   recorded as card evidence; branch deleted. The client suite is the probe vehicle because it is
   the fastest of the four and `test-client.ps1`'s exit-code contract is already pinned.
3. Also send one green run's one-liner (can be the same session, `-Suites client` after revert)
   to prove the D2 format.

### S3 — Windmill schedule on server2 + the proven scheduled fire + housekeeping

1. **New Windmill script** `u/lndcobra/antiphon_nightly_tests` (bash, worker tag `desktop`) —
   same shape as the cleanup job: SSH `lndco@host.docker.internal` with
   `/tmp/windmill/worker_to_windows`, run
   `pwsh -NoLogo -File C:\src\Antiphon\scripts\nightly-tests.ps1`, propagate the exit code.
   **Schedule**: daily **02:30 Europe/London** (clear of the operator's day and of Monday's
   09:00 cleanup). **Job timeout: 4 h** — must exceed the worst-case run because SSH-channel
   death kills the remote tree including the alert step (ground truth above).
2. **Proven to fire, per the card**: after wiring, wait for the first *scheduled* execution (no
   manual trigger substitutes) and record: the Windmill run log, the `logs/nightly/<date>/`
   artifacts on this machine, and the morning Telegram message. That first scheduled fire is the
   full end-to-end done-when evidence — which is why alerting (S2) lands before the schedule
   (S3), not after.
3. **Docs + housekeeping**:
   - `scripts/nightly-tests.ps1` header documents the Windmill ownership ("scheduled via
     u/lndcobra/antiphon_nightly_tests on server2 — do not add a local Scheduled Task"), mirroring
     `cleanup-build-junk.ps1`'s convention.
   - CLAUDE.md/AGENTS.md: one bullet in the always-on/backing-services area naming the nightly,
     its schedule, where its logs land, and the D2 rule that a missing morning message is itself
     an alarm.
   - Card comments: CARD-0010 ("structurally answered by CARD-0131's nightly-with-alerting" — the
     note CARD-0124's plan promised), CARD-0128 (back-reference: nightly solo runs feed S1's
     measurement series; closing checklist gains "remove the KNOWN-FLAKY label from
     nightly-tests.ps1"), CARD-0124 (S3 shipped).

## Deliberately not in scope

- **CARD-0124 S2** (hosted Windows job for PtyHost/SessionRunner tests) and S4 (pty suite into
  hosted CI) — separate slices of the parent plan; the nightly neither replaces nor blocks them.
- **Deflaking Agents.Pty.Tests** — CARD-0128's work; this card only labels (D1) and feeds it data.
- **Headed/canary suites** — excluded by not setting `ANTIPHON_HEADED_TESTS`; deliberate.
- **Routing alerts through the Antiphon agent pipeline or the `Telegram__AllowedChatIds`
  hardening** — the direct-send decision is correction #2; the allowlist gap stays the open
  hardening task `telegram-bot-ops.md` already records.
- **A local Windows Scheduled Task fallback** — explicitly rejected (established Windmill
  pattern); if Windmill is ever decommissioned, that is the moment to revisit, not before.
- **Green-run silence tuning** — if the daily one-liner proves annoying, the revisit is a weekly
  digest + failure-only, but only once something else watches for schedule death; D2's rationale
  must not be silently dropped.
