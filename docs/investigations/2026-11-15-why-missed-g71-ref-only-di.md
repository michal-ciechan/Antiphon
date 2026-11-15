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

## Exemption

None; this is the first note for this cause.
