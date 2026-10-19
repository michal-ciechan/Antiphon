# CARD-1313 S6-S7 verification design

Date: 2026-10-19. TestDesign task: `051d787d`. Target: **master only**.

This is an additive verification appendix to
[the S6-S7 fix plan](2026-10-19-card-1313-s6-s7-plan.md), read at
`b978f1d71f582df739143174b4d4e5e72996fe0d` on `feat/card-task-1193b198`.
It does not replace that design. The plan is on its named branch, not in this
task's base tree; land the plan and this appendix before commissioning Code.
Inspected implementation/base: `a0f3c4f3e728655933da1782981a88fb2b7b3a56`.
Card read: CARD-1313, `99e86094-bbdb-4329-a13f-06eed945951e`, board
`8988ca03-7414-47ad-b0b6-51556c701703`, revision count 26.

**Readiness:** S6-A has an implementable ordinary selection (CP-1..4); S6-T follows
it (CP-5..7). S6-B/C/D and final publication claims in S7 retain binding-required
dependencies BR-1/BR-2 below. The whole S6-S7 handoff is therefore **next: plan**,
not unrestricted Code. The caller may separately commission S6-A with these four
rows. No operator policy choice is needed to finish the missing seam design.
New method names below are specifications, not claims of existing implementations.
No builds, discovery, tests, mutations, release, activation or suite runs occurred
in TestDesign. All positive controls remain pending post-land Mutation.

## Verification design

### Inspection

Bodies, rather than class names alone, were read at the base above:

| Bodies read | Boundary and coverage |
|---|---|
| `tests/Antiphon.Tests/Scripts/ReleaseGateRunTests.cs`; `Test-C599_CreditVerdicts`, `Test-C599_MasterStateIsolation`, `Test-C599_ParameterHops`, `New-C599RunFx`, `Invoke-C599Run` in `scripts/test-release-gate.ps1` | V-1..3, R-1/R-5. Existing parameter checks partly inspect text; they are not forwarding evidence. |
| `ReleaseGateAuthorityContractTests.cs`, `ReleaseGatePublicationAuthorityTests.cs`; `New-C599PolicyCheckout`, `Get-C599Discovery`, `New-C599Authority`, `Update-C599Ledger`, `Set-C599TrxOutcome`, `Test-C599B_PinnedPolicy`, `Test-C599B_AllChunks`, `Test-C599A_ReceiptReadback`, snapshot/restore helpers in the release harness | V-2/V-8/V-9/V-11, R-3/R-4/R-7. Hand-written TRX/ledger is a validator fixture only; it cannot prove V-6/V-7 production. Historical schema-2 eight-suite fixture stays historical. |
| `ReleaseBranchModelGateTests.cs`; `Test-C1311_MovingTipAuthority` and file-backed Git/GitHub observer helpers | V-11, R-6. Tested SHA must equal the live rc tip on publication and promotion; old roots/tags remain unchanged. |
| `TestSelectionExecutionTests.cs`; `New-C1313GitFixture`, candidate/selector helpers, `Get-C1313FilterDiscovery`, `Test-C1313_ExactExpandedExecution`, `Test-C1313_CoverageRefusal`, `Test-C1313_NewUidRemainder` in `scripts/test-test-selection.ps1`; `tests/Antiphon.Checkpoints.FilterHost/C1313FilterTests.cs` | V-1/V-8/V-10/V-12. Real Git and native expanded UID/forbidden-sibling witnesses; discovery alone is not execution. |
| `TestTimingReportTests.cs`, `TimingJournalTests.cs`, `TestSetTimingTests.cs`; `scripts/lib/test-timings.ps1`; `Invoke-NightlyExactRoster` | V-4/V-5, R-2. Existing C# journal tests use `TimingJournal`; they do not prove the PowerShell producer's durability. Existing fake-driver timing tests do not prove child custody. |
| `tests/Antiphon.E2E/ReleaseGateReportDeliveryTests.cs`, `ReleaseGateIsolationTests.cs`; `AntiphonAppFixture.InitializeAsync`, host startup/restart, HTTP client, disposal and isolation configuration; E2E project staging and `Fixtures/ProcessSpawnLimit.cs` | Nearest fixture for V-6/V-7 new class. Existing report tests observe incident cards, not a bound release card's typed receipt; their list helper is unsuitable as an absence oracle without full pagination. |
| `tests/Antiphon.Tests/Scripts/ScriptHarness.cs`, `ScriptHarnessProcess.RunAsync`, options/default budgets; `scripts/lib/release-gate.ps1` credit/publisher/readback gates; `scripts/lib/release-authority.ps1` constructors, pinned-policy validator and card reader/correlation parser; `scripts/publish-release.ps1` pre-write composition | New script tests must use existing owned process harness. Its 300-second execution and 10-second cleanup budgets are not to be widened for this work. |
| `New-Efx`/`Invoke-E` in `scripts/test-nightly-tests.ps1`; `Write-C487Seams`/`Write-C487NativePassSeams` in `scripts/lib/c487-harness.ps1` | F-A can observe the production command boundary with substituted workload I/O; these helpers synthesize evidence and cannot supply F-P's real-child proof. |
| `scripts/release-cut.ps1`, `nightly-run.ps1`, `nightly-tests.ps1`; `Invoke-AntiphonNightlyTests` and exact-roster path; run implementation parameter/forwarding paths; `nightly-report.ps1` result, card write and recovery paths | V-1..7. No production call to either authority constructor or `ReleaseIntentPath` supplier was found. Current exact-roster timing hard-codes `nightly`, attempt 1, and swallows persistence failures. |
| `ReleaseBranchModelDocumentationTests.cs`; `docs/release-gates.md`, checkpoint/PC/delivery sections of `docs/testing-and-build.md`, orchestration stage and D-2 policy, project conventions; original CARD-1313 guard/PC map | V-13 nearest documentation fixture; preserves historical obligations and master-only boundary. No live deployment qualification inferred. |

Fixture recipes used by all rows:

- **F-A (envelope):** new `scripts/test-change-aware-release.ps1`, invoked through
  `ScriptHarness.RunHarnessCaseAsync` from `ChangeAwareReleaseTests`, category
  Integration, `[ParallelLimiter<ProcessSpawnLimit>]`. Own a temporary Git origin
  and release clone with committed B and A, pinned real policy/catalog/selector
  bytes, private candidate/state/coordination/qualification/timing roots. Derive
  expected path impact from a literal case table, not the result being tested.
  Adapt the existing Git-fixture mechanics without dot-sourcing a harness that
  executes its case at import. All new script cases have explicit required labels
  and exact assertion-row counts; those counts are not TUnit executions.
  Use two explicit observations for S6-A: (a) the real nightly orchestration
  selects every required suite/entry at its workload command boundary, using the
  existing seam pattern to avoid running the release suite; (b) its roster-mode
  decision drives actual FilterHost execution with four eligible expanded leaves
  in Full/Shadow versus a one-leaf proposal. An I/O adapter may supply the tiny
  workload; it must not choose the roster or replace the mode/credit predicate.
  This proves mode consumption and transport, not nine-suite production authority.
  Never relabel synthesized orchestration results as native executed evidence.
- **F-T (timing):** new `ReleaseSelectionTimingTests`, Integration and the same
  process limiter, script-harness ownership. Run the actual nightly exact-roster
  producer with the staged FilterHost, two run identities, and an attempt retry.
  Compare persisted records with independent TRX/diagnostic parsing and a fresh
  reader process. A writer I/O barrier may delay/fail storage; it must not replace
  journal identity, flush, replay, measurement or outcome logic.
- **F-P (producer):** new `ChangeAwareReleaseProducerTests`, category OptIn,
  `[NotInParallel]` and E2E's assembly-local process limiter. New slice-local
  `ChangeAwareReleaseProducerFixture` uses `AntiphonAppFixture` with disposable
  Postgres, real Program/Kestrel and its random-port runner; no browser or provider
  is launched and `UsePrebuiltFrontend=false`. Fresh project, board and existing
  release card are created through HTTP before invoking the cutter. The fixture
  owns and joins the script process tree through the established bounded owner
  pattern, not the old report test's bare `Process.WaitForExitAsync`. Build/stage
  tiny actual native and script/client workload children under the checkpoint's
  isolated build. **BR-1/BR-2 must be resolved before this recipe is executable.**
- **F-V (validator):** valid versioned authority/plan/ledger fixture derived from
  the existing release harness, real pinned Git blobs, independent remote-store
  observer and loopback card GET. Fabricated validator evidence is explicitly
  diagnostic; no production release credit. Mutate one field/UID/receipt at a
  time, repair unrelated hashes, and start each variant from a restored valid
  reachable control. It proves predicate discrimination, not producer delivery.
- **F-D (documentation):** new `ChangeAwareReleaseDocumentationTests`, Unit, based
  on `ReleaseBranchModelDocumentationTests`. Read actual script AST parameters,
  pinned policy and Windmill definition plus owner docs. Compare stated defaults,
  examples and stage claims with those inputs. It proves the documentation
  contract only, not registration, qualification or delivery.

For every V/R case below, classification includes ordinary regression/acceptance;
each PC adds a mutation classification. A compiling defect must reach its named
outcome assertion. Missing APIs, missing files, parsing/build/setup failures,
zero test selection and earlier unrelated assertions are **binding/infrastructure
failures**, never intended red. New tests may need a separately committed,
non-behavioral parameter/adapter scaffold before a meaningful red-first run.
That scaffold must retain the broken old outcome. Existing methods in R rows are
regression pins, not thirteen new red-first witnesses.

### Delivery inventory

All joins include repository, candidate ref/id, full A, intentId (when supplied),
runId and attempt. Selection adds B and selection digest; timing adds set/chunk
and eventId; card receipt adds card GUID and exact current revision. A caller's
request, exit zero, queue insertion, event, Sent flag or acknowledgement never
proves receipt.

| Path | Producer -> destination | Persistence boundary and recovery | Observable receipt / test |
|---|---|---|---|
| DL-1 frozen selection | `release-cut.ps1` -> nightly wrapper -> implementation -> self-reexec -> tests wrapper -> tests implementation | Attempt and selection written before launch; identical resume allowed; changed identity refuses; new run retains earlier attempt. Crash before each hop or failed child creation resumes the same attempt without substituting branch-tip bytes. | V-3: receiving child reads exact paths/digests/A/run/attempt, and actual test producer executes the frozen roster; argv capture alone is insufficient. G-59/G-87..92. |
| DL-2 timing | actual nightly child owner -> durable spool -> flushed JSONL -> fresh report reader | Start committed before launch; terminal after child/streams joined. Fail before spool, after spool/before append, during append, after flush/before ack, before spool removal; restart/replay uses same event ID. Recoverable storage failure retains data and marks timing incomplete. | V-4/V-5 fresh reader finds one complete event per logical event after owned output cleanup, exact run/count/build joins and unfinished start-only state; G-93..106. This local spool is the real recovery queue. |
| DL-3 Full authority | commissioned external intent/card supplier -> cutter -> discovery/plan -> children -> ledger | Create-once input; frozen authority/discovery/plan precede launch; terminal/prerequisite/teardown evidence precedes report intent. Restart at each boundary refuses contradictory identity and cannot turn partial evidence into green. | V-6 observes producer-created files and real child evidence in order, then an independent validator; hand-writing those files is not a substitute. G-62/G-107..113. BR-1/BR-2 pending. |
| DL-4 prepublication report | report producer -> real bound card content endpoint -> PostgreSQL -> independent GET -> local typed receipt -> publisher's fresh GET | Persist report intent before HTTP. Restart before write, failed write, committed write/lost reply, after readback/before receipt save, and after receipt save. Preserve identity and unrelated card text; use fresh concurrency token. | V-7 GET `/api/cards/{id}` and revisions prove exactly one complete correlation and report body at the bound card; typed receipt matches readback; publication blocked until it exists. G-63/G-64/G-113..119. |
| DL-5 public v2 asset | publisher -> file-backed Git/GitHub recipient substitute | Existing publication journal/reservation recovery is retained. New asset upload failure, committed upload/lost reply and mismatching readback resume or refuse without allocating another tag. Recheck live rc tip before credit. | V-11 reads recipient asset bytes/digest and peeled tag independently, including complete public scope; upload response is insufficient. G-69/G-136..140/G-146. Does not qualify real GitHub. |
| DL-6 train selection | external frozen train manifest -> `Resolve-ReleaseTrainSelection` caller | Synchronous value returned; persisted only in the caller-owned attempt when release is invoked. B/A/membership change invalidates it. | V-12 returned identity plus readback of bound attempt, no branch/card/task writes. Actual CARD-1281 scheduling/delivery excluded. |

**Real-queue applicability:** this plan introduces no session-input, Hangfire or
server report queue. DL-4 is synchronous HTTP with a durable local report intent;
test the real card persistence path, not a mock queue. Its busy-recipient arm
creates an actual stale-token conflict by editing the disposable card between
GET and PATCH; its already-eligible arm starts with a writable current revision;
its already-delivered arm begins with the complete correlated body. Both retry
and immediate delivery must end in recipient evidence. DL-2 tests the actual
spool/replay queue with a held writer and an immediately available writer, plus
each failure cut above. There is no enqueue API to fake. If supplier planning
adds an asynchronous queue, this inventory is incomplete until that **real** queue
has producer-to-recipient busy/already-eligible/enqueue/crash tests. If it adds
session input, only the matching **complete UserPrompt transcript** may close
delivery; no API ack, event or screen observation may substitute.

V-7 barriers: pause the owned script just after durable report-intent flush; hold
the transport before PATCH; use an isolated loopback proxy that forwards the real
HTTP write then drops its response; hold after independent GET but before local
receipt flush; inject a local receipt-write failure. Restart only the owned
producer (and, in one arm, `RestartOwnedHostAsync` with the same Postgres). Always
release barriers and join children in `finally`. Proxy observations schedule cuts;
the verdict comes from a fresh direct client to the real app. Inspect the exact
card and revisions, with pagination when enumerating, never a global DB count.

**Binding-required register (not waived by Code completion):**

- **BR-1 production supplier:** no production `ReleaseIntentPath` caller exists at
  the inspected SHA. The release coordinator/CARD-1281 owner must name its exact
  caller entrypoint, durable intent creation/resume operation, existing card
  acquisition and containment contract. F-P may create the disposable card by
  `POST /api/boards/{boardId}/cards`, GET it by ID, and supply that identity through
  the agreed caller entrypoint. Merely writing a plausible intent in setup proves
  a consumer only. Record an exact supplier invocation and prove its bytes reach
  `release-cut.ps1` before V-6 is bound. No live card writes are authorized here.
- **BR-2 producer workload seam:** the current release harness fabricates discovery,
  TRX and ledger, while FilterHost has only one native assembly. It cannot produce
  the schema-3 nine-suite Full plan. Plan must bind a bounded fixture mechanism
  for every required native suite and declared client/script entry, actual discovery,
  actual child terminals, and source/build identity, without relaxing the production
  universe or clearing diagnostic/seamed provenance. F-P can prove the producer
  and recipient with non-credit provenance; F-V separately proves the normal
  validator. Their conjunction is not live publication qualification. Specify the
  exact validator boundary expected to accept structural evidence while the final
  publication gate still refuses diagnostic provenance. Until then V-6/V-7 and
  dependent selected-publication acceptance remain **binding required**.

### Proves it works now

Each row supplies recipe, cases/variants, layer, exact detecting method, oracle,
reachability and classification. CP rows below supply the executable filters.
V-2's S6-A scope is baseline validation/fallback, with selected credit still refused;
it does not depend on the future production supplier.

| ID | Behavior / recipe and variants | Layer / test | Decisive expected oracle and reachability | Classification |
|---|---|---|---|---|
| V-1 | F-A + F-V: omitted mode, explicit Full, Shadow on a narrow diff, unqualified ChangeAware; selected/unknown scope presented to legacy credit; unknown/authority/build paths; immutable qualification root | Script integration; `ChangeAwareReleaseTests.C1313_ModeCredit` | Actual executed Full roster equals literal required union; Shadow also persists narrower diagnostic selection; default/fallback scope Full; selected/unknown legacy credit empty even with all old flags true. Invoke today's credit reader with extra scope fields for a reachable red; non-Full transport needs scaffolding first. Labels `shadow-full`, `unqualified-no-credit`, `qualification-root-unchanged`. | New acceptance; ready S6-A |
| V-2 | F-A/F-V: archived compatible Full B; absent/incompatible B; selected/readiness/legacy/foreign/wrong-platform claim; corrupt digest; invalid Git/nonancestor; historical card/tip advanced | Script integration; `ChangeAwareReleaseTests.C1313_BaselineAuthority` | Valid B pinned, absent or legitimately incompatible receipt yields Full before launch, contradictory claimed identity refuses with zero launch/public writes. Historical B needs immutable publication proof, not current card/tip equality. Each negative restores valid B and changes one fact. | New acceptance; ready S6-A; no selected publication |
| V-3 | F-A: all wrapper/core/reexec/tests hops, identical resume, altered attempt, separate retry; spaces/Unicode roots; missing PowerShell 7; native Windows 5.1 parent | Script/native process; `ChangeAwareReleaseTests.C1313_ParameterHops` | Receiving implementation consumes exact envelope and executes correct roster; tampered digest/A launches no tests; no overwriting prior attempt. Force real self-reexec using differing committed wrapper bytes in owned clone; child observation records consumed JSON, not just argv. Default Full works under 5.1; requested non-Full refuses before work if pwsh missing. | New acceptance; portable CP-3 and required Windows CP-21 |
| V-4 | F-T: two runs, retry attempt, successful/failed/timeout child; overlap case windows; unknown measurements; wrapper+owner double instrumentation | Real child/journal integration; `ReleaseSelectionTimingTests.C1313_RunIdentityAndMeasurements` | Distinct histories, exact expanded UID/count/build joins, one owner per logical terminal, monotonic positive child wall ending after join, class/category windows from evidence; unknown stays null/reason. Current hard-coded IDs/missing wall are reachable defects. | New producer acceptance |
| V-5 | F-T: held/available journal; before-spool, spool/append, torn append, flush/ack, cleanup cuts; replay conflict; failed timing with both passing and failed children; each comparability dimension | Durable recovery integration; `ReleaseSelectionTimingTests.C1313_DurableRecoveryAndEstimates` | Fresh process reads exactly one recovered terminal after cleanup; conflict refuses; incomplete timing remains visible; test verdict/membership unchanged; exact comparable history only, unknown estimate unbudgeted, producing build charged once. No sleep-only crash scheduling. | New recovery acceptance |
| V-6 | F-P: production supplier -> cutter, complete nine-suite discovery and frozen plan -> actual children -> terminal ledger -> bound report; prelaunch and terminal crash cuts | Real isolated Program/DB and owned children; `ChangeAwareReleaseProducerTests.C1313_FullProducer` | Authority/plan absent before supplier invocation, then producer-written in order; every required suite/entry accounted for; restart preserves identity and partial state; independent structural validator accepts complete evidence; master files unchanged; diagnostic fixture cannot publish. | New acceptance; **BR-1/BR-2 binding required** |
| V-7 | F-P: eligible/stale-token/unavailable card; each report barrier; committed-write/lost-response; newer conflicting correlation; receipt-save failure; app restart | Real HTTP/Postgres recipient; `ChangeAwareReleaseProducerTests.C1313_PrepublicationRecovery` | Independent GET returns exact card ID, complete expected report/correlation once, current revision; unrelated text preserved. Same logical intent survives each cut; no receipt/credit until readback; later conflict cannot be overwritten or accepted as old receipt. | New delivery/recovery; **BR-1/BR-2 binding required** |
| V-8 | F-V: valid selected partition; forged omission with repaired hashes; overlap/gap; missing/extra/duplicate/failed/skipped/zero selected UID; client/script missing entry; Full/v1/unknown readers | Script/native evidence contract; `ChangeAwareReleaseTests.C1313_ScopeAuthority` | Partition exactly covers fresh discovery, each selected expanded UID passes once, omission reasons recomputed from pinned B..A. Valid selected scope accepted only by v2 reader with `selectionComplete=true`, `coverageComplete=false`, and omitted cases never reported executed. Negative variants reach specific label before zero-write check. | New acceptance; depends on S6-B contract |
| V-9 | F-V: qualified record; absent/revoked/incompatible; corrupt/foreign evidence; selected attempt offered as next baseline/master readiness; NoReport/diagnostic/seamed states | Script authority contract; `ChangeAwareReleaseTests.C1313_QualificationBinding` | Full fallback before child launch for missing/revoked/incompatible; malformed claimed authority refuses. Selection cannot approve qualification; explicit selected success has no Full/master credit. Test qualifiers are fixture-only immutable records, never live activation. | New acceptance; depends on S6-B |
| V-10 | F-A + real FilterHost: correct A build; B build; dirty A; altered selector bytes; unseen UID; exact One + Rows selection | Native execution integration; `ChangeAwareReleaseTests.C1313_CandidateBinding` | Exactly four allowed expanded results (One plus three Rows), forbidden sibling sentinel absent; source/build mismatch refuses before launch; unseen UID broadens/holds before any child, never silently omitted. Native diagnostic/actual TRX UID union must agree. | New acceptance; full selection authority depends on S6-B |
| V-11 | F-V plus independent remote store: allowlisted selected manifest; private-data canaries; moved A; unknown scope; asset failure/lost response/readback corruption | Publisher contract; `ChangeAwareReleaseTests.C1313_PublicProjection` | Recipient asset contains exact B/A/scope/counts/digests, no host/root/raw timings/body; remote tip equality required, moved tip yields `remote-candidate-moved` and zero writes. Resume uses same tag and recipient digest. | New acceptance; substituted Git/GitHub, no live qualification |
| V-12 | F-A: train member changes plus integration-only path in B..A; same A/different membership; target drift; older Full baseline; failed recomposition; invalid RC ref | Synchronous adapter integration; `ChangeAwareReleaseTests.C1313_TrainAggregate` | Selection equals literal whole-diff impact, membership/B/A frozen; no exact Full baseline at target B or failed recomposition forces Full; mismatched manifest refuses; no task/card/approval/ref writes and no pre-land tag. | New acceptance; actual scheduler excluded |
| V-13 | F-D: script defaults/parameters, schema-3 nine-suite policy, Full/Shadow/qualification/intent/root/timing/rollback and disabled Windmill docs | Documentation contract; `ChangeAwareReleaseDocumentationTests.C1313_RolloutContract` | Text/AST/policy comparison names real capability by landed slice, explicit RC ref and supplier prerequisites; retains moving-tip and master-only constraints. Interim Shadow prose makes no selected-production claim. | New documentation regression; final S7 depends on implemented capability |

### Guards the regression

These exact existing bodies each contribute **one** TUnit execution (no Arguments).
Preserve their current case bodies; the new harness owns new variants.

| ID | Regression / exact test | Decisive assertion and limit |
|---|---|---|
| R-1 | `ReleaseGateRunTests.C599_CreditVerdicts` | Strict typed flags, identity, expected SHA and master/RC separation (24 internal assertions). New scope rejection is V-1, not claimed here. |
| R-2 | `TestTimingReportTests.C1313_EstimatesNeverSelect` | `budget-never-omits`: selected IDs remain A/B/C/D with a huge B estimate and absent C history; digest and unqualified flags unchanged. |
| R-3 | `ReleaseGateAuthorityContractTests.C599B_AllChunks` | Failed sibling, missing chunk, skipped required UID and failed client roster entry each refuse with zero remote writes; restored complete plan publishes to substitute. |
| R-4 | `ReleaseGatePublicationAuthorityTests.C599A_ReceiptReadback` | Unavailable GET, wrong current revision, missing/foreign correlation or wrong card refuses. Full matrix is recipient-validator evidence, not V-7 producer evidence. |
| R-5 | `ReleaseGateRunTests.C599_MasterStateIsolation` | Four master state files byte-identical after RC; RC pointed at default master state root refuses. |
| R-6 | `ReleaseBranchModelGateTests.C1311_MovingTipAuthority` | Separate second-SHA root, unchanged first authority/tag; both publish and promote refuse `remote-candidate-moved`; historical six script hashes exclude branch tip. |
| R-7 | `ReleaseGateAuthorityContractTests.C599B_PinnedPolicy` | Historical schema-2 eight suites remain pinned despite shrunken summary; missing suite/hash/blob/authority refuses with zero remote writes. Current nine-suite contract is separately V-6/V-8. |

### Guard inventory

The historical IDs G/PC-49..64, 69, 75 and 80 retain their obligations; new IDs
start at 81 to avoid renumbering S1-S5. All rows are safety-critical assertions
for this increment, including recovery, producer evidence and honest credit.
Independent checks are split; comma-separated mutation variants below require
separate cycles, not one broad mutant. Unchanged internal CARD-0599 guards not
altered by S6 are covered by the named R methods and their original PC plan;
this appendix does not claim those historical controls ran. If Code changes one
of those guards, add it here with a distinct PC before Review. No new guard is
exempted as 'too small'. The 'none' justification is inapplicable.

| Guard | Plan reference / invariant | Positive control |
|---|---|---|
| G-49 | D-1: Shadow executes the complete Full census | PC-49 |
| G-50 | D-5/activation: missing or revoked qualification cannot reduce execution | PC-50 |
| G-51 | D-4: baseline must be verified Full RC publication | PC-51 |
| G-52 | D-4: absent usable baseline falls back before launch | PC-52 |
| G-53 | D-4: contradictory claimed baseline identity refuses | PC-53 |
| G-54 | D-4/D-7: baseline contract hashes match the current contract | PC-54 |
| G-55 | D-8: candidate discovery belongs to the producing A build | PC-55 |
| G-56 | D-8: omissions are independently recomputed from pinned Git objects | PC-56 |
| G-57 | D-9: omissions cannot be projected as Full scope | PC-57 |
| G-58 | D-7: legacy Full reader cannot consume v2 scoped authority | PC-58 |
| G-59 | Attempt contract: every forwarding edge preserves the frozen envelope | PC-59 |
| G-60 | D-11: train attempt binds complete B/A/membership identity | PC-60 |
| G-61 | D-11: failed recomposition forces Full | PC-61 |
| G-62 | D-6/DL-3: actual entrypoint creates authority before execution | PC-62 |
| G-63 | D-6/DL-4: failed card write leaves report pending | PC-63 |
| G-64 | D-6/DL-4: lost acknowledgement reuses logical report identity | PC-64 |
| G-69 | D-7/public contract: public fields exclude private data | PC-69 |
| G-75 | D-8: dirty or changing candidate source cannot earn authority | PC-75 |
| G-80 | Activation contract: selector cannot approve itself | PC-80 |
| G-81 | D-1: omitted mode remains Full | PC-81 |
| G-82 | D-3/D-5: selector Full-fallback reasons remain authoritative | PC-82 |
| G-83 | Attempt contract: invalid explicit Git object refuses | PC-83 |
| G-84 | Attempt contract: B must be an ancestor of A | PC-84 |
| G-85 | D-4: legitimate baseline policy/platform incompatibility broadens | PC-85 |
| G-86 | Historical B contract: archived Full proof is independent of current B card/tip | PC-86 |
| G-87 | Attempt contract: immutable same-run selection identity | PC-87 |
| G-88 | Attempt contract: retries preserve prior run evidence | PC-88 |
| G-89 | Attempt contract: receiving boundary revalidates content digest | PC-89 |
| G-90 | Attempt contract: receiving boundary revalidates pinned A | PC-90 |
| G-91 | PowerShell contract: missing owned pwsh refuses requested non-Full capability | PC-91 |
| G-92 | PowerShell contract: wrappers preserve 5.1 parsing and transport | PC-92 |
| G-93 | Timing contract: durable event identity includes run and attempt | PC-93 |
| G-94 | Timing/DL-2: start event is durable before child launch | PC-94 |
| G-95 | Timing/DL-2: completion follows child and output join | PC-95 |
| G-96 | Timing contract: actual terminal counts are persisted | PC-96 |
| G-97 | Timing contract: measured host wall is not sum of overlapping case durations | PC-97 |
| G-98 | Timing contract: unavailable measurements stay null with reason | PC-98 |
| G-99 | Timing contract: one producer owns each logical terminal | PC-99 |
| G-100 | Timing/DL-2: spool survives append failure for replay | PC-100 |
| G-101 | Timing/DL-2: flushed but unacknowledged event replays idempotently | PC-101 |
| G-102 | Timing/DL-2: conflicting same-event payload refuses | PC-102 |
| G-103 | Timing contract: persistence failure cannot change test result or membership | PC-103 |
| G-104 | Timing contract: durable root survives checkout/output cleanup | PC-104 |
| G-105 | Timing contract: estimate lookup uses the complete comparability key | PC-105 |
| G-106 | Timing contract: producing build cost counted once; class overlap not added to wall | PC-106 |
| G-107 | Full producer: intent is typed and bound to repository/card/candidate | PC-107 |
| G-108 | Full producer: intent path stays inside durable state without escaping links | PC-108 |
| G-109 | Full producer: conflicting create-once authority is never overwritten | PC-109 |
| G-110 | Full producer: frozen complete discovery/plan precedes execution | PC-110 |
| G-111 | Full producer: actual terminals and digests precede report intent | PC-111 |
| G-112 | Full producer: failed teardown blocks release credit | PC-112 |
| G-113 | DL-4: report intent is durable before HTTP write | PC-113 |
| G-114 | DL-4: unavailable recipient readback cannot make a receipt | PC-114 |
| G-115 | DL-4: revision conflict preserves unrelated content and retries from fresh state | PC-115 |
| G-116 | DL-4: exact bound card and complete correlation are required | PC-116 |
| G-117 | DL-4: receipt revision equals current candidate card revision | PC-117 |
| G-118 | DL-4: typed prepublication receipt cannot be replaced by ReportDelivered | PC-118 |
| G-119 | DL-4: receipt-persistence failure remains recoverable | PC-119 |
| G-120 | Scoped authority: every discovered identity has one disposition | PC-120 |
| G-121 | Scoped authority: every selected UID has a result | PC-121 |
| G-122 | Scoped authority: execution contains no extra UID | PC-122 |
| G-123 | Scoped authority: selected UID executes once | PC-123 |
| G-124 | Scoped authority: failed selected UID blocks credit | PC-124 |
| G-125 | Scoped authority: skipped/unknown selected UID blocks credit | PC-125 |
| G-126 | Scoped authority: zero required execution is never green | PC-126 |
| G-127 | Scoped authority: declared client/script entries stay required | PC-127 |
| G-128 | Scoped authority: NoReport cannot publish | PC-128 |
| G-129 | Scoped authority: diagnostic execution cannot publish | PC-129 |
| G-130 | Scoped authority: seamed execution cannot publish | PC-130 |
| G-131 | Scoped authority: unknown schema or scope refuses | PC-131 |
| G-132 | Activation contract: corrupt claimed qualification refuses | PC-132 |
| G-133 | D-9: selected completeness flags preserve Full-only semantics | PC-133 |
| G-134 | D-9: selected green cannot overwrite master readiness | PC-134 |
| G-135 | D-8: newly discovered unclassified UID broadens or holds before launch | PC-135 |
| G-136 | D-8: exact native selection excludes similarly named siblings | PC-136 |
| G-137 | Public v2 contract: scope/omission identity is complete | PC-137 |
| G-138 | CARD-1311: publication tested SHA equals live rc tip | PC-138 |
| G-139 | CARD-1311: promotion tested SHA equals live rc tip | PC-139 |
| G-140 | D-7: v1 historical hash membership stays six files | PC-140 |
| G-141 | D-7: v2 binds selector/catalog/new producer-consumer bytes | PC-141 |
| G-142 | D-11: reduced train baseline is Full exactly at target B | PC-142 |
| G-143 | D-11: train computes whole B..A including integration edits | PC-143 |
| G-144 | Train contract: adapter cannot authorize tasks/cards/branches/approvals | PC-144 |
| G-145 | Train contract: aggregate metadata cannot bypass long-lived RC admission | PC-145 |
| G-146 | DL-5: publication depends on recipient asset readback | PC-146 |
| G-147 | Timing contract: class/category metadata joins by expanded UID and producing assembly | PC-147 |
| G-148 | S7: docs cannot activate or promise absent selected production capability | PC-148 |
| G-149 | Activation contract: qualification binds evidence/review/activation authority | PC-149 |
| G-150 | D-4: historical baseline requires confirmed immutable publication proof | PC-150 |
| G-151 | Attempt contract: selection files remain outside sync/cleanup scopes | PC-151 |

### Positive controls

Every row means **break G-n by the stated compiling defect; expect the exact
method red at the stated stable assertion**. Each filter is method-scoped and
contains one outer TUnit case; internal variants do not raise MinExecuted.
The case/fixture is the referenced V recipe. Each named variant starts from a
valid control, changes only its named fact, repairs unrelated envelope digests,
and leaves earlier prerequisites valid. The label is an outcome assertion in
that method's executed fixture, never merely a PASS count or source-text search
(except V-13, explicitly a documentation contract).

Code binds new assertion labels to C# assertions or explicit ScriptHarness
requiredRows (with exact inner case name/row count); the release harness emits
literal `C1313 <label>` failure lines. For mutation evidence, the TUnit wrapper
may fail on child exit, but the retained output must identify the named inner
assertion, with nonzero executed count and no setup error. Record the first
failing assertion. A failure at another guard does not prove the intended PC.
Where later tests need a mutated branch to reach the intended check, use a valid
controlled input; never remove another production guard to force reachability.

**Classification/reachability:** all PCs are designed, unexecuted, reachability
unproven. PC rows using V-6/V-7 are additionally BR-1/BR-2 binding required.
New schema/qualification/publication cases remain dependent on that design.
Code runs V/R; ordinary Review judges this inventory before land. After confirmed
land, Mutation runs each variant baseline-green / break / intended-red / restore /
fresh-build / green. All variants must run. No broad mutation filter, parallel
mutation of shared files, snapshot commit/push, or substituted latest-master run.
Keep source frozen during each phase, refresh restored timestamps, preserve full
40-hex landed SHA and external evidence/restoration records. An unbound payload
stays binding required after Code; a green sibling cannot discharge it.

| PC | Break / compiling mutation | Variant cases (each a cycle) | Detecting filter | Expected red assertion / oracle |
|---|---|---|---|---|
| PC-49 | G-49: feed the proposed selected roster into Shadow execution | narrow-provider | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | `shadow-full` — V-1 outcome must remain as specified above. |
| PC-50 | G-50: treat the qualification status as active in the non-active branch | absent,revoked | `/*/*/ChangeAwareReleaseTests/C1313_QualificationBinding` | `unqualified-no-credit` — V-9 outcome must remain as specified above. |
| PC-51 | G-51: admit a selected publication as the Full baseline | selected-baseline | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-full-only` — V-2 outcome must remain as specified above. |
| PC-52 | G-52: leave effective mode ChangeAware when no baseline is resolved | absent-receipt | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-absent-full` — V-2 outcome must remain as specified above. |
| PC-53 | G-53: convert baseline digest mismatch into the missing-baseline fallback | digest-mismatch | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-corrupt-refused` — V-2 outcome must remain as specified above. |
| PC-54 | G-54: skip the selector-contract comparison while leaving all other baseline joins enabled | changed-selector | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `contract-hash-bound` — V-2 outcome must remain as specified above. |
| PC-55 | G-55: accept discovery whose producing SHA is B | foreign-build | `/*/*/ChangeAwareReleaseTests/C1313_CandidateBinding` | `candidate-build-bound` — V-10 outcome must remain as specified above. |
| PC-56 | G-56: trust the supplied not-impacted set after local hashes validate | edited-omission | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `forged-omission-refused` — V-8 outcome must remain as specified above. |
| PC-57 | G-57: emit coverageScope Full for a legitimate selected result with omissions | false-full-scope | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `selected-not-full` — V-8 outcome must remain as specified above. |
| PC-58 | G-58: allow the v2 scoped branch through the v1 Full-only dispatch | legacy-reader | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `legacy-rejects-scoped` — V-8 outcome must remain as specified above. |
| PC-59 | G-59: drop only BaselineSha from one forwarding edge; keep all other fields intact | cut-run,wrapper-impl,impl-core,self-reexec,core-tests,tests-impl | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `hop-baseline-bound` — V-3 outcome must remain as specified above. |
| PC-60 | G-60: omit the named component from reuse identity comparison | target-B,aggregate-A,membership | `/*/*/ChangeAwareReleaseTests/C1313_TrainAggregate` | `aggregate-bound` — V-12 outcome must remain as specified above. |
| PC-61 | G-61: allow prior narrow selection after failed-predecessor recomposition | failed-predecessor | `/*/*/ChangeAwareReleaseTests/C1313_TrainAggregate` | `recomposition-full` — V-12 outcome must remain as specified above. |
| PC-62 | G-62: skip the production New-ReleaseGateAuthority call but keep child execution enabled | authority-call | `/*/*/ChangeAwareReleaseProducerTests/C1313_FullProducer` | `producer-ledger-present` — V-6 outcome must remain as specified above. |
| PC-63 | G-63: mark the report complete on the actual HTTP write failure branch | write-failure | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | `report-pending-until-receipt` — V-7 outcome must remain as specified above. |
| PC-64 | G-64: allocate a new report identity on committed-write/lost-response recovery | response-lost | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | `one-logical-report` — V-7 outcome must remain as specified above. |
| PC-69 | G-69: add host identity from timing to manifest fields and its allowlist | host-leak | `/*/*/ChangeAwareReleaseTests/C1313_PublicProjection` | `public-allowlist` — V-11 outcome must remain as specified above. |
| PC-75 | G-75: ignore the named dirty-source observation while preserving SHA | dirty-at-start,changed-before-execution | `/*/*/ChangeAwareReleaseTests/C1313_CandidateBinding` | `candidate-clean-required` — V-10 outcome must remain as specified above. |
| PC-80 | G-80: write an active qualification as a side effect of selection | self-activation | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | `qualification-root-unchanged` — V-1 outcome must remain as specified above. |
| PC-81 | G-81: change the omitted-mode default to ChangeAware with valid fixture qualification | omitted-mode | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | `default-full` — V-1 outcome must remain as specified above. |
| PC-82 | G-82: discard the selector fallback and retain the narrow union | unknown-path,authority-path,build-path | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | `selector-fallback-full` — V-1 outcome must remain as specified above. |
| PC-83 | G-83: catch explicit-object resolution failure as ordinary absent-baseline fallback | missing-A,missing-B | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `git-object-refused` — V-2 outcome must remain as specified above. |
| PC-84 | G-84: bypass the merge-base ancestry refusal | unrelated-B | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-ancestor-required` — V-2 outcome must remain as specified above. |
| PC-85 | G-85: retain reduced mode after a verified but incompatible baseline | changed-policy,changed-runtime | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-incompatible-full` — V-2 outcome must remain as specified above. |
| PC-86 | G-86: apply candidate-A live card/tip checks to archived B | card-advanced,tip-advanced | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `historical-baseline-accepted` — V-2 outcome must remain as specified above. |
| PC-87 | G-87: overwrite a conflicting existing attempt rather than refuse | changed-selection-digest | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `attempt-conflict-refused` — V-3 outcome must remain as specified above. |
| PC-88 | G-88: reuse the previous run's attempt directory for a new runId | new-run | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `prior-attempt-kept` — V-3 outcome must remain as specified above. |
| PC-89 | G-89: bypass selection-file digest comparison at the receiving implementation | edited-file | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `hop-digest-refused` — V-3 outcome must remain as specified above. |
| PC-90 | G-90: bypass the received A versus candidate comparison | foreign-A | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `hop-sha-refused` — V-3 outcome must remain as specified above. |
| PC-91 | G-91: continue non-Full through the parent when child pwsh resolution fails | Shadow,ChangeAware | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `pwsh-required-before-work` — V-3 outcome must remain as specified above. |
| PC-92 | G-92: replace the file/JSON transport with a legal 5.1 unquoted space-splitting argument join | spaces-unicode | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `windows-path-roundtrip` — V-3 outcome must remain as specified above. |
| PC-93 | G-93: replace the named identity component by a constant | runId,attempt | `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | `timing-run-attempt-distinct` — V-4 outcome must remain as specified above. |
| PC-94 | G-94: launch the child before awaiting start-event persistence | held-flush | `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | `timing-start-before-launch` — V-4 outcome must remain as specified above. |
| PC-95 | G-95: write set-completed before the owned child joins | held-child | `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | `timing-completion-after-join` — V-4 outcome must remain as specified above. |
| PC-96 | G-96: write executed/passed/failed counts as zero despite real TRX | passed-child,failed-child | `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | `timing-counts-bound` — V-4 outcome must remain as specified above. |
| PC-97 | G-97: assign the case duration sum to wallSeconds | overlap | `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | `timing-wall-not-sum` — V-4 outcome must remain as specified above. |
| PC-98 | G-98: convert unavailable wall/count metadata to zero | missing-timestamps | `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | `timing-unknown-not-zero` — V-4 outcome must remain as specified above. |
| PC-99 | G-99: allow wrapper and nightly owner to append distinct terminals for the same logical set | double-instrumentation | `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | `timing-one-owner` — V-4 outcome must remain as specified above. |
| PC-100 | G-100: delete the pending spool in the append-failure catch | after-spool | `/*/*/ReleaseSelectionTimingTests/C1313_DurableRecoveryAndEstimates` | `timing-recoverable-spool` — V-5 outcome must remain as specified above. |
| PC-101 | G-101: append the already-flushed event again during recovery | flush-before-ack | `/*/*/ReleaseSelectionTimingTests/C1313_DurableRecoveryAndEstimates` | `timing-replay-once` — V-5 outcome must remain as specified above. |
| PC-102 | G-102: accept a changed payload for an existing eventId | changed-outcome | `/*/*/ReleaseSelectionTimingTests/C1313_DurableRecoveryAndEstimates` | `timing-event-conflict` — V-5 outcome must remain as specified above. |
| PC-103 | G-103: set child verdict green on timing failure; separately remove a selected set in that catch | failed-child,selection-unchanged | `/*/*/ReleaseSelectionTimingTests/C1313_DurableRecoveryAndEstimates` | `timing-verdict-preserved` — V-5 outcome must remain as specified above. |
| PC-104 | G-104: accept the timing root nested in owned disposable output | nested-root | `/*/*/ReleaseSelectionTimingTests/C1313_DurableRecoveryAndEstimates` | `timing-root-outside-cleanup` — V-5 outcome must remain as specified above. |
| PC-105 | G-105: remove one named key component from the comparison in the estimate adapter | repository,membership,measurement,os,architecture,runtime,concurrency,profile,host | `/*/*/ReleaseSelectionTimingTests/C1313_DurableRecoveryAndEstimates` | `timing-comparable-only` — V-5 outcome must remain as specified above. |
| PC-106 | G-106: charge build cost per class; separately sum class windows into roster wall | duplicate-build,overlap-cost | `/*/*/ReleaseSelectionTimingTests/C1313_DurableRecoveryAndEstimates` | `timing-attempt-cost` — V-5 outcome must remain as specified above. |
| PC-107 | G-107: skip the corresponding intent identity validation | repository,cardId,candidate-A | `/*/*/ChangeAwareReleaseProducerTests/C1313_FullProducer` | `intent-bound` — V-6 outcome must remain as specified above. |
| PC-108 | G-108: bypass resolved containment on ReleaseIntentPath | outside-root,escaping-link | `/*/*/ChangeAwareReleaseProducerTests/C1313_FullProducer` | `intent-contained` — V-6 outcome must remain as specified above. |
| PC-109 | G-109: replace an existing authority with different intent bytes | conflicting-intent | `/*/*/ChangeAwareReleaseProducerTests/C1313_FullProducer` | `authority-create-once` — V-6 outcome must remain as specified above. |
| PC-110 | G-110: skip execution-plan creation but continue launching children | plan-call | `/*/*/ChangeAwareReleaseProducerTests/C1313_FullProducer` | `plan-before-launch` — V-6 outcome must remain as specified above. |
| PC-111 | G-111: declare terminal ledger complete while one required child has no terminal evidence | missing-terminal | `/*/*/ChangeAwareReleaseProducerTests/C1313_FullProducer` | `ledger-before-report` — V-6 outcome must remain as specified above. |
| PC-112 | G-112: record teardownSucceeded true after owned-child cleanup failure | failed-cleanup | `/*/*/ChangeAwareReleaseProducerTests/C1313_FullProducer` | `teardown-required` — V-6 outcome must remain as specified above. |
| PC-113 | G-113: send PATCH before saving report intent | crash-before-intent-flush | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | `intent-before-card-write` — V-7 outcome must remain as specified above. |
| PC-114 | G-114: construct a receipt from the successful write response when GET fails | unavailable-GET | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | `readback-required` — V-7 outcome must remain as specified above. |
| PC-115 | G-115: retry stale replacement content after a 409 instead of rebuilding it from the fresh card | stale-token | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | `concurrent-content-preserved` — V-7 outcome must remain as specified above. |
| PC-116 | G-116: accept the named wrong recipient observation | foreign-card,partial-body,foreign-run | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | `receipt-card-correlation` — V-7 outcome must remain as specified above. |
| PC-117 | G-117: accept an earlier stored receipt after a conflicting card revision | newer-conflict | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | `receipt-current-revision` — V-7 outcome must remain as specified above. |
| PC-118 | G-118: convert the legacy boolean into accepted prepublication authority | boolean-only | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | `typed-receipt-required` — V-7 outcome must remain as specified above. |
| PC-119 | G-119: delete report intent after readback even though receipt saving fails | receipt-write-failed | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | `receipt-save-recoverable` — V-7 outcome must remain as specified above. |
| PC-120 | G-120: skip the named partition validation | gap,overlap | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `partition-complete-disjoint` — V-8 outcome must remain as specified above. |
| PC-121 | G-121: ignore a missing required UID | missing-uid | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `selected-missing-refused` — V-8 outcome must remain as specified above. |
| PC-122 | G-122: ignore an executed UID outside the selected set | extra-uid | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `selected-extra-refused` — V-8 outcome must remain as specified above. |
| PC-123 | G-123: deduplicate actual results before duplicate detection | duplicate-uid | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `selected-duplicate-refused` — V-8 outcome must remain as specified above. |
| PC-124 | G-124: count a Failed result as Passed | failed-uid | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `selected-failed-refused` — V-8 outcome must remain as specified above. |
| PC-125 | G-125: admit nonexecuted/unknown outcomes as passed | skipped-uid,unknown-outcome | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `selected-skip-refused` — V-8 outcome must remain as specified above. |
| PC-126 | G-126: accept an empty actual execution union | zero-executed | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `selected-zero-refused` — V-8 outcome must remain as specified above. |
| PC-127 | G-127: drop a nonnative suite from required roster validation | client,scripts | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `declared-roster-required` — V-8 outcome must remain as specified above. |
| PC-128 | G-128: skip the NoReport refusal for v2 selected evidence | NoReport | `/*/*/ChangeAwareReleaseTests/C1313_QualificationBinding` | `noreport-no-credit` — V-9 outcome must remain as specified above. |
| PC-129 | G-129: skip the diagnostic refusal for v2 selected evidence | diagnostic | `/*/*/ChangeAwareReleaseTests/C1313_QualificationBinding` | `diagnostic-no-credit` — V-9 outcome must remain as specified above. |
| PC-130 | G-130: skip the seamed-provenance refusal for v2 selected evidence | seamed | `/*/*/ChangeAwareReleaseTests/C1313_QualificationBinding` | `seamed-no-credit` — V-9 outcome must remain as specified above. |
| PC-131 | G-131: route an unknown schema/scope to the selected reader | schema,scope | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `unknown-scope-refused` — V-8 outcome must remain as specified above. |
| PC-132 | G-132: treat a qualification digest mismatch as ordinary missing qualification | digest-mismatch | `/*/*/ChangeAwareReleaseTests/C1313_QualificationBinding` | `qualification-corrupt-refused` — V-9 outcome must remain as specified above. |
| PC-133 | G-133: set the named Full completeness flag true despite omitted cases | coverageComplete,allProfileCasesExecuted | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | `selected-completeness-honest` — V-8 outcome must remain as specified above. |
| PC-134 | G-134: write selected-green into the master last-complete-green path | selected-green | `/*/*/ChangeAwareReleaseTests/C1313_QualificationBinding` | `master-state-unchanged` — V-9 outcome must remain as specified above. |
| PC-135 | G-135: silently drop an unclassified candidate UID | new-uid | `/*/*/ChangeAwareReleaseTests/C1313_CandidateBinding` | `new-uid-before-launch` — V-10 outcome must remain as specified above. |
| PC-136 | G-136: use method-prefix matching so One also selects OneLive | prefix-filter | `/*/*/ChangeAwareReleaseTests/C1313_CandidateBinding` | `forbidden-not-launched` — V-10 outcome must remain as specified above. |
| PC-137 | G-137: omit the selected omission digest from public projection | missing-omission-digest | `/*/*/ChangeAwareReleaseTests/C1313_PublicProjection` | `public-scope-bound` — V-11 outcome must remain as specified above. |
| PC-138 | G-138: bypass the publication remote-candidate-moved refusal | moved-tip | `/*/*/ChangeAwareReleaseTests/C1313_PublicProjection` | `moving-tip-zero-writes` — V-11 outcome must remain as specified above. |
| PC-139 | G-139: bypass the promotion remote-candidate-moved refusal | moved-tip | `/*/*/ChangeAwareReleaseTests/C1313_PublicProjection` | `promotion-moving-tip-zero-writes` — V-11 outcome must remain as specified above. |
| PC-140 | G-140: add the new selector helper to the v1 hash membership | historical-v1 | `/*/*/ChangeAwareReleaseTests/C1313_PublicProjection` | `v1-hash-membership` — V-11 outcome must remain as specified above. |
| PC-141 | G-141: omit the named file from the v2 required hash map | selector,catalog,producer-helper,consumer-helper | `/*/*/ChangeAwareReleaseTests/C1313_PublicProjection` | `v2-hash-membership` — V-11 outcome must remain as specified above. |
| PC-142 | G-142: accept a verified older Full baseline that precedes target B | older-full-B | `/*/*/ChangeAwareReleaseTests/C1313_TrainAggregate` | `train-baseline-at-target` — V-12 outcome must remain as specified above. |
| PC-143 | G-143: union per-member selections instead of selecting the aggregate diff | integration-only-path | `/*/*/ChangeAwareReleaseTests/C1313_TrainAggregate` | `aggregate-integration-path` — V-12 outcome must remain as specified above. |
| PC-144 | G-144: write a tag-intent artifact while resolving train selection | tag-intent-write | `/*/*/ChangeAwareReleaseTests/C1313_TrainAggregate` | `train-read-only` — V-12 outcome must remain as specified above. |
| PC-145 | G-145: allow a master ref when TrainManifestPath is present | master-ref | `/*/*/ChangeAwareReleaseTests/C1313_TrainAggregate` | `train-rc-required` — V-12 outcome must remain as specified above. |
| PC-146 | G-146: accept upload success without comparing recipient manifest digest | corrupt-readback | `/*/*/ChangeAwareReleaseTests/C1313_PublicProjection` | `asset-recipient-bound` — V-11 outcome must remain as specified above. |
| PC-147 | G-147: join metadata by display name; separately ignore producing assembly hash | same-display-name,foreign-assembly | `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | `timing-discovery-bound` — V-4 outcome must remain as specified above. |
| PC-148 | G-148: state ChangeAware as the active default without qualification in owner docs | active-default-claim | `/*/*/ChangeAwareReleaseDocumentationTests/C1313_RolloutContract` | `rollout-default-full` — V-13 outcome must remain as specified above. |
| PC-149 | G-149: ignore one named external qualification binding | Full-evidence,Shadow-evidence,Review-identity,activation-revision | `/*/*/ChangeAwareReleaseTests/C1313_QualificationBinding` | `qualification-evidence-bound` — V-9 outcome must remain as specified above. |
| PC-150 | G-150: accept an archived green ledger with no confirmed publication digest | green-without-publication | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-publication-required` — V-2 outcome must remain as specified above. |
| PC-151 | G-151: place selection-attempt.json under the disposable checkout output | cleanup-owned-output | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `attempt-survives-cleanup` — V-3 outcome must remain as specified above. |

For PC-62/110/111, assert labeled artifact absence/order immediately after observing
the production boundary, before opening an absent file or validating later stages.
This makes missing production evidence an intended assertion rather than fixture
setup failure. For PC-92, compare the receiving process's decoded tuple at
`windows-path-roundtrip`; a script parse or launch failure is not that control.
PC-141's four mutation targets are `scripts/lib/test-selection.ps1`,
`tests/test-selection-policy.json`, `scripts/lib/release-run-evidence.ps1`, and
`scripts/lib/release-selection.ps1`, respectively. Its valid oracle also asserts
the complete literal v2 membership, including changed entrypoints; it does not
derive the expected membership from the producer map under test.

### Out of scope

- No whole Unit lane, whole assembly, nightly/RC suite, browser/provider canary or
  live qualification is run by this appendix's ordinary selection. Full RC/paired
  Shadow qualification is a separately commissioned acceptance operation; the
  historical 40–45 minute Linux roster and four-minute Windows roster are not
  measurements for these rows and confer no current qualification.
- No real release, RC branch/tag writes, deployment, Windmill registration or
  scheduling, credentials, new server queue, train scheduler/admission/approval or
  card projection. Master-only source publication does not activate product hosts.
- S1-S5 algorithms and old unrun PCs/platform qualification are not discharged.
  Existing unrelated CARD-0599/1311 internal guards retain their old controls.
  R-6 is the changed seam's narrow regression witness, not a fresh tag-system audit.
- The test fixture's loopback Git/GitHub recipient store proves supplied remote
  observations and new public bytes, not GitHub availability/authentication or
  real schedule delivery. F-V synthetic ledgers prove validation only.
- No session delivery changes or parking changes. There is no new waiting seat
  and no automatic release deadline while parking is disabled.
- No source changes or execution in TestDesign. Static authoring checks below
  validate document structure, not test coverage, PC reachability or runtime green.

### Checkpoints

Closed ordinary selection, IDs preserved from Plan. **CP-8..19 and final CP-20
claims are held by BR-1/BR-2 and slice dependencies**, not optional skips. CP-1..4
may be commissioned as S6-A alone; CP-5..7 as S6-T after A. CP-21 may be
commissioned earlier for A's Windows compatibility without claiming final S7.
A partial dispatch reports only its authorized scope and retains all other rows.

One row names one isolated build or a same-After reuse and exactly one filter.
Min is TUnit executions, not labels/variants/time. All new methods are deliberately
non-parameterized: exactly one outer execution; the native child UID census is
separate. Existing R methods' bodies confirm one execution each. Code must retain
this shape or amend/bind the table before running; discovery has not occurred here.
CP-3 and CP-21 deliberately execute the same method on different operating systems.

Use eligible Linux for CP-1..20; CP-8/9 require disposable Docker/Postgres and the
owned runner. CP-21 requires native Windows 5.1 plus installed PowerShell 7, including
5.1 parent -> pwsh child and spaces/Unicode path variants. A Linux skip or parser
simulation is not CP-21 evidence. All process rows run serially, no main/Pty
co-scheduling; fixture process limiters also apply inside each assembly.
Each fixture invocation stays inside its existing bounded child budget; multiple
recovery arms may run sequentially inside the outer method. If one invocation
cannot fit, split the fixture arms without broadening filters or raising deadlines,
and record the revised row/count/cost before execution.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S6-A | `tests/Antiphon.Tests -> bin-c1313-s6a/` | portable-mode | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | V-1 | exactly 1 executed, 0 failed/skipped | 1 | 7 | true |
| CP-2 | S6-A | `CP-1` | portable-baseline | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | V-2 | exactly 1 executed, 0 failed/skipped | 1 | 3 | true |
| CP-3 | S6-A | `CP-1` | portable-hops | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | V-3 | exactly 1 executed, 0 failed/skipped | 1 | 2 | true |
| CP-4 | S6-A | `CP-1` | portable-full-credit | `/*/*/ReleaseGateRunTests/C599_CreditVerdicts` | R-1 | exactly 1 executed, 0 failed/skipped | 1 | 1 | true |
| CP-5 | S6-T | `tests/Antiphon.Tests -> bin-c1313-s6t/` | portable-timing-producer | `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | V-4 | exactly 1 executed, 0 failed/skipped | 1 | 7 | true |
| CP-6 | S6-T | `CP-5` | portable-timing-recovery | `/*/*/ReleaseSelectionTimingTests/C1313_DurableRecoveryAndEstimates` | V-5 | exactly 1 executed, 0 failed/skipped | 1 | 4 | true |
| CP-7 | S6-T | `CP-5` | portable-budget-regression | `/*/*/TestTimingReportTests/C1313_EstimatesNeverSelect` | R-2 | exactly 1 executed, 0 failed/skipped | 1 | 1 | true |
| CP-8 | S6-B | `tests/Antiphon.E2E -> bin-c1313-s6b-recipient/` | linux-producer-recipient | `/*/*/ChangeAwareReleaseProducerTests/C1313_FullProducer` | V-6 | exactly 1 executed, 0 failed/skipped | 1 | 12 | true |
| CP-9 | S6-B | `CP-8` | linux-receipt-recovery | `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | V-7 | exactly 1 executed, 0 failed/skipped | 1 | 12 | true |
| CP-10 | S6-B | `tests/Antiphon.Tests -> bin-c1313-s6b/` | portable-full-authority | `/*/*/ReleaseGateAuthorityContractTests/C599B_AllChunks` | R-3 | exactly 1 executed, 0 failed/skipped | 1 | 7 | true |
| CP-11 | S6-B | `CP-10` | portable-receipt-readback | `/*/*/ReleaseGatePublicationAuthorityTests/C599A_ReceiptReadback` | R-4 | exactly 1 executed, 0 failed/skipped | 1 | 2 | true |
| CP-12 | S6-B | `CP-10` | portable-master-isolation | `/*/*/ReleaseGateRunTests/C599_MasterStateIsolation` | R-5 | exactly 1 executed, 0 failed/skipped | 1 | 1 | true |
| CP-13 | S6-C | `tests/Antiphon.Tests -> bin-c1313-s6c/` | portable-scoped-authority | `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | V-8 | exactly 1 executed, 0 failed/skipped | 1 | 8 | true |
| CP-14 | S6-C | `CP-13` | portable-qualification | `/*/*/ChangeAwareReleaseTests/C1313_QualificationBinding` | V-9 | exactly 1 executed, 0 failed/skipped | 1 | 4 | true |
| CP-15 | S6-C | `CP-13` | portable-candidate-roster | `/*/*/ChangeAwareReleaseTests/C1313_CandidateBinding` | V-10 | exactly 1 executed, 0 failed/skipped | 1 | 3 | true |
| CP-16 | S6-C | `CP-13` | portable-public-scope | `/*/*/ChangeAwareReleaseTests/C1313_PublicProjection` | V-11 | exactly 1 executed, 0 failed/skipped | 1 | 5 | true |
| CP-17 | S6-C | `CP-13` | portable-moving-tip | `/*/*/ReleaseBranchModelGateTests/C1311_MovingTipAuthority` | R-6 | exactly 1 executed, 0 failed/skipped | 1 | 3 | true |
| CP-18 | S6-C | `CP-13` | portable-pinned-full-policy | `/*/*/ReleaseGateAuthorityContractTests/C599B_PinnedPolicy` | R-7 | exactly 1 executed, 0 failed/skipped | 1 | 2 | true |
| CP-19 | S6-D | `tests/Antiphon.Tests -> bin-c1313-s6d/` | portable-train-aggregate | `/*/*/ChangeAwareReleaseTests/C1313_TrainAggregate` | V-12 | exactly 1 executed, 0 failed/skipped | 1 | 7 | true |
| CP-20 | S7 | `tests/Antiphon.Tests -> bin-c1313-s7/` | portable-rollout-contract | `/*/*/ChangeAwareReleaseDocumentationTests/C1313_RolloutContract` | V-13 | exactly 1 executed, 0 failed/skipped | 1 | 6 | false |
| CP-21 | S7 | `tests/Antiphon.Tests -> bin-c1313-s7-windows/` | windows-forwarding | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | V-3 Windows | exactly 1 executed, 0 failed/skipped | 1 | 8 | true |

Code commits/pushes the red-first test slice before its fix and the resulting
fixed source before long checkpoints. Bootstrap the checkpoint tool under
`scripts/build-slot.ps1`, then use its existing `run --plan` and `wait`
protocol; never launch build/test drivers outside the slot. An exact example for
the independently commissioned A slice is:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1313-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1313-tool/ -- run --plan docs/superpowers/plans/2026-10-19-card-1313-s6-s7-test-design.md --rows CP-1,CP-2,CP-3,CP-4 --expected-source-sha <committed-full-sha>
```

Wait until terminal (75 means still running); slot exit 4 is not run, never
permission to retry unleased. Preserve unedited CHECKPOINT/provenance lines and
validate receipts at the exact committed SHA. No broad rerun after green.
Base-commit triage is only the exact failing method. Code/Review run
`scripts/check-evidence-diff.ps1` over base..HEAD. Keep generated evidence ignored,
inventory/delete only producer-owned outputs after all consumers finish, and
retain historical failed outcomes and their costs. Static plan coverage needs
the new files/labels bound after implementation; TestDesign does not call it
green while those sources do not exist.

### Cost

All numbers below are **estimates**, not measurements. Ordinary Code V/R floor
is **105 minutes = sum of CP-1..21 EstimatedMinutes**, 21 outer executions and
eight isolated builds. This updates Plan's 91-minute proposal by +14 minutes:
CP-6 +1 (durable cuts), CP-8 +2 (real producer), CP-9 +7 (recipient recovery),
CP-14 +1 (qualification joins), CP-16 +3 (asset recovery). Six portable
Antiphon.Tests builds at 4 minutes, one E2E build at 6, one Windows build at 6
account for **36 build minutes + 69 execution minutes** within the 105, not on
top. Allow **5 minutes setup/tool bootstrap** separately.

The Plan's **66-minute red-first allowance** for thirteen new witnesses is
retained as a separate estimate, not a PC run and not an ordinary green claim.
Re-estimate a producer witness after BR-1/BR-2 binding if it exceeds that allowance.
Authoring and independent Review time are outside execution floors.

Mutation uses fresh isolated builds for red/restored-green, not reused dirty
outputs. Build/test minutes and exact methods are itemized below. Each listed
cycle includes red build+test, 0.25 minute restoration verification, and green
build+test. Baseline is one same-landed-source green per method/lane before its
first mutation; a later baseline is repeated when a source/environment change
requires it. All 90 PCs are mandatory. There are **134 design variants**, of
which PC-92 is Windows-only; PC-59's six and PC-91's two also need native Windows
cycles. Thus **133 portable + 9 Windows = 142 actual variant cycles**.
No shared-file mutants are batched.

| Detecting filter | PC IDs | Portable cycles | Build / test minutes per invocation | Baseline minutes | Red / restore / green minutes per cycle | Subtotal minutes |
|---|---|---:|---:|---:|---:|---:|
| `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | PC-49, PC-80, PC-81, PC-82 | 6 | 4 / 2 | 6 | 12.25 | 79.5 |
| `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | PC-51, PC-52, PC-53, PC-54, PC-83, PC-84, PC-85, PC-86, PC-150 | 12 | 4 / 3 | 7 | 14.25 | 178 |
| `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | PC-59, PC-87, PC-88, PC-89, PC-90, PC-91, PC-92, PC-151 (PC-92 costed only below) | 13 | 4 / 2 | 6 | 12.25 | 165.25 |
| `/*/*/ReleaseSelectionTimingTests/C1313_RunIdentityAndMeasurements` | PC-93, PC-94, PC-95, PC-96, PC-97, PC-98, PC-99, PC-147 | 11 | 4 / 2 | 6 | 12.25 | 140.75 |
| `/*/*/ReleaseSelectionTimingTests/C1313_DurableRecoveryAndEstimates` | PC-100, PC-101, PC-102, PC-103, PC-104, PC-105, PC-106 | 17 | 4 / 4 | 8 | 16.25 | 284.25 |
| `/*/*/ChangeAwareReleaseProducerTests/C1313_FullProducer` | PC-62, PC-107, PC-108, PC-109, PC-110, PC-111, PC-112 | 10 | 6 / 6 | 12 | 24.25 | 254.5 |
| `/*/*/ChangeAwareReleaseProducerTests/C1313_PrepublicationRecovery` | PC-63, PC-64, PC-113, PC-114, PC-115, PC-116, PC-117, PC-118, PC-119 | 11 | 6 / 12 | 18 | 36.25 | 416.75 |
| `/*/*/ChangeAwareReleaseTests/C1313_ScopeAuthority` | PC-56, PC-57, PC-58, PC-120, PC-121, PC-122, PC-123, PC-124, PC-125, PC-126, PC-127, PC-131, PC-133 | 18 | 4 / 3 | 7 | 14.25 | 263.5 |
| `/*/*/ChangeAwareReleaseTests/C1313_QualificationBinding` | PC-50, PC-128, PC-129, PC-130, PC-132, PC-134, PC-149 | 11 | 4 / 4 | 8 | 16.25 | 186.75 |
| `/*/*/ChangeAwareReleaseTests/C1313_CandidateBinding` | PC-55, PC-75, PC-135, PC-136 | 5 | 4 / 3 | 7 | 14.25 | 78.25 |
| `/*/*/ChangeAwareReleaseTests/C1313_PublicProjection` | PC-69, PC-137, PC-138, PC-139, PC-140, PC-141, PC-146 | 10 | 4 / 5 | 9 | 18.25 | 191.5 |
| `/*/*/ChangeAwareReleaseTests/C1313_TrainAggregate` | PC-60, PC-61, PC-142, PC-143, PC-144, PC-145 | 8 | 4 / 2 | 6 | 12.25 | 104 |
| `/*/*/ChangeAwareReleaseDocumentationTests/C1313_RolloutContract` | PC-148 | 1 | 4 / 1 | 5 | 10.25 | 15.25 |
| `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` (Windows) | PC-59, PC-91, PC-92 | 9 Windows | 6 / 4 | 10 | 20.25 | 192.25 |

Portable subtotal **2358.25 minutes**; Windows **192.25**; Mutation execution
floor **2550.5 minutes** (42h 30m 30s), before discovery/authoring and slot contention.
This is deliberately costed as exact-method execution: packed methods repeat all
their internal acceptance arms for each control. An implementation may propose
smaller independently bound methods to lower it, but cannot silently use inner
case switches to skip the declared detecting method or omit variants.

Combined floors: **5 setup + 105 ordinary V/R + 2550.5 PC = 2660.5 minutes**
(44h 20m 30s). Including the separate 66-minute red-first allowance gives
**2726.5 minutes** (45h 26m 30s). Builds are already inside the V/R and PC numbers.
Unused producer estimates remain estimates, not a budget excuse to skip binding.

Ordinary build reuse saves **54 estimated minutes** versus rebuilding for all
21 rows: twelve extra Antiphon.Tests builds at 4 minutes plus one extra E2E
build at 6. Baseline reuse within each immutable method/lane group saves
**1085 estimated minutes** versus rebuilding/running a new baseline before every
one of the 142 cycles (compute per method's cycles minus one, times its baseline).
Additional PC batching savings **0**: most controls share the same PowerShell
helpers/methods, so independent batching is not established. Product rollout
savings **0 measured minutes**; no fresh paired Full/Shadow release data exists.

### Handoff audit

- Guard inventory: **guards=90, mapped=90, missing=0, duplicate PC maps=0**.
  PC IDs inherited from the original S6-S7 map are all retained; PC-62/63/64 now
  target the new producer class instead of the nonexistent old method.
- **All PCs run: required in post-land Mutation; 0 executed here.** 90 PC IDs,
  134 design variants / 142 lane cycles are pending. Ordinary V/R: 0/21 rows
  executed here, as required by this documentation-only brief.
- Payload/fixture binding: **BR-1 and BR-2 remain binding required**. A complete
  guard map is not complete producer verification. No whole-card Code-ready,
  release-ready, qualified, activated or PC-clean claim is made.
- Next for the complete increment: Plan binds supplier and bounded nine-suite
  producer fixture/structural-validator seam, then TestDesign confirms exact
  payloads, assertions, reachability and costs. S6-A's CP-1..4 may proceed in a
  separate scoped Code task; preserve master-only and the live-rc-tip equality
  rule throughout.
