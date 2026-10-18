# RC cut and gate, 2026-10-18

Task `9d6824b0`. Branch `feat/card-task-9d6824b0`. No tag was created, moved, or pushed. `publish-release.ps1` and `promote-release.ps1` were not run. The canonical checkout `C:\src\Antiphon` was not switched or pulled. The production runner on port 17204 was not restarted and its sessions were not touched.

## Cut

`origin/master` at the cut was `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` (`feat(CARD-1311): publish vYYYY.MM.DD.N-rc.K and promote the final tag`).

Before the push, `git ls-remote --heads origin rc/2026-10-18` was empty. `refs/heads/rc/2026-10-15` was `61117dd1663d2f47ce4708c18b609b8936491986`.

Created, not forced:

```text
git push origin a1d24b6fc92d45ae5274b418bf54c6b8819a8697:refs/heads/rc/2026-10-18
```

Read-back:

```text
61117dd1663d2f47ce4708c18b609b8936491986	refs/heads/rc/2026-10-15
a1d24b6fc92d45ae5274b418bf54c6b8819a8697	refs/heads/rc/2026-10-18
```

`rc/2026-10-15` was not updated.

## Candidate journal

`scripts/release-cut.ps1 -Ref rc/2026-10-18 -SkipPublish` journalled the long-lived tip and did not push the branch.

| Field | Value |
|---|---|
| Candidate id | `rc-2026-10-18-a1d24b6fc92d` |
| Ref | `rc/2026-10-18` |
| SHA | `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` |
| Journal | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\candidate.json` |
| `pushed` | false |
| `createdAt` | `2026-10-18T16:40:54.7503471Z` |

The same process then refused the test step: `REFUSED: unmarked clone is not a proven nightly checkout` (`unmarked-clone`, exit 3). `release-candidate.ps1` writes `.antiphon-release-owned`. `nightly-run.ps1` accepts only `.antiphon-nightly-owned` and refuses an existing `.git` without it. Re-running `release-cut.ps1` repeats that refusal. Nothing was published.

A direct `nightly-run.ps1 -Profile rc` on a fresh checkout reached `phase=fetch` as run `39c9715c1eb3402da417da276455fa3c` and then exited 3 with the same unmarked-clone refusal. `git clean -fdx` removes the nightly marker, and the self-reexec hop then sees an unmarked clone. That run's owner pids `33212` and `32124` were dead. Its `state\run.lock` was still present; a dead owner is not cleared by the lock helper (`lock-held`). The stale lock file was removed so a later run could acquire it. No native lock was left under `C:\Antiphon\verification`.

## Windows lane (in progress)

The pinned checkout `C:\Antiphon\releases\checkout` is an independent clone of `https://github.com/michal-ciechan/Antiphon` at `a1d24b6fc92d45ae5274b418bf54c6b8819a8697`. It is not the canonical checkout and not a linked worktree. `.antiphon-nightly-owned` was written so the ownership check can accept it. The run uses `-NoSync`, so it does not fetch, reset, or clean, and it does not take the self-reexec hop. HEAD was verified equal to the candidate SHA before the process started. `-ExpectedSha` is still passed through.

Launcher: `C:\Antiphon\releases\start-rc-2026-10-18.cmd` (outside the repo), started with `Win32_Process.Create` so it is not a child of this session. `PATH` puts `C:\Program Files\PowerShell\7` first.

| Field | Value |
|---|---|
| Host | Windows desktop |
| Profile / trigger | `rc` / `rc` |
| Run id | `42d2c78424dd4612af43eed3afeaecc2` |
| SHA | `a1d24b6fc92d45ae5274b418bf54c6b8819a8697` |
| Started | `2026-10-18T16:47:34.4931667Z` |
| Phase at record time | `tests` |
| pwsh pid | `38396` (parent cmd `18564`) |
| State | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\state\last-run.json` |
| Console log | `C:\Antiphon\releases\rc-2026-10-18-nightly-nosync.log` |
| Run logs | `C:\Antiphon\releases\candidates\rc-2026-10-18-a1d24b6fc92d\logs\2026-10-18-1647-42d2c784` |

Do not start a second Windows lane while pid `38396` is alive.

### Already observed

`npm ci` in `client` failed before the solution build. `client\node_modules` was absent. The log is `npm-ci.log` (1571 bytes). Both attempts:

```text
Error: Cannot find module 'C:\Antiphon\releases\checkout\client\node_modules\npm\bin\npm-prefix.js'
Error: Cannot find module 'C:\Antiphon\releases\checkout\client\node_modules\npm\bin\npm-cli.js'
code: MODULE_NOT_FOUND
Node.js v24.6.0
```

The `npm.cmd` on this host is `C:\Program Files\nodejs\npm.cmd`. This was not compared with `rc/2026-10-15` at `61117dd1`, so it is not classified as inherited. The runner did not stop on it. `dotnet build Antiphon.sln` was still writing `dotnet-build.log.stdout.tmp` (28672 bytes at 17:48:36 local) when this note was written. No suite TRX yet. No inherited red from the brief (CARD-1216, C1192, C1191, C1105, C599, Windows LandDelivery) has been observed in this run.

## Linux lane

Not started. `server2` has git and no host `pwsh` or `dotnet`. The rc profile entry (`release-cut.ps1` / `nightly-run.ps1`) runs `npm.cmd` through `%ComSpec%`, which is the Windows profile. The production runner container was not used. A hand-rolled `dotnet` roster would not be the rc profile and would not earn rc-release credit.

## Resume

Watch pid `38396`. When it exits, read the last JSON line of `C:\Antiphon\releases\rc-2026-10-18-nightly-nosync.log` and `last-run.json` in the candidate state directory. Suite logs and TRX stay under the run log directory above. Do not publish, tag, or start a second Windows run at this SHA until that process has exited.
