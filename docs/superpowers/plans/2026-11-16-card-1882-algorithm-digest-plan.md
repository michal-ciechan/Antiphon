# CARD-1882 algorithm-digest coverage

Plan only. No implementation in this dispatch. One artifact, separate from CARD-1883.

Source read: full `origin/master` `48a4615dcb4d67033886a3e31629dc7351ed0852` (fetched this dispatch; the task branch starts at that SHA). Survey row read from `origin/feat/card-task-ae89df49` `e992ac4572313ca17790e94f419f9a4b2d58a2e7`, `docs/investigations/2026-11-16-server2-refill-survey.md` (CARD-1882 row and dispatch row 4). Card text is CARD-1882 revision 0; revision 1 is this Plan dispatch.

Later digest/composition Code collides with CARD-1820 S1R6 and with CARD-1836 admission activation. Do not dispatch it while either holds this source area or could treat a broader digest as carry credit. Codex reviews the Grok-authored implementation. Lane for the later checkpoint is Linux. No `-Runner` and no `-Platform` pin.

## Decisions

D-1. The activation-binding digest is `Get-ReleaseImpactAlgorithmIdentity` in `scripts/lib/release-impact.ps1`: raw `git hash-object --no-filters` compared with the committed blob, then a SHA-256 of `path + NUL + blob`. Publish `algorithmDigest` only when every inventoried file matches. Rejected: treating `Get-RcAdmissionAlgorithmCoverage`'s `$required` array as that closure. The function comment says the array is not a transitive closure, `complete` stays false, and `Read-RcAdmission` sets `admitted` only when `coverage.complete` is true (`scripts/lib/rc-admission.ps1`).

D-2. One deciding file is a repo source file whose executed code changes which classes or evaluated items a change selects, plus the project file that chooses what that command compiles. Data JSON and the input manifest stay out of the algorithm identity. Rejected: hashing `tests/impacted-classes.json`, `tests/test-selection-policy.json`, `tests/derived-impact-index.json`, or `tests/selector-runtime-inputs.json` into the algorithm digest. Those are inputs. The envelope's `inputManifestDigest` is still null.

D-3. The witness set is the 14 files in the roster below. Each new file gets the same two-variant raw-byte refusal the two current files already have in `AlgorithmSourceDriftIsRefused`: a one-byte comment, and a CRLF rewrite that filtered `hash-object` hides and `--no-filters` does not. Refusal is exit 2, `complete=false`, `fallback=Full`, error text contains `algorithm source drift`, and the payload has no `algorithmDigest`. Rejected: a comment-only witness. That would miss the filter bypass S1R6D closed.

D-4. Binding is not execution. `-ReleaseEvidence` still returns an empty class list and `release-graph-unavailable`. This card does not call the relation builder, project graph, runtime module, evaluator, or index driver from the envelope. Rejected: wiring those commands into the envelope here. That is S1R6 composition.

D-5. There is no executed canonicalizer and no script-closure module on this SHA. `ReleaseImpactGraphBuilder` only records `script-closure-unavailable`. Do not add a placeholder path. S1R6 adds those files and their witnesses together. Until then carry stays refused. Rejected: inventing a canonicalizer path so the roster looks complete.

D-6. Do not edit `scripts/lib/rc-admission.ps1`, do not remove `release-graph-incomplete`, and do not set `coverage.complete` or `admitted` true. `RcHistoricalAdmissionTests.CollisionAndAlgorithmCoverageForceFull` stays force-Full without a source edit. Rejected: marking the static required list covered and calling that activation.

D-7. Operator mutation waiver of 2026-11-12 still stands. This plan adds ordinary drift arguments, not PC ids, and does not discharge the pending M7 four-variant controls.

## Ground truth

| Card or survey assumption | What the code does on `48a4615dcb4d67033886a3e31629dc7351ed0852` |
|---|---|
| The refusal hashes raw bytes of `scripts/impacted-classes.ps1` and `scripts/lib/release-impact.ps1` only. | True. `Get-ReleaseImpactAlgorithmIdentity` lists exactly those two paths and uses `hash-object --no-filters`. `AlgorithmSourceDriftIsRefused` has four arguments (both files, comment and CRLF). |
| Relation builder, evaluator, canonicalizer, driver, and project graph are not hashed, and that is correct today because they are not on the envelope path. | The envelope path is correct as stated: `-ReleaseEvidence` dot-sources `release-impact.ps1` and exits before `Add-Type`. It does not run the graph. Those other files are also absent from the two-path identity. |
| Module inventory is not an admitted complete graph. | True. `Get-RcAdmissionAlgorithmCoverage` always returns `complete=false` and `release-graph-incomplete`, even if every listed path is covered. `gateCredit` stays false. Default covered paths are the same two scripts. |
| The required-path list is the executed closure. | False. It is a static inventory. Several entries are not called by the selector, either driver mode, or `release-inputs`. Several executed deciding files are missing from it. |
| A canonicalizer exists to hash. | False. No type or command canonicalizes the class set. The envelope JSON writer refuses a complete envelope. |
| Before activation, a one-byte change in any deciding file must make the refusal fire. | Not true yet. A one-byte change outside the two scripts still publishes an `algorithmDigest`. |

### Commands that actually run

Ordinary selection, `scripts/impacted-classes.ps1` without `-ReleaseEvidence`. Executes that script and `scripts/lib/SelectorCensus.cs` (`Add-Type`, then `SelectorCensus.Scan`). Reads `tests/impacted-classes.json`, `tests/test-selection-policy.json`, and `tests/derived-impact-index.json`. Does not dot-source `release-impact.ps1` and does not run a graph builder.

Release envelope, `-ReleaseEvidence`. Executes the script only through the early exit, then `scripts/lib/release-impact.ps1`. Classes stay empty. `complete` stays false. `fallback` stays `Full`.

Release relation, `Antiphon.DerivedImpactIndex --release-graph` (`scripts/regenerate-derived-impact-index/Program.cs`). Loads commit blobs and calls `ReleaseImpactGraphBuilder.Build` and `Serialize`. The only use of the index builder is `DerivedImpactIndexBuilder.Allowlist`. It does not call `ReleaseImpactProjectGraph` or the evaluator. The graph always includes `project-evaluation-unavailable`, `runtime-inputs-unavailable`, `script-closure-unavailable`, and `platform-evaluation-unavailable`.

Ordinary index, one-argument `Antiphon.DerivedImpactIndex <repo>`. Calls `DerivedImpactIndexBuilder.Build` / `Write`. `Build` calls `PlatformTaxonomy.Derive`. The selector later reads the committed JSON. The driver does not run inside the selector.

Project graph and runtime. `ReleaseImpactProjectGraph.Build` calls `ReleaseImpactGraphBuilder.Build`. `ReleaseImpactRuntimeInputs.Build` calls `ReleaseImpactProjectGraph.Build`. Tests call both. No production command calls either.

Evaluator, `Antiphon.LandGraphAudit release-inputs`. `Program.Main` dispatches to `ReleaseInputCommand.Run`, which calls `ReleaseInputEvaluation.Run`. That path uses Microsoft.Build evaluation and `EvaluationBudget.ToolCeilingSeconds` as a timeout. It does not call `BuildInputPolicy` or `EvaluationExpressionPolicy`. Those two types are on the land-audit command, not on `release-inputs`. The timeout refuses the process. It does not choose items, so `tools/Antiphon.LandGraphAudit.Core/EvaluationBudget.cs` is executed and is not a deciding file.

### Static admission list versus execution

Listed, and not executed by ordinary selection, `--release-graph`, the one-argument driver, or `release-inputs`:

- `scripts/lib/test-selection.ps1`
- `scripts/lib/rc-admission.ps1`
- `scripts/lib/rc-input-manifest.ps1`
- `scripts/lib/rc-chunk-digest.ps1`
- `scripts/lib/rc-boundary.ps1`
- `scripts/lib/rc-historical-stream.cs`
- `tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs`
- `tools/Antiphon.LandGraphAudit/BuildInputPolicy.cs`

Executed deciding files absent from that list:

- `scripts/lib/SelectorCensus.cs`
- `tests/Shared/PlatformTaxonomy.cs`
- `tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeInputs.cs`

## Deciding-file roster

Already witnessed, keep both variants:

| File | Why it decides |
|---|---|
| `scripts/impacted-classes.ps1` | Ordinary class selection, and the envelope entry. |
| `scripts/lib/release-impact.ps1` | Publishes the algorithm identity and the incomplete envelope. |

Add both variants. The envelope still does not execute the files that are not the two scripts above:

| File | Executed by |
|---|---|
| `scripts/lib/SelectorCensus.cs` | Ordinary selection (`Add-Type` + `Scan`). |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs` | `--release-graph`, and `ReleaseImpactProjectGraph.Build`. |
| `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs` | One-argument driver (`Build` / `Write`); `--release-graph` reads `Allowlist` only. |
| `tests/Shared/PlatformTaxonomy.cs` | One-argument driver (`PlatformTaxonomy.Derive`). Not called by `--release-graph`. |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs` | Its tests, and `ReleaseImpactRuntimeInputs.Build`. No production command. |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeInputs.cs` | `ReleaseImpactRuntimeTests` only. No production command. |
| `scripts/regenerate-derived-impact-index/Program.cs` | Both driver modes. Chooses disk rebuild versus commit-blob relation. |
| `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` | Compile closure: the two builders, `PlatformTaxonomy.cs`, and the Roslyn package. |
| `tools/Antiphon.LandGraphAudit/Program.cs` | `release-inputs` dispatch. The rest of the file is the land audit and is the same compilation unit. |
| `tools/Antiphon.LandGraphAudit/ReleaseInputCommand.cs` | `release-inputs`. |
| `tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs` | `release-inputs` item and import export. |
| `tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj` | Evaluator compile closure and Microsoft.Build package version. |

Fourteen files. Twelve are new. Twenty-four new argument rows, plus the existing four.

Identity order, top to bottom. Keep the two current paths first. Do not sort:

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

Clean envelope output stays incomplete: `classes` length 0, `complete` false, `fallback` `Full`, graph digests null. `AlgorithmDigestBindsCommittedBlobs` must expect this order. The fixture copies these fourteen files from the worktree into the tiny repo. Extend the fixture `.gitattributes` with `*.cs text eol=lf` and `*.csproj text eol=lf` so the CRLF variant still proves filtered `hash-object` equality. Do not change production `.gitattributes`.

## Slices

S1, after TestDesign. Edit only:

- `scripts/lib/release-impact.ps1`: the path array inside `Get-ReleaseImpactAlgorithmIdentity`. No other envelope behaviour.
- `tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs`: fixture copy set, fixture attributes, `AlgorithmSourceDriftIsRefused` arguments, and the expected inventory in `AlgorithmDigestBindsCommittedBlobs`.

Do not edit the other twelve deciding files, `scripts/lib/rc-admission.ps1`, the qualification executor, or the derived index JSON.

Tests this slice owns: `ReleaseImpactEnvelopeTests.AlgorithmSourceDriftIsRefused` and `ReleaseImpactEnvelopeTests.AlgorithmDigestBindsCommittedBlobs`. The other methods on `ReleaseImpactEnvelopeTests` stay green and incomplete. Adjacent non-edit guard: `RcHistoricalAdmissionTests.CollisionAndAlgorithmCoverageForceFull` still forces Full.

Existing tests affected for migrations, public DTOs/routes, operation labels, remote script functions, and statement budgets: none.

CARD-1782 table: `scripts/lib/release-impact.ps1` has no row. The test edit matches `tests/Antiphon.Tests/**/*.cs`, row `test-lane-category`, class `TestLaneCategoryGuardTests`. No row is added.

## Performance budget

Selection: `ReleaseImpactEnvelopeTests` in `tests/Antiphon.Tests`, Linux, one checkpoint row TestDesign will name. No AppHost, database, container, migration, or snapshot. The existing fixture builds a tiny git repo (the fourteen algorithm files, not the product tree) once per argument. Isolation stays per argument because the test mutates that repo. Teardown is the fixture's own directory delete, already capped at 30 seconds. Phase lines already printed: `C1820 phase=pwsh`, `phase=git`, `phase=cleanup`.

No measured p95 exists for the expanded argument list. Sibling plan `docs/superpowers/plans/2026-11-13-card-1820-drift-checkpoints.md` budgeted CP-108 at 3 minutes for the then ten-result class. This class currently has eight `[Test]` methods, one of them four arguments. Adding twenty-four arguments is provisional at about 18 seconds each (the sibling's class average, not a new sample): about 7 minutes of new drift work plus the current class, about 10 minutes wall for the class after one ordinary test build. Row timeout 20 minutes, about twice that provisional class estimate. The existing per-command ceilings stay 120 seconds for pwsh and 60 seconds for git. Build time and build-slot admission are separate and are not part of the 10 minutes. A run over 30 minutes needs a Debug task with timing evidence. TestDesign replaces this provisional average with a measured p95 or records why it is still provisional.

`CollisionAndAlgorithmCoverageForceFull` is an existing admission script test. Historical rc timing put that one case near 17 seconds (`docs/investigations/2026-11-15-why-missed-g136-rcbaseline-timeout.md`). If TestDesign includes it, budget 1 minute and a 2 minute row timeout. Do not fold it into the envelope row.

No session that waits for input is added. Parking and CARD-1083 do not apply.

## Out of scope

S1R5 script closure, S1R6 composition, canonicalizer introduction, evaluator-to-envelope wiring, admission `complete=true`, carry credit, native Windows W1, CARD-1866 A-2, and the M7 mutation controls. CARD-1836 S5c stays fail-closed and is a different commission.
