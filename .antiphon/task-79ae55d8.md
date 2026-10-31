# Task 79ae55d8 — failed-first ingress fallback

A previous-run record with no usable plan digest, candidate sha, liveness, or schema version now falls back to canonical order with a stated reason. Valid records keep the same ordering rules. It worked.

Tested source: `6a09884a8b236727c41c20b3b2c780e20b82a688`. Branch `feat/card-task-79ae55d8`, fast-forward from `3693d53b68c818e828e2dae055f7a3db046ff072`. No definition file changed. No `-Update`. Windows CP-15/16 remain owed.

## Commits

- `30788a46f0580c79e106cbef86c57667e215e307` test(nightly): witness unusable previous-run records falling back. Red tip.
- `6a09884a8b236727c41c20b3b2c780e20b82a688` fix(nightly): fall back when a previous-run record is unusable.

Files: `scripts/lib/nightly-dispatch.ps1`, `scripts/lib/nightly-tests-impl.ps1`, `scripts/test-nightly-failed-first.ps1`, `tests/Antiphon.Tests/Scripts/RcFailedFirstExecutionTests.cs`.

The digest guard stays in `New-NightlyDispatchSchedule` (`scripts/lib/nightly-dispatch.ps1` lines 104–116). `Resolve-NightlyPreviousRunOrder` only sees suite and chunk ids, so it cannot judge class membership. `PreviousOrder` calls Resolve directly and has no digest.

Resolve checks, inside the existing try and before the date/roster logic: liveness missing vs not `closed`, schema version missing vs not `1`, suites not an array, sha blank vs not 40 hex vs a different 40-hex sha. Candidate id is the existing `sha` field. A present schema of `"1"` or `1` is accepted. A 64-hex digest that differs stays `previous-run-plan-mismatch`. Uppercase hex is malformed. Fallback resets to canonical ordinal order and empty lanes. It never refuses and never returns a partial order. Ordering stays a pure function of its arguments.

The completed-summary producer (`scripts/lib/nightly-tests-impl.ps1` line 1786) now writes `schemaVersion=1` and `liveness='closed'` with the existing sha, completedAt, canonicalPlanDigest, and suites. `Initialize-NightlyDispatchHistory` is unchanged: an absent liveness still reaches Resolve, and an explicit non-closed value still drops history as `history-liveness-unknown`.

Stopped-run seed work was not edited. On `feat/card-task-f6aabaef` at `a00f1b46753542a8721a111fb4c26ed1d542cf2a` the seed returns on the line immediately after Resolve's param block, before the try. These checks sit inside the try, replacing the old liveness skip that their tip still has, so a merge conflicts on that liveness line and their early return stays above the try. The digest guard and the producer assignment at `nightly-tests-impl.ps1:1786` are elsewhere; that branch does not edit the producer line.

## Counts

| Run | Passed | Failed | Result |
|---|---|---|---|
| Red small probe at `30788a46`, label `79ae-red-ingress`, lease `eec9ad0c-7424-4b0c-bee9-45c037c03f0d` | 16 | 2 | `chainDigestMissing`, `chainIngress`. Duration 542.77s, held 106s |
| Green small probe at `6a09884a`, label `79ae-green-ingress`, lease `3dcda3fa-eb5c-494b-8295-9c6dc4dd448b` | 18 | 0 | Duration 286.57s, held 59s. Valid order `antiphon/Priority,antiphon/Ordinary,session-runner/Ordinary,session-runner/Priority` |
| Skip-restore mutant, label `79ae-mutant-ingress`, lease `37efece4-5f1f-4459-b27a-5d7929914d4d` | 16 | 2 | `chainDigestMissing`, `chainIngress`. Held 56s. Then restored |

Mutant restored the old truthy skip `if($PreviousRun -and $PreviousRun.canonicalPlanDigest -and $PreviousRun.canonicalPlanDigest -cne $dispatchDigest)`. Absent, null, and empty digests came back as `previous-run-order`. Whitespace, short, long, and non-hex digests came back as `previous-run-plan-mismatch`. Candidate, liveness, and schema rows stayed on their new reasons, and the single `chainIngress` assertion failed. The file was restored before any commit. SHA256 `08ebb137ca2a7b128454b30ce54363fc9c24cc503b8a4fb527dad10c276ead8d`. The mutant was not committed.

Pre-existing warnings, same on red, green, and mutant: `historical-membership-unavailable` / `short-trx`, `history-schema` JSON on the malformed fallback case, and `active-identity-unavailable` when `active-chunk.json` is not a file.

## Ingress

Original order means canonical ordinal order. A valid record is the same candidate, in window, suites well formed, liveness `closed`, schema version 1, and a matching 64-lowercase-hex digest.

| Case | At red tip `30788a46` | After `6a09884a` |
|---|---|---|
| Digest absent, null, or empty | Fail-open: `previous-run-order` | `previous-run-plan-digest-missing`, canonical |
| Digest whitespace | `previous-run-plan-mismatch` | `previous-run-plan-digest-missing`, canonical |
| Digest wrong length or not 64 lowercase hex | `previous-run-plan-mismatch` | `previous-run-plan-digest-malformed`, canonical |
| Digest 64 lowercase hex, different plan | `previous-run-plan-mismatch` | unchanged |
| Candidate sha absent, null, or blank | `previous-run-schema` | `previous-run-candidate-missing`, canonical |
| Candidate sha not 40 hex | `previous-run-schema` | unchanged |
| Candidate sha a different 40 hex | `previous-run-candidate-mismatch` | unchanged |
| Liveness absent | Trusted: `previous-run-order` | `previous-run-liveness-missing`, canonical. Producer writes `closed` |
| Liveness blank | `previous-run-liveness-unknown` | `previous-run-liveness-missing`, canonical |
| Liveness present and not `closed` | `previous-run-liveness-unknown`; Initialize also drops history | unchanged |
| Schema version absent or blank | Trusted: `previous-run-order` | `previous-run-schema-missing`, canonical. Producer writes `1` |
| Schema version present and not 1 | Trusted: `previous-run-order` | `previous-run-schema`, canonical |
| Valid record | `previous-run-order` | unchanged rules |

S1 strict prior-failure export and S2 priorFailure-first partition when there is no PreviousRun are unchanged.

## Checkpoint

Run `20261031-151233-86aa`. Linux, TEMP=/tmp, UseAppHost=false. Imported 4 rows. CP-5 through CP-8 ran serially from one fresh build, `bin-79ae-ingress/`. Build 288.215s. CP-6 harness duration 00:09:26.699 (testsWall 566.699s) against the unchanged 1080s bound. Slot waits are admission, not harness time: CP-6 waited 495s. Wall 35m28s. Charged execution 1183.111s. Admission 945.242s. `unlisted: none`. Verdict GREEN exit 0.

`CHECKPOINT SOURCE VALID source=6a09884a8b236727c41c20b3b2c780e20b82a688 rows=4`

Tool bootstrap lease `d6b00b2c-a97c-4b32-b56e-32fd1a2821db`, output `bin-79ae-tool/`, waited 15s, held 5s. The run was not wrapped in a second slot.

```text
CHECKPOINT CP-5 commit=6a09884a8b236727c41c20b3b2c780e20b82a688 build=ok filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogTests/CapFitsAdmittedPlannedCost executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-79ae55d8/.antiphon/checkpoints/20261031-151233-86aa/rows/CP-5/run.trx slot=granted waited=135s dirty=0 source=6a09884a8b236727c41c20b3b2c780e20b82a688 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=6a09884a8b236727c41c20b3b2c780e20b82a688 build=reused filter=/*/Antiphon.Tests.Scripts/RcFailedFirstExecutionTests/ActualLoopFollowsSchedule executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-79ae55d8/.antiphon/checkpoints/20261031-151233-86aa/rows/CP-6/run.trx slot=granted waited=495s dirty=0 source=6a09884a8b236727c41c20b3b2c780e20b82a688 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=6a09884a8b236727c41c20b3b2c780e20b82a688 build=reused filter=/*/Antiphon.Tests.Scripts/RcFailedFirstExecutionTests/JournalCustodySurvivesInterleaving executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-79ae55d8/.antiphon/checkpoints/20261031-151233-86aa/rows/CP-7/run.trx slot=granted waited=195s dirty=0 source=6a09884a8b236727c41c20b3b2c780e20b82a688 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=6a09884a8b236727c41c20b3b2c780e20b82a688 build=reused filter=/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-79ae55d8/.antiphon/checkpoints/20261031-151233-86aa/rows/CP-8/run.trx slot=granted waited=120s dirty=0 source=6a09884a8b236727c41c20b3b2c780e20b82a688 sourceState=clean buildSource=verified
```

Evidence: `/work/worktrees/task-79ae55d8/.antiphon/checkpoints/20261031-151233-86aa/report.md`. Generated evidence stays gitignored. Plan: `.antiphon/79ae/plan.md`.

## Land graph and branch

`scripts/audit-land-verification-graph.ps1 -Check` approved `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69 phase=pass2`. Five known not-built projects: `docs/investigations/evidence/card-0462/Reproduce.csproj`, `scripts/fixtures/nightly/c1375-probe/Probe.csproj`, `scripts/fixtures/nightly/c487-probe/Probe.csproj`, `tests/Antiphon.TimeoutAudit.Tests/Antiphon.TimeoutAudit.Tests.csproj`, `tools/Antiphon.NativeLinuxMigrationAudit/Antiphon.NativeLinuxMigrationAudit.csproj`. Auditor built Release to `bin-79ae-audit/` (lease `a27ac6e2-ab29-4ed0-bda9-3ee3910703f5`, waited 0s, held 31s) and invoked with `-ToolPath`. No `-Update`.

`scripts/check-evidence-diff.ps1` over `3693d53b68c818e828e2dae055f7a3db046ff072..6a09884a8b236727c41c20b3b2c780e20b82a688`: commits=2, entries=0, violations=0.

After `git fetch origin master` and `rc/2026-10-27`: merge-tree onto `origin/rc/2026-10-27` `e0d49d221513b467f8c853f47587e688e04255e2` is clean, tree `05e8c1ab25696ca935a761281b6bbc7bd20ecb0e`. Merge-tree onto `origin/master` `2051d327123a5e98524b4bd1ecf31c6e516bdb4e` has 48 conflicts, including `scripts/lib/nightly-tests-impl.ps1` and `scripts/test-nightly-failed-first.ps1`. No merge was made. The chain lands onto rc. No tag, publish, force-push, rebase, desktop read, or touch of qualification run d47848cc.

The checkpoint tool deleted `tests/Antiphon.Tests/bin-79ae-ingress/`. After the last tool use these exact paths were removed: `src/Antiphon.HostCleanup/bin-79ae-tool`, `tools/Antiphon.Checkpoints/bin-79ae-tool`, `tools/Antiphon.LandGraphAudit.Core/bin-79ae-audit`, `tools/Antiphon.LandGraphAudit/bin-79ae-audit`. The receipt marker `.antiphon/checkpoints/20261031-151233-86aa/builds/bin-79ae-ingress` remains with the ignored run.

--- next stage ---
next: review
handoff: short Final review of the whole chain, then merge to rc. Windows CP-15/16 remain owed. Master still conflicts and gets its own review.
artifact: /work/worktrees/task-79ae55d8/.antiphon/task-79ae55d8.md
