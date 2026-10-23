# CARD-1529: Claude warm handoff and refocus compact

Confirmed by source inspection: an unrelated Claude warm handoff can persist its refocus
compact, then block the task **before inserting its dispatch brief**. This is a brief-admission
defect, independent of whether compaction is still running. Today's inspected production
sample contains no Claude warm handoffs; it neither reproduces nor exonerates that path.
No product changes, builds, tests, provider launches, desktop operations or qualification
interference were performed by this Debug task.

| Evidence boundary | Identity / result |
|---|---|
| Debug source | `bd215d7b4f559db69fbf4d7363417fce0f2c2467` |
| Original observed test | CARD-1491 Code `d15bbee2-70df-47c3-af9e-244806a661f7`, source `a306dc71b3f02b7a5d06e4a76271a0c4eb329be7` |
| Original Code base | `bf66aedb13cd7dca818a7f6840d689090d464eb7` |
| Running server, GET `/api/version`, 05:28:17Z | `5a126f6fc5ae4e16d803b628716b8f0485a37174` (RC); contains the same affected code |
| Historical failure | `C1491_matching_effort_reuses(True, xhigh)`, 2026-10-23 **03:15:01.5215222–03:15:13.9082226Z**, expected `Dispatched`, actual `Blocked` |
| Historical CP-3 | 27 executed, 26 passed, 1 failed; not a run by this Debug task |

**Exact source sequence.** References below are at the Debug source above.
The reuse entry is [AgentTaskDispatcher.cs](../../server/Application/Services/AgentTaskDispatcher.cs),
`DispatchOneAsync` around line 5690 and `DeliverReuseMessagesAsync` around line 8539.

| Order | Condition / operation | Durable consequence |
|---|---|---|
| 1 | Shared dispatch successfully reuses an eligible warm session; commits task/session binding and dispatch time before delivery. Pinned and unpinned reuse reach this producer. | Task is `Dispatched`; the new brief need not exist yet. |
| 2 | Non-specialist; previous dispatched task on this session has a different `RootTaskId`; live session kind declares supported `RefocusCompact` with a command (Claude: `/compact`). | Producer awaits a WhenIdle enqueue of `/compact This session is being handed NEW, unrelated work. Keep only context useful for: {first 300 characters of goal}`. |
| 3 | `TryEnqueueReuseAsync` uses `Origin=Delegation`, `ExecutionTaskId=new task ID` and its execution deadline. `EnqueueAsync` persists before its delivery probe. | A current-attempt related queue row exists. It may still be Pending, may have been attempted, or may already be Sent. |
| 4 | Production has no `ReuseEnqueueOverride`; producer calls `TryEnsureReuseBriefAsync` → `EnsureDispatchBriefAsync`. Under queue gate and task-row lock, the query selects **all** rows with this `ExecutionTaskId`, among other correlation arms. | The compact is included even if it has finished; there is no Pending-only filter. |
| 5 | Task is still Dispatched/Working, request attempt/session/dispatch/start generation matches, compact creation is in the dispatch window, **no recognized brief exists**, and compact body/retained payload lacks the new task marker. | `RecognizedRows=0`, `OtherCurrentEvidence=true`; `unrecognized-evidence` wins before `absent`. |
| 6 | `StageBriefHold` handles that decision. | Task becomes `Blocked`, reason `dispatch_brief_evidence_uncertain: current brief evidence is not a complete unattempted row or a complete receipt.`, with a Blocked event. **Zero brief inserts.** |

The classifier is [DispatchBriefEvidence.cs](../../server/Application/Services/DispatchBriefEvidence.cs)
(`Table`, `Observe`, `IsRecognized`, lines 107–199); the broad query, hold and insertion branch
are in [SessionMessageQueueService.DispatchBrief.cs](../../server/Application/Services/SessionMessageQueueService.DispatchBrief.cs)
(lines 74–138 and 236–255). Row timestamps use dispatch-window comparison with PostgreSQL
microsecond normalization. Ordinary generated compact text has no task marker; a goal that
itself includes that marker is outside this exact condition and must not be generalized from it.

This is **held admission, not a Pending brief waiting for the compact boundary**. No evidence
shows a brief being typed into or dropped by the compact. A successful `/compact` UserPrompt
confirms that command only. A manual CompactBoundary can call `FlushIfIdleAsync`, but cannot
flush an absent brief. Calling ensure again with the blocked task returns `task-not-open`;
the same compact would still be unrecognized evidence if the attempt were simply reopened.
An exception after compact persistence retains the obstruction; an exception before insertion
does not create it. Actual caller cancellation still propagates. An already recognized brief,
same-root follow-up, no previous root, or specialist task does not meet this false-hold shape.
Codex/Grok do not send this refocus command under their current capability contracts.

**Delivery contract and ownership.** The dispatch handoff fails its obligation to get the
brief to a transcript-confirmed recipient. The receipt rule itself remains conservative:
there is no false claim here that the brief was delivered. The owner is the **server dispatcher
and queue-owned brief identity classification**, not the Claude adapter, PTY Enter handling,
compact working/idle parser or CARD-1491 effort stamp. Keep complete matching `UserPrompt`
evidence above the attempt floor; neither `Sent`, command echo, CompactBoundary nor
`QueuedUserPrompt` can replace it. Existing manual-boundary versus auto-boundary semantics
remain necessary ([runtime owner](../session-runtime-invariants.md), preserved gotcha 34).

**Attribution.** Commit `dbe4e4071f52adb17706aba499a66690bf0394bc` (CARD-1150, 2026-10-08)
introduced the ensure classifier and changed production warm-brief delivery from direct
enqueue to ensure, retaining the compact's execution identity. It is an ancestor of the
CARD-1491 base. Both complete brief-classification files and the dispatcher region from
`DeliverReuseMessagesAsync` through `RecordReuseEnqueueFailedAsync` are byte-identical at
that base, this Debug base and the running RC source. Their SHA-256 values respectively are
`1187984f4e0eda06e2d0d59508aeeb2687b3f1db32df8c9088918ff156e2acea`,
`ee5cc29bd6a15ff7801332fe74e57a720f785b5c4406df8c434a88e026cabf25`, and
`546b379790de75ed2397ac0b14d8095685c242c02c055cf54d17fd70290b493a`.
This establishes pre-CARD-1491 **source attribution**, not a baseline test rerun.

The original TRX and report remain readable locally at
`/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-030358-ee59/rows/CP-3/run.trx`
and `/work/worktrees/task-d15bbee2/.antiphon/task-d15bbee2.md`.
TRX SHA-256: `059caef7d934976caab82447bdd1413dae56dce13d3ece53bd3dfff0fa63c6f6`.
The artifact records the status assertion, **not a captured DB row/reason**; the exact reason
and row sequence above are source-derived. The prior report also preserves the run's cleanup
refusal and does not qualify that run as green. Its eventual 54/54 success used a same-root
V-4 fixture; it does not verify unrelated-root delivery. Only the pinned/xhigh V-4 cell used
the full dispatcher tick; the three other matching-effort cells call reuse selection alone.

**Today's frequency check, UTC.** At 05:28Z, enumerated
`GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&since=2026-10-23T00:00:00Z`,
then all returned task details and distinct session transcripts with `since=0`.
This list includes tasks completed today and all still-open tasks regardless of age; it is
not a created-today filter. Specialists are excluded by the list default. No list truncation
was found in its implementation. Queue snapshot followed at approximately 05:32Z.

| Inspected population / signal | Count and interpretation |
|---|---|
| Board tasks / distinct sessions | 53 / 52; all Worktree, 32 Codex and 21 Grok tasks; zero Claude delegate exposures |
| Normalized transcripts | 52/52 retrieved, 4,893 rows total, 4,570 timestamped today |
| Today's CompactBoundary | 10 across 9 Grok sessions, all explicitly `(auto)`; each already has its marked dispatch UserPrompt earlier in sequence |
| Refocus compact UserPrompts / actual warm-reuse dispatch events / uncertain-brief holds | 0 / 0 / 0; task/assistant prose discussing this card excluded |
| Pending queue snapshot | 52/52 retrieved through `/messages`; two System Grok-rules rows on one session, zero dispatch briefs or refocus compacts |
| Agent incidents | 7/52 agent endpoints readable, eight retained rows (seven dated today); zero matching refocus/brief-uncertain incidents. Other 45 return 404: unavailable history, not zero incidents |
| Project-scope cross-check | 56 tasks; the three additional tasks are Codex; still zero Claude tasks |
| Historical test witness today | One failed unrelated-root full-dispatch cell; not a production incidence count |

Examples that must **not** be counted as the target defect:

| Session | Task | Marked UserPrompt UTC / sequence | Auto compact UTC / sequence | Pending dispatch brief |
|---|---|---|---|---|
| `a1a1618f-e5e4-48f5-afbb-bfea25cc4a55` | `89be30a4` | 00:16:37.890 / 9 | 00:41:00.043 / 144 | None at snapshot |
| `29d23f5b-4873-480e-8c3c-34ccbbb704a7` | `821380f3` | 02:33:18.702 / 10 | 02:56:16.503 / 180; 03:24:46.310 / 330 | None; the two pending rows are rules refreshes |

Frequency of the target production defect is **unmeasured: zero eligible exposures**, not a
0% failure rate. The same affected source is deployed, so no recurrence in this Worktree-only
sample is not exoneration. Searches inspected typed transcript kinds and actual task events,
not textual occurrences in briefs/check digests. `/queue` returned 404 on this server;
the documented `/messages` route succeeded. That response contains Pending rows only, so it
cannot prove absence of historic Sent/Canceled rows. Incident GET requires an extant agent and
is bounded at 200; the seven readable responses each had only one or two rows. No live
database query or original fixture DB reconstruction was performed.

**Duplicate check and scope.** Before proposing new work, completed `card.ps1 search` with
`-Board Antiphon -All`: `refocus` (7 cards), `warm handoff` (1), `compact` (72), then read the
relevant cards in full. Keep this defect on CARD-1529; create no duplicate.

| Existing card | Relationship |
|---|---|
| CARD-0077, Done | Same missing-brief symptom; original compact exception skipped the second enqueue. Independent catches fixed that. Today's ensure returns a hold without throwing, so that repair does not cover it. |
| CARD-0029, Done / CARD-0041 | Prior brief existed Pending for 45.5 hours because manual compaction left Working stale. Its closure attributes this to CARD-0041. Today's brief is absent. Old duplicate prose in 0029/0077 is superseded by their distinct closure explanations. |
| CARD-0117, Done | Codex refocus behaved as real work plus stale incident attribution. Provider capability gating fixes that; Claude Supported reaches today's different classifier problem. |
| CARD-1150, Review | Owns the anti-duplicate ensure introduced in the source attribution above; preserve those guards and coordinate the focused correction. |
| CARD-1464, Review | Mostly SessionStateStore fixture publication. Its plausible RC `C544_BriefHandoffRecovery` uncertain-brief red overlaps this finding: the `warm` seed has a different root and Claude session; its first dispatch assertion precedes the later raw transcript inserts. Treat this cell as a delivery-identity candidate, not automatically as a store-only fixture fix. On post-CARD-1491 master, that seed also needs a valid launch-effort stamp to reach reuse. No fresh execution claimed. |

**Ranked fixes.** Recommendation 1 preserves the established refocus behavior and all
uncertain-brief protections. It is the smallest correction that keeps that behavior.

| Rank | Change | Cost / blast radius |
|---|---|---|
| 1 | Give the producer's refocus row an explicit durable purpose/correlation, scoped to task/attempt/session, and exclude only that positively identified non-brief from brief-identity evidence. A reserved internal `ConversationKey` can carry it without a schema migration. Retain `ExecutionTaskId` and deadline. Require purpose/body/provider consistency; preserve holds for malformed or unknown rows and ambiguous marker prompts. | Server producer, queue classification/projection, recovery selectors and focused tests. Ordinary brief receipt matching, FIFO and WhenIdle stay unchanged. Legacy untagged rows remain held until separately inspected; no blanket retroactive exclusion. Audit other correlation-key consumers before choosing the encoding. |
| 2 | Stop sending the separate Claude refocus command and fold the unrelated-work instruction into the single marked brief, using the existing refocus rendering. | Fewer code changes, but changes all unrelated Claude reuse behavior: no context shrink and potentially higher context cost. Correct delivery mitigation only if that behavior tradeoff is accepted; it does not recover an already persisted conflicting compact. |
| 3 | Persist a typed compact-and-brief handoff together, then deliver through the existing queue and receipt rules. | Larger transaction/recovery work; addresses the broader crash gap while preserving ordering. More change than needed for this classification defect. |

Recovery has two additional consumers to align in
[AgentSessionService.cs](../../server/Application/Services/AgentSessionService.cs):
`AcceptDispatchBriefAsync` (line 1104) treats any matching execution row as an existing brief;
`ReadRunningObligationAsync` (line 1291) can classify a lone unattempted compact as a brief,
or an attempted compact as `NotThis`. A fix only in `DispatchBriefEvidence` would leave those
crash/restart entry conditions inconsistent. Use the same positive non-brief identity there;
do not widen recovery authority or change receipt matching.

Do not remove `ExecutionTaskId` blindly: it also supplies terminal-task cancellation and
expiry ownership in `CancelDeadBriefsAsync` / `CancelExpiredBriefsAsync`. Do not recognize
the compact as a brief by adding a marker, ignore all markerless/attempted rows, treat a compact
receipt as brief delivery, reorder the brief ahead of compact, or lengthen waits. None resolves
identity correctly. Existing blocked tasks need inspected recovery, not an automatic replay.

The implementation handoff should first retain the unrelated-root full-dispatch witness,
including persisted compact identity/status, absence versus presence of the marked brief,
task reason, and exactly one complete matching brief UserPrompt after the real manual-boundary
shape. Exercise compact Pending, completed, and post-persist exception paths without the
`ReuseEnqueueOverride` bypass; same-root and non-Claude controls remain separate. Existing
`AgentTaskReuseEnqueueTests.both_reuse_messages_enqueue_when_nothing_throws` already calls the
real producer and is a focused candidate. Preserve the CARD-1150 uncertain/canceled/attempted,
spill, receipt-floor and producer-race assertions. Revisit only the implicated C544 warm cell
with the CARD-1464 owner. These are proposed repair checks, **not executed evidence**.

Raw API snapshots, retrieval indices and source hashes are ignored local evidence under
`/work/worktrees/task-9a5aaac8/.antiphon/debug-1529/`; they are not committed or a durable
external archive. The durable facts, limitations, original TRX digest and re-readable API IDs
are recorded above. Server transcript retention is seven days. Land this diagnosis, then
commission a separate Code task for CARD-1529; no running-stack change is needed to land it.
