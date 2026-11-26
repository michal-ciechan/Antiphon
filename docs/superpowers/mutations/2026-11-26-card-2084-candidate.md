# CARD-2084 candidate controls

These method-scoped controls are pending for SourceLanding Mutation. Use
the landed-source snapshot and its restored green receipts.

| Control | Compiling change | Exact treenode filter | Expected red |
|---|---|---|---|
| PC-1/a | Omit the `releaseSessionId` argument in `PhoneHomeCommandDispatcher.ReleaseSlotAsync` so a release for another session waits for pending placement. | `/*/*/PhoneHomeCommandDispatcherTests/Unrelated_slot_release_does_not_wait_for_stuck_linux_placement` | The unrelated release returns after the placement gate ends and fails the pending-placement assertion. |
| PC-2/a | In `PhoneHomeCommandDispatcher.MutateAsync`, exempt a release even when its session ID equals the pending launch ID. Keep the unrelated release exemption. | `/*/*/PhoneHomeCommandDispatcherTests/Unrelated_slot_release_does_not_wait_for_stuck_linux_placement` | The release for the launching session is already complete while placement remains pending. |
| PC-3/a | Remove only `_clock.GetTimestamp() < Deadline` from `LinuxPlacementAttempt.CanProceed`. This is M5 from Review `d2c7cfba`. | `/*/*/Card2060PlacementHandshakeTests/SlowFirstPlacementSucceedsAndStuckPeerExpires` | A valid PLACED at equality gets GO while the timer callback is still pending. |
