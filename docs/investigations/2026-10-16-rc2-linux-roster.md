# RC2 Linux frozen roster verification

**FAIL: 4,514 passed, 3 failed, 0 skipped in 4,517 cases across all 373 frozen classes. All three failures reproduce alone on RC2 and pass alone on RC1.** These are two introduced verification-contract root causes; no code was fixed. The Linux RC2 gate remains red.

## Source and selection

**Final remote-ref change:** after all execution and report publication, `git ls-remote` observed `refs/heads/rc/2026-10-15` at `41d6f6adf431622b3982f9ac41417a8e555072ba`, rather than the initially verified pin. All results below bind only `f7900158ef3416894c720d892bb1c21cc8095057` product/test source. Per the stop-on-mismatch instruction, no execution was started against the advanced tip; this report does not qualify that current branch tip. The RC owner must reconcile the moved ref before commissioning another gate.

Candidate `refs/heads/rc/2026-10-15` was verified by `git ls-remote origin refs/heads/rc/2026-10-15` as exactly `f7900158ef3416894c720d892bb1c21cc8095057` before execution. The assigned branch is `feat/card-task-ca4254f8`. RC1 comparison source is exactly `067b47e2dc903247f1a7c8aa539032cc4266e4ad`, built in the task-created detached checkout `/work/evidence/task-ca4254f8/rc1-checkout`.

The [Linux qualification document](2026-10-15-card-1287-linux-unit.md), [class roster](2026-10-15-card-1287-linux-unit-roster.txt) and [literal filter](2026-10-15-card-1287-linux-unit-filter.txt) matched `origin/master` at initial inspection, `69031cfa11244dc988c8d79d3f516bc627dd3824`. The exact frozen selection and all historical exclusions were retained. The brief's explicit full frozen-roster run and isolated candidate comparisons are the verification scope; no whole-Unit or unrelated suite was added.

The actual clean RC2 build commit and assembly informational version are `2a3f0c78698fa52bcb21bcdd0bc0fe55ed4bfc8b` / `1.0.0+2a3f0c78698fa52bcb21bcdd0bc0fe55ed4bfc8b`. Its sole delta from the pinned candidate is the initial version of this report. Product, test and build inputs match the candidate. Neither source tree was edited during execution; tracked/index status was clean before and after the test runs. The later report commit does not relabel the compiled source.

Fresh aggregate TRX identities equal all **373** roster classes: missing classes **0**, extra classes **0**, missing compiled methods **0**. All **2,867** currently compiled selected methods have results. RC2 has 411 compiled Unit classes; the remaining 38 are outside this frozen subset. The 4,517 cases are one more than the historical 4,516 because `CodexResponseAnalyzerTests.Plain_echo_takes_precedence_over_an_earlier_cursor_separated_match` was added within an existing selected class. The checkpoint project-contract method was also renamed, without changing its case count. No case was removed to obtain a result.

## Execution and wall time

| Run | Executed | Passed | Failed | Skipped | Classes | Outer wall | TRX wall | Slot wait | Exit |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| RC2 frozen roster | 4517 | 4514 | 3 | 0 | 373 | 45m25.971s | 45m16.739s | 0s | 2 |

RC2 isolated build: **2m31.734s**, 0 errors / 1,060 warnings. RC1 isolated build: **3m46.749s**, 0 errors / 1,057 warnings. Both builds had granted slots with zero wait. RC1's build overlapped the RC2 aggregate in a separate checkout under a separate granted lease; test assembly drivers ran sequentially. This is shared-host timing, with no adjusted or substituted duration.

The aggregate ran from **2026-10-16T08:05:50.554Z** to **2026-10-16T08:51:16.525Z**. From the first RC2 build start through the last isolated comparison, elapsed wall was **49m47.285s**; this includes the overlapping baseline build, audit gaps and reruns, and excludes report preparation. The aggregate exceeded the earlier 40m34s–42m15s observations; its actual completed wall is retained.

Each rerun selected exactly one fully qualified class/method and produced exactly one result with the expected identity. No retry changed the original aggregate verdict.

| Run | Executed | Passed | Failed | Skipped | Outer wall | Slot wait | Exit |
|---|---:|---:|---:|---:|---:|---:|---:|
| rc2-failure1 | 1 | 0 | 1 | 0 | 0m11.245s | 1s | 2 |
| rc2-failure2 | 1 | 0 | 1 | 0 | 0m13.751s | 1s | 2 |
| rc2-failure3 | 1 | 0 | 1 | 0 | 0m6.888s | 0s | 2 |
| rc1-failure1 | 1 | 1 | 0 | 0 | 0m5.839s | 0s | 0 |
| rc1-failure2 | 1 | 1 | 0 | 0 | 0m7.597s | 0s | 0 |
| rc1-failure3 | 1 | 1 | 0 | 0 | 0m13.229s | 0s | 0 |

Debian 12, SDK 10.0.401, runtime .NET 9.0.20, TUnit 1.44 / Microsoft Testing Platform 2.2.2. `TUNIT_MAX_PARALLEL_TESTS` and `ANTIPHON_HEADED_TESTS` were unset. All nine build/test invocations used `scripts/build-slot.ps1` and received granted leases; none ran unleased. No application deployment, AppHost or production session operations occurred. No assertion, timeout, selection, product or test source was changed.

## All failures and attribution

| Failure (fully qualified class and method) | Error | RC2 alone / RC1 alone | Classification | Introducing commit evidence |
|---|---|---|---|---|
| `Antiphon.Tests.Application.AbsentLaunchPolicyTests.C1149_Queue_columns_are_accounted_for` | Reflected `SessionQueuedMessage` properties differ from `AbsentLaunchPolicy.MessageColumns`: ten newly mapped properties are absent from the whitelist. | 1 failed / 1 passed | Introduced since RC1; queue-column policy contract mismatch. | First three added properties: `b8893359f3c4b27e41c7934bfb229cd16e152191`. Seven further direct-reply properties: `6288f26fea0ded62206d74e4f5166d3482ce921a`. |
| `Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata` | `missing classes: Antiphon.Tests.Application.SessionRuntimeActionDeferralTests`; `missing-reason Antiphon.Tests.Application.SessionRuntimeActionTests`; `unregistered-marked Antiphon.Tests.Application.SessionRuntimeActionDeferralTests`. | 1 failed / 1 passed | Introduced since RC1; slow-test registry completeness/reason contract. | Missing reason originates in `f4770aa921e818cc94aef90c95ee6d5613c00e28`; unregistered Slow deferral class introduced by `59544042d8937d5e97b266098f494acee1ea5ab1`. |
| `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G059` | Classification errors unexpectedly contain `unregistered-marked` for `SessionRuntimeActionDeferralTests`. | 1 failed / 1 passed | Introduced since RC1; same missing Slow registration as the preceding row. | `59544042d8937d5e97b266098f494acee1ea5ab1`. |

The ten queue properties are `ReviewThreadId`, `ReviewReplyConsumedAt`, `RuntimeActionKey`, `DirectReplyPromptSequence`, `DirectReplyThroughSequence`, `DirectReplyPendingThroughSequence`, `DirectReplyProvider`, `DirectReplyConversationId`, `DirectReplyHandle` and `DirectReplyTailClosedAt`. The queue assertion is at `tests/Antiphon.Tests/Application/AbsentLaunchPolicyTests.cs:158`; policy consumption is in `AgentTaskDispatcher`. The registry assertions are at `tests/Shared/TestClassificationGuardTests.cs:28` and `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs:19`.

Introduced classification is demonstrated by the exact isolated executions at both candidates. The specific introducing commits above are **source-diff attribution, not an executed commit-by-commit bisection**. The whitelist and corresponding guard did not change across the candidate interval. No inherited or isolation-only passing failure was observed. These results demonstrate guard/contract regressions; they do not establish a separate end-user runtime failure. Repair requires policy assessment of the new queue fields and correction of the slow-test registry through separately commissioned code work.

## Reproduction

From a checkout with the frozen roster files, use fresh result directories and the pinned source. The task-owned output name and labels distinguish this execution from the earlier qualification; the native commands and filter are otherwise the documented recipe.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-rc2-ca4254f8/ --property:UseAppHost=false --nologo
roster_filter=$(cat docs/investigations/2026-10-15-card-1287-linux-unit-filter.txt)
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-roster -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-rc2-ca4254f8/ --property:UseAppHost=false -- --treenode-filter "$roster_filter" --output Detailed --report-trx --report-trx-filename run.trx --results-directory "$PWD/.antiphon/rc2-roster-fresh"
```

For each isolated invocation use the same gated no-build command, a fresh results directory, and one of these exact filters:

- `/*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/C1149_Queue_columns_are_accounted_for`
- `/*/Antiphon.TestSupport/TestClassificationGuardTests/Registry_matches_compiled_metadata`
- `/*/Antiphon.Tests.TestHelpers/TestClassificationPolicyTests/C487_G059`

Repeat the build and those three isolated commands from the exact RC1 checkout. The retained Node driver supplied literal native argument arrays, preserving the wildcard filter unchanged. Exact argv and working directories for every invocation are in the evidence folders below.

## Evidence, limits and cleanup

Raw evidence root: **`/work/evidence/task-ca4254f8/`**. Each of `build/`, `roster/`, `rc1-build/`, `rc2-failure1/` through `rc2-failure3/`, and `rc1-failure1/` through `rc1-failure3/` retains console, exact argv and terminal timing. The seven test folders retain fresh `run.trx`, result JSON and audit JSON. The root retains compiled metadata, the source index/status, method delta, source-diff attribution and producer-owned cleanup records. There is no fabricated checkpoint source stamp or Review source-clean assertion.

| Binding | SHA-256 |
|---|---|
| RC2 main assembly | `58d2869e16d521355e44a4a5c7ff049d2cace1fe669e604b864a3916caea5852` |
| RC1 main assembly | `16c106afce0f896f2cb0526477014dd7e414195e013b5dfee29c4e08bad792a5` |
| Frozen roster | `716018884f1a68c923958dde2c65fc7150ecb78d123972fd2d671bfadbe28168` |
| Frozen filter | `9ba95d1db683438f6c382422c9ca4a596d2280930f100da9973b657596020ad7` |
| Aggregate TRX | `ec1680ef0101405bb2d0371bf8810c91be654caee1661d2640e3777fb8b49708` |

Assembly and frozen selection hashes were unchanged after all tests. All observed outcomes are Passed or Failed and counts match the TRX counters. `raw-trx-validation.json` binds counters read directly from the unchanged XML; the PowerShell audit JSON serialized its XML Counters node as an empty array. The isolated summary initially misread PowerShell's one-element JSON object as an array and reported `identityMatches=false`. The original summary is preserved. `isolated-validated.json` is an append-only deterministic correction, binding the original summary hash, validator hash, unchanged TRX/result hashes, exact selected identities and expected outcomes; all six identities validate. This reporting correction caused no test reruns or source changes.

All owned foreground drivers completed. Removed **56 producer-inventoried `bin-rc2-ca4254f8` roots**, 28 from each checkout, with no recorded root remaining. `cleanup-inventory.json` retains the exact file hashes and directory identities before deletion. The clean detached RC1 checkout remains under the external evidence root; no blanket temporary-directory cleanup occurred. Generated logs, TRX and JSON are outside committed deliverables. Only this report changed on the assigned branch.

This pass covers the frozen Linux subset only. It provides no full-Unit, excluded-class, Windows or runtime deployment qualification. The RC owner must decide remediation for the two introduced root causes and commission focused repair/reverification; this failed gate does not authorize release.

--- next stage ---
next: decide
handoff: RC owner to commission remediation for the queue-column policy contract and missing slow-test registration/reason; retain the failed RC2 gate until verified repair.
artifact: /work/worktrees/task-ca4254f8/docs/investigations/2026-10-16-rc2-linux-roster.md
