# RC run a779085c group 49: Linux reproduction

All 40 selected cases passed on Linux. None of the 29 reported Windows failures
reproduced. No test, fixture, assertion, timeout or product code was changed.
This result does not qualify the Windows rc candidate or establish that its
failures are fixed.

## Source and execution

- Task: `f726d70b`; branch: `feat/card-task-f726d70b`.
- Tested source: `a0b51f75fc93b768c6b3bfce883e3378660c53c9`, clean tracked
  source before and after execution. This was fetched `origin/master` at start.
- Reported Windows candidate: `a97194ee7a4adf215454fd82c453ba46362596ee`.
  Native evidence was unavailable on this host; the failure counts below come
  from the commissioning brief.
- Host: server2/Linux, Debian 12, .NET 9.0.21, TUnit 1.44.0. No desktop,
  native qualification lock, Unit lane, release script or tags were used.
- Isolated build: `bin-g49/`, zero errors, 1197 warnings, including the existing
  ASP.NET route analyzer AD0001 exception warning. MSBuild elapsed 3m33.45s;
  build slot wait 0s, held 215s.
- Test slot wait 150s, held 125s. Native test summary duration 2m02.008s;
  TRX start `2026-10-25T01:03:10.3822803Z`, finish
  `2026-10-25T01:05:12.3040332Z`. Exit 0; 40 executed/passed, zero
  failed, skipped, aborted or timed out.
- The baseline run is also the final source result: no source repair was
  warranted, so no duplicate verification run was made. There is no red/green
  repair claim.
- Build hard cap 1200s including at most 10 minutes of slot admission; test
  cap 1200s including at most 5 minutes of slot admission. These were bounded
  Debug collection caps, not measured p95 timeout recommendations. Neither fired.
- Detailed setup, database, HTTP, filesystem and teardown phase costs were not
  separately captured and remain unknown; TRX retains per-case duration and
  exact membership. The host was shared and its interference was not quantified.

The compiled test DLL was 68,549,120 bytes, exceeding 64 MiB. Per the brief,
verification used the build-slot wrapper and `dotnet run --no-build` rather than
the checkpoint inventory reader. Its SHA-256 before cleanup was
`25cb9f0bb7e03208fd978eac48ecc7712222e822263c1fd8f80b66649b655931`.
These are manual execution records, not checkpoint qualification receipts.

## Per-class results and disposition

| Class | Reported Windows failures | Linux passed | Linux failed | Linux skipped | Disposition |
|---|---:|---:|---:|---:|---|
| PhoneHomeBackpressureTests | 6 | 6 | 0 | 0 | Not reproduced; existing shared phone-home fixture exercised |
| RunnerCodexCliEvidenceTests | 6 | 6 | 0 | 0 | Not reproduced; probe, freshness and transport assertions retained |
| HostStatsPollServiceTests | 6 | 6 | 0 | 0 | Not reproduced; poll/cache/transport cases retained |
| OutputDistillationDeadlineTests | 4 | 6 | 0 | 0 | Not reproduced; existing observed Claude bridge exercised |
| OutputDistillationProducerTests | 3 | 6 | 0 | 0 | Not reproduced; observed parent delivery and fallback cases retained |
| TaskParkRunnerIdentityTests | 2 | 2 | 0 | 0 | Not reproduced; real owned repositories and typed wire cases retained |
| PhoneHomeCompletedCardTransportTests | 1 | 2 | 0 | 0 | Not reproduced; Linux removal/replay case also executed |
| RunnerAlarmHostedServiceTests | 1 | 6 | 0 | 0 | Not reproduced; timer, wake, retry and recovery cases retained |

The delivery-shaped cluster already uses shared generation-bound Claude
observation helpers on the tested master. This is consistent with earlier
repairs, but does not attribute the original native failures to those repairs.
The remaining transport/host-stat/Codex/identity/alarm clusters are unresolved
native observations. No Windows path or shell defect was isolated, so there is
no supported pure-string Windows defect witness or justified product patch.
Native reruns must distinguish platform behavior, historical source differences
and shared-host timing/state using their actual diagnostics.

## Commands and retained evidence

Run from the repository root. The build and test are separate foreground leased
drivers; retain their exit codes. No implicit rebuild occurs in the test driver.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group49-base -SlotWaitMinutes 10 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-g49/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group49-base-tests -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-g49/ --property:UseAppHost=false -- --treenode-filter '/*/*/(PhoneHomeBackpressureTests*)|(RunnerCodexCliEvidenceTests*)|(HostStatsPollServiceTests*)|(OutputDistillationDeadlineTests*)|(OutputDistillationProducerTests*)|(TaskParkRunnerIdentityTests*)|(PhoneHomeCompletedCardTransportTests*)|(RunnerAlarmHostedServiceTests*)/*' --report-trx --report-trx-filename base.trx --results-directory .antiphon/group49/base
```

Actual invocations had the hard caps described above. Ignored raw evidence:

- `/work/worktrees/task-f726d70b/.antiphon/group49/build-base.log`
- `/work/worktrees/task-f726d70b/.antiphon/group49/tests-base.log`
- `/work/worktrees/task-f726d70b/.antiphon/group49/base/base.trx`
- `/work/worktrees/task-f726d70b/.antiphon/group49/base/base.trx.summary.json`

Raw evidence is local and may be lost when the mirror is retired. This committed
report preserves the source, commands, counts and limitations. Owned `bin-g49`
outputs are removed after verification; their path inventory remains ignored at
`.antiphon/group49/output-inventory.json`.

## Native rerun rows

Queue these on a fresh integrated Windows rc candidate under its qualification
owner. They grant no authority to interfere with the detached run. Build once
in an owned isolated output through a build slot, then reuse it for the exact
filter rows below, or combine their starred class operands as in the Linux run.
Do not treat Linux results as native coverage.

| Row | Exact tree filter | Cases at tested source |
|---|---|---:|
| N49-1 | `/*/*/PhoneHomeBackpressureTests/*` | 6 |
| N49-2 | `/*/*/RunnerCodexCliEvidenceTests/*` | 6 |
| N49-3 | `/*/*/HostStatsPollServiceTests/*` | 6 |
| N49-4 | `/*/*/OutputDistillationDeadlineTests/*` | 6 |
| N49-5 | `/*/*/OutputDistillationProducerTests/*` | 6 |
| N49-6 | `/*/*/TaskParkRunnerIdentityTests/*` | 2 |
| N49-7 | `/*/*/PhoneHomeCompletedCardTransportTests/*` | 1 Windows case plus 1 intentional Linux-only skip |
| N49-8 | `/*/*/RunnerAlarmHostedServiceTests/*` | 6 |

`C1032_TransportQueueRecipientReplay` explicitly skips off Linux. Keep that
qualification limitation visible. Retain native TRX diagnostics and per-case
durations if any of the original failures recur. No additional permission was
requested: the caller's standing Debug authority covered this work.
