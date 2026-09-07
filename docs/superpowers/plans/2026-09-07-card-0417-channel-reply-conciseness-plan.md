# CARD-0417: concise replies for channel audiences

Date: 2026-09-07. Plan task: `23acc4a0`. Source checkout: `2c405c71`.
Status: design complete; implementation and live acceptance pending.

Add an opt-in `ReplyStyle.Phone`, scoped to replies delivered into Telegram or
Slack. Keep delegate reports under their existing reporting contracts. Start with
one bound agent, then expand only after reviewed before/after real replies pass.
This plan changes no live agent, channel, prompt, or message.

## Decisions

**D-1 — The audience determines the scope.** Apply the new guidance to the human
chat replies of channel-facing standing agents, including their user-facing
follow-ups to task completions, checks, and scheduled prompts. Do not apply it to
delegate/worker reports, delegation briefs, stage artifacts, specialist outputs,
or terminal-only conversations. A standing orchestrator can serve both audiences:
its channel summary is short; the worker report it reads can contain evidence,
tables, exact commands, and the complete stage handoff. Do not pass the phone rule
down in delegate goals. Being AlwaysOn or named Orchestrator is insufficient.

The four unbound project agents named in the card become eligible when their
actual Telegram/Slack bindings exist. No binding is created by this card. The
operator's suggestion of "maybe all agents" is resolved as **channel audiences
only**, rather than an unresolved permission question.

**D-2 — Add `Phone = 5`; do not repurpose an existing style.** One new embedded
`server/Bundles/style-phone.md` is the durable owner of the wording below.
`AgentReplyStyles` already derives the key from the enum name. Keep Normal=0,
Terse=1, Caveman=2, Explanatory=3, and Brief=4 exactly. The existing integer column
needs no data migration or default change; confirm the model still has no enum
check constraint when implementing. Keep create, worker, and orchestrator preset
defaults Normal. Phone is selected explicitly, through the existing style control
or API, and is rejected as a manual `bundleKeys` attachment like every style.

Why the alternatives lose:

- Tightening Brief would change the internal audience deliberately served by its
  "same voice to a human, an orchestrator, or as your own final report" sentence.
  It also requires three to four bullets and forbids paragraphs; neither is a
  faithful minimum-answer default for a one-fact chat reply.
- Terse lacks the phone layout, five-to-seven-word target, and audience boundary.
  Rewording it would silently alter existing non-channel users.
- Caveman deliberately changes grammar. Short natural English is the requirement.
- Changing Normal would break its no-instruction compatibility contract.
- Copying stronger prose into each SystemPromptAppend creates independently
  drifting copies, without a content-version stamp for the added prose.
- Automatically injecting a channel policy on every launch would need a new
  composition/selection mechanism and would activate it across the fleet before
  the requested one-agent trial.
- A formatter, hard word limit, truncator, or distiller cannot decide which fact
  is essential. Output distillation has its own contracts and rollout governance.

**D-3 — Retain the existing channel boilerplate.** This implementation adds a
style, not another copy of it in `ChannelPreamble.BuildPreset` or live appends.
"Keep replies phone-sized. Use plain Markdown only — no tables." is compatible
with Phone; retain those sentences and all surrounding transport instructions.
Do not overwrite an old deployed preamble with today's entire template just to
change verbosity: the templates differ in unrelated delivery and attachment
details. The first three scoped agents' appends contain no conflicting verbosity
instruction. Preserve them byte-for-byte when setting `replyStyle: Phone`.

The channel paragraph of `orchestrator.md` already asks for a human summary of
what changed, what happens next, and a necessary question. Keep it: short bullet
lines satisfy that intent, and its NO_REPLY/attachment obligations remain binding.
Do not add the new rule to `board-api.md`, `delegate-basics.md`, any stage bundle,
or the formatter's delegate report contract.

**D-4 — Brevity is a default, not permission to omit an answer.** Five to seven
words is an approximate target for ordinary prose bullets, not a token gate or a
minimum to pad up to. One fact can be one line. A request for a detailed explanation
gets one, arranged vertically for a phone. Preserve material caveats, uncertainty,
corrections, quantities, deadlines, and necessary actions. Let the user request
extra detail; do not end every answer with an offer or a follow-up question.

**D-5 — Select one live canary explicitly.** Default candidate: Slack Test, whose
current two Slack bindings make its scope visible. Its name does not prove the
chats are disposable: confirm the intended conversation from the catalog before
using it. AZ Care or Family is an acceptable substitute if it supplies suitable
real turns. Use one actual standing agent, its existing provider/model and real
channel delivery. An isolated clone or a fake model is useful preliminary evidence
but does not satisfy the card. No mass PATCH or default flip precedes live review.

## Ground truth

Read the complete live card with `pwsh -NoProfile -File scripts/card.ps1 get
CARD-0417`. Read-only live evidence is from `GET /api/agents`, individual agent
details, and `GET /api/channels` on 2026-09-07; these facts must be refreshed before
implementation touches live configuration.

| Card assumption / question | Observed truth | Consequence |
|---|---|---|
| The listed project agents all face channels | Only AZ Care, Family, and Slack Test have enabled bindings in this catalog | Select by binding and intended audience, not name |
| Reply style may need a mechanism | The enum, embedded styles, composer, API, client picker, setup catalog, and CLI already exist | Extend that mechanism with Phone |
| Existing phone prose is insufficient | AZ Care, Family, and Slack Test retain the vague phone-sized/no-tables sentences in their appends | Opt those rows into the versioned style; a source preset edit alone would miss them |
| Conciseness can be changed everywhere alike | Brief explicitly serves workers; stage-code explicitly requires a verification table | Keep phone rules out of the report audience |
| Updating the row updates an active prompt | Style composes at launch; refresh is policy- and idle-gated | Verify loaded style before judging after replies |
| All launch paths inherit a standing agent's style | Normal standing/card launch uses style+append; `AgentTaskDispatcher.ComposeDelegateArgs` composes role/attachment bundles only | Do not fix the deferred dispatcher gap as part of this card |
| Current preset equals deployed preambles | AZ Care/Family/Slack Test have older transport wording than `ChannelPreamble.BuildPreset` | Do not replace complete appends as a style migration |
| A short rendered sample proves the wording worked | A golden prompt or fake reply proves wiring, not model behavior | Require actual model outputs and their delivered chat rendering |

### Live rollout inventory

All listed channel candidates currently use ClaudeCode and are AlwaysOn. IDs are
observations of this deployment, not constants to embed in application code.

| Agent | Agent ID | Current style / bundle keys | Enabled binding status / append style evidence |
|---|---|---|---|
| AZ Care | `8acdd711-e289-427b-9266-ea1f3230825a` | Normal / none | Telegram AZ Care; old phone-sized/no-tables preamble |
| Family | `a7647365-4803-4541-a457-ba9e7fcfa8f0` | Normal / none | Telegram Family; same old preamble. Its Mike binding is disabled |
| Slack Test | `eecab440-0f89-4691-9596-ea1e8ff049d0` | Normal / none | Two enabled Slack rows; one titled general, one untitled; old Slack preamble |
| Torquay Leander | `ff20b0ef-ae55-4f32-bf2a-3aa5117ab410` | Normal / none | No catalog binding; append null |
| school-revision | `0713b081-10b8-4ec2-8ebd-296e885b38f3` | Normal / orchestrator, board-api | No catalog binding; project and test/deploy instructions, no reply style |
| markdown-package Orchestrator | `6c35a617-4a1c-48d4-9d61-f33900ab5336` | Normal / orchestrator, board-api | No catalog binding; project orchestration instructions, no reply style |
| Gym Stat Orchestrator | `cec87812-c785-4c92-9f0e-78f5b1fa25bc` | Normal / orchestrator, board-api | No catalog binding; project orchestration instructions, no reply style |
| Antiphon-Orchestrator | `a392cbc4-0fc0-4603-b4d0-5198d1929718` | Brief / board-api, orchestrator | No catalog binding; append is an existence-conditional startup ritual. Leave Brief |

ClaudeBot-Antiphon and Codeperf were also checked: Normal, no bundles, no append,
and no enabled catalog binding. They are not omitted channel targets. Disabled,
unbound, and retired catalog rows do not extend the rollout set. Named specialist
seats remain outside this card.

## Instruction inventory and ownership

These are the first-party standing instruction sources and the surfaces that
select, compose, describe, or verify them. Historical plans/specs are context, not
additional instructions to inject. Workspace identity/memory files are a separate
input: inspect the chosen canary's relevant files for competing reply rules during
the trial, without treating them as a new fleet-wide editing scope.

| Source | Existing guidance / role | CARD-0417 treatment |
|---|---|---|
| Live `Agent.SystemPromptAppend` via `GET /api/agents/{id}` | Full channel preambles on AZ Care/Family/Slack Test; project-specific append on the three project orchestrators | Preserve current bytes; audit selected agent before activation |
| `server/Application/Services/ChannelPreamble.cs` | `BuildPreset` owns shared Telegram/Slack phone-sized/no-tables text, delivery and attach rules; bootstrap/resume/recovery notes are separate | Keep text and note bodies; link Phone selection from owner docs |
| `server/Domain/Enums/AgentReplyStyle.cs` | Normal, Terse, Caveman, Explanatory, Brief | Append Phone=5 |
| `server/Bundles/style-normal.md` | Natural colleague voice, no filler, length follows content | Keep; Normal still composes nothing |
| `server/Bundles/style-terse.md` | Answer first, one line if enough, lists over narration | Keep |
| `server/Bundles/style-caveman.md` | Drop small words; identifiers/code remain exact | Keep |
| `server/Bundles/style-brief.md` | Three/four decision bullets, never paragraphs, same voice for human and worker report | Keep |
| `server/Bundles/style-explanatory.md` | Answer then alternatives, reasoning, dependencies, source locations | Keep |
| `server/Application/Services/AgentReplyStyles.cs` | Total enum-to-style map and shared correctness sentence | Existing derived mapping should suffice; add coverage |
| `server/Application/Services/InstructionBundles.cs`, `InstructionBundleComposer.cs` | Embedded text, normalized content hashes; role/attachments then style then verbatim append; styles not attachable | Preserve ordering, hashes, dedup, budgets, and attachment refusal |
| `server/Application/Services/AgentSessionLaunchComposer.cs` | Standing/card instructions with style and rendered placeholders | Exercise existing path for Phone |
| `server/Application/Services/AgentTaskDispatcher.cs` | Cold delegate role/attachment composition; warm reuse retains its launched composition | Preserve; no global Phone inheritance |
| `server/Bundles/orchestrator.md` | Channel-facing follow-ups: one/two human lines; NO_REPLY for unchanged checks; stage routing and attachments | Preserve; does not impose phone rules on internal reports |
| `server/Bundles/board-api.md` | Card API, terminal outcome, privacy/report custody; no general verbosity rule | Preserve |
| `server/Bundles/delegate-basics.md`, `stage-*.md` | Complete final report, outcome/verification, required handoff/token; Code requires test results in a table | Preserve |
| `server/Application/Services/DelegationReportFormatter.cs` | Outcome first, no preamble/narration, counts/failures, role-sized spill limits; specialist grammars | Preserve all report and transport budgets |
| `server/Bundles/output-distiller.md`, `check-interpreter.md`, `diagnose.md` | Separate bounded specialist outputs and explicit invariants | Preserve; no distiller prompt or mode change |
| `server/Bundles/Presets/orchestrator-prompt.md`, `AgentPresets.cs` | Project task mandate; both create presets select Normal | Keep defaults and project prompt |
| `server/Application/Dtos/AgentDtos.cs`, `AgentService.cs`, `ProjectSetupService.cs` | Enum in create/update/details; explicit catalog style descriptions | Existing DTO/service plumbing; add Phone to setup catalog |
| `client/src/api/agents.ts`, `api/projectSetup.ts`, `features/agents/ReplyStyleControl.tsx` | String union/options, shared picker and help text | Add Phone union/option; remove misleading "least to most words" description of audience-specific options |
| `AgentCreateModal.tsx`, `AgentSettingsModal.tsx`, `AgentsPage.tsx`, `features/settings/ProjectSetupModal.tsx` | Create/edit/setup selection and badges; Telegram/Slack preset buttons only replace append text | Verify Phone round-trip/visibility; no silent style change when applying a preamble preset |
| `scripts/project.ps1` | Explicit five-value `-ReplyStyle` ValidateSet and help | Add Phone |
| `server/Bundles/README.md`, `docs/agent-kinds.md`, `docs/orchestration-loop.md`, `docs/telegram.md`, `docs/telegram-bot-ops.md` | Operator explanation of instruction ownership, launch and channel rendering | Document Phone opt-in and audience split in the owning paragraphs; avoid duplicate full wording |
| `tests/Antiphon.Tests/Application/AgentReplyStyleTests.cs`, `AgentBundleAttachmentTests.cs`, `InstructionBundleTests.cs`, `ChannelContractsTests.cs` | Existing style, bundle, correctness and frozen transport contracts | Extend style coverage; keep channel snapshot byte-identical |
| `docs/superpowers/specs/2026-08-16-card-0058-0059-0060-instruction-bundles.md`, `docs/superpowers/plans/2026-09-02-card-0078-reply-style-durable-home-plan.md`, `docs/features/007-multi-agent-orchestration/proposal.md`, Telegram bot-agent spec | Historical decisions and report/preamble copies; CARD-0078's executed override deliberately preserved Caveman and added Brief | Cite as history; do not reinterpret older unexecuted alternatives as current behavior |

The Markdown renderer still supports tables as preformatted output. That feature
is not a reason to send them in Phone mode. Do not change the renderer, Slack
adapter limit, `ChannelBridge:MaxReplyChars`, digest formatting, or chunking.

## Exact proposed instruction

Commit the following text as `server/Bundles/style-phone.md`. Its last sentence is
the existing `AgentReplyStyles.CorrectnessSentence` verbatim.

```text
Reply style: phone.

Use this style only for replies delivered to a human in Telegram or Slack,
including chat follow-ups to Antiphon task reports, checks, and scheduled prompts.
For delegate/worker reports, delegation briefs, stage artifacts, specialist outputs,
and terminal-only replies, follow their own contracts; the phone rules below do
not apply. Do not pass these phone rules to delegates.

- Give the minimum useful answer to the current request. Lead with the answer,
  outcome, or blocker. If one short line is enough, use it and stop.
- Prefer bullets when there are several points. One point per bullet; aim for
  about five to seven words each. Use fewer words when enough, and more when
  needed for clarity or correctness. Do not pad or force broken grammar.
- Use short sentences and short lines. Split separate points onto separate lines.
  Use plain Markdown. No tables, aligned columns, or wide code blocks in chat.
  Use short descriptive links instead of displaying long URLs.
- No filler, pleasantries, preamble, repeated question, progress narration, recap,
  or sign-off. Include only what the user needs to understand or act now.
- Do not add background, alternatives, or explanations the user did not ask for.
  Let the user request more. Do not append offers of more help. Ask a question
  only when its answer is needed to continue.
- If the user asks for detail, provide the requested detail in short sections
  and bullets. Put a requested table or wide artifact in a file, with a short
  chat summary and the required attachment marker.
- Keep necessary caveats, risks, uncertainty, corrections, quantities, deadlines,
  and next actions. Never shorten an exact name, path, command, flag, identifier,
  quote, or attachment marker to meet the word or line target. Put long exact
  material in an appropriate attachment when needed; do not break it arbitrarily.
- Follow the channel's delivery and attachment contract. When that contract calls
  for silence, reply exactly NO_REPLY, without bullets or extra text.

Whatever the style: never drop a caveat, a risk, an uncertainty or a correction to save words.
```

UI/setup description, identical in both catalogs:

> Minimal Telegram/Slack replies. Short bullets, about 5–7 words; no tables.
> Delegate reports keep their own contracts.

This is proposed wording for review and testing, not a claim that a model has
already followed it. Keep the bundle under 3,000 characters as a design budget;
use the existing command-line budget guard for composed instructions. Do not add
an output word-count enforcement service. Natural language does not guarantee a
pixel width; verify phone rendering instead of inventing a fixed character limit.

## Implementation slices

### S-1 — Add the explicit style without activating it

Files: `server/Domain/Enums/AgentReplyStyle.cs`, new
`server/Bundles/style-phone.md`, `server/Application/Services/ProjectSetupService.cs`,
`client/src/api/agents.ts`, and `scripts/project.ps1`.

The embedded-resource glob, derived bundle map, existing DTO enum serialization,
service persistence, and shared picker should do the remaining work. Change those
only if a named verification fails for a real reason. Keep all old values and
defaults. Put Phone next to Brief in the catalogs, with an audience-specific
description; assess the six-option control at narrow widths and adjust layout
only if labels become clipped. No automatic selection on binding, preamble-preset
buttons, startup, or database seed.

Tests to extend/run: `AgentReplyStyleTests`, `AgentBundleAttachmentTests`,
`InstructionBundleTests`, `ProjectSetupServiceTests`, and
`client/src/features/agents/AgentReplyStyle.test.tsx`. Check the project CLI's
ValidateSet and help through a local argument-parsing probe with no live writes;
no existing project CLI test file was found. Tests must assert the new value,
not just cast it.

### S-2 — Verify composition and document selection

Add focused cases to `AgentSystemPromptLaunchTests` and, if needed for the existing
fixture boundaries, `DelegateBundleLaunchTests`. Prove a Phone standing launch
contains exactly one style block and the original append, in existing order;
Normal and existing delegate defaults retain their current composed instructions.
Cover a session resume and its observed stamp. Cover mixed-purpose instructions:
the Phone bundle explicitly exempts worker reports, and the stage/report text still
requires its table, evidence, and closing markers.

Run `ChannelContractsTests` unchanged as the transport regression guard. The old
channel preset snapshot is expected to remain byte-identical. Existing policy
refresh tests should be targeted only if refresh plumbing is changed; this design
needs no refresh implementation change.

Update the owner-doc paragraphs listed in the inventory to explain selection,
audience scope, the live gate, and rollback. The bundle remains the sole full
wording copy. Documentation must not claim that updating an append or receiving a
Notify response installs instructions into an already-running system prompt.

### S-3 — Deploy support, then run one real-agent trial

Land the supporting code and deploy from the canonical main checkout under the
existing AppHost runbook. Verify that the loaded API exposes Phone and serves the
new style version before any row selects it. Existing agents retain their old
styles; adding an unused bundle does not change their composed stamps.

Prepare an evidence manifest for the selected live agent: agent ID, all enabled
channel IDs/providers, provider/model/profile identity, selected current style,
exact append hash, attached keys, current session ID, launched style stamp, source
commit, and capture timestamps. Keep credentials and private chat bodies out of
committed artifacts. Confirm relevant workspace instructions do not contradict
Phone. If they do, record the exact competing rule for a scoped resolution rather
than rewriting a project's identity files wholesale.

Capture the before replies before applying the new style. PATCH only the canary's
`replyStyle` to `Phone`, using the existing agent endpoint and a structured body.
Immediately re-read the row and confirm append, model, bundles, and bindings did
not change. At an idle boundary use the supported policy refresh/resume path;
working-session or provider refusals are real refusals, never reasons to kill an
unrelated process or change model. `refreshed: false, notified: true` is not loaded
policy evidence. Record the actual launched `style-phone v...` stamp, then collect
the after replies under the same provider/model and comparable requests.

`AgentDetailDto.ComposedBundles` describes the NEXT launch, not the running one.
`BundlesOutOfDate: false` also covers missing historical evidence. TestDesign must
specify a read-only retrieval of the actual session's `ComposedBundleStamp` or its
correlated launch evidence; do not treat either DTO field alone as proof of the
loaded prompt. Record the session ID and refresh/resume boundary with that stamp.

Source of work for live evaluation: ordinary user-originated channel turns, or
explicitly authorized test messages in the selected conversation. This Plan
dispatch does not send test messages to real chats. Replays must be read-only
answering tasks, never a repeated booking, send, deployment, or other side effect.
The same historical prompt replayed after a change is still subject to changed
context; record that limitation rather than claiming a controlled A/B experiment.

### S-4 — Review evidence, then expand explicitly

Record the reviewer's wording decision and live acceptance result on CARD-0417.
After the real trial passes, apply Phone one agent at a time to the remaining
enabled, intended Telegram/Slack standing agents; the current set is AZ Care,
Family, and Slack Test. Re-query bindings and appends first, verify each loaded
style, and inspect its first real delivered reply. Do not switch unbound project
agents or Antiphon-Orchestrator merely to finish the card's name list.

Future binding setup explicitly selects Phone when the agent will answer humans
in these channels. Keep worker/general preset defaults unchanged. If a later
target has a conflicting custom style instruction, resolve that row's exact
clause while preserving its task and transport instructions; record any such
additional change alongside its before/after evidence.

## Verification requirements for TestDesign

Verification is a separate next stage. TestDesign should turn these requirements
into the repository's V-n/R-n/PC-n matrix, with exact existing test classes and
positive controls. Do not substitute tests that search for a phrase for the live
behavioral gate.

1. Enum/API/client/CLI round-trip Phone; omitted values remain Normal at create
   and unchanged at PATCH. All five old integer values and style texts survive.
   `style-phone` is embedded, versioned, not manually attachable, and ends with
   the common correctness sentence.
2. Standing launch/resume includes the selected style exactly once, with the
   append and transport clauses preserved. No global/default/delegate inheritance
   appears. Non-channel output and a worker report retain their own contract.
3. Reply-style selection changes the current composition stamp and a successful
   refresh records the new launched stamp. A stale or Notify-only session cannot
   be labeled an after sample. The test can use existing mocked runner seams;
   no test host may launch against the production runner.
4. Frozen channel preset, NO_REPLY, attachment marker, and existing report closing
   protocols remain unchanged. Fake channel tests verify delivery/formatting only.
5. The new option is selectable and retained in create/edit/setup, descriptions
   agree, and a narrow picker does not clip labels. Applying a Telegram/Slack
   preamble preset still does not silently change an explicitly chosen style.

Use the named-class TUnit pattern, for example:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0417/ -- --treenode-filter "/*/*/AgentReplyStyleTests/*"
pwsh -NoProfile -File scripts/test-client.ps1 AgentReplyStyle.test.tsx
```

Run any other selected classes with their own exact filters. Follow
`docs/testing-and-build.md` for isolated outputs and cleanup. Tests that spawn a
process need their assembly-local limiter. Do not co-run Antiphon.Tests and
Antiphon.Agents.Pty.Tests. This design changes neither Pty nor session delivery;
do not expand into an unrelated full-suite or production process exercise.

## Mandatory real-reply acceptance

Record at least three before/after pairs from **one actual channel-facing
standing agent**, with original prompt/context, raw final response, session/turn
or transcript sequence, timestamp, and the delivered channel message/rendering.
Use real captured outputs; an editorial rewrite or mock response is not an after
reply. Capture before inputs/outputs before changing the agent. Prompts must cover
a simple answer, a multi-point answer, and a necessary caveat or blocker. Include
an explicit request for more detail and its answer to prove progressive disclosure
still works; this may be an additional turn beyond the three pairs.

Across live samples plus targeted delivery checks, also exercise an exact long
identifier or link, an attachment, and a no-change check requiring NO_REPLY.
An irrelevant scenario need not be forced into a family chat. A task-completion
follow-up should be observed on the real canary if that agent normally handles
delegations. Otherwise use a second scoped real agent for that case before claiming
the orchestrator follow-up audience passed.

Acceptance rubric:

- The current question is fully answered; material facts, caveats, uncertainty,
  actions, quantities, and deadlines survive. No invented certainty.
- A one-fact answer is one short line. Multi-point answers prefer one-point
  bullets, ordinarily around five to seven words. Record justified exceptions
  for exact material or clarity; never expand a three-word answer to meet a floor.
- No unnecessary introduction, repeated question, narration, recap, sign-off,
  unsolicited explanation, or offer of more detail.
- No inline table, aligned columns, or wide prose/code block. Inspect a narrow
  phone-sized rendered view (roughly 360–390 CSS px): short vertical reading,
  no horizontal scanning. Long exact material remains intact via link/attachment.
- The follow-up asking for detail receives the requested substance, not a second
  underspecified summary. Required questions are stated directly.
- The verbose baseline becomes materially shorter while retaining every necessary
  fact. Already-minimal answers need not shrink. Record total words, bullets, and
  ordinary bullet lengths as diagnostics; shorter alone is not a passing verdict.
- Correct NO_REPLY behavior and file delivery remain intact. Match a delivery to
  its actual turn; a screen redraw or healthy service is not proof.

Evidence report format (use repeated sections, not side-by-side wide chat tables):

```text
Agent / channel / model:
Before session + sequence + timestamp + style/stamp:
After session + sequence + timestamp + style/stamp:
Prompt and necessary context:
Before, verbatim:
After, verbatim:
Delivered-message references and phone-render evidence:
Necessary facts retained; word/bullet measurements:
Exceptions, context differences, reviewer verdict:
```

Store full sensitive evidence under the canonical main checkout's ignored
`.antiphon/acceptance/card-0417/` so it survives worktree cleanup. The tracked
evidence summary under `docs/investigations/` should contain only publishable,
sanitized examples, explicit redaction labels, source references, metrics, and
review verdicts. Do not publish private family messages in Git. A redacted sample
is not verbatim; retain the exact original privately for the authorized reviewer.

Do not mark CARD-0417 accepted until reviewed wording is applied to the current
scoped agents, the actual before/after examples exist, and each scoped agent's
loaded policy is verified. A skipped/unavailable real trial leaves acceptance
pending, even if every deterministic test passed. No live replies were generated
or evaluated during this Plan task.

## Rollback and operational limits

Record each target's old style before activation. On a material omission, awkward
compression, broken exact value, or readability regression, stop expansion and
restore that agent's previous style, then verify it is loaded at an idle boundary.
Appends and bindings were preserved, so the normal rollback is one style PATCH,
not a reconstructed prompt. Re-test revised wording on the same canary before
resuming expansion.

Before rolling the application back to a binary without Phone, reset every Phone
row to its recorded old supported style while the new binary is still running.
Verify no Phone values remain, including newly created agents, before downgrading;
an old bundle catalog cannot resolve `style-5`. Retaining support code and changing
the canary's selected style is the less disruptive rollback during the trial.

The late append keeps precedence. Arbitrary future custom appends can conflict
with any selected style; audit them when selecting Phone. This card does not add a
semantic instruction-conflict detector or claim a prompt can enforce formatting.
Cold delegate style omission and provider-specific rules-refresh limitations stay
with their existing owners; use supported standing launches for this acceptance.

## Handoff

Next: TestDesign. Build a focused verification matrix for S-1/S-2 and an executable
evidence procedure for S-3/S-4, preserving D-1 through D-5. Then Code implements
the opt-in style. Landing this plan or shipping unused Phone support does not
close the card; real replies and scoped application are separate acceptance gates.
