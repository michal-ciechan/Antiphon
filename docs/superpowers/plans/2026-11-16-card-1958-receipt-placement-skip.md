# CARD-1958 checkpoint receipt placement skip

Base: `61259342c25c9960c12ac7171488e52b61dc03be`. Code owner: `e2208352-fab7-418c-9ef0-b31720046783`.

Implement the card's closed allowance in `scripts/validate-checkpoint-receipt.ps1`.
The report host must identify Linux or Darwin, every NotExecuted result must join
one exact TestDefinitions identity in the committed WindowsOnly roster at the
expected source SHA, and TRX counts must agree with the receipt. Unknown host,
missing or ambiguous TRX, unlisted skips and failed results refuse. Preserve
source qualification, receipt/schema formats, repeat requirements and deadlines.
Standalone source.json lacks recorded host evidence and therefore cannot use the
allowance. Zero-skip receipts keep their existing behavior.

Add C1210's existing Windows-placement audit to WindowsOnly and route its existing
skip through that roster. Native Linux exclusions must never grant this allowance.
No server, runner, delivery, lease, persistence, migration or startup behavior changes.

## Performance budget

Provisional estimates, not measured p95: source script class 3 minutes, repeat
script class 3 minutes, checkpoint classes 2-4 minutes per row, guards 3 minutes.
Collect one red selection and one green selection, no green repetitions required.
Use shared checkpoint output for the final rows. Bound every row to 570 seconds;
retain existing nine-minute script build deadline. Build-slot admission is separate
(45-minute existing gate), read /proc/loadavg before each build/row, admit below 16.
No new fixtures with database or service startup. Existing scratch Git/process
fixtures isolate each receipt; TRX and checkpoint phase evidence retain cost.
Bootstrap the checkpoint CLI and index driver separately through build-slot,
native output, UseAppHost=false. Those are listed bootstrap work, not extra proofs.

## Verification design

V-1: C1210 skipped on recorded Linux is VALID. V-2: unlisted/mixed skips INVALID.
V-3: recorded Windows/unknown host skips INVALID. V-4: rostered passing result on
Linux retains VALID. R-1: missing/ambiguous TRX, count disagreement and failed
results INVALID. R-2: wrong SHA and uncommitted roster edits cannot grant eligibility.
R-3: whole validator/repeat and adjacent checkpoint classes retain behavior.
R-4: required guard union, derived index and nightly pin identities retain behavior.
Manual M-1: inspect fresh TRX per intended class/method and validate real guard
report against exact tested SHA; preserve unedited CHECKPOINT lines/provenance.
Manual M-2: ASCII, full-range evidence-diff, OS witness declaration, merge-tree,
remote task ref, index regeneration twice (second unchanged), pin input audit.

PC-1 (pending Mutation/operator waiver): remove the closed allowance; V-1 fails.
PC-2 (pending): admit unlisted identities; V-2 fails.
PC-3 (pending): bypass host admission; V-3 fails.
PC-4 (pending): read working roster instead of committed roster; R-2 fails.
PC-5 (pending): union the Native roster into WindowsOnly; the native-roster
variant of C1958_Receipt_skip_allowance_is_closed must fail.
Deliberate mutants belong to SourceLanding Mutation; operator waived execution.

CP-1 has six existing cases and nineteen C1958 argument cases (25 results).
The native-roster negative case specifically distinguishes Windows-only placement
from the opposite native-Linux roster in the same file. It was added after a
static census found the initial minimum one above the then-declared case count;
the minimum and outcome assertions were not reduced. The first Final shared
build timed out before any TUnit discovery, so no prior TUnit result is credited.

### Checkpoints

All rows serial. Final run CP-1..CP-9; red-first selection CP-1 before repair.
Use CLI `--row-timeout 9m --parallel 1`; the public tool invocation also carries
`--timeout 570s` as required by the brief. The existing tool constructs its child
argv itself; the nine-minute row cap bounds those inherited children.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c1958-final/ | source-script | `/*/*/RunCheckpointSourceScriptTests*/*` | V-1,V-2,V-3,V-4,R-1,R-2 | all listed, 0 failed | 25 | 3 | true |
| CP-2 | all | CP-1 | repeat-script | `/*/*/RunCheckpointRepeatScriptTests*/*` | R-3 | all listed, 0 failed | 1 | 3 | true |
| CP-3 | all | CP-1 | checkpoint-source | `/*/*/CheckpointSourceExecutionTests*/*` | R-3 | all listed, 0 failed | 1 | 4 | true |
| CP-4 | all | CP-1 | checkpoint-terminal | `/*/*/(CheckpointTerminalReceiptTests*)\|(CheckpointStatePublicationTests*)/*` | R-3 | all listed, 0 failed | 2 | 4 | true |
| CP-5 | all | CP-1 | checkpoint-shape | `/*/*/(CheckpointShapeConflictTests*)\|(CheckpointTrackedBaselineTests*)/*` | R-3 | all listed, 0 failed | 2 | 3 | true |
| CP-6 | all | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-4 | all listed, 0 failed; C1210 placement skip allowed only with recorded non-Windows host | 114 | 3 | true |
| CP-7 | all | CP-1 | roster-callers | `/*/*/(CardFilePrivacyGitTests*)\|(CardFilePrivacyPathTests*)/*` | R-3 | all listed, 0 failed; six Windows junction variants skip on Linux | 1 | 3 | true |
| CP-8 | all | n/a | placement-cli | `pwsh -NoProfile -File scripts/test-checkpoint-placement-skip.ps1 -Label checkpoint` | V-1,V-2,V-3,V-4,M-1 | five synthetic receipt cases, 0 assertion failures | n/a | 1 | true |
| CP-9 | all | CP-1 | release-impact | `/*/*/ReleaseImpactEnvelopeTests*/*` | R-4 | all listed, 0 failed | 1 | 2 | true |

The full task-range selector additionally names ReleaseImpactEnvelopeTests for
the regenerated index (CP-9), and test-lane-category (already whole in CP-6).
It reports no capped/owed classes or Windows-only filter. No selector input or
selection row is changed by this task.

The derived reverse edges for WindowsUnitSkipRoster select CP-7. The hand
native-linux-gate-roster row selects C1210_Roster_closure_matches_source_gates,
already included whole in CP-6. After the pre-fix cold build hit its unchanged
nine-minute cap with no tests, a direct five-case PowerShell validator fixture
provided the behavioral red witness (Linux skip expected VALID, actual row_failed;
the other four controls passed). This unlisted manual acceptance run is diagnostic,
does not replace the class rows, and is retained under .antiphon/c1958-manual-red.
The same five-case fixture is now a committed, separately runnable CP-8 with a real
ProcessStartInfo/Process.Start validator child. Its ten-second per-child watchdog
is provisional (five cases took six seconds in the first green manual sample),
not measured p95. This portable receipt-data witness forces Linux/Windows host
metadata through the real script; it uses no native Windows API. OS execution of
the C1210 child audit itself is inherited pending Windows work, not passed here.

No full Unit lane: nightly/rc owns it. No spans or selector rows change, so the
conditional TestTimingOperationLabelTests/ImpactedClassesSelectorTests obligation
is not triggered. Extend only for impacted-classes rows matching the actual diff;
record any such selection before executing it. Native Windows C1210 execution
remains pending and this receipt allowance does not discharge it. No native APIs
change; Windows-recorded rejection is exercised via the real validator process.

Follow-up: the card proposes a per-row build deadline derived from measured p95
plus headroom; this task does not change run-checkpoint.ps1 deadlines.
