# Codex 0.170 boot pointer is typed, then Enter is withheld

After the ready-gate fix `2f5e1721` and the pointer-composer fix `b02b4ea7` (rc/2026-11-30 tip `b02b4ea7ae80ad8eff3445a82d5477b80107bf44`, AppHost restarted onto it at 05:52Z), both Codex 0.170 canaries pass the ready gate and receive the boot pointer in the composer. `CodexPointerComposerEvidence.TryReadComposer` still returns no composer, so `SubmitConsumedPointerAsync` waits out the 15s evidence timeout and withholds Enter. Codex never starts a turn, writes no cwd rollout, and the server fails the task at 10 minutes for "boot prompt was never delivered".

The 120s ready-gate kill does not apply. Both sessions were still `Running` when their screens were captured at 2026-11-21T06:07:31Z, about 14 minutes after launch.

Investigated by task `d6e31ffd` on 2026-11-21. Read-only. No code change, no build, no restart, no input sent to either session. Source inspected: `b02b4ea7ae80ad8eff3445a82d5477b80107bf44`.

## Specimens

| | Sol ladder | Astra |
| --- | --- | --- |
| Task | `7c3daf8a-0276-40a4-ac51-2c5d72b38da1` | `6d5b2869-30c6-41b7-895f-efe4fa66ad37` |
| Agent | `1a2cde19-00e3-44b4-8f96-c522f586c246` | `72feee52-0b31-408b-8930-5cd88fb25922` |
| Session | `dd111a14-2eb5-47ed-b27f-f9b7d15dadb7` | `2aa3f72e-28eb-4fce-98f7-73e7fc4ab92a` |
| Model on screen / argv | `GPT-6.2-Sol high`, `--model gpt-6.2-sol` | `GPT-6.2-Astra high`, `--model gpt-6.2-astra` |
| cwd | `/work/worktrees/ws-03-d3` | `/work/worktrees/ws-02-d3` |
| Accepted start | 2026-11-21T05:52:56.135988Z | 2026-11-21T05:53:13.047232Z |
| Pointer write started | 2026-11-21T05:53:08.052621Z | 2026-11-21T05:53:22.265223Z |
| `DeliveryVerificationFailed` | 2026-11-21T05:53:23.372707Z | 2026-11-21T05:53:37.631033Z |
| Task failed | 2026-11-21T06:03:12.056298Z | 2026-11-21T06:03:36.866781Z |
| Queue row | `1443a3c0-2224-4057-8838-2bb718642247` Pending, 1 attempt, 623 chars | `e7d1d779-9041-4578-9540-a7ef92c16765` Pending, 1 attempt, 622 chars |

Both processes were launched with `--no-alt-screen`, `-c disable_paste_burst=true`, and `-c model_reasoning_effort=high`. Codex CLI on the screen is `v0.170.0`.

The incident text, identical on both agents except the queue id and generation, is produced by `DeferPointersAsync`:

```text
pointer-consumption-unconfirmed queue=<id> generation=<accepted-start>: Complete pointer consumption is unconfirmed; Enter withheld. Retaining the typed attempt. Release requires a complete UserPrompt, a complete idle composer for Enter-only recovery, or a new generation.
```

`Supervision:DeliveryVerification:EvidenceTimeoutSeconds` defaults to 15. Each incident is 15 seconds after `lastDeliveryStartedAt`. That is the evidence deadline in `SubmitConsumedPointerAsync` returning `DeliveryVerdict.NoComposerEvidence` (`server/Application/Services/SessionMessageQueueService.CodexPointers.cs:99` and `:165-172`). Enter is sent only after `CompleteComposer` is true twice (`:98` and `:108-110`). It was not sent.

`TranscriptBindFailed` repeats at 60s, 360s, 661s (and 961s for Astra): `No cwd-matching Codex rollout candidates ... (519 rollout(s) ... 0 cwd-matched)`. That follows from no submit. Codex writes a rollout when a turn starts. The composer still holds the unsubmitted pointer, so there is no turn to bind.

Runner dialog at capture, both sessions: `inputRevision=1`, `submittedInput=null`, `inputUncertain=false`, `state=Unknown`, `screenState=Unknown`, `kind=None`. One body write landed. No submit was recorded. `lastSequence` was 75 (Sol) and 76 (Astra) on both `GET /api/sessions/{id}/buffer` and `GET :8080/sessions/{id}/snapshot` inside `antiphon-runner-session-runner-1`.

## Captured screens

Rendered screens from `GET http://127.0.0.1:8080/sessions/{id}/snapshot` at 2026-11-21T06:07:31Z. Each grid is 30 rows. The widest row is 119 columns. Neither screen contains `for agents`, `for shortcuts`, or a paste placeholder.

Sol, session `dd111a14-2eb5-47ed-b27f-f9b7d15dadb7`:

```text
  >_ OpenAI Codex (v0.170.0)
     /work/worktrees/ws-03-d3
  permissions: YOLO mode

  Tip: Use /keymap to configure keyboard shortcuts.


› [antiphon-task:7c3daf8a] role=Custom tier=High workspace=Worktree rc30 ladder canary 2 [antiphon-task:7c3daf8a] YOUR
  BRIEF IS NOT IN THIS MESSAGE. It is 1,609 characters — too long to type into a terminal without the transport
  dropping part of it, so it was written out instead. Read it in full before you do anything else: '.antiphon/
  inbox/1443a3c0-2224-4057-8838-2bb718642247.md' Everything you need is there. Do not start from this summary. --- how
  to report back --- The full reporting contract is in the brief above — read it there. Your final message is the
  entire report the caller receives. [antiphon-task:7c3daf8a]

  GPT-6.2-Sol high · /work/worktrees/ws-03-d3
                                                                                              ⚠ 2 warnings · f2 to view
```

Astra, session `2aa3f72e-28eb-4fce-98f7-73e7fc4ab92a`:

```text
  >_ OpenAI Codex (v0.170.0)
     /work/worktrees/ws-02-d3
  permissions: YOLO mode

  Tip: Use /init to create an AGENTS.md with project-specific guidance.


› [antiphon-task:6d5b2869] role=Custom tier=High workspace=Worktree rc30 astra canary 2 [antiphon-task:6d5b2869] YOUR
  BRIEF IS NOT IN THIS MESSAGE. It is 1,609 characters — too long to type into a terminal without the transport
  dropping part of it, so it was written out instead. Read it in full before you do anything else: '.antiphon/inbox/
  e7d1d779-9041-4578-9540-a7ef92c16765.md' Everything you need is there. Do not start from this summary. --- how to
  report back --- The full reporting contract is in the brief above — read it there. Your final message is the entire
  report the caller receives. [antiphon-task:6d5b2869]

  GPT-6.2-Astra high · /work/worktrees/ws-02-d3
                                                                                              ⚠ 2 warnings · f2 to view
```

On both screens the last non-empty row is 94 spaces followed by `⚠ 2 warnings · f2 to view` (row length 119). The model footer is the non-empty row above it. The pointer occupies the composer: a `›` row plus indented wrap rows. The queued body, whitespace-stripped the way `ComposerDeliveryEvidence.Normalize` strips it, is 532 characters (Sol) and 531 characters (Astra) and equals that composer text.

## Why the reader returns prefix 0

`ObservePointerAsync` calls `CodexPointerComposerEvidence` on `snapshot.RenderedScreen` (`SessionMessageQueueService.CodexPointers.cs:69-70`). `TryReadComposer` walks from the bottom:

```30:39:src/Antiphon.Agents.Pty/CodexPointerComposerEvidence.cs
        // Codex 0.170.0 draws an agents/shortcuts row under the model footer.
        // Step back to that footer. A 0.160 footer keeps the warning chip on the
        // model line, which does not match and stays the last line.
        if (CodexStartupScreen.IsAgentShortcutsRow(lines[footer]))
        {
            footer--;
            while (footer >= 0 && string.IsNullOrWhiteSpace(lines[footer])) footer--;
        }
        if (footer < 1 || !CodexStartupScreen.IsModelEffortCwdFooter(lines[footer])
            || !CodexStartupScreen.TryReadFooterOnlyModel(lines[footer].Trim(), out _)) return false;
```

`IsAgentShortcutsRow` (`CodexStartupReadiness.cs:235-239`) requires both `for agents` and `for shortcuts`. The captured last row has neither, so the reader does not step back.

`IsModelEffortCwdFooter` (`CodexStartupReadiness.cs:314-333`) then accepts the chip row: `⚠ 2 warnings · f2 to view` has a `·`, a non-empty right side, and at least two left-side tokens. `TryReadFooterOnlyModel` (`CodexStartupReadiness.cs:185-203`) rejects it, because the text before the first `·` is not `GPT-*` plus one effort word. `TryReadComposer` returns false. `MatchingPrefixLength` is 0. `IsComplete` is false for the whole 15 seconds. Enter stays withheld.

The same predicates, applied to these two rendered screens and the queued bodies, give prefix 0 today. Stepping that chip row off, once, lands on `GPT-6.2-Sol high · /work/worktrees/ws-03-d3` and `GPT-6.2-Astra high · /work/worktrees/ws-02-d3`. The existing composer walk then matches the queued body exactly (prefix 532 and 531). That step is what the code task should add. This diagnosis did not rebuild the assembly; the production incident text and the 15-second deadline already show the server took the prefix-0 path.

## The derived with-text grid is the wrong shape

The pointer fix (`9adbf3ba1`) steps off a shortcuts row. Its fixture in `tests/Antiphon.Agents.Pty.Tests/CodexAccountBootstrapTests.cs:288-321` replaces the idle composer line of the measured empty Sol frame and keeps this last row:

```text
← for agents · ? for shortcuts                                                              ⚠ 2 warnings · f2 to view
```

The comment at `:288-292` says that grid is derived, not a measured typed composer, and that the live canary is the measurement. `docs/superpowers/plans/2026-11-21-codex-0170-pointer-fix-checkpoints.md` says the same. Review r2 (`origin/feat/card-task-9bef45fb`, `docs/superpowers/reviews/2026-11-21-codex-pointer-fix-review-r2.md`) probed that derived shape, found the shortcuts step sufficient, and did not have a with-text capture. Ready-gate review r1 (`origin/feat/card-task-851cb98f`, `docs/superpowers/reviews/2026-11-21-codex-ready-fix-review-r1.md`, advisory A-1) already said the pointer reader had the last-row bug and recorded: "Not measured: whether 0.170 still draws the shortcuts row while the composer holds text."

The live with-text grid answers that. Codex 0.170 drops `for agents` / `for shortcuts` once the composer holds the pointer and leaves the warning chip right-aligned on that same row. The idle frame measured for `149a0bb6` still has the shortcuts text, which is why the ready gate (`TryReadBottomComposer`, `fd16cd4ca`) now passes and these sessions survive past 120 seconds. The pointer reader only learned the idle shape.

`CodexSubmitConfirmation.ComposerStillShows` squashes the whole screen and runs after Enter. It is not on this path. `disable_paste_burst=true` matches the screen: the pointer is wrapped in the composer, not collapsed to a paste placeholder.

## Smallest fix

In `TryReadComposer`, take the same one-row step for a warning-chip row that the shortcuts row already takes. A 0.160 footer keeps the chip on the `GPT-*` line, so a line containing `GPT-` must stay the footer.

```csharp
        // Codex 0.170.0 draws an agents/shortcuts row under the model footer while
        // the composer is empty. With text in the composer that row keeps only the
        // right-aligned warning chip. Step back to the footer. A 0.160 footer keeps
        // the chip on the GPT-* line, which fails IsWarningChipRow and stays put.
        if (CodexStartupScreen.IsAgentShortcutsRow(lines[footer])
            || IsWarningChipRow(lines[footer]))
        {
            footer--;
            while (footer >= 0 && string.IsNullOrWhiteSpace(lines[footer])) footer--;
        }
```

```csharp
    private static bool IsWarningChipRow(string line)
    {
        var t = line.Trim();
        return t.Contains("f2 to view", StringComparison.Ordinal)
            && t.Contains("warning", StringComparison.OrdinalIgnoreCase)
            && !t.Contains("GPT-", StringComparison.Ordinal);
    }
```

Leave `TryReadBottomComposer` unchanged. Idle 0.170 still ends on the shortcuts row, and a with-text screen is not a ready screen.

Add `Codex_0170_typed_pointer_with_warning_chip_row_is_complete` next to the existing derived-grid test. The fixture's last non-empty row is only `⚠ 2 warnings · f2 to view` (94 leading spaces is enough to match the capture; the trim is what the reader sees), the model footer is the row above it, and the pointer is a `›` row plus at least one indented wrap row. Assert `IsComplete` and a prefix equal to the compact body. Keep `Codex_0170_typed_pointer_with_shortcuts_row_is_complete` and the 0.160 chip-on-the-model-line case green. A chip row with no model footer above it stays incomplete.

One step is enough for both captured screens. If a later Codex build draws the shortcuts row and the chip as two separate rows, the single step lands on the shortcuts row and prefix stays 0; widen the `if` to a `while` over those two predicates only if a capture shows that stack.
