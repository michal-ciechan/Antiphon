# CARD-1989 control window and timings

Base: e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4. Code owner: 62cec2de.

The coordinator and live proof assertion share a configurable wall-clock budget.
Use `ANTIPHON_RETIRE_CONTROL_WINDOW_MS`, default 8000, range 1000..8000.
The ceiling preserves the host's existing 10-second response read and 11-second
effect fence. A 15-second default would require widening those independent gates;
that is outside this repair. No proof, ref, nonce, renewal or effect gate changes.

Record proof read, bundle hash, expected parse, ls-remote, live parse, comparison
and cleanup durations, including failures, in the coordinator receipt. Fixed step
names and numeric times only; no URLs, advertisements or raw errors.

S1 commits offline red witnesses before production changes. S2 implements the
window and timings. No deliberate mutants in Code; SourceLanding Mutation owns
PC-1 (remove budget check), PC-2 (discard timings), PC-3 (ignore configuration),
PC-4 (omit failure receipt), all pending; operator has waived mutation execution.

## Performance budget

Script witnesses use one tiny temporary proof, no database, Docker or network.
V-1 deliberately waits 5.5 seconds, V-2 1.2 seconds, others under 2 seconds plus
PowerShell startup. Estimates are provisional for CARD-1989, collect one red and
one green sample. No repeats after green. The first row's 15-minute estimate is
the brief's cold bootstrap budget, not an execution timeout increase. Each row
is serial and capped at 570 seconds. Build the checkpoint driver in native bin/;
command rows need no build, avoiding prohibited custom output directories.

## Verification design

V-1: a live read over the old five seconds passes inside eight seconds.
V-2: a hash that exceeds a one-second configured budget refuses before Git, names
bundle-hash and reports its actual elapsed time.
V-3: all step durations are populated and Git receives only remaining budget.
R-1: withdrawn refs and bundle tampering still refuse.
V-4: absent, empty, valid and invalid configuration states.
V-5: coordinator persists success/failure timing receipts (offline protocol fixture).
R-2: affected class selections, bounded to short rows; no whole slow class.
R-3: pin recomputation, land graph, evidence diff, merge-tree, OS branch audit.
V-6: inspect whether renewals can share a bundle without changing proof semantics.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S1-S2 | n/a | window | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case window` | V-1 | 1 PASS, zero failures | n/a | 15 | true |
| CP-2 | S1-S2 | n/a | deadline | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case deadline` | V-2 | 1 PASS, zero failures | n/a | 1 | true |
| CP-3 | S1-S2 | n/a | timings | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case timings` | V-3 | 1 PASS, zero failures | n/a | 1 | true |
| CP-4 | S1-S2 | n/a | guards | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case guards` | R-1 | 1 PASS, zero failures | n/a | 1 | true |
| CP-5 | S1-S2 | n/a | config | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case config` | V-4 | 1 PASS, zero failures | n/a | 1 | true |
| CP-6 | S1-S2 | n/a | receipt | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case receipt` | V-5 | 1 PASS, two protocol exchanges, zero failures | n/a | 1 | true |
| CP-7 | S1-S2 | n/a | host-proof | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Build -Class VolumeRetirementHostProofTests` | R-2 | 19 fresh TRX results, zero failures | n/a | 15 | true |
| CP-8 | S2 | n/a | adjacent-contracts | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class '(ClaudeTokenRefreshOptInTests*)\|(DockerStackContractTests*)\|(HostJqFixtureClosureTests*)\|(RetiredTempContainerScriptTests*)\|(RollingVolumeRecycleScriptTests*)'` | R-2 | every named class nonzero, zero failures | n/a | 3 | true |
| CP-9 | S2 | n/a | remote-bridge | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RemoteScriptContractTests -Method '(Custody_containment_is_routed*)\|(Temp_retire_requires*)\|(C849_Cache_cases_use_only*)'` | R-2 | 3 fresh TRX results, zero failures | n/a | 2 | true |
| CP-10 | S2 | n/a | host-jq | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class HostJqPrerequisiteScriptTests -Method '(C1058_Receipt_formatter*)\|(C1058_Proof_is_buffered*)\|(C1025_Check_qualifies*)'` | R-2 | each named method nonzero, zero failures | n/a | 2 | true |
| CP-11 | S2 | n/a | exchange-content | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementContentTests -Method Exchange_*` | R-2 | 8 fresh TRX results, zero failures | n/a | 3 | true |
| CP-12 | S2 | n/a | effect-fence | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementReceiptTests -Method '(One_repository_measured*)\|(Control_observation_older*)\|(Every_delete_rechecks*)'` | R-2 | 3 fresh TRX results, zero failures | n/a | 3 | true |
| CP-13 | S2 | n/a | publication-control | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementPublicationTests -Method '(Salvage_withdrawn_after_staging*)\|(Head_rolled_back_after_staging*)\|(Origin_unreachable_after_staging*)\|(Final_origin_binding_refresh*)'` | R-2 | 4 fresh TRX results, zero failures | n/a | 4 | true |
| CP-14 | S2 | n/a | apply-guards | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementApplyGateTests` | R-2 | every method, zero failures | n/a | 3 | true |
| CP-15 | S2 | n/a | wrappers | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementWrapperTests` | R-2 | 7 fresh TRX results, zero failures | n/a | 3 | true |
| CP-16 | S2 | n/a | retired-host | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RetiredTempContainerHostTests -Method C994_Each_removal_rechecks_live_proofs` | R-2 | 1 fresh TRX result, zero failures | n/a | 2 | true |
| CP-17 | S2 | n/a | ref-publication | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class C0824BranchDeletionRecycleAuditTests` | R-2 | 3 fresh TRX results, zero failures | n/a | 3 | true |
| CP-18 | S2 | n/a | bound-land-wait | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class C1364BoundLandWaitProofTests -Method C1364_Wrapper_*` | R-2 | 3 fresh TRX results, zero failures | n/a | 2 | true |
| CP-19 | S2 | n/a | layering | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class C1729LayeringGuardTests` | R-3 | zero violations, listed 162 | n/a | 2 | true |
| CP-20 | S2 | n/a | derived-index | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class DerivedImpactIndexTests -Method committed_index_matches_the_builder` | R-3 | 1 fresh TRX result, zero failures | n/a | 2 | true |
| CP-21 | S2 | n/a | declaration-category | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class TestLaneCategoryGuardTests` | R-3 | every method, zero failures | n/a | 2 | true |

The native-output adapter builds once at CP-7, records committed source and DLL
SHA-256, and refuses reuse at a different SHA or after a DLL change. Each row
emits a fresh TRX with counts; checkpoint command-row buildSource is honestly
notApplicable, with the additional native build binding in `.antiphon/c1989-native`.
All 17 impacted classes are represented, including TestLaneCategoryGuardTests
selected by the changed test declaration. The large adjacent classes have
method-scoped selections under the operator's SHORT-only override; the named
invariants are origin publication, final control binding, receipt and effect
fences, wrapper admission, helper custody, and unchanged bridge routing. No whole
Slow class or Unit lane is selected. No classification, selector or pin inputs are
edited, so the operator's explicit override omits the broad guard union.

Native Windows execution is not claimed. Changed logic is portable PowerShell,
with no new OS branch or production factory. Production script remains ASCII and
uses PowerShell 5.1-compatible syntax. No live retire-temp preview/apply is allowed.
Restart: none.

## Renewal cost decision (V-6)

`c1609_publication_gate` renews at every gate. The coordinator requests required
object coverage on renew; `Get-C1008RetireProofExports` passes `-FreshGraph`, and
`Invoke-C1609HostOriginProofAttempt` disables bundle reuse for FreshGraph. The
existing `Fresh_graph_renewal_fetches_again_for_unchanged_advertisement` witness
requires a second real fetch. Sharing one downloaded bundle across the five
renewals would change that invariant, so this repair retains it. The Debug note
measured 541.8 seconds of network across the renewals; this repair makes control
cost attributable but does not claim to reduce renewal traffic.

## Red provenance

Production remained byte-identical to base for the red runs. CP-1..CP-4 failed at
77f3c71a3df13da162a0671b1c91a638f88b458a: 8000 rejected, over-bound hash admitted,
timings missing, and widened-bound ref guard rejected as origin before reaching
the intended ref comparison. CP-5's absent-key fixture was corrected, then failed
for the missing config function at 342e4a0909ddb4480894a72cf41ff181bd4c5e2c.
CP-6's cleanup fixture was corrected, then failed for the missing timing receipt
at 4a579f8b6c1b561a77618ef0cb05c2e76fd3f45f. CP-7 at 342e4a0909ddb4480894a72cf41ff181bd4c5e2c
executed 19 methods: 18 passed, only the updated 8000-ms acceptance failed.
An earlier CP-7 was stopped and joined before any test child launched; its native
argument adapter was repaired to use literal ProcessStartInfo arguments. Preserve
these fixture failures and stopped-run costs; they are not passing evidence.

At production commit 81ba831c0cf3cdc3961a662fc938affb4de875f6 CP-1, CP-2, CP-4
and CP-5 passed. CP-3 correctly refused when its cold fixture first imported the
file-management module during cleanup, exceeding its unchanged 1000-ms budget.
Fixture setup now uses New-Item, matching the real proof exchange's preparation;
the budget and all assertions remain unchanged. CP-6 was stopped and joined while
still polling host load, before its fixture began. Rerun only CP-3 and CP-6.
