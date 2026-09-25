# CARD-0584: correlate channel replies across Grok newline removal

Date: 2026-09-25. Stage: Plan. Next: Code. Execution: server2 Linux, Codex Astra/Sol.

## Outcome and evidence

The defect still exists on fetched `origin/master` at
`65c29b742264cdf2b4a79ce2f6df335fbc5435b8` (the task checkout was identical).
Do not close CARD-0584 as already fixed. Delivery confirmation accepts Grok's joined
prompt, but channel reply routing and its TTL classifier reject the same prompt.
The 120-character reply probe also accepts distinct prompts with a common head.

The live card is `5a424a2c-c41a-4924-a459-b8358671f07c` on board
`8988ca03-7414-47ad-b0b6-51556c701703`. Its description records a completed Grok
turn followed by a lost-reply notice because `deploy\nLatest` became `deployLatest`.
History was initially empty; a subsequent read returned revision 1, the move
recording this Plan dispatch, by `card-transitions`. There is no earlier repair
or investigation report in that history. The description supplies no original
incident session ID; this plan does not claim to have retrieved that transcript.

An isolated PowerShell `Add-Type` probe compiled the **unchanged, extracted**
`ChannelReplyDispatcher.PromptsMatch` and `Normalize` methods alongside the current
`PromptSubmissionMatch.cs`. Eight diagnostic assertions passed, zero failed:

| Probe | Observed current result |
|---|---|
| Single-line channel body against itself | match |
| Enveloped `please deploy\nthe latest build and verify` against newline-elided text | channel mismatch |
| Same pair through queue identity | match |
| Same pair through queue completeness | complete |
| Two bodies with the same first 120 characters but `deploy blue` / `deploy green` tails | incorrect channel match |
| Same different-tail pair through queue completeness | rejected |
| `instruction: ab cd` against `instruction: a bcd` | queue whitespace-free comparison accepts |
| Collapsing whitespace to one space on the newline-elided pair | still unequal |

This is a compiled method probe, not a dispatcher integration test or a fresh
provider canary. No application build, TUnit run, live model call, or production
message was needed for Plan. Code's red tests below establish the application
outcomes. No production or test code changes belong to this Plan commit.

## Ground truth and caller inventory

Source references below are relative to the audited master commit.

| Component | Actual contract and exposure |
|---|---|
| `src/Antiphon.SessionRunner/GrokTranscriptNormalizer.cs:164,552` | `session/update` / `user_message_chunk` reads `params.update.content.text` verbatim into `UserPrompt`; event identity comes from `params._meta.eventId`. The normalizer does not remove newlines. The composer/native record has already lost them. A user chunk does not expose the later turn's `prompt_id` as a queue delivery identity. |
| `tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs:130-167` | Existing measured grok 1.0.5 evidence: 4,450 characters sent, 4,389 recorded, exactly 61 LFs removed without separators. `GrokTranscriptTailerTests` contains provenance-labelled real ACP row shapes. These are historical captures, not a claim about every later CLI version. |
| `ChannelBridgeService.cs:198-226` and `ChannelPromptFormat.cs` | The bridge preserves operator line breaks inside its provider/chat/author/time envelope and persists a Channel-origin WhenIdle row before input. Envelope timestamps have minute precision and are not unique delivery identifiers. |
| `ChannelReplyDispatcher.cs:318-319,1563-1574` | `Normalize` changes line endings to LF and trims edges. `PromptsMatch` performs ordinal containment of the first 120 characters. No internal whitespace normalization, completeness check, or attempt floor is applied by the live reply match. |
| `ChannelReplyDispatcher.cs:706-744` | `ClassifyTtlLossAsync` repeats that match, with a timestamp floor of `SentAt ?? CreatedAt`. Late-confirm overwrites `SentAt` after the prompt happened, so using it as the receipt floor is independently unsafe. Completion currently checks for any later TurnEnd; the new classifier must stay inside the owning turn. |
| `ChannelReplyDispatcher.cs:1162-1174` | Machine follow-up gate 2 compares **all historical Channel bodies** using the same probe to decide the main channel path owns a turn. It must use the same correlation rule as main dispatch, including settled rows for duplicate suppression. |
| `ChannelReplyDispatcher.cs:1319-1323` | Fourth and final direct call site: `MatchesHeaderLine` uses the probe on a machine note's first line. CARD-0397 additionally matches task/check short IDs. This is a separate identity contract: keep it separate from the new channel matcher. Do not quietly replace all four calls with one permissive function. |
| `SessionMessageQueueService.cs:2180-2283,2812-2822,3483-3521,3637-3674` | Inline, grace and late confirmation use `PromptSubmissionMatch`, not `PromptsMatch`. Candidates are scoped to session and a pre-typing sequence floor, or original timestamp floor when unobservable. Ordinary candidates include UserPrompt, drained QueuedUserPrompt and the specifically supported question ToolResult. Late-confirm requires a text-identifiable body and complete content before marking Sent without terminal writes. |
| `src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs` | Normalization strips ANSI and collapses whitespace/control runs. Identity compares a 200-character head, minimum 12; completeness compares the entire body. Both have a whitespace-free fallback. Bodies below the threshold take a weak, vacuously true arm. That arm is unsuitable for reply attribution. `SessionInputLog` also shares needle building/normalization for transcript binding; do not change it as a side effect. |
| Check interpretation | Legacy `AgentTaskCheckService.InterpretAsync` delegates through `SpecialistTaskRunner`; settlement's `AgentTaskReplyService.ExtractMarkedTurnAsync` uses dispatch-bounded `TranscriptPromptSpan` plus `[antiphon-task:...]`, then the final response boundary. It does not call `PromptsMatch`. Qualified specialist attempts use `SpecialistAttemptEvidence.Evaluate` and `SpecialistInputPolicy.CompletePromptEquals`: full ordinal equality after LF normalization and trim, plus sequence/attempt/report checks. The capability admits Claude/Codex, not Grok. Do not loosen that equality. |
| Check note delivery and continuation | Notes travel through the queue's whole-prompt confirmation. `CheckCompactionContinuationService` uses `IsCompleteIn` for note/caller receipts and human-origin classification. Channel-facing replies to `[check ...]` use CARD-0397's machine route. These already tolerate the measured Grok shape where applicable; preserve the separate gates. |

Other shared-helper consumers audited: `AgentSessionService` boot/local-command
confirmation, `CodexSubmitConfirmation`, `RemoteSpillCourier`, `LandNoteReceipt`,
`ExpectationNudgeDeliveryService`, and `GrokRulesRefreshService`. They use the
200-character identity and/or full-body contract, not the channel's 120-character
probe. This card changes neither their weak-body policy nor their receipt rules.

Claude's `TranscriptNormalizer.FromUser` preserves string content and individual
text blocks; `QueuedUserPrompt` remains a legal drained-command opener. Codex's
`CodexTranscriptNormalizer.FromFlatUser` preserves `payload.message`; its
`UserMessage` item path concatenates text blocks without inserting separators,
preserving whitespace inside each block. Codex dialect latching avoids duplicate
prompts. Neither normalizer strips embedded LFs. The channel defect is nevertheless
provider-neutral if an upstream transcript changes whitespace; the same-head false
positive affects all providers. Existing Claude delivery evidence preserves pasted
newlines; no equivalent Codex newline-loss measurement was found. Do not claim a
new live Claude/Codex/Grok parity qualification from replay tests.

## Decisions

### D1. Identity before lossy comparison

Use a server-generated, complete GUID marker in every newly persisted Channel
body, for example `[antiphon-channel:<queue-id:N>] ` followed by the existing
channel envelope and content. Own the grammar in a small Application helper,
`ChannelPromptCorrelation`; keep `ChannelPromptFormat` as the envelope authority.
The marker is transport correlation, never provider/chat addressing or an
authorization credential. Resolve outbound addresses only from the queue row.

Generate it from the allocated queue ID before the row can be typed. Always add
the server-owned outer marker; user prose containing a marker must not choose or
suppress it. Preserve it byte for byte across retries, reloads and SendNow of an
existing row. Use full GUIDs, never task-style eight-character abbreviations.
No new database column or migration is needed: the authoritative wire body is
already persisted in `SessionQueuedMessage.Body`.

The receipt identity is the stored marker **and** session **and** delivery-attempt
floor (`LastDeliveryBaselineSequence`, otherwise `LastDeliveryStartedAt` with the
existing configured tolerance). `LastDeliveryGeneration` remains the accepted
generation of that attempt. A marker alone, screen redraw or assistant text is
not a receipt. A body with no typed attempt cannot consume a reply.

### D2. Whole-body comparison, with a narrow compatibility path

The new channel matcher receives the row and candidate prompt metadata, not just
two strings. It requires a real prompt opener, matching session, nonempty expected
body, an eligible attempt floor, and complete content. Compare ordinally after
the existing `PromptSubmissionMatch.Normalize`; permit its whitespace-free
full-body arm only for a body with a server-owned marker. Require the literal
marker in the prompt independently; do not normalize, truncate or fuzz its GUID.
Use `RequiresTextMatch` before any helper with a weak arm. No 120/200-character
head alone may settle a channel correlation.

Retain framing containment for actual batches and provider-added wrappers. Reject
a clipped head, clipped middle, wrong marker, changed punctuation/case/content,
and a similar prefix followed by a different tail. Two whitespace-equivalent
messages have different markers and cannot claim each other's replies. Multiple
members of one real batch may all match and settle with its one reply.

For an observable attempt, require `prompt.Sequence > LastDeliveryBaselineSequence`.
For an unobservable attempt, require an original non-null transcript Timestamp at
or after `LastDeliveryStartedAt - configured tolerance`; ingestion `CreatedAt`
cannot substitute for native time. If native time is available, reject evidence
predating the recorded attempt generation. Do not require equality with the
session's *current* generation when routing an already-completed owed turn: a
standing resume must not erase valid prior-generation evidence. Do not invent a
generation for a legacy null field.

Legacy already-attempted rows cannot retroactively acquire a marker. Permit full
ordinal containment after LF normalization/trim with their original attempt floor
(or original SentAt for an old row lacking attempt metadata), preserving ordinary
newline-preserving traffic. Do not enable whitespace deletion for those rows.
Reject ambiguous legacy correlations unless membership in the same delivered batch
is evidenced; raw short legacy bodies require whole-prompt equality and must not
match inside machine headers. A never-attempted legacy Pending row can be wrapped
before its first input. Never rewrite an attempted row just to make it match.

This compatibility policy deliberately cannot auto-recover an old **unmarked**
Grok newline-elided answer with strong identity. Leave it owed for the existing
loss handling; log a bounded correlation reason rather than guessing. New and
never-attempted traffic receives the fix. Historical recovery is a separate,
explicitly selected action, not a nearest-prompt fallback.

No text marker can defeat an operator deliberately replaying a complete marked
prompt inside its eligible session/window. The guarantee here is against distinct
queued prompts, whitespace collisions, clipping and old-attempt evidence, not an
authentication claim about terminal actors.

### D3. Preserve identity through batching and spills

Inline batches retain each constituent's marked stored body, so full containment
still settles every delivered member. For an oversized batch, retain the current
one-file/one-pointer behavior: put the head row's generated marker on the shared
wire pointer and persist that **same complete pointer** on every member before
typing, with their common attempt metadata. All those rows represent the same
delivery. Do not require each secondary spilled row's own ID on that shared
pointer, and do not turn a marker found in file contents into transcript evidence.

For a single spill retain its row marker. Extract provider/chat orientation from
the original channel envelope after removing only the known outer marker;
`TypedBodySpill.TryReadChannelEnvelope` currently takes the first bracketed group
and would otherwise mistake the new marker for the chat envelope. Keep the marker
on the final pointer, including remote spills, re-staging and retry reconstruction.
`BindStagedSpill` lookup/ack must continue to see the exact staged wire it owns;
wrap after binding or adjust both sides together, never strand staged bytes.

Account for marker bytes before ceiling decisions and verify the resulting
pointer fits the selected transport. Preserve original spill-file content,
per-message file custody, retry identity and LF/bracketed-paste/separate-Enter
encoding. A spill confirms that the pointer was delivered, not that the model
read the file. Markers must not be emitted in human reply text or notices.

The bridge's durable WhenIdle path and existing-row SendNow are required. Bare
`EnqueueAsync(..., Now)` creates no owed correlation today; it is not made into
a new channel routing path. Any durable Channel row creation path, including
`SendNowTrackedAsync`, must apply the same wrapping rule.

### D4. One channel attribution rule, distinct machine and specialist rules

Use the new row-aware rule for main dispatch, TTL classification, and machine
follow-up gate 2. Gate 2 must use relevant Channel rows and attempt evidence, not
unbounded historical body substrings. Recognize a channel marker before applying
injection-shaped heuristics: channel prose may quote `[task ...]` or `[check ...]`.

Keep the machine task/check ID and header route separate, giving its private
header-probe helper an explicit name. No change to Check capability equality,
task settlement markers, provider normalizers, raw transcript text, shared C4
normalization, queue weak-body semantics, kill rules, or delivery deadlines.

TTL uses the same matching prompt and the same `TranscriptTurnWindow` boundaries
as routing. Report TurnUnmatched only for a matching completed turn with usable
assistant text; no own completed boundary/text means TurnIncomplete; no matching
receipt means StaleTtl. A later unrelated turn cannot supply the missing boundary.
Late-confirm's later SentAt must not hide the earlier matching UserPrompt.

Keep claim-before-produce, rollback on producer failure, NO_REPLY, API-error
withholding, attachments, and restart duplicate suppression intact. Log IDs,
sequence/floor and bounded mismatch categories; never log whole prompt bodies.

### Rejected alternatives

- Collapse whitespace to spaces only: does not equate a removed LF with zero characters.
- Delete whitespace globally or reuse `IsConfirmedBy` alone: conflates distinct prompts,
  permits head-only matches and inherits the short-body weak arm.
- Normalize stored transcripts or reconstruct missing spaces in Grok's parser: destroys
  source evidence and cannot infer the user's original boundaries.
- Use chat envelope/time, Grok eventId or ApiCallId as delivery ID: the first is not unique;
  the latter two are not known before typing and do not identify the queue request.
- Choose the nearest UserPrompt/TurnEnd after SentAt: an operator or another queued turn
  can own it; this would publish the wrong answer to a chat.
- Apply CARD-0397's task ID/header workaround to human messages: human channel messages
  have no source task, and header-only matching does not prove complete delivery.
- Broaden specialist exact-input matching: changes a capability qualification contract
  unrelated to ordinary channel routing.
- Add a new delivery-receipt table or provider version switch: unnecessary for this
  bounded fix; existing persisted body and attempt fields can carry the identity.

## Rounds and implementation slices

Round 1, **S1: four red application tests only**. Add
`tests/Antiphon.Tests/Application/ChannelPromptCorrelationTests.cs`, using
`BridgeQueueHarness`, a fake messaging producer, real database rows and production
normalizers. Drive production enqueue/runtime/dispatcher APIs. The tests must
compile without any new production helper. Commit S1, then run CP-1.

Round 2, **S2: wire identity and complete comparison**. Add the Application helper;
update durable Channel row creation, spill/pointer handling and attempt-aware
matching. Relevant files are `SessionMessageQueueService.cs`,
`ChannelPromptFormat.cs`, `TypedBodySpill.cs`, and the new helper. Preserve shared
queue confirmation behavior; only Channel wire bodies acquire correlation text.

**S3: routing and TTL**. Replace the three channel-attribution uses in
`ChannelReplyDispatcher.cs`, split the machine-header helper, apply owning-turn
bounds to classification and preserve existing publication semantics.

**S4: finish regression fixtures and documentation**. Complete the 12-test
integration roster and six unit methods below. Adjust existing tests to model
real attempt metadata/typed marked bodies, not to manufacture permissive fallback
behavior. Update `docs/telegram.md` and `docs/session-runtime-invariants.md` with
the correlation/legacy rules. No generated `docs/cards/` edits. Commit all of
S2-S4 before CP-2; CP-2 through CP-5 share that exact source/output.

## Verification design

Use server2's nested Docker/Testcontainers and the existing production-runner
guard. No tests launch a real provider or touch the production broker/runner.
These are parser replay plus database/runtime/publication tests: a Windows ConPTY
run is not needed for changes entirely above the unchanged input encoding layer.
All stage work remains on server2 under the caller's standing authority.

Real-shape fixtures: start from the provenance-labelled ACP rows in
`GrokTranscriptTailerTests`, Codex's `Fixtures/codex-tui-turn.jsonl` and
`codex-exec-turn.jsonl`, and Claude `TranscriptNormalizer` string/text-block and
queued-command fixtures. Reduce to user/assistant/completion rows; replace only
IDs, clocks and content with test-owned values, documenting each substitution.
Do not copy unrelated encrypted/provider metadata. Store new reduced fixtures in
`tests/Antiphon.Tests/Agents/Fixtures/card0584/` (already copied by the test project).
Run the actual normalizers, ingest their output through the runtime where the
assertion is publication. Never build the recorded prompt by calling the matcher
or its new normalization helper. Literal expected strings must include `deploythe`.

### Required red and regression roster

The new integration class has 12 non-parameterized tests (one execution each).
Use the four `C584_Red_*` names exactly for CP-1; add the other eight in S4.

| ID / method suffix | Required observable assertion | Baseline / positive control |
|---|---|---|
| V-1 `C584_Red_JoinedGrokReply` | Ingress multiline Channel body; ACP joined UserPrompt + AssistantText + TurnEnd; exactly one reply to its conversation, row settled, next sweep sends no loss notice. | Old dispatcher publishes zero. |
| V-2 `C584_Red_JoinedGrokTtl` | Suppress initial dispatch, age the owed row, then classify the recorded matching completed joined turn as TurnUnmatched, with prompt sequence/character evidence. | Old classifier reports StaleTtl. |
| V-3 `C584_Red_CommonHeadDifferentTail` | Two same-session/conversation candidates share >120 characters but differ in tail; one recorded turn settles only its actual row. | Old probe settles both. |
| V-4 `C584_Red_PreAttemptPrompt` | Same text exists only at/before the latest attempt's floor; its old answer does not settle the current correlation. | Old main matcher accepts it. |
| V-5 `C584_WhitespaceCollision` | Different queue IDs with bodies `instruction: ab cd` / `instruction: a bcd`; a recorded flattened body containing only one marker settles one row; wrong/missing marker publishes nothing. | Removing marker requirement must fail this test. |
| V-6 `C584_AttemptFloorsAndLateConfirm` | Parameter-free scenario sequence: original timestamp before attempt is rejected, null timestamp with null baseline is rejected, valid late receipt marks Sent with zero input writes and routes once even though SentAt now postdates prompt time. | Using SentAt instead of attempt time or removing floors must fail. |
| V-7 `C584_InlineBatch` | Two actually typed marked members settle once with one reply; unrelated third row stays owed; quoted task/check text inside channel input still uses main routing. | Dropping a member identity or injection-shape gate must fail. |
| V-8 `C584_SpilledBatchAndRetry` | Oversized two-row Channel run writes one owned file/shared marked pointer; reload/retry preserves pointer and ownership; normalized ACP pointer routes once for both members, with the real chat envelope retained. | Losing the marker/envelope on spill or regenerating it on retry must fail. |
| V-9 `C584_RestartAndProducerFailure` | Fresh dispatcher uses stored marker/floor; producer exception leaves owed rows; retry publishes once; another process does not republish after settlement. | In-memory-only identity or lost rollback must fail. |
| V-10 `C584_LegacyCompatibility` | Old full newline-preserving envelope still routes with valid floor; never-attempted legacy Pending is wrapped; attempted unmarked joined/ambiguous traffic remains owed. | Global whitespace fallback or rewriting attempted text must fail. |
| V-11 `C584_TtlOwnTurnOnly` | Matching prompt has no own completed usable response before next opener; unrelated later TurnEnd/text cannot make it TurnUnmatched. | Current any-later-TurnEnd check is the negative control. |
| V-12 `C584_ClippedAndMachineTurns` | Correct marker/head with missing middle/tail publishes nothing; actual marked channel receipt excludes machine follow-up; unrelated historical Channel text does not suppress a genuine machine note. | Head-only compare/historical gate must fail. |

Add six `[Category("Unit")]` non-parameterized methods in
`ChannelPromptCorrelationUnitTests`: marker generation/parsing and spoofed input;
whole-body positive matrix (LF, CRLF, CR, tabs/spaces, Unicode whitespace, no-LF
join); content/marker/empty/short/200-character-head negative matrix; attempt
eligibility matrix; Grok ACP literal-text preservation; Claude string/block and
Codex flat/item literal-text preservation. Looped cases count as six executions,
not as the number of internal assertions. Parser controls already pass master;
their Mutation-stage negative control is removal of the relevant parsing/identity
branch, which must change the asserted production output. Do not use self-comparisons.

Regression groups: R-1 is the ordinary Unit lane, including existing
`ChannelMachineTurnMatchTests`, `TypedBodySpillTests`, `GrokDeliveryShapeTests`,
`SpecialistAttemptEvidenceTests` and the new unit class. R-2 is the existing
ChannelBridge/ChannelReplyDurability/ChannelFollowUpAttachment/queue-spill classes
(85 source test methods before argument expansion). R-3 is the eight exact queue
methods in CP-4, guarding confirmation/late-confirm/queued prompt/attempt metadata.
R-4 is `AgentTaskCheckInterpreterTests` and `CheckNoteDeliveryHandoffTests` (47
source methods), preserving interpretation and complete caller-note delivery.

Run rows sequentially via `scripts/run-checkpoint.ps1`, fresh result directories
under `.antiphon/c584-checkpoints`, and Linux's default `UseAppHost=false`.
Wrap each invocation with `timeout 1800s`; poll the running command within the
turn. Pass each table Filter literally, `-MinExecuted` from Min, and `-Expect`
for every selected class (CP-4: every listed method). For output reuse add
`-NoBuild`. Do not invoke `dotnet test`, use list-tests as evidence, or broaden
to an Application namespace. In raw Markdown the table escapes OR separators as
`\|`; pass a literal `|` inside the quoted shell filter. Run all git commands with a timeout.

CP-1 is an explicit intentional-red exception: require four executed tests and
four **specified assertion failures**, with zero fixture/build errors or skips;
`run-checkpoint.ps1` exit 1 is expected for that row only. The later full new-class
run must make those same four green. No extra mutation/build loop is required
inside Code: record the production-line negative controls for the next stage.
Any unlisted execution or necessary rerun needs a stated reason and count.
Report each row with the owner's `CHECKPOINT CP-n commit=... build=... filter=...
executed=... passed=... failed=... skipped=... trx=...` line.

### Cost

Ordinary Code checkpoint floor: 39 minutes (8 + 8 + 10 + 5 + 8), plus roughly
60-90 minutes authoring. Expected dispatch budget: about 100-130 minutes.
These are estimates, not timeout overrides or substitutes for test counts.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c584-red/` | baseline-red | `/*/*/ChannelPromptCorrelationTests/C584_Red_*` | V-1-V-4 negative controls | exactly 4 executed, 4 named assertion failures, 0 skipped; expected red | 4 | 8 |
| CP-2 | S2-S4 | `tests/Antiphon.Tests -> bin-c584-green/` | unit | `/*/*/*/*[Category=Unit]` | R-1, six new unit methods | all selected, 0 failed/skipped; new class and named R-1 classes execute | 6 | 8 |
| CP-3 | S2-S4 | CP-2 | channel | `/*/*/(ChannelPromptCorrelationTests*)\|(ChannelBridgeTests*)\|(ChannelReplyDurabilityTests*)\|(ChannelFollowUpAttachmentTests*)\|(SessionMessageQueueSpillTests*)/*` | V-1-V-12, R-2 | all five classes, all 12 new methods, >=97 executed, 0 failed/skipped | 97 | 10 |
| CP-4 | S2-S4 | CP-2 | queue-receipts | `/*/*/SessionMessageQueueDeliveryVerificationTests/(Grok_matching_UserPrompt_still_wins_as_transcript_proof*)\|(A_record_carrying_a_stale_body_is_rejected_and_the_enter_is_re_pressed*)\|(Late_confirm_marks_the_message_sent_with_zero_writes_to_the_terminal*)\|(Queued_user_prompt_confirms_delivery_without_a_second_enter*)\|(Queued_user_prompt_late_confirm_never_types_the_body_twice*)\|(Attempt_metadata_survives_the_revert_a_failed_delivery_does*)\|(Card0164_null_baseline_attempt_is_late_confirmed_without_retype*)\|(Card0164_unobservable_old_timestamp_row_does_not_confirm*)` | R-3 | all 8 named methods, 0 failed/skipped | 8 | 5 |
| CP-5 | S2-S4 | CP-2 | check-interpretation | `/*/*/(AgentTaskCheckInterpreterTests*)\|(CheckNoteDeliveryHandoffTests*)/*` | R-4 | both classes, >=47 executed, 0 failed/skipped | 47 | 8 |
