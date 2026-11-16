# CARD-1882 S1: Linux algorithm-identity checkpoint manifest

Design only; no production or test source changed and no execution claimed. This
appends verification structure to Plan e2dd6b0a, commit
`6de02276d3d963bcf617e10a42872bff927b4cd6`, artifact
`docs/superpowers/plans/2026-11-16-card-1882-algorithm-digest-plan.md` on
`origin/feat/card-task-e2dd6b0a`. Its D-1 through D-7 and S1 production boundary
remain authoritative. Master fetched and read:
`48a4615dcb4d67033886a3e31629dc7351ed0852` (also this task's base).
Read CARD-1882's full description and history revisions 1 and 2 on 2026-11-16.

S1 changes only the identity path array in `scripts/lib/release-impact.ps1` and
`tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs`. Copying a deciding
file into a fixture does not authorize editing that source file. Keep
`coverage.complete=false`, `admitted=false`, `gateCredit=false`, and the release
envelope incomplete/Full. Do not compose S1R6 or call graph/evaluator/index code
from the envelope. No canonicalizer or script-closure module exists at this SHA;
do not invent an inventory entry for one.

Collision hold: CARD-1820 S1R6 owns the selector/envelope composition seam;
CARD-1836 S5c owns plan/status and must not interpret this digest as activation.
Code task `d2fff5d4` is reported in flight in the selector/index area (caller
evidence, not a live concurrency query). The entire S1 production/test slice and
CP-1 through CP-19 wait for the caller to establish that overlapping source
ownership is released and select the integration base. CP-3 through CP-10 bind
the selector/index/relation area directly. CP-17 guards the admission boundary;
it gives no permission to edit S5c. Do not dispatch a competing S1 writer, merge
the other branch, regenerate the index, or silently run against mixed tips.
After that hold clears, run every row; a hold is not a passing receipt.

## Verification design

### Inspection

- Read every body in `ReleaseImpactEnvelopeTests`, including `AssertIncomplete`,
  `AssertRefused`, `Fixture.CreateAsync`, both selector launch paths, Git/commit,
  process/drain/cleanup bounds, and `RepoRoot`. These are the nearest fixture;
  there is no separate fixture file. Boundaries: V-1 through V-18, R-1 through
  R-4. In particular, drift must call `SelectCopiedAlgorithmAsync`; `SelectAsync`
  executes checkout source and cannot witness drift in a scratch copy.
- Read `Get-ReleaseImpactAlgorithmIdentity`, `New-ReleaseImpactEnvelope`,
  `ConvertTo-ReleaseImpactJson`, the selector's early release branch and
  `Refuse-ReleaseImpact`: raw disk/commit comparison, ordered digest, CLI JSON
  refusal, and the conservative envelope map to V-1 through V-16 and R-1/R-2.
- Read `RcHistoricalAdmissionTests`, its ScriptHarness configuration,
  `test-rc-admission.ps1`'s `CollisionAndAlgorithmCoverageForceFull` case and
  `Get-RcAdmissionAlgorithmCoverage`: V-17/R-3. The separate admission digest
  fixture is not a substitute for the envelope identity test.
- Read the fourteen files' comment edit anchors, both driver project files,
  checkpoint importer schema/defaults and `RowTimeout`, the reviewed sibling
  `2026-11-13-card-1820-drift-checkpoints.md`, testing/build CARD-0585/0723/0931,
  performance standards R1-R9, orchestration D-2, and CARD-1836's S5c boundary.
  Changes to deciding code, composition, Windows execution and admission are
  excluded below. CARD-1782 category selection maps to V-18/R-4.

### Delivery inventory

None. S1 adds no asynchronous producer, queue, persistence/recovery path,
recipient or session input. The synchronous copied CLI's exit and parsed stdout
are the subject's actual result, not a substitute delivery receipt. No session
delivery assertion, UserPrompt transcript, live runner or broker is involved.

### Recipe, variants and oracle

Refine the existing argument test into fourteen thin public `[Test]` wrappers,
each with `[Arguments(false)]` and `[Arguments(true)]`, delegating to one private
`AlgorithmSourceDriftIsRefused(path, normalized)` helper. Wrapper names below are
proposed binding names, not existing executed tests. This only makes the plan's
28 argument results individually selectable; the production fix is unchanged.
Retain the other seven existing test methods. A bool argument counts once: two
executed results per file, 28 drift results, 35 results for the envelope class.
Do not retain the original four public argument results as duplicate executions.

Use the real copied CLI/module in a private committed repository under system
temp, outside the checkout. Copy all fourteen actual source files, create parent
directories, and extend only the fixture `.gitattributes` to:
`*.ps1 text eol=lf`, `*.cs text eol=lf`, `*.csproj text eol=lf`.
On Linux copy raw bytes; prove each clean raw hash equals its committed blob.
Do not silently normalize a dirty input into a clean fixture. Keep the fixture's
baseline and candidate commits fixed throughout each test.

For each result, the complete recipe is:

1. Invoke the copied selector clean. Require exit 0, a 64-hex `algorithmDigest`,
   and `AssertIncomplete`: empty classes, incomplete/Full, unverified target.
   Retain this digest as `before`; do not assert the expanded ordered oracle
   here, so a missing path reaches the dirty-source assertion before failing.
2. Read the selected file with `ReadAllBytesAsync`. Variant B (`false`) changes
   **exactly one existing byte**, upper to lower case at the comment anchor in
   the roster. Assert equal length, exactly one differing offset and the precise
   before/after byte. Do not replace the existing multi-byte appended comment
   and call it a one-byte witness. Variant W (`true`) rewrites LF to CRLF from
   the original bytes, preserving all non-newline bytes, encoding and BOM;
   assert it actually changed bytes. Both variants start from their own fixture.
3. Independently get `HEAD:path` and `git hash-object --no-filters -- path`.
   Require inequality. W additionally requires ordinary filtered `hash-object`
   equals `HEAD:path`. This proves Git text normalization hides whitespace drift.
   **The identity binds RAW BYTES, not tokens, semantics or normalized text.**
4. Invoke `SelectCopiedAlgorithmAsync`. Require `refused.Exit.ShouldBe(2)`,
   `ok=false`, `complete=false`, `fallback=Full`, error containing
   `algorithm source drift`, and absence of `algorithmDigest` (not null or an
   empty string). Failure to execute/parse is a fixture error, never a red witness.
5. Restore original bytes in this fixture and prove clean invocation succeeds
   with `algorithmDigest == before`. Reapply the same mutation and commit its
   **raw** blob in this scratch repository: `git hash-object -w --no-filters --
   <path>`, then `git update-index --cacheinfo 100644,<blob>,<path>`, then the
   existing bounded `CommitAsync` without another `git add`. Assert the new
   `HEAD:path` equals that raw blob. This matters for W: ordinary `git add` would
   remove CRLF and would not prove a changed committed identity.
6. Invoke the copied selector again. Require exit 0, the new fixture HEAD as
   `algorithmSha`, unchanged baseline/candidate, all conservative envelope
   assertions, and `after.ShouldNotBe(before)`. Independently construct
   `SHA256(UTF8(path + NUL + committedBlob + LF ...))` using all fourteen literal
   paths in roster order, and require `after.ShouldBe(expected)`.
   Never take the expected path list from production code or its returned digest.
7. Dispose the owned fixture even on failure. Retain case/variant identity,
   byte offset and original/mutated blob IDs in ignored diagnostic evidence.

The digest does **not** change to a new published value while source is dirty:
there must be no published digest then. Steps 3-4 prove raw-byte refusal; steps
5-6 separately prove committed raw-byte changes alter the production digest.
An independent hash calculation alone cannot satisfy either production oracle.

### Proves it works now

The following is the exact roster order from the plan. For every numbered row,
V-n is the entire recipe above, B and W are separate reported cases, and R-1
requires its dirty-source refusal. All filters are in `tests/Antiphon.Tests`.
Anchors are literal substrings within comments; change the anchor's initial
uppercase ASCII letter to its lowercase equivalent, not the comment delimiter.
If a collision resolution changes an anchor, bind a replacement comment byte
explicitly after Code; never fall back to editing executable syntax.

| V | Deciding file / what it touches | B anchor | Exact method filter | Before S1 / after S1 |
|---|---|---|---|---|
| V-1 | `scripts/impacted-classes.ps1`: selector entry and release CLI | `CARD-1782. Read-only` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_01_SelectorEntry` | B/W existing refusal green; expanded committed-digest oracle red / both green |
| V-2 | `scripts/lib/release-impact.ps1`: identity publisher | `CARD-1820 S1R1.` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_02_ReleaseModule` | B/W existing refusal green; expanded committed-digest oracle red / both green |
| V-3 | `scripts/lib/SelectorCensus.cs`: ordinary selector census | `No retained filesystem state` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_03_SelectorCensus` | B/W red at exit 0 vs 2 / both green |
| V-4 | `tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs`: uncapped relation | `Release-only positive relation` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_04_GraphBuilder` | B/W red at exit 0 vs 2 / both green |
| V-5 | `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs`: ordinary index; release Allowlist | `CARD-1820. Committed` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_05_IndexBuilder` | B/W red at exit 0 vs 2 / both green |
| V-6 | `tests/Shared/PlatformTaxonomy.cs`: ordinary index platform derivation | `CARD-1832. Method declaration` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_06_PlatformTaxonomy` | B/W red at exit 0 vs 2 / both green |
| V-7 | `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs`: tested project closure | `Conservative source inventory` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_07_ProjectGraph` | B/W red at exit 0 vs 2 / both green |
| V-8 | `tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeInputs.cs`: tested runtime positives | `Source-only runtime positives` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_08_RuntimeInputs` | B/W red at exit 0 vs 2 / both green |
| V-9 | `scripts/regenerate-derived-impact-index/Program.cs`: both driver modes | `Additive release-only artifact mode` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_09_IndexDriver` | B/W red at exit 0 vs 2 / both green |
| V-10 | `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`: driver compile closure | `Compile the same inputs` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_10_IndexProject` | B/W red at exit 0 vs 2 / both green |
| V-11 | `tools/Antiphon.LandGraphAudit/Program.cs`: release-inputs dispatch | `CARD-1483 S1:` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_11_EvaluatorDriver` | B/W red at exit 0 vs 2 / both green |
| V-12 | `tools/Antiphon.LandGraphAudit/ReleaseInputCommand.cs`: evaluator command | `Release-only evaluated inventory` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_12_EvaluatorCommand` | B/W red at exit 0 vs 2 / both green |
| V-13 | `tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs`: evaluated items/imports | `Actual evaluated, surviving items` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_13_Evaluator` | B/W red at exit 0 vs 2 / both green |
| V-14 | `tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj`: evaluator compile closure | `CARD-1483: real MSBuild` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_14_EvaluatorProject` | B/W red at exit 0 vs 2 / both green |

Reachability/classification: V-1/V-2 execute copied PowerShell; V-3 through V-14
are reached by the production identity's Git reads, not by executing/compiling
their copied C#/project code. All are Unit, assembly-local `ProcessSpawnLimit`,
real Git and pwsh integration at the identity boundary. A scratch edit of a C# or
csproj comment never builds that scratch tree. This proves binding, not graph
execution or transitive closure completeness.

- V-15: `AlgorithmDigestBindsCommittedBlobs` | same layer | CP-15 | fixed literal
  fourteen-path ordered oracle equals the emitted digest; a committed module edit
  changes it with endpoints fixed. Red before S1 at `digest.ShouldBe(expected)`.
- V-16: the six other existing envelope/fixture methods | CP-16 | incomplete/Full
  on missing index, stale inputs, mismatched candidate and ordinary-mode contrast;
  temp-root isolation and cleanup after failure. Green before/after S1.
- V-17: `CollisionAndAlgorithmCoverageForceFull` | Integration, real script via
  ScriptHarness | CP-17 | `explicit-incomplete-coverage`, each omitted-path case,
  and `no-coverage-override` pass. Green before/after; no admission source edit.
- V-18: inherited policy/mechanical/platform/layering/category and limiter
  censuses | CP-18/19 | named classes remain green, no fixture is a live stack.

### Guards the regression

- R-1: omission of any of fourteen deciding paths, or filtered hashing hiding
  CRLF drift | CP-1 through CP-14 | B/W each reach exit 2 and absent digest;
  committed raw B/W each change the emitted identity and equal the fixed oracle.
- R-2: sorted, truncated or detached identity; accidental release activation |
  CP-15/16 plus every clean branch of CP-1 through CP-14 | exact ordered digest,
  endpoint/algorithm SHA bindings, incomplete/Full, empty classes, null graph
  identities and unverified target remain asserted.
- R-3: broader digest mistaken for complete admission | CP-17 |
  `coverage.complete` stays false even with every static prerequisite covered;
  `release-graph-incomplete` remains. This test does not prove every possible
  S5c consumer refuses carry; the unchanged admission source boundary is required.
- R-4: fixture/test-lane regression | CP-16/18/19 | scratch location outside
  checkout, exceptional cleanup, Unit category and spawn limiter remain enforced.

### Guard inventory

Inventory scope is the S1 identity/refusal and conservative-output assertions,
including the adjacent admission freeze. Unchanged CLI option-validation,
process-custody and repository-wide meta-test internals retain their owners;
they are not new S1 guards. No S1 guard is omitted as untested.

| Guard | Plan reference and safety invariant | Distinct control |
|---|---|---|
| G-1 through G-14 (fourteen distinct guards) | D-1/D-3, roster position n: file n's disk bytes must match its committed blob before identity publication | PC-1 through PC-14 respectively, one-to-one |
| G-15 | D-3: raw hashing must bypass text filters | PC-15 |
| G-16 | D-1: digest binds every committed blob in exact roster order | PC-16 |
| G-17 | D-3: a drift refusal publishes no usable identity | PC-17 |
| G-18 | D-4/D-6: clean envelope stays incomplete | PC-18 |
| G-19 | D-4/D-6: clean envelope fallback stays Full | PC-19 |
| G-20 | D-4: no selected release classes | PC-20 |
| G-21 | D-4: target stays unverified | PC-21 |
| G-22 | D-4: graph/input/universe identities stay null | PC-22 |
| G-23 | D-6: covering the static admission inventory cannot grant completeness | PC-23 |

### Positive controls

These are **designed, unexecuted controls**, not ordinary byte-edit cases and not
claimed red/green evidence. The caller records the operator's 2026-11-12 mutation
execution waiver; no deliberate production mutants run in TestDesign or ordinary
Code. Retain the following recipes for Mutation after land if commissioned.
The waiver does not turn any pending control into a completed one or discharge M7.

- PC-n for n=1..14: break G-n by omitting only roster path n from the production
  identity array. The PowerShell still parses. Run the **exact** V-n method filter
  above (two bool results); expect its `refused.Exit.ShouldBe(2)` red with actual
  exit 0 after successful clean setup. Restore the path and both variants go green.
- PC-15: break G-15 by removing only `--no-filters` from the production disk hash.
  Exact method `AlgorithmSourceDriftIsRefused_01_SelectorEntry`: W red at
  `refused.Exit.ShouldBe(2)` with actual 0; B can remain green. Restore/green both.
- PC-16: break G-16 by swapping the first two production inventory paths.
  Exact method `AlgorithmDigestBindsCommittedBlobs` red at
  `digest.ShouldBe(expected)`; restore/green.
- PC-17: break G-17 by adding `algorithmDigest = ('0' * 64)` to the structured
  object in `Refuse-ReleaseImpact` (valid PowerShell, keep exit 2).
  Exact method `AlgorithmSourceDriftIsRefused_01_SelectorEntry` red at
  `TryGetProperty("algorithmDigest", out _).ShouldBeFalse`; restore/green.
- PC-18: break G-18 by setting the new envelope's complete bit true and removing
  that rejection clause from the JSON converter, leaving other validation.
  Exact method `MissingIndexIsIncomplete` red at
  `complete.ValueKind.ShouldBe(JsonValueKind.False)`; restore/green.
- PC-19: break G-19 by returning fallback `Scoped` and removing only its converter
  rejection. Exact method `MissingIndexIsIncomplete` red at
  `fallback.ShouldBe("Full")`; restore/green.
- PC-20: break G-20 by returning `classes=@('UnexpectedTests')` and removing its
  converter rejection. Exact method `MissingIndexIsIncomplete` red at
  `classes.GetArrayLength().ShouldBe(0)`; restore/green.
- PC-21: break G-21 by returning `target.verified=$true` and removing its converter
  rejection. Exact method `MissingIndexIsIncomplete` red at
  `target.verified.GetBoolean().ShouldBeFalse`; restore/green.
- PC-22: break G-22 by returning `universeDigest=('0' * 64)` and excluding only
  that name from the converter's null-identity loop. Exact method
  `MissingIndexIsIncomplete` red at
  `universeDigest.ValueKind.ShouldBe(JsonValueKind.Null)`; restore/green.
- PC-23: break G-23 by setting only `Get-RcAdmissionAlgorithmCoverage`'s return
  `complete=$true`. Exact method `CollisionAndAlgorithmCoverageForceFull` red at
  outer `RA CollisionAndAlgorithmCoverageForceFull.contract`, with inner
  `explicit-incomplete-coverage` / `no-coverage-override` false; restore/green.

All controls must compile/parse and reach these outcome assertions; build errors,
missing files, JSON failures, timeouts and zero tests do not count as red. Controls
18-22 require the paired converter relaxation so a different validation guard
does not stop the test before its specified assertion. Mutation uses only exact
`/*/*/Class/Method` filters, one leased native build per mutation/restoration,
then method-scoped red/restore/green; no class-wide mutation runs. SourceLanding
evidence and restoration records stay external and no snapshot commits occur.

### Out of scope

No canonicalizer, S1R5 closure, S1R6 composition, admission inventory repair,
coverage/admitted=true, carry credit, derived-index regeneration, CARD-1836 S5c,
native Windows W1, CARD-1866 A-2 or pending M7 controls. No live operational
change/restart. No migrations, DTO/routes, operation-label, remote-function or
statement-budget changes. Binding currently known files is not a complete graph.

### Existing tests affected

Changed class: `ReleaseImpactEnvelopeTests` (35 results after the wrapper split).
Adjacent named class/method: `RcHistoricalAdmissionTests.CollisionAndAlgorithmCoverageForceFull`.
CARD-1782: changed test glob selects `TestLaneCategoryGuardTests`; the release
module has no such table row. The new wrapper method names inherit the existing
Unit category and `ProcessSpawnLimit`. CP-18/19 name the inherited guard classes.
Migrations, public DTOs/routes, operation labels, remote script functions and
statement budgets: none. Ordinary selector/index outputs are not edited; their
large sibling relation/evaluator/legacy-index selections are excluded because S1
neither executes nor changes their behaviour. Index freshness consequences of
concurrent work belong to its owner, not a silent expansion of this slice.

### Checkpoints

Linux only. Every row has After=S1 and shares exactly one isolated
`Antiphon.Tests` build; CP-n reuse does not authorize another build. Serial rows
keep one process-spawning host at a time. Each CP has its own fresh
`rows/<CP>/run.trx`, including both bool cases; no inherited receipts.
`Min` is executed results, never inner assertions. `Expect` names are the method
or class tokens from that row's exact filter; require zero failed/skipped.

totalTimeoutMinutes: 68

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1882-s1/` | selector-entry | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_01_SelectorEntry` | V-1, R-1, R-2 | AlgorithmSourceDriftIsRefused_01_SelectorEntry; B/W, zero failed/skipped | 2 | 8 | true |
| CP-2 | S1 | CP-1 | release-module | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_02_ReleaseModule` | V-2, R-1, R-2 | AlgorithmSourceDriftIsRefused_02_ReleaseModule; B/W, zero failed/skipped | 2 | 1 | true |
| CP-3 | S1 | CP-1 | selector-census | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_03_SelectorCensus` | V-3, R-1, R-2 | AlgorithmSourceDriftIsRefused_03_SelectorCensus; B/W, zero failed/skipped | 2 | 1 | true |
| CP-4 | S1 | CP-1 | graph-builder | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_04_GraphBuilder` | V-4, R-1, R-2 | AlgorithmSourceDriftIsRefused_04_GraphBuilder; B/W, zero failed/skipped | 2 | 1 | true |
| CP-5 | S1 | CP-1 | index-builder | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_05_IndexBuilder` | V-5, R-1, R-2 | AlgorithmSourceDriftIsRefused_05_IndexBuilder; B/W, zero failed/skipped | 2 | 1 | true |
| CP-6 | S1 | CP-1 | platform-taxonomy | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_06_PlatformTaxonomy` | V-6, R-1, R-2 | AlgorithmSourceDriftIsRefused_06_PlatformTaxonomy; B/W, zero failed/skipped | 2 | 1 | true |
| CP-7 | S1 | CP-1 | project-graph | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_07_ProjectGraph` | V-7, R-1, R-2 | AlgorithmSourceDriftIsRefused_07_ProjectGraph; B/W, zero failed/skipped | 2 | 1 | true |
| CP-8 | S1 | CP-1 | runtime-inputs | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_08_RuntimeInputs` | V-8, R-1, R-2 | AlgorithmSourceDriftIsRefused_08_RuntimeInputs; B/W, zero failed/skipped | 2 | 1 | true |
| CP-9 | S1 | CP-1 | index-driver | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_09_IndexDriver` | V-9, R-1, R-2 | AlgorithmSourceDriftIsRefused_09_IndexDriver; B/W, zero failed/skipped | 2 | 1 | true |
| CP-10 | S1 | CP-1 | index-project | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_10_IndexProject` | V-10, R-1, R-2 | AlgorithmSourceDriftIsRefused_10_IndexProject; B/W, zero failed/skipped | 2 | 1 | true |
| CP-11 | S1 | CP-1 | evaluator-driver | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_11_EvaluatorDriver` | V-11, R-1, R-2 | AlgorithmSourceDriftIsRefused_11_EvaluatorDriver; B/W, zero failed/skipped | 2 | 1 | true |
| CP-12 | S1 | CP-1 | evaluator-command | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_12_EvaluatorCommand` | V-12, R-1, R-2 | AlgorithmSourceDriftIsRefused_12_EvaluatorCommand; B/W, zero failed/skipped | 2 | 1 | true |
| CP-13 | S1 | CP-1 | evaluator | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_13_Evaluator` | V-13, R-1, R-2 | AlgorithmSourceDriftIsRefused_13_Evaluator; B/W, zero failed/skipped | 2 | 1 | true |
| CP-14 | S1 | CP-1 | evaluator-project | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_14_EvaluatorProject` | V-14, R-1, R-2 | AlgorithmSourceDriftIsRefused_14_EvaluatorProject; B/W, zero failed/skipped | 2 | 1 | true |
| CP-15 | S1 | CP-1 | ordered-identity | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmDigestBindsCommittedBlobs` | V-15, R-2 | AlgorithmDigestBindsCommittedBlobs; zero failed/skipped | 1 | 1 | true |
| CP-16 | S1 | CP-1 | envelope-fixture | `/*/*/ReleaseImpactEnvelopeTests/(Fixture_repositories_live_under_system_temp_outside_the_checkout*)\|(MissingIndexIsIncomplete*)\|(StaleInputIsIncomplete*)\|(CandidateMismatchIsIncomplete*)\|(OrdinaryModeRemainsAdvisory*)\|(FixtureCleansUpAfterFailure*)` | V-16, R-2, R-4 | all six listed methods, zero failed/skipped | 6 | 2 | true |
| CP-17 | S1 | CP-1 | admission-freeze | `/*/*/RcHistoricalAdmissionTests/CollisionAndAlgorithmCoverageForceFull` | V-17, R-3 | CollisionAndAlgorithmCoverageForceFull; zero failed/skipped | 1 | 1 | true |
| CP-18 | S1 | CP-1 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-18, R-4 | all nine listed classes, zero failed/skipped | 78 | 3 | true |
| CP-19 | S1 | CP-1 | spawn-limiter | `/*/*/ProcessSpawnLimitTests/*` | V-18, R-4 | ProcessSpawnLimitTests; zero failed/skipped | 57 | 1 | true |

Union: all 35 envelope results, one admission case, nine inherited guard classes
with floor 78, and limiter floor 57: total minimum 171 executed results. Guard
floors come from the reviewed sibling manifest, not assertion counts. Code binds
them to fresh TRX and reports actuals; do not lower a floor to conceal a failure.

Execution protocol: commit failing tests before the production edit and run the
same CP-1..CP-15 selection at that red commit. CP-3..CP-14 must fail at the exit
assertion (24 results); CP-1/2's committed oracle and CP-15 fail on the fourteen-
path digest. Do not accept clean-setup failure as this evidence. Restore green
production only in a later commit, push, then run CP-1..CP-19 once at that SHA.
Red and green commits need separately owned outputs/run IDs. The table's
`bin-c1882-s1/` is reserved for the final green run. For red-first preparation,
import this table into ignored YAML and change only its one build's outputPath
to `bin-c1882-s1-red/`; retain that exact manifest as the declared diagnostic
substitute and run CP-1..CP-15 at the committed red SHA. Preserve its nonzero
outcome; this is expected-red evidence, not a green checkpoint certificate.
Filters, result floors and assertions are identical to the table. This single
output-only red substitute is authorized here and included in Cost; no filter
hunting or additional suite is authorized. Never overwrite either build-source
stamp or delete the retained red output using a generic clean command.

Use the checkpoint tool with its bootstrap build through the host build-slot
gate, native output for that bootstrap, no direct alternate-output build. Release
the bootstrap lease before starting the scheduler; it takes a separate lease for
each build and test driver. For example, after committing S1:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1882-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:UseAppHost=false
dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-11-16-card-1882-testdesign.md --after S1 --expected-source-sha <full-committed-code-sha>
```

The second command executes the built scheduling tool, which owns its driver
leases; it is not an unleased direct build/test command. Wait on exit 75 using
the tool's returned run ID until terminal;
never abandon an active run or retry unleased. Preserve all raw CHECKPOINT lines,
SHA/source/build provenance, per-row fresh TRX, counts and slot/execution timing.
Run evidence-diff over the entire Code base..HEAD; fetch master and use read-only
`git merge-tree --write-tree origin/master HEAD`, never merge to resolve.

### Performance budget

All estimates are **provisional**, owned by CARD-1882/CARD-1404; no expanded-test
Linux p95 has been measured. Per-file rows CP-2..CP-14 each budget 1 minute for
two argument results, CLI/Git phases and discovery; CP-1 budgets the same minute
plus 7 minutes for one cold isolated build. CP-15=1, CP-16=2, CP-17=1,
CP-18=3, CP-19=1 minutes. The sibling estimated its smaller envelope class at
3 minutes and recorded cold build observations of 291-426 seconds. These are
provenance for provisional estimates, not a claimed measured p95. The extra
committed-byte checks and fourteen test-host discoveries are included provisionally.
CP-17's historical approximately 17-second case supports its 1-minute estimate,
also not a comparable p95 (`2026-11-15-why-missed-g136-rcbaseline-timeout.md`).

Measurement plan: collect build, discovery, every B/W case, Git, pwsh and cleanup
durations from the first required red and green runs; retain host/platform/load,
source SHA, group/case, slot wait and `C1820 phase=*` timings with TRX under the
ignored checkpoint run. Use subsequent ordinary Review/qualification observations
to reach at least three comparable green samples per row; no sample-only repeats.
Report unavailable filesystem spans as unknown/remainder, never zero. Re-estimate
at roughly 2x observed p95 after enough samples. If a row or the selection exceeds
30 minutes, preserve attribution and commission Debug; do not silently widen it.

Timeout qualification: the table importer actually derives CP-1=24 minutes and
CP-2..CP-19=15 minutes (`max(15,3*estimate)`), not the desired measured 2x budgets.
Record that tooling-floor exception under CARD-1882/CARD-1404 until measurements
and an explicitly imported execution-manifest timeout amendment permit tightening;
do not describe these defaults as measured R5 compliance. Desired provisional
execution caps are CP-1 build 16 minutes, each drift/identity row 2 minutes,
CP-16 4, CP-17 2, CP-18 6, CP-19 2. The current command uses the declared importer
defaults; it does not silently apply an unlisted YAML amendment. Child bounds stay
git 60s, pwsh 120s, output drain 10s, process reap and teardown 30s. Each scratch
repository must be fresh because its bytes/index/commits are the subject; there
is no AppHost, DB, container, migrations or shared expensive startup to replay.
No timeout increases are part of the proposed source edits.

Admission is separately bounded by the checkpoint default 45 minutes per slot
acquisition. Execution allowance is `max(30,2*29+10)=68` minutes, not an expected
duration. No total-timeout override is used. With one admission allowance the
tool's default independent hard-wall budget is 113 minutes; actual admission-only
union and execution are reported separately. The expected serial ordinary total
is 29 minutes, close to R1; the first run must measure that boundary.

### Cost

- Ordinary V/R floor (Code), estimated: CP-1 8 + CP-2..14 13 + CP-15 1 + CP-16 2
  + CP-17 1 + CP-18 3 + CP-19 1 = **29 minutes**. This includes the isolated build
  (7) and all ordinary row work (22), not authoring or queue time.
- Red-first floor, estimated: CP-1..15 = **22 minutes** including a separate
  source-bound build. Both variants for every file are ordinary regression tests;
  the mutation waiver does not waive this red-first production evidence.
- Tool bootstrap/setup allowance, estimated **2 minutes**. Commissioned Code
  total = 2 + 22 red + 29 green = **53 minutes**, excluding authoring/admission.
  Each individual run targets less than 30 minutes. Design-stage actual builds,
  tests and mutations: **0**, documentation inspection only.
- PC floor (Mutation), if commissioned: PC-1..15 and PC-17 each select their
  exact two-result method; PC-16 and PC-18..23 each select one exact method.
  Budget **1 minute per selected red or restored-green method run**. With a
  7-minute leased build for each red and each restoration: 23 * (7+1+7+1) =
  **368 minutes**, plus 2 bootstrap/setup = **370 minutes**. No mutation batching
  savings assumed because most controls edit the same production file. A task
  of that size needs separately commissioned bounded batches/Debug under R1.
  The stated execution waiver schedules **0 minutes of PC execution** on this
  commission; it does not change that estimated non-waived floor. Full
  non-waived Code+Mutation estimate = 53 + 370 = **423 minutes**.
- Sharing one build across the 19 ordinary rows avoids 18 * 7 = **126 estimated
  minutes** versus rebuilding per row. No full-suite cost saving is claimed;
  excluded suites do not exercise a changed S1 behaviour. Per-file grouping
  trades fourteen host discoveries for precise receipt identity; phase data must
  confirm the provisional 1-minute groups.

### Binding and handoff audit

Before execution, Code binds the proposed wrapper names, B/W argument roster,
comment anchors/byte offsets, independent digest oracle and exact source SHA to
the implemented bodies and generated manifest. Review reads those bodies and
fresh TRX; names alone are unbound payloads, not evidence. Any non-equivalent
test/filter/timeout change needs an explicit manifest amendment before credit.

Guards=23; mapped=23; missing=0; duplicate PC maps=0. Designed controls=23;
executed controls=0 (design stage; stated mutation waiver). Every non-waived PC
must run break/red/restore/green after land; none is represented as already run.
Required V/R execution is pending Code, including ordinary red-first evidence.
Next: Code after the named source collision hold clears; no human product choice
or permission is needed to implement the already authorized slice.
