# CARD-1261 deploy-am-service post-deploy traffic-check text

Task 81e506c1 (Code, small), branch `feat/card-task-81e506c1`, base master
`e7b6b80378dffe7b2326a8bc46ee5a91d30e9e69`.

## Change

`scripts/deploy-am-service.ps1` printed post-deploy traffic-check lines (and a help
paragraph) naming the retired Antiphon-Family test group. They now say the round-trip
check is manual and operator-initiated through the Family conversation (name only, no
chat id), and that an agent never sends test traffic to it, matching
[docs/telegram-bot-ops.md](../../telegram-bot-ops.md) and [docs/telegram.md](../../telegram.md).
The script stays ASCII-only for the Windows PowerShell 5.1 fallback.

A script-text test exists: `scripts/test-deploy-am-service.ps1`. It gains three
assertions, named in `AmServiceDeployReadinessTests.SucceedsImmediatelyAndPreservesDeployContractAsync`'s
roster so the TUnit row fails when one is missing:

| ID | Assertion name | Guards |
|---|---|---|
| V-1 | `CARD-1261 skipped traffic check names Family as manual operator-initiated only` | deploy with `-SkipTrafficCheck $true` prints the literal new line and not the retired group text |
| V-2 | `CARD-1261 required traffic check names Family as manual operator-initiated only` | deploy with `-SkipTrafficCheck $false` prints the literal new line and not the retired group text |
| V-3 | `CARD-1261 deploy script stays ASCII-only and names no retired destination or chat id` | script bytes are ASCII; no `Antiphon-Family`; no ten-digit run |
| R-1 | existing deploy-am-service contract and readiness assertions | no regression in the same harness and class |

Slices: S1 test commit `a6e64feb89c8d44e215f357166c3f19cc6ba070d` (red), S2 fix commit
`72cdcd517b9d5c11551770f2749d3678c1c47dd2`.

## Verification design

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | n/a | deploy-script | `pwsh -NoProfile -NonInteractive -File scripts/test-deploy-am-service.ps1` | V-1, V-2, V-3, R-1 | 0 failed; `DEPLOY-AM-SERVICE TESTS EXIT CODE: 0` | n/a | 3 | true |
| CP-2 | all | `tests/Antiphon.Tests -> bin-c1261/` | deploy-readiness | `/*/*/AmServiceDeployReadinessTests/(RetriesUntilHttpReadyAsync*)\|(FailsWhenHttpReadinessBudgetIsExhaustedAsync*)\|(SucceedsImmediatelyAndPreservesDeployContractAsync*)` | V-1, V-2, V-3, R-1 | exactly 3 passed, 0 failed/skipped | 3 | 15 | true |

CP-2 leaves out the class's Windows-only Git discovery methods, which skip on Linux.
Import with `timeouts.rowMinutes: 40`; run with `--expected-source-sha <HEAD>` and
`--total-timeout 150m`, without `--keep-outputs`. The checkpoint tool bootstrap build
through `scripts/build-slot.ps1` into `bin-c1261-tool/` is a disclosed unlisted build.
The S1 red proof is a disclosed unlisted `scripts/build-slot.ps1` run of the CP-1
command at S1.

### Cost

About 18 minutes of driver time plus slot waits. No whole Unit lane (the brief
excludes it); the change touches printed text in one operator script. No restart or
live acceptance; the Family round trip stays operator-initiated.

### Mutation candidates (pending)

- PC-1: restore the skipped-branch line to the retired group text; V-1 goes red.
- PC-2: restore the required-branch line to the retired group text; V-2 goes red.
- PC-3: insert a non-ASCII character into the script help; V-3 goes red.
