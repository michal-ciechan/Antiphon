# RC2 candidate 1 final Linux roster verification

**PASS: 4,527 passed, 0 failed, 0 skipped across all 373 frozen classes.** The complete aggregate finished in **36m21.108s**. All three previously failing RC2 cases pass. No product, test, build input or selection was changed.

## Source and selection

The required candidate, `refs/heads/rc/2026-10-15`, was verified with `git ls-remote origin refs/heads/rc/2026-10-15` at exactly **b7265a05aa8773f08dbf457b93c7561e7874822d**, both before execution and after completion. The assigned branch `feat/card-task-45e84b80` started clean at that commit.

The [qualification document](2026-10-15-card-1287-linux-unit.md), [373-class roster](2026-10-15-card-1287-linux-unit-roster.txt) and [literal filter](2026-10-15-card-1287-linux-unit-filter.txt) were byte-identical to `origin/master` at initial inspection, **cd038acb3b62efbb67431866449ed6152acdb0db**. The documented native commands were retained, changing only the task-owned output path, labels and evidence directory. One complete aggregate was commissioned here; the historical two-run qualification is not claimed for this candidate.

The actual clean build commit is **82606e9440df27ad1d2bdb8810ed7f17b01b0f49**, and its assembly informational version is `1.0.0+82606e9440df27ad1d2bdb8810ed7f17b01b0f49`. Its sole delta from the candidate is the initial pending version of this report. Product, test and build inputs match the pinned candidate. Tracked/index status was clean before the build and after execution; main-assembly, driver, roster and filter hashes were unchanged. The later completed-report commit is reporting only and is not relabeled as the compiled source.

## Counts, coverage and timing

| Run | Executed | Passed | Failed | Skipped | Classes | Outer wall | TRX start/finish wall | Slot wait | Exit |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| RC2 candidate 1 frozen roster | 4527 | 4527 | 0 | 0 | 373 | 36m21.108s | 36m15.170s | 0s | 0 |

The fresh TRX counters agree with all 4,527 individual results; every outcome is Passed. Class identities equal the roster exactly: **0 missing, 0 extra**. All **2,867 compiled selected methods** have results: **0 missing methods**. There are **412 compiled Unit classes** at this source; the other **39** remain outside the frozen selection.

The historical roster had 4,516 cases. The previous RC2 execution at `f7900158ef3416894c720d892bb1c21cc8095057` had 4,517, following the added `CodexResponseAnalyzerTests.Plain_echo_takes_precedence_over_an_earlier_cursor_separated_match`. Compared with that execution, the selected methods are unchanged. The entire additional **10-case** increase is in `AbsentLaunchPolicyTests.C1149_Whitelist_requires_every_fact`, from **76 to 86** data rows. The new rows exercise `ReviewThreadId`, `ReviewReplyConsumedAt`, `RuntimeActionKey`, `DirectReplyPromptSequence`, `DirectReplyThroughSequence`, `DirectReplyPendingThroughSequence`, `DirectReplyProvider`, `DirectReplyConversationId`, `DirectReplyHandle` and `DirectReplyTailClosedAt`. No previous case was removed. The existing contract repair is `46fd97a187866e26f56a6f261038ada4d7d57870`.

The isolated build passed with **0 errors / 1,060 warnings** in **3m20.885s**. The aggregate ran from **2026-10-16T10:19:06.500Z** to **2026-10-16T10:55:27.608Z**. Elapsed wall from build start through aggregate completion was **39m47.225s**, excluding subsequent audit/report preparation. Both build/test drivers received granted leases with **zero wait**; neither ran unleased. This is an observed shared-host wall, not a performance guarantee.

Environment: Debian 12, SDK 10.0.401, .NET 9.0.20, TUnit 1.44 / Microsoft Testing Platform 2.2.2. `TUNIT_MAX_PARALLEL_TESTS` and `ANTIPHON_HEADED_TESTS` were unset. No AppHost, application deployment or production-session operations occurred; the prescribed build-slot broker was used. The test assembly used its isolated test fixtures.

## Failures and prior repairs

| Failure name | Error | Classification | Introducing commit |
|---|---|---|---|
| None (0 failed cases) | None | No failure to classify | Not applicable |

The three historical failures were independently identified in this aggregate's fresh TRX:

| Fully qualified test | Result |
|---|---|
| `Antiphon.Tests.Application.AbsentLaunchPolicyTests.C1149_Queue_columns_are_accounted_for` | 1 passed |
| `Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata` | 1 passed |
| `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests.C487_G059` | 1 passed |

There were no failing cases to rerun alone or compare against RC1 `067b47e2dc903247f1a7c8aa539032cc4266e4ad`; no baseline build or unrelated tests were added. No assertion relaxation, timeout extension or code fix occurred.

## Reproduction

From the pinned source with the frozen roster files, use a fresh results directory:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-rc2-45e84b80/ --property:UseAppHost=false --nologo
roster_filter=$(cat docs/investigations/2026-10-15-card-1287-linux-unit-filter.txt)
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc2-roster -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-rc2-45e84b80/ --property:UseAppHost=false -- --treenode-filter "$roster_filter" --output Detailed --report-trx --report-trx-filename run.trx --results-directory /work/evidence/task-45e84b80/roster
```

The retained Node driver used literal argument arrays; exact argv and working directories are recorded for both invocations.

## Evidence and cleanup

Raw evidence root: **/work/evidence/task-45e84b80/**. It contains `build/` and `roster/` consoles, exact argv, terminal timing and output inventories; the fresh **/work/evidence/task-45e84b80/roster/run.trx**; compiled metadata; source SHA/index/status; initial/final candidate-ref observations; class/method and case deltas; prior-failure result identities; hashes; and cleanup records. `roster/audit.json` binds direct XML counters, result counts, identities and TRX hash. No checkpoint source stamp or Review source-clean assertion was fabricated.

| Binding | SHA-256 |
|---|---|
| Main assembly | `56ba4184ec35d81d6d527b72ac8350b60f801662a1cb677fc9e08a1335d720c3` |
| Frozen roster | `716018884f1a68c923958dde2c65fc7150ecb78d123972fd2d671bfadbe28168` |
| Frozen filter | `9ba95d1db683438f6c382422c9ca4a596d2280930f100da9973b657596020ad7` |
| Aggregate TRX | `601898784f2d9fb394ce5cefabd1710b5b968366e3f9182233b07cf1e5deb5ce` |
| Literal-argv driver | `f90cb21d853cd37e4cba6ad897be735e42ed3c70e56a25d82fbb4bb16b11c0f0` |

All owned drivers completed. Removed **28 producer-inventoried `bin-rc2-45e84b80` roots**, with none remaining. `cleanup-inventory.json` retains exact file hashes and directory identities before deletion. No blanket temporary-directory cleanup occurred. Generated logs/TRX/JSON stay outside the committed deliverable. Only this report changed on the task branch.

This is a passing gate for the explicit frozen Linux subset at the pinned candidate. Full Unit, excluded classes, Windows and runtime deployment qualification remain outside this pass.

--- next stage ---
next: none
handoff: RC owner can record the frozen Linux roster gate as passing for b7265a05aa8773f08dbf457b93c7561e7874822d and combine it with the other required release gates.
artifact: /work/worktrees/task-45e84b80/docs/investigations/2026-10-16-rc2-final-linux-roster.md
