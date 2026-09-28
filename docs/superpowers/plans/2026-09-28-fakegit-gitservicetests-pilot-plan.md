# FakeGit: GitServiceTests pilot and gated migration

Plan-stage artifact for task `7647cb6a`, 2026-09-28. Complexity: hard. This document specifies future implementation and verification; no implementation or timing experiment was performed during Plan.

Source: [investigation](../../investigations/2026-09-28-test-reruns-and-fakegit-design.md), present at `c599994fc5bed87635d0ddb7c4aa0a531a8fb8fb`. Owners: [project conventions](../../project-context.md), [checkpoint/build rules](../../testing-and-build.md#checkpoint-manifest-card-0585), [stage handoffs](../../orchestration-loop.md#1-the-cycle-card-0146).

## Outcome and scope

Build one fixture-owned FakeGit command/state backend, make migrated test fixtures select it by default, and run those same test bodies against real Git with `ANTIPHON_TEST_REAL_GIT=1`. Production continues to use the real CLI. First prove the compact GitServiceTests contract and measure its actual wall-clock saving. **Only S1-S2 may be implemented before the pilot gate passes.** S3-S12 are ordered, conditional follow-on slices, not permission to implement the whole Git model up front.

The investigation's five large landing selections total about 122 minutes of serialized **case-duration sum**, not current suite wall time or a guaranteed saving. Its 5.20-second/12-case GitServiceTests observation is historical. Every benefit claim below must come from the planned paired measurements.

Out of scope: database replacement; global test-runner changes; queue, PowerShell and transcript performance work; a central timing database; wholesale production Git rewrites; GitDiffSpikeTests migration; release/nightly scheduling changes; and fixes owned by CARD-0792 or CARD-0793.

### Existing correctness follow-ups

On 2026-09-28, `pwsh -NoProfile -File scripts/card.ps1 get CARD-0792 -Json` returned **Backlog**, revisionCount 0, updatedAt `2026-09-28T17:37:21.283962Z`. The pinned GitServiceTests source still lacks its process limiter. CARD-0792 owns adding the attribute and ProcessSpawnLimitTests roster entry; this plan neither duplicates that fix nor waits for it.

Until its fix is available, pilot rows select GitServiceTests alone, set `TUNIT_MAX_PARALLEL_TESTS=1`, and use checkpoint `Serial=true`. The pinned TUnit 1.44 package documents that environment setting as the equivalent of `--maximum-parallel-tests`. Do not co-schedule another Antiphon.Tests or PTY test host during timing. This confines the missing-limiter class during the experiment; it does not close CARD-0792. Retain its fix unchanged when it lands. Every new class that can spawn a process must carry the assembly's existing `ParallelLimiter<ProcessSpawnLimit>`.

CARD-0793 separately owns card.ps1's Linux formatter reporting failure after a successful create. No card formatter or generated docs/cards file belongs in this change.

## Design decisions

### Command and filesystem boundaries

- Add an external-I/O seam `IGitCommandExecutor` under server/Application/Interfaces, with a typed result (exit code, stdout, stderr), working directory, tokenized arguments, explicit timeout/options, and CancellationToken last. Its production implementation belongs under server/Infrastructure/Git. Keep test types and backend selection out of server code.
- In S1, inject it and the existing `IFileSystem` into GitService, keeping existing constructor callers and DI composition valid. Use real defaults. Replace both private process paths, including TryRunGitAsync; preserve error-versus-absence handling, 30-second ordinary/10-minute clone deadlines, cancellation and kill/drain behavior, card-file invalidation, pre-commit sweeping, and trailers. Convert arguments at their call sites; do not introduce a generic string-splitting shell parser.
- GitService.CommitAllChangesAsync constructs LandingGit directly today. Give that nested I/O an explicit injectable production-default dependency so later fake calls cannot escape through it. Do not reimplement its privacy/owned-write policy. A focused real regression row covers the existing whole-index card-file test.
- Only after the pilot gate, route WorktreeManager's private process path, rollback's new LandingGit, and exercised filesystem readers through the same fixture dependencies. Audit GitWorkspaceService, TaskProgressGit, DelegationWorktreeService and LandWorkspace at each expanded call graph; do not claim a class is migrated while a setup/assertion/helper can shell out behind the backend.
- Preserve ILandingGit and ITaskProgressGit as public service contracts. A test adapter may share the FakeGit kernel, but protocol logic, database transactions, leases, guard decisions, and fault placement remain production code. Prefer keeping production parsing/validation above the executor seam rather than copying it into the fake.
- Use System.IO.Abstractions/TestingHelpers already pinned at 22.1.0. Real mode uses FileSystem; fake mode uses MockFileSystem for repository contents, index model, refs and administrative files. Replace tests' direct File/Directory access with the fixture filesystem without weakening assertions. No new filesystem package is needed.

### Shared test backend

Add `tests/Antiphon.Tests/TestHelpers/FakeGit/` containing the state kernel, command dispatcher, backend factory, observation/evidence records and fixture adapters. One backend instance owns setup, the system under test, and assertion readers. Keep state instance-local and dispose all roots. Real mode uses isolated local repos/remotes and fixture Git identity/config, never developer repositories or a network remote.

The factory accepts unset or `0` as fake and exactly `1` as real; malformed nonempty values fail visibly. Read the environment when constructing the fixture; never mutate a process-global backend switch from tests. Log the chosen backend, test identity and source SHA. Unconverted fixtures retain their current real defaults; change factory call sites class by class, never flip ScratchGitRepo or LandingGitFixture globally.

FakeGit is a semantic model, not a script of expected replies. Track immutable commits with ordered parents and trees, branch/tag refs, symbolic/detached HEAD, and separate index and working tree. Subsequent slices add linked worktrees, bare remotes, tracking refs and sequencer state. OIDs must be stable valid full-length identities within a run; they need not reproduce Git's object hashing.

Every accepted verb/flag combination has an explicit handler. An unsupported shape throws a diagnostic containing cwd and arguments **before mutation**. No default success, no canned service result, no fallback to git. Record commands, exit classes, mutations and CLI process starts. Backend-wiring tests deliberately miswire a service/fixture reader and must detect the escape. Review also inventories direct Process.Start/new LandingGit/static ScratchGitRepo calls in every migrated graph. Native exceptions are explicitly declared by test identity, never inferred from an unsupported-command exception.

ControlledLandingGit supplies useful graph ideas and fail-closed conventions, but its hard-coded landing topology and real-file projection are not a pilot dependency. Leave it unchanged through S1-S2. In S7, adapt its reusable behavior to the shared kernel only if its compatibility surface can be preserved; otherwise leave existing ControlledLandingGit consumers intact and use the new shared adapter for the named migrations. Do not migrate unrelated Controlled test classes to pay for this plan.

### Native resources remain native

FakeGit removes Git subprocesses; it does not certify OS semantics. Keep real PostgreSQL fixtures, real RepositoryMutationLease exclusion and durable journal handling in policy tests. A fake repository may have a fixture-owned physical control directory for the real lease/journal while its Git content lives in MockFileSystem. Define that projection explicitly, keep canonical identity consistent, and do not mirror a pretend binary Git index. If a case requires native content/registration bytes, hooks, OS handles, worker process identity or filesystem links, retain its real fixture as described below.

In particular, never replace RepositoryMutationLease with a permissive stub to make admission/concurrency tests fast. A simulated RunOwned callback may exercise application fault policy; it cannot qualify real PID/start-tick identity, descendant ownership or crash recovery. Existing native journal/process coverage remains authoritative.

### Same-body parity and independent observations

GitServiceTests retains all 12 existing methods and assertions (six naming methods, six Git integration methods). Mechanical fixture/FS plumbing is allowed. Add four service behavior cases in the same class: tag-at-current-HEAD is idempotent; an existing tag at a different commit is rejected without moving it; missing diff ref is an error; and duplicate branch creation preserves existing state. Thus the pilot roster is 16 tests in both modes.

The pilot command inventory includes setup and assertions, not just service calls:

| Surface | Required pilot shapes and observable contract |
|---|---|
| Setup | init with a fixed initial branch; config user.name/user.email; add .; initial commit. A configured identity is not a successful commit until an object/ref exists. |
| Branches | checkout -b with optional starting ref; checkout existing ref; branch --list/--show-current. Branch ancestry and files must follow the selected tree. |
| Artifacts | directory creation, .gitkeep, add path, commit -m with antiphon trailer; log -1 --format=%B. Preserve nested content and message/trailer bytes. |
| Tags | rev-parse HEAD; rev-list -n 1 tag including its missing-revision failure; tag creation/listing; unchanged and moved-HEAD retry outcomes. |
| Merge | merge --no-ff branch -m message: retain both parents even if a fast-forward was possible, set HEAD to workflow master and materialize the merged tree. |
| Diff | diff tag1..tag2: actual removed/added lines, file paths and unified hunk structure; invalid refs fail. Do not return text merely containing the two expected strings. |

Each full-observation run produces per-case snapshots from a read-only observer independent of mutation handlers/fault injection: ref targets, ordered parent topology, committed tree contents, staged/working contents, messages/trailers, branch identity, and relevant command stdout/exit/error category. Real observations query the actual repo; fake observations traverse stored state without calling fake command handlers. Both are also checked against explicit expected outcomes, so two identical bugs cannot pass by self-comparison.

Normalize fixture-root prefixes, generated task identifiers, line endings where the existing contract permits, timestamps, and OIDs through a **bijective graph mapping**. Preserve ref names, path spelling within the fixture, commit inequality/equality, parent order, content, trailers, diff signs/hunks and exact exit codes for supported command vectors. Stderr comparison uses the error category/phrases production branches on, not version-dependent advice. Never normalize all SHAs to one token or strip all stderr. Compare fake/real snapshots and complete TRX case rosters; a missing snapshot, skip, extra case, or unsupported command fails parity.

## Slice order and acceptance

Commit each group before its checkpoints. Later slices depend on all earlier accepted groups. No task should combine the pilot with higher-cost migration.

| Slice | Implementation boundary | Acceptance and reason for this order |
|---|---|---|
| S1 | Command/FS seam; test backend factory; minimal pilot state model. Add GitCommandExecutorTests (6 focused contracts), FakeGitPilotContractTests (8) and FakeGitEvidenceTests (6). No worktree/remotes/rebase model yet. | Preserve real defaults, errors/cancellation/argument fidelity and zero-escape fake wiring. New tests have observable negative cases, not constructor/constant-only assertions. |
| S2 | Convert only GitServiceTests; four additional behavior cases; observation recorder and offline comparison/measurement script `scripts/compare-fakegit-evidence.ps1`. | CP-1 through CP-11: same 16 tests pass both ways; full observations match; measured pilot gate passes. Commit S1-S2 together before the single pilot checkpoint run. Stop here on a no-go. |
| S3 | WorktreeBaseSelectionTests, including its SiblingWarnings partial; backend-aware Scratch fixture; WorktreeManager/necessary reader seams; shared DelegationTestServices injection. Add FakeGitWorktreeContractTests (8). | First medium-risk migration: branch choice, commit-vs-blob ref resolution, full-tip ancestry, patch equivalence, cancellation, and branch occupancy. Add hash-object/commit-peeling and patch-comparison shapes actually used, not just worktree add/list. Preserve TryAdd overrides and use the one legal graph helper. |
| S4 | DelegationWorktreeTests, including V25; linked worktree lifecycle and exercised merge/rebase/status/ignored-file operations. | Next safest useful extension; 208 s over 15 historical checkpoint rows versus 53 s/7 rows for S3. Preserve conflict/abort state, staged resolutions, metadata and guarded removal. Keep the hook methods real. |
| S5 | AgentTaskDispatchBaseGuardTests and its SiblingWarnings partial. | Highest repeated dispatch cost: 25 rows/1,304 s observed case sum; one old 20-tip guard probe cost 59.3 s. Thread one backend through dispatcher, graph helper, source inspection and observers; keep DB and warning persistence/delivery assertions. |
| S6 | WorktreeStartRefDispatchTests. | Builds on S5's proven composition; explicit-ref selection, pin identity, refusal and cancellation remain unchanged. No live runner or external Git network. Historical class wall is unmeasured. |
| S7 | Landing adapter/fixture seam and only AgentTaskLandRemovalMatrixTests.C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate (20 cases). Add FakeGitLandingContractTests (8). | A bounded authority/refusal matrix is the safest landing entry. Add remote/ref CAS, fetch/push, independent observer clone, publication refs and required registration behavior. Retain both other removal methods as real. Do not model binary index bytes or submodules. |
| S8 | AgentTaskLandAdmissionTests, then AgentTaskLandConcurrencyTests in the same committed group. | Approx. 20m32s and 13m02s historical selections; state/policy matrices have better migration safety than destructive cleanup. Preserve production leases and TaskCompletionSource barriers and prove both arrival orders. Backend does not replace the concurrency mechanism. |
| S9 | AgentTaskLandBoundaryTests, except native rebase-configuration capstone. | Largest historical selection, 44m18s, but ref/index correctness has higher risk; wait for S7-S8 proof. Model expected-old update-ref, commit-tree, fresh source/target observations and recovery-pin failures. Never turn a Git observation failure into absence. |
| S10 | AgentTaskLandCheckpointMatrixTests, except C665 worker-death method. | Historical 25m13s; preserve checkpoint transaction cuts, committed/uncommitted recovery and coordinates. Same fault hooks must demonstrably fire in both modes. Real abrupt worker death/registration removal remains native. |
| S11 | AgentTaskLandCleanupSafetyTests. | Historical 18m57s; cleanup's irreversible nature makes it last despite its cost. Preserve remote rechecks at each deletion, residue/content preservation, missing-component receipts and purpose/namespace checks. Independent real observer assertions remain in the real run. |
| S12 | Six shared capstone scenarios, explicit real rerun recipe, evidence summary and supported-command documentation. | Keep opt-in parity practical for Git upgrades or suspected mismatches. Leave Slow/Integration classifications and existing process limiters in place; reclassification requires a separate measured decision. |

This ranking is qualitative **saving opportunity × migration safety**, not a fabricated numeric score. S3-S6 build the command coverage and wiring needed by the five expensive matrices. Among landing candidates, bounded authority and admission/lease policy precede boundary/recovery/deletion semantics. Refresh the ranking from measured class wall and native exceptions after each slice; an unexpected new native dependency pauses that slice instead of widening FakeGit silently.

For S3-S11, first fake then real rows use the same committed code and exact filter. The real row's evidence-completion hook compares the matching fake case snapshots at the same SHA/group before succeeding. A mismatch stops the next slice. Report raw qualification wall, diagnostic-observer time, ordinary-work elapsed intervals excluding those extra probes, and process counts separately; never claim the cost of parity-only probes as migration savings. If ordinary work has no useful saving, retain the class's real default and return a narrowed plan rather than claim migration success. These single-pair results are provisional, separate from the replicated pilot gate.

## Pilot measurement gate

CP-3/CP-4 are full-observation parity/warm-up rows, excluded from performance statistics. CP-5 through CP-10 are three measured pairs in order real/fake, fake/real, real/fake, all at the same clean commit, host, Git version, runtime, test roster and output build. Build output is reused; no edit/rebuild between the paired rows. Serialize rows and test cases in both modes. Real and fake fixture setup, existing assertions and cleanup remain inside the measured work.

Record:

- No-build checkpoint-row wall from the runner's per-row Seconds, plus its separate build and slot-wait fields. The current runner includes acquisition and result parsing in row Seconds; label this row wall, not process-only wall. Measured samples require no recorded slot wait; a queued row is invalid and is not repaired by subtracting rounded seconds.
- GitServiceTests class elapsed time including ordinary fixture setup/cleanup, plus the six original integration cases and four new error/idempotency cases separately. Do not substitute a TRX sum for class wall.
- Per-case TRX durations/outcomes; command counts split into setup, system-under-test, assertions and diagnostic observers; actual CLI starts; backend/commit/host/OS/Git/runtime versions; sample/order and warm-up flag.
- Evidence path and rerun reason. A retried selected row is not automatically a flaky-test retry.

Full parity observers can issue extra diagnostic Git reads. **Disable those additional probes in the six measured rows** using `ANTIPHON_TEST_GIT_OBSERVATIONS=minimal`; keep all actual test assertions and lightweight timing/command recording. This avoids counting instrumentation-only Git commands as a speedup. Full-observation correctness is already proven by CP-3/CP-4. Native exceptions are absent from the pilot.

Store immutable records beneath `.antiphon/fakegit/<source-sha>/<group>/<sample>/<backend>/<attempt-id>/`, alongside pointers to the checkpoint report/TRX and normalized snapshots. Include schema version, class/case ID, backend, host, run/row, outcome and rerun reason. The offline comparer reads those artifacts and the selected run's persisted completed-row state/TRX (the enclosing final report does not yet exist at CP-11), writes a gate JSON/Markdown receipt, and never builds or reruns tests. Bind the receipt to that exact checkpoint run; do not use an unrelated latest report. Require an unambiguous explicit attempt mapping; missing, mixed-SHA, duplicated or silently replaced samples are an error.

Keep a qualification index naming the exact commit, run, row attempts and artifact hashes for each group. Carry the immutable evidence bundle into subsequent slice worktrees; do not assume an earlier worktree's ignored files will survive retirement. Commit a concise results report under docs/investigations after each measurement, linking that group's raw evidence and preserving its measured source SHA. Result-only documentation commits do not require another test run. CP-35 validates the explicit index across the different slice SHAs and links CP-11's accepted pilot receipt; it must not demand that every slice was measured at the final SHA.

Default go/no-go policy (chosen for this plan; not an investigation finding):

1. **Correctness:** all 16 cases in each mode pass without skips; CP-3/CP-4 snapshots and rosters match; no unsupported shape/real-Git escape in fake mode; production-adapter/privacy regressions pass.
2. **Savings:** fake median class wall is at least 20% and 0.5 seconds lower than real, **and** fake median no-build row wall is at least 5% lower. At least two of three paired row-wall deltas are positive. Report all six samples and ranges; do not choose the best run.
3. **Validity:** no shared-host interference or hidden retry invalidates a sample; fake CLI-start count is zero and real is nonzero. Require the largest fake measured row wall to be below the smallest real row wall; overlapping observed ranges are inconclusive. Three pairs are a practical decision rule, not a statistical confidence claim.

CP-11 fails closed with one of `parity-failed`, `no-useful-gain`, `inconclusive`, `invalid-evidence`, or `pass`. On anything except pass, stop before S3 and return the receipts with `next: decide` (or repair the concrete parity defect within S1-S2). A quiet-host repeat requires an explicitly recorded repeat of the same pilot rows; a changed threshold or scope requires a plan revision before proceeding. No full-suite percentage is extrapolated from this pilot.

## Retained real-Git coverage

The common factory switch is the same-test-body opt-in. It must stay usable for every migrated filter, including GitServiceTests. Ordinary Code runs each migration once in both modes; subsequent ordinary runs default to fake only for admitted cases.

S12 adds `FakeGitCapstoneTests` with six parameterless scenarios, sharing the same scenario functions/assertions across backend choices: artifact/tag/merge topology; linked-worktree occupancy and reattachment; conflicting rebase followed by abort with refs/index/tree preserved; publication with an independently fetched remote observer; rejected cleanup retaining changed content; and real lease exclusion in both call orders. The default run is fake; the exact same six tests with `ANTIPHON_TEST_REAL_GIT=1` form the small explicit real parity lane. No duplicate watered-down real assertions.

Keep this concrete native floor in addition to that small opt-in lane:

| Existing coverage | Retention rule |
|---|---|
| GitDiffSpikeTests (5 cases) | Entirely real, including its timing assertion. Pilot regression CP-2 includes it. |
| DelegationWorktreeTests.failed_creation_preserves_unknown_hook_content; a_failed_worktree_add_leaves_no_registration_branch_or_directory; land_push_rejection_keeps_the_source_branch_and_worktree | Real fixtures in both class selections (4 executions across the three methods). Preserve post-checkout/pre-receive hooks and timeout cleanup. Application deny-hook JSON formatting alone does not force a real Git backend. |
| AgentTaskLandRemovalMatrixTests.C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries; C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent | Entire methods remain real: 24 plus 6 argument cases, including index-byte equality, submodules and Windows file handles. CP-22 explicitly qualifies them after S7. |
| AgentTaskLandBoundaryTests.C448_V32_RebaseConfigurationCannotStashOrRewriteUnrelatedRefs | Real in both class selections; preserve actual autoStash/updateRefs semantics. |
| AgentTaskLandCheckpointMatrixTests.C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop | Both argument cases stay real in both selections; no in-memory substitution for the child process. |
| Existing GitIndexLockTests, RepositoryMutationLeaseTests, RepositoryChildJournalInspectorTests and OS path/link/native cleanup suites | Stay outside migration with real/native fixtures and existing platform gates. Their index locks, child identity, junction/symlink and Windows behavior are not claims FakeGit makes. |

A full-class fake selection with declared native cases is a **mixed** row. Label each case's backend and report its native subtotal; zero CLI starts is required for migrated fake cases, not for the entire mixed row. Native cases are never silently omitted or converted to skip. Preserve existing platform skips and report them separately. If additional native cases are discovered, amend the named floor and checkpoint roster before conversion.

Git version upgrades or suspected mismatches: rerun the same pilot and affected-slice pairs and CP-33/CP-34 capstones on one host/SHA, with real mode explicit. Use `--rows` for the closed selection, including each referenced build row. For broad semantic upgrades, run all admitted pairs, splitting Boundary/recovery selections only via a committed table revision if needed. Real-only native tests remain in their existing lanes. Windows path/handle parity requires Windows evidence; Linux cannot qualify it.

## Verification design

### Assertions and regression obligations

| ID | Evidence required |
|---|---|
| V-1 | Executor selection/defaults, token fidelity, exit/stdout/stderr, timeout/cancellation, no fake CLI escape, unsupported-command atomic refusal. Six executor tests plus eight pilot model contracts. |
| V-2 | All original GitServiceTests assertions and four new error/idempotency cases pass unchanged across backend choice; full normalized observations agree. |
| V-3 | Three valid paired samples, independent class/row wall, process counts and machine-readable pilot gate. Six evidence tests cover mismatch, missing case, mixed SHA, normalization alias collision, instrumentation exclusion, and no-gain/inconclusive decisions. |
| V-4 | Worktree branch/ref/object typing, registrations/occupancy, common-dir identity, staged/working separation, conflict/abort and metadata preservation. Eight new Worktree contract scenarios; existing base/delegation assertions remain. |
| V-5 | Dispatcher base/ref selection, source provenance, warning persistence, explicit-ref refusal and consistent backend graph. |
| V-6 | Landing command model and authority gate: ref CAS, rejected push, unknown-vs-absent, remote independence, conflict preservation, observer fault independence, RunOwned callback ordering, fresh registration observation (eight Contract_ methods). All 20 removal authority cases retained. |
| V-7 | Admission/concurrency uses production leases and actual barriers, both arrival orders; no mutation before admission. |
| V-8 | Boundary checks still stop dependent mutations on source/target/pin/sequencer changes. |
| V-9 | Recovery acknowledges only committed checkpoints; wrong coordinates cannot authorize later work. Native worker-death cases preserved. |
| V-10 | Every cleanup revalidates its authority/remote/content evidence and retains unknown or changed work. |
| V-11 | Six capstones, complete per-class backend roster, real opt-in instructions, per-slice wall/process results and no unsupported migration hidden by a native fallback. |
| R-1 | GitDiffSpikeTests and the existing whole-index privacy regression retain real production behavior. |
| R-2 | Constructor/DI compatibility, one DelegationTestServices graph, prior TryAdd overrides, existing ControlledLandingGit contract when touched. |
| R-3 | Named native floor and all current process limiters/Slow classifications preserved; no real PostgreSQL/OS semantics replaced. |

Next stage is separate TestDesign. It should bind discriminating positive controls to production lines (e.g. omit the trailer, turn nonzero tag probe into absence, alias two commits, permit duplicate branch mutation, bypass lease acquisition, suppress a remote recheck), specify expected assertion failures and restore proof, and confirm method/argument rosters. A build failure or unsupported fake command is not the positive-control verdict. Mutation-stage runs belong to their own manifest; they are not extra unlisted Code checkpoints. No E2E or namespace-wide suite is required for these seams.

### Execution and evidence rules

The table below is the closed ordinary Code/Review list. S1-S2 are one committed group; S3 through S12 each have their own run. The runner does not understand the performance gate dependency: the dispatcher must not select any S3+ rows until CP-11's receipt is pass. Do not launch the entire manifest without `--after`/`--rows`. Within each group all rows are serial, so later paired real rows can compare the preceding fake output. In final-graph, comparison applies to switchable GitServiceTests; the unchanged support/native classes retain their own explicit backend and are separately reported. Reused builds always have the same After value and must run in the same checkpoint run, before successful-run output cleanup.

Use the checkpoint tool through the build-slot wrapper for its launcher, then its own leased row drivers, for example:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label fakegit-pilot -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-28-fakegit-gitservicetests-pilot-plan.md --after S1-S2 --max-wait 50s
# If exit 75, keep waiting on the returned run id; do not start another run.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label fakegit-wait -- dotnet run --no-build --project tools/Antiphon.Checkpoints -- wait <run-id> --max-wait 50s
```

This tool bootstrap is the declared infrastructure build outside the row builds; report it once. Thereafter use the already built launcher. Do not nest another slot wrapper around row commands; the checkpoint runner leases them. Report exit 4 as a slot timeout, never bypass it. Follow the runner until exit is not 75. No `dotnet test`, ad hoc OutputPath, production runner launch, or concurrent PTY assembly. Respect default Linux UseAppHost handling; a child-process capstone without a usable Linux apphost needs its supported Windows lane, not a skip invented by FakeGit.

Each CP line includes actual executed/passed/failed/skipped counts, commit, build/reuse, filter, TRX and reruns. Supplement with backend/group/sample evidence and gate receipts. Existing static rosters at the pinned source are conservative floors, not discovery evidence: GitService 12 (becomes 16), BaseSelection 16, Delegation 39, DispatchGuard 74, StartRef 30, Removal 50 (20 migrated/30 native), Admission 8, Concurrency 14, Boundary 38, CheckpointMatrix 43, CleanupSafety 27. Historical profile counts differ. TestDesign must verify parameter data sources/platform behavior and amend floors before Code when needed; never copy the older 117/24/28 numbers into Min.

New contract floors count separately declared TUnit methods, not assertions. Do not reduce existing assertion or argument coverage to meet a floor. CheckpointMatrix requires all 43 executions: its two worker-death cases start the test assembly through dotnet and have no source-level platform skip. Removal-native requires 29 executed on Linux and all 30 on Windows; its windows-file-handle argument explicitly skips off Windows. The table uses 29 as the cross-platform minimum and still requires the expected platform roster. The final graph floor is 127: 16 pilot, 5 DI, 51 ControlledLandingGit and 55 LandingGit argument-expanded cases.

The row-only `ANTIPHON_TEST_GIT_GROUP`, `...SAMPLE`, `...OBSERVATIONS` and `...COMPARE` are test-evidence controls implemented in S2. An unset sample means one qualification sample. `COMPARE=1` on later real rows requires the same-SHA/group fake artifacts and fails the row on drift. An unset REAL_GIT variable is fake; table rows pin 0/1 to exclude caller environment accidents.

### Cost

Estimates are planning budgets including isolated builds where named, excluding authoring and unpredictable slot waits; they are not measured savings. The ordinary floor is **334 minutes** if all gates pass and all slices proceed; **27 minutes** is the pilot-only checkpoint budget. The later 307 minutes remain conditional. These totals are the sum of EstimatedMinutes below. Windows/native rows need host-specific qualification; do not present a Linux-only report as Windows coverage. The broad landing pairs are a one-time migration investment, not the proposed everyday lane. Rows with estimates above 15 minutes can hit the tool's 45-minute row ceiling; if current measured cost will exceed it, TestDesign must split that named class by method into a revised closed table before dispatch.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-fakegit-pilot/` | pilot-contracts | `/*/*/(GitCommandExecutorTests*)\|(FakeGitPilotContractTests*)\|(FakeGitEvidenceTests*)/*` | V-1, V-3 | all listed, 0 failed | 20 | 6 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot-contracts` |
| CP-2 | S1-S2 | CP-1 | pilot-real-regressions | `/*/*/(GitDiffSpikeTests*)\|(CardFileBoardLookupRepairTests*)/(PathFilteredDiff*)\|(Repair2_Whole_index_commit_omits_staged_opted_out_cards*)` | R-1 | all listed, 0 failed | 6 | 4 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot-regressions` |
| CP-3 | S1-S2 | CP-1 | pilot-parity-fake | `/*/*/GitServiceTests/*` | V-2 | all 16, 0 failed/skipped | 16 | 2 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot;ANTIPHON_TEST_GIT_SAMPLE=warmup;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-4 | S1-S2 | CP-1 | pilot-parity-real | `/*/*/GitServiceTests/*` | V-2 | all 16, 0 failed/skipped | 16 | 2 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot;ANTIPHON_TEST_GIT_SAMPLE=warmup;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-5 | S1-S2 | CP-1 | pilot-sample1-real | `/*/*/GitServiceTests/*` | V-2, V-3 | all 16, 0 failed/skipped | 16 | 2 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot;ANTIPHON_TEST_GIT_SAMPLE=1;ANTIPHON_TEST_GIT_OBSERVATIONS=minimal` |
| CP-6 | S1-S2 | CP-1 | pilot-sample1-fake | `/*/*/GitServiceTests/*` | V-2, V-3 | all 16, 0 failed/skipped | 16 | 2 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot;ANTIPHON_TEST_GIT_SAMPLE=1;ANTIPHON_TEST_GIT_OBSERVATIONS=minimal` |
| CP-7 | S1-S2 | CP-1 | pilot-sample2-fake | `/*/*/GitServiceTests/*` | V-2, V-3 | all 16, 0 failed/skipped | 16 | 2 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot;ANTIPHON_TEST_GIT_SAMPLE=2;ANTIPHON_TEST_GIT_OBSERVATIONS=minimal` |
| CP-8 | S1-S2 | CP-1 | pilot-sample2-real | `/*/*/GitServiceTests/*` | V-2, V-3 | all 16, 0 failed/skipped | 16 | 2 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot;ANTIPHON_TEST_GIT_SAMPLE=2;ANTIPHON_TEST_GIT_OBSERVATIONS=minimal` |
| CP-9 | S1-S2 | CP-1 | pilot-sample3-real | `/*/*/GitServiceTests/*` | V-2, V-3 | all 16, 0 failed/skipped | 16 | 2 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot;ANTIPHON_TEST_GIT_SAMPLE=3;ANTIPHON_TEST_GIT_OBSERVATIONS=minimal` |
| CP-10 | S1-S2 | CP-1 | pilot-sample3-fake | `/*/*/GitServiceTests/*` | V-2, V-3 | all 16, 0 failed/skipped | 16 | 2 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=pilot;ANTIPHON_TEST_GIT_SAMPLE=3;ANTIPHON_TEST_GIT_OBSERVATIONS=minimal` |
| CP-11 | S1-S2 | n/a | pilot-gain-gate | `pwsh -NoProfile -File scripts/compare-fakegit-evidence.ps1 -Mode Pilot -ResultsRoot .antiphon/fakegit -Group pilot` | V-2, V-3 | exit 0; pass receipt with 2 full-parity and 6 measured rosters | n/a | 1 | true | n/a |
| CP-12 | S3 | `tests/Antiphon.Tests -> bin-fakegit-worktree-base/` | worktree-base-fake | `/*/*/(WorktreeBaseSelectionTests*)\|(FakeGitWorktreeContractTests*)\|(DelegationTestServicesTests*)/*` | V-4, R-2 | all listed, 0 failed | 29 | 6 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=worktree-base;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-13 | S3 | CP-12 | worktree-base-real | `/*/*/(WorktreeBaseSelectionTests*)\|(FakeGitWorktreeContractTests*)\|(DelegationTestServicesTests*)/*` | V-4, R-2 | all listed, 0 failed | 29 | 4 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=worktree-base;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-14 | S4 | `tests/Antiphon.Tests -> bin-fakegit-delegation-worktree/` | delegation-worktree-fake | `/*/*/DelegationWorktreeTests/*` | V-4, R-2, R-3 | all listed, 0 failed | 39 | 8 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=delegation-worktree;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-15 | S4 | CP-14 | delegation-worktree-real | `/*/*/DelegationWorktreeTests/*` | V-4, R-2, R-3 | all listed, 0 failed | 39 | 6 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=delegation-worktree;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-16 | S5 | `tests/Antiphon.Tests -> bin-fakegit-dispatch-base/` | dispatch-base-fake | `/*/*/AgentTaskDispatchBaseGuardTests/*` | V-5, R-2 | all listed, 0 failed | 74 | 8 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=dispatch-base;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-17 | S5 | CP-16 | dispatch-base-real | `/*/*/AgentTaskDispatchBaseGuardTests/*` | V-5, R-2 | all listed, 0 failed | 74 | 6 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=dispatch-base;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-18 | S6 | `tests/Antiphon.Tests -> bin-fakegit-start-ref/` | start-ref-fake | `/*/*/WorktreeStartRefDispatchTests/*` | V-5, R-2 | all listed, 0 failed | 30 | 6 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=start-ref;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-19 | S6 | CP-18 | start-ref-real | `/*/*/WorktreeStartRefDispatchTests/*` | V-5, R-2 | all listed, 0 failed | 30 | 4 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=start-ref;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-20 | S7 | `tests/Antiphon.Tests -> bin-fakegit-removal-authority/` | removal-authority-fake | `/*/*/(AgentTaskLandRemovalMatrixTests*)\|(FakeGitLandingContractTests*)/(C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate*)\|(Contract_*)` | V-6, R-2 | all listed, 0 failed | 28 | 10 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=removal-authority;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-21 | S7 | CP-20 | removal-authority-real | `/*/*/(AgentTaskLandRemovalMatrixTests*)\|(FakeGitLandingContractTests*)/(C448_V36_DirectRemovalRequiresEveryAuthorityCoordinate*)\|(Contract_*)` | V-6, R-2 | all listed, 0 failed | 28 | 15 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=removal-authority;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-22 | S7 | CP-20 | removal-native | `/*/*/AgentTaskLandRemovalMatrixTests/(C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries*)\|(C448_V20_LastRemovalBoundaryPreservesEveryRemainingComponent*)` | R-3 | 29 Linux / 30 Windows; exactly the existing Windows-handle skip off Windows | 29 | 25 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=removal-native` |
| CP-23 | S8 | `tests/Antiphon.Tests -> bin-fakegit-admission-concurrency/` | admission-concurrency-fake | `/*/*/(AgentTaskLandAdmissionTests*)\|(AgentTaskLandConcurrencyTests*)/*` | V-7, R-3 | all listed, 0 failed | 22 | 12 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=admission-concurrency;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-24 | S8 | CP-23 | admission-concurrency-real | `/*/*/(AgentTaskLandAdmissionTests*)\|(AgentTaskLandConcurrencyTests*)/*` | V-7, R-3 | all listed, 0 failed | 22 | 35 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=admission-concurrency;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-25 | S9 | `tests/Antiphon.Tests -> bin-fakegit-land-boundary/` | land-boundary-fake | `/*/*/AgentTaskLandBoundaryTests/*` | V-8, R-3 | all listed, 0 failed | 38 | 12 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=land-boundary;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-26 | S9 | CP-25 | land-boundary-real | `/*/*/AgentTaskLandBoundaryTests/*` | V-8, R-3 | all listed, 0 failed | 38 | 35 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=land-boundary;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-27 | S10 | `tests/Antiphon.Tests -> bin-fakegit-land-checkpoints/` | land-checkpoints-fake | `/*/*/AgentTaskLandCheckpointMatrixTests/*` | V-9, R-3 | all listed, 0 failed | 43 | 12 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=land-checkpoints;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-28 | S10 | CP-27 | land-checkpoints-real | `/*/*/AgentTaskLandCheckpointMatrixTests/*` | V-9, R-3 | all listed, 0 failed | 43 | 30 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=land-checkpoints;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-29 | S11 | `tests/Antiphon.Tests -> bin-fakegit-land-cleanup/` | land-cleanup-fake | `/*/*/AgentTaskLandCleanupSafetyTests/*` | V-10, R-3 | all listed, 0 failed | 27 | 10 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=land-cleanup;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-30 | S11 | CP-29 | land-cleanup-real | `/*/*/AgentTaskLandCleanupSafetyTests/*` | V-10, R-3 | all listed, 0 failed | 27 | 25 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=land-cleanup;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-31 | S12 | `tests/Antiphon.Tests -> bin-fakegit-final-graph/` | final-graph-fake | `/*/*/(GitServiceTests*)\|(DelegationTestServicesTests*)\|(ControlledLandingGitTests*)\|(LandingGitTests*)/*` | V-2, R-2, R-3 | all listed, 0 failed | 127 | 12 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=final-graph;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-32 | S12 | CP-31 | final-graph-real | `/*/*/(GitServiceTests*)\|(DelegationTestServicesTests*)\|(ControlledLandingGitTests*)\|(LandingGitTests*)/*` | V-2, R-2, R-3 | all listed, 0 failed | 127 | 10 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=final-graph;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-33 | S12 | CP-31 | capstones-fake | `/*/*/FakeGitCapstoneTests/*` | V-11 | all 6, 0 failed/skipped | 6 | 5 | true | `ANTIPHON_TEST_REAL_GIT=0;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=capstones;ANTIPHON_TEST_GIT_OBSERVATIONS=full` |
| CP-34 | S12 | CP-31 | capstones-real | `/*/*/FakeGitCapstoneTests/*` | V-11, R-3 | all 6, 0 failed/skipped | 6 | 10 | true | `ANTIPHON_TEST_REAL_GIT=1;TUNIT_MAX_PARALLEL_TESTS=1;ANTIPHON_TEST_GIT_GROUP=capstones;ANTIPHON_TEST_GIT_OBSERVATIONS=full;ANTIPHON_TEST_GIT_COMPARE=1` |
| CP-35 | S12 | n/a | migration-evidence-summary | `pwsh -NoProfile -File scripts/compare-fakegit-evidence.ps1 -Mode MigrationSummary -ResultsRoot .antiphon/fakegit` | V-3, V-11, R-3 | exit 0; pilot pass linked; all admitted groups paired; native roster and timings complete | n/a | 1 | true | n/a |
