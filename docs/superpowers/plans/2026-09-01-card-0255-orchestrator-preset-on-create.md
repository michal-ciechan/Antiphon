# CARD-0255 — Orchestrator preset applies at create (workflow + prompt + bundles + flags)

**Date:** 2026-09-01 (Plan pass, task 1438b4cb — design only; no code changed)
**Card:** CARD-0255 "Orchestrator preset applies by default on agent create (workflow + prompt + bundles), overridable"
**Diagnosis:** done, on the card. `AgentPresets.Orchestrator` already names the standing-orchestrator shape; `ProjectSetupService` is the only consumer, and only when the caller passes `agent.Preset`. `POST /api/agents` (the UI new-agent modal) has no preset at all. school-revision's standing agent sat six weeks with empty bundles and a null prompt.

**Sources (verified this pass):** `server/Application/Services/AgentPresets.cs`, `ProjectSetupService.cs:299-327`, `AgentService.CreateAsync` (`AgentService.cs:352-477`), `server/Application/Dtos/{AgentDtos.cs:182-217, ProjectSetupDtos.cs:79-119}`, `client/src/features/{agents/AgentCreateModal.tsx, settings/ProjectSetupModal.tsx}`, `client/src/api/{agents.ts:279-311, projectSetup.ts:52-93}`, `server/Infrastructure/Data/Seeding/DatabaseSeeder.cs:12,118-142`, CARD-0293 Verdict (task e3c42bff), CARD-0008 / CARD-0032 plans, `tests/Antiphon.Tests/Application/ProjectSetupServiceTests.cs`, `InstructionBundleTests.cs`.

**Related, not this card:** CARD-0293 items 1–2 (gym-stat RC data cleanup; `AgentAddWorkModal` defaulting the start checkbox to `rc.supported`). Item 3 of that verdict is this card.

---

## Decision

One create-time applier, two human entry points, no stored preset key.

1. **`POST /api/agents` grows `Preset`.** `AgentService.CreateAsync` is the single apply site. Unknown key → 422 `preset`. Missing/null → today's bare create. An explicit request value always wins, including an explicit empty `BundleKeys` (detach-all, not unset).
2. **The applier is shared.** Extract the `??` chain that already lives in `ProjectSetupService:305-326` onto `AgentPresets` (or a tiny sibling in Application). `ProjectSetupService` stops merging; it passes `Preset` + overrides through `CreateAgentRequest`. Two implementations that can disagree is the failure this card exists to prevent.
3. **The orchestrator preset carries the two fields it is missing today:** `RemoteControlEnabled: true` (CARD-0293 item 3; both live orchestrators run true) and `DefaultWorkflowTemplateId` = the seeded Full Feature Pipeline id (`DatabaseSeeder.BmadFullTemplateId` = `b0000000-0000-0000-0000-000000000001`). Worker stays `false` / `null`.
4. **Prompt placeholders render at the same apply site.** `RenderTemplate` already substitutes `{project}/{board}/{repoUrl}/{directory}`. Create with a preset and a null `SystemPromptAppend` must render; a non-null append (including a caller-supplied empty string meaning "no prompt") must not.
5. **UI defaults the standing-orchestrator chip on.** Project setup's first-agent step and the new-agent modal both start on `orchestrator` and fill the visible fields from the catalog so the operator can edit them. Worker is one click. Skip-agent on setup still skips.
6. **Create only. Never PATCH. No backfill. No `Agent.Preset` column.** The filled fields stay visible and editable; nothing re-asserts them. 65 existing agents, several deliberately bare, stay as they are.

---

## Ground truth (checked, not guessed)

### Who applies a preset today

| Caller | Where | When it fires | What it gets |
|---|---|---|---|
| Project setup | `ProjectSetupService.cs:299-327` | Only if `request.Agent` is non-null **and** `setupAgent.Preset` is a known key | `??` chain for name, model, reply style, AlwaysOn, bundles, rendered prompt. **RC is `setupAgent.RemoteControlEnabled ?? false` — never the preset.** No workflow. |
| `POST /api/agents` | `AgentService.CreateAsync:352` ← `AgentEndpoints.cs:79-85` | Every UI new-agent and every raw create | No `Preset` on `CreateAgentRequest`. `AlwaysOn`/`RemoteControlEnabled` default **false** (`AgentDtos.cs:207-208`). `BundleKeys` null → no attachments (`:476-477`). `SystemPromptAppend` null → none (`:461-465`). `DefaultWorkflowTemplateId` null → none (`:446`). |
| New-agent modal | `AgentCreateModal.tsx:111-127` | Every "Create" | Sends `alwaysOn`, `remoteControlEnabled: rc.supported && remoteControlEnabled`, `bundleKeys` (state starts `[]`), `systemPromptAppend: trim() \|\| null`. No preset picker. |
| Setup modal | `ProjectSetupModal.tsx:80,116-122,146-159` | First-agent step, unless Skip | `presetKey` starts **null**. `selectPreset` copies AlwaysOn / ModelLevel / ReplyStyle / BundleKeys into the form; **does not copy RC**. Empty prompt is sent as `null`, so the server still renders. |

`Agent` has no `Preset` column (`Agent.cs`). "0 of 65 live agents have a preset set" is "none of them received the orchestrator shape", not a stored key.

### Why `??` cannot be copied onto today's `CreateAgentRequest` bools

`ProjectSetupAgentRequest` uses `bool? AlwaysOn` / `bool? RemoteControlEnabled` (`ProjectSetupDtos.cs:116-117`), so omitted vs explicit-false is distinguishable. `CreateAgentRequest` uses `bool AlwaysOn = false, bool RemoteControlEnabled = false` (`AgentDtos.cs:207-208`) because CARD-0008 had nothing to leave unchanged. JSON omit deserializes to `false`, and `false ?? preset.AlwaysOn` is `false`.

Without changing those two (and treating `BundleKeys` / `SystemPromptAppend` as already-nullable) a `POST { preset: "orchestrator" }` that omits the flags would still create a bare AlwaysOn-false agent. That is the school-revision leak, just moved.

`AgentReplyStyle ReplyStyle = Normal` has the same shape; both the preset and the hard default are Normal, so leaving it non-nullable is acceptable. `ModelLevel` is already `AgentModelLevel?`.

### Workflow id

Three built-in templates, seeded with stable GUIDs (`DatabaseSeeder.cs:12-14`). Full Feature Pipeline is `b0000000-0000-0000-0000-000000000001`. Application must not reference the seeder type: duplicate that GUID on the orchestrator preset with a comment, and pin equality in a test that can see both assemblies. Testcontainers migrate, they do not seed (`TestDbFixture.cs:45-50`) — any create-with-orchestrator-preset test must insert that template (or the create 404s via `EnsureWorkflowTemplateExistsAsync:920-928`).

### CARD-0293, item 3 only

Stored default is already off. Spawn/delegate hardcodes false. The nine gym-stat `true` workers were explicit POST payloads, not a preset. The remaining live bug (`AgentAddWorkModal` defaulting the start checkbox to `rc.supported`) is a different card. This card only puts `RemoteControlEnabled: true` on **the orchestrator preset**, not the worker preset, not `delegate.ps1`.

Orchestrator seats are Claude-only (`docs/agent-kinds.md`). `RemoteControlPolicy.Require` already 409s a true flag on Grok/Codex (`AgentService.cs:474`). A script that sends `preset=orchestrator` with a Grok profile and does not override RC gets that 409 — correct, not a new gate.

---

## Slices

### S1 — Catalog: RC + workflow on `AgentPresetDto`

`AgentPresetDto` (`ProjectSetupDtos.cs:79-88`) gains two trailing fields with defaults so existing `new(...)` sites compile:

- `bool RemoteControlEnabled = false`
- `Guid? DefaultWorkflowTemplateId = null`

`AgentPresets.All` (`AgentPresets.cs:20-42`):

| Preset | RC | Workflow |
|---|---|---|
| `orchestrator` | `true` | Full Feature Pipeline id `b0000000-0000-0000-0000-000000000001` |
| `worker` | `false` | `null` |

Client `AgentPresetDto` (`projectSetup.ts:52-62`) grows the same two fields. Rewrite the class doc on `AgentPresets` (`:8-11`): a preset is a **create-time starting point** whose resulting fields stay visible and editable; it is not re-applied on PATCH.

Pin: `InstructionBundleTests` (already loads the orchestrator template) asserts the new catalog facts and `DefaultWorkflowTemplateId == DatabaseSeeder.BmadFullTemplateId`.

### S2 — One applier; `CreateAsync` is the only apply site

New Application helper (name `AgentPresets.Apply` or a sibling `AgentPresetApplier` next to it — not a second copy in `AgentService`). Inputs: preset key, overrides (the nullable request fields), render context (`project`, `board`, `repoUrl`, `directory`). Unknown key → `ValidationException("preset", ...)`. Output: concrete values `CreateAsync` already knows how to persist.

Override rule, field by field, matching today's setup chain plus the two new fields:

| Field | Chain |
|---|---|
| `ModelLevel` | request ?? preset ?? High |
| `ReplyStyle` | request ?? preset ?? Normal |
| `AlwaysOn` | request ?? preset ?? false |
| `RemoteControlEnabled` | request ?? preset ?? false |
| `BundleKeys` | request ?? preset (null = do not `SetAsync`; `[]` = attach none) |
| `SystemPromptAppend` | request non-null → use as-is (whitespace becomes entity null, as today `:461-465`); request null → `RenderTemplate(preset?.SystemPromptTemplate, …)` |
| `DefaultWorkflowTemplateId` | request ?? preset ?? null |
| Name | unchanged validation; `NamePattern` stays a ProjectSetup convenience when the setup agent name is blank (`:313-315`). `CreateAsync` still requires a name. |

`CreateAgentRequest` (`AgentDtos.cs:182-217`):

- `string? Preset = null`
- `bool? AlwaysOn = null`, `bool? RemoteControlEnabled = null` (omit vs explicit-false)
- Leave `BundleKeys` / `SystemPromptAppend` / `DefaultWorkflowTemplateId` / `ModelLevel` as they are (already nullable)

`AgentService.CreateAsync`: after the board is resolved (so `Project.Name`, board name, `GitRepositoryUrl`, working directory exist — the `BoardId` arm already `Include`s Project at `:388-391`; the inherit arm must load Project the same way), call Apply, then persist the resolved values. `EnsureWorkflowTemplateExistsAsync` already 404s a missing id.

`ProjectSetupService:299-327` becomes a pass-through: build `CreateAgentRequest` with `Preset = setupAgent.Preset` and the nullable overrides, no local `??` and no local `RenderTemplate`. Delete the RC `?? false` that skipped the preset.

No EF migration. No `UpdateAgentRequest.Preset`. PATCH stays field-by-field as today.

### S3 — UI: chips on both create paths, default orchestrator, fill on select

**ProjectSetupModal** (`ProjectSetupModal.tsx:80`): `presetKey` initial state `'orchestrator'`; on catalog load, `selectPreset` once so AlwaysOn / bundles / **RC** / workflow preview match. `selectPreset` (`:116-122`) also sets `remoteControlEnabled` from `preset.remoteControlEnabled`. Skip-agent still sends `agent: null`.

**AgentCreateModal:** same chip group as setup (`:268-274`), same fill-on-select, default `'orchestrator'`. Submit includes `preset`. Do **not** send `bundleKeys: []` as a silent default when the user has not touched bundles — after select, state holds the preset keys; clearing the MultiSelect is the explicit empty. `systemPromptAppend: trim() \|\| null` stays, so an unedited prompt is `null` and the server renders (placeholders need board/project, which create now has). `remoteControlEnabled: rc.supported && remoteControlEnabled` stays — Grok default profile still submits false (CARD-0212 pin in `AgentRemoteControl.test.tsx:236`).

Client `CreateAgentRequest` (`agents.ts:279-311`) gains `preset?: string | null`. Optional bools stay optional so omit is possible for scripts; the modal sends the filled values.

### S4 — Pins (see test matrix)

### S5 — Throwaway e2e on school-revision (execute pass, not this plan)

Create a **new** agent on board `school-revision` (`05505223…`) with `preset: "orchestrator"`. Do not start it if that would fight the live AlwaysOn `school-revision` agent `0713b081`. GET must show bundles `orchestrator`+`board-api`, a rendered prompt (no literal `{project}`), `alwaysOn: true`, `remoteControlEnabled: true`, `defaultWorkflowTemplateId` = Full Feature Pipeline, with **no PATCH**. Then `DELETE /api/agents/{id}`. Working directory: `C:/src/school-revision` is already claimed by the live agent — use a throwaway subdir or the same path with an explicit `boardId` (create allows a board whose project path differs; it logs and continues, `AgentService.cs:393-398`). Prefer an explicit `boardId` so it lands on the school-revision board, and a name like `CARD-0255 throwaway`. Delete before leaving.

---

## What this card does not do

- **No PATCH re-apply, no `Agent.Preset` column, no backfill of the 65 existing agents.**
- **CARD-0293 items 1–2** — gym-stat RC cleanup; `AgentAddWorkModal` checkbox default. Cross-reference only.
- **No change to pool/delegate spawn** (`AgentTaskDispatcher` already hardcodes RC false).
- **NamePattern on `POST /api/agents`.** Create still requires `Name`. Setup still fills from the pattern when the setup agent name is blank.
- **Do not touch agent `0713b081`.**

---

## Test matrix

| Layer | Test |
|---|---|
| `Antiphon.Tests` unit | Catalog: orchestrator RC true + Full Feature Pipeline id equals `DatabaseSeeder.BmadFullTemplateId`; worker RC false + null workflow; template still embedded and not attachable |
| `Antiphon.Tests` unit | `Apply`: omit → preset; explicit false AlwaysOn/RC wins; explicit `[]` BundleKeys wins (no preset bundles); explicit prompt skips `RenderTemplate`; null prompt renders `{project}/{board}/{directory}`; unknown key throws `preset` |
| `Antiphon.Tests` Application | `CreateAsync(Preset: orchestrator)` (seed the pipeline template first) → AlwaysOn, RC, bundles, rendered prompt, workflow id; session/agent status Idle; **no** second write |
| `Antiphon.Tests` Application | `CreateAsync` with no Preset → today's bare row (AlwaysOn false, no bundles, no prompt, no workflow). Existing create tests stay green unedited |
| `Antiphon.Tests` Application | Explicit empty `BundleKeys: []` + orchestrator preset → no attachments. Explicit `AlwaysOn: false` + preset → false |
| `Antiphon.Tests` Application | `UpdateAsync` after an orchestrator-preset create does not restore a cleared prompt or re-attach bundles |
| `Antiphon.Tests` Application | `ProjectSetupServiceTests.orchestrator_preset_renders_the_project_facts_and_its_contract`: also `RemoteControlEnabled` true and workflow id set; existing override test still green |
| Client vitest | `ProjectSetupModal`: default chip is Standing orchestrator; submit `agent.preset === 'orchestrator'` and `remoteControlEnabled === true` after default select |
| Client vitest | `AgentCreateModal`: chips present, default orchestrator, submit includes `preset: 'orchestrator'` and filled bundles; Worker chip submits `preset: 'worker'` and empty bundles |
| Client vitest | `AgentRemoteControl.test.tsx` Grok create still submits `remoteControlEnabled: false` (capability gate on submit) |

Run per `docs/testing-and-build.md`: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0255/` (forward slash), `--treenode-filter "/*/*/ProjectSetupServiceTests/*"` etc.; `pwsh -File scripts/test-client.ps1` for the modal tests. Delete `bin-card0255` directories afterwards.

---

## Sequencing and risks

**Order: S1 → S2 → S3 → S4. S5 on the execute pass.** S2 is the behaviour; S3 is how humans reach it; S1 is the data S2 reads.

| Risk | Disposition |
|---|---|
| `bool` → `bool?` on Create silently changes omit-without-preset | Omit + no preset still resolves to false (hard default). Existing `new CreateAgentRequest("A", dir)` compiles; AlwaysOn stays false |
| Modal keeps sending `bundleKeys: []` and wipes the preset | S3 fill-on-select; pin the payload in the vitest |
| Orchestrator-preset create 404s in tests that never seed workflows | Test seeds `BmadFullTemplateId` (or the 404 is the pin that the id is real) |
| Defaulting the new-agent modal to orchestrator surprises worker creates | Worker is a chip; CARD-0295 already wants fewer raw POSTs. Scripts omit `Preset` |
| Grok profile + orchestrator preset + no RC override | 409 from `RemoteControlPolicy` — orchestrator seats are Claude-only |
| Render context missing Project on the inherit-board arm | S2 loads `board.Project` the way the explicit-`BoardId` arm already does |

---

## Execute notes (not this pass)

- Throwaway agent on the school-revision board; delete it. Never start-or-stop `0713b081`.
- `GET /api/workflow-templates` (or the seeded id) to confirm Full Feature Pipeline before asserting `defaultWorkflowTemplateId`.
- After S3, a human "New Agent" on that board with the default chip should match the throwaway POST with no PATCH.
