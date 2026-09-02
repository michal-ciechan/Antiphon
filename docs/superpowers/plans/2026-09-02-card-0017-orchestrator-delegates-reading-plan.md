# CARD-0017 — Steer orchestrator sessions to delegate their reading

**Date:** 2026-09-02 (Plan pass, task 384a85e1 — design only; no production code changed)
**Card:** CARD-0017 — a standing rule in the steering text Antiphon injects into ORCHESTRATOR
sessions only (not workers), mirrored into AGENTS.md. Requested 2026-08-09 after an orchestrator
hand-read ~1500 lines to scope a feature. First attempt (task 30b70dcc) was cancelled undelivered
(CARD-0003 launch race); this is the first plan that reached a file.
**Supersedes:** nothing.
**Sources (verified this pass):** `.claude/settings.json`; `scripts/hooks/orchestrator-investigation-hook.mjs`
and `orchestrator-investigation.mjs` (+ `__tests__`, `scripts/test-hooks.ps1`);
`server/Bundles/{README,orchestrator,delegate-basics}.md`, `server/Bundles/Presets/orchestrator-prompt.md`;
`server/Application/Services/{InstructionBundles,AgentSessionLaunchComposer,AgentPresets,DelegationReportFormatter,OrchestratorInvestigationSweepService,AgentBundleAttachments}.cs`;
`server/Application/Services/AgentTaskDispatcher.cs:2811`; `tests/Antiphon.Tests/Application/{InstructionBundleTests,AreaMapTests}.cs`;
`AGENTS.md` (7,024 bytes); `scripts/check-agent-context.ps1`; `docs/orchestration-loop.md` §0, §3;
the CARD-0247 plan (§3.1, §3.4, §4, §7) and the CARD-0254 plan (delivery budget); the live server
(`GET /api/agents/{id}` for both standing orchestrators).

## Verdict up front — partially shipped

The card asked the planner to check whether the hooks the orchestrator sees today already deliver
this. They do not; they deliver something adjacent. What exists, what the card asks for, and the gap:

| Card asks for | What exists today | Gap |
|---|---|---|
| A **standing** rule in orchestrator-only steering text | CARD-0247's `PreToolUse` hook nudges **after the 3rd consecutive cold source read** in a run, once per run (`NUDGE_CONTEXT`); the `SessionStart` hook fires **only on `compact`** (`COMPACT_CONTEXT`) — the wrapper exits silently for `startup`/`resume`/`clear`. Both are reactive backstops, tolerating two cold reads per run by design. | No text is injected at session start. Nothing says the rule as a rule. |
| The **exact rule text** ("Delegate the reading … Read directly only what you must quote exactly or must judge personally", with the "even another frontier-tier agent" clause) | `server/Bundles/orchestrator.md` says "Do yourself only: read enough to decompose (list files, read a spec, check git status)" and "Delegate … every investigation deeper than a single file read". `docs/orchestration-loop.md:7-9` has "the orchestrator's context is the scarce resource … not on archaeology". | Looser than the card in two places (a single-file read is allowed; "read enough to decompose" is open-ended) and the frontier-tier rationalisation — the one the card names — appears nowhere. Grep for "Delegate the reading", "frontier-tier", "quote exactly", "scarce resource for the whole run": zero hits in bundles, hooks, AGENTS.md, docs. |
| Injected into **orchestrator sessions only** | The bundle reaches Orchestrator-kind delegates (`InstructionBundles.ForDelegate` → `[orchestrator, delegate-basics]`) and any agent with an `orchestrator` attachment (the preset attaches it at create). The hook discriminator already arms for `ANTIPHON_TASK_KIND=Orchestrator` and for non-task sessions, and disarms workers/subagents. | **Both live standing orchestrators carry only `board-api`** (`Antiphon` 8478998e…, `Antiphon-Orchestrator` a392cbc4…, checked 2026-09-02). A bundle edit reaches neither until it is attached. They saw the hooks only because the discriminator's default rung (no `ANTIPHON_TASK_ID`) arms them. |
| A **mirror in AGENTS.md** | Nothing. AGENTS.md (CARD-0254 routing index, 7,024 of 24,576 bytes) has no line about delegating reads. | Absent. |

So: mechanism shipped (bundles, hooks, discriminator), rule text not shipped, reach incomplete,
mirror absent. The remaining work is text plus one data action, no new mechanism. This card does
**not** close as already-done.

## Decision

1. **The rule goes verbatim into `server/Bundles/orchestrator.md`**, replacing the looser "single
   file read" clause. That file is the only orchestrator-only steering text Antiphon injects
   (`--append-system-prompt`, composed for Orchestrator-kind delegates and for attached agents),
   it is content-versioned, and the bundle README is explicit: a rule that earns standing status is
   PR'd there; recorded anywhere else it reaches nobody.
2. **The AGENTS.md mirror is one bullet**, under "Immediate safety triggers → Cards and tracker",
   with the orchestrator/worker split stated in the line itself and routing to
   `docs/orchestration-loop.md` §0, which receives the full rule. AGENTS.md is universal — every
   worker, Codex and Grok delegate in this checkout loads it — so a bare "delegate the reading"
   there would tell a worker to do the thing `delegate-basics` forbids ("DO NOT SUB-DELEGATE").
3. **No hook changes.** No startup injection, no threshold change, no edit to `NUDGE_CONTEXT` /
   `COMPACT_CONTEXT`. The hooks are CARD-0247's reactive backstop and the sweep's scorecard; a
   third copy of the rule is a third place for it to drift (`docs/orchestration-loop.md` §3 makes
   this exact point about bundles). The bundle is a system prompt and survives compaction; AGENTS.md
   is reloaded; the compact re-injection already covers the hook's own text.
4. **Reach the two live standing orchestrators by attaching the bundle** (data: agent settings
   modal or `PATCH /api/agents/{id}` with `bundleKeys`), not by editing
   `Presets/orchestrator-prompt.md` (a create-time snapshot; "nothing re-applies a preset on
   PATCH") and not by an auto-reconciler (no row records which preset created an agent).
5. **The bundle keeps its explicit carve-out** for what an orchestrator reads itself — list files,
   git status, a plan or spec it must judge — because that is what the card's "must judge
   personally" means in practice and because the hook classifier already excludes `docs/` reads.

## Ground truth (checked, not guessed)

- `.claude/settings.json`: `PreToolUse` matcher `Read|Grep|Glob|Bash|PowerShell` and `SessionStart`
  matcher `compact`, both → `node scripts/hooks/orchestrator-investigation-hook.mjs`, timeout 5.
- `orchestrator-investigation-hook.mjs` `applyDiscriminator`, in order: `agent_id` set → subagent,
  off; `ANTIPHON_TASK_KIND=Orchestrator` → on; `ANTIPHON_TASK_ID` set → worker, off;
  `ANTIPHON_ORCHESTRATOR=0` → off; else on ("default-orchestrator"). `SessionStart` with any
  `source` other than `compact` exits with no output.
- `orchestrator-investigation.mjs`: `R = 3`, `N_REPORT = 25`, `N_DISPATCH = 10`; `docs/`,
  `.antiphon/`, `scratchpad/`, `memory/` are excluded from "source read"; the nudge fires once per
  run (`nudgedForRunStartedId`). Texts start with `[antiphon-orchestrator]` and are pinned by
  `scripts/hooks/__tests__/*.test.mjs` (run: `pwsh -File scripts/test-hooks.ps1`).
- `AgentTaskDispatcher.cs:2811` sets `ANTIPHON_TASK_KIND = task.Kind`;
  `AgentSessionLaunchComposer.cs:49-50` sets `ANTIPHON_ORCHESTRATOR=1` only when the agent carries
  the `orchestrator` attachment. `OrchestratorInvestigationSweepService.cs:318-320` also finds
  orchestrators by that attachment (plus by "has dispatched a task").
- `InstructionBundles.ForDelegate`: Orchestrator kind → `[orchestrator, delegate-basics]`; other
  roles → `[delegate-basics]`; Check → none. `AgentPresets.Orchestrator` attaches
  `[orchestrator, board-api]` **at create only**.
- Live (2026-09-02): `GET /api/agents/8478998e-…` (Antiphon) and `/a392cbc4-…`
  (Antiphon-Orchestrator) both return `attachedBundleKeys: ["board-api"]`,
  `composedBundles: ["board-api v7fc98677"]`. Neither carries `orchestrator`.
- `server/Bundles/orchestrator.md` is 4,747 bytes; the card's rule paragraph adds ~600 chars.
  `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` asserts
  the everything-at-once composition stays under 20,000 chars (measured 15,307 on 2026-08-30).
- `InstructionBundleTests.the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact`
  pins phrases, not paragraphs: "You are an orchestrator." (start), "Delegate everything else",
  "-Reply", the three incident codes, the credentials line, the channel-bound sentence,
  "re-emit `[[attach:]]` yourself", "docs/ops-http.md". Nothing pins "Do yourself only" or
  "single file read" (the only other copy is the historical `docs/features/007-…/proposal.md:504`).
- AGENTS.md: 7,024 bytes; `AgentContextContractTests` (in `AreaMapTests.cs`) enforces ≤ 24,576
  raw UTF-8 bytes and that every `](docs/….md)` link resolves; `scripts/check-agent-context.ps1`
  reports the same. The "Cards and tracker" subsection is where delegate/scope rules live.
- `docs/orchestration-loop.md` §0 is the owner: the three-rung ladder (trust the report / ask the
  same delegate / delegate the investigation) plus the line "the orchestrator's context is the
  scarce resource: spend it on judgement, not on archaeology". Its rung 3 already says direct
  reading is a Debug/Plan delegate's job even when the pipeline is broken — consistent with the
  card, just without the card's wording.
- CARD-0247 plan §3.4 declined "bundle text change … beyond one sentence" and recorded that "the
  failing session does not receive bundles at all". That deferral is what this card closes.
- The operator's own memory file (`feedback_orchestrator_delegates_the_reading.md`) states the same
  rule; it is not a repo artefact and reaches no agent.

## Slices

### S1 — The rule, verbatim, in the orchestrator bundle

**Files:** `server/Bundles/orchestrator.md`; `tests/Antiphon.Tests/Application/InstructionBundleTests.cs`.

Replace the bundle's second and third paragraphs (from "Do yourself only:" through "that is a
delegation.") with the following three paragraphs. Everything from "A delegate that reports
`StoppedBeforeFirstPrompt`" onward stays byte-for-byte:

```
Do yourself only: list files, check git status, read a plan or spec you must judge, decide
the plan and the roles, integrate delegate reports, talk to the caller.

Delegate the reading. When you need to know how something works - what a file contains, where
something is called, what shape the data is, whether an endpoint exists - send a delegate and
take its answer. Do not read it into your own context. This holds even when the answer looks one
grep away, and even when the delegate is another frontier-tier agent: your context is the scarce
resource for the whole run, and every file read into it is capacity the run never gets back.
Read directly only what you must quote exactly or must judge personally.

Delegate everything else - every code edit, every test run, every git operation. If you are
about to Edit, Write, or run a build, stop: that is a delegation.
```

The middle paragraph is the card's text unchanged apart from ASCII hyphens (the bundle is prompt
text; the file already mixes dashes, and an em-dash here would be the only non-ASCII byte in a
paragraph the test pins). "Delegate everything else" is kept because the test pins it. No
`{agentName}` / `{channels}` placeholders (`no_bundle_carries_a_channel_preamble_placeholder`).

**Tests:** extend `the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact` with
three pins — `"Delegate the reading."`, `"quote exactly or must judge personally"`,
`"frontier-tier"` — and one negative pin, `ShouldNotContain("single file read")`, so the loosening
cannot creep back. Update the measurement comment on
`the_worst_case_composition_measured_sits_far_under_the_budget` with the new `orchestrator` char
count from the test's own `detail` string (do not change the assertion).

**Verify:**
```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0017/ -- --treenode-filter "/*/*/InstructionBundleTests/*"
```
Expect all green (the class has ~25 tests). Delete every `bin-card0017` directory afterwards.

### S2 — Owner doc and the AGENTS.md mirror

**Files:** `docs/orchestration-loop.md`; `AGENTS.md`.

`docs/orchestration-loop.md` §0: after the three-rung ladder and before "**Also delegated: the
landing mechanics.**", add a short block headed **The standing rule (CARD-0017)** carrying the
card's paragraph verbatim, followed by one sentence: the canonical copy is
`server/Bundles/orchestrator.md`, which every sub-orchestrator launch composes and which a standing
orchestrator carries when the `orchestrator` bundle is attached; this copy exists so AGENTS.md has
an owner to route to. Also amend the CARD-0247 paragraph in the same section (line 37-40) with one
clause: the hook is the backstop at the third read; the rule is the bundle's.

`AGENTS.md`: one bullet, first under `### Cards and tracker`:

```
- Orchestrators delegate the reading: a session that dispatches delegates sends one for how something works and takes its answer, reading directly only what it must quote exactly or judge personally — even when it looks one grep away, even to another frontier-tier agent. A delegate reads its own files and never sub-delegates. Owner: [docs/orchestration-loop.md](docs/orchestration-loop.md) §0.
```

About 360 bytes; the file lands near 7,400 of the 24,576 target. No anchor in the link — the
owner-exists test's regex needs the path to end in `.md)`.

**Tests:** `AgentContextContractTests` (both tests) and `pwsh -File scripts/check-agent-context.ps1`.

**Verify:**
```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0017/ -- --treenode-filter "/*/*/AgentContextContractTests/*"
pwsh -NoProfile -File scripts/check-agent-context.ps1
```

S1 and S2 are independent; one delegate, one commit each, or one commit for both.

### S3 — Reach the live standing orchestrators (data, not code)

Attach `orchestrator` to `Antiphon` (`8478998e-f35e-46c7-9d5e-f9330c671474`) and
`Antiphon-Orchestrator` (`a392cbc4-0fc0-4603-b4d0-5198d1929718`): the agent settings modal's
"carries bundles" multi-select, or `PATCH /api/agents/{id}` with
`bundleKeys: ["board-api", "orchestrator"]` (send the full desired set and read back
`attachedBundleKeys` — `AgentBundleAttachments` validates the keys and rejects style bundles).
Effects, all at the agent's **next launch**: the rule reaches the session; `ANTIPHON_ORCHESTRATOR=1`
is exported, so the hook arms on its explicit rung rather than the default one; the sweep's
by-bundle candidate list includes them. `BundlesOutOfDate` badges each until it relaunches — that
is the mechanism working. Sub-orchestrator delegates need nothing: `ForDelegate` already composes
the bundle for `-Orchestrator` dispatches.

This is an operator action after S1 lands (attaching before S1 would only deliver the old text).
Do it from the modal or with `curl`; not a migration, because no row records which preset created
an agent and the README's rule is that presets are not re-applied.

### S4 — Close the card

Close note: rule shipped in `server/Bundles/orchestrator.md` (version stamp from the header),
mirrored in AGENTS.md, owner text in `docs/orchestration-loop.md` §0; the CARD-0247 hooks remain the
backstop; both standing orchestrators attached. Reference this plan.

## What this card does not do

- No new hook event, no `SessionStart` matcher widening, no startup `additionalContext`, no change
  to `R` / `N_REPORT` / `N_DISPATCH`, no edit to `NUDGE_CONTEXT` or `COMPACT_CONTEXT`. The hook
  tests are untouched.
- No gate of any kind (the card and CARD-0247 both rule gates out).
- No change to `server/Bundles/Presets/orchestrator-prompt.md` or to `delegate-basics.md`; workers
  keep "DO NOT SUB-DELEGATE".
- No auto-attach reconciliation for agents created before the preset attached bundles.
- No Codex/Grok orchestrator support (`docs/agent-kinds.md`: orchestrators are ClaudeCode only).

## Left open, deliberately

- Whether `COMPACT_CONTEXT` should open with the rule's first sentence. Cheap, but a third copy;
  decide from the `OrchestratorInvestigation` attention rows a week after S3 — if runs still start
  after compaction, that is the evidence for it.
- Whether the AGENTS.md line should move up to "Essential front doors". It is a safety trigger
  (irreversible context spend), which is why it sits with the other triggers.

## Test matrix

| Slice | Test | Kind | Pins |
|---|---|---|---|
| S1 | `InstructionBundleTests.the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact` | Unit | "Delegate the reading.", "quote exactly or must judge personally", "frontier-tier", not "single file read"; existing pins intact |
| S1 | `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` | Unit | composition still < 20,000 chars after +~600 |
| S1 | `InstructionBundleTests.no_bundle_carries_a_channel_preamble_placeholder`, `every_bundle_summarises_itself_in_its_opening_sentence` | Unit | unchanged, must stay green |
| S1 | `InstructionBundleTests.a_sub_orchestrator_carries_its_own_contract_first_then_the_basics` | Unit | reach for `-Orchestrator` dispatches, unchanged |
| S2 | `AgentContextContractTests.the_universal_agent_context_stays_within_the_raw_utf8_byte_budget` | Unit | ≤ 24,576 bytes |
| S2 | `AgentContextContractTests.every_local_owner_document_named_by_the_routing_index_exists` | Unit | the new link resolves |
| S3 | manual: `GET /api/agents/{id}` → `attachedBundleKeys` contains `orchestrator`; after relaunch, `composedBundles` carries `orchestrator v<new hash>` and `bundlesOutOfDate` is false | Live | reach |
| — | `pwsh -File scripts/test-hooks.ps1` | Node | untouched; run once to show it stayed green |

## Sequencing and risks

S1 → S2 (either order; both docs-shaped, Shared workspace is fine, scope `docs,server/Bundles/**,tests/Antiphon.Tests/Application/InstructionBundleTests.cs`) → S3 (operator, after S1 is on master and the AppHost has the new build) → S4.

- **Bundle version flips on S1.** Every agent carrying `orchestrator` badges `BundlesOutOfDate`
  until its next launch; sub-orchestrator dispatches get the new text immediately. Expected.
- **The AGENTS.md line is read by workers.** The split is in the sentence ("A delegate reads its
  own files and never sub-delegates"). The execute delegate for S2 is itself a worker; if the line
  reads as an instruction to it, the wording is wrong.
- **Text pins are brittle.** Pin phrases, as the existing test does; do not pin paragraphs.
- **The card's rule vs "read enough to decompose".** Reconciled by naming the carve-out concretely
  (list files, git status, a plan or spec you must judge) instead of "enough".
- **Command-line budget.** +~600 chars on a 15,307-char worst case against a 20,000 bound; the
  test's `detail` string reports the real number.

## Execution notes

- Build/test with `--property:OutputPath=bin-card0017/` (forward slash) and delete the `bin-card0017`
  directories before reporting.
- The bundle file is CRLF in the working tree; the hash is over LF-normalised text, so no
  line-ending care is needed beyond not introducing a BOM.
- Commit messages: `feat(bundles): CARD-0017 S1 orchestrator bundle carries the delegate-the-reading rule`
  and `docs: CARD-0017 S2 AGENTS.md mirror and orchestration-loop owner text`.
- Do not restart the AppHost for S1/S2; the server embeds the bundle at build, so the next deploy
  (`scripts/restart-apphost.ps1`, which the orchestrator orders, not the delegate) is what makes S3
  deliver the new text.
