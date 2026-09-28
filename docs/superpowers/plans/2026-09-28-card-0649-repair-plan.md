# CARD-0649 reviewed repair: final verification

## Verification design

- V-1: A runner brief pointer uses a relative staged path, and the queued path is the message-owned inbox path. The phone-home projection test reads the persisted row.
- V-2: A pointer that is within 1,024 UTF-8 bytes before queue binding but over the limit after binding is compacted by the dispatcher. The unit boundary test checks the actual bound text.
- V-3: The dispatcher pointer passes through staging, queue binding and a recorded complete UserPrompt with no second spill. The durable receipt test checks the persisted body and transcript text. The real ConPTY multi-line prompt contract is already covered by `SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn`; that Windows-only test is not Linux execution evidence for this round.
- R-1: The whole Unit lane and all three affected integration classes execute with zero failures. Platform skips are reported separately. The Windows-only native method remains a manual acceptance obligation on a Windows host; it is not counted as executed by this Linux round.
- PC-1 through PC-3: Pending method-scoped SourceLanding Mutation. Ordinary and nightly green do not discharge them.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c649-repair/` | unit | `/*/*/*/*[Category=Unit]` | V-2, R-1 | whole Unit lane, 0 failed; platform skips reported | 3400 | 12 |
| CP-2 | all | CP-1 | affected-integration | `/*/*/(PhoneHomeTaskDispatchProjectionTests*)\|(DurableRunnerSpillReceiptTests*)\|(PhoneHomeSpillTransportTests*)/*` | V-1, V-3, R-1 | all three classes, 0 failed/skipped | 20 | 12 |

### Cost

Ordinary checkpoint floor: 24 minutes. The CP-2 row reuses CP-1's isolated build. Both rows run through the build-slot checkpoint driver.
