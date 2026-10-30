# C680 AskUserQuestion timeout on native Windows

The reproduced timeout is a **test prompt-submission failure**, outside the proposed A/B/C split. On pinned Claude 2.1.312 the original question prompt remains in the composer, with no UserPrompt or tool call in the transcript. With explicit, verified fixture submission, the same Haiku prompt opens the question dialog, the observer reports Blocked, and the real nudge delivery service refuses with zero backend writes. Sonnet passes the same controlled scenario.

| Proposed cause | Finding |
|---|---|
| A: Haiku receives the request but does not call AskUserQuestion | Not reproduced. The failed original setup never submitted the question. Haiku calls the tool when the same prompt is actually submitted. |
| B: product misses the open question dialog and could type into it | Not reproduced. The real 2.1.312 question is classified Blocked/Choice and both model controls prove zero nudge writes. Unknown, seen on the stranded composer, also refuses automated input. |
| C: CLI tool-call/dialog schema changed incompatibly | Not reproduced. The native 2.1.312 call has the expected shape and the existing normalizer emits the expected ToolCall. |

This diagnoses the reproduced failure, not the lost historical screen from task 7c069314. That earlier run cannot be reconstructed exactly. No product change or product red witness is warranted by these results. The default canary remains unfixed; controlled diagnostic passes do not turn its recorded failures into a normal-lane pass.

## Source and scope

- Task/branch: `497eea4e`, `feat/card-task-497eea4e`.
- Base: `ade54222543381e2e1b31ad79d8ae33a4035f70b` (the supplied rc source).
- Initial diagnostic commit: `c25a25ff28110157f4969b02c1b191cb5053d6e5`.
- Corrected logging build: `8c241c55d08f99c76b29c83afeec80bcc76a662a`.
- Controlled-submission build: `6e661e281fba02c8257f775adbe564ec34a95de7`.
- Only executable-source changes are the existing canary and the new test-only `ClaudeDialogCanaryDiagnostics` helper. Production code, policy, rosters, timeouts, prompt text and nudge assertions are unchanged. The normal submission path remains unchanged unless both diagnostic root and submission-control opt-in are set.

The brief's native investigation authority was sufficient; no further approval was requested. No AppHost/runner restart, deployment, tag, branch switch, rebase, force push or operator-process kill was performed. No modification was made in the canonical checkout.

## What the test actually waits for

The base test's lines 92-100 conjunct exactly three conditions:

1. `snapshot.Dialog?.State == ProviderDialogState.Blocked`.
2. At least one normalized `UserPrompt` completely matches the submitted probe.
3. At least one normalized `ToolCall` has the expected name (`Bash` or `AskUserQuestion`).

There is **no separate screen-content conjunct**. Before sending either probe, a separate 30-second wait requires an empty, readable composer, Clear screen observation and two settled samples. The three-part dialog wait has a two-minute budget. Only after all three conditions does the test call `NudgeAsync` / `DeliverAsync` and check refusal, zero backend writes, unchanged input revision, stable blocked screen, no nudge UserPrompt, no pending-tool result, no permission marker and no attempt start.

The dialog observer does not parse JSONL. `ClaudeDialogObservation.Observe` reads the rendered screen; `SessionRunnerRuntime.ObserveDialog` combines that classification with generation/input-history evidence and exposes `claudeDialogGuardV1`. `ProviderDialogObservationService.Refusal` admits only Clear with valid identity/history. Blocked and Unknown both refuse. The queue's expectation path checks `ModalBlockReasonAsync`; the watchdog also checks the same refusal service, and conditional input checks the observation again at the runner.

Separately, `TranscriptNormalizer.FromAssistant` reads `type=assistant`, `message.content[]`, then `type=tool_use`, `name`, `id` and `input`. A missing transcript entry is not evidence that the screen observer considered a question safe.

Relevant source owners:

- [Live canary](../../tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs)
- [Diagnostic helper](../../tests/Antiphon.Tests/Agents/ClaudeDialogCanaryDiagnostics.cs)
- [Screen observation](../../src/Antiphon.Agents.Pty/ClaudeDialogObservation.cs)
- [Composer recognition](../../src/Antiphon.Agents.Pty/ClaudeScreen.cs)
- [Input encoding](../../src/Antiphon.Agents.Pty/PtyInputEncoding.cs)
- [Runner observation and conditional input](../../src/Antiphon.SessionRunner/SessionRunnerRuntime.cs)
- [Transcript normalization](../../src/Antiphon.SessionRunner/TranscriptNormalizer.cs)
- [Provider refusal](../../server/Application/Services/ProviderDialogObservationService.cs)
- [Expectation queue admission](../../server/Application/Services/SessionMessageQueueService.Expectations.cs)
- [Watchdog](../../server/Application/Services/WatchdogService.cs)

## Native evidence

Host: Windows 10.0.19045, win-x64, .NET 9.0.22, TUnit 1.44.0, ModernConPty. Every run set `ANTIPHON_HEADED_TESTS=1` and selected only `ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs` through the host build-slot gate. Every lease reported granted with zero queue wait. Each selection executed one test; none skipped.

The installed executable had advanced to 2.1.314 before this investigation. Its first result is retained separately. The exact cached 2.1.312 executable was copied into the task's ignored directory, with matching source/copy SHA-256 `6099672EBE5DCB518B67D1D32DDE1E3360BC6BEE1DAE7F8D28A69C49ACC7C865`. PATH selected the copy and `DISABLE_AUTOUPDATER=1` prevented an update. Launch captures and raw records confirm 2.1.312. The copy was removed after all runs; the installed binaries were unchanged.

| Evidence directory | CLI/model | Submission | Passed / failed / skipped | Test duration | Command wall time |
|---|---|---|---|---|---|
| `haiku` | 2.1.314 / Haiku 5.5 | Original | 0 / 1 / 0 | 148.780 s | 227.201 s |
| `haiku-312` | 2.1.312 / Haiku 5.5 | Original | 0 / 1 / 0 | 150.850 s | 175.678 s |
| `haiku-312-controlled` | 2.1.312 / Haiku 5.5 | Diagnostic control | 1 / 0 / 0 | 30.332 s | 97.799 s |
| `sonnet-312-controlled` | 2.1.312 / Sonnet 5.5 | Same diagnostic control | 1 / 0 / 0 | 37.682 s | 66.043 s |

Original runs used `8c241c55d08f99c76b29c83afeec80bcc76a662a`; controls used `6e661e281fba02c8257f775adbe564ec34a95de7`. The latter assembly SHA-256 was `828000484A1410FB49BD57A623550CB2E5CF8D48CC42CD8DDC6ACC15F59F5C6A`. All four runs have source/assembly metadata and fresh TRX files. Totals: 2 passed, 2 failed, 0 skipped. Both failures are the unchanged setup timeout, not a nudge-refusal assertion. Exactly one stronger-model run was spent (two scenarios); no Opus run was needed.

### Original submission failure

In `haiku-312`, question session `20d2e022-7722-49cf-869c-3bc81fc89a1b`, the complete color prompt remains between the composer rules, followed by a blank line and `manual mode on`. Through the two-minute wait:

- `submitted=false`, `called=false`, transcript entries `0`.
- The initial screen observation is Clear, followed by Unknown once the manual-mode footer renders; it never becomes a question dialog.
- Input revision is 2: the body and separate Enter reached the runner's input path, without producing a submitted UserPrompt.
- The native transcript cannot be copied because this unsubmitted conversation has no bound JSONL. The empty normalized transcript and screen are retained. Permission-case and successful question-case native JSONL files are retained.

The same stranded-draft outcome occurred on 2.1.314. Both Bash subcases passed and refused their nudge with zero writes.

The prompt is a single line. `PtyInputEncoding.EncodeBody` brackets multiline bodies only, so this probe is sent unbracketed, followed by an unconditional 100 ms delay and Enter. The encoder's existing documentation describes an Enter inside Claude's paste-heuristic window being folded into a newline, leaving an unsubmitted draft. The observed draft and blank line fit that mechanism. This investigation establishes submission failure; the control changes encoding, settling and fixture input route together, so it does not uniquely isolate the 100 ms interval from those other factors.

The `manual mode on` footer is absent from `ClaudeScreen.IsComposerHintRow`, explaining Unknown on this ordinary composer. That is a conservative composer-compatibility limitation, not an open question classified Clear. No product bypass was added.

### Controlled submission and actual questions

`ANTIPHON_C680_DEBUG_SUBMIT=1`, together with a diagnostic root, enables explicit fixture input to the owned child. It requires an initially Clear screen, brackets the entire single-line probe, and waits for two matching complete composer samples 500 ms apart before sending Enter. Its test-only composer oracle recognizes exactly the observed manual-mode footer. The watchdog/nudge still uses the unchanged production admission and delivery path.

The prompt is unchanged:

> Use AskUserQuestion now to ask which color I prefer, with Blue and Green as choices. Wait for my answer; do not choose for me.

On Haiku, at `2026-10-30T04:47:44.0539221Z`, the actual color question is visible and both screen/provider observations are Blocked/Choice, with the prompt receipt present. At `04:47:44.5797215Z`, the AskUserQuestion entry is present too. Sonnet shows the same transition at `04:49:02.2232944Z` and `04:49:02.7475231Z`. The ordinary polling lag was about 0.525 seconds, not two minutes.

Both successful TRX files contain these unedited receipts:

```text
C680 native cli=2.1.312 backend=ModernConPty case=Bash inputRevision=2 backendDelta=0 refused=true
C680 native cli=2.1.312 backend=ModernConPty case=AskUserQuestion inputRevision=2 backendDelta=0 refused=true
```

The helper also has a diagnostic-only post-timeout dismissal to inspect deferred tool persistence, preserving the original timeout as failure. That path was **never reached** in either control: zero explicit dismissals. Neither question was answered or canceled to manufacture the tool-call evidence.

## Transcript shape comparison

The retained live 2.1.312 question call uses:

```text
type: assistant
message.model: claude-haiku-5-5 (or claude-sonnet-5-5)
message.content[]:
  type: tool_use
  id: string
  name: AskUserQuestion
  input.questions[]:
    question: string
    header: string
    multiSelect: boolean
    options[]: { label: string, description: string }
  caller: object
```

Five historical tool-call shapes from 2.1.266 and 2.1.286 have the same relevant fields, including `caller`. Only field names, tool name, version and timestamp were extracted from the operator's existing transcripts; credentials and other conversation content were not inspected or printed. The earlier search found no 2.1.312 AskUserQuestion sample, so the live controls supplied that evidence. The parser's actual successful ToolCall entries establish compatibility; no new shape parser is needed.

An [upstream report for 2.1.281](https://github.com/anthropics/claude-code/issues/96795) describes delayed persistence of a pending question without a hook. It motivated the optional capture-after-dismissal control. It is not the observed cause here: both native 2.1.312 controls persisted the call while the question remained open, with hooks disabled.

## Required follow-up and manual obligation

Repair the canary's submission setup through an ordinary test change: submit with the established terminal framing, verify a complete settled draft before Enter, and require the transcript UserPrompt receipt as its own diagnosed phase before waiting for a question. Retain the two-minute dialog limit and every zero-write/refusal assertion. Do not substitute a larger model or a more forceful prompt; the existing prompt already works with Haiku. Separate reporting of prompt receipt, tool call and screen observation should remain available. Pin and report the CLI version in future native qualification.

The manual-obligation disposition remains advisable because this test requires authenticated live provider turns and a native PTY. The confirmed setup defect does not make it a deterministic unattended required test. No exclusion row, pinned decision-class guard, release-gates text or policy was changed here. The caller should handle the proposed CARD-0680 row and its coupled guard/documentation changes together, preserving the original failed qualification and this controlled evidence. These passes are not Windows Full/Final coverage or authorization to activate the watchdog on another backend/version.

## Reproduction and evidence custody

Retained ignored root:

`C:\Antiphon\worktrees\card-task-497eea4e\.antiphon\debug-c680`

`execution-summary.json` joins run metadata, conditions, actual model names, tool shapes, temporary-root checks and child cleanup. `evidence-hashes.json` records hashes of screens, observations, native JSONL and TRX. Each scenario directory contains UTC `observations.jsonl`, changing screen/state captures, `last-screen.txt`, `normalized-transcript.json`, and native `transcript.jsonl` when a transcript existed. The controlled runs also retain `before-enter-screen.txt`. Raw conversations are local only, ignored and not committed. A text-evidence secret-pattern scan found zero matches; only the harmless probe conversation/tool shapes are described here.

The ignored `run-c680.ps1` and `summarize.ps1` retain the exact local orchestration. The committed test can be rerun after building once:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c680-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c680debug497/ --nologo --verbosity quiet
$env:ANTIPHON_HEADED_TESTS = '1'
$env:ANTIPHON_C680_DEBUG_ROOT = '<fresh absolute local evidence directory>'
$env:ANTIPHON_C680_DEBUG_MODEL = 'haiku' # or sonnet for the bounded comparison
$env:ANTIPHON_C680_DEBUG_SUBMIT = '0' # 1 selects the diagnostic submission control
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c680-native -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c680debug497/ -- --treenode-filter '/*/*/ClaudeDialogWatchdogCanaryTests/C680_LiveNudgeRefusesPermissionAndQuestionDialogs' --report-trx --report-trx-filename run.trx --results-directory '<fresh result directory>'
```

For exact-version reproduction, copy the cached `C:\Users\lndco\.local\share\claude\versions\2.1.312` into a newly owned directory as `claude.exe`, verify the hash above, prepend that directory to PATH and set `DISABLE_AUTOUPDATER=1`. Do not change the installed CLI or operator configuration. Restore the launching shell's temporary environment afterward, or use a disposable child PowerShell as these runs did.

Build history is preserved: initial diagnostic build failed with one introduced DTO-signature error (1378 warnings, 609.49 s); the correction built with zero errors (1380 warnings, 622.87 s); the control build had zero errors (1380 warnings, 618.72 s). The first failure is not attributed to the base. No full suite or unrelated selection was run. Timings are single observations, not p95 estimates. Per-run timing shards and UTC observations are retained; uninstrumented CLI/setup/teardown subphases remain unknown rather than zero. Command wall times include driver/discovery overhead and must not be confused with test-body durations.

All 28 captured owned child identities have matching observed-exit records. All eight temporary native roots are absent. The 34 task-created `bin-c680debug497` directories and copied CLI were removed using verified worktree-contained literal paths; `cleanup.json` records their absence. Operator transcripts, installed binaries and other worktrees were not deleted.

--- next stage ---
next: docs
handoff: Record the submission-failure diagnosis and manual-obligation disposition; commission the normal canary setup repair separately. No product dialog-parser fix is supported. Preserve both original red runs and both controlled native passes; do not claim Full qualification.
artifact: C:\Antiphon\worktrees\card-task-497eea4e\docs\investigations\2026-10-30-c680-askuserquestion-timeout.md
