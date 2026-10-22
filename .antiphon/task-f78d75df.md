# Code f78d75df: CARD-1409 repair 8, a later part's directory property (Review d43d1e4a R2)

Outcome: done. Branch `feat/card-task-f78d75df` (worktree `/work/worktrees/task-f78d75df`, server2), fast-forward from `f8f3233f3cddce69cb4783e0de73565de7a5108d` (the Code 18ac0f5a tip). This report is committed before the one CP-1 checkpoint run, so the pushed tip is itself the tested source; the CHECKPOINT lines and validator exits are in the task's final message.

## Commits (all pushed)

| SHA | Slice |
|---|---|
| `c78c9f14c5b19c2569d9cae85d153e65f13db092` | S26 red witness `C1409_LaterPartDirectoryPropertyIsExpanded`; plan Repair 8 (D-39..D-41, S26..S28, V-31); CP-1 moved to S28 expecting 84 (Covers adds V-31) |
| `e176bb9e8555e8f7e42a12464909ac00cad18597` | S27 fix in `scripts/audit-land-verification-graph.ps1` (D-39), with the script-header and repair 7 skip-comment corrections |
| `b430e5d1134b28d95c4ae1eaaee82e2819d8fb24` | S28 wording: `docs/orchestration-loop.md` |
| (this commit) | this report |

## What changed (plan D-39)

`Resolve-ItemPathText`, `-DirectoryPropertiesOnly` branch: it no longer expands a directory property only at the start of the whole text. The text goes into the one split loop unexpanded; inside the loop each trimmed part that starts with `$(MSBuildThisFileDirectory)` / `$(MSBuildProjectDirectory)` (case-insensitive, inner whitespace allowed, as before) has that property replaced by the file or project directory and is marked anchored. The existing check then runs on the expanded part: a part that still carries `$(`, `@(` or `%(` stays `unresolved`. The directory values carry no `;`, so split-then-expand-per-part matches MSBuild's expand-then-split. The full expansion (`Expand-Candidates`) is unchanged, as are all consumers. Repair 7's comment in `Assert-PropertyValuesAllowed` ("a reference past a leading directory property") now says per part.

## Witness (plan D-40, V-31)

`C1409_LaterPartDirectoryPropertyIsExpanded` (28th `C1409_*` method):
- On the real test project tree, `<PackageReadmeFile>x.json;$(MSBuildThisFileDirectory)../../docs/guide.md</PackageReadmeFile>` and the `$(MSBuildProjectDirectory)/` form refuse naming `tests/Antiphon.Tests/Antiphon.Tests.csproj PackageReadmeFile -> docs/guide.md`.
- On a fixture, `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md` and `src/x.json;$(MSBuildProjectDirectory)/../docs/guide.md` refuse naming `server/App.csproj PackageTags -> docs/guide.md`.
- Controls: `src/x.json;$(Description)/../../docs/guide.md` and `src/x.json;$(Undefined)../docs/guide.md` stay approved with count=2, as at fd20e8dcc.
- Imports: `../eng/shared.props;$(MSBuildThisFileDirectory)../eng/missing.props` refuses naming `-> eng/missing.props`, where the tip said `cannot be resolved`. The `$(MSBuildProjectDirectory)/../eng/shared.props` form is approved with count=2. A later `$(Description)/...` part still refuses `import cannot be resolved`.

Every outcome was confirmed first against the fd20e8dcc, f8f3233f3 and fixed scripts on scratch fixtures, using script-only `pwsh` with no build or test driver.

## Runs (unlisted, each with its reason; every build or test driver went through `scripts/build-slot.ps1`)

1. **Red proof at `c78c9f14c`** (reason: red proof before the fix, S26). Build `tests/Antiphon.Tests --property:OutputPath=bin-c1409r8/ -p:UseAppHost=false`: `BUILD SLOT granted lease=a48cee5a-58df-476a-b55e-97110e7c0654 waited=60s maxcpucount=6`, held 227 s, Build succeeded. Run `dotnet tests/Antiphon.Tests/bin-c1409r8/Antiphon.Tests.dll --treenode-filter '/*/*/LandNonCodeSkipTests/C1409_LaterPartDirectoryPropertyIsExpanded'`: `BUILD SLOT granted lease=b772f95e-9186-4006-9b30-aee5ffe3cf80 waited=75s`, held 7 s. TRX counters: total=1 executed=1 passed=0 failed=1 skipped=0. `ShouldAssertException: ran.Code should be 1 but was 0`: the real test project with the later `$(MSBuildThisFileDirectory)` part was approved. TRX: `.antiphon/c1409r8-red/red.trx`.
2. **MSBuild probe** (reason: settle sweep row 37 against real MSBuild rather than memory): `dotnet msbuild <scratch>/server/App.proj -getItem:None -getItem:L -getProperty:PackageTags`, `BUILD SLOT granted lease=697ddf7d-fdbf-4d55-9a48-1770cfd3e792 waited=451s`, held 1 s, exit 0.
   - `<L Include="src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md"/>` gives FullPath `<scratch>/ms/docs/guide.md`, which confirms R2's MSBuild semantics.
   - `%24(MSBuildThisFileDirectory)../docs/guide.md` gives Identity `$(MSBuildThisFileDirectory)../docs/guide.md` and FullPath `<scratch>/ms/server/$(MSBuildThisFileDirectory)../docs/guide.md`. That is a literal segment, not docs.
   - `%24(MSBuildThisFileDirectory)/../docs/guide.md` gives FullPath `<scratch>/ms/server/docs/guide.md`.
3. **Real-repo acceptance (D-41, V-7)** at `e176bb9e8`: `pwsh scripts/audit-land-verification-graph.ps1 -Check` printed `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57`, exit 0. Script checks: 0 non-ASCII bytes; `Parser::ParseFile` 0 errors; the added lines contain no `??`, `&&`, `||` or `?.`. The orchestration-loop added lines are ASCII.
4. **Ordinary-edit acceptance (D-27/D-41)** at `b430e5d11`, in a scratch detached worktree (discarded): `tests/Antiphon.Tests/Antiphon.Tests.csproj` plus `PackageReference Humanizer.Core`, linked `Compile ..\..\server\Program.cs` and `ProjectReference ..\..\tools\Antiphon.Checkpoints\Antiphon.Checkpoints.csproj ReferenceOutputAssembly="false"`. `-Update` printed `digest=5562F875... count=57`.
5. **Differential sweep (D-41)**, script-only `pwsh`, no slot: 107 shapes x {fd20e8dcc, f8f3233f3, fix}. The fix column was re-run against the committed `e176bb9e8` script and is byte-identical. Table below.

Repetitions: one red run, one CP-1 run; no green subset run and no loaded repetition.

## Differential sweep

The fixture is `server/App.csproj` with `<Description>sub</Description>`, plus tracked `docs/guide.md`, `README.md`, `src/x.json`, `eng/shared.props` (so count=2) and `eng/docs.items`. Rows marked V put the shape in `<PackageTags>`. Rows marked I put it in `<Import Project="...">`, with PackageTags `x`. In the result columns, A means `-Update` exit 0 and R means exit 1 with its category: value = property value names a docs path; import-unresolved; import-target = not a tracked project file; segment = Windows land host normalisation.

Summary:
- **Restored to fd20e8dcc's outcome (R2):** every V shape whose later part starts with a directory property, refused at fd20e8dcc and approved at the tip. That covers rows 10-14, 16-19, 25-28, 30-35, 44, 46-50, 52, 53, 58, 68, 70-72, 74, 77, 79, 81-84: case/whitespace spellings, backslashes, escapes after the directory, a reference in an earlier or later part, empty parts, and leading/trailing `;`. Every import whose later or whitespace-led part starts with a directory property (rows 87-89, 91, 105, 106) is followed again, as at fd20e8dcc.
- **Refused at fd20e8dcc and approved by the fix: one shape, row 37 `src/x.json;%24(MSBuildThisFileDirectory)../docs/guide.md`. Not changed.** It is already approved at the tip, so this repair did not introduce it; it comes from repair 6 D-33/N-26 ("a decoded `$(` is literal text, never a reference"). fd20e8dcc decoded before it expanded, which repair 6 fixed as an order defect. Run 2 shows MSBuild reads that part as the literal `server/$(MSBuildThisFileDirectory)../docs/guide.md`, not `docs/guide.md`. fd20e8dcc's refusal was an over-refusal, not a protection. Making it refuse again would contradict D-33. That decision belongs to the caller or Review.
- Approved at fd20e8dcc and refused by the fix: row 100 `%24(MSBuildThisFileDirectory)../eng/shared.props` as an Import (fail-closed). Same D-33 origin, unchanged since repair 6.
- Both refuse with a different message: rows 92, 101-103 (imports). fd20e8dcc substituted a directory property anywhere in a part and named a non-tracked target; the fix refuses `import cannot be resolved` for a directory property past the start of its part, or for a NUL.
- Disclosure outside this sweep (fail-closed, unchanged since repair 6, not touched): an item `Include` with a later directory part, e.g. `<None Include="../src/x.json;$(MSBuildProjectDirectory)/../src/x.json"/>`, is approved at fd20e8dcc and refused `uses a property the audit cannot resolve` at the tip and with the fix. That is the full expansion's rule that a root belongs to the first part. No tracked file uses the shape (`-Check` count=57 unchanged).

| # | K | Shape | fd20e8dcc | tip f8f3233f3 | fix |
|---:|---|---|---|---|---|
| 1 | V | `../docs/guide.md` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 2 | V | `src/x.json;../docs/guide.md` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 3 | V | `../docs/guide.md;src/x.json` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 4 | V | `;../docs/guide.md` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 5 | V | `../docs/guide.md;` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 6 | V | `;;../docs/guide.md;;` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 7 | V | ` ; ../docs/guide.md ; ` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 8 | V | `$(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 9 | V | `$(MSBuildProjectDirectory)/../docs/guide.md` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 10 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 11 | V | `src/x.json;$(MSBuildProjectDirectory)/../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 12 | V | `src/x.json; $(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 13 | V | `src/x.json;<TAB>$(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 14 | V | `;$(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 15 | V | `$(MSBuildThisFileDirectory)../docs/guide.md;` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 16 | V | `;;$(MSBuildProjectDirectory)/../docs/guide.md;;` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 17 | V | `src/x.json;$(msbuildthisfiledirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 18 | V | `src/x.json;$(MSBUILDPROJECTDIRECTORY)/../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 19 | V | `src/x.json;$( MSBuildThisFileDirectory )../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 20 | V | `src/x.json;x/$(MSBuildThisFileDirectory)../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 21 | V | `src/x.json;$(MSBuildThisFileDirectory)$(MSBuildThisFileDirectory)../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 22 | V | `$(MSBuildThisFileDirectory)$(MSBuildProjectDirectory)/../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 23 | V | `src/x.json;$(MSBuildThisFileDirectory)$(Description)/../../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 24 | V | `$(MSBuildThisFileDirectory)../docs/guide.md;$(MSBuildProjectDirectory)/../README.md` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 25 | V | `src/x.json;$(MSBuildThisFileDirectory)../README.md` | R value -> README.md | A count=2 | R value -> README.md |
| 26 | V | `src/x.json;$(MSBuildProjectDirectory)/../README.md` | R value -> README.md | A count=2 | R value -> README.md |
| 27 | V | `src/x.json;$(MSBuildThisFileDirectory)..\docs\guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 28 | V | `src/x.json;$(MSBuildProjectDirectory)\..\docs\guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 29 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/*.md` | A count=2 | A count=2 | A count=2 |
| 30 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs` | R value -> docs | A count=2 | R value -> docs |
| 31 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/` | R value -> docs | A count=2 | R value -> docs |
| 32 | V | `src/x.json;$(MSBuildThisFileDirectory)../d%6Fcs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 33 | V | `src/x.json;$(MSBuildThisFileDirectory)%2E%2E/docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 34 | V | `src/x.json;$(MSBuildThisFileDirectory)..%2Fdocs%2Fguide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 35 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md%3Bsrc/x.json` | R value -> docs/guide.md;src/x.json | A count=2 | R value -> docs/guide.md;src/x.json |
| 36 | V | `$(MSBuildThisFileDirectory)../docs/guide.md%3B$(MSBuildThisFileDirectory)../README.md` | A count=2 | A count=2 | A count=2 |
| 37 | V | `src/x.json;%24(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | A count=2 |
| 38 | V | `src/x.json%3B../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 39 | V | `src/x.json%3B$(MSBuildThisFileDirectory)../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 40 | V | `../d%6Fcs/guide.md` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 41 | V | `src/x.json;../d%6Fcs/guide.md` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 42 | V | `../docs/guide.md%00` | A count=2 | A count=2 | A count=2 |
| 43 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md%00` | A count=2 | A count=2 | A count=2 |
| 44 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/guide%2A.md` | R value -> docs/guide*.md | A count=2 | R value -> docs/guide*.md |
| 45 | V | `../docs./guide.md` | R value -> docs/guide.md segment=docs. (trailing dot or space) | R value -> docs/guide.md segment=docs. (trailing dot or space) | R value -> docs/guide.md segment=docs. (trailing dot or space) |
| 46 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs./guide.md` | R value -> docs/guide.md segment=docs. (trailing dot or space) | A count=2 | R value -> docs/guide.md segment=docs. (trailing dot or space) |
| 47 | V | `src/x.json;$(MSBuildThisFileDirectory)../README.md.` | R value -> README.md segment=README.md. (trailing dot or space) | A count=2 | R value -> README.md segment=README.md. (trailing dot or space) |
| 48 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md:stream` | R value -> docs/guide.md segment=guide.md:stream (colon) | A count=2 | R value -> docs/guide.md segment=guide.md:stream (colon) |
| 49 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md::$DATA` | R value -> docs/guide.md segment=guide.md::$DATA (colon) | A count=2 | R value -> docs/guide.md segment=guide.md::$DATA (colon) |
| 50 | V | `@(None);$(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 51 | V | `$(MSBuildThisFileDirectory)../docs/guide.md;@(None)` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 52 | V | `%(None.Identity);$(MSBuildProjectDirectory)/../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 53 | V | `$([System.String]::Copy(''));$(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 54 | V | `$(MSBuildThisFileDirectory)../docs/guide.md;$([System.String]::Copy(''))` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 55 | V | `src/x.json;$(MSBuildThisFileDirectory)@(None)../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 56 | V | `src/x.json;$(MSBuildThisFileDirectory)%(None.Identity)/../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 57 | V | `src/x.json;$(MSBuildThisFileDirectory)$([System.String]::Copy('../docs/guide.md'))` | A count=2 | A count=2 | A count=2 |
| 58 | V | `$(Description);$(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 59 | V | `$(Undefined);../docs/guide.md` | R value -> docs/guide.md | R value -> docs/guide.md | R value -> docs/guide.md |
| 60 | V | `$(Description)/../../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 61 | V | `src/x.json;$(Description)/../../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 62 | V | `src/x.json;$(Undefined)../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 63 | V | `src/x.json;$(MSBuildThisFileDirectory)` | A count=2 | A count=2 | A count=2 |
| 64 | V | `$(MSBuildThisFileDirectory)` | A count=2 | A count=2 | A count=2 |
| 65 | V | `$(MSBuildThisFileDirectory);$(MSBuildProjectDirectory)` | A count=2 | A count=2 | A count=2 |
| 66 | V | `src/x.json;$(MSBuildProjectDirectory)../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 67 | V | `src/x.json;$(MSBuildThisFileDirectory)../../outside/docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 68 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md;src/y.json` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 69 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md$(Description)` | A count=2 | A count=2 | A count=2 |
| 70 | V | `src/x.json;$(MSBuildThisFileDirectory)./../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 71 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 72 | V | `src/x.json;$(MSBuildThisFileDirectory)../DOCS/Guide.md` | R value -> DOCS/Guide.md | A count=2 | R value -> DOCS/Guide.md |
| 73 | V | `src/x.json;$(OutDir)../../docs/guide.md` | A count=2 | A count=2 | A count=2 |
| 74 | V | `$(OutDir);$(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 75 | V | `$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)../docs/guide.md'))` | R value | R value | R value |
| 76 | V | `src/x.json;$([System.IO.File]::ReadAllText('../docs/guide.md'))` | R value | R value | R value |
| 77 | V | `$([System.IO.File]::ReadAllText('$(OutDir)x.txt'));$(MSBuildThisFileDirectory)../docs/guide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 78 | V | `src/x.json;$(MSBuildThisFileDirectory)../src/x.json` | A count=2 | A count=2 | A count=2 |
| 79 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/data.json` | R value -> docs/data.json | A count=2 | R value -> docs/data.json |
| 80 | V | `true;$(MSBuildThisFileDirectory)guide.md` | A count=2 | A count=2 | A count=2 |
| 81 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs%2Fguide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 82 | V | `src/x.json;$(MSBuildThisFileDirectory)..%5Cdocs%5Cguide.md` | R value -> docs/guide.md | A count=2 | R value -> docs/guide.md |
| 83 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md%20` | R value -> docs/guide.md segment=guide.md  (trailing dot or space) | A count=2 | R value -> docs/guide.md segment=guide.md  (trailing dot or space) |
| 84 | V | `src/x.json;$(MSBuildThisFileDirectory)../docs%2E/guide.md` | R value -> docs/guide.md segment=docs. (trailing dot or space) | A count=2 | R value -> docs/guide.md segment=docs. (trailing dot or space) |
| 85 | I | `../eng/shared.props` | A count=2 | A count=2 | A count=2 |
| 86 | I | `$(MSBuildThisFileDirectory)../eng/shared.props` | A count=2 | A count=2 | A count=2 |
| 87 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)../eng/shared.props` | A count=2 | R import-unresolved | A count=2 |
| 88 | I | `../eng/shared.props;$(MSBuildProjectDirectory)/../eng/shared.props` | A count=2 | R import-unresolved | A count=2 |
| 89 | I | `;$(MSBuildThisFileDirectory)../eng/shared.props` | A count=2 | R import-unresolved | A count=2 |
| 90 | I | `../eng/shared.props;` | A count=2 | A count=2 | A count=2 |
| 91 | I | ` $(MSBuildThisFileDirectory)../eng/shared.props` | A count=2 | R import-unresolved | A count=2 |
| 92 | I | `../eng/shared.props;x/$(MSBuildThisFileDirectory)../eng/shared.props` | R import-target -> server/x/eng/shared.props | R import-unresolved | R import-unresolved |
| 93 | I | `../eng/shared.props;$(Description)/../eng/shared.props` | R import-unresolved | R import-unresolved | R import-unresolved |
| 94 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)$(Description)/../eng/shared.props` | R import-unresolved | R import-unresolved | R import-unresolved |
| 95 | I | `../eng/shared.props;@(None)` | R import-unresolved | R import-unresolved | R import-unresolved |
| 96 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)../eng/docs.items` | R import-target -> eng/docs.items | R import-unresolved | R import-target -> eng/docs.items |
| 97 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)../eng/missing.props` | R import-target -> eng/missing.props | R import-unresolved | R import-target -> eng/missing.props |
| 98 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)../eng/*.props` | R import uses a wildcard the audit does not follow | R import-unresolved | R import uses a wildcard the audit does not follow |
| 99 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)../../outside.props` | R import-target -> outside the repository | R import-unresolved | R import-target -> outside the repository |
| 100 | I | `%24(MSBuildThisFileDirectory)../eng/shared.props` | A count=2 | R segment | R segment |
| 101 | I | `../eng/shared.props%3B$(MSBuildThisFileDirectory)../eng/shared.props` | R import-target -> eng/eng/shared.props | R import-unresolved | R import-unresolved |
| 102 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)../eng/shared.props%00` | R docs-include | R import-unresolved | R import-unresolved |
| 103 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)$(MSBuildThisFileDirectory)../eng/shared.props` | R import-target -> se | R import-unresolved | R import-unresolved |
| 104 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)../eng./shared.props` | R segment | R import-unresolved | R segment |
| 105 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)../eng/shar%65d.props` | A count=2 | R import-unresolved | A count=2 |
| 106 | I | `../eng/shared.props;$(MSBuildThisFileDirectory)..\eng\shared.props` | A count=2 | R import-unresolved | A count=2 |
| 107 | I | `;;` | R import-target -> (empty) | R import-target -> (empty) | R import-target -> (empty) |

Row 13's shape is `src/x.json;<TAB>$(MSBuildThisFileDirectory)../docs/guide.md`.

## Invariants

- V-31 is red first at `c78c9f14c` (run 1) and is covered green by CP-1.
- V-7 passes (run 3 and CP-1).
- V-1..V-5 and V-8..V-30 are covered by CP-1. That is every ordinary V-n of the plan; the plan has no R-n.
- No new PC rows. Existing land PCs stay pending for SourceLanding Mutation; nothing is discharged here.

## Landing checks (at `b430e5d11`; re-run at the final tip in the final message)

- `git merge-tree --write-tree origin/master b430e5d11` (master `711409c41b8263a29f9ba355ac1de1463826de00`): exit 0, no conflicts, tree `02c0aaf1472220292bd59de30e39be544f7a40b9`. Nothing merged.
- Cherry-pick of the 36 commits `9b39df4e0..b430e5d11` (`-x`, in order) onto `origin/rc/2026-10-20` (`ceb6e3d6b78471db6831258699c493b8eb2d619b`), in a scratch detached worktree: exit 0, 36 applied, no conflicts. The script, the test class, `docs/orchestration-loop.md`, `LandVerificationBuildGraph.cs` and the plan are byte-identical to the branch. Scratch removed and pruned; nothing pushed.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 9b39df4e0 -HeadRef b430e5d11`: commits=36 entries=4 violations=0. With `-BaseRef f8f3233f3`: commits=3 entries=0 violations=0.

## Out-of-contract residue

Unchanged from D-35/D-38. CARD-1483 (well-known item metadata) is pre-existing and untouched.

## Residue and cleanup

- `bin-c1409r8/` (27 directories) was deleted before the checkpoint, except `tools/Antiphon.Checkpoints/bin-c1409r8`, which runs the checkpoint tool and is deleted after `validate`.
- Scratch worktrees are removed and pruned. Probe fixtures live under the session scratchpad.
- `.antiphon/c1409r8-red` TRX and the checkpoint run folder are retained (ignored).
