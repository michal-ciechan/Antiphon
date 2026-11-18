# CARD-1976 token-file exits and quarantine release audit

Code task / landing owner: `051a8c28-409b-455e-aae7-7b9900079401`.
Base: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (current origin/master at start).

## Implementation

S1 commits the two regression witnesses against unchanged production. CP-1 must
fail for both defects before S2. S2 changes the shared credential diagnostic to
nonterminating stderr followed by exit 2, including zero-byte files, and records
the runner's actual release outcome only after its answer. HTTP responses and
runner cleanup semantics remain unchanged. No runner implementation repair,
migration or OS-selected behavior is needed.

## Performance budget

One pwsh child per script case, with the existing process limiter and 50-second
deadline; one existing loopback phone-home host per route case. The real cleanup
regression uses the existing isolated test database and transport world. No live
runner or production database is a test target. Existing 35/60-second test bounds
are retained. Provisional estimates: script selection <60 seconds, route selection
<120 seconds, category/layering <120 seconds, contract selection <180 seconds.
Cold build estimate 15 minutes is the brief's admission estimate, not permission
to extend a run: use `--row-timeout 570s`, serial rows, and no repetitions after
green. Record load below 16 before admission, polling every 30 seconds otherwise.
TRX records membership and per-test duration; checkpoint reports record build,
slot wait and execution. Fixture subphases without instrumentation are unknown.
This task collects one red and one green sample, not a claimed p95 (CARD-1976).

Checkpoint tool cold outputs are required by the stage contract; all direct
bootstrap/auxiliary builds use native outputs. The tool has no native-output
mode. Its 570-second row override also bounds generated test drivers. Bootstrap
of the checkpoint tool and derived-index driver are accounted auxiliary builds.

## Verification design

- V-1: missing, empty and whitespace token files produce exit 2, a path-only
  diagnostic and no HTTP request, for release and cold-reset (six cases).
- V-2: no release-outcome log before the runner reply; afterward one Warning
  carries the actual removed/refused/removal_failed outcome, identity and audit
  fields, with no credential disclosure (three cases).
- R-1: all original script cases retain request serialization, exit mapping,
  usage validation and ASCII portability.
- R-2: all original route cases retain authorization, validation, wire response
  and real quarantine ledger/disk behavior.
- R-3: category exclusivity and layering inventory remain valid; C1729 must report
  zero violations and 162 listed edges.
- R-4: impacted public contract snapshots remain unchanged.
- R-5: land graph `-Check`, nightly pin recomputation, OS inventory, evidence diff
  over the full task range and conflict-free merge-tree against fetched master.
- R-6: the three assembly-local process-spawn limiter rosters remain valid.
- R-7: derived-index, policy, classification, mechanical and platform guards stay
  valid after regenerating the changed capped-reference listing.

The actual range selects `contracts`, `spawn-limit-rosters` (the new limiter
annotation) and `test-lane-category`, plus the two touched test classes. No
capped/owed classes or Windows-only work. Derived-index regeneration changed one
capped-reference listing, so the brief's conditional guard union is required:
CP-3, CP-9 and CP-10 cover it, reusing the same build. The three nightly pins stay
unchanged. Whole Unit and slow classes stay with nightly/rc. No required manual live operation; inspect fresh TRX for every
intended method/case and validate clean-source receipts.

PC-1/missing, PC-1/empty, PC-1/whitespace (each verb): restore terminating
Write-Error / unsafe null Trim and expect V-1 red. PC-2/removed,
PC-2/refused, PC-2/removal_failed: restore the pre-answer released log and expect
V-2 red. All nine variants remain pending for caller-commissioned method-scoped
SourceLanding Mutation; deliberate mutations are waived for this Code task.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S2 | tests/Antiphon.Tests -> bin-c1976-proof/ | defect-witnesses | `/*/*/(C1960WorkspaceSlotsScriptTests*)\|(C1960QuarantineReleaseRouteTests*)/(Token_file_errors_exit_2_before_any_request*)\|(Release_log_waits_for_runner_and_records_actual_outcome*)` | V-1, V-2 | 9 cases, 0 failed/skipped after S2; expected red before S2 | 9 | 15 | true |
| CP-2 | S1-S2 | CP-1 | touched-classes | `/*/*/(C1960WorkspaceSlotsScriptTests*)\|(C1960QuarantineReleaseRouteTests*)/*` | V-1, V-2, R-1, R-2 | every method and case, 0 failed/skipped | 27 | 3 | true |
| CP-3 | S1-S2 | CP-1 | category-layering-spawn | `/*/*/(TestLaneCategoryGuardTests*)\|(C1729LayeringGuardTests*)\|(ProcessSpawnLimitTests*)/*` | R-3, R-6 | every method/case, 0 failed/skipped | 69 | 2 | true |
| CP-4 | S2 | n/a | client-bundle | `npm --prefix client run build` | R-4 prerequisite | exit 0 | n/a | 3 | true |
| CP-5 | S2 | tests/Antiphon.E2E -> bin-c1976-contract/ | contracts | `/*/*/ContractSnapshotTests/*` | R-4 | 5 methods, 0 failed/skipped | 5 | 8 | true |
| CP-6 | S2 | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll` | R-5 | exit 0, approved; native auxiliary tool built first | n/a | 5 | true |
| CP-7 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1976-runner/ | runner-spawn | `/*/*/ProcessSpawnLimitTests/*` | R-6 | both methods, 0 failed/skipped | 2 | 8 | true |
| CP-8 | S2 | tests/Antiphon.Agents.Pty.Tests -> bin-c1976-pty/ | pty-spawn | `/*/*/ProcessSpawnLimitTests/*` | R-6 | both methods, 0 failed/skipped | 2 | 6 | true |
| CP-9 | S1-S2 | CP-1 | policy-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-7 | all named classes, 0 failed/skipped | 8 | 4 | true |
| CP-10 | S1-S2 | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-7 | every method/case, 0 failed/skipped | 1 | 4 | true |
