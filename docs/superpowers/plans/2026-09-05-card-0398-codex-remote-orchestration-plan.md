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
