# CARD-1731: C599 snapshot restoration

Original Code/landing owner: 0915902c. Branch feat/card-task-0915902c.
Task base: 991e805e939b7cd95985a46feac8e378811a6496.
Debug handoff: 11cdf6ecb, docs/investigations/2026-11-06-debug-card-1731-ledger-tests.md.

Test-side only. S1 adds a deterministic failing work-count/timestamp regression and
an independently passing finally-isolation witness. S2 caches snapshot entries and
unique parents, compares bytes before writing, removes only the redundant variant
pre-restore, retains finally restoration and every production guard/assertion, and
registers the publication class Slow. No production evidence-path acceptance cache.
S3 records completed measurements and budgets without widening an unmeasured limit.

## Verification design

- V-1: C599SnapshotTests.Finally_restores_after_a_throwing_mutation proves initial
  real gate validity, thrown-mutation failure, cleanup, every saved byte/absence,
  subsequent real gate validity and recreation of a removed binary file/parent.
- V-2: C599SnapshotTests.Restore_runs_once_and_only_writes_changed_bytes counts
  real restores, checks a fixed old timestamp survives an unchanged restore and
  detects changed bytes even with identical length/timestamp. Red at S1, green S2.
- V-3: exact Ledger/Joins/Markers once, recording restore/validation/body phases.
  Included in the full publication class CP-2; do not rerun its green methods.
- R-1: full ReleaseGatePublicationAuthorityTests and ReleaseTagAllocatorTests, the
  only existing classes using Save/Restore-C599ASnapshot. No assertion changes.
- R-2: full classification/registry/tripwire and MechanicalPcContractTests.
- R-3: whole Unit lane in six disjoint initial-letter/category shards. This is the
  Final profile's regression invariant. Unit omits delivery, landing, leases and
  persistence; named integrations cover the bounded changed helper consumers.
- R-4: check-evidence-diff full base..HEAD; merge-tree against fresh origin/master;
  policy/hash and live pin recomputation if protected inputs change, both together.

No product OS branch or rc startup changes. Snapshot runs use real System.IO files
on Linux. The existing native evidence-link witness remains in the publication
class; Windows-specific runtime behavior is unchanged. Restart: none.

## Performance budget

Debug found 3/3 censored 300s failures at 0dbd8295a; Markers restored 91 times in
160.276s and validated ledgers in 115.955s. No completed p95 exists. Retain the
300s child execution cap until completed post-fix samples justify a smaller cap.
Snapshot/parent setup once per fixture, independent mutable fixture/listener per
method. Git creation measured 0.206s; no shared Git template is justified here.
Record monotonic totals for scan, parent creation, byte reads/comparison, writes,
restore, gate, fixture/setup and teardown; uninstrumented remainder stays unknown.
Native bootstrap/build estimate 5m, admission finite 45m; execution estimates below
are provisional CARD-1731/CARD-1404 bounds, not p95. Checkpoint's 15m/3x row floor
is a documented provisional tooling exception, not permission to widen tests.
Collect next 20 qualifications/20 days, checkpoint after five samples, and arrange
a quiet-host sample if still needed. No repeats after green. Unit aggregate ~35m
is the existing CARD-1720/1725 shard estimate; no individual row >30m. Any observed
>30m requires Debug. No full-assembly run; no unbounded affected integration class.
Fresh receipts/TRX in ignored .antiphon/checkpoints; essential lines in task report.
Bootstrap tool build and one requested native incremental compile are separately
accounted gated infrastructure runs. At most three checkpoint rows per invocation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1731-final/` | snapshot | `/*/*/C599SnapshotTests*/*` | V-1,V-2 | 2 cases, red work regression at S1, green S2 | 2 | 2 | true |
| CP-2 | S1-S2 | CP-1 | publication | `/*/*/ReleaseGatePublicationAuthorityTests*/*` | V-3,R-1 | full class, 15 cases, 0 failed/skipped | 15 | 15 | true |
| CP-3 | S1-S2 | CP-1 | allocator-guards | `/*/*/(ReleaseTagAllocatorTests*)\|(MechanicalPcContractTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)/*` | R-1,R-2 | every full class, 0 failed/skipped | 1 | 5 | true |
| CP-4 | S1-S2 | CP-1 | unit-ad | `/*/*/(A*)\|(B*)\|(C*)\|(D*)/*[Category=Unit]` | R-3 | nonzero, 0 failed/skipped | 1 | 10 | true |
| CP-5 | S1-S2 | CP-1 | unit-eh | `/*/*/(E*)\|(F*)\|(G*)\|(H*)/*[Category=Unit]` | R-3 | nonzero, 0 failed/skipped | 1 | 3 | true |
| CP-6 | S1-S2 | CP-1 | unit-il | `/*/*/(I*)\|(J*)\|(K*)\|(L*)/*[Category=Unit]` | R-3 | nonzero, 0 failed/skipped | 1 | 3 | true |
| CP-7 | S1-S2 | CP-1 | unit-mp | `/*/*/(M*)\|(N*)\|(O*)\|(P*)/*[Category=Unit]` | R-3 | nonzero, 0 failed/skipped | 1 | 7 | true |
| CP-8 | S1-S2 | CP-1 | unit-qt | `/*/*/(Q*)\|(R*)\|(S*)\|(T*)/*[Category=Unit]` | R-3 | nonzero, 0 failed/skipped | 1 | 10 | true |
| CP-9 | S1-S2 | CP-1 | unit-uz | `/*/*/(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit]` | R-3 | nonzero, 0 failed/skipped | 1 | 2 | true |
| CP-10 | S1 | n/a | baseline-isolation | `pwsh -NoProfile -File scripts/test-release-gate.ps1 -Case C1731_SnapshotIsolation -ResultsDirectory .antiphon/c1731-baseline-isolation` | V-1 | exit 0, five named assertion passes | n/a | 1 | true |

CP-10 is the corrected pre-fix isolation witness. CP-1 at acc9f5f0a recorded the
intended count/timestamp red, plus a witness naming error ($extra shadowed by
$Extra) corrected before implementation. CP-10 runs the real unchanged helpers
without an unnecessary compiler rebuild; its five assertion rows must pass before
removing the redundant pre-restore. This is a required manual acceptance row.

## Pending Mutation

PC-1: reintroduce the redundant pre-restore; exact Restore_runs_once_and_only_writes_changed_bytes must fail count=2.
PC-2: unconditional writes; same method must fail fixed timestamp.
PC-3: remove finally restore; Finally_restores_after_a_throwing_mutation must fail bytes/absence and next control.
PC-4: compare only stat/length; work method must fail same-length/timestamp bytes.
All PC variants and missing-control discovery belong to SourceLanding Mutation;
ordinary baseline red is not deliberate mutation credit. Unbound candidate under
docs/superpowers/mutations/2026-11-06-card-1731-candidate.md.
