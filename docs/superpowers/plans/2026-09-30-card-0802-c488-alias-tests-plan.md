# CARD-0802: keep the C488 claims without repeating the real verifier

Plan task: `6bdbf52e-007f-4a03-99c9-ef5ad39df238`. Authoring baseline:
`83ccea1a3483de5357ac7d6b9979c149bc910a55`. This is a test-only change.
TestDesign task: `f18c8069-92c8-4eed-a5c2-0cfa0315a8ea`, inspected at plan commit
`8e487dd7f5f3c738b906124feb04e7d179d15177`. Next stage is Code, then ordinary
Review, land, and post-land SourceLanding Mutation.

## Ground truth

Read CARD-0802 in full with `pwsh -NoProfile -File scripts/card.ps1 get CARD-0802`.
The card is `7b13e3fe-fbbd-49f4-8717-ffc0215acdd8` on board
`8988ca03-7414-47ad-b0b6-51556c701703`.

| Card assumption / obligation | Current evidence | Consequence |
|---|---|---|
| Three identities repeat one costly scenario. | In `tests/Antiphon.Tests/Application/AgentTaskLandSourceFreshnessTests.cs`, `C488_BehindSelectsRemote` and `C488_DetachedFollowUpRequiresFetch` directly await `C488_DetachedFollowUpPublishesReviewedFix`. | Replace the two bodies in place; retain all three exact class/method identities. |
| The repeated scenario runs the actual verifier. | The main method installs `RecordingRealVerifier`, builds a miniature solution, lands B rebased onto T, then runs another real `LandingVerifier` against the independently fetched target. Each successful scenario pays two verifier invocations. | An unfiltered run currently pays six successful verifier invocations through these three identities. The desired topology pays two, both inside the single end-to-end case. |
| The run was measured at 187.870 seconds. | CARD-0802 attributes that duration to **the main method only**, in Linux timing task `a7ce7cf4`. Its raw TRX and the two alias durations were not supplied here. | Treat 187.870 s as historical card evidence, not three measured durations or a current benchmark. Code measures all three before and after. |
| The two aliases are named proof obligations. | [C488 plan](2026-09-11-card-0488-land-source-freshness-plan.md) G/PC-31 names RequiresFetch; G/PC-44 names BehindSelectsRemote. [C494 plan](2026-09-27-card-0494-plan.md) V-4/R-4 and historical PC 31-49 preserve them. | Keep independent exact-method failure detection and update the coverage map explicitly. |
| Historical behind behavior fast-forwarded the source worktree. | `AgentTaskLandSourceResolver.ResolveAsync` now uses schema-3 operation semantics: Behind accepts E=observed remote B and retains local A. `LandOperationFactory` saves `SourceLocalSha=A`, Original/Reviewed/Input=B. | Test current source selection, not the retired source fast-forward. Do not introduce a runtime change. |
| A detached follow-up proves that a fetch supplied the objects. | A detached worktree shares the canonical object database. B can already be readable locally before fetch. | The focused fetch case must create B in the fixture's **independent clone**, prove B absent from the canonical object store, then observe it through production `LandingGit`. |
| Roster files are a current census. | `tests/linux-test-roster.json` and `docs/superpowers/plans/2026-09-21-card-0590-linux-test-roster.json` record the frozen CARD-0590 source commit, 40 methods for this class, its historical hash, and `disposition: exclude` for process spawning. Current anchored source attributes give 47 methods / 59 argument-expanded cases. C494's recorded CP-3 also executed 59. | Preserve both historical snapshots. Obtain current full-assembly discovery before/after and actual class execution; the frozen admitted-Linux subset is not the unfiltered assembly. |

Owners read: `docs/project-context.md`, `docs/testing-and-build.md` (checkpoint
manifest/tool, slots, exact filters, Mutation), `docs/orchestration-loop.md`
(stage order), and `docs/ops-http.md` (runner selection). Fixture owners inspected:
`LandingProtocolHarness`, `LandingSafetyHarness`, `LandingGitFixture`, and the
source-observation cases in `Infrastructure/LandingSourceFreshnessTests.cs`.

## Decisions

### D-1. Two focused cases and one retained end-to-end case

Keep the main method's miniature solution, real verifier, exact selected filter,
fresh single-probe TRX, independent target verifier, B nonce/behavior, T content
and ancestry, reviewed/prepared identity, and protected remote-source assertions.
Keep `C494_DetachedFixVerifierFailurePreventsPublication` unchanged: its failing
probe and no-publication assertions cover a separate negative obligation.

Replace both aliases with independent arrangements/assertions. Neither may call
another public `[Test]`, call the expensive scenario through a helper, install
`ConfigureRealVerifier`, or create a miniature .NET solution. Helpers, if needed,
are private to this test class and arrange fixture data only. No static/shared
cached result, order dependency, category exclusion, skip, renamed method or
new discovery identity is needed.

Rejected: deleting the aliases without replacing their claims; sharing a cached
landing result between independently filtered tests; consolidating to one test
while historical exact PC filters silently stop executing. A combined single
scenario could work with rewritten obligations, but retaining the existing names
and testing their separate seams is simpler and gives cheaper independent controls.

### D-2. BehindSelectsRemote exercises production selection and persistence

Use a new `LandingProtocolHarness` and its default controlled Git/verifier. It
already supports `AdvanceRemoteSource`, genuine repository lease admission,
production request/resolver/protocol execution and isolated database state.

1. Initialize; save local A; advance only the modeled remote source to strict
   descendant B. Assert A != B and local head is still A before execution.
2. Queue through `RequestAsync(expectedSourceSha: B, filter: "/*/*/Fixture/*")`.
   The explicit filter invokes the cheap controlled verifier. Clear only command
   traces after arrangement, then run `RunQueuedAsync` once.
3. From a fresh database context, require the exact request to have relationship
   Behind, LocalBefore=A, Remote=B, Candidate=B, Resolved=B, state Resolved,
   Expected=B and no source refusal. Assert an operation exists with
   Original=Reviewed=PreparationInput=B and SourceLocal=A, and publication Landed.
4. Require the controlled verifier's one invocation to use the detached land
   worktree and the recorded verification filter. Require the modeled target to
   equal the operation's verified SHA and remote source to remain B. No merge,
   reset, rebase or stash may run in the task worktree. Normal guarded cleanup
   can delete its branch at A; do not read a removed checkout as proof of failure.

These are production decisions and committed values, not comparisons of test
constants. This case proves selection with controlled I/O. Actual Git ancestry,
published production bytes and real verifier execution remain the main method's
obligation. Existing `C688_BehindLocalBranchLandsFromRemoteWithoutFastForward` and
stale-approval cases remain in the class regression filter.

### D-3. RequiresFetch exercises real source observation without landing

Use a new `LandingGitFixture`, no database harness and no verifier. Its `Observer`
is an independent `clone --no-hardlinks` with a separate object database.

1. Let the canonical local source branch and its tracking ref identify A. In the
   independent clone, detach at A, create a unique `nonce.txt`, commit B and push
   `HEAD:<fixture.SourceRef>` to the fixture-owned bare remote. Use a separate
   `FixtureGit` instance for arrangement/independent reads so trace assertions
   contain only the operation under test.
2. Establish B != A; read remote source B independently; assert canonical local
   source/tracking ref remain A. `git cat-file -e B^{commit}` in the canonical
   repository must fail **before** observation. No arrangement fetch into the
   canonical repository, shared-object clone or alternates is permitted.
3. Call the real `fixture.Git.ObserveSourceAsync(canonical, exact SourceRef,
   unique refs/antiphon/land/<task>/<request>/source-observed prefix, ct)` once.
   Do not fake its return value. Require Accepted=true, Sha=B, a nonempty
   fingerprint, and a non-null observation ref under that prefix.
4. Independently resolve that returned pin in the canonical repository to B;
   `git show <pin>:nonce.txt` must return B's unique bytes and `cat-file` must now
   succeed. For these post-action probes use `RunAsync` and explicit success/
   output assertions, not `RequiredAsync` exceptions: a missing pin under a
   mutant must fail the intended assertion rather than look like fixture setup
   failure. Canonical branch/HEAD and stale tracking ref remain A; remote source
   remains B. No land request, publication or build is involved.
5. Assert one source fetch for this stable remote with the exact source-to-pin
   refspec, resolved fixture push endpoint, `--no-tags` and
   `--no-write-fetch-head`. Content/object/pin assertions are primary; a trace
   containing the word `fetch` alone is insufficient.

Using the independently absent B catches both stale-local fallback and returning
the advertised B without actually fetching/pinning its objects. Fixtures and
their cleanup stay inside the existing owned temporary root.

### D-4. Preserve provenance and measure the actual savings

Add narrowly scoped CARD-0802 amendments to the C488 G/PC-31 and G/PC-44 coverage
rows and the C494 V-4/R-4, verifier-oracle and historical PC 31-49 mapping. State
that fetch/pin and Behind selection now have independent focused probes and that
the single main case still supplies real published behavior. Preserve historical
results and all unrelated PC obligations. This card does not claim the complete
C488/C494 mutation battery has run.

The two frozen JSON rosters retain their original counts, source SHA/hash and
exclusion. Do not relabel the class as Unit or included in the old admitted-Linux
subset to make this card look covered. The new evidence report records the
current census and explicitly reconciles the historical 40 with current 47/59.

## Slices and collision footprint

| Slice | Concrete work and commit boundary | Checkpoints |
|---|---|---|
| S1a: capture baseline executions | Commit the evidence-report scaffold and its exact source SHA, environment and measurement procedure, leaving every test body unchanged. Run the baseline target-method filter with `--keep-outputs`; preserve its report and output. | CP-1 |
| S1b: capture baseline census | Commit CP-1's baseline result in the evidence report, with no code changes. Copy the tool's complete CP-1 `report.json` verbatim to `.antiphon/c802-before-producer.json`. Discover from that retained build, before editing tests. | CP-2 |
| S2: replace the two aliases | Edit the two methods in `tests/Antiphon.Tests/Application/AgentTaskLandSourceFreshnessTests.cs` as D-2/D-3; retain the main/negative methods and all attributes. Add only private arrangement helpers there if needed. | Commit together with S3, then CP-3/4; CP-5 follows S4 |
| S3: reconcile obligations and evidence | Commit with S2: amend the two old plan mappings; record baseline per-case timing, identities, census, historical-roster interpretation, exact Unit count/skip ledger and pending PC variants in `docs/investigations/2026-09-30-card-0802-c488-alias-tests-verification.md`. Run CP-3/4 with `--keep-outputs`. | CP-3, CP-4 |
| S4: final census | Commit measured CP-3/4 results as evidence-only prose. Copy the complete group `report.json` verbatim to `.antiphon/c802-after-producer.json`. Discover from CP-3's retained build and compare with CP-2; then commit final census evidence. | CP-5; no rebuild for evidence-only prose |

Expected Code writes are exactly the test class, the C488 plan, the C494 plan,
and the new verification report. TestDesign may refine this plan itself.
`LandingProtocolHarness.cs`, `LandingSafetyHarness.cs`, `LandingGitFixture.cs`,
the roster JSON files, test policies, project files, and production files are
read-only dependencies. No new test class or global harness extension is planned.

For the orchestrator's collision check: CARD-0603 touches RepairSource landing
behavior, so the resolver/factory/protocol and shared landing fixtures are a
nearby dependency but outside this Code write set. Recheck that task's actual
write set for this test class or the two old plans before dispatch. CARD-0719's
quota classifier has no planned file overlap. A discovered need to edit
`server/Application/Services/AgentTaskLandSourceResolver.cs`,
`server/Application/Services/LandOperationFactory.cs`,
`server/Infrastructure/Git/LandingGit.cs` or a shared fixture requires an explicit
scope/collision reassessment, not a quiet expansion of this test optimization.
Mutation later edits its own managed snapshot, separately from implementation.

## Verification design

### Inspection and seam acceptance

TestDesign read the live CARD-0802 description and task `6bdbf52e`'s full Result,
the two alias bodies, retained main/negative bodies and real-verifier/report
helpers, `LandingProtocolHarness` request/queue/context/verifier methods,
`ControlledLandingGit` source observation/advance methods, `LandingSafetyHarness`
registration/request/queue methods, `LandingGitFixture` initialization, independent
clone, command tracing and disposal, and the existing real-source observation
tests. Production inspection covered the Behind arm and committed checkpoints in
`AgentTaskLandSourceResolver`, `LandOperationFactory.CreateAsync`'s source identity
and input fields, and `LandingGit.ObserveSourceAsync`'s advertisement/fetch/pin
confirmation. No shared fixture or production change is needed.

| Boundary inspected | Required implementation/oracle | Coverage |
|---|---|---|
| Controlled Behind resolver, real queue and database | Start at A=SeedSha; `AdvanceRemoteSource()` adds child B to the model without moving local A. Queue E=B and the explicit cheap filter, assert `queued.Status == "queued"`, then `RunQueuedAsync() == Complete`. Read the request by returned RequestId in a new context. Assert `SourceRefusalReason` null **before** Resolved/B assertions, then `State=Completed`, Behind, LocalBefore=A, Remote/Candidate/Resolved/Expected=B. Join the operation by `ApprovalLandRequestId == queued.RequestId`; assert schema 3, Original/Reviewed/PreparationInput=B, SourceLocal=A, Landed and non-null verified SHA. | V-2/R-1; 802-PC-1 |
| Controlled verifier and source protection | `Verifier.Calls == 1`; the single `(Worktree, Filter)` equals the operation's detached land path and saved request filter. RemoteTarget equals the verified SHA, RemoteSource remains B; clear both Commands and Trace immediately before execution. No merge/reset/rebase/stash command may have the task source directory as cwd. Cleanup may remove the local source branch at A. These model observations supplement the production DB verdict. | V-2/R-1; main real-Git coverage remains V-3 |
| Independent real-Git object acquisition | Use a second `FixtureGit` for all arrangement and oracle reads; hooks remain unset. Detach Observer at A, commit a unique nonce and push B to the exact SourceRef. Before observation assert B != A, canonical SourceRef and source tracking ref at A, canonical and source HEAD at A, bare remote SourceRef=B, and canonical `cat-file -e B^{commit}` fails. Observer has no alternates/shared object store. | V-2/R-1; 802-PC-2a/2b |
| Pin and content receipt | Call `fixture.Git.ObserveSourceAsync` once with a unique valid source-observed prefix. Assert Accepted, Reason null, SHA=B, fingerprint length 64, pin starts with prefix plus `/`. **Before checking the trace**, independently run `rev-parse --verify <pin>^{commit}`, `cat-file -e B^{commit}` and `show <pin>:nonce.txt`; assert success and exact B/nonce bytes. Recheck unchanged local refs/HEADs and remote B. Finally require exactly one canonical source fetch whose full argv is `fetch --no-tags --no-write-fetch-head <fixture.Remote> <SourceRef>:<returned pin>`. | V-2/R-1; 802-PC-2a/2b |
| Retained real-verifier oracle | Main and C494 negative test bodies plus `ConfigureRealVerifier`, `RecordingRealVerifier` and `AssertFreshProbeReport` remain unchanged. One recorded land-verifier call plus the explicit independent target-verifier call are **two** real invocations; recorder.Count alone is not the total. Preserve the two fresh one-probe passing TRXs, B behavior/nonce, T content/ancestry, and the separate one-probe failing TRX/no-publication case. | V-3/R-2; inherited 494-PC-30/31/32 |
| Discovery/importer | Read the pinned discovery probe, `scripts/lib/nightly-coverage.ps1` diagnostic parser, checkpoint table importer, command-row driver and TRX reader. Method OR operands retain suffix `*` because that is the documented pinned extractor syntax; exact three-name TRX equality prevents over-selection. | V-1, V-4, V-5 |

The fresh-context request and operation checks are production outcomes, not
assertions against values assigned by the test. The fetch test must not call
`AssertRemoteSourceAsync()` after pushing B: that helper expects SeedSha and
performs an extra fetch. Use the separate reader with explicit success assertions.
Neither alias may install `ConfigureRealVerifier`, call any public test body,
override the observation result, or use an already-local B. These constraints
make the three controls below reach the intended assertions.

### Delivery inventory and exclusions

No asynchronous delivery path is added or changed. The existing request producer,
`AgentTaskLandQueue` claim, persisted request/checkpoints and operation remain
joined by RequestId/ApprovalLandRequestId in V-2. This proves the resolver's
committed selection and completed protocol, not delivery to a session. There is
no session recipient or transcript claim here; busy-recipient, enqueue-crash and
receipt recovery qualification is unchanged and outside this test-only diff.

Remote races, divergence, stale approval, schema-2 repair, endpoint/ref validation,
publication fences and cleanup combinations retain their existing C488/C494
methods and controls. CP-3 executes this entire class, including its argument
rows and retained real-verifier negative case. Re-running other classes' unchanged
PC batteries, full-assembly execution, native E2E and deployment are excluded.

### Lane and evidence

The tests remain cross-platform Integration tests with the class-level
`[ParallelLimiter<ProcessSpawnLimit>]`. Use the Linux lane for comparable
before/after evidence of the reported Linux cost; no desktop/ConPTY dependency
is introduced. Read-only runner discovery on 2026-09-30 showed both OS lanes
dispatch-eligible and a Linux global default. Resolve defaults again at dispatch;
do not pin a fleet host. Git, the pinned SDK, restored TUnit dependencies,
Docker/Postgres and the normal build-slot broker must be available.

Use the checkpoint tool, once per committed group (finish each wait and commit
its following evidence slice before launching the next command):

```text
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0802-c488-alias-tests-plan.md --after S1a --keep-outputs
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0802-c488-alias-tests-plan.md --after S1b
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0802-c488-alias-tests-plan.md --after S2-S3 --keep-outputs
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0802-c488-alias-tests-plan.md --after S4
dotnet run --project tools/Antiphon.Checkpoints -- wait <run-id> --max-wait 50s
```

Continue `wait` on exit 75. **Do not launch all five rows in a single run.**
`RunScheduler` considers command rows ready immediately; `Serial` means exclusion,
not an earlier-build dependency. TestDesign therefore split baseline execution
and discovery into S1a/S1b, and added the evidence-only S4 census group. The
producer group's `--keep-outputs` prevents successful-run cleanup; each census
group can clean those manifest-owned outputs after exporting its evidence.
These four runs contain exactly the original five rows and three builds. All
rows remain Serial so the timing samples and builds within a group do not overlap.
Every test-producing row owns one isolated build and one exact filter. CP-2/5
are explicitly non-TUnit **discovery commands**, reuse their preceding output,
and expect **zero test executions**, with `Min=n/a`. Their n/a build cells do not
launch a build. Materialize the census driver below in S1a as ignored task-owned
evidence at `.antiphon/c802-census.ps1`; record its SHA256 in the evidence report
and use the identical bytes in CP-2/5. This adds no production or shared script.
Use the same Linux `UseAppHost=false` setting as the checkpoint build.

The tool obtains row build slots. Bootstrap builds/runs of the tool and any
other build/test driver use `scripts/build-slot.ps1`; report a bootstrap as a
tooling prerequisite, never an unreported test rerun. Keep the real miniature
verifiers' current build-slot gate. Exit 4 is a slot timeout, not authority to
run unleased. No simultaneous Pty test assembly, production-runner test host,
production landing, deployment or stack restart belongs to this card.

### Coverage and count acceptance

| ID | Required evidence | Checkpoint |
|---|---|---|
| V-1 | Each of the three historical exact methods executes once in the before selection; preserve its duration/outcome and fixture verifier cost. | CP-1 |
| V-2 / R-1 | Behind=A/B assertions and persisted selection/operation fields; fetch absent-object-to-present-pin/content proof; neither alias enters a real verifier. | CP-3, specifically the two aliases |
| V-3 / R-2 | The original main method still proves B behavior/nonce and T content/ancestry through two real verifier invocations and fresh one-probe TRXs; the distinct verifier-failure test still prevents publication. Remaining 55 class cases stay green. | CP-3, 59 expanded results total |
| V-4 / R-3 | Default Unit lane, including metadata/classification contracts: exactly U selected result rows, E=U-S executed, E passed, 0 failed, S explicitly accounted skips (definitions below). | CP-4 |
| V-5 / R-4 | Full unfiltered assembly discovery multiset before/after is identical, including all three named cases exactly once and 59 expanded class cases; reconcile the frozen rosters without rewriting them. Discovery plus executed CP-1/3 results provides census and execution evidence separately. | CP-2, CP-5 and CP-1/3 |
| V-6 | Before/after timing table for all three methods, plus combined time and verifier-invocation accounting. | CP-1 versus CP-3 |

The selected format is the pinned **TUnit 1.44.0 / MTP 2.2.2 diagnostic discovery**
adapter already used by `scripts/lib/nightly-coverage.ps1`, exported as
`antiphon-tunit-discovery-v1`. The [recorded nine-row feasibility probe](../../investigations/2026-09-11-card-0487-testdesign-discovery.md)
demonstrates distinct UIDs for Arguments, MethodDataSource and inherited rows.
Plain/Detailed list console output supplies only display names;
`--list-tests --report-trx` is unsupported. Do not use either as a census format.
No new full-assembly discovery or execution is claimed by TestDesign.

CP-2/5 invoke unfiltered `--list-tests --diagnostic --no-ansi --no-progress`.
The driver below refuses existing output, missing/ambiguous diagnostic logs,
version drift, duplicate UIDs, missing identity fields, zero discovery and the
wrong class/name counts. It stores the raw stdout/stderr/diagnostic, assembly
hash, exported JSON and a sorted normalized multiset before checkpoint cleanup.
Normalization retains the **entire UID** (including parameter signature and all
data-row suffixes), assembly simple name, namespace, type, method, categories and
exclusion bit. Only order, log envelope, display text and assembly hash are
excluded from equality; the assembly hash is separately retained and may change.
Do not strip numeric UID suffixes, paths/values inside UIDs, or deduplicate rows.
The adapter's Signature field is empty for diagnostic text; raw UID and raw
TestMethodIdentifierProperty records remain the argument-identity evidence.

CP-5 requires byte equality of these sorted identity records and records N before
and N after, delta 0, added=[] and removed=[]. N is measured, never copied from
the frozen 7,038-method inventory. The class has 47 source methods / 59 expanded
rows; generated `[Test]` text inside miniature-source string literals is not an
outer declaration. Each of the three named methods has exactly one row.

For CP-4, **U** is the number of CP-2 nodes whose Categories contains exactly
`Unit`, excluding explicitly non-selected OptIn/Explicit identities (retain that
separate exclusion ledger). **S** is the expanded count of documented existing
skip identities for this host/settings, and **E=U-S** is the exact expected
executed count. After CP-2 and before CP-4, Code freezes U, each expected skip and
its source condition, S and E in the S2-S3 evidence commit. This is a census-bound
count rule, not permission to accept any nonzero total: `Min=1` is only the
tool's bootstrap floor. Require CP-4's joined TRX Class.Method multiplicities to
equal the selected census after the explicit skip ledger, and record the numeric
E/passed/failed/S alongside the checkpoint line. Existing examples include
`TimeoutTests`' four Windows-only methods and `AgentRegistrySettingsTests`' npm
shim case on Linux. An unexpected skip, unmatched row or count mismatch requires
investigation and a documented manifest/evidence correction, not lowering E to
the observed passing count. No new skip/category change is authorized.

Census-only exit zero never proves execution. CP-1/3 require exactly 3/59 fresh
executed TRX results and the exact named methods. No unfiltered execution is
authorized by a discovery row. The frozen admitted-Linux subset excludes this
class and cannot substitute for CP-3. A full-assembly timing campaign needs its
own budgeted manifest.

### Census driver (task-local evidence only)

Save this block verbatim as `.antiphon/c802-census.ps1` before S1a's run. The
checkpoint command supplies the phase; that phase selects its retained producer
build and copied tool report. The current commit may differ only in Markdown
evidence, and the summary records both source and census commits.
A failed attempt keeps its evidence. Before rerunning the same row, archive its
exact owned phase directory under an attempt-suffixed name and record the rerun;
never delete a directory inferred merely from its age/name. Do not run this
driver outside CP-2/5 or nest another slot wrapper inside those leased rows.

```powershell
param([Parameter(Mandatory)][ValidateSet('before','after')][string]$Phase)
$ErrorActionPreference = 'Stop'
$repo = (Get-Location).Path
$project = 'tests/Antiphon.Tests'
$output = 'bin-c802-' + $Phase + '/'
$root = Join-Path $repo '.antiphon/c802-census'
$dir = Join-Path $root $Phase
if (Test-Path -LiteralPath $dir) { throw 'census phase evidence already exists' }
[void](New-Item -ItemType Directory -Path $dir)
. (Join-Path $repo 'scripts/lib/nightly-coverage.ps1')
$producer = Get-Content -Raw (Join-Path $repo ('.antiphon/c802-' + $Phase + '-producer.json')) | ConvertFrom-Json
$producerRowId = if ($Phase -eq 'before') { 'CP-1' } else { 'CP-3' }
$expectedExecuted = if ($Phase -eq 'before') { 3 } else { 59 }
$producerRows = @($producer.rows | Where-Object { $_.id -ceq $producerRowId })
if ($producer.exitCode -ne 0 -or $producer.commit -notmatch '^[0-9a-f]{40,64}$' -or
    $producerRows.Count -ne 1 -or $producerRows[0].executed -ne $expectedExecuted -or
    $producerRows[0].passed -ne $expectedExecuted -or $producerRows[0].failed -ne 0 -or
    $producerRows[0].skipped -ne 0) { throw 'missing successful producer report' }
& git merge-base --is-ancestor $producer.commit HEAD
if ($LASTEXITCODE -ne 0) { throw 'producer commit is not an ancestor' }
$changed = @(& git diff --name-only $producer.commit HEAD)
if ($LASTEXITCODE -ne 0 -or @($changed | Where-Object { $_ -cnotmatch '^docs/.+\.md$' }).Count) {
    throw 'census commit differs from producer beyond documentation'
}
$assemblies = @(Get-ChildItem -LiteralPath (Join-Path $project $output) `
    -Recurse -File -Filter Antiphon.Tests.dll)
if ($assemblies.Count -ne 1) { throw 'expected one producer test assembly' }
$assemblyHash = (Get-FileHash -LiteralPath $assemblies[0].FullName -Algorithm SHA256).Hash
$sha = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'cannot read source commit' }
$diag = Join-Path $dir 'diag'
$start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$start.WorkingDirectory = $repo
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($arg in @('run', '--project', $project, '--no-build',
    ('--property:OutputPath=' + $output), '--property:UseAppHost=false', '--',
    '--list-tests', '--no-ansi', '--no-progress', '--diagnostic',
    '--diagnostic-output-directory', $diag,
    '--results-directory', (Join-Path $dir 'results'))) {
    $start.ArgumentList.Add($arg)
}
$process = [System.Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [IO.File]::WriteAllText((Join-Path $dir 'stdout.log'), $stdout.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $dir 'stderr.log'), $stderr.GetAwaiter().GetResult())
    if ($process.ExitCode -ne 0) { throw ('discovery exit ' + $process.ExitCode) }
} finally { $process.Dispose() }
$logs = @(Get-ChildItem -LiteralPath $diag -Recurse -File -Filter '*.diag' |
    Where-Object { [IO.File]::ReadAllText($_.FullName).Contains('DiscoveredTestNodeStateProperty') })
if ($logs.Count -ne 1) { throw 'expected one discovery diagnostic log' }
$nodes = @((ConvertFrom-NightlyDiagnosticLog -Path $logs[0].FullName -Kind discovery).Nodes)
if ($nodes.Count -eq 0) { throw 'empty census' }
$discoveredRecords = @([IO.File]::ReadAllLines($logs[0].FullName) |
    Where-Object { $_.Contains('DiscoveredTestNodeStateProperty') })
if ($discoveredRecords.Count -ne $nodes.Count) { throw 'unparsed or duplicated discovered records' }
$lines = [Collections.Generic.List[string]]::new()
foreach ($node in $nodes) {
    foreach ($field in @('Uid','Assembly','Namespace','Type','Method')) {
        if ([string]::IsNullOrWhiteSpace([string]$node.$field)) { throw ('missing ' + $field) }
    }
    [string[]]$categories = @($node.Categories)
    [Array]::Sort($categories, [StringComparer]::Ordinal)
    $record = [ordered]@{ uid=$node.Uid; assembly=$node.Assembly;
        namespace=$node.Namespace; type=$node.Type; method=$node.Method;
        categories=$categories; excluded=[bool]$node.Excluded }
    $lines.Add(($record | ConvertTo-Json -Depth 8 -Compress))
}
$lines.Sort([StringComparer]::Ordinal)
$normalized = [string]::Join("`n", $lines) + "`n"
[IO.File]::WriteAllText((Join-Path $dir 'identities.jsonl'), $normalized)
$document = ConvertTo-NightlyDiscoveryDocument -Nodes $nodes -AssemblyHash $assemblyHash
$document | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $dir 'discovery.json') -Encoding utf8
$class = @($nodes | Where-Object {
    $_.Namespace -ceq 'Antiphon.Tests.Application' -and $_.Type -ceq 'AgentTaskLandSourceFreshnessTests'
})
if ($class.Count -ne 59 -or @($class.Method | Sort-Object -Unique).Count -ne 47) {
    throw 'source class must have 47 methods / 59 expanded cases'
}
foreach ($method in @('C488_DetachedFollowUpPublishesReviewedFix',
    'C488_BehindSelectsRemote', 'C488_DetachedFollowUpRequiresFetch')) {
    if (@($class | Where-Object { $_.Method -ceq $method }).Count -ne 1) {
        throw ('expected one discovered row for ' + $method)
    }
}
$unit = @($nodes | Where-Object { $_.Categories -ccontains 'Unit' -and -not $_.Excluded })
$summary = [ordered]@{ phase=$Phase; producerCommit=$producer.commit; censusCommit=$sha; assemblyHash=$assemblyHash;
    discovered=$nodes.Count; classMethods=47; classCases=59; unitSelected=$unit.Count;
    executed=0; parser='antiphon-tunit-discovery-v1'; tunit='1.44.0'; mtp='2.2.2' }
if ($Phase -eq 'after') {
    $before = [IO.File]::ReadAllText((Join-Path $root 'before/identities.jsonl'))
    if (-not [string]::Equals($before, $normalized, [StringComparison]::Ordinal)) {
        throw 'full assembly census differs; retain both files and report added/removed identities'
    }
    $summary['delta'] = 0
    $summary['added'] = @()
    $summary['removed'] = @()
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dir 'summary.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 8 -Compress
```

This is an evidence producer, not a new TUnit test. Its literal 47/59 and
three-name assertions can fail on discovery loss/duplication; the exported raw
records, rather than these constants, establish the observed counts. A discovery
format failure is a failed checkpoint requiring diagnosis, never permission to
fall back to display-name counting or to execute the whole assembly.

### Timing and execution evidence

Collect duration from each outer `UnitTestResult`, joined by testId to
`TestDefinitions/UnitTest/TestMethod` (not display-name guesses). The checkpoint
tool remains the verdict source; reading TRX durations supplements its report.
Include SHA, host/OS/SDK, filter, fresh TRX path, slot wait and row wall time.
Keep these distinct from test-case durations and nested probe durations. Use
the same host/settings for before and after. Do not infer each alias took
187.870 s or turn three times that figure into measured aggregate time.

| Method | Historical evidence | Code must record |
|---|---|---|
| `C488_DetachedFollowUpPublishesReviewedFix` | 187.870 s, card-reported full-suite sample | CP-1 before seconds, CP-3 after seconds, outcomes, real-verifier calls 2 -> 2 |
| `C488_BehindSelectsRemote` | Duration not supplied; calls main body | Before/after seconds, outcomes, real-verifier calls 2 -> 0 |
| `C488_DetachedFollowUpRequiresFetch` | Duration not supplied; calls main body | Before/after seconds, outcomes, real-verifier calls 2 -> 0 |

Acceptance requires all assertions and census preservation, structurally one
successful end-to-end scenario instead of three, and observed alias savings.
Target at least 80% lower combined alias duration and at least 40% lower combined
three-case duration; these are measurement goals, not timing assertions in CI.
If contention dominates or a target is missed, report the measured result and
cause explicitly; do not claim performance acceptance or repeat runs without
recording why. Preserve the negative real-verifier case's separate cost.

### Positive controls (post-land, not Code checkpoint rows)

Each control runs only its exact named outer method, with one executed result
per baseline/red/restored-green phase. Apply one compiling production defect
at a time, retain all other guards, and require the intended assertion failure.
Build/setup/zero-test/timeout failures are not kills. No test assertion is
mutated. Use the managed SourceLanding snapshot and external evidence/restoration
root described in `docs/testing-and-build.md`.

### Guard inventory

| Guard whose detector changes | Independently bypassable invariant | Unique control |
|---|---|---|
| 802-G-1 (488 G-44) | Behind E=B is accepted using observed B rather than stale local A | 802-PC-1 |
| 802-G-2a (488 G-31, freshness) | A successful observation reports fetched B rather than local A | 802-PC-2a |
| 802-G-2b (488 G-31, acquisition) | Advertising B is insufficient: B's objects and returned pin exist locally | 802-PC-2b |

Changed-coverage guards=3, mapped=3, missing=0, duplicate control maps=0.
Other D-2/D-3 assertions corroborate these outcomes or retain existing safeguards;
no production guard changes. The unchanged real-verifier guards stay mapped to
494-PC-30/31/32 as below. Historical 488-PC-31 is a family cross-reference, not
one mutation credited for both acquisition and freshness.

### Executable mutation variants

| Control | Production defect | Exact detector and required red |
|---|---|---|
| 802-PC-1 / 488-PC-44 | In **only the Behind arm** of `AgentTaskLandSourceResolver.ResolveAsync`, replace `if (expected != observed.Sha)` with `if (expected != local)`. Leave factory validation and all other resolver guards intact. | `/*/*/AgentTaskLandSourceFreshnessTests/C488_BehindSelectsRemote`: E=B != A reaches refusal; the fresh request's `SourceRefusalReason.ShouldBeNull()` fails with `reviewed_source_mismatch`. The queued result is otherwise Complete. No call to another test, real verifier or resolver mock can mask this. |
| 802-PC-2a / 488-PC-31 | At the successful terminal return in `LandingGit.ObserveSourceAsync`, replace `return new(observed, pin, fingerprint, null);` with `return new(await CommitAsync(repository, sourceFullRef, ct), pin, fingerprint, null);`. Leave real fetch, pin validation and both endpoint checks intact. | `/*/*/AgentTaskLandSourceFreshnessTests/C488_DetachedFollowUpRequiresFetch`: Accepted still passes, then `observed.Sha.ShouldBe(b)` fails because the untouched canonical SourceRef resolves A. B was actually fetched/pinned, so this red isolates stale-SHA selection from acquisition. |
| 802-PC-2b / 488-PC-31 | In `ObserveSourceAsync`, replace the block from `var fetch = await RunAsync(...)` through `if (observed != fields[0]) continue;` with `var observed = fields[0];`. Keep advertisement/OID checks, pin-name validation, the following endpoint-fingerprint check and terminal return unchanged. No fetch/update-ref is added elsewhere. | `/*/*/AgentTaskLandSourceFreshnessTests/C488_DetachedFollowUpRequiresFetch`: Accepted/SHA/prefix pass; the first independent `rev-parse --verify <pin>^{commit>` success assertion fails. B object/content availability and source-fetch count would also fail. Assert pin success before using its output or the trace; a `RequiredAsync` setup exception is not a kill. |

Each variant has exactly **1 passed baseline, 1 intended failed outer test, 1
passed restored test**, zero skips, across three separate exact-filter phases.
Three variants therefore require **9 outer results: 6 passing and 3 intended
failing**, not nine distinct methods. Each phase builds restored/mutant source
into its own owned output; restoration refreshes timestamps and validates the
restored production diff. There is no mutation of tests or test doubles. A
survivor is a detection gap; a compile/setup/timeout/zero-count failure is invalid
evidence and must be reported as such. These are source-validated compiling
edits, not a claim that TestDesign executed the mutation battery.

These are three independently reportable mutation variants across two claim
families. Preserve historical IDs as cross-references, not a claim that old
evidence applies to the new landed SHA. The unchanged real-verifier controls
494-PC-30 (filter/cwd), 494-PC-31 (ignore failed verifier), and 494-PC-32 (lose T)
retain their original methods and obligations; this card does not reopen or
claim completion of that unrelated full battery. Review must verify their
oracles survived the diff. Post-land discovery must also look for new masking
or missing detection introduced by the replacement test bodies.

### Cost

Estimates reserve complete work, not enforced sleep or evidence of measurements.
Ordinary checkpoint floor is **39 minutes** (15 + 2 + 15 + 5 + 2); allow **35
minutes** for Code authoring/evidence, giving **74 minutes** plus host-slot wait.
The broader class run is necessary because the edited file contains other
landing tests; no other landing class or native E2E is changed. The Unit lane
is the documented ordinary regression default.

Post-land Mutation reserves **30 minutes** for three independent complete
green/compiling-red/restore-build-green cycles (10 each), plus **15 minutes** for
discovery, custody/restoration audit and reporting: **45 minutes**. Code plus
Mutation reservation is **119 minutes**, with separate ordinary Review budget
of **15 minutes**. These numbers cover this card only. No saving in the old
C488/C494 full battery is claimed before measurement.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1a | `tests/Antiphon.Tests -> bin-c802-before/` | before-three-claims | `/*/*/AgentTaskLandSourceFreshnessTests/(C488_DetachedFollowUpPublishesReviewedFix*)\|(C488_BehindSelectsRemote*)\|(C488_DetachedFollowUpRequiresFetch*)` | V-1, V-6 | exactly 3 executed, each named method once, 0 failed/skipped; capture all 3 durations | 3 | 15 | true |
| CP-2 | S1b | n/a | before-full-census | `pwsh -NoProfile -File .antiphon/c802-census.ps1 -Phase before` | V-4, V-5 | exit 0; exactly 0 executed; diagnostic multiset N, 47/59 class methods/cases, all 3 identities once; export Unit population U | n/a | 2 | true |
| CP-3 | S2-S3 | `tests/Antiphon.Tests -> bin-c802-after/` | after-source-class | `/*/*/AgentTaskLandSourceFreshnessTests/*` | V-2, V-3, V-6, R-1, R-2 | exactly 59 executed, 0 failed/skipped; all 3 identities once and their durations | 59 | 15 | true |
| CP-4 | S2-S3 | `tests/Antiphon.Tests -> bin-c802-unit/` | unit-regression | `/*/*/*/*[Category=Unit]` | V-4, R-3 | exactly E=U-S executed/passed, 0 failed, S documented skips; U selected identities from CP-2, numeric U/S/E frozen before execution; no missing or extra result identities | 1 | 5 | true |
| CP-5 | S4 | n/a | after-full-census | `pwsh -NoProfile -File .antiphon/c802-census.ps1 -Phase after` | V-5, R-4 | exit 0; exactly 0 executed; full normalized identity multisets equal, delta 0; 47/59 class methods/cases and all 3 identities once | n/a | 2 | true |

## Handoff and completion

TestDesign confirms the existing fixture seams, pinned diagnostic discovery
format, normalization and three independently reachable production defects by
source inspection. Its static checks cover the five-row manifest, the embedded
driver's PowerShell syntax and the 47-method/59-expanded-row source census.
Retain this closed checkpoint list unless an identified defect requires a
documented revision. Plan/TestDesign builds, test executions and mutations: **0**;
source counts and historical discovery evidence are not new execution results.

Code commits/pushes each slice group before running its rows and finishes with
an evidence-only report commit. Report every CP with commit, build status,
exact filter/command, counts, failures/skips, paths and reruns; include before/
after timing and census tables, and CP-4's census-derived numeric expectation
plus skip identities. Any changed dependency or new class count is
reconciled by identity before changing a Min floor. Code returns `next: review`
with all three PC variants pending. The caller records the post-land companion
and commissions Mutation only after ordinary Review and confirmed publication.
No deployment is required for this test-only change.
