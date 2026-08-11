# CARD-0006 "regression": codex question detection

**Verdict: CARD-0006 did not cause this.** The failing test is fragile to the
*length of the directory the tests run from*. The A/B that implicated CARD-0006 was
confounded — master was measured from `C:\src\Antiphon` (short path) and the branch
from a worktree (long path). Underneath the false alarm there is a real production
bug, which is what I fixed.

## Root cause

`CodexResponseAnalyzer.ExtractResponse` strips the echoed prompt so that question
detection sees only the agent's reply:

```csharp
var promptIndex = clean.IndexOf(prompt, StringComparison.Ordinal);
if (promptIndex >= 0)
    clean = clean[(promptIndex + prompt.Length)..];
```

That is a **literal** match. The terminal hard-wraps the echo at the window width, so
once the echo crosses the right margin the prompt is no longer literally present — a
newline sits inside it. `IndexOf` returns -1, nothing is stripped, and the prompt's own
`?` is read as the agent asking a question.

Captured from the real ConPTY buffer during the failing run (cols = 120):

```
<CR>C:\Antiphon\worktrees\card-task-2c40e79f\tests\Antiphon.Tests\bin-verify>echo answer has no question & rem prompt has a <CR><LF>
question?<ESC>[K<CR><LF>
answer has no question
```

`IndexOf(prompt)` → `-1`. The break lands between `has a ` and `question?`.

The wrap is decided by the echo's column offset, and cmd's prompt prefix is the cwd:

| run location | prefix | prefix + command | wraps at 120? |
|---|---|---|---|
| `C:\src\Antiphon\tests\Antiphon.Tests\bin\Debug\net9.0>` | 54 | 110 | no → **passes** |
| `C:\Antiphon\worktrees\card-task-2c40e79f\tests\Antiphon.Tests\bin-verify>` | 73 | 129 | yes → **fails** |

73 + 47 = exactly 120, which is why the break falls mid-word.

### Proof CARD-0006 is innocent

I checked out **master (e6c0952)** in this same worktree path and ran the same test with
the same `--property:OutputPath=bin-verify/`:

```
Test run summary: Failed! - ...\bin-verify\Antiphon.Tests.dll
  total: 1  failed: 1  succeeded: 0
```

Master fails identically once the path is long. Code held constant except the branch;
only the directory length matters. CARD-0006 touches the session-runner, transcript
tailer and DTOs — none of which this test loads. Its DTO change is purely additive
(two new optional trailing parameters on `SessionRunnerEvent`).

## The real bug

This is not only a test artifact. Wrapping is a function of prompt length, and real
prompts are long, so in production the same path fails:

- the user's own prompt is left in `ResponseText` (which is what gets relayed to a
  channel — a channel-bound agent would echo the operator's prompt back out), and
- `IsAskingQuestion` goes true whenever the prompt itself contains `?`, which parks a
  session waiting on a human who was never actually asked anything.

## Fix

`src/Antiphon.Agents.Pty/CodexDetectors.cs` — `ExtractResponse` now locates the echo via
`FindPromptEchoEnd`, which tries the literal match first and, failing that, projects the
snapshot onto its newline-free form (keeping an index map) and maps the match back to an
offset in the original. Only newlines are dropped: ConPTY wraps by inserting a break at
the margin and does not pad or re-flow, so every other character survives verbatim. It
also handles genuinely multi-line prompts, whose own newlines flatten the same way.

Nothing in CARD-0006 was touched or weakened.

## Tests

`tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs` — three new cases, built from
the real captured buffer, so the behaviour is pinned **regardless of checkout path**
(the PTY test only caught this by accident of where the repo lives):

- `ExtractResponse_strips_a_prompt_echo_wrapped_at_the_terminal_margin`
- `IsAskingQuestion_ignores_question_mark_in_a_wrapped_prompt_echo`
- `IsAskingQuestion_still_detects_a_question_after_a_wrapped_prompt_echo` — guards
  against the fix simply suppressing all questions.

## Results

| suite | result |
|---|---|
| `CodexResponseAnalyzerTests` | 7/7 pass (4 existing + 3 new) |
| `CodexAdapterLocalShellTests` | 4/4 pass, **from the long worktree path** |
| `Antiphon.SessionRunner.Tests` (CARD-0006's 50) | 50/50 pass |
| full `Antiphon.Tests` | 729 total — run 1: 3 failed / run 2: 2 failed, all load flakes (below) |

The target test `Question_detection_ignores_question_mark_in_prompt_echo` **passed in both**
full runs and in isolation, from the long worktree path.

The residual failures are the documented PTY-timing-under-load family, and the set is not
stable between runs — which is itself the evidence they are load, not logic:

| test | run 1 | run 2 | in isolation |
|---|---|---|---|
| `Session_id_can_be_relaunched_after_exit_but_not_while_running` | fail | fail | **passes** |
| `Send_prompt_clears_live_buffer_before_send` | fail | fail | **passes** |
| `Wait_for_ready_accepts_codex_directory_trust_prompt` | fail | **pass** | — |

Two of these are the pair the brief named. The third, `Send_prompt_clears_live_buffer_before_send`,
was not on that list but is the same family and cannot be mine: it exercises `ClaudeAdapter`,
which uses `ClaudeResponseAnalyzer` (a different class, called without a prompt argument), and it
asserts on `RawSnapshot` — the untouched raw buffer, not `ExtractResponse`. It fails by reading the
*previous* turn's `for 1s` marker before the new output lands, i.e. a turn-boundary race. It passes
in isolation.

## Observation, not fixed

`InteractiveCmdSpec` passes `/k "@echo off & prompt $G"`, intending a one-character `>`
prompt — which would have made the echo far too short to wrap. The captured output shows
the full-path prompt instead, so that argument never takes effect; it is lost in the PTY
library's argument quoting. Worth a look on its own, but chasing it means changing
`PtyAgentRunner`'s command-line construction, which every agent launch goes through — out
of scope here, and the wrap-aware match makes the test robust either way.
