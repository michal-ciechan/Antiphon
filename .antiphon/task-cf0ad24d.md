# Merge batch 120

Master fast-forwarded from 74a8004f405bc54666fea33889839f0ff4d62ec7 to 6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa. First push accepted. rc/2026-11-10 stayed 74a8004f405bc54666fea33889839f0ff4d62ec7. No subject excluded. Report-only commit 61a663b368262e05b4a936cfedcc6bb0d6a94881 (.antiphon/task-f668441c.md) was not picked.

## Policy

CARD-1745 conflicted on tests/test-execution-policy.json and scripts/test-live-lane-run.ps1. Master's exclusion rows stayed. The CARD-1745 windows exclusion for LinuxServerRestartGateTests was re-added at the end of profiles.rc.exclusions. Both pins were stored in the same commit a1f644e5b4f2d615cc7f301135729f9c60260457.

- policyHash f6d55df5e16249b454f7dd575ca13725cb16817c677d4522d66a653c3c97ce06 to c6da42db3549444e55fcf35ead72f674861dd24f4809bc924780bebcbd07f39d (EmitPolicyHash; Read-NightlyExecutionPolicy accepted; NativeLinuxPolicyWitnessTests Policy_rows_require_pinned_attribute passed)
- PinnedInputDigest 656735d424cb7151fdfcbf2593cd91278fded6a7f492788fc1380e99b4994411 to 2e519236f898920407b63636797731c00aa73e22f01727d2f92872126cbc56f3 (scripts/test-live-lane-run.ps1 -PrintInputDigest)

## Patch-ids

Stable patch-id matched for every pick except the resolved CARD-1745 policy commit (source 0567d7a3cc29d45d962baf6dbf3a7ff782f3fcd1, landed 5af207383ee8f858efc6b540bb7001bf2213e5a2).

| Landed | Source | Patch-id | Subject |
|---|---|---|---|
| 1d86762c54bfbc7a8018ec529ca9ec415a3dea97 | 88580305ae9d953758aea314b1d380daaa5ce362 | ae344452df9fc7b0a00476ec5ea815986617ed36 | host-load timing labels |
| e6132c64d0f6f2e86bedca483403ef2fd7301e39 | 72771947e0b5188223b484515063dc6ea0554927 | 6f36787b152a66ef2669e0928134e54009141f72 | legacy land evidence |
| 640e5605424ae9b9f78a71dd9f210e14e8f75521 | cd1149e78b55661860bdf216cc084dfab3f50261 | 894437e2af96058ed5a9c68fa07fddfa2bffda8c | C459 FinishedAt |
| 0af14029dc48ce06802eae3feb591157a66080f2 | 555022d714b5ffea124241801e9db6f6a8eb2c80 | 59cd440dfbec957638c3a970beaded22ef3fda51 | CARD-1731 witness |
| df019b160454a5e0a396107264e7bcc6919846f5 | af40099a19e9252122e11d6f36dc48b998dbd5cc | 56eaf419055f87c2a1d8726ee3cf5da7f56cfc50 | CARD-1731 path resolve |
| 70aec174ed3f6d2fff2fc53ba59428fd9e821448 | cf0aba324bdfff15e10f4ef0120c84a6c380f345 | d04a1f26ee67387548e4f7109eb89c35aa54d37d | CARD-1731 PC note |
| fa472e31216bb5a11478a996b98674e0d0948926 | 53fad1f6f809e6c207b943f2e75e8ef3fae9dc8c | 3be3428f4853afdce4f1949a199666dd289030dd | CARD-1745 witness |
| 2b0468947bea8f8cfea45314fa231f54e09e62e4 | 8278a7205eccc8308ae9f06d01ed89000df016ee | 4a22e94c4f3df32dcd95f54cdec7fde1ac0dcae0 | CARD-1745 gate |
| 4f0ed2b81e40b6aca5251d6a5aef1df348c5e4d0 | 972611f1b59b90e6eab08a73fb60e91a4c4bfe50 | 4d73345867571d5f6322b440a4d077af549462d2 | CARD-1745 stop witness |
| b4c226c2e41262ef73f3597413382b5cbc12f1bc | 61b5da0e92773727d13c257bd4acd781f7175985 | ec913ae2ea8ca7c1d9c1bb66fa6abd5deede9519 | CARD-1745 probe-before-stop |
| a1f644e5b4f2d615cc7f301135729f9c60260457 | 2e5531699bd7b7f7d7b6b59af76a533e6a9eb133 | 5af207383ee8f858efc6b540bb7001bf2213e5a2 | CARD-1745 windows exclusion (resolved) |
| cc3a2b2fe1e83dcf5843342d0875c6094267d453 | 96b0dc778a8bd6a20834e1f6d61fb7d716eef407 | cfd4f3e7612ebcb8af0af0658c5718661fbd319b | CARD-1765 testdesign |
| 6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa | 9bfba59520bd079e3ee19a5c84a88d404a3bf9d1 | c871672edc624828d45164cf2057f6030aeabce7 | Review-column housekeeping |

Evidence 74a8004f405bc54666fea33889839f0ff4d62ec7..6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa was commits=13 entries=0 violations=0. The mirror was not shallow.

## Checks

Antiphon.sln build, UseAppHost=false, native output, slot lease 9be9129a, maxcpucount=6, waited 0s: 0 errors, 2044 warnings, 9m 25s. No NETSDK1047.

Batched Antiphon.Tests union, 6m 54s: total 265, succeeded 264, failed 0, skipped 1 (C1210_Windows_linux_only_outcomes_are_exact). C1729 inventory VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3. Witness Policy_rows_require_pinned_attribute passed.

CARD-1731 C599SnapshotTests|ReleaseGatePublicationAuthorityTests, slot waited 210s, run 11m 01s: 19/19 passed, 0 failed, 0 skipped.

scripts/test-restart-linux-server.ps1 once, cases W1 W2 W3 W4 W5 W6 W7 W8 W9 W10 W13 W14 W15 W16 W17: 80 PASS rows, 0 failed, each case exit 0.

No test reads docs/superpowers/testdesign/2026-11-06-load-telemetry-s1-testdesign.md. DockerStackDocumentationTests, which reads docs/docker-stack.md, passed in the batch.

land-graph -Check exit 0. Approved digest 655B2EF4C8BBA2C254C69B41FBD22F0689643B9C1032403635EAD7D6400926E7 count=71 SDK 10.0.403 pins 7a2586e58bd4768648c6f670f6f2c4432f68936a. No -Update. Same digest as the pre-batch record.

## Owed

Windows native reruns, positive controls, and the whole Unit lane. CARD-1731 evidence-path positive controls stay pending in docs/superpowers/mutations/2026-11-06-card-1731-candidate.md.
