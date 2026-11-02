# CARD-1610 S4a: test design (create-time ceiling, CLI seat-held and typed reply, client hold card)

TestDesign task a3a942ab, 2026-11-02, server2/Linux. Base: plan branch `feat/card-task-5073e215` tip
`a7fa13954056f65f3d950d48cd4e9de9c1f2a3d1` (master `10c8c2471` plus the S4a plan). This companion adds
the verification design for the
[S4a plan](2026-11-02-card-1610-s4a-cli-client-plan.md) ("the plan"). It does not change the plan's
fix design (D-1..D-10), with two exceptions. Both are flagged as open questions with defaults:
Q-1 refuses control characters in a ceiling reason, and Q-2 makes `-CeilingMinutes` a string the CLI
parses itself. Where this document and the plan disagree on a case list, count, row or filter, this
document wins for S4a. Inputs: the plan, the round-3 plan, the S2/S3 fix plan, investigation
a45f88c8, the S1-S3 code and tests on master, and the 0824-S1 and 1545-S2 test designs (for format).

No build or test ran in this task. The only tool run was the checkpoint tool's `import --plan` on
this file (see Checkpoints).

## Collision rule with CARD-0824 S1 (first step of the Code brief)

On 2026-11-02, `git log origin/master --oneline --grep card-0824` shows **no** S1 commit. Master is
still `10c8c2471`, and CARD-0824 S1 Code `00abf1bf` is in flight. So S4a Code must not start yet.

**Code step 0 (mandatory; report `blocked` if it fails):**

```text
git fetch origin master
git log origin/master --oneline --grep "card-0824" -i      # must list the S1 server + runner commits (match by subject; the land rebases)
git merge-base --is-ancestor <S4a start sha> origin/master  # the task was cut with -StartRef origin/master after that land
```

If S1 is absent, or the task branch was cut before it, end with
`[antiphon-report:<id> blocked]` and the line `CARD-0824 S1 not in base`. Do not rebase. The branch is
fast-forward-only (plan D-10).

**Files to re-check at Code time.** Find each edit by its anchor text, never by line number. At the
new base, confirm that every file below either still matches its `10c8c2471` shape around the anchor
or carries only the CARD-0824 S1 hunk listed in the plan's interaction table.

| File | Anchor S4a edits beside | Check |
|---|---|---|
| `server/Application/Dtos/AgentTaskDtos.cs` | `public sealed record CreateAgentTaskRequest(`, `AgentTaskDetailDto(`, `AgentTaskCreatedDto(` | only `partial` is added (R-11); the `WorkspaceColdReset` tail from 0824 is present and untouched |
| `server/Application/Services/AgentTaskService.cs` | `It is a hint that schedules the first check-in`; `if (storedPolicy is not null)`; both `new AgentTaskCreatedDto(`; `CeilingHold: await CeilingHoldFacts.DescribeAsync` | the bind call stays outside the object initializer that 0824 edits |
| `server/Application/Services/CeilingHoldFacts.cs` | `ValidateInitial`, `Reset` | unchanged by 0824 |
| `server/Application/Services/DelegationReportFormatter.cs` | `"integration: not requested"` | unchanged by 0824 |
| `scripts/delegate.ps1` | `[int]$ExpectAbout,`; `'Reply' {`; `'Status' {`; `$body['expectedMinutes']`; `title: pending` | 0824's `-ColdWorkspace` param and body line are present |
| `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs`, `slow-tests-allowlist.txt` | `StrictClasses` | 0824 may add its own entries; append, never reorder |
| `docs/ops-http.md` | end of file | 0824 adds a section before `## Shapes that bite`; S4a appends at the end |
| `client/src/api/agentTasks.ts`, `TaskDetailBody.tsx` | `failedSession?:`, `{detail.blocked && (` | not touched by 0824 |

Then run `git merge-tree --write-tree origin/master HEAD` before every push that claims Landable.

## Verification design

### Inspection

- Plan, all sections. Boundaries map to V-13a..V-21 and R-1..R-12 below. The plan's 16+16+3
  cases are re-cut as 33+22+7 (see Counts).
- `server/Application/Services/CeilingHoldFacts.cs` (113 lines). `ValidateInitial(long?, string?)`
  throws `ValidationException("ceilingMinutes", ...)` for a value outside 1..720, a null or
  whitespace reason, or a reason over 1000 characters. It does **not** refuse control characters,
  and it returns `(int)minutes` only after the range check. `Resolve` calls `ValidateInitial`
  first, then sets Resolved/Source/Next (`requested`, `rolePolicy` or `default`). `Reset` clears
  episode, Resolved, Source, Next and OperatorExtended, and keeps Requested. `Rearm` handles
  operator Extend (1..240 plus a reason, `operator_token_required` unless authenticated) and the
  cap (`ceiling_cap_reached` at 720 elapsed). `DescribeAsync` builds the `CeilingHoldDto`, including
  the exact no-phase sentence and `CapReached`.
- `server/Application/Dtos/AgentTaskDtos.cs`. `CreateAgentTaskRequest` is positional (`:10`, tail
  `FreshWorktree = false);` at `:223`). `AgentTaskDetailDto` carries `FailedSession` and
  `CeilingHold` (`:490-491`). The `CeilingHoldDto` fields are `:498-501`. `ReplyToAgentTaskRequest`
  has `CeilingAction`, `EpisodeId`, `int? ExtendMinutes`, `OperatorExtend` and `CeilingReason`.
  `ExtendMinutes` is an `int`, so a JSON number outside int range is a 400 bind error and the CLI
  must refuse it locally.
- `AgentTaskService.cs`. `HandoffRequestDigest` (`:1920`) serialises the whole request with
  **default** `JsonSerializer` options (PascalCase names, null values written), sorts the
  properties and takes SHA-256. `WhenWritingNull` on an init property therefore omits that key
  completely. The `ExpectedMinutes` check is at `:408-414`, the `storedPolicy` block at `:1519`, the
  handoff replay created DTO at `:1609`, the normal created DTO at `:1794`, the detail at `:2909-2910`
  and the Retry reset at `:3933`. Raw tokens are issued at `RawTokens[id] = token` just before the
  `:1794` return.
- `AgentTaskReplyService.CeilingHold.cs` `AnswerCeilingAsync`. Any `Message`, `Round`, `Origin`,
  `InterruptCurrentTurn` or `RequestId` refuses 422 `ceilingAction`. The method writes a decision
  event, publishes, and changes **no queue input** (the comment says so). This fixes the shape of
  the CLI and client bodies.
- `scripts/delegate.ps1` (1354 lines). The `$headers` map is script scope, with `X-Antiphon-Task-Token`
  from `ANTIPHON_TASK_TOKEN` (`:383-421`). `Invoke-Antiphon` (`:579`) surfaces the server
  `ErrorDetails` and has no per-call header parameter. The Status block is `:680-781`. The Reply
  block (`:884-895`) posts `{message, origin:'Cli'}`. `$ExpectAbout` is `[int]`, body at `:1171`. The
  echo block includes `title: pending` (`:1258`). The `-Runner` refusals use `exit 2`.
  `scripts/lib/runner-operator-token.ps1` **throws** on a missing or empty file, and its message
  includes the path, never the value. With `ANTIPHON_OPERATOR_TOKEN_FILE` unset on Linux it calls
  `Join-Path $null`, which throws too.
- Test fixtures read:
  - `tests/Antiphon.Tests/Application/DelegateScriptRunner.cs`: real `pwsh -NoProfile
    -NonInteractive -File`, `ANTIPHON_API`, `ANTIPHON_TASK_TOKEN` empty unless overridden, and a
    hard 60 s timeout shared by about 20 classes.
  - `TestHelpers/DelegateCreateStubApi.cs`: an `HttpListener` on `EphemeralHttpListener.BindLoopback`
    that answers everything with one JSON and records `LastBody` and `RequestCount`.
  - `Scripts/CommitOnSettleScriptTests.cs`: the nearest fixture. `[Category("Integration")]`,
    `[ParallelLimiter<ProcessSpawnLimit>]`, `RequestCount.ShouldBe(0)` for local refusals, and the
    ASCII-only check `Commit_on_settle_scripts_are_ascii_only` over `delegate.ps1`.
  - `Application/DefaultRunnerCreateTests.cs` `DefaultRunnerKit`: `Service(db)` over a real
    `AgentTaskService` with a `RecordingSessionStopper`, a mutable `Settings` (`RolePolicy`),
    `Caller`, `ReadAsync` and `TaskCountAsync`. `TaskPlatformPersistenceTests` uses it with an
    isolated schema.
  - `TestHelpers/TaskCeilingHarness.cs` and `TaskCeilingControlTests.C1610_Retry_resets_hold_but_preserves_requested_ceiling`
    seed Requested by mutation, never through create. This is the gap V-15..V-18 close.
  - `TaskCeilingPolicyTests` (12 cases), which already pins `ValidateInitial` for default, one, cap,
    zero, negative, over-cap, overflow and reasonless.
- Client:
  - `client/src/api/agentTasks.ts`: `failedSession?` is typed; `ceilingHold` is not.
    `useReplyToAgentTask` always adds `origin`, so it cannot carry a typed decision.
    `useCancelAgentTask` posts `/cancel`.
  - `TaskDetailBody.tsx`: `BlockedQuestionCard` renders only on `detail.blocked`; the page-level
    Retry is at `:558-562`.
  - `BlockedQuestionCard.test.tsx`: the msw `server`, `renderWithProviders` and the mocked
    `@mantine/notifications`. This is the nearest fixture for the new test.
  - Counts: `TaskDetailBody.test.tsx` has 6 tests (4 plus a 2-case `it.each`) and
    `BlockedQuestionCard.test.tsx` has 6, so 12 in total.
  - `scripts/test-client.ps1` takes `-JsonResultPath`, `-TypecheckProject` and `-LintPath`.
- `docs/testing-and-build.md` (checkpoint manifest, `--rows`, two-row overlap on Linux, build
  slots), `docs/test-performance-standards.md`, and AGENTS.md "Tests and builds".

### Delivery inventory

S4a adds **no new asynchronous delivery path**. It has three seams:

1. **Create → stored row → dispatch-time Resolve.** This is synchronous persistence. The durable
   identity is `AgentTask.Id`. Its receipt is the stored `RequestedCeiling*` columns and the
   created DTO echo (V-15). Its recovery is the existing dispatcher claim, which calls
   `CeilingHoldFacts.Resolve`. V-17 proves that the stored value resolves without throwing, which
   is why create must never store an invalid pair (D-2).
2. **Brief content.** The brief is session input on the **existing** dispatch path, which S4a does
   not change. S4a adds one line, for requested tasks only. The queue-level, producer-to-recipient
   witness of that path is `C1150_Outer_envelope_and_receipt_floor_through_the_queue` (CP-11), and
   it runs for default briefs. Declared substitute: V-20 `brief-requested` checks the `BuildBrief`
   text, not delivery. It cannot prove the new line reaches the session's UserPrompt. That holds by
   construction, because the brief is one string through an unchanged envelope, and the
   UserPrompt-transcript proof stays with CP-11 (default) and native runs (out of scope). Control
   characters are refused at create (G-9, G-26), so a reason cannot add or forge brief lines.
3. **Typed ceiling decision.** The CLI or client POST goes to `AnswerCeilingAsync`, a synchronous
   database write plus a publish. It sends no session input ("No queue input ... is changed"). So
   no UserPrompt is expected, and none may be claimed. The receipt is the 200 summary and the
   decision event, already witnessed by `TaskCeilingControlTests` (CP-7). The CLI and client
   witnesses prove only the request body, the header and the refusal handling.

None of these is a delivery claim, so no busy-recipient or enqueue-failure case applies.

### Fixtures and conventions

**Files.** Code may not edit any test file outside this set. The exceptions are the roster and
allowlist lines listed here.

| Witness | File | Shape |
|---|---|---|
| V-13a..c | **new** `tests/Antiphon.Tests/Scripts/TaskCeilingScriptTests.cs` | `namespace Antiphon.Tests.Scripts`; `[Category("Integration")]`, `[Category("Slow")]`, `[ParallelLimiter<ProcessSpawnLimit>]`; 3 methods with `[Arguments(string arm)]` |
| V-15..V-21 | **new** `tests/Antiphon.Tests/Application/TaskCeilingCreateBindingTests.cs` | `namespace Antiphon.Tests.Application`; `[Category("Integration")]`; no limiter; each case `await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync()` plus `DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null)` |
| V-14a..e | **new** `client/src/features/delegations/CeilingHold.test.tsx` | msw `server.use(http.post(...))`, `renderWithProviders(<CeilingHoldCard detail=... />)`, `vi.mock('@mantine/notifications')` as in `BlockedQuestionCard.test.tsx`; assertions `within(screen.getByTestId('ceiling-hold-card'))` |
| roster | `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs` | one `typeof(TaskCeilingScriptTests)` in `StrictClasses` (a loop, not a data source, so the CP-9 count stays the same) |
| allowlist | `tests/Antiphon.Tests/slow-tests-allowlist.txt` | `Antiphon.Tests.Scripts.TaskCeilingScriptTests` with reason `spawns pwsh per case (33 cases, about 4 min)` |

**Script stub.** This is a private nested class `CeilingStubApi : IDisposable` in
`TaskCeilingScriptTests`, copied from `DelegateCreateStubApi`. It is not shared, per plan D-9. It
records every request as `(Method, Path, IReadOnlyDictionary<string,string> Headers, string Body)`
in a `ConcurrentQueue`. Routes:

- `POST /api/agent-tasks`: 201 with `CreatedJson`. The default echoes `requestedCeilingMinutes` from
  the posted body. The `unconfirmed` arm omits the key.
- `GET /api/agent-tasks/{id}`: 200 with `DetailJson`, which is set per arm.
- `POST /api/agent-tasks/{id}/reply`: `ReplyStatus`/`ReplyJson`. The default is 200 with a minimal
  summary. The `stale-episode` arm returns 409
  `{"code":"ceiling_episode_stale","detail":"episode 1111 is stale; reload"}`.
- Anything else: 404 `{"code":"stub_unrouted"}`, **recorded**. This makes a mutant that posts to
  `/cancel` or `/retry` visible. If Create needs a probe route at the Code base (for example
  `/api/version`), add that route explicitly and name it in the commit.

Every arm sets `ANTIPHON_TASK_TOKEN=task-token-SENTINEL-7f3e` through the environment overload, and
every arm asserts `run.Output.ShouldNotContain("SENTINEL")`. Operator arms write
`operator-token-SECRET-91ab` to a temp file under the case's own directory and point
`ANTIPHON_OPERATOR_TOKEN_FILE` at it. Every arm also asserts `ShouldNotContain("SECRET")`. Each case
deletes its own temp directory, by the literal path it created, in `finally`.

The stub returns ISO timestamps, and pwsh 7 `ConvertFrom-Json` turns them into `DateTime`, whose
printed form depends on the locale. So the Status oracles assert the D-7 lines **up to the
timestamp token**: prefix and suffix around `held <at>` and `deadline <at>`. Equality on the
timestamp text is not asserted (see Q-5).

**Server cases.**
- Create calls use `kit.Service(db).CreateAsync(new CreateAgentTaskRequest("c1610 s4a",
  Role: AgentTaskRole.Review, Workspace: WorkspaceMode.Shared) { CeilingMinutes = m, CeilingReason
  = r }, kit.Caller, CancellationToken.None)`. Use the Shared workspace, so that no worktree is
  provisioned.
- Rows are read through a fresh context (`kit.ReadAsync`), and counts through
  `kit.TaskCountAsync()` plus an `AgentTaskEvents` count in the same schema.
- For refusals: `var ex = await Should.ThrowAsync<ValidationException>(...)`, then
  `ex.Errors.Keys.ShouldBe([expectedField])`, then both counts are 0.

### Proves it works now

Notation: `arm` is the `[Arguments]` value, `→` is the decisive assertion, and **red today**
says why the case fails on master (or on the scaffold).

#### CP-1 / CP-4: `TaskCeilingScriptTests` (33 cases)

**V-13a `C1610_Status(string arm)`, 9 cases.** Run `delegate.ps1 -Status 11111111-1111-1111-1111-111111111111`
against `DetailJson(arm)`. The baseline detail is a Working Review summary with `ceilingBudget`,
`ceilingHold` and `failedSession` set or absent per arm.

| arm | Detail | Decisive assertions |
|---|---|---|
| seat-held | Failed; `failedSession {summary:"Failed, session still Running: seat held", activity:"working", sessionId:S, failureSource:"Ceiling", noticeState:"Told"}` | the line `Seat: Failed, session still Running: seat held; activity working; session S; failure source Ceiling; notice Told` exists; the next line is exactly `Retry stops this session and spends a new attempt; Cancel does not apply to a Failed task.` |
| unknown | Failed; `failedSession.summary = "session state unknown: release unconfirmed"`, `noticeState:null` | `Seat: session state unknown: release unconfirmed; ...; notice none`; `ShouldNotContain("seat held")`; `ShouldNotContain("Retry stops this session")` |
| ceiling-hold | Working; `ceilingHold {episodeId:E, nextCeilingMinutes:240, lastTranscriptKind:"ToolCall", lastTranscriptAgeSeconds:360, phaseKind:"LocalExecution", phaseLimitMinutes:90, phaseDeadlineAt, capReached:false}` | `Ceiling hold: episode E; held ` … `; bound 240 min; last transcript ToolCall 360s`; `Phase: LocalExecution 90-minute limit, deadline ` …; `Decide: delegate.ps1 -Reply 11111111-... -CeilingAction Continue\|Extend -EpisodeId E [-ExtendMinutes M]`; no `Final hold` |
| ceiling-hold-no-phase | as ceiling-hold, with `phaseKind:null` | exactly `Phase: none - no phase deadline covers this tail; the ceiling is its only deadline` |
| final-hold | as ceiling-hold, with `capReached:true`, `nextCeilingMinutes:720` | `Final hold at 720 minutes: Continue and Extend refuse. Operator Extend (-OperatorExtend -ExtendMinutes 1..240 -CeilingReason "...") or POST /api/agent-tasks/11111111-.../cancel`; `ShouldNotContain("Decide:")` |
| budget-resolved | `ceilingBudget {requestedMinutes:600, requestedReason:"long review", resolvedMinutes:600, resolvedSource:"requested", nextCeilingMinutes:600, absoluteCapMinutes:720}` | `Ceiling: 600 min (requested); next bound 600 min; cap 720`; `Ceiling reason: long review` |
| budget-requested | `ceilingBudget {requestedMinutes:600, requestedReason:"r", resolvedMinutes:null, ...}` | `Ceiling: 600 min requested; resolved at dispatch` |
| budget-default | `ceilingBudget {requestedMinutes:null, resolvedMinutes:null, ...}` | `Ceiling: role default; resolved at dispatch`; no `Ceiling reason:` |
| legacy-detail | none of `ceilingBudget`, `ceilingHold`, `failedSession` | no line starts with `Ceiling`, `Seat:`, `Phase:`, `Decide:` or `Final hold`; exit 0 |

Every arm checks: exit 0, the lines appear after `Delegate: <status>` and in D-7 order, exactly one
GET request, no POST, no `X-Antiphon-Operator-Token` header, and no `SENTINEL`.
**Red today:** master's Status reads none of the three fields, so every arm that expects a line is
red. `legacy-detail` is a **guard green** on master and on the scaffold.

**V-13b `C1610_Declared_budget(string arm)`, 12 cases.** Run `delegate.ps1 -Role Docs -Goal x
[-CeilingMinutes v] [-CeilingReason r]`.

| arm | Args | Decisive assertions |
|---|---|---|
| valid | `600`, `"long review"` | exit 0; the single `POST /api/agent-tasks` body has `ceilingMinutes == 600` (number) and `ceilingReason == "long review"`; output contains `ceiling: 600 min requested` |
| omitted | none | exit 0; the body has **no** `ceilingMinutes` or `ceilingReason` key (`TryGetProperty` false); output has no `ceiling` line and no `WARNING: ceiling_unconfirmed` |
| zero | `0`, `"r"` | exit 2; `ceiling_invalid:`; stub request count 0 |
| negative | `-5`, `"r"` | exit 2; `ceiling_invalid:`; 0 requests |
| over-cap | `721`, `"r"` | exit 2; `ceiling_invalid:`; 0 requests |
| non-numeric | `"ten"`, `"r"` | exit 2; `ceiling_invalid:` (not a PowerShell binding error); 0 requests |
| huge | `"99999999999999999999"`, `"r"` | exit 2; `ceiling_invalid:`; 0 requests |
| reasonless | `600` | exit 2; `ceiling_invalid:`; 0 requests |
| reason-only | reason `"r"` without minutes | exit 2; `ceiling_invalid:`; 0 requests |
| long-reason | `600`, 1001 × `a` | exit 2; `ceiling_invalid:`; 0 requests |
| control-char | `600`, `"line1` + LF + `[antiphon-task:x]"` | exit 2; `ceiling_invalid:`; 0 requests |
| unconfirmed | `600`, `"r"`, with `CreatedJson` lacking `requestedCeilingMinutes` | exit 0; output contains `WARNING: ceiling_unconfirmed:` and `GET /api/version`; 1 request |

Every arm checks: no `SENTINEL`, and no operator header. **Red today:** the script has no ceiling
parameters, so every arm passing them fails at binding without the `ceiling_invalid:` text (red),
and `valid`/`unconfirmed` fail binding. `omitted` is a **guard green**.

**V-13c `C1610_Ceiling_reply(string arm)`, 12 cases.** Run `delegate.ps1 -Reply T -CeilingAction
<a> -EpisodeId E ...`. E is a fixed GUID.

| arm | Args / stub | Decisive assertions |
|---|---|---|
| continue | `Continue`; the token file **is** set (so a leaked global header would show) | exit 0; exactly one request `POST /api/agent-tasks/T/reply`; body key set **equals** `{ceilingAction, episodeId}`; `ceilingAction == "Continue"`; no `origin`/`message`; no `X-Antiphon-Operator-Token` header; output is `Decision recorded for task T (episode E): Continue. The session continues; -Status shows the next bound.` |
| extend | `Extend -ExtendMinutes 60` | key set `{ceilingAction, episodeId, extendMinutes}`; `extendMinutes == 60`; no operator header |
| message-exclusive | `Continue` plus positional `"hello"` | exit 2; `ceiling_invalid:`; 0 requests |
| continue-with-minutes | `Continue -ExtendMinutes 30` | exit 2; `ceiling_invalid:`; 0 requests |
| extend-missing-minutes | `Extend` | exit 2; `ceiling_invalid:`; 0 requests |
| extend-out-of-range | `Extend -ExtendMinutes 721` | exit 2; `ceiling_invalid:`; 0 requests |
| bad-episode | `-EpisodeId not-a-guid` | exit 2; `ceiling_invalid:`; 0 requests |
| operator-extend | `Extend -ExtendMinutes 120 -OperatorExtend -CeilingReason "operator: long merge"` | exit 0; header `X-Antiphon-Operator-Token == "operator-token-SECRET-91ab"`; key set `{ceilingAction, episodeId, extendMinutes, operatorExtend, ceilingReason}`; `operatorExtend == true`; `ShouldNotContain("SECRET")` |
| operator-no-reason | `Extend -ExtendMinutes 120 -OperatorExtend` | exit 2; `ceiling_invalid:`; 0 requests |
| operator-over-240 | `Extend -ExtendMinutes 241 -OperatorExtend -CeilingReason "r"` | exit 2; `ceiling_invalid:`; 0 requests |
| operator-no-token | as operator-extend, with `ANTIPHON_OPERATOR_TOKEN_FILE` pointing at a missing file | exit 2; `operator_token_unavailable:`; 0 requests; no `SECRET` |
| stale-episode | `Continue`, with a 409 stub | nonzero exit; output contains `ceiling_episode_stale` and the stub detail text; no `Decision recorded` |

Every arm checks: no `SENTINEL`. **Red today:** `-CeilingAction` is unknown to master's script, so
every arm fails at binding without the oracle text.

#### CP-2 / CP-5: `TaskCeilingCreateBindingTests` (22 cases)

| V | Method (arms) | Arrange / act | Decisive assertion | Red today (scaffold) |
|---|---|---|---|---|
| V-15 | `C1610_Create_binds_requested_ceiling` (six-hundred=600, one=1, cap=720), 3 | create with `CeilingMinutes=m`, `CeilingReason="declared"` | row `RequestedCeilingMinutes == m`, `RequestedCeilingReason == "declared"`; created DTO `RequestedCeilingMinutes == m`; `ResolvedCeilingMinutes` null (resolves only at dispatch) | red: the scaffold never binds |
| V-16 | `C1610_Create_refuses_invalid_ceiling` (zero=0, negative=-1, over-cap=721, overflow=4_294_967_297, reasonless=600/null, blank-reason=600/"   ", reason-only=null/"r", long-reason=600/1001×a, control-char=600/"a\u0007b"), 9 | create | `ValidationException`; field `ceilingMinutes` for the first six arms and `ceilingReason` for reason-only and control-char (and long-reason, see Q-3); `TaskCountAsync()==0`; `AgentTaskEvents` count 0 | red: the scaffold accepts all (count 1) |
| V-17 | `C1610_Requested_ceiling_resolves_at_dispatch` (requested, default), 2 | create (requested: 600/"long review"; default: none, with `kit.Settings.RolePolicy["Review"]` holding `TimeoutMinutes=90`), read the row, call `CeilingHoldFacts.Resolve(row, kit.Settings)` | requested: no throw, Resolved 600, Source `requested`, Next 600. default: 90, `rolePolicy`, 90 | requested red (Resolve gives 90/rolePolicy); default **guard green** |
| V-18 | `C1610_Retry_keeps_explicit_and_reresolves_default` (explicit, default), 2 | create (explicit 600/"r"; default none); set the row to Failed with `DispatchedAt`, and `Resolve` with RolePolicy 90; `kit.Service(db).RetryAsync(id)`; read; then set RolePolicy to 45 and `Resolve` again | after Retry: Resolved, Source and Next are null; explicit keeps Requested 600/"r" and re-resolves to 600; default has Requested null and re-resolves to **45**/`rolePolicy` | explicit red (Requested null after create); default **guard green** |
| V-19 | `C1610_Absent_ceiling_keeps_handoff_digest`, 1 | fixed request `new CreateAgentTaskRequest("c1610 digest pin", Title: "pin", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, ExpectedMinutes: 30)`; ceiling variant built by `JsonSerializer.Deserialize<CreateAgentTaskRequest>` of the same JSON plus `"CeilingMinutes":600,"CeilingReason":"r"` (it compiles at the pre-scaffold base) | (1) `HandoffRequestDigest(request) == PinnedBaseDigest` (hex from CP-0); (2) `SerializeToElement(request)` has no `CeilingMinutes`/`CeilingReason` property; (3) the ceiling variant's digest differs from the pin | CP-0: red at (1) by construction (pin capture). CP-2: **guard green** (scaffold carries `WhenWritingNull`) |
| V-20 | `C1610_Detail_and_brief_echo_budget` (detail-requested, detail-default, brief-requested, brief-default), 4 | detail: create, then `GetAsync(id)`; brief: read the row, `DelegationReportFormatter.BuildBrief(row, kit.Settings)` | detail-requested: `CeilingBudget == (600, "long review", null, null, null, 720)`; detail-default: `CeilingBudget` non-null with Requested null and cap 720; brief-requested: exactly one line `Declared ceiling: 600 minutes (reason: long review). At it the task is held Working for a decision; nothing is killed.`; brief-default: `ShouldNotContain("Declared ceiling")` | the three non-default arms red; brief-default **guard green** |
| V-21 | `C1610_Follow_up_does_not_inherit_ceiling`, 1 | create a parent with 600/"r"; mark it Succeeded; create `FollowUpOnTask: parent.Id` without fields (follow the `TaskPlatformPersistenceTests` follow-up arrange at `:142`) | precondition parent Requested 600; follow-up row Requested null/null | red at the precondition |

#### CP-3 / CP-15: `CeilingHold.test.tsx` (7 tests)

The fixture is `holdDetail({capReached, phaseKind})`: a Working summary plus `ceilingHold` with
episode E and `question` text. msw records every request into an array, so no request goes
unasserted.

| V | Test name (exact) | Decisive assertions |
|---|---|---|
| V-14a | `Continue sends a typed decision without an ordinary reply` | click Continue → exactly one POST `/api/agent-tasks/T/reply` with body `toEqual({ceilingAction:'Continue', episodeId:E})`; notification `Decision recorded; session continues`; within the card no button named /retry/i, /answer/i or /auto/i |
| V-14b | `Extend validates minutes and reports a stale episode` | 0 → Extend disabled, no request; 721 → disabled, no request; 1.5 → disabled; 60 → body `{ceilingAction:'Extend', episodeId:E, extendMinutes:60}`; the next response is 409 `{code:'ceiling_episode_stale', detail:'episode is stale'}`, so the card shows `episode is stale` and a refetch GET of the detail is observed |
| V-14c | `Final hold offers only Cancel and operator guidance` | capReached → Continue and Extend are `toBeDisabled()`; the guidance mentions `-OperatorExtend` and Cancel; no control named /operator/i; Cancel → one POST `/cancel`, zero `/reply` |
| V-14d | `renders %s guidance and sends nothing on render` (`it.each(['phase','no-phase'])`), 2 | phase: text `Phase LocalExecution: 90-minute limit` (from the server question/guidance); no-phase: exact `No phase deadline covers this tail; the ceiling is its only deadline.`; both: zero POSTs after render and a 50 ms flush |
| V-14e | `renders no hold card for %s` (`it.each(['working-without-hold','failed-seat-held'])`), 2 | `queryByTestId('ceiling-hold-card')` is null; zero POSTs |

**Red today (scaffold):** the empty card renders nothing, so V-14a..d are red, and V-14e is a
**guard green** (2).

### Scoped safety witnesses

| Safety property (brief) | Witnesses |
|---|---|
| A ceiling is a HOLD, not a kill | CP-7 reruns S2/S3 `TaskCeilingHoldTests`/`TaskCeilingControlTests` (Working kept, `Stopper.Killed` empty except Retry); V-14d (no request on render, no auto-continue); V-13c `continue` (exactly one `/reply`, never `/cancel`/`/retry`); V-20 brief line says nothing is killed; S4a adds no stop verb (R-11 diff scope) |
| A create without the new fields has the same digest and behaviour | V-19 (pin computed at the pre-S4a base, plus a structural no-key check); V-13b `omitted` (no body keys); V-20 `brief-default` plus CP-11 corpus (byte-identical briefs); CP-8 delegate Create contracts; CP-13 handoff replay |
| Typed ceiling reply semantics | V-13c (12 arms), V-14a/b/c; server semantics stay in CP-7 |
| Budget/limit validation | server V-16 (9 arms) and CLI V-13b (10 refusal arms) and V-13c (6 refusal arms): negative, zero, over-cap, huge/overflow, non-numeric, reasonless, blank, reason-only, too long, control characters |
| The CLI never prints a secret | every V-13 arm asserts no `SENTINEL` (task token); operator arms assert no `SECRET` (operator token); `operator-no-token` fails closed before any request |
| The client card renders every hold state and never offers a refused action | V-14c (cap: Continue/Extend disabled, no operator control), V-14b (out-of-range Extend disabled), V-14d (phase/no-phase), V-14e (absent) |

### Guards the regression

- R-1: default briefs byte-identical | CP-11 `C1150_Brief_envelope_corpus_matches_the_expected_matrix` (48) and `C1150_Outer_envelope_and_receipt_floor_through_the_queue` (7): corpus equality.
- R-2: Status/Reply still skip the land-v2 version probe | CP-10 `C495_StatusDoesNotProbeVersion*`, `C495_ReplyDoesNotProbeVersion*`.
- R-3: delegate Create contracts (kind, StartRef, title, workspace default, bind diagnosis) | CP-8 (79).
- R-4: S2/S3 hold, typed reply, retry and successor fence | CP-7 (49).
- R-5: `ValidateInitial` unchanged | CP-6 `TaskCeilingPolicyTests` (12).
- R-6: no pending model change (no migration) | CP-14 `C1065_EpisodeIdentitySurvivesRestart`.
- R-7: detail DTO and handoff digest/replay | CP-13 (22).
- R-8: TaskDetailBody and BlockedQuestionCard behaviour | CP-16 (12).
- R-9: lane, Slow registry and limiter rosters | CP-9 (26).
- R-10: verification brief profile block | CP-12 (1).
- R-11: textual scope. Code runs `git diff <base> HEAD -- server/Application/Dtos/AgentTaskDtos.cs`, and it must show exactly three changed lines, each adding `partial`. The plan's "not touched" list must show an empty diff (`git diff --exit-code <base> HEAD -- server/Domain/Entities/AgentTask.cs server/Infrastructure/Data/AppDbContext.cs server/Migrations server/Application/Services/AgentTaskReplyService*.cs server/Api/*AgentTaskEndpoints*.cs scripts/deploy-server2.ps1 docs/docker-stack.md`). These are git commands, not test runs.
- R-12: `delegate.ps1` stays ASCII and Windows PowerShell 5.1 parseable | CP-17 `Commit_on_settle_scripts_are_ascii_only`. Code also checks that `git grep -nE '\?\?|\?\.' -- scripts/delegate.ps1` gains no hit (no `??`, `?.` or ternary in the new lines).

### Guard inventory

Plan refs: D-1..D-8 of the plan; "G" is a safety-critical guard. Each one is independently
bypassable and maps 1:1 to the PC with the same number.

| G | Ref | Guard | Witness (case) | PC |
|---|---|---|---|---|
| G-1 | D-2 | create refuses an out-of-range ceiling | V-16 zero | PC-1 |
| G-2 | D-2 | refusal happens before any insert | V-16 negative (counts) | PC-2 |
| G-3 | D-2 | over-cap is refused, not clipped | V-16 over-cap | PC-3 |
| G-4 | D-1/D-2 | a long value is validated before any narrowing | V-16 overflow | PC-4 |
| G-5 | D-2 | a missing reason is refused | V-16 reasonless | PC-5 |
| G-6 | D-2 | a whitespace reason is refused | V-16 blank-reason | PC-6 |
| G-7 | D-2 | a reason over 1000 characters is refused | V-16 long-reason | PC-7 |
| G-8 | D-2 | reason without minutes is refused | V-16 reason-only | PC-8 |
| G-9 | Q-1 | control characters in a reason are refused | V-16 control-char | PC-9 |
| G-10 | D-3 | create binds the requested minutes | V-15 six-hundred | PC-10 |
| G-11 | D-3 | create binds the reason with the minutes | V-17 requested | PC-11 |
| G-12 | D-3 | created DTO echoes the requested minutes | V-15 one | PC-12 |
| G-13 | D-4 | follow-ups never inherit a requested ceiling | V-21 | PC-13 |
| G-14 | D-4 | Retry keeps Requested | V-18 explicit | PC-14 |
| G-15 | D-4 | Retry clears Resolved, so the default re-resolves | V-18 default | PC-15 |
| G-16 | D-1 | `CeilingMinutes` omitted from the digest when null | V-19 | PC-16 |
| G-17 | D-1 | `CeilingReason` omitted from the digest when null | V-19 | PC-17 |
| G-18 | D-3 | brief line only when requested | V-20 brief-default | PC-18 |
| G-19 | D-3 | detail always carries `CeilingBudget` | V-20 detail-default | PC-19 |
| G-20 | scope | no entity/model change (no migration) | CP-14 C1065 | PC-20 |
| G-21 | D-5 | CLI refuses an out-of-range ceiling before any request | V-13b zero | PC-21 |
| G-22 | D-5/Q-2 | CLI refuses a non-integer or huge ceiling with `ceiling_invalid` | V-13b huge | PC-22 |
| G-23 | D-5 | CLI refuses reason without minutes | V-13b reason-only | PC-23 |
| G-24 | D-5 | CLI refuses minutes without reason | V-13b reasonless | PC-24 |
| G-25 | D-5 | CLI refuses a reason over 1000 | V-13b long-reason | PC-25 |
| G-26 | Q-1 | CLI refuses control characters | V-13b control-char | PC-26 |
| G-27 | D-1/D-5 | an omitted ceiling sends no keys | V-13b omitted | PC-27 |
| G-28 | D-5 | an unconfirmed echo warns | V-13b unconfirmed | PC-28 |
| G-29 | D-6 | the typed body carries no `origin` | V-13c continue | PC-29 |
| G-30 | D-6 | `-Message` is exclusive with `-CeilingAction` | V-13c message-exclusive | PC-30 |
| G-31 | D-6 | Continue refuses minutes | V-13c continue-with-minutes | PC-31 |
| G-32 | D-6 | Extend minutes stay in 1..720 | V-13c extend-out-of-range | PC-32 |
| G-33 | D-6 | Extend requires minutes | V-13c extend-missing-minutes | PC-33 |
| G-34 | D-6 | EpisodeId must be a GUID | V-13c bad-episode | PC-34 |
| G-35 | D-6 | operator Extend sends the operator header | V-13c operator-extend (header) | PC-35 |
| G-36 | D-6 | the operator token is never printed | V-13c operator-extend (output) | PC-36 |
| G-37 | D-6 | operator Extend fails closed before POST | V-13c operator-no-token | PC-37 |
| G-38 | D-6 | operator Extend requires a reason | V-13c operator-no-reason | PC-38 |
| G-39 | D-6 | operator Extend minutes stay in 1..240 | V-13c operator-over-240 | PC-39 |
| G-40 | D-6 | the operator header is never on an ordinary path | V-13c continue (no header) | PC-40 |
| G-41 | D-6 | a server refusal is surfaced and exits nonzero | V-13c stale-episode | PC-41 |
| G-42 | D-7 | Seat line uses the server summary | V-13a unknown | PC-42 |
| G-43 | D-7 | Retry consequence only for seat held | V-13a unknown (absence) | PC-43 |
| G-44 | D-7 | final hold prints no Decide hint | V-13a final-hold | PC-44 |
| G-45 | D-7/Q-4 | no ceiling or seat line is synthesised without server fields | V-13a legacy-detail | PC-45 |
| G-46 | D-7 | no-phase sentence is printed exactly | V-13a ceiling-hold-no-phase | PC-46 |
| G-47 | D-7 | Ceiling line is printed from `ceilingBudget` | V-13a budget-resolved | PC-47 |
| G-48 | secrets | the task token is never printed | V-13a seat-held (`SENTINEL`) | PC-48 |
| G-49 | D-8 | client posts the typed body only | V-14a | PC-49 |
| G-50 | D-8 | client Extend minimum 1 | V-14b (0) | PC-50 |
| G-51 | D-8 | client Extend maximum 720 | V-14b (721) | PC-51 |
| G-52 | D-8 | client shows a 409 stale episode | V-14b (409) | PC-52 |
| G-53 | D-8 | cap reached disables Continue | V-14c | PC-53 |
| G-54 | D-8 | cap reached disables Extend | V-14c | PC-54 |
| G-55 | D-8 | Cancel goes to `/cancel`, never `/reply` | V-14c | PC-55 |
| G-56 | D-8/H-3 | no client operator-Extend control | V-14c | PC-56 |
| G-57 | D-8 | card only with `ceilingHold` | V-14e working-without-hold | PC-57 |
| G-58 | D-8/hold | the card sends nothing on render (no auto-continue) | V-14d | PC-58 |

Not safety-critical, so no PC: the handoff-replay created echo at `:1609`. Its absence only
produces a false `ceiling_unconfirmed` warning, which fails toward caution. The `ops-http.md` text
also gets no PC.

### Positive controls

**Binding required after Code** for every row, because no payload exists yet. The binder at the
confirmed landed source supplies the symbol, the literal patch, a unique match count, the file,
preimage and mutant hashes, the command, and the failure set, per
[the mechanical PC contract](../../mechanical-pc-contract.md). Classification:
`binding` / `binding-missing` for all 58. Variant `v1`. Reachability is the arm named in the
Guard inventory with every other input valid.

Each PC is one compiling edit in production code only (`CeilingHoldFacts.cs`,
`AgentTaskService.cs`, `AgentTaskDtos.Ceiling.cs`, `DelegationReportFormatter.cs`, `AgentTask.cs`
for PC-20, `scripts/delegate.ps1`, `CeilingHoldCard.tsx`, `agentTasks.ts`), never a test or a
fixture. The expected red is exactly the named method, at the named arm's decisive assertion.

| PC | Break by | Red method (filter) at |
|---|---|---|
| PC-1 | delete the create-path `ValidateInitial` call | `TaskCeilingCreateBindingTests/C1610_Create_refuses_invalid_ceiling` zero: no throw |
| PC-2 | move the create validation call after `SaveChangesAsync` | same method, negative: `TaskCountAsync()==0` |
| PC-3 | `minutes = Math.Min(minutes, 720)` before validation | same, over-cap: no throw |
| PC-4 | pass `(long?)(int?)minutes` (unchecked narrowing) to the validator | same, overflow: 4294967297 narrows to 1, no throw |
| PC-5 | create validator passes `reason ?? "x"` | same, reasonless |
| PC-6 | `IsNullOrWhiteSpace(reason)` → `IsNullOrEmpty(reason)` in `ValidateInitial` | same, blank-reason |
| PC-7 | `reason.Length > 1000` → `> 1001` | same, long-reason |
| PC-8 | delete the reason-only rule | same, reason-only |
| PC-9 | delete the control-character rule | same, control-char |
| PC-10 | delete the `BindRequested` call | `.../C1610_Create_binds_requested_ceiling` six-hundred: row minutes |
| PC-11 | `BindRequested` sets minutes only | `.../C1610_Requested_ceiling_resolves_at_dispatch` requested: `Resolve` throws |
| PC-12 | drop `RequestedCeilingMinutes =` from the `:1794` created DTO | `.../C1610_Create_binds_requested_ceiling` one: DTO echo |
| PC-13 | a follow-up copies the source task's `RequestedCeiling*` | `.../C1610_Follow_up_does_not_inherit_ceiling`: follow-up minutes |
| PC-14 | `Reset` adds `task.RequestedCeilingMinutes = null` | `.../C1610_Retry_keeps_explicit_and_reresolves_default` explicit |
| PC-15 | `Reset` drops `ResolvedCeilingMinutes = null` | same method, default: Resolved null after Retry |
| PC-16 | remove `[JsonIgnore(WhenWritingNull)]` from `CeilingMinutes` | `.../C1610_Absent_ceiling_keeps_handoff_digest`: pin equality |
| PC-17 | remove it from `CeilingReason` | same method: pin equality |
| PC-18 | emit the Declared line unconditionally | `.../C1610_Detail_and_brief_echo_budget` brief-default |
| PC-19 | `CeilingBudget = null` in the detail echo | same, detail-default |
| PC-20 | add a mapped `public int? CeilingScratch { get; set; }` to `AgentTask` | `BlockedTaskParkStateTests/C1065_EpisodeIdentitySurvivesRestart`: pending-model assertion |
| PC-21 | delete the CLI range check | `TaskCeilingScriptTests/C1610_Declared_budget` zero: request count 0 |
| PC-22 | replace the `[long]::TryParse` with an `[int]` cast | same, huge: `ceiling_invalid:` text |
| PC-23 | delete the CLI reason-only check | same, reason-only |
| PC-24 | delete the CLI missing-reason check | same, reasonless |
| PC-25 | `-gt 1000` → `-gt 1001` | same, long-reason |
| PC-26 | delete the CLI control-character check | same, control-char |
| PC-27 | always set `$body['ceilingMinutes']` | same, omitted: no key |
| PC-28 | skip the echo comparison | same, unconfirmed: WARNING |
| PC-29 | add `origin = 'Cli'` to the typed body | `.../C1610_Ceiling_reply` continue: key set |
| PC-30 | delete the `-Message` exclusivity check | same, message-exclusive |
| PC-31 | delete the Continue-minutes check | same, continue-with-minutes |
| PC-32 | Extend bound 720 → 721 | same, extend-out-of-range |
| PC-33 | delete the Extend-requires-minutes check | same, extend-missing-minutes |
| PC-34 | delete the `[guid]::TryParse` check | same, bad-episode |
| PC-35 | omit the operator header copy | same, operator-extend: header |
| PC-36 | `Write-Output ("operator token " + $operatorToken)` | same, operator-extend: no `SECRET` |
| PC-37 | catch the token error and continue without the header | same, operator-no-token: 0 requests |
| PC-38 | delete the operator reason check | same, operator-no-reason |
| PC-39 | operator bound 240 → 720 | same, operator-over-240 |
| PC-40 | add the operator header to `$headers` whenever the token file reads | same, continue: no header |
| PC-41 | wrap the typed POST in `try { } catch { }` and print success | same, stale-episode |
| PC-42 | print `Failed, session still Running: seat held` for any `failedSession` | `.../C1610_Status` unknown |
| PC-43 | print the Retry consequence unconditionally | same, unknown: absence |
| PC-44 | drop the `capReached` branch (always print Decide) | same, final-hold |
| PC-45 | print `Ceiling: role default; resolved at dispatch` when `ceilingBudget` is absent | same, legacy-detail |
| PC-46 | drop the `phaseKind` null branch | same, ceiling-hold-no-phase |
| PC-47 | delete the Ceiling line | same, budget-resolved |
| PC-48 | `Write-Output $headers['X-Antiphon-Task-Token']` in Status | same, seat-held: no `SENTINEL` |
| PC-49 | `useCeilingDecision` delegates to the ordinary reply body (adds `message`/`origin`) | `CeilingHold.test.tsx -t "Continue sends a typed decision without an ordinary reply"` |
| PC-50 | Extend min 1 → 0 | `-t "Extend validates minutes and reports a stale episode"` (0) |
| PC-51 | drop the Extend max | same test (721) |
| PC-52 | swallow the 409 in `onError` | same test (409 text) |
| PC-53 | Continue `disabled={false}` | `-t "Final hold offers only Cancel and operator guidance"` |
| PC-54 | Extend `disabled={false}` on capReached | same test |
| PC-55 | Cancel calls the ceiling decision | same test (`/reply` count 0) |
| PC-56 | render a `Button` "Operator Extend" | same test (/operator/i control) |
| PC-57 | render when `summary.status === 'Working'` | `-t "renders no hold card for"` (working-without-hold) |
| PC-58 | `useEffect(() => decide('Continue'), [])` | `-t "renders .* guidance and sends nothing on render"` |

**Cycle per PC:** run the mutant red, restore, then run green again. TUnit uses the exact method
filter `/*/*/<Class>/<Method>`, MinExecuted 1. Vitest uses `scripts/test-client.ps1
CeilingHold.test.tsx -t "<name>" -JsonResultPath ...`. Expect the method red on the mutant at the
named arm and green after restore, with zero skipped tests. A build or compile error, zero tests,
or a red at another arm only is not a pass. Batch independent mutations only where they touch
different files **and** methods (git-common). Script PCs (21-48) edit only `delegate.ps1`; the
runner reads it from the repository, so no rebuild is needed. Server PCs (1-20) need an
incremental build per cycle. Mutation runs break/red/restore/green after the land; Code runs the
V/R rows; Review judges this table before the land. All 58 PCs run; none is waived.

### Out of scope

- S4b (deploy drain, docker-stack steps, the six V-13 drain cases): CARD-1646.
- Real delivery of the new brief line to a live session's UserPrompt, and real operator-token
  custody on Windows (`LOCALAPPDATA`, the DPAPI capability store): native/E2E only. The witnesses
  use `ANTIPHON_OPERATOR_TOKEN_FILE`.
- Browser rendering on a real board (Playwright): server2 has no Chromium. jsdom Vitest only.
- HTTP model-binding of a non-numeric or out-of-`long` `ceilingMinutes` JSON value (400 from System.Text.Json): framework behaviour. The CLI refuses those locally (V-13b non-numeric/huge).
- Server-side typed-decision semantics (cap, stale, operator auth): already S2/S3 and rerun as CP-7.

### Implementation order (one Code slice, red first)

1. **Step 0:** the collision check above. Report blocked if it fails.
2. **Commit P (pin capture), group `S4a-pin`:** add `TaskCeilingCreateBindingTests.cs` containing
   **only** `C1610_Absent_ceiling_keeps_handoff_digest`, with `PinnedBaseDigest = "UNPINNED"`.
   No production edit. Push, then run CP-0 (expected red). Copy the actual hex from the assertion
   message.
3. **Commit R (red), group `S4a-red`:** in one commit, set the pin, add the compatible scaffold (DTO
   partial file with `WhenWritingNull` properties the service ignores, `CeilingBudgetDto` left null,
   client `ceilingHold` types plus a `useCeilingDecision` stub that throws, and an empty
   `CeilingHoldCard` that returns null), plus all witnesses, the roster entry and the allowlist line.
   `delegate.ps1` is unchanged. Push, then run CP-1..CP-3. Record each case outcome against the
   red list (guard greens: `legacy-detail`, `omitted`, V-17 default, V-18 default, V-19, V-20
   brief-default, V-14e ×2). Also record per-case durations from the TRX (pwsh p95, Q-6).
4. **Commit G (green), group `S4a`:** the server binding, validation and echoes; `delegate.ps1`; the
   card; and `docs/ops-http.md`. Push, then run CP-4..CP-14 and CP-17 (run G1), and CP-15/CP-16
   (run G2).
5. **Report commit.** No source edit while a run is in flight. Run
   `scripts/check-evidence-diff.ps1 -Repository . -BaseRef <base> -HeadRef HEAD`, R-11, and
   `merge-tree`.

### Open questions for Code (each with the default to use)

- **Q-1. Control characters in a reason.** Default: refuse. Create refuses any char below U+0020
  or equal to U+007F as 422 `ceilingReason`, and the CLI mirrors it. The check is in the new create
  validator, not in `ValidateInitial`, so S2 stays unchanged. Reason: the reason goes verbatim into
  the brief (session input) and into Status output.
- **Q-2. `-CeilingMinutes` type.** Default: `[string]`, parsed with
  `[long]::TryParse(..., [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, ...)`,
  so non-numeric and huge input give `ceiling_invalid:` rather than a binding error. Same for
  `-ExtendMinutes`.
- **Q-3. Error field for reasonless, blank and long reasons.** Default: keep `ValidateInitial`'s
  `ceilingMinutes` key (S2 unchanged). Only reason-only and control-char use `ceilingReason`. V-16
  asserts this exactly.
- **Q-4. Status against an older server without `ceilingBudget`.** Default: print no Ceiling line.
  The CLI never synthesises one (G-45).
- **Q-5. Timestamp text in Status.** Default: print as received. The oracles skip the timestamp
  token. Formatting with `ToString('o')` is allowed but not asserted.
- **Q-6. pwsh witness timeout.** Default: leave `DelegateScriptRunner`'s shared 60 s alone. It is
  a shared helper outside S4a's file list. Record the measured per-case p95 from CP-1 in the
  report. If p95 is above 30 s, file a card rather than widen anything.
- **Q-7. V-18 retry route.** Default: `kit.Service(db).RetryAsync(id, ct)` on a row set to Failed.
  If `RetryAsync` needs a service that `DefaultRunnerKit` lacks, use the harness route of
  `C1610_Retry_resets_hold_but_preserves_requested_ceiling` for the retry step only, and keep the
  create through `kit`. Say so in the report.
- **Q-8. Brief line placement.** Default: emit it right after the workspace block that ends with
  the `integration:` line, but outside that `if`, so a Shared task gets it too.

## Performance budget

No build or test ran here. All values are **estimated, n=0**. Measure in CP-0..CP-3 and calibrate
near 2x p95.

| Phase | Estimate | Bound and recording |
|---|---|---|
| `tests/Antiphon.Tests` isolated build | 8 min | the checkpoint tool records build separately; three builds (pin, red, green) |
| pwsh witness case | 2-4 s (Create runs git for the checkout root) | shared 60 s runner timeout (Q-6) |
| isolated-schema create case | 2-5 s | template shared once per run |
| Vitest file | about 15 s after install | `test-client.ps1` npm ci once under its lock |
| Slot admission | 0 expected | 45 min import default; exit 4 is "not run" |

Shared setup: the PostgreSQL template is assembly-owned; schemas are per test; there are no
containers or external calls. The pwsh cases share nothing (each has its own loopback stub) and
are serialised by `ProcessSpawnLimit`. CP-1/CP-4 and CP-8 run in the same host, under that
limiter. Never co-schedule `Antiphon.Agents.Pty.Tests`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-0 | S4a-pin | `tests/Antiphon.Tests -> bin-c1610-s4a-pin/` | server-integration-digest-pin | `/*/*/TaskCeilingCreateBindingTests/C1610_Absent_ceiling_keeps_handoff_digest` | V-19 pin capture | 1 executed; intended red at the pin assertion; the message carries the base digest | 1 | 1 |
| CP-1 | S4a-red | `tests/Antiphon.Tests -> bin-c1610-s4a-red/` | script-integration-ceiling-red | `/*/*/TaskCeilingScriptTests/C1610_*` | V-13a..V-13c | 33 executed; 31 intended red; guard green legacy-detail, omitted; no skipped | 33 | 4 |
| CP-2 | S4a-red | CP-1 | server-integration-create-binding-red | `/*/*/TaskCeilingCreateBindingTests/C1610_*` | V-15..V-21 | 22 executed; 18 intended red; guard green V-17 default, V-18 default, V-19, V-20 brief-default; no skipped | 22 | 3 |
| CP-3 | S4a-red | n/a | client-Vitest-red | `pwsh -NoProfile -File scripts/test-client.ps1 CeilingHold.test.tsx -JsonResultPath ../.antiphon/c1610-s4a-client-red.json` | V-14a..V-14e | JSON numTotalTests=7 with the exact names; 5 intended red, V-14e x2 guard green; no setup error, pending or todo | n/a | 2 |
| CP-4 | S4a | `tests/Antiphon.Tests -> bin-c1610-s4a/` | script-integration-ceiling | `/*/*/TaskCeilingScriptTests/C1610_*` | V-13a..V-13c | 33 passed; no failed/skipped | 33 | 4 |
| CP-5 | S4a | CP-4 | server-integration-create-binding | `/*/*/TaskCeilingCreateBindingTests/C1610_*` | V-15..V-21 | 22 passed; no failed/skipped | 22 | 3 |
| CP-6 | S4a | CP-4 | server-unit-ceiling-policy | `/*/*/TaskCeilingPolicyTests/*` | R-5 | 12 passed; no failed/skipped | 12 | 1 |
| CP-7 | S4a | CP-4 | server-integration-ceiling-hold-control | `/*/*/(TaskCeilingHoldTests*)\|(TaskCeilingControlTests*)/*` | R-4 | 49 passed; no failed/skipped | 49 | 4 |
| CP-8 | S4a | CP-4 | script-integration-delegate-contracts | `/*/*/(DelegateScriptKindTests*)\|(DelegateScriptStartRefTests*)\|(DelegateScriptTitleTests*)\|(DelegateScriptWorkspaceDefaultTests*)\|(DelegateScriptBindDiagnosisTests*)/*` | R-3 | 79 passed; no failed/skipped | 79 | 10 |
| CP-9 | S4a | CP-4 | server-unit-guards | `/*/*/(DelegateScriptRunnerSwitchTests*)\|(TestLaneCategoryGuardTests*)\|(TestClassificationGuardTests*)\|(ProcessSpawnLimitTests*)/*` | R-9 | 26 passed; no failed/skipped | 26 | 1 |
| CP-10 | S4a | CP-4 | script-integration-status-reply-compat | `/*/*/DelegateScriptLandCompatibilityTests/(C495_StatusDoesNotProbeVersion*)\|(C495_ReplyDoesNotProbeVersion*)` | R-2 | 2 passed; no failed/skipped | 2 | 2 |
| CP-11 | S4a | CP-4 | server-integration-brief-corpus | `/*/*/DelegationDispatchRecoveryBoundaryTests/(C1150_Brief_envelope_corpus_matches_the_expected_matrix*)\|(C1150_Outer_envelope_and_receipt_floor_through_the_queue*)` | R-1 | 55 passed; no failed/skipped | 55 | 4 |
| CP-12 | S4a | CP-4 | server-integration-verification-brief | `/*/*/VerificationRoundBriefTests/*` | R-10 | 1 passed; no failed/skipped | 1 | 1 |
| CP-13 | S4a | CP-4 | server-integration-detail-handoff | `/*/*/(AgentTaskDetailBlockedContextTests*)\|(PipelineHandoffPilotTests*)/*` | R-7 | 22 passed; no failed/skipped | 22 | 4 |
| CP-14 | S4a | CP-4 | server-integration-model-drift | `/*/*/BlockedTaskParkStateTests/C1065_EpisodeIdentitySurvivesRestart` | R-6 | 1 passed; no failed/skipped | 1 | 2 |
| CP-15 | S4a | n/a | client-Vitest | `pwsh -NoProfile -File scripts/test-client.ps1 -TypecheckProject client/tsconfig.app.json -LintPath client/src/features/delegations/CeilingHoldCard.tsx CeilingHold.test.tsx -JsonResultPath ../.antiphon/c1610-s4a-client-green.json` | V-14a..V-14e plus app typecheck and card lint | typecheck and lint pass; fresh JSON numTotalTests=7, numPassedTests=7, failed/pending/todo=0; all names; exit 0 | n/a | 3 |
| CP-16 | S4a | n/a | client-Vitest-detail-regression | `pwsh -NoProfile -File scripts/test-client.ps1 TaskDetailBody.test.tsx BlockedQuestionCard.test.tsx -JsonResultPath ../.antiphon/c1610-s4a-client-detail.json` | R-8 | fresh JSON numTotalTests=12, failed/pending/todo=0; exit 0 | n/a | 2 |
| CP-17 | S4a | CP-4 | script-unit-ascii | `/*/*/CommitOnSettleScriptTests/Commit_on_settle_scripts_are_ascii_only` | R-12 | 1 passed; no failed/skipped | 1 | 1 |

Each row has one isolated build and one exact filter. A reuse row (`Build = CP-n`) shares only its
own After group's build. The union is the whole ordinary scope: 359 TUnit executions plus 26 Vitest
tests. Combined filters use `(Name*)` operands with escaped pipes. The suffix census at
`10c8c2471` found no other class starting with `TaskCeilingScriptTests` or
`TaskCeilingCreateBindingTests`. The floors are a syntax census at `10c8c2471`. Code reconciles
them against a fresh TRX at its post-0824 base, and amends any inherited growth, with the reason,
in the report commit. It never silently lowers a floor.

**Runs.** Import this companion, not the plan. Each run is `start --after <group>` (plus
`--rows` where named) with `--expected-source-sha <committed sha>`, then `wait` until the exit is
not 75. Rows overlap two at a time on Linux.

| Run | Rows | Row minutes | Wall estimate (build + rows/2) | Under 30 |
|---|---|---:|---:|---|
| P | CP-0 | 1 | 9 | yes |
| R | CP-1, CP-2, CP-3 | 9 | 13 | yes |
| G1 | `--rows CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-13,CP-14,CP-17` | 40 | 28 | yes (forecast; see below) |
| G2 | `--rows CP-15,CP-16` | 5 | 5 | yes |

If G1's measured throughput forecasts more than 30 minutes (for example, the build takes longer
than 10 min under load), stop. Split G1 into G1a (CP-4, 5, 6, 7, 13, 14, 17) and G1b (CP-8, 9, 10,
11, 12) as two runs at the same SHA, and report the phase timings. Do not widen any row timeout.

Import validation (this task): `dotnet /work/worktrees/task-00abf1bf/tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-11-02-card-1610-S4a-test-design.md --out .antiphon/s4a-import/manifest.yaml`. Result: `imported 18 rows`, exit 0, no warning (tool dll sha256 prefix `6c81cdb4620c1e9b`, built in the CARD-0824 S1 worktree task-00abf1bf). Derived row timeouts: 15 min, and 30 min for CP-8 (estimate 10). No row timeout is widened.

### Cost

**Estimated, not measured.**

- **Ordinary V/R floor (Code)** = the sum of EstimatedMinutes = CP-0 1 + CP-1 4 + CP-2 3 + CP-3 2 +
  CP-4 4 + CP-5 3 + CP-6 1 + CP-7 4 + CP-8 10 + CP-9 1 + CP-10 2 + CP-11 4 + CP-12 1 + CP-13 4 +
  CP-14 2 + CP-15 3 + CP-16 2 + CP-17 1 = **52 row-minutes**.
- Builds: 3 × 8 = 24. Client install and setup: 3. Ordinary machine total = **79 minutes**.
- Wall time is about 55 minutes with two-row overlap, excluding slot waits.
- The pin run (CP-0, plus its 8-minute build) costs 9 minutes. That is the price of an absolute,
  base-computed digest pin. A structural check alone would save it, but it could not catch a
  positional-parameter mistake.

**PC floor (Mutation)**, 58 PCs:

| Group | PCs | Cost |
|---|---|---|
| Server | PC-1..PC-20 (20) | One forced build (8). Per PC: incremental mutant build (1.5) plus run (0.5), then restore build (1.5) plus run (0.5), plus 0.25 bookkeeping = 4.25. Subtotal 85. Total **93** |
| Script | PC-21..PC-48 (28) | No rebuild. Per PC: mutant run plus restored run of one method (about 0.75 each for 9-12 pwsh cases) plus 0.25 = 1.75. Total **49** |
| Client | PC-49..PC-58 (10) | Per PC: 2 × 0.5 Vitest plus 0.25 = 1.25. Total **12.5** |
| Baselines | 11 methods/tests | 0.5 each = **5.5** |

- **PC floor = 93 + 49 + 12.5 + 5.5 = 160 minutes** (estimated).
- Savings: an 8-minute forced rebuild per cycle would cost 20 × 17 = 340 minutes for the server
  group and 28 × 17.5 = 490 minutes for the script group. The incremental, no-rebuild route saves
  about **680 minutes**. That is justified because script PCs touch no compiled code, and server
  PCs are single-file incremental edits.
- **Total planned machine time = 79 ordinary + 160 PC = 239 minutes**, estimated, plus slot waits
  and binding work.
- Shard the PCs across up to three worktrees at the same tip (server, script, client: independent
  files) if wall time matters.

Before handoff: guards = 58, mapped = 58, missing = 0, duplicate PC maps = 0; all 58 PCs run; Cost
numeric (79 ordinary, 160 PC, 239 total, estimated).
