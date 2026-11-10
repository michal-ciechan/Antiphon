# Debug ea58f280 — herdr-alwayson-parity group 74

The second PtyHost Claude launch failed because the startup writer treated FakeClaude's pre-composer banner as a terminal `observation-unknown` refusal. It now waits on the caller's existing probe token and types nothing until the frame is classifiable. Product code changed. Next is review.

Product and why-missed note: `7761c91632f96646b9a851c891884e2954d5d01b` on `feat/card-task-ea58f280`. This file is the commit after that.
Red witness: `1b95d8c2914295255a5a23f8e624adbad1131de5`.
Base: `f125755c1648533c473ce46311c307788b4a9b65`.

## Cause

`HerdrAlwaysOnChannelParityTests.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, True)` failed in 4.669 s on Windows rc run 6591e378 (rc/2026-11-15 = `ac6d00022e940ee3b90584c7d313567b4491c498`). The second fresh PtyHost Claude session was already Failed. `FailureReason` was `Automated Claude input withheld: observation-unknown`. The same method passed for `(Grok, True)`, `(Grok, False)` and `(ClaudeCode, False)`. Stderr was empty. Nothing else in the chunk overlapped. The TRX does not name which observation field produced `observation-unknown`.

The second session's process tree lived about 0.7 s, shorter than the 3000 ms input-probe budget. The first session of the same row had already reached Running, and its marker was in the native JSONL, before the second launch was required.

On this harness AlwaysOn types nothing extra. The boot probe is off, remote control is off, no system-prompt launch note is set, and the second start is `Fresh` with no prompt and nothing pending. The race landed on the AlwaysOn-true row. `(ClaudeCode, False)` takes the same second-start shape and passed.

## Reader

The reader is unchanged. At `f125755c1648533c473ce46311c307788b4a9b65`, which equals the failing rc tip, a `TerminalScreen(120, 30)` fed the FakeClaude startup bytes classifies as follows:

- CRLF paint (`Fake Claude ready`, idle OSC title, CSI `2J`, 60-dash composer, `bypass permissions on`) is Clear.
- The banner alone is Unknown.
- A permission dialog stays Blocked.
- Rules whose `─` was replaced by `U+00C4` stay Unknown.
- The same paint with `\n` and no `\r` stays Unknown.

Slash-palette commit `585b6c1956e60a8b1f27de6eabbc96d8622fed96` does not change that paint. Its four files were not edited. A permanently unreadable composer would have failed every PtyHost Claude start in this serial class. The first session of the failing row reached Running.

A frame that stays unclassified still fails when the caller's token cancels. On the probe path that token is the existing probe budget, and the launch is then not-ready. A blocking dialog stays Blocked.

## Fix

`RunnerTerminalSession.WriteStartupAsync` delays 50 ms and re-reads while custody holds, the detected intent is None, and the frame is neither Clear nor Blocked. That wait sends nothing and does not consume the three stale-observation send attempts. A Blocked dialog or a dialog intent that appears during the wait throws `modal-blocked` with nothing typed. A dialog key that would land in the composer after the banner throws `stale-observation` with nothing typed. A SEND outcome of `observation-unknown`, `modal-blocked`, `generation-mismatch` or `unknown` is still not retried.

## Tests

`UseAppHost=false`, worktree default output, through `scripts/build-slot.ps1`.

- Red `1b95d8c2914295255a5a23f8e624adbad1131de5`: `RunnerStartupStaleObservationTests` 15 total, 4 failed, 11 passed. The four failures threw `observation-unknown` on the banner.
- After the writer change, same class: 15 passed, 0 failed (run before the fix commit, on those bytes).
- Guard union on the fix commit `7761c91632f96646b9a851c891884e2954d5d01b`, filter `/*/*/(TestLaneCategoryGuardTests*)|(ProcessSpawnLimitTests*)|(RunnerStartupStaleObservationTests*)|(HerdrAlwaysOnChannelParityTests*)/*`: 99 total, 93 passed, 0 failed, 6 skipped. The six skips are the existing CARD-0865 and CARD-0878 Linux skips (`AlwaysOn_channel_bound` Herdr and PtyHost Claude, both `Standing_grok_unavailable_native_store_never_creates` rows, `Unsafe_grok_rules_on_the_named_seat_refuse_before_any_pane_and_leave_the_streak_untouched`, `Standing_claude_ptyhost_native_wire_missing_target_never_creates`). The repaired method was not among the skips.

## WHY-MISSED

`docs/investigations/2026-11-10-why-missed-herdr-alwayson-parity-g74.md`

- introducing commit/card: `4de01c9a50ceb5874294b70f79deaa718b57b908` (CARD-0680)
- approving review(s) with scope (Full/Interim) and the selection they actually ran: `f0717652`, scope Interim (commissioned Final; the Unit lane did not run). Reviewed `4c399355c9e12791f6e50bb2641eccae7617b6ea`. Report `eace3de1eb99fd6c6b690ca1f79d1ca2668482c0`. Named 9-class selection, 124 passed, 0 failed. The committed report does not list the nine class names. CARD-1277 CP-3 ran `/*/*/HerdrAlwaysOnChannelParityTests/Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply*` serial on Linux only (`TUNIT_MAX_PARALLEL_TESTS=1`).
- why the failing test was outside that selection (lane/category/path): Integration lane, `[Category("Integration")]`, `tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.StandingRecovery.cs`. A Linux rerun of the Herdr arguments stays green because the banner and the composer paint are already in the pty when the probe snapshots.
- path-glob -> class-filter entry for the impacted-classes table of CARD-1782: `server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs` -> `/*/*/(RunnerStartupStaleObservationTests*)|(HerdrAlwaysOnChannelParityTests*)/*`. Not applied. `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`. The existing `runner-read-poll` row globs that file and names only `Card1761RunnerReadPollTests`.
- same cause as an already written note: none. `docs/investigations/2026-11-09-why-missed-pty-host-fixture.md` is a different cause.

## Land check

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` exited 0. `origin/master` is `a8d830d12f0258a8f133364e58b8de4eb25d77bd`. Result tree `fd25cdcc1d1bd9da9da507356b15957af8a815dc`. No conflicts. Merge-base with this branch is `f125755c1648533c473ce46311c307788b4a9b65`.

If a later Windows launch still fails after the probe budget, read the session's rendered screen at the probe write. A banner that becomes a composer inside 3000 ms is this fix. A screen that is still unclassified at the deadline is a different failure (`Agent process did not become ready`), and the reader must stay as pinned above.
