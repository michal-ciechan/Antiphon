# Why missed: Card1277PtyHostFixtureTests Claude two-write race

Debug task fa691f90. Qualification run 231f573c, chunk `antiphon-all-group-4.trx`, rc/2026-11-13 =
`653f1145de5b72c2617a018cd606e0e8ba3cfe6e` (equal to master at the cut). The Windows read-out is
`docs/investigations/2026-11-09-run-231f573c-pty-host-fixture-windows-failure.md` (sibling task
8ec5e1fc, branch `feat/card-task-8ec5e1fc`).

## Failure

`Antiphon.Tests.Agents.Card1277PtyHostFixtureTests.Claude_two_write_submit_is_a_normalizer_turn`,
`Card1277PtyHostFixtureTests.cs:107`, 1.023 s on DESKTOP-KTLKPIF (8 logical processors):

```
ShouldAssertException: assistant.IndexOf(nonce, StringComparison.Ordinal)
    should be greater than or equal to 0 but was -1
assistant text lost the nonce past the screen cut. ... jsonl=<user record only> ... assistant=
```

The screen ended at `SUBMITTED:...` with no `FAKE response to:` line, and the JSONL held only the
user record. The chunk had 140 tests, 139 passed. This was the first failure in 22 Windows Full runs.

## Classification

Test-helper defect (a wait race), present since the test was written. It shows on a loaded host. It
is not a product regression, and the rc change set did not cause it.

- `src/Antiphon.FakeClaude/Program.cs` `SubmitTurn` appends the user record first. It then paints
  `FAKE response to:` and appends the assistant record last.
- The test polled the JSONL only until `"type":"user"` appeared. It then captured the screen and
  asserted on the assistant record and the response line. Under load the poll woke between the two
  appends, and the 8 s budget never came into play.
- No file the fixture runs changed between the previous run's rc (`5fef2c23b`, a6410818, green 0.454 s)
  and `653f1145d`. The fakes, `Antiphon.Agents.Pty`, `Antiphon.PtyHost`, the normalizers,
  `TestAppHostPath` and `ProcessSpawnLimit` are all unchanged. The only test-host change is
  CARD-1790 (`e79c0e03b`, `9b326dfdd`, `fae5d39be`, `3714b0f52`): it compiles
  `src/Antiphon.Hosting/PwshPrivateCache.cs` into every `tests/*` host through
  `Directory.Build.targets`. On Windows that adds one temp directory and an `XDG_CACHE_HOME` value
  that the fake inherits and ignores. It does not touch the fakes, which live under `src/`. The
  CARD-1790 hypothesis is rejected: the evidence puts the poll inside the fake's own
  append-to-append gap.
- Linux at `653f1145d`: the class passed 2/2. The race needs a loaded host.

## Introducing commit and its verification

- Introduced by `5e21b61a2` (CARD-1277 S1, 2026-10-15, "pin the PtyHost transcript nonce and the
  Claude two-write submit"), Code task 7ec1da62. The predicate `t.Contains("\"type\":\"user\"")` was
  in that first version and was never changed.
- Plan `docs/superpowers/plans/2026-10-15-card-1277-ptyhost-standing-recovery-plan.md` `### Checkpoints`
  had three rows. CP-1 (red) and CP-2 (green) ran the two Card1277 methods. CP-3 ran
  `HerdrAlwaysOnChannelParityTests/Standing_ptyhost_history_recovery_*`. Every row was
  `Serial=true` with `TUNIT_MAX_PARALLEL_TESTS=1`, on server2 Linux. The receipts are in
  `docs/superpowers/evidence/2026-10-15-card-1277-code.md`.
- Review: the card closed with "Remaining: PCs pending for the SourceLanding Mutation; Interim
  review; a fuller rerun was not claimed". The card record shows only an Interim review, so no Final
  Review is on record that checked the poll predicate against the assertions. No mutation removed the wait. That mutation would survive on an idle host, so it
  would show the wait was untested.

## Why this class was outside the selection

1. Every pre-land run was serial and alone on an idle Linux host. There the gap between the fake's
   user append and its assistant append is far below one 50 ms poll tick, so the race cannot open.
2. The pin was written for Linux (the class summary says "Linux PtyHost pins"). Windows ConPTY saw it
   only in Full runs, where it passed 21 times. The race needs one poll to wake inside a gap of a few
   tens of milliseconds while about 15 Kestrel and wire tests share 8 CPUs.
3. The Grok sibling in the same class waits for the screen cut and for `turn_completed`, which ends
   the turn. Only the Claude method waited for the start of the turn. A review comparing the two
   would have caught the gap. No such review ran.
4. Nothing in the rc change set touches the fixture's inputs. No selector, including one keyed on
   the limiter, the spawn helpers or `Directory.Build.targets`, would have picked this class for
   `653f1145d`. The defect is a load-sensitive wait, not a regression.

## Fix

- Red `ead5dec3dce93a121f20ceb891c9c17df03ec3e5`. This commit extracts the wait predicate as
  `ClaudeTurnRecorded`. It adds `Claude_wait_does_not_end_on_the_user_record_alone`, which feeds
  the predicate the exact user-only JSONL captured on Windows. The run was 1 executed, 1 failed.
- Fix `4b9ca585529a4da727beb202c9ef4ecb2800c61b`. The predicate now requires the `"type":"assistant"`
  record, which fakeclaude writes last. The test also waits for the `FAKE response to: <60 chars>`
  screen line before `SnapshotText()`. Both waits keep their 8 s budgets. No assertion, budget or
  retry changed. Class run: 3 executed, 3 passed, 0 skipped (Linux).

## CARD-1801 selector row

This row selects the class when the fixture's own inputs change. It also covers the test-host,
limiter and spawn-helper paths the brief names, which would catch a real interaction there. It
would not have selected the class for this rc. The defect sat in the test since `5e21b61a2`, so
the row is about future edits, not this miss.

```json
{
  "id": "pty-host-fixture",
  "globs": [
    "tests/Antiphon.Tests/Agents/Card1277PtyHostFixtureTests.cs",
    "src/Antiphon.FakeClaude/**",
    "src/Antiphon.FakeGrok/**",
    "src/Antiphon.Agents.Pty/**",
    "src/Antiphon.PtyHost/**",
    "src/Antiphon.SessionRunner/TranscriptNormalizer.cs",
    "src/Antiphon.SessionRunner/GrokTranscriptNormalizer.cs",
    "tests/Shared/TestAppHostPath.cs",
    "tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs",
    "src/Antiphon.Hosting/PwshPrivateCache.cs",
    "Directory.Build.targets"
  ],
  "classes": ["Card1277PtyHostFixtureTests"],
  "windowsRequired": true
}
```

The class takes about 1 s, or up to 2.5 s on a loaded Windows host (Full-run range 0.41-2.47 s). Run the selector without
`TUNIT_MAX_PARALLEL_TESTS=1`, so the run shares the CPU the way a Full chunk does. A serial idle
run is exactly the condition that hid this race.

## Prevention

A fixture wait should end on the last record the test asserts on, never on the first record of a
multi-record turn. When a Review checks a test that polls a file or screen and then asserts, it
should check that the poll's done predicate covers every assertion that follows it.
