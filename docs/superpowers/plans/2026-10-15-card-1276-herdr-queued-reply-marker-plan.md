# CARD-1276 Herdr queued-reply marker rows

Task c32926d8 (Code, tests first). Follow-up of CARD-1273 (plan
`docs/superpowers/plans/2026-10-15-card-1273-standing-recovery-grok-herdr-plan.md`, evidence
`docs/superpowers/evidence/2026-10-15-card-1273-code.md`). Base master `78e400178`.

## Decision: the fixture's assertion is wrong, not the product

`HerdrAlwaysOnChannelParityTests.Standing_history_recovery_preserves_native_identity_and_queued_reply`
inserts a legacy unmarked Channel row (`Body = nonce`) and asserts a UserPrompt with
`Text == nonce`. The product marks a Channel row before its first typed attempt
(`ChannelPromptCorrelation.PrepareFirstAttempt` in `server/Application/Services/ChannelPromptCorrelation.cs`,
called from the queue flush and send-now paths in `SessionMessageQueueService`; new rows are
marked at insert by `ChannelPromptCorrelation.Mark`). The owner docs require the marker in the
submitted prompt: `docs/session-runtime-invariants.md` (CARD-0584 bullet: "Every newly persisted
Channel row carries a server-owned `[antiphon-channel:<queue-id:N>]` outer marker before input",
matching needs the "literal full marker and complete body") and `docs/telegram.md` ("A real
submitted prompt must contain the literal marker and the entire stored body"). The matcher
`ChannelPromptCorrelation.Matches` rejects a marked row whose prompt lacks the marker
(`missing-marker`). A provider transcript records the typed text, so the UserPrompt of the queued
reply is `[antiphon-channel:<queue-id:N>] <nonce>`. CARD-1273 run `20261015-005802-136e` printed
exactly that stored body and UserPrompt row. The marker is stripped only from human-facing replies
(`ChannelReplyPreparation`, `ChannelReplyDispatcher`), which the test's final
`reply.Text.ShouldContain(nonce)` already covers.

## Slices

- S1 (red): the eight-row method is split by backend into a shared private helper
  `StandingHistoryRecoveryAsync` (body unchanged) and two test methods:
  `Standing_history_recovery_preserves_native_identity_and_queued_reply(kind, backend, alwaysOn)`
  for the four Herdr rows, and
  `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(kind, alwaysOn)`
  for the four PtyHost rows with the CARD-1277 Linux guards (texts unchanged). Neither name is a
  prefix of the other. The CARD-1273 Herdr Linux guard is removed. The assertion is unchanged, so
  the Herdr rows run on Linux and go red at `Text == nonce` (CP-1).
- S2 (fix): the test states the literal wire body
  `$"[antiphon-channel:{queuedId:N}] {nonce}"`: the moved row's `Body` equals it, the UserPrompt
  after the delivery baseline has `Text` equal to it, and on Herdr a `pane.send_text` request
  carried it (the typed text, not only the fixture's copy of the stored body). The PtyHost branch
  keeps its native-prompt containment check. No deadline, guard or product change.

## Verification design

- V-1: the Herdr rows of `Standing_history_recovery_preserves_native_identity_and_queued_reply`
  run on Linux with no skip: red at S1 on the `Text == nonce` assertion (CP-1), green at S2 (CP-2).
- R-1: the class keeps its other outcomes at S2 (CP-3, full class). Base reference: CARD-1273
  run `20261015-013835-3c17` (cited in its evidence file).
- R-2: the methods of the class whose cases run on Linux pass with no skip (CP-4, clean receipt):
  the CARD-1273 CP-5 selection plus the Herdr method.

PCs pending for SourceLanding Mutation (none run here):

- PC-1: in `ChannelPromptCorrelation.PrepareFirstAttempt`, leave the body unmarked; the Herdr rows
  must fail at `moved.Body.ShouldBe(wire)` (or at the `pane.send_text` wire check).
- PC-2: in `ChannelPromptCorrelation.Mark`, write the id in `D` format instead of `N`; the Herdr
  rows must fail at the literal wire assertions.
- PC-3: in the S2 test, expect the bare nonce again (`t.Text == nonce`); the Herdr rows must fail
  as in CP-1.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1276-red/` | red | `/*/*/HerdrAlwaysOnChannelParityTests/Standing_history_recovery_preserves_native_identity_and_queued_reply*` | V-1 red | 4 executed, 0 skipped; red at the `Text == nonce` assertion | 4 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1276-green/` | green | `/*/*/HerdrAlwaysOnChannelParityTests/Standing_history_recovery_preserves_native_identity_and_queued_reply*` | V-1 green | 4 executed, 4 passed, 0 failed, 0 skipped | 4 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | class | `/*/*/HerdrAlwaysOnChannelParityTests/*` | R-1 | 0 failed; skips are the base class Linux skips of run `20261015-013835-3c17` minus the four Herdr rows | 14 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | CP-2 | clean | `/*/*/HerdrAlwaysOnChannelParityTests/(Generic_herdr_codex_banner_is_not_startup_ready*)\|(Herdr_launch_definition_starts_adopts_and_exits*)\|(Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume*)\|(Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry*)\|(Named_placement_refusal_is_non_qualifying_not_a_detect_failure*)\|(Standing_native_wire_missing_target_never_creates*)\|(Standing_history_recovery_preserves_native_identity_and_queued_reply*)` | R-2, clean receipt | 0 failed, 0 skipped: the CARD-1273 CP-5 selection plus the four Herdr rows | 14 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Pass the committed
HEAD as `--expected-source-sha`, use `--total-timeout 150m` without `--keep-outputs`. Linux
builds use `UseAppHost=false`. CP-1 runs at the S1 commit; CP-2 to CP-4 run at the S2 commit.
The checkpoint tool is bootstrapped once through `scripts/build-slot.ps1` into `bin-c1276-tool/`
(disclosed unlisted build).
