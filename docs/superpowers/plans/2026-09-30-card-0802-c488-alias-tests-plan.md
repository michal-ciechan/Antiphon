# CARD-0802: keep the C488 claims without repeating the real verifier

Plan task: `6bdbf52e-007f-4a03-99c9-ef5ad39df238`. Authoring baseline:
`83ccea1a3483de5357ac7d6b9979c149bc910a55`. This is a test-only change.
Next stage is TestDesign to check the seams, census procedure and controls below,
then Code, ordinary Review, land, and post-land SourceLanding Mutation.

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
| S1: capture baseline | Commit the evidence-report scaffold and its exact source SHA, environment and measurement procedure, leaving every test body unchanged. Run the baseline target-method filter and full-assembly discovery; preserve outputs before editing tests. | CP-1, CP-2 |
| S2: replace the two aliases | Edit the two methods in `tests/Antiphon.Tests/Application/AgentTaskLandSourceFreshnessTests.cs` as D-2/D-3; retain the main/negative methods and all attributes. Add only private arrangement helpers there if needed. | Commit together with S3, then CP-3..CP-5 |
| S3: reconcile obligations and evidence | Amend the two old plan mappings; record before/after per-case timing, executed identities, full discovery census, historical-roster interpretation and pending PC variants in `docs/investigations/2026-09-30-card-0802-c488-alias-tests-verification.md`. Populate measured after results after checkpoint completion in an evidence-only commit. | CP-3..CP-5; no rebuild for evidence-only prose |

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

### Lane and evidence

The tests remain cross-platform Integration tests with the class-level
`[ParallelLimiter<ProcessSpawnLimit>]`. Use the Linux lane for comparable
before/after evidence of the reported Linux cost; no desktop/ConPTY dependency
is introduced. Read-only runner discovery on 2026-09-30 showed both OS lanes
dispatch-eligible and a Linux global default. Resolve defaults again at dispatch;
do not pin a fleet host. Git, the pinned SDK, restored TUnit dependencies,
Docker/Postgres and the normal build-slot broker must be available.

Use the checkpoint tool, once per committed group:

```text
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0802-c488-alias-tests-plan.md --after S1
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0802-c488-alias-tests-plan.md --after S2-S3
dotnet run --project tools/Antiphon.Checkpoints -- wait <run-id> --max-wait 50s
```

Continue `wait` on exit 75. All rows are Serial so the discovery-only commands
run after their producer build and before the tool's successful-run output
cleanup, and the before/after timing samples do not overlap another row here.
Every test-producing row owns one isolated build and one exact filter. CP-2/5
are explicitly non-TUnit **discovery commands**, reuse their preceding output,
and report no executed-test count. Their n/a build cells do not launch a build.
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
| V-4 / R-3 | Default Unit lane, including metadata/classification contracts, remains green. Record actual expanded count; 1 is only a nonzero floor, not a claim about current suite size. | CP-4 |
| V-5 / R-4 | Full unfiltered assembly discovery multiset before/after is identical, including all three named cases exactly once and 59 expanded class cases; reconcile the frozen rosters without rewriting them. Discovery plus executed CP-1/3 results provides census and execution evidence separately. | CP-2, CP-5 and CP-1/3 |
| V-6 | Before/after timing table for all three methods, plus combined time and verifier-invocation accounting. | CP-1 versus CP-3 |

`--list-tests` is used only for a **full-assembly census**, with no filter: its
known unscoped behavior is appropriate here. Preserve both raw command logs and
a normalized sorted multiset of discovery identities, including argument rows.
Strip only presentation/order noise; do not collapse argument rows into method
names. Record full assembly total N before and N after, difference 0, and added/
removed identities (both empty). N is measured, not copied from the frozen 7,038
source-method inventory. Record this class's 47 source methods separately from
its 59 expanded cases. Generated `[Test]` text inside the two miniature-source
string literals is not an outer-assembly test declaration.

If this runner's discovery output cannot identify expanded rows reliably,
TestDesign must select its supported machine-readable discovery format before
Code starts; do not invent counts from display text. Census-only exit zero does
not prove execution. CP-1 and CP-3 must contain the named executed methods and
nonzero fresh TRX counts. No unfiltered execution is authorized by a discovery
row: the requested full-suite census is discovery, not a claim of a green full
Linux assembly. The older admitted-Linux subset excludes this class and cannot
substitute for CP-3. A separate full-assembly timing campaign would need its own
budgeted manifest; its known unrelated platform failures are outside this card.

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

| Control | Production defect | Exact detector and required red |
|---|---|---|
| 802-PC-1 / 488-PC-44 | In `AgentTaskLandSourceResolver.ResolveAsync`, make the Behind arm compare E to local A instead of observed B. This compiles and models preferring stale local source; other guards remain intact. | `/*/*/AgentTaskLandSourceFreshnessTests/C488_BehindSelectsRemote`: expected successful Behind resolution is refused or operation absent; required Candidate/Resolved/Original/Reviewed=B assertions cannot all pass. The fetch-only test does not exercise this resolver. |
| 802-PC-2a / 488-PC-31 | At the source observation return in `LandingGit.ObserveSourceAsync`, substitute a successful observation of canonical local source A for freshly fetched B. Keep the real fetch and its error handling intact. | `/*/*/AgentTaskLandSourceFreshnessTests/C488_DetachedFollowUpRequiresFetch`: observed SHA must equal independently pushed B, not A. Pin/content correlation also cannot agree. |
| 802-PC-2b / 488-PC-31 | After validated advertisement and pin-name validation in `ObserveSourceAsync`, replace the fetch/resolve confirmation block with a successful return of advertised B and the generated pin, without fetching or creating it. Leave endpoint/ref parsing intact. | Same exact RequiresFetch filter: success/SHA alone may pass, but source fetch count, pin resolution and B object/content availability must fail. B's proven initial absence makes this a real missing-fetch detector. |

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
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c802-before/` | before-three-claims | `/*/*/AgentTaskLandSourceFreshnessTests/(C488_DetachedFollowUpPublishesReviewedFix*)\|(C488_BehindSelectsRemote*)\|(C488_DetachedFollowUpRequiresFetch*)` | V-1, V-6 | exactly 3 executed, each named method once, 0 failed/skipped; capture all 3 durations | 3 | 15 | true |
| CP-2 | S1 | n/a | before-full-census | `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c802-before/ --property:UseAppHost=false -- --list-tests` | V-5 | exit 0; capture full discovery multiset N, 59 class cases, all 3 identities once; execution count n/a | n/a | 2 | true |
| CP-3 | S2-S3 | `tests/Antiphon.Tests -> bin-c802-after/` | after-source-class | `/*/*/AgentTaskLandSourceFreshnessTests/*` | V-2, V-3, V-6, R-1, R-2 | exactly 59 executed, 0 failed/skipped; all 3 identities once and their durations | 59 | 15 | true |
| CP-4 | S2-S3 | `tests/Antiphon.Tests -> bin-c802-unit/` | unit-regression | `/*/*/*/*[Category=Unit]` | V-4, R-3 | all selected, 0 failed; report expanded actual count and every skip | 1 | 5 | true |
| CP-5 | S2-S3 | n/a | after-full-census | `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c802-after/ --property:UseAppHost=false -- --list-tests` | V-5, R-4 | exit 0; before/after full identity multisets equal, delta 0; 59 class cases and all 3 identities once; execution count n/a | n/a | 2 | true |

## Handoff and completion

TestDesign confirms the discovery output format/count normalization and that
each compiling mutant reaches the intended seam with independent detection.
Retain this closed checkpoint list unless an identified defect requires a
documented revision. Plan-stage builds/tests/mutations executed: **0**; source
counts and historical evidence above are explicitly not new execution results.

Code commits/pushes each slice group before running its rows and finishes with
an evidence-only report commit. Report every CP with commit, build status,
exact filter/command, counts, failures/skips, paths and reruns; include before/
after timing and census tables. Any changed dependency or new class count is
reconciled by identity before changing a Min floor. Code returns `next: review`
with all three PC variants pending. The caller records the post-land companion
and commissions Mutation only after ordinary Review and confirmed publication.
No deployment is required for this test-only change.
