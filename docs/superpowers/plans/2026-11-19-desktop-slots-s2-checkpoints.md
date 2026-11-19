# CARD-0824 S2 checkpoints

Code task ac92f48f. CP-1 is the provision-script row from
docs/superpowers/plans/2026-11-19-desktop-hot-worktree-slots-plan.md
at 61d4149c32d52b9286a9b4feeefb1e73096ffe5b. Build is n/a.
The script test is the filter. EstimatedMinutes is 15.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | n/a | provision-script | `pwsh -NoProfile -File scripts/test-desktop-slot-provision.ps1` | D-9, S2 | exit 0; the releases-parent case exits 2 and creates nothing | n/a | 15 |
