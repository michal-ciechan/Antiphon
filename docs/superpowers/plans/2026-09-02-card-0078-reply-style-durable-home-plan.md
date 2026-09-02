# CARD-0078 — the orchestrator reply style already has a durable home; make the enum say what the operator means

**Date:** 2026-09-02 (task a9875039, design only — no production code changed, no tests run)
**Card:** CARD-0078 "The orchestrator reply style has no durable home - style bundles are rejected by name"
**Supersedes:** the card's own framing. Three of its premises were already false when it was written
(2026-08-17 17:11) — see "Premise corrections" — so this plan is mostly a reconciliation, and the
build that remains is small: one bundle file, two blurbs, one operator PATCH, three doc lines, and an
optional launch-path fix.
**Sources (verified this pass):** the live server on 17202 (`GET /api/agents/{id}` for
a392cbc4, the `/api/agents` list, `/api/agents/bundles`, `/api/agents/{id}/incidents`); the
`antiphon-postgres` container (`AgentSessions.ComposedBundleStamp`, `AgentBundleAttachments`,
`Agents.ReplyStyle`); CARD-0058, CARD-0060, CARD-0061 and CARD-0078 via `card.ps1 get -Json`;
`docs/superpowers/specs/2026-08-16-card-0058-0059-0060-instruction-bundles.md`; commits `7260e306`
(0060 slice 4), `ce735382` (0060 slice 5), `9beef604` / `b29f3f35` (0058 slice 6), `77613925`;
and the source files cited by line under "Ground truth".

---

## Verdict up front

**Where THIS orchestrator's "Reply style: CAVEMAN" section comes from:** the agent row's
`SystemPromptAppend` column, hand-set on 2026-08-16 (730 chars, 14 lines — exactly the block
CARD-0060 describes). It is not a bundle. It is not the reply-style enum: the same row reads
`replyStyle = Normal`, which composes to nothing. `AgentControlService.StartAsync` composes it at
every launch and resume via `AgentSessionLaunchComposer.ComposeForAgentAsync`, which calls
`InstructionBundleComposer.Compose(attached, ComposedKey(Normal) = null, SystemPromptAppend)` and
passes the result as `--append-system-prompt`. That is the durable mechanism the card says does not
exist, and it has been carrying this exact text on this exact agent since 2026-08-16.

**What the card got wrong:**

- The "unbuilt product-level slider" is **CARD-0060, not CARD-0061**, and it was **Done at
  2026-08-17 08:45** — eight and a half hours before CARD-0078 was created. `Agent.ReplyStyle`
  (Normal / Terse / Caveman / Explanatory), four `server/Bundles/style-*.md` files, composer
  wiring, a SegmentedControl in both agent modals, a style chip, `project.ps1 -ReplyStyle`, and the
  setup catalog all ship. CARD-0061 is the Fibonacci check-backoff card, also Done.
- "Style bundles are rejected by name, so the one durable mechanism is closed to this use" reads
  the decision backwards. They are rejected on `bundleKeys` **because** the style already has its
  own front door, the enum, and the 422 message says so verbatim: *"chosen with the agent's
  ReplyStyle, not attached — attaching one would give the agent two voices at once."* The
  mechanism is not closed to style; style is the one thing it has a dedicated control for.
- "Style currently persists nowhere" — it persists in `SystemPromptAppend` on this agent and can
  persist in `ReplyStyle` on any agent. The spec's own plan for this block was one sentence:
  *"The orchestrator's hand-written 730-char caveman block is retired by the operator choosing
  `Caveman` in the modal."* That retirement never happened, and this pass found the reason.

**What still needs building — the real residue:**

1. **The enum's `Caveman` does not say what the operator's Caveman says.** CARD-0060's table
   defines Caveman as *"Minimal. Short bullets, only the most important points. Numbers over
   adjectives."* The shipped `style-caveman.md` took the word literally: *"Talk like caveman. Short
   word. Drop 'the', 'a', 'is', 'of', 'that'. Simple verb. Grunt-length sentence."* Choosing
   `Caveman` in the modal today would make the orchestrator drop articles, not lead with the
   outcome. So the hand block could not move to the enum, and nobody moved it. **Fix: reword the
   bundle to the operator's block** (S1). Content-hash versioning makes that a PR with no
   migration; the only agent on Caveman today (`codex`, Stopped since 2026-08-30) picks it up at
   its next launch.
2. **Move Antiphon-Orchestrator onto the enum** — an operator PATCH (`replyStyle: Caveman`,
   `systemPromptAppend: ""`), one relaunch at a natural boundary, verified through the session's
   stored stamp (S2). Not code.
3. **Three doc lines and a README paragraph**, because no owner doc mentions reply style at all
   today — which is part of why a card written 40 minutes after CARD-0058 closed did not know the
   slider existed (S2).
4. **Optional, separable:** the dispatcher's cold launch of a pinned, non-always-on standing agent
   composes role bundles and attachments but **neither** `ReplyStyle` **nor** `SystemPromptAppend`
   (`AgentTaskDispatcher.ComposeDelegateArgs`). CARD-0058 recorded this gap and it is still open.
   Always-on agents (the orchestrator included) never take that path, so it is not this card's
   defect — but it is the last launch path where a chosen style silently disappears, and `codex`
   (Caveman, not always-on) is a live instance (S3).

**Scope decision, in the card's terms:** neither "lift the by-name rejection" nor "a smaller style
bundle mechanism". The rejection stands, the mechanism exists. Close CARD-0078 against S1+S2, with
S3 as the caller's call. Do not open a duplicate: CARD-0060 is Done and its closure note already
names this block as the one loose end.

---

## Live evidence, reconciled

| Fact | Value | Where |
|---|---|---|
| Agent | Antiphon-Orchestrator `a392cbc4-0fc0-4603-b4d0-5198d1929718`, AlwaysOn, ClaudeCode, ModelLevel High, modelId `opus` | `GET /api/agents/{id}` |
| `replyStyle` | `Normal` (DB `ReplyStyle = 0`) | same; `Agents` table |
| `systemPromptAppend` | `## Reply style: CAVEMAN` … `Correctness beats brevity: never drop a caveat, a risk, or an "I was wrong" to save words.` — 730 chars, 14 lines | same |
| `attachedBundleKeys` | `["board-api", "orchestrator"]`, both rows `CreatedAt 2026-09-02 04:10:02Z` (SetAsync replaces wholesale, so both stamp the last edit) | `AgentBundleAttachments` |
| `composedBundles` (what the NEXT launch carries) | `["board-api v7fc98677", "orchestrator v1937c405"]` — no style stamp, because Normal composes to nothing | DTO, recomputed per request |
| Live session | `fdf1dd3d`, created 2026-08-16 16:12:55Z, last started 2026-09-01 18:49:30Z (resume), `--append-system-prompt` re-passed on resume | `AgentSessions`; `Resume_launch_also_carries_append_system_prompt_and_delivers_restart_note` |
| Session's stored stamp | `board-api v7fc98677` **only** | `AgentSessions.ComposedBundleStamp` |
| `bundlesOutOfDate` | `true` — the `orchestrator` bundle was attached this morning, ten hours after the running session launched. The badge is the mechanism working. The CAVEMAN block is unaffected: it is not a bundle, so it is not in the stamp either way | README "The drift badge" |
| Any other source of that text? | None. `grep -ri caveman` over the repo finds only the bundle files, the enum, the client options, tests, plans and the board scan. Not in AGENTS.md, CLAUDE.md, the orchestrator bundle, or the user-level CLAUDE.md | this pass |

The composition the running process actually received on 2026-09-01 18:49 was therefore:
`[bundle:board-api v7fc98677]` + its text, a blank line, then the hand block verbatim
(`InstructionBundleComposer.Compose`, SystemPromptAppend last, untrimmed). The style enum contributed
nothing.

**Fleet view (45 agents):** one agent is styled — `codex` (`06a847ea`, Caveman, Stopped,
`ReplyStyle` last written 2026-08-30 17:36Z). Every other agent, including the two other standing
orchestrators (Gym Stat Orchestrator, school-revision) and the second Antiphon-board agent
(`Antiphon`, 8478998e), is Normal. None of them carries style text in `SystemPromptAppend`. The
`orchestrator` preset deliberately prefills Normal (`77613925`, matching the Gym Stat precedent).
So "other orchestrators have no equivalent" is not a gap: they have the same control and chose
Normal.

---

## Premise corrections (copy onto the card when closing)

- **P1 — wrong card, wrong status.** The speak-type slider is CARD-0060 ("Reply style should be a
  choice when creating an agent, not hardcoded prose in one row"), created 2026-08-16 20:22, Done
  2026-08-17 08:45 as slices 4–5 of the CARD-0058 plan (`7260e306`, `ce735382`, merged
  `ec5fec56`, migration `20260817080706_AddAgentReplyStyle`). CARD-0061 is check-in backoff.
- **P2 — the rejection is a redirect, not a closure.** `AgentBundleAttachments.Validate` refuses
  `style-*` keys on `bundleKeys` with a message naming `ReplyStyle` as the control. The slice-6
  decision record (`b29f3f35`, spec §7 slice 6) gives the reason: two controls that could contradict
  each other give an agent two voices with nothing to dedup against. That reasoning is intact.
- **P3 — the composition order already answers "per-agent, and the agent's own contract wins".**
  Attached/role bundles → style block → `SystemPromptAppend` last
  (`InstructionBundleComposer.cs:38-44`; pinned by
  `the_style_block_never_outranks_the_agents_own_contract`). A channel agent on Telegram keeps
  Normal; the orchestrator picks its own. There is no global setting to argue against.
- **P4 — the "decays within a session" observation has a mechanical explanation that is not
  storage.** `SystemPromptAppend` reaches a session **only at its next launch**; editing it on a
  running agent types nothing (the same invariant
  `Attaching_a_bundle_to_a_running_agent_raises_the_drift_badge_and_touches_nothing_else` pins for
  bundles), and — unlike a bundle or a style — an edited append is **not** in the drift stamp, so no
  badge says the running session lacks it. On 2026-08-16 the agent crashed and restarted at 16:12Z
  (incidents `Crash` / `RestartScheduled` / `Recovered 16:23Z`), and the hand block was set "by hand
  on 2026-08-16" (CARD-0060). If the block landed after that restart, the session the operator was
  correcting that day had never received it. This pass cannot prove the order from stored data, but
  it is the only mechanism in the code that produces the symptom. Once the text is in
  `--append-system-prompt` it is re-sent on every API call and survives compaction (spec D1, the
  OpenClaw lesson in `ChannelPreamble`).
- **P5 — the orchestrator's session today lacks the `orchestrator` bundle, not its style.** Stamp
  `board-api v7fc98677` vs. a recomputed `board-api, orchestrator`. One relaunch fixes both that
  and, after S2, the style.

---

## Decision

- **D1 — Scope.** Keep the by-name rejection. Build no new mechanism. Reword `style-caveman.md` to
  carry the operator's Caveman — the CARD-0060 table's definition, which is the hand block — and
  move the orchestrator onto the enum. Close CARD-0078 on that, recording P1–P5.
- **D2 — Reword, not a fifth value.** The alternative is to leave grunt-speak as `Caveman` and add
  `AgentReplyStyle.Brief = 4` for decision-bullets. Against: CARD-0060's own table already says what
  Caveman means, so the shipped file is the deviation; a scale with two adjacent "fewest words"
  points is the slider losing its meaning; and a fifth value touches the enum, the client union and
  options, the setup catalog, `project.ps1`'s `ValidateSet`, the `[Arguments]` rows in
  `AgentReplyStyleTests`, and the `InstructionBundleTests` key-set pin — for a distinction nobody
  asked for. For: one agent (`codex`) was put on Caveman on 2026-08-30 and may want grunt-speak for
  its own sake. **Recommendation: reword.** If the caller wants grunt-speak preserved, S1 becomes
  "add `Brief`" with the same shape and roughly double the surface; everything else in this plan is
  unchanged except the enum value named in S2.
- **D3 — The new `style-caveman.md` text** is the hand block with three mechanical edits: no `##`
  heading (bundles have no title heading — README; the first line doubles as the catalog `Summary`),
  the two phrases the existing test pins are kept (`stay exact`, `Code, commands and quoted output
  are written normally`), and the file ends with `AgentReplyStyles.CorrectnessSentence` byte for
  byte (pinned for every style). Proposed text:

  ```
  Reply style: caveman.

  Short bullets. Minimum words. Only what changes a decision.

  - Lead with the outcome or the number. No preamble, no restating the question.
  - Bullets, not paragraphs. One fact per bullet.
  - Drop hedging, throat-clearing, transitions, and summaries of what you just said.
  - Cut anything the reader already knows or can see.
  - Name the thing that is wrong or blocked FIRST, before the things that went fine.
  - Numbers, ids, file:line, commit hashes - not adjectives.
  - Long prose only when the reader must decide something and needs the reasoning; then keep it
    to the smallest argument that supports the decision.
  - Paths, flags, identifiers and numbers stay exact. Code, commands and quoted output are written
    normally - the style is how you talk, not what you type.

  Whatever the style: never drop a caveat, a risk, an uncertainty or a correction to save words.
  ```

  The operator's closing line ("Correctness beats brevity …") is subsumed by the pinned sentence;
  keeping both would say the same thing twice on every launch.
- **D4 — The orchestrator migration is an operator write, not a data migration.** PATCH
  `replyStyle: "Caveman"` and `systemPromptAppend: ""` (whitespace clears — `AgentService.cs:577-584`).
  One consequence to accept knowingly: the launch-notes gate keys on a non-empty
  `SystemPromptAppend` (`AgentControlService.cs:255-270`), so after the PATCH the orchestrator stops
  receiving `BootstrapBody` / `RestartResumeBody` at launch. Those bodies order a channel-agent
  ritual — read SOUL.md, USER.md, MEMORY.md, today's memory log, BOOTSTRAP.md — and **none of those
  files exist in `C:\src\Antiphon`**; the orchestrator has been paying a turn to say so on every
  launch. Losing the notes is the check-interpreter carve-out's reasoning applied to a second agent.
  `A_style_alone_produces_the_flag_but_still_no_launch_notes` already pins this shape. If the
  caller wants the notes kept, leave one non-style line in the append; the plan recommends not.
- **D5 — Takes effect at the next launch, and the badge already says so.** After the PATCH the DTO
  shows `composedBundles` gaining `style-caveman v<hash>` and `bundlesOutOfDate: true` (it is true
  already, P5). Because the running session **is the caller's own**, the relaunch is a boundary
  decision, not part of the PATCH: `POST /api/agents/{id}/stop`, and supervision brings the
  always-on agent back with `--resume` (verified path `AgentSupervisorService.cs:202` →
  `AgentControlService.StartAsync`). Verification is the stored stamp, not the screen:
  `AgentSessions.ComposedBundleStamp` must read `board-api v…, orchestrator v…, style-caveman v…`.
- **D6 — S3 is worth doing and separable.** `ComposeDelegateArgs` composes
  `InstructionBundles.ForDelegate(kind, role, attachedKeys)` only. For a pinned **non-pool** agent it
  should also pass `AgentReplyStyles.ComposedKey(agent.ReplyStyle)` and `agent.SystemPromptAppend`,
  then render `{agentName}` / `{channels}` over the composed text exactly as
  `AgentSessionLaunchComposer` does (`:80-88`) — the placeholder leak CARD-0058 warned about is
  why this was left undone. Pool delegates are unchanged **by construction** (Normal + null append
  compose to the same bytes; pin it). Check tasks keep composing nothing: the carve-out is about
  what the specialist can obey, and the interpreter is always-on so it never takes this path.

---

## Ground truth (file:line, verified 2026-09-02)

- **G1** `server/Domain/Entities/Agent.cs` — `SystemPromptAppend` (:46), `ReplyStyle` (:56, default
  Normal), `BundleAttachments` (:165). `AgentReplyStyle.cs` — Normal=0, Terse=1, Caveman=2,
  Explanatory=3; Caveman's XML doc reads "Short word. Drop small word." (:20). Column is `integer`
  (`20260817080706_AddAgentReplyStyle.cs:15-20`), so a value change needs no migration.
- **G2** `server/Application/Services/AgentReplyStyles.cs` — `BundleKey` total, `ComposedKey` null
  for Normal, `CorrectnessSentence` constant. `InstructionBundles.cs` — `StylePrefix = "style-"`,
  `IsStyle`, `Attachable` excludes styles.
- **G3** `server/Application/Services/AgentBundleAttachments.cs` `Validate` — unknown keys 422;
  style keys 422 with the "two voices" message. Pinned by `AgentBundleAttachmentTests.cs:57`.
- **G4** `server/Application/Services/InstructionBundleComposer.cs` — order bundles → style →
  append (:38-44, :70-100); `StampLine` is bundle stamps only, so an append edit never changes it
  (:27-33); `IsOutOfDate` (:113).
- **G5** `server/Application/Services/AgentSessionLaunchComposer.cs:74-88` — the standing-agent
  composition, `ChannelPreamble.Render`, budget guard, `--append-system-prompt` / `--rules` /
  `developer_instructions` by kind. `AgentControlService.cs:230` calls it; `:255-270` launch-notes
  gate keyed on `SystemPromptAppend`; `:293` and `:327` write `ComposedBundleStamp` on resume and
  fresh.
- **G6** `server/Application/Services/AgentTaskDispatcher.cs:2716-2780` `ComposeDelegateArgs` —
  role map + attachments only; no `AgentReplyStyles`, no `SystemPromptAppend` anywhere in the file.
  `PlaceOnStandingAgentAsync` (:3218-3222) — no live session: AlwaysOn → `WaitForAgent`, otherwise
  `SpawnFresh`, which reaches `BuildLaunchSpecAsync` (:2189-2190, :2329-2330) → `ComposeDelegateArgs`.
- **G7** `server/Bundles/style-caveman.md` — grunt-speak text; `style-terse.md` is the nearest
  shipped neighbour to the hand block but lacks "one fact per bullet", "broken thing first",
  "numbers over adjectives", and the decision-only rule for prose. `server/Bundles/README.md` —
  no mention of styles at all.
- **G8** Blurbs that restate the bundle: `client/src/api/agents.ts:66-70`
  (`AGENT_REPLY_STYLE_OPTIONS`), `server/Application/Services/ProjectSetupService.cs:378`
  (`ReplyStyleCatalog`). Neither blurb is asserted by any test (`grep "Short word"` hits only
  `agents.ts`). Consumers: `ReplyStyleControl.tsx` (description under the SegmentedControl),
  `AgentsPage.tsx:606` (`ReplyStyleBadge`), `project.ps1 catalog`.
- **G9** Tests that pin the style mechanism: `AgentReplyStyleTests.cs` —
  `every_style_block_ends_with_the_correctness_sentence` (all four), `every_enum_value_names_a_bundle_that_actually_ships`,
  `a_chosen_style_composes_its_block_under_a_versioned_header`,
  `the_caveman_block_keeps_code_and_identifiers_out_of_the_voice` (:143-152, pins `stay exact` and
  `Code, commands and quoted output are written normally`). `InstructionBundleTests.cs:57` pins
  the key set (unchanged by a reword). `AgentSystemPromptLaunchTests.cs:406-570` pins the launch
  path. `DelegateBundleLaunchTests.cs:29-180` pins the dispatcher composition (S3's spec).
- **G10** Write surfaces for the enum: settings modal (`AgentSettingsModal.tsx:188`, sends
  `replyStyle` and trimmed `systemPromptAppend` on every Save), create modal,
  `PATCH /api/agents/{id}` (`UpdateAgentRequest.ReplyStyle`, null = unchanged; `SystemPromptAppend`
  whitespace = clear), `POST /api/projects/setup`, `scripts/project.ps1 new -ReplyStyle`.
- **G11** Docs: `docs/antiphon-api.md:174-175` lists what PATCH sets and omits `replyStyle` and
  `systemPromptAppend`; `docs/agent-kinds.md:75-87` documents the standing-instructions channel per
  kind but not the composition order or the style; `docs/orchestration-loop.md:40-42` names the
  `orchestrator` bundle for standing orchestrators and says nothing about register. No owner doc
  mentions reply style.

---

## Slices

### S1 — the enum's Caveman becomes the operator's Caveman (Shared, ~1–2 h)

1. Replace `server/Bundles/style-caveman.md` with the D3 text (LF, no heading, ends with the
   pinned sentence).
2. Update the three restatements: `AgentReplyStyle.cs:20` XML doc; `agents.ts:66-70` description
   → "Short bullets, minimum words, only what changes a decision. Paths, flags and code still
   exact."; `ProjectSetupService.cs:378` to the same string.
3. Tests. `the_caveman_block_keeps_code_and_identifiers_out_of_the_voice` passes unchanged (both
   phrases kept). Add one pin so the reword cannot silently regress to grunt-speak:
   `the_caveman_block_asks_for_decision_bullets_not_grunt_speak` asserting the block contains
   `"Only what changes a decision"` and `"Lead with the outcome"` and does not contain
   `"Talk like caveman"`. Run `AgentReplyStyleTests`, `InstructionBundleTests`,
   `AgentBundleAttachmentTests`, `AgentSystemPromptLaunchTests` (targeted, per the harness rule),
   and `scripts/test-client.ps1` for `AgentReplyStyle.test.tsx` / `ProjectSetupModal.test.tsx`.
4. `server/Bundles/README.md`: a "Reply styles" section (three sentences): the four `style-*`
   files are chosen through `Agent.ReplyStyle`, never attached (`bundleKeys` refuses them 422);
   Normal composes to nothing; the block sits after attachments and before the agent's own
   `SystemPromptAppend`, which keeps the last word.
5. Spec `2026-08-16-card-0058-0059-0060-instruction-bundles.md` §6: one dated line recording that
   the Caveman text was realigned to the card's table on this date and why.

**Version effect:** `style-caveman` gets a new content hash. `codex` (Stopped) shows nothing until
its next launch; no running session changes. The orchestrator is not on Caveman yet, so S1 alone
changes nothing live.

### S2 — move Antiphon-Orchestrator onto the enum, and say where style lives (ops + docs, ~1 h)

1. Deploy S1 (server restart; migrations none).
2. PATCH the agent. Simplest is the settings modal (Agents → Antiphon-Orchestrator → Reply style
   `Caveman`, clear "System prompt append", Save — the modal sends both fields). Equivalent curl,
   echoing the required positional fields from the current row:

   ```
   curl -s -X PATCH http://localhost:17202/api/agents/a392cbc4-0fc0-4603-b4d0-5198d1929718 \
     -H "Content-Type: application/json" \
     -d '{"name":"Antiphon-Orchestrator","workingDirectory":"C:/src/Antiphon","details":"",
          "assignmentPolicy":"AutoPick","replyStyle":"Caveman","systemPromptAppend":""}'
   ```

   `autoCompact*` are applied-even-when-null and are null on this agent today, so omitting them
   is a no-op. `bundleKeys` omitted = attachments unchanged.
3. Confirm the DTO: `replyStyle: "Caveman"`, `systemPromptAppend: null`, `composedBundles` =
   `["board-api v…", "orchestrator v…", "style-caveman v…"]`, `bundlesOutOfDate: true`.
4. Relaunch at a boundary the caller chooses (`POST /api/agents/{id}/stop`; supervision resumes).
   Verify `AgentSessions.ComposedBundleStamp` for the new/resumed session carries all three stamps
   and `bundlesOutOfDate` reads false. Transcript-confirm the first reply is in register — that is
   the canary the card asked for, and it is the same check the operator has been doing by eye.
5. Docs (three lines, owners per AGENTS.md map): `docs/antiphon-api.md:174` add `replyStyle` and
   `systemPromptAppend` to the PATCH list, with "style keys on `bundleKeys` are 422";
   `docs/agent-kinds.md` §3 add the composition order (attachments → `ReplyStyle` block →
   `SystemPromptAppend`, Normal composes nothing) and the effect-at-next-launch rule;
   `docs/orchestration-loop.md:40-42` one sentence: a standing orchestrator's register is
   `ReplyStyle`, chosen per agent, not prose in its prompt append.
6. Close CARD-0078 with a terminal reason carrying P1–P5 and the S1/S2 commits; fix the
   CARD-0061 → CARD-0060 reference in the reason rather than editing the stale description.

### S3 — optional: a pinned standing agent keeps its style and contract on a dispatcher cold launch (Shared, ~2–3 h)

1. `ComposeDelegateArgs`: when `!agent.IsPoolDelegate` and `task.Role != AgentTaskRole.Check`
   (the carve-out keys on the role — `InstructionBundles.ForDelegate`, `InstructionBundles.cs:173`),
   compose `InstructionBundleComposer.Compose(ForDelegate(...), AgentReplyStyles.ComposedKey(agent.ReplyStyle), agent.SystemPromptAppend)`;
   load the agent's enabled `ChatChannels` and apply `ChannelPreamble.Render(text, agent.Name, channels)`
   before the budget guard, mirroring `AgentSessionLaunchComposer.cs:80-88`. Pool rows and Check
   tasks keep today's call byte for byte.
2. Tests in `DelegateBundleLaunchTests`: `a_pinned_standing_agents_style_and_own_contract_ride_its_cold_launch`
   (style block after the role bundles, append last, placeholders rendered);
   `a_pool_delegate_launch_is_byte_identical_with_and_without_the_new_parameters`;
   `a_check_task_on_a_pinned_agent_still_composes_nothing`; and the existing ten stay green.
3. Docs: `docs/orchestration-loop.md:193-199` gains "a task pinned to a standing agent that is not
   always-on launches with that agent's own style and prompt append as well".

If S3 is deferred, record it in CARD-0078's closure as the known remaining path, with `codex` as
the example, so it is not rediscovered as a fourth card.

---

## What this card does not do

- Lift the style-bundle rejection on `bundleKeys`. The two-voices reasoning holds and the enum is
  the control.
- Add per-conversation or per-channel style. Per-agent is the shipped shape and the card's own
  argument lands there.
- Style pool delegates. CARD-0060 deferred that pending "a week of reports"; that evidence was
  never gathered and this card does not gather it.
- Type anything into a running session, or relaunch on an edit. The badge-not-action decision
  (`9beef604`) stands; S2 relaunches by the caller's hand.
- Change the `orchestrator` preset's Normal default (`77613925`) or any other agent's style.

## Left open, deliberately

- **An edited `SystemPromptAppend` is invisible to the drift badge** (G4). It was the likely cause
  of the card's "decays within a session" observation (P4). After S2 the orchestrator's register
  rides a bundle stamp and is badged like everything else, so the gap no longer bites *this* agent;
  it still bites any agent whose contract is hand prose (channel agents, the check interpreter). A
  follow-up would append `append v<hash8>` as a pseudo-stamp in `StampLine` — small, but it changes
  the `""`-vs-null semantics `An_agent_with_no_attachments_and_no_style_records_an_empty_stamp_not_a_null_one`
  pins, so it wants its own card, not a rider here.
- Whether `Antiphon` (8478998e) and the two other standing orchestrators should pick Caveman: the
  operator's per-agent choice; the Gym Stat precedent chose Normal on purpose.

## Test matrix

| Change | Pinned by | Run |
|---|---|---|
| Caveman text ends with the sentence; every enum value has a file; composes under a header | `AgentReplyStyleTests` (existing) | targeted |
| Caveman keeps code exact | `the_caveman_block_keeps_code_and_identifiers_out_of_the_voice` (existing, unchanged) | targeted |
| Caveman is decision-bullets, not grunt-speak | new pin (S1.3) | targeted |
| Bundle key set unchanged | `InstructionBundleTests.cs:57` | targeted |
| Style keys still refused on `bundleKeys` | `AgentBundleAttachmentTests.cs:57` | targeted |
| Standing launch: style before append, no notes with style alone | `AgentSystemPromptLaunchTests` :429, :451 | targeted |
| Client picker/chip render the new blurb | `AgentReplyStyle.test.tsx`, `ProjectSetupModal.test.tsx` | `scripts/test-client.ps1` |
| S3 dispatcher composition | `DelegateBundleLaunchTests` + three new | targeted |
| S2 live | DTO fields, `AgentSessions.ComposedBundleStamp`, transcript first reply | manual, recorded in the closure |

## Sequencing and risks

- S1 → deploy → S2 → (S3 any time). S1 and S3 are independent code; S2 depends on S1 being
  deployed or the orchestrator would relaunch into grunt-speak.
- **The relaunch in S2 kills the caller's own session.** Do the PATCH whenever; do the stop only at
  a boundary the orchestrator chooses. Until then the badge is the truthful state.
- **Losing the launch notes (D4)** is deliberate. If a future CLAUDE.md ritual matters for the
  orchestrator, the gate should be changed to key on something other than "has an append" — that
  is CARD-0047's gate remark resurfacing, not this card.
- S3's placeholder rendering is the one place to get wrong: a channel-bound, non-always-on standing
  agent pinned to a task would otherwise receive literal `{channels}`. The test in S3.2 must use an
  agent with a bound channel.
- Harness rules apply: build with `--property:OutputPath=bin-<name>/` and delete the directories;
  TUnit via `dotnet run --project tests/Antiphon.Tests` with a treenode filter, never `dotnet test`.

## Execution notes

- Shared workspace is fine for S1 and S3 (bundle text, two blurbs, one dispatcher method, tests);
  nothing here touches the daemons' bin directories.
- The `codex` agent's next launch will carry the reworded Caveman. If the operator set it to
  Caveman for grunt-speak on purpose, D2's alternative applies — decide before S1 lands.
- Card closure text should lead with P1 (the wrong card number and the eight-and-a-half-hour gap),
  because that is the fact most likely to be re-misread by the next reader.
