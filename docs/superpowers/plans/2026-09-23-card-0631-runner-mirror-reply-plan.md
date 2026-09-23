# CARD-0631: runner mirror provisioning and reliable replies

Date: 2026-09-23. Stage: Plan, with verification design folded in for the requested Code handoff.
Base inspected: `9082973365e00903b7e3810a3e572997aa9dce34`.
Investigation: [server2 mirror no reply](../../investigations/2026-09-23-card-0631-server2-workspace-mirror-no-reply.md).

Make an accepted request produce one correlated Result or Error while its connection remains
writable. Provision the missing runner repository through anonymous HTTPS before the first
mirror, give runner commits a machine identity, and prevent deploy from accepting an unusable
checkout. A disconnected socket cannot deliver a reply: terminate that connection so the server
observes transport failure, instead of logging a lost reply and leaving the connection healthy.

This plan changes no production source. The seven new tests below are **predicted baseline-red
tests**, not seven tests already executed. No build, test, deployment or live task was run in Plan.

## Ground truth

| Card assumption / required outcome | Code and evidence at the inspected base | Consequence |
|---|---|---|
| WorkspaceMirror hangs inside git | Investigation records four immediate `Win32Exception(2)` failures in `GitAsync`: `/work/repos/antiphon` is absent in the container | Fix provisioning and reply handling; do not tune a wait to hide it |
| Dispatch produces a frame for every handler failure | `PhoneHomeCommandDispatcher.DispatchAsync` catches a finite list; Win32/IO/JSON exceptions escape | Preserve existing mappings; add a final unexpected-error mapping |
| The receive loop replies if dispatch throws | `PhoneHomeConnectionService.ReceiveLoopAsync` logs non-OCE exceptions in a detached task; OCE produces no frame either | Add an independent dispatch-failure backstop, including unexpected cancellation |
| Runner writes can overlap safely | Replies, request-limit errors, heartbeats and events call `PhoneHomeFraming.WriteFrameAsync` without a common gate | Give each connection one send gate, used by every frame producer |
| A work volume contains a checkout | `init-state.sh` creates only directories; `MirrorAsync` immediately fetches in `_repository`; `copy_checkout_into_volume` seeds the child project only | Lazy clone in the runner; test a truly absent directory |
| The mirror request identifies its repository | `PhoneHomeWorkspaceMirrorRequest` has only Branch, Sha and Name | Keep the clone source runner-owned; never add a request-controlled URL |
| A repo and mirror can be reused freely | `MirrorAsync` validates name/branch/SHA, refuses a different existing HEAD, checks FETCH_HEAD and resulting HEAD | Preserve all checks, validation before I/O, and no overwrite of unrelated files |
| Runner git can commit | Baked `docker/session-runner-grok/gitconfig` has SSH/push rewriting only; Dockerfile already copies it to `/etc/gitconfig` | Add a system identity; no Dockerfile change is needed |
| Deploy proves mirror readiness | `case_deploy_parent` tests health, secrets and phone-home failures, but never probes the runner repository | Verify the repository in the container as uid 1654 before acceptance/image retirement |
| Server waits forever | `PhoneHomeLiveConnection.RequestTimeoutFor` already gives mirror/remove five minutes; runner git defaults to ten minutes per command | Retain the server bound; timeout alignment is recorded outside this fix |
| CARD-0628 is still uncommitted | Round B branch `feat/card-task-cb060b4e` is clean at `f9b3d11a7dd96014c778286c639758c8dc205361`; its preceding `cfab8b26` adds ProviderAuth, probe and backstop. Rebased onto CARD-0604 Cut B at `7ef9effb`, where ProviderAuth is operation 22 (Cut B owns 16-21) | Base Code on the landed Round B change, or rebase after it before Review/land |
| The deploy key must be fixed before coding | Investigation proved anonymous fetch works; only write authentication fails | GitHub registration is a precondition only of the live push/settlement proof |

## Decisions

- **D-1 — Three bounded Code rounds.** Round A implements S1+S2, B implements S3, C implements
  S4+S5. Each has one build/filter checkpoint targeting three minutes, and an authoring budget
  below 90 minutes including that checkpoint. Keep deploy and live E2E in a separately commissioned
  post-land follow-up. Reject a single large Code dispatch and the full Unit/integration lanes:
  neither fits the operator's verification policy. These are selected implementation decisions,
  not unresolved product defaults requiring a decision stage.
- **D-2 — Two reply boundaries.** Append `catch (Exception ex) when (ex is not
  OperationCanceledException)` to the dispatcher. Map to a new
  `PhoneHomeProblemTypes.RunnerInternalError = "runner_internal_error"`, status 500, keeping
  Epoch/RequestId/Operation unchanged. Keep every existing typed catch first. A separate receive
  handler catches escaped failures, including OCE unless the connection token is cancelled.
  Reject adding only Win32Exception to the existing list: another exception would recreate the hole.
- **D-3 — Dispatch failure is distinct from send failure.** Construct the response inside the
  dispatch try/catch; send it once afterward. Never catch a failed Result send and attempt a second
  Error on the same transport. Log a send failure with request identity and end/abort the connection.
  Shutdown cancellation emits no synthetic Error and is observed. Keep the in-flight decrement in
  `finally`; do not pass the connection token as Task.Run's scheduling token, which could prevent
  that finally from ever running after the increment. Do not claim delivery from a log entry.
- **D-4 — One connection, one writer.** A small internal `PhoneHomeConnectionWriter` owns a
  `SemaphoreSlim(1,1)` for that socket; `WaitAsync(ct)`/`finally Release()` surrounds the whole
  `WriteFrameAsync`. Replies, fallback errors, 429s, heartbeats and events all use it. Never make
  the lock global or put it in static framing code. Keep handler execution outside the gate so a
  held mirror cannot stop reads or unrelated Health dispatch. Connection cancellation cancels
  queued writes; old tasks retain their old writer/epoch. Do not dispose a gate while users still
  hold/wait on it. Coordinate close after send producers stop, or abort on transport failure;
  do not overlap the close handshake with a data send.
- **D-5 — Bounded diagnostics.** Share an internal Error-frame factory between the two reply
  boundaries. Include exception type and a bounded message, never payload, environment, credentials
  or a stack trace in the wire frame. Bound the serialized diagnostic to the configured message
  budget; fall back to the type/minimal error if needed. If even that frame cannot fit, fail the
  connection visibly. Keep full exception logging subject to the existing secret-custody rules.
- **D-6 — Lazy anonymous clone.** In `MirrorAsync`, after request validation and existing-mirror
  handling, ensure the repository before fetch. Use the runner-owned default
  `https://github.com/michal-ciechan/Antiphon.git` and
  `git clone --filter=blob:none --no-checkout <source> <repository>`, launched from an existing
  parent as the runner uid using ArgumentList and `GIT_TERMINAL_PROMPT=0`. Keep the existing
  constructor compatible; an internal overload can supply a local bare origin in tests. No new
  public setting or wire field is needed for this single-repository runner. Reject SSH cloning,
  entrypoint cloning and host-checkout copying: anonymous reads work today and lazy creation
  also repairs a fresh volume without coupling boot to network access.
- **D-7 — Preserve custody during initialization.** Accept an existing `.git` directory or
  gitfile and let git validate it; never reclone merely because HEAD has moved. Clone only into
  an absent or empty destination. Refuse a nonempty directory without `.git`, a destination file,
  or an unusable checkout without deleting/resetting anything. A failed clone returns a named
  admission error; any partial residue remains visible, and a retry either verifies it or refuses.
  Existing dispatcher `MutationLock` serializes mirror operations; deploy is verification-only,
  so it does not introduce another clone writer. Do not add a second global lock.
- **D-8 — Workspace failures are admissions.** Wrap git start failures (`Win32Exception`, IO,
  access denial, null process) and filesystem initialization errors at the workspace boundary as
  `PhoneHomeAdmissionException(UnsupportedTarget, <stage and bounded cause>, 409)`. A nonzero
  clone/fetch result is likewise 409. Preserve genuine caller cancellation as cancellation;
  on cancellation/timeout kill and reap this operation's own git child before releasing it, and
  observe redirected stream tasks. Keep the existing timeout's admission outcome. Use a small
  internal process-start delegate defaulting to `Process.Start`
  to test start failure without changing global PATH or removing the installed git binary.
  Unexpected faults elsewhere still take D-2's 500 path.
- **D-9 — System git identity.** Bake `[user] name = Antiphon Runner` and
  `email = antiphon-runner@localhost` into `/etc/gitconfig`. This is a machine identity, not an
  operator's identity. Preserve `core.sshCommand` and `pushInsteadOf` exactly. Reject per-session
  configuration because every first commit should work, and no session should need to repair
  provisioning. Normal repository-local git overrides retain their existing precedence.
- **D-10 — Deploy verifies rather than seeds.** Add a function to `scripts/c590-remote.sh` called
  by `case_deploy_parent` after the container/health check and before image retirement/acceptance.
  Through `docker exec -u 1654:1654`, resolve `PhoneHome__RunnerRepository` or the code default
  `/work/repos/antiphon`, require `.git` and a usable git worktree rooted at that path, check the
  expected anonymous HTTPS origin, then fetch `origin "$BRANCH"` with prompts disabled and
  `timeout --kill-after=5s 120s`. A nonzero exit, including timeout, is a refusal.
  Use named nonzero refusals (`RunnerCheckoutMissing`, `RunnerCheckoutInvalid`,
  `RunnerCheckoutOriginMismatch`, `RunnerCheckoutFetchFailed`) and a success receipt containing
  the verified path and FETCH_HEAD SHA, not credentials. Do not probe the host's identically
  named directory or the child volume. Reject a second seeder racing live lazy initialization.
  A first deploy on an empty volume deliberately reports Missing even if registration works;
  complete lazy initialization through an ordinary mirror, or a separately commissioned uid-1654
  anonymous seed while no mirror is in flight, then rerun deploy verification. Document this
  bootstrap condition explicitly rather than accepting a false green.
- **D-11 — The GitHub key is an operator live-E2E precondition only.** Register the existing
  `antiphon-server2-runner` public deploy key for this repository with write access, using the
  credential owner procedure. No key is copied/printed by this work. Code, Review, local git
  tests, anonymous clone/fetch and deploy checkout verification do not wait for it.
- **D-12 — Preserve the server timeout; defer budget redesign.** The ten-minute per-git versus
  five-minute mirror budget remains a known limitation for a slow clone or git hang. A blanket
  four-minute *per-command* change would still not bound the whole clone+fetch sequence. This
  card fixes immediate exceptions and missing provisioning; a whole-operation budget needs a
  separate scoped design. Do not claim this fix prevents every possible five-minute timeout.

## CARD-0628 coordination and landing order

Observed Round B: `cfab8b266325f556b5cc935514957b96a744df0b` (implementation), then
`f9b3d11a7dd96014c778286c639758c8dc205361` (verification record: 22/22, no failures/skips).
Those are branch observations, not a claim that master or production contains them. The cached
`origin/master` did not contain the observed tip at inspection time.

| Shared file / area | 0628 Round B | 0631 | Merge rule |
|---|---|---|---|
| `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs` | Optional probe constructor argument, ProviderAuth switch arm, Claude admission/backstop | Final catch and shared internal error factory | Preserve constructor/probe, ProviderAuth (op 22 after Cut B), signed-out 409 and unknown-auth admission |
| `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs` | ProviderAuth (= 22 after CARD-0604 Cut B took 16-21) and shared auth DTOs/problem type | One new error-code constant | Add beside existing constants; never renumber an operation already on master or duplicate/move auth DTOs |
| `tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs` | Five new auth/Claude methods | Unexpected-exception test and runtime fault seam | Keep all 11 Round B methods; CP-1 executes them |
| `PhoneHomeSettings.cs`, `Program.cs`, `ClaudeAuthProbe.cs` | Auth settings, DI and probe | Read dependencies only in this design | Do not replace from the old base or remove the optional probe argument |
| `PhoneHomeConnectionService.cs` and its tests | Included in Round B verification, not changed by its implementation commit | S1/S2 edits | Recheck the settled diff if Round B receives a follow-up |

Caller lands CARD-0628 Round B before dispatching Round A when possible. Start each Code round
from the prior reviewed/landed round plus the landed 0628 changes. If Round A was started earlier,
commit its own work, rebase onto that landing before Review, inspect the overlapping hunks and
rerun CP-1 at the rebased commit (a justified rerun). Do not cherry-pick the whole 0628 branch or
edit its worktree. CARD-0631 code lands after Round B; no independent phone-home replacement.
This Plan can land independently because it changes only this document.

## Implementation slices

| Round / slice | Work and files | Tests and exit |
|---|---|---|
| A / S1: error replies, about 35 min | `PhoneHomeCommandDispatcher.cs`, `PhoneHomeConnectionService.cs`, `PhoneHomeContracts.cs`: D-2/3/5. Extract internal dispatch-and-reply and receive-pump seams used by production, without substituting a fake dispatcher or introducing a service interface | New tests 1/2 below; preserve typed errors, cancellation, identity and in-flight accounting |
| A / S2: serialize sends, about 40 min | Add `src/Antiphon.SessionRunner/PhoneHomeConnectionWriter.cs`; route every service write through the connection writer and handle shutdown ownership. Add a channel-driven fake WebSocket in `tests/Antiphon.SessionRunner.Tests/TestHelpers/PhoneHomeTestWebSocket.cs` | New test 3, plus the dispatcher/connection regression roster; commit S1/S2 before CP-1. Round A total about 78 min |
| B / S3: lazy clone, about 65 min | `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs`; extend `tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests.cs` scratch factory with an absent-clone mode, bare origin and process-start seam; add the assembly's `ParallelLimiter<ProcessSpawnLimit>` to this existing git-spawning class | New tests 4/5 and all six existing workspace tests; commit before CP-2. Round B total about 68 min |
| C / S4: identity, about 10 min | `docker/session-runner-grok/gitconfig`; `tests/Antiphon.Tests/Infrastructure/DindRunnerContractTests.cs` | New test 6, existing push-only SSH guard |
| C / S5: deploy checkout proof, about 45 min | `scripts/c590-remote.sh`; `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`; update `docs/docker-stack.md` with lazy clone, identity, named deploy refusals and fresh-volume bootstrap procedure | New test 7, existing deploy and project-isolation guards; commit S4/S5 before CP-3. Round C total about 58 min |

Every round commits/pushes its meaningful slices and reports its own checkpoint at the tested
SHA. Do not start Docker image builds, provider probes or live deploys within these Code rounds.
They have separate costs and receipts below. No daemon restart is necessary for local Code checks.

## Verification design

### Inspection

Read the production dispatcher/connection/workspace/framing paths and server `RequestAsync`;
all bodies in the three runner test classes; the Dind gitconfig/Dockerfile helpers and relevant
remote deploy contract tests; `DockerStackDocuments`, `RemoteScriptContractTests.Block/Order`,
`ProcessSpawnLimit`, the runner test project and `scripts/run-checkpoint.ps1`.

Missing setup to implement: existing connection tests never enter the WebSocket receive pump.
Provide an internal pump entry using a supplied WebSocket and connection writer; a scripted fake
receives real serialized Request frames and records real serialized output via PhoneHomeFraming.
It supports held sends, overlap detection, a single send failure, and cancellation. It must not
reimplement the error mapping. No HTTP server, live port, Postgres, provider or Docker is needed.
Scratch git tests use only local origins and isolate their git config from user/system credentials.

### Delivery inventory

| Producer -> destination | Identity and persistence | Failure / recovery | Receipt used here |
|---|---|---|---|
| Server request -> receive pump -> dispatcher -> shared writer -> server waiter | `(epoch, requestId, operation)`; waiter is memory-only | Typed fault -> existing error; unexpected fault/OCE -> 500; connection loss -> fail transport, server's existing retry/timeout policy remains | Decode the actual output frame from the fake socket and match every identity field, count exactly one reply |
| Heartbeat/event/429/reply -> same socket | Connection-owned writer, no durable outbox | Held send queues other sends; shutdown cancels waiters; transport failure ends this connection, never replays an old reply on the next epoch | Fake socket records complete decodable frames and peak concurrent send count one |
| Server mirror command -> runner git -> returned mirror path | Requested branch/SHA/name and actual git HEAD | Clone failure/admission refusal cannot yield a successful mirror; existing same-SHA mirror is reusable | Real scratch repository/worktree contents, branch and exact HEAD |
| Live task -> runner prompt -> commit/push -> desktop settlement | Task/session IDs, transcript attempt floor, branch and commit SHA | Existing session delivery and settlement custody paths remain unchanged | Complete matching UserPrompt on runner plus pushed SHA equal to desktop worktree SHA after settlement |

There is no new durable queue in the reply transport. Disconnect/crash loses an in-memory reply;
this plan does not invent exactly-once execution across reconnection. A fake socket is the local
recipient substitute: it proves framing/correlation and overlapping-send behavior, not receipt
by the production server or delivery to an agent. The live follow-up supplies those receipts.
Session queue busy/crash/recovery behavior is unchanged and outside the local three-minute scope.

### Proves it works now

Exactly seven new methods; internal scenario loops below count as one TUnit execution each.

| V | Exact new test and file | Setup and decisive outcome | Why baseline is red |
|---|---|---|---|
| V-1 | `Dispatch_replies_with_error_frame_when_handler_throws_unexpected_exception` in `PhoneHomeCommandDispatcherTests.cs` | Runtime Health/Get fault seam throws Win32Exception, IOException, UnauthorizedAccessException and JsonException in separate requests. Require Error/500/runner_internal_error, original epoch/id/op and bounded type/message. Include an oversized diagnostic and verify its frame serializes within the configured limit | Exceptions escape. Use injected faults, not a missing repo that S3 would repair and map to 409 |
| V-2 | `Receive_loop_writes_error_frame_when_dispatch_throws` in `PhoneHomeConnectionServiceTests.cs` | Feed a real serialized Get request; runtime throws OCE while connection token is live, which intentionally escapes dispatcher catch. Require exactly one decoded Error with matching identity and in-flight returns to zero. Next Health request succeeds. Separate scenario cancels the connection while handler waits: no synthetic Error, handler finishes, count returns to zero | Unrelated OCE sends nothing. A deadline that observes zero frames is the expected assertion failure, not a fixture error |
| V-3 | `Concurrent_reply_and_heartbeat_never_drop_a_reply` in the same file | Hold the fake's first heartbeat SendAsync, call the production internal dispatch-and-reply helper with a synchronously completed Health response, then release. The call must reach the send gate before yielding. Without the gate the fake deterministically detects the second send. Require one heartbeat, one correlated reply, no overlap. Repeat with event, 429 and fallback-error producers through their real helper paths. Also exercise first-send failure and cancellation of a queued send; gate releases and old writer never sends on a replacement socket | Competing sends overlap; either fake throws or overlap count exceeds one. Do not use sleep-based race probability |
| V-4 | `Mirror_clones_repository_when_absent` in `RunnerWorkspaceServiceTests.cs` | Real local bare origin with known branch/SHA; no target directory. Supply the origin via the internal constructor seam. Require usable .git, origin, mirror branch/HEAD/content and replay same-SHA success without reclone. Cover an empty destination, a populated destination without .git (refusal and sentinel intact), existing repo reuse, and unavailable clone source (409, no successful mirror). Record clone argv to pin filter/no-checkout/noninteractive behavior | First fetch tries a missing working directory; no clone occurs |
| V-5 | `Mirror_failure_is_admission_error_not_crash` in the same file | Existing scratch checkout; injected Process.Start throws Win32Exception, IO or access error (and returns null in a separate scenario). Require PhoneHomeAdmissionException/UnsupportedTarget/409, stage/cause detail, no worktree. For invalid-input ordering use an absent repository: assert zero starts. For caller cancellation, the start seam starts an owned `git hash-object --stdin` with redirected stdin held open; cancel after the start latch and require OCE plus child exit, closing/reaping it in fixture cleanup even if the assertion fails | Process start failure escapes as Win32Exception/IO instead of an admission |
| V-6 | `Gitconfig_sets_runner_identity` in `DindRunnerContractTests.cs` | Read actual gitconfig, ignoring comments; require both exact values in the `[user]` section and Dockerfile's existing copy to `/etc/gitconfig`. Do not search for values anywhere in a comment | There is no user section |
| V-7 | `Deploy_parent_seeds_or_verifies_runner_checkout` in `RemoteScriptContractTests.cs` | Inspect executable deploy/function bodies: invoked from deploy before retirement/true result; uid 1654 exec; actual runner repository env/default, .git and git verification, HTTPS-origin validation, noninteractive bounded fetch with checked exit, named refusals and success evidence. Require all failures precede acceptance. Reject host-only/child-volume probes and failure masking with `|| true` | No runner-checkout verification is called |

V-4 must not download from GitHub. V-1 must still kill its guard after V-5 catches workspace
faults. V-2 must exercise the receive handler, not call the dispatcher and label that transport
coverage. The contract tests are source/configuration guards, not an assertion that Linux ran.

### Guards the regression

- **R-1:** CP-1 includes all 11 dispatcher methods after 0628 Round B, especially
  `Claude_launch_is_refused_when_the_probe_says_logged_out`,
  `Claude_launch_is_admitted_when_the_probe_is_unknown_or_absent`, and
  `Provider_auth_operation_answers_the_probe_and_refuses_unknown_providers`. Existing admission
  codes and the 0628 ProviderAuth operation (22 after Cut B) continue to work. Also keep all three existing connection methods:
  adoption precedes registration, event overflow recovery, and held commands allow Health progress.
- **R-2:** CP-2 includes all six existing workspace methods: exact branch/SHA, SHA mismatch,
  invalid name/SHA, dirty removal refusal, outside-root removal refusal and bounded spill writes.
  Strengthen the SHA test's fixture to include a second *real* commit if needed so bypassing the
  FETCH_HEAD check reaches the wrong-commit assertion rather than failing later on an unknown SHA.
  Run the mismatch check with both absent and existing repository states.
- **R-3:** CP-3 includes `Gitconfig_pushes_over_ssh_only`,
  `Child_project_is_run_scoped_and_distinct`,
  `Host_compose_uses_the_deployed_tag_not_this_runs_sha`,
  `Deploy_parent_probes_the_phone_home_secret_as_the_app_uid`,
  `Deploy_parent_refuses_a_runner_whose_phone_home_keeps_failing`, and
  `Deploy_parent_retires_its_own_superseded_images`. New preflight cannot skip these existing gates.

### Guard inventory

The following are the independently bypassable safety/reply guards introduced or directly
exercised by this change. Each has one distinct positive control. Unchanged remove/spill custody
guards stay covered by R-2; their existing CARD-0604 mutation designs are not duplicated here.

| Guard | Decision / invariant | Control |
|---|---|---|
| G-1 | D-2 unexpected dispatcher exception becomes a correlated 500 | PC-1 |
| G-2 | D-2 escaped OCE on a live connection receives an Error | PC-2 |
| G-3 | D-3 shutdown cancellation does not create a reply or leak in-flight count | PC-3 |
| G-4 | D-3/5 error identity is the original request's identity | PC-4 |
| G-5 | D-4 every outgoing producer shares the send gate | PC-5 |
| G-6 | D-4 gate release and queued cancellation survive failed sends | PC-6 |
| G-7 | D-6 absent repository is initialized before fetch | PC-7 |
| G-8 | D-7 foreign/nonempty destination is never overwritten or deleted | PC-8 |
| G-9 | D-8 git-start faults become workspace admissions | PC-9 |
| G-10 | D-6 validation precedes initialization/process I/O | PC-10 |
| G-11 | Requested commit must equal fetched branch tip | PC-11 |
| G-12 | D-9 both baked identity fields exist in the effective user section | PC-12 |
| G-13 | D-10 preflight targets runner uid/repository, not host or child checkout | PC-13 |
| G-14 | D-10 missing/invalid checkout cannot reach deploy success | PC-14 |
| G-15 | D-10 failed anonymous fetch cannot reach deploy success | PC-15 |
| G-16 | D-5 diagnostic size cannot silently drop the error reply | PC-16 |
| G-17 | D-8 cancelled git child does not outlive its operation | PC-17 |

### Positive controls

Mutation runs these after ordinary Review and confirmed land, under SourceLanding rules. Code
does not spend its three-minute checkpoint budget running PC cycles. Every cycle is method-scoped,
compiles, reaches the named assertion red, restores source and proves that same method green.
No mutation reaches production, a foreign repository, a shared volume or live GitHub.

| PC | Compiling mutation | Exact test / expected red assertion |
|---|---|---|
| PC-1 | Remove dispatcher final catch | V-1: expected returned Error instead observes the injected exception |
| PC-2 | Restore receive handler's exclusion of all OCEs | V-2: exactly one matching Error, actual zero |
| PC-3 | Treat cancelled connection OCE as an ordinary failure and send with an uncancelled token | V-2 shutdown subcase: expected zero Errors on still-open fake, actual one |
| PC-4 | Error factory assigns a new RequestId | V-1: response.RequestId equals request.RequestId |
| PC-5 | Bypass the writer gate for the reply path | V-3: no overlap and exactly one reply fail |
| PC-6 | Omit gate Release in the writer's exception path | V-3 failure subcase: following standalone writer send does not complete; assertion deadline, not harness timeout |
| PC-7 | Skip repository initialization before fetch | V-4: missing target fails instead of real mirror at requested HEAD |
| PC-8 | Replace populated destination refusal with deleting that scratch destination before clone | V-4: expected refusal and sentinel preservation fail; only the test-owned destination is reachable |
| PC-9 | Remove git-start exception translation | V-5: expected PhoneHomeAdmissionException observes Win32Exception |
| PC-10 | Move initialization ahead of name/SHA validation | V-5 invalid-input subcase: process-start invocation count must be zero |
| PC-11 | Remove fetched-tip equality refusal | `Mirror_refuses_sha_mismatch`: real alternative commit must still be refused before worktree creation |
| PC-12 | Remove gitconfig's user.email line | V-6: exact user.email key missing |
| PC-13 | Change checkout probe to `docker exec -u 0:0` | V-7: uid 1654 probe requirement fails |
| PC-14 | Remove the Missing/Invalid refusal branch from checkout verification | V-7: executable refusal before acceptance is missing |
| PC-15 | Mask checkout fetch exit with `|| true` | V-7: fetch failure must be checked and refuse |
| PC-16 | Remove ErrorDetail truncation/minimal-frame fallback | V-1 oversized subcase: expected serialized frame within budget exceeds it |
| PC-17 | Skip child kill/reap on caller cancellation | V-5 cancellation subcase: owned git must have exited when MirrorAsync completes; fixture then reaps it |

Guard census: 17 guards, 17 distinct controls, missing=0, duplicate mappings=0. Each control
uses the exact method above with `/*/*/<Class>/<Method>`; V references resolve through the
seven-method table. Variants for per-producer send-gate bypasses use V-3 separately if Mutation
finds independently bypassable call sites; do not widen to a full class for those cycles.

### Out of scope

No server dispatcher sweep redesign, retries, protocol version change, authentication change,
remote Claude onboarding, session input delivery refactor, repository cleanup/reset, or timeout
increase. No live image build or deployment in the Code checkpoints. An image contract pass does
not complete the operator's live proof. Existing full-suite failures are not in this closed list;
if a selected existing test fails, reproduce that named failure on the base before attributing it
to this card. Never loosen an assertion or widen a deadline to hide it.

### Cost

Estimated, not measured in Plan: Code ordinary V/R = **3 + 3 + 3 = 9 minutes** total,
about three minutes per round including its one isolated build. Authoring plus ordinary checks:
Round A 78 min, B 68 min, C 58 min (204 min total across three dispatches). Narrow filters use no
Docker/Postgres/provider and do not run the broad Unit lane. Warm package caches are assumed;
report a cold build or over-budget checkpoint explicitly instead of silently widening scope.
After three minutes do not start another run without explaining the required correction/reason.

Mutation estimate: 17 method-scoped red/restore/green cycles at about 3 min each = **51 min**,
plus about 8 min fixture setup/restoration/discovery = **59 min**. Total planned local work
including authoring, ordinary checks and Mutation is about **263 min**. These figures are time,
not executed-test floors. The 17 controls make 34 minimum method executions across red/green,
separate from the ordinary roster's 33 executions. No full-suite or image run is hidden in these
numbers. Savings: each round is bounded to one build/filter; no 25-minute application suite or
CARD-0628's broader image/provider checkpoint is repeated here.

### Checkpoints

This is the closed Code list, partitioned by round; execute only the row for the commissioned
round after its slices are committed. In Markdown table cells, `\|` denotes a literal filter OR;
the executable command below has plain `|`. The paired class/method filters intentionally select
only the seven new methods and the named affected regressions.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2, after 0628 Round B | `tests/Antiphon.SessionRunner.Tests -> bin-c631-a/` | reply-contract | `/*/*/(PhoneHomeCommandDispatcherTests*)\|(PhoneHomeConnectionServiceTests*)/*` | V-1..V-3, R-1 | All 17 methods: 11+1 dispatcher and 3+2 connection; 0 failed/skipped | 17 | 3 |
| CP-2 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c631-b/` | lazy-mirror | `/*/*/RunnerWorkspaceServiceTests/*` | V-4,V-5,R-2 | All 8 methods: 6 existing + 2 new; 0 failed/skipped | 8 | 3 |
| CP-3 | S4-S5 | `tests/Antiphon.Tests -> bin-c631-c/` | provisioning-contract | `/*/*/(DindRunnerContractTests*)\|(RemoteScriptContractTests*)/(Gitconfig_sets_runner_identity*)\|(Gitconfig_pushes_over_ssh_only*)\|(Deploy_parent*)\|(Child_project_is_run_scoped_and_distinct*)\|(Host_compose_uses_the_deployed_tag_not_this_runs_sha*)` | V-6,V-7,R-3 | All 8 methods: 2 gitconfig + 4 Deploy_parent + 2 project/tag guards; 0 failed/skipped | 8 | 3 |

## Checkpoint execution and reporting

Run the corresponding command from the Code worktree, foreground, once per row. The wrapper
builds once, runs with `--no-build`, parses fresh TRX and reports counts. No `--list-tests` or
additional broad validation build is necessary. If a prerequisite adds methods, report the actual
roster/count and preserve the named floors; never infer a pass from exit zero alone.

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c631-a/ -Filter '/*/*/(PhoneHomeCommandDispatcherTests*)|(PhoneHomeConnectionServiceTests*)/*' -MinExecuted 17 -Expect Dispatch_replies_with_error_frame_when_handler_throws_unexpected_exception,Receive_loop_writes_error_frame_when_dispatch_throws,Concurrent_reply_and_heartbeat_never_drop_a_reply,Claude_launch_is_refused_when_the_probe_says_logged_out,Provider_auth_operation_answers_the_probe_and_refuses_unknown_providers -ResultsRoot .antiphon/c631-checkpoints
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-2 -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c631-b/ -Filter '/*/*/RunnerWorkspaceServiceTests/*' -MinExecuted 8 -Expect Mirror_clones_repository_when_absent,Mirror_failure_is_admission_error_not_crash,Mirror_refuses_sha_mismatch -ResultsRoot .antiphon/c631-checkpoints
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-3 -Project tests/Antiphon.Tests -OutputPath bin-c631-c/ -Filter '/*/*/(DindRunnerContractTests*)|(RemoteScriptContractTests*)/(Gitconfig_sets_runner_identity*)|(Gitconfig_pushes_over_ssh_only*)|(Deploy_parent*)|(Child_project_is_run_scoped_and_distinct*)|(Host_compose_uses_the_deployed_tag_not_this_runs_sha*)' -MinExecuted 8 -Expect Gitconfig_sets_runner_identity,Gitconfig_pushes_over_ssh_only,Deploy_parent_seeds_or_verifies_runner_checkout,Deploy_parent_probes_the_phone_home_secret_as_the_app_uid,Deploy_parent_refuses_a_runner_whose_phone_home_keeps_failing,Deploy_parent_retires_its_own_superseded_images,Child_project_is_run_scoped_and_distinct,Host_compose_uses_the_deployed_tag_not_this_runs_sha -ResultsRoot .antiphon/c631-checkpoints
```

Report `CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N
passed=N failed=N skipped=N trx=<path>` and any reruns with reasons. Keep an exact inventory
of produced alternate output directories; remove only those paths, after checking that each
resolved absolute path is within this worktree, before settlement. No source edits while a
verification command is running. Ordinary green reports leave PCs pending for post-land Mutation.

## Post-land deploy and live acceptance

This is a separate operator/deploy follow-up, not an extra Code checkpoint or a blocker to Code.
Estimated active time: deploy/image build and checkout verification 30-40 min; one task proof
10-20 min, excluding operator credential wait. The standing server2 stack is production; coordinate
its owner before activation. Use the existing `verify-docker-stack.ps1 -Case deploy-parent
-Manifest <run manifest>` flow, with the manifest pinning the landed source SHA/branch, not a
test's stub mode. Preserve CARD-0628 image and provider setup.

1. Confirm the landed runner image revision and registration buildVersion, not just health. On a
   fresh volume, the new preflight must refuse Missing until lazy mirror/controlled anonymous
   provisioning creates the uid-1654 checkout; rerun the preflight and record its repository path,
   successful anonymous fetch and FETCH_HEAD. Do not infer the container path from the host path.
2. Verify `git var GIT_AUTHOR_IDENT` as uid 1654 in that checkout reports the baked machine identity
   without user config. A scratch commit with no per-repository identity succeeds; record its SHA.
3. Operator precondition: the existing public deploy key has write access on GitHub. This gates
   only the following live commit/push/settlement proof. Provider sign-in is separately whatever
   the selected already-supported remote agent requires; a missing sign-in is not this fix's result.
4. Dispatch one ordinary Worktree task pinned to `-Runner server2` through `scripts/delegate.ps1`
   (not a SourceLanding/card-backed task). Give it a unique nonce and a bounded change to a
   task-owned evidence file, then require a commit and push on its own task branch. Observe the
   actual mirror request/reply and exact requested SHA, task `Working`, runner session Running,
   and the complete matching UserPrompt in the runner transcript. A screen redraw/ack is insufficient.
5. After settlement, compare remote branch tip, produced commit SHA and desktop task-worktree
   HEAD. They must be identical via the existing fast-forward sync. Keep task/session IDs and
   the transcript/commit receipts. Report any sync warning; do not reset or force-push to conceal it.
   Use normal retirement only after the receipt is captured; preserve dirty residue.

If the key is still unregistered, report live E2E pending with that specific operator precondition;
do not label the already completed Code rounds blocked or the live proof passed.

## Handoff

Next: **Code, Round A only**, S1-S2 and CP-1, on the landed CARD-0628 Round B base.
Round B (S3/CP-2) and Round C (S4-S5/CP-3) follow as separate bounded Code dispatches with ordinary
Review/landing and the same plan artifact. Final live acceptance requires the separate receipts above.
