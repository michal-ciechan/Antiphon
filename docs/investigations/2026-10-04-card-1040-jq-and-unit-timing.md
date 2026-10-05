# CARD-1040 S2: jq image admission and retained Unit timing

S2 is **blocked at the activation prerequisite**. On 2026-10-05 the executing
native Linux child could not find jq, and `/usr/local/bin/jq` was absent. The
landed CARD-1054 probe refused with `JqNotFound`. No qualified consumer test ran;
CP-1 through CP-3 and every ordinary V/R remain outstanding. This document is an
admission refusal and retained-log accounting record, not an activation receipt.

Original Code task / landing owner: `6159b95b-3804-4593-8ef4-f120f48a5684`.
Branch: `feat/card-task-6159b95b`.
Executing worktree: `/work/worktrees/task-6159b95b`.
Registered desktop checkout: `C:\Antiphon\worktrees\card-task-6159b95b` (unreachable
from this delegate).
Task base and inspected/probed source: `14316228eaac5afd8a404d75fca69d7a9fa94382`.
Source was clean throughout admission. The later evidence commit does not
relabel this SHA as consumer-tested source; no fresh TUnit-tested SHA exists.

The [landed plan and frozen checkpoint table](../superpowers/plans/2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md#checkpoints)
and [testing owner](../testing-and-build.md#jq-qualification-and-bounded-consumer-proof-card-1040)
require activation before S2 execution. The task's explicit bounded scope
excludes the generic Final-profile whole-Unit recipe: exactly fifteen selected
methods in three rows, no whole class, namespace or assembly. The CARD-1054
[probe contract](../superpowers/plans/2026-10-05-card-1054-jq-path-qualification-plan.md#verification-design)
changes the admission row but preserves image identity, uid, digest, owner,
mode and activation obligations. Its fixture passes do not satisfy those facts.

## Fresh admission evidence

At `2026-10-05T10:17:18Z`, a foreground, non-login `bash --noprofile --norc`
child inherited the task environment without PATH adjustment and cleared its
command hash. No environment or credential contents were printed. Tool lookup
found the following; execution qualification of all tools was not completed
because jq admission failed.

| Fact | Observation |
|---|---|
| Runtime | Linux x86_64, uid/gid 1654, app:app |
| bash | `/usr/bin/bash` |
| node / pwsh / Git | `/usr/local/bin/node`, `/usr/local/bin/pwsh`, `/usr/local/bin/git` |
| flock / sed / mkdir / readlink / sha256sum / stat | `/usr/bin/<name>` |
| dotnet | `/usr/bin/dotnet` |
| Child `command -v jq` after `hash -r` | No match |
| Canonical `/usr/local/bin/jq` | Absent, including no leaf symlink |
| jq version / digest / owner / mode | Unavailable; no jq executable invoked |
| `/c660-home` | Absent; no directory created |
| Outer container ID, creation/start times, immutable image ID/digest | Not supplied or established |
| Image build/source/activation receipts | Not supplied or established |

The unchanged probe's SHA-256 was
`d09360f811a1459628ef09770411051374c2f80c9127f7f443df2f9727605040`.
The task base includes the landed probe edit at
`c88b9623700c2fdad4fba1f3a75cc78bf73cc099`.
One foreground invocation exercised only its `jq-version` arm:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1040-s2-admission -- bash docker/session-runner-grok/verify-codex-image.sh jq-version
```

Unedited output, process exit **1**:

```text
BUILD SLOT granted lease=f8934b44-b7a7-4eac-b509-96fe772d0f6f waited=0s maxcpucount=6
C660_ROW jq-version fail reason=JqNotFound lookupPath=unavailable path=unavailable
BUILD SLOT released lease=f8934b44-b7a7-4eac-b509-96fe772d0f6f held=0s
```

This is a local prerequisite diagnostic, not the required throwaway immutable-image
qualification or a deliberate Mutation control. It stopped before invoking jq or
writing the version stderr file. It was the only executable qualification command
outside CP-1..CP-3; its reason was to record the existing admission failure before
spending the frozen consumer-proof budget. `slot=granted waited=0s`; no unleased
execution, retries, bootstrap, build or TUnit run occurred. The wrapper and child
both exited, and the lease was released.

Fresh `GET /api/runner-defaults` and `GET /api/session-runners` reads through the
configured API at 10:17 UTC returned defaults revision 2, eligible Linux and
Windows descriptors, and an unavailable draining descriptor. The task-bound
runner's status reported `buildVersion=4358939ecd85d6e7ff0941f970879499cb930e3d`,
Linux, available and dispatch-eligible. The current task detail confirms the
base above and requiredPlatform Any. These API observations establish placement
and reported software version only; none establishes the outer container's image.
No fleet location is embedded in a reproduction command or routing change.

The live CARD-1040 description (updated `2026-10-05T10:16:02.005005Z`) still
records missing jq and deferred image rebuild. Neither it nor this brief supplies
activation evidence. The earlier [S1 observation](2026-10-04-card-1040-s1-jq-admission.md)
also found canonical jq absent. Its user-home binary is historical diagnostic
evidence only. This task did not invoke that unapproved binary, install a tool,
inspect nested Docker as an outer-image substitute, access server2 credentials,
activate an image, deploy or restart a service.

Ignored local evidence: `/work/worktrees/task-6159b95b/.antiphon/c1040-s2-admission/`
contains the probe log and sanitized API/admission/retained-summary JSON. These
generated files are not committed; the essential admission row is retained above.

## Actual ordinary outcomes

| Checkpoint | Ordinary IDs | Required executions | Actual executions | Actual outcome |
|---|---|---:|---:|---|
| CP-1 | V-1, R-1 | 12 | 0 | NOT RUN: image/jq admission failed |
| CP-2 | V-2, R-2 | 2 | 0 | NOT RUN: image/jq admission failed |
| CP-3 | V-3, R-3 | 1 | 0 | NOT RUN: image/jq admission failed |

For each CP, passed/failed/skipped counters, TRX and verified build provenance
are **n/a**, not zero-failure acceptance. `slot=not-run waited=n/a` for each CP;
no CP lease was requested. No fresh CHECKPOINT line is manufactured. There are
no fresh four-mode driver summaries, required-present T-20 or 24/66/227 receipts.
V-1, V-2, V-3, R-1, R-2 and R-3 are individually **NOT RUN**. All remain due in
the continued Final S2; this is not a completed Interim round or a waiver.

Builds=0, TUnit executions=0, CP repetitions=0, repair rounds=0. No test, production
script, image source, timeout, assertion, retry or process limit was changed.
No new tests were added because the scoped work introduces no behavior and the
existing proof is admission-blocked. There are no alternate build outputs or
owned checkpoint children to clean up.

PC-1 / G-1, the single isolated jq-absence variant in
`RemoteScriptContractTests.C1008_Recycle_exact_default_volumes`, remains
**PENDING post-land SourceLanding Mutation**. The local prerequisite refusal is
not that method's red assertion, baseline or restored green. All deliberate
mutants and missing-control discovery remain with separately commissioned Mutation.

## Retained accounting, independently rechecked

Read-only artifact hashing and TRX/source-receipt inspection on 2026-10-05
reconfirmed the historical records below. The fifteen failing method names equal
the frozen manifest roster exactly. No retained run is presented as a new test.

Historical HEAD evidence root:
`/work/worktrees/task-7c7fdeeb/.antiphon/c1021-unit/c1021-unit-20261004-063854-5ec0/`.
Historical base TRX:
`/work/worktrees/task-7c7fdeeb/.antiphon/c1021-base/c1021-unit-base-prefix-20261004-065423-354e/run.trx`.

| Artifact | SHA-256, matches frozen plan |
|---|---|
| HEAD source.json | `7e085e2d85e6bba695826688e8229f50c4b3fa31c30883a907646e0e78fc6461` |
| HEAD run.trx | `b970078efd70cf762e2e443b51cf9964fd5b9a9fc89b250bc59eb1af71eb3da6` |
| HEAD run.log | `c159fe48fb658bfa40ed4e7f3f7a79ea6a11e5a20a075fe234d388a5b6c77a58` |
| Base run.trx | `5e09ec6897f85bf38b2f937da857329cc2f88befb1cb55983f0b746774c4bc38` |

Historical tested HEAD `bd5f4dc9f0ed1501d825a2dab6fcab3088433e91`: total 4,103,
executed 4,051, passed 4,036, failed 15, notExecuted 52, timeout 0, aborted 0.
Its source receipt has identical clean start/end SHA/fingerprint, dirtyFiles=0,
buildSource=verified and exit 1. Historical base
`d9cba338aa47d014cabf107183dd45c066b36a59`: executed 15, passed 0, failed 15,
skipped 0, timeout 0, aborted 0. These are inherited red observations, not a
qualified jq baseline for classifying a future failure.

| Historical phase | Seconds |
|---|---:|
| Build-slot wait | 0 |
| Isolated build | 102.2440571 |
| Test-host startup | 110.3572973 |
| Method interval, wall time | 407.7809549 |
| Teardown | 4.3298523 |
| Test-host wall | 522.468084 |

Build plus test-host wall is 624.7121411 seconds. The first result starts at
`2026-10-04T06:42:29.4333276+00:00`; the last ends at
`2026-10-04T06:49:17.2142825+00:00`. Failing method durations are
0.0776095-4.6942987 seconds. Timing uses method boundaries, not a sum of parallel
durations. The startup's internal cause remains unattributed; elapsed startup
and orchestration overhead alone do not demonstrate a stall. Fresh qualified
slot/build/startup/method/teardown accounting remains unavailable because S2 did
not run. No additional timed experiment or deadline change is warranted.

Unedited **historical red** receipt:

```text
CHECKPOINT c1021-unit commit=bd5f4dc9f0ed1501d825a2dab6fcab3088433e91 build=ok filter=/*/*/*/*[Category=Unit] executed=4051 passed=4036 failed=15 skipped=52 trx=/work/worktrees/task-7c7fdeeb/.antiphon/c1021-unit/c1021-unit-20261004-063854-5ec0/run.trx slot=granted waited=0s dirty=0 source=bd5f4dc9f0ed1501d825a2dab6fcab3088433e91 sourceState=clean buildSource=verified
```

Read-only scratch extraction initially used a Node CommonJS/top-level-await
combination that could not start, then a name pattern that omitted digits in
`audits_work_as_1654`; the corrected reader matched all fifteen literal names.
Neither failure launched a build/test, changed source or consumed a repair round.

## Required continuation and ownership

The caller/operator must supply or commission the activation prerequisite under
the [staged rollout owner](../docker-stack.md#staged-server2-rolling-rollout-card-0934).
This delegate is explicitly prohibited from activating or restarting anything.
The continuation needs:

1. Outer-host-owned container ID, creation/start times and immutable image
   ID/digest, joined to build source, activation receipt and runner buildVersion.
   If rollout is needed, retain named phase receipts and CARD-1025's separate
   outer-host jq receipt before phases that recycle.
2. Active canonical jq as uid 1654: version `jq-1.7.1`, SHA-256
   `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, root:root
   0755 regular executable non-symlink leaf at `/usr/local/bin/jq`; unchanged
   actual child PATH resolves it or an approved alias to that same file.
3. The reviewed landed probe mounted readonly in a throwaway container of that
   immutable image, uid 1654:1654, network none, no socket/ports, private owned
   `/c660-home` tmpfs. Require the updated path-bearing success row and exit 0.
   This supplements, rather than replaces, active-container qualification.
4. Only then bootstrap the tool once through `scripts/build-slot.ps1`, output
   `bin-c1040-tool/`, UseAppHost=false, and run the frozen table serially:

```powershell
$c1040Source = (git rev-parse HEAD).Trim()
dotnet tools/Antiphon.Checkpoints/bin-c1040-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md --after S1 --serial --expected-source-sha $c1040Source --max-wait 50s
```

Await exit-75 runs using their emitted run ID and `wait --max-wait 50s` until
terminal. Inspect exact fresh TRX names/counts 12/2/1, zero skips, all four
31-assertion driver summaries and required-present receipts; validate clean
source/build provenance and retain unedited CHECKPOINT lines. The table remains
unchanged: 18-minute ordinary estimate, derived row limits 18/15/24 minutes,
no repeats required after green, at most two repair rounds in the assigned brief.
Any new failure needs exact-method comparison at the committed base in the same
qualified environment. Historical missing-jq failures cannot classify it.

Next stage: **Code**, after the activation owner supplies these prerequisites.
Ordinary verification is incomplete, so this report is not a request for a clean
S2 Review or card closure. Review follows completed ordinary V/R; the caller then
lands the original Code task and commissions the pending SourceLanding Mutation.
Restart: **none performed**; any runner-image activation/restart remains with the
caller/operator under the separate rollout authorization.
