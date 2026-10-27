# Native E2E land-delivery cases — first half (76755674)

Status: execution pending, 2026-10-27. This early report records provenance; no passing coverage is claimed yet.

## Provenance

- Assigned branch: `feat/card-task-76755674`; report base `718bd29f4a1504c64c491d607d31403fcb87dae2`.
- Requested RC fetched before execution: `9f00008facfc48e75555c08df59887019b3f8dfe` (`origin/rc/2026-10-27`).
- The assigned branch and RC have diverged (546 / 494 unique commits). Testing uses an exact `git archive` export at `C:\Antiphon\scratch\76755674-rc`; the assigned branch is unchanged except for this report.
- Evidence root: `C:\Antiphon\worktrees\card-task-76755674\.antiphon\native-76755674` (ignored). Build output name: `bin-native-76755674/`.
- Initial preflight at 11:32 UTC: 7,804 MiB free of 32,709 MiB; no `testhost`, `Antiphon.Tests`, or `Antiphon.E2E` process found. Existing desktop processes are untouched.
- All builds and test drivers use `scripts/build-slot.ps1`. The fixture starts isolated random-loopback server/runner and test Postgres; production ports 17202/17204 are not test endpoints.

## Planned evidence collection

Rebuild `client/dist`, build native E2E once, enumerate compiled class methods, sort fully qualified names and select the first `ceil(N/2)`. Execute batches of 8–10 methods, retain fresh TRX durations and exact errors, repeat each failure once, then classify supported mechanisms without modifying source.

## Remaining work

Build, compiled inventory, first-half execution, failure repetition/investigation, duration table and owned-output cleanup remain pending. Historical results are not fresh coverage.

## Not done, noted

No fix designed or implemented.

--- next stage ---
next: investigate
handoff: Finish the native first-half rerun and classify any failures from fresh TRX and stored fixture evidence.
artifact: docs/investigations/2026-10-27-native-e2e-land-delivery-first-half.md
