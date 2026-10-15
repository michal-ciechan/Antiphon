# CARD-1273 Standing_history_recovery Grok-on-Herdr cases

Task fecd81fb (Code, investigate first). Follow-up of CARD-1252 (plan
`docs/superpowers/plans/2026-10-14-card-1252-herdr-linux-parity-plan.md`). The method
`HerdrAlwaysOnChannelParityTests.Standing_history_recovery_preserves_native_identity_and_queued_reply`
is skipped on Linux for its eight argument rows by one CARD-0865 guard. Its two Grok-on-Herdr rows
build `FakeHerdrServer` without the CARD-1252 opt-in `GrokStartupScreen`, so their Grok launches
probably cannot become ready (`GrokStartupScreen.Classify`) whatever CARD-0865 says.

## Slices

- S1 (probe): the Linux guard is removed so the eight argument rows run on Linux (CP-1: 8 executed,
  0 skipped) and their failures are recorded. No assertion, deadline or fixture change.
- S2 (fix): CP-1 (run `20261015-003609-6511`) failed each row, for three different reasons
  (evidence file `docs/superpowers/evidence/2026-10-15-card-1273-code.md`). The Grok-on-Herdr
  rows give the fake the captured Grok 1.0.41 dashboard (`GrokStartupScreen`, as CARD-1252) and
  run on Linux. The Linux guard is narrowed to the PtyHost rows (CP-1: TaskCanceledException in
  `ConfirmNativePromptAsync` at the 90 s deadline) and the Claude-on-Herdr rows (queued-reply
  UserPrompt assertion false). No assertion, deadline or other fixture change.
- S2b (diagnostic): CP-2 (run `20261015-005014-8d18`) showed the Grok-on-Herdr rows past launch
  and first delivery but red on the queued-reply UserPrompt assertion, like Claude-on-Herdr. The
  assertion gains a message naming the baseline, the stored body and each UserPrompt row
  (expression unchanged). CP-2 rerun (`20261015-005802-136e`) printed the stored body
  `[antiphon-channel:<id>] <nonce>` (`ChannelPromptCorrelation.Mark`), which the Herdr fixture
  inserts verbatim as the UserPrompt, so `Text == nonce` does not match.
- S3 (guard): the Grok dashboard stays (the launch cause is fixed); the Herdr rows keep a Linux
  guard whose text names the marker mismatch. Deciding whether the assertion, the fixture or the
  product owns that mismatch is outside this card (reported as a finding). No assertion or
  deadline change.
- S4 (repair of Review f2fbd6ae D1): the PtyHost guard is split by kind, with texts from the
  Review's witness run. Claude/PtyHost: the first prompt times out; the fake CLI JSONL exists but
  the transcript normalizer reads no entries from it (cause not established). Grok/PtyHost: the
  matching prompt is in the fake CLI JSONL, but the fake Grok cuts its reply at 60 characters
  (`src/Antiphon.FakeGrok/Program.cs:850`), so the 52-character `[antiphon-channel:<id>] ` marker
  pushes the nonce out of the reply and the `ConfirmNativePromptAsync` wait for it does not
  complete. Follow-up: CARD-1277. Skip reasons only; no assertion, deadline or fixture change.
  Verified by a CP-4 rerun at the repair tip.
- S5 (split for a clean receipt): `Standing_native_wire_missing_target_never_creates` had six
  argument rows; three skip on Linux inside the body and TUnit cannot select single rows, so no
  filter could take its three Linux-running rows without the skips. The method is split
  mechanically into a shared private helper (body unchanged) and three test methods, each carrying
  the guard text it had before: `Standing_native_wire_missing_target_never_creates(kind, backend)`
  for (Grok, PtyHost), (ClaudeCode, Herdr), (Grok, Herdr) with `unavailable` false and no guard;
  `Standing_claude_ptyhost_native_wire_missing_target_never_creates` with the CARD-0878 Linux
  guard; `Standing_grok_unavailable_native_store_never_creates(backend)` for PtyHost and Herdr
  with the CARD-0865 Linux guard. No name is a prefix of another. No assertion, deadline or
  fixture change; Windows still runs all six cases.

## Verification design

- V-1: with the Grok dashboard the Grok-on-Herdr rows get past launch and first delivery
  (CP-1 red at the first delivery without it; CP-2 red later, at the queued-reply assertion).
  The rows are not green on Linux: see S2b/S3.
- R-1: the class keeps its base outcome at the final commit (CP-4, full class).
- R-2: the methods of the class whose cases all run on Linux pass with no skip (CP-5). CP-5 is the
  clean receipt; CP-4 carries the class Linux skips, which the strict receipt validators refuse.

PCs pending for SourceLanding Mutation (none run here):

- PC-1: with the Linux guards lifted, drop `GrokStartupScreen` from the Standing_history_recovery
  fake; both Grok-on-Herdr rows must fail at the first delivery with the
  `FakeHerdrServer never saw pane.send_text` timeout instead of at the queued-reply assertion.
- PC-2: lift the narrowed Linux guards; the Claude/PtyHost, Grok/PtyHost and Herdr rows must go red
  with the reasons their skip texts name.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1273-red/` | probe | `/*/*/HerdrAlwaysOnChannelParityTests/Standing_history_recovery_preserves_native_identity_and_queued_reply*` | V-1 red, per-row reasons | 8 executed, 0 skipped; the Grok-on-Herdr rows red with the not-ready launch failure; the other six rows recorded as observed | 8 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1273-green/` | fixed | `/*/*/HerdrAlwaysOnChannelParityTests/Standing_history_recovery_preserves_native_identity_and_queued_reply*` | V-1 green | the two Grok-on-Herdr rows passed, 0 failed; the six guarded rows skipped on Linux | 2 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | class | `/*/*/HerdrAlwaysOnChannelParityTests/*` | R-1 | 0 failed; skips are the inherited CARD-0865/0878 Linux guards minus the two un-skipped rows | 12 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S5 | `tests/Antiphon.Tests -> bin-c1273s-final/` | final | `/*/*/HerdrAlwaysOnChannelParityTests/*` | R-1 | 0 failed; skips are the Linux guards of the base class (10 passed, 14 skipped in run `20261015-013835-3c17`) | 10 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S5 | CP-4 | final | `/*/*/HerdrAlwaysOnChannelParityTests/(Generic_herdr_codex_banner_is_not_startup_ready*)\|(Herdr_launch_definition_starts_adopts_and_exits*)\|(Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume*)\|(Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry*)\|(Named_placement_refusal_is_non_qualifying_not_a_detect_failure*)\|(Standing_native_wire_missing_target_never_creates*)` | R-2, clean receipt | 10 executed, 10 passed, 0 failed, 0 skipped: the 7 cases of Review 6660ce01 run `20261015-015750-d4f0` plus the 3 cases of `Standing_native_wire_missing_target_never_creates` | 10 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Pass the committed
HEAD as `--expected-source-sha`, use `--total-timeout 150m` without `--keep-outputs`. Linux
builds use `UseAppHost=false`. The checkpoint tool is bootstrapped once through
`scripts/build-slot.ps1` into `bin-c1273-tool/` (disclosed unlisted build).
