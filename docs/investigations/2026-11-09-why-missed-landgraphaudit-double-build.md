# Why missed: LandGraphAudit compiled twice into one obj directory

One committed note for the rc/2026-11-14 qualification start that died on CS2012 in
`tools/Antiphon.LandGraphAudit`. Windows run f8c8904b at `7bee43614` ended 13:01:42Z,
exit 1, `prerequisite-failed:dotnet build`. CARD-1781. Debug task 4cefd387.

## Failure

A solution build and the `tests/Antiphon.Tests` ProjectReference were two MSBuild project
instances of the same single-targeted project. The solution instance has no `TargetFramework`
global property. The reference added `SetTargetFramework="TargetFramework=net10.0"`, which is
a second global-property set, so both instances wrote `obj/Debug/net10.0`. On Windows the
loser reported CS2012 (the output file was locked). On Linux the same overlap has reported
MSB4018 on `runtimeconfig.json`. The previous Windows run 231f573c at `653f1145d` built
cleanly, so the defect is a timing race.

Before this fix, ten parallel `dotnet build Antiphon.sln` runs from a clean
`tools/Antiphon.LandGraphAudit` obj on this Linux host lost the file lock zero times and
printed the LandGraphAudit output path twice in every log (10/10). After the fix,
`ProjectGraph` reports one LandGraphAudit node and `TargetFramework` is absent from its
global properties. The project's own `<TargetFramework>net10.0</TargetFramework>` still applies.

`SkipGetTargetFrameworkProperties="true"` stays. For a single-targeted project the SDK drops
an incoming `TargetFramework` global, so the skip alone keeps the tool on net10.0 and keeps
it from inheriting the test project's net9.0. `CopyStagedHelpers` still passes
`TargetFramework` on its own inner MSBuild after the reference build; that call is sequential
and is unchanged. The precheck pin no longer allows `SetTargetFramework`. Reintroducing it
refuses as metadata the audit does not allow.

A second defect showed up while loading the graph. `6a371bc38888a77325fbdc24e55e61850ed05e68`
inserted the WriteBehind project line between the LandGraphAudit `Project` line and its
`EndProject`. `ProjectGraph` then refused the solution with MSB4262. `dotnet build` still
compiled the project. The fix closes the LandGraphAudit entry.

## Introducing commit/card

introducing commit/card: `19e6c73957bc01e36f2b0b85229e6f0c0ed5e76c` (CARD-1483)

That commit set `SetTargetFramework="TargetFramework=net10.0"` on the test project's
reference so the tool evaluated as net10.0. The project was not yet a solution member, so
there was one instance. The second instance appeared when `f5cc4facfacdc4e4a72a6d098d225305624dac12`
(CARD-1780, cherry-pick of `16f41344cbfad0c041bfda2cc196fa2aab90ed4f`) added
`tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj` to `Antiphon.sln`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

CARD-1483 stage outcomes. Each Clean row records `ordinaryScopeCompleted: Full` and
`reviewedSourceClean: true`. The outcome detail is `commits=0` or `commits=1` and does not
store a filter. The S5 plan's closed CP-5 list is
`/*/*/(LandGraphEvaluationTests*)|(LandNonCodeSkipTests*)/*` (144 tests: evaluation and
precheck). No checkpoint in that plan is a parallel `dotnet build Antiphon.sln`.

- `1aedf0f7-5546-4223-8ad8-5e08e64db55e`, subject `deb6730d-e7df-4344-9769-680fa591db18`, Final, Full, sha `0fab803fde898c026529d69988a92ae9aae51e0e` (2026-10-22 18:28Z)
- `69f144fd-cda2-4807-a0bc-1710d228852c`, subject `1757a065-8fcc-424a-8ac6-0b51020e6995`, Full, sha `b3d62ad1eade7222e544b1039dce94f7e19a092a`
- `0e1d9d0b-d191-4553-b7a5-b7fdd0fb78a3`, subject `1757a065-8fcc-424a-8ac6-0b51020e6995`, Full, sha `69089d6061f79ca93de444ce383b76b5bb4fba6d`
- `4e2b240e-4e6a-4b60-a22e-10f735e28b47`, subject `0ca57dc9-7d5c-459f-8bc2-bf201f51e528`, Full, sha `94983dd40c44982679e90137aca7171dce2183ca`
- `53888829-0e2e-49f2-a516-0f69dd7ba723`, subject `6e80fc7e-008b-4c02-af99-8b6ff3ef8dc1`, Final, Full, sha `2d08c43b726f4aaa299013d5c9999bbc4b8714d0` (2026-10-23 02:13Z)
- Landing Clean `a3467729-e2ed-48f7-8283-5505e5f4e54b`, subject `bb5773d2-1779-40a3-8afa-db6701554690`, Final, Full, sha `a8b433277a88bce0ba76468c08d584312162fa75` (2026-10-23 04:41Z)

Found rounds before those Cleans (`cb067299`, `8e9f68b4`, `cff23aba`, `52485763`) also
recorded Full. `cff23aba` caught `HelperTargetFramework` accepting any value. Their plan
checkpoints are the same evaluation and precheck fixtures.

## Outside the selection

why the failing test was outside that selection (lane/category/path): qualification prerequisite,
`dotnet build Antiphon.sln`, path `tools/Antiphon.LandGraphAudit`.

The failure is a compiler file lock during the run-start build. It is not a TUnit result, so
it sits outside `LandGraphEvaluationTests` and `LandNonCodeSkipTests`. It only appears when
the two instances overlap. A single-threaded build, and many parallel builds, finish with
exit 0 while still compiling the project twice. Merge 5c209018 (batch 138) saw MSB4018 on
`runtimeconfig.json`, rebuilt with `-m:1`, and left an optional card unfiled. Earlier Merges
did the same and treated the message as build noise. The Windows consequence is that a
qualification run never started: the prerequisite build lost the race.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Antiphon.Tests.csproj`, `tools/Antiphon.LandGraphAudit/**`, `Antiphon.sln` ->
`/*/*/LandGraphAuditProjectInstanceTests*/*`

The class reads the reference and the solution entry. It fails when `SetTargetFramework`,
`AdditionalProperties`, or `Properties` assigns `TargetFramework`, and when the LandGraphAudit
`Project` line is not followed by `EndProject`. That is the regression check. The race itself
is a ten-build parallel solution loop and is too slow for every land.

Proposed card, which this task does not file and does not implement: the qualification
run-prerequisite `dotnet build` retries once when the failure is CS2012. One retry covers the
timing loss. It does not remove the second instance; this fix does that. Do not change the
qualification scripts on this branch.

## Exemption

same cause as an already written note: none.
