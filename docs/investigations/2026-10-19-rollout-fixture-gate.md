# Server2 rollout Fixture gate: SDK/reference-pack mismatch

The failed Fixture seed reaches its offline .NET smoke restore, where the selected image's SDK expects **9.0.21 reference packs but the image contains only 9.0.20**. This is inherited image/toolchain drift, not CARD-1333, stale fixture resources, or the earlier npm ownership defect. A local controlled reproduction fails with `NU1100`; explicitly selecting the two installed 9.0.20 reference packs restores and builds successfully. The production gate remains failed. No production repair or gate retry was performed.

Task `efe949b4`, Debug; report branch `feat/card-task-efe949b4`. Investigated rollout source: `df6b422c71b0828b4bd7f855a86e2a51d1987e79`. The report worktree started at `f99f8bffb5887d43fafae61ae979ae9e11a36f80`; source attribution below uses `git show` of the rollout SHA, not an assumption that these tips match. Line numbers refer to that rollout SHA.

## Exact retained failure

Original evidence root:

```text
C:\src\Antiphon\.antiphon\c849-c849957ff39e7e024ebe\0
```

Remote copy: `/work/test-evidence/c849957ff39e7e024ebe0/runner-cache-fixture/`.

| Evidence | Exact retained content |
|---|---|
| `manifest.json:1` | `sourceSha=df6b422c71b0828b4bd7f855a86e2a51d1987e79`, `runId=c849957ff39e7e024ebe0` |
| `remote-output.txt:1-4` | `CacheSmokeFailed`, `CacheSeedSmokeFailed`, `FixtureSeedFailed`, `transport-exit=2` |
| `runner-cache-fixture/fixture-seed.txt:1-2` | `diagnosis=CacheSeedSmokeFailed`, `exit=2` |
| Both `c590-result.json` files | `{"accepted":false,"diagnosis":"FixtureSeedFailed","exit":2}` |
| `runner-cache-fixture/npm-integrity.txt:1` | `npm-integrity=passed` |
| `runner-cache-fixture/smoke.txt:1-3` | All three `C849_PACK /usr/share/dotnet/packs/<pack>/9.0.20` lines; no `C849_SMOKE` success line |
| `runner-cache-fixture/fixture-groups.txt` | `PASS F-1`, `PASS F-2`; F-3 seed did not complete |
| Cleanup ledger/residue | Both files exist and are zero bytes |

There are 15 completed expected-red control families and 19 variants. These partial results do not satisfy the required 9 groups, 32 families, 47 variants and 2 inventories.

The call chain is `c849_fixture_seed` -> `c849_fixture_inner seed c849_seed` -> `c849_smoke` -> its slot-gated `driver.sh`. `c849_smoke` at lines 1533-1534 maps any nonzero child status to `CacheSmokeFailed`, exit 2. Seed catches that at lines 2419-2421 and records `CacheSeedSmokeFailed`, exit 2. The outer seed wrapper at line 3943 reports `FixtureSeedFailed`, exit 2. `verify-card0849-caches.ps1:133` then throws because the remote case failed; it is a reporting boundary, not the defect.

**Evidence limit:** the original `dotnet restore` and `dotnet build` stdout is redirected to `/dev/null` at lines 1560-1561, and the outer slot wrapper stdout is also discarded at line 1567. The original raw restore message and raw command exit code were not retained. The original inner/case/transport exits are proven **2**; the local equivalent restore's exit is **1**. Do not describe the local NU1100 lines as recovered original log lines. The restore attribution follows the measured SDK/pack mismatch and the controlled reproduction below, rather than a retained per-command receipt.

## Why this image cannot restore the smoke project

The helper fingerprints record image:

```text
sha256:a9be72ebb98bddae896fd93649897cdc7d8c59449a36629e4dd2100fd956b50c
```

Read-only host inspection identified its tag as `antiphon-server2/session-testing:09e48c5d6f1d`, image revision `09e48c5d6f1d404cfea8ec0d992270424fd2a184`, created `2026-10-16T17:54:28.157662054Z`. The requested tag `antiphon-server2/session-testing:df6b422c71b0` is absent. `c849_image` at lines 1372-1411 explicitly permits Fixture to fall back to a retained marker/donor/main image when the requested tag is absent. The observed helper fingerprints establish which image it actually used. The image is not the new rollout SHA merely because the case's manifest names that SHA.

The standing container uses the same image. UID-1654 read-only inspection showed:

```text
dotnet --list-sdks:
10.0.402 [/usr/share/dotnet/sdk]

/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref:       10.0.13  9.0.20
/usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref:    10.0.13  9.0.20
/usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64: 10.0.13  9.0.20
```

Both 9.0.21 reference-pack directories are absent. The installed SDK's own `/usr/share/dotnet/sdk/10.0.402/Microsoft.NETCoreSdk.BundledVersions.props` specifies, for `TargetFramework="net9.0"`:

```text
TargetingPackName="Microsoft.NETCore.App.Ref"
TargetingPackVersion="9.0.21"
...
TargetingPackName="Microsoft.AspNetCore.App.Ref"
TargetingPackVersion="9.0.21"
```

The SDK's `Microsoft.NET.Sdk.FrameworkReferenceResolution.targets` documents and implements generating package downloads when those targeting packs are absent from `NetCoreTargetingPackRoot`.

The smoke project at `c590-remote.sh:1552` pins `RuntimeFrameworkVersion=9.0.20` and `TargetLatestRuntimePatch=false`, but does **not** set `TargetingPackVersion` on either framework reference. A runtime-version pin does not override the reference-pack version. The preceding file checks prove that 9.0.20 exists, then restore selects 9.0.21. Its fresh packages directory and NuGet configuration with cleared sources cannot supply those missing packs. Installed 9.0.21 shared runtimes are not reference packs.

The Dockerfile explains the drift: `docker/session-runner-grok/Dockerfile:35,53` uses the floating SDK `10.0` tag while lines 54-56 extract a fixed SDK `9.0.318` / pack `9.0.20` pair. The comment assumes SDK 10 expects 9.0.20. The actual image has moved to SDK 10.0.402, which expects 9.0.21. The separate image `net9-offline` probe also still insists on SDK `10.0.401` (`verify-codex-image.sh:244-245`); its positive qualification needs reconciliation with the supported image version as a separate follow-up, not an assertion bypass.

## Competing explanations

| Explanation | Finding |
|---|---|
| CARD-1333 regression | Excluded as the cause. Commit `07d47719017ed47860c0f368f9fe6e8d4add1647` changes only `scripts/fixtures/c1192-workspace-contract.sh` and `RemoteScriptContractTests.cs`. `mode_npm_sentinel` is an offline model; this live case executes `c590-remote.sh`. The C1191 edit retargets a test assertion at the already-existing inner wrapper. The live `c849_fixture_npm` phase is after the failed seed and was not reached. |
| Older c849 containers/volumes/networks | Successful censuses at 14:28:42 and 14:34:06 UTC returned no matching objects. The run uses fresh names and rejects occupied names. No residue cleanup is needed. |
| Root-owned npm verification logs | This run passed npm verification and checked writer cleanup before reaching smoke. All seed volume roots recorded `1654:1654:700`. The earlier 2026-10-16 failure was `NpmStagedIntegrityFailed`; this one is `CacheSeedSmokeFailed`. Removed fixture contents cannot now be stat'ed, but the retained successful npm step excludes that earlier failure path. |
| Full disk/inodes/memory | At 14:34:06 UTC, 194 GiB disk free, 33,770,849 inodes free (29% used), and 106,242 MiB available memory. Kernel journal inspection for the failure window returned no OOM, killed-process, I/O-error, or no-space lines. These are bounded observations, not historic continuous telemetry. |
| Concurrent task / build-slot starvation | Broker log: lease `0c30043a-d284-45f5-ae0a-3cf9c1349d37` granted to `c849-smoke` at 14:26:30 after 0s, `1/4 occupied`; released at 14:26:34 after 4s. The driver was admitted; this was not a slot timeout or broker connectivity failure. |
| npm registry | Seed's npm cache comes from a local tarball, verification passed, and the failing smoke uses cleared NuGet sources. No npm registry is involved at this step. |

## Local reproduction and proposed minimum repair

Remote access remained read-only. The installed 9.0.20 pack trees were streamed from the existing container with `docker exec -u 1654:1654 ... tar ...` into this task's ignored local evidence directory. No remote file was created, no container was started, and no remote build ran. Reference-pack `FrameworkList.xml` hashes were compared with the source paths.

The local controlled probe uses installed **Windows SDK 9.0.319**, which also requests net9 reference packs 9.0.21, and the copied image packs as `NetCoreTargetingPackRoot`. It reproduces the missing-reference-pack mechanism, not the full Linux SDK 10.0.402 environment. All drivers used the desktop build-slot gate; repository build customizations were disabled for this isolated project. The same linux-x64 smoke project, runtime pin, empty package directory and cleared package sources were used.

| Invocation | Outcome |
|---|---|
| Initial local original-project restore | Exit 1; NU1100 for both 9.0.21 reference packs plus Windows SDK's additional WindowsDesktop reference download. |
| Original project with Windows-only transitive reference downloads disabled | Exit 1; exactly two NU1100 errors: `Microsoft.NETCore.App.Ref (= 9.0.21)` and `Microsoft.AspNetCore.App.Ref (= 9.0.21)`. |
| Candidate with the same local adaptations | Restore exit 0; build exit 0, 0 warnings, 0 errors. `project.assets.json` has empty `libraries`; the linux-x64 apphost was produced. Native Linux execution was not attempted on Windows. |

The minimum proposed production change is to add these metadata overrides to the generated `Smoke.csproj` in `c849_smoke`:

```xml
<ItemGroup>
  <FrameworkReference Update="Microsoft.NETCore.App" TargetingPackVersion="9.0.20" />
  <FrameworkReference Update="Microsoft.AspNetCore.App" TargetingPackVersion="9.0.20" />
</ItemGroup>
```

This selects the existing reviewed image packs and preserves the runtime/apphost pin, empty caches, cleared sources, UID 1654, slot gate and masked-pack controls. The same smoke function is used by seed, Fixture apphost and prune refill. The race fixtures target net10.0, so they are not another unpinned net9 project requiring this change.

The candidate patch is prepared but **not applied**:

```text
C:\Antiphon\worktrees\card-task-efe949b4\.antiphon\fixture-gate-debug\proposed-smoke-pack-pin.patch
SHA256 B93A3A798F42E16224CA5337A6CC065B05F4916469742B895267809FA1F44FAA
```

`git apply --check` passes against this task's source. The patch's project bytes are the locally validated candidate. No production script or test was edited in this Debug task.

### Approval/execution boundary

Commission a separate Code/Review/land repair. In its assigned worktree, the exact proposed application commands are:

```powershell
$patch = 'C:\Antiphon\worktrees\card-task-efe949b4\.antiphon\fixture-gate-debug\proposed-smoke-pack-pin.patch'
if ((Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash -ne 'B93A3A798F42E16224CA5337A6CC065B05F4916469742B895267809FA1F44FAA') { throw 'Candidate patch changed' }
git apply --check $patch
if ($LASTEXITCODE -ne 0) { throw 'Candidate does not apply to selected source' }
git apply $patch
```

The repair should add a focused native restore regression using an SDK whose defaults differ from the installed pinned reference packs, and retain the existing missing-pack/empty-cache controls. The modeled `c913-image-contract.sh` stubs `dotnet restore` and cannot detect this SDK metadata drift. Preserve restore/build diagnostics in task-owned evidence on failure; currently they are discarded.

After review, land and explicit selection of the repaired RC, run from the canonical checkout at that full SHA:

```powershell
$approvedSha = (git rev-parse HEAD).Trim() # Must be the caller-selected repaired RC, not this report branch.
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha $approvedSha
if ($LASTEXITCODE -ne 0) { throw 'Fixture still refused; stop rollout and retain new evidence' }
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Inventory -Sha $approvedSha
```

These are proposed caller actions, not actions taken here. A retry of unchanged `df6b422c...` with the same image would encounter the same mismatch. Do not bypass Fixture, warm its deliberately empty package cache, change ownership, downgrade a running SDK, or restart the standing runner to address it. Full native Fixture acceptance is still required; the local probe does not certify later groups.

### Destructive cleanup requiring separate operator approval

**None proposed.** Both censuses found no c849 objects. The failed run's eight recorded volumes are already absent:

```text
c849c849957ff39e7e024ebe0-work
c849c849957ff39e7e024ebe0-mask
c849c849957ff39e7e024ebe0-packages
c849c849957ff39e7e024ebe0-scratch
c849c849957ff39e7e024ebe0-npm
c849c849957ff39e7e024ebe0seed-packages
c849c849957ff39e7e024ebe0seed-scratch
c849c849957ff39e7e024ebe0seed-npm
```

No `docker rm`, `docker volume rm`, network removal, prune, chown, drain or restart is needed or authorized by this diagnosis. Any later residue requires a fresh exact-identity census and separate approval under the brief.

## Artifacts and verification disposition

Ignored evidence is under `C:\Antiphon\worktrees\card-task-efe949b4\.antiphon\fixture-gate-debug\`: `readonly-host-observation.txt`, `sdk-pack-metadata.txt`, `copied-pack-hashes.txt`, `c849-smoke-at-df6b422c.txt`, `card1333-stat.txt`, `original-probe.txt`, `original-filtered-probe.txt`, `candidate-probe.txt`, the probe scripts/projects, and the proposed patch. These generated payloads are not committed.

This task performed **0 Fixture reruns, 0 repository test-suite runs, 3 local diagnostic restores (2 expected failures, 1 pass), and 1 local diagnostic build (pass, 0 warnings/errors)**. No native Linux apphost execution or full Fixture pass is claimed. All owned commands completed. Standing container identity/start time remained `a6457bc895084a5cefbe5e13de377f90e60fb5693b9030604b20bf612b6451d0` / `2026-10-18T00:32:45.678349724Z` across observations. No inference about runner scheduling counters is made from that Docker observation.

--- next stage ---
next: Code
handoff: Repair c849_smoke's missing reference-pack pins with the prepared two-reference metadata override, add native mismatched-SDK regression coverage and retained failure diagnostics, then Review/land and qualify Fixture once on the explicitly selected repaired RC. Current Fixture remains failed. No cleanup or runner-state change is needed; CARD-1333 is not the cause.
artifact: docs/investigations/2026-10-19-rollout-fixture-gate.md
