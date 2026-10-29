# CARD-1613 fix round 2

Outcome: the three witness defects are fixed and pushed. Linux checkpoint rows CP-1 through CP-8, CP-12, and CP-13 are green, 73/73, on `fa8452462e3f64134ea21cbd2984ea6b4027d645`. Run `20261029-231449-b550`. Windows CP-9, CP-10, and CP-11 stay owed.

The review heading said 30 in-memory fixtures. The listed families are 29 (20 correct red, 3 correct green, 1 control, 5 that had fooled the witness). All 29 were judged correctly. No 30th family was invented.

## Commits

Branch `feat/card-task-24d41ea5`, fast-forward from `1147f708910b07658336dcf6cce64e2a6d6659db`.

| SHA | Subject |
|---|---|
| `eaedaa4ce9edef7678ce08a880ef54869361539f` | test: require inherited-hide and inherited-builder witnesses |
| `9da2c500b7bf4434b5f62e2bc9097a05b9a61a03` | fix: keep inherited tests by MethodInfo and walk base members |
| `f3cc905667a4eb7b954ed44462f8f3751df7422b` | test: require the Windows probe skip reasons |
| `a44ffa6b8b649118dfa76977782404adc8accdf5` | test: require frozen rows to keep their platform and marker |
| `0e310a8ad77f8bb5d1fd0b21d030cd8640b9194c` | fix: witness every frozen row, even if its reason or platform changes |
| `47e476c86568130923f8cad5953a9a4fa8f2b580` | fix: compare Windows probe skip reasons |
| `fa8452462e3f64134ea21cbd2984ea6b4027d645` | fix: keep frozen-row stubs out of the lane class scan |

Files: `tests/Shared/NativeLinuxPolicyWitness.cs`, `tests/Antiphon.Tests/Fixtures/NativeLinuxPolicy/R4-inherited.cs.txt`, `tests/Antiphon.Tests/Fixtures/NativeLinuxPolicy/R12-resolution.cs.txt`, `tests/Antiphon.Tests/Scripts/NativeLinuxPolicyExecutionTests.cs`, `tests/Antiphon.Tests/Scripts/NativeLinuxPolicyWitnessMutationTests.cs`.

No marker was removed, no reason text was changed, and no test was skipped to make a row green.

## What changed

D-1. Inherited tests are keyed by MethodInfo. A non-test `new` hide keeps the base `[Test]`. A real override replaces it. A `new [Test]` hide replaces the base, matching TUnit running one node. `RefuseUnsupportedMembers` walks the base chain when the concrete type has `[InheritsTests]`, including policy-matched types.

D-2. `Problems` takes `OperatingSystem.IsLinux()`. On Windows it compares the eight `Skipped: ` DebugTrace values, Ordinal, to the probe marker reasons (`skip-reason` / `skip-extra`). The Linux parent still expects 9 passed. The same test feeds a Windows-shaped TRX: eight correct skips pass, a wrong reason fails, and one marked test running fails via `windows-counts`. This host cannot run Windows. CP-3 executed that witness.

D-3. Every key in `WitnessedRowKeys` must be `platform: windows` and start with `Linux-only by construction:`. A present frozen key that lost either shape emits `witnessed-row-reshaped` and is still marker-checked. If the policy contains any frozen key, a missing frozen key emits `witnessed-row-missing`. Zero frozen keys (the synthetic strip harness) does not emit missing-key. An empty baseline marker emits `missing-marker`.

Lane scan. The first closed run, `20261029-230006-0c89`, was red only on CP-8: `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration` saw the raw-string line `public class CheckpointBoundedReaderTests` as an untagged class (22 passed, 1 failed). The stubs are now ordinary string literals with the same compiled text. A retry before deleting the previous output directories, `20261029-231304-d5c6`, refused `pre-existing-build-source` and ran no tests.

## Fixture judgments

Scratch harness, not in the repo. Each line is the required verdict and it matched.

Correct red:

- ungated overload: missing-marker `Target(System.String)` only
- case-twin class: missing-marker `Two.Twin`
- case-twin method: missing-marker `Two.twin`
- nested decoy: missing-marker `Decoy.Real`
- outer-marker-only nested: missing-marker `Inner`
- InheritsTests unmarked base test: missing-marker `Inherited()`
- Conditional helper: missing-marker `ConditionalHost`
- look-alike other namespace: missing-marker `SameName()`
- look-alike same full name, other assembly: missing-marker `Ordinary()`
- BaseTestAttribute subclass on the type: unsupported-test-attribute `BoomAttribute`
- base-class marker only: missing-marker `FromBase()`
- different reason versus baseline: reason-mismatch `Run()`
- row added without a marker: missing-marker `Added`
- row deleted while the marker stays: reverse-coverage `Kept`
- empty reason: missing-marker `EmptyHost`
- unmarked generic: missing-marker Host`1
- plain row against a generic: unresolved-row `Host`
- static test: missing-marker `Run()`
- class marker on a method row: reverse-coverage `Out()`
- full-name row in another namespace: missing-marker `Other.Exact`, not `Only.Exact`

Correct green:

- C0 marked class
- base `[Test]` without InheritsTests
- `new [Test]` hide with a method marker only
- `methods:[""]` row

Formerly fooled, now red:

- M4c inherited non-test `new` hide: missing-marker `row=HideDerived`
- M7b inherited DynamicTestBuilder: unsupported-test-attribute `InheritedBuild()`
- M15 changed PhoneHome reason: witnessed-row-reshaped and missing-marker `Join()`
- M15b platform linux on UnixDelegate: witnessed-row-reshaped and missing-marker `Argv()`
- M15c platform all on CheckpointBoundedReader: witnessed-row-reshaped and missing-marker `Read()`

## Linux checkpoints

Import of the plan wrote 13 rows to `.antiphon/c1613-r2-manifest.yaml` (exit 0). The only import warning is CP-9's separate Windows build. The run used `--plan` with `--rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-12,CP-13 --serial --expected-source-sha fa8452462e3f64134ea21cbd2984ea6b4027d645`.

Run `20261029-231449-b550`, exit 0, source clean, buildSource verified, 10 green, 0 red. Evidence: `.antiphon/checkpoints/20261029-231449-b550/report.md`.

| Row | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-1 | 3 | 3 | 0 | 0 |
| CP-2 | 10 | 10 | 0 | 0 |
| CP-3 | 2 | 2 | 0 | 0 |
| CP-4 | 4 | 4 | 0 | 0 |
| CP-5 | 4 | 4 | 0 | 0 |
| CP-6 | 4 | 4 | 0 | 0 |
| CP-7 | 17 | 17 | 0 | 0 |
| CP-8 | 23 | 23 | 0 | 0 |
| CP-12 | 3 | 3 | 0 | 0 |
| CP-13 | 3 | 3 | 0 | 0 |

Parent total 73. CP-9, CP-10, and CP-11 were not run. Native task fa7879ff was already running them on the previous tip.

`scripts/check-evidence-diff.ps1` over `1147f708910b07658336dcf6cce64e2a6d6659db..fa8452462e3f64134ea21cbd2984ea6b4027d645`: 7 commits, 0 violations, exit 0.

`git merge-tree --write-tree origin/master HEAD` conflicts. `origin/master` is `0e7dd390520a43ab330396a72e9085a8691ac332`. The conflict set is the same at the start SHA `1147f708910b07658336dcf6cce64e2a6d6659db`. This round added none. Conflicting paths: `.antiphon/task-af2b78c8.md`, `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`, `docs/release-gates.md`, `scripts/test-rc-platform.ps1`, `server/Application/Services/CapacityRecoveryService.cs`, `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`, `tests/Antiphon.Tests/Application/UnixDelegateLaunchArgvTests.cs`, `tests/Antiphon.Tests/Checkpoints/AnchoredPathDeleteWitnessTests.cs`, `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs`, `tests/Antiphon.Tests/Infrastructure/FileUseCaptureTests.cs`, `tests/Antiphon.Tests/Scripts/ReleaseGatePlatformTests.cs`, `tests/Antiphon.Tests/Scripts/RetiredTempContainerScriptTests.cs`, `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`, `tests/test-execution-policy.json`. Not merged.
