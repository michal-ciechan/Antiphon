# Task 6a432bb3 — rebase LandGraphAudit double-build fix

Code, Final. Branch `feat/card-task-6a432bb3`. Worktree `/work/worktrees/task-6a432bb3`.
Base `ed162195eb07f0be8ae79868a9119266467ea6f4`. Not a CARD-1514 implementation.
Source fix: Debug `4cefd387`, `origin/feat/card-task-4cefd387` tip `5ce8d2f8a8235939e9b61f557d64146dd74bd491`.

## Outcome

LandGraphAudit is one MSBuild project instance in a solution build. The test reference keeps `SkipGetTargetFrameworkProperties="true"` and no longer sets `SetTargetFramework`. The solution entry is closed with `EndProject`. Master's C# pre-check stays; `PinnedMetadata` no longer pins `SetTargetFramework`.

## Resolution

Cherry-picked with `-x` onto the task branch (never rebased):

| SHA | Role |
|---|---|
| `98b8ca15bd4d92d86146c78b5ae5bedaa9bd496e` | Red witness (`f83142404ed88b62bcb361e90c037aea68ddc6c0`) |
| `36aef93a6ca207e9e19b7663120d3afd010926a1` | Fix (`89e75a46afa7051cee50de1a1a11d8f235540197`), conflict resolved |
| `4506862d59b4632fa133907e6078b6c1fe95ed77` | Assertion repair (`a600abcbe3e180f29f466e29baa397b994b1aa8a`) |
| `c6d9652efe651d26ce33d1c1fa88805cda59d6dd` | Why-missed note (`98a16e60a4679206d697543d2057887c256dc167`) |
| `6321d80db687b6ec42c384cb4ff4446ec9228706` | Fresh land-graph refresh (script commit) |

Skipped `722068cc1398aa965126bccf1c41220468b0bca2` (stale refresh), `68ac094aa7729f7f150fcbd869f6a300e78a76cd` (debug report), `5ce8d2f8a8235939e9b61f557d64146dd74bd491` (conflict note).

The fix commit conflicted only in `scripts/audit-land-verification-precheck.ps1`. Kept master's wrapper (`bc96c71bef39761b4dbc5c05e1030312396a0e16`). Dropped `projectReference["SetTargetFramework"]` in `tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs`. The pin that remains is `SkipGetTargetFrameworkProperties=true`. Any `SetTargetFramework` value, child element, or Remove refuses. `CopyStagedHelpers` is unchanged.

`scripts/land-graph.ps1` does not exist. Check is `scripts/audit-land-verification-graph.ps1 -Check`. Refresh is `scripts/land-graph-land-refresh.ps1`. No `-Update` by hand.

## Records

```
old-digest=3BE0F9C518C0B4A9F92CCCAB63B26452F832F728483F66AB92F2C1D720C7D075
new-digest=3ED4A38CAA52947D313A1BAD14BA15203DD7F261A219BBC20704CB13E329EB46
old-count=75
new-count=75
old-pins=7a2586e58bd4768648c6f670f6f2c4432f68936a
new-pins=7a2586e58bd4768648c6f670f6f2c4432f68936a
changed-records: Antiphon.sln, tests/Antiphon.Tests/Antiphon.Tests.csproj
```

`-Check` on the code tip exited 0 (`approved`, same digest, count 75). The first record-test rerun after the refresh still embedded the old digest (`LandVerificationBuildGraph.Fingerprint` is the compiled resource). Rebuilt `tests/Antiphon.Tests` (lease `5442adc8-8522-428d-b932-6a09e71062be`, waited=0s, held=214s, 0 errors) and reran only those 10 methods: 10 passed, 0 failed.

`tests/test-execution-policy.json` is unchanged. No migration.

## Tests

Tested code SHA `6321d80db687b6ec42c384cb4ff4446ec9228706`. TUnit via `dotnet run --project tests/Antiphon.Tests`, `UseAppHost=false`, `TEMP=/tmp`, one build-slot lease per command.

| Run | Result | Lease |
|---|---|---|
| Witness at red SHA | 1 failed, 0 passed | build `a76ec04f-030d-4aa8-9e9e-1093746a2410` waited=0s held=304s; run `ca801a04-1ab0-4a36-8e19-71acf17994be` waited=0s held=20s |
| Touched classes at `c6d9652efe` (before refresh) | 113 total, 103 passed, 10 failed. Witness passed. All 17 `C1483_PinnedHelperMetadataRefusesOtherShapes` passed. The 10 failures were stale embedded digest `3BE0F9C5…` vs tree `3ED4A38C…` | build `eb2fe7dc-183a-4686-ae49-09f494192c3d` waited=0s held=189s; run `62f0d538-8473-44a0-a6dd-bf38f1ba8672` waited=0s held=192s |
| Same 10 after embed rebuild | 10 passed, 0 failed | `21df8ec0-d3f0-44be-a9d5-e807b14b0df6` waited=0s held=78s |
| `LandGraphEvaluationTests*` | 155 total, 154 passed, 1 failed | `c01461ef-3512-48ac-81be-68728e737532` waited=0s held=682s |
| Batched guards | 77 passed, 0 failed | `f7318ba6-b14b-4abe-a61a-4ccead867907` waited=0s held=165s |

Guard filter: `TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*`, `MechanicalPcContract*`, `TaskPlatformGuidanceTests*`, `C1729LayeringGuardTests*`.

The pre-refresh 10-method rerun (lease `708a30b5-b3ac-483b-bfe0-b702461d1bac`, 10 failed) is not green evidence. It used `--no-build` binaries from before `6321d80`.

### Inherited red

`EvaluationBudgetRefusesSlowDotnet` expected `evaluation-failed: evaluating Antiphon.sln exceeded 1 s` and got `refused` / `audit refused: land graph pre-check refused` (35s). The test, `scripts/audit-land-verification-graph.ps1`, and `scripts/audit-land-verification-precheck.ps1` are unchanged versus the task base. The wrapper from `bc96c71be` starts `dotnet` on `PATH`. The test's shim sleeps 30s and prints `refused` for every argument other than `--version`, so the pre-check never reaches evaluation. The pin diff does not add a `dotnet` call. Not changed.

### Local mutants (formal PCs stay pending)

1. Put `SetTargetFramework="TargetFramework=net10.0"` back on the LandGraphAudit `ProjectReference`. Witness `--no-build`: 1 failed, 0 passed (`TargetFramework=net10.0`). Lease `5e66048e-a534-4825-b10f-54c81cad568f` waited=15s held=12s. Restored with `git checkout -- tests/Antiphon.Tests/Antiphon.Tests.csproj`.
2. Restored `projectReference["SetTargetFramework"] = "TargetFramework=net10.0"` and rebuilt (lease `f6ebcde9-1bf4-4f32-9816-0c2cb1ce2d1b` waited=45s held=150s, 0 errors). `C1483_PinnedHelperMetadataRefusesOtherShapes(stf-literal)` failed: exit 0, expected 1. The other 16 shapes in that filter still passed. Lease `79223ff1-4a71-4c33-a4b3-8344abb27a87` waited=0s held=10s. Restored with `git checkout -- tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs`. Tree clean before the race builds.

## Ten solution builds

Cleaned `tools/Antiphon.LandGraphAudit` default outputs first (lease `7ea17167-abd9-4cd2-81b1-f332ae33dcb9` waited=30s held=4s). Then 10 `dotnet build Antiphon.sln -p:UseAppHost=false`, each its own lease. Host budget was occupied=4/4, so overlap was 4, not 10.

Every log has one `Antiphon.LandGraphAudit ->` line. CS2012 = 0. MSB4018 = 0.

| Log | waited | held | solution errors |
|---|---|---|---|
| 01 | 345s | 42s | 0 |
| 02 | 195s | 164s | 0 |
| 03 | 360s | 35s | 0 |
| 04 | 330s | 27s | 0 |
| 05 | 255s | 73s | 0 |
| 06 | 360s | 36s | 0 |
| 07 | 15s | 234s | 0 |
| 08 | 150s | 200s | 3 (NETSDK1047 x2 reported, MSB3073 x1). LandGraphAudit itself printed once and its project build succeeded |
| 09 | 30s | 220s | 0 |
| 10 | 255s | 75s | 0 |

Build 08 is the known PtyHost `project.assets.json` race (`net9.0/linux-x64`) when overlapping solution builds copy the pty host. It is not a second LandGraphAudit instance.

## Other gates

- `scripts/check-evidence-diff.ps1 -BaseRef ed162195eb07f0be8ae79868a9119266467ea6f4 -HeadRef 6321d80db687b6ec42c384cb4ff4446ec9228706`: commits=5 entries=0 violations=0, exit 0. After this report was added, the same command against that later HEAD reported commits=6 entries=1 violations=0, exit 0.
- `git fetch origin master` then `git merge-tree --write-tree origin/master 6321d80db687b6ec42c384cb4ff4446ec9228706`: exit 0, tree `8fbce983e1d7b17e231c4b475e1d7b7821513ec9`. `origin/master` is `7f0a8bb1f8542d75bb9d8654d43b0c6fabefb2a6` (moved past the task base). No overlap with the fix files. Not merged. The report commit merges clean as well.
- OS-branch: csproj, sln, C#, tests, and the why-missed note are portable. This rebase has no plan file for the witness checker.
- Audit tool build for the refresh: lease `16a6a5d8-3580-4f16-a47b-361e22b9e6bf` waited=0s held=9s, 0 errors. Output `tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/`.

## Owed

Formal PC cycles stay pending for SourceLanding Mutation. The two local mutants above are the Code witness cycles, not that stage. No production restart. Nothing deployed. Next is ordinary Review round 1.
