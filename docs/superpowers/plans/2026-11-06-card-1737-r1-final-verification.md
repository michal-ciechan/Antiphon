# CARD-1737 R1 Final verification supplement

Original Code/landing owner `0cc54c85`; task base
`b8efe5cc91d3ae4015b100a9bcf89d40e92827b7`. Production is unchanged.
The frozen [R1 TestDesign](../testdesign/2026-11-06-card-1737-r1-testdesign.md)
continues to own CP-1..3, V-1..3, R-1..4 and author proof. This supplement records
the additional whole Unit lane and full affected integration classes explicitly
required by the Code brief's Final profile; it does not alter the frozen rows.

Invariant: the new preservation witnesses integrate without weakening existing
filesystem custody, real Git cleanup/CAS, compiled classification, execution policy,
mechanical instructions or layer boundaries. Unit omits real delivery, landing,
leases and persistence; the named full integration classes supply only their
existing Git/removal claims. No full assembly or unbounded integration classes.

Final Unit includes full MechanicalPcContractTests, TaskPlatformGuidanceTests,
TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestTripwireTests,
NativeLinuxPolicyWitnessTests and C1729LayeringGuardTests. SlowTestRegistryMergeTests
is Integration and explicitly selected in CP-4. CP-3 separately selects the live-lane
wrapper's protected-input witness. No redundant green class rerun is required.
Manual acceptance: inspect production diff empty, policy/marker/hash coupling,
reflection signatures and real input/outcome binding, full-range evidence guard,
OS-witness declaration and clean merge-tree against freshly fetched master.

## Performance budget

One cold checkpoint build, native UseAppHost=false, 28min execution cap and 10min
admission per driver. Existing Debug `5ae98d31` supplies build/queue comparators;
Debug `86a941a3` (`docs/investigations/2026-11-05-debug-narrower-parent-deadlines.md`
on its task branch) establishes why an explicit 30min run allowance cannot cover
build plus whole Unit. No timeout or assertion is widened. Estimates below are
provisional CARD-1737/CARD-1720; first required receipts are the collection sample,
not measured p95. Unit is estimated 15-30min execution, CP-4 1-3min plus 3-12min
build; any actual >30min selection needs caller performance Debug with its source,
load and timing evidence. Shared fixtures and process limiter stay unchanged.
No new delivery path, startup, web host or production runner. Missing subphase
measurements stay unknown. Source and phase evidence stays ignored.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-4 | R1-Final | tests/Antiphon.Tests -> bin-c1737-r1-final/ | full-affected | /*/*/(LandingRemovalPolicyControlTests*)\|(WorktreeGuardedCleanupTests*)\|(WorktreeNoFollowDeleteTests*)\|(SlowTestRegistryMergeTests*)/* | Final integration, policy sweep | every method/argument in these four full classes; platform skips recorded as pending native, no failures | 1 | 14 | true |
| CP-5 | R1-Final | CP-4 | full-unit | /*/*/*/*[Category=Unit] | Final Unit, mechanical instruction/platform/policy/layering guards | whole exact Unit predicate, nonzero counts; inherited/native outcomes reported individually | 1 | 15 | true |

Run these two rows as their own committed group, through the checkpoint tool,
with exact expected-source-sha. CP-4 timeoutMinutes=12, CP-5=45 (provisional existing
Unit importer allocation); build rowMinutes=28; charged total follows derivation
`max(30,2*(14+15)+10)=68m`, hard wall 98m (three admissions at 10m).
At most three rows per run. Do not repeat green rows. Inspect fresh TRX for every
named full class and the compiled Unit guard classes; rerun only actual red rows.
Formal PC-1/2/3, each single variant, remain pending for SourceLanding Mutation.
