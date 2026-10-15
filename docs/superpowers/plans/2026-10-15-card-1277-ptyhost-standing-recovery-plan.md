# CARD-1277 PtyHost standing-recovery rows

Task 7ec1da62 (Code, tests first). Follow-up of CARD-1273 (evidence
`docs/superpowers/evidence/2026-10-15-card-1273-code.md`) and CARD-1276 (plan
`docs/superpowers/plans/2026-10-15-card-1276-herdr-queued-reply-marker-plan.md`).
Base `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0`.

## Decision

`Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply` stays skipped
on Linux under CARD-1277. The witness in the CARD-1273 evidence file names two fixture defects.
The product assertions stay: `ConfirmNativePromptAsync` waits for AssistantText that contains
the nonce, and `VerifiedPromptSubmitter` keeps its body write followed by a separate CR.

- Grok: `updates.jsonl` records the queued UserPrompt as the channel wire and the assistant
  text as `FAKE response to: ` plus the first 60 characters of that wire. The screen cut is
  the contract pinned by
  `FakeClaudeContractTests.Multi_line_paste_then_lone_cr_submits_whole_body_with_escaped_marker`
  (`FAKE response to: ` + escaped[..60], and the screen must not contain escaped[..61]).
  The same cut is stored as the transcript assistant text. The screen echo stays at 60
  characters. The transcript assistant text becomes the escaped prompt, so the nonce past
  that cut remains in AssistantText.
- Claude: `TryEnableRawConsole` returns off Windows, so the Linux slave stays canonical. A
  body and a later CR then arrive as one read with a trailing newline, `SubmitTurn` does not
  run, and `conversation.jsonl` stays the empty file created at startup.
  `TranscriptNormalizer` yields no parts. The fixture takes Linux stdin out of canonical mode
  inside `TryEnableRawConsole` (clear ICANON, the echo local flags, IEXTEN, ISIG, and ICRNL,
  with VMIN 1 and VTIME 0). A trailing CR in the same write stays a non-submit
  (`FakeClaudeContractTests.Text_and_CR_in_one_write_does_NOT_submit`).
  `ANTIPHON_FAKE_LF_ENTER` stays unset. The Claude transcript assistant text uses the same
  split as Grok: screen echo at 60 characters, transcript text the escaped prompt.

`Card1277PtyHostFixtureTests.Grok_jsonl_reply_keeps_the_nonce_past_the_screen_cut` and
`Card1277PtyHostFixtureTests.Claude_two_write_submit_is_a_normalizer_turn` are the pins.
If the Claude red shows a UserPrompt already, the raw-mode edit is the wrong fix and stays
out; the failure text (raw JSONL and screen) decides.

## Slices

- S1 (red): the two methods above, with the CARD-1277 `Skip.Test` calls left in place.
  CP-1 expects both methods failed.
- S2 (fix): FakeGrok and FakeClaude store the escaped prompt in the transcript assistant
  text and keep the 60-character screen echo. FakeClaude enables the Linux raw stdin above
  when the S1 failure shows no UserPrompt. The two CARD-1277 `Skip.Test` calls come out.
  CP-2 expects both methods passed. CP-3 expects the four PtyHost rows passed, 0 skipped.

## Verification

- V-1: the two new methods fail at S1 (CP-1) and pass at S2 (CP-2).
- V-2: the four rows of
  `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply` pass with
  the CARD-1277 skips removed (CP-3).

PCs pending for SourceLanding Mutation (not run here):

- PC-1: restore the 60-character cut on the FakeGrok updates.jsonl assistant text.
  `Grok_jsonl_reply_keeps_the_nonce_past_the_screen_cut` fails because the nonce is absent
  from the assistant text.
- PC-2: remove the Linux `tcsetattr` call. `Claude_two_write_submit_is_a_normalizer_turn`
  fails because the normalizer yields no UserPrompt for the two-write body.
- PC-3: restore the 60-character cut on the FakeClaude conversation.jsonl assistant text.
  `Claude_two_write_submit_is_a_normalizer_turn` fails because the nonce is absent from
  the assistant text.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1277-red/` | red | `/*/*/Card1277PtyHostFixtureTests/(Grok_jsonl_reply_keeps_the_nonce_past_the_screen_cut*)\|(Claude_two_write_submit_is_a_normalizer_turn*)` | V-1 red | 2 executed, 2 failed, 0 skipped | 2 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1277-green/` | green | `/*/*/Card1277PtyHostFixtureTests/(Grok_jsonl_reply_keeps_the_nonce_past_the_screen_cut*)\|(Claude_two_write_submit_is_a_normalizer_turn*)` | V-1 green | 2 executed, 2 passed, 0 failed, 0 skipped | 2 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | ptyhost | `/*/*/HerdrAlwaysOnChannelParityTests/Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply*` | V-2 | 4 executed, 4 passed, 0 failed, 0 skipped | 4 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Pass the committed
HEAD as `--expected-source-sha`, use `--row-timeout 40m` and `--total-timeout 150m`, without
`--keep-outputs`. Linux builds use `UseAppHost=false`. CP-1 runs at the S1 commit. CP-2 and
CP-3 run at the S2 commit. The checkpoint tool is bootstrapped once through
`scripts/build-slot.ps1` into `bin-c1277-tool/` (disclosed unlisted build).
