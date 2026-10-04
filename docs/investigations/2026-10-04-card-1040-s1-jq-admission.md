# CARD-1040 S1: jq admission and retained Unit timing

S1 documentation is complete; durable image qualification and S2 consumer proof
remain pending. This report claims no fresh test pass or image activation.

Original Code / landing owner: `d20ed3ac-b453-46d7-b312-822a9fec5be4`.
Branch: `feat/card-task-d20ed3ac`.
Worktree: `/work/worktrees/task-d20ed3ac`.
Task base: `0e9ce38484148e38428da540d901b29f4a0796e5`.
Documentation slice: `33b323ada7fe00c44fab49a5407bdeaaa1b0773a`, pushed.
Plan/TestDesign: [frozen plan](../superpowers/plans/2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md),
landed at `5b46e3263a97435d88617ee6ea99541e9b320ab1`.

## Delivered and inspected

[Testing owner](../testing-and-build.md#jq-qualification-and-bounded-consumer-proof-card-1040)
now distinguishes child shell, executing image and outer host qualification;
specifies same-child PATH/uid/binary custody; preserves required-jq admission;
points to the exact fifteen-method proof and post-land PC; and separates slot,
build, startup, method interval and teardown costs. It explicitly explains that
the manifest's `After=S1` rows execute during S2 after image qualification.

Read/reused unchanged: `docker/session-runner-grok/Dockerfile`,
`docker/session-runner-grok/verify-codex-image.sh`,
`scripts/verify-card0660-codex-image.ps1`, and
`tests/Antiphon.Tests/Infrastructure/JqRunnerImageContractTests.cs`.
The pin, checksum-before-install, root-owned installation and exact version row
already exist; no concrete source repair was found or commissioned. No production,
test, image, rollout or timeout source was changed. No new test is warranted for
this documentation-only slice under the plan's S1 instructions.

Read-only checks: `git diff --check` passed; the pre-existing jq receipt subsection
is preserved byte-for-byte; all four repository-relative links in the new
subsection resolve. Full task-range `scripts/check-evidence-diff.ps1` is required
against the final pushed HEAD; its actual result is recorded in the task report.

## Fresh provisional admission observation

At `2026-10-04T22:04:26Z`, source was clean at the task base above. The diagnostic
used foreground `exec_command(login=false)` and native `bash -c`, inherited PATH,
without installing anything or changing the parent environment. This is a
non-login diagnostic; S2 must repeat qualification in its actual launcher/child
environment. No credential/environment dump or Docker inspection was performed.

| Observation | Actual result |
|---|---|
| OS / architecture / uid | Linux / x86_64 / 1654 |
| bash, resolved | `/usr/bin/bash` -> `/usr/bin/bash` |
| Other executable resolution | node `/usr/local/bin/node`; pwsh `/usr/local/bin/pwsh`; Git `/usr/local/bin/git`; flock, sed, mkdir, stat and sha256sum under `/usr/bin` |
| `command -v jq` in diagnostic child | No match; probe exited 3 at that prerequisite |
| Required image file `/usr/local/bin/jq` | Absent, confirmed independently after the PATH probe |
| Explicit user-home candidate, resolved | `/home/app/.local/bin/jq` -> same path |
| User-home version / SHA-256 | `jq-1.7.1` / `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5` |
| User-home owner / uid / gid / mode | app:app / 1654 / 1654 / 755 |
| Image identity / build / activation receipt | Not supplied; not established by this delegate |

The user-home copy matches the pin but fails image-custody admission. The plan's
earlier PATH observation is historical and cannot establish this child's PATH.
No required test was run in the unqualified environment, and no skip/pass is
claimed for the missing prerequisite.

`GET /api/runner-defaults` and `GET /api/session-runners` were freshly read through
the configured API/task header around 22:03 UTC. Defaults revision 2; eligible
Linux and Windows descriptors plus an unavailable draining descriptor. These are
placement observations, not immutable image provenance. No runner/platform pin,
fleet setting or runtime change was made.

Read [CARD-1025](/api/cards/66b75498-78a5-4e97-a699-0ba4fe8db651): InProgress,
updated `2026-10-04T22:02:54.207174Z`; it owns outer-host jq before real recycling.
No host qualification receipt was provided by that description. Its scope is
linked in the testing owner; rollout host-install instructions remain with that
owner. The caller must coordinate CARD-1025 before any phase that recycles.

## Retained timing, independently reread

Historical artifacts remain under
`/work/worktrees/task-7c7fdeeb/.antiphon/c1021-unit/c1021-unit-20261004-063854-5ec0/`.
This task reread `source.json`, parsed the TRX counters/result boundaries and
rehashed the source receipt, TRX and log. No historical payload was copied into
Git. A Python reader was unavailable (exit 127); the read-only Node reader then
completed. Neither invocation was a build or test driver.

| Retained artifact | SHA-256, identical to frozen plan |
|---|---|
| HEAD source.json | `7e085e2d85e6bba695826688e8229f50c4b3fa31c30883a907646e0e78fc6461` |
| HEAD run.trx | `b970078efd70cf762e2e443b51cf9964fd5b9a9fc89b250bc59eb1af71eb3da6` |
| HEAD run.log | `c159fe48fb658bfa40ed4e7f3f7a79ea6a11e5a20a075fe234d388a5b6c77a58` |
| Base run.trx | `5e09ec6897f85bf38b2f937da857329cc2f88befb1cb55983f0b746774c4bc38` |

Actual historical tested SHA: `bd5f4dc9f0ed1501d825a2dab6fcab3088433e91`.
Its receipt records matching clean start/end source, dirtyFiles=0 and
buildSource=verified, but exit 1 and 15 failures: this is **red**, not a clean
green certificate. TRX: total=4103, executed=4051, passed=4036, failed=15,
notExecuted=52, timeout=0, aborted=0. All fifteen failures retain the frozen
names; durations range from 0.0776095 to 4.6942987 seconds. First result starts
`2026-10-04T06:42:29.4333276+00:00`; last result ends
`2026-10-04T06:49:17.2142825+00:00`.

| Phase from retained source receipt | Seconds |
|---|---:|
| Build-slot wait | 0 |
| Isolated build | 102.2440571 |
| Test-host startup | 110.3572973 |
| Test interval, wall between method boundaries | 407.7809549 |
| Teardown | 4.3298523 |
| Test-host wall | 522.468084 |

Build plus test-host wall = 624.7121411 seconds. Startup is a recorded boundary
interval, not attribution to a particular initializer; the remaining outer
receipt/orchestration overhead is not proof of a stall. No deadline, assertion,
retry, process limit or estimate was increased. No new timed probe is justified.

Historical base TRX at
`/work/worktrees/task-7c7fdeeb/.antiphon/c1021-base/c1021-unit-base-prefix-20261004-065423-354e/run.trx`
records 15 executed / 15 failed / 0 skipped / 0 timeout / 0 aborted at
`d9cba338aa47d014cabf107183dd45c066b36a59`. This inherited no-jq comparison
cannot classify a future failure in a qualified jq environment. Windows Git
bash path failures remain outside this task (CARD-1050, per commissioning brief).

Unedited **historical** CHECKPOINT line from the retained source receipt:

```text
CHECKPOINT c1021-unit commit=bd5f4dc9f0ed1501d825a2dab6fcab3088433e91 build=ok filter=/*/*/*/*[Category=Unit] executed=4051 passed=4036 failed=15 skipped=52 trx=/work/worktrees/task-7c7fdeeb/.antiphon/c1021-unit/c1021-unit-20261004-063854-5ec0/run.trx slot=granted waited=0s dirty=0 source=bd5f4dc9f0ed1501d825a2dab6fcab3088433e91 sourceState=clean buildSource=verified
```

## Ordinary scope and pending obligations

The explicit S1-only brief excludes S2, whole Unit and whole Final Unit despite
the generic profile text. The plan's checkpoint table labels every row
`After=S1`, then explicitly says all three execute during S2 after the activation
gate. S1 permits recording the missing gate and expressly excludes image-class
replays when its source is unchanged. Therefore this task ran **0 builds and 0
tests**, no tool bootstrap, no unlisted build/test driver, no repeat and no repair
round. Fresh tested SHA/TRX/clean build receipt: not applicable. Fresh slot and
wait: `slot=not-run waited=n/a`; no build lease was requested. No fresh CHECKPOINT
line is manufactured for a deferred row.

| IDs | Actual outcome |
|---|---|
| CP-1, V-1, R-1 | Not run; deferred to separately commissioned qualified native-Linux S2 (12 frozen Remote methods) |
| CP-2, V-2, R-2 | Not run; deferred to S2 (2 frozen Rolling methods) |
| CP-3, V-3, R-3 | Not run; deferred to S2 (1 legacy method plus four driver receipts) |
| PC-1 / G-1 | Pending post-land method-scoped SourceLanding Mutation: isolated child jq absence, exact-defaults assertion red with `RecycleToolsMissing`, restore/green; no additional variants |

Caller/activation owner still owes the outer container ID and creation/start
times, immutable image identity, build/source/activation receipt and runner
`buildVersion`; active canonical jq file/digest/root ownership and child PATH
qualification; the isolated existing jq-version row; and CARD-1025's separate
host receipt plus named rollout phase receipts if activation requires rollout.
The source already implements the pin; this S1 does not close the runtime fix.

S2 must use the frozen plan's serial checkpoint-tool command, committed HEAD as
expected SHA, bounded waits to terminal completion, fresh exact TRX rosters and
source validation. No whole Unit or Windows repair is authorized here. The
ordinary estimate remains 18 minutes plus setup; derived row limits remain
18/15/24 minutes. No CP/V/R is marked passed or discharged by retained evidence.

Next is ordinary Review **of S1 documentation only**. After accepted Review the
caller lands original Code task `d20ed3ac-b453-46d7-b312-822a9fec5be4` through the
normal owner route, preserves the S2 obligation and commissions SourceLanding
Mutation when its qualified baseline is available. Restart for this documentation
slice: **none**; any later runner-image activation is caller/orchestrator-owned.
