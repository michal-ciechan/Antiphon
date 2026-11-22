# Why Windows qualification caught Git fixture teardown

## Introducing commits

The seeded Windows Full run `c9816b02` on `rc/2026-12-01` reported
`UnauthorizedAccessException` in `SourceRevisionStampContractTests.Two_heads_produce_identical_assembly_inputs`
in chunk `antiphon-all-group-123.trx`. The failure came from the fixture's
`finally` block. Git for Windows marks loose object files read-only, so the
recursive delete threw on an object file after the build comparisons had run.

The failing delete traces to CARD-1514's original SHA-stamp witness on the
assigned base. The advisory follow-up retained that teardown. The table records
the introducing commit of each replaced delete line in the assigned base
`ac8339c754a84ab53ca07708c091d4e79bd1fb5b`, using the landed history.

| Site at the assigned base | Introducing commit |
| --- | --- |
| `tests/Antiphon.E2E/CardCliE2ETests.cs:61` | `d7afa53e3fa79b06a1e80e3dd9760eb60544958b` |
| `tests/Antiphon.E2E/ContractSnapshotTests.cs:183` | `21866d557a8947d70931eff86edad01b6ec4ca49` |
| `tests/Antiphon.E2E/Fixtures/DelegationScratchRepo.cs:135` | `cb4cf6596b3238551ee4cf71e6b3b853b14315b7` |
| `tests/Antiphon.SessionRunner.Tests/C1441RunnerGitMaintenanceTests.cs:43` | `627082ad7bd8eb17aaa0d90bc3dea86b647eb5bd` |
| `tests/Antiphon.SessionRunner.Tests/C1441RunnerGitMaintenanceTests.cs:122` | `2f7722f541cff8891e92bbc64e440bca1c8fe1f7` |
| `tests/Antiphon.SessionRunner.Tests/C1677RetentionScratch.cs:252` | `9f2f929ce726ddbe145b27d131c28ec0e5bf955a` |
| `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceInspectionR3Tests.cs:63` | `58f62f5282e16c27d693c2f98f47936aab02d1e6` |
| `tests/Antiphon.Tests/Application/AgentTaskLandStageOutcomeTests.cs:845` | `e0385e9534e90fe85a849acbdac3c6566729937b` |
| `tests/Antiphon.Tests/Application/AgentTaskWorktreeContinuityTests.cs:315` | `f9eb0db45a975e1feb7961c5a1526719f0d23270` |
| `tests/Antiphon.Tests/Application/C0824SlotTransportWorld.cs:494` | `6ad3adf7b0324406a95a0ab5cd3593c0680eda51` |
| `tests/Antiphon.Tests/Application/C1677RetentionTransportWorld.cs:206` | `9f2f929ce726ddbe145b27d131c28ec0e5bf955a` |
| `tests/Antiphon.Tests/Application/CardReviewServiceIntegrationTests.cs:614` | `0be0dc4689f208b7c8df3bace1574b4636f83f47` |
| `tests/Antiphon.Tests/Application/DelegateCheckProbeTests.cs:1152` | `ede9b755f043ea60e72e0d006d0008cb096e2317` |
| `tests/Antiphon.Tests/Application/PhoneHomeMirrorJoinTests.cs:556` | `2e88288d0aff30ef482bfd8f95d458757ab4acdc` |
| `tests/Antiphon.Tests/Application/ProjectReadinessTests.cs:689` | `a6ae44088c7d1e96d1d4e724268e58c8b61a841d` |
| `tests/Antiphon.Tests/Application/ProjectSetupServiceTests.cs:293` | `733a4434b05bc67b1ae3b4b45577aaa145819ebc` |
| `tests/Antiphon.Tests/Application/ReviewLoopTests.cs:428` | `a8de709a750be9bb4a35c4211695a9aef935a74b` |
| `tests/Antiphon.Tests/Application/ReviewLoopTests.cs:611` | `ae98e17852a6cfef1240b6bfb0794dee9b516841` |
| `tests/Antiphon.Tests/Checkpoints/CheckpointWindowsAnchoredDeleteTests.cs:141` | `b8b417fb8f6b830b03fcc5b72fb887b15be13fa2` |
| `tests/Antiphon.Tests/Checkpoints/FixtureGitInProcess.cs:113` | `a48603b475ae3d0c2abc5b6a5c1449fb3fd0e49c` |
| `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:496` | `5baee470c225f6db33fa19495df275308eecc1d6` |
| `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:497` | `5baee470c225f6db33fa19495df275308eecc1d6` |
| `tests/Antiphon.Tests/Infrastructure/DindRunnerContractTests.cs:292` | `99a50a377a662668a9969707b1460e39794aaa12` |
| `tests/Antiphon.Tests/Infrastructure/GitDiffSpikeTests.cs:362` | `cfabe84bfd43977f05efe6286b46f47db086db8b` |
| `tests/Antiphon.Tests/Infrastructure/GitServiceTests.cs:277` | `1bf7439b7d49fcef189ff9ed5d089a89ec1f7537` |
| `tests/Antiphon.Tests/Infrastructure/ShellFixtureLineEndingContractTests.cs:196` | `4d8dc97f61a5dd21ee4b9492f7f9d544a165df7b` |
| `tests/Antiphon.Tests/Infrastructure/SourceRevisionStampContractTests.cs:147` | `193d6be2b7f52ac4ca6630a2fce6e9783c8c9cf9` |
| `tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs:1626` | `5d78f880bf722a2f58873e4ae071f5fd90e032c3` |
| `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:2426` | `f39f37c5c2c43bb82c20dc0f0c38f795bf9b674b` |
| `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:942` | `166630cde4daa3fd8a2bdb0fd91b121a241dac5e` |
| `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:994` | `ca5e51839698539c30545eb9cb5d88c14022bdde` |
| `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:1032` | `ca5e51839698539c30545eb9cb5d88c14022bdde` |
| `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:1130` | `62d881d8238b58ff549c11f3afdda0fff0c5edf3` |
| `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:2986` | `05b3a831dab6fec2fb79f27f35784e20b1e0862a` |
| `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:3069` | `05b3a831dab6fec2fb79f27f35784e20b1e0862a` |
| `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:3558` | `a9ed39fa09e306cf538c5244ac70d9c168daeea7` |
| `tests/Antiphon.Tests/TestHelpers/LandingFixtureGitInProcessTests.cs:171` | `1693349753bf02bb290ff1cafda8c763479703f6` |
| `tests/Antiphon.Tests/TestHelpers/RepairSourceWorld.cs:422` | `7284f89a38f024321bc2334b7c7368cf4b32347a` |

## Approving reviews

Interim Review `a1de8d74` ran the original witness on server2 at
`48c72753763b490dc962f45b4a9be6d8b8cae65b`. Run `20261120-164553-2553` recorded
eight passes in CP-1 `/*/*/SourceRevisionStampContractTests/*`, and the
two-heads method passed again in CP-8. Final Review `e4065157` completed the
deferred CP-3 PtyHost deployment check and retained the preceding Linux receipts.
The review reports are recorded in Git as `b6d954fe9d67024e2483593bfa1bf9123b631e2c`
and `c8a82df043bf20ee6191e7d4d472949270c019c4`.

## Operating-system coverage

The Linux selection ran the failing Unit test. Linux permits unlinking a
read-only file from a writable directory, so the same unsafe cleanup can pass
its assertions and teardown on Linux. The next Windows Full run owes
confirmation for the native failure.

## Pre-land check

`tests/**/*.cs` → `/*/*/TestFixtureCleanupGuardTests/*`

The guard runs in the Unit lane and detects raw recursive deletion in source
that references Git fixture creation. It reports file and line and points to
`ReadOnlyDirectory.Delete`. The fixture helper clears the read-only attribute
before attempting deletion. The source check can fail on Linux before a Windows
qualification run reaches the fixture.

## Related incident

[FT-2 read-only Git object teardown](2026-11-22-why-missed-rc-run-3528e6b1-ft2-readonly-git-objects.md)
covers the same Windows behavior in `Ft2Repo`.

