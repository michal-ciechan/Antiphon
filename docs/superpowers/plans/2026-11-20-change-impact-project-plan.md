# Change-impact as a separate project (plan)

Task 2aa3ce3b, role Plan. Base `master` 9d3d215e8883b6443c90f4efa464faf4b1efe761 (2026-11-20).
The in-flight S3 M1 v2 owner-map branch read for this plan is `feat/card-task-2dda603e` at
b43f1f5c1ef202a2569ede736810e05af714e8bb (not landed).

Operator direction (2026-11-20): the change tracking that decides which test suites a change
affects must become **its own project**, built on csproj/MSBuild graph knowledge, so that a
separate CLI can later run on other repositories. Operator rules to bake in:

- **M1:** ownership comes from the project closure and its linked items.
- **Unowned:** a path that no rule owns selects nothing and is flagged to the caller.
- **No-op list:** a known list of paths that select nothing.
- **Test-only class:** a change only to test source selects nothing. It is recorded as deferred,
  and the suite runs again when a product edge reaches it.
- **Forced-full roots:** the build graph, policy files and shared test infrastructure.
- **Shadow first:** no credit until the result is proven.

Two refinements followed. The project has its own test project. Its main test corpus is the
**live repository**. Golden fixtures are a small, deliberate set, added only for a case that the
live repository no longer has or that must stay frozen.

This document is the plan only. No code changes.

## Summary

- Add a standalone solution, `tools/ChangeImpact/`, with a library (`ChangeImpact.Core`), an
  optional evaluated-MSBuild provider (`ChangeImpact.MSBuild`), a CLI (`change-impact`) and a TUnit
  test project. None of them references an Antiphon project. Antiphon keeps a thin config and a
  small adapter that turns the generic result into today's `antiphon-rc-input-snapshot-v1`.
- **The port comes first.** Slices CI-3 to CI-9 port exactly the S3 M1 v2 behaviour of task
  2dda603e. The C# output must be byte-identical to `Read-RcInputInventory` and
  `Get-RcInputManifest`. Then `scripts/lib/rc-input-manifest.ps1` becomes a launcher.
- **Byte-identity matters.** `rc-chunk-digest.ps1` hashes every input path, its reason, its edges
  and the unresolved list. A port that changed any reason string would change every chunk digest
  and void every CARD-1836 carry.
- **The operator rules come later.** Unowned flags instead of selecting, test-only defers, the
  no-op list is proven against readers, and forced-full roots are explicit. These rules form a
  config schema v2. They run in shadow next to the parity policy and earn no credit until the
  operator's proof bar is met (O-5).
- **The real-world evaluation test set is the 18 unresolved items** that the S3 walk produces on
  this repository (measured below). Each one has a planned resolution: target pins, an allowlist
  for external SDKs, package roots, a `.gitattributes` parser, or absence records.
- **First slice (CI-1)** can start now on server2. It touches only new files under
  `tools/ChangeImpact/`. It touches nothing in `scripts/lib/**`, no policy, no pin and no
  `Antiphon.sln`, so the live Windows run e7f92f66 and the rc line are untouched.

## Ground truth: what the brief assumes vs what the code does

| # | Brief assumes | Code / measurement | Effect on plan |
|---|---|---|---|
| G1 | "11,130 tracked paths x 27 suites census" | 2dda603e's checkpoint doc records `paths-checked=11132, suites-checked=27, pairs-checked=787, under-selected=0` in 153 s. At base 9d3d215e8, `git ls-files` gives 11,174 paths. 27 suite csproj files match `tests/**/*.csproj`. | The census path count drifts with the repository. The live lane asserts `under-selected=0` and that every suite is checked, never a fixed path count. |
| G2 | "18 unresolved items" | Reproduced. `Read-RcInputInventory -SkipUnresolvedBroadening` at b43f1f5c1 returns `suites=27 unresolved=18`. The run took 3 min 24 s wall (pwsh 7, server2, one revision). See table T-18. | T-18 becomes the expected-unresolved list for the live lane. |
| G3 | "task 2dda603e is building the config-driven ps1 now" | Branch `feat/card-task-2dda603e` has 8 commits. They change `rc-input-manifest.ps1` (266 → 292 lines), `rc-input-owners.json` (7 → 57 lines, 50 roots) and the new `rc-input-reader-census.{ps1,json}` (211 and 253 lines), plus 12 harness cases. Not landed. | The port is frozen at the **landed, Review-approved** 2dda603e SHA. Until then, only the generic foundation (CI-1, CI-2) runs. |
| G4 | Operator rule: unowned selects nothing | S3 does the opposite. `**` is a fallback that selects every suite (`unmatched-path-fallback`), and the case `UnknownPathDirtiesAll` asserts it. Any unresolved item also broadens all suites. | Parity mode (v1 config) keeps S3 behaviour. The operator rule is a v2 policy switch, run in shadow first (D-6). |
| G5 | Operator rule: test-only changes select nothing | S3 selects for test sources: `tests/**/*.cs` → Antiphon.Tests (`derived-impact-scan`), `tests/Antiphon.Tests/**/*.cs` → GitInProcess.Tests, and a test project's own files select that project. | Same as G4: a v2 policy. The suites S3 adds are runtime readers. The v2 rule still has to honour reader evidence (D-7). |
| G6 | LandGraphAudit already evaluates MSBuild | Yes. `tools/Antiphon.LandGraphAudit` uses Microsoft.Build 18.0.2 and Locator 1.11.2 to evaluate every project of `Antiphon.sln` with no restore, plus a canary copy. It is a land **approval** gate for skipping docs-only changes, not a suite selector. `ReleaseInputCommand` is a release-only evaluated inventory. | Reuse the design (recording file system, expression policy, target pins) in `ChangeImpact.MSBuild`. Do not move LandGraphAudit in v1 (D-9). |
| G7 | A generic graph exists only in PowerShell | There is also a C# conservative project graph: `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs` (553 lines), which unions literal conditional alternatives. It sits inside the test assembly. | Its algorithm is a second oracle for the syntactic reader (CI-2). It is not a dependency. |
| G8 | Moving to C# changes `PinnedInputDigest` | None of the 7 pinned paths (`scripts/test-live-lane-run.ps1:24-28`; ps1 plan G3) is a change-impact file. Registering a **new test project** in `tests/test-selection-policy.json` would move `catalogHash`, then `policyHash`, then `PinnedInputDigest` (ps1 plan D-8). Separately, the chunk digests include reason strings and the unresolved list. | Pins move only if the test project is registered (O-6). The chunk-digest churn is avoided by byte-identity (D-4). |
| G9 | Windows PowerShell 5.1 is a constraint | Not for this work. `rc-input-manifest.ps1` and the census are already `#requires -Version 7.0`. `release-selection.ps1` and `rc-ledger.ps1` are 5.1 and stay out of scope. | No 5.1 code is added. A launcher that replaces a 7-only file stays 7-only. |
| G10 | The other agent "already porting scripts" | The ps1-to-C# plan (`2026-11-19-ps1-to-csharp-migration-plan.md`) lists `rc-input-manifest.ps1`, `rc-project-selection`, `release-impact*` (W2-rc) and `impacted-classes.ps1` and `regenerate-derived-impact-index.ps1` (W2-land). Its wave 1 (S1–S12) does not touch them. | Section 3.3 draws the boundary. This plan claims the selection core, and the ps1 plan needs a Docs amendment to strike those rows. |

## Decisions

Each decision is a default that the operator or Review may overturn. The decisions that are the
operator's are listed again in section 6.

- **D-1 One standalone solution under `tools/ChangeImpact/`.** It has its own `ChangeImpact.sln`
  and an isolating `Directory.Build.props`, and it has no ProjectReference into the repository.
  - Why: it can be extracted with `git subtree split` or a copy. It also keeps `Antiphon.sln`, and
    therefore the land graph (`LandGraphAuditGate.cs:279` evaluates `Antiphon.sln` only), unchanged.
  - Rejected:
    - a top-level `changeimpact/` folder: the repository keeps tools under `tools/`.
    - adding the projects to `Antiphon.sln`: every land would evaluate them, and the land-graph
      records would churn.
    - `tests/ChangeImpact.Tests`: `tests/**/*.csproj` is the S3 suite pattern, so the test
      project would silently become suite 28.
- **D-2 Neutral names.** Use `ChangeImpact.*` namespaces and the `change-impact` command, with
  no `Antiphon` in the generic projects. The Antiphon adapter is named `Antiphon.ChangeImpact`.
  The package id is O-1.
- **D-3 Two-tier MSBuild knowledge.** Tier 1 is syntactic: XML read from git blobs at an exact
  SHA. It is the soundness anchor and the parity target. Tier 2 is evaluated: Microsoft.Build
  through Locator, in `ChangeImpact.MSBuild`. Tier 2 may only **resolve** a tier-1 unresolved
  item. It never removes an input. Every item that tier 2 evaluates must already be inside the
  tier-1 closure, and anything else is exit 5 (a soundness failure).
  - Why: tier 1 runs on an immutable commit without a checkout or an SDK. Today's ledger needs
    exactly that, because `Read-RcInputInventory` reads `ls-tree` and `cat-file` at a SHA. Tier 2
    needs files on disk, a matching SDK, and evaluates only the host OS branch of each condition.
  - Rejected:
    - tier 2 alone: no immutable-commit mode, single-OS conditions, an SDK dependency, and ps1
      plan D-1 says it cannot be a single-file exe.
    - tier 1 alone: the 18 items stay unresolved forever, and so the whole repository stays
      broadened.
- **D-4 The port is byte-identical to S3 before any behaviour change.** The adapter must produce
  an `antiphon-rc-input-snapshot-v1` document whose canonical JSON equals the ps1 output for both
  revisions. That covers inputs, reasons, edges, unresolved, `indexBroadened` and `mapObjectId`.
  - Why: `rc-chunk-digest.ps1:36-70` hashes reasons, edges and unresolved. A one-character drift
    changes every chunk identity and voids every ledger carry.
  - Rejected: an approximately equal port with a one-time ledger cold start. It would be invisible
    in tests and expensive in rc time.
- **D-5 Parity is proved against the live ps1, not a stored snapshot, for as long as the ps1
  exists.** This follows ps1 plan D-7: a byte copy goes to `scripts/legacy/rc-input-manifest.ps1`
  in CI-8. A golden copy of the census matrix and truth tables is frozen only at retirement (CI-9),
  and only for the cases the live repository can no longer reproduce.
- **D-6 One result model, two policies.** Every changed path gets exactly one class:
  - `ForcedFull`
  - `Owned` (product edge)
  - `TestOnly`
  - `NoOp`
  - `Unowned`

  Precedence: ForcedFull, then Owned, then TestOnly, then NoOp, then Unowned. A NoOp glob that
  intersects an owner is a `check-config` error, and at run time Owned wins and a
  `noop-conflict` flag is set.
  - The policy is data. A v1 policy reproduces S3: unowned selects all, test sources select
    their readers, and unresolved broadens all. A v2 policy carries the operator rules: unowned is
    flagged and selects nothing, test-only is deferred, and unresolved broadens only the affected
    unit (O-3).
  - Unresolved is an evaluation state on a unit, not a path class.
- **D-7 Definition of TestOnly (v2).** A path is TestOnly when all of the following hold:
  - every owner is a unit with `role: test`;
  - the path is not under a forced-full root;
  - no reader row with `role: product` claims it.

  Test-to-test reader rows, such as a test reading another suite's fixture, stay test-only.
  Shared test infrastructure (`tests/Shared/**`) is ForcedFull by config, not TestOnly.

  The result lists `deferred: [{unit, paths, sinceSha}]`. The consumer owns the debt. In
  Antiphon, that is the CARD-1836 ledger through the adapter. The debt clears when the unit next
  runs for any reason. The bound on debt is O-4.
- **D-8 Unowned is surfaced, never silent.** Under v2, unowned paths are listed in `flags`, and
  the CLI exits 3 ("result with flags"). The caller decides. Antiphon's adapter maps exit 3 to the
  existing fail-closed path: Full, until the operator has approved O-2.
- **D-9 LandGraphAudit stays in place for v1.** It is an approval gate with Antiphon semantics:
  docs-skip, the canary text and the `Antiphon.sln` record. `ChangeImpact.MSBuild` borrows its
  ideas but not its code. A later card can make LandGraphAudit reference `ChangeImpact.MSBuild`.
  That direction (Antiphon → generic) is allowed, and the reverse is never allowed.
- **D-10 No new NuGet packages in Core or the CLI.** Arguments are parsed by hand: GNU style
  `--name value` and verbs, like `Antiphon.LandGraphAudit` and `Antiphon.Checkpoints`. The glob
  engine is a port of `New-RcInputPattern`. `ChangeImpact.MSBuild` reuses the versions already
  pinned in the repository (Microsoft.Build 18.0.2, Microsoft.Build.Locator 1.11.2). The tests use
  the TUnit version the repository already uses.
  - Rejected:
    - System.CommandLine: the same reason as ps1 plan D-2, a new package pin under CARD-1229.
    - Microsoft.Extensions.FileSystemGlobbing: its semantics differ from today's matcher, and the
      parity proof would then cover a translation as well.
- **D-11 Class-level selection is v2 of the library, not v1.** That covers `impacted-classes`,
  the derived-impact index and `SelectorCensus`. v1 is unit (suite) level only.
- **D-12 TFM `net10.0`, `RollForward=Major`.** This matches `global.json` 10.0.204 and
  `Antiphon.Checkpoints`. Multi-targeting for other repositories (for example `net8.0`) is O-7.

## 1. Inventory

Line counts are from `wc -l` at base 9d3d215e8, except where marked "(2dda603e)", which means
b43f1f5c1. Language column: PS7 = `#requires -Version 7.0`, PS5.1 = 5.1-parseable, C# = compiled
or `Add-Type`. **G** = generic logic, **A** = Antiphon-specific.

### 1.1 Suite-level input ownership (the core to port)

| File | Lines | Lang | Generic parts | Antiphon-specific parts |
|---|---:|---|---|---|
| `scripts/lib/rc-input-manifest.ps1` | 266 (292 at 2dda603e) | PS7 | G: the `ls-tree -z` reader with path validation (case collisions, links and submodules, `.gitattributes`); glob to regex (`New-RcInputPattern`); include resolution (`Resolve-RcInventoryPath`); the XML project walk (project directory, ancestor `Directory.Build.props/targets/Directory.Packages.props`, Import, ProjectReference, Compile, Content, None, EmbeddedResource, AdditionalFiles, Analyzer; `Target`, `UsingTask`, `Sdk` and `MSBuild` mark dynamic); owner roots then walk then `**` fallback for unclaimed paths; unknown-path; unresolved broadening; positive index edges; the two-revision union with absence records | A: map path `scripts/lib/rc-input-owners.json` hard-coded; format id `antiphon-rc-input-snapshot-v1`; the SDK allowlist (`Microsoft.NET.Sdk{,.Web,.Worker,.Razor}`); the `root-build-contract` trio; the derived-index edge input; dependency on `test-selection.ps1` helpers |
| `scripts/lib/rc-input-owners.json` | 7 (57 at 2dda603e) | JSON | the shape: `schemaVersion`, `suiteProjects` glob, `roots[]{path,consumers,reason}` | the content: 50 roots, with reasons such as `repository-reader-non-code`, `forced-full-*`, `runtime-reader-*` and `derived-impact-scan` |
| `scripts/lib/rc-input-reader-census.json` | 253 (2dda603e only) | JSON | the concept: a reader row is a group of paths plus the suites that read them, with evidence | the rows: build-slot, run-daemon, runner-restart, custody-shell and others |
| `scripts/rc-input-reader-census.ps1` | 211 (2dda603e only) | PS7 + inline C# | G: an independent oracle that scans test sources for string literals naming tracked paths and checks every pair (path, reading suite) is selected | A: hard-coded exclusions (`ServerGitAssemblyTests.cs`, `combined.pdf`, `TestClassificationMetadata.cs`, and others) |
| `scripts/test-rc-input-manifest.ps1` | 191 | PS7 harness | | the 4 cases behind `RcInputManifestTests.cs` (20 lines, 4 tests) |
| `scripts/test-rc-input-owners.ps1` | 261 (2dda603e only) | PS7 harness | the **S3 truth table**: 12 cases (UnknownPathDirtiesAll, ServerChangeDirtiesAntiphonAndE2E, DocsChangeDirtiesRepoReaders, BuildGraphChangeForcesFull, LinkedSourceSelectsIncludingProject, UnrelatedFileSelectsNothing, RuntimeReaderCensus, ScriptChangeSelectsSessionRunner, DockerAndComposeSelectRuntimeSuites, ExplicitForcedRootsNotFallback, NugetConfigIgnoreCaseForcesFull, ReaderRootsAreEverySuite) | synthetic Antiphon-shaped fixture repository; wrapped by `RcInputOwnersMapTests.cs` (28 lines, 12 tests) |
| `scripts/lib/rc-project-selection.ps1` | 271 | PS7 | G: the ProjectReference graph and downstream (reverse) closure over blobs | A: `tests/rc-project-categories.json` (215 lines) categories and batches (CARD-1545); `scripts/select-rc-project-batches.ps1` (19) |
| `scripts/lib/test-selection.ps1` | 379 | PS7 | G: canonical JSON, SHA-256 digests, the git child process, batched blob reads | A: selection catalog (`tests/test-selection-policy.json`, 3,653 lines; `catalogHash`) |
| `tests/Antiphon.Tests/Scripts/IncrementalSelectionSoundnessTests.cs` | 499 | C# | replays 12 historical rc failures through two pure selections over stored path lists | the historical data is Antiphon's own |

### 1.2 CARD-1820 release evidence and selector v2 (partly generic)

| File | Lines | Lang | G / A |
|---|---:|---|---|
| `tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs` | 553 | C# | **G**: conservative source inventory and reverse project closure over one immutable tree. Literal conditional alternatives are unioned. It is a second tier-1 oracle (G7). |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs` | 149 | C# | mixed: release-only positive relation |
| `tests/Antiphon.Tests/Scripts/ReleaseImpactRuntimeInputs.cs` | 232 | C# | G: source-only runtime positives (fixtures and hooks) |
| `scripts/lib/release-impact.ps1` | 127 | PS5.1/7 | A: release envelope; `Get-ReleaseImpactAlgorithmIdentity` hashes `impacted-classes.ps1` and `release-impact.ps1` |
| `scripts/lib/release-impact-scripts.ps1` | 808 | PS5.1 | G in principle (PowerShell dot-source closure), A in practice; future provider, out of v1 |
| Tests | 584 / 447 / 371 / 232 / 139 | C# | `ReleaseImpactProjectTests` (31), `ReleaseImpactScriptTests` (14), `ReleaseImpactEnvelopeTests` (8), `ReleaseImpactRuntimeTests` (5), `ReleaseImpactEvaluationTests` (4) |

### 1.3 CARD-1836 incremental rc (consumers; Antiphon-specific; they stay)

All of these files are **A**. They consume the selection and stay in the repository; the W2-rc
family of the ps1 plan ports them.

| File | Lines | Lang | Role |
|---|---:|---|---|
| `rc-ledger.ps1` | 1,100 | PS5.1 | `antiphon-rc-ledger-v1/v2` schema, carry decisions |
| `rc-chunk-digest.ps1` | 183 | PS7 | chunk input manifest; hashes the snapshot |
| `rc-boundary.ps1` | 322 | | boundary metadata (aligned with `LayeringGuardEngine`) |
| `rc-admission.ps1` | 321 | | the admission envelope |
| `rc-forced-full.ps1` | 182 | | forced-Full policy over findings |
| `rc-dirty-order.ps1` | 205 | | dirty marking and shadow launch order |
| `rc-incremental-plan.ps1` | 347 | | |
| `rc-shadow-sample.ps1` | 356 | | |
| `rc-baseline.ps1` | 128 | | |
| `rc-run-evidence.ps1` | 144 | | |
| `rc-historical-evidence.ps1` | 23 | | |
| `rc-ledger-{plan,status,stamp-duration,import}.ps1` | 12 / 18 / 30 / 52 | | launchers |
| `scripts/lib/Antiphon.Nightly/RcTagBacking.cs` | 616 | C# | tag backing and backing kind |
| `scripts/lib/Antiphon.Nightly/RcRunEvidence.cs` | 670 | C# | |

The only generic concept here is "dirty units for a diff". After the port, the adapter supplies
it.

### 1.4 Release-selection machinery (Antiphon-specific; stays)

`release-selection.ps1` 493 (PS5.1; intent only, every attempt runs Full, hop and attempt
records), `release-selection-worker.ps1` 15, `release-gate.ps1` 1,111, `release-authority.ps1`
937, `test-change-aware-release.ps1` 412 (harness). They consume a selection and never compute
one.

### 1.5 Class-level selection (library v2 per D-11)

| File | Lines | Lang | G / A |
|---|---:|---|---|
| `scripts/impacted-classes.ps1` | 1,375 | PS5.1 named | mixed: glob rows to classes or methods, plus merge subset |
| `tests/impacted-classes.json` | 572 | JSON | A rows (CARD-1782, 1801, 1819, 1829, 1830, 1926) |
| `scripts/lib/SelectorCensus.cs` | 110 | C# 5 Add-Type | G: class-to-file census from the tree |
| `tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs` | 1,001 | C# (Roslyn syntax) | G: reverse-reference index (cap 25, byte limit 2,000,000) |
| `tests/derived-impact-index.json` | 27,411 | generated | A data |
| `scripts/regenerate-derived-impact-index/` | `Program.cs` 145, csproj 19 (net9.0, Roslyn 5.9.0, TUnit.Core 1.44.0) | C# | G driver; links test sources |
| `scripts/regenerate-derived-impact-index.ps1` | 34 | PS7 | launcher |
| Tests | 1,407 / 1,572 | C# | `DerivedImpactIndexTests` (43), `ImpactedClassesSelectorTests` (31) |
| `scripts/test-selector-census.ps1` 269, `scripts/selector-replay-host.ps1` 54 | | PS7 | |

### 1.6 Land graph (MSBuild evaluation; stays per D-9)

| File | Lines | G / A |
|---|---:|---|
| `tools/Antiphon.LandGraphAudit` (12 files, net10.0, Microsoft.Build 18.0.2 + Locator 1.11.2) | 2,249 | G: `SolutionAudit` (501; evaluates every project, no restore), `BuildInputPolicy` (226; parsed globs and resolved imports), `EvaluationExpressionPolicy` (158), `TargetPins` (231; reviewed target bodies), `RecordingFileSystem` (82), `EvaluationSnapshot` (64), `ReleaseInputEvaluation` (133) and `ReleaseInputCommand` (182). A: `CanaryRepository` (224), docs-skip approval, `Antiphon.sln`, the record format |
| `tools/Antiphon.LandGraphAudit.Core` (9 files, net9.0) | 3,476 | A mostly: `LandGraphPrecheck` (2,246; XML emulation ported from ps1), `LandGraphAuditGate` (464), `PowerShellSemantics` (283), `PsJson` (198) |
| `scripts/audit-land-verification-graph.ps1` / `-precheck.ps1` / `land-graph-land-refresh.ps1` | 169 / 93 / 387 | A |
| `LandGraph*Tests.cs` (17 files) | 4,732 | A |

### 1.7 Totals

- **Ported by this plan (v1 core):** about 1,700 lines.
  - `rc-input-manifest` 292
  - owners map 57
  - reader census json 253
  - reader census ps1 211
  - `rc-project-selection` graph part 271
  - the `test-selection` helpers used: about 120 of 379 (UNKNOWN exact count until CI-1)
  - the harnesses 452, converted to C# tests
- **Generic but deferred (v2):** about 2,600 lines (class-level 1.5, release-impact project graph
  1.2).
- **Antiphon consumers that stay:** about 9,000 lines (1.3 and 1.4).
- **MSBuild-evaluation know-how to borrow:** about 2,200 lines (1.6).

## 2. Target shape

### 2.1 Layout

```
tools/ChangeImpact/
  ChangeImpact.sln
  Directory.Build.props        isolating: does NOT import the repository root; re-declares
                               DefaultItemExcludes bin-*/** (root Directory.Build.props rule,
                               CARD-0110), Nullable, LangVersion; no git SourceRevisionId target
  README.md                    usage for any repository (extractable)
  schema/config.v1.schema.json, config.v2.schema.json, result.v1.schema.json
  src/ChangeImpact.Core/       library, BCL only (D-10)
    Model/        Unit, UnitRole, PathClass, Reason, ImpactResult, Flag, Deferred
    Sources/      ITreeSource; GitTreeSource (ls-tree -z, cat-file --batch at a SHA);
                  WorkingTreeSource (path list or working tree)
    Glob/         GlobPattern (port of New-RcInputPattern; ordinal; per-row ignoreCase)
    Rules/        ConfigLoader (v1, v2), OwnerRules, ReaderRows, NoOp, ForcedFull, Policy
    MSBuild/      SyntacticProjectReader (tier 1), IncludeResolver, AncestorImports,
                  DynamicDetector, ExternalSdkPolicy
    Units/        IUnitProvider; MSBuildUnitProvider (tier 1); (later) NpmWorkspaceUnitProvider
    Selection/    Inventory (unit -> input records), RevisionUnion, Selector (diff -> result)
    Json/         canonical JSON writer (ordinal, LF; parity with ConvertTo-TestSelectionCanonicalJson)
  src/ChangeImpact.MSBuild/    tier 2, optional: Microsoft.Build + Locator; RecordingFileSystem;
                               TargetPin verification; evaluated ⊆ syntactic check (D-3)
  src/ChangeImpact.Cli/        exe "change-impact"; references Core, loads MSBuild only on --evaluate
  tests/ChangeImpact.Tests/    TUnit
    Synthetic/    in-code fixture repositories (temporary git repositories built by the test)
    Live/         [LiveRepo] category: assertions against the enclosing working tree
    Golden/       small frozen set with fixture.json provenance (section 2.8)
    Oracles/      PwshOracle (runs the ps1 while it exists); ReaderCensusOracle (port of the scan)
```

The Antiphon side, which is not generic:

- `tools/Antiphon.ChangeImpact/`: the adapter project (`ProjectReference` to
  `ChangeImpact.Core`). It maps the generic result to `antiphon-rc-input-snapshot-v1`, maps
  units to suite ids (csproj paths), and supplies the derived-index positive edges. It folds into
  `tools/Antiphon.Cli` as `antiphon rc-input …` once ps1 plan S1 has landed.
- `scripts/lib/change-impact.antiphon.json`: the v2 config, from CI-10. Until then, the adapter
  reads `scripts/lib/rc-input-owners.json` and `rc-input-reader-census.json` through a v1 reader
  (CI-3), so there is one source of truth.

### 2.2 Public API (library)

```csharp
public sealed record ImpactRequest(
    string RepositoryRoot,
    RevisionSpec Revisions,              // (BaseSha, HeadSha) | PathList(IReadOnlyList<string>, AtSha?) | WorkingTree
    ChangeImpactConfig Config,           // loaded and validated; carries schemaVersion and policy
    IReadOnlyList<IUnitProvider> Providers,
    EvaluationTier Tier = EvaluationTier.Syntactic);

public static class ChangeImpactEngine
{
    public static Inventory BuildInventory(ITreeSource tree, ChangeImpactConfig config, InventoryOptions options);
    public static ImpactResult Select(ImpactRequest request, CancellationToken token);
    public static PathExplanation Explain(Inventory inventory, string path);
}

public enum PathClass { ForcedFull, Owned, TestOnly, NoOp, Unowned }

public sealed record PathResult(string Path, ChangeKind Change, PathClass Class,
    IReadOnlyList<UnitHit> Units, IReadOnlyList<string> Reasons);
public sealed record UnitHit(string UnitId, string Reason, string? Via);   // Via = edge chain, e.g. "ProjectReference:a.csproj>b.csproj"
public sealed record ImpactResult(
    string Format,                        // "change-impact-result-v1"
    string BaseSha, string HeadSha, string ConfigDigest, string AlgorithmVersion,
    IReadOnlyList<PathResult> Paths,
    IReadOnlyList<string> SelectedUnits, IReadOnlyList<Deferred> Deferred,
    IReadOnlyList<Unresolved> Unresolved, IReadOnlyList<Flag> Flags,
    bool FullRequired, IReadOnlyList<string> FullReasons);
```

`Inventory.ToSnapshot()` lives in the Antiphon adapter, not in Core.

### 2.3 Config schema (JSON, versioned)

v1 is a generic re-statement of today's owner map. v2 adds the operator rules. Example v2:

```json
{
  "schema": "change-impact-config",
  "schemaVersion": 2,
  "units": {
    "providers": [{ "kind": "msbuild", "projects": "tests/**/*.csproj", "role": "test" },
                  { "kind": "msbuild", "projects": "**/*.csproj", "role": "product" }],
    "explicit": [{ "id": "scripts", "kind": "virtual", "role": "test", "note": "nightly scripts suite, no csproj" }]
  },
  "policy": { "unowned": "flag", "testOnly": "defer", "unresolved": "broadenUnit", "fallback": null },
  "forcedFull": [{ "path": "**/Directory.Build.*", "reason": "build-graph" },
                 { "path": "tests/test-execution-policy.json", "reason": "policy" },
                 { "path": "tests/Shared/**", "reason": "shared-test-infrastructure" }],
  "owners":  [{ "path": "docs/**", "units": ["*"], "reason": "repository-reader-non-code", "evidence": "census:docs" }],
  "readers": [{ "group": "build-slot", "paths": ["scripts/build-slot.ps1"],
                "units": ["tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj"],
                "role": "product", "evidence": "census-scan@<sha>" }],
  "noOp":    [{ "path": "docs/cards/**", "reason": "generated-board-mirror", "proof": "reader-census" }],
  "externalSdks": [{ "name": "Aspire.AppHost.Sdk", "effect": "unit-full", "reason": "external SDK targets" }],
  "targetPins": [{ "file": "Directory.Build.props", "target": "SetSourceRevisionIdFromGit",
                   "blob": "<git blob id>", "effects": [], "reviewed": "<review task id>" }],
  "packageRoots": [{ "property": "PkgMicrosoft_Windows_Console_ConPTY", "ownedBy": "PackageReference" }],
  "gitattributes": { "admit": ["text", "eol", "binary", "-text", "linguist-*"] }
}
```

- Rule order is fixed by the engine, not by row order, except within a class:
  1. forced-full;
  2. owners and readers;
  3. unit closures;
  4. under v1, the `**` fallback for unclaimed paths;
  5. unowned.
- `check-config` refuses:
  - a NoOp that intersects an owner, a reader or a unit closure;
  - a reader row without evidence;
  - an unknown unit id;
  - a stale target pin (its blob id is not the current blob);
  - duplicate case-folded paths.
- Under v1, `docs/cards/**` is not a no-op. `docs/**` selects every suite (G4). The example shows
  only the v2 form. The real v2 no-op list is O-2 and must pass the reader-census proof.

### 2.4 Output schema

`change-impact-result-v1`: the canonical JSON of `ImpactResult`, with ordinal-sorted arrays, LF,
and no timestamps, so two runs of the same input give the same bytes. `explain` emits
`change-impact-explain-v1`: for one path, the class, every matching rule, every unit edge chain,
and why other units are not selected. `graph` emits `change-impact-graph-v1`: units, edges, input
records and unresolved. The schemas live in `tools/ChangeImpact/schema/`, and a test validates the
output against them.

### 2.5 CLI

```
change-impact impact       --repo <dir> (--base <sha> --head <sha> | --paths <file|-> [--at <sha>] | --working-tree)
                           --config <file> [--tier syntactic|evaluated] [--json] [--out <file>]
change-impact explain <path> --repo <dir> --at <sha> --config <file> [--json]
change-impact owners <unit|path> --repo <dir> --at <sha> --config <file> [--json]
change-impact check-config --repo <dir> --at <sha> --config <file> [--census]
change-impact graph        --repo <dir> --at <sha> --config <file> [--json] [--skip-unresolved-broadening]
```

| Exit | Meaning |
|---:|---|
| 0 | result produced, no flags, nothing unresolved |
| 3 | result produced **with flags or unresolved** (v2: unowned, noop-conflict, deferred debt over its bound); the caller decides |
| 2 | invalid usage or config (check-config refusal) |
| 4 | evaluation or git failure (no result) |
| 5 | soundness self-check failed: tier-2 item outside the tier-1 closure, or `--census` under-selection |
| 64 | unknown command (same as `Antiphon.LandGraphAudit`) |

`--skip-unresolved-broadening` is today's census seam (`-SkipUnresolvedBroadening`). It is valid
only for `graph` and `check-config --census`, never for `impact`.

### 2.6 MSBuild evaluation

| Concern | Tier 1 (syntactic, default) | Tier 2 (evaluated, `--tier evaluated`) |
|---|---|---|
| Input | git blobs at an exact SHA; no checkout, no SDK | files on disk at the SHA (disposable worktree or archive); SDK through Locator |
| `Directory.Build.props/targets` | every ancestor added as input (over-approximation, as today) | the actual import chain |
| Conditions | ignored; every alternative unioned (monotone) | evaluated for the host OS only; the other OS branch stays unresolved unless pinned (LandGraphAudit `TargetPins` D-5) |
| Globs | matched against tracked paths at the SHA | MSBuild globbing with `DefaultItemExcludes` |
| SDK-style default items | project directory subtree (`project-tree:`) | evaluated `Compile`, `None`, `Content` and `EmbeddedResource` |
| Properties in paths (`$(X)`) | `unresolved-expression` | resolved, or recorded as an external root |
| Targets, `UsingTask`, `<MSBuild>` task | `dynamic-build:` unresolved | not executable by evaluation either: a **target pin** (reviewed body, blob id, declared effects such as "builds project P" or "generates Compile from X") resolves it; a changed body unpins it |
| Speed (server2) | ps1 today: 3 min 24 s per revision for 27 suites. Target under 15 s in C# (ESTIMATE; CI-3 measures) | UNKNOWN for this solution; `LandGraphAudit` has an evaluation budget ceiling (`EvaluationBudget.ToolCeilingSeconds`); CI-11 measures |

The tier-2 provider reuses the LandGraphAudit lessons: a recording file system (no filesystem
functions admitted), no restore, `--artifacts-path`-style isolated evaluation, and no state
written into the repository.

### 2.7 T-18: the 18 unresolved items

Measured at b43f1f5c1 on server2. They are the real-world evaluation test set, and the live lane
asserts this exact list (section 2.8).

| # | Item | Cause (from the file) | Planned resolution |
|---|---|---|---|
| 1 | `dynamic-build:Antiphon.AppHost/Antiphon.AppHost.csproj` | `<Sdk Name="Aspire.AppHost.Sdk" Version="9.3.0" />`. `global.json` `msbuild-sdks` pins 9.5.2 (which wins is UNKNOWN). | `externalSdks` row: effect `unit-full` for the AppHost unit and its dependents |
| 2 | `dynamic-build:Directory.Build.props` | `Target SetSourceRevisionIdFromGit` (Exec git) | target pin, no effects; the file is forced-full anyway |
| 3 | `dynamic-build:Directory.Build.targets` | `Target AntiphonResetBloatedFileWritesLedger` | target pin, no effects |
| 4 | `dynamic-build:tests/Antiphon.Agents.Pty.Tests/…csproj` | 2 `Target`, 3 `<MSBuild>` (staging fake providers or pty-host) | target pins whose effects name the projects built (they become ProjectReference-like edges) |
| 5 | `dynamic-build:tests/Antiphon.E2E/Antiphon.E2E.csproj` | 2 `Target`, 3 `<MSBuild>` | same as 4 |
| 6 | `dynamic-build:tests/Antiphon.MarkdownPdf.Tests/…csproj` | `Target` / `<MSBuild>` (count UNKNOWN; CI-2 lists them) | same as 4 |
| 7 | `dynamic-build:tests/Antiphon.PtyHost.Tests/…csproj` | same (count UNKNOWN) | same as 4 |
| 8 | `dynamic-build:tests/Antiphon.SessionRunner.Tests/…csproj` | 2 `Target`, 1 `<MSBuild>` | same as 4 |
| 9 | `dynamic-build:tests/Antiphon.Tests/Antiphon.Tests.csproj` | 4 `Target`, 8 `<MSBuild>` | same as 4 |
| 10 | `dynamic-build:tests/Shared/CheckpointRepeat.targets` | `Target AntiphonGenerateCheckpointRepeat BeforeTargets="BeforeCompile"` generates Compile items | pin with effect "generates Compile from the declared inputs" |
| 11 | `dynamic-build:tests/Shared/FakeProviderOutput.targets` | `ExcludeRootFakeProviderAppHosts`, `RemoveObsoleteRootFakeProviderAppHosts` (copy-item edits) | pin, no input effects |
| 12 | `external-content-contract:.gitattributes` | filter or eol drivers may substitute bytes | `.gitattributes` parser: admit `text`, `eol`, `binary`, `-text`, `linguist-*`; any `filter=` or `diff=` stays unresolved |
| 13 | `external-content-contract:scripts/fixtures/qualification-graph/s1/.gitattributes` | same | same |
| 14 | `external-content-contract:tests/Antiphon.Agents.Pty.Tests/golden/.gitattributes` | same | same |
| 15 | `missing-input:src/Antiphon.Messaging.FakeGateway/PublicAPI.Shipped.txt` | `src/Messaging.Pack.props:24` `AdditionalFiles Include="PublicAPI.Shipped.txt"`, resolved against the importing project, which has no such file | record an **absence input** (adding the file later is a change), not unresolved; tier 2 confirms the condition |
| 16 | `missing-input:…/PublicAPI.Unshipped.txt` | `Messaging.Pack.props:25` | same as 15 |
| 17 | `unresolved-compile:tests/Shared/CheckpointRepeat.targets:unresolved-expression` | `Compile Include` with a `$(…)` property | the pin from item 10 declares the generated paths; otherwise tier 2 |
| 18 | `unresolved-content:src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj:unresolved-expression` | `Content Include="$(PkgMicrosoft_Windows_Console_ConPTY)\runtimes\…"` (NuGet package contents) | `packageRoots`: owned by the PackageReference line and its version (`packages.lock.json` if present) |

At parity (v1), all 18 stay unresolved and broaden every suite, which is today's behaviour. A
resolution only lands under v2 and in shadow (CI-11).

### 2.8 Tests: live repository first, small golden set

Per the operator refinement (2026-11-20), the **live repository is the primary corpus**:

- **Live lane** (`[Category("LiveRepo")]`, `Live/`). The tests find the enclosing repository
  (`git rev-parse --show-toplevel` from the test assembly directory, or `CHANGEIMPACT_LIVE_REPO`)
  and assert against HEAD. They fail loudly on drift and print a diff.
  - T-18 equals the current unresolved list. The expectation file is
    `tests/ChangeImpact.Tests/Live/expected-unresolved.txt`.
  - Every `tests/**/*.csproj` closure equals the pwsh oracle while the ps1 exists.
  - The reader-census oracle reports `under-selected=0` with every suite checked.
  - The adapter snapshot is byte-identical to the ps1 (CI-3 onward).
  - `check-config` passes on the committed config.

  When the repository is absent (extracted copy), these tests **skip with a stated reason**. In
  this repository a skip is not a pass: the checkpoint validator treats `skipped>0` as
  `row_failed`, so the rows here must execute.
- **Synthetic lane** (`Synthetic/`). Temporary git repositories built in code. It holds the 12
  S3 truth-table cases and the operator-rule cases (unowned flag, test-only defer, no-op
  conflict, forced-full precedence). These are self-contained and not snapshots.
- **Golden lane** (`Golden/`). A small, deliberate set, each with a reason:
  - G-a: the 12 historical rc-failure replays (`IncrementalSelectionSoundnessTests` path lists).
    History cannot come from HEAD.
  - G-b: one real csproj, props or targets file per T-18 shape that must stay frozen once the
    repository fixes or removes it.
  - G-c: at CI-9 only, the final ps1 census matrix (path to suite set), compressed. The ps1
    oracle is then gone.

  Each fixture directory has `fixture.json`:
  - `sourceRepo`, `sourceSha`, `path`, `blobId`, `frozenBecause`, `frozenBy` (task id);
  - a byte-hash test that guards it against tampering;
  - a live-lane provenance test (`git cat-file -e <blobId>`) when run inside the repository.
- **Regeneration and review.** `change-impact dev freeze --case <name> --at <sha> <paths…>`
  (a test-project tool, not a shipped verb) writes the fixture.
  - Regenerating an expectation (golden bytes or `expected-unresolved.txt`) is **its own commit**,
    with a stated reason. It is never in the same commit as a selector behaviour change. The
    memories of CARD-1545 CP-30 show that re-seeding in the same change launders a regression.
  - Review treats any fixture or expectation diff as an oracle change. It checks that the
    independent oracle (pwsh or the reader census) agrees with the new expectation before
    approving.
  - Moving a case from live to golden happens only when the live repository no longer has it,
    and the commit says which commit removed it.

## 3. Packaging and release

### 3.1 Artifacts (in order of adoption)

1. **ProjectReference (now).** The Antiphon adapter references `ChangeImpact.Core` by relative
   path. No versioning is needed, and the source identity covers it.
2. **Framework-dependent single-file exe per RID** (`win-x64`, `linux-x64`), published into a
   per-user cache. This reuses ps1 plan D-4 (publish shape), D-5 (git-only source identity over a
   committed `tools/ChangeImpact/cli-inputs.txt`) and D-13 (cache root; this tool uses
   `…/antiphon/change-impact/<rid>/<identity>/`, a sibling of `…/antiphon/cli/`). The ps1
   launcher calls it. The tier-2 provider is **not** in the single-file build, because Locator
   needs a real SDK and the ps1 plan D-1 notes the same limit for LandGraphAudit. `--tier
   evaluated` exits 2 with `tier-unavailable` there.
3. **dotnet tool** (`PackAsTool`, command `change-impact`), with tier 2 included. This is the
   form for other repositories.
4. **NuGet libraries** `ChangeImpact.Core` and `ChangeImpact.MSBuild`. The feed (nuget.org, a
   private feed, or none) is O-1.

### 3.2 Versioning

SemVer `0.y.z` from `tools/ChangeImpact/version.json`, independent of Antiphon CalVer. Version
1.0.0 requires the v2 config schema and the result schema to be frozen. `AlgorithmVersion` in
the result is the package version plus the source identity. Inside this repository the adapter
consumes source, not packages, until O-1 says otherwise.

### 3.3 Boundary with the ps1-to-C# migration (the other agent)

What the ps1 plan owns, unchanged by this plan:

- wave 1, S1–S12: `tools/Antiphon.Cli` foundation, `status-snapshot`, `nightly input-digest`,
  the policy port, the census pin narrowing (S4, D-9), the receipt validator, `build-slot`, small
  HTTP tools, `card`, `delegate`;
- its CLI conventions (PsArgs, the caller labels, the launcher shape);
- all of W2-nightly and W2-deploy;
- in W2-rc: ledger, admission, boundary, chunk digest, dirty order, shadow sample, baseline, run
  evidence, the release gate, authority and selection, `RcTagBacking.cs` and `RcRunEvidence.cs`;
- in W2-land: `land-graph-land-refresh.ps1`, `audit-land-verification-*`, evidence and the other
  rows.

What this plan claims. These rows should be struck from W2-rc and W2-land by a Docs amendment to
the ps1 plan:

- `scripts/lib/rc-input-manifest.ps1`, `rc-input-owners.json`, `rc-input-reader-census.json`,
  `scripts/rc-input-reader-census.ps1`, `scripts/test-rc-input-manifest.ps1`,
  `scripts/test-rc-input-owners.ps1`;
- the graph part of `scripts/lib/rc-project-selection.ps1`. The category batches stay W2-rc and
  call the library.

Deferred to library v2, and left in W2 until then:

- `impacted-classes.ps1`, `SelectorCensus.cs`, the derived-impact index builder and driver;
- `release-impact*.ps1`.

Shared rules:

- Commands are not shared. `antiphon rc-input …` in `tools/Antiphon.Cli` is the adapter's
  front door once S1 lands. Before that, the adapter has its own small exe.
- The ps1 launcher pattern and the parity window (D-6 and D-7 of the ps1 plan) are reused as they
  are.

## 4. Migration path (slices, 1–3 h each)

Lane: Linux portable on server2 (`GET /api/runner-defaults` reports `globalRunnerId: server2`;
omit `-Runner` and `-Platform`) unless a slice says Windows.

| Slice | h | Depends | Scope (files) | Tests and parity |
|---|---:|---|---|---|
| **CI-1** foundation | 2–3 | none | new `tools/ChangeImpact/{ChangeImpact.sln, Directory.Build.props, README.md}`, `src/ChangeImpact.Core/{Model,Glob,Sources,Json}`, `src/ChangeImpact.Cli` (verbs stubbed; `graph` lists the tree), `tests/ChangeImpact.Tests` | `GlobPatternTests`: table cases plus the **pwsh oracle**, which runs `New-RcInputPattern` over every tracked path for every pattern in the working-tree `rc-input-owners.json` (identical match sets). `GitTreeSourceTests`: synthetic validation (case collision, symlink, submodule, `..`, backslash) plus live tree equal to `git ls-tree -r HEAD`. `CanonicalJsonTests`: parity with `ConvertTo-TestSelectionCanonicalJson` (pwsh oracle) |
| **CI-2** syntactic MSBuild | 2–3 | CI-1 | `Core/MSBuild/*`, `Core/Units/MSBuildUnitProvider` | live: T-18 equals `expected-unresolved.txt`; per-suite project-walk records (`project-tree:`, `project-or-import`, `ancestor-build-input`, `Compile`, `Import`, `ProjectReference`, `Content`, `None`, `EmbeddedResource`, `AdditionalFiles`, `Analyzer`, `root-build-contract`, `package-lock-or-resolved-receipt-required`) equal the pwsh `Read-RcInputInventory` output filtered to those reasons. Synthetic: include resolution (`Resolve-RcInventoryPath` table), XML hardening (DTD prohibited, 1 MiB limit). Second oracle: `ReleaseImpactProjectGraph.cs` closure ⊆ ours |
| **(freeze)** | – | 2dda603e landed and reviewed | record the landed SHA as the parity anchor in this plan (Docs amendment) | – |
| **CI-3** S3 inventory port | 2–3 | CI-2, freeze | `Core/Rules/*` (v1 config), `Core/Selection/Inventory`, new `tools/Antiphon.ChangeImpact/` (v1 owners and census readers, snapshot writer) | live: snapshot canonical JSON **byte-identical** to the ps1 at HEAD, with and without `-SkipUnresolvedBroadening`, with and without a derived index; measured duration recorded |
| **CI-4** two-revision manifest and selection | 2 | CI-3 | `Selection/RevisionUnion`, `Selection/Selector`, adapter `Get-RcInputManifest` equivalent | synthetic: the 12 S3 truth-table cases ported 1:1 (same names); golden G-a: 12 historical replays; live: `Get-RcInputManifest` byte parity for HEAD~1..HEAD and one merge range |
| **CI-5** independent census | 2–3 | CI-4 | `tests/ChangeImpact.Tests/Oracles/ReaderCensusOracle` (port of the literal scan; Antiphon exclusions supplied by the adapter config, not hard-coded) | live: `under-selected=0`, every suite checked; matrix parity: for every tracked path, C# and ps1 give identical selected-suite sets (one inventory each, compared as a matrix). Mutation: delete one reader row and the census goes red |
| **CI-6** CLI and schemas | 1–2 | CI-4 | `Cli` verbs `impact`, `explain`, `owners`, `check-config`, `graph`; `schema/*.json`; `README.md`; `docs/change-impact.md` (repository docs pointer) | CLI tests in process (`CliApp.Run(args, stdout)`); every exit code; output validates against the schema; `explain` for one path per class |
| **CI-7** shadow inside the rc | 2 | CI-6 | `scripts/lib/rc-input-manifest.ps1`: compute both and compare; mismatch to evidence, never credit; ps1 output stays authoritative | existing `RcInputManifestTests`, `RcInputOwnersMapTests`, `RcChunkInputDigestTests` unchanged and green; a new harness case for the shadow mismatch. **Touches `scripts/lib/**` (forced full): never during a live rc qualification window** |
| **CI-8** ps1 becomes a launcher | 2 | CI-7 plus one rc qualification with zero shadow mismatches | `rc-input-manifest.ps1` reduced to a launcher (cache publish per ps1 plan D-4, D-5, D-13); byte copy in `scripts/legacy/rc-input-manifest.ps1` (D-7); `rc-input-reader-census.ps1` reduced to a launcher; `tests/rc-project-categories.json` rows; test-project registration if O-6 = yes | the same three test classes plus the CI-5 matrix against the legacy copy; chunk digests of one recorded rc ledger recomputed unchanged |
| **CI-9** retire legacy | 1 | CI-8 plus a parity window (O-5) | delete `scripts/legacy/rc-input-manifest.ps1`; freeze golden G-c | live lane now asserts against golden G-c for the frozen SHA and the reader census for HEAD |
| **CI-10** v2 policy (operator rules) | 2–3 | CI-6 (logic), CI-9 (switch) | `Rules/Policy` v2, `config.v2.schema.json`, `scripts/lib/change-impact.antiphon.json`, adapter `deferred` output | synthetic: unowned flagged and selects nothing (exit 3); test-only deferred; no-op proven by census or refused; forced-full precedence. Live: v1 and v2 difference report for the last 50 master commits (counts per class); **shadow only, no credit** |
| **CI-11** tier 2 and T-18 resolutions | 2–3 | CI-6 | `src/ChangeImpact.MSBuild`; `targetPins`, `externalSdks`, `packageRoots`, `.gitattributes` | live: evaluated ⊆ syntactic for every unit (exit 5 otherwise); each T-18 row resolved or explicitly still unresolved; per-OS branch rows remain unresolved on Linux (**Windows row needed**: one `-Platform Windows` checkpoint) |
| **CI-12** packaging and extraction rehearsal | 2 | CI-6 | `PackAsTool`, nupkg, single-file publish; `version.json` | copy `tools/ChangeImpact` to a temporary directory outside the repository, then `dotnet build` plus golden and synthetic lanes green, and the live lane skipping with its stated reason. This proves there is no reference to the repository |
| later | – | – | class-level provider (D-11), `NpmWorkspaceUnitProvider` (`package.json` workspaces), PowerShell dot-source provider (`release-impact-scripts.ps1`) | – |

Total: 12 slices, about 23–31 h (ESTIMATE). CI-1, CI-2 and CI-6 can run in parallel with the
freeze. CI-10 and CI-11 can run in parallel after CI-6.

### 4.1 Non-.NET units (v1 design hook only)

`IUnitProvider` returns units with `{id, kind, role, roots[], edges[], unresolved[]}`. MSBuild is
the only v1 provider. An npm workspace provider would read `package.json` `workspaces` and
dependencies and map `client/**` into units. Today `client/**` is
`repository-reader-non-code` and selects every suite (2dda603e). Out of scope for v1.

## 5. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **Under-selection** (a suite that should run is skipped) | Critical | Three oracles in the library test suite: the pwsh ps1 (byte parity until CI-9), the independent reader-census scan (`under-selected=0`, CI-5), and `ReleaseImpactProjectGraph` closure containment (CI-2). Tier 2 can only resolve, never remove (D-3). v2 earns no credit until shadow proof (O-5). A mutation row per oracle proves each one can go red. |
| **Chunk-digest churn** (reason strings and edges inside `rc-chunk-digest.ps1` identities) | High | D-4 byte identity. CI-8 recomputes the chunk digests of a recorded ledger. |
| **Pin churn** | Medium | None of the 7 `PinnedInputDigest` inputs is a change-impact file (G8). Registering `ChangeImpact.Tests` in `tests/test-selection-policy.json` moves `catalogHash`, `policyHash` and the pin. Batch it with one reseal (O-6), the same as ps1 plan D-8 and D-10. Moving logic into C# changes no pin recipe unless the ps1 plan's S4 narrows to census sources that include these files. They do not: the S4 census is the nightly coverage census. |
| **`release-impact` algorithm identity** (`Get-ReleaseImpactAlgorithmIdentity` hashes `impacted-classes.ps1` and `release-impact.ps1`) | Low in v1 | Unchanged in v1. The class-level v2 must add the library sources to that identity. |
| **Derived-impact index drift** (`DerivedImpactIndexTests.committed_index_matches_the_builder`) | Medium | Each slice regenerates `tests/derived-impact-index.json` and commits it if it changed. UNKNOWN whether the builder indexes `tools/**` C#; CI-1 measures. |
| **Live-lane brittleness** | Medium | It is meant to fail loudly. The expectation-regeneration rules in 2.8 (own commit, oracle agreement) prevent laundering. |
| **Self-ownership** (`tools/ChangeImpact/**` is not in any suite closure, so under v1 the `**` fallback selects every suite; under v2 it would be Unowned) | Low | The v2 config adds a unit for `ChangeImpact.Tests` (role test) and makes the selector's own sources forced-full, the same as `scripts/lib/**` today. |
| **Collision with the ps1-port agent** | Medium | Section 3.3 boundary plus a Docs amendment to the ps1 plan. Never edit `scripts/lib/rc-*` while a W2-rc slice is in flight. |
| **Live Windows rc run** (e7f92f66) | Low | CI-1 to CI-6 touch only `tools/ChangeImpact/**` and the adapter. CI-7 and later touch `scripts/lib/**` and land on master only. rc ports are separate reviewed picks, done after a qualification window. |
| **Windows PowerShell 5.1** | none | Not a constraint (G9). |

## 6. Decisions for the operator

| # | Question | Options | Recommendation |
|---|---|---|---|
| O-1 | Name and distribution | a) `ChangeImpact.*`, in-repo only for now; b) the same plus a public nuget.org package and dotnet tool; c) an `Antiphon.ChangeImpact.*` prefix | **a** now and b after CI-12. The nuget.org id availability is UNKNOWN; check it before publishing. |
| O-2 | v2 unowned and no-op | a) unowned flags and selects nothing; the Antiphon adapter maps the flag to Full until the no-op list is approved; b) unowned flags and selects nothing; the adapter also selects nothing | **a**. b depends on a complete no-op list. The list starts empty, and each glob is added only with a reader-census proof. |
| O-3 | v2 unresolved scope | a) broaden all (today); b) broaden only the unit whose closure produced the item | **b** after CI-11 resolves T-18. Under b, root-level items (rows 2, 3 and 12) still broaden all. |
| O-4 | Test-only deferral bound | a) defer until a product edge reaches the unit; b) a plus a forced run before any final CalVer tag; c) a plus a maximum age of N rc runs | **b**. A final tag never ships with deferred debt. |
| O-5 | Shadow proof bar for credit | a) K consecutive rc qualifications with zero v2 under-selection against the Full outcome (suggest K=3); b) a 50-commit replay plus a; c) time-boxed | **b** (it reuses CARD-1820 V3, the frozen 50-commit replay). |
| O-6 | Register `ChangeImpact.Tests` in rc qualification | a) no; checkpoint rows only; b) yes at CI-8, batched with one `catalogHash` and pin reseal | **b**. While it is not registered, a regression in the library is caught only by Code-stage rows. |
| O-7 | Multi-target for other repositories | a) `net10.0` only; b) `net8.0;net10.0` | **a** until a real external consumer exists. |

## 7. First slice that can start now (CI-1)

- **Where:** server2, Linux portable, in its own worktree off master. No dependency on 2dda603e,
  no desktop work, no deploy.
- **What:** the layout in 2.1 for Core (Model, Glob, Sources, Json), the CLI host with the `graph`
  verb listing the tree, and `ChangeImpact.Tests` with `GlobPatternTests`, `GitTreeSourceTests`
  and `CanonicalJsonTests` (synthetic, live and pwsh oracle).
- **Must not touch:**
  - `scripts/**`;
  - `tests/*.json` policies;
  - `Antiphon.sln`;
  - any pinned input;
  - `tests/rc-project-categories.json`, unless an existing guard demands an entry for the new
    csproj files. Run `RcProjectSelectionTests`. If it names `unknown-project:` as a failure,
    add `tooling` rows, the same as ps1 plan S1. That is the only allowed outside edit.
- **Why safe for e7f92f66:** that run executes the rc tip on Windows, and nothing here reaches rc
  or the runner.
- **Expected master-side effect:** under the current owner map (blanket `**`) and under 2dda603e
  (`**/*.sln` forced full), the new files select every suite on the next master rc. That is
  correct and costs nothing extra, because rc runs Full today.

### 7.1 Proposed checkpoint rows for CI-1 (input to TestDesign; not a `### Checkpoints` table)

| CP | Build | Filter / command | Expect |
|---|---|---|---|
| CP-1 | `tools/ChangeImpact/tests/ChangeImpact.Tests -> bin-ci1/` | `/*/*/(GlobPatternTests*)\|(GitTreeSourceTests*)\|(CanonicalJsonTests*)/*` | 0 failed, 0 skipped, executed ≥ the method count TestDesign fixes |
| CP-2 | n/a | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | exit 0 (proves `Antiphon.sln` graph unchanged) |
| CP-3 | `tests/Antiphon.Tests -> bin-ci1-guards/` | `/*/*/(RcProjectSelectionTests*)\|(DerivedImpactIndexTests*)/*` | 0 failed |

- UNKNOWN whether the checkpoint tool accepts a Build cell outside `tests/`. TestDesign checks
  this with `import --plan`. The fallback is a `build-slot`-wrapped `dotnet run --project` command
  row.
- Suffix collisions: `GlobPatternTests`, `GitTreeSourceTests` and `CanonicalJsonTests` must not
  prefix-match an existing class in the same assembly. The project is new, so this holds by
  construction. TestDesign re-checks the roster.

## Existing tests affected (CARD-1782)

- No migration, no public DTO or route, no operation label, no remote script function and no
  statement budget is changed by any slice.
- CI-1 to CI-6: none outside the new project. `RcProjectSelectionTests` and
  `DerivedImpactIndexTests` are run as guards.
- CI-7 and CI-8 change `scripts/lib/rc-input-manifest.ps1`. Affected: `RcInputManifestTests`,
  `RcInputOwnersMapTests`, `RcChunkInputDigestTests` and `IncrementalSelectionSoundnessTests`
  (behaviour unchanged by D-4).
- `tests/impacted-classes.json` names none of these files today. CI-7 runs
  `pwsh -NoProfile -File scripts/impacted-classes.ps1 -Range <base>..<tip> -Json` and follows the
  rows it selects.

## Performance budget

Applies `docs/testing-and-build.md#performance-standards`. Measurements:

- One ps1 `Read-RcInputInventory` (27 suites, one revision, `-SkipUnresolvedBroadening`) took
  **3 min 24 s wall, 3 min 20.7 s user**. Single sample, server2, pwsh 7, at b43f1f5c1, measured
  2026-11-20 under normal load.
- 2dda603e's reader census took 153 s (one sample, its checkpoint doc).

Budgets:

| Item | Duration | Source | Timeout |
|---|---|---|---|
| C# inventory, one revision | target under 15 s | ESTIMATE; CI-3 measures 3 samples | test timeout 2x the measured p95; provisional 60 s |
| pwsh oracle (inventory) inside a live test | ~204 s | measured above | **once per run**, a shared `[Before(Assembly)]`-style cached oracle result keyed by HEAD SHA; per-test timeout 420 s (about 2x 204 s); the oracle class runs `Serial` |
| reader census (C#) | target under 30 s | ESTIMATE | 2x measured p95 after CI-5 |
| matrix parity (CI-5) | reuses the two inventories; compare under 5 s | ESTIMATE | 60 s |
| glob oracle (CI-1): ~11.2k paths × ~50 patterns in one pwsh process | under 30 s | ESTIMATE | 120 s provisional, re-derived from 3 samples |
| CP-1 row (CI-1) | under 5 min including build | ESTIMATE | the checkpoint tool's row deadline; no `--timeout` that undercuts the build |
| Whole ChangeImpact.Tests with live lane | under 10 min | ESTIMATE | any selection over 30 minutes opens a Debug task |

Phases:

- The build runs under the build slot and is reported separately.
- No containers, no database and no migrations.
- Setup: the synthetic git repositories are created per test in a temporary directory (under
  1 s each, ESTIMATE). The live-repo HEAD snapshot and the pwsh oracle output are shared once per
  assembly. Teardown deletes the temporary repositories (the CARD-1545 memory notes a 30 s cleanup
  budget for scratch repositories, so each stays small).
- Artifacts: TRX under the checkpoint run directory. Oracle JSON goes to the gitignored
  `.antiphon/change-impact/` folder.

## Checklist: sessions that wait for input

None. No slice adds or changes a session that waits for input.

## Next

`next: docs`. Two Docs tasks follow this plan:

- amend the ps1 plan with the section 3.3 boundary;
- record the 2dda603e freeze SHA when it lands.

The operator settles O-1 to O-7. CI-1 can then go to TestDesign under the stated defaults.
