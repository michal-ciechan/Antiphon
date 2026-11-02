# 2026-11-02 Code task 62afb5fc died: "Automated Claude input withheld: stale-observation"

Read-only Debug (task a3ce2552). No build, no code change.

## Verdict

This is a recurring launch defect, not a one-off. The Claude startup readiness probe sends each
keystroke through the runner's guarded conditional input. That write is fenced on the exact output
sequence of a snapshot taken just before it. Claude Code 2.1.280 keeps painting its footer hint
(`install gh for PR status · ← for agents`) and clearing it during the first seconds. When one
output byte lands between the snapshot and the write, the runner refuses with `stale-observation`,
which is a definitive refusal that sends zero bytes. The startup path never retries that refusal.
`ProviderDialogInputRefusedException` escapes `WaitForReadyAsync`, the launch catch fails the
session with the exception message, and the task settles Failed with "Session died".

## What `stale-observation` means

- `src/Antiphon.SessionRunner.Contracts/ConditionalInput.cs:12`: one of the conditional-input
  outcomes (CARD-0514 D-8, CARD-0680).
- `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:1674-1708`: the runner returns it when
  `session.LastSequence != request.ExpectedLastSequence`. It checks before the write and again inside
  the screen-lock `Fence`. `CheckDialogInput` (`:2758-2762`) also returns it when the runtime epoch
  or input revision moved. The refusal comes before the first backend byte.
- `server/Application/Services/ProviderDialogObservationService.cs:49-62`: `RequireWritten` turns
  every outcome other than `written` or `unknown` into
  `ProviderDialogInputRefusedException("Automated Claude input withheld: <reason>")`.

## Where it fired

- `server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs:121-138`:
  `WriteStartupAsync` takes `GetSnapshotAsync`, builds the request with
  `snapshot.Dialog.OutputSequence`, and calls `RequireWritten`. It has no retry.
- `server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs:161-215`: `WaitForReadyAsync`
  passes `_terminal.WriteStartupAsync` as the write primitive to `ClaudeStartupReadiness.RunAsync`.
  That covers the trust answer, the effort answer, the probe token and the Ctrl+U clear.
- `src/Antiphon.Agents.Pty/ClaudeStartupReadiness.cs` catches only `OperationCanceledException`, and
  `ComposerInputProbe.RunAsync` has no catch, so the conflict propagates.
- `server/Application/Services/AgentSessionService.cs:2710` (`WaitForReadyOrThrowAsync`) does not
  catch it either. The launch `catch (Exception ex)` fails the session with `ex.Message`, which is
  the session failure text that the task failure quotes.

## What the session was doing

Task 62afb5fc (CARD-1666, server2, ClaudeCode High) was dispatched at 22:58:28.27Z. Session
424eaa48 went Failed at 22:58:46.12Z, 18 s later. The transcript is empty (`lastSequence` 0, no
UserPrompt) and `tokensIn` is 0. The buffer (`GET /api/sessions/{id}/buffer`, `lastSequence` 12)
shows a healthy composer. The footer hint is painted, the DA query `ESC[>0q ESC[?u ESC[c` follows,
and the probe token **`zz424eaa48` is still rendered in the composer**. More output comes after it:
the footer-hint area is overwritten and an `ESC[K` line clear is sent. So the token write
succeeded, and a later probe write was refused because the TUI's own repaint advanced the sequence.
That later write is most likely the first Ctrl+U clear. The boot prompt was never typed.

## Recurrence

- The four Code siblings dispatched at the same moment (c7fd24a5, 39e2eabd, c1cb4001, 344b9bdc,
  all server2 ClaudeCode High) are Running/Working with transcripts. The race does not hit every
  launch.
- `GET /api/agent-tasks?since=2026-11-01T23:00:00Z&status=Failed` returned 2 rows. Only 62afb5fc
  has this text.
- `?since=2026-10-20T00:00:00Z&status=Failed` (79 rows, each detail read) found an **earlier
  identical failure: 5c904345** (CARD-1609 plan re-review, Review, server2). It was dispatched at
  2026-10-30T03:46:36Z, and session ceb93b18 went Failed at 03:46:52Z, 16 s later. Its buffer
  (`lastSequence` 10) shows the same composer and the same late `install gh for PR status` hint
  with no token visible. In that case the refused write was earlier, probably the token write
  itself.
- Both failures are on server2 runner launches, under Claude Code v2.1.280, through the
  conditional-input startup path. The remote-control recovery path already treats
  `stale-observation` as retryable (`RemoteControlRecoveryService.cs:534`, `:650-653`). The
  startup path does not.
- No test pins the startup path under a `stale-observation` refusal. `WriteStartupAsync` appears
  only in `FakeAgentProtocolAdapter`/`RemoteControlMaintenanceQueueTests`.

## Work lost

None. `origin/feat/card-task-62afb5fc` is `907edaebbd301af95629c31095f1b530803baea2`, the dispatch
base. The task produced no commit, and its cost is $0.

## Recommendation

Minimal fix (Code, server-only, `RunnerTerminalSession.WriteStartupAsync`). On a
`stale-observation` refusal, re-snapshot and re-send the same startup input. Bound it to a few
attempts, use a short poll delay, and stay within the existing readiness deadline. Run the
dialog/intent classification again on each new snapshot so the dialog guard is kept. The retry is
safe because `stale-observation` is a zero-byte refusal by contract. `unknown` (uncertain
transport) and `modal-blocked`/`observation-unknown`/`generation-mismatch` must keep their current
behaviour. A red test should drive `RunnerClaudeAdapter.WaitForReadyAsync` with a fake runner
client that returns `stale-observation` once on the token or Ctrl+U write and `written` on the
next. Today that launch fails; after the fix it should be ready.

Alternative (wider, not recommended first): `ClaudeStartupReadiness` catches
`ProviderDialogInputRefusedException(stale-observation)` and loops like `InterruptedByModal`. That
discards the probe proof and re-probes. It costs more token writes against `MaxWrites`.

Operator action now: re-dispatch 62afb5fc's work (CARD-1666) as a fresh Code task. No cleanup is
needed. The branch is at base and the session is already Failed.
