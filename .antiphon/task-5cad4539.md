# CARD-1375 Code report: S1

S1 implemented and verified. S2 was not started: the brief permits stopping at a slice boundary, and S2's planned 45-60 minutes would exceed the remaining 60-minute dispatch budget. No live Windmill job, deployment, landing or restart occurred. Restart: none.

## Source and custody

- Original Code task / landing owner: 5cad4539-72f7-40d2-8780-34c0eac9f89f.
- Branch: feat/card-task-5cad4539.
- Worktree: /work/worktrees/task-5cad4539. Assigned desktop mirror: C:\Antiphon\worktrees\card-task-5cad4539 (not used).
- Task base and plan commit: 56ea9c9959b42d2dc99bdfaebebd2d4af76f6950.
- Plan: docs/superpowers/plans/2026-10-20-card-1375-real-timeout-cancel-captures-plan.md.
- Tested implementation SHA: 7e341290b00a5ad0dae69ed97bedb9c4fca64c46. The subsequent commit adds only this report; its full SHA is in the caller handoff.
- All implementation slices were committed and pushed to the assigned ref, without rebase, reset, amend or branch switching.

Commits:

1. eff6639ac191a555f76bdaf9e404a5eb52134132: bounded local capture tooling and pinned native probe.
2. 41fe7c568cd256ff4b6a1a87fe19eabb5b203784: repair the capture validator's native summary-counter assumption; distinguish producer build SHA from capture-driver SHA.
3. 6864be3183dabf446ec66d1665bab7899f861ba3: real selected bytes, provenance and failing ten-row regression, before the parser fix.
4. 7e341290b00a5ad0dae69ed97bedb9c4fca64c46: bounded parser fix and historical evidence-limit documentation.

## Deliverables

- scripts/fixtures/nightly/c1375-probe/: standalone net9.0 TUnit 1.44.0 / MTP 2.2.2 producer, isolated from repository build props/targets.
- scripts/lib/nightly-probe-supervisor.ps1 and scripts/capture-nightly-probe.ps1: literal argv, immediate stream drains, Linux PID/start identities, observed descendant handles, optional injected execution elapsed provider, real 30,000 ms default execution deadline, and one separate real 5,000 ms cleanup allowance including kills, joins and EOF drains. Failed cleanup refuses subsequent probes.
- scripts/fixtures/nightly/c1375-diagnostic-records.ps1: exact base64-selected headers, terminal records and minimal native TRX sections. The harness adds an XML root around unmodified Results and TestDefinitions sections.
- scripts/fixtures/nightly/c1375-provenance.md: literal probe argv, package/assembly versions, source/build SHAs, native exits/counts, hashes and byte offsets/lengths. No state reconstruction, path replacement, reflection or newline substitution.
- scripts/lib/nightly-coverage.ps1: admit native empty Timeout values and empty exception fields for Timeout/Cancelled; preserve unique-state/unique-delimiter checks, populated durations and existing null/exception compatibility. An empty explanation supplies no Message.
- scripts/test-nightly-tests.ps1 and tests/Antiphon.Tests/Scripts/NightlyExecutionDiagnosticTests.cs: D13_RealTimeoutCancelled with ten named rows and independent literal semantic expectations.
- docs/testing-and-build.md: preserve the old CARD-1325 Error outcomes and separately identify these real captures and their trigger limitations.

## P-1 native observations

P-1 passed. The real elapsed Timeout used --detailed-stacktrace; the Cancelled producer performed local CTS cancellation, caught it, selected Cancelled through the supported public result override, and returned normally. This does not establish automatic classification of external cancellation.

Accepted raw evidence: .antiphon/c1375-evidence/producer-validated/{TimeoutProbe,CancelledProbe}/.
Each invocation produced one matching native terminal node and one Failed UnitTestResult, zero passed/skipped results, exit 2, no supervisor expiry, successful root join and both EOFs. No descendants were observed. This is normal-completion evidence, not an expiry/kill witness.

Timeout duration was 156.8098 ms, above its 100 ms test timeout. Its native TRX summary is total=1, executed=0, failed=0, timeout=1 despite the Failed result row. Cancelled's summary is total=1, executed=1, failed=1, timeout=0. Its native explanation and exception are empty; the override reason was not invented as Message.

The first Timeout invocation also produced the correct state and Failed result, but our initial capture validator incorrectly required summary failed=1. That introduced validator defect was fixed without changing triggers or the required Failed result. One capture rerun obtained both accepted records; initial evidence remains under .antiphon/c1375-evidence/producer/. P-1 stayed inside its ten-minute allowance.

Explicit non-checkpoint build/observation exceptions:

| Invocation | Source | Outcome | Slot/wait |
|---|---|---|---|
| P-1 probe build | eff6639ac191a555f76bdaf9e404a5eb52134132 | 0 errors, 0 warnings; held 8s | granted, waited=0s |
| P-1 initial capture | eff6639ac191a555f76bdaf9e404a5eb52134132 | validator refusal after correct Timeout; held 4s | granted, waited=0s |
| P-1 capture correction rerun | driver 41fe7c568cd256ff4b6a1a87fe19eabb5b203784; probe build eff6639ac191a555f76bdaf9e404a5eb52134132 | both requested states accepted; held 7s | granted, waited=0s |
| Allowed checkpoint-tool setup build | 41fe7c568cd256ff4b6a1a87fe19eabb5b203784 | 0 errors, 4 compiler warnings; held 13s | granted, waited=0s |

The tool was subsequently invoked using dotnet exec, with no implicit build. The exact producer commands, including the capture driver's explicit -ProbeBuildSourceSha argument, are in the adjacent provenance file. No other test selection or full assembly run was made. The custom brief explicitly restricts testing to each slice's planned red/green rows plus these setup/producer exceptions; the whole Unit lane was not run and is not claimed passed.

## Verification outcomes

- V-1: PASSED. CP-1 proved the guarded defect: exactly one native test failed solely on D13 real Timeout exact message; 9 harness rows passed and 1 failed. CP-2 then passed the new ten-row test, twice including the permitted receipt rerun.
- R-1: PASSED. All six pre-existing D13 methods and their 72 assertion rows remained unchanged and passed. Each CP-2 run executed all seven methods, enforcing 82 total harness rows, with no failed/skipped tests.
- V-2: NOT RUN. S2's native process-tree deadline/expiry witness remains deferred.
- CP-3 / CP-4: NOT RUN. No kill-suppression control or expiry acceptance cycle was performed.
- PCs: no PC-n IDs are enumerated in this plan. SourceLanding Mutation and its missing-control discovery remain pending after Review and landing; no PC was discharged. The planned S2 kill-suppression variant remains unperformed.

The first CP-2 had clean source, verified build provenance and seven passing results, but terminal exit 2 and receipt validation cleanup_refused (still-present). Its output directory was already absent when inspected. The retained CP-1 output was removed, and CP-2 was rerun exactly once at the same committed SHA, as the brief allows. The rerun ended exit 0, deleted its owned output, and passed receipt validation:

CHECKPOINT SOURCE VALID source=7e341290b00a5ad0dae69ed97bedb9c4fca64c46 rows=1

Fresh TRX was inspected for every intended method and nonzero counters on all three checkpoint invocations. Every process was awaited to terminal completion, including all exit-75 waits. No timeout or existing assertion was widened; no additional proof repetition followed qualified green.

## Unedited checkpoint lines and costs

### CP-1 intended red

Evidence: .antiphon/checkpoints/20261020-045401-eef6/report.json and rows/CP-1/run.trx.

```text
CHECKPOINT CP-1 commit=6864be3183dabf446ec66d1665bab7899f861ba3 build=ok filter=/*/*/NightlyExecutionDiagnosticTests*/D13_RealTimeoutCancelled* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-5cad4539/.antiphon/checkpoints/20261020-045401-eef6/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=6864be3183dabf446ec66d1665bab7899f861ba3 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=304.7051138s startup=5.1018907s testsWall=2.3720321s teardown=0.7100655s hostWall=8.183986s
wall: 5m43s  sequential-equivalent: 0m08s  builds: 4  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
verdict: RED exit=1
```

### CP-2 first run: tests passed, receipt refused

Evidence: .antiphon/checkpoints/20261020-050032-fde1/report.json and rows/CP-2/run.trx.

```text
CHECKPOINT CP-2 commit=7e341290b00a5ad0dae69ed97bedb9c4fca64c46 build=ok filter=/*/*/NightlyExecutionDiagnosticTests*/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-5cad4539/.antiphon/checkpoints/20261020-050032-fde1/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=7e341290b00a5ad0dae69ed97bedb9c4fca64c46 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=229.4301264s startup=9.1045125s testsWall=21.5608122s teardown=0.6320996s hostWall=31.2976224s
wall: 4m46s  sequential-equivalent: 0m31s  builds: 4  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
verdict: RED exit=2 reason=refused: still-present
```

### CP-2 sole rerun: qualified green

Evidence: .antiphon/checkpoints/20261020-050727-ad1a/report.json and rows/CP-2/run.trx.

```text
CHECKPOINT CP-2 commit=7e341290b00a5ad0dae69ed97bedb9c4fca64c46 build=ok filter=/*/*/NightlyExecutionDiagnosticTests*/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-5cad4539/.antiphon/checkpoints/20261020-050727-ad1a/rows/CP-2/run.trx slot=granted waited=45s dirty=0 source=7e341290b00a5ad0dae69ed97bedb9c4fca64c46 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=45s build=408.2032172s startup=13.9423121s testsWall=22.1752823s teardown=1.0598403s hostWall=37.1775805s
wall: 8m35s  sequential-equivalent: 1m23s  builds: 4  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
verdict: GREEN exit=0
```

## Cleanup and handoff

All task-owned alternate outputs were removed after their processes ended: the probe bin-c1375-probe directory, the checkpoint tool and HostCleanup bin-c1375-tool directories, and both test bin-c1375-s1-* roots. Raw diagnostics/TRX/receipts remain ignored; only this individual Markdown report is additionally committed.

GET /api/runner-defaults and GET /api/session-runners were re-read; revision 2 and an available eligible Linux runner were observed. No fleet address or host pin was embedded. The existing source merge check against origin/master 6ec71d427574f3a1b74bfbe2042e0ea9a819d191 was conflict-free (merge-tree object 299dac20e5055ded40103dd09fee3e82e2f34157). Final full-history evidence-diff and publication checks, including this report-only commit, are reported in the caller handoff.

Next: ordinary Review of S1. Review must preserve the first CP-2 invalid outcome alongside the qualified rerun. S2 / V-2 / CP-3 / CP-4 remain deferred to later Code and Final verification, not passed. After Review, the caller lands the original Code owner to master and commissions SourceLanding Mutation. No activation is requested.
