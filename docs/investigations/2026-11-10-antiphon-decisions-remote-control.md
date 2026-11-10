# Antiphon-Decisions remote control did not arm (2026-11-10)

## Cause

`remoteControlName` is not a stored field and did not need a PATCH. On start, `AgentControlService` sets it from the agent name when `remoteControlEnabled` is true (`remoteControlName = remoteControl ? agent.Name : null`). Antiphon then types `/remote-control` and, only after the bridge is armed, `/rename` to that name.

Antiphon-Decisions (`9a6616c9-0777-4b16-962d-e697f8662942`), Antiphon-Orchestrator and ClaudeBot-Antiphon are all ClaudeCode, alwaysOn, `remoteControlEnabled=true`, PtyHost, and the same TUI profile `claude` (`904d6f65-08d4-4dee-b849-337a97de2aaa`). Decisions has an empty `modelId` and `AssignmentPolicy=Paused`. Neither one gates the arm. The other two agents are already bridged on this machine, so this was not a claude.ai sign-in gap.

Session `7b80d3e0-f9c3-4093-86c3-39e2ee95556c` started fresh at 12:41:12Z. Setup typed `/remote-control` into the composer. Claude Code 2.1.322 opened the slash autocomplete under the box and covered the hint bar. `ClaudeScreen.TryReadComposer` returned false, so the dialog stayed Unknown. The arm waited the 15-second evidence budget and recorded `ArmUnconfirmed` / `observation-unknown` without sending Enter. The bridge stayed Unarmed, so `/rename` was skipped (`RcDegraded` / `RemoteControlNotArmed`). Later health passes could not reserve another automatic arm: an unconfirmed row for this generation vetoes it (`RemoteControlArmSuppressed`).

The trust dialog and the effort picker were already clear. Remote-control setup runs only after ready. The dialog guard did not consume the command body. It withheld the submitting Enter because the palette is not a recognised composer. The transcript is unbound (`missing`) because nothing has been submitted. `inputHistoryKnown` was true, so that was not the refusal.

## Evidence

- `C:\src\Antiphon\server\logs\antiphon-20261110.log`: 12:41:31.237Z remote-control setup starting (budget 60000ms); 12:41:46.375Z rename skipped, `armed=Unarmed`, `menu=false`, `remnant=false`.
- `SessionQueuedMessages` for that session, sequence 1, created 12:41:31.266Z: automatic arm, result `ArmUnconfirmed`, evidence `observation-unknown`, slot closed. Sequence 2, created 12:51:17Z, is still Pending. Its body is the orchestrator prompt and is not copied here.
- Incidents on the agent: `RcDegraded` / `RemoteControlNotArmed` at 12:41:46Z ("did not observe an armed bridge; /rename was skipped"); `RemoteControlArmSuppressed` at 12:45:36Z and 12:53:36Z ("already reserved or vetoed for this generation").
- Live runner snapshot: composer text is `/remote-control`; the rows under the box are the `/remote-control`, `/rc-status` and `/launch-remote` palette. `dialog.state` and `screenState` are Unknown, `inputHistoryKnown` true, `inputRevision` 3, `adopted` false. No trust prompt is on screen. Child pid 418772. Header reads Claude Code v2.1.322. `transcriptBound=false`, reason `missing`.

## Fix

`ClaudeScreen.TryReadComposer` now treats a slash autocomplete that begins with a highlighted `> /command` row as the live composer. The draft `/remote-control` is readable and the dialog is Clear, so the automatic arm can send Enter. A numbered choice menu under the box stays unreadable.

`ClaudeDialogObservationTests.C1833_SlashAutocompleteUnderComposerIsClear` failed first (`TryReadComposer` false, 1 failed). After the reader change, `ClaudeDialogObservationTests` passed 2/2. Both runs went through `scripts/build-slot.ps1`.

This generation stays vetoed. The running server is the canonical checkout, not this branch, so the live session was not restarted and nothing was typed into it. `POST /api/sessions/{id}/input` is not the remote-control path. After this change is activated, start the agent fresh. The automatic arm can then submit Enter and `/rename` to Antiphon-Decisions. Do not deliver the pending 12:51 prompt onto the stuck draft. That row belongs to session `7b80d3e0` and does not follow a new session.

## Creating the next remote-controlled standing agent

Set kind ClaudeCode, `remoteControlEnabled` true, and `alwaysOn`. Do not set a separate remote-control name: start uses the agent name. Use the same `claude` TUI profile as the other standing agents. Do not set `disableRemoteControl`. Antiphon forces `remoteControlAtStartup` false and types `/remote-control` itself. After the first start, confirm the log line `Remote-control setup completed`, not only status Running. If rename is skipped with `armed=Unarmed` and the screen shows an unsubmitted `/remote-control` under a slash list, this reader fix is not active yet.
