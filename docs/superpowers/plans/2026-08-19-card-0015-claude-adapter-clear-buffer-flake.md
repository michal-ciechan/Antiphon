# CARD-0015 — `Send_prompt_clears_live_buffer_before_send` flake: root cause + fix

**Status: FIXED (test-only change, shipped with this plan).**

## Symptom

`ClaudeAdapterLocalShellTests.Send_prompt_clears_live_buffer_before_send` failed under full
parallel suite load, passed 4/4 in isolation. Priority-3 flake; hit again twice the week of
2026-08-19 (CARD-0083 S2 full-suite run; CARD-0045's measurement runs also list it as a pure
load flake).

## Reproduction

10 busy-loop `pwsh` processes on the 8-core box + the single test in a loop:
**12/12 failures** (from ~0/20 unloaded). Failure was always the same assertion:

```
second.RawSnapshot should contain "NEW_CONTENT_Y"
    but was actually "OLD_CONTENT_X for 1s\n"
```

## Root cause (captured, not guessed)

`ANTIPHON_PTY_AUDIT=1` chunk timeline of a failing run:

```
[01:06:19.886] echo OLD_CONTENT_X f^or 1s                                ← typed echo (caret intact — no match)
[01:06:20.167] ESC]0;C:\Windows\system32\cmd.exe - echo  OLD_CONTENT_X for 1s   ← console TITLE
[01:06:20.248] OLD_CONTENT_X for 1s                                     ← the actual output, 81 ms later
```

The test kept the ` for Ns` done marker out of the typed command with a caret (`f^or 1s`),
trusting the marker to first appear in the command's *output*. But interactive cmd.exe sets the
console title to `cmd.exe - <command>` **after parsing** — carets removed — and **before the
command's output prints**. That title OSC chunk contains ` for 1s`, which
`ClaudeCrunchedDetector`'s `" for \d+s"` regex matches (the detector reads the raw buffer,
titles included — by design: the OSC ✳ title is its primary signal for real Claude).

Failure sequence:

1. Turn 1's detector (50 ms poll) fires on the **title** chunk, before the output line exists.
2. The test proceeds; `SendPromptAsync` for turn 2 calls `ClearLiveBuffer`.
3. Turn 1's real output `OLD_CONTENT_X for 1s` lands **after** the clear.
4. Turn 2's detector instantly matches the stale marker; the snapshot is exactly the stale
   line — no `NEW_CONTENT_Y` (its echo hadn't even rendered).

Unloaded, the title→output gap (~80 ms) usually loses to the detector's 50 ms poll cadence plus
the test's inter-turn work, so the clear lands after the output and everything passes. CPU
starvation widens the gap arbitrarily — hence load-only.

## Classification

**Test-construction defect** — the third of the brief's three shapes (the test's own timing
assumptions fail under load), with a twist: it isn't a too-short timeout but a synthetic done
marker that leaks into a channel (the console title) that fires *before* the event it is
supposed to mark. Specifically:

- **Not shared mutable state.** Nothing global; each test owns its adapter and pty. A keyless
  `[NotInParallel]` (the AgentSupervisionTests precedent) would be the wrong fix — it would
  only reduce load and mask frequency; the ordering inversion exists unloaded too.
- **Not a production race.** With real Claude the ✳ idle title and the "Crunched for Ns" text
  are emitted *after* the response, and ConPTY preserves stream order, so the detector's
  ordering assumption holds there. (Production turn tracking has anyway largely moved to
  transcript records — CARD-0055.) `ClearLiveBuffer`-before-send in `ClaudeAdapter` is
  correct; the test was feeding it a marker that violated the marker's own contract.

## Fix (shipped)

`tests/Antiphon.Tests/Agents/ClaudeAdapterLocalShellTests.cs` only:

- The marker text now never appears in **any** command line. A per-test scratch dir
  (`MarkerScript`) holds `emit.cmd` (`@echo %1` + `@type "%~dp0tail.txt"`) and `tail.txt`
  (literally ` for 1s`). Tests type `.\emit.cmd OLD_CONTENT_X`; the marker reaches the pty
  stream *only* as file content, strictly after the turn's content line, so the detector
  cannot fire before the output it signals is in the buffer. Audit-verified: no title OSC
  carries the marker.
- `.\` prefix is required: this machine sets `NoDefaultCurrentDirectoryInExePath=1`, which
  makes cmd refuse implicit current-directory lookup (`emit.cmd` alone → "not recognized").
- The launch spec's broken `/k "@echo off & prompt $G"` payload (audit showed it has *always*
  failed with "is not recognized" through this quoting path — the tests passed in spite of it)
  is dropped for a plain `cmd /d /q`; the misleading error line no longer opens every audit.

## Verification

- Unloaded: class 4/4 green.
- Loaded (10 busy-loop processes on 8 cores, the same conditions that failed **12/12** before
  the fix): **12 consecutive runs of the full class, 0 failures** (48/48 test executions).
