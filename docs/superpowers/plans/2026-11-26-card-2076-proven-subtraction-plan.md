# CARD-2076. Proven subtraction in the release impact selector

A compiler binding may remove a false `type` reason. A chunk becomes eligible for carry only when that was its last reason to execute and its own receipt is reusable. Ship the proof behind a default-off flag. Run Full while measuring the proposed selection. A single failure in a chunk it would have skipped closes the gate and becomes a regression fixture.

This is the 2026-11-26 operator amendment to the ambiguous-symbols row and D-2 of [selector v2](2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md). It applies to release selection. CARD-1836 owns qualification and CARD-2069 owns verification of candidate evidence. [Incremental qualification plan](2026-11-25-card-1836-incremental-windows-qualification-plan.md) · [Replay test design](2026-11-25-card-1836-incremental-windows-qualification-test-design.md) · [P2 integrity plan](2026-11-26-card-2069-p2-integrity-plan.md) · [P2 test design](2026-11-26-card-2069-p2-integrity-test-design.md)

## Ground truth

Inspected at `771bc1a46afe5893894b050f4da47effb6c758e7`, against Antiphon CARD-2076 revision 2 and the Final report of Review `75490c86-12f9-4e93-800b-c1e14ba465b8` on `fbc63753595acbf5893276ab226f42810a8b0eae`. The brief dates the advisory November 26. The tracked investigation is dated November 25. [CARD-1868 advisory and receipts](../../investigations/2026-11-25-card-1868-advisories.md)

| Assumption | What the implementation does | Design consequence |
|---|---|---|
| One helper selecting 1,978 consumers means that many launches can be saved | The S1R2 review counted 1,978 consumers on the largest non-global path and 2,090 identities overall. `ReleaseImpactGraphBuilder.Build` maps each `SimpleNameSyntax` in a test declaration to every same-named type file, then coalesces kinds per path and consumer. Delegates now contribute paths. `ReleaseImpactProjectGraph` also selects whole affected projects. | Reproduce the historical maximum by path at `b8f1e4e3530c5d9f94ce837de4e60082f4f9e5d9`. Report removed reasons, affected classes, changed chunk decisions and saved launches separately. Independent project closure can make the last two counts zero. |
| The derived index and release relation are interchangeable | `DerivedImpactIndexBuilder` writes ordinary schema 1, simple class names, cap 25. The separate driver `--release-graph` reads pinned Git blobs and produces an uncapped v1 relation with project-qualified identities, `complete=false` and Full. It loads bytes for C# files and supplies empty content for other tracked paths. Aliases declared outside test bodies need additional input. | Preserve ordinary regen, drift and MergeSubset contracts. Introduce an opt-in, versioned release artifact with typed contributions and source evidence. Reading an unchanged v1 artifact grants no subtraction authority. |
| Project evaluation already provides a compiler model | `tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs` evaluates surviving MSBuild items, imports and project references per target. Its receipt explicitly marks assembly metadata, generated provenance, environment and platform unproven. The syntactic graph guesses owning projects by directory. | Use actual compiler inputs at both commits. Treat missing compiler-input custody as a reason to keep candidates. A valid MSBuild item list alone cannot attest a binding. |
| The current oracle scores the proposed release graph | `RcSelectionReplayCommand` delegates to `RcSelectionReplay.Run`, which calls `RcChunkSelection.Select`. That selector reads the **two committed schema-1 blobs**, derives project closures on both sides and applies caps, reader hits and receipt checks. It does not read `--release-graph`. The replay identity hashes revision/format identifiers. | Feed the refined relation through one shared selection path for replay, shadow and admission. Include the executed proof implementation, policy and bindings in the new selector identity. Running today's replay unchanged would measure a different selector. |
| A replay pass or graph checksum authorizes skipping | The existing oracle writes `enables=false`. `RcIncrementalCut.Admit` checks approval, trust hash, shadows, inventory and Full sweep age. Its diagnostic executor has no production call in `NightlyTests`. P2 plans independent consumer recomputation. | A matching digest and exit 0 are inputs to admission. The qualification owner checks custody, proof parity and current obligations before giving carry credit. |
| A failure row already explains why selection missed it | Replay scores a predicted carry against actual FAIL and writes run, chunk/content, first failing case where available, SHA, baseline, reason and TRX. The skip trace need not identify a missing edge or even a failing case for a chunk-level failure. CARD-1781 owns the why-missed note. | Join the sealed prediction, typed edge decisions, complete diff and actual outcome. Record unknown attribution explicitly, then link its diagnosis and repair. |

## Decisions

**D-1. Use Roslyn over a verified compilation.** A collision in a simple name starts a binding query. Syntactic indexing remains a cheap candidate generator. Only a successful query at the actual use site can justify elimination. A compilation supplies the source files, references and compiler options that give a name its meaning. [Roslyn semantic analysis](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/semantic-analysis) · [C# using directives](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/using-directive)

| Candidate | Cost to build the index | Decision and reason |
|---|---|---|
| Evaluated Roslyn compilation with source/metadata provenance | Read and hash compiler inputs once per project, target and endpoint. Construct compilations in reference order. Reuse a semantic model per tree and query only supported candidate sites. Measure cold build and warm reuse. | **Recommended.** The compiler supplies scope, overload and alias identity. Missing target inputs have a deterministic fallback. |
| Syntactic resolver using namespaces, imports and alias maps | Extends the current parse and dictionary lookups. Cross-file imports, references, generators and compiler options would still require project evaluation. | Reject for subtraction. A lone lexical match can be wrong. A syntactic resolver may add candidates or flag ambiguous sites for the semantic pass. |
| Retain every candidate | Existing positive graph cost and no semantic index build | Keep as the off path and fallback. It carries the measured fan-out and supplies a matched comparison during rollout. |

**D-2. Define proven precise at one occurrence.** Let B be the comparison commit and A the candidate. At each applicable endpoint build the conservative set of candidate declaration paths for a consumer and use site. Include positive targets of qualified/alias uses, including directives outside the declaration. Preserve the candidate set before attempting any refinement.

1. Bind the endpoint tree and blobs, evaluated project/assembly identity, target OS/architecture, TFM, RID, configuration, defines, language version, compiler/SDK, parse and compilation options, resolved reference bytes and aliases, generated sources, global/implicit usings and linked file ownership. Use an immutable compiler-input manifest tied to a verified build or independently reproduced equivalent. Materialize the compilation from those bytes. Never substitute the analyst's installed assemblies, ambient properties or a convenient `obj/` directory. Reject source drift, missing projects, references, generated bytes or compilation errors for the affected context. Record the evidence per linked owner and required configuration.
2. Query the full relevant syntax in context. Require one non-error bound symbol of the supported kind, no alternative/error candidates, and an independently mapped, complete declaration identity. For a type, normalize its original definition and key by defining project/assembly, namespace, containing metadata names and arities, then map to **all** source parts. Verify the winner's files are present in the positive set or add them before refinement. Never use a zero-result lookup, the first candidate, a display string, a simple-name count, or a symbol from another compilation as proof.
3. With that proof remove only alternative `type` contributions at that use. For every ambiguous, failed, unsupported or unverifiable query **include all** conservative candidates and record the reason. When their extent cannot be enumerated, include the entire independently bounded affected project/downstream universe. Unknown ownership or unbounded dependency closure gives `complete=false`, Full and zero carry credit. An empty name bucket with no bound is unknown.
4. Union surviving contributions over every occurrence, partial consumer, linked owner, admissible configuration and both endpoints of the diff. For deletions retain B, for additions retain A, for moves/rebindings keep old and new ownership. A path-to-consumer `type` edge disappears only if every contribution to that edge is disproved. Independent `declaration`, literal path, global, runtime, script, project closure, hand rule, cap-widening, changed-index, trust-gate, failure and receipt obligations each still apply. A proof of which type a name denotes cannot by itself erase a project dependency or establish absence of reflection, DI, generated or transitive references.

**D-3. Carry auditable evidence to the selector.** Use a versioned, uncapped release artifact per endpoint. Record the source blob/span and use kind, consumer `(project, full metadata name)`, configuration digest, rule ID and resolver version, complete candidate set, winner and its declaration paths, dropped alternatives or include-all reason. Deduplicate large sets by content digest. Bind graph, input manifest, active flag and algorithm/policy digests in predictions. Keep stable ordinal ordering and exact relative paths. A resource limit invalidates the proof for the affected scope. It cannot truncate candidates.

At `RcChunkSelection.Select`, accept a validated proof input from the pinned release graph producer. Preserve the default overload. For each existing index hit, account for every contribution using the complete pre-cap origin relation before applying any removal. Schema 1 merges class names and loses reason provenance. If a legacy hit cannot be reconstructed and mapped one-to-one to its contributors, keep it in full, including every matching short-name consumer. Compose proven differences **before** flattening reasons or matching chunks. Join retained classes to frozen project/assembly, fully qualified class, method and case membership. An ambiguous join widens or forces Full. Reuse P2 independent input enumeration and recomputation at admission. A resealed, missing or stale proof, a candidate-controlled flag, or an off-mode receipt replayed in on mode cannot authorize carry. Extend `RcAdmission.RequiredAlgorithmPaths` and algorithm identity coverage to the new files.

**D-4. Choose five consecutive runs and a default-off switch.** Use the existing CARD-1836 cohort size, **N = 5**, and its separate predecessor. The older selector-v2 D-7 check of 50 first-parent diffs is a dependency proxy. Retain it and its two named pairs. For its deliberate broad type positives, reconcile each removed pair against independently expected bindings, retaining every unexplained omission as a failure. The behavioral release gate is the five actual Windows x64 RC outcome sets. Add `--proven-subtraction` as an opt-in analysis flag at the release driver and replay CLI. Absent is false, recorded in every versioned artifact. At cut and dispatch, take eligibility only from a version-matched, independently verified qualification approval. A flag alone can request a prediction while Full executes.

## Resolution cases and mutation fixtures

Each row is a pair of hand-authored worlds. Give the real declarations different files, keep the expected paths independent of the resolver, and assert exact edge **kinds** as well as the selected consumers. For a supported positive, show one named decoy was removed and every winner retained. In its ambiguous, incomplete or unsupported twin, assert the full conservative set survives with its reason. TestDesign will name the exact assertion a method-scoped mutation must turn red for each row.

| Case | Binding rule | Fixture change that must preserve edges |
|---|---|---|
| Namespace qualification and local `using` | Bind `global::N1.Probe`, namespace-qualified and unqualified occurrences in file- and block-scoped namespaces. Resolve each occurrence in its own scope. | Start with `using N1` and two `Probe` declarations. Import `N2` as well for an ambiguous unqualified use. Also keep a qualified use of each in the same consumer so one proof cannot erase the other. |
| Type, namespace and reference aliases | Follow the actual alias symbol to its final declaration. Include RHS dependencies of directives such as `using H = N1.Probe` even when only `H` appears in the test. `extern alias` requires the exact reference alias map. | Change alias target between B and A. Break the RHS, duplicate the alias or omit one reference-alias binding. Keep both sides' real targets and all possibilities for the failed side. |
| Partial classes and partial consumers | Several syntax declarations of one logical type are one identity. Retain every declaration file, including generated pieces with verified provenance, and examine all consumer pieces. | Add a second part containing the only real reference to the decoy, then omit a part from the manifest. The first keeps both reasons. The second refuses pruning. A same-named type in a different assembly is a different symbol. |
| Global usings | Include every active `global using` and global alias in the project, even from a different file. Bind in project scope. | Add a competing global import in a sibling file, then test a linked file compiled by two owners with different globals. Both answers survive. |
| Implicit usings and MSBuild `Using` items | Require the actual generated global-using tree and its SDK/project inputs for the target. Include additions/removals and aliased or static imports. | Introduce the competing namespace through `<Using Include>` or a different implicit SDK set. Remove the generated-source receipt in the twin. A guessed SDK list cannot eliminate a candidate. |
| Nested/inherited types and `using static` | Bind the declaring type chain, arity and assembly, including an inherited nested type. Static imports can put nested types in scope. | Two containers expose `Inner`. Add competing static imports or an unbound base class. An ambiguous `Inner` keeps both containers' declaration paths. |
| Generic arity | Bind `G` and `G<T>` via their definitions, including constructed/nested generics. Preserve independent dependencies from type arguments and constraints. | Introduce a second imported `G<T>` of equal arity or break an argument binding. An arity filter alone must keep both possibilities in the ambiguous case. |
| Extension methods | A reduced invocation needs a unique applicable method, its original static declaration, containing type and receiver binding. Add/retain provider and receiver edges even if the provider name is absent. Only refine an attributable speculative type contribution. | Competing accessible extensions for the same receiver, a dynamic receiver or missing reference keeps the possible providers. Use a unique extension and a same-named ordinary member as controls. An unsupported extension syntax takes the same fallback. |
| Type forwarding | A forwarded symbol needs a unique defining assembly/type and a complete, verified facade-to-destination chain mapped to source or pinned inputs. Retain the forwarding assembly/attributes, reference inputs and destination dependencies. | Provide two tiny reference assemblies, then remove the destination or change a forwarder to an unbound assembly. Retain candidates and widen if source ownership cannot be bounded. Treat unimplemented forwarding as include-all. |
| Shadowing by a local, parameter, type parameter or member | Bind at the occurrence. A value or type parameter with the same spelling supplies no proof of a unique type declaration under this rule. Keep all type candidates for that occurrence. | Pair an explicit type with `Probe` as a local, field/property and parameter in separate scopes. Move one use under a shadowing declaration. Reject pruning on that use even when another use resolves. |
| Conditional compilation and multi-targeting | Evaluate actual defines for each required project/target on each endpoint. Union active bindings. Require complete context coverage before dropping an alternative. | Flip `#if` between two `using` targets at B/A, and between two target configurations sharing a source file. Remove one define/configuration receipt or encounter an unknown branch and include all. |
| Other symbols, unsupported syntax and errors | Delegates, structs, enums, interfaces, attributes (including suffix elision) need a verified definition. A parser/binder error, dynamic lookup, ambiguous attribute name, unknown generator output or unsupported construct takes the conservative path. | Use a nested generic delegate as the known winner. Add an unresolved alias, two valid attribute spellings, or a malformed reference and keep the possible edges. An empty candidate bucket with unknown owner must end Full. |

## Replay gate, rollout and rollback

1. Ship v2 dark. Verify flag-off parity at the driver, replay and qualification boundaries. On identical pinned B/A trees compare the new conservative, **unpruned** relation with its pruned twin so newly discovered positives cannot masquerade as savings. Also compare both with the deployed selector. Retain every conservative difference, its proof or fallback and the reason each chunk ultimately runs or carries. A synthetic compiled two-project fixture must lose a real false type hit, keep a real consumer and change the predicted chunk decision when no independent obligation remains. Add a project/runtime reason back and the chunk must execute.
2. Have the qualification owner freeze the last five actual runs preceding an explicitly recorded UTC `--as-of`, in chronological order, on the applicable line/profile/target, plus their predecessor. Verify cohort completeness against run custody, manifest hashes, run IDs, attempts, B/A Git objects and target compiler inputs. Use the same cohort when comparing both arms or repairing a miss. A missing, unscorable or inconvenient run cannot be replaced by an older green. Record strict receipt-based opportunities separately from counterfactual reconstructions. Diagnostic invocation after implementation:

   ```sh
   scripts/antiphon replay-rc-selection --proven-subtraction \
     --input-root <read-only-custody> --output <fresh-external-directory> \
     --as-of <yyyy-MM-ddTHH:mm:ssZ> --repository-root <git-repository>
   ```

3. Seal each proposed run/carry partition, per-site proof digest and both branches of every trust hold **before scoring**. Reveal only own-gate results to resolve a hold, seal the resolved partition, then compare against independent Full outcomes. Advance the chained history with selected executions only. Digest the resolved trace as well as the initial prediction. Count every proposed carry with actual FAIL as **skipped-but-failed**, including intermittent failures. A carry against skipped, timed-out, otherwise nonclean or unknown evidence is unsafe. Reconcile each roster exactly once. Report per run and suite the run/carry × actual matrix, missed cases, incremental carries caused by subtraction, saved launch cost and cost of analysis. Keep teacher-forced diagnosis outside the verdict.
4. Require five reconciled runs, **zero skipped-but-failed in every run**, zero other unsafe carries, nonempty carry opportunities, verified inputs and the mutation fixtures above. Exit 1 is hard red, exit 2 invalid input, exit 3 insufficient evidence, exit 0 a replay criterion only. Historical evidence that never exercises a proof-dependent carry is insufficient to qualify this switch, even if the existing generic oracle returns 0. Record this disposition explicitly. Then require two distinct prospective Windows Full shadows under the same proof/selector and trust policy, nonempty proposed carries and zero discrepancies, with native evidence for the intended target. Recheck the latest five before approval if newer runs exist. Retain the replay and shadow IDs, artifact digests, algorithm/policy/target identity and approval revision. No Plan-stage build, pilot or native qualification is needed to decide the design.
5. Only the qualification owner can enable a qualification-only pilot once the P2 consumer and post-discovery dispatch/closure wiring are available. Require the whole existing CARD-1836 admission contract, including a current Full anchor younger than seven days, current strict receipts, exact owed/carry reconciliation and independent publication authority. A synthetic verdict, a prior selector's approval or a Linux fixture grants no Windows carry. Continue shadow comparisons, audit samples and periodic Full execution.
6. On a miss, unsafe carry, stale proof/approval, changed proof inputs or withdrawal, disable subtraction and withdraw the affected approval. Before resume, first skip and closure, recheck revocation and evidence. If carry was planned or credited, seal that attempt incomplete, preserve its traces and taint its carry origins/descendants for this decision. Schedule a distinct Full at the pinned candidate and verify the whole expected roster. Only fresh Full evidence can restore affected credit. Preserve both the failed and repaired verdicts. A new proof version needs fresh five-run replay and both prospective shadows.

## Miss feedback loop

**A miss starts with an observation.** Replay, a prospective Full shadow, a pilot audit or a later Full backstop can discover one. Join an actual failing case to a sealed decision to skip that exact chunk/content on the same candidate, target and roster. A failure from a different commit opens an investigation until the original omission is established. A failure in a chunk predicted to run is a caught failure. Open failures and other unsafe carry outcomes still block admission even when no failing test identity can be recovered.

Use the same versioned writer after these joins. For replay write `misses/<missId>.json` under its fresh output root. For shadow/audit use the qualification recorder's durable attempt-evidence root and reference the same record format from the attempt. Include the record set's digest in the verdict. The evidence owner retains the original run, predictions and artifacts. A stable ID over selector, target, B/A, run/attempt, chunk content, failing case (or a chunk-level sentinel) and evidence digest makes retries idempotent. Write atomically and preserve conflicting observations as an integrity failure. A failed evidence write leaves the approval closed. Corrections and disposition append a linked revision to a miss. They cannot replace the original failure.

| Record fields | Required contents |
|---|---|
| `schema`, `missId`, `observedBy`, `runId`, `attempt`, `target`, `baselineSha`, `candidateSha` | Version, replay/shadow/audit provenance and the exact comparison. Include time, repository/line, suite, chunk ID and content digest. |
| `failingTest`, `actualOutcome`, `actualEvidence`, `predictedDecision`, `predictionDigest`, `originReceiptDigest` | Project/assembly, full class/method and case key for **each** failing case. Reuse the frozen roster and protected TRX or equivalent evidence reference and hash. Emit a chunk-level record with a typed unknown when the row has no case identity. Preserve its hard-red disposition. |
| `changedFiles`, `selectorVersion`, `policyDigest`, `graphDigests` | Complete ordered status/old path/new path list from the B/A Git diff, or a digest and verifiable, lossless manifest reference. Include resolver version, active flag, executed algorithm digest, target-input bindings and both endpoint graphs. |
| `edge`, `attribution`, `repair` | `edge` carries endpoint, changed declaration/input path, consumer identity, reason kind, use-site/rule ID, before/after candidates and proof reference if present. Distinguish `wrongly-dropped`, `absent` and `unresolved`. For `absent`, identify the expected edge reconstructed during diagnosis. For `unresolved`, retain candidate paths, available trace and what must be established. `repair` links the owner card, why-missed note, fixture, fix SHA, rerun verdicts and status. |

For example, a sealed carry of `ConsumerTests.HandlesEmpty(input=0)` fails on Full. The changed file declares `Beta.Probe`. A trace showing that the `global-using` rule removed `Beta.Probe → ConsumerTests` points to a wrongly dropped edge. If the changed file was read through reflection and never appeared in the pre-subtraction graph, diagnose an absent runtime edge. If neither claim is proved, record the miss immediately with unresolved attribution. A large diff alone cannot identify the causal file.

The RC orchestrator opens or links one Debug cause card and the CARD-1781 why-missed note. The selector owner reproduces the missed case with minimized inputs, adds the missing positive edge or tightens/disables the faulty proof, and adds the enclosing replay regression. For comparison, the old algorithm must produce the documented omission and the repaired algorithm must select the previously missed chunk. Keep a control that still prunes a genuine decoy. Update the following surfaces in the repair, linking the miss ID, the changed rule and its fallback. Shared causes can link one repair from multiple immutable records. Close the repair only after the retained incident and latest gate have been rescored under the new version and affected coverage has been recovered. Execution remains Full until admission is restored.

| Selection instructions to update | What the reader needs |
|---|---|
| `docs/release-gates.md` | Flag default, exact gate, interpretation of replay exits, authority for the pilot, withdrawal and Full rerun. |
| `docs/testing-and-build.md` | Release and ordinary index contracts, supported proof cases and fallback, checkpoint/replay procedure and how to reproduce a miss. |
| `.claude/skills/antiphon-rc-qualification/SKILL.md` | At the failed-chunk step, compare predicted and actual work, retain the structured record and request the Debug fixture, repair and Full rerun. |
| `docs/orchestration-loop.md` and `docs/investigations/why-missed-template.md` | Own the dispatch and closure procedure. Extend the current five-item note with a miss ID, edge disposition and replay/fixture receipts. Preserve the historical notes. |
| `server/Bundles/orchestrator.md` and `.claude/skills/antiphon-orchestrator/SKILL.md` | Carry a short mandatory pointer to the owner section in the actual launch prompt and orchestrator skill. The generated `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS` / `GET /api/orchestrator-instructions` projects live settings. Maintain the rule in tracked owners. Respect the orchestrator bundle's body/argv budgets and update its LF hash witness. [Bundle contract](../../../server/Bundles/README.md) |

## Implementation slices for TestDesign

Paths below are relative to the checkout. TestDesign owns the verification appendix, fixture data, mutation-to-assertion mapping and final dispatch selections. Stage S1–S5 independently so each gets one short red witness and one narrow green. Real cohort and native qualification evidence belong to rollout.

| Slice | Files and entry points | Red witness and acceptance |
|---|---|---|
| S1. Bind a compilation | Extend `tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs` and its release input command/receipt alongside v1. Extend `scripts/regenerate-derived-impact-index/Program.cs` and `Antiphon.DerivedImpactIndex.csproj` in that directory to accept the versioned manifest and opt-in flag. Add a small compiler-input reader shared with `tests/Antiphon.Tests/Scripts/ReleaseImpactEvaluationTests.cs`. | A verified tiny project with a cross-file global alias can produce a usable compilation. Removing/changing a generated tree, reference alias, option or target binding preserves all candidates and denies proof. The fixture uses pinned compiler and reference inputs independent of the reader. |
| S2. Implement occurrence-level proof | `tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs`, new sibling `ReleaseImpactTypeResolver.cs`, linked into the release driver project, and `ReleaseImpactRelationTests.cs`. Extend the existing graph build entry point to emit versioned proof data. Use small fixture sources outside the scanned C# universe when stored as data. | Independently specify file-to-consumer sets for every paired resolution case above. Exercise multiple sites, partial consumers, delegate-only sources, linked contexts, alias-only references and overlaps with a literal/global reason. Off mode reproduces legacy v1. |
| S3. Compose both endpoints at chunk selection | `scripts/lib/Antiphon.Nightly/RcChunkSelection.cs`, `ReleaseImpactEvidence.cs`, `RcAdmission.cs`, and `tests/Antiphon.Tests/Scripts/RcProjectSelectionTests.cs`. Integrate with CARD-2069's P2 verifier once available. Bind the flag and all proof inputs to the executed selector identity. | A fixture diff with a decoy in an unrelated project changes exactly one chunk decision. B-only references, ambiguous same-short-name joins, closure/reader hits, stale proofs, cap widening and missing origins still execute or require Full. Show a genuine matched carry and the named reasons for both arms. |
| S4. Score the real selector and retain misses | `scripts/lib/Antiphon.Nightly/RcSelectionReplay.cs`, new sibling `RcSelectionMiss.cs`, `scripts/lib/Antiphon.Scripts/RcSelectionReplayCommand.cs`, `tests/Antiphon.Tests/Scripts/IncrementalSelectionSoundnessTests.cs` and its `ReplayWorld`. Reuse the same scorer/writer at the Full shadow/audit evidence boundary. | On a five-run fixture with a sealed proof-dependent carry and an observed failure, assert exit 1 and the exact record, diff, case, dropped/absent edge evidence and new selector hash. Drive a genuine missing reader as well as a faulty proof control. Check per-case multiplicity, unknown attribution, no-op retries, unscorable evidence, a clean opportunity and an all-Full cohort. |
| S5. Enforce withdrawal and feed repairs back | `scripts/lib/Antiphon.Nightly/RcIncrementalCut.cs`, `ReleaseCut.cs`, the `NightlyTests.cs` post-discovery seam with CARD-1836, `tests/Antiphon.Tests/Scripts/ReleaseCutTests.cs` and the instruction/why-missed owners listed above. Extend the existing `WhyMissedWriteUpContractTests.cs` under `tests/Antiphon.Tests/Application/` and maintain the pin in `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs`. | Use a qualification fixture with a fake launcher/publisher, matched approval, two Full shadows and an observed miss. A stale or replay-only approval must yield Full at admission and resume, even after an earlier successful check. Observe actual launches, zero carries, closed attempt and preserved records. Issue the repaired graph a new identity and exercise its regression. Full activation awaits the separate qualification commission. |

**Existing tests affected (CARD-1782).** The two plan paths have no matching hand-table glob. For tooling under `scripts/lib/Antiphon.Nightly/**`, `nightly-live-lane` names `LiveLaneRunnerTests` and `NightlyCoreGoldenTests`. For `tools/Antiphon.LandGraphAudit/**`, `land-graph-audit-project` names `LandGraphAuditProjectInstanceTests`. Re-resolve each actual slice. Migrations, public HTTP DTOs/routes, operation labels, remote script functions and statement budgets have no changes in this design. Versioned file artifacts, CLI opt-in and prompt content are its compatibility surfaces. [Impacted-classes table](../../../tests/impacted-classes.json)

Also preserve existing fixtures in `ReleaseImpactRelationTests`, `ReleaseImpactEvaluationTests`, `ReleaseImpactProjectTests`, `ReleaseImpactRuntimeTests`, `DerivedImpactIndexTests` (including staged-driver drift/regen), `ReleaseImpactEnvelopeTests`, `RcCoverageAdmissionTests`, `RcForcedFullTests`, `RcProjectSelectionTests`, `IncrementalSelectionSoundnessTests` and `ReleaseCutTests`. The docs/skill/prompt change affects `WhyMissedWriteUpContractTests`, `CheckpointRepeatDocumentationTests`, `InstructionBundleTests` and `OrchestratorBundleOwnershipTests`. Rebuild the embedded bundle with the tests and retain its independent budget checks. TestDesign should identify required existing assertions alongside the short per-slice selections.

## Performance budget

The change in this Plan is documentation. The code and real replay measurements below are future work. Historical release-driver observations from the S1R2 review were **33, 33, 50 and 46 seconds** per tree, producing 26.5–26.8 MB artifacts. At CARD-1868 the isolated Linux test build took 275 s in the author receipt and the review's red build took 8m40s. The CARD-1836 test design reports a measured nearest-rank p95 of **1.70 s over seven** Linux `C1545_ConsumerSafety` executions. These measurements cover different phases. The new semantic-pass, incremental decision and Windows replay p95s are unknown. [CARD-1836 timings](2026-11-25-card-1836-incremental-windows-qualification-test-design.md) · [Performance standards](../../testing-and-build.md#performance-standards)

| Phase | Initial budget and measurement |
|---|---|
| Index generation | Provisional target ≤2 minutes cold per endpoint, ≤4 minutes per pair, ≤15 s to verify/reuse a warm endpoint. Measure OFF and ON on the same SHA/configuration, toolchain and load. Record wall/CPU, peak RSS, bytes read/written, projects/trees/models reused, sites attempted, proven/retained alternatives, maximum path fan-out and incomplete reasons. Break out Git I/O, project evaluation, compilation/reference loading, binding and serialization. Include A/B, cache miss/hit and the historical 1,978-consumer path. If a verified input exceeds P2's per-object budget, store bounded per-project shards with a complete count and root digest. Missing or oversized proof keeps Full. |
| Shared setup | Evaluate the reference closure once per distinct endpoint and target. Cache immutable manifests by all input/tool/flag digests, bound in-memory models and release them as each project finishes. Share one tiny repo, metadata-reference set and sealed roster per fixture host/run. Give each variant fresh input overlays/output directories. Zero AppHost/web-host/DB/container/migration/restore/clone phases by design. Keep the assembly-local `ProcessSpawnLimit` on child-spawning fixtures. |
| Short checkpoints | Budget 9–10 minutes for a cold isolated build, ≤20 s discovery/startup, ≤5 s fixture data prep, ≤15 s body (≤30 s five-run miniature), ≤5 s teardown. Allow 12 minutes per cold row. Record exact method/case, SHA, SDK, lane, host/load, slot wait, build, process, I/O, test body and cleanup in checkpoint/TRX evidence. Record unknown and unattributed time explicitly. |
| Qualification | Reuse each shared endpoint across adjacent comparisons. Target ≤5 minutes per comparison including scoring, ≤25 minutes for the five-run offline measurement. Record the delta in full index-build and proof verification cost against wall/launch time saved, including instances with no savings. Store phase JSON, prediction hashes, maximum RSS and per-run results in the external replay directory. Use retained native outcome evidence for replay. |
| Timeouts | For CARD-2076 provisionally budget 4 minutes per cold endpoint operation, 30 s per small method (60 s for the replay fixture), and a 24-minute cold row override. Keep the slot-wait clock separate. From the first five comparable already-commissioned observations per method/row and index phase, publish sample size, p50/p95/max and set each timeout near **2 × measured p95** with measured exceptions. A bound that is reached retains candidates or refuses selection. A selection, batch or run forecast or observed over 30 minutes goes to Debug with phase evidence before another run. |

### Round 2 server2 calibration

The recorded round-1 CP-4 timeout was 60.8 s (Review `9c3d5d74`, run `20261126-161311-a2b8`).
Splitting the scenarios alone still left the gate-and-debt scenario at 31.029 s with
29.223 s of setup on `bb6d6d5178e74c9c7c332e54cf735eebee9e9b4b`. Each split replay
now shares its world within its scenario and builds only the extra endpoints it uses.
The S3/S4 fixtures reuse the staged graph driver's actual entry point in one test
process. S1 exercises the standalone graph driver and evaluator.

Five fresh serial TUnit hosts under granted build slots tested
`e089267bdf75aa123ef7324d87edc3e28478ad65` on server2, Debian Linux x64,
24 visible CPUs, .NET SDK 10.0.404 and runtime 9.0.23. All five ran the same
17 methods. Slot wait was zero. Samples 1, 4 and 5 were ordinary admissions
with one-minute loads 20.69→15.84, 19.20→10.66 and 11.89→11.93.
The loaded samples 2 and 3 had observed in-run one-minute loads 27.47 and 31.47,
with start→end loads 15.51→22.84 and 21.81→19.57. Lease hold times were
92, 123, 110, 76 and 93 s. The timings below are whole-method TRX durations in
seconds, including fixture work, with the nearest-rank p95 equal to the max at
this sample size. The limits round up at approximately twice that provisional
p95, subject to the existing 30 s CP-1 ceiling. CP-1's 18.025 s loaded sample
exceeded the initial 15 s target. Its 30 s limit is 1.66× the observed p95.
Discovery, teardown and other unattributed time remain outside the method
measurements where the host did not expose separate spans.

| Method | Samples 1–5 (s) | p50 (s) | p95/max (s) | Timeout (s) |
|---|---|---:|---:|---:|
| `ArchivedWrongAliasWinnerUsesTheReplayScorerAndLeavesAnOpenRepair` | 3.569, 6.458, 3.648, 3.790, 3.250 | 3.648 | 6.458 | 13 |
| `ProvenSubtractionReplayPreservesRepairAndRenameEvidence` | 6.252, 10.774, 11.063, 4.409, 4.203 | 6.252 | 11.063 | 23 |
| `ProvenSubtractionReplayRecordsEverySkippedFailure` | 9.679, 9.315, 13.711, 5.099, 6.941 | 9.315 | 13.711 | 28 |
| `ProvenSubtractionReplayRefusesUnverifiableOutcomes` | 5.509, 9.331, 12.313, 3.415, 5.357 | 5.509 | 12.313 | 25 |
| `ProvenSubtractionReplayRejectsInvalidArtifactsAndCohorts` | 2.657, 3.962, 4.240, 2.046, 2.833 | 2.833 | 4.240 | 9 |
| `ProvenSubtractionReplayResolvesGateAndFailureDebt` | 5.290, 8.567, 8.658, 5.651, 4.939 | 5.651 | 8.658 | 18 |
| `BothEndpointProofsPreserveIndependentlyOwedChunks` | 3.120, 3.624, 3.106, 2.800, 3.296 | 3.120 | 3.624 | 8 |
| `EndpointProofsPreserveIndependentReadersAndAddedConsumers` | 1.775, 3.309, 2.046, 1.880, 2.377 | 2.046 | 3.309 | 7 |
| `EndpointProofsPreserveOwnedAndUnownedCaps` | 1.609, 2.947, 2.011, 1.866, 1.499 | 1.866 | 2.947 | 6 |
| `ProofRequiresBoundCompilerInputs` | 12.823, 18.025, 10.573, 9.689, 14.560 | 12.823 | 18.025 | 30 |
| `ConfiguredBindingsPreserveEveryAmbiguousCandidate` | 4.107, 5.783, 8.007, 3.808, 6.421 | 5.783 | 8.007 | 17 |
| `ScopedBindingsPreserveEveryAmbiguousCandidate` | 4.879, 5.611, 5.163, 5.711, 4.738 | 5.163 | 5.711 | 12 |
| `SdkGeneratedUsingsRetainOnlyPinnedBindings` | 3.195, 2.597, 2.257, 4.239, 2.989 | 2.989 | 4.239 | 9 |
| `UniqueBindingsPruneOnlyTheirOwnContributions` | 8.545, 5.101, 6.000, 4.490, 6.968 | 6.000 | 8.545 | 18 |
| `UniqueBindingsPruneOnlyTheirOwnContributionsAcrossOwners` | 2.395, 3.903, 2.834, 2.359, 3.119 | 2.834 | 3.903 | 8 |
| `UniqueBindingsPruneOnlyTheirOwnContributionsAcrossTypeKinds` | 3.646, 4.950, 4.020, 4.725, 4.163 | 4.163 | 4.950 | 10 |
| `UniqueBindingsPruneOnlyTheirOwnContributionsThroughForwarders` | 3.757, 2.373, 1.832, 1.753, 2.009 | 2.009 | 3.757 | 8 |

#### Cold CP-2 follow-up

Final run `20261126-183901-012c` canceled the original combined owner scenario
at 8.955 s in a cold test host. The first compiler binding alone took 2.745 s.
The test now has independent global-using, linked-owner and extern-alias
scenarios with the same assertions and capture orders. The calibrated cold
follow-up ran at `7a5459226f88930a1a6291a68e8627938202984f` on server2. Every
slot was granted with zero wait. Samples 1, 4 and 5 began at one-minute loads
21.88, 22.07 and 22.40. Sample 2 ended at 36.79. Sample 3 selected each method
in its own cold host at one-minute loads 33.36, 32.50 and 30.13. Each method
passed all five samples. The linked-owner limit stays at the existing 8 s
ceiling (1.59× its observed p95).

| Method | Samples 1–5 (s) | p50 (s) | p95/max (s) | Timeout (s) |
|---|---|---:|---:|---:|
| `UniqueBindingsPruneOnlyTheirOwnContributionsAcrossGlobalUsings` | 4.387, 5.688, 5.635, 6.629, 3.840 | 5.635 | 6.629 | 14 |
| `UniqueBindingsPruneOnlyTheirOwnContributionsAcrossOwners` | 0.773, 1.111, 5.043, 1.999, 0.692 | 1.111 | 5.043 | 8 |
| `UniqueBindingsPruneOnlyTheirOwnContributionsAcrossExternAliases` | 2.485, 3.354, 4.658, 3.905, 1.876 | 3.354 | 4.658 | 10 |

### Round 3 regression evidence

Review `6c753207` observed the combined type-kind method at 3.957, 4.476,
4.591, 6.314 and 9.054 s. The final observation left 0.946 s at the original
10 s limit. The five declaration kinds now have separate case deadlines. The
attribute winner and ambiguous spelling share their own method. Every case
still checks both capture orders and the positive and ambiguous worlds.

Round 3 run `20261126-212133-bd50` built
`5410a2eb6db67fa176665d853872d19ee26a9ad2` on Debian 12 Linux x64 with 24
visible CPUs and SDK 10.0.404. Its five fresh type-kind and attribute hosts ran
30/30 cases green. The first three hosts observed one-minute loads in
26.39–30.53, 23.31–30.30 and 25.75–35.99 respectively. The last two added two
low-priority CPU workers during execution and observed 29.33–33.90 and
20.73–24.92. All test and build slots were granted with zero wait. The table
uses the slowest case per host for the parameterized method and provisional
nearest-rank p95. The attribute limit stays at the original 10 s ceiling,
1.82× the observed maximum.

| Method | Samples 1–5 (s) | p50 (s) | p95/max (s) | Timeout (s) |
|---|---|---:|---:|---:|
| `UniqueBindingsPruneOnlyTheirOwnContributionsAcrossTypeKinds` | 3.074, 2.995, 4.672, 3.760, 2.175 | 3.074 | 4.672 | 9.5 |
| `UniqueBindingsPruneOnlyTheirOwnContributionsAcrossAttributeSpellings` | 5.483, 1.381, 1.327, 1.129, 1.027 | 1.327 | 5.483 | 10 |

The same run recorded 2/5 passes for the cohort test. Three cold hosts
canceled at 9.085, 9.335 and 9.226 s in fixture setup, before the roster
assertions. The cohort test now reuses one real graph from one commit for six
distinct attempts and uses clean fixture outcomes. The other replay tests
still build their distinct endpoints and observe their failed cases. The
cohort deadline stays at 9 s.

The official land-graph refresh advanced the topic digest from `2D9312A5…`
to `49BA60B0…` at count 80. The target pin blob remained
`5c6bebb9830976468ea54c20c640127ab452931d`. Review's original ten-case results
remain in its retained evidence. [Round 2 base red](/work/review-evidence/task-6c753207/base/.antiphon/checkpoints/20261126-200816-4a0e/report.md) · [Round 2 topic red](/work/review-evidence/task-6c753207/tip-impacts/.antiphon/checkpoints/20261126-203714-ca7f/report.md) · [Refetched master green](/work/review-evidence/task-6c753207/master-final/.antiphon/checkpoints/20261126-210209-747a/report.md)

`P.roster` reseals run three with one duplicated content digest in place of
the other required digest. The roster count and the planned chunk files stay
the same. `ProvenSubtractionReplayRejectsInvalidArtifactsAndCohorts` requires
`proof-cohort-roster-mismatch:r3000000`. The count-only control and the M2,
preview and evaluation-target controls remain pending method-scoped Mutation.
[Candidate methods and expected failures](../mutations/2026-11-26-card-2076-candidates.md)

The replay's proof-dependent carry counter can include a carry caused by a
difference between the planned class list and member roster. Inspect the
matched roster and recorded removed contributions before treating a counted
carry as a subtraction witness.

### Checkpoints

Portable managed lane for every row, with target-specific compilations/Windows-profile evidence supplied by small fixtures. Both placement routes were checked. Omit runner and platform pins. Each filter binds a known method roster in one class. CP-1 and CP-3 each execute a single exact method. CP-2 and CP-4 select the scenario methods by their shared prefixes. Check every intended method in the fresh TRX. The same-tip S1–S4 Final can reuse one isolated build while preserving the filters and execution floors. At TestDesign keep each decisive positive and its ambiguous or unbound twin reachable through the production entry point.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c2076-inputs/` | portable-compiler-inputs | `/*/*/ReleaseImpactEvaluationTests/ProofRequiresBoundCompilerInputs` | D-1/2, complete and tampered compiler inputs | 1 passed, 0 failed or skipped | 1 | 12 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c2076-resolve/` | portable-type-proof | `/*/*/ReleaseImpactRelationTests/UniqueBindingsPruneOnlyTheirOwnContributions*` | D-2, paired matrix and overlapping reasons | 11 passed, 0 failed or skipped | 11 | 12 | true |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c2076-compose/` | portable-chunk-selection | `/*/*/RcProjectSelectionTests/BothEndpointProofsPreserveIndependentlyOwedChunks` | D-3, two trees, chunk and receipt joins | 1 passed, 0 failed or skipped | 1 | 12 | true |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c2076-replay/` | portable-miss-replay | `/*/*/IncrementalSelectionSoundnessTests/ProvenSubtractionReplay*` | D-4, blind score, nonvacuity and miss records | 5 passed, 0 failed or skipped | 5 | 12 | true |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c2076-cut/` | portable-cut-withdrawal | `/*/*/ReleaseCutTests/WithdrawnTypeProofRequiresFullOnResume` | Default off, approval, miss withdrawal and repaired version | 1 passed, 0 failed or skipped | 1 | 12 | true |

Commit the compiling regression witness against the conservative seam first. Each red must be the named behavioral assertion from the slice table. Keep its SHA and observed vs expected value. Implement, commit, then execute the same row once green through the checkpoint runner with `--after S1` through `--after S5` as applicable, `--expected-source-sha <commit>` and `--row-timeout 24m`. Wait for the terminal receipt. Compile failures, unknown flags, zero tests and missing prerequisites cannot establish red. Keep the per-case mutation roster for method-scoped controls on the reviewed source. The retained historical replay and two prospective Full shadows are qualification evidence outside the short Code rows.

### Cost

Five independent 12-minute rows give a 60-minute ordinary green floor across five dispatches. Budget one additional 12-minute row per red revision, about 24 minutes per slice for both runs, before authoring and slot admission. Sequence S1 → S2 → S3 → S4 → S5, coordinating P2 consumer and CARD-1836 launch wiring at their boundaries. Next stage is TestDesign.
