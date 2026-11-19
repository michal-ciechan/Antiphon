# CARD-0824 S3 A-1 review, round 2 (blockers only)

- Review task: a239df4b-0e40-4180-8a0f-3d94455f46d5
- Subject: origin/feat/card-task-54d2ac1b (Debug 54d2ac1b-0834-43fc-b7ce-91af0eba1cd0)
- Range: bfeaed2cb9ccff7eb96320d5c753f148a689470c..d56e5d92d13e02b288a1e1902275c9950bc388d2 (044c3afe4 red witness, 5bd8e564d fix, d56e5d92d index)
- Round-1 clean base: review 7951bfb5 (351140f13), advisory A-1.
- Master checked: ef331572957c36548dffed10a6f68a026268193f (moved past the brief's 9bcd61113).

## Verdict

CLEAN. No blockers.

## Judgement

1. **Can a real path be replaced?** No. `WindowsSlotCheckout.IsPending` is an ordinal `StartsWith("windows-slot-pending:")`, which is a prefix test and not a contains test. No Windows (`C:\...`) or Linux (`/...`) path starts with that text. The only writer of the sentinel is `DelegationWorktreeService.RecordWindowsSlotPendingAsync` (line 563). `RecordPathAsync` reads the row `FOR UPDATE` and refuses when a different `RemoteWorktreePath` is already recorded. A second readiness report therefore finds `WorktreePath` already replaced, so `IsPending` is false and the path is kept. Two different paths cannot both win.
   **Can it fail to replace when it must?** Both writers go through `RecordPathAsync`: the path-only preparer (`RemoteWorkspacePreparer.cs:310`) and the binding capture (`RemoteWorkspaceService.cs:520`). Both now replace the sentinel whether `slotId` is set or not.
2. **Are server2 and the default-off paths unchanged?** Yes. The sentinel is written only when the runner is Windows, advertises `workspaceSlotV1` and `DecideListed` admits it. That needs the slot flag, `WindowsSlotRoles` and `WindowsSlotRunnerIds` all set. With defaults, or on a Linux runner, `IsPending` is false, which is the same result the old `slotId != null && IsPending` gave. The other sentinel consumer (`PushBranchAsync`, `RemoteWorkspaceService.cs:124`) is not in the delta.
3. **Can the witness fail?** Yes. `Cleared_list_after_checkout_replaces_the_pending_sentinel` drives the real `RemoteWorkspaceService.ResolveSlotDecisionAsync` with a monitor whose list is cleared, then the real `RemoteWorkspaceReadiness.RecordPathAsync` on an isolated Postgres schema. It is not a copy of the logic. Mutant: I restored `slotId != null && ...` and rebuilt. The witness went red with expected `"C:\Antiphon\runner\workspaces\task-00000004"` but was `"windows-slot-pending:08240000000040008000000000000004"`. The other two CP-4 methods stayed green. I then restored the source, and the tree is clean.

## Runs at d56e5d92d (native output, UseAppHost=false, one build-slot lease per command)

| Run | Result |
|---|---|
| Build `tests/Antiphon.Tests` | 0 errors, 5m12s |
| C0824WindowsSlotAdmissionTests + C0824WorkspaceSlotServerTests + RemoteWorkspaceReadinessTests | 47/47 passed: 3 + 25 + 19 (CP-4 class = 3, incl. the 2 plan methods and the witness) |
| C0824_ReleaseRoundTripRealRunner alone, x2 | 2/2 and 2/2; `(direct)` 1.6 s and 1.2 s, `(reply-lost)` 14.6 s and 16.1 s |
| DerivedImpactIndexTests alone | 27/27 |
| Guard batch (TestClassificationPolicy 42, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3) | 53/53 |
| Mutant (old condition) on C0824WindowsSlotAdmissionTests | 1 failed (the witness), 2 passed |
| `audit-land-verification-graph.ps1 -Check` | exit 0, approved |
| `check-evidence-diff.ps1` over the range | commits=3, violations=0 |
| `git merge-tree --write-tree origin/master HEAD` | clean (dfa3a5502) |

The Debug task saw one 35 s timeout of `C0824_ReleaseRoundTripRealRunner(direct)`. I count that as load, not a regression. The delta does not touch the release path; server2 tasks never hold the sentinel. Alone, `(direct)` takes 1.2 to 1.6 s.

## Gaps

- I ran CP-4 through build-slot `dotnet run`, not the checkpoint tool, so there is no SHA-validated checkpoint receipt. CP-2 and CP-3 (SessionRunner) are not touched by the delta and were not rerun in this diff-only round.
- Load rule: I started the guard batch at 1-minute load 24.8 and the mutant run at 26.6 without waiting first. Both were green or red as expected.
- The task record shows 54d2ac1b as `agentKind=ClaudeCode`, while the brief says the author is Grok. If a Claude model wrote the A-1 delta, this Claude review breaks the standing author/reviewer-company rule. The caller should confirm the author before landing.
