# Review-column triage (board `8988ca03-7414-47ad-b0b6-51556c701703`)

**Date:** 2026-09-05
**Scope:** the 13 cards named in the Investigate brief (the brief said 12; the list has 13). All were `status=Review` via `scripts/card.ps1 get -Json` at 2026-09-05.
**Status:** triage complete. No product code was changed.
**Method:** `scripts/card.ps1 get` + `history -Json` per card (board `8988ca03-7414-47ad-b0b6-51556c701703`); `git log --all --grep` and `git merge-base --is-ancestor` against `origin/master` (`2db71726`); plan files under `docs/superpowers/plans/`.
**HEAD:** `2db71726` = `origin/master`.

Buckets as the brief defined them:

- **A** — Plan/Code/Execute settled Succeeded, real work on master (or an unmerged branch), closer can verify and close.
- **B** — a Plan settled with a real plan artifact; Code has not finished the plan (never started, or started and left remaining slices).
- **C** — no completed Plan/Code behind the Review move.

None of the thirteen is C. Every Review move in this set is a `card-transitions` settlement (`Task <id> (<Role>) settled Succeeded; no other task is open against this card.`).

---

## Table

| Card | Bucket | Evidence (task ids / commits / plan path) | One-line note |
|---|---|---|---|
| CARD-0330 | B | Plan `1e51696c` Succeeded 2026-09-03 02:50Z (history r2). Plan commit `633e4ba3` on master. Path: `docs/superpowers/plans/2026-09-03-card-0330-output-distiller-plan.md`. No Code task. | Distiller seat + PR-gated prompt loop designed, not built. Plan is one addendum behind: CARD-0146 S5 (history r3, 2026-09-05) requires the distiller to fail if it drops `next:`/`handoff:`. |
| CARD-0124 | A | Plan `b0f9c96b` Succeeded; Code `2aab4b1c` Succeeded 2026-09-04 (history r5, r7). Master: `8bf3eeac` (S1+S4), `829e516a`, `75f61035`. Plan: `docs/superpowers/plans/2026-09-04-card-0124-overnight-windmill-nightly-card-plan.md`. | Isolated nightly runner + card-on-red is live: CARD-0378 was filed by the first live run (2026-09-04 17:48, sha `829e516a`) and is now Done. AGENTS.md names Windmill `u/lndcobra/antiphon_nightly_tests`. Closer should confirm the 00:30 schedule (plan S3 "manual trigger does not substitute") then close. CARD-0131 is already Done (absorbed). |
| CARD-0095 | B | Plan `f0e9380f` Succeeded (history r3). Plan commit `21aff43b` on master. Path: `docs/superpowers/plans/2026-09-03-card-0095-orchestrator-panel-stats-plan.md`. No Code task. | Operator 2026-09-04 note says "merge with CARD-0092 rather than a second patch"; the plan already did that for Running/runtime. Remaining defect is still real: Cost/Tokens/`RetrySchedule` ignore `AgentTask` spend and delegate requeues. Dispatch Code S1–S3; do not close as duplicate of 0092. |
| CARD-0355 | B | Plan `d1a21387` Succeeded 2026-09-03 19:09Z (history r2). Plan on master `ae3bcaee`. Path: `docs/superpowers/plans/2026-09-03-card-0355-grok-tui-memory-queue-plan.md`. No Code. | Headed Grok queue-pane canary, not a new JSONL kind. Parent survey `c9f4b2c8` (`docs/investigations/2026-09-03-card-0156-grok-codex-composer-queue-survey.md`). Not urgent; plan is current. |
| CARD-0098 | B | Plan `31bc9be7` Succeeded; Code `78355a66` Succeeded (history r4, r6). Master: plan `610c11d4`, S1 `499a318b`. Path: `docs/superpowers/plans/2026-09-03-card-0098-within-tier-position-plan.md`. | S1 only: `Card.Position` + `OrderKey` on every sort site. S2–S6 (PATCH `/position`, `card.ps1 reorder`, board drag, bulk order, Backlog-box drag, rollout) have no commits. `scripts/card.ps1` has no `reorder` verb. Dispatch Code from S2. |
| CARD-0251 | B | Plan `6b4bbfa2` Succeeded; Code `cc9c7441` Succeeded (history r3, r6). Master: plan `c7073ad1`/`2aa47b96`, S0 `aee1f5cf`, S1 `36bad3fa`. Path: `docs/superpowers/plans/2026-08-30-card-0251-orchestrator-workspace-tooling-plan.md`. | Classifier + canaries only. S2–S5 (readiness check, launch incident, `orchestrator-workspace.ps1`, Gym Stat sibling migration) unbuilt: `OrchestratorWorkspaceAcknowledged` exists only in the plan. Dispatch Code from S2. S6/S7 stay gated. |
| CARD-0329 | B | Plan `2fc88d9a` Succeeded 2026-09-02 22:43Z (history r2). Plan on master `8e17bd84`. Path: `docs/superpowers/plans/2026-09-02-card-0329-nudge-postdate-gate-plan.md`. No Code. | Mechanism reproduced (nudge `CreatedAt` 22:34:54.739Z vs reply boundary 22:35:20.739Z vs `SentAt` 22:35:20.766Z; classifier refused). Plan is current. Dispatch Code (CreatedAt vs SentAt gate). |
| CARD-0133 | A | Plans `d1fd0051`/`714fec2c` Succeeded; Code `f12bceb3`/`7dacd3a1`/`fc8e1d49` Succeeded (history r4, r8, r14, r16). Master: S0 `13c1e9d4`/`5fa480c8`, paste-burst `f77d0d63`, S1 `09a6a8ba`, S1b-A+D `6c1def2f`. Plans: `docs/superpowers/plans/2026-08-27-card-0133-codex-readiness-and-boot-wedge-plan.md`, `docs/superpowers/plans/2026-09-02-card-0133-s1-positive-submit-evidence-status-plan.md`. | Original "Enter is dead" defect shipped. CARD-0299 (Done 2026-09-04) absorbed original S2–S4 + the S1 latch hole. Remaining S1b-C is the closer's census checkpoint (`scripts/codex-boot-census.ps1` still lacks the BootWedged SELECT the S1b plan named) — run it, then close; a one-line census tweak is optional, not a new feature pass. |
| CARD-0094 | B | Plan `567896e0` Succeeded; Code `df43bed4` Succeeded (history r3, r5). Master: plan `60f695c5`, S1 `d70c557a`. Path: `docs/superpowers/plans/2026-09-02-card-0094-backlog-by-quadrant-plan.md`. | S1 shipped the four quadrant boxes (`BacklogSection.tsx`). S2 (MoveMenu on a row) and S3 (contract fixture, stories, screenshots, docs, browser check) are absent — `BacklogRow.tsx` has no `MoveMenu`. Dispatch Code from S2. Box-internal drag is CARD-0098 S5, not this card. |
| CARD-0090 | B | Plan `91e797fa` Succeeded; Code `05e34c05` Succeeded (history r4, r7). Master: S1 `2eabe706`, S2 `11e7bf6c`, S3 `2c246c0a`, S4 `5f73b9ee`. Path: `docs/superpowers/plans/2026-09-02-card-0090-complexity-chains-plan.md`. | S1–S4 landed, including the list-in walker CARD-0322 asked for (`RoutingCandidates.Compose` + `WalkCandidatesAsync` in `ComplexityRoutingService.cs`). S5 (reactive `RerouteOnWallAsync`) is unbuilt: `git grep RerouteOnWall` on `*.cs` is empty; S4 commit message says "S5 (wall reroute) is a follow-up PR on this card." Dispatch Code for S5 only. Do not close. |
| CARD-0322 | B | Plan `084537e1` Succeeded 2026-09-02 12:15Z (history r3). Plan commit `ce44ab8b` on master. Path: `docs/superpowers/plans/2026-09-02-card-0322-routing-pin-candidates-plan.md`. No Code. | **Not superseded by CARD-0090.** 0090 is the walker; 0322 is the second list source (`RoutingPin.CandidatesJson`). 0090's own plan filed 0322 as the split-out piece. Blocker (0090 S1–S3) is now Done. Dispatch Code; it does not wait on 0090 S5. See §CARD-0090 / CARD-0322 below. |
| CARD-0022 | A | Debug `2ce34da8` Succeeded; Plan `8dbd3497` Succeeded; Code `4a207596` Succeeded 2026-09-01 18:06Z (history r4, r7, r9). Master: plan `988012f8`, code `083081bf` (S0–S4 + S6: parser, `ModelAvailabilityHold`, skip/409, attention, docs). Path: `docs/superpowers/plans/2026-09-01-card-0022-per-model-usage-limit-pause-plan.md`. | Per-model pause is the card as redesigned. S5 headed `/usage` canary was never added (`ClaudeUsageCommandCanaryTests` exists only in the plan); plan forbade a live poller in v1. CARD-0309/0335 wrote onto the same table afterwards. Closer can close; leftover S5 is optional. |
| CARD-0262 | B | Plan `b2c3020b` Succeeded 2026-08-31 00:15Z (history r2). Plan commit `f8ddd330` on master. Path: `docs/superpowers/plans/2026-08-31-card-0262-kb-preference-to-agent-instructions-plan.md`. No Code. | Plan's finding still stands: Antiphon has no KB; the Slack "always give me pdf" row lived in a foreign project store. Design is pinned per-agent instructions, not a sync. Not superseded. Dispatch Code. |

---

## CARD-0090 / CARD-0322 (dependency order)

Read both cards and both plans. They are related; they are not duplicates.

| | CARD-0090 | CARD-0322 |
|---|---|---|
| Ask | Hard/Medium/Easy **complexity** chains: ordered `(kind, level)` fallback, Block when exhausted | **Routing pins** (stage/card, CARD-0305) grow from one pair to an ordered candidate list |
| Who filed 0322 | CARD-0090 Plan `91e797fa`, as the piece deliberately split out | — |
| Shared mechanic | `WalkCandidatesAsync` over a composed list | Same walker; a pin with N≥2 is the second list source |
| What 0090 shipped | S1–S4 on master, including `RoutingCandidates.Compose` + `WalkCandidatesAsync` (`server/Application/Services/ComplexityRoutingService.cs:14,181,197`) | nothing (plan only) |
| What 0090 did **not** ship | S5 wall-time reroute; multi-candidate pins | — |

**Order:** CARD-0090 S1–S3 (Done) → CARD-0322 Code. CARD-0090 S5 is a separate remaining slice on 0090 and does **not** gate 0322. CARD-0322 must not be closed as "covered by 0090": a one-candidate pin is still today's CARD-0305 behaviour; a list pin is unbuilt.

0322 plan addendum on 0090 (empty default chains; auto-resume when capacity returns; stale `AgentTaskEventType` tail) was recorded on CARD-0090 history r5 before the Code pass.

---

## Counts

| Bucket | N | Cards | Orchestrator action |
|---|---|---|---|
| A | 3 | CARD-0124, CARD-0133, CARD-0022 | Verify the cited commits / live nightly / census, then close |
| B | 10 | CARD-0330, CARD-0095, CARD-0355, CARD-0098, CARD-0251, CARD-0329, CARD-0094, CARD-0090, CARD-0322, CARD-0262 | Dispatch Code (remaining slices named in the table). 0322 after 0090 S1–S3 (already landed). 0330 Code must take the CARD-0146 handoff-block addendum. |
| C | 0 | — | — |

Partial Code (0090 S1–S4, 0094 S1, 0098 S1, 0251 S0–S1) is **B**, not A: a Succeeded Code task that left named slices on the same card is not "just needs a close."

---

## Uncertainties

1. CARD-0124 S3's "proven to fire" bar was a *scheduled* 00:30 run. CARD-0378's first filing was 17:48 on 2026-09-04 from `origin/feat/card-task-2aab4b1c` (a Code-task run, not the cron). Closer should look at Windmill `u/lndcobra/antiphon_nightly_tests` run list / `C:\Antiphon\nightly\logs\` before writing the close reason. Does not change the A bucket: the job and the filing path exist.
2. CARD-0133 S1b-C census SELECT was not verified by executing `scripts/codex-boot-census.ps1` against live Postgres in this pass; the script file on master still has no `BootWedged` column in the SQL (read `scripts/codex-boot-census.ps1:23-39`).
3. CARD-0022 S5 (`/usage` headed canary) was not run. Catalog still treats remaining quota as unreadable. Closing A accepts that as out of v1, which matches the plan's own "no live sweep in v1."
4. No C cards. If a card reached Review by a manual move, it is not in this thirteen.

## Not done, noted

No fix. Orchestrator closes A and dispatches Code for B as named.
