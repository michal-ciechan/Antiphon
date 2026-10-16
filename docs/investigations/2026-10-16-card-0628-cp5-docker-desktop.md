# CARD-0628 CP-5 on Docker Desktop

CP-5 failed on the assigned tip and once at fetched master: eight of nine harness
grades passed in each run; nested HTTPS egress received GitHub HTTP 403
`rate limit exceeded`. This reproduces on master and does not demonstrate a new
implementation regression. CP-5 remains failed; no code was changed.

## Scope and provenance

Task `9afa1e1b`, assigned branch `feat/card-task-9afa1e1b`. Selection is only CP-5
from `docs/superpowers/plans/2026-09-23-card-0628-claude-on-server2-runner-plan.md`,
following `docs/superpowers/reviews/2026-10-16-card-0628-d1-verification.md`.
No CP-1 rerun, ordinary suite, mutation control, login, deployment or restart was
selected. The optional containment extension was not selected.

Host: Windows desktop, Docker server 29.5.3, Linux Docker Desktop
(`OperatingSystem=Docker Desktop (containerized)`, daemon name `docker-desktop`).
Every Docker invocation, including inspection, build progress and cleanup, used
`scripts/build-slot.ps1`. Both harness leases were granted immediately with
`maxcpucount=4`; neither ran unleased. The gate used its normal broker; no service
restart or production runner/session operation was performed. The harness bound
its temporary runner to port 18298 and disabled phone-home.

| Run | Clean source SHA | Image ID | Harness outcome | Slot held |
|---|---|---|---|---|
| Assigned tip | `13a16300a61383a11f999cfb6c9d3e6722359ec0` | `sha256:3aad2ece20bbcf7c9b824cab05ade805046286efe5c5c58f74bfaad0a446601b` | exit 1; 8 grades passed, 1 failed | 334 s |
| Master comparison | `cd038acb3b62efbb67431866449ed6152acdb0db` | `sha256:99bdb58c95b3a12c97a509f63f6ecee6dfb4ba41d0fb2825a9c4763ab08118e5` | exit 1; 8 grades passed, 1 failed | 173 s |

Both images built successfully, and their inspected
`org.opencontainers.image.revision` labels matched the corresponding source SHA.
Tracked source was clean before and after both runs. The master comparison used
a temporary detached worktree; the assigned branch was never switched. The
harness and `docker/session-runner-grok` files have no diff between these SHAs.
These are harness grades, not TUnit execution counts; no TRX was produced.

## Commands and original output

Assigned-tip invocation (the enclosing shell captured output in `cp5-task.log`):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c628-cp5-9afa1e1b -SlotWaitMinutes 3 -- pwsh -NoProfile -File scripts/verify-card0604-dind-runner.ps1 -Image antiphon-session-testing:c628-13a16300a613 -Build -EvidenceRoot .antiphon/task-9afa1e1b/harness
```

Unedited harness verdict:

```text
C604 HARNESS NOTE: observations not ok: egress
C604 HARNESS: image=antiphon-session-testing:c628-13a16300a613 nested=yes egress=no loopback=200 launchMs=571 key=ok restart=ok refusal=ok containment=skipped claude=2.1.280 claudeAuth=logged-out
C604 HARNESS EXIT CODE: 1
BUILD SLOT released lease=a72a8b31-a97d-4057-9588-5eaa2744a78a held=334s
```

Master comparison (captured in `cp5-master.log`):

```powershell
git worktree add --detach C:\Antiphon\worktrees\cp5-master-9afa1e1b cd038acb3b62efbb67431866449ed6152acdb0db
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c628-cp5-master-9afa1e1b -SlotWaitMinutes 3 -- pwsh -NoProfile -File C:\Antiphon\worktrees\cp5-master-9afa1e1b\scripts\verify-card0604-dind-runner.ps1 -Image antiphon-session-testing:c628-cd038acb3b62 -Build -EvidenceRoot C:\Antiphon\worktrees\card-task-9afa1e1b\.antiphon\task-9afa1e1b\master-harness
```

Unedited comparison verdict:

```text
C604 HARNESS NOTE: observations not ok: egress
C604 HARNESS: image=antiphon-session-testing:c628-cd038acb3b62 nested=yes egress=no loopback=200 launchMs=823 key=ok restart=ok refusal=ok containment=skipped claude=2.1.280 claudeAuth=logged-out
C604 HARNESS EXIT CODE: 1
BUILD SLOT released lease=6851708e-07c1-49f9-9f6b-6c5ed7e1282a held=173s
```

The brief requests an unedited `CHECKPOINT` line. The explicitly selected direct
build-slot/harness driver emits no `CHECKPOINT` line, and neither captured run
contains one. The original `C604 HARNESS` lines above are retained instead; no
checkpoint-tool receipt, synthetic line or TUnit counts are claimed.

## Failure and acceptance implications

Both `nested-egress.txt` files show a successful pull of `alpine:3.20`, followed
by the same failure of the unchanged probe:

```text
wget: server returned error: HTTP/1.1 403 rate limit exceeded
```

The failed command was nested `docker run --rm alpine:3.20 wget -qO-
https://api.github.com/zen`. This is an external endpoint rate-limit response,
not the prior Linux-host bridge-overlap failure. No nested-daemon startup or
bridge-overlap failure occurred in either Docker Desktop run.

V-2 and V-3 passed in both runs: Claude reports 2.1.280 as the app uid, and its
fresh store returns signed out (auth-status exit 1 and `loggedIn: false`). V-4
does not pass because the full CARD-0604 harness is red. Nested daemon identity,
loopback HTTP 200, session launch, deploy-key handling, daemon restart/image
retention and all three startup refusals passed in both runs. A successful pull
and an HTTP response do not override the failed egress grade.

Caller triage: retain CP-5 as failed and commission only this exact row again
when GitHub's rate limit permits it. Any change to the probe endpoint or its
acceptance rule needs separate authorized Code work; this task neither changed
the probe nor retried beyond the required single master comparison.

## Evidence and cleanup

Raw generated evidence remains gitignored at the absolute root:
`C:\Antiphon\worktrees\card-task-9afa1e1b\.antiphon\task-9afa1e1b`.

- Assigned-tip evidence: `harness/20261016T100357Z/`, `cp5-task.log`,
  `cp5-task-exit.txt`.
- Master evidence: `master-harness/20261016T101005Z/`, `cp5-master.log`,
  `cp5-master-exit.txt`.
- `cleanup.log` retains image IDs/revision labels, image deletion and the final
  empty checks for each exact container and volume name.
- `build-progress.log` retains the first build's Docker progress. The two extra
  build-status Docker invocations were read-only diagnostics for a buffered
  build, not additional builds or harness runs.

Both harnesses removed their own containers (`c604-20261016T100357Z` and
`c604-20261016T101005Z`), corresponding `-dind` volumes and throwaway credential
and identity files. Final exact-name checks found no remaining task containers
or volumes. Both image tags were confirmed absent before their respective
builds, then removed after source-label inspection; Docker reported both image
IDs deleted. Shared build cache and existing resources were left alone. The
clean temporary master worktree was removed using ordinary `git worktree remove`
without force. No alternate host build-output directory was created.

--- next stage ---
next: decide
handoff: CP-5 failed at both pinned sources because GitHub /zen returned HTTP 403 rate limit exceeded; retain the failed gate and commission the same row after the external limit clears, or separately authorize probe changes.
artifact: docs/investigations/2026-10-16-card-0628-cp5-docker-desktop.md
