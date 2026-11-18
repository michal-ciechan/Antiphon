# CARD-1987 S1+S2 checkpoints

Code task cd7b1a6e. Closed rows for the binding cache (S1) and the one-statement activity write (S2) only. M1-M3 belong to another task and are not in this table. Red rows run once at their test commit and are not re-run after the fix. Export `ANTIPHON_LINUX_COMPILER_POLICY=0` before every dotnet command. Lane is the runner-defaults host. Omit `-Runner` and `-Platform`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1987-s1red/` | c1987-s1-red | `/*/*/RoutingBindingCacheTests/C1987_route_reads_the_binding_once_per_ttl` | W-10 red | 1 executed, failed on the read count (5, budget 1) | 1 | 15 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1987-s1/` | c1987-s1 | `/*/*/(RoutingBindingCacheTests*)\|(PhoneHomeSessionRoutingTests*)/*` | W-10, W-11, existing routing | 8 executed (3 cache + 5 routing), 0 failed/skipped | 8 | 15 |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c1987-s2red/` | c1987-s2-red | `/*/*/AgentSessionRuntimeActivityTests/C1987_activity_write_is_one_command` | W-12 red | 1 executed, failed on the command count (3, budget 1) | 1 | 15 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1987-s2/` | c1987-s2 | `/*/*/(AgentSessionRuntimeActivityTests*)\|(RunAttemptStallDetectorTests*)/*` | W-12, W-13, burst test, stall detector | 4 executed (3 activity + 1 stall), 0 failed/skipped | 4 | 15 |

Min is the executed-test floor. A write-invalidation witness added with the cache may execute above the CP-2 floor of 8.
