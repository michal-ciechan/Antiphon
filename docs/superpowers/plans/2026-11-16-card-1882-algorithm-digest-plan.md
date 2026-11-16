# CARD-1882 algorithm-digest coverage

Plan only. No implementation in this dispatch. One artifact, separate from CARD-1883.

Source read: full `origin/master` `48a4615dcb4d67033886a3e31629dc7351ed0852` (fetched this dispatch; the task branch starts at that SHA). Survey row read from `origin/feat/card-task-ae89df49` `e992ac4572313ca17790e94f419f9a4b2d58a2e7`, `docs/investigations/2026-11-16-server2-refill-survey.md` (CARD-1882 row and dispatch row 4). Card text is CARD-1882 revision 0; revision 1 is this Plan dispatch.

Amendment 1 (Plan dc76c20f, 2026-11-16) answers round-1 Review b65e738f, `docs/superpowers/reviews/2026-11-16-card-1882-plan-testdesign-review.md` on `origin/feat/card-task-b65e738f` `0b502891d76a751be519cddac07173509f802fc9`. It re-reads full `origin/master` `18eabe4ce48a46adea27aa9cbb410415eae9ac91`. That master differs from `48a4615dc` only in two CARD-1883 docs, so every cited source line still holds. The amendment fixes D-1 by adding four imported MSBuild files to the roster, which now has 18 files (decision D-2, roster, D-3). It fixes D-2 by moving the owner regeneration of the derived index into S1, gated on CARD-1836 S5c (D-8, S1, collisions). It also carries the four advisories into the TestDesign (A-1 to A-4 below). The full delta for the manifest is in [Changes for the TestDesign manifest](#changes-for-the-testdesign-manifest).

## Collisions

Later digest/composition Code collides with three owners. Do not dispatch CARD-1882 S1 while any of them holds the files below, and never hand-merge across them.

| Owner | What it holds that S1 also edits or reads | S1 rows and steps that wait on it |
|---|---|---|
| CARD-1820 S1R5, the script-closure slice (not started on `18eabe4ce`) | It adds `scripts/lib/release-impact-scripts.ps1` and fixture scripts. S1R6 then wires that module into `scripts/lib/release-impact.ps1`, so it becomes a new deciding file and a new identity path. | All drift and identity rows (CP-1..15, CP-20..23 below). If S1R5 lands first, its module joins this roster as file 19 in a re-plan. It is never added silently. |
| CARD-1820 S1R6, composition | `scripts/lib/release-impact.ps1`, `scripts/impacted-classes.ps1`, `ReleaseImpactEnvelopeTests.cs` and the graph driver. | The whole S1 edit set and all CP rows. S1 and S1R6 serialize. The second to land rebases onto the first's path array and fixture copy set. |
| CARD-1836 S5c, Code task d2fff5d4 (in flight, tip `7154ce531`) | `tests/derived-impact-index.json`: its commit `34e73d5d1` regenerates the ScriptHarness and ScriptHarnessProcess cap rows. | The S1 index regeneration step and CP-24. They wait until d2fff5d4 lands, or the caller records that it released the index file. Then they regenerate on that integrated base. Because the S1 commit carries the regenerated index, the S1 dispatch itself waits on this owner. |
| CARD-1836 admission activation (any later slice that edits `scripts/lib/rc-admission.ps1` or sets `coverage.complete`) | The admission boundary that CP-17 freezes. | CP-17. S1 never edits `rc-admission.ps1`. Activation must not read the broader digest as carry credit (D-6). |

Codex reviews the Grok-authored implementation. Lane for the later checkpoint is Linux. No `-Runner` and no `-Platform` pin.

## Decisions

D-1. The activation-binding digest is `Get-ReleaseImpactAlgorithmIdentity` in `scripts/lib/release-impact.ps1`. It compares raw `git hash-object --no-filters` output with the committed blob, then takes a SHA-256 of `path + NUL + blob`. Publish `algorithmDigest` only when every inventoried file matches. Rejected: treating the `$required` array in `Get-RcAdmissionAlgorithmCoverage` as that closure. The function comment says the array is not a transitive closure, `complete` stays false, and `Read-RcAdmission` sets `admitted` only when `coverage.complete` is true (`scripts/lib/rc-admission.ps1`).

D-2. A deciding file is either of these:

- a repo source file whose executed code changes which classes or evaluated items a change selects;
- a project or MSBuild file that chooses what that command compiles. This includes every repo MSBuild file the SDK imports into the command's project or its `ProjectReference` closure, because any of them can add, remove or swap a `Compile` item.

Data JSON and the input manifest stay out of the algorithm identity. Rejected: hashing `tests/impacted-classes.json`, `tests/test-selection-policy.json`, `tests/derived-impact-index.json`, or `tests/selector-runtime-inputs.json` into the algorithm digest. Those are inputs. The envelope's `inputManifestDigest` is still null.

Amendment 1 applies the second rule to the imports, which the first plan missed (Review D-1). Both driver projects sit under the repo root, so both builds import `Directory.Build.props` and `Directory.Build.targets`. `Directory.Build.targets` imports `tests/Shared/CheckpointRepeat.targets` at lines 40-41. It already injects a `<Compile Include>` at lines 46-47, under a condition on project location, and `CheckpointRepeat.targets` injects `Compile` items too. The evaluator project references `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj`. These four files are bound (roster rows 15-18).

The decision is to bind them, not to leave them unbound. A one-line edit to any of the four can change what a driver compiles, so leaving one out would let S1 close the card on a roster that its own rule calls incomplete. The cost is eight more drift results, about 4 minutes. Rejected: naming them as deliberately unbound and carrying an open prerequisite into the CARD-1836 activation gate. That would only move the same witness to a later slice, and the files are as cheap to bind as the other twelve.

The digest will now change on any repo-wide MSBuild edit. That fails closed: a changed algorithm identity between baseline and candidate refuses carry credit and never grants it. Ordinary selection already treats `Directory.Build.props` and `Directory.Build.targets` as allowlisted global inputs (`DerivedImpactIndexBuilder.Allowlist`).

Excluded on purpose, with the reason for each:

| Path | Why it is not a deciding file |
|---|---|
| `global.json` | It selects the SDK, not items. The evaluator receipts the SDK it loaded: `ReleaseInputEvaluation` hashes the loaded MSBuild assembly and SDK inputs into its digest, and refuses `sdk-drift` (`ReleaseInputEvaluation.cs:39-41`). |
| `Directory.Build.rsp` | It holds one switch, `-nodeReuse:false`. Switches do not choose items. |
| `tools/Antiphon.LandGraphAudit.Core/EvaluationBudget.cs` | It is executed only as the `release-inputs` timeout (`ReleaseInputCommand.cs:39`). A timeout refuses; it does not choose items. |
| The other `tools/Antiphon.LandGraphAudit.Core/*.cs` files (`CanaryEntries`, `LandGraphAuditGate`, `LandGraphCanaryRemoval`, `LandGraphPrecheck`, `PhaseClock`, `PowerShellSemantics`, `PsJson`, `WallClock`) | They are compiled by the default glob but are not called on the `release-inputs` path, and the Core project has no `ModuleInitializer`. The Core csproj, which decides the glob, is bound. |
| `tests/Shared/CheckpointRepeatEvidenceAttribute.cs` | It is compiled only when `AntiphonCheckpointRepeat` names the selected TUnit test project, and never for either driver. The condition lives in `CheckpointRepeat.targets`, which is bound. |
| `src/Antiphon.Hosting/PwshPrivateCache.cs` | It is compiled only into projects directly under `tests/`. Neither driver is there. The condition lives in `Directory.Build.targets`, which is bound. |
| `scripts/fixtures/nightly/c1375-probe/Directory.Build.*` and `scripts/fixtures/nightly/c487-probe/Directory.Build.*` | They are not ancestors of either driver, so the SDK never imports them. |
| `Directory.Packages.props`, `NuGet.config` | Neither exists on `18eabe4ce`. Package versions sit in the two bound driver csproj files. |

D-3. The witness set is the 18 files in the roster below. Each new file gets the same two-variant raw-byte refusal that the two current files already have in `AlgorithmSourceDriftIsRefused`:

- B: a one-byte comment change.
- W: a CRLF rewrite, which filtered `hash-object` hides and `--no-filters` does not.

Refusal means exit 2, `complete=false`, `fallback=Full`, error text containing `algorithm source drift`, and no `algorithmDigest` in the payload. Rejected: a comment-only witness. That would miss the filter bypass that S1R6D closed.

D-4. Binding is not execution. `-ReleaseEvidence` still returns an empty class list and `release-graph-unavailable`. This card does not call the relation builder, project graph, runtime module, evaluator, or index driver from the envelope. Rejected: wiring those commands into the envelope here. That is S1R6 composition.

D-5. There is no executed canonicalizer and no script-closure module on this SHA. `ReleaseImpactGraphBuilder` only records `script-closure-unavailable`. Do not add a placeholder path. S1R5 and S1R6 add those files and their witnesses together. Until then carry stays refused. Rejected: inventing a canonicalizer path so the roster looks complete.

D-6. Do not edit `scripts/lib/rc-admission.ps1`, do not remove `release-graph-incomplete`, and do not set `coverage.complete` or `admitted` to true. `RcHistoricalAdmissionTests.CollisionAndAlgorithmCoverageForceFull` stays force-Full without a source edit. Rejected: marking the static required list covered and calling that activation.

D-7. The operator mutation waiver of 2026-11-12 still stands. This plan adds ordinary drift arguments, not PC ids. It does not discharge the pending M7 four-variant controls.

D-8 (Amendment 1, Review D-2). S1 owns the ordinary regeneration of `tests/derived-impact-index.json`, using the existing owner driver.

`DerivedImpactIndexBuilder.CollectStringTargets` adds an edge for every test-class string literal of 8 or more characters that equals a tracked path (`DerivedImpactIndexBuilder.cs:304-313`). Today `ReleaseImpactEnvelopeTests` has edges only for `scripts/impacted-classes.ps1`, `scripts/lib/release-impact.ps1` and the index itself. S1 adds literals for the other 16 roster paths, through the fixture copy set and the independent oracle. That changes the committed index in three ways:

- New edges appear for paths that have none today: `scripts/lib/SelectorCensus.cs`, both `scripts/regenerate-derived-impact-index/*` files, `ReleaseInputCommand.cs`, `ReleaseInputEvaluation.cs` and the Core csproj.
- `ReleaseImpactEnvelopeTests` is added to existing edges.
- The class counts change on the two allowlisted capped paths, `Directory.Build.props` (14) and `Directory.Build.targets` (11).

Without regeneration, `DerivedImpactIndexTests.committed_index_matches_the_builder` goes red and the advisory selector misses the new edges. No test pins the allowlist cap counts (checked: no `count=` or `Directory.Build` pin in `DerivedImpactIndexTests` or `ImpactedClassesSelectorTests`).

Rejected:

- Leaving the index to "its owner". S1 creates the staleness, so S1 owns the fix. The same rule appears in CARD-1820 S1R6: "Any routine ordinary-index regeneration uses the existing owner script and is committed before tests."
- Hand-merging the JSON with S5c.
- Avoiding the literals. The oracle has to name every path literally (see the TestDesign recipe, step 6).

## Ground truth

| Card or survey assumption | What the code does on `48a4615dcb4d67033886a3e31629dc7351ed0852` (re-checked on `18eabe4ce48a46adea27aa9cbb410415eae9ac91`) |
|---|---|
| The refusal hashes raw bytes of `scripts/impacted-classes.ps1` and `scripts/lib/release-impact.ps1` only. | True. `Get-ReleaseImpactAlgorithmIdentity` lists exactly those two paths and uses `hash-object --no-filters`. `AlgorithmSourceDriftIsRefused` has four arguments: both files, each with a comment and a CRLF variant. |
| Relation builder, evaluator, canonicalizer, driver, and project graph are not hashed, and that is correct today because they are not on the envelope path. | The envelope path is as stated: `-ReleaseEvidence` dot-sources `release-impact.ps1` and exits before `Add-Type`, so it never runs the graph. Those other files are also absent from the two-path identity. |
| Module inventory is not an admitted complete graph. | True. `Get-RcAdmissionAlgorithmCoverage` always returns `complete=false` and `release-graph-incomplete`, even if every listed path is covered. `gateCredit` stays false. The default covered paths are the same two scripts. |
| The required-path list is the executed closure. | False. It is a static inventory. Several entries are not called by the selector, by either driver mode, or by `release-inputs`. Several executed deciding files are missing from it. |
| A canonicalizer exists to hash. | False. No type or command canonicalizes the class set. The envelope JSON writer refuses a complete envelope. |
| Before activation, a one-byte change in any deciding file must make the refusal fire. | Not true yet. A one-byte change outside the two scripts still publishes an `algorithmDigest`. |
| (Amendment 1) The deciding closure is the C#, PowerShell and leaf csproj files only. | False. Both driver builds import `Directory.Build.props`, `Directory.Build.targets` and, through the targets, `tests/Shared/CheckpointRepeat.targets`. The evaluator also compiles `Antiphon.LandGraphAudit.Core.csproj` as a `ProjectReference`. All four are LF with no BOM and no CR. |
| (Amendment 1) Extending the test fixture leaves other committed outputs alone. | False. New tracked-path literals in a test class change `tests/derived-impact-index.json` (D-8). |

### Commands that actually run

**Ordinary selection:** `scripts/impacted-classes.ps1` without `-ReleaseEvidence`. Executes that script and `scripts/lib/SelectorCensus.cs` (`Add-Type`, then `SelectorCensus.Scan`). Reads `tests/impacted-classes.json`, `tests/test-selection-policy.json` and `tests/derived-impact-index.json`. Does not dot-source `release-impact.ps1`. Does not run a graph builder.

**Release envelope:** `-ReleaseEvidence`. Executes the script only up to the early exit, then `scripts/lib/release-impact.ps1`. Classes stay empty, `complete` stays false, and `fallback` stays `Full`.

**Release relation:** `Antiphon.DerivedImpactIndex --release-graph` (`scripts/regenerate-derived-impact-index/Program.cs`). Loads commit blobs and calls `ReleaseImpactGraphBuilder.Build` and `Serialize`. It uses the index builder only for `DerivedImpactIndexBuilder.Allowlist`. It does not call `ReleaseImpactProjectGraph` or the evaluator. The graph always includes `project-evaluation-unavailable`, `runtime-inputs-unavailable`, `script-closure-unavailable` and `platform-evaluation-unavailable`.

**Ordinary index:** one-argument `Antiphon.DerivedImpactIndex <repo>`. Calls `DerivedImpactIndexBuilder.Build` and `Write`, and `Build` calls `PlatformTaxonomy.Derive`. The selector later reads the committed JSON; the driver does not run inside the selector. The SDK build of this project imports `Directory.Build.props`, `Directory.Build.targets` and `tests/Shared/CheckpointRepeat.targets`.

**Project graph and runtime:** `ReleaseImpactProjectGraph.Build` calls `ReleaseImpactGraphBuilder.Build`, and `ReleaseImpactRuntimeInputs.Build` calls `ReleaseImpactProjectGraph.Build`. Tests call both. No production command calls either.

**Evaluator:** `Antiphon.LandGraphAudit release-inputs`. `Program.Main` dispatches to `ReleaseInputCommand.Run`, which calls `ReleaseInputEvaluation.Run`. That path uses Microsoft.Build evaluation, with `EvaluationBudget.ToolCeilingSeconds` as a timeout. It does not call `BuildInputPolicy` or `EvaluationExpressionPolicy`; those two types belong to the land-audit command. The build of this project compiles `Antiphon.LandGraphAudit.Core` through a `ProjectReference` and imports the same three repo MSBuild files.

### Static admission list versus execution

Listed, but not executed by ordinary selection, `--release-graph`, the one-argument driver or `release-inputs`:

- `scripts/lib/test-selection.ps1`
- `scripts/lib/rc-admission.ps1`
- `scripts/lib/rc-input-manifest.ps1`
- `scripts/lib/rc-chunk-digest.ps1`
- `scripts/lib/rc-boundary.ps1`
- `scripts/lib/rc-historical-stream.cs`
- `tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs`
- `tools/Antiphon.LandGraphAudit/BuildInputPolicy.cs`

Executed deciding files that the list leaves out:

- `scripts/lib/SelectorCensus.cs`
- `tests/Shared/PlatformTaxonomy.cs`
- `tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeInputs.cs`
- `Directory.Build.props`, `Directory.Build.targets`, `tests/Shared/CheckpointRepeat.targets` and `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj` (Amendment 1)

This plan does not repair the list (D-6). The CARD-1836 activation gate must reconcile it with this roster.

## Deciding-file roster

Already witnessed. Keep both variants:

| File | Why it decides |
|---|---|
| `scripts/impacted-classes.ps1` | Ordinary class selection, and the envelope entry. |
| `scripts/lib/release-impact.ps1` | Publishes the algorithm identity and the incomplete envelope. |

Add both variants. The envelope still does not execute any file in this table:

| File | Executed by |
|---|---|
| `scripts/lib/SelectorCensus.cs` | Ordinary selection (`Add-Type`, then `Scan`). |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs` | `--release-graph`, and `ReleaseImpactProjectGraph.Build`. |
| `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs` | The one-argument driver (`Build`, `Write`). `--release-graph` reads only `Allowlist`. |
| `tests/Shared/PlatformTaxonomy.cs` | The one-argument driver (`PlatformTaxonomy.Derive`). `--release-graph` does not call it. |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs` | Its tests, and `ReleaseImpactRuntimeInputs.Build`. No production command. |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeInputs.cs` | `ReleaseImpactRuntimeTests` only. No production command. |
| `scripts/regenerate-derived-impact-index/Program.cs` | Both driver modes. It chooses between a disk rebuild and a commit-blob relation. |
| `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` | Compile closure: the two builders, `PlatformTaxonomy.cs` and the Roslyn package. |
| `tools/Antiphon.LandGraphAudit/Program.cs` | `release-inputs` dispatch. The rest of the file is the land audit, in the same compilation unit. |
| `tools/Antiphon.LandGraphAudit/ReleaseInputCommand.cs` | `release-inputs`. |
| `tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs` | `release-inputs` item and import export. |
| `tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj` | Evaluator compile closure and the Microsoft.Build package version. |

Added by Amendment 1 (Review D-1). Add both variants. These are imported or referenced MSBuild files:

| File | Chooses what is compiled for |
|---|---|
| `Directory.Build.props` | Both drivers. It sets `DefaultItemExcludes`, which feeds the default `Compile` glob. |
| `Directory.Build.targets` | Both drivers. It imports `CheckpointRepeat.targets` (lines 40-41) and holds a conditioned `<Compile Include>` (lines 46-47). |
| `tests/Shared/CheckpointRepeat.targets` | Both drivers, imported by the targets file. It holds conditioned `Compile` items. |
| `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj` | The evaluator, as a `ProjectReference`. Its default glob compiles `EvaluationBudget` and the Core sources. |

The roster has 18 files, 16 of them new. That is 32 new drift results plus the existing four, 36 in all.

Identity order, top to bottom. Keep positions 1-14 as first planned, so the existing anchors, wrappers and CP numbers stay stable, and append 15-18. Do not sort:

1. `scripts/impacted-classes.ps1`
2. `scripts/lib/release-impact.ps1`
3. `scripts/lib/SelectorCensus.cs`
4. `tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs`
5. `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs`
6. `tests/Shared/PlatformTaxonomy.cs`
7. `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs`
8. `tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeInputs.cs`
9. `scripts/regenerate-derived-impact-index/Program.cs`
10. `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`
11. `tools/Antiphon.LandGraphAudit/Program.cs`
12. `tools/Antiphon.LandGraphAudit/ReleaseInputCommand.cs`
13. `tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs`
14. `tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj`
15. `Directory.Build.props`
16. `Directory.Build.targets`
17. `tests/Shared/CheckpointRepeat.targets`
18. `tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj`

Clean envelope output stays incomplete: `classes` length 0, `complete` false, `fallback` `Full`, graph digests null. `AlgorithmDigestBindsCommittedBlobs` must expect this order, with all 18 paths written literally.

The fixture copies these 18 files from the worktree into the tiny repo as raw bytes: `ReadAllBytes` then `WriteAllBytes` (A-3). The current fixture uses `ReadAllTextAsync`/`WriteAllTextAsync` and folds CRLF to LF. That strips the BOM the twelve `.cs` targets carry and normalizes line endings, so the clean raw hash would no longer equal the copied committed blob. The fixture `.gitattributes` becomes `*.ps1 text eol=lf`, `*.cs text eol=lf`, `*.csproj text eol=lf`, `*.props text eol=lf`, `*.targets text eol=lf`, so the CRLF variant still proves that filtered `hash-object` hides the drift. Do not change the production `.gitattributes`.

The fixture never builds, so `Directory.Build.props` and `Directory.Build.targets` at its root are bytes only. The baseline-to-candidate diff in `OrdinaryModeRemainsAdvisory` stays `input.txt` only, so the allowlist is not triggered there.

## Slices

S1, after TestDesign. It is dispatched only once the Collisions table clears: S1R6 is not holding these files, and S5c (d2fff5d4) has landed or released `tests/derived-impact-index.json`. Edit only these files:

- `scripts/lib/release-impact.ps1`: the path array inside `Get-ReleaseImpactAlgorithmIdentity`, which grows to 18 paths in roster order. No other envelope behaviour changes.
- `tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs`: the raw-byte fixture copy set, the fixture attributes, the drift wrappers and helper, and the expected 18-path inventory in `AlgorithmDigestBindsCommittedBlobs`.
- `tests/derived-impact-index.json` (Amendment 1, D-8). This is never edited by hand. It is regenerated by the owner driver after the last edit to `ReleaseImpactEnvelopeTests.cs`, on the integrated base that already holds S5c, and committed in its own commit before the green checkpoint run:
  1. Build the driver first and regenerate: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1882-index -- pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1`. It must report that the bytes changed.
  2. Run it a second time, without rebuilding, to prove a fixed point: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1882-index-2 -- pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1 -NoBuild`. It must report that the bytes are unchanged. If they changed again, stop and report it; the index has not settled.
  3. Commit only the JSON. The driver's `bin/` and `obj/` are already gitignored.

  If S5c lands after the S1 commit and before S1 lands, S1 regenerates again on the new base, in a new commit. Never resolve a conflict in this file by hand.

Do not edit the other 16 deciding files, `scripts/lib/rc-admission.ps1` or the qualification executor.

Tests this slice owns:

- the 18 drift wrappers on `ReleaseImpactEnvelopeTests`;
- `ReleaseImpactEnvelopeTests.AlgorithmDigestBindsCommittedBlobs`;
- `DerivedImpactIndexTests.committed_index_matches_the_builder`, through the regenerated index.

The other methods on `ReleaseImpactEnvelopeTests` stay green and incomplete. Adjacent non-edit guard: `RcHistoricalAdmissionTests.CollisionAndAlgorithmCoverageForceFull` still forces Full.

Existing tests affected for migrations, public DTOs/routes, operation labels, remote script functions and statement budgets: none. Committed derived output: `tests/derived-impact-index.json`, regenerated as above, with `DerivedImpactIndexTests.committed_index_matches_the_builder` as its existing guard.

CARD-1782 table: `scripts/lib/release-impact.ps1` has no row. The test edit matches `tests/Antiphon.Tests/**/*.cs`, row `test-lane-category`, class `TestLaneCategoryGuardTests`. No row is added.

## Changes for the TestDesign manifest

This is the delta that the TestDesign amendment applies to `docs/superpowers/plans/2026-11-16-card-1882-testdesign.md`, commit `ccd3e470199b41c50277cce2554dc70f6e58a9c0`. CP-1..CP-19 keep their numbers. New ids are appended rather than renumbered.

Roster and verification:

- Add V-19..V-22 for roster positions 15-18, using the same recipe, both B and W, and R-1. Wrappers:
  - `AlgorithmSourceDriftIsRefused_15_BuildProps`
  - `AlgorithmSourceDriftIsRefused_16_BuildTargets`
  - `AlgorithmSourceDriftIsRefused_17_CheckpointRepeatTargets`
  - `AlgorithmSourceDriftIsRefused_18_EvaluatorCoreProject`
- B anchors. Each is unique in its file on `18eabe4ce`, sits inside a comment and starts with an uppercase ASCII letter:
  - 15: `Keep MSBuild's default item globs`
  - 16: `CARD-0222: cap the growth`
  - 17: `The exact selected csproj alone`
  - 18: `Clock and approval orchestration`
- All four files are LF with no BOM.
- V-15 and the step-6 oracle grow to the 18 literal paths in roster order.
- V-23 is new: the regenerated index equals the builder (D-8).
- Guards G-24..G-27 cover roster positions 15-18, with distinct controls PC-24..PC-27. Each control omits only that path from the production array. Guards: 27 (was 23).
- A-1: replace the V-6 anchor `CARD-1832. Method declaration`, which appears at `tests/Shared/PlatformTaxonomy.cs` lines 6 and 22, with the unique `CARD-1832. Method declaration that the Linux lane` (line 6). Keep the assertion that exactly one offset differs.
- A-3: raw-byte fixture copies and the five-line `.gitattributes` above.

Rows:

| New row | After | Build | Group | Filter | Covers | Min | Waits on |
|---|---|---|---|---|---|---:|---|
| CP-20 | S1 | as CP-1 | build-props | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_15_BuildProps` | V-19, R-1, R-2 | 2 | S1R5, S1R6 |
| CP-21 | S1 | as CP-1 | build-targets | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_16_BuildTargets` | V-20, R-1, R-2 | 2 | S1R5, S1R6 |
| CP-22 | S1 | as CP-1 | repeat-targets | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_17_CheckpointRepeatTargets` | V-21, R-1, R-2 | 2 | S1R5, S1R6 |
| CP-23 | S1 | as CP-1 | evaluator-core-project | `/*/*/ReleaseImpactEnvelopeTests/AlgorithmSourceDriftIsRefused_18_EvaluatorCoreProject` | V-22, R-1, R-2 | 2 | S1R5, S1R6 |
| CP-24 | S1 (guard run, see budget) | guard build | index-fresh | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | V-23 | 1 | S5c d2fff5d4 landed or index released |

Other row changes:

- CP-1..CP-15 and CP-20..CP-23 wait on S1R5 and S1R6.
- CP-17 waits on CARD-1836 admission activation not being in flight.
- CP-24 and the regeneration step wait on S5c (d2fff5d4).
- Before S1, CP-20..CP-23 are red at exit 0 versus 2 (8 more results). CP-1, CP-2 and CP-15 are red on the 18-path oracle. CP-24 is not part of the red-first run: the index is regenerated only after the final test edit.

Counts after the amendment:

| Count | Before | After |
|---|---:|---:|
| Deciding files | 14 | 18 |
| Drift results | 28 | 36 |
| `ReleaseImpactEnvelopeTests` results | 35 | 43 |
| Checkpoints | 19 | 24 |
| Minimum executed results (43 + 1 + 78 + 57 + 1) | 171 | 180 |
| Guards and controls | 23 | 27 |

## Performance budget

Selection: `ReleaseImpactEnvelopeTests` and one `DerivedImpactIndexTests` method in `tests/Antiphon.Tests`, Linux, plus the admission and guard rows that TestDesign names. No AppHost, database, container, migration or snapshot. The existing fixture builds a tiny git repo once per result: the 18 algorithm files, not the product tree. Isolation stays per result because the test mutates that repo. Teardown is the fixture's own directory delete, already capped at 30 seconds. Phase lines already printed: `C1820 phase=pwsh`, `phase=git` and `phase=cleanup`.

No measured p95 exists for the expanded list. Sibling plan `docs/superpowers/plans/2026-11-13-card-1820-drift-checkpoints.md` budgeted CP-108 at 3 minutes for its then ten-result class. The estimate below is provisional, using the sibling's class average of about 18 seconds per result, not a new sample:

- 32 new drift results: about 10 minutes.
- The class as a whole: about 13 minutes wall after one ordinary test build.
- Index regeneration: two driver runs of about 1-2 minutes each, plus the driver build. The whole `DerivedImpactIndexTests` class has been seen above 8 minutes on loaded server2 (Merge batch 182, 2026-11-11), so budget the single CP-24 method at 3 minutes provisional.

Existing per-command ceilings stay: 120 seconds for pwsh and 60 seconds for git. Build time and build-slot admission are separate and not counted above.

Thirty-minute rule (A-4). The first TestDesign estimated a 29-minute serial run, which would grow to about 36 minutes with the new rows. That is over the 30-minute Debug threshold in `docs/test-performance-standards.md`, so this plan sets a hard budget: no single checkpoint run may be estimated at more than 25 minutes, which leaves headroom for unmeasured per-row host discovery. TestDesign meets it by splitting the green run in two. Each run has its own After group and its own isolated build, because the tool refuses to reuse a build across After groups:

- Drift run: CP-1..CP-15 and CP-20..CP-23. About 7 minutes for the build plus 18 minutes of rows, 25 minutes in all.
- Guard run: CP-16..CP-19 and CP-24. About 7 minutes for the build plus 10 minutes of rows, 17 minutes in all.

TestDesign may instead group drift rows with OR filters, to cut per-row discovery overhead, if that keeps one run within 25 minutes. Each case still keeps its own TRX result and an exact Min. If a measured run still crosses 30 minutes, split it further and open a Debug task with the timing evidence. Never widen a timeout to fit.

Total timeout (A-2). Use no total-timeout override. Remove the `totalTimeoutMinutes: 68` header line, which the importer reads as an explicit 68-minute override and which contradicts "no override". Let the tool derive each run's total. The importer's per-row floor of `max(15, 3 × estimate)` remains the recorded tooling-floor exception under CARD-1882/CARD-1404 until measured p95 values allow budgets near 2x.

`CollisionAndAlgorithmCoverageForceFull` is an existing admission script test. Historical rc timing put that one case near 17 seconds (`docs/investigations/2026-11-15-why-missed-g136-rcbaseline-timeout.md`). It keeps a budget of 1 minute and a 2-minute desired row timeout, in its own row.

No session that waits for input is added. Parking and CARD-1083 do not apply.

## Out of scope

S1R5 script closure, S1R6 composition, introducing a canonicalizer, wiring the evaluator into the envelope, admission `complete=true`, carry credit, repairing the static admission list, native Windows W1, CARD-1866 A-2 and the M7 mutation controls. CARD-1836 S5c stays fail-closed and is a separate commission. Its only contact with this card is the index file in the Collisions table.
