# CARD-1465 plan: CRLF raw string literal in the reply integration test

Test-only. `AgentTaskReplyIntegrationTests.continue_replays_standing_authority_on_a_question_block`
asserts `queued.Body.ShouldContain(BlockedNote.ContinueMessage(authority))`. `BlockedNote.ContinueMessage`
(`server/Application/Services/BlockedNote.cs`) is a two-line raw string literal, so on a
`core.autocrlf=true` checkout (`* text=auto`) its line break is CRLF. The queued body is
LF-normalised by `AgentTaskReplyService` (`$"{TaskMarker}\n\n{message.Trim()}".ReplaceLineEndings("\n")`)
and again by `SessionMessageQueueService` (`.Trim().ReplaceLineEndings("\n")`), so the expected text
never matches on Windows. On an LF checkout both sides agree.

## Slices

- **S1:** normalise the expected side only: `BlockedNote.ContinueMessage(authority).ReplaceLineEndings("\n")`.
  The assertion still requires the whole continue message, including its line break, inside the
  queued body. No product change; the product's LF normalisation is the behaviour under test.

Scan: `git grep '"""'` over `AgentTaskReply*` and `Blocked*` tests. The other raw literals
(`AgentTaskReplyIntegrationTests` report bodies near lines 322, 5140, 5224, 5253 and
`BlockedQuestionTests` line 15) are report *inputs* to the parser or seeded turns, not expected
text compared against an LF-normalised body; the one at 322 already calls `ReplaceLineEndings("\n")`.
`BlockedNote.StandingAuthorityBlock` is asserted only through its fixed single-line header
(`AgentTaskServiceIntegrationTests`, `DelegationUnitTests`), which is line-ending independent. Only
line 1115 is the same defect.

## Verification

- **V-1:** the continue message is found in the queued body regardless of the checkout's line
  endings (CP-1, method `continue_replays_standing_authority_on_a_question_block`).
- **R-1:** the three sibling `continue_refuses_*` methods stay green (CP-1, same filter).
- **W-1 (manual witness, outside the table):** in a scratch detached worktree at the base commit,
  rewrite `server/Application/Services/BlockedNote.cs` and the test file to CRLF
  (`sed -i 's/$/\r/'`), build and run V-1's method through `scripts/build-slot.ps1`: red with the
  `proceed\r\nwithout` expected text. Apply S1 to that scratch tree, rebuild, rerun: green. The scratch
  worktree is removed afterwards. A real Windows CRLF reproduction is owed to the Windows lane.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-card1465-crlf/` | reply-continue | `/*/*/AgentTaskReplyIntegrationTests/continue_*` | V-1, R-1 | all listed, 0 failed | 4 | 8 |

No Unit lane, assembly, full-class or qualification run: the brief asks for minimal testing and the
change is one assertion's expected side. Deferred to the Windows lane: the CRLF reproduction of V-1.

### Cost and mutation

Ordinary floor 8 minutes plus the W-1 witness (two scratch builds). Pending method-scoped
SourceLanding Mutation candidate, not executed here:

- **PC-1:** drop `.ReplaceLineEndings("\n")` from the expected side and check out
  `BlockedNote.cs` with CRLF; run only V-1; require the `ShouldContain` red, restore, then green.
