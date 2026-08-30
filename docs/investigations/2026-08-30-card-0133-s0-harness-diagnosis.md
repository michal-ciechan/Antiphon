# CARD-0133 S0 — why the interactive Codex/stub harness could not boot, and what the boot wedge is

**Date:** 2026-08-30. **Status:** harness fixed on the PtyHost lane; the herdr lane now reproduces the
CARD-0133 wedge deterministically and names its mechanism. Measured on codex-cli **0.151.0** (the global
npm shim; it was 0.147.0 on 2026-08-27 05:45, 0.150.1 from 2026-08-27 21:15, 0.151.0 from 2026-08-29 17:49).
Supersedes the blocked state in
[2026-08-27-card-0133-s0-boot-wedge-probe.md](2026-08-27-card-0133-s0-boot-wedge-probe.md).

## TL;DR

1. **The harness never reached a composer because an isolated `CODEX_HOME` has no Windows sandbox
   decision.** Right after the trust decision, Codex shows a second onboarding dialog — *"Set up the Codex
   agent sandbox … 1. Set up default sandbox (requires Administrator permissions) 2. Use non-admin sandbox
   3. Quit"* (`codex-rs/tui/src/lib.rs`: `trust_decision_was_made && windows_sandbox_level == Disabled`).
   The probe and the herdr canary re-sent Enter on every poll while the trust text lingered in cumulative
   `RawOutput`, so the stray Enter took option 1; Codex then ran the *elevated* setup in-process —
   `Setting up sandbox... Hang tight, this may take a few minutes › Input disabled until setup completes.` —
   repainting a spinner at 1 Hz for the whole 60 s window. The "quiet after visible output" ready rule can
   never fire on that, and the composer refuses input anyway. The operator's real `~/.codex/config.toml`
   carries `[windows] sandbox = "elevated"` with setup already completed (`.sandbox`, `.sandbox-bin`,
   `cap_sid` from the 2026-08-17 Codex Desktop install), which is why no production session ever showed it.
   **Fix (shipped):** `RealCliStubEnv.SeedCodexHome` now writes a `config.toml` with
   `[windows] sandbox = "unelevated"` (a decision that needs no setup), and both test helpers answer trust
   exactly once, as `RunnerCodexAdapter._acceptedTrustPrompt` already does.
2. **The synthetic ChatGPT `auth.json` (seeded since 2026-08-26, `cec61de`) made Codex start its built-in
   `codex_apps` MCP client against the REAL ChatGPT backend** with the unsigned fixture token, which failed
   `HTTP 401 "Could not parse your authentication token"` and printed `⚠ MCP startup incomplete (failed:
   codex_apps)` on the boot screen. `apps_enabled` is just `features.enabled(Feature::Apps)` (key `apps`,
   stable, default on). **Fix (shipped):** the seeded `config.toml` also sets `[features] apps = false`;
   the `/v1/models` refresh the fixture auth exists for is gated on auth mode, not on apps, and still fires.
3. **The `WARNING: proceeding, even though we could not create PATH aliases: Refusing to create helper
   binaries under temporary dir` line is a red herring.** It is `codex-rs/arg0/src/lib.rs::
   prepare_path_entry_for_codex_aliases`, which refuses to drop `apply_patch.bat` under
   `CODEX_HOME/tmp/arg0` when `CODEX_HOME` starts with `std::env::temp_dir()` — exactly where the harness
   puts it. Only consequence: `apply_patch` is not on the child's PATH. It has appeared on every stub run
   since CODEX_HOME isolation, `CodexRealCliStubProxyCanaryTests.Exec_turn_…` *asserts* it in a passing
   run, and zero production ANSI logs carry it (the one 2026-08-27 hit is delegate `card-task-be079028`
   echoing its own test output).
4. **With the harness healthy, the herdr lane reproduces CARD-0133's "body typed, Enter swallowed" wedge
   3/3 at the production 20 ms body→Enter gap, and the mechanism is Codex's `PasteBurst`.**
   `codex-rs/tui/src/bottom_pane/paste_burst.rs`: ≥3 chars ≤8 ms apart (`PASTE_BURST_MIN_CHARS`,
   `PASTE_BURST_CHAR_INTERVAL`) start a burst; `burst_window_until = now + 120 ms`
   (`PASTE_ENTER_SUPPRESS_WINDOW`); `chat_composer.rs` turns an Enter that lands while
   `is_active() || now <= burst_window_until` into an inserted `"\n"` **and extends the window by another
   120 ms**. Our queue sends the whole body in one write and Enter 20 ms later. Proof by the two knobs
   added to the canary: with `ANTIPHON_STUB_ENTER_GAP_MS=200` it **passes** (stub sees nonce, Bearer,
   `/v1/models`, `UserPrompt` row); with the gap at 20 ms and `-c disable_paste_burst=true`
   (`ANTIPHON_STUB_CODEX_DISABLE_PASTE_BURST=1`; a top-level Codex config key, default false) it **passes**;
   at production shape it holds the body in the composer for the full 60 s (screen dump in the test
   output: `› Reply with exactly this token … STUBCANARY-…` still standing, seq advanced by 1).

## What was ruled out

- **Codex CLI version drift is not the trigger.** The herdr canary rerun that failed on 2026-08-27 05:45
  ran on 0.147.0 — the same binary that passed on 2026-08-25 — before the 21:15 upgrade to 0.150.1. The
  sandbox NUX and `PasteBurst` predate 0.147 (`windows_sandbox_prompts.rs` history runs back to 2026-05).
  Between 08-25 and 08-27 the only harness change was the CODEX_HOME isolation + fixture `auth.json`
  (`1e1f89c`, `cec61de`); the fixture auth added the `codex_apps` failure, and the multi-Enter + sandbox NUX
  was always latent in an isolated home. Note the 0.151.0 features table marks `experimental_windows_sandbox` /
  `elevated_windows_sandbox` as `Stage::Removed` in favour of `[windows].sandbox`; the operator's
  `config.toml` gained `[windows] sandbox = "elevated"` at 2026-08-26 17:36 (a migration), so the real home
  never had a `Disabled` level during any of this.
- **Machine load is not needed to explain it.** Today's reproduction was deterministic at 8.2 GB free / 32 GB
  with 18 pty-hosts and 4 codex processes live; no watchdog/autostart restarts are logged for 2026-08-27
  04:00–07:00. The 359-byte ANSI capture from the 2026-08-27 06:08 probe (warning only, then nothing) is
  consistent with a starved machine not painting within the window, but that run predates the fixes above
  and cannot be re-derived; treat load as an aggravator, not the cause.
- **Interactive Codex 0.151.0 is healthy on this machine.** Two live production delegates today rendered
  `› Ask Codex to do anything`, `Working (40s • esc to interrupt)` in `C:\logs\antiphon\session-runner\9818025d….ansi.log`.
- **The PtyHost lane does not wedge in this harness:** P1 ×5 at the production shape (620-char single-line
  pointer body, 20 ms gap, 120×30, modern ConPTY) = 5/5 submitted, `Working (` seen each time, ready in
  ~4–5 s. Why the pty lane escapes the 120 ms window while herdr does not is unmeasured: the candidates are
  ConPTY chunking the write into several reads ≥ 8 ms apart (so the burst has already flushed and
  `is_active()` is false when Enter arrives) versus herdr delivering the body in one `pane.send_text` and
  Enter as `pane.send_keys ["enter"]`. Production's measured ~10 % rate on the pty lane (census: 10 of 89
  since 2026-08-20) is therefore a timing tail this harness has not yet hit; the 30-launch P1 should be run
  under load, and the incident's exact cwd shape, as §2.2 already says.

## Observations logged, not asserted (CARD-0187 D1 on herdr, Codex)

- Burst on, gap 200 ms: 43 200 B and 86 400 B bracketed-paste bodies both `record=0` in 60 s.
- Burst off, gap 20 ms: 43 200 B `complete=True joinedNewlines=True` (25 s); 86 400 B `record=0`.
  Consistent with herdr's `pane.send_text` not preserving the paste markers (so a 43 KB body is a typed
  burst that outlives any fixed gap), but that is a CARD-0187 question, not this card's.

## Files changed

- `src/Antiphon.FakeLlmApi/RealCliStubEnv.cs` — `SeedCodexHome` writes `config.toml`
  (`[windows] sandbox = "unelevated"`, `[features] apps = false`) beside the fixture `auth.json`.
- `tests/Antiphon.Tests/Agents/CodexBootWedgeProbeTests.cs` — `WaitForProductionReadyAsync` answers trust once.
- `tests/Antiphon.Tests/Agents/HerdrRealCliCanarySupport.cs` — `AcceptCodexTrustIfVisibleAsync` returns
  whether it answered; `SendWrappedBodyAsync` gap is `EnterGapMs` (`ANTIPHON_STUB_ENTER_GAP_MS`, default 20).
- `tests/Antiphon.Tests/Agents/CodexHerdrRealCliStubProxyCanaryTests.cs` — trust answered once; screen
  dumps before typing and on a nonce miss; `ANTIPHON_STUB_CODEX_DISABLE_PASTE_BURST=1` knob; skip message
  names the mechanism. **Default stays at production shape**, so the canary keeps detecting the wedge.

## How to run

```powershell
$env:ANTIPHON_REAL_CLI_STUB_TESTS = '1'; $env:ANTIPHON_CODEX_HEADED_TESTS = '1'
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-s0/
# PtyHost P1, N launches, artifacts kept under %TEMP%\card0133-p1-*
$env:ANTIPHON_CARD0133_ITERATIONS = '30'; $env:ANTIPHON_CARD0133_KEEP_ARTIFACTS = '1'
tests/Antiphon.Tests/bin-s0/Antiphon.Tests.exe --treenode-filter "/*/Antiphon.Tests.Agents/CodexBootWedgeProbeTests/P1_plain_ptyhost_production_shape_measures_boot_wedge" --output Detailed
# herdr reference canary: production shape skips (wedge), either knob passes
$env:ANTIPHON_STUB_ENTER_GAP_MS = '200'   # or $env:ANTIPHON_STUB_CODEX_DISABLE_PASTE_BURST = '1'
tests/Antiphon.Tests/bin-s0/Antiphon.Tests.exe --treenode-filter "/*/Antiphon.Tests.Agents/CodexHerdrRealCliStubProxyCanaryTests/B_runner_herdr_launch_hits_stub_and_kills_clean" --output Detailed
```

## Consequences for S1–S4 (decisions, not changes made here)

- **S2's remedy has two concrete candidates now, both measured green on herdr:** send Enter ≥ 120 ms after
  the last body byte (the window restarts on every char and on every suppressed Enter, so the gap must be
  measured from the *end* of delivery, which through ConPTY is later than the end of our write), or launch
  Codex with `-c disable_paste_burst=true` (`CodexLaunchArgs`), which makes Enter always submit. The second
  also removes the 43 KB-typed-burst hazard on herdr. Bracketed-paste bodies are exempt from `PasteBurst`
  only when the markers survive the transport — they do on the modern ConPTY (CARD-0037), and apparently
  not through herdr `pane.send_text`.
- **S1 stays as designed:** the wedge leaves the body standing in the composer with the sequence advanced,
  which is exactly the false-positive `ComposerDeliveryEvidence` shape S1 removes.
- **S4's composer-clear keystroke (P4) is now runnable** on the PtyHost lane; it was not attempted here.
