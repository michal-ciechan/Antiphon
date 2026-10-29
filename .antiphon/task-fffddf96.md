# Review fffddf96: card.ps1 404 plain-text error path (subject 028de754)

Reviewed tip db20d9395b7d1eae17303732905caea6382c980f (base rc/2026-10-27 fffa9e954e9dfe6685a4e6793ae0ae543688fcb8), server2/Linux, pwsh 7.5.4.
Scope: Interim, the brief's narrow named selection. The whole Unit lane was not run.

## Verdict

One defect (D-1, a new fail-open for in-process callers). With `pwsh -File` (the documented invocation) the behaviour and exit codes match base on every status.

## D-1: the 404 branch exits the script instead of throwing, so in-process callers no longer stop on a 404

- **Where:** `scripts/card.ps1:302` (`exit 1` inside `if ($status -eq 404)` in `Invoke-Antiphon`, lines 291-303).
- **Failure:** Base: under the script's `$ErrorActionPreference = 'Stop'`, `Write-Error` inside the catch raises `WriteErrorException`. The old trailing `exit 1` never runs in-process. The error propagates to the caller, so a `try/catch` catches it and a `-Command` chain stops. Tip: a 404 runs `exit 1`, which returns from the script with `$LASTEXITCODE=1` and no exception. Measured against a local stub returning 404 for `GET /api/cards/CARD-9999`:
  - `pwsh -Command "$c = & card.ps1 get CARD-9999 -Json | ConvertFrom-Json; 'NEXT STATEMENT RAN'"`. Base: the next statement does not run and the process exits 1. Tip: `NEXT STATEMENT RAN id=` prints and the process exits **0**.
  - `try { $r = & card.ps1 get CARD-9999 -Json } catch { 'CAUGHT' }` under EAP Stop. Base: `CAUGHT: WriteErrorException`. Tip: no throw, `$r` empty, `lastexit=1`.
  - The 404 text now goes to `[Console]::Error`, which skips PowerShell's error stream. An in-process `2>&1` or `-ErrorVariable` no longer receives it.
  - Statuses other than 404 (409/422/500/transport) still throw, so in-process callers now see two different failure contracts.
- **Why it matters:** 404 is the "card does not exist / not on this board / route missing" signal. An agent or orchestrator that runs card.ps1 in-process (`pwsh -Command "& scripts/card.ps1 ...; ..."`, a dot-sourced or `&` call from another script, or an interactive pwsh shell tool) previously stopped on a missing card. Now it continues past it, and with `-Command` the whole process reports success. No in-repo script calls card.ps1 in-process: the grep found only comments, and the other scripts each have their own `Invoke-Antiphon`. The exposure is ad-hoc agent commands. This is the "new fail-open" class the brief asked about.
- **Fix:** keep the plain `[Console]::Error.WriteLine` lines, so the phrase still arrives unsplit. Replace `exit 1` with a terminating error, e.g. `throw ("Antiphon {0} {1} failed (HTTP 404)" -f $Method, $Path)`. Keep its message short so ConciseView has nothing worth splitting, and do not repeat the detail in it. Under `-File` an uncaught throw still exits 1. Add a witness that is red on the current tip: (a) `pwsh -Command "& card.ps1 get CARD-9999; 'next'"` must exit non-zero without printing `next`; (b) `try { & card.ps1 get CARD-9999 } catch { 'caught' }` prints `caught`. Keep `Block_404_detail_stays_intact_when_concise_view_wraps_at_120_columns` green.
- **Landing owner:** Code task 028de754-d0c3-4839-8d99-023ca1636984.

## Base vs tip matrix (`pwsh -NoProfile -NonInteractive -File`, node stub, ANSI shown stripped)

| Case | Base exit / stderr | Tip exit / stderr |
|---|---|---|
| get CARD-9999, 404 JSON detail+code | 1, ConciseView `Invoke-Antiphon:` block, `... failed: No card ... code card_not_found` | 1, plain `Antiphon GET /api/cards/CARD-9999?cwd=... failed: No card ...` then `code card_not_found` |
| get, 404 empty body | 1, `failed: Response status code does not indicate success: 404 (Not Found).` | 1, same text, plain |
| get, 404 text body | 1, `failed: Not Found plain` | 1, same, plain |
| get -Board guid, 404 with candidates | 1, detail + candidate row joined | 1, detail then candidate row on its own line |
| history CARD-9999, 404 | 1 | 1, plain |
| block, relationships 404 | 1, phrase reflowed in ConciseView | 1, `failed: Relationship route unavailable` / `code relationship_route_missing` |
| unblock, relationships GET 404 | 1 | 1, plain |
| block, 409 / 422 / 500 | 1, Write-Error | 1, unchanged Write-Error (identical text) |
| get, relationships 404 (optional read) | 0, card printed, no stderr | 0, identical |
| connection refused | 1, `failed: Connection refused` | 1, identical (Response null, falls into the old path; no NullReference) |

The stub never served private-notes: 0 such requests logged.

## Checks requested by the brief

1. **Callers.** No try/catch inside card.ps1 wraps `Invoke-Antiphon`. The optional `get` relationships read calls `Invoke-RestMethod` directly and is unchanged (verified, exit 0). Repo scripts do not dot-source or `&` card.ps1. The tests launch it as a `pwsh -File` child. The only regression is in-process semantics (D-1).
2. **Stderr shape.** The `Invoke-Antiphon:` header and position lines are gone for 404. No test or server code matches that prefix or `WriteErrorException` (grep). With `-File`, stderr stays on the same stream. A whitespace-only line follows the plain text when there is a progress clear. That is harmless.
3. **5.1 and nulls.** card.ps1 remains ASCII-only (no byte > 127). No StrictMode. `$_.Exception.Response` is null-checked, the `InnerException` fallback is guarded, and connection refused takes the old path. On 5.1 the `[int]` cast of `HttpWebResponse.StatusCode` is sound by inspection. Windows PowerShell 5.1 was not executed here (no powershell.exe on server2).
4. **Witness.** It runs on Linux under a forced 120x40 pty (`script` + perl TIOCSWINSZ, ConciseView, Ansi rendering). It failed at base on the assertion: `error should contain "Relationship route unavailable" but was actually "Script started ..."`. Same tip-built binary, run against a detached base worktree's scripts. It passes at the tip.

## Test runs (one gated build, `dotnet exec --no-build`, both through scripts/build-slot.ps1)

- Build: `dotnet build tests/Antiphon.Tests -c Debug --property:OutputPath=bin-rv96/ -p:UseAppHost=false`. Succeeded; lease e6b37f4b, held 146s.
- Tip run (lease ef6572a7, 3m44s). Filter `/*/*/(Card*ScriptTests)|(RcRosterRepairTests*)|(WindowsUnitPlatformAdmissionTests*)|(C1514LandingSplitBudgetTests*)|(DelegateScriptVerificationRoundTests*)|(DelegatePlatformScriptTests*)/*`. Result: **68 total, 57 passed, 10 failed, 1 skipped.**
  - CardRelationshipScriptTests 8/8, CardListSearch 11/11, CardCloseDeploymentPrompt 5/5, CardDiagnose 2/2, CardPlatform 1/1.
  - RcRosterRepairTests 3/3. WindowsUnitPlatformAdmissionTests 4 passed (incl. C1210_Roster_closure_matches_source_gates), 1 NotExecuted (`C1210_Windows_linux_only_outcomes_are_exact`, Windows-only).
  - C1514LandingSplitBudgetTests 5/5 (C1404, C1577, C1584, C1410, G2865). DelegateScriptVerificationRoundTests 5/5, DelegatePlatformScriptTests 4/4.
  - CardFilePrivacyScriptTests 9 passed, 10 failed.
- Base run (tip binary against base scripts; lease 80670682): `(CardFilePrivacyScriptTests*)|(CardRelationshipScriptTests*)` gave 27 total, 16 passed, 11 failed. The same 10 CardFilePrivacy methods/cases fail there (inherited, Linux `C:\src\example` per the author), plus the witness (expected red).
- No PS51 ASCII census test covers card.ps1 by name. The NightlyScripts/RoutingPin/CommitOnSettle censuses cover other files. ASCII was checked directly.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef fffa9e95... -HeadRef db20d939...`: commits=3, violations=0, exit 0.
- Landability: `git merge-tree` against origin/rc/2026-10-27 8004259c70a068507decec1b1b9896d58c5b4047 is clean. Against origin/master c49efbdcabd6a333f1d80b279236388c189d6f18 the whole branch conflicts only on rc-carried `.antiphon/task-af2b78c8.md` and the CARD-1584 investigation doc. The three card commits each cherry-pick-simulate clean onto master (`merge-tree --merge-base=<c>^`). Compiling on master was not attempted.

Not checkpoint-tool runs: no SHA-validated receipts, so reviewedSourceClean is false.

## Advisories (no Review rerun needed for these alone)

- A-1: the motivating failure is native Windows (run 28bbb58e). No Windows execution of `Block_fails_loudly_when_relationships_returns_404` at the tip exists, so that native witness stays **pending**. On Windows the new witness raises a raw `SkipTestException`. A whole-class Windows checkpoint row would show skipped=1 and validate as row_failed, so exclude it by name there. The class is Category Integration, so the C1210 Unit skip roster does not apply. BuildSlotScriptTests has the same ad-hoc pattern.
- A-2: the witness needs util-linux `script` and `perl` on PATH. Without them `Process.Start` throws (red, not a skip). Present on server2.
- A-3: the 404 lines are now printed separately rather than joined into one Write-Error record. No consumer depends on the joined form.

Cleanup: the scratch base worktree was removed with `git worktree remove`, and all 32 `bin-rv96` directories were deleted by literal path. Nothing else touched.
