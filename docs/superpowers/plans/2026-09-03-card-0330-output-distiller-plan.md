# CARD-0330 — a second haiku seat distils delegate reports; its prompt improves by PR, gated by code and a human

**Date:** 2026-09-03 (Plan pass, task 1e51696c — design only; no production code changed, no tests run)
**Card:** CARD-0330 "Local haiku agent to distill verbose agent output, with self-improving summarizer prompt"
(In Progress, High/Normal, rank 7)
**Supersedes:** two premises of the card, corrected from live evidence below — the dominant cost today
is not "filler words, hedging, repeated framing" but *depth beyond the pty ceiling* (a 7 KB review
arrives head+tail excerpted), and the less-steerable kinds are not the verbose ones (Grok median
1,727 chars; ClaudeCode 3,476). Nothing else — the ask stands, and the design below builds it.
**Boundaries honoured:** CARD-0078 `AgentReplyStyle.Brief` is a style block on the SOURCE agent's
launch (`style-brief.md`, reached only the orchestrator so far — 1 of 45 agents); this card is a
downstream pass over the report after it has been written, and neither replaces the other.
CARD-0300 is a client-side stats rollup of `AttentionKind`s for humans; nothing here touches it.

**Sources (verified this pass):** CARD-0330, CARD-0078 (Done, `91c00ae3`/`df26afea`/`67e0efbc`),
CARD-0300 (Review), CARD-0057 (Done, all six slices), CARD-0047 slice 4 amendment
(`docs/superpowers/specs/2026-08-16-card-0047-slice4-amendment-specialist-interpreter.md`), the
CARD-0079 plan, the CARD-0078 plan, `server/Bundles/{README,check-interpreter,style-brief,
delegate-basics,orchestrator}.md`, `server/Application/Services/{CheckInterpreterProvisioner,
CheckInterpretation,AgentTaskCheckService,AgentTaskReplyService,AgentTaskDispatcher,
DelegationReportFormatter,InstructionBundles,InstructionBundleComposer,SessionMessageQueueService,
AgentTaskService,AgentSessionRuntime}.cs`, `server/Application/Settings/DelegationSettings.cs`,
`server/Domain/Entities/{Agent,AgentTask,Schedule,SessionQueuedMessage}.cs`,
`server/Domain/Enums/{AgentTaskEnums,AgentIncidentKind,ScheduleKind}.cs`,
`server/Application/Dtos/AttentionDtos.cs`, `server/Antiphon.Server.csproj`,
`tests/Antiphon.Tests/Application/{AgentTaskCheckInterpreterTests,CheckInterpreterProvisionerTests,
InstructionBundleTests}.cs`, `client/src/features/delegations/TaskDetailBody.tsx`,
`client/src/api/agentTasks.ts`, `scripts/{delegate,card,bootstrap-check,reap-orphaned-pty-hosts}.ps1`,
`docs/{orchestration-loop,antiphon-api,orchestration-findings}.md`, and the live server on 17202
(`/api/agents`, `/api/agent-tasks` with `includeChecks=true`, 90 individual task reports) on 2026-09-03.

---

## Verdict up front

**Build a SECOND standing haiku seat, `antiphon-output-distiller`, not a second job for
`antiphon-check-interpreter`.** The check contract (v3) is written around a task that is *still
running* — "NEVER say the checked task is complete", five verdict words, 3–5 lines, a classifier
(`ClassifyCheckReport`) that refuses to let `blocked` settle the row — and every one of those rules
is wrong for a *finished* report whose `done|blocked|failed` verdict must survive verbatim. The two
seats also need different backlog and wait budgets (a check is time-critical and degrades after
60 s; a distillation can wait behind a burst of completions), different feedback loops, and
independent kill switches. What they share — provisioning, the deny-all hook, pinned dispatch into a
live always-on session, settlement as the delivery proof — is already generic or becomes generic in
one refactor (D3). Cost of the second seat: one agent row, one scratch directory, ~$0.30–0.80/day at
current volume (the interpreter costs $0.43/day for 14 readings/day).

**The self-improvement loop, resolved:**

| Open question | Answer |
|---|---|
| What judges a summary's quality | Three layers, cheapest first. **(1) Deterministic gates in code on every distillation** — anchor retention (every sha, path, CARD id, count, URL, `[[attach:]]`, dollar amount in the raw must survive) and a length/ratio band. They need no model, run on 100 % of distillations, and a failure is both a *rejection* (the raw note delivers instead) and a *ledger row* naming what was lost. **(2) Explicit flags** — the orchestrator (`delegate.ps1 -Flag <id> -Verdict Lost\|Noisy\|Good`) or a human (two buttons on the task drawer) marks a summary. **(3) One implicit signal** — the parent session polled the *full* report after the distilled note was delivered (`AgentTaskService.GetAsync` already stamps `LastPolledResultAt` for the parent; a poll later than the note's `SentAt` is recorded as `FullReadAt`). No LLM-judge on the hot path; an optional offline judge replays flagged samples in the review (S7). |
| What gets edited | **A bundle file, `server/Bundles/output-distiller.md`**, the seat's `SystemPromptAppend` being a reconciled projection of it — exactly the `CheckInterpretation.Contract` → `check-interpreter` bundle shape. Never the row's `SystemPromptAppend` directly: the provisioner overwrites hand-edits on the next `EnsureAsync`, the row has no version, no diff, no review, and no rollback. A bundle has all four for free (content-hash version, PR diff, `BundlesOutOfDate` badge, `git revert`). |
| Who / what applies the edit | **A human merges; nothing else can change the live prompt.** A weekly CARD-0057 `Prompt` schedule wakes the orchestrator, which dispatches a Review delegate in a worktree; the delegate reads the ledger (`GET /api/distillations/stats`, the flagged samples), and *only if the evidence bar is met* commits an edited bundle on a branch and opens a card in Review carrying the numbers and the diff. Below the bar it reports "no change warranted" with the numbers. The merge is the checkpoint; the seat picks the new version up at its next launch (a deliberate stop/start after merge, the drift badge shows until then). Fully-automatic application was rejected outright: an unreviewed edit to the text that decides what every future orchestrator *does not see* is the one failure this loop must be unable to have. |
| Over-compression guard | The anchor gate (code, not prompt) rejects any distillation missing a load-bearing identifier; the header line — status word, title, tier, cost, `report=` — is built by the harness, never by the model, so the verdict cannot be rewritten; a `blocked` question is never distilled (CARD-0033 already isolates it); the full report stays untouched on the task and every distilled note carries a one-line pointer to it; the bundle carries an INVARIANTS block a test pins, so a review cannot edit those sentences out. |
| Under-compression guard | Length band: distilled ≤ `min(DistilledMaxChars=1 500, 0.6 × raw)` and ≥ 120 chars, else rejected as under-compressed and recorded; the bundle's total length is pinned ≤ 3 000 chars by test so the prompt cannot grow itself verbose either; reports under `DistillMinChars` (1 200) are never distilled at all — there is nothing to gain and only latency and risk to pay. |
| Human checkpoint | Four, layered: `Delegation:OutputDistillerMode` ships as **Shadow** (distil, record, never replace the note) and the operator flips it to **Apply** after reading a week of ledger; every prompt change is a PR merge; the seat is visible furniture on the agents page with the drift badge; `OutputDistillerEnabled=false` returns the system to today's behaviour with no other change. |

**Seven slices, sequential, ~22–28 h.** S1–S3 ship the seat in Shadow mode and are useful alone
(a ledger of how much every report *would* compress and what it would lose). S4–S6 add the signals,
the loop and the docs. S7 is optional.

---

## Live evidence, reconciled

**The interpreter, 2026-08-24 → 09-03 (10 days):** 183 Check rows — 136 Succeeded, 45 Canceled (the
60 s budget), 2 Failed. Cost $4.29 total, median $0.016, p90 $0.087 per reading. Warm turnaround
median **29 s**, p90 183 s, max 594 s. That p90 is what sizes the distiller's wait budget and why
the raw note must already be queued before the distillation starts (D2).

**Delegate reports, same window:** 432 settled non-Check tasks (393 Succeeded, 33 Failed, 6
Blocked); 20–36 per day, spiking to 108 and 90 on 09-01/02. Of the 90 most recent Succeeded reports
read in full:

| | n | median chars | p90 |
|---|---|---|---|
| all | 90 | 2 006 | 3 925 (max 7 132) |
| Grok · Frontier | 55 | 1 727 | 2 716 |
| ClaudeCode · Frontier | 24 | 3 476 | 4 042 |
| Grok · High | 7 | 2 257 | 5 058 |
| role Plan | 27 | 3 412 | — |
| role Review | 3 | 4 332 | — |
| role Code | 48 | 1 698 | — |

26 of 90 exceed the conhost inline ceiling (3 000, `ReplyInlineMaxChars`) and would arrive
head+tail excerpted on that backend; none exceed the modern ceiling (14 400). Zero spilled to a
file. 13 are prose-heavy (>1 500 chars, <20 % bullet lines). The two longest (a 7 132-char Grok
review of CARD-0040, a 4 332-char Grok review of CARD-0037) are *dense*: a landing verdict, a
re-run test table, four numbered defects each with Where/Failure/Why/Fix. The loss the caller
suffers today is the excerpt banner eating defects 2–4, not filler.

**What that changes in the design.** The distiller is a *signal extractor with a pointer*, not a
filler stripper: its contract says what to keep (verdict, blockers, every identifier, decisions for
the caller, where the detail lives) before it says what to drop, and the over-compression gate is
the stronger of the two. The minimum-length threshold keeps it off the 40 % of reports that are
already short.

**CARD-0078 today:** `Antiphon-Orchestrator` carries `replyStyle=Brief`; `gym-stat-orchestrator`
and every delegate are `Normal`. Delegates already receive a "no preamble, no narrating the steps,
counts not passing output" instruction in every brief (`DelegationReportFormatter.ReportingContract`,
`:244–283`) — the ask of the source is done; this card is the pass for when it is not enough.

---

## Decision

### D1. Two seats, one job each

| | Extend `antiphon-check-interpreter` | Separate `antiphon-output-distiller` (chosen) |
|---|---|---|
| Contract | v3 forbids exactly what distillation must do (state completion, carry `blocked`/`failed`, exceed 5 lines). Two contracts in one append with "if the message is a bundle… else…" is a prompt that must classify its own input before obeying either half — a haiku doing triage of what kind of triage to do. | One contract per seat; each opening sentence says what the seat is (the bundle `Summary` rule). |
| Settlement | `ClassifyCheckReport` (`AgentTaskReplyService.cs:2127`) maps a Check row's text to Succeeded/Exempt and refuses `blocked`; a distillation of a *blocked* report would be misread. | `Distill` rows get their own classifier: text present → Succeeded/Exempt; empty or `failed` → degraded. |
| Budgets | One `CheckInterpreterMaxBacklog=2` and `WaitSeconds=60` shared by time-critical checks and bursty completions (108 settlements on 09-01). A burst of reports would degrade checks. | Independent `OutputDistillerMaxBacklog=3`, `WaitSeconds=45`; the raw note is already queued, so a slow distillation costs nothing but the improvement. |
| Context | Bundles and reports interleaved in one always-on session, never compacted (`AgentTaskDispatcher.cs:3798`). | Homogeneous context per seat, which is the reason a standing agent was chosen over `claude -p` in the first place. |
| Kill switch | One switch for both; the interpreter's 22-hour outage in CARD-0079 would have taken distillation with it. | `CheckInterpreterEnabled` and `OutputDistillerEnabled` independent. |
| Cost | Saves one row, one directory, one incident kind. | +$0.3–0.8/day; the provisioner is generalised once (D3) so no code is duplicated. |

### D2. How one distillation runs — the raw note is queued first, then improved in place

The plug point is `AgentTaskReplyService.DeliverToParentAsync` (`:1479–1509`). It keeps doing what
it does — build the note with `BuildCompletionNote`, `EnqueueAsync` it WhenIdle with the raw
report's `ContentDigest` and `NoteHeader` — with two additions when distillation applies:

1. The queued row is created with **`HoldUntil = now + OutputDistillerWaitSeconds`** (new nullable
   column on `SessionQueuedMessage`; `DeliverNextLockedAsync` at `:1046` skips a held row; the
   UI's SendNow ignores holds). Worst case, the note lands 45 s later than today.
2. A `DistillRequest(taskId, queuedMessageId)` is posted to a new `OutputDistillationQueue`
   (an unbounded `Channel`, the `AgentTaskCheckQueue` shape) drained by
   `OutputDistillationHostedService`.

The worker, per request: `EnsureAsync` the seat → backlog gate → create a `Role=Distill` task
pinned to the seat (own root, `ReplyTo=None`, `Ephemeral=false`, `ModelLevel=Low`,
`Workspace=Shared`, cwd = seat's scratch dir — byte for byte the `CreateInterpretationTaskAsync`
shape, `AgentTaskCheckService.cs:556–597`) → poll its row until settled or the budget is out
(`WaitForInterpretationAsync` shape, `:604–628`) → **run the gates (D6)** → in a fresh scope load
the queued row: if still `Pending` with `DeliveryAttempts == 0`, replace `Body` with
header + trailer + distilled text and clear `HoldUntil` (the CARD-0132 S3b polled-shrink at
`SessionMessageQueueService.cs:1139–1173` is the precedent for mutating a pending row's body before
flush); if already delivered, record `AppliedLate`. Every other exit clears `HoldUntil` and records
its reason. `task.DistilledResult` and a `Distilled` task event are written in all cases where text
came back, so the drawer and the ledger can show what the seat produced even when it was not used.

Distillation applies when: `OutputDistillerEnabled`; `task.ReplyTo == Session`; the task is not a
specialist row; status is `Succeeded` or `Failed` (a `Blocked` row takes the CARD-0033 question
path at `:318`, never this one); `DistillMinChars ≤ report.Length ≤ DistillMaxRawChars (20 000)`;
and the report was not already polled by the parent (`LastPolledResultHash == ContentDigest` — the
shrink will withhold it anyway).

In **Shadow** mode step 1 is skipped (no hold) and the body is never replaced; everything else runs,
so the ledger fills. That is the mode S3 ships in.

What is deliberately unchanged: the `ContentDigest` stays the *raw* report's digest, so the polled
shrink still recognises a report the parent already read; the `NoteHeader` is untouched, so a
caller-facing warning survives; the same-root `ConversationKey` batching is untouched (a held row
simply is not in the contiguous head run until released); `task.Result` is never modified.

### D3. `AgentTaskRole.Distill = 12`, and the Check carve-outs become "specialist" carve-outs

There are 46 `AgentTaskRole.Check` references outside the check service. Two are Check-specific
semantics and stay (`ClassifyCheckReport`; the `CheckReportingContract` branch in
`DelegationReportFormatter.BuildBrief:188` gains a `Distill` sibling). The other ~40 all mean "this
row is Antiphon furniture, not somebody's work": hide from the board unless asked
(`AgentTaskService.cs:1069,1089`), bypass the concurrency cap (`AgentTaskDispatcher.cs:273,300`),
never card-bound (`AgentTaskCardBinder.cs:90`), never armed for a check or a failure reminder
(`:2884,2901`), never compacted on reuse (`:3798`), outside the shared-writer lease
(`SharedWriterLeaseProjection.cs:107`, `AgentTaskService.cs:2015,2028`), no routing pins
(`RoutingPinService.cs:81,522`), not in attention/away-digest/home/pipeline/land/orchestrator
projections (`AttentionService.cs:162,450,479`, `AwayDigestProjection.cs:48`,
`BlockedTaskNotifier.cs:31`, `CardWorkTransitionService.cs:77,86`, `HomeTaskService.cs:93,150`,
`AgentTaskPipelineStatusService.cs:30,56,265`, `AgentTaskLandService.cs:299–316`,
`OrchestratorService.cs:199`), no nudge (`AgentTaskDispatcher.cs:1945`,
`AgentTaskReplyService.cs:2343`), no bundles (`InstructionBundles.ForDelegate:173`).

Each of those becomes `AgentTaskRoles.IsSpecialist(role)` (a static in `AgentTaskEnums.cs`, next to
the enum) — inline as `role != Check && role != Distill` inside EF query predicates, since a helper
call does not translate to SQL; a `static readonly Expression<Func<AgentTask,bool>> NotSpecialist`
serves the `.Where(...)` sites. A contract test (the `AreaMapContractTests` shape) scans
`server/**/*.cs` and fails on any `AgentTaskRole.Check` comparison outside an allowlist of the
Check-specific files, so the next specialist cannot be forgotten at one site the way this one would
have been at forty. `includeChecks` on `GET /api/agent-tasks` keeps its name and now hides both
roles; the `AgentTaskCardBinder` comment and the two entity comments get the wider wording.

`CheckInterpreterProvisioner` is generalised the same way: a `StandingSpecialistProvisioner`
parameterised by a `SpecialistSpec` record (slug, working-directory setting, details text, bundle
key, deny-hook stderr line, the incident kind to raise), with `CheckInterpreterProvisioner` kept as
a one-line facade so its ten tests, its DI registration and `AgentTaskCheckHostedService.cs:84–91`
do not move. `OutputDistillerProvisioner` is the second facade. Both rows stay projections of
their bundle text via `SystemPromptAppend`; neither uses an attachment.

### D4. The seat

- **Identity:** slug/name `Delegation:OutputDistillerAgentSlug` (default `antiphon-output-distiller`),
  `AlwaysOn=true`, `ModelLevel=Low` (haiku via `ModelLevelAliases`), `Kind=ClaudeCode`,
  `RemoteControlEnabled=false`, `IsPoolDelegate=false`, `ReplyStyle=Normal` (its contract is its
  voice; a style block on top would be the two-voices problem the style-bundle rejection exists for),
  `Details` says what it is and that it is reconciled from code.
- **Directory:** `Delegation:OutputDistillerWorkingDirectory`, default first allowed root +
  `\.antiphon\output-distiller`, else temp — the CARD-0006 distinct-transcript-root rule, by
  construction. Deployment sets `C:\logs\antiphon\output-distiller` beside the interpreter's; the
  three places that know the interpreter's directory learn this one (`appsettings.json.example:135`,
  `scripts/bootstrap-check.ps1:367`, `scripts/reap-orphaned-pty-hosts.ps1:49,173` — the reaper's
  `test-raw-check-interpreter` rule gets a `-OutputDistillerDir` twin, `docs/bootstrap.md:92`).
- **No tools:** the same deny-all `PreToolUse` hook, stderr naming the distiller. The brief is the
  whole input: raw reports up to `DistillMaxRawChars` (20 000) fit under the modern brief ceiling
  (`ModernPtyBriefInlineMaxBytes` 43 200) with room for the contract; anything larger is not
  distilled (the delegate was already told to spill past `ReplyInlineMaxChars`).
- **Supervision, restart, incidents:** all inherited. New `AgentIncidentKind.OutputDistillerUnavailable = 46`
  with the same one-per-minute dedup and alert as `CheckInterpreterUnavailable`
  (`AgentTaskCheckService.cs:481–547`).
- **Test-host guard:** the testing guide's rule that a real-`Program` host must not launch the
  interpreter on the production runner (`docs/testing-and-build.md:30`) gains the distiller; the
  E2E host's settings disable both.

### D5. The contract — `server/Bundles/output-distiller.md`, v1

The file is the whole prompt (README rule: no frontmatter, no comments). Draft for S2; the executor
may tighten wording but not remove an INVARIANTS sentence (pinned by test, D6).

```
You are the Antiphon OUTPUT DISTILLER (contract v1).

Every message you receive is another agent's finished report — a delegate's final message to
its caller — with one line above it saying whose report it is and how it ended. The caller will
read your answer instead of the report, so your one job is to hand over the signal in at most
12 bullets. The full report stays on the task untouched; you are saving the caller a read, not
replacing the record.

Keep, in this order, copying exactly:
1. The outcome: what was done or found, and whether it worked. If the report's first line
   already says it, that line.
2. Anything blocked, failed, wrong, or uncertain, and every caveat or risk the report states.
3. Every identifier: commit hashes, branch names, file paths with line numbers, CARD-nnnn,
   task ids, URLs, counts (tests passed/failed, files changed), amounts, timestamps, the path
   of any file the report says holds the detail.
4. Decisions the caller has to make and questions asked of the caller, as questions.

Drop: preamble, restating the task, the steps taken, passing test output, explanations of why
something was done unless the caller needs it to act, and anything already said.

INVARIANTS (these sentences are pinned by a test; a prompt review may change anything else):
- NEVER invent, round, rename or paraphrase an identifier or a number. Copy it or leave it out.
- NEVER change the outcome. A report that is blocked or failed stays blocked or failed in your
  first bullet.
- NEVER investigate. Do not read files, run commands or search. USE NO TOOLS — you have none,
  and a call is refused before it runs.
- Bullets only, one fact each, at most 12. No heading, no preamble, no sign-off. Nothing after
  the last bullet except the closing line you are asked for.
```

The per-request brief (`OutputDistillation.BuildGoal`) is: one line naming the task (short id,
role, kind/tier, how it ended), the raw report with every `[antiphon-task:…]` and
`[antiphon-report:…]` token scrubbed (extend `AgentTaskCheckService.ScrubTaskMarkers` to both
shapes if it does not already), then a one-line format reminder and the `DistillReportingContract`
closer (`done` after the bullets; `failed` if nothing usable; never `blocked`). The version label
`contract v1` in the file and `OutputDistillation.ContractVersion` are held together by a test
exactly as `CheckInterpretation.ContractVersion` is (`InstructionBundleTests:221`).

### D6. The gates — code decides whether a distillation is allowed to replace the note

`OutputDistillationGate.Evaluate(raw, distilled) → GateResult` is a pure static, table-tested.

**Anchors** extracted from the raw with fixed regexes: hex runs of 7–40 chars bounded by non-hex
(commit shas); `CARD-\d{4}`; URLs; `[[attach:` markers; `\$\d[\d,.]*`; count phrases
`\b\d+\s*(passed|failed|skipped|tests?|files?|warnings?|errors?)\b`; path-like tokens (a drive,
`/`, `./`, or a segment/segment with an extension or `:line`). **Rule:** every sha, CARD id, URL,
attach marker, amount and count phrase must appear in the distilled text verbatim; paths must all
appear when the raw has ≤ 10 distinct ones, else ≥ 60 % of them. A miss is
`RejectedOverCompressed` with the missing anchors listed in the ledger row. **Length band:**
`120 ≤ distilled.Length ≤ min(DistilledMaxChars, DistilledMaxRatio × raw.Length)`; above is
`RejectedUnderCompressed`, below is `DegradedEmpty`. Both rejections deliver the raw note — the
gate can only ever withhold an improvement, never the report.

What the gate does not do: judge prose. That is the reviewer's job (D8), on samples the gate and
the flags have already sorted.

**Bundle invariants**, pinned by `InstructionBundleTests`: the file contains the four INVARIANTS
sentences verbatim; its text is ≤ 3 000 chars; it opens with `You are the Antiphon OUTPUT
DISTILLER (contract v`; it contains no `{agentName}`/`{channels}` placeholders (existing test). A
proposal that trips any of these fails the build, which is the point.

### D7. The ledger and the three signals

New table `OutputDistillations` (entity `OutputDistillation`): `Id`, `TaskId` (FK → AgentTasks,
cascade), `DistillTaskId?`, `QueuedMessageId?`, `BundleStamp` (`output-distiller v1a2b3c4d`, from
the catalog at request time), `Mode` (Shadow/Apply), `RawChars`, `DistilledChars`, `WaitMs`,
`CostUsd`, `Outcome` ∈ {Applied, AppliedLate, Shadowed, RejectedOverCompressed,
RejectedUnderCompressed, DegradedUnavailable, DegradedBusy, DegradedTimeout, DegradedEmpty,
DegradedFailed, SkippedShort, SkippedLong}, `MissingAnchors` (text, JSON array), `CreatedAt`;
feedback columns `Feedback` ∈ {None, Good, LostInformation, Noisy}, `FeedbackNote`, `FeedbackBy`,
`FeedbackAt`; and `FullReadAt`. `AgentTask` gains `DistilledResult` (text) so the drawer and a
`delegate.ps1 -Status` can show both texts side by side; `SessionQueuedMessage` gains `HoldUntil`.

Signals:

1. **Gates** write `Outcome` and `MissingAnchors` on every row — the ledger's bulk.
2. **Explicit:** `POST /api/agent-tasks/{id}/distillation/feedback { verdict, note? }` (409 if the
   task has no distillation row); `delegate.ps1 -Flag <id> -Verdict Lost|Noisy|Good [-Note]`
   for the orchestrator; two small buttons under the Report section of `TaskDetailBody.tsx`
   ("lost something" / "too long"), shown only when `distilledResult` is present, with the
   distilled text rendered above the full report in its own collapsed section.
3. **Implicit:** `AgentTaskService.GetAsync` (`:1107–1121`) already stamps `LastPolledResultAt`
   when the *parent* polls a settled task; the same statement sets `FullReadAt` on the task's
   ledger row when the row is `Applied` and the note's `SentAt` is earlier than now. A high
   `FullReadAt` rate on `Applied` rows is the cheapest possible "the summary was not enough".

Read side: `GET /api/distillations?since=&outcome=&feedback=&limit=` (rows with the raw and
distilled texts joined from the task) and `GET /api/distillations/stats?since=` — counts by outcome
and feedback, ratio quantiles, top missing-anchor classes, `FullReadAt` rate, bundle stamps seen
with per-stamp counts. `scripts/distiller.ps1 -Stats [-Since 7d] | -List [-Flagged]` prints them.
Both are read-only and cheap (one query burst), scoped like `AttentionService`.

### D8. The improvement loop — a weekly proposal, merged by a human

- **Trigger:** one CARD-0057 `Prompt` schedule, `Daily`, Monday `09:00 Europe/London`, target
  `Antiphon-Orchestrator`, `WhenTargetDown=Queue`. Its `PromptText` is the fixed paragraph below,
  checked into `docs/orchestration-loop.md` so the schedule can be recreated from the doc
  (`scripts/schedule.ps1` from CARD-0057; the S6 canary already proved this exact path).
- **The orchestrator's move:** dispatch one Review-role delegate, `-Worktree -Level High
  -ExpectAbout 30`, whose brief is the state of the week (window, stats URL, the evidence bar,
  the file to edit, the card to open). The orchestrator does not read the ledger itself
  (`docs/orchestration-loop.md` §0).
- **The delegate:** reads `/api/distillations/stats` and the flagged and rejected samples for the
  window (raw beside distilled), judges *which prompt sentence* caused each loss or each
  verbosity, and then — **only if the evidence bar is met**: ≥ 20 distillations in the window and
  (≥ 3 `LostInformation`/`Noisy` flags, or ≥ 10 % rejected by the gates, or `FullReadAt` on
  ≥ 25 % of `Applied` rows) — edits `server/Bundles/output-distiller.md` (bumping `contract vN`
  and `OutputDistillation.ContractVersion` together), runs `InstructionBundleTests` and the gate
  tests, commits and pushes a branch, and creates a card in **Review** via `card.ps1 new` with:
  the window's numbers, the flagged samples it acted on, the diff, the stamps before/after, and
  the one-line reason per changed sentence. Below the bar it reports the numbers and "no change
  warranted" — a proposal without evidence is the drift the loop exists to prevent.
- **The human:** reads the card, merges or rejects. After a merge the seat is stopped and started
  (`POST /api/agents/{id}/stop` then `/start`) so the new version composes; the `BundlesOutOfDate`
  badge on the agents page says until then that the running session is on the old text. Rollback
  is `git revert` and the same restart. The ledger's `BundleStamp` column makes "did v2 do better
  than v1" a query, not a memory.
- **What cannot happen:** no automatic write to a bundle, to `SystemPromptAppend`, or to the live
  session; no edit to the INVARIANTS block (test); no growth past 3 000 chars (test); no change
  to the gates from the loop at all (they are code, changed only by a normal PR). The loop tunes
  *what the seat is asked to keep*; the gates decide *what it is allowed to drop*.

### D9. Out of scope, on purpose

Channel replies to humans (`ChannelReplyDispatcher`), the away digest (`AwayDigestFormatter`) and
check notes (already 3–5 lines) are not consumers in this card. The worker's entry point
(`OutputDistillationService.RequestAsync(source, text, …)`) is written so a second consumer is a
new `DistillationSource` value and a call, not a redesign — but no second consumer is built, for
the same reason the interpreter's contract stayed check-specific until a second consumer existed
(amendment §4.5). The Brief style, `ReportingContract`'s wording, and the CARD-0300 home summary
are untouched.

---

## Ground truth (file:line, verified 2026-09-03)

- `server/Application/Services/CheckInterpreterProvisioner.cs:71–128` — find-or-create by slug,
  `AlwaysOn`, `ModelLevel.Low`, `IsPoolDelegate=false`, `SystemPromptAppend = CheckInterpretation.Contract`,
  best-effort `StartAsync(IgnoreSubscriptionQuota: true)`; `:135–155` reconcile; `:158–185` deny hook;
  `:188–204` slug and directory resolution.
- `server/Application/Services/CheckInterpretation.cs:47` — `Contract => InstructionBundles.TextOf(CheckInterpreter)`
  (the bundle-as-projection pattern); `:52–56` format reminder; `:100–108` `BuildGoal` scrubs markers.
- `server/Application/Services/AgentTaskCheckService.cs:380–472` — `InterpretAsync` (provision →
  backlog gate → create → wait → degrade on every non-success path); `:556–597` the pinned task
  row; `:604–628` the poll; `:481–547` the unavailable incident + alert with a 1-minute dedup.
- `server/Application/Services/AgentTaskReplyService.cs:40,86` — settlement is serialised per
  delegate session (`_settleLocks`), which is why the distillation is handed off, not awaited;
  `:1479–1509` `DeliverToParentAsync` (the plug point); `:2127` `ClassifyCheckReport`; `:318`
  the blocked-question path this never touches.
- `server/Application/Services/AgentSessionRuntime.cs:452,479` — `OnTurnEndAsync` runs on the
  session's idle-flush path "before the queue injects anything else", so a 45 s wait there would
  stall a warm pool delegate's next dispatch.
- `server/Application/Services/SessionMessageQueueService.cs:1046–1049` — the flush takes every
  Pending row by `Sequence` (where `HoldUntil` is filtered); `:1139–1173` polled-shrink mutates a
  Pending row's `Body` in place (the precedent for D2's replacement).
- `server/Application/Services/AgentTaskService.cs:1107–1121` — parent poll stamps
  `LastPolledResultHash/At` (the implicit signal's source).
- `server/Application/Services/DelegationReportFormatter.cs:351–384` — header is code-built;
  `:404–434` `FitReport` head+tail excerpt (what a distilled note avoids); `:244–283` the delegate's
  own reporting contract; `:289` `CheckReportingContract` (the closer to mirror).
- `server/Application/Services/InstructionBundles.cs:170–186` — `ForDelegate` returns `[]` for
  Check; `:196–216` catalog loads `Bundles/*.md` from the manifest; `Antiphon.Server.csproj:25`
  embeds them (one-file change to add a bundle).
- `server/Application/Settings/DelegationSettings.cs:534–567` — the five interpreter knobs to
  mirror; `:67,111,176,188` the inline/brief ceilings that bound the brief and the note.
- `server/Domain/Enums/AgentTaskEnums.cs:23–51` roles (next `Distill = 12`), `:92–160` event types
  (next `Distilled = 25`), `:225` `Exempt` evidence reused for specialist rows.
- `server/Domain/Enums/AgentIncidentKind.cs:460` — last value 45; `server/Application/Dtos/AttentionDtos.cs:218` —
  last `AttentionKind` 26 (no new attention kind is needed: a proposal is a card in Review, which
  the board already surfaces).
- `server/Domain/Entities/SessionQueuedMessage.cs:13–99` — no hold column today.
- `tests/Antiphon.Tests/Application/AgentTaskCheckInterpreterTests.cs:47–88,681–720` — the
  harness settles a specialist task in-process by writing its row; reuse for the distiller.
- `tests/Antiphon.Tests/Application/InstructionBundleTests.cs:48–56` pins the exact bundle key set
  (must add `output-distiller`); `:221` contract-forward test to mirror.
- `client/src/features/delegations/TaskDetailBody.tsx:241–266` — Report section; `:95–114`
  auto-`markRead` on open. `client/src/api/agentTasks.ts:196–211` — `AgentTaskDetailDto`.
- Live: `antiphon-check-interpreter` `be5d4502`, Running, `C:\logs\antiphon\check-interpreter`,
  `replyStyle=Normal`; `Antiphon-Orchestrator` `a392cbc4`, `replyStyle=Brief`, `sessionBackend=PtyHost`.

---

## Slices

Sequential. Each lands green, committed and pushed, independently revertable. Shared workspace is
fine for S1–S3 if nothing else is writing `AgentTask*`; another active worker forces `-Worktree`.

### S1 — `Distill` role and the specialist predicate (~3–4 h)

**Files:** `server/Domain/Enums/AgentTaskEnums.cs` (`Distill = 12`, `AgentTaskRoles.IsSpecialist`,
`NotSpecialist` expression, `Distilled = 25` event type), the ~40 carve-out sites listed in D3,
`DelegationReportFormatter.BuildBrief:188` (Distill → `DistillReportingContract`, new method beside
`CheckReportingContract`), `AgentTaskReplyService` (a `ClassifyDistillReport` arm before the
generic arms: non-empty → Succeeded/Exempt, empty or `failed` → Failed), `client/src/api/agentTasks.ts`
(role value), `docs/antiphon-api.md` (`includeChecks` now hides both specialist roles).
**Tests:** new `SpecialistRoleContractTests` scanning `server/**/*.cs` for `AgentTaskRole.Check`
comparisons outside the allowlist; every existing Check test in the 23 files still green (the
predicate change must be behaviour-preserving for Check); a `Distill` row is hidden by default,
bypasses the cap, is never card-bound, never armed, never compacted, never leased, carries no
bundles, is not nudged — each asserted once with `Distill` where the Check tests assert it with
`Check` (`[Arguments(AgentTaskRole.Check)][Arguments(AgentTaskRole.Distill)]` on the existing tests
where the harness allows).

### S2 — the seat exists, supervised, with a versioned contract (~3 h)

**Files:** `server/Bundles/output-distiller.md` (D5); `InstructionBundles.OutputDistiller` key;
`server/Application/Services/OutputDistillation.cs` (`ContractVersion`, `Contract` forward,
`OutputFormatReminder`, `BuildGoal`, `BuildTitle`, `DenyAllToolsSettingsJson` with the distiller's
stderr line); `StandingSpecialistProvisioner` + `SpecialistSpec` extracted from
`CheckInterpreterProvisioner` (facade kept), `OutputDistillerProvisioner` facade; `DelegationSettings`
(`OutputDistillerEnabled=true`, `OutputDistillerMode=Shadow`, `OutputDistillerAgentSlug`,
`OutputDistillerWorkingDirectory`, `OutputDistillerWaitSeconds=45`, `OutputDistillerMaxBacklog=3`,
`DistillMinChars=1200`, `DistillMaxRawChars=20000`, `DistilledMaxChars=1500`, `DistilledMaxRatio=0.6`);
`AgentIncidentKind.OutputDistillerUnavailable=46`; `Program.cs` DI; `appsettings.json.example`;
the three scripts/docs that know the interpreter's directory (D4).
**Tests:** `OutputDistillerProvisionerTests` — the ten `CheckInterpreterProvisionerTests` cases
against the new spec (shape, hook file, idempotent, recreate, reconcile, heal, disabled, directory
derivation); `CheckInterpreterProvisionerTests` unchanged and green through the facade;
`InstructionBundleTests`: key set includes `output-distiller`, contract forwards, invariants
sentences present, length ≤ 3 000, opens with the seat line, version label matches
`OutputDistillation.ContractVersion`.

### S3 — the pipeline in Shadow mode: queue, worker, gates, ledger (~6–8 h)

**Files:** `server/Domain/Entities/OutputDistillation.cs` + enums; `AgentTask.DistilledResult`;
`SessionQueuedMessage.HoldUntil`; one migration (`AddOutputDistillation`); `AppDbContext` maps;
`OutputDistillationGate.cs` (D6, pure); `OutputDistillationQueue.cs` (Channel) and
`server/Infrastructure/Orchestration/OutputDistillationHostedService.cs` (drainer; `EnsureAsync`
once at startup like `AgentTaskCheckHostedService.cs:84–91`); `OutputDistillationService.cs`
(`RequestAsync`, the create → wait → gate → apply/record sequence, the unavailable incident);
`AgentTaskReplyService.DeliverToParentAsync` (hold + post); `SessionMessageQueueService.DeliverNextLockedAsync`
(skip held rows; SendNow ignores holds; a `HoldUntil` in the past is not a hold); `AgentTaskService.GetAsync`
(`FullReadAt`); DTO `distilledResult` on the task detail.
**Tests:** `OutputDistillationGateTests` (table: every anchor class kept/missing, the ≤10/≥60 % path
rule, the length band edges, degenerate inputs); `OutputDistillationTests` (harness from
`AgentTaskCheckInterpreterTests`): Apply mode — settled distillation passing the gates replaces the
held row's body, header and `ContentDigest` unchanged, `HoldUntil` cleared, `DistilledResult`
written, `Distilled` event names cost; a row already delivered records `AppliedLate` and leaves the
body alone; a rejected distillation delivers the raw body and records the missing anchors; timeout
cancels a still-Queued distill task, clears the hold, raw delivers; backlog at the cap degrades
without creating; disabled → today's byte-for-byte note and no ledger row; short and long reports
skipped with reasons; Shadow mode — no hold, body never replaced, ledger row `Shadowed` with the
distilled text on the task; the polled shrink still wins over a distillation when the parent
already read the raw; the CARD-0132 completion-note grace still sees the (held) note exist; a
`Blocked` task is never distilled; a specialist row is never distilled (no recursion); the
unavailable incident dedups per minute. `SessionMessageQueue` tests: a held row is skipped and
delivers once the hold lapses; SendNow delivers a held row.

### S4 — the signals: feedback endpoint, CLI, drawer (~3–4 h)

**Files:** `AgentTaskEndpoints.cs` (`POST /{id}/distillation/feedback`), new
`DistillationEndpoints.cs` (`GET /api/distillations`, `/stats`), DTOs, `scripts/delegate.ps1`
(`-Flag`/`-Verdict`/`-Note`), `scripts/distiller.ps1` (`-Stats`, `-List [-Flagged]`),
`client/src/api/agentTasks.ts` + `TaskDetailBody.tsx` (collapsed "Distilled" section above Report
when present, two buttons, feedback state), `docs/antiphon-api.md`, `docs/ops-http.md`.
**Tests:** endpoint tests (feedback on a task without a row → 409; second feedback overwrites with
new `FeedbackAt`; stats counts by outcome/feedback/stamp match seeded rows; `since` window);
`FullReadAt` set only by a parent poll after `SentAt`; a Vitest for the drawer section rendering
and the two buttons' mutations.

### S5 — the loop: schedule, brief, proposal card (~2–3 h)

**Files:** `docs/orchestration-loop.md` new §"Distiller prompt review" (the schedule's exact
`PromptText`, the delegate brief template, the evidence bar, the merge-then-restart step, the
rollback), `server/Bundles/README.md` (a paragraph: the distiller bundle is the one whose edits
arrive as review cards, and the invariants a review may not touch), `docs/orchestration-findings.md`
(entry recording the premise corrections above). Ops: create the weekly schedule with
`schedule.ps1` against the live server; record its id on the card.
**Tests:** none new (docs + one schedule row); `InstructionBundleTests` already guards the file.

### S6 — live verification and the docs that changed shape (~2 h)

Foreground, against the dev stack (from the main checkout, per the worktree refusal rule): the
provisioner creates the seat and it comes up warm; a `-ExpectAbout 1` delegate with a ≥ 1 500-char
report is distilled in Shadow (ledger row, `DistilledResult`, note untouched); flip
`OutputDistillerMode=Apply` in settings, repeat, and confirm the held note lands distilled with the
header intact and the pointer line; poll the full report from the orchestrator and see `FullReadAt`;
`-Flag` it and see the row; kill the seat's session mid-distillation and see the raw note deliver
with the hold lapsed and one incident; delete the agent row and see it recreated. Record the
timings (median wait vs the 45 s budget) in `docs/orchestration-findings.md`. Docs:
`docs/agent-kinds.md` and `docs/orchestration-loop.md` name the second piece of furniture, the
Shadow→Apply switch, and the fact that a `[task … done]` note may now be a distillation with a
pointer; the delegate skill's "Trust the report" section says the *full* report is what to trust
and where it is.

### S7 — optional: offline replay judge for a proposal (~3 h, after S5 has run at least twice)

The review delegate, before opening a card, dispatches one Low-tier ephemeral delegate whose brief
is the *candidate* bundle text plus the window's flagged raw reports, runs `OutputDistillationGate`
over the answers (via a small `distiller.ps1 -Replay <bundle-file> <task-ids>` that calls a
read-only `POST /api/distillations/replay` evaluating gate results without touching anything), and
attaches the before/after gate table to the card. Not built until the loop has produced two real
proposals, so the replay is shaped by what reviewers actually needed.

---

## Test matrix

| Concern | Where |
|---|---|
| No `AgentTaskRole.Check` comparison survives outside the Check-specific allowlist | `SpecialistRoleContractTests` (S1) |
| Every Check carve-out holds for Distill | parameterised existing tests (S1) |
| The seat's row shape, hook, reconcile, heal, disabled, directory | `OutputDistillerProvisionerTests` (S2) |
| Bundle key set, forward, invariants, ≤ 3 000 chars, version label | `InstructionBundleTests` (S2) |
| Anchor retention and length band, every class and edge | `OutputDistillationGateTests` (S3) |
| Apply/late/reject/timeout/backlog/disabled/short/long/shadow/blocked/recursion/incident | `OutputDistillationTests` (S3) |
| Held rows skipped, released, SendNow overrides; digest and header untouched; shrink still wins | queue tests (S3) |
| Feedback 409/overwrite; stats; `FullReadAt` only by parent after `SentAt` | endpoint tests (S4) |
| Drawer section and buttons | Vitest (S4) |
| The whole path against a real seat, both modes, failure injection | S6 live run |

Run with `dotnet run --project tests/Antiphon.Tests --treenode-filter "/*/Antiphon.Tests.Application/*/*"`
built to `--property:OutputPath=bin-distiller/` (forward slash), then the targeted classes.

---

## Sequencing and risks

- **S1 is the widest diff and the least interesting** — forty mechanical edits. Land it alone, first,
  with the contract test, so S3's real logic reviews on its own.
- **The hold changes note ordering inside one session** (a held completion is not in the flush's
  head run). Bounded by the 45 s budget and by the fact that WhenIdle notes to a busy orchestrator
  already wait minutes. If it ever matters, the alternative is holding the whole flush while any
  Delegation row is held — one predicate change, noted in S3's code.
- **p90 turnaround of the interpreter is 183 s**, so ~10 % of distillations will land `AppliedLate`
  at a 45 s budget when the parent is idle. That is the ledger's first question for the review
  (raise the budget, or accept). Shadow mode measures it before anything is at stake.
- **A haiku that copies identifiers imperfectly** is the expected failure the anchor gate catches.
  If the reject rate is high in Shadow, the fix is the prompt (v2 via the loop), the gate's leniency
  on paths, or the tier knob — all visible in the ledger before Apply is switched on.
- **Two seats share `AgentControlService.StartAsync`'s quota bypass** (`IgnoreSubscriptionQuota:
  true` in the provisioner) — deliberate, same reason as the interpreter; note it in `agent-kinds.md`'s
  quota gotcha line which already lists the interpreter.
- **The reaper** (`reap-orphaned-pty-hosts.ps1`) keys a rule on the interpreter's cwd; a second
  scratch cwd that the reaper does not know would be reaped as an orphan. S2 adds the twin rule
  before the seat ever runs.

---

## Left open, deliberately

- **Whether to distil Failed reports at all.** Included, since a long failure narrative is where
  the caller most needs the blocker and the counts. If the Shadow ledger shows failures rejecting
  at a high rate (they cite many paths), scope it to Succeeded by one predicate.
- **Per-role thresholds.** A Plan report's 3 400 chars *is* the deliverable and the orchestrator is
  told to read plans it must judge (`orchestrator.md`); a single `DistillMinChars` may be wrong for
  Plan. The ledger's per-role view (add `Role` to `/stats`) answers it; the first review decides.
- **A second consumer.** Channel replies to humans are the obvious one (a Telegram answer has a
  4 000-char cap already, `ChannelBridge:MaxReplyChars`). New card when someone wants it; the
  service entry point is shaped for it.
- **Whether the gate should also require the report's own first line to survive.** Cheap to add
  (exact-substring of the raw's first sentence) and probably right; left to the S3 executor to
  decide from the first hundred Shadow rows rather than from taste.
