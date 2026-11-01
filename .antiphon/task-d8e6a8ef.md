# CARD-1610 S1 round 3

Zero-row warm sessions are retained on the TTL pass, the cap pass, and the pre-kill recheck. The boundary read is two server-side queries that return session ids only. Each Failed-owner clause is witnessed alone. Closed list run `20261030-085936-d38b` is GREEN at `a18ee7fcf3c2226823182579453db8a82987341b`.

## Commits

- `6e8dadbaac0e7b690d06533bd7af497d1876879b` — red zero-row witnesses (TTL, cap including the 3-minute agent, pre-kill query). Failed on that tip because a transcript with no rows was still eligible to kill. The 14 existing arms stayed green.
- `481ea4796632480acc6eb71577cfa7365a85321f` — reuse-other-agent and later-agent-task. Both pass on `6e8dadbaa`.
- `a18ee7fcf3c2226823182579453db8a82987341b` — retain, id-only queries, TurnEnd seeds, plan, invariants. Checkpoint source.

## Mutations (restored after each run)

- Any Failed on the session holds: `reuse-other-agent` failed (killed list empty for `ab74887a-c54c-4be4-a83f-2b0542d25a3f`). `later-agent-task` stayed green. Run total 19, failed 4, succeeded 15 (the three zero-row arms were still red).
- Agent-latest clause dropped (`FailedTaskSessionOwnership.cs` lines 98-101): `later-agent-task` failed (killed list empty for `a39bd296-7a39-48a8-83f9-7970be80b19a`). `reuse-other-agent` stayed green. Same 19/4/15 shape. Ownership file restored; `git diff` on it was empty before the D-2 commit.

## Checkpoints

Run `20261030-085936-d38b`, commit `a18ee7fcf3c2226823182579453db8a82987341b`, source clean, build source verified, verdict GREEN, exit 0. CP-1 at this fixed tip is green: the Expect text records the pre-fix red, and the tool passes when failed is 0 and executed is at least Min.

| CP | executed | passed | failed | skipped |
|---|---:|---:|---:|---:|
| CP-1 | 20 | 20 | 0 | 0 |
| CP-2 | 1 | 1 | 0 | 0 |
| CP-3 | 20 | 20 | 0 | 0 |
| CP-4 | 48 | 48 | 0 | 0 |
| CP-5 | 36 | 36 | 0 | 0 |
| CP-6 | 167 | 167 | 0 | 0 |
| CP-7 | 10 | 10 | 0 | 0 |
| CP-8 | 23 | 23 | 0 | 0 |
| CP-9 | 2 | 2 | 0 | 0 |
| CP-9a | 1 | 1 | 0 | 0 |

## Known cost

A pooled session that never writes a transcript row keeps its seat until a turn boundary or an operator acts.

Tests that depended on zero-row retirement, and how the intent is kept (TurnEnd seeded; none removed or relaxed):

- `the_janitor_retires_agents_idle_past_the_ttl`: proved-idle past the TTL is still killed and the row removed.
- `the_janitor_enforces_the_per_directory_cap_oldest_first`: all five proved idle, so the two oldest still go and the freshest stays.
- `the_per_directory_cap_skips_a_mid_turn_agent`: TurnEnd on the four that may be capped; the oldest stays mid-turn and is still skipped.
- `the_janitor_kills_a_recovered_worktree_delegate_after_the_ttl`: proved idle, so the TTL kill still happens and the incident row stays Stopped.
- `the_janitor_keeps_the_row_when_the_kill_did_not_take`: proved idle, so the kill is still attempted, retried, and the row stays Stopped with DelegateReleaseUnresolved.
- `GrokDelegateDispatchTests.the_cap_still_retires_a_surplus_within_one_kind`: both proved idle, so the cap still retires the surplus within one kind (CP-9a).

No other `RetireIdleWarmAgentsAsync` caller required a zero-row kill. Fresh-under-TTL, mid-turn, C691 sweeps, stale terminal rows, the park reservation keep, the check-schedule hint, and the settlement-race flush do not.

## Decisions and doc edits

D-10 already said absent transcript evidence is retained. Added an explicit zero-row sentence, the two id-only queries as the performance contract, A-1, and A-2. V-1 is 20. CP-1 Expect is `20 executed; pre-fix red: zero-row-ttl, zero-row-cap, zero-row-recheck; no skipped` with Min 20. CP-3 and CP-19 Min/Expect are 20. Slice table count is 20. CP-9a added for the Grok cap test.

`docs/session-runtime-invariants.md` said a transcript with no rows stayed eligible for TTL and cap retirement, and that the hold applies only when the session generation is proved. That sentence now says zero rows are retained on all three sites, generation is proved only by `StartedAt != default` (no `DispatchedAt >= StartedAt` comparison; a pre-restart Failed attempt still holds), and a later task on the session or the agent releases the hold, including a Canceled never-dispatched task with no session.

A-1 stays release. `later-agent-task` is that witness, and it is what makes dropping the agent clause red. A-2 is documented as `StartedAt != default` only.

Id-only proof: `ToQueryString()` select list before each FROM contains `AgentSessionId` and does not contain `Text`. The any-row SQL does not contain `Text`. The boundary SQL contains `TurnEnd`, `SessionRestartBoundary`, `(manual)`, and `[Request interrupted`. Both queries were executed on Postgres. A zero-row id, a title-only session, an automatic compact, and a plain user prompt are retained. A turn end, a manual compact, an interrupted prompt, and a session-restart boundary are not.

## Land and merge-tree

`scripts/audit-land-verification-graph.ps1 -Check` approved, exit 0, digest `20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6`, count 66. No `-Update`. The first invocation refused because the evaluator DLL was absent; it was built to `bin-land-audit/Release/net10.0/` and the check was re-run. Those output directories were removed.

`git fetch origin master` at `8b5f3f5d479b11ebb8e27105ec9ffac379b41909`. `git merge-tree --write-tree origin/master HEAD` exit 0, tree `e8beee1535a46c69a3d49131bc4b88ffc2575dba`, no conflicts. Not merged.

Round-2 closed list `20261030-075226-c1b6` at `ae5afcc5e` stays the prior green (CP-3 14/14, CP-4 48/48, CP-5 36/36, CP-6 167/167, CP-7 10/10, CP-8 23/23, CP-9 2/2). Review base `98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`. No S2..S4 product files in this round. For the rc pick, drop the plan doc: `ae5afcc5e` and `a18ee7fcf` both modify it, and the round-2 review already saw a modify/delete against rc. Leave out report commit `406fa2145`.
