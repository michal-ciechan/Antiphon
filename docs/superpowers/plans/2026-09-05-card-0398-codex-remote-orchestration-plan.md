# CARD-0398 — Safe Codex/ChatGPT remote orchestration

**Date:** 2026-09-05 (Plan pass, task f1f3b5a8 — design only; no production code changed)
**Card:** CARD-0398 — Codex remote orchestration: give ChatGPT/Codex a safe Antiphon delegation capability without exposing task tokens
**Supersedes:** nothing. Neighbours: CARD-0020 S1 (token-less callers inherit nothing), CARD-0032 (no setup flow widens `AllowedRoots`), CARD-0099 (Codex is a worker kind; orchestrator stays ClaudeCode), CARD-0212 (Codex remote control refused).
**Complexity:** hard (security boundary, new principal, quota-degraded UX). Test-design is a separate stage.

**Sources (verified this pass):** CARD-0398; `server/Application/Services/AgentTaskService.cs` (`AuthenticateAsync` 109–132, `Caller.MayDelegate` 102, `CreateAsync` workspace 317–321, `ResolveAgentKind` 2094–2143, `NewToken`/`HashToken` 2625–2632, `RawTokens` 1224–1228, `ReplyTo` 796); `DelegationWorkspaceResolver.cs`; `AgentTaskEndpoints.cs` `ResolveCallerAsync` 239–247; `DelegationSettings.cs` 287–292 and `DelegationSettingsValidator` 1038–1105; `server/appsettings.json` `Delegation` 211–244; `Program.cs` 139–141, 309; `AgentSessionLaunchComposer.cs` 43–48; `AgentTaskDispatcher.BuildEnv` 3624–3642; `RemoteControlPolicy.cs`; `AgentTuiRunnerCatalog.cs` Codex `remoteControl` Unsupported; `AgentLaunchEnv.ValidateOverride`; `AgentTaskCreatedDto`; `SessionEndpoints.cs` 91–97; `docs/agent-kinds.md` §§1, 6, 8; `scripts/delegate.ps1` 269–271, 506–527; `tests/Antiphon.Tests/Application/AgentTaskCallerResolutionTests.cs`; CARD-0032 plan §5; CARD-0106 write-only key contract.

## Recommendation (one mechanism)

**Primary: a named Delegation Capability principal (card option 1).** Issue it to ChatGPT/Codex as an identity that can call `delegate.ps1`, scoped to an explicit list of repository roots (and optionally one board/project), with hashed-at-rest credentials, operator issue/rotate/revoke, and a client store that is not the process environment and not the git workspace.

Do **not** make ChatGPT “attach” to an Antiphon-launched Codex TUI (option 2 cannot do that: Codex remote control is refused). Do **not** put `C:\src\Antiphon` on `Delegation:AllowedRoots` (option 3 is a silent grant to every token-less caller, which is the pressure already observed today).

Option 2 is the **complementary quota-recovery path**, already mostly true in code: a named AlwaysOn Codex agent receives a session-scoped `ANTIPHON_TASK_TOKEN` and `Caller.MayDelegate` is true because `Task is null`. ChatGPT talks to that session through the existing public session-message and transcript APIs. It is not the ChatGPT-as-orchestrator UX the card asked for, and it does not require lifting the Orchestrator-kind clamp.

Option 3 stays the local-only escape hatch for UI/`delegate.ps1` in a plain shell. Empty remains the safe default. Editing it still requires an AppHost restart. No endpoint, preset, or this card’s issue flow writes it.

### Supported ChatGPT UX (the sentence to ship)

> Run `delegate.ps1 -Capability <name> …` from the approved checkout (add `-Kind Codex` while Claude is held). Do not read capability files, do not dump env looking for tokens, do not edit `Delegation:AllowedRoots`. Reports land on the bound card; poll `delegate.ps1 -Status <id>` or the card thread.

Until that ships, the no-new-code fallback is: start a named Codex AlwaysOn with the `orchestrator` + `board-api` bundles, then `POST /api/sessions/{id}/messages` and read `GET /api/sessions/{id}/transcript`. Still do not widen `AllowedRoots`.

---

## Ground truth (card assumes vs code does)

| Card assumes | What the code does | Gap |
|---|---|---|
| External ChatGPT/Codex can inspect the board but has no `ANTIPHON_TASK_TOKEN` | The HTTP API is unauthenticated. `GET /api/cards`, `GET /api/agent-tasks/{id}`, `GET /api/sessions/{id}/transcript` work with no header. `card.ps1` / `delegate.ps1` send `X-Antiphon-Task-Token` only when the env var is set (`delegate.ps1:269–271`). | Inspection already works. Create is the hole. |
| Direct `delegate.ps1` is rejected unless `Delegation:AllowedRoots` lists the directory | `POST /api/agent-tasks` does **not** require a token. `ResolveCallerAsync` (`AgentTaskEndpoints.cs:239–247`) returns `Caller(null, null, "")` when the header is missing. `MayDelegate` is true (`Task is null`, line 102). `DelegationWorkspaceResolver` then requires `workingDirectory` under `AllowedRoots` because there is no parent directory to inherit (`NothingToInherit`, lines 32–36). Tracked `server/appsettings.json` **omits** `AllowedRoots`, so the list is empty (`DelegationSettings.cs:292`). Token-less create into `C:\src\Antiphon` is 422. `delegate.ps1` always sends `workingDirectory` (cwd / checkout root, lines 526–527), so the 422 is exactly this path. | Accurate. The 422 text currently *recommends* adding the path to `AllowedRoots` — that is the pressure toward the wrong fix (CARD-0020 already called this out). |
| Widening `AllowedRoots` is a blunt silent trust grant | Empty = only the caller’s own tree. A non-empty list authorises **every** token-less caller (UI Delegate button, any local shell, any agent) into that tree. `IsWithinRoot` is prefix-safe (`C:\src\antiphon-evil` is not inside `C:\src\antiphon`). No API or UI writes the list (CARD-0032: “no button, endpoint, or preset changes `AllowedRoots`”). `DelegationSettingsValidator` does not inspect it. | Accurate, and already acted on: an uncommitted `AllowedRoots` += `C:\src\Antiphon` appeared in the main checkout today and was reverted. That edit would have taken effect on the **next** `restart-apphost.ps1` from the main checkout. |
| `AllowedRoots` reloads with the file | `AddOptions<DelegationSettings>().Bind(…)` (`Program.cs:139–141`). Consumers take `IOptions<T>`, not `IOptionsMonitor<T>`. `AgentTaskService` is scoped (`Program.cs:309`) but captures `_settings = settings.Value` in the ctor (line 79). `IOptions<T>` is a process-lifetime cache. JSON `reloadOnChange` does **not** refresh this object. Environment / user-secrets bind at startup only. In-memory mutation of the `List<string>` (what tests do) is visible because it is the same object; a file edit is not. | File edit ⇒ **AppHost restart**. Worktree edits do not affect the live server; `restart-apphost.ps1` builds from the main checkout (exit 3 from a worktree unless `-AllowWorktree`). |
| Task tokens must not leak into chats / transcripts / logs / UI | Minted as 32 random bytes hex (`NewToken`, 2625–2628), stored SHA-256 only (`TokenHash` / `AgentSession.DelegationTokenHash`). `AgentTaskCreatedDto` has no token field. `RawTokens` is an in-memory dictionary from create until `BuildEnv` injects and `TryRemove`s it (`AgentTaskDispatcher.cs:3637–3640`). Launch-env overrides cannot set `ANTIPHON_*` (`AgentLaunchEnv.ValidateOverride`). ApiKey GET is write-only (CARD-0106). `AuditMiddleware` does not log bodies. Standing-agent raw token lives in **process env** of the launched TUI (`AgentSessionLaunchComposer.cs:43–48`). | The invariant holds for DTOs/logs/UI. It does **not** hold for Antiphon-launched process env — Claude orchestrators already have the bearer in env. Putting the same var in ChatGPT’s environment would create a *new* chat-shaped leak (ChatGPT will dump env). |
| Codex can be an Antiphon orchestrator / ChatGPT can attach via remote control | **Orchestrator task kind** is ClaudeCode only: explicit `Grok`/`Codex` is 422, policy-derived clamps (`ResolveAgentKind` 2125–2141; `ComplexityRoutingService` 498 skips non-ClaudeCode for Orchestrator tasks). **Named agent session tokens** are kind-blind: `AuthenticateAsync` session arm (124–131) returns `Task is null` ⇒ `MayDelegate` true, cwd = `session.Cwd`. Composer injects `ANTIPHON_TASK_TOKEN` for every named launch, including Codex. **Remote control** is catalog-Unsupported for Codex (`AgentTuiRunnerCatalog.cs:130`); `RemoteControlPolicy.Require` throws `409 remote_control_refused` at create/PATCH/start/card-spawn (CARD-0212). `SendRemoteControlCommandsAsync` types nothing. Codex has no claude.ai session entry. | Option 2 “launch a Codex session through Antiphon so it has identity” is already true for **named** agents. Option 2 “ChatGPT attaches to that TUI” is false and must stay false. Option 2 “`delegate.ps1 -Orchestrator -Kind Codex`” is false and this card does **not** lift it. |
| ChatGPT has no other write path into a live session | `POST /api/sessions/{id}/messages` (`SessionEndpoints.cs:91–97`) is unauthenticated, as is transcript GET. ChatGPT can already enqueue work into an Antiphon-owned Codex session and read what it said. | This is the complementary handoff, not a capability. Pre-existing localhost trust; this card does not lock down the whole API. |
| Claude is quota-blocked, so Codex must keep dispatching | `CreateAsync` / `StartAsync` refuse `409 model_disabled` / `subscription_quota_low` rather than silently rerouting (CARD-0136, CARD-0309). Role policy ships unset `Kind` ⇒ ClaudeCode. `delegate.ps1` omits `agentKind` unless `-Kind` is passed (lines 522–524). Complexity routing skips held Claude aliases for **Worker** tasks and can land on Codex if the chain says so; Orchestrator tasks still skip non-ClaudeCode. | A capability caller that does not pass `-Kind Codex` (or a pin/chain that includes Codex) will 409 while Claude is held. Silent reroute is forbidden. |

---

## Decisions

**D1 — Primary mechanism is a Delegation Capability principal, authenticated on the existing `X-Antiphon-Task-Token` header.**
A third `AuthenticateAsync` lookup after task hash and session hash. Same header, same SHA-256, new table. ChatGPT keeps using `delegate.ps1`; the script loads the capability and sends the header. Do not invent a second header (two ways to be a caller is two ways to get the 422 text wrong). Do not reuse a live session’s token (that is exactly the leak the card forbids).

**D2 — Capability roots are per-principal and independent of `Delegation:AllowedRoots`.**
The global list stays the token-less/UI ceiling and stays empty in tracked config. A capability with default root `C:\src\Antiphon` authorises *that principal* under that tree via the existing “parent directory is always allowed” arm (`DelegationWorkspaceResolver.cs:68–72`), by setting `Caller.WorkingDirectory` to the capability’s default root and `Caller.ExtraAllowedRoots` to the rest of its list. Token-less shells and the UI stay 422. This is the whole point versus option 3.

**D3 — The raw token is never a ChatGPT-visible value.**
Server mints with `NewToken()`, stores the hash, returns the raw token only on `POST` issue/rotate (the CLI is the only caller of that response). `scripts/capability.ps1` writes a DPAPI-`CurrentUser` blob under `%LOCALAPPDATA%\Antiphon\capabilities\<name>` (override `ANTIPHON_CAPABILITY_STORE` for tests) and prints name + store path + roots — **not** the token. GET list/detail DTOs have no token field (CARD-0106 write-only contract). `delegate.ps1 -Capability <name>` (or `$env:ANTIPHON_CAPABILITY=<name>`, the **name**, never the secret) Unprotects into a local variable, sets the header, never `Write-Output`s it, never assigns `ANTIPHON_TASK_TOKEN` (so `gci env:` in ChatGPT’s shell does not show a bearer). Errors must not echo the secret. If both a capability name and `ANTIPHON_TASK_TOKEN` are set, refuse (two identities). Auto-load of “the only file in the store” is forbidden — that would turn every local shell into the principal, i.e. option 3 in disguise.

Honest limit (state it, do not pretend): a model with shell as the same Windows user *can* Unprotect the blob if it knows the path. That is the same class as a Claude orchestrator echoing `$env:ANTIPHON_TASK_TOKEN`, which already exists. The design removes the *easy* leak paths (workspace files, env dump, GET, logs, UI, issue stdout). MCP as a sidecar that never puts the secret in ChatGPT’s process is a later hardening, not v1 (D11).

**D4 — Reply routing in v1 is the bound card, not a fake session.**
`ReplyTo = None` when `caller.SessionId` is null (already line 796). Do not mint a dummy `AgentSession` so reports try to type into a pty ChatGPT is not attached to. ChatGPT already reads the board and `GET /api/agent-tasks/{id}`. Bind `-Card` as today. A capability inbox is out of v1.

**D5 — Do not lift “orchestrator = ClaudeCode only” and do not enable Codex remote control.**
ChatGPT *is* the orchestrator; it dispatches Worker / stage-role tasks. `delegate.ps1 -Orchestrator -Kind Codex` stays 422. Catalog `remoteControl` for Codex stays Unsupported; `409 remote_control_refused` stays. Named Codex AlwaysOn + session token remains the complementary path (D6) without changing those clamps.

**D6 — Complementary path (option 2): named Codex AlwaysOn, not Orchestrator-kind tasks.**
Operator creates/starts a Codex named agent, attaches `orchestrator` + `board-api` (developer_instructions channel already exists). Composer already injects a session token. ChatGPT hands off via `POST /api/sessions/{id}/messages` and reads the transcript. Unmeasured: whether Codex *follows* the orchestrator bundle in practice (no PreToolUse hook). That is an operational bet, not a code change in this card. One test pins the code fact: a Codex `AgentSession.DelegationTokenHash` authenticates as `MayDelegate` and inherits `session.Cwd`.

**D7 — Quota-blocked Claude: fail-fast, prefer Codex explicitly, no silent remap.**
Capability docs and `delegate.ps1` help text tell ChatGPT to pass `-Kind Codex` (or `-Complexity` whose chain includes Codex) while Claude aliases are held. A create that would launch Claude 409s `model_disabled` / `subscription_quota_low` as today. `ignoreModelDisabled` still queues rather than launching (CARD-0309). This card does not add a “if Claude held then Codex” rewrite in `ResolveAgentKind`.

**D8 — Issue/revoke is operator CLI + HTTP; no client UI in v1.**
`scripts/capability.ps1` in the `card.ps1` mould (ASCII-only). Endpoints under `/api/delegation-capabilities`. Localhost trust is the existing API model (CARD-0106 already answered “no permission subsystem”). The capability’s value is **scoping the agent**, not replacing loopback trust. If the API is later exposed beyond loopback, capabilities become network-reachable the same way task tokens would; document that, do not invent IP ACLs here.

**D9 — Capability constraints at issue time.**
Name: `[A-Za-z0-9_.-]+`, max 64, unique among non-revoked. Roots: 1–8 existing directories, not a filesystem root (`C:\`, `/`), compared with `IsWithinRoot` so a later create cannot escape. Optional `boardId` / `projectId`: when set, `CreateAsync` refuses a card binding on another board (422 naming the capability) and `DeriveCallerProjectAsync` uses the capability’s project so API-key scope is not “none”. Revoke sets `RevokedAt`; authenticate 403 “capability revoked”. Rotate mints a new hash, overwrites the DPAPI file; the old bearer 403s. LastUsedAt updates on successful authenticate (no per-call audit row — that is noise).

**D10 — Audit without the secret.**
New `AgentTaskEventType` is the wrong table (no parent task at issue). New `DelegationCapabilityEvent` rows: `Issued`, `Rotated`, `Revoked`, detail = name + root list + board/project ids, **never** the token or hash (a hash in a log is still a credential-shaped value). Attention: one Info on issue/rotate, one Warning on revoke. `RecordRejectionAsync` stays parent-task-only; a capability create that fails AllowedRoots-equivalent checks is a 422 with the capability named, not “add it to AllowedRoots”.

**D11 — No MCP server, no settings UI for `AllowedRoots`, no `IOptionsMonitor` reload as a “fix”.**
MCP is the stronger “secret never in the model process” end-state; v1 is `-Capability`. CARD-0032 already rejected an `AllowedRoots` text box. Reloading the global list without restart would make an uncommitted edit take effect *without* the restart ritual that currently makes it at least noticeable.

**D12 — Tracked `appsettings.json` must stay free of `AllowedRoots`.**
A regression test reads the file (not the bound object) and asserts the `Delegation` object has no `AllowedRoots` property. That is what stops the reverted edit from landing later as “config”.

---

## Rejected alternatives

| Alternative | Why not |
|---|---|
| Add `C:\src\Antiphon` to `Delegation:AllowedRoots` | Authorises every token-less caller, not ChatGPT. The 422 text already pushes agents to do this; one uncommitted edit already appeared. CARD-0020 / CARD-0032 forbade it as an ergonomics fix. |
| Put `ANTIPHON_TASK_TOKEN` in ChatGPT’s user environment | ChatGPT dumps env. New chat-shaped leak. Standing Claude already has this problem *inside* an Antiphon pty; do not extend it to an external chat product. |
| Steal / copy another session’s token | The card’s forbidden workaround. Session tokens also inherit *that* session’s cwd and reply-route into *that* pty. |
| Make Codex remote-control Supported so ChatGPT attaches | Catalog is a measured fact: there is no Claude-style RC for Codex. Pretending would 409 today and type `/remote-control` into a TUI that does not arm (CARD-0212’s exact defect). |
| Lift Orchestrator-kind = ClaudeCode so `delegate.ps1 -Orchestrator -Kind Codex` works | The orchestrator contract (PreToolUse deny hook, check-interpreter interplay) has never run on Codex (CARD-0099). This card does not take that measurement. Named Codex + session token is the existing kind-blind delegate door. |
| ChatGPT-as-operator only (session messages, no capability) | Already possible, and it is the **fallback**, but it makes ChatGPT a messenger into a pty it cannot see, duplicates board context, and dies when that session dies. The card asked for a delegation capability. |
| Auto-load the DPAPI file whenever `delegate.ps1` runs | Every local shell becomes the principal. Option 3 with extra steps. |
| Show-once token in the UI / JSON that ChatGPT is told to paste | The paste *is* the leak (chats, transcripts, screenshots). The CLI store exists so ChatGPT never handles the secret. |
| `IOptionsMonitor` so `AllowedRoots` hot-reloads | Makes a mistaken file edit live without restart. Consumers cache `.Value` anyway. |
| Server-side “if Claude held, rewrite kind to Codex” | Silent reroute, forbidden by CARD-0136 / AGENTS.md. 409 naming the hold is the contract. |
| New header `X-Antiphon-Capability` | Two caller identities, two miss paths in every script. One header, three hash tables. |

---

## Until this ships (operator procedure, no code)

Claude fable/opus/sonnet/haiku are on a usage hold at dispatch time. Do **not** edit `Delegation:AllowedRoots`.

1. Create or reuse a named AlwaysOn **Codex** agent, cwd = `C:\src\Antiphon`, attach bundles `orchestrator` and `board-api`, start it (no `remoteControl`). Confirm `GET /api/agents/{id}` shows a live session.
2. Tell ChatGPT the session id. ChatGPT inspects the board as today, then `POST /api/sessions/{id}/messages` with `mode: WhenIdle` (or `card.ps1` moves). It does not call `delegate.ps1`.
3. That Codex process already has `ANTIPHON_TASK_TOKEN` and can `delegate.ps1` workers, including `-Kind Codex`.
4. If that session is not running, ChatGPT is stuck until an operator starts it — which is why D1 still ships.

---

## Slices

### S1 — Principal, authenticate, scoped create

**Files:** new `server/Domain/Entities/DelegationCapability.cs` + `DelegationCapabilityEvent.cs`; migration; `AgentTaskService.Caller` grows `ExtraAllowedRoots` and `CapabilityId`/`ProjectId` (defaults keep every existing constructor site compiling); `AuthenticateAsync` third arm (revoked ⇒ 403); `CreateAsync` concatenates `caller.ExtraAllowedRoots` onto the resolver’s root list **before** `ResolveAsync`, uses capability project in `DeriveCallerProjectAsync`, enforces optional board constraint; 422 text for a capability miss names the capability and does **not** say “add it to AllowedRoots”; new `DelegationCapabilityService` issue/rotate/revoke/list; `server/Api/Endpoints/DelegationCapabilityEndpoints.cs` (`POST /`, `POST /{id}/rotate`, `POST /{id}/revoke`, `GET /`, `GET /{id}`) — issue/rotate response includes `token` **and** `storePath`; GET omits `token`; `Program.cs` DI; `docs/antiphon-api.md` route block.

Issue body: `{ name, roots: string[], boardId?, projectId? }`. Validate D9. Mint via `AgentTaskService.NewToken()`. Persist hash, not raw. Write DPAPI store (S2 can own the file format if S1 returns the token and the CLI writes; prefer one writer — **the CLI writes the store**, the server never touches `LocalAppData`, so a test server cannot clobber an operator file).

**Tests:** `tests/Antiphon.Tests/Application/DelegationCapabilityTests.cs` (new, shared-Postgres scoped to rows it made):

- Issue stores hash ≠ raw; GET list/detail have no token property.
- Authenticate with raw ⇒ `MayDelegate`, `WorkingDirectory` = first root, `SessionId` null.
- Token-less create into that root still 422 (`AgentTaskCallerResolutionTests` stay green).
- Capability create into first root succeeds without any `Delegation:AllowedRoots` entry; `NoReplyRouting` true; `ReplyTo.None`.
- Capability create outside its roots 422; message contains capability name; message does **not** contain “Add it to Delegation:AllowedRoots”.
- Second root in the list is accepted; a path that is only a prefix-neighbour is not (`IsWithinRoot`).
- Board constraint: `-Card` on another board 422.
- Revoked bearer 403; rotated old bearer 403, new bearer works.
- Worker token minted for the child cannot itself create (existing `MayDelegate` false).
- Codex session-hash arm unchanged: Codex `AgentSession.DelegationTokenHash` still `MayDelegate` (D6 pin).
- `ResolveAgentKind` Orchestrator+Codex still 422 (D5 pin, existing `AgentTaskAgentKindTests`).
- Tracked `server/appsettings.json` `Delegation` has no `AllowedRoots` key (D12).

### S2 — Client store + `delegate.ps1` + `capability.ps1`

**Files:** `scripts/capability.ps1` (ASCII-only): `issue`, `rotate`, `revoke`, `list`. Issue/rotate call the API, write DPAPI via `[System.Security.Cryptography.ProtectedData]::Protect(..., CurrentUser)`, never print the token (assert in tests). `scripts/delegate.ps1`: `-Capability`, `$env:ANTIPHON_CAPABILITY`, conflict with `ANTIPHON_TASK_TOKEN`, Unprotect, header only. Missing file: error “capability '<name>' is not installed under <store>; ask the operator to run capability.ps1 issue” — not the AllowedRoots sentence. `tests/Antiphon.Tests/Scripts/CapabilityScriptTests.cs` + extend `DelegateScriptKindTests` / a focused `DelegateScriptCapabilityTests.cs` using the existing `DelegateScriptRunner` (empty `ANTIPHON_TASK_TOKEN` in the child, isolated store path).

DPAPI blob path: `$store\<sanitized-name>.dpapi`. Store default `%LOCALAPPDATA%\Antiphon\capabilities`. Tests set `ANTIPHON_CAPABILITY_STORE` to a temp dir.

**Tests:** issue does not write the hex token to stdout; file exists and Unprotect round-trips; delegate with `-Capability` sends the header and creates a task; delegate with only env name works; both env token and `-Capability` exits non-zero; `Write-Error` paths never contain the 64-hex token.

### S3 — Audit + attention + docs

**Files:** persist `DelegationCapabilityEvent`; Attention rows on issue/rotate/revoke (no secret in `Detail`); 422 copy in `DelegationWorkspaceResolver` stays for true token-less callers; capability arm uses its own sentence (S1). Docs: `docs/ops-http.md` (new row: issue/list/revoke; ChatGPT UX paragraph), `docs/agent-kinds.md` §6/§8 (ChatGPT is not RC; named Codex session token can delegate; Orchestrator-kind still ClaudeCode), `docs/orchestration-loop.md` short “external orchestrator” note pointing at ops-http, `docs/antiphon-api.md` already in S1, AGENTS.md one bullet under Cards/tracker routing to ops-http (do not put store paths or token advice in AGENTS.md — every worker loads it). Bundle `board-api.md`: `-Capability` and “while Claude is held, `-Kind Codex`”.

**Tests:** event detail does not contain the raw token or the hash; attention exists; docs files mentioned above contain the UX sentence (string pin, not a prose test of the whole doc).

### S4 — Quota-degraded dispatch (docs + help text, no remap)

**Files:** `scripts/delegate.ps1` comment/help: when create 409s `model_disabled` / `subscription_quota_low` on Claude, retry with `-Kind Codex` rather than editing `AllowedRoots`. `docs/ops-http.md` “Claude held” subsection: capability + `-Kind Codex`; complementary named Codex session (D6); still no `AllowedRoots`. No `ResolveAgentKind` change.

**Tests:** existing quota/hold tests stay; a capability create with explicit `AgentKind.Codex` succeeds in the S1 harness without a Claude alias; a capability create with default kind still hits the hold gate when a Claude `*` hold is active (reuse `ModelAvailability` test helpers). That last test is the degradation contract: 409, not Codex-by-surprise.

---

## Token-leakage checklist (must stay true after Code)

| Surface | Required behaviour |
|---|---|
| GET capability DTO / list | no token field |
| Issue/rotate HTTP response | token present; only `capability.ps1` consumes it; script stdout has no 64-hex bearer |
| DB | hash only |
| Logs / `DelegationCapabilityEvent.Detail` / Attention `Detail` | name, roots, ids — never raw, never hash |
| UI (none in v1) | if a later UI appears: metadata + revoke, no copy-once dialog that persists |
| `delegate.ps1` | header only; no env assignment of the bearer; no verbose dump |
| Workspace / git | no capability files; store is under LocalAppData |
| Transcripts Antiphon records | we never type the bearer into a session; do not add it to bundles or `developer_instructions` |
| `AgentLaunchEnv` | still refuses `ANTIPHON_*` overrides |
| `RawTokens` | still process-memory, still `TryRemove` at inject; capabilities do not use `RawTokens` (no child env to fill at issue) |

---

## Out of scope

- Codex as `AgentTaskKind.Orchestrator`.
- Codex remote control.
- MCP wrapper.
- Client Settings UI.
- Authenticating the rest of the localhost API.
- Hot-reload of `Delegation:AllowedRoots`.
- Changing `NothingToInherit` advice for true token-less callers (keep it; capability callers never see it).
- Measuring whether a named Codex AlwaysOn *obeys* the orchestrator bundle (operational, D6).

---

## What Build needs from Test-design

Name cases for: issue-once secrecy, authenticate third arm, extra roots vs global empty `AllowedRoots`, board constraint, revoke/rotate, script non-echo, D12 appsettings pin, D5 orchestrator clamp unchanged, D6 Codex session token still delegates, D7 409-not-remap under a Claude hold. No headed Codex TUI tests in this card. No E2E browser. Script tests follow `DelegateScriptRunner` (empty `ANTIPHON_TASK_TOKEN` in the child). Alternate `OutputPath=bin-card-0398/` with a forward slash; delete those dirs after.

## Verification design

Safety-critical: a new hashed-at-rest principal on `X-Antiphon-Task-Token` that must not widen `Delegation:AllowedRoots`, must not leak the bearer, must not lift the Orchestrator-kind clamp or Codex remote control, and must not silently remap a Claude hold onto Codex. Every guard below has a PC. Isolated schema for new tables (Gotcha #24). New test classes are `[Category("Integration")]` xor `[Category("Unit")]`. Script tests use `DelegateScriptRunner` (child `ANTIPHON_TASK_TOKEN` empty; isolated `ANTIPHON_CAPABILITY_STORE`). HTTP secrecy tests follow CARD-0106: assert on the **raw JSON string**, not on a DTO that might project differently. Build `--property:OutputPath=bin-card-0398/` (forward slash); delete the `bin-card-0398` directories after.

### Pins (plan ambiguities, so Code does not guess)

These are TestDesign defaults that match D1–D12. S1’s “concatenates ExtraAllowedRoots onto the resolver’s root list” is **not** the rule — D2 is.

| Topic | Pin |
|---|---|
| Outside the capability’s own roots | **422** `ValidationException` on `workingDirectory` (`ExceptionMiddleware` → Unprocessable Entity). Not 403. Field error **names the capability**. Must **not** contain `Add it to Delegation:AllowedRoots`. |
| Token-less create (no header, no `ANTIPHON_TASK_TOKEN`) | **422**, unchanged. `AgentTaskCallerResolutionTests` stay green **unmodified**, including `a_token_less_request_outside_the_roots_is_told_it_inherits_nothing`. |
| Capability vs `AllowedRoots` | When `caller.CapabilityId` is set, `CreateAsync` passes the capability’s roots **instead of** `_settings.AllowedRoots` into `ResolveAsync`. First root → `Caller.WorkingDirectory` (existing parent-inherit arm, `DelegationWorkspaceResolver.cs:68–72`). Remaining roots → `Caller.ExtraAllowedRoots`. A path that is on `AllowedRoots` but **not** on the capability list is **422**. Concatenating the global list is the fall-through this card exists to forbid. |
| Revoked bearer | **403** `ForbiddenException`, message contains `capability revoked`. Next `AuthenticateAsync` / create. **No cache**: no static/memory map of hash→capability that survives `RevokedAt`. There is no DELETE; revoke (`RevokedAt`) is the only withdrawal. |
| Rotated old bearer | **403** (revoked-or-unrecognised). New bearer authenticates. CLI overwrites the DPAPI file; server stores the new hash only. |
| Unknown header token | **403** `Delegation token is not recognised.` Unchanged. Lookup order: task hash, then session hash, then capability hash. |
| Board constraint miss | **422**, names the capability. |
| Issue HTTP | **201 Created**. Body includes `token` (64 lowercase hex from `NewToken()`) and may include `storePath` as a **hint**. Server **never** writes `%LOCALAPPDATA%` / the store (CLI is the only writer — S1). |
| Rotate HTTP | **200 OK**, new `token`. Revoke HTTP: **200 OK**, **no** `token` property. GET list/detail: **200**, **no** `token` property. |
| DPAPI blob | Path `$store\<name>.dpapi` where `name` is already `[A-Za-z0-9_.-]+` (max 64). Default store `%LOCALAPPDATA%\Antiphon\capabilities`. Tests set `ANTIPHON_CAPABILITY_STORE` to a temp dir. Payload: `ProtectedData.Protect(UTF8 bytes of the raw 64-hex token, optionalEntropy: null, DataProtectionScope.CurrentUser)`. Unprotect + UTF-8 equals the raw token. **No JSON envelope.** |
| Orchestrator + `-Kind Codex` | **422** `ValidationException` on `agentKind` (`an_orchestrator_cannot_be_asked_to_run_on_Codex` stays). Capability caller does not lift this. |
| Codex remote control | **409** `remote_control_refused`. Catalog `remoteControl` for Codex stays Unsupported. This card does not touch `RemoteControlPolicy`. |
| Claude usage hold | Default kind (ClaudeCode) → **409** `model_disabled` (`ModelDisabledException`). Explicit `AgentKind.Codex` Worker/stage succeeds. No rewrite in `ResolveAgentKind`. Kind-wide hold = `ModelAlias.KindWide` (`*`), reuse `ModelAvailabilityCreateTests.SeedHoldAsync`. |
| Subscription quota | **409** `subscription_quota_low`, never a silent provider remap. Reuse `Create_returns_409_subscription_quota_low_for_a_pinned_agent_whose_profile_key_is_low` shape with a capability caller. |
| Issuance audit | `DelegationCapabilityEvent` rows: `Issued`, `Rotated`, `Revoked`. `Detail` = name + root list + board/project ids. **Never** the raw token, **never** the hash (a 64-hex in a log is credential-shaped). |
| Use audit | `LastUsedAt` on the capability row, updated only on **successful** `AuthenticateAsync`. No per-call event row (D10). |
| Attention | Projection of those events onto `GET /api/attention`: Issued/Rotated → Info, Revoked → Warning, recency 24 h. New `AttentionKind` (client `attention.ts` union + `ATTENTION_VISUALS` Record + `attentionVisuals.test.ts` `ALL_KINDS`). Message/JSON must not contain token or hash. |
| Worker child | `MayDelegate` false; create **403** `Workers cannot delegate`. Existing `AgentTaskServiceIntegrationTests.a_worker_cannot_delegate` stays. Capability-minted child token is the same. |
| `RawTokens` | Capabilities do not enter `AgentTaskService.RawTokens`. Child task tokens still do. |

### Proves it works now

S1 — principal, authenticate, scoped create (`tests/Antiphon.Tests/Application/DelegationCapabilityTests.cs`, isolated schema; HTTP secrecy in `tests/Antiphon.Tests/ApiKeys/DelegationCapabilityApiTests.cs` mirroring `ApiKeyApiTests` canary-on-raw-JSON):

- V-1: capability create into its first root succeeds with `AllowedRoots = []` · integration · `capability_create_into_first_root_succeeds_with_empty_AllowedRoots` · 201/DTO; `NoReplyRouting` true; row `ReplyTo.None`; `SessionId` null
- V-2: capability create into a second listed root succeeds · integration · `capability_second_root_is_accepted`
- V-3: capability create **outside** its list is 422, names the capability, does not mention AllowedRoots · integration · `capability_create_outside_its_roots_is_422_names_capability_does_not_advise_AllowedRoots`
- V-4: same as V-3 even when `AllowedRoots` contains that outside path (no fall-through) · integration · `capability_create_outside_its_roots_is_422_even_when_AllowedRoots_contains_that_path`
- V-5: prefix-neighbour (`C:\src\antiphon-evil` vs root `C:\src\antiphon`) is 422 · integration · `capability_prefix_neighbour_is_not_within_root` (logic already in `DelegationUnitTests`; this is the capability caller)
- V-6: token-less create into a capability’s root is still 422 · integration · existing `AgentTaskCallerResolutionTests.a_token_less_request_outside_the_roots_is_told_it_inherits_nothing` **unmodified**, plus `token_less_create_into_capability_root_is_still_422` (empty AllowedRoots, capability exists, no header)
- V-7: `AuthenticateAsync(raw)` → `MayDelegate`, `WorkingDirectory` = first root, `SessionId` null, `CapabilityId` set; `LastUsedAt` advances · integration · `authenticate_raw_token_returns_MayDelegate_and_first_root`
- V-8: issue stores SHA-256 hash ≠ raw; DB column is the hash from `AgentTaskService.HashToken` · integration · `issue_stores_hash_not_raw`
- V-9: GET list + GET detail raw JSON has **no** `token` property and does **not** contain the issue canary or its hash · integration (HTTP, `JsonSerializerDefaults.Web` / actual response body) · `DelegationCapabilityApiTests.get_list_and_detail_keep_canary_and_hash_out_of_the_json` (CARD-0106 shape: plant `NewToken()` canary, assert `GetRawText()`)
- V-10: issue POST raw JSON **does** contain `token` matching `^[0-9a-f]{64}$`; rotate likewise; revoke GET/POST bodies do not · integration · `issue_and_rotate_http_include_token_revoke_and_gets_do_not`
- V-11: GET DTO type census: no property named `Token`/`RawToken`/`Secret`/`Bearer` (OrdinalIgnoreCase) unless `[JsonIgnore]`; serialize with API camelCase options; JSON has no those keys · unit · `get_dto_serialization_has_no_token_shaped_property`
- V-12: board constraint: `-Card` on another board is 422 naming the capability · integration · `board_constraint_card_on_another_board_is_422`
- V-13: revoked bearer is 403 `capability revoked` on the **next** authenticate/create (same process, no delay) · integration · `revoked_bearer_is_403_on_the_next_authenticate`
- V-14: rotate: old bearer 403, new bearer creates · integration · `rotated_old_bearer_is_403_new_bearer_works`
- V-15: worker token minted for the capability’s child cannot create · integration · `capability_child_worker_token_cannot_create` (403 `Workers cannot delegate`; 0 child rows)
- V-16: Codex named-session hash still `MayDelegate`, cwd = `session.Cwd` (D6) · integration · `codex_session_hash_still_MayDelegate`
- V-17: capability caller + `Kind: Orchestrator` + `AgentKind: Codex` is still 422 (D5) · integration · `capability_caller_orchestrator_kind_Codex_is_still_422` **and** existing `AgentTaskAgentKindTests.an_orchestrator_cannot_be_asked_to_run_on_Codex` unmodified
- V-18: tracked `server/appsettings.json` `Delegation` object has no `AllowedRoots` property (file text, not bound options) (D12) · unit · `DelegationAllowedRootsFileTests.tracked_appsettings_Delegation_has_no_AllowedRoots_key`
- V-19: issue validation: bad name / filesystem root (`C:\`) / 0 roots / 9th root / missing directory → 422, no row · integration · `issue_validation_rejects_name_filesystem_root_and_root_count`
- V-20: capability issue does not insert into `RawTokens` · integration · `capability_does_not_enter_RawTokens`

S2 — client store + scripts (`tests/Antiphon.Tests/Scripts/CapabilityScriptTests.cs`, `tests/Antiphon.Tests/Application/DelegateScriptCapabilityTests.cs`; `DelegateScriptRunner`; stub must capture `X-Antiphon-Task-Token`):

- V-21: `capability.ps1 issue` stdout/stderr do not contain the 64-hex token; file exists; Unprotect round-trips · integration · `issue_does_not_print_the_token_and_dpapi_round_trips`
- V-22: `delegate.ps1 -Capability <name>` sends the header and does not assign `$env:ANTIPHON_TASK_TOKEN` (source pin: capability path has no `$env:ANTIPHON_TASK_TOKEN =` write) · integration + source · `delegate_Capability_sends_header_and_does_not_assign_env_token`
- V-23: `$env:ANTIPHON_CAPABILITY=<name>` (the **name**) works with empty `ANTIPHON_TASK_TOKEN` · integration · `delegate_ANTIPHON_CAPABILITY_name_sends_header`
- V-24: both `ANTIPHON_TASK_TOKEN` and `-Capability` / `ANTIPHON_CAPABILITY` → non-zero exit, **zero** HTTP requests · integration · `capability_and_task_token_together_is_refused_before_request`
- V-25: missing store file: non-zero; message names the capability and the store path; does **not** contain AllowedRoots sentence · integration · `missing_capability_file_does_not_advise_AllowedRoots`
- V-26: exactly one file in the store, no `-Capability`, no `ANTIPHON_CAPABILITY` → does **not** auto-load (option 3 in disguise) · integration · `auto_load_of_the_only_store_file_is_forbidden`
- V-27: `Write-Error` / 422/403 paths never echo the 64-hex token · integration · `error_paths_never_contain_the_token`
- V-28: `capability.ps1` and `delegate.ps1` are ASCII-only · unit · extend `RoutingPinScriptTests.Delegate_and_routing_pin_scripts_are_ascii_only` to include `capability.ps1`
- V-29: server issue does not create a `.dpapi` under the test user’s LocalAppData default when `ANTIPHON_CAPABILITY_STORE` is unset on the **server** · integration · `server_issue_does_not_write_LocalAppData`

S3 — audit + attention + docs:

- V-30: `DelegationCapabilityEvent.Detail` on Issued/Rotated/Revoked contains name + roots and contains neither the raw canary nor `HashToken(canary)` · integration · `event_detail_has_name_and_roots_never_token_or_hash`
- V-31: after issue/rotate, attention feed has Info; after revoke, Warning; raw attention JSON has no canary/hash · integration · `attention_issue_info_revoke_warning_without_secret`
- V-32: docs string pins: `docs/ops-http.md` contains the ChatGPT UX sentence from the plan (“Run `delegate.ps1 -Capability`); `docs/agent-kinds.md` still says Orchestrator-kind is ClaudeCode and Codex RC is refused; `server/Bundles/board-api.md` mentions `-Capability` and `-Kind Codex` while Claude is held · unit · `docs_contain_capability_ux_sentence`

S4 — quota-degraded dispatch (no remap):

- V-33: capability create with explicit `AgentKind.Codex` (Worker/stage, not Orchestrator) succeeds under a Claude `ModelAlias.KindWide` hold · integration · `capability_create_Kind_Codex_succeeds_under_Claude_kind_wide_hold`
- V-34: capability create with **default** kind (no `agentKind`) under that hold is **409** `model_disabled`, `AgentKind` is not rewritten to Codex, **no row inserted** · integration · `capability_create_default_kind_is_409_model_disabled_under_Claude_hold_not_Codex`
- V-35: capability create pinning a Codex agent whose profile key is low is **409** `subscription_quota_low`, no row · integration · `capability_create_low_Codex_quota_is_409_not_rerouted`
- V-36: `ignoreModelDisabled` still queues rather than launching (existing `ModelAvailabilityCreateTests.Create_with_IgnoreModelDisabled_queues_with_a_warning_and_does_not_409` stays)

Codex remote control (untouched; pin so a drive-by catalog edit is visible):

- V-37: `RemoteControlPolicy.Require(AgentKind.Codex, wanted: true, …)` throws 409 `remote_control_refused` · unit · add `Require_Codex_true_throws_remote_control_refused` next to the Grok case; existing `AgentTuiProfileServiceTests` `SupportsRemoteControl(Codex) == false` stays

Launch-env leak surface (untouched):

- V-38: `AgentLaunchEnv.ValidateOverride` still refuses `ANTIPHON_*` · unit · existing `AgentLaunchEnvTests.ValidateOverride_refuses_ANTIPHON_prefixed_names_including_lowercase`

### Guards the regression

- R-1: a later change concatenates `Delegation:AllowedRoots` onto a capability caller so a path outside the capability list succeeds · caught by V-4 because that create must stay 422
- R-2: token-less / UI create starts inheriting a capability root or a non-empty AllowedRoots · caught by V-6 because token-less into the capability root stays 422 and the CARD-0020 tests stay unmodified
- R-3: GET list/detail (or a future UI bound to those DTOs) grows a `token` field · caught by V-9/V-11 because the canary appears in raw JSON
- R-4: revoke leaves a cached principal that still authorises · caught by V-13 because the next authenticate is 403 in the same process
- R-5: `delegate.ps1 -Orchestrator -Kind Codex` starts returning 201 · caught by V-17
- R-6: Codex `remoteControl` becomes Supported so ChatGPT “attaches” · caught by V-37
- R-7: `ResolveAgentKind` / `CreateAsync` remaps a Claude hold onto Codex · caught by V-34 because status is 409 `model_disabled` and no row exists
- R-8: tracked `appsettings.json` grows `AllowedRoots` (the reverted live edit) · caught by V-18
- R-9: `capability.ps1 issue` prints the bearer, or `delegate.ps1 -Capability` assigns `$env:ANTIPHON_TASK_TOKEN`, or auto-loads the only store file · caught by V-21/V-22/V-26
- R-10: event/attention/error JSON includes the raw token or its hash · caught by V-30/V-31/V-27
- R-11: capability create into a prefix-neighbour of a listed root · caught by V-5
- R-12: a worker child token minted on this path can itself create · caught by V-15
- R-13: issue writes the DPAPI file from the server process and clobbers an operator store · caught by V-29

### Positive controls

Build runs each: break, see red, revert, see green, and reports all three.

- PC-1: break V-4 / R-1 — in `AgentTaskService.CreateAsync`, pass `_settings.AllowedRoots.Concat(caller.ExtraAllowedRoots)` (or pass `_settings.AllowedRoots` whenever `CapabilityId` is set). Expect `capability_create_outside_its_roots_is_422_even_when_AllowedRoots_contains_that_path` red (create succeeds).
- PC-2: break V-13 / R-4 — in the capability arm of `AuthenticateAsync`, skip the `RevokedAt` check (or add a static `ConcurrentDictionary` hash cache that revoke does not clear). Expect `revoked_bearer_is_403_on_the_next_authenticate` red.
- PC-3: break V-9 / R-3 — add `string? Token` to the GET DTO and assign the raw bearer. Expect `get_list_and_detail_keep_canary_and_hash_out_of_the_json` red.
- PC-4: break V-3 / R-1 message — on a capability miss, reuse the token-less `Add it to Delegation:AllowedRoots` sentence. Expect `capability_create_outside_its_roots_is_422_names_capability_does_not_advise_AllowedRoots` red.
- PC-5: break V-17 / R-5 — delete the `kind == Orchestrator && resolved != ClaudeCode` throw in `ResolveAgentKind`. Expect `an_orchestrator_cannot_be_asked_to_run_on_Codex` and `capability_caller_orchestrator_kind_Codex_is_still_422` red.
- PC-6: break V-37 / R-6 — in `AgentTuiRunnerCatalog.CodexCapabilities`, change `remoteControl` from `Unsupported` to `Supported`. Expect `SupportsRemoteControl(Codex)` and `Require_Codex_true_throws_remote_control_refused` red.
- PC-7: break V-34 / R-7 — in `CreateAsync`, on `ModelDisabledException` for ClaudeCode, retry with `AgentKind.Codex`. Expect `capability_create_default_kind_is_409_model_disabled_under_Claude_hold_not_Codex` red (201 Codex).
- PC-8: break V-18 / R-8 — add `"AllowedRoots": ["C:\\src\\Antiphon"]` under `Delegation` in `server/appsettings.json`. Expect `tracked_appsettings_Delegation_has_no_AllowedRoots_key` red.
- PC-9: break V-21 / R-9 — in `capability.ps1` issue, `Write-Output` the token. Expect `issue_does_not_print_the_token_and_dpapi_round_trips` red.
- PC-10: break V-26 / R-9 — in `delegate.ps1`, when `-Capability` is omitted and the store contains exactly one file, Unprotect it. Expect `auto_load_of_the_only_store_file_is_forbidden` red.
- PC-11: break V-22 — in the capability path of `delegate.ps1`, assign `$env:ANTIPHON_TASK_TOKEN = $plain`. Expect `delegate_Capability_sends_header_and_does_not_assign_env_token` red.
- PC-12: break V-30 — put `capability.TokenHash` into `DelegationCapabilityEvent.Detail`. Expect `event_detail_has_name_and_roots_never_token_or_hash` red.

### Out of scope

- Headed Codex TUI, herdr, and `Antiphon.Agents.Pty.Tests` (plan: no headed Codex TUI in this card).
- E2E browser / client Settings UI (D8/D11: no UI in v1). Client work is only the AttentionKind totality if S3 adds a kind (`pwsh -File scripts/test-client.ps1 attentionVisuals.test`).
- MCP sidecar (D11).
- Authenticating the rest of the localhost API; `POST /api/sessions/{id}/messages` stays unauthenticated.
- Hot-reload of `AllowedRoots` / `IOptionsMonitor`.
- Changing `NothingToInherit` copy for true token-less callers.
- Measuring whether a named Codex AlwaysOn obeys the orchestrator bundle (D6 operational bet).
- Lifting Orchestrator-kind = ClaudeCode; enabling Codex remote control.
- A DELETE-capability verb (revoke only).
- `gci env:` inside a ChatGPT-attached interactive shell as a live probe — V-22 is the source pin plus the child-process header test; the honest limit in D3 (same-user Unprotect) is not a test target.

### Cost

- suites forced (class filters, not namespace-wide): `DelegationCapabilityTests`, `DelegationCapabilityApiTests`, `DelegationAllowedRootsFileTests`, `CapabilityScriptTests`, `DelegateScriptCapabilityTests`, `AgentTaskCallerResolutionTests`, `AgentTaskAgentKindTests`, `RemoteControlPolicyTests`, `AgentTuiProfileServiceTests` (catalog remoteControl assertions), `ModelAvailabilityCreateTests`, `AgentLaunchEnvTests`, `DelegationUnitTests`, `AgentTaskServiceIntegrationTests` (`a_worker_cannot_delegate` lives here — run the class), `AttentionServiceTests` if a new `AttentionKind` is added; client `pwsh -File scripts/test-client.ps1 attentionVisuals.test` only if S3 adds the kind
- verification floor ~ 8 min (isolated-schema clones + one WebApplicationFactory + pwsh script spawns). Do not co-schedule `Antiphon.Agents.Pty.Tests`. Do not run the full `Antiphon.Tests` assembly for this card.

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card-0398/ -- --treenode-filter "/*/*/DelegationCapabilityTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card-0398/ -- --treenode-filter "/*/*/DelegationCapabilityApiTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card-0398/ -- --treenode-filter "/*/*/DelegationAllowedRootsFileTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card-0398/ -- --treenode-filter "/*/*/CapabilityScriptTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card-0398/ -- --treenode-filter "/*/*/DelegateScriptCapabilityTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card-0398/ -- --treenode-filter "/*/*/AgentTaskCallerResolutionTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card-0398/ -- --treenode-filter "/*/*/AgentTaskAgentKindTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card-0398/ -- --treenode-filter "/*/*/RemoteControlPolicyTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card-0398/ -- --treenode-filter "/*/*/ModelAvailabilityCreateTests/*"
```

Forward slash on `OutputPath`. Delete the ~12 `bin-card-0398` directories afterwards.
