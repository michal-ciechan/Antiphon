# S4a wave 2 B-switch: B25 and B19

**Complete for the assigned two-control slice: B25-postunregister and B19-send are killed in this waived informational sweep.** Each has one selector-off baseline pass, one intended assertion failure, and one fresh restored-source green pass. Six executed results: **4 passed, 2 intended mutation failures, 0 skipped**. Two builds passed with zero errors. No unrelated red, timeout, missing witness, equivalence or second-guard masking occurred in these selected controls. The wider B-switch ledger has 21 controls remaining; this is not overall sweep completion or CARD-0478 SourceLanding evidence.

Custom task 0387f626, 2026-11-06, Linux server2, ordinary worktree /work/worktrees/task-0387f626, assigned branch feat/card-task-0387f626. The caller's standing authority covered this work; no further approval was requested. No product/test repair, desktop execution, landing, activation, rc push or land-graph update occurred. No SourceLanding operation identity was invented.

## Source binding and selection

Tested HEAD/base remained **10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9** throughout all eight drivers; tracked source/index were initially clean. The investigation was read in full from origin/feat/card-task-ce37c38f at 0037ffbd94ab6d42074d77b31f90015f43ea6a34, including grouping, interaction and exact-brief sections. The full B-switch wave 1 report was read at ef3c9670d74851d4401f672c5728185e9a600bc7; its archived selector/driver approach and exact command shape were reused. Its completed B17/B18 evidence, original admission timeout and absent-artifact reopening remain historical facts in that report; neither settled control was repeated here. Custom 6569dd36's full Goal/Result/report and the selected controls' cited Code evidence were read. All three source/witness paths below are byte-identical between the investigation's fed5899951daa3c66be3dee31355e6261286dc01 and this caller-specified base (git diff empty).

I selected two of the remaining guards with demonstrated boundary witnesses that directly fence local tree-byte deletion and transmission of a remote removal request. They affect different files and mechanisms. Exactly one is selected in each red process; neither bypasses the other's guard or any additional fence. Known masked/missing candidates were not run.

| Selector | Base mutation site and sole bypass | Required red |
|---|---|---|
| B25-postunregister | server/Infrastructure/Git/GuardedWorktreeRemoval.cs:266; omit only LiveAuthorizationAsync immediately after unregister and before deleting the set-aside | Directory.Exists(aside) is false: set-aside tree bytes deleted after Execute closed |
| B19-send | server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs:391; omit only beforeWrite after acquiring the send semaphore | removes is 1 rather than 0: WorkspaceRemove sent after Execute closed |

Literal exact filters (each is one unparameterized method, one discovered/executed row per invocation):

```text
/*/*/C0824DesktopLegacyReleaseSwitchTests*/Late_switch_after_unregister_preserves_set_aside
/*/*/C0824DesktopLegacyReleaseSwitchTests*/Late_switch_while_mirror_remove_waits_to_send_keeps_the_mirror
```

Only the two production paths above were temporarily edited; tests were unchanged. ANTIPHON_MUTATION_0387F626 selects by exact ordinal ID equality; absence means off. One module initializer rejects every unknown nonempty ID before tests, and the external driver also rejects unknown selectors before launching. There is no test-name/reflection mapping. The complete frozen patch, selector manifest and original/packed source copies are archived externally. Selector-off baselines used absent environment variables. Source and DLL hashes before/after every driver validate the frozen producer.

Existing TestDbFixture database isolation and assembly-local limiters remained intact. Docker 27.5.1 was available; passing fixtures exercised PostgreSQL and owned scratch Git repositories/local bare origins. The production-runner guard remained active. No Pty assembly was run. B19 uses PhoneHomeTestHost, DescribeProbe and a scripted peer holding the actual send semaphore: its claim is actual request send/not-send and stored mirror-path preservation, **not remote filesystem removal or real recipient acceptance**. B25's green also checks marker contents, branch/origin preservation and successful later retirement after the switch reopens.

## Results and fresh identities

| Control | Baseline P/F/S | Selected red P/F/S | Fresh restored green P/F/S | Verdict |
|---|---|---|---|---|
| B25-postunregister | 1/0/0 | 0/1/0 | 1/0/0 | Killed; completed cycle |
| B19-send | 1/0/0 | 0/1/0 | 1/0/0 | Killed; completed cycle |

Fresh TRX paths are /work/evidence/task-0387f626/<run>/run.trx; expanded exact rosters, errors/stacks and counts are in each trx-summary.json.

| Run | TRX TestRun ID | Execution ID |
|---|---|---|
| baseline-B25 | 33d36c35-db1b-4785-81ff-f4e546dc188e | 8013be07-78f9-46e5-8bbb-ebb9181fbf21 |
| baseline-B19 | 36da6aaf-d816-4eb0-aac0-6268b4196034 | 568b96dd-f0fd-4af0-916b-5f8d73110594 |
| red-B25 | 1503e6dc-6864-4103-a627-4da4e929429c | 691ec834-97c8-4fb3-a435-d480fc4fb7cf |
| red-B19 | 4f185499-47d3-482b-a2b2-5ef4549975b5 | 0bea19c9-f49a-4821-a36e-09324128149d |
| green-B25 | 94aa60d5-4892-4495-a850-78d6e773d79f | 5a3a8e7f-6118-4bd6-8713-36033a862f51 |
| green-B19 | 653a027f-0d71-4ee2-aacb-016da5d42cfe | d1ff5563-1461-4487-8775-e1d96c2ef6d2 |

The two exact red messages follow. Microsoft test-driver exit 2 is backed by these fresh nonzero assertion failures, not a build or fixture error.

red-B25, witness line 396:

```text
ShouldAssertException: Directory.Exists(aside)
    should be
True
    but was
False

Additional Info:
    the set-aside still holds the tree
```

red-B19, witness line 704:

```text
ShouldAssertException: removes
    should be
0
    but was
1

Additional Info:
    no WorkspaceRemove left after the switch closed
```

## Admission, execution and phases

Exactly one packed native-default-output Antiphon.Tests build and one restored-source build were run, both with UseAppHost=false and TEMP=/tmp. Each build and each no-build method driver independently took a genuine scripts/build-slot.ps1 lease; no lease wrapped a loop. Build admission limit: 10 minutes; build execution cap: 20 minutes. Method admission limit: 5 minutes; method execution cap: 3 minutes. Test attributes were unchanged. The archived Node supervisor times execution from the grant, owns and joins each local child, and rejects unleased/unlimited/skipped fallback. Outer envelopes were 31 minutes/build and 9 minutes/method. No deadline, admission timeout, bypass, retry, extra build or unlisted test selection occurred. Source was never edited during a driver.

Packed build: **1769 warnings, 0 errors**, MSBuild 6m43.62s. Restored build: **1746 warnings, 0 errors**, MSBuild 2m24.15s. Existing analyzer/package warnings are retained in raw logs and supply no mutation credit.

| Driver | Exit | Broker wait s | Observed admission s | Grant-to-exit execution s | Driver wall s |
|---|---:|---:|---:|---:|---:|
| packed-build | 0 | 0 | 1.390 | 404.860 | 406.250 |
| baseline-B25 | 0 | 210 | 211.159 | 50.977 | 262.136 |
| baseline-B19 | 0 | 60 | 61.204 | 44.753 | 105.957 |
| red-B25 | 2 | 15 | 15.693 | 26.885 | 42.578 |
| red-B19 | 2 | 0 | 1.512 | 43.191 | 44.703 |
| restored-build | 0 | 15 | 17.145 | 145.542 | 162.687 |
| green-B25 | 0 | 0 | 0.736 | 42.858 | 43.594 |
| green-B19 | 0 | 0 | 0.580 | 27.120 | 27.700 |

Eight sequential drivers sum to **1095.605s**: **309.419s observed admission** and **786.186s grant-held execution**. Build/test request window: 10:58:20.662–11:17:59.360 UTC, **1178.698s (19m38.698s)** including gaps. Source preflight to post-cleanup record: 10:58:17.460–11:18:56.022 UTC, 20m38.562s; preparation/reading/reporting lie outside those driver timings. This slice, including restoration and both fresh greens, completed inside the 90-minute outer budget. No individual selection/build or execution batch exceeded 30 minutes. These samples do not establish a new p95 or resolve the prior admission/build performance investigation 5ae98d31.

| Method phase | TRX host s | Before body s | Body s | After body s | Recorded db-create s | Recorded fixture-setup s |
|---|---:|---:|---:|---:|---:|---:|
| baseline-B25 | 44.977 | 27.215 | 10.349 | 7.413 | 2.871 | 9.613 |
| baseline-B19 | 37.565 | 26.254 | 9.760 | 1.551 | 3.355 | 10.267 |
| red-B25 | 24.023 | 12.479 | 6.935 | 4.609 | 2.013 | 4.664 |
| red-B19 | 38.233 | 26.810 | 5.836 | 5.587 | 8.341 | 5.362 |
| green-B25 | 39.024 | 19.152 | 14.324 | 5.548 | 2.377 | 10.466 |
| green-B19 | 24.176 | 12.925 | 6.811 | 4.440 | 2.192 | 4.572 |

Before/after body includes startup, discovery, fixtures, reporting and teardown; full internal attribution is unknown. Raw opt-in phase JSONL and process/db cleanup span summaries are retained per run. Nested phase sums are not additive wall time. All recorder health rows reported zero dropped phases. No C1514_NATIVE marker exists in these TRX outputs, so no native-ID-to-TRX per-test phase-join qualification is claimed. Unemitted clone/filesystem/HTTP/migration/teardown internals remain unknown, not zero.

## Hash provenance and restoration

All hashes below are SHA-256 unless labelled Git blob.

| Source path | Original = restored SHA-256 | Packed SHA-256 | Base = restored Git blob |
|---|---|---|---|
| server/Infrastructure/Git/GuardedWorktreeRemoval.cs | 9ad3fee678ee5eae31b68f58d813ab6cfa8ade4fd63f435784f585f4a239025b | 57249ce7180371652f79203d525ef9b029c82840d3c09f2fdf7261b436005f23 | 21532f34d70615a4363f1cb5ecc9bf06e3a70762 |
| server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs | e44e307a85d4fae991ff31f89fc0e126ce73aa037510da91b55f159d9e760963 | 269bcef88d181b029dc98a8b3cace17a110963c3e1de77e383fc9ca67310510c | 3086519d1e695ca8d9eecc50c4de59cef1708586 |
| tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseSwitchTests.cs | 3bc8fd106260548066c64700497e34aabe1703a30e5d992a7891c2f7b79b3740 | 3bc8fd106260548066c64700497e34aabe1703a30e5d992a7891c2f7b79b3740 | fb16c64f4fdd309005500408e3471706df461043 |

Complete mutants.patch SHA-256: **924cbacd174bf181ab18af5bb06923d857056c80350d2cb201425afe1cbc4fde**.

| Build | Producer = staged Antiphon.Server.dll | Antiphon.Tests.dll |
|---|---|---|
| packed-build | fdd31aa518c605a63f1abf74b08ce536481f5abdb88bfa51fa249ac8cfe60ff1 | 5eb9b4763b59b6874f324b7c5379026a8d5d93b95da210268d332224cb9ebe08 |
| restored-build | 4e7e3ed1ee7c99dbb458984b1c6c53e6f8dbcbe96da57db3d29dd0fc29fe038c | 9143a23e347a74830cc46db3e9e0e244809045b7af13b2f99540345bf1ff28f6 |

After both red children finished, exact restoration was:

```sh
git checkout -- server/Infrastructure/Git/GuardedWorktreeRemoval.cs server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs
```

Restored bytes matched the original bytes and caller base Git blobs, with a clean tracked tree/index before the restored build. The helper and selector were absent from restored source. Git checkout refreshed source timestamps; the fresh build produced different DLL hashes, producer/staged server hashes matched, and all checked server/test DLLs lacked both temporary markers. Both green drivers bound to that same frozen restored build. validation.json independently verifies all eight driver/source/DLL bindings, exact row names/outcomes and the intended red messages.

All commands/children were foreground, local inherited and awaited. Initially no bin/obj roots existed. After evidence validation, all **72** task-created native output roots (**5494** inventoried entries) were deleted using individually literal absolute paths in cleanup.ps1; no variables/globs/rm -rf/git clean were used for deletion. No owned output root remains. Final pre-report source was clean and restored. Raw evidence is external, so this cleanup does not remove receipts. Only this report is committed; no temporary mutation/test edit, log, JSON or TRX is committed.

Evidence root: **/work/evidence/task-0387f626**. It contains identity.json, investigation/wave1 copies, original/packed sources, selector-manifest.json, mutants.patch, packed-source.json, archived preparation/foreground-driver/summary/validation scripts, eight invocation/log receipts, six fresh TRX summaries and timing/phase summaries, restoration.json, restored-marker-check.json, validation.json, owned-output-inventory.json, cleanup.ps1/log and final-state.json. Exact rerun commands are retained verbatim in invocation.json and run.cjs; a future commissioned rerun must use a fresh evidence root and rebuild because outputs were deliberately removed. No repeat execution is needed for this settled slice.

## Continuation ledger

B17-unregisterinner and B18-childstart remain completed by wave 1; B19-send and B25-postunregister are completed here. Remaining **21** controls: B1–B16 and B20–B24. No additional control was selected, activated or given kill/equivalence credit here.

| Remaining IDs | Retained investigation classification / needed boundary |
|---|---|
| B1–B5, B9, B13, B15, B20–B22 | Existing candidate witnesses; pending separately commissioned bounded Mutation. B21 remains an inspection prediction; B22 shares B19's production guard but requires its distinct RefOnly queued-send witness. Do not give either B19 credit. |
| B6-precommand | Independent pre-command cut missing; later pre-move guard masks existing schedule |
| B7-postintent | Known survivor masked by later pre-move guard |
| B8-preinsert | Independent pre-insert cut not demonstrated; post-save rollback may mask |
| B10-postcursor | Missing flip during retry-cursor await; already-off case is insufficient |
| B11-resume | Missing late resume-boundary witness; early admission may mask |
| B12-branchouter | Ambient child-start read masks outer update-ref gate; B18's completed witness is distinct |
| B14-describe | Rebind required: later send callback can mask the post-describe read |
| B16-unregisterouter | Rebind required: inner unregister read can mask outer read |
| B23-ref-admission | Outer RefOnly admission is masked in existing late cuts by the live callback; no independent admission-only red claimed |
| B24-mirror-outer | Missing flip specifically at retention/helper boundary; later description/send callbacks protect |

These gaps are inherited inventory, not newly reproduced unmutated product failures, tested survivors or equivalent controls. Keep their second guards and schedules explicit; do not remove several fences to force red. Any missing-witness repair or design choice requires a separate caller-commissioned task. Formal SourceLanding qualification, native Windows behavior and real producer-to-recipient acceptance remain outside this waived slice.

--- next stage ---
next: mutation
handoff: B25 and B19 are complete in this waived informational sweep; retain wave 1 B17/B18 and all historical outcomes. Commission bounded slices for remaining B1–B16 and B20–B24, preserving the masked/missing boundary ledger; no overall SourceLanding completion claim.
artifact: /work/worktrees/task-0387f626/docs/investigations/2026-11-06-mutation-s4a-wave2-B-switch-0387f626.md
