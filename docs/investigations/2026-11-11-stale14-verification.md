# Stale-branch close check, 14 cards (2026-11-11)

Adversarial read of the 14 STALE-ABANDON cards from task 991aa3f4 (`docs/investigations/2026-11-11-unlanded-landing-queue.md` on `origin/feat/card-task-991aa3f4`). Patch-equivalence of a branch tip is not acceptance. Compared with `origin/master` `bc71a95e7710532a9a68416f1d7b9e158fe96a15`.

A commit counts as on master when `git merge-base --is-ancestor` succeeds, or when `git cherry -v origin/master <sha>` marks that commit `-` (same patch-id already on master). Card text is `GET /api/cards/CARD-nnnn` plus `/revisions`. Reviews are `GET /api/stage-outcomes?cardId=&latestOnly=false`, stage Review, plus the review task's result text. Open tasks are `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&status=Queued|Dispatched|Working|Blocked` (7 open rows; none of these 14 identifiers). No card is `NeedsDecision`. No card has an open blocker.

CLOSE means all of: every named slice has an implementing commit on `origin/master`; the approving review is Clean, Final, and Full, with `reviewedSourceClean=true`, at a SHA that is an ancestor or patch-id match; no later Found review; no open Debug/Review/Code/Mutation task and no pending operator decision; leftovers are only mutation controls or Windows reruns the card text does not require for Done.

Verdicts: CLOSE 2 (CARD-1376, CARD-1528). KEEP-OPEN 12.

| Card | Verdict | Per-slice SHA on origin/master | Review task + SHA + scope | Reason |
|---|---|---|---|---|
| CARD-0931 | KEEP-OPEN | Tool refusal only: `bcac67706cd0decb8b79c214ba05e07e589c016e` (patch-id of `51bd0b047`). Flaky-plan CP-3/CP-15 literals unchanged. | No Review row. Merge `f3492a34` recorded Rebase/Verify/Cleanup only. | Card ask still includes revising CP-3/CP-15 and a Linux+Windows requalify. Those rows are still bare operands. |
| CARD-1351 | KEEP-OPEN | v5 bundle: `2d61d1358ac6b1faa692b2e569b78049b1c85453` (ancestor; patch-id of `34083e79c02b`). | `ab4e2125` Clean, round Final, scope Unknown, `reviewedSourceClean` null, SHA unbound. Prose names `34083e79`. | Unknown scope and an unbound SHA do not approve. Card still says human merge/reject, then restart the distiller seat. |
| CARD-1532 | KEEP-OPEN | Stream digest `9a044e401404834260a8c44834bf771a3ed2a399`; PowerShell parity `82f027c50ddacacbdbb577013c843f1e5ef5921c`; digest context `6b3e7630d2a2bde2aa60eb9344b35cd0a1c18f30`; red witnesses `f788ef0b4e48f6ee07b40b31aba6000d1e485968`. | `f5885ccd` Clean Final Full `reviewedSourceClean=true` at `7932e60205a3` (patch-id `-`; master doc `617e1d5db0e8`). Earlier `2e4feb7c` same verdict at `7235182cb61e`. No later Found. | S3 CP-9 / V-10 Windows PowerShell 5.1 parser was not run. The Clean review and the plan call that missing row an incomplete gate. |
| CARD-1553 | KEEP-OPEN | Five C1115 methods changed in `a705977dccb1bc11d04bdcd9f25a908a81ffe1b4` (ancestor). | No Review row. | No Final review of the repair. |
| CARD-1548 | KEEP-OPEN | Fixture repair `29f1f908bf0a06c7a2c9d604e9938e8ffce7ad0d` (ancestor) touches the three named test files. | No Review row. Merge `679be8d0` recorded Rebase/Verify/Cleanup only. | No Final review. |
| CARD-1040 | KEEP-OPEN | S1 docs only. Newest master subject is still `90b786720daf78dbda09dea4b5810c08159d1c8a` "S2 V/R pending". | `b227ba87` Clean Final Full `reviewedSourceClean=false` at `11d9121e3899` (patch-id `-`). | Review text: this does not qualify jq and the SHA has no passing receipt. S2 CP-1..CP-3 were not run. |
| CARD-1510 | KEEP-OPEN | S1 red `5c96c0b2f94dcf28289a40e422ec899c191991ef`; S2 resolver `6d91a99e108f310ed7e6603c3887011b15d189fe` (ancestor; patch-id of reviewed `705662142c95`). | `269a3142` Clean Final Full `reviewedSourceClean=true` at `705662142c95`. No later Found. | Card text requires the original method validated on Windows. Review: CP-5 and CP-6 stay owed. |
| CARD-0972 | KEEP-OPEN | Diagnosis doc only: `a63cd468edab38918389b7197480f4303b7c0d34`. No Windows native-row commit and no SourceLanding PC commit. | No Review row. | Both work items in the title are still the card. A later Verify Found (`8423cb1a`, abandoned qualification) is not a Clean approval. |
| CARD-1368 | KEEP-OPEN | Host-exit adoption `a323cc6d03828dd84eae8a4e208ef02df17749aa`; 1 GiB stamp `7c20ea9eb98a35febc2f915cfd15203ec6d333ee`. | `b874a8b3` Clean Final Full `reviewedSourceClean=true` at `359cc912235d` (patch-id `-`). No later Found. | Committed Code evidence: W-1 guarded red unproved; CP-8..CP-13 not run. Review text: the strict certificate rejects CP-3. |
| CARD-1376 | CLOSE | Create-wait cancellation `d05cfddb3ca85b9519c4e3ad26e68a58307dcb9b`; late dispose `d31fde8c59a15c7f1c07976a4b255ad04a0b2d2e` (ancestor; patch-id of `9b97b7aebae4`). | `4b18ab15` Clean Final Full `reviewedSourceClean=true` at `9b97b7aebae4`. Earlier Found `d4501974` at `0e969cfe946a` (04:41Z) is before this Clean (05:10Z). | CP-10 and CP-8 receipts accepted. `Playwright.CreateAsync()` has no token parameter; the token reaches the wait through `CreatedResource.UseAsync`. Mutation is after Done in the plan, not a card condition. |
| CARD-1528 | CLOSE | Witnesses `1ee7bb44957ca88512bf913e7c7614b10dea6e6f`; static-call refusal `2bed847522cacd78a23ccfe93d6039dcb043c5e2`; removal preservation `4f9717413f0013d156705285ad9951cdea604586`. | Later `51c10e90` Clean Final Full `reviewedSourceClean=true` at `33f51ccb37a8` (patch-id `-`), own CP-1..CP-6 green 9/2/9/2/3/1. Earlier `7a42a520` same verdict at `1a30f3bebf5e`, 26/26. No later Found. | V-1 direct `File.Exists`, V-2 metadata-projected path, and V-3 metadata-only `ReadAllText` are the card's three negatives and are in that green CP-1. Pending PCs are not a Done condition of the card text. |
| CARD-1639 | KEEP-OPEN | Doc drift only: `a2b7534276211c9ef9859a512300880a79847564` (ancestor; patch-id of `b032722199a0`). | `48e15e43` Clean Final Full `reviewedSourceClean=true` at `b032722199a0`. | Slice (1), the equal-tree receipt skip, has no implementing commit. `git grep` finds no equal-tree skip under `server/`. |
| CARD-1715 | KEEP-OPEN | Honest refusal code: `a77bae94d1ff23728270a44181faa6752484617b` (ancestor; patch-id of `e64c5cd801c7`). | `d67a21b1` Clean, round Final, scope Interim, `reviewedSourceClean=false` at `e64c5cd801c7`. | Interim scope does not approve. Stderr buckets, attention, and the path-length pin are still named by the card and are outside the landed slice. Later Code `555da802` Failed. |
| CARD-1728 | KEEP-OPEN | Measurement doc `c7216a06f55fba7dc255702c99217248e1b4a20e` records no TRX. Scope wording `1d0d4f9f3b9fd7813927c5d81e9271b91c34898f` (ancestor; patch-id of `5acde1e2b7ad`). | `3d2eb2ca` Clean Final Full `reviewedSourceClean=true` at `5acde1e2b7ad`. Earlier Found `f31c70a6` at `ecfb032da692` is before it. | The card requires a complete Unit receipt and says a missing TRX is not credit. The Clean review approved a SCOPE-line edit. |

## Per card

### CARD-0931 KEEP-OPEN

Description ask: revise flaky-fix CP-3/CP-15 (trailing `*` or whole-class), requalify Linux and Windows, and consider a tool refusal. The tool refusal is on master (`docs/testing-and-build.md:606-609`, commit `bcac67706cd0`). The flaky plan's CP-3 and CP-15 filters are still bare method operands (`docs/superpowers/plans/2026-10-02-flaky-test-fixes-plan.md:396` and `:408`). The CARD-0931 plan itself says not to claim that requalification (`docs/superpowers/plans/2026-10-23-card-0931-or-filter-validation-plan.md:157`). Branch `feat/card-task-4963339c` tip `c2420e0336c7` is cherry `+0 -4`. No Review stage-outcome exists.

### CARD-1351 KEEP-OPEN

The v5 contract change is on master (`server/Application/Services/OutputDistillation.cs` `ContractVersion = "5"`, commit `2d61d1358ac6`). Review task `ab4e2125-c93b-42a2-a19d-8ff742868c8b` result says Final and clean at `34083e79c02b`, and that SHA is patch-id `-`. The stored stage row is scope Unknown, `reviewedSourceClean` null, and `reviewedSourceSha` empty. Unknown scope cannot approve. The card text still says it needs a human merge/reject and a distiller seat restart after merge.

### CARD-1532 KEEP-OPEN

Streaming stamps and the Linux CP-3..CP-8 receipt (32 passed) are on master, and both reviews are Clean Final Full with `reviewedSourceClean=true`. Review `f5885ccd` result: "CP-9/V-10 on Windows PowerShell 5.1 Desktop remains owed." The plan says a missing 5.1 parse is an incomplete gate (`docs/superpowers/plans/2026-10-23-card-1532-streaming-source-stamps-plan.md:121` and `:271`; evidence `docs/superpowers/evidence/2026-10-23-card-1532-recut-evidence.md:107`). That row is S3, not a post-Done rerun. Mutation variants also remain pending; those alone would not block.

### CARD-1553 KEEP-OPEN

`a705977dccb1` changes `RepairSourceDispatchTests.cs` (+79/-7). The five named methods still hold an independent fetch blocker and expect `repository_lease_busy` (`tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs:379-387`). That matches the card's requested repair shape. There is no Review stage-outcome, so it cannot close.

### CARD-1548 KEEP-OPEN

`29f1f908bf0a` changes `AgentTaskLandReceiptWatermarkTests.cs`, `ChannelInboundRecoveryTests.cs`, and `SessionStateWarmupTests.cs`, the three files the card names. No Review stage-outcome.

### CARD-1040 KEEP-OPEN

The plan's S2 is image qualification plus the fifteen consumer results. The newest master commit that names the card, `90b786720daf`, still says "S2 V/R pending". `docs/investigations/2026-10-04-card-1040-s1-jq-admission.md:140-142` leaves CP-1, CP-2, and CP-3 unrun. Review `b227ba87` recorded `reviewedSourceClean=false`, and its result says an evidence-backed land cannot use that review because `11d9121e3899` has no passing receipt.

### CARD-1510 KEEP-OPEN

Portable S1/S2 are on master. Review `269a3142` result: CP-2, CP-4, and CP-7 passed at `705662142c95`; "Windows CP-5 and CP-6 stay owed." The card text says to validate the exact group-5 method on Windows. The plan marks V-3 red/green as owed Windows and not Linux coverage (`docs/superpowers/plans/2026-10-23-card-1510-path-probe-plan.md:24` and `:59`). That owed run is a Done condition, so it is not waived.

### CARD-0972 KEEP-OPEN

The card's three items are the Windows `AgentTaskLandHalfResetWindowsTests` row (never executed, per the card), method-scoped SourceLanding mutation controls, and a frame-cap note. The branch tip `4117d4846703` adds only `docs/investigations/2026-10-23-rc-5a126f6fc-qualification-reds-debug.md`, landed as `a63cd468edab`. No Review row.

### CARD-1368 KEEP-OPEN

Adoption and the 1 GiB content stamp are on master. Review `b874a8b3` says a fresh CP-1..CP-7 run is green and that CP-3's four Windows skips make the strict certificate reject the row. `docs/superpowers/evidence/2026-10-19-card-1368-code.md:70` says no Clean/Full receipt was claimed; `:111` says the W-1 guarded red is unproved; `:137` defers CP-8..CP-13. The card asked for one red-first witness. That red is not established.

### CARD-1376 CLOSE

`tests/Antiphon.E2E/ChromiumRuntimeSmokeTests.cs:14` accepts the TUnit token. `:19` and `:46` pass it into `CreatedResource.UseAsync`, which waits on `Playwright.CreateAsync()` and disposes a late success (`tests/Antiphon.E2E/Fixtures/CreatedResource.cs:11-31`). `Playwright.CreateAsync()` takes no cancellation token. Review `4b18ab15-2171-4129-aefc-302bf3ee31ff` is Clean Final Full at `9b97b7aebae4`, patch-id `-`, and says CP-10 and CP-8 validate. The Found review `d4501974` is earlier. No open bound task. The plan's mutation note is after ordinary review, and the card text does not make positive controls a condition of Done.

### CARD-1528 CLOSE

The card names three negatives: a direct `System.IO.File.Exists` call, a metadata-projected path, and a read stored only in metadata. Those are plan V-1, V-2, and V-3 (`docs/superpowers/plans/2026-10-23-card-1528-land-audit-filesystem-metadata-plan.md:157-159`), covered by CP-1. Review `7a42a520` reports receipt `20261023-070333-3ba1` green 26/26 at `1a30f3bebf5e`. The later review `51c10e90` reports the re-cut's own CP-1..CP-6 green (9/2/9/2/3/1) at `33f51ccb37a8`, which is patch-id `-`. No later Found. Pending PC-1..PC-6 are SourceLanding work; the card description does not make them Done conditions.

### CARD-1639 KEEP-OPEN

The card lists four slices and says each is human-decided. (3) is explicitly other cards. (2) is "keep the allowlist." (4) the lease-doc correction is `a2b753427621` and `docs/orchestration-loop.md:2368`. (1) the equal-tree verification-build skip has no server commit. Review `48e15e43` approved only the doc commit.

### CARD-1715 KEEP-OPEN

Landed behavior reports `status_untrusted` instead of `adopt_local_changed` and appends a fixed explanation (`server/Application/Services/AgentTaskLandSourceResolver.cs:369`, `:511`, `:675-676`, commit `a77bae94d1ff`). The card also asks for a stable stderr bucket, an attention item when the bucket repeats, a non-English warning pin, and a path-length pin. The plan puts buckets, alerting, and the path walk outside the slice (`docs/superpowers/plans/2026-11-05-card-1715-status-untrusted.md:7`). The only review completed Interim scope with `reviewedSourceClean=false`. Code task `555da802-d558-4c26-9e38-49d072636603` then Failed.

### CARD-1728 KEEP-OPEN

The card says required Final Unit verification is incomplete and a missing TRX is not credit. `c7216a06f55f` records the A-L shard hitting the 45-minute cap with no TRX. The Clean review `3d2eb2ca` approved `5acde1e2b7ad` / master `1d0d4f9f3b9f`, which changes the Code SCOPE line so the Unit lane is nightly/rc. That is not the receipt.

## Not done, noted

No card was moved, closed, or edited. Close candidates are listed in `docs/investigations/2026-11-11-stale14-verified-close.txt`.
