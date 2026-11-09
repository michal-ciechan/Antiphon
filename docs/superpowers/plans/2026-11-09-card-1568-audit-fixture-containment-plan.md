# CARD-1568 audit-fixture outside-root containment

Tests only. `AuditGitFixture` still accepts no deletion path on its public constructor.
The witness seam is `internal AuditGitFixture(string)` plus `CleanupOwnedRoot(root, tempRoot)`.
`RunAsync` deletes only `Root`, and only after the same parent-equality and
`antiphon-audit-` name check.

## Decision

An owned root that already passes containment and is absent is a no-op.
`File.GetAttributes` must not run, so a successful body is not failed by
`FileNotFoundException`. Absence is not a bypass: the parent and name checks
run first, and a missing outside or misnamed path still throws
`Audit cleanup escaped its owned temp root.` A present directory link still
reaches `RefuseLink` before any attribute change.

Windows rows W-1..W-7 stay owed to the desktop lane and are not claimed here.

## Verification design

- V-1: `root-evil` next to `root` (parent differs by prefix) is refused and the sentinel remains.
- V-2: a path outside the temp root, named `antiphon-audit-*`, is refused and the sentinel remains.
- V-3: a direct temp child named `root-evil-*` is refused and the sentinel remains.
- V-4: an absent contained `antiphon-audit-*` root completes without throwing.
- V-5: an absent path outside temp still throws the containment `IOException`.
- V-6: the existing read-only, link, and body-failure witnesses still pass.
- R-1: policy sweep, mechanical contract, platform guidance, layering, and the lane census.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1568/` | owned-audit-cleanup | `/*/*/LandGraphAuditCleanupTests/*` | V-1, V-2, V-3, V-4, V-5, V-6 | all 10 results, 0 failed/skipped | 10 | 12 |
| CP-2 | all | `CP-1` | guard-batch | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1 | >= 40 executed, 0 failed | 40 | 8 |

### Cost

Ordinary floor 20 minutes: one isolated build, the touched class, then the guard batch on that build. No repeat after green. Red witness and per-witness mutants are explained unlisted runs, not extra checkpoint rows.

## Performance budget

One `Antiphon.Tests` build plus `LandGraphAuditCleanupTests` (10 results, about 6 seconds after a warm host on this Linux runner) and the named guard classes. No AppHost, container, or migration setup. The absent-root no-op adds no timeout. Build and slot wait are reported separately from the test body.
