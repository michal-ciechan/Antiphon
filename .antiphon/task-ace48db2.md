# CARD-1664 redo Code report (task ace48db2): out-of-process linux-x64 copy step

Outcome: fixed with a build-files-only change. The reviewer's single-node probe, driven through the REAL copy targets, is red at base (NETSDK1047) and green at the fix. Two fresh-worktree whole-solution first builds at the fix are green. No new packs, identical runtime output, Windows untouched. Land-graph `-Check` refuses only on the digest. `-Update` was NOT run.

- Branch feat/card-task-ace48db2, worktree /work/worktrees/task-ace48db2, Code task ace48db2 (landing owner), base 18bc8a19151be1a3f206a9107a0170c386337564 (restarted from master; nothing from feat/card-task-67640bd1).
- Fix commit 2ee3430f2ee168f28d84a3ff95261ee93ec5a600 (tested SHA for every run below). The report commit follows it.
- Files: tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj (CopyOwnedPtyHostAppHostForHerdr) and tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj (CopyOwnedPtyHostAppHost), +4/-6 each.

## Fix (candidate b)
In each linux-x64 copy step, the two in-process `<MSBuild Targets="Restore">` / `<MSBuild Targets="Build">` calls are replaced by one Exec:
`"$(DOTNET_HOST_PATH)" build <PtyHost.csproj> -nologo -tl:off -v:minimal -m:1 -p:Configuration=$(Configuration) -p:TargetFramework=$(TargetFramework) -p:UseAppHost=true -p:RuntimeIdentifier=linux-x64 -p:SelfContained=false "-p:OutDir=$(_PtyHostNativeDir)"`
- The properties are the same as before. `dotnet build` restores, then builds, in a fresh process (separate restore/build sessions). It reads project.assets.json from disk, never the outer node's cached net9.0-only copy.
- The Copy and chmod steps are unchanged.
- The Antiphon.Tests and E2E (RID-less) steps are untouched. E2ECopyOwnedPtyHostAppHostContractTests reads E2E and still passes.
- Why not (a): any isolating global property flows to PtyHost's ProjectReferences.
  - An absolute MSBuildProjectExtensionsPath, ProjectAssetsFile or RestoreOutputPath makes Agents.Pty, Protocol and Contracts share one assets file.
  - A relative MSBuildProjectExtensionsPath breaks the nuget.g.props/targets imports, because imports resolve relative to Microsoft.Common.props. That drops PkgMicrosoft_Windows_Console_ConPTY and the conpty/ files from ptyhost-native.
  - A relative BaseIntermediateOutputPath drops obj/** from DefaultItemExcludes, so the outer obj/*.AssemblyInfo.cs gets compiled (CS0579).
- Why not (c): it needs an $AllowedProperties edit.
- Residual, pre-existing, not the observed failure: the RID-less Antiphon.Tests/E2E copy-step restores still rewrite the same obj/project.assets.json (net9.0 only). A cross-process interleave between the child's restore and build stays theoretically possible. This change neither adds nor removes it.

## Witness (single MSBuild node, the real copy targets of the checkout under test; scratch, not committed: no natural home under tests/)
| run | checkout | SkipOuter | result | lease |
|---|---|---|---|---|
| red | base 18bc8a191 fresh | false | NETSDK1047 `no target for 'net9.0/linux-x64'`, ASSETS ON DISK ["net9.0","net9.0/linux-x64"], both copy targets ok=false, exit 1 | 4931f68e waited=0s held=16s |
| green | fix 2ee3430f2 fresh | false | both ok=true, apphost -rwxr-xr-x 75304 ELF md5 97be7a09..., exit 0 | 4e89f006 waited=0s held=56s |
| control | base fresh | true | both ok=true, same apphost md5, exit 0 | 4e155c03 waited=0s held=25s |

probe.proj:
```xml
<Project>
  <!-- CARD-1664 single-node witness. One MSBuild node (-m:1): optionally build Antiphon.PtyHost
       RID-less first (the outer solution build shape, which caches the net9.0-only assets file in
       this node), then run the REAL linux-x64 copy targets of the two test projects from the
       checkout under test. Base: NETSDK1047. Fixed: both copy targets succeed. -->
  <Target Name="Run">
    <MSBuild Condition="'$(SkipOuter)' != 'true'" Projects="$(R)/src/Antiphon.PtyHost/Antiphon.PtyHost.csproj" Targets="Build" Properties="Configuration=Debug;UseAppHost=false" />
    <MSBuild Projects="$(R)/tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj" Targets="CopyOwnedPtyHostAppHost" Properties="Configuration=Debug;UseAppHost=false" ContinueOnError="true" />
    <Message Importance="high" Text="PROBE CopyOwnedPtyHostAppHost (PtyHost.Tests) ok=$(MSBuildLastTaskResult)" />
    <MSBuild Projects="$(R)/tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj" Targets="CopyOwnedPtyHostAppHostForHerdr" Properties="Configuration=Debug;UseAppHost=false" ContinueOnError="true" />
    <Message Importance="high" Text="PROBE CopyOwnedPtyHostAppHostForHerdr (SessionRunner.Tests) ok=$(MSBuildLastTaskResult)" />
    <Exec Command="node -e &quot;console.log('PROBE ASSETS ON DISK=' + JSON.stringify(Object.keys(require(process.argv[1]).targets)))&quot; $(R)/src/Antiphon.PtyHost/obj/project.assets.json" />
  </Target>
</Project>
```
run.sh (one lease each: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label probe -- bash run.sh <fresh-checkout> false|true`):
```bash
#!/usr/bin/env bash
# CARD-1664 witness. $1 checkout root (fresh: no obj/ under src/Antiphon.PtyHost), $2 SkipOuter true|false.
# Restores the two test projects in the outer shape (UseAppHost=false, RID-less), then runs probe.proj
# in ONE MSBuild node. Exit = the msbuild exit; 1 with NETSDK1047 is the defect.
set -u
r="$1"
export TEMP=/tmp
for p in tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj; do
  dotnet restore "$r/$p" -p:UseAppHost=false >/dev/null || exit 10
done
dotnet msbuild "$(dirname "$0")/probe.proj" -t:Run -m:1 -nodeReuse:false -p:R="$r" -p:SkipOuter="$2" 2>&1 | grep -E "PROBE|NETSDK1047|error|Antiphon.PtyHost ->" | sed -E 's/^ +//' | sort -u
rc=${PIPESTATUS[0]}
for t in tests/Antiphon.PtyHost.Tests tests/Antiphon.SessionRunner.Tests; do
  f="$r/$t/bin/Debug/net9.0/Antiphon.PtyHost"
  [ -f "$f" ] && echo "PROBE $t apphost: $(stat -c '%A %s' "$f") $(md5sum < "$f" | cut -c1-32) $(head -c4 "$f" | od -An -c | tr -d ' ')" || echo "PROBE $t apphost: missing"
done
echo "PROBE exit=$rc skipOuter=$2"; exit $rc
```

## Output and pack checks
- Pack check: PtyHost and Agents.Pty obj/project.nuget.cache expectedPackageFiles are identical at base and fix.
  - Packs listed: App.Ref, AspNetCore.App.Ref, NETCore.App.Host.linux-x64 9.0.22, plus the package closure.
  - No Microsoft.NETCore.App.Runtime.linux-x64 and no AspNetCore runtime pack.
  - `find ~/.nuget/packages -maxdepth 2 -newer <marker>` (marker taken before the fix probe) is empty after the probe and both fresh builds.
- ptyhost-native tree, base control vs fix (PtyHost.Tests and SessionRunner.Tests):
  - The same 22 files and modes.
  - Every non-dll/pdb file is md5-identical: apphost, libporta_pty.so, deps.json, runtimeconfig.json and conpty/.
  - Only the .pdb sizes differ, by 4-8 bytes. That is the scratch worktree path length (wt-base2 vs wt-fix) embedded in the PDB, not the change.
- Fresh builds, copied app host: in all four test projects bin/Debug/net9.0/Antiphon.PtyHost is ELF, -rwxr-xr-x, 75304 bytes, with the same md5. ptyhost-native holds 25 files (Tests, E2E) or 22 files (PtyHost.Tests, SessionRunner.Tests). libporta_pty.so is copied in both.

## Fresh-worktree first solution builds at 2ee3430f2 (`dotnet build Antiphon.sln -p:UseAppHost=false`, TEMP=/tmp, one lease each)
- #1: 0 errors, 1599 warnings (= base), 0 NETSDK1047, 3m07s. Lease 49b2f3a1 waited=0s held=189s.
- #2: 0 errors, 1599 warnings, 0 NETSDK1047, 5m36s. Lease 6c2518d7 waited=0s held=337s.

## Tests
- E2ECopyOwnedPtyHostAppHostContractTests from fresh build #1 (--no-build, -p:UseAppHost=false): 1/1 passed, 0 skipped, TRX Passed (`e2e_copy_owned_ptyhost_restores_apphost_pack_before_inner_build`). Lease a5b33040 held=8s.
- Not run:
  - The whole Unit lane. No plan or `### Checkpoints` exists for this card, the brief commissioned this selection, and standing rule "minimal testing" applies.
  - The checkpoint tool, which needs a plan.
- Every build/test above ran in its own build-slot lease. Unlisted runs: the land-graph -Check (lease 67c122a1 held=11s).

## Land graph (-Check only; -Update NOT run)
- `-Check -ToolPath /work/cache/land-graph-audit/b87931b2.../Antiphon.LandGraphAudit.dll` (tools tree b87931b2 unchanged) printed:
  - `digest=7456743D28E951E9A6FBF53874B166750CD2631588969556A891FCC2CA673C22 count=69`
  - `manifest does not match the selected graph`
  - `audit refused: land graph pre-check refused`, exit 1
- No property/item/element allowlist refusal.
- Independent recompute from `git ls-tree -r -z` with Test-DefinitionPath selection, ordinal sort, NUL-joined, SHA256:
  - base: D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE/69, which equals the manifest.
  - fix: 7456743D...3C22/69, which equals the audit.
- Changed file entries (only two):
  - tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj 65c8cd10d64d858f2b12787e8821ac6eff93d093 -> 641fce3d664ff86a955c2b5ae7a7ba4cb0d77c8b
  - tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj d0c718b791352f0edc86aea66c4cbeaca961013e -> c1bc88ebbd8b1ae3166cb9ce9bf00bf24e3581a0
- Property entries changed: none. There is no PropertyGroup or Output change. The target body swaps 2 MSBuild tasks for 1 Exec, which references the environment property DOTNET_HOST_PATH without defining it.
- The Merge task's one -Update should produce 7456743D.../69.

## Gates
- check-evidence-diff 18bc8a191..2ee3430f2: commits=1 entries=0 violations=0, exit 0. Rerun over the report commit below.
- merge-tree onto origin/master 18bc8a191: exit 0.
- origin/rc/2026-11-01 10a826b9d:
  - Whole branch: exit 1, conflicting only on client/src/api/agentTasks.ts (master/rc drift, as Review bce1e9ec found).
  - Cherry-pick simulation `--merge-base 18bc8a191`: exit 0. The task is master-only.

## Mutation candidates (no plan PCs)
- Restoring the in-process MSBuild Build call in either target: probe red. Cheap: about 20 s per probe on a fresh checkout. The probe needs a fresh src/Antiphon.PtyHost/obj, because a second run finds the assets.cache and passes.
