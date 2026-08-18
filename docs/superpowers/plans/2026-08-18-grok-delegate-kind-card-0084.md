# CARD-0084: Grok 4.6 as a delegate/worker kind — plan

**Date:** 2026-08-18
**Status:** planned (task 17cede3d)
**Builds on:** CARD-0080 S1/S2 (`fd8c99e`, `efb2790`), `2026-08-18-grok-first-class-acp.md`
**Coordinates with:** CARD-0083 (provider capability contract) — boundary in §5

## Verdict up front

Grok can become a delegate kind as an **explicit `-Kind Grok` opt-in**, not a Code/Debug
default, and not until slice S1 (Grok delivery shape) lands. The newline-drop bug is a
**real exposure for delegate briefs today** — the CARD-0025 file-spill path does NOT
currently protect them (§1) — but the fix is small and structural (force the spill for
Grok, single-line the pointer), not a Grok-side fix or a new delivery mechanism. Promotion
to role default is a config edit later (S2 adds the config seam), gated on real mileage
(§4). Total estimated effort: ~4–6 days across six slices, S1→S3 being the critical path
to the first real Grok delegate task.

## 1. Is the newline drop a blocker? (question 1)

**Measured facts** (grok 1.0.5, pinned by `GrokCanaryTests` / `FakeGrokContractTests`):

- A bracketed paste lands **intact and unclipped** — 4,450 chars sent, 4,389 recorded,
  the difference being exactly the 61 newlines. Every LF is dropped, lines join with
  **no separator**. A 4.4 KB *typed* body also lands whole: Grok has no CARD-0027/28
  one-chunk clip mode. So nothing is *lost* — content arrives complete, structure doesn't.
- Delivery verification already survives the join: S2's whitespace-free confirm arm
  (`SessionMessageQueueGrokPtyIntegrationTests.Multiline_delivery_is_transcript_confirmed_…`)
  confirms against the joined `UserPrompt` row. Deliveries won't strand or false-kill.

**Why the file-spill path does not currently save briefs:** `FitBriefForTyping`
(`AgentTaskDispatcher.cs:1032`) spills only above the ceiling `PtyDeliveryProfile`
resolves, and that resolution is **backend-only, kind-agnostic** — on this deployment
(modern backend) the brief ceiling is **43,200 bytes**. Real briefs run ~1.5–6 KB, so
every brief to a Grok delegate would be pasted inline and arrive as one joined line.
The same gate governs refinements (`AgentTaskReplyService.cs:340`).

**The real exposure is semantic, not transport.** A joined brief merges words across
line boundaries: markdown headers fuse into the preceding sentence, list items
concatenate, and — the correctness hazard for Code/Debug specifically — commands, test
filters, and file paths merge with the following line's first word
(`…task-xxxx-brief.mdEverything you need is there`). A Grok model would muddle through
many joined briefs, but a Code/Debug brief is exactly the kind of text where a merged
token changes meaning. The pointer message (`BuildBriefPointer`) is multi-line too, so
even the spill path's own pointer would join — mostly survivable, but the spill-file path
concatenating with the next word is a live misread risk.

**Verdict:** *conditional non-blocker.* Not "moot" (the spill gate never fires for real
briefs today) and not "blocking" (nothing needs fixing in Grok itself). It blocks only
until S1 lands: force the spill for Grok and make the pointer single-line-safe. After S1,
every multi-paragraph body a delegate receives — brief, refinement — travels by file with
full fidelity; what still types live is short pointer/one-liner text that S1 makes
join-proof. Live-typed *conversation* (an orchestrator answering a Grok delegate's
question via the queue) remains joined — acceptable for opt-in use, called out in the
opt-in docs, and a reason (one of several, §4) not to make Grok a default yet.

**Optional future measurement, not in scope:** whether Grok's composer inserts a literal
newline on Alt+Enter / ESC-CR. If yes, a Grok delivery encoding could preserve structure
for typed bodies too. A `GrokCanaryTests` phase, only worth running if joined live-typed
follow-ups prove to matter in practice.

## 2. Slices (question 2)

### S1 — Grok delivery shape: always-spill + join-safe pointers (S, ~½ day)

- `FitBriefForTyping` and the refinement gate take the session's `AgentKind`; for
  `AgentKind.Grok` the brief/refinement inline ceiling is **0** — every multi-line body
  spills to `.antiphon/task-<id>-brief.md` (the CARD-0025 machinery, unchanged). The
  oversize tripwire and single-write ceilings stay at the measured modern values (Grok
  measured no clip; the tripwire is about evidence, not Grok's composer).
- `BuildBriefPointer` / `BuildRefinementPointer` gain a join-safe rendering for Grok:
  newlines become explicit spaces server-side (we control the join instead of the
  composer), with the spill path and the task markers delimited so nothing concatenates
  ambiguously (quote the path, keep the closing marker last).
- Pinned by: a fakegrok queue→PTY integration test (brief spills, pointer joined but
  parseable, delivery transcript-confirmed) alongside the existing
  `SessionMessageQueueGrokPtyIntegrationTests`.

### S2 — `AgentTask.AgentKind` + API + `delegate.ps1 -Kind` (S, ~½–1 day)

- New column `AgentTask.AgentKind` (enum, default `ClaudeCode`) + migration; surfaced in
  the create DTO and `delegate.ps1 -Kind` (values: `ClaudeCode` default, `Grok`).
- Validation: an explicit allowlist `{ClaudeCode, Grok}` for now (Codex/OpenCode rejected
  with a reason naming this card; CARD-0083's contract later replaces the allowlist with
  a capability query — §5). **Orchestrator tasks stay Claude-only**: an orchestrator's
  contract (deny-hook, delegate.ps1 usage patterns, check-interpreter interplay) has only
  ever been exercised on Claude; Grok starts as a *worker* kind.
- `RolePolicyEntry` gains an optional `Kind` field (unset = ClaudeCode) so a later
  promotion of Code/Debug to Grok is a config edit, not code. Ships unset.

### S3 — kind-aware dispatch: launch spec, pool, escalation (M, ~1–2 days)

The core threading slice. `BuildLaunchSpec` (`AgentTaskDispatcher.cs:1086`) branches the
way `AgentControlService.StartInteractiveSessionAsync` already does for named agents:

- Registry definition by kind: the `grok` definition already exists in
  `server/appsettings.json` (`grok.exe --always-approve --no-alt-screen`) — resolve it
  instead of `DefaultDefinition` when `task.AgentKind == Grok`. A missing definition
  fails the dispatch loudly.
- `--model ModelLevelAliases.ForGrok(level)`; **no `--name`** (Claude-only flag);
  instruction bundles ride **`--rules`** instead of `--append-system-prompt`
  (the exact branch at `AgentControlService.cs:227`). The ANTIPHON_* env block is
  provider-agnostic and unchanged. Command-line budget guard applies as-is.
- **Warm pool:** claiming must match kind (a warm Claude delegate must not take a Grok
  task and vice versa). Add `Agent.Kind` (default `ClaudeCode`) to the pool agent row —
  deriving kind from the latest session is racy and a pool row can have none. Pool-per-
  directory caps count per (directory, kind).
- **Escalation/retry:** the tier ladder is kind-agnostic and stays; note in the
  escalation event text that Grok's Frontier and High both map to `grok-4.6`, so a
  Debug escalation on Grok is a fresh context at the same model — still useful (that is
  most of what escalation buys anyway), but the event should say so rather than imply a
  bigger model.
- Delivery, verification, working/idle, turn-end settlement, check probes: **no work** —
  CARD-0080 S2 made them format-agnostic over transcript rows, and
  `GrokTranscriptNormalizer` already records per-turn usage tokens.

### S4 — kind-aware display: `ModelLevelAliases.For(kind, level)` (S, ~½ day)

A helper (`For(AgentKind, AgentModelLevel)`, or reuse
`AgentTuiRunnerCatalog.MapLegacyModel`) replacing the ~12 display-only
`ForClaude(task.ModelLevel)` call sites in `DelegationReportFormatter`,
`AgentTaskService` (retry/escalate texts), `DelegateCheckProbe`, and
`AgentTaskDispatcher` event details — so a Grok task's events say `grok-4.6`, not
`fable`. Mechanical; separable so S3's diff stays reviewable.

### S5 — Grok pricing (S, ~½ day) — see §6

### S6 — proving it (M, ~1 day)

- Integration: fakegrok end-to-end dispatch — `-Kind Grok` task → grok definition
  launched with `--rules`/`--model grok-4.6` → brief spilled → report settles → cost
  stamped from Grok rates.
- Headed `[Explicit]` canary: one real Grok delegate task (the standing "Grok 4.6" agent
  `cbbb38fc` / a scratch cwd is available for this) through `delegate.ps1 -Kind Grok
  -Role Test` or similar — the first real worker mileage, and the template for the
  evaluation runs in §4.

Order: S1 → S2 → S3 (critical path), S4/S5 parallel to S3, S6 last. S1 is genuinely
first: landing S2/S3 without S1 would deliver joined briefs to real sessions.

## 3. What CARD-0083 owns vs this card (question 4)

| Concern | Owner | Why |
|---|---|---|
| `AgentTask.AgentKind`, `-Kind` flag, dispatch threading (S2/S3) | **0084** | Delegation-path plumbing; no contract needed to branch on an enum. |
| Delegate-eligibility check | **0084 ships an allowlist**; 0083 replaces it | The principled version is a capability query ("has model argument + permission bypass + structured activity + a system-prompt channel"). 0084 must not block on designing that; the allowlist is one small method with a comment naming 0083. |
| Grok delivery shape / spill-always (S1) | **0084** | A consequence of a measured Grok composer behavior, not a declarable capability. If 0083 later adds a "preserves typed newlines" capability, S1's gate becomes its first consumer. |
| Per-kind display aliases (S4) | **0084** | Cosmetic, mechanical. |
| Concrete Grok rate entries (S5) | **0084** | Needed to make the kind dispatchable without zero/mis-costing; blocking it on a generic pricing contract re-creates the "scoped to Claude because nothing says otherwise" trap 0083 itself calls out. |
| Generic pricing/usage *declaration* (incl. provider self-reported cost — Grok's `turn_completed` carries a cost figure the normalizer currently ignores) | **0083** | Exactly the "what can this provider report" question. S5's rate-table shape must not preclude it (§6). |
| Usage-limit / quota shape survey for Grok | **0083** | Unknown today; a Grok delegate hitting a quota wall currently looks like a stall. This is the strongest *promotion* gate 0083 feeds into §4. |
| Stale `structuredActivity: Degraded` string for Grok (`AgentTuiRunnerCatalog.cs:150`) | **0083** (or a trivial standalone commit) | Flagged per the card's instruction; not fixed as a side effect here. It is per-Kind in `AgentTuiRunnerCatalog.GrokCapabilities` and now contradicts CARD-0080 S2. |

Nothing in S1–S6 waits on 0083; nothing in S1–S6 builds a capability registry 0083 would
have to demolish.

## 4. Opt-in first, promotion by config (question 3)

**Recommendation: explicit `-Kind Grok` opt-in now; Code/Debug role defaults stay
Claude.** Justification:

- **Zero worker mileage.** Grok has only ever run as a named interactive/channel agent.
  The delegate contract is different: reporting via turn-end settlement, refinements,
  check-ins, escalation, pool reuse — none exercised on Grok even once.
- **Unmeasured operational shapes.** Usage-limit behavior (0083's survey), `--rules`
  size ceiling (the 30,000-char command-line budget is Claude-measured; Grok's own limit
  is unprobed), interrupt/Esc shape under delegation, `session_recap` (auto-compaction)
  mid-task.
- **The join residue.** Live-typed follow-up conversation stays newline-joined after S1.
  Fine for a caller who chose Grok; not fine to silently impose on every Code/Debug task.

Promotion path: after ~20 real Code/Debug tasks at `-Kind Grok` with settle rate, report
quality, and cost compared against Claude equivalents — and 0083's usage-limit survey
done — flip `RolePolicy.Code.Kind` / `RolePolicy.Debug.Kind` to `Grok` in config (the
seam S2 builds). No code change, reversible the same way.

## 5. Pricing (question 5)

`DelegationCost.Estimate` keys rates by `AgentModelLevel` alone, so a Grok Frontier task
today would be priced at **fable rates ($10/$50 per M)** — not zero-cost, but wrong by
roughly an order of magnitude, distorting the per-root ceiling in the conservative
direction (blocking dispatch on spend that never happened).

**S5:** `DelegationPricingSettings` gains a per-kind overlay —
`KindRates: Dictionary<AgentKind-name, Dictionary<level, ModelRateSettings>>` — with
lookup order (kind, level) → (kind, High) → existing Claude-shaped `Rates` fallback, and
`DelegationCost.Estimate`/`RatesFor` gaining a kind parameter (default `ClaudeCode`, so
every existing caller and stored row prices identically). Fill grok-4.6/grok-4.5 numbers
from xAI's published pricing at implementation time (**do not trust a model's memory of
them**), same four-counter model — `GrokTranscriptNormalizer` already records
input/output/cacheRead/cacheCreation, so the existing rollup works unchanged. Grok's
self-reported per-turn cost stays unread until 0083 decides where self-reported cost
lives; the table shape above doesn't preclude it. `PricingVersion` bumps only if the
model changes, which this doesn't — it's a rate-lookup widening.

## 6. Flags for the caller (not fixed here, per the card)

- `AgentTuiRunnerCatalog.cs:150` — Grok's `structuredActivity` still reads "Degraded —
  PTY quiet-time fallback; Grok ACP session updates are not tailed", contradicted by
  CARD-0080 S2 (`efb2790`). One-line fix, per-Kind, in `GrokCapabilities`.
- `AgentTaskDispatcher.cs:1094` also passes `--name` (Claude-only) — S3 handles it, but
  anyone touching the launch path before S3 should know it's a Claude-ism.
