# CARD-1614: PtyHost Linux red classification

Status: investigation in progress. No product, test, helper, image or host configuration changes.

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

Remaining isolated executions and apphost comparison are pending. A classification will be called
confirmed only when the failing mechanism is reproduced or reconstructed from retained
test output, fixture records and source. Remaining uncertainties will remain explicit.

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

## Not done, noted

No fix designed or implemented; repair choices and ordering await the completed evidence.
