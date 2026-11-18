# CARD-1978 unbound mutation candidate

Owner Code task: c099d1cf-69a2-4b12-a935-d88cc5668612.
Plan: docs/superpowers/plans/2026-11-18-card-1978-checkpoints.md.
No mutation cycle was executed; operator waived mutation checks. Ordinary Final
verification and the source-census witness are incomplete. This is not a bound pack.

- PC-1 pending: remove RcBridgeProbePlatformTests.Windows_probe_reports_a_measured_count
  from WindowsUnitSkipRoster.WindowsOnly. The rc-probe-linux-skip case of
  scripts/test-checkpoint-placement-skip.ps1 must fail with skip_identity_unlisted.
  The baseline defect already demonstrated that ordinary red; it is not a deliberate
  mutant red/restore/green cycle.
- PC-2 pending: remove/replace the SkipOnLinux call in the Windows probe. Bind the
  required source-closure witness after its implementation; no current receipt-only
  fixture detects this call-site mutation. Missing-control discovery remains with
  Mutation. Code must complete the ordinary source-census acceptance first.

The caller commissions SourceLanding Mutation after ordinary Code/Review. Bind
precise methods or named script cases and keep all pending variants explicit.
