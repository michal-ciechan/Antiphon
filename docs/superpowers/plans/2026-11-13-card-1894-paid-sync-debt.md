# CARD-1894: paid Ready sync debt and retention

Task/landing owner: Code fd90dcd8. Base: e4c45f4b4bb58870bb8684087a6d218e1b988cbe.
The card is the acceptance source; no prior CARD-1894 plan was present. Evidence:
origin/feat/card-task-918ceea9, investigation commit 56c4e841a.

Both runner retention and desktop legacy release currently keep every Ready debt.
Use one shared, EF-translatable unpaid predicate in these two policies. Pending and
Held remain unpaid regardless of confirmation; Superseded remains nonblocking.
Ready is paid only with a nonblank SourceSha and equal ConfirmedSha. A separate
unpaid debt still blocks. Preserve all other retention guards and rechecks.
No migration, setting, delivery, runner operation or live cleanup is required.
CardSiblingInventory's sibling-selection semantics stay outside this change.

S1 commits regression tests first; CP-1 must show an assertion failure in each
policy's paid Ready case before S2 changes production. S2 implements the predicate.
S3 regenerates the derived index in its own commit if changed, building its driver
first. Final qualification runs CP-2/CP-3 once at the resulting committed HEAD.
CP-1 is rerun after the fix using the final checkpoint output, not another cold build.

## Verification design

V-1: runner Ready equal SHA sends the subject and control, records Removed and
clears the mirror path; unequal, missing and blank evidence stays Held/sync_debt.
V-2: desktop Ready equal SHA retires the subject through the real guarded removal;
unpaid cases preserve filesystem/git and do not invoke git for the subject.
V-3: both policy methods cover Pending/Held even with equal SHA, Superseded,
and a paid Ready row alongside a separate Held debt (13 variants each).
R-1: all eight selected adjacent integration classes preserve the existing server,
release, switch, ref-only, temp-sweep and salvage invariants.
R-2: guard union, mechanical PC contract, platform guidance and layering remain green.
R-3: impact selection, fresh TRX class/method counts, clean-source provenance,
evidence-diff and conflict-free merge-tree over full task history.
M-1: inspect recovery logic and explain whether desktop fast-forward clears the
21 Held source_changed debts; do not mutate live state.

No OS branch changes: database predicates are portable. Existing OS branches in
changed classes/services require non-behavior dispositions in the OS inventory.
Restart: none for this task; deploying the server change later requires server
activation. No runner changes.

### Performance budget

Bounded selection: 8 impacted integration classes plus 10 guard/contract classes;
no full assembly or Unit lane. Shared Postgres with per-test schema isolation and
the existing process limiters; fixtures own temporary directories and disposal.
Provisional cold build 3 minutes; integration row 8 minutes and guard row 3 minutes.
Capture slot waits, phase timings, per-test TRX durations and actual wall cost.
Importer floor (15 min, 3x estimate) is an existing tooling budget, not measured p95;
do not widen it. A selection exceeding 30 minutes needs Debug per CARD-1894.
One red then one green proof selection, no repetitions after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1894-red/ | paid-debt-witness | /*/*/(C1677RetentionServerTests*)\|(C0824DesktopLegacyReleasePolicyTests*)/C1894_Only_confirmed_Ready_debt_is_paid | V-1,V-2,V-3 | all listed, 0 failed | 26 | 4 |
| CP-2 | S2-S3 | tests/Antiphon.Tests -> bin-c1894-final/ | impacted-retention | /*/*/(C0824DesktopLegacyReleasePolicyTests*)\|(C0824DesktopLegacyReleaseSettingsTests*)\|(C0824DesktopLegacyReleaseSwitchTests*)\|(C0824RefOnlyBoundaryTests*)\|(C0824RefOnlyCleanupTests*)\|(C1677RetentionServerTests*)\|(C1677TempSweepServerTests*)\|(C1708ReflogSalvageTests*)/* | V-1,V-2,V-3,R-1 | all listed, 0 failed | 8 | 8 |
| CP-3 | S2-S3 | CP-2 | guards | /*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/* | R-2 | all listed, 0 failed | 9 | 3 |

CP-1's final green is included method-for-method in CP-2; retain both original
CP-1 red and CP-2 green receipts and verify each of the 26 results in fresh TRX.
Tool bootstrap and derived-index driver/regeneration are the only allowed unlisted
build/run drivers; both take host slots. Generated evidence remains ignored.

## Pending SourceLanding Mutation

Mutation owns deliberate mutants; ordinary red-first evidence is not PC completion.
All following controls are pending, method-scoped against each policy's
C1894_Only_confirmed_Ready_debt_is_paid method (runner and desktop variants):

- PC-1: count all Ready as unpaid again (equal-SHA case fails).
- PC-2: exclude all Ready (different/null/empty/whitespace evidence cases fail).
- PC-3: accept equal null/empty/whitespace source and confirmation (blank cases fail).
- PC-4: drop Pending, then drop Held (equal-SHA Pending/Held variants fail).
- PC-5: block Superseded (different and null Superseded variants fail).
- PC-6: let paid Ready bypass a second Held debt (additional-held variant fails).

Use an unbound candidate under docs/superpowers/mutations/ if commissioned.
