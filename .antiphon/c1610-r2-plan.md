# CARD-1610 S2+S3 fix round 2 verification

Scope: the commissioned seven priority rows (CP-15/16/22/25/38/90/91), terminal release, receipts and visibility siblings, retention and capacity/workspace release. S2 and S3 ship together. This is a narrow repair selection, not a full Unit or native qualification.

## Performance budget

One shared isolated build; all rows serial, including timing-sensitive rows. Reuse the existing per-assembly shared PostgreSQL/template-schema fixture and per-test isolated schemas. Preserve checkpoint/TRX per-test and phase timing under .antiphon/checkpoints; unknown external phases remain unknown. Prior review executed these 14 rows (335 cases); this repair adds seven receipt cases (342 total). Estimates and the importer's 15-minute timeout floor are inherited/provisional CARD-1404 exceptions; bound collection to this run, do not broaden. Queue admission is separate (45-minute gate bound); total charged execution allowance is 30 minutes. No selection is expected to exceed 30 minutes.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-15 | S2-S4 | `tests/Antiphon.Tests -> bin-c1610-r2-final/` | server-unit-policy | `/*/*/TaskCeilingPolicyTests/C1610_*` | V-2 | 12 passed; no failed/skipped | 12 | 5 |
| CP-16 | S2-S4 | CP-15 | server-integration-hold-control | `/*/*/(TaskCeilingHoldTests*)\|(TaskCeilingControlTests*)/C1610_*` | V-3..V-10 | 49 passed; no failed/skipped | 49 | 3 |
| CP-17 | S2-S4 | CP-15 | server-integration-receipts | `/*/*/TaskSupervisionReceiptTests/C1610_*` | V-11..V-12 | 34 passed; no failed/skipped | 34 | 5 |
| CP-19 | S2-S4 | CP-15 | server-integration-visibility-final | `/*/*/TaskSupervisionVisibilityTests/C1610_*` | V-1 final source | 22 passed; no failed/skipped | 22 | 2 |
| CP-22 | S2-S4 | CP-15 | server-integration-AgentTaskOverdueDeadlineTests | `/*/*/AgentTaskOverdueDeadlineTests/*` | full affected class | 36 passed; no failed/skipped | 36 | 4 |
| CP-25 | S2-S4 | CP-15 | server-integration-TaskDeadlinePolicyTests | `/*/*/TaskDeadlinePolicyTests/*` | full affected class | 39 passed; no failed/skipped | 39 | 3 |
| CP-38 | S2-S4 | CP-15 | server-integration-TaskProgressStallSweepTests | `/*/*/TaskProgressStallSweepTests/*` | full affected class | 13 passed; no failed/skipped | 13 | 2 |
| CP-90 | S2-S4 | CP-15 | server-integration-CheckCompactionAutomaticRestartTests | `/*/*/CheckCompactionAutomaticRestartTests/*` | CARD-0079 full regression | 11 passed; no failed/skipped | 11 | 3 |
| CP-91 | S2-S4 | CP-15 | server-integration-CheckCompactionRecoveryPersistenceTests | `/*/*/CheckCompactionRecoveryPersistenceTests/*` | CARD-0079 full regression | 2 passed; no failed/skipped | 2 | 1 |
| CP-92 | S2-S4 | CP-15 | server-integration-supervision-retention | `/*/*/DataRetentionServiceTests/C1610_Unresolved_supervision_retains_evidence_until_told` | S3 retention safeguards | 6 passed; no failed/skipped | 6 | 2 |
| CP-93 | S2-S4 | CP-15 | server-integration-terminal-release | `/*/*/TerminalRunnerSeatReleaseTests/*` | Cancel post-commit seat release | no failed/skipped after fix | 41 | 4 |
| CP-94 | S2-S4 | CP-15 | server-integration-capacity-lock-order | `/*/*/CapacityLockOrderTests/*` | Cancel gate lock order | no failed/skipped | 17 | 3 |
| CP-95 | S2-S4 | CP-15 | server-integration-workspace-release | `/*/*/WorktreeRetirementRaceTests/*` | full Cancel post-commit workspace release sibling class | 54 passed; no failed/skipped | 54 | 5 |
| CP-97 | S2-S4 | CP-15 | server-integration-closed-watch-retention | `/*/*/DataRetentionServiceTests/C1610_Closed_report_watch_releases_evidence` | closed watch permits real retention pruning | 6 passed; no failed/skipped | 6 | 2 |

## Controls

Method-scoped diagnostic mutants: suppress CloseReportWatch (ended/legacy closure); remove report scan backoff; restore close-at-EndedAt (new delayed-upload witness); restore late-report outcome flip (three receipt modes); restore per-pass failure propagation (poisoned-row witness). All mutations must be restored before the single clean green checkpoint build/run.

## Interpretation

The no-growth alternative to CARD-0519 TerminalComplete uses existing ScanFrontier plus nullable ReportTranscriptStableSince. It requires at least 15 minutes after both EndedAt and the last observed post-exit frontier change. Complete reports close after forwarding; no report closes after the drain bound. Reports uploaded beyond a durably closed window are outside the guarantee. Existing closure assertions remain intact; only the ended arm advances 15 minutes, with a new initial-open assertion. Legacy and successor closure timing remains unchanged.

The working read stays authoritative: cross-evaluation caching can hide a newly persisted TurnEnd or catch-up. Its indexed one-statement query is measured by the 100-sample receipt witness; no cache/clock/deadline behavior changes are needed for this repair.
