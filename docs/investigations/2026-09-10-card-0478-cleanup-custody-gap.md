# CARD-0478: cleanup custody needs a Plan decision

Code task `62229bff`, inspected base `4fbb8e77`, 2026-09-10.

Implementation is blocked at S3's process-ownership requirement. No runtime,
schema, CLI, client, or active instruction change is included in this checkpoint.
S1-S5 are not complete. Ordinary V/R has not run. PC-1 through PC-182 remain
pending for Role Mutation after the complete feature is landed and deployed.

## The decision needed

Approve a concrete producer of durable verification command/process custody and
descendant-exit evidence, including its runner/backend scope, before continuing
Code. Prefer a runner-owned receipt tied to the accepted session generation and
the verification task; unknown or unsupported custody must retain the worktree.
This requires a planned runtime/transport extension and corresponding TestDesign
coverage, rather than inference in the cleanup endpoint.

The existing plan requires terminal tasks, no live owner or commands, exact
creation identity and conservative handling of uncertain children. Its S3
verification includes G-89/G-90/G-110/G-119/G-120. A cleanup implementation that
always refuses when command custody is unknown could preserve safety, but would
not satisfy V-6's successful cleanup acceptance for restored terminal runs.

The scope instruction at line 193 of
[the implementation plan](../superpowers/plans/2026-09-10-card-0478-mutation-after-land-plan.md)
is explicit:

> A new required worktree/cleanup mechanism beyond these narrow seams returns to Plan rather than bypassing existing guards.

D-9 also requires the selector, settlement guards, cleanup, instructions and tests
to ship together. This checkpoint is a design return, not the implementation's
ordinary Review handoff or authority to deploy the reordered workflow.

## Evidence from the current implementation

| Existing surface | What it establishes | What it does not establish |
|---|---|---|
| `server/Infrastructure/Git/RepositoryChildJournal.cs`, `BeginAsync` / `HasUnfinishedAsync` | Durable start intent and PID/start-time records for commands launched through the application-owned Git/build infrastructure. Unacknowledged records fence repository admission even when the root PID has gone. | Commands launched by a provider's shell/tool execution do not pass through this journal. No journal entries therefore do not establish that a Mutation worker has no surviving commands. |
| `server/Infrastructure/Git/LandingGit.cs` and `WorktreeManager.cs`; `LandingVerifier.cs` | Journal producers for their owned command executions. | These are not execution wrappers for a delegate's arbitrary `dotnet`, PowerShell or child test processes. |
| `server/Infrastructure/Agents/WindowsZombieProcessCensus.cs`, `QueryProcesses` | WMI returns current PID, parent PID, process creation time and command line. A failed query throws. | The implementation sets `Cwd: ""`. A current snapshot is not durable ancestry history; an orphaned grandchild need not name the task path or retain a parent that still exists in the snapshot. |
| `server/Application/Dtos/SessionRunnerDtos.cs`, `SessionRunnerSessionDto` | Session PID, status, start time, exit reason, host PID and backend information. | No durable receipt that all processes owned by that accepted session generation have exited. |
| `src/Antiphon.Agents.Pty/PtyAgentRunner.cs`, `KillAsync` | Calls connection/job termination and waits for `_exitTcs`, returning whether that top-level exit task completed before timeout. | Does not query and acknowledge an empty descendant job as a durable cleanup receipt. |
| `src/Antiphon.Agents.Pty/ModernConPtyConnection.cs`, `Kill` / `TryTerminateJob` | Attempts job termination and top-level process-tree kill. | `TerminateJobObject`'s return value is discarded. Best-effort kill is not a persisted, generation-bound descendant-exit verdict. |
| `src/Antiphon.PtyHost/PtyHostServer.cs`, `KillMessage` branch | Awaits the session kill call. | Does not forward its Boolean result as descendant custody evidence. |
| `src/Antiphon.SessionRunner/HerdrPaneChild.cs`, `KillAsync` | Supports detach, retained-pane and best-effort child/pane close outcomes. The unexpected-foreground-process arm raises `PaneLeftOpen`. | A terminal session does not imply the pane and all potentially relevant children are gone. |
| `server/Application/Services/AgentTaskReplyService.cs`, `ReleaseDelegateAsync` | Preserves pool ownership or asks the session stopper to kill; logs kill exceptions. | A terminal task or agent removal is not stronger evidence than the underlying runtime observations. |

This is a conclusion from reading the implementations, not a measured claim that
a current worker leaked a process. No live process was stopped and no provider
was dispatched to investigate it.

## Why the apparent narrow fixes are insufficient

Checking task/session terminal state, the repository journal and a WMI snapshot
would still admit this counterexample: a provider tool launches a child that
launches a long-lived grandchild; the intermediate processes exit; the surviving
process has a relative command line without the snapshot path. The application
did not journal its start. The remaining process cannot be attributed from those
inputs with the certainty required by G-119/G-120.

A worker-written `commandsAwaited: true` field or an optional list of process IDs
would be an assertion by the worker, not an independently observed complete
ownership inventory. Such a manifest can record restoration and evidence but
cannot silently become the missing OS custody authority. PID liveness alone is
also explicitly insufficient in the existing repository child journal.

The PTY Job Object provides a promising existing runtime primitive. A revision
should specify what a successful empty-job/termination observation means, how it
is persisted and correlated across host/server restarts, and what happens for
Herdr and unavailable/legacy runner evidence. Keep unsupported cases as residue;
do not introduce a kill merely to authorize worktree deletion.

## Required Plan/TestDesign amendment

1. Identify the authoritative custody producer, persistence boundary, accepted
   session generation, and exact task association. Define which descendants it
   covers and how detached/breakaway or unobservable children are handled.
2. Define successful, failed and unknown custody outcomes for supported backends,
   including restart, PID reuse, top-level exit with surviving descendants and
   partial termination. State whether a first version deliberately restricts
   successful cleanup to a backend with proven ownership evidence.
3. Feed this evidence through `IWorktreeRemovalEvidence`; reload it under the
   repository lease immediately before removal. A report, transport ACK or
   terminal task status must not substitute for the ownership receipt.
4. Extend the guard/PC census for independently bypassable new checks, and amend
   G-89/G-90/G-110/G-119/G-120 and V-6 with real positive and unknown-child cases.
   Keep all existing 182 PCs pending; do not relabel a contract assertion as a
   runtime ownership test.
5. Resume S1-S5 Code, ordinary V/R, commit/push, then ordinary Review. Retain the
   existing confirmed-land/deploy prerequisite for this feature's Mutation run.

## Draft custody

The incomplete exploratory implementation was removed from the worktree after
being saved to:

`C:\Antiphon\worktrees\card-task-62229bff\.antiphon\task-62229bff-unfinished.patch`

It is local scratch, not part of the committed deliverable, and is **not build- or
test-verified**. It contains partial S1/S2 changes and an inadequate proposed
cleanup evidence reader. Do not apply or ship it wholesale. In particular, its
worker manifest/current-census approach does not resolve the gap above, and its
public cleanup command has no implemented endpoint. No migration or ordinary
guard fixture was generated. The original checkout was clean before this task;
only this task's draft changes were removed.

Validation of this checkpoint is limited to the document diff and confirming
that no runtime draft remains. There are no build/test pass claims.
