# CARD-0562: the rules-ack hex tokens are matched case-insensitively

Plan task `6ecb5238`, 2026-09-18, inspected checkout `26633049` (`feat/card-task-6ecb5238`; line
numbers below are at that SHA). There is no investigation document: the card carries the root cause
and the transcript evidence (failed task `23c9485a`, session `4ab91070-3f9c-4409-b7b7-cef330430d0d`,
transcript sequences 1 and 7), and this plan verified it against the code. Read-only against code;
no fix was built. The verification design is included at the end because the fix is two comparers
and a handful of test variants, so Build can execute it without a separate TestDesign stage.

Owners: [runtime invariants](../../session-runtime-invariants.md) (§ Grok rules refresh state,
CARD-0395), [testing](../../testing-and-build.md), [conventions](../../project-context.md).
Protocol origin: [CARD-0395 plan](2026-09-06-card-0395-grok-rules-file-plan.md) T-4 (ack grammar).

## Disposition in three lines

1. **Two comparers change, nothing else in the protocol.** `Judge` in `GrokRulesRefreshService`
   matches the ack line with `StringComparer.Ordinal` and the failure prefix with
   `StringComparison.Ordinal`; both become `OrdinalIgnoreCase` (D-1). The three receipt-path hash
   comparisons stay ordinal because both of their sides come from the same lowercase-producing
   function and no model ever renders them (D-2).
2. **The regression is pinned where the incident happened.** New `[Arguments]` variants on the
   existing `Only_current_refresh_assistant_ack_with_confirmed_prompt_releases_barrier` feed an ack
   whose sha256, generation or id differs from the expected text only by letter case and assert
   `Ready`; a sibling test on the failure line asserts an uppercased `ANTIPHON_RULES_FAILED` line
   is read as its stated reason rather than decaying to `missing_ack` (S1, V-1..V-6).
3. **One sentence of documentation** in the CARD-0395 runtime-invariants section says hex tokens
   in the ack and failure lines are case-insensitive (S2). No settings, runner, client or prompt
   text changes.

## Ground truth

Verified on `26633049` on 2026-09-18.

| Card assumption | Observed | Consequence |
|---|---|---|
| The sha256 token is compared case-sensitively "around line 359". | There is no token-level parse. `Judge` (`GrokRulesRefreshService.cs:324-371`) composes the **whole expected ack line** `ANTIPHON_RULES_ACK id={row.Id:N} generation={receipt.Generation:N} sha256={receipt.Sha256}` (`:349`) and tests `lines.Contains(ack, StringComparer.Ordinal)` (`:350`) over `StandaloneLines(answer)`. Any casing difference anywhere in the line, in any of the three hex tokens, makes the line a non-match. `missing_ack` is only the consequence: no ack, a `TurnEnd` present, and the grace elapsed (`:356-359`). | D-1 changes the comparer of the whole line; there is no narrower seam to change. |
| "Likely generation/id tokens" are affected too. | `id` and `generation` are `Guid` `:N` renderings (32 lowercase hex). They appear in the ack line (above) **and** in the failure prefix `ANTIPHON_RULES_FAILED id=… generation=… reason=` (`:345`), which is matched with `l.StartsWith(failure, StringComparison.Ordinal)` (`:347`). An uppercased failure line therefore yields `reason == null`, falls through to the ack check, and decays to `missing_ack` after grace, hiding the provider's stated reason. | Both comparers change (D-1); V-6 pins the failure side. |
| A mismatched ack is never recognised; there is no near-match path. | Confirmed. Pre-grace the row stays `Pending`; once `now >= end.CreatedAt + FinalMessageGraceSeconds` (`DelegationSettings.cs:485`, default 120; the fixture registers no `IOptions<DelegationSettings>`, so `Judge` uses the 120 fallback at `:357-358`) it fails `missing_ack`. | Pre-fix outcome of the new tests is deterministic: `Pending` when the `TurnEnd` is fresh (V-1..V-4), `missing_ack` when it is 3 minutes old (V-6). Both are distinguishable from the green assertion. |
| "Anywhere else in the rules-ack path" may compare these hex tokens. | grep `ANTIPHON_RULES_ACK` / `ANTIPHON_RULES_FAILED` over `server/` and `src/`: the only server sites are the `Prompt()` composer (`:319-322`) and `Judge`; the runner has none; `src/Antiphon.FakeGrok/Program.cs:546-549` **emits** them (lowercase, from `Convert.ToHexStringLower`). The receipt-path hash comparisons are `ValidateExpectedReceipt` (`:214-215`, `!=`), `CanReuse` (`:209`, `==`) and Contracts `GrokRulesTransport.ValidateReceipt` (`GrokRulesTransport.cs:85`, `string.Equals … Ordinal`). Every one compares the runner's `GrokRulesFileStore.cs:23` receipt, built with `GrokRulesTransport.Hash` (`Convert.ToHexStringLower`, `GrokRulesTransport.cs:59`), against the server's `PrepareLaunchAsync` value built with the same function (`:125`). No model text reaches them. | The ack path is the only place a model renders the hex. D-2 leaves the receipt path alone. |
| The prompt-echo match might also be casing-sensitive. | `Header(row.Id)` `StartsWith … Ordinal` (`:333`) and `PromptSubmissionMatch.IsCompleteIn` (Contracts `PromptSubmissionMatch.cs:220`, ordinal `Contains`) compare the server's own `row.Body` against the transcript's `UserPrompt` record of what the server typed. Both sides are server-produced; the model never touches them. | Unchanged. |
| The transcript pipeline preserves the model's casing (so the fix belongs at the comparison). | `GrokTranscriptNormalizer` reassembles chunks verbatim: `GrokRulesInitializationTests.Native_split_ack_and_late_owning_prompt_release_once_after_successful_turn` asserts the persisted `AssistantText` equals the ack byte for byte (`:33-39`). The incident transcript sequence 7 shows `037A46c7` persisted exactly as rendered. | A `Judge`-level test through persisted `TranscriptEntries` rows is a faithful regression; no E2E lane is needed. |
| `OrdinalIgnoreCase` is safe here. | `OrdinalIgnoreCase` is non-linguistic: each UTF-16 code unit is upper-cased with the invariant simple mapping and compared ordinally. No non-ASCII code unit upper-cases to U+0041–U+0046, so only `a-f`/`A-F` fold onto hex letters and the 128 hex characters (32 + 32 + 64) remain the discriminator. The only widening beyond hex is that `ANTIPHON_RULES_ACK`/`ANTIPHON_RULES_FAILED` and the `id=`/`generation=`/`sha256=` labels also become case-insensitive; a line with the right 128 hex characters and a differently-cased keyword is still an ack for the right file. Repo precedent: `AgentTaskLandNotificationService.FileHasSha256Async` already compares a sha256 with `OrdinalIgnoreCase` (`AgentTaskLandNotificationService.cs:277`). | Whole-line `OrdinalIgnoreCase` is adequate; a token regex would add code without adding safety (rejected under D-1). |
| Existing tests would not change meaning. | `Only_current_refresh_assistant_ack_with_confirmed_prompt_releases_barrier` variants (`wrong_id`, `wrong_hash` = 64 zeros, `wrong_generation`, `quoted`, `fenced`, `fragment`, `tool`, `user`, `unrelated_turn`, `before_prompt`, `no_prompt`, `no_end`, `provider_error`) never vary case; `GrokRulesFailureTests` `missing_ack` uses the text `finished reading`; no `GrokRules*` test asserts that an uppercased ack is rejected (grep `ToUpper` in `tests/Antiphon.Tests/Application/GrokRules*`: none). `GrokRulesReceiptTests` `hash` variant uses 64 zeros against the receipt path (D-2, unchanged). | New variants slot into the same `[Arguments]` list; no existing assertion moves. |
| The reason word is a hex token too. | It is not: `reason is "unreadable" or "incomplete" or "revision_mismatch"` (`:348`) is a case-sensitive word match. Only the prefix before it carries hex. | D-3: unchanged; noted as a follow-on only if ever observed. |
| The fixture hash contains hex letters (so a casing variant is non-vacuous). | `Fixture.CreateAsync` hashes `rules\ncontent` (`GrokRulesInitializationTests.cs:185-186`): `93e85d26ce8920e65df8154006491d1b90ba4e2cfaa2473a4bd9c2da2fab7c68`. First letter `e` at index 3. The `id` and `generation` are fresh `Guid.NewGuid()` values, which are not guaranteed to contain a letter. | V-1 uppercases the first letter of the sha (deterministic); V-3/V-4 upper-case the whole token and **assert the mutated line differs from the original** so a letterless GUID cannot make the variant pass vacuously. |

## Decisions

### D-1. Whole-line `OrdinalIgnoreCase` for the ack line and the failure prefix

`server/Application/Services/GrokRulesRefreshService.cs`, method `Judge`, two edits:

```csharp
// :347
var reason = lines.FirstOrDefault(l => l.StartsWith(failure, StringComparison.OrdinalIgnoreCase))?[failure.Length..];
// :350
if (end is not null && TranscriptKinds.IsReportBoundary(end.Kind, end.StopReason) && lines.Contains(ack, StringComparer.OrdinalIgnoreCase))
```

The slice `?[failure.Length..]` is by length and is unaffected by case. Add one comment line above
`:345` naming the reason: the id, generation and sha256 tokens are hex, hex is case-insensitive by
definition, and the provider renders them (CARD-0562: `037A46c7` for `037a46c7`).

Why whole-line: the line is already composed as one string on the server side, the discriminator is
the 128 hex characters, and the CARD-0395 grammar (T-4, "parse ordinally") was about avoiding culture
comparison and quoted/fenced/other-turn copies, not about letter case. `OrdinalIgnoreCase` keeps every
one of those properties (`StandaloneLines` still strips quoted and fenced lines; the owning-turn window
and `TurnEnd` requirement are untouched).

Rejected:

- **Token-level regex** (`^ANTIPHON_RULES_ACK id=([0-9a-fA-F]{32}) generation=([0-9a-fA-F]{32}) sha256=([0-9a-fA-F]{64})$`
  with per-token `OrdinalIgnoreCase`). Same acceptance set for the hex tokens, one more regex to
  maintain, and it only "protects" the keyword's casing, which is not what identifies the file.
- **Lower-casing the candidate lines** before the existing ordinal compare. Equivalent, but it hides
  which comparison is intentionally case-blind behind a transform on the data.
- **Prompt-side instruction** ("copy the sha256 exactly as shown, lowercase"). Depends on the model
  obeying, which is the failure mode being fixed; it would also change `row.Body` for new rows for no
  gain. D-4.
- **A near-match `revision_mismatch` path** ("right shape, wrong digits" fails fast instead of waiting
  for grace). Protocol redesign; the card excludes it.

### D-2. Receipt-path hash comparisons stay `Ordinal`

`ValidateExpectedReceipt` (`:214-215`), `CanReuse` (`:209`) and Contracts `ValidateReceipt`
(`GrokRulesTransport.cs:85`) compare two outputs of `GrokRulesTransport.Hash`, produced by the server
and by the runner from the shared Contracts assembly; both are lowercase by construction and no model
text is involved. Changing them would be harmless but widens the diff past the bug and pins nothing.
Flip condition: a runner that produces uppercase receipts, which cannot happen without changing the
shared function; if it ever does, change all three together and add a `GrokRulesReceiptTests` case.

### D-3. Reason words stay case-sensitive

`unreadable`, `incomplete`, `revision_mismatch` are protocol words, not hex. An uppercased reason word
today decays to `missing_ack` the same way, but it has not been observed and the card scopes the fix to
hex tokens. Not folded; a follow-on card if it is ever seen.

### D-4. `Prompt()` text is unchanged

See D-1's rejected prompt-side instruction. Leaving `Prompt()` alone also keeps `PromptSubmissionMatch`
and FakeGrok's prompt regex (`Program.cs:529-530`, lowercase classes) exactly as they are.

### D-5. No new settings, no runner change, no client change, no FakeGrok knob

An E2E casing lane (a `ANTIPHON_FAKE_RULES_UPPERCASE_ACK` knob in FakeGrok driving
`GrokRulesDispatchAcceptanceTests`) would exercise the same two comparers through the pty and
transcript pipeline, which the ground-truth table shows preserves casing. Not worth a process-spawning
test for a comparer change; listed under out of scope in case Review disagrees.

## Implementation slices

### S1. Comparer fix and regression tests (one commit)

- `server/Application/Services/GrokRulesRefreshService.cs`: the two edits under D-1 plus the comment.
- `tests/Antiphon.Tests/Application/GrokRulesInitializationTests.cs`: new `[Arguments]` rows on
  `Only_current_refresh_assistant_ack_with_confirmed_prompt_releases_barrier` (V-1..V-5). The variant
  branch builds the mutated ack from the same `ack` string the test already composes; the sha token is
  `fixture.Receipt.Sha256`, the generation token `fixture.Receipt.Generation.ToString("N")`, the id
  token `message.Id.ToString("N")`. Each uppercase variant asserts `mutated.ShouldNotBe(ack)` before
  persisting it.
- `tests/Antiphon.Tests/Application/GrokRulesFailureTests.cs`: new test method (V-6) reusing the
  existing `Fixture` and the `Add` helper shape (timestamps 3 minutes old, so grace has elapsed).

Commit message shape: `fix(CARD-0562): match rules-ack hex tokens case-insensitively` with the
verified test counts in the body.

### S2. Documentation (may share S1's commit)

`docs/session-runtime-invariants.md`, § "Grok rules refresh state (CARD-0395)", append one sentence
after the `refresh_loop` sentence: the `ANTIPHON_RULES_ACK` / `ANTIPHON_RULES_FAILED` lines are matched
with `OrdinalIgnoreCase` because the id, generation and sha256 tokens are hex and the provider may render
either case (CARD-0562); the receipt hash comparisons stay ordinal because both sides are
`GrokRulesTransport.Hash` output. No other document states the ack grammar
(`docs/ai-agent-tui-configuration.md:73` lists only the barrier codes).

## Verification design

Executable by Build as written. All tests are `[Category("Integration")]` on the shared test Postgres
(`TestDbFixture`), in-process, no child processes, no `ParallelLimiter` needed. Time source is
`TimeProvider.System` through the existing fixture.

### Delivery inventory

| Slice | File | Change |
|---|---|---|
| S1 | `server/Application/Services/GrokRulesRefreshService.cs` | `:347` and `:350` comparers, one comment |
| S1 | `tests/Antiphon.Tests/Application/GrokRulesInitializationTests.cs` | five `[Arguments]` rows plus their variant branches |
| S1 | `tests/Antiphon.Tests/Application/GrokRulesFailureTests.cs` | one new test method |
| S2 | `docs/session-runtime-invariants.md` | one sentence |

### Proves it works now

| V | Test (class / method / arguments) | Setup | Red on `26633049` | Green after S1 |
|---|---|---|---|---|
| V-1 | `GrokRulesInitializationTests.Only_current_refresh_assistant_ack_with_confirmed_prompt_releases_barrier("sha_one_uppercase_letter", true)` | Ack line with the sha token's first hex letter (index `sha.IndexOfAny("abcdef".ToCharArray())`, index 3 for the fixture) upper-cased; the incident shape (`037A46c7`). Prompt at seq 1, ack at seq 3, fresh `TurnEnd` at seq 4. | `GrokRulesState` stays `Pending`; `RulesAcknowledgedAt` null. | `Ready`; `RulesAcknowledgedAt` set; `RulesPromptSequence` 1; `RulesTurnEndSequence` 4; `DeliveryVerdict` `LateConfirmed`; a second `ReconcileAsync` adds no row (the existing `released` block). |
| V-2 | same method, `("sha_uppercase", true)` | Whole sha token `ToUpperInvariant()`. | as V-1 | as V-1 |
| V-3 | same method, `("generation_uppercase", true)` | Generation token upper-cased in the ack line only (the prompt keeps the server's lowercase); assert `mutated != ack`. | as V-1 | as V-1 |
| V-4 | same method, `("id_uppercase", true)` | Id token upper-cased in the ack line only; assert `mutated != ack`. | as V-1 | as V-1 |
| V-5 | same method, `("wrong_hash_uppercase", false)` | Sha token replaced by `new string('A', 64)`. Negative control: case-blindness must not accept a different value. | Not released (already true today). | Not released. |
| V-6 | `GrokRulesFailureTests.Uppercased_failure_line_is_read_as_its_reason_not_missing_ack()` | Prompt at seq 1; assistant text `ANTIPHON_RULES_FAILED id={ID:N upper} generation={GEN:N upper} reason=unreadable` at seq 2; `TurnEnd` at seq 3, all stamped 3 minutes ago (grace elapsed); retained goal row as in the sibling test; two `ReconcileAsync` calls. | `GrokRulesFailure` is `grok_rules_initialization_failed: missing_ack`. | `grok_rules_initialization_failed: unreadable`; goal row retained `Pending` with `DeliveryAttempts` 0; exactly one incident; two queued rows. |

V-3 and V-4 must upper-case the token and assert the mutated line differs from the original; a
`Guid.NewGuid()` rendering with no letters would otherwise pass vacuously. V-1 is deterministic because
the fixture content is fixed.

### Guards the regression (carried forward, must stay green)

Every existing `[Arguments]` row of `Only_current_refresh_assistant_ack_with_confirmed_prompt_releases_barrier`,
in particular `wrong_id`, `wrong_hash`, `wrong_generation`, `fragment`, `quoted`, `fenced`;
`GrokRulesFailureTests.Failed_read_or_missing_ack_retains_the_original_work_and_fails_once` (all four
reasons); `GrokRulesReceiptTests` `hash` variant (D-2 unchanged); `GrokRulesInitializationTests.Native_split_ack_and_late_owning_prompt_release_once_after_successful_turn`.

### Positive controls (Mutation, method-scoped)

| PC | Mutation | Expected red | Restore, then green |
|---|---|---|---|
| PC-1 | `:350` `StringComparer.OrdinalIgnoreCase` → `StringComparer.Ordinal` | V-1..V-4 red (`Pending`, not `Ready`); V-5 and every carried-forward row unchanged. | V-1..V-4 |
| PC-2 | `:347` `StringComparison.OrdinalIgnoreCase` → `StringComparison.Ordinal` | V-6 red (`missing_ack`). | V-6 |

Run each PC with `--treenode-filter "/*/*/GrokRulesInitializationTests/Only_current_refresh_assistant_ack_with_confirmed_prompt_releases_barrier"`
and `"/*/*/GrokRulesFailureTests/Uppercased_failure_line_is_read_as_its_reason_not_missing_ack"`
respectively; the two mutations are independent and may be batched (different lines, different tests).

### Commands

Build once to a producer-owned isolated output (forward slash), run both classes together with the
CARD-0403 combined-class syntax, keep the TRX, delete every `bin-c562` directory afterwards:

```
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c562/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c562/ -- --treenode-filter '/*/*/(GrokRulesInitializationTests*)|(GrokRulesFailureTests*)/*' --report-trx --report-trx-filename c562.trx --results-directory .antiphon/c562
Get-ChildItem -Recurse -Directory -Filter bin-c562 | Remove-Item -Recurse -Force
```

Expected after S1: every test in both classes passes; the TRX names V-1..V-6 with nonzero counts.
Red confirmation before S1 is one run of the same filter with only the test edits applied (V-1..V-4
`Pending`, V-6 `missing_ack`); commit the test edits as the checkpoint before that run.

## Out of scope

- Reason-word casing (D-3); trailing punctuation or backticks around the ack line; an ack split across
  two standalone lines. None observed.
- Receipt-path comparisons (D-2).
- `Prompt()` wording (D-4) and a FakeGrok casing knob (D-5).
- Re-dispatching the failed task `23c9485a` (CARD-0561 Code): orchestrator's call after land.
- Any change to `StandaloneLines`, the owning-turn window, the grace or deadline arithmetic.

## Cost and next stage

Code: about 30–45 minutes including the targeted run (two classes, seconds each, plus the isolated
build). Review: ordinary, different company from Code. Mutation: PC-1 and PC-2 after land. TestDesign
is not a separate stage: the table above is at method granularity with red and green outcomes named.
