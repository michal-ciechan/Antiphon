# CARD-1299 implementation evidence

Original Code/landing owner: 095a7f1a-4260-43dc-b1dd-f0505eb03695.
Branch: feat/card-task-095a7f1a; worktree: /work/worktrees/task-095a7f1a.
Base: 067b47e2dc903247f1a7c8aa539032cc4266e4ad.
Plan: [closed selection](../plans/2026-10-15-card-1299-plain-echo-plan.md).

The exact reviewer witness failed before the production fix: one executed result,
one assertion failure, no skips. Actual response was `> Run abc?\nDone.` instead
of `Done.`. Fresh TRX confirms the intended class and method. Source was clean,
build provenance verified, and ownership stayed bound; no unbound fallback used.

```text
CHECKPOINT CP-1 commit=ca8e61f0c7baf4fc0a314119b3623dbc5a8d373a build=ok filter=/*/*/CodexResponseAnalyzerTests/Plain_echo_takes_precedence_over_an_earlier_cursor_separated_match executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-215133-5a8f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=ca8e61f0c7baf4fc0a314119b3623dbc5a8d373a sourceState=clean buildSource=verified
```

Red run: `.antiphon/checkpoints/20261015-215133-5a8f/report.json` and sibling
report.md, TRX and build log. Build 201.247s; test host 3.319s; total 3m38s.
The report lists two manifest builds but only bin-c1299-red ran; the final build
was unused. The leased launcher bootstrap was the sole unlisted build, required
to obtain the checkpoint executable: 11.08s MSBuild wall, 2 warnings, 0 errors,
slot=granted waited=0s, lease hold 13s. No unlisted tests.

S2 prioritizes the plain echo match for opted-in Codex callers and searches the
cursor-separated text only on a plain miss. The separated response is retained
when neither form matches. Final CP-3/6/7/8 results are pending this commit.
The brief explicitly excludes whole Unit; no aggregate or Windows qualification
is claimed. PC-1 remains pending for SourceLanding Mutation, as do parent
CARD-0907 PC-1..PC-12 and D1-C1..D1-C4 variants. Restart after activation: server.

## Final ordinary verification

All required CARD-1299 V/R obligations are complete on Linux at production/test
commit **47117fd69ccbcb2eeabf51813932b8bbe24365fc**. The later evidence commit is
reporting only and is not relabeled as tested source. Round: Final under the
brief's explicit bounded scope; no whole Unit, full assembly, Windows, or live
CLI qualification is claimed. No manual acceptance was required by this plan.

| ID | Actual outcome |
|---|---|
| V-1 | Expected red at ca8e61f0c7baf4fc0a314119b3623dbc5a8d373a; passed after repair, including direct/adapter exact answer, false question verdict, completion and raw snapshot |
| R-1 / CP-3 | 74 passed: CodexResponseAnalyzerTests 12, SubscriptionUsageParserTests 5, UsageLimitWallParserTests 39, ProviderQuotaRefusalTests 18 |
| R-2 / CP-6 | 21 passed: RunnerCodexAdapterTurnCompleteTests 7, SubscriptionUsageMonitorTests 14 |
| R-3 / CP-7 | 20 passed: RunnerGrokAdapterTurnCompleteTests 12, OpenCodeAdapterTests 5, CodexAdapterRawResponseTests 3 |
| R-4 / CP-8 | 9 passed: TestLaneCategoryGuardTests 1, DelegationHarnessCensusTests 7, TestClassificationGuardTests 1 |

Fresh TRX audit covers all 12 intended classes, all 101 source test methods and
124 executed cases, with no missing methods, failures or skips. The two historical
CARD-1287 source-reader failures did not reproduce here; no baseline run was
needed or executed. Audit: `.antiphon/c1299-trx-audit.json`.

### Historical first final run: tests passed, certificate refused

Run 20261015-215558-9dda executed 124/124 passing tests but ended exit 2,
`refused: still-present`. Receipt validation returned
`CHECKPOINT SOURCE INVALID reason=cleanup_refused`. This is not a valid green
certificate and remains preserved without edits.

Its final output was removed by the tool. Its report.outputPaths also included
bin-c1299-red, the retained output of the earlier, unselected CP-1 row. Inspection
of CheckpointApp.cs shows OutputPaths and the remaining-path check use all
manifest builds, including unused builds. That older output remained and caused
the final cleanup refusal. This is a tooling limitation outside the parser fix;
no checkpoint source or receipt was changed to bypass it. Board search for the
literal still-present returned no cards; this note preserves the finding for
caller triage.

The exact rows were repeated once at the same source SHA after removing the
producer-owned old red output (2235 files; no tracked files or links). The retry
used --keep-outputs, followed by explicit owned-output cleanup after completion.
This extra isolated build/repetition was necessary to obtain the missing valid
certificate, not a flake retry. There were two normal final selections, zero loaded
repetitions, no timeout/assertion changes and no deliberate mutants.

Unedited historical lines:

```text
CHECKPOINT CP-3 commit=47117fd69ccbcb2eeabf51813932b8bbe24365fc build=ok filter=/*/*/(CodexResponseAnalyzerTests*)|(SubscriptionUsageParserTests*)|(UsageLimitWallParserTests*)|(ProviderQuotaRefusalTests*)/* executed=74 passed=74 failed=0 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-215558-9dda/rows/CP-3/run.trx slot=granted waited=60s dirty=0 source=47117fd69ccbcb2eeabf51813932b8bbe24365fc sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=47117fd69ccbcb2eeabf51813932b8bbe24365fc build=reused filter=/*/*/(RunnerCodexAdapterTurnCompleteTests*)|(SubscriptionUsageMonitorTests*)/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-215558-9dda/rows/CP-6/run.trx slot=granted waited=150s dirty=0 source=47117fd69ccbcb2eeabf51813932b8bbe24365fc sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=47117fd69ccbcb2eeabf51813932b8bbe24365fc build=reused filter=/*/*/(RunnerGrokAdapterTurnCompleteTests*)|(OpenCodeAdapterTests*)|(CodexAdapterRawResponseTests*)/* executed=20 passed=20 failed=0 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-215558-9dda/rows/CP-7/run.trx slot=granted waited=75s dirty=0 source=47117fd69ccbcb2eeabf51813932b8bbe24365fc sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=47117fd69ccbcb2eeabf51813932b8bbe24365fc build=reused filter=/*/*/(TestLaneCategoryGuardTests*)|(DelegationHarnessCensusTests*)|(TestClassificationGuardTests*)/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-215558-9dda/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=47117fd69ccbcb2eeabf51813932b8bbe24365fc sourceState=clean buildSource=verified
```

### Valid final certificate

Run 20261015-221006-2045 exited 0. All selected rows have dirty=0,
sourceState=clean, buildSource=verified and slot=granted waited=0s.
All checkpoints stayed task-bound; no missing-token/unbound fallback was needed.
The validator returned:

```text
CHECKPOINT SOURCE VALID source=47117fd69ccbcb2eeabf51813932b8bbe24365fc rows=4
```

Unedited final lines:

```text
CHECKPOINT CP-3 commit=47117fd69ccbcb2eeabf51813932b8bbe24365fc build=ok filter=/*/*/(CodexResponseAnalyzerTests*)|(SubscriptionUsageParserTests*)|(UsageLimitWallParserTests*)|(ProviderQuotaRefusalTests*)/* executed=74 passed=74 failed=0 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-221006-2045/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=47117fd69ccbcb2eeabf51813932b8bbe24365fc sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=47117fd69ccbcb2eeabf51813932b8bbe24365fc build=reused filter=/*/*/(RunnerCodexAdapterTurnCompleteTests*)|(SubscriptionUsageMonitorTests*)/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-221006-2045/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=47117fd69ccbcb2eeabf51813932b8bbe24365fc sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=47117fd69ccbcb2eeabf51813932b8bbe24365fc build=reused filter=/*/*/(RunnerGrokAdapterTurnCompleteTests*)|(OpenCodeAdapterTests*)|(CodexAdapterRawResponseTests*)/* executed=20 passed=20 failed=0 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-221006-2045/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=47117fd69ccbcb2eeabf51813932b8bbe24365fc sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=47117fd69ccbcb2eeabf51813932b8bbe24365fc build=reused filter=/*/*/(TestLaneCategoryGuardTests*)|(DelegationHarnessCensusTests*)|(TestClassificationGuardTests*)/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-095a7f1a/.antiphon/checkpoints/20261015-221006-2045/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=47117fd69ccbcb2eeabf51813932b8bbe24365fc sourceState=clean buildSource=verified
```

Revalidate without running tests:

```sh
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261015-221006-2045/report.json -ExpectedSourceSha 47117fd69ccbcb2eeabf51813932b8bbe24365fc -Rows CP-3,CP-6,CP-7,CP-8
```

The closed selection is reproducible with the plan's CP-3,CP-6,CP-7,CP-8 rows,
committed HEAD as expected-source-sha, and --keep-outputs. Use the documented leased
launcher bootstrap when its output is absent, then await run/wait to terminal.
Final receipts, TRX and logs stay ignored in the original checkpoint directories.

### Costs and cleanup

| Run | Reported wall | Isolated build | Test-host walls CP-3/6/7/8 | Row slot waits |
|---|---|---|---|---|
| Red-first CP-1 | 3m38s | 201.247s | CP-1 3.319s | 0s |
| First final, cleanup-invalid | 11m49s | 278.122s | 6.610 / 88.169 / 16.090 / 10.138s | 60 / 150 / 75 / 0s |
| Valid final retry | 6m21s | 251.141s | 5.497 / 79.967 / 15.850 / 10.151s | 0 / 0 / 0 / 0s |

These are the tool's measurements, excluding its later cleanup/foreground-wait
cost. The first final MSBuild driver itself reported 242.36s, 1078 warnings,
0 errors. Both final manifests mention two builds; only the selected final build
executed in each. The unlisted launcher bootstrap remains the sole extra tooling
build. The receipt-repair retry is explicitly accounted for above. No unlisted
test selection, baseline execution or further green repetition occurred.

Both checkpoint executors terminated and their shadow copies were removed by wait.
The first final tool removed its final output. Manual cleanup removed the earlier
red root (2235 files), the valid retry's final root (2235 files), and the two leased
bootstrap roots (38 and 3 files). Each target was identified from its producer,
checked for tracked content and links, and removed only after all runs finished.
Cleanup receipt: `.antiphon/c1299-output-cleanup.json`. No task-owned bin-c1299
compiler outputs remain; retained directories named for builds under checkpoint
folders contain evidence, not compiler outputs.

### Handoff

No introduced product failure, unresolved required ordinary verification, or
current-card deferred-to-final ID remains. Initial ordinary Review is next.
PC-1 remains pending, unbound, for SourceLanding Mutation; parent CARD-0907
PC-1..PC-12 and D1-C1..D1-C4 (all variants) remain pending under that card's owners.
Mutation owns deliberate mutants, red/restore/green and missing-control discovery.
Candidate: [plain echo precedence](../mutations/2026-10-15-card-1299-plain-echo-candidates.md).

Original Code task 095a7f1a-4260-43dc-b1dd-f0505eb03695 is the landing owner.
Restart: server after publication and activation in the next cut. No runner restart
is needed. No service was restarted and release/2026-10-15 was not changed.
