# CARD-1799 slice 1 — Code task 27eba224

Branch `feat/card-task-27eba224`, worktree `/work/worktrees/task-27eba224`, base master `cad7766950ee96edc13e6ebf805ec5b4bbebbd6f`.

- Red SHA: `fd585fe29ca0a5cad0d3c5cf2665f341f3b672be` (witnesses + the inert optional contract field `Attachment.InlineLimitBytes`, so witnesses fail on assertions, not on compile).
- Fix SHA = tested SHA: `d1185e6acc58f566f307bc0c1f123b014719e8e0`.

## What changed

- `src/Antiphon.Messaging`: optional `Attachment.InlineLimitBytes` (additive wire field; schema `docs/messaging/contract/v1/channel-message.schema.json` + CONTRACT.md; PublicAPI.Unshipped); `AttachmentSize.Format` (binary units, one decimal, invariant).
- `TelegramChannelAdapter`: a file over `MaxInlineAttachmentBytes` (declared size: no getFile; getFile `file_size`; downloaded length) is marked with the cap; one plain-text best-effort reply per message, `reply_to_message_id` + forum `message_thread_id`, no file name/caption; edits dedupe by chat:message_id (bounded 512). Cap 0 = no marker, no notice. Cap unchanged (14 MiB).
- `ChannelBridgeService.RenderMessageBody`: `[file attached — "X" (17.6 MiB) is over the 14 MiB limit and was not delivered]`; name neutralised (`[`→`(`, `]`→`)`, `"`→`'`, control→space). Failed downloads keep the old note unchanged.
- `TelegramSettings` doc comment; `docs/telegram.md` new "Inbound file size cap (CARD-1799)" section + settings row (cap, base64, 20 MiB Kafka relationship).
- Test infra: `FakeTelegramServer.GetFileCalls`.

## Runs (all on server2 runner mirror, build-slot leased, waited=0s each)

| Run | SHA | Result |
|---|---|---|
| Messaging.Tests `TelegramChannelAdapterTests/*` | fd585fe2 (red) | 24 total, 6 failed (all new witnesses except guard below), 18 passed |
| Antiphon.Tests `ChannelBridgeTests/(An_over_limit_*)` | fd585fe2 (red) | 2/2 failed on the note assertion |
| Messaging.Tests TelegramChannelAdapterTests+TelegramResilienceTests+ContractSchemaTests+WireContractTests | d1185e6a | 44/44 green (24+8+2+10 by TRX class) |
| Antiphon.Tests ChannelBridgeTests+ChannelContractsTests | d1185e6a | 57/57 green (43+14) |
| Guards (one run): TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests*, MechanicalPcContract*, TaskPlatformGuidanceTests*, C1729LayeringGuardTests* | d1185e6a | 77/77 green (41+1+5+2+3+11+5+9) |

`Inlined_failed_and_disabled_downloads_send_no_notice` is a no-false-positive guard: green at red SHA by design.
No checkpoint plan exists for this card (no `### Checkpoints`), so no checkpoint-tool run/receipt; runs were direct build-slot runs as the brief listed.

## Mutants (one leased build+run each, source restored with git checkout)

| Id | Mutation | Killed by |
|---|---|---|
| M1 | declared over-cap path drops `InlineLimitBytes` | 5 gateway witnesses |
| M2 | notice call removed | 5 (reply, topic, getFile-resolved, edited, failed-notice) |
| M3 | dedupe removed | Edited_over_cap_message_is_answered_only_once |
| M4 | `message_thread_id` dropped | Over_cap_notice_in_a_forum_topic_stays_in_that_topic |
| M5 | getFile-resolved over-cap returns null | Over_cap_size_found_only_by_getFile_is_marked_and_answered |
| M8 | declared-size gate disabled (getFile attempted) | 5 incl. ..._never_fetched (GetFileCalls 0) |
| M6 | bridge over-limit branch disabled | both bridge witnesses |
| M7 | bridge `[` not neutralised | An_over_limit_note_neutralises_the_channel_file_name |

Not pinned (pending Mutation): downloaded-bytes-over-cap `OverLimit(attachment, bytes.Length)` branch; bounded FIFO eviction (512); `(true, null)` name-without-size subject; non-numeric chat/message id fallbacks; "one attempt" assertion cannot distinguish a retry on a non-transient 400.

## Gates

- check-evidence-diff base..HEAD: commits=2 entries=0 violations=0.
- `git merge-tree --write-tree origin/master HEAD`: clean (origin/master = base).
- tests/test-execution-policy.json unchanged: no policyHash/PinnedInputDigest refresh.
- No migration. No real Telegram traffic (FakeTelegramServer only).

## Owed / not run

- Whole Unit lane not run (brief scoped to touched classes + guards); full Antiphon.Messaging.Tests assembly not run (only the four touched classes).
- Deploy of am-service to server2 is a separate operator step after landing.
- Slice 2 (out-of-band transfer) is a separate card.
