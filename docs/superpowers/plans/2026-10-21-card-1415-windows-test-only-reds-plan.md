# CARD-1415 Windows test-only reds (rc/2026-10-20 qualification)

Source: `docs/investigations/2026-10-21-rc-2026-10-20-windows-qualification-reds.md`
(commit `92c62bcc`), causes B, D, E, F and G. Test-only; no production file changes.

## Slices

| Slice | Cause | Change |
|---|---|---|
| S1 | B | `AdvisoryChangePolicyTests.CorrectionWorld.CreateAsync` skips with a named reason before any schema/temp directory when the untracked `04228bff` producer pack is absent from the git common dir. Covers the 5 AdvisoryChangePolicyTests reds and the PipelineSpeed correction cases that share the world. |
| S2 | D | `AgentTuiLaunchResolverTests` Codex High literal `gpt-6-sol` -> `gpt-6.1-sol` (matches `ModelLevelAliases.ForCodex` and `docs/agent-kinds.md`). |
| S3 | E | `C1213_SourceLandingKeepsLegacyIdentity` remote snapshot arm skips on Windows after the local arm (RemoteVerificationWorkspace admits POSIX runner paths only). |
| S4 | F | `KeepSixReminderTests`, `KeepSixSettingsTests`, `KeepSixShortfallTests` share `[NotInParallel("KeepSix")]`. |
| S5 | G | `a_git_timeout_fails_one_task_not_the_tick` skips on Windows (the sh sleep hook does not delay `git worktree add` there). |

## Verification design

Linux runner only. S3, S5 change behaviour only on Windows and S4 guards a packing race;
their Linux rows prove the tests still run and pass here, not the Windows fix. CP-1 expects
skips on a clone without the producer pack, so it runs alone and yields no clean receipt.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S5 | `tests/Antiphon.Tests -> bin-c1415-pack/` | advisory-pack-skip | `/*/*/AdvisoryChangePolicyTests/*` | S1 | 5 executed and passed plus 5 correction cases skipped with the CARD-1415 reason on a clone without the pack (Min counts executed only), 0 failed | 5 | 7 |
| CP-2 | S1-S5 | `tests/Antiphon.Tests -> bin-c1415/` | codex-high-literal | `/*/*/AgentTuiLaunchResolverTests/ModelTier_maps_against_the_resolved_profile_kind_and_omits_raw` | S2 | 1 passed, 0 failed/skipped | 1 | 6 |
| CP-3 | S1-S5 | CP-2 | c1213-legacy-identity | `/*/*/AgentTaskDispatchBaseGuardTests/C1213_SourceLandingKeepsLegacyIdentity` | S3 | 1 passed on Linux (remote arm runs), 0 failed/skipped | 1 | 1 |
| CP-4 | S1-S5 | CP-2 | keep-six-classes | `/*/*/(KeepSixReminderTests*)\|(KeepSixSettingsTests*)\|(KeepSixShortfallTests*)/*` | S4 | 22 passed, 0 failed/skipped | 22 | 2 |
| CP-5 | S1-S5 | CP-2 | git-timeout-hook | `/*/*/AgentTaskDispatchFailureTests/a_git_timeout_fails_one_task_not_the_tick` | S5 | 1 passed on Linux, 0 failed/skipped | 1 | 1 |

Not verifiable here: S3/S5 Windows skips and S4 under Windows group-12 packing; the next
Windows qualification is the witness. No PCs; Mutation has none to bind.
