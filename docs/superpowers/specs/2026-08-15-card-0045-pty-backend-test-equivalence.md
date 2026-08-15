# CARD-0045 — PTY tests declare their backend; the suite means the same thing whoever launches it

**Status:** Plan (not implemented). 2026-08-15. Independently re-measured the same day (retry
task `ea2feb92`, fresh full dual runs of Agents.Pty.Tests + Antiphon.Tests + SessionRunner.Tests
and fresh isolation reps): the §1 partition, the §4 seam analysis and the §6 verdict all
reproduced; §1.1-B/§1.3 carry the one refinement (the Codex trio is dual-natured) and §8 carries
both days' matrices.
**Card:** CARD-0045 "The test suite result depends on who launched it - 8 PTY tests inherit ANTIPHON_PTY_BACKEND" (`4cf412f7-7d99-486e-9eeb-eb3eabbd797c`)
**Relates to:** CARD-0037 (modern backend + conditional ceilings, ADR `docs/adr/0002-modern-conpty-backend.md`), CARD-0027/0028 (clip model), CARD-0030 (conhost is the discriminator), CARD-0026 (`JobObject_kills_session_when_memory_limit_exceeded`, standing red — §6).

**The deliverable is an equivalence:** the full suite run with `ANTIPHON_PTY_BACKEND` set and unset must give the SAME result. Everything below serves that one sentence.

---

## 1. The measured delta (this machine, 2026-08-15, master)

The card says 8. I did not trust the count: I ran all five non-E2E test projects both ways
(`env -u ANTIPHON_PTY_BACKEND` vs `ANTIPHON_PTY_BACKEND=modern`, `--property:OutputPath=bin-c45/`
because the always-on daemons lock `bin/`), then re-ran every differing class **in isolation, both
arms, 2-3 times each**, because CLAUDE.md's warning is real: several suite-level failures were load
flakes that vanish in isolation, in both directions.

Full-suite totals (1 215 tests): Agents.Pty.Tests 230, Antiphon.Tests 845, SessionRunner.Tests 58,
PtyHost.Tests 20, Messaging.Tests 62. PtyHost.Tests and Messaging.Tests are identical both ways
(0 failures). E2E was not run (§7.5). One suite-level modern run of Agents.Pty.Tests **crashed the
test host** (exit 127, no summary) after 9 failures; a rerun completed — the crash did not
reproduce and is noted, not explained (§7.6).

### 1.1 Deterministic modern-only failures — the real equivalence set (isolation-confirmed)

**Mechanism A — the test asserts an inbox-conhost fact, and inherits the backend instead of
declaring it.** On the modern pty the bracketed-paste markers reach fakeclaude, its clip model
exempts paste-mode content (CARD-0037 step 4), the body arrives whole, and a test asserting "a
chunk must be lost" fails *because the fix works*. These must **force `inbox` and keep passing** —
the behaviour they pin still ships as the fallback on any machine without the redistributable.

| test | file | inbox fact it pins |
|---|---|---|
| `The_same_brief_typed_inline_silently_loses_a_whole_chunk` | `tests/Antiphon.Tests/Application/DelegationBriefCeilingPtyTests.cs` | the card's headline: an inline oversized brief clips — the reason spill-and-pointer exists |
| `A_body_spanning_two_chunks_arrives_as_its_final_whole_chunk` | `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs` | clip keeps one whole 1 024-byte chunk |
| `Keep_first_models_the_first_chunk_only_shape` | same | keep-first clip variant |
| `Random_clipping_with_one_turn_keeps_exactly_one_whole_chunk` | same | random-survivor clip variant |
| `Deterministic_clipping_gives_identical_survivors_on_three_identical_trials` | same | seeded clip determinism |
| `Non_ascii_input_reaches_a_dotnet_peer_narrowed_to_one_byte_per_char` | same | the inbox conhost narrows non-ASCII typed input |
| `Stdin_write_past_the_console_input_cap_is_truncated_without_error` | `tests/Antiphon.Agents.Pty.Tests/PtyAgentRunnerTests.cs` | the console input path caps a 64 KB line (2/2 modern runs: the cap is different or absent on modern) |
| `The_production_pty_delivers_no_bracketed_paste_markers` | `tests/Antiphon.Agents.Pty.Tests/PtyBracketedPasteContractTests.cs` | the inbox conhost strips ESC[200~/201~ — CARD-0030's CI-runnable half. Uses `NodeStdinProbe` with `backend: null` = env-inherit. (See §1.5 for its run status.) |

**Mechanism B — the test asserts backend-AGNOSTIC behaviour and deterministically fails on modern
anyway.** This is the finding the card did not predict: these are **real modern-backend defects in
production-relevant code**, surfaced by the inherited variable, and pinning them to inbox without
recording the defect would hide a live problem — this deployment's server and runner both run
`modern` (AppHost env + `src/Antiphon.SessionRunner/appsettings.json`).

| test (all 0-fail on inbox, fail on modern, 2/2 isolated reps) | file | what actually breaks |
|---|---|---|
| `Send_prompt_round_trips_via_interactive_shell`, `Send_input_writes_raw_keystrokes_to_session`, `Resize_updates_pty_without_throwing` | `tests/Antiphon.Tests/Agents/RawPtyAdapterTests.cs` | typed input into an interactive `cmd /k` produces NO output within the adapter's 2 s quiet window; the snapshot holds only OpenConsole's init sequences (`ESC[1t ESC[c ESC[?1004h ESC[?9001h`). `RawPtyAdapter` is production server code (`server/Infrastructure/Agents/Pty/RawPtyAdapter.cs`). |
| `Wait_for_ready_accepts_codex_directory_trust_prompt`, `Question_detection_ignores_question_mark_in_prompt_echo`, `Wait_for_turn_complete_returns_question_state_after_quiet_output` | `tests/Antiphon.Tests/Agents/CodexAdapterLocalShellTests.cs` | same shape through `CodexAdapter`. **Dual-natured** (re-measurement): deterministic on modern (3F isolated, both days) AND load-flaky on inbox (failed a full unset run, pass isolated unset) — so a full-suite red here proves nothing without an isolation rep, on either arm. |
| `Transcript_path_env_appends_user_assistant_and_boundary_lines`, `Manual_compact_with_args_writes_the_full_measured_record_set`, `Auto_compaction_writes_an_auto_boundary_and_the_continuation_only`, `Compact_after_turns_env_emits_compacted_after_nth_turn` | `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs` | fakeclaude prints its ready banner (output path fine), then a typed line gets no `SUBMITTED:`/`Compacted (` within a **5 s** wait. Tests in the same file with **10 s** budgets pass on modern. **Intermittent, modern-aggravated**: all four failed in modern rep2, two of the four passed in modern rep3, none fail on inbox in isolation — consistent with a stall that is *usually* but not always over 5 s. |
| `WaitForQuiet_returns_false_under_continuous_output` | `tests/Antiphon.Agents.Pty.Tests/PtyAgentRunnerTests.cs` | a `cmd` loop echoing continuously reads as QUIET for 2 s on modern (3/3 modern runs) — same silent-early-window shape |
| `DoneDetector_returns_false_under_continuous_output` | `tests/Antiphon.Agents.Pty.Tests/ClaudeDetectorsTests.cs` | same continuous-output shape through the done-detector (seen in one complete full-suite modern run; **passed** the re-measurement's full modern run, so intermittent-modern-aggravated like the fakeclaude quartet, not deterministic) |
| `Exit_while_runner_down_is_collected_on_adoption_with_the_real_exit_code` | `tests/Antiphon.SessionRunner.Tests/PtyHostAdoptionTests.cs` | after adoption the session reads `Running`, never `Exited` — modern-path exit collection through a re-adopted host does not deliver |

The first four rows share one consistent story: **on the modern backend, output flows immediately
but the child's first response to typed input is late by several seconds** — every failing wait is
≤5 s, every passing sibling is ≥10 s. A plausible mechanism (unproven, §7.1) is OpenConsole
emitting `ESC[c` (DA1) and `ESC[?9001h` (win32-input-mode request) at startup and stalling some
part of its input pipeline on a terminal reply that our `PtyAgentRunner`/pty-host side never sends.
The adoption row is a different defect (exit signalling, not input latency).

### 1.2 Both-arms red — outside the equivalence, by measurement

`JobObject_kills_session_when_memory_limit_exceeded` (`PtyAgentRunnerTests`) fails **identically
with the variable set and unset** — full suite and isolation, ~45 s timeout each time, memory-kill
never fires. It is the card's "1 failed with it unset". **Out of scope for CARD-0045**: fixing it
does not move the equivalence in either direction. It is CARD-0026's standing red and deserves its
own effort; the only thing this plan does to it is stop it being drowned in phantom failures.

### 1.3 Load flakes — failed in a full-suite arm, pass 2/2 in isolation on BOTH arms

Not part of the equivalence; listed so nobody re-derives them: `Text_and_CR_in_one_write_does_not_submit(fakeclaude)`
(`ClaudeSubmitContractTests` — the fake's 12 ms burst gap vs ConPTY's ~14 ms read jitter),
`An_unsplit_turn_still_carries_a_message_id_on_its_single_record` (FakeClaudeContractTests — flaky
on BOTH arms even in isolation: failed unset rep3 and modern rep3, passed unset rep2/modern rep2),
`A_split_final_response_reaches_the_server_as_a_bare_turn_end_then_text` (failed modern rep3 only),
`Session_id_can_be_relaunched_after_exit_but_not_while_running` (SessionRunnerRuntimeTests), and —
observed once on the **unset** arm in isolation —
`Unbracketed_body_with_LF_line_endings_submits_as_one_intact_turn` (FakeClaudeContractTests).
The compaction/transcript quartet in §1.1-B also flaked once on a loaded unset run, which is why
isolation reps were required to classify anything.

### 1.4 Why the card counted 8

The card's 9-vs-1 run predates the crash-prone load flakes above and this machine's state today.
The stable answer is not a count but a partition: **8 inbox-fact tests (A) + 13 modern-defect
casualties (B, of which 4 intermittent) + 1 both-arms red + a rotating cast of load flakes**. Any future re-derivation
should use the isolation protocol in §5.1, not a single suite run.

### 1.5 The complete modern arm, confirmed

The crashed modern arm of Agents.Pty.Tests was rerun to completion (230 total, **11 failed**, 40
skipped, no crash): the mechanism-A clip quartet + non-ASCII + Stdin-cap, `WaitForQuiet…` and
`DoneDetector…` (mechanism B), `JobObject…` (both-arms red), one known flake (`An_unsplit_turn…`),
and — closing the one open cell — **`The_production_pty_delivers_no_bracketed_paste_markers` is
red under the inherited modern env**, exactly as mechanism A predicts (node is present: the
complete unset arm has no "no JS runtime" skips and the test passes there).
Headed tests (`ANTIPHON_HEADED_TESTS=1`) were not run in any arm; every `Claude*` canary that
builds `new PtyAgentRunner()` env-inherits today and is called out in §4.3.

---

## 2. Design principles

- **A test that asserts a backend's behaviour declares that backend.** `PtyAgentRunner(backendOverride)`
  exists for exactly this (CARD-0037; ADR: "Tests move independently, by construction").
- **The suite's default must not depend on ambient environment.** The equivalence is achieved
  twice over: every backend-sensitive test pins its backend (declarative), AND the test hosts
  neutralise the inherited variable (defence for every future test nobody pins).
- **An inbox pin must never silently absorb a modern defect.** Mechanism-B tests get their
  backend-independence back via the neutralised default, but the modern defect they exposed is
  recorded as its own card with a repro path — not erased.
- **A modern-asserting test SKIPS when the redistributable is absent** (the existing
  `PtyBackendContractTests` / `LaunchClippingFakeOnModernPtyAsync` pattern: `ConPtyRedistributable.TryLocate`
  → `SkipTestException`, then assert `runner.Backend` actually resolved modern).
- **Production propagation is untouched.** The runner's config→env export, the AppHost's server
  env, and `PtyDeliveryProfile`'s two-fact agreement stay exactly as CARD-0037 left them.

---

## 3. The decision the card asks for: should the test host refuse to inherit `ANTIPHON_PTY_BACKEND`?

**Yes.** Each pty-spawning test assembly clears the variable once, before any test runs.

What it costs, plainly:

1. **The "run the whole suite on modern by exporting one variable" knob dies.** The ADR documented
   that knob for the E2E fixture. But that knob *is* this card's bug — a suite whose meaning
   depends on who launched it. Anyone who wants a modern sweep after this plan runs the
   modern-pinned tests (they exist, they skip without the binary) or temporarily edits the guard;
   both are deliberate acts, which is the point.
2. **A machine-level operator override cannot leak into tests even intentionally.** Accepted: test
   meaning outranks operator convenience.
3. It does NOT cost modern coverage: every modern behaviour that matters is pinned by an
   explicitly-`"modern"` test already (`PtyBackendContractTests`, the paste-exemption arm of
   `FakeClaudeContractTests`, `PtyDeliveryCeilingsTests`, `ShadowCopyStoreTests`), and §5.4 adds
   the missing ones.

The alternative — pin every sensitive test and keep inheriting for the rest — fails the deliverable
the first time someone adds a pty test without reading this file. The guard makes the default
deterministic; the pins make the sensitive tests self-describing. Do both.

---

## 4. Is the CARD-0037 override sufficient? Mostly — one seam is missing

**4.1 Direct-runner tests: sufficient.** Every test that constructs `PtyAgentRunner` itself can
pass `"inbox"`/`"modern"` today. `NodeStdinProbe.StartAsync(backend:)` and
`FakeVsRealClipParityTests` already thread it; nothing more is needed.

**4.2 The missing seam: tests that spawn through `SessionRunnerRuntime` → detached pty-host.**
`DirectSessionRunnerClient` (used by `DelegationBriefCeilingPtyTests`,
`SessionMessageQueuePtyIntegrationTests`, `PtyHostAdoptionTests`, and the other
SessionRunner.Tests) builds a `SessionRunnerRuntime` whose `PtyHostLauncher` starts hosts that
inherit the **test process's** environment; `HostSession` then does `new PtyAgentRunner()`
(`src/Antiphon.PtyHost/HostSession.cs:17`). `SessionRunnerSettings.PtyBackend` exists but only
`Program.cs` (the daemon) acts on it — `SessionRunnerRuntime` never reads it. The per-instance
override is therefore **unreachable** from every host-mediated test. §5.3 adds the plumbing.
(Verified: env set on the launcher's `ProcessStartInfo` survives the `--spawn` intermediary —
`Win32ProcessSpawner` passes `lpEnvironment = NULL`, so the detached host inherits the
intermediary's block — but the plan prefers an explicit argument over ambient env, see §5.3.)

**4.3 Production classes that self-construct a runner:** `RawPtyAdapter`/`CodexAdapter`
(`new PtyAgentRunner()` with no seam) and `ClaudeHarness` (E2E). With the §5.2 guard their tests
deterministically get the code default (inbox), which is what they always meant. Do NOT add a
backend parameter to the adapters in this card; the defect card (§5.5) decides how it drives them
on modern.

---

## 5. Slices

Each slice is independently landable and verifiable. Slices 1 and 2 each achieve the equivalence
for today's suite on their own; both are wanted (§3).

### Slice 1 — Pin the inbox-fact tests (mechanism A)

**Files:**
- `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs` — `LaunchReadyFakeAsync` and
  `LaunchClippingFakeAsync` construct `new PtyAgentRunner("inbox")`. The fake IS the model of the
  inbox-conhost typing path (CARD-0028); every test through these helpers means inbox.
  `LaunchClippingFakeOnModernPtyAsync` stays `"modern"` + skip, unchanged.
- `tests/Antiphon.Agents.Pty.Tests/PtyAgentRunnerTests.cs` — `ReadLineLengthSeenByChildAsync`
  (serves `Stdin_write_past_the_console_input_cap_is_truncated_without_error`) pins `"inbox"`; its
  doc-comment already says "if the whole 64 KB now arrives, the platform has changed" — on modern
  it HAS changed, which is precisely why the test must say which platform it means.
- `tests/Antiphon.Agents.Pty.Tests/PtyBracketedPasteContractTests.cs` —
  `The_production_pty_delivers_no_bracketed_paste_markers` passes `backend: "inbox"` to
  `NodeStdinProbe.StartAsync`, and its name/docs change to say **inbox**, not "production": since
  CARD-0037 step 3, production on this deployment is modern, and the test pins the fallback.
- `tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractTests.cs`, `ClaudePasteLossCanaryTests.cs`,
  and the other headed `Claude*` canaries constructing bare `new PtyAgentRunner()`: pin `"inbox"`
  where the canary measures typing/clip behaviour (`ClaudePasteLossCanaryTests` explicitly so).
  Headed tests are outside the CI equivalence but the same principle applies, and they are the
  tests most likely to be run by hand with the variable exported.

**Verify:** `ANTIPHON_PTY_BACKEND=modern dotnet run --project tests/Antiphon.Agents.Pty.Tests
--property:OutputPath=bin-c45/` — the mechanism-A rows go green; the run equals the unset run
(minus known flakes, minus mechanism-B rows until slice 2/5). Each pinned test also asserts
`runner.Backend!.Backend == PtyBackend.InboxConhost` once, so a future regression names itself.

### Slice 2 — The guard: test hosts refuse the inherited variable

**Files:** one new `PtyBackendEnvGuard.cs` (a static class with a `[Before(Assembly)]` hook — the
pattern `TestDbFixture` already uses) in each of `tests/Antiphon.Agents.Pty.Tests`,
`tests/Antiphon.Tests`, `tests/Antiphon.SessionRunner.Tests`, `tests/Antiphon.PtyHost.Tests`,
`tests/Antiphon.E2E`. Body: `Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, null)`
plus one log line naming what it cleared (so a run launched "on modern" says visibly that the suite
ignored it). Messaging.Tests spawns no ptys; skip it or add for uniformity — either is fine.

This single slice restores the equivalence for **every** test, including mechanism B (their pty
falls back to the code default, inbox — the behaviour they were written against) and every future
unpinned test.

**Verify:** the full §5.1-style dual run of all five projects: identical results. Add one pin test
per guarded assembly: `The_suite_ignores_an_inherited_pty_backend` — set the env var inside the
test process? No: the guard has already run, so instead assert
`Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar) == null` and that
`PtyBackendPolicy.Resolve().Backend == InboxConhost` (on a machine with the redistributable this is
only true because the guard ran — the AppHost/runner exports would otherwise have leaked in when
launched by Antiphon, which is exactly the live evidence: this planning session itself inherited
`ANTIPHON_PTY_BACKEND=modern`).

### Slice 3 — The runtime seam: host-mediated tests can declare a backend

**Files:** `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs` (read
`settings.PtyBackend`, pass to launcher), `src/Antiphon.PtyHost.Client/PtyHostLauncher.cs`
(`BuildHostArgs` gains `--pty-backend <value>` when configured),
`src/Antiphon.PtyHost/Program.cs` (parse it), `src/Antiphon.PtyHost/HostSession.cs`
(`new PtyAgentRunner(backendOverride)`), `tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs`
(optional `ptyBackend` ctor param → settings).

An explicit argument, not env-on-StartInfo, so the choice is visible in the host's command line and
manifest when diagnosing a live host. Production behaviour is unchanged: the daemon's `Program.cs`
env-export continues to work and the new argument is simply the same value made explicit
(`appsettings.json` already sets `SessionRunner:PtyBackend`; a runner built from those settings now
passes it too — same resolved backend, now stated twice). Re-adopted hosts are untouched (they are
already running).

Then `DelegationBriefCeilingPtyTests` and `SessionMessageQueuePtyIntegrationTests` construct their
harness with `ptyBackend: "inbox"` — declarative, independent of the guard.

**Verify:** `PtyBackendContractTests`-style addition in `tests/Antiphon.SessionRunner.Tests`:
launch a session through `DirectSessionRunnerClient(ptyBackend: "inbox")` with the env var set to
`modern` in the launcher's own block (settable here because this test owns the runtime, not the
process env) — assert the host log line (`HostSession` logs `pty backend:` per session) reports
`InboxConhost`. Plus the slice-1 ceiling tests passing under an exported `modern` without relying
on the guard (temporarily unset guard in a scratch run to prove it, or assert via the host log).

### Slice 4 — Modern-side companions so the pins don't shrink coverage (small, optional)

Mechanism A's pins re-state the inbox facts; two now-known modern facts have no CI pin:
- the 64 KB single-line write on modern (companion to `Stdin_write_past_the_console_input_cap…`,
  in `PtyAgentRunnerTests`, `"modern"` + skip-if-absent; assert whatever slice-4-of-CARD-0037
  measured — write the assertion from a fresh measurement, not assumption);
- non-ASCII typed input on modern (companion to `Non_ascii_input_reaches_a_dotnet_peer…`).

Both follow the `LaunchClippingFakeOnModernPtyAsync` pattern. Land only with measurements attached.

### Slice 5 — File the modern-defect card(s); do not fix them here

Mechanism B is evidence of **two live defects on the deployed backend**, out of scope to fix in
CARD-0045 but mandatory to record (cards are the record — `project_cards_are_the_record`):

1. **Modern pty first-input stall**: typed input into a modern pty gets no child response for
   several seconds (bounded 5-10 s by the passing/failing wait budgets; affects `RawPtyAdapter` and
   `CodexAdapter` production sessions, whose ready-grace is 500 ms and quiet window 2 s). Repro:
   `RawPtyAdapterTests` under `ANTIPHON_PTY_BACKEND=modern`, 2/2. Include the DA1/win32-input-mode
   hypothesis and the §7.1 experiment.
2. **Modern pty exit not collected on adoption**: `Exit_while_runner_down_is_collected_on_adoption_
   with_the_real_exit_code` red on modern 2/2 — a session that dies while the runner is down would
   badge Running forever on this deployment, the exact CARD-0041 shape by a new route.

The card(s) should also own `WaitForQuiet_returns_false_under_continuous_output` and
`DoneDetector_returns_false_under_continuous_output` (same early-window silence) and decide
whether the fakeclaude 5 s waits are widened as part of the fix's verification
(do NOT widen them pre-emptively here — after slices 1-2 they run on inbox and pass; widening would
mask the defect's cleanest measurement).

### Slice 6 — Record the resolution against CARD-0045

Cards are write-once: record in the resolution (a) the real partition of §1 versus the card's
count of 8, (b) that `DelegationBriefCeilingPtyTests…loses_a_whole_chunk` was, as the card said,
right-and-unpinned, (c) the pointer to the defect card(s) from slice 5, and (d) CARD-0026's status
(§6).

**Landing order:** 1 → 2 → 3 → 5 → 4/6. Slices 1+2 together are the equivalence; 3 makes the
host-mediated pins declarative; 5 is the honesty debt; 4/6 close the loop.

---

## 6. CARD-0026's JobObject test: in scope or not?

**Not in scope, by measurement.** It fails with the variable set AND unset, full-suite and isolated
(~45 s, memory-kill never fires, both backends create the same memory-limited job via
`WindowsJobObject.AssignMemoryLimitedJob` on the child pid). It contributes nothing to the
set-vs-unset delta, so no change here moves the equivalence. It is, however, the card's core
warning made flesh — a red test everyone has learned to ignore — and it should be fixed or
`[Explicit]`-quarantined *under CARD-0026*, with this spec's only contribution being that after
slices 1-2 it is the ONLY red test in a default run, and therefore visible again.

---

## 7. What I could not determine, and what would settle it

1. **The mechanism of the modern first-input stall.** The 5 s-fail/10 s-pass boundary and the
   `ESC[c`/`ESC[?9001h` init sequences are measured; the "OpenConsole stalls its input pipeline
   awaiting a terminal reply" explanation is a hypothesis. Settle: instrument `PtyAgentRunner` to
   timestamp first write → first subsequent output on both backends (a 20-line probe test); then
   try answering DA1 (`ESC[?1;0c`) from the reader loop and see whether the stall vanishes.
   Belongs to the slice-5 card.
2. **Whether `Stdin_write_past…cap` and `WaitForQuiet…` are 100% deterministic on modern** — they
   are 2/2 and 3/3 across suite+isolation runs, but with smaller n than the 3-rep classes. The
   slice-1/2 verification runs will accumulate the evidence for free.
3. **Headed tests under the variable.** Not run (no `ANTIPHON_HEADED_TESTS`, and headed canaries
   cost real model turns). The slice-1 pins cover the canaries whose meaning is inbox; a headed
   sweep on modern is CARD-0037-step-2 territory, not this card's.
4. **The E2E project.** Not run both ways. Read-verified: its only pty is the headed
   `ClaudeHarness`; the headless E2E path spawns no pty (the fixture's server talks to a session
   runner on 17283 that nothing runs). The slice-2 guard lands there anyway. Residual risk: a
   future E2E test spawning a pty — covered by the guard.
5. **Why the card measured 786 tests / 9-vs-1.** Different day, different load, probably a subset
   of projects. The partition in §1 supersedes the counts; nothing in the plan depends on
   reconciling them.
6. **The one test-host crash** (Agents.Pty.Tests, modern arm, exit 127 after
   `WaitForQuiet…` failed, no summary). Did not reproduce on the rerun. If it recurs, capture it
   under the slice-5 card — a host crash mid-suite is itself a way for a red suite to lie.

---

## 8. Appendix: the raw matrix

Full-suite dual runs and isolation logs are under the session scratchpad
(`Antiphon.<project>.{unset,modern}.log`, `isolation/<Class>.<arm>.<rep>.log`); the numbers quoted
in §1 are: Agents.Pty.Tests unset 4F/230, modern 9F+crash then 11F/230 on the complete rerun
(`Antiphon.Agents.Pty.Tests.modern2.log`);
Antiphon.Tests unset 2F/845, modern 7F/845; SessionRunner.Tests unset 0F/58, modern 1F/58;
PtyHost.Tests 0F/0F; Messaging.Tests 0F/0F. Isolation: RawPtyAdapterTests inbox 0F×2 / modern
3F×2; CodexAdapterLocalShellTests 0F×2 / 3F×2; DelegationBriefCeilingPtyTests 0F×2 / 1F×2;
PtyHostAdoptionTests 0F×2 / 1F×2; FakeClaudeContractTests unset 1F(flake)×1, modern 9F;
PtyAgentRunnerTests unset 1F(JobObject), modern 3F (JobObject + Stdin-cap + WaitForQuiet);
ClaudeSubmitContractTests 0F×2 both arms; SessionRunnerRuntimeTests 0F×2 both arms.

**Independent re-measurement, same day (retry `ea2feb92`, `--property:OutputPath=bin-c45b/`),
run fully foreground.** Full suites: Agents.Pty.Tests unset **2F**/230 (JobObject +
`Slash_compact_emits_compacted_screen_line…`, the latter a flake — passed everywhere else), modern
**11F**/230 (the 8 mechanism-A/B deterministic rows + JobObject + two flakes:
`An_unsplit_turn…`, `Local_slash_command_writes_command_records_and_no_turn_end`; DoneDetector
passed this run); Antiphon.Tests unset **5F**/845 (all load flakes: Codex trio +
`Send_prompt_clears_live_buffer_before_send` + `HookRunner_timeout_kills_hung_hook`), modern
**8F**/845 (4 shared flakes + brief-ceiling + RawPtyAdapter trio); SessionRunner.Tests unset
0F/58, modern 1F/58 (`Exit_while_runner_down…` — same single row, both days). Isolation
(1 rep each unless noted): RawPtyAdapterTests inbox 0F / modern 3F; CodexAdapterLocalShellTests
inbox 0F / modern 3F; ClaudeAdapterLocalShellTests modern 0F (pure load flake);
DelegationBriefCeilingPtyTests inbox 0F / modern 1F; PtyBracketedPasteContractTests inbox 0F /
modern 1F; PtyAgentRunnerTests inbox 1F (JobObject) / modern 3F (JobObject + Stdin-cap +
WaitForQuiet); FakeClaudeContractTests class inbox 2F (`Manual_compact_with_args…`,
`An_unsplit_turn…` — both flakes, both passed elsewhere) / modern 6F (the 5 clip/non-ASCII rows +
`A_split_final_response…` flake); WorkspaceHookRunnerTests inbox 0F. Every deterministic row in
§1.1 reproduced exactly; every disagreement between the two days is confined to the §1.3 flake
cast. The re-measurement's shell was itself Antiphon-launched and started with
`ANTIPHON_PTY_BACKEND=modern` inherited — the card's premise, observed directly.
