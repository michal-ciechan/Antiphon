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

Initial committed slice `bdb22d39f21aa0d1952df933af1b032d883129ad`:

| Row | Executed / passed / failed / skipped | Verdict |
|---|---:|---|
| CP-5 | 66 / 66 / 0 / 0 | Pass, including both held-before-conversion deadline cases. |
| CP-6 | 31 / 29 / 2 / 0 | Red: running case assumed Grok's brief predated rules initialization; conversion-dispatched observed a marker file before its contents were flushed. |

Both rows used one isolated build and the literal plan filters through
`scripts/run-checkpoint.ps1` with granted build slots. Native receipts are under
`.antiphon/checkpoints/r38/CP-5-20260930-042455-f5a9/` and
`.antiphon/checkpoints/r38/CP-6-20260930-042831-5674/`. The next slice
uses the production `GrokRulesRefreshService.QueueLaunchBriefAsync` after the
direct runner starts, then the real queue delivers that persisted row. It also
waits for the dispatch cut marker's complete contents before killing the probe.

Final CP-1 through CP-13 outcome pending. V-25 remains pending because this
task authorizes no live send. PC-1 through PC-30 remain pending for paused
method-scoped SourceLanding Mutation.
