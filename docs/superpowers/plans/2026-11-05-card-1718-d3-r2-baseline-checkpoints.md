# CARD-1718 historical negative witnesses

Run once against untouched committed `0b7f9070e95e57b7d265663436478339530f3121` in a detached,
task-owned historical worktree. This is the test-only descendant of Review base 249d4654d;
the production bytes of the affected files are unchanged from that base. Its committed tests
precede fix f47be567b. No old task workspace is changed. Red assertions, not build or fixture
failures, establish guarded defects. This is additional baseline verification explicitly required
by the resume brief. Deliberate mutants remain SourceLanding Mutation's work.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | baseline | tests/Antiphon.SessionRunner.Tests -> bin-c1718-r2-red-runner/ | historical-runner-defects | /*/*/(RunnerWorkspaceIgnoredContentFloorTests*)\|(C1677RetentionEngineTests*)/(Disposable_override_cannot_release_a_secret_shaped_name*)\|(Ignored_secret_files_survive_admission_on_dry_run_and_execute*)\|(Phase_b_keeps_an_ignored_secret_planted_after_the_move*)\|(Residue_retry_keeps_an_ignored_secret*) | B-1, B-2, B-3 | expected assertion failures | 23 | 8 | true | TEMP=/tmp |
| CP-2 | baseline | tests/Antiphon.Tests -> bin-c1718-r2-red-server/ | historical-server-and-transport-defects | /*/*/(WorktreeIgnoredContentClassifierTests*)\|(C1677RetentionServerTests*)/(Both_classifiers_protect_the_secret_floor_under_a_disposable_override*)\|(Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay*) | B-1, B-2, B-3 | expected assertion failures | 40 | 10 | true | TEMP=/tmp |

Budget: provisional 18 minutes plus admission, 25-minute execution/45-minute wall ceiling. These
are the exact historical witness methods, not a full-assembly retry or a mutation run.
