# Why missed: g83 GrokRulesReadyOrdering 1 s real-clock rules deadline

One committed note for rc chunk `antiphon-all-group-83` in run `34d41b7e`
(`rc/2026-11-20` = `b559bfba2e9a420495122d27171352fe9b7e1ba4`, native Windows desktop). CARD-1781.
The Windows extract is
`docs/investigations/2026-11-14-run-34d41b7e-g83-grokrulesreadyordering-windows-failure.md`
(read task `cbedf221`, carried here byte-for-byte from `966c684fc2c42bc9aabbc46a774cf77b087b926d`).

## Failure

TEST FIXTURE/TIMING DEFECT. Not a product regression, not Windows-specific code (paths, line
endings and timer resolution play no part), and not the g54/g58 stale-contract pattern.

`GrokRulesReadyOrderingTests.Ambiguous_input_preserves_launch_and_waits_for_transcript(timeout)`
failed in 15.013 s at line 85 (`written` null) with launch error
`ConflictException: grok_rules_initialization_failed: timeout` from
`GrokRulesRefreshService.InitializeAsync` line 176. The test set
`GrokRulesSettings { InitializationTimeoutSeconds = 1 }`. The rules service runs on the real clock
(`BridgeQueueHarness` default `TimeProvider.System`; the test's `FakeTimeProvider` drives only the
phone-home transport). `InitializeAsync` stamps `RulesDeadlineAt = now + 1 s` (line 158) and then
does a transcript catch-up and a `ReconcileAsync` before the first queue flush charges the
delivery attempt. While `DeliveryAttempts == 0`, `Judge` fails an expired row with `timeout`
(line 389). The test was the first of its class, in the cold first wave of a 263-test chunk with
about 50 heavy integration tests starting in the same millisecond. That pre-flush window took
over 1 s, so the launch failed before the Input was written. The fault injection (`timeout`
argument) had not been reached. The `cancellation` and `disconnect` variants, identical up to
that point, passed minutes later in the warm process (6.8 s and 2.6 s).

The product behaviour is correct: an undelivered rules prompt past its deadline is a failed
startup. Production uses 480 s.

Deterministic reproduction (local, OS-independent seam): `h.Runner.TranscriptPull` delayed by
1.2 s, i.e. a slow pre-flush catch-up. With `InitializationTimeoutSeconds = 1`: 3 of 3 variants
failed with the exact Windows signature (`grok_rules_initialization_failed: timeout`, `written`
null). With 30: 3 of 3 passed under the same delay. The seam was a temporary uncommitted edit.

Fix (`b515cd4239f5eb47ec1b86dd841cb95539e89eef`): `InitializationTimeoutSeconds = 30`, twice the
test's own 15 s launch budget, so the rules deadline cannot fire inside the launch window. No
assertion changed. The "time alone cannot fail an uncertain attempt" contract is still forced
explicitly later in the test (`RulesDeadlineAt = UtcNow - 1 s`, then `ReconcileAsync`), so
mutation controls PC-2 and PC-3 of
`docs/superpowers/mutations/2026-11-13-card-1892-input-uncertainty-candidate.md` keep their bite.
No other test sets a short `InitializationTimeoutSeconds`.

## Introducing commit/card

introducing commit/card: edaa607c86054fdf8c34a73136282ef7245fde84 (CARD-1892). It added the test
with the 1 s budget. `47d86c514a1bdad73dbf84aeaeb3bd460e7aa7ac` amended its receipt fixture. The
product change it covers, `5b80144d3cc7dcdcdb50c85ee03cd737e1508b41`, does not touch the pre-flush
window. The deadline (line 158) and the `timeout` fail (line 389) predate this cut. rc/2026-11-20
is the first rc containing the test (absent from `rc/2026-11-19`), so g83 was its first native
Windows execution.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- d552541f (round 1, subject Code `13c4448d`, Grok on server2 Linux), scope Full, checkpoint
  `20261113-180544-d72a` at `01edf03f69ddfb6e5fe43de40d14db9e2ed382fe`: CP-4
  `/*/*/GrokRulesReadyOrderingTests/*` 5/5, plus CP-1 157, CP-2 95, CP-5 79, CP-6 5. "Native
  Windows execution was not run and stays owed."
- caeec914 (round 2, subject repair `df03fdd9`, Grok on server2 Linux), scope Full, receipts at
  `312a3961d2e2e00bd877571cd7bc2bcb0342c842`: CP-4 5/5 in serial rerun `20261113-205745-29d8`
  (its first concurrent run had CP-4 0/5 from overlapping Testcontainers startup cancels), CP-1
  157, CP-2 103, CP-3 106, CP-5 79, CP-6 5. Native Windows pending.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane,
`Integration`, `tests/Antiphon.Tests/Application/GrokRulesReadyOrderingTests.cs`. The test was
inside both selections and passed. What the selections lacked was load: CP-4 ran the class alone
(and `[NotInParallel("MessageQueue")]`) on a warm Linux host, so the pre-flush window was always
well under 1 s. The defect needs a cold process and a concurrent heavy first wave, which only the
rc chunk packing produced. Native Windows proof was explicitly left owed by both reviews. No
review applied the test-performance rule (budget about 2x measured p95) to an in-test
real-clock product deadline.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`{server/Application/Services/GrokRulesRefreshService.cs,tests/Antiphon.Tests/Application/GrokRulesReadyOrderingTests.cs}` -> `/*/*/GrokRulesReadyOrderingTests/*`

This row would have selected the class, which the reviews already ran, so a selection row alone
does not catch a load-only timing defect. The cheaper catch is a review check: any real-clock
product deadline configured by a test below twice the measured p95 of the window it guards is a
blocker. `tests/impacted-classes.json` is not changed here.

## Exemption

same cause as an already written note: none.

## Verification (native Windows desktop, Debug 35eb006c)

At `b515cd4239f5eb47ec1b86dd841cb95539e89eef`, native default output, through the build slot:
`/*/*/GrokRulesReadyOrderingTests/*` 5/5. Guard union (`TestClassificationPolicy`,
`TestClassificationGuard`, `SlowTestRegistryMerge`, `SlowTestTripwire`, `NativeLinuxPolicyWitness`,
`MechanicalPcContract`, `TaskPlatformGuidance`, `C1729LayeringGuard`, `TestLaneCategoryGuard`,
`DerivedImpactIndex`, `ProcessSpawnLimit`) 164/164 after building the derived-index driver (the
first run was 163/164 with `regenerate_script_reports_changed_then_unchanged` failing only because
`Antiphon.DerivedImpactIndex.dll` was not built; `DerivedImpactIndexTests` then 27/27, index
unchanged). `C1584_E2EExpandedBudget` 1/1.

## Native Windows proof owed

These runs were class-scoped, not under chunk load. On the next rc check that the chunk that
packs `GrokRulesReadyOrderingTests` passes.
