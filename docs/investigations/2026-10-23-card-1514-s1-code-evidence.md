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
