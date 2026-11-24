# CARD-1866 S1R1 advisories

The assigned base `b3c8a192e0d89599e1a83956a4464991e3481500` already
contains A-1 and A-3. Both passed fresh Linux verification. A-4 now has a
26-minute provisional planning estimate. A-2 needs a producer and consumer
design decision before release evidence can support skipping.

The source advisories are in `docs/superpowers/reports/2026-11-13-card-1820-s1r1-review.md`
at `e1387b1cc364f4d0492643e280cddc22f67e84d0` (Review `40a75a5a`).

| Advisory | Implementation and commit | Verification |
|---|---|---|
| A-1 algorithm source drift | S1R6D added the drift witness in `b21595a1149a2025330f526bf1ba83b9976e5f02` and fixed the raw source check in `e6946a058de487ff2134c2b2d2035646a0815fe5`. The C# port `a3bea86507d8adda29d7db1a5b99b88e607686b2` carries the witness forward. It commits a changed or CRLF blob in a private source repository and expects exit 2, incomplete/Full, the drift error and no algorithm digest. The running C# selector takes its bytes from embedded resources. | Historical S1R6D red `20261113-104450-967d`, source `5162524e7f53b1c89f1116ebcd7086e67b7498a0`, failed both CRLF variants with exit 0 instead of 2. Historical green `20261113-105339-abd5` passed the ten envelope cases. Fresh CP-1866 passed all four drift variants. The guard-removal M7 control remains with SourceLanding Mutation. [S1R6D receipts](../superpowers/reports/2026-11-13-card-1820-drift-code-2187186f.md#preserved-red-history) |
| A-2 candidate content binding and envelope authenticity | Open for P2. The current producer verifies exact endpoint commit IDs and the algorithm identity. The envelope is always incomplete/Full, with no admitted graph or carry proposal. | Design decision pending. See below. |
| A-3 fixture placement and cleanup | `b21595a1149a2025330f526bf1ba83b9976e5f02` added per-fixture lifetime and cleanup after both creation and test-body failures. `c4ba11dca6f9143ccaeb7856a2b07ea645ef8a2a` added the independent temp-location witness. `489e64ba695d056ae77734d0a31d5da17384e364` moved the producer in the parallel fixture-retirement repair. The current fixtures create unique system-temp Git repositories and remove their owned directories during disposal. | Historical red at `512d5822116393dab17d3c2727cbe6d818509719` reported 7 executed, 4 passed and 3 assertion failures, including the checkout-residue witness. Fresh CP-1866 passed the temp-location and exception-path cleanup cases. The historical red summary is in `docs/investigations/2026-11-13-selector-fixture-retirement-checkpoints.md` at `122865f76f488bdec794575bd0e3f3009de08ee3`. |
| A-4 CP-101 estimate | `3efd6f4127b3e1454dc417b0340c9422b77c4612` records a prospective 26-minute budget for the historical roster. Review run `20261113-021030-2338` measured 751 execution seconds for 42 passing cases on loaded server2. The budget rounds twice the sole comparable observation up to a whole minute. CARD-1862 measured the separate `DerivedImpactIndexTests` roster. | An estimate check against the base returned exit 1 with 7 minutes against the 26-minute target. It returned exit 0 on `3efd6f4127b3e1454dc417b0340c9422b77c4612` with the original 7 minutes preserved in the historical table and 26 minutes for planning. The 751-second sample predates the C# port. [Estimate addendum](../superpowers/reports/2026-11-13-card-1820-s1r1-code.md#performance-and-platform) |

## A-2 design decision

CARD-1820 P2 and the CARD-1836 consumer need to agree on the inputs the binding
covers, the versioned canonical envelope representation, and the consumer's
source of trust. The input decision is whether to bind the Git trees alone or
the complete admitted input manifest for each endpoint and target. The trust
decision is whether the consumer recomputes bindings and evidence from trusted
inputs or verifies a separately authenticated producer artifact. A digest stored
in the same editable envelope can be replaced along with its contents. P2's
`TamperedBindingsRefuse` witness must cover the chosen contract before a consumer
can allow carry. [P2 handoff](../superpowers/plans/2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md#card-1836-handoff-contract)

## Fresh checkpoint

One isolated Linux build under the host build-slot gate took 9m24s in MSBuild,
with zero errors. The three selected methods executed six cases in 6.4 seconds.
The evidence directory contains the JSON receipt and TRX for the tested commit.
This report is a Markdown-only follow-up to that commit.

```text
CHECKPOINT CP-1866 commit=3efd6f4127b3e1454dc417b0340c9422b77c4612 build=ok filter=/*/*/ReleaseImpactEnvelopeTests/(AlgorithmSourceDriftIsRefused*|FixtureCleansUpAfterFailure*|Fixture_repositories_live_under_system_temp_outside_the_checkout*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/ws-11/.antiphon/checkpoints/CP-1866-20261124-225840-7f1c/run.trx slot=granted waited=0s dirty=0 source=3efd6f4127b3e1454dc417b0340c9422b77c4612 sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=3efd6f4127b3e1454dc417b0340c9422b77c4612 rows=1
```

The scoped handoff is land for the Markdown changes. The board's L-1 remains a
Merge obligation on an integration tip that combines index changes. Regenerate
the index there and rerun `DerivedImpactIndexTests/committed_index_matches_the_builder`.
