# CARD-0114 — Converge `AgentTuiSecret` into the CARD-0106 API-key store

**Date:** 2026-08-21 · **Card:** CARD-0114 · **Status:** plan (no implementation in this pass)
**Grounded against:** master `1ea8404`, every file:line below read on 2026-08-21, plus the live
`antiphon-postgres` database on :17280.

---

## Verdict up top

**The card's mechanism is right. Its size estimate is wrong, and its framing — "a migration" — is
wrong for this installation.**

Three facts, measured, not assumed:

1. **There is nothing to migrate.** The live DB has **zero** rows in `AgentTuiSecrets`, **zero** rows
   in `ApiKeys`, and all **four** profiles (`raw-pwsh`, `claude`, `codex`, `grok`) are already
   `AuthenticationMode = 0` (`WrapperManaged`) with `NonSecretEnvironmentJson = {}` and
   `SecretEnvironmentNamesJson = []`. All four `Agents:Definitions` in `server/appsettings.json` have
   `"Env": {}`, so the importer has never had a secret to classify. Steps 1–3 of the card
   ("create a global ApiKey per configured AgentTuiSecret / rewrite the env / flip to WrapperManaged")
   apply to **zero rows here**. They still need to be *built* — for any other installation and for
   the appsettings import path — but they are not what makes this card expensive.

2. **The card is really a deletion card, and the deletion is not small.** `AgentTuiSecret` is threaded
   through ~1,100 lines of server code and is named by roughly 150 existing tests across seven test
   files, one PowerShell smoke script pinned by a test, and two published feature-spec documents that
   carry it as **Must** requirements FR-12/FR-13.

3. **One functional gap blocks the convergence outright, and it is not mentioned on the card.**
   `{{key:NAME}}` is resolved at **launch** only. The agent-TUI **validation** and **model-discovery**
   probes build their child environment from `AgentTuiProfileService.BuildAuthenticationEnvironment`
   (`server/Application/Services/AgentTuiProfileService.cs:1126`) and hand it straight to
   `IRunnerProcessProbe` via `BuildProcessRequest` (`:1266`). `AgentTuiProfileService` has no
   `ApiKeyEnvResolver` — its constructors (`:38`, `:58`) do not take one, and no grep hit exists in
   the file. So the moment a profile is converted, pressing **Validate** or **Refresh models**
   launches the runner with the literal string `{{key:anthropic-default}}` in its environment, with
   **no tripwire** (`ApiKeyPlaceholder.EnsureResolved` runs only in
   `AgentSessionService.BuildRuntimeLaunchSpec`) and **no probe-output redaction**
   (`RunnerProcessRequest.SecretValues`, `server/Application/Interfaces/IRunnerProcessProbe.cs:26`,
   is fed exclusively from the decrypted `AgentTuiSecret` plaintexts at `:1275`; a WrapperManaged
   profile passes an empty list, `:1132-1138`). Worse, the `authentication` validation stage would
   report **Passed** with the message *"Authentication is owned by the configured wrapper; managed
   keys were not accessed"* — which becomes a lie the instant the env carries a placeholder.

**So: build the missing capability first, then migrate, then delete.** Four slices, S0 through S3,
plus housekeeping. S0 is the real work; S3 is the bulk of the diff but is mechanical once S0 and S1
are green.

**The write-only contract is preserved throughout and is a hard constraint on every slice**: no slice
adds a value read-back endpoint, a reveal control, or any rendering of a stored value, for either
store, at any point. See §7 — this plan actually *tightens* it, because the ApiKey surface currently
has no equivalent of the agent-TUI cross-surface canary sweep.

---

## 1. The two stores as they actually are

### `AgentTuiSecret` (feature 011, shipped 2026-08-12)

| Piece | Location |
|---|---|
| Entity | `server/Domain/Entities/AgentTuiSecret.cs` (13 lines) |
| Nav property | `AgentTuiProfile.Secrets` (`server/Domain/Entities/AgentTuiProfile.cs:22`) |
| Schema | `AppDbContext.cs:647-666` — table `AgentTuiSecrets`, unique `IX_AgentTuiSecrets_ProfileId_Name`, cascade FK to the profile, `Name` max 200 |
| Protector | `IAgentTuiSecretProtector` (7 lines) + `DataProtectionAgentTuiSecretProtector` (`Infrastructure/Agents/Tui/DataProtectionAgentTuiSecretProtector.cs:25-91`), purpose chain `("Antiphon","AgentTui","ProfileSecret", profileId, environmentName)` |
| Write API | `PUT/DELETE /api/agent-tui/profiles/{profileId}/secrets/{environmentName}` (`Api/Endpoints/AgentTuiEndpoints.cs:88-160`) |
| Service | `AgentTuiProfileService.PutSecretAsync` (`:557`), `ClearSecretAsync` (`:673`), `RecordSecretAuditAsync` (`:1993`), `RequireDeclaredManagedSecret` (`:2269`), `FindDeclaredSecret` (`:2286`), `EnsureConfiguredSecretsRetained` (`:2298`), `ValidateSecretMutation` (`:2242`) |
| Launch use | `AgentTuiLaunchResolver.ResolveCoreAsync:264-303` — the `ManagedEnvironment` arm |
| Validation use | `BuildAuthenticationEnvironment:1126`, consumed by `ValidateAsync` (`:879`) and model discovery (`:773`) |
| Idempotency | `AgentTuiSecretIdempotencyCache` (97 lines), singleton, `Program.cs:393` |
| Metrics | `AgentTuiMetrics.RecordSecret` → `antiphon_agent_tui_secret_operations_total` |
| UI | `client/src/features/settings/AgentTuiProfileModal.tsx:274-330` + `client/src/api/agentTui.ts:240-286` |
| Docs | `docs/ai-agent-tui-configuration.md:78`; `docs/features/011-ai-agent-tui-configuration/{01-requirements,02a-secure-credential-storage,04-external-api}.md` (FR-12, FR-13, both **Must**) |

### `ApiKey` (CARD-0106, shipped 2026-08-20 as `a422675` + `bee9af7` + `f308c71`)

| Piece | Location |
|---|---|
| Entity | `server/Domain/Entities/ApiKey.cs` — `Name` max 128, nullable `ProjectId` (null = global) |
| Schema | `AppDbContext.cs:672-695` — two **filtered** unique indexes so a global and a project key may share a name |
| Protector | `IApiKeyProtector` + `Infrastructure/Security/DataProtectionApiKeyProtector.cs`, purpose chain `("Antiphon","ApiKey", keyId)` — keyed on the **row id**, so a rename cannot orphan ciphertext |
| API | `ApiKeyEndpoints.cs` — `GET /api/api-keys`, `GET /api/api-keys/global`, `PUT /api/api-keys/{name}`, `DELETE /api/api-keys/{id}`, `GET`/`PUT /api/projects/{projectId}/api-keys[/{name}]` |
| Resolution | `ApiKeyEnvResolver.ResolveAsync` over the fully-merged `Env`, project-then-global precedence |
| Tripwire | `ApiKeyPlaceholder.EnsureResolved`, called from `AgentSessionService.BuildRuntimeLaunchSpec` only |
| UI | `client/src/features/settings/ApiKeysSection.tsx` (global settings + `ProjectConfig` embed) |

**The card's central claim holds.** `AgentTuiLaunchResolver.ResolveCoreAsync:355-372` runs
`ApiKeyEnvResolver` over the merged environment *after* the `ManagedEnvironment` block, the agent-env
merge and the kind defaults. A profile whose `NonSecretEnvironmentJson` says
`{"ANTHROPIC_API_KEY":"{{key:anthropic-default}}"}` resolves today, on a `WrapperManaged` profile, with
no code change. That is already pinned by
`tests/Antiphon.Tests/ApiKeys/ApiKeyEnvResolverTests` →
`a_placeholder_in_a_profiles_non_secret_env_resolves`. **The launch path is genuinely done.**

**Name compatibility holds too.** Env names are `[A-Za-z_][A-Za-z0-9_]*` up to 200 chars
(`AgentEnvironmentVariableNames.IsValid`, `Application/Settings/AgentEnvironmentVariableNames.cs:113`);
API key names are `[A-Za-z0-9_.-]+` up to 128 (`ApiKeyNaming`). Every env name of 128 characters or
fewer is a legal key name. A 129–200 character env name is not — theoretical, but the migrator must
refuse rather than truncate (§3).

---

## 2. S0 — teach the validation and discovery probes about placeholders (**prerequisite**)

Without this, a converted profile's Validate and Refresh-models buttons are broken and silently
leak-prone. Three changes, all in `AgentTuiProfileService` and its probe request:

**(a) Resolve.** Inject `ApiKeyEnvResolver` as an **optional** constructor dependency, exactly the way
`AgentTuiLaunchResolver` takes it (`AgentTuiLaunchResolver.cs:148-179` — optional so a test harness
that does not wire it still runs, production always registers it in `Program.cs:405`). Run it over
`AuthenticationEnvironment.Environment` inside `BuildAuthenticationEnvironment` — which becomes async
— before `BuildProcessRequest` sees it. Scope: **global only**. A profile is installation-wide and has
no board and therefore no project; deriving one would repeat the mis-scoping mistake CARD-0106
explicitly rejected (`ApiKeyEnvResolver` class doc). A profile that references a project-scoped key
will fail validation naming the key and the scope searched — correct, and better than a silent
mis-scope.

**(b) Redact.** Every value `ApiKeyEnvResolver` substitutes goes into
`AuthenticationEnvironment.SecretValues`, so `RunnerProcessRequest.SecretValues` still receives the
plaintexts and `RunnerProcessProbe`'s `RedactOutput` / `SensitiveOutputDetected`
(`Infrastructure/Agents/Tui/RunnerProcessProbe.cs:563-569`) keeps working. This is the property the
existing canary test `Secret_routes_are_strict_write_only_and_keep_canary_out_of_reads_errors_logs_metrics_and_audit`
depends on, and it must survive the store swap. `ApiKeyEnvResolver.ResolveAsync` currently returns
only the resolved dictionary; add an overload (or an out-parameter record) returning the substituted
plaintexts, used **only** here. It must not be logged, returned by any endpoint, or persisted.

**(c) Stop lying in the `authentication` stage.** Today a `WrapperManaged` profile short-circuits to
`Ready = true` with *"managed keys were not accessed"* (`:1132-1138`). After (a), that arm must
instead: if the merged env contains no `{{key:` marker, keep the existing message; if it does, check
that **every referenced key exists** in the global scope and report `Passed` naming the key names, or
`Failed` naming the missing key. Existence only — the stage must never decrypt for display and never
surface a value. This is what gives an operator a working "Validate" after they delete a key out from
under a profile, instead of a launch that fails hours later.

**(d) A tripwire here too.** Call `ApiKeyPlaceholder.EnsureResolved`-equivalent enforcement on the
probe's `Environment` and `Arguments` immediately before `RequireProcessProbe().RunAsync`. The
tripwire exists precisely so a *forgotten resolution path* fails loudly on its first use rather than
exporting a literal token (`ApiKeyPlaceholder.cs:104-118`); the probe path is a second such path and
should be held to the same rule. `EnsureResolved` takes an `AgentLaunchSpec`, so either add a small
`EnsureResolved(IReadOnlyDictionary<string,string>, IReadOnlyList<string>, string subject)` overload
or call `EnsureAbsent` per entry — the former, to keep one implementation of the message.

**Tests for S0** (new, in `tests/Antiphon.Tests/AgentTui/`):
- a `WrapperManaged` profile whose non-secret env carries `{{key:X}}` runs discovery with the
  **resolved** value in the probe request, and the canary appears in **no** stage message, validation
  run, model list, `/metrics/agent-tui` response, or log;
- a probe whose stdout echoes the resolved value sets `SensitiveOutputDetected` and the run fails —
  proving redaction survived the store swap;
- a referenced key that does not exist fails the `authentication` stage **naming the key** and never
  starts the probe;
- a placeholder that survives to `RunAsync` (constructed by bypassing the resolver, mirroring
  `the_tripwire_refuses_an_unresolved_placeholder_in_an_env_value`) throws.

---

## 3. S1 — the migrator (build it even though it converts nothing here)

**It cannot be a SQL EF migration.** The two protectors use different purpose chains — `(profileId,
environmentName)` versus `(keyId)` — so a ciphertext cannot be moved by copying bytes. Conversion
must **decrypt with `IAgentTuiSecretProtector` and re-protect with `IApiKeyProtector`**, in process,
which means a startup migrator in the shape of `AgentTuiProfileImporter` (an idempotent, transactional
hosted step), not a file under `server/Migrations/`.

Per `ManagedEnvironment` profile, in one serializable transaction:

1. For each declared secret name `N` with a stored row: choose a key name. **Default: the env name
   itself** (`ANTHROPIC_API_KEY` → key `ANTHROPIC_API_KEY`), not the card's illustrative
   `anthropic-default` — a derived-from-nothing name is a name the operator did not choose and cannot
   predict. On collision with an existing global key of that name, **refuse the whole profile** and
   log naming the key; do not overwrite (that would silently replace a working key's value) and do not
   auto-suffix (that invents a name nobody will recognise).
2. Decrypt, create the global `ApiKey`, re-protect under its new row id.
3. Write a **new revision** with `AuthenticationMode = WrapperManaged`,
   `SecretEnvironmentNamesJson = []`, and `NonSecretEnvironmentJson` gaining `N = "{{key:N}}"`.
   A new revision, not an edit — revisions are immutable and monotonic
   (`Create_and_update_produce_immutable_monotonic_revisions`) and `AgentSession.TuiProfileRevisionId`
   FKs into them.
4. Delete the `AgentTuiSecret` rows for that profile.

**Refuse, don't half-convert**, on any of: a name longer than 128 chars (unspellable as a placeholder,
§1); a non-secret env value that already contains the `{{key:` marker (would be double-resolved or
report malformed); a decrypt failure (`CryptographicException` → leave everything, log, surface as a
startup warning, not a crash — the key ring may simply not be ready yet); a global-name collision.
Every refusal names the profile and the **environment name**, never a value.

**Idempotent and re-runnable**: a profile with no `AgentTuiSecrets` rows and
`SecretEnvironmentNamesJson = []` is already converted and is skipped. On this installation the
migrator will log "0 profiles converted" on every boot, forever, and that is the correct outcome.

**Tests for S1:**
- a seeded `ManagedEnvironment` profile with a canary secret converts: one global `ApiKey` exists,
  the new revision is `WrapperManaged` with `{{key:N}}` in its non-secret env, the `AgentTuiSecrets`
  row is gone, the prior revision is untouched, and **the canary is absent from the new ciphertext**;
- **resolution parity** — the `AgentLaunchSpec.Env` produced by `ResolveForAgentAsync` for that agent
  is byte-identical before and after conversion. This is the single most important test in the card:
  it is the one that says the new path *does the same thing*;
- running the migrator twice converts once (idempotence);
- each refusal arm above leaves the DB exactly as it found it, and the message carries the env name
  and not the value.

---

## 4. S2 — the appsettings import path

`AgentTuiProfileImporter` is the other producer of `ManagedEnvironment` profiles: it classifies each
definition's `Env` via `AgentEnvironmentVariableNames.Classify`
(`Application/Settings/AgentEnvironmentVariableNames.cs:21`), where a name is secret if it is listed in
`SecretEnvironmentNames` **or** matches the `LooksSecret` heuristic (`:119` — contains KEY / TOKEN /
SECRET / PASSWORD / CREDENTIAL), and then writes `AgentTuiSecret` rows and
`AuthenticationMode = ManagedEnvironment` (`AgentTuiProfileImporter.cs:234-256`).

After deletion that path has nowhere to put a classified secret. It must instead create a global
`ApiKey` and write `{{key:NAME}}` into the profile's non-secret env — the same conversion S1 performs,
sharing the same code. The classification itself stays: it is what keeps an accidentally-plaintext
`ANTHROPIC_API_KEY` in `appsettings.json` from being written into a revision row as cleartext.

**Note the real-world irrelevance and say so in the commit message**: all four definitions here have
`"Env": {}`, so this path has never fired. It is built because deleting the store without it would
turn a previously-supported `appsettings.json` shape into a startup crash.

**Tests for S2:** a definition with `Env: {"X_API_KEY": "canary"}` imports to a `WrapperManaged`
profile plus a global key, with the canary absent from `NonSecretEnvironmentJson`; the existing
`Import_rejects_unclassified_environment_without_exposing_its_value` still passes unchanged.

---

## 5. S3 — delete the store, its endpoints, and its UI

Only after S0–S2 are green and the parity tests in §6 exist.

**Delete outright:** `Domain/Entities/AgentTuiSecret.cs`; `Application/Interfaces/IAgentTuiSecretProtector.cs`;
`DataProtectionAgentTuiSecretProtector` **the class only** (`:25-91`) and its registration
(`:256`); `Application/Services/AgentTuiSecretIdempotencyCache.cs` + `Program.cs:393`; the two secret
routes and their helpers/records (`AgentTuiEndpoints.cs:88-160, 248-266, 284-311`);
`PutSecretAsync` / `ClearSecretAsync` / `RecordSecretAuditAsync` / `RequireDeclaredManagedSecret` /
`FindDeclaredSecret` / `EnsureConfiguredSecretsRetained` / `ValidateSecretMutation`;
the `ManagedEnvironment` arm of `ResolveCoreAsync:264-303`; the four secret DTOs and
`AgentTuiProfileDto.SecretEnvironment` (`AgentTuiDtos.cs:120`); the `AgentTuiMetrics` secret family; the `DbSet`, the entity
config (`AppDbContext.cs:647-666`) and the `AgentTuiProfile.Secrets` nav; the secret block of
`AgentTuiProfileModal.tsx:274-330` and the two hooks in `api/agentTui.ts:240-286`. Plus an EF
migration dropping `AgentTuiSecrets`.

**Do NOT delete:** `AgentTuiKeyProtectionReadiness` and `AgentTuiDataProtectionSetup` — both live in
the same 1,331-line file (`:93`, `:151`) and `DataProtectionApiKeyProtector` takes
`AgentTuiKeyProtectionReadiness` as a constructor dependency and `AgentTuiDataProtectionSetup.Configure`
is where `IApiKeyProtector` is registered (`:260`). The card's "two protectors sharing one key ring"
is accurate; only one protector goes, the ring machinery stays. **Rename nothing in this slice** — an
`AgentTui`-prefixed type serving API keys is mildly odd but renaming it is churn in a deletion diff,
and a follow-up can do it cleanly.

### The one schema decision that needs the operator's call

`AgentTuiAuthenticationMode.ManagedEnvironment = 1` and `AgentTuiProfileRevision.SecretEnvironmentNamesJson`
are **persisted history**. Revisions are immutable and referenced by `AgentSession` rows, so historical
rows recording `ManagedEnvironment` and a non-empty secret-name list will exist forever and cannot be
rewritten. There are also two persistence tests pinning them:
`Enums_keep_their_persisted_numeric_contracts` and `PostgreSql_rejects_undefined_profile_authentication_mode`
(a DB CHECK constraint over `(0,1)`).

**Recommendation: keep the enum member and both columns; make them unreachable, not absent.**
`AgentTuiRunnerCatalog` stops advertising `ManagedEnvironment` (`:54`, `:83`), profile validation
rejects it and rejects a non-empty `SecretEnvironmentNames` (the inverse of today's
`Wrapper_managed_profiles_reject_secret_declarations_and_puts`), and the launch resolver no longer
branches on it. No CHECK-constraint migration, no risk to historical rows, no test churn beyond the
inversion. The alternative — drop the member, narrow the constraint to `(0)`, drop
`SecretEnvironmentNamesJson` — is a second migration touching an immutable historical table for
cosmetic gain. Flagging it rather than deciding it unilaterally: **if the operator wants the enum
genuinely gone, that is an explicit S4, not part of S3.**

### Docs that must change with the code

`docs/ai-agent-tui-configuration.md:78`; `docs/features/011-ai-agent-tui-configuration/04-external-api.md:63-64`
(the two endpoint rows) and `:46,48,50,121`; `01-requirements.md:62-63,120` — **FR-12 and FR-13 are
`Must` requirements being retired**, which is a spec amendment with a stated reason ("superseded by
CARD-0106's API-key store; the write-only contract is preserved there"), not a silent line deletion;
`02a-secure-credential-storage.md:74-75`. Also `scripts/verify-agent-tui-profile.ps1` (its canary
scan at `:22,77` and the `secretEnvironmentNames = @()` field at `:162`) — pinned by
`AgentTuiSmokeScriptTests`, so it must change in lockstep. `scripts/bootstrap-check.ps1:516`'s key-ring
wording mentions "managed TUI secrets" and should say API keys.

---

## 6. What "migration coverage proves the new path" has to mean

The card gates deletion on coverage but does not say of what. Concretely, **all six of these must
exist and be green before S3 lands**:

1. **Resolution parity** (S1) — identical `AgentLaunchSpec.Env` before and after conversion for the
   same agent. Partially there already: `a_placeholder_in_a_profiles_non_secret_env_resolves` proves
   the new path *works*; nothing yet proves it produces the *same* result as the old one.
2. **Validation/discovery parity** (S0) — a converted profile passes `POST /validate` and
   `/models/refresh` with the runner receiving the real value, and the canary appears in no stage,
   run, model, metric, or log.
3. **Redaction parity** (S0) — a probe that echoes the resolved value still trips
   `SensitiveOutputDetected`. Today that property is carried entirely by `AgentTuiSecret` plaintexts;
   it must be carried by resolved key values instead, or deletion is a security regression.
4. **Migrator correctness and refusals** (S1) — including that every refusal is atomic.
5. **A cross-surface write-only canary sweep for the ApiKey store.** This does not exist today.
   `AgentTuiApiTests.Secret_routes_are_strict_write_only_and_keep_canary_out_of_reads_errors_logs_metrics_and_audit`
   (`:101-191`) sweeps a canary across seven read routes, the metrics endpoint, the audit rows and the
   stored ciphertext in one test. The ApiKey suite has the pieces — `listing_returns_metadata_only_and_never_the_ciphertext`,
   `the_stored_ciphertext_is_never_the_value_and_the_dto_never_carries_one`,
   `an_oversize_value_is_422_and_the_response_does_not_echo_it`, `a_protection_failure_reports_the_key_name_and_never_the_value` —
   but no single sweep, and **no test that a value supplied in the query string is rejected**
   (`AgentTuiEndpoints.ValidateSecretPutRequest:248-259` rejects `?value=` explicitly; `PUT /api/api-keys/{name}`
   just ignores it, which is safe but unpinned). Write the ApiKey equivalent **in S0 or S1, before the
   agent-TUI one is deleted**, so the contract is never uncovered even momentarily.
6. **Client parity** — `ApiKeysSection.test.tsx` already covers write-only-then-clear at both scopes
   (2 tests). Add one asserting the modal no longer renders a secret editor after S3, so the removal
   is pinned rather than merely done.

### Four contract properties that die with `AgentTuiSecret` — each needs an explicit decision

These are **losses**, not equivalences, and the plan should not pretend otherwise:

| Property | `AgentTuiSecret` | `ApiKey` | Recommendation |
|---|---|---|---|
| **Audit rows** | `RecordSecretAuditAsync:1993` writes an `AuditEventType.ToolInvocation` record per set/clear, inside the same transaction, asserted by the canary test | one `ILogger` line, no row | **Port it.** It is ~20 lines, `AuditService` is already injectable, and CARD-0106 §9's "no audit trail — nothing else has it" was written without noticing that the store it replaces *does*. Losing it silently is the worst option. |
| **Prometheus metrics** | `antiphon_agent_tui_secret_operations_total{operation,outcome}` | none | **Drop, and say so.** Single-operator installation; the audit row carries the same information with better fidelity. |
| **Optimistic concurrency** (`expectedRevision`, 409 `profile_revision_conflict`) | yes | last-write-wins | **Drop.** It existed because a secret was coupled to a profile revision; a global key has no revision to be stale against. |
| **`Idempotency-Key` replay** (`AgentTuiSecretIdempotencyCache`) | yes | none | **Drop.** Its purpose was to make a retried secret write not double-audit; with a plain upsert a replay is a no-op by construction. |
| **JSON extra-property rejection** (`ValidateExactProperties`) | yes | none | **Port** (~5 lines) — it is what stops a client silently sending `{"value":...,"correlationId":...}` and having a field ignored. |

---

## 7. The write-only contract — explicit, and unchanged by every slice

**No slice in this plan adds, and no future slice of this card may add:**

- any endpoint that returns a stored value, for either store, at any scope;
- any UI control that reveals, echoes back, pre-fills, or renders a stored value — `ApiKeysSection.tsx`
  and `AgentTuiProfileModal.tsx` both clear the input after a successful save and neither has a reveal
  toggle; that stays;
- any log line, exception message, incident summary, task failure reason, stage message, validation-run
  record, metric label, or audit summary carrying a value. Every message in `ApiKeyService`,
  `ApiKeyEnvResolver` and `ApiKeyPlaceholder` names the key, the variable and the scope and nothing
  else, by construction and by test; S0's new `authentication` stage message and the migrator's refusal
  messages must be held to the identical rule.

The **one** new place a plaintext moves in this plan is S0(b): the resolved value enters
`RunnerProcessRequest.SecretValues` so the probe can redact it out of the child's output. That is
strictly a *suppression* channel — it exists to keep values out of stored evidence, it is the same
channel `AgentTuiSecret` plaintexts use today, and it must never be returned, logged, or persisted.

`ApiKeyService.PutAsync` is the only write, `ApiKeyEnvResolver` is the only read, and after this card
those two remain the only two. If a slice appears to need a third, that is the signal to stop and
re-plan, not to add one.

---

## 8. Slices

- **S0 — probe-path resolution, redaction, honest auth stage, tripwire.** Server only. Ships
  independently and is useful on its own (it fixes a real hole today: any profile whose non-secret env
  already carries a placeholder validates wrongly). Also lands the ApiKey cross-surface canary sweep
  (§6.5) so the write-only contract is covered on the new store before anything is removed from the old.
- **S1 — the migrator** + resolution-parity test. Server only. Converts zero rows here; that is the
  expected and correct outcome, and the commit message should say so.
- **S2 — importer path** (`appsettings` classified secrets become keys + references).
- **S3 — deletion**: code, endpoints, UI, EF migration, feature-spec amendments, smoke script,
  bootstrap-check wording. The largest diff, the smallest thinking, and only safe once §6's six
  coverage items are green.
- **S4 (optional, operator's call) — retire `ManagedEnvironment` from the enum and drop
  `SecretEnvironmentNamesJson`.** Needs a CHECK-constraint migration over an immutable historical
  table. Recommended as a separate card, not folded into S3.

**Suggested dispatch:** S0 and S1 to one delegate (server, coherent, and S1's parity test needs S0's
validation path to assert against). S2+S3 to a second delegate once S0+S1 are on master — S3 is a wide
mechanical diff and will conflict with almost anything else touching `AgentTuiProfileService`. Do not
run them concurrently in a shared worktree.

**Revised size estimate:** the card says "should be small". S0 is ~200 lines of server code plus four
tests; S1 is ~250 lines plus six tests; S2 is ~80 lines plus two tests; S3 removes ~1,100 lines and
touches ~150 existing tests and five documents. That is a **medium-to-large card, not a small one**,
and the reason is not the migration — it is that the store being deleted is load-bearing in the
validation subsystem the CARD-0106 plan explicitly declined to touch.

---

## 9. Deliberately not in scope

- **A value read-back endpoint or UI reveal, for either store, at any point.** Not a scope decision —
  a constraint (§7).
- **Rotation, expiry, or per-key permissions.** No auth subsystem exists to hang them on; CARD-0106 §9
  already ruled these out and nothing here changes that.
- **Renaming `AgentTuiKeyProtectionReadiness` / `AgentTuiDataProtectionSetup`** now that they serve API
  keys. Cosmetic; churn inside a deletion diff. Separate follow-up.
- **Dropping `AgentTuiAuthenticationMode.ManagedEnvironment` or `SecretEnvironmentNamesJson`** — S4,
  operator's call (§5).
- **Project-scoped keys for profiles.** A profile is installation-wide; global-only resolution in the
  probe path is the deliberate answer (§2a).
- **Re-encrypting existing `ApiKey` rows or changing the purpose chain.** Untouched.
- **Migrating anything on this installation.** There is nothing to migrate (§Verdict); the migrator is
  built for correctness of the deletion, not for this database.

---

## 10. Card housekeeping

- **Correct CARD-0114 in place** (cards are correctable in place since CARD-0019) so the record does
  not keep asserting a premise this plan disproved: the "should be small" line and the four-step body
  should gain the measured facts — zero secrets, zero keys, four already-`WrapperManaged` profiles,
  all definitions with empty `Env` — and the S0 prerequisite. Keep the original text and append the
  correction; do not overwrite the operator's framing.
- `card.ps1 comment` on CARD-0114 per slice, naming the slice and the commit.
- Cross-comment **CARD-0106**: its §7 convergence path was accurate about the mechanism and missed the
  probe path; its §9 "no audit trail — nothing else has it" was wrong about the store it supersedes
  (§6). Both are worth recording where the next reader of that plan will find them.
- Move CARD-0114 to done only after S3; if S4 is wanted, file it as its own card at that point.
