# RC2 final Windows roster

The pinned RC2 candidate passed both fresh executions of the frozen 361-class Windows qualification roster: **4,537 passed, 0 failed, 0 skipped per run**. Test qualification is complete. Generated-output cleanup remains blocked by automatic approval review; the 28 inventoried output roots remain. No code was fixed.

## Candidate and selection

- Candidate: `b7265a05aa8773f08dbf457b93c7561e7874822d`.
- Before building, both checkout HEAD and `git ls-remote origin refs/heads/rc/2026-10-15` equaled that exact SHA.
- Selection owner: `docs/investigations/2026-10-15-card-1287-windows-unit.md`, read from fetched `origin/master` at `cd038acb3b62efbb67431866449ed6152acdb0db`.
- Its adjacent roster and literal filter were retained without selection edits. Their SHA-256 values match the original qualification: roster `67ecec5455bc80aa1e3f0f0f2dae583dc371dc90788d7ee7d27fa872988b7c97`, filter `5f3dfcc9cca230168380b1524982089ee67ba027feb2e769f60ed98d288d755a`.
- One isolated build was reused for both runs. Assembly informational version: `1.0.0+b7265a05aa8773f08dbf457b93c7561e7874822d`. Main assembly SHA-256 before/after both runs: `a7afbe4a95a89e26dbfe8f7892a03e9c2a4e7a631f47b9dc60463aec0c114ee1`.
- Tracked status was clean before/after the build and each test run. This narrative was written afterward.

## Results and cost

| Driver | Executed | Passed | Failed | Skipped | Classes | Methods | Outer wall | TRX wall | Slot wait | Exit |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Isolated build | — | — | — | — | — | — | 219.204s | — | 0s | 0 |
| gate1 | 4,537 | 4,537 | 0 | 0 | 361 | 2,856 | 336.086s | 226.415s | 105s | 0 |
| gate2 | 4,537 | 4,537 | 0 | 0 | 361 | 2,856 | 212.726s | 209.167s | 0s | 0 |

Build: **0 errors, 1,060 warnings**, MSBuild elapsed 205.00s. Total measured driver wall: **768.017s (12m 48.017s)**, including queue/release overhead. TRX walls are elapsed start/finish times, not summed case durations.

Each TRX contains exactly the frozen 361 class identities, with zero missing/extra classes. Both contain the same 2,856 executed methods. Class/method/outcome count multisets match exactly; every case passed in both runs.

The historical qualification had 4,526 cases and 2,855 methods. Comparison with its retained `gate1/cases.json` accounts for the entire increase: one new `CodexResponseAnalyzerTests.Plain_echo_takes_precedence_over_an_earlier_cursor_separated_match` case and ten additional parameter rows in `AbsentLaunchPolicyTests.C1149_Whitelist_requires_every_fact` (76 to 86). This is unchanged class selection against newer source.

Previously reported registry, classification-policy and queue-column regressions are green within both aggregate runs: all 23 `AgentRegistrySettingsTests` cases, all 25 `TestClassificationPolicyTests` cases, `TestClassificationGuardTests.Registry_matches_compiled_metadata`, and all 87 `AbsentLaunchPolicyTests` cases, including `C1149_Queue_columns_are_accounted_for`.

## Failure attribution

| Failure name | Error | Classification | Introducing commit |
|---|---|---|---|
| None observed in either complete roster execution | — | No observed failure or regression in this selection | Not applicable |

No failing case existed to rerun alone or execute at previous candidate `067b47e2dc903247f1a7c8aa539032cc4266e4ad`. No baseline build, retry, bisection, assertion relaxation, timeout adjustment or repair was necessary. The potentially intermittent landing-removal case did not cause a failure.

This qualifies only the frozen Windows roster at the pinned RC2 source. Excluded classes, newly added classes outside the roster, broader integration/native/platform lanes and release acceptance remain outside this report.

## Execution and production isolation

Every build/test driver went through `scripts/build-slot.ps1`; all three received granted leases with `maxcpucount=4`. No unleased fallback or `-NoSlot` was used. Gate1 waited for the host memory floor. The build's release request returned status 0/no response after 207s; the wrapper reported that its lease would be reaped on holder exit. A subsequent broker observation showed zero occupied leases before gate1 was admitted. Both test leases released successfully.

The child environment removed inherited phone-home settings, disabled `PhoneHome__Enabled`, supplied a closed-loopback origin and nonexistent secret path, pointed `SessionRunner__BaseUrl` to `http://127.0.0.1:1`, and disabled check interpreter, diagnose, output distiller and Hangfire worker startup. `TUNIT_MAX_PARALLEL_TESTS` was unset. The assembly's existing production-runner guard remained active. The only task-directed production-runner HTTP access was the required build-slot broker; no production session or AppHost operations were performed.

All foreground drivers finished. Ownership cleanup validated creation times and root-reachable process ancestry before checking live PID/start-time/executable identities. Raw ancestry snapshots include pre-existing processes through PID reuse and are not ownership certificates; `validated-process-ownership.json` and the final audit exclude those records. The task stopped its own leftover compiler/conhost processes; **zero validated owned processes remain**.

**Cleanup limitation:** automatic approval review rejected deletion of the 28 producer-inventoried `bin-rc2-ec89e544` directories, including literal-path recursive removal and a narrower per-file/empty-directory removal attempt. Its only stated reason was `blocked by policy`. No generated output was deleted. Caller cleanup is needed using the exact retained inventory; no production outputs are in that inventory. Generated evidence/output remains ignored and is not committed.

## Persistent evidence and reproduction

Evidence root: `C:\src\Antiphon\.antiphon\investigations\rc2-ec89e544`.

- `candidate-build/`: argv, full stdout/stderr, timings, source status and assembly hash.
- `gate1/run.trx` and `gate2/run.trx`: fresh complete results; adjacent `cases.json`, `audit.json`, logs, argv and timing receipts.
- `source-binding.json`, `hashes.json`, `historical-count-delta.json`, `repeat-comparison.json`: identity/count audits.
- `roster.txt`, `filter.txt`, `qualification.md`: retained origin/master inputs.
- `candidate-output-inventory.txt`, `output-cleanup-blocked.json`, `build-child-cleanup.json`, `final-process-cleanup.json`, `final-process-audit.json`: cleanup evidence.
- `run-owned.ps1`, `audit.ps1`, `cleanup-owned.ps1`: local inherited driver/audit scripts.

TRX SHA-256:

| Run | SHA-256 |
|---|---|
| gate1 | `87b7e776e29ebce46e74d1ca2d841c1137eab59f107bf5daa0eac75bdef6a1eb` |
| gate2 | `e67da70f7e36b18f9a8ca558d9c35a18a8b7173a05be987eaa97a7946388c59e` |

The exact literal driver arrays are in each `argv.json`. From the pinned clean candidate root, the commands have the selection owner's shape below; use fresh result directories and the sanitized child environment described above:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-candidate-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-rc2-ec89e544/ --nologo
$rosterFilter = (Get-Content -Raw 'C:\src\Antiphon\.antiphon\investigations\rc2-ec89e544\filter.txt').Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-gate1 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-rc2-ec89e544/ -- --treenode-filter $rosterFilter --output Detailed --report-trx --report-trx-filename run.trx --results-directory '<fresh-absolute-results-1>'
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-gate2 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-rc2-ec89e544/ -- --treenode-filter $rosterFilter --output Detailed --report-trx --report-trx-filename run.trx --results-directory '<fresh-absolute-results-2>'
```

--- next stage ---
next: decide
handoff: The pinned RC2 Windows roster passed twice; caller must arrange cleanup of the 28 inventoried generated output roots after automatic approval review blocked removal. No test repair is requested.
artifact: docs/investigations/2026-10-16-rc2-final-windows-roster.md
