# CARD-0418 round 18: branch ZIP custody and parent completion receipt

This continues the [round-17 evidence ledger](2026-09-28-card-0418-round-17-evidence.md). The source slice is `8725db1cae6aed8cd7dbfc52320c663517872997`. No shared-stack restart, live destination, or land was used.

## New ordinary evidence

`DeliverableBundleServiceTests.Branch_only_git_sources_survive_zip_with_exact_bytes_and_manifest_hashes` commits six UTF-16 BOM/CRLF/Unicode/trailing-space Markdown files to a branch, deletes every disk copy, then bundles them as one ZIP. It checks each extracted member against the original byte array and checks the manifest's original path, stored ZIP name, entry path, length and SHA-256, plus complete/no omissions. Together with the branch-only inline case and the round-16/17 disk-root inline/ZIP cases, this closes the read-path/packaging gap recorded for V-2. `SourceBundleManifestTests.Bytes_names_thresholds_and_budget_are_truthful` covers the five/six and exact 1 MiB thresholds, duplicate basenames, 64 selected paths, exact 64 MiB and +1 omission. V-2 is now **closed for its listed local source packaging assertions**; it does not imply V-3's reply-path custody or R-2's V-3/V-16 components.

`AgentTaskReplyIntegrationTests.completed_sources_reach_the_parent_as_exact_inline_or_zip_attachments` runs the actual settlement and session-message queue for four inline sources and six ZIP sources. A fake terminal writes a `UserPrompt` transcript record, and the queue's transcript-confirmed delivery verdict, source task/root conversation/digest and single prompt are checked. The note carries all four attach markers or one ZIP marker, the original report prose and truthful `N md`/`N md, sources zip` header; extracted/copied source bytes match the originals, and the settled task has no PDF path or render error. The note excludes task/report transport markers. This narrows V-1/V-4/R-1: it is an isolated parent-session receipt for one Docs role, not the full cross-project/role/configuration matrix or actual X/Y/Z channel routing.

The committed-slice CP-2 run `.antiphon/checkpoints/20260928-231618-c0ac/` passed **347/347**, zero skips, with a granted build slot. It ran no unlisted row. The separately leased checkpoint-tool bootstrap build passed with its existing `TaskOwnerGuard.cs` CS8602 warning; that build is outside the closed checkpoint table and is disclosed here.

## Exact remaining ordinary done criterion

The Code-stage ordinary gate is complete only when every still-open ID below has native evidence for **every sub-assertion in the plan's V/R matrix**, with a named test/result or manual artifact for each; a green class or one passing method does not close its whole ID. The [round-17 table](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions) remains the detailed missing-assertion inventory except for V-2 and the V-1/V-4 increment above.

| Status | Exact IDs | Required finish |
|---|---|---|
| Still open | V-1, V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10, V-11, V-12, V-13, V-14, V-15, V-16, V-17, V-18, V-21, V-22, V-23 | Each plan row's full oracle and positive/control cases, including actual source completion, policy, dispatch, recovery, transport and monitor outcomes. |
| Still open | R-1, R-2, R-3, R-4, R-5, R-6, R-7, R-8, R-9, R-10, R-11, R-12, R-13, R-14 | Every mapped V sub-assertion plus the specific risk's own positive/control evidence; R-14 requires an actual-run index that names every assertion and a validator invocation that cannot accept a broad-ID token as proof. |
| Closed local gates | V-2, V-19, V-20, V-24 | Preserve their named native artifacts from this and earlier rounds; V-24 is isolated fake-destination evidence only. |
| Later gates, untouched | V-25, PC-1 through PC-30 | V-25 requires an authorized live destination receipt; every PC requires method-scoped SourceLanding Mutation. No local fake receipt or green checkpoint substitutes for them. |

This is an exact ID roster, not a release claim. Closing an ID means its entire plan row is evidenced and removed from the still-open row; closing R-14 is last, after the evidence index has one entry per claimed sub-assertion. A future decision to close this work with residual gaps should cite the exact IDs and missing assertions from this roster rather than treating the Final checkpoint sweep as their replacement.

## CP-1 through CP-13 Final sweep

The checkpoint tool ran the plan's complete closed list on the committed source slice in `.antiphon/checkpoints/20260928-232428-2301/` (Debian 12, 36m27s). The native report, TRX, CP-8 failure detail and host/slot records are retained there. Every row held a granted host slot; the tool ran no unlisted row command.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3484 | 3484 | 0 | 33 | Green; Linux platform skips |
| CP-2 source settlement | 347 | 347 | 0 | 0 | Green, including the new branch ZIP and parent receipt cases |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited `PinnedAgentKindTests.T1` and `T2` `codex_desktop_unqualified` refusals, exactly the prior rounds' pair |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux; Windows descendant evidence remains round 15 |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green on Linux |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green with `ANTIPHON_BROKER_TESTS=1` |
| CP-12 client | 26 | 26 | 0 | n/a | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

Overall tool exit 1: twelve green rows and CP-8 red with two inherited failures. The sweep verifies the new slice and does not close the still-open V/R assertions, V-25, or any PC. Do not land from this evidence.
