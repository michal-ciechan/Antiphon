# CARD-0888: runner-bound refinement and reply spills

Date: 2026-10-01. Stage: Plan; TestDesign follows. Baseline: `origin/master` at **`2b4d7313324b6eaeadf6e45fb15808bacfad6263`** (the local remote-tracking ref read during this Plan; no claim of a fresh fetch). Assigned branch `feat/card-task-8ee59620` starts at `14661919c77b81819bf07a6380f58687225b4dfc` and is not rebased. The implementation sources and existing test files cited below were compared with that baseline; the relevant files have no differences. This Markdown file is the only Plan edit.

## Outcome and scope

Make an oversized refinement or blocked-task reply readable by its actual recipient. A runner-bound session receives a relative, queue-owned `.antiphon/inbox/<message-id>.md` pointer, and the existing phone-home Input operation writes the complete body on that runner before typing the pointer. Preserve successful desktop spills, task attribution, provider formatting, queue ordering and transcript-confirmed delivery.

Provide an exact authenticated API fallback containing the **whole immutable input**, not the truncated head of the newest event. A positively identified runner spill failure before input switches the same queued message to that fallback and records a task Warning. Ambiguous connection loss retains the original delivery evidence and follows existing reconciliation. Surface an explicit, correlated assistant complaint about an unreadable input in the Attention feed, including while the task is Working.

Pending-age escalation, `-Refine -Now`, mid-turn injection and stale-brief settlement are **out of scope**, owned by CARD-0682/0731. The existing check digest already renders the age of pending messages; retain and test that behavior. No instruction-bundle, checkpoint-tool, deployment-script, provider-launch, PTY encoding, settlement-policy or automatic-stop changes.

## Evidence and limits

Read CARD-0888 in full first with `pwsh -NoProfile -File scripts/card.ps1 get CARD-0888 -Board Antiphon`, then CARD-0682, 0731, 0649, 0084 and 0353 with the same read command. Consulted the CARD-0826 and CARD-0866 plans on `origin/master` for the plan/manifest shape. Owners read for the affected contracts: `docs/project-context.md`, `docs/session-runtime-invariants.md`, `docs/ops-http.md`, `docs/agent-kinds.md`, `docs/agent-card-lifecycle.md`, `docs/agent-credentials.md`, `docs/resilience.md`, and the checkpoint/build-slot and section-1 orchestration rules in `docs/testing-and-build.md` and `docs/orchestration-loop.md`.

Measurements are read-only: `git status`, `git rev-parse`, `git show`, `git diff`, bounded `rg`/source reads, card GETs and a Node attribute census. No tests/builds ran on Linux or Windows; no provider was launched, no live transcript was fetched, and no production setting or filesystem was changed. The Windows checkout is inaccessible from this runner. Card observations are historical, not fresh reproductions. Python was unavailable; the source census used Node instead.

| Evidence | Observation and implication |
|---|---|
| CARD-0888, historical October 1 incidents | Codex tasks 8d5aa4fc and 9a1c17e8 reported inaccessible `C:\src\Antiphon\...` refinement files. Their briefs and the operator's manually copied runner-inbox files were readable. The supplied brief also reports Claude reading relative inbox pointers successfully. These do not prove a provider-specific filesystem bug. |
| `AgentTaskReplyService.cs:578-694` | Refine saves a Refined event before enqueue, loads only the session kind, applies the server PTY profile and writes beneath `task.WorkingDirectory` on this process. It never consults RunnerCwd. A successful desktop write bypasses its IO fallback even when the recipient cannot see that filesystem. |
| `AgentTaskReplyService.cs:356-468` | A blocked Answer queues the full marked body WhenIdle; it does **not** call FitRefinementForTyping. The open-question answer is a separate unmarked Now/ToolResult path. The card's reply incident is historical; source proves the refinement pre-spill defect, while blocked replies require end-to-end coverage of their existing generic spill path. Do not claim both verbs have the identical implementation defect. |
| `AgentTaskDispatcher.cs:5503-5596` | Brief fitting uses RunnerCwd, stages a PhoneHomeInputSpill via queue.StageRemoteSpill, selects conservative runner ceilings and sizes against the final inbox GUID path. Reuse this delivery route, not Docker exec or a new workspace-write endpoint. |
| `SessionMessageQueueService.cs:136-163, 691-755` | BindStagedSpill rewrites to `.antiphon/inbox/<row.Id:D>.md`, stores RemoteSpillBody/RemoteSpillRelativePath in the row insert, and acknowledges only that staged object. A different spill for the session retains its own identity. |
| `RemoteSpillCourier.FindDurableAsync`; `PhoneHomeRunnerClient.cs:149-192` | Queue bytes survive server recreation and travel with Input. The client does not clear a failed in-flight spill. This is the required durability mechanism. |
| `PhoneHomeCommandDispatcher.cs:110-123, 202-212`; `RunnerWorkspaceService.cs:393-433` | There is no standalone brief file-write HTTP API. The phone-home Input payload carries runnerCwd and spill; WriteSpillAsync validates mirror confinement, message identity and symlinks, writes UTF-8, then SendInputAsync runs. Existing generic errors do not distinguish a write failure from a later uncertain input. |
| `DelegationReportFormatter.cs:991-1028`; `AgentTaskReplyService.cs:4537`; `AppDbContext.cs:2088` | Current fallback points at the newest Refined event. NewEvent truncates Detail at 4,000 characters and the database enforces that cap. It is neither an exact input identity nor a lossless fallback. This is part of CARD-0888 acceptance, not a separately deferred defect. |
| Session-runtime spill invariant; queue spill receipt tests | RemoteSpillBody is released after a complete matching UserPrompt. The model reads a pointer **after** that prompt exists, so an API fallback cannot depend on this delivery-only column continuing to hold the text. |
| `SessionDeliveryProfile.cs:55-65` | RunnerId selects the inbox single-write envelope, irrespective of the desktop's ModernConPty profile; the session's kind then narrows it. RunnerCwd selects the recipient filesystem. Missing remote binding must never be treated as permission to use a desktop path. |
| `DelegateCheckProbe.cs:399-424, 633-651` | Pending rows already carry CreatedAt; RenderDigest prints their age from facts.At, including a zero-attempt row. The five-row preview can hide a particular refinement. Dedicated oldest-refinement aggregation/escalation remains CARD-0682 work. |
| CARD-0649 / 0084 / 0353 | Preserve post-binding byte measurement and marker repetition; Grok refinements always spill and use a join-safe pointer. CARD-0353's original missing-follow-up theory was disproved: a pointer-only transcript can reflect provider capacity, not missing file bytes. Silence alone must not create the new attention condition. |

Static census at the baseline: AgentTaskRefineTests has 7 methods / **9 executions** (the settled-status method has three arguments); AgentTaskReplyOverlayTests **7**; PhoneHomeSpillTests **4**; PhoneHomeSpillTransportTests **3**; DurableRunnerSpillReceiptTests **11**. No arguments or OS gates occur in those latter four selected classes. `attentionVisuals.test.ts` has **22** executions (16 individual tests plus six it.each cases). These are source counts, not TRX evidence.

## Design decisions

### D-1: select the destination from the session and reuse message-owned Input spills

Load the bound session's AgentKind, RunnerId and RunnerCwd together in Refine and blocked Answer. For a complete remote binding, use the brief's conservative runner ceiling selection; for a local session retain the present process profile and successful desktop path behavior. A RunnerId without RunnerCwd is an unavailable file destination: use the durable API pointer, record that reason, and never write task.WorkingDirectory. A missing session remains the existing refusal.

For a spilling remote refinement, build the complete marked refinement once, stage it under a unique relative path, and pass it through StageRemoteSpill/BindStagedSpill. Size the pointer after replacing its temporary path with the exact GUID-length inbox path. At enqueue commit, the row owns both path and bytes; transport uses FindDurableAsync and the existing Input payload. Never join a Linux path using the server's Path.Combine. Two inputs in the same clock second must have different queue paths and immutable bodies.

Use the same task-input metadata and spill route for **oversized blocked replies**. Preserve their current spill threshold (SingleWriteMaxBytes) and marked WhenIdle semantics; do not impose the refinement wrapper or a new threshold on short answers. Refinements retain BriefInlineMaxBytes and Grok's forced-spill rule. The in-turn open-question popup branch remains untouched: no task marker, no file pointer, existing Now and ToolResult confirmation.

Rejected: Windows-to-Linux string substitution without transferring bytes; server writes to a mirror-looking path; Docker/SSH copy; a new file-upload RPC; always spilling all Claude/Codex input; routing on the task's requested kind instead of the bound session.

### D-2: persist one immutable, exact fallback body with the input event

Add nullable text `AgentTaskEvent.InputBody`; reuse its existing nullable AgentSessionId to record the recipient for Refined/Replied input events. Save the full LF-normalized **logical input body**, including its task marker, before enqueue. Detail retains its existing readable, 4,000-character summary and existing event type. Only these input events populate InputBody; do not expose it in the ordinary task summary/event DTO. Legacy rows remain null; no fabricated backfill from truncated Detail. The additive EF migration is CLI-generated.

Give each new queue input `ConversationKey = task-input:<task-guid-D>:<event-guid-D>`. A small concrete AgentTaskInputService owns strict parsing, event/session/task validation and fallback reads. This follows the queue's existing typed conversation keys and requires no new queue columns. Keep SourceTaskId null: that field drives completion-note deduplication/stamping and cannot safely be reused for arbitrary steers. A distinct event key also prevents two refinements being coalesced as one message. No input key is inferred solely from user-authored pointer prose.

Add `GET /api/agent-tasks/{id}/inputs/{eventId:guid}`, returning `text/plain; charset=utf-8`, Cache-Control no-store. Require the existing X-Antiphon-Task-Token through AuthenticateAsync, then require the authenticated task to be exactly `{id}` and its session to match the event recipient. An unrelated task, capability, absent/stale token or different-session principal receives a refusal with no body; do not use the permissive public-status ResolvePollingCallerAsync. A nonexistent/mismatched/non-input/legacy-null event is 404, never an arbitrary file read. The API reads InputBody directly and never resolves a caller-supplied path.

Pointers name the exact route and the recipient's existing `ANTIPHON_API` environment base, with the header value taken from `ANTIPHON_TASK_TOKEN` at execution. Never expand the secret into a prompt, URL, event or log. Do not embed the server's default `http://localhost:17202` as if it were runner-local. Code's isolated HTTP fixture proves the route with a synthetic injected base/token; later activation checks the runner's actual API reachability without printing credentials. A misconfigured/unreachable API is reported as unavailable, not a claimed successful fallback read.

InputBody has the existing task-event retention lifetime and survives queue-byte release. Apply the same bounded request acceptance as the existing refine/reply API; do not add an unlimited second ingestion surface. Ordinary task/event responses remain summarized. No automatic deletion policy is introduced.

Rejected: newest-event lookup (races later input); increasing Detail globally (broadens every event surface); truncated Detail; queue bytes that are cleared before the agent reads; rereading a mutable file; a public anonymous body route or a token in the query string.

### D-3: bounded pointers and a safe, durable fallback transition

Extend the refinement formatter with the brief formatter's max-wire/bound-path approach and add equivalent marked blocked-reply rendering. The normal pointer names the relative file and its exact API alternative. A compact rendering must retain the task marker on the instruction line and closing marker, input identity, readable location, and the instruction to apply the amendment without ending merely to acknowledge it. Validate UTF-8 bytes, including non-ASCII body/path inputs, against the recipient SingleWriteMaxBytes **after binding**. Grok's pointer is a single join-safe line; Claude and Codex keep LF formatting. If a verbose pointer does not fit, use compact text; never spill a pointer to another pointer.

Make the runner distinguish a spill failure **only inside WriteSpillIfPresentAsync, before runtime.SendInputAsync**. IO/access failure or the writer's path-admission refusal returns a stable `spill_write_failed_before_input` code, with a bounded non-path detail. Cancellation propagates. Once SendInputAsync is entered, no exception can acquire this code. The client maps this specific code to an Application exception, without changing generic connection/503 handling. Old runners may return a generic error: it remains pending/uncertain under current rules until the runner supports the new code; generic errors are not evidence of zero input.

Under the existing session queue lock, only a validated task-input row may handle that exception. The code proves zero bytes for **this Input frame**, not for an earlier uncertain attempt: reconcile prior attempts first, and allow the transition only at the initial body-write boundary with no unresolved earlier submission. A failure on overlay recovery/retyping after a successful body write in this attempt must retain the original wire text. Persist the eligible row's API-only Body and one Warning event keyed by input event/queue ID in the same transaction, then leave that same row Pending for ordinary delivery (or return a truthful deferred result to send-now). Clear only its matching transient staged object; keep the durable body until the normal receipt rule releases it. The next attempt has the API pointer as its persisted matching text and sends no file payload. Record the stable reason and exact input route, never an IO exception containing host paths. A server restart between fallback commit and typing must replay this same API pointer.

Close all row-bearing paths: WhenIdle flush, EnqueueDeliveringNowAsync where used for a task-input row, and SendNowAsync. Do not change the direct unpersisted Now lane or the question overlay. Keep prior attempt floors/generation evidence; a fallback transition is legal only on the explicit before-input result. No hidden immediate retry in the runner client, no body retype after ambiguous transport failure, and no relaxation of LF/bracketed-paste/separate-Enter or whole-UserPrompt confirmation. If fallback persistence fails, type nothing new and retain recoverable source bytes. Both primary/API pointers stay below the wire limit.

Rejected: fallback on any exception/timeout (can duplicate submitted work); changing typed text behind the queue's back (breaks confirmation); treating a successful Input ACK as a file-read acknowledgement; replacing the queue row or task marker; changing other spill producers.

### D-4: an explicit read complaint becomes an attention note

Add appended AttentionKind `TaskInputUnreadable` and its client mapping. Implement a read-only Attention projection, factored through a pure `TaskInputReadFailure` matcher. For Dispatched/Working/Blocked tasks, find a complete matching UserPrompt for that task's input pointer, then only AssistantText in that prompt's turn/session. A supported complaint must identify the referenced input and contain a bounded read-failure phrase such as `cannot read`, `can't access`, `not mounted`, `no such file`, or `cannot access`. Identify it by the exact file/route (normalize slash style for matching only), or by `the refinement file`/`the reply file` when there is exactly one such owning input. The latter admits the card's wording about the parent directory being unavailable without demanding that the assistant repeat the filename. Another explicit path overrides that implicit referent and must match. The historical card wording is a fixture. Require both facts; a quoted instruction in UserPrompt/ToolResult, another task/path, a previous session/turn, an ordinary provider-stall transcript, or mere lack of activity is not enough.

Also recognize existing legacy marked refinement pointers by their task marker and exact referenced spill path, so a currently unreadable old Windows pointer can be surfaced after activation. Legacy attention does not promise recovery of text that was never stored in full. Do not regex arbitrary user-supplied paths into file reads.

Project one Warning row per task/input with stable ConditionKey, task/session/queue IDs, transcript sequence/time and a bounded evidence excerpt. Actions open the existing task/session drawer; no new send/kill/settle action. Repeated polling must produce the same row and no database writes. The condition remains through later unrelated assistant text in that turn; a later delivered caller input supersedes the episode, and terminal task settlement removes the open-task attention row. Do not clear it merely because the pointer itself was Delivered. This is a conservative explicit-language detector, not a claim to understand every language or paraphrase.

Preserve the existing pending-age digest exactly. Add a controlled-clock regression proving a zero-attempt WhenIdle refinement says `38m old` while the session remains busy. Do not add an age threshold, automatic send-now or completion hold here.

Rejected: screen scraping, inference from silence, an LLM classifier, changing task status on a complaint, raising a global alert, or adding bundle guidance as a substitute for the Attention projection.

### D-5: preserve admission and keep Code serialized where required

Use no new scheduler, provider process or broad test lane. All tests use isolated database schemas, private scratch workspaces, fake runtime input and the real in-process queue/phone-home/runner writer where relevant. Additive input storage must not be mixed into landing evidence fields or public summaries. There is no bundle edit: CARD-0884's 30,000-character guard has shared headroom; any later commissioned bundle change must be net-shorter and re-admitted as a footprint change.

Rejected: slipping in CARD-0682/0731 policy, refactoring settlement/land while editing AgentTaskReplyService, or using a passing full suite to replace the named regression assertions.

## Exact implementation footprint and slices

Only this plan changes now. Future Code is limited to the files below; TestDesign freezes method names/counts and the generated migration identity before Code. No project-reference change is necessary: the existing Antiphon.Tests phone-home spill fixtures already exercise SessionRunner.

| Slice | Exact implementation/test files | Test-first work |
|---|---|---|
| S1 — detecting contracts | Add `tests/Antiphon.Tests/Application/AgentTaskInputSpillTests.cs`, `AgentTaskInputFallbackTests.cs`, `TaskInputReadFailureTests.cs`; add `tests/Antiphon.Tests/TestHelpers/TaskInputSpillFixture.cs`. Modify `client/src/features/attention/attentionVisuals.test.ts`. | Author the 32 named new TUnit executions and two client cases below against existing public entry points. Keep prospective field/enum assertions at HTTP/serialized boundaries so the red slice compiles without production stubs. Existing five regression classes remain read-only. Commit/push, then declared red rounds of CP-1..4. |
| S2 — durable body and runner delivery | Modify `server/Domain/Entities/AgentTask.cs` (InputBody only), `server/Infrastructure/Data/AppDbContext.cs`, `server/Application/Services/AgentTaskReplyService.cs`, `server/Application/Services/DelegationReportFormatter.cs`, `server/Application/Services/SessionMessageQueueService.cs`, `server/Api/Endpoints/AgentTaskEndpoints.cs`, `server/Program.cs`, `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs`, `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`, `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`. Add `server/Application/Services/AgentTaskInputService.cs`, `server/Application/Exceptions/RunnerSpillWriteException.cs`. Generate migration `AddAgentTaskEventInputBody` (its timestamped `.cs`/`.Designer.cs` pair) and modify `server/Migrations/AppDbContextModelSnapshot.cs`. | Implement D-1..D-3 without changing local successful behavior, queue release rules or runner writer admission. The migration timestamp is the only generated filename component; no other migration is authorized. Commit/push; run CP-1/2 once green. |
| S3 — attention and ownership docs | Add `server/Application/Services/TaskInputReadFailure.cs`, `server/Application/Services/AttentionService.TaskInputs.cs`; modify `server/Application/Services/AttentionService.cs`, `server/Application/Dtos/AttentionDtos.cs`, `client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts`, `docs/session-runtime-invariants.md`, `docs/ops-http.md`, and this plan. | Implement D-4; append rather than renumber the attention kind. Record API and pre-input fallback ownership. Commit/push; run CP-3/4 green and scoped Windows CP-5 at this final SHA. |

The new helper fixture owns its schema and scratch root; reuse BridgeQueueHarness/PhoneHomeTestHost through their existing hooks, without editing shared fixtures. RunnerWorkspaceService, RemoteSpillCourier, AgentTaskDispatcher, SessionDeliveryProfile, DelegateCheckProbe, PTY adapters and existing regression classes are read-only dependencies. If Code needs to change one, amend the footprint/collision/verification decisions before doing so. Enum/client mapping comes from the new attention kind, not a UI redesign.

### Same-source-area collisions

Brief-supplied in-flight/queued states are historical. Fresh card GETs in this Plan showed 0788, 0835, 0881, 0883, 0885 and 0886 in Review; that is **not** proof of publication, activation or a free Code slot. No fleet dispatch is performed here. The caller must refresh pipeline, session-runners, runner-defaults, hosts and board-scoped task occupancy immediately before Code admission, use the lower enforced stage/host caps and prefer server2. Windows is only the scoped CP-5 qualification. Never use a concurrency override to bypass a same-stage/source collision.

| Other card | Exact/mapped intersection | Decision |
|---|---|---|
| CARD-0826, brief names Code 3a378666 | Exact `server/Program.cs`, `AppDbContext.cs`, generated model snapshot, `PhoneHomeCommandDispatcher.cs`, Attention service/DTO/client visuals; mapped `schema`, `runner`, `delegation`, `client`. Its checkpoint Cleanup extraction, deploy-server2 and stage bundles are not our edits. | **Serialize Code; 0826 lands first if still admitted ahead.** Re-read its migration/attention numbering and current source before ours. No functional cleanup dependency is claimed. |
| CARD-0788 | Brief explicitly names AgentTaskReplyService as an overlap; its LandApproval/LandService/SourceResolver work also shares mapped `delegation`, with possible schema/shared application-test scope. | **Yes, CARD-0888 Code must serialize with CARD-0788.** Disjoint methods inside AgentTaskReplyService are not a waiver. Use the admitted queue order after 0826; do not require already-finished work to be relaunched. |
| CARD-0883 | No need to edit LandApproval.cs, AgentTaskLandService.cs or AgentTaskLandSourceResolver.cs here, but they share `delegation`; any schema change also intersects. | **Serialize admitted Code** by the same-source-area policy; it is not a logical prerequisite. |
| CARD-0835 | Exact AgentTaskReplyService and schema/snapshot overlap identified in the brief; receipt tooling is a read-only dependency. | **Serialize Code**, preserve its clean-source evidence rules. If already landed, plan against the integrated source without rebasing this pushed branch. |
| CARD-0881 | No bundle or effective-settings edit here, but its AgentTask pipeline/gate implementation shares `delegation`; inspect actual Program.cs/endpoint registration scope as well. Documentation-only overlap is allowed by the area map. | **Serialize its implementation Code**, but its TestDesign/Review/docs-only work can run beside this Plan/TestDesign and disjoint verification. |
| CARD-0885 / CARD-0886 | Their checkpoint tooling/standards and heavy landing/script test files are not edited here; we do not edit stage bundles, tools/Antiphon.Checkpoints or those tests. | **Can run beside Code** on the supplied exact footprints, under build slots and stage caps. Recheck any scope expansion into AgentTaskReplyService, schema or the selected spill fixtures. A green existing driver is consumed, not changed. |

Do not add area-map entries or enlarge a broad tests glob to manufacture compatibility. The Code scope consists of the exact paths above plus the one named generated migration pair.

## Verification design

The operator's October 1 Final Review rule applies: fail only for a regression of existing behavior or a new reachable fail-open/privacy exposure. These assertions trace the changed production boundaries. Do not pad the roster with provider canaries, broad Unit, landing recovery or checkpoint-tool internals. Missing acceptance evidence is reported as pending; it is never relabeled a pass.

### Fixtures and execution census

New test names below are the TestDesign contract: one `[Test]` execution per named method, with no argument expansion, skip or hidden OS gate. Small payload variants inside a method are assertion cases, not additional executions. TestDesign must explicitly amend this census if it selects parameterization.

Use real RefineAsync/AnswerAsync, persisted queue/event rows, real queue flush/send-now and PhoneHomeRunnerClient → PhoneHomeCommandDispatcher → RunnerWorkspaceService over the existing loopback WebSocket fixture. A recording fake runtime replaces the provider. It verifies file existence/content **when input is accepted**, not after an arbitrary delay. Record absence as a fact and assert `runner-file-present` before opening the file, so a missing file is a detecting assertion rather than a fixture exception. Produce a complete synthetic UserPrompt through the established transcript fixture and verify the queue receipt separately. API tests mount only the relevant endpoints on a random loopback port, never real Program or production runner 17204. Use synthetic tokens and payloads, including a sentinel beyond character 4,000. On Linux use two genuinely different scratch roots for server and runner; on Windows use two native roots and include Windows-path strings in foreign-path negative cases.

Clocks are fixed FakeTimeProvider instances for event/queue timestamps, identical-second input races, pending-age and attention episode lifetime. Delivery fixtures coordinate TCS barriers with RunContinuationsAsynchronously and advance a controlled clock only after waiter registration. Deadlock deadlines are diagnostics, never expected-red assertions. No wall-clock 100-ms inference, provider wait, CPU burner or sub-two-second real-time margin.

| ID | New `AgentTaskInputSpillTests` method (15 executions) | Named outcome |
|---|---|---|
| V-1 | `Runner_codex_refinement_writes_complete_body_before_pointer` | `runner-file-present`, `runner-body-exact`, `server-spill-absent`, `relative-pointer`, matching complete UserPrompt. |
| V-2 | `Runner_claude_refinement_uses_the_same_inbox_route` | Same production path and marker facts for Claude; no provider-specific path workaround. |
| V-3 | `Runner_grok_refinement_is_forced_to_a_join_safe_pointer` | Short refinement still spills, exact full bytes, one-line pointer and task marker. |
| V-4 | `Runner_ceiling_ignores_the_modern_desktop_profile` | Body between runner and desktop limits already has its task-input pointer and complete spill bytes at enqueue (`runner-ceiling-spill`), before generic queue fitting; final typed bytes <= 1,024, no second YOUR MESSAGE pointer. |
| V-5 | `Bound_pointer_measures_utf8_after_guid_path_rewrite` | Construct a real straddling boundary; assert pre-bind <= limit and expanded verbose > limit, then final compact <= limit with markers. |
| V-6 | `Two_same_tick_refinements_keep_distinct_paths_and_bodies` | Two queue IDs/paths, each exact body after dispatch, two immutable input events; no completion dedup/coalescing. |
| V-7 | `Local_desktop_refinement_keeps_its_file_and_pointer_contract` | Local absolute timestamped file, complete body, existing inline/large threshold behavior and no remote payload. |
| V-8 | `Runner_blocked_reply_reaches_the_inbox_with_task_attribution` | Working transition, reply watermark, marked WhenIdle row and exact runner body. |
| V-9 | `Runner_small_reply_remains_inline` | Existing threshold, literal answer and marker, no unnecessary spill. |
| V-10 | `Open_question_reply_remains_unmarked_now_and_toolresult_confirmed` | Overlay branch sends literal answer, no task-input pointer; ToolResult is its existing confirmation. |
| V-11 | `Missing_runner_cwd_selects_api_without_a_server_write` | RunnerId present, missing RunnerCwd: API route/Warning and zero local spill. |
| V-12 | `Queued_refinement_still_amends_the_goal` | No session queue row, ordinary goal amendment and existing Refined event semantics. |
| V-13 | `Fallback_input_body_survives_complete_prompt_and_restart` | Clear transient courier and queue receipt bytes, recreate services, GET still equals complete input including tail sentinel. |
| V-14 | `Refinement_send_now_upgrades_the_same_owned_row` | Same queue ID/origin/key/marker/body, correct runner bytes, no second input row. |
| V-15 | `Pending_refinement_age_remains_visible_without_delivery_attempts` | Fixed clock: `38m old`, attempts 0, Working task and unchanged WhenIdle behavior via real DelegateCheckProbe. |

| ID | New `AgentTaskInputFallbackTests` method (9 executions) | Named outcome |
|---|---|---|
| V-16 | `Runner_write_failure_types_only_the_durable_api_pointer` | Real writer failure using an ordinary file at the required directory component; `file-pointer-input-count=0`, API-only persisted/typed pointer and exactly one fallback Warning. Fetch exact whole body. No chmod assumption on Windows/root. |
| V-17 | `Runner_path_refusal_never_writes_outside_the_mirror` | Invalid confined destination is refused before runtime input, no outside file; task fallback route remains readable. Reuse the existing writer guards, never weaken them. |
| V-18 | `Input_failure_after_write_never_selects_fallback` | Runtime records body acceptance then returns an error/connection loss; `fallback-warning-count=0`, unchanged file pointer/attempt floor, retained bytes and normal retry confirmation. Also exercise a later before-input write failure with unresolved prior submission and an overlay-recovery write failure after initial body acceptance: neither may switch wire text. |
| V-19 | `Restart_after_fallback_commit_replays_the_same_api_pointer` | Recreate queue/courier after committed transition; exact same row and wire text, no stale file payload, one Warning. |
| V-20 | `Input_endpoint_requires_the_recipient_task_token` | Missing, stale, another-task and capability tokens refused without canary; correct recipient succeeds. Assertions count as one test. |
| V-21 | `Input_endpoint_binds_task_event_and_recipient_session` | Wrong task/event/session and legacy-null/non-input event return no body; exact route returns full text with no-store. |
| V-22 | `Input_body_is_absent_from_task_summary_events_and_logs` | Oversized body-tail sentinel absent from existing public summary/event DTO and structured logs, present only through authorized exact input read. |
| V-23 | `Fallback_commit_failure_sends_no_replacement_input` | Controlled database failure at the fallback transaction: `replacement-input-count=0`, original source recoverable; no false successful Warning/receipt. |
| V-24 | `Ordinary_spill_errors_keep_their_existing_delivery_behavior` | Brief/channel/non-task-input spills and an old-runner generic error never take the task-input fallback branch; cancellation propagates. |

| ID | New `TaskInputReadFailureTests` method (8 executions) | Named outcome |
|---|---|---|
| V-25 | `Own_assistant_read_complaint_appears_while_working` | Real AttentionService emits Warning TaskInputUnreadable with exact task/input/session and evidence sequence; no state/queue write. |
| V-26 | `Legacy_windows_pointer_complaint_is_visible` | Historical marked Windows pointer and its matching assistant refusal surface; no claim of full-body recovery. |
| V-27 | `Other_task_path_or_turn_does_not_create_attention` | Each wrong identity yields no row. |
| V-28 | `Quoted_user_tool_text_and_provider_silence_do_not_create_attention` | Non-assistant text and CARD-0353 pointer-only/provider-stall facts produce zero rows. |
| V-29 | `Repeated_attention_reads_keep_one_stable_condition` | Same key/time/IDs twice, one row, zero database mutations and zero input calls. |
| V-30 | `Unrelated_assistant_progress_does_not_hide_the_complaint` | Complaint remains in the owning turn despite later generic progress text. |
| V-31 | `Later_delivered_input_supersedes_the_old_complaint` | Before/after receipt with controlled timestamps; old condition disappears only at the new delivery boundary. |
| V-32 | `Terminal_task_removes_open_input_attention` | Task settlement removes this open-task row without changing other attention kinds. |

Client V-33 adds two executions to attentionVisuals.test.ts: `draws TaskInputUnreadable as a warning with a task target` and `keeps unreadable input in the review home bucket`. Include the new kind in the existing all-kinds census. Baseline 22 + 2 = **24 Vitest**, not 24 TUnit. Future 0826 visual additions require an explicit re-count, not a silent floor change.

R-1: unchanged AgentTaskRefineTests (9) protects status/refusal/goal amendment/local spill. R-2: unchanged AgentTaskReplyOverlayTests (7) protects blocked and in-turn reply behavior. R-3: unchanged PhoneHomeSpillTests (4), PhoneHomeSpillTransportTests (3), DurableRunnerSpillReceiptTests (11) protect brief routing, identity isolation, ACK custody, restart repair, screen-only retention, post-binding size and receipt release. These 34 existing results are the focused regression suite.

### Positive controls

Execution belongs to the later authorized Mutation stage, with clean baseline → one production mutation → named assertion red → restore/rebuild green. Do not run mutations in Plan/TestDesign. Each filter is `/*/*/<Class>*/<Method>*` using the class/method named above. No timeout, missing test, compilation error, HTTP startup failure or fixture-only edit is a detecting red.

| PC | Concrete production mutation | Named detecting test/assertion |
|---|---|---|
| PC-1 | Route remote Refine back through desktop File.WriteAllText and omit staging. | V-1 `runner-file-present` / `server-spill-absent`. |
| PC-2 | Select ModernConPty limits despite RunnerId. | V-4 `runner-ceiling-spill`; byte count/input capture proves the wrong path. |
| PC-3 | Measure the temporary path instead of bound inbox GUID path; bypass compact selection. | V-5 `final-wire-byte-limit` on the deliberately straddling fixture. |
| PC-4 | Reuse the first input's path/body for the second same-tick refinement. | V-6 `second-runner-body-exact` and distinct-path assertion. |
| PC-5 | Truncate InputBody to 4,000 or clear it with RemoteSpillBody on receipt. | V-13 `authorized-body-tail-exact` after complete UserPrompt and recreation (run each variant separately). |
| PC-6 | Suppress the task fallback transition for the specific pre-input write failure. | V-16 `api-only-wire` and `fallback-warning-count=1`; make the assertion on persisted/captured outcome after one bounded attempt, never wait for a timeout. |
| PC-7 | Label a runtime input failure as spill_write_failed_before_input. | V-18 `fallback-warning-count=0` and unchanged pointer/attempt evidence after the controlled failure. |
| PC-8 | Type the API pointer before committing its row and Warning. | V-23 `replacement-input-count=0` at the injected transaction failure. |
| PC-9 | Remove endpoint authentication or its exact task/session binding. | V-20 `unauthorized-body-absent` / V-21 `wrong-owner-body-absent` (separate variants). |
| PC-10 | Serialize InputBody into the ordinary event DTO or log it. | V-22 `summary-tail-absent` / `log-tail-absent` (separate variants). |
| PC-11 | Remove attention's assistant-kind or own-path/turn gate. | V-27 `foreign-condition-count=0` / V-28 `quoted-condition-count=0` (separate variants). |
| PC-12 | Omit the new attention projection or reset its SinceUtc on every poll. | V-25 `own-condition-count=1` / V-29 `condition-time-stable` (separate variants). |
| PC-13 | Fit blocked/open-question input through the refinement pointer unconditionally. | V-9 `inline-answer-exact` / V-10 `overlay-answer-unmarked` (separate variants). |
| PC-14 | Drop the new visual entry or route Warning to the wrong home bucket. | V-33's two named Vitest cases at their label/target/bucket assertions. |

PC-1 is also the historical red-first reproduction. Existing brief/courier controls need no new mutation battery just because they are dependencies. TestDesign should freeze the final distinct variant count and custody commands, without inventing further guards outside this footprint.

### Test-first order and known flakes

S1 commits compiling tests before implementation; invoke CP-1..4 as the declared red-first exception to their final After groups. V-1, V-11, V-16 and V-25 must show named missing-behavior assertions. Baseline-compatible tests can be green; that does not make them stubs because their PCs name production changes that break them. New-route tests may initially fail the explicit HTTP-200/body assertion, then must prove exact-body/auth assertions green after S2. Author no dummy production implementation merely to make tests compile.

S2 commits the feature/migration, then CP-1/2 green. S3 commits attention/docs, then CP-3/4 green and Windows CP-5. Later changes to S2 code require its affected rows again at the final SHA with the reason recorded; otherwise earlier clean certificates remain tied to their source commits. Final Review qualifies the integrated final commit using the same closed ordinary rows. Code never changes source during a running row. No full suite or routine repeat battery is commissioned.

Known unrelated failures, excluded by these filters: CARD-0791/0794 (provider readiness/snapshot/broken pipe), CARD-0818 (CheckpointExecutorLogTests concurrent callbacks), CARD-0820 (temp contention/cleanup and ResilienceBudgetTests slow-attempt budget), CARD-0828 (checkpoint ownership/executor races), CARD-0848 (DetachedLauncherTests executor survival), CARD-0879 (AlwaysOn DetectTimeout/PaneClosed), CARD-0889 (RunnerCodexAdapterSubmitConfirmTests' two-second confirmation budget), CARD-0890 (C448_V15 real-worker module-initializer failure, seven Linux cases), CARD-0900 (RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit fixed ten-second readiness), CARD-0742 (RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision), CARD-0757 (ScaledTimeProviderTests under load), CARD-0751 (HttpResilienceRegistrationTests/HttpReadResilienceTests under load). This is a historical known-failure list, not a claim every item is nondeterministic or still open in the same source. None excuses a failure in this roster or authorizes a new skip.

A demonstrated new nondeterministic failure may justify method-scoped repeats, at most three normal and two loaded observations unless separately commissioned. Those are maxima, not required runs. Observe existing load; generate no CPU burners. Record every extra command, count, first failure and reason. Exit 4 build-slot timeout is reported, never bypassed.

### Checkpoints

Closed ordinary list. Each TUnit row means one isolated build and one literal filter via `dotnet run --project`, never dotnet test. Markdown `\|` is decoded by the checkpoint importer to a literal `|`; a manually quoted shell filter uses plain `|`, never the Markdown backslash. Every class operand has trailing `*`. CP-4 is the documented non-TUnit row: one positional Vitest command, with Build/Min n/a. Opposite-OS rows are unselected, not passing skips. Final counts are prospective; recount fresh TRX method/argument names after implementation and after prerequisites land.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c888-linux-input/` | linux-task-input | `/*/*/(AgentTaskInputSpillTests*)\|(AgentTaskRefineTests*)\|(AgentTaskReplyOverlayTests*)/*` | V-1..V-15, R-1, R-2 | Linux 31 = 15+9+7; 0 failed/skipped; Windows 0 selected | 31 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c888-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c888-linux-fallback/` | linux-input-fallback | `/*/*/(AgentTaskInputFallbackTests*)\|(PhoneHomeSpillTests*)\|(PhoneHomeSpillTransportTests*)\|(DurableRunnerSpillReceiptTests*)/*` | V-16..V-24, R-3 | Linux 27 = 9+4+3+11; 0 failed/skipped; Windows 0 selected | 27 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c888-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c888-linux-attention/` | linux-input-attention | `/*/*/TaskInputReadFailureTests*/*` | V-25..V-32 | Linux 8; 0 failed/skipped; Windows 0 selected | 8 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c888-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S3 | n/a | input-attention-client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts` | V-33 | Linux 24 Vitest = 22+2; 0 failed/skipped; 0 TUnit; Windows 0 selected | n/a | 2 | true | n/a |
| CP-5 | S1-S3 | `tests/Antiphon.Tests -> bin-c888-windows-input/` | windows-task-input | `/*/*/(AgentTaskInputSpillTests*)\|(AgentTaskInputFallbackTests*)\|(AgentTaskRefineTests*)\|(AgentTaskReplyOverlayTests*)/*` | V-1..V-24, R-1, R-2 across OS/filesystem | Windows 40 = 15+9+9+7; 0 failed/skipped; Linux 0 selected | 40 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c888-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

## Acceptance mapped to CARD-0888

| Card requirement | Planned evidence |
|---|---|
| Fix 1 / acceptance: Linux Codex oversized refinement is placed in runner inbox and pointer is valid | V-1/4/5/6/14, CP-1. Real writer before fake runtime input, separate server/runner roots, full body equality and complete UserPrompt; Claude/Grok parity V-2/3. |
| Fix 2 / acceptance: runner write failure yields API pointer and a recorded task event | V-11/13/16..24, CP-1/2. Exact immutable body beyond 4,000 characters, restart survival, pre-input proof, same-row fallback Warning, authenticated route. |
| Acceptance: server-local desktop task keeps today's behavior | V-7, unchanged R-1/R-2, CP-1 and Windows CP-5. No claim of Windows execution from a Linux path-string test. |
| Reply spill and attribution | V-8/9/10/14 and R-2/R-3. Blocked reply exercised separately from Refine; overlay preserved. |
| Fix 3: attention note when session says file is unreadable | V-25..33, CP-3/4, with exact positive transcript evidence and negative own-turn/privacy controls. |
| Fix 4: Pending age and never-idle refinements | Existing age preserved and V-15 pinned. New aggregate/escalation, urgent send flag and stale-brief settlement explicitly remain CARD-0682/0731. |

## Execution, activation and rollback

Use the CARD-0723 checkpoint **tool**, not the CARD-0866 template's task-specific direct-driver exception. From a committed slice, bootstrap/run the tool under `scripts/build-slot.ps1` if dotnet run would build it; the checkpoint drivers then obtain their own row leases. A separately listed bootstrap/EF migration-generation build is an authorized setup exception, reported with its reason and slot receipt, never hidden as another verification row. Do not hold a launcher lease while waiting for child row leases: prepare the tool under a slot, then use its built output with `dotnet run --no-build --project tools/Antiphon.Checkpoints -- ...`.

One tool run per committed slice group, selecting exact rows and the committed SHA:

```powershell
# S2 Linux, after commit/push; use the tool's built output from its leased bootstrap.
dotnet run --no-build --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-01-card-0888-runner-bound-refinement-spill-plan.md --rows CP-1,CP-2 --expected-source-sha <full-slice-sha>
# S3 Linux, after commit/push.
dotnet run --no-build --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-01-card-0888-runner-bound-refinement-spill-plan.md --rows CP-3,CP-4 --expected-source-sha <full-final-sha>
# Focused Windows lane at the final source SHA.
dotnet run --no-build --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-01-card-0888-runner-bound-refinement-spill-plan.md --rows CP-5 --expected-source-sha <full-final-sha>
```

For S1 use one preparatory run selecting CP-1..4, recording expected named reds and its source SHA. Use supported `wait <run-id>` with a short foreground window until exit is not 75; do not end the task while detached verification is running. Preserve every CHECKPOINT line unedited, actual OS/counts/skips/failures, slot status, `dirty=0`, source/sourceState/buildSource, source.json/report.json, TRX and first failed assertion. Validate receipts against their exact source SHA. A missing Windows host leaves CP-5 pending, not skipped-green. Antiphon.Tests and any separately required Antiphon.Agents.Pty.Tests must never overlap; this plan commissions no Pty suite.

Code applies the generated migration only to its isolated test database. Production migration/server activation and runner rollout are later operator/orchestrator operations from the owning checkout/runbooks, after ordinary Review and confirmed publication. The server and runner both need the new before-input error contract; a server-only land cannot qualify that fallback. Confirm actual source/build identities and API reachability on the target runner. No real provider launch is required for ordinary acceptance and none is authorized by this Plan.

Rollback is a forward commit, never resetting/rebasing the published task branch. Keep the additive nullable InputBody column/data during binary rollback so recorded inputs are not destroyed. Reverting the feature restores the known unreadable-path defect and must reopen its evidence; do not claim a rollback fixes it. Do not rewrite historical events, delete provider state, weaken runner confinement or change shared local ports.

### Cost and handoff

Estimates, not measurements: final ordinary floor **40 minutes** (10+10+8+2+10), comprising Linux **66 TUnit + 24 Vitest**, Windows **40 TUnit**, total **106 TUnit + 24 Vitest**. Four isolated TUnit builds are included. S1's declared Linux red rounds add **30 minutes**, giving **70 minutes** planned Code verification before actual failures/slot waits. Authoring estimate **90–150 minutes**, plus 5–10 minutes for leased bootstrap/migration setup: budget Code around **165–230 minutes**, or commission an explicit continuation slice before the task ceiling rather than dropping rows. Windows scheduling is external to the Linux task and must be called out.

Mutation is separately commissioned after Review/land under SourceLanding custody. The fourteen PC groups above contain **21 distinct variants**: PC-5 and PC-9..14 have two each; the other seven groups have one each. Nineteen are TUnit production variants and two are client variants, for 57 TUnit phase invocations and six Vitest phase invocations (baseline/red/restored-green). A planning allowance of three minutes per isolated TUnit build/phase, one minute per client phase and fifteen minutes for custody/restoration gives **192 minutes**; these are estimates, not measured runtimes. TestDesign freezes the exact method filters and confirms that census before Mutation dispatch. Do not charge mutation runs to the ordinary Code floor or require repeated whole green suites.

TestDesign should validate the queue fallback call sites, strict endpoint authorization, additive migration/DTO privacy boundary, old-runner refusal behavior, isolated fixture seams, exact 32-new/34-existing source census and runnable five-row manifest. Recheck collision state and prerequisite changes. No operator design decision is required to proceed with these conservative defaults.
