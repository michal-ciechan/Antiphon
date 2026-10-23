# RC 5a126f6fc qualification stopped

Task `8423cb1a` stopped the abandoned Windows qualification at
2026-10-23 11:41:12 UTC (12:41:12 BST), following
[Inspecting and stopping a qualification](../release-gates.md#inspecting-and-stopping-a-qualification).
The stop succeeded. Lock recovery remains pending the required exclusive hold on
new native starts; both locks have been retained.

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

## Health and remaining lock recovery

Post-stop free physical memory was **8,792.11 MiB**, versus 5,961.73 MiB before.
AppHost PID `238576`, desktop session runner PID `295168`, and both Docker backend
processes (`18036`, `18332`) retained their original generations. At approximately
11:41:52 UTC, server `/health`, server `/api/version`, and runner `:17204/health`
returned HTTP 200. Server version was the exact candidate SHA with `land-v2`.
Docker answered `docker info` successfully, version `29.5.3`.

The native lock and run lock still refer to the stopped owner PID `219196` and
this run. They remain at their original paths:

- `C:\Antiphon\verification\native-run.lock`
- `C:\Antiphon\releases\candidates\rc-2026-10-20-5a126f6fc5ae\state-c11e7803\run.lock`

The authoritative procedure additionally requires recovery to be serialized with
all operators and new native starts held through existing launch owners. That
coordination has been requested but is not yet confirmed. Step 4 explicitly says
to abort cleanup without it, so neither lock was moved. Once that hold exists,
reobserve owner/children, compare exact preserved lock bytes and identity, then
move each separately to a unique same-volume evidence destination using
`Move-Item -LiteralPath` without `-Force`. Verify moved bytes and absent original
paths. Changed, foreign, live, reused or unknown identities require retention.
No later qualification is authorized by this report.
