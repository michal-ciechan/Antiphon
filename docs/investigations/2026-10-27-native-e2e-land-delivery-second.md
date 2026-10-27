# Native E2E land-delivery cases: second half

Task `d594e971`, 2026-10-27. Investigation and timing evidence only; execution in progress.

## Provenance and scope

- Requested RC, fetched and pinned: `9f00008facfc48e75555c08df59887019b3f8dfe`.
- Assigned report branch: `feat/card-task-d594e971`, starting at `718bd29f4a1504c64c491d607d31403fcb87dae2`. This branch differs from the RC. To preserve the assigned branch without rebasing or merging, native runs use a fresh Git archive export at `C:\Antiphon\scratch\d594e971-rc`.
- Generated evidence: `C:\Antiphon\evidence\d594e971`; `preflight.json` records 8,413 MiB available and no test hosts at 11:34:22 UTC. The source archive is retained there.
- Compiled output will be `bin-native-d594e971/`. Each driver takes `scripts/build-slot.ps1`; tests use the isolated fixture server/runner. No live AppHost/runner restart is authorized or performed.
- The compiled class roster will be sorted by fully qualified method name, then split at `ceil(N/2)`. This task owns the remaining methods, including all argument cases for each selected method.

## Current evidence

No build or test result is claimed yet. The frontend must be rebuilt before execution. Per-case TRX durations, exact failures, reruns and source/DB/transcript evidence will replace this preliminary section.

## Not done, noted

No product or test fix designed or implemented; existing C508 admission findings belong to task `1794d6c7`.

--- next stage ---
next: investigate
handoff: Complete the pinned native second-half roster, exact batches, per-case timing and failure classification; no current result is established.
artifact: docs/investigations/2026-10-27-native-e2e-land-delivery-second.md
