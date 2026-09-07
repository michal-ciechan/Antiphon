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

## Verification design

TestDesign task: `8552c9b9`, 2026-09-07, inspected at `1d37c6da`.
This section adds executable verification to D-1 through D-5; it does not change
the wording or activate Phone. Test names prefixed `Phone_` below are additions
for Code to implement, not tests already passing. Existing names are cited as
fixture seams. Server test paths below are under
`tests/Antiphon.Tests/Application/` unless stated otherwise.

**Two release gates:** V-1 through V-8 and the deterministic positive controls
are the support-code gate. V-9 and V-10 are the mandatory post-deployment channel
acceptance gate. Landing unused Phone support is allowed by S-3; expanding beyond
the one canary and accepting CARD-0417 require the live gate. Code reports each
item separately, including `PENDING (post-deployment)` for live work it cannot
yet run. It must not call that a pass or close the card. With support ready but
live acceptance pending, hand off to Review for the explicit support-only landing
and deployment boundary; name `restart: server` and the unfulfilled live gate.
No extra production endpoint, policy-refresh mechanism, formatter, or rollout
automation is needed to implement this verification.

### Proves it works now

| ID | Behaviour / layer | Executable test or probe | Required result |
|---|---|---|---|
| V-1 | Explicit enum and embedded wording / unit assertions in existing fixtures | Extend `AgentReplyStyleTests` and `InstructionBundleTests`, as specified below | Phone is the actual named value 5, embedded once and versioned; old numeric values, defaults and style text remain unchanged |
| V-2 | Real HTTP, persistence, attachment refusal and preserving update / integration | New `AgentReplyStyleEndpointTests`; extend `AgentBundleAttachmentTests`; `ProjectSetupServiceTests.Phone_catalog_and_explicit_setup_preserve_defaults` | POST/PATCH/GET return the string `Phone`; a new DbContext reads integer 5; omitted create remains Normal; omitted update retains its prior style; only the selected row's style changes |
| V-3 | Standing fresh/resume instructions and actual stored launch stamp / integration | Add `Phone_fresh_launch_preserves_order_append_and_stamp` and `Phone_resume_replaces_loaded_stamp_without_changing_append` to `AgentSystemPromptLaunchTests` | Captured launch argument has exactly one Phone block, attachments first, rendered append last; resumed session row records the new stamp |
| V-4 | Delegate instruction boundary, including a mixed-purpose standing composition / integration fixture with no process | Add `Phone_delegate_launch_and_brief_keep_stage_contracts` and `Phone_mixed_composition_exempts_internal_reports` to `DelegateBundleLaunchTests` | No Phone inheritance into cold delegate args or brief; Code instructions retain evidence/table requirements; an explicitly composed Phone block retains the full internal-audience exception |
| V-5 | A full worker report reaches its Phone parent intact / integration | Add `Phone_parent_preserves_worker_table_and_stage_handoff` to `AgentTaskReplyIntegrationTests` | Real settlement preserves report body, exact command, caveat, table, parsed handoff and completion header; no five-to-seven-word rewrite |
| V-6 | Style-only drift and refresh evidence / integration | Add `Phone_refresh_records_loaded_stamp` and `Phone_notify_or_working_keeps_loaded_stamp` to `PolicyRefreshServiceTests`; extend the style drift case in `AgentBundleAttachmentTests` | Saved selection changes desired composition only; successful fake-adapter resume records Phone; Notify and working refusal leave loaded stamp untouched |
| V-7 | UI/CLI selection stays explicit / client integration + local parsing + browser inspection | Extend `AgentReplyStyle.test.tsx`, `AgentCreateModal.test.tsx`, `ProjectSetupModal.test.tsx`; run CLI probe below; inspect picker at 360 and 390 CSS px | Create/edit/setup submit and retain Phone; descriptions agree; old defaults survive; preamble preset does not change style; all six labels remain readable |
| V-8 | Existing channel/report transport / unit + targeted integration | Run `ChannelContractsTests` unchanged, and the exact delivery methods listed below; run the unchanged-source check in R-1 | Frozen preamble, whole-turn NO_REPLY and attachment delivery survive; terminal-only output is not accidentally sent to a channel |
| V-9 | One real canary becomes eligible as an after sample / live probe | Execute the manifest, preserving PATCH, idle refresh and session-stamp procedure below | Real before evidence predates activation; actual loaded Phone stamp and successful launch boundary precede every after sample; saved DTO alone never qualifies |
| V-10 | Reviewed real replies and scoped rollout / mandatory live review | Execute the live sample and review procedure below | At least three genuine before/after pairs, detail follow-up, phone rendering and required follow-up coverage pass; only then expand one bound agent at a time |

#### V-1 / V-2: configuration is opt-in, with meaningful HTTP coverage

- Add `AgentReplyStyle.Phone` to the existing correctness-sentence and
  versioned-header argument cases. Add `Phone_enum_and_audience_contract_are_explicit`:
  assert `(Normal,Terse,Caveman,Explanatory,Brief,Phone) == (0,1,2,3,4,5)`,
  `BundleKey(Phone) == ComposedKey(Phone) == "style-phone"`, exactly one matching
  embedded catalog entry, and normalized bundle text shorter than 3,000 chars.
  Assert the complete scope paragraph, detail-request exception, exact-material
  preservation clause, whole-turn NO_REPLY instruction and correctness suffix.
  Inspect the new file against the exact proposed text above, allowing CRLF/LF and
  the final file newline only. Do not derive the expected paragraph from the bundle
  under test. These checks pin instruction custody; V-10 proves model behaviour.
- Preserve the two existing Normal composition tests. Include Phone in
  `a_chosen_style_composes_its_block_under_a_versioned_header` and assert the
  Phone stamp equals `InstructionBundles.Get("style-phone").Stamp` exactly once.
  Extend the existing command-line budget test with Phone, attachments and a
  representative append; a deliberately squeezed budget must still throw before
  launch, never truncate.
- `AgentReplyStyleEndpointTests` uses `AntiphonWebAppFactory`, its existing
  `ProductionRunnerGuard`/refusing runner, `[Category("Integration")]` and
  `[NotInParallel]`, following `PolicyRefreshEndpointTests`. Use fresh temp
  workspaces and fixture-created IDs; no real agent or live server. Do not request
  Start; explicitly override AlwaysOn to false even when exercising presets.
  Clean up only those IDs. Add these three methods:
  `Phone_create_patch_get_and_omission_round_trip`,
  `Phone_style_update_preserves_other_settings_and_other_agents`, and
  `Phone_manual_bundle_attachment_is_422`.
- First method: create one omitted-style agent and one explicitly Phone agent;
  assert HTTP status and JSON string for POST and fresh GET, then read both rows
  through a fresh context. Change Normal to Phone, PATCH another field while
  omitting replyStyle, and finally reset Phone to Normal. Assert every transition,
  including omission and the default-valued reset. Exercise both worker and
  orchestrator presets with omitted style. Inspect EF's design-time model/check
  constraints and migration diff: no new constraint, migration or default flip is
  justified for integer 5.
- Second method: seed non-default compaction overrides, non-empty Details,
  ManualConfirm assignment, a valid existing workflow-template ID, attachments,
  an append containing CRLF/trailing whitespace, and a sibling agent with Brief.
  Use the preserving request body below. Compare all writable settings before
  and after, including model/profile, append, bundles, compaction and sibling;
  exclude only the intentionally changed style and normal UpdatedAt metadata.
  No style update may create a binding or launch. Do not copy the entire DTO into
  the request: several response fields have different write semantics.
- Third method: try `bundleKeys: ["style-phone"]` through create and update;
  expect HTTP 422 with the style-selection explanation, no partial changes to
  the updated fixture agent. Add Phone to the direct attachment-validator case
  `a_style_is_rejected_by_name_rather_than_treated_as_a_typo` as well.
- Extend `ProjectSetupServiceTests` using its existing `CreateService` and
  no-op directory writer: catalog contains exactly the six styles, Phone next to
  Brief with the specified description. Explicit Phone setup survives GET/DB;
  worker/orchestrator omitted-style setup remains Normal. The existing
  `orchestrator_preset_renders_the_project_facts_and_its_contract` stays green.

#### V-3 / V-6: exercise the real composition and refresh seams

Use the existing `BridgeQueueHarness`, `SetPreambleAsync`, `SetStyleAsync`,
`AttachAsync`, `StartAsync`, `EndSessionAsync` and registering fake adapter.
Parameterize the Phone launch cases over Telegram and Slack preambles, using
`ChannelPreamble.PresetTemplateFor("telegram"/"slack")` with a sentinel append containing an
exact long Windows path and trailing whitespace. Persisted append bytes must
remain identical; only documented placeholders are rendered in the launch copy.
Attach board-api and assert its block precedes Phone, whose block precedes the
rendered append. Assert one system-prompt flag and one Phone block, the exact
transport clauses and the exact session-row stamp, scoped to the launched ID.

For resume, first launch Normal. Record the actual session ID, adapter args and
empty/attachment-only stamp. Set Phone; read desired composition via
`AgentService.GetByIdAsync`, and independently read the old session row. Desired
must change while adapter count, submitted-body count and loaded stamp do not.
End/dispose that actual session in the fixture (not the harness's original stale
ID), resume it, and wait for the launch queue. Assert same resumed ID, `--resume`,
Phone once, exact append, correct restart note and refreshed stored stamp.
Keep `A_normal_style_agent_launches_with_exactly_the_arguments_it_did_before`
and the style-only/no-bootstrap case; add Phone to the latter.

For V-6, reuse `SeedRelaunchReadyAsync` and `WaitForLaunchAsync` in
`PolicyRefreshServiceTests`. Normalize the helper's deliberately stale board-api
and instruction-file stamps to their current values before changing Normal to
Phone, so **Phone is the only drift**. Fresh-read the row to establish the old
stamp. Exercise `AgentControlService.RefreshPolicyAsync` with the fake adapter:

- Auto/eligible/idle: refreshed true, notified false; await queue idle, verify
  new adapter started with `--resume`, Phone text exactly once, and persisted
  current stamp. A response before the queue completes is insufficient.
- Notify/idle: refreshed false, notified true; no new adapter, no kill, old
  composed stamp retained even though desired DTO contains Phone. A system
  notification is permitted; it is not a replacement system prompt.
- Working, even with force: 409 `session_working`, no kill, no launch, no stamp
  change. Also retain existing unbound/Codex Notify and null-stamp tests.

The current plan needs no refresh-plumbing changes. These are narrow Phone
scenario additions proving the existing policy, not a redesign or a full sweep
exercise. Keep assertions scoped to fixture IDs and the existing test-clock
conventions; do not introduce a frozen clock into the message queue.

#### V-4 / V-5: regression proving the internal reporting contract survives

Extend `DelegateBundleLaunchTests.SpecOf` to accept an optional reply style and
append for its in-memory Agent, defaulting to the current values. Call the real
`BuildLaunchSpec` twice for the same Code task/attachments, first Normal then
Phone with a sentinel append. Compare the **instruction argument**, not random
credential environment values: it must be identical, contain the full stage-code
and delegate-basics texts once, and contain neither Phone nor the sentinel.
Check Worker/Code and the existing Check/Diagnose/Distill carve-outs. Build the
actual `DelegationReportFormatter.BuildBrief` and assert the goal and reporting
contract remain present, with no Phone block or phone prose injected.

Also compose `stage-code`, `delegate-basics`, Phone and an internal-report append
through the real composer as a mixed-purpose instruction fixture. Assert the
unaltered stage text still requires a verification table, all V/R/PC results,
and its handoff; assert the exact Phone exception explicitly includes
delegate/worker reports, stage artifacts, specialist outputs and terminal-only
replies. This covers custody when an already-launched standing seat is reused.
It does not claim a mocked model obeyed that exception; do not change warm-pool
reuse or resolve the deferred cold-dispatcher style gap to make a test pass.

For V-5, reuse `AgentTaskReplyIntegrationTests.SeedSessionAsync`,
`SeedDispatchedTaskAsync`, `SeedTurnAsync` and `CreateService().OnTurnEndAsync`,
following `a_settled_investigate_report_stamps_next_stage_plan_and_header_next_plan`.
Seed a parent standing Agent linked by PersistentSessionId, ReplyStyle Phone,
and a Worker/Code task reporting to it. Use the same report with Normal and Phone
parent variants. Optionally set the worker's style Phone in the second variant
to cover a mixed-purpose seat; make both actual Agent rows observable.

The synthetic report must have an outcome sentence, an exact command such as
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0417/`,
a Markdown table with `V-1`, `R-1`, `PC-1` and counts, a necessary caveat longer
than seven words, and this closing block:

```text
--- next stage ---
next: review
handoff: verify all evidence before accepting channel rollout
artifact: docs/superpowers/plans/2026-09-07-card-0417-channel-reply-conciseness-plan.md
```

Append `DelegationReportFormatter.ReportToken(task.Id, "done")` through the
existing seed helper (it normally adds the token; do not add it twice). Settle
through the real service. Assert Succeeded, Result equals the entire report body
verbatim after the existing terminal-token removal, the table/command/caveat
survive, NextStage is Review, NextHandoff matches, and
`PipelineHandoff.TryParse(settled.Result).ArtifactPath` matches the literal path.
Do not assert a nonexistent NextArtifact property. Assert exactly one
parent note carries `next=review` and the intact short report body. Keep the
report below the existing inline limit; keep output distillation at the fixture's
existing non-Apply setting. The transport token is consumed by the existing
protocol, not treated as truncation. This is a real application regression for
report preservation, complementary to instruction-text assertions and live
channel-summary review.

#### V-7 / V-8: client, CLI and delivery checks

Use the current MSW fixtures and actual modal interactions. In the three client
test files named in V-7, select Phone by its accessible radio name, capture the
submitted POST/PATCH/setup body, return Phone, reopen/fresh-fetch and assert it
remains selected. Assert the agent card shows `phone`, while Normal still shows
no chip. Test both old omitted-field fallback and preset default Normal. Click
the actual Telegram and Slack preamble preset buttons after choosing Phone and
after choosing Brief; the selection and submitted replyStyle must stay chosen.
Assert client/server descriptions separately against the exact proposed text
(joined with a single space); do not make one test compare two duplicated wrong
strings. View create, edit and setup at 360 and 390 CSS px in an isolated browser
fixture with mocked HTTP; JSDOM is not evidence of label fit. Retain screenshots
and observed clipping/wrapping verdict. Do not save changes through a live UI.

CLI validation can run without executing the script body or making an HTTP call.
Run this from the implementation checkout with PowerShell; it uses the parsed
script's actual parameter block for argument binding:

```powershell
$parseTokens = $null
$parseErrors = $null
$scriptPath = (Resolve-Path scripts/project.ps1).Path
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'project.ps1 parse failed' }
$bind = [scriptblock]::Create($ast.ParamBlock.Extent.Text + "`nreturn `$ReplyStyle")
foreach ($style in 'Normal','Terse','Caveman','Brief','Phone','Explanatory') {
    if ((& $bind -Verb catalog -ReplyStyle $style) -cne $style) {
        throw "ReplyStyle binding failed: $style"
    }
}
$rejected = $false
try { & $bind -Verb catalog -ReplyStyle NotAStyle | Out-Null }
catch [System.Management.Automation.ParameterBindingException] { $rejected = $true }
if (-not $rejected) { throw 'Invalid style was accepted' }
$helpLine = Get-Content -LiteralPath $scriptPath |
    Where-Object { $_ -match '^#.*\[-Level .*\[-ReplyStyle ' }
if ($helpLine -notmatch '\bPhone\b') { throw 'ReplyStyle help omits Phone' }
```

Run `ChannelContractsTests` byte-identical. Selected delivery regression methods:

- `ChannelBridgeTests.An_attach_marker_sends_the_file_inline_and_strips_the_marker_line`:
  exact attachment bytes/name and stripped marker; keep its temp file local.
- `ChannelBridgeTests.A_marker_only_reply_sends_the_document_with_no_text`.
- `ChannelBridgeTests.A_turn_the_bridge_did_not_start_sends_no_reply`.
- `ChannelFollowUpAttachmentTests.A_later_task_done_turn_with_attach_is_delivered_to_the_settled_conversation`.
- `ChannelFollowUpAttachmentTests.An_exact_NO_REPLY_without_markers_holds_the_bundle`.
- `ChannelFollowUpAttachmentTests.NO_REPLY_plus_marker_sends_the_file_with_empty_text`.

These fake transcripts and fake outbound senders prove routing and file handling,
not concise generation. Keep the existing distinction between exact whole-turn
NO_REPLY and NO_REPLY alongside an attachment; do not rewrite that contract.

### Guards the regression

| ID | Future defect | Caught by / assertion | Positive control |
|---|---|---|---|
| R-1 | Old styles/defaults, delegate stage text, formatter or channel preamble silently rewritten | V-1/V-2 defaults plus the unchanged-source command below; zero diff on preserved sources | PC-1, PC-3, PC-9 |
| R-2 | Phone becomes global, applies internally, or leaks through the dispatcher/brief | V-3 Normal launch equality; V-4 identical delegate instruction argument and explicit exception; V-5 exact settled report/table/header | PC-1, PC-2, PC-4, PC-5 |
| R-3 | A configuration save overwrites unrelated settings or permits a style attachment | V-2 old/sibling setting equality, omitted PATCH semantics and HTTP 422 | PC-6 |
| R-4 | Desired DTO or Notify is mislabeled as loaded policy | V-3/V-6 persisted session stamp plus captured successful resume; V-9 correlation required even if drift badge is false | PC-7, PC-8, PC-10 |
| R-5 | New option lost in create/edit/setup or channel preset silently opts into Phone | V-7 submitted-body/reopen checks, Normal fallback, preset selection invariant, exact catalog description and CLI binder | PC-1 through defaults; V-7 runs on every picker change |
| R-6 | Shortening loses essential facts, detail, exact material, silence or file delivery | V-1 correctness/exception pins, V-8 transport checks, V-10 real replies and narrow rendering review | PC-3, PC-9, PC-10 |

Run from the implementation branch; these sources must remain identical to the
landed plan baseline. This is an intentional scope/byte check, not a new snapshot
of generated model outputs:

```powershell
git diff --exit-code 1d37c6da -- server/Bundles/style-normal.md server/Bundles/style-terse.md server/Bundles/style-caveman.md server/Bundles/style-brief.md server/Bundles/style-explanatory.md server/Bundles/orchestrator.md server/Bundles/board-api.md server/Bundles/delegate-basics.md 'server/Bundles/stage-*.md' server/Bundles/output-distiller.md server/Bundles/check-interpreter.md server/Bundles/diagnose.md server/Bundles/Presets/orchestrator-prompt.md server/Application/Services/ChannelPreamble.cs server/Application/Services/DelegationReportFormatter.cs server/Application/Services/AgentPresets.cs tests/Antiphon.Tests/Application/ChannelContractsTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Preserved instruction or transport source changed' }
```

If upstream independently changed a protected source after `1d37c6da`, attribute
that exact difference before adjusting the baseline for it. Do not bless changes
from this card by refreshing snapshots. The old unused style bundles must also
keep their normalized content versions; adding Phone alone changes no old stamp.

### Positive controls

Run every mutation separately, only in the isolated implementation worktree.
Start from compiled green tests; preserve the intended implementation diff.
Rebuild the mutated source, execute the named test with a fresh TRX, confirm its
specific assertion failure, revert only the mutation, rebuild and see green.
An enum compile failure, fixture failure, no discovered tests, or stale binary is
not a red control. Report red/restore/green evidence for each PC; never push or
deploy a mutant. Existing guards are exercised, not weakened in the final code.

| ID | One-line mutation / negative input | Expected red evidence |
|---|---|---|
| PC-1 | In `AgentReplyStyles.ComposedKey`, replace the Normal arm `null` with `"style-phone"` | Existing Normal composition test and Normal launch case fail equality; restore => green |
| PC-2 | In `style-phone.md`, replace `and terminal-only replies, follow their own contracts; the phone rules below do` with `and terminal-only replies must also follow the phone rules below; they do` | V-1 complete audience paragraph assertion and V-4 mixed composition assertion fail; restore => green |
| PC-3 | Remove the final correctness sentence from `style-phone.md` | Phone argument of `every_style_block_ends_with_the_correctness_sentence` fails; restore => green |
| PC-4 | In `AgentTaskDispatcher.ComposeDelegateArgs`, pass `AgentReplyStyles.ComposedKey(agent.ReplyStyle)` as the second argument of its `InstructionBundleComposer.Compose(...)` call | V-4 Normal-versus-Phone delegate instruction equality/no-Phone assertion fails; restore => green |
| PC-5 | In `AgentTaskReplyService`, replace `task.Result = settledBody;` with `task.Result = settledBody.Split('\n')[0];` | V-5 full-body/table preservation assertion fails at real settlement; restore => green |
| PC-6 | Omit `autoCompactContextPercent` from the V-2 preserving PATCH request builder, while keeping a non-null override in the seeded row and expected baseline | V-2 preservation assertion fails because the endpoint resets the omitted override; restore => green. This injects a bad operational request, not a changed assertion |
| PC-7 | In `AgentControlService`'s resume branch, replace `previous.ComposedBundleStamp = composition.ComposedStamp;` with `previous.ComposedBundleStamp = "";` | V-3 resume and V-6 refresh persisted-stamp assertions fail despite correct args/desired DTO; restore => green |
| PC-8 | In `PolicyRefreshService.NotifyAsync`, add `live.ComposedBundleStamp = currentBundles;` immediately before its existing SaveChangesAsync | V-6 Notify old-stamp equality fails despite no launch; restore => green |
| PC-9 | Change `Keep replies phone-sized.` in `ChannelPreamble.BuildPreset` to `Keep replies short.` | Unmodified `ChannelContractsTests.Preset_contains_envelope_reply_contract_no_reply_and_compaction_note` and R-1 source check fail; restore => green |
| PC-10 | Review a local, clearly labelled negative evidence manifest with all desired DTO fields green but loaded stamp null/old or refresh Notify-only; separately remove one before/after pair or delivered-render reference from a copy of a complete manifest | V-9/V-10 eligibility verdict is **PENDING/FAIL**, never accepted. After the real manifest is complete, restore its evidence and record the qualifying verdict. This is a review-control exercise, with no chat send or fabricated positive sample |

PC-10's invalid-manifest rejection can be rehearsed before deployment; its
positive live counterpart remains pending until V-9/V-10 exist. Live acceptance
is not bypassed by a passing test suite or by completing the rehearsal.

#### V-9: executable live evidence and preserving style update

1. Discover the canonical main checkout with `git worktree list --porcelain`;
   it is currently `C:\src\Antiphon`, not this task worktree. Read the AppHost
   runbook before the S-3 support deployment. After deployment inspect
   `GET /api/projects/setup-catalog` for Phone and correlate the running server's
   loaded assembly/build identity to the verified implementation. Record the
   intended Phone content version from that compiled build's embedded bundle
   (V-1 supplies it); no agent needs to select Phone to compute it. The attachable
   bundle API intentionally excludes styles. A healthy endpoint alone is insufficient. Do not
   activate Phone until support-code verification and deployment succeed.
2. Re-read CARD-0417, `/api/agents`, `/api/agents/{id}` and `/api/channels` with
   the normal API base and task-token header. Save only necessary non-secret
   configuration evidence. Reconfirm the intended Slack Test conversation and
   all its enabled bindings (one agent style covers both Slack rows); name alone
   is insufficient. Otherwise choose one of the other actually bound candidates.
   Refresh eligibility again before expansion. Unbound names are not targets.
3. Create the private evidence directory in that main checkout:
   `.antiphon\acceptance\card-0417\`. Verify it is ignored before writing private
   material (`git check-ignore` on a proposed child path). Keep exact prompts,
   raw replies, delivered message references and screenshots there, never in Git.
   Manifest fields: capture UTC time, source/build commit, agent ID/name, all
   enabled channel IDs/providers/conversation identity, old replyStyle, bundle
   keys, append SHA-256 over exact UTF-8 text (distinguish null from empty),
   provider/model/profile revision, session ID, loaded stamp and refresh mode.
   Audit relevant workspace instructions for competing rules; record the finding.
4. Capture the three **before** cases described under V-10 before changing the
   row. Save actual transcript UserPrompt/AssistantText/TurnEnd sequence ranges
   and timestamps using `GET /api/sessions/{id}/transcript?since={sequence}`;
   save actual delivered-message references/rendering as well. A historical prompt
   without its real reply and delivery evidence is an incomplete before sample.
5. Read the actual launched stamp with the following narrow SELECT. This avoids
   inventing a session-details API or treating `ComposedBundles` as loaded state.
   Set `$agentId` to the freshly selected catalog ID, parsed as a Guid. The
   local container/db/user identity comes from the bootstrap owner; no password,
   environment dump, launch-env JSON, token or full session row is requested.

```powershell
[guid]$agentId = 'eecab440-0f89-4691-9596-ea1e8ff049d0' # replace if another canary was selected
$sql = @'
BEGIN READ ONLY;
SET LOCAL statement_timeout = '5s';
SELECT json_build_object(
  'capturedAt', CURRENT_TIMESTAMP,
  'agentId', a."Id", 'name', a."Name", 'replyStyle', a."ReplyStyle",
  'sessionId', s."Id", 'status', s."Status", 'startedAt', s."StartedAt",
  'endedAt', s."EndedAt", 'composedBundleStamp', s."ComposedBundleStamp",
  'effectiveModelId', s."EffectiveModelId",
  'tuiProfileRevisionId', s."TuiProfileRevisionId")
FROM "Agents" a
LEFT JOIN "AgentSessions" s ON s."Id"::text = a."PersistentSessionId"
WHERE a."Id" = :'agent_id'::uuid;
COMMIT;
'@
$stampJson = $sql | docker exec -i antiphon-postgres psql -X -qAt `
    -U antiphon -d antiphon -v ON_ERROR_STOP=1 -v "agent_id=$agentId"
if ($LASTEXITCODE -ne 0) { throw 'Loaded-stamp read failed' }
$stamp = $stampJson | ConvertFrom-Json
if (-not $stamp -or -not $stamp.sessionId) { throw 'No correlated session row' }
```

The projection/join was exercised read-only during TestDesign against Slack Test:
it returned its actual session and an empty stamp with ReplyStyle=0. This is
retrieval evidence only, not a before reply or Phone acceptance. A null stamp is
missing history, not proof of Normal; an empty stamp is a recorded no-bundle
launch. Capture each observation immediately: a supported resume can reuse the
same session ID and overwrite the old row stamp. If EffectiveModelId is null,
resolve the actual model from correlated profile/runner launch evidence; do not
label two nulls as a confirmed equal model.

6. **S-3 PATCH detail:** "PATCH only replyStyle" means change only that setting,
   not send a one-property JSON body. `UpdateAgentRequest` requires name and
   workingDirectory; `AgentService.UpdateAsync` unconditionally assigns Details,
   DefaultWorkflowTemplateId, AssignmentPolicy and the three compaction overrides.
   Omitting them can refuse the request or reset unrelated settings. Use a fresh
   detail response and this structured preserving body, also exercised by V-2:

```powershell
$api = if ($env:ANTIPHON_API) { $env:ANTIPHON_API.TrimEnd('/') } else { 'http://localhost:17202' }
$headers = @{}
if ($env:ANTIPHON_TASK_TOKEN) { $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }
$before = Invoke-RestMethod "$api/api/agents/$agentId" -Headers $headers
$body = @{
    name = $before.name
    workingDirectory = $before.workingDirectory
    details = $before.details
    defaultWorkflowTemplateId = $before.defaultWorkflowTemplateId
    assignmentPolicy = $before.assignmentPolicy
    autoCompactEnabled = $before.autoCompactEnabled
    autoCompactIdleMinutes = $before.autoCompactIdleMinutes
    autoCompactContextPercent = $before.autoCompactContextPercent
    replyStyle = 'Phone'
} | ConvertTo-Json -Depth 6
$updated = Invoke-RestMethod "$api/api/agents/$agentId" -Method Patch -Headers $headers `
    -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
$after = Invoke-RestMethod "$api/api/agents/$agentId" -Headers $headers
```

Omit append, attachments, model/profile, launchEnv and bindings so their
leave-unchanged paths apply. Re-read and compare all manifest settings, the
always-assigned values above and bindings immediately. Abort on a difference
outside replyStyle/UpdatedAt; resolve concurrent configuration edits rather than
overwriting them. Preserve prior style for rollback. This procedure does not
require changing PATCH semantics in production code.

7. At an eligible idle boundary, use `POST /api/agents/{id}/refresh-policy` with
   `{ "force": false }`, or observe the existing automatic idle refresh. Record
   response, policy incident/boundary timestamp, session ID, runner launch/readiness
   state and subsequent transcript evidence. Respect working/provider/Herdr
   refusals. Force can skip idle-duration/cooldown only, never working protection;
   no fallback to an unrelated kill or another provider/model.
8. Repeat the SELECT and compare its exact `style-phone v<hash>` entry to the
   intended deployed bundle stamp. Correlate to the current API/runner session
   and successful resume/start boundary. Stamps are assigned during launch setup,
   so a stamped Starting/Failed row alone is insufficient: require the runner's
   successful launch and the subsequent actual answering turn. `refreshed:true`
   alone is also insufficient. Reject stale, null, Notify-only and merely desired
   DTO evidence. Only subsequent turns qualify as after samples. Record intervening
   restarts/style changes; discard or requalify samples crossing such boundaries.

No proactive test messages are authorized by this TestDesign task. The live
operator uses ordinary user-originated turns or explicit prior authorization for
specific test messages in the selected conversation. If authorization or real
turns are unavailable, keep V-9/V-10 pending; do not substitute fake output.

#### V-10: mandatory real-agent comparison and rollout decision

Before activation, preselect comparable read-only tasks from that real agent's
normal audience. Minimum sample set:

| Case | Before/after comparison | Pass evidence |
|---|---|---|
| L-1 simple | A real one-fact question, with the same required fact after activation | One short line; exact answer intact, no forced bullets/padding |
| L-2 multi-point | A real request with at least three independent facts/actions | Answer first, vertical points; ordinary bullets approximately five-to-seven words, exceptions explained |
| L-3 caveat/blocker | A real uncertainty or necessary missing fact, plus quantity/deadline when relevant | Caveat retained, no invented certainty, direct necessary question/action; shortening never removes required content |
| L-4 requested detail | Explicit follow-up asking for detail after a concise answer | Requested substance is supplied in short sections/bullets; this can be an extra turn beyond the three pairs |
| L-5 task completion | Actual task-report follow-up if the canary normally delegates | Full worker report remains available internally; the standing agent's real channel summary follows Phone |

Do not force a delegation into a family chat just to fill L-5. If the canary does
not normally delegate, observe L-5 on a second eligible real standing agent before
claiming orchestrator-follow-up acceptance. That is explicit scoped evidence
work, not permission to broaden the fleet before review. Include an exact long
identifier/link, actual file delivery and a no-change NO_REPLY case across these
live turns plus V-8's targeted delivery checks; label which evidence is live and
which is deterministic. Do not claim live attachment/silence compliance from a
mocked test.

For each pair use the evidence report template above, repeated vertically. Save
exact raw output privately and link its transcript/session/sequence, UTC time,
style stamp and actual delivered message. Review the real rendered message at
360–390 CSS px or equivalent phone width, recording viewport and screenshot
reference. Inspect line wrapping, wide blocks, intact descriptive-link targets
and delivered attachment bytes/name as applicable. Do not render an editorial
rewrite in a local mockup and call it channel evidence.

Measure total words, bullet count and per-ordinary-bullet word lengths as
diagnostics using one consistent counting method (whitespace-separated words,
excluding Markdown bullet markers and separate attachment-marker lines).
Record long exact tokens/clarity exceptions separately. Reviewer checks every
necessary fact against the original request/context, including caveats, risks,
uncertainty, corrections, quantities, deadlines and next actions. Require
material shortening of verbose baselines; already minimal answers need not
shrink. Any omitted essential fact, unsupported certainty, unwanted filler,
broken exact value, inline table/wide artifact or failed detail follow-up is a
failure, regardless of word count. A good baseline alone does not prove a gain.
Record changes in context; historical replay is not a controlled A/B experiment.
Never repeat a booking, send, deployment or other side effect to obtain a pair.

Publish only a sanitized summary under
`docs/investigations/2026-09-07-card-0417-phone-reply-acceptance.md`: artifact and
bundle version, source/build identity, publishable examples with explicit
redaction labels, evidence references, metrics/exceptions, reviewer identity/date,
wording verdict and pass/fail/pending for L-1 through L-5 and V-9/V-10. Full private
evidence stays in the main checkout's ignored directory. A reviewer must have
access to originals; redacted examples are not described as verbatim.

**Required stop:** missing pairs, unavailable original replies, absent delivery
rendering, unverified loaded policy, or no reviewed wording verdict leaves
acceptance pending. Code tests, a fake model, an isolated clone or a Notify
response cannot waive it. On failure stop expansion, restore the canary's recorded
old style with a freshly built preserving request, verify that style is loaded at
idle, revise wording and repeat the canary review.

After reviewer pass is recorded on CARD-0417, re-query eligibility and apply one
agent at a time to the remaining intended enabled channel agents (currently
AZ Care, Family and Slack Test). For each preserve settings, verify loaded stamp,
and inspect its first real delivered reply. Disabled Family/Mike, the unbound
project names and Antiphon-Orchestrator's Brief remain outside rollout. Acceptance
requires all current scoped targets to have reviewed wording applied and loaded,
not just one successful canary. Before downgrading to a binary without Phone,
reset all Phone rows, including newly created agents, to recorded supported
styles while Phone support is still running, and verify no value 5 remains.

### Out of scope

- No implementation or live style/binding/message mutation in TestDesign. This
  stage supplies the verification design and read-only stamp retrieval evidence.
- No output length enforcement, Markdown renderer, distiller, reply-limit,
  chunking, Pty, bracketed-paste, provider migration or general refresh change.
  No full E2E/Pty/full server suite for untouched areas; expand only for an actual
  changed dependency or named failure, following the testing owner.
- No global Phone default, automatic selection on a binding/preamble, unbound
  project migration, specialist update, delegate style-inheritance fix or warm
  pool restart. A mixed-purpose prompt is tested as scoped text; semantic model
  compliance comes from real evidence and cannot be proved by string matching.
- No test-generated messages to a live broker or real chats. No private prompts,
  environment values or credentials in committed artifacts or test logs.

### Cost

Forced server classes: `AgentReplyStyleTests`, `AgentReplyStyleEndpointTests`
(new), `AgentBundleAttachmentTests`, `InstructionBundleTests`,
`ProjectSetupServiceTests`, `AgentSystemPromptLaunchTests`, `DelegateBundleLaunchTests`,
`ChannelContractsTests`. In `PolicyRefreshServiceTests`, run `Phone_*` additions
plus these existing methods with individual exact method filters:
`RefreshPolicyAsync_working_is_409_session_working_even_with_force`,
`RefreshPolicyAsync_notify_lane_returns_notified`, `Unbound_transcript_is_Notify_not_a_kill`,
`Codex_is_Notify_not_a_kill`, and `Herdr_null_stamp_does_nothing`;
only `Phone_parent_preserves_worker_table_and_stage_handoff` in the large
`AgentTaskReplyIntegrationTests`; only the six V-8 delivery methods. No full
`AgentTaskReplyIntegrationTests` run is required by this change.

Use one exact class/method filter at a time, foreground, sequentially:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0417/ -- --treenode-filter "/*/*/AgentReplyStyleTests/*" --report-trx --report-trx-filename card0417-style.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0417/ -- --treenode-filter "/*/*/AgentSystemPromptLaunchTests/*" --report-trx --report-trx-filename card0417-launch.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0417/ -- --treenode-filter "/*/*/PolicyRefreshServiceTests/Phone_*" --report-trx --report-trx-filename card0417-refresh.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0417/ -- --treenode-filter "/*/*/AgentTaskReplyIntegrationTests/Phone_parent_preserves_worker_table_and_stage_handoff" --report-trx --report-trx-filename card0417-report.trx
pwsh -NoProfile -File scripts/test-client.ps1 AgentReplyStyle.test.tsx AgentCreateModal.test.tsx ProjectSetupModal.test.tsx
```

Apply the same exact-filter command to each other class/method listed; use unique
TRX names for class, mutation-red and restored-green runs. Inspect fresh executed
method names and nonzero counters, not `--list-tests` or exit zero alone.
Run `npm run build` from `client/` for typed union/catalog validation and before
any E2E consuming dist. All server fixtures use test Postgres/fake adapters and
the production-runner guard; any new process-spawning fixture must have the
assembly-local limiter. Never co-run the server and Pty test assemblies.

Estimated deterministic verification floor ~20 minutes, allow ~30–45 minutes
including independent mutation rebuilds and browser inspection on a warm machine;
not measured in TestDesign. Builds/fixtures may dominate. Live acceptance requires
at least three real pairs plus follow-ups and an actual reviewer; time depends on
real turns/authorization and has no timeout-based waiver. Cleanup only task-owned
`bin-card0417/` outputs after resolving absolute targets inside this worktree;
retain reports through review and follow the testing/build guide.

TestDesign validation: full plan/card and the named fixture seams inspected;
read-only production stamp projection returned one selected agent/session.
The CLI parameter-only baseline probe accepted all five existing styles and
rejected Phone, and its help-line selector matched; it executed no script body
or HTTP request. No test suites/builds or model replies were run, and no Phone
acceptance is claimed.
