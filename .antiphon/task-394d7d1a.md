# Review round 1 (diff-only): CARD-1627 build-slot cap warning and docs

Subject: origin/feat/card-task-4aa6e6f8 tip d4345c226ecef44b3aca333ac3d56a4ed55d7f70, range
3a49b60133327f4441f06698ef3369b1d277d049..d4345c226ecef44b3aca333ac3d56a4ed55d7f70 (5 commits). Verdict: CLEAN, no defects.

## (a) dotnet path, env export, stream, exit codes
- dotnet path byte-identical (diff proof): `Get-AntiphonBuildSlotCapWarning` returns `$null` when
  `Test-AntiphonDotnetExe` matches token 0 (scripts/lib/build-slot.ps1:226). `$exportedMaxCpuCount` stays
  `$null`, `$capRestore` stays `$null`, and `Exit-AntiphonBuildDriverEnvironment -Restore $null` is a no-op.
  argv still comes from the unchanged `Add-AntiphonMaxCpuCount`. An inherited `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT`
  passes through untouched, as before. Witness row 4 checks that dotnet gets no warning and no exported value.
- Env collision: no other repo code reads `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT`. Non-dotnet runs save and restore it
  (absence is kept). Run under a real outer lease (outer value 6), the harness still saw its own grant of 3,
  because Set-C589Seams clears it.
- Stream: the warning uses Write-Host and lands on stdout. A live probe saw it between the existing
  `BUILD SLOT granted` and `released` lines, and nothing went to stderr. That is the same stream and prefix as
  the lines callers already tolerate. Callers that capture the output of a wrapped non-dotnet command:
  c590-remote.sh:1575/2977 (`>/dev/null`), :5341 (anchored `^C660_ROW` grep), test-build-driver-environment.ps1
  (anchored `PROBE `), test-isolated-helper-build.ps1 (dotnet, so no warning). None is corrupted.
- Exit code: a wrapped `bash -c '...; exit 7'` returned 7.

## (b) Witnesses (scratch git-archive copies, one lease per run)
- C1627 harness case at 4c05e8231: 3 passed, 2 failed (the warning row and the env row). Tip: 5/5.
- Mutants, all red: m1 drop export (env row FAIL); m2 warning also fires for dotnet (dotnet row FAIL);
  m3 export for dotnet (dotnet row FAIL); m4 -NoSlot exports '4' (NoSlot row FAIL).
- Doc witness at db6fbdc6b: all 4 files lack "never one lease around" (red 4/4). At the tip, all 4 pass.

## (c) Docs, bundle budget, embedded bundle
- No contradiction in living docs or skills. testing-and-build.md:1552 is about a single roster driver.
  The other hits are historical plans and investigations.
- Bundle budget: delegate-basics grew 6469 -> 6673 chars (+204). Measured worst case
  (InstructionBundleTests C1332, Orchestrator/Custom): argv 27,879 against early cap 28,000 (headroom 121),
  reserve 1,621, transport 2,121. It fits. DelegateLaunchArgvIntegrityTests are Windows-only (7 skips here).
- C1083_EmbeddedBundlesMatchSourceAndRenderedHeaders passed after the rebuild.

## (d) 405
c590-remote.sh:998-999 writes BUILD_SLOTS_SHA12 only when it is absent. broker_sha12 (:968-972) and the temp
env (:9559) reuse the pin, and docker-compose.server2-runner.yml:168 tags the broker image by it. Repo routes:
BuildSlotRoutes.cs:58 MapDelete `{leaseId:guid}` and :69 MapGet `/metrics` (eacfcf7e4, CARD-1489).
Live read at 2026-11-02 13:06Z: `GET $ANTIPHON_BUILD_SLOTS_URL/metrics` gave 405 with `Allow: DELETE`.
This confirms the claim as far as the repo shows.

## (e) Runs (HEAD d4345c226, native output, each through build-slot.ps1)
- Build tests/Antiphon.Tests: 0 errors, 4m08s, one lease.
- BuildSlotScriptTests: 20 total, 18 passed, 2 skipped (Windows).
- StableBuildOutputDocumentation 40, InstructionBundle 78, DelegateLaunchArgvIntegrity 8, TaskPlatformGuidance 5,
  CheckpointManifestDocumentation 8: 139 total, 132 passed, 7 skipped (Windows), 0 failed.
- 22 further classes that read the changed docs (AdvisoryChangePolicy, AgentBundleAttachment,
  BlockedTaskParkProjection, CheckpointRepeatDocumentation, CommitOnSettleDocumentation, D2AdvisoryDocumentation,
  DelegateBundleLaunch, DockerStackDocumentation, GrokRulesComposition, InstructionFileStamp, InstructionFragment,
  MechanicalPcContract, OrchestratorInstructionsGuidance, PostLandMutationContract, PostLandRetrospectiveContract,
  ReleaseBranchModelDocumentation, RepairSourceDocumentation, RolePolicyToolBundle,
  RunnerBranchContractDocumentation, RunnerPushCredentialDocs, ScopedVerificationInstruction,
  StandingPipelinePolicyDocumentation): 232/232 passed.
- The worker's count of 187 (180+7) does not match the named five classes (139). Their exact class set is not
  recorded.
- Land graph -Check (native Release tool via -ToolPath): approved, count=69,
  digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE. Canary removed. -Update not run.
- check-evidence-diff: 5 commits, 0 entries, 0 violations, exit 0.
- merge-tree: clean onto origin/master f5283ad08 (tree 6cbf15e55) and onto origin/rc/2026-11-01
  (tree 282de55a3). Master-only per brief.

## Advisories
- A-1: "MSBuild (SDK 10.0.403) honours no environment variable for its node count"
  (docs/testing-and-build.md:1154, scripts/lib/build-slot.ps1:219) is overstated. Microsoft.Build.dll 10.0.403
  contains `MSBUILDNODELIMITOFFSET` (a scheduler node-limit offset), and Microsoft.Build.Framework.dll contains
  `MSBUILDCOORDINATORNODEBUDGET`. Neither was measured here. The warn-only design still meets the card DoD
  ("or at least warn"). A follow-up could measure them.
- A-2: Only 121 chars of headroom remain under the C1332 early cap (28,000). The next growth of delegate-basics
  or orchestrator will trip InstructionBundleTests.
- A-3: Every leased non-build harness now prints the loop warning, for example the documented recipe at
  docs/testing-and-build.md:286 and harnesses headed "Run through build-slot.ps1". It is noise only.
- A-4: scripts/fixtures/seed-smoke-targeting-packs.ps1:1 and scripts/test-fake-provider-layout.ps1:5 still
  prescribe one outer lease around several builds. Both pass -maxcpucount:1, and neither is a living doc.
- A-5: Card DoD "metrics endpoint answers on the deployed broker" is still open: the operator has to bump the
  pin. Card items 4 and 5 (Low) are not addressed.

Not run: the whole Unit lane. It is outside the commissioned diff-only selection, so ordinaryScopeCompleted
is Interim. PCs: none; method-scoped SourceLanding Mutation stays pending.
