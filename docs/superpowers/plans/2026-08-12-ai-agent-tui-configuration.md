# AI Agent TUI Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add database-backed, cross-platform AI terminal-runner profiles with protected credentials, model discovery, OpenCode support, and per-agent runner/model selection, then prove Atlas through `ocg.ps1` with default and explicit models.

**Architecture:** Keep file-based `Agents` definitions as a bounded import/rollback source, but resolve every new managed launch from an immutable `AgentTuiProfileRevision`. A scoped profile service owns CRUD, catalogues, validation, and migration; an external process-probe seam owns bounded CLI calls; ASP.NET Data Protection protects write-only environment secrets with a key ring outside the repository/database. The React Settings tab manages profiles and the existing agent modals select an enabled profile plus an optional opaque model identifier.

**Tech Stack:** .NET 9, ASP.NET Core Minimal APIs, EF Core/PostgreSQL, ASP.NET Data Protection, `System.Diagnostics.Process`, TUnit/Shouldly, React 19, TypeScript, Mantine 8, TanStack Query, Vitest/MSW.

---

## File structure

### Backend domain and persistence

- Create `server/Domain/Enums/AgentTuiEnums.cs`: authentication, source, availability, capability, and validation enums.
- Create `server/Domain/Entities/AgentTuiProfile.cs`: durable profile identity and active revision pointer.
- Create `server/Domain/Entities/AgentTuiProfileRevision.cs`: immutable executable/argument/environment/guidance snapshot.
- Create `server/Domain/Entities/AgentTuiSecret.cs`: protected environment value metadata and ciphertext only.
- Create `server/Domain/Entities/AgentTuiModel.cs`: opaque model catalogue entry with provenance and availability.
- Create `server/Domain/Entities/AgentTuiValidationRun.cs`: bounded sanitized validation result.
- Modify `server/Domain/Entities/Agent.cs`: selected profile plus optional exact model.
- Modify `server/Domain/Entities/AgentSession.cs`: effective profile revision plus effective model.
- Modify `server/Domain/Enums/AgentKind.cs`: add `OpenCode` without renumbering existing values.
- Modify `server/Infrastructure/Data/AppDbContext.cs`: DbSets, constraints, indexes, relationships, and max lengths.
- Create an EF migration with `dotnet ef migrations add AddAgentTuiProfiles --project server`; never hand-author it.

### Backend application and infrastructure

- Create `server/Application/Settings/AgentTuiSettings.cs`: 30-second probe limit, output bound, and external key-ring path.
- Create `server/Application/Interfaces/IAgentTuiSecretProtector.cs`: purpose-isolated protect/unprotect seam.
- Create `server/Application/Interfaces/IRunnerProcessProbe.cs`: bounded child-process seam.
- Create `server/Application/Dtos/AgentTuiDtos.cs`: API and internal launch/probe contracts; no secret read DTO.
- Create `server/Infrastructure/Agents/Tui/DataProtectionAgentTuiSecretProtector.cs`: Data Protection implementation.
- Create `server/Infrastructure/Agents/Tui/RunnerProcessProbe.cs`: argument-list-based, no-shell process execution with timeout/output limits.
- Create `server/Application/Services/AgentTuiRunnerCatalog.cs`: curated runner metadata, capabilities, model suggestions, and legacy model mapping.
- Create `server/Application/Services/AgentTuiOperationCoordinator.cs`: DI-singleton join semantics for one discovery/validation per profile.
- Create `server/Application/Services/AgentTuiProfileService.cs`: CRUD, immutable revisions, secret writes, discovery, validation, and sanitized DTO mapping.
- Create `server/Application/Services/AgentTuiProfileImporter.cs`: idempotent file-definition import and legacy agent assignment.
- Create `server/Application/Services/AgentTuiLaunchResolver.cs`: active revision, secret injection, exact/default model, and effective launch metadata.
- Create `server/Api/Endpoints/AgentTuiEndpoints.cs`: `/api/agent-tui` public contract.
- Create `server/Infrastructure/Agents/SessionRunner/RunnerOpenCodeAdapter.cs`: dedicated PTY adapter with explicitly degraded activity semantics.
- Create `server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs`: bounded-label counters/gauges and Prometheus text rendering.
- Modify `server/Application/Services/AgentRegistry.cs`: expose one common resolver for imported snapshots while retaining file lookup.
- Modify `server/Application/Services/AgentControlService.cs`, `CardService.cs`, and `OrchestratorService.cs`: resolve selected profiles for assigned agents and record effective metadata.
- Modify `server/Application/Dtos/AgentDtos.cs` and `AgentSessionDtos.cs`: additive selection/effective fields.
- Modify `server/Application/Services/AgentService.cs`: default assignment, selection validation, and DTO mapping.
- Modify `server/Infrastructure/Agents/Pty/AgentProtocolAdapterFactory.cs`: map `OpenCode` to its own adapter.
- Modify `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`: keep structured Claude transcripts Claude-only; do not mislabel OpenCode PTY activity.
- Modify `server/Program.cs`: typed settings, Data Protection, services, import, endpoints, metrics.

### Frontend

- Create `client/src/api/agentTui.ts`: profile/model/capability types and TanStack Query hooks.
- Create `client/src/features/settings/AgentTuiConfig.tsx`: list and state handling.
- Create `client/src/features/settings/AgentTuiProfileModal.tsx`: profile revision editor, wrapper/direct setup, environment metadata, guidance, and command preview.
- Create `client/src/features/settings/AgentTuiSecrets.tsx`: write/replace/clear-only secret controls.
- Create `client/src/features/settings/AgentTuiModels.tsx`: discovery, provenance, availability, and capability display.
- Create `client/src/features/agents/AgentTuiSelection.tsx`: profile picker plus exact/default model picker.
- Modify `client/src/features/settings/SettingsPage.tsx`: add `AI Agent TUI` tab.
- Modify `client/src/features/agents/AgentCreateModal.tsx` and `AgentSettingsModal.tsx`: use `AgentTuiSelection` and retain model-level compatibility only in the API.
- Modify `client/src/api/agents.ts`: additive selection/effective types and request fields.
- Add focused Vitest files next to every new component plus updates to `AgentsPage.test.tsx`.

### Tests and operations

- Create `tests/Antiphon.Tests/AgentTui/AgentTuiPersistenceTests.cs`.
- Create `tests/Antiphon.Tests/AgentTui/AgentTuiSecretProtectorTests.cs`.
- Create `tests/Antiphon.Tests/AgentTui/AgentTuiProfileServiceTests.cs`.
- Create `tests/Antiphon.Tests/AgentTui/AgentTuiDiscoveryTests.cs`.
- Create `tests/Antiphon.Tests/AgentTui/AgentTuiLaunchResolverTests.cs`.
- Create `tests/Antiphon.Tests/Agents/OpenCodeAdapterTests.cs`.
- Create `tests/Antiphon.Tests/AgentTui/AgentTuiApiTests.cs`.
- Create `docs/ai-agent-tui-configuration.md`: operator/key-custody/recovery/setup guide.
- Create `scripts/verify-agent-tui-profile.ps1`: sanitized local smoke using REST, process metadata, terminal/transcript evidence, and no secret values.

---

### Task 1: Persist immutable runner profiles and effective session selection

**Files:**
- Create the domain/persistence files listed above.
- Modify `server/Domain/Entities/Agent.cs`, `server/Domain/Entities/AgentSession.cs`, `server/Domain/Enums/AgentKind.cs`, and `server/Infrastructure/Data/AppDbContext.cs`.
- Test `tests/Antiphon.Tests/AgentTui/AgentTuiPersistenceTests.cs`.

- [ ] **Step 1: Write the failing model-contract test**

```csharp
[Test]
public void Model_has_profile_revision_secret_model_and_effective_session_contracts()
{
    using var db = NewModelContext();
    var profile = db.Model.FindEntityType(typeof(AgentTuiProfile));
    profile.ShouldNotBeNull();
    profile.FindIndex(profile.FindProperty(nameof(AgentTuiProfile.DisplayName))!).IsUnique.ShouldBeTrue();
    var revision = db.Model.FindEntityType(typeof(AgentTuiProfileRevision))!;
    revision.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(
        [nameof(AgentTuiProfileRevision.ProfileId), nameof(AgentTuiProfileRevision.RevisionNumber)]))
        .IsUnique.ShouldBeTrue();
    var secret = db.Model.FindEntityType(typeof(AgentTuiSecret))!;
    secret.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(
        [nameof(AgentTuiSecret.ProfileId), nameof(AgentTuiSecret.Name)]))
        .IsUnique.ShouldBeTrue();
    typeof(Agent).GetProperty(nameof(Agent.TuiProfileId)).ShouldNotBeNull();
    typeof(AgentSession).GetProperty(nameof(AgentSession.TuiProfileRevisionId)).ShouldNotBeNull();
    AgentKind.OpenCode.ShouldBe((AgentKind)3);
}
```

- [ ] **Step 2: Run it and verify RED**

Run:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-agent-tui\ -- --treenode-filter "/Antiphon.Tests/AgentTuiPersistenceTests/*"
```

Expected: compile failure because the new entities/properties and `AgentKind.OpenCode` do not exist.

- [ ] **Step 3: Add the minimal domain contract**

Use these exact enum values so existing persisted `AgentKind` values remain stable:

```csharp
public enum AgentKind { Raw = 0, ClaudeCode = 1, Codex = 2, OpenCode = 3 }
public enum AgentTuiAuthenticationMode { WrapperManaged = 0, ManagedEnvironment = 1 }
public enum AgentTuiProfileSource { ImportedFile = 0, Operator = 1 }
public enum AgentTuiModelSource { Curated = 0, Discovered = 1, Operator = 2 }
public enum AgentTuiModelAvailability { Unverified = 0, Verified = 1, Stale = 2, Unavailable = 3 }
public enum AgentTuiCapabilityState { Supported = 0, Unsupported = 1, Degraded = 2, Unknown = 3 }
public enum AgentTuiValidationStatus { NeverRun = 0, Running = 1, Succeeded = 2, Partial = 3, Failed = 4, TimedOut = 5 }
```

Use durable identities and JSON strings only as private persistence format for ordered string/dictionary values; public DTOs remain typed collections. `AgentTuiProfileRevision` must carry `ArgumentsJson`, `DiscoveryArgumentsJson`, `VersionArgumentsJson`, `NonSecretEnvironmentJson`, `SecretEnvironmentNamesJson`, `ModelArgumentName`, `Guidance`, and `CreatedAt`. Add `TuiProfileId`/`ModelId` to `Agent` and `TuiProfileRevisionId`/`EffectiveModelId` to `AgentSession`.

- [ ] **Step 4: Configure EF constraints and rerun GREEN**

Required indexes and delete behaviour:

```csharp
entity.HasIndex(p => p.DisplayName).IsUnique().HasDatabaseName("IX_AgentTuiProfiles_DisplayName");
entity.HasIndex(r => new { r.ProfileId, r.RevisionNumber }).IsUnique()
    .HasDatabaseName("IX_AgentTuiProfileRevisions_ProfileId_RevisionNumber");
entity.HasIndex(s => new { s.ProfileId, s.Name }).IsUnique()
    .HasDatabaseName("IX_AgentTuiSecrets_ProfileId_Name");
entity.HasIndex(m => new { m.ProfileId, m.Identifier }).IsUnique()
    .HasDatabaseName("IX_AgentTuiModels_ProfileId_Identifier");
```

Profile deletion is restricted while agents, sessions, or the installation default reference it. Secret/model rows cascade with profile deletion. Revision rows are retained while sessions reference them.

- [ ] **Step 5: Generate the migration with the CLI**

Run:

```powershell
.\stop-server.ps1
dotnet ef migrations add AddAgentTuiProfiles --project server
```

Expected: a migration plus updated `AppDbContextModelSnapshot.cs`; inspect it for the five new tables, the four additive agent/session columns, unique indexes, and no plaintext seed values.

- [ ] **Step 6: Commit**

```powershell
git add server/Domain server/Infrastructure/Data server/Migrations tests/Antiphon.Tests/AgentTui/AgentTuiPersistenceTests.cs
git commit -m "feat(agent-tui): persist runner profile revisions"
```

### Task 2: Protect write-only environment secrets outside the database key boundary

**Files:**
- Create `server/Application/Settings/AgentTuiSettings.cs`.
- Create `server/Application/Interfaces/IAgentTuiSecretProtector.cs`.
- Create `server/Infrastructure/Agents/Tui/DataProtectionAgentTuiSecretProtector.cs`.
- Modify `server/Program.cs` and `server/appsettings.json`.
- Test `tests/Antiphon.Tests/AgentTui/AgentTuiSecretProtectorTests.cs`.

- [ ] **Step 1: Write failing confidentiality tests**

```csharp
[Test]
public void Protect_is_profile_and_environment_purpose_isolated()
{
    var provider = new EphemeralDataProtectionProvider();
    var sut = new DataProtectionAgentTuiSecretProtector(provider);
    var profile = Guid.NewGuid();
    var cipher = sut.Protect(profile, "OPENAI_API_KEY", "canary-secret");
    cipher.ShouldNotContain("canary-secret");
    sut.Unprotect(profile, "OPENAI_API_KEY", cipher).ShouldBe("canary-secret");
    Should.Throw<CryptographicException>(() => sut.Unprotect(Guid.NewGuid(), "OPENAI_API_KEY", cipher));
    Should.Throw<CryptographicException>(() => sut.Unprotect(profile, "OTHER_KEY", cipher));
}
```

Also add:

- a reflection/serialization test proving no read DTO has `Value`, `Plaintext`, `Ciphertext`, or `ProtectedValue`;
- a persisted-key-ring test that creates a provider, protects a value, recreates the provider from the same temporary key directory, and decrypts it;
- a wrong/missing key-ring test that proves managed-secret decryption fails closed while wrapper-managed profiles require no key access;
- table-driven path-resolution tests for Windows `%LOCALAPPDATA%`, Linux/macOS `$XDG_DATA_HOME`, and the Unix home-directory fallback without reading the developer machine's real environment.

- [ ] **Step 2: Verify RED**

Run the `AgentTuiSecretProtectorTests` filter; expect missing interface/implementation failures.

- [ ] **Step 3: Implement the protection seam**

```csharp
public interface IAgentTuiSecretProtector
{
    string Protect(Guid profileId, string environmentName, string plaintext);
    string Unprotect(Guid profileId, string environmentName, string protectedValue);
}

public sealed class DataProtectionAgentTuiSecretProtector(IDataProtectionProvider provider)
    : IAgentTuiSecretProtector
{
    private IDataProtector For(Guid id, string name) => provider.CreateProtector(
        "Antiphon", "AgentTui", "ProfileSecret", id.ToString("D"), name);
    public string Protect(Guid id, string name, string plaintext) => For(id, name).Protect(plaintext);
    public string Unprotect(Guid id, string name, string value) => For(id, name).Unprotect(value);
}
```

Configure `AddDataProtection().SetApplicationName("Antiphon.AgentTui")` and persist keys to the typed path. Put the pure path-selection rules in `AgentTuiSettings` so tests can supply the platform and environment values explicitly. The default path is `%LOCALAPPDATA%/Antiphon/DataProtection-Keys` on Windows and `$XDG_DATA_HOME/antiphon/data-protection-keys` (or `~/.local/share/antiphon/...`) on Linux/macOS. Create it with owner-only permissions where supported. Expose only a ready/not-ready status. Do not log the directory contents, key XML, secret body, ciphertext, or child environment.

- [ ] **Step 4: Run GREEN and commit**

Run the focused test, then commit as `feat(agent-tui): protect managed runner secrets`.

### Task 3: Add profile CRUD, immutable revisions, curated models, and idempotent import

**Files:**
- Create `server/Application/Dtos/AgentTuiDtos.cs`.
- Create `server/Application/Services/AgentTuiRunnerCatalog.cs`.
- Create `server/Application/Services/AgentTuiProfileService.cs`.
- Create `server/Application/Services/AgentTuiProfileImporter.cs`.
- Modify `server/Program.cs`.
- Test `tests/Antiphon.Tests/AgentTui/AgentTuiProfileServiceTests.cs`.

- [ ] **Step 1: Write failing profile behaviour tests**

Add tests that prove:

```csharp
var created = await service.CreateAsync(request, ct);
created.Revision.ShouldBe(1);
(await service.UpdateAsync(created.Id, request with { ExpectedRevision = 1, DisplayName = "Changed" }, ct))
    .Revision.ShouldBe(2);
await Should.ThrowAsync<ConflictException>(() =>
    service.UpdateAsync(created.Id, request with { ExpectedRevision = 1 }, ct));
```

The same test class must prove one default, duplicate-without-secrets, in-use delete conflict, secret metadata-only reads, atomic secret replace/clear with optimistic concurrency, sanitized `AuditRecord` entries for secret set/replace/clear, cached profile/model reads that invoke no process probe, and an idempotent two-pass import that assigns legacy agents and preserves Claude `ModelLevel` as `fable`/`opus`/`sonnet`/`haiku`. Audit assertions require profile identity, environment-variable name, operation, result, time, and correlation identity, and prove the old value, new value, and ciphertext are absent.

- [ ] **Step 2: Verify RED**

Run `/Antiphon.Tests/AgentTuiProfileServiceTests/*`; expect missing services/DTOs.

- [ ] **Step 3: Implement runner catalogue constants**

Curated models are exact:

```csharp
ClaudeCode: fable, opus, sonnet, haiku
Codex: gpt-5.6-sol, gpt-5.6-terra, gpt-5.6-luna
OpenCode: llmgateway/grok-4-5
```

OpenCode advertises model argument and model discovery as supported, structured activity as degraded with reason `PTY quiet-time fallback; ACP/event integration not active`, session resume as unknown, remote control and system-prompt append as unsupported, and permission bypass as supported when the profile includes `--auto`. Claude retains its existing structured/resume/remote/system capabilities. Codex must not be labeled structured merely because it has a quiet detector.

- [ ] **Step 4: Implement profile service atomicity**

Create/update use one EF transaction. Update checks `ExpectedRevision`, inserts a copied immutable revision, then changes `ActiveRevisionId`. Secret PUT protects before the transaction, rechecks `ExpectedRevision`, and atomically saves the ciphertext plus a sanitized `AuditService` event; read DTOs return only `{ name, configured, updatedAt }`; DELETE clears explicitly and records the same value-free audit metadata. Duplicate copies non-secret active revision and operator models but no `AgentTuiSecret` rows. Enabling/defaulting requires a usable active revision; deleting checks agents/default/sessions.

- [ ] **Step 5: Implement one-time import**

If no profiles exist, import every `AgentRegistrySettings.Definitions` entry in deterministic name order. Preserve wrapper paths/arguments. Classify environment names containing `KEY`, `TOKEN`, `SECRET`, `PASSWORD`, or `CREDENTIAL` as managed secrets and protect them; persist all other values as ordinary environment. Set the imported current default, assign all null legacy agents, and seed the mapped exact model. On later startup, only backfill still-null agents; never overwrite managed profiles from changed files.

- [ ] **Step 6: Run GREEN and commit**

Run the focused service tests and commit as `feat(agent-tui): manage and import runner profiles`.

### Task 4: Discover models and validate profiles through bounded no-shell probes

**Files:**
- Create `server/Application/Interfaces/IRunnerProcessProbe.cs`.
- Create `server/Infrastructure/Agents/Tui/RunnerProcessProbe.cs`.
- Create `server/Application/Services/AgentTuiOperationCoordinator.cs`.
- Extend `AgentTuiProfileService`.
- Test `tests/Antiphon.Tests/AgentTui/AgentTuiDiscoveryTests.cs`.

- [ ] **Step 1: Write failing discovery tests with a recording fake probe**

```csharp
probe.Result = new RunnerProcessResult(0, "llmgateway/grok-4-5\nopenai/gpt-5.6-sol\n", "", false);
var models = await service.RefreshModelsAsync(profileId, ct);
probe.Request!.Arguments.ShouldBe(new[] {
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ocgPath, "models"
});
models.Single(x => x.Id == "llmgateway/grok-4-5").Availability.ShouldBe(Verified);
```

Add malformed, oversized, timeout, non-zero exit, duplicate, secret-shaped stderr, stale-cache preservation, and concurrent-join tests. Add validation-stage tests for executable, ordered arguments, cwd, auth readiness, version/capabilities, discovery, bounded startup probe, clean stop, and suitability.

- [ ] **Step 2: Verify RED**

Run `/Antiphon.Tests/AgentTuiDiscoveryTests/*`; expect missing probe/coordinator methods.

- [ ] **Step 3: Implement `RunnerProcessProbe`**

Use `ProcessStartInfo.UseShellExecute=false`, `RedirectStandardOutput/Error=true`, `ArgumentList.Add` per argument, a copied bounded environment dictionary, and a linked timeout token. Kill the process tree on timeout. Capture at most `MaxProbeOutputBytes` across both streams and return a sanitized result; never construct a command string.

- [ ] **Step 4: Implement discovery and merge**

Only OpenCode runs discovery initially. Use the revision's separate `DiscoveryArguments`; for the local profile these are the PowerShell wrapper prefix followed by `models`, not the launch-only `--auto --mini` sequence. Accept only single-line opaque `provider/model` identifiers matching `^[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9][A-Za-z0-9._:/-]*$`. Replace discovered entries only after a complete success; otherwise retain them as stale and retain curated/operator entries.

- [ ] **Step 5: Run GREEN and commit**

Commit as `feat(agent-tui): discover and validate runner profiles`.

### Task 5: Add the public `/api/agent-tui` contract and Prometheus-safe telemetry

**Files:**
- Create `server/Api/Endpoints/AgentTuiEndpoints.cs`.
- Create `server/Infrastructure/Agents/Tui/AgentTuiMetrics.cs`.
- Modify `server/Program.cs`.
- Test `tests/Antiphon.Tests/AgentTui/AgentTuiApiTests.cs`.

- [ ] **Step 1: Write failing API confidentiality and concurrency tests**

Using `AntiphonWebAppFactory`, cover runner types, list/create/get/patch/duplicate/delete, secret PUT/DELETE, model refresh, capabilities, validate, and validation-run read. Submit a unique canary secret and assert it is absent from every GET body, Problem Details body, command preview, metrics body, log capture, and audit content. Assert the audit stream contains only the allowed secret-operation metadata. Assert stale `expectedRevision` is HTTP 409 with `code=profile_revision_conflict`.

- [ ] **Step 2: Verify RED**

Run `/Antiphon.Tests/AgentTuiApiTests/*`; expect 404.

- [ ] **Step 3: Map endpoints and stable errors**

Map the exact routes from `docs/features/011-ai-agent-tui-configuration/04-external-api.md`. All endpoints pass `RequestAborted`; services throw the existing `ValidationException`, `ConflictException`, and `NotFoundException`. Secret PUT accepts exactly `{ value, expectedRevision }`; it never accepts query-string secrets.

- [ ] **Step 4: Add bounded-label metrics**

Expose `/metrics/agent-tui` in Prometheus text format. Labels are limited to runner type, enabled, validation state, auth mode, operation/outcome, stage, cache result, model mode, and activity mode. Never render profile/model/path/argument/environment names or any secret/ciphertext. Cover profile readiness, key readiness, secret operations, discovery counts/duration/cache age, validation counts/duration, launches/duration, imports, and revision conflicts.

- [ ] **Step 5: Run GREEN and commit**

Commit as `feat(agent-tui): expose profile administration api`.

### Task 6: Resolve every agent launch from its profile and add a dedicated OpenCode adapter

**Files:**
- Create `server/Application/Services/AgentTuiLaunchResolver.cs`.
- Create `server/Infrastructure/Agents/SessionRunner/RunnerOpenCodeAdapter.cs`.
- Modify registry, factory, agent/card/orchestrator launch paths, session DTOs/entities, and settings.
- Test `tests/Antiphon.Tests/AgentTui/AgentTuiLaunchResolverTests.cs` and `tests/Antiphon.Tests/Agents/OpenCodeAdapterTests.cs`.

- [ ] **Step 1: Write failing launch-resolution tests**

```csharp
agent.ModelId = null;
var noModel = await resolver.ResolveForAgentAsync(agent, options, ct);
noModel.Spec.Args.ShouldNotContain("--model");
agent.ModelId = "llmgateway/grok-4-5";
var exact = await resolver.ResolveForAgentAsync(agent, options, ct);
exact.Spec.Args.TakeLast(2).ShouldBe(new[] { "--model", "llmgateway/grok-4-5" });
exact.Spec.Env["OPENAI_API_KEY"].ShouldBe(canary);
exact.ProfileRevisionId.ShouldBe(activeRevision.Id);
```

Add fail-closed decrypt, disabled/missing profile, unknown model, next-session-only revision, wrapper-managed no-secret, session effective metadata, card-assigned-agent, and OpenCode factory tests.

- [ ] **Step 2: Verify RED**

Run the two focused filters; expect missing resolver/adapter failures.

- [ ] **Step 3: Implement the resolver**

Load the selected enabled profile and active revision. Deserialize ordered values, unprotect only named managed values into a new child environment dictionary, append caller extra args, then append `[ModelArgumentName, agent.ModelId]` only for a non-empty selected model. Return `ResolvedAgentTuiLaunch(Spec, ProfileId, ProfileRevisionId, EffectiveModelId, ActivityMode)`. Pass the snapshot through the same registry normalization that already scrubs Claude nesting markers.

- [ ] **Step 4: Implement `RunnerOpenCodeAdapter`**

Give OpenCode its own type/factory branch. Reuse terminal-session primitives and `CodexResponseAnalyzer` for prompt-echo removal, but use OpenCode-specific ready/done settings and class names. Wait for quiet periods only; the profile capability remains `Degraded`, and `SessionRunnerHttpClient` must leave `TranscriptEnabled` false for OpenCode.

- [ ] **Step 5: Wire interactive and card launch paths**

`AgentControlService` uses the selected profile for cardless sessions, retains Claude-only name/preamble/resume behavior by resolved kind, and records revision/model on fresh and resumed session rows. `CardService` resolves `card.AssignedAgent` when present; compatibility requests with an explicit legacy definition stay on the old resolver. `OrchestratorService` resolves an assigned agent when dispatch data includes one and otherwise uses the installation default profile.

- [ ] **Step 6: Run GREEN plus affected regression suites and commit**

Run launch/adapter filters, `AgentControlServiceIntegrationTests`, `AgentRegistryTests`, and `AgentProtocolAdapterFactoryTests`. Commit as `feat(agent-tui): launch selected profiles and opencode`.

### Task 7: Add per-agent profile and exact/default model API selection

**Files:**
- Modify `server/Application/Dtos/AgentDtos.cs`, `AgentService.cs`, and `AgentEndpoints.cs`.
- Modify `client/src/api/agents.ts` only after backend tests are green.
- Extend `AgentTuiApiTests` and `AgentControlServiceIntegrationTests`.

- [ ] **Step 1: Write failing create/update compatibility tests**

Prove create with omitted selection uses the installation default; create with profile/model persists both; update with `tuiProfileId` and null `modelId` clears exact selection; disabled/missing/profile-mismatched model returns an actionable 409/422; an already-running session retains its effective revision/model while the configured selection changes.

- [ ] **Step 2: Verify RED, implement, verify GREEN**

Add `TuiProfileId` and `ModelId` to create/update requests and summary/detail DTOs; add configured and live effective fields. Treat an omitted/null update profile as leave-unchanged; callers clear a model by resending the current profile with `modelId:null`. Keep `ModelLevel` additive during migration but stop showing it in the new UI.

- [ ] **Step 3: Commit**

Commit as `feat(agent-tui): select runner and model per agent`.

### Task 8: Build the AI Agent TUI Settings experience

**Files:**
- Create `client/src/api/agentTui.ts` and all new settings components/tests.
- Modify `client/src/features/settings/SettingsPage.tsx`.

- [ ] **Step 1: Write failing Settings UI tests**

MSW-backed tests must prove: list state; create wrapper/direct profiles; immutable revision conflict feedback; command preview; enable/default controls; write-only secret replacement/clear and input clearing; discovered/curated/stale badges; capability reasons; refresh; validation stages; duplicate; in-use delete remediation; and setup guidance.

Example assertion:

```tsx
await userEvent.type(screen.getByLabelText('Secret value for OPENAI_API_KEY'), 'ui-canary')
await userEvent.click(screen.getByRole('button', { name: 'Save secret' }))
await waitFor(() => expect(screen.getByLabelText('Secret value for OPENAI_API_KEY')).toHaveValue(''))
expect(screen.queryByDisplayValue('ui-canary')).not.toBeInTheDocument()
expect(await screen.findByText('Configured')).toBeInTheDocument()
```

- [ ] **Step 2: Verify RED**

Run `npm test -- AgentTuiConfig.test.tsx AgentTuiProfileModal.test.tsx` from `client`; expect missing modules/tab.

- [ ] **Step 3: Implement typed hooks and components**

Use TanStack Query for every REST call, with bounded `staleTime` for profile, runner-type, capability, and model reads so opening either form does not trigger probes or serialized refetch churn. The tab label is `AI Agent TUI`. The editor uses repeatable ordered argument rows (not a shell string), key/value environment rows, explicit wrapper-managed/Antiphon-managed auth, separate version/discovery probe arguments, optional model argument, guidance, and a non-shell command preview. Secret values exist only in local password inputs, clear after settled writes, and never seed from responses.

- [ ] **Step 4: Run GREEN, lint, build, and commit**

```powershell
npm test -- AgentTuiConfig.test.tsx AgentTuiProfileModal.test.tsx
npm run lint
npm run build
```

Commit as `feat(agent-tui): add runner profile settings ui`.

### Task 9: Add runner/model pickers to agent create and settings

**Files:**
- Create `client/src/features/agents/AgentTuiSelection.tsx` and tests.
- Modify agent create/settings modals, `client/src/api/agents.ts`, fixtures, and `AgentsPage.test.tsx`.

- [ ] **Step 1: Write failing picker tests**

Prove enabled profiles only, runner capability/help text, verified/curated/stale model labels, a `Use runner default` option that sends null, profile change clears a model from another namespace, and settings show configured versus live revision/model when a restart is pending.

- [ ] **Step 2: Verify RED, implement, verify GREEN**

Replace `ModelLevelSelect` in both forms with `AgentTuiSelection`. Submit `{ tuiProfileId, modelId }`; retain `modelLevel` in TypeScript response types for compatibility but do not submit it from the new forms. Disable create/save until an enabled profile is selected.

- [ ] **Step 3: Run affected frontend suite and commit**

```powershell
npm test -- AgentTuiSelection.test.tsx AgentsPage.test.tsx
npm run lint
npm run build
```

Commit as `feat(agent-tui): choose runner profiles per agent`.

### Task 10: Document, configure Atlas, restart, and prove real OpenCode responses

**Files:**
- Create `docs/ai-agent-tui-configuration.md` and `scripts/verify-agent-tui-profile.ps1`.
- Update `appsettings.json.example` with non-secret key-ring and probe settings only.
- Do not commit `server/appsettings.Development.json` or any credential.

- [ ] **Step 1: Write the smoke script before local mutation**

The script accepts server URL, agent ID/name, profile name, optional model, and expected exact reply. It creates/updates the profile through REST, refreshes/validates it, patches the agent selection, stops/starts the agent, posts a message, and verifies sanitized process/effective session metadata plus an exact assistant/terminal response. It scans all retained JSON/text evidence for a caller-supplied canary and exits non-zero on exposure.

- [ ] **Step 2: Create `OpenCode Gateway` locally**

Use:

```text
exe: pwsh.exe
launch arguments: -NoProfile -ExecutionPolicy Bypass -File C:\Users\mike.ciechan\.local\bin\ocg.ps1 --auto --mini
version arguments: -NoProfile -ExecutionPolicy Bypass -File C:\Users\mike.ciechan\.local\bin\ocg.ps1 --version
discovery arguments: -NoProfile -ExecutionPolicy Bypass -File C:\Users\mike.ciechan\.local\bin\ocg.ps1 models
authentication: WrapperManaged
model argument: --model
```

Do not copy API key/proxy values out of `ocg.ps1`.

- [ ] **Step 3: Assign Atlas and verify default model omission**

Patch `Atlas-Orchestrator` to the OpenCode profile with `modelId:null`, freshly start it, inspect the actual child command line to prove no `--model`, send `Reply with exactly: Atlas OpenCode default verified.`, and require that exact response in terminal/adapter evidence with an idle/completed state.

- [ ] **Step 4: Verify explicit model**

Select one verified discovered model, or `llmgateway/grok-4-5` if discovery has no usable result. Stop/fresh-start, prove the process contains separate `--model` and exact model arguments, send `Reply with exactly: Atlas OpenCode explicit model verified.`, and require the exact response.

- [ ] **Step 5: Restart and run canonical stack smoke**

Use the current repo restart control path, preserving the session runner unless its binary changed. On this machine the final check is:

```powershell
.\verify-dev-stack.ps1 -SimpleMode
```

Also verify `/health`, `/api/agent-tui/profiles`, `/metrics/agent-tui`, the Atlas agent DTO, and the selected session buffer/transcript endpoint without exposing any secrets.

- [ ] **Step 6: Commit documentation and smoke automation**

Commit as `docs(agent-tui): add setup and verification guide`.

### Task 11: Final regression and requirements audit

**Files:** all changed files; no new production behaviour in this task.

- [ ] **Step 1: Run backend focused and full suites**

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-agent-tui\ -- --treenode-filter "/Antiphon.Tests/AgentTui*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-agent-tui\
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-agent-tui\
```

- [ ] **Step 2: Run frontend verification**

```powershell
Set-Location client
npm test
npm run lint
npm run build
Set-Location ..
```

- [ ] **Step 3: Run live and security gates**

Run `scripts/verify-agent-tui-profile.ps1` for both default and explicit model, then `verify-dev-stack.ps1 -SimpleMode`. Search repo diff, HTTP captures, retained smoke evidence, and logs for canary secrets. Inspect `git diff --check`, `git status`, migration SQL, and every session/profile DTO.

- [ ] **Step 4: Audit spec coverage**

Map FR-1–FR-19 and NFR-1–NFR-12 to implemented tests without creating a coverage artifact. Confirm immutable revisions, safe key custody, write-only reads, discovery fallback, truthful degraded OpenCode activity, next-session semantics, compatibility import, UI guidance, cross-platform path/env behavior, bounded probes, observability redaction, and both Atlas round trips.

- [ ] **Step 5: Finish the stacked implementation branch**

Use `superpowers:verification-before-completion`, then `superpowers:finishing-a-development-branch`. Do not push or open PRs unless explicitly requested.
