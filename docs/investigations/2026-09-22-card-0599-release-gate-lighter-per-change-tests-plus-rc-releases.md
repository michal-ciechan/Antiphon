# CARD-0599: Release-gate model — lighter per-change tests + periodic full-test RC releases

Date: 2026-09-22. Stage: Investigate. **Confirmed from source, live Windmill/GitHub censuses and on-disk
state. Next: Plan.**

The half of this card that asks for *lighter per-change testing* is already built and landed
(CARD-0544/CARD-0585) and is sitting **dormant** behind an operator-owned qualification step that has
never been performed. The half that asks for *periodic full-test runs* is also already built
(CARD-0487/CARD-0545 nightly) and has **never once run on schedule** — the Windmill job it depends on
does not exist in Windmill, and the last native attempt was 2026-09-04 on a feature ref, exit 1.
Meanwhile the only automated per-change gate that does exist, GitHub Actions `ci.yml`, has been **red
on master since 2026-09-01** and is ignored. There are **zero git tags and zero GitHub Releases** in
this repository, so the release/versioning half of the ask is genuinely greenfield.

The mechanism CARD-0599 wants is therefore mostly an *activation and re-pointing* problem, not a
build-from-scratch problem — with three real new pieces: an RC ref concept, a release identity, and a
way to execute the suites the nightly currently cannot execute at all.

No production changes, no policy activation, no schedule registration, no test executions, no builds,
no service restarts and no mutations were performed in this investigation. Every database query was
`BEGIN READ ONLY`.

## Evidence scope

- Inspected worktree `C:\Antiphon\worktrees\card-task-5e5aaedb`, branch `feat/card-task-5e5aaedb`,
  HEAD `bb89e77b4dcc30feb809e1549d62653890a0311a`.
- Live `GET http://localhost:17202/api/version` at inspection time:
  `{"version":"bb89e77b4dcc30feb809e1549d62653890a0311a","informationalVersion":"1.0.0+bb89e77b…","capabilities":["land-v2"]}`.
- Live Windmill census: `docker exec windmill-windmill_db-1 psql -U windmill -d windmill`, read-only,
  on server2, 2026-09-22.
- Live GitHub census: `gh release list`, `gh api repos/michal-ciechan/Antiphon/tags`,
  `gh run list --branch master`.
- Card text read with `scripts/card.ps1 get CARD-0599` (and 0544, 0545, 0585, 0487, 0592).

---

## 1. `tests/test-execution-policy.json` — how suites are categorized and gated today

The file (`tests/test-execution-policy.json`, 125 lines, `schemaVersion: 1`) is the **nightly runner's**
suite registry. It is not consulted by the land gate, by GitHub Actions, or by a delegate's per-change
work; nothing in `server/` reads it.

`defaultSuites` (`:4-12`) is the unattended universe: `antiphon`, `session-runner`, `pty-host`,
`agents-pty`, `messaging`, `client`, `scripts`. Eight suites are defined in `suites` (`:18-83`); the
eighth is `e2e`, which is `"mode": "manual"` with `"reason": "requires fresh client/dist and isolated
random runner"` (`:75-82`) and is deliberately absent from `defaultSuites`.

Per-suite fields that matter to a release gate: `project`, `assembly`, `mode`
(`unattended`|`manual`), `processGroup` (`native-dotnet` groups run one at a time), `slowRegistry`,
and for `messaging` a `"broker": "disposable-container"`. `watchdogs` (`:109-120`) sets per-suite
timeouts — `antiphon` 3,600,000 ms (60 min), everything else 20 min, builds 10–20 min.

**Finding 1a — `mode: manual` has no execution path at all, not even an explicit opt-in.**
`docs/testing-and-build.md:381` says "`-Suites e2e` is a manual opt-in", which reads as though naming
the suite runs it. It does not. `Resolve-NightlySelectedSuites` (`scripts/lib/nightly-policy.ps1:212-242`)
accepts `e2e` as a valid id and returns it in the selection, and then the executor's suite loop
unconditionally short-circuits it:

```
scripts/lib/nightly-tests-impl.ps1:306-318
    foreach ($suiteId in $selectedSuites) {
        ...
        if ($mode -eq 'manual') {
            $suiteResults += ... result = 'manual'; exitCode = 0
                coverageComplete = $false; detail = 'manual exclusion'
            continue
        }
```

There is no `-IncludeManual`, no `-AllowManual`, no env override. So **the nightly harness cannot
today execute `Antiphon.E2E` under any invocation**, and any suite it does skip this way also forces
`coverageComplete=false` for that entry. A release gate that must "run the full suite" needs this
executed lane built; it is the single largest missing capability for requirement (3) of the card.

**Finding 1b — `"reducedDispatchPolicy": "inactive"` (`:124`) has no consumer.** `grep -rn
reducedDispatchPolicy .` over the whole repository returns exactly one hit: the policy file's own
line. It is a documentation marker only.

**Finding 1c — category distribution** (source-attribute census over `tests/**/*.cs`, class- and
method-level combined): `Integration` 577, `Unit` 238, `Slow` 64, `OptIn` 59, `Headed` 42,
`HeadedCanary` 28, `Pty` 22, `RealCliStubProxy` 10, `PtyHost` 9, `GitIntegration` 4, `PtyStress` 1,
`HeadedLong` 1. `Antiphon.E2E` carries **no** `Unit`/`Integration`/`Slow` at all — 20 `OptIn`
declarations and nothing else, consistent with its manual status. Raw `[Test]` attribute counts per
project: `Antiphon.Tests` 7,476; `Antiphon.SessionRunner.Tests` 724; `Antiphon.Agents.Pty.Tests` 501;
`Antiphon.Messaging.Tests` 205; `Antiphon.PtyHost.Tests` 116; `Antiphon.E2E` 96. (Source attributes,
not expanded TUnit cases — lower bounds.)

**Finding 1d — the Slow registries are nearly empty outside `Antiphon.Tests`.**
`tests/Antiphon.Tests/slow-tests-allowlist.txt` is 123 non-blank lines (FQN + adjacent reason);
`tests/Antiphon.SessionRunner.Tests/slow-tests-allowlist.txt` names three classes; the other four
files are comments only, declaring "no Slow classes at initial classification". So "move the Slow
lane to release-only" would today move almost nothing outside `Antiphon.Tests`, and
`docs/testing-and-build.md` is explicit that "Slow is a cost marker, never Skip".

---

## 2. CARD-0585's checkpoint manifest — what it already narrows, and what it deliberately does not

`docs/testing-and-build.md:109-152` ("Checkpoint manifest (CARD-0585)") is live doctrine, mirrored in
`AGENTS.md` and in `server/Bundles/stage-code.md:5`. Its shape: a Plan/TestDesign artifact ends its
`## Verification design` with a `### Checkpoints` table, one row per (isolated build + one exact test
filter), each bound to the plan slice that must be committed first and to the V-n/R-n IDs it covers.
Code runs that table as a **closed list**; any other build or test command is "unlisted" and must be
reported with a reason. `scripts/run-checkpoint.ps1` emits the canonical one-line result per row.

**Finding 2a — CARD-0585 removes *redundant* work, not *coverage*.** The doc states this in terms
Plan should not re-litigate: it "removes the extra rebuilds (CARD-0490 ran 19 builds for 8 test runs),
the hunting for files the plan already named, and the second Code round that CARD-0459 paid for; **it
does not shrink the named Slow/native V/R work, which is the coverage itself**"
(`docs/testing-and-build.md:111`). CARD-0599's "lighter per-change testing" is therefore a
*different* lever from CARD-0585 — the checkpoint manifest is about not doing the same work twice;
CARD-0599 is about deferring a category of work to a later gate. The two compose cleanly and Plan
should not attempt to express CARD-0599 as a checkpoint-table change.

**Finding 2b — the same doc paragraph already reserves the broad run for this card's target.**
`docs/testing-and-build.md:83`: "**CI/nightly keep the broad run.**" and `:151`: the `### Cost`
block's ordinary Code floor is the sum of the table's `Min` column. The doctrine already assumes a
periodic broad backstop exists. It does not.

**Finding 2c — the real per-change lever already exists and is named `Interim`.** CARD-0544 landed a
complete interim-vs-final verification profile: `server/Bundles/stage-code.md:7` and
`server/Bundles/stage-review.md:5` both carry a `ROUND:` paragraph. `Final` (the default, and always
the first round) is "whole Unit lane, every full affected class, every ordinary V/R, required manual
work". `Interim` (explicit only) is "cumulative changed cases since the full baseline incl. earlier
repair cases, unresolved-finding tests, named adjacent smoke; unbounded shared impact needs Final",
with deferred IDs listed and never marked passed. A Final Review "reruns the complete ordinary scope
itself, including every row an Interim round deferred". **This is exactly CARD-0599 requirement (1),
already shipped.**

---

## 3. The lighter-per-change machinery is shipped and dormant

`server/Application/Settings/InterimVerificationSettings.cs` is the gate. Its own XML doc
(`:3-10`): "Disabled by default: with `Enabled` false no Interim task can be admitted or launched,
whatever a card's policy or a caller's request says. It is an additional operator gate… Enabling it
is CARD-0544 S6's job and requires accepted CARD-0545 qualification first."

| Setting | Meaning | Current value |
|---|---|---|
| `Enabled` | master switch | **false** — absent from every `appsettings*.json`, so the bool default stands |
| `CanonicalRepositoryPath` | the one qualified repo | unset |
| `ProjectId` | the qualified project | unset |
| `StateRoot` | trusted nightly state root holding the qualification receipt and `last-monitor.json` | unset |
| `MonitorFreshMinutes` | monitor staleness bound | 60 (default) |

`grep -rln InterimVerification --include=appsettings*.json .` returns **nothing**. Both
`Antiphon.AppHost/appsettings.json` and `server/appsettings.json` lack the section entirely.

The enforcement path is real, not decorative: `AgentTaskService.cs:211-219` resolves the round and
calls `InterimVerificationPolicy.RequireInterimShape` / `RequireExplicitIdentities`; `:947-948`
refuses with `BackstopUnreadyCode` when readiness is absent; `AgentTaskDispatcher.cs:4616` returns
"interim verification is unavailable in this host". `docs/ops-http.md:329` records the live state as
"CARD-0544 (dormant: `InterimVerification:Enabled=false`, every card `FullOnly`)".

CARD-0544's own close revision confirms it: *"Ships fully DISABLED: `InterimVerification:Enabled=false`,
every card `FullOnly` — the interim-scoped verification policy itself does not activate until
CARD-0545 (deferred notification controls) is qualified and this card's own S6 pilot runs."* CARD-0545
closed the same way: *"S6 (actual deploy, Windmill registration, token, reader login, live
qualification) remains explicitly operator-owned and untouched."*

---

## 4. `scripts/nightly-run.ps1` and `u/lndcobra/antiphon_nightly_tests` — live state

### What the code does

`scripts/nightly-run.ps1` (98 lines, ASCII-only for PS 5.1) is a thin param/dispatch shell over
`scripts/lib/nightly-run-impl.ps1`. Relevant parameters, all already present:

- `-Ref` (default `master`) — the git ref the isolated clone is reset to. `nightly-run-impl.ps1:254`
  does `git fetch origin $Ref`; `:337` labels the run `origin/$Ref`. **An RC branch can already be
  targeted with no code change.**
- `-Trigger` — `scheduled` (default), `manual`, or `feature`.
- `-Suites` — forwarded to `nightly-tests.ps1`; default from the policy file.
- `-CheckoutRoot` (default `C:\Antiphon\nightly\checkout`), `-StateRoot` (default `C:\Antiphon\nightly`).
- `-NoReport`, `-NoSync`, `-SeamsPath`, `-RunId`, `-PassThru`, `-WhatIf`.

It syncs an isolated clone (never `C:\src\Antiphon`, never a worktree), self-re-execs the clone's own
copy of the script with `-NoSync` if it differs from the ref, then builds (`npm ci`, `npm run build`,
client lint, `dotnet build Antiphon.sln -c Debug`) and runs the policy suites sequentially with
native project groups one at a time (`scripts/lib/nightly-tests-impl.ps1:295-330`). On red it files or
updates **one** `nightly`-labelled Antiphon-board card; green auto-closes it only when still Backlog
and unassigned (`scripts/nightly-report.ps1`). Its last stdout line on every exit path is a compact
JSON completion record `{nativeRunId, sha, ref, trigger, localDueDate, policyHash, coverageComplete,
testsPassed, reportDelivered, exitCode, summaryPath}` (CARD-0545 D-10), which Windmill stores as the
job result.

**Finding 4a — the complete-green predicate hard-refuses any ref that is not master.**

```
scripts/lib/nightly-run-impl.ps1:30-40
function Test-NightlyCompleteGreenPredicate {
    ...
    if (-not [bool]$State.coverageComplete) { return $false }
    if (-not [bool]$State.testsPassed) { return $false }
    if (-not [bool]$State.reportDelivered) { return $false }
    $ref = [string]$State.ref
    if ($ref -ne 'master' -and $ref -ne 'origin/master') { return $false }
    $trigger = [string]$State.trigger
    if ($trigger -ne 'scheduled') { return $false }
```

So `-Ref release/rc-2026-09-22a` runs fine but can **never** advance `last-complete-green.json`, and
`docs/testing-and-build.md:330-345` builds the whole readiness verdict on top of that predicate plus a
London *due date* and a 08:00 cutover — a once-nightly shape, not a twice-daily one. Repurposing the
nightly for RC runs means changing this predicate and the due-date model, and deciding whether an RC
green is the same kind of credit as a master green. That is the main structural change Plan must own.

### What is live

| Observation | Result (2026-09-22) |
|---|---|
| Windmill schedules matching `%antiphon%` | 3 rows: `antiphon_build_junk_cleanup` (`0 0 9 * * 1`), `antiphon_codex_residue_cleanup` (`0 30 9 * * 1`), `antiphon_github_sync` (`0 0 */3 * * *`). All enabled. |
| Windmill schedules matching `%nightly%` (path or script_path) | **0 rows** |
| Windmill `script` rows matching `%nightly%` | **0 rows**, archived included |
| Windmill `script` rows matching `%antiphon%` | 4 rows — the three cleanup/sync scripts (one archived github_sync version). No `antiphon_nightly_tests`, no `antiphon_nightly_readiness`. |
| All schedules in the instance | 9 rows total; none nightly-related |
| `C:\Antiphon\nightly\last-run.json` | sha `829e516a797044a354894790c06d6f8ebb7944e8`, ref **`feat/card-task-2aab4b1c`**, `succeeded: false`, `testExit: 1`, `exitCode: 1`, completed `2026-09-04T17:56:37+01:00` — **18 days stale, a feature ref, and red** |
| `C:\Antiphon\nightly\last-complete-green.json` | **absent** |
| `C:\Antiphon\nightly\last-monitor.json` | **absent** (the file the server's readiness reader requires) |
| `C:\Antiphon\nightly\readiness-config.json` | **absent** |
| `C:\Antiphon\nightly\watchdog-deploy.json` | **absent** |
| `docs/investigations/*card-0487*qualification*.md` | **absent** (only `2026-09-11-card-0487-testdesign-discovery.md` exists) |

The checked-in definitions exist and are correct — `scripts/windmill/antiphon-nightly-tests.schedule.json`
(`0 30 0 * * *`, Europe/London, `desktop` tag, empty args, enabled) and
`antiphon-nightly-readiness.schedule.json` (`0 */30 * * * *`, desktop) — but
`scripts/windmill/README.md` is explicit: *"Registration is an operator step. Do not infer live
registration from these files."* The census confirms it has never happened. Note also that the
job's bash content SSHes to `lndco@host.docker.internal` and runs
`C:\src\Antiphon\scripts\nightly-run.ps1` — the **main checkout's** copy, which then syncs the
isolated clone.

This is the same finding CARD-0544's investigation recorded on 2026-09-16. **Six days later nothing
has changed.** The pattern is worth naming for Plan: this infrastructure has now been built three
times over (CARD-0124, CARD-0487, CARD-0545) and never turned on, because every card's activation
step was scoped as operator-owned.

---

## 5. "What's live" today, and the total absence of any release concept

### The per-change gate that machines actually enforce

**At land:** `AgentTaskLandService.VerifyWithObserverAsync` (`server/Application/Services/AgentTaskLandService.cs:1012-1033`)
runs `dotnet build --artifacts-path <temp>` and then, **only if a filter was supplied**, one
invocation of `dotnet run --project tests/Antiphon.Tests -- --treenode-filter <filter> --report-trx`,
requiring exactly one fresh TRX with `executed != 0`, `passed == executed`, `failed == 0`.

```
:1021  if (!build.Ok) return LandVerification.Failure("build", Tail(build));
:1022  if (string.IsNullOrWhiteSpace(filter)) return LandVerification.Success("build OK");
```

The filter comes from the caller's optional `-Verify` (`scripts/delegate.ps1:258-259`), whose own
comment reads *"-Verify is an optional treenode filter; **the full suite is deliberately not a gate**"*
(`:254`). `AgentTaskLandingProtocol.cs:235` can skip verification entirely when the base is unchanged
and no filter was requested. Even at its strongest, the land gate only ever runs `Antiphon.Tests` — it
can never run the client, E2E, messaging, pty or scripts suites.

**So the machine-enforced per-change gate is already build-only-by-default.** The weight CARD-0599
wants to remove lives almost entirely in the *agent stage contract* (`stage-code.md`,
`stage-review.md`, the plan's `### Checkpoints` table), not in tooling — which is precisely what
CARD-0544's `Interim` profile was built to control.

**At push:** `.github/workflows/ci.yml` runs on push to `master` and `feat/**`. Two jobs: `ci`
(ubuntu — `npm ci`, `npm run lint`, `npm run build`, `npx vitest run`, then
`dotnet run --project tests/Antiphon.Messaging.Tests`) and `windows-native-tests` (windows-latest —
`Antiphon.PtyHost.Tests`, `Antiphon.SessionRunner.Tests`). It **never runs `Antiphon.Tests`** (the
7,476-attribute assembly), never runs `Antiphon.E2E`, and never runs `Antiphon.Agents.Pty.Tests`.

**Finding 5a — GitHub Actions CI has been red on master for 21 days.** Last success on master:
`ffe5ee89`, **2026-09-01T09:33:25Z**. Every run since has failed; 30 consecutive failures sampled back
to 2026-09-19 plus the 2026-09-01 boundary. The latest master run (`35786565547`, `bb89e77b`, 7m41s)
fails **both** jobs:

- `ci` → *Lint client*: `✖ 10 problems (10 errors, 0 warnings)` — React compiler rules
  ("Calling setState synchronously within an effect can trigger cascading renders", "Cannot access
  refs during render" ×8) plus `react-refresh/only-export-components`. Exit code 1.
- `windows-native-tests` → *Test session runner*: `CodexLaunchException: codex_launcher_unavailable:
  the Codex cmd launcher was not found beside the working directory or on PATH`, plus a cluster of
  `ShouldAssertException: File.Exists(executable)` failures (`C461_G083/G084/G085`,
  `Isolated_backend_closes_only_reviewed_incarnation`).

These are environment-assumption failures (tests that require locally-installed CLIs and native
executables that a hosted runner does not have) plus a genuine client lint regression — not, on this
evidence, a product regression. But the practical consequence is that **the repository's only
always-on per-change signal is noise and has been treated as noise for three weeks.** Any release-gate
design that assumes "master is green by CI" is designing against a false premise.

### The release/versioning concept: there is none

| Probe | Result |
|---|---|
| `git tag -l` (local) | **0 tags** |
| `git ls-remote --tags origin` | **empty** |
| `gh api repos/michal-ciechan/Antiphon/tags` | `[]` |
| `gh release list` | **empty** |
| `git branch -r` matching `rc\|release\|stable\|prod` | **0** (631 remote branches, overwhelmingly `feat/card-task-*`) |
| Any `gh release create` / `git tag` in tracked scripts | **none** — the only `release`/`tag` grep hits are unrelated (`node-releases` in a lockfile, `StageExecution`'s "git tag" comment, Codex fixture screen captures) |

The nearest thing to a version is `Directory.Build.props`' CARD-0179 R3 target, which stamps
`SourceRevisionId` from `git rev-parse HEAD` into `AssemblyInformationalVersion`. `GET /api/version`
returns that: a bare SHA, an `informationalVersion` of `1.0.0+<sha>` where `1.0.0` is an unmanaged SDK
default nobody bumps, and a `capabilities` array (currently `["land-v2"]`) used for feature probing.

"What's live" is therefore **a SHA compared against the canonical checkout's HEAD**, formalised as the
CARD-0495 post-land server activation check (`docs/orchestration-loop.md:86-104`): a land confirms
publication, not activation; `restart-apphost.ps1` exit 0 requires dashboard + `/health` +
`/api/version` SHA equal to source-root HEAD (or `-ExpectedServerSha`), with a mismatch as exit 5.
`/health` alone, a runner SHA, a pushed branch and a succeeded delegate are all explicitly rejected as
activation evidence. The one published-artifact concept in the repo is orthogonal: NuGet packaging of
the `Antiphon.Messaging*` libraries (`.github/workflows/publish-nuget.yml`), versioned by hand in
`src/Messaging.Pack.props`, triggered by path-filtered pushes to master, tagless.

There is real design space here for Plan and no legacy to preserve: an RC tag/release is additive and
can carry the SHA that `/api/version` already reports, making "what's live" answerable as a release
name rather than a SHA for the first time.

---

## 6. What a lighter gate would exclude, and what CARD-0599 moves to release-only

Current exclusion status of every lane, from the policy file and the executors:

| Lane | Runs per-change today? | Runs in nightly today (if it ran)? | Candidate for release-only |
|---|---|---|---|
| `Antiphon.Tests` Unit (203 class-level `Unit`) | Yes — Code/Review ordinary, plus optional land `-Verify` | Yes | No. This is the fast lane; keep per-change. |
| `Antiphon.Tests` Integration (562 class-level) | Yes — "named affected integration classes" per plan | Yes | Partly. The *unnamed* remainder is the real per-change saving. |
| `Antiphon.Tests` Slow (123-entry registry) | Yes, when named; Slow is a cost marker, never Skip | Yes | Yes — the clearest release-only candidate. |
| `Antiphon.SessionRunner.Tests` (724 `[Test]`, 3 Slow classes) | Only via GH CI (red) | Yes | Partly (the 3 Slow classes). |
| `Antiphon.PtyHost.Tests` (116) | Only via GH CI (red) | Yes | No — cheap and native-critical. |
| `Antiphon.Agents.Pty.Tests` (501, 31 `OptIn`) | No automated gate | Yes (sequential with `antiphon`) | The `OptIn`/vendor-canary subset, yes. |
| `Antiphon.Messaging.Tests` (205) | Yes via GH CI ubuntu (red) | Yes, with `ANTIPHON_BROKER_TESTS=1` + disposable broker | No. |
| `client` (vitest, 102 test files) | Yes via GH CI ubuntu (red at lint) | Yes (`requires: client-build, client-lint`) | No. |
| `scripts` (14 census'd PS harnesses) | No automated gate | Yes | No — cheap. |
| **`Antiphon.E2E` (96 `[Test]`, 20 `OptIn`)** | **No** | **No — `mode: manual`, unconditionally skipped** | **Already release-only in effect; needs an executor built.** |
| `Headed` (42) / `HeadedCanary` (28) / `HeadedLong` (1) | No | No — opt-in env names are *cleared* in nightly child environments | Release-only, needs an eligibility decision. |
| CARD-0490 QEMU native (PC-28–31) | No — Mutation-stage only | No | Out of scope; SourceLanding owns it. |
| CARD-0590 Linux/Docker roster (503 backend classes, `tests/linux-test-roster.json`) | No | No | New lane, separate card. |
| `RequireLinux()` classes (e.g. `LinuxPtyHostLauncherTests`) | Skip on this Windows host until CARD-0605 | Skip | Blocked on CARD-0605. |

The honest summary: **almost nothing runs automatically per-change today except a red CI workflow and
a build.** The per-change weight CARD-0599 wants to cut is delegate wall-time and token spend
governed by the stage bundles — which the dormant `Interim` profile already addresses — and the
"full run" it wants at the release gate does not currently exist in an executable form for E2E or
headed lanes.

---

## Confirmed mechanism and remaining uncertainty

**Confirmed.** (1) CARD-0599's requirement (1) is CARD-0544's `Interim` verification profile, landed
and disabled at `InterimVerificationSettings.Enabled=false` with no config section anywhere, gated on
CARD-0545 S6 qualification that has not been run (no receipt artifact, no `last-monitor.json`, no
readiness config, no watchdog deploy profile). (2) Requirement (3)'s natural host, `nightly-run.ps1`,
already accepts `-Ref` and `-Trigger` and can be pointed at an RC branch unchanged, but its
complete-green predicate (`nightly-run-impl.ps1:30-40`) refuses any ref but master and any trigger but
`scheduled`, and its whole readiness model is once-nightly-by-London-due-date. (3) The Windmill jobs
that would run it do not exist in the instance — 0 schedule rows, 0 script rows, archived included —
and the last native attempt was 2026-09-04 on a feature ref, exit 1. (4) There is no git tag, no
GitHub Release, no release branch and no versioning scheme; "what's live" is a SHA plus a
`capabilities` array, enforced by `restart-apphost.ps1`'s exit-5 SHA equality and the CARD-0495
activation check. (5) `Antiphon.E2E` is `mode: manual` and has **no** execution path in the nightly
harness under any flag; headed lanes are actively cleared from nightly child environments. (6)
GitHub Actions CI, the only always-on per-change gate, has failed on master on every run since
2026-09-01 (last green `ffe5ee89`), on both jobs, for a client-lint regression and for missing
host tooling.

**Remaining uncertainty for Plan.**

1. **Does an RC green earn interim-verification credit?** `InterimVerificationReadinessReader` consumes
   `last-monitor.json` + `last-complete-green.json` under a master/scheduled/London-due-date model. Whether
   a twice-daily RC green substitutes for, supplements, or is separate from that receipt is a design
   decision with a real correctness consequence (a stale-credit bug here silently lightens every
   dispatch). Not resolvable by reading source.
2. **The E2E executor is unbuilt work of unknown size.** No measurement exists of what
   `Antiphon.E2E` costs to run to completion, because nothing has ever run it as a suite. Its fixtures
   need fresh `client/dist` (`AntiphonAppFixture.EnsureClientBundleIsCurrent` hard-fails on a stale
   bundle) and an `IsolatedSessionRunner` on a random port. Sizing this needs one manual run.
3. **Whether CI should be fixed, narrowed, or deleted.** Three weeks of red is a policy fact, not an
   accident, and CARD-0599's gate design depends on whether GH Actions is meant to be part of the
   per-change gate at all. This is an operator call.
4. **No dollar or wall-clock saving is established here.** CARD-0544's investigation measured 459.43
   task-minutes / $158.70 across nine CARD-0527 repair rounds with 233.90 minutes of first-matrix wall
   time, and explicitly refused to multiply those into a saving. Nothing in this investigation improves
   on that; a paired Interim-vs-Final run at the same SHA would be needed.
5. **Whether the operator-owned activation step is the actual blocker.** Three cards have now built
   this infrastructure and stopped at "operator registers the job". If CARD-0599 scopes activation the
   same way, the evidence says it will also not happen. Whether a delegate may register a Windmill
   schedule and mint its token is a custody decision the operator owns
   (`docs/nightly-watchdog.md:141-152`, `scripts/windmill/README.md`).

## Not done, noted

A fix idea for the largest gap, one line each, for Plan to accept or discard:
add an `includeManual`/`releaseGate` suite disposition to `test-execution-policy.json` so `e2e` has an
executable path; relax `Test-NightlyCompleteGreenPredicate` to accept a configured RC ref prefix with
its own credit class distinct from master's scheduled green; name releases from the SHA
`/api/version` already reports, so `capabilities` and release identity stay one object.
Repairing the red GitHub Actions CI, and registering the Windmill jobs, are separate commissioned
work — neither belongs to this stage.

## Reproduction

Every claim above re-derives from, in order: `cat tests/test-execution-policy.json`;
`sed -n '109,152p' docs/testing-and-build.md`; `sed -n '30,40p' scripts/lib/nightly-run-impl.ps1`;
`sed -n '306,318p' scripts/lib/nightly-tests-impl.ps1`; `sed -n '1012,1033p'
server/Application/Services/AgentTaskLandService.cs`; `cat
server/Application/Settings/InterimVerificationSettings.cs`; `git tag -l` and `gh api
repos/michal-ciechan/Antiphon/tags`; `gh run list --repo michal-ciechan/Antiphon --branch master`;
`cat C:\Antiphon\nightly\last-run.json`; and, on server2, `docker exec windmill-windmill_db-1 psql -U
windmill -d windmill -c "BEGIN READ ONLY; SELECT path, schedule, enabled FROM schedule ORDER BY
path;"` with the companion `script` query. No test pass claim is made for this investigation;
verification consisted of source tracing, read-only censuses and on-disk state checks.

--- next stage ---
next: plan
handoff: CARD-0544's Interim profile already IS the lighter per-change gate, landed but dormant (InterimVerification:Enabled=false, no config section, CARD-0545 S6 unrun); nightly-run.ps1 already takes -Ref but its complete-green predicate refuses non-master, its Windmill jobs are unregistered, E2E has no executable path at all, and there are zero git tags or Releases. Plan the RC ref, release identity and activation path.
artifact: docs/investigations/2026-09-22-card-0599-release-gate-lighter-per-change-tests-plus-rc-releases.md
