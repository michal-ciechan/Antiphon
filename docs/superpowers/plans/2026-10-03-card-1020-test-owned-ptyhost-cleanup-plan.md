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

## Verification design

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

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1020-linux-lifecycle/` | linux-lifecycle | `/*/*/(DirectSessionRunnerClientDisposalTests*)\|(TestOwnedPtyHostIdentityTests*)/*` | V-1–V-7 | all 10 listed results, 0 failed/skipped | 10 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-linux-callers/` | linux-callers | `/*/*/(RunnerGrokAdapterReadyTestsPty*)\|(HerdrLabelFollowWireTests*)/*` | R-1, R-2 | all 6 listed results, 0 failed/skipped | 6 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-windows-lifecycle/` | windows-modern-lifecycle | `/*/*/(DirectSessionRunnerClientDisposalTests*)\|(TestOwnedPtyHostIdentityTests*)/*` | V-1–V-7 | all 10 listed results with Windows console witnesses, 0 failed/skipped | 10 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c1020-windows-grok/` | windows-modern-grok | `/*/*/RunnerGrokAdapterReadyTestsPty/Fake_dashboard_marker_reaches_ready_and_complete_first_prompt` | R-1 | both marker cases, 0 failed/skipped | 2 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
