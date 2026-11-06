# S4a C-salvage mutation wave 2 — task 4965fc83

**Complete for this two-control slice: S1 and S11-prunesym were killed at the intended preservation assertions, with both restored-source greens passing 1/1.** Six executed results: four passes, two expected mutation failures, zero skips. Both builds passed. No unrelated failure, timeout, survivor, equivalence or second-guard-masked result occurred among these two selections. This is waived ordinary-worktree evidence, not CARD-0478 SourceLanding qualification or overall-sweep completion.

## Identity and authority

Tested base: `10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9`; assigned branch `feat/card-task-4965fc83`; Linux/server2 worktree `/work/worktrees/task-4965fc83`. Initial tracked tree/index were clean. The caller's existing authority covered this execution and task-branch publication; no additional approval was requested. No product/test repair, desktop execution, landing, activation, rc push or land-graph update occurred. Only this report is committed; temporary mutations are external evidence and were never committed, as explicitly commissioned.

Read in full: Investigate ce37c38f's sweep report, C-salvage wave 1 report 0b3f9d62, Custom 6569dd36 Goal/Result/report, and the cited f822e84d and 935ca9be mutation evidence. Source/test bindings were inspected directly. `git diff fed5899951daa3c66be3dee31355e6261286dc01 HEAD --` for WorktreeReflogSalvage.cs, C1708ReflogSalvageTests.cs, GuardedWorktreeRemoval.cs and DesktopLegacyReleasePolicyService.cs was empty. Wave 1's settled S6-oldids and S12-dangling were not repeated.

Raw evidence: `/work/evidence/task-4965fc83`. It retains original/packed/restored source copies, full patch, manifest, runner and parser scripts, invocation/exit receipts, logs, fresh TRX/HTML, timing JSONL, parsed results, summaries, audit and restoration/marker checks. Test fixture: shared PostgreSQL container per process, isolated databases, real scratch Git repositories/local bare origins, unchanged assembly-local GitProcessSpawnLimit. Every executed method records the production-runner guard at http://127.0.0.1:1. No Pty test assembly was co-scheduled by this task.

## Selected controls and activation

S1 fences loss of locally rewritten, never-pushed commits when the worktree and branch reflogs disappear. S11 fences deletion of an unrelated precious branch through a symbolic salvage ref during pruning. These directly protect commits/branch data, have existing witnesses and are not listed as masked/missing. They use distinct methods and were never simultaneously active; neither changes a switch/age/completion/collision fence in the other control's process.

| Selector | Temporary change | Exact filter | Required red |
|---|---|---|---|
| S1 | SalvageAsync returns success before reading/writing salvage refs | `/*/*/C1708ReflogSalvageTests*/Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref` | rewritten commit's salvage ref missing after retirement |
| S11-prunesym | PruneAsync admits symbolic lines (third field) and omits --no-deref from update-ref -d; name/OID, age, completion, lease and live gates stay | `/*/*/C1708ReflogSalvageTests*/Prune_never_deletes_through_a_symbolic_salvage_ref` | refs/heads/precious missing rather than its original OID |

One process variable, `ANTIPHON_MUTATION_4965FC83`, selects exactly S1, S11-prunesym, or empty/off. Exact equality, no test-name/reflection mapping. A module initializer refuses other IDs before tests. The external driver separately rejects unknown IDs before a PowerShell/test launch or lease request; the INVALID check exited 1 and created no run directory. Compiled marker checks confirm both selectors/helper in the packed server DLL and their absence in the restored producer/staged copies. Baselines are selector-off controls on the packed build, not a pristine-source build. Each filter executed exactly the named parameterless method, args=[], one result per invocation.

S1 intended red, driver exit 2 / ShouldAssertException:

`await SalvageRefsAsync(w)` should be `["refs/antiphon/salvage/2007da42-f007-4e14-95c3-4b8c9090565f/274f78161f23fea8f2523ba1f298d7a56ba66f70"]`, but was `[]`: “one salvage ref, for the rewritten commit only (the pushed control has none)”. Both task retirements were asserted before this check. Later reachability assertions were not reached and are not claimed executed.

S11 intended red, driver exit 2 / ShouldAssertException:

`(await w.ReadAsync(w.Fixture.Repository, "rev-parse", "--verify", "refs/heads/precious")).Trim()` should be `"8bc1629ba7318ef02968041afa755bf7dc5ac9e7"`, but was `""`. This is the expected precious-branch loss, not an incidental refusal reason or fixture error. The later symbolic-target assertion was not reached and is not claimed executed. Both baseline and restored green verify the branch OID and symbolic target.

## Source and binary hashes

Only temporarily edited path: `server/Infrastructure/Git/WorktreeReflogSalvage.cs`. Witness `tests/Antiphon.Tests/Application/C1708ReflogSalvageTests.cs` stayed unchanged, SHA-256 `ce74bed2fbf161e173121e8c30ebdc5e090c8ba405711de9d3afed93a6d2f4aa`.

| SHA-256 identity | Value |
|---|---|
| Original / base Git blob / restored source (byte-equal) | `e3064c150e1c863107ea10acbe7d8052915c84d3a177e7c27cb740a8705b1a22` |
| Packed source | `6567e019605d021c9e9fc6f70c207e433fd8eaca5063672340f6001ebff2d8aa` |
| mutants.patch | `7e2ca0e044d55e744d476d8308d1b056ee924dbe19d0a468dfc4a12ddf92295a` |
| Packed server DLL, producer and staged | `475b456b05314ce60a4baf71b997f7e766e415c158ebd72876e1c226efda9daa` |
| Packed Antiphon.Tests.dll | `9ded9912c4c805c8748e330a384b8a7b0abaf8a2b3d56275ac107d0eed4b83b4` |
| Restored server DLL, producer and staged | `1d0e434c3ca9d4c06cc609df73664035c5b3a07503144cf290f6007cdcea3887` |
| Restored Antiphon.Tests.dll | `c52e4740e667ca521f44fcd603a78b987f22bb9df84872e535cc446dc14744d4` |

Producer: `server/bin/Debug/net9.0/Antiphon.Server.dll`; staged: `tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Server.dll`. Per-driver receipts record all source and before/after DLL hashes. The final audit requires equality to the applicable frozen build, exact roster/counts and genuine grant for each driver. No source edit occurred while a build or driver was running.

## Admission and execution

All commands were supervised local inherited children, foreground and joined. There were exactly eight real build-slot grants: one per build and one per exact-method driver. No unleased/unlimited/skipped admission, retry or timeout occurred. Each grant capped builds at maxcpucount=6. Properties on both builds and every no-build run: `UseAppHost=false`, `TEMP=/tmp`, native default outputs, no OutputPath/-o/--output. Admission bounds: build 10 minutes, method 5 minutes. Execution caps start at observed grant: build 20 minutes, method driver 3 minutes; test attributes unchanged. These explicit commissioned limits remain provisional, not measured p95s.

Literal argv is retained in each invocation.json. Build command through its own lease: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label mutation-4965fc83-<label> -SlotWaitMinutes 10 -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo`. Test command through its own lease uses -SlotWaitMinutes 5 then `dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter <exact filter above> --report-trx --report-trx-filename run.trx --results-directory <fresh external run directory>`. Selector/phase environment is archived by the driver and per-run receipt.

| Operation | Exit | P/F/S | Broker wait s | Observed admission s | Execution envelope s | Wall s |
|---|---:|---|---:|---:|---:|---:|
| packed-build | 0 | build | 0 | 0.918 | 660.817 | 661.734 |
| baseline-S1 | 0 | 1/0/0 | 16 | 17.197 | 35.437 | 52.633 |
| baseline-S11 | 0 | 1/0/0 | 75 | 78.744 | 28.352 | 107.096 |
| red-S1 | 2 | 0/1/0 | 30 | 31.939 | 28.951 | 60.890 |
| red-S11 | 2 | 0/1/0 | 15 | 17.785 | 44.834 | 62.619 |
| restored-build | 0 | build | 15 | 18.486 | 119.424 | 137.910 |
| green-S1 | 0 | 1/0/0 | 0 | 2.494 | 32.288 | 34.782 |
| green-S11 | 0 | 1/0/0 | 0 | 1.222 | 26.955 | 28.177 |

Packed build: 0 errors / 1769 warnings, MSBuild 00:10:59.91. Restored build: 0 errors / 1746 warnings, MSBuild 00:01:58.81. Warnings are retained in logs and not counted as mutation results. Aggregate driver wall 1145.841 s; admission 168.785 s; execution envelopes 977.058 s. Work began at about 10:56 UTC, final green joined at 11:19:18 UTC: all restoration/build/green work completed inside 24 minutes and the 90-minute outer budget. No build, selection or complete local execution slice exceeded 30 minutes. Preparation/reporting are outside the summed driver wall. The full build/admission history is preserved, with no green rerun erasing a prior red or cost.

| Run | Fresh TRX run ID | Execution ID | TRX test duration |
|---|---|---|---|
| baseline-S1 | ae172b17-85f1-47a4-9337-b8bb6b90980e | 743c13af-07a1-4e78-b98b-11d39ddeaaaa | 00:00:11.6037983 |
| baseline-S11 | 1282f119-0359-40e1-b547-fa1c9d9a066d | 3fc37643-1192-425e-8d19-8dfcec0e68fa | 00:00:06.7611540 |
| red-S1 | 3e3aace5-4df8-46b5-8d1c-c9ecacc24428 | 9512fd2d-ed3f-497b-9722-6fad7164a065 | 00:00:08.4546047 |
| red-S11 | 5d0947c5-cc8c-4a99-84df-4a7ab2894344 | 6f2bfccf-c505-4c0f-8685-eaaa79d46dcf | 00:00:05.9362248 |
| green-S1 | be25bb5c-c212-4e04-b1d4-3fa0f3fb119e | 4718d0f0-7fa4-4abb-aa66-05f8cb96f0a6 | 00:00:07.6107618 |
| green-S11 | 6f399432-e7cf-4de0-822b-19a479a407b9 | b10351c9-a7ab-4dd4-ba65-0bc97e2e7498 | 00:00:04.8136563 |

TRX path: `/work/evidence/task-4965fc83/<Run>/run.trx`. Stable test IDs: S1 `76f02cdb-2737-0964-13eb-0cca448bb8c3`; S11 `5e397ccb-282f-e0b1-3ae9-5c907c123c3f`. Six distinct run IDs and execution IDs, no reused results. Full test names, timestamps, assertion stacks and native timing IDs remain in parsed.json/TRX/audit.json.

## Phase timing

ANTIPHON_TEST_PHASE_ROOT and per-run/group/attempt/suite identity were set for every test driver. Timing JSONL and summaries retain the complete parameter roster and shared fixture records. Host intervals below derive from TRX Times plus result start/end; startup combines discovery/shared setup, method interval includes its own setup/disposal, and teardown is post-result host time.

| Run | Host wall s | Startup/discovery/shared setup s | Result interval s | Post-result teardown s | Recorded process wait s | Recorded DB cleanup s | Method unattributed remainder s |
|---|---:|---:|---:|---:|---:|---:|---:|
| baseline-S1 | 31.650 | 13.609 | 11.604 | 6.437 | 2.097 | 0.050 | 9.484 |
| baseline-S11 | 25.310 | 17.226 | 6.761 | 1.323 | 0.609 | 0.058 | 6.120 |
| red-S1 | 25.901 | 14.383 | 8.455 | 3.063 | 1.990 | 0.041 | 6.441 |
| red-S11 | 37.445 | 26.757 | 5.936 | 4.752 | 0.688 | 0.033 | 5.232 |
| green-S1 | 29.010 | 14.927 | 7.611 | 6.472 | 0.944 | 0.031 | 6.653 |
| green-S11 | 22.086 | 13.458 | 4.813 | 3.815 | 0.477 | 0.035 | 4.326 |

Shared DB creation and runtime-model/fixture setup are recorded separately in each timing summary. Docker creation/readiness and schema-cache timings remain in TRX output; all six reached readiness. Process-spawn spans nest within process-wait; do not add them as separate elapsed time. Shared fixture spans can overlap the method interval and are not additive host phases. Uninstrumented migration, filesystem/IO, HTTP, other external-call and individual teardown splits remain unknown, not zero; recorder zero totals do not prove those calls cost nothing. Native test IDs bind timing rows to their TRX StdOut. These six samples do not establish host p95.

## Restoration, retained outputs and reproduction

After both reds joined: `git checkout -- server/Infrastructure/Git/WorktreeReflogSalvage.cs`. Restored bytes match the original and pinned base Git blob; Git refreshed the timestamp. The selector was removed before exactly one restored build. Both same exact methods then passed against those fresh restored assemblies, with selector explicitly empty/off. Final product/test source diff is empty. Native ignored bin/obj outputs retain the restored assemblies under CARD-1629; no mutant-built producer/staged server or test DLL remains. No deletion cleanup was required or performed. Raw generated evidence remains external and is not committed.

To reproduce in a separately authorized ordinary worktree at the tested base: apply `/work/evidence/task-4965fc83/mutants.patch`, copy run.cjs to a new evidence root (change its fixed root; existing directories refuse reuse), build once, and execute the archived baseline/red invocation commands with their selectors and exact methods. Restore the literal source path, hash-check against the base, rebuild once and run the two green commands. Keep the individual leases, bounds and frozen source. This report alone supplies no formal SourceLanding binding/receipt.

## Continuation ledger

| Control | State after this slice / required next binding |
|---|---|
| S1 | Settled here: missing salvage ref red, restored green 1/1 |
| S2 | Unrun: salvage failure must preserve tree snapshot, registration and branch |
| S3 | Unrun: late disable at salvage write must write no ref and preserve tree; keep isolated from global child-start mutation |
| S4 | Unrun: young salvage ref remains; never activate with completion-lifetime mutant |
| S5 | Unrun: dry-run prune writes nothing |
| S6-oldids | Settled in wave 1, not repeated |
| S7-lease | Unrun: held repository lease prevents prune |
| S8-completion | Unrun: both Aged_retry_keeps_its_salvage_ref_for_a_retention_after_completion and Unfinished_release_keeps_its_salvage_ref_past_the_retention required |
| S9-prunegate | Unrun: pre-delete live/ambient child-start gate; every salvage ref remains and boundary fires once; isolate from global gate controls |
| S10-collision | Unrun: direct collision must be isolated first, Direct_salvage_collision_refuses_and_keeps_the_existing_ref. The older symbolic collision revert is known masked by the explicit symbolic-ref probe; Symbolic_salvage_collision_refuses_and_never_moves_another_branch remains a required additional binding only with reachability honestly classified. Neither known-masked symbolic control nor missing witness was run here; never combine S10 with S12 |
| S11-prunesym | Settled here: precious branch loss red, restored green 1/1 |
| S12-dangling | Settled in wave 1 including authorized restored-green continuation, not repeated |

Eight selectors remain (S2–S5, S7–S10), with S8/S10 additional method obligations preserved. U1–U6's missing independent source/bounds/post-write/CAS/parser/prune sub-fence witnesses remain unchanged from Investigate ce37c38f; no unsupported kill/equivalence is inferred. All historical masked, Windows-native, ordinary regression and formal SourceLanding obligations remain open according to that inventory. No new missing-witness/design decision arose for the two executed controls. Retain all four settled waived controls across waves; do not repeat completed reds.

--- next stage ---
next: mutation
handoff: Commission bounded slices for S2–S5 and S7–S10; retain wave 1 S6/S12 and this wave S1/S11 evidence, plus S8 unfinished and S10 direct/symbolic masking obligations. No overall SourceLanding completion claim.
artifact: /work/worktrees/task-4965fc83/docs/investigations/2026-11-06-mutation-s4a-wave2-C-salvage-4965fc83.md
