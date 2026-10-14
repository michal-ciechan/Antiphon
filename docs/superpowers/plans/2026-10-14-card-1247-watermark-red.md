# CARD-1247 competing-window regression

The initial repaired full selection passed 591 cases at bbc54106df7daafc65385c150ebfbdf7c4494539.
Inspection found an adjacent claim consequence: both dispatchers register their direct trailing
watermark before ownership, so a loser with a longer text snapshot can consume text that the
winner never published. R-6 gates two different snapshots, lets the shorter source win, holds
its producer while the longer loser completes, then requires the initial answer and its tail.
Two direct variants: recovery off/catalog present, recovery on/catalog absent. No sleeps.

This is an ordinary regression against the initial repair, not a deliberate mutant. PC-8
(let a losing direct dispatcher advance the watermark) remains pending for SourceLanding Mutation.
The separate table avoids the checkpoint wait validator including an unused prior build's
stamp from a different source SHA; no generated receipt is changed to work around that refusal.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-10 | tail-red | `tests/Antiphon.Tests -> bin-c1247-caee-tail-red/` | competing-window | `/*/Antiphon.Tests.Application/(ChannelReplyLateConfirmDispatchTests*)/(A_losing_direct_dispatch_cannot_consume_text_it_did_not_publish*)` | R-6 | expected missing-tail assertion before repair | 2 | 5 |
