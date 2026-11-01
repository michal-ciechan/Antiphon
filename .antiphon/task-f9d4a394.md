# CARD-1545 combined fix

Outcome: D-1 through D-7 are committed and pushed on `feat/card-task-f9d4a394`. Required checkpoint rows, the coverage bundle, the evidence diff, and the merge onto `origin/master` are green on code tip `ae885bfe938e2cec94f674748122a0863b7f4a64`. The land-graph `-Check` refused on the unchanged digest, which is the expected result, and the evaluator approved. `-Update` was not run.

This file is the only change after that code tip. Checkpoint receipts below were recorded against `ae885bfe938e2cec94f674748122a0863b7f4a64`.

## Branch

- Base: `0b903dd9df01204504632880eafc2dfd51191546` (reviewer literal Compile items kept; no further csproj edit).
- Code tip: `ae885bfe938e2cec94f674748122a0863b7f4a64`.
- `origin/master`: `5aea2708fca4e196ae1915b6101c5287cb8c7cea` (fetched again before this report; ancestor of HEAD).
- Push ref: `refs/heads/feat/card-task-f9d4a394` only. No rebase, amend, reset, force-push, tag, or publish. `feat/card-task-e38d6427-combined` and `/work/worktrees/task-e38d6427-*` were not touched.

Commits, newest code first:

| SHA | Defect | Subject |
|---|---|---|
| `ae885bfe938e2cec94f674748122a0863b7f4a64` | D-3 | fix: reseed the live-lane census on the combined tree |
| `2e5b78808e2a027bc3bddc00c43d0d9951f781d3` | D-7 | docs: correct CP-23 and CP-24 checkpoint counts |
| `2ea7f38e04ef34de93eb349c5de2800dca2d2a46` | D-6 | fix: keep the live-lane config-dir test off the parallel scheduler |
| `3bb48f63008d49b43a454420506c5dde4d24d1e9` | D-5 | fix: refuse long base64url runs that are not closed ids |
| `f827813070b848d377404b99e9ef177863d4a05b` | D-4 | fix: count live-lane exception, assert, trace, path and helper sinks |
| `531e021d48c7c8bc104e34f63521cb8b86d1659d` | D-2 | fix: project live-lane setup skips through the sanitizer |
| `0e11461f549b0e0d9e526f14e0e93bdc5d97c868` | D-1 | test: compare LiveClaude compile items with the directory |

Order under master `5aea2708fca4e196ae1915b6101c5287cb8c7cea`: S1c chain `a048b928d`, sanitization `2ef25eaf8`, S1a `9261c368b`, reviewer Compile items `0b903dd9d`, then the seven commits above.

## Defects

D-1. `PlanCoverageBindingTests.linked_shared_sources_are_literal_compile_items` compares every top-level `.cs` under `tests/Shared/LiveClaude` with the literal Compile items in both linking csproj files, and every literal LiveClaude item must exist. Glob-shape assertions remain. The reviewer commit that lists `LiveClaudeEvidenceSanitizer.cs` and `LiveClaudeSinkAudit.cs` stays.

D-2. Startup canary sites and the menu canary route setup skips through `LiveClaudeEvidence.SetupSkip(setup)`. `LiveClaudeProfileAudit.IsSetupSkip` and `RequireSetupFilter` match that projection. The audit was not weakened.

D-3. Census re-seed, done by this commissioned CARD-1545 combined-fix author. The runtime does not select `HEAD`, `HEAD~1`, or `origin/master`. Seed parent, fixture `producing.sourceSha`, and `$script:LiveLanePreS1BaseSha` are `2e5b78808e2a027bc3bddc00c43d0d9951f781d3`. The seed commit touches only `tests/fixtures/live-lanes/full-discovery-before.json` (21518 nodes, no BOM, 12170366 bytes), `scripts/lib/live-lane-manifest.ps1` (pre SHA only), and `tests/live-lanes.json` (`additiveContract` emptied because the `EveryLiveGateHasDisposition` uid is already in the fixture). `pre..seed` is only that commit. Discovery used for the fixture: Antiphon.Tests `bin-d6` hash `f5d0acada8fd15717b202078ddc7705f4d64c9e2b8533e78ef6955b39f1a476a` (20717 nodes) and Pty `bin-d4` hash `ff46b6829b720729927186129c67be9cbe832d957bb890d54cec579ed6ca8167` (801 nodes). `WriteFixture -ExpectedSourceSha` was the parent, not HEAD.

D-4. Syntax-aware sink audit covers exception constructors and throw expressions, `Skip.Test`, `Assert.Fail` / `Assert.*` messages, `Trace`/`Debug.Write*`, file-name construction, same-file helpers, and the C680 path.

D-5. Long-run refusal includes the base64url alphabet. Closed id shape still bounds run, case, and field ids.

D-6. `DedicatedConfigOwnsTrustAndEvidence` carries `[NotInParallel]` and a reflection witness.

D-7. Importable `### Checkpoints` table (plan line 521, not the lane-only table at line 513) and the s1c design: CP-23 Min 4; CP-24 is the isolation-wrapper gate, Min 4, plus CP-24-PARITY Min 1. Import of the plan produced 25 rows. Untouched on purpose: CP-6 "exactly 3 methods", CP-17 "all 3 methods", s1c "exactly 3 passed", CP-14 "all 3 methods", s1c CP-3 "exactly 5 passed".

## Checkpoints on ae885bfe

All of these receipts name `source: ae885bfe938e2cec94f674748122a0863b7f4a64 state=clean` and `verdict: GREEN exit=0`. CP-20, CP-29, and CP-30 are green on that same tree.

| Run | Row | Result | Wall |
|---|---|---|---|
| `20261031-231136-0dff` | CP-25 | 1 executed, 1 passed, 0 failed. buildSource=verified | 3m02s |
| `20261031-231448-8d17` | CP-1 | 2 executed, 2 passed, 0 failed. buildSource=verified. LiveLaneInventoryTests 150s | 10m28s |
| `20261031-232524-dbab` | CP-30 | command exit 0. buildSource=notApplicable. FullCensusUnchanged | 1m33s |
| `20261031-232742-a604` | CP-16 | 6/6. slotWait 75s, build 316s, testsWall 42.2s | group 22m38s |
| same | CP-17 | 3/3. build 52s, testsWall 0.96s | |
| same | CP-23 | 4/4. build 273s, testsWall 1.22s | |
| same | CP-3 | 5/5. build 61s, testsWall 0.80s | |
| same | CP-19 | 4/4. build 64s, testsWall 0.67s | |
| `20261031-235045-1114` | CP-18 | 6/6. buildSource=verified. build 44s | 1m23s |
| `20261031-235215-fcf4` | CP-24 | 4/4. build 387s | group 7m12s |
| same | CP-24-PARITY | 1/1 `StartupArgvMatchesProductionApplyOff`. build reused | |
| `20261031-235947-0ac9` | CP-20 | 2/2. build 86s, testsWall 12.3s | group 12m06s |
| same | CP-26 | 2/2. build 325s, testsWall 159s. SLOW CLASS LiveLaneEvidenceTests 158s | |
| `20261101-001201-c357` | CP-29 | command exit 0. buildSource=notApplicable. CompletedRows | 0m04s |

CP-18 then CP-24 and CP-24-PARITY ran through the isolation wrapper (context was `/tmp/c1545-portable-QyTxWJ`, since removed). Evidence rows CP-20, CP-26, and CP-29 ran through the evidence wrapper. Both wrappers exited 0.

Coverage bundle on the evidence c680 DLL, `--no-build`, filter `/*/*/(PlanCoverageBindingTests*)|(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)/*`: 15 succeeded, 0 failed, 3m 21s, exit 0. That is PlanCoverageBindingTests (7), RcRosterRepairTests (3), and C1514LandingSplitBudgetTests (5: C1404, C1577, C1584, C1410, G2865).

## Mutants

Each row below was shown red, then the edit was restored, then the same method was green on the already-built HEAD dll. No mutant remains in the tree. `git status --short` was empty after the last restore and is empty aside from this report.

Source-read audits (no rebuild; the audit reads the worktree):

- X6. Inserted `var exe = "claude"; await runner.StartAsync(exe, launch.Args);` before `LaunchUntilReadyAsync` in `RunStartupControlAsync`. Red: `C1545 audit/Control_auto_connects_a_bridge_session_before_the_first_user required consumed launch delegation`. Green: `ClSessionUsesAttestedExecutable` 1/1.
- S4. Control catch changed to `when ((LiveClaudeOutcome.SetupStatus(e.Reason) ?? "ReadyTimeout") is string setup)`. Red: `C1545 profile-audit/Control_auto_connects_a_bridge_session_before_the_first_user-filter`. Green: `S1cOutcomeFeedsS1bClassifier` 1/1.

Compiled, rebuilt into a throwaway OutputPath, then green on the HEAD dll:

- N5. Uncommitted insert of a raw `Console.WriteLine(Environment.GetFolderPath(...UserProfile))` above committed N6 in `ProveCallerMutants`. Red by throw-on-miss: `CanarySinksCannotBypassSanitizer` 1 succeeded (the raw-console insert raised). Green after restore on the HEAD pty dll, 1/1.
- X2. `HasBareLaunch` returned false immediately. Red: `C1545 V-1/bare-launch lambda must fail`. Green: `ClSessionUsesAttestedExecutable` 1/1.
- X4. `DelegatedCalls` dropped the argument check. Red: same `V-1/bare-launch lambda must fail` on the `RunStartupControlAsync(other)` synthetic. Green 1/1.
- P3. `RequireHookConfiguration` dropped `tokens[3] != source`. Red: `C1545 V-8/source-token expected typed settings-unobserved refusal` in `CwdInstructionsCannotLeak`. Green 1/1.
- S1. Removed `"premise-absent" => "StartupDefaultAbsent"` from `LiveClaudeOutcome.SetupStatus`. Red: `C1545 outcome premise-absent did not emit a setup skip`. Green: `S1cOutcomeFeedsS1bClassifier` 1/1.
- S5. Extra arm `"provider-error" => "ReadyTimeout"` before `_ => null`. Red: `C1545 outcome provider-error became 20 ReadyTimeout`. Green 1/1.
- P6. `TranscriptTailer.LocateAsync` used `ResolveProjectsRoot(null)` instead of `_projectsRoot ?? ResolveProjectsRoot(null)`. Red: `C1545 V-13/projects-root tailer bound /tmp/c1545-tailer-Wr2IUc/decoy/projects/decoy-cwd/850bbb73-a199-4f57-995e-2b594221c76f.jsonl` from `DedicatedConfigOwnsTrustAndEvidence`. Green 1/1 on the HEAD c680 dll.
- P8. Only the `StartTailerFor` argument `projectsRoot:` was set to null (other `TranscriptTailer` constructions left alone). Red: `C1545 V-13/projects-root tailer bound /tmp/c1545-tailer-B08UVf/decoy/projects/decoy-cwd/3ac85daf-9d47-4030-82a5-52b7e562d649.jsonl`. Green 1/1 on the HEAD c680 dll.

S1a script mutants, each red then restored green:

- early-final: `terminal = $false` flipped to `$true` inside `Write-LiveLaneExportFile`. ExportPending `finalized-early` / `finalized-on-failure`, exit 1. Restored: `accepted`, exit 0.
- skipped-receipt: the accepted-receipt check forced false. ProducerReceipt `census-skipped-receipt`, exit 1. Restored: `accepted`, exit 0.
- cleanup-removed: `Clear-LiveLaneOwnedScratch` body replaced with `return`. ScratchResidue failed, exit 1. Restored: `accepted`, exit 0.

In-suite sanitization mutants SM-S1, SM-S2, SM-S4, SM-S5, SM-S6, SM-C1, SM-C2, N1-N4, N6-N8, and SM-C1b live inside `ProveCallerMutants` / `LiveLaneEvidenceTests` and throw when the insert does not raise the expected sink. Green CP-20 (2/2) and CP-26 (2/2) discharge them. N5 was the extra cycle above; N6 is the exception form of `GetFolderPath`.

Not re-executed: S2, S6, and P7. The review recorded no edit recipe for them (P7 was described as server2-env dependent). This host is server2, and that still does not supply a patch. No edit was invented. In-suite positive-control rows were not armed during the green checkpoints (`C1545_EXECUTABLE_CONTROLS` / `C1545_PROFILE_CONTROLS` were unset), so those green rows do not discharge X2, X4, X6, P3, P6, P8, S1, S4, or S5. Those were discharged by the cycles above.

SM-A1, A2, A4, A5, A6, and A7b have no label table in the s1a design. CP-30 exit 0 already requires the census-body refusal witnesses inside `Invoke-LiveLaneFullCensusBody`. The missing-list names that return 1 when absent are: context-absent, seed-nonancestor, object-missing, fixture-blob, producer-provenance, prebase-drift, seed-unlisted-edit, candidate-source, candidate-build, candidate-run, pending-producer, producer-row-red, producer-row-timeout, producer-receipt-missing, producer-receipt-foreign, remove-old-uid, remove-Integration, tag-seventh, extra-expansion, unknown-new-uid (`undeclared-new-uid`), default-disposition (`nightly-disposition-changed`), reader-edit (`reader-input-changed`), c680-exclusion (`policy-hash-changed` plus `C680-required-to-excluded`), reason-edit, owner-edit. SA3b was the reviewer's exception under D-3 and is not a labeled table in this tree.

## Named, not chased

- `WrapperOnlyPaidManifest` in CP-2.
- `NativeLinuxPolicyWitnessTests` `8397984d` in Pty, SessionRunner, PtyHost, and Antiphon.Tests.
- `Custody_residue_counts_subdirectories_only`.
- Load flake `LoadTimeoutBoundWitnessTests.Checkpoint_positive_wait_accepts_a_late_death_and_rejects_a_hook_that_never_dies` (passes alone).

Native CP-4, CP-5, CP-6, CP-7, and CP-21 stay owed. No live Claude on this host. CP-27 and CP-28 are baseline captures at the pre-S1 base, not this tree.

## Land graph, evidence diff, merge

Land graph on `ae885bfe938e2cec94f674748122a0863b7f4a64` (no csproj edit, so the digest matches the value at `0b903dd9d`):

- Digest `1649974D464009999E5047B1F1DFA1ECA33487D9C14F7F114F54529B74AF1718`, count 68.
- `scripts/audit-land-verification-graph.ps1 -Check` refused: manifest does not match the selected graph. That refusal is the expected digest result. CHECK exit 1.
- Evaluator `--repo --solution Antiphon.sln --canary-record`: `phase=pass2 approved`, exit 0.
- `-Update` was not run. The Merge records it once.

`scripts/check-evidence-diff.ps1` over `5aea2708fca4e196ae1915b6101c5287cb8c7cea..ae885bfe938e2cec94f674748122a0863b7f4a64`: commits=101, entries=20, violations=0. Not shallow.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`: tree `58f8176b5dc3a4f6f1deb1c2bd6f0eafb7867855`, exit 0, ancestor yes. No conflicts. Not merged.

## Cleanup

Alternate `bin-<name>` directories from this task, including checkpoint-tool copies under `.antiphon/checkpoints` and `.antiphon/live-lane-runs`, were removed with `rm -r` on exact absolute paths. `/tmp` scratch for this card (`c1545-*` and `antiphon-live-lane-*` entries that were present) was removed the same way. Tailer dirs `/tmp/c1545-tailer-Wr2IUc` and `/tmp/c1545-tailer-B08UVf` were already gone. No `git gc`. Reviewer worktrees were left in place. After cleanup, `find` for `bin-*` under this worktree printed nothing, and `git status --short` was empty before this report file.

## Next stage

Short Final review of D-1 through D-7 on `feat/card-task-f9d4a394`, then merge to master with the single authorised land-graph `-Update`.

--- next stage ---
next: short Final review of D-1..D-7 on feat/card-task-f9d4a394, then merge to master with the single authorised land-graph -Update
handoff: Final review
artifact: ae885bfe938e2cec94f674748122a0863b7f4a64
