# Task f3973a3a: Merge batch 10 (master only)

**Outcome:** both stages are cherry-picked and published to master by plain fast-forward pushes. There were no
conflicts. Stage A took 15 picks and stage B took 23. rc/2026-11-01 stayed at
`10a826b9d61f2c2cdfa29a43cac59fe360c99ea9` before, between and after the stages.

- Master before: `e18a1177d9e042ac5986cf8bdebc5b47abb7ca11` (confirmed with ls-remote).
- Master after stage A: `43ae7371d7e9e173e33b69efdc061ac2891c9d98` (confirmed with ls-remote).
- Master after stage B: `5376ac529987b9e7b2019773cb4d784cba8007bf` (confirmed with ls-remote).
- All picks used `git cherry-pick -x`. No merge commits, no force pushes. `git cherry` found no pick already
  on master. The 3 `-` lines on bffd3e70 are the S2a-1 originals below ec25d5f1, which were not in the range.

## Stage A picks (15)

P1 73a9b0f6 (range has 1 commit, 1 picked):
- b032722199a0f32e9d03da52d3611351508dcc82 -> a2b7534276211c9ef9859a512300880a79847564

P2 4aa6e6f8 (range has 5 commits, 5 picked):
- 4c05e82317ba4b4786ffae16ee617b282809e796 -> 436f1778f0c4f51761c6de04f6edacd3b7c1fbd4
- d76f2c27d889eabaf0a0b328defbe285c59140a8 -> ec45ed58deae36325cb1f6a8793639d805ff60f5
- db6fbdc6b4b0de659eee0d5ea2b1fdcdc4f9819d -> 71064f4a3307f79b570529aee05f984fc7198fc7
- 5e296c9613473dff0fcbd3464ed0390bfee03ea1 -> eca4b93da915d82dc524192e60080ff1d2d8b65c
- d4345c226ecef44b3aca333ac3d56a4ed55d7f70 -> c0fbdd7ca27275a8ff437f1a2cd45a9596e683cf

P3 e72099f7 (range has 3 commits, 2 picked; skipped report b208243f3eb59681387c5eaf3393360e7a5e1b0e):
- 9a5bb119d70a3d1b0eadc80028f07bf0e2fb63fc -> 7df8377d57d348d50e9ba9c9c1caa6757ecc1274
- bbab2c4966f38f8af04c02ec9d433e92220574bc -> 70c70336d3c1f723c4ee78302515fef3ea50554e

P4 bffd3e70, range ec25d5f1..740e2a6a (range has 3 commits, 2 picked; skipped report 740e2a6a2391fec61d52f143f9086d73f2f9782c):
- 7d90e4a510b591217ea28814f5e29286ec4d4a6e -> 586cd885c80f3350bae1754c954005686446bee6
- 8a654ab70e87f2d45b92da4d34ef4fdfa06f0a28 -> e5d11120916a405f79692be297cf9962dbd6b311

P5 d340e01f (range has 7 commits, 5 picked; skipped reports 0c71d13c5268883ac2fa83e41f16dbd14d742654 and 3c66ddd5465007908f6d752aea55a829205c0ac8):
- e5c4793d6e0a52c2cd143d60d91d394a4a71574d -> 9ed5560a4b87d6dcc414bf4f5f560ccaf245ebed
- 2940ad1fdbb7f535863eeff379d4b80498d97bc2 -> 780a3690869aaa51201ecf34f5f6ed2080f4fdfa
- 4b945e6fb550a5165e3cfc4623e109688dda3396 -> d71757c1e0451e617caf305bcba4eabbf3b68b5e
- 403b11be775a578348e1b69efea23d4d0134d20d -> 143d134d0c2abdb089e23d42440c08645a7872e9
- 8daad6a2b9bfaf2fc4802f7cf2afdd8e0ad01216 -> 43ae7371d7e9e173e33b69efdc061ac2891c9d98

## Stage A verification (at 43ae7371d)

- Land graph `-Check`: approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE.
  The default evaluator path was missing, so the evaluator was built natively with `-c Release` through
  build-slot and passed as `-ToolPath`. `-Update` was not run.
- check-evidence-diff e18a1177..43ae7371: 15 commits, 0 violations.
- One native Antiphon.Tests build. Each run below used its own lease:
  - BuildSlotScriptTests: 20 total, 18 passed, 2 skipped, 0 failed.
  - Doc classes: 152 total, 152 passed. By class: StandingPipelinePolicyDocumentation 12,
    OrchestratorInstructionsGuidance 6, D2AdvisoryDocumentation 7, KeepSixDocumentation 3, InstructionBundle 78
    (green, within its headroom), AreaMapContractTests 6, StableBuildOutputDocumentationTests 40.
  - RcStoppedRunSeedTests: 22/22.
  - Stopped-seed harness cases, one lease each, all PASS: UnfinishedRunLaneResume, SkipRowsCountAsCompleted,
    FailedFirstMapping, FailedMethodBands, RealPackerFailedMethodFollows, RealPackerRegroupKeepsFailedFirst.
  - LiveLaneFingerprintTests 7/7 (CP-31) and LiveLaneAttestationTests 6/6 (CP-22): 13/13.
  - Six CARD-1640 classes (Resolver, Create, SyncDebt, DispatchBaseGuard, DispatchSiblingInspection,
    StartRefAvailability): 253/253. C508 was green, so no rerun was needed.

## Stage B picks (23 of 26; P6 aa89c309 chain across cf298805, 00abf1bf and aa89c309)

Skipped report-only commits: 41e63343b6449c6a97ee7c8df3bfe52da0354ea9 (task-cf298805),
009759428ccd73994b3241345dcf970858edefed (task-00abf1bf) and 4d2b722d266babfeecb2d00285b318cc4d2b13d5 (task-aa89c309).

- 3c273ba05cf38311dae7d9cbb5cffd40c4ec1fb3 -> 3b3501f44cc0833e2264cd6cd375c586ede69eb2 docs plan
- b6bb58e763c9a8cdc5c8ff86f31e344b92e36a93 -> 8bdad1cf42cd2c7f43e83d7d4273215170a768d6 docs H-1..H-8
- 5d37396b319e9880783e119809601187a037e583 -> 5e51e55b0a84604e9695d4aaad99c35829ba84f2 docs test design
- 336b22c917fa9330f9bd99d9015094adda73c50c -> d131390bf75f75b39e8cbe13be55a5e3bf8186e7 red 1
- 50d8ed5a8ba6ec48f3bb62b78064fa65ec66a262 -> 1e64ae5a3c9f6d9ea3d4d1c9e83e89b95b3fe4dc V-44
- 2ae46dae7cc4637f3d4df97d6cb7317bfa0c74c1 -> 8395c4a431db27680e306aacb0ef46f1dbafbaa8 runner pool
- b7029b0f1859d61e3ecf81f850b484a9ca7ec856 -> b98fbdacd1958c2b42d2b3c91bc01c95b8cedae1 timeouts
- 769701d84c7dbac6c1ef668959c8e827438cf277 -> 990acc3c4d532717699eedc5a01731d8fd713490 policyHash repin
- 29c66f36557d4d8a88192fe3a14bd229387862c9 -> 6e8df67ec5c59c446a3dc12e52ec47f174265dfd red 2 (EF)
- c8739afe38f4d744b50ff5f6f9b476012ac1a99e -> e06ef30735e57772e5d20255ddf91cebeaf76267 server half
- 34ab2dd74340e65564f2aab018e519290ff0860f -> 35e9e196646120cd24940739f1d261ac41597ad5 CP amendment
- 14efac8aae51c60036f3b72730e055cf763d377b -> 591c80c021be50fa814a7f34584aee9c94363955 V-37b red
- 414b13766c259f47aeb609f66bb87311da641cc9 -> 5b0a88249cf85e77ff42010cf8e683c87091b786 cold-reset route
- bc66451d05c0e277dc4c738a0eeed0e13d140c2b -> 80d9985cffc3de3d0ed71b684cecd6ec5bdd73f0 docs + workspace-slots.ps1
- 06dba7f6555bfcf6d19203a3c53fb6d541d35d31 -> 6ad3adf7b0324406a95a0ab5cd3593c0680eda51 V-40..V-43
- 8b97efa81cfa8e5087564d9bfb553d2caba2cc70 -> 67fa11a67ed1f6c76d9163015cac43b67bef3d99 CP amendment
- 4392d6d165e248060580f5dd6a37da826e04e69a -> 5086a80217470a4d16127dc4ecb59a2ab8bd4cf2 20 s timeouts
- 6ab40058b16af33eb16356cec6c06814376170e4 -> feaa669b7b4d21b961fa8cba02df60358bc00744 round 1 red
- 95b8ff5dd9689c1d4f88b6f32fab87bff4a005cc -> 4a5a1246afef6196d1d17339f743d13264f3cceb move D-3 witness
- fa2531861329f57505886dd03e24db1888033692 -> 20fac33a1dc1d16982091d29ab5a7c8ccdc40ebf round 1 fix
- d951e891ecf72fd1f9f2177e506912aa56fcbe27 -> fd88e3e6137f64ff3df351edd26ab1de36cf04b2 round 2 red
- 292ab1394a1a4a984d3c22640752a927ccaa909e -> 7ab9aa36cf88870410aada059071670c87528213 round 2 fix
- 2f73fbf52be4bb1c7bb1e377f20f466c1897263d -> 5376ac529987b9e7b2019773cb4d784cba8007bf CP amendment (fix2)

There were no conflicts with P5 in AgentTaskService, AgentTaskDispatcher or CreateAgentTaskRequest. The migration
`20261102070901_Card0824WorkspaceSlots` is the only migration master gained since the branch base ead93feb0, so
the snapshot did not diverge. Both flags still default to off.

## Stage B verification (at 5376ac529)

- Land graph `-Check`: approved, count=69, same digest.
- check-evidence-diff 43ae7371..5376ac52: 23 commits, 0 violations.
- Checkpoint tool, plan `2026-11-02-card-0824-S1-review-fix2-checkpoints.md`.
  - Run `20261102-133702-2d2d`, all 5 rows. CP-1 10/10, CP-8 14/14, CP-9 12/12 and CP-21 33/33 were green.
    **CP-4 failed 5 of 24.** All 5 failures were TUnit 20 s timeouts: LeaseRecoveryAtEachHandoff x2,
    ColdWorkspacePersistsAndLeaseCarriesCold and ReleaseRoundTripRealRunner x2. In the same run, the passing
    tests in the class took 16.6-19.96 s, and server2 load was about 36 on 24 cores.
  - The 5 failing tests passed when rerun alone from the same kept output: 5/5, about 11.2 s each.
  - Run `20261102-135946-3313` reran CP-4 alone and was **green 24/24** (max 11.5 s). Its receipt is VALID
    (`buildSource=verified`).
  - The receipt for run 2d2d is INVALID with `cleanup_refused`, because the red CP-4 row kept its output
    (the kept bins were removed afterwards). Its CP-1/8/9/21 rows were green. I did not rerun them only to get
    a clean receipt.
  - Assessment: this is a load flake against the 20 s per-test budget (commit 4392d6d1 set it from a dev p95
    of 8.4 s). It is not a merge regression. On a loaded server2 the parallel class runs close to that budget.
    It may need a Debug card if it recurs.
- V-12 (`C0824_FlagOffMirrorRequestUnchanged` x2 and `C0824_DefaultHandoffDigestIsThePreSlotDigest`, both in
  CP-4) and V-12r (`C0824_FlagOffRunnerKeepsTaskMirror`, in CP-1) are green.
- Six CARD-1640 classes at 5376ac52 after a fresh native build: 253/253.

## Cleanup

- Deleted `tests/Antiphon.Tests/bin-c0824-f2s` and `tests/Antiphon.SessionRunner.Tests/bin-c0824-f2r`
  (both kept by the red run) using literal paths. No `bin-*` directories and no land-graph canary directories
  remain. Native outputs are left in place.
