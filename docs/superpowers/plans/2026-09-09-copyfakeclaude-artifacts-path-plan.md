# CopyFakeClaude artifacts-path compatibility plan

Date: 2026-09-09. Planning task: `7f0751e8`. Complexity: easy; TestDesign is folded into this plan.
No owning card number was supplied; CARD-0468 is unrelated and excluded.

The implementation will resolve each fake executable project's own output directory before copying its complete output into either test project's staging directory. This fixes the landing verifier's absolute artifacts layout while preserving normal and relative alternate-output builds.

## Ground truth

Inspected checkout: `9157a181415213c616aa3a586cfdcefceb7ea6eb`.

| Brief assumption | Current code / evidence | Consequence |
| --- | --- | --- |
| CopyFakeClaude constructs an invalid path under `--artifacts-path`. | Both test projects prepend `..\..\src\Antiphon.FakeClaude\` to their own `$(OutputPath)` at lines 58 and 49 respectively. The prior investigation, task `964f5422`, confirmed MSB3030. | Replace the source-directory derivation in both targets. |
| Only FakeClaude is named in the failure. | Each target also has an identical FakeGrok glob and copy task. | Fix all four source globs in the two project files. |
| An absolute-path conversion might suffice. | SDK evaluation with `ArtifactsPath=<temp>` gives separate `bin\Antiphon.Tests\debug\`, `bin\Antiphon.Agents.Pty.Tests\debug\`, `bin\Antiphon.FakeClaude\debug\`, and `bin\Antiphon.FakeGrok\debug\` output directories. | The consuming project's OutputPath is the wrong source even after normalization. Query the producer. |
| Landing uses an isolated build. | `AgentTaskLandService.VerifyWithObserverAsync`, lines 489-493, creates a unique external `antiphon-land-verify-<guid>` directory and executes exactly `dotnet build --artifacts-path <directory>` from the worktree root. | Keep the verifier unchanged and run that exact command as acceptance. |
| The fake output is already built. | Both consumers have ProjectReferences to both fakes; `CopyFakeClaude` runs `AfterTargets="Build"`. Antiphon.Tests sets `ReferenceOutputAssembly="false"`; Pty.Tests also needs assembly references. | Retain reference metadata and target timing. Assembly-resolution items alone are insufficient for both consumers. |
| Successful build alone proves staging. | `FakeClaudeContractTests.SkipIfUnavailable` skips when `fakeclaude\fakeclaude.exe` is absent. Existing comments document an earlier stale/empty-copy regression with alternate outputs. | Require explicit file presence and content comparison; skipped tests cannot establish success. |

Planning probes completed successfully: four project-property evaluations under artifacts output, three `GetTargetPath` queries for FakeClaude (normal, relative alternate output, and artifacts), and one artifacts query with explicit Configuration/TargetFramework matching the proposed MSBuild task. All eight succeeded; no solution builds or tests were run in Plan. The resolved local SDK was 10.0.300, allowed by `global.json`'s 10.0.204 / `latestMinor` policy.

The observed project separation agrees with the SDK's documented [artifacts output layout](https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output). MSBuild documents [GetTargetPath as the build-product query](https://learn.microsoft.com/en-us/visualstudio/msbuild/build-process-overview) and the [MSBuild task's TargetOutputs result](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-task).

## Decisions

- D-1: Query `GetTargetPath` separately on the FakeClaude and FakeGrok project files using the MSBuild task inside each existing CopyFakeClaude target. Anchor project-file paths at `$(MSBuildThisFileDirectory)`. Capture separate output items and derive each source directory from the returned absolute path's `RootDir` and `Directory` metadata. This asks the producer to resolve its own output layout.
- D-2: Pass the current Configuration and TargetFramework to the query. Preserve inherited command-line global properties, including ArtifactsPath and explicitly supplied OutputPath. Do not pass the consumer's evaluated OutputPath, OutDir, or TargetDir as an additional property: SDK-computed paths are project-specific. These four projects currently target net9.0 only.
- D-3: Keep the existing recursive globs, destination transforms, `SkipUnchangedFiles="true"`, build ordering, and ProjectReference assembly semantics. Update the obsolete comments in both files to explain producer-path resolution and both supported output workflows.
- D-4: Limit implementation to the two csproj targets and comments. Their small matching changes do not warrant a new shared build abstraction, fake-project target, runtime change, package update, or new test framework. Verification is direct build execution plus staging inspection.
- D-5: Treat the confirmed prior investigation as baseline failure evidence. Execute the acceptance matrix after implementation; do not spend another full solution build re-proving the known failure during Plan. No user decision is outstanding.

Rejected alternatives:

- Prefixing MSBuildThisFileDirectory onto the current source expression or calling GetFullPath on the concatenation: neither repairs an embedded drive root nor selects the correct artifacts project directory.
- Branching on `IsPathRooted($(OutputPath))` and using that path directly when absolute: selects the test project's directory under artifacts output.
- Hardcoding `bin\$(Configuration)\net9.0` or reconstructing artifacts project/pivot names: reintroduces alternate-output staleness or duplicates SDK layout rules.
- Using only resolved assembly-reference paths: Antiphon.Tests deliberately does not reference either fake assembly.
- Changing the landing verifier back to shared bin/obj paths or suppressing Copy errors: defeats output isolation or leaves unusable test staging.

## Implementation slice

S-1 is one Code dispatch, with verification folded in.

Files:

- `tests/Antiphon.Tests/Antiphon.Tests.csproj`, target CopyFakeClaude.
- `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj`, target CopyFakeClaude.

For each fake, use this shape before the existing source ItemGroup; repeat for FakeGrok with distinct item/property names:

```xml
<MSBuild Projects="$(MSBuildThisFileDirectory)..\..\src\Antiphon.FakeClaude\Antiphon.FakeClaude.csproj"
         Targets="GetTargetPath"
         Properties="Configuration=$(Configuration);TargetFramework=$(TargetFramework)">
  <Output TaskParameter="TargetOutputs" ItemName="_FakeClaudeTargetPath" />
</MSBuild>
<PropertyGroup>
  <_FakeClaudeOutputDir>@(_FakeClaudeTargetPath->'%(RootDir)%(Directory)')</_FakeClaudeOutputDir>
</PropertyGroup>
```

Each single-target fake returns one item. Use the resulting scalar directory in the existing glob:

```xml
<FakeClaudeOutput Include="$(_FakeClaudeOutputDir)**\*" />
<FakeGrokOutput Include="$(_FakeGrokOutputDir)**\*" />
```

The path query does not replace or re-run the referenced Build. Keep the existing ProjectReferences as the prerequisite that produces the files. Do not add a fallback to another output directory when the expected source is absent.

Commit the implementation before acceptance builds and report the tested SHA. No source edits while a build is in flight. No changes to AgentTaskLandService, landing replay, stack processes, or ports are required.

## Verification design

### Proves it works now

Run sequentially in the implementation worktree, which owns its default bin/obj outputs. Retain external artifacts and logs. Do not restart or build in the canonical running checkout.

| ID | Behaviour / layer | Command or probe | Required result |
| --- | --- | --- | --- |
| V-1 | Landing build / build integration | Exact verifier command below, with a fresh external temp directory. | Exit 0, 0 build errors, no MSB3030. Run staging inspection for artifacts mode. |
| V-2 | Normal build / build integration | Plain `dotnet build` from the same worktree, with no output override. | Exit 0, 0 build errors. Run staging inspection for normal mode. |
| V-3 | Relative alternate output / build integration | Build each affected test project sequentially with the same unique relative `OutputPath=bin-copyfake-<guid>/`. | Both commands exit 0 with 0 errors. Run staging inspection for relative mode; all sources must be from that override. |
| V-4 | Complete, current staging / filesystem integration | Inspect both fake bundles in both consumers immediately after each mode using the procedure below. | Twelve successful bundle comparisons across three modes; all required files exist and every producer file matches its staged SHA256 at the same relative path. |

PowerShell commands (record native exit code immediately after each invocation):

```powershell
$copyFakeRunId = [Guid]::NewGuid().ToString('N')
$copyFakeArtifacts = Join-Path ([IO.Path]::GetTempPath()) ("antiphon-land-verify-" + $copyFakeRunId)
$copyFakeLogs = Join-Path ([IO.Path]::GetTempPath()) ("antiphon-copyfake-logs-" + $copyFakeRunId)
$copyFakeRelative = "bin-copyfake-$copyFakeRunId/"
New-Item -ItemType Directory -Path $copyFakeArtifacts, $copyFakeLogs | Out-Null

# V-1: same working directory and argv as AgentTaskLandService.
dotnet build --artifacts-path $copyFakeArtifacts 2>&1 | Tee-Object (Join-Path $copyFakeLogs 'artifacts.log')
if ($LASTEXITCODE -ne 0) { throw "V-1 failed: $LASTEXITCODE" }
# Run V-4 inspection for artifacts mode here.

# V-2
dotnet build 2>&1 | Tee-Object (Join-Path $copyFakeLogs 'normal.log')
if ($LASTEXITCODE -ne 0) { throw "V-2 failed: $LASTEXITCODE" }
# Run V-4 inspection for normal mode here.

# V-3
foreach ($copyFakeConsumer in @('Antiphon.Tests', 'Antiphon.Agents.Pty.Tests')) {
    dotnet build "tests/$copyFakeConsumer/$copyFakeConsumer.csproj" "--property:OutputPath=$copyFakeRelative" 2>&1 |
        Tee-Object (Join-Path $copyFakeLogs "relative-$copyFakeConsumer.log")
    if ($LASTEXITCODE -ne 0) { throw "V-3 $copyFakeConsumer failed: $LASTEXITCODE" }
}
# Run V-4 inspection for relative mode here.
```

V-4 inspection procedure, applied after each mode:

1. Query TargetDir independently for all four projects with `dotnet msbuild <project.csproj> -nologo -getProperty:TargetDir`. Supply `-p:ArtifactsPath=<the V-1 directory>` for artifacts mode, `-p:OutputPath=<the V-3 relative value>` for relative mode, and no property override for normal mode. Require query exit 0 and an existing directory.
2. For each consumer TargetDir, inspect `fakeclaude\` against FakeClaude's queried TargetDir and `fakegrok\` against FakeGrok's. Source and destination directories must be different; artifacts sources must name their respective fake projects rather than either consumer. Relative-mode sources must contain the unique relative output directory from V-3.
3. Require `fakeclaude.exe`, `fakeclaude.dll`, `fakeclaude.runtimeconfig.json`, and `fakeclaude.deps.json` in both producer and staged bundle, and the corresponding four `fakegrok` files. Missing files fail verification; never treat absence as a skip.
4. Enumerate every producer file recursively with `Get-ChildItem -LiteralPath <producer> -File -Recurse`. Use `[IO.Path]::GetRelativePath(<producer>, <file>)` to locate the staged counterpart and compare `Get-FileHash -Algorithm SHA256` values. Require a nonempty inventory and a match for every file, including nested paths and any PDBs. Record the source/destination paths and number of matched files for each of the four bundles per mode.

### Guards the regression

- R-1: Reintroducing consumer OutputPath concatenation is caught by V-1's native exit-code/error check and artifacts-mode V-4's producer-directory and bundle comparisons. Retain the landing verifier's current build command as the recurring build gate.
- R-2: Hardcoded default-bin copying or a source glob that silently matches nothing is caught by V-3's fresh unique relative output plus V-4's mandatory files and producer/staged hashes. V-1 also uses a fresh artifacts directory, so earlier staged files cannot mask a missing copy.
- R-3: Fixing only one consumer or only FakeClaude is caught by requiring all four consumer/fake combinations in every V-4 mode. Dropping runtime/deps or recursive contents is caught by the same full source inventory.

These are build/staging acceptance checks; no new TUnit tests or source-text assertions are required. Report every V-n and R-n with evidence, including build error/warning counts, command exit codes, paths, and bundle file counts. An unrelated solution-build failure must be named and leaves that acceptance row failed; targeted success alone does not satisfy V-1 or V-2.

### Positive controls

Not applicable: this slice adds no safety-critical assertion or guard requiring a PC-n mutation. The prior MSB3030 investigation is baseline defect evidence, not a claimed TUnit positive control. Do not run a full test assembly or create a mutation-test harness for these two build targets.

### Out of scope

- Full Antiphon.Tests / Pty.Tests execution, real-provider tests, and E2E: runtime behaviour is unchanged; actual build and complete-file staging checks exercise the defect directly and cannot silently skip a missing executable.
- Release, multi-targeting, and publish: these are not required acceptance configurations for this net9.0 build-target change. V-1 explicitly exercises absolute OutputPath through the landing verifier's artifacts layout.
- Landing execution, publication to master, stack restart/deploy, and CARD-0468: later orchestration owns those actions. The normal landing operation may still be blocked until the code fix is included in its verification checkout; this plan itself does not repair the build.
- Recursive output cleanup: retain outputs and report their exact locations; do not delete pre-existing directories.

### Cost

Suites forced: none. Four build commands (two solution builds and two targeted builds), plus twelve bundle comparisons and lightweight SDK property queries. Estimated verification floor about 5 minutes with a warm restore cache; reserve about 15 minutes depending on host load. These timings are estimates, not Plan measurements.

Implementation is ready for Code. No separate TestDesign or Review dispatch is required unless Code discovers an unresolved issue.

If the known build break prevents a separate Plan landing, the Code dispatch can read this document from `C:\Antiphon\worktrees\card-task-7f0751e8\docs\superpowers\plans\2026-09-09-copyfakeclaude-artifacts-path-plan.md` and carry the same plan document in its implementation branch. The normal landing verifier can then verify code and plan together. Preserve this Plan branch until incorporation is confirmed; do not disable verification or push directly to master to land the plan alone.
