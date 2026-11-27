# Herdr fixture mutex mutation candidate

Code owner `555f2cc6-0ad2-4f4c-b722-31967ff1b052`. Bind this candidate to
the published source for SourceLanding Mutation.

| Control | Compiling change | Exact treenode filter | Expected red |
|---|---|---|---|
| PC-1/a | In `HerdrSnapshotFile.TryAcquire`, change `mutex.WaitOne(millisecondsTimeout)` to `mutex.WaitOne()`. Keep the acquisition flag and disposal logic. | `/*/*/HerdrLabelFollowSchedulingTests/Saved_snapshot_read_times_out_while_writer_holds_mutex` | The reader misses its one-second deadline while the helper thread owns the snapshot mutex. |

Run in `tests/Antiphon.SessionRunner.Tests`. Restore the bounded wait and
run the same method green. The Code stage's pre-fix failure is the
regression witness. The SourceLanding Mutation cycle is pending.
