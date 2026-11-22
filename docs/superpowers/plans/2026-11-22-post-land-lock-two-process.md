# Post-land lock two-process witness

Repair for review round 1 of the post-land lane lock. The sequenced busy test stays.
The new witness is two OS processes calling production `EnterLock` on one absent lock.

## Verification design

Closed rows for this repair. One build, three filters. The scratch mutex-removed
mutant is unlisted and is not a row.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | repair | tests/Antiphon.Tests -> bin-c217e-lock/ | post-land-lock | `/*/*/PostLandEarlyWarningTests/Two_processes_publish_exactly_one_owner` | R-1 | the two-process witness, 0 failed | 1 | 12 |
| CP-2 | repair | CP-1 | post-land-class | `/*/*/PostLandEarlyWarningTests/*` | R-2 | the whole class, 0 failed | 23 | 8 |
| CP-3 | repair | CP-1 | spawn-guards | `/*/*/(ProcessSpawnLimitTests*)\|(TestLaneCategoryGuardTests*)/*` | R-3 | both guard classes, 0 failed | 58 | 4 |
