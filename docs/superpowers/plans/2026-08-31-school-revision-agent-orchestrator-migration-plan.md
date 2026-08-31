# Plan: migrate the school-revision agent onto the full orchestrator preset

- **Date:** 2026-08-31
- **Task:** 27e0f9f8 (Plan pass — investigation only; nothing in this doc has been executed)
- **Agent:** `school-revision` (`0713b081-10b8-4ec2-8ebd-296e885b38f3`), board `05505223-731e-4739-889a-32949627bc22`, AlwaysOn standing orchestrator. **It is the operator's live working session serving real students (https://schoolrevision.co.uk) — treat every step as production work.**
- **Relationship to CARD-0255:** that card fixes the CREATE-time preset path for future agents and explicitly excludes this live agent from its own verification. This plan is the separate, one-shot retrofit of this specific agent. Nothing here blocks on CARD-0255 and nothing here changes code.

## Verdict up front

The agent is already on the orchestrator preset in every respect but one. **The whole migration is a single field: set `DefaultWorkflowTemplateId` to Full Feature Pipeline (`b0000000-0000-0000-0000-000000000001`).** No restart, no session disturbance, no prompt change, no rename.

## Findings

### 1. Workflow templates: where they live and the confirmed id

- API: `GET /api/settings/templates` (list) and `GET /api/settings/templates/{id}` — `SettingsEndpoints.cs:18-33` → `WorkflowTemplateService`.
- Storage: `WorkflowTemplates` table (columns `Id, Name, Description, YamlDefinition, IsBuiltIn, CreatedAt, UpdatedAt, TemplateGroupId`), seeded by `DatabaseSeeder.SeedWorkflowTemplatesAsync` with fixed ids.
- The three seeded built-ins, verified live in the production DB:

  | Id | Name |
  |---|---|
  | `b0000000-0000-0000-0000-000000000001` | **Full Feature Pipeline** |
  | `b0000000-0000-0000-0000-000000000002` | Quick Change |
  | `b0000000-0000-0000-0000-000000000003` | Document Project |

- **Full Feature Pipeline confirmed as the right default**, not just assumed: its YAML is the full multi-stage pipeline (prd → ux-design → …) with `selectableStages: true` (stages can be trimmed per card) and `gateRequired: true` on the leading stages — the operator-confirmation gate is exactly the safe direction for a project serving real students. Quick Change is a two-stage lightweight lane and Document Project is a docs generator; neither fits a standing orchestrator's default.
- A real (if latent) reason to set it: with a null `DefaultWorkflowTemplateId`, `CardWorkflowRunFactory.CreateFromAgentDefaultAsync` (`CardWorkflowRunFactory.cs:26-34`) silently falls back to the **first template ordered by name — "Document Project"**, which is the wrong workflow for every card this agent would ever be assigned. Today the fallback has never fired (this agent has **zero** `CardWorkflowRuns` rows — it orchestrates through its own session and delegation, not queue assignment), so the field is dormant; setting it closes the trap before the first real assignment springs it.
- Observation, out of scope: all three built-in templates hard-code stale stage model ids (`claude-opus-4-20250514`, `gpt-4o`, `claude-sonnet-4-20250514`). Worth its own hygiene card someday; not this migration's problem.

### 2. How to set it on a live AlwaysOn agent, and why it is safe

**Path:** `PATCH /api/agents/{id}` (`AgentEndpoints.cs:95` → `AgentService.UpdateAsync` at `AgentService.cs:517`). The UI's agent-settings modal uses the same endpoint; there is no dedicated script helper. Either lane works — the API call is given below so the step is exact.

**No restart, no disturbance of the running conversation:**

- `DefaultWorkflowTemplateId` is read in exactly one place: `CardWorkflowRunFactory.cs:26`, called only from `AgentService.AssignCardAsync` (`AgentService.cs:739`) when a card is assigned into the agent's queue. It is not a launch-time input; the change takes effect on the next card assignment with no session involvement at all.
- `UpdateAsync` writes the row and publishes `AgentChanged` — which, verified across the server, is a **pure UI-invalidation push**; every occurrence is a publisher, no server-side subscriber restarts or reconciles a session off it.
- The running session is explicitly insulated from agent-row edits by design (see the bundle comment at `AgentService.cs:578-581`: the running session keeps what it launched with; drift shows as a badge only).
- The request validates the template exists (`EnsureWorkflowTemplateExistsAsync`, `AgentService.cs:521`) before touching the row.

**The one sharp edge — `UpdateAsync` is not uniformly "null = leave unchanged":** these fields are applied **unconditionally** and must be echoed from a fresh GET or they get clobbered:

| Field | Behaviour if omitted |
|---|---|
| `name`, `workingDirectory` | required; validation fails without them (and name recomputes the slug — echo the exact current name) |
| `details` | wiped to empty string (`AgentService.cs:541`) |
| `defaultWorkflowTemplateId` | **cleared to null** (`AgentService.cs:542`) — this is also the rollback lever |
| `assignmentPolicy` | reset to `AutoPick` (`AgentService.cs:543`) |
| `autoCompactEnabled` / `autoCompactIdleMinutes` / `autoCompactContextPercent` | applied even when null — null means "use global default" (`AgentService.cs:574-577`); echo the live values anyway so an operator override set between plan and execution survives |

Everything else on `UpdateAgentRequest` is genuinely null-leaves-unchanged and must be **omitted** in this PATCH: `boardId`, `alwaysOn`, `remoteControlEnabled`, `systemPromptAppend`, `modelLevel`, `tuiProfileId`/`modelId`, `replyStyle`, `sessionBackend`, `bundleKeys`, `launchEnv`, `kind`. Omitting them is what guarantees the operator's existing settings are untouched.

JSON note: enums serialize as **strings** and integers are rejected (`Program.cs:235`, `JsonStringEnumConverter(allowIntegerValues: false)`) — send `"assignmentPolicy": "AutoPick"`, never `0`. Echoing the GET response's own value satisfies this automatically.

### 3. Custom systemPromptAppend vs the generic template: leave it entirely alone

The generic `server/Bundles/Presets/orchestrator-prompt.md` is nine lines: identity, board/repo/checkout, the investigate→build→verify→merge→close pipeline, delegate-every-change, use-the-board-API. The agent's live custom prompt (read from the production DB) covers **every one of those elements** — identity with board/repo/checkout inlined, the same pipeline (plus deploy, which the generic lacks), the same delegate-everything rule — and adds four project-specific standing rules (one-build-at-a-time in the checkout, deploy-through-test-before-prod, work-on-master, tests-are-the-gate) plus pointers to the repo's own convention docs. The template has not grown anything since the prompt was hand-written; the only phrase not present verbatim is "Use the board API", which the attached `board-api` bundle already delivers with far more detail.

**The custom prompt is a strict superset for this project's purposes. Recommendation: no change, no merge.** This also honours the CARD-0255 design principle: a preset is what the user can edit, never a hidden default reapplied over an operator's work.

### 4. Full preset-field audit: nothing else is missing

Checked every field `AgentPresets.Orchestrator` carries (`AgentPresets.cs:22-31`) plus CARD-0255's gap list, against the agent's live row:

| Preset field | Preset value | Agent's live value | Verdict |
|---|---|---|---|
| `AlwaysOn` | true | true | ✅ |
| `ModelLevel` | High | High (1) | ✅ |
| `ReplyStyle` | Normal | Normal (0) | ✅ |
| `BundleKeys` | `[orchestrator, board-api]` | `[orchestrator, board-api]`, composing successfully | ✅ |
| `SystemPromptTemplate` | generic orchestrator-prompt.md | custom strict superset | ✅ leave alone (finding 3) |
| `NamePattern` | `{project} Orchestrator` → "school-revision Orchestrator" | "school-revision" | ⚠️ cosmetic only — **deliberately not migrating.** A rename recomputes the slug (`AgentService.cs:539`), and the name/slug identify the operator's live working session everywhere; churn for zero behaviour. |
| `DefaultWorkflowTemplateId` | (not on the preset DTO yet — CARD-0255 gap 2) | **null** | ❌ **the one real gap — this migration** |
| `RemoteControlEnabled` | (not on the preset DTO yet — CARD-0255 gap 3) | true | ✅ already matches the working-orchestrator norm |

AutoCompact overrides: the preset has no opinion; the agent is on global defaults (all three null) — correct, untouched.

## Execution steps (for whoever runs this — do not run as part of the plan task)

All against the production server API on `http://localhost:17202`. One PATCH total.

**Step 1 — fresh read (mandatory, immediately before the PATCH; do not copy values from this doc):**

```powershell
$base = 'http://localhost:17202'
$id   = '0713b081-10b8-4ec2-8ebd-296e885b38f3'
$before = Invoke-RestMethod "$base/api/agents/$id"
$before | ConvertTo-Json -Depth 6 | Set-Content "$env:TEMP\school-revision-agent-before.json"
```

Sanity-check `$before`: `alwaysOn=true`, `remoteControlEnabled=true`, `attachedBundleKeys` = orchestrator+board-api, `systemPromptAppend` non-empty, `defaultWorkflowTemplateId=null`. If any of those differ from this plan's findings, **stop and re-investigate — the agent moved under the plan.**

**Step 2 — confirm the template id still exists:**

```powershell
(Invoke-RestMethod "$base/api/settings/templates") |
  Where-Object name -eq 'Full Feature Pipeline' | Select-Object id, name
# expect id b0000000-0000-0000-0000-000000000001
```

**Step 3 — the PATCH (echo the unconditional fields from `$before`; omit everything else):**

```powershell
$body = @{
  name                      = $before.name
  workingDirectory          = $before.workingDirectory
  details                   = $before.details
  defaultWorkflowTemplateId = 'b0000000-0000-0000-0000-000000000001'
  assignmentPolicy          = $before.assignmentPolicy
  autoCompactEnabled        = $before.autoCompactEnabled
  autoCompactIdleMinutes    = $before.autoCompactIdleMinutes
  autoCompactContextPercent = $before.autoCompactContextPercent
} | ConvertTo-Json
Invoke-RestMethod -Method Patch "$base/api/agents/$id" -ContentType 'application/json' -Body $body
```

Timing: any time. Nothing reads the field except card assignment; the live session is not consulted. No idle window required.

**Step 4 — verify (read-only):**

```powershell
$after = Invoke-RestMethod "$base/api/agents/$id"
$after.defaultWorkflowTemplateId    # b0000000-0000-0000-0000-000000000001
$after.defaultWorkflowTemplateName  # Full Feature Pipeline
```

Then diff `$after` against the saved before-snapshot: **only** `defaultWorkflowTemplateId`, `defaultWorkflowTemplateName`, and `updatedAt` may differ. In particular `systemPromptAppend`, `attachedBundleKeys`, `alwaysOn`, `remoteControlEnabled`, `modelLevel`, `name`, `slug`, and `liveSession` must be byte-identical. Confirm the live session is still the same session id and still healthy (the `liveSession` block, or the Herdr pane) — expected untouched, verify anyway.

**Rollback:** repeat Step 3 with `defaultWorkflowTemplateId` omitted (or null) — the unconditional assignment clears it back to the pre-migration state.

## Out of scope, recorded so it isn't lost

- Building CARD-0255 (create-time preset application, adding `DefaultWorkflowTemplateId`/`RemoteControlEnabled` to the preset DTO).
- Renaming the agent to match `NamePattern` (rejected above, not deferred).
- Any prompt or bundle change.
- Template hygiene: the built-in templates' hard-coded stale stage model ids — candidate for a new card.
