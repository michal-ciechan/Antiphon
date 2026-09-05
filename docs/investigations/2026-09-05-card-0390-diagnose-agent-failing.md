# CARD-0390 — antiphon-diagnose standing agent keeps failing

**Date:** 2026-09-05
**Card:** CARD-0390 (`cfd1c1db-1b17-4f24-80d1-5f0f03dd3cb6`)
**Status:** root cause confirmed. No product code was changed.
**Agent:** `antiphon-diagnose` `fe052653-dfbb-4eae-aae3-8726a5157341`

## Verdict, in one sentence

Claude Code 2.1.258 parks a first launch into `C:\logs\antiphon\diagnose` on an **unnumbered** trust dialog whose default is **No, exit**; CARD-0047's answerer still sends the digit `"1"` from the 2026-08-16 numbered menu, the dialog stays up, `WaitForReadyAsync` returns false after the 15 s settle, and the supervisor kills the session. The seat has never been ready.

## What this is not

| Hypothesis from the card | Verdict | Evidence |
|---|---|---|
| CARD-0214 missing directory | Ruled out | `C:\logs\antiphon\diagnose` exists since 2026-09-03 19:10:58 local with `CLAUDE.md` and `.claude\settings.json`. `POST /api/agents/{id}/ensure-directory` is not needed. |
| Haiku / profile misconfiguration | Ruled out | Sibling `antiphon-check-interpreter` (`be5d4502-…`) is the same kind (`ClaudeCode`), same model (`haiku`), same `alwaysOn`, same parent (`C:\logs\antiphon\…`) and is **Running, consecutiveFailures=0**. |
| CARD-0352 S2 provisioner test-harness lie | Not the launch bug | `DiagnoseProvisionerTests` never starts Claude. S2 did its job: the row, cwd, deny-all hook, and AlwaysOn flag exist. What S2 *depends on* (CARD-0047's trust answerer) is stale vs current Claude. |
| CARD-0334 PolicyRefresh | Ruled out | `policyDrift.lastRefreshedAt` is null, `hasDrift` is false. Failures start 2026-09-03 18:20Z, at provision, before tonight's S4 landing. |
| Deny-all PreToolUse hook | Ruled out | Same CARD-0047 settlement as check-interpreter: the hook cannot fire until a tool is used, and this process never leaves the trust modal. Gotcha #48 already closed this. |

## Mechanism

1. `DiagnoseProvisioner` / `StandingSpecialistProvisioner.PrepareWorkspace` creates a brand-new cwd Claude has never seen (`C:\logs\antiphon\diagnose`) and writes the deny-all hook. It does **not** seed `~/.claude.json` `projects[cwd].hasTrustDialogAccepted`.
   - [`server/Application/Services/DiagnoseProvisioner.cs:75-84`](../../server/Application/Services/DiagnoseProvisioner.cs)
   - [`server/Application/Services/StandingSpecialistProvisioner.cs:130-144`](../../server/Application/Services/StandingSpecialistProvisioner.cs)

2. Claude's first launch into an unseen directory opens the trust dialog. That is Gotcha #48 / CARD-0047, documented as a numbered menu:
   ```
   ❯ 1. Yes, I trust this folder
     2. No, exit
   ```
   The unit fixture is transcribed from 2026-08-16 (`ClaudeStartupTrustPromptTests.cs:18-36`). The headed canary still asserts `AffirmativeKey.ShouldBe("1")` (`ClaudeTrustPromptCanaryTests.cs:65`).

3. **Current Claude 2.1.258 paints a different dialog.** Live screen (session `7bbe0c61-4ab4-4807-809f-51c3a2177c4f`, this investigation, and every surviving diagnose `.ansi.log` since provision):
   ```
   Accessing workspace:
   C:\logs\antiphon\diagnose
   Quick safety check: Is this a project you created or one you trust? …
   > No, exit
     Yes, I trust this folder
   Enter to confirm · Esc to cancel
   ```
   No digits. Default highlight is **No, exit**. Sending Enter would exit. Sending `"1"` does nothing visible.

4. `ClaudeBlockingPromptDetector.Detect` still matches this screen (`entertoconfirm` / `yesitrustthisfolder` / `isthisaprojectyoucreated`) and **always** returns `AffirmativeKey = "1"`:
   [`src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs:98-109`](../../src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs)

5. `RunnerClaudeAdapter.WaitForReadyAsync` waits for quiet, then `ClearStartupTrustPromptAsync` writes `"1"`, then polls `ClaudeTrustPromptSettleMs` (default **15 s**). The modal is still up → `TrustNotCleared` → ready=false.
   [`server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs:141-147, 257-261`](../../server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs)
   [`server/Application/Settings/AgentRegistrySettings.cs:24`](../../server/Application/Settings/AgentRegistrySettings.cs)

6. The adapter does **not** set `LaunchBlock = TrustDialogNotCleared` (the enum exists, CARD-0324, and Grok uses it). `WaitForReadyOrThrowAsync` therefore throws the generic `"Agent process did not become ready."` [`AgentSessionService.cs:1647`](../../server/Application/Services/AgentSessionService.cs). `KillAndDisposeAsync` then kills the pty (`KilledByRequest`, exit 1). The runner reports `transcriptBound: false`, `TranscriptMissing`: *"input had been delivered to it"* — that input is the useless `"1"`.

7. AlwaysOn supervision counts a consecutive failure, backs off, and retries into the same unseen directory. `~/.claude.json` never gains a diagnose project key, so every attempt is a first launch.

Check-interpreter survives because CARD-0047's original answerer (against the **old** numbered menu) persisted `hasTrustDialogAccepted: true` for `C:/logs/antiphon/check-interpreter`. Diagnose was created 2026-09-03 into a new cwd after the TUI changed; trust was never recorded.

## Evidence

### Agent row (GET `/api/agents/fe052653-dfbb-4eae-aae3-8726a5157341`)

Read 2026-09-05 ~03:00Z, then again after the live restart:

| Field | Value |
|---|---|
| status | `Failed` |
| alwaysOn | true |
| kind / modelId | `ClaudeCode` / `haiku` |
| workingDirectory | `C:\logs\antiphon\diagnose` |
| consecutiveFailures | 15 before this investigation's restart; **16** after |
| nextRestartAt | `2026-09-07T00:29:46Z` before; `2026-09-08T22:08:06Z` after (3.8 d backoff) |
| liveSession | null |
| policyDrift | `lastRefreshedAt: null`, `hasDrift: false` |
| createdAt | `2026-09-03T18:10:58.468036Z` |

### Incidents (GET `/api/agents/{id}/incidents`)

50 rows returned. Oldest in that window: `2026-09-03T18:20:23Z` `TranscriptBindFailed` — **nine minutes after create**. Recurring triplet on every attempt:

1. `TranscriptBindFailed` / `TranscriptMissing` — *"exited without producing an identifiable transcript, although input had been delivered to it."*
2. `Crash` — `Agent process did not become ready.` **or** `Process exited (KilledByRequest, code 1).`
3. `RestartScheduled` — same failureReason, backoff doubling (5.3 m → … → 3.8 d).

Also `DiagnoseUnavailable` (*"no reading within 90s"*) from the CARD-0352 sweep trying to use a seat that is not up, and `BackoffEscalated` Critical at 15 failures (`2026-09-05T02:59:06Z`).

### On-disk session logs

All diagnose pty logs are the trust dialog and nothing else:

| Session | File | Bytes | Contents |
|---|---|---|---|
| `ab2c4230-…` | `C:\logs\antiphon\session-runner\ab2c42301ff24a4a8312975fbdbc4669.ansi.log` | 1312 | unnumbered trust dialog, cwd `C:\logs\antiphon\diagnose` |
| `f4a032b1-…` | `…\f4a032b1f15944e4936a7ef225ab7f06.ansi.log` | 1312 | same |
| `de538b13-…` | `…\de538b1307bd4712a394170885bbe1cc.ansi.log` | 1312 | same |
| `cb3696fe-…` | `…\cb3696fe42d241308a5d7dd33d14f25b.ansi.log` | 2624 | same dialog painted twice (redraw after the ignored `"1"`) |
| `7bbe0c61-…` (this investigation) | `…\7bbe0c614ab44807809f51c3a2177c4f.ansi.log` | 1312 | same |

`GET /api/sessions/cb3696fe-…/transcript?since=0` → `entries: []`, `lastSequence: 0`. Buffer still held the trust dialog after exit.

`C:\src\Antiphon\logs\session-runner.log`:

```
[18:05:16 WRN] Session cb3696fe-…: the child exited without ever producing a transcript we could identify, although input was delivered to it.
[03:59:02 WRN] Session cb3696fe-…: the child exited without ever producing a transcript we could identify, although input was delivered to it.
```

(18:05 local 2026-09-04 = 17:05Z, matching the card's `updatedAt` / attempt 15.)

### `~/.claude.json` (trust flags only)

- `projects["C:/logs/antiphon/check-interpreter"].hasTrustDialogAccepted` = **true**
- **No** `diagnose` path anywhere in the file (forward or backslash)

### Live restart (this investigation)

`POST /api/agents/fe052653-…/start` body `{"fresh":true}` at **2026-09-05T03:06:22.98Z**.

| t (local BST) | Observation |
|---|---|
| 04:06:22 | start accepted; `liveSession` `7bbe0c61-4ab4-4807-809f-51c3a2177c4f` status `Starting` |
| 04:06:26–04:06:44 | `Running`, live session present, consecutiveFailures still 15 |
| 04:06:47 | `Failed`, live=null, consecutiveFailures **16**. Wall time **~25 s** (quiet wait + 15 s settle + kill) |

After fail, runner row:

```
sessionId=7bbe0c61-… status=Exited exitCode=1 exitReason=KilledByRequest
lastSequence=11 transcriptBound=false transcriptUnboundReason=locating
```

`GET /api/sessions/7bbe0c61-…/buffer` lastSequence=11, still:

```
> No, exit
  Yes, I trust this folder
Enter to confirm · Esc to cancel
```

New incidents at `2026-09-05T03:06:46Z`: Crash + RestartScheduled (`KilledByRequest`, code 1), then TranscriptBindFailed `TranscriptMissing`. Restart attempt 17 scheduled for `2026-09-08T22:08:06Z`.

Claude CLI on this box: **2.1.258**.

## Why the tests are green

`ClaudeStartupTrustPromptTests` and `RunnerClaudeAdapterTrustPromptTests` drive a **scripted 2026-08-16 screen** that clears on `"1"`. They do not launch Claude. The headed canary (`ClaudeTrustPromptCanaryTests`) would catch the new shape, but it is `[Explicit]` / `ANTIPHON_HEADED_TESTS=1`. DiagnoseProvisionerTests only assert the row, cwd, and hook JSON.

## Recommended fix direction

Not done here (not a one-line config typo; changing the answerer without a headed measurement would risk sending Enter onto "No, exit" and making the seat *exit itself*).

1. **Update `ClaudeBlockingPromptDetector`** so the current unnumbered, inverted trust dialog is answered with the keystroke that selects **Yes, I trust this folder** (likely Down then Enter — **measure on a headed canary against 2.1.258**, do not guess). Keep sending `"1"` for the old numbered form if it still exists. Replace the unit fixture with the live screen from `7bbe0c61….ansi.log` / `cb3696fe….ansi.log`.
2. **Retarget `ClaudeTrustPromptCanaryTests`**: `AffirmativeKey.ShouldBe("1")` is now the assertion that would have caught this. Run it headed.
3. **Optional belt, not a substitute:** seed `hasTrustDialogAccepted` (both slash spellings) at `StandingSpecialistProvisioner.PrepareWorkspace` / `AgentWorkspaceProvisioner`, the way `UntrustedDirectory.Trust()` already does in the canary. That unsticks *this* cwd even if the TUI changes again; the answerer is still required for every other new directory (worktrees, the next specialist).
4. **Observability:** `RunnerClaudeAdapter` should set `LaunchBlock = TrustDialogNotCleared` on `TrustNotCleared` so incidents stop saying the generic "did not become ready." The enum already exists (`AgentLaunchBlockKind.TrustDialogNotCleared`).

A temporary operator unstick (seed `~/.claude.json` for `C:/logs/antiphon/diagnose` and `C:\logs\antiphon\diagnose`, then `POST /api/agents/{id}/start`) would bring this seat up without a code change. It would not protect the next unseen cwd.

## Remaining uncertainties

- The exact affirmative keystroke on Claude 2.1.258's unnumbered dialog is not typed in this pass. `"1"` is proven *not* to clear it (dialog still painted at kill, process was `KilledByRequest` rather than a clean "No, exit"). Down+Enter is the obvious candidate; the headed canary has to measure it.
- Claude version that introduced the unnumbered inverted default is unknown. The 2026-08-16 fixture is the last recorded numbered form; diagnose's first failure is 2026-09-03 18:20Z.
- Server-side `LogError` *"still blocked on Claude's trust dialog after answering it"* was not recovered from disk (Aspire console, not `logs/session-runner.log`). The adapter path and the 15 s settle matching the 25 s live fail are the substitute.

## Routes used (documented)

`GET /api/agents/{id}`, `GET /api/agents/{id}/incidents`, `POST /api/agents/{id}/start`, `GET /api/sessions/{id}/buffer`, `GET /api/sessions/{id}/transcript`, `GET :17204/sessions`. There is no `GET /api/agents/{id}/sessions` and no `GET /api/agent-incidents`.
