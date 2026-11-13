# CARD-1884 Host-stats top consumers

`GET /host-stats` reports host CPU and a process list of the runner plus each live session. A shared compiler, `dotnet test`, MSBuild, the checkpoint tool, and the agent CLIs never appear. This plan adds a bounded top-consumer list on the existing host-load sample. Schema version stays 1. CARD-1885 (compiler cap) is out of scope. No landed Plan or TestDesign task exists for this card; this file is the checkpoint and OS-witness home the Code brief requires.

Review round 2 moves the scan off the sampler tick. The scan has its own cadence (`TopConsumerScanIntervalMs`, default 5 seconds) and the tick only reads the cached ranking. A scan stops after about 10 ms of thread CPU and reports `topConsumersTruncated`. `topConsumersAgeSeconds` is null until a scan exists and `topConsumersStale` is true after twice the cadence. Those fields are omitted when default. An absent or empty `topConsumers` array is omitted from the canonical ingest hash, so a pre-upgrade sample keeps its identity. V-7 is no longer a 25 ms wall-clock bound on a live scan inside the tick.

## Decisions

- D-1. Top consumers are a new optional field on `HostLoadSample`, `HostSample`, and `RunnerHostCurrentDto`. They are not extra rows in `processes`. `processes` stays `runner`, `session`, and `pty-host`.
- D-2. Rank at most 8 processes by CPU-core delta over the sample window. A core count of 5.3 means 5.3 cores, not a percent of the host. The host CPU percent stays the `/proc/stat` total.
- D-3. The key is pid plus start ticks. A recycled pid has no previous counter. The first sighting publishes nothing.
- D-4. The scan runs off the sampler tick on `TopConsumerScanIntervalMs` (default 5 seconds) and reads at most 512 processes. The tick reads the cached ranking. Retained pids are kept, then the highest pids. A scan fault, or a scan inside the minimum interval, returns the previous ranking. A scan that spends the 10 ms thread-CPU budget stops and sets `topConsumersTruncated`. Eight public-wire rows add 743 bytes (923 empty, 1666 full). One hundred twenty such samples are 199920 bytes, under the 1 MiB history-chunk cap, so the 120-record cap binds first. The 8192-byte CapsPersist fixture cap is unchanged.
- D-5. Linux reads `/proc/<pid>/stat` utime+stime and RSS. Command line is read only when `comm` is truncated or is `dotnet`, at most 1024 bytes, and only to recover a basename. Environment is never opened. Windows uses one toolhelp image-name snapshot and `GetProcessTimes` plus `K32GetProcessMemoryInfo` on a limited-information handle. No `GetProcesses` and no `NtQuerySystemInformation`.
- D-6. Published names match `^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$`. Roles are `compiler`, `build`, `test`, `agent-cli`, `antiphon-runner`, `pty-host`, `docker`, and `other`.
- D-7. `IHostStatsProbe` stays `SystemHostStatsProbe`. The sampler attaches the table with `with { TopConsumers = top }`. A null table publishes an empty list. `ProcessSampling` false does not scan.
- D-8. Profile-16 (`origin/feat/card-task-53b8f016`) edits `ThreadCpuMilliseconds`. This card does not. The sampler overlap is the `SampleOnce` hook and an `internal` constructor so the process table stays internal.
- D-9. Native Windows toolhelp proof is owed by the orchestrator. This host records the Linux factory and forces the Windows reader type without calling toolhelp.

## Verification design

V-1 `Shared_compiler_under_pid_1_is_ranked_compiler` builds a synthetic proc pid 1 named VBCSCompiler, samples twice five seconds apart, and requires one top row: pid 1, role compiler, 5.3 cores, RSS 409600. The session process list stays empty. The published JSON has no command-line secret.

V-2 `Pid_reuse_does_not_inherit_the_previous_counter` changes start ticks on the same pid. The next sample publishes nothing. The sample after that is 0.1 cores, not the pid-only delta.

V-3 `Vanished_pid_is_omitted_and_the_sibling_remains` lists a pid directory with no stat file. The sibling compiler remains and the scan does not throw.

V-4 `Top_count_keeps_eight_highest` ranks nine processes and keeps the eight highest.

V-4 `Projection_keeps_eight_publishable_rows` drops a slash name and an equals name and stops at eight.

V-5 `Command_line_and_environment_are_not_published` uses a 15-character comm so the command line is read, and writes an environment file. The published name is the basename and the JSON contains neither secret.

V-6 `Production_factory_selects_the_linux_proc_table` calls HostProcessTables.Create on Linux and resolves the process table from AddHostStats as LinuxHostProcessTable.

V-6 `Factory_selects_the_platform_reader` selects the Linux reader, the Windows reader, and the empty reader. The Windows reader on this host returns no rows.

V-7 `Blocking_scan_does_not_stretch_the_tick` finishes `SampleOnceAsync` while a table read is blocked. `Live_proc_scan_reads_proc_off_the_tick` reads live `/proc` on the scan path, not inside the tick. `Scan_stops_when_the_cpu_budget_is_spent` stops a synthetic scan at the 10 ms budget. `Cached_scan_is_stale_after_twice_the_cadence` flags an old cache.

V-7 `Short_interval_does_not_rescan_or_move_the_baseline` does not read again inside five seconds.

V-8 `Ninth_row_and_unsafe_name_are_rejected` makes HostLoadIngestService.Validate throw.

V-8 `Unknown_role_is_rejected` makes HostLoadIngestService.Validate throw.

V-8 `Valid_compiler_row_is_accepted` keeps schema 1.

V-8 `Null_cpu_and_rss_stay_publishable` keeps a row whose counters are absent.

V-8 `Schema_stays_one_and_session_names_stay_closed` keeps schema 1 and the closed session names.

V-8 `Old_history_without_top_consumers_deserializes_empty` accepts a payload that has no topConsumers field.

R-1 Existing sampler, sample, wire, spool, authority, store, endpoint, and probe-parse tests stay green, plus ProcessSpawnLimitTests in the runner project.

R-2 Policy, classification, slow-test, native-Linux witness, derived-impact, mechanical contract, platform guidance, layering, and ProcessSpawnLimitTests in Antiphon.Tests stay green.

Red witnesses are the new tests committed while ranking, classification, proc reads, and ingest checks return no rows. Mutation owns the deliberate pack. Every PC below stays pending.

### Positive controls

| PC | Mutant or row | Witness | Expected red |
|---|---|---|---|
| PC-1 | Key the CPU delta by pid only | `/*/*/HostTopConsumerTests/Pid_reuse_does_not_inherit_the_previous_counter` | The reused pid publishes on the first sighting of the new start, with cores above 2. |
| PC-2 | Publish the raw command line as the name | `/*/*/HostTopConsumerTests/Command_line_and_environment_are_not_published` | The JSON contains the command-line secret. |
| PC-3 | Drop the eight-row cap | `/*/*/HostTopConsumerTests/Top_count_keeps_eight_highest` | Nine rows are published. |
| PC-4 | Windows native row. Not a source mutant. | `/*/*/HostTopConsumerTests/Factory_selects_the_platform_reader` | Run on Windows so the reader uses toolhelp and process times. This Linux host does not discharge it. |

### Checkpoints

totalTimeoutMinutes: 90

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1884-host/ | host-top | `/*/*/(HostTopConsumerTests*)\|(C1514HostLoadSampleTests*)\|(HostStatsSamplerTests*)\|(C1514HostLoadWireTests*)\|(C1514HostLoadSpoolTests*)\|(C1514HostLoadAuthorityTests*)\|(ProcessSpawnLimitTests*)\|(HostStatsStoreTests*)\|(HostStatsEndpointTests*)\|(HostStatsProbeParseTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6, V-7, R-1 | >= 69 executed, 0 failed | 69 | 12 |
| CP-2 | S1 | tests/Antiphon.Tests -> bin-c1884-guards/ | guards | `/*/*/(HostTopConsumerIngestTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(ProcessSpawnLimitTests*)/*` | V-8, R-2 | >= 70 executed, 0 failed | 70 | 25 |
