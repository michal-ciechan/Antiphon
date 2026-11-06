# S4a D-policy mutation wave 2 — 6f53cb5a

**Both selected controls were killed at their intended preservation-outcome assertions; restored-source verification passed 4/4.**

2026-11-06, Linux/server2; assigned branch `feat/card-task-6f53cb5a`. Tested base **10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9**. Initial tracked/index state was clean and no native bin/obj roots existed. This is the explicitly waived ordinary-worktree informational sweep, **not CARD-0478 SourceLanding evidence**. No operation ID is applicable or invented; there is no overall SourceLanding completion claim. Standing caller authority covered execution, so no additional approval was requested.

Only T-D-4-retry and X-13b were selected. No product or test repair, desktop execution, landing, activation, rc push, land-graph update, class pack or broad test suite ran. Temporary instrumentation was removed and byte-verified before the restored build. Only this report is committed.

## Selected controls and results

These two runnable ledger entries protect dirty bytes during a reopened cleanup and a tree owned by a live event-linked session. The seven status/path survivors already have missing/masked witnesses; they were ineligible for this slice and were not run. The two chosen selectors cannot mask each other: exactly one ID is active per process, and the retry proof does not alter session classification.

| Selector | Exact method filter | Baseline P/F | Selected P/F | Restored P/F | Result |
|---|---|---:|---:|---:|---|
| T-D-4-retry | `/*/*/C0824DesktopLegacyReleasePolicyTests*/Reopened_attempt_re_proves_a_tree_dirtied_after_the_refusal` | 1/0 | 0/1 | 1/0 | Killed, specified compound retry-proof control |
| X-13b | `/*/*/C0824DesktopLegacyReleasePolicyTests*/Full_ref_dependents_and_event_linked_sessions_keep_the_tree` | 3/0 | 2/1 | 3/0 | Killed, event-linked argument only |

**12 results: 10 passed, 2 intended mutation failures, 0 skipped.** Fresh nonzero TRX results establish both reds; no timeout, fixture failure, reason-only red or unrelated failure receives kill credit. Baseline-off uses the frozen packed build; it is not presented as a pristine build.

### T-D-4-retry

The inventory explicitly requires skipping retry inspection and relevant downstream proof to avoid masking. An AsyncLocal flag is set around `_worktrees.TryRemoveAsync` only for a system retirement whose durable `attempt.AttemptNumber > 1`, and restored in finally. With this selector, nonempty status no longer causes `source_dirty` in LandingGit.InspectAsync, and LateUntrackedAsync returns null at both the pre-move and post-move calls. This is **one compound dirty-retry control**, not an independent kill of either isolated fence. It does not bypass tracked-content comparison, content hashing, identity, manifest/custody, no-follow deletion, salvage, remote checks or live authorization.

The existing witness first changes a tracked file after filing; attempt 1 still refuses source_dirty. It then undoes that edit, creates ordinary untracked `later.txt`, advances the retry clock, and retries. The selected result fails at test line 2039:

`row2.Outcome should be "Refused" but was "Removed"`.

Thus the newly dirtied reopened attempt loses the required refusal. The later byte-snapshot/two-attempt assertions do not run after this failure; the observation is the real removal's Removed outcome, not a separately executed post-failure byte census. Both baseline and restored green execute every assertion, including two attempts, byte-identical tree, registration, branch and origin-ref preservation.

### X-13b

The selector deletes only Classify's `facts.LiveSessionTasks.Contains(task.Id)` event-linked arm. The task-row AgentSessionId arm and all subsequent retirement/removal checks remain. The test's Running session has a Cwd elsewhere and links to the task solely through AgentTaskEvents.

Only `live-session-event-linked, live_session` fails at line 380:

`row.Outcome should be "Held" but was "Removed"`.

The `startref-queued-refs-heads, dependent_open` and `startref-open-refs-heads, dependent_open` sibling arguments pass. The failure precedes that row's reason/intact/branch/no-Git assertions, so those later assertions are not falsely claimed as executed in red. Baseline and restored runs execute all of them. This demonstrates deletion despite the live event owner; it does not simulate a simultaneous session write.

## Source and binary provenance

Raw persistent evidence: **/work/evidence/task-6f53cb5a**. The full ce37c38f inventory and ca9712bd wave-1 report are copied there. Inventory base fed5899951daa3c66be3dee31355e6261286dc01 was compared with this task's pinned base for every mutated path and the witness. Only LandingGit's in-process working-tree-read dispatch changed; its dirty-status contract remains at the selected site. That delta is archived as base-delta.patch. The original survivor witness commit 72b9962054452b5a431e0ab5ebf03adc8d55a9f2 (also cherry-picked as 14c6811182fa71e2c0fdae9ed445dca7a40e2b6a) was inspected. Wave-1 selector admission and exact build/run commands were reused with this task's IDs and paths.

One frozen packed source tree, one env variable `ANTIPHON_MUTATION_6F53CB5A`, allowed scalar values empty/T-D-4-retry/X-13b. Unknown IDs fail in the driver before slot/test launch and in the compiled module initializer. No class-prefix or test-name/reflection mapping exists. A driver self-check rejected INVALID before launch. Per-test TRX activation markers contain only the selected ID, with none in baseline/green. The complete patch and selector manifest remain external.

Patch SHA-256: `9d5daa9231ac2129bd8b23e269211f287de9fe02bdb563a4830b1a249c1bd547`.

| Source path | Original/base SHA-256 | Packed SHA-256 | Restored SHA-256 |
|---|---|---|---|
| `server/Application/Services/DesktopLegacyReleasePolicyService.cs` | `df86329c758fc5a653b63af2d58b009f9a5ef6232063542674935e208b12b3a3` | `9809658cf78a4a993a9a8d0fb81aea76df0755a06be3551171044a3947f4a4f9` | `df86329c758fc5a653b63af2d58b009f9a5ef6232063542674935e208b12b3a3` |
| `server/Application/Services/TaskWorktreeRetirementService.cs` | `a91f5cc81ee4a9861d0aa1910f6cf864bf50177805eeb6c200bf8bcc5488d7e9` | `6a63142c477d1c1b51cb8494127cadc429982875f4e96d32e2f13b89998f6231` | `a91f5cc81ee4a9861d0aa1910f6cf864bf50177805eeb6c200bf8bcc5488d7e9` |
| `server/Infrastructure/Git/LandingGit.cs` | `747a82240bde8f6a1aa2d73d2119b4937fc437df82c62adb5bbd1ba4d4ca15d4` | `c597e551d9d9b9b8aabd72a752c9a5113a8cc4b0130c8e8b12ed574cee87babd` | `747a82240bde8f6a1aa2d73d2119b4937fc437df82c62adb5bbd1ba4d4ca15d4` |
| `server/Infrastructure/Git/GuardedWorktreeRemoval.cs` | `9ad3fee678ee5eae31b68f58d813ab6cfa8ade4fd63f435784f585f4a239025b` | `1afd1722e9558dc6e23c9eff33c0312bd575d71ae9f65231167d2a1d6027bc61` | `9ad3fee678ee5eae31b68f58d813ab6cfa8ade4fd63f435784f585f4a239025b` |

Original Git blob IDs, with direct restored byte comparisons to git show <base>:<path>:

- `server/Application/Services/DesktopLegacyReleasePolicyService.cs`: `0d681df00e9eb32b9652665588b1eb8ec1743814`; bytes equal.
- `server/Application/Services/TaskWorktreeRetirementService.cs`: `dcd9fc868313051dfd9b412d48ad4896753c2bd6`; bytes equal.
- `server/Infrastructure/Git/LandingGit.cs`: `6a0117d001480d9b1c2bb9221936ae4dc14735fa`; bytes equal.
- `server/Infrastructure/Git/GuardedWorktreeRemoval.cs`: `21532f34d70615a4363f1cb5ecc9bf06e3a70762`; bytes equal.

Unmodified witness SHA-256: `e6e55b88a1604ced52ce198e2c0f85d5cec42d6fe57fbf490c6d799ba4647c89`.

| Artifact | Packed SHA-256 | Restored SHA-256 |
|---|---|---|
| `server/bin/Debug/net9.0/Antiphon.Server.dll` | `1d5e4fe03c218d77c1d3a765445b2fe0c514d85042703d36cc8e857daca59bd6` | `237061f9bbe9c225beb99e76dc418cd4826c6797bb4044da466151363f6c106a` |
| `tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Server.dll` | `1d5e4fe03c218d77c1d3a765445b2fe0c514d85042703d36cc8e857daca59bd6` | `237061f9bbe9c225beb99e76dc418cd4826c6797bb4044da466151363f6c106a` |
| `tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll` | `7bc009c4ceccf8cd6d53141673a1d2663c51bed485ffa200f4a2f9b04a17abe7` | `1a0c5338affd528b1ff7851934a6a9e0b2afed225e4ccadd1056d107c79e06f0` |

Each method invocation records producer/staged/test DLL hashes before and after execution. No drift occurred and server copies match. Packed binaries contained the helper/environment marker; the fresh restored server DLL contains neither, as recorded externally.

## Executions, admission and cost

Exactly two builds and six exact-method drivers acquired eight separate genuine host leases through scripts/build-slot.ps1. No unleased/unlimited fallback or admission retry occurred. Each native-default-output build used UseAppHost=false, TEMP=/tmp, SlotWaitMinutes=10 and a 20-minute post-grant execution bound. Each no-build method driver used the same property/environment, SlotWaitMinutes=5 and a 3-minute post-grant bound. No test attribute was changed. All local inherited children remained owned, foreground-supervised and joined before source changes.

| Operation | Exit | Executed/P/F | Admission envelope s | Post-grant execution s | Wall s |
|---|---:|---:|---:|---:|---:|
| build-packed | 0 | build | 361.303 | 409.378 | 770.681 |
| baseline-T-D-4-retry | 0 | 1/1/0 | 0.889 | 39.660 | 40.549 |
| baseline-X-13b | 0 | 3/3/0 | 0.565 | 40.328 | 40.893 |
| red-T-D-4-retry | 2 | 1/0/1 | 31.697 | 43.933 | 75.630 |
| red-X-13b | 2 | 3/2/1 | 0.619 | 40.551 | 41.170 |
| build-restored | 0 | build | 0.887 | 116.468 | 117.355 |
| green-T-D-4-retry | 0 | 1/1/0 | 1.552 | 39.842 | 41.394 |
| green-X-13b | 0 | 3/3/0 | 0.784 | 45.249 | 46.033 |

build-packed: 0 errors, 1769 warnings, MSBuild 00:06:48.25.

build-restored: 0 errors, 1746 warnings, MSBuild 00:01:55.45.

Summed driver admission 398.296s; execution 775.409s; wall 1173.705s. These are driver sums, not the whole task elapsed. First build began 2026-11-06T10:59:06.416Z; final green ended 2026-11-06T11:23:10.326Z. Preparation, restoration, report and cleanup stayed within the 90-minute outer budget. No individual build/selection reached 30 minutes; no bound was hit. Admission envelope includes wrapper startup/response overhead. Build/host/fixture/body/teardown subdivisions are **unknown** where not separately recorded; TRX per-result durations below are not relabelled body-only measurements.

| Fresh TRX relative to evidence root | TestRun ID |
|---|---|
| `baseline-T-D-4-retry/run.trx` | `06532c12-dd35-4094-8ca0-d4e0e14bb406` |
| `baseline-X-13b/run.trx` | `b99e6357-fbff-4bbd-8674-9dffa319024d` |
| `red-T-D-4-retry/run.trx` | `86644c6b-1910-4671-ba9b-a509d5010ed6` |
| `red-X-13b/run.trx` | `7e9992e5-7289-4889-9d4c-2f6f7e6fefb2` |
| `green-T-D-4-retry/run.trx` | `df8092b4-3399-4a40-baee-37d36d9c3cb5` |
| `green-X-13b/run.trx` | `70372fac-41f3-476d-9886-0f808e667e0c` |

All six IDs are unique. Full testId/executionId/message/stack fields are retained in summary.json; argument rosters match in baseline/red/restored.

| Argument | Baseline duration | Selected duration | Restored duration |
|---|---|---|---|
| Reopened_attempt_re_proves_a_tree_dirtied_after_the_refusal | 00:00:14.4473127 | 00:00:05.2971238 | 00:00:08.6633963 |
| Full_ref_dependents_and_event_linked_sessions_keep_the_tree(live-session-event-linked, live_session) | 00:00:12.8599225 | 00:00:07.8717796 | 00:00:05.7683575 |
| Full_ref_dependents_and_event_linked_sessions_keep_the_tree(startref-open-refs-heads, dependent_open) | 00:00:12.8208518 | 00:00:07.6451750 | 00:00:05.7683616 |
| Full_ref_dependents_and_event_linked_sessions_keep_the_tree(startref-queued-refs-heads, dependent_open) | 00:00:12.9129665 | 00:00:07.6477614 | 00:00:05.7925533 |

## Restoration, cleanup and continuation

Restored all four literal paths with git checkout --, compared restored bytes with the pinned base Git blobs and original SHA-256, and refreshed timestamps before the single restored build. Restoring the policy file removed the embedded helper. restoration.json is a local byte-check record, not SourceLanding custody evidence. All 72 inventoried task-owned native bin/obj roots were deleted with inspected literal absolute Remove-Item commands; cleanup-result.json confirms zero remaining roots and clean tracked status before the report. Cleanup inventory/commands and result remain external. No glob, variable deletion, git clean, volume or foreign-path cleanup was used.

To reproduce in a separately authorized ordinary worktree at the tested base, archive to a fresh external evidence root, update run.cjs's root literal and apply mutants.patch. Execute node <fresh-root>/run.cjs packed, baseline T-D-4-retry, baseline X-13b, red T-D-4-retry, red X-13b. Restore the four exact paths and verify base bytes, then run restored, green T-D-4-retry, green X-13b. The invocation receipts retain all exact commands/filters. Never overwrite these receipts or retry a slot timeout.

Fourteen unselected D-policy bindings remain:

| Binding | Continuation status; no run in this slice |
|---|---|
| PC-11 | Exact 48-hour boundary witness exists; mutation pending |
| PC-21e | Full-ref dependent witness exists; mutation pending (passing sibling rows here do not discharge its mutant) |
| X-11 | Normalized outside/nested-root witness exists; mutation pending |
| PC-52 | Caller-filed spent-command witness exists; mutation pending |
| PC-58 | Backed-off cap witness exists; mutation pending |
| M-5 | Least-recent retry-order witness exists; mutation pending |
| X-18 | Unreadable retention settings helper witness exists; mutation pending |
| hold-english-phrase | Missing non-English/different-warning witness through RunAsync |
| retry-skip-recheck | Needs rebind to current inspection and reopened exit-zero stderr witness |
| length-guard-on-linux | Injected limit cannot discriminate; real System Linux >=260-path witness missing |
| refuse-after-slot | Command-slot-not-spent assertion missing |
| follow-links | Earlier ignored_reparse_point masks it; independent witness missing |
| skip-unreadable | Earlier stderr hold masks it; walker-only unreadable witness missing |
| wse-default-true | Relevant production/doubles explicitly set flag; default-initialized trusted-path witness missing |

Wave 1 M-2 remains killed; X-26 remains second-guard-masked/diagnostic-only and its caller design choice remains open. Neither was repeated. No equivalence, missing-witness completion or extra kill is inferred from this slice. Other groups, native Windows controls, historical reuse conditions and formal SourceLanding obligations remain in the ce37c38f ledger.

D-2 attribution: no unmutated product regression or product/test repair is introduced or claimed. Required verification for these two waived controls is complete; broader formal qualification remains incomplete. Historical failures/costs are preserved.

--- next stage ---
next: mutation
handoff: Continue the seven remaining runnable D-policy bindings in bounded slices; retain the seven missing/masked status/path bindings and the prior X-26 design choice for caller triage. Do not repeat these two kills.
artifact: /work/worktrees/task-6f53cb5a/docs/investigations/2026-11-06-mutation-s4a-wave2-D-policy-6f53cb5a.md
