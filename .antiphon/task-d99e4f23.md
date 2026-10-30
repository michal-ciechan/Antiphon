# CARD-1514 fix round and portable M3 - d99e4f23

Assigned base: a43e8ed04cc7f2d17d508ed5dd570b319e9b5f1d.
Branch: feat/card-task-d99e4f23. No rebase, merge, amend, force-push, tag, publication or activation.

## Product and witness changes

- D-1: V-6 now starts real second processes for torn record 4 (three surviving records), a sealed predecessor and a source identity change. Each asserts successor generation, seed SHA and projected SHA against the parent's valid prefix, exact three chunk IDs, and unchanged predecessor files. The deliberate empty-seed mutation failed with `successor-seed-lost-valid-prefix torn`; restored V-6 passed all three cases.
- D-2: The summary constructs its TRX maps from reduction entries, including seed rows and retained records. V-10 checks JSON group/suite and CSV group labels for legacy seeds and sealed successors. Its committed red parent failed `seeded-summary-labels-lost legacy`; the repair passed. This changes diagnostic summary joins only.
- D-3: Replaced V-12's isolated authority-file self-comparison with actual `Invoke-AntiphonNightlyTests` outputs: LogRoot summary verdict fields, suite UID census/results, returned verdict fields and original TRX hashes. Three outcomes (success, failed, missing TRX) each run six chunks under no journal, clean journal, gap, torn record, missing state and held writer: 18 qualification fixture runs. Timings and run/root identities are observational and normalized; verdicts, reasons, counts/digests and policy identity remain literal. Promoting the projection to coverage authority failed `publication-changed-real-verdict missing-trx/clean`; restored matrix passed. This base has no separate S4 qualification ledger/receipt writer; the production suite ledger and summary receipt are compared, and CP-5 separately exercises actual release-authority validators.
- A-2 taken: standalone summarizer accepts `-TimingRoot`, including explicit recovery of a separate root. V-10 checks it.
- A-4 taken: V-5 compares recovered projection bytes with the pinned legacy oracle at all four kill barriers.
- A-1 and A-3 remain advisory and deferred: refused initialization reporting/fallback and contending explicit projectors.

Changed product files: scripts/lib/test-phase-evidence.ps1 and scripts/summarize-test-phases.ps1. Test witnesses: tests/Antiphon.Tests/TestHelpers/ManifestPublicationHarness.ps1. No coverage/release-authority/gate implementation or production qualification-loop change.

## Commits

- 469bc5ba89296e4b7dafb074eb51e8f39b272aaf - successor seed/projection witness.
- 6e159cc9dc58e73dddf387c0e4b095599f156f47 - D-2 assertion-red witness.
- c6d3f0a15457d071ca3001310a26466ee2ebec0a - D-2 production repair.
- dc29c0f5c2466ae23929efe9e4dba37af8ca97d9 - real V-12 verdict matrix.
- b8f90c61e5b622efa4100b78e17272b8ab0ea008 - A-2 and A-4.
- f4d99876b6e25d876f6eace1fdee39d7b9c11506 - separate V-1 setup from steady-state counter instrumentation; preserve corpus, assertions and timeout; retain setup/publication measurements.

Each was pushed immediately. Later evidence/M3 commits are listed in the final addendum.

## Verification history and performance

All required checkpoint executions use the real checkpoint tool and host build slots. TEMP=/tmp, UseAppHost=false. The importer accepted 11 rows. Native CP-10/CP-11 were never selected. The ignored imported execution manifests retain all filters/counts and plan timeout pins; fresh output suffixes avoid reusing existing output roots.

1. CP-1 run 20261030-113449-b901 at b8f90c61e5b622efa4100b78e17272b8ab0ea008: build passed (13m01.89s compiler wall; zero errors); the 16-minute selected-group execution budget expired after 97.250s of test execution. Exit 5; no complete row/TRX. The original red receipt is retained.
2. Exact CP-1 recovery using that verified build: CP-1-20261030-115217-e9af, 8 executed / 7 passed / 1 failed / 0 skipped. `NextPublicationTouchesOnlyCurrentChunk` hit its unchanged 180-second test timeout. Source clean, buildSource verified. The other seven witnesses passed.
3. Base attribution probe: real V-1 harness from a git archive of assigned base a43e8ed04cc7f2d17d508ed5dd570b319e9b5f1d, leased, with the same 180-second boundary. Passed in 168s. This does NOT establish an inherited red; timing attribution remains unresolved under host contention (observed load average near 89 on 24 cores). No production hot-path change was inferred from the timeout.
4. f4d99876b6e25d876f6eace1fdee39d7b9c11506 removes recursive measurement wrappers only during separately charged prefix import. Inputs, per-publication instrumentation, assertions and the 180-second test timeout remain unchanged. Leased direct V-1 passed in 156s; samples retained under `.antiphon/d99e4f23/v1-setup-fixed`. All four sizes reported zero prior rows, zero flat writes, one current hash and 749 serialized bytes. These are small, unpaired timing observations, not p95 or a causal speed-up claim.
5. Fresh CP-1 run 20261030-121129-e236 at f4d99876b6e25d876f6eace1fdee39d7b9c11506: build passed (12m07.41s compiler wall; zero errors), then selected-group execution budget expired. Exit 5. Budget wall=1081.261s, execution=961.271s, admissionOnly=119.990s. It remains red.
6. Exact CP-1 row recovery CP-1-20261030-123016-6074: 8/8 passed, zero failed/skipped, source clean and buildSource verified. V-1 was 97.868s; timezone differential 57.276s; successor corruption witness 14.240s; byte recovery 20.146s. This uses the same source/OS/M1 build; no extra build or timeout increase.
7. Attempted overlapping CP-2 preflight 20261030-122530-09eb was refused before compilation or tests: CP-1 was still changing its output inventory outside CP-2's write root. Exit 2, all eight rows executed=0. This was my scheduling error; no source recovery was attempted or needed. The refusal is retained, and CP-2 was restarted only after CP-1 settled.
8. Final CP-2..CP-9 run 20261030-123509-2f13: pending final receipt below. Its isolated build passed in 14m26.35s compiler wall, zero errors.

The inherited coupled build/group budget is demonstrably tight on this host. No timeout was widened, no assertion relaxed, and no red run relabeled green. Exact row recovery retained verified same-source builds. Future performance/packing work should use this timing record under CARD-1514/CARD-1404 rather than blindly repeating the aggregate. The test assembly and its large staged-probe build dominate admission/execution cost; no full-suite run was authorized or performed.

## Required portable rows

CP-1: final 8/8, source f4d99876b6e25d876f6eace1fdee39d7b9c11506, verified build.
CP-2..CP-9: final values and unedited CHECKPOINT lines are appended after completion.

## Definition and land check

No .csproj, .props, .targets, solution, SDK/package, policy or land-graph definition changed.
Real `scripts/audit-land-verification-graph.ps1 -Check -ToolPath .../bin-d99e4f23-land/Release/net10.0/Antiphon.LandGraphAudit.dll` passed exit 0: `approved`, digest=20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6 count=66. No -Update. The evaluator was built through the host gate in Release with UseAppHost=false (24.91s build; zero errors), and the check itself was leased (225s admission, 58s held).

## Owed and acceptance limits

Windows native CP-10/CP-11, native witness implementation/execution, the real retained historical ordered-input/TRX byte replay, and private/deployed watcher inventory remain owed. No desktop files, C:\Antiphon tree or live qualification d47848cc was accessed. Native Slow registration/policy-hash recheck belongs with the native implementation; no allowlist was changed here.

The synthetic portable M3 measurements cannot replace original historical inputs or Windows ownership/sharing/PS5.1 receipts. Keep the release/activation gate incomplete while these obligations remain. Review follows; a separate Merge task ports the reviewed changes to rc. No activation or qualification credit is claimed.

## Fix boundary (before portable M3)

Final original CP-2..CP-9 run: 7 green rows / 1 red (CP-8), exit 1; wall=2134.231s, execution=1503.840s, admissionOnly=630.391s. CP-8 failed only unreadable-timing-file-returns-inside-budget. An isolated base reader probe passed in 0.0080781s; the full assigned-base qualification harness, with the same mkfifo/chmod/ln-hidden PATH, passed in 79s. Thus inherited red was NOT established. One exact unchanged-source diagnostic CP-8 rerun passed, source clean/buildSource verified. No timeout, assertion or test input changed. The initial timing-sensitive failure remains unexplained and is retained; this is not clean verification or release authority. Caller Debug follow-up must investigate it, not silently erase it.

Latest portable row receipts: CP-1 8/8; CP-2 6/6; CP-3 6/6; CP-4 1/1; CP-5 3/3; CP-6 10/10; CP-7 2/2; CP-8 1/1; CP-9 1/1. Total 38 executed / 38 passed / 0 failed / 0 skipped. All source=f4d99876b6e25d876f6eace1fdee39d7b9c11506, sourceState=clean, buildSource=verified, slot=granted. CP-1 and CP-8 are exact row recoveries, not a relabeling of red aggregate runs. CP-9 asserts 17 named PASS and zero FAIL internally.

Unedited latest CHECKPOINT lines:

```text
CHECKPOINT CP-1 commit=f4d99876b6e25d876f6eace1fdee39d7b9c11506 build=reused filter=/*/Antiphon.Tests.TestHelpers/(RcManifestJournalTests*)|(RcManifestRecoveryTests*)/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-d99e4f23/.antiphon/checkpoints/CP-1-20261030-123016-6074/run.trx slot=granted waited=0s dirty=0 source=f4d99876b6e25d876f6eace1fdee39d7b9c11506 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=f4d99876b6e25d876f6eace1fdee39d7b9c11506 build=ok filter=/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-d99e4f23/.antiphon/checkpoints/20261030-123509-2f13/rows/CP-2/run.trx slot=granted waited=90s dirty=0 source=f4d99876b6e25d876f6eace1fdee39d7b9c11506 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=f4d99876b6e25d876f6eace1fdee39d7b9c11506 build=reused filter=/*/Antiphon.Tests.TestHelpers/TestPhaseSummaryTests/C1514T_MergeAndQuality executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-d99e4f23/.antiphon/checkpoints/20261030-123509-2f13/rows/CP-3/run.trx slot=granted waited=75s dirty=0 source=f4d99876b6e25d876f6eace1fdee39d7b9c11506 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=f4d99876b6e25d876f6eace1fdee39d7b9c11506 build=reused filter=/*/Antiphon.Tests.TestHelpers/TestClassificationPolicyTests/C487_G059 executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d99e4f23/.antiphon/checkpoints/20261030-123509-2f13/rows/CP-4/run.trx slot=granted waited=210s dirty=0 source=f4d99876b6e25d876f6eace1fdee39d7b9c11506 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=f4d99876b6e25d876f6eace1fdee39d7b9c11506 build=reused filter=/*/Antiphon.Tests.Scripts/ReleaseGateAuthorityContractTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d99e4f23/.antiphon/checkpoints/20261030-123509-2f13/rows/CP-5/run.trx slot=granted waited=105s dirty=0 source=f4d99876b6e25d876f6eace1fdee39d7b9c11506 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=f4d99876b6e25d876f6eace1fdee39d7b9c11506 build=reused filter=/*/Antiphon.Tests.Scripts/RcFullTimingContractTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-d99e4f23/.antiphon/checkpoints/20261030-123509-2f13/rows/CP-6/run.trx slot=granted waited=15s dirty=0 source=f4d99876b6e25d876f6eace1fdee39d7b9c11506 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=f4d99876b6e25d876f6eace1fdee39d7b9c11506 build=reused filter=/*/Antiphon.Tests.TestHelpers/TestPhaseSupervisorTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-d99e4f23/.antiphon/checkpoints/20261030-123509-2f13/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=f4d99876b6e25d876f6eace1fdee39d7b9c11506 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=f4d99876b6e25d876f6eace1fdee39d7b9c11506 build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogHarnessTests/HeartbeatExistingContract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d99e4f23/.antiphon/checkpoints/20261030-123509-2f13/rows/CP-9/run.trx slot=granted waited=15s dirty=0 source=f4d99876b6e25d876f6eace1fdee39d7b9c11506 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=f4d99876b6e25d876f6eace1fdee39d7b9c11506 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d99e4f23/.antiphon/checkpoints/CP-8-20261030-132053-74b2/run.trx slot=granted waited=15s dirty=0 source=f4d99876b6e25d876f6eace1fdee39d7b9c11506 sourceState=clean buildSource=verified
```
