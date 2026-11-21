# Group 47 Git wait checkpoints

These rows verify a named failure on a fake blocked fixture child, the real
resettlement transaction race and the production Git success contract
after a root exits while a descendant still holds an output writer.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-rc3528g47-fixed/ | git-wait-deadline | `/*/*/RunnerSettlementGitDeadlineTests/FakeGitExitAndOutputShareDeadline` | Child exit and EOF | 3 passed on Linux | 3 | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | settlement-serialization | `/*/*/ReviewEvidenceResettlementTests/C1043_SettlementsSerialize` | Both settlements, one successor, direct DB clone | 1 passed | 1 | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0; ANTIPHON_TEST_PG_POOL=off |
| CP-3 | all | CP-1 | git-success-contract | `/*/*/LandingGitTests/C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget` | Production success and journal custody | 1 passed on Linux | 1 | 1 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

Run the three Linux rows with the checkpoint tool. Their estimates are
provisional because the fake-child setup and guarded wait are new. CP-2
uses only the assembly-owned Docker PostgreSQL fixture. Turning off its
clone pool exercises the direct clone path. The Windows
resettlement class rerun is owed on the integrated repair SHA.
