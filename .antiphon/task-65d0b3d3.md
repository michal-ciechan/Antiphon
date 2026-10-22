# CARD-1483 S1 - Code task 65d0b3d3 report

Plan: docs/superpowers/plans/2026-10-22-card-1483-real-msbuild-land-audit-plan.md (Plan f7dd9618, e344858f6, on origin/master).
Branch feat/card-task-65d0b3d3, base 462379b02fd355fa1e9a796d3d7eda512c065d37. Operator O-1..O-4 at defaults (no restore, emulation kept, master only).

## Slices
- 0a991fb7b red (+ compile fix feca0ce22): `tests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs` (C1483_S1_ApiSurfaceAvailable, _EvaluatedSkippableItemRefuses, _HarmlessFixtureApproves, _EvaluationFailureDenies).
  Red witness (unlisted, stated): build-slot build of tests/Antiphon.Tests to bin-c1483-red/ then
  `--treenode-filter /*/*/LandGraphEvaluationTests/(C1483_S1_*)`: total 6, failed 6 (tool not staged; V-3 then had 3 [Arguments] cases).
- 5a5ea5f5b fix: `tools/Antiphon.LandGraphAudit` (net10.0; Microsoft.Build 18.0.2 + Framework compile-only, Microsoft.Build.Locator 1.11.2).
  `--repo <dir> --solution Antiphon.sln` evaluates every MSBuild project of the sln (Configuration/Platform from the sln's Debug|Any CPU mapping,
  ArtifactsPath under an uncreated temp dir, no restore, inner TargetFramework evaluations), refuses every item of every type whose FullPath is
  `<repo>/docs/**/*.md` or `<repo>/README.md` (case-insensitive, `\` folded to `/` off Windows), prints `refused: <defining file> <itemType> <FullPath>` (exit 1),
  `approved` (exit 0) or `evaluation-failed: <message>` (exit 2: exception, logged MSBuild error, Locator miss, 120 s solution / 300 s tool timeout).
  `--api-probe` reports the D-1 members by reflection on the Locator-loaded Microsoft.Build. The tool writes nothing.
  Staged by Antiphon.Tests into `land-graph-audit/` (StagedHelper `HelperTargetFramework="net10.0"`; ProjectReference with SetTargetFramework). Not in Antiphon.sln.
  V-3's three fixtures folded into one method so CP-1 executes exactly the plan's 4.
- 09876e2d5 manifest: `LandVerificationBuildGraph.manifest.txt` regenerated with master's unchanged audit script (`-Update`, then `-Check` exit 0): digest 90CCCDA2..., count 58.

Dev checks (unlisted, stated): one build-slot build of the tool alone (bin-c1483-dev, deleted) and direct runs on a scratch fixture: refusals x3 exit 1,
missing import / bad SDK / malformed XML exit 2, TargetFramework-conditioned item in a multi-target project refused, no obj dir created.

Not touched: scripts/audit-land-verification-graph.ps1, CARD-1409 branch chain. RC: master-only (O-4).
CP-1 is run at the commit that carries this report; its CHECKPOINT lines and outcome are in the final task message.

PCs pending for SourceLanding Mutation (plan): none in S1's rows (the plan's PCs target S3/S4/S5); suggested S1 controls: exit 2 counted as approval (V-3), skippable rule restricted to well-known item types (V-1 AntiphonCustomItem).
