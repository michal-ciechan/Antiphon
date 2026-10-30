# CARD-1613 fix round 3

Outcome: the three commissioned repairs are on `feat/card-task-f5557ddb` and the Linux proof is green. Native Windows CP-9, CP-10, and CP-11, and four policy rows the native comparison never reached, stay owed.

Verified source for every test below is `2e18b8af559414bda1a494ced63b69c7c93ad99f`. This report file is the only later change.

## Commits

- `1ab9fa2fadb6211e4a155032942bb927ba0e9ffb` test: repair native Windows probe and checkpoint stamp checks. Cherry-pick `-x` of `88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce`. Conflict in `NativeLinuxPolicyExecutionTests.cs` kept both intents: the Linux Windows-shaped TRX byte compare, and the native Windows roster, definition, skip-reason, and portable-sentinel checks. Method name is now `Native_child_observes_platform_outcomes_and_side_effects`.
- `3cbb70648151c4f13014c0968015c5ca89418f23` fix: pin the native Linux fixture target and admit its text fixtures.
- `2e18b8af559414bda1a494ced63b69c7c93ad99f` fix: regenerate the land-graph approval after the fixture pin.

`-Update` ran once. It was not run again.

## Land-graph defects

Red at the start tip, before the fix, filter `/*/*/LandGraphEvaluationTests/(C1483_S4_RealRepositoryApproves*)|(C1483_S5_CheckRequiresEvaluationRecord*)`: 2 executed, 2 failed, 0 skipped.

- `C1483_S4_RealRepositoryApproves`: exit 1, `refused: tests/Antiphon.Tests/Antiphon.Tests.csproj Target KeepNativeLinuxPolicyFixturesInMainAssembly (missing pin)`.
- `C1483_S5_CheckRequiresEvaluationRecord`: real `-Check` exit 1, document refusal on `Fixtures\NativeLinuxPolicy\*.cs.txt` (`R1-overload.cs.txt`), `audit refused: land graph pre-check refused`.

Fix, without weakening the audit:

- Pin `tests/Antiphon.Tests/Antiphon.Tests.csproj` target `KeepNativeLinuxPolicyFixturesInMainAssembly` at `6D9A062D0FD97CCE58DAA045F09C62E8D281BD97022F0FA9138A255B078A6B61` (whitespace-normalised OuterXml, same hasher as the existing pins).
- Exact allow-list pattern `tests/Antiphon.Tests/Fixtures/NativeLinuxPolicy/*.cs.txt`.
- Literal `EmbeddedResource` Update glob, not `@(EmbeddedResource)`. `WithCulture="false"` and `Culture=""` are pinned attribute values only. They are not on the general EmbeddedResource metadata list. The include carries the same two attributes plus `LogicalName`.

After that source commit, real `-Check` failed only because the recorded digest was stale: printed `digest=E117DDECAC45785183AEF8BBE6818DB4DB85A6EC89B9EAD065C1047BF0EDD49B count=68`, then `manifest does not match the selected graph`.

`-Update` once: exit 0, `approved`. Old digest `20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6` count 66. New digest `E117DDECAC45785183AEF8BBE6818DB4DB85A6EC89B9EAD065C1047BF0EDD49B` count 68. Evaluation SDK `10.0.403`. Pins blob `8d956c09c603c2eb806f26cf6053575d8823c1eb`.

Proving `-Check` after the record commit: exit 0, `approved`. Tracked files identical before and after. No canary left.

## Class runs at 2e18b8af

Own slot, `LandNonCodeSkipTests`, `/*/*/LandNonCodeSkipTests/*`, output `bin-c1613-r3-land/`, `UseAppHost=false`: total 111, failed 0, succeeded 111, skipped 0. Lease `2a680253-fa4e-4106-b39a-52f881687660`.

Own slot, `/*/*/(LandGraphEvaluationTests*)|(LandGraphAuditCleanupTests*)/*`: total 135, failed 0, succeeded 135, skipped 0. That selection includes `C1483_S4_RealRepositoryApproves` and `C1483_S5_CheckRequiresEvaluationRecord`. Lease `09c0a953-6f33-429b-b3e3-c334e0b47aeb`.

## Linux checkpoints

Run `20261030-001944-8739`. Plan `docs/superpowers/plans/2026-10-29-card-1613-pinned-linux-skip-reflection-plan.md`. Rows CP-1, CP-2, CP-3, CP-4, CP-5, CP-6, CP-7, CP-8, CP-12, CP-13. `--serial --expected-source-sha 2e18b8af559414bda1a494ced63b69c7c93ad99f`. Exit 0. `sourceState=clean`, `buildSource=verified`. Receipts under `.antiphon/checkpoints/20261030-001944-8739/`. The tool deleted its own `bin-c1613-ant/`, `bin-c1613-runner/`, `bin-c1613-pty/`, and `bin-c1613-agents/`.

| Row | executed | passed | failed | skipped |
|---|---:|---:|---:|---:|
| CP-1 | 3 | 3 | 0 | 0 |
| CP-2 | 10 | 10 | 0 | 0 |
| CP-3 | 2 | 2 | 0 | 0 |
| CP-4 | 4 | 4 | 0 | 0 |
| CP-5 | 4 | 4 | 0 | 0 |
| CP-6 | 4 | 4 | 0 | 0 |
| CP-7 | 17 | 17 | 0 | 0 |
| CP-8 | 23 | 23 | 0 | 0 |
| CP-12 | 3 | 3 | 0 | 0 |
| CP-13 | 3 | 3 | 0 | 0 |
| Total | 73 | 73 | 0 | 0 |

## Owed natively

Windows CP-9, CP-10, and CP-11 were not run on this tip. The native verify of the repair commit, on the previous tip, was CP-9 4 passed, CP-10 1 passed, CP-11 2 passed (run `20261029-223252-7dd2`). A final native confirmation of this tip is still owed.

Four policy rows were not run at either revision in that comparison:

- Antiphon.SessionRunner.Tests `CompletedCardWorkspaceRemovalTests` (`C1032_InspectionR3Mode` and `C1032_RemovalLinuxIgnoredFifoRefuses`)
- Antiphon.SessionRunner.Tests `HostStatsProbeLiveTests.Linux_probe_reads_live_proc`
- Antiphon.PtyHost.Tests `LinuxCgroupCustodyTests`
- Antiphon.Agents.Pty.Tests `UnixPtyArgvTests`

No native tests were run here. The desktop checkout was not read.
