# CARD-1836 remaining chain: status, order, ready briefs

Date: 2026-11-19. Investigate task `1800c3c0`. Read-only except this note.
Inspected source: `origin/master` `5f0804e425384f8b03f88b833fbdad0e93f966d3` (2026-11-19 19:41:57 +0000), which is also this branch's base. No build, test, qualification, deploy, or restart. Live Windows run `e7f92f66` was not opened.

**Confirmed.** The activation plan's libraries through S5c are on that master and are inert: they do not select, launch, close, or publish. S6a, S8b, S6b, S6c, S6d, S7b, S7c, S7d, and S7e have no file and no commit on any ref in this checkout. The 2026-11-19 tag-backing predicate (`Test-RcTagBacking`) is also absent. With the committed owner map, a real recut has nothing to carry, so two trusted shadow cuts need that map as well as the wiring.

Card `03408341-e38e-4dce-bdef-932c1c5e1ff0`, InProgress, Critical / Now, rank 0. History revisions 1–33 are dispatch and settle records plus one operator edit (revision 11, 2026-11-12): top priority for incremental qualification because a full rc was taking 17 h. No later card revision records a new product decision. Decisions below are the ones still open in the activation plan and the 2026-11-19 qualification plan.

## 1. Status of every activation-plan slice

Land SHAs are the introducing commits on `origin/master` (`git log origin/master`). Each row was checked by reading the cited lines at `5f0804e425384f8b03f88b833fbdad0e93f966d3`. "No branch" means `git log --all` has no commit that adds the slice's planned paths. This mirror listed 9201 refs; a remote head never fetched here is UNKNOWN.

| Slice | Status | Land commit and date | What is on master at `5f0804e4` |
|---|---|---|---|
| S1 ledger predicate | Landed | `81a03643086c666b91364b85072e32dc427a18bf` 2026-11-10 17:46:38 +0000. Strict reader `4e3120b9df5ed45d8426a26c20938082555908fa` the same minute. CARD-1842 durations `306a5ec42f38765f15dd3e98134309002f79b5cb` 2026-11-12 00:41:28 +0000 | `scripts/lib/rc-ledger.ps1:1-6` schema v1/v2, default-deny reader. `Get-RcIdentityCarryDecision` at `:290-336` (forced-full, unknown boundary, missing row, cross-platform, digest, outcome, chunk, flake, provenance) |
| S2a boundaries | Landed | `0241881c17cf614219c33176575ee37c7a1fdae9` 2026-11-13 02:21:24 +0000. Later on master: full-digest collision refusal `a4af3cc329eb5326dafa0241ee3999d0fae3094b` 2026-11-13 06:38:45; CARD-1878 `42b4f6975a431638dd769abbff6be83f5f233ab4` 2026-11-17 09:03:25 | `scripts/lib/rc-boundary.ps1`. `Get-RcBoundaryChunkId` at `:231`. `alwaysDirty` when boundary is `unassigned` at `:228`. Witnesses in `RcBoundaryChunkIdTests` (11 methods; names grew past the plan's original three) |
| S3a input inventory | Landed | `1d240a77fe7023428b05f8f82688bd888701f6fa` 2026-11-13 04:49:53 +0000. Positive-index union `1330c24e3321ce2143c25a89c6bfcca21aeea7aa` same minute | `scripts/lib/rc-input-manifest.ps1:1-2` "never carry authority". `RcInputManifestTests` has the four plan witnesses. Owner map is still one root |
| S5a historical extract | Landed | `8e70e74dcfeedbde44a9449d8f5b85e00a83ede7` 2026-11-13 12:22:11 +0000 | `scripts/lib/rc-historical-stream.cs`, `scripts/lib/rc-historical-evidence.ps1`. `RcHistoricalEvidenceTests`: `LargeSummaryIsBounded`, `TamperAndChangingReadRefuse`, `DuplicateTraversalAndTornInputRefuse` |
| S8a forced Full | Landed | `8e182a963727d4baf43b841c88ee575066719796` 2026-11-13 07:14:15 +0000 | `Get-RcForcedFullDecision` at `scripts/lib/rc-forced-full.ps1:73-124`. Contract-field mismatch, missing/expired/future anchor, and `shadow-discrepancy` append reasons. `RcForcedFullTests` has the three plan witnesses |
| S3b digests | Landed | `64efb25cf29127fe4d8bb3bc8bfe5b01a11d1111` 2026-11-13 11:42:53 +0000 | `scripts/lib/rc-chunk-digest.ps1:1-2` "never execution/carry authority". `RcChunkInputDigestTests` has the four plan witnesses plus later case-key methods |
| S5b admission | Landed, inert | `704820c433d40b940e80b5ca23ae36dfe733a1f0` 2026-11-13 14:27:54 +0000. Stage-output recheck `2e2fcbc3e93ecc7a3d0fb9c627ef9c9ecc560738` same minute | `scripts/rc-ledger-import.ps1:1-3` and `:26-27`: no launch; caller discovery is an obligation; passed rows are not written. `Read-RcAdmission` returns `gateCredit=false` at `scripts/lib/rc-admission.ps1:116-118`. `ImportIsInertAndWritesUnprovenShard` is a witness |
| S7a baseline picker | Landed, no authority | `d7c793f191d408dc166064e4fa92d3e33b19e366` 2026-11-15 04:33:26 +0000. Ref recheck `9af3e309a3457da13969b51e987b18e4e5b8bb01` 2026-11-15 04:33:27 | `scripts/lib/rc-baseline.ps1:1-2` "never execution/carry authority". `Get-RcReleaseBaseline` at `:31`. `RcReleaseBaselineTests` has the three plan witnesses |
| S4a boundary pack | Landed, opt-in | `97650da5e971dbd02ec55f15c84f45c173ad029c` 2026-11-13 10:11:24 +0000. Discovery indexed once in `4779d8a101a660c97fd1b54743cee7b09187455f` 2026-11-13 16:33:09 | `ConvertTo-NightlyBoundaryLaunchGroups` at `scripts/lib/nightly-tests-impl.ps1:439-441` "grants no carry credit". `-BoundaryAware` at `:450-454` delegates to that function. The production caller `Get-NightlyProfileLaunchPlan` at `:481` calls `ConvertTo-NightlyProfileLaunchGroups` with no `-BoundaryAware`. The compiled planner `scripts/lib/Antiphon.Nightly/LaunchPlanner.cs:1-3` is that non-boundary body (`33323be986c7cedf60b806825ee4e4a43d905ce7` 2026-11-19 15:31:46) |
| S5c exact plan and status | Landed, diagnostic | `a305f6a8b01a062b2873f5a259693aacd9f8525d` 2026-11-17 00:00:38 +0000. Revision-outcome fix `1cc52abadc986f9f6adc7f2261e28e90c5132eda` same minute | `scripts/lib/rc-incremental-plan.ps1:2` "No scheduler, activation or closure caller". Published plan at `:218-221` is `effectiveMode=Full`, `coverageComplete=false`, `gateCredit=false`, `bindingState=diagnostic-only`. `RcLedgerStatusTests` has the four plan witnesses |
| S6a dirty launch order | Not started | No commit. No branch | `scripts/lib/rc-dirty-order.ps1`, `scripts/test-rc-dirty-order.ps1`, `RcDirtyLaunchOrderTests` absent |
| S8b sample and trust state | Not started | No commit. No branch | `scripts/lib/rc-shadow-sample.ps1`, `scripts/test-rc-shadow-sample.ps1`, `RcIncrementalShadowTests` absent |
| S6b run evidence | Not started | No commit. No branch | `scripts/lib/rc-run-evidence.ps1`, `scripts/test-rc-run-evidence.ps1`, `RcRunEvidenceTests` absent. No writer of source/build seals in the qualification path |
| S6c scheduler | Not started | No commit. No branch | `scripts/test-rc-incremental-dispatch.ps1`, `RcIncrementalDispatchTests` absent. `scripts/lib/nightly-dispatch.ps1` has no incremental schedule |
| S6d closure | Not started | No commit. No branch | `scripts/lib/rc-closure.ps1`, `scripts/test-rc-incremental-closure.ps1`, `RcIncrementalClosureTests` absent |
| S7b selection hops | Not started | No commit. No branch | `Incremental` is absent from every entrypoint ValidateSet (section 3). `RcIncrementalHopTests` absent |
| S7c front door | Not started | No commit. No branch | `scripts/release-cut.ps1:184-193` still calls `publish-release.ps1` after a green rc unless `-SkipPublish` or `-WhatIf`. `RcIncrementalEntryTests` absent. `docs/release-gates.md` has no incremental/ledger-basis section |
| S7d native proof | Not started | No commit. No branch | `scripts/test-rc-incremental-native.ps1`, `RcIncrementalNativeTests` absent. Desktop proof has not run. UNKNOWN from this host |
| S7e release branch | Not started. Off the first-cut path | No commit. No branch | `scripts/cut-release-branch.ps1`, `RcLedgerReleaseBranchTests` absent |

Adjacent, not an activation-plan slice: the 2026-11-19 plan's selector-soundness replay is on master as `IncrementalSelectionSoundnessTests` (`61eec38f922d86d59c59e75f69d7a9fd44c9f493` 2026-11-19 16:26:13 +0000, red parent `d63fce0464007c06a358f06957ce2632101566b8`). It reads the committed `**` map and a proposed project closure. It is not carry authority. The 2026-11-19 plan's S1 carry-replay, S3 owner-map replacement, S4 class census, and S5 tag backing are absent (no `rc-carry-replay.ps1`, `RcInputOwnersMapTests`, `rc-class-census.ps1`, or `rc-tag-backing.ps1`).

## 2. Dependency order, parallelism, desktop, live run

Remaining activation order, from the activation plan's dependency table. Inputs S4a, S5b, S5c, and S8a are on master, so the first three units are unblocked.

```
S6a ──┐
S8b ──┼── S6c ── S6d ── S7b ── S7c ── S7d ── S7e
S6b ──┘
```

S6b does not depend on S6a or S8b. S6c depends on all three. S7e is not required for the first two shadow cuts (activation plan: "off the first-cut critical path").

### File-collision matrix

`tests/derived-impact-index.json` is the shared generated file. Every new test class changes it. Authoring can proceed in parallel; landing regenerates the index on the moved master instead of hand-merging it. That file is omitted from the cells below.

| Slice | Files it creates or edits | Collides with |
|---|---|---|
| S6a | `scripts/lib/rc-dirty-order.ps1`, `scripts/test-rc-dirty-order.ps1`, `tests/Antiphon.Tests/Scripts/RcDirtyLaunchOrderTests.cs` | none of the other remaining slices |
| S8b | `scripts/lib/rc-shadow-sample.ps1`, `scripts/test-rc-shadow-sample.ps1`, `tests/Antiphon.Tests/Scripts/RcIncrementalShadowTests.cs` | none |
| S6b | those two new scripts plus `RcRunEvidenceTests.cs`; edits `scripts/lib/nightly-tests-impl.ps1` and `scripts/lib/nightly-run-impl.ps1` | S6c, S6d, S7b (same two nightly files) |
| S6c | `scripts/test-rc-incremental-dispatch.ps1`, `RcIncrementalDispatchTests.cs`; edits `scripts/lib/nightly-dispatch.ps1` and `nightly-tests-impl.ps1` | S6b, S6d, S7b |
| S6d | `scripts/lib/rc-closure.ps1`, `scripts/test-rc-incremental-closure.ps1`, `RcIncrementalClosureTests.cs`; edits `nightly-tests-impl.ps1`, `scripts/lib/nightly-coverage.ps1`, `nightly-run-impl.ps1` | S6b, S6c, S7b |
| S7b | `scripts/test-rc-incremental-hops.ps1`, `RcIncrementalHopTests.cs`; edits `scripts/release-cut.ps1`, `scripts/nightly-run.ps1`, `scripts/nightly-tests.ps1`, `nightly-run-impl.ps1`, `nightly-tests-impl.ps1`, `scripts/lib/release-selection.ps1`, `scripts/lib/Antiphon.Scripts/ReleaseSelection.cs` | S6b–S6d on the nightly files; S7c on `release-cut.ps1` |
| S7c | `scripts/test-rc-incremental-entry.ps1`, `RcIncrementalEntryTests.cs`; edits `scripts/release-cut.ps1`, `docs/release-gates.md`, `docs/testing-and-build.md` | S7b on `release-cut.ps1`; S5 and S7e on `docs/release-gates.md` |
| S7d | `scripts/test-rc-incremental-native.ps1`, `RcIncrementalNativeTests.cs` | no shared production file. Desktop execution |
| S7e | `scripts/cut-release-branch.ps1`, `scripts/test-rc-release-branch.ps1`, `RcLedgerReleaseBranchTests.cs`; extends `scripts/lib/rc-baseline.ps1`; edits `docs/release-gates.md` | S7c and S5 on the doc; anyone else editing `rc-baseline.ps1` |
| S5 tag backing (2026-11-19 plan, not an activation slice) | new compiled type under `scripts/lib/Antiphon.Nightly/`, thin `scripts/lib/rc-tag-backing.ps1`, `RcTagBackingTests.cs`, `docs/release-gates.md` | S7c and S7e on the doc. File-disjoint from S6a, S8b, and S6b |
| 2026-11-19 S3 owner map (not in the wiring list; required for real carries) | edits `scripts/lib/rc-input-owners.json` and root resolution in `scripts/lib/rc-input-manifest.ps1`; adds `RcInputOwnersMapTests.cs` | none of S6a, S8b, S6b, S5 |

### What can run on server2 today

Parallel, one writer each, from `5f0804e425384f8b03f88b833fbdad0e93f966d3`:

- S6a
- S8b
- S6b (the only writer of `nightly-tests-impl.ps1` and `nightly-run-impl.ps1` in this wave)
- S5 tag backing (the only writer of `docs/release-gates.md` in this wave)

S6c starts only after S6a, S8b, and S6b have landed. Then S6d, S7b, and S7c are one writer at a time on the shared nightly and release-cut files. S7e waits for S7d and a trusted closure.

The 2026-11-19 owner-map slice is file-disjoint from that first wave. It is the map that makes a later shadow cut able to carry anything. See section 7.

### Desktop

Only S7d needs the desktop, and only after the server2 chain is reviewed and integrated. The proof is a fixture-owned miniature candidate. It does not read the live run. S7e's later real ref creation is a separate authorized operation; the slice's own proof is a local bare repository and can be Linux once commissioned. No remaining server2 slice needs Windows.

### Live run `e7f92f66`

Named, not accessed, in `docs/investigations/2026-11-19-nightly-harness-audit-astra.md:16`: full id `e7f92f66f30b495bab0683f765c0f91c`, started 2026-11-19 14:30Z, directory `C:\Antiphon\releases\candidates\rc-2026-11-29-26a99f1a747d\logs\2026-11-19-1430-e7f92f66`. `origin/rc/2026-11-29` is `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`, an ancestor of master `5f0804e4`. Whether the run is still executing, how many chunks remain, and whether it will finish green are UNKNOWN here.

What each remaining slice needs from that run: nothing. S6a through S7c and S5 are fixture work. S7d must not use that directory, the production release root, the session runner, or the broker. S7d also must not start while that run owns the desktop.

What the run cannot be, even if it finishes green: the Full anchor for the wired candidate. Wiring changes the execution scripts. `Get-RcForcedFullDecision` treats a changed `executionImplementation`, `harnessHash`, `launcherHash`, `coverageHash`, `inputMapHash`, or `inputMapVersion` as forced Full (`scripts/lib/rc-forced-full.ps1:81-91`). The activation plan's D-5 has no semantic-equivalence waiver. A green `e7f92f66` stays evidence of the pre-wiring tree.

## 3. Shadow-mode switch, shared by every brief

There is no Incremental switch on master. These are the off proofs at `5f0804e4`:

- `scripts/release-cut.ps1:30`, `scripts/nightly-run.ps1:91`, and `scripts/nightly-tests.ps1:37` accept only `Full`, `Shadow`, `ChangeAware`. `Shadow` here is the existing selector proposal. The activation plan's `IncrementalValidation=Shadow|Trusted` does not exist in code.
- `scripts/lib/release-selection.ps1:229` writes `effectiveMode=Full` and `executionEvidence=false`. `:374-375` throws `selection-hop-scope` unless the attempt and the selection are Full. `:396-397` returns the full membership when the attempt is Full, and throws `selected-execution-unqualified` otherwise.
- `scripts/lib/Antiphon.Scripts/ReleaseSelection.cs:105-106` hardcodes `effectiveMode` and `coverageScope` to `Full`.
- `Get-NightlyProfileLaunchPlan` does not pass `-BoundaryAware` (`scripts/lib/nightly-tests-impl.ps1:481`).
- Diagnostic plans set `gateCredit=false` and `bindingState=diagnostic-only` (`scripts/lib/rc-incremental-plan.ps1:218-221`). Admission eligibility sets `gateCredit=false` (`scripts/lib/rc-admission.ps1:116-118`).
- `Get-ReleaseGateCreditVerdict` refuses a present `coverageScope` other than `Full` (`scripts/lib/release-gate.ps1:233-236`).
- A green rc still reaches `publish-release.ps1` unless `-SkipPublish` or `-WhatIf` (`scripts/release-cut.ps1:184-193`). S7c is the slice that must refuse that call for Incremental. Until S7c lands, staying off means not adding a mode and not treating a diagnostic plan as qualification success.

Every slice below keeps `SelectionMode` default `Full`, writes no trust record, leaves `gateCredit=false`, and does not call `publish-release.ps1`. `IncrementalValidation` defaults to Shadow. Trusted is an operator record after two eligible shadow cuts, not a flag a slice turns on. Checkpoint rows are the activation plan's closed table (`docs/superpowers/plans/2026-11-12-card-1836-incremental-activation-plan.md:351-373`). `--after <slice>` also selects CP-90 because a row whose After is `all` matches every selection (`tools/Antiphon.Checkpoints/Manifest/AfterSelector.cs:24-25`). The later S5c checkpoint file widened CP-90; the activation-plan CP-90 filter is the one cited here. Budgets below are the plan's provisional minutes, not measured p95.

## 4. Ready briefs

### S6a — dirty launch order

- Goal: compute launch set `L` in failed, interrupted, not-run, then changed order, preserving sample membership. Consume cumulative failure containment. Leave the existing Full seed function in place.
- Files: `scripts/lib/rc-dirty-order.ps1`, `scripts/test-rc-dirty-order.ps1`, `tests/Antiphon.Tests/Scripts/RcDirtyLaunchOrderTests.cs`.
- Red witnesses (V-10): `OnlyDirtyAndSampleLaunch`, `OpenFailurePrecedesChanged`, `SameTipPassCannotClearFailure`. Regression row R-3: existing `RcFailureHistoryTests`.
- Checkpoints: CP-10, filter `/*/*/(RcDirtyLaunchOrderTests*)|(RcFailureHistoryTests*)/*`, Min 3, EstimatedMinutes 8, build `tests/Antiphon.Tests -> bin-c1836-s6a/`, plus CP-90.
- Master first: S5c and S4a, both present at `5f0804e4`.
- Risks: a witness that fails because the function is missing is not the required red; the assertion has to fail against the old default-deny behavior. Do not edit `nightly-tests-impl.ps1` in this slice.
- Shadow: library only. No caller in `nightly-run.ps1` or `release-cut.ps1`. No trust transition.

### S8b — sample selection and trust state

- Goal: D-8 sample `N = min(Cchunk, max(30, ceil(0.05 * Cchunk)))`, stratified by suite and boundary, fixed seed stored before launch, deterministic replay. Trust predicates that refuse a changed contract and refuse trust from zero carries or one pilot.
- Files: `scripts/lib/rc-shadow-sample.ps1`, `scripts/test-rc-shadow-sample.ps1`, `tests/Antiphon.Tests/Scripts/RcIncrementalShadowTests.cs`.
- Red witnesses (V-11): `SampleSizeStrataAndReplay`, `MismatchInvalidatesCarries`, `NoCarryAndOnePilotDoNotGrantTrust`.
- Checkpoints: CP-11, filter `/*/*/RcIncrementalShadowTests/*`, Min 3, EstimatedMinutes 6, build `bin-c1836-s8b/`, plus CP-90.
- Master first: S5c and S4a, both present.
- Risks: a test that draws until it likes the sample violates D-8. `Cchunk=0` must report `sample=not-applicable` and must not count as a shadow pilot.
- Shadow: persist a proposal. Do not write a trust record and do not flip `IncrementalValidation`.

### S6b — prospective build and source evidence

- Goal: source and input snapshots, output seals, and immutable per-chunk revisions, including during an explicit Full bootstrap. Revalidate source, build, and output at admission and closure. One build and one discovery per suite. Carry does not skip current discovery.
- Files: `scripts/lib/rc-run-evidence.ps1`, `scripts/test-rc-run-evidence.ps1`, `tests/Antiphon.Tests/Scripts/RcRunEvidenceTests.cs`, and edits to `scripts/lib/nightly-tests-impl.ps1` and `scripts/lib/nightly-run-impl.ps1`.
- Red witnesses (V-12): `BuildSourceAndLaunchOutputMustJoin`, `ChangedInputsDuringRunRefuse`, `InterruptedWriterLeavesNotRun`, `OneBuildAndDiscoveryPerSuite`. Regression R-4: `RcFullTimingContractTests`, `NightlyCoreTests`, `LiveLaneRunnerTests`.
- Checkpoints: CP-12, filter `/*/*/(RcRunEvidenceTests*)|(RcFullTimingContractTests*)|(NightlyCoreTests*)|(LiveLaneRunnerTests*)/*`, Min 4, EstimatedMinutes 12, build `bin-c1836-s6b/`, plus CP-90.
- Master first: S5b and S5c, both present. The non-boundary compiled planner is already in `LaunchPlanner.cs`; this slice edits the PowerShell evidence boundary, not that planner's pack order.
- Risks: sole writer of the two nightly impl files in the first wave. A later master commit on those files rebases at land. Do not change native custody. Do not point a snapshot at `C:\Antiphon\releases\candidates\rc-2026-11-29-26a99f1a747d\`.
- Shadow: evidence is recorded for Full and for later Incremental. Recording it grants no gate credit and does not change `effectiveMode`.

### S6c — scheduler uses L

- Goal: a separate verified Incremental schedule whose permutation equals `L`. The existing Full schedule guard stays exact. All-carried suites complete without a fake process.
- Files: `scripts/test-rc-incremental-dispatch.ps1`, `tests/Antiphon.Tests/Scripts/RcIncrementalDispatchTests.cs`; edits `scripts/lib/nightly-dispatch.ps1` and `scripts/lib/nightly-tests-impl.ps1`.
- Red witnesses (V-13): `ActualScheduleOmitsOnlyVerifiedCarries`, `MissingDuplicateOrAddedLaunchRefuses`, `AllCarriedSuiteCompletesWithoutFakeLaunch`. Regression R-5: `NightlyCoreTests`, `LiveLaneRunnerTests`. Native `ActualLoopFollowsSchedule` and `JournalCustodySurvivesInterleaving` stay on CP-18, not this Linux row.
- Checkpoints: CP-13, filter `/*/*/(RcIncrementalDispatchTests*)|(NightlyCoreTests*)|(LiveLaneRunnerTests*)/*`, Min 3, EstimatedMinutes 10, build `bin-c1836-s6c/`, plus CP-90.
- Master first: landed S6a, S6b, and S8b.
- Risks: shares `nightly-tests-impl.ps1` with S6b and S6d. Serialize. Fixture launch delegates only; no live run.
- Shadow: the Incremental schedule is reachable from the library under test. The production entrypoints still have no Incremental ValidateSet until S7b. Default nightly path stays Full.

### S6d — ledger closure

- Goal: exact universe partition at terminal close, prospective output checks, Linux kept diagnostic. A sample failure or a Linux result cannot claim Windows. A Full fallback resets the anchor only after that Full actually executes.
- Files: `scripts/lib/rc-closure.ps1`, `scripts/test-rc-incremental-closure.ps1`, `tests/Antiphon.Tests/Scripts/RcIncrementalClosureTests.cs`; edits `nightly-tests-impl.ps1`, `scripts/lib/nightly-coverage.ps1`, `nightly-run-impl.ps1`.
- Red witnesses (V-14): `CarryPlusExecutedExactlyCloses`, `MissingCaseOrUnexpectedSkipKeepsOpen`, `SampleFailureAndLinuxCannotClaimWindows`, `FullFallbackResetsAnchorOnlyAfterExecution`. Regression R-6: `RcCoverageAdmissionTests`, `ExpectedPlatformSkipRosterTests`, `ReleaseGatePlatformTests`, `NightlyCoreTests`, `LiveLaneRunnerTests`.
- Checkpoints: CP-14, filter `/*/*/(RcIncrementalClosureTests*)|(RcCoverageAdmissionTests*)|(ExpectedPlatformSkipRosterTests*)|(ReleaseGatePlatformTests*)|(NightlyCoreTests*)|(LiveLaneRunnerTests*)/*`, Min 4, EstimatedMinutes 12, build `bin-c1836-s6d/`, plus CP-90.
- Master first: landed S6c. S8a is already on master.
- Risks: do not modify publication policy here. `coverageScope=IncrementalDiagnostic` and `gateCredit=false` stay. A Windows `effectiveMode=Incremental` with `coverageBasis=ledger-v1` is qualification coverage, and `Get-ReleaseGateCreditVerdict` still refuses a non-Full scope.
- Shadow: closure of a shadow run records discrepancy or carries. It does not publish and does not set Trusted.

### S7b — wire selection hops

- Goal: thread the immutable plan path, digest, and validation mode through `release-cut.ps1`, `nightly-run.ps1`, `nightly-tests.ps1`, both impl scripts, `release-selection.ps1`, and `ReleaseSelection.cs`. The compiled selector returns NotHandled for Incremental so the PowerShell path stays the only admission implementation. Full stays Full.
- Files: the seven production files in the matrix row, plus `scripts/test-rc-incremental-hops.ps1` and `tests/Antiphon.Tests/Scripts/RcIncrementalHopTests.cs`. Update forwarding tests that pin the ValidateSet (`ReleaseSelectionCliTests`, `ReleaseSelectionHopSeedTests`).
- Red witnesses (V-15): `PlanBindingSurvivesEveryHop`, `ForgedOrDroppedPlanRefuses`, `CompiledIncrementalFallsBackAndFullStaysFull`. Regression R-7: those two selection classes plus `NightlyCoreTests` and `LiveLaneRunnerTests`.
- Checkpoints: CP-15, filter `/*/*/(RcIncrementalHopTests*)|(ReleaseSelectionCliTests*)|(ReleaseSelectionHopSeedTests*)|(NightlyCoreTests*)|(LiveLaneRunnerTests*)/*`, Min 3, EstimatedMinutes 12, build `bin-c1836-s7b/`, plus CP-90.
- Master first: landed S6d. S8a already present.
- Risks: this is the first slice that makes Incremental a real mode string. Default parameter values stay `Full`. A forged or dropped plan refuses. CARD-1820 selector files (`release-impact.ps1`, `impacted-classes.ps1`) stay out of this edit.
- Shadow: `IncrementalValidation` default Shadow. The compiled path must not grow a second carry implementation. No trust record.

### S7c — front door and runbook

- Goal: Incremental qualification never calls the publisher, including when `-SkipPublish` was omitted. Preflight prints fallback before launch. A moved candidate refuses before credit. Document executed Full, ledger coverage, Shadow, and publication authority in `docs/release-gates.md` and `docs/testing-and-build.md`.
- Files: `scripts/test-rc-incremental-entry.ps1`, `tests/Antiphon.Tests/Scripts/RcIncrementalEntryTests.cs`, `scripts/release-cut.ps1`, `docs/release-gates.md`, `docs/testing-and-build.md`.
- Red witnesses (V-16): `IncrementalNeverCallsPublisher`, `FallbackIsReportedBeforeLaunch`, `MovedCandidateRefusesBeforeCredit`. Full regression via `ReleaseSelectionCliTests`.
- Checkpoints: CP-16, filter `/*/*/(RcIncrementalEntryTests*)|(ReleaseSelectionCliTests*)/*`, Min 3, EstimatedMinutes 8, build `bin-c1836-s7c/`, plus CP-90.
- Master first: landed S7b. If S5 has already added the D-5 section to `docs/release-gates.md`, this slice edits that file on top of it.
- Risks: `release-cut.ps1:191` is the call that must grow an Incremental refusal. A fixture seam that skips the publisher without going through the entrypoint does not satisfy V-16.
- Shadow: the entrypoint refuses publish for Incremental. Full's existing publish-on-green behavior stays. No tag, no promote.

### S7d — native Windows proof

See section 5 for the operator runbook. Code adds `scripts/test-rc-incremental-native.ps1` and `tests/Antiphon.Tests/Scripts/RcIncrementalNativeTests.cs` only after S7c is reviewed and on the source the desktop will build. Red witnesses V-17: `NativeIncrementalClosure`, `NativeTamperAndCleanupRefuse`, `NativeHistoricalBundleReverification`. The fixture needs enough carry candidates that D-8 leaves some unsampled. A three-chunk fixture samples every carry and cannot prove omission.

### S7e — release baseline branch

Not in the first wave and not on the path to the first two shadow cuts. When commissioned: `scripts/cut-release-branch.ps1`, `scripts/test-rc-release-branch.ps1`, `RcLedgerReleaseBranchTests.cs`, an extension of `rc-baseline.ps1` for immutable manifests, and a procedure section in `docs/release-gates.md`. Witnesses V-18: `OpenOrShadowClosureCannotCut`, `ExactClosureCreatesOnlyBranch`, `ExistingDifferentShaAndPublishRefuse`. CP-20, Min 3, EstimatedMinutes 8, build `bin-c1836-s7e/`. A shadow closure and an open closure cannot create the branch. Real remote ref creation stays a separate authorized operation. The version string is operator decision OD-4.

## 5. S7d desktop runbook

Run this only after `e7f92f66` has exited and the desktop is free, on a worktree whose HEAD is the reviewed integrated SHA (S7c and its parents), not on the live candidate checkout `rc/2026-11-29` at `26a99f1a747d`. The checkpoint tool leases its own build slots (`docs/testing-and-build.md` at the checkpoint-runner paragraph: slot probe, exit 4 on slot timeout). Do not wrap one lease around the whole run.

```
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-11-12-card-1836-incremental-activation-plan.md --after S7d --parallel 1 --expected-source-sha <40-hex integrated SHA>
```

`--after S7d` selects CP-17, CP-18, CP-19, and CP-90. Windows runs one row at a time. CP-18 and CP-19 reuse the CP-17 build (`bin-c1836-s7d/`).

| Row | Proves | Floor |
|---|---|---|
| CP-17 | `RcIncrementalNativeTests`: all three V-17 methods, zero failed, zero skipped | Min 3, EstimatedMinutes 12 |
| CP-18 | `RcFailedFirstExecutionTests` `ActualLoopFollowsSchedule` and `JournalCustodySurvivesInterleaving`, zero skipped | Min 2, EstimatedMinutes 10 |
| CP-19 | `NightlyNativeOwnershipTests`, all methods, zero skipped | Min 9, EstimatedMinutes 3 |
| CP-90 | activation-plan guard filter, zero skipped | Min 79, EstimatedMinutes 4 |

The plan's ordinary floor for this set is 29 minutes. That number is the sum of EstimatedMinutes, not a measurement. The S5c final attempt at `c891dcd3fc0192022c8f959b0f370f7e3015eae6` spent 704 s in an isolated build and executed zero tests at load 62.69 (`docs/superpowers/plans/2026-11-16-card-1836-s5c-checkpoints.md:42`). A desktop wall over 30 minutes gets a Debug task under the activation plan; do not widen timeouts.

What the report must show: `.antiphon/checkpoints/<run-id>/report.json` with those four rows, `dirty=0`, `source` equal to the expected SHA, zero failed, zero skipped. `NativeIncrementalClosure` must record a launch census that omits at least one carry candidate. A Linux run of CP-17 is not this proof: the plan says zero skips on Windows, and a Windows-only skip on Linux is not a pass.

Must not: start while `e7f92f66` is running; pass that run's directory as `-SourceRunRoot` or as evidence; call `release-cut.ps1` or `publish-release.ps1`; restart AppHost, the runner, or Docker; touch `antiphon_pgdata`; write into `C:\Antiphon\releases\candidates\rc-2026-11-29-26a99f1a747d\`. The historical-bundle witness uses a streamed fixture, not run `0b292fa4` and not run `e7f92f66`. Read-only admission of a real retained root is a later operation with its own evidence.

## 6. S5 tag-backing rule

This is S5 of `docs/superpowers/plans/2026-11-19-incremental-rc-qualification-plan.md:119`, decision D-5 at `:140` and the tag paragraph at `:105`. It is not a CARD-1820 slice. CARD-1820's landed sequence is S1R3–S1R6 (selector and script closure). The dispatch line that says "CARD-1820 S5" points at this tag rule; there is no `Test-RcTagBacking` under CARD-1820.

- Goal: `Test-RcTagBacking` reads an admission-envelope chain and returns `full`, `chain-to-full(anchor, age)`, or `not-backed(reason)`. `docs/release-gates.md` gains the rule. No tag, publish, or promote code calls it.
- Red witnesses: `ChainOlderThanSevenDaysIsNotBacked`, `ShadowOnlyChainIsNotBacked`, `CarryWithoutAnchorIsNotBacked`.
- Checkpoint: the 2026-11-19 plan's CP-5, filter `/*/*/RcTagBackingTests/*`, Min 3, EstimatedMinutes 15, build `tests/Antiphon.Tests -> bin-irq-s5/`. That plan's CP-90/CP-91 are its own closed rows.
- Master first: nothing in the wiring chain. The predicate can be written against fixture envelopes now. A production chain to evaluate does not exist until S6d/S7c have produced real envelopes, and the function must return `not-backed` for a shadow-only chain even then.
- Risks: editing `docs/release-gates.md` in parallel with S7c or S7e. Run S5 in the first wave so it is the only doc writer. Do not call it from `publish-release.ps1` or `promote-release.ps1`.
- Shadow: `ShadowOnlyChainIsNotBacked` is the guarantee. Until two trusted shadow cuts exist, the only backed tag is an all-executed Windows Full of the exact SHA (`docs/superpowers/plans/2026-11-19-incremental-rc-qualification-plan.md:105`).

Recommendation: put the predicate in compiled C# under `scripts/lib/Antiphon.Nightly/`, and keep a thin `scripts/lib/rc-tag-backing.ps1` that loads that assembly and calls it. That matches the project contract in `scripts/lib/Antiphon.Nightly/Antiphon.Nightly.csproj:3-6`: the compiled project is the implementation, PowerShell functions stay the entry points, and a failed build falls back rather than forking a second algorithm. `LaunchPlanner.cs` is the existing shape (compiled body, PowerShell branch in front). This predicate is new, has no Windows PowerShell 5.1 bootstrap caller, and the operator direction of 2026-11-19 says new logic goes in that core. A second copy of the chain walk in PowerShell would be the duplication that direction forbids. The ps1 file stays ASCII. If the core fails to load, the launcher refuses `not-backed(core-unavailable)` and does not answer `full`.

## 7. Open decisions

| # | Decision | Options | Recommendation |
|---|---|---|---|
| OD-1 | Read-only admission of retained run `0b292fa4` before the bootstrap Full | A. Admit after the desktop is free and report unproven counts. B. Skip admission and go straight to an instrumented Full | A, and do not block the server2 wave on it. D-5 says a pre-wiring pass still fails the new contract hash, so the report is a cost and gap list. It is not carry credit |
| OD-2 | Sample size and how many shadow cuts | A. D-8 as written: `N = min(Cchunk, max(30, ceil(0.05 * Cchunk)))`, two eligible cuts. B. Larger N or more than two cuts | A. Do not reduce N |
| OD-3 | When Trusted may be set | A. Explicit operator record after two clean eligible Windows shadow cuts, bound to contract digest and platform. B. Automatic after two cuts | A. The slices must not write that record |
| OD-4 | `release/x.y.z` versus CalVer, and when to cut it | A. Defer until after Trusted and an explicit version. B. Pick a version now | A. S7e stays unscheduled. The tool invents no version |
| OD-5 | Desktop time | A. S7d only after `e7f92f66` exits. B. Run S7d beside the live qualification | A. The proof needs no byte from that run |
| OD-6 | Owner map before the bootstrap Full | A. Commission the 2026-11-19 S3 map in the same first wave as S6a/S8b/S6b. B. Wire first, map later | A. Today's map is `{"path":"**","consumers":["*"]}` (`scripts/lib/rc-input-owners.json:5-7`). Every changed file dirties every suite, so `Cchunk=0` on a real recut. D-8 then says `sample=not-applicable` and no successful shadow pilot. Landing the map after the bootstrap Full changes `inputMapHash` and forces another Full (`rc-forced-full.ps1:81-91`) |
| OD-7 | Where `Test-RcTagBacking` lives | A. C# in `Antiphon.Nightly` plus a thin ps1 launcher. B. PowerShell only, as the 2026-11-19 plan's file list | A. Section 6 |
| OD-8 | Tag rule text | A. Full of the exact SHA until two trusted shadow cuts; a ≤7-day chain only after that. B. Allow a chain immediately | A. S5 writes the text and does not hook the publisher |

The 2026-11-19 plan's OD-4 default was "S1–S3 of that plan before the wiring chain." The operator direction of 2026-11-19 puts the wiring chain first. OD-6 is the part of that older default still required for the shadow cuts to be eligible. S1 (carry replay on real evidence) and S4 (class census) can wait.

## 8. Effort and the shortest path to two trusted shadow cuts

Code estimates are the activation plan's provisional 30–60 minute task sizes, including that slice's checkpoint allowance. They are not observed durations. Review time is UNKNOWN. Qualification wall time is UNKNOWN; the plan's ~17 h Full figure is contextual history, not a promise for the next run.

| Work | Provisional Code |
|---|---|
| S6a, S8b, S6b in parallel | 45–60 min, the longest of the three |
| S6c, then S6d, then S7b, then S7c | 45–60 + 45–60 + 45–60 + 40–55 min |
| S7d code plus its desktop rows | 45–60 min plan estimate; row floor 29 min, unmeasured |
| S5 tag backing | UNKNOWN in both plans. Peer of a small library slice, provisionally 40–55 min, if the body stays a pure predicate |
| S7e | 40–55 min, not on this path |
| 2026-11-19 S3 map, if OD-6 A | UNKNOWN here; that plan's CP-3 row is EstimatedMinutes 15 |

Critical path to a shadow-capable tree on master, with the first wave parallel: about 6.0 to 6.9 Code task-hours through S7d (60+60+60+60+55+60 minutes at the top of the ranges). Reviews, land rebases, and the desktop wait for `e7f92f66` are extra.

Shortest path to two trusted shadow cuts (activation plan D-8: two eligible Windows Shadow cuts, each with N executed samples, no discrepancy, no open failure, current native receipts, Full anchor under 7 days, then an explicit trust record):

1. Land the first wave (S6a, S8b, S6b, S5, and the owner map if OD-6 A is accepted).
2. Land S6c, S6d, S7b, S7c in that order. Review each. Keep Incremental off the running rc line until the candidate is cut from a tree that contains them. Master publication does not install scripts onto `rc/2026-11-29`.
3. After `e7f92f66` exits, run the section 5 proof at that integrated SHA. Zero Windows skips. The fixture leaves at least one carry unsampled.
4. Cut an rc from that tree and run one instrumented all-executed Windows Full. Contract drift makes this Full all-dirty. It becomes the anchor. It is not a shadow pilot.
5. The next rc cut with `Cchunk>0` is Shadow cut 1: execute dirty obligations plus N sampled carried chunks. The cut after that is Shadow cut 2, same rule.
6. The operator writes the trust record bound to that contract digest and platform. Trusted mode keeps the same sample.

That is three rc cuts after the wiring and the map are on the rc line: one Full, then two Shadow cuts. `e7f92f66` is not one of them. Trust is a decision after the second Shadow cut, not a fourth cut. If the owner map lands after step 4, step 4 has to be repeated before step 5, because the map change forces Full. If the map stays `**`, step 5 cannot happen: there is no eligible carry, and D-8 forbids calling the all-executed run a shadow pilot.

**Not done, noted:** no fix and no new design; the briefs restate the existing activation and 2026-11-19 plans against the tree at `5f0804e4`.

Remaining uncertainties: whether any remote head absent from this mirror already contains a wiring file; the live progress and outcome of `e7f92f66`; measured task time for S5 and for the owner-map slice; desktop wall of S7d; wall of the future Full and of the two shadow cuts.
