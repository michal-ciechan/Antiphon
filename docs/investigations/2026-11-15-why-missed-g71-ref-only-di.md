# Why missed: g71 rolling-runner ref-only DI composition

## Introducing commit/card

CARD-0824 F1-F4 landed in Merge 232, task
`0704a004-807e-4bdb-937f-2e105981251d`, master tip
`fc92658cb88fb282455230b8e04595df78e2a58d`.
The introducing registration is `a077e8ef5932c8cafcfc6f3647a31df41c1c1242`
(original reviewed branch commit `f649a3b490ed206e39593600255010a82b475f2b`):
`AddDelegationWorktreeGraph` gained `RefOnlyDesktopProvisioner`, and the dispatcher
gained its optional provisioner dependency. The provisioner's required
`ITaskProgressGit` becomes required when DI constructs that registered service,
even with admission disabled. `PhoneHomeRollingRunnerTests.RollingWorld.Configure`
calls the shared graph and then removes every `ITaskProgressGit` registration.
That removal dates to `be0353a592d`; it became invalid with the new dependency.

At current origin/master `d100f8fa2c679a5c74b6b235087308776a8f5d50`, before any
repair, Linux reproduced the exact Windows exception in
`PhoneHomeRollingRunnerTests.Remote_prep_refuses_a_runner_that_began_draining_after_the_claim_check`:
1 executed, 0 passed, 1 failed, 0 skipped; host duration 29.216s.
The exception names `ITaskProgressGit` while activating `RefOnlyDesktopProvisioner`.
Build: 235.86s, zero errors, slot granted immediately. Red run: slot granted
immediately, held 32s. Load samples: `2.12 2.00 2.67` before build,
`9.54 11.06 6.69` after reproduction. Generated evidence is ignored under
`.antiphon/evidence/ac8b9d8e/` (baseline-build.log, red.log, red/*.trx, phase/).
The live Windows b7812833 g71 failures and previous 34d41b7e green are caller
evidence; neither live run was changed or rerun here.

## Production composition

`server/Program.cs` unconditionally registers scoped `RefOnlyDesktopProvisioner`
and singleton `ITaskProgressGit, TaskProgressGit`. It also registers scoped
`TaskWorkspaceContextResolver` and `DelegationWorktreeService`, singleton
`ILandingGit` and `IRepositoryMutationLease`, and the required options. The
provisioner is scoped with scoped dependencies; progress Git has singleton
dependencies. These registrations do not depend on `Delegation:RefOnlyDesktop`.
This is a fixture composition gap, with no missing production registration or
gate-on-only dependency. The server uses `WebApplication.CreateBuilder` and
does not explicitly override provider validation settings.

The repository census found scope validation in individual test harnesses but
no explicit `ValidateOnBuild` production-composition witness and no existing
test that resolves the production provisioner. The shared helper's eleven
root/DI cases resolve worktree services without resolving the provisioner.
Runtime production-container verification is recorded with the repair below.

## Approving reviews

Read-only task-detail results were retrieved for both reviews and Merge 232:

- Round 1 `3c766de7-9c99-4ef1-98ec-9a3cb8e9b259`, Final,
  `ordinaryScopeCompleted: None`, reviewed `17b511f68c636135bee82db6fec63f1b152f33df`.
  This review explicitly did **not** authorize land. It ran provision/preparation/
  producer lifecycle 17/17, other ref-only classes 56/56, nine-class guards
  80/80, two worktree-base and three settlement methods, and the statement-budget
  row (11 passed, 3 inherited failures). It did not run the rolling-runner class.
- Approving round 2 `b0ec9aab-d047-49b9-82be-750c8a904449`, Final,
  `ordinaryScopeCompleted: Full`, reviewed `3a8e115bb54847458f9495e82e1a9cc05642f039`.
  It ran `DelegationTestServicesTests` 11/11, layering 11/11 and smaller guards
  53/53 at the reviewed SHA. On the master compose it ran provision/identity/gate/
  ServerGitProcess/helper 51/51, ten ref-only regression classes 45/45,
  statement budgets 14/14, `RemoteWorkspacePreparerTests` 27/27, and the guard union
  (114 passed, 1 Windows-only skip). Its report records
  `reviewedSourceClean: false` and slot-gated direct runs, without checkpoint-tool
  receipts. Preserve those qualifications; they are not retrospectively Full
  source-clean receipts.
- Merge 232 ran the parent's CP-1..7 and other exact impacted rows, plus selected
  gate-off dispatcher (195/196, an attributed inherited flake), notification,
  slot, land, settlement and worktree classes. It did not run
  `PhoneHomeRollingRunnerTests`. Its derived suggestion list included this class;
  the committed Code report explicitly deferred suggestions beyond the named
  invariants to release qualification.

The recorded source selections and counts above are historical task reports,
not reruns or independent validation of their source-clean claims. Original
Code evidence is tracked at
`docs/superpowers/reports/2026-11-14-card-0824-code.md`; bounded round-2 filters are
in `docs/superpowers/plans/2026-11-14-card-0824-r2-checkpoints.md`.

## Outside the selection

`tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs` and its
Drain/Retire partial files declare the same Integration class. Its scenarios
exercise a real dispatcher through `RollingWorld`, which customizes
`BridgeQueueHarness`. This is a portable composition failure, unrelated to the
Windows-only producer witness. The gate-off selections checked other harnesses
whose shared progress-Git registration remained intact. Running helper tests
alone could not detect a caller removing a required registration afterward.

The registration census used `rg -l 'Add(Scoped|Singleton|Transient).*AgentTaskDispatcher'
tests -g '*.cs'` and `rg -n 'RemoveAll<ITaskProgressGit>' tests -g '*.cs'`.
Dispatcher fixtures use the shared graph directly or recognized
`LandingSafetyHarness` / `LandingProtocolHarness` owners. The separate
`Antiphon.ChannelOutbound.Probe` has a hand-built graph without the provisioner
registration, so it has no newly unresolved provisioner. The only removal
without replacement was `RollingWorld`. `AgentTaskLandAdmissionControlledTests`
and `ReviewEvidenceRecoveryEndpointTests` replace it with fixture progress Git;
`LeaseSpanRecorder` replaces it with a recording decorator (consumer:
`DispatcherLeaseSpanTests`). No other class was found with this DI gap.

## Cheapest pre-land check

`server/Application/Services/AgentTaskDispatcher*.cs`,
`server/Application/Services/RefOnlyDesktopProvisioner.cs`, and
`tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs`
-> `/*/*/PhoneHomeRollingRunnerTests/*`.

The one-method red filter above is the cheapest witness (29.216s including
discovery/setup). The full class is the safer pre-land row: it also covers drain,
redirect, release and retirement assertions. Use bounded method groups if host
load requires splitting. Add a single impacted-classes kind for this dependency
edge and the production-composition/helper witnesses; do not expand the Unit lane.

## Repair and verification

The rolling fixture remains on the shared graph and replaces its removed progress
seam with the existing in-memory fake seeded from the same synthetic commit as
`PushGit`. At candidate `5b657e19a0a54ef41329604b502dc7a10cb4d7ae`, the new
production-host witness passed: `ValidateOnBuild=true` and `ValidateScopes=true`,
production `TaskProgressGit`, scoped provisioner and dispatcher resolution, and
root-scope rejection all succeeded (1/1, host 29.237s). No production registration
changed. There was no previous equivalent production-composition witness.

The first rolling-launch group then passed 2/3 and failed the late-drain case:
its hand-seeded Code task had no `RepoPath`, and enabling progress capture made
that fixture fail `repair_source_identity_unavailable` before remote preparation.
The repair gives `SeedQueuedAsync` the same repository path as its working
directory. This is fixture data, with every assertion and timeout preserved.
The original DI red and this subsequent assertion red remain separate evidence.
The repaired source requires the full bounded verification recorded below.
Mutation is operator-waived.

At `82451517f8a8f079933a16b60f38a1653c5b6162`, all 34 rolling cases, all
17 ref-only classes (77 passed, 3 Windows-only skips), 18 helper/census cases,
35 replacement-fixture cases, and the guard union (114 passed, 1 Windows-only
skip) passed. The 50-case selector row passed 49 and exposed one introduced
selection regression: the extra `server/Program.cs` glob in the new kind also
selected it for the historical telemetry S1 range. Remove that extra glob;
retain the requested dispatcher/provisioner paths and shared-helper path.
The selector's exact assertion is preserved. Recheck the failing selector
method, table audit and derived-index guard after this table-only repair.

### Final verification

The complete guard union and selector class were rerun after the mapping repair
at `89424627151a29051b4c50fbf0cac58b91b2f658`: 114 passed / 1 Windows-only
skip and 50/50 passed, respectively. The original selector assertion was not
changed. Table audit passes, and all eleven requested guard classes occur in
the final TRX. Layering output is exactly
`VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

The following are the qualified runs; historical failures above remain separate.
Counts are actual TUnit results, not internal assertion counts.

| Row | Source | Passed | Failed | Skipped | Outer wall seconds |
|---|---|---:|---:|---:|---:|
| Production composition | 5b657e19a0a54ef41329604b502dc7a10cb4d7ae | 1 | 0 | 0 | 34.102 |
| Rolling launch / late drain | 82451517f8a8f079933a16b60f38a1653c5b6162 | 3 | 0 | 0 | 35.405 |
| Rolling rebind | same fixture source | 11 | 0 | 0 | 37.176 |
| Rolling operations | same fixture source | 20 | 0 | 0 | 38.997 |
| DelegationTestServices + harness census | same fixture source | 18 | 0 | 0 | 27.047 |
| AgentTaskLandAdmissionControlledTests | same fixture source | 24 | 0 | 0 | 43.803 |
| ReviewEvidenceRecoveryEndpointTests | same fixture source | 6 | 0 | 0 | 56.846 |
| DispatcherLeaseSpanTests | same fixture source | 5 | 0 | 0 | 39.220 |
| Ref-only provision/preparation/identity/gate | same fixture source | 23 | 0 | 0 | 61.664 |
| Ref-only lifecycle/sync/debt/continuation | same fixture source | 19 | 0 | 0 | 93.131 |
| Ref-only read/deliverable/consumer/receipt | same fixture source | 11 | 0 | 0 | 74.365 |
| Ref-only landing/cleanup/boundary | same fixture source | 20 | 0 | 0 | 98.114 |
| Ref-only snapshot migration/native | same fixture source | 4 | 0 | 3 | 84.479 |
| Required eleven-class guard union | 89424627151a29051b4c50fbf0cac58b91b2f658 | 114 | 0 | 1 | 238.358 |
| ImpactedClassesSelectorTests | same final mapping source | 50 | 0 | 0 | 285.312 |
| **Total** | **15 qualified rows** | **329** | **0** | **4** | **1248.019** |

The rolling split covers all 33 declared methods and 34 argument-expanded cases.
All 17 declared `C0824RefOnly*Tests` classes appear in the five ref-only rows.
Three skipped cases are the Windows-only producer witnesses in
`C0824RefOnlyNativeTests`; the fourth is
`C1210_Windows_linux_only_outcomes_are_exact`. No Windows acceptance is claimed.
The final mapping change touches only `tests/impacted-classes.json` and this note;
fixture C# is identical to its passing source. Production C# and the production
composition witness are identical to the earlier passing production source.

All builds and tests used `scripts/build-slot.ps1`, native default output and
`UseAppHost=false`. Test rows ran serially with `TUNIT_MAX_PARALLEL_TESTS=4`,
`dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false`, the
exact filters in `.antiphon/evidence/ac8b9d8e/run-rows.ps1`, `--report-trx` and
an outer `--timeout 9m` ceiling. Existing assertions and test timeouts have zero
diff. No row exceeded ten minutes; the longest was the final selector at
285.312s. Every completed build/test lease was granted immediately and released.
These are direct Debug runs, not checkpoint-tool receipts or a Review verdict.

Native test builds: baseline 235.86s, first fix 210.57s, seed repair 219.12s,
mapping repair 229.75s; each had zero errors (2094 existing warnings). The extra
build/selection after each repair is attributed to that changed source, not an
unchanged retry. The syntax-index driver was built first, 7.81s, zero errors;
generation printed changed then unchanged and was committed separately. After
the mapping correction, both further generations printed unchanged. The three
historical extra rows are retained: first rolling launch 2/3, 38.793s; first
guard union 114 passed / 1 skip, 236.318s; first selector 49/50, 250.248s.
The 18 recorded executions total 1773.378s, excluding the original baseline red
and builds. Costs were not erased by the fixes. These are individual loaded
samples, not calibrated p95 measurements. CARD-1906 load was recorded per row;
an observed build-time one-minute load sample reached 28.14.

An initial task-owned outer PowerShell wrapper was stopped and joined with exit
143 before any child test/build-slot request: native wildcard expansion scanned
the filesystem instead of forwarding the filter. Its replacement uses
`ProcessStartInfo.ArgumentList`; no stopped attempt is counted as test evidence.
A read-only selector invocation with the wrong parameter name and a hash-helper
invocation missing its common import were corrected; neither ran tests or earns
test credit.

Generated evidence remains ignored in `.antiphon/evidence/ac8b9d8e/`: per-row
TRX/HTML, `*-run.json` with source/image/filter/load/elapsed time,
`row-summary.json` with every case and duration, build/slot logs, historical task
reports and phase journals under `phase/`. Fixture/DB/process phases are retained
where journaled; unavailable external-call breakdowns remain unknown. Metadata
binds the final guard/selector assembly to SHA-256
`DFCCC99FDBA04E3FB00E950F3F8EC0E383608AF59A716EFDFB14890619EEBD7E`.
The fixture assembly at its passing source is
`33562632BB97B5008E9272DE4FD1346A69680FA3834C77751E0170222A9DA8F1`;
the production witness assembly is
`D5194F8ADB32A9D5CAF0C6B8ACEE573DCC70B0216E54D09DBEDBF59641E335D9`.

Verified pins remain unchanged:

- policyHash: `b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d`
  (recomputed with the canonical helper).
- PinnedInputDigest: `26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`
  (printed by the existing read-only digest mode).
- LegacyExclusionDigest: `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`
  (unchanged constant, validated by the passing native-policy guard).

There is no diff in production source, `tests/test-execution-policy.json`,
`scripts/test-live-lane-run.ps1`, or `tests/Shared/TestTiming.cs`. No host was
restarted and neither live Windows qualification was changed. Mutation remains
operator-waived. The work proceeded under the brief's standing authority.

### Published slices

- `dc802819b20ea690222ec126c5c3bd1a04cd0039`: current-master DI red and why-missed record.
- `75f476ea39ed1edfff1ad3ff317b1b2059cc2fff`: fixture progress fake, production DI witness, impact row.
- `5b657e19a0a54ef41329604b502dc7a10cb4d7ae`: separately regenerated derived index.
- `82451517f8a8f079933a16b60f38a1653c5b6162`: complete repository identity for seeded Code tasks.
- `89424627151a29051b4c50fbf0cac58b91b2f658`: restrict the mapping and preserve the selector assertion.

The later report-only commit contains this completed verification ledger; its
full pushed SHA is in the caller progress marker. At final executable source,
freshly fetched origin/master remained
`d100f8fa2c679a5c74b6b235087308776a8f5d50`; merge-tree exited 0 with tree
`03844172b5f1dd8c6a25bcad136a45ab5061563f`. Publication checks over the completed
report commit are recorded in the caller report. Next stage: Merge, test-only fix.

## Exemption

None; this is the first note for this cause.
