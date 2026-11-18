# CARD-1978 unbound mutation candidate

Owner Code task: c099d1cf-69a2-4b12-a935-d88cc5668612.
Plan: docs/superpowers/plans/2026-11-18-card-1978-checkpoints.md.
No mutation cycle was executed; operator waived mutation checks. Ordinary Final
verification proceeds in the reopened task. The repository-wide runtime skip
census is omitted under the caller's SHORT instruction, not passed. This is not
a bound pack.

- PC-1 pending: remove RcBridgeProbePlatformTests.Windows_probe_reports_a_measured_count
  from WindowsUnitSkipRoster.WindowsOnly. The rc-probe-linux-skip case of
  scripts/test-checkpoint-placement-skip.ps1 must fail with skip_identity_unlisted.
  The baseline defect already demonstrated that ordinary red; it is not a deliberate
  mutant red/restore/green cycle.
- PC-2 pending: remove/replace the SkipOnLinux call in the Windows probe. Bind the
  existing C1210_Roster_closure_matches_source_gates witness if it detects this
  exact call-site variant; no receipt-only fixture detects that mutation. Missing
  control discovery remains with Mutation. The broad runtime census was explicitly
  excluded from this Code continuation by the caller's SHORT instruction.

The caller commissions SourceLanding Mutation after ordinary Code/Review. Bind
precise methods or named script cases and keep all pending variants explicit.
