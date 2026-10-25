# L5 / Q Linux triage (task 2ee82266)

Completed: all 25 assigned Linux tests passed, zero failures/skips. No Linux
product or fixture defect reproduced; only this report changed. Historical Windows
L5 exit cause remains unobserved and requires the native diagnostic row below.
This is scoped Linux diagnostic evidence, not Windows qualification or release credit.

## Provenance and scope

- Baseline/fetched master: `e5a867361937752b959c1451824ab9716fac83c0`.
- Build and all test runs: clean committed source
  `0669d2473c11a0e010ad527bf095803a1c2e2506` (only the initial report differs from baseline).
- Host: `0c18bb7f591e`, Debian GNU/Linux 12, linux-x64, .NET 9.0.21,
  TUnit 1.44.0, Microsoft Testing Platform 2.2.2; 2026-10-25 UTC.
- Historical Windows extraction: branch `feat/card-task-b5042266`,
  `docs/investigations/2026-10-25-rc-a779085c-non-antiphon-failures.md`;
  run `a779085c09ec4397a4b49941fd3cad07`, candidate
  `a97194ee7a4adf215454fd82c453ba46362596ee`.
- No Unit/full-suite/native Windows runs, active qualification interaction, tags,
  release scripts, production changes or assertion changes.
- Ignored evidence root: `/work/worktrees/task-2ee82266/.antiphon/evidence/task-2ee82266/`.
  Raw logs/TRX, timing JSON, diagnostic script, selector summary and producer-owned
  output inventory are retained there; essential outcomes are preserved here for
  callers without mirror access. Raw evidence is not committed.

## Commands and results

One isolated build (exit 0; 0 errors, 55 existing warnings), MSBuild 34.23 seconds;
slot waited 0 seconds, held 35 seconds:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-2ee82266-build -SlotWaitMinutes 7 -- timeout 720 dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-debug-2ee82266/ --nologo
```

One compiled-output invocation per exact filter below; each exit 0. All four took
server2 build slots, waited 0 seconds, and ran sequentially. Invocation template:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-2ee82266-LANE -SlotWaitMinutes 7 -- timeout 300 dotnet exec tests/Antiphon.SessionRunner.Tests/bin-debug-2ee82266/Antiphon.SessionRunner.Tests.dll --treenode-filter 'FILTER' --report-trx --results-directory .antiphon/evidence/task-2ee82266/LANE
```

| Lane | Exact filter | Passed / failed / skipped | TRX wall s | Sum test s | Slot held s |
| --- | --- | --- | --- | --- | --- |
| L5 | `/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/C1032_RemovalComponentRecovery` | 1 / 0 / 0 | 26.835611 | 26.299271 | 28 |
| Q-argv | `/*/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests/*` | 15 / 0 / 0 | 2.649150 | 1.758109 | 4 |
| Q-publication | `/*/Antiphon.SessionRunner.Tests/AgentPinPublicationTests/*` | 6 / 0 / 0 | 1.941762 | 1.168100 | 3 |
| Q-store | `/*/Antiphon.SessionRunner.Tests/AgentPinWorkspaceStoreTests/*` | 3 / 0 / 0 | 1.236572 | 1.005682 | 3 |

Task budget 70 minutes; provisional diagnostic bounds: 7-minute admission,
12-minute build, 5-minute execution per filter, external wrapper cap 20 minutes
for build and 13 minutes per test invocation. No timeout occurred. These are
single-host samples, not measured p95. No DB/container fixture is selected.
Existing test-owned Git/filesystem, worker and loopback HTTP startup/body/teardown
costs are included in each test duration; their individual phase attribution is
unknown. Wall minus summed durations is unattributed runner/fixture remainder,
not a separately measured external-call span. Load was not sampled.

Built assembly SHA256:
`E37074449BE16A995E11FD92EF412EE5698EDA5434AC9C259E99E20E98838091`
(5,007,360 bytes). This assembly was run directly throughout; no further build.

| Lane | TRX SHA256 |
| --- | --- |
| L5 | `2A23D28C75B0E5469C59AD9333482EB970DC7E51757E62E61CAAFDACB8639D6F` |
| Q-argv | `DE812B18146804131291C7B8B4DEC913192E15C29D86DD9515CF7880E505BF2C` |
| Q-publication | `32B6A2435471249966F1248D036A879EF0FD41B56145DCC5EDB4689FC0657FAB` |
| Q-store | `A2C9FA3B296FBD3A837909F29DF246DE4AED2D094B3BFBC42C01E27C9967A8E7` |

## L5 conclusion and owed native diagnostic

`CompletedCardWorkspaceS5Tests.cs:439` iterates all 12 cuts: Intent, Prepared,
Moved, Unregistering, Unregistered, Deleting, Deleted, Complete, rename,
unregister, delete and partial-delete. The unchanged Linux test verified each
worker marker and kernel birth identity, killed/joined its owned worker, then
recovered the durable operation without replenishing the deletion allowance.
There was no early worker exit on Linux.

Historical Windows evidence records only `child.HasExited == true` before
`state:Intent`; it cannot establish the exit code or reason. The fixture reads
stdout/stderr but discards their values in its finally block
(`CompletedCardWorkspaceS5Tests.cs:462-481`). The worker has explicit terminal
codes 88 (RemoveAsync returned without reaching the requested cut), 89 (caught
exception; stderr currently names only the exception type), and 0 for its separate
FIFO mode (`CompletedCardRemovalCrashWorker.cs`). These are diagnostic possibilities,
not observed historical exit codes. No Windows cause or Linux repair is claimed.

| Native rerun row | Required selection and evidence | Status |
| --- | --- | --- |
| L5 Windows early worker exit | After the active qualification finishes, commission a Windows diagnostic on the exact L5 method filter above, with its own isolated build/slot. Preserve child exit code, drained stdout/stderr, requested cut, and RemoveAsync Complete/Reason if it returns before a cut. Keep the existing marker/identity/recovery assertions. The current worker must expose the returned result before exit 88, and the parent must attach captured output to its failure. | Owed; no native execution or speculative fixture/product change in this task. |

The 26-second Linux pass does not establish whether the native symptom is a
startup exception, a refused removal, or another Windows-specific path.
W1-W5 remain with their native owners.

## Why Windows selected Q and recommended fix

The historical and current `tests/test-execution-policy.json` both require the
`session-runner` suite in `profiles.rc.requiredSuites`. That suite maps to
`tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj`.
None of the three Q classes has a profile exclusion. Their Integration category,
class name and explicit in-body Linux assertion/guard are not selector metadata.

The executor reads platform-specific rows via
`scripts/lib/nightly-tests-impl.ps1:955` and
`Get-NightlyProfileExclusions` (`scripts/lib/nightly-policy.ps1:400`).
`Test-NightlyDiscoveryExcluded` and `Get-NightlyDiscoveryDisposition`
(`scripts/lib/nightly-coverage.ps1:713-763`) use declared class/method profile
rows; absent rows remain required. Launch grouping then retains those required
nodes (`Get-NightlyProfileExecutionChunks`, `nightly-tests-impl.ps1:28`).

Read-only diagnostic: joined all 24 executed Q TRX identities to class/method and
passed them through the actual policy disposition functions, loading both the
historical candidate policy and current policy. Both policies returned
**Windows required 24 / excluded 0**, and **Linux required 24 / excluded 0**,
with zero duplicate identities. This demonstrates the selector mechanism on
Linux; it is not Windows execution evidence.

Recommend a separate selector repair: add three **class-level RC exclusion rows
with `suite: session-runner`, `platform: windows`, reason and durable owner** for
UnixPtyArgvAdmissionTests, AgentPinPublicationTests and AgentPinWorkspaceStoreTests.
Verify the exact 24 expanded identities are excluded on Windows and remain required
on Linux, while unrelated SessionRunner nodes retain their existing disposition;
update the policy hash through its established helper. Keep the intended Linux
assertions and all three Linux class runs.

This is a multi-row policy/hash/selector-witness change, not a demonstrated one-line
test attribute fix, so it was recommended rather than implemented as requested.
A runtime skip alone would still leave required RC coverage nodes unexecuted.

## Per-test evidence

Durations below are TRX test durations; lanes map to the exact classes above.

| Lane | Expanded case | Outcome | Seconds |
| --- | --- | --- | --- |
| L5 | `C1032_RemovalComponentRecovery` | Passed | 26.299271 |
| Q-argv | `Platform_and_backend_boundaries_keep_existing_verdicts(platform_mismatch)` | Passed | 0.065814 |
| Q-argv | `Malformed_inputs_reach_existing_runtime_validation` | Passed | 0.036717 |
| Q-argv | `Null_exe_keeps_existing_validation_message_on_launch_routes(ordinary)` | Passed | 1.003026 |
| Q-argv | `Phone_home_nul_refusal_is_named(launch)` | Passed | 0.097002 |
| Q-argv | `Nul_request_is_refused_before_effects(grok_payload)` | Passed | 0.015785 |
| Q-argv | `Phone_home_nul_refusal_is_named(platform_constrained)` | Passed | 0.005051 |
| Q-argv | `Nul_request_is_refused_before_effects(grok)` | Passed | 0.000606 |
| Q-argv | `Platform_and_backend_boundaries_keep_existing_verdicts(herdr_nul)` | Passed | 0.349827 |
| Q-argv | `Null_exe_keeps_existing_validation_message_on_launch_routes(platform_constrained)` | Passed | 0.045276 |
| Q-argv | `Platform_and_backend_boundaries_keep_existing_verdicts(unknown_backend)` | Passed | 0.028358 |
| Q-argv | `Nul_refusal_is_named_on_both_launch_routes(ordinary)` | Passed | 0.071754 |
| Q-argv | `Nul_request_is_refused_before_effects(plain)` | Passed | 0.001012 |
| Q-argv | `Nul_refusal_is_named_on_both_launch_routes(platform_constrained)` | Passed | 0.020573 |
| Q-argv | `Nul_request_is_refused_before_effects(tracked)` | Passed | 0.004313 |
| Q-argv | `Platform_and_backend_boundaries_keep_existing_verdicts(platform_invalid)` | Passed | 0.012994 |
| Q-publication | `V14_PublicationFences` | Passed | 0.486613 |
| Q-publication | `V14_PublicationSerialization` | Passed | 0.045428 |
| Q-publication | `V34_PublicationAdmission` | Passed | 0.083017 |
| Q-publication | `V14_PublicationReplay` | Passed | 0.235309 |
| Q-publication | `V13_PublicationCustody` | Passed | 0.110440 |
| Q-publication | `V15_PublicationCleanup` | Passed | 0.207294 |
| Q-store | `V13_InspectPathSafety` | Passed | 0.326321 |
| Q-store | `V34_InspectProtocolAdmission` | Passed | 0.344946 |
| Q-store | `V32_InspectPosixPaths` | Passed | 0.334416 |

## Delivery and cleanup

The ten producer-owned `bin-debug-2ee82266` directories recorded in
`output-inventory.json` were removed after all runs; a fresh enumeration found
none remaining. Build/test processes completed before cleanup. Re-running the
commands above now requires the isolated build again.

Only this investigation report is delivered. No red test was weakened and no
product/fixture repair was invented for a passing Linux selection. The remaining
scope is the Windows L5 diagnostic and the separately commissioned selector policy
repair. Per the brief, this completed Linux Debug reports `next: land` for the
report; that does not approve a native/product fix or authorize release activation.
