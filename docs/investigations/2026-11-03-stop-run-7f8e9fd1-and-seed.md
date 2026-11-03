# 2026-11-03 stop of rc qualification run 7f8e9fd1 and its stopped-run seed (task 0e017562)

Operator authority (2026-11-03): "Let's kill the current rc, and then do [the plan]: Cut: take
the new rc from master. Activate: put the desktop AppHost and server2 on it. Qualify: start the
new cut's Windows run failed-first." This task did only the stop and the seed export. It did
not cut, restart, launch or touch agent sessions. It followed
[release-gates.md, Manual emergency stop and stale shared-lock recovery](../release-gates.md#inspecting-and-stopping-a-qualification)
and the precedent of task 8a7dbe6d (515f6cd1f) and task 78b36185 (41629e7b2). Evidence and the
helper scripts are in `C:\Antiphon\releases\task-0e017562`.

## Run at stop

- Candidate `rc-2026-11-01-10a826b9d61f`, run `7f8e9fd119c3428a8bd46ac88e884645`, sha
  `10a826b9d61f2c2cdfa29a43cac59fe360c99ea9`, launch set `C:\Antiphon\releases\task-8a7dbe6d`.
- Frozen at 06:27:26Z (`7f8e9fd1-prestop-trx-scan.txt`): log dir has 2938 top-level files,
  754 TRX and 757 timing files. TRX by suite: antiphon 613, pty-host 53, session-runner 53,
  e2e 25, agents-pty 9, messaging 1. Results: 19075 passed, 3 failed, 34 other.
- Failed chunks (3, one failure each):
  - `antiphon-all-split-15`: `LandNonCodeSkipTests.C1385_RegistryMatchesNarrowGraph`
  - `antiphon-all-profile-743`: `LandGraphEvaluationTests.C1483_S4_RealRepositoryApproves`
  - `antiphon-all-profile-744`: `LandGraphEvaluationTests.C1483_S5_CheckRequiresEvaluationRecord`
- Active chunk at freeze: `e2e/all-split-15` (generation 1509), started 06:27:03Z. The first
  trace, at 06:26:31Z, saw `e2e/all-split-14`.
- `rc-qualification-status.ps1` before the stop: exit 0, observation complete, no identity
  mismatches. Phase `tests`, no exit code. Both `run.lock` and `native-run.lock` belonged to
  this run, and the owner 430788 was `alive` (pid-start-match).
- The only files copied were the lock bytes and `last-run.json`, into the evidence dir.
  Step 1 of the procedure needs them for the byte compare. No log, TRX or timing file was
  copied or changed.

## Stop

The launch-record supervisor 347164 (and wrapper 413132) died at about 22:09Z on 2026-11-02
(see `2026-11-02-qualification-supervisor-exit.md`, a6e2d1ce2). Step 3 roots the kill at that
supervisor, and it no longer exists. The equivalent root used here was the topmost live
ancestor of the native-lock owner. That is launch-rc 127960, which the launch set records as
the supervisor's child (`task-8a7dbe6d\supervisor-child.json`). Before the kill, `stop.ps1`
checked the following and would have aborted on any mismatch:

| pid | process | start (UTC, ticks) | command-line check | parent pid |
|---|---|---|---|---|
| 127960 | pwsh `launch-rc.ps1` | 2026-11-02T13:13:41.5880449Z (639292220215880449) | `task-8a7dbe6d\launch-rc.ps1` | 347164 (dead; pid not present, so not reused) |
| 200108 | pwsh `build-slot.ps1` | 13:13:42.0753036Z (639292220220753036) | `checkout-8a7dbe6d\scripts\build-slot.ps1` | 127960 |
| 430788 | pwsh `nightly-run.ps1` (native-lock owner) | 13:13:44.1660694Z (639292220241660694) | `-RunId 7f8e9fd119c3428a8bd46ac88e884645` | 200108 |

The script also checked that `native-run.lock` named runId 7f8e9fd1 and pid 430788.

`taskkill /PID 127960 /T /F` ran at **2026-11-03T06:28:39.55Z** and exited 0. Nine processes
ended: 127960 launch-rc, 165152 its conhost, 200108 build-slot, 430788 nightly-run, 71760
nightly-tests (encoded pwsh, started 13:13:56.95Z), 171592 Antiphon.E2E (e2e-all-split-15,
started 06:27:03.87Z), 153904 its conhost, 26872 Antiphon.SessionRunner (the E2E isolated
runner, started 06:27:07.18Z) and 430872 its conhost.

E2E pty hosts are detached by design (their parent pids are gone), so `/T` cannot reach them.
The product reaps them by manifest pid and start time
(`IsolatedSessionRunner.SweepCrashedRunsAsync`). Three sets were seen:

- 158716 and 168012 at 06:24Z. The next chunk's sweep reaped them before the stop.
- 188796 and 371576 at 06:27:45Z. They had exited by the stop.
- `reap-e2e-hosts.ps1` (identity check only, run without `-Kill`) found **0** at 06:28:40Z.

Census at 06:28:50Z (11 s after the stop):

- Every pid seen before the stop is gone.
- No process has `checkout-8a7dbe6d`, `state-8a7dbe6d` or `7f8e9fd1` in its command line or
  executable path.
- No testhost, `Antiphon.*Tests`, Antiphon.E2E, nightly-run, nightly-tests, launch-rc or
  supervise-rc process exists.
- One hit remains and was left alone: another session's read-only watcher chain (bash 427988 →
  pwsh 223988, under the `C--src-Antiphon/b2f1a869...` scratchpad). It will probably report the
  run as gone.
- Nothing else was affected:
  - dcpctrl 442908 is still on 17202/17203 (started 2026-10-29) and Aspire.Dashboard 1108 on 17205.
  - SessionRunner 7996 is still on 17204 (started 2026-10-26).
  - 15 claude.exe and 14 production PtyHost processes, the same counts as before the stop.

## Locks

- `C:\Antiphon\verification\native-run.lock` before the stop:
  `{"pid":430788,"processStartedAt":"2026-11-02T13:13:44.1660694Z","runId":"7f8e9fd119c3428a8bd46ac88e884645","lane":"rc",...}`.
  After the stop its owner was dead and its bytes equalled the preserved copy. Step 4 says to
  move a proven stale lock to a new path on the same volume, and that was done
  (`move-lock.ps1`) at 06:29:10Z. The receipt is
  `C:\Antiphon\releases\task-0e017562\native-run.lock.7f8e9fd1-stale-receipt`, sha256
  `2A7387EF...0BB38E50`, and its bytes match the copy. The original path is absent.
  `C:\Antiphon\verification` now holds no lock file. There is no separate `run.lock` there.
- The state-root `run.lock` (`state-8a7dbe6d\run.lock`, owner 430788, now `absent`) was **left
  in place**, as in the precedent. The doc says it has its own reclaim rules and forbids
  deleting both lock files as a pair. A new run uses a new state root, so this lock does not
  block it.
- `rc-qualification-status.ps1` after the stop: exit 0, observation complete. Native lock
  `missing`. Run lock owner `absent` (pid-not-found). `last-run.json` is unchanged: phase
  `tests`, no exit code. Step 5 says it is not rewritten.

## Seed

Script version: this worktree at `7b920d84aae97139998f940ee2cce3f457bb6e70`, which equals
origin/master. That version has CARD-1633 (70c70336d): per-lane resume, and skip-only chunks
count as complete. The run's own checkout (`checkout-8a7dbe6d`) has the older
`scripts/lib/nightly-stopped-run-seed.ps1` (blob 1b1a84ed, master a6506be4). The exporter and
`nightly-failure-history.ps1` blobs are the same in both.

```
pwsh -NoProfile -File scripts/export-nightly-stopped-run-seed.ps1 `
  -RunDirectory C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f\logs\2026-11-02-1313-7f8e9fd1 `
  -OutputPath C:\Users\lndco\antiphon-seeds\7f8e9fd1.json `
  -SourceRunId 7f8e9fd119c3428a8bd46ac88e884645 -SourceCandidateId rc-2026-11-01-10a826b9d61f `
  -SourceSha 10a826b9d61f2c2cdfa29a43cac59fe360c99ea9 `
  -TimingRoot C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f\logs\timing-7f8e9fd119c3428a8bd46ac88e884645
```

- The export exited 0. Seed `C:\Users\lndco\antiphon-seeds\7f8e9fd1.json`:
  - sha256 `a125bf7d47baa5b73a46d05732e6d5254f8b6cf57dfc9ad1c16497c6986230c6`, 491104 bytes;
  - format `antiphon-nightly-stopped-seed-v1`, seedKind `operator-seed-from-stopped-run`;
  - contentHash `4865f646...1d97a`, sourceObservedAtUtc 2026-11-03T06:27:03Z.
- The log dir, timing root and state root were the same before and after the export: file
  count, bytes and newest mtime matched (`seed-before-snapshot2.json`,
  `seed-after-snapshot.json`).
- 845 chunks:
  - 754 completed: antiphon 613, pty-host 53, session-runner 53, e2e 25, agents-pty 9, messaging 1.
  - 1 started but unfinished: `e2e/all-split-15` (AgentTaskLandDeliveryE2ETests).
  - 90 never started: e2e 49 (all-split-16 onward), antiphon 28, markdown-pdf 11,
    agents-pty 1 and observability 1.
- 3 failed test identities in 3 chunks and 2 classes, the same 3 listed above.

### Relation to aa64a906.json

`aa64a906.json` (sha `aa113d85...`, which seeded this run) is not carried forward. The exporter
takes one run directory and has no prior-seed input. No doc defines a cumulative-seed rule.
The 515f6cd1f record says the same of the previous seed: it "describes only aa64a906".

- aa64a906's two failed chunks (antiphon all-group-111 and e2e all-group-1) ran first in
  7f8e9fd1 and completed without failure.
- 18 chunks completed in aa64a906 but never ran in 7f8e9fd1 (P3 last). In the new seed they
  are never-run, not passed: antiphon all-group-1, 2, 4..10, 12..17, 78 and 125, and
  all-split-8.

### Next run

A run on a new candidate needs:

```
-PriorFailuresPath C:\Users\lndco\antiphon-seeds\7f8e9fd1.json
-PriorFailuresDigest a125bf7d47baa5b73a46d05732e6d5254f8b6cf57dfc9ad1c16497c6986230c6
-AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-01-10a826b9d61f
```

The seed refuses once it is older than 14 days.
