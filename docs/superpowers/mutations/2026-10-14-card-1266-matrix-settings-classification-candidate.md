# CARD-1266 mutation candidate (unbound)

Plan: `docs/superpowers/plans/2026-10-14-card-1266-matrix-settings-classification.md`.
Every row is pending for method-scoped SourceLanding Mutation; Code ran none.

| ID | Treenode filter | Mutant | Expected red |
|---|---|---|---|
| PC-1 | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | delete class-level `[Category("Unit")]` on `MatrixDefaultComplexitySettingsTests` | `lane-xor Antiphon.Tests.Application.MatrixDefaultComplexitySettingsTests` |
| PC-2 | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | add `[Category("Unit")]` to `MatrixDefaultComplexityRoutingTests` (keeps `Integration`) | `lane-xor Antiphon.Tests.Application.MatrixDefaultComplexityRoutingTests` |
| PC-3 | `/*/*/TestClassificationPolicyTests/C487_G068` | PC-1 mutant | assertion `ShouldNotContain("lane-xor")` fails |
