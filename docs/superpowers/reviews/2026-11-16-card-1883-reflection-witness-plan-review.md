# CARD-1883 plan review, round 1

CLEAN. No blocker. Advisories below do not hold the land.

Reviewed source `854a1f32a12f6403970cba7e3e7112e34392a728` on `feat/card-task-ec0bad49`, plan owner `ec0bad49-99f9-4cbe-976e-587c2161e4e5` (Codex). Base and `origin/master` are `48a4615dcb4d67033886a3e31629dc7351ed0852`. The diff is only `docs/superpowers/plans/2026-11-16-card-1883-reflection-witness-plan.md`. Reviewer is Grok. Mutation stays waived (operator 2026-11-12). Positive controls stay pending.

## Completeness

The four failures recorded by review `4a404450` (the report CARD-1883 cites) each have their own method and checkpoint:

| Recorded form | Witness |
|---|---|
| Typed local `Func<string,Assembly>` bound to `Assembly.Load`, then `f(name)` | V-1 `DelegateBoundAssemblyLoadLocalFailsClosed`, CP-1 |
| The same delegate stored in a field | V-2 `DelegateBoundAssemblyLoadFieldFailsClosed`, CP-2 |
| `var f = (Func<byte[],Assembly>)Assembly.Load`, then `f(bytes)` | V-3 `DelegateBoundAssemblyLoadCastVarFailsClosed`, CP-3 |
| `using static System.Activator` with bare `CreateInstance<T>()` | V-4 `BareStaticUsingGenericActivatorFailsClosed`, CP-4 |

Each oracle requires `reflection-target-unresolved:tests/Consumer/Reflection.cs`, `Complete == false`, `Fallback == "Full"`, and both `project-evaluation-unavailable` and `assembly-metadata-unavailable`. Capture-without-invocation stays inside the same method (D-3), so it is not a fifth executed test.

## Fail-closed

D-4 and the witness oracles keep incomplete/Full and the exact caller-path reason. A global fence alone cannot pass. Doubtful binding stays unresolved. The plan has no `Skip` and no conditional skip. Green expects 0 failed and 0 skipped. Ordinary delegate, nonreflective field, and parameterless `GetType()` controls must not gain the reflection-path reason. Direct invocations keep their existing handling.

## Closed checkpoints

The imported manifest has eight rows, one build `tests/Antiphon.Tests -> bin-c1883-witness/`, and one exact filter per row. CP-2..CP-8 reuse CP-1. Filters and expect tokens match the named methods and classes. CP-1..CP-4 name methods the current classifier cannot satisfy: `Build` visits only `InvocationExpressionSyntax`, `TryReflectionCall` returns false for `GenericNameSyntax`, and `ReflectionBindings.Read` skips `var` and stores no fields. Those four can go red. A zero-discovery or build failure is defined as not red.

Mins against the roster at this master: CP-5 17 (13 existing plus 4 new), CP-6 19 (relation 4, runtime 4, envelope 11), CP-7 27, CP-8 79. The leased run below executed 13, 19, 27, and 81 in those classes. Floor 79 remains reachable.

p95 is UNMEASURED, n=0, and labelled provisional, with a collection plan on the first Code run, the independent Review, and up to three ordinary samples, then about 2x measured p95 (CARD-1404 R5, CARD-1883). `--row-timeout 10m` is that named exception. Green estimate sum is 16 minutes; derived total is `max(30, 2*16+10)` = 42 minutes. The imported `totalMinutes: 90` is the unused manifest default.

## Scope

D-5 names the collision with CARD-1820 S1R5/S1R6 and CARD-1836 S5c. S1 and CP-1..CP-4 red edit only `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectTests.cs`. S2 edits only `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs`. S3 may commit `tests/derived-impact-index.json` only when the owner regeneration changes bytes, after scope is free. CP-6 and CP-8 authorize no edit. The caller dispatches Code from fresh landed master and does not merge an in-flight sibling.

## Source match

`ReleaseImpactProjectGraph.cs` blob `9496dad706bbd4e71a3e9500a299700591e174fe` is identical at master `48a4615dc` and at the reviewed tip.

- Lines 20–24 add `assembly-metadata-unavailable`. `ReleaseImpactGraphBuilder.cs:21` supplies `project-evaluation-unavailable` and `runtime-inputs-unavailable`.
- Lines 132–147 visit only `InvocationExpressionSyntax`. The static-using flag is set only when `receiverExpression` is null.
- `TryReflectionCall` (lines 242–266) handles member access, member binding, and `IdentifierNameSyntax`, and returns false otherwise. `ReflectionBindings.Read` (lines 378–382) breaks on `var` and stores no field.

`743f12fd71390a41e161419afd32171c5e20d0c9` is on master and records `cherry picked from commit 38a360617df8b1f76090777d4e0e674758094a50`. The other four CARD-1877 subjects have matching commits on master.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `a8f90a6f5295a60e1b04d84c99fe2ab8281e36f6`, no conflicts |
| `scripts/check-evidence-diff.ps1` base..HEAD | commits=1, entries=0, violations=0 |
| Impacted-class range | kinds empty, classes empty, windowsFilters empty; downgrade count 0 |
| `scripts/check-os-witnesses.ps1` | OS-WITNESS complete; only the plan markdown changed; restart not required |
| Leased `dotnet build` `tests/Antiphon.Tests` (`UseAppHost=false`, slot `055430c2`) | 0 errors, 2142 warnings, 11m 56.58s, exit 0 |
| Leased `dotnet run --no-build` of the existing CP-5..CP-8 classes (slot `6d6773de`) | total 140, failed 0, skipped 0, 4m 30s 413ms, exit 0 |

Executed split: `ReleaseImpactProjectTests` 13, `ReleaseImpactRelationTests` 4, `ReleaseImpactRuntimeTests` 4, `ReleaseImpactEnvelopeTests` 11, `DerivedImpactIndexTests` 27, `TestClassificationPolicyTests` 42, `TestClassificationGuardTests` 1, `SlowTestRegistryMergeTests` 5, `SlowTestTripwireTests` 2, `NativeLinuxPolicyWitnessTests` 3, `MechanicalPcContractTests` 11, `TaskPlatformGuidanceTests` 5, `C1729LayeringGuardTests` 11, `TestLaneCategoryGuardTests` 1.

V-1..V-4 are not in the tree, so CP-1..CP-4 were not executed. This run is one ordinary sample of the existing green subset. It does not replace the plan's provisional p95 label. `reviewedSourceClean` stays false because these runs are build-slot leases, not checkpoint-tool receipts.

Producer/recipient delivery and CARD-1083 input-wait release do not apply. No OS-selected production path changed.

## Advisories

- CARD-1883's sentence lists "a lambda" and untyped `var f = Assembly.Load`. Review `4a404450` records the cast byte-array method group as the third failure, and records expression-lambda and local-function direct loads as already classifying. V-1 is the typed local that compiles. V-3 freezes the cast form. A lambda failure witness would be a stub. Align the card text in a separate card.
- CP-8's floor 79 is the historical discovered count. This run executed 81 in the nine named classes because layering is 11. `RunnerDefaultGuidanceTests` (4 methods in the platform file) is outside the CP-8 filter. The floor can be refreshed to 81 later; 79 still passes.
- Per-row file ownership is stated in D-5 and slices S1–S3. The checkpoint table has no files column. Overlapping edits stay forbidden.
