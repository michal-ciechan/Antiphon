# Why missed: P2 admission matrix timeouts on Windows

## Introducing commit and card

CARD-2069 landed in batch `5855e61481fa0f80f015ae1a4e8584619b96eb40`. Windows rc run
`419ed669` at `5c0883bf203d122fdc7d4556e79087af2a683669` timed out in group 138.
`GraphAndTrustBindingsRefuse` ran 30.15 s and `TamperedBindingsRefuse` ran 30.27 s.

## Approving review

Round 3 Final/Full Review `801d2b17` at `1d788ccd82febd9de97cd91514ce3e6a89564027` selected
`/*/*/ReleaseImpactEnvelopeTests/TamperedBindingsRefuse`,
`/*/*/ReleaseImpactEnvelopeTests/GlobalContractChangesRefuse` and
`/*/*/ReleaseImpactEnvelopeTests/GraphAndTrustBindingsRefuse`. The reported Linux runs
took 8.48 s, 4.46 s and 14.00 s respectively.

## Outside the selection

The Unit methods were selected on Linux only. Native Windows under qualification load was outside
the review's timing sample. The 30-second Windows observations are timeouts and give only a lower
bound on the old fixtures' duration.

## Cheapest pre-land check

`tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.P2*.cs` →
`/*/*/ReleaseImpactEnvelopeTests/*` on native Windows with phase timings and the matrix methods'
per-test durations retained.

## Exemption

None.

## Fixture cost and repair

The Linux baseline `CP-Baseline-20261127-014216-281b` ran the unchanged test sources
at `65dfb9b209a5397804f52f724f56917da2261664` (the repair branch's first commit adds only
this note). Git Trace2 start events were joined to non-overlapping TRX test intervals.

| Baseline test | IDs | Worlds / tool repos | Fixture Git starts | Admission Git starts | Blob sessions / project graphs | TRX seconds |
|---|---:|---:|---:|---:|---:|---:|
| TamperedBindingsRefuse | 297 | 1 / 1 | 108 | 333 | 28 / 53 | 9.185 |
| GlobalContractChangesRefuse | 7 | 4 / 4 | 140 | 144 | 8 / 16 | 2.795 |
| GraphAndTrustBindingsRefuse | 19 | 9 / 9 | 303 | 225 | 12 / 21 | 4.430 |

Most resealed proposals reuse one `Prepare` result and cost zero Git starts, graph builds or tree
walks per case. Distinct authorities such as changed discovery, a failed origin or a different
trust policy invoke the real preparation path. A normal cold preparation uses one persistent
`cat-file --batch` blob session, up to two pinned tree listings plus tree-ID reads,
two project graphs and two typed edge graphs. Cold project and contract reads add separate Git
batches, ancestry and commit identity are verified, and the installed tool source alone costs
four `rev-parse` starts plus direct reads of the loaded assembly and three installed files. Early
refusals stop at the relevant gate. Counts vary with cache state. The summed child lifetimes
would overlap preparation wall time, especially for the persistent blob reader.

The old fixture staged the whole endpoint worktree four times, even for the two empty history
commits, staged a second source repository once per world, and walked both owned roots on cleanup.
Each execution pin added an `ls-tree` and a tree-ID query before production preparation. The repair
creates actual Git objects with LibGit2Sharp, stages only altered candidate paths and uses one
fixture tree inventory per distinct immutable tree. Each test shares one installed-source
repository. It retains separate committed worlds for distinct graph or contract inputs and
reuses a prepared result for its honest and disputed proposal cases. Inventories still use the
real sealed file writer, and the fixture roots are still disposed per owned lifetime.

The first repair measured zero fixture Git launches over 50 method executions. Its combined
receipt method still reached 680 production Git starts when the bounded process-wide Git caches
were full. The final split bounds custody and origin separately. The three original checked-in
rosters are identical, with 323 unique case IDs in total. The 311 original IDs and 12 additions in round 3 are present. The three original method names
and three additional selections form the six-method matrix.

## Linux timing and native calibration

The final split is sampled ten times on the 24-CPU Linux server2 runner in one TUnit process so
later repetitions exercise the populated Git caches. The nearest-rank p95 of ten samples is the
observed maximum. The Linux deadlines round twice that observation up to the next second. The Linux sample remains
provisional. Native Windows has a separate collection below.

`CP-SplitLoaded-20261127-020238-dfab` at `4575a4e0a3d266086300cdfe57c93588033cbf1c`
passed 60 of 60 executions with no skips. The server's one-minute load average at row entry
and exit was 19.72 and 5.31. The run retained the TRX, per-scenario console timings, checkpoint
phase journal and Git Trace2 counts. It recorded zero fixture Git launches in all 60 executions.

| Test | IDs | Observed p95 (s) | Linux (s) | Windows provisional (s) | Admission Git starts | Project graphs |
|---|---:|---:|---:|---:|---:|---:|
| TamperedBindingsRefuse | 264 | 4.258 | 9 | 90 | 310 | 17 |
| ReceiptBindingsRefuse | 21 | 2.824 | 6 | 90 | 360 | 20 |
| OriginBindingsRefuse | 12 | 2.695 | 6 | 90 | 357 | 20 |
| GlobalContractChangesRefuse | 7 | 2.940 | 6 | 30 | 144–344 | 16 |
| GraphAndTrustBindingsRefuse | 7 | 2.457 | 5 | 90 | 89–128 | 7 |
| TrustedProjectBindingsRefuse | 12 | 2.585 | 6 | 90 | 136–271 | 14 |

The four Windows rows split from the two timed-out methods use the provisional 90 s ceiling
approved for this repair. `GlobalContractChangesRefuse` retains its 30 s Windows ceiling.
The two censored Windows durations establish lower bounds for the former methods. A Windows
multiplier requires measured native completions. Case failures and cooperative deadline failures
include timings for the completed and current phases. The test starts each scenario with a phase
line for the framework's hard timeout. Fixture setup, preparation and teardown also emit
structured timing spans.

CARD-2069 owes 20 native observations per method on the next integrated Windows candidate
after the active qualification releases the desktop. Run the six named methods through a leased
native checkpoint repeat, retain TRX, phase logs, run/group identity, host load and queue wait
separately, and set the caps to about twice the resulting native p95. A timeout is a censored
datum and requires investigating the last recorded phase before setting a new cap. Release
qualification remains on its own pinned candidate.

## Current positive-control selections

The historical CARD-2069 mutation register retains its original filters. For this fixture use
the exact method on `ReleaseImpactEnvelopeTests` that owns the case:

- `GlobalContractChangesRefuse` covers n1 and n2 (PC-41 and PC-42).
- `OriginBindingsRefuse` covers n7 (PC-43) and the origin/install cases for PC-12, PC-29,
  PC-30, PC-33, PC-39 and PC-40.
- `TamperedBindingsRefuse` covers n8 (PC-44).
- `ReceiptBindingsRefuse` covers the custody and owed cases for PC-16, PC-17, PC-18, PC-34 and PC-38.
- `TrustedProjectBindingsRefuse` covers the trust cases for PC-20, PC-21, PC-31 and PC-32.
