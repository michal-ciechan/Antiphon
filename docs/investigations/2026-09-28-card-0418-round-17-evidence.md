# CARD-0418 round 17: cross-root ZIP source custody

This continues the [round-16 ledger](2026-09-28-card-0418-round-16-evidence.md). The tested source slice is `791c0efd923cc1867e518f81ba0cb25b83fde5dc`. No shared-stack restart, live destination, or land was used.

## V-2/R-2 increment

`DeliverableBundleServiceTests.Source_bytes_from_all_disk_roots_survive_a_single_zip` creates six Markdown sources across `WorktreePath`, `WorkingDirectory`, and `RepoPath`, forcing one ZIP. It compares every extracted entry with its original UTF-16 BOM/CRLF/Unicode/trailing-space bytes and independently checks the manifest's path, length, SHA-256, complete flag, and no-omission list. The prior round tested each disk root's inline copy; `SourceBundleManifestTests.Bytes_names_thresholds_and_budget_are_truthful` already covers five/six source thresholds, duplicate basenames, exact 1 MiB and 64 MiB boundaries, 64 paths, and recorded omissions. The branch-only Git read path is still tested inline, not inside a ZIP, so V-2's full read-path/packaging cross-product remains open.

The committed-slice CP-2 run `20260928-221818-f4c3` passed **344/344**, zero skips, with a granted host slot. Its native report and TRX are under `.antiphon/checkpoints/20260928-221818-f4c3/`. The checkpoint tool ran no unlisted row. A separately leased build of the checkpoint tool succeeded with the existing `TaskOwnerGuard.cs` CS8602 warning.

## Remaining ordinary V/R assertions

The following is the handoff inventory. A green checkpoint class is evidence for its executed methods, not for absent rows in the plan's [V/R matrix](../superpowers/plans/2026-09-07-card-0418-channel-outbound-agent-plan.md#proves-it-works-now).

| IDs still open | Required evidence still missing |
|---|---|
| V-1 | Actual completion-note/source/zero-render outcome across both projects and all eligible roles, legacy configuration, enabled-off and no-channel variants in one named matrix. |
| V-2 | Branch-only Git source through ZIP with byte/hash oracle; all disk roots now have inline and ZIP evidence. |
| V-3 | Historical PDF explicit-attachment-once and complete stale/unknown manifest, path-exclusion custody matrix through a real reply. |
| V-4 | The real four-source task-done note, marker-free prose, and X/Y/Z direct versus deferred delivery, including the generated complete-source ZIP variant. |
| V-5 | Complete endpoint and client select/save/clear/refetch and atomic invalid-change matrix with pinned project/converter/prompt revision and one-metered-invocation oracles. |
| V-6 | Main, trailing and machine send shapes through the same facade; source manifest, generated ZIP, explicit MD, Markdown-body and nonmatching control variants. |
| V-7 | Real withheld operator/API-error/silence cases and proactive/digest/incident/alert control callers with healthy matched companions. |
| V-8 | Source runtime finishes and accepts its next prompt while converter is held; fresh-connection no-open-transaction and null-stamp checks. The before-commit admission barrier already passed. |
| V-9 | Two independent dispatcher/pump triggers, distinct identity changes and target outcomes, unexpired/expired lease takeover with stale-owner fencing. Existing admission/lease samples do not cover the whole race matrix. |
| V-10 | Full internal-purpose side-effect and privilege suppression, normal Custom/Distill/Check/Diagnose companions, composed instruction/env/capability sentinels and ordinary transcript/usage. |
| V-11 | Refusal categories, per-converter/global capacity, queued-selection and pre-dispatch expiry, Working-owner CAS race, text-only fallback. Deadline equality and one late-result sample already passed. |
| V-12 | Complete unchanged/PDF/PNG/TXT result, replacement text, two-delivery isolation, late-edit/delete sealed-byte send, and prose/marker exclusion. |
| V-13 | Remaining I/O, link-swap, ZIP/path, serialization and fallback faults with exact boundary and no arbitrary source read. Partial staging and several malicious-manifest rows already passed. |
| V-14 | Full T1/T2 frozen body/route/metadata/order after restart, other-conversation/control bypass, and held/uncertain-head resolution. |
| V-15 | Fresh-connection task/intent/hash/ownership/correlation/acceptance/stamp/attention assertions for every C-1–C-8 process-death cut. The cuts ran, but not every oracle did. |
| V-16 | Blocked producer, three definite retries, uncertain outcome, atomic stamps, partial four-source/incomplete ZIP, and multi-target acceptance/retry outcomes. |
| V-17 | Old correlation with live conversion through matching/TTL/late-confirm, beyond-budget fallback, and truthful Held/Failed/PublishUncertain attention. |
| V-18 | Revocation/rebinding at each launch/publish boundary, same-name prompt revision, new-message policy, and final validation/claim race ordering. |
| V-21 | Private-schema migration/history, fresh DI and structural dependency/instruction contract as one named result. The legacy warning and no-render samples passed. |
| V-22 | Full existing transport/monitor configuration/deploy control assertions, especially unknown-observation versus committed-old-lag behavior. |
| V-23 | Complete original/converted/fallback serialized limits, near-default publication through broker with topic/key, and terminal over-cap attention. A near-default serialization-only sample passed. |
| R-1, R-2 | V-1/V-2/V-3/V-16 completion-note, provenance, legacy history, source-completeness and renderer-absence assertions above. |
| R-3, R-4, R-5, R-6 | V-5–V-9/V-14–V-16/V-18 policy, all caller shapes, durable runtime release, competing triggers and recovery outcomes above. |
| R-7, R-8, R-9 | V-10–V-13/V-23 purpose/refusal/ownership, untrusted I/O, sealed bytes and full serialized fallback above. |
| R-10, R-11 | V-14/V-16/V-17 frozen route, order, multi-target, TTL and attention above. |
| R-12, R-13 | V-21/V-22/V-23 remaining structural, transport/monitor and near-default broker assertions above. V-19's renderer/CLI matrix has Linux and Windows process-cleanup evidence, and V-20's real parsed/inspected PDF passed. |
| R-14 | Actual-run evidence index and validator invocation require every claimed sub-assertion to have named native evidence. The current helper accepts one method per broad ID, so running it as a completed V/R index would overclaim these gaps. |

V-19 has native CLI/renderer failure, argument and cleanup evidence in CP-9 on Linux and Windows, including the Windows process-tree and staged-HTML checks from round 15. V-20 is closed by the independent browser/PDF evidence in round 7 and CP-10 on the round-16 tip. V-24's isolated inbound X/Y/Z/fallback run passed in round 14; it is local fake-destination evidence. V-25 needs its later authorized live receipt. PC-1–PC-30 stay pending for method-scoped SourceLanding Mutation. No checkpoint or local guard-red test discharges them.

## CP-1 through CP-13 Final sweep

The checkpoint tool ran the plan's closed list on the committed source slice in `.antiphon/checkpoints/20260928-222417-722e/` (Debian 12, 36m55s). The native report, TRX, CP-8 failure detail, and host/slot records are there. Every row held a granted host slot; the tool ran no unlisted row command. The separately leased checkpoint-tool bootstrap build above is the only build outside the list.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3484 | 3484 | 0 | 33 | Green; Linux platform skips |
| CP-2 source settlement | 344 | 344 | 0 | 0 | Green, including the new cross-root ZIP case |
| CP-3 policy/schema | 30 | 30 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited `PinnedAgentKindTests.T1` and `T2` `codex_desktop_unqualified` refusals, exactly the prior rounds' pair |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green on Linux; Windows descendant checks remain the round-15 evidence |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green on Linux |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green with `ANTIPHON_BROKER_TESTS=1` |
| CP-12 client | 26 | 26 | 0 | n/a | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

Overall tool exit 1: twelve green rows and CP-8 red with two inherited failures. The sweep does not close the open V/R assertions above, V-25, or any PC. Do not land from this evidence.
