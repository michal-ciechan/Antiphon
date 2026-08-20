# Delegate/session reliability — test coverage plan

**Date:** 2026-08-20 (planning task `0bb2385d`, Plan/High)
**Status:** plan only. No implementation in this pass.
**Scope:** the "dispatch a session, get real work back" pipeline — launch spec → argv → pty →
readiness → brief delivery → transcript bind → report → teardown.
**Cards in scope:** CARD-0101 (shipped fix, coverage gap), CARD-0103 (open), CARD-0102 (open),
CARD-0073 (closed, same root cause as CARD-0101).

---

## 0. Verdict up front

The suite is large and the unit tier is genuinely good. The gap is **not** "we need more tests" —
it is four *structural blindnesses* that make whole classes of defect unreachable by any test that
currently exists. Each one is a property of the harness, not of a missing assertion, so adding more
tests at the existing tiers cannot close them.

| # | Blindness | Evidence | What it hid |
|---|---|---|---|
| **B1** | **The fake is a .NET child.** `fakeclaude --echo-args` prints argv *as .NET parsed it*, and .NET's parser accepts a doubled `""` inside a quoted argument as one escaped quote. `CommandLineToArgvW` — what `claude.exe`, node and bun use — splits there instead. | `LaunchArgvGuardTests.Portas_format_does_not_round_trip_the_shape_that_shredded_production` measures **9 argv entries where 3 were intended** for the exact literal used by `SessionMessageQueuePtyIntegrationTests.Launch_args_reach_the_child_process` (`:372`). That test passed for months **on the failing shape**. | CARD-0101 |
| **B2** | **The test suites run the wrong pty backend.** `PtyBackendEnvGuard` (one copy per pty-touching assembly, CARD-0045) clears `ANTIPHON_PTY_BACKEND` and pins unqualified resolution to `InboxConhost`. Production runs `modern` (`src/Antiphon.SessionRunner/appsettings.json` + the AppHost's server env). | `PtyBackendEnvGuardTests.The_suite_ignores_an_inherited_pty_backend` asserts `PtyBackendPolicy.Resolve().Backend == InboxConhost`. CARD-0101's defect was in `ModernConPtyConnection.BuildCommandLine`. | CARD-0101 |
| **B3** | **The only real-Claude E2E substitutes exactly the layer that broke.** `DelegationSequencingE2ETests` replaces the session-runner daemon with an in-test `TestSessionRunner`; `DelegationPipelineE2ETests` stops at the task row by design. | Both class doc-comments say so. So argv composition, readiness, queue delivery, transcript binding and pty-host lifecycle sit outside every real-Claude E2E. | CARD-0101, CARD-0103, CARD-0073 |
| **B4** | **The bundle→argv seam is one step short.** `DelegateBundleLaunchTests` composes the **real** bundles through the **real** `AgentTaskDispatcher.BuildLaunchSpec` — and stops at the `Args` array. Nothing ever takes those real args to a command line and back. | The bundle that shredded production (`server/Bundles/delegate-basics.md:18`) was already loaded, in-process, in a passing test, on the day it broke. | CARD-0101 |

`B4` is the cheapest fix in this document and the one that would have turned three days into zero.

**Per-bug answer to "would a fake ever exercise this?"** — asked explicitly in the brief:

| Bug | Needs a real subprocess? | Why |
|---|---|---|
| CARD-0101 (argv shred) | **No — but it needs a strict parser.** | The defect is pure string composition. `LaunchArgvGuard.ParseArgv` (real `CommandLineToArgvW`) reaches it with **no process at all**. A subprocess only helps if the child parses like `claude.exe`; today's fake does not (B1). Fix the parser, not the process. |
| CARD-0103 (quiet ≠ ready) | **No, if the fake can be made deaf.** | The defect is "the readiness rule accepts silence". A fake that paints and then refuses to read stdin for N ms reproduces it deterministically, with none of the load-flakiness of the real trigger. |
| CARD-0102 (pty-host leak) | **Yes.** | It *is* process lifecycle. Only real detached hosts can leak. |
| CARD-0073 (bind regression) | **Real subprocess, not real model.** | Needs a child that honours `--session-id` and writes a transcript where the tailer looks. The fake can be taught both (see P2-1). |

---

## 1. What already exists (so nothing here re-invents it)

Read before proposing anything; all of this is load-bearing and stays.

- **Unit tier, CARD-0101:** `ModernConPtyCommandLineTests` (7 tests, round-tripped through the real
  Win32 `CommandLineToArgvW`), `LaunchArgvGuardTests` (12, including negative cases fed the *old*
  broken escaping and a reflection check that the Porta replica still matches the shipped assembly).
  `LaunchArgvGuard.VerifyOrThrow` is wired into **both** launch paths
  (`ModernConPtyConnection.cs:153`, `PtyAgentRunner.cs:90`), so a future escaping defect fails the
  launch loudly instead of running a shredded delegate. **This tier is sufficient for the mechanism
  and insufficient for the content** — see §2.
- **Readiness tier, CARD-0047/0048:** `ClaudeStartupTrustPromptTests`,
  `RunnerClaudeAdapterTrustPromptTests` (with `ScreenScriptedRunnerClient` — a scripted-screen double
  that already stands in for the runner client), `Da1StartupResponderTests`, `ModernPtyDa1Tests`,
  `WaitForQuietAfterVisibleTests`, `ClaudeTrustPromptCanaryTests` (headed).
- **Delivery tier:** `SessionMessageQueueDeliveryVerificationTests`, `ComposerDeliveryEvidenceTests`,
  `PromptSubmissionMatchTests`, `VerifiedPromptSubmitterTests`,
  `SessionMessageQueuePtyIntegrationTests` (through a real ConPTY + fakeclaude),
  `ClaudeSubmitConfirmCanaryTests` (headed, `[Explicit]`).
- **Binding tier:** `TranscriptAdoptionSafetyTests` (14), `TranscriptBindingIncidentTests` (4),
  `GrokTranscriptTailerTests`.
- **Full-pipeline template:** **`GrokDelegateEndToEndTests`** — real `delegate.ps1` over real HTTP →
  real `AgentTaskService` → real `AgentTaskDispatcher.TickAsync` → real `AgentSessionLaunchQueue` →
  **a real ConPTY launch of `fakegrok.exe` on the declared `modern` backend** → real
  `SessionMessageQueueService` delivery → real tailer/normalizer → self-firing settlement and
  pricing. Two named substitutions only (a three-line API relay; the event pump replaced by the
  runtime's own `SyncTranscriptAsync`). **This class is the proof that everything in §4 is
  buildable — there is no new test category to invent.**
- **An isolated runner already exists for tests:** `DirectSessionRunnerClient` runs an in-process
  `SessionRunnerRuntime` with its own `SessionLogPath` and `PtyHostLingerHours = 0.02`. Only
  `Antiphon.E2E`'s `AntiphonAppFixture` points at production (`17204`).
- **Fake capabilities already shipped:** `--echo-args`, `ANTIPHON_FAKE_TRANSCRIPT_PATH`,
  `ANTIPHON_FAKE_STDIN_CLIP`, `ANTIPHON_FAKE_BURST_MS`, `ANTIPHON_FAKE_STDIN_READ_DELAY_MS`,
  `ANTIPHON_FAKE_SWALLOW_ENTER`, `ANTIPHON_FAKE_PASTE_PLACEHOLDER`.
- **Periodic mechanisms that already exist:** `AgentSupervisorHostedService` (the documented home for
  "a clock nobody's turn owns" — already hosts the CARD-0067 channel sweep, the CARD-0082 compaction
  sweep and the API-error sweep, each every minute); `SessionReconciliationHostedService`; the
  runner's `SessionLivenessSweepService` and `SessionCpuWatchdogService`; **Windmill on server2**
  (tag `desktop`, SSHes into Windows) — already runs `scripts/cleanup-build-junk.ps1` weekly, and
  that script's own header says *do not re-add a local Scheduled Task*.
- **CI reality:** one GitHub workflow (`publish-nuget.yml`, `ubuntu-latest`, packaging only). There
  is **no** Windows runner and **no** test gate in CI today.

---

## 2. Is the shipped CARD-0101 unit coverage sufficient?

**No — it covers the mechanism, not the content, and the production bug was content.**

`ModernConPtyCommandLineTests` and `LaunchArgvGuardTests` both assert on *hand-written strings that
copy* the failing bundle line. Both would have stayed green if `delegate-basics.md` had been edited
to add a *different* hostile character (a trailing `\`, a `\"`), and neither reads a bundle file at
all. The launch-time guard closes the production hole (a shredded launch now throws before the
process exists) but it is a **runtime** brake: it turns silent corruption into a loud launch failure
at 3am, not into a red test at commit time.

What is missing is one seam, and it is one file away from an existing passing test: **take the real
composed `BuildLaunchSpec(...).Args` — which already contain the real bundle text — and round-trip
them through the real command-line composition for both backends.** That is `P0-1`.

`server/Bundles/delegate-basics.md` still contains two `"` today (lines 18 and 30). Both backends now
handle them correctly (`PtyAgentRunner` pre-escapes with the corrected CRT rule and hands Porta a
verbatim line), so this is not a live defect — it is a live *reason the test must exist*: the bundle
files are edited by agents, routinely, with no awareness that their punctuation reaches a command
line.

---

## 3. The general property behind CARD-0047 / CARD-0048 / CARD-0103

Three independent misses, months apart, three narrow fixes, none of which generalised:

| Card | Trigger | Fix |
|---|---|---|
| 0047 | trust modal makes no output | detect and answer *that modal* |
| 0048 | `OpenConsole` DA1 stall, 3.0 s silent | answer *that query* |
| 0103 | TUI painted but not draining stdin for 48–200 s under load | *unfixed* |

The shared property is exact and testable:

> **A session reported ready by `WaitForReadyAsync` must be able to consume input and render it
> within N seconds.**

Not "it stopped producing output." The cheapest honest probe — which CARD-0103's own "what a fix has
to cover" already names — is a **round trip that costs nothing**: write a short harmless token,
require it to appear in the composer, clear it with Ctrl+U before the real body is typed.

That property is testable at three tiers *without a real model*, and the test at the **interface**
tier is what makes the fourth occurrence impossible rather than merely fixed (`P1-2`).

**Should it run under induced load?** Recommendation: **no, not as a gate.** Reproducing CARD-0103 by
saturating 8 cores makes a timing assertion whose red/green depends on what else the machine is
doing — exactly the "flake cast" the client suite spent CARD-0069 dismantling. Model the *effect*
deterministically instead (a fake that is deaf for a declared interval), and treat load itself as a
**monitoring** signal (§6), not a test fixture. One exception worth building: a **non-gating**
`[Explicit]` load arm that records time-to-first-echo under N concurrent sessions, so the number that
matters is measured on demand rather than guessed.

---

## 4. Proposed additions

Priority = (severity × time-undetected) ÷ cost. `P0` items are the ones that would have caught the
longest-lived bug for the least work.

### P0-1 — `DelegateLaunchArgvIntegrityTests` (new)

`tests/Antiphon.Tests/Application/DelegateLaunchArgvIntegrityTests.cs` · **unit/integration, no
process, milliseconds** · closes **B4** · *would have gone red on `28afb5f`, the commit that
introduced the bug.*

For every `(AgentTaskKind × AgentTaskRole × AgentKind)` the dispatcher supports, and for every bundle
in `InstructionBundles.All` as an attachment:

1. `args = dispatcher.BuildLaunchSpec(task, agent, session, attached).Args` — the real composer over
   the real embedded bundle files (exactly what `DelegateBundleLaunchTests` already does).
2. Modern arm:
   `LaunchArgvGuard.VerifyOrThrow(exe, args, ModernConPtyConnection.BuildCommandLine(exe, args, verbatim: false), "modern ConPTY")`.
3. Inbox arm: the same pre-escape-then-verbatim composition `PtyAgentRunner.cs:82-108` performs.
4. Assert per-argument, not just per-count: the `--append-system-prompt` value must come back
   **char-for-char and length-equal**, and `--session-id` must be present at its intended index.

Plus a standalone property test over the catalog alone:

```
every InstructionBundles.All value, as a single argument, round-trips through both backends
```

so a bundle edit is caught even if no role currently composes it.

**Also add a hostile-content regression seed** — a synthetic bundle-shaped string carrying `"`, `\"`,
a trailing `\`, `{braces}`, a lone `\r`, and a 40 KB body — asserted through the same two arms. This
is what stops the test from only ever testing today's punctuation.

*Feasibility:* proven. Every ingredient (`BuildLaunchSpec` harness, `InstructionBundles`,
`LaunchArgvGuard`, `ModernConPtyConnection.BuildCommandLine`) is already used by passing tests in
these two assemblies. No new dependency, no process, no skip gate beyond `IsWindows`.

### P0-2 — Teach `fakeclaude` to parse argv like a native child

`src/Antiphon.FakeClaude/Program.cs` (+ `Antiphon.FakeGrok`) · **~25 lines** · closes **B1**.

Add `--echo-argv-strict`: call `GetCommandLineW()` and re-parse it through `CommandLineToArgvW`, then
print that vector (same `␟`-joined, newline-escaped format as `--echo-args`). Keep `--echo-args` —
the divergence between the two lines *is* the assertion.

Then:

- Upgrade `SessionMessageQueuePtyIntegrationTests.Launch_args_reach_the_child_process` to assert on
  the **strict** line, and give it a second arm on the **modern** backend
  (`DirectSessionRunnerClient(ptyBackend: "modern")`). Today it runs on `PinnedBackend` only.
- Mirror in `FakeClaudeContractTests`:
  `A_doubled_quote_argument_splits_for_a_native_parser_and_not_for_dotnet` — pins *why* the fake
  needed changing, so nobody "simplifies" it back.

*Why this is worth doing even though `LaunchArgvGuard` now exists:* the guard protects the two launch
paths we know about. The fake is the model every future pty test is written against; leaving it lying
about argv guarantees the next test written on it is blind in the same way.

### P0-3 — Alert on the census we already collect

`server/Application/Services/SessionReconciliationService.cs` + `AgentSupervisorHostedService` ·
**small** · closes half of **CARD-0102** · *the data already existed on 2026-08-20 and nobody was
told.*

`SessionReconciliationService`'s third pass already fetches the runner's full session list **once per
sweep, unconditionally** (CARD-0056), and the server log on 2026-08-20 09:38:24 already said
**"46 runner sessions with no DB row at all"**. That line is the leak, printed, four hours before a
human found it by hand. Nothing alerted.

Add `AgentIncidentKind.PtyHostCensusDiverged = 28` (Warning; **Critical** past a hard ceiling), raised
from the existing sweep when *either*:

- unclaimed runner sessions exceed `UnclaimedSessionAlertThreshold` (start at 10), or
- live `Antiphon.PtyHost` count exceeds runner sessions with a live agent child by more than
  `PtyHostSurplusAlertThreshold` (start at 5),

carrying the full census in the detail (hosts / claude.exe / runner sessions / DB rows / oldest age).
Dedup by the existing alert `DedupKey` so it does not become the CARD-0101 refusal storm.

**Hard constraint, inherited from CARD-0056 and non-negotiable:** *unclaimed never implies kill.* This
raises an incident. It does not reap.

*Feasibility:* the sweep, the incident enum, the severity/channel-bound escalation and the alert dedup
are all existing machinery. `SessionReconciliationServiceTests` (14 tests) is the home for the new
cases.

### P1-1 — The readiness round-trip, at two tiers

**(a) `RunnerAdapterReadinessTests` (new)** · `tests/Antiphon.Tests/Agents/RunnerAdapterReadinessTests.cs`
· **deterministic, no process** · catches the CARD-0103 *class*.

Extend the existing `ScreenScriptedRunnerClient` (already in `RunnerClaudeAdapterTrustPromptTests`)
with a **deaf** mode: it renders a settled composer, reports quiet, and *discards* writes for a
declared interval before it starts echoing them. Assert:

- `WaitForReadyAsync` does **not** return true while the terminal is deaf;
- it returns true within `ClaudeReadyMaxWaitMs` once echoing starts;
- it returns false (not "true, eventually") if the deaf window outlasts the max wait — a launch that
  cannot be talked to must fail as a launch, not park a message;
- the probe token is **cleared** from the composer (Ctrl+U) before ready is reported, and the cleared
  screen holds none of it — a probe that leaks a stray prompt is CARD-0101's `green` again;
- the CARD-0047 trust arm and the CARD-0048 3 s stall arm both still pass **through the same
  property**, proving the general rule subsumes the two narrow ones.

**(b) `ClaudeReadinessProbeContractTests` (new)** · `tests/Antiphon.Agents.Pty.Tests/` · **real
ConPTY, fakeclaude, seconds** · declared on **both** backends.

New fake knob `ANTIPHON_FAKE_DEAF_MS`: paint the banner and composer, then do not read stdin for N ms.
Set N to 20 000 (comfortably past the 15 s `EvidenceTimeoutSeconds`, well short of the measured
48.8 s) and assert the production readiness rule refuses to call it ready — through a real
pseudoconsole, on the backend production runs.

This is the arm that would have caught CARD-0103 *and* CARD-0048 (a 3 s deaf window is the same shape)
*and* CARD-0047 (a modal is a deaf window that never ends).

**(c) `ClaudeReadinessCanaryTests` (new, `[Explicit]`, `[Category("HeadedCanary")]`)** · real Claude ·
**costs zero model turns** (the probe token is cleared, never submitted).

Launch real Claude, start writing the probe token immediately, record time-to-echo, assert the
readiness verdict implies echo within the budget. Log the measured latency every run so the real
distribution is recorded rather than assumed — CARD-0103's 0.74 s vs 48.8 s pair is the only such
measurement that exists today, and it was taken by hand. Precedent: `ClaudeTrustPromptCanaryTests`,
`ClaudeSubmitConfirmCanaryTests`, `FakeVsRealClipParityTests` — all already `[Explicit]` headed
canaries that cost no API turns.

### P1-2 — The property, applied to every adapter

`tests/Antiphon.Tests/Agents/AgentAdapterReadinessContractTests.cs` (new) · **the answer to "is there
a way to test the GENERAL property"**.

One parameterised class over **every** `IAgentProtocolAdapter` implementation — `RunnerClaudeAdapter`,
`RunnerGrokAdapter`, `RunnerCodexAdapter`, `RunnerOpenCodeAdapter`, `RunnerRawAdapter`,
`ClaudeAdapter`, `CodexAdapter`, `RawPtyAdapter` — driven by the shared deaf scripted client, each
asserting the same single property. A new adapter added without an input-proving readiness rule goes
red on the day it is added, instead of shipping and waiting for its own incident months later.

This is the structural fix. `P1-1` fixes CARD-0103; `P1-2` is what makes CARD-0103 the last one.

*Feasibility note:* the adapters differ in constructor shape and in which terminal seam they take.
Expect one small refactor — hoisting `ScreenScriptedRunnerClient` out of the trust-prompt test file
into `tests/Antiphon.Tests/TestHelpers/` and giving it the deaf mode — before the parameterised class
can exist. That refactor is the first slice, not a separate project.

### P2-1 — `ClaudeDelegateEndToEndTests` (new) — the missing capstone

`tests/Antiphon.Tests/Application/ClaudeDelegateEndToEndTests.cs` · **headless, one real subprocess,
~60 s** · closes **B3** at CI tier · a direct copy of the `GrokDelegateEndToEndTests` shape, which is
why it is `P2` and not a research project.

Real `delegate.ps1` → real HTTP relay → real `AgentTaskService` → real dispatcher tick → **real
`BuildLaunchSpec` carrying the real bundles** → real ConPTY launch of `fakeclaude.exe` on the
**declared `modern`** backend → real `SessionMessageQueueService` delivery → real `TranscriptTailer`
bind → self-firing settlement → report correlated back to the caller.

Asserts, in order, each of which is a bug that actually happened:

1. the **full** bundle text arrives in the child's strict-parsed argv (P0-2) — *CARD-0101*;
2. `--session-id` arrives, and the child's transcript appears at the path derived from it —
   *CARD-0101 / CARD-0073*;
3. the tailer binds **`exact`**, read from `TranscriptSidecar.How`, not from the suppressed bind event
   — *CARD-0073's measurement, turned into an assertion*;
4. no stray positional argument reached the child (nothing is submitted before the brief) — the
   `green` prompt;
5. the brief's `SessionQueuedMessages` row reaches **`Sent`**, not parked, and is **one** turn;
6. the report correlates and settles;
7. the session and its pty-host are gone at teardown — *CARD-0102*.

**Two small fake changes make (2) and (3) possible**, and they are the only genuinely new work here:

- fakeclaude honours **`--session-id`**, and
- fakeclaude honours **`CLAUDE_CONFIG_DIR`**, writing its JSONL to
  `<CLAUDE_CONFIG_DIR>/projects/<enc-cwd>/<session-id>.jsonl`.

`TranscriptTailer.ResolveProjectsRoot()` (`:786-793`) already reads `CLAUDE_CONFIG_DIR` before falling
back to `~/.claude`, so the test points both the fake and the tailer at a temp root and **nothing
touches the operator's real `~/.claude/projects`**. Without this, `ANTIPHON_FAKE_TRANSCRIPT_PATH`
writes to an arbitrary path the tailer never searches, and the fake ignores `--session-id` entirely —
so the bind assertions are impossible as things stand. This is the one open feasibility item in the
plan; if it proves harder than expected, ship the test without (2)/(3) and keep binding coverage where
it is (`TranscriptAdoptionSafetyTests`).

### P2-2 — Isolate the E2E suite from the production runner (CARD-0102)

`tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:385-388`.

**First, a correction to CARD-0102's own proposed remedy:** shortening `PtyHostLingerHours` would
**not** have fixed this. `HostSession.cs:303-313` starts the linger clock **only after the child
exits** — the E2E children are interactive `cmd.exe` with no arguments, which never exit. Those 117
hosts were not lingering orphans; they were **live sessions the suite started and never stopped**, and
they would have survived a linger of one minute exactly as well as one of 24 hours. The fix is
lifecycle, not TTL.

Recommended, in order:

1. **Stop every session the fixture starts.** A `[After(Test)]`/dispose hook that kills sessions this
   fixture created — and, at assembly teardown, a census assertion that **fails the run** if any
   pty-host it started is still alive. A leak becomes a red test in the run that caused it. *This
   alone closes the incident and is worth doing first even if nothing below happens.*
2. **Stop pointing at `17204`.** Two options:
   - **(a) recommended** — start a per-run `Antiphon.SessionRunner.exe` on an ephemeral port with its
     own `SessionLogPath` under `TestOutput/` and `PtyHostLingerHours: 0.02`, and
     `POST /sessions/kill-all` on dispose. Keeps the HTTP seam the fixture is testing.
   - (b) cheaper — hoist `DirectSessionRunnerClient` into shared test-support and register it in the
     fixture's DI. Loses the HTTP seam; acceptable only if (a) proves slow.
3. Read `AntiphonAppFixture`'s own comments first (CARD-0102 asks this, correctly): the fixture
   deliberately does not start a runner, and `EnsureSessionRunnerReachable()` exists so
   session-dependent tests fail fast with a verdict. Whichever option is taken must preserve that
   fail-fast behaviour, or the 30–60 s mystery timeouts come back.
4. Consider whether `e2e-raw` should stop being bare `cmd.exe` — a child that exits on its own after a
   bounded life makes the linger TTL a real backstop instead of an inapplicable one.

### P3-1 — The periodic live delegate smoke

**Answer to the question asked: yes — an hourly smoke would have caught CARD-0101 within ~1 hour
instead of 3 days.** The regression landed 2026-08-17 07:21 UTC and broke **every fresh delegate
launch** from that moment: system prompt truncated 42 %, `--session-id` never delivered, exact bind
impossible. Any smoke asserting "the launched session bound its transcript by `--session-id`" goes red
on its first run after 07:21. First red ≤ 08:21 UTC on 08-17 versus first human notice on 08-20.

**What it is:** `scripts/smoke-delegate.ps1`.

1. Dispatch a real delegate through the real `scripts/delegate.ps1`
   (`-Role Docs -Level Low -Dir <scratch>`), goal: *write `SMOKE-<guid>` into `smoke.txt` and report
   the token back*.
2. Poll the API until settlement or timeout.
3. Assert, and fail with the specific verdict:
   - task reached `Dispatched` with a session row within 60 s;
   - the session's `TranscriptSidecar.How == exact` (the CARD-0101/0073 tripwire);
   - the brief's queued-message row reached `Sent` — **not parked** — within 120 s (the CARD-0103
     tripwire; parked-at-3-attempts is the exact failure shape);
   - `smoke.txt` contains the token;
   - the report came back and correlates within 10 min (`DelegationSettings.DeliveryFailTimeoutMinutes`);
   - the session and its pty-host are gone afterwards (the CARD-0102 tripwire);
   - **and record `claude --version`** with the result, so a TUI-behaviour change is attributable.
4. Clean up the scratch dir and the task unconditionally.

**Cost:** one Low-tier (haiku) delegate turn per run. Negligible.

**Where it runs:** **Windmill on server2**, tag `desktop`, SSHing into Windows — the mechanism this
repo already uses for `cleanup-build-junk.ps1`, whose header explicitly says not to add a local
Scheduled Task. Failure alerts via the existing Telegram path.

**Cadence:** **hourly**, plus on demand. Not more often — a 10-minute settlement budget means a
sub-hourly cadence overlaps itself and the runs start interfering. Not less — the whole point is to
compress "3 days" into "one cycle".

**Not on merge-to-master, and not in GitHub Actions.** There is no Windows runner, no `claude.exe` and
no real pseudoconsole there, and the only existing workflow is an ubuntu packaging job. The merge gate
should be the headless tiers (`P0-1`, `P0-2`, `P1-1a/b`, `P2-1`), which need neither. Standing up a
Windows CI gate for those is worthwhile separate work — see §7.

**Alternative considered and rejected:** implementing the smoke as an Antiphon-internal periodic check
inside `AgentSupervisorHostedService`. Rejected because a self-test that runs *inside* the process it
is testing cannot report when that process is the thing that is broken — the smoke must be able to say
"the server did not answer". Windmill is external by construction. (The **census** check, `P0-3`, is
different: it inspects state the server already holds, so inside is right.)

### P3-2 — Bind-health trend check (cheap, high signal)

A daily Windmill job (or a pass inside `P3-1`) that reads the `TranscriptSidecar.How` distribution over
the last 24 h and alerts when the `exact` fraction drops below a floor.

CARD-0073's root cause was *measured from this exact data*: **exact was 100 % of binds until
2026-08-17 07:47, then ~0 %.** The signal was sitting on disk the whole time. A daily check on it is a
handful of lines and would have flagged the regression on 08-18 even without the smoke.

---

## 5. Priority and sequencing

| Rank | Item | Cost | Catches | Tier |
|---|---|---|---|---|
| 1 | **P0-1** bundle→argv integrity | ~1 h | CARD-0101 at commit time | headless |
| 2 | **P0-3** census alert on existing data | ~2 h | CARD-0102 degradation, before it bites | runtime |
| 3 | **P2-2 step 1** E2E kills its own sessions | ~2 h | CARD-0102 at source | test infra |
| 4 | **P0-2** fakeclaude strict argv | ~2 h | removes B1 permanently | fake + tests |
| 5 | **P1-1a/b** readiness round-trip | ~1 d | CARD-0103 class | headless |
| 6 | **P1-2** adapter-wide readiness property | ~1 d | the *fourth* occurrence | headless |
| 7 | **P2-1** `ClaudeDelegateEndToEndTests` | ~2 d | B3 at CI tier | headless |
| 8 | **P3-1** hourly live smoke | ~1 d | everything, within 1 h | Windmill |
| 9 | **P2-2 step 2** isolated E2E runner | ~1 d | CARD-0102 by construction | test infra |
| 10 | **P1-1c** headed readiness canary | ~0.5 d | real-TUI drift | headed, `[Explicit]` |
| 11 | **P3-2** bind-health trend | ~0.5 h | binding regressions in ≤1 day | Windmill |

Ranks 1–4 are each independently shippable in under half a day and together close the two cheapest
blindnesses (B1, B4) plus the active production degradation. **They should not wait on the rest.**

Suggested card split: one card per rank, with 1–4 as a single "cheap tripwires" card if that suits the
board better. `P1-2` depends on hoisting `ScreenScriptedRunnerClient` (first slice of `P1-1a`). `P2-1`
depends on `P0-2` for assertion (1) and on the two fake changes for (2)/(3).

---

## 6. What automated testing **cannot** catch

Stated honestly, because pretending otherwise is how the next three-day bug gets missed.

1. **Machine saturation.** CARD-0103's actual trigger was ~100 % CPU across 8 cores with 137 pty-hosts
   and 35 `claude.exe` alive. A test can pin the *rule* ("ready ⇒ input round-trips") but cannot pin
   the *condition*. → **Monitoring:** record per-launch
   `time-from-ready-to-first-composer-evidence` on the session row and alert on distribution shift;
   alert on the `P0-3` census; alert on sustained host CPU. A test that tried to reproduce this would
   be a flake generator (CARD-0069's lesson).
2. **Real-TUI behaviour drift.** Claude Code changes its composer, its paste-collapse threshold, its
   trust dialog, its DA1 handshake. Only headed canaries see this, only after it ships, and only when
   they are run. → **Process:** run the `HeadedCanary` set nightly (non-blocking), and record the
   `claude --version` each canary last passed against so a red canary is immediately attributable to
   an upgrade rather than to our own change.
3. **Anything on the far side of a substituted seam.** B3 generalised: a defect that is correct on both
   sides of a substitution is invisible to every test that substitutes it. `P2-1` moves the seam; it
   does not abolish it (the fake is still not Claude). → **Only the live smoke (`P3-1`) has no
   substituted seam at all.** That is precisely why it must exist even after every test in §4 ships.
4. **A stale deployed binary.** CARD-0101's cascade was worsened because the running session-runner
   predated the CARD-0064 fix by 21 h — every test was green *and the fix was not running*. No test can
   see this. → **Monitoring:** the runner should report its build stamp on `GET /capabilities`, and
   something should compare it to `HEAD`. Cheap, and it is item 3 on CARD-0101's own fix list, still
   open.
5. **Alert fatigue as a failure mode.** CARD-0101's refusal fault fired 37 identical Warnings over three
   hours and nobody acted; then the incident stream went quiet while the fault kept running, and "no new
   incidents" was read as "fixed". → Any new incident added by this plan (`P0-3`) must have a dedup key
   **and** an escalation path, or it becomes noise that proves nothing by its absence.

---

## 7. Explicitly considered and not proposed

- **A Windows CI gate in GitHub Actions.** Worth doing eventually and out of scope here: it needs a
  self-hosted Windows runner (Postgres testcontainer, ConPTY, the `Microsoft.Windows.Console.ConPTY`
  redistributable). Until then the headless tiers are run locally, and this plan deliberately does not
  depend on CI existing.
- **Deleting `PtyBackendEnvGuard` so the suites run `modern`.** That knob *was* the bug (CARD-0045): a
  suite whose meaning depends on its launcher is worse than one with a known-narrow scope. The right
  answer is what this plan does — **declare `modern` on the tests that need it** (`P0-1` inbox+modern
  arms, `P0-2` modern arm, `P1-1b` both, `P2-1` modern), which is the pattern
  `GrokDelegateEndToEndTests` and `PtyDeliveryCeilingsTests` already use.
- **Widening `EvidenceTimeoutSeconds` / `MaxDeliveryAttempts` to make CARD-0103 go away.** That is the
  "quietly widen a timeout" move the delegate contract forbids, and CARD-0103's own fix list rules it
  out first. Not a test question; noted so nobody proposes it as one.
- **A test that reproduces CARD-0103 by generating real CPU load.** See §3 and §6.1.
- **Reaping unclaimed runner sessions automatically.** CARD-0056's constraint: the false positive fired
  on a perfectly healthy session, and the leaked session was the operator's own conversation. `P0-3`
  alerts; it does not kill.

---

## 8. Acceptance — how to know this plan worked

Each item below is a *replay*, not an aspiration. Every one can be run against the base commit to prove
the test is real before it is trusted.

| Test | Replay that must go red at the pre-fix commit |
|---|---|
| P0-1 | check out `28afb5f`, run it → red on `delegate-basics.md`'s quote |
| P0-2 | revert `aa1c8f1`'s `EscapeArgument`, run it → red (the current `--echo-args` assertion stays green: that is the point) |
| P1-1a/b | set `ANTIPHON_FAKE_DEAF_MS=20000` → `WaitForReadyAsync` must not report ready |
| P1-2 | remove the input probe from any one adapter → that adapter's row goes red |
| P2-1 | revert `aa1c8f1` → red at assertion (1) and (2); revert `94947f1` (CARD-0064) → red at (3) |
| P2-2 | run the E2E suite, count `Antiphon.PtyHost` before and after → delta must be 0 |
| P3-1 | run against a server with `aa1c8f1` reverted → red inside one cycle |
