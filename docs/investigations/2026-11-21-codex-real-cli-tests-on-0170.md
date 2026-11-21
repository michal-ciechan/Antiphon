# Codex real-CLI tests against 0.170.0 at b02b4ea7

Diagnose only. No deploy, no restart, no production Postgres, no shared Codex auth. Checkout `feat/card-task-206053cc` at start `b02b4ea7ae80ad8eff3445a82d5477b80107bf44`. Host uid `1654(app)`. `codex --version` printed `codex-cli 0.170.0`. Binary `/usr/local/bin/codex` is a symlink to `/opt/codex/0.170.0/package/vendor/x86_64-unknown-linux-musl/bin/codex`. Throwaway `CODEX_HOME=/tmp/codex-home-206053cc` (empty; `auth.json` was not created and `/state/codex` was not opened).

`2f5e17219` is an ancestor of this tip (message: `docs(tests): name the 0.170 ready-fix checkpoint rows`). `589abe80` is not an object in this repository.

## Verdict at b02b4ea7

The canary that types the boot pointer did not fail. `C1521_Cold_pointer_records_write_consume_enter_and_receipt` skipped in 69 ms with `Real-CLI stub-proxy canaries require Windows`. No Codex process started. No pane text was captured. Enter-withheld / `DeliveryVerificationFailed` was not reproduced on this host.

The ready-gate code for failure (1) is already in this tip: `CodexStartupScreen.IsAgentShortcutsRow` steps back over a last row that contains both `for agents` and `for shortcuts`. The pointer reader copies that same step (`CodexPointerComposerEvidence`). It does not treat a last row that is only the warning chip as a footer. That is failure (2), and the real-CLI test that would observe it never left the gate.

## What each class does

All six were read at this tip. "Real CLI against a stub" means `RealCliStubEnv.ForCodex` plus `FakeLlmApi` (synthetic bearer, per-run home, nonce on `POST /v1/responses`). None of these classes reads a shared `auth.json`.

| Class | What it does | Real binary + stub, no login | How it is selected | Why it missed 0.170 |
|---|---|---|---|---|
| `CodexRealCliStubProxyCanaryTests` | Four methods. Two run `codex exec` (nonce reply; models 400 fail-closed). One launches that exec through `SessionRunnerRuntime` PtyHost and kills it. One projects the CARD-0167 endpoint through `AgentControlService` and requires the same nonce. | Yes. Throwaway home inside the test. No ChatGPT login. | `[Explicit]`, categories `RealCliStubProxy` and `Integration`. Opt-in `ANTIPHON_REAL_CLI_STUB_TESTS=1` (`RealCliStubGate`). Not `Headed`. | `exec` never draws the TUI footer or the composer, so neither the shortcuts row nor the warning-chip row can fail it. On Linux the gate skips before lookup. |
| `CodexHerdrRealCliStubProxyCanaryTests` | Interactive TUI on the Herdr backend, same stub overlay, nonce oracle. A second method uses the npm shim. `B_agent_path_deferred_until_CARD_0167` always skips. Boot-stall and a missing nonce are skips, not failures. | Yes, when it launches. | Same Explicit + `RealCliStubProxy` + `ANTIPHON_REAL_CLI_STUB_TESTS=1`. Also needs a live Herdr pipe (`SkipIfHerdrUnreachableAsync`). | Windows skip first. It never uses `CodexReadyWait` or `CodexPointerComposerEvidence`. A nonce miss is a skip that names PasteBurst, so a chrome failure would not fail the rc run even on Windows. |
| `CodexBootWedgeProbeTests` | `C1521` is the boot-brief canary: PtyHost, stub, ready wait, type one pointer, require `CodexPointerComposerEvidence.IsComplete`, then Enter, one `UserPrompt`, and the stub body. `P1` measures wedge rate over 30 iterations and does not assert a rate. `P3` is that measurement pinned to a scratch Codex 0.149.1. `P4` measures clear-keys. | Yes for C1521/P1/P4. | `[Explicit]`, categories `Headed`, `OptIn`, `RealCliStubProxy`, `Integration`. Same stub env flag. `P3` also needs `ANTIPHON_CARD0133_RUN_P3=1` and `ANTIPHON_CARD0133_CODEX_EXE`. | C1521 is the regression for failure (2). It is Explicit, policy-excluded, and the Linux gate skips it. P1/P4 skip the same way. P3 returned without launching. |
| `CodexMcpBootProbeTests` | Times bundled MCP boot text, types a marker, never submits. | No. Real CLI, real headed home, no stub. | `[Explicit]`, categories `Headed`, `OptIn`, `HeadedCanary`, `Card0195`. `CxSession`: Windows, `ANTIPHON_CODEX_HEADED_TESTS=1`, and `auth.json` in `%LOCALAPPDATA%\Antiphon\codex-test-home`. | Not run. It requires a seeded login this task must not open. It never classifies the ready footer and never reads composer evidence. `CxSession.ResolveCli` does not honor `ANTIPHON_CODEX_EXE`. |
| `CodexCliVersionProbeTests` | Parser plus a fixture process whose banner is `0.160.0`. Asserts the probe's argv is `--version` and that it opens no auth file. | No. The child is the fixture, not `/usr/local/bin/codex`. | `[Category("Integration")]`. No Explicit, no OptIn. Not in `profiles.rc.exclusions`. | Runs in the integration lane and still cannot see 0.170 chrome. The pinned banner is 0.160.0. |
| `CodexAuthProbeTests` | `auth.json` presence only. The file is held open so a probe that read it would fail. A sentinel string must not appear in the DTO or the log. | No. No process. | `[Category("Unit")]`. No Explicit. | Unit lane. Metadata only. |

Isolation owner: `docs/agent-kinds.md` (Codex credentials, headed-test home, and the real-CLI stub section). The stub canaries are `[Explicit]` and gated on `ANTIPHON_REAL_CLI_STUB_TESTS=1`, distinct from `ANTIPHON_HEADED_TESTS` / `ANTIPHON_CODEX_HEADED_TESTS`. The headed home is a second login the operator seeds; tests must not copy `~/.codex` or `/state/codex`.

## Why the two live failures were invisible

Failure (1), ready stayed Unknown for 120 s because 0.170 adds a last row `← for agents · ? for shortcuts … f2 to view`. Before `IsAgentShortcutsRow`, the ready classifier treated the last non-empty line as the model footer. A 0.160 fixture keeps the warning chip on that same line, so it stayed Ready. No rostered test contained the extra row until the fix's own fixture (`CodexStartupReadinessTests.Codex_0170_shortcuts_row_under_the_footer_is_ready`, Unit, in `Antiphon.Agents.Pty.Tests`). That fixture is in this tip. It did not exist to fail the upgrade beforehand.

Failure (2), pointer evidence timed out because a typed pointer leaves the last row as the warning chip (about 94 spaces, then the chip), not the shortcuts row. `TryReadComposer` steps back only when the last line matches `IsAgentShortcutsRow` (both `for agents` and `for shortcuts`). A chip-only line fails `IsModelEffortCwdFooter`, so `IsComplete` stays false and C1521 never sends Enter (`whole consumption must be observed within 15 seconds`). That is the same withhold as production `DeliveryVerificationFailed`, with the test's own assertion text.

The Unit stand-in does not encode that frame. `CodexAccountBootstrapTests.Codex_0170_typed_pointer_with_shortcuts_row_is_complete` copies the idle 0.170 grid and replaces the composer line. The shortcuts row is still underneath. The comment in that test says the grid is derived, not a measured typed capture, and that the live canary is the measurement. The derived case is green at this tip, so the Unit lane stays green while the live composer is not readable.

## Roster

Nightly (`profiles.nightly`) has an empty exclusion list and still drops category `OptIn` or `Explicit` (`Test-NightlyDiscoveryExcluded` in `scripts/lib/nightly-coverage.ps1`). The three real-CLI classes carry `Explicit`. Boot-wedge and the MCP probe also carry `OptIn`.

The rc profile lists all three classes, plus `CodexMcpBootProbeTests`, under owner CARD-0599. Reason text: `opt-in real-provider or interactive workflow; needs a live CLI session` (MCP: `headed or real-provider experiment owned by its own canary workflow`). `tests/live-lanes.json` marks the same methods `policy-excluded`.

Merge verification is the short set in `docs/orchestration-loop.md` (one build, the subject's witness rows, land-graph, pins, derived index, evidence diff). These canaries are not in that set and not in the guard union.

`CodexAuthProbeTests` is in the Unit lane. `CodexCliVersionProbeTests` is an ordinary Integration class. `CodexStartupReadinessTests` and `CodexAccountBootstrapTests` are Unit. Those are the rostered tests. They do not launch 0.170.

## Runs on this host

One build, native output, `ANTIPHON_LINUX_COMPILER_POLICY=0`, `UseAppHost=false`, slot `codex-0170-canary-build`, lease `75ca3c5c-e939-4e94-a8f2-6afa9b59f5cf`. The load line read inside that command was `32.66` (above the gate of 14); an earlier read in the same minute was `10.82`. Result: 0 errors, 2426 warnings, 3 min 22 s. No `bin-*` directory was created.

Each run: `ANTIPHON_REAL_CLI_STUB_TESTS=1`, `ANTIPHON_CODEX_EXE=/usr/local/bin/codex`, `CODEX_HOME=/tmp/codex-home-206053cc`, `--no-build`, its own slot. `ANTIPHON_CARD0133_RUN_P3` and `ANTIPHON_CODEX_HEADED_TESTS` were unset. Load at each start: 12.96, 8.80, 8.07.

| Class | Slot | Exit | total / passed / failed / skipped | Notes |
|---|---|---|---|---|
| `CodexRealCliStubProxyCanaryTests` | `5bdfe2e1-2633-4f40-a091-4e09dcd74c32` | 8 | 4 / 0 / 0 / 4 | Every method: `Real-CLI stub-proxy canaries require Windows`. TUnit text: `Zero tests ran`. 26 s. |
| `CodexHerdrRealCliStubProxyCanaryTests` | `c2d25b3f-85a6-4f70-b3ef-a2cc4da13ddc` | 8 | 3 / 0 / 0 / 3 | Two methods: Windows. `B_agent_path_deferred_until_CARD_0167`: the CARD-0167 deferral skip. 5 s. |
| `CodexBootWedgeProbeTests` | `f602e573-a34c-4b3d-b5a6-6f174c55dfc5` | 0 | 4 / 1 / 0 / 3 | C1521, P1, P4: Windows. The pass is `P3_scratch_01491_path_measures_boot_wedge`, which returns immediately unless `ANTIPHON_CARD0133_RUN_P3=1`. That flag was left unset: the method header requires a scratch `@openai/codex@0.149.1` prefix and refuses the global shim. 5 s. |

TRX files, not committed: `/tmp/codex-0170-canary/realcli/realcli.trx`, `/tmp/codex-0170-canary/herdr/herdr.trx`, `/tmp/codex-0170-canary/bootwedge/bootwedge.trx`.

`RealCliStubGate.SkipIfNotEligible` throws on non-Windows before the env flag and before `HeadedCodexGate.ResolveCodex`. The resolver would still miss a bare `codex` on `PATH`: it accepts `ANTIPHON_CODEX_EXE` or Windows npm names (`codex.exe`, `codex.cmd`, `codex.bat`, `codex.ps1`). The override was set and was never consulted.

To make C1521 actually fail here, the gate has to allow Linux when `ANTIPHON_CODEX_EXE` points at the image binary and the test's own throwaway home is used. Herdr still needs a reachable Herdr pipe, which this runner did not get far enough to probe. The MCP probe still needs a Windows ConPTY, `ANTIPHON_CODEX_HEADED_TESTS=1`, a seeded test home, and a resolver change before it can see `/usr/local/bin/codex`. Do not point it at `/state/codex`.

## Smallest rc-gate addition

Do not enroll `CodexBootWedgeProbeTests`. P1's default is 30 iterations and a one-hour timeout, and the class is Explicit on purpose.

Add one Integration method, in a class that is not `Explicit` or `OptIn` and is not listed in `profiles.rc.exclusions` or `profiles.nightly`. On Linux and Windows, when `ANTIPHON_REAL_CLI_STUB_TESTS=1` and `ANTIPHON_CODEX_EXE` is the image binary, launch that binary through PtyHost against `FakeLlmApi` with `RealCliStubEnv.ForCodex` and a per-run home. Wait ready, type one boot-brief pointer, require `CodexPointerComposerEvidence.IsComplete`, send Enter, and require one `UserPrompt` plus the nonce on `POST /v1/responses`. One iteration, a few minutes. A missing binary on the Linux image lane is a failure. A host that does not ship Codex may skip only under that named reason. Keep P1, P3, P4, the exec class, and the Herdr class excluded.

That is the check to run against the image's `codex` before a rollout. The Windows full qualification does not see this container's binary. A fixture of the measured typed frame (last row is the warning chip alone) belongs in the Unit lane as well, and does not replace the process check: the current derived shortcuts-row fixture stays green on the broken reader.

## Command against the fix-3 tip

`589abe80` is not in this clone. On a host where the Windows gate does not skip, the regression method is C1521 alone (not the whole class):

```
export ANTIPHON_LINUX_COMPILER_POLICY=0
export ANTIPHON_REAL_CLI_STUB_TESTS=1
export ANTIPHON_CODEX_EXE=/usr/local/bin/codex
export CODEX_HOME=/tmp/codex-home-throwaway
pwsh -NoProfile -File scripts/build-slot.ps1 -Label codex-0170-c1521 -- dotnet run --project tests/Antiphon.Tests -p:UseAppHost=false -- --treenode-filter "/*/*/CodexBootWedgeProbeTests/C1521_Cold_pointer_records_write_consume_enter_and_receipt"
```

The same command on this server2 tip skips. It will skip on the fix tip too until the gate allows Linux. Expect, once it launches at b02b4ea7: ready can pass (the shortcuts-row step is already here) and the method fails on `whole consumption must be observed within 15 seconds`, with Enter not sent. After the pointer reader accepts the warning-chip last row, that assertion should pass and the stub should see the pointer.
