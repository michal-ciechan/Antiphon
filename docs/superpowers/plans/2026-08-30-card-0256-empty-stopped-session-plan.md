# CARD-0256 — Empty `Stopped` delegate sessions: truthful failure and repeat guard

**Date:** 2026-08-30  
**Card:** CARD-0256  
**Decision:** Implement this independently of CARD-0259/CARD-0260. Those cards may remove one
plausible cause of the observed Grok launches, but they do not make a `Stopped`, transcript-empty
session attributable to an operator, nor do they prevent a repeated bad dispatch.

## 1. Verified current behaviour

`AgentTaskDispatcher.FailDeadSessionTasksAsync` currently classifies every dead session after the
runner-and-grace safety gates. At the current fallback around lines 966–970 it uses the session's
`FailureReason`, or, for **any** `SessionStatus.Stopped` row with no reason, writes:

> stopped before the task settled, with no failure reason — an operator ended it

The transcript is not considered by that fallback. `AgentTaskLiveness` reinforces the same
assumption in its `Stopped counts` documentation.

That is not a valid inference. `AgentSessionService.KillAsync` leaves a successfully killed session
`Stopped` with a null failure reason, but `AgentSessionRuntime.CloseSessionOnExitAsync` and
`SessionReconciliationService` also write `Stopped` for a clean process exit (exit code 0). Neither
path persists *who* requested the stop. The dead-session sweep receives enough evidence to prove
that no report will arrive; it receives no durable evidence that an operator caused it.

The existing zero-transcript bind-refusal recovery must remain before failure classification.
`TryRecoverBindRefusalAsync` can positively recover work that happened in a different transcript,
but it does **not** prove that every otherwise empty session was a bind refusal.

## 2. Failure vocabulary and evidence boundary

Do not name a cause that the row cannot prove. The initial implementation should expose one task
failure code, `StoppedBeforeFirstPrompt`, for an otherwise unclassified terminal `Stopped` session
with zero `TranscriptEntries`. Its human reason must say that Antiphon observed no prompt before the
session stopped and that the stop origin was not recorded; it must not contain “operator ended it”.

Keep the already-observable cases distinct in the message, without turning arbitrary text into a
new taxonomy:

| Evidence | Truthful outcome |
| --- | --- |
| Session's existing `FailureReason` (launch timeout, runner absence, non-zero process exit, herdr error) | Preserve that concrete reason. |
| No task session id / session row missing / inconsistent `EndedAt` | Existing liveness description. |
| `Stopped`, zero transcript entries, no persisted stop origin | `StoppedBeforeFirstPrompt`; origin unknown. |
| Positive unbound-transcript evidence | Existing recovery; do not fail the task. |

In particular, a bind refusal and a Grok TUI that cleanly exits before the first submit are not
separable today. A runner that never starts normally already reaches a `Failed` session with a
launch reason, but a missing row is only “session row missing.” Those distinctions are deliberately
not renamed as a specific Grok/provider defect.

Add a small persisted `SessionTerminationSource` enum on `AgentSession` to make the operator case
observable going forward: `Unknown` (including legacy rows), `OperatorRequest`, `SystemRequest`,
and `ProcessExit`. `AgentControlService.StopAsync` supplies `OperatorRequest`; dispatcher cleanup
and pool retirement supply `SystemRequest`; runtime/reconciliation record `ProcessExit` only when a
prior request source is absent. `AgentSessionService.KillAsync` must save the source before asking
the runner to kill, so an exit-event race cannot erase it. Then the classifier may use a narrowly
worded `OperatorRequestBeforeFirstPrompt` message only when that durable source is present. It still
must not pretend it knows whether the operator interacted with the TUI rather than the API.

Add nullable `AgentTaskFailureCode` on `AgentTask` (initial member:
`StoppedBeforeFirstPrompt`) rather than querying prose. This is the durable, machine-readable input
to the repeat guard; all existing failures remain null/legacy. Create the EF migration through the
CLI and update the model snapshot.

## 3. Stop the second identical launch before dispatch

Put the guard in `AgentTaskService`, not in the dispatcher: dispatcher time is after a queued task
can have acquired an agent or a Worktree. Run it in both paths that can create a repeat:

1. `CreateAsync`, after the card binding and agent kind are resolved but before adding a runnable
   task.
2. `RetryAsync`, before `RequeueAsync` can stop/relaunch the delegate.

The family predicate is intentionally exact and provider-aware: same nullable `CardId`, trimmed
goal text, `AgentTaskKind`, role, and `AgentKind`, with an earlier `Failed` task whose
`FailureCode` is `StoppedBeforeFirstPrompt`. Do not use `RootTaskId`: separate requests from a
channel-bound standing parent get different roots, which is precisely how the two observed Grok
attempts escaped a root-only guard. Include `AgentKind` so selecting `ClaudeCode` is an intentional
alternative, not another blocked Grok retry.

On a matching repeat, create (or change the retrying row into) a `Blocked` task with a `Blocked`
event. Its reason names the previous short task id and failure code, states that no Grok process or
worktree was started, and asks the parent to offer ClaudeCode or resolve the launch incident. Publish
the normal `AgentTaskChanged` event and enqueue the same structural note to `ParentSessionId` when
one exists. A `Blocked` task already projects as Critical `BlockedQuestion` in `AttentionService`,
so no new client page or bespoke alert sink is needed. This is stronger than prose: it remains
visible if the parent forgets the rule or is a warm session that predates the new bundle.

Do not add an automatic same-kind bypass. A deliberately repaired Grok route can be tried only
through an explicit later acknowledgement/retry surface, designed with the operator who fixes that
route; silently weakening the first implementation would recreate the failure loop. A different
agent kind is not a bypass and remains allowed immediately.

## 4. Channel-parent guidance

Add one compact, code-oriented paragraph to `server/Bundles/orchestrator.md`: when a delegate
reports `StoppedBeforeFirstPrompt` or the repeat guard blocks it, treat it as a launch incident,
not a failed work attempt; do not re-dispatch the same agent kind, surface the blocked item, and
offer a ClaudeCode delegate. The paragraph must point to the failure code rather than attempting to
list Grok-specific theories.

The `Blocked` task plus queued parent note are the enforcement and visibility mechanism. The bundle
is only a decision aid for choosing the next provider; it is not relied on as the retry barrier.

## 5. Worktree `AllowedRoots` paper-cut

This is a real, adjacent usability defect and belongs in this pass because it misdirected the same
retry. The 422 is correctly enforcing the security boundary: an explicit
`-Dir C:\Antiphon\worktrees\card-task-...` is outside the caller's Coesite tree and configured
roots. Do **not** add the transient worktree root to `Delegation:AllowedRoots`.

When `WorkspaceMode.Worktree` is requested and directory resolution rejects an explicit directory,
have `AgentTaskService` augment the validation message: a Worktree task takes the source repository
as `-Dir` (or inherits it), and Antiphon creates a new worktree at dispatch. The message should show
the valid shape, `-Dir <repo> -Worktree`, while retaining the ordinary instruction to add an allowed
root only when `<repo>` itself is intentionally outside the boundary. No workspace authorisation
rule changes.

## 6. Test plan

Extend the shared-database, non-parallel
`tests/Antiphon.Tests/Application/AgentTaskDeadSessionReconciliationTests.cs` harness:

1. Seed a scripted `AgentKind.Grok` worker whose session is `Stopped`, has zero
   `TranscriptEntries`, and has no `OperatorRequest` source. After the existing grace and runner
   gates, assert `Failed`, `FailureCode == StoppedBeforeFirstPrompt`, a reason without the operator
   claim, a parent completion note carrying the named code, and no kill by the sweep.
2. Seed the same shape with `OperatorRequest` and assert the only operator wording is backed by
   that source. Cover a clean process exit and a legacy/unknown source separately so neither is
   promoted to an operator stop.
3. Retain the existing bind-refusal recovery test to prove a positive recovery still wins before
   the new classification.

Add focused session lifecycle tests around `AgentSessionService`, `AgentSessionRuntime`, and
`SessionReconciliationService` for source persistence and race-preserving behaviour.

Extend `AgentTaskService`/attention integration coverage with an earlier failed Grok task and a
second exact create/retry. Assert that the repeat is `Blocked`, has no agent session or worktree,
emits the `Blocked` event and parent note, and appears in `AttentionService` as a critical blocked
item. Assert that otherwise identical `ClaudeCode` work is created normally. Add the Worktree 422
test for the new source-repository guidance while retaining the rejected path.

Run the focused TUnit classes with isolated output paths, for example:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0256/ -- --treenode-filter "/*/*/AgentTaskDeadSessionReconciliationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0256/ -- --treenode-filter "/*/*/AgentTaskService*/*"
```

## 7. Implementation surface

| File/area | Change |
| --- | --- |
| `server/Domain/Enums/` | Add `SessionTerminationSource` and `AgentTaskFailureCode`. |
| `server/Domain/Entities/AgentSession.cs`, `AgentTask.cs`, migration | Persist stop source and nullable task failure code. |
| `AgentSessionService.cs`, `AgentControlService.cs`, `AgentSessionRuntime.cs`, `SessionReconciliationService.cs` | Record and preserve the source at every explicit-stop and observed-exit path. |
| `AgentTaskDispatcher.cs`, `AgentTaskLiveness.cs` | Replace the false `Stopped` fallback after bind recovery with the evidence-based classifier and update the stale documentation. |
| `AgentTaskService.cs` | Apply the repeat guard at create/retry, make it a visible blocked task/parent note, and improve Worktree rejection guidance. |
| `AttentionService.cs` | Reuse its generic blocked-task projection; change only if a focused test exposes a missing parent/session detail. |
| `server/Bundles/orchestrator.md` | Add the provider-switch guidance keyed to the structural failure code. |
| Relevant TUnit classes | Pin empty scripted Grok stop, explicit stop source, repeat blocking/attention, and Worktree diagnostic behaviour. |

CARD-0259/CARD-0260 should still land their LLM project/key inheritance and proxy-routing changes.
They may cause the real Grok session to bind and record its first prompt, but CARD-0256 neither
waits on nor assumes that result; it makes every remaining empty terminal session truthful and
non-repeatable.
