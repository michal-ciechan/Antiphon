# CARD-1259 continuation recovery qualification

Continuation `d636249e` starts at `41289ba905c64a99a12bd42369fbaa74987bfdc3`.
The main verification manifest remains authoritative for CP-3..CP-13 and V/R.
These additional rows reproduce the four production gaps explicitly assigned to
this continuation. They do not replace the routing or lifecycle matrices.
Both use the full, bounded recovery class (six argument-expanded executions).
No whole Unit or full assembly run is authorized by this supplement.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-14 | recovery-red | tests/Antiphon.Tests -> bin-c1259-recovery-red/ | recovery-red | /*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* | V-5,V-6,V-10,D-4 | all 6 executed; intended guarded assertions red | 6 | 7 |
| CP-15 | recovery-green | tests/Antiphon.Tests -> bin-c1259-recovery-green/ | recovery-green | /*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* | V-5,V-6,V-10,D-4 | all 6 executed, 0 failed/skipped | 6 | 7 |
| CP-16 | recovery-green | CP-15 | recovery-adjacent | /*/Antiphon.Tests.Application/(SessionRuntimeActionTests*)\|(ChannelReplyLateConfirmDispatchTests*)\|(ChannelReplyDurabilityTests*)\|(DataRetentionServiceTests*)/* | V-3,V-4,V-8,V-9,V-10,R-3 adjacent | all 146 listed, 0 failed/skipped | 146 | 10 |
| CP-17 | base-confirmation | tests/Antiphon.Tests -> bin-c1259-base-six/ | inherited-six | /*/Antiphon.Tests.Application/(ChannelReplyLateConfirmDispatchTests*)\|(ChannelReplyDurabilityTests*)/(The_measured_transport_fixture_dispatches_the_error_through_the_runtime*)\|(A_late_confirmed_api_withheld_turn_does_not_warn_as_an_unpublished_reply*)\|(A_late_confirmed_no_reply_turn_does_not_warn_as_an_unpublished_reply*)\|(A_runtime_batch_redispatches_a_late_confirmed_channel_reply_once*)\|(A_failed_late_confirm_publication_is_durably_uncertain_and_leaves_it_owed*)\|(A_reply_the_live_path_already_published_is_not_published_again_by_the_grace_confirm*) | verify exact CP-16 failures at base 41289ba9 | all 6 executed; preserve actual failures | 6 | 7 |
| CP-18 | recovery-green | CP-15 | routing-partial | /*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/* | V-6,V-11 partial | all 14 executed, 0 failed/skipped | 14 | 7 |
| CP-19 | recovery-green | CP-15 | task-settlement-adjacent | /*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/* | task-attempt settlement and parent delivery regression | all 334 executed, 0 failed/skipped | 334 | 12 |
| CP-20 | base-confirmation | CP-17 | inherited-runtime | /*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/Deferred_is_durable_and_releases_runtime | verify exact CP-19 failure at base 41289ba9 | one executed; preserve actual outcome | 1 | 2 |

The task-attempt cases must leave the replacement Working with no result/events;
the direct cases require exact original recipient/body after restart or failed
tail send; queue pruning must hold the actual transcript mutation lease;
diagnostics must expose identity/count/age/frontier/phase without transcript text.
Method-scoped deliberate mutants remain the SourceLanding Mutation stage's work.

CP-16 is the full affected-class regression selection for this recovery slice:
SessionRuntimeActionTests 28, ChannelReplyLateConfirmDispatchTests 20,
ChannelReplyDurabilityTests 25, DataRetentionServiceTests 73. The shared queue
claim, runtime phase and retention changes justify this additional bounded row.
It does not discharge any missing routing matrix or the main final CP-3..CP-13.

CP-18 is an explicitly incomplete routing slice, not CP-3 or CP-6: eight V-11
variants use ReviewThreadService, a real queue attempt, and a hosted pump receipt.
The atomic-comment case covers rollback before commit; its post-commit/action-ack
cut, legacy migration, enqueue-failure H0-H4 and fresh redispatch remain pending.
The six V-6 cases cover working/Herdr/rules eligibility and stale generation,
including the complete streamed receipt after release, task-attempt changes in
bound/catch-up actions, and new debt admitted while the actual queue flush is
held. Working also exercises the capped-attempt row after becoming eligible.
The other three routing methods and the main 35-case requirement remain
pending. The original eight-case CP-18 run remains historical (7 passed, 1 fixture
failure: a 100-character prefix did not retain the 200-character identity window).
The repaired fixture proves identity and incompleteness before the real attempt.
The inherited failure confirmation uses a detached, clean base checkout; its
source remains frozen independently of work on the assigned continuation branch.

CP-19 bounds the newly affected task settlement surface: the opt-in database save
fence and shared already-persisted fallback must preserve existing task results,
attempt identity and parent delivery. It runs the complete named class (334 cases
observed in fresh TRX; the initial static count of 182 omitted matrix expansion),
not an assembly or Unit lane. No shared impact has
been declared qualified beyond these bounded selections; full Final remains open.
