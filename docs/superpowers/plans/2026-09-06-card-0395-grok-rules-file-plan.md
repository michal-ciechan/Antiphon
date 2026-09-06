# CARD-0395: durable Grok rules files and reread delivery

Status: Plan complete; TestDesign next. This is an availability repair plus a session lifecycle change, so use the hard path with Review. Runtime implementation and live acceptance are outstanding.

Evidence baseline: repository `3d0cf6e3cbe42c75a1522f7f7bce181605cddb1a`; installed `grok 1.0.13 (5e9a58528b76) [stable]`, Windows, 2026-09-06. Read CARD-0395, closed CARD-0411, and CARD-0382 through `scripts/card.ps1 get`. CARD-0411's structural multiline diagnosis is accepted, not reinvestigated.

## Decisions

### D-1. Use a file plus a short, literal bootstrap; do not claim native file expansion

There is **no verified session-specific, append-equivalent file/env transport** in the measured binary. `--rules @path` and `--rules path` succeed, but their literal strings appear inside `<human_rules>`; the file contents do not. Guessed `GROK_RULES` and `GROK_RULES_FILE` do not inject their contents. These are negative results for the tested mechanisms, not a claim that every possible Grok extension has been exhausted.

There ARE native agent-file transports: `--agent <valid-markdown-definition>`, `GROK_AGENT=<definition-path>`, and `[agent] definition = '<path>'` in an isolated `GROK_HOME/config.toml`. However, the explicit agent definition changed the builtin system prompt and tool set even with `promptMode: extend`: 24 tools became 25, `wait_commands_or_subagents` appeared, and the baseline browser-verification instructions disappeared. Do not silently replace the operator's native agent, emulate Grok's builtin definition, or couple Antiphon to an upstream prompt snapshot.

Select the next-best transport permitted by the brief: place the exact composed rules on disk; keep only a short instruction to read that file in `--rules`. Example value (one physical line):

```text
Antiphon standing rules are in "C:\Antiphon\logs\sessions\instructions\grok\<session-id-N>\rules.md". Before doing any work, read the entire file and follow it as this session's standing instructions. Re-read it when Antiphon requests a refresh and after resume or compaction. If any part is unreadable, stop and report the failure.
```

This is **model-mediated file retrieval**, not native expansion and not the full text in the system channel. The bootstrap delegates authority to a known file; its content becomes tool context when read. A real Grok file-tool round trip against the local stub read a 14,262-byte file in a path containing spaces, returning both sentinels and all 180 content lines, including non-ASCII and quotes. The stub selected the tool call: this proves the CLI/file transport, not that a real model will voluntarily obey the bootstrap. V-8/V-9 must establish that separately before anyone declares the card fixed.

Use this same transport for Grok on both supported launch backends and operating systems. Do not preserve a separate inline-rules implementation on non-Windows. Claude/Codex composition remains unchanged.

### D-2. Preserve the argv guard and separate content from argv

Keep `GrokRulesArgvPolicy`'s CR/LF/NUL, 4,096 UTF-16-unit, occurrence, alias, equals-form, and Herdr effective-env semantics. Validate user/profile/registry argv before any file materialization. The runner validates the generated bootstrap at its final effective-argv boundary before process creation; the server validates the returned path/bootstrap receipt before releasing work. Both use the shared policy. The server cannot precompute a remote runner's absolute path. No fallback to raw multiline text if a file write fails.

Replace the **composition-time raw-payload** check with typed file-content validation, because composed text no longer becomes argv. This intentionally changes CARD-0382's normal bundled-launch refusal tests to successful file transport tests; it does not weaken the existing unsafe-argv backstops. Reject NUL and unresolved `{{key:` placeholders in file content with bounded, content-free diagnostics. CR/LF are valid file contents. Preserve `InstructionBundle.Render`, `BlockSeparator`, composition order, and stamps byte for byte.

Give file content its own explicit limit: default 262,144 UTF-8 bytes, counted without a BOM. Reject, never truncate. Retain the command-line budget for the actual executable/args, including the generated reference. Do not charge the off-argv file body against `CommandLineBudgetChars`; this is an intentional successor to CARD-0382 D-T1, and its old Grok over-budget expectations must change. Claude/Codex limits stay unchanged.

When composed instructions are nonempty and a profile/definition already supplies a safe explicit `--rules`/alias, refuse a duplicate-source conflict with guidance to move the append to `SystemPromptAppend`. An unsafe explicit value still gets `grok_rules_argv_unsafe` first. Do not silently consume, normalize, or reinterpret an operator's literal path-shaped argument. Empty-composition specialists and bare agents keep their original args and create no rules file or refresh turn.

### D-3. The runner owns a stable file; the server owns the rules revision

Use CARD-0382 D-5's reserved location on the **runner host**:

```text
<SessionLogPath>\instructions\grok\<Antiphon-session-id:N>\rules.md
```

The file is UTF-8 without BOM and contains exactly the final rendered composition, with no metadata/front matter. The filename is stable across genuine native resumes of that Antiphon session. Metadata lives separately: content SHA-256, byte count, transport version, and launch generation. Never put a revision/hash into the system bootstrap: native resume retains that bootstrap, so it must remain valid when file contents change.

Carry the body as a typed optional Grok payload through `AgentLaunchComposition`, `AgentLaunchOptions`, `AgentLaunchSpec`, `RunnerGrokAdapter`, and `RunnerLaunchRequest`; it must never be hidden in an argument/env value. Other kinds reject an accidental Grok payload. The runner validates it, derives a canonical absolute path using only its configured root and the session GUID, atomically writes a sibling temp file then replaces `rules.md`, and adds the short bootstrap to effective argv before process creation. Failures occur before session registration/Herdr allocation wherever no child has yet started. Apply the existing owned-process cleanup/terminalization contract for failures after spawn.

Return a typed rules receipt (absolute runner-visible path, hash, version, generation) in the launch/session DTO. Preserve it in the runner's existing adoption/manifests metadata and persist it on the server session before allowing task/channel work. The server must not guess a remote runner path. A restart between runner start and receipt persistence recovers the same receipt from the runner; it does not assume a successful launch means rules were installed.

Add a versioned runner capability (`grokRulesFileV1`) and check it before sending this payload. Use a small explicit `/capabilities` runner endpoint/contract if the existing health DTO cannot carry capabilities cleanly. Old runners must refuse this launch server-side as `grok_rules_transport_unsupported`; silently ignoring an unknown JSON field would start an uninstructed agent. A missing/mismatched receipt is also a launch failure, never permission to send the brief.

Use the session-log directory's access controls; do not publish rules contents through logs, failure messages, a download endpoint, or launch manifests. Store only path/hash/count in receipts and diagnostics. Keep the file through stopped/resumable and pooled states, runner/server restarts, and worktree cleanup. Delete it only with deliberate expiry/deletion of that session's resume artifacts. Remove abandoned temp files within this exact directory; never globally sweep files belonging to another session.

### D-4. Fresh start and resume require an ordered reread turn

Measurement: `grok --session-id <uuid> --rules CARD0395-OLD-RULE -p ...`, then `grok --resume <same-uuid> --rules CARD0395-NEW-RULE -p ...`, both exit 0. Both resumed user-turn requests still contained OLD and not NEW in the system prompt. Rebuilding argv/stamps alone does **not** refresh Grok's native instructions.

On a fresh file-transport launch and every actual native resume:

1. Recompose current instructions, install the file, and persist its receipt and generation.
2. Create a short System-origin queue item with a unique first-line identifier, the absolute path, and expected content revision. It instructs a full read (including continuation reads for files over the tool's 1,000-line default), no task work yet, and a fixed acknowledgement identifying that revision. An unreadable/incomplete file must produce a failure acknowledgement.
3. Deliver through `SessionMessageQueueService` after the sign-in/trust/ready checks. Reuse LF normalization, bracketed paste, separate Enter, and transcript-confirmed delivery. Keep task/card boot prompts, resumed auto-continue, and channel work behind this initialization barrier.
4. Release that barrier only on the matching acknowledgement from the refresh turn, after its `UserPrompt` is confirmed and the turn completes without a provider error. Store this as **provider acknowledgement**, not independently proven byte consumption or obedience. An old-hash or unrelated acknowledgement does not satisfy it.

The refresh turn carries no task-report token. Recognize its queued origin/key before task settlement/nudges and channel reply extraction; it must neither settle an unmarked delegate nor publish acknowledgements to a bound channel. A genuine task report still settles normally after initialization. Failure/timeout surfaces a bounded rules-initialization failure and leaves work queued; follow existing launch-failure cleanup for an owned startup process. Never create an unlimited reread/nudge loop.

Keep the queue's retry and late-confirmation contract. `Sent` with null verdict is not proof; after restart, reconcile the exact first-line identifier against transcript evidence before retrying. Persist the initialization barrier so an interrupted server launch cannot release pending work prematurely.

### D-5. Compaction refresh is durable, idle-gated, and replay-safe

The card attributes `compacted` plus `event_msg/context_compacted` to Grok. Those are the **Codex** shapes in this checkout. Grok's ACP normalizer maps `sessionUpdate: auto_compact_completed` to `CompactBoundary`; `compaction_checkpoint` is bookkeeping. Use the normalized event, with a new fixture taken from a real Grok auto-compaction. Do not add a parser that waits for a Codex-shaped row or types an assumed `/compact` command.

Extend `CompactionRecoveryService` with a Grok file-rules branch, backed by a dedicated `GrokRulesRefreshService`. Every file-backed Grok session qualifies, including pool delegates; do not gate on `Agent.SystemPromptAppend` or the continued existence of an ephemeral agent row. Preserve the existing Claude incident/channel recovery path.

Create the refresh queue row and its durable trigger record in one transaction. A nullable `RulesRefreshKey` on the queue row with a unique `(AgentSessionId, RulesRefreshKey)` index is sufficient: launch keys use generation; compact keys use the persisted transcript-entry identity. Store the expected rules revision and satisfy/cancel state with that item. Do not reuse the current watermark-then-enqueue pattern: a crash between those steps permanently loses a reminder. An in-memory latch can optimize only after the durable transaction commits.

Trigger from BOTH live ingestion and catch-up/replay. `AgentSessionRuntime.SyncTranscriptAsync` currently recovers turn ends/manual-compaction flushing but does not dispatch Grok auto-compaction recovery. A persisted pending refresh must be retried even when every incoming transcript row deduplicates. At server startup, reconcile active file-backed sessions for unhandled compact boundaries and unfinished launch barriers. This covers a crash after transcript persistence but before queue creation.

Queue compact refresh `WhenIdle`, never `Now`: Grok auto-compaction happens mid-turn. It is not a turn end, must not settle a task, and must not interrupt a tool call or synthesize an Enter. At the next genuine idle window, give the refresh precedence over pending ordinary work without interrupting a delivery already in progress. If several boundaries accumulated in one busy turn, one reread of the latest file may satisfy the pending group; persist the covered boundary identities so replay cannot create duplicates. A new boundary after an acknowledged refresh needs another refresh.

Use the same acknowledgement and transcript-delivery machinery as D-4. Do not autoretry a successfully acknowledged reread merely because its own turn compacted: coalesce that boundary into the read that occurred after it, or schedule at most one necessary later reread, with explicit loop detection. A refresh failure on an already working session is an incident/blocked delivery decision, never an automatic session kill.

This is recovery **at the next idle window**, not reinjection before Grok's first post-compact API call inside the current turn. Retention measurements in V-9 determine whether that interval is acceptable. If the bootstrap vanishes or the model acts contrary to rules before idle, stop acceptance and return to Plan; do not claim equivalence to a native SessionStart hook.

### D-6. Pool, legacy sessions, and drift

For an already file-backed warm pool session, reuse requires the desired bundle/content revision to match its installed receipt and initialization to be acknowledged. A mismatch takes the normal cold-dispatch/idle-retirement path; never overwrite rules underneath another task. An empty-composition specialist remains eligible for its existing empty lane and does not acquire Worker bundles to make a test pass.

Legacy native sessions may retain baked-in inline rules across resume; those can contradict a newly read file. Do not claim to replace their system prompt with a typed message. New cold delegates use a fresh native ID. Keep running legacy sessions owned and untouched. Do not reuse legacy bundled pool sessions for new work; retire them at their normal idle opportunity. A standing legacy native resume with nonempty desired rules and no v1 receipt must refuse with a specific migration explanation and require an explicit fresh start through the existing start API: equivalence to its baked-in rules is not established. It must not silently discard history or silently change `--resume` to a fresh identity. The operator may arrange a checkpoint/handoff before that fresh start. This is a one-time compatibility constraint, separate from routine resumes of sessions initialized with the stable-file bootstrap.

For transport-v1 sessions, resume re-renders the file and rereads it. For a live file change without a process launch, retain existing policy-refresh ownership: use its idle-gated actual resume, not an additional watcher writing the active file. Attachment removal to an empty composition must also clear/retire the prior rules contract deliberately; do not leave a resumed bootstrap pointing at stale instructions. TestDesign must specify the empty-transition path (fresh start for removing the system bootstrap is the default).

### D-7. Grok-only behavior; correct the Codex rationale

Leave Claude/Codex transports and compaction working-state behavior unchanged. This is based on code and provider evidence, not the mistaken cross-provider marker in the card. Antiphon's Codex normalizer already documents a real mid-turn compact with a later `task_complete`. Upstream Codex explicitly rebuilds initial context around mid-turn compaction, or clears the reference so the next turn reinjects it; local compaction also obtains base instructions separately from history. See [Codex compact.rs](https://github.com/openai/codex/blob/main/codex-rs/core/src/compact.rs) and [remote compaction](https://github.com/openai/codex/blob/main/codex-rs/core/src/compact_remote.rs).

Thus the sound rationale is **provider-owned instruction/context reconstruction**, not that every developer instruction is simply resent unchanged on every network packet. This is source confirmation and existing Antiphon measurement, not a fresh installed-Codex multi-compaction canary. Pin the no-Grok-refresh behavior for Codex in V-6; do not add another paid provider integration to this urgent repair.

## Ground truth and rejected assumptions

| Question | Evidence / consequence |
|---|---|
| Can prose flattening restore dispatch? | No; CARD-0411 confirms mandatory bundle header LF and shared block separators. Do not modify them. |
| Does successful `--rules @path` prove reading? | No; captured literal `<human_rules>` contains `@C:\...\rules with spaces.md`, no file sentinels. |
| Does a native definition file work? | Yes, by CLI, env, and config with valid front matter; rejected as an append replacement because it changes the builtin agent. |
| Do home rules offer session isolation? | No. Tested isolated home `rules/antiphon.md` and `AGENTS.md` were not discovered in this setup. Global/cwd injection is unacceptable even if enabled: agents sharing a home/cwd would load each other's instructions. |
| Does `--resume` apply new `--rules`? | No in the measured binary: OLD remains; NEW absent. Stable path plus explicit reread is necessary. |
| Where are composed rules built? | `AgentSessionLaunchComposer.ComposeForAgentAsync` (including `ChannelPreamble.Render`) and `AgentTaskDispatcher.ComposeDelegateArgs`. Both currently put composed text on argv. |
| Where can raw overrides bypass composition? | `AgentSessionService.BuildRuntimeLaunchSpecAsync`, then `SessionRunnerRuntime.StartAsync`; retain both final guards. |
| Does the existing recovery cover this? | It is named-agent/SystemPromptAppend-gated; watermark saves before enqueue, and auto-boundary catch-up is missing. It cannot be reused unchanged for durable pool refresh. |
| What did local wire capture actually see? | Grok 1.0.13 / `grok-4.6` used `/responses` for the real tool-bearing user turn as well as helper calls. Distinguish by body/nonce/tools, not just URL. Existing tests' older `/chat/completions` assertion needs a measured compatibility update. |

The detailed measurement record is [CARD-0395 transport measurements](../../investigations/2026-09-06-card-0395-rules-transport-measurements.md). No live production agents, wrappers, auth stores, board routing, or shared stack configuration were changed by Plan.

## Code slices

| Slice | Files / work | Required verification |
|---|---|---|
| S1: payload and store | `server/Application/Dtos/AgentLaunchSpec.cs`, `AgentSessionLaunchComposer.cs`, `AgentTaskDispatcher.cs`, `AgentRegistry.cs`, `AgentTuiLaunchResolver.cs`; new Grok payload/policy helpers; `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`; `RunnerGrokAdapter.cs` and client plumbing; new runner `GrokRulesFileStore.cs`, `SessionRunnerRuntime.cs`, DTO/adoption metadata and capability route. Keep shared composition and guard algorithms. | V-1, V-2, V-3. No old runner may silently ignore the payload. |
| S2: initialization and revisions | Session receipt/generation fields; new `GrokRulesRefreshService.cs`; `AgentSessionService.cs` fresh/card/resume/interrupted launch paths; `SessionMessageQueueService.cs` initialization barrier and refresh ordering; `AgentTaskReplyService.cs`/channel dispatch exclusion for refresh turns; DI registration and CLI-generated EF migration. | V-4 plus real CLI initial/read/resume wire canary. Do not queue ordinary work before receipt/ack. |
| S3: compaction and reuse | `CompactionRecoveryService.cs`, `AgentSessionRuntime.cs` live and sync paths, queued refresh idempotency/transaction/index, startup reconciliation, `AgentTaskDispatcher.cs` warm eligibility; narrowly extend `GrokTranscriptNormalizer.cs` only if actual measured ACP shape demands it. | V-5, V-6; crash/replay and settlement controls are mandatory. |
| S4: restore coverage and prove behavior | `GrokDelegateEndToEndTests.cs`, `GrokDelegateDispatchTests.cs`, `GrokRulesLaunchRefusalTests.cs`, `DelegateBundleLaunchTests.cs`, `DelegateLaunchArgvIntegrityTests.cs`, `CardSpawnModelArgumentTests.cs`, `CodexDelegateDispatchTests.cs`; runner guard/store tests and real CLI canaries. Bring stub Responses SSE schema up to the measured version when needed. Update `docs/agent-kinds.md`, `docs/session-runtime-invariants.md`, `docs/ai-agent-tui-configuration.md`, `docs/herdr-sessions.md`, HTTP owner if adding capability route. | V-7, V-8, V-9 and named existing regressions; Code reports every item, skip, failure, and positive control. |

S1 and S2 are the minimum deployable availability slice; a file-pointer launch without initialization ordering is not shippable. S3/S4 remain part of this same card and must complete before closure. Land the Plan before dispatching TestDesign, and land TestDesign before Code so sibling worktrees contain the artifacts.

## Verification design brief for TestDesign

TestDesign must turn these into named runnable tests, fixture shapes, exact assertions and red-then-green positive controls. It must retain the distinction between process launch, confirmed typed delivery, provider acknowledgement, complete file read, and actual model behavior.

| ID | Required proof |
|---|---|
| V-1 | Real production composition for Worker plus a pipeline stage, standing attachment + non-Normal style + append + bound-channel rendering, and card-spawn. Exact final rendered UTF-8 file content, unchanged bundle stamps/order, body absent from every argv element, short bootstrap passes policy. Include CR, LF, CRLF, quotes, `$`, backticks, non-ASCII, marker-like prose, >4,096 units, >old argv budget, byte-limit boundary, >1,000 lines, NUL and secret-marker rejection. No handcrafted single-line substitute for delegate-basics. |
| V-2 | Runner-host materialization on PtyHost and fake-Herdr: spaces/Unicode path, hash receipt, atomic replacement, no stale/partial file on injected write failure, resume same path/new hash, two sessions same cwd stay isolated, adoption/restart retains receipt, worktree deletion does not remove file. Missing capability/receipt fails before work; an old runner cannot produce an uninstructed successful launch. |
| V-3 | Preserve all CARD-0382 unsafe **raw argv** cases server and runner, including aliases/equality/duplicates/Herdr env expansion versus PtyHost literal env tokens, NUL, 4,096/4,097 boundary, kind/OS branches and no runner/pane effects. Path-shaped safe arguments still pass policy unchanged. Validate source-conflict precedence and safe bare/specialist passthrough. File write failure never falls back to inline. |
| V-4 | Initial and resume barrier: ready/trust handled before typing, exact revision ack only after confirmed UserPrompt, work/auto-continue/channel messages held then released, unknown or stale ack ignored, provider error/read failure/timeout cannot release work, restart before/after receipt and before/after queue Sent recovered once. Refresh turn cannot settle/nudge an unmarked task or send a channel acknowledgement. Include legacy-native migration refusal without losing history, and transport-v1 resume updating the file. |
| V-5 | Actual ACP auto_compact_completed fixture -> normalized boundary -> durable System refresh for standing AND delegate. Mid-turn stays working and no input is injected until real idle; live/sync-only/replayed/rebased sequences all yield one logical refresh. Inject crashes at transcript save, trigger/queue transaction, send, and ack boundaries; replay must neither lose nor duplicate it. Multiple boundaries coalesce correctly; another real compact after ack causes another read; refresh-induced compaction cannot loop. |
| V-6 | Claude manual/auto recovery, Codex compact/task_complete timing and developer-instruction argv unchanged. Empty specialists create no file/refresh; warm reuse with matching hash succeeds, mismatched/legacy bundled warm sessions cannot accept new work. Task settlement, channel follow-up exclusion and process ownership stay correct. |
| V-7 | Restore all three Windows Grok delegate E2E behaviors lost under CARD-0382: normal priced settlement, unmarked/nudge settlement, and boot-stall retry. Remove the two CARD-0382 skips. fakegrok models the new bootstrap/ack/receipt contract rather than bypassing it. Keep unsafe-profile refusal E2E as a separate negative control. |
| V-8 | REAL grok.exe dispatched through the REAL delegate.ps1 -> server -> runner -> transcript -> settlement path, with normal multiline goal and actual stage + delegate-basics rules. Run PtyHost plus an isolated Herdr standing-agent attachment case. Confirm file read and final task report, no clap error/refusal, Grok detection before Herdr timeout, pane retained. Run a zero-spend wire arm first, then an authenticated real-model compliance arm. A stub's scripted read/ack is not the live behavioral verdict. |
| V-9 | In a real headed Grok session, observe at least TWO genuine auto-compactions and capture their native ACP identities. Measure baseline single-line `--rules` canaries and the new file bootstrap separately: before compact, first post-compact continuation (before idle refresh), after acknowledged refresh, after second compact. Ask neutral tasks whose required output depends on early/middle/tail rules sentinels, not on repeating their answers in the prompt. Change a rules-file sentinel, genuinely resume, then verify the new answer. Record survival/loss/mixed results and whether refresh repaired them. No conclusion from just seeing a boundary or a reread prompt. |

Positive controls must include: replace file text with a truncated body; put raw multiline content back on argv; remove each raw-argv backstop separately; let an old runner ignore payload; release work before the ack; save trigger before enqueue and crash; omit sync handling; classify auto compact as TurnEnd; remove refresh-turn settlement exclusion; and make the real CLI pointer refer to a missing file. Each must fail a meaningful named assertion, then pass when restored.

### The real dispatch acceptance run

Build an isolated test server/runner graph using `RealCliStubBServerHarness`, `DirectSessionRunnerClient` or the existing isolated runner fixture; do not boot real `Program` against production runner 17204. Give the process lane its assembly-local `ParallelLimiter<ProcessSpawnLimit>` and headed tests their existing opt-ins and `NotInParallel("Headed")` group. `RealCliStubEnv.ForGrok` remains the only committed test env builder: both redirect variables must be present, and a synthetic `/api-key` hit plus a nonce in the **actual tool-bearing user-turn** request must reach the stub. Extend its SSE fixture if required, including usage details, sequence numbers and text annotations; do not make a permissive endpoint-only assertion.

Create a normal multiline goal file in the disposable repo (several paragraphs, quotes, at least 5 KB, independent start/middle/end task markers). Invoke `delegate.ps1` as a PowerShell script **in process** so the long Goal does not itself cross a Windows native argv boundary:

```powershell
$goalText = Get-Content -LiteralPath $goalFile -Raw
& .\scripts\delegate.ps1 Code -Kind Grok -Level High -Dir $disposableRepo -Worktree -ReadOnly -Goal $goalText -Title 'CARD-0395 transport acceptance' -NoInheritEnv
```

Supply the isolated test API/task identity through the harness environment; for the later real-service acceptance use the authorized task/capability identity without changing routing pins or bypassing quota/auth refusals. A transport probe task itself must not spawn further delegates.

Record task/session/native-session IDs, CLI version, backend, rules/brief file hashes, argv metadata without content, confirmed bootstrap and brief UserPrompt identities, matching refresh acknowledgement, native read-file tool output containing all file markers, and the actual marked final report/settlement. Preserve both the full multiline brief file and evidence that the agent consumed its end marker; Grok's normalized transcript can flatten typed newlines, so compare using the existing provider matcher and the on-disk brief, not a screen redraw. A successful `delegate.ps1` create alone is not acceptance.

For real-model compliance, put a harmless response requirement only in the attached rules (distinct from the task markers); the final report must obey it. For compaction, first use the real CLI/local stub to measure how to trigger its own automatic compaction without assuming a manual command, then execute the real-model headed sequence. If auth/quota, inability to induce genuine compaction, or model noncompliance blocks V-8/V-9, report the exact remaining gate and retain the card open; do not replace it with a fake boundary, a unit test, or a silent skip.

### Commands, rollout and stopping rules

Use class-scoped TUnit commands such as `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0395/ -- --treenode-filter "/*/*/GrokDelegateEndToEndTests/*"`; TestDesign supplies the final class list for server, runner, and Pty suites. Run `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` sequentially. No frontend tests/build are needed unless Code actually changes the client. Clean only verified worktree-local `bin-card0395` directories using native PowerShell after runs.

Expect several hours of Code/integration work plus real-session acceptance; repeated auto-compaction is an explicit additional runtime gate, not a five-minute unit check. Report test counts/failures and every positive control, not just an aggregate green build.

Deployment changes server, shared contracts and runner: restart both from the main checkout using the documented bootstrap scripts, never this linked worktree. Verify checkout HEAD and that both rebuilt binaries contain the new transport/refresh symbols and capability before the real dispatch. A health check alone is insufficient. Existing running legacy agents are not mass-restarted. If rollback is needed, stop creating file-transport launches and retain rules files/metadata for resumable sessions; the old guarded build will again refuse normal multiline compositions. Do not remove the guard as a rollback mechanism.

## Plan-stage validation

Completed: full card reads; owner/source inspection; installed CLI help/version; 11 final transport-matrix executions and 6 follow-up executions (one is inspect), all final exits 0; captured native file read and old-rules-on-resume behavior. Earlier probe fixture attempts timed out or exited on incomplete Responses SSE; they were corrected, not counted as passing transport cases. No repository tests/builds, live-model completions, production delegate dispatches, compaction endurance run, deployment, or production configuration changes were performed.

No decision is needed to author TestDesign under D-1 through D-7. The selected fallback's model-mediated semantics and legacy fresh-start constraint must remain visible in Code/Review; neither may be described as native full-system-prompt file loading.
