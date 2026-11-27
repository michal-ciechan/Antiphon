# Why the rc cut missed the stale migration selector guard

## Introducing commit

CARD-1789 introduced the source-text guard in `227214826f31a1d6b23222ce505e1224c2dc9396`. The group 90 repair `fc626a13342bec63c13684cb763d487fbb31b18f` later added `nameof(IMigrator.Migrate)` while looking up migration API metadata. The guard's unanchored source regex credits that reference as a call site and accepts the stale token in its rejection witness. Windows run `419ed669`, group 98, reported the failure on rc pin `5c0883bf203d122fdc7d4556e79087af2a683669`. The original Windows TRX was unavailable on the repair host. The witness was already present at the earlier run `1fe763a9` pin `0cfc4ac1ffb41f5af159a0c1db423fe119950267`. The `nameof` reference arrived after that pin.

The same defect can hide a stale token in the real owner paragraph. The `.Down(` entry already did so. Outside the witness, its only source-text match was an assertion message in `ImpactedClassesSelectorTests`. Direct `.Down(` remains a prospective selector in the migrations row.

## Approving reviews and selections

CARD-1789 Review `3d6a3f6f` was Final/Full. It ran all eight `AdditiveMigrationRuleWitnessTests` at `bcdf6cbbb83451285ad643e921af0125d93d8735`, plus the other affected classes. At that source, the stale `IMigrator.Migrate` token had no occurrence outside the witness. The Review listed the `.Down(` false match as advisory A-1. The review report is at `.antiphon/task-3d6a3f6f.md` in commit `5059c130bd0e55620e69e535d407ff107029734c`.

The later group 90 change landed from Debug task `88067d23-b5fa-481c-b86c-351c2193c414`. Its task record carries landed publication and no Review evidence. Its verification ran an exact historical-schema sweep and the revised PostgreSQL catalog witness. The rc cut also ran the catalog witness as CP-7 and the repository guards as CP-1. [Group 90 verification](2026-11-26-why-missed-rc-run-1fe763a9-group-90.md) · [RC cut selections](2026-11-26-rc-cut-gates-pass3-bdb2790af.md)

## Selection gap

`AdditiveMigrationRuleWitnessTests.owner_selector_tokens_each_name_a_real_test_call_site` is a source-only Unit method in `tests/Antiphon.Tests/Application`. The group 90 selections covered historical schema writes and catalog additions. The cut's class union omitted the owner-selector class. A test-only source change therefore altered the input of this checkout-wide scan outside either selection.

## Cheapest pre-land check

For the impacted-classes table, map `tests/Antiphon.Tests/Application/HistoricalMigrationSeedCoverageTests*.cs`, `tests/Antiphon.Tests/Application/AdditiveMigrationRuleWitnessTests.cs` and `docs/testing-and-build.md` to `/*/*/AdditiveMigrationRuleWitnessTests/owner_selector_tokens_each_name_a_real_test_call_site`. The method needs no PostgreSQL fixture and detects the original false match in a single execution.

## Exemption

None.
