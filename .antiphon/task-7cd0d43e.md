# Group 35a Linux diagnosis and fixes

Task: 7cd0d43e. Base: 5b6a1f9cb14d79b7169a19ba1a6fc2ebb5270d82 (the brief's admitted master baseline). Assigned branch: feat/card-task-7cd0d43e. No desktop/native qualification lock was acquired.

## Baseline evidence

A gated isolated build succeeded: 0 errors, 1197 pre-existing warnings, 458.34 seconds; slot wait 0 seconds, lease held 459 seconds. OutputPath: bin-7cd0d43e/, UseAppHost=false. The 64 MiB checkpoint-reader limitation is the brief's reason for using the build-slot wrapper with dotnet run --no-build.

One gated sequential selection at the clean base executed 62 rows: 22 passed, 8 failed, 32 skipped, test wall time 304.321 seconds; lease held 308 seconds, slot wait 0 seconds. Filter:

`/*/Antiphon.Tests.Application/(SessionQueueReceiptPlumbingTests*)|(HerdrAlwaysOnChannelParityTests*)|(RemoteControlRecoveryPtyIntegrationTests*)|(SessionMessageQueuePtyIntegrationTests*)/*`

| Class | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| SessionQueueReceiptPlumbingTests | 6 | 0 | 16 |
| HerdrAlwaysOnChannelParityTests | 12 | 6 | 6 |
| RemoteControlRecoveryPtyIntegrationTests | 4 | 2 | 2 |
| SessionMessageQueuePtyIntegrationTests | 0 | 0 | 8 |

Generated logs, TRX, HTML and opt-in phase data stay ignored under `.antiphon/evidence/task-7cd0d43e/`. The baseline TRX is `base/base.trx`; source is unchanged at the admitted base. Per-test durations are retained there; external-call and fixture phases without instrumentation are unknown, not zero.

Reproduced clusters:

1. Scripted C514: `C514_Working_modal_waits_for_operator_then_converts_without_replay` and `C514_Internal_queue_drain_creates_no_replay_request` call automated Claude input while the modal is open and receive the intended `modal-blocked` refusal. The operator path is `SendInteractiveInputAsync`; provider-internal activity is distinct from automated server input.
2. Herdr/Claude: both `Standing_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, Herdr, alwaysOn)` rows, `Herdr_launch_definition_starts_adopts_and_exits(ClaudeCode)`, and `Named_AlwaysOn_agent_lands_on_its_labelled_tab_across_repeated_resume` fail because production Herdr reports Unknown input custody. `docs/session-runtime-invariants.md` CARD-0680 explicitly requires that behavior; composer text alone cannot authorize input.
3. PtyHost/Claude: both `Standing_ptyhost_history_recovery_preserves_native_identity_and_queued_reply(ClaudeCode, alwaysOn)` rows reach their existing 90-second deadline waiting for the fake provider's native prompt. The append-only fake does not paint supported Claude composer chrome.

The other 26 native rows did not reproduce on Linux: 16 receipt-plumbing rows, 8 SessionMessageQueuePtyIntegration rows, and 2 real C514 ConPTY rows were explicitly skipped. No native fix is inferred from these skips.

## Final verification

Pending.
