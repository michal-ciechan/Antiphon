# Merge batch 130

Master fast-forwarded from 8fb32b25be7d17490d2dc999f35b068843ce1f4f to 7624fb6820a48805c2fb23c9d7e38f324f7cece2. First push accepted. No subject excluded. No migration. No cherry-pick conflict. docs/docker-stack.md and src/Antiphon.SessionRunner/SessionRunnerRuntime.cs auto-merged and the stable patch-ids still matched. rc/* was not pushed. Loop count 1.

## Commits

| Landed | Source | Patch-id | Subject |
|---|---|---|---|
| 31cbd977a11ca7ca791d63eeba5492ff5b4a3c61 | 7e71febc2d754c6005e5be46f8c8d39a09a21cf3 | 7f1daca89134a716626b181ce8516ef9011d14c7 | CARD-1778 plan |
| 619b29230aff343c927ac04f49920ddbf34d708d | 43b71bf67aeab10d5f1449d78aab92fc0e357b57 | b0684a9d4b5fd1efa9821fc23a081d2f703b0f59 | CARD-1778 S1 red |
| 1c83d2d97acd225da4cebed87217cc5a55e8a963 | 2f45c1f482159efe2f86be737ba609a7e9d4e715 | f8b568ff5c3c9a4cae85a3fe26e42affd134b2e5 | CARD-1778 S1 drain probe |
| 975403fe0d7679ddc080542c0baa99781647e939 | 76a85e11aa6c6f5c7156073a3f7206aab255d283 | 615776397c40b39c046b49461d9ccf1073c669de | CARD-1778 S1 fix |
| 2179d718cafd8abaa1900143094dc251d0df3fd8 | 7296ce3eb39fa294fe30fed4b958ee94499136f8 | 190062f7ad98e4aa100699e41cf93bd3d66c2b14 | CARD-1778 S2 red |
| de3c59991297c9d49d18bba0b7ad6d25ea270156 | df1516b315860aec4cad872114ec4b6d4a0ef272 | 56bafe09abda253246ed19a2375070167eb276d2 | CARD-1778 S2 fix |
| cbc08c44f6d43b8f0c9017878749d89cdd09e059 | bc080757599a024e512d0487d2c4561c4f9cfb70 | 2e75cc4d8cacd261c7b44e6b6d40e69f94072d59 | CARD-1774 red |
| 0774dfa3dd8f9e5392981418bed21de0e18937e9 | 36a7f9051a0b707fcb6468ca1da03df609c860f2 | 1622e4e2db508844044687c4a284d6e91d6eaaaf | CARD-1774 retry fix |
| e14fc009ffd3c1fb040c0bd5fe7fdaf8ef93a475 | f2ca67d77d84054deef171ff417c2c88eb1b3407 | e9e33bde156185867990720cb3a54a18cda244eb | CARD-1774 holder red |
| 663faf96fa0853af2dcef70062e7acb71f3bdddf | 62271791fb4cdf9c72e5aa748ccb91bc2f743298 | 594d5d8e60fd840a4767c2a0677b9d71a2c4500d | CARD-1774 holder fix |
| c2c5171de11188ae0b7c79e154a06b552df096f9 | bd429870d1276a2533888e69d1872bd588447a55 | 6a955daecdcd7ce324880fa5331045ea77ab7a8c | group-89 poll red |
| ff0091b9e168ab9fbb3e4a398769ae63063b7ecd | 5e3d46c6f53c92fd19d0d0895f44a177cca338f5 | 6b1807ca9ce1da7d3ed32ac302c673c76ef5f20e | group-89 clock fix |
| 7624fb6820a48805c2fb23c9d7e38f324f7cece2 | 71f9f6ced9c84a6c0c77364874780a60d59e083f | 4725dfc5544c009a279ab4a2a6c689f95ee0d5c6 | group-89 why-missed |

CARD-1774's fetched history has no cdf2c821 commit. git cherry origin/master origin/feat/card-task-806e4892 listed only the four commits above, all +, parented on c460b0d0a4. Those four were picked. No .antiphon or report-only commit was in any of the three ranges.

Evidence diff 8fb32b25be7d17490d2dc999f35b068843ce1f4f..7624fb6820a48805c2fb23c9d7e38f324f7cece2 was commits=13 entries=0 violations=0.

## Policy and land-graph

Protected live-lane inputs and tests/test-execution-policy.json were unchanged. docker-compose.server2-server.yml is not in the protected-input list. Recomputed values match the pins already on the tree:

- policyHash 3d54ece0132f9cd04bb80ca30f6f6817383ed3a2dbf96a824f27626299b5f3fb (old = new)
- PinnedInputDigest 9e79dffc9ce14a1ffa3e4bddc2b1c4bcb80848a2067ca9d97a83fc2184f541b1 (old = new)

No recompute commit. CARD-1777 was not a subject of this batch.

land-graph -Check approved digest 3607946C34AFF302EC65D57B41F93E400DBB0C7C1F51CE30D2202224A593432E count=71. Same digest as the base. No -Update. No refresh commit.

## Checks

Antiphon.sln via build-slot, UseAppHost=false, native output: 0 errors, 2084 warnings. No NETSDK1047, so no -m:1 rebuild.

Antiphon.Tests subject union: 191 passed, 0 failed, 0 skipped (RemoteExecutableLaunch, Server2SidecarStandingAgents, Server2ProductionServerContract, DockerStackContract, HangfireStorageSelection, HangfirePostgreSqlStorage via Testcontainers, HangfireLeaseDocumentation, Card1761RunnerReadPoll).

SessionRunner ClaudeLaunchTrustSeed + RunnerChildClaudeTokenInheritance: first launch died before discovery with "No space left on device". Unused build outputs from this worktree were removed and the same --no-build run then passed 11/0/0.

Policy guard batch, own process: 50 passed, 0 failed, 0 skipped.

MechanicalPcContractTests + TaskPlatformGuidanceTests + C1729LayeringGuardTests: 25 passed, 0 failed, 0 skipped. C1729 stdout: VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3.

## Owed

SourceLanding mutation positive controls and the Unit lane were outside this batch. The group-89 Windows qualification failure on rc run 4e0d1159 stays on that run; this batch landed the Linux clock seam and the why-missed note.
