# rc bundle dry-run onto rc/2026-10-20 (2026-10-22)

Task 4ba5becb (Investigate, read-only). Scratch detached worktree at `origin/rc/2026-10-20`
`ceb6e3d6b78471db6831258699c493b8eb2d619b`; nothing pushed except this document.

## Outcome

All nine card sets apply to rc. Of the 25 picks, 23 are clean and 2 hit conflicts that touch only
plan docs. Two one-line test adaptations are needed. The build has 0 errors. The evidence diff is
clean. The bundle adds no EF migrations.

- Bundle tip in the dry-run: `0b9f36a6d9edafe5261f47484b2f2959bd5919e0` (27 commits over rc:
  25 picks + 2 adaptations). The commit SHAs depend on committer time. The **tree** is
  deterministic: `dbc76ebe9601a72036d68c48bdebc5d4d31eba10`. A mechanical re-run must reproduce
  this tree exactly.
- The recipe below was run **verbatim** in a fresh worktree and produced that tree. An earlier
  hand run was thrown away because its conflict helper ran `git rm -r --cached docs/superpowers/plans`,
  which removed all 832 rc plan files. The recipe only removes the plan paths that each pick
  itself touches.

## SHA verification

`git cat-file -t` returns `commit` for all 25 listed SHAs. **None of the listed SHAs is an
ancestor of origin/master, except CARD-1471 `0aa1473a9` and `e7825b823`.** Landing rebased the
others. Each one has a master equivalent with the same subject and the **same stable patch-id**:

| Set | Listed (branch) SHA | origin/master equivalent | patch-id |
|---|---|---|---|
| 1471 | 0aa1473a9 / e7825b823 | same (on master) | = |
| 1485 | fd06b3c35 / d1bce8c01 | f3c120a7a / 36cf9bc20 | = |
| 1481 | 4bf0275ee ce72659f9 92a5e97eb a57a77635 98d47fa7b ae4fa6f0c 6b4b0137a | 31a6f7e23 1dd6a4d41 10a0c48b9 c554c0e60 754452ae1 bc2f8f592 47772c152 | = |
| 1465 | cb148bcc7 | a432bd63c | = |
| 1464 | e41eb41bb | 3a2ff6d80 | = |
| 1489 S1 | 7d09503c2 / cc1156d01 | 48441b729 / eacfcf7e4 | = |
| 1480 S1 | b0fac8217 b0ccfd1ca c88082a68 | a3bb73d05 1819567f8 98b8a8d48 | = |
| 1490 | 8d407e723 520d2cd41 5602e7bfd 42e75c3d9 | 17e063a30 4c0cce692 a104324d5 8f61a6c74 | = |
| 1474 | 0dd0f8551 57f78333c 060c15d01 | 29c0742bc e8a654e46 25e8908dd (on master; tip b0c0f89b8) | = |

None of these is on rc already (subject grep over `origin/rc/2026-10-20` returns nothing).

The gaps in the listed chains are docs-only commits, and they are correctly left out:
268695d55 / 45e7e8775 / 0e8be8391 are `.antiphon/task-*.md` reports, and 792227d02 / a9cfd3623
are plan docs. The master card logs contain no other code commit for these cards.

**Order note.** The brief lists CARD-1471 as "e7825b823 + 0aa1473a9". `0aa1473a9` (the red test)
is the parent of `e7825b823` (the fix), so the recipe picks them in topological order.

## Per-pick result

| # | Pick | Result | Plan doc dropped |
|---|---|---|---|
| 1 | 0aa1473a9 | clean | 2026-10-22-card-1471-dispatch-slug-plan.md (A) |
| 1 | e7825b823 | clean | - |
| 2 | fd06b3c35 | clean | 2026-10-22-card-1485-supersede-plan.md (A) |
| 2 | d1bce8c01 | clean | - |
| 2 | **adapt** | `LandReplacementApprovalTests.cs:19` GitProcessSpawnLimit -> ProcessSpawnLimit (**not anticipated by the brief**) | - |
| 3 | 4bf0275ee ce72659f9 92a5e97eb a57a77635 98d47fa7b ae4fa6f0c | clean | - |
| 3 | 6b4b0137a | **conflict, plan docs only**: `CONFLICT (modify/delete): docs/superpowers/plans/2026-10-22-claude-effort-by-role-plan.md deleted in HEAD and modified in 6b4b0137a`; code files auto-merged | claude-effort-by-role-plan.md (M), claude-effort-by-role-repair.md (A) |
| 4 | cb148bcc7 | clean | - |
| 5 | e41eb41bb | clean | 2026-10-22-card-1464-store-aware-test-helpers-plan.md (A) |
| 6 | 7d09503c2 | clean | 2026-10-22-card-1489-build-reuse-plan.md (A) |
| 6 | cc1156d01 | clean | - |
| 7 | b0fac8217 b0ccfd1ca c88082a68 | clean | - |
| 7 | **adapt** | `AgentTaskWorktreeBaseSyncDebtTests.cs:24` GitProcessSpawnLimit -> ProcessSpawnLimit (as briefed) | - |
| 8 | 8d407e723 | clean | - |
| 8 | 520d2cd41 | clean | 2026-10-22-held-model-explicit-dispatch-plan.md (A) |
| 8 | 5602e7bfd | clean | - |
| 8 | 42e75c3d9 | **conflict, plan docs only**: modify/delete on 2026-10-22-held-model-explicit-dispatch-plan.md | held-model-explicit-dispatch-plan.md (M) |
| 9 | 0dd0f8551 | clean | 2026-10-22-card-1474-nudge-after-report-plan.md (A) |
| 9 | 57f78333c 060c15d01 | clean | - |

The two conflicts are modify/delete on plan docs that the recipe already left out of an earlier
pick in the same set. No code hunk conflicted, so no code was hand-resolved.

### Adaptation patches (exact)

```diff
--- a/tests/Antiphon.Tests/Application/LandReplacementApprovalTests.cs
+++ b/tests/Antiphon.Tests/Application/LandReplacementApprovalTests.cs
@@ -16,7 +16,7 @@ namespace Antiphon.Tests.Application;
 /// resolution, the operation factory and the protocol through the real git fixture.
 /// </summary>
 [Category("Integration")]
-[ParallelLimiter<GitProcessSpawnLimit>]
+[ParallelLimiter<ProcessSpawnLimit>]
 public sealed class LandReplacementApprovalTests
 {
     [Test]
--- a/tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseSyncDebtTests.cs
+++ b/tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseSyncDebtTests.cs
@@ -21,7 +21,7 @@ namespace Antiphon.Tests.Application;
 /// </summary>
 [Category("Integration")]
 [Category("Slow")]
-[ParallelLimiter<GitProcessSpawnLimit>]
+[ParallelLimiter<ProcessSpawnLimit>]
 public class AgentTaskWorktreeBaseSyncDebtTests
 {
     private const string AbsentSha = "0123456789abcdef0123456789abcdef01234567";
```

After both adaptations, `git grep GitProcessSpawnLimit -- '*.cs'` on the bundle returns nothing.

## Build (bundle tip 0b9f36a6d, tree dbc76ebe9)

| Build | Command (through `scripts/build-slot.ps1`) | Result |
|---|---|---|
| B-1 (briefed) | `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-rcbundle/` (builds server and src/Antiphon.SessionRunner) | **Build succeeded, 0 errors**, 1109 warnings, 3m25s; slot lease d81d3e48 waited 61s |
| B-2 (extra, stated reason) | `dotnet build tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj --property:OutputPath=bin-rcbundle/` | **Build succeeded, 0 errors**, 43 warnings; lease 22be99b7 |

B-2 is outside the brief's single build. It was run because CARD-1489's `BuildSlotMetricsTests`
and `BuildSlotEndpointTests` live in Antiphon.SessionRunner.Tests, which Antiphon.Tests does not
reference.

Warnings in bundle-touched files fall on 11 lines: AgentTaskReplyService.cs:3721 (CS8604), nine
CS8604 lines in AgentTaskReplyIntegrationTests.cs, and BuildSlotEndpointTests.cs:124 (TUnit0015).
`git blame` traces every one of those lines to commits that are already ancestors of rc
ceb6e3d6b, so the bundle added no new warning on them.

All 29 `bin-rcbundle` directories were deleted afterwards.

### Bundle test classes that compile on rc

String presence in the built assemblies confirms each class below.

- **Antiphon.Tests (new):** ClaudeEffortByRoleDispatchTests, ClaudeEffortByRoleSettingsTests
  (1481); ClosingReportNudgeLifecycleTests (1474); HeldModelEscalationTests (1490);
  LandReplacementApprovalTests (1485); PhoneHomeTaskDispatchProjectionTests.DispatchSave partial
  (1471); AgentTaskDispatchBaseGuardTests.SyncDebt partial and AgentTaskWorktreeBaseSyncDebtTests
  (1480); TestHelpers/StoreAwareTranscript (1464).
- **Antiphon.Tests (modified):** AgentTaskReplyIntegrationTests (1465, 1474);
  CheckNoteDeliveryHandoffTests, PostLandMutationDeliveryTests (+CompletionRecovery),
  SessionMessageQueueDeliveryVerificationTests (+C561), TestHelpers/BridgeQueueHarness (1464);
  slow-tests-allowlist.txt (1480).
- **Antiphon.SessionRunner.Tests:** BuildSlotMetricsTests (new), BuildSlotEndpointTests
  (modified) (1489).

No class failed to compile. No test suite was run.

## Evidence diff

`pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef ceb6e3d6b78471db6831258699c493b8eb2d619b -HeadRef HEAD`
gives the following output and exits 0:
`EVIDENCE result commits=27 entries=0 violations=0 base=ceb6e3d6b78471db6831258699c493b8eb2d619b head=0b9f36a6d9edafe5261f47484b2f2959bd5919e0`

## EF migrations

None of the 27 commits touches `server/Migrations/` or `AppDbContextModelSnapshot.cs`. After the
bundle, rc's latest migration is still `20261020142734_AddStandingAutomaticFreshRecovery`, so
there is no ordering question inside the bundle or against rc.

Master carries four later migrations that the bundle does **not** need, as the build proves:
20261022015609_AddRuntimeActionConversationKey, 20261022074212_AddProviderEndpoints,
20261022095749_AddWorktreeResidueCursorLastOutcome and 20261022110953_AddPoolProviderProjection.

## Activation footprint

The bundle changes `server/` and `src/Antiphon.SessionRunner*` (CARD-1489 `GET
/build-slots/metrics`, BuildSlotBroker, BuildSlotMetricsRecorder). Activating it therefore needs a
**session-runner restart as well as the server**. The docs touched are agent-kinds.md,
antiphon-api.md, ops-http.md and testing-and-build.md.

## Pick recipe (mechanical)

Run it from any repository clone with `origin` fetched:

```bash
git fetch origin rc/2026-10-20 master
git worktree add --detach ../rc-bundle origin/rc/2026-10-20   # must be ceb6e3d6b78471db6831258699c493b8eb2d619b
cd ../rc-bundle
test "$(git rev-parse HEAD)" = ceb6e3d6b78471db6831258699c493b8eb2d619b
bash /path/to/rc-bundle-recipe.sh            # script below, saved verbatim
test "$(git rev-parse HEAD^{tree})" = dbc76ebe9601a72036d68c48bdebc5d4d31eba10   # proven tree
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef ceb6e3d6b78471db6831258699c493b8eb2d619b -HeadRef HEAD
# then build/test as the rc gate requires, and publish the bundle to rc per docs/release-gates.md
```

Expected recipe output: 23 `clean` lines, 2 `plan-conflict-dropped` lines (6b4b0137a, 42e75c3d9),
2 `adapt` lines and a final `bundle tip <sha>`. Any `STOP` line means rc or a pick has drifted
from this dry-run. In that case abort with `git cherry-pick --abort` and report the stop; do not
resolve it by hand.

`rc-bundle-recipe.sh`:

```bash
#!/usr/bin/env bash
# rc bundle recipe (CARD-1471,1485,1481,1465,1464,1489 S1,1480 S1,1490,1474) onto origin/rc/2026-10-20 ceb6e3d6b.
# Run inside a worktree checked out at origin/rc/2026-10-20. Stops on any unexpected conflict.
set -euo pipefail
PLANS=docs/superpowers/plans

# pick <sha>: cherry-pick -x; drop only the plan docs this commit touches (added or modified).
pick() {
  local s=$1 plans
  plans=$(git show --format= --name-only "$s" -- "$PLANS")
  if git cherry-pick -x "$s" >/dev/null 2>&1; then
    if [ -n "$plans" ]; then git rm -q -f -- $plans; git commit -q --amend --no-edit; fi
    echo "clean  $s${plans:+ (dropped: $plans)}"
  else
    local bad
    bad=$(git diff --name-only --diff-filter=U | grep -v "^$PLANS/" || true)
    if [ -n "$bad" ]; then echo "STOP   $s conflict outside plan docs: $bad"; exit 1; fi
    git rm -q -f -- $plans
    git -c core.editor=true cherry-pick --continue >/dev/null
    echo "plan-conflict-dropped $s (dropped: $plans)"
  fi
}

# adapt <msg> <file>...: rc lacks CARD-1421 cc4a5564d (GitProcessSpawnLimit).
adapt() {
  local msg=$1; shift
  sed -i 's/\[ParallelLimiter<GitProcessSpawnLimit>\]/[ParallelLimiter<ProcessSpawnLimit>]/' "$@"
  git commit -q -m "$msg" -- "$@"
  echo "adapt  $(git log -1 --format=%h) $*"
}

# (1) CARD-1471 (topological order: red test 0aa1473a9 is the parent of fix e7825b823)
pick 0aa1473a9; pick e7825b823
# (2) CARD-1485
pick fd06b3c35; pick d1bce8c01
adapt "test(CARD-1485): rc port - ParallelLimiter<ProcessSpawnLimit>; rc lacks CARD-1421 GitProcessSpawnLimit" \
  tests/Antiphon.Tests/Application/LandReplacementApprovalTests.cs
# (3) CARD-1481 (tasks 8333e231 + baa2812c, ClaudeEffortByRole)
pick 4bf0275ee; pick ce72659f9; pick 92a5e97eb; pick a57a77635; pick 98d47fa7b; pick ae4fa6f0c; pick 6b4b0137a
# (4) CARD-1465
pick cb148bcc7
# (5) CARD-1464
pick e41eb41bb
# (6) CARD-1489 S1
pick 7d09503c2; pick cc1156d01
# (7) CARD-1480 S1
pick b0fac8217; pick b0ccfd1ca; pick c88082a68
adapt "test(CARD-1480): rc port - ParallelLimiter<ProcessSpawnLimit>; rc lacks CARD-1421 GitProcessSpawnLimit" \
  tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseSyncDebtTests.cs
# (8) CARD-1490
pick 8d407e723; pick 520d2cd41; pick 5602e7bfd; pick 42e75c3d9
# (9) CARD-1474
pick 0dd0f8551; pick 57f78333c; pick 060c15d01

if git grep -n 'GitProcessSpawnLimit' -- '*.cs'; then echo "STOP   GitProcessSpawnLimit still referenced"; exit 1; fi
echo "bundle tip $(git rev-parse HEAD)"
```

## Remaining uncertainties

- Compilation only. No test was run on the bundle (by brief). The cards' Review-clean status was
  established on master bases, not on rc.
- The adaptation for CARD-1485 `LandReplacementApprovalTests` was not in the brief. It is the same
  mechanical swap as CARD-1480's (see memory note rc-lacks-git-process-spawn-limit).
- The CARD-1474 SHAs were listed as "landing on master". They are now on origin/master as
  29c0742bc / e8a654e46 / 25e8908dd (tip b0c0f89b8) with identical patch-ids.

## Not done, noted

- Picking the master-equivalent SHAs instead of the branch SHAs gives identical patches, so the
  choice is cosmetic: it only changes which SHA the `-x` trailer names.
