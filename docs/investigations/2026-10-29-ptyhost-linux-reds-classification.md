# CARD-1614: PtyHost Linux red classification

Classification report complete; the requested isolated execution matrix is incomplete: 9/15 mode-specific invocations executed (4 passed, 5 failed, 0 skipped), 6 not run within the 60-minute budget. Two builds passed. No product/test/helper/image/host configuration fix was made.

The original five launcher reds are missing build artifacts; a valid Linux apphost build gives 4 passes and exposes a readiness race in the fifth. The inbox fixture is Windows-specific. Real custody defects are confirmed in root identity, termination and automatic sealing; the old missing-daemon symptom also involves an unsynchronized fixture. The live runner currently advertises no custody backend.

Assigned source: `8edda999623a473be2d1970727c172bfb5088d59`; branch `feat/card-task-19964ed5`.
Date: 2026-10-29 UTC. Native host: server2 runner container, Linux x86_64, kernel
`4.15.0-213-generic`, uid/gid 1654. Evidence root:
`.antiphon/evidence/card-1614-19964ed5/` (generated, ignored).

## Scope and inherited evidence

CARD-1614 (Antiphon board, id `370efd86-0d70-481d-9b44-fcf82bb607b6`, revision 0)
records 91 tests: 10 failed, 56 passed, 25 skipped at `16477bad0` plus
`4b121a76c6af356e7479ee592233aa6a2f748990`. The advertised prior report
`.antiphon/task-fbe4de0e.md` is absent from `origin/feat/card-task-fbe4de0e`.
The original terminal log was subsequently recovered through server transcript session
`7dc92c8d-6cae-4a84-8e12-b11d4772c444`: sequence 116 records the sweep
command, sequence 126 names its provider terminal log, and sequence 168 records
its source SHA and result. The original TRX is absent at its recorded path.
The retained `prior-sweep.log` has SHA-256
`cadeb4cfc39754c7669561685ccad3d91abea35ae8f204fd7affcd2b4f36252a`.
All nine compared cgroup/launcher product, helper, policy and test files are unchanged
between that sweep's source and this task's base (`source-comparison.json`).

## Initial evidence

- The isolated `UseAppHost=false` build succeeded: 0 errors, 1 inherited CS8620
  warning at `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs:191`; slot wait 195 s,
  slot held 11 s. See `build-noapp.log`.
- This host has cgroup v1 pids/freezer custody roots, root-owned mode 0755 helpers,
  `NoNewPrivs: 0`, `Seccomp: 0`, and no effective process capabilities. The
  installed helper SHA-256 hashes match the assigned source. See `environment.log`.
  Absence of cgroup v2 is not itself a missing prerequisite: the fallback is explicit
  in `src/Antiphon.Agents.Pty/IPtyCustodyContainment.cs:164` and
  `docker/session-runner-grok/antiphon-custody-enter.sh:48`.
- Live `GET http://127.0.0.1:8080/capabilities` at 18:29:10 UTC reports
  `platform=linux`, `verificationCustodyBackend=null`, without the custody feature.
  See `runner-capabilities.json`. This is an availability observation, not proof of
  a deployed-source SHA or an incident in an existing tracked session.
- The two Linux classes skip off Linux (`LinuxCgroupCustodyTests.cs:42`,
  `LinuxPtyHostLauncherTests.cs:20`) and have Windows exclusion rows in
  `tests/test-execution-policy.json:479` and `:812`. They are not Windows passes.
  `HostCustodyTests` is a mixed class; its inbox method has no OS guard (`:212`).

## Classification and native results

The first isolated execution reproduced
`HostCustodyTests.Inbox_is_unsupported_and_never_becomes_no_start_proof`: 1 failed,
0 passed, 0 skipped (229 ms test body, 180 s slot wait). The retained TRX and log
record `ErrorMessage` expected, `LaunchedMessage` actual at line 217. The fixture
sets `windows-job-v1` (`HostCustodyTests.cs:278`, `:293`) and sends `cmd.exe`
(`HostHarness.cs:65`), while Unix ignores the Windows PTY selector
(`src/Antiphon.Agents.Pty/PtyBackend.cs:90`). The Windows-only unsupported-backend
check is at `PtyAgentRunner.cs:149`; non-Windows tracked launches use cgroups at
`:153`. This is a test portability defect, with a host-level platform/binding
validation concern retained for the final report. Runner admission separately
checks platform/backend correspondence (`RunnerCustodyLedger.cs:142`).

The final execution matrix distinguishes fresh runs from reconstructed historical failures. Mechanisms and remaining uncertainties are recorded below.

## Confirmed custody mechanisms

### Tracked identity is the sudo supervisor, not the contained shell

The isolated tracked-PID test failed at `LinuxCgroupCustodyTests.cs:63`:
expected PID 13811 in the tree, actual PIDs [13814, 13821]. At 18:37:29.329 UTC,
retained /proc rows show 13811 is `sudo`, 13814 is its `sh` child, and 13821
is that shell's `sleep`. The sudo process remains in the container's original
pids/freezer cgroups; both payload processes are in execution container
`d4c73def-3815-4d72-ad24-a30681fc5203/tree`.
See `noapp-LinuxCgroupCustodyTests.Tracked_launch_places_child_in_tree.observations.jsonl`.
The journal's `tracking.json` records 13811; the host log calls that the launched
shell's PID. Test result: 1 failed, 0 skipped, 120.199 s body/teardown, 345 s slot wait.

`PtyAgentRunner.cs:193` records Porta's PID. `IPtyCustodyContainment.cs:94`
rewrites the executable to sudo. The placement helper moves its own PID and then
executes the payload (`antiphon-custody-enter.sh:96`, `:163`). The shipped sudo
policy assumes direct execution but disables only `use_pty` and `pam_session`
(`docker/session-runner-grok/sudoers-antiphon-custody:18`). Read-only `sudo -n -l`
confirms those effective settings on sudo 1.9.13p3. PAM credential establishment
also affects whether sudo retains a supervisor, and is enabled by default; see the
[upstream sudo manual](https://www.sudo.ws/docs/man/1.9.14/sudoers.man.pdf)
under `pam_setcred`. The retained parent/child and cgroup rows directly establish
the mechanism here; no policy changes were made.

### Kill helper fails before termination

The installed helper has the exact source SHA-256. A cleanup invocation against the
finished, empty, test-owned tree returned exit 1 and this unedited stderr:

`/usr/local/bin/antiphon-custody-kill: line 91: id: unbound variable`

See `owned-tree-cleanup.json` (18:44:45 UTC). In
`docker/session-runner-grok/antiphon-custody-kill.sh:91`, the same `local`
statement assigns `id` and expands it in two later arguments. Bash expands those
arguments before assigning the local variables; `set -u` at line 14 stops the
helper. A read-only shell expansion reconstruction has the same failure
(`bash-local-expansion.json`). No kill or freezer operation is reached.
The v2 helper has the same declaration shape at line 72; the enter helper's v2
preparation also has it at `antiphon-custody-enter.sh:57`. Native v2 was not run.

`LinuxCustodyOperations.Kill` returns a failed termination observation on nonzero
exit (`IPtyCustodyContainment.cs:137`), then the runner reads the still-populated
tree (`PtyAgentRunner.cs:524`), and the host reports Draining
(`HostCustodyJournal.cs:101`). This reconstructs the original
`Seal_terminates_and_observes_zero` and `Second_seal_is_idempotent` failures
at lines 134 and 241; their historical durations were 300.102 s and 300.082 s.
The second test never reached its second seal, so it gives no idempotency verdict.
The runtime startup probe rejects a failed helper (`LinuxCgroupCustodyProbe.cs:32`),
consistent with the live null backend. No production-session outcome was inferred.

### Root exit has both a fixture race and an automatic-seal defect

The original run timed out after 20.160 s waiting for `daemon.pid`
(`prior-sweep.log:148`). The script backgrounds `setsid sh` and immediately
exits without a readiness handshake (`LinuxCgroupCustodyTests.cs:101`). That
fixture does not establish that the detached process has installed its session
and written its PID before the parent exits. The exact signal/scheduling history
of the old run is unavailable; no missing executable or privilege is established.
The prior report records exactly four failures and four passes in this eight-method,
non-parameterized class. Its complete failed-method set therefore implies that
`Double_fork_and_setsid_stay_in_tree` passed in the same sweep; that companion uses
the same daemon command but keeps the root alive with `sleep 120` (lines 79-81).
This is a reconstruction from the stored class counts and failure roster, not a
new per-method receipt. It supports a root-exit/setup race rather than missing
`setsid` or a generally unwritable scratch directory.

On the unchanged source, the isolated rerun wrote `daemon.pid=19989`, passed
its live-descendant membership assertion at line 107, then failed at line 111:
`producer-seal.json` existed before the explicit custody request at line 114.
Result: 1 failed, 0 skipped, 401 ms body, 435 s slot wait. Retained records:

- Host log: root exit code 0 at `2026-10-29T18:47:15.1156774Z`.
- Seal record revision 3: `2026-10-29T18:47:15.1215528Z`.
- Tracked root PID 19968; detached sleep PID 19989, PPID 1, session/group 19989;
  custody container `dead6b31-ec07-42d8-9b71-82730c2d5a57`.

The host starts its observer after root exit (`HostSession.cs:404`). Its ordinary
read forces sealing for `PtyHostStatus.Exited` at line 332; the journal then calls
`SealAndObserveCustodyAsync` (`HostCustodyJournal.cs:101`), which records a seal
and invokes termination on Linux (`PtyAgentRunner.cs:512`). This is a confirmed
product mechanism that the older missing-marker failure masked. Windows job
observation and Linux terminate-at-seal have different consequences here. The
existing native test requires live descendants to remain unsealed until its
explicit request; R-9 in
`docs/superpowers/plans/2026-09-22-card-0604-persistent-runner-dind-plan.md:1083`
names this boundary. A working kill helper would make this automatic path capable
of terminating those descendants; that consequence is source reconstruction,
not a deliberately repaired native execution.

### Apphost comparison

The baseline output has managed host DLL/deps/runtimeconfig but no extensionless
`Antiphon.PtyHost`. Its native libraries are under `runtimes/<rid>/native`,
not at the root (`noapp-layout.json`). The test project is plain project references
with no `CopyOwnedPtyHostAppHost` target
(`tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj:21`). That special
staging target belongs to `tests/Antiphon.Tests/Antiphon.Tests.csproj:168`;
`docs/testing-and-build.md:529` explains the checkpoint default and its narrower
staging exception at line 531.

A second isolated build uses `UseAppHost=true --runtime linux-x64`, matching the
native RID used by the runner Docker publishes
(`docker/session-runner-grok/Dockerfile:39`). It succeeded with 0 errors and the
same inherited warning; wait 75 s, held 11 s. It was built at documentation-only
commit `d67393258334d6255fd5da217fedd5c62f991f80`, with unchanged product/test bytes from the assigned base.
Both the executable and root native library are present. The first native launcher
method, `Shadow_host_exchanges_bytes_and_exits`, passed alone (1/1, no skips,
1.408 s run, wait 30 s). The next method, `Detach_creates_a_new_session`, failed alone despite the correct
artifacts: expected host session ID 27622, observed parent session ID 4717
(243 ms body, 165 s slot wait). `PosixProcessSpawner.StartDetached` returns
immediately after `Process.Start` (`src/Antiphon.PtyHost/PosixProcessSpawner.cs:46`);
the child performs `setsid` only later, at line 67. The intermediary reports the
PID at `Program.cs:14`, and `PtyHostLauncher.cs:160` waits for the intermediary,
not detached-child readiness. The test reads `getsid` immediately at
`LinuxPtyHostLauncherTests.cs:107`. This is a test readiness race masked by the
original missing apphost. Production separately connects to the child pipe before
using the session (`SessionRunnerRuntime.cs:3231`). The other two methods also passed alone: pipe EOF/stdio (0.872 s run) and canceled launch/cleanup (1.780 s run). The apphost comparison is complete: 4 passed, 1 failed, 0 skipped.

The live runner also already has `/app/Antiphon.PtyHost` and
`/app/libporta_pty.so` (`live-launcher-layout.log`). Its production launcher
artifacts are not the deficient baseline test output.

## Per-test classification

Classes below describe the original red, with newly exposed failures stated separately.
`H` = HostCustodyTests; `C` = LinuxCgroupCustodyTests; `L` = LinuxPtyHostLauncherTests.
The Investigate stage forbids fix design; the requested minimal-fix column records
that boundary. The single repair note below is for the next stage.

| Test | Cause class and evidence | Can this pass merely by provisioning Linux correctly? | Minimal fix | Owner | Risk |
|---|---|---|---|---|---|
| H.Inbox_is_unsupported_and_never_becomes_no_start_proof | **TEST DEFECT**: Windows fixture and expectation on Unix; fresh expected ErrorMessage / actual LaunchedMessage; H:212, :278, :293, PtyBackend.cs:90. Host platform-binding validation gap also observed. | No: Unix deliberately ignores the inbox selector. Native Windows result was not measured. | Deferred to Plan | PtyHost test + host admission owners | Medium: platform guard alone would conceal the host's acceptance of a Windows binding on Unix; runner admission currently fences it. |
| C.Tracked_launch_places_child_in_tree | **PRODUCT DEFECT**: fresh recorded sudo PID 13811 outside the tree, shell/sleep 13814/13821 inside; C:63; PtyAgentRunner.cs:193; shipped sudo policy:18. | No with the shipped policy/identity assumption; this host already has the required roots/helpers. | Deferred to Plan | Session-testing image + custody identity owners | High: recorded root identity differs from the contained root. |
| C.Root_exit_with_live_descendants_is_draining_until_seal | **TEST DEFECT** in original missing-marker setup (unacknowledged background start, C:101); **PRODUCT DEFECT** beneath it: fresh native run reaches C:111 and finds an automatic seal before the explicit request. | Not reliably: provisioning does not synchronize the fixture or change the automatic-seal path. Exact old-run signal is unknown. | Deferred to Plan | PtyHost lifecycle + native test owners | High: a working termination helper makes root-exit observation capable of killing live descendants. |
| C.Seal_terminates_and_observes_zero | **PRODUCT DEFECT**: original Draining at C:134; installed kill helper exits 1 at line 91 before termination; population remains nonzero. | No with the shipped helper. v2 is not a remedy for the same declaration defect. | Deferred to Plan | Linux custody helper owner | High: sealing cannot terminate the tree; tests wait out sleep 300. |
| C.Second_seal_is_idempotent | **PRODUCT DEFECT**: same helper failure; original first seal is Draining at C:241. The second seal is never reached. | No with the shipped helper; idempotency itself remains unqualified. | Deferred to Plan | Linux custody helper + receipt owners | High: no first terminal receipt, hence no idempotency evidence. |
| L.Shadow_host_exchanges_bytes_and_exits | **ENVIRONMENT** (test build artifacts): missing apphost at L:35 in original output; native apphost/RID build passes this method alone. | Yes, observed with apphost and root native library. | Deferred to Plan | Linux test build/staging owner | Low for this original failure; ordinary native byte exchange is now witnessed. |
| L.Detach_creates_a_new_session | **ENVIRONMENT** originally (missing apphost); **TEST DEFECT** newly exposed: fresh native host session ID still equals parent ID at L:108 because no child-readiness wait precedes getsid. | Artifacts remove the original error, but this method remains racy. | Deferred to Plan | Linux test build/staging + launcher test owners | Medium: a timing check was masked; production waits for a pipe connection. |
| L.Detach_failure_exits_without_pipe_readiness | **ENVIRONMENT**: original Process.Start cannot find executable at L:136. Fresh apphost run passes alone. | Yes, observed. | Deferred to Plan | Linux test build/staging owner | Low for original red; this assertion alone does not qualify every native detach failure. |
| L.Intermediary_pipes_reach_eof_while_host_lives | **ENVIRONMENT**: original Process.Start lacks apphost at L:172; fresh native apphost run passes alone (1/1). | Yes, observed. | Deferred to Plan | Linux test build/staging owner | Original red was artifact-related; native EOF/stdio assertions now execute. |
| L.Canceled_launch_leaves_no_owned_host | **ENVIRONMENT**: original FileNotFoundException precedes expected cancellation at L:210; fresh apphost run passes alone (1/1). | Yes, observed. | Deferred to Plan | Linux test build/staging owner | Original red was artifact-related; native cancellation/cleanup assertions now execute. |

## Windows gate and qualification limits

The brief's Linux-only description applies to the two Linux classes, not the mixed
`HostCustodyTests` class. Direct TUnit execution on Windows skips all eight cgroup
methods with `The linux-cgroup-v1 backend executes on Linux only.`, and all five
launcher methods with `Linux native pty-host methods execute on Linux only.`
(`LinuxCgroupCustodyTests.cs:42`; `LinuxPtyHostLauncherTests.cs:20`). The Windows rc
profile instead has explicit excluded roster rows owned by CARD-0590
(`tests/test-execution-policy.json:479`, `:812`); the latter is also covered by the
platform inventory at `scripts/test-rc-platform.ps1:538`. Neither is a Windows pass.
`.github/workflows/ci.yml:44` runs the PtyHost assembly on windows-latest with the
default apphost-producing build. No native Windows receipt was obtained for the inbox
method, which remains admitted there and targets Windows behavior.

Confirmed means a mechanism was reproduced or reconstructed from the stored evidence;
it does not mean repaired or fully qualified. Remaining limits:

- The old missing-daemon run has no retained syscall/signal trace. Its unacknowledged
  background-start race is visible in the fixture; the fresh run proves it can get past
  that point and expose the separate automatic-seal defect. No claim of the exact old
  death signal is made.
- cgroup v2 was not executed. Its same-statement local-variable expansion issue is
  source reconstruction, not native v2 qualification.
- Native Windows behavior of the inbox method was not rerun. The two Linux classes
  provide skips/exclusions there, not green Linux custody evidence.
- Live capabilities and installed helper hashes were observed directly. The running
  server/runner assembly SHA and outcomes of existing production sessions were not
  established; no deployment or restart occurred.
- Apphost comparison intentionally changes both `UseAppHost` and RID, because the
  launcher fixture requires a root native library as well as the executable. It is a
  comparison with the production Linux payload layout, not a single-variable toggle.
- Isolated execution coverage is reported below independently of reconstructed causes.

## Execution matrix and requested runs left incomplete

Every fresh invocation selected one exact method and acquired its own build slot. No broad rerun, unleased driver, host reconfiguration or product/test mutation was used. The final queue admission was bounded by the remaining task budget; a slot timeout is not a test failure.

| UseAppHost / RID | Exact method | Fresh result | TRX test duration | Slot wait |
|---|---|---|---|---|
| false | HostCustodyTests.Inbox_is_unsupported_and_never_becomes_no_start_proof | Failed | 00:00:00.2293844 | 180 s |
| false | LinuxCgroupCustodyTests.Tracked_launch_places_child_in_tree | Failed | 00:02:00.1996002 | 345 s |
| false | LinuxCgroupCustodyTests.Root_exit_with_live_descendants_is_draining_until_seal | Failed | 00:00:00.4014752 | 435 s |
| false | LinuxCgroupCustodyTests.Seal_terminates_and_observes_zero | NOT RUN | — | budget exhausted |
| false | LinuxCgroupCustodyTests.Second_seal_is_idempotent | NOT RUN | — | budget exhausted |
| false | LinuxPtyHostLauncherTests.Shadow_host_exchanges_bytes_and_exits | Failed | 00:00:00.0982686 | 240 s |
| false | LinuxPtyHostLauncherTests.Detach_creates_a_new_session | NOT RUN | — | slot wait timed out |
| false | LinuxPtyHostLauncherTests.Detach_failure_exits_without_pipe_readiness | NOT RUN | — | budget exhausted |
| false | LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives | NOT RUN | — | budget exhausted |
| false | LinuxPtyHostLauncherTests.Canceled_launch_leaves_no_owned_host | NOT RUN | — | budget exhausted |
| true + linux-x64 | LinuxPtyHostLauncherTests.Shadow_host_exchanges_bytes_and_exits | Passed | 00:00:00.8535627 | 30 s |
| true + linux-x64 | LinuxPtyHostLauncherTests.Detach_creates_a_new_session | Failed | 00:00:00.2433394 | 165 s |
| true + linux-x64 | LinuxPtyHostLauncherTests.Detach_failure_exits_without_pipe_readiness | Passed | 00:00:00.2404907 | 270 s |
| true + linux-x64 | LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives | Passed | 00:00:00.3573759 | 300 s |
| true + linux-x64 | LinuxPtyHostLauncherTests.Canceled_launch_leaves_no_owned_host | Passed | 00:00:01.2498896 | 330 s |

The original sweep remains separate: 91 total, 10 failed, 56 passed, 25 skipped, 21m 25s 778ms. Its unedited terminal log records `BUILD SLOT granted lease=5a7b6e77-c2b3-4741-8ac6-8770b049cc6c waited=0s maxcpucount=6` and `BUILD SLOT released lease=5a7b6e77-c2b3-4741-8ac6-8770b049cc6c held=1287s`. It is not substituted for the 6 requested isolated invocations that did not execute.

To complete exactly those rows, rebuild through the same gate and run their one-method filters. Baseline build:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1614-noapp-build -- dotnet build tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj --property:OutputPath=bin-card1614-noapp/ --property:UseAppHost=false -nodeReuse:false`

Native payload build:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1614-app-build -- dotnet build tests/Antiphon.PtyHost.Tests/Antiphon.PtyHost.Tests.csproj --property:OutputPath=bin-card1614-app/ --property:UseAppHost=true --runtime linux-x64 -nodeReuse:false`

Each baseline method uses:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1614-one -- dotnet run --project tests/Antiphon.PtyHost.Tests --no-build --property:OutputPath=bin-card1614-noapp/ --property:UseAppHost=false -- --treenode-filter '/*/*/<Class>/<ExactMethod>' --report-trx --report-trx-filename <unique>.trx --results-directory <owned-evidence>`

For the apphost comparison use `bin-card1614-app/`, `UseAppHost=true` and `--runtime linux-x64` before the test-argument separator. Fixture evidence was retained with `ANTIPHON_PTYHOST_TESTS_KEEP=1` and an owned short TMPDIR. Each TRX and log has a unique mode/class/method filename in the evidence root. No source tests were altered to change their result or their 120/300-second fixture waits.

Pending exact rows:

- `noapp-LinuxCgroupCustodyTests.Seal_terminates_and_observes_zero`
- `noapp-LinuxCgroupCustodyTests.Second_seal_is_idempotent`
- `noapp-LinuxPtyHostLauncherTests.Detach_creates_a_new_session`
- `noapp-LinuxPtyHostLauncherTests.Detach_failure_exits_without_pipe_readiness`
- `noapp-LinuxPtyHostLauncherTests.Intermediary_pipes_reach_eof_while_host_lives`
- `noapp-LinuxPtyHostLauncherTests.Canceled_launch_leaves_no_owned_host`

## Provenance and cleanup

The baseline build is stamped at `8edda999623a473be2d1970727c172bfb5088d59`; the apphost build at documentation-only `d67393258334d6255fd5da217fedd5c62f991f80`. Build provenance JSON records the SHA-256 of all seven Antiphon assemblies in each output. Only this report and its card-named index changed in Git. Generated logs, TRX, fixture records and observations remain ignored. Essential outcomes, timestamps, identities and source locations are retained here.

The recovered provider log is the exact path named by normalized transcript sequence 126: `/state/grok/sessions/%2Fwork%2Fworktrees%2Ftask-fbe4de0e/7dc92c8d-6cae-4a84-8e12-b11d4772c444/terminal/call-0da101b4-c42e-466e-acc0-efc4ca1924fc-75.log`. The server transcript is retained as `prior-transcript.json` (169 entries). No unrelated provider/session files were read.

No test-owned detached hosts or populated test cgroups remained in the ownership census. Three empty execution cgroup trees remain in both v1 hierarchies because the shipped cleanup helper fails: `d4c73def-3815-4d72-ad24-a30681fc5203`, `200a8ca3-5671-4bfb-b9ad-a19640904da6`, `dead6b31-ec07-42d8-9b71-82730c2d5a57`. They were not manually deleted outside scratch. Alternate output cleanup is recorded in `cleanup.json`; raw evidence remains in the assigned ignored evidence root.

## Not done, noted

Suggested order for Plan (not designed or implemented): restore the custody termination/root-identity contracts, resolve automatic root-exit sealing and host platform-binding validation, then address native fixture synchronization and the Linux apphost build/staging contract.
