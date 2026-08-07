# Feature 007 — Test spec: multi-agent orchestration

**Status:** proposal — companion to [proposal.md](proposal.md)
**Date:** 2026-08-06

---

## 0. The shape of the problem

Most of this feature is ordinary server logic and tests like ordinary server logic. But four claims
are only true if **real Claude** behaves a certain way, and no amount of unit testing establishes
them:

1. A skill actually makes Claude invoke the delegate script, with sensible arguments.
2. Given the tables, Claude picks the right **shape** (worker vs sub-orchestrator) and the right
   **role** — together these are the entire "decide how big this is" mechanism.
3. Told to report concisely, Claude's final message is actually concise and outcome-first.
4. Told to spill above 20 000 characters, Claude writes the file and summarises instead.
5. A sub-orchestrator rolls its subtree up instead of forwarding everything it received — the one
   behaviour that makes nesting worth having.

Those four get **headed canaries** against the real CLI, following the existing convention
(`ClaudeInterruptCanaryTests`, `ClaudeLocalCommandCanaryTests`): `ANTIPHON_HEADED_TESTS=1`,
`[NotInParallel("Headed")]`, `[Category("HeadedCanary")]`, self-skip when `claude` isn't on PATH.
`fakeclaude` then **mirrors** whatever the canaries measure, so the fast suites can exercise the same
shapes without burning tokens — the same split that made the working/idle work reliable.

### Determinism policy for the real-Claude layers

A model is not a deterministic fixture, and pretending otherwise produces a suite everyone learns to
ignore. So:

- **Assert structure, never wording.** "First line is ≤ 200 chars and isn't a filler opener" — not
  "starts with 'Done'".
- **Accepted sets, not single values.** Role selection asserts membership in a defensible set and
  logs the actual pick, so drift is visible before it's a failure.
- **Budget every headed test.** Wall-clock cap and a cost assertion, so a runaway loop fails the test
  instead of quietly costing money.
- **Canaries gate the contract, not the build.** They run nightly and on changes to the skill, the
  preamble or the reply contract — never in the default CI path.

---

## 1. Real-Claude canaries — `tests/Antiphon.Agents.Pty.Tests/ClaudeDelegateSkillCanaryTests.cs`

Harness: `ClSession.SkipIfNotEligible()` / `BuildLaunch` / `HeadedSafeEnv()`, `PtyAgentRunner`
(`StartAsync` takes `cwd` and `env` — both needed here), `ClaudeReadyDetector`.

**Arrange, shared:** a temp workspace containing `.claude/skills/antiphon-delegate/SKILL.md` (the
real one) and a **stub** `scripts/delegate.ps1` that appends its `$args` as one JSON line to
`invocations.jsonl` and prints a fake task id. The stub is what makes these tests cheap and
assertable: no server, no fleet, no second model — we are testing *what Claude decides to invoke*.
Launch with `cwd` = that workspace, env = `HeadedSafeEnv()` + the `ANTIPHON_*` block.

| # | Test | Asserts |
|---|---|---|
| **C1** | `Skill_makes_claude_invoke_the_delegate_script` — prompt: "run the test suite and tell me what fails" | `invocations.jsonl` gains exactly one line within the turn; it carries `-Role` and `-Goal`. Pins that skill discovery from cwd works and that the script is reached at all. |
| **C2** | `Role_selection_matches_the_work` — `[Arguments]` over the table below | The chosen `-Role` ∈ the accepted set for that prompt. **The single most valuable test here** — it is the only evidence the complexity classification works. Log every pick. |
| **C2b** | `Worker_or_sub_orchestrator_matches_the_size_of_the_work` — `[Arguments]` over five prompts spanning "fix this typo" → "ship the Postgres 18 upgrade" | `-Orchestrator` present for the large ones, absent for the small ones. The other half of the classification: getting the role right but the shape wrong still wastes a tier. Accepted-set style, and log every pick — the boundary cases in the middle are the interesting signal. |
| **C3** | `Reporting_contract_produces_an_outcome_first_message` — launch with the §2.7 contract in `--append-system-prompt`, give a small real task | Final assistant message: first non-empty line ≤ 200 chars; does not open with a filler set (`I'll `, `Let me `, `Sure`, `Great`, `I have `, `Here's what`); contains no "let me know"; total ≤ 20 000 chars. |
| **C4** | `Oversized_report_spills_to_a_file` — task whose honest report must exceed 20 k (e.g. "document every public method in this directory in full") | `.antiphon/task-<id>.md` exists and is > 20 k; the final message is < 20 k **and** contains that path. Pins the primary size mechanism. |
| **C5** | `Twenty_thousand_char_delivery_lands_as_one_turn` — deliver a 20 000-char body using the queue's exact discipline (LF-normalise → bracketed paste → separate `\r`) | Exactly one `UserPrompt` record and one `TurnEnd`; no fragmentation. Extends `SessionMessageQueuePtyIntegrationTests.Large_multiline_channel_body_submits_as_one_intact_turn` to the actual ceiling — 20 k is far past what that test currently proves, and the CR-fragmentation failure mode is a documented live miss. |
| **C6** | `Antiphon_env_vars_reach_a_script_claude_runs` — ask Claude to run a script that echoes `ANTIPHON_SESSION_ID` | The real session guid appears. Guards the pty-host boundary: if env doesn't propagate to the agent's child processes, the whole skill contract is dead and every other test would fail confusingly. |

### C2's parameter table

| Prompt to Claude | Accepted `-Role` | Intended tier |
|---|---|---|
| "work out how to add rate limiting here, don't write code yet" | `Plan` | fable |
| "add a Fizz(int) that returns 'Fizz' for multiples of 3" | `Code` | fable |
| "is the locking in SessionMessageQueueService actually correct?" | `Review` | fable |
| "the link-check fails on relative anchors, find out why" | `Debug` | opus |
| "what does this change not cover?" | `Coverage`, `Review` | opus |
| "rewrite the Windows install section for pwsh" | `Docs` | sonnet |
| "commit this and push it" | `Commit` | sonnet |
| "run the agent test suite" | `Test` | haiku |
| "restart the app host and check it's healthy" | `Deploy` | haiku |

Tier is asserted from the **policy resolution**, not from Claude — the model picks a role, the server
picks the tier. That keeps C2 stable when you retune the ladder in config.

---

## 2. fakeclaude mirrors — `FakeClaudeContractTests`

Whatever C3/C4 measure, `Antiphon.FakeClaude` must be able to reproduce, so the server and E2E
layers can run without the real CLI:

- **F1** — fakeclaude can emit a final assistant message of a requested size (`--emit-report <n>`),
  ending in a normal `TurnEnd`.
- **F2** — fakeclaude honours the spill instruction: given a report target above the ceiling it writes
  `.antiphon/task-<id>.md` and returns a referencing summary, exactly as C4 observes real Claude doing.
- **F3** — fakeclaude can invoke a script when its prompt carries a delegate instruction, so the
  server-side dispatch/reply loop can be driven end to end deterministically.

---

## 3. Server integration — `tests/Antiphon.Tests` (Postgres fixture, `[NotInParallel("AgentQueue")]`)

| # | Test | Asserts |
|---|---|---|
| **S1** | Two concurrent dispatcher ticks claim a task exactly once | One `Dispatched`, one no-op — mirrors `OrchestratorService.TryClaimCardAsync`'s guarantee |
| **S2** | Role → tier resolution per role, from config; `-Level` overrides it and records why | The whole ladder, table-driven |
| **S3** | Launch args for an ephemeral delegate | `--model <alias for tier>`, reporting contract present in `--append-system-prompt`, all five `ANTIPHON_*` env vars set, `cwd` = the worktree |
| **S4** | **Reply correlation, positive** — child turn-end carrying `[antiphon-task:id]` | `Result` written verbatim, `Succeeded`, cost rolled up, note enqueued to the parent |
| **S5** | **Reply correlation, negative** — a human turn in the same session with no marker | No completion, task still `Working`. This is the misroute guard; without it a person typing in a delegate's terminal ends the task |
| **S6** | Question classification | Delegate asks → `Blocked`, no completion note; `POST /reply` unblocks and re-delivers |
| **S7** | **Under the ceiling passes through whole** — an 18 k report | Note body contains the full 18 k, unmodified |
| **S8** | **Over the ceiling backstops** — a 25 k report from a delegate that ignored the instruction | Server writes `.antiphon/task-<id>.md`; note carries head 6 k + elision + tail 6 k + the path; `AgentTask.Result` still holds all 25 k |
| **S9** | Size-aware coalescing | 5 × 6 k results: batches until the combined body would cross 20 k, remainder delivered on the next turn-end; no result dropped |
| **S10** | Escalation | Failure → tier + 1 with the handoff block containing the prior attempt's findings; stall timer → same; ceiling and `MaxAttempts` respected; `EscalatedFrom` and the event row written |
| **S11** | Fan-out caps | Depth 4 rejected; `MaxTasksPerRoot` enforced; `MaxConcurrentTasks` throttles dispatch without dropping tasks |
| **S12** | Worktree lifecycle | Created; merged by **rebase then `--ff-only`** (never a merge commit); conflict → `Blocked` + a `Merge` task spawned with the conflict list |
| **S13** | Shared-mode leases | Two `Shared` tasks with intersecting `ScopeGlob` serialise; disjoint globs run concurrently |
| **S14** | Auth | `POST /api/agent-tasks` without `ANTIPHON_TASK_TOKEN` → 401; with another session's token → 403 |
| **S15** | `ReplyTo` routing | `None` → board only, no delivery; `Session` → delivered; `Channel` → out the existing channel sink |
| **S16** | **A worker cannot delegate** | A task created with the worker's own token → 403, no row written, an incident recorded. The enforcement boundary behind the whole recursion story — if this leaks, `MaxCostUsdPerRoot` becomes the only thing between you and a fork bomb |
| **S17** | **A sub-orchestrator can** | Same call with an orchestrator's token → 201, child carries `ParentTaskId`, `RootTaskId` of the grandparent's root, `Depth` + 1 |
| **S18** | **Children report to their own parent** | A grandchild's completion note is delivered to the sub-orchestrator's session, **not** the root's. The context economy of nesting, asserted directly |
| **S19** | Subtree rollup | A sub-orchestrator's own report is the only thing the root receives for that subtree — the root's session gets exactly one note per subtree, whatever the child count |
| **S20** | Hierarchical merge | A worker under a sub-orchestrator merges into the **sub-orchestrator's** branch, not master; the sub-orchestrator's branch then merges one level up; both by rebase + `--ff-only` |
| **S21** | Cost ceiling | Crossing `MaxCostUsdPerRoot` stops further dispatch for that root and marks it `Blocked`; tasks already in flight are left alone and still report |
| **S22** | Depth backstop | A task at `MaxDepth` is rejected with a message naming the limit; the tree below it is untouched |

Run: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-ptyhost\ --treenode-filter "/*/*/AgentTask*/*"`

> **As built:** S10 is `AgentTaskStallEscalationTests` (auto-bump pins the shipped Debug policy;
> transcript progress resets the clock; no-target and at-target tasks untouched) plus the
> retry/escalate/token-refresh tests in `AgentTaskServiceIntegrationTests`. S12 splits into
> `DelegationWorktreeTests` (real-git lifecycle: branch-from-target, adopt-leftover, commit-all
> sweep, ff-advance of a checked-out target, conflict aborts cleanly with the file list, no-target
> leaves the branch, empty branch removed) and the settle-side tests in
> `AgentTaskReplyIntegrationTests` (merged→note, conflict→Blocked+Merge task, Merge-task completion
> un-blocks the conflicted parent). Ephemeral cleanup: succeeded → session stopped + agent row
> deleted; Blocked keeps both.
(alternate output path because the always-on daemons lock `bin/`).

---

## 4. The pipeline E2E — `tests/Antiphon.E2E/DelegationPipelineE2ETests.cs`

**This is the test the feature is really judged by** — real stack, real Claude, real git, one human
message in, a finished pipeline out. `[Category("Headed")]`, opt-in, nightly.

**Arrange**
- A scratch git repo: a trivial C# project, one failing test.
- An orchestrator agent — Frontier tier, the §2.8 preamble, the skill on its path, the env block.
- Role policy pinned in test config so tier assertions are stable.

**Act** — one message:
> "Add a `Fizz(int)` that returns 'Fizz' for multiples of 3, get the tests passing, and commit."

**Assert — the follow-through**

| # | Assertion |
|---|---|
| E1.1 | ≥ 4 tasks created under one `RootTaskId` |
| E1.2 | Roles observed cover plan/code, test, and commit |
| E1.3 | **Tier per role matches policy** — plan and code at Frontier, test at Low, git at Medium. The user-facing promise of the whole design |
| E1.4 | **Distinct sessions per task** — not all funnelled into one agent |
| E1.5 | Every completion note reached the orchestrator: `[task <id> done]` records present in its transcript, in completion order |
| E1.6 | The repo ends with the test passing and a commit whose message references the goal |
| E1.7 | **The orchestrator made no file edits** — zero `Edit`/`Write` tool records in its transcript; the commit came from a delegate's session. The direct test of "don't do work on the main agent" |
| E1.8 | Wall clock < 15 min and total cost < a configured ceiling — a runaway fails rather than bills |

**E2 — the manual path stays separate.** Files view → Delegate… on `docs/setup.md` → one worker
created with `ReplyTo=None`; nothing is delivered into any session; the board shows it. Then the same
modal with the sub-orchestrator toggle → an orchestrator task, and its children appear nested under
it. Proves the two entry points share a core without the manual one acquiring reply behaviour.

**E2b — nesting, end to end.** One message to the root orchestrator describing work with two clearly
separable halves (e.g. "upgrade the Postgres image to 18 and update the docs that reference 17"),
phrased so a sub-orchestrator is the sensible shape for at least one half.

| # | Assertion |
|---|---|
| E2b.1 | At least one task has `Kind = Orchestrator`, and it has children of its own |
| E2b.2 | **The grandchildren's notes went to the sub-orchestrator's session, not the root's** — the root's transcript contains one `[task … done]` for that subtree, not one per leaf |
| E2b.3 | The sub-orchestrator's report is materially shorter than the concatenation of its children's reports, and mentions each child's outcome. The rollup clause working, or not |
| E2b.4 | Worker branches merged into the sub-orchestrator's branch; that branch merged one level up; `git log --merges` on the target is empty (rebase discipline held throughout) |
| E2b.5 | Neither the root nor the sub-orchestrator has `Edit`/`Write` records in its transcript |
| E2b.6 | A worker's attempt to delegate, if any occurred, was refused — and the run still completed |

**E3 — escalation, observed live.** A `Debug` task on a deliberately subtle bug with
`escalateAfterMinutes` set low: task starts at opus, escalates to fable, the handoff block contains
the opus attempt's findings, the board chip shows the escalation marker.

**E4 — a delegate's question round-trips.** Delegate asks → `Blocked` → orchestrator answers via
`-Reply` → delegate resumes and finishes. Asserts the orchestrator answered rather than absorbing the
work (still no `Edit`/`Write` in its transcript).

---

## 5. Client — Vitest

- **U1** Board renders lanes by status; chips show agent, tier pill, elapsed, cost, workspace badge.
- **U2** Tier pills use the intensity ladder and are distinct from status colour — a `Failed` task at
  `fable` shows a red status stripe and a solid-violet tier pill simultaneously.
- **U3** Delegate modal: the worker/sub-orchestrator toggle switches the role default to `Plan` and
  the tier display with it; role chips update the displayed tier; submitting posts the chosen `Kind`.
- **U4** A sub-orchestrator row in the tree collapses its children by default and shows the subtree's
  task count and spend on the parent row; expanding reveals them.
- **U5** Drawer actions hit the endpoint they claim: Retry posts `/retry`, Escalate posts `/escalate`
  with no tier (the ladder is the server's decision), Cancel posts `/cancel` and closes. Escalate is
  unavailable at `fable` and Retry is unavailable on a task that has not run — a disabled control is
  cheaper than a 409 the user has to read.
- **U6** A `Blocked` task's drawer offers an ANSWER, not a retry: typing one posts `/reply` with the
  message. Taking the work back is the failure mode delegation exists to prevent.

> Built as `client/src/features/delegations/*.test.tsx` (+ `taskVisuals.test.ts`), 41 tests.
> The board/drawer/modal stories seed from the `agent-tasks.json` / `agent-task-detail.json`
> contract fixtures, captured by `ContractSnapshotTests.Delegated_task_board_and_drawer_contracts`.

---

## 6. Open question to settle before writing E1.3

The brief said tests run on the **cheapest** tier, and later that "run tests" is **top tier**. This
spec assumes the first: `Test`/`Deploy` = Low (`haiku`) for *running* a suite and reporting failures,
with *interpreting* those failures being a separate `Debug` task at High. That split is why the cheap
tier is safe here — the haiku task never has to reason about a failure, only report it.

It costs one config line to change. It changes no test code: E1.3 asserts "the tier the policy
resolves for this role", not a literal.

---

## Done means

- C1–C6 (incl. C2b) green against real Claude, with their measured behaviours mirrored in
  fakeclaude (F1–F3).
- S1–S22 green in the fast suite. **S16 is the one that must never be skipped** — it is the only
  thing stopping recursion from becoming unbounded.
- E1 and E2b each green twice consecutively (nondeterminism check), E2–E4 green.
- U1–U6 green.
- The role table in `SKILL.md` and C2's accepted sets reviewed together — they are the same contract
  written twice, and drift between them is silent.
