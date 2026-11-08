# CARD-1780 Review round 1 (task 437a6c9a)

Verdict: one blocker (D-1), two advisories. The membership-before-walk fix, the twelve solution
members, the three projects left out, the manifest refresh and the Release solution build are all
right. The audit's exception for explicit `SetConfiguration` / `SetPlatform` / `AdditionalProperties`
on an out-of-solution reference models a behaviour real MSBuild does not have, and the witness's
third case pins that wrong model.

Subject: origin/feat/card-task-81df14b8 at 071a4fe8d76bf962b812e418acde0907c4283192 (Grok Code,
task 81df14b8-3441-4f30-a352-3d4a487953c4). Base f54741ef3bc08baa0493f9e0bebd2425acab6fb5. Red
394296bc463b74c411ee7c1cc1496fea633affd7. Reviewed range f54741ef3..071a4fe8d7 (5 commits, 6 files).

## Findings

### D-1 (blocker): explicit configuration metadata on an out-of-solution reference is modelled as kept; MSBuild drops it

- Where: tools/Antiphon.LandGraphAudit/SolutionAudit.cs:304-333 (`ReferenceGlobals`: the membership
  block runs before the `SetConfiguration` / `SetPlatform` / `SetTargetFramework` /
  `AdditionalProperties` loop; comment "Explicit SetConfiguration / SetPlatform / AdditionalProperties
  below still win"); tests/Antiphon.Tests/Application/LandGraphEvaluationSolutionMembershipTests.cs:13
  (`[Arguments("set-configuration", "configuration=kept platform=unset")]`).
- Failure: real Release solution builds on this SDK (10.0.403), each a leased scratch solution with
  App (member) referencing Outside:

  | Probe | Reference | Outside built in | Audit line at the tip |
  |---|---|---|---|
  | outside | plain, Outside not in .sln | bin/Debug | `configuration=unset platform=unset` (right) |
  | member | plain, Outside in .sln | bin/Release | none (right) |
  | setcfg | `<SetConfiguration>Configuration=Release</SetConfiguration>`, Outside not in .sln | bin/Debug | `configuration=kept platform=unset` (wrong) |
  | addprops | `AdditionalProperties="Configuration=Release"`, Outside not in .sln | bin/Debug | would be `kept` (same code path) |
  | member-setcfg | `<SetConfiguration>Configuration=Debug</SetConfiguration>`, Outside in .sln | bin/Release | audit assigns Debug (Set* overrides the mapping) |
  | task | `<MSBuild Properties="Configuration=Release" RemoveProperties="Configuration" />`, parent Debug | bin/Debug | n/a: RemoveProperties wins over Properties |

  AssignProjectConfiguration appends `Configuration;Platform` to GlobalPropertiesToRemove for every
  unresolved reference and the MSBuild task applies RemoveProperties after Properties and
  AdditionalProperties, so no explicit metadata restores them; for a resolved reference it
  overwrites SetConfiguration/SetPlatform with the solution mapping.
- Why: the card's A-1 ask is an audit that sees "a producer builds in the wrong configuration". The
  tip models and pins the opposite of the build for explicit metadata, which is the same defect class
  (the audit evaluates a configuration the build never uses). Latent today: no ProjectReference in the
  repository carries SetConfiguration, SetPlatform or AdditionalProperties (grep over *.csproj,
  *.props, *.targets), so the digest and the fifteen real cases are unaffected. It is the first
  thing the next author reaches for when a helper builds in the wrong configuration, and both the
  tool and the test would then tell them it worked.
- Fix (Code owner 81df14b8; two files, no manifest change since tool code is not a hashed
  definition): in `ReferenceGlobals` apply the Set*/AdditionalProperties loop first, then the
  membership rule (member: Configuration and Platform := the solution mapping; non-member: remove
  both), then the item's GlobalPropertiesToRemove last. Change the witness's third case to
  `configuration=unset platform=unset`; optionally add an `AdditionalProperties` case and a
  member-with-SetConfiguration case that expects the mapping. Fix the comment. Round 2 is diff-only.

### Advisories (cards, not land holds)

- A-1: tests/Antiphon.Tests/Application/LandGraphEvaluationSolutionMembershipTests.cs:56-61 pins
  DockerStack.Fixture, LandGraphAudit.Core and TimeoutAudit as the outside set without saying why
  each stays out. The rule is stated only in commit 16f41344cb and the LandGraphAudit csproj comment.
  Verified here: all three are consumed only through plain `ProjectReference` (Antiphon.Tests;
  LandGraphAudit -> Core; TimeoutAudit.Tests, itself outside the closure), none is a `StagedHelper`,
  and nothing calls GetTargetPath on them, so the choice is right. One comment line per project.
- A-2: tools/Antiphon.LandGraphAudit/SolutionAudit.cs:159-168 records a solution entry with no
  mapping for Debug|Any CPU as a member with Configuration=Debug. MSBuild skips such an entry and
  clears references to it. All 51 entries map today; a future entry without a mapping would be
  modelled as built and referenced in Debug.

## Brief checks

1. Clearing modelled, membership before the walk: yes for plain references. f987d5619 fills
   `memberConfiguration` from `ProjectsInOrder` before any `Evaluate`; against the real repository
   the tool prints exactly the three expected `outside-solution:` lines, so all 51 members matched
   on Linux through `MsBuildFullPath`. Explicit-metadata exception: D-1.
2. Twelve members (OwnedChild, ChannelOutbound.Probe, Checkpoints.FilterHost/GitFixture/
   LifecycleHost/RepeatHost, CustodyTestChild, FileUseTestChild, HostLoad.Probe,
   NativeLinuxPolicy.Probe, TestTiming.Probe, LandGraphAudit): each is a `StagedHelper` producer
   whose `CopyStagedHelpers` target calls GetTargetPath with `Configuration=$(Configuration)`, the
   one consumer shape that reads a configuration-specific output path. In the Release solution build
   each produced bin/Release only. 51 distinct project GUIDs; each new GUID has 12 configuration
   lines and one nesting line; eleven nest under tests `{0AB3BF05}`, LandGraphAudit under tools
   `{07C2787E}`, OwnedChild under a new root folder `scripts` `{23D52B2D}`; LF endings; the C# type
   GUID matches siblings. The `Antiphon.LandGraphAudit.csproj` comment was updated to match.
3. Three left out: correct (A-1 for the missing comment). They built to bin/Debug only in the
   Release solution build, which is harmless through a plain ProjectReference.
4. Digest: `-Check` from this worktree with the Release-built evaluator exits 0 (`approved`,
   `digest=1C65008FF3E0A137F356ABE424E92AAD3A985D8980FC438D6EC027097C602B3B count=71`), equal to the
   manifest and the evaluation record; SDK 10.0.403 and pins 7a2586e58 unchanged. Commits 9edad1cfe6
   and 071a4fe8d7 touch only the two land-graph records. `-Update` was not run. A first `-Check`
   from a scratch worktree that held the Release bin/obj refused with "evaluating Antiphon.sln copy
   exceeded 5 s" (host load 44); its pass-1 digest already matched, and the clean rerun above and
   C1483_S5_CheckRequiresEvaluationRecord's real `-Check` inside CP-1 both pass.
5. C1483_S5: the Code report's single LandGraphEvaluationTests failure was
   C1483_S5_CheckRequiresEvaluationRecord's real `-Check` on the stale manifest; at the tip it passes
   inside CP-1 (151/151).
6. Witness and mutant: the test commit precedes the fix and the base tool prints no
   `outside-solution:` line, so two of three cases are red at base (Code: 2 failed, 1 passed) and the
   Code mutant (keep for outside) reported `configuration=kept platform=kept` red. The flag is derived
   from the globals actually passed to `Evaluate`, so it discriminates kept from unset. This review
   ran no mutant; D-1 changes the third case's expectation.
7. Release solution build (scratch detached worktree at the tip, leased): exit 0, 0 errors, 2078
   warnings, 6m06s under load 44 (3m02s at lower load in the CARD-1740 review). 54 projects produced
   output: 51 members plus the three outside references (was 39 + 3).
8. Touched class and guard batch: checkpoint run 20261108-130223-c40b, see below.
9. check-evidence-diff f54741ef3..071a4fe8d7: commits=5 entries=0 violations=0, exit 0.
   merge-tree --write-tree origin/master (b35fb137acde6e0c52e296dd6d7b4e345fe3d238) HEAD:
   5f1bec6f1248aeaef8bac7882ac826613f7b2ad8, exit 0, clean.

## Verification (server2 Linux, UseAppHost=false, TEMP=/tmp, every build or test driver leased)

| Row | Lease / run | Result |
|---|---|---|
| CP-1 `tests/Antiphon.Tests -> bin-rv1780/` `/*/*/LandGraphEvaluationTests/*` | run 20261108-130223-c40b, build=ok | executed=151 passed=151 failed=0 skipped=0; C1780 x4 and C1483_S5 x12 passed; 892 s |
| CP-2 reuse CP-1 `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)/*` | same run, build=reused | executed=50 passed=50 failed=0 skipped=0 (39+1+5+2+3); 147 s |
| Receipt | `validate --expected-source-sha 071a4fe8d7` | CHECKPOINT SOURCE VALID source=071a4fe8d7 rows=2; sourceState=clean buildSource=verified; unlisted: none; outputs deleted bin-rv1780/ |
| Checkpoint tool bootstrap | 0cb17d4e held 10 s | `dotnet build tools/Antiphon.Checkpoints`, 0 errors |
| Whole-solution Release build | 45f3ac76 held 367 s | exit 0, 0 errors, 54 projects |
| `-Check` from this worktree, `-ToolPath` the Release evaluator | 137d2580 held 55 s | exit 0 approved, digest 1C65008F... count 71 |
| `-Check` from the scratch worktree | 22a92730 held 42 s | pass-1 digest 1C65008F... count 71, then copy budget refusal (environmental) |
| Probes outside / member / setcfg / addprops / member-setcfg / task | six leases, 5-17 s each | see the D-1 table; all exit 0 |
| Audit tool at the tip against outside / member / setcfg fixtures | no build | unset+unset / none / kept+unset, all `approved` |
| check-evidence-diff, merge-tree | n/a | violations=0; clean |

Scope: the brief's Final selection (touched class, named guard batch, evidence-diff, merge-tree,
digest recompute, solution build, red/green and mutant evidence) ran in full with a validated
checkpoint receipt at 071a4fe8d7, so ordinaryScopeCompleted is Full and reviewedSourceClean is true.
The whole Unit lane did not run: docs/testing-and-build.md (Unit lane) assigns it to the nightly
backstop and the rc qualification run, not to per-task Final. The card has no plan, no `### Checkpoints`
table and no PCs; SourceLanding Mutation still owns PC discharge.

Cleanup: the detached scratch worktree was removed with `git worktree remove`; the checkpoint tool
deleted bin-rv1780/; no land-graph canary remains under /work/worktrees or the scratchpad.
