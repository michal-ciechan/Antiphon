# CARD-1508 round-1 repair report

B-1 is fixed. AccountBootstrap is the fatal pre-prompt frame only. A composer that quotes the account/read sentence stays pointer-readable (IsComplete true, prefix equals the normalized sentence, TryOutcome null). A ready rendered screen stays Ready when the three phrases are only in the raw tail. Flatten runs AnsiStripper.Clean first, so a CSI-colored fatal frame still classifies AccountBootstrap and the clause matches the uncolored 217-character outcome. Sign-in still wins. A bare -32603 and a usage-limit -32603 above an idle composer stay Ready. FailureCode and LaunchBlock stay unset.

## Identity

- Branch: feat/card-task-5f302fe2
- Worktree: /work/worktrees/task-5f302fe2
- Continued: origin/feat/card-task-64e5af25 at e25cebdf5f2c357c36f515de3836389c8379db97
- Landing-owner Code task: 64e5af25-be6e-45e2-ac8d-64f9e7c79f8a
- This Code task: 5f302fe2-6734-45d5-951f-66f9fed2bb4b
- Red: 3d78de4f998e94d2f788a23c51498bbc93b758e7
- Fix and tested SHA: 304b6097b4599f89a433adc2b6d7f0cb606310d9
- No Checkpoints table. No plan file. No V-n/R-n rows.
- Review evidence: origin/feat/card-task-7e1aae7e:.antiphon/task-7e1aae7e.md
- Policy hash unchanged. Restart none. Nothing deployed.

## Files

- src/Antiphon.Agents.Pty/CodexAccountBootstrap.cs — IsFatal returns false when CodexStartupScreen.IsLiveSession is true. Flatten runs AnsiStripper.Clean before the clause is built.
- src/Antiphon.Agents.Pty/CodexStartupReadiness.cs — IsLiveSession is a bottom composer, or the OpenAI Codex banner plus the model-effort cwd footer. Sign-in is still checked first.
- tests/Antiphon.Agents.Pty.Tests/CodexAccountBootstrapTests.cs — 11 witnesses: quoting composer, raw-tail-only on P3, agent message, scrollback, code block, partial composer, glyphless row, resumed history, wrapped quote, composer plus raw tail, colored fatal frame.

## Runs on the tested SHA

Red at 3d78de4f9, CodexAccountBootstrapTests: total 20, failed 11, succeeded 9. Build slot db1de024-fa74-458d-bb57-7c420fd7d028 waited=0 held=12s. Run slot 66a5d0d3-4a0d-4776-ae9a-4f6b795d1378 waited=0 held=3s.

Green on 304b6097b4599f89a433adc2b6d7f0cb606310d9:

- Fix build slot e1d0e6dd-f64f-4d35-947d-02fe56fdf4ef waited=0 held=9s. Class then 20 succeeded, 0 failed. Slot 4617cacf-6019-4cb4-a76c-6624e59e1ac6 waited=0 held=2s.
- Pty CodexAccountBootstrapTests|CodexStartupReadinessTests|CodexReadyWaitTests: 70 succeeded, 0 failed. Slot 81a65d2f-6403-4a67-a52c-9dc05019eb28 waited=0 held=4s.
- Antiphon.Tests build, 0 errors. Slot 5014f09e-9860-4c6b-bf88-da64829cb6d1 waited=0 held=213s.
- CodexAccountBootstrapFailureTests|RunnerCodexAdapterReadyTests|AgentSessionLaunchFailureTests|SessionMessageQueueCodexPointerTests|Card1277PtyHostFixtureTests: 121 succeeded, 0 failed, 1m 14s. Slot dacca1da-28e0-4631-8168-919a4a127276 waited=0 held=77s. impacted-classes selected only Card1277PtyHostFixtureTests; it is in that run.
- Nine-class guard union: 78 succeeded, 0 failed, 2m 39s. Slot cbf92b1a-534f-49ae-b5c1-7f1dc5d74d7d waited=0 held=162s.
- scripts/check-evidence-diff.ps1 base e25cebdf5f2c357c36f515de3836389c8379db97 head 304b6097b4599f89a433adc2b6d7f0cb606310d9: commits=2 entries=0 violations=0 exit 0.
- origin/master 057bf1f0361bb507dd11a818763e9e094fe1d192. git merge-tree --write-tree origin/master HEAD tree 9daf477a6ceaec81cd7544d0d49743f3b9905bcd exit 0.

## Local mutants

Restored with `git checkout -- src/Antiphon.Agents.Pty/CodexAccountBootstrap.cs`. SourceLanding Mutation still owns the PC cycles.

- Live-session early return removed: total 20, failed 10, succeeded 10. Build slot 14e4ee01-1540-4735-a066-337593cf9645 waited=15s held=41s. Run slot b9541063-fbb1-4bc1-a1bb-0760187e3f2e waited=45s held=3s.
- AnsiStripper.Clean removed: total 20, failed 1 (Colored_fatal_frame_clause_drops_csi), succeeded 19. The clause contained `[31m-32603` and lost the closing parenthesis. Build slot bfbd2352-24b5-4b2e-9d91-39720f20e0ba waited=15s held=18s. Run slot 588f16ad-77e2-4d09-a4f4-e249e83a4946 waited=0 held=6s.
- Restore green: 20 succeeded, 0 failed. Build slot 397336e4-eede-41ad-b1c5-a6c3ecc9731f waited=0 held=14s. Run slot 08a5122c-6968-4c63-81d3-2ca8b3adbfa0 waited=30s held=7s.

## PCs pending

Method-scoped SourceLanding Mutation still owns every PC.

- PC-1 Exact_pre_prompt_frame_is_account_bootstrap
- PC-2 and PC-3, unnamed; review 7e1aae7e did not re-run them
- Quoting_composer_stays_readable
- Ready_screen_with_the_sentence_only_in_the_raw_tail_stays_ready
- Sentence_in_an_agent_message_above_a_ready_composer_stays_ready
- Sentence_in_scrollback_above_the_banner_stays_ready
- Sentence_in_a_code_block_above_a_ready_composer_stays_ready
- Partially_drawn_composer_prefix_stays_readable
- Glyphless_partial_row_on_ready_chrome_stays_unknown
- Resumed_session_with_the_old_error_in_history_stays_ready
- Wrapped_quote_in_the_composer_stays_readable
- Composer_and_raw_tail_quoting_the_sentence_stay_readable
- Colored_fatal_frame_clause_drops_csi
