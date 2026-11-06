# S4a C-salvage mutation wave 1 — task 0b3f9d62

**Complete for this two-control slice: S6-oldids and S12-dangling both have the intended red evidence and restored-source green 1/1.** The caller reopened the task to run only the missing S12 restored-green method; it passed. Aggregate: two selector-off baselines passed, two expected mutation failures, two restored greens passed; six executed results, four passed, two failed as expected, zero skipped. Both original builds passed. These are two settled waived ordinary-worktree kills, with no formal SourceLanding or overall-sweep completion claim.

## Authorized continuation, 2026-11-06

Continuation starting HEAD: `0de741e7e1703111c1725ab83ebacb59d19a4f71`; branch `feat/card-task-0b3f9d62`, worktree `/work/worktrees/task-0b3f9d62`. That commit changes only the initial report from tested base `fed5899951daa3c66be3dee31355e6261286dc01`. Source was already restored. Before this run it was rechecked byte-for-byte against the base Git blob, and all retained producer/staged/test DLL hashes matched the completed restored build. No rebuild or new red ran. The selector was explicitly empty/off.

- Exact selection: `/*/*/C1708ReflogSalvageTests*/Dangling_symbolic_salvage_collision_refuses_and_keeps_the_symbolic_ref`.
- One test lease: `6f02382e-8a4a-4f1c-a6d4-2dfd503387aa`, real BUILD SLOT grant, broker wait 0 seconds, slot held 28 seconds. No unleased run.
- Command: archived literal argv in `/work/evidence/task-0b3f9d62/green-S12-resume1/invocation.json`; native output with `dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false`, `TEMP=/tmp`, `-SlotWaitMinutes 5`; unchanged three-minute execution cap. One foreground supervised local child, joined to completion.
- Result: exit 0, executed 1 / passed 1 / failed 0 / skipped 0. Parameterless method `Dangling_symbolic_salvage_collision_refuses_and_keeps_the_symbolic_ref` (args=[]), duration `00:00:05.1185481`.
- Fresh TRX: `/work/evidence/task-0b3f9d62/green-S12-resume1/run.trx`; run ID `40a8555f-b264-42be-ad51-b05a08525ce8`, execution ID `e613e2fc-4434-4576-86ee-425b1c547310`, test ID `f87233b5-b43e-717f-4a1e-0cb46b52343f`.
- Restored source SHA-256: `e3064c150e1c863107ea10acbe7d8052915c84d3a177e7c27cb740a8705b1a22`.
- Restored server DLL SHA-256 (producer and staged copy, unchanged before/after): `e8e24db3000400822ab4060b51e21e63c4184af6af86b1a486fa6a53c1e21d2e`.
- Restored Antiphon.Tests.dll SHA-256 (unchanged before/after): `22075ace11001d10bf78c2c388e26df8ee7f3574ab89fe723f69ca1196e6a159`.

| Continuation phase | Seconds |
|---|---:|
| Observed admission, including wrapper startup | 1.297 |
| Granted execution envelope | 27.745 |
| Total driver wall | 29.042 |
| TRX host wall | 24.905 |
| Startup/discovery/shared fixture interval | 18.012 |
| Test-result interval | 5.118 |
| Post-result host teardown interval | 1.775 |
| Recorded method process wait | 0.424 |
| Recorded method DB cleanup | 0.032 |
| Method unattributed remainder | 4.684 |

Timing JSONL, parsed.json, summary.json, run.log, HTML and the immutable invocation/receipt are in the continuation run directory. Uninstrumented external-call splits remain unknown. The full five-minute admission failure from the original attempt is retained below and in its unchanged evidence directory; this green does not erase that cost. Aggregate driver wall across both attempts: 1143.240 seconds; observed admission 364.392 seconds; execution envelopes 778.848 seconds. There were nine lease requests in total: eight grants and one admission timeout, with six executed method selections and two builds. The continuation added only one exact method selection and no build.

Final product/test source is unchanged; the temporary switch remains removed and retained native outputs remain restored. Only this report is changed by the continuation. The previous report is preserved below as historical: its incomplete/NOT RUN statements describe the first attempt, not the current result.

## Initial attempt, historical report at 0de741e7e1703111c1725ab83ebacb59d19a4f71

**Incomplete: the final S12-dangling restored-green driver timed out before build-slot admission (exit 4 after five minutes).** S6-oldids completed its intended red and restored-green cycle. S12-dangling reached its intended red with observed symbolic-ref replacement, but its required restored green is NOT RUN, so that cycle remains incomplete. Five executed test results: three passed, two expected mutation failures, zero skipped. Both builds passed. No survivor, equivalence or second-guard-masked result occurred; the missing green is an admission gap, not a passing test or a finalized S12 kill.

This is the expressly waived ordinary-worktree informational sweep, following Custom 6569dd36; it is not CARD-0478 SourceLanding evidence and has no invented operation binding. The caller's existing authority covered execution and task-branch publication; no further approval was requested. No product/test repair, desktop execution, landing, activation, rc push or land-graph update occurred.

## Source and binary identity

- Tested base: `fed5899951daa3c66be3dee31355e6261286dc01`; assigned branch `feat/card-task-0b3f9d62`, worktree `/work/worktrees/task-0b3f9d62`. Initial tracked tree/index clean.
- Only temporarily edited path: `server/Infrastructure/Git/WorktreeReflogSalvage.cs`; source witness: `tests/Antiphon.Tests/Application/C1708ReflogSalvageTests.cs`, SHA-256 `ce74bed2fbf161e173121e8c30ebdc5e090c8ba405711de9d3afed93a6d2f4aa` (unchanged).
- Raw evidence: `/work/evidence/task-0b3f9d62`. Includes the full investigation, precedent report, original/packed source copies, patch, manifest, command/selector receipts, logs, fresh TRX/HTML, timing JSONL, parsed results and restoration records. Historical full precedent Goal/Result were read from `/work/evidence/task-ce37c38f/6569dd36-{brief,result}.md`.

| SHA-256 identity | Value |
|---|---|
| Original source / base Git blob / restored source (byte-equal) | `e3064c150e1c863107ea10acbe7d8052915c84d3a177e7c27cb740a8705b1a22` |
| Packed source | `bef88158cf79a9df1d6aad7e37c8bb6b4e8d991b6d0453e341b3290ca9dbf1a8` |
| Archived mutants.patch | `76d35aff634e8abf524d751dda6e04ed6a95e350f8ff29a54b3a25841d3f27bf` |
| Packed server DLL, producer and staged copy | `6f0fc0397b75b5883a9997877070c65b9f58705f3bc798fbb1b997ab36a0cb08` |
| Packed Antiphon.Tests.dll | `29bb32247f370ced57759db13ba2eb7eae7f1e57f0e24c99a47ad2887abf5ac2` |
| Restored server DLL, producer and staged copy | `e8e24db3000400822ab4060b51e21e63c4184af6af86b1a486fa6a53c1e21d2e` |
| Restored Antiphon.Tests.dll | `22075ace11001d10bf78c2c388e26df8ee7f3574ab89fe723f69ca1196e6a159` |

Producer: `server/bin/Debug/net9.0/Antiphon.Server.dll`; staged copy: `tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Server.dll`. Before/after hashes match the corresponding frozen build for every driver. The packed DLL contains both selector literals; the restored producer and staged DLL contain neither the temporary selector variable nor helper name.

## Selected mutations and exact filters

One process variable, `ANTIPHON_MUTATION_0B3F9D62`, selects exactly `S6-oldids`, `S12-dangling`, or empty/off. All four packed drivers used identical frozen source and DLL bytes. There is no test-name/reflection mapping. The module initializer rejects any other ID; the external driver also refuses unknown IDs before starting PowerShell or requesting a lease. An INVALID-driver check exited 1 before creating a run directory; no .NET test driver launched for that check.

| Selector | Temporary change | Exact filter |
|---|---|---|
| S6-oldids | LoggedIdsAsync reads only the new-id field (Skip(1).Take(1)); both fields' validation remains. | `/*/*/C1708ReflogSalvageTests*/Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref` |
| S12-dangling | SalvageAsync bypasses the entire symbolic-ref probe loop. Namespace collision checks, --no-deref, create-only old-id CAS and post-write verification remain. | `/*/*/C1708ReflogSalvageTests*/Dangling_symbolic_salvage_collision_refuses_and_keeps_the_symbolic_ref` |

Each method expands to exactly one parameterless row (args=[]), with the method name shown above. Baselines are selector-off controls on the packed assembly, not a pristine-source build. Restored greens use the fresh restored-source assembly.

S6 intended red (ShouldAssertException, test-driver exit 2): `await SalvageRefsAsync(w)` should equal `["refs/antiphon/salvage/11557fa7-4466-4f9b-a61f-e07ead109d37/55e3af245b4ab12a6a85f9d29f69b6fc1913da26"]`, but was `[]`. The method had already proved the commit existed only as an old reflog ID and that retirement completed. This is the required missing salvage-ref kill.

S12 intended red (ShouldAssertException, test-driver exit 2): `(await w.RowAsync(tick.RunId, a)).ReasonCode` should be `"reflog_salvage_collision"`, but was `"removed"`. A selected-only diagnostic emits the existing post-write for-each-ref verification result:

`MUTATION_0B3F9D62 direct verification=refs/antiphon/salvage/b2960dde-eda8-423d-88bd-344f09257007/fcf124d62519edf61c59d4eb22780da06168c2bb fcf124d62519edf61c59d4eb22780da06168c2bb`

The fixture planted that exact name as a dangling symbolic ref to refs/heads/c1708-missing. The verification has only name and OID, with no symbolic target: the ref was replaced by a direct ref. The reason assertion stops the test before its later byte/target assertions; those later red assertions are not claimed executed. This observed direct-ref replacement plus removed outcome establishes the requested preservation failure, rather than the narrow ==0-only mutant's differently worded refusal. No additional Git operation or test edit was added by the diagnostic.

## Execution and fresh TRX ledger

All commands were foreground, supervised local inherited children, and joined before continuing. No source edits occurred during builds/drivers. Each of eight build/test requests had its own lease request: seven received real BUILD SLOT grants; green-S12 ended with admission exit 4 and launched no test. No further execution/admission request or retry followed that timeout. No unleased/unlimited/skipped driver ran. Broker waits below are the wrapper's printed integer seconds; observed admission includes wrapper startup and acquisition overhead. Time bounds were provisional limits explicitly commissioned by the brief, not claimed measured p95: build admission 10 minutes, build execution 20 minutes; method admission 5 minutes, method driver execution 3 minutes. Existing test attributes were unchanged.

Both build and no-build run properties: `UseAppHost=false`, `TEMP=/tmp`; native default output, no OutputPath/-o/--output. Exact invocation argv and bounds are stored per run in invocation.json and receipt.json. The external watchdog starts its execution budget on the granted line and separately bounds the whole envelope; no watchdog deadline fired.

| Build | Errors / warnings | Broker wait s | Observed admission s | Execution envelope s | Wall s |
|---|---:|---:|---:|---:|---:|
| packed-build | 0 / 1769 | 0 | 1.516 | 428.220 | 429.736 |
| restored-build | 0 / 1746 | 0 | 1.213 | 160.895 | 162.108 |

Packed MSBuild elapsed 00:07:06.99; restored MSBuild elapsed 00:02:40.21. Analyzer/package warnings are retained in the logs and are not mutation evidence.

| Run | Passed / failed / skipped | Driver exit | Broker wait s | Observed admission s | Execution envelope s | Wall s | TRX test duration | Fresh TRX run ID |
|---|---|---:|---:|---:|---:|---:|---|---|
| baseline-S6 | 1/0/0 | 0 | 0 | 1.695 | 47.717 | 49.412 | 00:00:13.8623493 | 1d417bba-e33d-4502-af7d-248eebca32cb |
| baseline-S12 | 1/0/0 | 0 | 0 | 2.271 | 35.506 | 37.777 | 00:00:10.9154346 | bdcdf28d-cd0c-4728-8238-6f82c8ef1829 |
| red-S6 | 0/1/0 | 2 | 15 | 17.778 | 28.086 | 45.864 | 00:00:04.3592636 | b9e0dbea-04a1-420b-a5cc-5099c00f2f83 |
| red-S12 | 0/1/0 | 2 | 0 | 1.651 | 22.749 | 24.400 | 00:00:04.3302105 | 5bbf6dcf-6a90-42e1-8cde-b1a28784eb48 |
| green-S6 | 1/0/0 | 0 | 31 | 33.794 | 27.930 | 61.724 | 00:00:06.0173384 | 56ce7e67-7ec3-45bf-ab10-58ab0b47dec7 |
| green-S12 | NOT RUN | 4 | timed out at 5m | 303.177 (admission-only wall) | 0 (not launched) | 303.177 | none | none |

TRX paths are `/work/evidence/task-0b3f9d62/<Run>/run.trx`; per-row execution IDs, stable test IDs, start/end times and full assertion stacks are in parsed.json and the original TRX. They are five distinct fresh runs, not reused results. The green-S12 request has no TRX: it was admission-only, wall 303.177 seconds, final position 1 with all four slots occupied. Its receipt records unchanged restored DLL hashes before/after the wait.

## Phase timing and limits

ANTIPHON_TEST_PHASE_ROOT and per-run/group identity were set for every method driver. Each executed run retains its owned timing JSONL and summary.json. The admission-only green-S12 request has neither. The following host intervals derive from TRX Times and row start/end; startup includes discovery and the shared database fixture, and the test-result interval includes the method's own setup/disposal. They are elapsed intervals, not sums of overlapping spans.

| Run | Host wall s | Startup/discovery/shared setup s | Test-result interval s | Post-result host teardown s | Recorded process wait s | Recorded DB cleanup s | Method unattributed remainder s |
|---|---:|---:|---:|---:|---:|---:|---:|
| baseline-S6 | 43.000 | 24.292 | 13.863 | 4.845 | 1.846 | 0.052 | 11.993 |
| baseline-S12 | 31.997 | 18.551 | 10.915 | 2.531 | 1.598 | 0.054 | 9.296 |
| red-S6 | 25.186 | 15.403 | 4.359 | 5.424 | 0.459 | 0.031 | 3.886 |
| red-S12 | 20.389 | 14.641 | 4.330 | 1.418 | 0.423 | 0.031 | 3.893 |
| green-S6 | 23.066 | 13.218 | 6.017 | 3.831 | 0.722 | 0.031 | 5.283 |

Shared fixture records separately retain database creation and runtime-model setup; Docker readiness/cache durations remain in TRX output, and post-result teardown is the host interval above. Individual uninstrumented teardown operations remain unknown. Docker readiness was successful for every run; the unchanged fixture uses one shared PostgreSQL container per process and isolated cloned databases. No production runner was used (the assembly guard records 127.0.0.1:1). Git scratch repositories and the assembly-local GitProcessSpawnLimit remain unchanged; no Pty test assembly was co-scheduled.

Process-spawn spans are nested inside process-wait spans, so they must not be added as separate elapsed time. Missing standalone migration, web-host, IO/filesystem, HTTP and other external-call splits are unknown; recorder zero totals are not proof that an uninstrumented call cost zero. Shared/test remainder is retained rather than assigned to a guessed phase. These five executed samples are not a measured host p95. Aggregate driver wall 1114.198 s, observed admission 363.095 s, execution envelopes 751.103 s. The admission-only final request contributes its whole wall to admission and zero execution (no child launched). No individual selection/build or the complete local slice exceeded 30 minutes.

## Restoration and continuation

After both reds joined, restoration used the exact command `git checkout -- server/Infrastructure/Git/WorktreeReflogSalvage.cs`. Restored bytes equal the pinned base Git blob and original source SHA-256; checkout refreshed timestamps. The temporary switch was removed, then exactly one restored native-output build ran. S6 restored green passed 1/1; S12 restored green never launched because admission timed out. Final product/test diff is empty. Restored output is retained in its native ignored locations, as the direct-build policy prescribes; no mutant-built producer/staged DLL remains. No cleanup deletion was required or performed. Only this report is committed.

The author red records from 935ca9be/bb0a6ce8 were read as precedent, not counted as new kills. Historical full-group/Review/native/SourceLanding gaps are unchanged. All other controls remain continuation inventory, not authorized selections here: C-salvage S1–S5 and S7–S11, including the additional S8 unfinished-release and S10 direct-collision bindings. The full inventory and masking caveats remain in Investigate ce37c38f at origin/feat/card-task-ce37c38f:docs/investigations/2026-11-06-investigate-s4a-mutation-sweep-plan.md. No missing witness/design decision was exposed for these two selected controls. The S12 cycle is verification-incomplete solely because its restored-green admission failed. Preserve its red evidence; do not repeat the completed reds or S6 cycle.

Reproduction in a separately authorized ordinary worktree: start at the tested base, apply `/work/evidence/task-0b3f9d62/mutants.patch`, choose a new external evidence root in a copy of run.cjs (existing run directories refuse reuse), and repeat the two archived exact baseline/red commands, restore the literal path, rebuild once and repeat the two greens. Keep each lease and timeout unchanged; invocation.json contains literal argv. The report alone has no formal SourceLanding or overall-sweep completion claim.


Historical handoff: the initial report requested only the missing S12 restored-green selection before continuing the remaining inventory. This continuation discharged that exact missing selection; no completed red or S6 cycle was repeated.

--- next stage ---
next: mutation
handoff: Retain the settled S6-oldids and S12-dangling waived evidence. Commission bounded slices for the remaining C-salvage controls S1–S5 and S7–S11, including S8's unfinished-release and S10's direct-collision bindings. Preserve the shared continuation ledger; no overall SourceLanding completion is claimed.
artifact: /work/worktrees/task-0b3f9d62/docs/investigations/2026-11-06-mutation-s4a-C-salvage-0b3f9d62.md
