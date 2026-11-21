# Group-47 pipe-timeout witness

Debug for rc run 3528e6b1 chunk `antiphon-all-group-47`. The success-path git
drain must fail with `git_pipe_timeout` when a grandchild never closes stdout,
and an 8-second holder must still succeed.

Positive controls: this plan has no PC-n row. None are discharged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c4699-pipe/` | pipe-timeout | `/*/*/LandingGitTests/(HungPipeHolderFailsWithGitPipeTimeout*)\|(C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget*)` | pipe bound and the finite holder | 0 failed, 0 skipped, executed at least 2 | 2 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
