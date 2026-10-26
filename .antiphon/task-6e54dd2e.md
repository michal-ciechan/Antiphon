# Debug outcome: native group 49

No Linux failure reproduced. PhoneHomeCompletedCardTransportTests passed both cases; all six GrokDelegateEndToEndTests skipped at their explicit Windows guards. No fixture or product repair is justified by these results. Native failures remain unresolved, and this report does not grant native qualification credit.

Source: bedea1c03cd788d83b2ff54c40bfb603927bd69c, equal to fetched origin/rc/2026-10-26 at task start. Assigned branch feat/card-task-6e54dd2e was retained without rebase. Historical Windows failure source: 105792f7d22f297746cdb338976f52e424b76fa7, run 8f055f87, antiphon-all-group-49 (3 Grok failures, 1 phone-home failure). Desktop evidence was unavailable here and the native supervisor/lock was untouched.

## Linux verification

Debian 12, linux-x64, SDK 10.0.403, runtime 9.0.22, TUnit 1.44.0. One isolated build through scripts/build-slot.ps1: exit 0, 0 errors, 1196 warnings, MSBuild elapsed 4m45.51s, lease held 286s; slot wait 0s. No source changed before or during execution.

| Exact filter | Passed | Failed | Skipped | Runner duration | Exit |
| --- | ---: | ---: | ---: | --- | ---: |
| `/*/*/PhoneHomeCompletedCardTransportTests/*` | 2 | 0 | 0 | 3.681s | 0 |
| `/*/*/GrokDelegateEndToEndTests/*` | 0 | 0 | 6 | 46.174s | 8 |

Phone-home cases: C1032_TransportClientUnsupported passed (1.8641998s); C1032_TransportQueueRecipientReplay passed (0.8418625s). Leases held 7s and 50s respectively; both waited 0s.

Grok skip reasons: one Windows raw-argv policy test and five ConPTY-only tests. This harness uses staged fakegrok.exe/fakeclaude.exe and disables Grok credential probing; real Grok CLI/auth is not its prerequisite. No passing Grok execution is claimed. Its assembly warm-up still started the shared PostgreSQL fixture; log reports schema-cache miss 35.972s and container deletion at 44.10s. These nested observations are not additive phase totals. Detailed phase/external-call attribution is unknown: requested phase capture omitted required attempt/suite metadata and emitted no phase files. TRX retains individual case durations. No timeout, retry or assertion changed.

Classification: phone-home native-only/unreproduced; Grok platform-dependent/unexecuted. CARD-0680 is an unconfirmed hypothesis here. No Windows-string semantics defect was identified to support a pure-string witness or repair.

Raw evidence (gitignored): `/work/worktrees/task-6e54dd2e/.antiphon/debug-6e54dd2e/` contains build.log, phonehome.log, grok.log, phonehome/phonehome.trx and grok/grok.trx. Linux commands used the build-slot wrapper, `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-debug-6e54dd2e/ --property:UseAppHost=false --nologo`, then `dotnet run --project tests/Antiphon.Tests --no-build` with both same properties, each exact filter above and fresh TRX output. No checkpoint reader was used because the test DLL exceeds its 64 MiB limit. Producer-owned bin-debug-6e54dd2e directories are removed after execution.

## Native handoff

After the existing qualification owner releases the native lane, build once at the intended rc tip through the build-slot wrapper using `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-debug-6e54dd2e-native/ --nologo` (retain Windows apphosts). Then run these two separate rows through the wrapper:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group49-native-grok -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-debug-6e54dd2e-native/ -- --treenode-filter '/*/*/GrokDelegateEndToEndTests/*' --report-trx --report-trx-filename group49-grok.trx
pwsh -NoProfile -File scripts/build-slot.ps1 -Label group49-native-phonehome -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-debug-6e54dd2e-native/ -- --treenode-filter '/*/*/PhoneHomeCompletedCardTransportTests/*' --report-trx --report-trx-filename group49-phonehome.trx
```

Grok requires staged fake providers, ModernConPTY, PowerShell and the shared PostgreSQL test infrastructure. Inspect actual failing method names, exceptions and retained native PTY diagnostics before commissioning a fix. Phone-home's replay case is explicitly Linux-only, so its Windows failure must be C1032_TransportClientUnsupported. Expected Windows roster: six Grok cases; phone-home one executed case and one Linux-only skip. No whole-group rerun or desktop process operation was performed by this task.

Landing observation: report commit 890b0783fccffb1f8b2cc95f0a978a1ba6566d28 has a clean merge-tree with origin/rc/2026-10-26 at bedea1c03cd788d83b2ff54c40bfb603927bd69c. Merge-tree against fetched origin/master b6db2de3dd0c0bad2ba30823d01882ca6fc9388e conflicts at `.antiphon/task-af2b78c8.md` and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`. The same conflicts reproduce with the unchanged task base, so they are inherited rc/master divergence, not this report's delta. No merge, rebase or conflict repair was attempted. Landing owns reconciliation; do not claim the whole branch is master-landable. Evidence history guard reported one report entry and zero violations. All 33 producer-owned output directories were removed.

--- next stage ---
next: land
handoff: Report-only branch; no product fix or native gate credit. Caller owns the exact native reruns after lane release. No fixture-only Review needed.
artifact: .antiphon/task-6e54dd2e.md
