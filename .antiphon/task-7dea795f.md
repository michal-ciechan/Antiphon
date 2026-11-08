# CARD-1780 Review round 3 (task 7dea795f)

Verdict: CLEAN. D-2 is closed. `ReferenceGlobals` now applies Set*, then the in-solution mapping, then
AdditionalProperties, then the out-of-solution Configuration/Platform removal, then the item's
GlobalPropertiesToRemove. That order matches real MSBuild in all 13 leased probe shapes. There are no blockers and
no regressions.

Subject: origin/feat/card-task-87954929, reviewed tip 3a9224a85d31088c5c6d08e3ac2992a880059a10. That tip is code
tip a9c6171379 plus a report-only commit that adds `.antiphon/task-87954929.md`. Repair task 87954929 was written by
Grok Code. The original Code landing owner is 81df14b8-3441-4f30-a352-3d4a487953c4. Reviewed range:
4f00afe045b82fdf9ec6ac026864d9f3188ee3da..3a9224a85d31088c5c6d08e3ac2992a880059a10. It has 3 commits and touches
2 code files: SolutionAudit.cs and LandGraphEvaluationSolutionMembershipTests.cs.

## D-2: real MSBuild probes (SDK from global.json 10.0.204 rollForward, server2 Linux, one lease per build)

The scratch solutions have App and Outside. Outside's Directory.Build.props sets `Configuration=Unset` and
`Platform=UnsetPlat` only when the property is empty. Outside prints its Configuration and Platform before Build.
Control Z0 (Outside built alone) printed `Unset/UnsetPlat`, so the marker works. Each in-solution case also prints
the solution's own build of Outside with the mapping. The extra line is the configuration App's reference received.
Custom values are used so that "removed", "parent kept" and "metadata honoured" give different output.

| Case | Shape | Build | Reference received (real) | Model at tip | Lease |
|---|---|---|---|---|---|
| A1 | member, AP Configuration=Release | -c Debug | Release/AnyCPU | Release | 86ba17f1 |
| A2 | member, AP Configuration=Custom | -c Debug | Custom/AnyCPU | Custom | 9e9a5d63 |
| B1 | non-member, AP Configuration=Release | -c Debug | Unset/UnsetPlat | unset/unset | 02a01553 |
| B2 | non-member, AP Configuration=Custom | -c Release | Unset/UnsetPlat | unset/unset | 940cbd89 |
| C1 | member, SetConfiguration=Debug | -c Release | Release (mapping only) | mapping | 8495b876 |
| C2 | member, SetConfiguration=Custom | -c Debug | Debug (mapping only) | mapping | 2e7f9678 |
| D1 | member, SetConfiguration=Debug + AP Configuration=Custom | -c Release | Custom/AnyCPU | Custom | 4308a438 |
| D2 | non-member, SetConfiguration=Debug + AP Configuration=Custom | -c Release | Unset/UnsetPlat | unset/unset | 61a857c9 |
| E1 | member, GlobalPropertiesToRemove=Configuration + AP Configuration=Custom | -c Release | Unset/AnyCPU | unset/AnyCPU | 2701c694 |
| F1 | non-member, SetConfiguration=Custom | -c Release | Unset/UnsetPlat | unset/unset | c742fe89 |
| G1 | member, plain (control) | -c Release | Release only | mapping | 32eb2899 |
| H1 | non-member, plain (control) | -c Release | Unset/UnsetPlat | unset/unset | a1a96fae |
| P1 | member, AP Platform=x64 | -c Debug | Debug/x64 | Debug/x64 | 1104305a |

The audit tool ran against matching git fixtures with an Antiphon.sln and the Debug|Any CPU mapping. Each run was
leased. The tip tool was built in a detached worktree at 3a9224a85d (lease f6bd2cd3). The old-order tool was
built at the red-witness commit 73231205c (lease 81ac6b24).

| Fixture | Shape | Tip tool | Old-order tool (73231205c) | Real |
|---|---|---|---|---|
| TA | member AP Configuration=Release | configuration=Release platform=AnyCPU | configuration=Debug | Release (A1) |
| TD | member SetConfiguration=Release + AP Custom | Custom/AnyCPU | Debug | Custom (D1) |
| TE | member GPTR Configuration + AP Custom | unset/AnyCPU | unset/AnyCPU | Unset/AnyCPU (E1) |
| TP | member AP Platform=x64 | Debug/x64 | Debug/AnyCPU | Debug/x64 (P1) |
| TB | non-member AP Custom | outside-solution unset/unset | unset/unset | Unset (B2) |
| TC | member SetConfiguration=Release | Debug/AnyCPU | Debug/AnyCPU | mapping (C1/C2) |

Every tip result matches real MSBuild. Under the old order, TA prints `configuration=Debug`, but the new
`member-additional-properties` witness asserts
`member-configuration: Outside/Outside.csproj configuration=Release platform=AnyCPU`. So the witness goes red at
the old order, which matches the author's red run at 73231205c (7 cases, 1 failed). TD and TP fail under the
old order too.

Comments: the SolutionAudit.cs:314-320 comment is accurate. It says Properties carries Set*, the mapping has
already overwritten that, AdditionalProperties overrides the mapping, and removal comes last for non-members and
for GlobalPropertiesToRemove. E1, B*, D2 and F1 show that removal beats AdditionalProperties. The test header and
the new case comment are also accurate.

## Gates

- Checkpoint run 20261108-171449-a8aa used the plan extract `.antiphon/c1780-r3-review-plan.md`, which is ignored
  and not committed. It reported GREEN exit=0 with one build (5 min, lease a64d2f53) and `unlisted: none`.
  `validate --expected-source-sha 3a9224a85d...` printed `CHECKPOINT SOURCE VALID source=3a9224a85d31088c5c6d08e3ac2992a880059a10 rows=2`.
  - `CHECKPOINT CP-1 commit=3a9224a85d31088c5c6d08e3ac2992a880059a10 build=ok filter=/*/*/LandGraphEvaluationTests/* executed=155 passed=155 failed=0 skipped=0 ... sourceState=clean buildSource=verified` (620 s)
  - `CHECKPOINT CP-2 commit=3a9224a85d31088c5c6d08e3ac2992a880059a10 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=50 passed=50 failed=0 skipped=0 ... sourceState=clean buildSource=verified` (73 s)
  - The TRX shows all 7 `C1780_OutsideSolutionReferenceClearsParentConfiguration` cases and
    `C1780_RealRepositoryFlagsReferencesOutsideTheSolution` passed.
- `audit-land-verification-graph.ps1 -Check -ToolPath <tip Debug evaluator>` ran in the detached tip worktree
  (lease 1cd412dc, 31 s). It exited 0 with `approved` and
  `digest=1C65008FF3E0A137F356ABE424E92AAD3A985D8980FC438D6EC027097C602B3B count=71`, the same three
  `outside-solution:` lines, and 13 `member-configuration:` lines that all read Debug/AnyCPU. `-Update` was not run.
- `git merge-tree --write-tree origin/master <sha>` against master 5fef2c23b89414b8818aaf40db9ad738bce9524c:
  3a9224a85d gives tree 2d76c070f0 and a9c6171379 gives tree 299b281861. Both exit 0 with no conflicts, and nothing
  was merged. Master since base f54741ef3 (43 commits) changes no Antiphon.sln, .csproj, .props, .targets or
  tools/ file. It touches only LandGraphEvaluationTests.cs (the StartRefGit dispose change), with no textual
  conflict.
- check-evidence-diff: 4f00afe04..3a9224a85 gives commits=3 entries=1 violations=0, exit 0. f54741ef3..3a9224a85
  gives commits=11 entries=1 violations=0, exit 0.

## Advisory (do not hold the land)

- A-3: the repair report (`.antiphon/task-87954929.md`, Other gates) swaps two values. It says "master 299b28186 ...
  tree 5fef2c23b". In fact 5fef2c23b is master and 299b281861 is the merge tree of a9c617137 (`git cat-file -t`
  says tree). The brief's "Master moved (299b28186 at the author's check)" comes from that swap. Master did not
  move after the author's check.

Scope: this round ran the brief's Final selection in full: the touched class, the guard batch, `-Check`,
merge-tree, evidence-diff, the old-order red and the real-MSBuild probes. It has a validated receipt at the
reviewed SHA. As in rounds 1 and 2, the whole Unit lane is left to the nightly/rc backstop. There are no PCs, so
SourceLanding Mutation still owns PC discharge. Both detached worktrees were removed and no bin-c1780r3rev output
was left. Evidence copies are in the session scratchpad `evidence/` folder.
