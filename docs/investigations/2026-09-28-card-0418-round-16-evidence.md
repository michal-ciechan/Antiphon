# CARD-0418 round 16: Linux CP-10 and source read roots

This continues the [round-15 ledger](2026-09-28-card-0418-round-15-evidence.md). The initial Linux run used the exact round-15 pushed tip `b0bb73895938bff932a774ae4ed7b2d6efc3a54b`. No live destination, shared-stack restart, or land was used.

## CP-10 at the round-15 tip

The checkpoint tool ran the plan's CP-9 and CP-10 rows together in `.antiphon/checkpoints/20260928-211152-3f41/` on Debian 12. CP-9 built the isolated Markdown PDF tests and passed **19/19** with zero skips. CP-10 reused that build and passed **1/1** with zero skips; its real Chromium fixture independently parsed the four-document PDF and inspected rendered pages. Both rows held granted host build slots. The tool ran no unlisted row.

This closes the platform gap left by the round-15 Windows sweep at that exact source commit. Round 15 already supplied Windows process-tree evidence for CP-9; this Linux run supplies the browser fixture that Windows skips.

## V-2/R-2 source read-path increment

Commit `561939f76139706653cd2a8dd3c5a657c8d80cf4` adds three integration cases to `DeliverableBundleServiceTests.Source_bytes_survive_each_disk_read_root`. They put a UTF-16 BOM, CRLF, Unicode and trailing spaces in a source file reachable from only one of `WorktreePath`, `WorkingDirectory` and `RepoPath`; each case checks the attached copy's exact bytes and the manifest SHA-256. This covers the service's three disk read roots without conflating them with the separate branch-only Git read test. It narrows V-2/R-2; the full provenance and completion-note matrix remains open.

The checkpoint tool ran CP-1 and CP-2 after that commit in `.antiphon/checkpoints/20260928-211436-f154/`. CP-1 passed **3,484/3,484** with 33 platform skips. CP-2 passed **343/343** with zero skips, including all three new cases. Both rows held granted slots; the tool ran no unlisted row.

## R-14 accounting decision

`ChannelOutboundEvidenceAccounting` and its three synthetic validator tests exist, but this round does not present them as a validated execution-evidence index. The plan asks for independent assertions inside each V/R ID, while the validator currently accepts one named green method per ordinary ID. Many of the named V-1–V-18 and V-21–V-23 sub-assertions remain open in the round-8, round-13 and round-15 ledgers. Indexing one passing method under each broad ID now would make an incomplete matrix look complete. V-25 requires separately reviewed native destination evidence, and PC-1–PC-30 remain for method-scoped SourceLanding Mutation. R-14's actual-run index and validator invocation therefore remain pending; no synthetic fixture or local fake receipt is claimed as that gate.

## Final checkpoint sweep

The checkpoint tool ran the plan's complete closed list at source commit `561939f76139706653cd2a8dd3c5a657c8d80cf4` in `.antiphon/checkpoints/20260928-212653-4587/` (Debian 12, wall 46m04s). Its report, TRX files, CP-8 failure detail and host/slot records are retained there. Every row driver held a granted host slot; the tool ran no unlisted row command. The three `dotnet run --project tools/Antiphon.Checkpoints` launchers in this round also performed the tool's own incremental bootstrap outside a separately acquired build slot; this is the unlisted launcher build, distinct from the leased checkpoint row builds.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3484 | 3484 | 0 | 33 | Green; Linux platform skips |
| CP-2 source settlement | 343 | 343 | 0 | 0 | Green, including the three new disk-root cases |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red, inherited `PinnedAgentKindTests.T1` and `T2` `codex_desktop_unqualified` refusals, the same two cases as rounds 11–15 |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux; Windows descendant evidence remains round 15 |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green on Linux |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green with `ANTIPHON_BROKER_TESTS=1` |
| CP-12 client | 26 | 26 | 0 | n/a | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

Overall: 12 green rows, CP-8 red with two inherited failures, tool exit 1. CP-8 is not called green, and this checkpoint sweep alone does not close the still-open plan matrix.

## Remaining gates

The [round-8 open-case table](2026-09-28-card-0418-round-8-evidence.md#remaining-ordinary-scope) and [round-15 summary](2026-09-28-card-0418-round-15-evidence.md#remaining-gates) remain the ordinary-case ledger except for the three disk read-path cases above. Full source provenance and completion-note paths, send-shape and control callers, worker-purpose and refusal companions, file/ZIP fault and serialized fallback, complete routing/crash/multi-target/TTL/policy-race oracles, monitor controls and near-default wire publication still need named evidence. V-25 and PC-1–PC-30 remain separate later gates. Do not land from this evidence.
