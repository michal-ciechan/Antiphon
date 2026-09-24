# CARD-0647 durable runner spill follow-up

The queue row owns the remote spill bytes and runner-relative file name. The runner
checks the name against the row identity before writing. A new server process reads
the same row on every Input retry. The existing queue and receipt logic remain the
authority for terminal acceptance.

## Ordinary verification

- V-1: a queued brief survives courier replacement and reaches the runner file.
- V-2: two briefs for one busy session retain distinct bodies and file identities.
- V-3: a failed Input retries with the same identity and bytes.
- V-4: a complete matching UserPrompt is confirmed with `QueuedReceiptAssertions`.
- R-1: the existing server Grok credential and task projection classes.
- R-2: the runner command dispatcher and Grok auth probe classes.

The brief caps execution at one isolated build and these named selections, including
new tests. The ordinary Unit lane and other integration classes defer under this cap.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
| --- | --- | --- | --- | --- | --- | --- | ---: | ---: |
| CP-1 | repair | `tests/Antiphon.Tests -> bin-c647/` | spill-receipt | `/*/*/DurableRunnerSpillReceiptTests*/*` | V-1, V-2, V-3, V-4 | all DurableRunnerSpillReceiptTests methods, 0 failed | 6 | 2 |
| CP-2 | repair | `tests/Antiphon.SessionRunner.Tests -> bin-c647/` | spill-write | `/*/*/RunnerWorkspaceServiceTests/Spill_write*` | V-1 | Spill_write_stays_inside_runner_cwd and Spill_write_refuses_a_symlink_that_escapes_the_mirror executed, 0 failed. Dangling final-file and directory symlink tests run on Linux and skip when Windows denies symlink creation | 2 | 1 |

## Pending post-land controls

- PC-1: remove the persisted body assignment; V-1 must fail.
- PC-2: key the lookup by session only; V-2 must fail.
- PC-3: change the retry identity; V-3 must fail.
- PC-4: replace the complete UserPrompt matcher with a weaker receipt; V-4 must fail.

All PC variants remain for SourceLanding Mutation after ordinary Review and land.
