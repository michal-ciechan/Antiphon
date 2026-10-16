# RC2 Windows roster qualification — 2026-10-16

**FAILED.** The frozen 361-class Windows roster failed both complete executions at **`b227f814c02a88ea2e4c9f0731c22d8ff0ffcd4d`**. Three failing tests reproduce alone on RC2 and pass alone on the previous candidate: confirmed regressions. A fourth case failed only in the first aggregate execution, passed in the repeat and passed alone on both candidates: observed intermittent failure, with introduction unresolved. No code was repaired and this report grants no release acceptance.

## Source and selection

The caller replaced the original `f7900158ef3416894c720d892bb1c21cc8095057` pin with `b227f814c02a88ea2e4c9f0731c22d8ff0ffcd4d`. Both detached HEAD and `git ls-remote origin refs/heads/rc/2026-10-15` matched the replacement before execution. All RC2 builds/tests below used that exact clean source, without rebuilding between aggregate and isolated runs. The previous candidate was independently built in a fresh detached checkout at **`067b47e2dc903247f1a7c8aa539032cc4266e4ad`**.

The roster, filter and reproduction document were read from `origin/master`, frozen at **`59ed791fa4c51ce72c7adb8581a10a383575d1ad`**, using `docs/investigations/2026-10-15-card-1287-windows-unit.md` and its adjacent `-roster.txt` and `-filter.txt` files. The published 48 whole-class exclusions were retained unchanged. They remain outside this gate, including its previously recorded Q1/Q2 findings. This execution does not reassess them.

Each aggregate TRX contains exactly **361 classes, 2,856 distinct methods and 4,527 cases**, with zero missing or extra roster classes. The method/case-count multisets match between runs. These are current-source counts; the historical 4,526-case / 2,855-method certificate is not reused. The extracted Git blobs use LF. Converting their line endings to CRLF reproduces the qualification document's frozen roster/filter SHA-256 values exactly (`67ecec5455bc80aa1e3f0f0f2dae583dc371dc90788d7ee7d27fa872988b7c97` and `5f3dfcc9cca230168380b1524982089ee67ba027feb2e769f60ed98d288d755a`). No filter operands were changed.

## Executions

All drivers ran sequentially through `scripts/build-slot.ps1`; all twelve leases were granted with zero slot wait and `maxcpucount=4`. No unleased fallback, `-NoSlot`, fail-fast setting, retry policy, timeout extension or assertion change was used. `TUNIT_MAX_PARALLEL_TESTS` was unset. No PTY test assembly was co-scheduled.

| Execution | Executed | Passed | Failed | Skipped | Outer wall | Driver exit |
|---|---:|---:|---:|---:|---:|---:|
| RC2 isolated build | — | — | — | — | 141.709s | 0 |
| RC2 gate1 | 4,527 | 4,523 | 4 | 0 | 253.109s (4m 13.109s) | 2 |
| RC2 gate2, identical selection and binary | 4,527 | 4,524 | 3 | 0 | 415.901s (6m 55.901s) | 2 |
| Previous-candidate isolated build | — | — | — | — | 124.646s | 0 |

The RC2 build had **0 errors / 1,060 warnings**, with MSBuild elapsed 140.30s. The previous-candidate build had **0 errors / 1,057 warnings**, with MSBuild elapsed 122.78s. TRX start/finish walls were **248.517s** and **410.471s** for gate1/gate2. The duration difference is retained as observed shared-host timing; its cause was not established.

Every unique failing case was then executed alone once at each candidate. Each row below represents exactly **one executed case per source**, no skips, using its exact method filter or the precise TUnit case UID. A passing isolation does not clear an aggregate failure.

| Case | RC2 passed / failed | RC2 outer wall | Previous passed / failed | Previous outer wall |
|---|---:|---:|---:|---:|
| `TestClassificationGuardTests.Registry_matches_compiled_metadata` | 0 / 1 | 9.107s | 1 / 0 | 4.943s |
| `AbsentLaunchPolicyTests.C1149_Queue_columns_are_accounted_for` | 0 / 1 | 6.585s | 1 / 0 | 3.548s |
| `TestClassificationPolicyTests.C487_G059` | 0 / 1 | 7.880s | 1 / 0 | 5.509s |
| `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation(deletion-sha)` | 1 / 0 | 6.284s | 1 / 0 | 5.543s |

Total: **two builds and ten test drivers**, including two complete roster executions and eight single-case comparisons. No test driver was stopped at its first failure. Full console output, error text and stack traces are retained, including the aggregate C762 sentinel diagnostics; those diagnostic lines are not counted as additional failed test cases.

## Failures and attribution

“Regression” here means the same existing test passes alone at `067b47e2dc903247f1a7c8aa539032cc4266e4ad` and fails alone at RC2. Introducing commits below are attributed from the exact source changes and history, **not from a per-commit execution bisection**. No failure was established as pre-existing at the previous candidate.

| Fully qualified failing case | Error | Classification | Introducing commit / evidence |
|---|---|---|---|
| `Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata` | Expected no registry diagnostics; got `missing classes: Antiphon.Tests.Application.SessionRuntimeActionDeferralTests`, `missing-reason Antiphon.Tests.Application.SessionRuntimeActionTests`, and `unregistered-marked Antiphon.Tests.Application.SessionRuntimeActionDeferralTests`. | **Confirmed regression**; fails in both aggregates and RC2 isolation, previous isolation passes. | First mismatching registration: `f4770aa921e818cc94aef90c95ee6d5613c00e28` adds `SessionRuntimeActionTests` to `slow-tests-allowlist.txt` without its own required reason comment. Additional missing/unregistered class: `59544042d8937d5e97b266098f494acee1ea5ab1` introduces marked `SessionRuntimeActionDeferralTests` without a registry entry. |
| `Antiphon.Tests.Application.AbsentLaunchPolicyTests.C1149_Queue_columns_are_accounted_for` | Reflected `SessionQueuedMessage` property set differs from the test's explicit column inventory: new `ReviewThreadId`, `ReviewReplyConsumedAt`, `RuntimeActionKey`, and seven `DirectReply*` properties. | **Confirmed regression**; fails in both aggregates and RC2 isolation, previous isolation passes. | `b8893359f3c4b27e41c7934bfb229cd16e152191` first adds the three review/runtime-action properties. `6288f26fea0ded62206d74e4f5166d3482ce921a` later adds the seven direct-reply properties. The inventory assertion was not updated. This is a failed schema-accounting contract; no additional product behavior is inferred. |
| `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G059` | `errors.ShouldNotContain("unregistered-marked")` fails for `SessionRuntimeActionDeferralTests`. | **Confirmed regression**; fails in both aggregates and RC2 isolation, previous isolation passes. Same registry omission as the first row. | `59544042d8937d5e97b266098f494acee1ea5ab1` adds the marked class without registering it. The earlier missing reason alone does not trigger this specific assertion. |
| `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation(deletion-sha)` | Line 186: preliminary `(await safe.RemoveAsync()).IsClean.ShouldBeTrue("the unchanged command path must be reachable")` receives `False`, before applying the requested authority-coordinate change. | **Observed intermittent aggregate failure**, not an established regression or pre-existing failure. Failed gate1, passed gate2 and both isolated comparisons. | **Unresolved.** This test file is unchanged between the two candidates. Passing baseline isolation cannot establish whether an aggregate-only failure predates RC2. The internal refusal reason was not exposed by this assertion; no mechanism is claimed. |

The actionable confirmed regressions group into two causes: classification-registry drift and an outdated queued-message column inventory. The intermittent landing-removal control remains a separate finding. Remediation or release disposition belongs to the caller; this task changes neither product nor test code.

## Reproduction and safety

Run at the exact candidate SHA with fresh results directories. The retained driver is `C:\src\Antiphon\.antiphon\investigations\rc2-windows-8003a58e\run.ps1`; its `argv.json` files contain the literal argument arrays for every execution. It runs local child processes synchronously and waits for completion. The commands follow the roster document, substituting task-specific output/results names:

```powershell
$evidence = 'C:\src\Antiphon\.antiphon\investigations\rc2-windows-8003a58e'
$rosterFilter = (Get-Content -Raw "$evidence\2026-10-15-card-1287-windows-unit-filter.txt").Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-windows-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-rc2-8003a58e/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-windows-gate1 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-rc2-8003a58e/ -- --treenode-filter $rosterFilter --output Detailed --report-trx --report-trx-filename run.trx --results-directory "$evidence\gate1"
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-windows-gate2 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-rc2-8003a58e/ -- --treenode-filter $rosterFilter --output Detailed --report-trx --report-trx-filename run.trx --results-directory "$evidence\gate2"
```

Before each child invocation, the retained driver clears inherited `PhoneHome__*`, `PHONE_HOME_*` and `C589_*` settings, sets `PhoneHome__Enabled=false`, and sets both phone-home origin forms to `http://127.0.0.1:1`. It sets `SessionRunner__BaseUrl=http://127.0.0.1:1`, disables check-interpreter/diagnose/output-distiller provisioning and disables Hangfire workers. The assembly's production-runner guard also runs. Reproduction must retain this environment sanitation. Only the required host `/build-slots` broker was accessed on 17204; no runner-session API, production AppHost on 17202, restart or deployment was used.

Exact isolated method filters:

```text
/*/Antiphon.TestSupport/TestClassificationGuardTests/Registry_matches_compiled_metadata
/*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Queue_columns_are_accounted_for
/*/Antiphon.Tests.TestHelpers/TestClassificationPolicyTests/C487_G059
```

The parameterized case instead used `--filter-uid 'Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests.1.1.C448_V36_EachAuthorityCoordinatePrecedesMutation(System.String).11.1.0'`, obtained from the aggregate HTML report's case data. TUnit 1.44's tree path ends with the method name, so its method filter would select all argument variants. The UID selected exactly the requested single `deletion-sha` case on each source. This follows the [MTP UID-filter contract](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-cli-options) and was checked against the pinned [TUnit filter implementation](https://github.com/thomhurst/TUnit/blob/v1.44.0/TUnit.Engine/Services/TestFilterService.cs).

## Retained evidence and cleanup

Persistent ignored evidence root: **`C:\src\Antiphon\.antiphon\investigations\rc2-windows-8003a58e`**. Every execution directory retains `stdout.log`, `stderr.log`, `argv.json`, `timing.json` and `processes.json`; every test directory also retains its fresh `run.trx`, HTML report, `cases.json` and `summary.json`. `pair-audit.json` records exact class/method coverage, shared/unique failures and artifact hashes. The root retains both assembly-provenance records, frozen roster/filter/document, source attribution diffs and drivers/auditors. Generated evidence is not committed.

| Artifact | SHA-256 |
|---|---|
| RC2 test assembly, informational version `1.0.0+b227f814c02a88ea2e4c9f0731c22d8ff0ffcd4d` | `9a7b5effebc0f4e67ae2e67fa92316bd4f95b225558e901537f7bd4bd25a2e84` |
| Previous-candidate test assembly | `5d5b7fdb6ff8ec67c977049199a8ba30e30017af0f35bdd383fb85cd19a9e80d` |
| gate1 TRX | `53c95f23bd475a98557842c6d809096e26dda88879e20d215dc582cabf58e21f` |
| gate2 TRX | `3ce4064b4429cd4da3aa7c1d47078bc353443242e3e4c9931ce83537f9b3a121` |
| Retained LF roster blob | `6cae348edf4b1d679f5fcdc5f1fb20ccd1f9357ccd6ea70db3b96a8da774a5c0` |
| Retained LF filter blob | `7424e044a0119774ebab2c79a17e97a93f51ed0c2312867115d3b057866f9eb1` |

All twelve drivers finished, and clean source/unchanged HEAD was recorded before and after each. RC2 assembly hashes remained identical across all runs. No owned leftover process was found after any driver; the final PID/creation-time and producer-executable-path census also found zero remaining or unknown task-owned processes, so no process termination was necessary. The census concerns this task's observed descendants and output paths, not a machine-wide orphan verdict.

Removed **56 inventoried alternate output directories** (28 per checkout), using literal Git pathspecs after an exact dry-run/inventory comparison. No such inventoried output remains. The clean baseline checkout was removed through ordinary `git worktree remove`. `final-process-census.json`, `cleanup-plan.json`, `output-cleanup.json` and `baseline-checkout-cleanup.json` retain the observations and results. Automatic review refused the initial combined process/deletion command and then a recursive PowerShell deletion command (`blocked by policy`); read-only ownership verification followed by the narrower literal-path Git cleanup completed successfully.

--- next stage ---
next: decide
handoff: Triage the two confirmed regression causes and the separate intermittent landing-removal control; commission repairs and fresh qualification if required. RC2 Windows roster gate remains failed.
artifact: docs/investigations/2026-10-16-rc2-windows-roster.md
