# S4a B-switch mutation slice: B17 and B18

**Incomplete after reopening: the two exact restored-green methods cannot run because the prior settlement removed all build outputs, and the continuation explicitly forbids rebuilding.** Both selected mutants previously failed at their required preservation assertions. The fresh restored-source build passed, but its first green method never launched (exit 4 after five minutes, position 2, occupied 4/4); the second was not requested. There is no restored-green credit.

## Reopened continuation, 2026-11-06 10:00 UTC

The caller authorized only the two missing exact selector-off green methods, stated that the restored build exists, and explicitly instructed: "No new reds or rebuilds in this slice." Direct filesystem inspection found `tests/Antiphon.Tests/bin`, `tests/Antiphon.Tests/obj`, `server/bin` and `server/obj` absent. The preceding report and cleanup log already recorded removal of all 72 task-created output roots. No server/test DLL copies were preserved under the external evidence root. The previous build hashes prove the earlier build, not current runnable artifact availability.

No build/test driver or lease was requested on reopening, so there are no new tests, counts, TRX identities or inferred green results. No implicit build, substitute assembly, new red or rebuild was attempted. Source remains restored and unchanged at the tested base; reopening HEAD `789d18cce4b962849b16575ba41bb0ee5c83afb4` adds only this report to that base. The fresh preflight record is `/work/evidence/task-8b3bd292/resume-1-preflight.json`.

**Required caller action:** authorize one fresh restored-source build before the two exact green methods, or provide a complete runnable restored artifact set with verifiable source/build provenance. The explicit no-rebuild constraint prevents finishing this continuation. Preserve the earlier two baseline passes, two intended reds and successful restored-build record as historical evidence. The execution and restoration ledger below is unchanged.

2026-11-06, Custom task 8b3bd292, Linux/server2, ordinary worktree `/work/worktrees/task-8b3bd292`, assigned branch `feat/card-task-8b3bd292`. This is the explicitly **waived ordinary-worktree informational sweep**, not CARD-0478 SourceLanding evidence. No operation identity was invented. The caller's standing authority covered this work; no additional approval was requested. No product/test repair, desktop execution, landing, rc push, activation or land-graph update occurred.

## Binding and frozen build

Tested base and HEAD throughout execution: `fed5899951daa3c66be3dee31355e6261286dc01`. Initial tracked source/index were clean and there were no bin/obj roots. The investigation was read in full at `0037ffbd94ab6d42074d77b31f90015f43ea6a34`; precedent Custom 6569dd36's full brief/result/report was read at `7e8a86a4e13fbe51cf98df1a1f5a0bbb677a6e1b`. The cited 27123eab Code report was also read. Its historical author reds are not counted as this task's executions.

Only `server/Infrastructure/Git/LandingGit.cs` was temporarily edited. One environment variable, `ANTIPHON_MUTATION_8B3BD292`, selects exactly one of the two IDs by ordinal equality; empty means off. A module initializer rejects unknown values before test execution, and the external driver independently refuses unknown IDs before launching a child. No test-name/reflection mapping was used. The complete switch and both sites are archived in `mutants.patch` and `packed.cs` under the evidence root. Source/properties were frozen across the four packed-assembly method runs; per-run hashes before/after prove unchanged source and assemblies.

| Selector | Base site | Sole active change | Exact witness |
|---|---|---|---|
| B17-unregisterinner | LandingGit.cs:826, UnregisterWorktreeAsync | Short-circuit only the inner DestructiveRefusalAsync read after the common-directory/gitdir awaits | Late_switch_inside_unregister_keeps_the_registration |
| B18-childstart | LandingGit.cs:254, ExecuteProcessAsync | Replace only the mutating-child gate result after journal creation with null | Late_switch_before_branch_delete_start_keeps_the_branch |

B17 retains the child-start gate; B18 retains the unregister gate. Neither removes an additional fence. Every method uses class `C0824DesktopLegacyReleaseSwitchTests*` with one of these literal filters:

```text
/*/*/C0824DesktopLegacyReleaseSwitchTests*/Late_switch_inside_unregister_keeps_the_registration
/*/*/C0824DesktopLegacyReleaseSwitchTests*/Late_switch_before_branch_delete_start_keeps_the_branch
```

Both methods are unparameterized; each completed invocation discovered and executed exactly its one named row, with zero skips. Fixtures use their existing isolated PostgreSQL database clones, scratch Git repositories and local bare origins. The production-runner guard remained active. Docker 27.5.1 and postgres:16-alpine were available. No Pty test assembly was run.

## Results and fresh TRX identities

| Control | Selector-off baseline P/F/S | Selected red P/F/S | Fresh restored-source green | Result |
|---|---|---|---|---|
| B17-unregisterinner | 1/0/0 | 0/1/0 | Not run: admission timeout, no TRX | Intended mutation detected; full cycle incomplete |
| B18-childstart | 1/0/0 | 0/1/0 | Not requested after timeout | Intended mutation detected; full cycle incomplete |

Totals: **four executed tests, two baseline passes, two expected mutation failures, zero skips**. Two builds passed with zero errors (packed: 1769 warnings; restored: 1746 warnings). One additional test-driver request timed out before admission and executed zero tests. No unrelated red, equivalent, missing-witness or second-guard-masked result occurred in these two selected controls. Neither timeout nor missing execution is a kill. The two detections are not claimed as completed restored-green PC cycles.

Fresh TRX files are `/work/evidence/task-8b3bd292/<run>/run.trx`; full assertions/stacks and expanded rosters are retained unchanged.

| Run | TRX TestRun ID | Execution ID |
|---|---|---|
| baseline-B17 | 41c225f3-8d70-44e8-9723-3ec9d84ef010 | 3f063d5a-d792-4d51-aa54-df474cb0ab36 |
| baseline-B18 | 740fcdcd-5ded-404c-86fb-f4908b3a966e | bd3d881d-52f9-4c88-acda-a55693faca18 |
| red-B17 | 2a7f2dff-ef3c-489b-982e-fafde6ee1172 | a9122df8-ea9a-4dbc-b0c4-103a31cad965 |
| red-B18 | 07b38490-f2a7-43d5-a93b-9229aabb66c5 | 75785e70-841c-4425-93c2-2c391e542afa |

B17, witness line 583:

```text
ShouldAssertException: await w.RegisteredAsync(a.Path)
    should be
True
    but was
False

Additional Info:
    Git's entry was never dropped
```

B18, witness line 630:

```text
ShouldAssertException: await w.BranchExistsAsync(a)
    should be
True
    but was
False

Additional Info:
    the local branch is intact
```

The unmutated packed baselines passed these same assertions. No inherited-red attribution or repair was needed. Red driver exit 2 is Microsoft's test-failure exit, supported here by nonzero fresh TRX assertion failures; it is not a compilation/fixture failure.

## Admission, execution and phase cost

Every build and each method driver independently invoked `scripts/build-slot.ps1`; all six admitted operations had genuine grant/release lines. No unleased fallback, unlimited mode, bypass, lease around a loop or admission retry occurred. Native default output and `UseAppHost=false`, `TEMP=/tmp` were identical for builds and no-build runs. Builds allowed 10 minutes admission and 20 minutes execution; exact methods allowed 5 minutes admission and 3 minutes execution, without changing test attributes. The archived foreground Node supervisor starts its execution timer on the grant, joins the wrapper, and records commands/exit/hashes. Its outer envelopes were 31 minutes per build and 9 minutes per method, inside this task's 90-minute outer budget. No execution deadline fired.

Observed admission includes wrapper startup; broker-reported waits are separately preserved. Execution is grant-to-wrapper-exit, not a claim of pure test-body time.

| Operation | Exit | Broker wait s | Observed admission s | Execution s | Driver wall s |
|---|---:|---:|---:|---:|---:|
| packed-build | 0 | 0 | 1.120 | 456.976 | 458.096 |
| baseline-B17 | 0 | 45 | 46.406 | 30.257 | 76.663 |
| baseline-B18 | 0 | 0 | 2.358 | 33.192 | 35.550 |
| red-B17 | 2 | 15 | 15.784 | 23.221 | 39.005 |
| red-B18 | 2 | 0 | 0.797 | 40.977 | 41.774 |
| restored-build | 0 | 0 | 1.235 | 161.690 | 162.925 |
| restored-B17 | 4 | timeout after 5m | 301.898 admission-only | Not launched | 301.898 |
| restored-B18 | — | Not requested | — | Not launched | — |

Seven sequential driver requests consumed 1115.911 seconds: 369.598 seconds admission/envelope and 746.313 seconds grant-held execution. Build/test request window: 09:35:08.657–09:55:07.753 UTC (19m59.096s including gaps). Preparation/restoration/cleanup are separate from driver sums. Packed MSBuild elapsed 7m36.04s; restored MSBuild elapsed 2m39.65s. No individual selection, build or execution batch exceeded 30 minutes. These small live samples do not establish a new p95 or authorize larger slices; the prior performance investigation 5ae98d31 remains the scheduling reference.

| Completed method phase | TRX host window s | Before body s | TRX body s | After body s |
|---|---:|---:|---:|---:|
| baseline-B17 | 26.813 | 14.688 | 5.507 | 6.617 |
| baseline-B18 | 27.489 | 14.546 | 11.211 | 1.731 |
| red-B17 | 20.886 | 12.040 | 4.196 | 4.650 |
| red-B18 | 37.866 | 23.219 | 11.142 | 3.505 |

Before/after-body windows combine discovery, startup, fixtures, reporting and teardown; their complete internal attribution is **unknown**, not zero. Existing opt-in phase JSONL was retained for each admitted method. Run-scope completed db-create spans were 1.993/2.162/2.220/3.049 seconds respectively; fixture runtime-model spans were 4.703/4.206/4.072/9.809 seconds. Raw process-spawn/wait and db-cleanup spans remain in `timing` and `phase-summary.json`; nested span sums are not wall totals. The receipts contain no `C1514_NATIVE` stdout join marker, so no native-ID-to-TRX per-test phase-join qualification is claimed. Full clone, filesystem, HTTP, migration and teardown subphase attribution is unknown where not emitted. Recorder health reported no dropped phases. The timeout has no test/host/body/teardown evidence.

## Hash provenance and restoration

SHA-256 unless explicitly labelled Git blob:

| Item | Hash |
|---|---|
| Original LandingGit.cs | c44a95986c7c7a9a371c9baca5faf1c9c0f28be7f5715f93acae51891d4bea7a |
| Original/restored Git blob | 5a344e9f6e259ef7fb681c89bd1051870b775232 |
| Packed LandingGit.cs | c7f9a24d5eb7a5bb75364afb5b3ed5c7b2e7b53d6fc5b7f0989a9ac5801e06a8 |
| Packed Git blob | 2dc27fd4ad5691d5e606b96d18e09b96198b8aaf |
| Complete mutants.patch | 3d4b52d8ab916d7739c5e48b6b3a8e2461e935335e46e731f65dd1306d886dbe |
| Restored LandingGit.cs | c44a95986c7c7a9a371c9baca5faf1c9c0f28be7f5715f93acae51891d4bea7a |
| Unchanged witness source | 3bc8fd106260548066c64700497e34aabe1703a30e5d992a7891c2f7b79b3740 |
| Packed server DLL, producer and staged | c01166a5cba9b1b4045a3a26a5097038e80894a29287130d1ddfd8274640dfa5 |
| Packed test DLL | 105e3faf602acced44f7328630a63c7cd768aad92cb58612f4e0b7cf79b071f8 |
| Restored server DLL, producer and staged | d8d1aa8a4bb587e3bcef12fea72f0e7953f9879a5092fd5b8a082bff648ba5b5 |
| Restored test DLL | 47e7f236a640c9f8e7ae2ebd7ca2493d3c1fdd0b2f29031943ad14bdbbc9b06b |

After both red processes completed, restoration used exactly `git checkout -- server/Infrastructure/Git/LandingGit.cs`. Restored bytes equalled both the original bytes and the base Git blob; the temporary helper/selector was absent. The tree/index were clean before the restored build. That fresh build passed once. Restored producer/staged hashes agreed; both DLLs lacked the temporary environment-name and helper-type markers. The admission-only failure changed neither source nor DLL hashes. These are ordinary-worktree records, not formal SourceLanding custody/restoration receipts.

All driver processes were foreground, local inherited children and awaited. After the admission timeout, no further build/test was requested. All 72 exact task-created native bin/obj roots (4537 inventoried entries; none existed initially) were removed using individual literal absolute paths in `cleanup.ps1`; final enumeration found zero such roots. Source remained restored. Only this Markdown report is committed; no mutation, test change, raw log, JSON or TRX is committed.

External evidence: `/work/evidence/task-8b3bd292`. It contains `identity.json`, `scope.json`, original/packed source copies, `mutants.patch`, source/patch hashes, archived `run.cjs` and `summarize.ps1`, each invocation receipt/log, four fresh TRX files and summaries, opt-in timing JSONL and phase summaries, `restoration.json`, `restored-marker-check.json`, `owned-output-inventory.json`, `cleanup.ps1` and `cleanup.log`. The incomplete attempt's directory has its admission log/receipt and no TRX. Historical failures remain preserved.

## Required continuation

The caller must authorize **one fresh restored-source build plus the two missing exact green methods**, since validated outputs were removed and the reopened no-rebuild constraint currently blocks execution. Alternatively, provide the complete validated runnable outputs. Each green method then needs its own lease and fresh TRX. Preserve these two baseline passes and two intended reds; do not repeat them merely to hide the timeout. The initial attempt did not retry because its brief explicitly required stopping on exit 4.

The broader B-switch ledger still contains **23 unselected controls: B1–B16 and B19–B25**. They are continuation inventory, not executed selections. Preserve their investigation classifications, including masked/missing boundary witnesses; no test repair or equivalence claim is inferred here. No overall sweep or SourceLanding completion is claimed.

--- next stage ---
next: mutation
handoff: Reopening is blocked by absent restored outputs and an explicit no-rebuild constraint. Authorize one restored-source build, then the two missing exact green methods; retain both valid reds and the separate 23-control B-switch continuation ledger.
artifact: /work/worktrees/task-8b3bd292/docs/investigations/2026-11-06-mutation-s4a-B-switch-8b3bd292.md
