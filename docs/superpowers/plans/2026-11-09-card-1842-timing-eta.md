# CARD-1842: test and batch timing model, ETA

Date: 2026-11-10. Filename date is the brief's `2026-11-09`. Base `ef7afd44927206f6c03547670a5add84130bdce9`. Card `94e75145-5a8c-4e6e-a694-4bdd5b6e73e4`.

Inventory: `docs/investigations/2026-11-10-test-timing-eta-inventory.md` on `origin/feat/card-task-99b53b68` at `d7cea32130bdf3ba4e192791aedba87eb8b44d2a`. This plan builds on that inventory. It adds no second duration store.

This plan does not add a session that waits for input. With parking disabled there is no automatic release deadline to set (CARD-1083). Nothing here runs on the desktop, and nothing is deployed. Checkpoints name the Linux lane. `GET /api/runner-defaults` and `GET /api/session-runners` were read at plan time: placement stays the global default, and both a linux host and a windows host are eligible. Commands omit `-Runner` and `-Platform`.

## Intended behaviour

### Before

```
TRX result durations
  -> scripts/refresh-nightly-class-costs.ps1
  -> scripts/lib/nightly-class-costs.json   (one seconds, file platform from the refresher's separator)
  -> Get-NightlyClassCostTable (seconds/nodes)
  -> packer plannedSeconds on groups-planned-<suite>.json
  -> heartbeat idle budget for the active chunk only
  -> sidecar <suite>-<chunk>.timing.json (runWallSeconds, lastPhase)
  -> watch-qual.ps1 prints failures, RUN-ENDED, HANG-SUSPECTED, HEARTBEAT-STALE

rc ledger document can store durationSeconds
  -> no production caller of Write-RcLedger
  -> a host field is ledger-invalid
```

`scripts/refresh-nightly-class-costs.ps1` lines 16-20 copy `nodes`, `seconds`, and `basis`, and drop a row `source`. Lines 39-46 rewrite a refreshed row as `basis=trx` with no `source`, and set file `platform` from `DirectorySeparatorChar`. The committed file is `format=antiphon-nightly-class-costs-v1`, `platform=windows`, 772 classes (measured 711, conservative-bound 30, observed-bound 26, pace 5), 764 of them with `source` (`scripts/lib/nightly-class-costs.json` lines 1-6).

`Get-NightlyClassCostTable` (`scripts/lib/nightly-tests-impl.ps1` lines 226-241) keeps `seconds/nodes` and ignores every other property. `Get-NightlyLaunchClassCost` (lines 267-290) and `NightlyLaunchPlan.ClassCost` (`scripts/lib/Antiphon.Nightly/NightlyCore.cs` lines 917-945) price from that per-node double. The packer writes `plannedSeconds` (`scripts/lib/test-phase-evidence.ps1` lines 606-614 and 636-658). The heartbeat uses that number only for the active chunk (`scripts/lib/qualification-supervisor-heartbeat.ps1` lines 75-81 and 214-229). The sidecar stores the actual (`test-phase-evidence.ps1` lines 3778-3804). `scripts/orchestration/watch-qual.ps1` lines 33-99 do not read `plannedSeconds`. `Get-RcQualificationStatus` (`scripts/lib/rc-qualification-status.ps1` lines 122-149) reads four records. `docs/release-gates.md` lines 156-159 say `lastActivityAt` does not supply an ETA.

`scripts/lib/rc-ledger.ps1` line 389 accepts only `antiphon-rc-ledger-v1`. Identity and chunk `durationSeconds` are nullable (lines 85-90, 419, 434, 694, 703). The closed maps are lines 666-716. `Write-RcLedger` (lines 648-660) refuses an existing file. The only caller is `scripts/test-rc-ledger.ps1`. Line 355 of that test rejects the format string `antiphon-rc-ledger-v2`.

`PlatformTaxonomy.Derive` (`tests/Shared/PlatformTaxonomy.cs` lines 117-177) returns a platform label. `AgentTask.ExpectedDurationMinutes` (`server/Infrastructure/Data/AppDbContext.cs` line 2418) is the delegate check-in hint.

### After

```
measurement caller passes -HostId -Os -MeasuredRunId
  -> refresh keeps row source
  -> primary seconds change only when -Os is the file platform
  -> a different OS is stored as otherSample and does not replace seconds
  -> S3 promotes otherSample to secondsByOs only past the tolerance
  -> export writes tests/Shared/expected-duration-roster.json
  -> packer still reads one per-node number (the launch OS entry when a split exists)

timing root (groups-planned-*.json, active-chunk.json, *.timing.json)
  -> scripts/qualification-eta.ps1 prints one line
  -> watch-qual.ps1 prints that line only when -TimingRoot is set

ledger document
  -> Set-RcLedgerDurations fills durationSeconds from the sidecar and the TRX
  -> host is written only after format is antiphon-rc-ledger-v2
```

Outstanding, running, and completed come from the plan rows and the sidecars. The finish line is `now` plus the priced remainder. Below 8 completed walls, p50 is that point and p95 is `unavailable`.

### What stays unchanged

- `PlatformTaxonomy.cs` lines 10-178. `Derive` stays a platform function. CARD-1832 is the neighbor file, not a change on this card.
- `AgentTask.ExpectedDurationMinutes`.
- `Get-NightlyLaunchClassCost`'s return shape `{NodeCount, Seconds, MeasuredPerNode, Heavy}` and `NightlyLaunchPlan.ClassCost`'s signature. OS selection happens in PowerShell before the per-node dictionary is built.
- Heartbeat writer, idle budget, and `buildSlot=not-used` on qualification sidecars (`docs/test-performance-standards.md` lines 51-58).
- `Get-RcQualificationStatus`'s four records and its 1 MiB limit. It does not grow an ETA property.
- `Write-RcLedger` still refuses an existing path. The nightly loop still has no ledger caller. This card does not turn on `-SelectionMode Incremental`.
- The committed 772-row cost file is not rewritten by S1. New properties appear when a measurement refresh runs. Missing `hostId`, `os`, and `measuredRunId` read as null.
- `tools/Antiphon.TimeoutAudit/SourceInventory.cs` `ReadCosts` (lines 143-148) keeps reading `basis` and `seconds`. The fixture at `tests/Antiphon.TimeoutAudit.Tests/TimeoutAuditTests.cs` line 136 stays a v1 document without the new properties.
- Run `6591e378` is not opened by any checkpoint.

## Ground truth

| The card assumes | The code does |
|---|---|
| An expected duration per class, one number, split per OS only when the second OS differs | One `seconds` per class. File `platform` is rewritten from the refresher's directory separator. No second OS column. |
| Record the host the measurement came from | No host on a class row. Supervisor `platform` is `Windows` or `Unix` on the identity file and is not joined to a class. |
| Maybe an attribute beside `PlatformTaxonomy` | No duration attribute. `Derive` (lines 117-177) is platform-only. The cost file has 772 classes. |
| Estimate each batch, list outstanding, and give an ETA with p50 and p95 | `plannedSeconds` is stored and used as the active chunk's hang budget. Nothing sums the rows still outstanding. The class file says it is not a p95 (line 4). |
| Ledger duration, host, OS, CPU, and counts | Nullable `durationSeconds` and document `platform` exist. No production writer. Host is rejected. CPU and executed/passed/failed/skipped are on the TRX and the sidecar, not on the ledger. |

Counted on this tree: 772 classes, basis measured 711, conservative-bound 30, observed-bound 26, pace 5, source present on 764, `platform=windows`.

## Decisions

### D-1 Roster, with an attribute only as a checked annotation

Recommendation: the expected duration is the class-cost row. A generated roster beside `PlatformTaxonomy.cs` projects it. Code does not put an attribute on the 772 classes.

The roster file is `tests/Shared/expected-duration-roster.json`, format `antiphon-expected-duration-roster-v1`. Each class copies `seconds`, `basis`, `hostId`, `os`, and `measuredRunId` (null when the cost row has no measurement identity). `scripts/export-expected-duration-roster.ps1` writes it from the cost file. `tests/Shared/ExpectedDurationRoster.cs` reads that path. It is a `Compile Include` in `tests/Antiphon.Tests/Antiphon.Tests.csproj` next to the `PlatformTaxonomy.cs` include at lines 172-173. The JSON is not an `EmbeddedResource`: a refresh must not change the test assembly hash.

`tests/Shared/ExpectedDurationAttribute.cs` is the operator's attribute, also linked only into `Antiphon.Tests`. It has one constructor argument, the seconds. This card applies it to no test class.

Sync guard, one class `ExpectedDurationSyncTests`:

- Every roster class equals the cost row's `seconds` and `basis`, and the class sets match.
- A source scan of `tests/**/*.cs` finds zero `[ExpectedDuration(` applications on this card.
- A fixture class that carries the attribute passes only when its seconds equal the roster seconds for that class name.
- A fixture class that carries the attribute and is absent from the roster fails. The packer never reads the attribute, so an attribute-only class would be invisible.
- `Derive` on an empty member list still returns `PlatformClassLabel.Both`. The roster reader does not call `Derive`.

Rejected: a hand attribute on all 772 classes (it drifts from the measured file, and a refresh would edit test source that CARD-1836 input digests hash); generating a 772-entry C# table (same drift, and it changes the compiled test assembly on every refresh); making the attribute the only store (the packer reads the cost file); deleting the attribute type (the operator asked for an attribute, and the guard is what keeps a later annotation honest).

### D-2 One number, then a split past both thresholds

Recommendation: keep `seconds` as the number the packer reads for the file platform. Store a second OS only as `otherSample` until both of these hold, comparing primary `seconds` with `otherSample.seconds`:

- the absolute difference is greater than 30 seconds, and
- the absolute difference is greater than 0.25 times the larger of the two.

Constants: `ExpectedDurationOsSplitMinSeconds = 30`, `ExpectedDurationOsSplitMinRatio = 0.25`. Equal to either threshold does not split.

Fixtures the witness uses:

| Primary | Other | Difference | Ratio of the larger | Result |
|---|---:|---:|---:|---|
| 100 | 120 | 20 | 0.167 | one number |
| 100 | 130 | 30 | 0.231 | one number (30 is not greater than 30) |
| 200 | 150 | 50 | 0.250 | one number (0.25 is not greater than 0.25) |
| 400 | 510 | 110 | 0.216 | one number (ratio gate) |
| 100 | 140 | 40 | 0.286 | `secondsByOs` |
| 200 | 149 | 51 | 0.255 | `secondsByOs` |

On a split, `secondsByOs` is `{ "windows": <n>, "linux": <m> }` and `otherSample` is removed. `seconds` stays the file-platform sample. The launch OS, already known to the nightly script as `windows` or `linux`, selects `secondsByOs[os] / nodes` when that key exists, otherwise `seconds / nodes`. `Get-NightlyClassCostTable`'s process cache key includes that OS, so one process cannot keep a windows table for a later linux price.

A refresh passes `-HostId`, `-Os`, and `-MeasuredRunId`. All three are required. The script does not read the machine name and does not map supervisor `Unix` to `linux`; the caller passes `linux`. `-Os` must be `windows` or `linux`.

When `-Os` equals the file `platform`, the matching row's `seconds`, `nodes`, and `basis=trx` update, and `hostId`, `os`, and `measuredRunId` record this sample. The previous row `source` string is copied onto the new row. Rows absent from the TRX are copied whole, including `source` and any measurement fields.

When `-Os` differs from the file `platform`, `seconds` is left as it is and the sample is `otherSample`. A linux run of the refresher must not set file `platform` to `linux`. File `platform` changes only when `-SetPlatform` is passed and every stored primary `os` agrees. S1 does not pass `-SetPlatform` in any witness.

Rejected: averaging the two OS samples; replacing `seconds` with the slower sample; inferring OS from `DirectorySeparatorChar`; treating supervisor `Unix` as a stored `os` value.

### D-3 ETA from plannedSeconds

Recommendation: `scripts/qualification-eta.ps1` is read-only. Inputs are one timing root: `groups-planned-*.json` except `groups-planned-index.json`, `active-chunk.json`, and `*.timing.json`. The class-cost file is opened only for a plan row whose `plannedSeconds` is missing. The script writes nothing.

Chunk key is suite plus chunk id. State, in order:

1. Non-empty `completedAt`, or `lastPhase` in `complete`, `timeout`, `cancelled`, `teardown` (the settled set at `qualification-supervisor-heartbeat.ps1` line 75): completed. The reason is that `lastPhase`, or `complete` when only `completedAt` is set.
2. Else `active-chunk.json` `state=Active` for that suite and chunk: running.
3. Else outstanding. A plan row with no sidecar is outstanding. A sidecar left at `lastPhase=run` after the active chunk moved on is outstanding.

Remainder: outstanding contributes its price; running contributes `max(0, plannedSeconds - elapsed)` where elapsed is `NowUtc - startedAt`. A missing or unparsable `startedAt` contributes the full `plannedSeconds`. A completed chunk contributes 0. A missing `plannedSeconds` uses the sum of cost-file `seconds` for the row's classes, labeled class-cost. A class that is also absent is unpriced and is not treated as 0. The line carries `unpriced`.

Finish time uses the priced remainder only. Let N be the count of completed chunks that have `plannedSeconds > 0` and a numeric `runWallSeconds`. Ratio is `runWallSeconds / plannedSeconds`.

- N below 8: p50 is `now + priced remainder` (ratio 1). p95 is the token `unavailable`.
- N at least 8: sort ratios ascending. Index is `ceil(percentile * N) - 1`. p50 uses 0.50 and p95 uses 0.95. Finish is `now + priced remainder * ratio[index]`. For N=8 that is index 3 and index 7.

The sidecar's own `p50Seconds` and `p95Seconds` are per-test durations. The ETA does not read them. The class file has no p95. Shared build time stays inside `runWallSeconds` and outside `plannedSeconds`; the ratio absorbs it once 8 walls exist. Until then the point estimate does not add a constant for the copied suite build.

Stdout is one line:

`ETA outstanding=<n> running=<n> completed=<n> unpriced=<n> pointSeconds=<n> p50=<utc> p95=<utc|unavailable>`

`-NowUtc` fixes the clock in tests. The default clock is UTC now.

Rejected: summing class-cost `seconds` for every outstanding chunk when `plannedSeconds` is already on the plan row; printing p95 from the class file; writing the line back into the timing root; treating a negative overrun as a negative remainder.

### D-4 One watch-qual line

Recommendation: `scripts/orchestration/watch-qual.ps1` gains optional `-TimingRoot`. When it is set, each poll, including `-Once`, prints the one ETA line. When it is omitted, stdout stays the current failure, end, hang, and stale lines. The state file remains the list of seen TRX paths. The heartbeat writer is not edited.

`rc-qualification-status.ps1` and `Get-RcQualificationStatus` gain no parameter and no property. `docs/release-gates.md` keeps the `lastActivityAt` refusal and gains one sentence that the ETA line is the watch-qual line with an explicit timing root.

Rejected: walking the log directory from `Get-RcQualificationStatus` (that reader is a 1 MiB, four-record check); putting the ETA on the heartbeat (a live qualification would gain a new writer).

### D-5 Ledger duration on v1, host only after v2

Recommendation: `Set-RcLedgerDurations` in `scripts/lib/rc-ledger.ps1` sets chunk `durationSeconds` from sidecar `runWallSeconds` and identity `durationSeconds` from the matching TRX `UnitTestResult/@duration`. It does not change `format`. It does not set a host. `Write-RcLedger` of that v1 document stays valid.

Host is a second step in the same slice, after the format bump. `antiphon-rc-ledger-v2` is v1 plus optional chunk `hostId` and chunk `os` (each a string or null). `Read-RcLedger` accepts v1 and v2. Format `antiphon-rc-ledger-v3` still fails with `format`. A v1 document that contains `hostId` still fails `ledger-invalid` before any write. `Set-RcLedgerHost` throws `ledger-schema` unless the in-memory document's format is already v2, and only then sets the two chunk fields from arguments the caller passed. It does not read the machine name. `-IncludeHost` on the stamp script is the only path that sets format to v2 and then calls `Set-RcLedgerHost`. Omitting it writes v1 with durations and no host properties.

`scripts/test-rc-ledger.ps1` line 355 changes the rejected token from `antiphon-rc-ledger-v2` to `antiphon-rc-ledger-v3`. The case `MissingFieldIsInvalid` still rejects an unknown format. That is the schema bump, not a loosened assertion. `UnknownPropertyIsInvalid` still rejects a v1 chunk that carries `hostId`.

No production nightly call is added. `Write-RcLedger` has no caller outside the ledger tests, and CARD-1836's plan keeps the live loop on Full until its own later switch. A stamp script writes a new ledger path for a caller who already holds a document; it refuses an existing path the same way `Write-RcLedger` does.

Rejected: adding `hostId` to the v1 map (line 355 and the closed schema forbid it); writing the host before the reader accepts v2; calling the stamp from `nightly-tests-impl.ps1` in this card.

### D-6 Desktop measurement is a fixture here and a read on the qualification host

Recommendation: `scripts/measure-class-cost-error.ps1` reads a log directory and writes only the path the caller passes. It refuses when that path is inside the log directory. For each `*.trx` with a `ResultSummary`, wall is `Times/@finish` minus `Times/@start`. Class sum is the cost-file `seconds` of the classes on the matching `groups-planned` row, not `seconds/nodes`. Error is wall minus that sum. The report also keeps sibling `runWallSeconds` and `buildSeconds`. It does not subtract `buildSeconds` from the error. Family is the suite plus the chunk id with a trailing `-group-<n>` or `-profile-<n>` removed.

The Linux witness uses a fixture shaped like the inventory's `dce78ee0` note: a shared build copied onto chunks, one watchdog-long wall, and one chunk whose test-duration sum exceeds the wall. The real directory `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378` is not a checkpoint. The full run id is read there from `supervisor-identity.json` or `summary.json`. The report is written outside that directory.

Rejected: using `dce78ee0` as a substitute result for `6591e378`; opening that directory from the Linux lane; correcting the estimator by a hard-coded 131 second build.

## Tests

New tests are committed red, then the production change. Each new class is `[Category("Integration")]` and `[ParallelLimiter<ProcessSpawnLimit>]`, with `[Timeout(120_000)]` and a `CancellationToken` parameter, matching `RcIncrementalLedgerTests`. Harness cases use `ScriptHarness` and a 90 second case budget inside that timeout. Min in the checkpoint table is the TUnit method count. PASS lines inside a harness are not Min.

| Id | Witness | Red before the fix | Mutant after green |
|---|---|---|---|
| V-1 | `RefreshKeepsRowSource` | line 19 drops `source` | delete the source copy |
| V-2 | `RefreshRequiresHostOsAndRun` | the script has no such parameters | default `-HostId` from the machine name |
| V-3 | `ForeignOsDoesNotReplaceSeconds` | line 46 sets `platform` from the separator and line 40 replaces `seconds` | let a linux refresh overwrite `seconds` |
| V-4 | `PackerStillReadsPrimarySeconds` | extra JSON properties are ignored today, so this goes red only if the table starts returning the raw row instead of `seconds/nodes` | divide by `nodes` twice, or price from `otherSample` |
| V-5 | `RosterMatchesCostFile` | the roster file is absent | change one roster `seconds` |
| V-6 | `TreeHasNoExpectedDurationAttribute` | no attribute applications exist; the red commit is a fixture scan that the production scanner must still report zero on the tree | leave one `[ExpectedDuration(` in `tests/` |
| V-7 | `AttributeAgreesWithRoster` | no comparer exists | accept a 1 second difference |
| V-8 | `AttributeOnlyClassFails` | no comparer exists | ignore a class missing from the roster |
| V-9 | `DeriveStaysAPlatformLabel` | would fail if S2 edited `Derive` | make the roster reader call `Derive` |
| V-10 | `DocPinsTheRosterSentence` | the Nightly subsection does not exist yet | delete the roster sentence |
| V-11 | `WithinToleranceKeepsOneNumber` | no split function | split on the 100 vs 120 fixture |
| V-12 | `PastBothThresholdsStoresSecondsByOs` | no `secondsByOs` | keep `otherSample` after a 100 vs 140 split |
| V-13 | `EqualThresholdDoesNotSplit` | no split function | treat 30 seconds or ratio 0.25 as enough |
| V-14 | `LaunchOsSelectsSplitSeconds` | the table has no OS key | keep the windows per-node price for a linux launch |
| V-15 | `DocPinsTheTolerance` | the 30 second and 25 percent sentence is absent | change the doc to 20 seconds |
| V-16 | `OutstandingSumsPlannedSeconds` | no ETA script | drop one outstanding row from the sum |
| V-17 | `RunningSubtractsElapsed` | no ETA script | add the full planned seconds of the active chunk |
| V-18 | `CompletedDoesNotAdd` | no ETA script | add a `timeout` chunk |
| V-19 | `FewWallsLeaveP95Unavailable` | no ETA script | print a numeric p95 below 8 walls |
| V-20 | `EightWallsSetP50AndP95` | no ETA script | average the ratios instead of the nearest-rank index |
| V-21 | `RefusesTimingRootWrites` | no ETA script | write `eta.json` under the timing root |
| V-22 | `OncePrintsOneEtaLine` | watch-qual has no `-TimingRoot` | print the line twice in `-Once` |
| V-23 | `OmittedTimingRootStaysSilent` | current stdout has no ETA token; the new assertion fails if a stub always prints one | print ETA when the parameter is omitted |
| V-24 | `StatusObjectHasNoEta` | the status object has no ETA property; the assertion is the pin that S5 does not add one | add `eta` to the status object |
| V-25 | `ReleaseGatesKeepsLastActivityRefusal` | the new sentence is absent; the existing refusal at lines 156-159 must remain | delete either sentence |
| V-26 | `V1DurationFromSidecarAndTrx` | no filler; durations stay null | leave chunk `durationSeconds` null when `runWallSeconds` is present |
| V-27 | `V1RejectsHostBeforeWrite` | already red for a v1 host via `ledger-invalid`; the new witness requires the throw before the file exists | write the file and then throw |
| V-28 | `V2AcceptsExplicitHost` | line 389 rejects v2 | accept a v2 host that the caller did not pass |
| V-29 | `V2OmitsHostWhenSwitchOff` | no stamp script | emit `hostId: null` on a v1 document |
| V-30 | `WallMinusClassCostSum` | no measure script | compare wall to `seconds/nodes` |
| V-31 | `OutputInsideRunRefused` | no measure script | write the report under the log directory |
| V-32 | `SharedBuildStaysInTheError` | no measure script | subtract `buildSeconds` from the error |
| R-1 | `NightlyCoreTests.CoreMatchesFallback` | already green; S3 must keep the C# and PowerShell prices equal | change only one side of `ClassCost` |
| R-2 | `LiveLaneRunnerTests` four methods | already green; the nightly-live-lane row runs because S3 edits `scripts/lib/nightly-*.ps1` | a lock-order edit inside the class-cost change |
| R-3 | `UnknownPropertyIsInvalid` | already rejects an unknown chunk property | add `hostId` to the v1 chunk map |
| R-4 | `MissingFieldIsInvalid` | line 355 rejects v2; after the bump it rejects v3 and still rejects a missing field | leave v2 rejected, or stop rejecting v3 |

Doc sentences, exact:

- Under `docs/testing-and-build.md` `## Nightly` (line 1514): "Expected duration is one seconds number on the class-cost row. A second OS is stored only when the two samples differ by more than 30 seconds and by more than 25 percent of the larger. The roster beside PlatformTaxonomy is a projection of that file. An ExpectedDuration attribute, when present, must equal the roster seconds for that class."
- Under `docs/release-gates.md` immediately after the `lastActivityAt` sentence (lines 156-159): "The batch ETA is the one line from scripts/orchestration/watch-qual.ps1 when -TimingRoot is passed. This status reader does not walk a timing root, and lastActivityAt does not supply an ETA."

V-10 pins the roster and attribute sentences. V-15 pins the 30 second and 25 percent sentences. V-25 pins the watch-qual sentence and the existing `lastActivityAt` refusal.

Existing tests affected: no migration, no public DTO or route, no operation label, no remote script function, and no statement budget. CARD-1782 `tests/impacted-classes.json` row `nightly-live-lane` matches `scripts/lib/nightly-*.ps1`, so S3's edit selects `LiveLaneRunnerTests` and `NightlyCoreTests`. Those are CP-4 and CP-5, not an unlisted run. No other slice's files match an impacted-classes glob. `ReadCosts` and the timeout-audit fixture are unaffected because `basis` and `seconds` stay required.

## Migrations

None. No EF migration and no `Down`. The cost-file format string stays `antiphon-nightly-class-costs-v1`; new properties are additive and old readers use `nodes` and `seconds`. The ledger format gains `antiphon-rc-ledger-v2` as an additional accepted value. A v1 file remains valid. There is no rewrite of existing ledger files and no downgrade path that deletes rows.

## Slices

Each slice is one red commit and one green commit, about 60-90 minutes. Dependencies: S2 on S1; S3 on S1 and S2; S5 on S4; S6 on the CARD-1836 library already in this tree (`scripts/lib/rc-ledger.ps1`), not on S1-S5; S4 and S7 on nothing. CARD-1832 is not a blocker. S7's desktop read does not block S4.

### S1 Measurement identity on the class-cost row

Edit `scripts/refresh-nightly-class-costs.ps1`. Add `scripts/test-class-cost-measurement.ps1` and `tests/Antiphon.Tests/Scripts/ClassCostMeasurementTests.cs`. Do not rewrite `nightly-class-costs.json`. Do not edit `nightly-tests-impl.ps1`. Packer behavior is proved by pointing `NightlyClassCostPath` at a fixture.

### S2 Roster beside PlatformTaxonomy

Add the export script, the roster JSON, `ExpectedDurationRoster.cs`, `ExpectedDurationAttribute.cs`, the two `Compile Include` lines beside `Antiphon.Tests.csproj` lines 172-173, `ExpectedDurationSyncTests`, and the Nightly roster sentence. Apply the attribute nowhere. Do not edit `PlatformTaxonomy.cs`.

### S3 OS split

Add the tolerance decision to the refresh script and the OS key to `Get-NightlyClassCostTable`. Re-run the roster export in the green commit so V-5 still matches, including `secondsByOs` when present. Add `ExpectedDurationOsSplitTests` and the tolerance sentence. Do not edit `NightlyCore.cs`. CP-4 and CP-5 are the existing nightly-live-lane classes.

### S4 Read-only ETA

Add `scripts/qualification-eta.ps1`, `scripts/test-qualification-eta.ps1`, and `QualificationEtaTests`. Fixtures live under the harness temp directory. The script does not take a class-cost path except when a plan row lacks `plannedSeconds`.

### S5 Watch-qual line

Edit `scripts/orchestration/watch-qual.ps1` and `docs/release-gates.md`. Add `WatchQualEtaLineTests`. Do not edit `rc-qualification-status.ps1` or the heartbeat writer.

### S6 Ledger durations, then the v2 host

Edit `scripts/lib/rc-ledger.ps1` and `scripts/test-rc-ledger.ps1` line 355 (rejected format becomes v3). Add `scripts/rc-ledger-stamp-duration.ps1` and `RcLedgerDurationTests`. Do not call the stamp from `nightly-tests-impl.ps1`.

### S7 Class-cost error, fixture here

Add `scripts/measure-class-cost-error.ps1` and `ClassCostErrorTests`. The desktop listing of run `6591e378` is the owed Windows step, not a checkpoint row.

## Verification design

Lane: Linux. One isolated build per slice. Later rows in a slice reuse that build. Output names contain no `ref` substring. Provisional estimates are at most 12 minutes so `3 * EstimatedMinutes` stays at or under the 45 minute advisory. Do not set a `totalTimeoutMinutes` line. A full-table run derives `max(30, 2 * 88 + 10)` = 186 minutes. A slice run derives from the rows it selects: S3 is `8+10+12` and derives 70 minutes; S6 is `8+8` and derives 42; every other slice derives 30. Code passes `--after` for one slice. The first green run records harness seconds. A later slice widens the 120_000 ms timeout only when that recorded p95 makes the 2x bound exceed it, and then it names this card.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1842-s1/` | cost | `/*/*/ClassCostMeasurementTests/(RefreshKeepsRowSource*)\|(RefreshRequiresHostOsAndRun*)\|(ForeignOsDoesNotReplaceSeconds*)\|(PackerStillReadsPrimarySeconds*)` | V-1, V-2, V-3, V-4 | all listed, 0 failed | 4 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1842-s2/` | roster | `/*/*/ExpectedDurationSyncTests/(RosterMatchesCostFile*)\|(TreeHasNoExpectedDurationAttribute*)\|(AttributeAgreesWithRoster*)\|(AttributeOnlyClassFails*)\|(DeriveStaysAPlatformLabel*)\|(DocPinsTheRosterSentence*)` | V-5, V-6, V-7, V-8, V-9, V-10 | all listed, 0 failed | 6 | 8 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1842-s3/` | split | `/*/*/ExpectedDurationOsSplitTests/(WithinToleranceKeepsOneNumber*)\|(PastBothThresholdsStoresSecondsByOs*)\|(EqualThresholdDoesNotSplit*)\|(LaunchOsSelectsSplitSeconds*)\|(DocPinsTheTolerance*)` | V-11, V-12, V-13, V-14, V-15 | all listed, 0 failed | 5 | 8 |
| CP-4 | S3 | CP-3 | core | `/*/*/NightlyCoreTests/(CoreMatchesFallback*)` | R-1 | all listed, 0 failed | 1 | 10 |
| CP-5 | S3 | CP-3 | lane | `/*/*/LiveLaneRunnerTests/(LockBeforeSlot*)\|(WrapperOnlyPaidManifest*)\|(ChildEnvironmentIsScoped*)\|(DeadlineJoinsOwnedChildren*)` | R-2 | all listed, 0 failed | 4 | 12 |
| CP-6 | S4 | `tests/Antiphon.Tests -> bin-c1842-s4/` | eta | `/*/*/QualificationEtaTests/(OutstandingSumsPlannedSeconds*)\|(RunningSubtractsElapsed*)\|(CompletedDoesNotAdd*)\|(FewWallsLeaveP95Unavailable*)\|(EightWallsSetP50AndP95*)\|(RefusesTimingRootWrites*)` | V-16, V-17, V-18, V-19, V-20, V-21 | all listed, 0 failed | 6 | 10 |
| CP-7 | S5 | `tests/Antiphon.Tests -> bin-c1842-s5/` | watch | `/*/*/WatchQualEtaLineTests/(OncePrintsOneEtaLine*)\|(OmittedTimingRootStaysSilent*)\|(StatusObjectHasNoEta*)\|(ReleaseGatesKeepsLastActivityRefusal*)` | V-22, V-23, V-24, V-25 | all listed, 0 failed | 4 | 8 |
| CP-8 | S6 | `tests/Antiphon.Tests -> bin-c1842-s6/` | ledger | `/*/*/RcLedgerDurationTests/(V1DurationFromSidecarAndTrx*)\|(V1RejectsHostBeforeWrite*)\|(V2AcceptsExplicitHost*)\|(V2OmitsHostWhenSwitchOff*)` | V-26, V-27, V-28, V-29 | all listed, 0 failed | 4 | 8 |
| CP-9 | S6 | CP-8 | ledger-v1 | `/*/*/RcIncrementalLedgerTests/(UnknownPropertyIsInvalid*)\|(MissingFieldIsInvalid*)` | R-3, R-4 | all listed, 0 failed | 2 | 8 |
| CP-10 | S7 | `tests/Antiphon.Tests -> bin-c1842-s7/` | error | `/*/*/ClassCostErrorTests/(WallMinusClassCostSum*)\|(OutputInsideRunRefused*)\|(SharedBuildStaysInTheError*)` | V-30, V-31, V-32 | all listed, 0 failed | 3 | 8 |

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and `docs/test-performance-standards.md` R1-R6.

These checkpoints do not boot AppHost, Postgres, or a browser. Each new case builds its fixture under the harness temp directory and runs one function. No shared container, so R3's once-per-run setup is the harness process, not a database snapshot. External calls are the `pwsh` process and file reads. Unavailable phases stay null.

Durations are provisional. There is no measured p95 for the new classes. The neighbour is `RcIncrementalLedgerTests`: a 90 second harness budget inside a 120_000 ms timeout (`RcIncrementalLedgerTests.cs` lines 10-14). Estimates above are about twice a short harness guess and at most 12 minutes, under the importer advisory (`3 * 12 = 36`, advisory 45). No row is expected to pass 30 minutes, so R1 files no Debug task for these checkpoints. R5: the 120_000 ms timeout stays until a recorded p95 says otherwise. Queue admission is the checkpoint build slot; execution is the row. They are separate.

The qualification this plan prices is not run here. Class-cost `seconds` remains a serial sum, not a wall p95. Chunk wall stays `runWallSeconds`. The ETA point estimate ignores the shared suite build until 8 completed walls supply a ratio. S7 records that error and does not hide it by subtracting `buildSeconds`.

Recording: class-cost `hostId`, `os`, `measuredRunId`, and `seconds`; sidecar `runWallSeconds` and `lastPhase`; ledger `durationSeconds` when a caller stamps a document. The watch-qual line is stdout. It is not written into the run directory.

## Risks

- A foreign-OS refresh that still overwrote `seconds` would replace the windows primary with a linux sample. V-3 fails that.
- `Get-NightlyClassCostTable` caches in `$script:NightlyClassCostTable` (line 224). An OS-blind cache would price the second OS with the first OS number. S3 keys the cache by OS. V-14 fails a stuck cache.
- Below 8 walls the ETA misses the shared build copied onto every chunk. The line is still the sum of `plannedSeconds`. S7's error is the calibration, and D-3 does not add a constant.
- A sidecar stuck at `lastPhase=run` after the active chunk moved is outstanding and inflates the ETA. That matches the settled set already used by the heartbeat. Completing it without `completedAt` or a settled phase would hide a chunk the heartbeat still treats as live.
- S6 could accept `hostId` on v1 and break `UnknownPropertyIsInvalid`. R-3 and V-27 are that gate. The v2 bump moves line 355's rejected token to v3; R-4 fails if v3 starts to read.
- Wiring the stamp into the nightly loop would write ledgers during a Full qualification before CARD-1836's switch. S6 does not add that call.
- The roster and the cost file can drift after a later refresh. V-5 fails closed until the export is re-run.
- An `[ExpectedDuration]` that disagrees with the roster fails V-7. An attribute on a class the roster does not list fails V-8.

## Operator-only steps

No open question. Defaults D-1 through D-6 are the plan. Tag, publish, and promote stay human-only and this card does not offer them. Choosing when to re-run the class-cost refresh against a real TRX, and which `-HostId` string to pass, is an operator measurement. The script does not invent the host. The desktop listing below is the owed measurement, not a checkpoint.

## Owed on Windows

CP-1 through CP-10 run on the Linux lane. The native step is a read-only run of `scripts/measure-class-cost-error.ps1` against `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378`, writing the report outside that directory. It records chunk walls from TRX `Times`, p50 and p95 of those walls, the ten slowest, the sum, and the error against the class-cost `seconds` sum, beside sidecar `runWallSeconds`. This plan does not start that run.
