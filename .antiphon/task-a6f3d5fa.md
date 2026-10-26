# Land/remote residual Debug: a6f3d5fa

Fixed the Windows Git executable-name lookup with a Linux red/green string witness and added native rerun diagnostics. The original Windows 3+1 residuals are not fully resolved or qualified: latest-master Linux exposes inherited graph drift, and the remote class exceeded this task's bounded execution window.

## Source and delivered slices

Assigned branch: `feat/card-task-a6f3d5fa`. Worktree: `/work/worktrees/task-a6f3d5fa`.
Fetched starting master/base: `d78b580951d34bb6907d92cfbbba695553e4033c`.
No branch switch, rebase, target merge, tags, release scripts, restart or production-code change.

Each slice was pushed independently:

- `88f38eef2fc03b3c881bee092e1ad8f754b88aba`: `scripts/test-land-git-resolution.ps1`, reproducing the existing lookup failure with Windows-style PATH input.
- `b3fb7b9fc0620398dad6a6c6658d63b3ea2ac116`: `LandNonCodeSkipTests.WhichGit` now searches for `git.exe` on Windows and `git` elsewhere.
- `e3d30b79b8bb86f01f0cad4399c981297a42e406`: assertion diagnostics include the landing operation's publication/reason/verification command and graph counts; the missing-NUL fixture logs its Git/shim/byte identity; `RemoteScriptContractTests.LinuxShell` logs transport, normalized input SHA-256, exit and elapsed time. Child stdout returned to assertions is preserved. Existing assertion outcomes and timeouts were retained.

The Git witness compiles the exact helper extracted from the C# source, modeling only PATH, the OS selector and File.Exists. It changes no global environment or filesystem lookup state. Base: Unix passes, forced Windows fails with `git was not found on PATH.` Fixed: both pass. It proves executable-name selection, not native Windows process launch. The `git.cmd` interception used later by the missing-NUL fixture still needs a native observation; do not infer that the complete method now passes on Windows.

## Executed verification

Exactly one isolated full build, at the base SHA:

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label a6f3d5fa-build -- dotnet build tests/Antiphon.Tests --configuration Debug --property:OutputPath=bin-a6f3d5fa/ --nologo
BUILD SLOT granted lease=4002de8b-551b-4a75-a108-e7b15e755d8a waited=0s maxcpucount=6
1196 Warning(s), 0 Error(s), Time Elapsed 00:03:34.64
BUILD SLOT released lease=4002de8b-551b-4a75-a108-e7b15e755d8a held=215s
```

Compiled `Antiphon.Tests.dll`: 68,757,504 bytes; SHA-256 `3629fa215fc6fd5fcd8ab26e50d93ee5d56faa35bbdba8b4919767112d7154bc`.
All TUnit runs reused that output via `dotnet run --project tests/Antiphon.Tests --no-build --configuration Debug --property:OutputPath=bin-a6f3d5fa/ -- ...`, through the build-slot wrapper. No Unit lane or second full build was run. The final C# edits therefore have no fresh full-assembly build receipt; the exact changed lookup helper and remote-shell helpers were compiled separately with Add-Type for the string/static/probe checks below.

| Selection/check | Actual result | Evidence under `.antiphon/a6f3d5fa/` |
|---|---|---|
| `/*/*/LandNonCodeSkipTests/*` | 104 executed: 94 passed, 10 failed, 0 skipped; 11m26.165s; exit 2 | `land-base.log`, `land-base/run.trx` |
| `/*/*/RemoteScriptContractTests/*`, `--timeout 18m` | Aborted at 17m59.893s; console: 46 completed passes, 0 assertion failures, 0 skips; exit 3; **no final TRX** | `remote-base.log`, phase JSONL |
| `/*/*/RemoteScriptContractTests/C980_*` | 4/4 passed, 0 failed/skipped; 1.760s | `path-conversion.log`, `path-conversion/run.trx` |
| `scripts/test-land-git-resolution.ps1` before fix | 2 cases: 1 passed, 1 expected Windows-name failure | `git-name-red.log` |
| Same witness after fix and after diagnostics | 2/2 passed each time | `git-name-green.log`, `git-name-final.log` |
| Exact `PrepareLinuxShellScript` / `LinuxShell` source fragments | Both compile, including Windows runtime branches; final helper's Linux exit-0 and exit-7 probes both preserve stdout and emit diagnostics | `compile-remote-shell.ps1`, `remote-static-final.log` |
| Gated `scripts/audit-land-verification-graph.ps1 -Check` on unchanged base definitions | Exit 1: Card0418 Markdown admission refused | `graph-check.log` |

The canceled remote run also has 95 queued cancellation headers, and its sidecar records one in-flight `Failed` row for `C1105_Resume_digest_pins_previous_sha12` variant `4.1.0` at cancellation. These are retained as incomplete/censored evidence, not recast as the original Windows failure or as a completed class pass. Its completed resume matrix passed in 364.235s. Bash subphase attribution is unavailable where the recorder reports remainder. The first standalone static-check harness attempt lacked a nullable annotation context and failed CS8632; correcting that evidence-only harness produced the successful compile before and after diagnostics. It was not a product/test-source compiler failure.

The compiled source for all TUnit rows was the base SHA. Land ran before edits. Remote/C980 ran while their consumed C# source and scripts were unchanged; runtime HEAD was the Git-lookup-only slice `b3fb7b9fc0620398dad6a6c6658d63b3ea2ac116`. Source stayed frozen throughout each long run. Phase capture was enabled with run/group/attempt/suite metadata, and per-test durations remain in the TRX/JSONL. Owned child identities were checked after remote cancellation: none remained. All 33 producer-owned `bin-a6f3d5fa` directories were removed after execution.

Final helper witness source-file SHA-256: `609733BD836460918BAE206B0F3CDB57A20A3B728A49B9364644DFA286A87861`.

## Inherited Linux graph failures

The ten failures are six `C1380_RegularDocumentationChangesSkip` variants (add/edit/delete/rename/README/empty), `C1380_DocsOnlyRebasedLandRecordsNonCodeSkip`, `C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff`, `C1385_UnrelatedScriptChangeStillSkips`, and `C1385_RegistryMatchesNarrowGraph`.

The registry computes `124B52D47DD3232B60F4AEAF083B5E50ACC68DEDDE374AB2ECD6D43E543DC055`, while the embedded/current record declares `8D4CCE85EBDE82978FD648F0F5DDCEA221A3F492C4A28D67DCD1A7A549FCF34B`. The test project changed since the last audit-record commit: `6befcc97c22e12e58ac4ae538010dec8b517d597` added `Content Include="Fixtures\Card0418\**"`. Audit regeneration is currently refused:

```text
definition file item selects a markdown or text document the audit does not allow; -Update cannot approve it: tests/Antiphon.Tests/Antiphon.Tests.csproj Content Include=Fixtures\Card0418\** -> tests/Antiphon.Tests/Fixtures/Card0418/current-snapshots.md
audit refused: land graph pre-check refused
```

Duplicate search found existing Antiphon **CARD-1587**, whose full description records the same inherited refusal and owns reconciliation of the transport fixtures with audit admission. No duplicate was filed and no allowance, manifest override, generated approval or transport fixture was changed here. CARD-1537 was read in full: its committed-byte/mode repair is present, and its two CRLF seed witnesses pass in this Linux baseline. That historical diagnosis is not relabeled.

The original three Windows Land failures and one Remote failure were supplied only as class/count summaries. The older `d805bdeb` extraction predates those split results. Exact method names/errors were requested but not received. These Linux failures and the executable lookup defect are not asserted to be identical to those four native records.

## Required native follow-up (not executed)

Build the resulting source once on Windows through `scripts/build-slot.ps1`, using Debug and a fresh `bin-a6f3d5fa-native/` output. Reuse it with `--no-build`, separate results directories and the following exact selections. Retain TRX, diagnostics and timing metadata; a cap/skip is not a pass.

| Row | Exact filter | Purpose / provisional execution cap |
|---|---|---|
| W-1 | `/*/*/LandNonCodeSkipTests/C1390_MissingNulFailsUnderNonInvariantCulture` | Verify real `git.exe` lookup and whether the `git.cmd` shim actually intercepts the no-shell Git reader; retain `C1390_MISSING_NUL`; 3m |
| W-2 | `/*/*/LandNonCodeSkipTests/*` | After CARD-1587 and valid audit regeneration, identify/verify the three original native residuals while retaining every assertion; 20m |
| W-3 | `/*/*/RemoteScriptContractTests/*` | Identify the original native residual using `REMOTE_SHELL_START/EXIT` plus full errors; 18m, with incomplete status if capped |

The final changes add no `#if/#elif WINDOWS` members or imports. Static inspection checked the Windows filename branch, missing-NUL diagnostic expression and WSL launch branch; the exact lookup and shell methods compile in the standalone probes. This is not a native build or WSL/API execution receipt.

## Publication checks

Code tip `e3d30b79b8bb86f01f0cad4399c981297a42e406`: evidence-history check over base..tip returned `commits=3 entries=0 violations=0`. Fresh `origin/master` was `2b3ed0ed847be7d1556308fe30fb54ccced5a86c`; `git merge-tree --write-tree origin/master HEAD` exited 0, tree `ed1b110e63b3ed59a01b0b904b2bdd50a2d4d018`. Final report-only commit and checks are identified in the caller receipt.

Overall rc/class verification remains incomplete. The fixture-only repair has the explicit brief's no-Review route; native follow-up and CARD-1587 remain separate work. Restart: none.

--- next stage ---
next: land
handoff: Land the verified fixture lookup/diagnostic slice only; retain CARD-1587 and W-1/W-2/W-3 as unqualified follow-up work, and do not credit the aborted remote class or overall rc gate.
artifact: /work/worktrees/task-a6f3d5fa/.antiphon/task-a6f3d5fa.md
