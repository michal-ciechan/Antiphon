# CARD-1721 unbound Mutation candidate

All controls below are **pending**. Original Code/landing owner: `1c7946d0-8231-429b-9c5b-c420711258ce`.
Plan: `docs/superpowers/plans/2026-11-05-card-1721-ghost-seat-followups.md`.
Code ran ordinary production-baseline witnesses, not deliberate mutants. SourceLanding Mutation owns binding to the landed source, method-scoped red/restore/green and missing-control discovery. Never treat these candidates or prior CARD-1719 Review controls as SourceLanding proof.

Use `tests/Antiphon.Tests`; each filter below is `/*/*/GhostSeatReleaseTests/<exact method>`. Methods with Arguments include their declared variants. Keep every PC cycle method-scoped; one build lease and one run lease per cycle, restore exact source paths, and keep evidence outside the SourceLanding snapshot. No timeout/assertion relaxation.

| PC / variant | Exact test method | Candidate guarded defect | Source path |
|---|---|---|---|
| PC-1 inherited | Clean_equal_failed_seat_is_released_after_the_idle_bound | Close clean/equal admission | server/Application/Services/GhostSeatRelease.cs |
| PC-2 inherited | Dirty_mirror_keeps_the_seat_and_one_attention_item | Treat dirty as clean/equal | server/Application/Services/GhostSeatRelease.cs |
| PC-3 inherited | Clean_equal_failed_seat_is_released_after_the_idle_bound | Change IdleMinutes to nine | server/Application/Services/GhostSeatRelease.cs |
| PC-4 transcript | Completed_followup_resets_idle_until_ten_minutes_after_last_activity | Omit LastTranscript from idle max | server/Application/Services/AgentTaskDispatcher.PoolSupervision.cs |
| PC-4 session | Completed_followup_resets_idle_until_ten_minutes_after_last_activity | Omit LastSeenAt from idle max | server/Application/Services/AgentTaskDispatcher.PoolSupervision.cs |
| PC-4 Codex | Completed_followup_resets_idle_until_ten_minutes_after_last_activity | Omit CodexTokenCountAt from idle max | server/Application/Services/AgentTaskDispatcher.PoolSupervision.cs |
| PC-5 dirty/unpushed/unknown | Mirror_changed_after_candidate_read_keeps_the_seat | Use the stale candidate RemoteSync record instead of the final current record | server/Application/Services/AgentTaskDispatcher.PoolSupervision.cs |
| PC-6 | One_sweep_releases_at_most_five_seats_and_the_next_sweep_drains_the_remainder | Remove the cap, or increase it above five | server/Application/Services/AgentTaskDispatcher.PoolSupervision.cs or server/Application/Services/GhostSeatRelease.cs |
| PC-7 | Held_attention_describes_conditional_release_and_preservation | Restore the old never-killed sentence | server/Application/Services/AttentionService.Seats.cs |
| PC-8 qualified | Automatic_coordinator_owns_both_hold_and_release_without_force_fallback | Suppress coordinator TryHandleTaskAsync in the retire sweep, leaving ownership exclusion | server/Application/Services/AgentTaskDispatcher.cs |
| PC-8 held | Automatic_coordinator_owns_both_hold_and_release_without_force_fallback | Bypass both coordinator ownership fences so held tasks reach the force path | server/Application/Services/AgentTaskDispatcher.cs and server/Application/Services/AgentTaskDispatcher.PoolSupervision.cs |

PC-8 held is a compound candidate because the retire sweep and ghost pass fence the same ownership independently. Mutation must establish reachability and the expected assertion; a fixture/transport error is not a red proof. No deliberate mutation has been commissioned in this Code stage.
