# CARD-0418 round 22: large broker payload and assertion-level accounting

This continues the [round-21 ledger](2026-09-29-card-0418-round-21-evidence.md).
The tested implementation tip is `44bc041f12374beaf7f3b286465679143324cc02`.
No shared-stack restart, live destination, land, or SourceLanding Mutation was attempted.

## New executed assertions

**V-23 / R-13, near-default broker and key portion only.**
`KafkaOutboundPayloadTests.Near_default_serialized_reply_crosses_the_real_broker_with_exact_bytes_and_key`
publishes a real reply to an isolated Redpanda topic whose message cap is configured
above the application's 20 MiB cap. Its source is 13.5 MiB raw; the actual
`MessagingJson.Options` UTF-8 payload is greater than 18 MiB and below the 20 MiB
cap by more than 64 KiB. The consumer checks the same topic, conversation key,
exact serialized bytes, frozen reply handle/message id, Unicode raw metadata and
source attachment bytes. `Broker_key_uses_conversation_then_reply_handle_then_empty_string`
proves all three key choices on broker records. Both are Passed in CP-11's TRX.
Existing CP-4 tests separately cover original and converted serialized exact/+1
storage boundaries. New CP-5 method
`ChannelOutboundDeliveryTests.Fallback_annotation_uses_actual_wire_budget_and_overcap_keeps_stamps_null`
uses the real pump with a configured 2,048-byte cap. An original reply whose
actual annotated serialized payload is exactly 2,048 bytes publishes with its
original attachment and correlation stamp; the same reply at 2,049 bytes enters
Failed without producer invocation, PublishedAt, or correlation stamp. The
terminal failure reason is recorded. This does **not** close V-23 or R-13:
the combined original/converted/fallback broker publication matrix, output
priority and omission-warning cases, and terminal attention projection remain
without complete named evidence.

**R-14, validator guard only.** The test-only
`ChannelOutboundEvidenceAccounting.RequiredOrdinaryAssertions` now enumerates
qualified V/R assertion keys. `ValidateOrdinary` rejects broad `V-n`/`R-n` tokens
in both required and claimed rows and requires a named green, non-skipped TRX
method for each requested assertion. The new
`ChannelOutboundEvidenceAccountingTests.Broad_id_tokens_cannot_stand_in_for_assertion_evidence`
is Passed in CP-1's TRX. This closes the broad-token validator defect only.
There is still no complete, independently reviewed actual-run index mapping every
assertion key to its own decisive oracle and native result; R-14 remains open.
The validator's synthetic fixtures do not certify absent behavioral coverage.

## Final checkpoint sweep

The plan's complete closed CP-1 through CP-13 list ran on the committed tip in
`.antiphon/checkpoints/20260929-081324-266a/` (Debian 12, 20m15s). Its
`report.md`, TRX, CP-8 failure detail and host/slot records are the native
evidence. Every row held a granted build slot. The tool ran no unlisted row.
Earlier complete sweeps on committed slices `69b1eecff0bf9f1acbf12d7d6138c79b8b58b00c`
and `48221185b0bd54cbb9ea7a1ae57853bf27ce9852` remain in
`.antiphon/checkpoints/20260929-071742-aec9/` and
`.antiphon/checkpoints/20260929-074830-1b38/`; both had twelve green rows and
the identical CP-8 pair. Slice `b2e6eddaa637ef49582644e71c54c610a058c198`
ran in `.antiphon/checkpoints/20260929-081042-617b/`, where CP-1's build
failed on an ambiguous test-only `MessagingJson` reference; CP-2–CP-8 were
unexecuted because their shared build failed. The reference was qualified in
`44bc041f12374beaf7f3b286465679143324cc02` and the complete list reran.
Before the first sweep, a
separately leased bootstrap build of `tools/Antiphon.Checkpoints` was necessary
because the worktree had no built tool; it succeeded with the pre-existing
`TaskOwnerGuard.cs` CS8602 warning. This bootstrap was the only build outside
the plan's closed list.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3485 | 3485 | 0 | 33 | Green; Linux platform skips; new R-14 guard observed |
| CP-2 source settlement | 371 | 371 | 0 | 0 | Green |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 7 | 7 | 0 | 0 | Green; new fallback boundary observed |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited `PinnedAgentKindTests.T1`/`T2` `codex_desktop_unqualified` |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green |
| CP-11 gateway wire | 125 | 125 | 0 | 0 | Green with `ANTIPHON_BROKER_TESTS=1`; both new broker methods observed |
| CP-12 client | 26 | 26 | 0 | n/a | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

Overall tool exit **1**: twelve green rows and CP-8 red. The two failures match
round 21 exactly and are unrelated to this round's test-only changes. The first
sweep had CP-1 3484/3484 and CP-11 125/125; the final sweep includes the new
validator and pump methods. No CP success is
presented as a passing assertion that its methods did not execute.

## Open gates

No whole ordinary V/R ID was closed in this round. The open set remains
**V-5–V-18, V-23, R-2–R-11, R-13–R-14**. The
[round-17 sub-assertion inventory](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions)
still defines the missing behavior for each, excluding the individual new
V-23/R-13 broker, V-23/R-9 pump-budget and R-14 validator portions above. In particular, the validator
catalogue is an obligation list, not a completed evidence index. V-25 remains
for separately authorized actual-destination receipt; PC-1–PC-30 remain for
method-scoped SourceLanding Mutation. Do not land from this evidence.
