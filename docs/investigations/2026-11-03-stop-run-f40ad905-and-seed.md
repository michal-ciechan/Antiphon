# 2026-11-03 stop of rc qualification run f40ad905 and its stopped-run seed (task d26dbaaf)

Operator authority (2026-11-03): "Stop the windows run, cut a new rc based on master and start
that". This task did only the stop and the seed export. It did not cut, restart, launch or touch
agent sessions. It followed
[release-gates.md, Manual emergency stop and stale shared-lock recovery](../release-gates.md#inspecting-and-stopping-a-qualification)
and used the stop record for run 7f8e9fd1 (task 0e017562, e7451dedc) as its template. Evidence
and the helper scripts (adapted from that record's scripts) are in
`C:\Antiphon\releases\task-d26dbaaf`.

## Run at stop

- Candidate `rc-2026-11-03-919d8d42f14d`, run `f40ad905ae50487996a62a8be5bc9b58`, sha
  `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd`, launch set `C:\Antiphon\releases\task-302ba3d2`,
  failed-first from `7f8e9fd1.json`.
- Frozen at 16:11:57Z (`f40ad905-prestop-trx-scan.txt`):
  - The log dir has 834 top-level files, 205 TRX and 208 timing files.
  - TRX by suite: antiphon 117, e2e 65, markdown-pdf 11, session-runner 10, agents-pty 1,
    observability 1.
  - TRX results: 9087 passed, 50 failed, 27 other.
- Failed chunks: 11 chunks with 51 failures. Ten are TRX chunks with 50 failed results, and
  `client/all` adds one Vitest failure (TRX-less):
  - `antiphon-all-group-123` 1: LinuxOnlyWindowsExclusionPolicy
  - `antiphon-all-group-125` 1: ReferenceClosureInvalidates (60 s ScriptHarness budget timeout)
  - `antiphon-all-group-126` 1: WrapperOnlyPaidManifest
  - `antiphon-all-group-78` 10: AgentTaskCatchUpSettlementTests x4, BootStallAttentionTests x6 (parameterized)
  - `antiphon-all-group-82` 6: HerdrPlacementSettingsTests x5, CardFileIgnoreTests x1
  - `antiphon-all-group-86` 2: AgentTaskAnswerTests
  - `antiphon-all-group-87` 9: channel ingress, remote-control modal, diagnostics bundle, start recovery, Grok rules, machine turn text
  - `antiphon-all-group-88` 2: OrchestratorWorkspaceWarningTests
  - `antiphon-all-group-96` 1: SessionTerminationSourcePersistenceTests
  - `session-runner-all-group-2` 17: C0824 workspace slot overflow/rehome/reset
  - `client-all` 1: `SessionTerminal.mouse.test.tsx > CARD-0148 mouse policy > reset and
    instances remain independent` (`CLIENT TESTS EXIT CODE: 1`)
- The active (newest) chunk at the freeze was `antiphon/all-group-11` (generation 411,
  PostLandMutationDeliveryTests), started at 16:10:52Z. It had no TRX yet.
- `rc-qualification-status.ps1` before the stop:
  - exit 0, observation complete, no identity mismatches;
  - phase `tests`, no exit code;
  - `run.lock` and `native-run.lock` both `this-run`, and their owner 440112 was `alive`
    (pid-start-match).
- Only the lock bytes and `last-run.json` were copied into the evidence dir. No log, TRX or
  timing file was copied or changed.

## Stop

The launch-record supervisor 477116 (`launch-record.json`, start ticks 639292904248000076) was
alive, so step 3 rooted the kill at it. The full tree was traced through `Win32_Process` from
the detached wrapper:

| pid | process | start (UTC) | parent |
|---|---|---|---|
| 40088 | pwsh `task-302ba3d2\start-supervisor.ps1` (detached wrapper, `detached-create.json`) | 08:13:44.1799490Z | 4152 WmiPrvSE |
| 477116 | pwsh `task-302ba3d2\supervise-rc.ps1` (launch-record supervisor) | 08:13:44.8000076Z | 40088 |
| 384408 | pwsh `task-302ba3d2\launch-rc.ps1` (`supervisor-child.json`) | 08:13:47.2551625Z | 477116 |
| 258044 | pwsh `checkout-302ba3d2\scripts\build-slot.ps1` | 08:13:47.8708606Z | 384408 |
| 440112 | pwsh `nightly-run.ps1 ... -RunId f40ad905...` (lock owner) | 08:13:50.2066074Z | 258044 |
| 267976 | pwsh encoded nightly-tests | 08:14:01.489Z | 440112 |
| 243112 | `Antiphon.Tests.exe` (antiphon-all-group-11) | 16:10:52.48Z | 267976 |

`stop.ps1` checked the start ticks, the command-line marker and the parent pid of 477116,
384408, 258044 and 440112. It also checked that `native-run.lock` named runId f40ad905 and
pid 440112. Any mismatch would have aborted the stop.

`taskkill /PID 477116 /T /F` ran at **2026-11-03T16:13:12.77Z** and exited 128. It terminated
these processes:

- 477116 supervisor
- 384408 launch-rc, and its conhost 346156
- 258044 build-slot
- 440112 nightly-run
- 267976 nightly-tests
- 243112 Antiphon.Tests, and its conhost 358740

The exit code is non-zero only because three transient children of 243112 (git 333704, and
72380 and 468824) had already exited: "no running instance". The wrapper 40088 and its conhost
295300 exited by themselves when the supervisor died.

`reap-e2e-hosts.ps1` (dry run, no `-Kill`) found 0 E2E pty hosts both before and after the stop.

Census at 16:13:25Z, 12 s after the stop:

- All 15 pids seen before the stop are gone.
- No process has `checkout-302ba3d2`, `state-302ba3d2` or `task-302ba3d2` in its command line
  or executable path. No testhost, `Antiphon.*Tests`, Antiphon.E2E, nightly-run, nightly-tests,
  launch-rc or supervise-rc process exists.
- Four hits remain and were left alone. They are another session's read-only `watch-qual.ps1`
  chain (bash 137668 → 452096 → 454832 → pwsh 265248, under the
  `C--src-Antiphon/b2f1a869...` scratchpad). It matched only through its `-LogDir` argument.
- Nothing else was affected. These are the same before and after the stop:
  - dcpctrl 313228 on 17202/17203, SessionRunner 153560 on 17204 and Aspire.Dashboard 416800
    on 17205;
  - 15 claude.exe, 14 PtyHost and 9 Docker processes.

## Locks

- `C:\Antiphon\verification\native-run.lock` before the stop:
  `{"pid":440112,"processStartedAt":"2026-11-03T08:13:50.2066074Z","runId":"f40ad905ae50487996a62a8be5bc9b58","lane":"rc",...}`.
  After the stop its owner was dead and its bytes equalled the preserved copy. As step 4 says,
  `move-lock.ps1` moved the proven stale lock at 16:13:47Z. The receipt is
  `C:\Antiphon\releases\task-d26dbaaf\native-run.lock.f40ad905-stale-receipt`, sha256
  `AB3A762072E541F8057C43906DA491030070B2E9FEE083335C21B7FCE233EF12`, and its bytes match the
  copy. The original path is absent. `C:\Antiphon\verification` now holds no lock file. There
  is no `run.lock` there.
- The state-root `run.lock` (`state-302ba3d2\run.lock`, owner 440112, now `absent`) was **left
  in place** with its bytes unchanged, as in the precedent. It has its own reclaim rules, and a
  new run uses a new state root.
- `rc-qualification-status.ps1` after the stop: exit 0, observation complete. Native lock
  `missing`. Run lock owner `absent` (pid-not-found). `last-run.json` is byte-identical to the
  copy taken before the stop: phase `tests`, no exit code. It was not rewritten.

## Seed

Script version: this worktree at `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0`, which equals
origin/master.

```
pwsh -NoProfile -File scripts/export-nightly-stopped-run-seed.ps1 `
  -RunDirectory C:\Antiphon\releases\candidates\rc-2026-11-03-919d8d42f14d\logs\2026-11-03-0814-f40ad905 `
  -OutputPath C:\Users\lndco\antiphon-seeds\f40ad905.json `
  -SourceRunId f40ad905ae50487996a62a8be5bc9b58 -SourceCandidateId rc-2026-11-03-919d8d42f14d `
  -SourceSha 919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd `
  -TimingRoot C:\Antiphon\releases\candidates\rc-2026-11-03-919d8d42f14d\logs\timing-f40ad905ae50487996a62a8be5bc9b58
```

- The export exited 0. Seed `C:\Users\lndco\antiphon-seeds\f40ad905.json`:
  - sha256 `d97603497af9aa0f9c0cd7a377318916d3e8a9fdc9370aa567277c365d534917`, 344983 bytes;
  - format `antiphon-nightly-stopped-seed-v1`, seedKind `operator-seed-from-stopped-run`;
  - contentHash `13564f1c...ef132`, created 2026-11-03T16:14:46Z, sourceObservedAtUtc
    2026-11-03T16:10:52Z.
- The log dir, timing root and state root were the same before and after the export: file
  count, bytes and newest mtime matched (`seed-before-snapshot2.json`,
  `seed-after-snapshot.json`).
- 863 chunks:
  - 204 completed: antiphon 116, e2e 65, markdown-pdf 11, session-runner 10, agents-pty 1,
    observability 1.
  - 2 started but unfinished: `antiphon/all-group-11` (the chunk killed by the stop) and
    `antiphon/all-group-125`.
  - 657 never started: antiphon 532, pty-host 53, session-runner 52, e2e 10, agents-pty 9 and
    messaging 1.
- `all-group-125` has a closed TRX (195 total, 185 passed, 1 failed). The exporter's completion
  test still marked it not completed. That test needs the TRX rows to be at least the planned
  count, with every row in a finished outcome. The chunk also holds a failure, so it reruns in
  the first band either way.
- 43 failed test identities, deduplicated per method, in 10 chunks and 19 classes:
  - antiphon all-group-123 1, -125 1, -126 1, -78 6, -82 6, -86 2, -87 9, -88 2, -96 1;
  - session-runner all-group-2 14.
- The seed does not carry the client Vitest failure, because client is not a TRX chunk.

### Relation to 7f8e9fd1.json

`7f8e9fd1.json` is not carried forward, as in the precedent. The exporter has no prior-seed
parameter: its only parameters are RunDirectory, OutputPath, SourceRunId, SourceCandidateId,
SourceSha, TimingRoot and PassThru. No doc defines a cumulative-seed rule; a grep of `docs/`
and the rc-qualification skill for "cumulative" finds none for seeds. This seed describes
only f40ad905. Chunks that completed in 7f8e9fd1 but never ran here are never-run in this
seed, not passed.

### Next run

A run on a new candidate needs:

```
-PriorFailuresPath C:\Users\lndco\antiphon-seeds\f40ad905.json
-PriorFailuresDigest d97603497af9aa0f9c0cd7a377318916d3e8a9fdc9370aa567277c365d534917
-AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-03-919d8d42f14d
```

The seed refuses once it is older than 14 days, counted from 2026-11-03T16:14:46Z.
