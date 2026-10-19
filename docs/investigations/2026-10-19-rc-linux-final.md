# CARD-1350 final Linux roster at df6b422c

Status: **green: 4,528 executed, 4,528 passed, zero failed, zero skipped**.
The final TRX contains exactly the frozen 373 classes, with no missing or extra
class and no unexpected outcome. The prior `/proc` registration failure did not
recur. This is the commissioned Linux CARD-1287 subset; the separate Windows rc
profile remains outside this report.

## Source and method

Tested source: `df6b422c71b0828b4bd7f855a86e2a51d1987e79`, candidate
`rc-2026-10-18-df6b422c71b0`, requested final tip of `rc/2026-10-18`.
Task: `aaee7f74-88f5-429b-bcd7-852303e6a812`, bound by the task API to
CARD-1350, card `3ffa3752-5901-4eda-966b-afbc384f8625` on the Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`.

The test checkout was `/work/worktrees/rc-final-aaee7f74`, detached at the exact
requested source. HEAD and clean tracked/index status were verified before the
build, after the build, and after the roster. Assembly informational version was
`1.0.0+df6b422c71b0828b4bd7f855a86e2a51d1987e79`; its bytes were unchanged after
execution. The report branch is `feat/card-task-aaee7f74`, based on
`f99f8bffb5887d43fafae61ae979ae9e11a36f80`. Report commits are not the tested source.

The first gate's method was fetched from `feat/card-task-dcba76bd` at
`27c198a50a5e8464125ce8832452d2fcb0098c03`, in
`docs/investigations/2026-10-18-rc-linux-lane.md`. This pass used its same frozen
filter and reproduction from
[CARD-1287 qualification](2026-10-15-card-1287-linux-unit.md): one isolated build,
then one no-build roster execution, both through `scripts/build-slot.ps1`, with
`UseAppHost=false`, Detailed output and TRX. Output path was
`bin-rcfinal-aaee7f74/`. No test selection, product code or test source was changed.

Both inherited task-context variables were omitted together from the child
environment: `ANTIPHON_TASK_ID` and `ANTIPHON_TASK_TOKEN` were absent, with no
substitute values. Headed, distiller, broker-test and
`TUNIT_MAX_PARALLEL_TESTS` overrides were absent. Slot shims were absent. The normal
assembly guard reported `SessionRunner__BaseUrl=http://127.0.0.1:1` and disabled
the check interpreter, diagnose and distiller hosts.

## Results

| Suite | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| antiphon: frozen Linux roster, 373 classes | 4528 | 4528 | 0 | 0 |
| session-runner | not run | | | |
| pty-host | not run | | | |
| agents-pty | not run | | | |
| messaging | not run | | | |
| client | not run | | | |
| scripts (standalone suite) | not run | | | |
| e2e | not run | | | |
| markdown-pdf | not run | | | |

The other suites were outside the commissioned frozen Linux lane. Script-contract
tests that belong to the selected Antiphon.Tests classes are included in its count.
The frozen exclusions in the CARD-1287 qualification remain unchanged.

| Piece | Granted lease | Wait | Hold | Result |
|---|---|---:|---:|---|
| Build | `5808ebb0-f649-4f12-9ff5-b4a46363da10` | 0s | 197s | Exit 0; 1085 warnings, 0 errors |
| Roster | `fa26f0ce-cfab-4a4e-84b4-405c687c5bc0` | 0s | 2097s | Exit 0; 4528/4528 passed |

Build outer interval: `2026-10-19T13:05:09.453Z` to
`2026-10-19T13:08:27.210Z` (197.757s); MSBuild elapsed 195.75s.
Roster outer interval: `2026-10-19T13:08:45.824Z` to
`2026-10-19T13:43:44.448Z` (34m 58.624s).
TRX interval: `2026-10-19T13:08:50.136276Z` to
`2026-10-19T13:43:43.1114549Z` (34m 52.975s).
TUnit's own summary duration is 34m 53.414s.

The external metadata inspector initially assumed the DLL was under
`Debug/net9.0`; explicit OutputPath instead placed it directly under the output
root. That lookup stopped the first wrapper after its successful build and before
any test launch. Only the external wrapper's inspection path was corrected; the
existing assembly was source-checked and used for the one roster run. The build
was not repeated. `driver-correction.json` retains this setup failure.

## Previous failures

`CheckpointOwnedOutputTests.disk_ledger_cannot_authorize_user_deletion(outside)`
**passed**, duration 0.1313193s. Its `forged`, `missing` and `truncated` siblings
also passed. The former `/proc/<pid>/stat` registration failure did not recur;
there was no additional candidate rerun.

`CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`
also **passed**, duration 3.8980715s.

There are no reds to classify as inherited or new. Consequently, no baseline
build or test was needed at `61117dd1663d2f47ce4708c18b609b8936491986`.

## Server2 workload observations

Host container `a6457bc89508`, Debian 12, .NET SDK 10.0.402, runtime 9.0.21,
PowerShell 7.5.4, Node v22.23.3, TUnit 1.44.0 and Microsoft Testing Platform 2.2.2.
The inherited slot endpoint was **`http://build-slots:8080/build-slots`**, budget
4, per-build max CPU count 6, memory floor 16,384 MB. Both grant lines and final
release lines are retained. Periodic reads confirmed the roster lease was alive.

There are 43 container process/load snapshots from 13:05:09Z to 13:43:45Z and
37 periodic configured-broker samples from 13:11:25Z through completion, plus
an earlier configured-broker read. The initial sampler read the separate
`127.0.0.1:8080` broker; those loopback occupancy readings are not evidence about
the configured broker's leases. The actual driver always used the inherited
configured endpoint.

Observed concurrent background processes were the session runner, PTY hosts and
an existing pair of Codex processes under
`/tmp/codex-parser-probe-359b4ff7/plain`, alongside this task and its test fixtures.
No other task worktree appeared in the container process samples. The runner
catalogue consistently reported server2 at 1/10 occupied sessions, server2-temp
unavailable, and desktop at 1/2 occupied delegated seats. No desktop test or
process was started or changed by this pass.

The configured-broker samples showed only this roster's lease, zero waiters and
zero competing leases; after completion occupancy was zero. Sampled available
memory ranged from 103,013 to 106,881 MB. The host's one-minute load average ranged
from 0.96 to 25.21 across build and execution. Thus these observations do not
establish a competing test load despite the brief's expected busy server2 state.
They cover visible container processes, runner occupancy and the shared broker,
not every unbrokered workload on the physical host.

## Evidence and cleanup

Raw evidence is retained at **`/work/evidence/task-aaee7f74/`**. Key files are
`build/console.log`, `build/command.json`, `build/terminal.json`,
`roster/console.log`, `roster/command.json`, `roster/terminal.json`,
`roster/run.trx`, `audit.json`, `cases.json`, `assembly.json`,
`source-before.json`, `roster-source-before.json`, `source-after.json`,
`task-binding.json`, `host-snapshots.jsonl`, `configured-broker-snapshots.jsonl`,
`runner-catalogue.jsonl`, `workload-summary.json` and `cleanup.json`.
Drivers, the exact filter/roster bytes and the TRX audit script are also retained.

| Binding | SHA-256 |
|---|---|
| Assembly, before and after execution | `44320e960a3243360f1efbc851d3e4d409cf2898faf9ff68fea0ec06e0219b42` |
| Frozen class roster | `716018884f1a68c923958dde2c65fc7150ecb78d123972fd2d671bfadbe28168` |
| Frozen filter | `9ba95d1db683438f6c382422c9ca4a596d2280930f100da9973b657596020ad7` |
| Final TRX, ID `5347bc93-2a9d-41e5-834f-dea4cc6c3912` | `9b01430efe77c3687b769dbcdab832018036419f7590eb0b6056e109c03c93e7` |

All owned drivers were awaited. After confirming no process remained in the test
checkout, 28 producer-inventoried output roots were removed (3,034 files,
1,283,100,078 bytes inventoried). No `bin-rcfinal-aaee7f74` roots remain. Source
was still clean. The detached scratch worktree was then removed with ordinary
`git worktree remove`, without force. Raw evidence remains outside Git and outside
the removed checkout. No source repair, assertion change, tag, deployment or
release publication was performed.

To replay, use a fresh checkout at the tested SHA and a fresh results directory.
The actual literal argv is retained in the two `command.json` files. The essential
commands, with the task pair omitted together, are:

```sh
env -u ANTIPHON_TASK_ID -u ANTIPHON_TASK_TOKEN \
  pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1350-linux-final-build -- \
  dotnet build tests/Antiphon.Tests --property:OutputPath=bin-rcfinal-replay/ \
  --property:UseAppHost=false --nologo
roster_filter=$(cat docs/investigations/2026-10-15-card-1287-linux-unit-filter.txt)
env -u ANTIPHON_TASK_ID -u ANTIPHON_TASK_TOKEN \
  pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1350-linux-final-roster -- \
  dotnet run --project tests/Antiphon.Tests --no-build \
  --property:OutputPath=bin-rcfinal-replay/ --property:UseAppHost=false -- \
  --treenode-filter "$roster_filter" --output Detailed --report-trx \
  --report-trx-filename run.trx --results-directory /work/evidence/rc-final-replay/roster
```

Keep the other opt-in/test overrides absent as recorded above. The historical
Linux subset does not certify the full automated Windows rc profile.

--- next stage ---
next: decide
handoff: CARD-1350 Linux roster is green at df6b422c71b0828b4bd7f855a86e2a51d1987e79: 4528 executed/passed, zero failed/skipped, exactly 373 classes. The prior outside-ledger failure did not recur and the namespace census passed. Combine this evidence with the separate Windows lane; no publication was performed. No competing broker lease was observed in the workload samples.
artifact: docs/investigations/2026-10-19-rc-linux-final.md
