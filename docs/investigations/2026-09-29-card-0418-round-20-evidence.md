# CARD-0418 round 20: historical custody and structural source boundary

This continues the [round-19 ledger](2026-09-29-card-0418-round-19-evidence.md). The tested source tip is `17cec451427a72c7c56f62971d2335525e0a3740`. No shared-stack restart, live destination, land, or Mutation positive control was attempted.

## New ordinary evidence and exact ID accounting

**V-3 closed.** `ChannelFollowUpAttachmentTests.Historical_pdf_is_explicit_once_and_manifest_custody_survives_reply` runs four actual fake-channel reply cases: valid source manifest, corrupt manifest, unknown manifest version, and pre-manifest legacy bundle. Each case plants a historical PDF, stale PDF, unlisted Markdown, unrelated ZIP, render HTML/log and temporary file. The PDF is sent exactly once through its explicit attach marker and the sent bytes equal its original file. The valid manifest attaches only its listed Markdown; corrupt/unknown manifests attach no implicit file; the legacy fallback attaches Markdown (including the otherwise unlisted file) and `*-sources.zip` only. The reply's exact attachment-name array excludes the stale PDF, unrelated ZIP, HTML/log and temporary file in every case. `SourceBundleManifestTests.Only_authorized_source_members_are_implicit` additionally creates `docs/cards/`, `.antiphon/` and traversal candidates and verifies that the generated bundle contains only its authorized source. Existing manifest/legacy custody assertions remain in that method.

**V-21 closed.** `ChannelOutboundContractTests.Server_and_gateway_keep_source_and_transport_boundaries` now asserts the workspace provisioner prescribes automatic Markdown sources and a configured optional conversion, and forbids the old universal PDF instruction. It checks the server composition root for source bundling and absence of renderer registration, and every API endpoint source for a renderer dependency or render-PDF endpoint. Its existing assertions cover the independent tool/server project graph, default enabled source bundling, no browser/timeout defaults, channel preambles, orchestrator bundle, nullable/unique model links and enum values. `ChannelOutboundMigrationTests.Upgrade_preserves_history_and_defaults` migrates a private PostgreSQL schema from the preceding migration with a historical task and channel, then verifies the old result/thread, null new binding/FK and unique delivery/task constraints. `ChannelOutboundEndpointTests.Legacy_renderer_keys_warn_once_on_fresh_host_without_browser` verifies a fresh host's one warning and no browser launch. CP-2's source completion receipt matrix settles sources without conversion even with legacy keys and a valid fake browser, retaining explicit attachment syntax.

**R-1 and R-12 closed** using V-1/V-21 and V-19/V-20/V-21 respectively. V-1's completion matrix is recorded in round 19. V-19's failure/process cleanup evidence and V-20's independently parsed/visually inspected PDF are recorded in earlier ledgers. This round's structural test makes each universal-renderer restoration target visible, while the real settled source receipts keep the default path source-only.

The committed-slice checkpoint tool run `.antiphon/checkpoints/20260929-013100-6324/` passed **CP-2 371/371** and **CP-3 30/30**, zero skips, with granted host slots and no unlisted row. A separately leased checkpoint-tool bootstrap build passed with the existing `TaskOwnerGuard.cs` CS8602 warning; it was the only unlisted build and was needed because this worktree had no built tool. An earlier CP-2 attempt at `.antiphon/checkpoints/20260929-012137-ca8a/` ended without a verdict when the shared filesystem reached zero free bytes while writing `console.log`; it produced no valid checkpoint result. The exact committed CP-2 row passed after space returned. No foreign worktree or process was deleted.

## Remaining ordinary done criterion

Closed this round: **V-3, V-21, R-1, R-12**. Previously closed: **V-1, V-2, V-4, V-19, V-20, V-24**. Still open: **V-5–V-18, V-22–V-23, R-2–R-11, R-13–R-14**. The exact missing sub-assertions remain in the [round-17 inventory](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions), excluding closed IDs. R-2 still requires V-16's complete publication outcome and source-completeness matrix. R-14 still needs a per-sub-assertion actual-run index and a validator that rejects broad-ID tokens. V-25 remains for authorized live receipt, and PC-1–PC-30 remain for method-scoped SourceLanding Mutation; neither was attempted.

## CP-1 through CP-13 Final sweep

The checkpoint tool ran the plan's complete closed list on the committed source tip in `.antiphon/checkpoints/20260929-014000-fdf2/` (Debian 12, 40m37s). The native report, TRX, CP-8 failure detail and host/slot records are there. Every row held a granted host slot; the tool ran no unlisted row command. Its exit was **1: twelve green rows and one red**. CP-8's two `codex_desktop_unqualified` failures are the identical inherited `PinnedAgentKindTests.T1`/`T2` pair from rounds 17–19; this branch did not introduce them.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3484 | 3484 | 0 | 33 | Green; Linux platform skips |
| CP-2 source settlement | 371 | 371 | 0 | 0 | Green, including four new V-3 reply cases |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green, including expanded V-21 contract |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited T1/T2 `codex_desktop_unqualified` |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux; Windows descendant evidence remains round 15 |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green; parsed/inspected real PDF |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green with `ANTIPHON_BROKER_TESTS=1` |
| CP-12 client | 26 | 26 | 0 | n/a | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

This sweep verifies executed local assertions. It does not close the remaining V/R IDs, V-25, or any PC. Do not land from this evidence.
