# CARD-1780 Review round 2 (task 2abcd1c0)

Verdict: one blocker (D-2, a regression). D-1 is closed for every case round 1 probed. But the order that round 1
prescribed, and that the tip now uses, is wrong in one case real MSBuild handles differently: an in-solution
reference with `AdditionalProperties`. At 071a4fe8d7 that case was modelled correctly; at the tip it is wrong.
It is latent: no ProjectReference in the repository carries AdditionalProperties, and the digest is unchanged.

Subject: origin/feat/card-task-1590d06a, reviewed tip 4f00afe045b82fdf9ec6ac026864d9f3188ee3da (repair task
1590d06a, Grok Code). Original Code landing owner 81df14b8-3441-4f30-a352-3d4a487953c4. Report-only commit
1e353c7dd7 above it (`.antiphon/task-1590d06a.md`) was skipped. Reviewed range
071a4fe8d76bf962b812e418acde0907c4283192..4f00afe045b82fdf9ec6ac026864d9f3188ee3da (3 commits, 2 files).

## D-2 (blocker, regression): AdditionalProperties on an in-solution reference is overwritten by the solution mapping

- Where: tools/Antiphon.LandGraphAudit/SolutionAudit.cs:320-345 (`ReferenceGlobals`). The
  `SetConfiguration`/`SetPlatform`/`SetTargetFramework`/`AdditionalProperties` loop runs first. The member branch then
  overwrites `Configuration`/`Platform` with the solution mapping. The comment at :314-319 says "Set* and
  AdditionalProperties go on first".
- Failure: a real Release solution build, leased on SDK 10.0.403. The scratch solution has App and Outside, both
  members, with `<ProjectReference Include="../Outside/Outside.csproj"><AdditionalProperties>Configuration=Debug</AdditionalProperties></ProjectReference>`.
  Outside builds into **bin/Debug** for App's reference, beside its own bin/Release from the solution.
  Controls: a plain reference builds Release only, and `SetConfiguration=Debug` builds Release only (that is round 1's
  member-setcfg result, reproduced). The tip's audit tool ran against the same shape with
  `AdditionalProperties Configuration=Release` under the Debug|Any CPU mapping, and printed
  `member-configuration: Outside/Outside.csproj configuration=Debug platform=AnyCPU`. The build hands that
  reference Release. At 071a4fe8d7, Set*/AdditionalProperties went last, so the old order gave Release (correct).
- Why: the MSBuild task applies its `Properties` parameter first. That parameter carries `SetConfiguration`/`SetPlatform`,
  which AssignProjectConfiguration has already overwritten with the mapping. The item's `AdditionalProperties` comes
  next and overrides those values. `RemoveProperties` is last. So AdditionalProperties loses only to removal, which
  covers the non-member case and GlobalPropertiesToRemove. It does not lose to the mapping. This is D-1's defect
  class (the audit evaluates a configuration the build never uses). Round 1 treated it as a blocker even though it
  was latent. Round 1's own probe table had no member + AdditionalProperties row, so its prescribed order was
  wrong and the repair followed it. Latent: `git grep` finds no AdditionalProperties, SetConfiguration or
  SetPlatform on any ProjectReference. The 13 real `member-configuration:` lines come from
  `GlobalPropertiesToRemove=WebPublishProfileFile` (Microsoft.NET.Sdk.Publish.targets:197) and the
  `SetTargetFramework` at Antiphon.Tests.csproj:98, and all of them read `configuration=Debug platform=AnyCPU`.
- Fix (Code owner 81df14b8; two files; no manifest change): in `ReferenceGlobals`, apply
  `SetConfiguration`/`SetPlatform`/`SetTargetFramework` first. Then apply the member mapping overwrite. Then apply
  `AdditionalProperties`. Last, remove the non-member `Configuration`/`Platform` and then the item's
  `GlobalPropertiesToRemove`. Fix the comment to match. Add a witness case `member-additional-properties` (Outside in
  the solution, `<AdditionalProperties>Configuration=Release</AdditionalProperties>`). It expects
  `member-configuration: Outside/Outside.csproj configuration=Release platform=AnyCPU` and no `outside-solution:`
  line, and it is red at 4f00afe04. The six existing cases keep their expectations. Round 3 is diff-only, blockers only.

## D-1 brief checks

1. Order: Set*/AdditionalProperties first, then membership, then GlobalPropertiesToRemove last. This holds at the
   tip and matches all six round-1 probes (outside, member, setcfg, addprops, member-setcfg, task). It is wrong only
   for the unprobed member + AdditionalProperties case (D-2).
2. The flipped `set-configuration` case is now `configuration=unset platform=unset`. Three cases were added:
   `additional-properties` (non-member, unset), `member-set-configuration` (expects the mapping, Debug) and
   `remove-configuration` (GlobalPropertiesToRemove beats the mapping). All match real MSBuild. Old-order red: in a
   detached worktree at 8f9740979f (the final witness on the old order, with the new reporting line), one leased build
   and one leased run of the method gave total 6, failed 4 (set-configuration, additional-properties,
   remove-configuration, member-set-configuration) and succeeded 2 (outside, member), exit 2. So the witness goes red
   at the old order.
3. The new `member-configuration:` output line is diagnostic only: `-Check` still says approved, and the digest is
   unchanged.
4. `land-graph -Check` (this worktree, `-ToolPath` the Debug evaluator built here): exit 0, `approved`,
   `digest=1C65008FF3E0A137F356ABE424E92AAD3A985D8980FC438D6EC027097C602B3B count=71`. It still prints exactly the
   three `outside-solution:` lines. `-Update` was not run.
5. A-1 comments: accurate. DockerStack.Fixture is a plain ProjectReference from Antiphon.Tests. LandGraphAudit.Core is
   a plain ProjectReference from LandGraphAudit. TimeoutAudit is referenced from Antiphon.Tests, and
   TimeoutAudit.Tests is `not-built` (outside the closure). None of the three is a StagedHelper. A-2 was not folded;
   it stands as an advisory for a card.
6. Merge with current master 584224f0e: `git merge-tree --write-tree origin/master 4f00afe045` gives tree caba8ebff6,
   exit 0, no conflicts; HEAD 1e353c7dd gives tree 77e4ad3c98, exit 0. Master since base f54741ef3 changes neither
   Antiphon.sln nor either land-graph record, and changes no .csproj/.props/.targets, so the digest still holds after
   rebase. Master does touch LandGraphEvaluationTests.cs, the StartRefGit dispose change, without a textual conflict.
7. check-evidence-diff: 071a4fe8d7..4f00afe04 commits=3 entries=0 violations=0, exit 0. f54741ef3..1e353c7dd
   commits=9 entries=1 violations=0, exit 0.

## Verification (server2 Linux, UseAppHost=false, TEMP=/tmp, every driver leased)

| Row | Lease / run | Result |
|---|---|---|
| CP-1 `tests/Antiphon.Tests -> bin-rv1780r2/` `/*/*/LandGraphEvaluationTests/*` | run 20261108-160333-efc8 at 4f00afe04 (detached worktree), build=ok | executed=154 passed=154 failed=0 skipped=0; all six C1780 cases and C1780_RealRepository passed; 731 s |
| CP-2 reuse CP-1, guard batch `(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)` | same run, build=reused | executed=50 passed=50 failed=0 skipped=0; 121 s |
| Receipt | `validate --expected-source-sha 4f00afe04...` | CHECKPOINT SOURCE VALID rows=2; sourceState=clean buildSource=verified; unlisted: none; outputs deleted |
| Checkpoint tool bootstrap | 85c77115, 7 s | 0 errors |
| Audit tool build (this worktree) | 147924ba, 9 s | 0 errors |
| Old-order red: Antiphon.Tests build at 8f9740979f | d1b4ac28, 263 s | 0 errors |
| Old-order red: C1780 method | f22c8bb5, 38 s | 6 total, 4 failed, 2 passed (expected red) |
| Real MSBuild probes plain / addprops / setcfg (member, Release sln) | cb7c8e3a / 4698dd07 / 5f5e5363 | Release / Debug+Release / Release |
| `-Check` | 719988f9, 37 s | exit 0, digest 1C65008F... count 71 |

The brief expected 147 tests in LandGraphEvaluationTests. That count is stale: round 1 ran 151, and this repair
adds three cases, so 154. Scope: this ran the brief's Final selection in full (touched class, guard batch, `-Check`,
merge-tree, evidence-diff, old-order red), and it has a validated receipt at the reviewed SHA. The whole Unit lane
is the nightly/rc backstop, as in round 1. There are no PCs, so SourceLanding Mutation still owns PC discharge.
Evidence copies are in the session scratchpad `evidence/` folder (report.json, report.md, CP-1.trx).
