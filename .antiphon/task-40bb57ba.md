# Group 28b Debug result

Fixed the remaining Linux failure in the assigned parking delivery class. Latest results cover 56 cases with zero failures or skips across two identified invocations: 50 unchanged cases at cc89e064e0a575884bb0b73310d5776805b2a91d and all six parking cases at 0af6eeb6fa77535d0d5c9fce1478160398da8018. Native Windows qualification remains pending.

## Source and final change

Assigned branch: `feat/card-task-40bb57ba`. Base: `0e64a6aba1b4e28052faebfac88f01232b4cb66b`, fetched origin/master at task start and a descendant of the commissioned minimum e970c2c9. No branch switch, rebase, merge, force push, tag, release script, Unit lane, Review, desktop execution or native lock was used. The existing desktop qualification supervisor was untouched.

The final net source delta changes only `tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs` (10 additions, seven deletions):

- Reuse the existing `AttachReviewRecipientAsync` helper for positive parked-answer recipients at launch and after simulated server restart. It supplies the shared Claude dialog observation and binds the runtime to the persisted accepted generation. This covers the six answer-size arms, wrong-session receipt probes, and busy/idle recovery cut scenarios.
- Before deleting the test's synthetic wrong-session prompts, delete only runtime actions referencing those exact prompt rows. This respects the transcript foreign key without deleting unrelated action debt.

Every original assertion remains, including exact spill bytes, complete recipient UserPrompt, busy deferral, generation/sequence/session refusal, exact-once receipt, and recovery-boundary checks. Production code, timeouts, retries, shared fixture source and sibling-owned classes are unchanged in the final delta.

## Counts and classification

Counts are passed / failed / skipped from fresh TRX, not scenario labels inside methods.

| Class | Reported native failures | Linux base | Latest Linux result | Latest executed source |
|---|---:|---:|---:|---|
| DelegateBranchDeliveryTests | 7 | 7 / 0 / 0 | 7 / 0 / 0 | cc89e064e0a575884bb0b73310d5776805b2a91d |
| RepairSourceSettlementTests | 7 | 36 / 0 / 0 | 36 / 0 / 0 | cc89e064e0a575884bb0b73310d5776805b2a91d |
| LandEvidenceWarningDeliveryTests | 7 | 7 / 0 / 0 | 7 / 0 / 0 | cc89e064e0a575884bb0b73310d5776805b2a91d |
| BlockedTaskParkDeliveryTests | 3 | 5 / 1 / 0 | 6 / 0 / 0 | 0af6eeb6fa77535d0d5c9fce1478160398da8018 |

The final commit changes only the parking test file, so the preceding 50 passing results for unchanged classes are retained rather than executed again. This is not a claim of a single green 56-case run at the final SHA.

The sole base failure was `C1065_FullAnswerRequiresNewSessionUserPrompt`: its first delivery arm found no spill file. The fixture attached raw fake Claude adapters with no supported current observation. Automated input was therefore withheld; the missing file was a downstream symptom. Supplying the existing observation/generation helper let all six answer-size arms execute and exposed a second, previously unreachable fixture error: PostgreSQL 23503 when deleting synthetic transcript rows still referenced by SessionRuntimeActions. The scoped dependency-first cleanup resolves that error. Both clusters are fixture defects: missing current observation and relational cleanup order. No stale-screen, Windows path/platform, cross-fixture shared-state, load-induced or product-defect cluster was demonstrated. No product change was made.

Initial directory-lifetime hypotheses did not fix the failure. Those intermediate changes were reverted through new commits, not rewritten history; the shared RunnerSeatReleaseFixture matches the base. Their failed receipts and costs remain below.

Current master already included upstream fixes for wire platform consistency, queued recovery path persistence, guarded repair/land-warning delivery and parked-review observation. The other reported native failures do not reproduce at this newer base; this does not establish their native disposition.

Sample ownership: all C1213 branch samples and C1558 platform samples are in DelegateBranchDeliveryTests; C1104_ParkedReviewReplyReceiptSpillsToPointerAtProductionChunkSize is in BlockedTaskParkDeliveryTests. All passed on Linux. Existing C1558_WireRegistersReportedPlatform explicitly forces `"windows"`, `"linux"` and null, checks registration and reconnect, while C1558_InconsistentPlatformIsRefused forces contradictory Windows/Linux strings and checks HTTP 409 and no live registration. These portable string witnesses executed without a native lock. No additional Windows-semantics repair was necessary.

## Execution and historical costs

Host: server2/Linux, Debian 12, linux-x64, .NET 9.0.21, TUnit 1.44 / Microsoft Testing Platform 2.2.2. Every build/test driver used build-slot.ps1; all leases were granted with reported admission wait 0s and maxcpucount 6. Sources stayed frozen during each driver. Every driver exited and was joined.

| Evidence prefix | Source | Build elapsed, exit | Test selection | Passed / failed / skipped | Test elapsed, exit |
|---|---|---|---|---|---|
| baseline | 0e64a6aba | 3m41.91s, 0 | Four assigned classes | 55 / 1 / 0 | 4m26.010s, 2 |
| repair | 4e18217b0 | 2m08.92s, 0 | Parking class | 5 / 1 / 0 | 1m30.409s, 2 |
| diagnostic | 8f4403778 | 2m26.36s, 0 | Exact failing method | 0 / 1 / 0 | 1m00.709s, 2 |
| final (historical storage attempt) | 00a23656d | 2m09.90s, 0 | Four assigned classes | 55 / 1 / 0 | 4m26.496s, 2 |
| observation | cc89e064e | 3m30.49s, 0 | Four assigned classes | 55 / 1 / 0 | 4m13.116s, 2 |
| verified | 0af6eeb6f | 3m26.66s, 0 | Parking class | 6 / 0 / 0 | 2m41.588s, 0 |

All builds had zero errors and retained 1,197 existing warnings. Rebuilds compiled committed diagnostic/repair changes. Additional execution was limited to the failing method/class, except the two four-class reruns: one checked a proposed shared-fixture change, and one consolidated the restored fixture plus observation setup. The storage attempts remained red and receive no green credit.

Bounds were provisional finite Debug budgets, not measured p95: each build had 600s total including up to five minutes slot admission; four-class runs had 900s total including five minutes admission; parking-only runs had 600s including five minutes admission; the diagnostic method had 300s including two minutes admission. No bound was reached, and no test timeout/retry/assertion was relaxed. Final observation-run start load sample was 40.06 / 33.94 / 22.04; earlier load is unknown. No causal load failure is claimed.

TRX retains per-test durations, arguments and executed membership. Phase JSONL was enabled on every invocation with run ID, group 28b, attempt 1 and suite antiphon-suite. Shared assembly DB setup was once per invocation; baseline container start/migration measured 7.461s/36.509s, final verified start/migration 5.605s/41.049s. Other uninstrumented spans remain unknown/remainder. Fixture sidecars mark the shared fixture outcome incomplete; TRX determines test outcome. These are small-sample Debug measurements, not p95 qualification.

Evidence root: `/work/worktrees/task-40bb57ba/.antiphon/evidence/task-40bb57ba/`. Each table prefix has `<prefix>-build.log`, `<prefix>-tests.log`, `<prefix>-results/<prefix>.trx`, `timing-<prefix>/` and an assembly SHA-256 file. Source SHA and empty pre-test status records accompany baseline, repair, final, observation and verified runs. The diagnostic source is its committed diagnostic slice. `owned-outputs.txt` records the 33 producer-owned bin-g28b roots. All were removed after the drivers exited; retained evidence remains ignored.

Verified DLL SHA-256: `b2d427d273fac1557b7b477eebd25dc3eeaccb140623c9ae12a53d6e4abaa496`; checked unchanged after execution, before cleanup. MVID: `af3a52cc9c7448679df2e7323124969a`. Baseline and intermediate hashes are retained separately. Final source worktree was clean before testing.

## Published history and landing checks

Each slice was committed and pushed before its next build/run:

- `4e18217b023bc097fb92ebd69bcbac206510753d`: directory hypothesis; red, later reverted.
- `8f44037787eef8339001433087df4653eb8176d4`: diagnostic assertion; red, diagnostic later removed.
- `00a23656d3aa84ba9ea69f2e24eb304fb6525401`: storage hypothesis; red, later reverted.
- `cc89e064e0a575884bb0b73310d5776805b2a91d`: shared observation setup; advanced to latent FK cleanup failure and removed the storage experiments.
- `0af6eeb6fa77535d0d5c9fce1478160398da8018`: remaining supported recovery recipients and scoped cleanup; six parking cases green.

Before report publication, evidence-history guard over base..0af6eeb6f passed: five commits, zero evidence entries, zero violations. Merge-tree against fetched master `99bd60f861eab6ffdcb98d5a4a006719ff4d97bc` was clean, tree `a3c39f9e1ad03f34094128e25280824d703fedf6`. The report-only tip is checked separately in the final handoff; it changes no tested source.

## Native rerun owed after landing

The caller should rebuild the candidate on its owned Windows qualification lane through the build-slot gate and execute these rows into fresh TRX directories. Do not borrow the existing qualification supervisor or native lock:

1. `/*/*/DelegateBranchDeliveryTests*/*` (seven Linux results).
2. `/*/*/RepairSourceSettlementTests*/*` (36 Linux results).
3. `/*/*/LandEvidenceWarningDeliveryTests*/*` (seven Linux results).
4. `/*/*/BlockedTaskParkDeliveryTests*/*` (six Linux results).

Build command from the checkout: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label group28b-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-g28b/ --nologo`.

Test command: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label group28b-tests -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-g28b/ -- --treenode-filter '<row above>' --report-trx --report-trx-filename run.trx --results-directory <fresh-directory>`.

The actual combined Linux filter was `/*/*/(DelegateBranchDeliveryTests*)|(RepairSourceSettlementTests*)|(LandEvidenceWarningDeliveryTests*)|(BlockedTaskParkDeliveryTests*)/*`. The final run used only the parking row. The diagnostic selection was `/*/*/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt`.

--- next stage ---
next: land
handoff: Land the pushed test-only branch under the operator no-Review instruction, then rerun the four native rows. No native Windows qualification or activation is claimed by this Debug result.
artifact: /work/worktrees/task-40bb57ba/.antiphon/task-40bb57ba.md
