# CARD-0133 S0 boot-wedge probe - partial measurement

**Date:** 2026-08-27. **Status:** blocked before P1/P3/P4 measurement. The real interactive Codex/stub harness still does not reach a rendered composer on this machine.

## What was run

The new `CodexBootWedgeProbeTests.P1_plain_ptyhost_production_shape_measures_boot_wedge` was run once with `ANTIPHON_CODEX_HEADED_TESTS=1`, `ANTIPHON_REAL_CLI_STUB_TESTS=1`, and the plain `PtyHost` backend. It uses native `codex.exe` 0.147.0, `PtyBackend=modern`, the production `--no-alt-screen --dangerously-bypass-approvals-and-sandbox` flags, a seeded isolated `CODEX_HOME`, the five `RealCliStubEnv.ForCodex` provider overrides, 120x30 geometry, and a 620-character body.

The host launched the child, but the composer marker never rendered. That run exposed a false-positive submit predicate in the first probe revision; the committed revision now requires the marker before it sends Enter and reports `no-composer-evidence` otherwise. The ANSI capture was 359 bytes and contained only terminal negotiation plus:

```
WARNING: proceeding, even though we could not create PATH aliases: Refusing to create helper binaries under temporary dir
```

The host log recorded:

```
Launched ...codex.exe (child pid 59616); pty backend: ModernConPty
Child exited (code -1, reason KilledByRequest)
```

The `KilledByRequest` line is the probe's teardown after the failed evidence window, not evidence of a clean Codex exit. There was no `CODEX_HOME/log/codex-tui.log` in this CLI version/run.

The known Herdr canary was also rerun on the same machine. It reached `Running` in 4,680 ms but skipped because the stub never received the nonce within its 60-second boot window. Thus the currently observed blocker is not unique to the plain PtyHost construction: neither lane produces a usable interactive stub-proxied composer here.

## Census

`pwsh -NoProfile -File scripts/codex-boot-census.ps1` is read-only. On the current database it found **10** matching boot-wedge rows among **89** Codex sessions since 2026-08-20. The script requires the `Guid:N` ANSI filename convention and `QueuedMessageOrigin.Delegation = 3`; both are explicitly pinned in the script.

## Consequence

P1/P2/P3/P4 numbers are intentionally not claimed. Before attempting the 30-launch runs, resolve why an interactive Codex configured with the local stub never paints the composer (or run this probe through a known-good authenticated/no-stub interactive configuration while preventing real turns). P3 is guarded to require an explicit executable from a scratch 0.149.1 npm prefix and will not fall back to the global shim.
