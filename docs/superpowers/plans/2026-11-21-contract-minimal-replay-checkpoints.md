# Checkpoint project contract minimal replay

Base `72565797a7702f4af64f43f51d854ea08071b1d1`. The contract and selector carry forward the content from `72d3440e4b1788fd18d72eeced6d1a890754fac3`, reviewed in round 2 by `55022a2d`. The earlier replay plans record their own scope. The table here is the closed list for this replay.

The checkpoint tool's project references are exactly HostCleanup and GitInProcess. HostCleanup references only GitInProcess. GitInProcess has no project references and references only LibGit2Sharp as a package. Merge selects the project contract when any of the three project files changes. The committed derived index must match a fresh builder result byte for byte.

## Performance budget

The two classes share one cold Antiphon.Tests build and run serially. Neither class needs Postgres or AppHost. The 7- and 3-minute estimates carry forward the prior replay's provisional budgets. Review `55022a2d` recorded single samples of 4.8 seconds for the contract class and 169.4 seconds for the derived-index class. These are single samples, with fresh TRX and build/slot timings collected by this run for CARD-1404. The importer applies its default deadlines under CARD-1720. Build-slot waits are recorded separately. The nightly and rc Unit lane retains broad coverage.

## Verification design

- V-1. Run all seven checkpoint project contract tests.
- V-2. Run all 27 derived-index cases, including `committed_index_matches_the_builder`.
- R-1. Build the derived-index driver in its native output and regenerate twice. The first run changes the committed index bytes and the second leaves them identical. Commit the generated index last.
- R-2. Check evidence policy across the full base-to-tip range and check merge compatibility with fetched master.
- R-3. Inspect the range for affected OS branches. This replay changes test code, selector data, a generated index and plans. There is no changed OS-specific production behavior or startup path.

The prior replay covered the guard and consumer classes on the reviewed source. The selector class and land graph audit are outside this replay. The C1801 selector failure is owned by Debug `abffe0c2`. There are no PC rows or migrations.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-6bf9a4f0/ | project-contract | `/*/Antiphon.Tests.Invariants/CheckpointToolProjectContractTests/*` | V-1 | 0 failed, 0 skipped | 7 | 7 | true |
| CP-2 | all | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | V-2 | 0 failed, 0 skipped; committed_index_matches_the_builder | 27 | 3 | true |
