# CARD-1404 cost-balanced rc launch groups

The CARD-1397 packer in `scripts/lib/nightly-tests-impl.ps1` fills an OR
group by class count. Debug cfa9de89 on the Windows rc/2026-10-20
qualification (tip cadf547f7) killed group 9: 34 land/dispatch/check
classes, 605 required nodes, no TRX, about 213 nodes still unreached when
the 60-minute `watchdogs.antiphon` (3600000) fired. Serial land-lane work
there was about one node per 5 seconds.

Inspected at origin/master `fb8628488782a1f86fbae8e090f7bfcc4909c1d6`.
`GET /api/runner-defaults` reports global runner `server2`. Checkpoints
name no host and no OS.

## Decisions

D-1. A group admits another class only while both totals stay at most 120.
An ordinary required node weighs 1. A node whose categories contain `Slow`
weighs 4. A bare type whose name starts with `Land`, `Delivery`, or `Check`
doubles that weight. 120 ordinary nodes is about ten minutes at the measured
land-lane pace, inside a 20-minute budget and under the 60-minute watchdog.

D-2. A class whose own cost is already over 120 still launches, alone.
The packer does not split a class into method filters. Exact-method chunks
stay as they are.

D-3. The CARD-1397 class-count search (about 40-60 groups once a suite has
at least 40 classes, max 30 otherwise) stays. The cost cap only refuses a
placement the count cap would still allow. One-node classes keep the
C1397 shape: 50 class launches, 7 exact launches, 57 total.

D-4. `Resolve-NightlySuiteChunks` still returns the original chunk when the
suite is not native, the profile is not exclusion-aware, or the exclusion
list is empty. Nightly and any other no-exclusion profile stay one launch.

D-5. Suffix-unsafe types stay out of OR groups. The two C1397 prefix
exceptions and the seven exact-method chunks stay. Census rules stay: every
required node once, no duplicate class, no invalid filter.

D-6. Recorded TRX durations are not an input. The candidate logs live under
a Windows release root this runner does not have, and there is no stable
checkout-local duration table. Node weight is the cost.

D-7. The same cap runs for every native suite the rc profile already groups
(antiphon, session-runner, pty-host, agents-pty, e2e, markdown-pdf). A light
suite stays one launch. The cap does not turn each class into its own process.

Rejected: lower the class-count size for every suite. Light suites and the
C1397 band would explode. Rejected: split an over-cap class into methods.
That changes the exact-chunk contract. Rejected: raise `watchdogs.antiphon`.
The process limit stays 3600000.

## Verification design

S1 is the red `-Case` below, unlisted, run before the packer change. It must
fail while a multi-class group is still over 120 required nodes. S2 is the
packer. One checkpoint-tool command row is the green proof. Do not run a
suite and do not run `dotnet test`.

- V-1: no multi-class group has more than 120 required nodes.
- V-2: no multi-class group weighs more than 120. `DeliveryWatchdogTests`
  (Slow), `CheckSweepTests`, and each `LandBatch*` class launch alone.
- V-3: every required node is in one launch, prefix types are not OR'd, and
  the partial class stays two exact-method launches.
- V-4: nightly and an empty exclusion list stay the single `all` chunk.
  `watchdogs.antiphon` stays 3600000.
- V-5: session-runner, agents-pty, e2e, and markdown-pdf light fixtures stay
  one launch. The pty-host medium fixture is two launches, largest group
  120 nodes.
- R-1: one checkpoint-tool run of CP-1, exit 0, `buildSource=notApplicable`.

`Test-C1397_RcAntiphonLaunchGroups` is re-run once after S2 and must stay
green at classLaunches=50 exact=7 total=57. That run is unlisted.

No PC-n rows. Mutation has nothing to plant until a later plan names one.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | n/a | cost-groups | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1404_RcCostBalancedGroups` | V-1, V-2, V-3, V-4, V-5 | exit 0 | n/a | 3 | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

### Unlisted

The S1 `-Case` is the red witness. Reason: a cap assertion that passes on
the class-count packer is a stub. The C1397 re-run confirms D-3. Reason:
the brief asks whether that case stayed green. Neither is a checkpoint row.

### Cost

Ordinary floor is 3 minutes.
