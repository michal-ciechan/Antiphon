# CARD-1410 measured rc launch-group packer

CARD-1404 capped rc launch groups at 120 weighted nodes. On the Windows
rc/2026-10-20 qualification (66e9cfa9f, investigation
`docs/investigations/2026-10-21-rc-2026-10-20-windows-qualification-reds.md`
on `feat/card-task-41256176` 92c62bcc) that gave 231 launches at about 3.2
min each including a 46 s gap, and six classes above the cap launched alone
with no split. Group 27 (16 Slow AgentTaskLand* nodes) took 32.8 min.

Base: origin/master `47e6916c7590b101c9336fab84398fa93b68878f`. Must cherry-pick onto
`rc/2026-10-20` (806b23084fa91daaa61ff0ff1957f39c5fe76baa). Checkpoints name no host and no OS.

## Decisions

D-1. Cost is estimated serial seconds. A class with a row in
`scripts/lib/nightly-class-costs.json` uses that row (seconds / nodes per
node). Any other node costs 0.5 s, Slow 1.5 s, and Slow in a type naming
`Land` or `Delivery` 4 s. A type naming `Land`, `Delivery` or `Check`
anywhere in its name doubles its nodes (so `AgentTaskLand*` and
`AgentTaskDelivery*` now double): lane Slow is 8 s per node.

D-2. Calibration (caller refinement): the whole suite estimates 5-8 h serial,
since CARD-0856 measured the full Windows run at about 270 min. Two minutes
per Slow node holds only for the group 27 land nodes, which are a measured
row (AgentTaskLandActiveSourceClaimTests, 16 nodes, 1920 s). Measured Slow
classes outside it ran at 1.7-5.4 s per node (groups 19, 25, 26). On the real
listing census the model gives 7.8 h in 84 launches; the largest estimated
launch is that measured land class alone at 1920 s.

D-3. A launch group packs classes of one namespace first fit, largest first,
up to 900 s (15 min) and 30 operands. No node cap. A class at or over 900 s
launches alone. Suffix-unsafe types stay out of CARD-0403 OR groups.

D-4. A class estimated over 2400 s (40 min) splits into exact-method chunks of
at most 900 s: one method uses the plain exact filter, several use the
single-class form `/*/Namespace/Type/(MethodA*)|(MethodB*)`. A method that
prefixes a sibling method launches alone on the exact filter. A single
method with many cases is not split further. Each chunk carries its nodes,
and its leaf verdict reads that selection, like an exact profile leaf.

D-5. The cost table is data. Rows are `measured` (single-class Windows groups
19, 25, 26, 27), `pace` (RemoteScriptContractTests and
DelegationDispatchRecoveryBoundaryTests at the 9.2 s/node pace of the
watchdog-killed Windows group 9, until TRX replaces them) or `trx`. Refresh:
`pwsh -NoProfile -File scripts/refresh-nightly-class-costs.ps1 -TrxPath (Get-ChildItem <run>\logs\*\antiphon-*.trx).FullName`.

D-6. Unchanged: `watchdogs.antiphon` 3600000, nightly and no-exclusion
profiles stay the single `all` chunk, partial-class exact leaves, census.

## Verification design

The red `-Case` is unlisted and runs before the packer change. CP-1 is the
green proof. No suite, no `dotnet test`.

- V-1: launch count 60-100 and a 5-8 h suite estimate on the real listing
  census (19012 nodes).
- V-2: no launch estimated at or over 3600 s; no multi-class or
  multi-method launch over 900 s.
- V-3: every required node selected exactly once by its launch filter,
  excluded nodes never, prefix methods never OR'd, a prefix OR filter refused.
- V-4: RemoteScriptContractTests, DelegationDispatchRecoveryBoundaryTests and
  a synthetic oversized land class split, with at least one (Name*) method
  group; the Land/Delivery/Check match is anywhere in the type name.
- V-5: nightly and an empty exclusion list stay the single `all` chunk;
  `watchdogs.antiphon` stays 3600000.
- R-1: one checkpoint-tool run of CP-1, exit 0.

PC-1 (pending for Mutation): revert `Test-NightlyHeavyFamilyType` to a
`StartsWith` match; CP-1 must go red on `family-anywhere`.
PC-2 (pending): raise `NightlyLaunchLoneClassBudgetSeconds` to 1e9; CP-1
must go red on `split:` and `watchdog-budget`.
PC-3 (pending): set `NightlyLaunchSlowNodeSeconds` to 60; CP-1 must go red
on `launch-count` and `suite-hours`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | n/a | measured-groups | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1410_MeasuredCostGroups` | V-1, V-2, V-3, V-4, V-5 | exit 0 | n/a | 5 | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

### Unlisted

The S1 red `-Case` run (exit 124: no verdict within 900 s on the CARD-1404
packer). Reason: a witness that passes on the CARD-1404
packer is a stub. `Test-C1404_RcCostBalancedGroups` is removed: it pinned
the 120 cap and the no-split rule this card reverses; C1410 carries its
census, suffix, nightly and watchdog checks.

### Cost

Ordinary floor is 5 minutes.
