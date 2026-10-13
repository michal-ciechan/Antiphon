# CARD-1205 S5 recipient acceptance

Code/landing owner: c0e86d50-00e6-4942-b303-7bc44122c640.
Branch: feat/card-task-c0e86d50. Base: 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2.
S5 only; the explicit brief excludes the Unit lane. No production or shared-fixture
changes are authorized. The frozen ordinary census is CP-5=30 and CP-6=4.

The first test-only group implements T37 in both cache modes. Actual adapter bytes
enter runner custody and HTTP/runtime persistence. Its expected defect is PC-84
(suppressed Sync recovered-boundary wakeup), owned by later SourceLanding Mutation.
Run the witness on unchanged pre-S5 production first; record its actual outcome.
The S1/S2 repair already landed, so baseline red is not assumed.

Extra setup: one build-slot gated checkpoint-tool bootstrap into
bin-c1205-s5-tool/, UseAppHost=false. No full-assembly, deployment or native PTY run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| S5-BASE | Tests | `tests/Antiphon.Tests -> bin-c1205-s5-base/` | five-note-baseline | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/C1205_Restart_delivers_five_gap_held_completions_once*` | T37,V-20,R-5 | Two executed on unchanged pre-S5 production; report actual outcome | 2 | 6 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1205-s5/` | restart-receipts | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/*` | V-20..24,V-36..40,R-5 | Exactly 30 executed, all named cases green | 30 | 16 |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c1205-receipts/` | receipt-compatibility | `/*/*/(VerificationRoundDeliveryTests*)\|(TestClassificationGuardTests*)/(C544_CompletionReceipt*)\|(C544_StampIsNotReceipt*)\|(Registry_matches_compiled_metadata*)` | R-8,R-9 | Exactly four named methods green | 4 | 7 |

All PC-84..100 variants remain pending. No deliberate mutant is authorized here.
