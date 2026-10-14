# CARD-1266 MatrixDefaultComplexitySettingsTests lane classification

## Problem

`TestClassificationGuardTests.Registry_matches_compiled_metadata` (and the
narrower `TestClassificationPolicyTests.C487_G068`) fail on master with exactly
`lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests`.
The guard reads categories at class level only
(`TestClassificationMetadata.Read` -> `GetCategories(type)`) and requires every
`Antiphon.Tests` class to carry exactly one of `Unit` / `Integration`.

CARD-0097 S-0 (`f1dc0a2f5`, `e5779e40a`) wrote the class with no class-level
category and per-method categories: three pure option/validator methods marked
`Unit` and one database-backed method,
`The_default_setting_changes_routing_only_when_a_cell_resolves`, marked
`Integration` (it calls `TestDbFixture.CreateIsolatedSchemaAsync`). The class has
neither lane, so it fails the XOR. The guard is right; the fixture is wrong.

## Change

- S1 (fixture only, no production change): split the class by lane.
  - `MatrixDefaultComplexitySettingsTests` keeps the three option/validator
    methods under a class-level `[Category("Unit")]`; the per-method `Unit`
    attributes go.
  - The database-backed method moves unchanged (body, arguments, assertions) to
    a new `[Category("Integration")]` class
    `MatrixDefaultComplexityRoutingTests` in its own file. It reuses the settings
    class's `Bind` helper, now `internal`.
- No guard, assertion, argument row or timeout changes. No allowlist entry (the
  class is not `Slow`).

## Verification design

- V-1 `TestClassificationGuardTests.Registry_matches_compiled_metadata` red at
  the base with exactly the `lane-xor` line for the class; green after S1.
- V-2 `TestClassificationPolicyTests.C487_G068` red at the base (its error text
  contains `lane-xor`); green after S1.
- V-3 `MatrixDefaultComplexitySettingsTests` (U): 11 cases (3 + 5 + 3 argument
  rows) all pass after S1.
- V-4 `MatrixDefaultComplexityRoutingTests` (I): the moved method's 6 argument
  rows all pass after S1.
- R-1 No other `lane-xor`, `unknown`, `unregistered-marked` or
  `method-level-slow` line appears (V-1 asserts the whole error list is empty).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S0 | `tests/Antiphon.Tests -> bin-c1266-red/` | guard-red | `/*/*/(TestClassificationGuardTests*)\|(TestClassificationPolicyTests*)/(Registry_matches_compiled_metadata*)\|(C487_G068*)` | V-1, V-2 red | 2 executed, 2 failed (`lane-xor` for the class), 0 skipped; expected red at the base | 2 | 8 | false |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1266-green/` | guard-green | `/*/*/(TestClassificationGuardTests*)\|(TestClassificationPolicyTests*)/(Registry_matches_compiled_metadata*)\|(C487_G068*)` | V-1, V-2, R-1 | 2 passed, 0 failed/skipped | 2 | 8 | false |
| CP-3 | S1 | CP-2 | matrix-classes | `/*/*/(MatrixDefaultComplexitySettingsTests*)\|(MatrixDefaultComplexityRoutingTests*)/*` | V-3, V-4 | 17 passed (11 Unit + 6 Integration), 0 failed/skipped | 17 | 4 | false |

S0 is the committed plan with no source change, so CP-1 runs the base fixture.
Run CP-1 alone at S0, then CP-2 and CP-3 together at S1. Use the checkpoint tool
with `--expected-source-sha <committed HEAD>`, `--total-timeout 150m`, without
`--keep-outputs`; import this table and set `timeouts.rowMinutes: 40` in the
ignored generated manifest. Linux builds use `UseAppHost=false`. Bootstrap the
checkpoint tool once through `scripts/build-slot.ps1` into `bin-c1266-tool/`;
that is the sole planned non-table build. CP-1 is red by design, so its receipt
validators are expected to refuse it (`cleanup_refused`); the green group is the
qualifying receipt.

### Cost

Two Antiphon.Tests builds (~4-8 minutes each on server2) plus 19 test cases,
one requiring the shared Postgres fixture. The whole Unit lane is not run (the
brief excludes it); it is deferred, not passed. The change touches only two test
files, and the classification guard reads every class, so CP-2 is the
assembly-wide classification check.

### Positive controls (pending SourceLanding Mutation)

| ID | Method | Deliberate defect | Expected failure |
|---|---|---|---|
| PC-1 | TestClassificationGuardTests.Registry_matches_compiled_metadata | Remove class-level `[Category("Unit")]` from `MatrixDefaultComplexitySettingsTests` | `lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests` |
| PC-2 | TestClassificationGuardTests.Registry_matches_compiled_metadata | Add `[Category("Unit")]` beside `Integration` on `MatrixDefaultComplexityRoutingTests` | `lane-xor Antiphon.Tests.Application.MatrixDefaultComplexityRoutingTests` |
| PC-3 | TestClassificationPolicyTests.C487_G068 | Same defect as PC-1 | error text contains `lane-xor` |

Mutation owns all deliberate mutant/red/restore/green cycles. Code runs none.
The unbound candidate is
`docs/superpowers/mutations/2026-10-14-card-1266-matrix-settings-classification-candidate.md`.
