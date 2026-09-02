# CARD-0324 — Pool Grok delegates die on the sign-in screen: detect it, fail fast, name the cause

**Date:** 2026-09-02
**Card:** CARD-0324 (GitHub #28). Predecessors: CARD-0256 (`0a27bd54`, truthful empty-Stopped
labeling + repeat block), CARD-0315 (`e9dc9e3b`, `GrokTrustPromptDetector` for the directory-trust
dialog), CARD-0286 (`ef9afc3c`, `AuthenticationRequired` for the Codex 401 turn-kill).
**Verdict in one line:** the pool path has no sign-in detector; Grok 1.0.13's quiet OAuth
device-approval screen passes `WaitForReadyAsync`, the brief is typed into a screen with no
composer, Grok exits on its own when its sign-in ceiling lapses, and the sweep can only say
`StoppedBeforeFirstPrompt`. The missing `auth.json` is not an intra-machine refresh race; it is
Grok clearing its own credential store after a *permanent* refresh failure, a documented code path
that leaves exactly the `auth.json.lock`-only state the card observed.

---

## 0. Where the evidence came from, and what could not be verified here

The incident ids on the card (`task 1b50ad57`, session `7966b4e5-…`, and the ClaudeCode siblings
`75addf2c`/`fdb49312`/`e61435de`/`a10de84e`) do **not** exist on this desktop's server (17202) or
runner (17204, up since 2026-08-22 with no restart and no such session in its list). The standing
profile the card contrasts against (`gkp.ps1`) lives at `C:\Users\mike.ciechan\.local\bin\gkp.ps1`
per `docs/ai-agent-tui-configuration.md`, i.e. the work laptop. **The incident happened on the
laptop's Antiphon instance.** Everything below is therefore reconstructed from four sources that do
not depend on that box:

1. **The shared code path** (identical on both machines): `AgentTaskDispatcher.BuildLaunchSpec` →
   registry `grok` definition (`grok.exe --always-approve --no-alt-screen`, `Env: {}`) →
   `AgentSessionLaunchQueue.EnqueueInteractiveSession` → `AgentSessionService.LaunchInteractiveProcessAsync`
   → `RunnerGrokAdapter.WaitForReadyAsync` → `_messageQueue.FlushSessionAsync` types the queued brief.
2. **Grok 1.0.13's own binary and bundled docs** (`~/.grok/bin/grok.exe`, `~/.grok/docs/user-guide/02-authentication.md`),
   read for the sign-in screen copy and the credential-clearing log strings.
3. **Grok's structured log on this desktop** (`~/.grok/logs/unified.jsonl`, 15 581 rows,
   2026-09-02 11:47Z → 21:06Z): 21 launches today, every one `has_cached_token: true`; two
   lock-serialised OIDC refreshes (13:31Z and 19:26Z, token lifetime 6 h, proactive refresh 300 s
   before expiry) with siblings adopting the new token ("refresh adopted sibling token pre-lock",
   "refresh used disk token"). No sign-in screen, no credential clear, on this machine, today.
4. **A live measurement** (this session, 21:18Z): `grok.exe` launched through the production runner
   with `GROK_HOME` pointed at an empty directory and a never-seen cwd. Captured rendered screen at
   t=4 s, quiet thereafter, killed at 24 s:

   ```
     ~/AppData\Local\Temp\...\scratchpad\cwd-fresh
                                                         Connecting...
                                         Approve in your browser to finish signing in.
                                                           FYED-XF4N
                                            Make sure your browser shows this code.
                                            If it doesn't open, click here to copy.
                                       Copying not working? Click here to show full URL.
                                                    Waiting for approval...
                                                         ctrl+q  quit
   ```

   Grok's log for that launch: `AuthManager::new auth.json load result {"read":"error","path_exists":false}`
   ×7, `auth: initialize() ... has_current: false`, `auth started {"method":"grok.com"}`,
   `auth: inline auth flow {"headless": false, "reauth": false}`, then `startup phase running long
   {"phase":"app_init","open_ms":10076}`. **No directory-trust dialog appeared first**: sign-in gates
   trust. The empty home gained no `auth.json` and no `auth.json.lock`.

What this plan could **not** verify and says so: the exact laptop screen (the card quotes "Paste your
token here…", which is the welcome view's token input — `crates/codegen/xai-grok-pager/src/views/welcome/mod.rs`
strings: `Login with grok.com`, `Paste your token here`, `Switch account`, `Logout`; the measured
device-approval screen above is the sibling view from `xai-grok-pager-minimal/src/auth.rs`); and
the precise trigger of the laptop's credential clear (Section 2 gives the mechanism and the one
grep that will settle it there in thirty seconds).

---

## 1. Why the brief was swallowed (confirmed against the shipped code)

- `RunnerGrokAdapter.WaitForReadyAsync` is quiet-after-visible (`GrokReadyQuietPeriodMs` 1 000 ms,
  max 60 s) → `ClearStartupTrustPromptAsync` (CARD-0315) → `GrokReadyMinTotalWaitMs`. The sign-in
  screen is visible at ~4 s and then silent; the trust detector does not match it. **Ready is
  reported true.**
- `LaunchInteractiveProcessAsync` then flips the session `Running` and calls
  `_messageQueue.FlushSessionAsync`, which delivers the delegation brief (bracketed paste + Enter)
  into a screen whose only bound key is `ctrl+q`. Nothing echoes, no `updates.jsonl` is created
  (Grok creates the session directory lazily at first submit, and the directory key is the
  URL-encoded **cwd**, not the session id — the card's "no `~/.grok/sessions/<sessionId>` dir" check
  looked in the wrong place, but the conclusion was right: nothing was ever submitted).
- Grok's sign-in flow has a 300 s ceiling ("you have 300 seconds — enough for a browser round trip",
  02-authentication.md; `Auth recovery exhausted; re-authentication required` is the binary's
  terminal string). A process that gives up on the sign-in exits **0**. That is under the dispatcher's
  10-minute `FailNeverStartedAsync` watchdog, so the dead-session reconciler sees `Stopped`,
  `ProcessExit(0)`, zero transcript rows, and correctly writes `StoppedBeforeFirstPrompt` — the
  label CARD-0256 made honest, which is exactly why the card says it "did not stop it". (The exit-0
  timing is inferred from the documented ceiling, not measured; it does not change the fix.)
- A `false` from `WaitForReadyAsync` already fails fast today: `WaitForReadyOrThrowAsync` throws
  `InvalidOperationException("Agent process did not become ready.")`, the launch catch kills the
  adapter (`KillAndDisposeAsync`), marks the session `Failed` with that text and
  `SessionTerminationSource.SystemRequest`, and the reconciler fails the task with the session's
  `FailureReason` (`AgentTaskLiveness.ClassifyFailure` first arm) and a **null** failure code. So the
  plumbing for "fail fast" exists; what is missing is (a) the detector and (b) a reason and code
  that name *sign-in* so the repeat guard, the attention feed and the orchestrator bundle can act
  on it.

The catalog already promised this: `ProviderContractCatalog` (Grok, `BlockingStartupModal`) says an
unauthenticated `GROK_HOME` "parks on a device-code login that swallows input (measured 1.0.5) —
fail-fast, never auto-answered". **"Fail-fast" was documented intent with no implementation**, and
the 1.0.5 wording ("device-code login") no longer matches 1.0.13's OAuth device-approval screen.

---

## 2. Why `auth.json` was absent (root cause, from Grok's own strings and docs)

Grok 1.0.13's refresh path, as the binary logs it (all strings present in `grok.exe`; the first
group was observed live in this desktop's `unified.jsonl` today):

| Log string | Meaning |
|---|---|
| `auth lock: attempting acquire (timeout=…)` / `auth lock: acquired (pid=…)` on `auth.json.lock` | every refresh is serialised through a lock file. **The lock file is never deleted**; it holds `<pid>:<unix-ts>` of the last holder (`46160:1788377353` here). `auth.json.lock`-only is therefore not evidence of a hung refresh. |
| `auth: refresh adopted sibling token pre-lock` / `auth: refresh used disk token` | a process about to refresh re-reads disk before and under the lock and adopts a sibling's newer token instead of refreshing with its stale one. Seen 9× and 2× today. |
| `auth.refresh.success` / `auth update disk written` | the winner writes the rotated `key` + `refresh_token`. |
| `auth: sibling-rotation detected; demoting to transient` (`tried_rt_prefix` ≠ `disk_rt_prefix`) | a refresh that the IdP rejected is **not** treated as fatal when a sibling had already rotated the refresh token. |
| `auth: adopted sibling token after PermanentFailure` | same guard, other branch. |
| **`auth: cleared credentials after permanent refresh failure`** (and `failed to clear credentials … write_failed`) | when the IdP rejects the refresh token **and the token on disk is the one that was just tried**, Grok deletes the scope from `auth.json`. `02-authentication.md`: "When a token can't be refreshed, Grok prompts you to sign in again." |
| `auth: scope removed from auth.json`, `auth: removed stale WebLogin scope from auth.json` | further removal paths (policy / stale scope). |

Conclusions:

1. **Concurrent pool workers sharing one `~/.grok` are not, by themselves, the race.** Grok 1.0.13
   serialises refreshes and adopts sibling rotations; 1.0.13's changelog entry "Fixed startup
   timeouts caused by concurrent auth refreshes across multiple sessions" is the vendor closing the
   remaining startup-side contention. On this desktop, with 8–11 concurrent grok processes all day,
   two refresh windows passed cleanly.
2. **The state the card saw (`auth.json` gone, `auth.json.lock` present, every new pool launch on
   the sign-in screen) is the signature of `cleared credentials after permanent refresh failure`.**
   The refresh token on the laptop was rejected by `auth.x.ai` (`invalid_grant`-class) while it was
   still the token on disk. Known causes of that class: the account's refresh-token family rotated
   or was revoked elsewhere (a `grok login` / "Switch account" / "Logout" on another device or in
   the standing TUI, IdP session lifetime, a team-policy change), or `grok logout` on the box.
   The sibling guard cannot help there because no sibling holds a newer token.
3. Once cleared, the loss is **global per `GROK_HOME`**: every registry-path pool worker (which
   inherits the user's `~/.grok`) lands on the sign-in screen until a human runs `grok login`. The
   standing `grok-gkp-project` profile kept working because `gkp.ps1` (llm-key-proxy) authenticates
   with its own API key (`Logged in with API key` path), not the OAuth store — precisely the
   asymmetry the card noticed.
4. Mid-session, Grok surfaces the same event as a turn-level modal — `Your session expired and
   your sign-in helper could not renew it in the background. Run /login to sign in again.` — which
   is a *different* screen (a working session's turn fails) and belongs to the API-error path
   (CARD-0083/0286 family). Named here so nobody expects the launch detector to catch it; it is a
   follow-up card, not this one.

**Thirty-second confirmation on the laptop (do this first, S0):**

```powershell
Select-String -Path "$env:USERPROFILE\.grok\logs\unified.jsonl" -Pattern 'cleared credentials|sibling_rotation|permanent refresh|scope removed|auth: logout|invalid_grant' |
  ForEach-Object { $_.Line.Substring(0, [Math]::Min(320, $_.Line.Length)) }
```

Expect one `auth: cleared credentials after permanent refresh failure` row (with `tried_rt_prefix`
== `disk_rt_prefix`) shortly before the first dead pool launch, or an `auth: logout` row. Record the
timestamp and preceding `oidc refresh` rows on the card. If neither appears, the clear came from
outside Grok (a script deleting the file) and Section 5's probe still fails fast on it.

---

## 3. Design

Three layers, cheapest first, each independently correct. The screen detector is the ground truth;
the file probe is the fast path; the failure code is what makes the fleet react.

### 3.1 `GrokSignInPromptDetector` (in-band, the truth)

`src/Antiphon.Agents.Pty/GrokDetectors.cs`, beside `GrokTrustPromptDetector`, same shape
(compact-normalised, lowercase, whitespace stripped). Anchors, any one of which is a match, all
taken verbatim from `grok.exe` 1.0.13 and the measured screen:

| Anchor (compact) | Source screen |
|---|---|
| `approveinyourbrowsertofinishsigningin` | measured device-approval view |
| `waitingforapproval` | measured device-approval view |
| `makesureyourbrowsershowsthiscode` | measured device-approval view |
| `pasteyourtokenhere` | welcome view token input (the card's laptop screen) |
| `openthisurlinyourbrowsertoapprove` | device-code fallback when no browser opens |
| `couldnotopenabrowser` | same fallback |
| `signintogrok` | `auth.rs` header |
| `loginwith` **and** `ctrl+q` on the same screen | welcome view's login button (`Login with grok.com` / custom `auth_provider_label`) with the quit hint that only the pre-session views show |

`IsVisibleOnScreen(rendered)` only — no raw-buffer arm. The trust detector needed raw for the
"did it leave" loop; this one never answers anything, so the stale-buffer trap has nothing to
protect and a raw match could only add false positives. Evaluated **only inside
`WaitForReadyAsync`** (launch preamble, before any turn), so an assistant later *talking about*
signing in cannot trip it.

`RunnerGrokAdapter.WaitForReadyAsync` ordering becomes: quiet-after-visible → **sign-in check** →
trust check → min-total wait. Sign-in first because the measurement shows it gates the trust
dialog. On a match: `LogError` with the rendered screen and the resolved `GROK_HOME`, **type
nothing**, set `NotReadyReason`, return `false`. The existing launch catch then kills the process
(which also ends Grok's 300 s approval poll so no orphan sign-in can later "approve" itself).

### 3.2 A reason and a code that survive to the task

`IAgentProtocolAdapter` gains one optional member:

```csharp
/// Why the last WaitForReadyAsync returned false, when the adapter can name it. Null keeps
/// today's generic "Agent process did not become ready." Set with the launch block kind.
AgentLaunchBlock? LaunchBlock => null;
```

with `public sealed record AgentLaunchBlock(AgentLaunchBlockKind Kind, string Reason)` and
`enum AgentLaunchBlockKind { ProviderSignInRequired = 1, TrustDialogNotCleared = 2 }` in
`server/Application/Dtos` (Codex/Claude keep returning null this card; Claude's "TUI painted but
deaf" and the trust-not-cleared arms are obvious later fillers).

`AgentSessionService.WaitForReadyOrThrowAsync` throws a new
`AgentLaunchBlockedException(block)` (`server/Application/Exceptions/`, message = `block.Reason`)
instead of the bare `InvalidOperationException` when a block is present. Both launch catches
(`StartAsync` and `LaunchInteractiveAsync`) already record `ex.Message` as
`session.FailureReason`; they additionally persist the kind:

- New nullable column `AgentSession.LaunchBlock` (`SessionLaunchBlock` enum: `None = 0`,
  `ProviderSignInRequired = 1`, `TrustDialogNotCleared = 2`), migration
  `AddSessionLaunchBlock`, same shape as CARD-0256's `TerminationSource`. Durable, queryable,
  and the dead-session sweep reads rows, not exceptions.
- `AgentTaskLiveness.SessionSnapshot` gains `LaunchBlock`; `ClassifyFailure` maps
  `ProviderSignInRequired` → `AgentTaskFailureCode.AuthenticationRequired` **before** the
  existing-`FailureReason` arm (the reason text is kept verbatim; only the code is added).
  `AuthenticationRequired`'s doc comment widens from "the marked turn was killed by an HTTP 401"
  to "structural authentication failure: a 401 turn-kill (CARD-0286) or a provider sign-in screen
  at launch (CARD-0324); never a retryable transport glitch". The create/retry repeat guard
  (`FindLaunchFailureRepeatAsync`) already blocks a second identical dispatch on that code, and
  `orchestrator.md` already tells the parent it is terminal — both come for free.
- Rejected alternative: sniff a `SignInRequired:` prefix on `FailureReason` in the sweep. Zero
  migration, but it turns free text into a taxonomy, which CARD-0256 explicitly refused to do.

The reason text (one constant, `GrokSignInPromptDetector.BlockReason(grokHome)`):

> ProviderSignInRequired: Grok opened on its sign-in screen ("Approve in your browser to finish
> signing in" / "Paste your token here") — the credential store `<GROK_HOME>\auth.json` has no
> usable session. Nothing was typed into it. Run `grok login` (or `grok login --device-auth` on a
> headless host) as the Windows user that runs the session-runner, then re-dispatch. Every Grok
> pool launch on this machine will fail the same way until then.

`GROK_HOME` resolves exactly as the tailer does (`GrokTranscriptTailer.ResolveGrokHome(spec.Env)`:
launch env → process env → `~/.grok`), so a profile with an isolated home names the right file.

### 3.3 Fleet visibility: one incident per credential store, not one per corpse

`AgentIncidentKind.ProviderSignInRequired = 45` (appended after `BootWedged = 44`), severity
**Critical** (NeedsHuman: no retry can fix it; matches `HandleApiErrorTurnAsync`'s rule), raised from
the launch catch the way `RecordRunnerBuildStaleAsync` is (session-scoped write, then the attention
projection), **deduped on `(AgentKind.Grok, grokHome)`** rather than per session — the fifth dead
worker must not be the fifth Critical row. Cleared when a later Grok launch on the same home reports
ready (the same "later success closes the episode" rule `QueuedInputNeverConverted` uses). Detail
carries the reason text plus the `grok login` remedy.

### 3.4 Pre-launch credential probe (fast path, saves the worktree and the 60 s)

`GrokCredentialStore.Inspect(grokHome, launchEnv)` in `src/Antiphon.Agents.Pty` (pure, file-only):

| Finding | Rule |
|---|---|
| `ApiKeyAuth` | `XAI_API_KEY` or `GROK_CODE_XAI_API_KEY` present in the merged launch env, or a `GROK_AUTH_PROVIDER_COMMAND` → skip; the store is not what authenticates this launch (this is the gkp profile's case). |
| `Absent` | `auth.json` missing (honours `GROK_AUTH_PATH` when set). The lock file is ignored — it is a permanent artefact. |
| `Empty` | file present but no scope object with a non-empty `key`. (Shape measured: `{"https://auth.x.ai::<client>": {"key", "auth_mode": "oidc", "refresh_token", "expires_at", …}}`.) |
| `Present` | a scope with `key`. `expires_at` in the past is **still Present** when `refresh_token` is non-empty — Grok refreshes on launch (`oidc refresh enter {"reason":"PreRequest","is_expired":true}` succeeded today). Only an expired key with no refresh token is `Empty`. |
| `Unreadable` | parse/IO error → treat as Present (the screen detector is the backstop; a probe must never block a launch that would have worked). |

Applied in `AgentTaskDispatcher` for **registry-path `AgentKind.Grok` launches only** (the
`program.ProfileId is null` branch, just before `EnqueueInteractiveSession`): `Absent`/`Empty` →
do not spawn; fail the task with `AuthenticationRequired`, the Section 3.2 reason, the Section 3.3
incident, and the parent completion note (via `FailAndNotifyAsync`'s non-destructive tail — there
is no session to kill); release the worktree the way a Blocked create does. Setting
`Agents:GrokCredentialProbeEnabled=false` disables only this layer. The standing/profile path
(`BuildLaunchSpecAsync` with a profile) is left alone: profiles may legitimately authenticate
differently, and `gkp` proves it.

### 3.5 Create-time refusal, the quota doctrine (recommended, small, separable)

`POST /api/agent-tasks` for a registry-Grok task when the probe says `Absent`/`Empty`: **409
`provider_sign_in_required`**, problem-details extension `{ "agentKind": "Grok", "grokHome": …,
"remedy": "grok login" }`, next to `SubscriptionQuotaLowException` (`subscription_quota_low`) and
following AGENTS.md's rule for it verbatim: a launch refusal the caller resolves by choosing another
allowed kind or passing the documented override (`allowUnauthenticatedProvider: true`, for the
operator who is about to log in and wants the task queued). `delegate.ps1` surfaces the 409 the way
it surfaces quota. Rationale: the orchestrator learns in 200 ms instead of after a dead task, and
never silently reroutes. Section 3.4 stays as the dispatch-time backstop because credentials can be
cleared between create and launch.

### 3.6 What this plan deliberately does not do

- **No auto-answer, no auto-login.** The screen wants a human in a browser; typing into it is the
  bug. `--device-auth` in a pty is the same wait with a code.
- **No `XAI_API_KEY` fallback in the registry `grok` definition by default.** It would make a
  cleared store degrade to API-key auth instead of the sign-in screen (Grok's precedence: session
  token → `XAI_API_KEY`), which is real resilience — but it moves pool spend from the SuperGrok
  subscription to console.x.ai metered billing without anyone choosing that. Decision D2 below.
- **No `GROK_HOME` isolation per pool worker.** An isolated home is an *unauthenticated* home
  (measured); it would make every launch hit this screen.
- **No change to Grok's refresh cadence** (`GROK_AUTH_EARLY_INVALIDATION_SECS`) — the vendor's
  sibling guard is the right layer and it works.

---

## 4. Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| D1 | Reuse `AuthenticationRequired` vs a new `SignInRequired` failure code | **Reuse.** The repeat guard, the attention severity and `orchestrator.md`'s "terminal, do not re-dispatch this kind" paragraph all already key on it; the reason text carries the distinction. A new code would need three more edits to say the same thing. |
| D2 | Put `XAI_API_KEY` (or the gkp-style `GROK_CODE_XAI_API_KEY` + proxy) on the registry `grok` definition as a fallback | **Not by default.** Offer it as a documented `Agents:Definitions:grok:Env` opt-in in `docs/agent-credentials.md` with the billing consequence spelled out. If the owner wants pool Grok to survive an OAuth clear unattended, this is the one switch that does it. |
| D3 | Ship the 409 create-time refusal (3.5) in this card or a follow-up | **This card, last slice.** It is ~40 lines beside an existing exception and is what turns "the delegate died" into "pick another kind". |
| D4 | Migration for `AgentSession.LaunchBlock` vs the prefix sniff | **Migration.** One nullable int, CARD-0256/0316 precedent, and the sweep keeps reading rows. |

---

## 5. Slices, in order (each lands green on its own)

**S0 — Confirm on the laptop (no code).** Run the Section 2 grep on the work laptop; paste the
`cleared credentials` / `logout` row (timestamps, `tried_rt_prefix`/`disk_rt_prefix`, never the
token) into the card. Run `grok login` there. This unblocks the live Execute dispatches today and
is independent of everything below.

**S1 — Detector + adapter (fail fast, truthfully).**
`GrokSignInPromptDetector`; `RunnerGrokAdapter.WaitForReadyAsync` sign-in check before trust,
`LaunchBlock` populated, nothing typed; `IAgentProtocolAdapter.LaunchBlock` default-null member;
`AgentLaunchBlockedException`; `WaitForReadyOrThrowAsync` throws it. Session `FailureReason` now
names sign-in. Tests: `GrokSignInPromptDetectorTests` (each anchor as its own fixture, including the
measured screen verbatim and a `Paste your token here` welcome variant; negatives: ready screen,
trust dialog, a working turn, a reply that quotes "Waiting for approval"),
`RunnerGrokAdapterSignInPromptTests` (fake `ISessionRunnerClient` as in
`RunnerGrokAdapterTrustPromptTests`: sign-in screen → `false`, `Inputs` empty, `LaunchBlock.Kind`
== `ProviderSignInRequired`, reason names `GROK_HOME` and `grok login`; sign-in screen that also
contains the trust text → still no `y`; trust-only screen unchanged from CARD-0315).

**S2 — Persist the block, code the task, raise the incident.**
`SessionLaunchBlock` enum + `AgentSession.LaunchBlock` + migration; both launch catches set it from
the exception; `AgentTaskLiveness.SessionSnapshot.LaunchBlock` + `ClassifyFailure` mapping;
`AgentIncidentKind.ProviderSignInRequired = 45` raised from the launch catch, deduped on
`(Grok, grokHome)`, closed by the next ready Grok launch on that home; attention projection row.
Tests: `AgentTaskLivenessTests` new verdict row; `AgentTaskDeadSessionReconciliationTests` table
gains "Failed session with `ProviderSignInRequired` → task Failed, code `AuthenticationRequired`,
reason verbatim"; `SessionTerminationSourcePersistenceTests`-style round trip for the new column;
incident dedup test (two dead sessions on one home → one incident; a ready launch closes it).

**S3 — Pre-launch probe.**
`GrokCredentialStore.Inspect`; dispatcher gate on the registry-Grok branch;
`Agents:GrokCredentialProbeEnabled` (default true) in `AgentRegistrySettings` + validator line.
Tests: `GrokCredentialStoreTests` over temp directories for every row of the 3.4 table (`Absent`,
lock-only, `Empty` scope, `Present`, expired-with-refresh-token, `GROK_AUTH_PATH` override,
`ApiKeyAuth` skip, unreadable → Present); dispatcher test: registry Grok task with an `Absent`
store fails `AuthenticationRequired` **before** `EnqueueInteractiveSession` is called, parent note
delivered, worktree released, incident present; a profile-path Grok task is never probed.

**S4 — fakegrok + runtime E2E.**
`ANTIPHON_FAKE_SIGN_IN=1` makes `Antiphon.FakeGrok` paint the measured screen, ignore every key
except `ctrl+q`, and exit 0 after `ANTIPHON_FAKE_SIGN_IN_EXIT_MS` (default 3 000 in tests) — the
real 300 s ceiling in miniature. E2E in `Antiphon.Tests` (session-runtime lane): dispatch a
registry-Grok pool task against fakegrok in sign-in mode with the probe disabled → the session fails
inside the ready window, fakegrok's input log is empty (**the brief was never typed**), the task is
`Failed`/`AuthenticationRequired`, the parent note names `grok login`, the incident exists once.
Headed canary `GrokSignInCanaryTests` (`[Explicit]`, `ANTIPHON_HEADED_TESTS=1`, `GkSession`
helpers, `GROK_HOME` = fresh temp dir, `GROK_DISABLE_AUTOUPDATER=1`): the detector matches the live
screen within 10 s. Document that the canary opens a browser tab to `auth.x.ai` on the host and
must be killed, never approved.

**S5 — 409 at create (D3) + docs.**
`ProviderSignInRequiredException` (`provider_sign_in_required`), `AgentTaskService` create/retry
check on registry-Grok, `allowUnauthenticatedProvider` override, `delegate.ps1` message,
`docs/antiphon-api.md` row beside `subscription_quota_low`. Docs in the same slice:
`docs/agent-kinds.md` §5 (replace the 1.0.5 "device-code login" sentence with the measured 1.0.13
screen, the credential-clear mechanism, and the `grok login` remedy; note that sign-in gates
trust), `docs/session-runtime-invariants.md` new gotcha, `ProviderContractCatalog` Grok
`BlockingStartupModal` text (now true) + `ProviderContractCatalogTests`,
`docs/agent-credentials.md` (auth.json is Grok's, never copied; D2 opt-in), `server/Bundles/orchestrator.md`
one sentence: `AuthenticationRequired` from a Grok pool launch means the host needs `grok login`;
do not retry Grok, do not switch profile to hide it; AGENTS.md "Immediate safety triggers → Cards
and tracker" gains the 409 next to the quota sentence.

Estimate: S1+S2 ≈ half a day, S3 ≈ 2–3 h, S4 ≈ 3 h (fakegrok knob + E2E + canary), S5 ≈ 2 h.

---

## 6. Verify

Build to an alternate output while the daemons hold `bin/`:

```powershell
dotnet build Antiphon.sln --property:OutputPath=bin-c324/
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c324/ -- --treenode-filter "/*/*/GrokSignInPromptDetectorTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c324/ -- --treenode-filter "/*/*/RunnerGrokAdapterSignInPromptTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c324/ -- --treenode-filter "/*/*/RunnerGrokAdapterTrustPromptTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c324/ -- --treenode-filter "/*/*/AgentTaskLivenessTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c324/ -- --treenode-filter "/*/*/AgentTaskDeadSessionReconciliationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c324/ -- --treenode-filter "/*/*/GrokCredentialStoreTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c324/ -- --treenode-filter "/*/*/ProviderContractCatalogTests/*"
Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-c324 | Remove-Item -Recurse -Force
```

Then the migration on live Postgres (`dotnet ef database update` through the documented path),
`pwsh -NoProfile -File scripts/restart-apphost.ps1`, and one real proof: on a box whose `~/.grok`
has been signed out with `grok logout` (the owner's call — this desktop is currently signed in and
running eight Grok sessions), `delegate.ps1 -Kind Grok -Worktree` must come back 409
`provider_sign_in_required` (S5) or, with the override, a task that is `Failed`/`AuthenticationRequired`
within ~10 s naming `grok login`, with no worktree left behind and one Critical attention row.
`grok login`, re-dispatch, and the incident row closes on the first ready launch.

---

## 7. Related and follow-ups (file as cards, not in this one)

- **Mid-session sign-in expiry** (`Your session expired … Run /login to sign in again`, `Auth
  recovery exhausted`): a turn-time modal on a working delegate. Belongs to the API-error /
  `ApiErrorClassifier` family (CARD-0083 S4 "Grok usage-limit shape" is the same survey); not a
  launch detector.
- **CARD-0312** (post-start liveness probe) would catch any *unknown* quiet screen this detector
  does not name; this card is the named fast path for the one screen we have measured.
- **ClaudeCode siblings on the card** (`75addf2c`/`fdb49312`, `e61435de`/`a10de84e`, exit 1 at
  "Yes, I trust this folder", empty transcript, `failureCode` null): a different provider and a
  different dialog (`ClaudeBlockingPromptDetector` should have answered it — `TrustNotCleared`
  today only logs). Same `LaunchBlock` plumbing from S2 gives it a code for free; file it against
  that plumbing.
- **Trust-dialog-not-cleared** (CARD-0315's `false` arm) gets `LaunchBlockKind.TrustDialogNotCleared`
  as a one-line follow-on once S2 exists.
