# CARD-0987 RC project-reference contract repair

Code task / landing owner: `15685311`. Base: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.
Branch: `feat/card-task-15685311`. Final round, with the brief's explicit CARD-1287
bounded-scope override: no whole Unit lane or assembly run. Current RC is untouched.

## Decision and implementation

The [custody refresh plan](2026-10-15-card-0987-custody-refresh-plan.md), D-3,
explicitly requires a one-way Checkpoints -> HostCleanup reference in S1a0.
The [test design](2026-10-15-card-0987-test-design.md) includes those project files
in S1a0 and binds dependency direction to R-1/6. Commit
`367c8ee4cdcc877aa71f1a46d30c410d628edc3b` implements that planned edge.
The old `csproj_has_no_project_references` contract is therefore stale.

S1 replaces it with `csproj_references_only_host_cleanup`: parse the real project
XML and require exactly the single planned Include, retaining rejection of missing,
duplicate and unrelated references. Explain the deliberate contract change in the
test comment. No production project or cleanup behavior changes.

The roster attribution at `origin/feat/card-task-b7430380`,
`docs/investigations/2026-10-15-card-1287-windows-unit.md`, independently records
4/4 passing at morning baseline cc8798c1 and 3/4 passing at the assigned base.
This repair reproduced the exact failing method at the assigned base: CP-0,
1 executed / 0 passed / 1 failed / 0 skipped, clean source and verified build.
Run: `.antiphon/checkpoints/20261015-222309-c1c4/`; build 372.004s, row slot wait 75s,
total 7m55s. The already-committed failing test supplies red-first evidence.

## Verification design

- V-0: base exact method fails on the obsolete no-reference assertion. Complete
  before S1; its ignored baseline plan and receipt remain under `.antiphon/`.
- V-1: exact new reference set passes, with all four affected-class cases (CP-1).
- R-1: full adjacent csproj/package contract classes (CP-2):
  DotnetToolManifestContractTests (2), E2ECopyOwnedPtyHostAppHostContractTests (1),
  PackageVersionPinContractTests (2), ChannelOutboundContractTests (2).
- V-2: manual standalone publish plus dependency/launch inspection and standalone
  PowerShell Add-Type/create/collision smoke. This is a declared additional build,
  also used to bootstrap the checkpoint launcher, through build-slot.ps1. Publish
  at the base succeeded and included Antiphon.HostCleanup.dll. Production sources
  remain identical in S1. The helper was already repaired by
  `cdb18eef5013bfa45336dfd304a022b489686bbc`: ExclusiveDirectory.cs is independent;
  its context overload is in CheckpointCleanupDefaults.cs. No tracked script
  directly dotnet-publishes this tool; normal project publish follows its reference,
  and ShadowCopy copies its dependency DLL. No packaging/restore graph change needed.

Search census: the other ProjectReference hits in Antiphon.Tests are synthetic
checkpoint/landing fixtures, not repository graph contracts. RunnerCapabilitiesTests
checks the runner-only closure; PublishWorkflowSanityTests checks Messaging's NuGet
closure. Neither contains this tool. Docker/Codex image classes use csproj filenames
as image/fixture sentinels; their behavior is outside this test-only repair.
There are no affected delivery, landing, lease or persistence integration classes.

CP-0 is historical baseline evidence, not a requested repeat at the repaired tip.
CP-1 includes the renamed replacement of its failing method. Run CP-1/2 once per
committed S1 with expected-source-sha=HEAD, bound task ownership, and wait to terminal
exit. If refused specifically for a missing task token, the brief permits unbound
foreground execution via env -u ANTIPHON_TASK_ID; disclose any use. Inspect fresh
TRX identities, nonzero counts and validate the clean receipts. CP-2 reuses CP-1's
isolated build within the same After group, as permitted by testing-and-build.md
and the brief's one-build/exact-filters scope. No repetitions are required after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c15685311-fixed/` | checkpoint-project-contract | `/*/Antiphon.Tests.Infrastructure/CheckpointToolProjectContractTests/*` | V-1 | all 4 cases, 0 failed/skipped | 4 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | adjacent-project-contracts | `/*/*/(DotnetToolManifestContractTests*)\|(E2ECopyOwnedPtyHostAppHostContractTests*)\|(PackageVersionPinContractTests*)\|(ChannelOutboundContractTests*)/*` | R-1 | all 7 cases, 0 failed/skipped | 7 | 1 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

Ordinary remaining estimate: 8 minutes plus manual smoke and actual slot waits.
No full-assembly run: no unbounded shared impact is introduced. No timeout or
assertion is loosened to obtain green. Source remains frozen during each run.

## Mutation and handoff

PC-1 variants missing / unrelated-extra / duplicate reference remain pending for
method-scoped SourceLanding Mutation against
`/*/Antiphon.Tests.Infrastructure/CheckpointToolProjectContractTests/csproj_references_only_host_cleanup`.
Mutation owns deliberate mutants, valid assertion-red/restoration/green and missing
control discovery; this Code task runs none. Compilation errors are not assertion red.
Parent CARD-0987 controls remain pending and are not discharged by these receipts.

Next: ordinary Review, then caller lands original Code task 15685311 and commissions
SourceLanding Mutation. Restart: none. Detailed local evidence/report:
`.antiphon/task-15685311.md`; generated payloads remain ignored.
