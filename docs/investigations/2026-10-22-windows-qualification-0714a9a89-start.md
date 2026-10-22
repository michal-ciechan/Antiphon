# Windows rc qualification start for 0714a9a89 (task 2a34ba8c, 2026-10-22)

Outcome: the obsolete `rc-2026-10-20-ceb6e3d6b784` qualification was stopped and a fresh Windows
rc-profile qualification of `rc/2026-10-20` at `0714a9a89e4bff65aae7e0b4b94098694aeb931c` was
started, detached. No gate credit was claimed, no tag or release script was run.

## Documented procedure and what the docs do not say

- Start: [release gates](../release-gates.md#cutting-testing-and-publishing-a-candidate).
  *Cut* is `scripts/release-candidate.ps1 -Ref rc/YYYY-MM-DD`, which journals an existing
  long-lived tip (record only, no push). *Test* is
  `nightly-run.ps1 -Profile rc -Trigger rc -Ref <candidate> -ExpectedSha <sha>` against
  `C:\Antiphon\releases\checkout` and the candidate's private state root. The documented
  `-NoSync` hop runs a separately prepared, cleaned, marked checkout at the pinned SHA after
  the remote and candidate journal are rechecked. `scripts/release-cut.ps1` was **not** used,
  because it publishes on green. The run goes through `scripts/build-slot.ps1`
  ([testing and build](../testing-and-build.md), Build slots).
- Watchdogs: the per-suite values in `tests/test-execution-policy.json` (antiphon 60 min,
  session-runner 30 min, e2e 120 min; [testing and build](../testing-and-build.md#windows-rc-watchdogs-and-gate-lanes-2026-10-1819)).
  No override was passed.
- **Missing from the docs:**
  - There is no operator stop path for a running rc qualification: no stop verb and no
    cancel file.
  - There is no removal procedure for a dead-owner `C:\Antiphon\verification\native-run.lock`.
    `Enter-ReleaseGateNativeLock` returns `stale-shared-lock` and never reclaims it.
  - There is no read-only status command for an rc run.
  - The detached wrapper with its hang heartbeat is task-local tooling. It came from task
    a5303e1a and is not a documented script.

  This run reused that wrapper unchanged except for the SHA, candidate, state and evidence
  names. Removing the stale lock by hand followed a5303e1a's precedent: the bytes were kept and
  the dead owner was proven first.

## Stopped: old run

| Fact | Value |
|---|---|
| Candidate / run | `rc-2026-10-20-ceb6e3d6b784` / `27a1b370ab984049a08da85a184939e5` |
| Supervisor | pid 296224 `pwsh -File C:\Antiphon\releases\task-a5303e1a\supervise-rc.ps1` (parent 155728 already gone), created 2026-10-22 02:57:11 local |
| Tree stopped 16:04:11Z | 296224 supervisor, 257884 launch-rc.ps1, 68976 build-slot.ps1, 328000 nightly-run.ps1 (native lock owner), 364804 nightly-tests.ps1 hop (decoded: RunId 27a1b370, candidate ceb6e3d6b784), 50384 `Antiphon.Tests.exe` (antiphon-all-group-51), 174872 run-checkpoint.ps1 fixture child, 2 conhosts |
| Ownership proof | every member's command line was under `C:\Antiphon\releases\checkout` or `task-a5303e1a`; no orphan referencing the release clone/candidate remained |
| Progress at stop | 47 antiphon TRX (group 50 written 17:00 local); heartbeat 16:02:55Z |
| Lock | `native-run.lock` (pid 328000, run 27a1b370) copied, owner proven dead, removed |

Nothing outside that tree was touched. The session runner, AppHost, Docker and agent sessions
were left alone. Evidence for the stop is in `C:\Antiphon\releases\task-2a34ba8c\`:
`stopped-tree-before.json`, `stop-log.txt`, `old-native-run.lock.json` and
`old-last-heartbeat.json`.

## Started: new run

| Fact | Value |
|---|---|
| Candidate id | `rc-2026-10-20-0714a9a89e4b` (journal `candidate.json`, `pushed=false`) |
| Run id | `d2777e21ef4c4a3e9b1fb315ff8f5602` |
| Supervisor pid | 133964 (launched via `Win32_Process.Create`, outside this session's job), child launch-rc pid 354100; native lock owner pid 389996 |
| Started | 2026-10-22T16:06:40Z; build slot granted, waited 0s, maxcpucount 4 |
| Command | `build-slot.ps1 -Label rc-0714a9a8-windows-qualification -SlotWaitMinutes 90 -- pwsh -File scripts\nightly-run.ps1 -NoSync -Profile rc -Trigger rc -Ref rc/2026-10-20 -ExpectedSha 0714a9a89e4bff65aae7e0b4b94098694aeb931c -CandidateId rc-2026-10-20-0714a9a89e4b -CheckoutRoot C:\Antiphon\releases\checkout -StateRoot ...\state-2a34ba8c -LogRoot ...\logs -ReleaseRoot C:\Antiphon\releases -CoordinationRoot C:\Antiphon\verification` |
| State | `C:\Antiphon\releases\candidates\rc-2026-10-20-0714a9a89e4b\state-2a34ba8c\last-run.json` |
| Logs | `C:\Antiphon\releases\candidates\rc-2026-10-20-0714a9a89e4b\logs\2026-10-22-1606-d2777e21\` |
| Evidence | `C:\Antiphon\releases\task-2a34ba8c\` (prepare/supervisor/commission consoles, heartbeat, exit file) |
| Status (read-only) | `pwsh -NoProfile -File C:\Antiphon\releases\task-2a34ba8c\status.ps1` |

Preparation (`prepare.ps1`):

1. Checked that the ownership markers are intact and core.longpaths is true.
2. Confirmed `FETCH_HEAD` and the remote tip both equal the pinned SHA.
3. Ran `reset --hard`, then the 92-line `git clean -fdx`, which excludes the markers. The
   marker bytes were unchanged afterwards.
4. Journalled the candidate with the clone's own `release-candidate.ps1`.
5. Rechecked that there was no residue and that the remote had not moved.

## Group count and ETA

The tool does not state a total up front. At ceb6e3d6 the old run's plan preview listed **170
antiphon launches** and an estimated serial time of 7.8 h for that suite. The old run actually
completed 47 launches in about 13.7 h. The suite admits only about 46 UIDs a minute through the
one-slot `ProcessSpawnLimit`, and its long 900 s+ groups run early. The eight other rc suites
follow it.

There is no reliable ETA. The antiphon suite alone is at least 8 h and, at the old run's pace,
well over a day. The old run's group-10 watchdog kill set
`coverageComplete=false`, and the same can recur here.

Free memory at start was about 8.1 GB of 32.7 GB.

## Not done

- `publish-release.ps1`, `promote-release.ps1` and `release-cut.ps1` were not run.
- No tags were created, moved or deleted.
- No gate credit was claimed, and no Docker volumes were touched.
- No secrets were read.
