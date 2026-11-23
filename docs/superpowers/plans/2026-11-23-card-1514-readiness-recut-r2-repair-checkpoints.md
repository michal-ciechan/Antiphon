# CARD-1514 readiness re-cut: Review 5bfb258d round 2 repair

Code task 74b3af9d repairs the two round 2 blockers of Review 5bfb258d on top of the re-cut tip `eefad7d1802defd9043980ca9e985438f50c3291` (Code 84561585, plan [re-cut checkpoints](2026-11-23-card-1514-readiness-recut-checkpoints.md)). No production code changes.

- R-6 (blocker 1, CP-12): `TestTimingSession.OperationLabels` in `tests/Shared/TestTiming.cs` now lists the two static `ProjectReadinessCollationTests` phase labels `C-locale readiness database` and `C-locale readiness database tip`, so the recorder no longer writes `omitted` for them. `TestTimingOperationLabelTests.C1620_EveryCallSiteOperationLabelIsKept` guards it.
- R-7 (blocker 2, CP-10): `WindowsUnitSkipRoster.WindowsOnly` lists the two master-inherited Linux skips of `GrokRulesLaunchRefusalTests` (Herdr and PtyHost unsafe raw-rules refusals, the `cmd.exe` fixture), count one each, so the Linux receipt qualifies them instead of failing `skip_identity_unlisted`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-10 | r2 | `tests/Antiphon.Tests -> bin-r2r/` | grok | `/*/*/GrokRulesLaunchRefusalTests/*` | R-7 | 3 passed, 0 failed; the 2 Linux skips are roster-listed | 5 | 4 | true |
| CP-12 | r2 | CP-10 | timing | `/*/*/(ProjectReadinessCollationTests*)\|(TestTimingOperationLabelTests*)/*` | R-6 | all listed, 0 failed/skipped | 7 | 5 | true |
| CP-15 | r2 | CP-10 | roster | `/*/*/WindowsUnitPlatformAdmissionTests/*` | R-7 | 0 failed; any skip is roster-listed | 5 | 4 | true |
| CP-16 | r2 | CP-10 | exclusion | `/*/*/ReleaseGatePlatformTests/LinuxOnlyWindowsExclusionPolicy` | R-7 | 1 passed, 0 failed/skipped | 1 | 6 | true |
| CP-17 | r2 | CP-10 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-5 | 0 failed/skipped | 1 | 4 | true |

CP-10 and CP-15 may record roster-listed skips. Qualify them with `pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json> -ExpectedSourceSha <tip>`. The checkpoint tool's `validate` verb refuses every skip, so rows with skips do not count for that verb.
