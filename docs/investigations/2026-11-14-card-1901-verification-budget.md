# CARD-1901 verification budget

Measurement task `7555b555`, 2026-11-14. Measurement-only work; no CARD-1901
implementation or live retirement operation is authorized by this report.

## Provenance

- Plan: `origin/feat/card-task-2c3ecf9c`, commit `3ef663e98d51e3063614207fcf22343b5b7e3fe9`;
  `docs/superpowers/plans/2026-11-14-card-1901-retirement-audit-plan.md`.
- Current master observed using `git ls-remote origin refs/heads/master`:
  `8c06c7e11f40cc224235ecbc68c2389913cc67ee`.
- Fresh detached measurement worktree: `/tmp/card1901-7555b555-master`, at that
  exact master commit. The assigned report branch starts at the same commit.
- Host: Linux x86_64, kernel `4.15.0-213-generic`, 24 available processors,
  .NET SDK `10.0.404`. The shared host is oversubscribed (CARD-1906).
- Raw evidence and local measurement drivers:
  `/tmp/card1901-7555b555-evidence`. Generated receipts, logs, TRX and phase
  journals are external to Git. Every driver uses `scripts/build-slot.ps1`;
  tests run serially via `dotnet run --project tests/Antiphon.Tests --no-build
  -p:UseAppHost=false`. Native default build output is retained for reuse.
- Admission is measured separately from lease-held execution. Load columns use
  `/proc/loadavg`; one run is not a p95. Uninstrumented phases remain unknown.

## Cold build

Fresh checkout outputs; shared NuGet cache retained. Command:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1901-cold-build -SlotWaitMinutes 10 -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -p:UseAppHost=false -m:1 -nodeReuse:false
```

| Outcome | Admission (s) | Execution (s) | Whole wall (s) | Load start (1/5/15 min) | Load end |
|---|---:|---:|---:|---|---|
| Passed; 0 errors, 2,091 warnings | 211.6 | 232.4 | 444.1 | 19.50 / 23.57 / 22.02 | 23.14 / 22.06 / 21.67 |

MSBuild itself reported 231.22 s. The graph includes the checkpoint coordinator
and referenced fixture/probe projects; no separate coordinator bootstrap build
was needed. It does **not** build the standalone derived-impact regeneration
driver. See the prerequisite finding below. Recommend a provisional **8 min
cold-build execution cap**, approximately twice this one observation. Keep the
**10 min slot-admission cap** independent. This is n=1, not a p95, and measures
the native-output Debug build rather than checkpoint cold custom-output builds.

## Budget arithmetic that Code must replace

| Plan accounting | S1 | S2 | S3 | S4 Linux | Windows | Total test minutes |
|---|---:|---:|---:|---:|---:|---:|
| `EstimatedMinutes` checkpoint cells | 7 | 7 | 17 | 15 | 10 | 56 |
| Performance-section approximations | 1 | 2 | 7 | 6 | 3 | 19 |

The performance section's 39–59 min adds five 4–8 min cold builds to the second
line. Applying those same build estimates to the actual checkpoint cells gives
**76–96 min**, before admission. Neither is a measured duration. Mutation's
separate provisional 95–190 min is additional, not included in either total.

## Measurement status

Existing-test measurements are in progress. Test rows execute alone and serially
inside one foreground, slot-gated measurement driver. Top-level test launches
use `--no-build`; its admission is charged once. Each child row reports zero
additional admission and records reuse of the held lease. Existing policy tests
compile their own temporary probe projects as fixture work; this cost is included
in those test rows. The cold build above has its own independent lease.

## Fresh-checkout prerequisite finding

`DerivedImpactIndexTests` returned **27 executed / 26 passed / 1 failed / 0
skipped** in 204.83 s. The failing method is
`regenerate_script_reports_changed_then_unchanged`. Its assertion expected exit
0 and received exit 1:

```text
The application to execute does not exist:
scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll
```

This occurred on untouched master, before any CARD-1901 code. It is a missing
native preparation build, not evidence of a CARD-1901 behavior regression.
`scripts/regenerate-derived-impact-index.ps1 -NoBuild` deliberately requires the
already-built driver. The initial build inventory omitted this standalone
project. Add declared **CP-0** preparation:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1901-impact-driver -SlotWaitMinutes 10 -- dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -m:1 -nodeReuse:false
```

Board search `card.ps1 search 'DerivedImpactIndex' -Board Antiphon -All` completed
with 13 hits. Full `CARD-1868` text explicitly records this exact prerequisite in
A-2, including the class-wide selector and failing method. Reuse CARD-1868; no
duplicate was filed. The cold driver build and post-preparation recheck are
pending at this evidence slice. Preserve the original red and its timing.
