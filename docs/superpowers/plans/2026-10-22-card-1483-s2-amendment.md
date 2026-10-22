# CARD-1483 S2 verification amendment

Code/landing owner: 1757a065-8fcc-424a-8ac6-0b51020e6995. Base:
`db1e72fd34acd52c4ab2436de28f0d1af3578b42` (S1 landed).
This amends only S2 of [the plan](2026-10-22-card-1483-real-msbuild-land-audit-plan.md).

S2 implements D-2/D-4/D-7: evaluated ProjectReference closure, tracked not-built
inventory, future glob reachability and resolved import trust. The seven landed
S1 methods plus three S2 methods make CP-2 ten results. S1's linked-item assertion
continues to count its six item refusals separately from the new glob diagnostics.
The caller's refinement in input e4a01f5a-5ae6-48a4-aad8-9fbc326759e3 replaces
the original whole-Unit requirement: the closed S2 checkpoint list is the entire
verification scope, and the Unit lane is deferred. The affected integration class
is LandGraphEvaluationTests in its entirety. No other integration class or
production land behavior changes; wiring is S5.

V-5 includes empty future Markdown globs, ancestor globs, wildcard extensions,
case-insensitive suffixes, no-extension patterns, ignored excludes and safe JSON,
source and sibling globs. V-6 includes an out-of-solution docs project, a false
reference condition and a transitive closure with a cycle. V-7 includes untracked,
docs, wildcard, non-definition and linked imports, with tracked and duplicate
tracked import acceptance. Existing V-1..V-4 and S1 repair V-1a/V-1b/V-3a stay required.
There are no ordinary R-n rows.

CARD-1504 is deferred to S3 target pins and S4 file probes (V-8..V-12).
CARD-1498 explicitly assigns its imported-props MSBuildProjectDirectory witness
to S5; it is not added to S2. V-13..V-16 also remain with S4/S5, not passed here.

Unlisted execution: bootstrap the checkpoint tool once through build-slot.ps1;
run CP-2 on the committed tests before implementing S2 as the required red witness.
The three new methods must fail for missing S2 behavior, not fixture/build errors.
Then run the closed final list below on the committed implementation/report tip.
The previous aggregate run had a passing CP-2 but an incomplete Unit row and an
invalid aggregate receipt. Run CP-2 once on the refinement/report commit to obtain
the missing standalone qualified receipt. Do not rerun the Unit lane or any suite.
Rebuilding the previously cleaned checkpoint-tool bootstrap is an unlisted build
needed to execute that row; it must also take a build slot.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1483-s2/` | c1483-s2 | `/*/*/LandGraphEvaluationTests/(C1483_S1_*)\|(C1483_S2_*)` | V-1..V-7, V-1a V-1b V-3a | exactly 10 executed, 0 failed/skipped | 10 | 15 |

### Cost

Ordinary floor: 15 minutes including an isolated build. The red witness needs one
additional isolated build. One final CP-2 execution replaces the invalid aggregate
certificate; no repeat-proof run follows a qualified green. Historical Unit cost
and failure evidence remain in the Code report, with the lane deferred, never passed.

### Pending SourceLanding Mutation

All plan controls remain pending: pins comparison (V-8/V-9), differential (V-12),
exit-2-as-approval (V-3/V-14), plus inherited S1 controls. S2 candidates for discovery:
PC-S2-1 remove glob reachability (V-5); PC-S2-2 omit reference recursion or not-built
inventory (V-6); PC-S2-3 omit import trust/wildcard/real-path checks (V-7).
Mutation owns every deliberate mutant and red/restore/green cycle.

### SDK layout clarification from the first implementation run

D-7's literal SDK-directory/wildcard rules reject every harmless SDK fixture on
SDK 10.0.402: Microsoft.Common.CurrentVersion.targets imports SDK-owned ImportAfter/*,
and Microsoft.NET.Sdk.ImportWorkloads.targets imports installed WorkloadManifest.targets
from the sibling sdk-manifests tree. Both are part of the selected SDK installation.
S2 permits wildcard imports only when importer and resolved target are both under
the Locator SDK root. It permits the manifest exception only for the exact installed
Microsoft.NET.Sdk.ImportWorkloads.targets importer, the workload-locator SDK name,
the literal WorkloadManifest.targets import and filename, and a real target under
that installation's sdk-manifests directory. Repository wildcard imports still
refuse, and no whole installation-root trust is added. Existing acceptance assertions
remain unchanged. Custom workload layouts outside this narrow exception fail closed.
