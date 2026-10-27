# CARD-1313 native ChangeAwareRelease selection-worker-timeout and PS5.1 obligation

Task b11dd450, 2026-10-27; selection code CARD-1313, timing qualification context CARD-1514.

The complete evidence report is [Native ChangeAwareRelease timeout and PS5.1 obligation](2026-10-27-native-change-aware-timeouts.md).

Confirmed: the Windows origin reader returns Git-config escape bytes (`scripts/lib/nightly-common.ps1:187-188`) and rejects a correct decoded local path (`:238`). A clean CRLF checkout also fails the selector's raw source pin (`scripts/lib/test-selection.ps1:174`). The actual PS5.1 timing load path passes its native audit. Serial Git calls dominate selection worker cost; the original untraced 60s timeout trigger remains unconfirmed because Git tracing materially changes the runtime. Twelve compiled test cases executed: 5 passed, 7 failed; the nine untraced repetitions had no selection-worker timeout. No source fix was made.

The linked report contains source SHAs, exact transcripts/rows, timings, the one-build provenance, diagnostic observer limitations, and cleanup refusal. Automatic approval review blocked deletion of 33 task-created output roots and the detached diagnostic checkout; caller cleanup remains.

--- next stage ---
next: investigate
handoff: Use the full report to capture the historical untraced worker timeout under representative load; triage the separate confirmed origin-parser and source-pin findings into Plan, and retain the fixture-cost and cleanup obligations.
artifact: docs/investigations/2026-10-27-native-change-aware-timeouts.md
