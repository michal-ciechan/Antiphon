# Why missed: herdr-alwayson-parity group 74

Debug task ea58f280. Qualification run 6591e378, chunk `antiphon-all-group-74`, rc/2026-11-15 =
`ac6d00022e940ee3b90584c7d313567b4491c498`. The Windows read-out is
`docs/investigations/2026-11-10-run-6591e378-herdr-alwayson-parity-g74-windows-failure.md`
on `origin/feat/card-task-16be0469` at `f9376388b487067d67ee4f7df735e1c6126bd2fb`.

## Failure

`HerdrAlwaysOnChannelParityTests.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, True)`
failed in 4.669 s. The second fresh PtyHost Claude session
(`StandingRecovery.cs` launch, `RequireCompletedAsync`) was already Failed.
`FailureReason` was `Automated Claude input withheld: observation-unknown`.
The same method passed for `(Grok, True)`, `(Grok, False)` and `(ClaudeCode, False)`.
`AlwaysOn_channel_bound_survives_child_death_and_replies(PtyHost, ClaudeCode)` passed.
Stderr was empty. Nothing else in the chunk overlapped. The TRX does not say which
observation field produced `observation-unknown`.

The second session's process tree lived about 0.7 s, shorter than the 3000 ms input-probe
budget. The first session of the same row had already reached Running and its marker was
in the native JSONL before the second launch was required.

## Classification

Product defect in the startup writer. The dialog reader is not the defect, and this is
not an expected Windows skip.

`ClaudeDialogObservation` at `f125755c1648533c473ce46311c307788b4a9b65` equals the failing
rc tip. Fed through `TerminalScreen(120, 30)`, the FakeClaude startup bytes
(`Fake Claude ready`, the idle OSC title, CSI `2J`, and the 60-dash composer with
`bypass permissions on`) classify as Clear. The banner alone is Unknown. A permission
dialog stays Blocked. Rules whose `─` was replaced by `U+00C4` stay Unknown. The same
paint with `\n` and no `\r` stays Unknown. The slash-palette commit
`585b6c1956e60a8b1f27de6eabbc96d8622fed96` does not change that paint. Its four files
were not edited.

A permanently unreadable Windows composer would have failed every PtyHost Claude start
in this serial class. The first session of the failing row, the `(ClaudeCode, False)`
row, and the channel-bound PtyHost Claude row all became Running. AlwaysOn does not add
a typed write on this harness: the boot probe is off, remote control is off, no
system-prompt launch note is set, and the second start is `Fresh` with no prompt and
nothing pending.

FakeClaude writes the banner before the composer paint. The quiet period can close on
that visible banner. `ComposerInputProbe` then calls `WriteStartupAsync`. On an
unclassified frame `ProviderDialogObservationService.Request` throws `observation-unknown`
before any byte and before the probe can poll. `ClaudeStartupReadiness` does not catch
that exception, so the launch is stored as Failed.

## Fix

`WriteStartupAsync` now waits on the caller's token, and writes nothing, while custody
holds and the frame is neither Clear nor Blocked and the detected intent is None.
Those waits do not consume the three stale-observation send attempts. A Blocked dialog
or a dialog intent that appears during the wait throws `modal-blocked` with nothing
typed. A dialog key that would land in the composer after the banner throws
`stale-observation` with nothing typed. A frame that stays unclassified fails only when
the caller's token cancels. On the probe path that token is the existing probe budget,
and the launch is then not-ready. A blocking dialog is still not Clear. A SEND outcome
of `observation-unknown`, `modal-blocked`, `generation-mismatch` or `unknown` is still
not retried.

Red witness `1b95d8c2914295255a5a23f8e624adbad1131de5`: class run 15 total, 4 failed,
11 passed. The four failures threw `observation-unknown` on the banner. After this
commit's writer change the same class was 15 passed, 0 failed.

## Introducing commit/card

introducing commit/card: `4de01c9a50ceb5874294b70f79deaa718b57b908` (CARD-0680)

That commit guarded automated startup writes and made an unclassified snapshot throw
`observation-unknown` before any byte. CARD-1668
(`4c399355c9e12791f6e50bb2641eccae7617b6ea`) retries only `stale-observation` and left
the `Request` refusal in place.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `f0717652`, scope Interim. The commissioned profile was Final. The report says the
  Unit lane did not run, so the ordinary scope is Interim. Reviewed
  `4c399355c9e12791f6e50bb2641eccae7617b6ea`. Report
  `eace3de1eb99fd6c6b690ca1f79d1ca2668482c0`. Named 9-class selection: 124 total,
  124 passed, 0 failed. `RunnerStartupStaleObservationTests` had 9 tests. The committed
  report does not list the nine class names. It checked that `Request` refusals
  (`ModalBlocked` / `ObservationUnknown` for intent None) still happen before the send.
- CARD-1277 plan
  `docs/superpowers/plans/2026-10-15-card-1277-ptyhost-standing-recovery-plan.md`
  CP-3 filter
  `/*/*/HerdrAlwaysOnChannelParityTests/Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply*`,
  Serial=true, `TUNIT_MAX_PARALLEL_TESTS=1`, server2 Linux, expect 4 passed. The
  qualifying rerun recorded there is S3 `20261015-083106-71b7`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane,
`[Category("Integration")]`, path
`tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.StandingRecovery.cs`.

The CARD-1668 review ran a Unit selection. This class is Integration, so that selection
does not execute it. CP-3 ran this method only as a serial Linux checkpoint. On Linux
the banner and the composer paint are already in the pty when the probe snapshots, so
the first frame is Clear. The Windows failure is a snapshot that is still the banner.
A Linux rerun of the Herdr arguments stays green. The impacted-classes kind
`runner-read-poll` already globs `RunnerTerminalSession.cs` and names only
`Card1761RunnerReadPollTests`.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs` -> `/*/*/(RunnerStartupStaleObservationTests*)|(HerdrAlwaysOnChannelParityTests*)/*`

Not applied. `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`. The Unit
class is the cheap check. The Integration class is the launch-shaped check.

## Exemption

same cause as an already written note: none

`docs/investigations/2026-11-09-why-missed-pty-host-fixture.md` is a different cause
(`Card1277PtyHostFixtureTests` waited for the user JSONL record only).
