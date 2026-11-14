# CARD-1901 verification budget

Measurement task `7555b555`, 2026-11-14. Measurement-only work; no CARD-1901
implementation or live retirement operation is authorized by this report.

## Provenance

- Plan: `origin/feat/card-task-2c3ecf9c`, commit `3ef663e98`;
  `docs/superpowers/plans/2026-11-14-card-1901-retirement-audit-plan.md`.
- Current master observed using `git ls-remote origin refs/heads/master`:
  `8c06c7e11f40cc224235ecbc68c2389913cc67ee`.
- Fresh detached measurement worktree: `/tmp/card1901-7555b555-master`, at that
  exact master commit. The assigned report branch starts at the same commit.
- Host: Linux x86_64, kernel `4.15.0-213-generic`, 24 available processors,
  .NET SDK `10.0.404`. The shared host is oversubscribed (CARD-1906).
- Raw evidence and local measurement drivers:
  `/tmp/card1901-7555b555-evidence`. Generated receipts, logs, TRX and phase
  journals are external to Git. Every driver uses `scripts/build-slot.ps1`;
  tests run serially via `dotnet run --project tests/Antiphon.Tests --no-build
  -p:UseAppHost=false`. Native default build output is retained for reuse.
- Admission is measured separately from lease-held execution. Load columns use
  `/proc/loadavg`; one run is not a p95. Uninstrumented phases remain unknown.

## Measurement status

Cold build and the closed existing-test selections are being measured. This
initial evidence slice records the immutable source and execution approach;
it does not claim completed measurements or passing verification.
