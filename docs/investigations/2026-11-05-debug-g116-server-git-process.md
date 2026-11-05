# g116: CARD-1432 counts an eligibility probe as a git launch

## Finding

This is a test regression introduced by
`40c2ab63c10dda3887705e80897b37e0dd119e5e`
(`perf(land): answer read-only git commands in process with libgit2`, committed
2026-11-04 09:13:14Z). It added a third `ProcessStartInfo` construction to
`LandingGit.cs`, but that construction never launches a process. Both actual
launch sites still disable automatic maintenance. A test-only Code repair is
needed on master and must be picked into the rc line.

No product code or tests were changed by this Debug task. The live Windows
qualification, desktop checkout, running services and rc refs were untouched.

## Revision evidence

| Role | Full commit | Evidence |
|---|---|---|
| Last passing boundary, immediate parent of introducing commit | `dd30b167ac522e5462f52f3307ef861e648fb0bf` | Two constructions; both guarded launches. Fresh targeted execution: 6 passed. |
| Introducing commit | `40c2ab63c10dda3887705e80897b37e0dd119e5e` | Adds `var probe = new ProcessStartInfo("git")` inside the original `InProcessEligibility()` method. Static scanner changes from passing to failing. |
| Assigned rc baseline | `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a` | Three constructions; supplied live run `b19a9880`, `antiphon-all-group-116`, reports expected 2 / actual 3. Static inspection independently reproduces the mismatch. |
| Fetched `origin/master` used for execution | `3ce7f87ef06315ace62572d7e51b2de0347dbef9` | Three constructions; fresh targeted execution reproduces the same failure, 5 passed / 1 failed. |

`git log -S'new ProcessStartInfo' -- server/Infrastructure/Git/LandingGit.cs`
identifies `40c2ab63c10dda3887705e80897b37e0dd119e5e`, not the later suspects:

- `fceeab72a7824deb29dae73afbbd1ec6a075cef5`: no change to this file.
- `cc8845f030d934507e7a0d2db7d939be06d9f2bc`: changes the in-process-hit profiling call.
- `92264fa896e3ebb0beccda8d288eb61d1a6910e6`: adds observation pinning through existing `RunAsync` calls.

The introducing commit is an ancestor of both inspected tips. The scanner test
has identical Git blob `cb6bda837fd26f1f748a3cef269b6384dc347617` at all four
revisions. A transcription of its roster checks found no count or guard failure
at the parent, and only the new probe at each later revision. This static check
is separate from the two actual TUnit executions below. The passing boundary is
freshly verified here; this report does not assert historical CI results for
every intervening commit.

## What each construction does

Line numbers below refer to `server/Infrastructure/Git/LandingGit.cs` at the
named immutable revisions.

| Construction / method | rc line | master line | Guard and launch |
|---|---:|---:|---|
| `probe` / `InProcessEligible()` | 128 | 162 | Calls `ConfigureProcess(probe)`, inspects executable/environment, and returns cached eligibility. No launch and no maintenance guard. |
| `start` / `ExecuteProcessAsync()` | 164 | 200 | Calls `ConfigureProcess(start)`, then `ServerGitProcess.DisableAutoMaintenance(start)`, then adds arguments and calls `StartProcess(start)`. |
| `start` / `RunGitInputBytesAsync()` | 1119 | 1161 | Same ordering: configuration, maintenance guard, argument population, then `StartProcess(start)`. |

The probe has no command arguments and is never handed to `StartProcess`,
`Process.Start`, or an instance `Start()`. On master it also supplies a copied
environment dictionary for configuration observation; that does not launch the
probe. The production `ConfigureProcess` implementation is empty, and the
production `TaskProgressGit` subclass does not override it.

At both real launches the helper appends `gc.auto=0` and
`maintenance.auto=false` using `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_n` and
`GIT_CONFIG_VALUE_n`, preserving earlier entries. On current master the guards
are at lines 209 and 1167, before launches at 215 and 1177. On rc they are at
173 and 1125, before launches at 179 and 1135. The new construction therefore
does not introduce an unguarded git child or a new opportunity for automatic gc.

## Smallest safe repair

Change `tests/Antiphon.Tests/Infrastructure/ServerGitProcessTests.cs` only:

1. Represent the three `LandingGit` constructions explicitly: the named probe
   and the two named launch methods. Distinguish construction count from launch
   count: the roster now has 14 constructions and still 13 actual launch sites.
2. Assert that the probe remains non-launching. Scope this assertion to its
   actual method/local identity; a comment exemption or a blanket skip of
   constructions without a regex launch match would weaken the invariant.
3. Assert each actual launch individually: its own maintenance guard follows
   configuration and precedes its own launch. Preserve the existing checks on
   the other roster files and the real-git environment tests.

Changing only the `LandingGit` tuple from 2 to 3 is insufficient: the aggregate
`Roster.Sum(...).ShouldBe(13)` then fails. Changing that aggregate to 14 still
leaves the probe in `missing`, because the current loop requires a guard even
when its segment contains no launch. Adding a product guard to an unused probe
would mask this modeling error.

The Code repair should demonstrate that removing either real `LandingGit`
guard makes its precise scanner assertion fail, and that the unchanged
non-launching probe is accepted. Those controls were not executed here because
this brief explicitly forbids product/test edits. Repair master first and take
that repair into rc/2026-11-06 through the caller's normal pipeline; master alone
does not change the live qualification checkout.

## Targeted execution and provenance

On the Linux runner, exact Git archives of master and the immediate parent were
built in separate, task-owned directories, with native default outputs. The
test, `LandingGit.cs`, and `ServerGitProcess.cs` in both directories were checked
byte-for-byte against their named Git blobs. No branch was switched or created.
Each build and each run took its own host build-slot lease. All children were
joined before cleanup. Only `ServerGitProcessTests*` ran; no full suite or
desktop run was started.

| Selection | Admission wait | Build / run | Result |
|---|---:|---:|---|
| Initial master build attempt | 180s limit | Not run | Slot timeout, exit 4; no compilation or tests. |
| Master build, bounded gated retry | 165s | 710.17s | Exit 0; 0 errors, 1690 warnings. |
| Master class run | 121s | TUnit 6.285s; lease held 13s | Exit 2; 6 total, 5 passed, 1 failed, 0 skipped. |
| Parent build | 436s | 254.55s | Exit 0; 0 errors, 1662 warnings. |
| Parent class run | 166s | TUnit 6.423s; lease held 13s | Exit 0; 6 total, 6 passed, 0 failed, 0 skipped. |

Build warnings included analyzer `AD0001` exceptions; neither build failed.
Build-slot maximum parallelism was 6. Admission was bounded separately from
execution: the retry and parent build used a 15-minute slot wait and a 1500s
outer limit; runs used a 15-minute slot wait, an 1080s outer limit and TUnit
`--timeout 3m`. These are provisional diagnostic limits, not measured p95
budgets or proposed production/test-policy changes. No selection exceeded 30
minutes.

The master failure was exactly:

```text
failed C1432_every_server_git_start_site_disables_auto_maintenance (225ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: starts.Count
      should be
  2
      but was
  3

  Additional Info:
      server/Infrastructure/Git/LandingGit.cs ProcessStartInfo constructions
```

TRX per-case durations, in seconds, preserve the exact six-case membership:

| Case in `ServerGitProcessTests` | Master seconds / result | Parent seconds / result |
|---|---|---|
| `C1432_land_git_reads_gc_auto_zero` | 1.1587295 / pass | 0.9325892 / pass |
| `C1432_every_server_git_start_site_disables_auto_maintenance` | 0.2250349 / fail | 0.0152558 / pass |
| `C1432_append_preserves_existing_entries` | 0.0050744 / pass | 0.0684612 / pass |
| `C1432_invalid_count_throws("-1")` | 0.0018434 / pass | 0.0001816 / pass |
| `C1432_invalid_count_throws("x")` | 0.0004473 / pass | 0.0013135 / pass |
| `C1432_invalid_count_throws(" ")` | 0.0002161 / pass | 0.0002512 / pass |

Phase recording was enabled only for test children. Run IDs were
`debug-g116-master` and `debug-g116-last-green`, group `server-git-process`,
attempt `1`, suite `Antiphon.Tests`. Both recorders emitted 6 starts and 6
completions, no dropped phases, and no health failure. Module version IDs were
`d520d7b6b93d4c0a87a0ab1e86fc7db7` and
`269457817c7a443ca8d18e34012d24a0`, respectively.

The real-git case recorded 15 git spawns on each revision. Master recorded
261ms of nested spawn spans, 1073ms of process-wait spans/exclusive process
time, and 147ms remainder over its 1220ms recorder envelope. Parent recorded
197ms of nested spawn spans, 885ms of process-wait spans/exclusive process
time, and 48ms remainder over 933ms. Spawn spans are nested in waits and must
not be added to them. Separate filesystem and fixture setup/teardown phase
costs are unknown; the recorder's remainder retains unattributed work. Other
cases recorded no child-process spans. TUnit session duration includes further
overhead and is not the sum of case or nested phase durations.

For either immutable source snapshot, rerun from that snapshot's root with one
lease per command:

```sh
TEMP=/tmp TMPDIR=/tmp timeout --kill-after=15s 1500s pwsh -NoProfile -File scripts/build-slot.ps1 -Label g116-build -SlotWaitMinutes 15 -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo --verbosity quiet
TEMP=/tmp TMPDIR=/tmp timeout --kill-after=15s 1080s pwsh -NoProfile -File scripts/build-slot.ps1 -Label g116-tests -SlotWaitMinutes 15 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/ServerGitProcessTests*/*' --report-trx --timeout 3m
```

The temporary source archives, their bin/obj outputs, and generated test-result
outputs were removed after recording these results, as required by the brief.
This document is the sole committed deliverable and preserves the diagnosis,
source identities, outcomes and timing evidence. No repair, landing or green
claim for the failing master/rc scanner is made.

--- next stage ---
next: code
handoff: Repair the test-only construction/launch inventory on master; assert the probe never launches and each real launch has its own maintenance guard; take the reviewed fix into rc.
artifact: docs/investigations/2026-11-05-debug-g116-server-git-process.md
