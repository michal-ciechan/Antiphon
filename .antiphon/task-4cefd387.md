# Debug 4cefd387 — LandGraphAudit double build

LandGraphAudit builds once in a solution build. After the fix, 10 parallel `dotnet build Antiphon.sln` runs from a clean `tools/Antiphon.LandGraphAudit` obj/bin were 10/10 exit 0, 0 CS2012, 0 MSB4018, and one `Antiphon.LandGraphAudit ->` line each (13:49:50Z through 14:00:35Z). Before the fix the same loop on this host lost the file lock 0/10 and printed that path twice in 10/10 logs. `ProjectGraph` reports one LandGraphAudit node, and `TargetFramework` is not among its global properties. The project's own `TargetFramework` is net10.0.

The test-project reference keeps `SkipGetTargetFrameworkProperties="true"` and no longer passes `SetTargetFramework`. The precheck refuses `SetTargetFramework`. `CopyStagedHelpers` still passes `TargetFramework` on its inner MSBuild after the reference build; that call is sequential and was left as it is. The LandGraphAudit solution entry is closed with `EndProject`. `6a371bc38888a77325fbdc24e55e61850ed05e68` had inserted the WriteBehind project line between the LandGraphAudit `Project` line and its `EndProject`, which made `ProjectGraph` refuse the solution with MSB4262 while `dotnet build` still compiled the project.

## Commits

All pushed to `feat/card-task-4cefd387`:

- `f83142404ed88b62bcb361e90c037aea68ddc6c0` red witness (`LandGraphAuditProjectInstanceTests` failed at this tip: the reference still assigned `TargetFramework`)
- `89e75a46afa7051cee50de1a1a11d8f235540197` fix: drop `SetTargetFramework`, close the solution entry, refuse the metadata in the precheck
- `a600abcbe3e180f29f466e29baa397b994b1aa8a` the real-tree assertion matches `SetTargetFramework=` and `<SetTargetFramework`, so the comment that names the metadata is allowed
- `98a16e60a4679206d697543d2057887c256dc167` why-missed note
- `722068cc1398aa965126bccf1c41220468b0bca2` `chore: refresh land-graph records for the landing batch`

## Tests

On the binaries built from `a600abcbe3` (later commits change the note and the land-graph records only):

- `/*/*/LandGraphAuditProjectInstanceTests*/*` — 1 passed, 0 failed
- `/*/*/LandNonCodeSkipTests/C1483_PinnedHelperMetadataRefusesOtherShapes*` — 17 passed, 0 failed

The first C1483 run after the fix failed 17/17 because `ShouldNotContain("SetTargetFramework")` matched the comment. The assertion commit is the repair. The witness was red at `f83142404` and green at `89e75a46` and again after the assertion rebuild.

## Land graph

The refresh ran while `origin/master` was `25b347f493cb781b693abc450e7090491c8638ae`. At that tip, `git merge-tree --write-tree origin/master HEAD` exited 0 and the written tree equalled `HEAD^{tree}`.

A later fetch moved `origin/master` to `ed162195eb07f0be8ae79868a9119266467ea6f4`. `git merge-tree --write-tree origin/master HEAD` then exited 1. The only content conflict is `scripts/audit-land-verification-precheck.ps1`. `bc96c71bef39761b4dbc5c05e1030312396a0e16` replaced that script's PowerShell pre-check with a wrapper that runs the C# pre-check. The pin this branch removed now lives on master in `tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs`, `PinnedMetadata`, as `projectReference["SetTargetFramework"] = "TargetFramework=net10.0"`. Master's `tests/Antiphon.Tests/Antiphon.Tests.csproj` still has that attribute. This branch was not merged with master. A land has to drop that C# pin and keep this branch's reference, solution entry, and `C1483` test. A scratch `git merge-file` of `LandNonCodeSkipTests.cs` exits 0 and keeps this branch's assertion.

`-Check` before the refresh refused in 11 seconds with `manifest does not match the selected graph` and `land graph pre-check refused` (digest `3ED4A38CAA52947D313A1BAD14BA15203DD7F261A219BBC20704CB13E329EB46`, count 75). That is a record mismatch. `scripts/land-graph-land-refresh.ps1 -Base 25b347f493cb781b693abc450e7090491c8638ae` printed `action=refresh` and its second `-Check` passed. Digest `3BE0F9C518C0B4A9F92CCCAB63B26452F832F728483F66AB92F2C1D720C7D075` to `3ED4A38CAA52947D313A1BAD14BA15203DD7F261A219BBC20704CB13E329EB46`, count stayed 75, pins blob stayed `7a2586e58bd4768648c6f670f6f2c4432f68936a`. Changed records: `Antiphon.sln`, `tests/Antiphon.Tests/Antiphon.Tests.csproj`. The refresh commit contains `LandVerificationBuildGraph.manifest.txt` and `LandVerificationBuildGraph.evaluation.txt` only.

## WHY-MISSED

Note: `docs/investigations/2026-11-09-why-missed-landgraphaudit-double-build.md`

1. introducing commit/card: `19e6c73957bc01e36f2b0b85229e6f0c0ed5e76c` (CARD-1483). The second instance began when `f5cc4facfacdc4e4a72a6d098d225305624dac12` (CARD-1780) added the project to `Antiphon.sln`.
2. approving review(s) with scope (Full/Interim) and the selection they actually ran: six Clean reviews, each `ordinaryScopeCompleted: Full`. Landing review `a3467729-e2ed-48f7-8283-5505e5f4e54b`, subject `bb5773d2-1779-40a3-8afa-db6701554690`, sha `a8b433277a88bce0ba76468c08d584312162fa75` (2026-10-23 04:41Z). The outcome detail does not store a filter. The S5 plan CP-5 selection is `/*/*/(LandGraphEvaluationTests*)|(LandNonCodeSkipTests*)/*` (144 tests). The other Clean shas are `0fab803fde898c026529d69988a92ae9aae51e0e`, `b3d62ad1eade7222e544b1039dce94f7e19a092a`, `69089d6061f79ca93de444ce383b76b5bb4fba6d`, `94983dd40c44982679e90137aca7171dce2183ca`, and `2d08c43b726f4aaa299013d5c9999bbc4b8714d0`.
3. why the failing test was outside that selection (lane/category/path): qualification prerequisite, `dotnet build Antiphon.sln`, `tools/Antiphon.LandGraphAudit`. It fails only when the two writes overlap. Merge 5c209018 saw MSB4018, rebuilt with `-m:1`, and left an optional card unfiled. Earlier Merges did the same. Windows run f8c8904b on rc/2026-11-14 (`7bee43614`) ended 13:01:42Z exit 1 `prerequisite-failed:dotnet build`.
4. the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782: `tests/Antiphon.Tests/Antiphon.Tests.csproj`, `tools/Antiphon.LandGraphAudit/**`, `Antiphon.sln` -> `/*/*/LandGraphAuditProjectInstanceTests*/*`
5. same cause as an already written note: none.

Proposal only: the qualification run-prerequisite `dotnet build` retries once when the failure is CS2012. This branch does not change those scripts. The orchestrator files the card.

Owned `bin`, `obj`, `bin-land-audit`, and the graph probe were removed by literal path after the runs. Nothing ran on the desktop.
