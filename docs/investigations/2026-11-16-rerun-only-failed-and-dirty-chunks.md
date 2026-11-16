# Can the next rc run re-run only failed/dirty/not-run chunks?

2026-11-16; Investigate task `fbc651a4`; read-only investigation of product/runtime state.
Source examined: `9b98828eb34eb552ce62bcde5a64dd045a872a88`.
Previous candidate: `a71c45ab4d5703d480f4eee4c4e30adab45bc4f9`, `rc/2026-11-22`, run prefix `160200cb`.

**Confirmed: no supported qualification launch skips previously passed chunks, within the same candidate or across candidates. A new Full attempt runs the entire newly prepared plan: 1,250 chunks if its plan has the reported 1,250 entries. Failed-first seeds change order only.** The exact new-candidate plan count must be measured after preparation; discovery and packing can change between cuts.

The mechanism is reconstructed with the real scheduling functions and confirmed against a stored native run report. Selection/admission helpers contain pieces of a future carry system, but execution still requires Full. No process was stopped, signalled, launched for qualification, or inspected on Windows. No repair was designed or implemented. The caller's existing authorization covered the pure loaders and repository reads; no further approval was needed.

## Decision table

| Mechanism | Exists? | Exact flags/front door | What it skips | Requirements | Risk / credit |
|---|---|---|---|---|---|
| Full new attempt | Yes | `-Profile rc -Trigger rc -SelectionMode Full -Ref <rc line> -ExpectedSha <full SHA> -RunId <fresh id>` with the normal owned roots and launch record | Nothing from the required plan | Pinned candidate/checkout, ownership and native lock; complete rc profile | Normal qualification path; must finish green and satisfy publication authority |
| Ordinary failed-first history | Yes | `-PriorFailuresPath <history.json> -PriorFailuresDigest <SHA256>`; `export-nightly-prior-failures.ps1 -RunDirectory <ended logs> -OutputPath <file>` | Nothing | Validated terminal summary and hashed artifacts; same candidate at executor, recent closure, matching canonical plan for previous-progress ordering | Invalid history falls back to a complete schedule; no carry credit |
| Cross-candidate stopped/ended-run seed | Yes | `-PriorFailuresPath <seed.json> -PriorFailuresDigest <SHA256> -AcceptCrossCandidateSeed -SeedSourceCandidateId <old candidate id>` | Nothing; passed band runs last | Valid seed content/hash, matching acknowledged source candidate, created/observed stamps within 14 days, no more than 5 minutes future | Failure, interrupted/unseen, sibling, completed ordering; never pass reuse |
| Seed export / cumulative merge | Yes | `export-nightly-stopped-run-seed.ps1 -RunDirectory <old logs> -OutputPath <new external file> [-SourceRunId ... -SourceCandidateId ... -SourceSha ... -TimingRoot ...]`; merge uses repeated `-Seed`, `-OutputPath`, `-ReportPath`, optional `-Repo`, `-Decision`, repeated `-Proven`, `-Strict` | Nothing | Retained stable evidence; output outside source; metadata supplied when absent | Exports completion observations and failures; merges failure proof history; no baseline authority |
| Restart same RunId and skip completed chunks | **No** | No `-Resume`; supervisor starter refuses an already observed RunId | Nothing | Fresh supervisor TaskRoot/RunId required | `resumeChunk` is an ordering cursor, not a persisted execution checkpoint |
| Bootstrap continuation | Yes, internal hop | `-ContinueRunId -ContinueParentPid -ContinueParentStartedAt` | Nothing | Existing lock and still-live parent with matching identity | Self-update lock handoff, not recovery after death |
| `Shadow` / `ChangeAware` selection | Yes, advisory requests | `-SelectionMode Shadow` or `ChangeAware`, optional `-BaselineSha -BaselineReceiptPath -QualificationPath` | Nothing: `effectiveMode=Full` | pwsh for non-Full requests; sealed selection identity/digests; any supplied claim must validate | `ChangeAware` remains unqualified, even if qualification JSON is supplied |
| Suite subset | Yes | `-Suites antiphon` or comma-separated suite IDs on nightly entrypoints; retain `-Profile rc` for rc policy | Entire omitted suites, **not** selected chunks inside antiphon | Known suite IDs; matching profile | Missing required suites sets `coverageComplete=false`; no rc-release credit |
| `Selected` / `Impacted` / `FailedOnly` / `Subset` / `Chunks` modes; `-Chunks`, `-OnlySuites` | **No** at these entrypoints | Not accepted parameters/modes | N/A | N/A | Cannot name the four old group IDs as a qualification subset |
| Exact diagnostic roster | Yes, library only | `Invoke-NightlyExactRoster -AssemblyPath -Discovery -Members -ResultsDirectory [-ClassifiedUids -Environment]` | Unselected methods | Current assembly-bound discovery, resolvable class/methods, new output directory, exact UID reconciliation | `executionEvidence=true`, `releaseCredit=false`; not a qualification supervisor mode |
| Prior-failure sweep resolver | Yes, library only | `Resolve-NightlyPriorFailures -History -Suites -QualificationMode PriorFailureSweep [-ExclusionContext ...]` | Resolves matching current classes/chunks; **does not launch a sweep** | Complete ordinary history membership, bound exclusions; no unresolved or empty selection | Stopped-run seeds explicitly refuse `seed-ordering-only`; coordinator calls the resolver with `Full` |
| Baseline / dirty-carry predicates | Yes, inert proposal/admission libraries | `Get-RcReleaseBaseline`, `Read-RcAdmission`, `Get-RcIdentityCarryDecision`, `Get-RcChunkCarryDecision` | No execution skips in launch path | Verified provenance/input manifests, compatible contracts, full anchor and complete history; graph admission still unavailable | Proposal/reader returns `gateCredit=false`; admission stays dirty/forced Full |

Parameter evidence: `scripts/nightly-run.ps1:82`, `scripts/nightly-tests.ps1:27`, `scripts/qualification/start-supervisor.ps1:20`, `scripts/qualification/supervise-rc.ps1:20`. The supervisor passes a launch script, not a chunk selector; `launch-rc.ps1:6` and `:64` forward the launch record's nightly arguments through the build-slot gate. `scripts/release-cut.ps1:30` accepts the same three selection modes. Current policy has **13 rc suites and 11 nightly suites**, not the older seven/nine described in some comments: `tests/test-execution-policy.json:18` and `:35`.

## Why seeds and resume markers do not save launches

`New-NightlyDispatchSchedule` enumerates every prepared chunk, stable-partitions references and verifies that the result is a set-equal permutation of the original plan (`scripts/lib/nightly-dispatch.ps1:97`, `:109`, `:116`). `Invoke-NightlyPreparedSchedule` independently checks the complete set and then invokes **every** schedule entry (`:225`, `:237`, `:243`). There is no branch here that accepts a historical pass in place of a launch.

For an ordinary previous run, `Resolve-NightlyPreviousRunOrder` explicitly describes a **new complete attempt**, validates closed liveness, same candidate SHA, schema and terminal age, and partitions failed / unfinished / passed (`scripts/lib/nightly-dispatch.ps1:28`, `:35`, `:49`, `:63`, `:80`). Its plan digest must match (`:127`). The history loader validates the history/evidence digests, summary provenance, terminal closure and seven-day freshness (`scripts/lib/nightly-failure-history.ps1:699`, `:716`, `:748`). These checks protect an ordering hint; satisfying all of them still skips zero chunks. `historyDigest` records the accepted ordering input, not an execution-resume token.

The stopped-run seed is the explicit cross-candidate exception for **ordering**. Its source SHA is not required to be the new SHA; old chunk numbers are not binding. It maps failed methods/classes to the current plan (`scripts/lib/nightly-stopped-run-seed.ps1:451`). It ranks failed methods first, then interrupted/unseen work, then siblings, then completed classes, and includes the passed band in the final list (`:553`, `:568`, `:591`). The lane's `resumeChunk` / `lastPassedChunk` are emitted at `:584`. They do not remove the passed band.

`Initialize-NightlyDispatchHistory` requires the cross-candidate acknowledgement, exact source-candidate acknowledgement and valid time bounds for this seed (`scripts/lib/nightly-failure-history.ps1:727`). A refusal becomes `failed-first fallback` rather than permission to omit execution (`:773`). The normal coordinator calls history resolution in `Full` mode (`scripts/lib/nightly-dispatch.ps1:215`). The alternate `PriorFailureSweep` resolver validates exclusions and membership but is not an entrypoint flag; stopped seeds reject that mode (`scripts/lib/nightly-failure-history.ps1:780`, `:800`, `:879`).

There is also **no same-run recovery that skips passes**. The complete public parameter ASTs have no `Resume`, and `start-supervisor.ps1:40` refuses an existing supervisor identity or reused RunId in last-run/run.lock/own-last-run. `ContinueRunId` is populated only for the script self-update hop (`scripts/lib/nightly-run-impl.ps1:642`, `:658`); its lock path requires the continuing parent still alive (`scripts/lib/nightly-common.ps1:1035`, `:1058`). It cannot revive a stopped parent.

### Stored native witness and the alleged 473/670 counts

The requested `origin/feat/card-task-b2663606` report exists as commit `9a83e86ea560721a4b06e8e357f3f8b45f019c84`, `docs/investigations/2026-11-15-seed-34d41b7e.md`. Its last paragraphs explicitly say the seed only reorders Full, all 1,204 chunks were marked completed, and the launch uses the four prior-failure/acknowledgement flags. It reports 13 failure members, not 1,204 reusable passes.

A stronger execution witness is commit `4e16fc96cea6d89ff8612550d3c7a4d97abe3fcb`, `docs/investigations/2026-11-15-run-b7812833-final-result.md:37`: **1,236 scheduled, 1,236 executed, zero skipped (including skipped-by-seed), 59 prior-failure entries first**. Line 46 explicitly reports no seed `invalidated` field. Its summary had `baseline-absent` (`:24`). The seed produced afterwards had 1,234 native chunk rows, 22 failed-method rows and a cumulative identity report (`:118`); client/scripts had no TRX and were absent from that seed, not absent from execution.

The specifically named `docs/investigations/2026-11-15-seed-b7812833.md` is absent from HEAD and from the available `git log --all -- <path>` history. The start and final b7812833 reports above are available and were read. No `seeded` or `invalidated` runtime counters occur in the inspected PowerShell libraries/supervisor files. **The original source of “473 seeded / 670 invalidated” is unavailable; its unit and derivation cannot be established. It must not be interpreted as 473 skipped chunks.** Resolving that historical statement needs its original report/output; it does not change the executable permutation proof.

## Selection and baseline credit

The only accepted selection requests are `Full`, `Shadow`, `ChangeAware` (`scripts/nightly-run.ps1:91`, `scripts/nightly-tests.ps1:37`). The selection constructor always seals `effectiveMode=Full`, `coverageScope=Full`, `executionEvidence=false`; ChangeAware adds `change-aware-unqualified` (`scripts/lib/release-selection.ps1:219`, `:228`, `:253`). A supplied qualification JSON is hashed but does not activate it (`:221`). Receiving hops refuse edited scopes and mismatched identity/digests (`:363`), and `Resolve-ReleaseSelectionMembers` returns the full member set (`:394`).

There are two distinct baseline concepts:

1. The launch selection baseline reader accepts a **fully published Full rc release**, not arbitrary prior passing TRXs. It verifies repository/platform, full ledger and green verdict, publication artifacts/receipt, each required suite/UID and bound TRX digests (`scripts/lib/release-selection.ps1:68`, `:97`, `:119`, `:133`, `:153`). Even a valid compatible baseline only informs an advisory proposal; execution is still Full.
2. `Get-RcReleaseBaseline` is a read-only **proposal** returning `executionEvidence=false`, `gateCredit=false` (`scripts/lib/rc-baseline.ps1:31`, `:39`). It verifies candidate/pointers/history, excludes non-ancestors/self, requires same line for non-release candidates, prefers recorded release, then newest ancestor release, then newest same-line candidate (`:54`, `:73`, `:84`, `:97`). It does not require a *published release on the same line* for the ancestor-release choice. It does require admitted historical evidence/full anchor; post-baseline Failed/Interrupted/NotRun/Unproven history forces Full (`:78`, `:112`, `:115`). Missing/corrupt provenance forces Full (`:123`).

The carry predicates do exist: matching platform, known boundary, identical input digest, passed/expected-skip identity **and closed passing chunk**, no unresolved same-digest flake and complete carried provenance (`scripts/lib/rc-ledger.ps1:290`, `:339`). Those are library decisions, not launch authority. `Read-RcAdmission` is explicitly inert, requires an executed Full passing anchor with no carried entries (`scripts/lib/rc-admission.ps1:2`, `:230`), and its algorithm-coverage check is unconditionally incomplete (`:9`, `:31`). It returns `admitted=false`, `dirty=true`, `forceFull=true`, `executionEvidence=false`, `gateCredit=false` while that remains so (`:318`). No launch caller to these baseline/admission/carry functions was found in nightly, qualification, or release-cut entry paths.

Forced Full additionally considers unknown/changed policy, catalog, selector, parser, coverage, launcher/custody/build/harness/runtime/input contracts; unverified baseline/admission/history/global ownership/shadow evidence; and a missing, future or seven-day-old Full anchor (`scripts/lib/rc-forced-full.ps1:73`). A stopped run's passing TRXs alone satisfy none of that closure. `scripts/rc-ledger-import.ps1:26` deliberately writes no historical passed rows; its output is admitted=0, Full=true, gateCredit=false (`:45`).

The advisory class index is not an unchanged-input proof. Its release-evidence mode explicitly emits no admitted graph, `complete=false`, `fallback=Full`, and refuses a purported complete/classes-bearing envelope (`scripts/lib/release-impact.ps1:56`, `:84`, `:105`). The project-batch proposal also remains advisory (`docs/release-gates.md:817`, `:834`).

`-Suites` is real, but a proper subset records `partial-selection missing ...` and incomplete coverage (`scripts/lib/nightly-tests-impl.ps1:1920`, `:1940`). `-Suites antiphon` still runs all that suite's chunks. A Full scope label by itself is not credit: `Get-ReleaseGateCreditVerdict` requires true coverage/tests/report flags, exit zero and pinned candidate SHA (`scripts/lib/release-gate.ps1:201`, `:219`). Historical narrower operator acceptance explicitly granted no rc-release/publication credit (`docs/release-gates.md:435`, `:454`). A repaired four-class/method diagnostic is useful repair evidence; it does not turn a failed/stopped Full run into a passing release baseline.

## What stopping preserves

**Losing the monitor and invoking the recorded stop command are different.** `supervise-rc.ps1:15` never stops/signals/restarts children; loss of its heartbeat can coexist with a still-running driver. Its own copied state is observational (`scripts/lib/qualification-supervisor.ps1:145`, `:176`).

The explicit Windows command is `stop-supervisor.ps1 -TaskRoot <bound task root> -CoordinationRoot <bound coordination root> -RunId <full old run id> -Reason <text> [-EvidenceRoot <same-volume destination>]`, preview without `-Confirm` (exit 2), apply with `-Confirm`. It validates recorded process generations and kills the supervisor tree, then the driver if it survived; it conditionally preserves/moves only the dead matching native-run lock (`scripts/qualification/stop-supervisor.ps1:6`, `scripts/qualification/qualification-stop.ps1:263`, `:305`, `:356`). **This investigation did not execute either preview or apply.**

| Artifact already on disk | Effect of explicit forced tree stop |
|---|---|
| Completed chunk TRXs, console/discovery logs, timing sidecars and plans | Not deleted by stop code; remain raw evidence. An in-flight file or queued evidence write may be incomplete; no final flush is guaranteed after `/F`. |
| `summary.json` | Written at terminal aggregation, not after each chunk (`scripts/lib/nightly-tests-impl.ps1:2190`, `:2228`, `:2279`). A mid-run forced stop does not manufacture it or a run verdict. |
| `last-run.json` / `own-last-run.json` | Existing state remains. Driver writes phase=tests before launch and final completedAt only after tests/report (`scripts/lib/nightly-run-impl.ps1:745`, `:869`). Stop explicitly does not edit last-run; supervisor copies only its bound run and preserves a completed copy (`scripts/lib/qualification-supervisor.ps1:160`). |
| Supervisor heartbeat/log/identity | Last flushed files remain; heartbeat becomes stale. Stop appends a STOP reason. Normal `EXIT` / commission-exit is not guaranteed after forced death (`supervise-rc.ps1:94`, `:118`; `qualification-stop.ps1:420`). |
| `run.lock` / native-run lock | Stop does not remove run.lock. Native-run lock moves only with verified dead matching owner and unchanged bytes; refusals retain it (`stop-supervisor.ps1:11`). |

The stopped-seed exporter does **not** export only failures. It reads plans, root chunk TRXs and timing/owner metadata, writes each chunk's classes, completed/started status and last-run time, plus failed-method identities and hashed evidence (`scripts/lib/nightly-stopped-run-seed.ps1:285`, `:342`, `:368`, `:392`, `:430`). “Completed” includes a finished failing chunk and tolerated skips; it is not “all tests passed.” Torn/missing/changing files remain unfinished. It can work without summary if the required metadata/plans are available (`:179`, `:295`, `:397`). The merged seed copies the newest chunk observations and cumulatively retains unproven failed identities (`scripts/merge-nightly-seeds.ps1:437`). These exports are ordering inputs, not pass-credit baselines.

## Measured diff and impacted scope

Commands run against the exact requested endpoints:

```sh
git diff --stat a71c45ab4..9b98828eb
pwsh -NoProfile -File scripts/impacted-classes.ps1 \
  -Range a71c45ab4d5703d480f4eee4c4e30adab45bc4f9..9b98828eb34eb552ce62bcde5a64dd045a872a88 \
  -Repo /work/worktrees/ws-02 -Json
pwsh -NoProfile -File scripts/select-test-rosters.ps1 \
  -BaselineSha a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 \
  -CandidateSha 9b98828eb34eb552ce62bcde5a64dd045a872a88 -Json
```

Diff: **15 files, 676 insertions, 19 deletions**: four why-missed reports; CARD-1087 plan; `rc-baseline.ps1`; `test-selection.ps1`; CheckpointImportTests; ImpactedClassesSelectorTests; RcReleaseBaselineTests; PairedOverheadStatisticsTests; TestTimingChokePointTests; shared TestTiming.cs; derived-impact-index.json; impacted-classes.json.

The impacted-classes parser returned **33 distinct short class names, 30 derived class names, three filters, zero capped/owed entries**. Project memberships are 29 Antiphon.Tests classes, five SessionRunner classes and one Agents.Pty class; ProcessSpawnLimitTests is present in all three, hence 35 memberships but 33 names. The three matched hand-table kinds were `spawn-limit-rosters`, `test-lane-category`, `timing-labels` (`tests/impacted-classes.json:81`, `:330`, `:426`). This is advisory Code/Review selection (`scripts/impacted-classes.ps1:12`), not a count of dirty qualification chunks.

| Changed input | Advisory evidence / affected tests | Suite implication |
|---|---|---|
| CheckpointImportTests.cs and CARD-1087 plan | Changed class itself; reverse edge to PlanCoverageBindingTests (`tests/derived-impact-index.json:23550`); checkpoint glob adds ProcessSpawnLimitTests | antiphon; guard name also selects session-runner and agents-pty |
| rc-baseline.ps1 / RcReleaseBaselineTests.cs | Changed RcReleaseBaselineTests; stored native failure is g136, `AncestorReleaseThenSameLineFallback` (`docs/investigations/2026-11-15-why-missed-g136-rcbaseline-timeout.md:5`) | antiphon; no reverse index row for rc-baseline itself |
| test-selection.ps1 / ImpactedClassesSelectorTests.cs | SelectorLineEndingAttributeTests reverse edge (`tests/derived-impact-index.json:3924`) and changed class | antiphon; release selector also detects authority change |
| PairedOverheadStatisticsTests.cs / TestTimingChokePointTests.cs | Both changed classes; reverse edge to PairedOverheadStatisticsTests (`tests/derived-impact-index.json:26460`); stored g138 timing benchmark failure (`docs/investigations/2026-11-16-why-missed-g138b-testtiming-overhead.md:3`) | antiphon |
| tests/Shared/TestTiming.cs | 21 reverse-edge class names (`tests/derived-impact-index.json:26855`), including timing/fixture/removal tests and four SessionRunner classes; hand table adds TestTimingOperationLabelTests | antiphon and session-runner in advisory output; shared harness change is not proven limited by these edges |
| derived-impact-index.json | DerivedImpactIndexTests, ReleaseImpactEnvelopeTests (`tests/derived-impact-index.json:26905`) | antiphon |
| impacted-classes.json | DerivedImpactIndexTests, ImpactedClassesSelectorTests, PostLandEarlyWarningTests (`tests/derived-impact-index.json:26924`) | antiphon |
| Four new reports | No executable class attribution from advisory index | Release catalog treats these paths as unknown, requiring Full |

The reported failed chunks map to CheckpointImportTests g109, RcReleaseBaselineTests g136, TestTimingChokePointTests g138 and TestTimingOperationLabelTests g139, supported by the four why-missed reports named above (`g109` report `:5`, `g139` report `:5`). New candidate discovery/packing may renumber groups; class/method identity must be used when comparing cuts. The older b7812833 final-result report `:95` demonstrates actual group-number changes between candidates.

For completeness, advisory output's 33 class names were:

```text
AgentTaskScopedListEndpointTests C0824DesktopLegacyReleasePolicyTests
C1514HostLoadApiTests C1514HostLoadMigrationTests C1708ReflogSalvageTests
Card1619ExitPersistenceTests Card1619LinuxStopIntentTests Card1619RunnerExitTests
Card1653PeerCheckTests Card1653SocketDirTests ChannelOutboundUnifiedTransportTests
CheckpointImportTests CheckpointOutputConfinementTests CheckpointShapeConflictTests
CheckpointTrackedBaselineTests DerivedImpactIndexTests ImpactedClassesSelectorTests
LandingRemovalPolicyControlTests LinuxOwnedGitProcessTests PairedOverheadStatisticsTests
PlanCoverageBindingTests PostLandEarlyWarningTests ProcessSpawnLimitTests
RcReleaseBaselineTests ReleaseImpactEnvelopeTests SelectorLineEndingAttributeTests
TestLaneCategoryGuardTests TestTimingChokePointTests TestTimingOperationLabelTests
TestTimingRecorderTests TimingProbeTests VolumeRetirementReceiptTests WorktreeNoFollowDeleteTests
```

The actual **release** selector returned `mode=full`, all **42** selection sets, `executionEvidence=false`, `activation=unqualified`. Reasons: `selection-authority-changed` for test-selection.ps1, plus 14 unknown changed paths. It considers both old/new catalogs, expands every set on fallback and marks the result unqualified (`scripts/lib/test-selection.ps1:317`, `:336`, `:342`, `:348`, `:370`). Thus this particular diff would already request Full at proposal level, even before the executor's unconditional Full restriction.

No live 160200cb plan/TRXs were available in this Linux checkout. The brief's 871-done/867-passed/4-failed/379-unrun at 08:30Z and approximately 321 remaining at 09:32Z are **caller-supplied snapshots**, not fresh measurements. There is no defensible exact union of dirty old chunks from the capped advisory index and those counts alone; that requires the retained current group membership plus per-chunk state and admitted input digests. The operational launch count does not depend on that missing union because no historical pass is omitted.

## Cost and operator choice

Counts below hold the plan at the supplied **1,250** entries, with successful preparation and no early fatal refusal. They are launches, not equal-duration units; build/discovery/report overhead is additional.

| Choice | Further chunk launches | Carry / gate consequence |
|---|---:|---|
| A. Stop and start new candidate Full, no seed | **1,250** | Zero historical passes reused |
| B. Stop and start new candidate Full, valid stopped/cumulative seed | **1,250** | Same count; four failed chunks lead if identities map, unrun/interrupted work follows, passes eventually re-execute |
| Same candidate, fresh attempt with valid ordinary previous progress | **1,250** | Matching SHA/plan only permits reordering, not skipping |
| New candidate `Shadow` or `ChangeAware` | **1,250** | Effective Full, irrespective of requested narrowing |
| New candidate with baseline proposal/admission/import helpers | **1,250** through the supported qualification launcher | No admitted carry / gate credit |
| Keep current run to its end | **379** at the 08:30 snapshot, or approximately **321** at 09:32 | Completes old candidate's evidence; its existing failures keep that candidate red |
| Finish old run, then fresh repaired candidate Full | **1,629** from 08:30, or approximately **1,571** from 09:32 | More total work than restarting now; retains complete old-candidate diagnostic coverage |
| Desired failed + unrun + dirty union | Hypothetical **383 + d** at 08:30, where d is dirty among the 867 earlier passes; **325 + d** at 09:32 only if still exactly four failures | **Unavailable as a qualified launch mechanism.** d is not measured or safely derivable here |
| Four old groups alone on new candidate | **No supported chunk-selective rc invocation** | Ordinary exact-filter diagnostics can retest repairs; they confer no Full closure |

**Recommended operational decision:** do not stop on the assumption that a seed preserves 13 hours of pass credit. It preserves observations and priorities only. If the goal is the earliest qualification of repaired source, accept a fresh Full run after the operator's stop decision; if complete old-candidate diagnosis is worth the remaining approximately 321 launches, let it finish, knowing a new repaired candidate still owes Full. Finishing the old run and testing four repairs does **not** produce a green new-candidate release baseline.

There is no exact command for “stop, new cut, rerun only failed/dirty/not-run with qualification credit” in this revision. Existing supported launch ingredients for the fresh Full option are:

```text
# In the new candidate's owned launch-record.json nightlyArguments:
-NoSync -Profile rc -Trigger rc -SelectionMode Full
-Ref <new cut's rc/YYYY-MM-DD line> -ExpectedSha <new full candidate SHA>
-CandidateId <candidate derived from that line and SHA> -RunId <fresh full run id>
-CheckoutRoot <new candidate checkout> -StateRoot <owned state>
-LogRoot <owned candidate logs> -ReleaseRoot <owned releases>
-CoordinationRoot <shared native coordination root>
# Optional ordering only, after exporting/merging and validating a seed:
-PriorFailuresPath <seed file> -PriorFailuresDigest <SHA256>
-AcceptCrossCandidateSeed -SeedSourceCandidateId <seed source candidate id>

# Existing launcher, on Windows, after ordinary commissioning/preflight:
pwsh -NoProfile -File scripts/qualification/start-supervisor.ps1 -TaskRoot <fresh task root> -LaunchScript <bound launch script> -LogRoot <bound log root> -StateRoot <bound state root> -RunId <same fresh id>
```

These are parameter bindings for the repository launcher, not an executed or fully commissioned launch. Keep the normal Windows detached-start/owned-root contract; `launch-rc.ps1:64` takes the build slot. Choose values from a fresh commissioning record; the brief supplies only a prefix of the old RunId and no old supervisor TaskRoot, so a concrete live stop/launch record cannot be reconstructed here.

For diagnostic repair confirmation, existing TUnit `--treenode-filter` execution or `Invoke-NightlyExactRoster` can select those four classes or precise methods after a matching native build. The latter builds one exact method filter per discovery identity (`scripts/lib/nightly-coverage.ps1:2400`) and explicitly returns `releaseCredit=false` (`scripts/lib/nightly-tests-impl.ps1:2370`). Neither lets the supervisor run four old group IDs, and method diagnostics omit unchanged tests that shared those chunks. No new wrapper, policy override, or synthetic summary is proposed.

## Reconstruction transcript and validation scope

All product calls below were pure parsers/loaders/planners, with outputs in a newly created `/tmp/antiphon-fbc651a4-evidence`; no build or test driver ran. No nightly, supervisor or qualification entrypoint was executed. Four entrypoint parameter ASTs parsed with zero errors; their parameter sets confirmed the flag inventory above.

A scratch harness dot-sourced nightly-common, nightly-failure-history and nightly-dispatch, constructed **1,250 unique synthetic chunk references**, and supplied the brief's exact cardinalities: 871 observed terminals, failed IDs 109/136/138/139, 867 other passes, 379 absent. Ordinary previous-progress input had closed liveness, current terminal time, same SHA and the exact freshly computed canonical plan digest. The stopped-seed-shaped input had one unique class per chunk, started/completed booleans for the 871 terminals, four failed methods and current timestamps. These are **synthetic rows**, not fabricated live receipts. Only the scheduling functions were called; the executor was not invoked.

```text
pwsh -NoProfile -File /tmp/antiphon-fbc651a4-evidence/reconstruct.ps1
exit=0
case=same-candidate-order
  entries=1250 unique=1250 reason=previous-run-order
  first=109,136,138,139,872 last=869,870,871 lastPassedChunk=871
case=new-candidate-ordinary-history
  entries=1250 unique=1250 reason=previous-run-candidate-mismatch
case=new-candidate-stopped-seed
  entries=1250 unique=1250 reason=operator-seed-order
  first=109,136,138,139,872 last=869,870,871
  resumeChunk=872 lastPassedChunk=871
case=same-candidate-wrong-plan
  entries=1250 unique=1250 reason=previous-run-plan-mismatch
fixtureExport: summaryExists=false chunks=21 completed=11 incomplete=10
  failures=6 failureMembers=6
```

The export used the repository's unmodified `scripts/fixtures/nightly/stopped-seed`, wrote only the scratch output, and re-read it with the returned digest. Its 11 completed chunks include failed chunks: this independently shows that export retains completion observations beyond `failedTests` and works without summary. Fixtures are source data, not observations of 160200cb.

A second pure harness preloaded the normal library dependencies at script scope and called `New-ReleaseSelectionAttemptCore` for Full/Shadow/ChangeAware, with the pinned endpoints and a scratch release root, then `Get-RcReleaseBaseline` without pointers:

```text
Full        -> effective=Full scope=Full proposalMode=full executionEvidence=false
Shadow      -> effective=Full scope=Full proposalMode=full executionEvidence=false
ChangeAware -> effective=Full scope=Full proposalMode=full executionEvidence=false
  additional reason=change-aware-unqualified
all three: baseline-absent, selection-authority-changed, 14 unknown-path reasons
Get-RcReleaseBaseline: selected=null forceFull=true
  reason=baseline-absent executionEvidence=false gateCredit=false
exit=0
```

The first version of this scratch harness failed dependency setup (`Get-NightlyPolicyHash` unavailable after function-local loading guards); preloading dependencies as the normal entrypoint does corrected the harness. This was not a qualification/test failure and no repository source was altered for it.

Re-run the two committed-endpoint selector commands above to reproduce impact findings. Reconstruct scheduler behavior by creating chunk objects `{id,classes}`, a suite `{id,chunks,nodes}`, calling `New-NightlyDispatchSchedule` once for its canonical digest, then supplying previous `{schemaVersion=1,liveness='closed',sha,completedAt,canonicalPlanDigest,suites}` or a validated seed through `-PreviousRun`. The fixture export front door is shown in the decision table. Full generated output is intentionally outside git. Essential results are retained here; scratch paths are not durable deployment evidence.

## Remaining uncertainties

- Actual current progress, exact old RunId/TaskRoot and live file inventory were not read. Obtain a fresh bound native observation before any operational action.
- New candidate packing may change 1,250; measure its prepared schedule. The invariant is **all N**, not an immutable global N=1,250.
- The exact historical 473/670 statement needs its original source; the requested seed-b7812833 Markdown is unavailable in accessible history. Available native evidence explicitly contradicts treating those as executed skips.
- Exact dirty-group count needs current/previous plan membership and proven transitive inputs. Advisory class counts cannot supply it. This uncertainty does not affect the supported execution count.
- The repaired Windows overhead witness still owes native confirmation, as its own why-missed report states; this investigation did not validate the fixes.

## Not done, noted

No implementation/design work; selective cross-candidate qualification would require separately commissioned work beyond the currently inert carry/admission mechanisms.

--- next stage ---
next: decide
handoff: Decide whether to finish the old candidate for diagnostic closure or stop and accept a fresh Full attempt; seeds skip zero chunks, and the requested failed/dirty/not-run carry path is not available at 9b98828eb34eb552ce62bcde5a64dd045a872a88.
artifact: docs/investigations/2026-11-16-rerun-only-failed-and-dirty-chunks.md
