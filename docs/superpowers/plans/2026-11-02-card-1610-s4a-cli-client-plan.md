# CARD-1610 S4a: create-time ceiling, CLI seat-held and typed reply, client hold card

Date: 2026-11-02. Plan task 5073e215 (server2, Linux, docs only).
Source base: origin/master `10c8c2471633c7c0212294afc57348878021e183`.
Inputs: the reviewed round-3 plan
[2026-10-29-card-1610-failed-task-live-session-plan.md](2026-10-29-card-1610-failed-task-live-session-plan.md)
(D-3, D-4, D-6, D-8, the S4 row, V-13/V-14, CP-13/14/18/20), the S2/S3 repair table
[2026-10-31-card-1610-s2s3-schema-fix-plan.md](2026-10-31-card-1610-s2s3-schema-fix-plan.md),
and investigation a45f88c8 (`docs/investigations/2026-11-02-card-1610-next-stage.md` at
`c245a828b2acb6ef5914b597b0795c82ae9009e1`).

This file amends the round-3 plan for **S4a only**. It does not edit the round-3 file, so it
cannot cause an add/add conflict there. For S4a it supersedes round-3 rows CP-13, CP-14, CP-18 and CP-20
and the S4 half of V-13. It does **not** re-run CP-21..CP-105, the S2/S3 sweep that the Final Review
already passed. S4b (deploy-server2.ps1 drain slices, docker-stack.md steps 4/8 and the six V-13
drain cases) is out of scope. Under round-3 D-6 it waits for CARD-1615 Code and moves to its own follow-up card.

## Problem

S1-S3 are on master. A role ceiling now holds a Working task instead of failing it, and the server
exposes `failedSession` and `ceilingHold` on task detail. Three surfaces are still missing:

1. **No declared budget can reach the server.** `AgentTask.RequestedCeilingMinutes` and
   `RequestedCeilingReason` exist (migration `20261030114738_AddTaskCeilingSupervision`), and
   `CeilingHoldFacts.Resolve` (`server/Application/Services/CeilingHoldFacts.cs:28-35`) prefers them.
   But `CreateAgentTaskRequest` has no ceiling field, and nothing in `server/` writes those
   columns. The `requested` source is dead code, so a declared long Review or Deploy still
   holds at the role default.
2. **The CLI cannot see or answer the hold.** `delegate.ps1 -Status` reads neither `failedSession`
   nor `ceilingHold`. This is the card's Wanted item 2, the "seat held" line. `-Reply` sends only
   `message` plus `origin: 'Cli'`, and the typed ceiling route refuses any non-null `origin`. So
   the CLI cannot send Continue or Extend at all, nor an operator Extend.
3. **The client cannot see or answer the hold.** `client/src/api/agentTasks.ts` types
   `failedSession` but not `ceilingHold`. No `CeilingHoldCard` exists, and `TaskDetailBody` shows
   only `BlockedQuestionCard`, which never renders for a Working hold.

## Scope (exactly S4a)

| Item | Files |
|---|---|
| Create-time ceiling binding | `server/Application/Dtos/AgentTaskDtos.cs` (one `partial` keyword on three records only); **new** `server/Application/Dtos/AgentTaskDtos.Ceiling.cs`; `server/Application/Services/CeilingHoldFacts.cs` (create validation and bind helpers); `server/Application/Services/AgentTaskService.cs` (validation call after the ExpectedMinutes check, a bind call after the `storedPolicy` block, created-DTO echo, detail echo); `server/Application/Services/DelegationReportFormatter.cs` (one brief line when requested) |
| CLI | `scripts/delegate.ps1`: Create `-CeilingMinutes`/`-CeilingReason`; Reply `-CeilingAction`/`-EpisodeId`/`-ExtendMinutes`/`-OperatorExtend`/`-CeilingReason`; Status lines from `ceilingBudget`, `ceilingHold`, `failedSession` |
| Client | `client/src/api/agentTasks.ts` (types plus `useCeilingDecision`); **new** `client/src/features/delegations/CeilingHoldCard.tsx`; `client/src/features/delegations/TaskDetailBody.tsx` (render the card) |
| Docs | `docs/ops-http.md` (new section appended at end of file) |
| Tests | **new** `tests/Antiphon.Tests/Scripts/TaskCeilingScriptTests.cs`; **new** `tests/Antiphon.Tests/Application/TaskCeilingCreateBindingTests.cs`; `tests/Antiphon.Tests/slow-tests-allowlist.txt` (one FQN plus reason); `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs` (one `StrictClasses` entry); **new** `client/src/features/delegations/CeilingHold.test.tsx` |

Not touched by S4a: `server/Domain/Entities/AgentTask.cs`, `AppDbContext.cs`, any migration or the
model snapshot, `AgentTaskReplyService*.cs`, `AgentTaskEndpoints.cs`, `AgentTaskDispatcher*.cs`,
`scripts/deploy-server2.ps1`, `docs/docker-stack.md`. A Code stage that finds it must edit one of
these reports blocked rather than widening scope.

## Decisions

- **D-1. Create-time fields live on a partial of `CreateAgentTaskRequest` as init-only
  properties, not new positional parameters.** `long? CeilingMinutes` and `string? CeilingReason`
  carry `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` and live in the new
  `AgentTaskDtos.Ceiling.cs`. Reasons:
  - The only edit to `AgentTaskDtos.cs` is `partial` on line 10. CARD-0824 S1 rewrites the
    positional tail at lines 222-223.
  - `long` lets `CeilingHoldFacts.ValidateInitial(long?, string?)`, already on master, return 422
    for `long.MaxValue`, instead of a 400 bind error from an `int` overflow.
  - `WhenWritingNull` keeps `AgentTaskService.HandoffRequestDigest`
    (`AgentTaskService.cs:1920`) byte-identical for every request that declares no ceiling. The
    digest serialises the whole request, so a plain new property would change every stored
    handoff digest. A caller replaying a handoff across the upgrade would then get
    `handoff_request_changed`.

  Rejected: a positional parameter appended after `FreshWorktree`, which textually conflicts with
  CARD-0824 S1 and changes every digest. Also rejected: a separate endpoint or a later PATCH, which
  would give a second write path to a dispatch-time snapshot.
- **D-2. Validation happens at create, before any side effect, and reuses S2's rule unchanged.**
  The rule is 1..720 minutes plus a nonblank reason of at most 1000 characters
  (`CeilingHoldFacts.ValidateInitial`). There is one addition: a reason without minutes is refused
  as 422 `ceilingReason`, because it is meaningless and would otherwise be stored silently. The
  check runs right after the ExpectedMinutes validation (`AgentTaskService.cs:408-414`), before
  quota, routing, gate or insert. That matters because the dispatcher calls `Resolve`, which
  calls `ValidateInitial`, at claim (`AgentTaskDispatcher.cs:6194/6648/8622/8881`). An invalid
  stored value would throw at dispatch, so create must never store one. The 720 cap, the reason
  bound and operator authority stay exactly as S2 implemented them. No redesign.
- **D-3. Binding and echoes.** A helper `CeilingHoldFacts.BindRequested(task, minutes, reason)`
  sets the two Requested columns. It is called after the `storedPolicy` block, not inside the
  object initializer that CARD-0824 S1 edits at `:1514`. `AgentTaskCreatedDto` gains the init
  property `RequestedCeilingMinutes` in the same partial file. It is set at both
  `AgentTaskCreatedDto` constructions (`:1609` handoff replay, `:1794` normal create). The detail
  DTO gains an init property `CeilingBudget` of type `CeilingBudgetDto(int? RequestedMinutes,
  string? RequestedReason, int? ResolvedMinutes, string? ResolvedSource, double?
  NextCeilingMinutes, int AbsoluteCapMinutes)`, which is always present. `BuildBrief` adds one
  line, only when `RequestedCeilingMinutes` is set, after the integration line: `Declared ceiling:
  N minutes (reason: ...). At it the task is held Working for a decision; nothing is killed.`
  Default briefs are byte-identical to master.
- **D-4. No inheritance.** Follow-up, child, merge/conflict and system-created tasks never inherit
  a parent's requested ceiling. Only an explicit create carries one. Retry is already correct on
  master (`AgentTaskService.cs:3933` `CeilingHoldFacts.Reset` keeps Requested and clears
  Resolved), and S4a only witnesses it through the create path.
- **D-5. The CLI validates locally before any POST.** It mirrors D-2 with `ceiling_invalid:` messages
  and exit 2, like the `-Runner` refusals. If the server's created response lacks a matching
  `requestedCeilingMinutes`, for example an older build that ignores unknown members, the CLI
  prints `WARNING: ceiling_unconfirmed: ...the role default applies; check GET /api/version` and
  still exits 0, because the task exists. The script stays ASCII-only and Windows PowerShell 5.1
  parseable: no `??`, ternary or `?.`.
- **D-6. The typed Reply is a separate branch of the existing `-Reply` set.** When `-CeilingAction`
  is bound:
  - `-Message` is refused locally.
  - `-EpisodeId` must be a non-empty GUID.
  - Continue refuses `-ExtendMinutes`.
  - Extend requires `-ExtendMinutes` (1..720, or 1..240 with `-OperatorExtend`).
  - `-OperatorExtend` requires Extend and a nonblank `-CeilingReason`.

  The body is exactly `{ceilingAction, episodeId[, extendMinutes][, operatorExtend][,
  ceilingReason]}`, with **no `origin`**, because `AnswerCeilingAsync` refuses any non-null origin,
  round, message, requestId or interrupt. Operator mode dot-sources the existing owner-only helper
  `scripts/lib/runner-operator-token.ps1` (`Get-RunnerOperatorToken`, honouring
  `ANTIPHON_OPERATOR_TOKEN_FILE`). It adds `X-Antiphon-Operator-Token` to a copy of `$headers` for
  that single request, and fails closed with `operator_token_unavailable:` and exit 2 before
  POST. The token value is never printed. The success line is `Decision recorded for task <id>
  (episode <e>): <action>. The session continues; -Status shows the next bound.` Ordinary Reply,
  Retry and Cancel gain no token requirement.
- **D-7. Status lines, in this order after `Delegate: <status>`:**
  - `Ceiling: N min (<source>); next bound X min; cap 720` when resolved. When not resolved:
    `Ceiling: N min requested; resolved at dispatch`, or `Ceiling: role default; resolved at
    dispatch`. Then `Ceiling reason: ...` when one is set.
  - With `ceilingHold`:
    - `Ceiling hold: episode <id>; held <at>; bound X min; last transcript <kind> <age>s`
    - `Phase: <kind> <limit>-minute limit, deadline <at>` or `Phase: none - no phase deadline covers this tail; the ceiling is its only deadline`
    - then either `Decide: delegate.ps1 -Reply <id> -CeilingAction Continue|Extend -EpisodeId <id> [-ExtendMinutes M]`,
    - or, when `capReached`, `Final hold at 720 minutes: Continue and Extend refuse. Operator Extend (-OperatorExtend -ExtendMinutes 1..240 -CeilingReason "...") or POST /api/agent-tasks/<id>/cancel`.
  - With `failedSession`: `Seat: <summary>; activity <activity>; session <id>; failure source
    <source>; notice <state|none>`. The summary is the server's own string: `Failed, session still
    Running: seat held` or `session state unknown: release unconfirmed`. Then, for seat held only:
    `Retry stops this session and spends a new attempt; Cancel does not apply to a Failed task.`

  The CLI never synthesises a "seat held" claim itself (round-3 D-3).
- **D-8. Client.** `CeilingHoldCard` renders only when `detail.ceilingHold` is present, as
  `data-testid="ceiling-hold-card"`. It shows:
  - the question, next bound and phase guidance (or the exact no-phase sentence);
  - Continue;
  - Extend, with a minutes input validated as an integer from 1 to 720;
  - Cancel, through the existing `useCancelAgentTask`.

  On a cap-reached hold, Continue and Extend are disabled and the card shows operator guidance
  without reading any credential. There is no client operator-Extend control.
  `useCeilingDecision` posts the typed body only, with no `message`/`origin`/`round`. Success
  shows `Decision recorded; session continues`. A 409 `ceiling_episode_stale` shows the server
  message and refetches. The card has no Retry, Answer or auto-continue control. The page-level
  Retry/Escalate/Cancel row stays as it is (D-3 of round 3: Retry is an existing verb with its
  consequences). `BlockedQuestionCard` is unchanged.
- **D-9. Test classes.**
  - `TaskCeilingScriptTests`: namespace `Antiphon.Tests.Scripts`; `[Category("Integration")]` and
    `[Category("Slow")]` as round-3 D-8 decided; `ParallelLimiter<ProcessSpawnLimit>`; FQN plus
    reason in `slow-tests-allowlist.txt`; listed in `ProcessSpawnLimitTests.StrictClasses`.
  - `TaskCeilingCreateBindingTests`: namespace `Antiphon.Tests.Application`, Integration, not
    Slow, no limiter (database only, no child process), and an isolated schema per test, as in
    `TaskPlatformPersistenceTests`'s `DefaultRunnerKit`.
  - The script stub is a private nested class in the test file. It records method, path, headers
    and body and serves a canned detail. It is not a shared helper, to keep the footprint small.
- **D-10. Ordering (collision rule).** S4a Code is cut with `-StartRef origin/master` only
  **after** CARD-0824 S1 has landed on master. See the next section. If the Code stage is
  commissioned earlier, it must report blocked rather than branch from a pre-0824 base. The
  task branch is fast-forward-only, so a later rebase is not available as a remedy.

Rejected for S4a: a client operator-Extend control, which would need a browser-held operator
credential; a new stop or release verb (round-3 D-3); auto-continue; any change to Cancel/Retry;
a migration; and the brief line for default ceilings, which would churn every brief corpus.

## Ground truth (card / round-3 plan vs master at 10c8c2471)

| Assumption | Master truth | Consequence for S4a |
|---|---|---|
| S4 only needs "bindings" (round-3 S4 row) | `CreateAgentTaskRequest` has no ceiling field, and nothing writes `RequestedCeiling*` | Add the field, validation, bind and echo (D-1..D-3) |
| A ceiling field can be appended after `FreshWorktree` (investigation's fix idea) | CARD-0824 S1 appends `WorkspaceColdReset` there, and `HandoffRequestDigest` serialises the whole request | Partial init property with `WhenWritingNull` (D-1) |
| Retry must be taught to keep an explicit ceiling | `RequeueCoreAsync` → `CeilingHoldFacts.Reset` already keeps Requested and clears Resolved; `C1610_Retry_resets_hold_but_preserves_requested_ceiling` pins it | Witness through create only (V-18) |
| The CLI typed reply can reuse the ordinary body | `AnswerCeilingAsync` refuses `origin`, `message`, `round`, `requestId` and interrupt with a typed action | Separate body without `origin` (D-6) |
| Operator Extend is authorised by a flag | The endpoint requires `OperatorCredential.Require` and passes `CeilingOperatorAuthorization`; `Rearm` refuses unauthenticated operator Extend | CLI sends the header from the owner-only helper; fails closed (D-6) |
| The client hold card hides Retry | `TaskDetailBody` always renders a page-level Retry for non-Queued tasks | Assertions are scoped `within` the card; page Retry stays (D-8) |
| V-13 holds 11 cases in one class | 5 of them are S4a (status, budget, reply) and 6 are S4b drain | S4a re-specifies its own 16 script cases; drain cases go to S4b |
| `-Status` needs a new server read | `GET /api/agent-tasks/{id}` already returns `failedSession` and `ceilingHold` | Only `ceilingBudget` is new (D-3) |
| An operator token is read by `delegate.ps1` today | No: only `runner-slots.ps1` and similar dot-source `lib/runner-operator-token.ps1` | Dot-source in the operator branch only |
| A migration is needed | The columns exist in `20261030114738` | Forbidden; the CP-14 drift row guards it |

## Interactions with in-flight work (CARD-0824 S1)

CARD-0824 S1 Code `00abf1bf` (branch `feat/card-task-00abf1bf`, tip `41e63343b6449c6a97ee7c8df3bfe52da0354ea9`,
merge base `ead93feb00fcaf8bf31018599437f17bee8e934c`) edits these S4a-adjacent lines:

| File | CARD-0824 S1 hunk | S4a edit | Overlap |
|---|---|---|---|
| `server/Application/Dtos/AgentTaskDtos.cs` | `:222-225`: `FreshWorktree = false);` becomes `FreshWorktree = false,` plus `WorkspaceColdReset = false);` | `partial` keyword at `:10` (`CreateAgentTaskRequest`), `:420` (`AgentTaskDetailDto`) and `:615` (`AgentTaskCreatedDto`); everything else is in the new `AgentTaskDtos.Ceiling.cs` | none (over 190 lines apart) |
| `server/Application/Services/AgentTaskService.cs` | `:1514-1517`: adds `WorkspaceColdReset = request.WorkspaceColdReset,` before the initializer's `};` | validation at `:414` (after ExpectedMinutes); `CeilingHoldFacts.BindRequested(...)` after the `storedPolicy` block (`:1519-1524`, at least 5 unchanged lines below the 0824 hunk); created echo at `:1609` and `:1794`; detail at `:2910` | none if placement is kept; **do not** put the bind inside the initializer |
| `scripts/delegate.ps1` | `:95-101` `-ColdWorkspace` param; `:1088` body line | params after `-ExpectAbout` (`:189`) and in the Reply set (`:283`); body after `expectedMinutes` (`:1171`); echo after the title-pending line (`:1258`); Reply branch (`:884`); Status block (`:680-781`) | none |
| `server/Domain/Entities/AgentTask.cs`, `server/Migrations/*` (model snapshot) | slot columns at `:270-285`; migration `20261102070901` | not touched (no migration) | none |
| `docs/ops-http.md` | new section at `:395` before `## Shapes that bite` | a new section appended at end of file | none |
| `docs/docker-stack.md`, `docs/session-runtime-invariants.md` | +15 / +35 lines | not touched (S4b owns docker-stack) | none |

The table above is the textual minimisation. **Two rules remain:**

1. **Rebase-first (D-10).** S4a Code starts from an origin/master that already contains CARD-0824
   S1. Check it with `git log origin/master --oneline --grep "card-0824"`, which should show the
   S1 server and runner commits, or the land's rebased equivalents matched by subject. A
   sequencing slip then cannot reach Review as a textual conflict.
2. **The V-19 digest pin is computed at that base.** CARD-0824's non-nullable
   `WorkspaceColdReset` already changes every `HandoffRequestDigest` once it lands, because a
   non-null bool is always serialised. S4a's witness pins the digest of a no-ceiling request at
   the S4a Code base. It records the hex in the red commit, computed from the base build before
   the DTO edit. The witness proves S4a adds no further change. (The 0824 digest shift is an
   observation for CARD-0824, outside this card.)

CARD-1624 (Review 253f7e9d) and the CARD-1514 landing fence (Review bd1e86ea) touch none of these
files, per investigation a45f88c8. The in-flight reconciler-fix branch is still unidentified. Code
re-runs `git merge-tree --write-tree origin/master HEAD` before reporting Landable.

## Slices

One Code slice with three committed groups:

1. **Red commit(s), group `S4a-red`.** A compatible scaffold first: the DTO partial file with
   properties the service ignores, `CeilingBudgetDto` with null values, the client types, and an
   empty `CeilingHoldCard` that renders nothing. Old behaviour is retained, so the witnesses
   compile but fail their oracle. Then the three witness files and the allowlist/roster entries.
   Compile errors are never red.
2. **Green commit(s), group `S4a`.** The server binding, validation and echoes (D-1..D-4);
   `delegate.ps1` (D-5..D-7); the client card (D-8); and `docs/ops-http.md`.
3. **Report commit.** No source edit while a checkpoint run is in flight.

## Witnesses for TestDesign

Each witness names the mutant that must turn it red. Positive controls belong to a later
method-scoped SourceLanding Mutation stage.

| ID | Class.Method (Arguments) | Oracle | Mutant that must fail it |
|---|---|---|---|
| V-13a | TaskCeilingScriptTests.C1610_Status (seat-held, unknown, ceiling-hold, final-hold, budget) = 5 | The exact D-7 lines from a canned detail. Seat-held prints the server summary and the Retry consequence. Unknown prints `session state unknown: release unconfirmed` and never `seat held`. Ceiling-hold prints the episode and a Continue/Extend `Decide:` line. Final-hold prints no Continue hint and does print the operator/cancel guidance. Budget prints requested/resolved/source/next | drop the Seat line; hard-code "seat held" for any failedSession; print the Continue hint on capReached; drop the Ceiling line |
| V-13b | TaskCeilingScriptTests.C1610_Declared_budget (valid, zero, over-cap, reasonless, reason-only, unconfirmed) = 6 | Valid posts `ceilingMinutes: 600` plus the reason and echoes `ceiling: 600 min requested`. Each invalid arm exits nonzero with `ceiling_invalid:` and the stub saw 0 requests. Unconfirmed: the stub response lacks `requestedCeilingMinutes`, output has `WARNING: ceiling_unconfirmed`, exit 0 | remove the local check (a request reaches the stub); remove the reason-only check; skip the response comparison |
| V-13c | TaskCeilingScriptTests.C1610_Ceiling_reply (continue, extend, message-exclusive, operator-extend, operator-no-token) = 5 | Body keys are exactly as in D-6, with no `origin`/`message`. Message-exclusive is refused with 0 requests. Operator-extend: the header equals the temp-file token, the body has `operatorExtend: true` and the reason, and the output never contains the token. Operator-no-token: `operator_token_unavailable`, 0 requests, nonzero exit | send `origin: 'Cli'` on the typed path; omit the operator header; echo the header value; POST when the token file is missing |
| V-15 | TaskCeilingCreateBindingTests.C1610_Create_binds_requested_ceiling (six-hundred, one, cap) = 3 | The stored row has Requested minutes and reason; the created DTO `RequestedCeilingMinutes` matches | drop `BindRequested`; drop the created-DTO echo |
| V-16 | TaskCeilingCreateBindingTests.C1610_Create_refuses_invalid_ceiling (zero, negative, over-cap, overflow, reasonless, reason-only, long-reason) = 7 | `ValidationException` (422) naming `ceilingMinutes`/`ceilingReason`; AgentTasks count unchanged; no raw token issued | validate after insert; drop the reason-only rule; clip an over-cap value instead of refusing |
| V-17 | TaskCeilingCreateBindingTests.C1610_Requested_ceiling_resolves_at_dispatch = 1 | The created row, then `CeilingHoldFacts.Resolve`, gives 600, source `requested`, next 600 | bind minutes without the reason, so Resolve throws |
| V-18 | TaskCeilingCreateBindingTests.C1610_Retry_keeps_explicit_and_reresolves_default (explicit, default) = 2 | Explicit keeps Requested and clears Resolved. Default: after the role default changes, the re-resolved value is the new default | `Reset` clears Requested; Retry keeps Resolved |
| V-19 | TaskCeilingCreateBindingTests.C1610_Absent_ceiling_keeps_handoff_digest = 1 | `HandoffRequestDigest` of a fixed no-ceiling request equals the hex pinned at the S4a Code base. A request with a ceiling has a different digest | remove `WhenWritingNull` |
| V-20 | TaskCeilingCreateBindingTests.C1610_Detail_and_brief_echo_budget (detail, brief) = 2 | Detail `CeilingBudget` fields. The brief contains `Declared ceiling: 600 minutes` when requested and no `Declared ceiling` when default | emit the brief line for every task; omit `CeilingBudget` |
| V-14a | CeilingHold.test.tsx "Continue sends a typed decision without an ordinary reply" | The POST body equals `{ceilingAction:'Continue', episodeId}`; the notification text matches; within the card there is no Retry/Answer/auto-continue | route through `useReplyToAgentTask` (adds message/origin) |
| V-14b | CeilingHold.test.tsx "Extend validates minutes and reports a stale episode" | 0 sends no request; 60 posts `extendMinutes: 60`; a 409 `ceiling_episode_stale` shows the server text | drop the minimum validation; swallow the 409 |
| V-14c | CeilingHold.test.tsx "Final hold offers only Cancel and operator guidance" | capReached disables Continue and Extend and shows the operator guidance; Cancel posts `/cancel` and never `/reply` | leave Continue enabled on capReached |

Counts: TaskCeilingScriptTests is **16** cases, TaskCeilingCreateBindingTests is **16**, and
CeilingHold.test.tsx is **3**. The S4a cases in V-13 grow from the round-3 count of 5 to 16
because each refusal becomes its own argument, so a mutant can turn exactly one case red.
TestDesign may rename methods. Changing a count means revising the checkpoint row Min and Expect
in the same commit.

Red expectations (CP-1..CP-3), with the scaffold (server and client types only, `delegate.ps1`
unchanged):
- Every V-13 case is red. The old script has no ceiling parameters, so the invalid arms fail with a
  PowerShell binding error, and the oracle requires the `ceiling_invalid:` or
  `operator_token_unavailable:` text, not just a nonzero exit.
- V-15, V-16, V-17, V-18 `explicit` and V-20 are red. The scaffold accepts the properties but
  never binds or validates them, so every create succeeds.
- Two cases are expected guard greens, recorded as such: V-18 `default`, because master's Retry
  already re-resolves, and V-19, because the scaffold already carries `WhenWritingNull`.
- V-14a..V-14c are red against the empty card.

Record per-case outcomes in the red receipt rather than requiring every case to fail. A guard
green is never counted as a new red.

## Performance budget

Apply [docs/testing-and-build.md#performance-standards](../../testing-and-build.md#performance-standards)
and `docs/test-performance-standards.md`. This Plan ran no product build or test; the only tool
run is `checkpoint import --plan`.

- **Selection.** Only the closed table below. There is no Full Unit sweep and no
  `AgentTaskReplyIntegrationTests` (341 cases, about 25 min), because S4a does not touch the
  reply service. `DelegationDispatchRecoveryBoundaryTests` (382 cases, about 25 min) is reduced
  to its two brief methods (55 cases), because only `BuildBrief` changes.
- **Builds.** One `tests/Antiphon.Tests` build per After group (red, green), each in its own
  output. Build time and slot admission are reported separately from row time. Admission is
  bounded at 45 min (import default). Client rows need no .NET build. `test-client.ps1`
  installs client dependencies once under `logs/client-npm-ci.lock`.
- **Per-test costs (provisional; no fresh sample on this base).**
  - A delegate.ps1 pwsh run is about 1-3 s on server2. `DelegateScriptRunner` has a 60 s
    per-run timeout, which TestDesign should tighten to about 2x the p95 measured in the red run.
  - `DelegateScriptKindTests` was estimated at 6 min for 46 cases (CARD-0824 S1 CP-20).
  - Isolated-schema integration cases take about 2-5 s each.
  - Vitest files take about 15 s each after install.

  These are estimates, not measured p95. The red run collects the first bounded sample, and Code
  records per-row wall time from the checkpoint phases.
- **Shared setup.** The PostgreSQL template is assembly-owned and shared once per run; each
  integration test takes its own isolated schema. The pwsh witnesses share nothing: each spawns
  one pwsh against its own loopback stub, serialised by the assembly-local `ProcessSpawnLimit`.
  No container start, migration roundtrip or external call is made, apart from loopback stubs.
- **Phases recorded.** Admission, build, host startup, migration (template), body and teardown,
  per the checkpoint tool's phase timings, with TRX per row.
- **Run sizes.**
  - Red run: about 8 min of rows plus build.
  - Green run: about 39 min of row time if serial, which is **over 30**. Run it with
    `--parallel 2`, two isolated test hosts, each with its own schemas and its own assembly
    limiter. Expected wall time is 22 min or less.
  - If measured throughput forecasts more than 30 min, stop and open a Debug task with the phase
    timings (as in the S2/S3 fix-3 precedent). Do not widen any row timeout.
  - The client rows (CP-3, CP-15, CP-16) are command rows and share no .NET build.
- **Timeouts.** Import defaults are 45/15/90 min (slot/row/total). Every row estimate is at most
  10 min, so no row exceeds the 15-minute row default. No timeout is widened.

## Sessions waiting for input

S4a adds no session that waits for input and changes no release rule. A ceiling hold remains
**Working** and keeps its seat. While parking is disabled there is no automatic release deadline
(CARD-1083), and a hold is never parked. It is released only by an explicit Cancel, by Retry
(which stops the old attempt), by a normal report settlement, or by an existing qualified
release. The new CLI and client controls only record Continue or Extend decisions, or call the
existing Cancel. Nothing auto-kills a Working session (CARD-0079 remains the only automatic stop).

## Platform

Read on 2026-11-02:
- `GET /api/runner-defaults`: revision 2, global default `server2`, set by a human.
- `GET /api/session-runners`: `desktop` (windows), `server2` (linux), `server2-temp` (linux).

Every S4a row is portable and Linux-runnable. There are no E2E, Playwright, headed or
native-Windows rows (server2 has no Chromium). Dispatch with live defaults: omit `-Runner` unless
pinning, and omit `-Platform`. Each checkpoint names its logical lane in **Group**.

### Checkpoints

This closed S4a table supersedes round-3 CP-13, CP-14, CP-18 and CP-20 for S4a. Round-3 CP-21..CP-105
and the fix tables are **not** re-run. Rows are numbered locally (CP-1..CP-16) under their own
After groups `S4a-red` and `S4a`. Combined class filters use `(Name*)` operands with escaped pipes. The
suffix census at the base found no class or method sharing a prefix with any operand. The two new
classes do not exist yet, and no existing class starts with `TaskCeilingScriptTests` or
`TaskCeilingCreateBindingTests`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S4a-red | `tests/Antiphon.Tests -> bin-c1610-s4a-red/` | script-integration-ceiling-red | `/*/*/TaskCeilingScriptTests/C1610_*` | V-13a..V-13c | 16 executed; intended script assertions red per the red-expectation list; no skipped | 16 | 3 |
| CP-2 | S4a-red | CP-1 | server-integration-create-binding-red | `/*/*/TaskCeilingCreateBindingTests/C1610_*` | V-15..V-20 | 16 executed; intended reds, V-18 default and V-19 guard green; no skipped | 16 | 3 |
| CP-3 | S4a-red | n/a | client-Vitest-red | `pwsh -NoProfile -File scripts/test-client.ps1 CeilingHold.test.tsx -JsonResultPath ../.antiphon/c1610-s4a-client-red.json` | V-14a..V-14c | JSON numTotalTests=3 with the three exact names; intended behavioral red; no setup error, pending or todo | n/a | 2 |
| CP-4 | S4a | `tests/Antiphon.Tests -> bin-c1610-s4a/` | script-integration-ceiling | `/*/*/TaskCeilingScriptTests/C1610_*` | V-13a..V-13c | 16 passed; no failed/skipped | 16 | 3 |
| CP-5 | S4a | CP-4 | server-integration-create-binding | `/*/*/TaskCeilingCreateBindingTests/C1610_*` | V-15..V-20 | 16 passed; no failed/skipped | 16 | 3 |
| CP-6 | S4a | CP-4 | server-unit-ceiling-policy | `/*/*/TaskCeilingPolicyTests/*` | full affected class (ValidateInitial reuse) | 12 passed; no failed/skipped | 12 | 1 |
| CP-7 | S4a | CP-4 | server-integration-ceiling-hold-control | `/*/*/(TaskCeilingHoldTests*)\|(TaskCeilingControlTests*)/*` | full affected classes (hold, typed reply, retry) | 49 passed; no failed/skipped | 49 | 4 |
| CP-8 | S4a | CP-4 | script-integration-delegate-contracts | `/*/*/(DelegateScriptKindTests*)\|(DelegateScriptStartRefTests*)\|(DelegateScriptTitleTests*)\|(DelegateScriptWorkspaceDefaultTests*)\|(DelegateScriptBindDiagnosisTests*)/*` | delegate.ps1 Create/Invoke-Antiphon contracts | 79 passed; no failed/skipped | 79 | 10 |
| CP-9 | S4a | CP-4 | server-unit-guards | `/*/*/(DelegateScriptRunnerSwitchTests*)\|(TestLaneCategoryGuardTests*)\|(TestClassificationGuardTests*)\|(ProcessSpawnLimitTests*)/*` | lane, Slow registry and limiter roster guards plus the Runner block contract | 26 passed; no failed/skipped | 26 | 1 |
| CP-10 | S4a | CP-4 | script-integration-status-reply-compat | `/*/*/DelegateScriptLandCompatibilityTests/(C495_StatusDoesNotProbeVersion*)\|(C495_ReplyDoesNotProbeVersion*)` | Status and Reply blocks still skip the land-v2 probe | 2 passed; no failed/skipped | 2 | 2 |
| CP-11 | S4a | CP-4 | server-integration-brief-corpus | `/*/*/DelegationDispatchRecoveryBoundaryTests/(C1150_Brief_envelope_corpus_matches_the_expected_matrix*)\|(C1150_Outer_envelope_and_receipt_floor_through_the_queue*)` | default briefs byte-identical | 55 passed; no failed/skipped | 55 | 4 |
| CP-12 | S4a | CP-4 | server-integration-verification-brief | `/*/*/VerificationRoundBriefTests/*` | brief profile block unchanged | 1 passed; no failed/skipped | 1 | 1 |
| CP-13 | S4a | CP-4 | server-integration-detail-handoff | `/*/*/(AgentTaskDetailBlockedContextTests*)\|(PipelineHandoffPilotTests*)/*` | detail DTO and handoff digest/replay | 22 passed; no failed/skipped | 22 | 4 |
| CP-14 | S4a | CP-4 | server-integration-model-drift | `/*/*/BlockedTaskParkStateTests/C1065_EpisodeIdentitySurvivesRestart` | no pending model change (no migration) | 1 passed; no failed/skipped | 1 | 2 |
| CP-15 | S4a | n/a | client-Vitest | `pwsh -NoProfile -File scripts/test-client.ps1 -TypecheckProject client/tsconfig.app.json -LintPath client/src/features/delegations/CeilingHoldCard.tsx CeilingHold.test.tsx -JsonResultPath ../.antiphon/c1610-s4a-client-green.json` | V-14a..V-14c plus app typecheck and card lint | typecheck and lint pass; fresh JSON numTotalTests=3, numPassedTests=3, failed/pending/todo=0; all names; exit 0 | n/a | 3 |
| CP-16 | S4a | n/a | client-Vitest-detail-regression | `pwsh -NoProfile -File scripts/test-client.ps1 TaskDetailBody.test.tsx BlockedQuestionCard.test.tsx -JsonResultPath ../.antiphon/c1610-s4a-client-detail.json` | TaskDetailBody and BlockedQuestionCard unchanged behaviour | fresh JSON numTotalTests>=12, failed/pending/todo=0; exit 0 | n/a | 2 |

Expected executed counts are a syntax census at `10c8c2471`, with Arguments expanded:
- TaskCeilingPolicyTests 12; TaskCeilingHoldTests 25; TaskCeilingControlTests 24.
- DelegateScriptKindTests 46; DelegateScriptStartRefTests 11; DelegateScriptTitleTests 10;
  DelegateScriptWorkspaceDefaultTests 6; DelegateScriptBindDiagnosisTests 6;
  DelegateScriptRunnerSwitchTests 3.
- TestLaneCategoryGuardTests 1; TestClassificationGuardTests 1; ProcessSpawnLimitTests 21.
- The two C1150 brief methods 48 + 7; VerificationRoundBriefTests 1.
- AgentTaskDetailBlockedContextTests 10; PipelineHandoffPilotTests 12.

Code reconciles these floors against a fresh TRX at its actual base, after CARD-0824 S1. A floor
that changes because of inherited master growth is amended, with the reason given, in the
report commit. It is never silently lowered.

## Risks

- **Base drift after CARD-0824 S1.** Line numbers above refer to `10c8c2471`. Code re-locates by
  anchor text, as in the D-10 rule.
- **Old server builds ignore unknown members.** A CLI on a newer build talking to an older server
  would silently drop `ceilingMinutes`. D-5 makes that visible as `ceiling_unconfirmed`.
- **Handoff digest.** D-1 and V-19 protect S4a. The CARD-0824 digest shift is noted for that card.
- **Linux pwsh environment.** `Get-RunnerOperatorToken` falls back to `$env:LOCALAPPDATA`, which
  is unset on Linux. The witnesses always set `ANTIPHON_OPERATOR_TOKEN_FILE`, and the
  missing-path branch must fail closed rather than throw an unhandled `Join-Path` error.
- **Load flakes.** Pwsh spawn witnesses on a loaded server2 can approach the 60 s runner timeout.
  Run CP-4 and CP-8 in the same process under the assembly limiter; never co-schedule with Pty
  tests.
- **Client JSON path.** `-JsonResultPath` is relative to `client/` (round-3 precedent). The
  `.antiphon/` evidence stays gitignored.

## Stages after this plan

1. **TestDesign** (S4a): finalise the method names and cases above, measure the pwsh witness
   p95 in the red run to set its timeout, and commit the red witnesses plus the compatible
   scaffold.
2. **Code** (S4a), only after CARD-0824 S1 lands (D-10): green commits, the closed table above,
   `scripts/check-evidence-diff.ps1`, and a Landable `merge-tree` check.
3. **Review** (ordinary, Final round for S4a).
4. **Mutation** (SourceLanding, post-land, method-scoped): one positive control per mutant column above.

S4b is on its own follow-up card, gated by CARD-1615 Code under round-3 D-6.

## Decisions for the human

Each item has a default that this plan already applies.

1. **H-1. S4b waits for CARD-1615 Code and moves to a follow-up card.** Default: yes (applied by
   the orchestrator 2026-11-02). CARD-1610 can then close after S4a lands.
2. **H-2. Card Wanted items 1, 3 and 4 are superseded by the reviewed plan.** Default: yes
   (applied).
   - Item 1: Retry already stops the old session (D-3).
   - Item 3: the Working hold replaced a progress-aware ceiling (D-1/D-6).
   - Item 4: S3 forwards a triage note instead of settling (D-5).
3. **H-3. No client operator-Extend control.** Default: CLI only, because it needs the operator
   token. The client shows guidance.
4. **H-4. An unconfirmed ceiling warns and exits 0.** The alternative is exit 3 with the task
   already created. Default: warn, exit 0.
