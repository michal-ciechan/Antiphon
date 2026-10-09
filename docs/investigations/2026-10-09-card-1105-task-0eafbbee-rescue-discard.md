# CARD-1105: rescue, discard, worktree removal, ref delete, and audit — stopped (2026-10-09)

Task 9b261dfc (Debug). Operator decisions: rescue the three post-failure files from
`worktrees/task-0eafbbee` onto `rescue/card-0835-0eafbbee`, then discard that worktree;
remove linked worktree `c1005-rebase-rX0auR`; delete the superseded KEEP refs; re-run the
`51f175db` audit. Sources: compare `97c14deb9`, cleanup `dfb23d6cca2a359a77dd1104aa5a71980580363c`,
identification `e10b874168c32e869a13f0889a6b93daf76c9839`.

## Outcome

**Stopped before any contact with volume `antiphon-runner_work`.** No throwaway container
ran. No rescue branch was created or pushed. No `checkout`, `clean`, `worktree remove`, or
`update-ref` ran. No proof bundle exists. The audit was not run. Nothing on the old volume
was read or written by this task.

## Why

This session is the server2-temp runner container `927bb1ad3d60`. Its `/work` mount is
`antiphon-runner-temp_work` (`/var/lib/docker/volumes/antiphon-runner-temp_work/_data`), not
`antiphon-runner_work`. Nested `docker info` Name is `927bb1ad3d60`. That daemon has no
volume `antiphon-runner_work` and no image `antiphon-server2/session-testing:4358939ecd85`.
There is no host docker socket. uid 1654 has empty effective capabilities. `sudo -n -l`
allows only `/usr/local/bin/antiphon-custody-enter` and `/usr/local/bin/antiphon-custody-kill`.
`debugfs -n` on `/dev/dm-0` returns `Permission denied`.

The documented host path `ssh mc@server2` resolves to `192.168.4.34`. Port 22 is open.
`ssh -o BatchMode=yes -o IdentitiesOnly=yes` as `mc`, and the same command with
`IdentityFile` set to the runner deploy key, both return `Permission denied (publickey)`.
Host port 2375 is closed. Port 2376 completes a TLS handshake (CN `codeperf.net`,
certificate notAfter 2020-05-08) and then sends alert `bad certificate`. It is not a Docker
API this session can use. No `DOCKER_HOST` or `DOCKER_CERT_PATH` is set.

The throwaway container's `:ro` mountinfo line and failed write probe cannot be produced,
and the read-write scope cannot be proved. The brief says to stop there.

## Preconditions that could be read

`GET /api/agent-tasks/pipeline` at `2026-10-09T06:26:13.2448938Z`:

| Stage | In flight |
|---|---|
| Merge | 0 |
| Deploy | 0 |
| Code | 1 (`39023430`, CARD-1157) |
| Review | 1 (`a97f37e9`, CARD-1158) |
| Debug | 1 (this task, `9b261dfc`) |

No land task is in flight. `GET /api/session-runners` at the same time: `server2`
`draining=true`, `occupied=0`, `acceptingNewWork=false`, `available=true`; `server2-temp`
accepting, occupied 9. The host process check for `deploy-server2.ps1` / `c590` / `c1008`
was not run, because SSH to the host did not authenticate. The worktree's 30 entries and
the three file hashes were not re-read.

## Not produced

| Item | Value |
|---|---|
| Proof bundle path / sha256 | none |
| `tools/Antiphon.Checkpoints/CheckpointApp.cs` | not re-hashed (compare prefix `558ffd074839`) |
| `tools/Antiphon.Checkpoints/Report/ReportMerger.cs` | not re-hashed (compare prefix `e7694c77ada4`) |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs` | not re-hashed (compare prefix `05dc5a608d5f`) |
| Rescue branch | not created (`rescue/card-0835-0eafbbee` was not pushed) |
| `git ls-remote` of that rescue branch | not applicable |
| Worktree porcelain count before / after | not read / not changed |
| HEAD and branch of `worktrees/task-0eafbbee` | not re-read (compare recorded `8331a9cf1` on `feat/card-task-0eafbbee`) |
| `c1005-rebase-rX0auR` worktree count | not changed |
| Ref listing undo file | none |
| Refs deleted | 0 |
| Audit PASS / REFUSE | not run |
| `worktrees/task-*` directory count | not read |

## What unblocks a retry

A seat that can `ssh mc@server2` and run `docker run --rm` on the host daemon, image
`antiphon-server2/session-testing:4358939ecd85`, volume `antiphon-runner_work`, uid
`1654:1654`, with the git env from the compare note. This server2-temp seat cannot.
Re-check the pipeline (Deploy and Merge still empty) and that no `deploy-server2.ps1`
phase is in flight on the host before the first mount.
