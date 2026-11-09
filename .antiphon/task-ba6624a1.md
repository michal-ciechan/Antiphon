# CARD-1813 Review round 1 (Final, Full) — Code 55b84e31 tip fe5f054b4

Verdict: **3 blockers** (D-1..D-3). Advisories A-1..A-4 go to cards and do not hold a land.
Reviewed `364e271331..fe5f054b4fb00a857ead26b5b4afef74f3e4425b` (2 commits, red 9bbace4e8 then fix fe5f054b4).
Read-only review. No product or test source was changed. Mutants ran in a detached scratch worktree, and only the
probe-host dll in this worktree's bin was swapped. It was restored afterwards: sha256 608eac20216b0933... before and after.

## Evidence (server2 Linux, all builds and runs leased through build-slot, private TMPDIR on every test host)

| Row | Result |
|---|---|
| build Antiphon.Tests / SessionRunner.Tests (UseAppHost=false, native output) | exit 0 / exit 0 |
| PwshPrivateCacheTests* + ScriptHarnessLinuxOwnershipTests* @ fe5f054b4 | 31/31 passed (9 + 22) |
| C1793SessionPwshCacheTests* (SessionRunner.Tests) | 2/2 passed |
| Guards: TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests* | 52/52 passed |
| Red: probe host built from base PwshPrivateCache.cs (364e27133), same 31 | 5 failed for the right reasons: Cache_root_override (stale kept), A_live_pid_different_start (reused kept), A_fresh_sweep_stamp (stale swept), Old_unmarked (old kept), Supervisor_removes_its_private_cache (`parts.Length` = 1, pid-only marker) |
| M1 UnmarkedExpired => true | killed: Old_unmarked + Install_removes_dead_owner... |
| M2 IsLive ignores start ticks | killed: A_live_pid_with_a_different_start |
| M3 stamp check disabled | killed: A_fresh_sweep_stamp |
| M4 root override ignored | killed: Cache_root_override |
| M5 drop Garbled from the skip | survived, equivalent: a garbled record has pid 0, and `GetProcessById(0)` succeeds, so it counts as live |
| M5b garbled => delete | killed: Old_unmarked + Install_removes... |
| M6 legacy pid-only marker => dead | killed: Install_removes_dead_owner... |
| check-evidence-diff 364e27133..fe5f054b4 | exit 0, commits=2 violations=0 |
| merge-tree origin/master (25b347f49) HEAD | exit 0, clean (tree 00b962c73) |
| land-graph -Check on the local scratch merge (fece0613, not pushed) | exit 0 `approved` |
| tests/test-execution-policy.json | unchanged |

Note on the first class run: a scratchpad TMPDIR longer than 108 characters made 22 ScriptHarnessLinuxOwnership tests fail
with a domain-socket path-length error. That was my environment. The rerun with a short private /tmp/r1813 is the row above.

## Defects

### D-1 (blocker, regression): on Linux, a wall-clock step deletes LIVE caches
- **Where:** `src/Antiphon.Hosting/PwshPrivateCache.cs` `IsLive`, `SameStart`, `TryStartTicks`, `OwnedByThisProcess`. Also `SessionRunnerRuntime` passes `_hostStartTimeUtc` (Process.StartTime) to `AssignOwner`.
- **Failure:** On Linux, .NET's `Process.StartTime` is `bootTime + /proc/<pid>/stat starttime`. `bootTime` is computed once per observing process as CLOCK_REALTIME_COARSE minus CLOCK_BOOTTIME. Measured on server2: inside one process, 300 reads give the same value (spread 0 ms). Six separate processes reading pid 1 differ by up to 1.4 ms. NTP stepping, a VM resync or a leap second moves REALTIME but not BOOTTIME. After any step larger than 50 ms, every process that reads the clock after the step computes every live owner's start differently. The difference is the size of the step, so `SameStart` fails and `SweepStale` deletes the cache of a live session or sibling test host. That is the CARD-1793 A-1 incident class. The long-lived session runner makes this worse. It caches its own `bootTime` at its first StartTime read, possibly days earlier, and writes host ticks with that stale base. The pre-change pid-only marker never deleted a live owner's cache, so this is a new hazard. On Windows, StartTime is the kernel's stored creation FILETIME and is unaffected.
- **Why the tests miss it:** C1793 and the marker tests compare ticks read in the same process or immediately afterwards. No test can step the clock.
- **Fix:** On Linux, record and compare the kernel start generation, `/proc/<pid>/stat` field 22 (clock ticks since boot), exactly, with no tolerance. The repo already has this pattern in `src/Antiphon.SessionRunner/LinuxProcessIdentity.cs` `TryParseStartTicks` and `src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs` `StartToken`, whose comment reads "Unlike Process.StartTime's UTC conversion it is byte-stable across observers". PwshPrivateCache is a source include, so inline the parse. Keep Process.StartTime with the tolerance on Windows. On Linux, have `AssignOwner` read `/proc` for the host pid instead of trusting `startedUtc`. Pid-only legacy markers stay live-if-exists. Add a witness that the Linux marker value equals the `/proc` starttime field.

### D-2 (blocker, brief acceptance item): a future-dated stamp disables sweeping until that date
- **Where:** `SweepIsCurrent`: `DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) < SweepInterval`. A negative age counts as current.
- **Failure (reproduced at the tip with the tip's probe host):** I seeded a dead-owner cache (pid 99999 > pid_max 32768) and wrote a stamp, then ran `touch -d '+1 year'` on it. The probe host exited 0 and the stale directory survived, with the stamp still dated 2027-11-09. The control, a stamp 11 minutes old, swept it. Only a sweep rewrites the stamp, so a clock that was ahead and is later corrected backwards, or any far-future mtime, stops all sweeping for the size of the skew. The brief says a future stamp "must not disable sweeping forever".
- **Fix:** Treat the stamp as current only when `0 <= age < SweepInterval`, so a future mtime means sweep and re-stamp. Add a witness with a stamp mtime one year ahead.

### D-3 (blocker, test adequacy for the A-1 deliverable): the documented `ANTIPHON_PWSH_CACHE_ROOT` recipe makes the class red with no mutant
- **Where:** `tests/Antiphon.Tests/Scripts/PwshPrivateCacheTests.cs` `ProbeAsync` and `Checkpoint_tool_installs_...`. These children inherit the test host's `ANTIPHON_PWSH_CACHE_ROOT`, which overrides the TMPDIR/TMP/TEMP they set. `docs/testing-and-build.md` (new paragraph) says to set "TMPDIR, TMP and TEMP, or ANTIPHON_PWSH_CACHE_ROOT" on the test host.
- **Failure:** At the unmutated tip, with only `ANTIPHON_PWSH_CACHE_ROOT=<private>` added to the test host, 6 of 9 tests fail: Two_concurrent_hosts, A_live_pid_with_a_different_start, Install_removes_dead_owner_caches, Checkpoint_tool_installs, Old_unmarked, Absent_empty_and_foreign. A_fresh_sweep_stamp then passes vacuously. A Mutation stage that follows the doc's ROOT option sees every sweep mutant "killed", so its PC evidence is invalid. With TMPDIR only, the class is 9/9 green and the mutants above are discriminating.
- **Fix:** In the test, remove `ANTIPHON_PWSH_CACHE_ROOT` from each child environment before `configure` and in the checkpoint-tool test. Cache_root_override sets it again explicitly. Alternatively, make the doc recommend TMPDIR/TMP/TEMP only. The test fix is preferred because it keeps both options safe.

## Advisories (cards, never hold the land)
- **A-1:** `CacheRoot` fails open. When `ANTIPHON_PWSH_CACHE_ROOT` is set but unusable (a symlink, a file, or an access error), the sweep silently falls back to `Path.GetTempPath()`, which is the real /tmp when only the override was set. That is the exact A-1 hazard. Prefer skipping the sweep (create only) when the override is set but rejected.
- **A-2:** `IsLive` maps `InvalidOperationException` to dead. A live process whose StartTime read throws would be swept. This is narrow, and before the change only the exited path reached it. Consider "unknown => keep" for the StartTime read.
- **A-3:** M5 (Garbled not skipped) is equivalent only because garbled records carry pid 0 and pid 0 resolves as live on both OSes. A future refactor that changes the Garbled pid would remove this second guard. M5b shows that the explicit skip is witnessed.
- **A-4:** Windows execution of the 4 new tests plus the changed assertions is owed to rc qualification (not run here).

Checked and safe: only `antiphon-pwsh-cache-*` children directly under the root are candidates, by exact parent and prefix, with reparse points excluded. Root `/` sweeps nothing because the TrimEnd makes the parent comparison fail. Delete does not follow links. Legacy pid-only markers stay alive-if-pid-exists. The old CARD-1793 code treats new `pid@ticks` markers as unparsable and keeps them, so mixed versions are safe in both directions. Stamp temp+rename is atomic, and concurrent first sweeps are bounded with no lost-sweep path. Unmarked grace uses the directory mtime, which the marker write and rename bump. No MSBuild change. Herdr A-5 is documented in docs/herdr-sessions.md and by a comment.
