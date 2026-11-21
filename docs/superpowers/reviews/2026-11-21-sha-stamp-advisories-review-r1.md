# SHA-stamp advisories review, round 1 (Final/Full)

Subject: origin/feat/card-task-238fabb4 at f4d45a524d86258f3d092766acc05aa305135a72 (Codex author, task 238fabb4-da22-49f9-a30a-302bc4885a64). Replay parent 0870476de012359c4c2a6ef869bb68344931d8b9. Named base 3ed773829e584eec0c93ec8b47bf3517a69204d7. Fetched origin/master at review time faf7ff1b1206710886263dd3f32fdf9cd23d71a9 (the named base is an ancestor). Reviewer task 4380f903. Verdict: **CLEAN**. No blockers or regressions. Advisories and pending rows below do not hold the land.

No production server C# is in the diff. The stamp is read at the next rc restart. This review did not restart the live server.

## Fail-open and injection

Each of the five entry points lowercases `git rev-parse HEAD` and throws before interpolation unless it matches `^[0-9a-f]{40}$`:

- `dev-start.ps1` throws, then puts one double-quoted `-p:SourceRevisionId=` argument into the encoded server command.
- `dev-upgrade.ps1` throws, then both Release builds (the `--no-restore` build and the fallback) use that same value.
- `restart-server.ps1` throws before the toolchain-floor try, then passes one double-quoted property to `dotnet build`.
- `dev-aspire.ps1` uses `Write-Error` under `$ErrorActionPreference = 'Stop'`, so a bad SHA is terminating. The encoded child always builds with one quoted property. `-NoBuild` skips restore and npm only.
- `scripts/start-windows-phone-home-runner.ps1` throws before its build when the SHA is not 40 lowercase hex. `-NoBuild` does not build. It warns when the existing product version lacks `+[0-9a-f]{40}`, then launches. That warning is not the restart success path.

A value that already matched `^[0-9a-f]{40}$` cannot contain a space, quote, semicolon, or a second `-p:` fragment. The added script lines have no PowerShell 7-only `??` or `&&`. The two `?:` hits are .NET regex non-capturing groups.

`Resolve-AppHostLoadedIdentity` treats a non-40/64-hex `/api/version` value, including `unknown`, as `malformed`. `scripts/restart-apphost.ps1` exits 5 (`server build unverified`) unless the observed SHA equals the expected HEAD or `-ExpectedServerSha`. The phone-home warning cannot make that exit 0.

## Scratch mutant

Deleted only `-p:SourceRevisionId=$sourceRevision` from `restart-server.ps1`, then ran `scripts/test-toolchain-floor.ps1 -Case C1698_RestartServerUnchanged` under a build slot at load 13.96. `C1698 RestartServer same stop build start with a warning` failed because the traced build line had no property. 4 passed, 1 failed, harness exit 1. `git checkout -- restart-server.ps1` restored a clean tree. The green path for that case is the validated CP-9 receipt at the same SHA (ToolchainFloorWarningTests is in that filter).

## Receipts

Checkpoint tool run `20261121-202637-f80f` at load 11.70. `validate` accepted the four rows: `CHECKPOINT SOURCE VALID source=f4d45a524d86258f3d092766acc05aa305135a72 rows=4`. CP-9 was not re-run. Its existing receipt validated at the same SHA.

```
CHECKPOINT CP-5 commit=f4d45a524d86258f3d092766acc05aa305135a72 build=ok filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(DerivedImpactIndexTests*)|(ProcessSpawnLimitTests*)/* executed=168 passed=168 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-202637-f80f/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=f4d45a524d86258f3d092766acc05aa305135a72 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=f4d45a524d86258f3d092766acc05aa305135a72 build=ok filter=/*/*/(PtyHostDeploymentGuardTests*)|(ProcessSpawnLimitTests*)/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-202637-f80f/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=f4d45a524d86258f3d092766acc05aa305135a72 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=f4d45a524d86258f3d092766acc05aa305135a72 build=ok filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-202637-f80f/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=f4d45a524d86258f3d092766acc05aa305135a72 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=f4d45a524d86258f3d092766acc05aa305135a72 build=ok filter=/*/*/(DockerStackDocumentationTests*)|(LinuxServerRestartGateTests*)|(NightlyScriptsTests*)|(StandingPipelinePolicyDocumentationTests*)|(ToolchainFloorWarningTests*)/* executed=71 passed=71 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-193347-3beb/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=f4d45a524d86258f3d092766acc05aa305135a72 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=f4d45a524d86258f3d092766acc05aa305135a72 build=reused filter=/*/*/(LandGeneratedArtifactTests*)|(ReleaseImpactEnvelopeTests*)/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261121-202637-f80f/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=f4d45a524d86258f3d092766acc05aa305135a72 sourceState=clean buildSource=verified
```

CP-5 is the guard batch (168 executed, plan floor 111). Wall 27m18s, one build at a time, slot waits 0. CP-5 build 381s, tests 251s.

## Generated checks

- Derived index rebuilt, then `regenerate-derived-impact-index.ps1 -NoBuild` twice. Both printed `derived-impact-index unchanged`. The worktree stayed clean.
- `[Antiphon.Nightly.NightlyPolicy]::Read` policyHash `a4b5c2852d4a5c4504c9ddc10f4041ab332b5fdbd65b7a2c313fa6d77d726083`.
- `scripts/test-live-lane-run.ps1 -PrintInputDigest` equals the pin `aff88704a0be7c2b989b6149b08976853da2fdf094491fd5baef6a57deae858d`.
- `scripts/audit-land-verification-graph.ps1 -Check` approved, digest `A9BB27C595EA171463F0ED13725E198AA64385B1744B9E0BA2D211829C65A7CF`, count 80, exit 0.
- `scripts/antiphon evidence-diff --base 3ed773829e584eec0c93ec8b47bf3517a69204d7 --head f4d45a524d86258f3d092766acc05aa305135a72`: commits=9 entries=0 violations=0, exit 0.
- `git merge-tree --write-tree origin/master f4d45a524d86258f3d092766acc05aa305135a72` against fetched `faf7ff1b1206710886263dd3f32fdf9cd23d71a9`: exit 0, tree `d4d625d408290d368a98bca82a2ea89b7c029d3e`.
- OS-witness lint exit 0 for base `3ed77382` head `f4d45a52`. The only candidates are the unchanged phone-home `$IsWindows` refusal (line 40 both sides) and the pre-existing `LaunchAsync` Windows skip (base line 30, head line 56). `restart.required` is true because `dev-aspire.ps1` changed. The Windows native obligation is pending. That is the next rc restart, not a restart performed here.

The five stamp scripts and their direct tests are byte-identical from `68b64fa83306ba7d2e41d26ad1dd51dd275aedb1` through this tip. Historical CP-1 through CP-4 cover those bytes. This round re-ran the commissioned gap only.

## Pending (do not hold the land)

- PC-1 through PC-4 in `docs/superpowers/mutations/2026-11-21-sha-stamp-advisories-candidate.md`. Operator waived the mutation sweep. The one scratch mutant above is the review proof, not a SourceLanding discharge.
- Windows native pty-host hash compare (the production `LaunchAsync` path) and rc activation of the stamp. The portable C1022 tests hash assemblies and parse `+` 40-hex. They do not call `LaunchAsync`.

## Advisories (card them, do not hold the land)

- The committed plan's CP-6 still invokes `scripts/check-evidence-diff.ps1`, which is not on this tree. The successor `scripts/antiphon evidence-diff` passed with zero violations, and land-graph `-Check` plus the nightly pins passed beside it.
- `dev-aspire.ps1` `.PARAMETER NoBuild` says it skips the build. The switch skips restore and npm. The encoded AppHost command still builds with the stamp, and a bad SHA still refuses.
