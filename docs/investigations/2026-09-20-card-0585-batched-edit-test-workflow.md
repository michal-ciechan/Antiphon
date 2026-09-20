# CARD-0585 — Batched edit/test workflow vs Code-stage turn/token cost

Investigate, 2026-09-20. Task `0eb35cd2`. Evidence only: board cards, Antiphon task rows, Grok session `usage.json` / `events.jsonl` / `updates.jsonl` / `signals.json`, and Antiphon session transcripts. No production code changed.

**Date:** 2026-09-20. **Card:** CARD-0585 (`49211e68`). Related: CARD-0583 (`30388d23`), CARD-0459 (`ac011385`), CARD-0490 (`76b3b630`).

## Verdict

The Code-stage spend from tonight is real and large, but it is **not** mostly “wait for long test” in the transcript. Across the three settled/blocked Grok Code sessions, **43–47 % of model loops are read-only** and **8–15 % are wait-only**. Wall-clock on CARD-0459 **is** dominated by already-batched Slow/ProcessSpawnLimit runs (one backgrounded test command ran **159 minutes**). CARD-0490 is the opposite: tests were short (**8.8 min** total) while the agent issued **19 separate `dotnet build`s** and 364 model loops of reading/editing.

A Plan/TestDesign **checkpoint manifest** (files + named `--treenode-filter`s) is the useful next shape, because that list **already exists** on CARD-0459 (`484fb214` plan: coverage-to-class, V-1..V-12, R-1..R-8, `bin-c459/`, method-scoped PC filters) and the Code agent still invented extra rebuilds and tautological tests. **Apply-all-edits then one final test run** is the wrong default: CARD-0459’s first Code round greened dummy `x.ShouldBe(x)` bodies, which a single end-of-round suite would have called success and spent another $110 to undo.

`grok learn` is the bundled Grok Build `/learn` skill (`C:\Users\lndco\.grok\bundled\skills\learn\SKILL.md`). It is a **post-session harness miner** (create/edit/retire skills), not a test-optimizer and not in this repo. CARD-0583 (Hindsight) is the same family as `/reflect` and `/learn`: durable lessons after the fact. Neither writes a per-card edit/test manifest.

## 1. What “grok learn” actually is

Located, not assumed:

| Claim | Evidence |
|---|---|
| Product | xAI Grok Build bundled skill, Grok 1.0.34 (`~/.grok/bundled/manifest.json` hashes `skills/learn/SKILL.md`; `settings_cache.json` `grok_version=1.0.34`) |
| Invocation | `user-invocable: true`; slash `/learn`; also “learn from my traces”, “what skills do I never use” (`SKILL.md` frontmatter) |
| Workflow | `collect_sessions.py` → `learn-traces.rhai` map-reduce → `report.md` + `actions.json` → user curates create/edit/delete of skills/plugins/MCP |
| Job | Mine **user-sat** Grok sessions for repeated process phrases, stale skill lines, unused surfaces. Explicitly **not** “summarizing sessions into notes or memory” (`SKILL.md` L7–8, L36) |
| In this repo | **No.** Zero matches for CARD-0585/`/learn` under Antiphon docs or source |
| User-guide | `~/.grok/docs/user-guide/04-slash-commands.md` does not list `/learn`; skills appear as slash commands when `user-invocable` (`08-skills.md`) |

Applicable pattern for CARD-0585: encode a **durable Code-stage procedure** as a skill so agents stop re-deriving “edit one file, rebuild, run one class”. That is harness-level, not a per-card file list.

Not applicable: `/learn` does not batch edits or tests. Default collector **drops `session_kind=headless`** (`collect_sessions.py:553,637`, help text “bots, grok -p”) unless `--include-headless`. These four summaries have **no `session_kind` field** (`d549ef5f` `summary.json`); whether Antiphon’s Grok spawn would be kept is unverified.

## 2. CARD-0583 patterns (Hindsight)

CARD-0583 (Backlog): evaluate [Hindsight](https://github.com/EfficientStreet/hindsight) vs existing `/reflect` and `/reflect-session`. Same loop as `/learn`: whole-session review → one upstream lesson → persistent memory/skill, with pin+drift if adopted. Filed 2026-09-19; legitimacy of the GitHub repo is **that** card’s job, not this one.

Overlap with tonight’s cost:

- After `0e3d0465`, a session-end lesson “do not ship tautological C459 bodies” would have been the single upstream fix that avoided `af90420b`’s **$110.06**.
- That is **learning**, not **batching**. It does not remove the 159-minute Slow batch.

## 3. Before-state: tonight’s four Code tasks

Antiphon `GET /api/agent-tasks/{id}` plus native Grok session dirs under `C:\Users\lndco\.grok\sessions\C%3A%5CAntiphon%5Cworktrees%5Ccard-task-<id>\`. Costs below are **Antiphon `costUsd`** (pricing version 2). Grok `usage.json` `costUsdTicks/1e9` is higher ($148.56 / $141.13 / $316.25) and is not the billed row.

The card text’s “CARD-0490 Code round $201.40 (1h43m)” is **not a fourth task**. It is `d3319023` at first Blocked (`2026-09-20T10:13:38Z`, 1 h 43 m after `08:30:32Z` dispatch). The same task then continued to **$233.23** at `11:52:26Z` (3 h 22 m). Fleet listing later shows it Failed; native session still had `updated_at` 14:18Z.

| Task | Card | Title | Antiphon status | Dispatched → completed (UTC) | costUsd | tokensIn / cacheRead / tokensOut | Uncached in |
|---|---|---|---|---|---|---|---|
| `0e3d0465` | 0459 | worktree cleanup implementation | Succeeded | 19 23:06:52 → 20 03:51:32 | **108.76** | 43.42M / 42.24M / 132k | 1.18M |
| `af90420b` | 0459 | replace tautological test stubs | Succeeded | 20 08:19:13 → 13:30:33 | **110.06** | 43.80M / 43.08M / 152k | 0.72M |
| `d3319023` | 0490 | phone-home runner implementation | Blocked then Failed | 20 08:30:32 → 11:52:26 | **233.23** | 93.05M / 91.69M / 214k | 1.35M |
| `29bb901f` | 0459 | fix V-11/R-8 native receipt timeouts | Dispatched (live) | 20 13:31:09 → (open) | **0.00** on task row | usage.json 109k / 72k / 781 | n/a |

Cache hit rate 97–99 %. Spend is **loop count × growing cached context**, not fresh tokens.

### Turns, tools, wall-clock

Grok `usage.json` `turnCount` is **user prompts** (rules ACK + brief, plus later Blocked replies on 0490). Model loops are `modelCalls` / `events.jsonl` `loop_started`. Antiphon transcripts match tool counts within 0–4.

| Task | User turns (`usage`) | Model loops | Tool calls | Compact | Session wall (`signals.json`) | Backgrounded cmd wall |
|---|---|---|---|---|---|---|
| `0e3d0465` | 2 | 180 | 490 | 1 auto-compact (`signals` compactionCount=2) | 17 051 s (**4 h 44 m**) | **229.7 min** |
| `af90420b` | 2 | 198 | 464 | 1 | 18 613 s (**5 h 10 m**) | **245.8 min** |
| `d3319023` | 4 | 356 | 856–861 | 1 | 12 098 s (**3 h 22 m**) | **26.6 min** |
| `29bb901f` | 1 | 59–61 (live) | 125 | 0 | still running | **30.5 min** so far |

Antiphon transcript kinds (`GET /api/sessions/{id}/transcript`):

| Task | entries | UserPrompt | Thinking | ToolCall | CompactBoundary |
|---|---|---|---|---|---|
| `0e3d0465` `d549ef5f` | 703 | 3 | 181 | 494 | 1 |
| `af90420b` `99b74403` | 708 | 3 | 199 | 466 | 1 |
| `d3319023` `fcb92c62` | 1265 | 5 | 361 | 862 | 1 |
| `29bb901f` `5fd8c850` | 201 | 2 | 59 | 125 | 0 |

### Loop composition (wait vs edit vs read)

Grouped `events.jsonl` `tool_started` names between `loop_started` rows.

| Task | wait-only | shell-or-wait | read-only | edit (no test) | mixed |
|---|---|---|---|---|---|
| `0e3d0465` | **11.1 %** (20/180) | 10.6 % | **45.6 %** | 23.3 % | 8.3 % |
| `af90420b` | **15.1 %** (30/199) | 17.1 % | **42.7 %** | 16.6 % | 7.5 % |
| `d3319023` | **8.0 %** (29/364) | 15.9 % | **47.3 %** | 25.0 % | 2.7 % |
| `29bb901f` (live) | 11.5 % | 27.9 % | 32.8 % | 4.9 % | 21.3 % |

The card’s “bulk of the transcript is build/wait/read-TRX” is **true of CARD-0459 wall-clock**, false of tool/turn mix. Combined wait+shell is 21–32 % of loops, not a majority.

### Builds and test batches

`run_terminal_command` `rawInput.command` classified; wall times from `task_completed.task_snapshot` `start_time`/`end_time` (Grok auto-backgrounds commands at ~15 s, so `tool_completed.duration_ms` caps at 15–20 s and is **not** test duration).

| Task | `dotnet build` cmds | test cmds | build+test cmds | wait_poll tools | Longest bg command |
|---|---|---|---|---|---|
| `0e3d0465` | 5 (5.0 min) | 7 (**210 min**) | 3 (13.2 min) | 24 | **159.1 min** test (`c459-vr-1d2a14db` publication-regression + journal-notifications batch) |
| `af90420b` | 4 (5.4 min) | 9 (**196.8 min**) | 7 (42.3 min) | 32 | **120.6 min** test (`Antiphon.Tests\bin-c459\`) |
| `d3319023` | **19 (15.9 min)** | 8 (8.8 min) | 0 | 29 | 2.7 min test (wide class OR filter) |
| `29bb901f` | 0 separate | 4 (26.6 min) | 2 (3.9 min) | 6 | 19.6 min E2E (`c459-r8`) |

CARD-0490 also rebuilt `PtyHost`, `SessionRunner`, `Antiphon.Server`, `PtyHost.Tests`, `SessionRunner.Tests`, `Agents.Pty.Tests` as separate commands — slice-shaped, not one isolated `bin-card0490/` after a file group.

Grok still backgrounded 18 / 21 / 29 commands despite the delegate standing rule “run every command in the foreground and wait”: the **harness** moves a command to background after ~15 s (this Investigate session saw the same). The agent then spends wait-only loops on `get_command_or_subagent_output`. That tax is 8–15 % of loops and is independent of a file/test manifest.

## 4. Three workflow shapes against this evidence

### A. Current ad-hoc apply / test / rebuild

Observed. Extra rebuilds after compile errors (0459 race tuple, script-from-`bin-c459`, `WorkspaceUseAdmission`; 0490 lease/recovery/V-7 origin) are **useful** mid-stream feedback. Extra rebuilds with no new failure (0490: 19 builds for 8 short test runs) are waste. Extra Code round after dummy greens (`af90420b`, $110) is a **verification-shape** failure, not a missing batch.

### B. Plan/TestDesign checkpoint manifest (files + filters)

The CARD-0459 plan at `484fb214` already names classes, V/R ids, `OutputPath=bin-c459/`, and method-scoped PC filters. TestDesign is supposed to be that spec. Code did not treat it as a closed checkpoint list: it wrote tautological C459 methods, skipped V-11/R-8, and still ran seven test commands including a 159-minute Slow batch that the plan already expected to be expensive.

What a binding would change, measured against tonight:

- **Would not** delete the 159 min / 121 min Slow batches. Those are the named V/R work. Non-goal: do not cut coverage.
- **Would** cap rebuilds: 0459’s 5+3 and 0490’s 19 → one isolated build per checkpoint (slice or V/R group).
- **Would** cut some of the 43–47 % read-only loops that are “find the files the plan already named”.
- **Would not** cut wait-only loops while Grok auto-backgrounds long tests; those stay until the harness/rules change.
- Estimated save if 0490 builds drop 19 → ~4 and 0459 drops redundant Unit/class reruns: **roughly 20–35 % of billed cost** on 0490, **under 20 %** on 0459 (wall-clock stays on Slow tests). Not an A/B; derived from loop mix + rebuild counts.

### C. All edits up front, one final test run

Against tonight this is **net harmful**:

- `0e3d0465` result: “most specified C459 tests are tautological stubs”. One final green suite would have settled that as done. The follow-up cost **$110.06 / 5 h 10 m**.
- `0e3d0465` publication-regression 90/85/5 then needed a **27.6 min** inherited-red rerun at `484fb214`. One combined end run makes “which of N edits caused which of 5 failures” the diagnosis, on top of 159 min.
- `d3319023` compile/origin mistakes were found by intermediate builds. Deferring them to one run at the end does not remove the rebuild; it stacks failures.
- Diagnosis cost when the plan is wrong (unfamiliar failure) is exactly the case the card already carves out. That case was **0490 V-7 live-turn**, which ended Blocked on credentials/image/asset-lock — a spec cannot pre-know those.

Reserve apply-all for the rare slice where TestDesign lists a closed, compile-safe file set with no new types/migrations. Not the default.

## 5. Mechanism (confirmed)

Code-stage $ is the product of (1) **named Slow/native V/R duration**, which batching cannot shrink without cutting coverage, (2) **extra isolated rebuilds and extra model loops** after every file, which a checkpoint manifest can shrink, (3) **read-heavy hunting** despite Plan already listing files/classes, (4) **dummy tests that force a second Code dispatch**, which a post-session lesson (`/learn` / Hindsight / `/reflect`) can shrink next time, (5) **Grok 15 s auto-background + poll**, 8–15 % of loops.

The missing artefact is not “write V/R filters” (TestDesign already does). It is a **Code-stage obligation to execute that list as checkpoints** and to treat tautological greens as not-done.

## Uncertainties

- Antiphon `costUsd` vs Grok `costUsdTicks/1e9` disagree (~1.37× on 0490). Billed figures used above are Antiphon.
- `29bb901f` still Dispatched; task row tokens 0; native usage ~$0.39 + 30.5 min tests. Do not treat it as a completed before-state.
- Whether Antiphon Grok sessions are `session_kind=headless` for `/learn` was not proven (field absent on these summaries).
- Savings percents are reconstructed from loop mix, not a paired rerun of the same card.
- CARD-0583 Hindsight legitimacy (stars/forks) not re-checked here.

## Not done, noted

A Code bundle line that forbids settling while any new test body is `x.ShouldBe(x)` (or equivalent tautology) would have been cheaper than `af90420b`; that is a Plan/bundle change, not this Investigate.
