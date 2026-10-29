# CARD-1614: Linux custody and PtyHost test repairs

Status: skeleton published for incremental completion; not implementation authority yet.
Base: `3a003152840f07911e027747ee15e4da58538cc7`. Scope: a written repair plan only.
Evidence: [full investigation](../../investigations/2026-10-29-ptyhost-linux-reds-classification.md).

## Decisions

- D-1: Keep the three confirmed product defects distinct from fixture/build defects; a Linux test exclusion cannot repair custody.
- D-2: Root exit with live descendants keeps custody open. Observation must not initiate termination. Explicit release remains available; no new automatic kill or stall deadline.
- D-3: Complete the six missing baseline reruns before implementation. Preserve expected reds as baseline evidence, never as a passing gate.
- D-4: The finished artifact goes to independent plan review by a different company's model before implementation.

## Ground truth

| Card assumption | Observed code/evidence | Planning consequence |
|---|---|---|
| Ten unexplained Linux failures | Investigation distinguishes artifacts, fixture races, identity, helper expansion and automatic sealing | Repair each cause with a production-path witness |
| cgroup v2 required | Shipped helpers support v1; observed host has v1 pids/freezer | Native v1 qualification required; native v2 remains separate evidence |
| Launcher failures imply broken production payload | Test output lacks apphost; observed live payload has it | Fix staging and readiness tests; do not infer deployed launch failure |
| Custody defects affect current production sessions | Live capability probe advertised no custody backend | Blast radius is source reachability until activation/session evidence exists |

## Product repair design

Line references below are to the base above. Each product fix follows a committed failing witness; build/fixture failure or zero selected tests is not that witness.

### P-1: wrong tracked root identity

`PtyAgentRunner.cs:193-200` records Porta's PID; `IPtyCustodyContainment.cs:94-98` has replaced the payload with `sudo`. The enter helper places its own PID before `exec setpriv` (`antiphon-custody-enter.sh:96-100,163`). Investigation observed the recorded outer sudo outside the tree, with its shell and sleep inside. The image policy (`docker/session-runner-grok/sudoers-antiphon-custody:18-23`) disables `use_pty` and `pam_session` but omits PAM credential teardown, which can retain a sudo supervisor.

Smallest candidate: add `!pam_setcred` to that existing uid-scoped policy, keep the two-command allowlist and the helper's uid/gid, supplementary-group drop and `no_new_privs` boundaries. Validate with `visudo -c`, the effective policy and native PID evidence in a separately provisioned candidate image. This is a candidate policy repair, not a proven no-fork guarantee on every sudo build. Do not install it into the running delegate's container. If the actual shipped sudo still supervises, stop this slice for focused investigation of the launch identity protocol; do not substitute the first cgroup member or a process-name/parent-PID census as identity authority.

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

Pending portable inbox fixture, daemon/launcher readiness and once-per-build native apphost staging.

## Implementation slices

Pending exact files and verification mapping. No product source changes are authorized by this Plan dispatch.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards).
Investigation samples are small, not measured p95: isolated tracked PID body/teardown 120.199 s; historical seal cases about 300 s each; native launcher runs 0.872–1.780 s except the readiness failure. Build slot waits reached 435 s and are separate from execution. Final section will specify phase recording, shared setup, bounded collection, and approximately 2x measured-p95 deadlines; no fabricated p95.

## Verification design

The first six checkpoint rows will be the missing exact baseline methods, followed by red-first witnesses and targeted green qualification. Native Linux and Windows lanes will remain explicit. Pending importer validation.

## Safe residue cleanup and release

Plan only: do not delete the three leftover cgroup trees in this dispatch. Final procedure will require exact literal paths, verified ownership and fresh empty membership before nonrecursive removal. Product fixes require rc and master assessment; no tags, publish, deployment or rc rebase here.
