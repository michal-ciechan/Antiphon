# Backlog triage for six in flight

Snapshot `2026-10-17T09:32:56Z` from `GET /api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, plus a full `card.ps1 list -Board Antiphon -Status Backlog -All` (359 cards, `truncated: false`). Named rc tip `7b3b9adbe98b18ae306b73cfb220331dc433f801`. `origin/master` `96460bf7bbf3603f6a0935a736bc1e890128f3c1`. Neither is an ancestor of the other. Open blockers: pipeline `investigateBacklog` omitted `blocked` (zero held), and every candidate relationship read was `blockedBy: []`.

In flight then: Plan CARD-1320 (keep-6, excluded), Deploy CARD-1311 (user-owned, Deploy remaining 0), Investigate this triage. Absolute cap 6, open 3, remaining 3. After this task settles, two tasks remain, so four dispatches reach six. Investigate cap 3 (remaining 2 while this task is open). Plan remaining 2. Review remaining 5. Code remaining 5. TestDesign remaining 1. `server2` is draining (`acceptingNewWork: false`). Linux work goes to `server2-temp` (occupied 2 of 10). Desktop is the only Windows seat (occupied 1 of 2).

Rank is `13 - (3·importance + 2·effectiveUrgency)`, lower first. No Backlog card is High or Critical. The only Now card is CARD-0429 (operator publication review; not an agent task). Test-only cards are ranked below non-test work. Fresh Backlog order inside a rank is the server key (position, due, createdAt). Formal `ready` rows were checked against git before being treated as dispatchable.

## Top 8

Dispatch from the top. Do not start two cards that share an area. Only one Windows card. At most three Investigate tasks.

| # | Card | Next stage | Train | Windows | Area | Blockers | Goal |
|---|---|---|---|---|---|---|---|
| 1 | CARD-0242 Codex ingests no turn-abort signal — an interrupted delegate strands until the deadline | Investigate | master-only | no | `runner` (`src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs:44`, `:342`) | none | Investigate the missing Codex abort boundary; the normalizer still says Codex has no `stop_reason`. |
| 2 | CARD-1288 Closed-card task refusal scope vs agent-card-lifecycle.md (title/parent-bound) | Investigate | rc-fix | no | `delegation` (`server/Application/Services/AgentTaskService.cs:1651`) | none | Confirm whether a title-bound or parent-bound task on a Done card still escapes `card_implementation_closed`. The sentence the card cites at `docs/agent-card-lifecycle.md:59` no longer says those tasks are allowed. |
| 3 | CARD-1318 CARD-1310 follow-ups: Windows seal docs drift and custody hardening | Investigate | master-only (stored label; the body asks rc only for the doc half) | (b) and (c) only | `docs` plus `tools/Antiphon.Checkpoints` custody | none | Investigate the three follow-ups. (a) is still open: `docs/testing-and-build.md:466` says off Linux nothing is adopted, and `:491` says Linux only. |
| 4 | CARD-1305 RC 2026-10-15 environment and lane-placement defects D-2..D-6 | Investigate | rc-fix | no for D-2 | `ops` (`verify-dev-stack.ps1:79`) | none | Investigate D-2..D-6. D-2 still enumerates listeners with `Get-NetTCPConnection` on the rc tip and on master. |
| 5 | CARD-1290 D-2 verification-incomplete next-stage ambiguity; check-evidence-diff base handling | Investigate | rc-fix | no | `docs` plus `ops` (`scripts/check-evidence-diff.ps1`) | none | Define the next stage for "verification incomplete", and make a non-ancestor base fail clearly. Hold this while CARD-1305 is in flight (same `ops` area). |
| 6 | CARD-0757 ScaledTimeProviderTests flake under full-lane load | Investigate | master-only | no | tests (`ScaledTimeProviderTests`) | none | Confirm the 2026-10-02 `Speed_10` clock root cause still flakes, then hand to Plan. Demoted: rank 8 Soon, test-only. |
| 7 | CARD-1287 Full Unit lane never completes on Linux: run on Windows, attribute failures | Investigate | master-only | yes | `tests/Antiphon.Tests` Unit lane | none | Continue the ready handoff: qualify one Linux roster and one Windows roster at one SHA. Takes the desktop seat. Demoted: test-only. |
| 8 | CARD-0416 Prove the Grok rules refresh fires across two genuine auto-compactions | TestDesign | master-only | yes | Grok rules refresh / pty | none | Run the ready two-arm acceptance checkpoint on an isolated authenticated Windows host. Rank 13. Use this only if the desktop seat is free and CARD-1287 is not running; it is the only non-stale non-Investigate ready row, so it is what fills the sixth seat. |

rc-fix means label `rc-2026-10-15`: branch from `7b3b9adbe98b18ae306b73cfb220331dc433f801` and land on rc. master-only means that label is absent: land on master.

First wave that reaches six in flight without an area clash: 1, 2, 3, and 4 (three Investigate on server2-temp, one more Investigate only after this task exits). That is five in flight with CARD-1320 and CARD-1311. The sixth seat is CARD-0416 on the desktop, or CARD-1287 instead of CARD-0416 if the Unit lane outranks the compaction proof. Leave CARD-1290 and CARD-1306 until CARD-1305 leaves `ops`.

## Not dispatched

Already on the named rc tip and on master, so the formal ready row would repeat published work:

- CARD-1259 Review of `03838480d2`. `2fea6a8b1` ("CARD-1259 D-1") is an ancestor of both tips, and `SessionRuntimeActionCoordinator.cs` matches that review tip. Later CARD-1259 commits are on both tips.
- CARD-1286 Review of `829b0cd9f6`. The ops-http pointer sentence is already at `docs/ops-http.md:139` on both tips.
- CARD-1298 Review of `a16263459c`. `DelegateBranchDeliveryTests.cs` and `CheckpointTrackedBaselineTests.cs` match the rc tip and master.
- CARD-1296 Review of `e03508908b`, an ancestor of both tips. `D2AdvisoryDocumentationTests` is on master.
- CARD-1281 Plan for D-7..D-12. Later commits `7c580d7b5` and `e55f4bfcb` are on both tips.
- CARD-1292. `PipelineHandoffActionService.cs:122` already throws `handoff_recipient_denied` on both tips, and `Program.cs` maps the routes.

Blocked, excluded, or not a task:

- CARD-1010 Code stays ready, and S1 admission is not in `PhoneHomeContracts.cs`, but the frozen plan sequence still awaits CARD-1030 (In Progress). CARD-0980 and CARD-0983 are Done.
- CARD-1320 is the in-flight Plan. CARD-1313 S6-S7 stays held until the server2 rollout finishes. CARD-1316, CARD-1317, CARD-1319, and CARD-1310 are done.
- CARD-1311, CARD-1291, CARD-1031, and CARD-0907 are user-owned. CARD-1031 is also Review-ready; the live runner catalogue still reports `codexCliVersionError: stderr_output`, so the defect is not stale, but it is not ours to move.
- Mutation is paused. Do not dispatch ready Mutation on CARD-0698, CARD-0701, or CARD-0812.
- CARD-1263 is due 2026-10-21. CARD-0548's premise was corrected; it is not a bug. CARD-0429 is an operator publication review and does not authorize the work. CARD-0972 and CARD-0979 are SourceLanding mutation controls.
- CARD-1315 (`scripts/c590-remote.sh`) is the same rollout surface as the in-flight CARD-1311 deploy. Hold it with CARD-1313.
- CARD-0508 (Critical, rank 0) and CARD-1065 (Critical, Review column) have no ready stage. CARD-1149's latest recorded Review is Clean and it is not in `ready`.
- CARD-1306 is still red (`scripts/lib/nightly-coverage.ps1:189` matches `Passed` before `Error` on both tips) but it is test-infra and the same `ops` area as CARD-1305. Next after that area is free. CARD-1312 still defaults `scripts/test-run-checkpoint.ps1` to `bin-c585h/`; same hold.

Older rank-10 ideas (CARD-0105 and its neighbors) were not re-verified one by one. They sort ahead of October cards inside rank 10 and they are not defects with a ready stage.

## Not done, noted

No card was moved and no product code was changed.
