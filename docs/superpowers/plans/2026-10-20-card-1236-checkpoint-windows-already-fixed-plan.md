# CARD-1236: checkpoint Windows defects already fixed

Disposition: close CARD-1236 onto CARD-1310. Both reported defects are already
fixed in current master. No implementation or TestDesign dispatch is needed.
This is the durable result of the brief's source-inspection stop condition,
not a claim of fresh Windows execution.

Inspected on 2026-10-20, on assigned branch `feat/card-task-2e1a64f1`.
The task base, checkout HEAD and freshly fetched `origin/master` were all
`e8bd995b0481e838c0110217dc4f680fe4de8374`.
Publication scope is master only; this task changes only this document.

## Ground truth

| Card assumption | Current code and existing test | Disposition |
|---|---|---|
| A real confined Windows build injects `UseAppHost=false`, so the subsequent run cannot find `Antiphon.E2E.exe`. | `tools/Antiphon.Checkpoints/BuildConfinement.cs:545` adds the property only when `!OperatingSystem.IsWindows()` and no explicit value exists. Both `Rewrite` and project-graph `Evaluate` call that helper. `Execution/BuildStep.cs:28` already uses the same platform default for build/run arguments. `scripts/run-checkpoint.ps1:759,1058` also guards both confined injections with `-not $onWindows`. `CheckpointOutputConfinementTests.windows_confined_build_leaves_use_app_host_unset` inspects real confinement and the script's Windows build/evaluation argument paths. | Fixed by CARD-1310, commit `a625778ef9dbf532d1458afa8182e6119a6a1e30`, confirmed ancestor of inspected master. No row flag is needed. |
| A coverage-extension mapping file written by the build cannot be cleaned, making an otherwise passing Windows row red. | `Execution/RunScheduler.cs` passes the observed build exit to `CheckpointBuildBinding.Write`; `Evidence/SourceRunGuard.cs:121` seals the fresh output root. `src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs` adopts eligible child-written entries at seal. `OutputCustody.Stat` now rejects only `CheckpointPlatform.Other`, and `CheckpointNativeIo.CustodyStat` has a Windows implementation. `OutputCleanupTests.windows_seal_adopts_an_overwrite_before_exit_and_keeps_a_later_write` writes `.msCoverageExtensionSourceRootsMapping_App` twice before exit, then asserts adoption, no cleanup refusals, and removal of the output root. | Fixed by CARD-1310, commit `6a42c44eef6641baf35dc86a3bf723d72a3ea895`, confirmed ancestor of inspected master. The file's assembly suffix does not affect adoption. |
| The coverage filename needs an exception or a special ownership category. | `CheckpointOutputLedger.Check` checks each path against in-memory creation records, not a filename allowlist. Windows custody now checks owner, root DACL, link/reparse status and the observed time window. CARD-1318 hardened these checks and admitted copied files whose creation time is inside that window. Unknown or changed entries still refuse cleanup. | Preserve the general custody mechanism. A filename exception would weaken cleanup authority and merely expose the next unrecorded file on the old implementation. |
| The recent checkpoint changes might have made the old diagnosis applicable again. | Current source still contains both CARD-1310 fixes after CARD-1356's build-deadline work, CARD-1370's state-publication work and CARD-1297's selected-output inventory change at inspected HEAD. CARD-1368 additionally calls `OwnedOutputLedger.Adopt` after a completed row host exits, preserving existing creation records. | No source regression found in either reported behavior. Host-written `TestResults` adoption is separate from the original build-written mapping file. |

The earlier [CARD-1310 diagnosis](../../investigations/2026-10-17-card-1310-diagnosis.md)
explicitly identifies CARD-1236 as the duplicate covering both symptoms. It also
explains why the mapping file was simply the first unrecorded build output.
That document is historical diagnosis; the current implementation above is the
basis for this disposition.

## Decisions

- **D-1 — Close the duplicate after source inspection.** The brief explicitly
  stops implementation planning when both defects are already fixed. Reject
  another Code/TestDesign cycle: there is no remaining change to specify for
  these two defects.
- **D-2 — Keep the existing platform default and explicit property override.**
  Windows leaves `UseAppHost` unset; Linux keeps its existing `false` default;
  an explicit value wins. Reject a `needs-apphost` flag or E2E-project special
  case: the common Windows rewrite was repaired and the existing override
  already works.
- **D-3 — Keep custody-based cleanup.** Reject ignoring the coverage filename,
  replacing existing creation records at cleanup, or accepting changed files.
  The build's final bytes are adopted after the observed exit, and subsequent
  unexpected changes remain refusals.
- **D-4 — Record inspection, not new qualification.** No builds, tests, mutation
  controls or Windows E2E runs were performed by this Plan task. Existing test
  bodies are evidence of coverage intent; their presence is not a fresh green
  receipt. No whole-Unit run, deployment or restart is commissioned.

## Slices

| Slice | Files | Verification and completion |
|---|---|---|
| S1 — Publish the closure record | `docs/superpowers/plans/2026-10-20-card-1236-checkpoint-windows-already-fixed-plan.md` | Inspect the source/test methods listed above at fetched master; check patch whitespace and conflict-free merge-tree; commit and push on the assigned branch. |
| Product/test slices | None | Existing fixes and tests already cover the reported causes. There is no new red witness or narrow green to commission under this brief's stop condition. |

## Existing verification lanes

These are reference selections for a separately requested requalification, not
an executable checkpoint manifest or outstanding work for this closure. Each
selection is one exact method in `tests/Antiphon.Tests`; never broaden to Unit.

| Lane | Existing exact selection | What it can establish |
|---|---|---|
| Linux, unpinned default placement | `/*/*/OutputCleanupTests/external_build_payload_is_adopted_at_seal_and_removed_on_green` | Build-written leftovers are adopted and cleaned on the Linux custody path. This does not qualify NTFS behavior. |
| Linux, unpinned default placement | `/*/*/CheckpointSourceExecutionTests/host_written_entries_under_the_row_output_are_adopted_and_cleaned_on_green` | A passing row with host-written output remains GREEN, removes its output, and passes both receipt validators. This is the later CARD-1368 behavior. |
| Windows native; desktop runner required in the observed catalogue | `/*/*/CheckpointOutputConfinementTests/windows_confined_build_leaves_use_app_host_unset` | Windows confinement/evaluation do not inject `false`, and an explicit `true` survives. The test skips on Linux; it is not a full Playwright E2E smoke. |
| Windows native; desktop runner required in the observed catalogue | `/*/*/OutputCleanupTests/windows_seal_adopts_an_overwrite_before_exit_and_keeps_a_later_write` | The mapping-file overwrite is adopted and cleaned; late writes, hard links and reparse points retain their refusals. The test skips on Linux. |

`GET /api/runner-defaults` and `GET /api/session-runners` both succeeded during
inspection. Defaults revision was 2; the catalogue had eligible Linux runners
and one eligible Windows desktop runner. Re-read those routes before any later
dispatch. Omit `-Runner` for default placement and omit `-Platform` when no OS
is required; select `-Platform Windows` only for the two native rows above.
No fleet path or host address is embedded in this plan.

If either original symptom recurs on a newer source SHA, reopen investigation
with that SHA, the selected row, effective build/run properties, observed build
exit, refused path and custody reason. Measure those facts before proposing a
new apphost flag or weakening the ledger. This task adds no session that waits
for input and no session-release behavior.
