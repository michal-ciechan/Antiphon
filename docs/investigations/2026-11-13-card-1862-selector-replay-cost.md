# CARD-1862: selector replay cost and containment S1

The final exact class passes **27/27**, with identical case names/outcomes and
unchanged assertions, in **196.679s versus 519.334s**: **322.654s
saved (62.13%)**, exceeding the 200s target. The final checkpoint run passes
all four rows with clean exact-source/build provenance. S1 diagnostic results
and the ordinary-gate limitation are below.

## Source and design

Base: cc48c70050a2932eddf4abe18cea8659d478c33e. Final tested code:
b9350ea1e3c91bba26007dafd982aa923af2938a.
Plan: docs/superpowers/plans/2026-11-13-card-1862-selector-replay-cost-plan.md.

The baseline repeatedly starts PowerShell and reconstructs the whole tests-tree
census and 2,332-edge derived index. The repair has three measured parts:

1. scripts/lib/SelectorCensus.cs reads every current C# file in one compiled
   traversal, creates new class/file maps each time, retains duplicate project
   ownership, and uses the original ordinal sorting and case rules. It applies
   the **unchanged line-bound regex** only to lines containing its required class
   literal; lexical matches in raw strings/comments are preserved. Bin/obj
   exclusions are unchanged.
2. The selector uses 66 identical direct generic constructors instead of repeated
   New-Object command/type resolution, especially in index hydration. Normal
   standalone CLI output and exit contracts remain. No table, git result, path,
   declaration, OS label or selected roster is retained across requests.
3. DerivedImpactIndexTests uses one lazy owned PowerShell host through a TUnit
   PerClass fixture. Every selection creates and disposes a **new runspace**,
   rereads the real script/table/index/git/tree, and returns its own request id,
   exit code, stdout and stderr. Only runtime/immutable compiled code is shared.
   The worker restores its working directory after each request. Refusal does
   not poison a later valid request. No restart/retry hides a failed request.

The existing ProcessSpawnLimit lane, all 27 methods, the 17 replay rows, six
historical-selector comparisons, arrays and assertions remain. Four private
helpers become instance methods to use the shared fixture; the test bodies and
assertions are unchanged. The committed index gains exactly the six-line reverse
edge from DerivedSelectorReplay.cs to DerivedImpactIndexTests.

Request timeout stays 120s, the existing selector assertion 60s and method
timeout 600s. Teardown closes stdin and joins exit/error drain; failure kills
and joins only the owned tree. Response id/pid matching prevents stale response
reuse. Final TRX records SELECTOR_HOST requests=54 stopped=true, and a process
check finds no surviving owned replay host. The controller emits all 54 original
invocation results. The worker is a test-only stdio transport, not a service or
selection cache.

## Correctness and freshness evidence

Both hosted and standalone witnesses pass nine script assertion groups and
seven stale-output blindness assertions. These cover changed/new/removed
declarations, malformed tables and stale named classes, duplicate class projects,
changed Windows/Linux labels, independent scan instances, and differential
comparison against the original full-text declaration regex. That comparison
checks both full maps over 2,629 declaration-bearing files and 4,263 class names,
plus CRLF, modifiers, partials, raw strings, multiple classes on one line and
word-boundary shapes. The hosted witness handles 13 requests in one stable pid,
then joins its clean exit; its invalid-then-valid calls preserve exit status.
These are script assertion groups, **not additional TUnit results or source
mutations**. Mutation was explicitly waived by the operator.

Sorted method/outcome roster SHA256 is identical in all four complete class
runs: 4dd732dedd60a802131655d721a8191ec18cb2ef54a253c4d1fca20780bc8482.
All 27 pass; none is skipped. Constructor fixture metadata changes the TUnit
native id, but not the reported 27-case names or results.

## Exact-class measurements

Seconds; each column selects exactly /*/*/DerivedImpactIndexTests/*.

| Measure | Baseline | Compiled walk | Constructor/walk refinement | Hosted final |
|---|---:|---:|---:|---:|
| Tests wall | 519.334 | 426.143 | 420.559 | 196.679 |
| Summed cases | 519.265 | 426.054 | 420.448 | 196.570 |
| s1_replay_matches_declaration_and_index_edges | 173.640 | 126.676 | 90.649 | 27.133 |
| merge_subset_matches_the_hand_selector | 148.616 | 131.855 | 141.119 | 81.994 |
| capped_real_paths_keep_the_specific_class | 33.046 | 30.573 | 21.707 | 2.271 |
| windows_only_classes_stay_out_of_linux_filters | 50.966 | 21.373 | 31.899 | 8.400 |
| committed_index_matches_the_builder | 29.034 | 38.313 | 41.276 | 24.756 |
| override_file_names_exist_and_are_undeclared | 30.076 | 26.245 | 24.819 | 26.844 |

The first two refinements saved only 93.191s and
98.775s respectively. Those green behavior receipts do not earn
the target. Their measured process compilation/startup cost justified the hosted
refinement; no green full-class run was repeated for timing alone. Every further
full class run followed changed executable code, or recovered exact missing
execution after a total-budget cancellation. No full CARD-1860 selection was
rerun. Its historical 84-test/518.150s and 517.720s DerivedImpact sum remain
separate historical observations.

### Where time went

The same 46 successful JSON invocations have joined spans in each complete run.
These sums are nested and **not additive class wall totals**. Table includes its
JSON input; index hydration includes index JSON; census includes compile/walk.
The remaining eight invocation records include the six unchanged historical
script calls, one text invocation and the schema refusal. Missing subspans are
unknown, not zero.

| Seconds | Baseline | Compiled walk | Constructor/walk | Hosted final |
|---|---:|---:|---:|---:|
| Table/catalog validation | 21.494 | 22.657 | 23.228 | 3.801 |
| Census inclusive | 247.790 | 67.480 | 77.090 | 24.800 |
| Helper compilation | not instrumented | 29.931 | 51.216 | 0.886 |
| Walk | not instrumented | 36.239 | 23.980 | 23.788 |
| File reads within walk | not instrumented | not instrumented | 14.053 | 14.262 |
| Declarations/maps within walk | not instrumented | not instrumented | 4.874 | 4.340 |
| Downgrade matching within walk | not instrumented | not instrumented | 2.250 | 2.088 |
| Index hydration inclusive | not instrumented | not instrumented | 24.400 | 13.514 |
| JSON input | 4.355 | 5.332 | 7.972 | 2.937 |
| JSON output | 0.827 | 1.515 | 1.520 | 0.020 |
| Selector git subprocesses | 0.613 | 1.002 | 1.587 | 1.441 |
| Whole script | 328.111 | 208.403 | 166.188 | 49.642 |

Standalone process startup/exit/transport remainder sums to 37.663s in baseline
and 67.035s in the pre-host final, with another 51.216s of helper compilation
in the latter. These are measured remaining costs, not all pure startup. The
independent first-statement probe in the final hosted witness measures 751.949ms
startup and 133.166ms exit/drain; the normal witness measures 726.160ms and
142.860ms. A pure-startup baseline probe is unavailable. Hosted invocation
remainder instead includes fresh-runspace/transport setup; it is not labelled
as 54 new process startups. Directory enumeration/sorting and unrecorded
fixture subdivisions stay remainder. DB/container/web-host phases are not
applicable to this class; absent finer fixture/teardown spans stay unknown.

All measurements: Debian 12, 24 cores, SDK 10.0.404, .NET 9.0.23, TUnit 1.44.0.
Baseline execution load was 20.47/18.27/21.70, available memory 96,191,532KB.
Compiled-walk sample: 31.64/34.74/30.60, 90,914,728KB. Constructor/walk build
load reached 60-82; its class recovery declined from 50.26/57.67/57.67 to
37.36/46.49/53.04. Hosted execution sample at 04:31:55 UTC:
27.26/45.54/52.21, 94,833,460KB. Raw samples/initial host snapshots are retained.
These are single observations with different load, not controlled repeated
benchmarks or a measured run p95. No timeout was derived or widened from them.
Bounded next collection: ordinary Review, then three naturally required
comparable runs; no extra broad timing run.

## Checkpoint outcomes and source binding

- Baseline a7192898cc5a2b1c755b46cf336ddc2c03c43dda, run
  20261113-013929-4c52: CP-0 prerequisite and CP-1, 27/0/0.
- Witness 4c5f486cfd1181684d27738af6be6b0b60ab5286,
  20261113-020839-bb01: introduced Select alias harness failure; **no red credit**.
  Corrected in 0b3014168f06ac15187c3436e4bb3dee70fa564e.
- 20261113-020936-32e4: intended pre-fix red after six freshness groups and
  blindness controls; census 5912.620ms fails the unchanged 2000ms assertion.
  35.072s execution, zero admission.
- 674bf6c76861676bae2eab08c285ea2b387c4a98,
  20261113-021048-9211: CP-3/4 green, eight script groups, 27/0/0.
- fec8773b7abd5628554297ab2f4962986fe2cd8a,
  20261113-023901-3eda: CP-5 script passes, but run exhausts the unchanged
  30m execution budget before CP-6 TRX. Exit 5 remains ineligible. Wall 1997.166s,
  execution 1802.128s, admission 195.038s. Compiler alone takes 12m53.45s,
  zero errors; source/output custody adds substantial cost. Owned compiler
  CPU advances; this was not a test hang.
- Exact missing CP-6 recovered once at the same fec SHA through supported tool
  row --no-build, with its clean same-source stamp and identical properties:
  CP-6-20261113-031410-4ee0, 27/0/0, source validator passes. It has an unchanged
  18m foreground execution bound, not a wider allowance. CP-5 is recovered
  separately at the same SHA (20261113-032145-b604), nine groups, 45.223s
  execution. A row inside a failed report cannot be validated as green.
- A leased SDK feasibility probe preserves exit 0,1,0 through independent
  runspaces without terminating the host: 541s admission, 2s held. This is the
  declared unlisted narrow exploration for the observed startup/compile cost.
- Final b9350ea1e3c91bba26007dafd982aa923af2938a, 20261113-040949-b7f7:
  CP-7 hosted witness 16.945s, CP-9 ordinary witness 54.889s, CP-8 27/0/0,
  CP-10 **57/0/0**. CP-10's plan Min=2 is a floor and its expected count was
  provisional; the full class was selected and all 57 executed. It was not
  narrowed to two cases. All four rows have source dirty=0, stable clean source,
  source/build binding verified as applicable, and pass the source validator.

Final CP-8 isolated build wall is 719.042s, compiler 315.96s; host startup
16.756s, tests wall 196.679s, teardown 0.964s. CP-10 reuses that build,
startup 8.864s, test wall 3.571s, teardown 0.695s. Final whole run 1473.248s
wall / 1473.243s charged execution / 0.005s admission, all four rows green.
Build/custody cost remains separate from the 322.654s class saving.

The only unlisted builds are the leased native checkpoint-tool bootstrap
(16.14s compiler, 18s held) and the requested scratch native prerequisites.
Both index regenerations use the unchanged native builder/program/taxonomy
inputs and their existing -NoBuild entry point, each with its own lease.
The exact missing no-build row recovery and narrow SDK probe above are explicit
unlisted invocations with reasons. No slot semantics, tool custody, test lane,
assertion, rerun policy or existing timeout is weakened.

## S1 exact guard union

The exact original CP-S1-3 ten-class filter passes **127/127**, zero failed,
zero skipped, with tests wall **337.008s** and checkpoint execution
**367.647s** / wall **367.717s**; whole diagnostic run is 368.840s, zero
admission. This completes within the unchanged **12-minute** execution cap.
The separate incremental native prerequisite compiles in 256.81s (258s lease
held). Its compiler plus diagnostic execution totals 624.457s, also below 12m
in this observed native setup; this is not a cold isolated-build guarantee.

| Class | Passed | Failed | Summed journal seconds |
|---|---:|---:|---:|
| MechanicalPcContractTests | 11 | 0 | 0.654 |
| C1729LayeringGuardTests | 9 | 0 | 0.262 |
| TaskPlatformGuidanceTests | 5 | 0 | 0.423 |
| SlowTestTripwireTests | 2 | 0 | 0.056 |
| ServerGitProcessTests | 23 | 0 | 1.748 |
| NativeLinuxPolicyWitnessTests | 3 | 0 | 12.230 |
| TestClassificationGuardTests | 1 | 0 | 2.471 |
| SlowTestRegistryMergeTests | 5 | 0 | 21.094 |
| DerivedImpactIndexTests | 27 | 0 | 206.895 |
| TestClassificationPolicyTests | 41 | 0 | 127.809 |

The source is S1 b4ab4c34ea0f5592f355d8eb6a0c68eb8421a2d7 plus exactly six
overlaid paths from the tested replay repair: selector, census helper, host,
DerivedImpactIndexTests, its shared fixture, and a locally regenerated S1 index
that preserves S1's own eight index lines. S1's original test source equals the
assigned base before our harness/diagnostic changes; its assertion bodies are
not replaced by different acceptance. Native C# binaries are rebuilt for this
composition. The original builder input code is unchanged. Source and binary
hashes are verified unchanged after execution.

This is an explicit **dirty diagnostic composition**, not ordinary clean S1
credit: dirty=6, fingerprint
51a629a2c9f00f44832869eadc9abe58251d2a5728b2ff5a9b5bcc9390f089e6,
buildSource=notApplicable for the command envelope. Overlay SHA256:
b2fd685555c02d399b01c8561380d1fbe1482e1826086ce564621ffa9c7cd4c4.
The native binary/input hashes and exact overlay are in ignored
.antiphon/c1862/s1-hosted-composition.json and s1-hosted-overlay.patch. The
original ten-class filter is unedited in the checkpoint line below; counts come
from its own s1.trx, not the command row's n/a TUnit counters. Per-test journals
are under /tmp/card1862-s1-hosted-phase with explicit run/group/attempt/suite
metadata.

Before hosted reuse, the b4+fec composition still exhausts 12m without TRX
(run 20261113-033021-bc9b), so it is not called unblocked. An earlier S1
invocation accidentally passed bare 12 (parsed as seconds/rounded to 1m);
it is stopped through the supported owner command while queued, exit 130,
20261113-032743-7198. It earns no test credit. The correction uses explicit
12m, restoring the intended existing budget; it does not widen S1's budget.
A wrong-relative-path preparation attempt makes no source changes and only
executes an unchanged 62s index regeneration; the corrected absolute-path
preparation regenerates the new fixture edge in 43s. Both logs remain retained.

**Answer for containment:** yes, its exact filter now completes within the
existing budget with this repair applied. Integrate the reviewed repair into S1
and run clean ordinary CP-S1-3 before claiming its gate. Cold compilation and
source/output custody remain separate costs and can still consume a whole-run
budget on a loaded host. The historical S1 failures remain incomplete.
Scratch source/index are restored to b4 and git status is clean after the run;
the diagnostic native outputs/evidence are retained and are not b4 build credit.

```text
CHECKPOINT CP-S1-3 commit=b4ab4c34ea0f5592f355d8eb6a0c68eb8421a2d7 build=n/a filter=dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter "/*/*/(ServerGitProcessTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*" --report-trx --report-trx-filename s1.trx --results-directory .antiphon/c1862-s1-results executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=6 source=b4ab4c34ea0f5592f355d8eb6a0c68eb8421a2d7+dirty:51a629a2c9f00f44832869eadc9abe58251d2a5728b2ff5a9b5bcc9390f089e6 sourceState=dirty buildSource=notApplicable
```

S1 TRX SHA256: e1f5e7587401beaee84206dead4798ea9520d609423464b884b7cca9eb066c21.
S1 report SHA256: d5a7c00e82de8766941e232e0d279a3671c22e1f43eee933ee20a68c7592c584.


## Retained evidence

Generated reports/TRX/logs stay ignored under /work/worktrees/task-371692c7/.antiphon/checkpoints/
and .antiphon/c1862/. The scratch evidence stays under
/tmp/card1862-s1-b4ab4c34/.antiphon/, with its composition hashes and overlay
copied into the assigned evidence root. Generated payloads are not committed.
Historical outcomes/costs are preserved; only this narrative is committed.

### Unedited checkpoint lines

```text
CHECKPOINT CP-0 commit=a7192898cc5a2b1c755b46cf336ddc2c03c43dda build=n/a filter=dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:1 --nologo executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=30s dirty=0 source=a7192898cc5a2b1c755b46cf336ddc2c03c43dda sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=a7192898cc5a2b1c755b46cf336ddc2c03c43dda build=ok filter=/*/*/DerivedImpactIndexTests/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-371692c7/.antiphon/checkpoints/20261113-013929-4c52/rows/CP-1/run.trx slot=granted waited=120s dirty=0 source=a7192898cc5a2b1c755b46cf336ddc2c03c43dda sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=4c5f486cfd1181684d27738af6be6b0b60ab5286 build=n/a filter=pwsh -NoProfile -File scripts/test-selector-census.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=4c5f486cfd1181684d27738af6be6b0b60ab5286 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=0b3014168f06ac15187c3436e4bb3dee70fa564e build=n/a filter=pwsh -NoProfile -File scripts/test-selector-census.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=0b3014168f06ac15187c3436e4bb3dee70fa564e sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=674bf6c76861676bae2eab08c285ea2b387c4a98 build=n/a filter=pwsh -NoProfile -File scripts/test-selector-census.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=45s dirty=0 source=674bf6c76861676bae2eab08c285ea2b387c4a98 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-4 commit=674bf6c76861676bae2eab08c285ea2b387c4a98 build=ok filter=/*/*/DerivedImpactIndexTests/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-371692c7/.antiphon/checkpoints/20261113-021048-9211/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=674bf6c76861676bae2eab08c285ea2b387c4a98 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=fec8773b7abd5628554297ab2f4962986fe2cd8a build=n/a filter=pwsh -NoProfile -File scripts/test-selector-census.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=15s dirty=0 source=fec8773b7abd5628554297ab2f4962986fe2cd8a sourceState=clean buildSource=notApplicable
CHECKPOINT CP-6 commit=fec8773b7abd5628554297ab2f4962986fe2cd8a build=n/a filter=/*/*/DerivedImpactIndexTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=30s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-6 commit=fec8773b7abd5628554297ab2f4962986fe2cd8a build=reused filter=/*/*/DerivedImpactIndexTests/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-371692c7/.antiphon/checkpoints/CP-6-20261113-031410-4ee0/run.trx slot=granted waited=0s dirty=0 source=fec8773b7abd5628554297ab2f4962986fe2cd8a sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=fec8773b7abd5628554297ab2f4962986fe2cd8a build=n/a filter=pwsh -NoProfile -File scripts/test-selector-census.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=fec8773b7abd5628554297ab2f4962986fe2cd8a sourceState=clean buildSource=notApplicable
CHECKPOINT CP-7 commit=b9350ea1e3c91bba26007dafd982aa923af2938a build=n/a filter=pwsh -NoProfile -File scripts/test-selector-census.ps1 -Hosted executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=b9350ea1e3c91bba26007dafd982aa923af2938a sourceState=clean buildSource=notApplicable
CHECKPOINT CP-9 commit=b9350ea1e3c91bba26007dafd982aa923af2938a build=n/a filter=pwsh -NoProfile -File scripts/test-selector-census.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=b9350ea1e3c91bba26007dafd982aa923af2938a sourceState=clean buildSource=notApplicable
CHECKPOINT CP-8 commit=b9350ea1e3c91bba26007dafd982aa923af2938a build=ok filter=/*/*/DerivedImpactIndexTests/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/task-371692c7/.antiphon/checkpoints/20261113-040949-b7f7/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=b9350ea1e3c91bba26007dafd982aa923af2938a sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=b9350ea1e3c91bba26007dafd982aa923af2938a build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=57 passed=57 failed=0 skipped=0 trx=/work/worktrees/task-371692c7/.antiphon/checkpoints/20261113-040949-b7f7/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=b9350ea1e3c91bba26007dafd982aa923af2938a sourceState=clean buildSource=verified
```

| Artifact | SHA256 |
|---|---|
| .antiphon/checkpoints/20261113-013929-4c52/report.json | 95a04ef1234fe25ff4b79ec0400170b11fcc69af6faec5b92a1891f583c66126 |
| .antiphon/checkpoints/20261113-020839-bb01/report.json | e365938ba600976f6ef7824cb25eda188f5f99bfa98877d74185ecacfeb6381b |
| .antiphon/checkpoints/20261113-020936-32e4/report.json | 4fcdffb20ef85ba99396602b393849a79469b53d8a108e0d6d4fd8a6198c96e5 |
| .antiphon/checkpoints/20261113-021048-9211/report.json | 75b33fc7be566a13b7d464e9735f7b9c3057d54d2a151876519ee008d2beb975 |
| .antiphon/checkpoints/20261113-023901-3eda/report.json | 048ff8526757dcb172f9b9d5dcfac44e108ae7113452f20a5152af6216e668b3 |
| .antiphon/checkpoints/CP-6-20261113-031410-4ee0/report.json | 8317e3920eb02383ab7c679aa1ef2b3b43236c06a0275555d4ab27321a531673 |
| .antiphon/checkpoints/20261113-032145-b604/report.json | ccd1a661e98dd07a76011b44b4537a36c90232a3df16d983a8e1278ea6534687 |
| .antiphon/checkpoints/20261113-040949-b7f7/report.json | 356f8f452592608b81291381c04577f4971139cb5d325c37dd0917f49d100420 |
| final CP-8 TRX | c062974979a832d7c77f2b78b0167f7f0754687ba278de4779d460f224781ec6 |

Final publication SHA and full-range evidence-diff/merge-tree checks are in the
task completion report. Initial ordinary Review is required. Work proceeded
under the caller's standing authority; no additional approval was needed. No
live run, host setting, session, Docker object, volume, user Codex home or build-
slot policy was changed. All owned commands and test hosts are joined.
