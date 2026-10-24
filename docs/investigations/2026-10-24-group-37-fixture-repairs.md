# Group 37: dispatch projection, alarms and sweeps

Four failures reproduced on Linux at the required base; all four pass after three fixture-only repairs. No production code, assertions, timeouts, release configuration or sibling-owned classes changed.

## Source and changes

Task: `22bd72e0-f737-43db-a619-e729aa315406`, branch `feat/card-task-22bd72e0`.
Baseline: `dc1a2184526085b8f4305878a3804f9cec41daad`.
Repaired test source: `45a099924786a59127bc1c8bcda002a72d531d63`.

- `6ab92deaaba011a32eadcb5ebc88318f5dc431ae`: AgentTaskLandQueueAgingTests uses `ClaudeDialogFixture.CreateBridgeAsync`. Both C641 busy/idle cases previously had no submitted body because the default bridge supplied no current Claude dialog observation.
- `7ee13ea472c4ae3ca079b3c97afbd948402cb0cf`: ScheduleSweepTests.RegisterIdle binds the fake adapter to its persisted session generation through `EnableClaudeDialogObservation`. The idle delivery case previously reported Enqueued instead of Delivered.
- `45a099924786a59127bc1c8bcda002a72d531d63`: RunnerAlarmDeliveryTests scopes its post-insert read fault to the queued-message projection, identified by its table and NoteHeader column. The unrestricted next-read fault hit `session-state.seed`, poisoning the working-state cache during its retry interval; the subsequent immediate hints could not flush. The test still requires exactly one injected exception, one durable row, a complete UserPrompt and Sent status. This repair restores the test's intended queue-refresh failure boundary.

Each slice was committed and pushed before verification. No new product defect was established. The supplied C514 examples belong to RemoteControlRecoveryTests, C641 to AgentTaskLandQueueAgingTests, and Queue_reaches_recipient_when_busy_or_already_idle to PhoneHomeQueuedTurnTests.

## Results and native rerun rows

All rows below remain native Windows rerun obligations after land. Use each named class as `/*/Antiphon.Tests.Application/<Class>/*`; these Linux results do not qualify Windows.

| Class | Baseline pass/fail | Repaired pass/fail | Native row |
| --- | --- | --- | --- |
| PhoneHomeTaskDispatchProjectionTests | 18/0 | unchanged; not repeated | N37-1 |
| RunnerAlarmDeliveryTests | 8/1 | 9/0 | N37-2 |
| RemoteControlRecoveryTests | 17/0 | unchanged; not repeated | N37-3 |
| RunnerSeatOrphanSweepTests | 17/0 | unchanged; not repeated | N37-4 |
| ContextCompactionSweepTests | 18/0 | unchanged; not repeated | N37-5 |
| CardWorktreeCleanupRecoveryTests | 6/0 | unchanged; not repeated | N37-6 |
| AgentTaskLandQueueAgingTests | 7/2 | 9/0 | N37-7 |
| PhoneHomeQueuedTurnTests | 17/0 | unchanged; not repeated | N37-8 |
| ScheduleSweepTests | 17/1 | 18/0 | N37-9 |
| ChannelOutboundRecoveryTests | 18/0 | unchanged; not repeated | N37-10 |

Baseline: 147 executed, 143 passed, 4 failed, zero skipped; 8m09.975s. The historical 438-case group included more than these ten assigned classes. Repaired selection: 36 executed, 36 passed, zero failed/skipped; 1m11.969s. Seven unchanged classes account for 111 baseline passes; their historical Windows failures did not reproduce. No Windows path/shell semantics cluster was established, so no speculative platform fix or pure-string witness was added.

## Commands and evidence

Builds and tests all used `scripts/build-slot.ps1`; every lease was granted immediately with maxcpucount 6. The baseline and repaired builds completed with zero errors and 1197 warnings each, including the inherited route analyzer exception. Cold build: 7m31.84s (slot held 453s). Repair build: 1m52.66s (slot held 113s). Test slot holds were 494s and 75s respectively. No slot timeout occurred.

Build command (900-second cold / 600-second repair outer bound, three-minute slot wait):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug37-build -SlotWaitMinutes 3 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-debug37/ --nologo
```

Baseline filter, also the combined native rerun selection (147 cases on this source):

```text
/*/Antiphon.Tests.Application/(PhoneHomeTaskDispatchProjectionTests*)|(RunnerAlarmDeliveryTests*)|(RemoteControlRecoveryTests*)|(RunnerSeatOrphanSweepTests*)|(ContextCompactionSweepTests*)|(CardWorktreeCleanupRecoveryTests*)|(AgentTaskLandQueueAgingTests*)|(PhoneHomeQueuedTurnTests*)|(ScheduleSweepTests*)|(ChannelOutboundRecoveryTests*)/*
```

Repaired filter:

```text
/*/Antiphon.Tests.Application/(AgentTaskLandQueueAgingTests*)|(RunnerAlarmDeliveryTests*)|(ScheduleSweepTests*)/*
```

Run a built selection through the gate, passing the chosen literal filter as one argument:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug37-tests -SlotWaitMinutes 3 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-debug37/ -- --treenode-filter '<filter above>' --report-trx --report-trx-filename results.trx --results-directory '<fresh results directory>'
```

The baseline outer bound was 1200 seconds, including admission; repaired selection 300 seconds with a two-minute slot wait. These bounds were provisional operational caps within the commissioned 75-minute task, not measured p95 recommendations or changes to test deadlines. No Unit lane, desktop/native lock, release script, tag or desktop process was used. The checkpoint inventory reader workaround was explicitly commissioned: this test DLL is 68,510,720 bytes, over 64 MiB.

Local generated evidence is under `.antiphon/debug-22bd72e0/`: build logs, baseline/repaired logs and TRX, per-class JSON summaries, source/DLL provenance and the 32-directory producer-owned output inventory. TRX retains method membership and per-test durations. Shared PostgreSQL setup is once per assembly, with isolated database clones where the existing fixtures request them. Fine-grained setup, clone, IO/HTTP/database call and teardown phase attribution was unavailable and remains unknown; class nested duration totals are not wall times.

Baseline DLL SHA-256: `11b08a8337dc755529cd382717fb000e61365801efadef5a60e5bb7359e6d215`.
Repaired DLL SHA-256: `8ae60ece02104db474e56e49c140beee843cffdb6f24a9864792b903b5ff9266`.
Host: server2/Linux, Debian 12, .NET 9.0.21, TUnit 1.44.0.

The unchanged classes were not repeated after the scoped fixture repairs. Native rows remain necessary to resolve historical Windows-only behavior; this report claims Linux fixture verification only. Operator brief explicitly requests land without Review for these fixture-only repairs.
