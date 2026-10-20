# CARD-1375 real producer capture provenance

Captured 2026-10-20 on Linux. These are curated parser inputs, not whole run logs.
Raw diagnostics, TRX and receipts remain ignored under `.antiphon/c1375-evidence/`.
Encoding: native UTF-8; selected diagnostic lines preserve LF, including final LF.
TRX selects exact Results and TestDefinitions sections; the harness adds only an XML root.
No newline variants, path substitutions, reconstructed state strings or reflection.

Probe build source: `eff6639ac191a555f76bdaf9e404a5eb52134132`.
Capture driver source: `41fe7c568cd256ff4b6a1a87fe19eabb5b203784`.
Packages: TUnit 1.44.0, Microsoft.Testing.Platform 2.2.2.
Assembly versions: TUnit.Core and TUnit.Engine 1.44.0.0; Microsoft.Testing.Platform 2.2.2.0.
Native MTP header commit: `3ee14aa6d97279c7b2d244657fafdd0c35f40242`.

The initial Timeout capture at `.antiphon/c1375-evidence/producer/` produced the requested state
and Failed result, but our validator incorrectly required summary `failed=1`. Native MTP
reports `timeout=1, executed=0, failed=0` for that Failed result. The validator was repaired;
the same triggers were rerun once (no alternative-trigger search). Both accepted captures below
completed without supervisor expiry, joined their root, drained both streams and observed zero descendants.

Build command:
```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1375-probe-build -SlotWaitMinutes 1 -- dotnet build scripts/fixtures/nightly/c1375-probe/Probe.csproj --property:OutputPath=bin-c1375-probe/ --property:UseAppHost=false --nologo
```
Capture command:
```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1375-probe-capture -SlotWaitMinutes 1 -- pwsh -NoProfile -File scripts/capture-nightly-probe.ps1 -ProbeDirectory scripts/fixtures/nightly/c1375-probe -ResultsDirectory .antiphon/c1375-evidence/producer-validated -ProbeBuildSourceSha eff6639ac191a555f76bdaf9e404a5eb52134132
```

## Timeout

Trigger: a real elapsed `[Timeout(100)]` on a token-aware five-second delay, with `--detailed-stacktrace`.

Literal argv (JSON array, passed without shell expansion):
```json
[
  "dotnet",
  "exec",
  "/work/worktrees/task-5cad4539/scripts/fixtures/nightly/c1375-probe/bin-c1375-probe/Probe.dll",
  "--treenode-filter",
  "/*/*/C1375Producer*/TimeoutProbe*",
  "--detailed-stacktrace",
  "--report-trx",
  "--results-directory",
  "/work/worktrees/task-5cad4539/.antiphon/c1375-evidence/producer-validated/TimeoutProbe",
  "--diagnostic",
  "--diagnostic-output-directory",
  "/work/worktrees/task-5cad4539/.antiphon/c1375-evidence/producer-validated/TimeoutProbe/diag"
]
```

Native results: one Failed result, zero passed/skipped; exit 2.
TRX summary: total=1, executed=0, timeout=1, failed=0. Duration 156.8098 ms >= 100 ms.
Supervisor execution 1980 ms; cleanup 33 ms.
Built DLL SHA-256: `4e18fe42b84ec870830f3564ea819a28d54d6322a6726906720bfa52e9436073`.

Raw files:

| Path | Bytes | SHA-256 |
|---|---:|---|
| `.antiphon/c1375-evidence/producer-validated/TimeoutProbe/diag/log_261020045205434.diag` | 12072 | `0498ec9d783e1f264521edf382bb3b90c2c63c328a9532b0df247e867f27e9ba` |
| `.antiphon/c1375-evidence/producer-validated/TimeoutProbe/_328f856108e4_2026-10-20_04_52_06.8436350.trx` | 3131 | `c125e71e4a768107e5a40952dab569a4ea3e0a4cd410a66932c96b3fe6eb055e` |

Selected byte ranges (zero-based offsets in the named raw file):

| Part | Source | Offset | Bytes | SHA-256 |
|---|---|---:|---:|---|
| MtpHeader | diagnostic | 0 | 153 | `020a1e16b882307072ed8ee3787ff1829e6afbe37060faa9012f95ab9c683b46` |
| TunitHeader | diagnostic | 5347 | 251 | `bd6ba2008c12bf15a018bd4d2e334c78a9730524664e419171591126802975ce` |
| Record | diagnostic | 6876 | 1378 | `95d53b9dff0eba840d3747aaed6732f3fca2473b9e69f7b26b8d16caafe93546` |
| Results | TRX | 548 | 685 | `573707522cdf3008ce960a33edcd78bfd9b17c96e96099600ad332c4f0e3fb3c` |
| Definitions | TRX | 1236 | 532 | `dc069edcb1695c0f8bb6c66f13b67cc37ff544106340617913d107b73850f973` |

## Cancelled

Trigger: local CTS cancellation awaited and caught, followed by public `Execution.OverrideResult(TestState.Cancelled, "CARD-1375 controlled cancellation")` and normal return. This does not establish automatic classification of external cancellation.

Literal argv (JSON array, passed without shell expansion):
```json
[
  "dotnet",
  "exec",
  "/work/worktrees/task-5cad4539/scripts/fixtures/nightly/c1375-probe/bin-c1375-probe/Probe.dll",
  "--treenode-filter",
  "/*/*/C1375Producer*/CancelledProbe*",
  "--detailed-stacktrace",
  "--report-trx",
  "--results-directory",
  "/work/worktrees/task-5cad4539/.antiphon/c1375-evidence/producer-validated/CancelledProbe",
  "--diagnostic",
  "--diagnostic-output-directory",
  "/work/worktrees/task-5cad4539/.antiphon/c1375-evidence/producer-validated/CancelledProbe/diag"
]
```

Native results: one Failed result, zero passed/skipped; exit 2.
TRX summary: total=1, executed=1, timeout=0, failed=1. Explanation and exception are empty; override reason is not the native explanation.
Supervisor execution 2373 ms; cleanup 0 ms.
Built DLL SHA-256: `4e18fe42b84ec870830f3564ea819a28d54d6322a6726906720bfa52e9436073`.

Raw files:

| Path | Bytes | SHA-256 |
|---|---:|---|
| `.antiphon/c1375-evidence/producer-validated/CancelledProbe/diag/log_261020045207701.diag` | 11830 | `7acf7d7add71a42d8f54d8e8ae1142cf8b26cce7fa49a410d6aa04c4ab4016ab` |
| `.antiphon/c1375-evidence/producer-validated/CancelledProbe/_328f856108e4_2026-10-20_04_52_09.4643402.trx` | 2942 | `2bc4edec80d51f01150bbdc61d7d7ed3cf448dcd73586f4ecc22848f3bed7e9b` |

Selected byte ranges (zero-based offsets in the named raw file):

| Part | Source | Offset | Bytes | SHA-256 |
|---|---|---:|---:|---|
| MtpHeader | diagnostic | 0 | 153 | `4b5dcd19b1e2597d1dc78520df40aefea6b4070713b47f2b1d6da07770d4ee89` |
| TunitHeader | diagnostic | 5353 | 251 | `8cf299b8928332c5faa788b6fa27b954c4adb22d3e1f4208cfd601b1fb6b58e5` |
| Record | diagnostic | 6820 | 1184 | `d3c9d16a2777e05e44230b0b32d6f421c74d66349982e09c7308c9a20ff24205` |
| Results | TRX | 548 | 493 | `541cc0006474fb904e5045c4bc49f06f47ebc49cf6b9194ae19663b9e263e312` |
| Definitions | TRX | 1044 | 536 | `a70ac420537651aa145afa2e9f0b0ac2d5f87f028de6250579ba4bfab5f62df0` |
