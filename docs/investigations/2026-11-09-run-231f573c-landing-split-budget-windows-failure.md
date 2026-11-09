# Run dce78ee0 group-130 Windows failure: C1514LandingSplitBudgetTests.C1584_E2EExpandedBudget

Read-only extraction from the desktop qualification run dce78ee0 (candidate
`rc-2026-11-14-7bee43614495`, checkout `C:\Antiphon\releases\checkout-42be26dd` at
`7bee43614495293afbae1b0f4add019d11dacb9c`). The file name keeps the `run-231f573c` slug the brief
asked for; the failing run is dce78ee0, which superseded 231f573c.

Sources (not copied, no secrets in them):

- `logs/2026-11-09-1306-dce78ee0/antiphon-all-group-130-tests.log`
- `logs/2026-11-09-1306-dce78ee0/antiphon-all-group-130.trx`
- `logs/2026-11-09-1306-dce78ee0/antiphon-all-group-130.timing.json`
- `logs/2026-11-09-1306-dce78ee0/antiphon-all-group-130-execution/log_261109191221434.diag`

## Verdict

**Deterministic: an assertion on repository state, not load or timing.** The C1584 source census
found four E2E `[Test]` methods in the source tree that are missing from the committed roster
fixture `scripts/fixtures/nightly/c1584-e2e-source-roster.txt`. The script threw
`expanded-source-roster-changed` before it computed any launch bound. The test reads files only and
runs no process that could be slowed by load. It fails the same way on any host and on master, at
every commit since 2026-11-08 19:00Z.

## Failed test

| Field | Value |
|---|---|
| Class | `Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests` (`[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, no platform gate) |
| Test | `C1584_E2EExpandedBudget` |
| Harness case | `test-nightly-tests.ps1` prefix `C1584`, case `C1584_E2EExpandedBudget`, 1 row |
| Phase id | `Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests.1.1.C1584_E2EExpandedBudget.1.1.0` |
| Outcome | Failed |
| Duration | 00:00:03.0895851 (19:12:37.9386518Z to 19:12:41.0282369Z) |
| Host | DESKTOP-KTLKPIF |

### Error message (tests.log, verbatim)

```
failed C1584_E2EExpandedBudget (3s 089ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.ExitCode
      should be
  0
      but was
  1

  Additional Info:
      FAIL C1584 E2E expanded cases fit conservative launch bounds with every required node once - setup-error: expanded-source-roster-changed: added=Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests|Every_windows_path_setting_resolves_inside_the_fixture_workspace|;Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests|Fixture_boot_creates_no_backslash_named_entry_and_teardown_removes_the_interpreter_directory|;Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests|Fixture_boot_leaves_the_user_claude_config_byte_identical_and_seeds_its_own|;Antiphon.E2E.LandDeliveryFixtureProbeTests|Notification_scan_reads_the_land_clock| removed= at At C:\Antiphon\releases\checkout-42be26dd\scripts\test-nightly-tests.ps1:1981 char:13
  +             throw ('expanded-source-roster-changed: added=' + ($added .
  +             ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

  C487: 0 passed, 1 failed, 1 rows
    C1584 E2E expanded cases fit conservative launch bounds with every required node once : setup-error: expanded-source-roster-changed: added=<same four identities> removed= at At C:\Antiphon\releases\checkout-42be26dd\scripts\test-nightly-tests.ps1:1981 char:13
  C487 HARNESS EXIT CODE: 1  (FAIL - do not report this run as green)
```

### Stack (tests.log, verbatim)

```
    at Antiphon.Tests.Scripts.ScriptHarness.Validate(ScriptHarnessResult result, String prefix, String caseName, Int32 expectedRows, IReadOnlyList`1 requiredRows) in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:25
    at Antiphon.Tests.Scripts.ScriptHarness.<>c__DisplayClass1_0.<RunHarnessCaseAsync>b__0(ScriptHarnessResult result) in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Scripts\ScriptHarness.cs:18
    at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:132
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at Antiphon.Tests.Scripts.ScriptHarnessProcess.RunAsync(String harness, String prefix, String caseName, String script, ScriptHarnessOptions options, CancellationToken callerToken, Action`1 validate) in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Scripts\ScriptHarnessProcess.cs:193
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
    at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
    (the three lines above repeat three more times)
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
  Standard output
    ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests.1.1.C1584_E2EExpandedBudget.1.1.0
```

The whole trace is under 40 lines. The `.diag` (lines 161-227) repeats the same message and adds no
other frame.

### Timeout, process, git, pwsh and path text

- **Timeout:** none. The diag has no timeout text for this test. Its 30 `timeout` hits are test or
  class names such as `ProcessSpawnTimeoutPolicyTests`.
- **Process:** a single pwsh harness child (`ScriptHarnessProcess.RunAsync`) exited with 1 after
  about 3 s. There was no kill and no watchdog.
- **git:** none. The case reads only source files.
- **pwsh path:** `C:\Antiphon\releases\checkout-42be26dd\scripts\test-nightly-tests.ps1:1981 char:13`.
- **Fixture read:** `scripts/fixtures/nightly/c1584-e2e-source-roster.txt` (193 non-empty lines at
  7bee43614).

## Code that throws (checkout 7bee43614, `scripts/test-nightly-tests.ps1` 1948-1993)

`Test-C1584_E2EExpandedBudget` performs these steps:

1. It regex-scans every `tests/Antiphon.E2E/**/*.cs` file, plus `tests/Shared/TestClassificationGuardTests.cs`, for `[Test]` methods.
2. It builds `namespace.Class|Method|Arguments`.
3. It compares the sorted list case-sensitively with the committed roster file:

```powershell
$expected = @(Get-Content (Join-Path $here 'fixtures/nightly/c1584-e2e-source-roster.txt') | ... | Sort-Object)
$actual = @($roster | Sort-Object)
if (($expected -join "`n") -cne ($actual -join "`n")) {
    $added = @($actual | Where-Object { $_ -cnotin $expected })
    $removed = @($expected | Where-Object { $_ -cnotin $actual })
    throw ('expanded-source-roster-changed: added=' + ($added -join ';') + ' removed=' + ($removed -join ';'))
}
...
if ($nodes.Count -ne 188 -or $required.Count -ne 181) { $reasons.Add('source-census') }
```

After the roster is updated, the hard-coded census `188` / `181` (line ~1990) and its comment also
have to change. With four new nodes the total becomes 192. The required count depends on whether the
Windows rc profile excludes any of the new nodes.

## The four unrostered identities and the commits that added them

| Identity | Added by |
|---|---|
| `Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests\|Every_windows_path_setting_resolves_inside_the_fixture_workspace\|` | `9f009f299` test(card-1795), 2026-11-08 19:00Z (refined by `3cc9e471c`) |
| `Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests\|Fixture_boot_creates_no_backslash_named_entry_and_teardown_removes_the_interpreter_directory\|` | `9f009f299` / `3cc9e471c` test(card-1795) |
| `Antiphon.E2E.Fixtures.AntiphonAppFixtureHostPathTests\|Fixture_boot_leaves_the_user_claude_config_byte_identical_and_seeds_its_own\|` | `5aee4e8e8` test(card-1797), 2026-11-09 01:06Z (fix `a28982a5d`) |
| `Antiphon.E2E.LandDeliveryFixtureProbeTests\|Notification_scan_reads_the_land_clock\|` | `906533773` test: rc a6410818 e2e-split-5, 2026-11-09 03:22Z |

None of these commits touches `scripts/fixtures/nightly/c1584-e2e-source-roster.txt` or
`scripts/test-nightly-tests.ps1`. The roster was last changed in `4c1296357` (2026-11-04, group-125).
On `origin/master` at `965ab0803`, all four tests are present and none is in the roster. **Master is
red on this test too.**

## Neighbouring tests in the chunk (TRX, start order)

| # | Class | Test | Duration | Outcome |
|---|---|---|---|---|
| 0 | CacheFixtureCompletionTests | Retired_cache_inspect_failure_is_not_absence | 0.347 s | Passed |
| 1 | CacheFixtureCompletionTests | Modern_daemon_missing_volume_text_is_positive_absence | 0.201 s | Passed |
| 2 | C1514LandingSplitBudgetTests | G2865_Group28Group65MeasuredBudget | 4.155 s | Passed |
| 3 | C1514LandingSplitBudgetTests | **C1584_E2EExpandedBudget** | **3.090 s** | **Failed** |
| 4 | C1514LandingSplitBudgetTests | C1404_Group10MeasuredBudget | 3.359 s | Passed |
| 5 | C1514LandingSplitBudgetTests | C1410_MeasuredCostGroups | 18.736 s | Passed |
| 6 | CacheFixtureCompletionTests | Fixture_namespace_inspect_failure_is_not_free | 0.267 s | Passed |
| 7 | C1514LandingSplitBudgetTests | C1577_Group71MeasuredBudget | 2.864 s | Passed |
| 8 | CacheFixtureCompletionTests | Bridge_retains_diagnostics_and_refuses_failed_transport | 0.840 s | Passed |
| 9 | CacheFixtureCompletionTests | Retention_failed_volume_inspect_is_not_absence | 1.872 s | Passed |
| 10 | CacheFixtureCompletionTests | Seed_metadata_pipeline_failure_is_refused | 1.338 s | Passed |

The four sibling cases in the same class, which use the same harness, passed in 2.9-18.7 s.

## Host load hints (timing.json and TRX)

- Chunk: 393 tests, 392 passed, 1 failed, 0 timeout or aborted. Duration 20 min 31.6 s
  (19:12:21Z to 19:32:54Z). `runWallSeconds` 1232.8, `buildSeconds` 131.1, `queueWaitSeconds` 0,
  `fixtureSetupSeconds` 3.9.
- Per-test time: p50 1.121 s, p95 13.35 s, max 72.6 s (`C1336_NightlyCandidateIdentity`).
- The failure came 16 s into the chunk. It took 3.1 s, close to the sibling harness cases. Nothing
  points to starvation, and load cannot add identities to a source file.

## Diagnosis

The failing assertion is `ScriptHarness.cs:25`, the harness exit code. That exit code comes from
`test-nightly-tests.ps1:1981` throwing `expanded-source-roster-changed` with `added=` four
identities and `removed=` empty. The guard is a closed census by design: it makes every new E2E
`[Test]` be recorded in the C1584 roster and cost bound. The authors of CARD-1795, CARD-1797 and the
a6410818 e2e-split-5 witness added E2E tests and did not refresh the roster.

**Classification:** deterministic stale fixture. It is not a product defect and not load or timing.

**Repair for Debug:**

1. Add the four lines above to `scripts/fixtures/nightly/c1584-e2e-source-roster.txt`.
2. Update the census `188`/`181` and its comment in `Test-C1584_E2EExpandedBudget`.
3. Add any needed per-case bound or cost row, following what `4c1296357` and `635e90cca` did for
   earlier additions.
4. Check it with the one filter `/*/Antiphon.Tests.Scripts/C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget`.
5. Land it on master and on the rc line.

**Why it was missed before the rc run (for the Debug why-missed note):**

- The three introducing changes touched only `tests/Antiphon.E2E/**`.
- The guard that pins that tree lives in `Antiphon.Tests`, under `Scripts/C1514LandingSplitBudgetTests` (Integration).
- Narrow author and Review filters do not select that class for an E2E-only path.
- The cheapest pre-land check is the impacted-classes entry `tests/Antiphon.E2E/**/*.cs` ->
  `/*/Antiphon.Tests.Scripts/C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget`.
- No earlier why-missed note covers this cause, so the exemption is `none`.
