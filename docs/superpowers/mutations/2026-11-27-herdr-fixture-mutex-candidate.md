# Herdr fixture mutex mutation candidate

Original landing owner `c75ed5c7-4557-4017-be25-77ede14d0b63`. Bind this
candidate to the published source for SourceLanding Mutation.

| Control | Compiling change | Exact treenode filter | Expected red |
|---|---|---|---|
| PC-1/a | In `HerdrLabelFollowFixture.TryReadSaved`, change `mutex.WaitOne(millisecondsTimeout)` to `mutex.WaitOne()`. Keep the release and disposal logic. | `/*/*/HerdrLabelFollowSchedulingTests/Saved_snapshot_read_times_out_while_writer_holds_mutex` | The reader misses its one-second deadline while the helper thread owns the snapshot mutex. |
| PC-2/a | In `HerdrLabelFollowFixture.WaitAsync`, move `if (matched) return;` above the expiry check immediately after the attempt. Keep the pre-attempt check and bounded read. | `/*/*/HerdrLabelFollowSchedulingTests/Saved_snapshot_poll_expires_under_contention_and_rejects_late_success` | The late attempt returns the saved label at expiry and the assertion requiring cancellation fails. |

Run in `tests/Antiphon.SessionRunner.Tests`. Restore each change and run
its exact method green. The ordinary pre-fix failures are the regression
witnesses. Both SourceLanding Mutation cycles are pending.
