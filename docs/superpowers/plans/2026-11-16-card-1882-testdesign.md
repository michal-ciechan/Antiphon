# CARD-1882 S1: Linux algorithm-identity checkpoint manifest

Design only; no production or test source changed and no execution claimed.
Amendment 1 (this file, TestDesign f8dd5dd4) follows amended Plan dc76c20f,
commit `81e157eab125611c97c095fe785fa6da8cc69a6c`, artifact
`docs/superpowers/plans/2026-11-16-card-1882-algorithm-digest-plan.md` on
`origin/feat/card-task-dc76c20f`. That plan answers Review b65e738f, commit
`0b502891d76a751be519cddac07173509f802fc9`. Plan D-1 through D-8 and the S1
edit set remain authoritative. The first manifest followed Plan e2dd6b0a,
commit `6de02276d3d963bcf617e10a42872bff927b4cd6`. Master fetched and read for
this amendment: `18eabe4ce48a46adea27aa9cbb410415eae9ac91`. The first manifest
read `48a4615dcb4d67033886a3e31629dc7351ed0852`; the plan re-checked cited
source lines and records that the later master adds only CARD-1883 docs.

S1 edits the identity path array in `scripts/lib/release-impact.ps1`, the
envelope fixture and witnesses in
`tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs`, and regenerates
`tests/derived-impact-index.json` with the owner driver (D-8). Copying a
deciding file into a fixture does not authorize editing that source file. Keep
`coverage.complete=false`, `admitted=false`, `gateCredit=false`, and the release
envelope incomplete/Full. Do not compose S1R6 or call graph/evaluator/index code
from the envelope. No canonicalizer or script-closure module exists at this SHA;
do not invent an inventory entry for one. Do not hand-edit the index JSON.

## Collisions

Do not dispatch S1 while any owner below holds the named files. Never hand-merge
across them. A hold is not a passing receipt. After the owner releases, run the
rows that waited; do not silently run against mixed tips.

| Owner | What it holds that S1 also edits or reads | Rows and steps that wait |
|---|---|---|
| CARD-1820 S1R5, script-closure (not started on `18eabe4ce`) | Adds `scripts/lib/release-impact-scripts.ps1` and fixture scripts. S1R6 then wires that module into `scripts/lib/release-impact.ps1`, so it becomes deciding file 19 and a new identity path. | Drift and identity rows CP-1..CP-15 and CP-20..CP-23. If S1R5 lands first, its module joins this roster only through a re-plan. Never add it silently. |
| CARD-1820 S1R6, composition | `scripts/lib/release-impact.ps1`, `scripts/impacted-classes.ps1`, `ReleaseImpactEnvelopeTests.cs` and the graph driver. | The whole S1 edit set and every CP row. S1 and S1R6 serialize. The second to land rebases onto the first's path array and fixture copy set. |
| CARD-1836 S5c, Code `d2fff5d4` (in flight, tip `7154ce531`) | `tests/derived-impact-index.json`. Commit `34e73d5d1` regenerates the ScriptHarness and ScriptHarnessProcess cap rows. | The S1 regeneration step and CP-24. They wait until d2fff5d4 lands, or the caller records that it released the index file. Then regenerate on that integrated base. Because the S1 commit carries the regenerated index, the S1 dispatch itself waits on this owner. |
| CARD-1836 admission activation (any later slice that edits `scripts/lib/rc-admission.ps1` or sets `coverage.complete`) | The admission boundary CP-17 freezes. | CP-17. S1 never edits `rc-admission.ps1`. Activation must not read the broader digest as carry credit (D-6). |

CP-3..CP-10 still bind the selector/index/relation area directly and wait with
the other drift rows on S1R5 and S1R6. CP-17 gives no permission to edit S5c.

## Verification design

### Inspection

- Read every body in `ReleaseImpactEnvelopeTests`, including `AssertIncomplete`,
  `AssertRefused`, `Fixture.CreateAsync`, both selector launch paths, Git/commit,
  process/drain/cleanup bounds, and `RepoRoot`. These are the nearest fixture;
  there is no separate fixture file. Boundaries: V-1 through V-23, R-1 through
  R-5. In particular, drift must call `SelectCopiedAlgorithmAsync`; `SelectAsync`
  executes checkout source and cannot witness drift in a scratch copy.
- Read `Get-ReleaseImpactAlgorithmIdentity`, `New-ReleaseImpactEnvelope`,
  `ConvertTo-ReleaseImpactJson`, the selector's early release branch and
  `Refuse-ReleaseImpact`: raw disk/commit comparison, ordered digest, CLI JSON
  refusal, and the conservative envelope map to V-1 through V-16 and R-1/R-2.
- Read `RcHistoricalAdmissionTests`, its ScriptHarness configuration,
  `test-rc-admission.ps1`'s `CollisionAndAlgorithmCoverageForceFull` case and
  `Get-RcAdmissionAlgorithmCoverage`: V-17/R-3. The separate admission digest
  fixture is not a substitute for the envelope identity test.
- Read the eighteen files' comment edit anchors, both driver project files,
  `Directory.Build.props`, `Directory.Build.targets`,
  `tests/Shared/CheckpointRepeat.targets`,
  `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj`,
  `DerivedImpactIndexTests.committed_index_matches_the_builder`, checkpoint
  importer schema/defaults and `RowTimeout`, the reviewed sibling
  `2026-11-13-card-1820-drift-checkpoints.md`, testing/build CARD-0585/0723/0931,
  performance standards R1-R9, orchestration D-2, and CARD-1836's S5c boundary.
  Changes to deciding code, composition, Windows execution and admission are
  excluded below. CARD-1782 category selection maps to V-18/R-4. The regenerated
  index maps to V-23/R-5.

### Delivery inventory

None. S1 adds no asynchronous producer, queue, persistence/recovery path,
recipient or session input. The synchronous copied CLI's exit and parsed stdout
are the subject's actual result, not a substitute delivery receipt. No session
delivery assertion, UserPrompt transcript, live runner or broker is involved.

### Recipe, variants and oracle

Refine the existing argument test into eighteen thin public `[Test]` wrappers,
each with `[Arguments(false)]` and `[Arguments(true)]`, delegating to one private
`AlgorithmSourceDriftIsRefused(path, normalized)` helper. Wrapper names below are
proposed binding names, not existing executed tests. This only makes the plan's
36 argument results individually selectable; the production fix is unchanged.
Retain the other seven existing test methods. A bool argument counts once: two
executed results per file, 36 drift results, 43 results for the envelope class.
Do not retain the original four public argument results as duplicate executions.

Use the real copied CLI/module in a private committed repository under system
temp, outside the checkout. Copy all eighteen actual source files, create parent
directories, and extend only the fixture `.gitattributes` to these five lines:
`*.ps1 text eol=lf`, `*.cs text eol=lf`, `*.csproj text eol=lf`,
`*.props text eol=lf`, `*.targets text eol=lf`. Do not change the production
`.gitattributes`. The fixture copy is RAW-BYTE: `File.ReadAllBytes` then
`File.WriteAllBytes`. Do not use `ReadAllText`, `WriteAllText`, or the async
text APIs. Those strip the BOM the twelve `.cs` targets carry and fold CRLF to
LF, so the clean raw hash would no longer equal the copied committed blob.
Prove each clean raw hash equals its committed blob. Do not silently normalize
a dirty input into a clean fixture. Keep the fixture's baseline and candidate
commits fixed throughout each test. The fixture never builds, so
`Directory.Build.props` and `Directory.Build.targets` at its root are bytes
only. The baseline-to-candidate diff in `OrdinaryModeRemainsAdvisory` stays
`input.txt` only, so the allowlist is not triggered there.

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
   `SHA256(UTF8(path + NUL + committedBlob + LF ...))` using all eighteen literal
   paths in roster order, and require `after.ShouldBe(expected)`.
   Never take the expected path list from production code or its returned digest.
7. Dispose the owned fixture even on failure. Retain case/variant identity,
   byte offset and original/mutated blob IDs in ignored diagnostic evidence.

The digest does **not** change to a new published value while source is dirty:
there must be no published digest then. Steps 3-4 prove raw-byte refusal; steps
5-6 separately prove committed raw-byte changes alter the production digest.
An independent hash calculation alone cannot satisfy either production oracle.

### Proves it works now

The following is the exact roster order from the amended plan. Positions 1-14
are unchanged. Positions 15-18 are the Amendment 1 MSBuild files. For every
numbered file row, V-n is the entire recipe above, B and W are separate
reported cases, and R-1 requires its dirty-source refusal. All filters are in
`tests/Antiphon.Tests`. Anchors are literal substrings within comments; change
the anchor's initial uppercase ASCII letter to its lowercase equivalent, not
the comment delimiter. Each anchor below is unique in its file on
`18eabe4ce48a46adea27aa9cbb410415eae9ac91`. The V-6 anchor is the longer Linux
sentence, because `CARD-1832. Method declaration` also appears on the Windows
attribute (A-1). If a collision resolution changes an anchor, bind a
replacement comment byte explicitly after Code; never fall back to editing
executable syntax.

| V | Deciding file / what it touches | B anchor | Exact method filter | Before S1 / after S1 |
|---|---|---|---|---|
| V-1 | `scripts/impacted-classes.ps1`: selector entry and release CLI | `CARD-1782. Read-only` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_01_SelectorEntry` | B/W existing refusal green; expanded committed-digest oracle red / both green |
| V-2 | `scripts/lib/release-impact.ps1`: identity publisher | `CARD-1820 S1R1.` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_02_ReleaseModule` | B/W existing refusal green; expanded committed-digest oracle red / both green |
| V-3 | `scripts/lib/SelectorCensus.cs`: ordinary selector census | `No retained filesystem state` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_03_SelectorCensus` | B/W red at exit 0 vs 2 / both green |
| V-4 | `tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs`: uncapped relation | `Release-only positive relation` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_04_GraphBuilder` | B/W red at exit 0 vs 2 / both green |
| V-5 | `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs`: ordinary index; release Allowlist | `CARD-1820. Committed` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_05_IndexBuilder` | B/W red at exit 0 vs 2 / both green |
| V-6 | `tests/Shared/PlatformTaxonomy.cs`: ordinary index platform derivation | `CARD-1832. Method declaration that the Linux lane` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_06_PlatformTaxonomy` | B/W red at exit 0 vs 2 / both green |
| V-7 | `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs`: tested project closure | `Conservative source inventory` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_07_ProjectGraph` | B/W red at exit 0 vs 2 / both green |
| V-8 | `tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeInputs.cs`: tested runtime positives | `Source-only runtime positives` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_08_RuntimeInputs` | B/W red at exit 0 vs 2 / both green |
| V-9 | `scripts/regenerate-derived-impact-index/Program.cs`: both driver modes | `Additive release-only artifact mode` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_09_IndexDriver` | B/W red at exit 0 vs 2 / both green |
| V-10 | `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`: driver compile closure | `Compile the same inputs` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_10_IndexProject` | B/W red at exit 0 vs 2 / both green |
| V-11 | `tools/Antiphon.LandGraphAudit/Program.cs`: release-inputs dispatch | `CARD-1483 S1:` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_11_EvaluatorDriver` | B/W red at exit 0 vs 2 / both green |
| V-12 | `tools/Antiphon.LandGraphAudit/ReleaseInputCommand.cs`: evaluator command | `Release-only evaluated inventory` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_12_EvaluatorCommand` | B/W red at exit 0 vs 2 / both green |
| V-13 | `tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs`: evaluated items/imports | `Actual evaluated, surviving items` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_13_Evaluator` | B/W red at exit 0 vs 2 / both green |
| V-14 | `tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj`: evaluator compile closure | `CARD-1483: real MSBuild` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_14_EvaluatorProject` | B/W red at exit 0 vs 2 / both green |
| V-19 | `Directory.Build.props`: both drivers; `DefaultItemExcludes` feeds the default Compile glob | `Keep MSBuild's default item globs` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_15_BuildProps` | B/W red at exit 0 vs 2 / both green |
| V-20 | `Directory.Build.targets`: both drivers; imports CheckpointRepeat.targets and a conditioned Compile item | `CARD-0222: cap the growth` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_16_BuildTargets` | B/W red at exit 0 vs 2 / both green |
| V-21 | `tests/Shared/CheckpointRepeat.targets`: both drivers, imported by the targets file; conditioned Compile items | `The exact selected csproj alone` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_17_CheckpointRepeatTargets` | B/W red at exit 0 vs 2 / both green |
| V-22 | `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj`: evaluator ProjectReference; default glob compiles Core sources | `Clock and approval orchestration` | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_18_EvaluatorCoreProject` | B/W red at exit 0 vs 2 / both green |

Reachability/classification: V-1/V-2 execute copied PowerShell; V-3 through V-14
and V-19 through V-22 are reached by the production identity's Git reads, not by
executing or compiling their copied C#, project or MSBuild bytes. All four new
files are LF with no BOM on the master SHA above. All are Unit, assembly-local
`ProcessSpawnLimit`, real Git and pwsh integration at the identity boundary. A
scratch edit of a comment, props or targets file never builds that scratch tree.
This proves binding, not graph execution or transitive closure completeness.

- V-15: `AlgorithmDigestBindsCommittedBlobs` | same layer | CP-15 | fixed literal
  eighteen-path ordered oracle equals the emitted digest; a committed module edit
  changes it with endpoints fixed. Red before S1 at `digest.ShouldBe(expected)`.
- V-16: the six other existing envelope/fixture methods | CP-16 | incomplete/Full
  on missing index, stale inputs, mismatched candidate and ordinary-mode contrast;
  temp-root isolation and cleanup after failure. Green before/after S1.
- V-17: `CollisionAndAlgorithmCoverageForceFull` | Integration, real script via
  ScriptHarness | CP-17 | `explicit-incomplete-coverage`, each omitted-path case,
  and `no-coverage-override` pass. Green before/after; no admission source edit.
- V-18: inherited policy/mechanical/platform/layering/category and limiter
  censuses | CP-18/19 | named classes remain green, no fixture is a live stack.
- V-23: `DerivedImpactIndexTests.committed_index_matches_the_builder` | CP-24 |
  after the owner regeneration, the committed `tests/derived-impact-index.json`
  bytes equal `DerivedImpactIndexBuilder.Build`. Not part of the red-first run.
  The index is regenerated only after the final envelope-test edit, on the
  integrated base that already holds S5c. Green only after that regeneration
  commit. A hand-edited JSON is not this witness.

### Guards the regression

- R-1: omission of any of eighteen deciding paths, or filtered hashing hiding
  CRLF drift | CP-1 through CP-14 and CP-20 through CP-23 | B/W each reach exit 2
  and absent digest; committed raw B/W each change the emitted identity and
  equal the fixed oracle.
- R-2: sorted, truncated or detached identity; accidental release activation |
  CP-15/16 plus every clean branch of CP-1 through CP-14 and CP-20 through CP-23 |
  exact ordered eighteen-path digest, endpoint/algorithm SHA bindings,
  incomplete/Full, empty classes, null graph identities and unverified target
  remain asserted.
- R-3: broader digest mistaken for complete admission | CP-17 |
  `coverage.complete` stays false even with every static prerequisite covered;
  `release-graph-incomplete` remains. This test does not prove every possible
  S5c consumer refuses carry; the unchanged admission source boundary is required.
- R-4: fixture/test-lane regression | CP-16/18/19 | scratch location outside
  checkout, exceptional cleanup, Unit category and spawn limiter remain enforced.
- R-5: new tracked-path literals in `ReleaseImpactEnvelopeTests` leave
  `tests/derived-impact-index.json` stale | CP-24 | committed bytes equal the
  builder after the owner script's fixed point. This row does not run before
  that regeneration commit.

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
| G-24 | D-2/D-3, roster position 15: `Directory.Build.props` disk bytes must match its committed blob before identity publication | PC-24 |
| G-25 | D-2/D-3, roster position 16: `Directory.Build.targets` disk bytes must match its committed blob before identity publication | PC-25 |
| G-26 | D-2/D-3, roster position 17: `tests/Shared/CheckpointRepeat.targets` disk bytes must match its committed blob before identity publication | PC-26 |
| G-27 | D-2/D-3, roster position 18: `Antiphon.LandGraphAudit.Core.csproj` disk bytes must match its committed blob before identity publication | PC-27 |

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
- PC-24: break G-24 by omitting only `Directory.Build.props` from the production
  identity array. Exact method `AlgorithmSourceDriftIsRefused_15_BuildProps`:
  both results red at `refused.Exit.ShouldBe(2)` with actual exit 0; restore/green.
- PC-25: break G-25 by omitting only `Directory.Build.targets`. Exact method
  `AlgorithmSourceDriftIsRefused_16_BuildTargets`: both results red at
  `refused.Exit.ShouldBe(2)` with actual exit 0; restore/green.
- PC-26: break G-26 by omitting only `tests/Shared/CheckpointRepeat.targets`.
  Exact method `AlgorithmSourceDriftIsRefused_17_CheckpointRepeatTargets`: both
  results red at `refused.Exit.ShouldBe(2)` with actual exit 0; restore/green.
- PC-27: break G-27 by omitting only
  `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj`.
  Exact method `AlgorithmSourceDriftIsRefused_18_EvaluatorCoreProject`: both
  results red at `refused.Exit.ShouldBe(2)` with actual exit 0; restore/green.
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
coverage/admitted=true, carry credit, hand-editing `tests/derived-impact-index.json`,
CARD-1836 S5c's own plan/status edits, native Windows W1, CARD-1866 A-2 or
pending M7 controls. Owner regeneration of that JSON is in S1 (D-8), not out of
scope. No live operational change/restart. No migrations, DTO/routes,
operation-label, remote-function or statement-budget changes. Binding the
eighteen files is not a complete graph. `global.json`, `Directory.Build.rsp`,
`EvaluationBudget.cs` and the other Core sources stay unbound for the reasons
in amended plan D-2; this manifest does not add witnesses for them.

### Existing tests affected

Changed class: `ReleaseImpactEnvelopeTests` (43 results after the wrapper split).
Adjacent named class/method: `RcHistoricalAdmissionTests.CollisionAndAlgorithmCoverageForceFull`.
Committed derived output: `tests/derived-impact-index.json`, regenerated by
`scripts/regenerate-derived-impact-index.ps1` and guarded by the existing method
`DerivedImpactIndexTests.committed_index_matches_the_builder` (one result).
CARD-1782: changed test glob selects `TestLaneCategoryGuardTests`; the release
module has no such table row. The new wrapper method names inherit the existing
Unit category and `ProcessSpawnLimit`. CP-18/19 name the inherited guard classes.
Migrations, public DTOs/routes, operation labels, remote script functions and
statement budgets: none. Ordinary selector behaviour is not edited. The index
JSON changes only through the owner script, after the envelope-test literals
exist, on the S5c-integrated base. Large sibling relation/evaluator selections
stay excluded because S1 neither executes nor changes their behaviour.

### Checkpoints

Linux only. Two runs, two After groups, one isolated `Antiphon.Tests` build
each. The tool refuses to reuse a build across After groups, and a second build
of the same project in the same After group is only an equivalent-build warning.
`S1b` is the guard-run selector, not a second production slice. Both runs
require the S1 commit. Do not pass `--after all`: that would schedule both
builds in one run and exceed the 25-minute estimate cap.

- Drift run, `--after S1`: CP-1..CP-15 and CP-20..CP-23. Build
  `bin-c1882-s1/`. Estimated sum 25 minutes.
- Guard run, `--after S1b`: CP-16..CP-19 and CP-24. Build
  `bin-c1882-s1b/`. Estimated sum 17 minutes. CP-24 runs only after the
  regeneration commit below.

Serial rows keep one process-spawning host at a time. Each CP has its own fresh
`rows/<CP>/run.trx`, including both bool cases; no inherited receipts.
`Min` is executed results, never inner assertions. `Expect` names are the method
or class tokens from that row's exact filter; require zero failed/skipped.
No `totalTimeoutMinutes` line. Each run lets the tool derive its allowance:
drift `max(30, 2*25+10)=60` minutes, guard `max(30, 2*17+10)=44` minutes.
Those allowances are not expected durations.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1882-s1/` | selector-entry | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_01_SelectorEntry` | V-1, R-1, R-2 | AlgorithmSourceDriftIsRefused_01_SelectorEntry; B/W, zero failed/skipped | 2 | 7 | true |
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
| CP-16 | S1b | `tests/Antiphon.Tests -> bin-c1882-s1b/` | envelope-fixture | `/*/*/ReleaseImpactEnvelopeTests/(Fixture_repositories_live_under_system_temp_outside_the_checkout*)\|(MissingIndexIsIncomplete*)\|(StaleInputIsIncomplete*)\|(CandidateMismatchIsIncomplete*)\|(OrdinaryModeRemainsAdvisory*)\|(FixtureCleansUpAfterFailure*)` | V-16, R-2, R-4 | all six listed methods, zero failed/skipped | 6 | 9 | true |
| CP-17 | S1b | CP-16 | admission-freeze | `/*/*/RcHistoricalAdmissionTests/CollisionAndAlgorithmCoverageForceFull` | V-17, R-3 | CollisionAndAlgorithmCoverageForceFull; zero failed/skipped | 1 | 1 | true |
| CP-18 | S1b | CP-16 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-18, R-4 | all nine listed classes, zero failed/skipped | 78 | 3 | true |
| CP-19 | S1b | CP-16 | spawn-limiter | `/*/*/ProcessSpawnLimitTests/*` | V-18, R-4 | ProcessSpawnLimitTests; zero failed/skipped | 57 | 1 | true |
| CP-20 | S1 | CP-1 | build-props | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_15_BuildProps` | V-19, R-1, R-2 | AlgorithmSourceDriftIsRefused_15_BuildProps; B/W, zero failed/skipped | 2 | 1 | true |
| CP-21 | S1 | CP-1 | build-targets | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_16_BuildTargets` | V-20, R-1, R-2 | AlgorithmSourceDriftIsRefused_16_BuildTargets; B/W, zero failed/skipped | 2 | 1 | true |
| CP-22 | S1 | CP-1 | repeat-targets | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_17_CheckpointRepeatTargets` | V-21, R-1, R-2 | AlgorithmSourceDriftIsRefused_17_CheckpointRepeatTargets; B/W, zero failed/skipped | 2 | 1 | true |
| CP-23 | S1 | CP-1 | evaluator-core-project | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_18_EvaluatorCoreProject` | V-22, R-1, R-2 | AlgorithmSourceDriftIsRefused_18_EvaluatorCoreProject; B/W, zero failed/skipped | 2 | 1 | true |
| CP-24 | S1b | CP-16 | index-fresh | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | V-23, R-5 | committed_index_matches_the_builder; zero failed/skipped | 1 | 3 | true |

Union: all 43 envelope results, one admission case, nine inherited guard classes
with floor 78, limiter floor 57, and one index result: total minimum 180
executed results (43+1+78+57+1). Guard floors come from the reviewed sibling
manifest, not assertion counts. Code binds them to fresh TRX and reports
actuals; do not lower a floor to conceal a failure. Drift-run minimum is 37
(36 drift results plus CP-15). Guard-run minimum is 143.

Execution protocol: commit failing tests before the production edit and run the
drift selection CP-1..CP-15 and CP-20..CP-23 at that red commit. CP-3..CP-14 and
CP-20..CP-23 must fail at the exit assertion (32 results); CP-1, CP-2 and CP-15
fail on the eighteen-path digest. Do not accept clean-setup failure as this
evidence. CP-24 is not part of the red-first run.

Restore green production only in a later commit. Then regenerate the index, and
only after S5c (d2fff5d4) has landed or the caller has recorded that it released
`tests/derived-impact-index.json`. Regenerate on that integrated base, never by
hand-merge:

1. `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1882-index -- pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1`. The first run must report that the bytes changed.
2. `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1882-index-2 -- pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1 -NoBuild`. This second lease must report that the bytes are unchanged. If they changed again, stop; the index has not settled.
3. Commit only the JSON. The driver's `bin/` and `obj/` stay gitignored.

If S5c lands after the S1 commit and before S1 lands, regenerate again on the
new base, in a new commit. Then run the two green checkpoint runs once at that
SHA: `--after S1`, then `--after S1b`. Red and green commits need separately
owned outputs/run IDs. The table's `bin-c1882-s1/` is reserved for the final
green drift run, and `bin-c1882-s1b/` for the final green guard run. For
red-first preparation, import this table into ignored YAML, keep only the
After=S1 rows, and change only that build's outputPath to `bin-c1882-s1-red/`.
Retain that exact manifest as the declared diagnostic substitute and run
CP-1..CP-15 and CP-20..CP-23 at the committed red SHA. Preserve its nonzero
outcome; this is expected-red evidence, not a green checkpoint certificate.
Filters, result floors and assertions are identical to those rows. This single
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
# S1b only after the derived-index JSON commit described above.
dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-11-16-card-1882-testdesign.md --after S1b --expected-source-sha <full-committed-code-sha>
```

Run the drift rows and the guard rows as two separate `run` invocations, with
`--after S1` and `--after S1b` respectively, each with
`--expected-source-sha <full-committed-code-sha>`. The guard invocation starts
only after the JSON commit. Each command executes the built scheduling tool,
which owns its driver leases; it is not an unleased direct build/test command.
Wait on exit 75 using the tool's returned run ID until terminal;
never abandon an active run or retry unleased. Preserve all raw CHECKPOINT lines,
SHA/source/build provenance, per-row fresh TRX, counts and slot/execution timing.
Run evidence-diff over the entire Code base..HEAD; fetch master and use read-only
`git merge-tree --write-tree origin/master HEAD`, never merge to resolve.

### Performance budget

All estimates are **provisional**, owned by CARD-1882/CARD-1404; no expanded-test
Linux p95 has been measured. The sibling estimated its smaller envelope class at
3 minutes and recorded cold build observations of 291-426 seconds. Those figures
are provenance, not a claimed measured p95. The amended plan sets a hard budget:
no single checkpoint run is estimated above 25 minutes (A-4). This manifest meets
that by the two runs above. It does not collapse drift rows with OR filters;
each case keeps its own row, TRX and Min, and each run stays within 25 minutes.

Drift run, sum **25**: CP-1 is 7 minutes (the cold build and its own two
results). CP-2..CP-15 and CP-20..CP-23 are 18 rows at 1 minute. The previous
manifest budgeted CP-1 at 8 (7 for the build plus 1 for the row). Putting that
extra minute back would make this run 26, over the cap, so the build and CP-1's
two results share the 7-minute row. 426 seconds is already inside that 7. If
the first measured build alone exceeds about 6 minutes, split the drift run
further and commission Debug; do not widen the estimate.

Guard run, sum **17**: CP-16 is 9 minutes (a second cold build plus the previous
2 minutes for six fixture methods). CP-17=1, CP-18=3, CP-19=1, CP-24=3. CP-24's
3 minutes is the plan's provisional budget for this one method. The whole
`DerivedImpactIndexTests` class has been seen above 8 minutes on loaded server2
(Merge batch 182, 2026-11-11); that class timing is not this row's p95.
CP-17's historical approximately 17-second case still supports its 1-minute
estimate (`2026-11-15-why-missed-g136-rcbaseline-timeout.md`).

Index regeneration is not a checkpoint row. Budget **3 minutes** for the first
leased driver build plus regenerate (must report changed) and **2 minutes** for
the second leased `-NoBuild` run (must report unchanged). Each lease is its own
build-slot acquisition. Neither is estimated over 25 minutes.

Measurement plan: collect build, discovery, every B/W case, Git, pwsh, cleanup
and both regeneration runs from the first required red and green runs; retain
host/platform/load, source SHA, group/case, slot wait and `C1820 phase=*`
timings with TRX under the ignored checkpoint run. Use subsequent ordinary
Review/qualification observations to reach at least three comparable green
samples per row; no sample-only repeats. Report unavailable filesystem spans as
unknown/remainder, never zero. Re-estimate at roughly 2x observed p95 after
enough samples. If a row or either run exceeds 30 minutes, preserve attribution,
split it further and commission Debug; do not silently widen it.

Timeout qualification: the table importer derives `max(15, 3*estimate)`. That is
CP-1=21 minutes, CP-16=27 minutes, CP-18=15, CP-24=15, and 15 for every
1-minute row. These are the tooling-floor exception under CARD-1882/CARD-1404,
not measured 2x budgets. Desired provisional execution caps, not applied by
this table, are: drift build row 16 minutes, each 1-minute drift or identity
row 2 minutes, CP-16's six methods 4 minutes plus the build observation, CP-17
2, CP-18 6, CP-19 2, CP-24 6. The commands use the importer defaults; they do
not silently apply an unlisted YAML amendment. Child bounds stay git 60s, pwsh
120s, output drain 10s, process reap and teardown 30s. Each scratch repository
must be fresh because its bytes/index/commits are the subject; there is no
AppHost, DB, container, migrations or shared expensive startup to replay. No
timeout increases are part of the proposed source edits.

Admission is separately bounded by the checkpoint default 45 minutes per slot
acquisition. No total-timeout override is used. The previous
`totalTimeoutMinutes: 68` line is removed (A-2); the importer treated it as an
explicit override. Derived execution allowances are drift 60 minutes and guard
44 minutes, as stated under Checkpoints. With one admission allowance the
default independent hard-wall budgets are 105 and 89 minutes. Actual
admission-only union and execution are reported separately. The expected serial
ordinary total of the two green runs is 42 minutes; neither run is estimated
over 25.

### Cost

- Ordinary V/R floor (Code), estimated: drift 25 + guard 17 = **42 minutes**.
  Drift is CP-1 7 + CP-2..CP-15 14 + CP-20..CP-23 4. Guard is CP-16 9 + CP-17 1
  + CP-18 3 + CP-19 1 + CP-24 3. The 42 includes both isolated builds (7+7) and
  the ordinary row work (18+10), not authoring or queue time.
- Red-first floor, estimated: the drift rows only, **25 minutes**, including a
  separate source-bound build. CP-24 is excluded. Both variants for every file
  are ordinary regression tests; the mutation waiver does not waive this
  red-first production evidence.
- Index regeneration, estimated **5 minutes** (3 + 2), two build-slot leases,
  outside the checkpoint sums. The JSON commit is required before the green
  guard run.
- Tool bootstrap/setup allowance, estimated **2 minutes**. Commissioned Code
  total = 2 + 25 red + 5 regeneration + 42 green = **74 minutes**, excluding
  authoring and slot admission. Neither green run, nor the red-first run, is
  estimated above 25 minutes. Design-stage actual builds, tests and mutations
  of the product: **0**. The checkpoint-table import below is a read of this
  document into ignored `.antiphon/`; it is not a product test.
- PC floor (Mutation), if commissioned: PC-1..PC-15, PC-17 and PC-24..PC-27
  each select an exact two-result method; PC-16 and PC-18..PC-23 each select
  one exact method. Budget **1 minute per selected red or restored-green method
  run**. With a 7-minute leased build for each red and each restoration:
  27 * (7+1+7+1) = **432 minutes**, plus 2 bootstrap/setup = **434 minutes**.
  No mutation batching savings assumed because most controls edit the same
  production file. A task of that size needs separately commissioned bounded
  batches/Debug under R1. The stated execution waiver schedules **0 minutes of
  PC execution** on this commission; it does not change that estimated
  non-waived floor. Full non-waived Code+Mutation estimate = 74 + 434 =
  **508 minutes** (the 2-minute bootstrap is inside both addends, matching the
  previous manifest's addition).
- Sharing one build across the 19 drift rows avoids 18 * 7 = **126 estimated
  minutes**, and sharing one build across the 5 guard rows avoids 4 * 7 =
  **28 estimated minutes**, 154 in all, versus rebuilding per row. No
  full-suite cost saving is claimed; excluded suites do not exercise a changed
  S1 behaviour. Per-file grouping trades eighteen host discoveries for precise
  receipt identity; phase data must confirm the provisional 1-minute groups.

### Binding and handoff audit

Before execution, Code binds the proposed wrapper names, B/W argument roster,
comment anchors/byte offsets, independent digest oracle and exact source SHA to
the implemented bodies and generated manifest. Review reads those bodies and
fresh TRX; names alone are unbound payloads, not evidence. Any non-equivalent
test/filter/timeout change needs an explicit manifest amendment before credit.

Counts: deciding files=18; checkpoints=24; minimum executed results=180;
guards=27; mapped=27; missing=0; duplicate PC maps=0. Designed controls=27;
executed controls=0 (design stage; stated mutation waiver). Ordinary floor=42
minutes across two runs (25 and 17). Every non-waived PC must run
break/red/restore/green after land; none is represented as already run.
Required V/R execution is pending Code, including ordinary red-first evidence
and the owner regeneration before CP-24. This amendment is the Review round 2
subject, diff-only, together with the amended plan. Code follows that review
and still waits on the collisions table. No human product choice is needed to
implement the already authorized slice.
