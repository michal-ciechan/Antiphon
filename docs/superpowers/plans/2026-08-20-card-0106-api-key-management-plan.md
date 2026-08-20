# CARD-0106 — Per-agent API key overrides: global and project-scoped keys, resolved via a placeholder at launch

**Date:** 2026-08-20 · **Card:** CARD-0106 · **Status:** plan (no implementation in this pass)

**Verdict up top:** build ONE new `ApiKey` table (name + ciphertext, nullable `ProjectId` — null means
global), a new per-agent env-override field on `Agent`, and a `{{key:NAME}}` placeholder resolved over
the fully-merged launch `Env` in the two places `Env` is finalized — plus a **tripwire at the single
choke point every launch passes through** (`AgentSessionService.BuildRuntimeLaunchSpec`) that fails the
launch loudly if any placeholder survives to the adapter handoff. Project overrides global. Placeholders
are legal in **env values only** — never args or system-prompt text, because those land on the command
line, which is process-listing-visible and quoted into logs and failure reasons. `AgentTuiSecret`
**coexists for now** with an explicit migration path (its own follow-up card), not an undecided limbo.
Four slices; S1+S2 are the working feature end-to-end via the API, S3 is the UI, S4 is the
AgentTuiSecret convergence follow-up (filed, not built here).

All file:line references verified by reading the files on 2026-08-20 against master `2755613`. The
card's "what already exists" investigation held up on every point I re-read; nothing inconsistent found.

---

## 1. The ground truth this plan stands on

**Where `Env` is finalized — exactly two bottom-level resolvers, seven call sites, one handoff choke point.**

- Bottom level A — `AgentRegistry.Resolve` (`server/Application/Services/AgentRegistry.cs:102-186`):
  sync, no DB access, merges `AgentDefinition.Env` (static appsettings, `{}` everywhere today) with
  `options.ExtraEnv`, applies kind defaults (DISABLE_AUTOUPDATER, nesting markers, Grok telemetry).
  Used directly by `AgentTaskDispatcher.BuildLaunchSpec` (`AgentTaskDispatcher.cs:1470`) and
  `CardService` (`CardService.cs:572`), and as the legacy fallback inside `AgentLaunchResolution`.
- Bottom level B — `AgentTuiLaunchResolver.ResolveCoreAsync`
  (`server/Application/Services/AgentTuiLaunchResolver.cs:190-312`): async, DB-backed, merges profile
  revision non-secret env + decrypted `AgentTuiSecret`s + `options.ExtraEnv` + kind defaults. Reached
  via `AgentLaunchResolution.ResolveForAgentAsync`/`ResolveDefaultAsync` from `AgentControlService.cs:233`,
  `CardService.cs:581,599`, `OrchestratorService.cs:602,610`.
- Choke point — every spec from every path is handed to an adapter inside `AgentSessionService`, and
  all three `adapter.StartAsync(spec, ct)` sites (`AgentSessionService.cs:170,359,822`) go through
  `BuildRuntimeLaunchSpec` (`AgentSessionService.cs:983-1002`) immediately before. Nothing else in the
  server calls `IAgentProtocolAdapter.StartAsync`.

**There is no per-agent env today.** `Agent` (`server/Domain/Entities/Agent.cs`) has `TuiProfileId`,
`ModelId`, `SystemPromptAppend`, compaction overrides — no env dict. The profile revision's env is
shared by every agent on that profile. So "per-agent API key overrides" needs a per-agent env field to
put the placeholder IN, not just a key store to resolve it FROM. That field is part of this design (§3).

**No permission subsystem exists.** Grepping `server/` for authorization finds only agent *roles*
(delegation) — this is a single-operator local tool with no auth on any endpoint (`server/Api/Endpoints/*`
are all open minimal-API routes). So the card's question "should project keys need different edit rights
than global keys?" answers itself: **no permission distinction is buildable or needed today.** Scope
visibility is a UI-placement concern only (global keys in Settings, project keys on the project's config
panel). If auth ever arrives, the nullable-`ProjectId` model is the shape RBAC would want anyway.

**How an agent maps to a project.** `Agent.BoardId` (nullable) → `Board.ProjectId` (non-nullable,
`server/Domain/Entities/Board.cs:8`) → `Project`. Card sessions also reach a project via
`card.Board.ProjectId`. Pool delegates (dispatcher-created, `IsPoolDelegate`) have no board. `AgentTask`
carries only `WorkingDirectory` — no project FK — and worktrees are sibling directories of the repo, so
path-prefix matching against `Project.LocalRepositoryPath` is unreliable. Rule in §4.

**Encryption to reuse.** `DataProtectionAgentTuiSecretProtector`
(`server/Infrastructure/Agents/Tui/DataProtectionAgentTuiSecretProtector.cs:24-114`) + the readiness/
custody machinery (`AgentTuiKeyProtectionReadiness`, `AgentTuiDataProtectionSetup.Configure`) are solid
and already deployed: DPAPI/cert-protected key ring outside the content root, owner-only ACLs, per-secret
purpose chain `("Antiphon","AgentTui","ProfileSecret",profileId,name)`. We reuse the provider, key ring,
and readiness wholesale — only the purpose chain is new.

---

## 2. Data model

One table. Global-vs-project is a nullable FK, not two tables — nothing anywhere treats them
differently except precedence at lookup time, and one table makes "list every key that could resolve
for this agent" a single query.

```csharp
// server/Domain/Entities/ApiKey.cs
public class ApiKey
{
    public Guid Id { get; set; }
    /// <summary>Referenced by {{key:NAME}}. [A-Za-z0-9_.-]+, max 128. Case-sensitive (Ordinal), like env handling in AgentRegistry.Resolve.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Null = global. Cascade-delete with the project.</summary>
    public Guid? ProjectId { get; set; }
    public string Ciphertext { get; set; } = string.Empty;
    public string ProtectionVersion { get; set; } = string.Empty;   // same field AgentTuiSecret carries
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Project? Project { get; set; }
}
```

- **Uniqueness:** two filtered unique indexes — `(Name) WHERE "ProjectId" IS NULL` and
  `(ProjectId, Name) WHERE "ProjectId" IS NOT NULL`. (Version-safe; avoids depending on PG15+
  `NULLS NOT DISTINCT`.) A project key and a global key MAY share a name — that's the override feature.
- **Project navigation:** add `ICollection<ApiKey> ApiKeys` to `Project`
  (`server/Domain/Entities/Project.cs`) with cascade delete. Deleting a project deletes its keys; any
  agent still referencing one fails its next launch loudly (§5) — correct, not a bug.
- **Encryption:** new thin `IApiKeyProtector` / `DataProtectionApiKeyProtector` delegating to the SAME
  `IDataProtectionProvider` + `AgentTuiKeyProtectionReadiness` already registered by
  `AgentTuiDataProtectionSetup.Configure` (`...:279` registers the TUI protector; we add one line for
  ours). Purpose chain: `("Antiphon", "ApiKey", keyId.ToString("D"))` — keyed by the row id, not the
  name, so renaming a key does not orphan its ciphertext. Copy `GetPayloadKeyId`/readiness call shape
  from `DataProtectionAgentTuiSecretProtector` verbatim (or extract a shared base — implementer's call;
  the readiness handshake at `:37-52` is the part that must not be skipped).
- **Value ceiling:** enforce the same `MaximumEnvironmentValueLength = 4000` the TUI resolver enforces
  (`AgentTuiLaunchResolver.cs:103,240`) at write time AND after decrypt.

### API (minimal-API style, mirroring `AgentTuiEndpoints.cs:88-166`'s secret PUT/DELETE)

New `server/Api/Endpoints/ApiKeyEndpoints.cs`:

- `GET  /api/api-keys` → list (id, name, projectId, createdAt, updatedAt — **never the value**)
- `GET  /api/projects/{projectId}/api-keys` → project-scoped list (or a `?projectId=` filter on the above)
- `PUT  /api/api-keys/{name}` body `{ projectId?: guid, value: string }` → upsert (write-only value,
  same as the TUI secret PUT; there is deliberately no GET-value endpoint)
- `DELETE /api/api-keys/{id}`

---

## 3. Where the placeholder may appear, and the per-agent field

**Syntax: `{{key:NAME}}`**, `NAME` matching `[A-Za-z0-9_.-]+`, matched with
`\{\{key:([A-Za-z0-9_.-]+)\}\}`, Ordinal. Rationale over the alternatives the card offered:

- `${NAME}` — collides with real shell-syntax values agents legitimately carry in env (and the card
  correctly notes no `${...}` precedent exists to match).
- Bare `{NAME}` (the `ChannelPreamble.Render` style, `ChannelPreamble.cs:27-38`) — single braces are
  too common in JSON-in-env values; the preamble gets away with it because its input is an
  operator-authored template, not arbitrary env text.
- `{{key:NAME}}` — the double brace plus the `key:` discriminator makes accidental collision
  effectively impossible, keeps the brace-token family resemblance, and leaves namespace room
  (`{{key:...}}` is the only form; anything else in `{{...}}` is inert text).

**Collision/escaping: none in v1, by design.** A value that legitimately contains the literal ten
characters `{{key:X}}` would be misread — but the failure is LOUD (unknown key → launch fails naming
the token, §5), never a silent substitution or a silent pass-through. An escape syntax is complexity
for a case nobody has; the plan documents the limitation instead.

**Allowed in: env VALUES only.** Not args, not `SystemPromptAppend`, not the brief. Args are visible
to any process lister (`Get-WmiObject Win32_Process ... CommandLine` is used by our own docs), are
quoted into failure reasons and argv-integrity tests, and `--append-system-prompt` text additionally
lands in transcripts. A secret in either is a secret published. The tripwire (§4) scans args too — and
**rejects** a placeholder found there with "placeholders are not supported in args", so the rule is
enforced, not just documented.

**The per-agent field:** new column `Agent.LaunchEnvJson` (`text`, JSON `Dictionary<string,string>`,
default `{}`), exposed as `launchEnv` on the agent DTOs. This is where "per-agent" lives:

```
ANTHROPIC_API_KEY={{key:anthropic-maven}}
FOO=literal-non-secret-value
```

Merge order (both resolvers): definition/profile env → **agent `LaunchEnv`** → kind defaults →
`options.ExtraEnv` last. Agent env must NOT outrank `ExtraEnv` — that's the ANTIPHON_* orchestration
block (`AgentTaskDispatcher.BuildEnv` at `:1489-1505`, `AgentControlService.cs:149-154`) and a
per-agent override of `ANTIPHON_SESSION_ID` would be a self-inflicted CARD-0006. Plumb it as a new
`AgentEnv` member on `AgentLaunchOptions` (`server/Application/Dtos/AgentLaunchSpec.cs:9-14`) applied
before `ExtraEnv` in `AgentRegistry.Resolve` (`:116-121`) and `ResolveCoreAsync` (`:258-262`), so the
legacy no-profile path and the managed-profile path get identical semantics.

Because resolution (§4) runs over the **merged** env regardless of which layer contributed a value,
placeholders also work for free in profile revision non-secret env values and appsettings
`AgentDefinition.Env` — which is exactly the migration road away from `AgentTuiSecret` (§7).

---

## 4. Resolution point, scope selection, and the tripwire

**New service `ApiKeyEnvResolver`** (scoped; DB + `IApiKeyProtector`):

```csharp
Task<IReadOnlyDictionary<string,string>> ResolveAsync(
    IReadOnlyDictionary<string,string> env, Guid? projectId, string subject, CancellationToken ct)
```

Scans each value for `{{key:NAME}}` tokens; for each distinct name, looks up project-scoped first
(`ProjectId == projectId && Name == name`), then global (`ProjectId == null`). **Project overrides
global** — stated reason: a project key exists precisely to specialize what the installation-wide
default would do, and the narrower scope winning is the same rule every other override in this codebase
follows (agent compaction overrides beat installation settings, `Agent.cs:52-66`). Multiple tokens per
value and multiple values per env are supported (simple replace, same shape as `ChannelPreamble.Render`).

**Call sites — resolution happens where `Env` is finalized, in both bottom-level paths:**

1. `AgentTuiLaunchResolver.ResolveCoreAsync` — after the `ExtraEnv` merge at `:258-262`, before the
   spec is constructed at `:296`. The resolver is already async + DB-scoped; inject `ApiKeyEnvResolver`.
2. The `AgentRegistry.Resolve` path is sync/no-DB and stays that way. Its two direct callers wrap the
   result: `AgentTaskDispatcher.BuildLaunchSpec` (`:1470`) and `CardService` (`:572`) call
   `spec = spec with { Env = await _apiKeyEnvResolver.ResolveAsync(spec.Env, projectId, subject, ct) }`.
   `AgentLaunchResolution.ResolveLegacy` (`AgentTuiLaunchResolver.cs:78-89`) is reached from async
   callers only — same wrap in `AgentLaunchResolution`, which becomes the one place legacy specs get
   finalized (it takes the resolver as a parameter; it's already the funnel for 5 of 7 sites).

**Which `projectId`:** the agent's board's project when the agent has a board
(`agent.BoardId → Board.ProjectId`); for card launches, `card.Board.ProjectId`; else **null (global
keys only)**. Pool delegates therefore resolve global keys only in v1 — deriving a project from
`task.WorkingDirectory` vs `Project.LocalRepositoryPath` is explicitly rejected (worktrees are sibling
directories; a prefix match would silently mis-scope), and a pinned standing agent with a board gets
project keys already, which covers the CARD-0084 "Grok delegate on its own key" case via a pinned agent.

**The tripwire — the rule that outlives this plan:** in `AgentSessionService.BuildRuntimeLaunchSpec`
(`AgentSessionService.cs:983`), before returning: if any `Env` value still matches the placeholder
regex, or any arg matches it, **throw**. This is the CARD-0006/CARD-0055 lesson applied in advance:
every launch from every present and future path passes through this method on its way to
`adapter.StartAsync` (`:170,359,822` — the only three sites in the server), so a new code path that
forgets resolution fails its first launch with a named token instead of exporting the literal
`{{key:...}}` string into a real process — or worse, being "fixed" by someone deleting the placeholder.
The tripwire is sync string-scanning; it does NOT resolve (no DB here), it only refuses.

---

## 5. Failure modes — loud, specific, and never carrying the value

- **Unknown key** (name not found in either scope, or found only under a DIFFERENT project):
  `ConflictException("API key '{name}' referenced by {subject} was not found (searched project {projectId?} then global). Add it under Settings → API Keys or fix the placeholder.", "api_key_not_found")`
  — the launch fails; the dispatcher already stores exception messages as the task failure reason, and
  the interactive path surfaces problem-details. Matches the resolver's existing `profile_not_validated`
  convention (`AgentTuiLaunchResolver.cs:203-211`). The message names the KEY NAME and scope searched —
  never any value.
- **Decrypt failure / protection not ready:** same split `ResolveCoreAsync` already makes —
  `CryptographicException` → `ServiceUnavailableException("secret_protection_unavailable")`
  (`AgentTuiLaunchResolver.cs:248-254`). Reuse verbatim.
- **Oversize after substitution** (resolved value pushes an env value past 4000): same
  "could not be read safely" conflict as `:240-245`.
- **Tripwire hit** (§4): `InvalidOperationException` naming the env var (name only) or arg index and
  the surviving token — configuration-gap style, per the `AgentRegistry.DefinitionNameForKind`
  precedent (`AgentRegistry.cs:89-96`), so the message survives verbatim into a task failure reason.
- **Never silent pass-through, never silent omission** — the card demanded this and every arm above
  ends in a throw. There is no "leave the literal text in" branch.

### Keeping values out of logs, incidents, and reports

- Resolution logs (Information) name key names and the subject only; the resolver never logs a value
  or a substituted env value.
- Exception messages carry key NAMES only (all arms above are constructed that way — none interpolates
  a value). `AgentIncident.Message`, task failure reasons, and check-in digests therefore can't carry
  one via this feature.
- The resolved value's exposure is identical to today's `AgentTuiSecret` exposure: it exists in
  `spec.Env`, serialized once to the localhost session-runner and into the child's environment. No new
  surface. The UI precedent "Command preview (no secrets)" (`AgentTuiProfileModal.tsx:384`) carries
  over: any future spec-preview surface must render `{{key:NAME}}` UNresolved.
- The API never returns a value after write (§2); list DTOs are metadata-only.
- Test to pin: a resolution failure's exception message and logged output contain the key name and NOT
  a sentinel value stored through the real protector.

---

## 6. UI

Three surfaces, all following `AgentTuiProfileModal.tsx`'s write-only secret pattern
(`(configured)`/`(missing)` label, value field that clears after save, `:289-336`):

1. **Global keys — new "API Keys" panel** in the settings area (sibling of `ProjectConfig.tsx` /
   `ProviderConfig.tsx` in `client/src/features/settings/`). Table of name + scope + updatedAt, add
   row (name + value), per-row replace-value and delete. One shared `ApiKeysSection` component
   parameterized by `projectId?`.
2. **Project keys — a section inside `ProjectConfig.tsx`** (`client/src/features/settings/ProjectConfig.tsx`,
   the existing per-project settings surface): the same `ApiKeysSection` with the project's id.
3. **`AgentSettingsModal.tsx`** (`client/src/features/agents/AgentSettingsModal.tsx`) — one new field
   in the existing form (fields at `:54-71`, labels at `:199-...`): a textarea `Launch environment
   (KEY=value per line)` bound to `launchEnv`, with helper text: `values may reference stored API keys
   as {{key:NAME}}; resolved at launch, project keys override global`. Reuse the `envToText`/`textToEnv`
   helpers pattern from `AgentTuiProfileModal.tsx:111,157`.

Client API hooks mirror `usePutAgentTuiSecret`/`useClearAgentTuiSecret` (`AgentTuiProfileModal.tsx:19-21`).

---

## 7. Relationship to `AgentTuiSecret` — decided: coexist now, converge behind a filed follow-up

**Decision: the new store does NOT replace `AgentTuiSecret` in this card, and the coexistence is
bounded, not open-ended.** The two stores differ in contract, not just scoping: `AgentTuiSecret`
participates in profile VALIDATION (`profile_not_validated` when a declared secret name has no row,
`ResolveCoreAsync:222-231`) and in the `AuthenticationMode` machinery (`WrapperManaged` vs
`ManagedEnvironment`, `AgentTuiProfileModal.tsx:264-265`). Ripping that out under this card would
couple a new feature to a risky migration of a working subsystem.

**The named maintenance cost of coexisting:** two encrypted stores, two protectors sharing one key
ring, two UI idioms for "enter a secret", and an operator question ("where does this key go?") that
needs one documented answer: *profile secrets authenticate the RUNNER PROGRAM under a profile; API
keys are named values agents reference by placeholder.* That sentence goes in both UI sections.

**The convergence path (follow-up card, filed as part of S4):** because resolution runs over the merged
env (§3), a profile's non-secret env value can already say `ANTHROPIC_API_KEY={{key:anthropic-default}}`.
The follow-up migrates each `ManagedEnvironment` profile by creating a global `ApiKey` per secret,
rewriting the profile env to reference it, flipping the profile to `WrapperManaged`-with-references,
then dropping `AgentTuiSecret` and its endpoints/UI. That card can be small precisely because this
design made profile env a resolution surface. Not attempted here.

---

## 8. Slices

**S1 — key store + protector + API** (server only, independently shippable):
`ApiKey` entity + migration (two filtered unique indexes, cascade FK), `IApiKeyProtector` +
`DataProtectionApiKeyProtector` (+ one-line registration in `AgentTuiDataProtectionSetup.Configure`),
`ApiKeyEndpoints` CRUD, DTOs. Tests: round-trip through the real protector, uniqueness (global vs
project same name legal; duplicate within scope 409), project cascade delete, list never returns a
value, 4000-char ceiling.

**S2 — per-agent env + resolution + tripwire** (the feature works end-to-end via API after this):
`Agent.LaunchEnvJson` column + DTO plumbing; `AgentLaunchOptions.AgentEnv` merged in both resolvers
(order pinned by test: definition/profile → agent → kind defaults → ExtraEnv wins); `ApiKeyEnvResolver`;
wraps at `ResolveCoreAsync`, `AgentLaunchResolution`, `AgentTaskDispatcher.BuildLaunchSpec`,
`CardService:572`; project-id derivation rule; the `BuildRuntimeLaunchSpec` tripwire. Tests:
placeholder resolves (global; project; project-overrides-global), unknown key throws naming key+scope
(and the message contains no value), deleted-after-referenced fails next launch, agent-env cannot
override `ANTIPHON_SESSION_ID`, placeholder in an ARG refused by the tripwire, unresolved placeholder
in env refused by the tripwire (constructed spec bypassing the resolvers — the "future forgotten path"
case), placeholder in profile non-secret env resolves.

**S3 — UI:** `ApiKeysSection` (global settings panel + `ProjectConfig` embed), `AgentSettingsModal`
launch-env field, client hooks + vitest coverage mirroring the existing profile-modal secret tests.

**S4 — housekeeping, no code:** file the `AgentTuiSecret` convergence follow-up card (§7) and the
"project scope for pool delegates" idea (§4) as explicit backlog; update AGENTS.md/CLAUDE.md gotchas
only if implementation surfaces one.

Suggested split: S1+S2 one delegate (server, coherent), S3 second delegate, S4 with whoever merges.

## 9. Deliberately not in scope

- **Placeholders in args or system-prompt text** — rejected for leak-surface reasons (§3), enforced by
  the tripwire, not just documented.
- **Escape syntax for a literal `{{key:...}}`** — no known need; failure is loud if it ever occurs (§3).
- **Permissions/RBAC on keys** — no auth subsystem exists to hang it on (§1).
- **Migrating `AgentTuiSecret`** — follow-up card (§7).
- **Project derivation for pool delegates from working directory** — rejected as unreliable (§4);
  they get global keys.
- **Key-value read-back API or UI reveal** — write-only by design.
- **Rotation/expiry/audit-trail for keys** — nothing else in this codebase has it; not invented here.

## 10. Card housekeeping

On merge of each slice: `card.ps1` comment on CARD-0106 naming the slice and commit. After S4, move
CARD-0106 to done and link the new convergence follow-up card. CARD-0084/0099 (Grok/Codex delegates)
should be cross-commented when S2 lands — a pinned Grok agent + a project `{{key:...}}` is their
per-provider-credential answer.
