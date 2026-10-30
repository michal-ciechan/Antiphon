# Previously failed qualification chunks first: investigation

Confirmed at `cde8def4163829aa5b867df6ec625433be2522cb`: the qualification entrypoints have no prior-failure-aware sweep, chunk-order override, or completed-chunk resume. Whole-suite selection and ordering already work; lower-level class selection also exists. The planner's namespace/cost order explains the delayed feedback. Recommend Plan next and leave the current run running.

## Scope and evidence custody

Task `16ac13af`, 2026-10-30 UTC, assigned branch `feat/card-task-16ac13af`. This is an evidence-only investigation: no product edits, builds, native tests, desktop file access, live-run operations, tags or publication. The caller's standing authorization covered the repository/card reads and this report; no further approval was needed. Local scratch is gitignored under `.antiphon/evidence-16ac13af/`.

The stage contract prohibits designing a fix. This report establishes current behavior, evidence formats, integration boundaries and outstanding questions. The requested implementation design, new red-first checkpoint roster and 150-minute Code-task estimate belong to Plan; they are not claimed complete here.

All unqualified source references below are at the assigned base SHA. Historical documents recovered with `git show`, without checking out another branch or opening their cited desktop paths:

| Evidence | Immutable repository source |
|---|---|
| E1: prior result inventory | `9eced8b2f7713e54d32ead633d55504dca1b0a24:docs/investigations/2026-10-29-rc-qualification-28bbb58e-result.md` |
| E2: prior duration breakdown | `e4e65f69078f6340ba5fb8e0cee27f6778d58802:docs/investigations/2026-10-29-rc-run-28bbb58e-time-breakdown.md` |
| E3: reviewed manifest-publication plan | `de4863ac37e4197fb1fd69e70c463069f8016476:docs/superpowers/plans/2026-10-30-card-1514-incremental-manifest-publication-plan.md` |
| E4: local reconstruction | The real planner and credit predicates executed in PowerShell against committed data; essential transcript and reproduction below. No test executable launched. |

E1/E2 are stored investigators' transcriptions of run artifacts, not a fresh inspection of the original TRX/diagnostics. Their fix-status statements are historical and were not requalified here. Their source documents are present in retained Git history, not in this checkout's current file tree.

Read-only board transcript: `pwsh -NoProfile -File scripts/card.ps1 search 'qualification' -Board Antiphon -All` completed with **143 cards**; the same search for `28bbb58e` returned **CARD-1605 and CARD-1606**. `get CARD-1514` returned id `c92555be-65d0-45b6-a759-3d1ba5938bd2`, revision count 39; it explicitly preserves ordering and verdicts for its timing instrumentation. `get CARD-1606` returned id `0977d5bd-8ba3-4021-a8a9-2e121ab1b496`, revision count 0; its description records group 105's missing progress and says the task-private supervisor producer is outside the checkout. No private notes were read.

## 1. Existing controls and their actual meaning

The complete parameter lists are `scripts/nightly-run.ps1:82`, `scripts/nightly-tests.ps1:27`, and their implementations at `scripts/lib/nightly-run-impl.ps1:108` and `scripts/lib/nightly-tests-impl.ps1:840`.

| Control | Supported use and limit |
|---|---|
| `-Suites` | Both nightly entrypoints accept comma-separated suite IDs, e.g. `-Suites 'antiphon,session-runner'`. This selects whole suites. `Resolve-NightlySelectedSuites` preserves first occurrence order and removes duplicates (`scripts/lib/nightly-policy.ps1:241`); the executor follows that order (`scripts/lib/nightly-tests-impl.ps1:1174`). It does not select classes or chunks. |
| Whole-suite order | Supplying every required suite in a different order preserves the required-suite set. For example, `-Suites 'session-runner,agents-pty,antiphon,pty-host,messaging,client,scripts,e2e,markdown-pdf,observability'` places two previously failing suites ahead of antiphon. This is not failed-chunk-first within a suite and does not itself confer Full credit. |
| `-Profile rc` / `nightly` | Chooses the policy's required universe and exclusions, not a failure subset. At this SHA **rc requires ten suites**, including observability; nightly requires eight (`tests/test-execution-policy.json:4`, `:29`). Older nine/seven-suite prose is not the current roster. Unknown profiles refuse. |
| `-SelectionMode Full`, `Shadow`, `ChangeAware` | Accepted mode names, but all admitted attempts execute Full. `New-ReleaseSelectionAttemptCore` writes `effectiveMode='Full'`, `coverageScope='Full'`, and `executionEvidence=false`; ChangeAware records `change-aware-unqualified` (`scripts/lib/release-selection.ps1:218`). `Resolve-ReleaseSelectionMembers` returns `FullMembers`, ignoring proposed members (`:376`). These are not an operational subset switch. |
| `-BaselineSha`, `-BaselineReceiptPath`, `-QualificationPath`, selection/intent paths and digests | Selection authority/proposal inputs, not a failed-test list. Receiving hops validate Full scope, digests, candidate, run and attempt (`scripts/lib/release-selection.ps1:345`). A hand-edited selection file is not a supported bypass. |
| `-RunId`, `-Attempt` | Identify a run and sealed selection attempt. There is no completed-chunk lookup or skip in the execution loop. Supplying a prior ID is not execution resume: a run directory is selected and tests are invoked again (`scripts/lib/nightly-run-impl.ps1:554`, `:615`; `scripts/lib/nightly-tests-impl.ps1:1319`). |
| `-ContinueRunId`, `-ContinueParentPid`, `-ContinueParentStartedAt` | Internal self-reexec lock inheritance. The wrapper passes its live parent identity (`scripts/lib/nightly-run-impl.ps1:460`); shared-lock continuation checks matching live PID, start, run and lane (`scripts/lib/release-gate.ps1:402`). These cannot resume a stopped supervisor's completed tests. |
| `-NoSync` | Skips clone/fetch/reset/clean and reads the existing checkout HEAD (`scripts/lib/nightly-run-impl.ps1:385`, `:520`). It does not skip the build or tests. Neither nightly entrypoint has `-NoBuild`. |
| `-PolicyPath` on nightly-tests | `nightly-tests.ps1 -PolicyPath <complete-policy.json>` can read class-bearing `chunks.<suite>` declarations from an alternate hash-valid policy (`scripts/lib/nightly-policy.ps1:73`; `scripts/lib/nightly-tests-impl.ps1:1017`, `:1192`). This is a lower-level route to class selection, not a prior-failure input or chunk-order override: rc packing still reorders those leaves, and the full discovered UID union remains the completeness check (`:1278`, `:1595`). It is not exposed by nightly-run/release-cut and changes policy identity. It cannot retain the old Full authority; the direct executor also does not acquire the shared native-run lock. No alternate policy was created or executed here. |
| `-WhatIf`, `-NoReport`, `-SeamsPath`, timing/output paths | No chunk-order or failure-selection capability. `nightly-tests -WhatIf` reports suites before discovery (`scripts/lib/nightly-tests-impl.ps1:999`); it does not expose a chunk plan. `NoReport` prevents delivery credit (`scripts/lib/nightly-run-impl.ps1:686`). Seams are fixture injection. |
| TUnit `--treenode-filter` | Exists below the qualification front door. A directly invoked assembly can select `/*/Namespace/Class/*` or an exact method. The qualification executor constructs those arguments internally (`scripts/lib/nightly-tests-impl.ps1:753`); it exposes no matching public filter parameter. Operational restrictions are in section 5. |
| `Invoke-NightlyExactRoster` | Existing internal callable accepts `AssemblyPath`, a discovery document, class/method `Members`, and a fresh `ResultsDirectory`. It creates exact method launches and writes `antiphon-exact-roster-v1`, with `releaseCredit=false` on success (`scripts/lib/nightly-tests-impl.ps1:1733`, `:1795`). It is not wired as a qualification failed-class mode, has its own 600000 ms launch bound, and does not acquire `native-run.lock` in that function. |

`release-cut.ps1` forwards the sealed selection and run identity to nightly-run (`scripts/release-cut.ps1:143`), then proceeds toward publication. It is not the diagnostic restart command; `docs/release-gates.md:197` explicitly says so.

## 2. Confirmed ordering mechanism and reconstruction

The current path is:

1. Read selected suites in their supplied/policy order (`scripts/lib/nightly-tests-impl.ps1:973`, `:1174`).
2. Discover one native suite, apply its platform exclusions and census, and resolve its chunks (`:1265`, `:1276`, `:1278`). `Resolve-NightlySuiteChunks` packs when the suite is native, profile-aware, and has exclusions; otherwise it returns the existing chunks (`:469`).
3. `Get-NightlyProfileExecutionChunks` emits whole-class or exact-method leaves (`:28`). `ConvertTo-NightlyProfileLaunchGroups` groups ordinary classes by **sorted namespace** (`:388`); within each namespace `Split-NightlyItemsBySeconds` uses first-fit decreasing cost, with key tie-breaks (`:216`). It appends oversized-class splits and then exclusion-induced exact leaves (`:419`, `:426`).
4. `Get-NightlyProfileLaunchPlan` validates estimates and suite-specific watchdog admission (`:430`). The planned manifest receives the resulting array (`:1312`), and `foreach ($chunk in $chunks)` launches it serially (`:1319`). Owned cleanup precedes the next chunk (`:1436`). There is no read of prior failures anywhere in this chain.

This explains why script/checkpoint failures can be hours into a Full run despite having failed previously. A class is not necessarily one chunk: method exclusions and oversized classes split it; a group can contain many classes. Historical numeric group IDs therefore are not stable class identities across new discovery/cost inputs.

E4 replay used the committed Linux discovery census from 2026-10-21, expanded by the same field parsing documented in `scripts/test-nightly-tests.ps1:2276`, current Windows exclusions, current class costs, and the actual `Get-NightlyProfileLaunchPlan`. It did not alter, reorder, or execute the generated chunks.

```text
REPLAY nodes=19012 chunks=150 plannedSeconds=106343.8 elapsedSeconds=15.847
position=23  id=all-group-23  seconds=917 watchdog=3600 contains=TerminalRunnerSeatReleaseTests
position=99  id=all-group-99  seconds=900 watchdog=3600 contains=CheckpointRepeatBuildIsolationTests
position=101 id=all-group-101 seconds=340 watchdog=3600 contains=CheckpointExecutorLogTests
position=118 id=all-group-118 seconds=900 watchdog=3600 contains=ChangeAwareReleaseTests
```

These positions are a reproducible mechanism witness, **not** a forecast of run d47848cc. The old fixture has synthetic method names except where explicitly retained, predates current discovery, and uses current prices. Its 106343.8 seconds are summed model estimates, not measured execution time.

E2 records 386 chunk start/end pairs with maximum overlap 1, a 60.2-minute timed-out group 105, and 1378.1 minutes total. E1 records the same run identity and duration (`E1:3`, `:19`, `:104`). That stored run plus the current replay confirms the cause of delayed failure feedback; it does not prove which failures still reproduce after subsequent repairs.

## 3. Failure evidence exists, but no failed-list input format exists

The complete historical run is `28bbb58edca04b0ebb7afc914b74d386`, source `4563e52529b23052856cb0f1e0d01b383cf7590f`. E1 records **36 TRX failures in 28 classes**, plus **seven Error terminal records and one InProgress/hung test** in group 105, whose TRX is missing (`E1:21`, `:61`, `:102`). Filtering only present TRX Failed results omits those eight observations and the whole missing-coverage chunk.

Available formats:

| Artifact | What is actually present |
|---|---|
| `<suite>-<chunk>.trx` XML | `TestDefinitions/UnitTest[@id]/TestMethod@className,name`, joined by `UnitTestResult@testId`; outcome is on the result. `Read-NightlyTrxIdentities` already performs that identity join and rejects missing/duplicate identities (`scripts/lib/nightly-coverage.ps1:18`). This is the class-name source, not display text. |
| Run `summary.json` | Run/source/profile identity, suite/chunk rows, TRX paths, timeout flags and evidence reasons (`scripts/lib/nightly-tests-impl.ps1:1452`, `:1511`, `:1667`). It is not a supported one-class-per-line input or a complete failed-class roster. |
| Diagnostic discovery/execution | `antiphon-tunit-discovery-v1` and `antiphon-tunit-execution-v1`, plus pinned MTP diagnostics (`scripts/lib/nightly-coverage.ps1:8`, `:452`). E1's additional seven errors and hang were recovered from group 105 diagnostics, not synthesized from a missing TRX. |
| Timing planned/executed manifests | Planned groups have classes and expanded IDs; executed timing rows retain IDs/outcomes and TRX digests (`scripts/lib/test-phase-evidence.ps1:1240`, `:1399`). They are diagnostics, not release authority or execution resume. |
| Class cost JSON | `antiphon-nightly-class-costs-v1` maps class names to node count, seconds and provenance. `refresh-nightly-class-costs.ps1:22` aggregates TRX durations irrespective of outcome. It is a pricing file, not a failure list. |

`Get-NightlyBareClassIdentity` normalizes constructor-display suffixes (`scripts/lib/nightly-coverage.ps1:682`). Class identity, suite identity, expanded method identity and historical chunk number are distinct inputs; the existing artifacts contain those distinctions. No production entrypoint consumes a prior-failure text/JSON file today, so naming a new accepted format here would be design rather than investigation.

The 28 class names below were reconstructed from E1 rows 1-36. Four abbreviated entries were resolved against the current method declarations: `WorktreeManagerTests.cs:304`, `TestSelectionTests.cs:27`, `EvidenceDiffGuardTests.cs:202`, `ScriptsLaneRepairTests.cs:19`. A local parse reported `TRX rows=36 uniqueClassLabels=28`. This is a candidate mapping from stored evidence to current source, not a newly extracted TRX receipt.

| Suite / namespace prefix | Class names from the 36 TRX failures |
|---|---|
| antiphon / `Antiphon.Tests.Application.` | `TerminalRunnerSeatReleaseTests`, `SessionRuntimeActionTests`, `AttentionFeedNoiseTests`, `AgentReportStoreTests`, `CardFileGitFailureAcceptanceTests`, `GrokSignInRuntimeTests`, `CardFileIgnoreAcceptanceTests`, `StandingRestartAccountingTests`, `GrokRulesResumeMigrationTests`, `LandNonCodeSkipTests` |
| antiphon / `Antiphon.Tests.Checkpoints.` | `CheckpointRepeatBuildIsolationTests` |
| antiphon / `Antiphon.Tests.Infrastructure.` | `LandingGitTests`, `RepositoryChildRecoveryTests`, `WorktreeManagerTests` |
| antiphon / `Antiphon.Tests.Scripts.` | `TestSelectionTests`, `ChangeAwareReleaseTests`, `EvidenceDiffGuardTests`, `RunCheckpointRepeatScriptTests`, `C1008PlatformContractTests`, `WindowsUnitPlatformAdmissionTests`, `CardRelationshipScriptTests`, `NightlyDeadOwnerLockTests`, `ScriptsLaneRepairTests`, `RemoteScriptContractTests` |
| antiphon / `Antiphon.Tests.TestHelpers.` | `TestPhaseSummaryTests` |
| session-runner / `Antiphon.SessionRunner.Tests.` | `GrokRulesHttpAcceptanceTests` |
| agents-pty / `Antiphon.Agents.Pty.Tests.` | `FakeClaudeContractTests` |
| e2e / `Antiphon.E2E.` | `DispatchBaseWarningDeliveryE2ETests` |

The additional group-105 observations name `Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests` (hung), `CheckpointOrFilterTests`, `CheckpointLaunchCleanupTests` (three errors), `CheckpointAppTests`, `CheckpointAdmissionContractTests`, and `CheckpointSlotExecutorTests` (`E1:110`). E1 also records 480 NotExecuted TRX rows and 331 missing union UIDs. A failed-class sweep and a sweep of every incomplete obligation are therefore different selections. The next stage must settle that intended scope; this investigation has not silently widened the requested list.

## 4. Coverage, S3 and manifest boundaries relevant to Plan

### Partial execution does not currently earn Full credit

`Test-NightlyRequiredSuitesPresent` checks the selected suite set against the full policy (`scripts/lib/nightly-policy.ps1:225`). Missing suites set `coverageComplete=false` and add `partial-selection missing ...` (`scripts/lib/nightly-tests-impl.ps1:993`). Native chunk coverage still checks the full required suite UID union after exclusions (`:1595`; `scripts/lib/nightly-coverage.ps1:1161`). Missing results, unknown outcomes, zero rows, required skips and stale source/policy remain incomplete (`scripts/lib/nightly-coverage.ps1:999`).

The executor initializes `coverageScope='Full'` and only changes it to `PlatformDiagnostic` for Linux rc (`scripts/lib/nightly-tests-impl.ps1:1641`). Thus the existing Windows suite subset carries an incomplete Full-profile scope, **not an explicit Partial label**. Consumers must read `coverageComplete`/`succeeded`, not the scope word alone. This is an existing semantic limitation relevant to the requested explicitly partial mode; no relabeling was implemented.

E4 exercised the actual predicates with constructed state, not execution receipts:

```text
SUITES selected=agents-pty,antiphon complete=False
missing=session-runner,pty-host,messaging,client,scripts,e2e,markdown-pdf,observability
PARTIAL-SUITES credit= ok=False reasons=not-true-boolean:coverageComplete
NON-FULL-SCOPE credit= ok=False reasons=full-credit-scope-required
SHADOW-RESOLUTION members=A,B,C
```

The first credit case supplied `coverageComplete=false`, `coverageScope=Full`, otherwise green flags; the second supplied `coverageComplete=true`, `coverageScope=Partial`. Both were refused by `Get-ReleaseGateCreditVerdict` (`scripts/lib/release-gate.ps1:219`). Actual qualification flags are copied into run state and evaluated before green-file advancement (`scripts/lib/nightly-run-impl.ps1:625`, `:707`). A passing diagnostic is not a release receipt.

Publication has a separate stricter ledger validator. It requires `ledger.selection=full`, non-diagnostic/non-seamed evidence, exact run/source/authority/plan digests (`scripts/lib/release-authority.ps1:658`), one row per planned chunk, matching suite and UID membership, original TRX/digest, and every required UID Passed (`:795`). It looks up ledger rows by chunk identity rather than requiring matching array position (`:805`). The frozen plan itself is digest-bound, so rearranging an already sealed plan still changes its authority.

There is an inherited integration boundary: `New-ReleaseGateExecutionPlan` constructs a separate per-class plan (`scripts/lib/release-authority.ps1:356`); the inspected nightly producer chain does not call it or emit its `authority/ledger.json`. The checked-in speed-up plan explicitly records missing production authority/ledger wiring as an S4 prerequisite (`docs/superpowers/plans/2026-10-29-card-1514-rc-full-speedups-plan.md:209`). The timing manifests must not be mistaken for that missing wiring.

### Pricing and publication are independent of failure history

Current pricing uses measured seconds/nodes when available, otherwise the node/category/family model (`scripts/lib/nightly-tests-impl.ps1:148`, `:175`). Packing targets 900 seconds, with ordinary class splitting above 2400 seconds; E2E has lower class/launch limits (`:138`, `:354`, `:445`). S3 admission runs both while building the plan and immediately before execution, with the actual suite and policy (`:457`, `:1413`). Changing a class's placement is distinct from changing its members, price, watchdog or concurrency.

The admission library's measured path requires at least 20 complete observations, bound policy and matching suite/UID/cost/execution/assembly/OS/runtime/host/load/fixture/concurrency stratum (`scripts/lib/nightly-watchdog-admission.ps1:128`, `:197`). Complete failed-test walls remain samples; censored observations are retained as such (`:48`). At this SHA the planner passes `Basis='default'` and no calibration (`scripts/lib/nightly-tests-impl.ps1:457`), so the existence of a TRX-priced class does not mean the measured-watchdog path is active.

At this base `Write-TestPhasePlannedManifest` records the actual supplied group array and partition-exact flag (`scripts/lib/test-phase-evidence.ps1:1200`, `:1280`), retaining per-suite documents/index (`:1287`). `Write-TestPhaseExecutedManifest` still rereads and rewrites the cumulative snapshot (`:1420`).

E3 M1/M2 is a separate workstream: M1 adds journal/reducer; M2 owns production lifecycle and consumers (`E3:204`). Its contract preserves selection, watchdogs, ledger and receipt meaning (`E3:37`, `:43`), and journal recovery is diagnostic recovery, not permission to skip tests (`E3:75`). Retained implementation commit `597848127038e5dc2f3cd32f42874a7f29380591` changes this library; `git merge-base --is-ancestor <that-sha> HEAD` returned **1**. It is not part of the inspected base. This is repository-history evidence, not a claim about its current Review or deployment status.

The shared production locations are therefore `Invoke-AntiphonNightlyTests`' plan/manifest/loop boundary, `Publish-NightlyChunkObservation`, and `Write-TestPhaseExecutedManifest`/M1 lifecycle. Plan needs the actual commissioned integration tip; applying assumptions from this base to an M1/M2-integrated tree is unverified.

### Current witness inventory, not a proposed checkpoint plan

| Existing evidence/witness | Present scope |
|---|---|
| `scripts/test-nightly-tests.ps1:1541` / `Test-C1397_RcAntiphonLaunchGroups` | Uses the real suite resolver and validates grouping/membership/filter behavior. |
| `scripts/test-nightly-tests.ps1:2260` / `Test-C1410_MeasuredCostGroups` | Stored discovery census, packing, splitting and required-node conservation. |
| `scripts/test-nightly-tests.ps1:261` / G040 | Partial suite selection is incomplete while selected tests can pass. |
| `tests/Antiphon.Tests/Scripts/RcFullWatchdogTests.cs:52`, `:131` | Admitted cost/cap relationship and timeout incompleteness; `RcRosterRepairTests.cs:20` wraps cost regressions. |
| `scripts/test-change-aware-release.ps1:129` / C1313_ModeCredit | Non-Full scope cannot earn release credit. |
| `scripts/lib/release-authority.ps1:613` and its authority contract tests | Frozen plan/ledger, exact chunk/UID and original-evidence requirements. |

These were inspected, not run. None establishes a nonexistent prior-failure ordering input. A new red-first assertion and integration witness cannot be claimed from this replay. No evidence supports promising that both modes, wrapper hops, authority compatibility, native validation and M1 integration fit one 150-minute Code task; exact rows/build reuse/native obligations remain Plan's work.

One additional scope question affects size: execution is suite-at-a-time. Prioritizing within each suite would still leave session-runner/agents-pty/E2E failures behind all antiphon chunks. A promise that **all** previously failed chunks precede **every** other chunk is stronger than per-suite ordering. This is observable in the nested loops, not a chosen design.

## 5. Native reruns now and the current run

Native class/method reruns are documented (`docs/testing-and-build.md:1372`); exact and combined filter rules are documented at `:1117`. A native assembly can technically run outside the qualification supervisor, using the already built assembly at the matching SHA and a fresh private results directory. That execution is diagnostic and earns no Full credit. The existing exact-roster helper similarly binds assembly hash and emits non-credit evidence.

**There is no documented safe concurrent native sweep beside the current Windows qualification.** Both qualification lanes serialize through `C:\Antiphon\verification\native-run.lock` before sync/build/test (`docs/release-gates.md:110`; `scripts/lib/nightly-run-impl.ps1:302`). A contending supported run returns `deferred-busy`; stale/unknown ownership is not permission to clear it (`scripts/lib/release-gate.ps1:385`). Direct TUnit/ExactRoster invocation does not automatically participate in that shared lock. A different CoordinationRoot would evade the production serialization, not prove isolation.

The build-slot gate is separately required for test/build drivers (`docs/testing-and-build.md:1049`) and is not this native-run exclusion. `ProcessSpawnLimit.Limit => 1` is assembly/process-local (`tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:6`); two processes each have their own limiter. Separate outputs can avoid overwriting files while still adding CPU, memory, DB, process and filesystem pressure. E1 records load-sensitive seat-release, runtime-action, report-store, repeated-build and graph-evaluation failures (`E1:65`, `:68`, `:75`, `:93`). Their exact load attribution was not remeasured here.

Thus a documented individual rerun is available once native ownership/exclusive launch coordination permits it. It is not an authorized or demonstrated way to run concurrently now. Linux execution here cannot discharge Windows RC obligations (`docs/release-gates.md:230`), and this task ran no native tests on either host.

No repository copy of task-private `supervise-rc.ps1` or its commissioning script exists in the inspected tree. The available `scripts/lib/qualification-supervisor-heartbeat.ps1:1` is explicitly a detection-only library; `nightly-probe-supervisor.ps1` supervises a different probe workflow. The repo's status observer and ownership-preserving manual stop procedure are in `docs/release-gates.md:123`. E3 also retains private watcher inventory as a rollout condition (`E3:53`). No supervisor arguments or current process state are inferred from those other scripts.

## 6. Cost of stopping and restarting

Leave d47848cc running, assuming the caller's current-running description remains true. A restart cannot select failed-first with these entrypoints, cannot reuse completed chunks as new Full evidence, and adds setup/re-execution cost. There is no observed new failure or liveness evidence in this investigation that justifies a stop. This recommendation does not certify the live run's health or ETA.

| Restart effect | Evidence and practical limit |
|---|---|
| Completed work | Retained TRX/logs remain diagnostic evidence, but no supported chunk resume imports them into the new run. The suite loop starts again. The release owner says a changed rc tip is tested from scratch and partial runs are never reused (`docs/release-gates.md:496`). |
| Interrupted chunk | It may lose its final TRX and terminal coverage accounting. Group 105 is stored evidence of exactly that: 331 missing union UIDs, seven diagnostic errors and one hang despite no TRX (`E1:102`, `:194`). Preserve the available diagnostics. |
| Normal syncing restart | `fetch`, `reset --hard`, and `clean -fdx` run in the owned clone (`scripts/lib/nightly-run-impl.ps1:385`). Only the two ownership markers are excluded from clean; old ignored build outputs are not preserved by that path (`:412`). |
| Reusing the directory via `-NoSync` | The existing owned checkout can be the path after ownership/source checks; the documented prepared-checkout hop exists (`docs/release-gates.md:490`). All of npm ci, client build/lint and dotnet build still run (`scripts/lib/nightly-tests-impl.ps1:1068`, `:1138`). Dotnet might benefit from retained incremental outputs, but there is no build-skip/verified-build-reuse qualification option. This does not promise the old binaries can qualify changed tooling/source. |
| New identity | After an actual stop, the owner procedure requires a later qualification to be a new attempt, preserving old evidence (`docs/release-gates.md:192`). Selection digests bind run/attempt/source; reusing the ID does not create a continuation. |
| Measured scale | E2 records 1378.1 minutes total: setup 11.0, chunk walls 1365.7, gaps 1.4. Setup includes about 7.5 minutes of npm/build/lint/dotnet and about 3.6 minutes discovery/planning. This is one historical run, not a current restart estimate or p95. |

The incremental work cost of a restart is the already completed work that must run again, repeated setup, and any interrupted chunk work, offset only by measured future improvements. d47848cc's elapsed time, completed chunks and precise source/build identity were not inspected, so that cost cannot honestly be stated in current minutes. A new order alone changes feedback latency, not total required work. Even failed-first would not guarantee failure within a few minutes: historical setup was 11 minutes and a selected class can occupy a much longer chunk.

## Reproduction and limits

E4's script only dot-sourced production libraries and called pure planning/credit functions. It launched no TUnit, dotnet, npm, build-slot, supervisor or service process. Interpreter computation is not a qualification receipt.

Inputs:

```text
source: cde8def4163829aa5b867df6ec625433be2522cb
fixture: scripts/fixtures/nightly/c1410-antiphon-listing.txt
fixture SHA256: e203deda0430c2a411d4a3fe4f1cd9d23af499469f9d4ab4cc94456356a95216
costs: scripts/lib/nightly-class-costs.json
cost SHA256: 507caee1c73ef5271c285000dc0c53b06aba51c491c339d0b21f1cc0038b288c
scratch script SHA256: 378e51a50e13dcfd5556c9854fe228d9a36770cbaddcb943b88d7b37a605187a
raw transcript SHA256: 4c0e3f48b47f4a8b4c5ce11d91985ba3006311286ca8e590e92d630a8f226d48
```

The following minimal reproduction uses the same fixture expansion and real planner; run from the recorded source root. It reads committed files and emits a plan summary, without native tests:

```powershell
$ErrorActionPreference = 'Stop'
foreach ($name in 'nightly-common','nightly-policy','nightly-coverage','nightly-tests-impl') {
    . (Join-Path $PWD "scripts/lib/$name.ps1")
}
$policy = Read-NightlyExecutionPolicy -RepoRoot $PWD
$nodes = [Collections.Generic.List[object]]::new()
foreach ($line in [IO.File]::ReadAllLines((Join-Path $PWD 'scripts/fixtures/nightly/c1410-antiphon-listing.txt'))) {
    if (-not $line -or $line.StartsWith('#')) { continue }
    $f = $line.Split('|'); $name = $f[0]
    $bare = Get-NightlyBareClassIdentity $name
    $type = $name.Substring($bare.LastIndexOf('.') + 1)
    $cats = @(); if ($f[1] -ceq 'S') { $cats = @('Slow') }
    $i = 0
    foreach ($entry in $f[2].Split(',')) {
        $eq = $entry.IndexOf('=')
        if ($eq -ge 0) { $method = $entry.Substring(0,$eq); $count = [int]$entry.Substring($eq+1) }
        else { $method = 'M' + $i.ToString('0000'); $count = [int]$entry }
        $i++
        for ($k=0; $k -lt $count; $k++) {
            $nodes.Add([pscustomobject]@{ClassName=$name;Type=$type;Method=$method;
                Uid=($name+'.'+$method+'.'+$k);Categories=$cats})
        }
    }
}
$ex = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile rc -SuiteId antiphon -Platform windows)
$plan = @(Get-NightlyProfileLaunchPlan -DiscoveryNodes $nodes.ToArray() `
    -Chunks @([pscustomobject]@{id='all';classes=@()}) -Exclusions $ex `
    -SuiteId antiphon -PolicyObject $policy.Object)
$plan | Select-Object id,seconds,watchdogSeconds,classes
```

Remaining uncertainties: the current run's live progress and exact commissioned supervisor/tooling; fresh native discovery and failure reproduction on the next candidate; diagnostic-only versus all-incomplete selection intent; global versus per-suite priority; unknown/renamed/excluded prior class handling; the integrated M1/M2/S4 state; implementation size and new assertion-RED/native verification rows. These do not weaken the confirmed absence of a current failed-first control or the reconstructed ordering mechanism.

## Not done, noted

Plan a stable failed-class-first order of admitted complete chunks and, separately, an explicitly partial non-credit sweep; bind the prior-failure input and settle global/per-suite scope, missing-TRX handling, M1/S3 integration, red-first witnesses and cost there.

--- next stage ---
next: plan
handoff: Plan failed-first Full ordering and a separately explicit non-credit partial sweep from the commissioned rc tip; settle global/per-suite priority and missing-TRX scope, bind prior failure identities, preserve S3 pricing and M1 publication custody, and provide real-planner red-first witnesses plus a measured Code budget. Leave the current run untouched.
artifact: docs/investigations/2026-10-30-failed-first-qualification-run.md
