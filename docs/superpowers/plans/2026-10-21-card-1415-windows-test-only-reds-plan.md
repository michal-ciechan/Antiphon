# CARD-1415 Windows test-only reds (rc/2026-10-20 qualification)

Source: `docs/investigations/2026-10-21-rc-2026-10-20-windows-qualification-reds.md`
(commit `92c62bcc`), causes B, D, E, F and G. Test and policy only; no production file changes.

## Slices

| Slice | Cause | Change |
|---|---|---|
| S1 | B | `AdvisoryChangePolicyTests.CorrectionWorld.CreateAsync` skips with a named reason before any schema/temp directory when the untracked `04228bff` producer pack is absent from the git common dir. Covers the 5 AdvisoryChangePolicyTests correction cases and the 4 PipelineSpeed methods that build or call them. |
| S2 | D | `AgentTuiLaunchResolverTests` Codex High literal `gpt-6-sol` -> `gpt-6.1-sol` (matches `ModelLevelAliases.ForCodex` and `docs/agent-kinds.md`). |
| S3 | E | `C1213_SourceLandingKeepsLegacyIdentity` remote snapshot arm skips on Windows after the local arm (RemoteVerificationWorkspace admits POSIX runner paths only). |
| S4 | F | `KeepSixReminderTests`, `KeepSixSettingsTests`, `KeepSixShortfallTests` share `[NotInParallel("KeepSix")]`. |
| S5 | G | `a_git_timeout_fails_one_task_not_the_tick` skips on Windows (the sh sleep hook does not delay `git worktree add` there). |
| S6 | Review 54253a76 D-1 | Narrow first: the C1213 remote arm becomes `C1213_RemoteSourceLandingKeepsLegacyIdentity` so the local arm stays required on Windows; the pack-free delivery half of `C1281_WiredRoundtrip` becomes `C1281_WiredRoundtripDelivery`. |
| S7 | Review 54253a76 D-1 | Four method-scoped `profiles.rc.exclusions` rows (owner CARD-1415), policy hash recomputed: all platforms for the 9 pack-dependent methods (`AdvisoryChangePolicyTests` C1303_R12 x3 args, C1281_ReceiptCorrection, C1281_IncompleteNeverClean; `PipelineSpeedIntegrationTests` C1281_B1Authorization, C1281_B1ReceiptImport, C1281_B1CorrectionTransaction, C1281_WiredRoundtrip); `platform: windows` for the C1213 remote arm and the git timeout test. `test-rc-platform.ps1 -Case C1415_SkipExclusions` (TUnit `ReleaseGatePlatformTests.C1415_SkipExclusions`) proves the census. |

## Verification design

Linux runner. Every row below selects only cases that execute here, so every row validates
(0 skipped). S3/S5 change behaviour only on Windows and S4 guards a packing race; their Linux
rows prove the tests still run and pass here, not the Windows fix. The policy rows are proved
offline by CP-6 and the C1323 policy case; their Windows effect is witnessed by the next
Windows qualification.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S7 | `tests/Antiphon.Tests -> bin-c1415r/` | advisory-pack-free | `/*/*/AdvisoryChangePolicyTests/(C1281_AdvisoryDoesNotRerun*)\|(C1281_SeparateDocsIdentity*)\|(C1281_NarrativeBoundary*)\|(C1281_CommentEquivalence*)\|(C1281_HistoryPreserved*)` | S1 | 5 passed, 0 failed/skipped | 5 | 3 |
| CP-2 | S1-S7 | CP-1 | codex-high-literal | `/*/*/AgentTuiLaunchResolverTests/*` | S2 | >= 14 executed (whole class), 0 failed/skipped | 14 | 2 |
| CP-3 | S1-S7 | CP-1 | c1213-arms | `/*/*/AgentTaskDispatchBaseGuardTests/(C1213*)` | S3, S6 | 6 passed (local and remote arms run on Linux), 0 failed/skipped | 6 | 3 |
| CP-4 | S1-S7 | CP-1 | keep-six-classes | `/*/*/(KeepSixReminderTests*)\|(KeepSixSettingsTests*)\|(KeepSixShortfallTests*)/*` | S4 | 22 passed, 0 failed/skipped | 22 | 2 |
| CP-5 | S1-S7 | CP-1 | dispatch-failure | `/*/*/AgentTaskDispatchFailureTests/*` | S5 | >= 14 executed on Linux (whole class), 0 failed/skipped | 14 | 2 |
| CP-6 | S1-S7 | CP-1 | release-gate-platform | `/*/*/ReleaseGatePlatformTests/*` | S7 | 3 passed (C1323 x2, C1415_SkipExclusions), 0 failed/skipped | 3 | 6 |
| CP-7 | S1-S7 | CP-1 | pipeline-speed-runnable | `/*/*/PipelineSpeedIntegrationTests/(C1281_AdmissionHolds*)\|(C1281_B1Schema*)\|(C1281_B2*)\|(C1281_B3*)\|(C1281_WiredDeliveryRecovery*)\|(C1281_WiredRoundtripDelivery*)\|(C1288*)\|(C1289*)\|(C1303*)` | S6 | 35 passed (19 methods, the 4 pack-dependent ones left out), 0 failed/skipped | 35 | 12 |

Not in the receipt (documented by-design skips, run separately as a diagnostic): the 9 pack-dependent
identities, `/*/*/AdvisoryChangePolicyTests/(C1303_R12*)|(C1281_ReceiptCorrection*)|(C1281_IncompleteNeverClean*)`
and `/*/*/PipelineSpeedIntegrationTests/(C1281_B1Authorization*)|(C1281_B1ReceiptImport*)|(C1281_B1CorrectionTransaction*)|(C1281_WiredRoundtrip)`,
skip with the CARD-1415 reason on a clone without the producer pack. They run nowhere until a
tracked fixture replaces the pack.

Policy validation outside the table (pwsh harness, no build): `Read-NightlyExecutionPolicy`
hash check, `scripts/test-nightly-tests.ps1 -Case C1397_RcAntiphonLaunchGroups` and
`-Case C1404_RcCostBalancedGroups` (both resolve the shipped rc antiphon exclusions for Windows).

No PCs; Mutation has none to bind.
