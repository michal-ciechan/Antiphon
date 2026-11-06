# CARD-1715 resumed-site red proof

M5 is killed by the new resumed dirty-checkout witness. The initial-site M1 and resumed-site M2 label regressions are also killed by their existing witnesses. After exact restoration and an incremental rebuild, the three exact methods pass and all five requested landing classes pass 130/130. No product or test changes are delivered.

Task: 5e5930cc. Tested source: `dec066906a1f4656f2f1d334d975df61b07c2817` (tree `d2aa2e27321edba6415b5cc3fb8122e78817276b`). Assigned report branch: `feat/card-task-5e5930cc`. Fresh detached worktree: `/work/worktrees/redprove-5e5930cc`, outside /tmp, now removed. All execution was local inherited execution on the Linux runner; no desktop execution, landing, rc/master push or land-graph update occurred.

## Bound probes and decisive assertions

The brief waives the ordinary-worktree Mutation sweep. This is its narrowly commissioned adequacy proof, not a claim that the complete pending CARD-1715 mutation pack has run.

One cold build packed three independently env-gated expressions into `AgentTaskLandSourceResolver.cs`; `C1715_REDPROVE` is set only in each child process. No switch means original semantics. Baselines used that packed build with the switch absent; they are semantic baselines, not a pristine-source build receipt. The actual restored source was rebuilt once before the restored green runs.

| Mutant | Exact method in AgentTaskLandHalfResetTests | Baseline | Mutant | Restored |
|---|---|---|---|---|
| M5 / PC-5 | C1715_Resumed_dirty_checkout_preserves_adopt_local_changed | 1/1 pass | 1/1 intended failure | 1/1 pass |
| M1 / PC-1 | C1715_Initial_adoption_preserves_untrusted_status_reason | 1/1 pass | 1/1 intended failure | 1/1 pass |
| M2 / PC-2 | C1715_Resumed_adoption_preserves_untrusted_status_reason | 1/1 pass | 1/1 intended failure | 1/1 pass |

M5 applies Review d67a21b1's exact condition replacement, `inspected.Reason == "status_untrusted"` to `inspected.Reason is not null`, at the resumed `resetResume` site (source lines 368-370). The brief's approximate line 510 identifies the initial site in this tip; the review's variable and reset-site definition determine the correct mutation.

M5 fails at `AgentTaskLandHalfResetTests.cs:49`: `row.SourceRefusalReason.ShouldBe("adopt_local_changed")` receives `"status_untrusted"`. It executed one test, with one assertion failure and no fixture/build failure. The witness first proves exactly one resumed reset following the real CAS interruption. Its restored green run also verifies preserved dirty bytes, reset intent, no publication/target push and no stderr explanation. The previously surviving M5 is therefore detected by this witness.

M1 forces `adopt_local_changed` only at the initial `afterLocal` site. M2 forces it only at the resumed `inspected` site. Each matching untrusted-status witness fails at the shared helper's line 154: expected `"status_untrusted"`, actual `"adopt_local_changed"`.

All PC runs used one exact method filter each; no class-wide run was credited as a PC. The three red children used different process-local switches with frozen source/output. The three restored exact checks ran concurrently with separate leases; fixtures use isolated databases and Git checkouts plus the existing Git process limiter. Every child was joined. The landing-class run was separate.

Direct TUnit/Microsoft Testing Platform returns **2** for these failed tests. Initial external supervisors expected checkpoint-translated exit 1 and emitted nonzero orchestration diagnostics. The fresh TRX files and exact assertion messages were validated, the external expectations were corrected, and no red tests were repeated or assertions changed. Baseline, restored build and restored tests all return 0.

## Restored regression result

| Class | Passed |
|---|---|
| AgentTaskLandSourcePersistenceTests | 35 |
| AgentTaskLandHalfResetTests | 61 |
| SlowTestRegistryMergeTests | 5 |
| AgentTaskLandRegistrationSafetyTests | 7 |
| LandSourceIdentityTests | 22 |
| Total | 130 |

Zero failed or skipped. The roster includes the new resumed dirty witness, both existing status-untrusted witnesses, and both `C1715_Post_reset_status_error_preserves_diagnostic(false/true)` argument cases. No Unit lane, frontend controls, PC-4 or diagnostic-omission mutants were added to this custom scope; their outstanding obligations are unchanged.

## Commands and provenance

Each build and test invocation used its own `scripts/build-slot.ps1` lease, native default output, `UseAppHost=false` and `TEMP=/tmp`. Admission was bounded at 10 minutes per invocation, test execution at `--timeout 5m`, and the outer build driver at 1800 seconds including admission. These are explicit task-local provisional bounds under the brief's generous-timeout instruction, not measured p95 claims. No limit was widened and no ungated retry occurred.

Build command, from the scratch worktree:

`TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label packed-build -SlotWaitMinutes 10 -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo`

Example red command after applying the retained packed patch:

`TEMP=/tmp C1715_REDPROVE=M5 pwsh -NoProfile -File scripts/build-slot.ps1 -Label M5-red -SlotWaitMinutes 10 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/AgentTaskLandHalfResetTests/C1715_Resumed_dirty_checkout_preserves_adopt_local_changed' --report-trx --report-trx-filename run.trx --results-directory /work/evidence/task-5e5930cc/rerun-M5 --timeout 5m`

For M1/M2, substitute their switch and exact method from the table. Green exact checks use the same filters with the switch absent after restoration and rebuild. The single restored regression filter was:

`/*/*/(AgentTaskLandHalfResetTests*)|(AgentTaskLandRegistrationSafetyTests*)|(AgentTaskLandSourcePersistenceTests*)|(LandSourceIdentityTests*)|(SlowTestRegistryMergeTests*)/*`

The only mutated file was restored with `git checkout -- server/Application/Services/AgentTaskLandSourceResolver.cs`. Original/restored SHA-256: `7efb2005694e73174cc6758d5690cd685568230921d05e665ad00d965b0f4b20`. Packed SHA-256: `88e5601eb99bbeca027179ca99013755b8a9386704fdd047945163cb240ea2be`. Tracked diff and status were empty before the restored build. Packed source and compiled hashes remained stable through red. The rebuilt server DLL differs from the packed DLL and matches its copy in the test output. Final restored hashes/status were checked again after green, before scratch removal.

Generated evidence is retained externally at `/work/evidence/task-5e5930cc`: `binding.json`, `packed-mutants.patch`, `packed-artifacts.json`, `restoration.json`, `restored-artifacts.json`, `red-validation.json`, `green-validation.json`, `cleanup.json`, `timing-summary.json`, per-run argv/exit JSON and logs, fresh TRX archived in `trx-evidence.tar.gz`, and extracted per-test assertions/durations. No generated payload is committed. The scratch worktree and its owned bin/obj outputs were removed using its exact literal absolute path. Standalone test-result directories were removed by exact literal paths after archiving their TRX evidence. The assigned worktree has only this report as a task delta.

## Measured cost

Seconds; lease-held time includes the driver/test host and is separate from queue admission. Parallel rows overlap; these values must not be summed into wall time.

| Invocation | Admission | Lease held | Invocation wall | Exit |
|---|---:|---:|---:|---:|
| packed-build | 0 | 468 | 469.272 | 0 |
| baseline-dirty | 195 | 31 | 227.377 | 0 |
| baseline-initial | 255 | 35 | 291.646 | 0 |
| baseline-resumed | 376 | 35 | 411.889 | 0 |
| M5-red | 225 | 32 | 258.707 | 2 |
| M1-red | 225 | 32 | 257.885 | 2 |
| M2-red | 240 | 35 | 276.538 | 2 |
| restored-build | 165 | 118 | 284.030 | 0 |
| green-dirty | 195 | 26 | 223.180 | 0 |
| green-initial | 226 | 30 | 257.731 | 0 |
| green-resumed | 211 | 29 | 240.873 | 0 |
| green-landing | 346 | 66 | 412.755 | 0 |

The cold build's compiler-reported elapsed time was 467.07 seconds; restored incremental build 116.79 seconds. Per-test durations and exact class/method membership are retained in TRX and extracted JSON. Fixture startup, database preparation, teardown and individual external-call phase splits are unknown; host startup/remainder is not reported as zero. This single loaded-host sample is not p95. No individual selection/build exceeded 30 minutes and no timeout was changed.

--- next stage ---
next: land
handoff: Land the CARD-1715 test/doc follow-up through the caller's normal route; M5 and both matching label mutants are killed, with exact restored greens and 130/130 landing results. This report does not close the remaining CARD-1715 work or discharge its unexecuted controls.
artifact: docs/investigations/2026-11-06-redprove-card-1715-resumed-site.md
