# CARD-0824 S2 checkpoints

Code task ac92f48f. Repair task fa168f8a. CP-1 is the one provision-script
row. Build is n/a. The script test is the filter. EstimatedMinutes is 15.
The assertion floor is the green run count C0824S2 passed=281 failed=0
(build-slot lease f9b9f481, waited=0s, held=34s, load 6.39). Command-row
Min stays n/a.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | n/a | provision-script | `pwsh -NoProfile -File scripts/test-desktop-slot-provision.ps1` | D-1, D-2, D-3, D-9, S2 | exit 0; the releases-parent case exits 2 and creates nothing; C0824S2 passed=281 failed=0 | n/a | 15 |
