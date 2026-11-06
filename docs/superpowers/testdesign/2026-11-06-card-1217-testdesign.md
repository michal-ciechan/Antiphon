# CARD-1217 conditional checkpoint receipt verification design

Date: 2026-11-06. TestDesign task `097141bd`, branch `feat/card-task-097141bd`.
Source/Plan base: `1a15a15b2fc2f103c693fb4dffc896d7586e741b`.

This adds the verification contract to the unchanged
[fix design](../plans/2026-11-06-card-1217-checkpoint-receipts-plan.md).
It follows the CARD-1737 [R1](2026-11-06-card-1737-r1-testdesign.md) and
R2 (`docs/superpowers/testdesign/2026-11-06-card-1737-r2-testdesign.md`) structure (inspected on
`origin/feat/card-task-a43ac839` and `origin/feat/card-task-0ba30c05`).
The prerequisite record on `origin/feat/card-task-36bb3715` correctly identifies
two validators: `ReportValidator.Validate` and `validate-checkpoint-receipt.ps1`.
Neither currently implements the exception. Original Code/landing owner remains
`36bb3715-4e5e-4038-9a81-5ec024e60a99`.

The Plan's 34 PC variants are individually numbered below. Additional controls
cover the other conditional admission/evidence guards; they are not silently
credited by those 34. All are **pending post-land Mutation**. This TestDesign
ran **zero builds, tests and PCs**. Source inventories are not compiled or executed
rosters. Payloads, exact patches and producer binaries remain **binding required
after Code**; none is a mechanically eligible pack yet.

Verification refinements: CP-10 selects **31**, not 35, results; four methods in
`RunnerDefaultGuidanceTests` share a file with `TaskPlatformGuidanceTests` but do
not match its class filter. CP-20 remains **39** results. CP-9 moves after S3 so
its real producer can resolve the consumer catalog at its actual build/source
commit. This changes verification ordering, not the feature's fix design.

## Verification design

### Inspection

`T/` = `tools/Antiphon.Checkpoints/`; `C/` = `tests/Antiphon.Tests/Checkpoints/`;
`S/` = `tests/Antiphon.Tests/Scripts/`. Proposed class prefix is
`Antiphon.Tests.Checkpoints.C1217ConditionalSkip` throughout.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| Plan decisions, schema, truth table, slices, V/R, 18 PC groups/34 variants, performance and checkpoint rows; Code prerequisite record; CARD-1737 R1/R2 verification, wiring, selectors and performance manifests | V-1..17 and all PCs; no historical execution credit |
| `docs/project-context.md`; testing/build checkpoint schema, runner, PC/restoration, classification and timing; test-performance-standards R1..9; orchestration stage order and D-2; performance Debug `c318d8edfc97e046bb9a332ec34c76973f9a4f4a:docs/investigations/2026-11-05-debug-s5b-1-performance.md` | Closed bounded selections, source provenance, slot admission separate from execution |
| T/Manifest model, loader, validator; importer optional-column admission and selection; T/Trx/TrxReport complete | Present YAML normalization/ignored properties and permissive legacy TRX must not authorize conditional evidence -> V-1..4/R-1 |
| T/Report/ReportValidator, ReportModel, ReportMerger, CheckpointLine; Program.Validate/unique JSON check; CheckpointApp.BuildReport/ToReportRow; RunState/RowProgress | Both envelopes, source conjunction, version dispatch, persisted evidence, no merge laundering -> V-5..10/13..15, R-2/3 |
| RowRunner source/build admission, launch, TRX, rerun, floor/roster and result paths; RunScheduler row request, state copies, terminal/closed-row paths; run-checkpoint preflight, build binding, host launch, TRX judgment and Write-Trailer | Fresh proof, genuine host exit, output/refusal persistence -> V-12/13/15/16 |
| `scripts/validate-checkpoint-receipt.ps1` complete, source/repeat helper boundaries | Independent PowerShell judgment and real CLI exit/output -> V-14/15, R-3 |
| C/CheckpointManifestTests, CheckpointImportTests, TrxReportTests, CheckpointRepeatConfigurationTests; RowRunnerTests, RunSchedulerTests, ReportWriterTests, ReportMergerTests; CheckpointSourceExecutionTests; S/RunCheckpointSourceScriptTests and RunCheckpointRepeatScriptTests, including local fixtures | Nearest tests for the new files; retained behavior and source fences -> R-1..3 |
| C/CheckpointTestSupport (FakeDriver/WriteResults), CheckpointTestScope/base, CheckpointFixtureProcess, CheckpointRepeatFixture/HostTests, CheckpointRoster.Compiled; FilterHostTests and FilterHost csproj; S/OuterRowBuildStamp | Existing WriteResults lacks strict execution IDs/notExecuted/reasons. New literal corpus must not reuse it unmodified. Owned scratch/process lifecycle; staged child precedent -> all new fixtures |
| ChannelOutboundStorageTests metadata and the focused source-root/file-link/directory-link/output-swap bodies and their FileLink classifier vectors; FileLink complete; WindowsUnitPlatformAdmissionTests Audit, ValidDocument, launch/stamp checks, compiled roster and native child | Exact reason and two count-1 identities; actual storage behavior -> V-11/16/17, R-6. Other storage bodies are unchanged; their source method/Arguments inventory supplies the pending full consumer roster |
| CheckpointToolProjectContractTests, TestLaneCategoryGuardTests, TestClassificationGuardTests; C1729LayeringGuardTests and per-class repository fixture; MechanicalPcContractTests, TaskPlatformGuidanceTests; NativeLinuxPolicyExecutionTests, LiveLaneRunnerTests, ScriptHarness; TestClassificationMetadata and policy project dispositions | R-4/5; class versus file census; 5-argument mechanical case; actual live-lane digest sentinel |
| `tests/Antiphon.Tests/Antiphon.Tests.csproj`, T/Antiphon.Checkpoints.csproj; policyHash reader and live-lane protected-input digest | Tool -> HostCleanup only. Test project stages existing child/tool inputs; classification/pins move together |

New files: six test classes (Contract, Validation, Probe, Producer, Parity, Host)
and one private fixture `C1217ConditionalSkipFixture.cs` in C/. Do not change shared
helpers to accept incomplete evidence. Contract/Validation are Unit; Probe/Producer/
Parity/Host are Integration with `ParallelLimiter<ProcessSpawnLimit>`. Case vectors
inside a method are not extra TUnit executions. Pure cases launch no subprocess.
ProbeTests includes pure classifier vectors but is Integration because other
methods exercise owned filesystem/process operations; no conflicting method Unit tag.

#### Frozen declaration and wire schema

The permission file is exactly `tests/checkpoint-conditional-skips.json`, a
regular Git blob at the caller's explicit expected full lowercase SHA (40 or 64
hex). Read tree mode and blob with bounded argument-list Git calls from the trusted
source root. Never get that root from report JSON, read the working catalog for
authority, fetch, checkout, or change Git configuration. Clear inherited Git routing
variables for that read. Byte hash is SHA-256 over the exact blob bytes, not JSON
reserialization. The reviewed runner/filesystem remains trusted; hashes are joins,
not authentication of fabricated whole evidence.

Exact catalog object: `{ "schemaVersion": 1, "contracts": [ ... ] }`.
Each contract has exactly `id`, `probe`, `project`, `filter`, `expectedTotal`,
`minExecuted`, `skips`; each skip has exactly `className`, `method`, `count`,
`reason`. Ordinal comparisons throughout. Reject duplicate JSON keys, IDs and
qualified skip identities, unknown/missing fields, null/wrong scalar types, empty
strings, wildcard class/method, unsupported version/probe, nonpositive/overflowing
counts, impossible total/floor; every contract's skips array is nonempty. No whitespace trimming or reason normalization.
Limits: 65,536 raw bytes, 64 contracts, 64 skip identities/contract, 2,048 reason
characters. The floor is positive and total strictly exceeds the skip sum.
`project` is repository-relative with forward slashes and no traversal; its exact
string, filter and floor must equal the row's declared tuple. Argument instances
are authorized only as the full multiplicity of a qualified method.

Table `ConditionalSkips` and YAML `conditionalSkips` are an optional scalar ID.
Absent means legacy; supplied blank/null/list/map/number means refusal. In an
opted-in table every row cell must contain a valid ID; do not interpret an empty
cell as authorization. The ordinary checkpoint table below has no such column.
Reject command, skip-only, repeat>1, known-flaky rerun and merged-attempt conditional
evidence. A schema-4 run also refuses a repeated ordinary sibling. Schema 4 is
used only if conditional evidence exists; standalone version 3 likewise. Legacy
report 2/3 and standalone 1/2 keep their old rules and reject conditional members.
`SourceEvidence.version` remains 1.

New row JSON member `conditional` is either absent/null for an ordinary row or
this exact object (the same member in RowRunResult, RowProgress and ReportRow):

```text
version: 1
contractId: nonempty ordinal string
catalogSha256: 64 lowercase hex
contract: exact resolved catalog entry, diagnostic copy only
binding:
  sourceSha, sourceFingerprint, buildBindingSha256: normalized digests
  runId, rowId: exact nonempty producer identities
  attempt: 1
  nonce: 32 lowercase hex, cryptographically random and fresh per row launch
  project, filter: exact contract/row strings
  minExecuted, expectedTotal: exact contract integers
  executor: existing ProcessIdentity tuple (host, boot, pidNamespace, pid, startUtcTicks)
  workingDirectory, tempRoot: absolute effective launch context
  rowStartedAtUtc, rowEndedAtUtc: UTC round-trip timestamps
probePath: absolute owned regular evidence-file path
probeSha256: 64 lowercase hex
trxSha256: 64 lowercase hex
hostExitCode: actual observed child exit, zero required
hostInvocations: 1
```

The immutable `conditional-probe.json` in the row results directory has exactly:
`version:1`, `probe:"file-symlink-create-v1"`, `probeVersion:1`, the same
`binding` launch identity (excluding rowEndedAtUtc, which is known only later),
`observation` and no report/trx digest. Observation fields are:

```text
status: available | unavailable | unknown
os: windows | linux | macos | other
operation: create-file-symlink
exceptionKind: none | unauthorized-access | win32 | io | other
hResult: signed int32 or null
nativeErrorCode: int32 or null
linkVerified: boolean
targetVerified: boolean
child: existing ProcessIdentity tuple
sameToken: boolean
tokenIdentity: opaque SID/effective-uid identifier, no privileges/environment dump
startedAtUtc, endedAtUtc: UTC round-trip timestamps
elapsedMilliseconds: nonnegative int64
exitCode: int32
timedOut, canceled: boolean
cleanup: complete | failed | unknown
diagnosticCode: bounded stable code, max 128 ASCII characters
```

No circular hash: finalize and hash probe bytes first; run the host; hash raw TRX;
then publish row/envelope. The immutable probe has launch identity, not the future
TRX hash; the row joins both hashes. Bind row times to actual producer observations.
Probe times must lie inside the row interval and end before host launch. A copied
probe with a changed row nonce or source is foreign even if its digest is recomputed.
Probe/read failures retain diagnostic evidence and never produce conditional clean.
Use the existing bounded regular-file reader ceilings; cap new probe JSON at 64 KiB.

A usable observation requires the named probe/version/operation, the joined owned
child identity, matching effective token/context, no cancellation/timeout and
confirmed child exit/cleanup. Missing or inconsistent raw facts are unknown.
Available requires successful creation, verified link and owned sentinel target,
exit zero and complete cleanup. Unavailable requires Windows and exactly one of
the three FileLink exception shapes: UnauthorizedAccessException or IOException
with signed HRESULT `-2147023582` (`0x80070522`), or Win32Exception native error
1314, during creation. Timeout/cancellation/cleanup uncertainty overrides any
earlier status to unknown. All other failures are unknown. Recompute status in
each validator from these raw facts, without running a probe on the validator host.

Standalone v3 has exactly the new envelope fields `version:3`, `source`, `name`,
`runId`, `project`, `filter`, `minExecuted`, `expect`, `receipt`, `exitCode`,
`executed`, `passed`, `failed`, `skipped`, `trx`, `conditional`, `timings`.
Tool report 4 retains the existing report fields and adds row `minExecuted`,
`expect`, `conditional`; project resolves through the named report build and must
match conditional binding/catalog. Both validators dispatch both new envelopes.
Legacy standalone support remains PowerShell-owned. No ordinary row gets skip
permission by being beside a conditional row. Inner/outer source, build, cleanup,
failure, floor, Expect and all four receipt counts remain conjunctive.

Strict TRX requires the TeamTest/2010 TestRun root, single Results, TestDefinitions,
ResultSummary/Counters sections; unique definitions and nonempty unique execution
IDs; exactly one qualified TestMethod for every referenced testId; no display-name
fallback. Only Passed and NotExecuted can qualify. Require exact declared total,
observed counter equality for total/executed/passed/failed/notExecuted, positive
executed=passed, failed=0, total=executed+skipped. Each skipped result has exactly
one result-local Output/DebugTrace equal to `Skipped: ` plus the literal reason.
Retain raw bytes/reasons/counts; never turn a skip into a pass.

### Delivery inventory

No session, messaging queue or remote recipient path changes. The changed async
path is **local checkpoint work/evidence handoff**; its recipient is the validator
reading durable artifacts. The inventory is therefore explicit rather than
crediting a returned DTO, queued work item, ack or green state.

| Producer -> destination | Persistence/recovery boundary | Durable join and observable receipt |
|---|---|---|
| Import/selection -> scheduler pending row -> RowRunner | resolved YAML/request before executor; validate reference before build-slot/driver; source re-admission after waiting | expected SHA + catalog hash + row ID; V-1/13 read saved selection and observe actual selected row launch |
| RowRunner or standalone script -> owned probe child -> parent | immutable probe file before host launch; write/readback failure stops launch; no reuse after interruption | run/row/attempt/nonce + source/build/context/process tuples; V-12/13 read and hash real file, observe owned child exit and cleanup |
| Parent -> real test host -> raw TRX | host exit/drain, fresh TRX and matching digest before conditional verdict | same launch identity; V-16/17 inspect complete qualified result multiset and raw reasons |
| Row result -> state/report or source.json -> independent validators | save/reopen state and finalized envelope; interrupted/partial publication cannot qualify; fresh run after crash uses fresh nonce/probe | V-13..17 reopen artifacts, both validators return expected exits, bytes unchanged |

For local queued work, V-13 uses the real RunScheduler queue with (a) a build/row
held busy behind a barrier and (b) an already eligible row on a ready build. A
completed queue insertion is insufficient: release the barrier, await the actual
row, reopen its probe/TRX/state/report and validate. Use exception/write-failure
cuts after saved selection, after probe execution before probe publication, after
probe publication before host launch, after host exit before envelope publication,
and before state/report readback. Recover by a fresh run, not by inventing a
resume-execution API: this source has observation/wait/reconstruction, not a
general resumable row executor. Every cut retains nonqualifying partial evidence;
the fresh run has a different nonce and its own TRX. G-33 and the publication
controls below protect these handoffs.

No real product queue was substituted or left before recipient proof: it is
outside this component. If scope expands into such delivery, return to TestDesign
for the real queue, busy/already eligible recipient, crash/enqueue-failure at every
handoff and recipient evidence. Session input requires the matching complete
UserPrompt transcript. A request/event/Sent/ack never proves delivery.

Substitutes: hand-authored unavailable observations prove judgment only; fake
driver/source/slot fixtures prove local propagation/refusal only; deterministic
exceptions prove interruption recovery, not power-loss durability. The staged real
host proves runtime TRX plus actual probe on that host, not Windows 1314 on Linux.
No DotnetShim fixture, synthetic OS flag or test-authored build stamp earns a
native qualifying receipt.

### Proves it works now

All new methods have one `[Test]` and no Arguments: 25 portable methods plus two
explicit native acceptance methods. Below, method names after the class suffix
are exact. Every negative has a qualifying control and one changed semantic fact.
Independent expected answers are literal; never serialize a production answer to
create the oracle. `P(case,surface,envelope)` means exit=2/non-null refusal, no
`CHECKPOINT SOURCE VALID`, and unchanged input files. Assert nonqualification
**before** any diagnostic reason. Pure admission uses no admitted contract/row;
producer uses no qualified result and retained evidence. A reason-only red fails.

| V | Exact method(s) | Recipe / variants / case / layer / oracle and reachability |
|---|---|---|
| V-1 | ContractTests.DeclarationRoundTrips | W-ROUND: Markdown -> YAML -> load -> selection -> saved request, exact ID/tuple; absent field stays legacy. Unit. Read saved bytes, not only DTO; supplied valid ID must survive |
| V-2 | ContractTests.MalformedDeclarationsRefuse | W-DECL: absent positive; blank/null/list/map/number field negatives; strict catalog duplicate/type/shape/count/cap vectors. Unit. Admission result refuses with zero slot/driver calls. Unknown probe is otherwise a valid contract |
| V-3 | ContractTests.StrictTrxPreservesIdentityAndReason | W-TRX: literal A/U documents below, colliding display names and qualified identities; strict projection matches every result, multiplicity and raw reason. Unit. No production fixture serializer |
| V-4 | ContractTests.LegacyParsingIsUnchanged; UnsupportedCombinationsRefuse | W-LEGACY: diagnostic missing counters/name fallback still works; conditional command, skip-only, repeat, rerun, mixed-repeat and merged shapes refuse before work. Unit. Each forbidden combination has a valid ordinary twin |
| V-5 | ValidationTests.UnavailableExactRosterQualifies; AvailableRequiresAllDeclaredPassed | A and U controls qualify with literal raw counts; A+skip refuses. Unit resolved-contract judgment; each declared instance is present. This is the acceptance behavior absent at the base |
| V-6 | ValidationTests.UnavailableRequiresEveryDeclaredSkip | U -> one/all declared results pass, one missing, partial parameterized method, multiplicity swap. Unit. Recompute counters/hashes so the roster/outcome guard is decisive |
| V-7 | ValidationTests.ForeignSkipAndReasonRefuse | U -> foreign class/method/case, extra skip, altered/prefixed/suffixed/whitespace reason, absent/duplicate trace. Unit. P; exact reason and qualified identity, not substring |
| V-8 | ValidationTests.MalformedTrxCannotQualify | duplicate/missing definition/TestMethod/execution ID/sections/counters, wrong root/ns, display-name substitution, counter drift, illegal ordinary outcomes. Unit. P; raw evidence preserved |
| V-9 | ValidationTests.ProbeBindingDisagreementRefuses | one binding/hash/time/raw-status change at a time, missing/unreadable/foreign/stale probe; catalog copy broadening. Unit resolved inputs; authority read itself is V-15. P with every unrelated join valid |
| V-10 | ValidationTests.SourceAndFailureFencesRemain; LegacyAndUnsupportedShapesRemainClosed | dirty/changed/unknown/foreign source, build mismatch, cleanup refusal, failed sibling/host, no executions, wrong floor/Expect, duplicate receipt token, legacy versions/conditional members, ordinary schema-4 skip. Unit. P, including all-pass unknown probe |
| V-11 | ProbeTests.OnlyWindows1314IsUnavailable | W-CLASSIFY: the 15 FileLink exception vectors x two OS arms, plus successful operation; C# and actual PS classifier. Integration. Feed result into the qualifying U judgment: unknown cannot grant eligibility; first oracle is false eligibility, then status |
| V-12 | ProbeTests.ProductionProbeObservesAndCleansOwnedFiles; ProbeTimeoutOrCleanupFailureIsUnknown | W-PROBE: production factory and actual effective temp root; inspect link/owned target at operation barrier and complete removal after joined child. Timeout/cancel/launch/drain/cleanup-failure cases become unknown and cannot qualify. Integration; fake OS branches are not native evidence |
| V-13 | ProducerTests.RowPersistsBoundProbeAndTrx; ResumeAndReportKeepEvidence; RowRefusesMismatchedOrUnknownProof | W-WIRE: real scheduler/row/persistence graph with controlled IO/driver for exception cuts; busy and eligible rows, no-build reuse, two fresh run IDs. Integration. Read saved probe/TRX/state/report, compare nonce/digests/counts, preserve invalid bytes. Do not mistake state green for qualification |
| V-14 | ParityTests.BothValidatorsAgreeOnTruthTable; BothValidatorsRejectTamperedEvidence | W-PARITY: identical independent corpus, both new envelopes and both validators; A/U exit0, every negative P. Integration. Batch real PS script invocations in one owned host; no mock of script judgment |
| V-15 | ParityTests.BothValidatorsUseCommittedPolicy; StandaloneProducerBindsProof | W-AUTH: real two-commit mini repository plus working edit, wrong root and hostile Git routing; expected first commit stays authoritative. W-STANDALONE: script producer and exact phase-boundary mismatch, diagnostic shim explicitly refused for native credit; actual no-shim producer covered V-16. Integration |
| V-16 | HostTests.PortableHostProducesQualifyingReceipt; UnavailableFixtureCannotMasqueradeAsNativeProof | W-HOST after S3: actual current-build Antiphon.Tests child, focused three storage methods, real probe, no nested build, both real producer surfaces and validators. Linux requires observed available and 3/3/0/0; on Windows capability is observed, not forced. Synthetic unavailable/shim claim refuses. Integration |
| V-17 | HostTests.NativeWindowsAvailableReceiptQualifies; NativeWindowsUnavailableReceiptQualifies | W-NATIVE-A/U: exact explicit Windows methods, separate real capability placements, focused storage child, matching source/build and raw 3/3/0/0 or 1/1/0/2. Prerequisite mismatch fails, never skips/passes. Pending outside Linux Code |

#### BOUNDARY WIRING: fixture and parity corpus

The new helper owns scratch via CheckpointTestBase; register every child/work item
and await bounded exit/drain/cleanup. Use argument arrays and CheckpointFixtureProcess
or the existing owned ScriptHarness process machinery. Share immutable strings
and compiled image per assembly/run, one two-commit repository per authority method,
private evidence copies per vector. No DB, AppHost, live runner or nested build.
Retain test artifacts needed by the outer receipt before teardown.

Literal fixture contract `c1217-fixture-v1`: project `tests/Fixture`, filter
`/*/Fixture/Cases/*`, expectedTotal=3, minExecuted=1; skips `Fixture.Cases.LinkA`
and `Fixture.Cases.LinkB`, each count1, reason `fixture capability unavailable`.
`Fixture.Cases.Sentinel` is always ordinary. A: all3 Passed, counters3/3/3/0/0
(total/executed/passed/failed/notExecuted). U: Sentinel Passed, A/B NotExecuted,
counters3/1/1/0/2, exact DebugTrace. Give every result a distinct testId and
executionId and every definition a TestMethod. Duplicate display names are
intentional and harmless. For multiplicity use a separate source contract
total4/floor1 with A count2 and B count1, plus Sentinel; swap A,A,B -> A,B,B
without changing total/skips. Parameter display names may differ; method count
must not collapse. These fixture contracts are committed only in owned miniature
repositories, not the production catalog.

Each matrix row below expands over report4 and standalone3, then C and P validator.
`selected=all` and explicit row selection are both exercised for report4; duplicate,
empty and missing requested IDs refuse independently. A second ordinary sibling
is included in the report corpus to test heading/selected-row conjunction.

| Case family | Exact one-fact variants; literal expected verdict |
|---|---|
| TT | A valid=0; U valid=0; available+U=2; unavailable+A=2; unavailable+one pass=2; unknown+A=2; unknown+U=2; missing probe=2 |
| ID | swap multiplicity; missing declared instance; foreign namespace/class/method; case-changed method; undeclared ordinary skip; partial parameter outcome: all2 |
| REASON | one character, prefix, suffix, trailing space, CRLF, missing trace, two equal traces, reason moved to StdOut: all2 |
| XML | root, namespace, zero/two Results, zero/two TestDefinitions, duplicate definition ID, unknown testId, missing/two TestMethod, empty class/method, missing/duplicate executionId, zero/two Summary/Counters: all2 |
| COUNT | each required counter absent, noninteger, negative, overflow or +1; total wrong but self-consistent; failed ordinary; Error/Timeout/Aborted/unknown ordinary outcome; zero executed; floor unmet: all2 |
| JOIN | each binding field independently differs; probe digest mismatch; TRX digest mismatch; copied contract differs; catalog bytes/hash differs; stale/future/reversed times; raw/status mismatch; missing/nonregular/unreadable/oversize proof: all2 |
| ENVELOPE | conditional fields under report2/report3/standalone1/standalone2, unknown version, duplicate JSON key/token, missing line, wrong name/commit/source/build token, each of four count tokens wrong, repeat/rerun/attempt/merged mismatch: all2 |
| SOURCE | source version/end/capture/state/SHA/fingerprint/dirtiness/build mismatch, cleanup refusal, failed report/sibling/host exit, wrong Expect, ordinary schema4 skip: all2 |
| AUTH | wrong root, missing commit/blob, symlink/submodule catalog, uncommitted broader catalog, coherent copied broader contract+digest, GIT_DIR/WORK_TREE/INDEX routing to broader repo: all2 for broadened evidence; original A/U still0 when validation tree is merely dirty |

An uncommitted validation-tree edit is not itself disqualifying: authority is the
requested commit. In the working-catalog mutant world, commit C1 authorizes A/B;
C2 authorizes A/C. Worktree holds C2, caller pins C1, evidence claims C1 but uses
C2's catalog hash/contract/skip roster consistently. Only the authority selection
is false. Fixed validation refuses; working-tree authority mutant wrongly qualifies.

Do not launch one PowerShell process per pure vector. One owned pwsh batch invokes
the real script with `& <literal-script-path> -Evidence ... -ExpectedSourceSha ...
-SourceRoot ...`, resets/captures LASTEXITCODE and output for every invocation and
emits exactly one record per `(case,envelope,selection)`. Script `exit` must be
contained by script invocation; missing records fail. Four subprocess smoke cases
(A/U and an invalid vector for each envelope, packed by method) verify actual
process exit handling; C# uses Program.RunAsync/Validate entry plus its captured
output. No batch-level exit0 can substitute for individual vector outcomes.
Compare the emitted key multiset to the literal expected corpus before accepting
the method. Preserve stdout/stderr as diagnostics, never as the sole oracle.

Recompute hashes and all unaffected counts after a deliberate semantic edit;
otherwise an early digest mismatch would mask the guard being tested. For a digest
guard change only the nominated digest. For PC-4 tuple binding, change project OR
filter OR floor consistently in every evidence copy but leave the committed
contract unchanged. For PC-13 use U with Sentinel outcome `Mystery`, while counters
and envelope say one ordinary pass. The mutant maps that unknown outcome to Passed
at projection; subsequent counters/ordinary-pass checks then succeed. Merely
removing an error message or retaining an unknown outcome is not the nominated
mutation. For unknown-status PC-11 use a valid unknown observation (other failure)
whose raw classification also computes unknown, not a mislabeled unavailable one.

#### BOUNDARY WIRING: producers, persistence and actual hosts

S2 tests enter `CheckpointApp.CreateRun/ExecuteAsync` and RunScheduler/RowRunner,
then save/read RunStateStore and BuildReport/ReportWriter. Observe declaration
admission **before** slot probe/acquire, build, probe or host calls. Within an
admitted row the order is source/build recheck -> fresh nonce/probe -> owned proof
write/readback -> host -> raw TRX digest/judgment -> state -> report. Bind injected
cuts to exact file/phase, not arbitrary sleeps or all writes. Two queued rows share
a build but never a probe nonce. Unknown proof permits no host launch. Probe IO
failure cannot erase prior diagnostic files. Interrupting publication never
reconstructs a qualified row from a successful host exit alone.

`ResumeAndReportKeepEvidence` means reopen persisted state/report in a new reader,
and start a fresh run for unfinished work. The second run/row must actually reach
its probe boundary. PC-17 substitutes cached operation facts at the acquisition boundary, before the
normal wrapper binds the current nonce/time. First run observes unavailable and
qualifies U. Second run actually observes available while its controlled driver
returns U: fixed code refuses; the cached-unavailable mutant wrongly qualifies.
The positive twin is second-run available+A. Merely returning a whole old proof
would be masked by nonce/time guards and is not an eligible mutation. Assert
second-run nonqualification before fresh nonce/probe-call counts; the second
probe acquisition boundary must actually be reached.

S2 has no consumer catalog in the real source yet. Its miniature-repository,
fake-driver tests are explicitly synthetic wiring evidence. **CP-9 executes after
S3**, against that committed catalog and the real outer checkpoint build. Reuse the
actual Antiphon.Tests DLL/apphost and matching project/OutputPath/properties from
the outer resolved manifest/build receipt. Require the stamp already exists and
matches the outer SHA; do not call OuterRowBuildStamp's write-missing fallback,
copy a DLL into a miniature repo and manufacture a clean stamp, or inject SourceCapture.
Read and hash the actual parent/child image and stamp before/after execution.
The focused child filter has only the three existing storage methods, so it cannot
recurse into HostTests. No FilterHost extension is necessary for this design;
its existing staging/real-child pattern is the nearest fixture, not new coverage.

Run the real RowRunner no-build path and the real standalone `run-checkpoint.ps1
-NoBuild` path at the same committed source with genuine probe, fresh result roots,
matching build properties and no shim. The nested no-build host is explicit work
within the parent process-test row, not a hidden rebuild. The script's slot lease
is retained and accounted separately; do not use `-NoSlot` for this native proof.
Production probe/test children share effective token, cwd and TEMP/TMP/TMPDIR.
Select a unique owned temp root for both; remove only owned entries. Set the
production-runner guard to its dead endpoint, disable interpreter checks, and
remove unrelated inherited test/PC/repeat overrides. This fixture starts no product
server. Persist complete child TRX, exit, probe, envelope and both validator results.

`StandaloneProducerBindsProof` uses miniature source and a diagnostic shim arm
to prove shim refusal, plus the actual post-host judge seam below. Its native
counterpart belongs to W-HOST in CP-9; CP-5 cannot require the S3 catalog. PC-18 must target the actual standalone post-host conditional-judgment
function, called with a real valid producer observation and a private TRX copy
changed from A to U with its hash/counts coherently updated. It must not target
the earlier shim rejection: the exact judge function is invoked from the producer
path and the test, with an observer proving the call. Primary assertion is
`preserved:standalone-mismatch:qualified=false`. Complete genuine script-to-envelope
wiring is independently required by W-HOST. This narrow seam is a declared
substitute for racing an actual host's TRX file while the script consumes it.

### Guards the regression

| R | Exact existing selection and decisive assertion |
|---|---|
| R-1 | CP-2/3 full four classes in roster below. Preserve `falls_back_when_counters_missing`, class-qualified joins, optional-column refusal, YAML round trip, build reuse and repeat/default arguments. Conditional strictness cannot rewrite legacy diagnostic parsing |
| R-2 | CP-6 full RowRunner/RunScheduler/ReportWriter/ReportMerger. In particular `skipped_class_still_meets_the_roster` stays diagnostic green, `rows_wait_for_the_build_source_receipt_before_readiness` observes held receipt then actual green, builds never overlap, every row field persists, legacy latest-per-row merge stays supported |
| R-3 | CP-7/8 full existing source/script classes. Clean control accepted; dirty/changed/unknown/foreign/missing-stamp/failed/cleanup-refused inputs refused in both validators, original evidence retained; repeat5 remains one native host invocation and five complete sets |
| R-4 | CP-10 exact six classes: XOR/registry errors empty; exact tool->HostCleanup reference; layering map/parse/verdict valid; mechanical routing/PC ownership and platform instructions intact. Floor31, not35 |
| R-5 | CP-11 native policy child reports expected exact platform results/side effects; LiveLaneRunnerTests includes WrapperOnlyPaidManifest's actual full-inputs/policy-edit/reader-edit/pin sentinels, not only a mocked hash |
| R-6 | CP-12 original directory-link method's FileLink classification/target-preservation assertions and `C1210_Audit_rejects_empty_or_incomplete_outcomes` raw identity/reason/counter/exit negatives remain independent of production conditional judgment |

All V methods also become regression tests. No old assertion is loosened. Counts
below refer to outer executions; assertions and nested TRX rows are separately
inventoried. Pre-existing reds must be reproduced at the base with the exact
failing methods before attribution, not by rerunning an assembly.

### Guard inventory

The primary table has one distinct guard/control per Plan variant; the C and P
implementations are independently bypassable and never share a kill. Additional
admission/evidence predicates are enumerated separately below. These are the
conditional feature's safety boundaries, not a claim to mutation-test all existing
Git, source, build-slot or cleanup implementation. Their unchanged internals are
R-1..6 prerequisites; the new conjunction with them is explicitly controlled.

The temporary mutation gate is defined only in external candidate patches, never
landed production or test source:

```csharp
private static bool C1217Mutation(string id) => string.Equals(
    Environment.GetEnvironmentVariable("ANTIPHON_C1217_PC"), id, StringComparison.Ordinal);
```

```powershell
function Test-C1217Mutation([string]$Id) {
    return [string]::Equals($env:ANTIPHON_C1217_PC, $Id, [StringComparison]::Ordinal)
}
```

Every ID below is the exact environment selector. A predicate cut is compiling
`if (!C1217Mutation("PC-n") && invalid) return refusal;` (or its throw equivalent),
and PowerShell `if (-not (Test-C1217Mutation 'PC-n') -and $invalid) { throw ... }`.
Value substitutions below use the same conditional/ternary form at the named
assignment. Keep selector-off code behavior identical. No mutation changes test
expectations, qualification-source facts or unrelated guards. Code binds concrete
symbols/lines and a compiling patch; Review checks that a broad helper bypass has
not replaced the specified narrow cut. If implementation splits a listed predicate
into independently bypassable guards, split controls and cost before Mutation;
do not assert the conceptual inventory is the final source census.

### Positive controls

**Every listed control must run** baseline/break/red/restore/green after land.
Executed here=0. Code runs ordinary V/R, not mutants. Review judges design/ordinary
evidence before land. Formal Mutation binds author C and landed L, path/symbol,
patch SHA-256, selector, producer/staged DLL hashes, exact method, reached marker
and preservation assertion. Snippets are recipes, not compilation evidence.

The following table is both the 1:1 guard inventory and the primary PC list.
`CT`, `PT`, `QT`, `WT` abbreviate ContractTests, ProbeTests, ParityTests,
ProducerTests under `C1217ConditionalSkip`. `F(Class.Method)` expands exactly to
`/*/*/C1217ConditionalSkip<Class>/<Method>`; no wildcard/class/suite filter and
Min=1 for each phase. `red:P(case)` names the qualification-preservation assertion
defined above, not its refusal reason.

| Guard -> PC selector | Plan variant; compiling defect | Exact method; decisive red and reachability |
|---|---|---|
| G-1 -> PC-1 | PC-1 importer: malformed supplied field becomes absent | CT.MalformedDeclarationsRefuse; `preserved:decl:admitted=false`, empty string field otherwise valid |
| G-2 -> PC-2 | PC-2: skip unsupported-probe predicate | CT.MalformedDeclarationsRefuse; `preserved:unknown-probe:admitted=false`, only probe ID wrong |
| G-3 -> PC-3 | PC-3.C: choose working catalog bytes instead of C1 blob | QT.BothValidatorsUseCommittedPolicy; red:P(working-policy,C,each), coherent C2 evidence world |
| G-4 -> PC-4 | PC-3.P: same authority substitution in PS | QT.BothValidatorsUseCommittedPolicy; red:P(working-policy,P,each) |
| G-5 -> PC-5 | PC-4.C: bypass exact row tuple comparison | QT.BothValidatorsRejectTamperedEvidence; red:P(row-project/C); filter/floor variants also run, all other joins aligned |
| G-6 -> PC-6 | PC-4.P: same tuple bypass in PS | QT.BothValidatorsRejectTamperedEvidence; red:P(row-project/P) |
| G-7 -> PC-7 | PC-5.C: missing TestMethod resolves from full qualified testName | QT.BothValidatorsRejectTamperedEvidence; red:P(display-fallback/C); valid IDs/counters/reasons |
| G-8 -> PC-8 | PC-5.P: same fallback in PS | QT.BothValidatorsRejectTamperedEvidence; red:P(display-fallback/P) |
| G-9 -> PC-9 | PC-6.C: bypass counter/observed equality | QT.BothValidatorsRejectTamperedEvidence; red:P(counter/C); summaries/envelope agree with one another |
| G-10 -> PC-10 | PC-6.P: same counter cut | QT.BothValidatorsRejectTamperedEvidence; red:P(counter/P) |
| G-11 -> PC-11 | PC-7.C: use ordinal Contains instead of exact reason equality | QT.BothValidatorsRejectTamperedEvidence; red:P(reason-suffix/C); one trace, suffix only |
| G-12 -> PC-12 | PC-7.P: same ordinal substring defect | QT.BothValidatorsRejectTamperedEvidence; red:P(reason-suffix/P) |
| G-13 -> PC-13 | PC-8.C: compare Distinct identities instead of multiset | QT.BothValidatorsRejectTamperedEvidence; red:P(AAB-to-ABB/C); total and skip counts unchanged |
| G-14 -> PC-14 | PC-8.P: same set defect | QT.BothValidatorsRejectTamperedEvidence; red:P(AAB-to-ABB/P) |
| G-15 -> PC-15 | PC-9.C: available arm accepts exact declared skips | QT.BothValidatorsAgreeOnTruthTable; red:P(available-U/C); actual probe still available |
| G-16 -> PC-16 | PC-9.P: same outcome-arm defect | QT.BothValidatorsAgreeOnTruthTable; red:P(available-U/P) |
| G-17 -> PC-17 | PC-10.C: unavailable arm accepts proper subset | QT.BothValidatorsAgreeOnTruthTable; red:P(unavailable-one-pass/C); declared instances remain present |
| G-18 -> PC-18 | PC-10.P: same subset defect | QT.BothValidatorsAgreeOnTruthTable; red:P(unavailable-one-pass/P) |
| G-19 -> PC-19 | PC-11.C: unknown judgment uses unavailable arm | QT.BothValidatorsAgreeOnTruthTable; red:P(unknown-U/C); raw recomputation also unknown |
| G-20 -> PC-20 | PC-11.P: same status defect | QT.BothValidatorsAgreeOnTruthTable; red:P(unknown-U/P) |
| G-21 -> PC-21 | PC-12.C: omit nonce equality only | QT.BothValidatorsRejectTamperedEvidence; red:P(nonce/C); all other joins/hashes valid |
| G-22 -> PC-22 | PC-12.P: same nonce cut | QT.BothValidatorsRejectTamperedEvidence; red:P(nonce/P) |
| G-23 -> PC-23 | PC-13.C: map unknown ordinary outcome to Passed at strict projection | QT.BothValidatorsRejectTamperedEvidence; red:P(unknown-outcome/C); frozen Mystery world above |
| G-24 -> PC-24 | PC-13.P: same outcome projection defect | QT.BothValidatorsRejectTamperedEvidence; red:P(unknown-outcome/P) |
| G-25 -> PC-25 | PC-14.C: admit legacy conditional envelope into new judgment | QT.BothValidatorsRejectTamperedEvidence; red:P(downgrade/C); otherwise valid U; legacy blanket skip path must not mask cut |
| G-26 -> PC-26 | PC-14.P: same dispatch defect | QT.BothValidatorsRejectTamperedEvidence; red:P(downgrade/P) |
| G-27 -> PC-27 | PC-15.C-access: Windows Win32 access-denied5 also classifies unavailable | PT.OnlyWindows1314IsUnavailable; `preserved:win32-5:C:eligible=false` before status assertion |
| G-28 -> PC-28 | PC-15.P-access: same whitelist extension in PS | PT.OnlyWindows1314IsUnavailable; `preserved:win32-5:P:eligible=false` |
| G-29 -> PC-29 | PC-15.C-os: omit Windows condition for valid1314 shape | PT.OnlyWindows1314IsUnavailable; `preserved:linux-1314:C:eligible=false` |
| G-30 -> PC-30 | PC-15.P-os: same OS cut in PS | PT.OnlyWindows1314IsUnavailable; `preserved:linux-1314:P:eligible=false` |
| G-31 -> PC-31 | PC-16-timeout C: retain earlier unavailable when owned probe times out | PT.ProbeTimeoutOrCleanupFailureIsUnknown; `preserved:timeout:host-launched=false`; inject timeout after valid1314 observation before completion |
| G-32 -> PC-32 | PC-16-cleanup C: retain unavailable after unconfirmed cleanup | PT.ProbeTimeoutOrCleanupFailureIsUnknown; `preserved:cleanup:host-launched=false`; only cleanup fails |
| G-33 -> PC-33 | PC-17: scheduler substitutes cached operation facts before normal current binding | WT.ResumeAndReportKeepEvidence; `preserved:fresh-run:qualified=false` fails on second available+U run; cached unavailable facts are consumed with fresh identity |
| G-34 -> PC-34 | PC-18: standalone conditional judge returns success for mismatch | QT.StandaloneProducerBindsProof; `preserved:standalone-mismatch:qualified=false`; direct actual judge cut described above, shim admission left intact |

For roster mutations, distinguish multiplicity/quota admission from completeness.
A,A,B -> A,B,B breaches B's quota while preserving cardinality; the proper-subset
case breaches completeness while staying within every quota. The nominated
mutation must change that operation (set-collapse for the former, subset admission
for the latter), not merely suppress a diagnostic before another exact-equality
check. Bind both to the actual judge and prove each reaches its primary assertion.

PC-31/32 cover the C# parent completion reducer. The standalone script has
independent timeout/cleanup reduction and receives PC-101/102 below; one C# kill
cannot cover it. Each feeds its actual result into the producer admission boundary.
Similarly PC-5/6's tuple is one ordinal tuple check; separate field guards require
separate controls.

Additional guard/control inventory and the complete roster/performance manifest
follow below. These controls prevent the original 34 from hiding untested safety
assertions in schema, source binding or persistence.

The supplemental rows use the same selector helper and F expansion. Each C/P
cell is a distinct guard, selector and run, not one shared PC. `cut` means bypass
only the nominated refusal predicate; valid A/U twins must still qualify. For
admission vectors C uses CT.MalformedDeclarationsRefuse or
CT.UnsupportedCombinationsRefuse; P uses the actual-script QT method shown, so
Unit tests never spawn PowerShell. All are Plan strict-declaration, binding,
source-conjunction or producer-persistence requirements.

| Guard -> distinct PC | Compiling defect / case | Exact method and preservation assertion |
|---|---|---|
| G-35 -> PC-35 (C); G-36 -> PC-36 (P) | cut duplicate JSON-property rejection; identical duplicate `contractId`, otherwise U | QT.BothValidatorsRejectTamperedEvidence; red:P(duplicate-key,C/P) |
| G-37 -> PC-37 (C); G-38 -> PC-38 (P) | catalog unique-ID check accepts two identical contract IDs | C: CT.MalformedDeclarationsRefuse, `preserved:duplicate-contract:admitted=false`; P: QT.BothValidatorsUseCommittedPolicy, red:P(duplicate-contract,P) |
| G-39 -> PC-39 (C); G-40 -> PC-40 (P) | catalog unique-qualified-identity check accepts duplicated skip entry | C: CT.MalformedDeclarationsRefuse, `preserved:duplicate-identity:admitted=false`; P: QT.BothValidatorsUseCommittedPolicy, red:P(duplicate-identity,P) |
| G-41 -> PC-41 (C); G-42 -> PC-42 (P) | substitute successful strict-schema validation for invalid-shape result; unknown catalog property with valid values elsewhere | C: CT.MalformedDeclarationsRefuse, `preserved:catalog-shape:admitted=false`; P: QT.BothValidatorsUseCommittedPolicy, red:P(catalog-shape,P) |
| G-43 -> PC-43 (C); G-44 -> PC-44 (P) | substitute successful catalog-limits validation; reason length2049, legal shape/total, matching literal TRX | C: CT.MalformedDeclarationsRefuse, `preserved:catalog-limit:admitted=false`; P: QT.BothValidatorsUseCommittedPolicy, red:P(catalog-limit,P) |
| G-45 -> PC-45 (C); G-46 -> PC-46 (P) | accept catalog mode120000 as regular blob; blob contents themselves are valid JSON | QT.BothValidatorsUseCommittedPolicy; red:P(blob-mode,C/P), real Git tree object; never follow a filesystem symlink |
| G-47 -> PC-47 (C); G-48 -> PC-48 (P) | missing explicit expected SHA falls back to HEAD | QT.BothValidatorsUseCommittedPolicy; red:P(expected-sha-required,C/P); HEAD has qualifying catalog |
| G-49 -> PC-49 (C); G-50 -> PC-50 (P) | absent committed catalog falls back to copied row contract | QT.BothValidatorsUseCommittedPolicy; red:P(missing-catalog,C/P), all copied evidence otherwise U |
| G-51 -> PC-51 (C); G-52 -> PC-52 (P) | accept conditional command row at combination boundary | C: CT.UnsupportedCombinationsRefuse, `preserved:command:admitted=false`; P: QT.BothValidatorsRejectTamperedEvidence, red:P(command,P) |
| G-53 -> PC-53 (C); G-54 -> PC-54 (P) | omit repeated ordinary sibling from schema4 combination scan | C: CT.UnsupportedCombinationsRefuse, `preserved:mixed-repeat:admitted=false`; P: QT.BothValidatorsRejectTamperedEvidence, red:P(mixed-repeat,P) |
| G-55 -> PC-55 (C); G-56 -> PC-56 (P) | accept known-flaky conditional row at combination boundary | C: CT.UnsupportedCombinationsRefuse, `preserved:known-flaky:admitted=false`; P: QT.BothValidatorsRejectTamperedEvidence, red:P(known-flaky,P) |
| G-57 -> PC-57 (C); G-58 -> PC-58 (P) | remove merged-attempt rejection; copied valid conditional row under merged envelope | QT.BothValidatorsRejectTamperedEvidence; red:P(merged,C/P) |
| G-59 -> PC-59 (C); G-60 -> PC-60 (P) | omit exact expectedTotal equality; total4 fixture with two sentinels/two skips loses one sentinel, counters consistently3 and floor1 met | QT.BothValidatorsRejectTamperedEvidence; red:P(expected-total,C/P) |
| G-61 -> PC-61 (C); G-62 -> PC-62 (P) | accept impossible floor5/total4 during declaration admission | C: CT.MalformedDeclarationsRefuse, `preserved:impossible-floor:admitted=false`; P: QT.BothValidatorsUseCommittedPolicy's actual declaration-reader boundary, same assertion; do not let later executed floor mask it |
| G-63 -> PC-63 (C); G-64 -> PC-64 (P) | strict conditional outcome judge treats Failed ordinary result as Passed and adjusts its projected counters | ValidationTests.SourceAndFailureFencesRemain for C; QT.BothValidatorsRejectTamperedEvidence for P; red:P(failed-ordinary,C/P), invoke actual judgment boundary before enclosing failed-report fence |
| G-65 -> PC-65 (C); G-66 -> PC-66 (P) | missing proof read falls back to diagnostic observation copy | QT.BothValidatorsRejectTamperedEvidence; red:P(missing-proof,C/P), fixture includes tempting diagnostic copy |
| G-67 -> PC-67 (C); G-68 -> PC-68 (P) | omit probe-byte digest equality only | QT.BothValidatorsRejectTamperedEvidence; red:P(probe-digest,C/P) |
| G-69 -> PC-69 (C); G-70 -> PC-70 (P) | omit TRX-byte digest equality only | QT.BothValidatorsRejectTamperedEvidence; red:P(trx-digest,C/P) |
| G-71 -> PC-71 (C); G-72 -> PC-72 (P) | omit non-nonce binding-tuple comparison; only rowId differs, digest recomputed | QT.BothValidatorsRejectTamperedEvidence; red:P(binding-row,C/P); all individual fields have ordinary vectors |
| G-73 -> PC-73 (C); G-74 -> PC-74 (P) | omit observation/row interval containment; proof ends before row starts, internally ordered | QT.BothValidatorsRejectTamperedEvidence; red:P(stale-interval,C/P) |
| G-75 -> PC-75 (C); G-76 -> PC-76 (P) | trust stored unavailable instead of comparing raw recomputation; raw facts available | QT.BothValidatorsRejectTamperedEvidence; red:P(raw-status,C/P) |
| G-77 -> PC-77 (C); G-78 -> PC-78 (P) | strict TRX structural validator accepts duplicate identical definition; subsequent projection resolves first | QT.BothValidatorsRejectTamperedEvidence; red:P(duplicate-definition,C/P) |
| G-79 -> PC-79 (C); G-80 -> PC-80 (P) | available roster check ignores declared multiplicity; total4 A-count2/B-count1 fixture replaces one A with ordinary sentinel, unique IDs/counters valid | QT.BothValidatorsAgreeOnTruthTable; red:P(available-missing-instance,C/P) |
| G-81 -> PC-81 (C); G-82 -> PC-82 (P) | omit receipt four-count tuple equality; only passed token differs | QT.BothValidatorsRejectTamperedEvidence; red:P(receipt-count,C/P) |
| G-83 -> PC-83 (C); G-84 -> PC-84 (P) | new conditional success returns before old source/build/cleanup/failure conjunction; cleanupRefusals alone nonempty | QT.BothValidatorsRejectTamperedEvidence; red:P(conjunction-cleanup,C/P); ordinary source variants remain R-3 |
| G-85 -> PC-85 (C); G-86 -> PC-86 (P) | ordinary schema4 sibling inherits conditional skip permission | QT.BothValidatorsRejectTamperedEvidence; red:P(ordinary-sibling-skip,C/P), selected=all and selected conditional row |
| G-87 -> PC-87 (C); G-88 -> PC-88 (P) | ignore actual host exit7 with otherwise valid U/TRX | WT.RowRefusesMismatchedOrUnknownProof for C; QT.StandaloneProducerBindsProof for P; `preserved:host-exit:qualified=false` |
| G-89 -> PC-89 (C); G-90 -> PC-90 (P) | admit DotnetShim-produced conditional receipt as native | HostTests.UnavailableFixtureCannotMasqueradeAsNativeProof; `preserved:shim:C/P:qualified=false`, reach native-admission boundary with otherwise coherent source fixture |
| G-91 -> PC-91 (C); G-92 -> PC-92 (P) | available probe verification accepts an ordinary file at link path | PT.ProductionProbeObservesAndCleansOwnedFiles; `preserved:not-link:C/P:host-launched=false`; real private filesystem object, creation operation substituted, no fake available status |
| G-93 -> PC-93 (C); G-94 -> PC-94 (P) | proof-write/readback error still admits host using in-memory facts | WT.RowRefusesMismatchedOrUnknownProof for C; QT.StandaloneProducerBindsProof for P; `preserved:proof-write:C/P:host-launched=false` |
| G-95 -> PC-95 | state/report publication failure is returned as successful settlement from host success | WT.ResumeAndReportKeepEvidence; `preserved:partial-publication:settlement-succeeded=false`; actual invocation exit must remain nonzero, then real saved/reopened partial state remains nonqualifying |
| G-96 -> PC-96 | standalone publication failure is returned as successful settlement before durable envelope readback | QT.StandaloneProducerBindsProof; `preserved:partial-publication:P:settlement-succeeded=false`; actual invocation exit nonzero is primary; no qualifying line or recipient validator success |
| G-97 -> PC-97 | final pre-host source boundary uses the earlier clean capture | WT.RowRefusesMismatchedOrUnknownProof; `preserved:changed-while-queued:host-launched=false`; hold the last launch barrier after earlier checks, change only owned mini repo, release; later end-source refusal cannot mask the forbidden launch |
| G-98 -> PC-98 | already-ready branch drops conditional metadata before handing row to executor | WT.RowPersistsBoundProbeAndTrx; `preserved:ready-row:qualified=true` fails; real ready queue path, full recipient artifact readback required |
| G-99 -> PC-99 | skip proof capture on second queued row, pass previous row artifact path unchanged | WT.RowPersistsBoundProbeAndTrx; `preserved:second-row:qualified=true` fails at recipient; both rows reached, second is refused by intact nonce guard |
| G-100 -> PC-100 | omit conditional payload when persisting RowProgress/report | WT.ResumeAndReportKeepEvidence; `preserved:reopened-U:qualified=true` fails after independent reopen; do not inspect only live DTO |
| G-101 -> PC-101 | P timeout reducer retains an earlier unavailable observation after timeout | PT.ProbeTimeoutOrCleanupFailureIsUnknown; `preserved:timeout:P:host-launched=false`; actual standalone reducer, intact offline recomputation runs only after this primary assertion |
| G-102 -> PC-102 | P cleanup reducer retains unavailable after cleanup is unconfirmed | PT.ProbeTimeoutOrCleanupFailureIsUnknown; `preserved:cleanup:P:host-launched=false`; only owned cleanup fails, independent script path |

PC-27..32/91..94/101..102 assert the producer admission boundary **before** offline
recomputation, which otherwise masks a defective classifier. The test feeds the
actual classifier result into the real producer admission reducer, observes zero
host launches for invalid/unknown facts, and separately checks final validation.
PC-63/64 and PC-61/62 deliberately observe the actual narrower judgment/admission
function because enclosing guards otherwise mask those cuts. Each has an observer
and an ordinary entry-to-function wiring witness; a test-local imitation is invalid.

The inventory names semantic guard boundaries, including typed strict-schema,
limits, tuple and structural validators. Those are indivisible result checks in
this design, with all their listed field vectors exercised ordinarily. Code must
bind the actual implementation: if independently bypassable checks are introduced
inside such a boundary, add distinct G/PC rows for them before claiming census
completion. Do not use a broad top-level `return true` mutation to cover several
independent guards. All 102 mapped controls remain required; an unreachable or
masked patch returns to TestDesign, not a waived control.

Design census: **guards=102; mapped=102; missing=0; duplicate PC maps=0**.
None excluded as 'too small'. Source-level census, exact patches and payloads are
**binding required after Code**. All PCs run in Mutation; none ran in this docs
stage. Any added controls increase the numeric floor and packet count below.

### Out of scope

- Windows native acceptance is pending, not Linux Code credit. Two distinct real
  Windows capability placements are required; simulated OS/error fixtures prove
  only parsing/classification. No desktop execution in this task.
- Product messaging queues, session input and transcripts are unchanged; no
  synthetic receipt is substituted for their delivery contract.
- Whole Antiphon.Tests, Pty/E2E/DB suites and unrelated policy redesign are outside
  this closed selection. No new process infrastructure or test fixture DB is needed.
- Cryptographic authenticity against an attacker controlling producer binaries,
  source checkout and all evidence is not provided by digest equality. The trusted
  caller root/source and real build provenance remain required.
- Existing source/build/slot/cleanup implementations retain their own regression
  witnesses. This feature tests that its exception remains conjunctive with them;
  it does not remutate every unchanged internal predicate.

#### Classification, source graph and slice order

S1 lands first: schema/import/strict TRX plus ContractTests and fixture's pure
portion. CP-1..3 run at its committed tip. No permission is enabled yet.

S2 next: probe, runtime/state/report, both validator envelopes, Producer/Probe/
Validation/Parity tests and fixture process support. CP-4..8 run at its tip in two
packets. It cannot earn native/current-source consumer credit until the catalog
exists. Keep S2 and S3 serial on the same Code branch and submit their complete
consumer-qualified result together for Review/land; do not advertise S2 fixtures
as full native acceptance. This is the CP-9 ordering refinement, not a new fix.

S3 completes the consumer catalog, HostTests, owner docs, classification and pins;
CP-9..12 run at that committed tip in two packets. The consumer entries are exactly
`c1210-storage-file-links-v1` (total3/floor1, focused filter below) and
`c1210-storage-audit-v1` (total39/floor37, full filter below). Each grants the two
count-1 qualified file-link methods and the literal CARD-1210 reason in the consumer appendix, never
the directory-link or Windows native audit skip. No lower floor is a skip waiver.

New classes declare explicit Category metadata: Contract/Validation Unit;
Probe/Producer/Parity/Host Integration. Every process-spawning class has the
assembly-local process limiter and owned cleanup. Native methods are additionally
`[Explicit]` and Category `OptIn`, with method-specific manual native-policy
exclusions. There is no invented NativeWindowsOnly attribute and no whole-class
exclusion of HostTests: the two portable methods remain discoverable in Linux.
No explicit method can silently skip because its prerequisite is absent.

If `tests/test-execution-policy.json` changes, regenerate its actual `policyHash`
with the repository's current reader and refresh `PinnedInputDigest` in
`scripts/test-live-lane-run.ps1` in the **same S3 commit**. Use the actual
`Get-NightlyPolicyHash -Object <parsed policy>` helper in
`scripts/lib/nightly-policy.ps1` and `pwsh -NoProfile -File
scripts/test-live-lane-run.ps1 -PrintInputDigest`, then preserve CP-10/11's guards.
Do not hardcode a guessed digest or weaken the sentinels. Also update the
classification metadata/catalog where its existing guard requires it. The tool
project retains its exact HostCleanup-only reference; production code must not
reference the test assembly or application layering.

Overlap requiring serial ownership:

| Files/boundaries | Slice overlap |
|---|---|
| Manifest/import models and validator, shared conditional types | S1 defines; S2 consumes and threads values; do not implement concurrently |
| TrxReport/strict projection and new fixture | S1 pure projection; S2 judgment/corpus; S3 real consumer wiring |
| RowRunner, RunScheduler, RunState, report DTO/validation and CheckpointApp | S2 only, then S3 consumer validation; one writer |
| `scripts/run-checkpoint.ps1`, `scripts/validate-checkpoint-receipt.ps1` and shared script helper | S2 producer/validator symmetry; S3 owner documentation only unless wiring exposes a defect |
| New HostTests and catalog | S3 together, dependency on genuine outer build/source |
| Test policy, metadata, policyHash, live-lane pin, testing owner docs | S3 one atomic slice; no parallel lane-policy writer |

### Checkpoints

Schema is [docs/testing-and-build.md](../../testing-and-build.md), Checkpoint
manifest. Each row selects exactly one filter and one isolated build identity;
`CP-n` in Build reuses that packet's isolated build. Group is the committed slice
packet, not permission for an extra selection. All use project
`tests/Antiphon.Tests` (its existing csproj), configuration Debug,
`build.properties.UseAppHost: "false"`, serial rows, TEMP/TMP/TMPDIR=/tmp on Linux.
Use the existing driver arguments unchanged; no unsupported manifest fields.
Expected source is the full committed
slice SHA. `Min` means `-MinExecuted` TUnit executions. No conditional permission
is applied to these ordinary verification rows.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1217-s1/ | c1217-s1 | `/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipContractTests/*` | V-1..4 | green | 5 | 14 | true |
| CP-2 | S1 | CP-1 | c1217-s1 | `/*/Antiphon.Tests.Checkpoints/(CheckpointManifestTests*)\|(CheckpointImportTests*)\|(TrxReportTests*)/*` | R-1 | green | 48 | 2 | true |
| CP-3 | S1 | CP-1 | c1217-s1 | `/*/Antiphon.Tests.Checkpoints/CheckpointRepeatConfigurationTests/*` | R-1 | green | 5 | 2 | true |
| CP-4 | S2 | tests/Antiphon.Tests -> bin-c1217-s2a/ | c1217-s2a | `/*/Antiphon.Tests.Checkpoints/(C1217ConditionalSkipProbeTests*)\|(C1217ConditionalSkipProducerTests*)\|(C1217ConditionalSkipValidationTests*)/*` | V-5..13 | green | 14 | 15 | true |
| CP-5 | S2 | CP-4 | c1217-s2a | `/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipParityTests/*` | V-14/15 | green | 4 | 3 | true |
| CP-6 | S2 | CP-4 | c1217-s2a | `/*/Antiphon.Tests.Checkpoints/(RowRunnerTests*)\|(RunSchedulerTests*)\|(ReportWriterTests*)\|(ReportMergerTests*)/*` | R-2 | green | 39 | 3 | true |
| CP-7 | S2 | tests/Antiphon.Tests -> bin-c1217-s2b/ | c1217-s2b | `/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests/*` | R-3 | green | 11 | 15 | true |
| CP-8 | S2 | CP-7 | c1217-s2b | `/*/Antiphon.Tests.Scripts/(RunCheckpointSourceScriptTests*)\|(RunCheckpointRepeatScriptTests*)/*` | R-3 | green | 10 | 3 | true |
| CP-9 | S3 | tests/Antiphon.Tests -> bin-c1217-s3h/ | c1217-s3h | `/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipHostTests/(PortableHostProducesQualifyingReceipt*)\|(UnavailableFixtureCannotMasqueradeAsNativeProof*)` | V-16 | green | 2 | 15 | true |
| CP-10 | S3 | tests/Antiphon.Tests -> bin-c1217-s3g/ | c1217-s3g | `/*/*/(TestLaneCategoryGuardTests*)\|(TestClassificationGuardTests*)\|(CheckpointToolProjectContractTests*)\|(C1729LayeringGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)/*` | R-4 | green | 31 | 15 | true |
| CP-11 | S3 | CP-10 | c1217-s3g | `/*/Antiphon.Tests.Scripts/(NativeLinuxPolicyExecutionTests*)\|(LiveLaneRunnerTests*)/*` | R-5 | green | 5 | 3 | true |
| CP-12 | S3 | CP-9 | c1217-s3h | `/*/*/*/(C1059_Source_reads_reject_directory_links*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)` | R-6 | green | 2 | 2 | true |
| CP-13 | N1 | tests/Antiphon.Tests -> bin-c1217-n1/ | c1217-n1 | `/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipHostTests/NativeWindowsAvailableReceiptQualifies` | V-17 pending | green | 1 | 16 | true |
| CP-14 | N2 | tests/Antiphon.Tests -> bin-c1217-n2/ | c1217-n2 | `/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipHostTests/NativeWindowsUnavailableReceiptQualifies` | V-17 pending | green | 1 | 16 | true |

CP-1..12 are the whole ordinary Linux Code scope: **176 outer executions**, five
isolated builds, no assembly run. CP-13/14 are separate pending native packets,
not part of that union; exact method selection explicitly admits each `[Explicit]`
method, with observed count1 mandatory. A zero-result filter is a failed checkpoint.
The union of the two portable HostTests and the two native methods is its complete
four-method roster. No class wildcard may accidentally execute a native prerequisite.

#### Frozen execution roster

Existing source roster at the stated base follows. Parenthesized argument lists
are distinct TUnit executions; every unmarked method is one execution. Lifecycle
hooks are excluded. Compiled metadata and fresh TRX must equal this multiset at
Code, with any base drift reconciled explicitly before changing Min. A static
method count is not execution proof. New exact methods are frozen in V-1..17
(Contract5, Validation8, Probe3, Producer3, Parity4, Host2 portable+2 native).

```text

CheckpointManifestTests [7]
  parses_yaml_and_defaults
  rejects_backslash_or_trailing_space_output_path
  rejects_duplicate_or_unknown_build_ids
  rejects_build_ids_that_are_not_isolated_output_directories
  rejects_reuse_across_different_after
  after_selector_parses_ranges_and_all
  results_root_must_be_under_worktree

CheckpointImportTests [34]
  imports_the_card_0688_table
  unescapes_pipes_in_filters
  maps_cp_reuse_to_the_same_build
  command_rows_become_command_checkpoints
  derives_roster_tokens_from_the_filter
  refuses_the_legacy_eight_column_table
  warns_when_estimate_exceeds_advisory_threshold
  imports_the_card_0723_table
  serial_import_round_trips_and_excludes_other_rows
  environment_map_round_trips_without_changing_serial
  environment_reaches_only_its_row_and_its_reruns
  scheduler_passes_test_parallel_limit_only_to_serial_rows_process
  invalid_serial_is_refused_before_execution
  unknown_duplicate_and_malformed_columns_are_refused
  invalid_environment_names_and_duplicates_are_refused
  annotated_reuse_resolves_only_an_earlier_filter_build
  filter_shorthand_and_trailing_environment_prose_are_refused
  cross_after_reuse_is_refused_without_a_relaxation_flag
  conflicting_projects_cannot_share_an_output_identity
  run_plan_and_import_resolve_the_committed_table_identically
  C1098_card_1087_wrapper_census_row_is_split_under_the_same_cap
  C1493_DuplicateBuild_second_equivalent_build_warns_once
  C1493_DuplicateBuild_project_spellings_normalise
  C1493_distinct_after_repeat_or_project_do_not_warn
  C1493_warning_keeps_import_exit_and_output
  C1493_DuplicateBuild_recommendation_imports_for_non_numeric_row_id
  explicit_total_below_derived_is_refused_without_an_override_reason
  explicit_total_below_derived_warns_when_an_override_reason_is_given
  explicit_total_at_or_above_derived_is_kept_without_a_warning
  prose_mention_of_total_timeout_is_not_a_budget_directive
  explicit_total_directive_must_be_a_positive_integer
  cli_total_below_derived_is_refused_without_an_override_reason
  cli_total_below_derived_is_allowed_when_an_override_reason_is_given
  yaml_explicit_total_below_derived_is_refused_without_a_reason

TrxReportTests [7]
  joins_results_to_class_qualified_names
  counts_failed_error_timeout_aborted
  skips_not_executed
  falls_back_when_counters_missing
  reads_message_stack_and_stdout
  aggregates_seconds_per_class
  malformed_trx_is_exit_2

CheckpointRepeatConfigurationTests [5]
  repeat_round_trips_from_cli_yaml_and_table
  invalid_repeat_refuses_before_slot_or_driver
  repeat_properties_bind_build_and_run_only_to_selected_project
  reuse_requires_matching_repeat_build
  default_one_preserves_legacy_arguments_and_counts

RowRunnerTests [13]
  green_row_is_exit_0
  red_row_is_exit_1
  zero_executed_is_exit_3
  roster_miss_is_exit_3
  skipped_class_still_meets_the_roster
  build_failed_is_exit_2
  no_trx_is_exit_2
  malformed_trx_is_exit_2
  no_build_reports_reused
  properties_are_forwarded_to_build_and_run
  linux_defaults_use_app_host_false
  windows_leaves_use_app_host_unset
  explicit_use_app_host_wins

RunSchedulerTests [19]
  rows_wait_for_the_build_source_receipt_before_readiness
  builds_never_overlap_even_with_free_slots_and_width_two
  rows_of_a_finished_build_run_while_the_next_build_is_in_flight
  serial_runs_one_activity_at_a_time_builds_included
  an_exclusive_row_keeps_the_build_lane_idle_until_it_finishes
  an_exclusive_row_waits_for_the_in_flight_build_of_another_row
  builds_start_in_the_order_rows_first_need_them
  unreferenced_builds_end_unused_and_state_records_one_concurrent_build
  builds_each_output_once
  rows_wait_for_their_build
  width_two_runs_two_rows_at_once
  pty_project_rows_run_alone
  serial_rows_run_alone
  failed_build_fails_only_its_rows
  concurrent_rows_hold_distinct_leases_and_their_lines_say_granted
  records_output_directories_the_build_created
  build_deadline_is_the_largest_dependent_row_timeout_and_the_row_override_replaces_it
  scheduler_passes_the_derived_build_deadline_and_records_it
  build_timer_uses_selected_row_deadlines_and_override

ReportWriterTests [6]
  summary_line_and_json_carry_max_concurrent_builds
  block_layout_matches_d10
  unlisted_is_none
  tool_runs_are_not_reported_as_unlisted_none
  empty_outputs_say_none
  json_carries_every_row_field

ReportMergerTests [1]
  latest_per_cp_wins_and_earlier_attempts_count_as_reruns

CheckpointSourceExecutionTests [11]
  host_written_entries_under_the_row_output_are_adopted_and_cleaned_on_green
  clean_and_dirty_runs_publish_bound_source
  changed_admission_and_driver_boundaries_refuse
  strict_cli_and_reuse_require_clean_binding
  terminal_paths_do_not_invent_clean_evidence
  merge_cannot_launder_source_identity
  validation_requires_complete_consistent_source
  command_only_selection_beside_built_row_receipt_passes_both_validators
  cleanup_refusals_disqualify_green_rows_in_both_validators
  red_run_kept_outputs_are_not_recorded_as_cleanup_refusals
  red_run_still_refuses_output_it_did_not_create

RunCheckpointSourceScriptTests [6]
  C1035_ChildCwdMatchesCertifiedRoot
  C835_DiagnosticReceipts
  C835_StrictAdmission
  C835_DriftAndReuse
  C835_TerminalEvidence
  C835_ReceiptValidation

RunCheckpointRepeatScriptTests [4]
  C885_RepeatBuildAndSingleRun
  C885_RepeatReceiptAndTimings
  C885_RepeatFailurePartialAndInvalidInput
  C885_RepeatNoBuildAndLeaseCustody

TestLaneCategoryGuardTests [1]
  every_test_class_is_tagged_unit_xor_integration

TestClassificationGuardTests [1]
  Registry_matches_compiled_metadata

CheckpointToolProjectContractTests [4]
  csproj_packs_as_tool_named_antiphon_checkpoints
  csproj_references_only_host_cleanup
  solution_lists_the_tool
  tests_project_references_the_tool

C1729LayeringGuardTests [9]
  Map_covers_every_server_file_exactly_once
  Layer_of_pinned_files_follows_first_match
  Allowlist_parses_with_grouped_rows_and_a_mode_header
  Repository_graph_satisfies_the_committed_allowlist
  Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode
  Allowed_edges_listed_rows_comments_and_strings_do_not_fire
  Stale_row_and_unmapped_file_fail_in_strict_mode
  Ambiguous_simple_names_resolve_by_using_or_are_listed
  Warn_mode_prints_the_inventory_and_fails_only_on_map_or_parse_errors

MechanicalPcContractTests [11]
  C479_V05_MutationBundleFailsClosedAtLow
  C479_V05b_CodePreparesUnboundCandidates
  C479_V05c_ReviewJudgesAdequacyWithoutMutants
  C479_V06_OwnerDocHoldsTheContract
  C479_V06b_WorkedExamplesAreIllustrative
  C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow ("docs/orchestration-loop.md"; ".claude/skills/antiphon-delegate/SKILL.md"; "server/Bundles/orchestrator.md"; "server/Bundles/README.md"; "server/Bundles/delegate-basics.md")
  C479_R06_LowNeverDiscoversAndTierRowsStayLow

TaskPlatformGuidanceTests [5]
  Stage_bundles_leave_twenty_characters_below_the_size_cap
  Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes
  Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning
  Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes
  Review_guidance_preserves_every_instruction

NativeLinuxPolicyExecutionTests [1]
  Native_child_observes_platform_outcomes_and_side_effects

LiveLaneRunnerTests [4]
  LockBeforeSlot
  WrapperOnlyPaidManifest
  ChildEnvironmentIsScoped
  DeadlineJoinsOwnedChildren

ChannelOutboundStorageTests [33]
  C1059_Source_reads_require_captured_roots_without_traversal
  C1059_Source_reads_reject_directory_links
  C1059_Source_reads_reject_file_links
  C1059_Source_length_is_checked_before_reading_with_a_finite_budget
  C1059_Source_reads_refuse_nonregular_files_without_blocking
  Failed_staging_never_exposes_a_partial_snapshot ("partial_write"; "complete_write"; "rename_refusal"; "disk_full"; "access_denied")
  Frozen_reply_and_input_bytes_survive_source_mutation
  Oversized_reply_never_exposes_snapshot_directory
  Exactly_fourteen_mebibytes_of_original_bytes_can_be_staged
  Serialized_limit_counts_unicode_metadata_and_base64_at_exact_boundary
  Near_default_wire_cap_accepts_exact_serialized_size_and_refuses_one_more_byte
  Converted_files_are_sealed_with_original_sources_and_frozen_routing
  Manifested_zip_sources_are_expanded_from_attached_bytes
  Source_zip_expansion_accepts_exact_budget_and_refuses_one_extra_byte
  Inline_and_zip_sources_share_the_expanded_budget
  Attachment_source_url_does_not_supply_worker_input
  Source_zip_manifest_rejects_unsafe_or_ambiguous_members ("parent_path"; "absolute_path"; "backslash_path"; "stored_file_traversal"; "case_collision"; "zip_entry_differs"; "forged_length"; "forged_hash"; "missing_entry")
  Worker_created_sealed_reply_cannot_bypass_manifest_validation_on_recovery
  Output_manifest_cannot_override_frozen_routing
  Output_rename_and_link_swap_after_inspection_seals_the_opened_file
  Complete_source_stamp_requires_every_manifested_attachment

WindowsUnitPlatformAdmissionTests [6]
  C1210_Guard_matches_native_platform
  C1210_Native_linux_entries_guard_before_work
  C1210_Roster_closure_matches_source_gates
  C1210_Audit_rejects_empty_or_incomplete_outcomes
  C1210_Host_roster_matches_windows_shaped_observation
  C1210_Windows_linux_only_outcomes_are_exact
```

CP-2=7+34+7=48; CP-3=5; CP-6=13+19+6+1=39; CP-7=11;
CP-8=6+4=10; CP-10=1+1+4+9+11+5=31; CP-11=1+4=5; CP-12=2.
CP-20=33 storage results (21 methods, including5+9 argument instances) +6 audit
methods=39. Hooks and the four RunnerDefaultGuidanceTests are not selected.

#### BOUNDARY WIRING: consumer acceptance, pending full row

The source catalog freezes these exact strings (same project spelling as the
consumer row); do not translate wildcard shape or trim the reason:

```text
project: tests/Antiphon.Tests
focused filter: /*/Antiphon.Tests.Application/ChannelOutboundStorageTests/(C1059_Source_reads_require_captured_roots_without_traversal*)|(C1059_Source_reads_reject_file_links*)|(Output_rename_and_link_swap_after_inspection_seals_the_opened_file*)
full filter: /*/*/(ChannelOutboundStorageTests*)|(WindowsUnitPlatformAdmissionTests*)/*
className: Antiphon.Tests.Application.ChannelOutboundStorageTests
skip method 1: C1059_Source_reads_reject_file_links
skip method 2: Output_rename_and_link_swap_after_inspection_seals_the_opened_file
count: 1 each
reason: CARD-1210: file-link controls require symbolic-link creation privilege on Windows; capability unavailable.
```

The focused row is the real child inside W-HOST/W-NATIVE. Its catalog floor1 is
fixed in both states: available must still execute all3; unavailable executes1,
skips exactly2. This is not permission to accept an arbitrary one-test receipt.
The full CP-20 receipt is a separate CARD-1210 follow-up with the closed 39-result
roster above. The following table is its manifest input, **not** an additional
CARD-1217 Code row. It uses the S3 importer only after the feature lands. Budget
one isolated build and one exact filter per Windows placement, run twice total
(one available, one real1314), not Repeat=2 on one placement.

```markdown
### Checkpoints
| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | ConditionalSkips |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-20 | S3 | tests/Antiphon.Tests -> bin-c1217-cp20/ | windows-storage-audit | /*/*/(ChannelOutboundStorageTests*)\|(WindowsUnitPlatformAdmissionTests*)/* | CARD-1210 native follow-up | available 39/39/0/0 or unavailable 37/37/0/2, exact39 raw results | 37 | 16 | true | c1210-storage-audit-v1 |
```

Full CP-20 requires `UseAppHost=true` on **Windows** for the existing audit child's
Antiphon.Tests.exe. The ordinary focused child and portable packets use
UseAppHost=false as above. The current native audit body must run and cannot be
credited from its Linux platform skip. Apply the same 28-minute build, 6-minute
row, 10-minute admission, 36-minute execution/56-minute hard-wall ceilings as N1/N2.
Two valid raw envelopes plus both validator exits are the receipt, never just a
CHECKPOINT line. No historic 19/19/0/6 or 36/36/0/2 receipt is retroactively valid.

#### BOUNDARY WIRING: budgeted manifest packets

Import this document with the **existing** importer first:
`import --plan docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md --out
.antiphon/checkpoints/c1217-import.yaml`. Compare each selected tuple
`(id, after, build/project/output, group, filter, expect roster tokens, minExecuted,
estimatedMinutes, serial, repeat)` to the corresponding complete packet below.
The only refinements are explicit budgets, common build properties/environment,
removal of unselected rows/builds and the plan provenance. No membership editing.
Use the source-bound existing bootstrap binary if available; an absent tool needs
one separately reported slot-gated native bootstrap, not an implicit `dotnet run`
build. Unknown YAML fields would be ignored by today's loader, so use exactly the
supported fields below and read the saved resolved manifest before running.

Run each packet with the supported positional form:
`run <packet.yaml> --rows <its CP IDs> --expected-source-sha <committed source SHA>`.
Use the checkpoint tool through its documented runner entry, which owns build/row
slots; do not place a second outer lease around it. Run/wait in the foreground,
retain its run ID and call `wait` until exit is not75; final state/report and all
rows must exist. Do not use `run --plan` for execution here: that would rederive
noncompliant per-row deadlines. Do not use CLI `--row-timeout`, which also changes
the build deadline. No edits while a packet is running. Each run contains at most
three rows. S2 and S3 each require two runs, never `--after S2` or `--after S3` as
one execution. Generated manifests/receipts stay ignored.

The importer may warn about same-project builds in the same After group. Here
separate build identities intentionally isolate <=3-row packets and committed
source receipts. Do not collapse them into a six-row run to silence an advisory.
`explicitTotalMinutes` is the current YAML spelling for the charged cap; the
human Plan directive is `totalTimeoutMinutes`. `totalOverrideReason` explains
why the below-derived cap is based on phases. `timeouts.totalMinutes` is not used.

Packet s1; `.antiphon/checkpoints/c1217-s1.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 28
  explicitTotalMinutes: 42
  totalOverrideReason: "CARD-1217 phase budget: one 28-minute build, explicit row caps, 2-minute settlement; admission charged separately"
  hardWallMinutes: 82
builds:
  - id: bin-c1217-s1
    project: tests/Antiphon.Tests
    outputPath: bin-c1217-s1/
checkpoints:
  - id: CP-1
    after: [S1]
    build: bin-c1217-s1
    group: c1217-s1
    filter: "/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipContractTests/*"
    expect: ["C1217ConditionalSkipContractTests"]
    minExecuted: 5
    estimatedMinutes: 14
    timeoutMinutes: 4
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
  - id: CP-2
    after: [S1]
    build: bin-c1217-s1
    group: c1217-s1
    filter: "/*/Antiphon.Tests.Checkpoints/(CheckpointManifestTests*)|(CheckpointImportTests*)|(TrxReportTests*)/*"
    expect: ["CheckpointManifestTests","CheckpointImportTests","TrxReportTests"]
    minExecuted: 48
    estimatedMinutes: 2
    timeoutMinutes: 4
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
  - id: CP-3
    after: [S1]
    build: bin-c1217-s1
    group: c1217-s1
    filter: "/*/Antiphon.Tests.Checkpoints/CheckpointRepeatConfigurationTests/*"
    expect: ["CheckpointRepeatConfigurationTests"]
    minExecuted: 5
    estimatedMinutes: 2
    timeoutMinutes: 4
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
```

Packet s2a; `.antiphon/checkpoints/c1217-s2a.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 28
  explicitTotalMinutes: 46
  totalOverrideReason: "CARD-1217 phase budget: one 28-minute build, explicit row caps, 2-minute settlement; admission charged separately"
  hardWallMinutes: 86
builds:
  - id: bin-c1217-s2a
    project: tests/Antiphon.Tests
    outputPath: bin-c1217-s2a/
checkpoints:
  - id: CP-4
    after: [S2]
    build: bin-c1217-s2a
    group: c1217-s2a
    filter: "/*/Antiphon.Tests.Checkpoints/(C1217ConditionalSkipProbeTests*)|(C1217ConditionalSkipProducerTests*)|(C1217ConditionalSkipValidationTests*)/*"
    expect: ["C1217ConditionalSkipProbeTests","C1217ConditionalSkipProducerTests","C1217ConditionalSkipValidationTests"]
    minExecuted: 14
    estimatedMinutes: 15
    timeoutMinutes: 6
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
  - id: CP-5
    after: [S2]
    build: bin-c1217-s2a
    group: c1217-s2a
    filter: "/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipParityTests/*"
    expect: ["C1217ConditionalSkipParityTests"]
    minExecuted: 4
    estimatedMinutes: 3
    timeoutMinutes: 6
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
  - id: CP-6
    after: [S2]
    build: bin-c1217-s2a
    group: c1217-s2a
    filter: "/*/Antiphon.Tests.Checkpoints/(RowRunnerTests*)|(RunSchedulerTests*)|(ReportWriterTests*)|(ReportMergerTests*)/*"
    expect: ["RowRunnerTests","RunSchedulerTests","ReportWriterTests","ReportMergerTests"]
    minExecuted: 39
    estimatedMinutes: 3
    timeoutMinutes: 4
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
```

Packet s2b; `.antiphon/checkpoints/c1217-s2b.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 28
  explicitTotalMinutes: 42
  totalOverrideReason: "CARD-1217 phase budget: one 28-minute build, explicit row caps, 2-minute settlement; admission charged separately"
  hardWallMinutes: 72
builds:
  - id: bin-c1217-s2b
    project: tests/Antiphon.Tests
    outputPath: bin-c1217-s2b/
checkpoints:
  - id: CP-7
    after: [S2]
    build: bin-c1217-s2b
    group: c1217-s2b
    filter: "/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests/*"
    expect: ["CheckpointSourceExecutionTests"]
    minExecuted: 11
    estimatedMinutes: 15
    timeoutMinutes: 6
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
  - id: CP-8
    after: [S2]
    build: bin-c1217-s2b
    group: c1217-s2b
    filter: "/*/Antiphon.Tests.Scripts/(RunCheckpointSourceScriptTests*)|(RunCheckpointRepeatScriptTests*)/*"
    expect: ["RunCheckpointSourceScriptTests","RunCheckpointRepeatScriptTests"]
    minExecuted: 10
    estimatedMinutes: 3
    timeoutMinutes: 6
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
```

Packet s3h; `.antiphon/checkpoints/c1217-s3h.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 28
  explicitTotalMinutes: 40
  totalOverrideReason: "CARD-1217 phase budget: one 28-minute build, explicit row caps, 2-minute settlement; admission charged separately"
  hardWallMinutes: 70
builds:
  - id: bin-c1217-s3h
    project: tests/Antiphon.Tests
    outputPath: bin-c1217-s3h/
checkpoints:
  - id: CP-9
    after: [S3]
    build: bin-c1217-s3h
    group: c1217-s3h
    filter: "/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipHostTests/(PortableHostProducesQualifyingReceipt*)|(UnavailableFixtureCannotMasqueradeAsNativeProof*)"
    expect: ["PortableHostProducesQualifyingReceipt","UnavailableFixtureCannotMasqueradeAsNativeProof"]
    minExecuted: 2
    estimatedMinutes: 15
    timeoutMinutes: 6
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
  - id: CP-12
    after: [S3]
    build: bin-c1217-s3h
    group: c1217-s3h
    filter: "/*/*/*/(C1059_Source_reads_reject_directory_links*)|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)"
    expect: ["C1059_Source_reads_reject_directory_links","C1210_Audit_rejects_empty_or_incomplete_outcomes"]
    minExecuted: 2
    estimatedMinutes: 2
    timeoutMinutes: 4
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
```

Packet s3g; `.antiphon/checkpoints/c1217-s3g.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 28
  explicitTotalMinutes: 42
  totalOverrideReason: "CARD-1217 phase budget: one 28-minute build, explicit row caps, 2-minute settlement; admission charged separately"
  hardWallMinutes: 72
builds:
  - id: bin-c1217-s3g
    project: tests/Antiphon.Tests
    outputPath: bin-c1217-s3g/
checkpoints:
  - id: CP-10
    after: [S3]
    build: bin-c1217-s3g
    group: c1217-s3g
    filter: "/*/*/(TestLaneCategoryGuardTests*)|(TestClassificationGuardTests*)|(CheckpointToolProjectContractTests*)|(C1729LayeringGuardTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)/*"
    expect: ["TestLaneCategoryGuardTests","TestClassificationGuardTests","CheckpointToolProjectContractTests","C1729LayeringGuardTests","MechanicalPcContractTests","TaskPlatformGuidanceTests"]
    minExecuted: 31
    estimatedMinutes: 15
    timeoutMinutes: 6
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
  - id: CP-11
    after: [S3]
    build: bin-c1217-s3g
    group: c1217-s3g
    filter: "/*/Antiphon.Tests.Scripts/(NativeLinuxPolicyExecutionTests*)|(LiveLaneRunnerTests*)/*"
    expect: ["NativeLinuxPolicyExecutionTests","LiveLaneRunnerTests"]
    minExecuted: 5
    estimatedMinutes: 3
    timeoutMinutes: 6
    serial: true
    environment: { TEMP: /tmp, TMP: /tmp, TMPDIR: /tmp }
```

Packet n1; `.antiphon/checkpoints/c1217-n1.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 28
  explicitTotalMinutes: 36
  totalOverrideReason: "CARD-1217 phase budget: one 28-minute build, explicit row caps, 2-minute settlement; admission charged separately"
  hardWallMinutes: 56
builds:
  - id: bin-c1217-n1
    project: tests/Antiphon.Tests
    outputPath: bin-c1217-n1/
checkpoints:
  - id: CP-13
    after: [N1]
    build: bin-c1217-n1
    group: c1217-n1
    filter: "/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipHostTests/NativeWindowsAvailableReceiptQualifies"
    expect: ["C1217ConditionalSkipHostTests"]
    minExecuted: 1
    estimatedMinutes: 16
    timeoutMinutes: 6
    serial: true
```

Packet n2; `.antiphon/checkpoints/c1217-n2.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 28
  explicitTotalMinutes: 36
  totalOverrideReason: "CARD-1217 phase budget: one 28-minute build, explicit row caps, 2-minute settlement; admission charged separately"
  hardWallMinutes: 56
builds:
  - id: bin-c1217-n2
    project: tests/Antiphon.Tests
    outputPath: bin-c1217-n2/
checkpoints:
  - id: CP-14
    after: [N2]
    build: bin-c1217-n2
    group: c1217-n2
    filter: "/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipHostTests/NativeWindowsUnavailableReceiptQualifies"
    expect: ["C1217ConditionalSkipHostTests"]
    minExecuted: 1
    estimatedMinutes: 16
    timeoutMinutes: 6
    serial: true
```

#### Dependency and fixture input closure

Direct project references read at the base (not a claim about transitive MSBuild
imports). Actual evaluated graph/source fingerprint still comes from each build.
No new project/reference is needed; reuse the owned staged process helpers.

```text
tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj [1 direct references]
  -> src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj
tests/Antiphon.Tests/Antiphon.Tests.csproj [27 direct references]
  -> server/Antiphon.Server.csproj
  -> scripts/fixtures/nightly/owned-child/OwnedChild.csproj
  -> src/Antiphon.Resilience/Antiphon.Resilience.csproj
  -> src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj
  -> tests/Antiphon.FileUseTestChild/Antiphon.FileUseTestChild.csproj
  -> tests/Antiphon.DockerStack.Fixture/Antiphon.DockerStack.Fixture.csproj
  -> tests/Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj
  -> tests/Antiphon.ScriptHarnessHost/Antiphon.ScriptHarnessHost.csproj
  -> tests/Antiphon.ChannelOutbound.Probe/Antiphon.ChannelOutbound.Probe.csproj
  -> src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj
  -> src/Antiphon.FakeLlmApi/Antiphon.FakeLlmApi.csproj
  -> src/Antiphon.NightlyWatchdog/Antiphon.NightlyWatchdog.csproj
  -> tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj
  -> tools/Antiphon.TimeoutAudit/Antiphon.TimeoutAudit.csproj
  -> tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj
  -> tests/Antiphon.Checkpoints.FilterHost/Antiphon.Checkpoints.FilterHost.csproj
  -> tests/Antiphon.Checkpoints.RepeatHost/Antiphon.Checkpoints.RepeatHost.csproj
  -> tests/Antiphon.Checkpoints.GitFixture/Antiphon.Checkpoints.GitFixture.csproj
  -> tests/Antiphon.TestTiming.Probe/Antiphon.TestTiming.Probe.csproj
  -> tests/Antiphon.NativeLinuxPolicy.Probe/Antiphon.NativeLinuxPolicy.Probe.csproj
  -> tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj
  -> tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj
  -> src/Antiphon.Messaging.Client.Testing/Antiphon.Messaging.Client.Testing.csproj
  -> src/Antiphon.Messaging.Gateway/Antiphon.Messaging.Gateway.csproj
  -> src/Antiphon.Messaging.Slack/Antiphon.Messaging.Slack.csproj
  -> src/Antiphon.FakeClaude/Antiphon.FakeClaude.csproj
  -> src/Antiphon.FakeGrok/Antiphon.FakeGrok.csproj
```

Fixture closure: six new classes and C1217ConditionalSkipFixture; existing
CheckpointTestScope/base, CheckpointFixtureProcess, ScriptHarness, staged actual
Antiphon.Tests/tool image and its real build-source stamp; two-commit miniature
Git catalog; literal independent strict TRX/probe/envelope corpus; exact storage
consumer methods and FileLink classifier; caller-selected expected SHA/root.
The probe capability itself is a fresh per-row input and is never cached across
rows. No paid provider, database, broker or production session runner is an input.

#### Performance manifest and execution gates

This is a **scoped** verification profile, never a Full/rc lane receipt. The
CARD-1737 format's measured comparator is Debug `5ae98d31`, source record
`c318d8edfc97e046bb9a332ec34c76973f9a4f4a`. Its 11 successful cold Antiphon.Tests
builds had p50=431.6s and empirical p95/max=817.1s; 39 admissions averaged46.5s,
p95=255s, max405s. Short host maxima were42-85s, loaded startup reached67s and
one guard selection reached181s. These are historical comparator measurements,
not CARD-1217 p95. New methods/native placements have **unknown measured p95**;
provisional budgets are tracked by CARD-1217/CARD-1404 and must be replaced by
actual phase evidence from the first required executions, not timing-only repeats.

| Phase | Expected | Ceiling/basis | Sharing and evidence |
|---|---|---|---|
| slot admission | 0-5min; comparator mean0.775min | 10min/acquisition, rounded >2x255s | log every broker admission separately; exit4 means not run |
| isolated Antiphon.Tests build | estimate12min; historical4-14min | 28min, rounded >2x817.1s | once per packet, native owned output; record restore/build/staging and host load |
| new pure method | <0.5s; corpus<2s | 5s provisional | immutable literals shared; no process/DB per vector |
| Git catalog read | <1s | 5s and64KiB | one owned two-commit fixture per authority method, private evidence per vector |
| probe operation / joined cleanup | usually<1s plus startup | 5s +5s provisional | new owned directory/nonce each row is intentional; token/cwd/temp same as host |
| parity method | 5-30s | 60s provisional | one owned pwsh batch, bounded CLI exit smokes; no process per pure vector |
| actual host method | 10-60s | 120s provisional | existing compiled/staged image, no nested build; record child startup/body/drain |
| parent host startup/teardown | normally10-25s/5-15s | included in row caps; owned child exit/drain initially10s each | no shared mutable vectors; retained evidence before cleanup |
| short row CP-1/2/3/6/12 | 0.5-2min | 4min | literal roster and startup/body/teardown spans |
| other ordinary/native row | 1-3min | 6min | source scans/Git/pwsh/actual children separately timed |
| container/readiness, DB seed/migration/clone, AppHost | not required | no setup authorized for new witnesses | unexpected activity is investigated; absent measurements remain unknown |

Packet arithmetic (charged caps include2min settlement):

| Packet | Rows | Expected table time | Charged cap | Hard wall | Maximum outer admissions |
|---|---|---:|---:|---:|---:|
| S1 | 1,2,3 | 18 | 28+4+4+4+2=42 | 82 | 4 |
| S2a | 4,5,6 | 21 | 28+6+6+4+2=46 | 86 | 4 |
| S2b | 7,8 | 18 | 28+6+6+2=42 | 72 | 3 |
| S3h | 9,12 | 17 | 28+6+4+2=40 | 70 | 3 |
| S3g | 10,11 | 18 | 28+6+6+2=42 | 72 | 3 |
| N1 / N2 | 13 /14 | 16 each | 28+6+2=36 each | 56 each | 2 each |

S3h and native witness inner standalone no-build acquisition is separately
recorded and remains inside its parent row's6min deadline; it does not extend the
charged/wall cap. Contention can therefore refuse that native witness. It cannot
be retried with -NoSlot or an invented source stamp. The outer parent holds the
assembly process limit; child selection cannot recurse. Do not co-schedule
Antiphon.Agents.Pty.Tests/FakeClaude. One build at a time/worktree.

Record source SHA, packet/run/row, exact Class.Method/arguments, raw counts,
producer image hash, catalog/probe/TRX hashes, OS/load, build properties/output
and source stamp. Keep admission-only union, build, discovery/startup, fixture
preparation, case body, teardown, Git/pwsh/filesystem spans and unattributed
remainder separate; do not sum nested spans as wall time. Use current CARD-1514
phase receipts. Expected nil phases are n/a, missing observations are unknown.
Persist unedited CHECKPOINT and CHECKPOINT-TIMING lines, raw TRX, probe, envelopes,
resolved manifest and validator exits in the ignored checkpoint root.

Under [test-performance-standards.md](../../test-performance-standards.md), any
expected or actual >30min selection/batch/run needs a bounded performance Debug
with exact membership, source and phase timings. Loaded packet ceilings are not
a waiver. Stop repeating an overlong selection; hand its evidence to Debug. Each
PC's conservative baseline/red/restore cycle below exceeds30min, so Mutation's
execution commission must include that bounded performance review before the
large control inventory runs. A group of many method-scoped controls is not
exempt because each test body is small. An approved bound pack may reduce builds
only after its provenance/restoration behavior is demonstrated and repriced.

#### Mutation binding and restoration contract

Classification now: every PC is **higher-tier-required / binding-missing**, with
unbound candidate payloads; no Low mechanical eligibility is claimed. After Code,
Frontier binding evaluates all 102 separately under the mechanical-PC contract.
The deterministic parser/judge cases are candidates for mechanically-eligible
execution only after exact patch/method/oracle/reachability/provenance are bound.
Producer/queue/publication/native cases retain higher-tier-required if backend
reachability, actual process ownership or native prerequisites remain unresolved.
Category Unit/Integration above is separate from mutation execution eligibility.

For each PC the bound record contains recipe and variant ID, base/landed SHA,
exact relative source path and symbol/line, before/after bytes and SHA-256 patch,
selector, complete literal input case, source/tree/catalog/probe/TRX identity,
exact F filter (one method, Min1), expected assertion key, an observer proving the
mutated site was consumed, and classification/reason. All other evidence joins
must pass before the nominated boundary. Test-local helpers never substitute for
the actual producer/validator function. A compiler error, zero tests, timeout,
wrong assertion or earlier fixture failure is **not red**.

Run baseline with selector unset; apply the compiling env-gated defect; rebuild
through a new host build-slot lease; run just F with its PC selector; confirm the
exact preservation assertion red. Restore source byte-for-byte, unset the selector,
rebuild under a new lease and run the same F green. Keep per-PC baseline/red/green
receipts, exit/count/assertion records and patch/restoration hashes. Never mutate
tests to expect success and never change source during an in-flight run. Dynamic
selector helpers are mutation artifacts only, not landed production switches.
Ordinary Code does V/R and prepares candidates; Review judges adequacy/ordinary
receipts; post-land Mutation owns all controls. SourceLanding snapshots never
commit/push: records and restored-state evidence go to their assigned external
root. No generated evidence is added to Git. An unbound payload stays binding
required after Code; no 'tests pass' promotion of a candidate pack.

### Cost

All CARD-1217 numbers below are **estimates**, not measured executions. This
TestDesign used 0 builds / 0 tests / 0 PC runs and created no build/result outputs.
Authoring time is separate from the execution floors; use approximately6-10h for
S1/S2/S3 implementation as the Plan did, revising from actual work.

- **Ordinary Code V/R floor =92min**, the sum of CP-1..12 EstimatedMinutes:
  S1=18, S2a=21, S2b=18, S3h=17, S3g=18. This is60min for five isolated builds
  +32min for the exact row filters above. Do not add those builds a second time.
  Optional absent-tool bootstrap=3min, separately gated/native, so setup+V/R=95min.
  A Review with the same ordinary profile is another92min, not hidden in Code.
- Pending native CP-13/14=32min, split two16min packets (24build+8host). Separate
  CARD-1210 full CP-20 follow-up=32min for the two real states. Neither is Linux
  Code credit and neither runs under this docs commission.
- **Mutation floor =4,029min** for 102 controls, each one exact F method:
  baseline build12+host1; mutant build12+red host1; restored build12+green host1;
  patch/check/restore bookkeeping0.5. Thus39.5min/control,306 builds (3,672min),
  306 method phase executions (306min),51min accounting. Original Plan 34 variants
  cost1,343min; the 68 additional guard controls add2,686min. This deliberately
  assumes no unapproved shared baseline or frozen-binary rebuild exemption.
  Every PC table's exact method is its charged filter, with Min1 in each phase.
- **Combined setup + Code V/R + Mutation =4,124min (68h44)** before authoring,
  Review, contention and native scheduling. With one same-scope Review=4,216min;
  with native N1/N2 also=4,248min; with the separate full consumer follow-up
  also=4,280min. These are staged totals, never one long runner task or a90min
  promise. A failure/repair is separately reported and targets only affected rows.
- Admission is outside those charged floors:17 outer Code acquisitions plus at
  least1 real standalone child acquisition; comparator mean0.775min suggests
  13.95min, not a guarantee. Bootstrap adds1 acquisition. The conservative PC
  model has612 build/test leases, approximately474.3min at that comparator mean;
  any fixture's additional real leases must be separately counted. Use the10min
  hard acquisition bound, not a relaxed timeout, when queues are loaded. Windows
  admission is unmeasured and no scheduling-delay estimate is claimed.

Savings: reusing one build within each ordinary packet uses5 instead of12 cold
builds, saving **7x12=84min** against per-row builds (176min ->92min). Moving CP-9
past S3 costs one additional cold build versus the Plan (80min ->92min), justified
by actual committed catalog/build provenance. Pure vectors reuse literal inputs,
mini repo and pwsh batch; unknown process-per-vector savings are not fabricated.
There is **zero assumed PC build saving** until a bound execution contract proves
it. At current pricing the 102-control cycle requires a dedicated bounded
performance/binding workstream; it must not be assigned as a single90min Mutation.

### Handoff completeness

Read-only document checks: a static table/manifest census verified14 checkpoint
rows, seven <=3-row packets, matching exact filters and budget arithmetic,
176 portable executions/92 estimated minutes, and 102 unique G-to-PC mappings.
This is not an importer build, compiled discovery, test or mutation receipt.

Design guard census: 102 / mapped: 102 / missing: 0 / duplicate maps: 0; the original 34 Plan
variants are all present. All 102 are mandatory later runs, currently unexecuted.
Exact source bindings/patches/compiled rosters and native evidence remain explicitly
pending after Code. No unverifiable seam is being claimed as proof: direct judge
substitutes and their real-producer wiring obligations are stated above. S1 can
start now without Windows access or a policy decision. S2/S3 finish serially,
then ordinary Review; native acceptance and post-land Mutation retain their gates.

Next: **code**, first slice **S1 declaration/import/strict-TRX plus ContractTests**,
run only CP-1..3 with packet c1217-s1 and land S1 before S2/S3. Preserve original
Code/landing owner `36bb3715-4e5e-4038-9a81-5ec024e60a99`. Do not rewrite the fix
Plan to erase prior estimates, roster corrections or unexecuted obligations.
