# CARD-2063 Linux Git census and other-user processes

CP-9 exhausted its six-second cleanup allowance after the Git root exited and the owned orphan died. The census tried to open `/proc/<pid>/environ` for each visible live process born at or after the root. A later process under a different UID could keep returning `EACCES`, turning every scan into `Unknown` even when it found zero members. The earlier containment cases used same-UID strangers with readable environments. The injected faults established that unreadable observations stayed `Unknown`, but those fixtures had no credential evidence with which to distinguish a foreign UID. Postgres backends starting during the loaded review exposed that gap. [CP-9 investigation](2026-11-23-review-8f467d8d-cp7-cp9-load-reds.md)

## Membership verdict

`Joined` requires the root to exit and a complete census of the cooperative invocation. When an environment read is denied and the adapter has captured the root PID and start ticks, the census can use the visible `Uid:` records in `/proc/<pid>/status` and `/proc/self/status`. The real, effective, saved-set and filesystem UIDs must be disjoint, and a second stat read must confirm the same live PID, start ticks and parent. The row stays available for the ancestry check. A validated descendant still counts, and a readable environment with the exact invocation marker still proves membership.

Shared UIDs, unavailable credential evidence, a changed process identity, a hidden process table and missing root identity keep the verdict conservative. The repository journal uses a marker-only probe without a captured root PID. Its unreadable candidates still keep the fence. Detached marker discovery assumes cooperating descendants retain the invocation's credentials. A detached child that has changed credentials and whose marker and ancestry are both unavailable would require stronger containment to track. [Linux census](../../src/Antiphon.Server.Git/LinuxOwnedGitMembers.cs) [Journal marker probe](../../server/Infrastructure/Git/OwnedChildMarker.cs)

## Verification on the Linux runner

The native witness starts a second Git process as UID 999 in a test-owned Docker container after the managed root starts. It confirms Git is visible in the runner's PID namespace and opening its environment is denied, then closes the owned root's stdin. At the cleanup verdict the owned orphan is dead and the foreign Git is still alive. The fixture uses the locally cached `node:22-bookworm` image and closes the container's stdin in teardown. The Windows exclusion is pinned to this method.

| Runner source | Selection | Result |
|---|---|---|
| `5669ee72aea3a74b7e1fef55ff5d677ab6e74fba` | `Unrelated_foreign_uid_git_does_not_prevent_join` | 1 failed at `Joined`. `rootExited=True members=Unknown/0 reason=Deadline signals=1 duration=00:00:06.0009949`. |
| `ff85938cc687e483c9d846bae1fac9ebab53df4c` | `LinuxOwnedGitProcessTests` | 43 passed, 0 failed, 0 skipped. The second-UID witness took 0.801 s including fixture setup and teardown. All four existing reparenting cases and the production marker factory witness passed. |

The first witness build at `e95acb000fe9439ced483e11d0f475217bfd3ba3` stopped at `CS0104` for an ambiguous `Category` attribute. Qualifying the exception type fixed the compile error before the red assertion run. The red and green evidence builds each took a build slot with `maxcpucount=6` and finished with zero errors. The test runs each took a separate slot. The five native methods in the test class match the Windows exclusion row.

Rerun the following on the corresponding source. Set `c2063_case` to `Unrelated_foreign_uid_git_does_not_prevent_join` for the red source, or to `*` for the fixed source.

```sh
c2063_results="$PWD/.antiphon/card-2063"
mkdir -p "$c2063_results"
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-2063-build -- \
  dotnet build tests/Antiphon.Tests -p:UseAppHost=false
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-2063-test -- \
  dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- \
  --treenode-filter "/*/*/LinuxOwnedGitProcessTests/$c2063_case" \
  --report-trx --report-trx-filename rerun.trx --results-directory "$c2063_results"
```

Runner evidence is in `/work/worktrees/ws-08-d2/.antiphon/card-2063/`, with `red.trx`, `green.trx`, and the associated build and run logs.
