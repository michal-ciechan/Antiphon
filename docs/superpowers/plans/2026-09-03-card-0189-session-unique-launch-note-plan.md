# CARD-0189 — Make the always-on launch note session-unique — plan

**Date:** 2026-09-03 · **Card:** CARD-0189 (`ab1940d5-61e7-4c83-a99a-8ee3413d24d2`) · **Status:**
plan (investigate + design; no implementation in this pass) · **Verified against:** this worktree
@ `aa493525`. CARD-0181 has already landed (`fa7dc3c0` S1–S2, `a6eea917` S3, `d3ba36a3` S4); this
card is the residual that plan named and deferred.

**Sources re-read this pass:** the card, CARD-0181 residual
(`docs/superpowers/plans/2026-08-24-card-0181-stale-sidecar-claim-vs-exact-id-bind-plan.md:135-140,
363-367`), `ChannelPreamble.cs:84-100`, `AgentSessionService.DeliverLaunchNoteAsync:2124-2163`,
`AgentControlService.cs:251-270`, `LaunchNotes.cs`, `SessionInputLog.MatchesRecordedInput:76-83`,
`PromptSubmissionMatch` (`MinMatchChars` 12, `MatchWindowChars` 200), `TranscriptTailer.EvaluateCandidates`
C0 (`:557-568`) and C4 (`:606-610`), `ChannelReplyDispatcher.PromptsMatch:1378-1386`, CARD-0233 /
CARD-0338 / CARD-0055 notes, every C# citation of `BootstrapBody` / `RestartResumeBody`, client
`SessionTranscriptPanel` (no parse), `DelegationReportFormatter.Short`.

No production code is changed by this plan.

---

## Verdict up front

1. **The residual is real and still open.** C0 (CARD-0181) refuses a file whose basename is another
   *known* session's id. A Claude **self-fork** chooses a new GUID basename, so C0 has no namesake.
   Two same-named always-on incarnations then share cwd, `--name`, and the identical templated launch
   note, so C2 / C2b / C4 all pass and C3 only rejects *older* files — the CARD-0181 vector-2 shape,
   minus C0. Closing it means the launch note C4 matches against must differ per session id.

2. **Do not suffix. Prefix.** The card (and CARD-0181's residual) sketched
   `[session <id:8>]` as a **suffix** at delivery. That does not close C4. C4's needle is the
   candidate prompt's **first 200 normalized chars** (`PromptSubmissionMatch.MatchWindowChars`,
   `SessionInputLog.MatchesRecordedInput:76-83`). Measured on this commit:

   | Body | Length | Suffix in the 200-char C4 needle? |
   |---|---:|---|
   | `ChannelPreamble.BootstrapBody` | **195** | `" [ses"` only — **zero** characters of the session id |
   | `ChannelPreamble.RestartResumeBody` | **211** | **none** — the tag sits past the window |
   | `RecoveryNoteBody` (out of scope) | 244 | none |

   Two incarnations that suffix the bootstrap produce **identical** 200-char needles. Two that
   suffix the restart note produce the untagged restart needle. C4 still cannot tell them apart.
   A **prefix** `[session <8 hex>] ` (19 chars, house style of `[task <8>]` / `[check <8>]`) sits
   entirely inside the window on both bodies. That is the whole design.

3. **Append at delivery, do not mutate the frozen constants.** `ChannelPreamble.BootstrapBody` /
   `RestartResumeBody` stay byte-for-byte. `DeliverLaunchNoteAsync` wraps whichever body it is about
   to enqueue with `ChannelPreamble.WithSessionTag(body, sessionId)`. Construction in
   `AgentControlService` (`:269`) cannot tag: the fresh path has not created the row yet, and the
   resume path reuses the previous id — the session id is only certain at delivery.

4. **Blast radius is tests that assert the *delivered* bytes, not every citation of the constants.**
   Nothing in the client, channel dispatcher, or C0–C4 *matcher* parses the ritual text. Dispatcher
   fixtures that *insert* `BootstrapBody` as a stand-in QueuedUserPrompt stay as they are. See §2.

5. **One Code slice.** Helper + delivery wrap + the tests that go red + the C4 residual pin.
   Recovery notes, Grok/Codex, and living-doc rewrites of CARD-0181 are out of scope (§5).

---

## 1. Why C4 still collides (the residual, in the current code)

Discovery (`TranscriptTailer.EvaluateCandidates:540-614`) for a non-namesake file:

- **C0** — refuse if the basename is a GUID this runner has a sidecar for (`:557-568`). A self-fork
  is a *new* GUID; no sidecar; C0 is a no-op. The comment at `:560` still says the quiet part:
  "two incarnations of one always-on agent share cwd, `--name` and a templated launch note, so
  C2/C2b/C4 all pass — and C3 only rejects OLDER files."
- **C4** — `probe.ContentMatched` iff some user-prompt in the file appears in *this* session's
  `SessionInputLog` (`:606-610`). Direction: needle = first 200 normalized chars of the
  **candidate record**; match = that needle is a substring of **this session's input log**.

So uniqueness has to live in the candidate's *head*, not its tail. Extra text in the input log
never prevents matching a shorter candidate (Contains). Extra text in the candidate's first 200
chars, absent from the other log, does.

The existing runner test `Templated_launch_note_does_not_let_a_previous_incarnation_bind_the_next_ones_file`
(`TranscriptAdoptionSafetyTests.cs:1458-1504`) pins **C0** on a *namesake* file with *identical*
copied `BootstrapBody` text. It does not pin the self-fork residual. Sibling test
`Two_sessions_in_one_cwd_adopt_their_own_forks_not_each_others` (`:228-268`) already proves C4
separates two forks when the prompts **differ**. The missing pin is the same shape with two
launch notes that share the 195-char ritual and differ only by a session tag.

Launch notes are ClaudeCode + `SystemPromptAppend` only (`AgentControlService.cs:266-270`), and
not the check interpreter. That is exactly the always-on / channel-bound population the residual
names. Grok and Codex do not receive these bodies.

---

## 2. Blast radius — what parses or displays this text today

**Frozen constants (do not change, do not re-assert as delivered text):**

| Citation | What it does with the exact string | Action |
|---|---|---|
| `ChannelPreamble.BootstrapBody` / `RestartResumeBody` (`:85-93`) | Source of the ritual. Comment: frozen so launch plumbing, recovery, tests, fakeclaude, docs cite one source. | Keep frozen. Document that *delivery* prefixes a tag. |
| `AgentControlService.cs:269` | `new LaunchNotes(BootstrapBody, RestartResumeBody)` | Unchanged. Tagging is not here. |
| `ChannelContractsTests.Note_bodies_reference_workspace_files_and_no_reply` | `ShouldContain` on the constants (CLAUDE.md, READY, NO_REPLY). | Unchanged. Add `WithSessionTag` cases next to it. |
| `TranscriptAdoptionSafetyTests` C0 templated-note test | Copies `BootstrapBody` literally (runner must not reference the server). | Unchanged — it pins C0 with *identical* text on purpose. |
| CARD-0233 / CARD-0338 / CARD-0078 / telegram-bot docs | Quote the ritual as human-readable intent. | Leave historical. Do not rewrite CARD-0181's "suffix" sentence; this plan supersedes it. |

**Delivery-path assertions (will go red; update to the tagged body):**

| Test | Assertion | Why it breaks |
|---|---|---|
| `AgentSystemPromptLaunchTests` | `SubmittedBodies.ShouldBe([BootstrapBody])` at `:53`, `:74`, `:247`, `:266`; `ShouldBe([RestartResumeBody])` at `:94`, `:326`; `note.Body.ShouldBe(RestartResumeBody)` `:319`; `note.Body.ShouldBe(BootstrapBody)` `:351`; `InsertTurnAsync(BootstrapBody, "READY")` `:366` | Real launch chain through `DeliverLaunchNoteAsync`. Queued `Body` and submitted bytes become the tagged form. `:366` is a *second* synthetic turn for "bootstrap produces no channel reply" — keep the untagged insert (dispatcher identity, not delivery). |
| `AgentSessionLaunchFailureTests` `:346` | `SubmittedBodies.ShouldContain("launch note body")` | That fixture also goes through `DeliverLaunchNoteAsync` (`LaunchNotes("launch note body", null)` at `:319`). Expect `WithSessionTag("launch note body", fixture.SessionId)`. |

**Dispatcher / review fixtures that INSERT the constant as a QueuedUserPrompt (do not change):**

These reconstruct CARD-0233's "a launch note landed inside an open channel/review turn". They never
call `DeliverLaunchNoteAsync`. `PromptsMatch` is 120-char containment of a *Channel-origin* body
against the turn prompt (`ChannelReplyDispatcher.cs:1378-1386`); an untagged (or tagged) bootstrap
does not contain a Telegram envelope, which is the point.

- `ChannelBridgeTests.A_mid_turn_launch_note_does_not_steal_the_channel_reply` (`:144`)
- `ChannelReplyDurabilityTests.Ttl_with_a_completed_unmatched_turn_is_turn_unmatched` (`:398`)
- `ReviewReplyDispatcherTests.A_mid_turn_queued_prompt_does_not_steal_the_review_reply` (`:171`)
- `ChannelMachineTurnTextTests.System_plain_text_is_not_delivered_and_does_not_claim` (`:118`) —
  seeds a System-origin row with `BootstrapBody` and a READY turn; CARD-0338 origin gate, not
  delivery. Untagged still System, still marker-only.

**Not parsers of this text:**

- **Client UI.** No `New session started` / `BootstrapBody` string in `client/`. Transcript and
  queue surfaces render `Body` / `UserPrompt` as opaque text. Operators will see
  `[session abcdef12] New session started…` in the session transcript. That is useful, not a break.
- **Channel reply routing.** Launch notes enqueue as `Origin=Ui` (Now) or `Origin=System`
  (yield-to-channel / fallback) (`DeliverLaunchNoteAsync:2145-2155`) and "notes never route to a
  chat" (`:2123`). `Bootstrap_produces_no_channel_reply` stays the pin. CARD-0338 delivers
  Delegation / Check / Scheduled plain text; System stays marker-only; Ui is a terminal turn.
- **Task / check markers.** `[task <8>]`, `[check <8>]`, `[antiphon-task:<8>]`
  (`AgentTaskCheckService.TaskMarkerPattern` is `\[antiphon-task:[0-9a-fA-F]{8}\]`). `[session <8>]`
  matches none of them. Settlement and check-in scrubbing do not see launch notes.
- **CARD-0055 delivery confirmation.** `IsConfirmedBy` is the same 200-char **head** window, opposite
  direction (record contains body's head). A prefix is *in* that head, so confirmation still
  requires the record to carry the tag — which it will, because the tag is part of the typed body.
  Completeness (`IsCompleteIn`) wants the full normalized body; +19 chars is irrelevant to paste
  collapse.
- **Logs.** Launch-note failure logs the session id, not the body (`:2150-2160`).

**READY / NO_REPLY instructions stay at the end of the frozen bodies.** Prefixing does not move
them. Channel agents still reply READY on bootstrap and NO_REPLY on resume.

---

## 3. Design

### 3.1 Helper (on `ChannelPreamble`, next to the frozen bodies)

```csharp
/// <summary>Eight hex chars, same short-id as <c>[task …]</c> / <c>[check …]</c>.</summary>
public static string SessionShortId(Guid sessionId) => sessionId.ToString("N")[..8];

public static string SessionTag(Guid sessionId) => $"[session {SessionShortId(sessionId)}]";

/// <summary>
/// Prefixes <paramref name="body"/> with <see cref="SessionTag"/> so C4's 200-char head window
/// can tell two always-on incarnations' launch notes apart. Must be a prefix: a suffix on
/// <see cref="BootstrapBody"/> (195 chars) or <see cref="RestartResumeBody"/> (211 chars) does
/// not reach that window. Applied at delivery, not baked into the frozen bodies.
/// </summary>
public static string WithSessionTag(string body, Guid sessionId) =>
    $"{SessionTag(sessionId)} {body}";
```

Short-id is `Guid.ToString("N")[..8]`, the same function as `DelegationReportFormatter.Short`.
Collision of two concurrent incarnations on those 8 hex chars is 1/16^8; ignored.

Do **not** put this helper in Contracts. The runner never tags; it only matches whatever was
typed. Server-only keeps the runner free of channel-preamble knowledge (the C0 test's copy of
`BootstrapBody` stays the documented exception).

### 3.2 Delivery (`AgentSessionService.DeliverLaunchNoteAsync`)

After the empty-body guard, before enqueue (both the Now/WhenIdle try and the WhenIdle fallback
must use the tagged string — they already share `body`):

```csharp
var body = resumeMode is null ? notes.FreshBody : notes.ResumeBody;
if (string.IsNullOrWhiteSpace(body))
    return;

body = ChannelPreamble.WithSessionTag(body, sessionId);
```

Yield-to-channel (CARD-0233 S4), origin, mode, and the "never fail the launch" fallback are
untouched. Tagging a custom `LaunchNotes` body (the launch-failure fixture's `"launch note body"`)
is correct: any launch note is C4 evidence.

### 3.3 Why prefix, restated as the never-weaken argument

C4 today is "candidate head appears in our log". Tagging the candidate head with a per-session
token makes a launch-note C4 match **stricter** (a sibling incarnation's tagged prompt is no
longer in our log). No other prompt class changes. C0–C3, claim strength, fork-follow, and
CARD-0055's matcher are untouched. A suffix would claim to close the residual and leave it open
for both bodies; that is the failure mode this plan exists to prevent.

Mixed-version (one incarnation pre-change): a **new** tagged file cannot be stolen by an **old**
untagged log (needle starts with `[session …]`, old log hasn't got it). An old untagged file is
usually C3-older than a new child. The live residual is two *new* self-forked incarnations; prefix
closes that.

A future hook that *prepends* more than ~180 chars in front of the queued body would push the tag
out of the window again. Today's queue types the stored `Body` as the prompt; nothing prepends.
Named, not fixed here.

### 3.4 RecoveryNoteBody

Out of scope. Compaction recovery runs on an already-bound session (`CompactionRecoveryService`
enqueues `RecoveryNoteBody` as-is). It is not a discovery/C4 input. Do not tag it on this card.

---

## 4. Tests

**Unit — `ChannelContractsTests` (additions)**

- `WithSessionTag_prefixes_the_short_id_and_leaves_the_body_intact` — starts with
  `[session {N[..8]}] `, then the original body; constants themselves unchanged.
- `WithSessionTag_differs_across_session_ids` — two ids, two strings, neither contains the other's
  tag.
- Optional length pin: `BootstrapBody.Length` is 195 and `RestartResumeBody.Length` is 211 *today*,
  with a comment that a suffix would miss `MatchWindowChars`. Do not fail the build if someone
  lengthens the ritual; the prefix helper is what must stay a prefix.

**Unit — `PromptSubmissionMatchTests` / `SessionInputLog` (additions, synthetic strings, no server
reference)**

- `A_prefix_of_19_chars_distinguishes_two_otherwise_identical_long_prompts` — two logs, two
  candidates `"[session aaaaaaaa] " + 195-char body` vs `bbbbbbbb`; each log matches only its
  candidate.
- `A_suffix_past_the_head_window_does_not` — shared prefix ≥ 200 chars, differ only by a 19-char
  suffix; `MatchesRecordedInput` cross-matches. This is the trap the delivery helper must not
  walk into; if someone later "simplifies" to a suffix, this stays green and the runner residual
  test below goes red.

**Integration — `AgentSystemPromptLaunchTests`**

Replace every `ShouldBe([ChannelPreamble.BootstrapBody])` / `RestartResumeBody` on
`SubmittedBodies` and queued `note.Body` with `WithSessionTag(..., thatSessionId)`. Session id is
`Guid.Parse(started.PersistentSessionId!)` on fresh, `h.SessionId` on resume. Do not change
`InsertTurnAsync(BootstrapBody, "READY")` in `Bootstrap_produces_no_channel_reply` (synthetic
dispatcher turn). Yield-to-channel test: Pending System row body is the tagged restart note;
after flush, `SubmittedBodies` is `[channelBody, taggedRestart]`.

**Integration — `AgentSessionLaunchFailureTests`**

`ShouldContain(ChannelPreamble.WithSessionTag("launch note body", fixture.SessionId))`.

**Runner — `TranscriptAdoptionSafetyTests` (the card's acceptance)**

`Two_same_named_incarnations_with_tagged_launch_notes_bind_only_their_own_fork`. Shape of
`Two_sessions_in_one_cwd_adopt_their_own_forks_not_each_others` (`:228-268`):

- Shared `--name`, shared cwd, `TranscriptClaimRegistry` shared, `tree.NewTranscript()` for both
  files (Claude-chosen names, **not** either session id, **no** sidecars → C0 does not apply).
- Copy `BootstrapBody` literally (same comment as the C0 test: runner must not reference server).
- `inputA.Append("[session " + sessionA.ToString("N")[..8] + "] " + bootstrap)` and likewise for B.
- Each file's user record is that session's tagged text.
- A binds only file A; B binds only file B; swapping would be the residual (newest-mtime + shared
  ritual).

Keep the existing C0 templated-note test. It is a different gate.

---

## 5. Out of scope

- Mutating `BootstrapBody` / `RestartResumeBody` / `RecoveryNoteBody` text.
- Tagging compaction recovery notes.
- Launch notes for Grok / Codex (they do not get these bodies; Codex bind is first-prompt C4 of
  whatever was actually typed — CARD-0190).
- Changing C4's 200-char window, C0, claim strength, or the migration-shim deletion.
- Client UI for the tag (transcript already shows the prompt).
- Rewriting CARD-0181's historical "suffix" sentence.
- Channel dispatcher / review identity-theft fixtures.

---

## 6. Slice

**S1 — sonnet — prefix the launch note at delivery.**

Files: `server/Application/Services/ChannelPreamble.cs`,
`server/Application/Services/AgentSessionService.cs` (`DeliverLaunchNoteAsync` only),
`tests/Antiphon.Tests/Application/ChannelContractsTests.cs`,
`tests/Antiphon.Tests/Application/AgentSystemPromptLaunchTests.cs`,
`tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs`,
`tests/Antiphon.SessionRunner.Tests/PromptSubmissionMatchTests.cs` (or the SessionInputLog
section of that class),
`tests/Antiphon.SessionRunner.Tests/TranscriptAdoptionSafetyTests.cs`.

A one-line XML note on `BootstrapBody` / `RestartResumeBody`: delivered text is
`WithSessionTag(body, sessionId)`; the constant is the ritual, not the queued bytes.

**Verify** (class filters, not a namespace — `docs/testing-and-build.md` Gotcha #18):

```text
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0189/ --treenode-filter "/*/*/ChannelContractsTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0189/ --treenode-filter "/*/*/AgentSystemPromptLaunchTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0189/ --treenode-filter "/*/*/AgentSessionLaunchFailureTests/*"
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c0189/ --treenode-filter "/*/*/PromptSubmissionMatchTests/*"
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c0189/ --treenode-filter "/*/*/TranscriptAdoptionSafetyTests/*"
```

Run the two projects sequentially. Delete the `bin-c0189/` trees after. Forward slash on
`OutputPath`.

Done when: two same-named self-forked incarnations with tagged launch notes bind only their own
file; every launch-path test expects the prefix; the frozen ritual strings are unchanged; READY /
NO_REPLY still terminate the bodies.
