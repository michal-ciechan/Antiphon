# CARD-0407: resolve scoped internal questions without a human round trip

Date: 2026-09-08. Plan task: `31740fa8`. Code baseline: `4db2076b`.
Status: ready for TestDesign; implementation and acceptance have not run.

## Outcome

Add caller-declared internal decision grants to an AgentTask's dispatch and a
synchronous structured question check that consumes those grants. A matching
internal question receives a concrete continue answer in the worker's current
turn. It neither opens a human popup nor settles the task Blocked. A question
outside that grant goes through AskUserQuestion, or the existing structured
Blocked fallback when the provider cannot ask in-turn.

Change the actual generated brief and delegate bundle together. This is a
dispatch contract plus a deterministic mechanism, not a prose classifier over
blocked reports. The server does not infer authorization from an English goal,
the word "internal", a file extension, or the question's recommended option.

## Ground truth

The full live cards CARD-0407, CARD-0294, CARD-0033, CARD-0122, CARD-0123 and
CARD-0159 were read for this plan. Incident facts below come from CARD-0407;
the Gym Stat checkout and production backup were not inspected or executed.

| Card assumption / requirement | What this checkout does | Consequence |
|---|---|---|
| A Grok worker parked on a mechanical deploy fix | CARD-0407 records task `42bb5ffb`, CRLF in a bash here-string, a blocked go-ahead, and eventual `.gitattributes` commit `142de00` | Reproduce the question/continuation behavior in an isolated fixture; do not repeat a production backup as the test |
| The report instruction is too broad | `server/Application/Services/DelegationReportFormatter.cs`, `ReportingContract`, says blocked "if you need a decision or an answer to continue"; `server/Bundles/delegate-basics.md` repeats it | Both executable instruction sources must change |
| Existing authority might already solve this | `BlockedNote.StandingAuthorityBlock` embeds free text; `ContinueWithAuthorityAsync` replays it only on an already Blocked question | Useful compatibility path, but no bounded internal scope or synchronous decision check |
| CARD-0294's explicit once-only auto-continue exists | At this baseline, API DTO/entity accept/store `AutoContinue`/`AutoContinueOnWait` and expose `AutoContinuedAt`. Entity/DTO comments explicitly call firing a follow-on. There is no runtime consumer/writer of the once-only flag/stamp, and `scripts/delegate.ps1` has Authority/Continue but no AutoContinue parameter | The card's shipped claim exceeds this checkout. Preserve the specified once-only contract; do not make CARD-0407 depend on CARD-0294 S3 or silently implement it here |
| Grok supports an in-turn question | `GrokQuestionTool` and `AgentTaskReplyService.HasOpenQuestionToolAsync` identify an unclosed `ask_user_question`; `AnswerAsync` can answer a Working/Dispatched task without changing its status | Retain this human-question path; the new automatic path answers synchronously before opening a popup |
| A generic typed "continue" is safe | `AgentTaskReplyOverlayTests` and CARD-0159/0241 distinguish a popup ToolResult from a new UserPrompt. `GrokQuestionPopup` still has empty measured chrome literals | Do not build a new popup selector, blind Now send, Esc action, or screen heuristic |
| CARD-0033 avoids the wait | `BlockedContextBuilder` projects only Blocked tasks; `BlockedQuestionCard.tsx` provides question, context and reply/continue actions | It reduces the cost of answering but still needs a human. Reuse it for real blockers |
| NeedsDecision could carry this | `docs/agent-card-lifecycle.md` separates card state from AgentTask state; CARD-0122/0123 concern card decisions | No card status, board column, or tracker transition is needed |
| Scope is an authorization boundary | Existing `AgentTask.Scope`/area leases warn about collisions and record drift; AGENTS explicitly says writes are not enforced | Add a separate grant field. Never reinterpret `-Scope`, unknown area names, or scope drift as permission |
| All workers have a fresh task token | `AgentTaskDispatcher` transfers the prior token hash on warm reuse; pinned standing dispatch discards the new task token. `AuthenticateAsync` also recognizes session-scoped tokens | Accept the current worker task binding or the exact current worker session binding; detect a standing seat without either before promising the helper |
| Every final question is structured | `ClassifyReportAsync` accepts explicit tokens, then `LooksLikeAQuestion`, then the existing nudge rules | Keep these settlement rules. Do not auto-answer a report by parsing its wording |

Related designs: [CARD-0294 authority and continuation](2026-09-03-card-0294-authority-continue-and-blocked-escalation-plan.md),
[CARD-0159 settlement evidence](2026-08-30-card-0159-settlement-positive-evidence-plan.md),
[CARD-0033 answer in place](2026-08-26-card-0033-answer-blocked-delegate-plan.md).

## Decisions

**D-1 — Explicit grants at dispatch, empty by default.** Add optional
`internalDecisionPolicy` to `CreateAgentTaskRequest`, supplied through
`delegate.ps1 -InternalDecisionPolicyFile <UTF-8 JSON>`. Store a validated,
versioned snapshot on the task, including server-resolved granting caller and
creation time. The caller names permitted categories, exact repository-relative
paths, and the behavior that must be preserved. This is a declaration within the
assignment's existing authority; composing it does not require a new human
approval. Update the orchestrator bundle to supply it for assigned mechanical
work. An omitted policy grants no automatic answer through this new mechanism.

Rejected: default-true `InternalOnly`, automatic classification of the goal, or
enabling legacy `AutoContinue` for every Code/Deploy task. Each erases the
distinction the card requires. A task can include product work and still grant
specific mechanical repairs; the grant applies to the pending choice, not a
blanket classification of the entire task.

**D-2 — Three bounded categories, with common exclusions.** V1 accepts only:

| Category | Permitted local choice within explicit paths | Excluded examples |
|---|---|---|
| `LineEndings` | LF/CRLF representation repair and `text`/`eol` attributes for explicitly named target files, preserving intended executable text | Repository-wide renormalization, broad attribute patterns, filters, binary/text reclassification of unrelated files, encoding/BOM changes |
| `ShellTransport` | Quoting, escaping and here-string/line-continuation repair that restores the assignment's existing intended command and arguments | Different host/database, pg_dump options, backup content/retention, credentials, deployment policy, bypassed worktree/ownership guards |
| `BuildTestHarness` | Local build/test invocation, isolated fixture setup or helper portability that preserves the tested behavior and verification strength | Looser assertions, skipped tests, wider timeouts/retries to hide red, production data use, dependency/runtime upgrades, changed shipped configuration |

There is no `OtherInternal` catch-all. A future category needs a reviewed contract
and tests. An explicitly declared product behavior, data, UX, public-contract,
security, operational-policy or new external-action/spend impact, and any unknown
impact, always goes to a human. A small impact is still an impact. Local editing
of a deploy script is distinguishable from permission to deploy it: this grant
does not itself authorize production execution, data changes, secrets access,
messages, destructive operations, landing, or restarting shared services.

**D-3 — A structured question answered before a human tool opens.** Introduce
`scripts/task-question.ps1 -Task <id> -QuestionFile <path>`, backed by
`POST /api/agent-tasks/{id}/decision-questions`. It returns JSON immediately:
`Continue` or `NeedsHuman`, with stable reason, question id and applicable grant
id. It is the provider-neutral equivalent of a structured go-ahead question.
The delegate calls it only for an actual uncertainty/go-ahead, not for every
edit. Routine already-authorized work continues as usual.

For `Continue`, the delegate performs the named local repair and verification in
the same turn. For `NeedsHuman`, it uses its available AskUserQuestion/equivalent
and waits for the answer before dependent work. If there is no usable native
question tool, it gives the question, proposed action, impact and missing
authority in the final report and settles Blocked through today's path.
Independent work can continue while a native asynchronous question is pending.

Rejected: opening a Grok popup and then trying to dismiss/answer it automatically.
The current question transport is provider-specific and CARD-0159 demonstrates
why an unrelated UserPrompt/TurnEnd must not count as progress or completion.
The new path has no terminal delivery and needs no new provider normalizer.

**D-4 — Grant checks are deterministic; semantic claims remain explicit.** The
server checks identity, current attempt, policy version, grant id, category,
all paths, attribute targets and impact fields. It does not ask a model whether
an English authority "matches" a question. The delegate must describe why the
proposed edit preserves the dispatch's stated behavior, and report verification.
That explanation is audit evidence, not a text matcher.

This is not a sandbox or a proof that arbitrary code has zero semantic impact.
The worker is still trusted to describe its proposal honestly, just as it is
trusted to implement and report work. The concrete improvement over inferred
judgment is that the dispatcher fixes the allowed decisions before the worker
asks, and the server refuses missing categories, paths, mixed/unknown impacts
and self-expanded grants. Filename-only approval is specifically insufficient.

**D-5 — Preserve CARD-0294 as a separate authority.** Do not set, consume, reset,
or inherit `AutoContinueOnWait`/`AutoContinuedAt` when resolving an internal
question. Its explicit per-dispatch, at-most-once design stays intact, including
human escalation on a second wait if S3 subsequently lands. `-Continue` remains
an explicit caller action. A `NeedsHuman` verdict must not invoke it or fall
through to legacy automatic authority replay. If a later checkout contains S3,
pin this precedence in its integration tests rather than combining counters.

Multiple distinct permitted mechanical questions can be answered in the current
turn without spending a new model turn or granting broader work. Repeated
submission of the same question is an idempotent read of its recorded answer.
A new id for an unchanged question does not turn a denial into permission.

**D-6 — No new waiting state or decision UI.** The automatic question is an
audited synchronous interaction, not an AgentTask settlement. It leaves status,
CompletedAt, report evidence, report/result fields, stage handoff, reply watermarks
and card state unchanged. Native human questions and Blocked fallback retain
their current answer/surfacing routes. A task detail shows the immutable grant
and compact question-check history; do not add an approval modal or a board column.

**D-7 — No retroactive activation.** Existing tasks have no grant. Same-task
retry/escalation keeps the original immutable policy, with questions bound to
the current attempt and session. New tasks, `-OnAgent` follow-ups, stage changes,
specialists and auto-created Merge children do not inherit it. A worker cannot
edit its grant using Refine, a report marker or a repository file. Fresh dispatch
is the v1 route for a materially different authorization.

## Dispatch and question contracts

The JSON examples are proposed wire contracts, not commands supported today.
The policy's `preserve` is authored by the dispatching caller from the assignment;
it is not presented as a verbatim human quote.

```json
{
  "version": 1,
  "grants": [
    {
      "id": "backup-transport",
      "categories": ["LineEndings", "ShellTransport"],
      "paths": ["scripts/deploy-gym-stat.ps1", ".gitattributes"],
      "attributeTargets": ["scripts/deploy-gym-stat.ps1"],
      "preserve": "Restore the existing production-backup command's intended arguments. Keep backup target, data, credentials, retention and deployment guards unchanged. Local edits only."
    }
  ]
}
```

Use exact repository-relative file paths in v1, including a proposed new helper
when needed. Reject absolute/drive/UNC paths, traversal, wildcards, directory-wide
grants, duplicate ids and unsupported versions. Normalize separators and dot
segments with a dedicated matcher; do not use the advisory lease prefix matcher.
Path comparison follows the task repository's filesystem rules and must not
authorize a sibling prefix. Any target resolving through a link outside the
repository is denied. Do not accept an arbitrary repository root from the worker.

`.gitattributes` is permitted only for `LineEndings`, with an explicit nonempty
`attributeTargets` subset of grant paths. A proposed `.gitattributes` change must
declare those exact targets and only `text`/`eol`; touching the containing file
does not authorize other attributes or targets. No mass checkout/renormalization
is implied. For ShellTransport the script path is allowed, `.gitattributes` is not.

Limits: at most 16 grants, 64 distinct paths per policy, 1,000 characters per
`preserve`, and 20,000 characters for the canonical policy JSON. Empty policy
normalizes to absent. A grant needs categories, paths and preserve; unsupported
categories or an invalid grant yield 422 before task creation. Reject nonempty
grants on ReadOnly workspaces and roles whose contract forbids edits (Review,
Investigate, Test and specialists). Code/Debug/Custom/Deploy and writing
Plan/TestDesign/Docs/Commit/Merge roles may carry grants within their own brief;
role eligibility never gives permission by itself.

Example internal question:

```json
{
  "requestId": "a22f6300-654d-4102-a25a-48886e79371e",
  "attempt": 1,
  "grantId": "backup-transport",
  "category": "LineEndings",
  "paths": ["scripts/deploy-gym-stat.ps1", ".gitattributes"],
  "attributeTargets": ["scripts/deploy-gym-stat.ps1"],
  "attributes": ["text", "eol"],
  "impact": "None",
  "question": "May I normalize this script to LF and pin its eol attribute?",
  "proposedAction": "Normalize this script only and add its exact text eol=lf entry.",
  "preservationEvidence": "The bash continuation bytes change; the intended docker exec/pg_dump arguments remain the same. Verify with an isolated argument-capture stub."
}
```

`impact` is a required non-flags enum: `None`, `ProductBehavior`, `Data`, `Ux`,
`PublicContract`, `Security`, `OperationalPolicy`, `ExternalActionOrSpend`,
`Mixed`, `Unknown`. Omitted is invalid, never None. Any value except None gives
`NeedsHuman`. Question/action/evidence are required bounded text (500/1,000/2,000
characters); the entire request is capped at 10,000 characters. Unknown JSON
fields that could hide additional requested effects must be rejected on these
new DTOs. No arbitrary command body is executed by this endpoint.

Admission order and response:

1. Authenticate the token and require either `caller.Task.Id == resolved id`
   (including the dispatcher's warm-pool hash transfer), or a session-scoped
   principal whose session is exactly this task's current `AgentSessionId`.
   Require the current attempt, live attached session, status Working/Dispatched
   and a unique active task binding for that session. `AuthenticateAsync` already
   supports these identities; `MayDelegate` is not this self-only operation's
   gate. Reject absent, capability, sibling and parent-session tokens here. They
   may create grants at dispatch, but cannot impersonate the worker's question.
   Invalid identity is 403; stale/ambiguous task, attempt or session is 409.
   Do not fall back to public/polling authentication.
2. Validate schema and normalize the payload. A malformed payload is 422, not
   an implicit approval. The helper treats transport/auth/schema errors as
   unresolved and never prints Continue.
3. Under a task-row lock/transaction, recheck attempt/status, then resolve the
   request id. Exact duplicate returns the stored result; different canonical
   content under the same id is 409 `decision_question_changed`.
4. Evaluate the immutable policy. Continue requires every declared effect to
   fit one named grant and category and `impact == None`. Otherwise persist
   NeedsHuman with a fixed reason such as `no_grant`, `category_not_granted`,
   `path_not_granted`, `attribute_not_granted`, or `human_impact`. Do not combine
   partial grants to cover a mixed request. `preserve` cannot be changed here.
5. Persist the check and its task timeline event in the same transaction before
   returning. Publish `AgentTaskChanged` after commit. Example result:
   `{"questionId":"...","disposition":"Continue","reason":"dispatch_grant","grantId":"backup-transport","answer":"Proceed with the proposed local repair under backup-transport; preserve its stated behavior and report verification."}`.

Persist `AgentTask.InternalDecisionPolicyJson` (nullable, immutable) and a new
`AgentTaskDecisionQuestion` entity/table with task id, attempt, session id,
request id, canonical payload/hash, policy version/hash, grant id, disposition,
reason and creation time. Unique `(AgentTaskId, Attempt, RequestId)` is the
cross-request/restart idempotency guard. A disposition is the result of the
check, not a second human-answer lifecycle; NeedsHuman remains historical even
after the worker gets a native answer. Expose history on the existing detail
read with a bounded page/query if needed; keep report Result separate.

Append `DecisionQuestion` to `AgentTaskEventType` using the current next value at
implementation time. Event detail summarizes question, disposition and grant;
the typed row is authoritative. Do not log tokens or reproduce secret-bearing
command output in evidence. Existing event rendering can display the compact
entry; client enum totality and API type tests must cover the addition.

## Actual instruction changes

`BuildBrief` must place the validated grant and helper's absolute path/usage in
every applicable dispatch, including spill-file briefs and warm/standing-session
reuse. Resolve the helper from the Antiphon installation/checkout that owns it,
not from the target project: the incident runs in Gym Stat, which has no Antiphon
scripts. Keep the worker's target repository root separate from the helper path.
Do not depend on a new bundle reaching an already warm process. The grant block
includes attempt id and policy version; it contains no secret.

A pinned standing seat is eligible only when its launch has a usable
session-scoped identity bound to that current session. Preflight a requested
grant against that fact; a seat lacking both supported identity forms refuses
the grant-bearing dispatch with `internal_decision_identity_unavailable` before
typing the brief. Explain that a normal cold/warm worker supports the requested
policy; do not silently reroute the task. Never put a replacement token in a
brief, command argument, question file or transcript. This check does not change
ordinary grant-free standing dispatch. Pin cold, warm-rebound, authenticated
standing and credential-less standing cases independently.

Proposed shared question instruction, included in the generated generic contract
and kept consistent with `delegate-basics.md`:

> Do not end the task merely to ask permission for an implementation detail.
> For a pending internal go-ahead, use the supplied structured task-question
> helper with the dispatch grant, affected paths, proposed action and impact.
> If it returns Continue, make the scoped repair and keep working in this turn;
> record the decision and verification in your final report. Do not open a human
> approval popup for that resolved choice. If it returns NeedsHuman, or coverage
> or impact is unknown, ask through AskUserQuestion or your available structured
> question tool. Wait for that answer before dependent work. When no usable
> in-turn question path exists, report the precise question, proposed action,
> impact and missing authority and end with the blocked token. Never turn a
> product, data, UX or public-contract choice into an internal repair to continue.

Replace the generic verdict sentence with:

> End with done when the assigned work is complete, blocked when unresolved
> input or an external dependency prevents further work and must be handed back,
> or failed when you could not do the work. A grant-resolved internal question is
> not a blocker. An unanswered human question is not completion.

Also revise `BlockedNote.StandingAuthorityBlock` and `ContinueMessage` so their
"not covered => blocked" wording points to structured questioning first. Do not
change the meaning of explicit authority replay. Keep specialist report
vocabularies and stage/finding/report tokens unchanged. The stage handoff's
`next: decide` remains a completed artifact under defaults; unresolved necessary
human input still justifies blocked.

The orchestrator bundle and the delegate CLI reference must say: provide narrow
grants for anticipated mechanical choices, including exact `.gitattributes`
targets when line endings may need repair; no extra human round trip is needed
to express already-authorized internal work. Missing grants are visible in task
detail, not guessed by the harness. Use the new flag in the incident-shaped
acceptance dispatch; merely publishing a policy schema would not fix this wait.

## Implementation slices

| Slice | Files / work | Named test owners |
|---|---|---|
| S1: immutable grant contract | `server/Domain/Entities/AgentTask.cs`; new domain policy/category types; `server/Application/Dtos/AgentTaskDtos.cs`; `AgentTaskService.cs` create/retry/escalation/follow-up/merge paths; new pure `InternalDecisionPolicy.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated migration and snapshot | `InternalDecisionPolicyTests` (new), `AgentTaskServiceIntegrationTests`, `AgentTaskCallerResolutionTests` |
| S2: structured check | New `AgentTaskDecisionQuestion` entity, DTOs and `AgentTaskDecisionQuestionService`; new self-only route in `AgentTaskEndpoints.cs`; DI in `server/Program.cs`; append event enum; transactional/idempotent storage from S1 migration if implemented together | `AgentTaskDecisionQuestionTests` and `AgentTaskDecisionQuestionIntegrationTests` (new), API authentication tests using the existing refusing runner |
| S3: delivery and operator front door | `scripts/delegate.ps1` file-form policy flag; new ASCII-only `scripts/task-question.ps1`; `AgentTaskDispatcher.cs` standing identity preflight and helper location; `DelegationReportFormatter.cs`; `BlockedNote.cs`; `server/Bundles/delegate-basics.md` and `orchestrator.md`; keep Check/Distill/Diagnose contracts separate | `DelegateScriptInternalDecisionTests` (new), `DelegationUnitTests`, `DelegateBundleLaunchTests`, `AgentTaskStandingAgentDispatchTests`, `AgentTaskPoolTests`, `CodexDelegateDispatchTests`, `GrokDelegateDispatchTests`, `DelegationBriefCeilingPtyTests` |
| S4: visible evidence and end-to-end acceptance | `client/src/api/agentTasks.ts`, task drawer/event rendering only as needed for grant/history; affected event visual mappings; `docs/orchestration-loop.md`, `docs/ops-http.md`, `docs/antiphon-api.md`, `.claude/skills/antiphon-delegate/SKILL.md` | New client policy/history tests, existing task drawer tests; incident-shaped deterministic worker harness; `AgentTaskReplyOverlayTests`, `AgentTaskReplyIntegrationTests`, `AgentTaskSettlementRaceTests`, `AgentTaskDeliveryWatchdogTests` regressions |

S1 precedes S2; S3 publishes a callable helper only after S2 exists. Land as one
coherent feature or keep partial slices inert: do not ship instructions pointing
to an absent endpoint. S4 completes acceptance. Each Code slice commits and
pushes its actual result. Plan/TestDesign artifacts must land before the next
stage's worktree is dispatched, per the orchestration owner.

Read the area owners before implementation: project-context, orchestration-loop,
agent-card-lifecycle, agent-credentials, testing-and-build and, if touching question delivery,
session-runtime-invariants plus the Pty owner. Do not change the delivery layer
merely to implement this plan.

## Acceptance requirements for TestDesign

TestDesign is a separate stage. It must convert these requirements into the
repository's `V-n`/`R-n`/`PC-n` verification design with exact filters and positive
controls; these are coverage requirements, not claims of executed tests.

1. **Incident:** dispatch a synthetic Grok/Custom task for a local deploy-script
   repair with the sample grant, submit the LineEndings question, get Continue,
   apply the fixture-only repair and reach a marked final report. Assert zero
   human answer requests, zero Blocked events, no extra UserPrompt/continue
   queued message, no premature CompletedAt/settlement, and one decision record.
   Check the helper actually received the dispatch grant through the spill file.
2. **Other allowed choices:** ShellTransport with unchanged captured argv and
   BuildTestHarness portability with unchanged assertions receive Continue.
   A CRLF fixture plus exact `.gitattributes` entry must preserve unrelated bytes.
   All execution remains against stubs/temp files, never production docker/pg_dump.
3. **Human boundary:** product/data/UX/public-contract and other non-None impacts,
   Mixed, Unknown, missing grant, wrong category, out-of-scope path, broad
   attributes and insufficient evidence fields cannot return Continue. Same
   script path with a backup-retention/database/argument policy change goes to
   a human. "Internal" in prose and a recommended Yes never grant authority.
4. **Provider shape:** returned Continue remains in-turn and cannot count as a
   report. NeedsHuman uses a native structured question where available, and
   waits. Existing matching Grok ToolResult confirms an explicit human reply;
   an unrelated result does not. For a provider without a usable question tool,
   the revised contract produces an explicit structured Blocked fallback.
5. **Authorization and lifecycle:** own worker token and exact current worker
   session token accepted, including warm hash transfer; missing, sibling,
   parent, stale-session/attempt and capability impersonation denied. A standing
   seat without usable identity refuses grant-bearing dispatch before delivery. Test
   cancel/settle racing with question admission and duplicate posts across
   independent service scopes/restart. No orphan session, card transition,
   replayed approval or second event from retries. ReadOnly/Test/Review and
   specialists cannot receive edit grants.
6. **Compatibility:** absent-policy tasks keep existing behavior; legacy fields
   and once-only semantics are unchanged; new answers never consume/reset that
   counter. Same-task retries retain grants but not old-session answers; new
   follow-ups/merge children never inherit them. Grant files edited after create
   cannot alter the stored snapshot. Preserve settlement positive-evidence,
   report nudges, stage tokens, scope-warning and CARD-0033 reply-round tests.
7. **Instruction delivery:** exact old unconditional missing-decision language
   is removed from all generic instruction builders named above; both direct
   and spill briefs include the effective grant and real helper path, including
   a target project without an Antiphon scripts directory. Warm reuse cannot
   depend on the old process's bundle. No-policy and specialist brief cases are
   explicit. A fixture agent must consume the contract/helper response, not
   merely assert that an arbitrary prompt contains the word "internal".

Use the existing isolated database/runner harnesses. TUnit runs through
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0407/ -- --treenode-filter "/*/*/<NamedClass>/*"`;
client tests through `pwsh -File scripts/test-client.ps1 <test-file>`. Name
scoped classes, require nonzero executed test counts, and verify any inherited
failure at the base. Process-spawning fixtures require the project-local limiter;
do not run Antiphon.Tests and Pty.Tests concurrently. Clean only verified
workspace-contained `bin-card0407` directories. No build/tests are necessary
for this planning-only document.

Before claiming the behavioral incident fixed, perform one explicitly dispatched
Grok canary on an isolated mechanical fixture using the real brief and helper,
plus a product-impact negative. Preserve the question record and transcript
showing continued tool work and the eventual report. No real deploy or provider
reroute is needed. If the required provider is held/unavailable, report this
acceptance as pending; a fake harness is not proof a real delegate used the new
instruction. TestDesign should separate the deterministic regression gate from
this behavioral acceptance gate.

## Rollout, limits and remaining decisions

No human decision blocks writing or implementing this plan. The defaults above
are deliberate: explicit grant, empty old-task policy, three categories, exact
paths, synchronous answer, existing human fallback. TestDesign proceeds next.

Apply the additive CLI-generated migration before running the feature. Publish
the service and installed helper together, then the generated contract/bundles.
Verify a freshly composed real dispatch carries the policy and accessible helper;
a healthy server alone is insufficient evidence. Do not amend running tasks or
repair CARD-0294's missing S3 as incidental work. For rollback, stop granting new
policies and restore the prior instruction composition; preserve the nullable
policy and question history until no active task depends on the new endpoint.

Limits to report: an honest but wrong semantic classification is still possible;
the category/path/impact contract narrows that risk but does not prove program
equivalence. A policy omission or an unusable helper can still require a human
fallback. V1 intentionally has no automatic rescue of a free-form blocked report
or already-open popup: those lack the structured, preauthorized question this
mechanism requires. The actual dispatch and report-contract changes are therefore
part of acceptance, not optional documentation cleanup.
