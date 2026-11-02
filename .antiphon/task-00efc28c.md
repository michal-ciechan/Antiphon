# CARD-1610 S4a Code report (task 00efc28c)

Outcome: S4a is implemented red-first. All S4a witnesses are green: 33 script, 22 server and 7 client
tests. The closed checkpoint table is green except one CP-8 case,
`DelegateScriptBindDiagnosisTests.a_bind_400_with_an_unknown_served_build_is_diagnosed`. That case is
pre-existing: the script output is byte-identical at base and at HEAD (see "Inherited red").

- Branch: `feat/card-task-00efc28c`. Worktree: `/work/worktrees/task-00efc28c` (runner mirror).
- Landing owner: Code task 00efc28c.
- Base: `5376ac529987b9e7b2019773cb4d784cba8007bf` (master, CARD-0824 S1 landed).
- Plan: `docs/superpowers/plans/2026-11-02-card-1610-s4a-cli-client-plan.md`.
- Test design (checkpoint table): `docs/superpowers/plans/2026-11-02-card-1610-S4a-test-design.md`.
- Both docs were cherry-picked from `a7fa13954` and `2f1a9d762`.

## Commits (fast-forward from base)

| SHA | Group | Content |
|---|---|---|
| 8b4c863406 / 12cdc7fc07 / 24acad3633 / 9fe9f9099d | docs | S4a plan and test design, cherry-picked |
| 0e7c5f2d73579dd3c2a061396375de42f517ca84 | S4a-pin | V-19 pin capture (`UNPINNED`) |
| fbdc20858437e7e56d1f79753000c294b2684cd9 | S4a-red | pin set to base digest; DTO, client scaffold; 3 witness files; roster and allowlist |
| c93f05d457811671a6c02cc9c23c330a2e99ad52 | S4a-red | client test status literal `Canceled` (fixes the CP-3 typecheck) |
| 603a4db485427ad512295b1d90c57ddef3e74822 | S4a | green: server, delegate.ps1, client card, ops-http.md |

Tested source SHA for the green group: `603a4db485427ad512295b1d90c57ddef3e74822`. The report commit
adds only this file.

## What S4a delivered

**Server**

- `AgentTaskDtos.cs`: only `partial` on three records. R-11: exactly 3 changed lines.
- New `AgentTaskDtos.Ceiling.cs`:
  - `CeilingMinutes` (`long?`) and `CeilingReason`, both `WhenWritingNull`.
  - `AgentTaskCreatedDto.RequestedCeilingMinutes`.
  - `AgentTaskDetailDto.CeilingBudget`.
  - `CeilingBudgetDto`.
- `CeilingHoldFacts`: `ValidateCreate`, `BindRequested` and `Budget`. `ValidateCreate` applies the S2
  rule unchanged, then refuses two more cases as 422 `ceilingReason`: a reason without minutes, and
  control characters (Q-1).
- `AgentTaskService`:
  - validation right after the ExpectedMinutes check;
  - `BindRequested` after the `storedPolicy` block, outside the CARD-0824 initializer;
  - created echo on both `AgentTaskCreatedDto` constructions;
  - `CeilingBudget` on detail.
- `BuildBrief` emits the `Declared ceiling: ...` line only when a ceiling was requested, after the
  repair block and outside it (Q-8).
- No entity, model, migration, reply-service, endpoint, dispatcher, `SessionRunnerRuntime` or
  test-execution-policy change.

**delegate.ps1** (ASCII; parses; no `??` or `?.` added)

- Create: `-CeilingMinutes` (a string parsed with `[long]::TryParse`, Q-2) and `-CeilingReason`.
  - Every invalid arm prints `ceiling_invalid:` and exits 2 before any request.
  - The create echo prints `ceiling: N min requested`.
  - If the response does not echo the value, it prints `WARNING: ceiling_unconfirmed: ... check
    GET /api/version` and exits 0 (H-4).
- Status: D-7 lines after `Delegate:`, each printed only from its server field (Q-4), in this order:
  - `Ceiling` and `Ceiling reason`;
  - `Ceiling hold`, `Phase`, then `Decide` or the final-hold line;
  - `Seat`, then the Retry consequence (seat held only).
- Reply, `-CeilingAction`, `-EpisodeId`, `-ExtendMinutes` and `-OperatorExtend`:
  - Sends the exclusive typed body, with no `origin`.
  - `-OperatorExtend` dot-sources `lib/runner-operator-token.ps1`. It sends `X-Antiphon-Operator-Token`
    on a header copy for that one request only, and never prints the token.
  - A token failure prints `operator_token_unavailable:` and exits 2 before any request.
- `Invoke-Antiphon` gained `-ExtraHeaders` and `-PlainFailure`, used only by the typed reply.
  `-PlainFailure` prints one unwrapped stderr line, so `ceiling_episode_stale` and its detail stay
  greppable. `Write-Error` wraps them on this host.

**Client**

- `useCeilingDecision` posts `{ceilingAction, episodeId[, extendMinutes]}` only.
- `CeilingHoldCard` (`data-testid="ceiling-hold-card"`):
  - question, next bound, and phase or no-phase guidance;
  - Continue; Extend with a 1..720 whole-minute input; Cancel through `useCancelAgentTask`;
  - a cap-reached hold disables Continue and Extend and shows operator CLI guidance, with no operator
    control;
  - a 409 shows the server detail and invalidates the detail query.
- `TaskDetailBody` renders the card.

**Docs**: `docs/ops-http.md` gets a new section at the end of the file.

## Checkpoints (checkpoint tool, plan = test-design doc)

| Run | SHA | Rows | Result |
|---|---|---|---|
| P `20261102-142524-4f50` | 0e7c5f2d | CP-0 | intended red: base digest `6ED139471AC87C4438E95882106A33CEB01C0CDA19099C7ECFF9E8F8C682405A` |
| R `20261102-143549-5989` | fbdc2085 | CP-1, CP-2, CP-3 | CP-1 and CP-2 intended red; CP-3 exit 2 (typecheck: test used `'Cancelled'`) |
| R' `20261102-150851-52ab` (detached worktree) | c93f05d4 | CP-3 | exit 1 at **lint** (scaffold `_props` and `_decision` unused), so vitest never started |
| G `20261102-150937-c03e` | 603a4db4 | S4a | superseded: build `refused: unreadable` (hard-linked `client/node_modules/**/esbuild` from run R's install); 0 .NET rows executed |
| G2 `20261102-152955-33dc` | 603a4db4 | S4a (14 rows) | 13 green, CP-8 78/79 (pre-existing case) |

Unedited CHECKPOINT lines (paths shortened only in this table above; the lines below are verbatim
prefixes from `report.md`):

```
CHECKPOINT CP-0 commit=0e7c5f2d73579dd3c2a061396375de42f517ca84 build=ok filter=/*/*/TaskCeilingCreateBindingTests/C1610_Absent_ceiling_keeps_handoff_digest executed=1 passed=0 failed=1 skipped=0 ... slot=granted waited=120s dirty=0 source=0e7c5f2d73579dd3c2a061396375de42f517ca84 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=fbdc20858437e7e56d1f79753000c294b2684cd9 build=ok filter=/*/*/TaskCeilingScriptTests/C1610_* executed=33 passed=2 failed=31 skipped=0 ... slot=granted waited=600s dirty=0 source=fbdc2085... sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=fbdc20858437e7e56d1f79753000c294b2684cd9 build=reused filter=/*/*/TaskCeilingCreateBindingTests/C1610_* executed=22 passed=4 failed=18 skipped=0 ... slot=granted waited=645s dirty=0 ... sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=fbdc20858437e7e56d1f79753000c294b2684cd9 build=n/a ... exit=2 slot=granted waited=615s
CHECKPOINT CP-3 commit=c93f05d457811671a6c02cc9c23c330a2e99ad52 build=n/a ... exit=1 slot=granted waited=405s
CHECKPOINT CP-4 commit=603a4db485427ad512295b1d90c57ddef3e74822 build=ok filter=/*/*/TaskCeilingScriptTests/C1610_* executed=33 passed=33 failed=0 skipped=0 ... slot=granted waited=285s dirty=0 ... sourceState=clean buildSource=verified
CHECKPOINT CP-5 ... build=reused filter=/*/*/TaskCeilingCreateBindingTests/C1610_* executed=22 passed=22 failed=0 skipped=0 ... slot=granted waited=330s ... sourceState=clean buildSource=verified
CHECKPOINT CP-6 ... filter=/*/*/TaskCeilingPolicyTests/* executed=12 passed=12 failed=0 skipped=0 ... slot=granted waited=300s
CHECKPOINT CP-7 ... filter=/*/*/(TaskCeilingHoldTests*)|(TaskCeilingControlTests*)/* executed=49 passed=49 failed=0 skipped=0 ... slot=granted waited=255s
CHECKPOINT CP-8 ... filter=/*/*/(DelegateScriptKindTests*)|...|(DelegateScriptBindDiagnosisTests*)/* executed=79 passed=78 failed=1 skipped=0 ... slot=granted waited=540s
CHECKPOINT CP-9 ... executed=26 passed=26 failed=0 skipped=0 ... slot=granted waited=450s
CHECKPOINT CP-10 ... executed=2 passed=2 failed=0 skipped=0 ... slot=granted waited=150s
CHECKPOINT CP-11 ... executed=55 passed=55 failed=0 skipped=0 ... slot=granted waited=135s
CHECKPOINT CP-12 ... executed=1 passed=1 failed=0 skipped=0 ... slot=granted waited=210s
CHECKPOINT CP-13 ... executed=22 passed=22 failed=0 skipped=0 ... slot=granted waited=225s
CHECKPOINT CP-14 ... executed=1 passed=1 failed=0 skipped=0 ... slot=granted waited=450s
CHECKPOINT CP-15 ... exit=0 slot=granted waited=780s ... sourceState=clean   (typecheck 0, lint 0, tests 0; fresh JSON 7/7, failed/pending/todo 0)
CHECKPOINT CP-16 ... exit=0 slot=granted waited=855s ... sourceState=clean   (fresh JSON 12/12, failed/pending/todo 0)
CHECKPOINT CP-17 ... executed=1 passed=1 failed=0 skipped=0 ... slot=granted waited=375s
```

Every executed floor equals the design floor (33, 22, 12, 49, 79, 26, 2, 55, 1, 22, 1, 7, 12, 1). No
floor was amended. G2 budget: wall 50m42s, of which 36 min was slot admission. Execution took 14m17s.

**Receipt:** G2 has verdict RED (CP-8). The checkpoint tool kept `bin-c1610-s4a/` and its receipt
cannot validate, so there is no SHA-validated clean receipt for 603a4db4. I removed the kept output
by hand. A validated receipt needs a rerun of the S4a rows with CP-8 narrowed to exclude the
pre-existing method, either through a plan amendment or by Review.

### Red evidence (per case)

- **CP-1:** 31 intended reds and 2 guard greens (`C1610_Status(legacy-detail)`,
  `C1610_Declared_budget(omitted)`). The reds fail on the oracle text (binding error, no
  `ceiling_invalid:`) or on missing D-7 lines. Per-case pwsh duration: p50 0.8 s, p95 2.4 s, max 2.8 s
  (Q-6: far below the shared 60 s; no card needed).
- **CP-2:** 18 intended reds and 4 guard greens (V-17 default, V-18 default, V-19, V-20 brief-default).
- **CP-3 behavioural red:** an **unlisted** direct vitest run at c93f05d4 in the detached worktree.
  - Command: `scripts/build-slot.ps1 -Label c1610-cp3-red-vitest -- npx vitest run CeilingHold.test.tsx`,
    slot granted after waiting 556 s.
  - Result: 7 total, 5 failed (`Unable to find [data-testid="ceiling-hold-card"]`), 2 passed (V-14e
    guard greens). Pending 0, todo 0.
  - Reason: the listed CP-3 command always lints first, and the scaffold's unused parameters fail
    lint. So the row as written can never reach vitest on a compatible scaffold. This is a
    test-design defect, recorded for the next design.

## Inherited red: CP-8 `a_bind_400_with_an_unknown_served_build_is_diagnosed`

- The test asserts `could not be converted` in the output. pwsh's ConciseView `Write-Error` wraps the
  message on this host as `...could not be` / `converted to ...`.
- I reproduced it with a node stub (400 with the incident body, plus a version JSON). The output of
  base `5376ac529:scripts/delegate.ps1` and of HEAD's script are identical, except the script path and
  line number (`1214` vs `1405`).
- The S4a Create path is unchanged for this request; `-PlainFailure` is not used on Create.
- A test-level base confirmation (build and run at 5376ac529 in the detached worktree) was started
  but had not finished when this report was written. See the final message for its outcome.

## Ordinary V/R

- V-13a, V-13b, V-13c: CP-4 33/33.
- V-15 to V-21: CP-5 22/22.
- V-14a to V-14e: CP-15 7/7.
- R-1: CP-11 55/55.
- R-2: CP-10 2/2.
- R-3: CP-8 78/79 (one pre-existing case).
- R-4: CP-7 49/49.
- R-5: CP-6 12/12.
- R-6: CP-14 1/1.
- R-7: CP-13 22/22.
- R-8: CP-16 12/12.
- R-9: CP-9 26/26.
- R-10: CP-12 1/1.
- R-11:
  - the `AgentTaskDtos.cs` diff is exactly 3 `partial` lines;
  - `git diff --exit-code` over AgentTask.cs, AppDbContext.cs, Migrations, AgentTaskReplyService*,
    deploy-server2.ps1 and docker-stack.md is empty;
  - no AgentTaskEndpoints, SessionRunnerRuntime or test-execution-policy change.
- R-12: CP-17 1/1, and no `??` or `?.` in the added delegate.ps1 lines.
- Manual smoke (pwsh, no server): every local refusal arm exits 2 with its text. Operator Extend with
  a missing token file, or with `ANTIPHON_OPERATOR_TOKEN_FILE` unset on Linux, prints
  `operator_token_unavailable:` and exits 2. A readable token reaches the POST.

## Landability and policy

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 5376ac529 -HeadRef HEAD`: commits=8,
  violations=0, exit 0 (run at 603a4db4; rerun at the report tip in the final message).
- Land graph `-Check`: `approved`, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE,
  count 69. This matches the expected value. The check ran in a detached worktree at 603a4db4 with a
  build-slot Release build (native output) passed as `-ToolPath`. `-Update` was not run.
- `git merge-tree --write-tree origin/master HEAD`: clean (origin/master = base 5376ac529).
- `git merge-tree --write-tree origin/rc/2026-11-01 HEAD`: CONFLICT in `client/src/api/agentTasks.ts`
  only. Both hunks are adjacent insertions next to rc's own CeilingHoldDto comment, and the
  resolution keeps both sides. S4a is master-only (the plan names no rc need), so I made no rc port.
- `tests/test-execution-policy.json`: untouched, no rows needed (portable classes).

## Unlisted builds and runs

- Land-audit Release build and `-Check`, as the brief requires.
- The direct vitest red above.
- Base confirmation build and run of the CP-8 method, as git-common requires.
- Manual pwsh smoke runs of delegate.ps1, plus the bind-diagnosis reproduction against a node stub.
  These are script invocations, not build or test drivers.

## Open questions / deviations (defaults applied)

- Q-1 to Q-8: defaults applied. Q-7: `RetryAsync` via `DefaultRunnerKit` worked directly, without
  the harness route.
- Additional local rule: on a reply, `-CeilingReason` without `-OperatorExtend` is refused as
  `ceiling_invalid:`, because the server ignores it there. Ceiling-only parameters without
  `-CeilingAction` are refused too.
- `AgentTaskCreatedDto.RequestedCeilingMinutes` is serialized as null when unset, not omitted.
- CP-3's command cannot show a behavioural red on a lint-failing scaffold. The red evidence comes
  from the direct vitest run instead.

## PCs pending for Mutation

All 58 are pending (PC-1..PC-58, variant v1, binding-missing). Each needs a binding at the landed
source per the test design's Positive controls table.
