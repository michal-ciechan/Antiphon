# CARD-1521 continuation: blocked by incompatible branch ancestry requirements

The required re-cut cannot be performed under this dispatch's branch contract.
No product or test changes were made and no build/checkpoint was started. Ordinary
verification remains incomplete. This report and the recovered predecessor report
are the only committed changes; no target ref or other worktree was changed.

Task and assigned landing owner: `5f3b17ba-6a8b-431e-af97-bf0d93fdd7af`.
Original implementation Code task: `efe49438-7e31-4b87-8c99-39353812aa21`.
Branch: `feat/card-task-5f3b17ba`.
Worktree: `/work/worktrees/task-5f3b17ba`.
Task base / unchanged implementation source: `85c518873e1cbe947392f1d1f1995fe540785ed8`.
The final report-only commit SHA, remote confirmation and evidence-history outcome
are in the completion message; that commit has not been tested.

## Blocking contract and caller action

The brief requires FIRST re-cutting onto current master, dropping the carried plan
commit and replaying the six CARD-1521 test/product commits with `cherry-pick -x`,
without merge commits. Its runner branch contract also states:

> The branch is fast-forward-only from 85c518873e1cbe947392f1d1f1995fe540785ed8: never rebase, amend or reset a pushed commit, and never force-push

> If your brief asks you to rebase this branch, report blocked and say so.

The developer Git contract additionally forbids creating/switching branches and
merging the target into this task branch. Re-cutting would replace already-pushed
ancestry, so it cannot satisfy these constraints. The task did not reset, rebase,
merge, switch branches or force-push. A subsequent fast-forward report commit does
not resolve the ancestry conflict.

Caller action: commission a replacement Code worktree starting at current master,
with authority to replay the six commits listed below onto that new assigned
branch. Alternatively, commission a permitted forward repair of the existing
plan conflict and explicitly replace the re-cut requirement. Do not dispatch
Review yet: CP-2/CP-3 remain missing ordinary execution. No product-design decision
is needed; the dispatch's start ref/ancestry contract needs correction.

Fetched master was `50e71d63ef185ba11081e7f868d2e974072e6733`.
`git merge-tree --write-tree origin/master HEAD` exited 1, with only an add/add
conflict in `docs/superpowers/plans/2026-10-22-card-1521-complete-codex-pointer-plan.md`.
The difference is the pinned-TUnit wildcard suffix fix in CP-1/CP-2/CP-3 filters.
The already-landed plan is `c230d8f3fbc7dc9fdffe8b4e9f36d15d1da7a74a`; the carried
copy is `1ccdd40501d135a7759d3adea3da9dc9e5392803`. Preserve the wildcard fix while
replaying; merely dropping all plan edits loses the predecessor's discovery repair.

Replay these existing implementation/test commits in order on a correctly
commissioned master-based branch, without replaying the carried plan commit:

1. `c331e1ae8a82968f768c9f60bc842d09cd0cf317`
2. `fba66341ed1e3df8923c1f6b4d35a3eff211a4f4`
3. `b69d76eaf37d5648f217eb896e08b76a9edcc51e`
4. `5e27e3593fd757fb0486ecff603728f0550af691`
5. `c6ed50a027e08b50f5a70155496ffc14c44883ce`
6. `85c518873e1cbe947392f1d1f1995fe540785ed8`

## Preserved verification and current obligations

The instructed `git show origin/feat/card-task-efe49438:.antiphon/task-efe49438.md`
fails even after fetching: the predecessor never committed that report. Its full
report was recovered from `/work/worktrees/task-efe49438/.antiphon/task-efe49438.md`,
read in full, and copied unchanged to this branch at `.antiphon/task-efe49438.md`.
Both copies have Git blob hash `ea1d651286dc63373c888864edd2bf62ccbe1a88`.
The API task status independently reports the same incomplete verification.
The copied Markdown preserves every historical unedited CHECKPOINT line, every
CP-1 argument result, actual source SHAs, slot/wait costs, and receipt limitations.
Those observations are predecessor evidence, not fresh results from this task.

| ID | Actual outcome |
|---|---|
| CP-1 / V-1, V-2 red | Historical: 27 executed, 9 passed controls, 18 intended failures, 0 skipped at `5e27e3593fd757fb0486ecff603728f0550af691`. Both capture CR witnesses and Pending duplicate-body witness failed as intended. Not rerun here. |
| CP-2 / V-1, V-2 green | Not run here; all 27 argument rows remain pending at a new valid source tip. Predecessor final build was interrupted before test execution. |
| CP-3 / R-1 | Not run here; all five named regression methods remain pending. |
| CP-4 / V-3 | Owed to Windows; no measurement, timing, receipt or owned-process-exit qualification obtained. |
| Whole Unit / full affected classes | Deferred by the explicit minimal-testing brief; never passed. |

No new CHECKPOINT lines, TRX or build receipts exist. Current task build/test cost:
zero runs, `slot=not-requested`, `waited=0s`. Historical receipt validation still has
the predecessor's `cleanup_refused` limitation; no clean Final qualification is
claimed. No failed ordinary row was rerun because no ordinary run was admitted
after the required first-step conflict. No timeout/assertion changed and no
repeat-proof repetition was used. No alternate build outputs were created.

Pending Mutation obligations remain PC-1 and PC-2 with every variant:

- PC-1: capture-first, capture-second, prefix-10, prefix-14, missing-last-byte,
  complete-120, complete-80, partial-then-complete, history-only,
  complete-swallowed-enter.
- PC-2: pending, interrupted-sent, send-now, canceled-before-next,
  capped-before-next, expectation-direct.

Mutation owns method-scoped deliberate red/restore/green and missing-control
discovery. The unbound candidate remains
`docs/superpowers/mutations/2026-10-22-card-1521-complete-pointer.md`; its historical
owner is efe49438, so a future bound pack must use the caller's actual landing
owner/source identity. No deliberate mutants were applied.

## RC prerequisites: patch applicability, not qualification

Fetched `origin/rc/2026-10-20` is
`0714a9a89e4bff65aae7e0b4b94098694aeb931c`.
Each simulation used a new temporary index seeded with that exact tree, then
sequential `git diff --binary <commit>^ <commit> | git apply --cached --3way`.
No checkout, branch, working source or shared target was changed. This demonstrates
three-way patch applicability, not a completed cherry-pick, build or RC acceptance.

CARD-0266's six code/test commits, in order:

1. `138c72bdad4fb52b7d4903c91a18f013c7d7b384` — S1 canceled-composer witnesses.
2. `96ee5df43209cba8d991ac5e8d11c2d65f90b6d1` — shared canceled-typed-row hold.
3. `60da72847203962d99638fbb4630cdba719bc78b` — queue-notification witness.
4. `5f560e91aefb21ffdcd65459b213cf6267ebc3ff` — notification repair.
5. `86d03f3fdbcd2f30bec6e73ac0c15dfc093ec101` — expression-compatible assertion.
6. `52debc786ac54e321d0f7b6c2c75e342f468cf34` — live-snapshot buffer fixture repair.

These six apply cleanly. Followed by the landed CARD-1521 plan and its six commits,
they eliminate the Expectations conflict but still conflict in
`SessionMessageQueueService.cs` at implementation commit c6ed50a0. That source
also depends on CARD-1403 context, including the CompilerServices import.

The additional CARD-1403 chain that made the entire sequence apply cleanly is:

1. `684c772c58aa8cdaf1b5bfb7be2e506dc076dfa8` — introduces DbCommandMetrics plus witnesses.
2. `7bb6355a468891c2f44e253a84fe686df7c98b8d` — S0 command-counter implementation.

Trying the second commit alone fails because RC has no DbCommandMetrics.cs.
Verified clean order is both CARD-1403 commits above, all six CARD-0266 commits,
the landed CARD-1521 plan `c230d8f3fbc7dc9fdffe8b4e9f36d15d1da7a74a`, then all six
CARD-1521 commits listed earlier. The resulting simulated tree is
`a250744298fd3cc68bee4470a3810b6d3ee22805`.

No CARD-1486 commit is needed for this patch sequence. Its inspected landed S1-S3
product commits (`8b7edcdde449f7e85e39e27b0f166ebaf92a5b3e`,
`9516bfd35a3a3e2a075ba71fa4e3c586d7104220`,
`0fc7a4c2b08e6df7354cff2f374c9d603ab5ac03`) change AgentTaskReplyService.cs, not
either queue service. The clean simulation contains none of them. The caller must
decide the RC port scope for CARD-1403; this task has not authorized or performed
an RC feature port merely to avoid a patch conflict. A narrower reviewed import
conflict resolution is another possible RC port strategy, not verified here.

## Platform and handoff

Read GET /api/runner-defaults and GET /api/session-runners. Defaults revision 2
selects the registered Linux runner; Linux and Windows registered lanes were
dispatch-eligible, while the temporary lane was stale/draining. Eligibility is not
a CPU/memory admission measurement. Per the brief, Windows CP-4 stays owed; the
desktop qualification supervisor PID 133964 and C:\Antiphon\releases were untouched.
No runner pin was embedded and no service was restarted. Restart: none.

Plan: `docs/superpowers/plans/2026-10-22-card-1521-complete-codex-pointer-plan.md`.
Once a valid branch is commissioned, freeze the report before the final checkpoint
run, run CP-2/CP-3 via the checkpoint tool with committed HEAD as expected SHA,
wait for terminal exit, validate receipts and inspect all 27 arguments plus the
five R-1 methods. Preserve CP-4 as owed to the Windows lane. Re-run actual red rows
after a committed repair. Review follows completed ordinary portable verification.

--- next stage ---
next: code
handoff: Commission a master-based Code worktree and replay the six CARD-1521 commits; the current branch contract forbids the required re-cut. Preserve predecessor evidence, finish CP-2/CP-3 on committed HEAD, and keep CP-4 owed. RC patch prerequisites and the clean simulated order are in this report.
artifact: docs/superpowers/plans/2026-10-22-card-1521-complete-codex-pointer-plan.md
