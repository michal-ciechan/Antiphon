# Why Windows Full group 141 missed caller-wake gate dispositions

Windows Full run `1fe763a9` on `rc/2026-12-06` at `0cfc4ac1ffb41f5af159a0c1db423fe119950267` reported two failures in `antiphon-all-group-141.trx`. `EveryLiveGateHasDisposition` reported an unmapped gate count. `ExplicitGatesAreNeverFullRequired` named both Boolean arguments of `CallerWakeTests.Adopted_native_recipient_reads_its_report`. Both inventory methods passed in `b8cbfb24` at `2fd61b643ea4231fa732cd4969b5284d43b33f39`.

The Linux scan of the previous pin found 148 gates, all mapped. The failing pin found 151 gates against the same 148-row catalog. Both scans had zero unresolved shapes. A build-slot run at the failing pin reproduced both Windows assertions on Linux (2 executed, 2 failed).

| New case | Introducing commit | Disposition |
|---|---|---|
| `Adopted_native_recipient_reads_its_report(false)` and `(true)` | `f78a12d92194d7c1f7e461600f47852a20d34089`, published in the caller-wake merge `fccb17b2b2d755ab0ebfdbbbad2b51cb8ef2be29` | The explicit canary requires an installed Claude CLI and an opt-in local model stub. Both cases are excluded from automated Full by one method-scoped policy row. |
| `C2074_Native_caller_strips_every_inherited_provider_variable` | `23ce495220adeb362c1ea1ddf9c233c055a659b2` (CARD-2074) | The helper name `RealCliStubEnv` triggered the source scan. The test now shares the native canary's overlay setup and Full continues to require this offline witness. |

CARD-2056's headed lookup handling was present at both pins. The source scan reports `Explicit` and `RealCliStub` here. The Full census expects a policy exclusion even for an `[Explicit]` case, which the normal class filter cannot execute.

The Full census matches the method named in the new exclusion. The catalog writer originally reported the offline witness as excluded because its scanner treated the stub overlay helper as a gate and applied the policy reason by class. Shared fixture setup now keeps that offline witness out of the source gate roster. The catalog refresh produced 150 entries and zero unresolved shapes. The policy hash, live-lane input digest and legacy exclusion digest include the new policy row.

An intermediate scanner change at `4aa8329395d33161a9f47c845c47f43ae96ca68f` applied method matching to every gate and surfaced four older `PtyPasteMarkerExperiments` methods. Their class is `[Explicit]`, and the CARD-1545 plan requires the four local diagnostics in Full. The run passed 62 of 64 tests and failed both inventory checks on that separate discrepancy. The caller-wake repair now lives in policy data and fixture setup. [CARD-1545 lane plan](../superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md), [October 19 regate evidence](2026-10-19-rc-regate.md)

## Approving reviews

Review `ede19f30` (Final/Full) approved `9218d74123abfec3cb79d8ddad96b45f4d689793`. Its validated run `20261125-210349-5cbe` selected `BlockedReplyReportRetentionTests` and the `RequestedModelAlias*` tests (29), two `CallerWakeTests` receipt and claim witnesses (2), and the six-class guard (57). The two opt-in canary cases also passed separately with inherited provider variables unset. The inventory checks were outside the reported selection. The earlier two-case native receipt recorded the opt-in CLI run.

The CARD-2074 checkpoint plan selected the environment witness, its related native canary, migration and incident witnesses, and the six-class guard. The card was in Review when checked, and an approving review of that change was unavailable. [CARD-2074 checkpoint plan](../superpowers/plans/2026-11-25-card-2074-checkpoints.md)

## Outside the selection

Both inventory methods are platform-independent Unit tests under `tests/Antiphon.Tests/Scripts`. The Linux six-class guard and short Merge fixed subset excluded them by selection. The previous path-dependent Merge selector targeted other affected tests. The native canary and its environment witness could pass independently of the catalog.

## Cheapest pre-land check

Select `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)|(ExplicitGatesAreNeverFullRequired*)` for a change to `tests/live-lanes.json`, the scanner, or test-source diff lines with a gate marker. The Merge impacted-classes table now selects this pair. Both methods run the source inventory in process. The class's other methods include compiled discovery and a scratch Git witness, which the short pre-land guard can avoid.

The Windows follow-up is the same pair on the next integrated candidate, or `/*/Antiphon.Tests.Scripts/LiveLaneInventoryTests/*` when the group is rerun. The next Windows qualification still owes that receipt.

## Exemption

None. The earlier catalog gap and explicit coverage gap established the two checks. This instance needs a method-scoped exclusion on a mixed class and a pre-land selection for both checks. [Group 138 gate dispositions](2026-11-22-why-missed-g138-live-gate-dispositions.md), [Full 557cdb63 coverage gaps](2026-11-23-why-missed-557cdb63-full-coverage-gaps.md)
