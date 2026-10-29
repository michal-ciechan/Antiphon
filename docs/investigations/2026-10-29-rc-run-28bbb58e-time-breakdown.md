# rc run 28bbb58e: where the 22 h 58 m went

Run `28bbb58edca04b0ebb7afc914b74d386` (task 5906dbf2), rc `4563e525`, native Windows (DESKTOP),
Full, `nativeActiveMax` 1. 2026-10-28T10:13:24Z to 2026-10-29T09:11:30Z, `durationSeconds` 82686.4.
Sources (read-only): `C:\Antiphon\releases\candidates\rc-2026-10-27-4563e52529b2\logs\2026-10-28-1013-28bbb58e\summary.json`
(`timingChunks[]`, `suites[]`, `suiteTimeline[]`), the timing root `...\logs\timing-28bbb58edca04b0ebb7afc914b74d386\`
(`<suite>-<chunk>.timing.json`, `tests.csv`, `cases-*.jsonl`), the setup logs in the run folder, and
`C:\Antiphon\releases\task-5906dbf2\commission-console.log`.

## 1. Time budget (skeleton, filling in)

| Phase | Start (UTC) | Seconds | Minutes | Source |
|---|---|---:|---:|---|
| npm ci | 10:13:24 | 25 | 0.4 | npm-ci.log mtime |
| npm run build (tsc + vite) | 10:13:49 | 21 | 0.3 | npm-build.log mtime |
| client lint | 10:14:10 | 35 | 0.6 | client-lint.log mtime |
| dotnet build Antiphon.sln | 10:14:44 | 370 | 6.2 | dotnet-build.log `Time Elapsed 00:06:09.35` |
| Antiphon.Tests profile discovery (20156 tests) | 10:20:55 | 21 | 0.4 | discovery log/json mtimes |
| unlogged (group planning, before first chunk) | 10:21:16 | 190 | 3.2 | gap to groups-planned.json create 10:24:26 |
| 387 chunks executed serially | 10:24:26 | 81945 | 1365.7 | timingChunks (386) + group 105 |
| inter-chunk gaps (excluding group 105) | | ~84 | 1.4 | start(n+1) − end(n) |
| **run total** | | 82686 | 1378.1 | summary.json |
| post-run summary + report (outside durationSeconds) | 09:11:30 | 189 | 3.2 | commission-console.log |
