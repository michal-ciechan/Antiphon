# CARD-1475 DeliveryUnverified copy after a bounded miss — plan

Card: CARD-1475 (Antiphon board). Evidence: [docs/investigations/2026-10-22-report-contract-and-first-prompt-noise.md](../../investigations/2026-10-22-report-contract-and-first-prompt-noise.md), Debug task fc28850a.

## Step 1 findings (read-only)

**Where the copy lives.**

| Surface | Location | Old text |
|---|---|---|
| Incident (server) | `SessionMessageQueueService.RecordDeliveryUnverifiedAsync` | "A message was typed but could not be confirmed: this session has no transcript bound (or has not written one yet). The terminal redrew, so the text probably landed — nothing can verify it." |
| Degraded receipt (server) | `SessionMessageQueueService.ToReceipt`, `Reason` when `ConfirmedBy == Screen` | "this session has no transcript bound (or has not written one yet)" |
| Composer label (client) | `client/src/features/agents/SmartComposer.tsx` `DeliveryReceipt`, screen/degraded arm | "Typed · unverified — no transcript bound (see incidents)" |

`SessionTranscriptPanel.tsx` ("This session has no transcript bound…") and `AgentRail.tsx` ("Terminal live — no transcript bound") render the transcript *binding* state of a live session, not a delivery verdict; they stay as they are.

**Condition.** `WaitForTranscriptConfirmAsync` with an unobservable baseline (the session had zero `TranscriptEntries` when the attempt began: unbound, or the provider has not written its first row yet). The loop already polls `TryFindUnobservableConfirmingRecordAsync` (attempt-time timestamp floor, whole-body completeness) and pulls the runner transcript every `pullEvery`. Only when the deadline (`TranscriptConfirmTimeoutSeconds`) passes with no matching complete record, and the screen showed a positive submit signal, does it return `Confirmed(Screen)` and call `RecordDeliveryUnverifiedAsync`.

**True state after a bounded miss.** No matching complete UserPrompt was available within the verification window *at that attempt*. Delivery is unverified, not disproved, and not unverifiable: the evidence named in the card shows complete stored UserPrompts arriving ~30 s before the warnings were raised for prompts that the provider wrote after the bind. A later matching UserPrompt in the transcript proves the delivery. A redraw is a submit signal, not proof.

**Pins of the old text.** `SessionMessageQueueDeliveryVerificationTests.Mode_Now_screen_only_fallback_returns_a_degraded_receipt_and_records_DeliveryUnverified_once_per_window` (`Reason.ShouldContain("no transcript bound")`); `SmartComposer.test.tsx` "renders the unverified receipt for a screen confirmation" (label text and a fixture reason). Owner doc: `docs/session-runtime-invariants.md` Preserved Gotcha #50 ("nothing to confirm against"; "degrades to the old screen-only verdict… when the session has zero TranscriptEntries").

## Change (one slice)

Wording only. No change to the timeout, verdict, `Confirmed(Screen)` return, severity (Critical when channel-bound, Warning otherwise), 10-minute dedup, no-kill/no-retype, completeness check or timestamp floors. Historical incidents are not rewritten.

- Incident text names the bounded observation and the attempt context already in scope (Enter count, verification window in seconds, zero-row baseline): no matching complete UserPrompt within the window; unverified as of this attempt; a later matching UserPrompt proves it; a redraw alone does not prove the text was submitted.
- Receipt reason: no matching UserPrompt within the verification window; unverified until a later matching UserPrompt proves it.
- Client label: "Typed · unverified — no transcript confirmation yet (see incidents)".
- Gotcha #50: separate the boot `VerifiedPromptSubmitter` initial-submit exception from durable transcript proof; say the queued/Mode:Now first-brief path already polls and pulls for a matching record, and the screen fallback is a bounded-window outcome.

## Verification

- V-1 `DeliveryUnverifiedDiagnosticTests.Missing_receipt_copy_is_temporal_and_allows_later_confirmation` — red witness: zero baseline, no UserPrompt written; real receipt and incident must carry the bounded copy and must not say "nothing can verify", "probably landed" or "no transcript bound". Assertion-red at the old copy.
- V-2 `Complete_first_prompt_uses_transcript_and_emits_no_warning` — guard: a fresh, stamped, complete matching UserPrompt confirms by transcript; no DeliveryUnverified.
- V-3 `Still_unproved_channel_delivery_keeps_critical_dedup_and_no_retype` — guard: two channel-bound screen-only attempts inside the window → one Critical incident, each body typed once, no kill.
- R-1 the existing `Mode_Now_*` receipt/incident methods of `SessionMessageQueueDeliveryVerificationTests` (updated pin) stay green.
- R-2 `SmartComposer.test.tsx` (updated label pin) stays green.

PCs pending for SourceLanding Mutation: PC-1 restore the old incident text → V-1 red; PC-2 restore the old receipt reason → V-1 red; PC-3 drop the channel-bound Critical branch → V-3 red; PC-4 shorten dedup to zero → V-3 red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1475-red/` | c1475-copy-red | `/*/Antiphon.Tests.Application/DeliveryUnverifiedDiagnosticTests/*` | V-1 witness, V-2, V-3 | 3 executed, 1 failed (`Missing_receipt_copy_is_temporal_and_allows_later_confirmation`, assertion), 2 passed, 0 skipped | 3 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1475-green/` | c1475-copy-green | `/*/Antiphon.Tests.Application/DeliveryUnverifiedDiagnosticTests/*` | V-1, V-2, V-3 | 3 passed, 0 failed/skipped | 3 | 8 |
| CP-3 | S2 | CP-2 | c1475-modenow-pins | `/*/Antiphon.Tests.Application/SessionMessageQueueDeliveryVerificationTests/Mode_Now_*` | R-1 | 6 passed, 0 failed/skipped | 6 | 5 |
| CP-4 | S2 | n/a | c1475-composer | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/SmartComposer.test.tsx` | R-2 | `Tests 9 passed`, 0 failed; `CLIENT TESTS EXIT CODE: 0` | n/a | 5 |

Closed list, run through the checkpoint tool on server2 (one run per After group), wait for a non-75 exit. No whole-suite run; alternate outputs removed.
