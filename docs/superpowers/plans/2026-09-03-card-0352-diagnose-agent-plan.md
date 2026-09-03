# CARD-0352 — `antiphon-diagnose`: a haiku seat that titles untitled tasks and labels unlabelled cards

**Date:** 2026-09-03 (Plan pass, task d1844c51 — design only; no production code changed, no tests run)
**Card:** CARD-0352 "Build a haiku-tier Diagnose agent: auto-title untitled tasks, auto-label card complexity/UI-scope (resolves CARD-0303)" (InProgress, Normal/Normal, rank 10)
**Resolves:** CARD-0303's open question is settled by the operator (build it). This plan does not reopen it.
**Prerequisite landed:** CARD-0351 (`delegate.ps1 -Title` capped at 80, warning on long Goal fallback) is on master at `ae06d961`.
**Coordinates with:** CARD-0330 (output distiller; plan `633e4ba3`, **no code landed yet**), CARD-0332 (role × complexity matrix, Backlog, consumer of job 2), CARD-0334 (live-orchestrator propagation, Backlog).

**Sources (verified this pass):** CARD-0352, CARD-0303, CARD-0330 + its plan, CARD-0332, CARD-0334, CARD-0079 (closed), CARD-0335 plan, CARD-0339 plan, CARD-0351 plan; `server/Bundles/check-interpreter.md`; `server/Application/Services/{CheckInterpreterProvisioner,CheckInterpretation,AgentTaskCheckService,AgentTaskCheckQueue,AgentTaskService,AgentTaskDispatcher,AgentTaskReplyService,AgentTaskCardBinder,CardService,CardRevisionLog,BoardService,ExternalTrackerSyncService,TrackerBidirectionalSyncService,TrackerSyncMarkers,InstructionBundles,ModelAvailability}.cs`; `server/Infrastructure/Orchestration/{AgentTaskCheckHostedService,CardWorkTransitionHostedService}.cs`; `server/Application/Settings/DelegationSettings.cs`; `server/Domain/Entities/{Card,AgentTask,Board}.cs`; `server/Domain/Enums/{AgentTaskEnums,AgentIncidentKind,CardStatus,CardRevisionKind}.cs`; `server/Application/Dtos/{AgentTaskDtos,BoardDtos}.cs`; `server/Api/Endpoints/{AgentTaskEndpoints,CardEndpoints}.cs`; `scripts/{delegate,card}.ps1`; `.claude/skills/antiphon-delegate/SKILL.md`; `client/src/features/board/BoardCard.tsx`; `tests/Antiphon.Tests/Application/AgentTaskCheckInterpreterTests.cs`; `tests/Antiphon.Tests/TestHelpers/{ProductionRunnerGuard,AntiphonWebAppFactory}.cs`; and the live server on 17202 (`/api/cards`, `/api/cards/{id}`, `/api/agent-tasks`, `/api/boards`) on 2026-09-03.

---

## Verdict up front

**One new standing haiku seat, `antiphon-diagnose`, doing exactly two jobs, both wired server-side so no orchestrator has to remember to run them.** It is provisioned, supervised, tool-less and pinned exactly like `antiphon-check-interpreter`, on the shared "standing specialist" substrate that CARD-0330's plan already designed (D3 there) but has not built — whichever card executes first builds that substrate; the other rebases onto it.

The five questions the card asks, answered:

| Question | Answer |
|---|---|
| 1. Auto-title trigger | **Async, after create.** `POST /api/agent-tasks` stores the Goal-first-line fallback exactly as today, returns at once, and — only when no Title was sent AND the fallback is longer than 80 chars — hands the task id to the diagnose worker. The title is replaced in place typically 30–60 s later; the create response says `titleDiagnosisQueued: true` so `delegate.ps1` can print "title pending". Creation is never blocked and never fails on the seat. |
| 2. Auto-label trigger | **A periodic sweep, not a create hook.** Every `DiagnoseSweepMinutes` (10) the worker takes up to `DiagnoseSweepBatch` (5) open cards on non-archived boards that lack a `complexity:*` or `ui:*` label, ordered by status then importance, with a 24 h / 3-attempt backoff per card recorded in a ledger. Plus one on-demand entry, `POST /api/cards/{id}/diagnose` / `card.ps1 diagnose CARD-nnnn`, which is also how S5 verifies the path. The sweep covers every card-creation path (card.ps1, tracker import, workflow spawn, UI) without hooking any of them. |
| 3. Cost / rate limiting when haiku is held | **Never block the underlying operation; never create rows that cannot run.** Before creating a diagnose row the worker checks the seat's alias against `ModelAvailability.IsHeldAsync` (the CARD-0079 lesson: 53 of 76 interpreter rows were created into a hold and cancelled). Held → no row, title request dropped, sweep tick skipped, ledger row `DegradedHeld`. A `DiagnoseDailyBudgetUsd` (2.00) cap, a `DiagnoseMaxBacklog` (2) gate, a `DiagnoseWaitSeconds` (90) budget, and the two kill switches (`DiagnoseEnabled`, plus `DiagnoseTitleEnabled` / `DiagnoseSweepEnabled`) bound the spend. Every degraded path leaves the raw fallback title / the unlabelled card exactly as today. Expected cost at current volume: ≈ $0.01 per request (the interpreter's median reading is $0.016 on a bigger bundle), ≈ 70–100 titles/day today falling as CARD-0351's skill rule takes hold, and a one-off ≈ 120-card backlog sweep ≈ $1–2 total. |
| 4. Where it lives | **A second standing specialist row (`antiphon-diagnose`), not a job on the check interpreter and not CARD-0330's distiller.** Same reasoning as CARD-0330 D1: the check contract forbids what this seat must do (write a verdict about a *card*, not a running task) and shares budgets and a kill switch it must not share. What the three seats have in common — find-or-create, reconcile-from-bundle, deny-all hook, own cwd, pinned Low-tier row, poll-until-settled, cancel-if-queued, unavailable incident — becomes generic once (S1: `StandingSpecialistProvisioner` + `SpecialistSpec`, exactly CARD-0330 D3's names, plus a `SpecialistTaskRunner` extracted from `AgentTaskCheckService.InterpretAsync`). One seat for both diagnose jobs: the two request kinds share every rule (read what you are given, answer one line in a fixed grammar, use no tools, never modify anything) and differ only in the answer grammar, which the request's first word selects. Splitting them would be a row, a directory, an incident kind, a reaper rule and a test-guard entry for no behavioural gain. |
| 5. Scope discipline | **Only these two jobs.** Not built, on purpose (see "Not this card" at the end): picking a workflow, path, model, tier or worktree for a card; deciding Investigate-vs-Plan; setting `AgentTask.Complexity` from a label (CARD-0332's dispatcher does that); rewriting card titles or descriptions; UI badges for the labels (CARD-0333); a self-improving prompt (CARD-0330's loop, on its own evidence). |

**Five slices, sequential, ~19–22 h** (S1 shrinks to ~2 h if CARD-0330 S1+S2 have landed first). S1 is the shared substrate; S2 the seat and contract; S3 job 1; S4 job 2; S5 docs, skill, live verification and the first backlog sweep.

---

## Live evidence, and what it changes

**Job 1 is not hypothetical.** Of the 289 non-Check tasks created on 17202 in the three days to 2026-09-03, **218 carry a 300-character title** — the `BuildTitle` clamp of an unbroken Goal paragraph (`AgentTaskService.cs:1849–1861`). Median title length is 300; the roles are Code 118, Plan 95, Debug 24, Merge 14, Review 14, Deploy 11. Every check header, completion note, attention row and home rail line dumps that excerpt (CARD-0350/0351). CARD-0351's skill rule ("always pass `-Title`") will cut the rate, but the orchestrator seat that dispatched all 218 was already told to be brief and is a long-lived session that will not re-read the skill until it restarts (CARD-0334's exact gap) — so the server-side fallback is what fixes the board this week.

**Job 2 has a backlog and no vocabulary yet.** The Antiphon board holds 350 cards: 53 Backlog, 2 InProgress, 65 Review, 230 Done. **120 are open; none carries a complexity or UI label**; 40 carry no label at all. Existing labels are free-form topic tags (`bug` 25, `reliability` 24, `delegation` 20, `pty` 12, `ux` 10 …) stored as a JSON string list (`Card.LabelsJson`), with two *managed* prefixes already in use by tracker sync: `status:` and `priority:` (`TrackerSyncMarkers.IsManagedLabel`). Full descriptions of 40 sampled open cards: median 3 841 chars, p90 6 715, max 8 412 — every one fits a single brief (`ModernPtyBriefInlineMaxBytes` 43 200) with room for the contract.

**The vocabulary decision.** The card says Complex/Medium/Simple. The code already has one complexity axis: `TaskComplexity { Hard, Medium, Easy }` (`AgentTaskEnums.cs:192`), spoken by `delegate.ps1 -Complexity Hard|Medium|Easy`, `ComplexityChains` (CARD-0090, shipped S1–S4) and the CARD-0332 matrix that will consume job 2's label. **The labels are `complexity:hard|medium|easy`.** Two vocabularies for one axis would need a mapping table the orchestrator has to remember; the operator's words are read as the same three-way split. The UI flag is `ui:yes|no` — two-valued so "diagnosed as not UI" is distinguishable from "never diagnosed", which a bare `ui` tag cannot express. Both go at the END of the label list: `BoardCard.tsx:53` shows only the first two labels on a card face, and the topic tags are what a human scans for.

**The tracker-sync hazard.** `ExternalTrackerSyncService.cs:329–356` (import-authoritative) replaces `card.LabelsJson` wholesale with the issue's labels minus managed prefixes whenever they differ. A diagnosis label on an imported card would be erased on the next import. S4 makes diagnosis labels survive import (union with the card's existing `complexity:`/`ui:` labels) and keeps them off the outbound export (`TrackerBidirectionalSyncService.cs:475–520`), so they stay Antiphon-local.

**The hold lesson.** CARD-0079's reopen audit: 53 of 76 interpreter rows over one night were created while the `haiku` alias was held, sat Queued behind `AgentTaskDispatcher.cs:447–485` ("haiku is held; dispatch paused for that model"), and were cancelled at the 60 s budget with a `Held` event each. CARD-0335 bounded such holds to six hours, but a six-hour window of dead rows is still waste and noise. The diagnose worker checks the hold *before* creating a row (`ResolveDispatchAliasAsync`'s logic, `:3310`: the seat's normalised `ModelId`, else `ModelLevelAliases.For(ClaudeCode, Low)` = `haiku`).

---

## Decisions

### D1. One seat, two request kinds, one contract with two answer grammars

The seat receives briefs whose first word is `TITLE` or `LABELS`. Each brief carries the input (a task goal, or a card title + description), then a one-line format reminder naming the grammar, then the Diagnose reporting contract closer. The standing contract (`server/Bundles/diagnose.md`, D5) defines both grammars and the shared hard rules. Why not two seats: CARD-0330's D1 argument for a *separate* seat rested on contradictory safety rules (a check must never say "complete"; a distillation must carry `done|failed` verbatim) and on divergent budgets. Here both jobs are classification of a static text with no completion semantics at all, they share one wait budget, and both are best-effort cosmetic/routing metadata. Why not the check interpreter: its v4 contract is written around a *running* task and pins "never say the checked task is complete", "LOOKS STUCK ≠ blocked", one-line-240-chars — none of which applies, and its `CheckInterpreterMaxBacklog=2` / `WaitSeconds=60` are time-critical budgets a sweep batch must not compete with.

### D2. The shared substrate (built here or by CARD-0330, whichever executes first)

Same names as CARD-0330 D3 so the two plans converge on one refactor:

- `AgentTaskRole.Diagnose = 13` (`Distill = 12` is reserved for CARD-0330 whether or not it lands first; gaps are fine).
- `AgentTaskRoles.IsSpecialist(role)` (`Check`, `Distill`, `Diagnose`) plus a `static readonly Expression<Func<AgentTask,bool>> NotSpecialist` for EF predicates (a method call does not translate). The 50 `AgentTaskRole.Check` comparisons outside `AgentTaskCheckService` (dispatcher `:281,308,353,408,535,2016,2107,2164,2933,2950,3847`; `AgentTaskCardBinder.cs:90`; `InstructionBundles.ForDelegate:173`; the attention/away-digest/home/pipeline/land/orchestrator/lease/routing-pin sites CARD-0330 D3 enumerates) become specialist predicates. Two Check-specific sites stay: `ClassifyCheckReport` (`AgentTaskReplyService.cs:2321`) gains sibling arms; `CheckReportingContract` in `DelegationReportFormatter.BuildBrief` gains a `DiagnoseReportingContract` sibling.
- `SpecialistRoleContractTests` scans `server/**/*.cs` and fails on any `AgentTaskRole.Check` comparison outside the allowlist (the `AreaMapContractTests` shape), so the next specialist cannot be forgotten at one of fifty sites.
- `StandingSpecialistProvisioner` parameterised by `SpecialistSpec(Slug, WorkingDirectory, Details, BundleKey, ContractVersion, DenyHookStderr, UnavailableIncidentKind)`, extracted from `CheckInterpreterProvisioner` (`:71–128` find-or-create, `:135–155` reconcile, `:158–185` hook, `:188–204` slug/dir). `CheckInterpreterProvisioner` stays as a one-line facade so its ten tests, its DI registration and `AgentTaskCheckHostedService.cs:84–91` do not move. `DiagnoseProvisioner` is the second facade.
- `SpecialistTaskRunner.RunAsync(spec, title, goal, waitBudget, ct) → SpecialistRun` extracted from `AgentTaskCheckService.InterpretAsync` (`:380–472`): ensure → backlog gate → create the pinned Low-tier row byte-for-byte as `CreateInterpretationTaskAsync` (`:556–597`: own root, depth 0, `Workspace=Shared`, cwd = seat's dir, `Ephemeral=false`, `ReplyTo=None`) → poll as `WaitForInterpretationAsync` (`:604–628`) → cancel-if-still-Queued on timeout → the per-minute-deduped unavailable incident + alert (`:481–547`) keyed by the spec's incident kind. Returns `{Outcome, Result, CostUsd, WaitMs, RunTaskId}`. `AgentTaskCheckService` calls it for checks; behaviour-preserving, proven by the existing interpreter tests staying green.
- `AgentTaskEventType.Diagnosed = 26` (`Distilled = 25` reserved for CARD-0330). `AgentIncidentKind.DiagnoseUnavailable` = next free value at execute time (**46 is now `LaunchInterruptedByRestart`** — CARD-0340 took the number CARD-0330's plan assumed; CARD-0330 gets 47, this card 48, or the reverse if this lands first — read the enum, do not trust either plan's number).

Ordering rule, written on both cards at execute time: if CARD-0330 S1/S2 have landed, S1 here is "add `Diagnose` to the predicate, add a third `SpecialistSpec`, and extract `SpecialistTaskRunner` if CARD-0330 S3 did not". If this card lands first, CARD-0330 S1/S2 shrink the same way.

### D3. Job 1 — auto-title: async, gated, in-place, never blocking

**Trigger** (`AgentTaskService.CreateAsync`, after the `SaveChangesAsync` that persists the row and its `Created` event, `:720ff`): when `string.IsNullOrWhiteSpace(request.Title)` AND `!IsSpecialist(request.Role)` AND `DiagnoseEnabled && DiagnoseTitleEnabled` AND the stored fallback title's length `> DiagnoseTitleMinFallbackChars` (80 — the same threshold as CARD-0351's CLI warning; a one-line short Goal already *is* a title) → `_diagnoseQueue.TryEnqueue(DiagnoseRequest.ForTitle(task.Id))` and set `TitleDiagnosisQueued = true` on `AgentTaskCreatedDto`. Merge tasks (`:1571`, explicit title) and Check/Distill/Diagnose rows never qualify. A task created Blocked (repeat guard, routing exhausted) still qualifies — its title is on the board too.

**Queue and worker.** `DiagnoseQueue` is the `AgentTaskCheckQueue` shape (unbounded `Channel<DiagnoseRequest>`, single reader) drained by `DiagnoseHostedService` (`BackgroundService`; `EnsureAsync` the seat once at startup like `AgentTaskCheckHostedService.cs:84–91`; serial; each request in its own scope; exceptions logged and dropped). Serial on purpose: the seat is one session that answers one turn at a time, so parallel requests would only queue inside it; a serial drainer keeps the specialist backlog at one row from this source and makes the ledger's `WaitMs` honest. At the interpreter's measured warm median of 29 s, 100 titles/day is ~50 min of drainer time.

**Per request:** load the task `AsNoTracking`; if `task.Title != BuildTitle(fallback of task.Goal)` (someone or something already changed it) → ledger `SkippedAlreadyTitled`, done. Gates in order — hold check (D6), daily budget (D6) — then `SpecialistTaskRunner.RunAsync(DiagnoseSpec, Diagnosis.BuildTitleTaskTitle(task), Diagnosis.BuildTitleGoal(task), DiagnoseWaitSeconds)`. On `Succeeded` with text → `Diagnosis.TryParseTitle(answer, fallback, task.CardIdentifier)`:

- trim; strip one pair of surrounding quotes/backticks; strip a trailing full stop;
- reject: empty, more than one physical line, `> 80` chars, fewer than 2 or more than 10 words, contains `antiphon-task`/`antiphon-report`, equals the fallback, starts with `TITLE`/`Title:`;
- if the task is card-bound and the title does not already contain that identifier, prefix `CARD-nnnn ` in code (the CARD-0040 reading habit — and `-Pin`'s regex reads the title — are preserved by code, not by trusting haiku); the prefixed result may reach 90 chars, reject above 100.

Apply in a fresh scope: reload tracked; re-check the still-fallback condition; `task.Title = title`; add `AgentTaskEvent { Type = Diagnosed, Detail = "Title set by antiphon-diagnose from \"<first 60 chars of fallback>…\" (diagnose task ab12cd34, $0.0087)" }`; save; publish `AgentTaskChanged` (`AgentTaskService.cs:753` shape). Ledger row `Applied`. Every other exit (rejected, timeout, failed, empty, busy, held, budget, unavailable) leaves the row untouched and writes its outcome to the ledger; nothing is delivered to anyone. Card binding is **not** re-run: the row bound at create from the fallback (which contained the same identifiers, being the Goal's first line).

**What the caller sees.** `delegate.ps1` keeps CARD-0351's pre-POST WARNING (a failed create should still teach) with one clause added — "…until antiphon-diagnose replaces it, if enabled" — and, after the response, prints `  title: pending (antiphon-diagnose will set it from the goal; pass -Title to set it yourself)` when `titleDiagnosisQueued` is true. The check header (`AgentTaskCheckService.BuildNote`), completion note and every projection read `task.Title` at render time, so they pick the new title up with no change.

### D4. Job 2 — auto-label: a sweep with a ledger, labels appended, a revision per write

**Selection** (`CardDiagnosisSweep.SelectAsync`): boards with `ArchivedAt == null`; cards with `ArchivedAt == null` and `Status ∈ {Backlog, InProgress, Review, NeedsDecision}` whose labels lack a `complexity:` label OR lack a `ui:` label (a card a human labelled with one family still gets the other); excluding cards with a `Diagnoses` row in the last `DiagnoseRetryHours` (24) or with `≥ DiagnoseMaxAttemptsPerCard` (3) non-`Applied` rows newer than the card's `UpdatedAt` (an edited card earns a fresh attempt). Ordered Backlog → NeedsDecision → InProgress → Review (the label matters most before the next dispatch), then `Importance` desc, `Urgency` desc, `CreatedAt` desc. Take `DiagnoseSweepBatch` (5). Done/Canceled cards are never swept: CARD-0332 does not route finished work, and back-labelling 230 cards is a calibration exercise for another card.

**Timer.** `DiagnoseSweepHostedService`, the `CardWorkTransitionHostedService` shape (`PeriodicTimer`, `DiagnoseSweepMinutes` = 10, floor 1), each tick selecting the batch and enqueueing one `DiagnoseRequest.ForCard(cardId)` per card onto the same `DiagnoseQueue` job 1 uses — one drainer, one ordering, one set of gates. A tick that finds the alias held or the budget spent enqueues nothing (D6). Title requests and card requests interleave in arrival order; at batch 5 per 10 min the sweep can never starve titles for more than ~2.5 min.

**Per card:** load `Card` (title, description, labels, status, identifier); brief = `Diagnosis.BuildLabelsGoal(card)`: `LABELS for CARD-0352 "…" (Backlog)`, the description clamped to `DiagnoseMaxInputChars` (12 000) head+tail with a `[… n chars elided …]` marker (no live open card exceeds it), and the format reminder. Run through `SpecialistTaskRunner`. `Diagnosis.TryParseLabels(answer)` accepts exactly `complexity=(hard|medium|easy) ui=(yes|no)` (case-insensitive, whitespace-tolerant, one line); `unclear` is a valid answer that produces no labels and a ledger row `Unclear` (counts toward backoff); anything else is `RejectedUnparseable`.

**Apply** (`CardService.ApplyDiagnosisAsync(cardId, complexity, ui, reason, ct)` — a service method, not the HTTP `UpdateContentAsync`, because there is no caller token and the writer must handle its own conflict): load tracked; recompute which families are still missing (a human may have added one meanwhile — the human's wins, only the missing family is added; on-demand re-diagnosis replaces the diagnosis-prefixed labels only); `card.LabelsJson = Serialize(existing minus replaced diagnosis labels, then the new diagnosis labels appended)`; `CardRevisionLog.AppendContentEdit(card, "antiphon-diagnose: complexity=medium ui=no (diagnose task ab12cd34, $0.0091)", "antiphon-diagnose", now)` (the card record is append-only and this is a content edit of the record — `UpdateContentAsync:466` does the same); `UpdatedAt = now`; new `ConcurrencyToken`; `SaveCardWriteAsync` (`:1010`); publish `CardChanged`. A `ConflictException` → ledger `RejectedConflict`, retried next tick after the backoff window (a conflict means somebody is editing that card right now; not our turn).

**Modes.** `DiagnoseLabelMode = Apply | Shadow` (default **Apply** — the operator asked for labels, and until CARD-0332 ships nothing routes on them, so a wrong label costs a human one `card.ps1 edit -Labels`). Shadow runs everything, writes the ledger and no label; S5 uses it for the first batch to eyeball quality, then flips.

**On-demand.** `POST /api/cards/{id}/diagnose` → 202 `{ queued: true }` (409 when `DiagnoseEnabled=false`); enqueues `DiagnoseRequest.ForCard(cardId, force: true)` which bypasses the backoff and label-presence checks and replaces diagnosis labels. `card.ps1 diagnose CARD-nnnn` calls it and prints the ledger row when it lands (poll `GET /api/diagnoses?cardId=` for up to 120 s, or `-NoWait`). This is the deterministic live-verification path and the operator's "re-judge this one" button.

**Tracker sync.** New static `CardDiagnosisLabels` (`ComplexityPrefix = "complexity:"`, `UiPrefix = "ui:"`, `IsDiagnosisLabel`, `Complexity(labels) → TaskComplexity?`, `Ui(labels) → bool?`). `ExternalTrackerSyncService:330` builds the imported list as `StripManaged(issue.Labels) ∪ card's current diagnosis labels` so an import cannot erase a diagnosis; `TrackerBidirectionalSyncService:480` and `:690` exclude diagnosis labels from what is pushed to the tracker (they are Antiphon routing metadata, not GitHub labels). `TrackerSyncMarkers.IsManagedLabel` is left alone: diagnosis labels are not sync-managed.

### D5. The contract — `server/Bundles/diagnose.md`, v1

The file is the whole standing prompt (README rule: no frontmatter, no comments); `Diagnosis.Contract => InstructionBundles.TextOf(InstructionBundles.Diagnose)`, `Diagnosis.ContractVersion = "1"` held together with the literal `contract v1` by a test exactly as `CheckInterpretation.ContractVersion` is. Draft; the executor may tighten wording but not remove a HARD RULES sentence (pinned by `InstructionBundleTests`).

```
You are the Antiphon DIAGNOSE agent (contract v1).

Every message you receive is one request about a piece of work that belongs to someone else.
The first word of the request names the job. Answer with exactly one physical line in that
job's grammar, then the closing line you are asked for. Nothing else.

TITLE — the request carries a delegated task's goal. Answer with a title of 2 to 8 words,
at most 80 characters, that says what the task will do or find: lead with the verb or the
subject, keep any CARD-nnnn the goal names, drop "please", role words, file paths, and
anything a check header already shows. Target: Plan haiku diagnose seat for CARD-0352.
Not a sentence, no full stop, no quotes.

LABELS — the request carries a card's title and description. Answer exactly:
complexity=hard|medium|easy ui=yes|no
- easy: one place to change (a file, a setting, a doc, a script line), the fix is named
  in the card, tests or verification are obvious, one short slice.
- medium: a few files in one area behind a mechanism that already exists, the design is
  settled by the card, one or two slices.
- hard: a new mechanism or table, cross-cutting change (schema + service + client, or
  several services), open design decisions, three or more slices, or the card says a
  Plan pass must decide something first.
- ui=yes when the work touches the browser client (client/src, a page, drawer, panel,
  badge, chip, form, button, settings screen, board view, or anything a user clicks or
  reads on screen) — even partly. ui=no when it is server, scripts, docs, agents, pty,
  channels or tests only.
If the card is a question with no work described, or the description is empty, answer
exactly: unclear

HARD RULES (these sentences are pinned by a test; a prompt review may change anything else):
- NEVER change, judge, summarise or restate the work. You name it or you label it.
- NEVER invent a CARD id, a number or a name that is not in the request. Copy or omit.
- USE NO TOOLS. You have none, and a tool call is refused before it runs. Do not read
  files, run commands or search; the request is the whole input.
- Exactly one physical line before the closing line: no preamble, no bullets, no
  explanation, no sign-off, no second option.
```

Per-request briefs (`Diagnosis.BuildTitleGoal` / `BuildLabelsGoal`) scrub `[antiphon-task:…]` and `[antiphon-report:…]` markers with `AgentTaskCheckService.ScrubTaskMarkers` (`:873`; a delegate's Goal often opens with its own marker — a live-looking marker in the seat's session would correlate its turn to somebody else's task), then append `Diagnosis.TitleFormatReminder` / `LabelsFormatReminder` and the `DiagnoseReportingContract` closer (`done` after the one line; `failed` if no answer is possible; never `blocked`). Settlement: `ClassifyDiagnoseReport` mirrors `ClassifyCheckReport` — `done` → Succeeded/Marked, `failed` → Failed, anything else → Succeeded/Exempt; a trailing `?` never Blocks the seat's row.

### D6. The gates — code decides what a diagnosis is allowed to change

All pure statics in `Diagnosis.cs`, table-tested; the worker applies them in this order and records the first that fails:

1. **Enabled** — `DiagnoseEnabled` and the job's own switch; off → today's behaviour byte-for-byte, no ledger row.
2. **Held** — `IModelAvailability.IsHeldAsync(ClaudeCode, seatAlias)`; held → `DegradedHeld`, no row, and the sweep tick ends. (The dispatcher's own hold skip at `:447–485` remains the backstop for a hold that begins after the row exists.)
3. **Budget** — `SUM(CostUsd)` of `Role == Diagnose` rows with `CreatedAt >= today 00:00 UTC` `≥ DiagnoseDailyBudgetUsd` → `DegradedBudget`. One indexed query.
4. **Backlog** — unfinished `Diagnose` rows on the seat `≥ DiagnoseMaxBacklog` (2) → `DegradedBusy` (a stuck row from a crash, or a burst; titles are dropped, cards retry next tick).
5. **Run** — timeout → `DegradedTimeout` (row cancelled if still Queued; a Dispatched row settles onto itself and its late text is recorded, never applied); `Failed`/`Canceled` → `DegradedFailed`; empty → `DegradedEmpty`.
6. **Parse** — grammar as in D3/D4 → `RejectedUnparseable` / `RejectedGate` (with the reason text: "3 lines", "91 chars", "contains marker", "equals fallback").
7. **Still applicable** — re-checked in the apply scope → `SkippedAlreadyTitled` / `SkippedAlreadyLabelled`.

**Bundle invariants** pinned by `InstructionBundleTests`: the file opens with `You are the Antiphon DIAGNOSE agent (contract v`; contains the four HARD RULES sentences verbatim; `≤ 3 000` chars; contains no `{agentName}`/`{channels}` placeholders (existing test); key set now includes `diagnose`.

### D7. The ledger — `Diagnoses`

Entity `Diagnosis` → table `Diagnoses`: `Id`, `Kind` (`Title=0`, `Labels=1`), `TaskId?` (FK `AgentTasks`, cascade), `CardId?` (FK `Cards`, cascade), `DiagnoseTaskId?` (the seat's row, set-null on delete), `Outcome` (the enum in D6 plus `Applied`, `Shadowed`, `Unclear`, `RejectedConflict`, `DegradedUnavailable`), `Answer` (text, the raw one-liner or null), `Applied` (text: the title written, or `complexity=… ui=…`), `Reason` (text, gate detail), `BundleStamp` (`diagnose v1 a1b2c3d4` from the catalog hash), `CostUsd`, `WaitMs`, `Forced` (bool), `CreatedAt`. Indexes `(CardId, CreatedAt desc)`, `(TaskId)`, `(CreatedAt)`. One migration `AddDiagnoses`.

Read side: `GET /api/diagnoses?cardId=&taskId=&since=&outcome=&kind=&limit=` (newest first, joined with card identifier / task short id), `GET /api/diagnoses/stats?since=` (counts by kind × outcome, p50/p90 `WaitMs`, total cost, label distribution of `Applied` rows). Read-only, one query burst each, no client work. The ledger is what says whether the sweep is worth its money and whether the contract needs a v2 — this card ships no self-improvement loop; if the numbers ever justify one, CARD-0330's PR-gated pattern is the template.

### D8. What this means for CARD-0334

Nothing here is a stage an orchestrator runs. Titles are fixed by a create hook; labels by a server sweep. A running orchestrator that has never heard of `antiphon-diagnose` loses nothing and breaks nothing. The only propagation this card needs is a *consumer* hint — one sentence in `server/Bundles/orchestrator.md` and one row in the delegate skill: a card's `complexity:*` label is the default `-Complexity` for its dispatches, and `ui:yes` is the signal the UI-deferral policy reads (that policy's durable home is CARD-0332/0333, not this card). A live session picks that up at its next launch, which is exactly the "at natural boundaries" answer CARD-0334 leans toward; the card should cite this as its first worked example when it is planned.

---

## Ground truth (file:line, verified 2026-09-03)

- `server/Application/Services/AgentTaskService.cs:126` `CreateAsync`; `:332` `BuildTitle` before card binding; `:654` `Title = title` on the row; `:720ff` row + `Created` event saved; `:753` `AgentTaskChanged` publish; `:1571` merge-task title; `:1849–1861` `BuildTitle` (explicit → clamp 300; else Goal first line, clamp 300, `"Delegated task"` if empty).
- `server/Application/Services/AgentTaskCheckService.cs:380–472` `InterpretAsync`; `:481–547` incident + alert with 1-min dedup; `:556–597` `CreateInterpretationTaskAsync`; `:604–628` `WaitForInterpretationAsync`; `:873` `ScrubTaskMarkers`.
- `server/Application/Services/CheckInterpreterProvisioner.cs` (whole; the prototype for `StandingSpecialistProvisioner`); `CheckInterpretation.cs` (`ContractVersion`, bundle forward, `OutputFormatReminder`, `DenyAllToolsSettingsJson`, `BuildGoal`, `BuildTitle`).
- `server/Application/Services/AgentTaskCheckQueue.cs` (the queue shape); `server/Infrastructure/Orchestration/AgentTaskCheckHostedService.cs:84–91` (ensure at startup); `CardWorkTransitionHostedService.cs` (the periodic sweep shape).
- `server/Application/Services/AgentTaskDispatcher.cs:447–485` model-hold skip; `:3310` `ResolveDispatchAliasAsync`; Check carve-outs at `:281,308,353,408,535,2016,2107,2164,2933,2950,3847`.
- `server/Application/Services/AgentTaskReplyService.cs:2253` Check classify dispatch; `:2321` `ClassifyCheckReport`.
- `server/Application/Services/AgentTaskCardBinder.cs:90–96` Check never binds (becomes specialist).
- `server/Application/Services/InstructionBundles.cs:88` key constants; `:173` `ForDelegate` Check carve-out; `:196–216` catalog from the manifest; `Antiphon.Server.csproj` embeds `Bundles/*.md`.
- `server/Application/Settings/DelegationSettings.cs:534–579` the five interpreter knobs to mirror.
- `server/Application/Services/CardService.cs:119` `CreateAsync` (`:152` labels); `:453–500` `UpdateContentAsync` (`:466` revision, `:496` labels, token bump); `:1010` `SaveCardWriteAsync` (concurrency → `ConflictException`).
- `server/Application/Services/CardRevisionLog.cs:60` `AppendContentEdit(card, reason, editedBy, now)`; `CardRevisionKind.ContentEdit`.
- `server/Application/Services/BoardService.cs:519/534` `ParseLabels`/`SerializeLabels`.
- `server/Application/Services/ExternalTrackerSyncService.cs:329–356` import overwrites labels; `TrackerBidirectionalSyncService.cs:475–520,690` export; `TrackerSyncMarkers.cs:66–82` managed prefixes.
- `server/Domain/Entities/Card.cs:42` `LabelsJson`; `Board.cs:33` `ArchivedAt`; `CardStatus.cs`.
- `server/Domain/Enums/AgentTaskEnums.cs:23–51` roles (`Check = 11`); events end at `LandedWithResidue = 24`; `:192` `TaskComplexity { Hard, Medium, Easy }`.
- `server/Domain/Enums/AgentIncidentKind.cs:468` last value `LaunchInterruptedByRestart = 46`.
- `server/Application/Dtos/AgentTaskDtos.cs:340` `AgentTaskCreatedDto`; `BoardDtos.cs:211` `UpdateCardContentRequest`.
- `server/Api/Endpoints/CardEndpoints.cs:118–159` the `POST /{id}/…` verbs to sit beside; `AgentTaskEndpoints.cs:19` create.
- `server/Application/Services/ModelAvailability.cs:56` `IsHeldAsync(kind, alias)`; `ModelLevelAliases.cs:26` Low → `haiku`.
- `scripts/delegate.ps1:355–361` CARD-0351 warning; `:450–475` response printing. `scripts/card.ps1` verb `ValidateSet` (`get,history,new,edit,move,close,reopen,archive,unarchive`).
- `client/src/features/board/BoardCard.tsx:53` first two labels only.
- `tests/Antiphon.Tests/Application/AgentTaskCheckInterpreterTests.cs:47–88` in-process specialist settlement harness; `CheckInterpreterProvisionerTests`; `InstructionBundleTests`; `ProductionRunnerGuard.cs:50` / `AntiphonWebAppFactory.cs:106` / `ProductionRunnerIsolationTests.cs:64` (the test-host guard the new seat must join).
- `server/appsettings.json:194`, `scripts/bootstrap-check.ps1:367`, `scripts/reap-orphaned-pty-hosts.ps1:49,170–173`, `docs/bootstrap.md:92`, `docs/testing-and-build.md:30` — the places that know the interpreter's directory.
- Live: 289 non-Check tasks / 218 fallback titles in 3 days; 120 open cards, 0 with `complexity:`/`ui:`; open descriptions median 3 841 chars, max 8 412; boards: Antiphon, AZ Care, ClaudeBot-Antiphon, Codeperf, Family, Gym Stat, school-revision, Slack Test, Torquay Leander, antiphon-check-interpreter.

---

## Slices

Sequential. Each lands green, committed and pushed, independently revertable. S1 is the widest diff and the least interesting; land it alone. Worktree workspace for S1 (it touches fifty files and CARD-0330 may be executing); Shared is fine for S2–S5 if nothing else writes `AgentTask*`/`Card*` — another active worker forces `-Worktree`.

### S1 — the specialist substrate (~4–5 h; ~2 h if CARD-0330 S1+S2 landed first)

**Files:** `AgentTaskEnums.cs` (`Diagnose = 13`, `AgentTaskRoles.IsSpecialist`, `NotSpecialist`, `Diagnosed = 26`); the ~50 Check comparison sites (D2); `AgentTaskReplyService` (`ClassifyDiagnoseReport` arm beside the Check arm); `DelegationReportFormatter` (`DiagnoseReportingContract`); `StandingSpecialistProvisioner.cs` + `SpecialistSpec` extracted from `CheckInterpreterProvisioner` (facade kept); `SpecialistTaskRunner.cs` extracted from `AgentTaskCheckService` (`InterpretAsync` becomes a thin caller); `AgentIncidentKind` (next free value); `client/src/api/agentTasks.ts` (role value); `docs/antiphon-api.md` (`includeChecks` hides every specialist role).
**Tests:** `SpecialistRoleContractTests` (source scan + allowlist); every existing Check test green (23 files); the interpreter tests green through the facade and the runner; `[Arguments(AgentTaskRole.Check)][Arguments(AgentTaskRole.Diagnose)]` on the existing carve-out tests where the harness allows (hidden by default, bypasses the cap, never card-bound, never armed for a check, never compacted, never leased, no bundles, not nudged, no routing pin).

### S2 — the seat exists, supervised, with a versioned contract (~3 h)

**Files:** `server/Bundles/diagnose.md` (D5); `InstructionBundles.Diagnose` key; `server/Application/Services/Diagnosis.cs` (`ContractVersion`, `Contract` forward, `TitleFormatReminder`, `LabelsFormatReminder`, `BuildTitleGoal`, `BuildLabelsGoal`, `BuildTitleTaskTitle`/`BuildLabelsTaskTitle` — `"title for task ab12cd34"` / `"labels for CARD-0352"`, `DenyAllToolsSettingsJson` with the diagnose stderr line, `TryParseTitle`, `TryParseLabels`, `ClampInput`); `DiagnoseProvisioner` facade; `DelegationSettings` (`DiagnoseEnabled=true`, `DiagnoseTitleEnabled=true`, `DiagnoseSweepEnabled=true`, `DiagnoseLabelMode=Apply`, `DiagnoseAgentSlug="antiphon-diagnose"`, `DiagnoseWorkingDirectory`, `DiagnoseWaitSeconds=90`, `DiagnoseMaxBacklog=2`, `DiagnoseDailyBudgetUsd=2.00`, `DiagnoseTitleMinFallbackChars=80`, `DiagnoseSweepMinutes=10`, `DiagnoseSweepBatch=5`, `DiagnoseRetryHours=24`, `DiagnoseMaxAttemptsPerCard=3`, `DiagnoseMaxInputChars=12000`); `Program.cs` DI; `appsettings.json` (`DiagnoseWorkingDirectory: C:\logs\antiphon\diagnose`) and `appsettings.json.example`; `scripts/bootstrap-check.ps1` directory list; `scripts/reap-orphaned-pty-hosts.ps1` (`-DiagnoseDir` twin of the `test-raw-check-interpreter` rule — without it the reaper would kill the seat's test-raw host as an orphan); `docs/bootstrap.md:92`; `ProductionRunnerGuard` (`Delegation__DiagnoseEnabled=false`), `AntiphonWebAppFactory` (same), `ProductionRunnerIsolationTests` (asserts it); the E2E host settings.
**Tests:** `DiagnoseProvisionerTests` — the ten `CheckInterpreterProvisionerTests` cases against the diagnose spec (row shape: `AlwaysOn`, `Low`, `IsPoolDelegate=false`, `RemoteControlEnabled=false`, `SystemPromptAppend == Diagnosis.Contract`; hook file content; idempotent; recreate after delete; reconcile a drifted prompt; heal a swept directory; disabled → null; directory derivation with/without roots; version label matches the bundle); `DiagnosisTests` — table over `TryParseTitle` (quotes stripped, trailing stop stripped, 2/8/10/11 words, 80/81 chars, two lines, marker, equals fallback, CARD prefix added / already present / pushes past 100) and `TryParseLabels` (every valid combination, case/whitespace variants, `unclear`, prose, two lines, `complexity=hard` alone) and `ClampInput` (head+tail with the elision marker); `InstructionBundleTests` (key set, forward, HARD RULES sentences, `≤ 3 000`, opening line, version label).

### S3 — job 1: the queue, the worker, in-place titles (~4 h)

**Files:** `DiagnoseQueue.cs` (`Channel<DiagnoseRequest>`; `DiagnoseRequest` is a small record `{Kind, TaskId?, CardId?, Forced}`); `server/Infrastructure/Orchestration/DiagnoseHostedService.cs` (ensure at startup, serial drain, per-request scope); `DiagnoseService.cs` (`RunTitleAsync`: gates → runner → parse → apply → ledger; `RunCardAsync` lands in S4 but the dispatch switch is here); `Domain/Entities/Diagnosis.cs` + enums, `AppDbContext` map, migration `AddDiagnoses` (D7 — created here because job 1 already needs the ledger); `AgentTaskService.CreateAsync` (enqueue + DTO flag); `AgentTaskCreatedDto.TitleDiagnosisQueued`; `scripts/delegate.ps1` (warning clause + `title: pending` line); `client/src/api/agentTasks.ts` (DTO field).
**Tests:** `AgentTaskAutoTitleTests` (harness from `AgentTaskCheckInterpreterTests`): no title + 90-char first line → request queued, DTO flag true, row title is the fallback; no title + 40-char Goal → not queued, flag false; explicit `-Title` → not queued; a Check/Diagnose row → never queued; a good answer settles → title replaced, `Diagnosed` event names the diagnose task and cost, `AgentTaskChanged` published, ledger `Applied`; card-bound task → `CARD-nnnn ` prefixed when absent; answers of 3 lines / 91 chars / one word / containing `[antiphon-task:` / equal to the fallback → title untouched, ledger `RejectedGate` with the reason; timeout → the seat's row cancelled if Queued, title untouched, `DegradedTimeout`; `Failed` / empty result → `DegradedFailed` / `DegradedEmpty`; alias held (fake `IModelAvailability`) → no seat row created, `DegradedHeld`; budget spent (seeded Diagnose rows totalling `≥ 2.00` today) → `DegradedBudget`, no row; backlog at 2 → `DegradedBusy`; `DiagnoseEnabled=false` → create is byte-identical to today (no flag, no request, no ledger row); a title changed by something else before apply → `SkippedAlreadyTitled`; the unavailable incident dedups per minute. `DelegateScriptTitleTests`: stub returns `titleDiagnosisQueued: true` → output contains `title: pending`; false/absent → unchanged output (the CARD-0351 cases stay byte-identical).

### S4 — job 2: the sweep, label apply, tracker preservation, on-demand (~6–7 h)

**Files:** `CardDiagnosisLabels.cs` (D4 statics); `CardDiagnosisSweep.cs` (`SelectAsync` with the backoff query); `server/Infrastructure/Orchestration/DiagnoseSweepHostedService.cs`; `DiagnoseService.RunCardAsync`; `CardService.ApplyDiagnosisAsync`; `ExternalTrackerSyncService` (union on import); `TrackerBidirectionalSyncService` (exclude on export, both sites); `CardEndpoints` (`POST /{id}/diagnose`); `DiagnosisEndpoints.cs` (`GET /api/diagnoses`, `/stats`) + DTOs; `scripts/card.ps1` (`diagnose` verb, `-NoWait`); `docs/antiphon-api.md`, `docs/ops-http.md`.
**Tests:** `CardDiagnosisSweepTests`: selection — Backlog/InProgress/Review/NeedsDecision in, Done/Canceled/archived card/archived board out; a card with only `complexity:` still selected, with both out; ordering (status, importance, urgency, created) and the batch cap; backoff — a row 1 h old excludes, 25 h old includes, three non-Applied rows exclude until `UpdatedAt` moves; `DiagnoseSweepEnabled=false` → no requests. `CardDiagnosisApplyTests`: both labels appended at the end, other labels untouched, one `ContentEdit` revision with the reason and `EditedBy = "antiphon-diagnose"`, `ConcurrencyToken` changed, `CardChanged` published, ledger `Applied`; a human-added `ui:yes` in the meantime → only `complexity:` added and the human's value kept; `Forced` → diagnosis labels replaced, topic labels kept; Shadow → ledger `Shadowed`, `LabelsJson` byte-identical, no revision; `unclear` → `Unclear`, no write; prose → `RejectedUnparseable`; a stale token / duplicate revision number → `RejectedConflict`, card untouched; held / budget / busy / timeout as in S3. Tracker: import with a differing label set keeps the card's `complexity:`/`ui:` labels; export to an `AntiphonExport` issue never sends them. Endpoints: `POST /diagnose` 202 + queued request; 404 unknown card; 409 disabled; `GET /api/diagnoses?cardId=` newest first; `/stats` counts by kind × outcome match seeded rows. Script: `card.ps1 diagnose CARD-0001` against the stub posts to the right route and prints the ledger row (or `-NoWait` prints the 202).

### S5 — docs, skill, live verification, the first sweep (~2–3 h)

1. Docs: `docs/orchestration-loop.md` — a "Diagnose seat" section (what it does, the two triggers, every knob, the ledger endpoints, `card.ps1 diagnose`, the hold/budget behaviour, and that titles/labels are best-effort metadata); `docs/agent-kinds.md` — name the second piece of furniture and add the seat to the subscription-quota bypass line; `docs/agent-card-lifecycle.md` — the `complexity:`/`ui:` label families and who writes them; `docs/testing-and-build.md:30` — the test-host guard now names both seats; `server/Bundles/README.md` — the diagnose bundle and its pinned sentences; `server/Bundles/orchestrator.md` + `.claude/skills/antiphon-delegate/SKILL.md` — the consumer hint (D8): read a card's `complexity:*` label as the default `-Complexity`; `-Title` stays mandatory advice ("a diagnose pass titles what you forget, a minute late").
2. Live, from the main checkout (the worktree refusal rule applies to restarts): deploy; confirm `antiphon-diagnose` is provisioned in `C:\logs\antiphon\diagnose` and warm (`GET /api/agents`); dispatch a `-ExpectAbout 1` delegate with a 200-char one-paragraph Goal and no `-Title`, see `title: pending`, and within ~60 s see the row's title replaced and the `Diagnosed` event; set `DiagnoseLabelMode=Shadow`, `card.ps1 diagnose` three cards of visibly different size (CARD-0351-shaped small, CARD-0330-shaped large, CARD-0303-shaped question) and read the ledger; flip to `Apply`, re-run one, confirm the two labels, the revision, and the board chip order; simulate a hold (`model-availability.ps1` manual hold on `haiku`), confirm a tick creates no row and the ledger says `DegradedHeld`, clear it; let the sweep run its first hour and record in `docs/orchestration-findings.md`: cards labelled, label distribution, p50/p90 wait, total cost, reject/unclear rate.
3. Board: comment on CARD-0303 that CARD-0352 shipped the concrete first jobs (its "Resolved" note already points here); CARD-0332's next Plan pass reads `CardDiagnosisLabels.Complexity(labels)` as its default when a dispatch carries no `-Complexity`.

---

## Test matrix

| Concern | Where |
|---|---|
| No `AgentTaskRole.Check` comparison outside the allowlist | `SpecialistRoleContractTests` (S1) |
| Every Check carve-out holds for Diagnose | parameterised existing tests (S1) |
| Interpreter unchanged through facade + runner | existing `AgentTaskCheckInterpreterTests`, `CheckInterpreterProvisionerTests` (S1) |
| Seat row shape, hook, reconcile, heal, disabled, directory | `DiagnoseProvisionerTests` (S2) |
| Title/labels grammars, every edge; input clamp | `DiagnosisTests` (S2) |
| Bundle key, forward, HARD RULES, length, version | `InstructionBundleTests` (S2) |
| Queue/flag/apply/reject/timeout/held/budget/busy/disabled/already-titled | `AgentTaskAutoTitleTests` (S3) |
| `title: pending` line; CARD-0351 cases byte-identical | `DelegateScriptTitleTests` (S3) |
| Sweep selection, ordering, batch, backoff, switch | `CardDiagnosisSweepTests` (S4) |
| Label append, revision, token, event, human-wins, forced, shadow, unclear, conflict | `CardDiagnosisApplyTests` (S4) |
| Import keeps / export drops diagnosis labels | tracker sync tests (S4) |
| `/diagnose`, `/api/diagnoses`, `/stats`, `card.ps1 diagnose` | endpoint + script tests (S4) |
| Test hosts never launch the seat on the production runner | `ProductionRunnerIsolationTests` (S2) |
| The whole path against a real seat, both modes, a held alias | S5 live run |

Run with the forward-slash isolated output and delete it afterwards:

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0352/ -- --treenode-filter "/*/*/SpecialistRoleContractTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0352/ -- --treenode-filter "/*/*/AgentTaskCheckInterpreterTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0352/ -- --treenode-filter "/*/*/DiagnoseProvisionerTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0352/ -- --treenode-filter "/*/*/DiagnosisTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0352/ -- --treenode-filter "/*/*/AgentTaskAutoTitleTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0352/ -- --treenode-filter "/*/*/CardDiagnosis*Tests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0352/ -- --treenode-filter "/*/*/InstructionBundleTests/*"
```

S1 additionally runs the whole Application namespace once (`--treenode-filter "/*/Antiphon.Tests.Application/*/*"`, ~12 min, chunked) because the predicate change touches fifty sites. Never widen a wait or loosen a `RequestCount == 0` assertion to pass.

---

## Sequencing and risks

- **CARD-0330 collision.** Both plans extract the same provisioner and predicate. Before S1 starts, read `git log --grep CARD-0330` on master; if its S1/S2 landed, S1 here is additive only. If both are in flight at once, this card takes `-Worktree` and rebases; the enum values (`Distill=12`/`Diagnose=13`, events 25/26, incidents by reading the enum) are pre-assigned so the merge is textual, not semantic.
- **Haiku titles that drop the CARD id** are the expected failure; the code prefix (D3) makes it harmless. Haiku titles that are *wrong* (name the card's topic rather than the task's job) are the ledger's first question; the fix is a v2 contract sentence, by PR.
- **Label quality before CARD-0332 routes on it.** Until the matrix ships, a wrong `complexity:` costs nothing but a human edit. Before CARD-0332 flips routing onto labels, its plan should read `/api/diagnoses/stats` and the human-overwrite rate (a human edit of a diagnosis label is visible as a `ContentEdit` revision after an `antiphon-diagnose` one) and decide whether the label is trustworthy enough to be a default.
- **Seat context growth.** ~4–8 KB per request, ~150 requests/day at the start → Claude Code auto-compacts roughly daily. The contract rides `--append-system-prompt`, so it survives compaction; the interpreter has lived this way since CARD-0047. Specialist rows are never compacted on reuse (dispatcher `:3847`) — that carve-out becomes `IsSpecialist` in S1.
- **The sweep on a cold deploy** creates up to 5 rows per 10 min; the seat's first launch after deploy is cold (the interpreter's cold p90 was 183 s), so the first tick may see 3–5 `DegradedTimeout` rows. They back off 24 h and the next tick takes the next five. Acceptable; if S5's ledger shows a cold-start cluster, raise `DiagnoseWaitSeconds` for the first tick after `EnsureAsync` created the row (one boolean), not globally.
- **Two seats share the quota bypass** (`StartAsync(IgnoreSubscriptionQuota: true)` in the provisioner) — deliberate, same reason as the interpreter; `agent-kinds.md`'s quota line lists both.
- **Revision noise.** Every Applied labelling adds one revision and bumps `RevisionCount`, so ~120 cards will show the "edited" affordance after the first sweep. That is the honest record of a change; a future UI card may want to fold `antiphon-diagnose` edits into a quieter style, which is not this card's call.

---

## Not this card (on purpose)

- Choosing workflow, path, model, tier, kind or worktree for a card or task; deciding Investigate-vs-Plan; WIP shaping (CARD-0303's broader framing — future scope only after these two jobs have a ledger behind them).
- Writing `AgentTask.Complexity` from a card label, or any dispatcher change (CARD-0332 consumes the label).
- The UI-deferral policy itself; badges or a picker for the labels (CARD-0333); a diagnose column or attention kind.
- Card titles or descriptions (only *task* titles are rewritten, only when no title was given).
- Back-labelling Done/Canceled cards; labelling comments/discussion; a second title pass after `refine`.
- A self-improving prompt loop (CARD-0330's D8, on its own evidence); an LLM judge; a second seat.
- Tightening `BuildTitle`'s 300 clamp or making `-Title` mandatory (CARD-0351's decisions stand).
- Notifying any channel or human about a diagnosis.
