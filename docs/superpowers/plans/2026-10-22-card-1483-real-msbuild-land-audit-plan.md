# CARD-1483: ask real MSBuild in the land audit

Date: 2026-10-22. Plan stage, task f7dd9618. Verification design is folded in; there is no separate TestDesign stage.

Baseline: `origin/master` = `08765c68cdd05f4c80be092e06d39abe704c814e` (this branch's base). CARD-1409 is **not landed**: its script is on `feat/card-task-18ac0f5a` at `f8f3233f3cddce69cb4783e0de73565de7a5108d` (1,664 lines), and Final Review d43d1e4a left R2 open. Master's script is the 616-line CARD-1400 version. Card: CARD-1483, platform Any. This plan pins no runner and no platform. The checkpoint lane is `tests/Antiphon.Tests` on the default runner. Read `GET /api/runner-defaults` and `GET /api/session-runners` at dispatch. Every row runs on Linux. No row needs Windows (D-11).

## Problem

`scripts/audit-land-verification-graph.ps1 -Update` decides whether the tracked dotnet-build definition files can be pinned. A pinned digest lets a docs-only land skip its verification build. The script approves only when no definition file can select `docs/**/*.md` or the root `README.md` into the build. It re-implements MSBuild's evaluator: imports, `$()`/`@()`/`%()` expansion, split, unescape, path normalisation and globbing. Seven repair rounds have each found another evaluator corner. The latest is CARD-1483 itself: well-known item metadata read through a batched `%(Item.RelativeDir)`/`%(Item.Identity)` inside a Target is treated as empty. The operator has ended the whack-a-mole. This plan replaces the emulation as the authority with real MSBuild evaluation and keeps a small, closed, human-pinned static scan for what evaluation cannot see.

## Ground truth

| # | Card / brief assumes | Code / measurement says | Source |
|---|---|---|---|
| G-1 | The audit's cost is paid "per land". | A land never evaluates MSBuild. It hashes the tracked definition files at the candidate (`git ls-tree`) and compares the result with the embedded `LandVerificationBuildGraph.manifest.txt`. The audit runs only at `-Update` (an author or Code task, after a definition-file change) and inside `C1385_RegistryMatchesNarrowGraph` (`-Check` on the real tree). Evaluation therefore adds **zero** cost to a land, and the manifest digest is already the cache key. | `server/Application/Services/LandVerificationBuildGraph.cs`; `docs/orchestration-loop.md` "No MSBuild evaluation is run to decide a skip" |
| G-2 | The bypass is at `audit-land-verification-graph.ps1:700`. | That line exists only on the unlanded CARD-1409 branch `18ac0f5a`. The master script has no `Expand-Candidates`. A fix to "the script" has to say which script. | `git show origin/master:scripts/...` (616 lines) vs `origin/feat/card-task-18ac0f5a` (1,664 lines) |
| G-3 | `dotnet msbuild -getItem` evaluates without restore. | True. P1: exit 0. P3, measured again here on loaded server2: 46 tracked `*.csproj`, evaluated one CLI process each with `-getItem:None/EmbeddedResource/Content/Compile/AdditionalFiles`, took **78.1 s** with 0 failures and created **0** `obj` directories. `-preprocess` of `Antiphon.Tests.csproj` took 2.5 s. | probe `t.sh`, build-slot lease 25af22ce |
| G-4 | Evaluation sees target-time items. | It does not. P2a (evaluation only) returned `None: []` for both card witnesses. P2b (target executed) returned `<repo>/docs/data.json` and `<repo>/README.md`. Target bodies need a separate rule. | prior Plan 668ab510 probes P2a/P2b |
| G-5 | `-getItem` shows what a glob *could* select. | It shows only what the glob selects **today**. A glob such as `../../docs/new/*.md` that matches no file today returns nothing, and a later docs-only land that adds `docs/new/x.md` is then selected. Real evaluation needs the unexpanded glob as well. The MSBuild API's `Project.GetAllGlobs()` returns it, and the CLI cannot. | reasoning; S2 witness proves it |
| G-6 | Real evaluation of the repo is clean. | It is not empty. `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` evaluate `EmbeddedResource docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json`, which is allowed because docs JSON is not skippable. `docs/investigations/evidence/card-0462/Reproduce.csproj` evaluates SDK default items under `docs/`, including a `**/*` None glob rooted inside `docs/`. That project is **not** in `Antiphon.sln`. | p3 JSON |
| G-7 | The land builds "the repo". | It runs `dotnet build --artifacts-path <tmp> --configuration Debug [-maxcpucount:N]` at the worktree root, which builds the single root `Antiphon.sln`: 35 of the 46 tracked `*.csproj`. | `AgentTaskLandService.cs:1884` |
| G-8 | Target bodies that reference paths are rare. | There are 12 `<Target>` elements in 8 tracked files. The 4 OS-dependent conditions (`'$(OS)' != 'Windows_NT'`) are all on Targets. File-probing constructs (`Exists(`, `System.IO.`, `...FileAbove`) occur 7 times in 5 files. | grep over the tracked definition files at `f8f3233f3` |
| G-9 | The C1409 suite is 83 tests. | 83 executed = 27 `C1409_*` + 10 `C1400_*` + 3 `C1390_*` + 1 `C1385_RegistryMatchesNarrowGraph` + 42 `C1380_*` cases (13 methods with `[Arguments]`). That count holds on the 1409 branch only. Master has the C1400 set. | `LandNonCodeSkipTests.cs` on `18ac0f5a` |
| G-10 | The manifest can carry more facts. | `LandVerificationBuildGraph.ParseManifest` reads two lines: digest and count. Any new verdict record belongs in a separate file so the land parser stays untouched. | `LandVerificationBuildGraph.cs:85` |

## Decisions

**D-1: Use the MSBuild API in a small tool, not the `dotnet msbuild` CLI.** Add `tools/Antiphon.LandGraphAudit` (net10.0 console; `Microsoft.Build` with `ExcludeAssets="runtime"` plus `Microsoft.Build.Locator`, matching the SDK pinned by `global.json`). One process evaluates every project. The API exposes `Project.GetAllGlobs()` (G-5), `Project.Imports` (the true import closure), `ProjectProperty.IsEnvironmentProperty`, and an `EvaluationContext` with a custom `MSBuildFileSystemBase` (D-6). The CLI was rejected: it costs about 1.7 s per project (G-3), it cannot show unexpanded globs or imports, and parsing its JSON is another emulation surface. S1 first runs a spike assertion that these API members exist on the pinned SDK, so a wrong API assumption shows up before any other code is written.

**D-2: Evaluate what the land builds.** Evaluate `Antiphon.sln` with global properties `Configuration=Debug`, `ArtifactsPath=<scratch>` (G-7) and no restore. Each solution project is evaluated, then its `ProjectReference` closure (evaluated items, not text). A tracked `*proj` outside that closure, such as `Reproduce.csproj`, is listed as `not-built` and is not refused. A non-solution project the land does not build cannot change the build. If the land ever builds another entry point, `G-7` and this decision change first.

**D-3: Evaluation refuses on ground-truth item paths.** For every evaluated item of **every** item type, compute `FullPath` (the API's `GetMetadataValue("FullPath")`). Refuse when it is in the skippable set: a tracked or untracked `docs/**/*.md`, or the root `README.md`, compared case-insensitively after `Path.GetFullPath`. Other `docs/` files, such as the G-6 JSON, are allowed, because the land policy never skips them. Every rule about escape, metadata, split and normalisation now comes from MSBuild itself.

**D-4: Globs refuse on reachability, not on today's matches.** For each `GlobResult` from `GetAllGlobs()`, take `MSBuildGlob.FixedDirectoryPart` (F), `WildcardDirectoryPart` (W) and `FilenamePart` (N). Refuse when either condition holds:
- F is `<repo>/docs` or below it, or F is a proper ancestor of `<repo>/docs` and W is non-empty; and N could name a `.md` file. N could name a `.md` file if it ends in `.md` ignoring case, or if its final extension segment contains `*` or `?`, or if it has no literal extension.
- F is `<repo>` or an ancestor, and N could name `README.md` by the same rule.

Excludes are **ignored** (default-deny). Neither rule tests a probe path with `IsMatch`; both read the decomposed parts. This rule applies only to globs owned by projects in the D-2 build set.

**D-5: Targets and OS-conditioned elements are a closed, human-pinned set.** Evaluation cannot see Target bodies (G-4) or the other branch of an `$(OS)` condition. `server/Application/Services/LandVerificationBuildGraph.pins.txt` lists one row per pinned element: `<repo-relative file>\t<Target name or element xpath>\t<SHA-256 of the element's OuterXml, whitespace-normalised>`. Every `<Target>` in a tracked definition file, and every non-Target element whose `Condition` contains `$(OS)`, `IsOSPlatform` or `RuntimeInformation`, must match a row. A missing row, a changed hash or a stale row refuses by name. The scan never interprets the body. The two card witnesses refuse because their Targets are unpinned, not because a path heuristic catches them. A pin row is approved by Review of the pins diff. The pins file is not a definition file, but any change to it is non-docs, so that land builds. Today's 12 Targets are pinned in S3 after the S3 Code task reads each one.

**D-6: File probes are checked by evaluating twice and recording.** Two independent checks:
- (a) A recording `MSBuildFileSystemBase` refuses any `FileExists`, `DirectoryExists`, enumeration or read during evaluation that touches a skippable path, `<repo>/docs` or any directory under it, or `<repo>/README.md`. This covers `Exists(...)` conditions, globs and SDK lookups.
- (b) The audit evaluates a second time on a scratch copy where every skippable file's bytes are replaced with a canary, `README.md` is deleted and `docs/c1483-canary/canary.md` is added. Any difference in evaluated properties (excluding the few reserved, path-of-scratch ones), item `FullPath` lists or the import list refuses. This covers property functions that read files directly, such as `$([System.IO.File]::ReadAllText(...))`, which may bypass the evaluation file system.

S4 records which of (a) and (b) catches each witness. Neither is removed if the other appears to cover it.

**D-7: Imports are taken from evaluation.** Every entry in `Project.Imports`, plus `ImportsIncludingDuplicates`, must resolve to one of three places: a tracked definition file, a path under the SDK directory resolved by the Locator, or the NuGet package root. Anything else refuses, including an untracked file, a file under `docs/`, or a non-definition extension. An `ImportElement.Project` text containing `*` or `?` refuses: a wildcard can match a future file, as in CARD-1409 D-8.

**D-8: Evaluation failure defaults to deny.** An exception, a load error, a missing import, an unresolved SDK, a Locator failure, a timeout (120 s per solution evaluation, 300 s for the tool) or any nonzero tool exit makes `-Update` exit 1 without writing either file. Tool exit codes are 0 approved, 1 refused (with a message naming the file and the item, glob, target or probe), and 2 evaluation failure. The script treats every exit code other than 0 as a refusal. The land is unaffected: an unwritten manifest leaves a digest mismatch, so the land builds.

**D-9: The verdict file binds the evaluation.** `-Update` writes `server/Application/Services/LandVerificationBuildGraph.evaluation.txt` with three lines: the same digest, the SDK version from `global.json` resolution, and the pins file's blob SHA. `-Check` fails when that file is missing or disagrees. The C# manifest parser is unchanged (G-10). The land does not read this file. The file exists so that Review and `C1385_RegistryMatchesNarrowGraph` can see that the pinned digest was approved by evaluation, not only by the emulation.

**D-10: Migration keeps the emulation as a fast pre-check.** The order is S1 to S4 (tool and tests, with no change to the script), then S5 (the script runs the emulation first and then the tool, and both must approve), then S6 (wording). The emulation is never the sole approver after S5. Its known and future bypasses stop being defects, because evaluation is the authority. **No further repair rounds on the emulation.** Its false refusals are acceptable under default-deny. Deleting it is operator decision O-2, which is not part of this plan.

**D-11: The evaluation platform is Linux, and Windows is not needed.** The land host may be Windows. OS-dependent elements are pinned under D-5, so the Linux evaluation plus the pin set covers both branches. Windows PowerShell 5.1 only needs to start `dotnet <tool.dll>`. The script stays ASCII. No PowerShell 7 feature is added.

**D-12: No restore.** Evaluation runs without `obj/*.nuget.g.props` (G-3), so package-contributed `build/*.props` are not evaluated. That is accepted residual **R-1**. Package identities and versions are definition-file content and are already digested, so any change forces a build, and a package's props can only add items relative to its own folder or to a project directory. Restore was rejected because it needs the network on the `-Update` host and costs time. Operator O-1 may reverse this.

**D-13: The interim cheap fix belongs to CARD-1409, not here.** The card's suggestion is to refuse any `%(Item.WellKnown)` reference inside a Target property. It edits the 1409-branch script, which this plan does not touch, and D-5 subsumes it. Whether CARD-1409 lands first is O-3.

**D-14: The test fate.** The 83 tests (G-9), on whichever branch carries them, stay as regression for the pre-check and pass unchanged through S5. The new class `LandGraphEvaluationTests` holds the evaluation witnesses. S5 reuses the card's two witnesses and the three Review witnesses that real MSBuild proved, from 8e8726fd F1 and d43d1e4a R2, as fixtures that the tool must refuse. Removing emulation tests happens only with O-2.

## Operator decisions (defaults stated; plan proceeds under them)

- **O-1, restore:** the default is no restore, with residual R-1 accepted. The alternative is `dotnet restore --locked-mode` in the scratch copy before evaluation, at a cost of network access and about 1 to 3 minutes per `-Update`.
- **O-2, emulation retirement:** the default keeps it as the pre-check indefinitely and does no more repair rounds. The alternative is a later card that deletes `Assert-NoDocumentationInput` and the C1400/C1409 emulation tests after one release of evaluation-authoritative `-Update`.
- **O-3, CARD-1409:** the default closes CARD-1409's remaining repair loop (R2 open) and lands its branch only if the operator wants the stronger pre-check. CARD-1483 S1 to S4 do not depend on it. S5 adapts to whichever script is on master when S5 starts.
- **O-4, rc:** the default is master only. The docs-only skip itself (CARD-1380/1385) is master-first, and RC adoption needs the operator's go (`docs/orchestration-loop.md`). Port to rc only together with the skip policy, as one reviewed rc cherry-pick of tool, script and pins, after master activation is observed.
- **Residuals accepted under the defaults:** R-1, package props not evaluated (D-12). R-2, a projects-not-in-solution change, since `Reproduce.csproj` is not built (D-2). R-3, task parameters inside pinned Targets, such as `Copy` and `Exec`, are trusted at pin-review time (D-5).

## Slices

Each slice is one Code task of 30 to 60 minutes. Each one commits its red test before the fix and pushes after every commit.

### S1: tool skeleton and evaluated-item refusal (D-1, D-2, D-3, D-8)
- New `tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj` and `Program.cs`. Arguments: `--repo <dir> --solution Antiphon.sln`. It prints `approved`, `refused: <file> <itemType> <FullPath>` or `evaluation-failed: <message>` and exits 0, 1 or 2. Add it to `Antiphon.sln` only if Review asks. The default is **not** in the sln, so the tool does not change the land's own build graph.
- Tests: `tests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs`. Each test builds a temp git repo fixture with `global.json` copied from the repo, a tiny `Antiphon.sln` and SDK projects, then runs `dotnet <tool.dll>` under `ParallelLimiter<ProcessSpawnLimit>`. The tool dll reaches the test output through a `ProjectReference` with `ReferenceOutputAssembly="false"` and a copy target in `Antiphon.Tests.csproj`, following the `CopyOwnedPtyHostAppHost` pattern. Code picks the exact mechanism.
- Red witness first: the tests are committed before the tool exists, so they fail on a missing dll or an assertion.

### S2: glob reachability, solution closure and imports (D-4, D-2, D-7)
- `GetAllGlobs` rule, ProjectReference closure, `not-built` listing, import resolution rule.
- Red: an S1 tool approves a `None Include="../docs/new/*.md"` that matches nothing today.

### S3: Target and OS pin set (D-5)
- `LandVerificationBuildGraph.pins.txt` plus the scan, with whitespace-normalised `OuterXml` SHA-256. Pin the 12 real Targets after reading each one; the report lists them.
- Red: the S2 tool approves card witness 1 and witness 2.

### S4: file-probe recording and differential evaluation (D-6)
- Recording file system plus a second evaluation on the canary copy.
- Red: the S3 tool approves `<PropertyGroup Condition="Exists('$(MSBuildThisFileDirectory)../docs/flag.md')"><X>y</X></PropertyGroup>` with an item conditioned on `X`, and approves `<P>$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)../README.md'))</P>` feeding an item.

### S5: wire into the audit script (D-8, D-9, D-10, D-14)
- `scripts/audit-land-verification-graph.ps1 -Update` runs the existing checks first, then `dotnet run --project tools/Antiphon.LandGraphAudit -c Release -- --repo <root> --solution Antiphon.sln` (or the built dll), and writes the evaluation file only on exit 0. `-Check` verifies the evaluation file.
- Commit the regenerated real-tree manifest (digest and count unchanged) and the evaluation file.
- Red: a `C1483_UpdateRefusesWhenEvaluationRefuses` fixture that the current script approves (card witness 1 in a fixture repo).

### S6: wording
- `docs/orchestration-loop.md`: replace "No MSBuild evaluation is run to decide a skip" with this: the land still runs no evaluation, `-Update` is evaluation-authoritative, and the emulation is a pre-check. Also cover the pins review rule and R-1 to R-3.
- Update the script header and `docs/testing-and-build.md` if the tool needs a build-slot note.
- No tests.

## Verification design

| ID | Behaviour | Witness (all in `LandGraphEvaluationTests`) |
|---|---|---|
| V-1 | A `None Include="../docs/guide.md"` and an `EmbeddedResource Include="../README.md"` in a solution project refuse and name the file and FullPath. | `C1483_S1_EvaluatedSkippableItemRefuses` |
| V-2 | A harmless fixture (Compile `**/*.cs`, an EmbeddedResource of `docs/x.json`) approves, exit 0. | `C1483_S1_HarmlessFixtureApproves` |
| V-3 | A missing `Import`, a bad SDK name or malformed XML gives exit 2 `evaluation-failed`. | `C1483_S1_EvaluationFailureDenies` |
| V-4 | The `Microsoft.Build` members named in D-1 resolve on the pinned SDK. This is a spike assertion. | `C1483_S1_ApiSurfaceAvailable` |
| V-5 | `../docs/new/*.md` with no match today refuses; `../docs/**/*.json` approves; `../*` from a root-adjacent project refuses (README). | `C1483_S2_GlobReachabilityRefuses` |
| V-6 | A docs-tracked project outside the sln with default items is `not-built` and approved. Reached only by `ProjectReference`, it refuses. | `C1483_S2_SolutionClosureScopesProjects` |
| V-7 | `Import` of an untracked file, of a `docs/*.md` file and of a wildcard each refuse. | `C1483_S2_ImportOutsideTrackedSetRefuses` |
| V-8 | Card witness 1 (`%(_StagedHelperFiles.RelativeDir)`) and witness 2 (`%(_AntiphonRepeatLine.Identity)`) refuse as an unpinned Target. | `C1483_S3_CardWitnessesRefuseAsUnpinnedTargets` |
| V-9 | A pinned Target approves; a whitespace-only change approves; a body change refuses; a stale pin row refuses. | `C1483_S3_PinnedTargetMatchesOnlyItsHash` |
| V-10 | A non-Target element with `Condition="'$(OS)' == 'Windows_NT'"` refuses unless it is pinned. | `C1483_S3_OsConditionedElementNeedsPin` |
| V-11 | An `Exists('../docs/flag.md')` condition refuses by recording (a). | `C1483_S4_ExistsProbeOfDocsRefuses` |
| V-12 | `ReadAllText` of README into a property refuses by differential (b), including the `%`-escaped spelling. | `C1483_S4_FileReadingPropertyFunctionRefuses` |
| V-13 | The real tree at S4's tip: the tool approves and lists the `not-built` projects. | `C1483_S4_RealRepositoryApproves` |
| V-14 | `-Update` refuses card witness 1 and the d43d1e4a R2 spelling, which the current script approves, and writes neither file. | `C1483_S5_UpdateRefusesWhenEvaluationRefuses` |
| V-15 | `-Check` fails with the evaluation file missing or with a different pins SHA, and passes on the committed real tree. | `C1483_S5_CheckRequiresEvaluationRecord` |
| V-16 | The pre-check regression (G-9) still holds. | the existing 83 (or master's C1400 set) |

No R-n rows. Positive controls for SourceLanding Mutation: delete the pins comparison (V-8 and V-9 go red), skip the differential (V-12 goes red), and make the tool's exit 2 count as approval (V-3 and V-14 go red).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1483-s1/` | c1483-s1 | `/*/*/LandGraphEvaluationTests/(C1483_S1_*)` | V-1 V-2 V-3 V-4 | exactly 4 executed, 0 failed/skipped | 4 | 15 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1483-s2/` | c1483-s2 | `/*/*/LandGraphEvaluationTests/(C1483_S1_*)\|(C1483_S2_*)` | V-1..V-7 | exactly 7 executed, 0 failed/skipped | 7 | 15 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1483-s3/` | c1483-s3 | `/*/*/LandGraphEvaluationTests/(C1483_S1_*)\|(C1483_S2_*)\|(C1483_S3_*)` | V-1..V-10 | exactly 10 executed, 0 failed/skipped | 10 | 15 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1483-s4/` | c1483-s4 | `/*/*/LandGraphEvaluationTests/(C1483_*)` | V-1..V-13 | exactly 13 executed, 0 failed/skipped | 13 | 18 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1483-s5/` | c1483-s5 | `/*/*/(LandGraphEvaluationTests*)\|(LandNonCodeSkipTests*)/(C1483_*)\|(C1409_*)\|(C1400_*)\|(C1390_*)\|(C1385_RegistryMatchesNarrowGraph*)\|(C1380_*)` | V-1..V-16 | exactly 15 + the pre-check count executed (98 if CARD-1409 is on master, otherwise 15 + master's count, which the S5 Code task states before running), 0 failed/skipped | 15 | 25 |

Each row's red witness is committed and run (unlisted, stated) before its fix. Each row has its own build output (`bin-c1483-s<n>`) because the tool refuses build reuse across `After` groups. One run per committed slice group goes through `dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this plan>`. S6 has no row.

### Cost

The rows' floor is about 88 minutes, dominated by the Antiphon.Tests build (8 to 10 minutes) and the C1380 harness in CP-5. Each fixture evaluation is expected at 2 to 5 s in-process, against 1.7 s per project with the CLI (G-3). V-13 and V-15 evaluate the real sln, which should take under 60 s. Per-land cost is zero (G-1). Per-`-Update` cost is one tool run, about 1 to 2 minutes on server2.

## Checklist (sessions that wait for input)

No session that waits for input is added or changed.
