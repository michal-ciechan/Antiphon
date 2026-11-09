# Review df7abdd7: CARD-1695 S1 round 1 (Final, Full)

Verdict: CLEAN. No blockers, no regressions. 4 advisories are listed below. They go to cards and do not hold the land.

- Subject: Code task 026f0aba-1549-488c-b865-e3b13460f35b (Grok author), branch feat/card-task-026f0aba
- Reviewed SHA: 111487d5cfa85820067e862d898d3654464e4981. Range e983d6fe263af623a3da186a2260575f60cc4c5f..111487d5cf has 3 commits: plan 14d455809, red 0f0bd7c75, fix 111487d5c.
- Master: e06bcb3a0c17f4452db8b31ba92a22694ad3d5ab

## Evidence (all at 111487d5c, clean tree)

| Check | Result |
|---|---|
| CP-1 (plan row, checkpoint run 20261109-232252-3a21) | `StandingRunnerCwdTests`: executed=16 passed=16 failed=0 skipped=0. sourceState=clean, buildSource=verified. `validate`: CHECKPOINT SOURCE VALID rows=1 |
| CP-11 (review extract `.antiphon/review-df7abdd7-plan.md`, run 20261109-233309-d814) | `PhoneHomeCommandDispatcherTests`, `StandingRunnerCwdTests`, `TestClassificationGuardTests` and `NativeLinuxPolicyWitnessTests` in SessionRunner.Tests: 65/65 passed |
| CP-12 (same run) | Guard batch in Antiphon.Tests: `TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitness*` and `TestLaneCategoryGuard*`, plus `RunnerWorkspaceCwdWindowsTests` (the other consumer of `RunnerWorkspaceCwd`): 80/80 passed. `validate`: VALID rows=2. Noted: `SLOW CLASS TestClassificationPolicyTests` took 115 s for 41 tests; this is an existing class |
| Red commit 0f0bd7c75 (detached scratch worktree, build-slot run) | 6 of 16 red: Agents_checkout_is_admitted, Agents_root_nested_and_traversal_are_refused, Linux_agents_directory_case_difference_is_refused, Very_long_single_segment_within_name_max_is_admitted, Absent_checkout_is_admitted_without_following_a_symlink, Dispatcher_message_names_the_agents_checkout. Every failure is a Shouldly assertion on `IsAdmitted` or on the message's agents clause, which is the right reason |
| Mutants (env-gated in one build, one lease per run, restored, tree verified clean) | 8 of 8 killed. nobranch: 5 red. Nested `/`: 7 red. Admit `/work/repos/x`: 1 red. Resolve `..` via GetFullPath: 8 red. Worktree treated as standing: 1 red. Message agents clause dropped: 1 red. Length bound dropped: 1 red. ASCII/control filter dropped: 2 red. Unmutated control: 16/16 green |
| Adversarial probe (scratch TUnit class, deleted) | 77 strings with both `windows` flags = 154 classifications. Each was compared with a verbatim copy of the OLD function and with an independent oracle (root + `/agents/` + one printable-ASCII segment without `/`, `\` or `:`, not `.` or `..`, at most 255 bytes, and on Windows no trailing dot or space). 0 violations: nothing the old function admitted is now refused, and every new admission is a standing checkout that the oracle agrees with |
| check-evidence-diff (base..HEAD, repository is not shallow) | commits=3, entries=0, violations=0, exit 0 |
| merge-tree vs origin/master e06bcb3a0 | Clean, tree 4247d55b7 |
| land-graph `-Check` on a scratch merge commit (838a70185, local only) | `approved`, exit 0 |
| Policy json / migrations | Neither changed |

Probe classifications on Linux (`windows=false`, root `/work`):

- Refused: `/work/agents/x/`, `//x`, `/./x`, `/../x`, `/x/../y`, `/x/y`, `/..`, `/.`, `/work/agents/`, `/work/agents`, `/work/agents\x`, `/Work/Agents/x`, NUL, tab, LF, CR, U+FF0F, U+2215, U+00A0, U+200B, `é`, `İ`, 256-char segment, `//work/agents/x`, leading space, `C:\work\agents\x`, `C:/work/agents/x`, relative `agents/x`, `work/agents/x` and `./work/agents/x`, `/work/./agents/x`, `/work/agentsx/y`, `/work/agents.x/y`, `/workagents/x`, `/work/worktrees/agents/x`, `/work/agents/x/worktrees`, `/work/repos/x`, UNC, `\\?\`, `/work/agents/..\..\etc`, `x::$DATA`
- Admitted: `x`, `%2e%2e` (literal, never decoded), `%2e%2e%2fetc` (literal), `a b`, `x.`, `...`, `....`, 255-char segment, `-x`, `~`, `~root`, `worktrees`, `repos`, `x ` (trailing space), `$(id)`, `` `id` ``, `'x'`, `x*`, `CON`
- Windows flag: same as Linux, except it adds backslash and case folding and refuses a trailing dot or space and a colon. `C:\work` root admits `C:\work\agents\x` and `c:/WORK/AGENTS/x`, and refuses `D:\...`, `x:stream`, `x.`, `..` forms and `\\?\`.
- Unchanged: `/work` and `/work/worktrees/x` are admitted under both flags, exactly as before. The worktree predicate is a verbatim move.

## Answers to the brief's checks

1. Path escape: none found. Only one call site uses this: `PhoneHomeCommandDispatcher.IsAdmittedCwd`, which handles launch admission. The new branch is reached only after an exact `root + "/agents/"` prefix match. The segment refuses `/`, `\` and `:` on both flags, refuses `.`, `..` and empty, and is limited to printable ASCII 0x20-0x7E and at most 255 UTF-8 bytes. Nothing is decoded or resolved. The cwd reaches `PtyAgentRunner.StartAsync`/`StartTrackedAsync` as a structured spawn parameter, not as shell text, and is not interpolated into any `cd` or shell string in SessionRunner, PtyHost or Agents.Pty. `RunnerWorkspaceActivity.RecordLaunch` now stamps an agents path. That only protects paths at or under it from retention and cannot widen a removal.
2. Symlinks: the check is lexical only, as the plan says (D-1, and line 71: "The cwd gate is not a sandbox"). See A-2.
3. Segment grammar: this is a deny-list, not an explicit `[A-Za-z0-9._-]` allow-list. Dot-only names other than `.` and `..` (such as `...`) are admitted on Linux, where they are ordinary names with no traversal meaning. This matches plan D-1 ("Segment rules match worktrees"), and is stricter than the worktree rule, which admits controls and non-ASCII. See A-1.
4. Server/runner consistency: the server has no `cwd must be` text and no lexical runner-cwd check. It projects `/work`, or the task `runnerCwd` under `/work/worktrees/`, and the runner is the only gate. In S1 the server cannot yet send an agents path, so there is no mismatch now. S2 must use `RunnerWorkspaceAdmission.IsStandingCheckout`, which the plan requires. See A-3.
5. Tests: covered in the evidence table above.

## Advisories (cards; they do not hold the land)

- A-1. Where: `RunnerWorkspaceAdmission.IsAgentsCheckout`. Failure: shell metacharacters, a leading `-` or `~`, dot-only names (`...`) and Windows device names (`CON`) are admitted as standing names. Why it is not a blocker: there is no escape, because cwd is a structured spawn parameter, and the historical worktree rule admits a wider set. Fix: before S2 lets an API caller set the path, tighten the segment to `[A-Za-z0-9][A-Za-z0-9._-]{0,254}` and refuse device names on the Windows flag.
- A-2. Where: runner launch admission. Failure: `/work/agents/<name>` can be a symlink to anywhere. Any uid-1654 process on the runner, which includes every delegate, can create one, as it already can under `/work/worktrees/`. A later standing launch would then start in the link's target. Mitigation: the cwd only arrives from an API-written agent `WorkingDirectory` (S2), and the operator creates the clone (D-4). Fix: at launch, lstat the standing leaf and refuse a symlink or a non-directory, or make `/work/agents` writable only by the operator.
- A-3. Where: S2 server policy. Failure: if the server's configured `RunnerWorkspace` for the runner differs from the runner's `PhoneHome:AllowedCwd`, S2's `IsStandingCheckout` and the runner gate will disagree. Fix: the S2 review checks that both sides use the same root, and that a mismatch fails as a 409 before save.
- A-4. Where: `TryNormalize` root handling. Failure: `AllowedCwd` set to `/` or empty admits `/agents/<x>`, just as the old rule admitted `/worktrees/<x>`. The cause is configuration only and nothing changes in production (`/work`). Fix (optional): refuse a root that is empty or `/` at settings load.

## Not run / pending

- PCs: all stay pending for method-scoped SourceLanding Mutation. The review mutants above do not discharge them.
- CP-2..CP-4 belong to S2..S4 and are not in this subject.
- Operator acceptance (create `slides-server2`, kill and restart, one delegate dispatch) stays manual and pending, and is out of S1.
- Changed input waits: none in S1. Parking stays disabled, so a waiting standing session has no automatic release deadline (CARD-1083).
