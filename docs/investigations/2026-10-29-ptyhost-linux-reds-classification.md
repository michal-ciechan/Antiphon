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
Its four cgroup failure method names therefore need fresh identification.

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

## Not done, noted

No fix designed or implemented; repair choices and ordering await the completed evidence.
