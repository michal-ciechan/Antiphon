# CARD-1811 Review round 1 (task 93bda762) — CLEAN

Subject: Code task 8fcf2612-7544-4a3d-abf3-b0d752e54f80, branch feat/card-task-8fcf2612,
tip 1164028afe42dc26daeb1f3a77ff6afb145bc65d (red 6d1ac8f7eea1ef1a209b1a2cf15863829bead475,
base 2bf29ee9dadbd08c3ae78c186e2af00a6deb290e). Master at review time
c599061752fce18db7e7e2ea64cd87d3b365fb02. Reviewed on server2 (Linux); nothing ran on the desktop.

## Verdict

CLEAN. No blocker, no regression. Land the Code owner at 1164028afe42dc26daeb1f3a77ff6afb145bc65d.
A Merge onto c59906175 needs NO re-pin: none of the pinned inputs changed on master since the base.

## Brief checks

1. Gate. `issue_does_not_print_the_token_and_dpapi_round_trips` starts with
   `if (!OperatingSystem.IsWindows()) Skip.Test(...)`; the Windows body below it is byte-identical to
   the base (diff is two added lines). `Skip.Test` behind `!OperatingSystem.IsWindows()` is the
   repository's established mechanism (DelegateScriptCapabilityTests:140, C0824DesktopLegacyReleaseNativeTests,
   AgentTaskLandPublicationTests:767). The sibling `error_paths_never_contain_the_token` has no gate and
   no row; it passed on Linux in CP-1. `scripts/capability.ps1` is not in the diff.
2. Policy row. Semantic diff base->tip of tests/test-execution-policy.json: exactly one added
   rc exclusion row (suite antiphon, class CapabilityScriptTests, methods [the DPAPI method],
   platform linux, owner CARD-1811) plus the policyHash; no other row or property changed.
   Key order suite,class,methods,platform,reason,owner matches the 44 other method-scoped platform
   rows (CARD-1415, CARD-1614 HostCustodyTests, CARD-1561). The file is not alphabetically ordered,
   so placement after the CARD-1415 rows carries no convention. Linux rows now 5; the CARD-1323
   harness counts only its own three.
3. Pins, recomputed by me with the repository's tools.
   - Tip: Get-NightlyPolicyHash = 9cf98d229e32ca52e76d6b8946856776c7c390e3027e2eae191f5042aca3b697
     (Read-NightlyExecutionPolicy accepts it); test-live-lane-run.ps1 -PrintInputDigest =
     b587b96f62c5d652f2de4db1488fabcd0d050a5b34d35aa83af2cca04d10f031; audit tool `digest`:
     witnessedCount=95 legacyCount=92 legacyDigest=25dae45aca52b4b8de5dee6bf4abb4ebcae13b455dab8018e9d64221d10313ff.
   - Base policy: witnessedCount=95 legacyCount=91 legacyDigest=be806b71aa6cdf11edc0961cb1babd782034c0556e5f2e985dbbe8f2f7d7592f,
     policyHash 7e54681b... (the "before" values are reproduced).
   - Merged scratch tree 318faaee9 (merge-tree 69a83d75c of c59906175 + 1164028af, no conflicts):
     policy JSON, test-live-lane-run.ps1 and NativeLinuxPolicyWitness.cs are byte-identical to the
     tip; all three pins recompute to the same values. `git diff --stat 2bf29ee9d..origin/master`
     over the pinned inputs is empty (only docs/testing-and-build.md changed, a non-pinned file).
     What a Merge must re-pin: nothing for this branch. If another land adds or edits an rc row or any
     live-lane protected input before this one, the Merge re-pins policyHash and PinnedInputDigest,
     and LegacyExclusionDigest only if that row is non-prefixed (not "Linux-only by construction:").
   - Judgement on the third pin: correct and unavoidable. NativeLinuxPolicyWitness adds every row to the
     legacy payload unless its key is a frozen witnessed key or it is `platform: windows` with the
     Linux-only prefix. A `platform: linux` DPAPI row can be neither, so 91 -> 92 records is the only
     honest outcome; the WitnessedRowKeys list is for Linux-native classes and was rightly left alone.
4. Land graph. `audit-land-verification-graph.ps1 -Check -ToolPath <Release dll>` on the merged scratch
   tree: exit 0, phase=pass2, approved; no refusal, no new property/construct line.
5. Red/green and mutants (detached scratch worktree, native build, each run leased):
   - Red commit 6d1ac8f7e: witness fails `rows.Length should be 1 but was 0` (1 failed).
   - Tip: whole class 3 total, 2 passed, 1 skipped (the DPAPI case with the CARD-1811 message).
   - Mutant A (row removed from the policy on disk): red, rows.Length 1 vs 0.
   - Mutant B (row platform -> windows): red, platform should be "linux" but was "windows".
   - Mutant C (Skip.Test lines deleted, rebuilt): red, `should throw SkipTestException but threw
     ShouldAssertException` (the case ran capability.ps1 on Linux and failed its exit-code assertion).
   All mutations restored; scratch worktrees removed.
6. Docs. release-gates.md paragraph sits with the other mixed-platform row records and states the
   facts correctly; testing-and-build.md OS-branch paragraph is accurate; the why-missed note's
   claims check out: the file had one prior commit (86902399c), the CARD-1043 plan row 444 calls it
   the "Windows-only DPAPI runner", the Unit lane is `[Category=Unit]` (class is Integration), the
   roster disposition is `exclude` with no runtime effect (no script or test reads sourceSha256),
   nightly and rc qualification are Windows, the Linux rc run is PlatformDiagnostic. The root-cause
   claim holds: `[SupportedOSPlatform("windows")]` is a CA1416 analyzer attribute; TUnit discovers
   and runs the method regardless, which is exactly what CARD-1808 observed on server2.

## Checkpoint run (tip, checkpoint tool, source clean, buildSource verified)

run 20261109-103822-3a3b, commit 1164028afe42dc26daeb1f3a77ff6afb145bc65d, 1 build, 6 rows GREEN,
`validate --expected-source-sha` -> CHECKPOINT SOURCE VALID rows=6. unlisted: none.

| CP | Filter | executed | passed | failed | skipped |
|---|---|---:|---:|---:|---:|
| CP-1 | /*/*/CapabilityScriptTests/(error_paths*)\|(C1811*) | 2 | 2 | 0 | 0 |
| CP-2 | /*/*/NativeLinuxPolicyWitness*/* | 15 | 15 | 0 | 0 |
| CP-3 | /*/*/ReleaseGatePlatformTests/* | 6 | 6 | 0 | 0 |
| CP-4 | /*/*/LiveLaneRunnerTests/* | 4 | 4 | 0 | 0 |
| CP-5 | /*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)/* | 49 | 49 | 0 | 0 |
| CP-6 | /*/*/(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)\|(C1729LayeringGuardTests*)/* | 18 | 18 | 0 | 0 |

The DPAPI case is not a checkpoint row because a skipped row fails receipt validation; its Linux
skip is asserted by the witness (CP-1) and observed directly (tip sanity run above).
Timing: wall 9m54s, build 309 s; slowest row CP-3 142 s. Nothing near the 30-minute bound.

## Other checks

- check-evidence-diff.ps1 2bf29ee9d..1164028af: commits=2 entries=0 violations=0.
- merge-tree --write-tree origin/master HEAD: clean (69a83d75cda4812ae8d29229f8a532220b763b3d).
- Unlisted runs, all leased through build-slot.ps1 and each with a reason: Checkpoints tool build
  (to run the plan), NativeLinuxMigrationAudit `digest` x2 (pin recompute), LandGraphAudit build +
  -Check (brief item 4), scratch-worktree Antiphon.Tests builds x3 and test runs x5 (red/mutant
  evidence). Pure-pwsh pin reads (Get-NightlyPolicyHash, -PrintInputDigest) are not drivers.
- The other three NativeLinuxPolicyWitness suites (agents-pty, session-runner, pty-host) were not
  rerun: they compile the same tests/Shared sources against the same policy bytes, and the new row's
  suite is antiphon; CP-2 covers the digest pin.

## Pending (not discharged by this round)

- Windows-native execution of the DPAPI case body at the tip (the Code report also names it as
  owed). The body is unchanged and the row keeps it Windows-required, so nightly / rc qualification
  executes it; this Linux review could not. Not a land blocker for a test-only gate.
- PCs stay pending for method-scoped SourceLanding Mutation; the three mutants above are review
  checks, not Mutation results.

## Advisory (card, never holds the land)

- DelegateScriptCapabilityTests (Integration, `[SupportedOSPlatform("windows")]`) has six tests
  that all call WriteBlob, which `Skip.Test`s off Windows, and no Linux rc exclusion row. Under the
  CARD-1415/1614 rule "a launched skip leaves the census incomplete" it needs a class-level
  `platform: linux` row (and the same three pins resealed). Pre-existing, outside CARD-1811's scope;
  the why-missed note's "cheapest pre-land check" grep finds it.
- tests/linux-test-roster.json still records CapabilityScriptTests with directTestMethods 2 and the
  old sourceSha256. No script or test reads those fields (frozen CARD-0590 admission record), so no
  effect; a roster refresh task may want to note it.
