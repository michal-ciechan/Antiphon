# CARD-1020: await disposal of test-owned PtyHosts

Plan stage, 2026-10-03. Source examined: `2d3c582416c5610d72e53df3e790bc114d87ad82`.
Task: `76611588-7e62-405c-95e7-0d4364e75efc`. Plan only; no implementation or native runs performed.

## Outcome and acceptance

Fix `DirectSessionRunnerClient` teardown in the test assembly. With its default
`KillOnDispose = true`, completion of disposal must mean its captured PtyHost,
session child, and Windows OpenConsole processes have exited. Keep a short normal
shutdown opportunity, then kill and await only positively identified test-owned
survivors. The production runtime continues to detach on disposal so sessions can
survive runner restarts.

The leak is still actionable, but the card's 24-hour explanation is inaccurate for
this helper. Its existing override is 0.02 hours (72 seconds). Waiting for that TTL
does not meet the disposal contract, and requesting host exit is not proof of OS
process exit. This plan corrects that premise without treating the leak as resolved.

Acceptance is a real Windows ModernConPty process-lifetime assertion after client
disposal, including both argument cases of `RunnerGrokAdapterReadyTestsPty`.
Successful prompt delivery remains independently asserted. No production restart,
provider turn, admission rule, inbox qualification, or process-wide orphan sweep is
part of this card.

## Card and source ground truth

Read `card.ps1 get/history CARD-1020 -Board Antiphon` and its HTTP thread on
2026-10-03 around 23:08–23:12 UTC. History has one move revision, for this Plan
dispatch; no terminal verdict, prior plan, or linked fix commit. The thread's
CARD-1011 qualification report `2299fe11` independently records three owned
PtyHost/OpenConsole pairs left after its Windows CP-3 at `749e73f1c`, including
the two `c1004-pty-*` roots used by the reported Grok test. Its passing delivery
counts did not establish cleanup. These are prior reports, not runs repeated here.

| Card assumption | Code at the examined source | Consequence |
|---|---|---|
| The helper uses the host's 24-hour default. | `src/Antiphon.PtyHost/PtyHostOptions.cs:26,54` defaults to 24 hours, but `tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs:114-119` sets `PtyHostLingerHours = 0.02`. `SessionRunnerRuntime.cs:2271-2278` passes it to the launcher as the host linger argument. The override dates to `f3660a80dc2d5edaff0d93d4c2c1ca0fb474177a`. | Do not add an override that already exists or lower the production TTL. Record actual launch arguments in Windows evidence. |
| Disposal kills every child. | `DirectSessionRunnerClient.DisposeAsync`, lines 397–418, kills only Running/Starting sessions, catches failures, then disposes the runtime. It never waits for a host PID. | Include already-exited hosts and confirm physical exit before the test client finishes. |
| Shutdown is never sent, or the host ignores it. | `RunnerSession.KillAsync` at `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:3032-3075` waits for `_exited`, not host exit. `HandleExited` at 3207–3253 completes `_exited` and schedules `ShutdownHostAsync` with `Task.Run`. `RunnerSession.DisposeAsync` at 3176–3197 nulls `_client` and closes the pipe. `ShutdownHostAsync` at 3354–3375 returns if `_client` is null or logs a send failure. | A missed-ack race is present: disposal can beat the scheduled shutdown. The code establishes the race and missing wait; it does not prove the exact interleaving of the historic processes. |
| Pipe disconnect should end the host. | `src/Antiphon.PtyHost/PtyHostServer.cs` deliberately accepts another runner after disconnect. `HostSession.cs:375-447` starts linger after child exit; `Shutdown` at 336–341 requests exit. `Program.cs` then disposes the host session. | Disconnect is intentional detach, not a cleanup primitive. |
| A TTL or shutdown log proves process exit. | `HostSession.DisposeAsync` awaits runner teardown; `PtyAgentRunner.DisposeAsync` awaits its read task before disposing the native connection. `ModernConPtyConnection.Dispose` closes the pseudoconsole and job. These are later than the exit request. | Observe actual host and descendant process handles, not only terminal DTOs, manifest deletion, or log messages. Do not claim a 24-hour measured lifetime from the available logs. |
| Production disposal can simply become kill-on-dispose. | Runtime disposal intentionally detaches; `tests/Antiphon.SessionRunner.Tests/PtyHostAdoptionTests.cs` pins restart/adoption. The helper also has explicit `KillOnDispose = false` and `SimulateRunnerRestartAsync`. | Confine the fix to test ownership; preserve explicit detach/restart behavior. |
| An existing cleanup sweep covers this test. | `PtyHostLeakSweep` belongs to the separate SessionRunner test assembly and its root roster does not cover `c1004-pty-*`. `SessionQueueReceiptPlumbingTests.ReleasePtyHostAsync` is a separate fixture-local workaround. | Implement the contract in the shared direct test client; do not add another global sweep or copy PID-only killing. |
| A related landed card already satisfies this. | CARD-0691 is in Review; its plan S5/S6 and ADR host-lifetime paragraph describe pending owner-watch/bounded-exit work. This checkout has no `PtyHostExitWithOwner` setting or owner-watch arguments, and the helper still has the disposal gap. | CARD-0691 is related, not evidence of completion. Recheck its ownership before Code because its proposed test changes overlap. |

## Decisions

- **D-1 — Fix test-owned cleanup, with no production lifetime change.** Add a small
  concrete helper within `Antiphon.Tests` and use it from `DirectSessionRunnerClient`.
  This satisfies the reported resource contract without changing runner restart,
  verification-custody, host protocol, or session-kill semantics. Rejected: making
  runtime `DisposeAsync` kill everything; that breaks the defining detach contract.
- **D-2 — Retain ownership before teardown races can erase it.** Capture host PID,
  actual process start time, session ID, private manifest/shadow-copy root, and
  process handles after a successful PtyHost launch/adoption. Keep records for
  replaced/exited generations until they are disposed. Before cleanup, supplement
  from this client's tracked sessions and their own manifest paths, including
  partially started sessions. Never enumerate unrelated temp roots. Capture the
  session child and Windows console descendants while the owned host is alive;
  the test project already references `System.Management`. Rejected: looking up
  only a manifest after Kill, a global process-name census, or using PID alone.
  A manifest can disappear before native teardown finishes, and PIDs can be reused.
- **D-3 — Normal stop, bounded wait, owned fallback, then runtime disposal.** For
  default disposal, take the ownership snapshot first, run the current bounded
  session kills while their pipes remain connected, then await owned host exit.
  Give normal shutdown up to two seconds; kill surviving owned host trees once and
  await the captured host/child/console identities within a further five seconds.
  Bound the session kill call itself with a cancellation deadline rather than
  relying on its timeout argument to bound pipe I/O. Use one remaining deadline
  per phase, not a fresh allowance per poll. If the host has gone but an already
  captured descendant survives, only that same verified descendant may be stopped.
  Always attempt runtime disposal and release process handles in `finally`, even
  after a kill failure. Surface cleanup failures with identities and elapsed time;
  do not quietly report successful cleanup. Preserve primary test-failure evidence
  when also reporting cleanup errors. Rejected: blind delay, shorter linger, or
  fire-and-forget process kill. The existing 72-second TTL remains a backstop.
- **D-4 — Keep explicit ownership transfer.** `KillOnDispose = false` disposes only
  the connection and local observation handles; the host and child stay adoptable.
  Preserve the current behavior of `SimulateRunnerRestartAsync`, including its
  flag setting. Herdr sessions get their existing teardown and no PtyHost fallback.
  Make repeated disposal idempotent. Exclude verification-bound executions from
  this ordinary test-host fallback; their existing custody path retains authority.
- **D-5 — Observe processes independently in tests.** Capture witness process
  handles before disposing the client, require those processes to be alive first,
  and assert exit immediately after disposal returns. On Windows require a real
  ModernConPty backend and at least one positively attributed OpenConsole witness.
  A process-name match, empty census, or manifest absence cannot satisfy the test.
  Assert the fixture's shadow-copy/temp tree can be deleted after releasing its
  own observer handles. Emergency cleanup runs after the assertions, in `finally`.
- **D-6 — Keep native evidence focused.** Use FakeGrok/native shells and unique
  roots, with the assembly-local `ParallelLimiter<ProcessSpawnLimit>` and Integration
  category. Test identity predicates separately as Unit tests. No paid provider,
  credentials, production runner, or legacy inbox arm. CARD-1023's allow-by-default
  compatibility rule is unchanged; this card adds no version/capability admission gate.
- **D-7 — Verification remains a separate stage.** The brief requests verification
  and checkpoints but does not fold TestDesign into Plan. The proposed design below
  is the input to TestDesign, which freezes the roster, controls, and exact bounds
  before Code. These design choices require no outstanding product decision.

## Implementation slices

### S1 — Make the test client's ownership contract real

Files:

- `tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs`: integrate capture
  at launch/adoption, keep ownership across simulated restart, and implement the
  ordered/idempotent default teardown. Preserve explicit detach and non-PtyHost paths.
- `tests/Antiphon.Tests/TestHelpers/TestOwnedPtyHost.cs` (new): concrete per-instance
  process identity/capture/wait/kill helper; no production interface or static registry.
  Validate own root/session/generation before process control. A process access error
  is an unresolved cleanup result, not proof of death or permission to kill a stranger.
- `tests/Antiphon.Tests/Agents/DirectSessionRunnerClientDisposalTests.cs` (new): the
  six Integration methods in the verification roster below.
- `tests/Antiphon.Tests/Agents/TestOwnedPtyHostIdentityTests.cs` (new): one Unit method
  with four explicit argument rows for own identity, foreign root, foreign session,
  and stale process generation.

Commit and push S1 before CP-1. Do not generalize or replace other assemblies'
cleanup frameworks. Production failed-launch cleanup already exists; only retain
test-owned survivors visible through the client's own launch/runtime records.

### S2 — Pin the originally reported caller

File: `tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs`.
Keep both dashboard-marker cases and all readiness/transcript assertions. Give the
client/adapter explicit nested scopes so adapter disposal precedes client disposal;
after both scopes, assert independently captured host/child/Windows console exit and
successful cleanup of the test's own root. Replace the unverified comment that the
client kills every child with this executable postcondition. Retain diagnostic logs
on failure and ensure assertion failure still reaches owned emergency cleanup.

Commit and push S2 before CP-2 through CP-4. Amend this plan only if the final roster
changes; no generated evidence is committed. No other source files are authorized
by this plan without explaining the specific new dependency.

## Routing, collision notes, and activation

Read live `GET /api/runner-defaults` and `GET /api/session-runners` at 23:08:59 UTC.
Defaults revision 2 has a global preference, no per-kind overrides, and no unresolved
references. The catalogue had one available/eligible Windows lane and one
available/eligible Linux lane; another entry was unavailable and draining. This is
a dated observation, not a fleet address or scheduling reservation. Resolve again
at dispatch. Omit `-Runner`; omit `-Platform` for Plan/TestDesign and portable work.
Request `-Platform Windows` only for Windows native checkpoints. `-Platform Any`
clears an inherited platform pin. Lane names are in the CP groups and mapping below.

The board-scoped live task read at 23:09 UTC corroborated the brief's active work:

| Work | Collision disposition |
|---|---|
| CARD-1011 Code `b657a1e2`, orchestrator bundle/provider docs/routing tests | No planned edit to its files. Both cards may exercise FakeGrok. Recheck its final file list for `RunnerGrokAdapterReadyTestsPty.cs` and the shared helper before Code; defer any same-file work, and preserve its delivery assertions when landing. |
| CARD-0959 TestDesign re-freeze `4170230f`, runner Codex probes | Production runner/probe code is outside this plan, so no current source collision or activation dependency. |
| CARD-1008 Review `ca3d4a6b`, deployment scripts | No source collision: neither `scripts/deploy-server2.ps1` nor `scripts/c590-remote.sh` changes. Respect active rollout/drain and build-slot availability when scheduling tests. |
| CARD-0691 pending S5/S6 | Its planned changes include this direct helper and broader test-host cleanup. Reconcile ownership and scope before Code. If those changes land first, retain the missing disposal postconditions and drop any duplicate helper implementation; an owner-death watch alone does not prove that an in-process client has disposed. |
| CARD-1022 inbox deprecation / CARD-1023 compatibility | Follow the operator decisions already given. Add neither inbox qualification nor global version floors; do not fold those implementations into this card. |

Activation order: Plan publication → TestDesign freeze → S1/S2 Code and named
portable checkpoints → exact-source Windows native checkpoints → regression-only
Review → ordinary landing. This is test-only behavior, activated by the next test
assembly build; no server, runner, AppHost, or shared backend restart is required.
Windows rows must execute on the same candidate source as the review; record the
actual OS, host binary/build provenance, backend and ConPTY version. A later
production lifecycle change belongs to CARD-0691 and its own activation procedure.

Rollback is a normal revert of this card's test-only changes. Reverting restores
the leak risk; do not claim it as an accepted long-term cleanup state.

## Planner verification proposal (superseded by the freeze below)

Proposed roster for TestDesign to freeze. All named new methods are single-result
tests except the four explicitly parameterized identity cases. Windows witnesses
are platform-conditional assertions inside portable lifecycle tests, not skipped
Windows-only tests counted as portable coverage.

| ID | Test method | Required observation |
|---|---|---|
| V-1 | `DirectSessionRunnerClientDisposalTests.Dispose_awaits_exit_of_its_running_host_and_descendants` | Launch FakeGrok with the direct client; prove host/child and, on Windows, OpenConsole alive; dispose the client; every captured identity is exited before disposal returns, and its private tree is deletable. This is the card's explicit host-outlives-client regression. |
| V-2 | `DirectSessionRunnerClientDisposalTests.Lingering_exited_host_is_reaped_before_the_cleanup_returns` | Create a real detached host through `PtyHostLauncher` and a raw `PtyHostClient`, launch a native shell, capture ownership, attach and kill the child, observe Exited, then disconnect without sending Shutdown. No runtime auto-ack is installed. Prove host alive in linger, invoke the same owned cleanup primitive used by disposal, and require physical exit well before its 72-second TTL. This deterministically exercises the fallback without a production test hook. |
| V-3 | `DirectSessionRunnerClientDisposalTests.Dispose_with_kill_disabled_leaves_the_session_adoptable` | Set `KillOnDispose = false`; host and child remain alive, a new direct client on the same private root adopts and accepts input, then its ordinary disposal removes them. |
| V-4 | `DirectSessionRunnerClientDisposalTests.Dispose_does_not_touch_another_clients_host` | Two live clients have distinct roots and captured identities. Dispose A; A is gone while B still accepts a nonce. Dispose B in finally. |
| V-5 | `DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent` | Dispose twice; both calls finish, no stale process action, and no owned survivors. |
| V-6 | `DirectSessionRunnerClientDisposalTests.Body_failure_still_reaps_the_owned_host` | A controlled body exception crosses the await-using boundary; preserve its sentinel and assert owned processes are gone after catching it. Emergency cleanup is later than the assertion. |
| V-7 | `TestOwnedPtyHostIdentityTests.Identity_requires_the_owned_root_session_and_process_generation` | Four argument results: exact owned identity accepted; foreign root, foreign session, and changed start time rejected. Root comparison respects path boundaries and platform casing; identity failures never authorize process control. |
| R-1 | `RunnerGrokAdapterReadyTestsPty.Fake_dashboard_marker_reaches_ready_and_complete_first_prompt` | Both false/true marker cases still produce the complete UserPrompt; both also independently assert disposal removes their host tree. Windows host logs must say ModernConPty with no fallback. |
| R-2 | `HerdrLabelFollowWireTests` | Existing four results remain green, including the direct clients using explicit detach. Covers the helper's non-PtyHost branch without requiring a real Herdr installation. |

Do not select `SessionMessageQueueGrokPtyIntegrationTests` as a ModernConPty
regression here: its `PinnedBackend` constant at line 131 is `"inbox"`, overriding
the environment. Migrating that existing coverage belongs to CARD-1022. R-1
already exercises the affected helper through a real fake-provider PTY path.

Positive controls for the later Mutation stage are method-scoped. PC-1 disables the
default helper's stop/reap branch while retaining runtime detach; V-1 must fail at
the physical host-survived assertion. PC-2 removes the owned fallback kill in the
deterministic raw-host linger fixture; V-2 must fail on a live host, not startup or
fixture failure. PC-3 ignores `KillOnDispose = false`; V-3 must fail on survival or
adoptability. PC-4 removes generation validation; V-7's stale-generation argument
must fail. Each control retains an independent, identity-scoped finally cleanup.
Freeze exact mutation lines and assertion labels in TestDesign; zero execution,
missing binaries, build failure, or waiting out linger is not a successful control.

Record the source SHA, launch/kill/disposal/host-exit times, exact linger argument,
host log ending, host PID/start time and console identities in ignored evidence.
The Windows run distinguishes a dropped shutdown from a host stuck after shutdown:
if any host still survives the owned fallback, report its OS state and final log
phase for CARD-0691 triage instead of extending the TTL/wait or silently broadening
this card into production lifetime repair. The prior reports do not establish
which of those phases accounts for every historic leak.

### Execution and review rules

- CP-1 and CP-2 are the **portable Linux lane**; CP-3–CP-4 are the **Windows
  ModernConPty lane**. The importer has no Lane column; do not add an unsupported
  header. Select the appropriate rows explicitly on each lane. Windows evidence
  cannot be substituted with Linux success.
- Bootstrap the checkpoint tool through `scripts/build-slot.ps1` to its own
  `bin-c1020-tool/` output if needed; report that one infrastructure build explicitly.
  Use the checkpoint tool `run --plan <this-plan> --rows <lane-rows>
  --expected-source-sha <committed-sha>` from its prebuilt output. Await every run,
  continuing `wait` while it returns 75; never settle with an executor in flight.
  Rows acquire their own build slots; do not wrap them in a second slot lease.
- Linux fake-process rows need generated apphosts. Use an imported ignored manifest
  with `build.properties.UseAppHost: "true"`, as permitted by the checkpoint tool,
  before running CP-1/CP-2, using `run <manifest-path> --rows <lane-rows>
  --expected-source-sha <committed-sha>` (a positional manifest path replaces
  `--plan`). Windows uses its ordinary apphost build. Keep filters, rows and plan
  provenance identical when adding this property.
- Use separate `bin-c1020-*/` outputs with forward slashes. Commit/push each slice
  before its run and freeze source while it runs. All rows are serial, and all
  spawners retain the assembly limiter. No full assembly/namespace sweep is needed.
- For each CP report actual expanded executed/passed/failed/skipped counts and the
  unedited CHECKPOINT line, SHA, slot, clean-source and build-provenance fields.
  Minimums are result floors, not time estimates or internal assertion counts.
  CP-1/CP-3 require ten results; CP-2 requires six; CP-4 two. Total ordinary
  cost estimate is 24 minutes including four isolated builds, excluding slot wait.
- Review judges introduced regressions. Confirm a suspected inherited failure with
  its exact failing filter on the base commit; preserve original counts and do not
  loosen assertions, add retries, or enlarge a deadline. A missing Windows run is
  unperformed evidence, not a passing or invented regression verdict.
- Run `scripts/check-evidence-diff.ps1` across the full candidate range and perform
  the owner-required plan coverage lint once tests exist. Do not commit TRX, logs,
  JSON, manifests, or checkpoint directories. Remove the task's alternate outputs
  after process-exit evidence is collected; output deletion must not be achieved
  by sweeping unrelated PtyHosts.

### Planner checkpoint proposal (superseded)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1020-linux-lifecycle/` | linux-lifecycle | `/*/*/(DirectSessionRunnerClientDisposalTests*)\|(TestOwnedPtyHostIdentityTests*)/*` | V-1–V-7 | all 10 listed results, 0 failed/skipped | 10 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-linux-callers/` | linux-callers | `/*/*/(RunnerGrokAdapterReadyTestsPty*)\|(HerdrLabelFollowWireTests*)/*` | R-1, R-2 | all 6 listed results, 0 failed/skipped | 6 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-windows-lifecycle/` | windows-modern-lifecycle | `/*/*/(DirectSessionRunnerClientDisposalTests*)\|(TestOwnedPtyHostIdentityTests*)/*` | V-1–V-7 | all 10 listed results with Windows console witnesses, 0 failed/skipped | 10 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-windows-grok/` | windows-modern-grok | `/*/*/RunnerGrokAdapterReadyTestsPty/Fake_dashboard_marker_reaches_ready_and_complete_first_prompt` | R-1 | both marker cases, 0 failed/skipped | 2 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

## Verification design

TestDesign freeze, 2026-10-04, task `b01f2e7b`. Inspected checkout
`d87d195be33666c3bfeb61ee907da7647f045e2f`. This section supersedes only the
planner's verification proposal, roster, run notes and checkpoint table; D-1–D-7
and the fix design remain intact. No builds, test runs, native process experiments
or mutation runs were performed during this documentation stage.

### Inspection

Bodies read, not just test names:

- `DirectSessionRunnerClient.cs`: constructor, Start, Adopt, simulated restart,
  mapping and Dispose; `SessionRunnerRuntime.cs`: Kill, HandleExited, ShutdownHost,
  runner-session Dispose | default/detach/restart, lost ack, early exit,
  custody/backend exclusions -> V-1–V-6, V-8–V-13.
- `RunnerGrokAdapterReadyTestsPty.cs`: complete original method; CARD-1011's
  additional methods and helpers at `9567afa32de09cb956ed478b612dcffbd1af8772`
  | two marker arguments, full UserPrompt, actual modern backend -> R-1.
- `HerdrLabelFollowWireTests.cs`: all three methods, four argument-expanded
  results; `HerdrLabelFollowHttpFixture.cs`: start, fake server, isolated DB,
  adoption, monitor and disposal | explicit detach/non-PtyHost -> R-2, V-13.
- `PtyHostAdoptionTests.cs`: running restart and exited-host adoption bodies;
  `PtyHostLeakSweep.cs`: root registry, teardown, sweep and ownership test;
  `SessionQueueReceiptPlumbingTests.cs`: ReleasePtyHost, DisposeCore and delete
  bodies | restart -> V-3; PID-only sweep and fixture-local workaround excluded.
- `PtyHostLauncher.cs`: launch, apphost/shadow copy, intermediary and cancellation
  handling; `HostSessionPipeTests.cs`: launch, detached replay and linger methods;
  `HostSession.cs`: shutdown, child exit and linger | nearest fixtures for the new
  raw-host case V-2. Existing in-process HostHarness cannot witness OS host exit.
- `tools/Antiphon.Checkpoints/Cleanup/ProcessIdentity.cs`: Capture/Observe,
  generation and unknown/dead distinction | nearest identity fixture/practice for
  new helper tests V-7, V-11; do not add a dependency on the checkpoint executable.
- `Antiphon.Tests.csproj`, `TestAppHostPath.cs`, `ProcessSpawnLimit.cs` | apphost
  staging, private roots and assembly limiter -> all native cases. Linux already
  stages the owned PtyHost and FakeGrok/FakeClaude apphosts in inner builds.
- `PlanTableImporter.cs`, `ManifestValidator.cs`, `AfterSelector.cs` and
  `CheckpointImportTests.cs` import/escaping/roster/environment bodies;
  `docs/testing-and-build.md` checkpoint schema/runner/slots | import audit below.
  Owner policies read: project context, ops HTTP, orchestration stage/ownership,
  session runtime restart/delivery invariants, ADR-0002 host lifetime/native policy.

**Live ownership and footprint reconciliation.** Board-scoped task enumeration,
task detail and fetched branch diffs were read around 00:00–00:04 UTC on 2026-10-04.
Null API scopes are not evidence of disjoint files. These are observations, not
reservations; recheck the same branches at Code admission.

| Work and observed tip | Disposition |
|---|---|
| CARD-1011 Code `d422c5a9`, Dispatched, `9567afa32de09cb956ed478b612dcffbd1af8772` | Its full inherited branch includes **the same** `RunnerGrokAdapterReadyTestsPty.cs`, plus C1011 qualification helpers. Its latest continuation changes docs/bundle/checkpoint documentation. S1 has no overlap; serialize S2 against CARD-1011 publication. Preserve all C1011 additions when applying S2. CP-2/4 deliberately select only the original marker method, excluding both C1011 backend and paid-canary methods. Do not merge entire old file copies. |
| CARD-0959 Code `bd02f8d9`, Dispatched, `7899fee8f777f020475696e0bf59262755016289` | Observed runner Codex settings/probes, server observation/routing, docs and their tests; no direct helper/original marker file edit. No current same-file collision. |
| CARD-1018 Code `13149836`, Succeeded, `936763a0463246d7d873d6198660258f5bfd15c1` | ProviderSignInRequiredException and ProviderSignInRequiredCreateTests plus report; no collision. |
| CARD-1013 Code `2c35a27d`, Dispatched, `df6c76bcd0ba91b994d1d34ca1fd5e20984d3d5c` | Coverage reader/parser/command tests, `.gitattributes`, testing docs; no source collision here. Preserve its LF digest behavior and do not edit checkpoint tests/census. |
| CARD-1022 Plan `da6e8e47`, Succeeded, `13d7100febc3f4b595299eb982bf7fc5e44c88af` | Read its modern-only plan: staged A/B/C retirement, Unix transport retained, no live inbox qualification. CARD-1020 requests modern on Windows and adds no inbox assertion or capability/version admission gate. |
| CARD-0691, still Review | Read card and S5/S6 plan. CARD-1020 owns **only** `tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs` disposal/capture and new `TestOwnedPtyHost.cs`, with the two new Agents test files and original marker method. CARD-0691 owns production owner-watch/bounded host shutdown, SessionRunner `TestSessionTeardown`/sweep/settings and E2E helpers. Its planned edit to this direct client is only the future owner-watch setting opt-in; preserve that setting if it lands, do not implement it here or duplicate the cleanup helper. Owner death is not in-process client disposal. |

The CARD-1020 card read still contains the inaccurate 24-hour explanation. The
caller owns that card correction. The existing helper override is **0.02 hours =
72 seconds**; do not shorten it. CARD-0691's longer-than-TTL reports also mean that
waiting 72 seconds is not a cleanup oracle.

**Setup and missing facilities to implement in S1.** The new helper/tests do not
exist at this source. Add the per-instance concrete ownership record and cleanup
operations in the already authorized `TestOwnedPtyHost.cs`. The two new Agents
files hold the six native methods plus the unit methods below; the identity file
may also declare `TestOwnedPtyHostPolicyTests`. Provide internal, instance-local
I/O delegates and a controllable clock for process observe/enumerate/kill/wait,
session stop, runtime dispose and observer-handle release. Defaults invoke real
operations; no static hook, runtime/production seam or public interface is needed.
Unit tests call the same cleanup/capture code used by the direct client, substituting
only I/O; they must not implement a second cleanup algorithm. Operation recording
must expose identity and order, including rejected actions.

For dual-failure reporting, an `IAsyncDisposable` cannot automatically discover the
exception escaping its caller's body. The test-owned scope in S2 must retain the
body exception before disposal and combine it with a cleanup exception (both original
exceptions, not just strings); use a small internal scope function in the new helper
and exercise that same function in V-15. V-6 separately covers ordinary await-using
with successful cleanup. This specifies how to verify D-3's primary-evidence clause,
without changing production exception behavior.

Native observers are independent of the cleanup record: retain their own process
handles, PID/start identity and Windows ancestry/image observations **before** any
stop. Never validate cleanup by reading its own `Success` flag. If process access,
ancestry or modern payload is unavailable, fail setup with the exact reason. Do not
skip and do not claim an empty witness set passed. Windows needs WMI access and the
shipped modern pair. Linux needs `/proc`, a native shell and staged fake apphosts;
R-2 additionally needs the existing isolated PostgreSQL fixture and fake Herdr socket.
No production runner or user provider/auth state is used.

### Delivery inventory

No new or changed application-message producer, durable queue, enqueue handoff or
receipt recovery path is introduced. Therefore busy-recipient, already-eligible
queue dispatch and crash/enqueue-failure queue matrices are excluded: changing
those paths is outside D-1. This is not a claim that the direct test client proves
the real message queue.

| Path | Producer -> recipient | Persistence / identity | Recovery | Decisive receipt |
|---|---|---|---|---|
| Changed test teardown | Direct client's Dispose -> runtime Kill/Shutdown -> owned PtyHost and child/console | Own manifest/shadow root + session GUID + PID and captured actual start identity; in-memory handles retained before removal. No queue or new durable record. | Pipe failure/lost ack/child already exited -> bounded wait then verified owned kill and exit wait. Access errors remain unresolved. Process death of the whole test harness is CARD-0691. | Independent host **and** descendant handles report exited when Dispose returns; deletable private tree. V-1/2/8–12, G-1/8–19. Sent Shutdown/Exited/manifest deletion alone cannot pass. |
| Retained fake-provider input | Original adapter -> direct runtime -> PTY -> FakeGrok transcript/tailer -> client transcript | Session GUID, attempt baseline sequence and complete unique body. FakeGrok's private transcript file is the persistence boundary. | Existing tailer only; no changed replay or queue recovery. | Exactly one matching complete `UserPrompt` above baseline, for each marker case; same receipt after adoption in V-3 and from surviving client B in V-4. R-1/G-22. |
| Explicit detach/restart | Old direct runtime -> private manifest -> adopted direct runtime -> same live FakeGrok | Same root/session/host and child generation, sequence floor retained | V-3 disconnects then adopts, also calls SimulateRunnerRestartAsync and verifies it leaves KillOnDispose false; explicitly restores true only for final owned cleanup | Host/child survive detach, a newly sent unique complete UserPrompt is observed after each adoption, and final disposal exits every witness. G-2. |

Raw `PtyHostClient` in V-2 substitutes only for the runtime's automatic Shutdown ack
so linger is deterministic. FakeGrok substitutes for the paid provider and proves
its actual input/transcript path, not model behavior. Unit I/O delegates prove guard
branching, ordering, deadlines and failure reporting, not OS process death. R-2's
fake Herdr proves the existing wire/metadata contract, not a live Herdr installation.
Windows native V/R must supply recipient/process evidence separately; the design
does not stop at requests, queue inserts, events, acknowledgements or Sent flags.

### Proves it works now

All new native cases are Integration, use `ParallelLimiter<ProcessSpawnLimit>`,
unique roots and independent emergency cleanup in `finally` **after** the decisive
assertions. Unit cases use no real process control. Exact methods and argument rows
are frozen below; argument rows are TUnit results, internal scenario assertions are
not additional results.

- V-1: `DirectSessionRunnerClientDisposalTests.Dispose_awaits_exit_of_its_running_host_and_descendants` | native, 1 result | start FakeGrok, require live host/child and Windows OpenConsole; dispose | `host-exited-at-return`, `child-exited-at-return`, `console-exited-at-return`, `private-tree-deletable`. Capture a native shell descendant as well where the fixture starts one; the host/child/OpenConsole set is mandatory on Windows.
- V-2: `DirectSessionRunnerClientDisposalTests.Lingering_exited_host_is_reaped_before_the_cleanup_returns` | native raw host, 1 | launcher + raw client, 72-second linger, native shell launch/attach/kill, observe child Exited; disconnect **without Shutdown**; prove host still live, capture witnesses and remove only its manifest before invoking the shared owned-cleanup primitive | `linger-live-before-cleanup`, `fallback-host-exited`, `cleanup-before-ttl`. Completion within 10 seconds of cleanup entry, never by waiting out TTL. Independent cleanup retains handles even if the mutant returns without killing.
- V-3: `DirectSessionRunnerClientDisposalTests.Dispose_with_kill_disabled_leaves_the_session_adoptable` | native, 1 | explicit false disposal, fresh-client adoption, then simulated restart on adopter | `detach-host-alive`, `detach-child-alive`, `restart-preserves-optout`, same identities and full nonce UserPrompts after both adoptions; final true disposal exits them. This also pins production detach because the test uses the unmodified real runtime Dispose/adoption path.
- V-4: `DirectSessionRunnerClientDisposalTests.Dispose_does_not_touch_another_clients_host` | native, 1 | A/B separate roots, both proved live; dispose A | `A-exited`, `B-still-alive`, B receives its complete unique UserPrompt; then dispose B and assert exit before emergency cleanup.
- V-5: `DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent` | native, 1 | two Dispose calls and an internal operation recorder | `dispose-core-once`, second call awaits the same completion/failure, no repeated kill or use of released handles; no surviving witness.
- V-6: `DirectSessionRunnerClientDisposalTests.Body_failure_still_reaps_the_owned_host` | native, 1 | throw a sentinel through await-using | identical caught sentinel and `body-failure-host-exited` plus descendant exit. Cleanup success must not conceal the body failure.
- V-7: `TestOwnedPtyHostIdentityTests.Identity_requires_the_owned_root_session_and_process_generation` | Unit, **12 explicit Arguments**: owned, foreign-root, prefix-sibling, dotdot-escape, symlink-outside, platform-casing, foreign-session, stale-generation, zero-pid, unknown-start, foreign-image, unrelated-descendant | exercise the action-authorizing predicate, not a duplicate comparison | owned accepted; platform-casing follows the actual OS rule; all other invalid identities refused and control-operation count zero. For stale-generation test both capture-time and recheck immediately before control; a retained PID is insufficient. Use unique temporary directories/symlink under fixture cleanup for path cases.
- V-8: `TestOwnedPtyHostPolicyTests.Capture_retains_every_owned_generation` | Unit, **3 Arguments**: replaced, partial-start, manifest-missing | drive the client's capture/supplement path with scripted runtime/own-manifest observations | `all-owned-generations-retained`, `capture-before-stop`, exact identified records still visited even after status Exited or manifest deletion; no global root scan. V-2 supplies the real missing-manifest/linger counterpart.
- V-9: `TestOwnedPtyHostPolicyTests.Cleanup_waits_and_uses_one_deadline_per_phase` | Unit, **4 Arguments**: normal-grace, post-kill-wait, stop-cancel, shared-deadline | controlled tasks/clock | `grace-before-force` (no force before 2 seconds), `return-awaits-exit` (kill completion alone does not finish), `stop-token-cancelled` (2-second session-stop deadline), `phase-deadline-not-reset` across two hosts/descendants (one 2-second grace and one further 5-second fallback budget). Gates prove order without wall sleeps; every parked operation is released/joined in finally.
- V-10: `TestOwnedPtyHostPolicyTests.Cleanup_reaps_a_captured_descendant_after_host_exit` | Unit, 1 | host dead, previously captured same-generation descendant alive | `surviving-descendant-killed-and-awaited` and no unrecorded/reused descendant killed. V-1/R-1 supply actual Windows descendant evidence; no requirement that a real native job deliberately leak to arrange this branch.
- V-11: `TestOwnedPtyHostPolicyTests.Cleanup_unknown_or_failed_operations_never_report_success` | Unit, **4 Arguments**: probe-denied, enumeration-denied, kill-denied, survivor | inject these faults only at OS I/O boundaries | `cleanup-unresolved` includes identity, phase and elapsed time; unknown is never dead and never authorizes an unverified kill; proven-owned other records are still attempted. A denied operation is not a successful mutation red until this assertion actually fails.
- V-12: `TestOwnedPtyHostPolicyTests.Dispose_failure_still_releases_runtime_and_handles` | Unit, **2 Arguments**: cleanup-throws, runtime-dispose-throws | invoke direct-client orchestration with injected faults | `runtime-dispose-attempted-once` and `all-observer-handles-released-once`, cleanup error retained. Resource release is independent of process cleanup success.
- V-13: `TestOwnedPtyHostPolicyTests.Cleanup_eligibility_keeps_herdr_and_custody_separate` | Unit, **4 Arguments**: ordinary-pty, herdr, verification-pty, verification-herdr | all other ownership fields valid | `fallback-eligible` true only for ordinary PTY; `fallback-actions-zero` for each exclusion. Existing ordinary Herdr teardown/custody authority is preserved; this tests the additional OS fallback's admission, not custody implementation.
- V-14: `TestOwnedPtyHostPolicyTests.Witness_validation_rejects_incomplete_process_evidence` | Unit, **3 Arguments**: missing-host, missing-child, missing-windows-console | call the fixture's nonempty/live witness validator with one missing dimension and the others valid; Windows mode is an explicit parameter here | `incomplete-witness-rejected`. Native tests call the validator with actual OS and independently captured handles; neither an empty process census nor a manifest-only record can pass.
- V-15: `TestOwnedPtyHostPolicyTests.Body_and_cleanup_failures_are_both_preserved` | Unit, 1 | exercise the S2 scope function with a unique body exception and injected cleanup exception | `both-original-exceptions-retained`, cleanup attempted once. Do not assert only message text or catch only one of the two.

Native teardown budgets: session-stop phase at most 2 seconds, normal host-exit
grace at most 2 seconds, fallback kill/wait phase at most 5 seconds, shared within
each phase. Native timing assertion allows at most 10 seconds from teardown entry
for these fixtures; slow launch/readiness has its own 20-second fixture deadline.
The unit clock pins exact phase boundaries; the native margin is not a retry policy.
No process may outlive a successfully completed test. If assertions fail, emergency
cleanup must still kill/await all positively identified witnesses and report any
unresolved identity rather than conceal residue.

### Guards the regression

- R-1: `RunnerGrokAdapterReadyTestsPty.Fake_dashboard_marker_reaches_ready_and_complete_first_prompt` | 2 results, false/true | retain ready classifier, screen marker and initially empty UserPrompt assertions, and exact single complete body equality. Add independent alive-before/exit-after witness assertions around nested adapter/client scopes and private-root deletion. Adapter disposal precedes client disposal. Log actual requested/resolved backend; Windows requires ModernConPty and a positively attributed live OpenConsole before teardown. Failure diagnostics survive in ignored evidence, outside the private tree being deleted.
- R-2: `HerdrLabelFollowWireTests.Launch_and_get_round_trip_follow_metadata`, `Direct_and_http_get_refresh_and_map_the_same_follow_observation`, `Old_peers_and_sidecars_remain_compatible_without_follow` | 1+1+2 = 4 results | keep all existing assertions, including direct-client false-disposal users, sidecar metadata and old-peer compatibility. No edits to these bodies are required.

Boundary coverage is pairwise where independent: root/session/generation faults
are one-at-a-time with all other fields valid (V-7); eligible/excluded backend and
custody are the complete 2x2 matrix (V-13); running/exited/partial/replaced/adopted
and missing manifest are V-1/2/3/8. Both OSes execute identity and timing policies.
Real native Windows and Linux are separate checkpoints. Simultaneous arbitrary
multiple OS access failures add no new authority: V-11 proves refusal for each
independent source and V-12 proves finally cleanup on failure. Production death,
queue recovery, global sweeps and inbox qualification are excluded below.

### Guard inventory

Each independently bypassable safety guard maps to its own PC; none is untested.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-1/D-3: default disposal performs owned teardown before returning | PC-1 |
| G-2 | D-4: explicit false/simulated restart preserves detach and adoptability | PC-2 |
| G-3 | D-2/S1: own normalized root/path boundary authorizes control | PC-3 |
| G-4 | D-2/S1: session GUID must match | PC-4 |
| G-5 | D-2/S1: actual process generation matches at initial capture | PC-5 |
| G-6 | D-4: verification binding excludes ordinary OS fallback | PC-6 |
| G-7 | D-4: Herdr excludes PtyHost OS fallback | PC-7 |
| G-8 | D-2: capture before stop/removal; manifest loss does not erase ownership | PC-8 |
| G-9 | D-2/S1: replaced/partial/exited generations retained until cleanup | PC-9 |
| G-10 | D-2/S1: unknown process observation is not confirmed death | PC-10 |
| G-11 | D-2/D-5: failed descendant enumeration is not a complete empty set | PC-11 |
| G-12 | D-3: normal exit gets its bounded grace before forcing | PC-12 |
| G-13 | D-3: surviving verified host receives fallback tree kill | PC-13 |
| G-14 | D-3: captured surviving descendant is reaped even if host exited | PC-14 |
| G-15 | D-3/D-5: await all captured identities after kill; kill return is insufficient | PC-15 |
| G-16 | D-3: session-stop I/O has cancellation deadline | PC-16 |
| G-17 | D-3: one remaining deadline per phase, not per identity/poll | PC-17 |
| G-18 | D-3: runtime disposal always attempted in finally | PC-18 |
| G-19 | D-3: observation handles always released in finally | PC-19 |
| G-20 | D-3: failed kill yields explicit cleanup failure | PC-20 |
| G-21 | D-4: repeated disposal shares one completion, no stale actions | PC-21 |
| G-22 | D-5/S2: successful input means complete matching UserPrompt | PC-22 |
| G-23 | D-5: host witness set cannot be empty | PC-23 |
| G-24 | D-3/S2: cleanup error cannot erase primary body evidence | PC-24 |
| G-25 | D-2/S1: ancestry attribution excludes unrelated descendants | PC-25 |
| G-26 | D-2/S1: host image must match its owned shadow-copy identity | PC-26 |
| G-27 | D-5: child witness set cannot be empty | PC-27 |
| G-28 | D-5: Windows console witness set cannot be empty | PC-28 |
| G-29 | D-2/S1: generation is rechecked immediately before process control | PC-29 |
| G-30 | D-3: a still-live identity after fallback yields explicit failure | PC-30 |

### Positive controls

Post-land Mutation owns break/red/restore/green; Code runs ordinary V/R and Review
judges the design before landing. All defects below are compiling edits to helper
or caller behavior, never deletion of the decisive assertion. Every run uses the
**exact method filter** in the command-filter inventory below, without class
wildcard/category filters. Parameterized methods run
all their rows; the specified row/assertion must be red. Do not treat build/startup
failure, zero tests, deadline expiry outside the decisive assertion, or missing
native payload as a killed control. Restore and rerun that same method green.

| PC | Compiling break of its guard | Exact test method; required red assertion |
|---|---|---|
| PC-1 | In default direct-client disposal replace owned teardown with runtime detach only | `DirectSessionRunnerClientDisposalTests.Dispose_awaits_exit_of_its_running_host_and_descendants`; `host-exited-at-return` or `child-exited-at-return` observes live captured identity |
| PC-2 | Ignore KillOnDispose false and enter ordinary teardown | `DirectSessionRunnerClientDisposalTests.Dispose_with_kill_disabled_leaves_the_session_adoptable`; `detach-host-alive` |
| PC-3 | Replace only the own-root/path-boundary predicate with true | `TestOwnedPtyHostIdentityTests.Identity_requires_the_owned_root_session_and_process_generation`; foreign-root/prefix-sibling `identity-rejected` |
| PC-4 | Replace only session-id equality with true | `TestOwnedPtyHostIdentityTests.Identity_requires_the_owned_root_session_and_process_generation`; foreign-session `identity-rejected` |
| PC-5 | Omit only initial process start-generation equality | `TestOwnedPtyHostIdentityTests.Identity_requires_the_owned_root_session_and_process_generation`; initially stale-generation `identity-rejected` |
| PC-6 | Remove verification-binding exclusion | `TestOwnedPtyHostPolicyTests.Cleanup_eligibility_keeps_herdr_and_custody_separate`; verification-pty `fallback-actions-zero` |
| PC-7 | Remove Herdr/backend exclusion | `TestOwnedPtyHostPolicyTests.Cleanup_eligibility_keeps_herdr_and_custody_separate`; herdr `fallback-actions-zero` |
| PC-8 | Clear captured records before teardown and rely only on remaining manifests | `TestOwnedPtyHostPolicyTests.Capture_retains_every_owned_generation`; manifest-missing `capture-before-stop` / `all-owned-generations-retained` |
| PC-9 | Replace append-by-generation storage with replacement by session ID, dropping the previous record | `TestOwnedPtyHostPolicyTests.Capture_retains_every_owned_generation`; replaced `all-owned-generations-retained` |
| PC-10 | Convert a process observation access exception to confirmed Dead | `TestOwnedPtyHostPolicyTests.Cleanup_unknown_or_failed_operations_never_report_success`; probe-denied `cleanup-unresolved` |
| PC-11 | Convert enumeration failure to a successful empty descendant list | `TestOwnedPtyHostPolicyTests.Cleanup_unknown_or_failed_operations_never_report_success`; enumeration-denied `cleanup-unresolved` |
| PC-12 | Set normal grace to zero, leaving fallback intact | `TestOwnedPtyHostPolicyTests.Cleanup_waits_and_uses_one_deadline_per_phase`; normal-grace `grace-before-force` |
| PC-13 | Skip the owned-host fallback kill call, retaining the wait | `DirectSessionRunnerClientDisposalTests.Lingering_exited_host_is_reaped_before_the_cleanup_returns`; `fallback-host-exited` observes live host, not merely a timeout exception; fixture catches cleanup failure to reach this witness assertion |
| PC-14 | Return when host is dead before visiting captured descendants | `TestOwnedPtyHostPolicyTests.Cleanup_reaps_a_captured_descendant_after_host_exit`; `surviving-descendant-killed-and-awaited` |
| PC-15 | Replace awaited post-kill exit task with Task.CompletedTask | `TestOwnedPtyHostPolicyTests.Cleanup_waits_and_uses_one_deadline_per_phase`; post-kill-wait `return-awaits-exit` while exit gate is held |
| PC-16 | Pass CancellationToken.None to the session-stop operation | `TestOwnedPtyHostPolicyTests.Cleanup_waits_and_uses_one_deadline_per_phase`; stop-cancel `stop-token-cancelled`; release fake stop in finally |
| PC-17 | Renew phase deadline for each next owned identity | `TestOwnedPtyHostPolicyTests.Cleanup_waits_and_uses_one_deadline_per_phase`; shared-deadline `phase-deadline-not-reset` |
| PC-18 | Move runtime disposal out of finally into success-only branch | `TestOwnedPtyHostPolicyTests.Dispose_failure_still_releases_runtime_and_handles`; cleanup-throws `runtime-dispose-attempted-once` |
| PC-19 | Release observer handles only after successful runtime disposal | `TestOwnedPtyHostPolicyTests.Dispose_failure_still_releases_runtime_and_handles`; runtime-dispose-throws `all-observer-handles-released-once` |
| PC-20 | Catch and discard a verified-owned kill exception, retaining other error handling | `TestOwnedPtyHostPolicyTests.Cleanup_unknown_or_failed_operations_never_report_success`; kill-denied `cleanup-unresolved` (its scripted identity exits independently, isolating the swallowed kill error from survivor detection) |
| PC-21 | Remove memoized disposal task/once guard so the second call repeats core work | `DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent`; `dispose-core-once` |
| PC-22 | In the original marker caller send only a strict prefix of its body, preserving expected full body and transcript assertions | `RunnerGrokAdapterReadyTestsPty.Fake_dashboard_marker_reaches_ready_and_complete_first_prompt`; `prompts[0].Text.ShouldBe(body)` in both argument rows |
| PC-23 | Remove only the shared fixture validator's nonempty host check | `TestOwnedPtyHostPolicyTests.Witness_validation_rejects_incomplete_process_evidence`; missing-host `incomplete-witness-rejected` |
| PC-24 | Scope error combination keeps only the cleanup exception | `TestOwnedPtyHostPolicyTests.Body_and_cleanup_failures_are_both_preserved`; `both-original-exceptions-retained` |
| PC-25 | Drop only ancestry attribution while retaining root/session/generation/image predicates | `TestOwnedPtyHostIdentityTests.Identity_requires_the_owned_root_session_and_process_generation`; unrelated-descendant `identity-rejected` with zero control actions |
| PC-26 | Drop only host-image attribution while retaining root/session/generation/ancestry predicates | `TestOwnedPtyHostIdentityTests.Identity_requires_the_owned_root_session_and_process_generation`; foreign-image `identity-rejected` with zero control actions |
| PC-27 | Remove only the fixture validator's nonempty child check | `TestOwnedPtyHostPolicyTests.Witness_validation_rejects_incomplete_process_evidence`; missing-child `incomplete-witness-rejected` |
| PC-28 | Remove only the fixture validator's Windows console check | `TestOwnedPtyHostPolicyTests.Witness_validation_rejects_incomplete_process_evidence`; missing-windows-console `incomplete-witness-rejected` |
| PC-29 | Omit pre-control generation recheck but keep initial capture validation | `TestOwnedPtyHostIdentityTests.Identity_requires_the_owned_root_session_and_process_generation`; stale-generation scenario changes generation after valid capture; `control-operations-zero` |
| PC-30 | Treat a successful kill call followed by an expired wait on a live identity as success | `TestOwnedPtyHostPolicyTests.Cleanup_unknown_or_failed_operations_never_report_success`; survivor `cleanup-unresolved`, with no kill exception |

Exact `--treenode-filter` values (use identically for each listed PC's red and green):

| Controls | Exact filter | Min per run |
|---|---|---:|
| PC-1 | `/*/*/DirectSessionRunnerClientDisposalTests/Dispose_awaits_exit_of_its_running_host_and_descendants` | 1 |
| PC-2 | `/*/*/DirectSessionRunnerClientDisposalTests/Dispose_with_kill_disabled_leaves_the_session_adoptable` | 1 |
| PC-3/4/5/25/26/29 | `/*/*/TestOwnedPtyHostIdentityTests/Identity_requires_the_owned_root_session_and_process_generation` | 12 |
| PC-6/7 | `/*/*/TestOwnedPtyHostPolicyTests/Cleanup_eligibility_keeps_herdr_and_custody_separate` | 4 |
| PC-8/9 | `/*/*/TestOwnedPtyHostPolicyTests/Capture_retains_every_owned_generation` | 3 |
| PC-10/11/20/30 | `/*/*/TestOwnedPtyHostPolicyTests/Cleanup_unknown_or_failed_operations_never_report_success` | 4 |
| PC-12/15/16/17 | `/*/*/TestOwnedPtyHostPolicyTests/Cleanup_waits_and_uses_one_deadline_per_phase` | 4 |
| PC-13 | `/*/*/DirectSessionRunnerClientDisposalTests/Lingering_exited_host_is_reaped_before_the_cleanup_returns` | 1 |
| PC-14 | `/*/*/TestOwnedPtyHostPolicyTests/Cleanup_reaps_a_captured_descendant_after_host_exit` | 1 |
| PC-18/19 | `/*/*/TestOwnedPtyHostPolicyTests/Dispose_failure_still_releases_runtime_and_handles` | 2 |
| PC-21 | `/*/*/DirectSessionRunnerClientDisposalTests/Dispose_is_idempotent` | 1 |
| PC-22 | `/*/*/RunnerGrokAdapterReadyTestsPty/Fake_dashboard_marker_reaches_ready_and_complete_first_prompt` | 2 |
| PC-23/27/28 | `/*/*/TestOwnedPtyHostPolicyTests/Witness_validation_rejects_incomplete_process_evidence` | 3 |
| PC-24 | `/*/*/TestOwnedPtyHostPolicyTests/Body_and_cleanup_failures_are_both_preserved` | 1 |

PC-1/2/13/21/22 use the same Windows modern setup as CP-3/4; the other controls
are portable and run on Linux. No controls are batched: most edit the same helper,
so independence means one restored source and one guard per cycle. Emergency
cleanup uses independent captured identities and is not mutated. For PC-13, host
linger stays 72 seconds and assertion happens within 10 seconds; TTL expiry cannot
turn missing fallback into green. PC-15 supplies the separate wait-after-kill
control so PC-13 does not confuse a kill request with observed exit.

### Out of scope

- No production host/runtime/PTY transport changes, owner-death watch, host
  hard-exit timer or global orphan sweep: CARD-0691 owns these. V-3 uses real
  production detach/adoption but does not claim the full production restart suite.
- No inbox-native expectations/canary, paid provider or CLI-version eligibility
  work. CARD-1022 owns retirement; CARD-1011 owns its added qualification methods.
- No real-queue busy/idle/crash/enqueue recovery cases: message queue behavior is
  unchanged, and R-1's direct complete UserPrompt is explicitly not queue proof.
- No changes or additions in `Antiphon.Tests.Checkpoints`. The caller's census
  literal **377 remains untouched**; grep occurrence counts include embedded test
  source strings and do not establish a replacement census. New tests reside in
  `Antiphon.Tests.Agents`; no checkpoint namespace regression lane is required.
- No claim for arbitrary unobservable escaped descendants, unrelated processes,
  process-wide PID sweeps or killing on unknown ownership. Required fixture
  descendants must be positively attributed; missing evidence fails, not succeeds.

### Checkpoints

This is the **only importable checkpoint table**. All four rows run at one final
S1+S2 source SHA: commit/push both slices before the grouped verification. This
supersedes the earlier CP-1-after-S1 timing, avoiding a stale pre-S2 certificate.
CP-1/2 run on Linux; a separate Windows Debug at that exact SHA runs CP-3/4.
Each row has its own isolated build and one exact filter. No repeat run is required
once green unless source changes or a demonstrated failure requires it.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-linux-lifecycle/` | linux-lifecycle | `/*/*/(DirectSessionRunnerClientDisposalTests*)\|(TestOwnedPtyHostIdentityTests*)\|(TestOwnedPtyHostPolicyTests*)/*` | V-1–V-15 | all 40 listed results, 0 failed/skipped | 40 | 9 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-linux-callers/` | linux-callers | `/*/*/(RunnerGrokAdapterReadyTestsPty*)\|(HerdrLabelFollowWireTests*)/(Fake_dashboard_marker_reaches_ready_and_complete_first_prompt*)\|(Launch_and_get_round_trip_follow_metadata*)\|(Direct_and_http_get_refresh_and_map_the_same_follow_observation*)\|(Old_peers_and_sidecars_remain_compatible_without_follow*)` | R-1, R-2 | all 6 listed results, 0 failed/skipped | 6 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-windows-lifecycle/` | windows-modern-lifecycle | `/*/*/(DirectSessionRunnerClientDisposalTests*)\|(TestOwnedPtyHostIdentityTests*)\|(TestOwnedPtyHostPolicyTests*)/*` | V-1–V-15 | all 40 listed results; required native Windows witnesses; 0 failed/skipped | 40 | 9 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-windows-grok/` | windows-modern-grok | `/*/*/RunnerGrokAdapterReadyTestsPty/Fake_dashboard_marker_reaches_ready_and_complete_first_prompt` | R-1 | both marker cases with Windows process witnesses, 0 failed/skipped | 2 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

**Code execution correction, 2026-10-04 (task 1d3e0d92).** CP-2's original
method-OR selection executed zero tests at `d9be1fb113ca8233c64b5a861b5b59f9281c0c64`.
The method operands now use trailing `*`, as required by the pinned TUnit discovery
hint behavior in docs/testing-and-build.md. This implements the Code brief's prefix
filter requirement. The intended roster and floor remain exactly 2 marker + 4 Herdr;
inspect fresh TRX to reject any additional prefix matches. No Windows row changes.

**Read-only importer contract audit.** `ExtractSection` chooses the first exact
`### Checkpoints`; the planner table's heading was renamed so it cannot silently
win. `SplitRow` unescapes each `\|` before splitting; these rows each have 11 cells,
the first nine exactly match the required schema, optional Serial/Environment are
valid. Import yields four distinct BuildSpecs for `tests/Antiphon.Tests` with the
four forward-slash outputs above, After `[S1,S2]`, Min `[40,6,40,2]`, estimates
`[9,6,9,4]`, repeat=1, serial=true and one row environment entry. No build reuse,
unknown header, category filter, trailing filter prose or inherited platform pin.
RosterTokens yields the three lifecycle class names, the two caller class names,
and the original marker class for CP-4; the human roster still checks methods and
argument counts because these tokens alone do not prove completeness. These
import checks do not build or execute tests and do not prove the new methods exist.
The actual importer and ManifestValidator were also invoked read-only, without a
build, through PowerShell reflection on the already-built checkpoint assembly at
`/work/worktrees/task-2c35a27d/tools/Antiphon.Checkpoints/bin-c1013-tool/Antiphon.Checkpoints.dll`
(SHA256 `04107b99e87701d26f9cbf6300e1f0ea2c012eeee121c59346164150d612ceee`).
`ImportFile(plan, plan, false)` and `ImportFile(plan, plan, true)` each returned
exit 0, warnings empty, four builds and exactly the filters/rows described above;
`ManifestValidator.Validate` accepted both. No CLI run/start/build/test was called,
and no manifest or checkpoint run directory was written. This binary is parser
validation evidence only, not a candidate-source build certificate. Code imports
again with its own prebuilt tool before executing the manifest.

Counts: lifecycle = six native single-result methods + 12 identity + 3 capture +
4 deadline + 1 descendant + 4 failure + 2 finally + 4 eligibility + 3 witness +
1 dual-failure = **40**. Callers = 2 marker + 4 Herdr = **6**. Total ordinary
executions across both OS lanes = **88**; floors are TUnit executions, not minutes.

Use the checkpoint tool's `run --plan` with `--rows CP-1,CP-2` on Linux and
`--rows CP-3,CP-4` on Windows and `--expected-source-sha` equal to the final
committed code SHA. Build its own isolated tool output through build-slot only if
needed (the one named infrastructure build); each CP driver takes its own slot.
Wait through exit 75 to completion. **Do not set UseAppHost=true globally or add a
YAML override:** the inspected project already restores/stages its native helper
apphosts while the outer Linux build uses the checkpoint default false. No csproj
change is authorized or needed by this freeze. Missing staged binaries are a real
setup/build defect, not a reason to silently skip the row.

The separate Windows Debug's exact filters are CP-3/CP-4 above (ordinary rows,
not extra fifth/sixth checkpoints). It must record source/build SHA and clean
receipt, OS, real host image/shadow path and hash, actual `--linger-hours 0.02`,
ModernConPty resolved with no fallback, package/file version and hashes, session
GUID, host/child/OpenConsole PID **and start identity**, ancestry, alive-before
and exit-after observations, disposal/kill/exit times and host log ending. Both
false/true marker results require complete UserPrompt receipt and all process
witnesses exited at client return. Release witness handles and delete the private
root; after the row/test host exits remove that row's alternate output directories.
A failure to delete or surviving process remains visible. Do not use a global
process-name census or a cleanup sweep to manufacture passing evidence.

Keep unedited CHECKPOINT lines and actual counts/SHA/slot/source/build provenance
in the stored Code report. Validate structured receipts, run the owner-required
coverage lint once methods exist, and run `scripts/check-evidence-diff.ps1` across
the full task range. Generated outputs stay ignored. Source remains frozen during
runs; a code repair gets a new commit and the affected final-SHA evidence. Windows
unavailability means pending mandatory evidence, never a Linux substitute.

### Cost

All values are **estimated**, not measured; slot waiting is additional elapsed
queue time and is reported separately. Ordinary Code floor is the checkpoint sum:
CP-1 9 + CP-2 6 + CP-3 9 + CP-4 4 = **28 minutes**. This includes four isolated
builds (budget 3 minutes each = 12) plus 16 minutes V/R execution. Infrastructure
setup/import/tool bootstrap allowance is **3 minutes** outside those rows; total
ordinary setup/build + V/R = **31 minutes**, excluding authoring and slot wait.

Post-land Mutation floor: the exact method filters in the PC table name every run.
Portable PC-3–12, PC-14–20 and PC-23–30 are **25 cycles**, each 0.5 minute edit/check
+ 3 minutes red build/run + 0.5 restore/check + 3 green build/run = **7 minutes**
per cycle, **175 minutes**. Windows native PC-1/2/13/21/22 are **5 cycles**, each
0.5 + 4 + 0.5 + 4 = **9 minutes**, **45 minutes**. No class/suite mutation run is
budgeted. Mutation setup/source-restoration evidence allowance **3 minutes**;
Mutation floor **223 minutes**. Combined ordinary + post-land floor =
3 + 28 + 223 = **254 minutes**. Ordinary Review uses the 28-minute scope if fresh
reruns are commissioned; add 28 then, not silently inside Code or Mutation.

Savings: grouping the same ordinary selections per OS into four rows instead of
one isolated build per selected class/OS (3 lifecycle classes on each OS, two
caller classes on Linux and one on Windows = 9 builds) saves **5 builds x 3 =
15 minutes**, estimated. No PC batching savings claimed (zero): mutations share
files and independent restoration/precise red assertions are required. Extra
reruns need their actual cause, filters and minutes reported.

Handoff audit: bodies read; **guards=30, mapped=30, missing=0, duplicate PC maps=0**.
All 30 controls have a compiling behavioral defect, exact method and decisive
assertion; implement the specified missing test-only seams in S1 before calling
any control executable in a built assembly. No production seam or human product
choice is unresolved. Mandatory Windows acceptance remains an execution obligation,
not evidence supplied by this docs-only freeze. Next stage: Code; serialize the
S2 same-file edit with CARD-1011 and start from this freeze's pushed tip.
