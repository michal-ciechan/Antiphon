# Standing operator rule repair controls

Original Code and landing owner `43a985de-8ff2-4fda-8ed6-197a667864ea`.
Bind these candidates to the confirmed landed SHA. Each control remains pending for
SourceLanding Mutation, including restoration and the identical green rerun.

| PC | Source change | Exact method filter | Expected failure |
|---|---|---|---|
| PC-1 | In VerifiedSessionStopService, remove the session row's exclusive dispatch lock. | `/*/*/VerifiedSessionStopTests/Stop_binds_to_the_verified_unattached_and_unowned_seat` | The reassigned case observes no wait on the warm claim's row or signals the runner before the claim commits. |
| PC-2 | In SessionTerminalAttachments, let an existing browser subscriber coexist with stop admission. | `/*/*/VerifiedSessionStopTests/Stop_binds_to_the_verified_unattached_and_unowned_seat` | The browser-attached case fails its conflict and no-command checks. |
| PC-3 | In VerifiedSessionStopService, admit Herdr attached and unknown runner attachment. | `/*/*/VerifiedSessionStopTests/Stop_binds_to_the_verified_unattached_and_unowned_seat` | The Herdr case reaches the runner. |
| PC-4 | In VerifiedSessionStopService, reject a missing owner even when the expected owner is also null. | `/*/*/VerifiedSessionStopTests/Stop_binds_to_the_verified_unattached_and_unowned_seat` | The idle unowned case cannot get a confirmed exit receipt. |
| PC-5 | In VerifiedSessionStopService, treat a successful command acknowledgment as an exit on readback. | `/*/*/VerifiedSessionStopTests/Stop_binds_to_the_verified_unattached_and_unowned_seat` | The unconfirmed runner case gets success while still running. |
| PC-6 | In BlockedNote, omit the structured recommendation from the caller header. | `/*/*/AgentTaskReplyIntegrationTests/Structured_blocked_note_reaches_the_caller_once_with_parking_disabled` | The delivered caller prompt loses the recommendation. |
| PC-7 | In AgentTaskReplyService, restore parking-only durable note admission for unprofiled Blocked reports. | `/*/*/AgentTaskReplyIntegrationTests/Structured_blocked_note_reaches_the_caller_once_with_parking_disabled` | The Blocked report commits without the caller obligation, so crash and failed enqueue cannot recover its prompt. |
| PC-8 | In VerifiedSessionStopService, omit the expected generation check before sending the runner command. | `/*/*/VerifiedSessionStopTests/Stop_binds_to_the_verified_unattached_and_unowned_seat` | The stale-generation case observes a runner command. |

The Mutation stage owns missing-control discovery.
