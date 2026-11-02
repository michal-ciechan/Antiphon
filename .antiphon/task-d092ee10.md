# CARD-1664 Review round 2 (task d092ee10): out-of-process PtyHost copy step

Verdict: CLEAN. No defects found. D-1 and D-2 from Review bce1e9ec are both closed.

- Subject: Code task ace48db2-866a-4a71-9d33-4c1627698da9, branch feat/card-task-ace48db2.
- Tip dc1cbc7a3b5233b9efccd971203b8598a9fec3a4. The fix is 2ee3430f2ee168f28d84a3ff95261ee93ec5a600, plus a report-only commit after it.
- Base 18bc8a19151be1a3f206a9107a0170c386337564.
- Every build below ran in a fresh detached scratch worktree at the stated SHA (checked with `git rev-parse`), with one build-slot lease each and TEMP=/tmp. All scratch worktrees have been removed.

## 1. D-1 closed: the stale in-node assets file
I ran the Code report's single-node probe verbatim (probe.proj and run.sh), with SkipOuter=false and a fresh `src/Antiphon.PtyHost/obj`.

| run | SHA | result | lease |
|---|---|---|---|
| base | 18bc8a191 | NETSDK1047 `no target for 'net9.0/linux-x64'`; assets on disk `["net9.0","net9.0/linux-x64"]`; both copy targets ok=false; app hosts missing; exit 1 | 4cf20598, held 23 s |
| fix | 2ee3430f2 | both copy targets ok=true; app host -rwxr-xr-x, 75304 bytes, ELF, md5 97be7a0945e9e8104976616d740aee17 in both outputs; exit 0 | b1113a2f, held 48 s |

I ran `dotnet build Antiphon.sln -p:UseAppHost=false` at 2ee3430f2 in fresh worktrees, twice:
- #1: exit 0, 0 errors, 1599 warnings, 0 NETSDK1047, 4m42s. Lease d6f9fa1f, held 283 s.
- #2: exit 0, 0 errors, 1599 warnings, 0 NETSDK1047, 3m15s. Lease 42edd3f6, held 196 s.

## 2. D-2 closed: no new packages
- `expectedPackageFiles` in PtyHost `obj/project.nuget.cache` is identical at base and at the fix. It lists 10 entries:
  - microsoft.netcore.app.ref 9.0.22
  - microsoft.aspnetcore.app.ref 9.0.22
  - microsoft.netcore.app.host.linux-x64 9.0.22
  - the package closure

  Agents.Pty lists 11 entries, with the same packs.
- No Microsoft.NETCore.App.Runtime.linux-x64 and no AspNetCore.App.Runtime.linux-x64 appear at either SHA.
- `find ~/.nuget/packages -maxdepth 2 -newer <marker>` was empty after the fix probe and both solution builds. The marker was taken before the fix probe.
- The two runtime-pack folders in the cache date from 20:45 today. That is the round-1 Code task, not this one.
- Copied app host after fresh build #1: in Antiphon.Tests, E2E, PtyHost.Tests and SessionRunner.Tests it is -rwxr-xr-x, 75304 bytes, ELF, with the same md5 97be7a09… in all four.

## 3. The Exec is safe
- **A broken nested build fails the outer build.**
  - Setup: a scratch copy with `-p:RuntimeIdentifier=bogus-rid`, and the old app host deleted first.
  - Result: NETSDK1083 inside the child, then MSB3073 "exited with code 1" at `tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj:49`, Build FAILED, exit 1.
  - The app host stays missing. The target has no ContinueOnError and no IgnoreExitCode. Lease 704149d0.
- **An empty DOTNET_HOST_PATH fails closed.**
  - With `-p:DOTNET_HOST_PATH=` the command becomes `"" build …`. sh reports `: Permission denied`, MSB3073, Build FAILED, exit 1. Lease 861da2f8.
  - There is no fallback, but it is never a silent success.
  - The dotnet CLI sets the variable even when the caller's environment does not have it: `env -u DOTNET_HOST_PATH dotnet build|msbuild` prints `/usr/share/dotnet/dotnet`.
  - So dotnet build/run/msbuild, the checkpoint tool (`dotnet build`) and build-slot are all covered.
- **Command line and conditions.**
  - The project path is absolute, from `$(MSBuildThisFileDirectory)../../src/…`, and quoted, as is OutDir. Configuration and TargetFramework are unquoted tokens.
  - The child uses -m:1, so it adds at most one node outside the slot's -maxcpucount.
  - `Condition="'$(OS)' != 'Windows_NT'"` is unchanged on both targets, so Windows is unaffected.
  - Neither target has Inputs or Outputs, before or after the change. Both run after every Build, as before.
- **Incremental no-op cost.** I timed a no-op build of PtyHost.Tests:
  - base: 8.5 s and 9.8 s
  - fix: 11.3 s, 11.9 s and 10.2 s; of that, the child ran 1.4–2.9 s

  That is about +2 s per copy target, and a whole-solution build has two copy targets. Acceptable.

## 4. Residual race (advisory, not reproduced)
- The race: the RID-less Antiphon.Tests and E2E copy-step restores (`Antiphon.Tests.csproj:199`, `Antiphon.E2E.csproj:64`) can rewrite PtyHost `obj/project.assets.json` to net9.0 only. If that lands on another node between the child's restore and its ResolvePackageAssets, the child gets NETSDK1047.
- The window is seconds wide. The same window existed at base, between the in-process Restore and Build, so this change does not introduce it.
- It did not reproduce: my two fresh builds and the worker's two were all green.

## 5. Land graph and gates
- `git diff --raw` base..fix shows only two changes:
  - tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj 65c8cd10d → 641fce3d6
  - tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj d0c718b79 → c1bc88ebb
- No PropertyGroup, property or Output entry changed. The target bodies swap 2 MSBuild tasks for 1 Exec.
- fix..tip adds only `.antiphon/task-ace48db2.md`.
- The ten master commits after base (origin/master is now bac789483) touch no *.csproj, *.props, *.targets, sln, global.json or NuGet.config. The expected digest 7456743D…3C22 / 69 therefore still holds for the Merge.
- I did not run `-Update`.
- check-evidence-diff 18bc8a191..dc1cbc7a3: commits=2, entries=1, violations=0, exit 0.
- merge-tree onto origin/master bac789483: clean, exit 0.
- merge-tree onto origin/rc/2026-11-01 10a826b9d:
  - Whole branch: conflicts only in `client/src/api/agentTasks.ts`, which is master/rc drift.
  - Cherry-pick of 2ee3430f2 with `--merge-base 18bc8a191`: clean, exit 0.
- E2ECopyOwnedPtyHostAppHostContractTests from fresh build #1: 1/1 passed, 0 skipped. TRX: `e2e_copy_owned_ptyhost_restores_apphost_pack_before_inner_build` Passed. Lease 710cf585. The E2E csproj is untouched.

## Advisories (no action required to land)
- **A-1: the Exec child does not inherit the outer build's global properties.** This covers ArtifactsPath, UseArtifactsOutput, OutputPath and the others that MSBuild tasks inherited.
  - With the checkpoint-tool build shape (`--property:OutputPath=bin-x/ --property:ArtifactsPath=<abs> --property:UseArtifactsOutput=true -p:UseAppHost=false -nodeReuse:false`) on fresh worktrees, a single PtyHost.Tests build gave:
    - base: no `obj/` in the worktree
    - fix: `obj/` created in src/Antiphon.PtyHost, Agents.Pty, PtyHost.Protocol and SessionRunner.Contracts
  - Both builds exited 0.
  - The checkpoint ledger's write roots include `obj/` beside each statically visible project (`BuildConfinement.WriteRoots`), and VerifyLedger compares only entries that existed beforehand. So this is not a tamper refusal, and it also keeps the child away from the outer assets file.
  - Optional: say so in the comment.
- **A-2: no fallback when DOTNET_HOST_PATH is empty.** It fails closed (MSB3073). Only a host that runs MSBuild without the dotnet CLI could hit it. An optional fallback is `$(NetCoreRoot)dotnet`.
- **A-3:** the residual race in section 4.

## Scope
- No plan or `### Checkpoints` table exists for CARD-1664. The commissioned selection ran in full: probe base and fix, two fresh solution builds, the pack check, broken-nested and empty-host-path runs, the checkpoint-shape builds, no-op timing, the contract test and the gates.
- Every run was leased.
- Unlisted runs, with reasons: the four no-op timing builds (performance check) and the two checkpoint-shape builds (A-1 environment check).
- The whole Unit lane did not run. No plan selection exists, and the standing "minimal testing" rule applies.
- No PCs exist.
