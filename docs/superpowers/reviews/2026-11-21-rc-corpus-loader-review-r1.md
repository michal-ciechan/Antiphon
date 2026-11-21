# CARD-1836 RC corpus loader, Final Review round 1

Subject task: `592e48bc-022c-406e-b267-28f476f37a20` (Debug, Codex).
Reviewed tip: `14057e5ed305df85a7239ef8657b9b4bdbe2f8d0` on `feat/card-task-592e48bc`.
Repair range: `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0..14057e5ed305df85a7239ef8657b9b4bdbe2f8d0`.
Commits: `9eb8fb3c8dd69d366328003b913e06597c2fe6fe` (red witnesses), `1d57b6b1dfb7ca443a9b3557e2af9de509afd796` (resolver and Windows package), `14057e5ed305df85a7239ef8657b9b4bdbe2f8d0` (TUnit `Assembly` alias).
`origin/master` at review: `b5690352bb0a4467542cce83bb833820477eff5a`.
Verdict: **Clean**. No product defect. Land must refresh the land graph and regenerate the derived impact index. Windows execution of the two named corpus methods stays UNOBSERVED. Positive controls stay pending. Shadow mode: no activation and no gate credit.

## What the repair does

`RcPortCorpus` no longer calls `Assembly.LoadFrom` on the hard-coded path `/opt/powershell/System.Management.Automation.dll`. `RcPortPowerShellAssembly.Load` discovers the assembly.

On Linux the probe order is `PSHOME`, then each `PATH` directory that contains `pwsh` followed by that executable's symlink target, then `/opt/microsoft/powershell/7`, then `/opt/powershell`. A miss throws `InvalidOperationException` whose message names the RC port corpus and lists every probed DLL path.

On Windows, `Load` tries `Assembly.Load("System.Management.Automation")` first and only then the same resolver. The resolver looks for `pwsh.exe`, not `powershell.exe`, and does not add the Linux directories. `Program Files\PowerShell\7` and the MSIX WindowsApps path are used only when `PSHOME` or a `PATH` entry points at them.

The test project reference is Windows-only:

```xml
<PackageReference Include="System.Management.Automation" Version="7.5.4"
                  Condition="'$(OS)' == 'Windows_NT'" PrivateAssets="all" />
```

`PrivateAssets="all"` stops the package flowing to a parent project. It does not set `IncludeAssets="compile"`, so on Windows the test host still gets the runtime asset and `Assembly.Load` binds it through the dependency context. There is no central package management and no `packages.lock.json`. The package license is MIT, `requireLicenseAcceptance` is false, and the repo has no `NuGet.Config`, so restore uses the default nuget.org feed. The net9.0 closure is 19 packages. The test project already references `System.Management` 9.0.21 and `System.Security.Cryptography.ProtectedData` 9.0.21, both above the package's 9.0.10 minimums, so those two are not NuGet downgrades.

`scripts/lib/Antiphon.Nightly/Antiphon.Nightly.csproj` is unchanged. It still references `PowerShellStandard.Library` 5.1.1 with `IncludeAssets="compile"` and `PrivateAssets="all"`. Its restored assets file has a compile DLL and a runtime `_._` placeholder. The built nightly output directory contains no `System.Management.Automation.dll`.

## Windows path the tests take

The four resolver tests call `Resolve` with injected files. They do not call `Load`. On Windows the tests therefore take the package path, not an installed pwsh:

1. `Load` calls `Assembly.Load("System.Management.Automation")`.
2. A .NET 9 test host does not probe the GAC, so Windows PowerShell 5.1's `System.Management.Automation` 3.0.0.0 is not selected by that call.
3. The Windows-only package reference puts 7.5.4, which targets net9.0, into the test dependency context. That matches the test TFM and the runner's pwsh 7.5.4 (`AssemblyVersion` 7.5.0.500, public key token `31bf3856ad364e35` on `/opt/powershell/System.Management.Automation.dll`).
4. The desktop's observed pwsh 7.6 is .NET 10. It is not the primary path. It is reached only if `Assembly.Load` throws `FileNotFoundException` and `PSHOME` or `PATH` then points at that install. `LoadFrom` of that DLL into the net9 host fails loud. The fallback does not prefer 5.1.

`WindowsUsesTheInstalledPwshDirectory` covers the fallback resolver only. Nothing in this tree asserts that `Load` prefers the package. That gap is an advisory, not a failed assertion: the four tests do fail if the probe order or the miss message changes.

## Linux evidence

This host's `pwsh` is `/usr/local/bin/pwsh` -> `/opt/powershell/pwsh`, version 7.5.4 on .NET 9.0.10. `PSHOME` was unset in the review shell, so discovery uses the symlink target and loads the same DLL the old constant named. The 199-case golden `tests/Antiphon.Tests/Scripts/Fixtures/card-1836-s6c-rc-port-golden.json` has an empty diff over the repair range.

The red commit `9eb8fb3c8` compiles. Its four tests look up `RcPortPowerShellAssembly.Resolve` by reflection and assert the method is non-null with the message "The RC port corpus must provide a PowerShell assembly resolver." The type is added only in the next commit, so the failure reason is the missing resolver. That commit was not rebuilt here. The commissioned cap was one build, used for the green re-run at the reviewed SHA. A scratch mutant that swaps the two standard directories would fail `LinuxTriesBothStandardInstallDirectoriesInOrder`; that mutant was not executed, for the same one-build cap. The assertions compare discovered paths and the exception text. They are not self-comparisons.

Green re-run at `14057e5ed`, one leased build then one leased `--no-build` run, `UseAppHost=false`, `TEMP=/tmp`, `ANTIPHON_LINUX_COMPILER_POLICY=0`, `--timeout 570s`. Build lease `0e67fa54-5ef0-4eb2-bdf3-d9d33d7fec74`, 0 errors. Test lease `51ced797-89d3-4e68-8f08-ac829cdfebff`. Linux `project.assets.json` for `Antiphon.Tests` has no `System.Management.Automation` package, and the test output directory has no copy of that DLL.

| Class | Passed | Failed | Skipped |
|---|---:|---:|---:|
| RcPortPowerShellAssemblyTests | 4 | 0 | 0 |
| RcShadowSampleCSharpTests | 12 | 0 | 0 |
| RcDirtyOrderCSharpTests | 6 | 0 | 0 |
| RcShadowDirtyDifferentialTests | 1 | 0 | 0 |
| TestClassificationPolicyTests | 45 | 0 | 0 |
| TestClassificationGuardTests | 1 | 0 | 0 |
| SlowTestRegistryMergeTests | 5 | 0 | 0 |
| SlowTestTripwireTests | 2 | 0 | 0 |
| NativeLinuxPolicyWitnessTests | 3 | 0 | 0 |
| TestLaneCategoryGuardTests | 1 | 0 | 0 |
| Total | 80 | 0 | 0 |

`TestLaneCategoryGuardTests` is the extra class the impacted-class selector named for a new test file. It passed. Duration 1m 16s. These are build-slot rows, not checkpoint-tool receipts, so `reviewedSourceClean` is false.

## Guards

`scripts/check-evidence-diff.ps1` over the repair range: commits=3, entries=0, violations=0.

`scripts/impacted-classes.ps1 -MergeSubset -Range bd34a7ff..14057e5ed`: `migrations=False`. Downgrade selector count is 0. No migration, model, or `Down(` edit. Kinds selected: `land-graph-audit-project`, `test-lane-category`.

`git merge-tree --write-tree origin/master HEAD` exited 0. Result tree `7f428ee19e426d3890f5a20a78257ae111c66973`. No conflicts.

No `.ps1` file is in the repair diff. The PowerShell 5.1 ASCII rule has nothing to apply. The why-missed note contains three middle dots in markdown links. It is not a script.

Protected nightly pin inputs are not in the diff. No pin recompute.

`tests/derived-impact-index.json` is not in the three commits. `RcPortPowerShellAssembly.cs` and `RcPortPowerShellAssemblyTests` are absent. `DerivedImpactIndexTests.committed_index_matches_the_builder` compares the committed bytes to a fresh build, so this tip would fail that test until the index is regenerated. The second regenerate run must print unchanged. Land regenerates once per wave. This review did not regenerate it: the one build was `Antiphon.Tests`, and the review does not commit product files.

`server/Application/Services/LandVerificationBuildGraph.manifest.txt` is not in the diff. A csproj edit is a build definition, which is why the selector named `land-graph-audit-project`. The Linux restore graph did not gain the package, because the reference condition is `OS == Windows_NT`. The definition digest still changes with the csproj bytes. `audit-land-verification-graph.ps1 -Check` was not run: the `bin-land-audit` evaluator DLL was absent, and rebuilding it would have been a second build. Land runs `scripts/land-graph-land-refresh.ps1 -Base <merge-base>` and then `-Check` must exit 0. Do not pass a bare `-Update`.

## Windows rows still UNOBSERVED

`NonBooleanProvenanceFailureInvalidates` and `CarriedChunkResultInvalidatesCarries` are `trust` cases in the golden. On run `3528e6b1` they recorded 22 and 13 mismatches because materialization tried `C:\opt\powershell\System.Management.Automation.dll` before any verdict compare. Both methods passed in the Linux re-run above (they are part of the 12). `RcShadowSample.Trust` discards `trustRecordPath`, so the hard-coded `/tmp/rc-port-trust-unused.json` argument is not written.

What can still fail on a Windows desktop run, after run `3528e6b1` ends, in the native pass already owed with CARD-2048:

- Restore of `System.Management.Automation` 7.5.4 and its 19 dependencies if nuget.org is unreachable and the desktop cache does not already have them. The package is not vendored.
- Output path length when that closure is copied into the test output.
- `Assembly.Load` throwing something other than `FileNotFoundException` (no fallback), or a failed package load falling through to pwsh 7.6 on .NET 10.
- Any golden case whose materialization needs a sibling assembly that exists beside the Linux pwsh DLL but is outside the NuGet closure. `PSObject` and `PSNoteProperty` are in the engine assembly itself. The two named trust methods do not write the unused record path.

Until that Windows row runs, platform verification for this loader stays UNOBSERVED. Record the source SHA, the PowerShell version, and the passed method roster.

## Ancestry

The tip is 12 commits ahead of `origin/master` and 74 behind. Nine of those commits are the Codex 0.170 readiness fixes already on `origin/rc/2026-11-30` (`f459e06e5` through `bd34a7ff6`), not the loader repair. Merge-tree of the whole tip onto current master is clean, so a land of this SHA publishes those nine commits as well.

## Advisories

These do not hold the land.

- Add a witness that `Load` on Windows prefers the package reference before `Resolve`, so deleting the `Assembly.Load` block can go red without a desktop run.
- The why-missed note is the right record of how the hard-coded path escaped the Linux-only S6c row.

## Positive controls

No positive control is defined for this repair. None were executed. Operator waiver of 2026-11-12 still leaves them pending for a later SourceLanding mutation pass.
