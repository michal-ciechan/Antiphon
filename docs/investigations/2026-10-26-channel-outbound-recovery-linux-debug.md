# Channel outbound recovery: Linux debug of Windows group 37

Task `efa6bbf0`, 2026-10-26. The two failures reported for
`ChannelOutboundRecoveryTests` in native Windows qualification `8f055f87` did not
reproduce on Linux: **18 passed, 0 failed, 0 skipped**. No fixture, assertion,
timeout, or product code changed. The failing Windows case names and exceptions
were not supplied, so their root cause remains unclassified pending native evidence.

## Source and execution

- Tested source: `bedea1c03cd788d83b2ff54c40bfb603927bd69c`, equal to fetched
  `origin/rc/2026-10-26` when work began; assigned branch
  `feat/card-task-efa6bbf0`. No rebase or branch switch occurred.
- Original Windows candidate: `105792f7d22f297746cdb338976f52e424b76fa7`.
  The recovery test file and `tests/Antiphon.ChannelOutbound.Probe` have no delta
  between that candidate and the Linux source tested here.
- Host: server2 runner mirror, Debian 12 Linux x64, SDK 10.0.403, runtime 9.0.22,
  TUnit 1.44.0. Exactly one build and one class run, both through build-slot.ps1.
- Build: exit 0, 0 errors, 1196 warnings; MSBuild elapsed 4m20.94s,
  slot held 262s, slot wait 0s, maxcpucount 6.
- Tests: exit 0, 18/18 passed, duration 7m09.480s; slot held 431s, wait 0s.
  Per-case TRX durations range from 0.078s to 56.997s. The running converter
  passed in 56.997s; the five process-death arguments all passed. No repeated
  run was necessary because no cluster reproduced. No desktop process or native
  qualification lock was touched.

The exact portable commands were:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label efa6bbf0-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-efa6bbf0/ --nologo --verbosity:quiet
pwsh -NoProfile -File scripts/build-slot.ps1 -Label efa6bbf0-baseline -- dotnet tests/Antiphon.Tests/bin-efa6bbf0/Antiphon.Tests.dll --treenode-filter '/*/*/ChannelOutboundRecoveryTests/*' --report-trx --report-trx-filename base.trx --results-directory /work/worktrees/task-efa6bbf0/.antiphon/debug-efa6bbf0/base
```

The compiled assembly was run directly, with no implicit rebuild, because its
68,771,840-byte size exceeds the checkpoint reader's 64 MiB limit.
Phase recording used `ANTIPHON_TEST_PHASE_ROOT`, run id `efa6bbf0-base`, group
`ChannelOutboundRecoveryTests`, attempt `1`, suite `Antiphon.Tests`.
TRX is authoritative; phase coverage is partial, with unrecorded external-call
and startup attribution unknown rather than zero.

## Evidence and native handoff

Full logs, TRX, phase JSONL and output inventory were retained outside the retiring
worktree at `/work/repos/antiphon/.git/antiphon-evidence/task-efa6bbf0/`.
All 33 task-owned `bin-efa6bbf0` output directories were removed after the run.

- `base/base.trx` SHA-256:
  `D8342121D79C0340CFA70C729A31729F3CBEB4B68AC2E74E91ECB5844269B87B`.
- Tested `Antiphon.Tests.dll` SHA-256:
  `E9F85249B3CD7A3E4535B0C83458D0E157FF3A70148DE50F7D4740F103A3BBCA`.

Pending native rerun row: Windows, `tests/Antiphon.Tests`, exact filter
`/*/*/ChannelOutboundRecoveryTests/*`, expected 18 cases, after the native
qualification owner admits the run. Build once into an isolated output through
`scripts/build-slot.ps1`, then invoke its compiled `Antiphon.Tests.dll` through
the same gate with `--report-trx`. Retain failing case names, exception and TRX.
This Linux pass does not qualify the Windows row or establish that its failures
were flaky. No Windows-semantics cluster was established, so no speculative
pure-string witness or source fix was added. There is no code fix to land on rc
or master; this branch carries only the diagnostic report.
