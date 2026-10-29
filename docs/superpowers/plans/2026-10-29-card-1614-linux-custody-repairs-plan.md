# CARD-1614: Linux custody and PtyHost test repairs

Status: skeleton published for incremental completion; not implementation authority yet.
Base: `3a003152840f07911e027747ee15e4da58538cc7`. Scope: a written repair plan only.
Evidence: [full investigation](../../investigations/2026-10-29-ptyhost-linux-reds-classification.md).

## Decisions

- D-1: Keep the three confirmed product defects distinct from fixture/build defects; a Linux test exclusion cannot repair custody.
- D-2: Root exit with live descendants keeps custody open. Observation must not initiate termination. Explicit release remains available; no new automatic kill or stall deadline.
- D-3: Complete the six missing baseline reruns before implementation. Preserve expected reds as baseline evidence, never as a passing gate.
- D-4: The finished artifact goes to independent plan review by a different company's model before implementation.
- D-5: Use the existing direct-exec sudo design, with the missing credential setting and native proof; reject post-launch PID guessing and a new privileged broker unless this minimal repair is disproved.
- D-6: Stage the native PtyHost once at test build time. Reject a per-test `dotnet build`, global `UseAppHost=true` (shared-output collisions), and skipping Linux launcher tests to hide missing artifacts.
- D-7: Keep Windows inbox semantics explicitly Windows-only, and separately test host OS/backend rejection. Reject changing the Unix backend selector to make a Windows fixture pass.
- D-8: Treat v1 and v2 as separate native lanes, and Windows as a separate regression lane. An unavailable lane is incomplete verification, not a skip credited as green. No default fleet pin.

## Ground truth

| Card assumption | Observed code/evidence | Planning consequence |
|---|---|---|
| Ten unexplained Linux failures | Investigation distinguishes artifacts, fixture races, identity, helper expansion and automatic sealing | Repair each cause with a production-path witness |
| cgroup v2 required | Shipped helpers support v1; observed host has v1 pids/freezer | Native v1 qualification required; native v2 remains separate evidence |
| Launcher failures imply broken production payload | Test output lacks apphost; observed live payload has it | Fix staging and readiness tests; do not infer deployed launch failure |
| Custody defects affect current production sessions | Live capability probe advertised no custody backend | Blast radius is source reachability until activation/session evidence exists |
| Root exit needs only a host fix | Runner `CollectCustodyAsync` also escalates root exit to seal; tracked linger can dispose the observer | Repair the entire observation chain and retain custody until terminal |
| Every HostCustody test is Linux-only | Mixed class uses Windows bindings; inbox method has no OS guard | Method-scoped OS exclusion plus separate platform rejection coverage |
| Empty helper probes prove tracked identity | Probe creates/removes an empty tree and never launches a payload | Require real payload PID and membership evidence |

## Product repair design

Line references below are to the base above. Each product fix follows a committed failing witness; build/fixture failure or zero selected tests is not that witness.

### P-1: wrong tracked root identity

`PtyAgentRunner.cs:193-200` records Porta's PID; `IPtyCustodyContainment.cs:94-98` has replaced the payload with `sudo`. The enter helper places its own PID before `exec setpriv` (`antiphon-custody-enter.sh:96-100,163`). Investigation observed the recorded outer sudo outside the tree, with its shell and sleep inside. The image policy (`docker/session-runner-grok/sudoers-antiphon-custody:18-23`) disables `use_pty` and `pam_session` but omits PAM credential teardown, which can retain a sudo supervisor.

Smallest candidate: add `!pam_setcred` to that existing uid-scoped policy, keep the two-command allowlist and the helper's uid/gid, supplementary-group drop and `no_new_privs` boundaries. Validate with `visudo -c`, the effective policy and native PID evidence in a separately provisioned candidate image. The [upstream sudoers manual](https://www.sudo.ws/docs/man/1.9.14/sudoers.man.pdf) also requires no I/O logging or configured log server for direct execution; verify those conditions rather than weakening audit policy globally. This is a candidate policy repair, not a proven no-fork guarantee on every sudo build. Do not install it into the running delegate's container. If the actual shipped sudo still supervises, stop this slice for focused investigation of the launch identity protocol; do not substitute the first cgroup member or a process-name/parent-PID census as identity authority.

RED V-7: extend `LinuxCgroupCustodyTests.Tracked_launch_places_child_in_tree` through `HostSession.LaunchAsync -> StartTrackedAsync -> Porta -> installed enter helper`. The payload writes its own PID and a readiness nonce, then holds. Require equality of payload PID, `LaunchedMessage.ChildPid`, running manifest child PID and `tracking.json` PID/start time, and membership in every active hierarchy. Current code already fails membership. Retain `/proc` identity/start and helper/policy/image digests, not command arguments or environment. GREEN must also retain the existing child privilege/containment witness. This fixes the tracked **payload** root; `VerificationHostIdentity.HostPid` separately identifies the observing PtyHost and is not to be replaced.

### P-2: helper exits before termination

Under `set -u`, Bash expands all arguments of `local` before its assignments: `antiphon-custody-kill.sh:91` references `id` in the same statement that initializes it. `:72` and `antiphon-custody-enter.sh:57` repeat the shape for v2. Split each into an `id` declaration/assignment followed by dependent paths. Preserve validation, freeze/kill/thaw, deadlines and failure propagation. Correct the misleading no-kill comment on `PtyAgentRunner.cs:503` when separating observation below.

RED V-8/V-9: the first two checkpoint methods call `HostSession.GetCustodyAsync(seal:true) -> HostCustodyJournal -> PtyAgentRunner -> LinuxCustodyOperations.Kill -> installed helper` with a live contained payload. Require zero accounting, drained output, terminal receipt and successful termination; a second explicit seal returns identical durable bytes. The existing first-seal assertion fails on current code; the old second-seal run never exercised idempotency. Keep helper stderr/exit separate from accounting. Add a real candidate-image probe witness invoking `LinuxCgroupCustodyProbe.Detect()` and require both installed helpers' probes to succeed. v2 must exercise its native branch; a script text assertion is not native qualification.

### P-3: root exit implicitly authorizes termination

There are **two** seal escalations: `HostSession.cs:332` uses `seal || Status == Exited`, and `SessionRunnerRuntime.cs:4480` uses `HasExited || runner-seal-present`. The host's exit observer (`HostSession.cs:404,424`) starts ordinary reads. `HostCustodyJournal.cs:97-101` maps those to `SealAndObserveCustodyAsync`; `PtyAgentRunner.cs:512-529` writes the irreversible producer seal and terminates Linux containment. Fixing P-2 alone therefore makes this path more destructive. Ship P-2 and P-3 together.

Smallest coherent change spans `src/Antiphon.PtyHost/HostSession.cs`, `HostCustodyJournal.cs`, `src/Antiphon.Agents.Pty/PtyAgentRunner.cs`, and `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`:

- Carry explicit release intent from the persisted runner seal only; root exit is an observation, never that intent. Remove both automatic escalations.
- Split nonterminating accounting/finalization from explicit Linux seal-and-terminate. A running unsealed root remains Tracking. Root exited with live members is Draining without a producer seal or receipt. Read/identity/output errors are Unknown, never zero. The same retained container observer remains authoritative.
- On natural descendant-zero, under the existing admission/write serialization, fence further input, persist the producer seal, drain output, re-read zero and persist the receipt without invoking termination. A mere root exit or helper exit code cannot certify zero. Reuse the owned drain task and immutable receipt ordering; do not create a replacement container.
- Explicit Linux release persists its fence before termination, then requires observed zero and drained output. Failed termination with live/unknown accounting stays nonterminal. Windows explicit seal remains nonterminating, as `Live_job_seals_without_killing_and_late_input_and_launch_are_refused` requires.
- `StartLingerExpiry` (`HostSession.cs:445`) currently requests host exit on a timer even for tracked descendants. Withhold automatic linger disposal while tracked custody lacks a final receipt; retain the observer and explicit-release path. Ordinary untracked linger behavior is unchanged. Once custody is terminal, use the existing acceptance/shutdown contract. Lost host remains Unknown and retained residue, never reconstructed clean.

RED V-10: synchronize the existing root-exit daemon fixture, then require actual root exit, living detached child in the real cgroup, Draining on repeated ordinary reads, no seal/receipt and child still alive before explicit release. Exercise both direct host reads and the real runner's `ReadCustodyAsync(seal:false)` path; a host-only test would miss the second trigger. Add `LinuxRunnerCustodyLifecycleTests.C1614_Root_exit_read_does_not_seal_live_descendant` in SessionRunner.Tests using an isolated runtime/runner and real staged PtyHost/helpers, never the production runner.

Add native `LinuxCgroupCustodyTests.C1614_Natural_descendant_exit_finalizes_without_release` and `C1614_Root_exit_retains_observer_past_linger`: use controlled child release/readiness, a short test-only linger duration, receipt absence while alive, then natural exit/zero/drain and durable final receipt. These expose the otherwise tempting incomplete fixes (remove the force-seal but never finalize; keep the timer and abandon custody). An independently observed descendant survival before explicit release is mandatory. Retain receipt-before-ack, lost-host, cancellation/output-error and Windows nonterminating-seal regressions.

This changes custody observation, not an input-waiting session policy. A release still leaves the process killed, pooled warm or owned by a standing agent. A stall never authorizes kill. No automatic release deadline is introduced; with parking disabled, input-waiting sessions have none (CARD-1083).

### Reachability and port requirement

Verified in source: `SessionRunnerRuntime.StartAsync:455-471` selects tracked admission only when `RunnerLaunchRequest.VerificationBinding` exists. `RunnerCustodyLedger.PrepareStart:142-156` requires a matching advertised backend and PtyHost launch; the host receives that binding/backend at runtime lines 3226-3256. Unbound ordinary Claude/Grok/Codex sessions do not enter these custody helpers. SourceLanding verification launches are the intended tracked consumer; no currently running affected session was established. The launcher apphost path is shared more broadly by detached PtyHost launches.

Verified live evidence: the investigation's runner capabilities had `verificationCustodyBackend=null`; this Plan's runner catalogue read at 2026-10-29 ~19:25 UTC also omitted `verificationCustodyV1` from both Linux entries. This does **not** establish their assembly SHA, explain all deployment state, or prove past sessions unaffected. Runner defaults revision 2 and the runner catalogue were read; use current placement at dispatch, omit `-Runner`, and specify Linux/Windows only for the native lane. No fleet address is embedded here.

P-1, P-2 and P-3 require **rc and master**, including the image policy/helpers and affected managed binaries as one reviewed candidate; none is master-only. The test/staging fixes should travel with them so rc Linux qualification executes the repaired paths. Check target ancestry/content before porting; do not infer presence from a branch name. Caller owns independently reviewed ports and activation. No deployment, tags, publish, or rc rebase is part of this Plan. Before enabling custody after a rollout, qualify the candidate image then read actual loaded runner/host SHAs, helper/policy digests, capabilities and a new owned tracked launch/receipt. A health response or master land alone is not activation.

## Test and build repair design

**Inbox and host binding (V-13).** Add a Windows-only guard to `HostCustodyTests.Inbox_is_unsupported_and_never_becomes_no_start_proof`; retain its Windows assertions, including no native intent, no marker and no NeverStarted receipt. Its `cmd.exe` fixture is inappropriate on Linux. Do not skip the mixed class. Add `tests/Antiphon.PtyHost.Tests/HostCustodyPlatformTests.cs` with `C1614_Foreign_platform_binding_is_refused_before_native_start`, using the current OS's real benign marker command but an opposite-OS binding and matching host option. Current `HostSession.cs:125` only compares the two strings; `AdvertisesCustody` knows the OS but launch admission does not enforce it. Require error/no marker/no native intent/no terminal receipt even when option and binding agree with one another. Add the OS/backend check to host admission before native launch. Preserve the existing Windows inbox UnsupportedBackend journal result; do not introduce a NeverStarted receipt for unsupported custody. This related host admission repair also ports to rc and master. Normal runner admission already fences the mismatch; direct-host exposure and deployed incidence are unverified.

**Daemon synchronization (V-10/11/12).** In `LinuxCgroupCustodyTests`, use one test-owned readiness/release handshake: the `setsid` child publishes its PID and nonce only after it has detached and redirected stdio; the parent waits for that acknowledgement before exiting. The test verifies session identity and cgroup membership before releasing the root. Use file/FIFO barriers with finite cancellation, not an arbitrary sleep. A separate release lets the descendant exit naturally for V-11; a watchdog bounds a broken fixture and reports rescued cleanup as failure. Replace 120/300-second idle payload sleeps in the repaired fixtures with controlled holds and deterministic teardown only **after** S0 preserves their baseline costs. Every process remains owned until joined; an unobservable cleanup is a failing residue report, not swallowed best effort.

**Launcher synchronization (V-14).** In `LinuxPtyHostLauncherTests.Detach_creates_a_new_session`, connect to its unique pipe and complete Hello before reading `getsid(hostPid)`. Child `Program` performs detachment before pipe startup, so the protocol handshake establishes the relevant order. Assert returned session ID, host liveness, `getsid(hostPid)==hostPid` and inequality to the actual parent session ID; remove the unrelated requirement that the test process itself cannot be a session leader. Preserve the failure-without-readiness, stdio EOF and cancellation tests. Production already connects before use (`SessionRunnerRuntime.cs:3231`); no change to launch success semantics is proposed.

**Owned apphost (V-14/15).** Add a Linux-only `CopyOwnedPtyHostAppHost` target to `tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj`, following the existing Antiphon.Tests and SessionRunner.Tests targets. Restore then build the producer with `UseAppHost=true` and the current native Linux RID, once per outer build, into a producer-owned subdirectory of `$(TargetDir)`. Stage the executable with execute mode and the matching `libporta_pty.so` at the consumer root beside the managed host DLL/deps/runtimeconfig. Keep outer checkpoint `UseAppHost=false`. Bind inner restore/build intermediate/output paths to owned isolated directories; do not leak to daemon-held bin or mix different RID artifacts. `PtyHostLauncher` continues to resolve from `AppContext.BaseDirectory`; no fallback to `/app` or stale global binaries. The existing SessionRunner.Tests target at `:64` already stages an apphost; extend its native library/layout handling only as needed by the new Linux lifecycle fixture. Avoid broad target extraction in this repair.

Add a once-per-build staging-contract test (`LinuxPtyHostLauncherTests.C1614_Owned_payload_has_native_apphost_and_matching_library`) checking the complete matching layout before shadow copy. The five existing real launcher methods supply executable acceptance. A fresh **owned empty NuGet cache** build of CP-18 (cold run variant) must restore the apphost pack without borrowing a prior output; use a separate ignored manifest/output identity for that named variant. This is a declared build variant, not a repeated full suite. Image construction/provisioning is once per candidate image outside individual tests; no fixture edits the live helper installation.

**Classification and rosters (V-16).** Keep launcher tests Integration/PtyHost; native process startup alone does not justify Slow. Classify the real cgroup and new runner lifecycle classes as Slow + Integration with CARD-1614 reasons in each project's `slow-tests-allowlist.txt` while baseline cost and privileged setup warrant it. Split fast backend/receipt contracts from native Slow cases; no new test project is needed. Maintain assembly-local `ParallelLimiter<ProcessSpawnLimit>` and serialize native rows; never co-schedule Pty/FakeClaude with the other process-spawning assemblies. Add only the inbox **method** to Linux OS exclusions, keep the existing Linux-class Windows exclusions, add Windows exclusions for the new Linux runner class, and explicitly retain Linux native required rosters. Update `tests/test-execution-policy.json` and its hash using the owner tooling, and `tests/test-selection-policy.json` only if its selection census requires the new rows. Run each changed assembly's `TestClassificationGuardTests`, the policy/platform script census and the actual discovered-vs-executed census. Skips/exclusions require reason, owner and platform; do not convert failed prerequisites to passing counts. Check every OR prefix for collisions again after adding classes.

## Implementation slices

| Slice | Files and change | Tests / exit condition |
|---|---|---|
| S0 baseline | No product/test edits; record six remaining investigation invocations at unchanged source in an ignored evidence root | CP-1–6; expected red remains red, counts and exact failure captured |
| S1 witnesses and test setup | The three PtyHost test files above; new HostCustodyPlatformTests; new LinuxRunnerCustodyLifecycleTests; PtyHost.Tests.csproj and, if required, SessionRunner.Tests.csproj; deterministic readiness/cleanup only | CP-7–10 committed red against unchanged product; staging/launcher accepted by CP-18 later |
| S2 image prerequisites | `docker/session-runner-grok/sudoers-antiphon-custody`, `antiphon-custody-enter.sh`, `antiphon-custody-kill.sh`; candidate image built from this exact commit, digest recorded | CP-11 probe + CP-12 PID green, CP-13 runner read intentionally red; helper baseline red is CP-1/2. No production activation |
| S3 lifecycle and admission | `src/Antiphon.Agents.Pty/PtyAgentRunner.cs`, `src/Antiphon.PtyHost/HostSession.cs`, `HostCustodyJournal.cs`, `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs` | CP-14–23 native and contract closure; fix both auto-seal callers, natural zero, linger and host platform admission |
| S4 qualification metadata | Per-project Slow allowlists, `tests/test-execution-policy.json`, necessary selection catalog/hash and `scripts/test-rc-platform.ps1` inventory assertions; update `docs/session-runtime-invariants.md`, `docs/testing-and-build.md`, ADR only where behavior statements changed | CP-24–27 classification/census; receipt and port review |

Commit and push every witness before its fix and every slice before its checkpoint run. The product/helper candidate is not releasable between S2 and S3. Prepare classification entries with S1 if needed to keep new tests classified, then freeze their final metadata in S4. Any source changes after a green row invalidate that row's source claim: rerun only affected rows at the final reviewed SHA, preserving earlier receipts. Code does not execute post-land mutation PCs; ordinary Review and land precede SourceLanding Mutation.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards).
Investigation samples are small, not measured p95: isolated tracked PID body/teardown 120.199 s; historical seal cases about 300 s each; native launcher runs 0.872–1.780 s except the readiness failure. Build slot waits reached 435 s and are separate from execution. Final section will specify phase recording, shared setup, bounded collection, and approximately 2x measured-p95 deadlines; no fabricated p95.

## Verification design

The first six checkpoint rows will be the missing exact baseline methods, followed by red-first witnesses and targeted green qualification. Native Linux and Windows lanes will remain explicit. Pending importer validation.

## Safe residue cleanup and release

Plan only: do not delete the three leftover cgroup trees in this dispatch. Final procedure will require exact literal paths, verified ownership and fresh empty membership before nonrecursive removal. Product fixes require rc and master assessment; no tags, publish, deployment or rc rebase here.
