# CARD-1259 final continuation checkpoint supplement

Task 65a75ada, base d603a85cb1fc8bed0c37cade149233b44f8d7a24.
The original verification manifest remains authoritative for CP-3..CP-13.
These additional bounded rows reproduce and repair the two direct-tail findings.
The red row must execute assertion failures, never build/fixture errors.
No whole Unit lane or assembly; no runner/platform pin. Estimated wall minutes
exclude slot wait. Every PC-1..PC-113/variant remains pending SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-21 | tail-red | tests/Antiphon.Tests -> bin-c1259-tail-red/ | direct-tail-red | /*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/(C1259_uncertain_direct_roots_cannot_starve_a_later_recoverable_tail*)\|(C1259_terminal_direct_tail_catches_up_before_closing*) | starvation, terminal closure and final suffix custody | five intended assertion failures | 5 | 6 |
| CP-22 | tail-green | tests/Antiphon.Tests -> bin-c1259-tail-green/ | direct-tail-green | /*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/* | all recovery guards, terminal generation and incomplete snapshot guards | all 11 pass | 11 | 6 |
| CP-23 | tail-green | CP-22 | direct-tail-adjacent | /*/Antiphon.Tests.Application/(ChannelReplyLateConfirmDispatchTests*)\|(ChannelReplyDurabilityTests*)/* | complete existing direct/late-confirm classes | all 45 pass | 45 | 5 |
