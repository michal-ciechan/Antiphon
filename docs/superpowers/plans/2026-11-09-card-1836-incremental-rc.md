# CARD-1836: incremental rc qualification ledger

Date: 2026-11-09. Base `da6b708a2cab8ced435feb2f359d2e90618ae333`. Card `03408341-e38e-4dce-bdef-932c1c5e1ff0`.

An rc recut launches only the chunks whose inputs or results are dirty. A clean chunk keeps its last green result, with the run it came from. Chunks are cut on CARD-1729 boundaries. When a platform ledger is a complete green closure, a script may create `release/x.y.z`. Tag, publish, and promote stay human-only. The live qualification is not switched over by this plan: `-SelectionMode Incremental` defaults off, and `release-cut.ps1` keeps `Full`.

This plan does not add a session that waits for input. Nothing here runs on the desktop, and nothing is deployed. Checkpoints name the Linux lane. No runner is pinned.

## Intended behaviour

### Before

```
rc ref -> release-cut.ps1 -SelectionMode Full (or ChangeAware)
       -> New-ReleaseSelectionAttemptCore
       -> effectiveMode=Full, coverageScope=Full
       -> nightly launches every planned chunk
       -> summary.json.selection copies the attempt
       -> stopped-run seed orders the NEXT full attempt
```

`scripts/lib/release-selection.ps1` `New-ReleaseSelectionAttemptCore` (lines 189-231) always stores `effectiveMode=Full` and `coverageScope=Full`. A missing baseline appends `baseline-absent` (`Read-ReleaseSelectionBaseline`, lines 68-92). `ChangeAware` also appends `change-aware-unqualified` (line 219). The C# hop in `scripts/lib/Antiphon.Scripts/ReleaseSelection.cs` `Attempt` (lines 55-88) does the same for the no-baseline case and refuses (`NotHandled`) when a baseline is requested. `Get-RcProjectSelection` (`scripts/lib/rc-project-selection.ps1` lines 216-270) already computes affected projects, then sets `activation=shadow-only`, `executionEvidence=false`, `gateCredit=false`, and `effectiveMode=Full`.

Launch chunks are not stable. `Get-NightlyProfileExecutionChunks` (`scripts/lib/nightly-tests-impl.ps1` lines 49-138) numbers `{chunk.id}-profile-{n}`. `ConvertTo-NightlyProfileLaunchGroups` (lines 399-519) packs by namespace with first-fit (`Split-NightlyItemsBySeconds`, lines 299-323) and numbers `{sourceId}-group-{n}`. `Resolve-NightlyStoppedSeedOrder` (`scripts/lib/nightly-stopped-run-seed.ps1` lines 451-457) says those chunk ids are not binding. The dispatch comment (`scripts/lib/nightly-dispatch.ps1` lines 22-28) says the order is a preference for a new complete attempt, never execution resume.

The only baseline the selector will accept is `antiphon-full-release-baseline-v1`, and it requires `publish.json` to contain the step `published` (`release-selection.ps1` lines 96-133). That is the human publish archive, not a `release/x.y.z` branch.

### After, only when Incremental is requested

```
candidate SHA
  -> baseline picker (D-5)
  -> per-boundary input digest (D-3)
  -> per-identity dirty predicate (D-4)
  -> pack dirty identities inside one boundary (D-2)
  -> launch those chunks in failed-first order (D-6)
  -> append ledger rows; copy clean identities with carriedFrom
  -> one-page status
  -> on a Windows closure, cut-release-branch.ps1 may push release/x.y.z
```

With the switch omitted, the before path is unchanged, including `change-aware-unqualified` and the published-archive baseline reader.

### What stays unchanged

- `release-cut.ps1` `-SelectionMode` stays `Full|Shadow|ChangeAware` (line 30) until S7 adds `Incremental` as an opt-in. The default parameter stays `Full`.
- `Get-RcProjectSelection` stays shadow-only. This plan does not give it `gateCredit`.
- Stopped-run seeds, `merge-nightly-seeds.ps1`, and `Resolve-NightlyStoppedSeedOrder` stay the Full-run order. A skip inside a completed seed chunk still looks green there (`merge-nightly-seeds.ps1` lines 12-15). The ledger is stricter and does not rewrite that seed rule.
- `tests/Antiphon.Tests/Layering/layers.txt` and `C1729LayeringGuardTests` stay as they are. The boundary reader parses the file; it does not edit it.
- Timing roots, TRX, `summary.json` `timingChunks`, watchdogs, and the build-slot gate stay as they are. Watchers keep watching launched chunks only.
- Run `6591e378` is not read and not restarted. Its log directory is not in a committed note. The dce78ee0 directory is read-only desktop state and is not opened from this host.

## Ground truth

| The card assumes | The code does |
|---|---|
| A result ledger per rc and per release | No ledger. `summary.json` `selection` (`nightly-tests-impl.ps1` lines 1905-1952) copies the attempt. Seeds store one stopped run (`antiphon-nightly-stopped-seed-v1`). |
| Compare a recut to the last green `release/x.y.z`, else the last rc | `Read-ReleaseSelectionBaseline` accepts only a published full-release archive, else `baseline-absent`, and then runs Full. |
| Chunks never mix CARD-1729 boundaries | Packing is per namespace, target 900s, lone class 2400s, max 30 operands (`nightly-tests-impl.ps1` lines 205-223 and 476-508). |
| Stable chunk ids so history compares | Ids are `{source}-group-{n}` and `{source}-profile-{n}`. The seed reader says they are not binding. |
| Dirty when inputs, failure, interruption, not-run, or a later fix say so | The next run is a new Full attempt. CARD-1823 proof (`Get-MergeContainment`, `merge-nightly-seeds.ps1` lines 77-103) clears a failure identity only. It does not skip a green chunk. |
| Carry the last green result | A pass with `coverageComplete=false` stays unfinished (`docs/investigations/2026-11-10-rc-run-dce78ee0-end-unexecuted-outcomes.md` on `origin/feat/card-task-276564a2`). There is no carry. |
| One-page human status | `scripts/release-status.ps1` answers whether the build is a published release. It is not a per-boundary ledger page. |
| Windows and Linux stay separate | The selection contract already stores `platform` (`release-selection.ps1` lines 59-65). Nothing carries a result across it. |
| Measure wall saved on dce78ee0 and 6591e378 | dce78ee0 is desktop-only: 60109s, 1139 planned groups, 1141 sidecars, one 131.056s suite build copied onto every chunk (`docs/investigations/2026-11-10-slow-chunk-trace-and-in-process-plan.md` on `origin/feat/card-task-e1fa7e11`). 6591e378 has no committed path. |

## Decisions

### D-1 Ledger store

Recommendation: immutable JSON files under the release root, one platform at a time.

`{ReleaseRoot}/ledgers/{platform}/{candidateId}/attempts/{runId}/ledger.json` is written once. `{ReleaseRoot}/ledgers/{platform}/index.json` is the latest pointer for that candidate and may be replaced. A cut writes `{ReleaseRoot}/ledgers/{platform}/baselines/release-{version}.json` of format `antiphon-rc-ledger-baseline-v1`.

Who writes: `scripts/rc-ledger-plan.ps1` before launch, and the nightly loop after each launched chunk once S6 is switched on. Humans read `scripts/rc-ledger-status.ps1`, one page: candidate, baseline ref, forced-full reasons, and per-boundary counts of carried, dirty, failed, not-run, plus the carried wall seconds.

Retention: keep every attempt file. Do not delete from this card.

Rejected: a Postgres table (the qualification host already treats the release root as the evidence authority, and a migration would not be where `summary.json` lives); git notes or a ledger branch (hard to append per chunk, poor one-page read); a committed JSON per release (generated evidence stays gitignored, and a recut would churn the source tree).

Schema `antiphon-rc-ledger-v1`: `platform`, `repository`, `candidateId`, `candidateSha`, `baselineSha`, `baselineRef`, `runId`, `attempt`, `contract` (`policyHash`, `catalogHash`, `selectorHash`, `runtimeHash`), `forcedFull` (reason strings), `identities[]`, `chunks[]`. An identity has `suite`, `className`, `method`, `boundary`, `inputDigest` (64 hex or `unknown`), `outcome` (`passed`, `failed`, `interrupted`, `not-run`, `unexpected-skip`, `expected-skip`, `unproven`), `durationSeconds` (number or null), `carriedFrom` (null or `candidateId`, `runId`, `candidateSha`, `chunkId`, `inputDigest`). A chunk has `chunkId`, `boundary`, `suite`, `identityKeys`, `inputDigest`, `outcome`, `durationSeconds`. Chunk outcome is the worst identity outcome, with `unexpected-skip` and `failed` both worse than `passed`.

### D-2 Boundaries and chunk planning

Recommendation: pack inside one boundary, and give the chunk a content id.

Boundary ids, lowercase:

- The seven layers in `tests/Antiphon.Tests/Layering/layers.txt` lines 11-18: `host`, `core`, `persistence`, `git`, `runners`, `integrations`, `application`. Edges on lines 3-9 are the digest closure, not extra boundaries.
- Lane boundaries: `scripts`, `checkpoints`, `tui`, `e2e`, `client`, `messaging`, `session-runner`, `pty-host`, `agents-pty`, `observability`, `server-git`, `markdown-pdf`.
- `unassigned` for anything else.

`suite` stays the policy suite (`tests/test-execution-policy.json` rc `requiredSuites`, lines 32-43). `boundary` is the pack key. One chunk has one suite and one boundary. `tests/Antiphon.Tests/Checkpoints/**` is boundary `checkpoints` inside suite `antiphon`. `tests/Antiphon.Tests/AgentTui/**` is `tui`. `tests/Antiphon.Tests/Scripts/**` is `scripts` even though those classes launch inside `antiphon`. `tests/Antiphon.E2E/**` is `e2e`. The client suite is `client`. The other required suites are one boundary each, named by the suite id.

Class assignment while `Antiphon.Tests` is still one assembly, in order:

1. Lane path above.
2. Else the first `layers.txt` production glob that matches a repo-relative string literal in the test file.
3. Else a committed namespace-prefix table `scripts/lib/rc-boundary-prefixes.json`, generated in comments from the layer names so a reviewer can see the fallback (`Antiphon.Tests.Infrastructure` is not specific enough and stays step 4).
4. Else `unassigned`.

`unassigned` is always dirty (D-4). Do not wait for CARD-1729's mirrored projects, CARD-1546 S3, CARD-1829, or CARD-1830. When a mirrored project lands, the boundary id stays the layer name so old rows still compare. CARD-1820's derived index is in Review and is not a prerequisite. A later card may replace step 2 with that index.

Stable chunk id: `b-{boundary}-{16 hex}` where the hex is the first 16 of SHA-256 over `suite`, `boundary`, and the sorted `class/method` keys. Order of packing does not change the id. A class that moves to another chunk changes both ids. Carry is per identity (D-4), so a repack does not by itself rerun an identity whose digest still matches.

Granularity inside a boundary stays the current packer: 900s target, a class over 2400s (1500s for `Antiphon.E2E`) splits by method, max 30 operands, prefix-unsafe names stay alone (`nightly-tests-impl.ps1` lines 205-223 and 361-396). The new step is to split the pool by boundary before `Split-NightlyItemsBySeconds`. More chunks cost a process start, not another suite build. On dce78ee0 the 131.056s build is copied onto all 1141 chunks; recorded fixture setup on the slow chunks is about 4-5s; discovery is null on every sidecar. Provisional extra-chunk cost is 5s until the desktop replay measures it.

The boundary pack is a parameter on `ConvertTo-NightlyProfileLaunchGroups`, default off. The production caller omits it until S6 turns it on for `Incremental` only.

Rejected: waiting for the csproj split (the operator's savings are available from the path map now); using today's `all-group-N` ids as the ledger key (they change when a neighbor class moves); one chunk per class (startup multiplies without a measured need; the 900s target already limits the bin).

### D-3 Input digest

Recommendation: hash git blob ids, not working-tree bytes.

For boundary B at SHA S, `git ls-tree -r -z S -- <paths>` and SHA-256 the sorted `mode type blob path` records. Paths are:

- B's own `layers.txt` globs, or the lane directory when B is a lane.
- The same for every layer B reaches through the edges (transitive). `git` includes `core`. `host` includes all seven. A lane that does not reference `server/Antiphon.Server.csproj` does not include those globs.
- The test files assigned to B.
- Shared inputs already treated as shared by `Get-RcProjectSelection` (the `shared-input:` regex at `rc-project-selection.ps1` line 233): `global.json`, NuGet config, `*.props`, `*.targets`, `*.sln`, `*.slnx`, `tests/*policy*.json`, `tests/rc-project-categories.json`, plus `scripts/lib/nightly-class-costs.json` and `scripts/lib/rc-boundary-prefixes.json`.
- The csproj blob ids of the ProjectReference closure when those projects exist. While the server is one csproj, the closure blob is included only for boundaries whose path set intersects that project's compiled inputs. A one-csproj server does not by itself dirty every boundary: the path globs do that job. An unreadable project file makes the digest `unknown`.

The contract hashes (`policyHash`, `catalogHash`, `selectorHash`, `runtimeHash`) are stored beside the digest, not folded into it, so a policy change is a forced Full (D-4) rather than a silent digest drift. Platform is not hashed in; it is a separate key. `unknown` if any required path is missing, a submodule is unresolved, or `ls-tree` fails.

Rejected: hashing file contents with a second git show (blob ids are the content hash); hashing the whole repo (every commit dirties every chunk).

### D-4 Dirty rules

An identity is clean only when every line holds:

- ledger row exists on the same platform
- `inputDigest` is 64 hex and equals the new digest
- outcome is `passed` or `expected-skip`
- the chunk that produced it completed (not interrupted)
- no forced-full reason is active
- a prior failure of this identity is proven by the CARD-1823 rule: the green source contains a fix SHA the failing source did not already contain (`merge-base --is-ancestor`, or a body line that is exactly `(cherry picked from commit <sha>)`). A same-tip green stays open. This plan calls that rule; it does not edit `merge-nightly-seeds.ps1`.

Otherwise dirty. Unknown is dirty. A missing row is `not-run`.

Outcomes that never carry: `failed`, `interrupted`, `not-run`, `unproven`, `unexpected-skip`. A `NotExecuted` row is `expected-skip` only when the current expected-platform-skips roster names that identity. Every other skip is `unexpected-skip`. CARD-1832's declarations replace that roster later inside `Test-RcExpectedSkip`. Until they land, the roster is the input. dce78ee0's twelve exit-8 chunks and the other `unexecuted-outcome` rows stay unexpected for carry purposes until that roster names them. This plan does not change `ConvertTo-NightlyCoverageVerdict`.

Flaky: a pass that follows a fail of the same identity at the same digest, with no new fix SHA, is `unproven` and dirty. A pass at a new digest can be clean.

Forced Full marks every identity dirty and records the reason. Triggers, any one:

- `policyHash`, catalog pin, `selectorHash`, or `runtimeHash` differs from the baseline contract
- the host SDK string recorded on the baseline ledger differs from this host
- seven days since the last Full on this platform (a Full is a closure whose identities were all executed on that run, none carried)
- the operator passes `-ForceFull`

A release-branch cut is not itself a forced Full of the next rc. The seven-day rule is what bounds how long carries can chain. Rejected: carry across Windows and Linux; carry an interrupted chunk's passed tests; treat the seed's "skip looks green" rule as ledger truth.

### D-5 Baselines and the release branch

Picker, first hit whose tip is an ancestor of the candidate (`merge-base --is-ancestor`, the same check as `release-selection.ps1` lines 202-206):

1. `baselineRef` recorded for this rc line, when it is `release/x.y.z` and a ledger exists for this platform.
2. Else the newest ancestor `release/x.y.z` by committer date, same platform ledger.
3. Else the previous candidate on the same `rc/YYYY-MM-DD` line that has a ledger. Carry only clean identities.
4. Else `baseline-absent` and Full, the current reason string.

A hotfix rc records `baselineRef=release/x.y.z` at cut time. A newer `release/x.y.(z+1)` that is not an ancestor does not steal it.

`scripts/cut-release-branch.ps1` pushes `refs/heads/release/<version>` at the closed SHA. Version matches `^[0-9]+\.[0-9]+\.[0-9]+$` and comes from `-Version` or the existing `releaseIntentPath`. If neither is set the script exits `release-version-required` and does not invent a number. It refuses when the Windows ledger is not a closure, when the ref exists at another SHA, and when asked to tag, publish, or promote. It does not call `publish-release.ps1` or `promote-release.ps1`. The nightly loop does not call it. A later qualification task runs it after the status page says closed. Windows ledger closure is what authorizes the branch. The Linux ledger is separate and neither blocks nor satisfies that cut. `gateCredit` stays false. The published archive reader is left in place for the human publish path.

Rejected: reusing `antiphon-full-release-baseline-v1` (it requires a published release); automatic branch creation inside the test loop (a remote ref write does not belong in a chunk); tagging from this script.

### D-6 Integration

`Incremental` composes with the existing pieces. It does not replace them.

- Failed-first launch order of dirty chunks only: ever-failed and not proven (CARD-1823), then ledger `failed`, then `interrupted`, then `not-run` / `unproven` / digest-changed. Clean identities are absent from the launch list and counted in the status page. Full runs keep `Resolve-NightlyStoppedSeedOrder`.
- Coverage: an incremental run is a qualifying closure when every required identity is `passed` or `expected-skip`, none are dirty, and `forcedFull` is empty. `summary.json` then records `requestedMode=Incremental`, `effectiveMode=Incremental`, `coverageScope=Full`. Anything short of that stays `coverageScope=IncrementalDiagnostic` and `coverageComplete=false`. `gateCredit` stays false either way. The branch script is the qualifying act short of human publish.
- Seeds: S6 reads the 1823 containment rule for "fix since failure". The exporter is unchanged. `-PriorFailuresDigest` still orders Full runs.
- Watchers: unchanged. They do not see carried chunks as process launches. The status page is how a human sees those rows.
- Project selection stays shadow-only. The ledger digest is the execution input, not `projectProposal`.

### D-7 Measurement

`scripts/lib/rc-replay.ps1` is a pure function. Input is a fixture document: chunk id, suite, class names, wall seconds, outcome, plus a boundary label per class, plus which boundaries changed on a recut. Output is dirty chunk count, carried chunk count, dirty wall, carried wall. Linux CP-9 runs that function on a small committed fixture shaped like the dce78ee0 ids (`all-group-19`, `all-group-108`), not on the live logs.

The desktop measurement is a later Custom task, read-only, after this plan has landed its replay function. It reads `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0` and the timing root named in the slow-chunk note, and it locates `6591e378` only by a directory listing under the release candidates root. It writes a gitignored report. It does not start, stop, or delete anything. It is not a checkpoint row because this host cannot see those directories.

### D-8 Switch and order against the other cards

`-SelectionMode Incremental` is off unless a caller passes it. S1-S5 and S8-S10 add files and do not change the launch loop. S4's packer parameter defaults off. S7 adds the mode to the `ValidateSet` and the picker; the default parameter stays `Full`.

Do this card before relying on CARD-1729 project extraction, CARD-1832 declarations, or CARD-1820's index. Do not start it against the checkout that is running `6591e378`. CARD-1823 S1 has already landed the proof rule this plan calls.

## Design choices already rejected in the decisions

The store, the id, the digest, the carry rule, and the branch writer are each chosen above with the alternative next to them. The short form: files on the release root, content-addressed chunk ids, blob-id digests, unknown means dirty, and a branch script that cannot publish.

## Existing tests affected

No migration, public DTO, HTTP route, operation label, remote `scripts/*.sh` function, or statement budget. The CARD-1782 impacted-classes table has no row for `scripts/lib/nightly-tests-impl.ps1` or `scripts/lib/release-selection.ps1`. None.

Later slices still pin these behaviours with new witnesses rather than by rerunning `RcFailedFirstExecutionTests.ActualLoopFollowsSchedule`:

- Full and ChangeAware still store `effectiveMode=Full` (`ReleaseSelection.cs` lines 70-86, `release-selection.ps1` lines 219-230).
- Default packing still emits `{source}-group-{n}` when the new parameter is omitted.
- `C1729LayeringGuardTests` is untouched because `layers.txt` is untouched.

## Tests

Harness shape matches `RcStoppedRunSeedTests`: `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, `ScriptHarness.RunHarnessCaseAsync`, one case name per method. Each new class gets `scripts/test-rc-<slice>.ps1`. Red commit is the C# method plus the harness case that fails because the function is absent. Green commit adds the function. Do not widen the 120s test timeout.

| Id | Witness | Mutant that must fail it |
|---|---|---|
| V-1 | `MissingDigestIsDirty` | Treating a missing digest as clean |
| V-2 | `UnexpectedSkipNeverCarries` | Carrying `NotExecuted` that the roster does not name |
| V-3 | `CrossPlatformNeverCarries` | Ignoring platform on carry |
| V-4 | `SameDigestCarriesProvenance` | Dropping `carriedFrom` |
| V-5 | `StableIdIgnoresOrder` | Numbering chunks by pack order |
| V-6 | `MovedClassChangesId` | Keeping the old id after a class leaves |
| V-7 | `UnassignedStaysAlone` | Packing an unlabeled class into `core` |
| V-8 | `TwoBoundariesNeverShare` | One bin with two boundary ids |
| V-9 | `ClosureBlobChangesDigest` | Hashing only the boundary's own paths |
| V-10 | `OutsideBlobKeepsDigest` | Hashing the whole tree |
| V-11 | `MissingPathIsUnknown` | Hashing a partial path list as if complete |
| V-12 | `MixedBoundaryRefusesOneBin` | Packing across boundaries when the switch is on |
| V-13 | `StatusCountsCarriedAndDirty` | Omitting a boundary from the page |
| V-14 | `PlanWritesNoLaunch` | Starting a test host from the plan writer |
| V-15 | `FailedIdentityLaunchesBeforeChanged` | Sorting a digest change ahead of a failure |
| V-16 | `CleanIdentityIsNotLaunched` | Emitting a clean identity in the launch list |
| V-17 | `FixPresentAtFailureStaysOpen` | Proving a same-tip green |
| V-18 | `AbsentBaselineStaysFull` | Switching the no-baseline path to Incremental |
| V-19 | `AncestorReleaseBeatsNewerLine` | Picking a newer release that is not an ancestor |
| V-20 | `CutRefusesTagPublishPromote` | Calling publish or promote, or creating a tag |
| V-21 | `OpenLedgerRefusesBranch` | Pushing a branch from a ledger that still has dirty rows |
| V-22 | `PolicyHashForcesFull` | Carrying across a policy hash change |
| V-23 | `SeventhDayForcesFull` | Carrying when the last Full is older than seven days |
| V-24 | `YoungerMatchStillCarries` | Forcing Full on a younger matching digest |
| V-25 | `OneBoundaryRecutSavesTheRest` | Charging every chunk when one boundary changed |
| V-26 | `UnknownBoundarySpendsAll` | Saving wall for an `unassigned` class |
| V-27 | `LedgerFormatSentencePresent` | Removing `antiphon-rc-ledger-v1` from `docs/release-gates.md` |
| V-28 | `PublishStaysHumanOnly` | Removing the human-only publish sentence |
| R-1 | `DefaultPackKeepsGroupIds` | Emitting boundary ids when the packer switch is omitted |
| R-2 | `FullAttemptStillEffectiveFull` | Dropping `change-aware-unqualified` or storing Incremental for ChangeAware |

Positive controls for Mutation, method-scoped, not run in Code: one per V row, the mutant in the table. No PC for the R rows beyond the red witness already in the row.

## Migrations

None. No database, no Down method.

## Slices

Each slice is one red commit and one green commit, about 60-120 minutes. Checkpoints are the Linux lane. Build output names avoid the substring `ref`.

### S1 Ledger predicate

New `scripts/lib/rc-ledger.ps1` and `scripts/test-rc-ledger.ps1`. Class `RcIncrementalLedgerTests`. Implement the schema writer and the clean/dirty predicate only. No packer, no git, no launch.

### S2 Boundary ids

New `scripts/lib/rc-boundary.ps1`, `scripts/lib/rc-boundary-prefixes.json`, `scripts/test-rc-boundary.ps1`. Class `RcBoundaryChunkIdTests`. Parse `layers.txt`. Assign the four steps. Stable id. Do not edit `layers.txt`.

### S3 Digest

New `scripts/lib/rc-chunk-digest.ps1` and `scripts/test-rc-chunk-digest.ps1`. Class `RcChunkInputDigestTests`. Fixture git repo inside the harness temp dir. `ls-tree` only.

### S4 Boundary pack, default off

Edit `ConvertTo-NightlyProfileLaunchGroups` to take an optional boundary label per class. Default path unchanged. New `scripts/test-rc-boundary-pack.ps1`. Class `RcBoundaryPackTests`.

### S5 Status page

New `scripts/rc-ledger-plan.ps1`, `scripts/rc-ledger-status.ps1`, `scripts/test-rc-ledger-status.ps1`. Class `RcLedgerStatusTests`. Writes under a temp release root. Does not launch.

### S6 Launch order

New `scripts/lib/rc-dirty-order.ps1` and `scripts/test-rc-dirty-order.ps1`. Class `RcDirtyLaunchOrderTests`. Uses the 1823 containment test. Does not change `Resolve-NightlyStoppedSeedOrder`. The production nightly loop calls the new order only when `SelectionMode` is `Incremental`.

### S7 Baseline and branch script

New `scripts/lib/rc-baseline.ps1`, `scripts/cut-release-branch.ps1`, `scripts/test-rc-baseline.ps1`. Class `RcReleaseBaselineTests`. Add `Incremental` to the `release-cut.ps1` `ValidateSet`. Default stays `Full`. Tests use a fixture repo and never push.

### S8 Forced Full

Extend `rc-ledger.ps1`. New cases on class `RcForcedFullTests` in `scripts/test-rc-forced-full.ps1`. Seven days is a named constant `RcLedgerFullIntervalDays = 7`.

### S9 Replay

New `scripts/lib/rc-replay.ps1`, `scripts/fixtures/rc-replay/dce-shaped.json`, `scripts/test-rc-replay.ps1`. Class `RcIncrementalReplayTests`. Document the desktop Custom task in the script comment. Do not run it.

### S10 Doc pins

Add a subsection under `docs/release-gates.md` "Cutting, testing and publishing a candidate" (line 524) naming `antiphon-rc-ledger-v1`, the Incremental switch defaulting off, and that tag, publish, and promote stay human-only. Add a short paragraph under `docs/testing-and-build.md` "Nightly" (line 1514) that the ledger is release-root files and that a seed skip is not a ledger carry. Class `RcLedgerDocPinTests` reads those sentences.

## Verification design

Lane: Linux. One isolated build per slice. Later rows in a slice do not appear because each slice is one filter. Provisional `EstimatedMinutes` 4 is about twice a two-minute harness guess; no measured p95 exists for these new classes. The 120_000ms test timeout stays. Do not set `totalTimeoutMinutes` below the derived total.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1836-s1/` | ledger | `/*/*/RcIncrementalLedgerTests/(MissingDigestIsDirty*)\|(UnexpectedSkipNeverCarries*)\|(CrossPlatformNeverCarries*)\|(SameDigestCarriesProvenance*)` | V-1 V-2 V-3 V-4 | all listed, 0 failed | 4 | 4 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1836-s2/` | boundary | `/*/*/RcBoundaryChunkIdTests/(StableIdIgnoresOrder*)\|(MovedClassChangesId*)\|(UnassignedStaysAlone*)\|(TwoBoundariesNeverShare*)` | V-5 V-6 V-7 V-8 | all listed, 0 failed | 4 | 4 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1836-s3/` | digest | `/*/*/RcChunkInputDigestTests/(ClosureBlobChangesDigest*)\|(OutsideBlobKeepsDigest*)\|(MissingPathIsUnknown*)` | V-9 V-10 V-11 | all listed, 0 failed | 3 | 4 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1836-s4/` | pack | `/*/*/RcBoundaryPackTests/(MixedBoundaryRefusesOneBin*)\|(DefaultPackKeepsGroupIds*)` | V-12 R-1 | all listed, 0 failed | 2 | 4 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1836-s5/` | status | `/*/*/RcLedgerStatusTests/(StatusCountsCarriedAndDirty*)\|(PlanWritesNoLaunch*)` | V-13 V-14 | all listed, 0 failed | 2 | 4 |
| CP-6 | S6 | `tests/Antiphon.Tests -> bin-c1836-s6/` | order | `/*/*/RcDirtyLaunchOrderTests/(FailedIdentityLaunchesBeforeChanged*)\|(CleanIdentityIsNotLaunched*)\|(FixPresentAtFailureStaysOpen*)` | V-15 V-16 V-17 | all listed, 0 failed | 3 | 4 |
| CP-7 | S7 | `tests/Antiphon.Tests -> bin-c1836-s7/` | baseline | `/*/*/RcReleaseBaselineTests/(AbsentBaselineStaysFull*)\|(AncestorReleaseBeatsNewerLine*)\|(CutRefusesTagPublishPromote*)\|(OpenLedgerRefusesBranch*)\|(FullAttemptStillEffectiveFull*)` | V-18 V-19 V-20 V-21 R-2 | all listed, 0 failed | 5 | 6 |
| CP-8 | S8 | `tests/Antiphon.Tests -> bin-c1836-s8/` | forced | `/*/*/RcForcedFullTests/(PolicyHashForcesFull*)\|(SeventhDayForcesFull*)\|(YoungerMatchStillCarries*)` | V-22 V-23 V-24 | all listed, 0 failed | 3 | 4 |
| CP-9 | S9 | `tests/Antiphon.Tests -> bin-c1836-s9/` | replay | `/*/*/RcIncrementalReplayTests/(OneBoundaryRecutSavesTheRest*)\|(UnknownBoundarySpendsAll*)` | V-25 V-26 | all listed, 0 failed | 2 | 4 |
| CP-10 | S10 | `tests/Antiphon.Tests -> bin-c1836-s10/` | docs | `/*/*/RcLedgerDocPinTests/(LedgerFormatSentencePresent*)\|(PublishStaysHumanOnly*)` | V-27 V-28 | all listed, 0 failed | 2 | 4 |

## Performance budget

These checkpoints do not boot AppHost, Postgres, or a browser. Each class builds one temp git repo or temp release root in the harness, once per case, and then runs the function. No shared container.

Durations are provisional. There is no measured p95 for the new classes. The nearest neighbour is a stopped-seed harness case, budgeted at 90s inside a 120s test timeout (`RcStoppedRunSeedTests` lines 11-14). A four-method row is estimated at 4 minutes, about twice a two-minute guess, and under the importer's 15-minute advisory. CP-7 is 6 minutes because it has five cases and a fixture repo. The first green run of each CP records the harness seconds in the checkpoint report. A later slice does not widen the 120_000ms timeout unless that recorded p95 says the 2x bound exceeds it, and then it names this card.

Phase budget per case: process start under 5s, fixture git init under 2s, the function under 10s, teardown under 2s. Unknown phases stay null, matching the timing recorder. No row is expected to pass 30 minutes, so no Debug task is owed for the checkpoint itself.

Qualification cost this plan is designing, not running: dce78ee0 was 60109s with 1139 groups and one 131s suite build shared across chunks. Boundary packing must not multiply that build. Extra chunks are process starts, provisionally 5s, to be replaced by the desktop replay's measured difference. The 900s pack target and the 2400s lone-class budget stay. The seven-day Full is the periodic whole-run cost, not an every-rc cost.

Recording: ledger `durationSeconds` per identity and chunk, plus the existing timing root for launched chunks. Carried rows copy the source duration and set `carriedFrom`. The status page prints carried wall separately from launch wall.

## Risks

- A carry hides a skip the roster forgot. Mitigation: unknown and unexpected-skip are dirty, and the seven-day Full re-executes.
- Boundary mis-label puts a class in `unassigned` and it reruns every rc. That is the safe direction. The status page lists `unassigned` counts so a follow-up can label them.
- S4 could change default group ids. R-1 fails if `{source}-group-{n}` changes when the switch is omitted.
- `cut-release-branch.ps1` could be pointed at a tag. V-20 refuses tag, publish, and promote.
- The desktop replay could disturb `6591e378` if it writes under the run directory. The task is read-only and writes only a gitignored report outside that directory.

## Operator-only steps

Tag, publish, and promote stay human-only. The branch script refuses them. Choosing the `x.y.z` string is an operator input (`-Version` or `releaseIntentPath`); the script does not invent one. No open question remains under these defaults.

## Owed on Windows

Nothing in CP-1 through CP-10. The desktop Custom task in D-7 is the native measurement: replay dce78ee0 and `6591e378` with the landed `rc-replay.ps1`, read-only, and record dirty chunks and wall saved per recut. The Windows ledger closure is what a later run of `cut-release-branch.ps1` requires. This plan does not start that run.
