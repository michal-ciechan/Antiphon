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
