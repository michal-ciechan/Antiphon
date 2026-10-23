# RC 5a126f6fc qualification stopped

Task `8423cb1a` stopped the abandoned Windows qualification at
2026-10-23 11:41:12 UTC (12:41:12 BST), following
[Inspecting and stopping a qualification](../release-gates.md#inspecting-and-stopping-a-qualification).
The stop and stale-lock recovery succeeded. The caller confirmed an exclusive
hold on new native starts and task `8423cb1a` as sole recovery owner. Both locks
were preserved in the evidence directory; their original paths are absent.

## Identity and custody

- Candidate: `rc-2026-10-20-5a126f6fc5ae`.
- SHA: `5a126f6fc5ae4e16d803b628716b8f0485a37174`.
- Run: `6bd38cbb9604482e96a4854957cead05`, launched by task `c11e7803`.
- Supervisor: PID `209072`, started `2026-10-23T02:00:10.0301861Z`.
- Verified owner ancestry: `219196 -> 308300 -> 120576 -> 209072`
  (nightly owner, build-slot wrapper, launch wrapper, supervisor).
- Read-only Git inspection confirmed the candidate checkout at that exact SHA
  with a clean tracked index/worktree. No checkout files were changed.

The operator authorized abandonment because timeout cuts and failures had already
prevented qualification and the run occupied desktop native capacity. Immediately
before the kill, all ten observed processes belonged to the supervisor tree,
creation order was valid, and the supervisor generation matched the launch record.
`taskkill /PID 209072 /T /F` returned 0. The subsequent census took 7.74 seconds,
within the documented 30-second budget: every observed tree PID was absent and no
process command line referenced this run ID or the candidate checkout.

## Preserved evidence and qualification disposition

Restricted local evidence, scripts, before/after observations and SHA-256 manifest:

`C:\Antiphon\releases\task-8423cb1a\stop-evidence-20261023`

Evidence includes exact before-state/lock bytes, candidate and selection/attempt
JSON, launch identity, redacted process inventories, TRX sizes and modification
times, the newest existing group log's last 200 lines, and memory snapshots.
`stop-note.txt` records the stop independently of the supervisor's state.

The original logs remain in place for Debug task `392d9d33` and later analysis:

`C:\Antiphon\releases\candidates\rc-2026-10-20-5a126f6fc5ae\logs\2026-10-23-0201-6bd38cbb`

Groups 1-21 were reached, with partial coverage only. Sixteen TRX files exist:
groups 1-14, 19 and 20. They record 4,119 cases: 4,018 passed, 74 failed and
27 not executed (4,092 executed). Groups 15-18 have no TRX after timeout cuts;
group 21 was stopped in flight and has no TRX. These missing groups are outside
those counts. No new tests, builds or qualification runs were started by this task.

**INCOMPLETE/ABANDONED; NO gate credit claimed.** `last-run.json` was not edited:
before/after SHA-256 is
`AD87A0911EA40FD67C9EB1A23F436606140A878B1197D561D37BF88450D2504C`.
Its recorded phase remains `tests`, with null exit; it is not a terminal receipt.

## Health and completed lock recovery

Post-stop free physical memory was **8,792.11 MiB**, versus 5,961.73 MiB before;
the final post-recovery measurement was **8,804.80 MiB**.
AppHost PID `238576`, desktop session runner PID `295168`, and both Docker backend
processes (`18036`, `18332`) retained their original generations. At approximately
11:41:52 UTC, server `/health`, server `/api/version`, and runner `:17204/health`
returned HTTP 200. Server version was the exact candidate SHA with `land-v2`.
Docker answered `docker info` successfully, version `29.5.3`.

Recovery completed at **2026-10-23T11:48:24.3141573Z** (12:48:24 BST).
Each lock was independently reread and compared with its exact preserved bytes.
Both identified this run and owner PID `219196`, started
`2026-10-23T02:00:40.4040758Z`; the owner and all previously observed tree PIDs
were absent. Recovery paths and ancestors were checked for reparse points, and
each destination was nonexisting and on the same volume. The locks were moved
separately using `Move-Item -LiteralPath` without `-Force`:

| Original path | Retained file within the evidence directory |
| --- | --- |
| `C:\Antiphon\verification\native-run.lock` | `native-run.lock.moved-20261023T114818721Z` |
| `C:\Antiphon\releases\candidates\rc-2026-10-20-5a126f6fc5ae\state-c11e7803\run.lock` | `run.lock.moved-20261023T114818721Z` |

The moved files' SHA-256 values match their before-state copies:

- Native: `8CCA594581FB8D5201ABF8AA7EC21513A41F0FA7CCAF93F6FAEA6799B6BC5586`.
- Run: `642573C7E3CBF87725542833923E95F36E590D289E7260959938D609D41D814D`.

Checks before and after each move, followed by a final census, found no prior tree
PID, run/checkout process, or new native start. Both original lock paths were
absent after each relevant move and at final health verification. The supervisor's
state remained byte-identical and the original logs remained in place.

`lock-recovery-receipts.json`, `lock-recovery-complete.json` and
`recovery-health.json` record the completion. The earlier blocked receipt is
retained as historical evidence; the appended stop note records its resolution
after the caller confirmed the operational hold. Final health checks again found
all four protected process generations unchanged, HTTP 200 from server health,
server version and runner health, the exact candidate server SHA, and healthy
Docker `29.5.3`. No qualification, preflight or test run was started. No recovery
blocker remains; this abandoned run still earns no gate credit.
