# CARD-0286 — detect terminal, no-progress implementation delegates

**Plan pass, 2026-08-31. Design only; nothing here is built.**

## Decision

Treat the reported `401 Unauthorized: LiteLLM Virtual Key expected` as a structural, terminal
Codex API failure and surface it through the existing task-failure path. Do **not** translate
Herdr's `agent_status: "done"` into a task or session terminal state: it is the normal post-turn
status on that backend and is deliberately only corroboration.

Add a second, deliberately narrow completion check for an explicit `done` report from a
`Code` task in an isolated `Worktree`: a readable worktree with neither a post-dispatch commit
nor a changed/untracked file is objective zero progress. That report must fail visibly rather
than merge or look successful. Plan, review, test, deploy, debug, and shared-workspace tasks
remain outside this check; clean source trees are legitimate for them and shared changes are not
attributable to one delegate.

Both failure shapes are repeat-blocking launch incidents. The same task family and agent kind
becomes `Blocked` before dispatch, with no replacement process or worktree. A different allowed
kind remains an intentional operator choice. This card adds no automatic retry, silent reroute,
or credential mutation.

## Verified current behaviour and gap

1. `src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs` already normalizes a Codex
   `event_msg/task_complete` containing an `error` object as a `TurnEnd` with
   `IsApiError`, `ApiErrorClass`, and `ApiErrorStatus`. It parses the measured JSON-inside-a-string
   shape, but drops the error text and cannot derive a status from a literal
   `401 Unauthorized: ...` message.
2. `ApiErrorClassifier` maps known `authentication_failed` to `NeedsHuman`, but its HTTP-status
   fallback treats 401 as `Unknown`. Unknown has a timed retry ladder; `NeedsHuman` is terminal and
   never schedules a resume.
3. `AgentTaskReplyService.HandleApiErrorTurnAsync` already does the right durable work once a
   terminal class is reached: fail the task, write an event, release the delegate, emit an
   `ApiErrorTurnDied` incident, enqueue the parent-facing failure, and publish `AgentTaskChanged`.
   It bounds the error excerpt to 600 characters, but today receives no Codex error text to show.
4. CARD-0256's delivery watchdog and `StoppedBeforeFirstPrompt` classifier cover an empty,
   actually `Stopped` session. Its repeat guard only keys on that one failure code. The delivery
   watchdog also regards a successfully typed first prompt as started, which is correct but leaves
   a first-turn provider rejection for the API-error path to explain.
5. The existing `TaskProgressStalled` sweep is intentionally not this repair: it requires repeated
   transcript rows, waits for its stall clock, and only records an incident. The hard/phase deadline
   similarly is not an immediate first-progress verdict.
6. `HerdrStatusCorroborationService` maps `done` plus a working transcript verdict only to a
   `HerdrStatusDisagreement` Warning. `docs/herdr-sessions.md` is explicit that `done` is not
   idle and that Herdr events/statuses are verification triggers, never evidence. This separation
   must stay intact.
7. There is no persisted "expected artefact" field on `AgentTask`. The durable generic evidence
   available today is task-owned worktree activity. Do not parse the brief for filenames. A named
   first artefact can be a later explicit task-contract feature if a real collision proves that
   code-change evidence is insufficient.

## Implementation design

### 1. Preserve and classify the Codex 401 structurally

In `CodexTranscriptNormalizer` extend the `task_complete.error` reader to return a bounded
diagnostic text in addition to `IsApiError`, class, and status:

- retain the existing JSON parse for the measured wrapper, extracting the nested status, type, and
  human message when present;
- add a constrained HTTP-status extraction only within the `error` object for a literal
  `401 Unauthorized: ...` message; do not scan normal assistant text or reports;
- put that diagnostic text on the normalized API-error `TurnEnd`. It remains excluded from normal
  report extraction because `AgentTaskReplyService` already filters `IsApiError` rows, while its
  API-error handler can now include the bounded message in the task failure, incident, and parent
  notification.

In `ApiErrorClassifier`, classify HTTP 401 as `NeedsHuman` in the structural status fallback.
Keep 400, unrecognised classes, and other existing fallbacks unchanged: the new rule is evidence
for authentication/configuration failure, not a text heuristic or a blanket change to every
client error.

`HandleApiErrorTurnAsync` should assign a new
`AgentTaskFailureCode.AuthenticationRequired` when the terminal stub is HTTP 401. Its existing
failure result, `ApiErrorTurnDied` incident, parent delivery, and agent release remain the single
implementation of capture/surface/recovery. The failure text must name the HTTP status and the
bounded diagnostic, never a key value or configuration secret.

### 2. Reject a clean, explicitly completed Code worktree

Before `ClassifyReportAsync` accepts an explicit `[antiphon-report:... done]` as `Succeeded`, add
a small, read-only initial-progress validation arm:

- apply only when `Role == Code`, `Workspace == Worktree`, `DispatchedAt` and `WorktreePath` are
  present, and the worktree probe is available;
- reuse `AgentFilesService`/the existing git evidence semantics to look for either a commit on the
  task branch after dispatch or changed/untracked worktree content after dispatch;
- no evidence in an available, isolated worktree means `CompletedWithoutProgress`. Preserve the
  delegate's report for diagnosis, mark the task `Failed`, add a Failed event and a new
  `DelegateCompletedWithoutProgress` Error incident, deliver the failure reason to the parent, and
  publish the task update. Do not merge the branch or start another delegate;
- release the finished ephemeral delegate through the same ownership-safe release path used by
  terminal API errors. Keep the unmerged worktree/branch for inspection; no successful landing or
  cleanup is implied by this failure;
- fail open when git evidence is unavailable. The guard is a proof of *zero* work, not a reason to
  turn a failed git probe into a false task failure.

The failure reason must state that the delegate reported completion but Antiphon observed no
post-dispatch worktree progress. It must include the worktree path and the zero commits/changes
facts, not infer why the agent made no progress.

This check deliberately runs on an explicit task completion, not on a bare Herdr status. It
therefore catches a false terminal success without converting normal idle panes, long-running
turns, or a session's ordinary `done` status into failures.

### 3. Reuse CARD-0256's no-repeat recovery contract

Add `CompletedWithoutProgress` beside `AuthenticationRequired` in `AgentTaskFailureCode`. No EF
migration is needed: `FailureCode` is already a nullable integer column and only enum members are
being added.

Generalise `AgentTaskService.FindStoppedBeforeFirstPromptRepeatAsync` and its reason/logger names
to a launch-failure-repeat predicate. It must retain the current exact family key—same nullable
`CardId`, trimmed goal, task kind, role, and agent kind—but accept all explicitly guardable failure
codes:

- `StoppedBeforeFirstPrompt` (unchanged CARD-0256 behaviour);
- `AuthenticationRequired`;
- `CompletedWithoutProgress`.

`CreateAsync` and `RetryAsync` continue to turn a match into a `Blocked` row, a `Blocked` event,
an immediate parent note, and `AgentTaskChanged` before the dispatcher can claim it. The reason
names the prior task and its actual failure code. It must state that no process or worktree was
started. Do not add a same-kind bypass in this card; fixing a proxy/key route or choosing a
different agent kind is an explicit next decision, never a background recovery.

### 4. Operator guidance and status semantics

Add a compact paragraph to `server/Bundles/orchestrator.md` for
`AuthenticationRequired` and `CompletedWithoutProgress`: surface the blocked/failed item, inspect
the recorded terminal evidence, and choose an allowed recovery explicitly. It must not advise
pasting, logging, or repeating credentials, and it must not prescribe a replacement launch.

Do not change `HerdrEventPumpService`, `SessionRunnerRuntime.ApplyHerdrAgentStatus`, or
`HerdrStatusCorroborationService` beyond a regression pin. Their current separation is the safety
property: Herdr's status is cached/displayed and can raise a corroboration Warning, but it neither
settles a task nor starts recovery by itself.

## Tests

Run the backend and runner test projects sequentially with the repository's TUnit command form
and isolated output paths; do not run the full build for this planning pass.

1. `tests/Antiphon.SessionRunner.Tests/CodexTranscriptNormalizerTests.cs`
   - Add literal and wrapped fake 401 task-complete payloads containing
     `LiteLLM Virtual Key expected` (no real key). Assert `TurnEnd`, `IsApiError`, status 401, and
     the diagnostic text survive normalization.
   - Retain the current HTTP-400 malformed-model fixture to prove the broad turn-boundary contract
     is unchanged.
2. `tests/Antiphon.Tests/Application/ApiErrorClassifierTests.cs`
   - Assert an otherwise unrecognised Codex class with status 401 is `NeedsHuman`.
   - Assert the existing known authentication class remains `NeedsHuman` and a 400 remains on its
     existing path.
3. `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs`
   - Seed a Codex marked API-error turn with `invalid_request_error`, status 401, and the LiteLLM
     diagnostic. Assert immediate `Failed`, `FailureCode.AuthenticationRequired`, captured bounded
     reason, no scheduled recovery, `ApiErrorTurnDied` at Critical severity, parent failure
     delivery, delegate release, and no replacement task/session.
   - Seed a `Code` worktree task whose marked final report says done while the task branch has zero
     post-dispatch commits and zero changed/untracked files. Assert `Failed`,
     `CompletedWithoutProgress`, no merge, the new attention incident, parent delivery, and no
     replacement.
   - Prove a post-dispatch commit or changed file permits normal success, and that unavailable git,
     `Shared` workspaces, and non-Code roles do not receive the new failure.
4. `tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs` and attention coverage
   - Parameterise the existing CARD-0256 repeat shape for each guardable code. Assert create and
     retry become `Blocked` before dispatch, leave `AgentSessionId`/worktree empty, write the
     parent note/event, and appear as a blocked attention item. Assert the identical work offered
     to a different allowed agent kind is not blocked.
5. `tests/Antiphon.Tests/Application/HerdrStatusCorroborationServiceTests.cs` (or its existing
   focused test class)
   - Pin that a `done` status alone does not settle, fail, release, or replace a delegate. The
     Codex error `TurnEnd`, not the Herdr status, is the causal input for the 401 path.

Suggested focused commands after implementation:

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0286/ -- --treenode-filter "/*/*/CodexTranscriptNormalizerTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0286/ -- --treenode-filter "/*/*/ApiErrorClassifierTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0286/ -- --treenode-filter "/*/*/AgentTaskReplyIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0286/ -- --treenode-filter "/*/*/AgentTaskServiceIntegrationTests/*"
```

Run `Antiphon.SessionRunner.Tests` and `Antiphon.Tests` one after the other, then remove the
isolated `bin-card0286/` outputs after verification.

## Acceptance-criteria trace

| Required behaviour | Design coverage |
| --- | --- |
| Detect terminal no-first-progress work | Existing empty-stopped guard remains; API-error `TurnEnd` handles rejected first prompts; a completed Code worktree now requires objective post-dispatch work evidence. |
| Capture and surface error/completion reason | Codex normalizer preserves the 401 diagnostic; the existing API-error failure/event/incident/parent-delivery path is reused; clean completion writes an explicit failure/event/incident/parent note. |
| Mark blocked/failed and recover without a manual status request | Both terminal shapes fail immediately on their evidence; their same-kind family is blocked before a retry launches. The attention feed and parent note carry the reason. |
| Preserve one-agent/one-worktree recovery | No Herdr-status-triggered action, no automatic retry or replacement, no success merge after a clean completion, and the repeat guard blocks before process/worktree creation. |

## Implementation surface

| File/area | Change |
| --- | --- |
| `src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs` | Preserve structured/literal API-error diagnostic text and HTTP 401 on Codex `task_complete`. |
| `server/Application/Services/ApiErrorClassifier.cs` | Map status 401 to `NeedsHuman`. |
| `server/Application/Services/AgentTaskReplyService.cs` | Assign the authentication failure code; add the Code-worktree explicit-completion evidence check and its terminal failure/notification path. |
| `server/Application/Services/AgentFilesService.cs` (only if its current probe cannot express the required zero-evidence result) | Expose a read-only, task-worktree progress verdict; do not change shared-work attribution semantics. |
| `server/Domain/Enums/AgentTaskEnums.cs`, `AgentIncidentKind.cs` | Add the two task failure codes and the no-progress incident kind. No schema migration for enum additions. |
| `server/Application/Services/AgentTaskService.cs` | Generalise the CARD-0256 repeat guard while preserving its exact task-family key and pre-dispatch block. |
| `server/Bundles/orchestrator.md` | Add safe, evidence-led guidance for the two terminal failure codes. |
| Focused runner/application tests | Pin normalization, classification, failure delivery/attention, clean-worktree rejection, repeat blocking, and the no-action Herdr status contract. |

## Out of scope

- Changing credentials, profiles, LiteLLM configuration, or secret storage.
- Treating screen text, a Herdr event, or `agent_status: done` as task-completion evidence.
- Parsing a free-form brief to invent an expected filename.
- Auto-retrying, auto-switching providers, auto-merging, or silently launching a replacement.
- Changing CARD-0248's deferred-report boundary/nudge gates or CARD-0256's empty-stopped evidence
  classification beyond reusing its explicit repeat-blocking mechanism.
