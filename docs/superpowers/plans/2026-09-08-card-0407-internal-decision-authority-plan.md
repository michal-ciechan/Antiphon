# CARD-0407: resolve scoped internal questions without a human round trip

Date: 2026-09-08. Plan task: `31740fa8`. Code baseline: `4db2076b`.
Amendment task: `ccc876ce`, following the full `cff67ab5` design review.
Amendment baseline: `33d906a7`, including TestDesign commit `ccb48b2c`.
Status: Plan and TestDesign amended for D1-D5; ready for sequential Code slices.
Implementation and acceptance have not run.

## Outcome

Add caller-declared internal decision grants to an AgentTask's dispatch and a
synchronous structured question check that consumes those grants. A matching
internal question receives a concrete continue answer in the worker's current
turn. It neither opens a human popup nor settles the task Blocked. A question
outside that grant uses Grok's server-answerable `ask_user_question`, or the
existing structured Blocked report for every other provider, including ClaudeCode
and Codex. An absent policy preserves today's permission-question behavior.

Change the policy-bearing generated brief and orchestrator guidance. Keep the
static delegate bundle and unconditional reporting contract unchanged. This is a
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
| The report instruction is too broad | `server/Application/Services/DelegationReportFormatter.cs`, `ReportingContract`, says blocked "if you need a decision or an answer to continue"; `server/Bundles/delegate-basics.md` repeats it | Retain both unconditional sentences. Only the validated-policy block adds an exception for a question actually resolved by Continue |
| Existing authority might already solve this | `BlockedNote.StandingAuthorityBlock` embeds free text; `ContinueWithAuthorityAsync` replays it only on an already Blocked question | Useful compatibility path, but no bounded internal scope or synchronous decision check |
| CARD-0294's explicit once-only auto-continue exists | At this baseline, API DTO/entity accept/store `AutoContinue`/`AutoContinueOnWait` and expose `AutoContinuedAt`. Entity/DTO comments explicitly call firing a follow-on. There is no runtime consumer/writer of the once-only flag/stamp, and `scripts/delegate.ps1` has Authority/Continue but no AutoContinue parameter | The card's shipped claim exceeds this checkout. Preserve the specified once-only contract; do not make CARD-0407 depend on CARD-0294 S3 or silently implement it here |
| Grok supports an in-turn question | `GrokQuestionTool` and `AgentTaskReplyService.HasOpenQuestionToolAsync` identify an unclosed `ask_user_question`; `AnswerAsync` can answer a Working/Dispatched task without changing its status | Retain this human-question path; the new automatic path answers synchronously before opening a popup |
| Any native question tool can use that answer path | `AnswerAsync` refuses Working/Dispatched replies without an open Grok-named question. Claude's `AskUserQuestion` passes through verbatim, with no matching server answer route | Only Grok receives the native-wait instruction; ClaudeCode/Codex report the question and settle Blocked |
| `ReportingContract` already takes provider kind | Its current `kind` parameter is `AgentTaskKind` (Worker/Orchestrator), not `AgentKind`. `FitBriefForTyping` has the actual session's `AgentKind`, but does not pass it to `BuildBrief` | Thread resolved provider kind explicitly into conditional contract composition; do not branch on the existing task-kind parameter |
| A generic typed "continue" is safe | `AgentTaskReplyOverlayTests` and CARD-0159/0241 distinguish a popup ToolResult from a new UserPrompt. `GrokQuestionPopup` still has empty measured chrome literals | Do not build a new popup selector, blind Now send, Esc action, or screen heuristic |
| CARD-0033 avoids the wait | `BlockedContextBuilder` projects only Blocked tasks; `BlockedQuestionCard.tsx` provides question, context and reply/continue actions | It reduces the cost of answering but still needs a human. Reuse it for real blockers |
| NeedsDecision could carry this | `docs/agent-card-lifecycle.md` separates card state from AgentTask state; CARD-0122/0123 concern card decisions | No card status, board column, or tracker transition is needed |
| Scope is an authorization boundary | Existing `AgentTask.Scope`/area leases warn about collisions and record drift; AGENTS explicitly says writes are not enforced | Add a separate grant field. Never reinterpret `-Scope`, unknown area names, or scope drift as permission |
| Scope observation already audits grants against all actual edits | `RecordScopeDriftAsync` uses `AgentFilesService.GetFilesAsync(..., since: null)`, a HEAD/worktree plus transcript view. It neither compares grants nor reliably captures shell-made, already-committed changes | Add a dispatch-baselined Git observation and separate grant-path Warning at settlement; reuse the warning surface, not the advisory matcher or HEAD-only evidence |
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

Role eligibility is explicit: Code, Debug, Custom, Deploy, Plan, TestDesign,
**Coverage**, Docs, Commit and Merge may carry grants in Shared or Worktree.
Coverage writes tests and supporting harness code, so it has the same bounded
mechanical eligibility as Code; it gains no right to weaken verification.
Review, Investigate, Test, Check, Distill and Diagnose reject nonempty grants;
ReadOnly rejects them for every role. Unknown/future roles default to rejection
until deliberately classified. This accounts for every current AgentTaskRole;
role eligibility never supplies a grant by itself.

**D-3 — A structured question answered before a human tool opens.** Introduce
`scripts/task-question.ps1 -Task <id> -QuestionFile <path>`, backed by
`POST /api/agent-tasks/{id}/decision-questions`. It returns JSON immediately:
`Continue` or `NeedsHuman`, with stable reason, question id and applicable grant
id. It is the provider-neutral equivalent of a structured go-ahead question.
The delegate calls it only for an actual uncertainty/go-ahead, not for every
edit. Routine already-authorized work continues as usual.

For `Continue`, the delegate performs the named local repair and verification in
the same turn. For `NeedsHuman`, branch on the resolved **AgentKind**: Grok uses
`ask_user_question` and waits for an explicit answer through the existing server
reply path before dependent work. Every other kind, explicitly ClaudeCode and
Codex, gives the question, proposed action, impact and missing authority in its
final report and ends with the blocked token. Having a native tool named
`AskUserQuestion` or another structured-question tool is insufficient: Antiphon
must be able to detect and answer it. Unknown kinds use Blocked too. If Grok's
documented tool is unavailable, it also uses this Blocked fallback. Independent
work can continue while a supported asynchronous Grok question is pending.

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
Both grant author and worker can be LLMs; the caller's preserve text and the
worker's preservationEvidence are claims, not human approval or semantic proof.
D-8 adds detection of actual changed paths outside the grants used for Continue.

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

**D-8 — Audit actual changed paths at settlement (review D4).** Add detection;
do not defer it. For an attempt with one or more persisted Continue decisions,
compare the actual changed-file set with the union of `paths` from the immutable
grants referenced by **those Continue rows**. Do not include unused grants,
NeedsHuman rows, previous attempts, request prose or advisory `Scope`. Evaluate
each decision against one grant at admission as before; this later union is
observation, never a way to authorize a mixed question. Use the same exact path
normalization/comparison rules as D-2.

Capture a server-owned `InternalDecisionAuditBaselineJson` on grant-bearing
dispatch before the brief can reach the worker: attempt/session, resolved Git
root, HEAD SHA, capture time, dirty-at-start flag and explicit unavailable reason.
It is nullable for old/no-policy tasks, immutable within the attempt and replaced
only for a new dispatch attempt; retained warning details preserve their baseline.
No worker field or policy-file edit can supply or refresh it. Collect committed
changes since that SHA plus index/worktree changes and untracked nonignored files;
include both old and new rename paths and deleted paths. Capture observation
**before MergeBackAsync or any worktree cleanup/release** can remove the evidence.
Repeat it on subsequent report settlements after Blocked replies; never use a
decision response itself as a settlement trigger.
S2b must cover normal, deferred/unmarked and recovery settlement writers that
can encounter Continue rows, and the explicit Cancel path before it disposes of
workspace evidence. Reuse one audit service rather than inventing another state
transition. If cancellation or a missing workspace prevents observation, retain
an unavailable warning; cancellation/release still follows its existing contract.

Use a typed success/unavailable Git observation seam, with infrastructure-owned
process I/O. The current best-effort `GetChangesSinceAsync` returns an empty list
on Git failure and cannot prove a clean audit. A missing baseline/repository,
unreadable diff, truncated observation or changed repository identity must yield
`Warning` detail code `internal_decision_audit_unavailable`, never a clean result.
For mismatches emit the existing `AgentTaskEventType.Warning` with detail code
`internal_decision_path_drift`, affected paths, used grant/question IDs,
attempt/session and baseline/observed HEAD. Carry its compact warning through
the existing caller-warning note and task event display; leave `ObservedScope`
and the existing `ScopeDrift` event/header meanings untouched. Deduplicate the
same attempt, report boundary and observation across settlement retries using
the existing serialized settlement/persistence boundary; a later changed
observation after an answered blocker gets a new audit.

This warning says files fell outside **internal-decision grants**, not that the
whole assignment was unauthorized. A task can also contain separately assigned
product work. Shared workspaces and a dirty starting tree may include other work:
report that attribution limit and conservatively include the observed paths
rather than silently subtracting a dirty path the worker might also change.
An explicit empty diff is clean only when observation succeeded; grants absent
or no Continue rows mean not applicable, with no new observation/warning.

Warnings do not block, kill, roll back, auto-reply, change the reported verdict,
consume legacy continuation, or authorize a land. Observation failure cannot
prevent normal settlement. This detects visible path drift, including a
shell-made committed edit, but cannot establish semantic equivalence, detect
every reverted/ignored temporary write, attribute every shared-tree edit, or
validate `.gitattributes` contents and preservationEvidence. Those limits remain
explicit; V-19/PC-35 pin the detection that v1 does promise.

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
grants on ReadOnly workspaces and the explicitly ineligible roles in D-2.
Code/Debug/Custom/Deploy/Plan/TestDesign/Coverage/Docs/Commit/Merge may carry grants
within their own brief; role eligibility never gives permission by itself.

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

`BuildBrief` emits the entire internal-decision instruction block **only when
that dispatch has a validated, nonempty stored policy**. Omitted, null and
empty-to-absent policies emit none of the helper instructions, grant exception
or discouragement text. Neither StandingAuthority nor role eligibility activates
this block. It places the validated grant and helper's absolute path/usage in
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

Leave `server/Bundles/delegate-basics.md` unchanged. Leave the unconditional
generic `ReportingContract` verdict text unchanged, including the exact meaning
and sentence "blocked ... if you need a decision or an answer to continue".
Thus a grant-free worker with a pending permission question still reports it and
ends Blocked. Do not replace this with "must be handed back", tell that worker
not to block for implementation details, or send it to an absent helper.

Proposed question instruction **inside the conditional policy block only**:

> Do not end the task merely to ask permission for an implementation detail.
> This dispatch has the validated grants listed here. For a pending internal
> go-ahead, use the supplied structured task-question
> helper with the dispatch grant, affected paths, proposed action and impact.
> If it returns Continue, make the scoped repair and keep working in this turn;
> record the decision and verification in your final report. Do not open a human
> approval popup for that resolved choice. Only a recorded Continue resolves
> that permission question; an error, NeedsHuman or unknown coverage/impact does
> not. Use the human-question route below for an unresolved choice. Never turn
> a product, data, UX or public-contract choice into an internal repair to continue.

Append exactly one human-question branch to that policy block:

| Resolved provider kind | Instruction |
|---|---|
| Grok | Ask using `ask_user_question` and wait for an explicit answer before dependent work. Antiphon can detect and answer this tool. If it is unavailable, report the precise question, proposed action, impact and missing authority and end with the blocked token. |
| ClaudeCode, Codex and every other/unknown kind | Report the precise question, proposed action, impact and missing authority in your final report and end with the blocked token. This is Antiphon's supported answer route for this kind; do not open a native question tool and wait on it. |

Thread the resolved session `AgentKind` from `FitBriefForTyping` into `BuildBrief`
and its conditional-block renderer. Keep `AgentTaskKind`'s Worker/Orchestrator
rollup separate. If conditional text is factored through `ReportingContract`,
add an explicitly named provider argument; its existing `kind` is not that value.
An unknown/unresolved provider never selects Grok. Cover cold, warm, standing and
direct/spill composition with the actual resolved kind, not a default constant.

The conditional block also says: a grant-resolved question is no longer missing
input under the ordinary verdict rule; an unanswered human question is not
completion. These qualifications must not leak into the unconditional text.

Keep `BlockedNote.StandingAuthorityBlock` and `ContinueMessage` unchanged,
including their existing "not covered => blocked" fallback. The new dispatch
block supplies the narrow grant check without rewriting legacy explicit authority
replay; an explicit `-Continue` remains a caller action. Keep specialist report
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
| S1: immutable grant contract | `server/Domain/Entities/AgentTask.cs` policy and server-only audit-baseline fields; new domain policy/category types; `server/Application/Dtos/AgentTaskDtos.cs`; `AgentTaskService.cs` create/retry/escalation/follow-up/merge paths; new pure `InternalDecisionPolicy.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated migration and snapshot | `InternalDecisionPolicyTests` (new), `AgentTaskServiceIntegrationTests`, `AgentTaskCallerResolutionTests`, `AgentTaskInternalDecisionLifecycleTests` (new) |
| S2a: structured check | New `AgentTaskDecisionQuestion` entity, DTOs and `AgentTaskDecisionQuestionService`; self-only route in `AgentTaskEndpoints.cs`; DI in `server/Program.cs`; append event enum; own CLI-generated additive migration after S1 | `AgentTaskDecisionQuestionTests`, `AgentTaskDecisionQuestionIntegrationTests`, `AgentTaskDecisionQuestionApiTests` (new), refusing-runner API authentication fixtures |
| S2b: settlement path audit | New pure grant-path audit policy/service; `IInternalDecisionWorkspaceObserver` in Application and Git implementation in Infrastructure; `AgentTaskDispatcher.cs` baseline capture; `AgentTaskReplyService.cs` normal/deferred/recovery observation before MergeBackAsync and warning persistence/delivery; `AgentTaskService.cs` Cancel audit | `InternalDecisionSettlementAuditTests` (new), `AgentTaskSettlementRaceTests`, `AgentTaskReplyIntegrationTests`, `AgentTaskInternalDecisionLifecycleTests`; real temporary Git fixtures plus refusing I/O spies |
| S3: delivery and operator front door | `scripts/delegate.ps1` policy flag; new ASCII-only `scripts/task-question.ps1`; dispatcher identity preflight/helper location/resolved provider plumbing; `DelegationReportFormatter.cs` conditional block; `orchestrator.md` and CLI reference. `delegate-basics.md` and `BlockedNote.cs` receive compatibility tests, no text changes | `DelegateScriptInternalDecisionTests`, `TaskQuestionScriptTests`, `InternalDecisionInstructionContractTests` (new), `DelegationUnitTests`, `DelegateBundleLaunchTests`, `AgentTaskStandingAgentDispatchTests`, `AgentTaskPoolTests`, `CodexDelegateDispatchTests`, `GrokDelegateDispatchTests`, `DelegationBriefCeilingPtyTests` |
| S4a: visible evidence and deterministic acceptance | `client/src/api/agentTasks.ts`, task drawer/event display and visual mappings for grant/history/audit warning; deterministic worker fixture; `docs/orchestration-loop.md`, `docs/ops-http.md`, `docs/antiphon-api.md`, `.claude/skills/antiphon-delegate/SKILL.md` | New client policy/history tests, existing task drawer tests; `InternalDecisionWorkerFlowTests` (new); reply/overlay/settlement/watchdog regressions and FakeGrok contract if changed |
| S4b: real-provider acceptance and final evidence | E2E `InternalDecisionGrokCanaryTests`, pure `InternalDecisionGrokCanaryGuardTests`, owned fixture resources and sanitized acceptance manifest; consolidate the per-slice verification ledger | Explicit V-17 two-task canary, PC-31 pure guard mutants, final R-5 rebase inspection; targeted reruns only for integration changes |

Dispatch **six separate sequential Code tasks**: S1 -> S2a -> S2b -> S3 -> S4a
-> S4b. The caller lands each completed slice via `-Land` before creating the
next worktree, which otherwise branches from master rather than its sibling.
Each slice commits/pushes implementation and its own verification evidence; it
settles `next: code` naming the next slice. S4b settles `next: review` with the
consolidated evidence (or reports the exact acceptance block). Do not give one
Code dispatch the entire card or imply that all PCs fit a single short session.

S1/S2a/S2b remain inert for existing dispatches: no unconditional instruction
changes, no caller guidance to use the feature and no helper advertised before
S3. S3 can enable explicit opt-in only after the endpoint and audit exist and its
contract tests pass; general rollout/card closure awaits S4a/S4b and Review.
Plan/TestDesign artifacts must land before S1. If a slice exceeds its practical
session budget, commit the coherent work and hand off its exact remaining V/R/PC
variants in another `next: code` report; never drop variants to meet an estimate.

The execution ownership and estimates below are part of each next brief. A V/R
row spanning slices has one evidence chain with labeled subcases, not permission
for either slice to claim the other's unfinished cases.

| Code dispatch | Verification owned (all variants unless partitioned) | Next handoff |
|---|---|---|
| S1 | V-1..5 create/pure-policy/lifecycle subcases (question-time denials belong to S2a); PC-2..5, PC-17; legacy create/inheritance part of R-4 | S2a: implement the mapped decision endpoint on landed S1 and finish its admission/durability checks |
| S2a | V-2/V-3/V-5 question-time subcases, V-6..12; R-1, R-2 decision-service side, R-4, current R-5 inspection; PC-1, PC-6..16, PC-18..24 (PC-24 service-side witness) | S2b: add dispatch-baselined settlement path warnings before publishing the helper |
| S2b | V-19 including baseline lifecycle; R-7 audit/settlement side; PC-35 | S3: deliver the helper and kind-specific policy block, preserving every no-policy contract |
| S3 | V-13..14, V-18; R-3 script/contract side, R-8; PC-25..27, PC-33..34 contract witnesses | S4a: implement deterministic worker flows and visible grant/history/audit evidence |
| S4a | V-15..16; R-2/R-3 worker/reply side, R-6, remaining R-7/R-8; PC-24/PC-26/PC-33/PC-34 worker witnesses, PC-28..30, PC-32 | S4b: run isolated real Grok acceptance and consolidate the complete evidence ledger |
| S4b | V-17, PC-31, R-5 pre-land inspection/conditional integration, unresolved cross-slice regressions only | Review: judge the integrated behavior, per-variant PC evidence and real-provider acceptance |

Read the area owners before implementation: project-context, orchestration-loop,
agent-card-lifecycle, agent-credentials, testing-and-build and, if touching question delivery,
session-runtime-invariants plus the Pty owner. Do not change the delivery layer
merely to implement this plan.

## Acceptance requirements for TestDesign

TestDesign ran as a separate stage and the amended verification design below
converts these requirements into `V-n`/`R-n`/`PC-n` rows with exact filters and
positive controls. These are coverage requirements, not executed-test claims.

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
   report. Only Grok's NeedsHuman branch opens `ask_user_question` and waits;
   a matching ToolResult confirms an explicit human reply, an unrelated result
   does not. ClaudeCode and Codex always receive the explicit structured Blocked
   fallback for unresolved policy checks, even when their own native question
   tool exists. Unknown kinds and Grok without its supported tool also Block.
5. **Authorization and lifecycle:** own worker token and exact current worker
   session token accepted, including warm hash transfer; missing, sibling,
   parent, stale-session/attempt and capability impersonation denied. A standing
   seat without usable identity refuses grant-bearing dispatch before delivery. Test
   cancel/settle racing with question admission and duplicate posts across
   independent service scopes/restart. No orphan session, card transition,
   replayed approval or second event from retries. ReadOnly/Test/Review and
   specialists cannot receive edit grants. Coverage is explicitly eligible in
   Shared/Worktree, with exactly the same grant and verification limits as Code.
6. **Compatibility:** absent-policy tasks keep existing behavior; legacy fields
   and once-only semantics are unchanged; new answers never consume/reset that
   counter. Same-task retries retain grants but not old-session answers; new
   follow-ups/merge children never inherit them. Grant files edited after create
   cannot alter the stored snapshot. Preserve settlement positive-evidence,
   report nudges, stage tokens, scope-warning and CARD-0033 reply-round tests.
7. **Instruction delivery:** preserve the old unconditional permission-question
   verdict in the generic contract and delegate-basics.md. No-policy briefs
   still direct Blocked for missing permission and contain no new helper or
   discouragement instructions, including a standing-authority-only dispatch.
   Only validated-policy direct/spill briefs include the effective grant,
   conditional exception, provider-specific fallback and real helper path,
   including a target project without Antiphon scripts. Warm reuse cannot depend
   on the old process's bundle. Specialist vocabularies stay unchanged. A fixture
   agent must consume the contract/helper response, not merely assert keywords.
8. **Settlement observation:** for every attempt that used Continue, audit
   actual committed/uncommitted/rename/delete/untracked paths against the union
   of grants used by its Continue rows before cleanup. Mismatches and unavailable
   evidence produce distinct existing Warning-shaped records and a caller
   caveat. Unused/denied/old-attempt grants cannot conceal drift. No-policy and
   no-Continue tasks retain existing settlement; a failed observation never
   masquerades as a clean audit or prevents settlement.

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
paths, synchronous answer, kind-specific existing human fallback and advisory
settlement detection. Verification is amended below; S1 Code proceeds next.

Apply the additive CLI-generated migrations before running the feature. Publish
the service and installed helper together, then the generated contract/bundles.
Verify a freshly composed real dispatch carries the policy and accessible helper;
a healthy server alone is insufficient evidence. Do not amend running tasks or
repair CARD-0294's missing S3 as incidental work. For rollback, stop granting new
policies and restore the prior instruction composition; preserve the nullable
policy and question history until no active task depends on the new endpoint.

Limits to report: an honest but wrong semantic classification is still possible;
the category/path/impact contract narrows that risk but does not prove program
equivalence. Settlement warnings detect visible path mismatch and disclose
unavailable/ambiguous attribution; they are not enforcement or semantic proof.
A policy omission or an unusable helper can still require a human
fallback. V1 intentionally has no automatic rescue of a free-form blocked report
or already-open popup: those lack the structured, preauthorized question this
mechanism requires. The actual dispatch and report-contract changes are therefore
part of acceptance, not optional documentation cleanup.

## Verification design

TestDesign: `446f9666`, 2026-09-08, against landed plan/baseline `1730bc56`.
This section specifies tests to implement and run in Code; no runtime tests or
real-provider acceptance were run in TestDesign. Amendment `ccc876ce` updates
both design and verification for review D1-D5: V-18/PC-33..34 pin provider and
no-policy contracts; V-4/PC-4 name Coverage; V-19/PC-35 pin settlement detection;
the slice ownership table and Cost replace the single-dispatch allowance.
No human decision is needed to implement this verification design.

### Proves it works now

#### Harness and evidence contract

New test class and method names below are implementation targets, not claims that
they already exist. Put new server tests under `tests/Antiphon.Tests/Application/`
unless a different project is named. Tag each class Unit or Integration. Use
`DelegationTestServices.AddDelegationWorktreeGraph` / `AddGitWorkspaceService` for
hand-built dispatcher/reply graphs. Use the existing `TestDbFixture` for service
tests and `AntiphonWebAppFactory` (its own cloned database and refusing runner)
for the actual mapped HTTP route. Seed all observations under the fixture's task,
session and card IDs. Never count the entire shared database. Global dispatcher
or reconciliation sweeps require unkeyed `[NotInParallel]`.

Process fixtures, including PowerShell helpers, FakeGrok and the real canary,
take their assembly-local `[ParallelLimiter<ProcessSpawnLimit>]`. Real ConPTY
fixtures also take `[NotInParallel("Headed")]`; execute test projects sequentially.
Use barriers to order races, not sleeps. A clock used by message delivery must
advance with real time (or an auto-advancing fake), never a frozen `UtcNow` with
real timers. Preserve real final-message identity and closing tokens in the new
fake-worker transcript; do not copy the legacy FakeGrok test's grace-period
override as a way to obtain successful settlement.

For each endpoint test, assert HTTP code and structured disposition/reason, then
read fresh DB state in another scope. An error body containing the word Continue
is not approval. Capture the pre-call task state and the post-call state, including
status, CompletedAt, Result/report evidence, stage handoff, reply watermark,
session binding, attempt, legacy continuation fields, card revisions and queued
messages. Ignore only changes explicitly required by D-6: decision history,
timeline event, normal update publication and associated concurrency bookkeeping.
`NeedsHuman` does **not** itself set Blocked: V-15 verifies the subsequent worker
behavior separately. Authentication/schema/state failures persist no accepted
decision; well-formed authorized checks, including denials, persist their result.
HTTP fixtures use the existing `X-Antiphon-Task-Token` header; do not accidentally
test an Authorization-header path that this endpoint family does not consume.
For every stored check verify task/attempt/session, canonical payload/hash,
immutable policy version/hash, grant ID, disposition/reason and creation time;
the event's summary must agree with its authoritative typed row.

Use the canonical sample policy/question above for the main positive control.
Seed a temporary Git repository with no Antiphon installation, an unrelated
sentinel file, a CRLF script and an existing `.gitattributes` entry for that
sentinel. All fixture script bodies call an absolute argument-capture stub, never
resolve docker/pg_dump from PATH. Capture arguments as a JSON array so whitespace,
quotes, empty arguments, metacharacters and argument boundaries are observable.
Expected argv is a fixture-authored constant, not calculated by the repair code.
Keep encoding/BOM and all unrelated bytes in before/after hash evidence.

| ID | Behavior and layer | Executable owner / expected result |
|---|---|---|
| V-1 | Policy schema and limits; unit + mapped HTTP | New `InternalDecisionPolicyTests.Policy_schema_boundaries` and `AgentTaskDecisionQuestionApiTests.Create_rejects_invalid_policy_before_task_creation`. Valid v1 and empty-to-absent normalize deterministically. Test 16/17 grants, 64/65 distinct paths, 1000/1001 preserve characters and 20000/20001 canonical JSON characters; construct valid boundary specimens for each independent limit. Missing/blank categories, paths or preserve, duplicate IDs, unsupported categories/version and unknown fields at policy or grant level are 422 with no task/session/worktree/queue side effects. A server-generated provenance field supplied by the caller is rejected, not trusted. |
| V-2 | Exact repository boundary; unit + filesystem integration | New `InternalDecisionPolicyTests.Exact_paths_only` and `AgentTaskDecisionQuestionIntegrationTests.Repository_path_boundary`. Accept a named existing file and a named proposed new helper; normalize slash/backslash and `./`. Reject drive-absolute, drive-relative, rooted, UNC, traversal (including internal `..`), wildcard and directory grants. Deny a sibling prefix (`scripts/deploy.ps1.bak`), another repository and a mixed in/out-of-grant request. Compare case using the repository filesystem's rules, with unit vectors for case-sensitive and insensitive comparers and native filesystem coverage on the executing platform. A file link, directory junction and a missing file below an escaping linked ancestor must not authorize an outside target; re-point a link after dispatch to pin question-time resolution. Do not accept a worker-supplied repository root. Invalid policy syntax is 422; a valid request whose resolved target escapes is NeedsHuman/path_not_granted. |
| V-3 | Attribute scope is narrower than the containing file; unit + integration | New `InternalDecisionPolicyTests.Attribute_grant_boundaries` and `AgentTaskDecisionQuestionTests.Attribute_requests_are_exact`. Require nonempty `attributeTargets` within grant paths when `.gitattributes` is granted. An exact target with only text/eol passes LineEndings. Unlisted target, broad `*.ps1`/directory pattern, filter/diff/merge/working-tree-encoding attribute, omitted targets, ShellTransport or BuildTestHarness touching `.gitattributes` cannot Continue (422 for malformed fields; NeedsHuman/attribute_not_granted or category_not_granted for a well-formed mismatch). Include two explicit targets to catch first-target-only checks. |
| V-4 | Dispatch provenance and role eligibility; integration | Extend `AgentTaskServiceIntegrationTests` with `Internal_policy_create_role_matrix`. Enumerate every current AgentTaskRole and assert the case list is exhaustive: Code/Debug/Custom/Deploy/Plan/TestDesign/**Coverage**/Docs/Commit/Merge with Shared and Worktree accept a valid grant; Review/Investigate/Test/Check/Distill/Diagnose reject it; ReadOnly rejects every role. Include explicit Coverage positives for both writable workspaces and its ReadOnly negative. Unknown future enum values reject until classified; they cannot silently enlarge eligibility. Existing grant-free requests stay valid. Store caller identity/time from resolved dispatch caller, including an authorized capability-created task; worker questions remain self-only. Scope/unknown area warnings do not create grants. |
| V-5 | Immutable snapshot and lifecycle; integration | New `AgentTaskInternalDecisionLifecycleTests.Policy_snapshot_survives_only_same_task_requeue`. Edit/delete the source policy file after create, Refine the task, submit a report containing a forged grant and write a policy file in its target repository: the persisted canonical policy/hash and granting identity stay unchanged. Retry and escalation retain that snapshot but advance binding/attempt; old tokens and old-session answers do not become new permission. New tasks, `-OnAgent` follow-ups, new stage-role tasks, specialist children and automatic Merge children have null policy unless their own eligible dispatch explicitly supplies one. Preserve CARD-0294's separate StandingAuthority inheritance. Include a migration upgrade of a pre-feature task: null policy, no history, unchanged legacy fields. |
| V-6 | Worker identity, not caller delegation permission; mapped HTTP | New `AgentTaskDecisionQuestionApiTests.Self_identity_matrix`. POST the real `/api/agent-tasks/{id}/decision-questions` route. Own task token succeeds despite `MayDelegate == false`; exact session-scoped token succeeds. Absent/invalid, sibling task, parent task/session, capability and unrelated session tokens receive 403 and no row/event. Capability root permission and a token-less polling GET are not alternatives. A still-valid session principal referring to a retired/rebound session is rejected with 409 as stale; a revoked/unknown token remains 403. Include a same-session principal attached to another task to distinguish live binding from mere parent lineage. |
| V-7 | Attempt, session and state admission; mapped HTTP + integration | New `AgentTaskDecisionQuestionApiTests.Current_binding_required` covers Dispatched and Working positives; Queued/Blocked/Succeeded/Failed/Canceled negatives, wrong/missing attempt, no session, ended/detached session, previous session and two active tasks bound to the same session. Valid but stale/ambiguous binding is 409; malformed attempt is 422. Repeat an already recorded request after cancel/retry: admission still runs before duplicate lookup, so an old Continue cannot be replayed to an inactive or different attempt. |
| V-8 | Strict question schema; mapped HTTP | New `AgentTaskDecisionQuestionApiTests.Question_schema_boundaries`. Require requestId, attempt, grantId, one category, nonempty paths, impact, question/action/evidence. Test invalid GUID, enum strings/numbers/flags, absent/null impact (never default None), blank evidence, unknown fields at every object level, and 500/501, 1000/1001, 2000/2001 text boundaries and 10000/10001 whole-request characters. For the request-size boundary, use otherwise-valid serialized JSON padding so a text-field cap is not the failing guard. Assert 422, no decision/event, no helper approval. Do not add a command-execution field. |
| V-9 | All declared effects fit one grant; unit + integration | New `AgentTaskDecisionQuestionTests.Granted_category_returns_continue` has three independent positive rows: LineEndings, ShellTransport and BuildTestHarness. `Human_impact_never_continues` enumerates ProductBehavior, Data, Ux, PublicContract, Security, OperationalPolicy, ExternalActionOrSpend, Mixed and Unknown: each is NeedsHuman/human_impact even with correct paths/category and persuasive prose. `One_named_grant_must_cover_all_effects` tests absent policy/no_grant, nonexistent grant ID/no_grant, wrong category/category_not_granted, one-of-two paths missing/path_not_granted, two partial grants whose union would cover the request, and a denied request retried under a new requestId. No Continue in any negative row. Use the same deploy-script path for a retention/database/backup-argument change, with its honest Data/OperationalPolicy/Mixed impact. |
| V-10 | Durability and idempotency; Postgres integration | New `AgentTaskDecisionQuestionIntegrationTests.Idempotency_survives_independent_scopes_and_restart`. Exact duplicates and canonical-equivalent property-order/normalized-path requests return the same questionId/result and leave one row plus one DecisionQuestion event. Same `(task, attempt, requestId)` with changed canonical content returns 409/decision_question_changed, for both a stored Continue and a stored NeedsHuman. Concurrent duplicate posts through two service scopes produce one durable result; destroy/recreate the service provider against the same test database and retry. Same requestId on a different task or a later valid attempt gets a distinct record and a fresh evaluation, never an old answer. Verify the migration's unique composite constraint with direct duplicate insertion as well as application-level concurrency. |
| V-11 | Transaction and state races; Postgres integration | New `AgentTaskDecisionQuestionIntegrationTests.State_transition_races_are_serialized` uses barriers for cancel, settlement and retry between preliminary admission and locked recheck. If the state change commits first, receive 409/no decision; if the decision commits first, one check is valid before the later transition and cannot resurrect the task. Pin unique session binding when a second binding appears during admission. `Decision_and_event_commit_together` injects a save failure between typed row and event: neither survives, no publication/Continue escapes, and retry after recovery works once. A recording event bus reads from a separate DB connection to prove publication occurs only after both are committed. |
| V-12 | In-turn check has no settlement/delivery effects; integration | New `AgentTaskDecisionQuestionIntegrationTests.Decision_is_not_a_reply_or_report` snapshots the fields named in the harness contract for both Continue and NeedsHuman. Assert unchanged status/CompletedAt/report/result/handoff/watermark/card revisions, no Blocked/Completed/Replied event, no AnswerAsync/ContinueWithAuthorityAsync/terminal send/release/merge call, no new user prompt or approval message. Multiple distinct permitted questions each add one audit record but do not use another model turn or legacy continuation allowance. A DB write error and auth/schema error also have zero such effects. |
| V-13 | Real operator/helper scripts; subprocess + mapped HTTP | New `DelegateScriptInternalDecisionTests.Policy_file_is_posted_verbatim_as_structured_json` executes `delegate.ps1 -InternalDecisionPolicyFile` with UTF-8 multiline/Unicode content and a path containing spaces via `ProcessStartInfo.ArgumentList` (reuse `DelegateScriptRunner`). Inspect the posted object and stored snapshot; absent file/invalid JSON/oversize policy fail without a create POST. New `TaskQuestionScriptTests.Helper_is_fail_closed` executes the actual installed helper from another repository; success returns a complete parseable JSON decision. Timeout, connection failure, 403/409/422/500, empty/truncated/non-JSON response, unknown disposition and missing required response fields are unresolved/nonzero and must never output a success-shaped Continue. A valid NeedsHuman stays distinguishable from transport failure. Keep script ASCII and parse under pwsh and Windows PowerShell 5.1. Verify injected synthetic tokens never appear in argv, stdout/stderr, question files, brief or audit detail. |
| V-14 | Dispatch reaches the actual worker; integration + ConPTY | Extend `DelegationUnitTests`, `DelegateBundleLaunchTests`, `CodexDelegateDispatchTests`, `GrokDelegateDispatchTests`, `AgentTaskPoolTests`, `AgentTaskStandingAgentDispatchTests` and `DelegationBriefCeilingPtyTests` with policy-bearing direct/spill cases. Assert effective policy, version, current attempt, resolved AgentKind and existing absolute Antiphon helper path; target repository has no Antiphon scripts. Warm hash transfer authenticates the new task and denies the old; a deliberately stale warm bundle means only the fresh brief can supply the grant. Authenticated standing session accepts the check; credential-less standing dispatch refuses with internal_decision_identity_unavailable before delivery and neither reroutes nor changes the standing agent. Grant-free standing behavior stays unchanged. Assert no token embedding and a server-owned attempt baseline before policy-bearing delivery. V-18 pins unchanged unconditional formatter/bundle/legacy authority text and the gated provider branch in the actual direct/spill brief. Specialist verdict vocabularies stay unchanged. |
| V-15 | Actual local repairs and human fallback; deterministic worker integration | New `InternalDecisionWorkerFlowTests` implements the four fixture flows below using the real helper, dispatch contract, decision service and reply observer. Policy-bearing flows read the spill file, parse its actual policy/helper path and branch on the returned disposition and provider instruction. No-policy flows follow the actual Blocked contract without inventing a helper call. Stub only worker choice/tool behavior, not the endpoint or result. A fake worker that just prints done or receives its grant through a second test-only channel fails the evidence oracle. |
| V-16 | Visible history and enum compatibility; integration + client | New `AgentTaskDecisionQuestionIntegrationTests.Detail_history_is_separate_from_report` plus new `client/src/api/agentTasks.test.ts` and `client/src/features/delegations/TaskDetailBody.test.tsx`. Detail exposes immutable grants/provenance and recorded checks, including a NeedsHuman still shown as historical after a later human answer. Empty old-task policy/history renders cleanly; bounded history ordering/page behavior loses or duplicates no entries across page boundaries if paging is implemented. Existing Result, blocked context and reply rounds retain their meanings. Extend `taskVisuals.test.ts` to require a DecisionQuestion entry; assert existing enum numeric values are unchanged and the addition uses the current next value. No new modal/column/decision state. |
| V-17 | Real Grok uses the new mechanism; isolated real-provider acceptance | New explicit `tests/Antiphon.E2E/InternalDecisionGrokCanaryTests.cs`, method `Real_grok_repairs_in_turn_and_waits_on_product_impact`. Execute the procedure below after the deterministic gate passes. Both the mechanical positive and product-impact negative are required. FakeGrok, a real CLI backed by scripted model responses, a composed-bundle stamp or a healthy service cannot replace this evidence. |
| V-18 | Provider answerability and opt-in instruction boundary; unit + composed-brief integration | New `InternalDecisionInstructionContractTests.Needs_human_route_uses_resolved_provider_kind` and `No_policy_permission_question_still_directs_blocked`. Cross ClaudeCode/Codex/Grok with absent/null/empty/validated policy and direct/spill/cold/warm/standing composition. For policy-bearing ClaudeCode and Codex assert an affirmative report-question-and-blocked directive and absence of native-wait permission, even with a fixture native tool present; Grok alone names ask_user_question and explicit waiting, with Blocked on tool unavailability. Unknown kind also gets Blocked. Exercise a resolved-kind value different from a stale/default formatter value. For every no-policy variant assert the original blocked-if-you-need-a-decision-or-answer instruction remains effective, with no new discouragement/helper/grant exception; StandingAuthority alone cannot activate it. Pin unchanged delegate-basics.md, StandingAuthorityBlock and ContinueMessage as supplementary source assertions. V-15 consumes these branches and proves Blocked is surfaced and answerable; a check only that old wording disappeared is insufficient. |
| V-19 | Actual changed paths versus used grants; Git + settlement integration | New `InternalDecisionSettlementAuditTests.Continue_paths_are_audited_before_cleanup`, `Audit_baseline_is_attempt_scoped`, `Audit_failure_is_visible_without_changing_settlement` and `Repeated_settlement_deduplicates_the_same_audit`. A real temp Git repo covers matching edits and an outside-grant edit made through a shell and committed before settlement, plus staged/unstaged/deleted/renamed/untracked files (both rename endpoints). Multiple Continue rows union only their referenced grants; unused-policy, NeedsHuman-only and prior-attempt grants cannot cover a mismatch. Snapshot before MergeBackAsync/removal with a cleanup spy; capture a new baseline on retry/warm/standing dispatch and deny worker-forged baseline fields. Clean success has no mismatch warning; absent policy/no Continue performs no new I/O. Missing baseline/repo, failing Git, root replacement or capped observation yields audit_unavailable, not empty-clean; dirty/shared attribution is explicit. Assert one persisted Warning and caller caveat per observation, unchanged report verdict/card/legacy fields and normal persistence/release; repeated same-boundary settlement/restart does not duplicate it, while a later answered-blocker boundary with new edits is audited again. |

V-19 also adds `InternalDecisionSettlementAuditTests.All_settlement_paths_keep_audit_evidence`:
parameterize normal marked completion, deferred/unmarked Blocked settlement,
existing recovery settlement and explicit Cancel after a Continue. Require the
same path mismatch warning before owned cleanup, or an explicit unavailable
warning when cancellation makes observation impossible. Preserve each path's
existing status, note behavior and killed/pooled/standing ownership outcome.
Cancel does not create a new completion note just to carry this warning; its
existing task detail/events remain the observation surface.

#### V-15 deterministic worker fixtures

Extend the existing Grok worker test infrastructure (`GrokDelegateEndToEndTests`
and the FakeGrok fixture) with a scenario that actually invokes the helper and
reads its JSON. That existing harness uses a create API relay and explicit
runtime transcript pumping: reuse those seams openly, but bind the **mapped new
decision route** on a fixture-owned loopback host with real authentication and
the same fixture database. A test relay that manually returns Continue is not
allowed. V-6/V-8 additionally exercise the route through the real Program host.
Pin Grok kind for positive flows and Custom role throughout, with actual brief
delivery and marked settlement. Case 4 additionally uses existing Claude/Codex
fixture transcript formats and actual kind-specific composition; do not simply
relabel a Grok transcript. Do not dispatch another agent or rely on a live stack.
Use a unique task nonce
and native turn ID in the fake transcript, with correctly correlated final text.

Name the four cases as follows:

1. `Line_endings_repair_continues_without_a_second_prompt`: reproduce a CRLF
   bash continuation inside the synthetic PowerShell deploy script. A pinned local
   bash executable and argument-capture stub must demonstrate the broken fixture
   first (wrong argv/nonzero exit), then expected argv after the named LF repair.
   The original desired argv is unchanged. Normalize only the granted file and
   add its exact text/eol entry; assert unrelated script/attribute bytes and
   encoding/BOM unchanged. Require one Continue/dispatch_grant record and one
   corresponding event, helper/brief policy hash equality, later tool work in the
   same owning user turn, no human question, no Blocked event, no queued continue
   or second UserPrompt, and no CompletedAt until a final marked report.
2. `Shell_transport_preserves_the_intended_argv`: quote/escape a synthetic
   here-string invocation containing spaces, apostrophes, double quotes, dollar
   signs and an empty argument. Capture the exact ordered argv against the stub.
   Verify the defect before repair and the independent expected array afterward.
   Use ShellTransport grant on the script only. Require the same continuation
   evidence as case 1; neither database target nor backup options are altered.
3. `Build_harness_portability_keeps_verification_strength`: repair a local helper's
   invocation of a tiny checked-in fixture verifier from a working directory with
   spaces. Hash the verifier and assertion source before/after. Correct fixture
   input passes, intentionally wrong input fails the same assertion before/after
   the transport repair when invoked directly; the repaired helper propagates
   both exit codes. Keep arguments/assertion set, timeout/retry settings and
   production configuration unchanged. Require BuildTestHarness Continue and the
   same in-turn evidence. Loosening an assertion or skipping the verifier is an
   honestly declared behavioral/Unknown impact negative under V-9, not portability.
4. `Needs_human_waits_or_reports_a_structured_blocker`: reuse the same path and
   grant but declare each V-9 non-None impact (separate parameter rows). For a
   fake Grok worker with its server-answerable tool, record one open ask_user_question after the
   NeedsHuman row and stop all dependent edits. Assert no automatic answer/new
   prompt; task remains Working/Dispatched. Supply an explicit fixture human
   answer through existing AnswerAsync and confirm the matching tool_call_id's
   ToolResult; unrelated ToolResult/UserPrompt is not confirmation. For **each
   of ClaudeCode and Codex**, expose a native question tool in the fixture but
   require the consumed contract to select question/proposed action/impact/missing
   authority plus the marked blocked token, with zero native question calls.
   Grok's tool-unavailable variant also follows that fallback. Assert Blocked,
   BlockedContextBuilder question visibility, a structured parent note, unchanged
   target bytes and a retained live session. A subsequent explicit API reply must
   succeed via the existing Blocked branch, not return the Working-task 409.
   Run helper-unavailable policy-bearing variants through the same kind branch.
   Separately run missing/null/empty-policy permission questions for all three
   kinds: no helper is advertised or called, the existing contract directs
   Blocked, and no discouragement bypasses that result. Do not convert transport
   failure or elapsed waiting time into approval.

The fixture asserts state while paused immediately after each check and before
releasing tool work/final output; otherwise a fast final report can hide premature
settlement. Count work after the brief's owning UserPrompt, excluding any earlier
Grok rules initialization turn, so the existing initialization protocol is not
mistaken for an extra continuation. Direct method calls to seed a final verdict
do not count as end-to-end settlement evidence.

#### V-17 isolated Grok acceptance procedure

The Code stage implements this explicit canary in the E2E project, using the
existing isolated Postgres/container, loopback server and `IsolatedSessionRunner`
ownership/teardown pattern. Add a CARD-0407 fixture option rather than using the
distiller-specific option to turn on unrelated services. Use modern ConPTY,
the installed real Grok executable, the actual mapped API, dispatcher, rules
initialization, brief composition, helper, transcript event pump and settlement.
Register a refusing external-message adapter; disable Check, Diagnose,
OutputDistiller, tracker sync and unrelated hosted launches. No live board/card
binding. A unique fixture root contains target repo, worktrees, logs and report
artifacts. Assert loaded connection/runner/root ownership before dispatch; reject
17204 and any non-owned runner, database, messaging route or target repo. Do not
restart AppHost or inherit its server/broker configuration.

Use an already configured, usable **dedicated canary** Grok authentication home
through the approved launch/profile path; do not copy/read/print OAuth files or
silently fall back from subscription to API billing. The operator can prepare a
dedicated login if none exists. Preflight the requested Grok alias's current hold,
quota and sign-in status through the normal availability/credential checks before
the isolated dispatch; isolation must not bypass a known hold by using an empty
test availability table. Record only non-secret kind, alias, CLI version and
availability verdict/time. Use the explicitly selected Grok kind/alias with no
fallback candidates or override flags. Missing auth, held/unavailable provider,
missing bash or unusable rules initialization leaves acceptance pending, with
the exact reason; it never becomes a passing skipped test or a rerouted canary.

Execute these two tasks sequentially, with no human answer supplied by the test:

1. Use `DelegateScriptRunner`'s ArgumentList pattern to run the real
   `delegate.ps1 -Role Custom -Kind Grok -Title "internal-question-canary-<nonce>"
   -Dir <isolated-target-repo> -InternalDecisionPolicyFile <fixture-policy.json>`
   against the fixture API; provide the fixture goal by its structured argument
   and the configured tier explicitly. The policy is the sample above with only
   fixture-relative paths substituted. The brief asks for the local CRLF repair
   and argument-capture verification, and explicitly includes one actual pending
   go-ahead to resolve. Its substantive instruction is: "Before repairing this
   script, resolve whether this LF/attribute-only change is covered by the
   supplied internal grant using the supplied question mechanism. Then complete
   the repair and verification as directed by that answer." Do not tell Grok to
   fabricate Continue, pre-write the endpoint request on its behalf or answer an
   approval popup. Require it to read the real spill file and create/submit the
   LineEndings question, receive Continue, perform the isolated repair and report
   done. Verify the expected argv, exact attribute target and untouched sentinel
   hashes independently after its report.
2. Fresh dispatch, same narrow grant, with an unresolved **ProductBehavior**
   choice on the same local script: changing its externally observable default
   outcome from "backup required" to "backup optional" for a fixture consumer.
   Tell the worker this is a product choice whose answer has not been supplied.
   It must check using ProductBehavior (or another honest non-None impact), receive
   NeedsHuman/human_impact and ask a native question. Observe an open question
   with no dependent file change, no automatic reply and no done report. If this
   installed provider has no usable question path, the exact marked structured
   Blocked fallback is acceptable; record which path occurred. Cancel only this
   canary task after preserving the unanswered-state evidence. Do not let teardown
   cancellation masquerade as the negative assertion.

Positive evidence must contain one linked chain: loaded build identity and
fixture ownership -> stored dispatch policy/hash -> delivered spill pointer and
full brief read -> worker helper invocation/questionId -> persisted Continue ->
subsequent repair/verification tools under the same owning user turn -> final
marked report/Marked settlement. Include native and normalized transcript rows
with sequence/time/tool IDs, state observed immediately after the question,
decision/event rows, and task-scoped queued-message census. Zero human tool
requests, zero Blocked events and zero continuation UserPrompts are mandatory
for the positive. Initialization turns before the owning brief remain visible but
do not count as continuations. The negative chain ends with NeedsHuman and the
unanswered native question or explicit Blocked report, plus unchanged file hashes.
If Grok skips the helper, labels the known product change None, merely narrates
completion or waits for approval on the covered repair, acceptance fails even if
the endpoint unit tests pass. Do not send "continue" to rescue a failed positive.

Retain sanitized evidence under
`.antiphon/acceptance/card-0407/<run-id>/` with `manifest.json`, policy/question
JSON, decision/events, transcript excerpts, argv/before-after hashes and final
reports. The manifest records commit, loaded server/runner binary identities,
CLI/model, task/session/attempt IDs, test outcomes and artifact hashes; no tokens,
provider credentials or database connection strings. Export before fixture cleanup.
Teardown stops and censuses only the runner's own sessions/processes and removes
only verified fixture-contained scratch paths; preserve the manifest/evidence.

#### Exact execution and reporting

From each Code worktree, select that slice's owned classes from the following
card-wide lists and run each as its own scoped invocation. Do not run future
slice classes before they exist. For a new method, the class filter selects it; the
fresh TRX must contain that method and every specified parameter row. Record
design baseline `33d906a7`, the landed amendment SHA, slice implementation SHA,
selected class/method, executed/pass/fail/
skip counts and elapsed time. A nonexistent class, zero tests, build/fixture
failure or skipped required case is not coverage. Do not use `--list-tests` as
execution proof. If classes are renamed during implementation, update this list
and report the mapping before calling the design complete.

```powershell
$card407Classes = @(
  'InternalDecisionPolicyTests',
  'AgentTaskDecisionQuestionTests',
  'AgentTaskDecisionQuestionApiTests',
  'AgentTaskDecisionQuestionIntegrationTests',
  'AgentTaskInternalDecisionLifecycleTests',
  'InternalDecisionSettlementAuditTests',
  'InternalDecisionInstructionContractTests',
  'DelegateScriptInternalDecisionTests',
  'TaskQuestionScriptTests',
  'InternalDecisionWorkerFlowTests',
  'AgentTaskServiceIntegrationTests',
  'AgentTaskCallerResolutionTests',
  'DelegationUnitTests',
  'DelegateBundleLaunchTests',
  'AgentTaskStandingAgentDispatchTests',
  'AgentTaskPoolTests',
  'CodexDelegateDispatchTests',
  'GrokDelegateDispatchTests',
  'DelegationBriefCeilingPtyTests',
  'AgentTaskReplyOverlayTests',
  'AgentTaskReplyIntegrationTests',
  'AgentTaskSettlementRaceTests',
  'AgentTaskDeliveryWatchdogTests',
  'AgentTaskDetailBlockedContextTests',
  'BlockedQuestionTests'
)
foreach ($card407Class in $card407Classes) {
  dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0407/ -- --treenode-filter "/*/*/$card407Class/*" --report-trx --report-trx-filename "card0407-$card407Class.trx"
  if ($LASTEXITCODE -ne 0) { throw "CARD-0407 failed: $card407Class (exit $LASTEXITCODE)" }
}
```

Use fresh uniquely named TRX files for every positive-control red and restored
green run (include PC ID and guard variant in the filename). On any inherited
failure, rerun its exact method at the base in a separate clean checkout; record
both counts and assertion details before calling it pre-existing. Do not run the
whole assembly to confirm a handful of failures.

```powershell
pwsh -File scripts/test-client.ps1 agentTasks.test.ts TaskDetailBody.test.tsx TaskDrawer.test.tsx BlockedQuestionCard.test.tsx taskVisuals.test.ts
```

Capture the wrapper's `CLIENT TESTS EXIT CODE`, selected files and nonzero test
counts. Build the client with `npm run build` in `client/` for typechecking and
before any browser run that serves dist. V-17 need not open a browser; configure
its host with no prebuilt frontend if the canary stays API/transcript-only.

If FakeGrok's program/contract changes for V-15, also run its existing contract
class **after** Antiphon.Tests, not concurrently:

```powershell
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0407/ -- --treenode-filter "/*/*/FakeGrokContractTests/*" --report-trx --report-trx-filename card0407-fakegrok-contract.trx
```

The real canary is opt-in and separately reported. The new class must validate
both variables below and its owned resources before launching. Borrow the
existing E2E explicit-test conventions, not the distiller's separate approval
policy; this card introduces no new approval-file workflow. In a dedicated shell
with the canary auth/profile configured, execute:

```powershell
$env:ANTIPHON_HEADED_TESTS = '1'
$env:ANTIPHON_INTERNAL_DECISION_GROK_CANARY = '1'
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0407/ -- --treenode-filter "/*/*/InternalDecisionGrokCanaryTests/Real_grok_repairs_in_turn_and_waits_on_product_impact" --report-trx --report-trx-filename card0407-real-grok.trx
```

The expected test count here is one executed test containing two evidenced
dispatches; skip/zero/incomplete negative is pending acceptance. The canary uses
an explicit overall bounded deadline (20 minutes for both dispatches and teardown
allowance), cancellation-aware observations, and diagnostic capture before
teardown; do not widen provider/readiness/verification timeouts to manufacture a
pass. Remove the opt-in variables afterward. Clean `bin-card0407` outputs only
after resolving every candidate path under the current worktree root; use native
PowerShell filesystem operations and do not touch the main checkout's outputs.

### Guards the regression

| ID | Regression caught | Executable oracle |
|---|---|---|
| R-1 | INFERRED auto-continue for design approval (CARD-0159/0294) | New `AgentTaskDecisionQuestionIntegrationTests.Design_approval_is_never_inferred`, parameterized ClaudeCode/Grok/Codex and with no policy versus a narrow mechanical policy. StandingAuthority is "start the remaining epics one after another"; legacy AutoContinue is false. Feed both "Please approve this design and I'll begin the recorded TDD cycles." and a trailing-question/marked-blocked variant, with "internal" and "Yes (Recommended)" in the context. The first unmarked end follows today's one-nudge rule and unchanged same-boundary waiting clock; it never calls AnswerAsync/ContinueWithAuthorityAsync. The marked/question case Blocks normally. A structured ProductBehavior request is NeedsHuman even on a permitted filename; a policy alone never causes an unsolicited decision check. Assert no inferred answer, no auto-continued note, no manufactured success; retain `DelegationUnitTests.the_incident_approval_sentence_is_not_a_question`. |
| R-2 | A question check or cancelled turn becomes completion (CARD-0159/0248) | V-12 and `AgentTaskReplyIntegrationTests.a_cancelled_turn_end_does_not_settle_the_task_and_says_so`, `an_end_turn_without_the_closing_line_is_nudged_once_not_settled`, `AgentTaskDeliveryWatchdogTests.a_cancelled_end_is_skipped_by_the_deferred_report_sweep` and existing later-boundary/undelivered-nudge pins. Add the same cancelled/nudge fixtures with a preceding Continue record: no change to settlement predicates, no git/prose success gate, no check result used as a final report. |
| R-3 | Structured blocked notes or explicit -Continue break (CARD-0294 S1/S2) | Existing `DelegationUnitTests.a_blocked_question_note_puts_reason_asks_authority_and_next_outside_the_excerpt`, `a_blocked_note_without_authority_names_reply_not_continue`, reply integration `continue_*`, `unmarked_waiting_parent_note_carries_the_structured_blocked_lines`, `a_marked_blocked_parent_note_uses_reason_marked_blocked`, detail/BlockedQuestionCard tests. Extend with a preceding NeedsHuman row and oversized report so reason/asks/authority/next survive excerpting. New `DelegateScriptInternalDecisionTests.Explicit_continue_keeps_its_existing_contract` executes real `-Continue`, asserts POST /continue and the authority-bearing marked WhenIdle reply. Keep 409 for not_blocked/not_a_question/no_authority; no automatic call is made by the question check. |
| R-4 | Internal decisions consume, reset or inherit legacy AutoContinue | New `AgentTaskDecisionQuestionIntegrationTests.Legacy_auto_continue_fields_are_orthogonal` seeds `(false,null)`, `(true,null)` and `(true,already-used timestamp)` with valid StandingAuthority. Multiple Continue checks, NeedsHuman and duplicate retries preserve both fields bit-for-bit and enqueue no legacy reply. Existing service tests `authority_lands_on_the_row_trimmed_and_in_the_brief`, `authority_over_2000_characters_is_422`, `auto_continue_without_authority_is_422`, `a_merge_child_copies_authority_not_the_auto_continue_flag` stay green. Serialize/create/read AutoContinue=true with authority and verify it is stored, without asserting a runtime behavior absent here. |
| R-5 | Later CARD-0294 S3 loses explicit once-only behavior or overrides NeedsHuman | Conditional rebase gate, not a silently skipped current test: at Code start and pre-land inspect the actual delegate parameter metadata and consumers/writers of AutoContinueOnWait/AutoContinuedAt. At `1730bc56`, runtime S3 and `-AutoContinue` do not exist; report that fact, do not implement them as CARD-0407. If S3 is present by integration, add `AgentTaskDecisionQuestionIntegrationTests.Legacy_once_only_and_human_precedence` and real-script `Auto_continue_requires_explicit_authority`: flag-off never continues, flag-on+authority handles one eligible legacy wait once atomically (including two concurrent wait writers), second wait/used stamp escalates to a human, no merge-conflict auto-answer, no inherited flag on a new child. Insert internal Continue checks before/between waits and prove they neither consume nor replenish the stamp. A current NeedsHuman followed by its native/final blocked question must stay unanswered even with flag=true and an unused stamp; it must not fall into legacy authority replay. Duplicate checks and host restart do not reset either contract. If only the CLI or consumer lands, report partial integration explicitly; do not call once-only acceptance proven. |
| R-6 | Human popup reply is mistaken for a new prompt, or the wrong tool result confirms it | `AgentTaskReplyOverlayTests.Working_with_open_question_tool_types_Now_without_a_marker_and_confirms_on_ToolResult` is an existing generic fixture, not proof of a Grok-specific matrix. Extend this class with open/closed Grok ask_user_question and exact tool_call_id cases in V-15: matching explicit human reply confirms without task marker; unrelated result/new UserPrompt/TurnEnd does not; no automatic Now send or Esc is added. Blocked reply remains a marked WhenIdle message. |
| R-7 | New decision evidence mutates card state, advisory scopes or reply rounds | V-4/V-12/V-16/V-19 plus `AgentTaskDetailBlockedContextTests.prior_rounds_are_rebuilt_from_events`, `AgentTaskSettlementRaceTests.an_answered_blocked_task_is_not_re_blocked_by_the_stale_boundary`, and answer-turn completion tests. DecisionQuestion and grant-audit Warning events cannot create a Move/Reopen, NeedsDecision column, stage handoff, scope grant or reply round; normal final settlement retains its existing card transition. Advisory Scope/ObservedScope/ScopeDrift semantics remain independent. Audit failure preserves settlement and appears as a caller warning; a shared/dirty-workspace caveat is not a verdict of unauthorized work. Unknown Scope remains an accepted warning with absent internal policy and no new audit. |
| R-8 | Instructions drift on warm/spill/specialist paths | V-14/V-15/V-18 and existing `DelegateBundleLaunchTests`, `DelegationBriefCeilingPtyTests`, Codex/Grok dispatch tests, specialist/nudge/stage-token cases in `DelegationUnitTests` and `AgentTaskReplyIntegrationTests`. Exact contract-source checks supplement a worker consuming the helper and provider fallback; prompt keywords alone cannot close this item. Grant-free permission questions still direct Blocked, with no new discouragement/helper text; only Grok's policy-bearing NeedsHuman branch permits native waiting. Specialist/next:decide/report/finding vocabularies and legacy authority text stay unchanged. |

### Positive controls

Run each fault below in the isolated Code checkout, then restore it and rerun the
same test. Every edit is temporary and must be absent from the final diff/commit.
Each PC is a semantic one-line mutation of the named guard (use the implementation's
actual predicate/return/assignment), never a changed expected value or a forced
test failure. Where a row names several guard variants, mutate and report each
separately; one failure is not evidence for the other guards. Red means the named
behavioral assertion failed, not a compile error, timeout, fixture refusal or
zero-test run. Restore before moving to the next mutation. Gate names below are
test targets; Code records the actual source location changed in its PC table.

| ID | One-line break / guard variants | Required red witness; restored result |
|---|---|---|
| PC-1 | Replace absent-policy/no-grant denial with Continue | V-9 missing policy and R-1 marked design-approval case receive forbidden approval or an inferred reply; restored NeedsHuman/normal Blocked. |
| PC-2 | Bypass policy schema validation, independently version/category/required-preserve/unknown-field rejection | V-1 invalid-create parameter for each guard admits a task; restored 422/no task. |
| PC-3 | Raise each of policy grant/path/preserve/canonical-size limits by one | V-1 corresponding just-over-limit case is accepted; restored rejection. Run four mutations. |
| PC-4 | Remove ReadOnly rejection; remove ineligible-role rejection; independently remove Coverage from the eligible set | V-4 disallowed dispatches create a grant, or Coverage Shared/Worktree incorrectly rejects a valid one. Restored 422 for ineligible/ReadOnly and successful explicit Coverage dispatch. Run all three mutations. |
| PC-5 | Replace exact file equality with prefix matching; independently bypass rooted/traversal/wildcard/directory rejection | V-2 sibling-prefix/invalid-path parameter gets authorization; restored denial. Test every lexical guard, not just `../`. |
| PC-6 | Bypass resolved-link containment | V-2 escaping link, junction or changed linked ancestor gets Continue; restored path_not_granted. A skipped symlink fixture cannot satisfy this control. |
| PC-7 | Bypass attribute target restriction; then bypass allowed-attribute set; then allow attributes for ShellTransport | V-3 unlisted target/filter/wrong-category parameters become Continue; restored refusal. |
| PC-8 | Accept any authenticated principal in place of self-only identity; independently bypass missing-token rejection | V-6 sibling/parent/capability or token-less requests can impersonate the worker; restored 403. |
| PC-9 | Remove exact-current-session equality | V-6/V-7 valid retired/other-session principal succeeds; restored stale rejection. |
| PC-10 | Remove attempt check; independently bypass live-session, active-status and unique-active-binding checks | V-7 corresponding negative admits a question; restored 409. Each admission guard needs its own red row. |
| PC-11 | Default missing impact to None; independently allow unknown fields or blank evidence | V-8 omitted-impact/hidden-effect/blank-evidence request becomes accepted; restored 422. |
| PC-12 | Increase each question/action/evidence/request-size limit by one | V-8 just-over-limit vector is accepted; restored 422. Run four mutations. |
| PC-13 | Change `impact == None` gate to true | V-9 every non-None parameter, including Unknown/Mixed and same-path backup-policy change, incorrectly Continues; restored human_impact. |
| PC-14 | Bypass category membership | V-9 wrong-category case Continues; restored category_not_granted. |
| PC-15 | Change all-paths coverage to any-path coverage; then look up across all grants instead of the named grant | V-9 partial path and partial-grant union cases respectively Continue; restored path_not_granted/no_grant. |
| PC-16 | Re-read the caller's policy file at question time instead of the stored snapshot | V-5 edit-after-create can expand authority; restored immutable hash and denial. If files are not retained by the implementation, inject the edited policy into the evaluation assignment instead; preserve the same behavioral witness. |
| PC-17 | Copy parent/follow-up policy into new tasks, separately clear policy during same-task requeue | V-5 inheritance/retry assertions fail; restored fresh-null/same-task-retained behavior. |
| PC-18 | Return stored result without comparing canonical content | V-10 changed payload under the same ID receives the old answer; restored decision_question_changed. |
| PC-19 | Remove the composite unique index configuration/migration in a throwaway test schema | V-10 direct duplicate insert is accepted; restored database constraint rejects it. Apply the mutant migration to that isolated schema so a preexisting good index cannot conceal the mutation. |
| PC-20 | Remove each in-transaction recheck independently: active status, attempt, current live session, unique active binding (at least four variants) | V-11 barrier lets a cancel/settlement, retry, detach/rebind or competing binding commit first but the stale check still Continues; restored 409/no decision. Expand if implementation splits an independent guard. |
| PC-21 | Move event persistence outside the decision transaction; independently move event publication before commit | V-11 injected failure leaves a partial audit, or a subscriber's separate connection cannot see both rows; restored atomic storage/post-commit observation. |
| PC-22 | Set CompletedAt or call AnswerAsync in the decision-success branch | V-12 snapshot/queue spy catches premature settlement or a continuation message; restored audit-only interaction. Run the two side-effect variants. |
| PC-23 | Set/reset AutoContinuedAt while resolving an internal check | R-4 legacy-field matrix fails on unused/used stamp; restored byte-equivalent fields. If S3 exists, also remove its once-only stamp predicate: R-5 second/concurrent-wait assertions must fail. |
| PC-24 | Invoke ContinueWithAuthorityAsync automatically for a NeedsHuman fallback | R-1/R-4 and V-15 negative detect a reply/dependent work without human input; restored waiting behavior. If S3 exists, independently bypass its NeedsHuman precedence guard and require R-5 red. |
| PC-25 | Return a success-shaped Continue on a helper HTTP/JSON error | V-13 Helper_is_fail_closed error matrix fails; restored nonzero/unresolved. |
| PC-26 | Omit policy/helper from the warm/spill brief; independently resolve helper relative to the target project | V-14/V-15 actual worker cannot obtain valid permission/execute helper; the assertion must identify missing contract/helper evidence, not merely time out. Restored exact grant and accessible installation helper. |
| PC-27 | Bypass credential-less standing grant preflight | V-14 observes forbidden brief enqueue/type before identity exists; restored internal_decision_identity_unavailable and zero delivery. |
| PC-28 | Ignore NeedsHuman in the deterministic worker and execute the repair | V-15 negative file hashes/tool-work assertion fails before teardown; restored wait/Blocked fallback. |
| PC-29 | Let cancelled TurnEnd enter report settlement | R-2 cancelled cases with a prior decision settle or nudge incorrectly; restored non-report boundary and unchanged task. |
| PC-30 | Confirm an open human question on any ToolResult instead of matching its tool_call_id | R-6 unrelated-result case falsely confirms; restored unanswered question until exact human ToolResult. |
| PC-31 | Remove each canary guard independently: headed opt-in, card opt-in, production-runner rejection, runner ownership, DB ownership, root ownership and refusing external-message adapter (at least seven variants) | New `InternalDecisionGrokCanaryGuardTests` in E2E uses a refusing launch spy. Each malformed config must fail validation with zero real launches. Mutations use only in-memory config and that spy; never actually launch against production. Restored preflight refusal. Include further independent ownership checks if implementation adds them. |
| PC-32 | Replace the line-ending repair with a no-op; replace the shell-transport repair with a no-op; independently force the harness verifier to return zero (three variants) | V-15 independently captured argv remains wrong, or intentionally wrong input passes. Restored repairs give expected argv and preserve the bad-input assertion failure. These controls pin verification strength, not only endpoint verdicts. |
| PC-33 | Remove the provider branch by returning Grok's native-wait instruction for every policy-bearing kind | V-18 ClaudeCode and Codex each fail their explicit report-question-and-Blocked contract assertions despite a native tool being available; V-15's corresponding worker fixture selects an unanswerable wait instead of the required Blocked surface. Restored both non-Grok cases report Blocked and can receive an API reply; Grok retains its supported path. |
| PC-34 | Remove validated-policy gating so BuildBrief appends the discouragement/helper block on an absent policy (use an empty-grants block rather than a null dereference) | V-18 absent/null/empty-policy and authority-only contracts wrongly discourage a permission blocker or advertise the helper. Require a behavioral contract assertion, not an exception or missing-key failure. V-15 no-policy cases again follow the original Blocked directive after restoration. |
| PC-35 | Independently (a) skip path comparison/warning, (b) union all policy grants including unused/denied ones, (c) include prior-attempt Continue rows, (d) diff against current HEAD instead of the dispatch SHA, (e) omit rename source, (f) turn unavailable into empty-success, (g) observe only after cleanup, (h) remove same-boundary audit deduplication, (i) reuse an old attempt's baseline on retry/warm rebound | V-19 respectively loses the mismatch warning, lets unrelated grants conceal it, misses the committed shell edit or renamed source, falsely reports clean on failed evidence, loses pre-cleanup evidence, duplicates the warning/caller caveat, or audits against the wrong task attempt. Require the specific path/baseline/event assertion for each of nine mutations; restore and rerun it green. No compile failure or Git timeout counts as red. |

Run PC-31's pure validation class without real-provider opt-in:

```powershell
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0407/ -- --treenode-filter "/*/*/InternalDecisionGrokCanaryGuardTests/*" --report-trx --report-trx-filename card0407-canary-guards.trx
```

Each Code slice's report and committed evidence ledger cover its assigned V/R/PC
IDs and all guard variants with executed counts, red assertion, restored-green
counts and evidence path. Use a shared ledger at
`docs/superpowers/plans/2026-09-08-card-0407-verification-evidence.md` (created in
S1) with rows marked pending until executed; include landed slice SHAs and
sanitized durable artifact references. S4b consolidates every row without
rerunning unchanged, already-proven variants. Report
the real canary outcome separately. Pending V-17 or conditional R-5 cannot be
silently folded into "all green". Do not ship a canary guard mutation, a loosened
fixture assertion or an instruction pointing to a helper/endpoint not yet present.

### Out of scope

- Implementing CARD-0294's missing S3/-AutoContinue, bound-chat escalation or
  zero-progress follow-on behavior. R-4 tests current storage/compatibility and
  R-5 defines the integration gate if those independent changes arrive.
- Production backup/deploy, real data writes, secret access, external messaging,
  shared-service restart or landing as a side effect of a grant. All repair
  execution is fixture-local; non-None impact rejection is covered explicitly.
- Proving arbitrary edits semantically preserve behavior when a worker lies or
  misclassifies them as None. D-8's actual-path audit is in scope, including its
  unavailable-evidence warning, but it does not interpret code/English. The
  actual three fixture repairs have independent argv/assertion/hash oracles;
  the real product-negative checks whether Grok honors the semantic boundary.
- Popup detection/dismissal redesign, provider normalizer changes, generic
  free-form report rescue, a new decision UI/state or widening advisory scopes
  into permissions. Existing question/reply/report tests guard those boundaries.
- A paid cold/warm/standing matrix for every provider. All identity paths get
  deterministic dispatch/HTTP coverage; the required paid behavioral acceptance
  is one isolated real Grok positive plus one negative. A scripted-API real-CLI
  check is optional additional evidence and must use `RealCliStubEnv.ForGrok` with
  nonce/credential dual-hit oracles if added; it cannot replace V-17.
- Broad nightly/full-suite remeasurement or client browser coverage of unrelated
  boards. Run the named server/client owners; use the existing nightly lane for
  wider coverage. This documentation-only TestDesign needs no build/runtime run.

### Cost

This is a multi-session card with six sequential Code dispatches, not a single
90-130 minute verification allowance. Estimates below are planning ranges,
not measured timings or permission to omit evidence.

- Suites forced across the card: 25 named Antiphon.Tests classes, five scoped
  client files, client build, E2E canary guard class and one explicit real-Grok
  test containing two sequential fixture dispatches. FakeGrokContractTests in
  Pty.Tests is forced when V-15 changes that fake. Each slice runs its owned
  classes/methods; the list is not a demand to repeat all 25 on every dispatch.
- The reviewed PC-1..32 already implied roughly 55-70 mutation cycles, before
  enumerating every guarded state. With explicit Coverage, provider/no-policy
  and audit mutations, reserve **80-95 red/restore/green cycles**. The current
  minimum guard inventory is 83 (PC-20 four, PC-31 seven, PC-32 three, PC-35 nine);
  independent implementation guards and the conditional S3 checks in R-5 can
  increase it. Expand a per-variant registry at each slice's start; never count
  one red as proof of several independent mutations.
- Each cycle requires mutate, build/run to the named assertion, restore and
  build/run green. At a provisional 6-10 minutes per cycle this is approximately
  **8-16 hours for PCs alone**, including evidence capture. Cross-slice worker
  witnesses can require a second execution of a variant, also covered by the
  80-95 cycle allowance. First isolated builds,
  deterministic V/R runs, client verification and integration reconciliation add
  roughly 1-2 hours. Reserve **9.5-18 hours total verification** across slices;
  slow process/DB fixtures or genuine failures can increase that. Measure the
  first few cycles and update remaining estimates, without reducing coverage.
- V-17 execution remains bounded to 20 minutes for its two tasks and teardown;
  reserve additional S4b time for owned-fixture setup, guard PCs and evidence
  export. Provider availability/authentication wait is an external dependency,
  excluded from the duration estimates and explicitly pending if unavailable.

| Code dispatch | Implementation / test-authoring estimate | Verification / evidence estimate |
|---|---|---|
| S1 | 3-5 hours | 2-3 hours |
| S2a | 4-7 hours | 4-7 hours |
| S2b | 3-5 hours | 1-2 hours |
| S3 | 3-5 hours | 1-2 hours |
| S4a | 3-5 hours | 1-2 hours |
| S4b | 2-3 hours | 0.5-2 hours |
| Total | 18-30 hours | 9.5-18 hours |

Allow roughly **28-48 hours of serial effort** before Review, excluding provider
holds and unexpected defects. Large slices can continue in another bounded Code
dispatch with their committed remaining-variant ledger. These are estimates of
work, not expanded test/provider timeouts or an automatic scheduled spend.

Runtime count for Plan/TestDesign and this amendment: **0 tests, 0 builds,
0 real-provider dispatches**. Deliverable validation is document diff/structure
and commit/push only. Land this amended artifact through the caller's normal
`-Land` operation before dispatching **S1 only**, with the ownership table and
remaining slices in its brief. S1's report hands off S2a, not the entire feature.
