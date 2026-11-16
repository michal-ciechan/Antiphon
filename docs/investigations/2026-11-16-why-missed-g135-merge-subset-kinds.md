# Group 135: merge-subset kinds expectation omitted timing-labels

Debug `67a5f843` reproduced the Windows group-135 failure on Linux at fetched
current master `18eabe4ce48a46adea27aa9cbb410415eae9ac91`. The exact method
`MergeHighRadiusSubsetTests.merge_subset_runs_every_impacted_row_the_batch_diff_selects`
ran alone: 1 total, 0 passed, 1 failed, 0 skipped, exit 2. Its exact kinds
assertion expected `remote-scripts`, `test-lane-category`; the selector also
returned `timing-labels`. Baseline source was clean and unchanged.

## Introducing change and legitimate selection

Merge 245, the g139 compose-label repair (Debug `22ee1e10`), introduced the
selection extension in `acd7b4530207c8ddad298c5c8e73738fa37be2c8`.
`fe30ff701385ecdecd588bf6935501c9d1453a10` extended the selector's strict
historical expectation; `5e81bfb96058525591b83f31a233fe25d4266f59` corrected
the fixture name. The card-level provenance is Merge 245 / g139 fix, following
Merge 244's CARD-1953 fixture repair. The retained
[g139 investigation](2026-11-15-why-missed-g139-compose-fixture-delete-label.md)
records that original selector red and its repaired green.

The failing test uses `RemoteScriptsRange`, exactly
`a54a6d4f755fa139b41fd7dc8aef456d3d2c0975^..a54a6d4f755fa139b41fd7dc8aef456d3d2c0975`.
It replays this historical changed-file set rather than constructing a new diff:

- `docker-compose.server2-runner.yml`
- `docker/stack.env.example`
- `docs/docker-stack.md`
- `docs/superpowers/plans/2026-11-06-c346b7a6-runner-slot-compose-plan.md`
- `scripts/c590-remote.sh`
- `scripts/fixtures/c346-workspace-slot-compose.mjs`
- `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs`

`scripts/fixtures/c346-workspace-slot-compose.mjs` matches the new exact glob
in the existing `timing-labels` row. It imports and calls `withComposeFixture`
from `compose-fixture-root.mjs`; the shared C# fixture records the
`compose-fixture-delete` TestTiming span. Its admission is intentional.

The correct repair extends only the exact expected kinds list with
`timing-labels`. `AssertImpactedMethods` also pins `RemoteTestsFilter` by
strict equality. That filter already contains `TestTimingOperationLabelTests*`
because the fixed Merge subset includes the whole class. No extra method row
or filter member is added by this kind. Keep the remote-script method checks,
project checks, exact filter, and deliberately broken-output assertions.

## Why Merge 245 missed it and the cheapest pre-land coverage

Merge 245 ran `ImpactedClassesSelectorTests` but omitted
`MergeHighRadiusSubsetTests`. Updating the former's historical kinds list
did not update the latter's independent pin of the same historical range.
The repair changed coverage correctly; its neighbour's expectation was stale.

A whole-`tests/` grep for `impacted-classes.json`, selector invocations,
`timing-labels`, and kinds assertions finds five consuming test classes:
`MergeHighRadiusSubsetTests`, `ImpactedClassesSelectorTests`,
`DerivedImpactIndexTests`, `ReleaseImpactEnvelopeTests`, and
`PostLandEarlyWarningTests`. The last two exercise isolated tables through
the real selector. `DerivedImpactIndexBuilder` reads the table as a helper,
not a test class. `WhyMissedTestsFilter` is a pinned constant inside
`ImpactedClassesSelectorTests`, not another class. `WhyMissedWriteUpContractTests`
mentions the table in documentation requirements but does not read its rows.

Any Merge that edits `tests/impacted-classes.json` must run every consuming
class. The exact filter union found by this audit is:

```text
/*/*/(MergeHighRadiusSubsetTests*)|(ImpactedClassesSelectorTests*)|(DerivedImpactIndexTests*)|(ReleaseImpactEnvelopeTests*)|(PostLandEarlyWarningTests*)/*
```

For this Debug, each class runs once alone, serially, with its own fresh TRX;
the required guard union additionally reruns `DerivedImpactIndexTests`.
Build once in native default output through `scripts/build-slot.ps1`, then use
`dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false --
--treenode-filter '<filter>' --timeout 570s --report-trx` through that same gate.
Build the derived-index driver before two regenerations; the second must say
`derived-impact-index unchanged`. No selector/table change is needed here.

## Evidence and constraints

Ignored evidence is retained at
`/work/worktrees/ws-02/.antiphon/debug-67a5f843/`: baseline build log,
isolated-red TRX/log/source/load metadata, historical files, and selector JSON.
The native-default baseline build passed with 0 errors and 2,142 warnings in
12m47.91s (772s lease), exceeding the requested ten-minute row budget; this
build cost is retained separately from test execution. Initial load was
`13.24 20.29 19.95`; it rose above 40 during compilation. The red waited three
minutes for load admission (`17.02` then `14.59`), then took 14.676s wrapper
wall and 9.680s framework time. All subsequent builds/tests require load below
16, their own build-slot lease, and serial execution. Phase JSONL and per-test
TRX durations are retained; uninstrumented phases remain unknown.

The policy hash recomputed to
`c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`;
the protected-input digest recomputed to
`2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`.
The legacy exclusion pin is
`6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`;
the native-policy guard validates that seal. These inputs remain untouched.
Mutation checks are waived by the supplied operator authority. No live host,
Windows qualification, rc branch, selector script, or selector table is changed.

## Final verification

All repaired-source rows verified committed, clean source
`e57114e5fbafc4c51eb2ba833cb03b2639e00c36`. No tracked source changed during
builds or tests. Each build/test took its own granted build-slot lease. All
final rows admitted at one-minute load below 16; the raw reads are retained in
their JSON records. Final test admission loads were 5.55, 9.10, 12.82, 11.33,
10.07, 7.74, and 10.43 respectively. The merge-subset row first waited three
minutes after an inadmissible 16.20 read. Together with the baseline and
regeneration admission waits, this accounts for nine minutes of load waiting.

| Row | Total / passed / failed / skipped | Framework seconds | Slot wait seconds | Wrapper wall seconds |
|---|---|---:|---:|---:|
| MergeHighRadiusSubsetTests | 6 / 6 / 0 / 0 | 28.565 | 75 | 108.140 |
| TestTimingOperationLabelTests | 2 / 2 / 0 / 0 | 6.054 | 120 | 130.462 |
| ImpactedClassesSelectorTests | 50 / 50 / 0 / 0 | 228.072 | 0 | 233.076 |
| DerivedImpactIndexTests, alone | 27 / 27 / 0 / 0 | 162.532 | 75 | 243.020 |
| ReleaseImpactEnvelopeTests | 11 / 11 / 0 / 0 | 53.709 | 45 | 104.331 |
| PostLandEarlyWarningTests | 22 / 22 / 0 / 0 | 160.906 | 150 | 316.893 |
| Required eleven-class guard union | 115 / 114 / 0 / 1 | 243.623 | 60 | 309.402 |

Final total: **233 results, 232 passed, 0 failed, 1 skipped**. Every consumer
class in the census ran once alone; the guard union additionally ran the full
derived-index class. Every test row used `--timeout 570s` and finished below
ten minutes, including its slot wait. The Windows-only skip is
`WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`;
native Windows execution remains owed. This Linux verification does not claim
Windows qualification credit or change the live run.

All eleven required guard classes have nonzero TRX counts: classification
policy 42, classification guard 1, slow registry 5, slow tripwire 2, native
Linux policy 3, derived index 27, mechanical contract 11, platform guidance 5,
layering 11, lane category 1, and Windows admission 7 (6 passed, 1 skipped).
The actual layering TRX contains:

```text
VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3
```

The native-policy guard validated the legacy exclusion seal. All three pins
remain the exact values above; no repin was needed.

The repaired-source native-default build passed with 0 errors and 2,142
warnings in 7m26.98s (450.347s wrapper wall). The derived-index driver built
first, separately, with 0 errors and 0 warnings in 11.57s; it waited 60s for
a slot (78.341s wrapper wall). Both valid regenerations printed
`derived-impact-index unchanged`, taking 32.806s and 32.743s wrapper wall.
There are no generated-index changes to commit. One preceding regeneration
invocation was refused with usage/exit 1 because the driver accepts only a
repository argument and rejected TUnit's `--timeout 570s` arguments; it changed
no index bytes. The corrected driver commands use external `timeout 570s`
around `dotnet run --no-build` inside the build-slot gate. All TUnit commands
retain the required native timeout switch. The rejected invocation and the
initial failing test remain in the evidence history.

The final test-project build and all test rows remain below ten minutes; the
12m47.91s baseline build is the explicit budget exception already recorded.
Overall wall time reached approximately one hour, including nine minutes of
load admission, slot waits, and both test-project builds. No individual
selection reached 30 minutes. Tests retain per-case durations and phase JSONL;
uninstrumented phases and the wrapper's remaining cost remain unattributed.

The two source slices were pushed separately:

- `0e861287ca6b4bcc59b35be9ce40a3e60ad7d8eb`: baseline red and consumer census.
- `e57114e5fbafc4c51eb2ba833cb03b2639e00c36`: strict kinds expectation extension.

The final evidence appendix changes only this investigation document; the
caller report gives its pushed tip. Evidence-history and whitespace checks
passed at the repaired tip. A fresh fetch still resolved master to the baseline
SHA, and `git merge-tree --write-tree origin/master HEAD` succeeded without
conflicts. Final checks are repeated after this appendix is committed.

Standing caller authority covered the repair and branch pushes, so no extra
approval was requested. Mutation remains waived. The test-only handoff is
**next: merge**.
