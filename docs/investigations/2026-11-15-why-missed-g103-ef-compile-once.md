# Why g103's EF compile-once witnesses failed

The two failures are test-cache isolation defects. EF's shared compiled-query cache
can already contain the dispatcher's queries when a new fixture installs its
`IQueryExpressionInterceptor`. Executing a cached query does not emit a new
compilation event. Consequently both witnesses can observe `probe.Total == 0`
even though the SQL executes three times and the production query shape is stable.

Debug task: `5c14b3c5-dcc7-4607-afa5-89c20afec892`, Linux server2, 2026-11-15.
Starting HEAD and freshly fetched `origin/master` were both
`4ce55f36794f3e2fa904d56ba980f4182d89ab3a`.

## Historical attribution

The brief's claimed earlier green baseline cannot be credited for these methods:
`git show b559bfba2:tests/Antiphon.Tests/Application/EfCompiledQueryCacheTests.cs`
reports that the file does not exist at
`b559bfba2e9a420495122d27171352fe9b7e1ba4`. Thus there is no green-to-red test
transition to bisect between that pin and master. The actual source history is:

| Commit | Relevant change |
|---|---|
| `541eae6d18c48d1aab785e645d13299d6b12cb34` | Introduced both witnesses and their shared-cache fixture. |
| `3cdd5a4c62c417cb2cb481d0cad09dfdc085971c` | Hoisted the instance clocks in both production queries. |
| `f658c8bd6375221fd1174a2cdd12d93a9fc20484` | Restored the bulk expiry update's inline per-row `Guid.NewGuid()`. |
| Merge batch 227, published through `f701b2ba967d503427d7064fe4fffa17b8c1738d` | Landed the witnesses and fixes. |

Both query blocks are byte-identical between `f658c8bd6375` and the Debug base.
The later dispatcher changes concern ref-only workspace preparation. They do not
change expiry or delivery-failure recovery. No new clock literal or SQL parameter
was introduced in these two queries after the original fix.

## Reproduction and SQL

Three fresh-process selections on unchanged master passed: expiry alone 1/1,
recovery alone 1/1, and both together 2/2. This does not reproduce the broader
Windows process's prior cache state.

The committed red witness repeats each existing test through two independently
built service providers, retaining the three calls and five-second clock advances
inside each fixture. Production code is unchanged. Both tests then fail on the
second fixture with the same assertion as the Windows report:

| Witness | First fixture `(Count, Total)` | Second fixture `(Count, Total)` | Expected |
|---|---|---|---|
| Optional-work expiry | `(1, 3)` | `(0, 0)` | `Total > 0`, target `Count == 1` |
| Delivery-failure recovery | `(1, 1)` | `(0, 0)` | `Total > 0`, target `Count == 1` |

The red commit is `95910d4cb3270e8be2344df737e8eae286e8ce68`.
Its exact two-method selection executed 2, passed 0, failed 2, skipped 0.
These are assertion failures, not build failures, timeouts or empty selections.

Command logging proves that the second fixture still executes each target query
three times. SQL and parameter names/types are unchanged across fixtures and
ticks. The expiry command uses one `DateTime` parameter:

```sql
UPDATE "AgentTasks" AS a
SET "ConcurrencyToken" = gen_random_uuid(),
    "FailureReason" = 'Optional work expired before execution.',
    "CompletedAt" = @__expiredAt_0,
    "Status" = 6
WHERE (a."Role" = 12 OR (a."Role" = 11 AND a."SpecialistInputPolicyJson" IS NOT NULL)
       OR a."OutboundDeliveryId" IS NOT NULL)
  AND a."Status" = 0 AND a."ExecutionDeadlineAt" <= @__expiredAt_0
```

Delivery recovery uses `@__now_0` (`DateTime`) and `@__p_1` (`Int32`, the
128-row limit):

```sql
SELECT a."Id"
FROM "AgentTaskLandNotifications" AS a
WHERE a."Kind" = 5 AND a."State" <> 3 AND a."State" <> 4
  AND a."NextAttemptAt" <= @__now_0
ORDER BY a."NextAttemptAt", a."Id"
LIMIT @__p_1
```

The fixture advances its injected UTC clock through 00:00:00, 00:00:05 and
00:00:10 on 2026-11-14. Those values are parameters, not literals in changing SQL.
Command logs retain EF's default parameter-value redaction. There is no differing
SQL to explain an extra compile: the observed defect is zero new compilations
from an already warm cache. The production static-clock and per-row GUID rules
remain intact.

## Repair

`c02546483e923a1c88f0b9114184bcc5b85bbe2e` gives each dispatcher fixture an owned
`IMemoryCache` through `AddMemoryCache` and `UseMemoryCache`. Every scope/tick in
that fixture shares it; a later fixture starts with its own cache. The identity
compile-count witness also owns and disposes its cache. Database clones alone
did not provide this isolation.

The two-fixture repetition remains in both dispatcher witnesses, along with SQL
and count diagnostics. The exact `Count == 1` assertions remain unchanged. The
green evidence records expiry `(1, 3)` twice and recovery `(1, 1)` twice. No
production source, expectation, clock authority or timeout was changed.

## Why pre-land verification missed it

Review `ec0488ff-0329-4402-a62d-980437ce8ceb` recorded CP-1 query-cache/provider/
source guards as **14/14 passed** at
`1f44a31a694edab5a1f3ce160b9a0505ef3adf43`. Its overall verdict was Found with
incomplete verification and a separate production-clock witness gap. This note
does not upgrade that verdict.

Merge `fc30e791-9d7d-44a0-8229-361ceeb35980` (batch 227) also recorded CP-1 as
**14/14 passed**. Its report says the operator's standing authority covered landing
without Review approval. Both records were retrieved through their task-detail
GET routes and retained in this task's ignored evidence directory.

These selections included the tests. They did not ensure that a second fixture
could observe compilation after an earlier fixture had compiled the same shape.
The original witnesses depended on process composition. The exact prior warmer
in Windows run `b7812833` was not identified here; no live Windows run was touched.
The deterministic Linux repetition reproduces both reported assertion signatures.

The cheapest pre-land selector row is the existing four-test class, now with the
second-fixture regression inside its existing methods:

| Row | Project | Exact filter | Expected tests |
|---|---|---|---:|
| g103 compile-cache | `tests/Antiphon.Tests` | `/*/*/EfCompiledQueryCacheTests/*` | 4 |

The existing CP-1 union with `EfLambdaClockGuardTests` remains a 14-test selection.
No selector-roster widening is required to detect this fixture defect.

## Verification and costs

All builds and test drivers used `scripts/build-slot.ps1`, native default build
outputs and `UseAppHost=false`. Runs were serial. Test evidence below binds the
unchanged production base plus the named test commits; the final documentation
commit adds this note only.

| Selection | Source | Passed / failed / skipped | TUnit duration | Slot wait |
|---|---|---:|---:|---:|
| Expiry alone | `4ce55f36794f` | 1 / 0 / 0 | 58.561 s | 0 s |
| Recovery alone | `4ce55f36794f` | 1 / 0 / 0 | 31.860 s | 45 s |
| Both original methods | `4ce55f36794f` | 2 / 0 / 0 | 38.303 s | 0 s |
| Two-fixture red, both methods | `95910d4cb327` | 0 / 2 / 0 | 39.411 s | 45 s |
| Runtime union | `c02546483e92` | 44 / 0 / 0 | 69.209 s | 0 s |
| Required guard union, first run | `c02546483e92` | 113 / 1 / 1 | 236.214 s | 15 s |
| Exact failed guard after driver build | `c02546483e92` | 1 / 0 / 0 | 9.209 s | 0 s |

Runtime union: `EfCompiledQueryCacheTests` 4/4, `EfLambdaClockGuardTests` 10/10,
`ComplexityChainRaceTests` 9/9, and `ReceiptFailureDeliveryTests` 21/21. The two
neighbours check the existing expiry-token races and delivery-failure obligations.

The first guard run's only failure was
`DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`: the
driver DLL had not been built in this fresh checkout. A separately gated build of
`scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`
passed (4.87 s, zero warnings/errors), and the exact failed method passed. The
other 113 guard passes remain credited at the same source. Aggregate guard
coverage is **114 passed, one Windows-only skip**, across all eleven requested
classes. The skipped method is
`WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`.
C1729 emitted `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

The three test-project builds passed with zero errors and the same 2,092 warnings:
277.35 s (31 s admission), 256.38 s (0 s), and 226.55 s (0 s). No execution row
exceeded ten minutes. `/proc/loadavg` samples included `12.10 14.44 18.19` at
start, `50.57 32.24 25.55` during the red build, `19.63 25.92 24.86` around the
red run, `13.24 25.73 26.02` during the green runtime run, and
`8.90 19.83 23.80` during the guard run. These are contended-host samples, not
p95 estimates. Individual TRX durations are retained; unattributed startup,
fixture and teardown remainder is unknown rather than zero. No test timeout or
retry policy was widened.

The complete eleven-class guard filter was:

```text
/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/*
```

The committed derived index matches its builder; it was not regenerated or
changed. Computed policy hash and live-lane input digest match the brief:
`b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d` and
`26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`.
The legacy exclusion pin remains
`6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`;
the native-policy witnesses passed. No pin inputs changed. Mutation checks were
waived by the brief. `EfProductionClockQueryTests` is absent from this base and
was neither edited nor run; Merge 238's files were left alone.

Evidence root: `/work/worktrees/task-5c14b3c5/.antiphon/g103-evidence/`.
It contains build/driver logs, the three baseline TRXs, `warm-red/warm-red.trx`,
`green-runtime/runtime.trx`, `guard-union/guards.trx`,
`derived-retry/derived-retry.trx`, and the original Review/Merge reports.
