# Antiphon-Decisions failed to launch (2026-11-10)

Agent `9a6616c9-0777-4b16-962d-e697f8662942` (Antiphon-Decisions, ClaudeCode, High, alwaysOn, remote control on, assignmentPolicy Paused, cwd `C:\Users\lndco\antiphon-agents\decisions`) is Running again. Live session `7b80d3e0-f9c3-4093-86c3-39e2ee95556c` since 2026-11-10 12:41:12Z. Continuity hold cleared. No prompt was sent. AssignmentPolicy was left Paused. No second agent row. No product code change.

## Cause

The first launch died on Claude's trust dialog for a new folder that is not a git repo.

Session `818c1fce-2ada-45aa-b62d-61b41fa4e2d4` started at 11:39:42Z. The pty host launched `c:\users\lndco\.local\bin\claude.exe`. The raw ANSI shows the Quick safety check for `C:\Users\lndco\antiphon-agents\decisions`, including "Yes, I trust this folder". Replaying that first-launch slice through the shipped `TerminalScreen` (120x30) classifies `TrustFolder` on the mid-dialog frame, roughly characters 1030–1270. The live startup snapshot did not. The composer probe runs only when `ClaudeBlockingPromptDetector.Detect` returns null, and that is the branch in the stack.

Winning line, server log `C:\src\Antiphon\server\logs\antiphon-20261110.log`:

```
2026-11-10 11:39:54.049 +00:00 [WRN] Failed to start interactive agent session "818c1fce-2ada-45aa-b62d-61b41fa4e2d4"
Antiphon.Server.Application.Services.ProviderDialogInputRefusedException: Automated Claude input withheld: observation-unknown
   at ProviderDialogObservationService.Request ... ProviderDialogObservationService.cs:line 41
   at RunnerTerminalSession.WriteStartupAsync ... RunnerTerminalSession.cs:line 160
   at ClaudeStartupReadiness.ProbeWriteAsync ... ClaudeStartupReadiness.cs:line 93
   at ComposerInputProbe.RunAsync ... ComposerInputProbe.cs:line 123
   at ClaudeStartupReadiness.RunAsync ... ClaudeStartupReadiness.cs:line 97
```

Line 41 throws when the dialog intent is None. The pty log records the child exit at 11:39:53.498Z, code 1, reason `KilledByRequest`. Incident Crash at 11:39:57Z: "Session died (process exited with exit code 1; Antiphon stop requested: Automated Claude input withheld: observation-unknown)." Runner log 11:39:56.597Z: the child exited without a transcript although input had been delivered. Incident TranscriptBindFailed, reason TranscriptMissing.

Create at 11:39:35.158Z logged only `AgentWorkspaceProvisioner` writing the CLAUDE.md floor. There is no "Seeded Claude trust" line. Ordinary workspace provisioning writes CLAUDE.md and does not call `ClaudeProjectTrust.Seed`. Standing specialists do. The decisions directory has CLAUDE.md and no `.git`, so Claude keys trust on the folder path itself. Other standing agents already have `hasTrustDialogAccepted` on their project keys.

A nearby observation-unknown stack at 11:39:52Z and 11:40:26Z is session `df3b5125-d4de-4f31-b155-2674d4740a98` (`RecoverRunningDispatchBrief`), a different agent.

## Why the supervisor stopped

Paused does not suppress AlwaysOn supervision. The supervisor did restart this agent:

- 11:39:37.135Z restart attempt 1 scheduled for 11:39:41Z (backoff 5s). That attempt is the original start.
- 11:39:57.268Z restart attempt 2 scheduled for 11:40:07Z (backoff 10s).
- 11:40:16.827Z supervised restart attempt 2.

Attempt 2 resumed the same session id. Claude printed "No conversation found with session ID: 818c1fce-2ada-45aa-b62d-61b41fa4e2d4" and exited code 1 (`ProcessExited`, 11:40:21.958Z). Server line:

```
2026-11-10 11:40:27.026 +00:00 [WRN] Failed to start interactive agent session "818c1fce-2ada-45aa-b62d-61b41fa4e2d4"
AgentSessionService+ResumeTargetMissingException: Claude resume session was not found. Continue from last context in this worktree or start a new Claude session.
   at AgentSessionService.LaunchInteractiveProcessAsync ... AgentSessionService.cs:line 716
```

`RestartFailurePolicy` classifies `ResumeTargetMissingException` as `ContinuityUnavailable` and does not climb the backoff for that kind. Incident 11:40:27Z StandingContinuityHeld: "Standing conversation 818c1fce-2ada-45aa-b62d-61b41fa4e2d4: NativeSessionMissing. Inspect and repair, select an owned conversation, or explicitly start fresh." No attempt 3 was scheduled. `POST /api/agents/{id}/start` with `{}` is refused while `ContinuityHeldAt` is set. The accepted recovery is `{"fresh":true}`.

## Checked and cleared

- Details and SystemPromptAppend. The append has no newline, backtick, or dollar. The cardless-start line says Details is not delivered as a prompt. Details is the standing job.
- Remote control on the original session. The death is inside startup readiness, before remote-control setup. Session 818c1fce has no RcDegraded or RemoteControlArmSuppressed incident.
- Model profile. Kind ClaudeCode, level High, empty modelId. The process started.
- AssignmentPolicy Paused. The two restart lines above show the supervisor was active. The stop is the continuity hold.
- Herdr. `sessionBackend` is PtyHost. Workspace and tab labels are empty. No herdr sidecar exists for session 818c1fce. Ownership evidence Stamped is recorded and is not the failure mechanism.

## Fix

The project key `C:/Users/lndco/antiphon-agents/decisions` already had `hasTrustDialogAccepted` true before the restart. That entry matches a Claude-written stub, not a product `Seed` at create time (Seed sets only the boolean). `.claude.json` was not rewritten. No agent field was patched. AssignmentPolicy stays Paused so the agent does not count as a dispatch slot.

At 12:41:12Z, `POST /api/agents/9a6616c9-0777-4b16-962d-e697f8662942/start` with body `{"fresh":true}`. Incident StandingFreshSelected: accepted explicit fresh conversation `818c1fce-2ada-45aa-b62d-61b41fa4e2d4` to `7b80d3e0-f9c3-4093-86c3-39e2ee95556c`. Runner status Running, backend pty-host, child pid 418772, no exit line. The new ANSI has no trust dialog and no missing-session text. Agent status Running, working false, continuityHeldAt empty, continuityReason empty, supervision not suspended, nextRestartAt empty. Nothing was sent to the session.

Residual on the live session, after it was already Running:

- 12:41:31.237Z remote-control setup started (budget 60000ms).
- 12:41:46.375Z "Remote-control rename skipped ... armed=Unarmed menu=false remnant=false". Incident RcDegraded, reason RemoteControlNotArmed: "Remote control did not observe an armed bridge; /rename was skipped."
- 12:45:36Z incident RemoteControlArmSuppressed: "Remote control was never armed; an automatic arm is already reserved or vetoed for this generation."
- 12:42:35Z TranscriptBindFailed, reason AdoptionRefused. The runner refused other sessions' JSONL files. Runner `transcriptBound` is false and `transcriptUnboundReason` is missing. The session is idle until the first prompt.
- `restartBackoffFailures` is still 1 from the first death.

The session was left Running. No prompt, including `/remote-control`, was sent.

## Next standing agent

Before the first start of an ordinary always-on agent in a new non-git folder, set `projects["<cwd with backslashes flipped to forward slashes>"].hasTrustDialogAccepted` true in the runner user's `%USERPROFILE%\.claude.json`. `ClaudeProjectTrust.Seed` does that. Writing CLAUDE.md does not. If the first launch dies before a transcript, do not resume that session id. While the NativeSessionMissing hold is set, `{}` is refused; start with `{"fresh":true}` or a selected owned resume. The fixer sends no first prompt. Leave AssignmentPolicy Paused when the agent must not count as a dispatch slot.
