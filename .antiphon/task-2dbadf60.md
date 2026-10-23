CARD-1545 implementation is pushed; ordinary verification remains incomplete because R-1 fails at both the candidate and task base. Do not land this task yet.

Original Code / landing owner: 2dbadf60-f236-4086-ad24-58a6cd57ea1e.
Card: 76a139bb-6888-492d-8df2-c6c1e2b7a2cf (CARD-1545), board 8988ca03-7414-47ad-b0b6-51556c701703.
Branch: feat/card-task-2dbadf60. Worktree: /work/worktrees/task-2dbadf60.
Base: c51023b99b3fcccd222ddbf8137de8c203e04760.
Implementation tip: 311c227321ebd41ad18588e06eb7fc7bd5d7daee.
This report is committed separately; the final progress line supplies its pushed tip. Do not relabel earlier test receipts as that report commit.
Plan/evidence design: docs/superpowers/plans/2026-10-23-card-1545-project-selection-slice.md.
Round: Final, with the brief's explicit minimal selection; ordinaryScopeCompleted: Incomplete (R-1).
Restart: none. This is a master-only feature slice; no RC pick/compile is proposed. No startup path changed, so CARD-1526 native restart probe is not applicable.

Implemented the first safe slice: scripts/lib/rc-project-selection.ps1 reads pinned Git snapshots, maps paths to owning projects, closes reverse ProjectReference edges at both commits, includes linked source/content/resource consumers and literal imports, and emits deterministic project/category/batch metadata. scripts/select-rc-project-batches.ps1 exposes a create-only JSON manifest. scripts/lib/release-selection.ps1 adds projectProposal to baseline-backed non-Full intents. tests/rc-project-categories.json records all 48 C# projects, seven whole-project native qualification batches and six reasoned harness-only TUnit executables. Mixed projects keep all relevant categories; the git handler and provider-specific assemblies are not extracted. Unknown ownership, unresolved graph inputs, cycles, missing TUnit dispositions and shared build/scripts/policy changes propose Full.

The proposal always records effectiveMode=Full, coverageScope=Full, gateCredit=false and executionEvidence=false. Existing release execution still owns the full nine-suite profile. This slice adds no baseline reuse, qualification admission, UID discovery integration or scoped gate credit. Shadow/full rollout comparison and equivalent-delta qualification policy remain later CARD-1545 work. Documentation: docs/release-gates.md.

Verification outcomes:

| ID | Actual outcome |
|---|---|
| V-1 | Passed: messaging owners/downstream closure, exact batches/categories, manifest closure and determinism. CP-1 first failed the intended assertions; CP-2 and CP-5 passed. |
| V-2 | Passed: unknown paths, shared inputs, dynamic references and missing category ownership force Full. CP-5 includes its whole method. |
| V-3 | Passed: linked consumers, deleted ownership, literal old/new graph references and cycles. CP-5 includes its whole method. |
| V-4 | Passed manual CLI census at 311c227321ebd41ad18588e06eb7fc7bd5d7daee: 48 projects, 13 TUnit executables, 7 native batches, 6 harness-only dispositions, no fallback reasons. Unchanged delta is an empty Scoped proposal with zero credit. Real messaging node records 16 downstream projects, including server, messaging tests, E2E and Antiphon.Tests. Changed messaging selection is independently asserted by V-1's committed fixture. |
| V-5 | Passed: copied literal/wildcard fixture inputs, imported references and test-batch completeness. CP-4 had the intended 3 failed / 1 passed assertions; CP-5 passed the TUnit method; CP-7 reran the final changed PowerShell body with 4 passed / 0 failed assertions. |
| V-6 | Passed: bracket characters remain literal in linked filenames. CP-6 failed only glob-consumer (3 passed / 1 failed), then CP-7 passed all four assertions including both wildcard and bracket variants. |
| R-1 | FAILED / INHERITED / UNRESOLVED. ChangeAwareReleaseTests.C1313_ModeCredit: 1 executed, 0 passed, 1 failed, no skips. All four mode assertions fail on unmatched policy projects Antiphon.TestTiming.Probe and scripts/fixtures/nightly/c1375-probe/Probe.csproj. Qualification-root preservation, selector fallback and unqualified-no-credit assertions pass. |

R-1 base attribution used a detached worktree at c51023b99b3fcccd222ddbf8137de8c203e04760 and the exact test-change-aware-release.ps1 -Case C1313_ModeCredit entrypoint. It reproduced 3 passed / 4 failed assertion rows and the same two unmatched projects. Raw output: .antiphon/c1545-base-mode-credit.log. slot=granted waited=0s held=123s. Its FilterHost came from the verified CP-2 build; that helper's source is unchanged over base..implementation tip. This was a direct PowerShell failure witness, not a base TUnit/compiled-build green claim. The temporary base worktree was removed normally after completion. tests/test-execution-policy.json is unchanged by this task. The brief excludes another task's policy/fixture area, so no policy repair or assertion relaxation was made.

The caller must commission the policy owner's Debug/repair, integrate that repair through its authorized workflow, and rerun CP-3's exact filter before ordinary Review. No baseline-red waiver or land approval is claimed. No IDs are deferred to a later Final round: R-1 is recorded as failed, and all other selected IDs above have their actual outcomes.

Fresh TRX inspection confirmed literal class/method identities and nonzero counts. CP-5 includes exactly C1545_Closure, C1545_FailClosed, C1545_HistoricalEdges and C1545_ConsumerSafety, 4 passed / 0 failed / 0 skipped. Their durations were respectively 6.9460855, 5.0980793, 4.2692300 and 8.5361423 seconds. CP-3 contains exactly C1313_ModeCredit, failed in 170.9594820 seconds. CP-5 source validation returned CHECKPOINT SOURCE VALID for 257acdc2d453857e2c01b87f5598391508795ba1. CP-7 source validation returned CHECKPOINT SOURCE VALID for 311c227321ebd41ad18588e06eb7fc7bd5d7daee. CP-7 is a command row: buildSource=notApplicable, four assertion rows, no invented TUnit count.

Only the PowerShell consumer body/matcher changed after CP-5; CP-6/CP-7 directly execute that exact body. The compiled wrapper is unchanged. Do not claim the whole TUnit class was rerun at the final matcher SHA.

Unlisted work and cost:

- One bootstrap build was necessary to obtain the checkpoint executable: build-slot.ps1 -Label c1545-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1545-tool/ --nologo -v:q. Granted slot, waited=0s, held=6s; 0 errors, 4 existing warnings. All three test-project builds were checkpoint-owned and isolated.
- The one exact base harness probe above established inherited attribution without compiling irrelevant unchanged C# code. No assembly or Unit lane was run; the direct brief explicitly selected narrow testing.
- Manual V-4 used build-slot.ps1 around select-rc-project-batches.ps1. Initial census at 0f2f2ce7e803105d6a016d64fe194c0bfdf4c8f7 had slot=granted waited=0s. The required final census after graph changes had slot=granted waited=16s held=21s. JSON/log outputs remain ignored at .antiphon/c1545-final-manual.json and .antiphon/c1545-final-manual.log.
- Two admissions launched no driver: an incorrect expected SHA and a temporarily separated checkpoint table. Both were corrected. Intermediate commit 64b44bb29dcad530e78f8c4b47370cb0cf678dba contained a scripted-edit syntax defect, corrected in 257acdc2d453857e2c01b87f5598391508795ba1 before the successful compiling checkpoint. Failed histories are preserved.
- CP-5 exceeded the provisional six-minute estimate: native MSBuild log 9m41.71s; checkpoint build accounting 646.4560761s; row slot wait 90s; testsWall 24.8765684s; hostWall 30.1666734s. Host load sampled during that build was 63.46 / 44.72 / 31.45 on 24 reported cores. No run exceeded 30 minutes, no timeout was widened, and no assertion/retry was loosened. Fixture filesystem/CLI subdivisions remain unknown/remainder; no DB/container/app-host fixture was started for the planner. These are single provisional samples, not p95 estimates.
- No repetitions after green. Later executions followed actual test/implementation changes and targeted their affected body.

All raw CHECKPOINT lines and phase lines below are copied without editing. JSON receipts/TRX/logs remain ignored in their original paths.

Evidence: .antiphon/checkpoints/20261023-203301-9dfb/report.md

```text
CHECKPOINT CP-1 commit=688ec2dde764a31bd43f338cfd74d19355362b9d build=ok filter=/*/*/RcProjectSelectionTests/* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-2dbadf60/.antiphon/checkpoints/20261023-203301-9dfb/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=688ec2dde764a31bd43f338cfd74d19355362b9d sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=266.8992613s startup=3.4740569s testsWall=16.1871315s teardown=1.4999948s hostWall=21.1611937s
```

Evidence: .antiphon/checkpoints/20261023-204056-7252/report.md

```text
CHECKPOINT CP-2 commit=0f2f2ce7e803105d6a016d64fe194c0bfdf4c8f7 build=ok filter=/*/*/RcProjectSelectionTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-2dbadf60/.antiphon/checkpoints/20261023-204056-7252/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=0f2f2ce7e803105d6a016d64fe194c0bfdf4c8f7 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=241.8495664s startup=7.2893211s testsWall=33.5270096s teardown=1.0726628s hostWall=41.8890107s
CHECKPOINT CP-3 commit=0f2f2ce7e803105d6a016d64fe194c0bfdf4c8f7 build=reused filter=/*/*/ChangeAwareReleaseTests/C1313_ModeCredit executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-2dbadf60/.antiphon/checkpoints/20261023-204056-7252/rows/CP-3/run.trx slot=granted waited=45s dirty=0 source=0f2f2ce7e803105d6a016d64fe194c0bfdf4c8f7 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=45s build=0s startup=8.0711536s testsWall=170.959482s teardown=0.626201s hostWall=179.6568368s
```

Evidence: .antiphon/checkpoints/20261023-205349-245f/report.md

```text
CHECKPOINT CP-4 commit=cdecd8bd3b6e55a4977b4489014061913bc744e3 build=n/a filter=pwsh -NoProfile -File scripts/test-rc-project-selection.ps1 -Case C1545_ConsumerSafety -ResultsDirectory .antiphon/c1545-consumer-red executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=cdecd8bd3b6e55a4977b4489014061913bc744e3 sourceState=clean buildSource=notApplicable
```

Evidence: .antiphon/checkpoints/20261023-205707-af45/report.md

```text
CHECKPOINT CP-5 commit=257acdc2d453857e2c01b87f5598391508795ba1 build=ok filter=/*/*/RcProjectSelectionTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-2dbadf60/.antiphon/checkpoints/20261023-205707-af45/rows/CP-5/run.trx slot=granted waited=90s dirty=0 source=257acdc2d453857e2c01b87f5598391508795ba1 sourceState=clean buildSource=verified
PHASES CP-5 slotWait=90s build=646.4560761s startup=4.4734099s testsWall=24.8765684s teardown=0.8164333s hostWall=30.1666734s
```

Evidence: .antiphon/checkpoints/20261023-211250-863e/report.md

```text
CHECKPOINT CP-6 commit=d17d675a6a589df599334177eb9bb178e70e2b4d build=n/a filter=pwsh -NoProfile -File scripts/test-rc-project-selection.ps1 -Case C1545_ConsumerSafety -ResultsDirectory .antiphon/c1545-path-red executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=d17d675a6a589df599334177eb9bb178e70e2b4d sourceState=clean buildSource=notApplicable
```

Evidence: .antiphon/checkpoints/20261023-211330-2556/report.md

```text
CHECKPOINT CP-7 commit=311c227321ebd41ad18588e06eb7fc7bd5d7daee build=n/a filter=pwsh -NoProfile -File scripts/test-rc-project-selection.ps1 -Case C1545_ConsumerSafety -ResultsDirectory .antiphon/c1545-path-green executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=90s dirty=0 source=311c227321ebd41ad18588e06eb7fc7bd5d7daee sourceState=clean buildSource=notApplicable
```

Pending SourceLanding Mutation inventory (none executed/discharged):

| PC | Guard / variants |
|---|---|
| PC-1 | C1545_Closure: downstream edge omission / batch or category loss. |
| PC-2 | C1545_FailClosed: unknown path/category, shared-input, dynamic-reference or cycle fallback loss. |
| PC-3 | C1545_HistoricalEdges and C1545_ConsumerSafety: old graph, deleted owner, linked source and imported-reference variants. |
| PC-4 | C1313_ModeCredit: release Full override / unqualified credit variants; ordinary R-1 must first pass. |
| PC-5 | C1545_ConsumerSafety: literal and wildcard content-consumer omission. |
| PC-6 | C1545_ConsumerSafety: omitted TUnit batch silently accepted. |
| PC-7 | C1545_ConsumerSafety: bracketed literal filename reinterpreted as a wildcard. |

Mutation owns deliberate mutants, exact method-scoped red/restore/green and missing-control discovery. No landing O/L/R exists for this branch. Keep this original Code task as landing owner after ordinary verification and Review complete.

Before this report-only commit, the full-range evidence guard passed (8 commits, 0 violations). git fetch origin master followed by git merge-tree --write-tree origin/master HEAD succeeded at implementation tip 311c227321ebd41ad18588e06eb7fc7bd5d7daee against origin/master b693ad16d4ad3df92063f97ef859b70d43fae3ce; resulting tree 97104284f21b71ee6adeab9d9051e5ab9397f155. Final report-tip checks/readback are supplied in the final response, without relabeling test provenance.

Cleanup completed after all owned runs exited: the tool removed bin-c1545-final; this task removed its retained bin-c1545-red, bin-c1545-green and both bootstrap bin-c1545-tool roots after containment, regular-directory and tracked-file checks. No alternate output directory remains. No runner/server restart or deployment is required by this slice.

Implementation/history commits (each pushed):

```text
688ec2dde764a31bd43f338cfd74d19355362b9d test(release): add CARD-1545 project selection defect witnesses
0f2f2ce7e803105d6a016d64fe194c0bfdf4c8f7 feat(release): propose shadow project batches from committed dependency graphs
858effab756df7f04440915fc5c9b173ac4fb53f test(release): expose copied fixture and missing batch selection defects
cdecd8bd3b6e55a4977b4489014061913bc744e3 docs(testing): keep consumer checkpoint rows in the closed table
64b44bb29dcad530e78f8c4b47370cb0cf678dba fix(release): include copied inputs and refuse incomplete test batch inventories
257acdc2d453857e2c01b87f5598391508795ba1 fix(release): correct project input resolver after scripted edit
d17d675a6a589df599334177eb9bb178e70e2b4d test(release): guard literal bracket names in copied project inputs
311c227321ebd41ad18588e06eb7fc7bd5d7daee fix(release): keep brackets literal when matching project input globs
```

Next: code. Repair inherited R-1 policy dispositions through the owner, rerun the exact CP-3 selection, then ordinary Review. Do not land while verification remains incomplete.
