# CARD-1610 S4a Review round 1 (task e52b2bed)

Outcome: **3 defects found**. The core change works: the checkpoint rerun is green and validated
at 8bbad39c51625e88e0f5883136928b879e3ba3bb, the token is never printed, and the 422 is refused
before any write. Not landable yet:

- D-1: the E2E contract fixture was not re-captured.
- D-2: S4a's own local `-CeilingReason` reply refusal has no witness.
- D-3: no test covers the server-side operator Extend gate (operator credential and the 240-minute bound).

- Subject: `feat/card-task-00efc28c`. Code landing owner: task 00efc28c.
- Reviewed range: `5376ac529987b9e7b2019773cb4d784cba8007bf..8bbad39c51625e88e0f5883136928b879e3ba3bb`.
  Tested source is `603a4db485427ad512295b1d90c57ddef3e74822`. 8bbad39c5 adds only `.antiphon/task-00efc28c.md`.

## Defects

### D-1: contract fixture `agent-task-detail.json` not re-captured (e2e drift)

- **Where:** `server/Application/Dtos/AgentTaskDtos.Ceiling.cs:29-33` and `AgentTaskService.cs` detail
  `{ CeilingBudget = CeilingHoldFacts.Budget(task) }`. The detail now always carries `ceilingBudget`.
  `client/src/test/fixtures/contract/agent-task-detail.json` is unchanged.
- **Failure:** `ContractSnapshotTests.Delegated_task_board_and_drawer_contracts` fails at 603a4db4
  (E2E build-slot run, total 5, failed 2, succeeded 3). The failure message is
  `Backend contract for /api/agent-tasks/aaaaaaaa-0000-0000-0000-000000000004 drifted from agent-task-detail.json`.
  I re-captured the fixture in a scratch worktree: the only difference is the new top-level block
  `"ceilingBudget": {requestedMinutes:null, requestedReason:null, resolvedMinutes:null, resolvedSource:null, nextCeilingMinutes:null, absoluteCapMinutes:720}`.
  It sits on lines 2-9, before `summary`.
- **Not S4a:** the other failure, `Agent_files_review_threads_and_content_contracts`
  (session-transcript.json), is red on Linux by construction: the fixture holds Windows `\\` toolInput
  paths. The other three tests are green.
- **Why:** e2e group 1 goes red on the next qualification. The earlier CARD-1610 change failed for exactly this.
- **Fix:** delete the fixture and re-run `Delegated_task_board_and_drawer_contracts` to re-capture it.
  Check that the client stories and types accept `ceilingBudget`. Commit the fixture in this change.
  Add the E2E row to the plan's checkpoint table.

### D-2: the reply-side `-CeilingReason` refusal has no witness

- **Where:** `scripts/delegate.ps1:1054-1056`
  (`elseif ($PSBoundParameters.ContainsKey('CeilingReason')) { Exit-AntiphonCeilingInvalid '-CeilingReason on a reply applies only to -OperatorExtend.' }`).
  Also untested: the companion refusal at `:1062-1065`, ceiling parameters without `-CeilingAction`.
- **Failure:** in mutant M1 I replaced line 1055 with a comment. I ran
  `TaskCeilingScriptTests/(C1610_Ceiling_reply*)|(C1610_Declared_budget*)` (24 cases) in the same batch
  as M1b and M2 (below). Only the M1b and M2 arms went red; no `C1610_Ceiling_reply` arm passes
  `-CeilingReason` without `-OperatorExtend`. So `-Reply X -CeilingAction Extend -ExtendMinutes 60
  -CeilingReason r` could silently drop the reason and still pass every test.
- **Why:** this is the worker's added local rule and one of the three guards the brief asked to mutate.
- **Fix:** add a `C1610_Ceiling_reply` arm `reason-without-operator` (exit 2, `ceiling_invalid:`,
  0 requests). Optionally add an arm `ceiling-params-without-action`. Each must be red when its guard is removed.

### D-3: the server operator Extend gate has no witness

- **Where:** `server/Api/Endpoints/AgentTaskEndpoints.cs:245`
  (`OperatorCredential.Require(... "Operator Extend requires the operator credential.")`) and
  `server/Application/Services/CeilingHoldFacts.cs:81` (operator `minutes is not (>= 1 and <= 240)`).
- **Failure:** I applied mutants M4 (Require commented out) and M5 (240 changed to 100000) in one
  build together with M3, then ran `(TaskCeilingCreateBindingTests*)|(TaskCeilingPolicyTests*)|(TaskCeilingControlTests*)|(TaskCeilingHoldTests*)`:
  total 83, failed 2. Both failures belong to M3. M4 and M5 turned nothing red.
  `C1610_Final_cap_requires_operator_extend(unauthorized-operator)` only covers the service-level
  `authenticated:false` flag. No test posts `operatorExtend` over HTTP, and none sends an operator
  Extend over 240.
- **Origin:** the code is correct and pre-existing (S3, 641616d56), but S4a now exposes it through
  the CLI. The brief requires the operator-token mutant to go red.
- **Fix:** test-only.
  - An HTTP test of `POST /api/agent-tasks/{id}/reply` with `operatorExtend:true`: no or wrong
    `X-Antiphon-Operator-Token` gives 403 with no state change; the correct token gives 200.
  - A service-level operator Extend 241 gives 422 `extendMinutes`.

## Verification (all fresh, this task)

The checkpoint tool ran run `20261102-163438-22fb` at 8bbad39c5. The plan was an ignored review
amendment of the S4a design table, `.antiphon/c1610-s4a-review-plan.md`. It splits CP-8 into CP-8a
and CP-8b to exclude the one inherited method. The verdict was GREEN, 15/15 rows. `validate` printed
`CHECKPOINT SOURCE VALID source=8bbad39c5... rows=15`. Wall time was 31m43s, of which 17m11s was slot
admission.

| Row | Count | Row | Count |
|---|---|---|---|
| CP-4 script V-13 | 33/33 | CP-11 brief corpus R-1 | 55/55 |
| CP-5 create binding V-15..21 | 22/22 | CP-12 R-10 | 1/1 |
| CP-6 policy R-5 | 12/12 | CP-13 R-7 | 22/22 |
| CP-7 hold/control R-4 | 49/49 | CP-14 R-6 | 1/1 |
| CP-8a delegate contracts R-3 | 73/73 | CP-15 client V-14 + typecheck + lint | 7/7 (JSON), exit 0 |
| CP-8b bind diagnosis minus 1 | 5/5 | CP-16 detail regression R-8 | 12/12 (JSON), exit 0 |
| CP-9 guards R-9 | 26/26 | CP-17 ASCII R-12 | 1/1 |
| CP-10 R-2 | 2/2 | | |

```
CHECKPOINT CP-4 commit=8bbad39c51625e88e0f5883136928b879e3ba3bb build=ok filter=/*/*/TaskCeilingScriptTests/C1610_* executed=33 passed=33 failed=0 skipped=0 slot=granted waited=150s dirty=0 source=8bbad39c51625e88e0f5883136928b879e3ba3bb sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=8bbad39c51625e88e0f5883136928b879e3ba3bb build=reused filter=/*/*/TaskCeilingCreateBindingTests/C1610_* executed=22 passed=22 failed=0 skipped=0 slot=granted waited=195s dirty=0 source=8bbad39c51625e88e0f5883136928b879e3ba3bb sourceState=clean buildSource=verified
CHECKPOINT CP-8a commit=8bbad39c51625e88e0f5883136928b879e3ba3bb build=reused filter=/*/*/(DelegateScriptKindTests*)|(DelegateScriptStartRefTests*)|(DelegateScriptTitleTests*)|(DelegateScriptWorkspaceDefaultTests*)/* executed=73 passed=73 failed=0 skipped=0 slot=granted waited=180s dirty=0 source=8bbad39c51625e88e0f5883136928b879e3ba3bb sourceState=clean buildSource=verified
CHECKPOINT CP-8b commit=8bbad39c51625e88e0f5883136928b879e3ba3bb build=reused filter=/*/*/DelegateScriptBindDiagnosisTests/(a_422_is_reported*)|(a_bind_400_counts*)|(a_bind_400_with_head*)|(a_bind_400_on_another*)|(a_bind_400_with_version*) executed=5 passed=5 failed=0 skipped=0 slot=granted waited=180s dirty=0 source=8bbad39c51625e88e0f5883136928b879e3ba3bb sourceState=clean buildSource=verified
```
The full CHECKPOINT lines for all 15 rows are in
`.antiphon/checkpoints/20261102-163438-22fb/report.md`, which is gitignored on this mirror.

Unlisted runs, all build-slot gated, with their reasons:

- **E2E ContractSnapshotTests at 603a4db4** (brief item 3): a detached scratch worktree, build plus a
  class run, then one re-capture run of the drawer test.
- **CP-8 inherited method at base 5376ac529:** total 1, failed 1, with the same
  `should contain "could not be converted"` assertion. This confirms the red is inherited.
- **Mutant runs at 603a4db4** in the scratch worktree, all restored (`git status` clean, worktree removed):
  - M1 (reply reason refusal): no red, so D-2.
  - M1b (create reason-only refusal): `C1610_Declared_budget(reason-only)` red.
  - M2 (operator header dropped): `C1610_Ceiling_reply(operator-extend)` red.
  - M3 (`ValidateInitial` cap 720 changed to 100000): `C1610_Declared_budget_validation(over-cap)` and
    `C1610_Create_refuses_invalid_ceiling(over-cap)` red.
  - M4 and M5: no red, so D-3.
- **Land-graph evaluator Release build** for `-Check`. I deleted both `bin-land-audit` directories afterwards.

## Brief priorities

1. **Secrets: clean.**
   - The token is read in process from the owner-only file. It goes only into a per-request header
     copy passed to `Invoke-RestMethod -Headers`. It is never on a command line, in the environment,
     in a file, or in output.
   - Error paths print only the server's ErrorDetails or the exception message. A server 403 body
     does not echo headers. The token helper's errors carry the path only.
   - Status prints no token. The client has no operator path and stores nothing.
   - Every V-13 arm asserts that neither `SENTINEL` nor `SECRET` appears in the output.
2. **Authorisation and bounds: correct in code; D-3 is the missing test.**
   - The endpoint requires the operator credential only when `OperatorExtend` is set. Continue needs
     none, as designed.
   - The service enforces 1..720 and the 720-minute elapsed cap (`ceiling_cap_reached`), plus 1..240
     and a reason for operator Extend.
   - A worker can answer its own hold with an ordinary Continue/Extend: S2/S3 design, bounded by the 720 cap (A-5).
   - With no ceiling, behaviour is unchanged: CP-11 briefs are byte-identical (55/55), the V-19
     digest pin is green, and the CP-13 handoff replay is green.
3. **Input validation.**
   - 422 comes before any write: V-16 asserts 0 tasks and 0 events.
   - The brief gains one line: an integer, plus a reason with C0 control characters refused on both
     server and CLI.
   - The response shapes are additive. The detail fixture still needs re-capturing (D-1).
4. **Witnesses.** 62 green: 33 + 22 + 7. CP-8 is 78 green at the tip plus 1 inherited red. The
   mutants are listed above.
5. **Deviations acceptable.**
   - Q-1..Q-8 defaults, `-PlainFailure` and the extra local refusals are fine.
   - `requestedCeilingMinutes: null` on every create is additive, and the CLI treats null as unconfirmed.
6. **Landability.**
   - Land graph `-Check` approved: `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`.
     `-Update` was not run.
   - `check-evidence-diff`: commits=11, violations=0, exit 0.
   - merge-tree onto origin/master 1859a1273 is clean.
   - merge-tree onto rc/2026-11-01 10a826b9d conflicts only in `client/src/api/agentTasks.ts`, as
     expected; S4a is master-only.

## Advisories

- **A-1:** the reason rule refuses only C0 and DEL. C1 controls (including U+0085 NEL) and U+2028/U+2029
  pass into the brief line. Consider `char.IsControl` plus the two separators on both server and CLI.
  This is not an escalation: the goal is already free text.
- **A-2:** `ceilingBudget` serializes first in the detail JSON, before `summary`. This is cosmetic.
- **A-3:** the CP-3 design row can never reach vitest on a scaffold that fails lint. The worker
  recorded this; fix it in future designs.
- **A-4:** `DelegateScriptBindDiagnosisTests.a_bind_400_with_an_unknown_served_build_is_diagnosed` is
  red at base on Linux (ConciseView wrap). It needs its own card.
- **A-5:** see item 2 (self-answer within the cap).
- **A-6:** the client `HoldCard` keeps its minutes text across episodes. Consider a `key={episodeId}`.
