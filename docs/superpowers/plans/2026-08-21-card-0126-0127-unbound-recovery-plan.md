# CARD-0126 + CARD-0127 — unbound-session recovery: verify the evidence, mark the settlement

**Date:** 2026-08-21 · **Cards:** CARD-0126 (false "Succeeded in 10 minutes" durations), CARD-0127
(Codex task Succeeded on zero work, backed by a wrong-tool recovered transcript) · **Status:** plan
(no implementation in this pass) · **Verified against:** master `2f325c6`, live dev DB on 17280, and
the actual wrongly-attached transcript file on this machine.

## Verdict

One fix, two properties, both in the CARD-0085 recovery path — exactly as both cards predicted:

1. **CARD-0127 (evidence verification, the severity driver):** the JSONL arm of
   `DelegateBindRefusalRecovery` attaches "the work is at `<file>`" on evidence weak enough that a
   *different task's* transcript qualifies. Three gates close it, each one validated below against
   the live `753cdb4e` instance and the 14 presumed-genuine recoveries: **(a)** the JSONL arm runs
   only for `AgentKind.ClaudeCode` tasks — for any other kind a hit under `~/.claude/projects` is
   *guaranteed* wrong-tool; **(b)** a C1 arm — a candidate whose filename is another
   `AgentSessions.Id` is some other session's own conversation, refuse it; **(c)** needle
   tightening — drop `Needle.Card` from the JSONL arm entirely and require the surviving needles
   (full task marker, bounded short-id) to match a **user-type record**, i.e. the brief this task
   actually sent was typed into that session (C4-shaped positive evidence), not merely mentioned by
   whoever was talking nearby. With no verifiable evidence, `TryFindAsync` returns null and the
   sweeps fall through to their existing honest Failed (`AgentTaskDispatcher.cs:466`/`:483`) — never
   Succeeded on unverified evidence. The recovery path itself is **kept**: 14 of the 15 recovered
   rows in the live DB look genuine, and both cards forbid suppressing it.

2. **CARD-0126 (distinguishable settlement):** a new nullable `AgentTask.RecoveredAt` column,
   stamped by `RecoverFromBindRefusalAsync` alongside `CompletedAt`, carried through
   `AgentTaskSummaryDto` to the client, backfilled for the 15 existing rows by
   `Result LIKE 'Recovered from an unbound session%'` so the string filter dies with this migration.
   Non-null means "settled by recovery at this instant; `CompletedAt` is a settlement time, not an
   observed finish". The UI labels rather than hides (the `CostPricingVersion == 0` /
   `isLegacyCostEstimate` precedent), and any future duration analysis filters
   `"RecoveredAt" IS NULL` instead of string-matching prose.

## Ground truth this plan stands on

All reads on master `2f325c6` unless stated; DB facts from the live dev Postgres (17280) on
2026-08-21.

- **The settlement:** `AgentTaskReplyService.RecoverFromBindRefusalAsync`
  (`server/Application/Services/AgentTaskReplyService.cs:517`) unconditionally writes
  `Status = Succeeded`, `Result = "Recovered from an unbound session; work is at
  {evidence.Describe()}…"`, `CompletedAt = now` (`:538–:541`). No field distinguishes it from an
  observed completion; no check relates `evidence` to this task's session, kind, or tool.
- **The evidence selection:** `DelegateBindRefusalRecovery`
  (`server/Application/Services/DelegateBindRefusalRecovery.cs`). Two arms, either alone suffices
  (`TryFindAsync`, `:50`): a git arm (commits after `DispatchedAt` matching card-id needles, or any
  commit on a worktree task's own branch) and a JSONL arm (`TryScanJsonl`, `:102`) that scans
  **Claude's** projects root for *any* kind of task, checks C2 (cwd equality, `:188`) and C3 (first
  *timestamped* record not older than session start − 2 s, `:201`), then accepts the first file
  where any needle matches **any record** (`:207`) — assistant and tool output included. Needles
  (`DistinctiveNeedles`, `:214`): bounded short-id, literal `[antiphon-task:…]` marker, and **every
  `CARD-\d+` found in Title/Goal**. C1 and C4 are absent by design (the class doc says so).
- **The three callers**, all funneling through `AgentTaskDispatcher.TryRecoverBindRefusalAsync`
  (`AgentTaskDispatcher.cs:1697`): the 10-minute delivery watchdog (`:435`), the dead-session sweep
  (`:729`, gated on zero `TranscriptEntries`), and overdue gate 3 (`:873`). On a null from
  `TryFindAsync` each falls through to its existing Failed.
- **The `753cdb4e` failure chain, now fully measured** (this plan's new evidence — the card knew the
  outcome; these are the mechanics):
  - Task `753cdb4e` (AgentKind **Codex**), session `2ff8f0b9`, cwd `C:\src\Antiphon`, StartedAt
    **18:05:13Z**, settled 18:15:26Z — 10.2 min, the watchdog's mark.
  - The attached file `cc704d7c-….jsonl` is **task `ef6c0bd0`'s `AgentSessionId`** — the sibling
    Claude delegate's own conversation (Antiphon launches Claude with `--session-id
    <AgentSessionId>`, so the filename *is* the session id). One `AgentSessions` lookup would have
    refused it. That is the missing C1.
  - **C2 passed vacuously**: both delegates ran Shared in `C:\src\Antiphon`. For shared-workspace
    pool delegates — the default — cwd equality excludes almost nothing.
  - **C3 passed legitimately but weakly**: `cc704d7c`'s session started 18:04:43Z (30 s *before*
    `2ff8f0b9`), but the file's lead records (`last-prompt`, `custom-title`, `agent-name`, `mode`,
    `permission-mode`) carry **no timestamp**, so the first *timestamped* record is 18:05:55Z —
    42 s after `2ff8f0b9`'s launch. C3's clock starts after the untimestamped lead; two delegates
    dispatched within a minute of each other sail past it.
  - **The matching needle was `CARD-0010`** — `753cdb4e`'s Goal says "recorded on the now-closed
    CARD-0010" in passing, and `ef6c0bd0` (planning CARD-0102, the E2E-isolation card — same
    territory) mentions CARD-0010 in a record at 18:06:16Z. The strong needles — short-id
    `753cdb4e`, full marker — appear **zero times** in the file (verified by grep). A card id
    matches whoever *talks about* the card; it is not evidence of doing this task's work.
- **The 15 recovered rows** (`Result LIKE 'Recovered from an unbound session%'`): 14 are ClaudeCode;
  9 are worktree tasks whose pointer is under the task's own
  `C--Antiphon-worktrees-card-task-<shortid>` project dir (per-task cwd makes C2 genuinely strong
  there); **`753cdb4e` is the only row whose pointer filename is another `AgentSessions.Id`**
  (checked against the full sessions table). So the proposed C1 arm catches exactly the one bad row
  and none of the presumed-genuine ones — measured, not hoped.
- **Duration consumers:** client `taskVisuals.elapsedSeconds`
  (`client/src/features/delegations/taskVisuals.ts:93`) → `TaskChip.tsx:119`, `TaskDrawer.tsx:140`
  ("Elapsed" metric); ad-hoc SQL analyses (the CARD-0109 report §2.1(a) had to filter by `Result
  NOT LIKE …` prose). Nothing server-side schedules or decides off `CompletedAt − DispatchedAt`
  today — the fix is a labeling/queryability fix, not a behavior fix.
- **Existing pins:** `AgentTaskDeliveryWatchdogTests` (recovery cases at `:352–:545`, with a
  `ClaudeProjectsRoot` test seam), `AgentTaskDeadSessionReconciliationTests`,
  `AgentTaskOverdueDeadlineTests`. Note
  `zero_transcript_plus_later_jsonl_needle_recovers_without_ingesting` currently plants the marker
  in an **assistant** record — slice 1 moves it to the user record (see S1, and why reality still
  matches).

## Design

### S1 — verify before attach (CARD-0127)

All in `DelegateBindRefusalRecovery`, plus one query handed in by the caller.

**S1a — agent-kind gate.** `TryScanJsonl` returns null unless `task.AgentKind ==
AgentKind.ClaudeCode`. The scan enumerates Claude's per-cwd projects root and nothing else; a Codex
task's real transcript lives under `~/.codex/sessions/…`, Grok's elsewhere again — for any
non-Claude kind, *every possible hit is wrong-tool by construction*. The git arm stays for all
kinds: commits are tool-agnostic evidence, and it is the honest recovery route left for Codex.
(A symmetric Codex rollout-scan arm is deliberately out of scope — below.)

**S1b — C1: not another session's conversation.** Parse the candidate filename stem as a Guid; if
it equals any `AgentSessions.Id` other than this task's own `AgentSessionId`, refuse the file. The
task's *own* session id as filename is the strongest possible match and stays acceptable (that is
"our transcript existed but was never bound/ingested"). Plumbing: `TryFindAsync` gains the set of
known-other session ids (the three call sites all sit on `_db`; a
`HashSet<Guid>` built from `AgentSessions.Select(s => s.Id)` per recovery attempt is fine — the
sweep fires a handful of times a day on unhealthy tasks only). Keeping the check inside the
recovery class keeps all four rules (kind, C1, C2, C3) in one place with one test seam.

**S1c — needle tightening on the JSONL arm.** Two changes, one arm:
- **Drop `Needle.Card` from the JSONL arm.** Card ids are shared across tasks by design (multiple
  slices of one card, plan docs, incidental greps); the measured failure matched on exactly this.
  The git arm keeps card needles — a commit message citing the card after `DispatchedAt` is real
  landed-work evidence, and commit messages never carry task markers (`fix(CARD-0107)` is the repo
  convention), so card ids are the only needle git has.
- **Require the match in a user-type record** (`"type":"user"`), for the surviving needles (full
  marker, bounded short-id). Rationale: the brief is *typed into* the delegate's session and lands
  as a user record carrying the marker (CARD-0055 measured that even a collapsed paste's JSONL
  record carries the full body) — so a genuine unbound-own-transcript *necessarily* qualifies. An
  assistant/tool record mentioning the task is what a *neighbor* produces: the parent's completion
  notes, a sibling that read `.antiphon/task-<id>.md`, a grep whose output captured the marker.
  The existing test plants the marker in an assistant record only because its fixture is minimal;
  real recovery targets always contain the brief. Update the fixture, keep the test's contract
  (recovers without ingesting, C4 stays refused for *binding* purposes).

C2 and C3 stay as they are. Both were shown weak in isolation (C2 vacuous for shared cwd, C3
defeated by the untimestamped lead) but they still exclude cheaply, and the new gates don't lean on
them. Deliberately **not** "fixing" C3 to use file mtime or the untimestamped lead: the lead records
are rewritten by Claude on activity, and CARD-0006's C3 semantics ("first *timestamped* record")
are shared with `TranscriptTailer` — diverging the two C3s invites worse confusion than the gate
this plan adds.

**Post-fix behavior for the `753cdb4e` shape:** kind gate kills the JSONL arm, git arm finds
nothing (verified: no commit ever touched the briefed files), `TryFindAsync` returns null, the
delivery watchdog writes its existing honest Failed — "Boot prompt was never delivered …" — and the
caller redispatches with a clean conscience. That is the exact verdict CARD-0127 asks for.

### S2 — distinguishable settlement (CARD-0126)

- **Entity/migration:** `AgentTask.RecoveredAt` (`DateTime?`, null for every ordinary settlement).
  Named for the path that writes it (`RecoverFromBindRefusalAsync`, `Result` prose "Recovered
  from…"), not "watchdog", because three different sweeps reach it. Migration
  `AddAgentTaskRecoveredAt` with backfill:
  `UPDATE "AgentTasks" SET "RecoveredAt" = "CompletedAt" WHERE "Result" LIKE 'Recovered from an
  unbound session%';` — 15 rows today; after this, no consumer ever string-matches the prose again
  (the CARD-0067 `ChannelReplySettledAt` backfill is the precedent for stamping history in the same
  migration that adds the column).
- **Write site:** `RecoverFromBindRefusalAsync` sets `task.RecoveredAt = now` next to
  `CompletedAt = now` (`AgentTaskReplyService.cs:541`). `SettleAsync`, `FailUnreportedTurnAsync`,
  `HandleApiErrorTurnAsync`, `FailAsync` are untouched — observed settlements keep null.
  `CompletedAt` **stays stamped**: the board lane needs a settle instant; the column pair now reads
  "settled at X" + "and that was a recovery, not an observed finish".
- **DTO/API:** `AgentTaskSummaryDto` gains `DateTime? RecoveredAt`
  (`server/Application/Dtos/AgentTaskDtos.cs:54`, mapped at `AgentTaskService.cs:886`); client type
  in `client/src/api/agentTasks.ts` gains `recoveredAt: string | null`.
- **Client:** `taskVisuals.ts` gains `completionObserved(task): boolean` (`recoveredAt == null`);
  `TaskChip` and `TaskDrawer` render the duration with a `~` prefix and a tooltip ("recovered from
  an unbound session — completion was not observed; the delegate may have kept working") when it is
  false. Label, don't hide — same posture as `isLegacyCostEstimate`.
- **Analyses:** future duration work filters `"RecoveredAt" IS NULL`. The CARD-0109 report's §2.1(a)
  hazard note stays correct for re-runs against pre-backfill snapshots only.

### Slices

| # | What lands | Proven by |
|---|---|---|
| S1 | Kind gate + C1 + needle tightening in `DelegateBindRefusalRecovery`; callers pass other-session ids | New watchdog tests: a Codex task with a needle-matching Claude jsonl **fails with the `:466` reason** (the `753cdb4e` regression, named); a file whose name is another session's id is refused even when a needle matches; a card-id hit in an assistant record no longer recovers; marker-in-user-record still recovers. Updated fixture in `zero_transcript_plus_later_jsonl_needle_recovers_without_ingesting` (marker moves to the user record; contract unchanged). Existing worktree/git-arm tests (`:352`, `:424`) must stay green untouched — they pin the recoveries this plan preserves. |
| S2 | `RecoveredAt` column + backfill migration, write site, DTO, client label | Watchdog test asserting `RecoveredAt` set on recovery and null on ordinary settle; `taskVisuals` unit tests for `completionObserved`; TaskChip/TaskDrawer render tests for the `~`+tooltip; migration backfill verified against the dev DB (15 rows gain the stamp, everything else null). |

S1 and S2 are independently landable in either order; S1 first — it is the severity driver (a
false Succeeded outranks a mislabeled duration).

Test runs: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-<name>/`
(forward slash; delete the `bin-<name>` dirs after), `--treenode-filter` on the three watchdog test
classes; client via `pwsh -File scripts/test-client.ps1`.

## CARD-0127's open questions, answered

- **Where the fallback lives and how it chooses:** `DelegateBindRefusalRecovery.TryScanJsonl` —
  first file in enumeration order under the cwd-encoded Claude project dir that passes C2/C3 and
  matches *any* needle on *any* record. It never looks at agent kind, never checks whether the file
  is another session's, and treats a card-id mention as equivalent to the task's own marker. All
  three weaknesses had to align to produce `753cdb4e`, and all three are closed by S1.
- **Same root mechanism as CARD-0117?** No — related exposure, different defect. Whatever kept
  `2ff8f0b9` at zero ingested rows (plausibly CARD-0117's Codex delivery territory, possibly a
  Codex transcript-observation gap — CARD-0108's rollout-verdict work is adjacent) is upstream and
  stays those cards' subject. This plan fixes what happens *downstream* of "zero rows": today the
  recovery path can convert that state into a fabricated success; after S1 it converts it into
  either verified success or the honest Failed.
- **Should Succeeded ever be reachable on "zero ingested transcript rows"?** Yes — with *verified*
  evidence. The 14 genuine recoveries (worktree commits, own-cwd transcripts with the brief in
  them) are precisely the false-Failed→redispatch-on-landed-work hazard CARD-0085 was built
  against, and CARD-0126 explicitly forbids suppressing the path. "Zero rows ⇒ always Failed" was
  considered and rejected; the fix is evidence discipline, not verdict inversion.

## Rejected alternatives

- **A distinct terminal status (e.g. `RecoveredSucceeded`).** `AgentTaskStatus` fans out across the
  client lanes, `STATUS_COLOR`, filters, and every switch on status; a new value is a breaking sweep
  through all of them for what is fundamentally *provenance*, not state. The nullable timestamp is
  invisible to every existing consumer until one opts in — and the card explicitly allows either.
- **A bool instead of a timestamp.** Same cost, less information — the stamp records *when* the
  sweep settled it, which is exactly the number the duration question is about.
- **Suppressing or narrowing the recovery path to worktree-only.** Two of the presumed-genuine 14
  are shared-cwd JSONL recoveries; both cards forbid it; the false-Failed hazard is real
  (CARD-0056: acting on a false Failed is how live work gets killed or redone).
- **Fixing C3 (mtime, lead records) instead of adding gates.** Diverges from `TranscriptTailer`'s
  C3 semantics (CARD-0006) and still would not have saved `753cdb4e` on its own — the sibling's
  first *activity* genuinely postdates the launch. The kind gate and C1 are categorical; a
  sharpened clock is probabilistic.
- **Building a Codex rollout-scan evidence arm now.** Symmetric in principle
  (`~/.codex/sessions/YYYY/MM/DD/*.jsonl` carries cwd and text), but it is new surface with its own
  format assumptions, and the measured Codex failure had *no work to find* — a scan arm would have
  changed nothing. Codex tasks keep the git arm. If Codex delegation volume grows, this is its own
  small card.

## Deliberately not in scope

- Why `2ff8f0b9` ingested zero rows (CARD-0117 / CARD-0108 territory — Codex delivery and
  transcript observation).
- The 10-minute delivery window itself, check-in scheduling, and every other sweep clock.
- Retroactively re-verifying the 14 existing recovered rows' pointers — they are labeled by the S2
  backfill; re-adjudicating settled history buys nothing.
- `VerifiedPromptSubmitter` / boot-prompt scope (CARD-0055's standing scope-out).
- Duration analytics themselves (CARD-0109's own recommendations).

## Card housekeeping

- Both cards are one implementation effort; whoever implements should take both together (S1 ↔
  CARD-0127, S2 ↔ CARD-0126) and close both against this plan. Each card already points at the
  other; this plan is the merge point.
- CARD-0127's "what to investigate" bullets are all answered above — the card can carry a pointer
  here rather than re-deriving.
- The CARD-0109 report §2.1(a)/§5.1(b) hazard note is superseded by the `RecoveredAt` column once
  S2 lands (the `Result NOT LIKE` filter remains correct only for pre-migration snapshots).
- Adjacent finding worth its own card if not already filed: nothing in this investigation — the
  pty-suite flake found during CARD-0124 (`ClaudeDetectorsTests.cs:73`) is unrelated and already
  reported there.
