# CARD-0418 round 27: outbound gates and real control callers

This continues the [round-26 evidence](2026-09-29-card-0418-round-26-evidence.md).
The tested code commit is `b2cd2c437b93ca9cde01d1287160419a5d1e0bf3`.
No shared-stack restart, actual destination, land, or SourceLanding Mutation was used.

## V-7 ordinary acceptance

`ChannelOutboundDeliveryTests.Gates_precede_admission_and_a_matched_companion_still_converts`
has three executed arguments against a real selected `EveryAgentReply` profile:
`NO_REPLY` without an explicit attachment, an unmatched operator terminal turn,
and a matched API-error stub. For each, a fresh database read finds zero outbound
intents and zero linked converter tasks; the fake producer received nothing.
The `NO_REPLY` correlation is settled, while operator and API-error correlations
remain owed. A later matched turn in each fixture creates one `Pending` intent,
links its correlation and, after a real pump tick, creates one `Converting`
worker task. The producer still has no source reply while that worker runs.

`Disallowed_plain_text_machine_turn_stays_out_of_admission` seeds a prior
channel row and an actual Sent System injection, then dispatches its plain-text
turn. It neither publishes nor claims the machine row or creates an intent/task.
A subsequent matched channel turn in the same fixture admits one conversion.
The existing named `ChannelMachineTurnTextTests.System_origin_with_attach_still_sends`
and `ChannelFollowUpAttachmentTests.NO_REPLY_plus_marker_sends_the_file_with_empty_text`
retain the explicit-marker exceptions; the new silence case has no marker.

`Real_control_callers_bypass_the_selected_profile` executes four arguments:
`ChatChannelService.SendAsync` proactive, `AwayDigestNotifier.SendDueAsync`,
`IncidentPageNotifier.SweepAsync`, and `ChannelAlertRouter.RouteAsync` followed
by `AlertDigestFlusher.FlushDueAsync`. Each sends one control reply directly
under the same selected profile and creates no intent or converter task.
Proactive/digest/incident sends stamp `LastReplyAt`; the incident also stamps
`HumanNotifiedAt`, and alert routing stamps `RoutedAt` without changing
`LastReplyAt`. The same facade then publishes a Markdown-attached conversion
failure notice as `Control` with exact bytes and zero recursion. A subsequent
matched agent reply creates one converter task in every argument.
The existing named `ChannelReplyDurabilityTests.A_terminal_grok_402_sends_one_notice_settles_and_skips_ttl`
and `A_grok_transport_death_sends_the_error_now_settles_and_skips_ttl`
retain provider-capacity and transport notice behavior. Their notices use
`ChatChannelService.SendAsync`, the control caller exercised above.

The new rows use an isolated PostgreSQL schema, real dispatcher and pump,
and a fake destination. This closes **whole V-7** as ordinary local evidence;
it does not stand in for V-25's actual destination.

## Checkpoint trail

- At `bbea0e9705827f8493a859bb0d11b0f921a537a4`, the first committed slice
  ran CP-5 22/22 and CP-7 320/320 in
  `.antiphon/checkpoints/20260929-133440-4784/`.
- After the machine-turn and Markdown-attached control additions, CP-5 ran
  23/23 at the final code commit in
  `.antiphon/checkpoints/20260929-134154-8531/`.
- The full CP-1–CP-13 run at that same commit is
  `.antiphon/checkpoints/20260929-134504-cd58/`. All rows held a granted host
  slot; the tool ran no unlisted row. Its exit 1 consists of one CP-7 elapsed
  time edge and CP-8's inherited pair. CP-7 then passed 320/320 at the same
  commit in the isolated row rerun
  `.antiphon/checkpoints/20260929-140605-d323/`.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3485 | 3485 | 0 | 33 | Green; Linux platform skips |
| CP-2 source settlement | 372 | 372 | 0 | 0 | Green |
| CP-3 policy/schema | 32 | 32 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 23 | 23 | 0 | 0 | Green; new V-7 rows |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 319 | 1 | 0 | One timing edge; isolated rerun 320/320 |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Inherited T1/T2 `codex_desktop_unqualified` |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green |
| CP-11 gateway wire | 125 | 125 | 0 | 0 | Green with broker opt-in |
| CP-12 client | 26 | 26 | 0 | n/a | Green |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

The CP-7 miss was
`ChannelConsumerIdentityEndpointTests.Returns_effective_overrides_and_only_allowlisted_fields`:
its two-second elapsed assertion measured 2.4046072 seconds in the full sweep.
That same class passed in the earlier CP-7 run and the final-code isolated
rerun. No outbound gate row failed. CP-8's two failures are the inherited
`PinnedAgentKindTests.T1` and `T2` `codex_desktop_unqualified` refusals; this
round did not change or chase them. The separately leased checkpoint-tool
bootstrap build succeeded with the existing `TaskOwnerGuard.cs` CS8602 warning.

## Remaining gates

Closed ordinary IDs are V-1–V-8, V-19–V-22, V-24, R-1 and R-12.
The open ordinary set is **V-9–V-18, V-23, R-2–R-11, R-13–R-14**. Use the
[round-17 assertion inventory](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions),
as amended by rounds 18–26 and this V-7 closure, for each missing matrix.
V-25 remains the later authorized actual-destination gate. PC-1–PC-30 remain
pending method-scoped SourceLanding Mutation; no ordinary or nightly green
discharges them. The next Code slice should start with V-9's two-dispatcher,
two-pump identity and lease-takeover matrix, then continue the open V/R IDs.
