# Phone-home replacement and retry Mutation candidates

Round 2 Code and landing owner is `3ba3f373-25a0-443b-a40f-e38c8733aab5`. The first repair was Debug `1a845af9-5198-45c4-b9bd-7abe966e5cbe`. The [Round 2 plan](../plans/2026-11-27-phone-home-deadlock-round-2.md) lists ordinary verification.

All controls are pending for SourceLanding Mutation. Binding is required after Code. Inspect each failed assertion and restored green result with the exact method filter.

| ID | Variant | Candidate defect | Expected failure | Method filter |
|---|---|---|---|---|
| PC-1 | blocking-close-under-gate | In `AcceptConnectAsync`, after publishing the replacement and before leaving the directory gate, synchronously wait for `previous.DisposeAsync("superseded")`. | The old close stays held and the open replacement cannot pass the bounded directory read. The fixture releases its old socket in cleanup. | `/*/*/PhoneHomeQueuedTurnTests/Open_replacement_recovers_and_confirms_queued_prompt_while_old_socket_closes` |
| PC-2 | await-close-before-notification | In `AcceptConnectAsync`, start the tracked cleanup and await it before `Notify` and before returning the connection. | The held close prevents the second eligibility notification and subsequent List recovery. | `/*/*/PhoneHomeQueuedTurnTests/Open_replacement_recovers_and_confirms_queued_prompt_while_old_socket_closes` |
| PC-3 | uncapped-hint | In the retry calculation, add the parsed hint to the jitter without bounding the hint contribution. | Both the one-hour delta-seconds and HTTP-date arm a delay over 30 seconds. All four cases fail the timer assertion. | `/*/*/PhoneHomeConnectionServiceTests/Oversized_retry_after_arms_a_timer_of_at_most_thirty_seconds` |
| PC-4 | stale-disconnect | In `Disconnect`, drop the live connection identity check. | An old Disconnect drops the recovered replacement and its eligibility. | `/*/*/PhoneHomeQueuedTurnTests/Open_replacement_recovers_and_confirms_queued_prompt_while_old_socket_closes` |
| PC-5 | swallow-close-handshake-fault | In `PhoneHomeLiveConnection.CloseSocketAsync`, consume the unexpected handshake exception without recording it. Leave socket abort, disposal and logging in place. | The superseded epoch's warning contains the dispose fault and omits the handshake fault. The shutdown test fails its handshake-fault assertion. | `/*/*/PhoneHomeDirectoryTests/Shutdown_drains_a_held_superseded_close_and_observes_its_fault` |
