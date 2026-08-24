# CARD-0168 — real CLI binaries against a recording stub LLM API: FakeLlmApi, proven-redirect discipline, PtyHost + Herdr launch-path coverage — plan

**Date:** 2026-08-24 · **Card:** CARD-0168 (`f4d2e90a-82ce-41d5-8a8f-007154c35d9b`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `feat/card-task-f0dea55c` @ `aa31590`. Every file:line below was re-read out of
the code on that commit.

**Established facts, not re-derived here** (Investigate stage, task `14fb184e`, findings on the
card 2026-08-24 — PROBE-VERIFIED: real CLI binaries were actually launched against a real local TCP
listener and the redirect mechanisms, request shapes and auth headers directly observed):

- **Claude:** `ANTHROPIC_BASE_URL` + `ANTHROPIC_API_KEY` redirect confirmed live. Wire order:
  `HEAD /api/hello` (must answer **200** or print-mode stalls), then `POST /v1/messages?beta=true`
  with **`x-api-key`** (not Bearer), `anthropic-version: 2023-06-01`, streamed, ~250 KB body (full
  tool schemas). A 400 from the stub exits the CLI cleanly — no observed silent fallback to
  api.anthropic.com.
- **Grok:** **`GROK_XAI_API_BASE_URL` is a FALSE SAFETY** — it redirects only `GET /api-key`
  (Bearer = the injected `GROK_CODE_XAI_API_KEY`); the chat completion still hits **real xAI**
  (proven by a unique-token probe that returned exit 0 after a single stub `/api-key` hit — that
  probe almost certainly spent a real xAI turn). The genuine chat redirect is
  **`GROK_CLI_CHAT_PROXY_BASE_URL`**: stub-confirmed hits on `GET /models`, `GET /settings`,
  streaming `POST /responses` (OpenAI **Responses** API; paths WITHOUT `/v1` when the base URL has
  none). Auth on the chat-proxy path with intact local login is an **OAuth JWT Bearer**, not the
  injected key; breaking `GROK_AUTH_PATH` forces the injected key on `/models` but then "Not
  signed in" refuses the turn.
- **Codex:** redirect requires FIVE `-c` launch arguments together
  (`model_providers.stub.{name,base_url,env_key,wire_api=responses}` + `model_provider=stub`).
  Confirmed hit: `GET /v1/models?client_version=0.147.0` with `Authorization: Bearer <injected>`,
  then reconnect-looping `POST /v1/responses` after a models 400. This is launch-ARGUMENT
  injection; CARD-0167 is the card for making that first-class in agent setup.
- CARD-0160/0161/0162/0164: Herdr is **ClaudeCode-only** (refused at create/PATCH/launch), so the
  backend matrix is **4 cells**: Claude×PtyHost, Claude×Herdr, Grok×PtyHost, Codex×PtyHost.
- FakeClaude/FakeGrok fake the **CLI side** of the boundary (the drift class this card exists to
  remove); `Antiphon.Messaging.FakeGateway`'s `DeliveryStore`
  (`src/Antiphon.Messaging.FakeGateway/DeliveryStore.cs:19`) is the recording pattern to copy —
  in-memory list as the API contract, optional JSONL as grep convenience.

**Verified in code this pass (load-bearing for sequencing):**

- `RunnerLaunchRequest` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:3`)
  carries `Exe`, `Args`, `Env` verbatim — a test constructing one controls the full launch line,
  Codex `-c` flags included. `SessionRunnerRuntime` is constructed in-process by tests today
  (`HerdrRunnerSessionTests`, `SessionRunnerRuntimeTests`).
- `POST /api/sessions` (`server/Api/Endpoints/SessionEndpoints.cs:31`) accepts caller
  `ExtraArgs` + `ExtraEnv` on definition-based launches, flowing through
  `AgentRegistry.Resolve` (`AgentRegistry.cs:113`) into `AgentSessionService.StartAsync`.
- **Agent-based** launches (`AgentControlService.StartAsync:284-293`) construct `extraArgs`
  internally (bundles, `--model`, Codex developer-instructions) — a caller CANNOT inject arbitrary
  arguments there; env-only injection (CARD-0106 layers, `AgentTuiLaunchResolver.cs:332-358`) is
  what the agent path supports. This is precisely CARD-0167's gap and precisely why the Codex
  dependency splits by tier (§8).
- `ApplyClaudeEnvironmentDefaults`/`ApplyGrokEnvironmentDefaults`
  (`AgentTuiLaunchResolver.cs:492/509`) are `ContainsKey`-guarded — injected stub env is never
  overwritten by kind defaults. There is no `ApplyCodexEnvironmentDefaults`.
- Headed conventions: `HeadedClaudeGate` (`tests/Antiphon.Tests/Agents/HeadedClaudeGate.cs`,
  `ANTIPHON_HEADED_TESTS`), `HeadedCodexGate` (`ANTIPHON_CODEX_HEADED_TESTS`), classes carry
  `[NotInParallel("Headed")]` + `[Category("Headed")]` + `[ParallelLimiter<ProcessSpawnLimit>]`
  (`ClaudeAdapterIntegrationTests.cs:12-14`). `ProcessSpawnLimit` exists in `Antiphon.Tests`,
  `Antiphon.PtyHost.Tests`, `Antiphon.Agents.Pty.Tests` — NOT in `Antiphon.SessionRunner.Tests`.

**Related:** CARD-0167 (first-class per-kind proxy/key setup — reciprocal dependency, §8),
CARD-0106 (the env-layer mechanism B-tier Claude/Grok rides), CARD-0160/0161/0162 (Herdr lane),
CARD-0055 (the delivery verdict B-tier asserts), CARD-0006 (transcript bind rules B-tier asserts),
CARD-0050 S5 (`ProcessSpawnLimit`), CARD-0047 (trust-dialog clearing on interactive launches).

---

## The safety invariant, stated first

**Never trust an unverified redirect variable name. A mechanism is only "a redirect" once an
actual request has been observed arriving on the stub side — and the test oracle for every
committed test is stub receipt of a per-run nonce, never the CLI's own exit code or output.**

The investigation's own near-miss is the proof: `GROK_XAI_API_BASE_URL` looks exactly like a base
-URL override, redirects a real endpoint (`/api-key`), produces a plausible "it worked" exit 0 —
and the completion silently went to real xAI and spent real quota. A plausible-but-unproven
redirect is worse than none, because it converts "obviously talks to the real API" into "silently
talks to the real API while looking safe." Every mechanism this plan relies on is probe-proven
(Claude env pair, Grok chat-proxy var, Codex `-c` five-set); every mechanism a future slice or
future maintainer adds — a new CLI version, a new agent kind, a new endpoint the interactive TUI
turns out to call — must be proven the same way BEFORE a committed test leans on it. §9's S4
applies this rule to ourselves: interactive-mode wire surfaces are unprobed today, so S4 begins
with its own capture probe, not an assumption that print-mode findings transfer.

---

## Verdict up front — the twelve decisions

1. **Stub surface per provider: the probe-observed minimum, two response tiers, no tools.**
   Claude: `HEAD /api/hello` → 200; `POST /v1/messages` (any query) → scripted Anthropic SSE.
   Grok: `GET /models`, `GET /settings` → minimal JSON; `POST /responses` → scripted OpenAI
   Responses SSE; plus `GET /api-key` (the key-injection oracle, §5). Codex: `GET /v1/models`;
   `POST /v1/responses` → same Responses SSE writer. Every route records before answering; every
   UNMATCHED path records and answers 404 (a recorded 404 is evidence of a surface gap, a silent
   one is drift). Response tiers: `ScriptedError(status)` and `ScriptedTextTurn(text)` — one
   complete single-text-block turn ending `end_turn`/`response.completed`. §4.
2. **Project shape: `src/Antiphon.FakeLlmApi`, library only, in-process Kestrel, ephemeral
   ports; all tests in `Antiphon.Tests`.** No host exe until a manual-debug need exists (YAGNI —
   the FakeGateway precedent needed one because Aspire hosts it; nothing hosts this). Tests hold
   the `FakeLlmApiServer` instance and assert on `Requests`, `FakeHerdrServer`-style.
   `Antiphon.Tests` owns every suite: it already has the gates, `ProcessSpawnLimit`, the DB
   fixture B-tier needs, and `SessionRunnerRuntime` reach; splitting across projects would either
   duplicate the spawn lane or recreate the forbidden co-scheduling problem. §4.
3. **Fail-closed policy per kind: invalid synthetic keys + config isolation + nonce oracle;
   Grok's residual spend risk bounded, stated, and made loud.** Claude: synthetic key + isolated
   `CLAUDE_CONFIG_DIR` (no OAuth tokens to fall back to) → a redirect regression 401s at the real
   API, spends nothing. Codex: synthetic key + provider config carries the base_url, so a partial
   regression is a config error, not a real call. Grok: the clean turn REQUIRES intact local OAuth
   login (probe-proven), and that same login would authenticate a fall-through — the one kind
   where hard spend-closure and a clean turn conflict. Bounded by: minimal prompt bodies, both
   Grok base-URL vars pointed at the stub, `[Explicit]`-forever (D5), and the nonce oracle
   failing the test loudly the moment the stub misses a hit. Relying on `GROK_XAI_API_BASE_URL`
   for chat isolation is BANNED and pinned by a self-test on the env-builder helper. §5.
4. **Grok canonical redirect env is `GROK_CLI_CHAT_PROXY_BASE_URL`; CARD-0167's text gets
   corrected as part of S2; key-injection and redirect are proven by two different stub hits.**
   Set BOTH vars at the stub: `GET /api-key` arrives with `Bearer == GROK_CODE_XAI_API_KEY`
   (proves the injected credential reached the process), `POST /responses` arrives carrying the
   nonce (proves the chat turn was redirected), its OAuth JWT recorded and asserted present but
   not equal to anything. `GROK_AUTH_PATH` is never broken in committed tests ("Not signed in"
   kills the turn). §5.
5. **Opt-in: a NEW flag `ANTIPHON_REAL_CLI_STUB_TESTS=1`, plus `[Explicit]`, permanently.**
   Reusing `ANTIPHON_HEADED_TESTS` would silently add a spend-adjacent test class to everyone who
   already runs headed suites — the opposite of "unmistakable." One flag for all three kinds
   (per-kind binary/login checks live in the gate, missing pieces self-skip with a message naming
   the flag). `[Explicit]` stays on every suite indefinitely: Grok's residual (D3) never goes to
   zero, and a uniform rule is what keeps the class structurally obvious; relaxing it is an
   operator decision, not a build-slice decision. §6.
6. **Parallelism: one `[NotInParallel("RealCliStubProxy")]` key + `[ParallelLimiter<
   ProcessSpawnLimit>]` on every suite; per-test budget 120 s, whole opted-in run ≤ ~15 min.**
   The spawn lane already serializes against headed/pty suites in the same assembly. Never
   co-scheduled with `Antiphon.Agents.Pty.Tests` (existing repo rule). Ephemeral stub ports mean
   no cross-talk even if serialization ever regresses. §6.
7. **Herdr matrix: 4 cells, Claude×Herdr behind a second gate.** `RealCliStubGate` (flag +
   binary) composes with a herdr-liveness check (pipe answering + `SessionRunner:Herdr:Enabled`
   semantics) for the one Herdr cell; herdr absent ⇒ skip, not fail. Grok×Herdr and Codex×Herdr
   are N/A by CARD-0160's refusal matrix — documented in the suite header, no test written, the
   existing refusal pins already own that behavior. §9 S5.
8. **Tiering and CARD-0167 sequencing (decisions 10+11, resolved together, B-tier NOT scoped
   out):** A-tier (bare CLI print/exec → stub) is slice 1-3 — immediately buildable, and it IS
   the proven-redirect regression canary every later slice stands on. B-tier (Antiphon's own
   launch path — the operator's stated goal) splits by level: **B-runner**
   (`SessionRunnerRuntime.StartAsync` with a caller-built `RunnerLaunchRequest`) supports all
   three kinds TODAY, Codex included, because Args/Env are verbatim — no CARD-0167 wait;
   **B-server** (`AgentSessionService` via definition-based launch) supports Claude/Grok via env
   and Codex via caller `ExtraArgs`; **B-agent** (agent-based `AgentControlService` launch with
   CARD-0106 env layers — the path production agents actually take) supports Claude/Grok today
   (pure env) and **cannot carry Codex's `-c` five-set** — that single cell is explicitly
   deferred to land WITH CARD-0167, whose acceptance should include turning the deferred test on;
   this card's stub is reciprocally CARD-0167's verification harness. Nothing else waits. §8.
9. **Naming: `ClaudeRealCliStubProxyCanaryTests` / `GrokRealCliStubProxyCanaryTests` /
   `CodexRealCliStubProxyCanaryTests` / `ClaudeHerdrRealCliStubProxyCanaryTests`, all
   `[Category("RealCliStubProxy")]`.** Adopted from the investigation unchanged: the class name
   says "real CLI" and "stub proxy" in the same breath, the category makes the class filterable
   as a unit, and `[Explicit]` + the dedicated flag complete the three independent signals the
   operator's directive asks for. Not also `Category("Headed")` — these are gated by a different
   flag, and overloading the headed category would let a headed run half-select them. §6.
10. **Recording: full bodies in memory, always — including Claude's ~250 KB; JSONL sidecar
    truncates at 16 KB/body with full length + SHA-256.** Memory is the API contract
    (DeliveryStore's rule) and a per-test store holding a handful of 250 KB strings is nothing;
    assertions target headers + nonce containment, never full-body snapshots, so no 250 KB
    fixture files exist. Tool-stripping CLI flags are NOT used to shrink bodies — the launch must
    stay production-shaped or the test proves less than it claims. §4.
11. **The stub never sees a real credential — by construction, and stated.** Every key the tests
    inject is synthetic (`stub-<kind>-<guid>`); recording headers verbatim (the whole point,
    operator directive 2) is safe because of that, and the Grok OAuth JWT — the one real-ish
    credential that transits the stub — is asserted-present, recorded in memory, and the sidecar
    redacts the `Authorization` value to its SHA-256 (grep-ability without a bearer token at rest
    in a gitignored-but-real file). §4.
12. **Tool-call round-trips: forbidden in this card.** The stub's scripted turns are single text
    blocks; the scripting seam (`ScriptedTextTurn`) leaves room but no `tool_use`/function-call
    emission ships. Launch-path verification doesn't need it, and a half-faithful tool loop is a
    new drift surface — the exact disease this card treats. A future card owns it if a need
    appears. §4.

---

## 1. What this card is, and is not

It IS: a new recording stub for the **LLM-API side** of the boundary, plus opt-in canary suites
that spawn the **real installed CLI binaries** through progressively more of Antiphon's own launch
machinery, asserting from the stub's records that (a) the injected credential reached the child
process, (b) the redirect genuinely happened (nonce receipt), and (c) Antiphon's launch/transcript
/delivery path behaved (B-tier). It is NOT: a replacement for FakeClaude/FakeGrok (they stay the
cheap CI workhorses for TUI-side behavior), a load/latency harness, a tool-calling simulator, or a
CARD-0167 implementation.

## 2. FakeLlmApi — shapes

```
src/Antiphon.FakeLlmApi/
  FakeLlmApiServer.cs      // WebApplication on 127.0.0.1:0; BaseUrl; Requests; IAsyncDisposable
  RecordedRequest.cs       // Seq, UtcTimestamp, Method, Path, QueryString, Headers (verbatim,
                           // multi-value), Body (string, full), BodyByteLength
  RecordedRequestStore.cs  // thread-safe list; All, Query(predicate), Reset(),
                           // WaitForAsync(predicate, timeout) — the async oracle tests await
  ClaudeStubEndpoints.cs   // HEAD /api/hello; POST /v1/messages → AnthropicSse
  GrokStubEndpoints.cs     // GET /models, /settings, /api-key; POST /responses → OpenAiResponsesSse
  CodexStubEndpoints.cs    // GET /v1/models; POST /v1/responses → OpenAiResponsesSse
  AnthropicSse.cs          // message_start → content_block_start → content_block_delta(s)
                           //  → content_block_stop → message_delta(stop_reason:end_turn, usage)
                           //  → message_stop
  OpenAiResponsesSse.cs    // response.created → output_item.added → output_text.delta(s)
                           //  → output_item.done → response.completed
  StubScript.cs            // per-endpoint queue: ScriptedError(status,json) | ScriptedTextTurn(text)
  RealCliStubEnv.cs        // §5 — the ONLY sanctioned env/args builder per kind
```

Construction: `await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = …, Grok = …,
Codex = … , JsonlPath = … })`. A server instance can enable any subset of provider surfaces;
recording middleware runs FIRST for every request (body buffered fully before routing), so even a
scripted-error or 404 response leaves a complete record. Classlib with
`<FrameworkReference Include="Microsoft.AspNetCore.App"/>`; nets the repo's standard TFM; no
Antiphon project references (contracts-free — tests compose it with whatever they launch).

SSE fidelity bar: the streams must be accepted by the real CLIs as a completed turn (CLI exit 0 in
print/exec mode with the scripted text visible) — that is the definition of "enough fidelity," and
S1-S3's A-tier tests are what hold it. Event names/field shapes are written from the probe
captures, and where a capture is thin the slice re-probes rather than guessing (safety invariant).

## 3. What each test asserts (the oracle, uniformly)

Every canary generates `var nonce = $"STUBCANARY-{Guid.NewGuid():N}"` and sends a prompt
containing it. Pass requires ALL of:

1. **Redirect:** `Requests.WaitForAsync(r => r.Path == <chat path> && r.Body.Contains(nonce))`
   within budget. The nonce arriving at the stub is the only accepted proof of redirection.
2. **Credential injection:** the recorded auth header equals the synthetic key — Claude
   `x-api-key`, Codex `Authorization: Bearer`; Grok's split oracle per D4 (`/api-key` Bearer ==
   injected key; `/responses` Bearer present, JWT-shaped, recorded).
3. **Turn completion (ScriptedTextTurn tests):** the CLI's output/transcript carries the stub's
   scripted reply text — proving the SSE was consumed as a real turn, not merely received.
4. **Fail-closed (ScriptedError tests):** CLI exits with an error surface and the stub records
   the attempt; the test additionally asserts NO chat-path request beyond the scripted-error
   exchange (a retry storm is visible in the record count) and, for Claude, that the error text
   names the stub's status (the probe-observed `API Error: 400` shape).

## 4. — 5. Env/args per kind: `RealCliStubEnv`, the single sanctioned builder

One static builder per kind returns `(IReadOnlyDictionary<string,string> Env,
IReadOnlyList<string> Args)`; **committed tests may not hand-roll stub env**, so the safety
decisions live in exactly one reviewable place (and its own unit pins, which run un-gated in CI):

- **`ForClaude(baseUrl, syntheticKey, configDir)`**: `ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY`,
  `CLAUDE_CONFIG_DIR` → per-test temp dir (OAuth isolation; also keeps `~/.claude.json` trust
  state out of assertions — interactive B-tier relies on the existing
  `ClaudeBlockingPromptDetector` trust-dialog clearing, CARD-0047), `DISABLE_AUTOUPDATER=1` and
  the other `ApplyClaudeEnvironmentDefaults` values pre-applied so A-tier bare launches match
  B-tier resolver output.
- **`ForGrok(baseUrl, syntheticKey)`**: `GROK_CLI_CHAT_PROXY_BASE_URL` (the canonical redirect),
  `GROK_XAI_API_BASE_URL` ALSO → stub (defense-in-depth + the `/api-key` key-injection oracle —
  never the chat mechanism), `GROK_CODE_XAI_API_KEY` = synthetic, `GROK_TELEMETRY_ENABLED=0`,
  `GROK_FEEDBACK_ENABLED=0`. Never touches `GROK_AUTH_PATH`. Unit pin: the returned dict names
  `GROK_CLI_CHAT_PROXY_BASE_URL`, and the builder throws if a caller tries to opt out of it —
  the executable form of the `GROK_XAI_API_BASE_URL` ban.
- **`ForCodex(baseUrl, syntheticKey)`**: env `OPENAI_API_KEY` = synthetic; args the five-set
  `-c model_providers.stub.name="Stub"`, `.base_url="<stub>/v1"`, `.env_key="OPENAI_API_KEY"`,
  `.wire_api="responses"`, `-c model_provider=stub`. (Kept in this builder, NOT in
  `CodexLaunchArgs`, until CARD-0167 promotes endpoint injection to production code.)

Grok's residual spend risk, restated honestly: with local OAuth intact, a future grok binary that
renames the proxy var would send the turn to real xAI, spend one minimal turn, and the test would
then FAIL LOUDLY on oracle 1 (nonce never arrives). That failure is the alarm working — the
bounded cost of keeping the clean-turn goal — and `[Explicit]`-forever keeps the exposure at
"operator deliberately ran it," never CI background burn.

## 6. Gating, naming, parallelism (decisions 5, 6, 9)

`RealCliStubGate` (new, `tests/Antiphon.Tests/Agents/`): `SkipIfNotEligible(AgentKind)` — Windows,
`ANTIPHON_REAL_CLI_STUB_TESTS=1`, per-kind binary resolvable (reusing `HeadedClaudeGate`'s PATH
resolution shape; codex/grok resolved similarly), Grok additionally a local-login presence check
(default `~/.grok` auth file exists — skip with a message naming what to do, don't burn a launch
to find out). Herdr cell: + herdr pipe answering. Suites: the four D9 names, each
`[Explicit]` + `[Category("RealCliStubProxy")]` + `[NotInParallel("RealCliStubProxy")]` +
`[ParallelLimiter<ProcessSpawnLimit>]`. Budgets: 120 s per test hard (CLI ready/turn waits well
inside it — probes completed in seconds), suite ≤ ~15 min opted-in, zero cost un-opted (skip in
milliseconds). Runbook line in the suite header: how to run exactly one cell
(`--treenode-filter` on the class + the flag).

## 7. B-tier: what "through Antiphon's launch path" asserts

B-tier reuses the same oracle (§3) and adds Antiphon-side assertions:

- **B-server (Claude, Grok — S4):** definition-based `POST`-equivalent through
  `AgentSessionService.StartAsync` (in-process, the `AgentSessionServiceIntegrationTests`
  harness), definition Exe = real CLI, Env = `RealCliStubEnv`, `TranscriptEnabled` where the kind
  supports it. Asserts: session reaches Running; the transcript BINDS (CARD-0006 — a
  `TranscriptEntries` `UserPrompt` row for the sent prompt appears); a queued `WhenIdle` message
  delivers with the CARD-0055 verdict `Delivered` **via transcript confirm, not screen fallback**;
  and the stub saw exactly the turns the test drove (record count — no invisible extra turns).
  The reply text rendered on screen is the stub's scripted text — the full loop: Antiphon typed →
  real CLI → stub answered → real CLI rendered → Antiphon confirmed.
- **B-runner (Codex — S6):** `SessionRunnerRuntime.StartAsync` with a hand-built
  `RunnerLaunchRequest` (PtyHost backend) carrying `ForCodex` args/env. Asserts launch, stub
  oracle, and clean kill. This is real Antiphon launch machinery (runner → pty-host →
  `ModernConPtyConnection`) with zero CARD-0167 dependency.
- **B-herdr (Claude — S5):** the S4 shape with `SessionBackend = Herdr` against live herdr;
  asserts the herdr sidecar exists, delivery confirms via the CARD-0164 transcript-first path,
  and the stub oracle.
- **B-agent Codex — DEFERRED, explicitly:** the one cell that needs argument injection through
  the agent-based path. Deferred to ride CARD-0167 (whose design should treat "the deferred
  `CodexRealCliStubProxyCanaryTests` agent-path test goes green" as its own acceptance evidence).
  Claude/Grok B-agent coverage is NOT deferred conceptually, but is also not a separate slice:
  B-server exercises the same resolver/env layers (`AgentTuiLaunchResolver` runs on both), and a
  B-agent variant adds DB agent furniture without adding launch-mechanism coverage — one
  Claude B-agent test is included in S4 as a cheap layer-order pin (LaunchEnvOverride carrying
  the stub env survives merge order), not a whole matrix.

## 8. Sequencing with CARD-0167 (decision 8, restated as actions)

- S2 EDITS CARD-0167's description: canonical Grok chat redirect =
  `GROK_CLI_CHAT_PROXY_BASE_URL`; `GROK_XAI_API_BASE_URL` documented as the `/api-key`-only false
  safety with a pointer at this plan's ban; Codex noted as five `-c` args (the card currently
  says three).
- This card never blocks on CARD-0167: A-tier Codex (S3) and B-runner Codex (S6) prove the
  mechanism and the runner path now. CARD-0167 unblocks exactly one deferred test (B-agent
  Codex), and inherits a ready-made verification harness in return.

## 9. Build order

1. **S1 — FakeLlmApi + Claude A-tier.** The library (server, store, Claude endpoints, both SSE
   writers, `StubScript`, `RealCliStubEnv.ForClaude`), un-gated self-tests
   (`FakeLlmApiSelfTests`: store semantics incl. `WaitForAsync`, recording-before-routing, 404
   recording, sidecar truncation/redaction, env-builder pins), and
   `ClaudeRealCliStubProxyCanaryTests`: (a) `claude -p` + nonce → hello 200, messages hit,
   `x-api-key` exact, scripted reply on stdout, exit 0; (b) ScriptedError 400 → error exit, no
   retry storm, no further chat requests — **the fail-closed/never-silent-fallback pin**.
2. **S2 — Grok A-tier.** Grok endpoints + `ForGrok` + `GrokRealCliStubProxyCanaryTests` (dual-hit
   oracle per D4; fail-closed 400 arm) + the CARD-0167 card-text correction.
3. **S3 — Codex A-tier.** Codex endpoints + `ForCodex` + `CodexRealCliStubProxyCanaryTests`
   (`codex exec`, Bearer exact, nonce, fail-closed arm — bounded assertions on its observed
   reconnect-looping: the loop must stop, budget-bounded).
4. **S4 — B-server Claude + Grok (PtyHost).** Begins with an interactive-mode capture probe per
   kind (headed, results recorded in the slice commit + appended here) — the safety invariant
   applied to ourselves; stub grows whatever additional startup endpoints interactive mode
   proves to need. Then the §7 B-server tests + the one Claude B-agent layer-order pin.
5. **S5 — B-herdr Claude.** `ClaudeHerdrRealCliStubProxyCanaryTests` behind the composed gate;
   N/A cells documented in the header.
6. **S6 — B-runner Codex + docs.** The `RunnerLaunchRequest` Codex test; CLAUDE.md gotcha entry
   (the proven-redirect principle, the Grok false-safety ban, the flag/category, the CARD-0167
   deferral); close out with measured wall-clocks per cell appended to this plan.

Slices are independently shippable and strictly ordered by evidence: no slice trusts a wire fact a
prior slice (or the investigation) didn't observe on a stub.

## 10. Out of scope

Tool-call round-trips (D12); any CARD-0167 implementation (per-kind first-class setup UI/resolver);
FakeClaude/FakeGrok changes; CI/nightly enrollment of these suites; non-Windows; load/latency;
Grok or Codex Herdr cells (structurally N/A); production code changes of any kind — the only
`src/` addition is the new FakeLlmApi library, and the only `server/` change is none.

## 11. S4–S6 measured wall-clocks and probe findings (2026-08-24)

Interactive-mode capture probes (before committed B-tier, as required):

- **Claude interactive:** same wire as print-mode — `HEAD /api/hello` then `POST /v1/messages?beta=true` with `x-api-key` = synthetic key and the per-run nonce in the body. Isolated empty `CLAUDE_CONFIG_DIR` parks on the theme picker; global json is `{CLAUDE_CONFIG_DIR}/.claude.json` (not `~/.claude.json`, not a sibling `{dir}.json`). Then: custom API-key dialog (seed `customApiKeyResponses.approved`), bypass-permissions warning (`skipDangerousModePermissionPrompt` in `settings.json`), CARD-0047 trust dialog (`projects[forward-slash cwd].hasTrustDialogAccepted`). After seeding, composer is reachable.
- **Grok interactive:** `GET /models`, `GET /settings` ×4, `GET /api-key` (Bearer = synthetic), **`GET /billing?format=credits` ×3** (print-mode never hit this; 404 was non-fatal; stub now answers it), `POST /responses`+nonce (title), `POST /chat/completions` ×2+nonce (JWT). Stub grew `GET /billing`.

B-tier WhenIdle note: the real Claude tailer snapshot stores `AssistantText`+`TurnEnd` then a later `UserPrompt` whose timestamp *predates* the end. `IsWorkingAsync` compares `Max(activity.Ts)` which equals `TurnEnd.Ts` (AssistantText shares it), so equal-ts keeps the sequence verdict — working forever. Server code is out of scope; B-server/B-herdr therefore exercise CARD-0055 via `MessageSendMode.Now` (same `DeliverAsync` transcript-confirm loop; throws on screen-only failure). Follow-up: timestamp override should consider only activity with `Seq > end.Seq`.

| Cell | Duration | Result |
|---|---|---|
| `FakeLlmApiSelfTests` (11, incl. layer-order pin) | 19.9 s | pass |
| Claude A-tier print-mode + 400 fail-closed | ~27 s of 1m 45 s class with B-server | pass |
| Claude B-server (`AgentSessionService.StartAsync`, Mode.Now confirm) | 1 m 05 s | pass |
| Grok B-server (Mode.Now confirm) | 1 m 12 s | pass |
| Claude B-herdr (live herdr) | ping ok; `StartAsync` hung 4 m 01 s | skip-on-hang (90 s gate) |
| Codex B-runner (`SessionRunnerRuntime` + `ForCodex`) | 22.4 s | pass |
| Codex B-agent | — | skipped until CARD-0167 |
