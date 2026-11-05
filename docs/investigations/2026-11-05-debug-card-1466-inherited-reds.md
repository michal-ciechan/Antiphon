# CARD-1466: inherited census reds on current master

The requested **19 tests pass on master 1527c29fef2737bf3eaa49f2293d82d85402265a**:
one registry check, sixteen shape cases, the roster check and the C1382 method.
The first three historical failures already have landed test repairs. C1382 still
contains an avoidable real-clock scheduling race in its fake, although its exact
method passed under substantial host load. The remaining Code work is narrow
fixture hardening, without changing production behavior or increasing a timeout.

## Source, scope and evidence boundary

- Debug task `1f10d17d`, branch `feat/card-task-1f10d17d`, server2 Linux worktree.
- Assigned base and master fetched at investigation start:
  `1527c29fef2737bf3eaa49f2293d82d85402265a`. Every test used this unchanged source.
- Original red census base named by the card:
  `294fa3d1a81ff97abb285af3180d93ee4d4b25f6`.
- The card and its history were read through `scripts/card.ps1`. History contained
  the dispatch move, rather than the original census receipts. Historical
  attribution below uses the card, touching-file history and the retained CARD-1572
  investigations; it is source attribution, **not a newly executed historical bisect**.
- No product or test files were written. No Windows desktop command, rc command,
  publication, activation, land-graph update or policy modification was performed.
- Host: Debian 12, 24 logical processors, SDK 10.0.403; native test host .NET 9.0.22,
  TUnit 1.44.0 / MTP 2.2.2. Load varied substantially, including approximately 48
  during the build and 43–56 around the C1382 execution.
- Tested `Antiphon.Tests.dll` SHA-256, checked again after the first two rows:
  `29f7c62b458e99a3640dc29fd1da4ff65b43a0e8ec598f7c1c827ccbf1ae7e80`.

Owners read: testing/build operations, test performance standards, orchestration
(including D-2), and the card HTTP/history interface. The Debug brief's exact
selection replaces a full-suite sweep. No full-suite or Windows rc green is claimed.

## Exact execution and cost

One native default-output build was used, with `UseAppHost=false`; initial output
inventory was empty. No separate cold checkpoint build was necessary. The build
passed with **0 errors, 1,728 warnings**, MSBuild elapsed **324.78 seconds**, admission
**0 seconds**, lease held **326 seconds**.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1466-native-incremental -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -p:UseAppHost=false -v:minimal
```

Each of the three test rows had its own lease and reused that build with
`dotnet run --no-build --project tests/Antiphon.Tests -p:UseAppHost=false --`.
Each wrote a fresh TRX with `--report-trx`, `--report-trx-filename` and
`--results-directory`. These were the only TUnit runs; no passing selection was repeated.

| Row | Exact filter | Passed / failed / skipped | Admission s | Lease held s | Native test summary s |
|---|---|---:|---:|---:|---:|
| Registry | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | 1 / 0 / 0 | 270 | 8 | 5.499 |
| Shapes | `/*/*/CheckpointShapeConflictTests/shape_*` | 16 / 0 / 0 | 360 | 386 | 380.397 |
| Roster + C1382 | `/*/Antiphon.Tests.Application/(DispatchBaseWarningRosterTests*)\|(AgentSessionCancelledTurnTests*)/(C1155_RosterNamesEveryDispatchWarningE2EMethod*)\|(C1382_Rearm_non_end_turn_stop_returns_to_awaiting_input*)` | 2 / 0 / 0 | 316 | 59 | 47.992 |

The OR filter uses the documented pinned-runner form and the fresh TRX contains
exactly the two intended methods. The third leased PowerShell driver ran one
`--no-build` TUnit invocation, then an additional latency measurement. Its test
child exited **0**, but its first measurement failed during assembly initialization,
so the **driver exited 1**. This is retained as diagnostic setup failure, not hidden
or counted as a test failure. The warning about no maxcpucount injection into
PowerShell did not hide a build: that driver contained no build.

Essential native summaries, in row order:

```text
total: 1   failed: 0   succeeded: 1   skipped: 0   duration: 5s 499ms
total: 16  failed: 0   succeeded: 16  skipped: 0   duration: 6m 20s 397ms
total: 2   failed: 0   succeeded: 2   skipped: 0   duration: 47s 992ms
```

### Per-test durations

All rows below have TRX outcome `Passed`. Shape methods belong to
`Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests`; other class names are explicit.

| Method | TRX seconds |
|---|---:|
| TestClassificationGuardTests.Registry_matches_compiled_metadata | 0.9566393 |
| shape_directory_on_file | 23.0221096 |
| shape_file_on_directory | 22.1524216 |
| shape_symlink_on_file | 37.6053066 |
| shape_untracked_descendant | 26.4021106 |
| shape_ignored_descendant | 37.6181367 |
| shape_staged_untracked | 28.2368185 |
| shape_modified_tracked | 18.9177710 |
| shape_deleted_tracked | 18.6890038 |
| shape_clean_build | 27.2403261 |
| shape_regular_user_move | 27.9099882 |
| shape_deleted_untracked | 32.6886758 |
| shape_changed_untracked | 30.1245161 |
| shape_existing_stamp | 8.2916461 |
| shape_compiler_collision | 7.6739079 |
| shape_existing_output_directory | 14.3273931 |
| shape_existing_output_symlink | 6.5494455 |
| DispatchBaseWarningRosterTests.C1155_RosterNamesEveryDispatchWarningE2EMethod | 0.6466951 |
| AgentSessionCancelledTurnTests.C1382_Rearm_non_end_turn_stop_returns_to_awaiting_input | 28.1200338 |

Shape TRX duration sum is **367.4495772 seconds**, maximum **37.6181367 seconds**.
The phase recorder's slightly different envelope totals 367.863 seconds: exclusive
process 267.223, fixture 0.625, filesystem 0.007, remainder 100.008. These are
recorded categories, not a claim that missing DB/network/host instrumentation cost zero.
Startup, per-child build subphases and uninstrumented external-call portions remain
unknown within the host overhead/remainder.

C1382's phase envelope is **28.140 seconds**, including recorded DB **0.748 seconds**
and remainder **27.392 seconds**. Do not label that remainder EF initialization or
thread-pool delay without additional spans. Its whole-method time is not the duration
of any one first-output wait.

Raw driver logs and traces are retained at `/tmp/card1466-*.log`,
`/tmp/card1466-*-trace.jsonl`, `/tmp/card1466-phases/`, and
`/tmp/card1466-last-row-load.jsonl`. Phase metadata binds run `card1466-master`,
suite `antiphon`, attempt `1`, and groups `registry`, `shapes`, `roster-race`.
Result directories are removed after extraction as required by the brief; the
committed report retains their counts, identities, durations and build identity.

## Findings and smallest fixes

### 1. Snapshot class registry: repaired; no new fix

The actual failing method was
`Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata`.
`TestSchemaSnapshotTests` does not itself define a method with that name; it was
the Slow class mentioned by the guard's error. Running a filter combining that
class with the registry method would select zero tests.

The snapshot class was introduced by
`9533c20912ea17a85818f4958f1f66f4869c147e`, with Slow metadata but no allowlist entry.
This was a real **test metadata/registry defect**, not a database parity defect.
`816b1b03159969b77a1d0887ddeebccabb0460b0` already adds the class, with an owned-
container/migration/dump/restore reason, to
`tests/Antiphon.Tests/slow-tests-allowlist.txt`.

Current result: **1 passed**. Smallest current fix: **none**; retain that entry and
the compiled metadata guard. Actual snapshot parity tests were not part of this
commission and were not run. The old repair's blast radius is test classification;
the allowlist is not `tests/test-execution-policy.json`, so no nightly policyHash
or live-lane input-pin refresh is required for this repair.

### 2. Shape negative qualification: repaired stale expectation

`3a7ed451668051b3627b8802ed9005e7bf3db327` made the report validator reject a failed
report as `report_failed` before reaching report source eligibility. The old shape
fixture expected `report_source_ineligible`, producing the historical fifteen
`receipt-invalid: negative qualification mismatch` failures. The CARD-1572
[continuation investigation](2026-10-23-card-1572-unit-continuation.md) records the
pre-fix red and this cause.

`ffeb5aa4fad7dbcc4fe145cc9a490fdc408a8186` already repairs `ReadReceipt`: independently
require `IsSourceEligible(...) == false`, then require exact refusal `report_failed`.
This preserves both the tamper refusal and the validator's precedence; it does not
accept an arbitrary failure or remove a preservation assertion.

`61dc58c26f2e35e4b69feb18a7d2c3e043739743` subsequently split the old
`shape_conflict_rule` argument method into sixteen named `shape_*` methods. The
executed replacement filter covers all sixteen, rather than falsely crediting the
now-absent old name. Current result: **16 passed**, no negative-qualification mismatch.

Smallest current fix: **none**. Retain the exact dual assertions. No validator,
product, timeout, test-execution-policy or pin change is justified. Blast radius of
the existing repair is this fixture's receipt expectations. These methods have no
deliberate Windows skip gate; native Windows fixture qualification is separate
from this Linux execution result.

### 3. C1155 roster: repaired stale plan

`208277fcea8e684f3e6faff465487a50e302c959` added
`C540_PreEnqueueWaitSurvivesSlowMaterialization`, leaving the roster plan short of
the thirteenth E2E method. This was a **test plan/roster expectation defect**.
`1f5c07686fc89a2f084ffba15ec27539eea12012` already restores CP-14, updates the plan
budget from 54 to 58 minutes, and aligns the roster commentary. The historical
[CARD-1572 investigation](2026-10-23-card-1572-unit-lane-debug.md) records the red
and all eleven roster guards passing after repair.

Current exact-method result: **1 passed**. Smallest current fix: **none**. Keep the
Roslyn-derived equality assertion. The portable roster guard needs no desktop;
executing its real delivery E2E obligations still requires their Windows lane.
Blast radius is the plan and its roster guard. No execution-policy/hash/pin change.

### 4. C1382: current pass, scheduling race remains in the fixture

The exact non-end-turn method was introduced by
`6e631f0a4ae06e1f49cc5f2137227a71844d4242`. `EchoWorld`'s one-second setting appears
with `031a377f98f1622a4febf5359d080c4c4baf2cfc`; the shared fake's asynchronous
`Task.Run` output path predates it (`796457cf805665f8f9498cca0019ad31056bca2e`).
This identifies the source interval of the risk, not a newly proven first failing commit.

The fixture still sets `FirstDeltaTimeoutMs = 1_000`.
`FakeAgentProtocolAdapter.SendPromptAsync` schedules `Task.Run`, then a default
10 ms `Task.Delay`, then emits output and completes its signal. Its
`WaitForFirstPromptOutputAsync` races that signal against a **real** `Task.Delay`.
`EchoWorld.Clock` does not control that deadline. Thread-pool scheduling can consume
the budget even though the test's subject is return to AwaitingInput for five
non-end-turn reasons, rather than output latency.

Current exact method: **passed**, body **28.1200338 seconds**, one invocation
containing all five stop reasons. Load was `43.44 / 38.41 / 34.66` before the final
host and `55.91 / 42.87 / 36.41` afterward, on 24 processors. This is a successful
loaded isolated execution, not evidence that the original full-suite race is gone.
The method duration cannot justify widening the separate one-second wait.

**Additional first-output measurement:** 100/100 signals received, zero samples
at or above 1,000 ms; minimum **10.2031 ms**, median **12.4084 ms**, nearest-rank p95
**12.5987 ms**, maximum **20.7603 ms**. The stopwatch includes reflective SendPrompt
and Wait calls on the existing fake, with its unchanged 10 ms delay and 1,000 ms
wait. Twice this observed maximum is 41.5206 ms; this sample supplies no basis for
increasing the existing 1,000 ms wait.

This is a provisional one-process micro-measurement, not a full-suite first-delta
p95. It used PowerShell's .NET **9.0.10**, rather than the native test host's 9.0.22,
at `23:41:53.856Z–23:41:55.173Z`. Load changed from `13.75 / 29.53 / 32.75` to
`13.13 / 29.14 / 32.61`; instantaneous contention had eased after the successful
loaded test. It cannot disprove a busy test process's thread-pool scheduling race.
The current test recorder does not separately time the five first-output waits;
their exact individual durations remain unknown.

The initial reflection probe collected zero samples because its module initializer
could not load in PowerShell's default dependency context. Its failed driver is
retained above. The successful measurement-only rerun used an isolated load context
for the same SHA-256-verified assembly and its dependencies, including DiagnosticSource
10, without a rebuild or repeated TUnit selection. It had its own lease: admission
**150 seconds**, held **2 seconds**, exit **0**. Raw samples and slot lines remain in
`/tmp/card1466-fake-latency-second.log`; the original setup error remains in
`/tmp/card1466-fake-latency.err` and the original driver log.

Smallest recommended Code repair: add a default-off synchronous-output option to
the shared fake, then enable it only for adapters created by `EchoWorld.StartCancelledAsync`.
When selected and `PromptOutput` is nonempty, perform the same `Emit` and signal
completion before `SendPromptAsync` returns. Preserve prompt recording, injected
prompt failures and callbacks. Empty output must still leave the signal incomplete;
do not replace the wait with unconditional success. The ordinary default asynchronous
path and its delay must retain their existing behavior for other consumers.

Keep every phase/status/completion assertion, the one-second setting, production
timeouts and stall semantics unchanged. Add a deterministic compiling red witness
for the opt-in behavior with asynchronous output withheld, plus an empty-output
control; validate the affected cancellation fixture after the fix. The blast radius
is an explicit test-fake mode and its EchoWorld consumers, with no production change.
No test-execution-policy or digest refresh is needed.

## Relationship to the failed Windows rc and other Code tasks

The inspected prior Debug report is
`origin/feat/card-task-d8f70e4c:docs/investigations/2026-11-05-debug-rc-coverage-incomplete.md`
at `1d15909de96d07fb10fa66b0381131226c56a40c`. It binds run `b19a9880` to failed
candidate `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a` and explicitly says that the
58 result identities, failed assertions and exit-to-chunk mapping were unavailable.
It does not contain a list that can be joined to these four issues.

The failed candidate already contains the first three repairs. The candidate and
tested master have identical shape fixture, roster test/plan, classification guard,
C1382 fixture and shared fake files. The slow allowlist differs only by later entries
for other classes; the snapshot entry is present in both. Therefore the old snapshot,
negative-qualification and missing-CP-14 causes must not simply be counted again as
b19a9880 failures. This source comparison is not a substitute for that run's actual TRX.

| Issue | Historical b19a9880 failed result or one of the 58 unexecuted results? |
|---|---|
| Snapshot registry cause | No matching historical result supplied; its repair is already present in the candidate. The guard could fail for another class, which would be a different cause. |
| Shape negative qualification | No matching historical result supplied; repaired expectation is already present. A later fixture/capability failure would require its actual message. |
| C1155 roster | No matching historical result supplied; CP-14 repair is already present. |
| C1382 | Possible recurring load race because its fixture is unchanged; membership in either historical set is **unknown**, not inferred from a timeout count. |

None of these exact selections declares a Windows-only exclusion that justifies
ignoring an unexecuted required result. Exact historical overlap needs a read-only
join of that run's TRX identities/reasons and diagnostics, using the extraction
already specified by Debug d8f70e4c. The supplied native path is:
`C:\Antiphon\releases\candidates\rc-2026-11-06-5a95ae7a5611\logs\2026-11-04-1808-b19a9880\summary.json`.
No desktop access was attempted. Keep this unresolved join with the existing
coverage owner **Code 07ab1e49**; do not duplicate its admission/coverage repair or
weaken required-UID accounting.

**Code d69fe301 is separate.** Its inspected tip is
`5cb4c4abd09af6dff064e9d15b6b9c3dd81bff6f`; its
`docs/superpowers/plans/2026-11-05-d69fe301-runtime-warmup-repair.md` and actual
`TestDbFixture.cs` diff initialize the real EF provider/model once in the selected-
store assembly hook, and adjust bounded admission for workspace/retention packs.
It does not edit this fake or the C1382 fixture. Retain that 77-timeout repair;
do not copy it into this task or treat uninstrumented time here as its proof.

**Debug 5ae98d31** at `c318d8edfc97e046bb9a332ec34c76973f9a4f4a` measured four build
slots on 24 cores, cold builds 222–817 seconds and admission up to 405 seconds.
This investigation's native build (325 seconds) and row waits (270/360/316 seconds)
fit that host behavior. Admission is structural host capacity, not a reason to
relax an assertion or extend C1382's first-output deadline.

Including the separate corrected measurement, admission totals **1,096 seconds**;
lease-held wall time totals **781 seconds** (build 326, tests/first probe 8+386+59,
corrected probe 2). Their charged sum, approximately **31m17s**, crosses R1's
30-minute selection threshold. This Debug records its attribution: **18m16s**
admission and **13m01s** leased wall time. No individual row approached 30 minutes.
The existing 5ae98d31 capacity diagnosis explains the admission cost; no timeout
change or repetition is proposed. Final fetch still resolved master to the exact
tested `1527c29fef2737bf3eaa49f2293d82d85402265a`.

## Policy, cleanup and handoff

All four smallest dispositions above leave `tests/test-execution-policy.json`
unchanged. No new exclusion is recommended. Consequently neither its nightly
`policyHash` nor `scripts/test-live-lane-run.ps1`'s pinned input digest needs refresh
for this work. If an independent coverage repair changes policy, those two must be
refreshed together on the final combined tree by its existing owner.

**Cleanup complete.** Every owned build, test and measurement driver exited before
cleanup. All inventoried native bin/obj directories and the three fresh test-result
directories were removed using individually typed literal absolute paths, without
force, glob/variable removal or `git clean`. The final worktree output-directory
inventory is empty, and all three result roots are absent. Logs/phase traces remain
outside the worktree at the paths above. No tracked source or foreign output was
removed. Only this Markdown report is committed.

--- next stage ---
next: code
handoff: Retain the three already-landed registry/shape/roster repairs. Harden only the C1382 EchoWorld fake first-output scheduling: default-off synchronous emission in FakeAgentProtocolAdapter, enabled only in EchoWorld; retain actual emitted-output signaling, empty-output negative behavior, all existing state assertions and the one-second setting. Add a deterministic compiling red witness for the option and validate the affected cancellation fixture. No product, policy or timeout changes. Keep the historical b19a9880 result join with existing Code 07ab1e49 and the 77-timeout provider/admission repair with Code d69fe301.
artifact: docs/investigations/2026-11-05-debug-card-1466-inherited-reds.md
