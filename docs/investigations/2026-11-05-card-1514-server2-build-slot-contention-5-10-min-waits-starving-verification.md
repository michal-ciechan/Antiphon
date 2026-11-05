# CARD-1514: server2 build-slot contention

The complete evidence report, reconstructed lease ledger, resource measurements and remaining uncertainties are in [2026-11-05-server2-build-slot-contention.md](2026-11-05-server2-build-slot-contention.md).

Task: `a8d643be-e182-4ec8-b67c-767be7e58e22`. Source inspected: `d10e1c51a72852aa51895109b66e567f81346dca`. Four shared build/test slots competing with seven or eight tasks are confirmed; safe capacity sizing, peak CPU/I/O attribution and complete broker history remain unresolved. No product changes or live mutations were made.

--- next stage ---
next: decide
handoff: Use the confirmed shared-pool contention evidence; obtain host broker logs and peak resource samples before choosing capacity or scheduling changes. Preserve the separate long-test execution failures and operator-only host budget authority.
artifact: docs/investigations/2026-11-05-server2-build-slot-contention.md
