# Why missed: g75 HerdrAlwaysOnChannelParity red

Debug task 9074945e. Qualification run `d88de419`, chunk `antiphon-all-group-75`,
rc/2026-11-16 = `6f8741d872d63507827014f972ccf71196722d4a`. The Windows read-out is
`docs/investigations/2026-11-10-run-d88de419-g75 HerdrAlwaysOnChannelParity red-windows-failure.md`
on `origin/feat/card-task-9b049f83` at `39b49cd9cae7a77693b071207d348c8f0aa8e8e7`. CARD-1781.

## Failure

`HerdrAlwaysOnChannelParityTests.Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, False)`
failed in `00:00:02.2792465`. `RequireCompletedAsync` at `StandingRecovery.cs:287` saw
`SessionStatus.Failed`. `FailureReason` was `Automated Claude input withheld: stale-observation`.
The stack's first caller is line 67, the first PtyHost session. Stderr was absent. No other
test in the chunk overlapped the row. The chunk was 453 executed, 452 passed, 1 failed.

`(ClaudeCode, True)` passed in the same chunk in 7.266s, including the full three-launch body.
That is the row `b6bbfa26be15fad1fb6a6d23d0ba75fd2b1bd2dc` fixed. Run 6591e378 group 74 failed
`(ClaudeCode, True)` with `observation-unknown` and passed `(ClaudeCode, False)`. Final-result
note: `7a3c45c3f82e36c7b992d5b4b66316cd52127b47` on `origin/feat/card-task-d6579463`. Predecessor
why-missed: `docs/investigations/2026-11-10-why-missed-herdr-alwayson-parity-g74.md`.

## Classification

Product defect in the startup writer. This is not a fixture timeout and not an expected Windows skip.

`WriteStartupAsync` waits, and writes nothing, while custody holds and the frame is neither
Clear nor Blocked. After that wait, any input that is not a composer probe token threw
`stale-observation` before a byte. `ComposerInputProbe.TokenFor` (`zz` plus 8 hex digits)
passes `IsComposerProbeToken`. `ComposerInputProbe.KillLine` (`\x15`) does not: length 1, not
a letter or digit. Each probe write is its own `WriteStartupAsync` call, so the flag does not
carry from the token write into the kill-line write. A kill-line call whose first snapshot is
still the banner, and whose next snapshot is the composer, took the new guard and failed the
launch. `ClaudeStartupReadiness` does not catch `ProviderDialogInputRefusedException`.

The 2.279s row fits one 50ms unclassified wait plus harness setup. It does not fit the 3000ms
probe budget. The dialog key `"j"` stays refused. The kill line is not a dialog answer.

## Fix

Once the unclassified frame has become the composer, `WriteStartupAsync` sends the probe token
and Ctrl+U. A dialog key is still refused with nothing typed. A Blocked dialog or a dialog
intent during the wait is still `modal-blocked`. Custody failures are still immediate
`observation-unknown`.

Red witness `af1df40d6be2aa018dfdf818e8fa8353615ebbc5`:
`A_kill_line_is_sent_once_the_composer_replaces_an_unclassified_banner`, 1 total, 1 failed.
The failure was `ProviderDialogInputRefusedException: Automated Claude input withheld: stale-observation`
at `RunnerTerminalSession.WriteStartupAsync` line 171. After this note's writer change the same
class was 16 passed, 0 failed. The `"j"` refusal stayed in that run.

## Introducing commit/card

introducing commit/card: `b6bbfa26be15fad1fb6a6d23d0ba75fd2b1bd2dc` (no card id in the message; cherry-pick of `7761c91632f96646b9a851c891884e2954d5d01b`)

That commit waits on an unclassified Claude frame and then throws `stale-observation` when the
input is not a composer probe token. The red witness for the old `observation-unknown` banner
is `17500c844f94d36792c64d2e34673e842a3cfca9` (cherry-pick of `1b95d8c2914295255a5a23f8e624adbad1131de5`).
Those tests write `"zz"` and `"j"`. They do not write the kill line.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No Review task id for `b6bbfa26b` is in this checkout. The green selection recorded with the
  fix, in `docs/investigations/2026-11-10-why-missed-herdr-alwayson-parity-g74.md`, is
  `RunnerStartupStaleObservationTests`: 15 passed, 0 failed, after the writer change. That is a
  Unit class. It does not execute
  `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, False)`.
- The predecessor Interim review `f0717652` (report `eace3de1eb99fd6c6b690ca1f79d1ca2668482c0`)
  approved CARD-1668 (`4c399355c9e12791f6e50bb2641eccae7617b6ea`), not this commit. Its Unit
  selection was the named 9-class run, 124 passed, 0 failed, and did not run this Integration
  method. Quoted so a later reader does not treat that review as coverage of `b6bbfa26b`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane,
`[Category("Integration")]`, path
`tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.StandingRecovery.cs`,
method argument `(ClaudeCode, False)`, PtyHost.

The green class run is Unit. On that run the banner-then-composer case writes the probe token
`"zz"`, which the guard allows, and the dialog key `"j"`, which the guard refuses on purpose.
The Windows integration row types through `ClaudeStartupReadiness` after a real FakeClaude
banner. The kill-line write is a second `WriteStartupAsync` call. A first snapshot that is
still unclassified, followed by the composer, refused `"\x15"` with `stale-observation` and
nothing typed. The Unit selection never made that call.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs` -> `/*/*/RunnerStartupStaleObservationTests/*`

The new row is banner, then composer, write `ComposerInputProbe.KillLine`, expect the conditional
send, zero `stale-observation`. The existing `"j"` row must stay a refusal. The Integration method
is the launch-shaped check and is the row this chunk failed:

`server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs` -> `/*/*/HerdrAlwaysOnChannelParityTests/Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply*`

Not applied. `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`.

## Exemption

same cause as an already written note: none

`docs/investigations/2026-11-10-why-missed-herdr-alwayson-parity-g74.md` is the predecessor.
Its row was `(ClaudeCode, True)` with `observation-unknown`, and that row passed in this chunk.
This note is the `(ClaudeCode, False)` `stale-observation` refusal added by the same writer.
