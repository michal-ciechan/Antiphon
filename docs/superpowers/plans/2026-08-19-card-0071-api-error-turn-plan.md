# CARD-0071 — a turn killed by an API error: what is shipped, and the one consumer that was missed

- **Date**: 2026-08-19
- **Status**: Plan (planning only — no fix written in this pass)
- **Card**: CARD-0071 (*A turn killed by an API error is ingested as a successful turn, and its
  error text can be published as a channel reply*)
- **Precedent**: `docs/superpowers/specs/2026-08-17-usage-limit-and-api-error-resilience.md`
  (reconciles CARD-0022 / CARD-0071 / CARD-0072; §2 says CARD-0071 "closes when S2 + S3 land")
- **Sibling**: CARD-0072 owns detection carriage (S1) and the Transient retry ladder (S5);
  CARD-0022 owns the Wall class. Neither is planned here.
- **Evidence**: live database (`antiphon` on 17280), the live session-runner wire payload on 17204,
  the running server/runner binaries, and the raw JSONL of a real 529 stub.

> **This is a planning document only.** Nothing below was implemented in this pass.

---

## Verdict up front

| Question | Answer |
|---|---|
| Is CARD-0071's named hazard still live? | **No.** S2 (`b4fda1a`) and S3 (`254f2f9`) shipped 2026-08-17 on S1 (`3c9728f`). The error string cannot reach a channel reply, and cannot settle a task as done. |
| Does the spec's own close condition hold? | **Yes** — §2 says 0071 closes on S2+S3; both landed, both verified by reading the code, both test-pinned. |
| So is the card done? | **Not quite.** One consumer the spec's §0 named itself was never guarded: **`ReviewReplyDispatcher`**. Same defect, different surface. |
| Has any of this machinery ever run in production? | **No.** Zero `ApiErrorTurnDied` incidents; zero rows with `IsApiError = true`. Expected, not broken — see §2. |
| Is the "queue flush probably fires too" worry real? | **No**, and it is now re-verified against the current code (§1.4). |
| Does anything need inventing? | **No.** One slice, ~20 lines, in the shape of the guard already shipped next door. |

---

## 1. What is already shipped (verified by reading, not by trusting the commit titles)

Three commits, all 2026-08-17:

| Commit | Slice | Owner card |
|---|---|---|
| `3c9728f` | S1 — carry `IsApiError` / `ApiErrorClass` / `ApiErrorStatus` through the runner boundary | CARD-0072 |
| `b4fda1a` | S2 — an API-error stub is never published as a channel reply | **CARD-0071** |
| `254f2f9` | S3 — a turn killed by an API error never settles as done | **CARD-0071** |

### 1.1 Detection is structural and reaches the database

`TranscriptNormalizer.FromAssistant` (`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:96-98`)
reads the three **top-level** fields and stamps them on both the `AssistantText` and the `TurnEnd`
part. `TranscriptKinds.IsApiErrorStub(kind, isApiError)`
(`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:321`) is the predicate; it is
structural, never text-matched, exactly as the spec's D1 required.

Carriage is complete at every hop and I checked each one rather than assuming:

- `RunnerTranscriptEvent` (`SessionRunnerContracts.cs:122-124`) — additive-optional params.
- `SessionRunnerHttpClient.MapTranscript` (`:315-337`) — maps all three.
- `SessionRunnerTranscriptEvent` (`server/Application/Dtos/SessionRunnerDtos.cs:63-65`).
- `AgentSessionRuntime.PersistTranscriptAsync` (`:566-568`) — writes all three columns.
- Columns exist in the live database (`IsApiError boolean`, `ApiErrorClass varchar`,
  `ApiErrorStatus integer`).

Confirmed on the wire against the **live runner**, not just in source:

```
GET http://localhost:17204/sessions/70eb4c2d-.../transcript
  …"isApiError":false,"apiErrorClass":null,"apiErrorStatus":null…
```

That row is the benign `"No response requested."` synthetic — the exact negative the spec worried
about in §6.6 — and it correctly reports `false`, not `true`. The distinction the card asked for is
being made in production today.

### 1.2 The channel-reply guard (S2) — CARD-0071's actual ask

`ChannelReplyDispatcher` does **both** halves of spec §D5, and does them on **both** paths:

- **Main path**: `ExtractTurnResponseAsync` (`:695-707`) returns `ContainsApiErrorStub` alongside the
  text, *and* excludes stub rows from the join — belt and braces, so a later refactor of the withhold
  cannot let the error string ride out inside a body. The caller (`:221-229`) returns **before
  `SettleAsync`**, so the correlations stay **owed**, which is the whole point: CARD-0067 stamps
  `ChannelReplySettledAt` before the produce, so a published error would also have *cancelled* the
  genuine answer.
- **Whole-turn withhold, not line-stripping** — a mixed turn (real text, then a later API call dies)
  publishes nothing, per §D5.
- **Follow-up path**: `DispatchFollowUpAsync` (`:618-629`) withholds too, and deliberately does not
  advance the watermark.
- **Backstop intact**: nothing suppresses the `PendingReplyTtlMinutes` sweep, so an unanswered
  correlation still raises Critical `ChannelReplyLost` (21).

### 1.3 The settlement guard (S3)

`AgentTaskReplyService` (`:1125-1155`) strips stub rows from the report text *and* short-circuits on
`IsApiErrorStub(end.Kind, end.IsApiError)` the moment the turn is known to be this task's. The
failure arm (`:655-712`) marks the task **`Failed`** with a reason naming the class/status, **never**
stores the error text as `Result`, raises `AgentIncidentKind.ApiErrorTurnDied = 22` — Warning
normally, **Critical when the agent is channel-bound or the class is NeedsHuman** — and attaches
`git status --short` when the workspace is the shared checkout (spec §D6's rejected-auto-salvage
compromise).

### 1.4 The card's one UNVERIFIED item, re-verified

CARD-0071 asked whether the queue flush also fires on a dead turn. It does not:
`AgentSessionRuntime.IsTurnBoundary` (`:261-269`) requires `StopReason == "end_turn"` (or Grok's
`"cancelled"`); the stub carries `stop_sequence`. The hazard was always the `AssistantText` trigger
at `:219` plus the `AgentTaskDispatcher` sweep — both now guarded. **This item can be marked settled
on the card.**

### 1.5 Test coverage that already exists

- `ChannelReplyDurabilityTests` — `A_turn_killed_by_an_api_error_publishes_nothing_and_stays_owed`,
  `A_mixed_turn_with_real_text_beside_the_stub_is_withheld_whole`,
  `A_stub_in_the_trailing_window_withholds_the_follow_up`.
- `AgentTaskReplyIntegrationTests` — `a_turn_killed_by_an_api_error_fails_the_task_and_never_stores_the_error_text`,
  plus the Warning/Critical severity pair and `a_dirty_shared_checkout_is_named_in_the_api_error_incident`.
- `TranscriptNormalizerTests` — real-JSONL fixtures with the benign synthetic as a negative.
- `FakeClaudeContractTests.An_armed_api_error_kills_that_turn_only_and_writes_the_measured_stub_record`.

---

## 2. The machinery has never once fired — and that is expected, not broken

Measured against the live database today:

| Query | Result |
|---|---|
| `TranscriptEntries` where `IsApiError = true` | **0** |
| `TranscriptEntries` where `IsApiError IS NOT NULL` | **0** of 70 797 |
| `AgentIncidents` where `Kind = 22` (`ApiErrorTurnDied`) | **0** |

The all-null column looks alarming and is not. `GetBool(root, "isApiErrorMessage")` returns **null
when the property is absent**, which is every ordinary assistant record. Only two record shapes ever
produce a non-null value: an API-error stub (`true`) and the benign `"No response requested."`
synthetic (`false`). **Neither has been ingested since S1 landed** — the newest benign synthetic row
in the database is 2026-08-16 18:45Z, and the newest API-error text rows are:

| Session | Seq | Text | Ingested |
|---|---|---|---|
| `95240663` (delegate task `3f3159c3`) | 2, 5 | `API Error: 529 Overloaded…` | 2026-08-18 18:03Z, 18:06Z |
| `cefed08a` (orchestrator) | 2292, 2295 | `You've hit your session limit · resets 11:10pm (Europe/London)` | 2026-08-17 20:46Z, 20:47Z |

All four carry `IsApiError = NULL`, which is **spec §D1's deliberate no-retroactivity decision**
working as designed — plus, for the `95240663` pair, a deploy-lag effect worth recording (§5).

I verified the raw JSONL for `95240663` seq 2 rather than inferring it
(`~/.claude/projects/C--Antiphon-worktrees-card-task-3f3159c3/acbf608b-….jsonl`):

```
type = 'assistant', isApiErrorMessage = True, error = 'server_error', apiErrorStatus = 529
message.model = '<synthetic>', stop_reason = 'stop_sequence'
```

So the shape the whole design keys on is confirmed still current as of 2026-08-18, unchanged from the
2026-08-17 sweep. **Do not re-run the frequency sweep** — CARD-0072 owns it.

**What this means for the card**: S2 and S3 are correct by reading and by test, but have never
executed against a real stub in production. Nothing about that argues for more code; it argues for
saying so plainly when the card is closed.

---

## 3. The residual gap: `ReviewReplyDispatcher` was never guarded

The spec's §0 names **three** consumers that fan out from `AgentSessionRuntime`:

> …which fans out to `ChannelReplyDispatcher.OnTurnEndAsync`, **`ReviewReplyDispatcher`**, and
> `AgentTaskReplyService.OnTurnEndAsync`…

S2 guarded the first, S3 the third. The middle one was never revisited: the spec's §3 file list and
the S2/S3 slice rows do not mention it, and `grep IsApiErrorStub` finds no hit in that file.

`ReviewReplyDispatcher.ExtractTurnResponseAsync`
(`server/Application/Services/ReviewReplyDispatcher.cs:141-160`) is the pre-S2 `ChannelReplyDispatcher`
code, verbatim in shape: gather every `AssistantText` between the prompt and the next prompt, filter
only `IsNullOrWhiteSpace`, join. On a dead turn the stub's error string is the only text in that
window, so:

1. `"API Error: 529 Overloaded…"` is written as a **`ReviewComment` authored `Agent`**
   (`:170-178`) — the reviewer reads it as the agent's answer to their review comment.
2. The correlation is **consumed** by `TakeAllMatching` (`:163`), which *dequeues* from
   `_pending`. Unlike channel replies after CARD-0067, this correlation is **process-memory only and
   has no durable record**, so once eaten there is nothing to re-answer against and no TTL sweep to
   raise a loss incident. The real answer never arrives, and nothing anywhere records that.
3. The thread flips to `ReviewThreadStatus.AwaitingHuman` (`:180`) — a human is told it is their
   turn, on the strength of an error string.

That is CARD-0071's defect, one sentence at a time, on the review surface. It is **less severe** than
the channel case — no Telegram, no family chat, no Kafka produce — and **strictly worse in one
respect**: the channel path leaves its correlation owed with a Critical backstop, and this path
leaves nothing at all.

### 3.1 The slice

**S8 — the review-thread reply guard.** One file, mirroring the shipped S2 guard:

- `ExtractTurnResponseAsync` returns `ContainsApiErrorStub` alongside the text (add
  `IsApiError` to the projection — it is already on `TranscriptEntry`), and excludes stub rows from
  the join, both for the same belt-and-braces reason S2 gives.
- `DispatchAsync` returns **before `TakeAllMatching`** when the window contains a stub, logging a
  Warning naming the session and prompt sequence. The correlation stays **pending in `_pending`**, so
  the resumed turn's real answer lands on the thread by the existing `[Review #id]` tag match — the
  same "stay owed" posture S2 took, achieved by not dequeuing.
- The existing `PendingTtl` (60 min) `EvictStale` path remains the backstop and already logs a
  Warning on drop. **Do not** add an incident here in this slice: review threads are an in-app
  surface with the thread visibly stuck at its prior status, unlike a human waiting on Telegram.

**Tier**: sonnet. **Depends on**: S1 only (shipped). **Independent of** S4/S5/S6.

**Tests** (`ReviewReplyDispatcherTests`, or the file that covers this service today — check before
writing; the shared-Postgres rule applies, so scope every assertion to the rows the test made):

1. A stub-only turn adds **no** `ReviewComment`, leaves the thread status unchanged, and leaves
   `PendingCount(sessionId)` unchanged.
2. A mixed turn (real text then a stub) is withheld whole — same rule as S2, same reason.
3. The genuine answer on a **later** turn still lands on the thread (proving the correlation was
   preserved, not merely skipped).
4. A normal turn is unaffected.

---

## 4. Deliberately NOT in scope

- **Any retry or resume** (S4/S5/S6). CARD-0072 owns the Transient ladder; CARD-0022 owns the Wall
  class, `UsageLimitState`, dispatch pause and `AttentionKind.UsageLimitExhausted`. CARD-0071 is only
  ever "what the pipeline does with a dead turn it can already see", and that boundary is worth
  keeping sharp.
- **S3's defer-to-resume arm.** Today a stub-killed task **Fails** with a reason. That is spec §D6's
  correct "no resume coming" arm while S5 is unlanded, and it activates on its own when S5 lands. No
  change now.
- **A text fallback for the four legacy rows.** Spec §D1 priced this and rejected it; the four rows
  sit in two non-channel-bound sessions and one dead delegate worktree. Nothing to gain.
- **S7 transcript rendering** (stub rows as an error chip). Cosmetic, optional, and listed on the
  spec as such. Not a CARD-0071 blocker.
- **`stop_sequence` archaeology** (spec §6.2). Detection keys on `isApiErrorMessage`, so it is moot;
  worth a one-off query only if someone ever proposes leaning on `StopReason`.

---

## 5. One deploy-lag observation worth carrying forward

Not a CARD-0071 defect, but it directly conditions every runner-side field this family adds.

The running session-runner is the always-on daemon, launched from a **built exe** and *adopted* by
the AppHost rather than rebuilt by it. The live process (pid 43424) started **2026-08-19 03:43** from
a binary written the same minute. So:

- It **does** carry S1 (`3c9728f`, 2026-08-17 19:50) — confirmed on the wire in §1.1.
- It does **not** carry CARD-0082 S1's `Model` field (`2e8106c`, 2026-08-19 07:28) — `Model` is null
  on all 782 rows ingested since the **server** was rebuilt at 22:47, and the runner's own payload
  reports `model: null` for rows it tailed minutes ago.

`Model` being inert is CARD-0082's business, not this card's. The transferable point is the rule:
**a runner-side transcript field is inert until the always-on runner is restarted**, and a server
rebuild does not do that. The `95240663` stub on 2026-08-18 is the concrete cost — it landed while an
older runner was live and was ingested with the fields dropped, which is why the only real 529 since
S1 shipped is invisible to the machinery built for it. Anyone verifying a future detection slice
should check the runner's build time first, and `scripts/restart-session-runner.ps1` (which no longer
kills sessions, per the pty-host split) is the way to pick it up.

---

## 6. Verification for the slice

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c71/ \
  --treenode-filter "/*/*/ReviewReplyDispatcherTests/*"
```

Then the two suites that pin the shipped guards, to prove the change did not disturb them:

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c71/ \
  --treenode-filter "/*/*/ChannelReplyDurabilityTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c71/ \
  --treenode-filter "/*/*/AgentTaskReplyIntegrationTests/*"
```

Trailing **forward** slash on `OutputPath`, and delete the `bin-c71/` directories afterwards
(`Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-c71 | Remove-Item -Recurse -Force`).

No migration. No contract change. No runner restart required — S8 is server-side only, on fields the
running runner already sends.

---

## 7. Recommended commit line

```
fix(review): CARD-0071 - an API-error stub is never posted as a review-thread reply
```
