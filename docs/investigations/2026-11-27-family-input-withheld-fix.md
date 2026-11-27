# Family inbound 1904 and the adopted Claude input guard

Telegram inbound `1904` reached session `fb38e751-dfbd-4b8d-9ce5-ef84d1828461` as queue row `393b1a54-95f2-401e-bfcc-aa748230baf1`. The adopted runner had permanently excluded its session from known input history. The repeated observation of an unchanged reason does not increment the persisted withhold count. Incident `adcc0055-b52a-4c49-858c-9df40e2301c4` was recorded after ten minutes. The digest pager required an AlwaysOn agent and digest-enabled destinations.

## Recovery

An adopted PtyHost session can establish a new input boundary from its owned native transcript. The runner requires the current native reader to have parsed through EOF, a real prompt followed by an idle boundary in that binding, and two observations of a clear, empty composer at least 500 ms apart. The session generation, binding and input revision fence the proof. Every subsequent guarded write still checks the current dialog. An unavailable transcript, draft or modal keeps the input pending.

The channel wake worker revisits durable queue ownership. Its retry schedule for withheld rows starts after 10 seconds, backs off to 30 seconds after one minute, and reaches two minutes after five minutes. The original queue row owns the reply correlation. The persisted UserPrompt supplies delivery evidence.

If the session remains withheld, inspect `GET /api/sessions/fb38e751-dfbd-4b8d-9ce5-ef84d1828461/transcript`. For a verified idle agent that needs a relaunch, call `POST /api/agents/a7647365-4803-4541-a457-ba9e7fcfa8f0/stop`, then `POST /api/agents/a7647365-4803-4541-a457-ba9e7fcfa8f0/start` with `{"resumeSessionId":"fb38e751-dfbd-4b8d-9ce5-ef84d1828461"}`. The guarded manual retry for the pending row is `POST /api/sessions/fb38e751-dfbd-4b8d-9ce5-ef84d1828461/messages/393b1a54-95f2-401e-bfcc-aa748230baf1/send-now`. Check current custody and queue status before acting. The desktop recovery delegate owns any live action.

## Operator notification

A committed Critical withheld incident now triggers the operator's Critical `AlertRaised` toast and the attention client's `AgentChanged` push. A hidden tab can show the existing desktop notification when browser permission is granted. The monitor records `HumanNotifiedAt` after publication. This records a feed push, not human acknowledgement. Legacy incidents with a pending input are matched on their committed timestamp, native message identity, agent and session. The incident carries the recovery route. These alerts bypass digest-chat paging.

For enabled, agent-bound Telegram groups, `ChannelBridge:GroupReplyDeadlineMinutes` defaults to 10. The oldest accepted inbound still awaiting its own correlated reply starts the clock. An unrelated outbound or another inbound cannot postpone it. Once the guard clears, an overdue reply can raise its own alert even after the input-withheld incident closes.

## Gateway receipt

Gateway ingress and the Kafka inbox consumer both write the same native key. Both the successful receipt and the desktop queue row were observed for `1904`. The redacted 23505 log cannot establish which writer or key lost its race. The store now uses a single conflict-targeted insert, preserves original content and offsets, and can fill missing broker coordinates on an older receipt. A conflict on any other key remains an error.


## Desktop recovery evidence and follow-up repairs

The separate recovery task `30a74d3b` verified these IDs on 2026-11-27. Its final observation at 08:20:24 UTC showed zero pending channel inputs. This Debug task used its read-only report and synthetic fixtures on server2.

| Inbound | Transcript UserPrompt | Assistant reply | Outbound partition and offset | Telegram reply |
|---|---|---|---|---|
| 1904 | 1802 | 1809 | 0:821 | 1911 |
| 1913 | 1814 | 1819 | 0:823 | 1915 |
| 1914 | 1821 | 1823 | 0:824 | 1916 |

- **Startup input.** Boot remote-control setup previously released the queue writer before submitting `/rename`. A resumed session now serializes the setup, rename and first output with ordinary delivery. Launcher wakeup follows setup. The user’s remote-control setting can stay enabled.
- **Pre-prompt output.** Assistant entry 1800 became broker record 0:822 and Telegram message 1912. Transcript order places it before UserPrompt 1802. The recovery evidence does not identify the instruction that generated 1800. The bridge could attach startup text to the trailing window of a prior completed turn. Restart boundaries and generation timestamps now cap that window. A standing resume note also cannot supply a machine-follow-up destination.
- **Incident closure.** The monitor verifies positive queue delivery and the complete, matching UserPrompt beyond the attempt baseline. It appends one closure referencing the original incident, queue, session, prompt sequence and transcript entry. Both incident records remain visible through the incident API. After activation, the operator can request the same closure with `POST /api/agents/a7647365-4803-4541-a457-ba9e7fcfa8f0/incidents/adcc0055-b52a-4c49-858c-9df40e2301c4/close?promptSequence=1802`. Recovery confirmed native prompt UUID `944284bd-a49c-4210-9a47-236a8a6e8305`.
- **Gateway receipt.** New channel answers carry the exact native inbound IDs. On successful provider send, the gateway journals the accepted outbound ID and projects Answered onto the matching provider, conversation and message. Evidence also applies when the inbox row arrives second. For historical gateway inbox row `82ae5e3a-a972-435f-803e-5208aff42b88`, the recovery report verified acceptance as 1911. The operator can record that existing delivery using the gateway’s `POST /api/channels/messages/82ae5e3a-a972-435f-803e-5208aff42b88/ack`. This route sends no message.
- **Resume note.** Sequence 414 belongs to a previous generation. Startup, queue selection and the stranded-queue sweep cancel outstanding System resume notes whose generation key predates the accepted launch. The queue preserves the note, its attempts and cancellation time.

## Verification on server2

At pre-fix commit `650c6766e0e0f7bb393ba52952406db2b55eb249`, the three server witnesses, the adopted-runner witness and the inbox constraint-classification witness each failed on their asserted behaviour. The server retried before backoff, omitted the overdue notification and paged the digest destination. The runner still withheld after verification. The messaging detector classified an unrelated primary-key violation as a duplicate.

The corrected server witnesses passed on an isolated PostgreSQL schema. The synthetic photo produced one body write, one complete UserPrompt matching the queued body, one delivery attempt and one correlated answer across repeated drains. The clock-driven checks covered 9:59, 10:00, a failed push followed by a successful retry, and the digest pager with an enabled group and a pending incident. All three native-runner methods and all nine inbox cases passed. The inbox suite exercised the actual store on SQLite and classified structured Npgsql exceptions.

| Temporary fault | Exact test method | Observed failure | After restoration |
|---|---|---|---|
| Retry suppressed | `AdoptedChannelPhotoRetriesOnceAfterVerifiedHistory` | 0 bodies submitted after the guard cleared | 1 failed, then 1 passed |
| Critical push suppressed | `UnansweredGroupInboundPushesAttentionAtTenMinutes` | The injected publication failure was never reached | 1 failed, then 1 passed |
| Fake native reader reports ready before its history is verified | `AdoptedClaudeRequiresOwnedCompletedTurnAndEmptyComposer` | The simulated writer accepted input while the reader was behind | 1 failed, then 1 passed |

The three controls ran in detached checkouts. Each restored checkout matched its committed tree. The retry and alert controls used `7d88eff54fa58a80b2f63abc85194402583c84e9`. The simulated native reader used `05cfcc784d2f37bea5f9aeb66146e6125b8e6f0c`. The final server witness commit is `a0dd22b3c13dd50587d51a805ae871934047cf77`.

### Plan-selection guard

The six-class union passed all 57 executed cases. Adding `PlanCheckpointClassSelectionGuardTests` produced 58 passes and one failure. The same method at the assigned base, `561591c562f31dd4fe0fc8f940f2ea7e947ce965`, reported exactly the same three unresolved operands. The checkpoint validator rejected the combined receipt with `report_failed`.

| Historical plan | Row | Missing class operand | Class deletion commit |
|---|---|---|---|
| 2026-11-05 CARD-1722 | CP-3 | `ScriptHarnessSharingTests*` | `c68e72ae5ba75d3b1ea11c51c45abaa202194841` |
| 2026-11-14 CARD-1901 | CP-35 | `HostJqFixtureClosureTests` | `c0c33d51edba5e98e03b3462669410d00c68b24f` |
| 2026-11-18 CARD-1984 | CP-25 | `HostJqFixtureClosureTests*` | `c0c33d51edba5e98e03b3462669410d00c68b24f` |

### Checkpoint provenance

Each passing row below was validated with `scripts/antiphon checkpoints validate --evidence <report.json> --expected-source-sha <commit> --rows <row>`. An early storage interruption and two runner filter selections executed zero tests. The individual runner methods supply the qualified receipts.

<details><summary>Checkpoint output</summary>

```text
CHECKPOINT server-red commit=650c6766e0e0f7bb393ba52952406db2b55eb249 build=ok filter=/*/*/ChannelInputWithheldIncidentTests/(AdoptedChannelPhotoRetriesOnceAfterVerifiedHistory)|(C1591_ProducerPathPagesOneAgedWithhold)|(UnansweredGroupInboundPushesAttentionAtTenMinutes) executed=3 passed=0 failed=3 skipped=0 trx=/tmp/de62cb52-red-verify/.antiphon/de62-evidence/server-red-20261127-083257-9967/run.trx slot=granted waited=0s dirty=0 source=650c6766e0e0f7bb393ba52952406db2b55eb249 sourceState=clean buildSource=verified
CHECKPOINT runner-red2 commit=650c6766e0e0f7bb393ba52952406db2b55eb249 build=ok filter=/*/*/ClaudeDialogInputGuardTests/AdoptedClaudeRequiresOwnedCompletedTurnAndEmptyComposer executed=1 passed=0 failed=1 skipped=0 trx=/tmp/de62cb52-red-verify/.antiphon/de62-evidence/runner-red2-20261127-085246-3fb3/run.trx slot=granted waited=510s dirty=0 source=650c6766e0e0f7bb393ba52952406db2b55eb249 sourceState=clean buildSource=verified
CHECKPOINT inbox-red commit=650c6766e0e0f7bb393ba52952406db2b55eb249 build=ok filter=/*/*/EfInboxReceiptStoreTests/Unique_violation_is_not_inferred_from_constraint_name_alone executed=1 passed=0 failed=1 skipped=0 trx=/tmp/de62cb52-inbox-red/.antiphon/de62-evidence/inbox-red-20261127-085944-a7b2/run.trx slot=granted waited=516s dirty=0 source=650c6766e0e0f7bb393ba52952406db2b55eb249 sourceState=clean buildSource=verified
CHECKPOINT server-green3 commit=05cfcc784d2f37bea5f9aeb66146e6125b8e6f0c build=ok filter=/*/*/ChannelInputWithheldIncidentTests/(AdoptedChannelPhotoRetriesOnceAfterVerifiedHistory)|(C1591_ProducerPathPagesOneAgedWithhold)|(UnansweredGroupInboundPushesAttentionAtTenMinutes) executed=3 passed=3 failed=0 skipped=0 trx=/tmp/de62cb52-server-green/.antiphon/de62-evidence/server-green3-20261127-090156-39db/run.trx slot=granted waited=651s dirty=0 source=05cfcc784d2f37bea5f9aeb66146e6125b8e6f0c sourceState=clean buildSource=verified
CHECKPOINT server-final commit=a0dd22b3c13dd50587d51a805ae871934047cf77 build=ok filter=/*/*/ChannelInputWithheldIncidentTests/(AdoptedChannelPhotoRetriesOnceAfterVerifiedHistory)|(C1591_ProducerPathPagesOneAgedWithhold)|(UnansweredGroupInboundPushesAttentionAtTenMinutes) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/de62-evidence/server-final-20261127-094052-15b2/run.trx slot=granted waited=495s dirty=0 source=a0dd22b3c13dd50587d51a805ae871934047cf77 sourceState=clean buildSource=verified
CHECKPOINT runner-boundary commit=7d88eff54fa58a80b2f63abc85194402583c84e9 build=reused filter=/*/*/ResumeReceiptReadinessTests/AdoptedInputBoundaryRequiresAnOwnedFullyReadIdleTurn executed=1 passed=1 failed=0 skipped=0 trx=/tmp/de62cb52-runner-green/.antiphon/de62-evidence/runner-boundary-20261127-092436-e6e7/run.trx slot=granted waited=255s dirty=0 source=7d88eff54fa58a80b2f63abc85194402583c84e9 sourceState=clean buildSource=verified
CHECKPOINT runner-adoption commit=7d88eff54fa58a80b2f63abc85194402583c84e9 build=reused filter=/*/*/ClaudeDialogInputGuardTests/AdoptedClaudeRequiresOwnedCompletedTurnAndEmptyComposer executed=1 passed=1 failed=0 skipped=0 trx=/tmp/de62cb52-runner-green/.antiphon/de62-evidence/runner-adoption-20261127-092438-90af/run.trx slot=granted waited=255s dirty=0 source=7d88eff54fa58a80b2f63abc85194402583c84e9 sourceState=clean buildSource=verified
CHECKPOINT runner-modal commit=7d88eff54fa58a80b2f63abc85194402583c84e9 build=reused filter=/*/*/ClaudeDialogInputGuardTests/C1591_AdoptedIdleDraftDialogAndWorkingWriteNothing executed=1 passed=1 failed=0 skipped=0 trx=/tmp/de62cb52-runner-green/.antiphon/de62-evidence/runner-modal-20261127-092439-612e/run.trx slot=granted waited=270s dirty=0 source=7d88eff54fa58a80b2f63abc85194402583c84e9 sourceState=clean buildSource=verified
CHECKPOINT inbox-green commit=05cfcc784d2f37bea5f9aeb66146e6125b8e6f0c build=ok filter=/*/*/EfInboxReceiptStoreTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/de62-evidence/inbox-green-20261127-090150-4ad4/run.trx slot=granted waited=642s dirty=0 source=05cfcc784d2f37bea5f9aeb66146e6125b8e6f0c sourceState=clean buildSource=verified
CHECKPOINT guards-six commit=05cfcc784d2f37bea5f9aeb66146e6125b8e6f0c build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)/* executed=57 passed=57 failed=0 skipped=0 trx=/tmp/de62cb52-server-green/.antiphon/de62-evidence/guards-six-20261127-092746-b52b/run.trx slot=granted waited=736s dirty=0 source=05cfcc784d2f37bea5f9aeb66146e6125b8e6f0c sourceState=clean buildSource=verified
CHECKPOINT guards commit=05cfcc784d2f37bea5f9aeb66146e6125b8e6f0c build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)|(PlanCheckpointClassSelectionGuardTests*)/* executed=59 passed=58 failed=1 skipped=0 trx=/tmp/de62cb52-server-green/.antiphon/de62-evidence/guards-20261127-092223-9ccb/run.trx slot=granted waited=165s dirty=0 source=05cfcc784d2f37bea5f9aeb66146e6125b8e6f0c sourceState=clean buildSource=verified
CHECKPOINT plan-guard-base commit=561591c562f31dd4fe0fc8f940f2ea7e947ce965 build=ok filter=/*/*/PlanCheckpointClassSelectionGuardTests/Plan_checkpoint_rows_select_declared_test_classes_or_are_retired executed=1 passed=0 failed=1 skipped=0 trx=/tmp/de62cb52-guard-base/.antiphon/de62-evidence/plan-guard-base-20261127-092709-bfaf/run.trx slot=granted waited=407s dirty=0 source=561591c562f31dd4fe0fc8f940f2ea7e947ce965 sourceState=clean buildSource=verified
```

</details>
