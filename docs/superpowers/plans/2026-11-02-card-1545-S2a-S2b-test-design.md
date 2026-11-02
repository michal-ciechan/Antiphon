# CARD-1545 S2a-1, S2a-2 and S2b: test design (fingerprint, attestation, receipt)

TestDesign task 70cde4e7, 2026-11-02, base master `10c8c2471633c7c0212294afc57348878021e183`
(server2/Linux). This companion appends verification detail to the
[landed plan](2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md) ("the plan") for
three slices only: **S2a-1** repository closure (CP-8, V-5a), **S2a-2** environment attestation
(CP-22, V-5b) and **S2b** receipt schema and validation (CP-9, V-6). It does not amend the plan's
fix design. Inputs: investigation a9b3bc09
(`docs/investigations/2026-11-02-card-1545-next-stage.md` at `585c39908`), the plan, the S1b/S1c
test designs, S1 code on master and Final Review 185b71e2.

Status of decisions. **D-6 is PROVISIONAL.** This design implements both D-6 options behind one
switch: `AlwaysRerun` (the default until the operator accepts reuse; plan: "Until explicitly
accepted, no live reuse is enabled") and `Bounded24h` (the recommended exception: equal inputs, same
rc line/host/account, receipt age under 24 h, fresh zero-prompt auth and startup-control probes for
this candidate, no incident revocation). If the operator picks always-rerun, nothing in this design
changes except that the switch is never set; the fingerprint, attestation and receipt checks stay
useful as diagnostics ("why is this lane due"). **D-9 is out of scope:** every receipt in these
slices is diagnostic, no issuer is trusted, and no output carries gate credit. D-5 enforcement
(S3) and the native lane (CP-4..7, D-3/D-4/D-11) are out of scope.

No build or test ran in this task. Only the checkpoint tool's `import --plan` validated the table
below (see Checkpoints). Everything else is a proposal for Code.

## Verification design

### Inspection

- Plan sections Input fingerprint and lane state, Receipt/candidate checks/trust, Implementation
  slices (S2a-1/S2a-2/S2b rows), Verification design V-5a/V-5b/V-6, Checkpoints CP-8/CP-22/CP-9,
  Performance budget. Boundaries -> V-1..V-19, R-1..R-8 below.
- `scripts/lib/rc-project-selection.ps1` (`Resolve-RcProjectInput`, `Get-RcDownstream`,
  `Read-RcProjectGraph`, `Get-RcProjectSelection`) and `scripts/lib/test-selection.ps1`
  (`ConvertTo-TestSelectionCanonicalJson`, `Get-TestSelectionDigest`, `Invoke-TestSelectionGit`,
  `Get-TestSelectionSorted`). Findings that shape S2a-1:
  - `Read-RcProjectGraph` returns direct `references`, explicit item `inputs` (Compile, Content,
    None, EmbeddedResource, AdditionalFiles, with glob expansion) and `downstream`. It does **not**
    return the visited import set (Directory.Build.*, literal `<Import>` targets), and it does not
    list SDK-default glob members (every `*.cs` under a project directory). A fingerprint built only
    from `inputs` would miss both. S2a-1 must add the project directory prefix and the shared build
    files itself, without editing the reader.
  - Its `fallbackReasons` mix input faults (`graph:<file>:...` from `unresolved-input`,
    `outside-input`, `missing-input`, `missing-linked-input`, `missing-reference`, invalid XML;
    `project-cycle:`) with catalog faults (`unknown-project:`, `stale-project:`, `unknown-batch:`,
    `unbatched-test-project:`, `category-inventory-invalid`, `invalid-test-disposition*`). Only the
    first kind says the input closure is unknown.
  - Upstream closure is not computed by the reader; `Get-RcDownstream` gives the reverse relation,
    which is the independent oracle for "a selected test project is downstream of every project in
    its closure".
- `scripts/test-rc-project-selection.ps1` plus `tests/Antiphon.Tests/Scripts/RcProjectSelectionTests.cs`:
  the nearest fixture. An owned Git repository under the harness results directory ("repository
  with spaces"), `Put`/`Commit`/`Check` helpers, `Assert-C487` named rows, and a TUnit method per
  harness case through `ScriptHarness.RunHarnessCaseAsync`. Category `Integration` plus
  `ParallelLimiter<ProcessSpawnLimit>`.
- `scripts/lib/live-lane-outcome.ps1`, `live-lane-run.ps1`, `scripts/test-live-lane-outcome.ps1`,
  `LiveLaneOutcomeTests.cs`, `LiveLaneRunnerTests.cs`: S1 exit vocabulary (0 green, 1 contract red,
  2 invalid, 20 inconclusive) and the synthetic terminal record (`InputBefore/After`,
  `ExecutableBefore/After`, `Rows`, `Authority='diagnostic'`). S2b's receipt reuses this vocabulary.
- `tests/Shared/LiveClaude/LiveClaudeSettingsProjection.cs`, `LiveClaudeExecutable.cs`,
  `LiveClaudeLaunchContext.cs`, `LiveClaudeProfile*.cs`, `LiveClaudeExecutableCases.cs`: the S1
  attestations S2a-2 consumes. `LiveClaudeSettingsProjection(Digest, ReuseEligible, Sources[Kind,
  Path, State, Projection])` already fails closed (unknown key, `absent-unverified` managed source,
  unreadable source, additional unresolved source -> `ReuseEligible=false`).
  `LiveClaudeExecutable` checks attested path, SHA-256, PID/start/session and the chain; its
  `LiveClaudeExecutableReceipt` holds the observed image. Neither is serialized anywhere today.
  These files are linked into `Antiphon.Tests` by literal Compile items, so a test in
  `tests/Antiphon.Tests/Scripts/` can call them without a csproj edit.
- `tests/live-lanes.json` (`antiphon-live-lanes-v1`: `core`, `gates`, `additiveContract`): six core
  rows with project, namespace, class, method, arguments, uid. It has no input-root field.
- `tests/Antiphon.Tests/Scripts/ScriptHarness.cs`, `ScriptHarnessProcess.cs`:
  `ScriptHarnessOptions(ExecutionBudget, CleanupBudget, ScriptPath, ExecutablePath,
  AdditionalArguments, ..., PostExitDrainBudget)`. `AdditionalArguments` carries the attestation
  directory for S2a-2.
- `TestLaneCategoryGuardTests` (Unit xor Integration), `slow-tests-allowlist.txt` (Slow classes
  only), `tests/test-execution-policy.json` (no per-class LiveLane entries; Antiphon.Tests classes
  are required by default), `server/Application/Services/LandVerificationBuildGraph.*.txt`
  (re-pinned by S1 in `4c0cdfe3a` after csproj edits).
- `docs/testing-and-build.md` (Checkpoint manifest, runner tool, import timeouts, build slots,
  mutation), `docs/test-performance-standards.md`, AGENTS.md Tests and builds.
- Final Review 185b71e2 report (`.antiphon/task-185b71e2.md` on `origin/feat/card-task-185b71e2`):
  four unfiled advisories, now CARD-1645 (see Out of scope).

### Delivery inventory

These slices add no asynchronous delivery path: no message queue, session input, notification,
event or recipient. The status front door reads Git objects and local JSON documents and prints a
state document; the S2b use record is a local file written once. A file write is not delivery, and
nothing here claims that a reader saw it. Producer-to-recipient queue tests do not apply. The
only producer/consumer seam is S1 attestation types (C#) -> attestation JSON -> S2a-2 PowerShell
consumer, and it is exercised end to end in CP-22 (V-8..V-13) with the real S1 types as producer.
Substitute declared: the future native wrapper will be the real producer; until S1c-native exists,
the test-side exporter stands in and cannot prove that the native lane emits the same fields.

### Fixtures and conventions

**Files (all new; Code may not touch other files without a TestDesign amendment, except the shared
checks listed under Overlap):**

| Slice | Production | Harness | TUnit class | Data |
|---|---|---|---|---|
| S2a-1 | `scripts/lib/live-lane-fingerprint.ps1`, `scripts/live-lane-status.ps1` | `scripts/test-live-lane-fingerprint.ps1` | `tests/Antiphon.Tests/Scripts/LiveLaneFingerprintTests.cs` | `tests/live-lane-inputs.json` |
| S2a-2 | `scripts/lib/live-lane-attestation.ps1`; fingerprint and status integration | `scripts/test-live-lane-attestation.ps1` | `tests/Antiphon.Tests/Scripts/LiveLaneAttestationTests.cs`, `tests/Antiphon.Tests/Scripts/LiveLaneAttestationExport.cs` | none |
| S2b | `scripts/lib/live-lane-receipt.ps1`; status integration | `scripts/test-live-lane-receipt.ps1` | `tests/Antiphon.Tests/Scripts/LiveLaneReceiptTests.cs` | none |

`rc-project-selection.ps1`, `test-selection.ps1`, `tests/live-lanes.json`, every
`tests/Shared/LiveClaude/*` file, every S1 `live-lane-*.ps1` and every csproj stay byte-identical.
Code checks this with `git diff --exit-code 10c8c2471 -- <those paths>` before each push.

**TUnit shape.** Each class is `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`,
`namespace Antiphon.Tests.Scripts`, one `[Test]` per method, each calling
`ScriptHarness.RunHarnessCaseAsync("<harness>", "C1545", "C1545_<Method>", <case count>,
<names "C1545 <Method>/<case>">, new ScriptHarnessOptions(<budget>, 10 s, PostExitDrainBudget: 10 s,
AdditionalArguments: ...), CancellationToken.None)`. The plan's "Unit metadata" is replaced by
Integration because these spawn pwsh and git, matching every S1 harness class and
`RcProjectSelectionTests`. The expected row count equals the case list below exactly; a missing,
extra or renamed case fails. Budgets: 60 s per method, 120 s for `StableCanonicalInputs` (it reads
the real tree once).

**S2a-1 fixture repository** (built fresh per case by `New-FingerprintFixture` in the harness,
owned Git repo under the results directory, path with spaces, `git init -b main`):

```text
tests/rc-project-categories.json   schemaVersion 1, every csproj categorised, batches = both test projects + tests/Other
Directory.Build.props, global.json, NuGet.Config, Directory.Packages.props, build/Server.targets
src/Protocol/Protocol.csproj        namespace Antiphon.PtyHost.Protocol   (upstream only, outside minimum roots)
src/Agents.Pty/Agents.Pty.csproj    -> Protocol                            namespace Antiphon.Agents.Pty
src/SessionRunner/SessionRunner.csproj                                     namespace Antiphon.SessionRunner
server/Server.csproj                -> SessionRunner; <Import Project="../build/Server.targets" />
tests/Pty.Tests/Pty.Tests.csproj    TUnit; -> Agents.Pty; Compile ../Shared/LiveClaude/Shared.cs; Content Fixtures/**
tests/Main.Tests/Main.Tests.csproj  TUnit; -> Server; Compile ../Shared/LiveClaude/Shared.cs
tests/Shared/LiveClaude/Shared.cs, tests/Shared/LiveClaude/Fixtures/witness.mjs
src/Unrelated/Unrelated.csproj, tests/Other/Other.csproj -> Unrelated     namespace Antiphon.Unrelated
scripts/hooks/hook.ps1, scripts/lib/live-lane-contract.ps1, docs/unrelated.md
CLAUDE.md "@AGENTS.md", AGENTS.md "@docs/imported.md", docs/imported.md, .claude/settings.json, .mcp.json
tests/live-lanes.json               fixture lane "fixture-lane": core rows in Pty.Tests and Main.Tests
tests/live-lane-inputs.json         declaration below
```

**Declaration** `tests/live-lane-inputs.json` (new, strict):
`{format:'antiphon-live-lane-inputs-v1', lanes:{<laneId>:{manifest, contractFiles[],
minimumRoots[], runtimeRoots[], namespaceRoots[], instructionPatterns[]}}}`. Unknown field, other
format or missing lane -> exit 2 `declaration-invalid`. The real file declares lane
`live-claude-core-windows` with the plan's minimum roots (`src/Antiphon.Agents.Pty/`,
`src/Antiphon.PtyHost/`, `src/Antiphon.PtyHost.Client/`, `src/Antiphon.PtyHost.Protocol/`,
`src/Antiphon.SessionRunner/`, `src/Antiphon.SessionRunner.Contracts/`, `server/`, `tests/Shared/`),
runtime roots `scripts/` (whole tree: Antiphon.Tests reads scripts by name at run time, D-8
conservative), `tests/live-lane-checkpoints/`, `.claude/`; contract files `tests/live-lanes.json`,
`tests/live-lane-inputs.json`, `scripts/lib/live-lane-*.ps1`, `scripts/live-lane-status.ps1`,
`scripts/run-live-lane.ps1`; namespace roots `Antiphon.Agents.Pty`, `Antiphon.PtyHost`,
`Antiphon.SessionRunner`, `Antiphon.Server`, `Antiphon.Tests`, `Antiphon.Agents.Pty.Tests`;
instruction patterns `CLAUDE.md`, `CLAUDE.local.md`, `AGENTS.md`, `.claude/**`, `.mcp.json` at any
depth. The selected test projects come from `tests/live-lanes.json` `core[].project`, resolved to
the one csproj whose file name is `<project>.csproj`.

**Fingerprint contract** (`Get-LiveLaneFingerprint -RepositoryRoot -Sha -Lane`), from Git objects
at `Sha` only (`ls-tree -r -z`, blob ids, modes; no working tree, no mtime):

- Closure projects = selected test projects plus their transitive `references`.
- Inventory = every tracked entry under each closure project's directory, its explicit `inputs`,
  every tracked `Directory.Build.*`, `*.props`, `*.targets`, `global.json`, `NuGet.Config`,
  `nuget.config`, `Directory.Packages.props`, `packages.lock.json`, `*.sln*`; every entry under the
  minimum and runtime roots; contract files; instruction files and their resolved `@` imports.
  Entry = `{path, mode, blob}`, ordinal-sorted. An empty blob and an absent path are distinct.
- Components: `laneContract`, `repositoryClosure`, `liveRoots`, `testSources`, `runtimeInputs`,
  `instructions` (S2a-2 adds `environment`). Each is `Get-TestSelectionDigest` of its sorted entries.
- `digest` = SHA-256 of UTF-8 `"antiphon-live-lane-fingerprint\n"` + canonical JSON of
  `{schemaVersion:1, laneId, components}`. Never the candidate SHA, time, run id or any absolute or
  scratch path.
- `status` is `complete` or `unknown` with sorted `unknownReasons`. Unknown when: an input-fault
  reason from `Read-RcProjectGraph` names a closure project or one of its imports; a closure entry
  is a symlink (120000) or gitlink (160000); a declared root has no tracked entry; a core project
  name resolves to zero or two csprojs; a relevant namespace or loaded instruction lies outside the
  inventory; a runtime script literal in a selected test source lies outside the inventory; an
  import escapes the tree, names a home path or is missing. Catalog-only reasons and faults in
  projects outside the closure are recorded under `observations`, not unknown.
- `scripts/live-lane-status.ps1 -Lane -CandidateSha [-Attestation] [-History] [-ReusePolicy]`
  prints one JSON state document and exits 0, or exits 2 on invalid input. The document always
  carries `effectiveMode='Full'`, `activation='shadow-only'`, `gateCredit=$false`,
  `executionEvidence=$false`. In S2a-1, state is always `DUE` (`no-receipt-validator`, or
  `unknown-input` when the fingerprint is unknown).

**Attestation document** (S2a-2 consumer input), `antiphon-live-lane-attestation-v1`:
`{schemaVersion:1, laneId, host:{enrollmentId, os, arch}, runtime:{dotnetRuntime, dotnetSdk, pwsh,
node, backend, restoredPackagesDigest}, rows:[{uid, attested:{route, path, sha256, version,
chain:[{sha256}]}, observed:{path, sha256, chain:[{sha256}], pid, started, session},
pre:{sha256}, post:{sha256}}], installedTarget:{sha256}, settings:{digest, reuseEligible,
sources:[{kind, state, projectionDigest}]}, model:{mode, requested, effective}, environment:{<name>:
'absent'|'empty'|'value:<v>'}, account:{enrollmentId, authMethod, subscriptionClass},
external:{epoch}}`. Booleans and integers are typed; unknown fields refuse. Private paths and
PID/start/session are checked (V-10) but never enter the environment digest. The consumer
recomputes eligibility from source states and never trusts `reuseEligible` alone.

**Attestation producer.** `LiveLaneAttestationExport` (new, test-side, in
`tests/Antiphon.Tests/Scripts/`, glob-compiled, no csproj edit) serializes the real S1 objects:
`LiveClaudeSettingsProjection.Read(...)` over an owned fake profile (config dir, cwd with
`.claude/settings*.json`, injected managed-policy root, `.claude.json`), and a
`LiveClaudeExecutableReceipt` from `LiveClaudeExecutable.Resolve/Start` with fake image files and
a fake observer, following `LiveClaudeExecutableCases`. Each CP-22 method writes `base.json` and
one `<case>.json` per case into a fresh temp directory, passes it as `-AttestationDirectory`, then
deletes it. Pure schema forgeries (string booleans, unknown fields) are made by the harness from
`base.json`. The producer never reads `~/.claude`, never starts a process and never writes machine
policy.

**Receipt fixtures** (S2b) are built in the harness: an owned Git repo with `main`, an
`rc/2026-11-01`-style ref, an ancestor chain and a divergent branch; a manifest copy with six fixture
UIDs (one with an argument expansion); receipts `kind='live-lane-execution'`, `schemaVersion=1`,
`authority='diagnostic'`, typed counts/outcomes, artifact files with SHA-256 digests; a fake clock
(`-NowUtc`) and probe records bound to a candidate nonce. Receipt validation and state evaluation:
`Test-LiveLaneReceipt`, `Get-LiveLaneState`, `New-LiveLaneUseRecord`.

**Red-first stubs.** At the base none of these libraries exist, so a new method's first failure
would be a dot-source error, which is not an accepted red. Each Code slice therefore commits, in
its red commit, the tests plus a minimal **fail-open stub** with the final signatures:
`Get-LiveLaneFingerprint` returns `{status='complete'; unknownReasons=@(); digest=('0'*64);
components=@{}; inventory=@()}`; `Get-LiveLaneEnvironmentAttestation` returns
`{status='complete'; digest=('0'*64); reuseEligible=$true}`; `Test-LiveLaneExternalFreshness`
returns eligible; `Test-LiveLaneReceipt` returns valid; `Get-LiveLaneState` returns `SATISFIED`;
status prints the stub. Against these, every invalidation case is red (digest never changes) and
every refusal case is red (nothing refuses). Cases marked **stub-green** in the tables pass on the
stub; they are non-vacuous only through their listed mutant, which Mutation runs after land. The
stub is deliberately unsafe and is never landed: the green commit replaces it in the same slice.

### Proves it works now

Planned witnesses, not claims of execution. V-1..V-19 and R-1..R-8 are local to this companion; plan V-5a = V-1..7, V-5b = V-8..13, V-6 = V-14..19. Every case is an assertion row
`PASS C1545 <Method>/<case>`; "Red today" is the failure on the red-first stub, "Mutant" is the
one-line production defect that must turn that row red after the fix (PC id where the guard is
safety-critical, `P` for precision controls that need no PC).

#### CP-8, S2a-1: `LiveLaneFingerprintTests` (7 methods)

Exact method filters: `/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/<Method>`.

**V-1 `StableCanonicalInputs`** (9 cases). Arrange the fixture at commit B; act
`Get-LiveLaneFingerprint` (and status where named); assert as below.

| Case | Arrange -> act | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| deterministic | compute twice at B | canonical JSON byte-equal; digest 64 hex | stub-green | P: build `components` from an unordered hashtable enumeration with `-Depth` JSON |
| sha-free | commit only `docs/unrelated.md` -> C; compute at B and C | equal digest; canonical bytes contain neither SHA, nor `runId`, nor results path | stub-green | PC-1: add `candidateSha=$Sha` to the digest material |
| path-free | `git clone` fixture to a second directory; compute at B there | equal digest | stub-green | PC-2: put `RepositoryRoot` (absolute) into the `laneContract` component |
| mode | `git update-index --chmod=+x` a closure `.cs` -> commit | digest differs; `repositoryClosure` differs | red: constant digest | PC-3: drop `mode` from inventory entries |
| absent-vs-empty | variant X empties a closure file; variant Y deletes it | three pairwise distinct digests (B, X, Y) | red | PC-4: skip zero-length blobs when building the inventory |
| domain | same tree, lane id copied to `fixture-lane-2` in both data files | digests differ | red | PC-5: omit `laneId` from the hashed material |
| shadow-only | run status for the fixture lane; also `Get-RcProjectSelection` digest for (B,C) before and after dot-sourcing the fingerprint library | `effectiveMode='Full'`, `activation='shadow-only'`, `gateCredit` is `[bool]$false`, `executionEvidence=$false`; selection digests equal | red: the stub status prints no shadow fields | PC-6: set `gateCredit=($fp.status -eq 'complete')` in status |
| missing-root | declaration names `src/Missing/` | `status='unknown'`, reason `declared-root-empty:src/Missing/` | red | PC-7: skip declared roots with no entries instead of refusing |
| real-head | compute lane `live-claude-core-windows` at the repository's own `HEAD` twice | equal digests; every declared minimum, runtime and contract root contributes at least one entry; no value pinned | red | P: misspell one real minimum root in `tests/live-lane-inputs.json` |

**V-2 `ReferenceClosureInvalidates`** (7 cases). Each case edits one file at B, commits C, computes
at B and C.

| Case | Edit | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| direct-edge | `src/Agents.Pty/A.cs` | digest differs; `liveRoots` and `repositoryClosure` differ | red | PC-8: drop `minimumRoots` and project directories from the inventory |
| transitive-edge | `src/Protocol/P.cs` (upstream of Agents.Pty, outside minimum roots) | digest differs; closure projects equal `{P : Pty.Tests or Main.Tests in Get-RcDownstream(P) or P is one}` computed independently with `Get-RcDownstream` | red | PC-9: use direct `references` only, no transitive walk |
| edge-removed | remove `Pty.Tests -> Agents.Pty` ProjectReference | digest differs; `Pty.Tests.csproj` blob differs in inventory | red | PC-10: exclude `*.csproj` files from the inventory |
| edge-added | add `Pty.Tests -> Unrelated`; then edit `src/Unrelated/U.cs` (D) | C vs B differ; D vs C differ; before the edge, editing `U.cs` did not change the digest | red | PC-11: compute the closure from the graph at the receipt/base SHA instead of `Sha` |
| shared-build-inputs | one commit each: `Directory.Build.props`, `build/Server.targets`, `global.json`, `NuGet.Config`, `Directory.Packages.props` | all five differ from B and from each other | red | PC-12: omit the shared build file set |
| rename | `git mv` a `tests/Pty.Tests` file | digest differs | red | PC-13: key entries by blob only, dropping `path` |
| unrelated-stable | `src/Unrelated/U.cs` without the edge | digest equal | stub-green | P: hash every tracked entry in the tree |

**V-3 `TestAndFixtureInvalidates`** (6 cases).

| Case | Edit | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| test-source | non-core `tests/Pty.Tests/Other.cs` (SDK default glob, no XML item) | differs; `testSources` differs | red | PC-14: use only the explicit `inputs` of test projects, not their directory |
| linked-shared | `tests/Shared/LiveClaude/Shared.cs` in a fixture whose declaration has no `tests/Shared/` root | differs | red | PC-15: ignore explicit `inputs` outside the project directory |
| content-glob | edit `tests/Pty.Tests/Fixtures/a.json`; then add `Fixtures/b.json` | both differ | red | PC-16: drop glob-expanded `inputs` |
| deletion | delete `tests/Main.Tests/M.cs` | differs | red | P: union the inventory with the previous run's inventory |
| other-test-project | edit `tests/Other/O.cs` | equal | stub-green | P: include every TUnit project |
| core-project-map | rename `Pty.Tests.csproj` file name only | `status='unknown'`, `core-project-missing:Pty.Tests` | red | PC-17: fall back to the first csproj under `tests/` |

**V-4 `UnknownDependencyRefuses`** (11 cases). Status `unknown`, state `DUE`, reason named.

| Case | Arrange | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| dynamic-import | `<Import Project="$(Root)/x.targets" />` in `Server.csproj` | reason starts `graph:server/Server.csproj:unresolved-input` | red | PC-18: treat `graph:` reasons as observations |
| missing-linked | `<Compile Include="../Shared/Gone.cs" />` in `Main.Tests` | reason contains `missing-linked-input` | red | PC-19: filter `missing-linked-input` out of the unknown set |
| cycle | `Protocol -> Agents.Pty` | reason `project-cycle:` | red | PC-20: ignore `project-cycle:` reasons |
| outside-root | `<Compile Include="../../../../x.cs" />` | reason contains `outside-input` | red | PC-21: ignore `outside-input` reasons |
| symlink | closure path committed as a symlink (mode 120000) | reason `symlink:<path>` | red | PC-22: accept 120000 entries as ordinary blobs |
| submodule | gitlink (160000) under `src/Agents.Pty/` | reason `gitlink:<path>` | red | PC-23: skip 160000 entries |
| ambiguous-project | second `Pty.Tests.csproj` under `tests/Copy/` | reason `core-project-ambiguous:Pty.Tests` | red | PC-24: take the first match |
| missing-manifest | delete `tests/live-lanes.json` | status exits 2 `manifest-missing` | red | PC-25: treat a missing manifest as an empty roster |
| declaration-schema | add field `extra` to the lane entry; separately format `v2` | both exit 2 `declaration-invalid` | red | PC-26: ignore unknown declaration fields |
| catalog-only | `tests/Other/Other.csproj` unbatched in the catalog | `status='complete'`; observation `unbatched-test-project:...` | stub-green | P: treat every fallback reason as unknown |
| fault-outside-closure | dynamic import in `Unrelated.csproj` | `status='complete'`; observation recorded | stub-green | P: same as above |

**V-5 `RuntimeScriptsIncluded`** (6 cases).

| Case | Edit | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| hook | `scripts/hooks/hook.ps1` | differs; `runtimeInputs` differs | red | PC-27: drop `runtimeRoots` |
| contract-scripts | `scripts/lib/live-lane-contract.ps1` | differs; `laneContract` differs | red | PC-28: drop `contractFiles` |
| manifest-and-declaration | `tests/live-lanes.json` (manifest bytes, no roster change); separately `tests/live-lane-inputs.json` | both differ | red | PC-29: hash the parsed roster instead of manifest and declaration bytes |
| fixture-assets | `tests/Shared/LiveClaude/Fixtures/witness.mjs` (not a Compile item; declaration root `tests/Shared/LiveClaude/Fixtures/`) | differs | red | PC-30: include only `*.cs`/`*.csproj` under declared roots |
| undeclared-runtime-literal | `tests/Pty.Tests/Uses.cs` contains `"scripts/other/tool.ps1"` and `RunHarnessCaseAsync("tool2.ps1", ...)`; declaration runtime roots are `scripts/hooks/`, `scripts/lib/` | `status='unknown'`, reasons `undeclared-runtime:scripts/other/tool.ps1` and `undeclared-runtime:scripts/tool2.ps1`; control: same literals under `scripts/lib/` -> complete | red | PC-31: skip the runtime literal scan |
| unrelated-file | `docs/unrelated.md` | equal | stub-green | P: hash the whole tree |

Literal scan: `git grep -n -E '"(scripts/)?[A-Za-z0-9_./-]+\.(ps1|psm1|mjs|js|sh)"' <sha> --
<selected test project dirs>`; a bare name maps to `scripts/<name>`. One git process per scan.

**V-6 `RelevantNamespaceOutsideInputsRefuses`** (5 cases). Scan: `git grep -n -E
'^\s*namespace\s+[A-Za-z_][A-Za-z0-9_.]*' <sha> -- '*.cs'`, prefix-match declared namespace roots
(`X` or `X.`), compare with the inventory.

| Case | Arrange | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| file-scoped | add `tools/Stray/Stray.cs` with `namespace Antiphon.SessionRunner.Stray;` | `unknown`, `namespace-outside-inputs:tools/Stray/Stray.cs` | red | PC-32: skip the namespace scan |
| block-scoped | same with `namespace Antiphon.Agents.Pty.Extra {` | same reason | red | PC-33: match only declarations ending in `;` |
| test-namespace | `tools/T.cs` with `namespace Antiphon.Agents.Pty.Tests.Helpers;` | same reason | red | PC-34: scan only product namespace roots, not test roots |
| foreign-namespace | `tools/Stray/Stray.cs` with `namespace Antiphon.Tools;` and `namespace Antiphon.ServerTools;` | `complete` (prefix is `X` or `X.`, not a substring) | stub-green | P: substring match |
| inside-inputs | `src/Agents.Pty/New.cs` with `namespace Antiphon.Agents.Pty;` | `complete`, digest differs | red (digest) | P: report every relevant namespace file |

**V-7 `LoadedInstructionsOutsideInputsRefuse`** (8 cases). Instruction files are found by a fixed
CLI-loaded list in the library (`CLAUDE.md`, `CLAUDE.local.md`, `AGENTS.md`, `.claude/**`,
`.mcp.json`, any depth), independent of the declaration; `@path` lines resolve relative to the file
(bounded depth 8, cycle-safe).

| Case | Arrange | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| root-instructions | edit `CLAUDE.md`; separately `AGENTS.md` | both differ; `instructions` differs | red | PC-35: drop instruction patterns from the inventory |
| import-chain | edit `docs/imported.md` (only reachable by AGENTS.md `@docs/imported.md`) | differs | red | PC-36: do not follow `@` imports |
| nested-and-claude-tree | add `sub/dir/CLAUDE.md`; edit `.claude/skills/x/SKILL.md`; edit `.mcp.json` | each differs | red | PC-37: match instruction names at the root only |
| import-escape | AGENTS.md `@../outside.md` | `unknown`, `instruction-import-outside:AGENTS.md` | red | PC-38: drop imports that leave the tree silently |
| import-home | AGENTS.md `@~/.claude/notes.md` | `unknown`, `instruction-import-user:AGENTS.md` | red | PC-39: treat `~` imports as relative paths |
| import-missing | AGENTS.md `@docs/gone.md` | `unknown`, `instruction-import-missing:docs/gone.md` | red | PC-40: skip missing import targets |
| import-cycle | `docs/a.md @docs/b.md`, `docs/b.md @docs/a.md` | returns within budget, `complete`, both files in inventory | red (inventory) | P: remove the visited set |
| undeclared-instruction | declaration `instructionPatterns` omits `.mcp.json` while `.mcp.json` is tracked | `unknown`, `instruction-outside-inputs:.mcp.json` | red | PC-41: derive the instruction list from the declaration instead of the fixed CLI list |

#### CP-22, S2a-2: `LiveLaneAttestationTests` (6 methods)

Exact filters `/*/Antiphon.Tests.Scripts/LiveLaneAttestationTests/<Method>`. Each method first
produces documents with `LiveLaneAttestationExport` from real S1 objects, then runs the harness.
Consumer API: `Get-LiveLaneEnvironmentAttestation -Path -Lane` -> `{status complete|unknown,
reasons, digest, components{executable, runtime, settings, account}}`; exit 2 on schema faults.
`Get-LiveLaneFingerprint -Attestation` adds `components.environment`; unknown attestation makes
the fingerprint unknown (`environment-unknown`).

**V-8 `CliVersionOrBytesInvalidates`** (5 cases).

| Case | Producer variant | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| same-version-new-bytes | owned image A vs image B (one byte differs), same `version` | digests differ; `executable` differs | red | PC-42: hash `version` and route only |
| version | same bytes, version `2.0.1` vs `2.0.2` | differ | red | PC-43: drop `version` |
| installed-target | copy unchanged, `installedTarget.sha256` changed | differ | red | PC-44: drop `installedTarget` |
| pre-post-drift | `pre.sha256 != post.sha256` on one row | `unknown`, `executable-drift:<uid>` | red | PC-45: compare only `pre` with attested |
| equal | two documents produced independently for image A (different PID, start, session, temp path) | digests equal | stub-green | P: include PID or path in the digest |

**V-9 `WrapperPayloadInvalidates`** (4 cases).

| Case | Variant | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| chain-payload | chain entry bytes changed | differ | red | PC-46: drop `chain` from the digest |
| chain-shape | wrapper chain of 1 vs direct copy (chain empty) | differ | red | PC-47: hash chain entries as a set that ignores count |
| chain-unresolved | chain entry `sha256: null` | `unknown`, `chain-unresolved:<uid>` | red | PC-48: skip null chain entries |
| observed-chain | `observed.chain` differs from `attested.chain` | `unknown`, `executable-mismatch:<uid>` | red | PC-49: compare observed image only, not chain |

**V-10 `AttestedPathMismatchRefuses`** (5 cases).

| Case | Variant | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| launched-path | `observed.path != attested.path` | `unknown`, `executable-mismatch:<uid>` | red | PC-50: drop the path comparison |
| launched-hash | `observed.sha256 != attested.sha256` | same | red | PC-51: drop the hash comparison |
| path-route | `attested.route='path-search'`; separately leaf name `cl.exe` | `unknown`, `route-not-owned:<uid>` | red | PC-52: accept any route |
| missing-observation | one row without `observed` | `unknown`, `observation-missing:<uid>` | red | PC-53: treat a missing observation as equal |
| any-row | rows 1..5 valid, row 6 mismatched | whole document `unknown`; no partial digest emitted | red | PC-54: evaluate only `rows[0]` |

**V-11 `ConfigurationInvalidates`** (10 cases). Settings come from the real
`LiveClaudeSettingsProjection.Read` over the owned profile, then are serialized.

| Case | Producer variant | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| user | `permissions.allow` edit in `<config>/settings.json`, no revision field anywhere | differ; `settings` differs | red | PC-55: hash `sources` kinds and states only, not `projectionDigest` |
| project-local | `<cwd>/.claude/settings.json`; separately `settings.local.json` | both differ | red | PC-56: drop `project`/`local` sources |
| managed | managed-policy edit with `ManagedLocationProven=true` | differ | red | PC-57: drop the `managed` source |
| global-state | `.claude.json` `remoteControlAtStartup` edit, settings.json unchanged | differ | red | PC-58: drop the `global` source |
| overlay | `--settings` overlay bytes changed | differ | red | PC-59: drop `overlay` sources |
| model | requested/effective model `haiku` vs named other | differ | red | PC-60: drop `model` |
| env-tristate | `DISABLE_AUTOUPDATER` absent / empty / `value:1` | three distinct digests | red | PC-61: normalise `empty` to `absent` |
| runtime-identity | `dotnetRuntime`; separately `restoredPackagesDigest` | both differ | red | PC-62: drop `runtime` |
| volatile-only | `.claude.json` `numStartups`, `lastSessionId` changed | equal | stub-green | P: hash raw `.claude.json` |
| credential-free | profile carries `oauthAccount`, `primaryApiKey` sentinel values; hand-forged document adds a credential key | sentinel bytes absent from document, consumer output and digest material; forged document exit 2 `credential-field-present` without echoing the value | red (forged case) | PC-63: accept unknown fields under `environment` |

**V-12 `UnknownSettingsRefuseReuse`** (9 cases).

| Case | Variant | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| unknown-key | unknown behaviour key in user settings (`ReuseEligible=false` from S1) | `unknown`, `settings-not-reusable` | red | PC-64: ignore `settings.reuseEligible=false` |
| managed-unverified | `ManagedLocationProven=false` (`absent-unverified`) | `unknown`, `source-unverified:managed` | red | PC-65: accept `absent-unverified` as absent |
| unreadable | user settings is invalid JSON (`unreadable`) | `unknown`, `source-unreadable:user` | red | PC-66: accept `unreadable` |
| missing-source-entry | forged: `global` source removed | `unknown`, `source-missing:global` | red | PC-67: check only listed sources, not the required kind set |
| additional-source | `additionalSources` unresolved entry | `unknown`, `source-unresolved` | red | PC-68: drop unknown source kinds |
| forged-eligible | forged: `reuseEligible=true` but one source `unreadable` | `unknown` (recomputed) | red | PC-69: trust the document's `reuseEligible` |
| runtime-missing | `restoredPackagesDigest` null | `unknown`, `runtime-missing:restoredPackagesDigest` | red | PC-70: allow null runtime fields |
| string-boolean | forged: `reuseEligible:"true"` | exit 2 `attestation-invalid` | red | PC-71: coerce with `[bool]` |
| schema | forged: unknown top-level field; separately `schemaVersion:2` | exit 2 each | red | PC-72: ignore unknown fields and version |

**V-13 `ExternalEpochIsNotInferred`** (11 cases), D-6. API `Test-LiveLaneExternalFreshness -Policy
AlwaysRerun|Bounded24h -ExecutedAtUtc -NowUtc -Candidate {nonce, host, account} -Probes
-Revocations`. Fixed horizon 24 h; skew 120 s; probe window 60 s (plan budget).

| Case | Arrange | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| always-rerun-default | no `-Policy`, everything else fresh and equal | `reuseEligible=$false`, `policy-always-rerun` | red | PC-73: default the policy to `Bounded24h` |
| no-epoch | Bounded24h, `external.epoch=null`, no probes | false, `external-epoch-unknown` | red | PC-74: treat a null epoch as equal |
| bounded-fresh | Bounded24h, age 23:59:59, auth and control probes 30 s old for this nonce/host/account | `true`, `d6-bounded-exception`, `externalProof=$false` | stub-green | P: return false always (positive control for the switch) |
| expired | age exactly 24:00:00; separately 24:00:01 | both false, `reuse-expired` (age < 24 h only) | red | PC-75: use `-gt` instead of `-ge` on the horizon |
| future-skew | executed now+121 s false `clock-skew`; now+119 s accepted as age 0 | as stated | red | PC-76: drop the skew check |
| probe-stale | control probe 61 s old | false, `probe-stale:control` | red | PC-77: check only the auth probe age |
| probe-foreign-nonce | probes bound to another candidate nonce | false, `probe-foreign` | red | PC-78: skip the nonce comparison |
| probe-failed | auth probe result `AuthExpired` | false, `probe-failed:auth` | red | PC-79: accept any probe result |
| horizon-fixed | `-MaxAgeHours 48` | exit 2 `horizon-not-configurable` | red | PC-80: honour a caller horizon |
| host-account | probe host or account enrollment differs from receipt | false, `host-mismatch` / `account-mismatch` | red | PC-81: compare host only |
| incident-revoked | revocation entry naming the epoch or receipt | false, `revoked` | red | PC-82: ignore `-Revocations` |

#### CP-9, S2b: `LiveLaneReceiptTests` (6 methods)

Exact filters `/*/Antiphon.Tests.Scripts/LiveLaneReceiptTests/<Method>`.

**V-14 `ExactRosterRequired`** (8 cases). `Test-LiveLaneReceipt -Receipt -Manifest`.

| Case | Receipt variant | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| exact | six expanded UIDs, all Passed | valid, `outcome='GREEN'` | stub-green | P: refuse everything |
| dropped | five rows | invalid, `roster-incomplete` | red | PC-83: compare counts with `-le` |
| duplicate | one UID twice (still six rows) | invalid, `roster-duplicate` | red | PC-84: compare sets, not multisets |
| extra-expansion | extra `(codex)` expansion of the argument method | invalid, `roster-unexpected` | red | PC-85: match on method name without arguments |
| renamed | one UID not in the manifest, five valid | invalid, `roster-unexpected` | red | PC-86: count-only comparison |
| planned-executed | planned six, executed five, `missing=1` | not GREEN, `roster-incomplete` | red | PC-87: validate `planned` only |
| skipped | one row `Skipped` | `outcome='INCONCLUSIVE'`, code 20, never GREEN | red | PC-88: count Skipped as passed |
| zero | zero rows and zero counts | invalid, `roster-empty` | red | PC-89: allow an empty roster when the manifest read fails |

**V-15 `TypesAndCountsFailClosed`** (13 cases).

| Case | Receipt variant | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| valid | complete typed receipt | valid | stub-green | P: refuse everything |
| string-bool | `sourceClean:"true"` | invalid, `type:sourceClean` | red | PC-90: `[bool]` coercion |
| string-count | `passed:"6"` | invalid, `type:passed` | red | PC-91: `[int]` coercion |
| negative | `durationMs:-1`; separately `failed:-1` | invalid | red | PC-92: drop the non-negative check |
| counts-outcomes | `passed=6` but rows show 5 Passed, 1 Failed | invalid, `counts-mismatch` | red | PC-93: trust summary counts |
| unknown-field | extra top-level field | invalid, `field:<name>` | red | PC-94: ignore unknown fields |
| kind | `kind='live-lane-use'` passed as execution | invalid, `kind` | red | PC-95: drop the kind check |
| schema-version | `schemaVersion:2` | invalid, `schema` | red | PC-96: accept `-ge 1` |
| null-required | `fingerprint.digest:null` | invalid, `required:fingerprint.digest` | red | PC-97: treat null as empty string |
| outcome-enum | row outcome `Pass` | invalid, `outcome-enum` | red | PC-98: accept any string starting with `Pass` |
| classifier-exit | `finalExit:0` with classifier verdict 20 | invalid, `classifier-mismatch` | red | PC-99: read `finalExit` only |
| artifact-digest | one artifact file byte changed after receipt | invalid, `artifact-digest:<name>` | red | PC-100: check digest presence, not value |
| time-order | `endedUtc < startedUtc` | invalid, `time-order` | red | PC-101: drop the order check |

**V-16 `LineageAndHostBound`** (10 cases). `Get-LiveLaneState` with Bounded24h and fresh probes so
that only the named fact differs; owned Git repo answers ancestry with real `git merge-base
--is-ancestor`.

| Case | Variant | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| same-line-ancestor | receipt at X on `rc/fixture-1`, candidate Y with X ancestor, same ref | `SATISFIED`, `gateCredit=$false` | stub-green | P: refuse everything |
| foreign-repo | `repository` identity differs | `DUE`, `foreign-repository` | red | PC-102: drop the repository comparison |
| other-line | receipt ref `rc/fixture-0` | `DUE`, `foreign-ref` | red | PC-103: compare ref prefixes `rc/` only |
| not-ancestor | receipt SHA on a divergent branch (force-moved ref) | `DUE`, `not-ancestor` | red | PC-104: skip the ancestry check when refs match |
| missing-object | receipt SHA absent from the repo | `DUE`, `source-missing` | red | PC-105: treat `merge-base` exit 128 as ancestor |
| host | host enrollment differs | `DUE`, `host-mismatch` | red | PC-106: drop the host comparison |
| account | account enrollment differs | `DUE`, `account-mismatch` | red | PC-107: drop the account comparison |
| lane | lane id differs | `DUE`, `foreign-lane` | red | PC-108: drop the lane comparison |
| dirty | `sourceClean=$false` | receipt never selected, `DUE`, `source-dirty` | red | PC-109: ignore `sourceClean` |
| issuer | `authority='trusted'` with no verifiable signature | invalid, `issuer-unqualified`; diagnostic authority still yields `gateCredit=$false` | red | PC-110: accept any `authority` string |

**V-17 `NewerRedRevokes`** (8 cases).

| Case | History | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| green-then-red | green t1, red t2, same fingerprint | `DUE`, `newer-red` | red | PC-111: select the newest GREEN ("search backward") |
| newer-inconclusive | green t1, inconclusive t2 | `DUE`, `newer-inconclusive` (default, Q-4) | red | PC-112: skip INCONCLUSIVE rows when finding the latest attempt |
| revocation | green t1 plus a revocation of its digest | `DUE`, `revoked` | red | PC-113: ignore revocation records |
| red-then-green | red t1, green t2 | `SATISFIED` from t2 | stub-green | P: any red in history makes DUE |
| other-fingerprint-red | green F1 t1, red F2 t2; candidate F1 | `SATISFIED` | stub-green | P: revoke across fingerprints |
| last-green-only | green, red, inconclusive, use record | `lastGreen` is the green receipt digest | red | PC-114: set `lastGreen` to the newest record of any kind |
| order-independent | same history as green-then-red, files listed shuffled | same result | red | PC-115: order by enumeration instead of `endedUtc` then sequence |
| contradictory-duplicate | two receipts with one run id, GREEN and RED | invalid, `contradictory-receipts` | red | PC-116: keep the last one read |

**V-18 `ReuseIsNotExecution`** (7 cases). `New-LiveLaneUseRecord` after a SATISFIED state.

| Case | Arrange | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| use-record | SATISFIED under Bounded24h | record `kind='live-lane-use'`, `executedNow=0`, `reused=$true`, references the receipt digest and run id, candidate SHA bound; executed counts 0 | red | PC-117: copy the receipt's counts into the use record |
| receipt-immutable | same | receipt file SHA-256 identical before and after | red | PC-118: rewrite the receipt with a `lastUsed` field |
| no-chained-reuse | history whose newest GREEN-like entry is a use record | `DUE`, `no-execution-receipt` | red | PC-119: accept a use record as a reuse source |
| forged-executed | use record with `executedNow:6` | invalid, `use-executed` | red | PC-120: drop the `executedNow=0` check |
| gate-credit | every state from V-14..V-18 cases | `gateCredit` is `[bool]$false`, `effectiveMode='Full'`, `activation='shadow-only'` | red | PC-121: set `gateCredit=($state -eq 'SATISFIED')` |
| no-overwrite | target path already exists | refuse exit 2 `use-record-exists`, existing bytes unchanged | red | PC-122: write with overwrite |
| policy-default | equal green, no `-ReusePolicy` | `DUE`, `policy-always-rerun`; no use record file | red | PC-123: default status to `Bounded24h` |

**V-19 `InputDriftRefuses`** (8 cases).

| Case | Variant | Decisive assertion | Red today | Mutant |
|---|---|---|---|---|
| equal-all | equal fingerprint, roster, attestation; Bounded24h; fresh probes | `SATISFIED`, `localCompatibility='SATISFIED'`, `reuseEligible=$true`, `gateCredit=$false` | stub-green | P: always DUE (positive control) |
| fingerprint-unequal | candidate `testSources` differs | `DUE`, `fingerprint-changed:testSources` | red | PC-124: compare `laneContract` component only |
| component-tamper | receipt components do not recompute to its digest | invalid, `fingerprint-tamper` | red | PC-125: trust the stored top-level digest |
| roster-changed | manifest roster digest differs, inputs equal | `DUE`, `roster-changed` | red | PC-126: drop the roster comparison |
| unknown-input | candidate fingerprint `unknown` with an equal digest field | `DUE`, `unknown-input` before any receipt match | red | PC-127: compare digests before checking `status` |
| pre-post | receipt `InputBefore != InputAfter` | never GREEN; invalid, `input-drift` | red | PC-128: check executables only |
| attestation-unknown | candidate environment attestation `unknown` | `DUE`, `environment-unknown` | red | PC-129: skip the attestation status |
| stale | receipt age 24 h + 1 s under Bounded24h | `DUE`, `reuse-expired` (state wiring calls V-13 freshness with the receipt's `endedUtc`) | red | PC-130: pass `NowUtc` as the executed time |

### Scoped-rc safety witnesses

| Rule | Witnesses |
|---|---|
| A scoped selection never skips a project that a changed file or a downstream dependent affects | V-2 `transitive-edge` (closure equals the reverse of `Get-RcDownstream`), `edge-added`, `shared-build-inputs`; V-3 all; V-4 unknown fallbacks; V-5..V-7 rot guards. Any input not proven covered makes the lane DUE. |
| Shadow-first: compared with Full, never replaces it | V-1 `shadow-only` (reader digest unchanged, `effectiveMode='Full'`), V-18 `gate-credit` over every state. No file of the Full path (`release-selection.ps1`, nightly, policy) changes. |
| A stale or foreign receipt never satisfies a gate | V-16 all, V-19 `stale`, V-13 `expired`/`future-skew`/`host-account`, V-17 `revocation`. No S2 output carries gate credit at all (V-18). |
| D-6: 24 h reuse with expiry, always-rerun as the switch | V-13 (`always-rerun-default`, `bounded-fresh`, `expired`, `horizon-fixed`), V-18 `policy-default`, V-19 `stale`. |
| Fingerprint inputs complete | V-1 `missing-root`/`real-head`, V-4, V-5 `undeclared-runtime-literal`, V-6, V-7 `undeclared-instruction`, V-12. |

### Guards the regression

- R-1: equal inputs give equal digests independent of candidate SHA, clone path and process facts
  (V-1, V-8 `equal`).
- R-2: every byte, mode, path and edge in the closure, runtime, contract and instruction sets
  changes the digest, and unrelated inputs do not (V-1..V-3, V-5, V-7).
- R-3: unknown input, rot or unresolved environment makes the lane DUE before any receipt is
  considered (V-4, V-6, V-7, V-10, V-12, V-19 `unknown-input`/`attestation-unknown`).
- R-4: executable identity is bytes plus chain plus installed target, and a launched image that
  differs from the attested one refuses (V-8..V-10).
- R-5: loaded settings, managed policy, global state, overlay, model, env and runtime change the
  digest; credentials never appear (V-11).
- R-6: reuse needs the explicit D-6 switch, a fresh candidate probe and age under 24 h (V-13,
  V-18, V-19).
- R-7: a receipt is exact, typed, lineage-bound and revocable; only GREEN updates last-green
  (V-14..V-17).
- R-8: a reuse is a use record, never an execution, and never gate credit (V-18).

### Guard inventory

Plan references: **F** = Input fingerprint and lane state, **E** = Execution/credentials (CLI
identity, effective configuration, external-state contract), **T** = Receipt, candidate checks and
trust, **D6** = decision D-6. All guards below are safety-critical: bypassing any of them lets a
changed or unproven input, a foreign or stale receipt, or a reuse earn a SATISFIED state. Each is
independently bypassable and maps 1:1 to a distinct PC with the same number. Precision controls
(`P` in the tables) are not safety guards and get no PC.

| G | Ref | Guard | PC |
|---|---|---|---|
| G-1 | F | candidate SHA/time/run id excluded from material | PC-1 |
| G-2 | F | no absolute or scratch path in material | PC-2 |
| G-3 | F | file mode is part of an entry | PC-3 |
| G-4 | F | empty blob distinct from absent path | PC-4 |
| G-5 | F | domain separation by lane id and schema version | PC-5 |
| G-6 | F | status is shadow-only, gateCredit false | PC-6 |
| G-7 | F | empty declared root refuses | PC-7 |
| G-8 | F | minimum roots and project directories hashed | PC-8 |
| G-9 | F | transitive upstream closure | PC-9 |
| G-10 | F | project files are inventory members | PC-10 |
| G-11 | F | closure read from the candidate graph | PC-11 |
| G-12 | F | shared build files hashed | PC-12 |
| G-13 | F | path is part of an entry | PC-13 |
| G-14 | F | whole selected test project directory | PC-14 |
| G-15 | F | linked inputs outside the project | PC-15 |
| G-16 | F | glob-expanded inputs | PC-16 |
| G-17 | F | core project resolved exactly, no fallback | PC-17 |
| G-18 | F | dynamic import refuses | PC-18 |
| G-19 | F | missing linked input refuses | PC-19 |
| G-20 | F | closure cycle refuses | PC-20 |
| G-21 | F | include outside the tree refuses | PC-21 |
| G-22 | F | symlink refuses | PC-22 |
| G-23 | F | gitlink refuses | PC-23 |
| G-24 | F | ambiguous core project refuses | PC-24 |
| G-25 | F | missing manifest refuses | PC-25 |
| G-26 | F | strict declaration schema | PC-26 |
| G-27 | F | runtime roots hashed | PC-27 |
| G-28 | F | contract files hashed | PC-28 |
| G-29 | F | manifest and declaration bytes hashed | PC-29 |
| G-30 | F | non-code assets under declared roots hashed | PC-30 |
| G-31 | F | undeclared runtime literal refuses | PC-31 |
| G-32 | F | relevant namespace outside inputs refuses | PC-32 |
| G-33 | F | block-scoped namespace form scanned | PC-33 |
| G-34 | F | test namespace roots scanned | PC-34 |
| G-35 | F | instruction files hashed | PC-35 |
| G-36 | F | instruction imports followed | PC-36 |
| G-37 | F | nested instructions and `.claude/**`, `.mcp.json` hashed | PC-37 |
| G-38 | F | escaping import refuses | PC-38 |
| G-39 | F | home import refuses | PC-39 |
| G-40 | F | missing import refuses | PC-40 |
| G-41 | F | CLI-loaded list independent of declaration | PC-41 |
| G-42 | E | image bytes in executable digest | PC-42 |
| G-43 | E | version in executable digest | PC-43 |
| G-44 | E | installed target in executable digest | PC-44 |
| G-45 | E | pre/post executable drift refuses | PC-45 |
| G-46 | E | chain payload bytes hashed | PC-46 |
| G-47 | E | chain shape hashed | PC-47 |
| G-48 | E | unresolved chain refuses | PC-48 |
| G-49 | E | observed chain must equal attested | PC-49 |
| G-50 | E | launched path must equal attested | PC-50 |
| G-51 | E | launched hash must equal attested | PC-51 |
| G-52 | E | PATH/cl route refuses | PC-52 |
| G-53 | E | missing observation refuses | PC-53 |
| G-54 | E | every row checked | PC-54 |
| G-55 | E | user settings projection hashed | PC-55 |
| G-56 | E | project/local settings hashed | PC-56 |
| G-57 | E | managed policy hashed | PC-57 |
| G-58 | E | `.claude.json` global state hashed | PC-58 |
| G-59 | E | overlay hashed | PC-59 |
| G-60 | E | model hashed | PC-60 |
| G-61 | E | env absent/empty/value distinct | PC-61 |
| G-62 | E | runtime identity hashed | PC-62 |
| G-63 | E | credential fields refused, never echoed | PC-63 |
| G-64 | E | S1 not-reusable projection refuses | PC-64 |
| G-65 | E | absent-unverified managed source refuses | PC-65 |
| G-66 | E | unreadable source refuses | PC-66 |
| G-67 | E | required source kinds present | PC-67 |
| G-68 | E | unresolved additional source refuses | PC-68 |
| G-69 | E | eligibility recomputed, flag not trusted | PC-69 |
| G-70 | E | missing runtime field refuses | PC-70 |
| G-71 | E | typed booleans | PC-71 |
| G-72 | E | strict attestation schema/version | PC-72 |
| G-73 | D6 | default policy is AlwaysRerun | PC-73 |
| G-74 | D6/E | no epoch, no reuse | PC-74 |
| G-75 | D6 | 24 h expiry boundary | PC-75 |
| G-76 | T | future timestamp beyond 120 s refuses | PC-76 |
| G-77 | D6 | control probe freshness | PC-77 |
| G-78 | D6 | probe bound to candidate nonce | PC-78 |
| G-79 | D6 | failed probe refuses | PC-79 |
| G-80 | D6 | horizon not caller-configurable | PC-80 |
| G-81 | D6 | probe host and account binding | PC-81 |
| G-82 | D6/T | incident revocation | PC-82 |
| G-83 | T | dropped UID refuses | PC-83 |
| G-84 | T | duplicate UID refuses | PC-84 |
| G-85 | T | extra argument expansion refuses | PC-85 |
| G-86 | T | unknown UID refuses | PC-86 |
| G-87 | T | planned equals executed | PC-87 |
| G-88 | T | skipped is never green | PC-88 |
| G-89 | T | empty roster refuses | PC-89 |
| G-90 | T | typed booleans | PC-90 |
| G-91 | T | typed integers | PC-91 |
| G-92 | T | non-negative counts and durations | PC-92 |
| G-93 | T | counts equal row outcomes | PC-93 |
| G-94 | T | unknown receipt field refuses | PC-94 |
| G-95 | T | receipt kind | PC-95 |
| G-96 | T | schema version | PC-96 |
| G-97 | T | required non-null fields | PC-97 |
| G-98 | T | closed outcome enum | PC-98 |
| G-99 | T | final exit equals classifier verdict | PC-99 |
| G-100 | T | artifact digests verified | PC-100 |
| G-101 | T | time order | PC-101 |
| G-102 | T | same repository | PC-102 |
| G-103 | T | same rc line ref | PC-103 |
| G-104 | T | receipt source is an ancestor | PC-104 |
| G-105 | T | missing source object refuses | PC-105 |
| G-106 | T | same host | PC-106 |
| G-107 | T | same account | PC-107 |
| G-108 | T | same lane | PC-108 |
| G-109 | T | clean source only | PC-109 |
| G-110 | T/D-9 | no trusted issuer in S2 | PC-110 |
| G-111 | T | newer red revokes | PC-111 |
| G-112 | T | newer inconclusive revokes (Q-4 default) | PC-112 |
| G-113 | T | revocation record honoured | PC-113 |
| G-114 | T | only GREEN updates last-green | PC-114 |
| G-115 | T | order by recorded time, not enumeration | PC-115 |
| G-116 | T | contradictory duplicates refuse | PC-116 |
| G-117 | T | use record executes nothing | PC-117 |
| G-118 | T | original receipt immutable | PC-118 |
| G-119 | T | reuse needs an execution receipt | PC-119 |
| G-120 | T | forged use execution refuses | PC-120 |
| G-121 | T | no gate credit from any state | PC-121 |
| G-122 | T | use record never overwrites | PC-122 |
| G-123 | D6 | status default AlwaysRerun, no record | PC-123 |
| G-124 | F/T | every component compared | PC-124 |
| G-125 | T | receipt components recomputed | PC-125 |
| G-126 | T | roster digest compared | PC-126 |
| G-127 | F/T | unknown input checked before match | PC-127 |
| G-128 | T | pre/post input drift never green | PC-128 |
| G-129 | E/T | unknown environment makes DUE | PC-129 |
| G-130 | D6/T | state uses receipt time for expiry | PC-130 |

Count: guards = 130, mapped = 130, missing = 0, duplicate PC maps = 0.

### Positive controls

**Binding required after Code** for every row: no payload exists yet. The binder at the confirmed
landed source supplies the symbol, literal patch, unique match count, file/preimage/mutant hashes,
command and failure set per [the mechanical PC contract](../../mechanical-pc-contract.md).
Classification: `binding` / `binding-missing` for all 130.

PC-n = break G-n by the mutant named in the V table row that cites PC-n (one parseable PowerShell
edit in `scripts/lib/live-lane-fingerprint.ps1`, `live-lane-attestation.ps1`,
`live-lane-receipt.ps1` or `scripts/live-lane-status.ps1`; never the harness, the test, the S1
producer or the reader). Expect exactly the method of that table red at assertion
`C1545 <Method>/<case>` of that row, with every other case of the method still passing where the
mutant does not reach it. Recipe, variant `v1`, case and oracle are the row itself; reachability is
the fixture state the row names with every other input valid. Cycle per PC: mutant run red,
restore, restored run green, filter `/*/Antiphon.Tests.Scripts/<Class>/<Method>`, MinExecuted 1,
exactly one failed method on the mutant, zero skipped. A build or harness error, zero tests or a
red at another row is not a pass. Mutation runs break/red/restore/green after land; Code runs the
V rows; Review judges the table before land.

Grouping by method for the runner (exact method filter, PC ids):

| Method | PCs | Count |
|---|---|---:|
| LiveLaneFingerprintTests/StableCanonicalInputs | 1-7 | 7 |
| LiveLaneFingerprintTests/ReferenceClosureInvalidates | 8-13 | 6 |
| LiveLaneFingerprintTests/TestAndFixtureInvalidates | 14-17 | 4 |
| LiveLaneFingerprintTests/UnknownDependencyRefuses | 18-26 | 9 |
| LiveLaneFingerprintTests/RuntimeScriptsIncluded | 27-31 | 5 |
| LiveLaneFingerprintTests/RelevantNamespaceOutsideInputsRefuses | 32-34 | 3 |
| LiveLaneFingerprintTests/LoadedInstructionsOutsideInputsRefuse | 35-41 | 7 |
| LiveLaneAttestationTests/CliVersionOrBytesInvalidates | 42-45 | 4 |
| LiveLaneAttestationTests/WrapperPayloadInvalidates | 46-49 | 4 |
| LiveLaneAttestationTests/AttestedPathMismatchRefuses | 50-54 | 5 |
| LiveLaneAttestationTests/ConfigurationInvalidates | 55-63 | 9 |
| LiveLaneAttestationTests/UnknownSettingsRefuseReuse | 64-72 | 9 |
| LiveLaneAttestationTests/ExternalEpochIsNotInferred | 73-82 | 10 |
| LiveLaneReceiptTests/ExactRosterRequired | 83-89 | 7 |
| LiveLaneReceiptTests/TypesAndCountsFailClosed | 90-101 | 12 |
| LiveLaneReceiptTests/LineageAndHostBound | 102-110 | 9 |
| LiveLaneReceiptTests/NewerRedRevokes | 111-116 | 6 |
| LiveLaneReceiptTests/ReuseIsNotExecution | 117-123 | 7 |
| LiveLaneReceiptTests/InputDriftRefuses | 124-130 | 7 |

All 130 PCs run (none waived). Precision mutants (`P`) are optional evidence, not PCs.

### Out of scope

- S1c-native acceptance (CP-4..7, CP-21): needs an eligible authenticated Windows host, ConPTY and
  D-3/D-4/D-11. Only a native run can prove that the real wrapper emits the attestation fields of
  this design, that `observed` is the real spawned image, that the real CLI loads exactly the
  sources the projection lists, that the OS managed-policy location is real, and real probe
  latencies. Until then every S2a-2 attestation in tests comes from the test-side exporter.
- S2c issuer (CP-10, D-9), S3 gates (CP-11..14, D-5), S4a lanes (CP-15): separate TestDesigns.
  A trusted issuer is refused in S2 (G-110); signature verification is S2c.
- Package-content identity of the NuGet cache and SDK images: S2a-2 requires a
  `restoredPackagesDigest` field and refuses when it is absent; computing it on a host is the native
  producer's job.
- Remote model/service stability: no test here can prove it; D-6 Bounded24h records
  `externalProof=$false`.
- Final Review 185b71e2 advisories (filed as **CARD-1645**, Normal/Normal): (1) CP-30 re-seed with a
  hand-edited fixture can hide drift; (2) a removed declared additive method is not diagnosed;
  (3) the sink audit is a deny-list (`LiveClaudeSinkAudit`); (4) CP-30 reports
  `undeclared-new-uid` on later trees. None touches S2 files; item 3 should close before S1c-native.

### Implementation order (sequential Code chain)

One Code task per slice, run one after another on server2 (Opus). Investigation a9b3bc09 found 16%
of CARD-1545-series defects came from parallel slices clashing; do not run these in parallel.
Each slice: commit and push the red commit (tests + harness + fail-open stub), run its new row
once at that SHA to record the red (expected red cases as listed; not a gate), commit and push the
fix, then run the slice's closed rows at the fix SHA.

1. **S2a-1** (45-60 min authoring). Red: `LiveLaneFingerprintTests` (7), harness, data file, stub
   library and status. Green: fingerprint library and status. Rows: CP-8.
2. **S2a-2** (45-60 min), branched from the landed S2a-1 tip. Red: `LiveLaneAttestationTests` (6),
   `LiveLaneAttestationExport`, harness, stub attestation library. Green: attestation library,
   fingerprint `environment` component, status `-Attestation`. Rows: CP-22, then CP-31
   (fingerprint regression on the same build).
3. **S2b** (45-60 min), from the landed S2a-2 tip. Red: `LiveLaneReceiptTests` (6), harness, stub
   receipt library. Green: receipt validation, state evaluation, use record, status `-History`.
   Rows: CP-9, CP-32, CP-33.

A slice that needs to edit a file outside its listed set stops and returns `next: plan` with the
exact seam.

### Overlap with in-flight work and shared files

In flight on 2026-11-02 (investigation a9b3bc09): 00abf1bf CARD-0824 S1 slot pool
(runner/dispatcher), 253f7e9d CARD-1624 Review, bd1e86ea CARD-1514 landing fence (group 111,
LandingGit), 250a07fb CARD-1514 Windows Full run, the reconciler fixes, CARD-1610 S4a plan. None
of them touches an S2 file; all S2 files are new. Some edit the live minimum roots
(`src/Antiphon.SessionRunner/`, `server/`), which changes the real fingerprint value but not any
test: every value assertion uses the owned fixture, and `real-head` pins no value.

Shared files Code must check before each push and before land:

- `tests/test-execution-policy.json` and the classification roster: no change expected (new
  Antiphon.Tests classes are required by default; Integration tag satisfies
  `TestLaneCategoryGuardTests`; none is Slow, so `slow-tests-allowlist.txt` is unchanged). If Code
  finds a classification requirement, it stops (policy is a protected input of CP-30-style checks).
- Land-graph digest (`server/Application/Services/LandVerificationBuildGraph.{evaluation,manifest}.txt`,
  re-pinned by S1 in `4c0cdfe3a`): no csproj, props or targets edit is allowed, so the digest should
  not move. Before land, run `scripts/audit-land-verification-graph.ps1 -Check` through the build
  slot; if it fails, re-pin in a separate commit and report it. The landing-fence work may re-pin
  it too, so take the master value at land time.
- `scripts/lib/rc-project-selection.ps1`: read-only here. S4a will modify it; S4a Code must follow
  S2a-1 or merge-tree against it.
- `tests/live-lanes.json`: read-only here (CP-1/CP-25/CP-30 pin it).

### Open questions for Code (each with the default to use)

- Q-1 Declared inputs location: new `tests/live-lane-inputs.json`. Default yes; do not add fields
  to `tests/live-lanes.json`.
- Q-2 Inventory identity: Git blob ids plus mode and path, SHA-256 over canonical JSON on top.
  Default yes; a SHA-256 repository needs no change.
- Q-3 Real runtime roots: whole `scripts/` for the core lane. Default yes (D-8 conservative);
  narrowing needs a reviewed shadow comparison.
- Q-4 Newer INCONCLUSIVE for the same fingerprint revokes an older green. Default yes (fail
  closed); the plan names only red and revocation.
- Q-5 Expiry boundary: eligible only when age < 24 h. Default yes.
- Q-6 Attestation exporter location: `tests/Antiphon.Tests/Scripts/LiveLaneAttestationExport.cs`
  (no csproj edit). Default yes; S1c-native may move it to `tests/Shared/LiveClaude/` later.
- Q-7 Status exit codes: 0 with a state document (DUE or SATISFIED), 2 invalid input. Default yes.
- Q-8 Catalog-only graph reasons do not make the fingerprint unknown. Default yes; input faults in
  closure projects do.

## Performance budget

No build or test ran here. All values are **estimated, unmeasured, n=0** (CARD-1545/CARD-1404
provisional). Collect three comparable samples from the first red/fix/green runs; calibrate near 2x
measured p95 without buying extra runs.

| Phase | Estimate | Bound and recording |
|---|---|---|
| Antiphon.Tests isolated build | 8 min (plan sample 509 s, n=1, other host) | build cap 16 min; restore/build recorded separately |
| Harness case (owned Git repo, a few commits, 2-4 fingerprint calls) | 1-3 s | per-method `ExecutionBudget` 60 s, cleanup 10 s, drain 10 s |
| `StableCanonicalInputs` (includes one real-tree read, about 20k entries and about 70 csproj blobs) | 20-40 s | `ExecutionBudget` 120 s |
| One class run (6-7 methods) | 1.5-2 min | row timeout 4 min (YAML pin) |
| Slot admission | 0 expected | `build.slotWaitMinutes: 5`; exit 4 is not run, never unleased |

Shared setup: none expensive. No DB, AppHost, network or provider. Fixture repos are per case
because they are the subject; Git process spans and pwsh startup are recorded in the harness trace.
No new project is justified (R2). Unknown timing is null, never zero.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-8 | S2a-1 | tests/Antiphon.Tests -> bin-live-fingerprint/ | portable-live-fingerprint | /*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* | V-1..7, R-1..3 | StableCanonicalInputs,ReferenceClosureInvalidates,TestAndFixtureInvalidates,UnknownDependencyRefuses,RuntimeScriptsIncluded,RelevantNamespaceOutsideInputsRefuses,LoadedInstructionsOutsideInputsRefuse; exactly 7 passed, 0 failed/skipped | 7 | 10 |
| CP-22 | S2a-2 | tests/Antiphon.Tests -> bin-live-attestation/ | portable-live-attestation | /*/Antiphon.Tests.Scripts/LiveLaneAttestationTests/* | V-8..13, R-3..6 | CliVersionOrBytesInvalidates,WrapperPayloadInvalidates,AttestedPathMismatchRefuses,ConfigurationInvalidates,UnknownSettingsRefuseReuse,ExternalEpochIsNotInferred; exactly 6 passed, 0 failed/skipped | 6 | 10 |
| CP-31 | S2a-2 | CP-22 | portable-live-fingerprint-after-attestation | /*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* | V-1..7, R-1..3 | the 7 fingerprint methods; exactly 7 passed, 0 failed/skipped | 7 | 2 |
| CP-9 | S2b | tests/Antiphon.Tests -> bin-live-receipt/ | portable-live-receipt | /*/Antiphon.Tests.Scripts/LiveLaneReceiptTests/* | V-14..19, R-3, R-6..8 | ExactRosterRequired,TypesAndCountsFailClosed,LineageAndHostBound,NewerRedRevokes,ReuseIsNotExecution,InputDriftRefuses; exactly 6 passed, 0 failed/skipped | 6 | 10 |
| CP-32 | S2b | CP-9 | portable-live-fingerprint-after-receipt | /*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* | V-1..7, R-1..3 | the 7 fingerprint methods; exactly 7 passed, 0 failed/skipped | 7 | 2 |
| CP-33 | S2b | CP-9 | portable-live-attestation-after-receipt | /*/Antiphon.Tests.Scripts/LiveLaneAttestationTests/* | V-8..13, R-3..6 | the 6 attestation methods; exactly 6 passed, 0 failed/skipped | 6 | 2 |

One isolated build and one exact filter per row; a reuse row (`Build = CP-n`) shares only its own
After group's build. The union is the whole ordinary scope: 39 TUnit executions, 19 distinct
methods. CP-31/CP-32/CP-33 are the declared regression rows for the shared status/fingerprint
edits of S2a-2 and S2b, not extra runs. `Min` is executions (MinExecuted), never harness PASS rows.
CP ids 31-33 are new; the plan uses CP-1..30.

Import validation (this task): `dotnet Antiphon.Checkpoints.dll import --plan <this doc> --out .antiphon/s2-import/manifest.yaml` with an existing built tool (sha256 prefix `3eb4cb046739dd87`, from a master-based worktree) printed `imported 6 rows`, exit 0, no warning. Derived row timeouts without pins: 30 min for CP-8/CP-22/CP-9, 15 min for CP-31/CP-32/CP-33; the pins below replace them.

**Run per slice** with the checkpoint tool, one run per committed After group, then `wait` until
the exit is not 75. Import this companion, not the main plan:

```text
dotnet <isolated Antiphon.Checkpoints.dll> import --plan docs/superpowers/plans/2026-11-02-card-1545-S2a-S2b-test-design.md --out .antiphon/s2-checkpoints/manifest.yaml
```

Pin in the ignored YAML only: `build.slotWaitMinutes: 5`, `timeouts.rowMinutes: 16`, every
`checkpoints[].timeoutMinutes: 4`, `parallel.maxRows: 1`. Start with `--after S2a-1` (or `S2a-2`,
`S2b`), `--expected-source-sha <committed sha>`, `--serial` and:

| Run | Rows | Charged execution (`--total-timeout`) | Hard wall | Under 30 min |
|---|---|---:|---:|---|
| S2a-1 | CP-8 | 20 m | 25 m | yes |
| S2a-2 | CP-22, CP-31 | 24 m | 29 m | yes |
| S2b | CP-9, CP-32, CP-33 | 24 m | 29 m | yes |

A run expected or observed over 30 minutes goes to Debug with SHA, roster, phases and host load
before repetition (R1). Keep CHECKPOINT lines unedited; validate receipts at the committed SHA;
delete owned `bin-live-*` outputs after evidence is retained; run
`scripts/check-evidence-diff.ps1 -Repository . -BaseRef <base> -HeadRef HEAD`.

### Cost

**Estimated, not measured.** Ordinary V/R floor (Code) = sum of EstimatedMinutes = CP-8 10 +
CP-22 10 + CP-31 2 + CP-9 10 + CP-32 2 + CP-33 2 = **36 minutes** (three isolated builds at 8
minutes plus 12 minutes of class runs). Setup/bootstrap 2 minutes per slice = 6. Ordinary total =
**42 minutes**, excluding slot waits, red-commit runs (one per slice, about 2 minutes each on the
same build if Code reuses its red build, otherwise 10) and authoring (45-60 minutes per slice).

PC floor (Mutation): 130 PCs, all PowerShell-only edits. The TUnit dll is unchanged by them; the
harness resolves scripts from the repository that contains the build output, so one forced build
in the mutation worktree (8 min) serves every cycle. Per method one baseline green run (0.5 min x
19 = 9.5 min); per PC a mutant run plus a restored run (2 x 0.5 min) plus 0.25 min bookkeeping =
1.25 min. **PC floor = 8 + 9.5 + 130 x 1.25 = 180 minutes** (about 3 hours). If the mutation
contract requires a forced rebuild per cycle instead, the floor is 130 x 25 = **3,250 minutes**;
the no-rebuild route saves an estimated **3,070 minutes**, justified because no PC edits compiled
code. Total planned machine time = ordinary 42 + PC 180 = **222 minutes** (estimated), plus slot
waits and binding work. Shard the PCs across up to three worktrees at the same tip if wall time
matters (more than 15 rows, independent files per shard: fingerprint, attestation, receipt).

Before handoff: guards = 130, mapped = 130, missing = 0, duplicate PC maps = 0; all 130 PCs run;
Cost numeric (42 ordinary, 180 PC, 222 total, estimated).
