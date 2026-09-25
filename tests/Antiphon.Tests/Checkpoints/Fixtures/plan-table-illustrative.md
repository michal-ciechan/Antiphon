### Checkpoints

Illustrative example — the class, group and path names below are invented to show the two columns, not lifted from any plan:

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-ex/` | sample-surface | `/*/*/ExampleSurfaceTests/*` | V-1, R-1 | all 3 methods, 0 failed/skipped (between them they assert 12 named rows) | 3 | 9 |
| CP-2 | S2 | n/a | client-lint | `pwsh -File scripts/test-client.ps1 -Lint` | V-2 | 0 errors, 0 warnings | n/a | 2 |
