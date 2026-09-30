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

The first CP-6 rerun on `716d2751550539a7fe38a392dae7a6b0419ea037`
executed 31 / passed 30 / failed 1 / skipped 0 at
`.antiphon/checkpoints/r38/CP-6-20260930-043726-0f61/`. The remaining
failure was a fixture write of null `StartedAt` from the direct runner DTO;
the persisted session already owns its accepted generation, so the fixture now
changes only `Status` to Running. The complete-marker change passed its case.

The D-1 revert-and-run used a detached throwaway worktree at committed
`76f73f8abdc3b75516aa3f88c0596488b0042341`. Its only source difference
from the regression-test slice was restoring the old unconditional `Ready`
assignment in `ResumeHeldAsync`. Exact filter
`/*/*/ChannelOutboundDeadlineTests/Resume_held_before_conversion_preserves_work_or_annotates_expired_original`
built in isolation and executed 2 / passed 0 / failed 2 / skipped 0. Both
rows failed at the expected `Pending` versus `Ready` assertion; native TRX
and log were copied to `.antiphon/checkpoints/r38/red-proof/`. The green
CP-5 result above ran those same two cases on the fixed source.

An extra exact V-15 running-method diagnostic is warranted before spending
another full CP-6 run: the first two full runs each took about seven minutes
and failed in the new native fixture setup. It gets an isolated build and
granted slot, then CP-6 will be rerun as the plan row.

`DIAG-V15` on `d3db721a1b31f736c18efdb2220633bed6c03959` built cleanly,
executed 1 / passed 0 / failed 1 / skipped 0 at
`.antiphon/checkpoints/r38/DIAG-V15-20260930-044857-3001/`.
The dispatcher brief was persisted through `QueueLaunchBriefAsync`, but
`SendNowAsync` returned it to the queue because its Enter produced no output.
The next exact-method diagnostic records the native stdin burst shape and
transcript at that refusal; no LF-as-Enter mode has been restored.

The second exact V-15 diagnostic on
`c1426bd5b5e4d4c8ecb8ac02072583914abfbeff` executed 1 / passed 0 /
failed 1 / skipped 0 at
`.antiphon/checkpoints/r38/DIAG-V15-20260930-045317-a171/`.
FakeGrok received the 677-byte brief with LF and a later separate one-byte LF,
and rendered the brief. The converter's tool gate holds the turn before FakeGrok
emits output or a transcript row, so synchronous `SendNowAsync` correctly
refused to mark delivery while the gate was held. The fixture now keeps that
real queue send in flight, cuts the dispatcher at the held tool, releases the
tool, then requires queue completion and a native matching `UserPrompt` before
the rebuilt server reconciles the task and sends its own second prompt.

The third exact V-15 diagnostic on
`9f23fd2cbab658fbec814765f26a5306cf6adb42` executed 1 / passed 0 /
failed 1 / skipped 0 at
`.antiphon/checkpoints/r38/DIAG-V15-20260930-045901-640e/`.
The queued send reached the held converter before the cut. The next assertion
was stale: it expected the dispatch-created session to remain `Starting` even
though the direct runner and queue now correctly advanced it to `Running`.
The cut assertion now distinguishes the running case.

The fourth exact V-15 diagnostic on
`afa47ce2232ca5acca7b307099f7574371f5ade6` executed 1 / passed 0 /
failed 1 / skipped 0 at
`.antiphon/checkpoints/r38/DIAG-V15-20260930-050210-320b/`.
The first queue send and native converter task now pass the cut and settlement
checks. The rebuilt server's second `SendNowAsync` refused its prompt because
no matching transcript record appeared in the verification window. The next
diagnostic captures native kinds, prompt match, screen and byte shape there.

The fifth diagnostic on `bc16de4266a3f96f736d48026757d740b36bb123`
executed 1 / passed 0 / failed 1 / skipped 0 at
`.antiphon/checkpoints/r38/DIAG-V15-20260930-050719-4731/`.
The native transcript now contains two complete turns including a `UserPrompt`
with the second probe text, but queue verification still refused that turn.
The next exact-method diagnostic compares native and persisted prompt sequences,
the queue's attempt baseline, and the shared prompt matcher. It will establish
whether this is transcript ingestion, a floor mismatch, or a text-match issue.

The sixth diagnostic on `3ffc388ea4e9dbb8287807451019d8c35b5ca111`
executed 1 / passed 0 / failed 1 / skipped 0 at
`.antiphon/checkpoints/r38/DIAG-V15-20260930-051224-4bbc/`.
The native and stored transcripts contain the second prompt, the stored row's
sequence 4 exceeds the queue baseline 3, and the shared matcher accepts its
text. The fixture had no hosted event pump: its stored row appeared only during
the queue's post-failure grace, after its verification window. The rebuilt
fixture now runs production `AgentSessionRuntime.SyncTranscriptAsync` while its
second `SendNowAsync` waits, representing the live transcript ingestion that
Program normally supplies through `SessionRunnerEventPump`. The row still comes
from the native runner transcript, not a test-written prompt.

Final CP-1 through CP-13 outcome pending. V-25 remains pending because this
task authorizes no live send. PC-1 through PC-30 remain pending for paused
method-scoped SourceLanding Mutation.
