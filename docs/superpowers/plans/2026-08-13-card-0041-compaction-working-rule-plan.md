# CARD-0041 — A compacted session reads Working forever: implementation plan

**Status:** planned, not implemented (implementation assigned to the next agent)
**Card:** CARD-0041 (board 8988ca03), labels session/working-state/compaction/reliability
**Verified against:** stored rows of session `e77fb0a7-8ab9-4ea7-9a2e-0588c5e0c598`
(`GET /api/sessions/<id>/transcript`), the session's raw JSONL
(`~/.claude/projects/C--src-Antiphon/e77fb0a7-….jsonl`), and
`SessionMessageQueueService.IsWorkingAsync` / client `isWorking()` / runner
`TranscriptWorkingState.IsProvenIdle` as they exist on `master` today.

## 1. Diagnosis verification — confirmed, with three refinements

The card's diagnosis was re-derived from the primary sources, not assumed.

**Confirmed.** The stored rows after the session's last real turn are exactly as the card
states (seq 11 TurnEnd 10:46:22 → 12 raw `/compact …` UserPrompt 10:53:10 → 13
CompactBoundary "Context compacted (manual)" 10:54:04 → 14 continuation UserPrompt
10:53:54 → 15 `<command-name>` wrapper → 16 `<local-command-stdout>`). Under the current
rule: end = seq 11 (ts 10:46:22), activity = seq 14 (max ts 10:53:54). Sequence says
working; the backfill timestamp override does not rescue it (10:53:54 > 10:46:22).
Working forever, no TurnEnd ever coming. Both escaping shapes are real:

1. **The raw typed slash-command prompt** (seq 12). The raw JSONL confirms Claude writes
   the literal typed text as a plain `type=user` record (line 40) *in addition to* the
   `<command-name>`-wrapped record (line 49). Only the wrapper matches
   `TranscriptKinds.IsLocalCommandRecord`.
2. **The compaction continuation prompt** (seq 14). A plain UserPrompt to our
   normalizer; nothing excludes it.

**Refinement A — "each alone still leaves it stuck" is true in substance, with a
nuance.** Boundary-as-end *alone* would, on the server and client, happen to read idle
for THIS data via the timestamp override (activity ts 10:53:54 < boundary ts 10:54:04 —
the timestamps are non-monotonic in the transcript itself, and here they lean the lucky
way). That rescue is accidental: it depends on Claude stamping the continuation record
before the boundary record, which nothing guarantees, and the **runner** has no
timestamp override (deliberately — its mirror is file-ordered) so it stays stuck
regardless: in file order the continuation (line 47) comes AFTER the boundary (line 46).
Continuation-exclusion *alone* leaves all three stuck on the raw typed prompt (seq
12 / line 40). So both changes are required, exactly as the card concludes.

**Refinement B — boundary-as-end must be scoped to `trigger=manual`.** The card says
"treat CompactBoundary as a turn END like SessionRestartBoundary", unqualified. An
**auto** compaction boundary lands MID-turn (auto-compact runs when a request starts
over the context threshold: prompt already submitted, model about to stream). Counting
it as an end would misread a genuinely working session as idle for the window between
the boundary and the first assistant record — a WhenIdle injection into a working
composer, and on the runner a false "proven idle" fed to the CPU watchdog, whose
contract is "cannot prove idle must never read as idle". A *manual* compact only runs
between turns, so it genuinely proves the previous turn is over. The stored Text already
carries the trigger — `TranscriptNormalizer.FromSystem` writes
`"Context compacted (manual)"` / `"(auto)"` — so scoping costs one text check. A
null-trigger boundary (old rows) stays housekeeping (today's behavior, conservative).

**Refinement C — nothing flushes the queue when the boundary lands (gap the card does
not cover).** `AgentSessionRuntime.IsTurnBoundary` fires the flush only on
TurnEnd(end_turn) and the interrupt marker. With the rule fixed, a session becomes
*readable* as idle at the boundary, but messages stranded from before (the CARD-0029
brief is the live case) still sit until `FlushStrandedQueuesAsync`, which only serves
idle+live+**always-on** sessions. The recovery note itself survives only by luck of
ordering (`EnqueueAsync`'s idle fast-path runs after the boundary persists). A
boundary-time flush is part of the fix — but a **narrow** one, see step 5: routing
manual boundaries through `IsTurnBoundary` wholesale would fire `PublishFinishedAsync`
(a spurious "Agent finished" on every idle /compact — the SessionFinishedDuplicateTests
domain) and `AgentTaskReplyService.OnTurnEndAsync` (settlement attempt against the stale
pre-compaction report — the exact mis-settle CARD-0029 warns about).

**Useful discovery for tests:** the real continuation record carries
**`isCompactSummary: true`** (line 47 of the raw JSONL) — a structural flag. The fix
below still matches by text prefix, because the rule must heal *already-stored* rows
(the stuck session reads idle the moment the new rule deploys, with no data migration)
and the client only has `text`. The canary pins both the flag and the exact wording so
a future structural migration stays possible.

## 2. The fix

Two rule changes, in all three lockstep implementations, plus the flush:

- **A CompactBoundary whose text marks a manual trigger counts as a turn END**, like
  SessionRestartBoundary. Auto/unknown-trigger boundaries stay housekeeping (excluded
  from activity, not an end) exactly as today.
- **The compaction continuation prompt is excluded from activity** by prefix:
  `"This session is being continued from a previous conversation"` (the real record
  continues "…that ran out of context." even for manual compacts — pinned by the
  canary; the shorter prefix is the match, the canary pins the full wording).
- **A manual boundary triggers a narrow queue flush** (deliver-if-idle only; no
  Finished publish, no reply/settlement dispatch).

Verified against the real rows: end becomes seq 13, activity seq 12, 12 < 13 → idle on
sequence alone in all three implementations (no timestamp override needed). In runner
file order: lastEnd = line 46, lastActivity = line 40 → proven idle.

Matching the raw `/`-prefixed typed text was considered and stays REJECTED (as in the
card): a prompt legitimately beginning with `/` is real activity, and with the boundary
counting as an end the raw record is outranked and harmless. A test pins this
non-exclusion (step 6, server case 3).

## 3. Implementation steps

### Step 1 — `TranscriptKinds` (src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs)

- `public const string CompactionContinuationPromptPrefix = "This session is being continued from a previous conversation";`
  plus `IsCompactionContinuationPrompt(string? kind, string? text)` (UserPrompt +
  `TrimStart().StartsWith(…, Ordinal)` — same shape as `IsInterruptPrompt`). Doc
  comment: written by compaction into the transcript, synthetic, NO TurnEnd follows;
  live miss 2026-08-11 (CARD-0041).
- `public const string ManualCompactMarker = "(manual)";` plus
  `IsManualCompactBoundary(string? kind, string? text)` → kind == CompactBoundary &&
  text contains the marker. Doc comment: the marker comes from
  `TranscriptNormalizer.FromSystem`'s `"Context compacted ({trigger})"`; manual proves
  the previous turn over, auto lands mid-turn and must NOT be an end.
- Update the `CompactBoundary` doc comment: no longer "NOT a turn end" unqualified —
  manual boundaries are turn ends for the working rule; auto boundaries are not.

### Step 2 — server `SessionMessageQueueService.IsWorkingAsync`

- End query: add
  `|| (t.Kind == TranscriptKinds.CompactBoundary && t.Text != null && t.Text.Contains(TranscriptKinds.ManualCompactMarker))`
  (inline for EF translation, like the interrupt prefix already is).
- Activity query: add
  `&& !(t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.StartsWith(TranscriptKinds.CompactionContinuationPromptPrefix))`.
  Keep the existing blanket CompactBoundary activity exclusion (covers auto/null).
- Extend the method's comment with the CARD-0041 story (raw typed prompt + continuation
  outranked the last end; manual boundary is the proof of idleness).

### Step 3 — client `isWorking()` (client/src/features/agents/SessionTranscriptPanel.tsx)

- New consts mirroring TranscriptKinds: `CONTINUATION_PREFIX`, `MANUAL_COMPACT_MARKER`.
- End branch: `e.kind === 'CompactBoundary' && (e.text ?? '').includes(MANUAL_COMPACT_MARKER)`
  joins TurnEnd / SessionRestartBoundary / interrupt.
- Housekeeping branch keeps non-manual CompactBoundary and adds
  `isCompactionContinuation(e)` (UserPrompt + trimStart().startsWith(CONTINUATION_PREFIX)).
- `SessionWorkingBadge` needs no change (it renders the server's verdict from the queue
  DTO); the transcript panel's own `working` memo picks the change up automatically.

### Step 4 — runner `TranscriptWorkingState.IsProvenIdle` (src/Antiphon.SessionRunner/TranscriptWorkingState.cs)

- End branch: add `TranscriptKinds.IsManualCompactBoundary(entry.Kind, entry.Text)`.
- Housekeeping branch: CompactBoundary stays (now reached only for auto/null), add
  `TranscriptKinds.IsCompactionContinuationPrompt(entry.Kind, entry.Text)`.
- The deliberate no-timestamp-override divergence is untouched; refresh the class
  comment's lockstep list.

### Step 5 — boundary-time flush (server `AgentSessionRuntime` + `SessionMessageQueueService`)

- New `SessionMessageQueueService.FlushIfIdleAsync(Guid sessionId, CancellationToken ct)`:
  take the per-session lock, `DeliverNextLockedAsync`, publish QueueChanged only when
  something moved. Explicitly NO `PublishFinishedAsync` on an empty queue and NO
  channel/review/task dispatch — rationale in Refinement C, written into the method
  comment.
- `AgentSessionRuntime.ProcessEntryAsync`: in the existing CompactBoundary branch
  (alongside `DispatchCompactionRecoveryAsync`), when
  `TranscriptKinds.IsManualCompactBoundary(...)`, call `FlushIfIdleAsync`. Order:
  recovery dispatch first (so its note is in the queue), then flush.
- Sync path (`SyncTranscriptAsync`): a manual boundary that arrives only via backfill
  must flush too — same argument as the 2026-08-08 TurnEnd-via-sync fix. Extend
  `PersistResult` with `AddedManualCompactBoundary` and call `FlushIfIdleAsync` for it.
  Do NOT widen `IsTurnBoundary` (it feeds `actOnTurnBoundary`, `AddedTurnBoundary`, the
  finished toast, and settlement).
- `FlushIfIdleAsync` re-checks `IsWorkingAsync` via `DeliverNextLockedAsync`'s existing
  guard, so a mid-turn sync stays a no-op.

### Step 6 — comment/doc corrections riding along

- `CompactionRecoveryService` comment "WhenIdle is safe here BECAUSE the boundary kind
  is excluded from IsWorkingAsync" — falsified by this card (the note stranded whenever
  the raw prompt/continuation followed). Rewrite: safe because a manual boundary is a
  turn END and triggers the flush.
- `TranscriptNormalizer.FromSystem` comment "Deliberately NOT a turn end (no
  StopReason)" — still true at the normalizer level (kind stays CompactBoundary, no
  TurnEnd part is emitted); add "the working rules treat MANUAL boundaries as turn ends
  (CARD-0041)". Same for `TranscriptNormalizerTests.Compact_boundary_is_not_a_turn_end`
  (test stays valid; clarify its comment).
- CLAUDE.md: extend the working/idle gotcha family with the compaction entry (raw typed
  slash prompt + continuation prompt; manual boundary = end; three implementations).

## 4. Tests

### Server — `tests/Antiphon.Tests` (extend `SessionMessageQueueDeliveryVerificationTests`, which already holds `Session_with_compact_boundary_after_last_turn_end_reads_idle`; harness helpers in `BridgeQueueHarness`)

1. **The CARD-0041 shape reads idle** (red today): TurnEnd, raw `/compact args`
   UserPrompt, manual CompactBoundary, continuation UserPrompt, `<command-name>`,
   `<local-command-stdout>` — with the REAL non-monotonic timestamps (boundary ts LATER
   than continuation ts) so the case cannot be quietly satisfied by the timestamp
   override.
2. **Continuation after a manual boundary reads idle** (red with boundary-as-end alone
   when timestamps lean the other way — use continuation ts AFTER boundary ts to defeat
   the override, pinning that the exclusion, not the override, does the work).
3. **A raw `/`-prefixed prompt with no boundary still reads working** — pins the
   deliberate NON-exclusion of raw slash text.
4. **An AUTO boundary mid-turn reads working**: UserPrompt then
   `"Context compacted (auto)"` boundary, no TurnEnd — pins the manual-only scoping.
5. **Flush on manual boundary**: stranded Pending WhenIdle message; manual boundary
   processed through the runtime → message delivered; and with an EMPTY queue no
   Finished event is published (guards the narrow-flush decision).
6. `CompactionRecoveryTests`: recovery note enqueued at a manual boundary with the full
   record set present is delivered immediately, not stranded.
7. Existing `Session_with_compact_boundary_after_last_turn_end_reads_idle` stays green
   (idle before, still idle — now via the end rank rather than mere exclusion).

### Runner — `tests/Antiphon.SessionRunner.Tests/TranscriptWorkingStateTests`

File-order variants of server cases 1–4 (the file order differs from arrival order:
raw prompt, boundary, continuation, wrapper, stdout). Case 1 is red today; case 2 is
the one that proves the runner needs the continuation exclusion (no timestamp override
exists to save it).

### Client — `client/src/features/agents/SessionTranscriptPanel.test.tsx`

Mirror cases 1–4 against the exported `isWorking()`, using DTO shapes with the real
texts and timestamps.

### Canary — extend `ClaudeCompactionCanaryTests` (headed, capture-then-pin)

This is compaction's surface, so it belongs here, not in
`ClaudeLocalCommandCanaryTests` (cross-reference it — the 2026-07-31 canary pinned the
wrapper records and this card is the missed sibling shape). New test: run
`/compact <args>` (the delivery-path shape — args are what produced the raw record in
the live miss), then capture and pin a full fixture
(`Fixtures/compact-full-manual.jsonl`) asserting:

- a plain `type=user` record with the literal typed `/compact …` text, NO
  `isCompactSummary`, NO `isMeta` (the raw record — shape 1);
- `type=system, subtype=compact_boundary, compactMetadata.trigger="manual"`;
- a `type=user` record with `isCompactSummary: true` whose text starts with the pinned
  continuation prefix (pins BOTH the structural flag and the exact wording);
- the `isMeta` caveat, `<command-name>` and `<local-command-stdout>` records;
- **NO record carrying a stop_reason after the raw prompt** — the "no TurnEnd is ever
  coming" fact the whole card rests on.

Also capture whether a bare `/compact` (no args) writes the raw record — the existing
bare-compact canary test can assert-and-report rather than guess.

### fakeclaude — `src/Antiphon.FakeClaude/Program.cs` + `FakeClaudeContractTests`

fakeclaude currently matches only bare `text == "/compact"` and emits a lone boundary
line. Extend to the measured shape, always-on (this is transcript-shape modeling, not
an input-loss behavior, so no opt-in env var is warranted):

- match `/compact` with arguments (`StartsWith("/compact")` on the submitted text);
- manual path emits the FULL record set to the transcript: raw user line (literal typed
  text), boundary with `trigger="manual"`, continuation line with
  `isCompactSummary: true` and the real prefix text, `isMeta` caveat, `<command-name>`
  and `<local-command-stdout>` lines;
- the auto path (`ANTIPHON_FAKE_COMPACT_AFTER_TURNS`) emits `trigger="auto"` + the
  continuation line only (no command records — nothing was typed);
- pin the emitted sets in `FakeClaudeContractTests`, keeping the fixture comment chain
  (`compact-full-manual.jsonl` becomes the shared source of truth alongside
  `compact-boundary.jsonl`).

## 5. CARD-0029 disposition

- **Lead 1 (a fork after `/compact`, unfollowed) is REFUTED for this shape and
  superseded by this card.** The raw JSONL shows every post-compaction record in the
  SAME file (lines 40–50, one file, no fork), and all of them were ingested into the
  server rows (seqs 12–16) — ingestion was alive the whole time. `/clear`'s fork-follow
  coverage (`TranscriptTailerCompactionTests`) is unaffected and stays.
- **Lead 2 (the WhenIdle delivery never fired) is CONFIRMED and is exactly CARD-0041**:
  the brief for task 74f4de94 sat Pending behind a session misread as working.
- **Lead 3 (warm-reuse policy for just-failed/compacted agents) remains open** — policy
  question, untouched by this fix; CARD-0029 should be annotated, not closed.
- CARD-0029's mis-settle worry (a boundary triggering settlement against a stale
  report) is *defended against* here by the narrow flush (step 5) — settlement is
  deliberately NOT dispatched on a compaction boundary.

## 6. Rollout notes

- The fix is retroactive by construction: all three rules recompute over stored rows,
  so session e77fb0a7's badge flips to Idle on deploy with no data migration. Its
  stranded brief stays Pending on a dead session — correct; delivery is the relaunch
  path's decision.
- No DB schema change, no API change, no client/server protocol change.
- Residual risk accepted and documented: a manual `/compact` that FAILS (no boundary
  written) leaves the raw typed prompt as unmatched activity → working until the next
  real turn end. Rare, self-healing, and not worth matching raw slash text for.
- Order of work: step 1 first (shared constants), steps 2–4 in any order, step 5 after
  step 2, tests alongside each step; canary + fakeclaude last (canary is headed/opt-in
  and burns real model turns).
