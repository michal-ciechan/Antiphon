# CARD-0073 transcript binding status

Date: 2026-10-13. Investigator: task `6813e7cf-b2c2-4012-8d96-f197eaba9b34`.
Source baseline: `00fa6b884a5f127982cff26a677aae74e6666650`.

## Verdict

**The August Claude exact-binding regression is fixed. A separate, current remote incident-delivery defect is confirmed by code and reconstructed from stored evidence.** Recommend closing CARD-0073's original S1/S2 claims with the CARD-0101 fixes cited below, and commissioning Plan for the phone-home reporting gap. Do not describe all launch reliability as fixed: one current Grok startup failed without a transcript, and its underlying startup cause remains unconfirmed.

During a frozen 48-hour dispatch cohort, **279 of 280 sessions have a normalized transcript and a UserPrompt**. Five Claude sidecars explicitly record `exact`; the sixth Claude session has a transcript but its sidecar is not available locally. Two hundred Codex sidecars explicitly record `discovery`, which is Codex's normal identity contract, not recurrence of the Claude regression. All 54 Grok sidecars record a deterministic path, but one has no input or transcript: allocating a deterministic path does not prove a file was created.

Investigation only. No source changes, builds, test runs, database access, live launches, service restarts, or tracker writes. API GETs used the inherited task token without printing it. Only sidecars for session IDs returned by the scoped task API were read; no provider credential stores or native conversation files were opened. The requested Markdown report is the sole repository edit.

## Measurement and provenance

Window: **2026-10-11 16:35:33.9020996Z through 2026-10-13 16:35:33.9020996Z**. Project: `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

`GET /api/agent-tasks?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f&since=2026-10-11T16:35:33.9020996Z` returned 292 scoped rows, excluding 18 rows belonging to another project. Filtering `dispatchedAt` into the frozen window gives 280 unique tasks, agents, and session IDs, all attempt 1. Statuses at that read: 255 Succeeded, 12 Failed, 5 Canceled, 2 Blocked, 6 Dispatched. Specialist machinery is excluded by the route's default `includeChecks=false`; these are delegated task launches, not a fleet-wide count of standing agents or every physical process start.

The route's `since` is a **completion** filter, retaining all open tasks regardless of age (`server/Application/Services/AgentTaskService.cs:2427`); therefore counting its returned rows as launches would be wrong. A preliminary Succeeded/Failed query from midnight October 11 returned 429 rows and was not used as the launch denominator. The final cohort includes all statuses to avoid silently losing still-open launches.

Live server `/api/version`: `7f8448798955a24427bc2279138bf9fb64bc4195`. Local server2 `/capabilities`: runner `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, process start `2026-10-11T13:42:59.1014261Z`, UnixPty, no backend fallback. These SHAs differ from the investigation checkout; the event mapper and Codex publisher were also read directly with `git show <deployed-sha>:<path>` and have the mechanisms described below. Git ancestry checks confirmed the historical quoting and S1 fixes are ancestors of the relevant deployed versions.

| Kind | Dispatched 48h | Dispatched last 24h | Nonempty normalized transcript | Zero rows | Known sidecar method | Method unavailable |
|---|---:|---:|---:|---:|---|---:|
| ClaudeCode | 6 | 6 | 6 | 0 | 5 exact; 0 discovery | 1 |
| Codex | 220 | 183 | 220 | 0 | 200 discovery; exact unsupported | 20 |
| Grok | 54 | 24 | 53 | 1 | 54 deterministic paths allocated; 53 have transcript evidence | 0 |
| Total | 280 | 213 | 279 | 1 | 259 sidecars available | 21 |

All 213 launches in the nested last-24-hour window have a UserPrompt. The one zero-row launch is in the preceding 24 hours: 1/280 overall (0.36%), 1/54 Grok (1.85%). This is a measured cohort, not a long-term failure-rate estimate. Initial parallel transcript retrieval returned eight HTTP 502s; one sequential recheck of those eight succeeded with 20, 26, 45, 16, 22, 14, 23, and 24 entries respectively. They are counted as observed transcripts, not as failures or empty sessions. Cause of the transient 502s was not investigated.

Normalized evidence came from `GET /api/sessions/{id}/transcript?since=0`. This reads stored normalized rows after best-effort live synchronization (`server/Application/Services/AgentSessionService.cs:2235`). Every successful read was reduced to entry count, UserPrompt count, and sequence metadata; conversation text was not copied into the report. All first prompts in the initial successful reads occurred after dispatch; the eight rechecks contained UserPrompts as well. Known-session sidecars were read from `/state/session-runner/transcripts/{sessionId:N}.json` and reduced to `how`, input/update timestamps, and whether the recorded path names the session. No desktop sidecar access was available.

## Current binding mechanisms

| Kind | Exact identity | Discovery / restore | Evidence locations |
|---|---|---|---|
| ClaudeCode | Cold launches receive `--session-id <session-id>`; resume uses `--resume`, continue uses `--continue`. The tailer checks `<session-id>.jsonl` under each project directory on every locate pass. | Existing recorded sidecar path first; exact filename second; guarded discovery after a default 10-second grace. Later forks can switch the bound path. | `server/Application/Services/AgentSessionService.cs:2338`, `:2537`, `:2547`; `src/Antiphon.SessionRunner/TranscriptTailer.cs:127`, `:559`, `:579`, `:588`, `:394`. |
| Codex | No launch session-identity argument: its SessionResume contract is Unknown. Provider rollout UUID differs from the runner's ID. There is no exact-ID fast path. | Existing sidecar path first; otherwise discover rollout JSONL by C1-C4 ownership evidence. | `server/Application/Services/ProviderContractCatalog.cs:178`; `src/Antiphon.SessionRunner/CodexTranscriptTailer.cs:406`, `:419`, `:430`, `:491`. |
| Grok | SessionResume is Supported; launch carries `--session-id`. The known path is `GROK_HOME/sessions/<URL-encoded full cwd>/<session-id>/updates.jsonl`. | No Claude/Codex heuristic discovery or claim selection. The path is allocated in the sidecar before the file exists; actual tailer binding happens when that file appears. | `server/Application/Services/ProviderContractCatalog.cs:113`; `src/Antiphon.SessionRunner/GrokTranscriptTailer.cs:87`, `:210`; `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:3024`, `:3037`. |

The kind gate is `UsesSessionIdentityArgs`, driven by the provider's supported resume contract (`AgentSessionService.cs:2537`), not a blanket assumption that every kind accepts Claude's arguments.

Claude discovery still enforces ownership: C0 excludes another known session's namesake, C1 excludes another claim, C2 matches cwd, C2b rejects a differing recorded agentName, C3 requires an eligible child-start epoch, and C4 requires content this session was actually sent (`TranscriptTailer.cs:720`). The final claim is atomic (`:654`). Codex likewise uses claims, cwd, epoch, and delivered-prompt matching (`CodexTranscriptTailer.cs:545`). These predicates were read, not changed or experimentally weakened.

Sidecar binding provenance is durable `TranscriptSidecar.How` (`src/Antiphon.SessionRunner/TranscriptSidecar.cs:54`). A restart's same-path `sidecar` re-tail preserves the prior method instead of overwriting it (`SessionRunnerRuntime.cs:3319`). Runner metadata exposes current bound/how/unbound-reason fields (`:3779`); those are current tailer observations and can differ from durable original-bind provenance after restore.

### Logs, incidents, and faults

Claude discovery logs a Warning that its namesake file never appeared (`TranscriptTailer.cs:593`). **Exact and sidecar binds deliberately suppress `SessionTranscriptBound`** (`:692`). Codex logs an Information adoption line and publishes a bound event for every non-sidecar bind (`CodexTranscriptTailer.cs:432`, `:511`). Grok has no heuristic-bound event.

On the desktop HTTP/SSE lane, bound events reach `TranscriptBindingIncidentService.OnHeuristicBindAsync`, yielding Info `TranscriptBoundByDiscovery`, with a message distinguishing discovery/fork/etc. and `raiseAlert:false` (`server/Application/Services/TranscriptBindingIncidentService.cs:283`, `:303`). There is no corresponding exact-bind incident. **Zero exact-bind incidents is expected, not evidence of zero exact binds.**

Claude's live, delivered-input, zero-cwd-candidate state raises `TranscriptMissing` after the 60-second refusal interval, repeating no more often than five minutes (`TranscriptTailer.cs:45`, `:607`, `:1050`). Candidate refusals and exact-file claim loss have their own census/fault path (`:989`); missing root and child-exit faults are separate. Codex has analogous delivered-input refusal/empty-census and child-exit reporting (`CodexTranscriptTailer.cs:688`, `:744`, `:795`); an absent sessions root alone remains normal before a first submit (`:460`). Grok reports a missing updates file after child exit only if input was delivered (`GrokTranscriptTailer.cs:335`).

The server records `TranscriptBindFailed` as Warning, or Critical for channel-bound agents; `ClaimRevoked` has a separate incident. It escalates stuck episodes and has standalone-alert fallbacks for unowned sessions and failed incident writes (`TranscriptBindingIncidentService.cs:47`, `:92`, `:113`, `:143`, `:198`). Owner lookup checks `PersistentSessionId`, then a matching task whose agent still exists (`server/Application/Services/SessionOwnerLookup.cs:34`). Remote event loss below prevents these handlers from receiving phone-home faults.

## API incident and attention evidence

`GET /api/agents/{agentId}/incidents?take=200` was requested for each of the 280 cohort agents. 228 returned successfully; 52 returned HTTP 404 (Claude 1, Codex 43, Grok 8). Actual successful responses contained 236 incident rows, with at most two per agent, so none hit the 200-row cap (`server/Application/Services/AgentService.cs:233`). No pagination claim is needed or possible on this route. Agent deletion cascades incidents (`server/Infrastructure/Data/AppDbContext.cs:428`, `:443`), so those 404s cannot establish incident absence.

Returned incident kinds in the frozen window: DelegateReleaseUnresolved 217, ApiErrorTurnDied 9, DeliveryTransportFailed 4, DeliveryVerificationFailed 4, DelegateCompletedWithoutProgress 1, **TranscriptBoundByDiscovery 1**. No TranscriptBindFailed, TranscriptClaimRevoked, exact-bind incident, or incident message containing `boot prompt watchdog` / `StoppedBeforeFirstPrompt` / `no transcript` was returned. These are accessible-row counts, not complete fleet counts.

The sole bind row was incident `f0a505be-390b-452e-bf4c-dd673540e270`, agent `15011b30-0044-4bef-9091-87a036b45047`, session `db247849-6d4b-4bad-a025-20d39fdc659a`, at `2026-10-13T15:33:48.670562Z`, Info, message `Transcript bound by discovery: C:\Users\lndco\.codex\sessions\2026\10\13\rollout-2026-10-13T16-33-34-01a135e5-c95c-79f1-83c1-f7d57a7ab5b1.jsonl`. This is desktop provider-path evidence; it does not describe the other 200 remote discovery sidecars.

`GET /api/attention` was read without a board filter, as documented. Matching headline/evidence search over rows whose sinceUtc is in the window found no boot-watchdog/StoppedBeforeFirstPrompt/no-transcript alarm. One unrelated ReportUnsettled row mentioned a transcript. Attention is a current projection, not an incident archive; given deletion and the remote defect, its silence is not a health proof.

All 12 Failed task detail responses were inspected. None has failureCode StoppedBeforeFirstPrompt or a boot-prompt-watchdog reason. Eleven Codex tasks have nonempty transcripts: five report incomplete work/verification, one reports a decision question, three hit the 240-minute Code deadline after AssistantText, one hits a 20-minute mid-turn model wait after AssistantText, and one has CompletedWithoutProgress. The remaining Grok task is described next. The contemporary boot policy also differs from August: unresolved transcript-confirmed boot-model stalls are detection-only (`server/Application/Services/AgentTaskDispatcher.BootStall.cs:8`); the never-started delivery arm still distinguishes absent prompt from unreported native activity (`AgentTaskDispatcher.cs:2255`).

## The one current zero-transcript launch

Task `e4073ef5-1a6c-4c22-87c9-624a7a3ecf5f`, CARD-1192, Grok/High, runner server2, session `2866cd07-9b82-430f-96aa-141693c46fa6`:

- Dispatched event: `2026-10-12T08:50:59.737904Z`.
- Session detail: Failed, ended `2026-10-12T08:52:16.97314Z`, lastTranscriptAt null.
- Failed task event: `2026-10-12T08:55:43.435509Z`, detail: `Session died before the task settled: its session is Failed (Agent process did not become ready.). No report is coming; read session 2866cd07-9b82-430f-96aa-141693c46fa6 before re-running this task.` FailureCode null.
- Normalized transcript GET: entries `[]`, lastSequence `0`.
- Known-session sidecar: `how=deterministic`, firstInputAtUtc null, updatedAtUtc `2026-10-12T08:51:00.3934991Z`.
- Local runner GET `/sessions`: same session Exited, exitReason KilledByRequest, transcriptBound false, lastSequence 473. Terminal-output sequence 473 is **not** a transcript receipt.

This is a **startup-readiness failure before delivered input**, approximately 77 seconds to the session's Failed state, not a ten-minute watchdog failure after a Claude transcript bind refusal. Its exact startup blocker is not established by these API/sidecar records. A known-session startup capture/runner log or owned ANSI record would resolve which readiness gate failed. The investigation did not inspect native provider content or infer trust/auth/MCP/concurrency from this generic failure.

## Confirmed remaining mechanism: phone-home drops binding diagnostics

The remote transport can deliver transcript content while losing binding diagnostics. Reconstruction from current and deployed source:

1. A Codex discovery bind persists the sidecar, then publishes `SessionTranscriptBound` (`CodexTranscriptTailer.cs:505`, `:516`). Fault paths publish `SessionTranscriptFault`.
2. The runner sends every event's name and JSON in a phone-home frame (`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:359`).
3. Server `RunnerContractMapper.ParseEvent` recognizes only SessionOutput, SessionExited, SessionAdopted, SessionStarted, and SessionTranscript, then returns null (`server/Infrastructure/Agents/SessionRunner/RunnerContractMapper.cs:75`, `:124`). Both diagnostic event names fall through. This omission is present in deployed server SHA `7f844879...` and in the original phone-home introduction `b6fddd73e` (CARD-0490), not merely in unpublished checkout code.
4. Both the streaming client and active recovery pump discard null parser results (`PhoneHomeRunnerClient.cs:562`; `PhoneHomeRecoveryPump.cs:366`). The active pump additionally handles only Output, Exited, and Transcript after parsing (`PhoneHomeRecoveryPump.cs:380`); it has no bound/fault incident dispatch. This second omission is also present in the deployed server source.
5. The desktop HTTP parser **does** map both names (`SessionRunnerHttpClient.cs:1006`, `:1020`), and its event pump invokes the incident service (`SessionRunnerEventPump.cs:418`, `:430`). Hence desktop diagnostic rows and remote normalized transcripts can coexist with missing remote bind incidents.

Concrete stored/live witness: this investigation's own task `6813e7cf-b2c2-4012-8d96-f197eaba9b34`, agent `b2929dc4-cbcf-4b2d-8731-1d2571511e49`, session `867413cc-4e26-4a01-b5d7-102872684091`, runnerId server2. Dispatch `2026-10-13T16:33:05.711498Z`; sidecar `how=discovery`, updated `2026-10-13T16:33:10.4870774Z`; transcript GET yielded four rows including one UserPrompt. A subsequent task detail still showed the active owner, and its incidents GET returned HTTP 200 with `[]`. This avoids explaining the missing row by deleted-agent 404s. A second witness is task `fe5c4378-89e7-48a4-98db-3595fc69b11e`, session `0b33cfa7-3809-47d5-9add-442a9b4e1bdf`, discovery at `16:32:56.8113464Z`, four normalized rows, and an accessible empty incident response.

**Confirmed by reconstruction**, not by a synthetic event injection: these exact event names cannot reach remote incident handlers through the deployed parser/pump. No captured wire frame was retained, so the individual witness's wire transmission timing is not independently proved. The code path suffices to establish deterministic dropping of such frames; the sidecar/API mismatch establishes the live observability consequence. Fault-event loss is established by the same parser fall-through, but no actual delivered-input unbound remote failure was observed in this cohort.

## Which changes fixed the original card

| Commit/card | Established effect |
|---|---|
| `620f6eb6c0c6cd1886dd6e27c0fd8290740ae49b`, CARD-0073 S1 | Added the live delivered-input zero-candidate TranscriptMissing census and delegated-session owner fallback. The previously silent reporting state is handled on the desktop lane. |
| `aa1c8f123a83f762637f6d159c66b8ebb7302bb6`, CARD-0101 | Replaced doubled-quote Windows argument formatting with CRT escaping in ModernConPtyConnection. The bundle had swallowed --session-id; this restores the argument, rather than removing --name. Current implementation: `ModernConPtyConnection.cs:562`, `:582`. |
| `31020516a`, CARD-0101 | Added launch-time Win32 argv round-trip verification and stuck-bind escalation. Current guard sites: `ModernConPtyConnection.cs:169`, `LaunchArgvGuard.cs:76`. |
| `7a92098f01b13d0101756d5a5093c505308207c0`, CARD-0101/CARD-0104 | Corrected the inbox backend too: pre-escape arguments and hand Porta a verbatim line, retaining verification (`PtyAgentRunner.cs:181`). The card's August addendum warning that inbox merely refuses unsafe shapes is superseded by this follow-up. |

The stored August reconstruction is `docs/investigations/2026-08-20-delegate-command-line-shred.md:30`: the measured live command line became 165 arguments, its system bundle ended early, positional `green` became the first prompt, and --session-id was swallowed. CARD-0073 revisions 2/3 and its current description explicitly correct the original --name attribution and the claim that the lost session produced no provider file. The unresolved historical question of why its C2 census missed that file is still unresolved; it is unnecessary to establish either the quoting mechanism or S1's missing reporting path.

CARD-0340 `4dafda4a4` addressed interrupted Starting launches, and CARD-0353 `b8d9e0b94` addressed first-model-call liveness; neither is the fix for swallowed --session-id. Current observed Claude exact bindings supply the fresh evidence the August correction asked for. The five measured exact binds are on Linux server2; this pass did not independently exercise current Windows CLI quoting. The shipped code, stored Windows reconstruction, and live Linux exact evidence have distinct scopes.

## Remaining uncertainties and disposition

- Method provenance for one Claude and 20 Codex sessions is unavailable from this runner's sidecars. Their normalized transcripts prove delivery/content exists, not their original bind method. Retrieve their owning runner's known-session sidecars to complete the method census.
- Fifty-two agent incident routes are 404; surviving incident rows cannot certify a complete historical failure count. The independent transcript census covers all 280 current sessions.
- No injected remote fault was attempted under this read-only brief. The deterministic parser/pump omissions are confirmed; an authorized focused event replay would measure end-to-end alarm delivery and deduplication behavior.
- Grok task e4073ef5's precise readiness blocker, the eight transient 502 causes, and the August session's C2 census anomaly remain unconfirmed. None is evidence that Claude's original quoting regression persists.
- S3 is partly available as sidecar/runner metadata; this pass does not establish that a UI/digest exposes original How. The remote incident gap is a real additional limitation. Historical Windows worktree residue is outside this runner checkout and was not swept.

Disposition: close the original exact-bind/S1 root-cause questions with the commits and five fresh exact witnesses; retain or file separately the confirmed phone-home diagnostic loss for Plan. Preserve the Grok startup row as an unresolved startup investigation rather than turning it into a transcript fallback defect. No fix was designed or implemented.

## Not done, noted

One possible follow-up: restore remote bound/fault event handling across the mapper and active recovery pump while preserving the existing incident service's ownership and severity semantics; implementation slices and tests belong to Plan.

## Stored evidence ledger

The compact session and receipt ledgers below preserve API-derived metadata in this report; generated JSON receipts remain outside Git at `/tmp/card0073-evidence` and are not relied on as a durable caller artifact. Snapshot counts above are frozen at the first task read; transcript and incident retrieval followed it. Terminal sessions' normalized transcript retention is seven days (`docs/logs.md:77`), so a later rerun may not reproduce all retained-row counts. There were no builds or tests and no CHECKPOINT receipts.

Receipt SHA-256 provenance (raw responses/reductions, not committed payloads):

| Receipt | SHA-256 |
|---|---|
| manifest.json | 5cd14bf870d2b0346efc0631a1f45d186c3365d9dcda554748e6d05526589a3f |
| task-list.json | fe0280d73c02b2cb550a17ff32edc1549390b61b8263f0c19868717353ae9480 |
| tasks.json | 19c9f630a0a2ee123e1f4855561a1059d8ce7b511a96ef703f2b99720e6fd859 |
| incidents.json | de0f723fc4896437190f5f0e559cfc333c1a0009ee8356e51a5f4636ea165348 |
| attention.json | dc08463bb09bbd95fdfa73196a36c62d007b9513b440b8b0c41b2da38ac61db1 |
| transcript-metadata.json | 68909313a111bba9049a8f956b3856873ae8935ad56bbd5b0bb27ef1e335ac5f |
| transcript-recheck.json | 0d72ba1f2e8093e0d9294b758f894dcf5e75955fb2ae7368a4f8e12a303e5c92 |
| mapper-sample-detail.json | 650da4721ed77fa495f72d85990278cdb24e24b2a931f9a47f852f60bd2e1eca |

Session ledger: C = ClaudeCode, X = Codex, G = Grok. Method `?` means no local sidecar. `deterministic` is an allocated path, not a file-existence receipt. Counts are normalized entries/UserPrompts; `R` identifies the successful recheck after an initial 502. Incident column is successful API row count or HTTP 404, not an inferred bind count.

| Task ID | Session ID | Kind | Status | Dispatched UTC | Sidecar How | Entries / UserPrompts | Incident GET |
|---|---|---|---|---|---|---|---|
| 01c9d3f2-18bc-4aab-babc-0a57ac195d53 | 17d93c22-1822-44a1-9f52-37f0c75b44a0 | G | Succeeded | 2026-10-11 16:41:37 | deterministic | 342 / 3 | 1 |
| 149ee904-97fb-4ffc-bd86-6edf4e772717 | 5725d03d-ff0c-4499-8859-e132b8d3a90a | G | Succeeded | 2026-10-11 16:41:37 | deterministic | 320 / 3 | 1 |
| 1234b5c5-9a34-4f6c-8139-98055c26e5c6 | f5076eb1-e285-4406-adc4-3513570a6e94 | X | Succeeded | 2026-10-11 16:42:50 | discovery | 16 / 1 | 1 |
| f503e575-2bef-46db-b067-750f367e3e42 | 37e0853f-eeb7-4b87-8dc0-c9f812676b6a | G | Succeeded | 2026-10-11 16:43:36 | deterministic | 278 / 3 | 1 |
| d633d5c0-114c-4fa8-889b-5de159857794 | 19de1a9d-86fb-48d8-95a5-a33e082d3dcb | X | Succeeded | 2026-10-11 16:47:06 | discovery | 11 / 1 | 1 |
| e1a99197-ff00-4841-b742-6d042c635bbe | bcb66910-0303-4b04-853f-67de86b6c18f | G | Succeeded | 2026-10-11 16:51:27 | deterministic | 273 / 3 | 1 |
| ecd7aa48-aee4-47ee-a3df-118a60b6515c | 7da40fe8-abee-46e1-bcbe-e057d22cd46d | G | Succeeded | 2026-10-11 17:05:24 | deterministic | 425 / 3 | 1 |
| b2f5300d-5371-4771-bd58-d7eed132add0 | 04e3a2c4-0482-4d29-b5ff-99fd764a7432 | X | Succeeded | 2026-10-11 17:28:07 | discovery | 10 / 1 | 1 |
| 7971bc66-1cc9-45e0-866b-5e7759443b74 | 027e5eca-864e-46ce-a4c2-fa7200a3832f | X | Succeeded | 2026-10-11 17:38:32 | discovery | 15 / 1 | 1 |
| 18327db7-bc93-48fa-b8e0-d28d8dc2112f | edbdb8fd-9d4c-41f9-80d0-9d1710c2924c | X | Succeeded | 2026-10-11 17:40:47 | discovery | 23 / 1 | 404 |
| b1c9760c-09d1-4376-ab11-2e8634783476 | d2d5d845-a04b-47a5-a6c3-60661a186497 | X | Succeeded | 2026-10-11 17:42:37 | discovery | 17 / 2 | 404 |
| 6f4b657d-b61c-4311-9ac6-ad430d20f5bd | 87270c38-6feb-4721-8dfd-596bd2fa1b2b | G | Succeeded | 2026-10-11 17:49:17 | deterministic | 478 / 3 | 1 |
| cdb0ca52-88c2-4df1-be64-48a73f01b418 | 58ea9e52-37c9-40fa-9854-36026f2386be | G | Succeeded | 2026-10-11 18:03:32 | deterministic | 126 / 2 | 1 |
| b8bedc73-4fbd-4b77-bbc0-f769a83c0037 | e76f5086-526a-4135-9c7b-0b4379ad695c | X | Succeeded | 2026-10-11 18:04:36 | discovery | 12 / 1 | 1 |
| 29f384b1-76b8-4f42-b4a0-db4d03f91988 | 67b0ecb8-a3db-4e56-b071-8a8eed800654 | G | Succeeded | 2026-10-11 18:14:31 | deterministic | 237 / 4 | 1 |
| d5014684-20bf-49ad-88b8-295f7772e1ce | 40245d6b-47ef-4006-8c6f-f633753cb099 | G | Succeeded | 2026-10-11 18:20:21 | deterministic | 333 / 3 | 1 |
| d749b2fa-5114-4f43-9409-dd16f3201d1d | 5d358049-9981-47a5-ae50-968ec8b3f367 | X | Succeeded | 2026-10-11 18:25:06 | discovery | 11 / 1 | 1 |
| 9dea6ddd-7b64-4689-acde-d57671e8634a | 78b51615-458b-4b59-b62e-305ee383149b | X | Succeeded | 2026-10-11 18:40:51 | discovery | 9 / 1 | 1 |
| 771fa4ce-c6ab-4e94-a6bc-1ef22185f8cd | d8abb925-4fdd-476e-bea1-23274a06e655 | X | Succeeded | 2026-10-11 18:40:35 | discovery | 8 / 1 | 1 |
| c8c2ae56-e5a0-44b9-96c9-3a08530e1873 | f04a412f-8ee9-4621-b438-86b5e2150264 | X | Succeeded | 2026-10-12 07:06:33 | discovery | 14 / 1 | 1 |
| 261a43a2-6d0f-486c-9b96-5f7aeee6e6f0 | c2b6b13b-42fd-4f4a-9017-ca0a4aa3c7b2 | X | Succeeded | 2026-10-12 07:07:38 | discovery | 30 / 10 | 2 |
| 6b75a131-3dc3-47b5-a301-88a24a26e5ce | 8ff5cd6e-1c9b-4d52-99ab-566a5533269c | X | Succeeded | 2026-10-12 07:07:38 | discovery | 11 / 1 | 1 |
| f9bcd692-68a4-4d71-88a5-bf7148e7cec4 | 54578c50-c3d0-4fe8-a02f-7d856ad0db23 | X | Succeeded | 2026-10-12 07:07:39 | discovery | 12 / 2 | 1 |
| 23285d2c-f62c-4320-8a1f-c69ba83254ef | 5dc6158c-8248-4725-a87f-86aa1d9c556c | X | Succeeded | 2026-10-12 07:07:40 | discovery | 11 / 2 | 1 |
| 96f951b3-05e8-4e65-a973-bf1391ca42f1 | 3591e385-ec19-4274-87c9-c296c93c4f6d | X | Succeeded | 2026-10-12 07:07:45 | discovery | 7 / 1 | 1 |
| 2000a0d1-996f-44dc-be0c-4408146185ed | e8ecc84e-2aee-480d-a9fe-ebadc19f47a7 | X | Succeeded | 2026-10-12 07:15:25 | discovery | 8 / 1 | 1 |
| 07c7df01-3a0e-4b51-b328-fb3cd25a2ed3 | bdd8bb42-c862-46d8-aed0-b368e787f4f6 | G | Succeeded | 2026-10-12 07:20:11 | deterministic | 102 / 2 | 1 |
| dd2619a1-4765-44cf-be14-b43818c176d3 | 25ae7c0e-9029-4800-98cd-41fda492f215 | X | Succeeded | 2026-10-12 07:25:56 | discovery | 13 / 1 | 1 |
| 5091044b-8592-4b80-bb58-abfa62a1cf9e | d7cff34a-7c8b-4290-8a54-2a7fe8d993e9 | X | Succeeded | 2026-10-12 07:27:40 | discovery | 7 / 1 | 1 |
| d3540698-f40f-480f-bddd-8b75c9af4a3c | 60914ba3-9cd0-4530-8b2e-703774017f6a | G | Succeeded | 2026-10-12 07:35:35 | deterministic | 112 / 2 | 1 |
| a3fd0df3-a5b3-49d5-a14a-c6dd49f6b9ca | 0cbeee51-77fc-4375-b92f-704d4afd9d91 | G | Succeeded | 2026-10-12 07:36:40 | deterministic | 304 / 3 | 1 |
| 44a8eca2-a89c-4d2e-8859-4b8e8d47a4a8 | 4477ec2e-e1dd-49c4-bb61-2cead2d091ef | G | Canceled | 2026-10-12 07:38:25 | deterministic | 407 / 4 | 404 |
| 43c99ab4-05be-435b-81fb-0c39c4ea0c24 | 5c7ac160-2637-444a-8046-c0f549ec9d95 | G | Succeeded | 2026-10-12 07:39:16 | deterministic | 404 / 3 | 1 |
| 29c608fe-fd8d-4325-8c50-96c263a3aebe | 8a11424a-1fb2-44a5-b25c-13d1d68dc196 | X | Succeeded | 2026-10-12 07:41:01 | discovery | 13 / 3 | 2 |
| cee24c68-22d5-47ed-929c-0f9177577f9a | c497a274-da61-4267-bcae-b4cc53855189 | G | Succeeded | 2026-10-12 07:43:40 | deterministic | 467 / 3 | 1 |
| a6077026-8dad-4387-a883-4fff233b5fc7 | 63c84930-560a-47bc-a4df-e6a7687de0ed | X | Succeeded | 2026-10-12 07:45:36 | discovery | 8 / 1 | 1 |
| 899f86dc-5a9f-4741-b5e6-8966a0394255 | 67dfb23f-b3ee-46a0-b463-e4039e8f51b4 | X | Succeeded | 2026-10-12 07:57:42 | discovery | 8 / 1 | 1 |
| b6f2c2af-3065-4359-b5ff-9952063e94c0 | 4a453f46-eb70-4587-a88b-e1f1e040d658 | G | Succeeded | 2026-10-12 07:57:55 | deterministic | 180 / 3 | 1 |
| 04655af5-5aa0-4f83-9257-393916348d84 | 741bf8f1-7927-4e27-a981-ca73e5e14f87 | X | Succeeded | 2026-10-12 08:18:05 | discovery | 10 / 1 | 1 |
| 9b90ffac-16c2-4952-878e-23864390cf58 | be31db3d-c500-455d-8aa5-6de5d229b011 | X | Succeeded | 2026-10-12 08:29:30 | discovery | 8 / 1 | 1 |
| 3a846ebf-ccde-43c4-81f4-a22e1a844de4 | 337ea31d-9c81-4224-b03a-ec465a9da8f0 | X | Succeeded | 2026-10-12 08:30:27 | discovery | 14 / 1 | 1 |
| 0a12bbeb-40e2-42e0-a87e-6b85a6f2c852 | 9cac0c19-6452-40b7-83eb-2b31f5d7cf16 | X | Succeeded | 2026-10-12 08:32:50 | discovery | 9 / 1 | 1 |
| 45932800-2aac-4fef-90a1-0e6c282cff10 | 5e580f4e-95c4-4a64-9372-d9f974623f52 | X | Succeeded | 2026-10-12 08:45:32 | discovery | 9 / 1 | 1 |
| d904c7f6-5494-4a44-aa4e-14ae140996fb | 254de45f-e75c-46bc-8276-541042c3e647 | X | Succeeded | 2026-10-12 08:51:18 | discovery | 9 / 1 | 1 |
| e4073ef5-1a6c-4c22-87c9-624a7a3ecf5f | 2866cd07-9b82-430f-96aa-141693c46fa6 | G | Failed | 2026-10-12 08:50:59 | deterministic | 0 / 0 | 404 |
| 57268864-b34e-40a0-9556-f2f22abb855a | ac37f0bc-3eb5-4646-8e47-62a587b47787 | G | Succeeded | 2026-10-12 08:56:38 | deterministic | 251 / 3 | 1 |
| 308bbf35-a983-4d68-8def-9e8d729fae12 | e711c7f3-0c8c-4abb-bbbe-c09f63abe005 | G | Succeeded | 2026-10-12 08:57:12 | deterministic | 289 / 3 | 1 |
| b8d379f3-6813-487c-8608-21b2aca06cfc | f9b70fef-95b2-456e-b76a-ffba63ef7bf9 | G | Succeeded | 2026-10-12 09:00:17 | deterministic | 535 / 3 | 2 |
| 3c07676a-7395-41d4-83bb-56b741c4a9fc | f64c5487-9600-4e49-b66f-312833a9aa21 | G | Succeeded | 2026-10-12 09:12:18 | deterministic | 445 / 4 | 1 |
| c962f32c-9bb6-4cda-8f9c-e2d2e9e4b051 | 8960229e-5a21-4ea3-b18f-fa7c7bac23bb | X | Succeeded | 2026-10-12 09:13:32 | discovery | 20 / 1 | 1 |
| 4a35b4b6-e04e-46f3-9a30-64fa6d17d514 | e102ddf1-6c9f-45a2-87c6-fba01f304b58 | X | Succeeded | 2026-10-12 09:25:42 | discovery | 10 / 1 | 1 |
| fbe4b0e6-d537-498d-9435-6b32b758f1ba | b3d5dadb-2732-421e-9550-bb4ec4e23722 | G | Succeeded | 2026-10-12 09:38:07 | deterministic | 258 / 3 | 1 |
| 23fc90c0-9db5-4fe0-96cc-b6993f0448b1 | 42a574f5-2cfa-439f-9816-6a35b25fd4c6 | G | Succeeded | 2026-10-12 09:39:46 | deterministic | 131 / 2 | 1 |
| c5dbda9e-7358-4377-8b67-d71982674ccb | 968767b2-4dbe-4297-9f23-cea3ff935b95 | G | Succeeded | 2026-10-12 09:39:57 | deterministic | 122 / 2 | 1 |
| cdef52d8-ecc5-485f-bff7-1184faa91c68 | 11d8216a-cb20-4b59-a546-0aab76369a1b | X | Succeeded | 2026-10-12 10:06:52 | discovery | 13 / 1 | 1 |
| 4756cf4b-c379-4b14-b4f2-9481ee19ad99 | c22a4683-092e-49cb-bfea-c15123bd58b1 | X | Succeeded | 2026-10-12 10:13:42 | discovery | 10 / 1 | 1 |
| b47d9f18-e87f-41ef-98d9-32b68c225108 | 504b6baf-60bd-4e8f-a423-08cedfffcca3 | G | Succeeded | 2026-10-12 10:23:37 | deterministic | 124 / 2 | 1 |
| f70f4cea-bd4c-4c0c-9c84-4ebe48cfdf76 | 654d8dd4-bec7-4246-a882-ef907b68715a | G | Succeeded | 2026-10-12 10:23:52 | deterministic | 100 / 2 | 1 |
| d1365602-8345-40b8-a7e6-f38d66cd3d0d | 19a96149-1440-48b6-88dd-711917388a15 | X | Succeeded | 2026-10-12 10:33:42 | discovery | 13 / 1 | 1 |
| da085af8-4294-4319-b1d0-b5b582781a41 | 59037fc7-bf27-47ec-b981-1dac77504419 | X | Succeeded | 2026-10-12 10:36:37 | discovery | 12 / 1 | 1 |
| 52c02a24-125f-4bd8-bb3e-a3bc6ecf35fb | 90358ab7-a1bb-41cd-82ad-290393e5c973 | X | Succeeded | 2026-10-12 10:38:02 | discovery | 16 / 1 | 1 |
| bf5659fd-e2d4-4a8e-9a58-f6de75997428 | e6c03c38-2a6b-4829-8a91-2a5f793ac10b | G | Succeeded | 2026-10-12 10:54:38 | deterministic | 292 / 3 | 1 |
| 4e70ffd0-af53-4bd6-85fa-f27a2c8ecaf0 | 4dc797ed-ee83-4e00-9d2e-72e328875b47 | X | Succeeded | 2026-10-12 10:57:24 | discovery | 13 / 1 | 1 |
| b48a7bff-241c-4699-a1c8-31e64bd3b126 | 18ae16e7-c486-44d5-9139-3ca17ae82d69 | G | Succeeded | 2026-10-12 10:58:28 | deterministic | 533 / 3 | 1 |
| feafa656-4ccb-4b0c-9011-6969f9599869 | dfc864e8-41ce-46ad-b5cf-09ba386c51f5 | G | Succeeded | 2026-10-12 11:08:28 | deterministic | 418 / 3 | 1 |
| 12bf506d-843b-4bba-97fc-39ea42923d94 | 3f50b7af-770d-4f44-b9d9-7d4fa5959b79 | G | Succeeded | 2026-10-12 11:08:36 | deterministic | 272 / 3 | 1 |
| fb9c2e7a-2094-4879-a9b6-f94647900710 | 6915fa76-8d11-4cf5-a22c-451490d43d94 | X | Succeeded | 2026-10-12 11:16:36 | discovery | 13 / 1 | 1 |
| ef0bb4ed-e865-4591-9bcf-2937dc2f7a87 | a025d4d0-e2cf-4063-85cd-19359ae0a93b | X | Succeeded | 2026-10-12 17:08:56 | discovery | 15 / 2 | 404 |
| 4b08e5d5-8fa5-4401-98b4-402ab9772312 | 89bbbcfc-f4f4-4177-bb25-aef9ed140464 | G | Succeeded | 2026-10-12 17:08:57 | deterministic | 53 / 2 | 1 |
| fc488148-bd9a-41d7-9217-52f4a5d75a19 | f855d473-d3f8-4874-89be-8e5ae3ba4772 | X | Succeeded | 2026-10-12 17:08:58 | discovery | 12 / 1 | 404 |
| 270e01f3-a89a-4c1c-9f22-6349a7904359 | 660c3605-449e-40b9-9750-7eeb31cfc71a | X | Succeeded | 2026-10-12 17:08:58 | discovery | 14 / 1 | 1 |
| bf188fd1-2681-4770-a667-28af1f9a4123 | 2787f95b-7e30-4eec-8561-32eeb8164c54 | X | Succeeded | 2026-10-12 17:08:59 | discovery | 18 / 1 | 1 |
| dfa1ed5b-7008-4f68-9b37-000f9a5158ed | fdb51447-f266-4f97-b773-bf11e83f1e98 | X | Succeeded | 2026-10-12 17:09:05 | discovery | 13 / 1 | 1 |
| cadf4f62-34dc-4bfa-82e2-2204263c160d | 4c762c57-f31b-4d7e-8f10-9329ea85b27f | X | Succeeded | 2026-10-12 17:14:11 | discovery | 15 / 1 | 1 |
| 40fe843e-210c-4d45-91b9-0f1cbd5b2ca4 | 4df4cff2-dd76-46a6-9ef3-ac257d68f843 | C | Succeeded | 2026-10-12 17:23:10 | exact | 93 / 1 | 1 |
| d5091e82-b00f-44ab-8c1e-265ea6c8d687 | 91e773f8-8ef7-46a0-b6e1-95cefe966760 | X | Succeeded | 2026-10-12 17:28:42 | ? | 8 / 1 | 404 |
| cac7e685-f97a-401e-8e47-2f1f8babc731 | 0fd9defe-5a39-4ac2-ab6b-87926786c9cb | G | Succeeded | 2026-10-12 17:34:59 | deterministic | 176 / 2 | 1 |
| 71060864-a171-4efa-95ee-ae1d223fb496 | 294ac8af-e993-4676-bcbe-6287a818ab9d | G | Succeeded | 2026-10-12 17:40:04 | deterministic | 332 / 3 | 1 |
| 88ffed25-4b28-4409-8a0d-598d8ba040a9 | 373f6cf0-14ad-4570-b3e3-0767aa90fc4d | X | Succeeded | 2026-10-12 17:40:04 | discovery | 9 / 1 | 1 |
| 1a656bd4-4242-4e17-82f1-61c6eb3fa5ed | 5261a352-ec21-4e14-9524-01fbba4f0f9b | G | Succeeded | 2026-10-12 17:41:05 | deterministic | 213 / 3 | 1 |
| b113f1fa-0497-414e-9a80-ae1a8ce288ad | 17a9a102-028f-4d3e-83b9-8897473b330d | G | Canceled | 2026-10-12 17:41:22 | deterministic | 69 / 2 | 404 |
| afb235c4-5663-4231-bef3-2718a18e89c0 | f181d87e-e448-4e7e-962e-985eb2fd7282 | X | Succeeded | 2026-10-12 17:41:34 | discovery | 9 / 1 | 1 |
| da4cdf51-463e-45bd-ace1-99e2c8ec8f8b | e2c5f004-e0e7-4abe-b4fe-af84c57427c2 | G | Succeeded | 2026-10-12 17:48:30 | deterministic | 119 / 2 | 2 |
| 04839ba2-4569-48eb-9870-445321244f5e | f0d0c78a-4b1d-4c71-ae5f-d4a10e83876c | C | Succeeded | 2026-10-12 17:55:55 | exact | 102 / 1 | 1 |
| d8992498-396a-4e3c-b6c7-a2286f9c3524 | edb712fd-9edc-417d-8cfa-1ee74cf601a8 | X | Succeeded | 2026-10-12 17:56:09 | discovery | 10 / 1 | 1 |
| c0bc8977-57c1-4c80-9fef-d10d2b747e00 | 08e00e61-a1d0-46de-a0d7-d71cda663092 | X | Succeeded | 2026-10-12 18:01:19 | discovery | 11 / 2 | 2 |
| 63021462-81d9-48e2-9a26-37c23752ea53 | cce2aeef-324e-43bd-81a8-17a0f61aeb1e | G | Succeeded | 2026-10-12 18:07:19 | deterministic | 151 / 2 | 1 |
| 7bbab731-740e-4e1f-b24c-3966ec446aff | 9bcf5567-2db3-484a-8f92-9b5f919f2ed3 | X | Succeeded | 2026-10-12 18:09:34 | discovery | 11 / 1 | 1 |
| 494219e8-65b2-4c80-bb80-d62abb13223b | 1f0deedc-5966-4b1f-82d4-e437f0881ad6 | X | Succeeded | 2026-10-12 18:13:15 | discovery | 9 / 1 | 1 |
| 75216093-be59-4d98-848f-46c8917cc682 | 4e3d16a4-d4b8-42f9-8559-e4612e0cacf5 | G | Succeeded | 2026-10-12 18:13:24 | deterministic | 163 / 3 | 1 |
| f59ffbd2-aa74-44d5-9e24-428eaa98439c | 74856666-57cc-4a81-88cb-9f641c9d60ae | G | Succeeded | 2026-10-12 18:22:14 | deterministic | 317 / 3 | 1 |
| 768488ed-a8b0-4a4b-ac20-8aed086ba76a | 28bc2d02-6db7-4e4f-8cca-ed93a4085f97 | G | Canceled | 2026-10-12 18:25:29 | deterministic | 340 / 2 | 404 |
| a7663fe9-1009-4ba9-aea0-7e5ac214dbb6 | 08f214bb-1a07-4b48-b17f-60dac170e131 | X | Succeeded | 2026-10-12 18:27:14 | discovery | 12 / 1 | 1 |
| 04c02708-9096-44fe-ba49-7659361231de | fba60f4e-2260-44ea-8267-fdbab3dc890b | G | Succeeded | 2026-10-12 18:28:04 | deterministic | 247 / 3 | 1 |
| 7188a896-5708-433f-b7fe-c376ca71b769 | 85c158ab-e834-4ae5-994e-641cbe2f51f9 | X | Succeeded | 2026-10-12 18:44:04 | discovery | 8 / 1 | 1 |
| 53992a72-f43b-4e78-84aa-cb7245a0c999 | fca781ae-ae23-4b85-a3a6-7872567fe5b0 | X | Succeeded | 2026-10-12 18:46:04 | discovery | 14 / 1 | 1 |
| 2c70c12c-5bd5-4e21-b883-9b75c485a516 | 5b32295f-214c-48ca-94c2-17df6de7a868 | G | Succeeded | 2026-10-12 18:48:19 | deterministic | 200 / 3 | 1 |
| f13b23e4-5393-4d92-b0f2-a5615f56751d | b3d47b7b-ac45-435c-9335-5ced33cfe469 | G | Succeeded | 2026-10-12 18:51:49 | deterministic | 208 / 3 | 1 |
| 535e4326-21d0-4811-bed5-19ca97c3f4c0 | d5dfb283-6fd2-4471-a5e0-4518bcb9cb03 | X | Succeeded | 2026-10-12 19:03:04 | discovery | 8 / 1 | 1 |
| 9ed5ee58-f534-42f0-856e-2ada79bf9216 | 39904513-e785-42b3-a1e0-056b03c319a5 | G | Succeeded | 2026-10-12 19:04:54 | deterministic | 569 / 3 | 404 |
| 2cf84f6c-c1f9-4c36-abfa-e4302c8951ac | cae9398f-e63d-4149-b55a-95f346d8df52 | G | Succeeded | 2026-10-12 19:06:59 | deterministic | 356 / 3 | 1 |
| 79817d7a-3f35-4fe5-a2ff-c01d8597cd90 | a472d71c-018b-4f84-80ae-54d9250e99f8 | G | Succeeded | 2026-10-12 19:12:26 | deterministic | 369 / 4 | 2 |
| b0e6162a-0ec8-47da-be1f-99d339053c3d | 2f6a6280-6a39-4e4f-9036-070b33297423 | X | Succeeded | 2026-10-12 19:28:29 | discovery | 8 / 1 | 1 |
| addee829-b5e2-4911-aac3-9ac5bad6d649 | ee39ae15-e93f-4f07-a394-c0e5ab57f376 | X | Succeeded | 2026-10-12 19:33:01 | discovery | 10 / 1 | 1 |
| f36e645d-7b79-49bc-a6ba-42ac792f7865 | 3b018f7d-0e75-459d-974c-17f58bf2dcc4 | X | Succeeded | 2026-10-12 19:33:14 | discovery | 19 / 1 | 1 |
| c053b948-067e-4d73-8a85-3c96cd905425 | d1a3102e-183a-4524-99ba-85f0b9e87554 | G | Succeeded | 2026-10-12 19:38:14 | deterministic | 84 / 2 | 1 |
| 7e66b5f2-371a-46a4-afe4-1a15032dce1c | 737722bc-7237-4c29-9a74-a609d6be4aa9 | G | Succeeded | 2026-10-12 19:47:05 | deterministic | 196 / 4 | 404 |
| 2c9d87dd-f298-4865-af78-55db702612c6 | fb370228-5266-43ae-9223-96451b7fc5d6 | X | Succeeded | 2026-10-12 19:51:14 | discovery | 12 / 1 | 1 |
| b5a3ee2f-a001-4518-a4d0-e97e3e0b11fa | 2871f038-d253-4c33-b2e8-1ebd46b12dea | G | Succeeded | 2026-10-12 20:07:18 | deterministic | 141 / 2 | 1 |
| e6aea1d5-5b0b-4e57-89dc-681cdf200dc7 | e0822f66-6bfe-46de-8c32-7f690e2855dd | X | Succeeded | 2026-10-12 20:16:30 | discovery | 10 / 1 | 1 |
| 108a9cea-473e-443d-8f65-07d75abfb5a0 | ec296cb9-187e-45ca-8c56-a0e2509bdbc3 | X | Succeeded | 2026-10-12 20:23:37 | discovery | 18 / 1 | 404 |
| 04a4131f-c41b-457a-add1-456ed55729f2 | 9744870b-c8b9-4631-be50-6c8b124e9fad | X | Succeeded | 2026-10-12 20:24:22 | discovery | 14 / 1 | 1 |
| 0882c3c7-2a9a-4f0b-9b4b-0cde75fe73b4 | ce70d496-50bf-4572-91ed-cf51d6f1ae46 | X | Succeeded | 2026-10-12 20:30:57 | discovery | 14 / 1 | 1 |
| ef734e84-7f50-4bd2-b7d2-202e0e7151aa | 1fc08acd-2ae2-4b64-ab9a-ed30bf701339 | X | Succeeded | 2026-10-12 20:32:19 | discovery | 24 / 1 | 1 |
| adf0e6ba-7252-47b1-9904-51eddb4c433c | bdec0f3e-db09-4852-801f-588783a86600 | X | Succeeded | 2026-10-12 20:32:32 | discovery | 10 / 1 | 1 |
| 5d2d0e80-9ea9-4f4f-b22a-d22b017263ff | d6ccbd4f-0f06-40c1-ac5a-5fabff30e407 | G | Succeeded | 2026-10-12 20:42:58 | deterministic | 152 / 2 | 1 |
| 97835e71-d736-4caf-a38c-c945356ce697 | 8357bee2-2588-474d-91e4-84e093cf22e0 | G | Succeeded | 2026-10-12 20:44:12 | deterministic | 297 / 3 | 1 |
| 3ac09955-5633-4882-b451-de8166d4bf48 | ffecc158-b123-4230-b360-95432a4cc849 | G | Canceled | 2026-10-12 20:58:47 | deterministic | 740 / 2 | 404 |
| a3dcaef9-36fa-4ef2-b060-1cbef3fa3376 | 4eafb2ee-1af8-4ec2-9eff-7b28dcca1f75 | X | Succeeded | 2026-10-12 20:59:28 | discovery | 42 / 1 | 1 |
| f88d5ef7-5508-4028-9f81-e6f95f348286 | b6a6ed4f-f2e8-4585-a1f6-ae7e2270a36c | X | Succeeded | 2026-10-12 21:00:07 | discovery | 12 / 1 | 1 |
| c0885df4-6e1c-43dc-9e2a-755a8e0b2de7 | a7312c29-0262-4932-a52f-8d4ff06b2b43 | X | Succeeded | 2026-10-12 21:02:27 | discovery | 11 / 1 | 1 |
| cec16163-6270-429e-9669-289fea3072e0 | e8100e06-a4c2-491e-9cdd-ecc89fa5133c | X | Succeeded | 2026-10-12 21:10:46 | discovery | 9 / 1 | 1 |
| ce4980da-9fb7-4ddc-821a-1ae22299bdf6 | 0538cbb9-a252-45d9-ba15-01ad6b40d2bf | X | Succeeded | 2026-10-12 21:18:46 | discovery | 19 / 1 | 1 |
| bcba8cf7-857b-4e27-8afb-0f4eeb652558 | f6260665-a584-47f1-973a-8e259757a3b9 | G | Succeeded | 2026-10-12 21:18:58 | deterministic | 304 / 3 | 404 |
| 19d66290-1d75-45e5-bf29-8edff30e53db | 9db29586-8afb-410a-92df-b68be2801446 | G | Succeeded | 2026-10-12 21:20:38 | deterministic | 160 / 2 | 1 |
| 12b3b0bd-2011-4867-b833-a3dac20e16ff | 5d3e10c2-6d83-4e11-9a64-c18282a18a11 | X | Succeeded | 2026-10-12 21:28:38 | discovery | 14 / 1 | 1 |
| e07d1562-19e1-4f10-bb19-b2153bb47636 | cb558371-699a-4a84-989c-55d303a48fc9 | X | Succeeded | 2026-10-12 21:45:46 | discovery | 15 / 1 | 1 |
| 2086c5d2-b597-4f6d-b444-5efcf7c5fdfe | f63a37c7-e256-4129-b227-e3f295578e34 | X | Succeeded | 2026-10-12 21:46:43 | discovery | 10 / 1 | 1 |
| c0d59c0d-99e8-40a6-87e2-6488ec9edd69 | f05529a3-ac58-4570-9605-4064d527bab4 | X | Succeeded | 2026-10-12 22:02:05 | discovery | 13 / 1 | 1 |
| 6d69efc4-d755-4b24-8529-4ac30cdfdf98 | 8b40d116-78ca-4afb-ad2d-14eb889e9ad3 | X | Succeeded | 2026-10-12 22:02:46 | discovery | 24 / 1 | 1 |
| d8173c35-1497-47bb-a81c-5985353bc88d | f78034bd-4159-40ba-95fe-1cefec496b4e | X | Succeeded | 2026-10-12 22:03:17 | discovery | 16 / 1 | 1 |
| 250706b1-5eca-4553-b70c-72538054b89c | 5f670332-6c5b-4302-b3d9-aed35bdd1365 | X | Succeeded | 2026-10-12 22:08:31 | discovery | 16 / 1 | 1 |
| 0ee48ce0-372f-4a8d-af03-18197f3e85d7 | 139ef54f-34f7-46fc-bb82-d869987d54f2 | X | Succeeded | 2026-10-12 22:26:30 | discovery | 20 / 1 | 1 |
| 39bf31d8-66a4-4012-835f-a9a64e8c54e8 | d328ae8a-9296-44fb-9e1d-259a5f9c8ac1 | X | Succeeded | 2026-10-12 22:26:58 | discovery | 17 / 1 | 1 |
| f1ece864-3e3d-407e-8e28-b7707a208c7e | e3211fa6-1d93-4ca4-a71e-10737f28fd13 | X | Succeeded | 2026-10-12 22:27:50 | discovery | 28 / 3 | 2 |
| 54ad209e-df48-4ac1-9fe8-e05fd928c3ba | 803d4e48-4439-4eb7-a416-0929ff64bb95 | X | Succeeded | 2026-10-12 22:28:03 | discovery | 15 / 1 | 404 |
| 96f3df54-eecd-42cd-a678-d21f49c19e43 | 682f5a57-ac1a-4b7f-884b-bb1fe5c8ea36 | X | Succeeded | 2026-10-12 22:43:09 | discovery | 11 / 1 | 1 |
| 49cee229-cccf-4880-9986-a776ace3ee1e | 6e11f31f-3dff-4b37-9e73-d21660bd981c | X | Succeeded | 2026-10-12 22:52:53 | discovery | 10 / 1 | 1 |
| aa9cb8eb-96eb-4e44-bfd5-116286ac8466 | 6c823bd9-f316-48cf-b182-ef9c05405caf | X | Succeeded | 2026-10-12 22:56:53 | discovery | 15 / 1 | 1 |
| 415fcb98-0842-4103-a3f4-4e72f85782a4 | da5ae8b2-d3c8-4642-8e7c-143dd4cd61fb | X | Succeeded | 2026-10-12 22:57:47 | discovery | 20 / 1 | 1 |
| ed4b2c88-1d69-484f-afc8-d73542bab46c | ac5339c8-852c-43ee-9cb4-28f1f757b8e7 | X | Succeeded | 2026-10-12 22:57:53 | discovery | 22 / 2 | 404 |
| 2ec97f2d-9f93-4c99-bf17-aa5d558dddcf | fcff4083-32ba-4347-872e-7d25daa1788c | X | Succeeded | 2026-10-12 23:01:33 | discovery | 12 / 1 | 1 |
| 8da91605-c82f-48ca-ae26-b177d622cd67 | e1b7d0cf-44ad-465f-bfcf-d99c092c3245 | X | Succeeded | 2026-10-12 23:08:50 | discovery | 10 / 1 | 1 |
| 8295e472-263a-4179-ab03-eaa7953e84cd | 4bbd9f59-8325-4a92-b5d4-cf85b96ada07 | X | Succeeded | 2026-10-12 23:14:55 | discovery | 8 / 1 | 1 |
| 626eec47-1a8a-4e4b-9ad7-b70c592c9607 | b812a307-a68b-420b-aff0-8aa3d2b1272d | X | Succeeded | 2026-10-12 23:20:20 | discovery | 9 / 1 | 1 |
| d25ba49f-2fde-4413-8cba-a3ad72165dc7 | 21319e8f-b48a-4fc7-a4e8-4c695bc07e62 | X | Succeeded | 2026-10-12 23:21:15 | discovery | 12 / 1 | 1 |
| d3cc8360-d502-4525-8f0b-b161e1adc4b4 | 8a0d64b7-43df-4f3a-be07-355bd6d4ce26 | X | Succeeded | 2026-10-12 23:23:17 | discovery | 23 / 2 | 404 |
| c3814b00-688d-421c-a2f7-c90fdd757db6 | 810b4555-d81e-439d-b373-461c469e942f | X | Succeeded | 2026-10-12 23:29:58 | discovery | 30 / 1 | 1 |
| bc957ed9-9a6a-4503-a99d-fce2f677d4ec | b6fd310b-017a-4636-90f0-0765814800fe | X | Failed | 2026-10-12 23:30:32 | discovery | 47 / 1 | 1 |
| 71d0c64a-a2db-48d1-94f3-bfcd56303547 | 00d5c075-c4dd-457d-845a-4068ebc5ee2d | X | Succeeded | 2026-10-12 23:30:48 | discovery | 7 / 1 | 1 |
| 0a3d75f7-5f69-4f59-90d0-3bae67dbc65c | 280969fd-728b-42fb-91d3-6dd5465372f2 | X | Succeeded | 2026-10-12 23:33:04 | discovery | 16 / 1 | 1 |
| 6bfc22bf-5f8c-4444-b1dc-abeb792ad18b | e7d8f50e-b193-455c-b8b2-fcd17b4055f2 | X | Succeeded | 2026-10-12 23:34:53 | discovery | 8 / 1 | 1 |
| 6c9fe082-cfe7-48bc-9d40-a9a58918da4f | 59cb8a9d-5562-4a32-960f-5e306a3fd251 | X | Failed | 2026-10-12 23:45:28 | discovery | 27 / 1 | 1 |
| 1527665f-fc65-4d96-b7d4-5658686f4adf | ae813d84-8228-4e49-afb7-ce720ff42abe | X | Failed | 2026-10-12 23:45:58 | ? | 32 / 1 | 404 |
| 16459e79-5010-458e-adf4-0e03b65b98ee | d73e52fb-ef99-4108-9571-64d9f5a54027 | X | Succeeded | 2026-10-12 23:52:52 | discovery | 17 / 1 | 1 |
| 000c569b-a55c-4c5b-a9f5-f52a9125fced | cd366374-a2a5-4a26-b046-e5b7246eaf4e | X | Succeeded | 2026-10-12 23:56:50 | discovery | 12 / 1 | 1 |
| c533d6c6-3a11-4b45-b851-f27c39ed0b92 | f760f93e-8e50-49d5-a355-098e285a80da | X | Succeeded | 2026-10-13 00:08:18 | discovery | 14 / 1 | 1 |
| cbce1a3f-5d59-47d2-a8a3-859ca571c16f | b7fb86e3-f131-43d2-bdec-fde4aaf7e641 | X | Succeeded | 2026-10-13 00:11:53 | discovery | 16 / 1 | 1 |
| c0c7c3e0-b964-4296-9ca8-6fae9c2021ae | 8b6a00c5-68c9-4a19-acb0-ec7bc1d38e54 | X | Succeeded | 2026-10-13 00:14:18 | discovery | 9 / 1 | 1 |
| 4ede83bf-a652-450a-865b-e8063143be27 | da215c79-3c53-4127-80fe-5b2ec0475a38 | X | Succeeded | 2026-10-13 00:31:52 | discovery | 14 / 1 | 1 |
| dab30ee3-9474-4281-be4d-c293c12faefc | 6711d9f0-ed9a-415f-b82c-357d5d46c648 | X | Succeeded | 2026-10-13 00:35:21 | discovery | 13 / 1 | 1 |
| 842dd560-84d5-482f-a0f4-918ec245e6fe | c2fc152b-2021-4fdc-8796-ee1fb13ccd9a | X | Succeeded | 2026-10-13 00:36:39 | discovery | 19 / 1 | 1 |
| 2cb07437-f49b-459c-a76c-9ea0fbe53ba2 | 142efd22-512e-462b-ad76-76a6c9f226a1 | X | Succeeded | 2026-10-13 00:38:20 | discovery | 22 / 1 | 1 |
| e02647de-f4bf-4b67-b5ff-bc2b2163809a | 03b6fe2f-7f87-452b-8a10-3f73e5b14066 | X | Succeeded | 2026-10-13 00:38:36 | discovery | 13 / 1 | 1 |
| a835b016-6f0c-4886-9ac9-b3651f556ae9 | e9d1ea0a-b307-432b-9131-50fb804537ef | X | Succeeded | 2026-10-13 00:48:32 | discovery | 11 / 1 | 1 |
| efe40115-531a-4664-86d5-c0a87bc899fc | ff581247-233a-405e-9793-0caa3d67f4eb | X | Succeeded | 2026-10-13 00:53:43 | discovery | 19 / 1 | 1 |
| 1baae4ee-3a02-495f-925b-345b0630c01c | cf4d0808-5a63-40d4-9ce2-8ec3a4c42cde | X | Succeeded | 2026-10-13 01:01:10 | discovery | 36 / 1 | 404 |
| 1af8b8ab-e1f4-4fcb-b3ce-61177c79adb3 | 6b635a3b-6757-44a5-98a4-122614b13ae2 | X | Succeeded | 2026-10-13 01:02:26 | discovery | 18 / 1 | 1 |
| f6e00270-2149-45b2-b065-5c0ef10d1f17 | d266bab8-a29f-4578-8c21-a280f5e732b9 | X | Succeeded | 2026-10-13 01:03:23 | discovery | 26 / 2 | 404 |
| 520a9bf3-d0ab-427b-a036-88323a621750 | 21ae1737-f79e-4e8c-a255-1651f23eeade | X | Succeeded | 2026-10-13 01:03:37 | discovery | 27 / 1 | 1 |
| 3df28f8c-7fa0-4848-b8fc-6a4679dd36db | a4dd1808-2dbe-4cdf-8db7-5f8d0c6cc6ae | X | Canceled | 2026-10-13 01:03:51 | discovery | 35 / 2 | 404 |
| 71c428e1-835e-47ec-bee9-e5e990f3a4bd | 9ba1ddd3-4931-4acb-8268-a2d58dd336b1 | X | Succeeded | 2026-10-13 01:26:31 | discovery | 15 / 1 | 404 |
| e4b3ff1c-4bb2-4b9b-ac11-8982cca19ff3 | 2058f6a9-0f3d-43f6-b634-3df8065657b9 | X | Succeeded | 2026-10-13 01:28:08 | discovery | 10 / 1 | 1 |
| 93ad5325-7695-4545-b2e0-8877d08f4021 | 725619a7-111e-4330-b27a-ece0aa5c8762 | X | Succeeded | 2026-10-13 01:39:16 | discovery | 10 / 1 | 1 |
| c4cbce6e-3cc3-4fe4-a739-0eb47195cafe | 3965cbec-d4bb-4e29-b8c7-f403f7401107 | X | Succeeded | 2026-10-13 01:51:39 | discovery | 17 / 1 | 1 |
| b1b731e0-1d07-4589-8c85-94b420b101bd | b0921f1c-39a8-4dac-b610-e5d2f7c560f6 | X | Succeeded | 2026-10-13 01:51:40 | discovery | 23 / 1 | 1 |
| 81eb612a-3cf4-454e-9e1c-c3be6c63c59b | 4567769f-f8dd-455f-a48e-c4f75ca74095 | X | Succeeded | 2026-10-13 01:51:41 | discovery | 20 / 1 | 404 |
| c7cf7371-378c-4e1a-8e1f-bd1697670972 | 421af342-d65e-4ac0-8b5a-6e15d908b682 | X | Succeeded | 2026-10-13 01:52:25 | discovery | 19 / 1 | 404 |
| 3e736c72-542c-472a-b6c9-1dcd367c7a36 | 8bd74579-daa2-4bbb-9048-9eb2f7803f60 | X | Succeeded | 2026-10-13 01:52:41 | discovery | 24 / 1 | 404 |
| 8db33c5e-6fb9-4e26-93ad-952f33475461 | ced558f7-f665-4867-8f00-9d58e456c677 | X | Succeeded | 2026-10-13 01:59:15 | ? | 13 / 1 | 404 |
| 8bcfa5da-1c58-477c-bf97-65f83a299509 | 7fe9623f-f588-4a3c-8812-795764490873 | X | Succeeded | 2026-10-13 02:33:56 | discovery | 19 / 1 | 2 |
| 3f0e5491-2cf8-4b0c-a590-bd79ee1f4052 | bb87a72c-37bd-49c6-9aea-58c7867b396f | X | Failed | 2026-10-13 02:33:57 | discovery | 17 / 1 | 1 |
| 578ae84e-829d-4da6-b4bc-2354e3bb876c | 44510689-ba8f-4044-9c7f-7b9796ba3951 | X | Succeeded | 2026-10-13 02:35:02 | discovery | 24 / 1 | 1 |
| 6c3dfed9-d7bf-412b-a0c3-8d71ce0dc945 | 429e7ccd-4b6a-45b5-93bc-edbc7fb702c8 | X | Succeeded | 2026-10-13 02:35:19 | discovery | 14 / 1 | 1 |
| 0734866e-67cc-4da9-a382-2d19613b4c77 | 7bcc66ca-5f5e-46b8-b74e-3e7f5c3c48df | C | Succeeded | 2026-10-13 02:35:34 | exact | 147 / 1 | 1 |
| e99f75c4-ca49-4dbf-9bb2-34c1289d30ca | 86da86ff-99e4-4548-b2d9-6bd50a662672 | X | Succeeded | 2026-10-13 02:52:36 | discovery | 10 / 1 | 2 |
| 80f776ce-76cc-4006-bbc1-abed5c21e7e8 | dd5ac31b-27d4-4d94-ab5a-c571cd425c6b | X | Succeeded | 2026-10-13 02:52:49 | discovery | 15 / 3 | 2 |
| 080e0111-220a-4633-bc3e-efa40d4016ff | 52864143-fc3d-4ebc-bc49-ffa13719baf3 | X | Succeeded | 2026-10-13 02:57:54 | discovery | 19 / 1 | 1 |
| 0039718c-042e-41bf-a257-4d988f79e4b3 | be6ab4c5-efe6-4797-99b4-c2c62be8968a | X | Succeeded | 2026-10-13 03:04:54 | discovery | 12 / 1 | 1 |
| 353dd304-353c-4bd6-ad80-939fb992511b | bd0aeefc-1782-4562-8dec-0a4a977801b8 | X | Succeeded | 2026-10-13 03:10:29 | discovery | 19 / 1 | 1 |
| 8c713d0c-fe3f-4097-894b-9fc892a709ae | e0a8b7c2-7417-449e-9905-a3cb58dae7d9 | X | Succeeded | 2026-10-13 03:18:11 | discovery | 23 / 1 | 404 |
| 604cc024-353a-46d8-870d-e4d5a00b4695 | 65a4e5fa-b228-4dec-9d87-f6c44e4b624d | X | Failed | 2026-10-13 03:18:24 | discovery | 46 / 2 | 404 |
| ebf0a2f0-49cb-4525-95b7-1d2dcffecd2f | 18caef95-f753-4a6f-87d4-5fc99f640bcc | X | Succeeded | 2026-10-13 03:22:19 | discovery | 17 / 1 | 1 |
| f6c26cd9-cbd3-42c7-b154-9d7de2707532 | 85e66a8d-c6be-4bac-bc46-1ee583ffe92f | X | Succeeded | 2026-10-13 03:26:26 | discovery | 13 / 1 | 1 |
| e37a97b6-e6b9-4f2a-9585-a93d27dcd52e | b32aaf70-1ea9-4f82-b153-f8d681189f6e | X | Succeeded | 2026-10-13 03:41:34 | discovery | 45 / 4 | 2 |
| 7b7d644a-a776-42e8-bc12-3d57e5df5ace | 9d16f51d-6cc9-4fcb-a954-3f1bbb4eae4c | X | Failed | 2026-10-13 03:58:34 | discovery | 36 / 5 | 1 |
| 059e44d6-18b4-4ef2-b14e-5dc1211201cd | a5cc8b6f-eb4e-441b-a3a1-71a44f2cb557 | X | Failed | 2026-10-13 03:59:29 | discovery | 50 / 3 | 404 |
| f5ac8bb8-83eb-4e72-a974-d64a34cd3368 | 7fed0188-0626-44d6-ab37-2624670eb41c | X | Failed | 2026-10-13 03:59:35 | discovery | 14 / 1 | 404 |
| 9694c487-61bb-4cbe-9beb-40d8d31aad2b | 05069602-3e53-40ad-b970-eadafbf4d793 | X | Succeeded | 2026-10-13 07:05:48 | discovery | 18 / 1 | 1 |
| 4da2d6db-7b6a-4c58-aa47-4062e234712f | 37b30d53-05bd-439e-b1e8-d600d48acea7 | X | Succeeded | 2026-10-13 07:06:24 | discovery | 19 / 2 | 2 |
| 48fbe811-6df9-4abb-93fe-803cc1459779 | b22a76dc-f648-4fad-a05d-fc6d2ff005a9 | X | Succeeded | 2026-10-13 07:19:39 | discovery | 12 / 1 | 1 |
| cea60832-c7da-494a-adf9-e9de9fa5063d | 63346b1f-2a20-486c-a55a-65f44f1e620e | X | Succeeded | 2026-10-13 07:23:54 | ? | 20 / 1 | 404 |
| 84176c0c-a32e-483d-8c6c-519ea0680743 | 50629b0a-40c5-48f6-90a2-fc1068b42d64 | X | Succeeded | 2026-10-13 07:29:46 | discovery | 5 / 1 | 1 |
| 15a9381e-0e9d-46a0-a49b-9534d34f3eb4 | c60cce6e-a7b9-4125-8685-8bb20b2af301 | X | Succeeded | 2026-10-13 07:36:24 | discovery | 12 / 1 | 404 |
| 5b4007a2-bc1d-42c7-a3fd-fd32f0cd8737 | 3ba6ec35-540f-4d7d-9457-2d205a1f52de | X | Succeeded | 2026-10-13 07:38:28 | discovery | 9 / 1 | 1 |
| 5914f175-75f3-4c27-9a4e-e5780c78407a | 381e25e5-583e-455b-bac1-dfe2358c31aa | X | Succeeded | 2026-10-13 07:40:08 | ? | 13 / 1 | 404 |
| cadcf040-9c54-4615-9ee2-ad15e1278951 | 2312fc9d-013d-4b66-94ca-a4a76c7a79cc | X | Succeeded | 2026-10-13 07:42:38 | discovery | 20 / 1 R | 404 |
| 902bc6d6-3b26-465f-bdef-ff32f5920dd5 | 908fda3b-4050-42f3-9549-c7a3bf96d9a9 | X | Succeeded | 2026-10-13 08:00:57 | discovery | 17 / 1 | 1 |
| cc78d17e-004e-4770-86ff-8e94c6639c25 | ec554a4a-7cde-4ef7-811f-d288bcdd0783 | X | Succeeded | 2026-10-13 08:01:12 | ? | 20 / 1 | 404 |
| b56c49d8-21a1-409f-9dd7-8683de0c29ec | cf61b115-bf02-4ab7-8784-dfedb7315cb2 | X | Succeeded | 2026-10-13 08:02:37 | discovery | 36 / 1 | 1 |
| d7a4f178-1a80-40ab-8730-f0be6b07eead | dacc5f9d-fc8f-4aae-836d-4ae1ab3e3a25 | X | Succeeded | 2026-10-13 08:02:40 | ? | 34 / 3 | 404 |
| e5579cd3-2134-46ef-af80-a59aa9210042 | f35eed45-ffc6-4f56-b02e-478803aaf2a2 | X | Succeeded | 2026-10-13 08:14:16 | discovery | 21 / 7 | 2 |
| a7f248c0-cf34-455c-b4ac-bdc4fc638e19 | 3d9e7ccb-7144-4066-b0e8-32a72c53462b | X | Blocked | 2026-10-13 08:26:58 | discovery | 31 / 3 | 0 |
| c162e333-7995-4db9-bbab-9f19e340b022 | 96a611a5-4f9c-45fd-a957-75e6b781d616 | X | Succeeded | 2026-10-13 08:33:08 | discovery | 9 / 1 | 1 |
| 722d7d90-5172-4b89-afbf-b7c1506fb893 | e3cbd260-2c02-4286-9583-7ece67b8d6f0 | X | Succeeded | 2026-10-13 08:38:49 | ? | 16 / 1 | 404 |
| b80e5c9e-cdcb-4f3d-9d09-a4c612bf0a08 | 474ebd53-b534-4873-bd84-823cc87352ad | X | Succeeded | 2026-10-13 08:42:53 | discovery | 21 / 2 | 2 |
| e3c11469-712c-4d93-8685-f1cf5c78e44d | 150ea52c-8f3e-49f7-b16a-1ec54c4ec640 | X | Succeeded | 2026-10-13 08:43:00 | ? | 26 / 1 R | 404 |
| 443a4222-e79e-47d1-85c2-ea6ada76848c | 02ce3f4c-f663-4e45-9df7-9aa1c2b4a758 | X | Succeeded | 2026-10-13 08:58:45 | discovery | 14 / 1 | 404 |
| 0ced9d05-9c38-478e-81ba-3dc9c9375223 | 32e1dda9-3841-43df-a423-82752f1aecbe | X | Succeeded | 2026-10-13 08:58:48 | discovery | 17 / 1 | 1 |
| 123472ac-c7f0-4039-8a87-8b1e6f4f0053 | 692848c9-18a8-4f61-ad52-618821a8545e | X | Failed | 2026-10-13 08:58:51 | discovery | 7 / 1 | 1 |
| 4e06595f-32f4-4229-b6a9-f47264982e26 | 490e7e70-799b-4ab0-bc58-2c02cd362f59 | X | Succeeded | 2026-10-13 09:15:53 | ? | 14 / 1 | 404 |
| 6284d6ec-03f7-4a88-b9f5-12a601bfe017 | 29103669-c1fb-41ac-8636-6a3ead9e7cd2 | X | Failed | 2026-10-13 09:25:40 | discovery | 35 / 1 | 1 |
| 650b7652-d2e6-490f-8d71-3601bf06f13b | 68a3213b-d008-45c6-ba16-961ca6410910 | X | Succeeded | 2026-10-13 09:27:28 | discovery | 45 / 3 R | 1 |
| 9bd508ad-1f80-48bf-84af-1c2385b2c843 | 8fe8022d-e54b-4114-bfb7-637540664317 | X | Succeeded | 2026-10-13 09:27:37 | discovery | 13 / 1 | 1 |
| 54eb0a67-2902-43d2-84e0-6ca87e0d39b7 | ab61757b-2254-4aa7-b989-a345fc7bda24 | X | Succeeded | 2026-10-13 09:30:57 | discovery | 17 / 1 | 1 |
| c0e86d50-00e6-4942-b303-7bc44122c640 | 07f75ae7-e615-4211-abf5-86723faa9c21 | X | Succeeded | 2026-10-13 09:58:12 | discovery | 58 / 4 | 2 |
| 4e70a385-83c4-483c-a53b-4d5963de1a43 | 62f436ba-c780-4be0-90b5-8eee3278191b | X | Succeeded | 2026-10-13 10:26:57 | discovery | 16 / 1 R | 1 |
| 47c95281-78bf-412d-9c98-26c4071fa039 | 267d4c37-a7ef-42fb-b470-2501032dd8bd | X | Succeeded | 2026-10-13 10:26:58 | discovery | 22 / 1 R | 1 |
| 361919bc-8ec6-4a1f-a430-f22755eeeba9 | e6e6c6c3-667a-4d2c-a0a5-f176f0216ba6 | X | Blocked | 2026-10-13 10:27:01 | discovery | 6 / 1 | 0 |
| f3302b7a-cae8-49be-a0ff-95bbb13a3666 | 4d43c006-a252-4eda-8920-2c0a20289c54 | X | Succeeded | 2026-10-13 10:33:36 | discovery | 34 / 1 | 1 |
| 78ecd9b2-602a-4e8c-ad30-f2d39cba4d96 | 46edc3c0-1a29-4a91-a1c4-7c34381f2345 | X | Succeeded | 2026-10-13 10:33:50 | discovery | 10 / 1 | 1 |
| c05e150e-c3d0-47b3-82fa-26a512f52ddb | 1f510343-8397-4abc-9839-ce76829e1600 | X | Succeeded | 2026-10-13 10:50:26 | discovery | 18 / 2 | 1 |
| cff0cd4f-e5d2-4410-8d0e-03e4d6062f1e | e0eb5279-4683-4550-993d-068c471a5909 | X | Succeeded | 2026-10-13 10:52:06 | ? | 14 / 1 R | 404 |
| 60ccb73c-1fdd-49be-818e-79817a9c4c8e | eec32d89-1b13-44bf-8650-0bbd2c958912 | X | Succeeded | 2026-10-13 10:52:50 | ? | 23 / 1 R | 404 |
| 139dc9f6-9060-4dcc-8448-0a4ba7d3e010 | 6b3b5abf-7ea6-4b6d-a502-f1f4fe0307d2 | X | Succeeded | 2026-10-13 11:12:22 | ? | 22 / 2 | 404 |
| 31712551-01d9-4034-8cf6-cf577ebfa279 | e66997ba-132f-414e-b3f3-d8e884685c54 | X | Succeeded | 2026-10-13 11:17:30 | discovery | 17 / 1 | 1 |
| 46f143ee-a27e-43c8-9752-8628087c52dc | cc521bac-9dde-430c-8306-5cd27862679f | X | Succeeded | 2026-10-13 11:31:10 | discovery | 12 / 1 | 1 |
| b01076cd-6882-42d3-8887-304a5f0ab5ff | 5623c98b-cee0-4ad8-813b-f75ab144ed86 | X | Succeeded | 2026-10-13 11:31:15 | discovery | 9 / 1 | 1 |
| 044d2836-88d8-4c8d-b237-15f329afba07 | b34d2674-5315-42fa-9611-742dd1690397 | X | Succeeded | 2026-10-13 11:32:54 | ? | 27 / 2 | 404 |
| fab24275-0c57-4a40-9801-b4fb01551a5e | 95be904f-9d68-4925-8661-36f5ae2badd7 | X | Succeeded | 2026-10-13 11:40:51 | discovery | 18 / 1 | 1 |
| f0995c2a-9679-4c21-a518-ce2b6c8a30f0 | 1d4b15b7-3857-47d9-a9a1-404b27b96e8d | X | Succeeded | 2026-10-13 11:51:16 | discovery | 24 / 2 R | 1 |
| 2a33144d-0f31-4ca1-a086-bbde8fee6edd | 9bdee181-0892-4bb0-9fdc-299728137086 | X | Succeeded | 2026-10-13 11:57:57 | discovery | 25 / 1 | 1 |
| 070b34c8-f47f-4291-8297-ecabebc0e5ad | 600ac635-4f61-41eb-aa9a-3a9e38671b8e | X | Succeeded | 2026-10-13 12:01:06 | discovery | 15 / 1 | 404 |
| 2fdae085-5429-491b-a31b-c0b547e2db48 | 3d10152f-dbc8-4046-8a7f-e336d43d8e96 | X | Succeeded | 2026-10-13 12:02:29 | ? | 12 / 1 | 404 |
| 4512c736-18aa-4c32-88df-43381bf70905 | d2f43e16-5d1f-4a94-8dc4-3e6843686c6c | X | Succeeded | 2026-10-13 12:28:04 | discovery | 8 / 1 | 1 |
| 6327ca04-343e-4286-8974-c1a9d0477e29 | faebccf4-9e53-48d9-9d0c-0da4fc38481c | C | Succeeded | 2026-10-13 12:32:55 | exact | 119 / 1 | 1 |
| 2be1191d-082a-4820-ba16-cb58fdf27379 | dce10bf5-c930-49b0-8608-2fdf5cbcfd39 | X | Succeeded | 2026-10-13 12:34:47 | discovery | 25 / 1 | 1 |
| 18cb003f-0f76-4aa7-8387-992931ac7192 | 1b2bb84c-dca6-41d2-8145-a14a27759760 | X | Succeeded | 2026-10-13 12:34:54 | discovery | 21 / 1 | 1 |
| ab9eb94e-1e89-45f0-9305-b2c57b13aa2f | 714add16-f0d1-4088-b575-593eea0a1197 | X | Succeeded | 2026-10-13 12:44:51 | discovery | 12 / 1 | 1 |
| af6a833c-f0b7-4981-8ce4-4b511a7505fe | 09958cc0-d8dc-4f3a-956d-81c721d09e61 | X | Succeeded | 2026-10-13 12:44:56 | discovery | 43 / 1 | 1 |
| 3a8d77b0-6cb6-4973-9d4c-3bb3d3e2d3b1 | 7b0486a1-f156-4b30-ae8a-22d138806b36 | X | Succeeded | 2026-10-13 12:50:15 | discovery | 17 / 1 | 1 |
| bb8f5555-6d60-4b54-9337-112a82914f5f | 740a8547-e526-4366-b4bd-c50ec43ee161 | X | Succeeded | 2026-10-13 12:51:29 | discovery | 10 / 1 | 1 |
| 88db4c4b-36e6-4b43-b403-ac6bef76210f | 33893b70-b1e2-4e57-9cf6-a4765ca49be6 | X | Succeeded | 2026-10-13 13:02:32 | ? | 9 / 1 | 404 |
| 3280a044-57d9-40e5-a1dc-c3c72ed31db1 | 0771434e-de34-44d7-9cda-53c534b6f874 | X | Succeeded | 2026-10-13 13:04:04 | discovery | 12 / 1 | 1 |
| ba777bcd-6ccc-4209-8d42-82067a4fab83 | 71e2049c-4399-4574-8f3f-4174bde68bc4 | X | Failed | 2026-10-13 13:14:16 | discovery | 26 / 1 | 0 |
| 2bf25028-d549-45b5-81aa-f2896dee8cbe | 35ebae63-6ce1-4bfd-adcc-0939d0f901f1 | X | Succeeded | 2026-10-13 13:22:56 | discovery | 10 / 1 | 1 |
| 3ebab2a8-b7c2-4065-921c-b6775d8bbe38 | b29eea29-52ed-46bc-954e-1717713d89bc | X | Succeeded | 2026-10-13 13:26:20 | discovery | 19 / 1 | 1 |
| a8b33bf7-3165-49e8-8151-2e217d7f6b16 | 9a37e903-f3c0-4e78-8c31-20d1c0cc3484 | X | Succeeded | 2026-10-13 13:28:46 | discovery | 23 / 1 | 1 |
| f2b2ece7-37bf-4c83-9b74-cfb9e78c1eae | c034cb64-4cb1-4579-b69b-8620b60a5558 | X | Succeeded | 2026-10-13 13:51:48 | discovery | 28 / 2 | 2 |
| 5c3f0366-48a5-4a7f-8e14-75501670e43f | aa939ffa-d8e1-4324-b7f1-8537c84875c6 | X | Succeeded | 2026-10-13 13:54:33 | discovery | 19 / 1 | 404 |
| 29f3946d-24ca-453b-b6ef-991c8fbf74f3 | 852deefb-8a4f-461c-a006-c125c700feb8 | X | Succeeded | 2026-10-13 14:07:53 | ? | 31 / 2 | 404 |
| 6f4c2f0d-9b2f-4e18-976f-f6b9adc6a38c | afaef049-7fee-4f2d-b6ac-8176f81545b1 | X | Succeeded | 2026-10-13 14:11:54 | ? | 15 / 1 | 404 |
| d2396ae2-d07b-47bb-8348-3e8b42d7fb63 | a9fb69f3-5b40-4fcb-899b-465b81af49b4 | X | Succeeded | 2026-10-13 14:24:13 | discovery | 17 / 1 | 1 |
| 39dd2763-ffdc-4e56-9b62-0fb674f2aa10 | b402dafe-bb15-425e-94c3-9bee4fdc3026 | X | Succeeded | 2026-10-13 14:37:58 | discovery | 23 / 1 | 1 |
| 9f9613bd-209f-4527-8168-53d940fc2035 | 2d5ad5f8-ee28-4dfe-80bc-cf96281bb295 | X | Succeeded | 2026-10-13 14:42:43 | discovery | 16 / 1 | 1 |
| aba5b950-a8c2-46c7-83c5-f10d5f6a7d26 | d175383a-5135-461f-a22a-d3c4d7175055 | X | Succeeded | 2026-10-13 15:14:31 | ? | 9 / 1 | 404 |
| dbb9a32f-0026-4af8-ab19-1f4baae9d28b | fa11e09c-cfb1-4fee-9f08-93683bcc5332 | X | Dispatched | 2026-10-13 15:22:20 | discovery | 24 / 1 | 0 |
| 4eee4798-762f-410b-8da7-d9d051f4c54f | 3ab68457-8ddc-41ee-aff8-1b640af29079 | C | Succeeded | 2026-10-13 15:25:23 | ? | 24 / 1 | 404 |
| e0b648d4-14a5-472b-a6ad-df499dedde0a | 79e59441-970c-4ec4-a88f-57cd4db4544e | X | Succeeded | 2026-10-13 15:29:24 | discovery | 17 / 1 | 1 |
| 045e9719-3246-44af-b147-580847b16a44 | d63c82cf-c87e-4a0e-ad75-5dfdd0f3cf97 | X | Succeeded | 2026-10-13 15:30:07 | discovery | 29 / 1 | 1 |
| 57d43acc-cb85-407b-82c0-1d7c54829ad2 | a5895b05-4f78-48ec-bf9f-42646d8b4624 | X | Succeeded | 2026-10-13 15:30:29 | discovery | 10 / 1 | 1 |
| dfaf5740-9fa0-4a75-912f-e665f8fbca55 | db247849-6d4b-4bad-a025-20d39fdc659a | X | Dispatched | 2026-10-13 15:33:11 | ? | 63 / 1 | 1 |
| fcd1f78e-aa90-489b-b976-700c619cbf37 | 283d015f-7833-41c7-9ce1-70693dcaba5f | X | Succeeded | 2026-10-13 15:43:05 | discovery | 13 / 1 | 1 |
| f3d7a159-32d4-4628-89df-245e845bcfe0 | a0700aaf-ad0f-402b-90c1-f6fbceff8351 | X | Succeeded | 2026-10-13 15:56:40 | discovery | 9 / 1 | 1 |
| c0549b71-c4ad-48d3-816a-81bc6086d797 | 32687302-4863-45f8-a524-da67e1662284 | X | Dispatched | 2026-10-13 16:00:25 | discovery | 31 / 1 | 0 |
| 60025819-39c5-49e6-8951-817cdeeff5fc | d34a4d46-aaaa-47f3-8f52-59a50358bed5 | X | Dispatched | 2026-10-13 16:06:25 | discovery | 14 / 1 | 0 |
| 012c1c9e-d489-47fa-9412-4bfc284a2b29 | 6423eb0d-fcc7-41ec-8960-74c7d7b6bc16 | X | Succeeded | 2026-10-13 16:09:30 | discovery | 7 / 1 | 1 |
| 9761afeb-b3e0-44bf-ae05-b7922c36e25e | bab10246-5b07-4a3e-aef0-62c02d4c1b17 | C | Succeeded | 2026-10-13 16:21:31 | exact | 121 / 1 | 1 |
| fe5c4378-89e7-48a4-98db-3595fc69b11e | 0b33cfa7-3809-47d5-9add-442a9b4e1bdf | X | Dispatched | 2026-10-13 16:32:50 | discovery | 4 / 1 | 0 |
| 6813e7cf-b2c2-4012-8d96-f197eaba9b34 | 867413cc-4e26-4a01-b5d7-102872684091 | X | Dispatched | 2026-10-13 16:33:05 | discovery | 4 / 1 | 0 |

Failure detail ledger (all Failed cohort tasks; reasons are stored API fields, grouped above):

| Task | Kind | FailureCode | Stored reason |
|---|---|---|---|
| e4073ef5-1a6c-4c22-87c9-624a7a3ecf5f | Grok | null | Session died before the task settled: its session is Failed (Agent process did not become ready.). No report is coming; read session 2866cd07-9b82-430f-96aa-141693c46fa6 before re-running this task. |
| bc957ed9-9a6a-4503-a99d-fce2f677d4ec | Codex | null | The brief requires only the ignored-repository row to fail before the fix. For the dirty-repository control, I’ll use a non-ignored dirty nested repository (expected refusal before and after); dirt wholly inside an ignored independent repository cannot affect outer tracked state. Does that match the intended control? |
| 6c9fe082-cfe7-48bc-9d40-a9a58918da4f | Codex | null | Incomplete: redesign and repairs are pushed, but Final verification needs a rerun. |
| 1527665f-fc65-4d96-b7d4-5658686f4adf | Codex | null | Implemented and pushed the resolver fix; Windows qualification passed. Final verification remains incomplete because Unit timed out twice without TRX. |
| 3f0e5491-2cf8-4b0c-a590-bd79ee1f4052 | Codex | null | Incomplete: Final recorded **168 passed, 34 failed, 1 Windows skip**. Late gate and stale-receipt fixes pass native smoke; the full checkpoint rerun remains required. |
| 604cc024-353a-46d8-870d-e4d5a00b4695 | Codex | null | Ran 4h00m against the 240-minute ceiling for role Code. Last transcript entry: AssistantText, 1m ago. The task is Failed, not escalated and not retried: a bigger model cannot finish a task that never ends, and re-running it is your call. The session was NOT killed — read session 65a4e5fa-b228-4dec-9d87-f6c44e4b624d for what it was actually doing before you decide. |
| 7b7d644a-a776-42e8-bc12-3d57e5df5ace | Codex | null | Ran 4h00m against the 240-minute ceiling for role Code. Last transcript entry: AssistantText, 2m ago. The task is Failed, not escalated and not retried: a bigger model cannot finish a task that never ends, and re-running it is your call. The session was NOT killed — read session 9d16f51d-6cc9-4fcb-a954-3f1bbb4eae4c for what it was actually doing before you decide. |
| 059e44d6-18b4-4ef2-b14e-5dc1211201cd | Codex | null | Ran 4h01m against the 240-minute ceiling for role Code. Last transcript entry: AssistantText, 3m ago. The task is Failed, not escalated and not retried: a bigger model cannot finish a task that never ends, and re-running it is your call. The session was NOT killed — read session a5cc8b6f-eb4e-441b-a3a1-71a44f2cb557 for what it was actually doing before you decide. |
| f5ac8bb8-83eb-4e72-a974-d64a34cd3368 | Codex | null | Found four defects; Final verification is incomplete because the task-owner API remained unavailable. |
| 123472ac-c7f0-4039-8a87-8b1e6f4f0053 | Codex | CompletedWithoutProgress | The delegate reported completion but Antiphon observed no attributable post-dispatch progress at C:\Antiphon\worktrees\card-task-123472ac. No new pushed commit was observed on refs/heads/feat/card-task-123472ac: runner_reported_commit_not_pushed. Runner mirror /work/worktrees/task-123472ac on 'server2' is at 99910a78c7ec4295786ca94923a040013c689fcd, equal, dirty=false; inspection=ok. |
| 6284d6ec-03f7-4a88-b9f5-12a601bfe017 | Codex | null | F1/F2 implemented and pushed; Final verification is incomplete after the budget stop. |
| ba777bcd-6ccc-4209-8d42-82067a4fab83 | Codex | null | Mid-turn and waiting on the model for 20m, against a 20-minute deadline. Last transcript entry: AssistantText, 20m ago. The task is Failed, not escalated and not retried: a bigger model cannot finish a task that never ends, and re-running it is your call. The session was NOT killed — read session 71e2049c-4399-4574-8f3f-4174bde68bc4 for what it was actually doing before you decide. |

--- next stage ---
next: plan
handoff: Plan the confirmed phone-home transcript diagnostic loss at RunnerContractMapper and PhoneHomeRecoveryPump. Recommend closing CARD-0073 original Claude/S1 questions as fixed by 620f6eb6c, aa1c8f123 and 7a92098f0; retain or split the remote reporting defect. Grok e4073ef5 remains a separate unconfirmed startup cause.
artifact: docs/investigations/2026-10-13-card-0073-transcript-binding-status.md
