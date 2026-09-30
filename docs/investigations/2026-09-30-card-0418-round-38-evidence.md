# CARD-0418 round 38: held conversion and native queue recovery

This round starts at `ff3bb16f103b15e8cc59439af1314b414825e347` without
merging master. The round-36/37 ledger claims were corrected at their source.

## Scope and coverage

- D-1: resume a Held conversion with no conversion task or outcome to Pending.
  Within the frozen deadline the pump must create one ordinary converter task;
  past it the pump must publish an annotated original with no converter task.
  An already converted Held delivery still resumes to Ready. The two new
  deadline cases require a revert-and-run red receipt against the old service.
- D-2: the running converter case must deliver the dispatcher's persisted
  delegation brief through `SessionMessageQueueService`, observe its native
  `UserPrompt`, and check a second queued prompt using the rebuilt server after
  dispatcher death. The test must use a native pty-host apphost, with no LF
  mode or test-output launcher replacement.
- D-3: `AttentionServiceTests.Outbound_delivery_states_have_truthful_attention`
  covers Held, Failed, PublishUncertain references/severity and Published
  absence. It does not cover Pending, Converting, Ready, or Publishing. Round-36
  V-15 receipts were Linux-only and used a synthetic prompt and launcher shim.

## Checkpoint receipts

Pending. Run the plan's exact CP-1 through CP-13 filters on committed source;
at minimum CP-5, CP-6, CP-7 are affected. Record each native count and any
inherited CP-8 `PinnedAgentKindTests.T1/T2` failure here. V-25 remains pending
because this task authorizes no live send. PC-1 through PC-30 remain pending
for paused method-scoped SourceLanding Mutation.
