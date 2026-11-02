# CARD-1664 Review round 1 (task bce1e9ec) — FOUND: the fix does not fix the failure

Subject: feat/card-task-67640bd1 tip c9c92e50b49ddc242e4d0dfc5d742242ad719e98, base 18bc8a19151be1a3f206a9107a0170c386337564, range base..c9c92e50b. Code landing owner: task 67640bd1. Read-only on the subject; scratch worktrees under the session scratchpad.

## Defects

**D-1 (blocking): the fresh first solution build still fails NETSDK1047 at the tip; the fix targets a different race than the one that fails.**
- Where: tests/Antiphon.Tests/Antiphon.Tests.csproj:203, tests/Antiphon.E2E/Antiphon.E2E.csproj:68 (the fix); the failing callers are tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj:49-51 and tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj:80-82 (linux-x64 inner Build).
- Failure: fresh detached worktree at c9c92e50b, `dotnet build Antiphon.sln -p:UseAppHost=false` (lease 22b037b3, held 229 s): **2 errors NETSDK1047** on src/Antiphon.PtyHost (`no target for 'net9.0/linux-x64'`), 1599 warnings. Neither the PtyHost.Tests nor the SessionRunner.Tests copy step produced ptyhost-native; the E2E and Antiphon.Tests copy steps did. The linux-x64 obj dir was created at 21:07:51, and the assets file on disk was last written at 21:08:00, already holding both targets.
- Why: the SDK resolves the assets file through a per-node lock-file cache (registered task object, lifetime = build) keyed by path. On a fresh worktree there is no obj assets.cache yet, so the outer RID-less build of PtyHost (through ProjectReferences, after the outer solution restore with net9.0 only) loads `project.assets.json` into that node's cache. Later the copy step's linux-x64 Restore rewrites the file on disk with both targets. Its inner Build in the same node then reads the stale cached copy and gets NETSDK1047. A rebuild passes because the assets.cache files now exist and the lock file is not reloaded. This matches "first build on a fresh worktree fails, rerun passes". The fix only changes the on-disk content written by the RID-less copy-step restores. It cannot affect the cached copy, which comes from the outer restore.
- Proof (single MSBuild node, `dotnet msbuild probe.proj -m:1 -nodeReuse:false`, at the tip, after an outer-shape `dotnet restore -p:UseAppHost=false`):
  - Variant A: Build PtyHost RID-less, then Restore the RID shape (probe prints `ASSETS ON DISK=["net9.0","net9.0/linux-x64"]`), then Build the RID shape → **NETSDK1047, exit 1** (lease b721e1f3).
  - Variant B: the same run without the first Build → green, exit 0 (lease 580cb287).
- Fix: make the outer restore already carry linux-x64, or keep the copy step off the shared assets path:
  - (a) PtyHost `<RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>` on non-Windows. The land-graph pre-check refuses this property today, so it needs a reviewed `$AllowedProperties` edit in scripts/audit-land-verification-precheck.ps1.
  - (b) A distinct relative `MSBuildProjectExtensionsPath` (or `ProjectAssetsFile`) on both the copy step's Restore and its Build.
  - (c) Run the copy step's restore and build out of process.
  - Whichever option is chosen, the witness must be the single-node probe: red at base and at c9c92e50b, green at the fix, plus a fresh-worktree first build.

**D-2: the plural-RID restore adds new runtime-pack downloads (offline/restricted-host regression).**
- Where: same two lines (`UseAppHost=true;RuntimeIdentifiers=linux-x64`, no `SelfContained=false`).
- Failure: PtyHost restore shapes compared (lease c6e7f5d0). The existing RID shape and the base RID-less shape download only App.Ref, AspNetCore.App.Ref and NETCore.App.Host.linux-x64 9.0.22. The tip shape adds **Microsoft.NETCore.App.Runtime.linux-x64 9.0.22 and Microsoft.AspNetCore.App.Runtime.linux-x64 9.0.22**. On server2 both 9.0.22 runtime-pack folders were first created in ~/.nuget/packages at 2026-11-02 20:45, during the Code task. No other build step needs them: the Dockerfile publish is `--self-contained false` and runs in its own container.
- Why: a framework-dependent copy step must not pull runtime packs. A host without those packages cached (offline/restricted, the desktop, a fresh runner, every SDK patch bump) now fails the restore or downloads about 100 MB.
- Fix: whatever replaces D-1 must keep downloadDependencies equal to the existing RID shape. Compare `project.nuget.cache` expectedPackageFiles in the witness.

## Checked and sound
- Land-graph digest: `-Check` at the tip with the cached evaluator (/work/cache/land-graph-audit/b87931b2…, tree identical at base and tip; lease a60bd14c) prints `digest=35C53D9366FB8FF4CA4C166B06FE4F5321AAC4922627FE6FBD8CCB6B634611F3 count=69`, `manifest does not match the selected graph`, exit 1. It refuses in the pre-check, so the evaluator itself did not run. I recomputed the digest independently from `git ls-tree -r -z` with the pre-check's definition-path selection: base D92450D9…36BE/69 (equals the manifest), tip 35C53D93…11F3/69. The only differing rows are the two csproj blobs (E2E bcb8b1b3→6dfd8a67, Tests b605bc30→994ef384). No -Update was run.
- Worker witness (copied verbatim from .antiphon/task-67640bd1.md):
  - base: `ASSETS ["net9.0"]`, NETSDK1047, exit 1 (lease da84effd).
  - tip: `ASSETS ["net9.0","net9.0/linux-x64"]`, 0 errors, exit 0 (lease 32461544).
  - It is not vacuous for the cross-process restore-order race, but that is not the race that fails the solution build (D-1).
- E2ECopyOwnedPtyHostAppHostContractTests at the tip (from the fresh build, --no-build; lease e7d71d52): 1/1 passed, 0 skipped, TRX Passed (`e2e_copy_owned_ptyhost_restores_apphost_pack_before_inner_build`). It asserts UseAppHost=true on the Restore and the Build and is unaffected by the edit.
- Windows: all four copy-step targets are `Condition="'$(OS)' != 'Windows_NT'"`, so a Windows build is unchanged.
- Copied app host at the tip: tests/Antiphon.Tests and tests/Antiphon.E2E `bin/Debug/net9.0/Antiphon.PtyHost` are ELF, -rwxr-xr-x, 75304 bytes and byte-identical to each other. The RID-less inner Build properties are unchanged.
- Gates:
  - check-evidence-diff over base..c9c92e50b: commits=3 entries=1 violations=0, exit 0.
  - `merge-tree` onto origin/master 18bc8a191: exit 0.
  - origin/rc/2026-11-01 10a826b9d: a whole-branch merge-tree conflicts only in client/src/api/agentTasks.ts (master/rc drift); a cherry-pick of 3c41d87aa with --merge-base 18bc8a191 is clean, exit 0.

## Scope
No plan or `### Checkpoints` table exists for this card. Standing rule 8 (minimal testing, no whole-Unit runs from Review) applies. The commissioned selection all ran: witness ×2, contract test, fresh build, -Check, gates, plus 2 probe runs and 1 pack-comparison run, each in its own lease. PCs: none planned, so none are pending from this review.

## Probe (scratch, not committed)
```xml
<Project>
  <Target Name="Run">
    <MSBuild Condition="'$(SkipOuter)' != 'true'" Projects="$(P)" Targets="Build" Properties="Configuration=Debug;UseAppHost=false" />
    <MSBuild Projects="$(P)" Targets="Restore" Properties="Configuration=Debug;TargetFramework=net9.0;UseAppHost=true;RuntimeIdentifier=linux-x64;SelfContained=false;OutDir=$(O)/" />
    <Exec Command="node -e &quot;console.log('PROBE ASSETS ON DISK=' + JSON.stringify(Object.keys(require(process.argv[1]).targets)))&quot; $(A)" />
    <MSBuild Projects="$(P)" Targets="Build" Properties="Configuration=Debug;TargetFramework=net9.0;UseAppHost=true;RuntimeIdentifier=linux-x64;SelfContained=false;OutDir=$(O)/" />
  </Target>
</Project>
```
```bash
#!/usr/bin/env bash
# $1 checkout root, $2 out dir, $3 SkipOuter true|false
set -u
p="$1/src/Antiphon.PtyHost/Antiphon.PtyHost.csproj"; a="$1/src/Antiphon.PtyHost/obj/project.assets.json"
dotnet restore "$p" -p:UseAppHost=false >/dev/null || exit 10
dotnet msbuild "$(dirname "$0")/probe.proj" -t:Run -m:1 -nodeReuse:false -p:P="$p" -p:O="$2" -p:A="$a" -p:SkipOuter="$3" 2>&1 | grep -E "PROBE|NETSDK1047|Antiphon.PtyHost ->" | sort -u
rc=${PIPESTATUS[0]}; echo "PROBE exit=$rc skipOuter=$3"; exit $rc
```
Run as `pwsh -NoProfile -File scripts/build-slot.ps1 -Label probe -- bash run.sh <root> <out> false|true` (one lease each).
