# CARD-1287: Windows Unit lane at a30b7f4

On 2026-10-15 the full Windows Unit selection completed normally and failed: **5,591 cases, 5,108 passed, 94 failed, 389 skipped**. Tested source was **a30b7f4297400f010ce70188ac99ebe55be51931**, on Windows 10.0.19045, SDK 10.0.300 / .NET 9.0.21, with default TUnit in-process parallelism and one assembly driver at a time. No product/test repair or card filing was performed.

| Measurement | Observed |
|---|---:|
| TUnit wall | 52m 13.831s |
| Outer Unit driver, including 15s lease wait | 52m 33.981s |
| Isolated build outer wall | 3m 10.306s; 0 errors |
| Build plus Unit outer driver walls | 55m 44.287s |
| Selected classes / methods | 407 / 3,494 |

All runs used `scripts/build-slot.ps1 -Label unit-lane`. One isolated build produced `bin-c1287-5ca376de/`; subsequent tests reused that binary. The full run used `dotnet run --project tests/Antiphon.Tests --no-build` with filter `/*/*/*/*[Category=Unit]`, detailed output and TRX. All 93 failed methods were then selected individually from the same binary: **99 results, 19 passed, 80 failed, no skips**. Fifteen originally failed cases pass alone; **79 remain failed**. A different receipt argument newly fails the same four-second child deadline. Original failures remain recorded.

The 24 groups below are proposed follow-up card topics, not filed cards. D overlaps three E cases and one F case. The detailed report maps all 94 original failures to these groups and records the landed history for each.

| Group / proposed card topic | Cause and isolated outcome |
|---|---|
| A. Restore routing-owner guidance | CARD-1250 removed the exact owner link expected in `ops-http.md`; reproduces alone. |
| B. Portable matrix byte limits | `AppendLine` emits CRLF; splitting on LF leaves CR and measures 161 bytes against a 160-byte cap. Both methods reproduce. |
| C. Bounded receipt reads under contention | Three-second asynchronous read deadlines cancel in the full lane; nine methods pass alone. Related import/output refusals lack the internal read diagnostic, so their common-cause attribution is inferred. |
| D. Windows Git fixture teardown | Recursive `Directory.Delete` rejects read-only Git objects in 27 original cases; an inspected object had ReadOnly attributes. Teardown also masks body failures in overlapping cases. |
| E. Consistent fixture Git normalization | Three C1241 fixtures commit CRLF with global `core.autocrlf=true`; hardened admission suppresses global config and reports a dirty project. A scratch probe confirms this mismatch; all three also fail teardown. |
| F. Portable failing-Git fixtures | Two output-cleanup cases require missing Windows executable `/bin/false`; one also fails teardown. Both reproduce. |
| G. Receipt child-start deadline | Four seconds includes cold dotnet startup and the CLI operation. Two original failures pass alone, but a different whitespace argument hits the same deadline in isolation. |
| H. Scheduler owner-loss timing | Fixed five-second waits expire under full-lane load; the method passes alone. The timeout does not prove a product regression. |
| I. Sweep budget assertions | A five-second production sweep competes with an exact removal-count assertion; the full lane retains an extra root, while isolation passes. |
| J. Coverage-intake diagnostics | Both methods pass alone. Full-run exit 2 and an unexpected admission diagnostic hide the precise operation; reader contention is plausible but unresolved. |
| K. Windows state replacement | `SafeWrite` overwrite move gets access denied with a concurrent reader in both runs. CARD-1199 replaced the previous replacement helper and exception handling. No corrupt JSON was demonstrated. |
| L. WSL linked-worktree identity | Linux Git interprets the Windows `.git` pointer as a relative Linux path; `rev-parse` fails and the wrapper calls `Trim` on null. Reproduces. |
| M. Legitimate audit source provenance | Two C1050 cases and one C1210 case require `checkpoint-build-source.json`, absent from the plain build recipe. They fail before nested audits; no stamp was fabricated. |
| N. Extracted harness dependency | C1066 extracts `c849_fixture_workspace_labels_ok` without its new resource-state dependency. Both arguments fail before the intended npm assertion. |
| O. C1192 ownership mock | Four methods invoke real `chown` through `sh`, which does not import the exported Bash function. WSL uid 1000 cannot assign uid 1654. Traces confirm fixture failures, not production Docker-root failure. |
| P. C1192 acquisition ledger | Lock/barrier fixtures plant volumes without `.fixture-volumes` entries. CARD-1208 rejects them as `CacheVolumeForeign` before the intended child starts. |
| Q. Partial-cleanup observer | The fixture expects attempted removal of never-acquired `restore-p1`; acquisition-only deletion correctly omits it, leaving a stale `cleanup=0` witness. Explicit failure cuts can retain residue. |
| R. Transfer model dynamic scope | Uninitialized local `mode` shadows the dispatch mode under Bash `set -u`; fake image lookup fails before transfer and reports `CacheHelperImageMissing`. |
| S. Native Linux fixture admission | 28 methods pass unconverted Windows `remote.sh` paths to Bash, which exits 127 before host assertions. They need platform admission while retaining Windows wrapper coverage. |
| T. Legacy jq method duration/cancellation | Four serial harnesses exceed the new ten-minute TUnit limit; the independent child wait ignores TUnit cancellation. All four harnesses report success in the full run, but the method fails in both runs. |
| U. Native registration-only deletion | The false argument reproduces Windows sharing error 32 at `SetFileInformationByHandle`; the true argument passes. The sharing-handle owner was not captured. |
| V. Shared test DI registration | `DelegateBranchDeliveryTests` directly registers `AgentTaskReplyService` without the shared graph/harness registration; the Rule C guard reproduces. |
| W. Refusal-path separators | Full lane hits Git timeout. Isolation reaches a stable mixed-slash diagnostic mismatch against a `Path.Combine` expectation. Both outcomes are retained. |
| X. Embedded-source census | The text scanner treats `FreshProbe` in a raw C# fixture string as an untagged compiled class. Reproduces; it does not establish an unclassified compiled test. |

The [full report](C:/src/Antiphon/.antiphon/investigations/card-1287-5ca376de/task-5ca376de.md) contains every failed case, its isolated result, 24 proposed follow-up card titles, source locations, 34 full ancestor commit SHAs, exact commands, uncertainty disclosures and skip reasons. [The evidence bundle](C:/src/Antiphon/.antiphon/investigations/card-1287-5ca376de/summary.json) retains the original TRX and all per-method records. History attribution uses git log/blame and diffs, not historical bisection. Exact internal operations remain unresolved where full-run admission diagnostics were lost; those limits are explicit.

CARD-1281 D-6 needs at least the observed approximately **56 minutes** for this build plus Unit sample, then queue allowance, margin and independently measured integration work. This failed sample is not a clean qualification or completion guarantee. Windows also has 389 explicit native-Linux/FIFO/statx/host-contract skips; a blanket zero-skip full-Unit row is infeasible. Release-gate script classes and NightlyWatchdogCoreTests are Integration here and are outside this Unit result.

One 64-second observer attempt was aborted without a completed TRX. The completed legacy rerun's TRX reports 10m 01.758s; its observer's later count was rejected after direct zero census, so exact child-drain duration is unmeasured. Its 1,026-second lease includes observer overhead and is excluded from lane sizing. All records are retained; no source stamp was fabricated.

The wrap-up refinement stopped further reruns. Owned child processes were drained or stopped; the final census and removal of all 28 alternate output roots are retained in the evidence bundle. Product and test sources were unchanged.
