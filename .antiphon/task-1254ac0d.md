# Review 1254ac0d: failed-first qualification ordering plan

Subject: plan task `38d5d544-1e27-49d0-ae15-ce69c2341f18`, branch `feat/card-task-38d5d544`, tip `a6210f1703dcd653eabcc4884455e4937e0b8cb3`.
Files: `docs/superpowers/plans/2026-10-30-failed-first-qualification-ordering-plan.md`, its CARD-1514 twin (the two are byte-identical, md5 `9e1a45eb...`), and `docs/investigations/2026-10-30-failed-first-qualification-run.md`.

**Verdict: the plan is not ready for Code. There are 6 defects and 4 advisories.** The design is sound: global stable partition, an immutable canonical plan with a separate dispatch schedule, Partial as an orthogonal mode, and whole-chunk fallback. The defects are consumers of chunk order and preparation order that the plan does not name, and a fallback rule that refuses the historical run the feature exists for.

## Checks run (plan-level; read-only)

| Check | Result |
|---|---|
| Importer (`Antiphon.Checkpoints import --plan`, tool built through build slot lease `921b42e2…`, 37 s, 0 errors / 5 warnings) | **16 rows, 6 builds** (`bin-failfirst-s1..s5`, `-win`). Per-row estimates 6,2,7,2,1,7,2,2,8,2,2,1,6,2,8,2 = **60 whole minutes**. `after` groups S1×2, S2×3, S3×3, S4×4, S5×2, S5-W×2. Both plan copies import identically. Build outputs `src/Antiphon.HostCleanup/bin-r1254`, `tools/Antiphon.Checkpoints/bin-r1254` removed |
| Real planner replay at this SHA (production `Resolve-NightlySuiteChunks` with `-SuiteId antiphon -PolicyObject`, rc/windows exclusions) | **19,012 nodes / 150 chunks / 150 unique ids, 35.9 s**. Positions 23 / 99 / 101 / 118 reproduced. Group 101 also holds CheckpointLaunchCleanup/App/SlotExecutor. CheckpointOrFilterTests and CheckpointAdmissionContractTests are absent from the 2026-10-21 census, so the plan's supplemental fixture is required |
| Fixture / cost digests | `c1410-antiphon-listing.txt` = `e203deda…` (106,434 B, committed); `nightly-class-costs.json` = `507caee1…`. Both match the plan |
| Existing CP rows | CP-5 `RcFullWatchdogTests.CapFitsAdmittedPlannedCost` is present at the base and on `rc/2026-10-27`. CP-8 `Antiphon.Tests.TestHelpers.RcManifestIntegrationTests.LedgerAndReceiptBytesAreUnchanged` is present on `origin/feat/card-task-695a5150` (`190d9c17e`) |
| Prerequisites | S3 is on rc, landed rebased as `041565b22` / `026cff122`. M1/M2 (`3dd236bb3`) is **not** on rc |
| `check-evidence-diff.ps1 -BaseRef 2991d96bb… -HeadRef a6210f17…` | 5 commits, 0 entries, 0 violations, exit 0. `git diff --check` is clean |
| Line references | Gate `release-gate.ps1:235`, ledger `release-authority.ps1:668` and `:805`, baseline `release-selection.ps1:97`, summary scope `nightly-tests-impl.ps1:1641`, and coverage primitives `nightly-coverage.ps1:18/92/314/413/500/585/682` are all accurate |

## Consumers of chunk order (brief item 1)

| Consumer | Order-independent? |
|---|---|
| Planner: chunk set, content, `seconds`/`costBasis`, admission (`:430`, prelaunch `:1413`), ids `all-group-<n>` | Yes. Ids are canonical planner positions, so dispatch order cannot change them |
| Ledger lookup (`release-authority.ps1:805`), keyed by chunk id | Yes |
| Timing sidecars `<suite>-<chunk>.timing.json` (`test-phase-evidence.ps1:2875`) | Yes |
| `Set-NightlyOutcome` (priority minimum, `:1024`); summary `coverageComplete`, `testsPassed`, `succeeded` (monotonic false) | Yes |
| M1/M2 journal: envelope keyed by sequence plus suite/chunk; no chunk-index assumption | Yes for keys. **No for the header and per-chunk verdict fields** (D-1) |
| Executed manifest per-chunk `testsPassed`/`coverageComplete` | **No** (D-1) |
| Summary `reasons` and `suiteTimeline`, runner state `reasons` (`nightly-run-impl.ps1:632`) | **No** (D-1) |
| Singleton `groups-planned.json` and the heartbeat's `plannedSeconds` lookup | **No** (D-2) |
| Resume | Not applicable: no completed-chunk resume exists, and the plan adds none |

## Defects

**D-1: order-dependent fields in Full artifacts are not named, so the "order only" byte claim is wrong or vacuous.**
- Where: `nightly-tests-impl.ps1:1418` passes the *run accumulators* `$coverageComplete`/`$testsPassed`, as they stand **before** this chunk's verdict (`:1497-1530`), into `Publish-NightlyChunkObservation`. That writes them as the chunk's own fields through `Write-TestPhaseExecutedManifest` (`test-phase-evidence.ps1:1415-1418`; M1/M2 `:2646-2649`) into `groups-executed.json` and the journal records.
  - The M1/M2 projection header `suite` is sticky from the first publication (M1/M2 `test-phase-evidence.ps1:2160-2181`, `:1966-1969`).
  - Summary `reasons`, which flows into `last-run.json`/LastGreen through `nightly-run-impl.ps1:632`, and `suiteTimeline` are appended in execution order.
- Failure: in a failed-first run, priority chunk all-group-101 fails first. Every later ordinary chunk is then published with `testsPassed=false` even when it passed; in canonical order the chunks before position 101 carried `true`. The executed-manifest header suite also becomes the first priority suite, and the state's `reasons` order changes.
- Why: brief item 1 requires naming every consumer of chunk order. The plan's CP-4 claim ("byte-identical after sorting only row arrays by `(suite,chunk,UID)`") either fails on `reasons`, or is satisfied only because these artifacts are left out. CP-7 says only "does not change coverage".
- Fix:
  - Enumerate these fields.
  - For each, decide whether to canonicalize it (for example, emit `reasons` in canonical chunk order) or declare it an execution-order diagnostic.
  - Make CP-4 and CP-7 assert each decision by name, including that per-chunk verdicts are either chunk-local or explicitly documented as run-cumulative.

**D-2: the heartbeat's planned-cost lookup becomes wrong under preparation-first or interleaving.**
- Where: `Write-TestPhasePlannedManifest` overwrites the singleton `groups-planned.json` for every suite (`test-phase-evidence.ps1:1287`, "keep the singleton for existing readers"). `Get-QualificationGroupPlannedSeconds` (`qualification-supervisor-heartbeat.ps1:36`) matches on the bare chunk id, and ids are `all-group-<n>` in every suite, so they collide across suites. Today the singleton always belongs to the suite currently executing, because execution is suite-major.
- Failure: S3's `Prepare-NightlySuiteExecution` prepares all suites first, which leaves the singleton belonging to the last native suite (observability). The heartbeat for antiphon `all-group-3` then reads observability's `all-group-3` `plannedSeconds`, or null (the 20-minute default). The idle budget is wrong, which can produce a false stall or hide a real one.
- Why: this is an unnamed consumer whose correctness depends on order. The private supervisor relies on it.
- Fix: name it. Either suite-qualify the lookup through the per-suite file and index, or rewrite the singleton to the executing chunk's suite before each launch. Add a CP-6 assertion for the interleaved case.

**D-3: the historical group-105 fallback cannot be reconstructed under the plan's own rule.**
- Where:
  - Plan D-5 and the extraction table line 56: "If only legacy singleton exists, accept only matching suite/run". Plan line 61: "fallback count must come from the bound original manifest". Commissioning step 2: "missing membership is a stop".
  - Source `4563e525` (run `28bbb58e`) writes **only** the singleton; its `test-phase-evidence.ps1:1236` has no per-suite planned files.
  - Its rc roster places antiphon first, followed by seven more native suites (session-runner, pty-host, agents-pty, messaging, e2e, markdown-pdf, observability).
- Failure: the retained singleton is observability's plan. The exporter must refuse group 105, so commissioning stops at step 2 for exactly the run that motivated the feature. CP-1 passes only on synthetic data.
- Fix: specify a bound reconstruction. Options:
  - Rerun the planner from source `4563e525` on the retained `antiphon-profile.discovery.json`, with that source's policy and cost file. Bind the assembly, policy and cost digests, and cross-check the chunk ids against the retained `antiphon-all-group-*` log, TRX and timing names.
  - Or use the retained launch filter, if the group-105 log or diagnostics hold it.

  Add CP-1/CP-2 cases for "the singleton belongs to another suite", covering both refusal and the reconstruction path.

**D-4: a no-history Full run does not behave identically to today.**
- Where: plan D-2 ("No history means the existing dispatch order") combined with Prepare-once-for-all-suites. A discovery failure rethrows at `nightly-tests-impl.ps1:1302`, and the whole-loop `catch` (around `:1613`) aborts all remaining work.
- Failure: today the antiphon, session-runner and other suites execute and leave evidence before a later suite's discovery throws. With preparation first, the throw happens before any chunk runs: the verdict is equally red, but the executed evidence is different. The E2E prerequisite `continue` also moves earlier.
- Why: this is a semantic change outside "order only", and CP-4's failure matrix (all-pass, failure, missing TRX, skip, unknown UID, timeout, reporting, teardown) has no preparation or discovery failure case.
- Fix: state the policy explicitly. Either keep lazy per-suite preparation when there are no priority members, or execute the prepared suites and mark only the failed suite incomplete. Add discovery-failure and E2E-prerequisite variants to CP-4/CP-6.

**D-5: CP-4 can pass when the feature does nothing (self-compare).**
- Where: CP-4 `FullBytesAndCoverageConserved` compares the canonical run against the failed-first run.
- Failure: with a no-op scheduler, both runs dispatch in canonical order, so the bytes are trivially equal and the row stays green.
- Fix: make CP-4 assert a precondition before comparing bytes:
  - at least one priority chunk exists;
  - the dispatch order is a non-identity permutation that crosses at least one suite boundary.

**D-6: the Partial consumer roster and state-root isolation are incomplete (brief item 2).**
- Where:
  - Not listed, and with no witness:
    - `promote-release.ps1:122`, which reads LastGreen;
    - `release-gate.ps1:901`;
    - the `nightly-health.ps1:851-861` Windmill monitor, which reads `last-run.json` and `last-complete-green.json` from StateRoot;
    - `qualification-supervisor-heartbeat`.
  - D-9 asks for a "separate diagnostic state root", but the commissioning commands (plan `:203-204`) tell the operator to keep the "existing … state … arguments", and the plan names no mechanism that derives or forces a separate root.
  - The gate treats an absent `coverageScope` as historical Full (`release-gate.ps1:233`), which contradicts D-9's "a detached artifact has unknown scope".
- Failure: a sweep launched with the Full StateRoot writes `last-run.json` there. The monitor and health checks read it, and no witness exists. Any producer path that drops `coverageScope` is then read as Full scope; only the flags still protect it.
- Fix:
  - Have the mode force-derive the Partial state and evidence roots, and refuse when they resolve to the Full root.
  - List every consumer above with its refusal mechanism and red witness.
  - Make the credit verdict refuse when `qualificationMode≠Full`, `diagnostic=true` or `releaseCredit=false`, whether or not the scope field is present.

## Advisories

- **A-1: S2 is allowed to start before M1/M2 lands, but both edit the same function.** S2 edits `nightly-tests-impl.ps1`. M1/M2 (`3dd236bb3`) changes `Invoke-AntiphonNightlyTests` in about 20 hunks, including the planning and loop region. Either restrict S2 to `nightly-dispatch.ps1` plus tests, or base S2 on the rc tip with M1/M2 integrated.
- **A-2: renamed or removed classes fail loud, but leave the operator stuck.** The plan never mentions CARD-1613 or CARD-1618, and no test-file renames appear on rc since 2026-10-29. D-6 refuses correctly, but a single deleted *fallback* member (co-packed, not a failure) blocks the whole failed-first run, and the plan offers no recourse beyond "regenerate evidence", which needs a new Full run. Consider separating observed failures, which must resolve, from conservative fallback members, which could be reported as retired with evidence. This is a decision for the caller.
- **A-3: a private hop can silently degrade to an unordered Full run.** If the private `supervise-rc` hop drops both the path and the digest, the repo receives neither and runs unordered Full without noticing. Make operator acceptance read the persisted dispatch schedule's `historyDigest` and priority count before the first chunk, and stop on a mismatch. Step 4's "tiny-child fixture from CP-13/15" is test code: name an operator entrypoint, or reword the step.
- **A-4: the twin plan copies and rc.** rc carries no `docs/superpowers/plans`, so any rc Code task must carry the plan for `--plan` to resolve. Keep the two copies identical, or drop one.

## Brief items 5–7 (sampled)

- **Item 5, replay fixture:** the fixture is a committed listing expanded in scratch, and the companion fixtures are tiny and synthetic. CP-3, CP-4, CP-6 and CP-9 drive the real planner. Of the five sampled rows, CP-3, CP-6, CP-7 and CP-9 go red on a no-op; CP-4 does not (D-5). CP-11 and CP-12 are positive-control designs, correctly deferred to Mutation. The filters resolve to existing names for CP-5 and CP-8, and the rest are proposed names with no collisions.
- **Item 6, sizing:** every slice is estimated at 110–145 minutes, within the 150-minute limit. The land order (rc first, separate master port) and the absence of any definition-file change are stated correctly.
- **Item 7, operator procedure:** the repo-side recipe and the release-gates documentation are planned for S5. The private hop remains a commissioning obligation (see A-3).
