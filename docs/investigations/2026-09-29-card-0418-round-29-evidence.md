# CARD-0418 round 29: internal conversion purpose evidence

This continues the [round-28 evidence](2026-09-29-card-0418-round-28-evidence.md).
The implementation/test slice is `113cf6311e8b8435be8651ed93d1f128b98f7b93`.
No shared-stack restart, actual destination, land, or SourceLanding Mutation was used.

## V-10 and R-7 ordinary evidence increment

`OutboundConversionTaskTests.Ordinary_creation_links_one_pinned_internal_worker_without_source_inheritance`
now uses the created outbound Worker task as a caller for a second task request.
The normal `AgentTaskService` refuses it with `ForbiddenException`, persists one
Rejected event, and leaves zero children. This tests the real worker-token
delegation boundary as well as the instruction forbidding child dispatch. The
existing assertions in the same test cover one linked Worker/Custom/Shared task,
pinned converter/project/workspace, no parent/card/env inheritance, one attempt,
`CommitOnSettle=Never`, and the frozen request path.

`AgentTaskReplyIntegrationTests.Internal_conversion_settles_with_usage_but_without_sources_or_follow_up`
uses a PostgreSQL schema, a linked delivery, transcript prompt and report token,
and the normal reply-settlement service. It records ordinary marked success and
19 input/7 output tokens while asserting no deliverable source bundle, completion
notification, parent queue note, or child task. Distillation is ineligible even
with that feature enabled. A separate ordinary Custom task with the same report
and Markdown path settles and produces one byte-identical source attachment.
Existing specialist-role companions in `AgentTaskReplyIntegrationTests` cover
Check, Distill and Diagnose settlement, and `OutputDistillationPolicyTests`
covers the purpose-specific distillation gate. This round adds a normal Custom
companion to that inventory.

These tests add **partial V-10 and R-7 evidence**. Whole V-10/R-7 remain open:
the full normal dispatch and composed instruction/environment/capability sentinel
matrix, public HTTP create boundary, check/diagnose escalation and card transition
companions have not yet been executed as one named integration result. V-11–V-18,
V-23 and the remaining open R matrices gain no new assertion from this slice.
R-14 indexes these named new sub-assertions, but remains open until the complete
ordinary evidence index and validator invocation exist.

## Checkpoint trail

The plan's full CP-1–CP-13 closed-list sweep ran on the committed slice in
`.antiphon/checkpoints/20260929-151721-1138/` (22m23s). Every row held a granted
host slot. CP-2 and CP-5 include the new methods and both passed. The separately
leased checkpoint-tool bootstrap build passed with the existing CS8602 warning.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3485 | 3485 | 0 | 33 | Green; Linux platform skips |
| CP-2 source settlement | 373 | 373 | 0 | 0 | Green |
| CP-3 policy/schema | 32 | 32 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 26 | 26 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Inherited T1/T2 refusal only |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green |
| CP-11 gateway wire | 125 | 125 | 0 | 0 | Green with broker opt-in |
| CP-12 client | 26 | 26 | 0 | n/a | Green |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

CP-8's failures are exactly
`PinnedAgentKindTests.T1_a_task_pinned_to_a_stopped_standing_Codex_agent_with_no_kind_stores_Codex`
and `PinnedAgentKindTests.T2_an_explicit_kind_mismatch_is_refused_and_an_agreeing_kind_is_accepted`,
both inherited `codex_desktop_unqualified` refusals. No other row failed and no
unlisted test/build row was run by the tool.

## Remaining gates

Closed whole ordinary IDs remain V-1–V-9, V-19–V-22, V-24, R-1 and R-12.
Open ordinary IDs remain V-10–V-18, V-23, R-2–R-11 and R-13–R-14.
V-25 is the later authorized actual-destination gate. PC-1–PC-30 remain pending
method-scoped SourceLanding Mutation; ordinary or nightly green does not discharge them.
