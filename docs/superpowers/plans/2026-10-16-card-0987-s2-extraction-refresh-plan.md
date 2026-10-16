# CARD-0987 S2: extraction closure and bound verification

Date: 2026-10-16. Plan + TestDesign task: `2799778e-215c-4099-b0b2-e8b4bdd068d0`.
Inspected HEAD: `fecfd65e9c13d54e4bc1bb9c6b1265601b4590b8`; fetched master:
`69031cfa11244dc988c8d79d3f516bc627dd3824`. Their only task delta is the S2 preflight.
Master-only; no RC change. **Design complete; next is Code S2, with verification folded in.**

This replaces only S2's scope/selection in the [custody plan](2026-10-15-card-0987-custody-refresh-plan.md)
and [frozen verification ledger](2026-10-15-card-0987-test-design.md). Their other requirements,
method/variant oracles and pending controls remain binding, subject to the explicit clarifications
below. The [preflight](../evidence/2026-10-16-card-0987-s2-preflight.md) and reachable full report
`/work/worktrees/task-288c1f6e/.antiphon/task-288c1f6e.md` were read. No implementation, build,
test, mutation, live cleanup or activation is claimed by this document.

## Ground truth

| Card/earlier scope assumes | Inspected code/evidence | S2 consequence |
|---|---|---|
| Original card's 40 ports and 66-case union are current | Landed S1a has 14 tool-only results; S1b has 16 including nested custody. Remaining sweep/scope ports are 27 + 11. | Preserve **14 / 16 / 54 / 80**; 80 remains S3's 12 ownership + 14 adapter + 54 compatibility, not an aggregate of supplemental rows. |
| Moving the enumerated cleanup files closes the library | `SafeDelete.cs:157,160` calls `OutputCleanup.IsIsolatedOutputId`; `OutputCleanup.RecordCreatedOutputs` requires tool-owned `RunState`. | Extract the two pure naming predicates; keep the output orchestration in the tool. |
| ExclusiveDirectory has only C# consumers | `scripts/lib/checkpoint-owned-output.ps1:10` reads its exact source path and compiles it alone. `run-checkpoint.ps1` invokes the helper for results and build directories. S1a0 already suffered this regression. | Rebind the path atomically with the move; retain standalone compilation and test both OS kernels. |
| Namespace preservation alone closes the move | `ToolCopyAuthority` is internal, currently friends `Antiphon.Tests`; `CheckpointApp` calls `Seal`. Sweep options/core/receipt are internal in a file also containing TUnit hooks and roster. | Add the tool friend at the library boundary; split core from test hooks and retain LifecycleHost links. |
| Generic receipt parsing can simply move | `ReceiptJson.Validate` names `ReportModel`; `ReceiptFile` calls the tool's coverage reader. | Separate generic reader/parser from tool report validation; preserve all legacy facade behavior. |
| Frozen CP-25/27/28 cover all affected consumers | The four-variant sweep race repair, script source loader, project contract and native scope/linked-host consumers are additional. | Add CP-30..35 below without a whole Unit/namespace run. |
| CARD-1307 A3 still uses static custody | `CheckpointDelete.SafeWrite => new(context)` already serves the FileOnly branch. | Add a positive/fresh-context witness; no A3 production repair or card closure is presumed. |
| Later integration is available | No server/runner HostCleanup registrations or library references exist; native daily adapters still throw. | S2 is independently landable but does not enable daily cleanup. |

Historical authority is the landed [S1a0](../evidence/2026-10-15-card-0987-s1a0-code.md),
[S1a1](../evidence/2026-10-15-card-0987-s1a1-code.md),
[S1a](../evidence/2026-10-15-card-0987-s1a-code.md),
[S1b](../evidence/2026-10-16-card-0987-s1b-code.md), and
[D-1 repair](../evidence/2026-10-16-card-0987-s1b-d1-code.md) records. Preserve their actual
tested SHAs, failures, later repairs and costs. The D-1 tracked record defers final receipts to its
task report; its four source variants exist here. None of that evidence is relabeled as S2 green.

## Decisions

- **D-1 — One shared naming policy.** Add `CheckpointOutputNames.IsPathSegment` and
  `IsIsolatedOutputId` in `src/Antiphon.HostCleanup/Checkpoints/CheckpointOutputNames.cs`.
  Preserve the current conditions and `^bin-[A-Za-z0-9._-]+$`/CultureInvariant regex exactly,
  including its final-LF behavior; no normalization or new grammar. Both instance methods in
  `CheckpointOutputCleanup` and static `OutputCleanup` forward to it; moved `CheckpointDelete`
  calls it directly. Reject copying the predicate, moving `RunState` orchestration, or a reverse
  project reference. `IsStrictlyInside` remains unchanged in the tool.
- **D-2 — Preserve the PowerShell source contract.** Move the existing standalone partial
  `ExclusiveDirectory.cs` byte-for-byte to `src/Antiphon.HostCleanup/Checkpoints/ExclusiveDirectory.cs`;
  change only the helper's relative source path to `../../src/Antiphon.HostCleanup/Checkpoints/ExclusiveDirectory.cs`.
  Keep its BCL-only imports, namespace, native calls and error mapping. The context overload stays
  in the moved `CheckpointCleanupDefaults.cs` partial. The helper still uses `Read-CheckpointText`,
  fresh-process Add-Type, `Create(string)`, and the existing type guard. Reject assembly loading,
  an old-path duplicate/stub, and adding context/library imports to the standalone source.
- **D-3 — One compiled owner.** Move the closure in the table below; preserve existing public
  namespaces/signatures and serialized shapes. `Antiphon.HostCleanup` owns the moved types;
  executable callers resolve the existing tool-to-library reference. Keep `ToolCopyAuthority`
  internal with its existing `Antiphon.Tests` friend and add `InternalsVisibleTo("Antiphon.Checkpoints")`
  in that moved file for `CheckpointApp.Seal`. A friend is not a project/assembly dependency.
  Preserve the executable's existing test-friend declaration in new `C/AssemblyInfo.cs` when
  its original declaring file moves; extraction must not silently remove assembly-level access.
  Reject making authority public or compiling copied public types in both assemblies.
- **D-4 — Split policy from tool models.** Move `ReceiptFile` unchanged except its call to the new
  shared `CheckpointFileReader`. Move the verified-read implementation to that reader and leave
  every `Coverage.ConfinedFileReader` overload/constant as a forwarding facade. Move generic strict
  deserialize/walk/identity validation into `CheckpointReceiptJson`; shared ownership readers use
  it. Keep `ReceiptJson` in `C/Evidence/ReceiptFile.cs` as a facade that also applies the existing
  `ReportModel` checks on **Read, Parse and Validate**, including Program's direct report reads.
  `ReportWriter` retains digest/source/terminal-exit binding. Reject a report-only check that runs
  solely in ReportWriter, or a generic parser naming `ReportModel`/`CheckpointApp`/`RunState`.
- **D-5 — CARD-1307 A1/A2 are out of S2.** They remain separately commissionable on CARD-1307:
  A1 needs a partial-removal/marker policy and fault test; A2 needs an explicit cancellation exit
  contract and Program token wiring. The card records that neither changes the S1a verdict and no
  real production token is wired today. Extraction must preserve current fail-closed behavior,
  not claim transactional deletion. A3 is already context-bound and receives a regression witness.
  A4 uses the explicit base SHA for evidence-diff (CARD-1290). Reject an invented exit code or
  silently absorbing behavioral cancellation changes into a move. No CARD-1307 completion is claimed.
- **D-6 — Independent S2 landing, both OS receipts.** S2 needs the landed S1 chain only. Run
  CP-29 on committed unmoved ports, then the post-move rows, including Windows CP-33 on the same
  source SHA. Missing Windows proof leaves S2 verification incomplete; it is not deferred to S10.
  No deployment/restart is required. Defaults/catalogue GETs succeeded at planning time (revision 2;
  eligible Linux and Windows). Resolve placement again at dispatch; omit `-Runner`, omit `-Platform`
  unless the lane requires an OS, and use `-Platform Any` only to clear a stale constraint.
- **D-7 — No scheduled further planning round.** S3-S10 can be commissioned directly to Code
  under the retained plan/ledger and dependency table below. These are settled implementation
  decisions, not defaults awaiting approval. New source drift or a newly demonstrated defect can
  require a scoped amendment; there is no known omitted S2 dependency being deferred to another Plan.

## Extraction and caller audit

Aliases: `C=tools/Antiphon.Checkpoints`, `L=src/Antiphon.HostCleanup`,
`T=tests/Antiphon.Tests`. Each brace list enumerates exact files; it is not directory-wide scope.
`rg -w` searches covered every declared moved type (including its nested types) across
tools/src/tests/scripts/server, plus naming methods, source-path strings, Compile links and
ProjectReference consumers. Same-named `Antiphon.HostCleanup.CleanupReceipt` and unrelated
process-identity records are different types. The remaining callers keep the facades/imports.

| Exact source -> destination | Types/symbols and caller disposition |
|---|---|
| `C/Cleanup/ToolCopyCleanup.cs` -> `L/Checkpoints/ToolCopyCleanup.cs` | `CleanupReceipt`, `ToolCopyCleanup`, `ExecutorOwnership`, `ExecutorOwnershipStore`, `ContainedCleanup`; retained callers: Program, WaitCommand, OutputCleanup, EvidenceFolder, CheckpointApp, BaselineComparer, scope, native ownership/recovery/launch tests and virtual fixture. Executor reader switches to generic parser. |
| `C/Cleanup/RunOwnership.cs` -> `L/Checkpoints/RunOwnership.cs` | `RunOwnership`, `RunOwnershipStore`; shared authority/guard/cleanup, CheckpointApp, scope/sweep and fixture callers retain formats. Reader switches to generic parser. |
| `C/Cleanup/TestRootGuard.cs` -> `L/Checkpoints/TestRootGuard.cs` | `CheckpointRootMarker`, `TestRootGuard`; CheckpointApp, scope/sweep, ownership/usage/authority/race callers. Keep marker/gate/owner-death contract. |
| `C/Cleanup/ProcessIdentity.cs` -> `L/Checkpoints/ProcessIdentity.cs` | `ProcessIdentity`, `ProcessVerdict`, `ProcessObservation`, `ProcessIdentityProbe`; cleanup, Program/Wait, ProcessControl, CheckpointApp/LaunchOutcome and scope/fixture callers. `ProcessControl` and launch/stop orchestration remain tool/test-owned. |
| `C/Cleanup/SafeWrite.cs` -> `L/Checkpoints/SafeWrite.cs` | `WriteKind`, `WriteOwnership`, `SafeWrite`, `CheckpointWrite`; shared cleanup plus BuildConfinement, RunStateStore, ProjectWriteRoots, BaselineComparer, EvidenceFolder/SourceRunGuard, scheduler/log/shadow/process/row writers, ReportWriter, scope and tests. No caller gets a fresh default registry. |
| `C/Cleanup/SafeDelete.cs` -> `L/Checkpoints/SafeDelete.cs` | `DeleteDisposition`, `DeleteResult`, `DeleteOwnership`, `SafeDelete`, `CheckpointDelete`, private `GitRun`; shared cleanup plus OutputCleanup, EvidenceFolder, BaselineComparer and process-boundary/virtual tests. Rebind both naming calls per D-1. |
| `C/Cleanup/OwnedOutputLedger.cs` -> `L/Checkpoints/OwnedOutputLedger.cs` | `OwnedOutputLedger`, `CheckpointOutputLedger`; write/delete/authority, OutputCleanup, ShadowCopy, SourceRunGuard and existing/virtual tests. No durable-ledger adoption. |
| `C/Cleanup/ToolCopyAuthority.cs` -> `L/Checkpoints/ToolCopyAuthority.cs` | `ToolCopyDigest`, `ToolCopyAuthority`, nested `CopyRecord`/`CopyEntry`; shared delete/cleanup/run ownership, CheckpointApp.Seal and direct fixture/authority tests. Retain internal access per D-3; rebind generic parser. |
| `C/Cleanup/OutputCustody.cs` -> `L/Checkpoints/OutputCustody.cs` | `OutputCustody`, nested `Entry`/`Window`; SafeWrite/OwnedOutputLedger and OutputCleanup/virtual tests. No API change. |
| `C/Cleanup/{ExclusiveDirectory,CheckpointCleanupDefaults}.cs` -> `L/Checkpoints/{ExclusiveDirectory,CheckpointCleanupDefaults}.cs` | `ExclusiveDirectory` both partials, `CheckpointCleanupDefaults`, `CheckpointNativeGitRunner`; all facade default callers, custody tests, and PowerShell source consumer. Preserve readonly process context and injectable Git adapter. |
| `C/LedgerStat.cs` -> `L/Checkpoints/LedgerStat.cs` | `LedgerStat`; BuildConfinement, ToolCopyAuthority and confinement/custody tests. Preserve portable identity and unknown comparisons. |
| Split `C/Evidence/ReceiptFile.cs`, `C/Coverage/ConfinedFileReader.cs`; new `L/Checkpoints/{ReceiptFile,CheckpointReceiptJson,CheckpointFileReader}.cs` | D-4. Receipt callers include BuildConfinement, state, TrxReport, Program/Wait, CheckpointApp, ProjectWriteRoots, SourceSnapshot, RepeatRequest, manifest/importer and ReportWriter; no caller rewrite needed through retained public facades. CoverageCommand/fixture/PlanCoverageHandleTests retain coverage facade. |
| `scripts/lib/CheckpointProcessRunner.cs` stays at its existing path; move its Compile link from `C/Antiphon.Checkpoints.csproj` to `L/Antiphon.HostCleanup.csproj` | `CheckpointProcessRunner` and nested `Result`; native Git adapter, BuildConfinement, CheckpointFixtureProcess and custody tests resolve the library type. `scripts/lib/checkpoint-process-runner.ps1` keeps standalone source loading unchanged. No runner behavior changes. |
| Split `T/Checkpoints/CheckpointTempRootSweep.cs`; new `L/Checkpoints/CheckpointTempRootSweep.cs` | Make `CheckpointSweepOptions`, `CheckpointSweepReceipt`, `CheckpointTempRootSweep` public in `Antiphon.Checkpoints`; move private cursor/core with them. All current C# callers already import that namespace or are nested beneath it, including LifecycleHost. Replace scope marker references with `TestRootGuard.MarkerName`. Keep TUnit hook and `CheckpointRoster` in the original linked test file. |
| `T/Checkpoints/CheckpointTestScope.cs` stays test-owned | Add explicit context/temp-root construction; route file/time/usage observations through it. Keep native constructor compatibility, joins and process control test-owned; allow synthetic `ProcessIdentity` registration without opening a real process. Both paths use the same scope policy. Preserve failed-join retention, fresh 10-second cleanup budget and existing native child ownership. |

The scope's `CheckpointUsageEvent.Write` already accepts context; pass it without moving the event
type. Its tool dependency is legal because scope stays test-owned. Factor the assembly hook's
coordinator invocation into an explicit-context overload in its existing test file; its attributed
native entry and roster behavior stay unchanged. S3 can exercise real hook composition virtually.
`tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj` gains a direct
library reference and retains its two existing links, which now contain only scope/hooks/roster.
The Antiphon.Tests build already builds/stages this host; no extra linked-host build is needed.

Additional exact edit scope: new `C/AssemblyInfo.cs`, `C/Cleanup/OutputCleanup.cs`, `C/Report/ReportWriter.cs` (D-4 only),
the three project files above, and `scripts/lib/checkpoint-owned-output.ps1`. The existing
`ICheckpointCleanupIo`, native I/O, creation registry and context need no new production primitive:
they already expose directory, stream, deletion, process observation and time operations. Preserve
their behavior. No edit to Program, RunState, process control, BuildConfinement, SourceSnapshot,
run-checkpoint.ps1 or the standalone PowerShell file-reader/process-runner implementations is needed.

## Slices and remaining commissions

| Group | Exact implementation/test scope | Closing rows and landing boundary |
|---|---|---|
| S2-pre | Thread the existing test scope/hook file as above; extend `T/Infrastructure/{HostCleanupCheckpointFixture,HostCleanupCheckpointCompatibilityTests}.cs` with the frozen 27 sweep + 11 scope results. No physical move. | Commit/push ports, then CP-29=54. A failed port must reach its production boundary and fail an assertion; compile/fixture errors are not red. |
| S2 | Perform the exact move/splits/forwarding/project/script changes above. Add `T/Infrastructure/HostCleanupCheckpointExtractionTests.cs`, `T/Scripts/CheckpointOwnedOutputScriptTests.cs`, `scripts/test-checkpoint-owned-output.ps1`. Existing supplemental test bodies may gain binding assertions in their already-owned files; native R-6 bodies remain unchanged. | CP-2/25/27/28/30/31/32/34/35 on Linux at committed moved SHA. S2-pre plus S2 is one independently reviewed/landed source slice. |
| S2-win | Same committed S2 source; verification only, no source fork or new implementation. | CP-33 on Windows; source-qualified receipt required before S2 land. |
| S3 | Retained daily adapter/registry and test-only completion footprint; shared gate/custody APIs and virtual scope/hook are supplied by S2. | Direct Code after S2: frozen CP-3/4/13; 80 floor unchanged. |
| S4 -> S7 -> S5 | Retained inventory/server graph (including `server/Antiphon.Server.csproj` reference), then runner contracts/store/outbox/transport, then scheduling/endpoints/auth. | Direct Code in that order: frozen CP-5, CP-10, CP-6. S5 must not precede the real S7 transport in the composed production graph. |
| S6a -> S6b | Maintenance includes existing interface/store permission in the retained plan; S6a can follow S4 when its land-service gate opens. Delivery requires **S5 + S6a + S7**, real outbox and fresh HTTP recipient. | Direct Code: frozen CP-8, then CP-7/9 (60 service results). |
| S8 -> S9 -> S10 | Retained worker/wrappers/packaging, Linux adapter, Windows adapter/wrappers. Worker consumes S3 registry and S7 receipt contract; no new mount. S9 can follow S3; S10 requires S8 plus S9's native interface. | Direct Code: frozen CP-11/12, CP-14, CP-15/16. Per-host dry-run and explicit activation remain separate operator gates. |

One retained S3 oracle needs precise interpretation now: “no ... kill APIs” in
`Shared_library_has_no_test_or_tool_dependency` excludes cleanup/session stop authority, not the
unchanged `CheckpointProcessRunner` finally block that reaps **its own spawned Git/build child**.
The custody plan explicitly requires that linked implementation. Check assembly references and
cleanup-policy call edges; retain the existing `sweep_never_signals_a_process` assertion and permit
only this existing owned-child boundary. Reject deleting its cancellation/reap code to satisfy a
blanket string scan. All original PC-58 and PC-138 obligations remain.

## Verification design

Frozen R-1a/R-1b names, arguments and oracles remain the authority for the 54-result class;
new regressions use separate classes. R-6 and V-14/15/16 retain their existing named cases.
The native defaults prove forwarding; virtual tests prove context isolation. Neither substitutes
for the other. No full Unit, assembly, checkpoint namespace, repeat, or fleet smoke (CARD-1287).

| New/bound witness | Exact test method and oracle | Guard/control binding, pending Mutation |
|---|---|---|
| V-17 naming | `HostCleanupCheckpointExtractionTests.Naming_policy_and_facades_preserve_admission`: literal matrix through shared predicate and both tool facades; positive `bin-x`, `bin-a._-9`, final-LF `bin-x\n`; negative null/blank, `bin`, `bin-`, `BIN-x`, slash/backslash/colon, `..`, rooted and Unicode names. Exercise valid/invalid root and stamp-parent admission through actual `CheckpointDelete`, with reached-observation witnesses. One result, not one per internal case. | PC-415 remove the shared segment guard (include `bin-a..b`); PC-416 bypass instance/static facade for an invalid name (two variants); PC-417 bypass each delete naming check (root/stamp variants). Assert literal booleans/refusal and zero unlink for invalid names. |
| V-18 context | `HostCleanupCheckpointExtractionTests.File_only_delete_uses_supplied_registry`: create/seal file through explicit context; same context removes it; fresh context over identical bytes refuses, retaining sentinel. Record reached FileOnly match/unlink and no native fallback. | PC-418 resolve native default instead of `new(context)` at FileOnly match: positive removal assertion fails. A3 regression, not a claimed repair. |
| V-19 closure | `HostCleanupCheckpointExtractionTests.Moved_types_have_one_library_owner`: enumerate the moved types in the table (exclude retained facades/scope/hooks), require one definition in the library, no definitions in executable, both exclusive partials co-located, shared core used by scope/hook, tool friend present, no test/tool assembly reference from library. Check csproj link and LifecycleHost library reference. | PC-421 add a compiling TUnit reference/use to the library: forbidden-reference assertion fails. Retain PC-58's later adapter witness too; do not count a duplicate-type compile failure as red. |
| V-20 standalone native source | `CheckpointOwnedOutputScriptTests.C987_StandaloneNativeSource`: fresh pwsh child, read only moved ExclusiveDirectory source, Add-Type with nullable enabled and no library DLL/reference; create a private absent child, require null/success, write sentinel, collide and require `pre-existing-output` with identical sentinel/directory identity; missing-parent create returns `output-create-failed:*`. On Linux verify 0700. | PC-419 map existing-directory error to success: collision assertion fails. Outer test host must compile; child results include an observed compile/create/collision outcome and assertions, not an unexplained fixture crash. |
| V-21 real helper | `CheckpointOwnedOutputScriptTests.C987_OwnedOutputHelper`: fresh pwsh from unrelated cwd, dot-source actual helper via absolute script path, create fresh output/owner marker, call again and require refusal with byte-identical marker/sentinel. Assert source loaded from new path; old source absent. | PC-420 restore obsolete helper source path: child records failed source-load outcome and TUnit's success assertion fails. This guards path resolution as behavior, not a textual self-comparison. |
| R-7 script callers | Existing `RunCheckpointScriptTests.C585_Green`, `C585_FreshResultsDir`, `C585_NoBuild`, `C589_SlotGranted`, unchanged, one result each. | Actual run-checkpoint helper path for fresh result/build creation, refusal, reuse and slot release; offline dotnet/slot shims only. |
| R-8 affected readers/callers | Existing project-contract 4, tool-copy 16, native scope 11, context-path 2, plus exactly two linked-host methods below. | Public source/project contract, facade composition, scope native default and real linked teardown/owned-child join. No new test edits or stale no-reference oracle. |
| R-9 sweep race | Existing `HostCleanupCheckpointSweepRaceTests.vanished_index_entry_does_not_abort_remaining_candidate` with four boolean pairs. | CP-30 preserves D-1's vanished-index behavior after move; PC-414 four variants stay pending. |

The two new script methods use `ScriptHarness.RunHarnessCaseAsync` and the new offline harness
above, Integration + `ParallelLimiter<ProcessSpawnLimit>`, bounded owned child/join, private scratch,
fixed named outcome inventory and fail-on-unexpected command. Use installed pwsh 7 on **both** OSes;
the helper already requires 7.0, so this is not a new PowerShell 5.1 promise. Linux emulation of
Windows flags cannot close CP-33. No live token, endpoint, provider, build or deletion target is
passed to the child; its native filesystem operations touch only fixture-owned scratch.

New PC-415..421 are execution designs, not results; Code must bind exact production lines,
variants, assertion labels and source SHA in its candidate. PC-1..413 and every frozen variant,
PC-414's four race variants, and these seven additions all remain pending SourceLanding Mutation.
No control is discharged or renumbered. New assertions use `C987.<method>.<variant>.<boundary>`.

Run the checkpoint tool against **this** manifest, task-bound, `--expected-source-sha` equal to
the committed source. Select CP-29 alone at S2-pre; select the nine Linux S2 rows explicitly at
the moved SHA; select CP-33 alone in the Windows lane. Wait to terminal exit (75 is still running).
Build reuse below is intentional: all references to CP-2 have identical `After=S2` and reuse its
certified output at the same SHA. The parent Antiphon.Tests build stages LifecycleHost, so CP-34
needs no unlisted build. One gated CLI bootstrap per needed source/lane is permitted setup and
must be reported separately; every other build/test follows the table and host slot gate.
Preserve raw CHECKPOINT/provenance/count lines, inspect exact TRX rosters and validate receipts.
Never edit source during a run. Report failed/not-run rows truthfully; clean only task-owned outputs.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-29 | S2-pre | `tests/Antiphon.Tests -> bin-c987-compat-premove/` | linux-compat-premove | `/*/*/HostCleanupCheckpointCompatibilityTests*/*` | R-1a, R-1b | all 54 frozen results at unmoved SHA; 0 failed/skipped | 54 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c987-compat-sweep/` | linux-compat-postmove | `/*/*/HostCleanupCheckpointCompatibilityTests*/*` | R-1a, R-1b | same 54 results at moved SHA; 0 failed/skipped | 54 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-25 | S2 | `tests/Antiphon.Tests -> bin-c987-move-existing/` | linux-moved-facades | `/*/*/(OutputCleanupTests*)\|(CheckpointReceiptSafetyTests*)\|(CheckpointShadowCustodyTests*)\|(SafeDeleteProcessBoundaryTests*)/*` | R-6 | 43 + 8 + 12 + 2 results; 0 failed/skipped | 65 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | S2 | `tests/Antiphon.Tests -> bin-c987-move-readers/` | linux-moved-readers | `/*/*/(PlanCoverageHandleTests*)\|(CheckpointConfinementLedgerTests*)/*` | R-6 | 16 + 16 results; 0 failed/skipped | 32 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-28 | S2 | `tests/Antiphon.Tests -> bin-c987-move-policies/` | linux-moved-policies | `/*/*/(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointReceiptTests*)\|(HostCleanupCheckpointAuthorityTests*)/*` | V-14, V-15, V-16 | 12 + 8 + 16 results; 0 failed/skipped | 36 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-30 | S2 | CP-2 | linux-sweep-race | `/*/*/HostCleanupCheckpointSweepRaceTests/vanished_index_entry_does_not_abort_remaining_candidate` | R-9 | all 4 argument pairs; 0 failed/skipped | 4 | 1 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-31 | S2 | CP-2 | linux-existing-callers | `/*/*/(CheckpointToolProjectContractTests*)\|(CheckpointToolCopyCleanupTests*)\|(CheckpointTempScopeTests*)\|(HostCleanupCheckpointPathTests*)/*` | R-8 | 4 + 16 + 11 + 2 results; 0 failed/skipped | 33 | 3 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-32 | S2 | CP-2 | linux-source-loader | `/*/*/(CheckpointOwnedOutputScriptTests*)\|(RunCheckpointScriptTests)/(C987_StandaloneNativeSource)\|(C987_OwnedOutputHelper)\|(C585_Green)\|(C585_FreshResultsDir)\|(C585_NoBuild)\|(C589_SlotGranted)` | V-20, V-21, R-7 | exactly 2 new + 4 existing methods; 0 failed/skipped | 6 | 3 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-34 | S2 | CP-2 | linux-linked-host | `/*/*/CheckpointTempLifecycleTests/(passing_test_runs_teardown)\|(early_assertion_still_joins_owned_child)` | R-8 | exactly 2 methods; 0 failed/skipped | 2 | 2 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-35 | S2 | CP-2 | linux-extraction-contract | `/*/*/HostCleanupCheckpointExtractionTests/*` | V-17, V-18, V-19 | exactly 3 named methods; 0 failed/skipped | 3 | 1 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-33 | S2-win | `tests/Antiphon.Tests -> bin-c987-source-loader-win/` | windows-source-loader | `/*/*/(CheckpointOwnedOutputScriptTests*)\|(RunCheckpointScriptTests)/(C987_StandaloneNativeSource)\|(C987_OwnedOutputHelper)\|(C585_Green)\|(C585_FreshResultsDir)\|(C585_NoBuild)\|(C589_SlotGranted)` | V-20, V-21, R-7 | exactly 2 new + 4 existing methods on Windows; 0 failed/skipped | 6 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost and completion

Estimated ordinary S2 floor: **75 minutes** (12 pre-move + 55 post-move Linux + 8 Windows),
six isolated Antiphon.Tests builds, 295 selected executions across the two source states/OSes.
Counts are source-derived expectations, not measured outcomes. Add 120-180 minutes authoring,
up to 5 minutes bootstrap per lane and actual slot waits; failures add their measured cost.
Build reuse avoids five additional builds; no measured runtime saving is claimed. No whole Unit
fallback. The earlier 57-minute S2 baseline remains historical; other slice estimates are unchanged.
The seven new mutation controls estimate 28 minutes at the retained four-minute-per-control
assumption, excluding additional variants/qualification cost; no mutation timing was measured.

S2 acceptance requires all eleven rows at their stated source/lane, exact 45-method/54-result pre/post roster,
native source smoke on both OSes, no reversed/duplicate assembly edge, and ordinary Review before
land. `git fetch origin master` then conflict-free `git merge-tree --write-tree origin/master HEAD`
establishes independent mergeability; never merge/rebase the task branch. Run evidence-diff with
the explicit task base and full pushed SHA, and leave generated payloads ignored.

No session waiting for input is introduced or changed. Parking remains disabled by default, so
there is no automatic input-wait release deadline (CARD-1083). Daily cleanup stays disabled and
non-destructive until the parent's namespace qualification and explicit operator activation gates.
