# Native E2E land-delivery cases — first half (76755674)

Status: batch execution in progress, 2026-10-27. Build and compiled inventory are complete; no passing case coverage is claimed yet.

## Provenance

- Assigned branch: `feat/card-task-76755674`; report base `718bd29f4a1504c64c491d607d31403fcb87dae2`.
- Requested RC fetched before execution: `9f00008facfc48e75555c08df59887019b3f8dfe` (`origin/rc/2026-10-27`).
- The assigned branch and RC have diverged (546 / 494 unique commits). Testing uses an exact `git archive` export at `C:\Antiphon\scratch\76755674-rc`; the assigned branch is unchanged except for this report.
- Evidence root: `C:\Antiphon\worktrees\card-task-76755674\.antiphon\native-76755674` (ignored). Build output name: `bin-native-76755674/`.
- Initial preflight at 11:32 UTC: 7,804 MiB free of 32,709 MiB; no `testhost`, `Antiphon.Tests`, or `Antiphon.E2E` process found. Existing desktop processes are untouched.
- All builds and test drivers use `scripts/build-slot.ps1`. The fixture starts isolated random-loopback server/runner and test Postgres; production ports 17202/17204 are not test endpoints.

## Build and compiled selection

- Native Windows SDK `10.0.300`; test runtime reports Windows `10.0.19045`, x64, .NET `9.0.22`, TUnit `1.44.0.0` / Microsoft Testing Platform `2.2.2`.
- Frontend `npm ci --no-audit --no-fund` and `npm run build` passed under one build slot, 234 seconds held / zero admission wait. The archive has no Git metadata, so Vite's version probe printed `not a git repository`; the bundle build still exited zero. The index hash is retained in `client-bundle.json`.
- One native build passed: 39 warnings / zero errors; MSBuild 371.12 seconds, slot held 377 seconds / zero admission wait. Exact output: `native-build.log`. OutputPath `bin-native-76755674/`; explicit `SourceRevisionId=9f00008facfc48e75555c08df59887019b3f8dfe`.
- Archive comparison at 11:43:03 UTC checked all 9,088 source files with zero differences (`source-verification-before.json`). Archive SHA-256: `A3CF26B1C6BE5A519474E2927F5ED1ADD0261122AA1D06F87913D5040813D97A`.
- Compiled `--list-tests --treenode-filter '/*/*/AgentTaskLandDeliveryE2ETests/*'` emits all 177 assembly cases on this pinned runner. Reflection of the compiled class supplies the 35 declared method names; joining those to discovery gives 52 class cases. Sorting fully qualified names ordinally selects the first 18 methods, expanding to 23 cases. Full roster, both halves and exact method filters: `compiled-roster.json`; assembly/runner hashes: `binary-hashes.json`.
- Batch 1: first nine methods (11 cases); batch 2: next nine methods (12 cases). Prefixes in the pinned TUnit OR syntax match no additional compiled methods. Actual TRX rosters will be checked before coverage is credited.
- Memory recovered to 9,406 MiB before execution; `preflight-batch-1.json` records the immediate process/memory census. Batch 1 started at approximately 11:46 UTC, leased with zero admission wait.

## Evidence collection

Rebuild `client/dist`, build native E2E once, enumerate compiled class methods, sort fully qualified names and select the first `ceil(N/2)`. Execute batches of 8–10 methods, retain fresh TRX durations and exact errors, repeat each failure once, then classify supported mechanisms without modifying source.

## Remaining work

First-half execution, failure repetition/investigation, duration table and owned-output cleanup remain pending. Historical results are not fresh coverage.

## Not done, noted

No fix designed or implemented.

--- next stage ---
next: investigate
handoff: Finish the native first-half rerun and classify any failures from fresh TRX and stored fixture evidence.
artifact: docs/investigations/2026-10-27-native-e2e-land-delivery-first-half.md
