# CARD-1514 S3 implementation and baseline evidence

Code/landing owner: `2c5c38b8-548d-40ee-8948-6bb8557b1b5a`.
Branch: `feat/card-task-2c5c38b8`.
Worktree: `/work/worktrees/task-2c5c38b8`.
Base: `9a9fee9ab34385765bea5e84bb7471fb12891865`.
Test-first commit: `2ce5afede6ec3a5d51f73e8ad928ab873cebb708`.
Implementation commit: `b1513df1f6c98738a334f6cef1b7629489c94ebb`.

Only S3 is implemented. The 41 Git-only checkpoint cases use the existing bounded
Git lane; both real worker-death cases moved intact to a strict Integration/Slow
class. Their child selector follows the new class. No product behavior, limiter
value, timeout, assertion or worker mode changed. Two offline budget wrappers and
one compiled-type topology guard supplement the existing 20 limiter cases.

[Verification design and executable S3 manifest](../superpowers/plans/2026-10-23-card-1514-s3-code-verification.md).
The caller explicitly commissioned only S3 and prohibited broader Unit/assembly
runs. V-1/V-2/V-4/V-5 and CP-2..5 remain unexecuted for later slices; they are not
passed. S1 then S2 remain Code work after this slice's ordinary Review.

## Source and equivalence checks

A source ledger compared the base with candidate: all four Matrix bodies are
byte-identical, and the entire worker test block (including false/true arguments,
kill/join, drains, parent-owned DB and recovery assertions) differs only in its
child class selector. Counts remain 1+11+11+18+2 = 43. The original worker block
SHA-256 is `fd0427286a3c554ce2faab746e08b034a96d7a85a8b69fe90c323aa5606367f8`.

The Git-only audit covered the sole Matrix file, LandingSafetyHarness's executed
paths, LandingGitFixture, LandingGit and the injected ControlledVerifier. Each
case owns repository/source/remote/observer/HOME/task/DB state. The verifier does
not start a process; real Git starts are fixture-owned, including input-batch
commands. The fixture's index-lock census is scoped to its own children. No
DirectoryLink/ScriptHarness or Windows-only child path is reached by these four
methods. Worker modes and owner sentinels remain unchanged; existing forbidden-
helper and limiter roster assertions remain. The worker and budget wrapper join
the strict roster, Matrix joins the Git roster, and Worker joins the Slow allowlist.

The historical C1404 and C1410 fixtures are untouched. The new C1514 projection
maps all 158 historical identities one-to-one (41+2+52+63); C1404 runs the real
packer and tests eight isolated launches, complete coverage, unchanged Publication
splitting and the 3600000 ms watchdog. Each split class retains all 1560 historical
seconds with denominators 41/2, basis pace and explicit split provenance. This is
a conservative allocation, not two measured Windows totals. C1410's calibration
body remains byte-identical; its ordinary result is part of CP-1.

## Baseline and red witness

Run `20261023-053912-af7c`, clean source/build verified at the test-first SHA.
One actual isolated test build (the report also lists the unused candidate build
entry). Counts: 66 executed, 65 passed, exactly one intended failure, zero skips.
All 43 unchanged landing cases passed. R-3 failed at the missing Matrix Git limiter
attribute, with the message “the 41 Git-only cases must use the existing Git lane”.
This is a real compiled attribute observation, not a timing threshold or constant.

```text
CHECKPOINT CP-0 commit=2ce5afede6ec3a5d51f73e8ad928ab873cebb708 build=ok filter=/*/*/(AgentTaskLandCheckpointMatrixTests*)|(AgentTaskLandCheckpointWorkerTests*)|(ProcessSpawnLimitTests*)|(C1514LandingSplitBudgetTests*)/* executed=66 passed=65 failed=1 skipped=0 trx=/work/worktrees/task-2c5c38b8/.antiphon/checkpoints/20261023-053912-af7c/rows/CP-0/run.trx slot=granted waited=0s dirty=0 source=2ce5afede6ec3a5d51f73e8ad928ab873cebb708 sourceState=clean buildSource=verified
PHASES CP-0 slotWait=0s build=211.6127715s startup=46.788629s testsWall=141.11426s teardown=1.6971855s hostWall=189.6000589s
```

Server2 container `0c18bb7f591e`, Debian 12, .NET 9.0.21, 24 processors. Routes
runner-defaults revision 2 and session-runners confirmed eligible server2 before
execution. Baseline Git override=1; candidate clears it, resolving the unchanged
default to 4. Deep landing capture is off on both sides. Shared-host interference
is not controlled. No Windows operation was performed.

Baseline 43-case wall span: 111.2094715 s; interval union/sum: 111.1759577 s.
41 Git cases: 80.2204553 s union, maximum overlap 1. Two workers: 30.9555024 s
union, maximum overlap 1. Full selected host: 189.6000589 s, startup 46.788629 s,
selected test window 141.11426 s, teardown 1.6971855 s. Build 211.6127715 s;
build and row slot waits 0. No before/after speedup is claimed in this pre-run
source report. Fresh candidate results must be appended to the external report
at the final tested SHA, leaving this committed source unchanged.

DB clone wait/seed split, cache hit identity, per-fixture Git command counts and
child-process concurrency are unavailable in this low-overhead pair. TRX overlap
measures test concurrency, not child concurrency. This limits phase attribution;
the actual selected-case wall/count comparison is the performance evidence.

## Evidence and remaining qualification

External evidence root: `/work/test-evidence/card-1514-2c5c38b8/`.
Checkpoint receipts: `.antiphon/checkpoints/20261023-053912-af7c/`.
Final generated report: `.antiphon/task-2c5c38b8.md`; the final caller report binds
its candidate receipt and SHA. Raw TRX, receipts and JSON remain ignored.

The task-owned offline adapter `profile-checkpoint.ps1` consumes one existing
checkpoint report/TRX, verifies its expected SHA, reports every method/count and
computes mapped 43-case intervals. It starts no test host. SHA-256:
`67e4ca0ef17041aca1539bc8f6922108418b249ee520312c1473d32f93cb5bfb`.
The historical parser adapter SHA-256 is
`9d905e5f5f4e3740364f4682dbc319449ca168a1a5eadba537e874e6346dcd32`;
it reproduced Persistence 52 passes and Remote 29 passes plus 18 cancellation
errors (censored), preserving the historical files. Original profile/parser hashes
match the landed plan. These historical timings are not the matched baseline.

One necessary unlisted prerequisite build bootstrapped the checkpoint tool outside
the checkout under build-slot.ps1: slot=granted waited=0s, held=6s, 0 errors.
The checkpoint tool leases test builds/rows itself. No suite or Unit-lane run.
Initial monitoring waits mistakenly supplied a relative directory where a run ID
was expected; they ran no build/tests. The corrected wait for the exact run ID
returned the recorded exit 1. Baseline outputs were inventoried (2653 entries)
and removed after executor exit before the candidate run, avoiding CARD-1532.

RC scratch trial at `5a126f6fc5ae4e16d803b628716b8f0485a37174` did **not**
cherry-pick cleanly: the test-first commit conflicts in ProcessSpawnLimitTests.cs,
and that RC has no GitProcessSpawnLimit.cs. The scratch operation was aborted and
the scratch worktree removed. S3 is test/test-selection-only but requires the
CARD-1421 Git-lane prerequisites before an RC port. Potential RC benefit: parallel
41-case checkpoint work and a separately budgeted two-case worker launch; no
numerical Windows saving or live qualification result is claimed.

Merge-tree against fetched master `b6615e643e570be7af25ec946153b52f03d3ee79`
was conflict-free before this report, tree `f1b3c39cb070c4a1a5efe9e93618f914f1008048`.
Evidence-diff over full base..implementation: 2 commits, 0 entries, 0 violations.
Repeat both checks after final commit. Commit this report before CP-1; the final
pushed tip must be the tested source. Validate its receipt and inspect fresh TRX.

PC-3a (worker lane), PC-3b (mapping), PC-3c (missing cost), PC-3d (Matrix lane)
remain pending for method-scoped SourceLanding Mutation after ordinary Review and
landing by the original Code owner. No PC is discharged by ordinary green.
Unbound candidate: the verification design above; no bound mutation pack yet.
Restart: none. Next: Review when V-3/R-3 and manual checks are complete.
