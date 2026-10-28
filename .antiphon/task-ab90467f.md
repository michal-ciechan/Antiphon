# Final Review ab90467f: timing owner-sidecar attribution (subject c3813ab4)

Reviewed tip 7199d96dbaea0a2b4b73fce7262c0d66f75c6f18 (base rc/2026-10-27 f30fddfd433932d95aada7669b78f4fbe49c515c) on server2/Linux.
Verdict: no regression and no new fail-open. Seven advisories follow, none blocking.

## Runs (all through scripts/build-slot.ps1, at 7199d96d)

| Run | Result |
|---|---|
| Build: Antiphon.Tests to bin-rvab/ | exit 0, 0 errors (152 s) |
| Build: SessionRunner.Tests, TestTiming.Probe, Checkpoints.LifecycleHost (all consumers of tests/Shared/TestTiming.cs) | exit 0, 0 errors each |
| `--no-build` filter `(TestPhaseSummaryTests*)\|(TestPhaseSupervisorTests*)\|(TestTimingRecorderTests*)\|(TestTimingLifecycleTests*)\|(TestTimingChokePointTests*)\|(NightlyDiscoveryParserTests*)\|(NightlyScriptsTests*)` | total 39: 37 passed, 1 skipped (C1514T_PS51AndRerun, Windows-only), 1 failed (C1514T_Overhead: upper 0.0236 vs 0.02; host load average 19-20) |
| C1514T_Overhead alone | 1/1 passed (exit 0) |
| scripts/test-qualification-timing.ps1 | exit 0, OK. Foreign witness: 41 files, 188,750,264 bytes, list 23 ms, terminal 85.7 ms, why empty. ps51-add-type-netfx-clean passed. The powershell.exe load row stays pending-native (desktop lane). |
| check-evidence-diff.ps1 -BaseRef f30fddfd -HeadRef 7199d96d | commits=2, entries=0, violations=0 (needed `git fetch --unshallow` first; the mirror was shallow) |
| ASCII scan of both changed .ps1 files | 0 non-ASCII bytes |

## Red witnesses (scratch copies of scripts/, worktree untouched)

I extracted the foreign-journal witness block verbatim into a standalone script that dot-sources a chosen lib copy and runs two passes.
- HEAD lib: both passes PASS (153.5 / 128.9 ms, why empty).
- m1, the base lib (no attribution): FAIL, why=timing-read-refused.
- m2, the foreign-sidecar skip removed (every journal read through the header fallback): FAIL on both passes, why=timing-read-refused (273 / 258 ms). The witness therefore pins the sidecar skip itself, not just the header filter.
- m3, the own-chunk filter removed (every journal treated as owned): FAIL, why=timing-read-refused.
- The C# side: C1514T_OwnerFileNamesTheChunk asserts File.Exists on the sidecar and its exact content, so it is red by construction if the write is removed. Not rebuilt.

## Own scenarios (HEAD lib; chunk exit 3 / KEEP-3 kept in every case)

| Scenario | Result |
|---|---|
| 40 foreign journals x 4.6 MB (~184 MB), all indexed; chunks g-a, g-b, g-c; g-c crashed before its sidecar | each 71-81 ms, why empty, exact own fixture-setup and process-spawn stats; g-c attributed from its header |
| Same root, but 20 of the 40 foreign journals legacy (no sidecar) | g-a 562 ms refused (first call); g-b 216 ms and g-c 186 ms why empty. See A2. |
| Retry: same group and root, attempt 1 and attempt 2 journals both small | attempt-2 stats only (setup 7, spawn 0.7), 50 ms |
| Retry: attempt-1 journal 40 MB, same group token | refused (239 ms). See A3. |
| Own sidecar naming a fifo | refused at 99 ms (fail-closed) |
| Foreign sidecar naming a fifo, plus own-group sidecars naming a missing journal or containing `..%2f`/`../../etc/passwd` text, plus 3000 junk sidecars | no traversal: the journal name comes from the sidecar *file name* matched against the listing; sidecar content is never read. Refused at 740 ms because of the sidecar count. See A1. |
| Legacy journal with a 100 MB unterminated first line | refused at 133 ms (fail-closed) |
| Foreign sidecar naming our journal while our sidecar is missing | our phase stats dropped (TRX stats kept). See A5. |
| Upper-case hex sidecar token for our group | matched case-insensitively; the header check still applies, so harmless |
| Scale: N tiny indexed foreign journals plus sidecars, under load 36-41 | N=100: 96-150 ms ok. N=150: 109-118 ms ok. N=200: 115-158 ms ok (an earlier pass at N=200 refused at 252-276 ms). N=300: 239-404 ms refused. N=500 and N=1000: refused. See A1. |

## Recorder (tests/Shared/TestTiming.cs)

- The header is written and flushed (WriteLine flushes) before the sidecar, so a host that crashes between them is still attributed by the header fallback; verified with g-c.
- The sidecar is written through File.WriteAllText inside the existing IOException/UnauthorizedAccessException catch.
- Its name is built only from lowercase hex, the pid digits and a validated nonce token, so it adds no new exception type.
- On failure the partially opened session is disposed; Dispose swallows its own IO errors. No test outcome changes.
- The name is unique per pid and GUID nonce. Distinct groups give distinct lowercase hex, so case-insensitive file systems cannot collide.
- Overhead is one small file per host at open. In C1514T_Overhead the session opens outside the timed walls, so the sidecar does not enter the 2% bound.
- Real chunk ids, e.g. `all-group-65-split-12-class-3-profile-4` (39 chars), give a name of about 130 bytes, well under the 255-byte component limit.

## Advisories (not regressions; every edge is fail-closed with exit, result and coverage kept)

- **A1 Reader scale.** The listing and per-shard PowerShell loop cost about 0.5-1 ms per journal+sidecar pair under load. Past about 200-300 test hosts in one timing root, the chunk read goes back to why=timing-read-refused. Base refused at 41, so this is a large improvement, but if a Full run spawns more than about 200 journal-writing hosts, late chunks will refuse again.
  Fix if needed: enumerate only `own-<token>-*.txt` plus `cases-*.jsonl` names, and skip foreign names without building per-entry cache keys.
- **A2 Legacy fallback cost.** Unindexed journals cost an lstat, an open and a first-line read each. Twenty to forty of them can exhaust the budget (m2 and the 20-legacy case). This only matters for mixed-version roots or hosts whose sidecar write failed.
- **A3 Same-root retries.** The sidecar token omits runId and attempt. A rerun of the same group in the same root (a reused RunId) fully reads the earlier attempt's journals before the header filter discards them: stats are correct, but a large earlier journal refuses the retry. Base behaved the same. release-cut seals the timing root per RunId.
- **A4 Sidecar failure is all-or-nothing.** A sidecar write failure (read-only file, or a groupId over about 105 UTF-8 bytes, which pushes the name past 255 bytes) disables the whole session (phase-sink-failed), even though the header fallback could still have attributed the journal. Real ids are far shorter.
- **A5 Hostile foreign sidecar.** A foreign sidecar naming our journal hides it when our own sidecar is missing. This needs a pid plus GUID-nonce collision.
- **A6 C1514T_Overhead is load-sensitive.** It went red in the 39-test combined run (load about 19) and green alone. The sidecar is outside its measured interval, so this is not caused by the change.
- **A7 Pending native rows.** The powershell.exe load row and the Windows-only TestPhaseSummaryTests case remain for the desktop lane.

## Left in place (the brief said to delete nothing)

- `bin-rvab/` output directories under the four built test projects and their referenced projects (gitignored).
- Scratch fixtures, about 1 GB, under /tmp/claude-1654/-work-worktrees-task-ab90467f/6e72ae81-a6d3-42d9-b64c-db67711a9071/scratchpad (scenroot, scaleroot, scaleroot2, m1, m2, m3).
- The mirror was unshallowed (local objects only; no branch changed).
