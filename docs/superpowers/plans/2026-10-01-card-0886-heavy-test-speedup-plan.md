# CARD-0886: reuse the two heavy CARD-0835 test fixtures

Date: 2026-10-01. Stage: Plan complete; TestDesign follows. Baseline: `origin/master` at **`4cdd8809b85ed479a8a2b3d43f80e3cd7577bf6e`**, confirmed by `git ls-remote origin refs/heads/master` at 21:42 UTC. Assigned branch: `feat/card-task-a3c7520f`, starting at that same commit. This stage changes only this file. The branch remains fast-forward-only; no rebase, reset or force-push.

## Outcome and scope

Reduce repeated database/Git setup in `CheckpointSourceApprovalTests.recovery_and_resume_recheck_source_assertion` and repeated Git/PowerShell startup in `RunCheckpointSourceScriptTests.C835_DriftAndReuse`. Keep the real application decisions, durable PostgreSQL observations, real Git recovery/cleanup cases, real checkpoint script and all existing assertions. Share expensive family resources inside one test invocation; use new service/fault/queue state at every case boundary. Run cases sequentially.

The implementation is test infrastructure only. Receipt formats, validators, source fingerprints, dirty-tree admission, production build-slot acquisition/release, landing approval, production cleanup and the checkpoint CLI stay unchanged. CARD-0885 owns supported repeat mode, phase timing fields and brief caps. There is no provider launch, live landing, deployment, migration or bundle edit in this card.

The performance gates are independent of correctness: each heavy method must take at most 50% of its paired baseline in idle and 24-burner conditions; the original 23-method subtotal must reach at most 138.6 seconds idle and at most 60% of its paired loaded baseline. Do not declare success merely from fewer fixture allocations.

**Halving both methods is insufficient for the round target.** Historical arithmetic is `76.6 + 48.9 = 125.5 s`; the remaining tests cost `231 - 125.5 = 105.5 s`. Halving the pair gives `168.25 s`, or 72.8% of the round. If other costs stay flat, the pair needs a combined budget of **33.1 s**. Use provisional engineering budgets of 12 s landing and 20 s script, not promises of observed performance. Missing these budgets triggers measurement and a design correction within this footprint, never fewer cases, weaker assertions or unapproved parallelism.

## Evidence and limits

Read the full card with `pwsh -NoProfile -File scripts/card.ps1 get CARD-0886 -Board Antiphon`, and CARD-0885, CARD-0788, CARD-0883 and every flake card listed below. Read the two requested templates from `origin/master`: `2026-10-01-card-0826-daily-host-cleanup-plan.md` and `2026-10-01-card-0866-disposal-preview-redaction-plan.md`. Owners consulted: `docs/project-context.md`, checkpoint/build-slot/filter/mutation/temp rules in `docs/testing-and-build.md`, and stage/collision rules in `docs/orchestration-loop.md` section 1. This is a delegate, not a board orchestrator; no agents were dispatched.

Evidence collection was read-only: `git status --short`, `git rev-parse`, `git show`, `git diff --name-only`, `git ls-remote`, `rg`, bounded source reads and a Node text census. No builds, tests, burners or provider processes ran. Executed results in Plan: **Linux 0; Windows 0**. The desktop checkout is inaccessible from this runner.

| Observation | Source-derived measurement or provenance | Limit / implication |
|---|---|---|
| Historical heavy landing | Card: 76.6 s idle, 154.6 s with 24 burners; failed task `c5d2ae3a` | Historical, card-supplied TRX measurements, not a new timing or the final landed case census. |
| Historical heavy script | Card: 48.9 s idle; about ten invocations | Loaded script time was not supplied. Do not invent it. |
| Historical round | CARD-0886: 23 tests, 231 s test time; CARD-0885: 327 s total = about 93.5 startup + 231 tests + 3 teardown; loaded 472 total / 343 tests / 127 startup | Rounded historical figures. Startup savings belong to CARD-0885, not this card's test-time claim. |
| Landed landing matrix | `CheckpointSourceApprovalTests.cs:87`: ordinary `2 latches × 4 cuts × 3 assertions = 24`; recovery `2 modes × 2 timings × 3 assertions = 12`; published cleanup `3 modes × 2 assertions = 6` | **42 internal cases**, not the historical 36; still one TUnit execution. Preserve all 42. |
| Landed script matrix | `RunCheckpointSourceScriptTests.cs:93`: **8 Fixture allocations, 14 RunAsync calls**, 35 lexical Shouldly assertion sites | Corrects the approximate card count. One TUnit execution. Fourteen calls are retained. |
| Landing assertion inventory | Target method contains **37 lexical Shouldly assertion sites**, including exception assertions | Sites are an audit aid, not executed assertion or TUnit counts. Helpers and branch multiplicity need semantic comparison. |
| Current C835 category | `CheckpointSourceStateTests` 8 + script 5 + execution 6 + approval 4 + parser C835 methods 2 + delegate C835 method 1 = **26 results** | Original V-1..V-23 plus two hidden-index methods and `git_fixture_cleanup_removes_read_only_contents`. Report both the 23 subtotal and all 26; never silently call this a 23-test TRX. |
| Landing setup | Both harness `InitializeAsync` methods call `TestDbFixture.CreateIsolatedSchemaAsync`; the latter clones a migrated PostgreSQL database, not a search-path schema | The target currently creates/drops 42 databases. Service recreation alone does not reset persisted state. |
| Native landing setup | 18 cases use `LandingSafetyHarness` / `LandingGitFixture`; fixture initialization issues 10 Git commands including an independent observer clone. Ordinary 24 use `ControlledLandingGit` | Retain native Git for the existing 18 cases. Avoid replacing recovery/adoption with a new permissive fake. |
| Script process floor | 14 outer `pwsh -File run-checkpoint.ps1` processes; source control flow also starts 17 shim PowerShell children (build/run phases) on the green path | Sharing only the Git fixture does not remove most process startup. Count is source-derived, not an OS trace; retain nested shim children in the first implementation. |
| Reset hazards | `LandingProtocolHarness.SaveFault` has private armed/triggered state; both harnesses replace the queue on service restart; native fixtures retain refs, registrations, traces and callbacks; script fixtures retain `_round`, call files and ignored stamps | Clearing only a trace or resetting HEAD is insufficient. Prefer fresh case objects and complete resource images. |
| Baseline seeded data | `AppDbContext` seeds an admin user; migrations can seed other tables | Never assume an isolated migrated database is entirely empty or truncate it without restoring its baseline data. |

Source reproduction commands (no execution of tests):

```sh
git rev-parse origin/master
git ls-remote origin refs/heads/master
rg -n 'recovery_and_resume_recheck_source_assertion|C835_DriftAndReuse' tests/Antiphon.Tests
rg -n 'Category\("C835"\)' tests/Antiphon.Tests
rg -n 'CreateIsolatedSchemaAsync|InitializeAsync|RestartServicesAsync|DisposeAsync' tests/Antiphon.Tests/TestHelpers/LandingProtocolHarness.cs tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs
git show 4cdd8809b85ed479a8a2b3d43f80e3cd7577bf6e:docs/superpowers/reviews/2026-10-01-card-0835-validator-guard-coverage.md
```

No fresh TRX from the failed historical attempt was available in this checkout. No source inspection proves that the proposed reuse meets the timing target, or that a PowerShell runspace has identical exit/stream behavior to `pwsh -File`. Those are explicit implementation qualifications below.

## Design decisions

### D-1: six landing families, with an owned store and fresh case state

Introduce a test-local `CheckpointSourceApprovalFamily` owner. Families are ordinary (24 cases), self recovery (6), adoption (6), and published cleanup in ordinary/self/adoption modes (2 each). Each family creates **one** isolated database; the five native families each initialize **one** real Git fixture. The ordinary family constructs a fresh inexpensive `ControlledLandingGit` for each case. This reduces expensive database clones from 42 to 6 and native initializations from 18 to 5 without changing the 42-case matrix.

A family produces disposable case views built from the existing harness composition. Add explicit borrowed-resource overloads to `LandingProtocolHarness` and `LandingSafetyHarness`, following `C544World`'s existing `_ownsSchema` distinction. Existing constructors and no-argument InitializeAsync retain their ownership and behavior. A borrowed case never drops the family database or deletes its native fixture. It always owns/disposes its service provider, contexts, queue, verifier, fault interceptor and callback state. Family disposal joins the last case before releasing the database and filesystem.

Between cases, dispose all case services/contexts, reset the private database to its captured **pre-case data image**, restore the private Git image, then construct and seed a new case view. Do not reuse tracked EF entities, evidence/request/operation IDs, queue claims, verifier counters, fault objects or clock instances. Inside a single resume case, keep the existing `RestartServicesAsync` boundary without resetting data or Git: the restart must reread the exact persisted request and changed Review evidence.

Database reset is a concrete test-only operation against the exact `IsolatedTestSchema` allocated by this family. Capture baseline rows before seeding the owner; preserve migrations and seeded data. Reset the application table dependency closure in one transaction, restoring captured seed rows; validate the resulting table counts/identities before reseeding. Quote identifiers from trusted EF/database metadata. Refuse shared `antiphon_test`, `antiphon_tmpl`, mismatched connections and any database not held by this owner. No global Npgsql pool clearing, migration replay, disabled constraints or shared-store reset. TestDesign must freeze the table closure and seed restoration algorithm; an unexpected new table/seed fails reset qualification instead of being silently ignored.

Use `FakeTimeProvider` (already referenced by the test project), pinned to an explicit instant, for new case service clocks and evidence seed times. Recreate it at each case boundary. The original C544Clock is a wall clock plus an offset, **not** a deterministic replacement; do not credit it as controlled time. Await actual transaction/service completion and barriers; never advance a fake clock to simulate a completed database or process operation.

Rejected: assembly-global shared fixtures; rollback transactions spanning production commits/fresh contexts; simply clearing ChangeTracker; one database per case; parallel matrix execution; a production DI/land rewrite; replacing the existing native cases with fake Git. Fresh service objects cost little and make reset substantially easier to prove than resetting every mutable interceptor field.

### D-2: restore small, fully owned Git images at the same absolute paths

Add `CheckpointSourceFixtureImage`, a test-only snapshot owner shared by the landing and script fixtures. Capture the tiny, initialized fixture's complete file/directory bytes and required attributes/modes, including its bare remote, canonical repository, linked-worktree admin files and observer. Restore to the **same absolute root**; moving a linked-worktree image would invalidate its `.git` and `gitdir` paths. Keep the image and receipts outside the mutable fixture subtree. Do not hard-link writable indexes, objects or working files between cases.

Before restore, require all owned Git/process tasks and redirected streams joined, all case contexts/provider disposed, and no live repository lease or unresolved child custody. A reset cannot erase uncertain child journals to make a later case pass. Use a new native `FixtureGit` recorder on the restored image so callbacks, command history and registration-drop lists cannot leak. Verify HEAD/source/target/remote refs, registrations, clean index/worktree, seed sentinel bytes and absent extra pins/journals before running a case. Restore deleted source worktrees after successful cleanup as part of this **fixture-owned** image, never through a production recovery override.

Confinement is based on the exact fixture owner/root, not an `antiphon-*` prefix alone. Reject links, foreign roots and external paths before reset; fail rather than follow them. Reuse `GitFixtureCleanup` for fixture disposal where applicable. Preserve failed-case evidence before reset. No sweep of host temp roots or production worktree metadata is introduced.

Rejected: just `git reset --hard` (does not restore remote refs, registrations, ignored stamps or child state); unscoped `git clean`/prune; copying a live Git tree; caching mutable fixture instances statically; weakening ignored-content cleanup to enable reuse.

### D-3: one script fixture and one PowerShell worker, with a new runspace per invocation

Extract the private script fixture into `CheckpointSourceScriptFixture`, retaining its current process mode for the other four C835 methods and for comparison. Only `C835_DriftAndReuse` opts into the reusable worker initially. Keep its eight **scenario blocks** and all fourteen real script invocations. Preserve the three initial successful-build assertions; do not synthesize a valid stamp or reuse a cached result in their place.

Create the source repository/shims once, capture its clean image, and reset at the eight original `new Fixture()` boundaries. Do not reset between the build/reuse/missing-stamp steps of one scenario, between tampered/dirty stamp steps, or between successful/failed rebuild/reuse: those sequences are the behavior under test. Each script call still receives a unique results directory and real `source.json`, `git.txt`, receipt and applicable TRX. Maintain a monotonic run ID outside resettable state. Call counters remain cumulative within a scenario and reset explicitly between scenarios.

For the process/worker comparison, set fixed `GIT_AUTHOR_DATE` and `GIT_COMMITTER_DATE` on fixture-owned child environments only, including initial seed commits and the head-drift shim. This makes identical Git operations from the same restored seed produce identical SHAs without normalizing away an identity mismatch. Do not modify the TUnit host environment. Source observation timestamps remain real diagnostic timestamps and are excluded only from cross-invocation equality, not from each receipt's validity checks.

Launch one private `pwsh -NoProfile -NonInteractive` worker for this method. In it, execute the **unmodified file** `scripts/run-checkpoint.ps1` in a new PowerShell runspace per request, using typed parameters/argument arrays, not generated shell strings. A test-only PSHost captures `SetShouldExit`, host/information/error streams and termination; success is not inferred merely from a completed pipeline. Preserve `exit 0/1/2/3/4` and distinguish an actual terminating invocation error from an ordinary script refusal. Implement the host in the worker using PowerShell's already-loaded automation assembly; do not add a PowerShell SDK package to the application/test project.

The worker is strictly serial. Set and restore the exact fixture environment and process working directory in `try/finally`; a fresh runspace alone does **not** isolate `$env:` or process CWD. Include every C835/C585/C589/C671 override used by the fixture, missing-value semantics, LASTEXITCODE, streams and PowerShell location in reset qualification. No environment changes occur in the TUnit host. Dispose each pipeline/runspace before answering its request. The worker does not replace source capture, validators, build binding, or `Invoke-Dotnet`; real nested shim child launches and Git observations remain.

Capture per-request exit, streams, input identity, result paths and child completion. Outer fixture timeout/cancellation must stop and join its exact worker/children and drain streams before releasing the root. Do not apply this reusable worker to terminal-interruption tests until their process semantics have their own proof. Preserve existing process limiter attributes; all new process-starting test classes get the same assembly-local `ParallelLimiter<ProcessSpawnLimit>`.

The process/runspace parity test below is a hard prerequisite. If the platform cannot host the real script's exit/host semantics faithfully, retain process mode and report the measured shortfall; do not count an emulated receipt as equivalent. TestDesign can simplify the worker only after proving the same boundaries, not by assuming nested script `exit` behaves like `pwsh -File`.

Rejected: parallel PowerShell cases; a persistent mutable runspace with a guessed variable cleanup list; regex rewriting `exit`; dot-sourcing a modified script; fake `source.json`/build stamps; moving script guards into a duplicate C# implementation; a production checkpoint repeat feature (CARD-0885).

### D-4: preserve assertions, names, execution counts and mutation witnesses

Keep both existing public test names and their single-result shape. Do not parameterize 42 landing cases into 42 TRX tests or merge existing test methods. The current 26-result C835 roster remains 26. Eight proposed C886 fixture tests are separate, explicitly counted results; they are excluded from the historical 23-method timing subtotal but included in ordinary verification cost.

Before refactoring, extract the target methods' complete assertions and labels from the pinned baseline. In the final diff map each original expression, expected value, branch and case key to its destination. Required starting audit totals are 37 landing and 35 script lexical assertion sites; matching totals alone is insufficient. All original exception, zero mutation/call, durable request/evidence, publication/cleanup, count, fingerprint and stamp assertions survive. New reset/parity assertions are additive.

Label the **first detecting assertion** as well as the later detailed assertion where an earlier unlabelled failure would mask the intended PC. In particular: script strict-admission exits/lease counts for PC-7, drift exit/state for PC-8, all strict stamp rejection exits for PC-9A, land admission exception for PC-22, and the first refused-state/ref/trace assertions for PC-23. Retain the original labels as aliases rather than replacing them. PC-23 starts with unlatched/false and independently witnesses each saved cut; a missing event's `SingleAsync` exception is not a named refusal assertion.

All 37 CARD-0835 mutation variants remain commissioned, including both hidden-index readers; the guard-coverage document is the source map. Its selected earlier Code reds do not substitute for re-proving this implementation. Keep unchanged production guards and validator coverage intact. Additional fixture controls below prove the new reset/isolation behavior, not new product policy.

Rejected: reducing matrices to representative product cases, accepting a timeout as a red, ignoring a now-missing assertion label, counting loops as executed tests, or claiming an old mutation receipt applies to the new helper.

### D-5: bounded, paired measurements with no production receipt extension

Record current source SHAs, OS/CPU/runtime/Git/pwsh versions, actual load, slot wait, build, test-host startup, per-method TRX durations and teardown separately in task-owned evidence. Use the existing receipt schemas unchanged. Strict checkpoint qualification continues to bind clean source and builds; inner synthetic fixture receipts remain diagnostic fixture output, never Review evidence for the working branch.

For each OS, take one before/after pair idle and one before/after pair under exactly 24 owned CPU burners. Use the same runtime, host, burner program, source selection, serial settings and cache policy; baseline precedes fixture changes and final follows them. No best-of timing, discarded slow run, hidden warmup, changed timeout or repeat-mode switch. Record both the sum of the original 23 method durations and the current 26 total. Read the TRX roster rather than relying on the category's name.

Keep at most three normal and two loaded measurement rounds per version, including a justified replacement sample; these are ceilings, not targets. One pair per condition is the default. A contaminated/incomplete sample remains visible and cannot establish acceptance. Any extra build/test command needs its reason and slot. Do not run Antiphon.Agents.Pty.Tests beside this work.

No new wall-clock success margin below two seconds. Tests use awaited outcomes and controlled clocks for state decisions; deadlines (at least the existing 90-second script diagnostic limit, with explicit cancellation/join) diagnose hangs and never supply a successful PC assertion. Benchmark stopwatches measure elapsed time; they are not used to decide production correctness.

Rejected: counting reduced host startup as test speed, benchmarking only a subset of V-21/V-8, using historical loaded script numbers that do not exist, routine repeat batteries, or changing CARD-0885 receipt fields here.

## Exact implementation footprint and slices

This Plan and TestDesign change only this Markdown file. Future Code has the following closed footprint; no production, project/package, migration, checkpoint-cleanup or bundle file is edited.

| Slice | Exact files | Test-first work and exit |
|---|---|---|
| S0: baseline and assertion inventory | This plan; task-owned evidence outside tracked source | Freeze the 26-result/42-case/14-invocation census and assertion map. Commit/push any plan refinement. Run CP-1 idle and CP-2 loaded at the clean pre-optimization SHA on Linux and Windows. Retain complete receipts and 23/26 subtotals. |
| S1: characterizations and compiling seams | New `tests/Antiphon.Tests/Application/CheckpointSourceApprovalReuseTests.cs`, `tests/Antiphon.Tests/Scripts/CheckpointSourceScriptReuseTests.cs`, `tests/Antiphon.Tests/TestHelpers/CheckpointSourceApprovalFamily.cs`, `tests/Antiphon.Tests/TestHelpers/CheckpointSourceFixtureImage.cs`, `tests/Antiphon.Tests/Scripts/CheckpointSourceScriptFixture.cs`; modify the two target test files below to expose their exact case body to these fixtures | Author the eight detecting tests first. Provide compiling, non-reusing/no-op-reset seams as needed. Commit/push; CP-3's declared preparatory red must fail named reset/reuse assertions, not compilation, fixture setup or a hang. Do not call a self-comparison a red. |
| S2: resource reuse and witness preservation | Modify `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs`, `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs`, `tests/Antiphon.Tests/TestHelpers/LandingProtocolHarness.cs`, `tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs`, `tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs`; implement S1 helpers | Add borrowed ownership and reset/image mechanics, table-drive all original cases and use the worker only for V-8. Fresh service state per case, original restart semantics within a case. Commit/push and review the assertion diff; run final CP-3/4 after S3's static documentation finalization so all final receipts bind one SHA. |
| S3: qualification and evidence | This plan; `docs/superpowers/reviews/2026-10-01-card-0835-validator-guard-coverage.md` only if line/label links need updating | Commit/push completed implementation and accurate census/witness documentation; run CP-5 idle and CP-6 loaded at the same implementation source. Preserve timing evidence for the caller to record on CARD-0886. No speedup claim before both performance and correctness gates pass. |

The worker's PowerShell/PSHost source stays a literal test asset in `CheckpointSourceScriptFixture.cs`, emitted only into its exact owned external fixture root. No application bundle or public script is edited. `CheckpointSourceFixtureImage` must not depend on `tools/Antiphon.Checkpoints/Cleanup` or alter its ownership/sweep code. Existing `ControlledLandingGit`, `TestDbFixture`, `C544World`, production graph registrations and default test-host startup stay read-only.

Suggested Code scope is these eleven paths: the five new files, five modified test/helper files, and this plan; add the exact existing guard-coverage Markdown path for S3. Do not replace this with `tests/**`, `scripts/**`, `delegation` or a schema area. Every slice is committed and pushed before checks. Runtime measurement evidence stays outside tracked source. If a further tracked correction is necessary, commit/push normally and obtain strict final evidence at that reviewed SHA; never relabel the previously checked source.

### Same-source-area collisions

| Other card | Exact overlap / shared area | Code decision |
|---|---|---|
| CARD-0835 | Its two heavy tests and helpers are this input | Already landed at the pinned baseline. Recount after any later correction; no remaining land prerequisite from the original card. |
| CARD-0885 | It changes `scripts/run-checkpoint.ps1`, checkpoint execution/receipt tooling and their tests. This plan adds a caller of that exact script and changes its source-receipt fixture; no finalized sibling plan was present in this baseline | **Serialize Code in the checkpoint/script-fixture area.** Prefer 0886's before/after measurements with one fixed 0885 revision. If 0885 lands first, re-read its script/receipt/CLI and repeat the source census before implementation. Do not combine its startup savings with ours. Plans/TestDesign may proceed independently. |
| CARD-0788 / CARD-0883 | Supplied scope includes `LandApproval.cs`, `AgentTaskLandService.cs`, `AgentTaskLandSourceResolver.cs`; this card edits their shared landing test harnesses and recovery/adoption fixture | **Serialize Code in the landing source area**, even though those production files are read-only here. CARD-0788's broad application-test scope can also intersect the target directly. Consume whichever lands first; preserve actual request/recovery behavior and redo relevant counts. |
| CARD-0826 | Moves checkpoint cleanup code, adds AddHostCleanup/EF snapshot, changes `AgentTaskLandService.cs`, Program, PhoneHomeCommandDispatcher, deploy scripts and stage-code/stage-review. None is an exact edit here; it changes the landing composition exercised by these shared harnesses | **Serialize overlapping landing-harness Code with its land/maintenance integration.** Cleanup-only extraction is not itself a functional prerequisite, and there is no EF-migration, runner, deploy or bundle collision in our footprint. Do not adopt its unfinished cleanup helper as a dependency. |

These are source-area decisions, not a claim that a particular Code task is currently occupying a slot. The caller refreshes `/api/agent-tasks/pipeline`, `/api/session-runners`, `/api/runner-defaults`, `/api/hosts` and board-scoped active task scopes before Code; apply the lower effective stage/host cap, prefer server2 and defer collisions. Do not raise budgets or use an absolute-limit override to bypass a same-area occupant. Windows work is limited to the portability/performance rows here. The 30,000-character bundle guard has little shared headroom; no bundle edit is required. Any later separately justified bundle edit must be net-shorter.

## Verification design

### Existing roster and preserved scenario inventory

The original CARD-0835 V IDs remain stable. These are exact public method names; each contributes one TUnit result, with no argument expansion or OS skip in this selection.

| Original ID | Exact detecting method | Results |
|---|---|---:|
| V-1 | `CheckpointSourceStateTests.clean_and_ignored_outputs_match_head` | 1 |
| V-2 | `CheckpointSourceStateTests.index_and_worktree_edits_are_dirty` | 1 |
| V-3 | `CheckpointSourceStateTests.untracked_contents_and_paths_affect_identity` | 1 |
| V-4 | `CheckpointSourceStateTests.failed_or_unstable_capture_is_unknown` | 1 |
| V-5 | `CheckpointSourceStateTests.script_and_tool_snapshots_agree` | 1 |
| V-6 | `RunCheckpointSourceScriptTests.C835_DiagnosticReceipts` | 1 |
| V-7 | `RunCheckpointSourceScriptTests.C835_StrictAdmission` | 1 |
| V-8 | `RunCheckpointSourceScriptTests.C835_DriftAndReuse` | 1 |
| V-9 | `RunCheckpointSourceScriptTests.C835_TerminalEvidence` | 1 |
| V-10 | `RunCheckpointSourceScriptTests.C835_ReceiptValidation` | 1 |
| V-11 | `CheckpointSourceExecutionTests.clean_and_dirty_runs_publish_bound_source` | 1 |
| V-12 | `CheckpointSourceExecutionTests.changed_admission_and_driver_boundaries_refuse` | 1 |
| V-13 | `CheckpointSourceExecutionTests.strict_cli_and_reuse_require_clean_binding` | 1 |
| V-14 | `CheckpointSourceExecutionTests.terminal_paths_do_not_invent_clean_evidence` | 1 |
| V-15 | `CheckpointSourceExecutionTests.merge_cannot_launder_source_identity` | 1 |
| V-16 | `CheckpointSourceExecutionTests.validation_requires_complete_consistent_source` | 1 |
| V-17 | `ReviewEvidenceParserTests.C835_SourceCleanGrammar` | 1 |
| V-18 | `ReviewEvidenceParserTests.C835_SourceStateCannotBeInferred` | 1 |
| V-19 | `CheckpointSourceApprovalTests.settlement_persists_source_assertion` | 1 |
| V-20 | `CheckpointSourceApprovalTests.land_admission_requires_clean_review_source` | 1 |
| V-21 | `CheckpointSourceApprovalTests.recovery_and_resume_recheck_source_assertion` | 1 |
| V-22 | `CheckpointSourceApprovalTests.migration_and_override_preserve_unknown` | 1 |
| V-23 | `DelegateScriptLandApprovalTests.C835_FindingSourceCleanIsExplicit` | 1 |

The three additional C835-category results are `CheckpointSourceStateTests.assume_unchanged_edit_is_unknown_in_both_readers`, `.skip_worktree_edit_is_unknown_in_both_readers`, and `.git_fixture_cleanup_removes_read_only_contents`. The benchmark reports original-23 and complete-26 separately. It must not discard the two hidden-index controls or the cleanup regression to obtain the older denominator.

V-21 retains ordinary cuts queued/Prepared/Verified/PushStarted for both latch values and all false/null/true assertions; self/adoption admission and resume cases; and all six published-cleanup cases with immutable publication, operation ID, zero extra verification and no rebase/merge/push. V-8 retains build/reuse/missing stamp; valid then fingerprint/dirty stamp; valid then failed replacement build and stale reuse; run drift; same-count dirty byte drift; build drift; HEAD drift; dirty-to-clean drift. Each block starts from a reset image, while transitions within a block share its original state.

### New fixture tests: eight executions, not part of the 23-method timing subtotal

All eight methods are `[Category("Integration")]`, in the two new classes, with the assembly-local process limiter and sequential CP environment. No `[Repeat]`, implicit data source, skipped row or new C835 category is added. The following counts are proposed, not already executed.

| ID / exact method | Results | Decisive evidence and test-first red |
|---|---:|---|
| F-1 `CheckpointSourceApprovalReuseTests.Family_reset_restores_database_and_service_state` | 1 | Complete one real case, leave distinct request/evidence/events/reservations and set fault/verifier/queue state; reset and inspect via a fresh context/provider. `family-db-baseline`, `family-case-services-fresh`, `family-fault-unarmed`, `family-store-created-once` must hold, including preserved migration/seed rows. A no-op reset fails the database equality assertion. |
| F-2 `CheckpointSourceApprovalReuseTests.Family_reset_restores_native_git_image` | 1 | Mutate local/remote heads, add a registration/pin and ignored sentinel, and exercise a cleanup that deletes the source. Reset then independently read actual refs/index/status/registrations/sentinel bytes. `family-native-image-restored`, `family-recorder-fresh`, `family-native-init-once`. No production guard is bypassed to arrange/reset the owned image. |
| F-3 `CheckpointSourceApprovalReuseTests.Family_cases_are_order_independent` | 1 | Run a small, explicit adversarial sequence through each of the six family types: successful publication or admitted true followed by false/null refusal, plus the reverse order; compare normalized verdict/request/ref/cleanup observations with a fresh-family oracle. Require `family-order-independent` and actual live database/remote reads. Preserve all 42 main-test cases; this proof adds cross-case order coverage instead of duplicating the entire matrix repeatedly. |
| F-4 `CheckpointSourceApprovalReuseTests.Borrowed_family_lifetime_preserves_default_ownership` | 1 | Disposing one borrowed case leaves its family DB/image usable; disposing the family once removes owned resources after awaited work. Default standalone protocol/native harness still cleans its own DB/root. A foreign/shared database or linked/external image is rejected before mutation. `family-borrowed-owner-intact`, `family-default-owner-disposed`, `family-foreign-target-untouched`; use fake destructive adapters for foreign-path inputs. |
| F-5 `CheckpointSourceScriptReuseTests.Session_driver_matches_process_receipts` | 1 | Run the same fourteen invocation/scenario contract once in existing process mode and once in worker mode from reset seed images. Compare each real exit, source state/fingerprint relationships, build binding, reason, calls, TRX counts, trailer and file/line agreement. Normalize only generated paths/run IDs and observation timestamps, never status/SHA/count/reason; changed HEAD must match an independent Git observation in each run. `script-process-parity` and one worker PID across calls. Add failed/zero-count TRX cases for exits 1/3, and an offline busy-broker timeout for exit 4 with a budget of at least two seconds; assert exit/receipt/zero-driver calls, not elapsed time. |
| F-6 `CheckpointSourceScriptReuseTests.Session_driver_resets_environment_and_scope` | 1 | First invocation changes fixture environment/location/PowerShell variables and fails, second is a clean success. Fresh runspace, exact env/CWD restoration, separate streams and LASTEXITCODE; `script-environment-reset`, `script-scope-reset`, `script-next-exit-independent`. Block on protocol messages/completion, not sleeps. |
| F-7 `CheckpointSourceScriptReuseTests.Script_family_restores_stamp_head_and_counters` | 1 | Deliberately poison HEAD/index/worktree, stamp and call files between scenario blocks. Restore and compare the actual Git image/stamp baseline; require distinct fresh results directories and no stale receipt selection. `script-fixture-reset`, `script-results-distinct`, `script-scenario-counters-reset`. The intra-scenario failed-rebuild invalidation remains production work. |
| F-8 `CheckpointSourceScriptReuseTests.Session_driver_joins_children_on_failure` | 1 | At an explicit owned-child barrier inject cancellation/worker failure, release/cancel and join in finally. Direct process/stream/task completion observations prove `script-owned-children-joined` before root disposal and `script-primary-failure-preserved`. An unrelated sentinel process is untouched. Timeout/harness setup failure cannot be the expected red. |

The reset oracle must be independent of the reset implementation: compare against captured literal/table/byte observations, not two calls to the same reset digest helper. A failed reset stops the family and preserves evidence; it must not continue through later cases with unknown state. Instrument counters at actual database allocation/Git initialization/process-start boundaries, not as constants returned by a facade.

R-1 is the complete 26-result C835 roster, covering receipts/dirty source/build binding/validators/parser/settlement/recovery and census. R-2 is six existing standalone-harness results: four argument results of `AgentTaskLandBoundaryControlledTests.C448_V10_VerificationCannotFreezeOldTaskCoordinates`, and one each of `AgentTaskLandApprovalRecoveryTests.C488_OriginalApprovalNeverAdoptsHead` and `.C488_ChangedSourceNeedsNewApproval`. R-2 proves the default harness path and original approval coordinates still work. It does not claim coverage of the broken real-worker-death lane.

### Positive controls: retain the complete CARD-0835 battery

Run after ordinary Code, separate Review and confirmed land in a SourceLanding Mutation task. Copy the unchanged external driver plus `lib/build-slot.ps1` **and `lib/checkpoint-source.ps1`** outside the snapshot before mutating script source. Each compiling defect below runs alone, with exact method-scoped baseline green, intended assertion red, restoration, fresh build and green. Refresh restored timestamps; await each child and preserve TRX, logs and first assertion. No zero-test, timeout, compile failure, setup exception or unrelated guard is a successful red.

| Variant | Concrete compiling defect | Named detecting method and required assertion |
|---|---|---|
| PC-1A | Include nontracked ignored files in the tool source inventory. | `CheckpointSourceStateTests.clean_and_ignored_outputs_match_head`: `ignored-output-stability` expects zero dirt and unchanged fingerprint after writing ignored outputs. |
| PC-1B | Filter tracked `bin-*` entries out of that inventory. | `CheckpointSourceStateTests.clean_and_ignored_outputs_match_head`: `tracked-output-must-count` expects a force-added output-path edit to count and change identity. |
| PC-2 | Omit cached diff bytes from the tool fingerprint. | `CheckpointSourceStateTests.index_and_worktree_edits_are_dirty`: `index-only-content-change` expects different fingerprints for two staged contents with identical porcelain records and worktree restored to HEAD. Status alone still says dirty, so asserting only dirty would not kill this mutant. |
| PC-3 | Omit untracked content digests from the tool fingerprint. | `CheckpointSourceStateTests.untracked_contents_and_paths_affect_identity`: `untracked-content-digest` expects changed hash with identical path/count. |
| PC-4 | Convert capture failure to known clean/count zero. | `CheckpointSourceStateTests.failed_or_unstable_capture_is_unknown`: `failed-git-is-unknown` expects unknown/null and ineligibility for a deterministic Git failure. |
| PC-5 | Change the PowerShell canonical framing version byte. | `CheckpointSourceStateTests.script_and_tool_snapshots_agree`: `fixed-vector-parity` expects the independently pinned digest and tool/PowerShell equality on identical fixture bytes. Newline/NUL-path cases remain ordinary assertions; this is one framing defect, not an unspecified alternative mutation. |
| PC-6 | Hardcode script terminal dirty/source-state tokens to zero/clean. | `RunCheckpointSourceScriptTests.C835_DiagnosticReceipts`: `dirty-receipt-agrees-with-source-json` expects parsed nonzero dirty and dirty state matching the actual capture, while retaining the test exit/counts. |
| PC-7A | Omit the strict script dirty-preflight refusal. | `RunCheckpointSourceScriptTests.C835_StrictAdmission`: `dirty-preflight-no-lease` expects exit 2 and zero slot-acquire calls as well as zero dotnet calls. A later guard refusing is still red on the lease assertion. |
| PC-7B | Omit strict script expected-SHA equality. | `RunCheckpointSourceScriptTests.C835_StrictAdmission`: `wrong-sha-no-lease` expects exit 2/no lease/no dotnet for a clean tree and a different valid full SHA. |
| PC-7C | Omit the script post-slot source recheck. | `RunCheckpointSourceScriptTests.C835_StrictAdmission`: `slot-edit-no-driver` expects drift refusal and zero build/run calls after a slot shim changes source before granting; assert the recorded post-wait observation, not merely a final exit. |
| PC-8 | Make the script driver-boundary comparison accept a changed fingerprint. | `RunCheckpointSourceScriptTests.C835_DriftAndReuse`: `driver-drift-is-changed` expects changed/exit 2 after gated same-count content drift and preserved passed TRX counts. |
| PC-9A | Bypass the script strict reused-build binding qualification. | `RunCheckpointSourceScriptTests.C835_DriftAndReuse`: `invalid-stamp-no-tests` expects exit 2/zero test calls for each missing, dirty and one-coordinate-mismatched stamp. One shared qualification bypass is one variant. |
| PC-9B | Leave the previous build-source stamp valid when a replacement build fails. | `RunCheckpointSourceScriptTests.C835_DriftAndReuse`: `failed-rebuild-invalidates-stamp` expects absent/invalid stamp and strict reuse refusal after a failed rebuild of the same clean source. Otherwise the old matching stamp would pass. |
| PC-10 | On a handled script interruption, substitute start for the unavailable end capture and mark it clean. | `RunCheckpointSourceScriptTests.C835_TerminalEvidence`: `interruption-has-no-observed-end` expects null/unknown end and validator refusal. Use a reachable catch/finally seam; killing the wrapper before it can write cannot test this defect. |
| PC-11A | Normalize missing/legacy script source evidence into a complete clean default. | `RunCheckpointSourceScriptTests.C835_ReceiptValidation`: `legacy-receipt-ineligible` expects validator exit 2 on an otherwise valid legacy receipt. |
| PC-11B | Bypass the script validator's clean/stable-source eligibility predicate. | `RunCheckpointSourceScriptTests.C835_ReceiptValidation`: `dirty-source-clean-receipt-ineligible` expects `reason=source_ineligible` for dirty JSON behind a clean receipt and verified build binding. |
| PC-11C | Bypass the script validator's failed-run refusal. | `RunCheckpointSourceScriptTests.C835_ReceiptValidation`: `selected-receipt-ineligible` expects `reason=receipt_failed` for a clean source whose JSON and receipt agree on one failed test. Other matrix cases independently change CP identity, SHA, counts and tokens. |
| PC-12 | BuildReport constructs clean row evidence from request.Commit instead of propagating actual row source. | `CheckpointSourceExecutionTests.clean_and_dirty_runs_publish_bound_source`: `dirty-row-preserved` expects actual dirty fingerprint/state in persisted JSON, Markdown, line and git evidence for TUnit and command rows. |
| PC-13 | At executor admission replace the saved CreateRun observation with the current capture instead of comparing them. | `CheckpointSourceExecutionTests.changed_admission_and_driver_boundaries_refuse`: `queued-source-not-readmitted` expects original start identity retained, changed/exit 2 and no driver for A-at-create/B-at-execute. |
| PC-14A | Make the tool's normal driver-boundary source comparison accept drift. | `CheckpointSourceExecutionTests.changed_admission_and_driver_boundaries_refuse`: `driver-drift-stops-next-row` expects changed state, no next driver and all already-owned drivers awaited; place drift after build/row gates and check each labeled boundary. |
| PC-14B | Let the known-flaky rerun replace its original source/verdict after drift. | `CheckpointSourceExecutionTests.changed_admission_and_driver_boundaries_refuse`: `rerun-cannot-certify-drift` expects changed/exit 2 and retained original source even when the rerun TRX is green. |
| PC-15A | Drop expected-source SHA when binding CLI input to the run/row request. | `CheckpointSourceExecutionTests.strict_cli_and_reuse_require_clean_binding`: `strict-cli-refuses-wrong-sha` expects refusal/no driver and persisted expected SHA for each run/start/row entry. Invoke the real argument parser in-process with fake launch/slot I/O. |
| PC-15B | Bypass the tool's effective build-binding equality check. | `CheckpointSourceExecutionTests.strict_cli_and_reuse_require_clean_binding`: `property-mismatch-no-tests` expects strict reuse exit 2/no driver when only effective MSBuild properties differ. |
| PC-16 | Synthesize a known clean end after executor exception. | `CheckpointSourceExecutionTests.terminal_paths_do_not_invent_clean_evidence`: `executor-error-has-no-observed-end` expects unknown end and failed qualification while retaining existing crash/owner exit precedence. Owner cancellation and timeout remain separate ordinary matrix assertions. |
| PC-17 | Remove merge source compatibility checks and stamp the latest heading identity onto retained rows. | `CheckpointSourceExecutionTests.merge_cannot_launder_source_identity`: `old-dirty-row-not-relabeled` expects merge refusal for an older dirty CP retained beside a later clean CP. |
| PC-18A | Upgrade schema-1/missing tool evidence to a known clean default during validation. | `CheckpointSourceExecutionTests.validation_requires_complete_consistent_source`: `legacy-report-ineligible` expects exit 2 despite valid SHA/counts. |
| PC-18B | Ignore tool heading/row/receipt source equality during validation. | `CheckpointSourceExecutionTests.validation_requires_complete_consistent_source`: `row-heading-disagreement` expects exit 2 when exactly one row's otherwise valid source differs from the heading. |
| PC-19 | Accept the last duplicate source-clean declaration. | `ReviewEvidenceParserTests.C835_SourceCleanGrammar`: `duplicate-clean-is-unknown` expects null for false then true and true then false, with all other grammar valid. |
| PC-20 | Default absent ReviewedSourceClean to true. | `ReviewEvidenceParserTests.C835_SourceStateCannotBeInferred`: `full-scope-is-not-source-clean` expects null for a valid bare SHA/Clean/Full report without the field. |
| PC-21 | Persist true instead of the parsed nullable assertion in settlement. | `CheckpointSourceApprovalTests.settlement_persists_source_assertion`: `settled-source-assertion-roundtrip` expects false/null in a new DB context and DTO after real report settlement and replay. |
| PC-22 | Remove the source-clean predicate from LoadUsableEvidenceAsync. | `CheckpointSourceApprovalTests.land_admission_requires_clean_review_source`: `unclean-evidence-no-request` expects conflict code `review_evidence_source_not_clean`, no new durable request, empty queue and no Git mutations for false/null. All other approval dimensions are valid. |
| PC-23 | Retain the old early return for unlatched owners in the unpublished approval recheck. | `CheckpointSourceApprovalTests.recovery_and_resume_recheck_source_assertion`: `unlatched-resume-refuses-unclean` expects persisted source-not-clean refusal and zero new mutations after true admission, false/null update and restart at each saved cut. Test false/unlatched first so the intended assertion is unambiguous. |
| PC-24 | Change the actual new migration's nullable column default/backfill to true. | `CheckpointSourceApprovalTests.migration_and_override_preserve_unknown`: `legacy-source-clean-remains-null` expects null on the upgraded legacy row and no true SQL default. Mutate the executed migration, not just the model snapshot. |
| PC-25 | Assign new override source cleanliness from the superseded row. | `CheckpointSourceApprovalTests.migration_and_override_preserve_unknown`: `override-does-not-inherit-true` expects new null/false and old true unchanged, with a valid explicit SHA to keep authorization checks satisfied. |
| PC-26 | Always add reviewedSourceClean=true to delegate.ps1 finding JSON. | `DelegateScriptLandApprovalTests.C835_FindingSourceCleanIsExplicit`: `finding-json-preserves-explicitness` expects absent/null when omitted, false when explicit false, and true only when supplied; inspect actual loopback request bodies. |
| PC-27A | In `SourceSnapshot.cs`, skip the `git ls-files -v -z` hidden-entry refusal while keeping capture otherwise intact. | Both `CheckpointSourceStateTests.assume_unchanged_edit_is_unknown_in_both_readers` and `CheckpointSourceStateTests.skip_worktree_edit_is_unknown_in_both_readers`: `assume-unchanged-tool-hidden` and `skip-worktree-tool-hidden` expect `unknown/indexed_path_hidden`; known before setting and after clearing each flag. |
| PC-27B | In `scripts/lib/checkpoint-source.ps1`, skip the `git ls-files -v -z` hidden-entry refusal while keeping capture otherwise intact. | Both `CheckpointSourceStateTests.assume_unchanged_edit_is_unknown_in_both_readers` and `CheckpointSourceStateTests.skip_worktree_edit_is_unknown_in_both_readers`: `assume-unchanged-script-hidden` and `skip-worktree-script-hidden` expect `unknown/indexed_path_hidden`; known before setting and after clearing each flag. |

PC-10's landed label is `handled-driver-interruption-has-no-observed-end` (contains the historical witness); PC-18B is `fingerprint-row-heading-disagreement`, with independently eligible row evidence. Preserve both mappings explicitly. PC-27A and PC-27B each run **both** hidden-index methods listed above, and require respectively both `assume-unchanged-tool-hidden`/`skip-worktree-tool-hidden` or both script labels. Do not run only V-5's older parity method for these controls.

The inherited inventory is **37 variants**, **111 baseline/red/restore phase invocations**; PC-27's two-method selections make **117 TUnit phase results** in total. Every other inherited PC selects one result. Derive filters as `/*/*/<Class>*/<Method>*`; for PC-27 use `/*/*/CheckpointSourceStateTests*/(assume_unchanged_edit_is_unknown_in_both_readers*)|(skip_worktree_edit_is_unknown_in_both_readers*)`, Min 2. Literal shell filters contain `|`, whereas table cells escape it as `\|`. Preserve all existing coverage rows, even those outside the two optimized methods.

Additional controls for the new fixture behavior (no product mutations implied):

| PC | Concrete compiling defect | Named detecting test / intended assertion |
|---|---|---|
| PC-886-1 | Omit database restore after the first family case. | F-1 / `family-db-baseline`; old durable rows remain. |
| PC-886-2 | Keep the previous triggered SaveFault instance in the next case view instead of constructing a new one. | F-1 / `family-fault-unarmed`: Triggered must be false before the next operation; inspect this before any new save. |
| PC-886-3 | Omit the remote master ref from the restored native image. | F-2 / `family-native-image-restored`; inspect the independent Git command's exit and ref value with that named assertion before using a throwing RequiredAsync helper. Missing or wrong ref is an assertion-red, not a setup exception. |
| PC-886-4 | Drop the borrowed database when disposing a case. | F-4 / `family-borrowed-owner-intact`, using a maintenance existence observation before opening the next case, not a connection exception. |
| PC-886-5 | Force the worker's returned exit to zero. | F-5 / `script-process-parity` on a known script refusal while evidence remains nonzero. |
| PC-886-6 | Omit restoration of the worker's per-request environment after a failing request. | F-6 / `script-environment-reset`; explicit next-request observation differs. |
| PC-886-7 | Omit clearing the preceding scenario's call-counter file during reset. | F-7 / `script-scenario-counters-reset`, inspected before invoking the next script. |
| PC-886-8 | Release the worker fixture's root-disposal barrier before its owned child/stream completion acknowledgement. | F-8 / `script-owned-children-joined` at the recorded disposal attempt; release the child in finally so the defect yields an assertion rather than a hang. |

TestDesign confirms each new mutation compiles against its proposed seam and reaches the named first assertion. Total proposed battery: 45 variants / 135 phase invocations / 141 TUnit phase results. This is a design census, not a claim of executed positive controls. Ordinary Final Review judges existing-behavior regressions and new reachable fail-open behavior under the operator's 2026-10-01 rule; do not pad it with unrelated suites. The explicit performance and PC acceptance obligations still remain visible if unmet.

### Measurement protocol and known hazards

Use CP-1/2 for the pre-change baseline and CP-5/6 for the final pair, on the same host per OS. The exact selection is six named classes intersected with `[Category=C835]`, giving 26 results. Extract the original 23 names from the roster above and sum their TRX durations; also retain all 26 and both heavy method durations. No test-host startup or build time is included in the 138.6-second historical test-time threshold. Record whole invocation time separately so moving work into setup/teardown cannot masquerade as savings. Family setup/reset/worker startup remains inside the target test's timed method.

The loaded measurement supervisor is task-owned evidence tooling, not a new product feature. It starts exactly 24 owned CPU-bound children (Linux `yes` with output discarded; Windows 24 equivalent owned native/runtime workers, with the same program before/after on that OS), records PID/start identity and readiness, and supervises the single selected checkpoint tool run. Sample that all 24 remain alive during the measured test interval. In finally stop and await only these children; record zero remaining owned burners. If the tool returns 75, keep burners supervised while issuing `wait` on that same run. On cancellation stop/await the checkpoint through its owner before reaping load; never leave a detached run unsupervised. Existing `verify-card0778-control-matrix.cjs` supplies a cleanup pattern, not a command to run: its mutation battery/raw build are outside this task.

Do not run a loaded row opportunistically beside other tasks and call it controlled load. Record ambient occupancy/CPU and the idle precondition (no intentional burners and no competing benchmark lane); coordinate measurement slots with the caller. No process kill, host-budget change or stack restart is authorized to manufacture idle. If comparable measurements cannot be obtained, report performance unqualified while preserving functional evidence.

| Known card(s) | Failure family / treatment in this plan |
|---|---|
| CARD-0791 / 0794 / 0742 | Codex readiness snapshot timing, Claude effort timing, multiline PTY broken pipe. No provider/readiness/PTY test selected. |
| CARD-0818 | Executor log callback timeout can hold its writer during disposal. Class excluded; new worker tests release and join in finally. |
| CARD-0820 | Windows temp-root/file-handle contention and tight timing, including EvidenceFolder, renewal/ownership, resilience and controlled unregister observations. No broad checkpoint/Unit lane. New image restoration is directly qualified on Windows; a selected failure is not excused by this card. |
| CARD-0828 | Ownership uncertainty tests leak a watcher/executor after failure. Class excluded; no shared unjoined background executor introduced. |
| CARD-0848 | DetachedLauncher fixed marker window. Class excluded; explicit worker protocol/exit observations replace timing guesses. |
| CARD-0879 | Herdr AlwaysOn DetectTimeout versus PaneClosed. Parity class excluded. |
| CARD-0889 | Codex submit confirmation fails under 24 burners with a two-second budget. Submit tests excluded; burners apply only to the selected C835 measurement. |
| CARD-0890 | `C448_V15_RealWorkerDeathRecoversDurableBoundaries` has seven inherited Linux module-initializer failures. Excluded explicitly; R-2 does not select its class or claim real worker-death proof. |
| CARD-0900 | `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` readiness deadline under load. That class is excluded: the production driver is unchanged and F-5 directly compares the affected source-script fixture. Do not run its whole class as an unrelated regression tax. |
| CARD-0757 / 0751 | ScaledTimeProvider and HttpResilience wall/fake-clock scheduling flakes. Classes excluded; use a truly controlled case clock and explicit barriers. |

These cards were read; none grants an automatic rerun exemption, broader timeout, new skip or an inherited verdict for a newly selected failure. Keep full first-run evidence and classify any failure at its actual SHA. No tests that boot real Program, client build, E2E, production runner or native provider are commissioned.

### Test-first order and cost

S0 baseline rows must precede reuse edits. S1's compiling fixtures run CP-3 as one declared preparatory red. Commit S2 implementation and S3's static assertion/census/coverage documentation before the final green selection: run CP-3/4, then idle CP-5 and loaded CP-6 at that **same SHA**. Keep resulting measurement evidence outside tracked source and hand it to the caller for the card revision; do not add a late evidence-only source commit that invalidates the final receipts. Execute one tool run per committed slice/measurement group; idle and loaded groups are deliberately separate `--rows` selections because their external load differs. Never start another run while the previous one reports 75. Do not repeatedly run all six rows after each edit. A source change after a green row requires the affected rows at the final reviewed source, with the rerun and reason reported; never relabel an older receipt.

Estimates, not measurements: the ordinary table totals **56 minutes Linux / 82 minutes Windows**, including six isolated builds per OS and the required before/after measurements. CP-3's preparatory red adds 8 / 10 minutes. Final implementation verification is CP-3..6: 34 / 49 minutes; CP-1/2 are preserved baseline evidence, not green certificates for changed code. Allow another 2–4 minutes per OS for the explicitly accounted, leased tool bootstrap below; it contributes no TUnit results. Proposed authoring: 90–150 minutes, with TestDesign required before Code; split committed work if the dispatch budget cannot hold authoring plus the Linux 64-minute verification allowance, bootstrap and slot waits. Windows is a separate focused execution lane, not an inaccessible-host pass inferred here.

Mutation estimate: 37 inherited variants × 3 phases × 2 minutes = 222 minutes, plus eight new variants × 3 × 3 minutes = 72, plus 15 minutes custody/discovery/reporting = **309 minutes**. This includes targeted build/startup cost and is intentionally separate from Code. It exceeds a single 240-minute dispatch ceiling: commission sequential bounded Mutation batches on the same companion through supported SourceLanding ownership after the prior batch is terminal/restored, recording variant coverage at the same landed SHA. Do not launch overlapping same-operation snapshots or truncate the battery. Ordinary plus Mutation estimate is 447 minutes across OS/stages, or 465 including preparatory reds, plus authoring and scheduling; none is a performance claim.

### Checkpoints

Closed ordinary manifest. Each row performs **one isolated build and one exact TUnit filter**, through `dotnet run --project tests/Antiphon.Tests --no-build`, never `dotnet test`. All rows are serial with `TUNIT_MAX_PARALLEL_TESTS=1`; class operands retain trailing `*`. The C835 category intersection intentionally selects only the two parser and one delegate methods, not their entire classes. Source-derived baseline and planned final counts are the same on Windows and Linux; there are no expected skips. New eight-result counts require TestDesign confirmation and implemented-source/TRX recount.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---:|---|---|
| CP-1 | S0 | `tests/Antiphon.Tests -> bin-c886-before-idle/` | before-idle | `/*/*/(CheckpointSourceStateTests*)\|(RunCheckpointSourceScriptTests*)\|(CheckpointSourceExecutionTests*)\|(CheckpointSourceApprovalTests*)\|(ReviewEvidenceParserTests*)\|(DelegateScriptLandApprovalTests*)/*[Category=C835]` | R-1, baseline | Linux 26; Windows 26; 0 failed/skipped; original-23 subtotal recorded | 26 | 10 | 15 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S0 | `tests/Antiphon.Tests -> bin-c886-before-loaded/` | before-loaded | `/*/*/(CheckpointSourceStateTests*)\|(RunCheckpointSourceScriptTests*)\|(CheckpointSourceExecutionTests*)\|(CheckpointSourceApprovalTests*)\|(ReviewEvidenceParserTests*)\|(DelegateScriptLandApprovalTests*)/*[Category=C835]` | R-1, baseline under 24 burners | Linux 26; Windows 26; 0 failed/skipped; load and original-23 subtotal recorded | 26 | 12 | 18 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c886-fixtures/` | fixture-isolation | `/*/*/(CheckpointSourceApprovalReuseTests*)\|(CheckpointSourceScriptReuseTests*)/*` | F-1..F-8 | Linux 8; Windows 8; 0 failed/skipped in final green | 8 | 8 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c886-compat/` | standalone-harness-compatibility | `/*/*/(AgentTaskLandBoundaryControlledTests*)\|(AgentTaskLandApprovalRecoveryTests*)/(C448_V10_VerificationCannotFreezeOldTaskCoordinates*)\|(C488_OriginalApprovalNeverAdoptsHead*)\|(C488_ChangedSourceNeedsNewApproval*)` | R-2 | Linux 6; Windows 6; 4+1+1 results, 0 failed/skipped | 6 | 8 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c886-after-idle/` | after-idle | `/*/*/(CheckpointSourceStateTests*)\|(RunCheckpointSourceScriptTests*)\|(CheckpointSourceExecutionTests*)\|(CheckpointSourceApprovalTests*)\|(ReviewEvidenceParserTests*)\|(DelegateScriptLandApprovalTests*)/*[Category=C835]` | R-1, performance | Linux 26; Windows 26; 0 failed/skipped; timing gates separately evaluated | 26 | 8 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c886-after-loaded/` | after-loaded | `/*/*/(CheckpointSourceStateTests*)\|(RunCheckpointSourceScriptTests*)\|(CheckpointSourceExecutionTests*)\|(CheckpointSourceApprovalTests*)\|(ReviewEvidenceParserTests*)\|(DelegateScriptLandApprovalTests*)/*[Category=C835]` | R-1, performance under 24 burners | Linux 26; Windows 26; 0 failed/skipped; load and timing gates separately evaluated | 26 | 10 | 15 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

## Execution, acceptance and handoff

Use the checkpoint tool as required by this brief, with committed source and `--expected-source-sha`. Launcher bootstrap is the only separately stated build: acquire a build slot for the isolated tool build, then release it before starting the built tool. The `--no-build` tool launcher only orchestrates; its executor takes the required leases for its builds and test drivers. Never hold an outer build-slot lease across `run`/`wait`, or wrap `scripts/run-checkpoint.ps1` in another lease. `-NoSlot` is prohibited for actual verification drivers; preserve the existing private script fixtures' offline `-NoSlot`/fake-broker behavior, never a real broker from an inner fixture. A broker refusal/slot timeout is reported, never bypassed.

Future command shape (PowerShell; actual SHA and selected rows come from the committed slice):

```powershell
$plan = 'docs/superpowers/plans/2026-10-01-card-0886-heavy-test-speedup-plan.md'
$sha = (git rev-parse HEAD).Trim()
# One explicitly accounted launcher bootstrap, rebuilt only if its source changes.
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c886-tool-build -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c886-tool/ --nologo
# Run once for the selected committed group. CP-2/6 are supervised under 24 owned burners.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c886-tool/ -- run --plan $plan --rows CP-3,CP-4 --expected-source-sha $sha --max-wait 50s
# If exit 75, wait on the printed run ID; do not start another run.
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c886-tool/ -- wait <run-id> --max-wait 50s
```

TestDesign confirms owner binding and these CLI options against the then-landed tool (CARD-0885 may change that interface). Keep the forward-slash isolated output in both bootstrap and invocation. Select `CP-1`, `CP-2`, `CP-3` (red), `CP-3,CP-4` (green), `CP-5`, then `CP-6` in the declared order; the load supervisor surrounds only CP-2/6. Every continuing wait uses the same printed run ID and no new build. If owner qualification fails, report it and preserve the run instead of silently switching to a raw driver.

Keep the driver's unedited `CHECKPOINT CP-n` lines with SHA, build/filter, executed/passed/failed/skipped, fresh TRX, slot/wait and `dirty`, `source`, `sourceState`, `buildSource` tokens; report reruns and their reasons. Validate final CP-3..6 receipt selection with `scripts/validate-checkpoint-receipt.ps1` or the tool equivalent against the exact reviewed source. CP-1/2 retain their original baseline SHA. Total scheduled ordinary source results per OS are `26+26+8+6+26+26=118` (236 across both); final implementation rows contribute 66 per OS, and the declared red CP-3 adds eight per OS. Counts in a future report must be observed, not copied from these floors.

| Card acceptance / hard rule | Required evidence |
|---|---|
| Each heavy test about 50% or less, idle and under 24 burners | Paired CP-1 vs CP-5 and CP-2 vs CP-6 per-method TRX times, ratios, same-host conditions and whole-invocation times. Historical idle reference points are 38.3 s landing and 24.45 s script; loaded landing reference 77.3 s. Also require the paired current-baseline ratios; do not relax an absolute miss by choosing a slower baseline. |
| 23-test round about 60% of 231 s | Original-23 subtotal at most 138.6 s idle, and at most 0.60 of fresh before time in each measured condition. Report current all-26 total alongside it. Additional fixture tests/cost are explicit, not hidden inside a changed denominator. |
| No weakened/removed assertions | Complete before/after semantic assertion map from 37/35 lexical starting sites, original labels preserved, all 42 landing cases/eight script scenarios/fourteen invocations recorded. |
| No state leakage/order dependency | F-1..F-8 green on Linux and Windows; exact private owner/reset proofs, fresh services/runspaces, adversarial case orders, children joined. |
| No real-time margin under two seconds | Fixed FakeTimeProvider where logical time matters, explicit completion/barriers, diagnostic deadlines only; no sleep-based success or PC verdict. |
| Receipts and census preserved | R-1 retains actual script/tool guard coverage and 26 results, R-2 default harness behavior; strict final receipts clean/bound and validator accepted, same selected roster on each OS. |
| Every CARD-0835 control re-proven | Later Mutation report for all 37 inherited variants at the landed SHA, named assertion reds and restored greens, plus eight new reset/isolation controls. No claim that ordinary Review or this Plan executed them. |
| Record timing on CARD-0886 | Caller appends baseline/final SHA, host/OS/load, four timing samples, 23/26 counts, ratios, receipt paths and outstanding Mutation status through a description revision; this Plan performs no board write. |

If the target pair improves but the round still exceeds its gate, keep the card open for measured follow-up; do not silently optimize unrelated tests, lower the roster, absorb CARD-0885 or declare the target approximate enough. Publication is test-source activation only; no application/runner restart is needed. Rollback is a forward commit restoring test fixture behavior while preserving evidence and cases, never branch reset or mutation of production configuration.

TestDesign handoff: validate the borrowed-resource/reset table closure, native image custody and PowerShell exit/stream parity design; freeze the eight new method bodies and confirm their concrete controls; confirm all 37 inherited first-assertion witnesses and 26/42/14 census; pin the six tool rows/measurement supervisor and collision order. No operator product decision is needed to proceed under these defaults.

Plan validation: a read-only Node census confirmed the 23 original method declarations, 26 current category results, 42 landing cases, eight script fixture allocations/fourteen calls, and 37/35 lexical assertion sites. Static manifest validation confirmed six twelve-column rows, distinct isolated outputs, escaped filter pipes, 118 planned results per OS and 56/82 minute sums. The 37 inherited plus eight proposed controls are unique. These are source/design checks, not executed TUnit, timing, PowerShell worker or mutation evidence. TestDesign must retain that distinction.
