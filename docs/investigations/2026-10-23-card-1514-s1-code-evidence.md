# CARD-1514 S1: independently report all resume scenarios

Original Code / landing owner: `f35fcce8-eb5a-4ecb-ad7d-3ba0217c8852`.
Branch: `feat/card-task-f35fcce8`; worktree: `/work/worktrees/task-f35fcce8`.
Base: `77eb7806d35b3b522d46e3942742e4864c688de3` (origin/master at start).
Baseline: `58ca64e7716e20bea88e9739d3e81ef5736d7b53`.
Implementation: `72c5bfa64fb3dfe3879be062de8d9da968d3d39a`.

Only S1 is implemented: one method becomes four methods with 1/5/8/2 cases.
This exposes a reporting boundary; it removes no body work and claims no speed-up.
S2 remains separate Code work after ordinary S1 Review/landing. No product,
C1374 helper, class category, timeout, limiter, cost calibration or assertion changes.

[Executable S1 verification design](../superpowers/plans/2026-10-23-card-1514-s1-code-verification.md).
The brief expressly prohibits a Unit-lane/full-class/assembly run; the closed
logical CP-2 is partitioned into CP-20..23 with one shared candidate build.
V-1 is required here; no ordinary R-1 is defined for the mechanical extraction.
V-2/CP-3 are S2; V-3 belongs to landed S3; V-4/5 and CP-4/5 remain deferred,
not passed by this task. The new methods preserve existing fault-sensitive
assertions; no timing assertion, constant oracle or self-comparison test is added.

## Scenario, action and assertion ledger

The original method block SHA-256 is
`8d5537c448ae6ef3c89a2a5083fb4283c17d342cc27e56830fd7b1c4be9bc8b2`.
The external `source-ledger.json` records every assertion expression and the
whitespace-normalized SHA-256 of each retained block. Every normalized block
is present in the extraction; prefix and suffix outside this method are unchanged.
The only added control flow dispatches the special partial case and returns from
it. All original loops become argument vectors; each case owns its fixture.

| Original scenario(s) | New method suffix | Retained setup/actions and outcome assertions | Cases / script calls / dynamic assertions |
|---|---|---|---|
| Seed failure | after_seed_failure | private fixture; injected seed failure; exit 2; parse durable journal and require recreating; ReloadDocker; resume; exit 0; exactly three removals | 1 / 2 / 4 |
| Standalone rm-second-failed | after_partial_failure(rm-second-failed) | private fault fixture; first run; exact first-removal array; external journal exists; read persisted Docker JSON, clear and deep-copy it; clear fault; resume; exactly three removals and exit 0 | 1 / 2 / 4 |
| stop-failed, owned-rm-failed, rm-first-failed, rm-third-failed | after_partial_failure(other four arguments) | private fixture/fault; interruption exit 2; reload/clear; first/third preserve null runnerSessions and unavailable/ineligible status; resume exit 0 and three removals | 4 / 8 / 12 |
| sourceSha, project, context, dryRun, composeDigest, store, generation, stop-receipt | rejects_receipt_drift | private partial-removal fixture; first exit 2; reload/clear; parse journal; exact original per-field mutation (including generation timestamp, foreign store, empty stop receipts and unavailable status); persist journal; resume exit 2; unchanged exact first-removal array | 8 / 16 / 24 |
| foreign=false/true | accepts_only_recorded_replacement | private fixture; clone three replacement volumes/timestamps; write recreated volumes/container; real injected build mutates state, records recreation then fails; exit 2; reload; optional foreign container ID; resume exit 0/2; exactly three removals | 2 / 4 / 6 |

Totals: 16 fixtures, 32 awaited real script calls and 50 unchanged dynamic outcome
assertions. Each of the four methods retains native-Linux prerequisite checking
and `ParallelLimiter<ProcessSpawnLimit>`. No optional status is dereferenced before
the same original guard. Class/category names are unchanged, so no new slow-test
allowlist entry is needed. The historical pace row remains 499 nodes / 4591 seconds.

## Complete matched baseline

Run `20261023-090734-b0c7`, clean source and verified build provenance. CP-0 passed
one reporting node containing all sixteen scenarios; CP-10 passed the exact C1374
host method containing 32 transport vectors. Both fresh TRX identities/counts were
inspected and the full report passed validate-checkpoint-receipt.ps1.

```text
CHECKPOINT CP-0 commit=58ca64e7716e20bea88e9739d3e81ef5736d7b53 build=ok filter=/*/*/RemoteScriptContractTests/C1008_Recycle_resume_requires_matching_receipt executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f35fcce8/.antiphon/checkpoints/20261023-090734-b0c7/rows/CP-0/run.trx slot=granted waited=0s dirty=0 source=58ca64e7716e20bea88e9739d3e81ef5736d7b53 sourceState=clean buildSource=verified
PHASES CP-0 slotWait=0s build=331.5936244s startup=7.6169875s testsWall=383.7210761s teardown=1.1070124s hostWall=392.4453407s
CHECKPOINT CP-10 commit=58ca64e7716e20bea88e9739d3e81ef5736d7b53 build=reused filter=/*/*/RemoteScriptContractTests/C1374_Host_census_recovers_transient_reads executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f35fcce8/.antiphon/checkpoints/20261023-090734-b0c7/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=58ca64e7716e20bea88e9739d3e81ef5736d7b53 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=3.0094342s testsWall=58.164868s teardown=0.6783391s hostWall=61.8526414s
```

Baseline resume test wall/span/union/sum: 383.7210761 s; all report one completed
node, 0 failures/skips. Baseline driver host: 392.4453407 s, build separate
331.5936244 s. C1374 test: 58.164868 s; host: 61.8526414 s. No speed-up inferred
from historical censored measurements. Discovery-only listing found 19701 assembly
nodes and 499 Remote nodes mapped by the source method names; helper names with
zero discovered nodes are excluded. Candidate discovery must find 514 Remote nodes.

Same server2 container `0c18bb7f591e`, Debian 12, SDK 10.0.402, runtime 9.0.21,
24 cores. runner-defaults revision 2 and session-runners were read/saved; server2
was available and eligible. No fleet pin or Windows action. Both sides use the
same strict limiter, private cold fixture construction and normal capture mode;
there is no Compose cache. Deep landing capture is unused. Materialization, Git,
child-process counts and individual phase times are not instrumented; they remain
unknown. The offline adapter consumes TRX durations and tool monotonic phase
receipts; cross-host/overlapping sums are never treated as elapsed time.

## Adapter, execution cost and evidence custody

External root: `/work/test-evidence/card-1514-f35fcce8/`.
Adapter `profile-checkpoint.ps1`, SHA-256
`920a144eee6c0f93b509034a3add8bb0637a23fdbd67ea835874c5a65c5295b0`, accepts
`-Report <report.json> -ExpectedSha <actual tested SHA> -Output <fresh JSON>`.
It launches no tests, records source/receipt/TRX hashes and reports every method's
counts, wall span, union, summed duration, median/p95, build and driver timings.
The original profile/parser hashes match the landed plan; the multiline adapter
hash is `9d905e5f5f4e3740364f4682dbc319449ca168a1a5eadba537e874e6346dcd32`.
Historical reparse reproduced Persistence 52 pass and Remote 29 pass / 18 deadline
errors, without changing those artifacts or treating cancellations as assertion red.

One necessary unlisted tool-bootstrap build: slot=granted waited=0s, held=8s,
zero errors, external output. Two planned leased discovery-only invocations (before
and after) execute no tests. Initial alphanumeric manifest was refused before
build/test launch; numeric IDs corrected it. No extra profiling test pass, Unit
lane or full-class run. All actual test builds/rows are checkpoint-leased.
Baseline output inventory recorded 2652 entries and no live users; it was removed
before candidate execution. Preserve ignored generated evidence; no payloads added
to Git. Candidate results are appended externally after this report commit so the
final pushed source tip remains exactly the tested SHA. No repeat after green.

## RC and manual checks

Scratch cherry-pick of all three task commits through implementation onto
`origin/rc/2026-10-20` at `5a126f6fc5ae4e16d803b628716b8f0485a37174` succeeded,
scratch tip `7dfbbd5f44ac8f7da9bb3b5511d9c7f37d470426`. S1 is test-only plus
verification docs and needs none of the CARD-1421 Git-lane prerequisites absent
from RC. This is a source compatibility trial, not RC execution or publication.
No startup path changes. Scratch removal follows the final report-only pick.

Merge-tree against fetched master `523a8098b560c8090be1c1eaf167e954810b1609`
is conflict-free, tree `d85b2136a249c84fb3abfc2ed1d2d0cc51aa0590`. Full
base..implementation evidence-diff: 3 commits, 0 entries, 0 violations. Repeat
both at final HEAD and record externally. Final external report:
`/work/worktrees/task-f35fcce8/.antiphon/task-f35fcce8.md`.

S1 defines zero PCs. Mutation owns missing-control discovery after ordinary Review
and landing. [Unbound inventory](../superpowers/mutations/2026-10-23-card-1514-s1-candidate.md).
S3 PC-3a/b/c/d remain separate, not discharged here. Restart: none. Next: Review
when candidate V-1 and manual checks are complete; S2 then returns to Code.

## Dependent selector repair and inherited failure attribution

The first candidate run 20261023-092445-474e at 10eba74ed was explicitly
stopped during its build, before any test host launched. The dependency audit
found the old identity in C1008PlatformContractTests and WindowsUnitSkipRoster.
This omission was introduced by S1 and is repaired in this slice. The partial
output (275 entries) was inventoried and removed; no green or completed build
time is claimed for that stopped run. Two manifest-only refusals also launched
no tests (alphanumeric build references, then guard rows outside the table).

CP-11 captured the existing C1050 guard red on the missing old method, at clean
17a17e280b69fc9b6ee2ef3a68645432d53e2e98. This is an assertion-level witness
for the introduced selector defect. The second selected guard failed first on
C1374CensusProbes.Check. CP-12 then reproduced that exact closure method and
message at task base 77eb7806d35b3b522d46e3942742e4864c688de3, with a separate
clean, verified build. This is confirmed inherited red, recorded under CARD-1544
(6cdf08bc-655c-42fa-af14-98533d8ddde2), not an S1 regression or a passing gate.
The helper and closure assertion remain unchanged. Per the standing minimal-testing
policy it is an explicit diagnostic/non-receipt row, not a silent omitted pass.

```text
CHECKPOINT CP-11 commit=17a17e280b69fc9b6ee2ef3a68645432d53e2e98 build=ok filter=/*/*/(C1008PlatformContractTests*)|(WindowsUnitPlatformAdmissionTests*)/(C1050_Remote_entries_guard_before_work*)|(C1210_Roster_closure_matches_source_gates*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-f35fcce8/.antiphon/checkpoints/20261023-092919-68a5/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=17a17e280b69fc9b6ee2ef3a68645432d53e2e98 sourceState=clean buildSource=verified
PHASES CP-11 slotWait=0s build=248.567164s startup=5.2360114s testsWall=16.2523855s teardown=0.4909646s hostWall=21.9793628s
CHECKPOINT CP-12 commit=77eb7806d35b3b522d46e3942742e4864c688de3 build=ok filter=/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates executed=1 passed=0 failed=1 skipped=0 trx=/work/test-evidence/card-1514-f35fcce8/base-attribution/.antiphon/checkpoints/20261023-093637-f657/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=77eb7806d35b3b522d46e3942742e4864c688de3 sourceState=clean buildSource=verified
PHASES CP-12 slotWait=0s build=322.6763476s startup=4.1322038s testsWall=12.7069575s teardown=0.442499s hostWall=17.2818727s
```

Red build cost: 248.567164 s; host 21.9793628 s (two actual assertion failures).
Base attribution build: 322.6763476 s; host 17.2818727 s (one inherited failure).
Both builds and rows had granted slots, waited=0s. Red receipts remain RED and
are not submitted as valid green acceptance. All identities/counts/messages were
read from fresh TRX/report artifacts. Baseline attribution used only the exact
failing method, never the assembly. These extra costs are retained separately
from the before/after resume comparison.

The repair replaces the old native identity/filter operand with four exact
methods. RemoteHosts explicitly expands the independent literal expected Windows
multiset by 1/5/8/2, and Existing skip-roster entries record those result counts.
No native reason, OS branch, outcome predicate, deadline or audit assertion is
loosened. The native audit still demands an exact identity/outcome/reason multiset,
now 27 native results plus one portable result (previously 12 plus one). CP-24
runs the direct C1050 source guard and its unchanged negative audit controls,
while CP-20..23 still execute every resume case. Required S1 acceptance is these
18 results, with the inherited closure failure explicitly outside the green
receipt. Native Windows child audits remain unexecuted/outside this Linux-only
commission; no changed OS-selected production behavior exists.

The final candidate must run once at the final pushed repair/report SHA; append
its actual outcomes, source validation, timings, refreshed RC trial, merge-tree
and evidence-diff to the ignored full task report. Do not relabel earlier tips
as tested final source. S1 still defines zero PCs; Mutation owns missing controls
for the selector/multiplicity mapping as well as the extraction.

## Settlement: implementation passes, checkpoint gate incomplete

At 7ae930be3d9adb70252e900995cc1a57ab466e52, all 18 intended tests passed,
zero failed/skipped. CP-20/21/22/23 counts 1/5/8/2; CP-24 both C1050 tests passed
but its stale extra Windows class caused roster-miss / exit 3. Full receipt
validation refused report_failed, correctly. The manifest now removes only that
unused class operand. This final correction is committed but untested: the
60-minute budget was exhausted. Verification remains incomplete, next Code,
not Review or land. Preserve all earlier outcomes and costs.

Same-server2 semantic resume cases remain 16 passed on both sides (old reporting
node 1, new nodes 16); 32 script calls and every assertion retained. Test-interval
sum: 383.7210761 s before, 459.3701813 s after. Candidate method wall spans:
seed 34.6801005 s; partial 188.2823059 s; drift 163.2983626 s; replacement
73.2404916 s. Four-host total 486.971651 s versus baseline 392.4453407 s.
Build: 316.5995333 s versus baseline 331.5936244 s. Build-plus-host is slower
by 79.5322192 s. No speed-up is claimed; S1 retains its independent reporting
benefit. Raw per-method median/p95 and CHECKPOINT lines are in ignored evidence.

The versioned analysis-only adapter profile-checkpoint-v2.ps1 corrects even-sized
medians to the arithmetic mean; SHA-256
8ea91a660001189462d139b5e1e1dfc6cdc5fa28cec33aeb65ff10de3bbabad4.
The original adapter and hashes remain retained; no test/capture changed. Both
baseline and candidate were parsed with v2. No new test invocation was hidden.
