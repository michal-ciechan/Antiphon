# CARD-1247 bound grace dispatch and atomic direct ownership

Repair of Final Review 4457e553 F1/F2 from 47e2ded61fca7ff767c76c3e36e495b9442a26d9.
Original landing owner: 4c42a269-3d07-482f-ad01-bc90793f25ec; repair Code caeeae9a.

The grace worker must receive the confirmed correlation identities and select their completed
prompt windows, independently of the newest turn. Broker I/O stays outside the queue semaphore.
Direct publication must atomically claim the existing source settlement marker, respecting
the durable delivery linkage used by capture and the source locks used by loss recording.
Only the owning completed producer invocation may release a failed direct claim. No claim
timeout, assertion relaxation, migration or second ownership store is planned.

## Verification design

- V-1 / T1: pause a real enqueue's grace worker, complete a newer Channel turn, release the
  worker and require the literal earlier answer first; the later trigger publishes its own answer.
  Four variants cover recovery on/off and catalog present/absent. Must fail before repair.
- V-2 / T2: gate grace and the runtime's real turn-end path immediately before direct ownership;
  require one correctly addressed reply. Default-off/catalog and recovery-on/catalog-less.
  Must fail before repair. The gate is before ownership so a correct loser need not publish.
- V-3 / T3: retain both existing real-enqueue held-producer cases; another enqueue must complete
  while publication is held. All four original dispatch/verdict tests remain required.
- V-4 / T4: definite producer refusal leaves the confirmed source owed; a fresh dispatcher
  retries once and subsequent triggers do not duplicate. Both direct variants. This safety
  behavior already exists at the base; its red mutation belongs to SourceLanding Mutation.
- R-1: complete durability and bridge classes, including runtime late-confirm redispatch.
- R-2: complete delivery-verification class; no delivery verdict changes.
- R-3: full outbound delivery class (including gate and send-shape partial files).
- R-4: full task reply integration class, including outbound runtime partial; live routing
  before/after queue processing remains compatible.
- R-5: full outbound dispatch integration and capture classes; catalog-less routing and
  capture's shared source claim remain compatible. Two added bound-grace trailing-text cases
  preserve the default-off/catalog-less watermark behavior.
- R-6: a losing dispatcher with a longer text snapshot must not consume the winner's owed
  tail. The separate [expected-red checkpoint](2026-10-14-card-1247-watermark-red.md) proves
  the missing-tail assertion before the repair; the complete grace class covers it afterwards.
- R-7: caller cancellation cannot release ownership while the actual transport continues.
  The [cancellation checkpoint](2026-10-14-card-1247-cancellation-red.md) proves the duplicate
  owner before repair; the full grace class repeats both variants after repair.

Whole Unit is explicitly deferred by the repair brief, not passed. HerdrAlwaysOnChannelParityTests
is excluded for inherited CARD-1252. No live-provider/manual traffic is commissioned. No whole
assembly run: impacts are bounded to queue grace, dispatcher source selection and outbound
ownership. The closed list costs approximately 38 minutes plus the expected-red builds/runs.
All rows run serially on the loaded host and reuse one build per source slice. The checkpoint
tool bootstrap is the only extra driver and runs through build-slot. Linux uses UseAppHost=false.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-2 | repaired | `tests/Antiphon.Tests -> bin-c1247-caee-complete/` | grace | `/*/Antiphon.Tests.Application/(ChannelReplyLateConfirmDispatchTests*)/*` | V-1,V-2,V-3,V-4,R-5,R-6,R-7 | all listed, 0 failed | 20 | 6 |
| CP-3 | repaired | CP-2 | durability | `/*/Antiphon.Tests.Application/(ChannelReplyDurabilityTests*)/*` | R-1 | all listed, 0 failed | 25 | 4 |
| CP-4 | repaired | CP-2 | bridge | `/*/Antiphon.Tests.Application/(ChannelBridgeTests*)/*` | R-1 | all listed, 0 failed | 40 | 4 |
| CP-5 | repaired | CP-2 | verification | `/*/Antiphon.Tests.Application/(SessionMessageQueueDeliveryVerificationTests*)/*` | R-2 | all listed, 0 failed | 123 | 8 |
| CP-6 | repaired | CP-2 | outbound | `/*/Antiphon.Tests.Application/(ChannelOutboundDeliveryTests*)/*` | R-3 | all listed, 0 failed | 38 | 5 |
| CP-7 | repaired | CP-2 | runtime | `/*/Antiphon.Tests.Application/(AgentTaskReplyIntegrationTests*)/*` | R-4 | all listed, 0 failed | 334 | 5 |
| CP-8 | repaired | CP-2 | outbound-dispatch | `/*/Antiphon.Tests.Application/(ChannelOutboundDispatchIntegrationTests*)/*` | R-5 | all listed, 0 failed | 10 | 3 |
| CP-9 | repaired | CP-2 | capture | `/*/Antiphon.Tests.Application/(ChannelOutboundCaptureTests*)/*` | R-5 | all listed, 0 failed | 5 | 3 |

Historical CP-1 is preserved at fcb155656c2ca07d3c146280fde8592147b2bd20 and its run
20261014-102319-c349: 14 executed, 8 passed, 6 expected assertion failures (four wrong-turn,
two duplicate-publication variants). The initial repaired eight-row run 20261014-103226-01ad
passed 591 cases with clean source/build provenance at bbc54106df7daafc65385c150ebfbdf7c4494539.
Both receipt validators pass. Its final wait refuses the unused CP-1 build stamp's different
SHA because the tool enumerates all manifest builds; the report itself is GREEN/exit 0.
The final table includes only its own build; reports and receipts are retained unedited.

## Pending Mutation

All controls remain pending for method-scoped SourceLanding Mutation: PC-1 replace bound
dispatch with newest-turn dispatch (all V-1 variants); PC-2 remove the atomic direct claim
(both V-2 variants); PC-3 await publication under the queue semaphore (both V-3 variants);
PC-4 retain the failed direct claim (both V-4 variants); PC-5 drop grace dispatch; PC-6
dispatch for no confirmed Channel source. This Code task runs no deliberate mutants.
PC-7 omit bound-dispatch trailing watermark registration (both R-5 added variants) is pending.
PC-8 let a losing dispatcher advance the watermark (both R-6 variants) is pending.
PC-9 release ownership on caller cancellation while transport continues (both R-7 variants) is pending.
