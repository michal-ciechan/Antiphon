# CARD-1217 S1 R2-1 repair, last ordinary Review round

Original Code/landing owner: `36bb3715-4e5e-4038-9a81-5ec024e60a99`.
Repair task: `8711a7d0-8ec6-413a-b275-ef7af0da004b`.
Review range starts at `2d186060257cff3815d6f26a71378c415e57f279`.

Restore XML 1.0 character validation without changing literal CR/LF projection.
Refuse every forbidden control range, surrogate halves, noncharacters FFFE/FFFF,
out-of-range/overflow and malformed numeric references, in decimal and hex, in
element text, attributes and skip reasons. Accept valid boundary references.
CDATA/comment reference spellings remain literal. DTD/size guards remain closed.

The shared literal corpus drives strict text/file projection and both real receipt
validator entry points. The existing PowerShell XML-consuming path is the repeat
receipt path: use an otherwise qualifying repeat report with intact marker evidence
and change only irrelevant text/attribute/DebugTrace content. This proves lexical
refusal, not S2 conditional skip eligibility (still pending). A valid control must
qualify before negatives count. New tests must fail on the old strict reader.

No OS-selected production branch changes. Native Windows/rc startup and prior
S2/S3/405 Scripts/whole Unit obligations remain pending, as explicitly commissioned.
Never run the whole assembly. No restart is required for this standalone tool repair.

## Performance budget

Retain prior 4-minute CP-1..3 and 6-minute guard/source/repeat/script row caps;
one build cap 28 minutes. Prior source `64e767520` measured build 408s, CP-1 12s,
CP-2 9s, CP-3 8s, guard 204s. Script rows were bounded individually in round 2;
their historical timing remains in `.antiphon/task-4c959a75.md`. New XML corpus
uses one PowerShell host and retains its 30-second child deadline. Setup is one
test-owned directory with independent TRX/report pairs; no database or web host.
Record new body/build/slot/teardown times. Provisional estimates are inherited
CARD-1720/CARD-1404 obligations, not measured p95 claims. A selection over 30
minutes retains those existing Debug obligations; no deadline widening or green
repetitions. Execution allowance 55 minutes, admission separately bounded at 10
minutes per grant. Red is one exact new unit method; green is the closed 16 rows.
An ignored resolved YAML retains explicit existing row caps and build properties;
it is generated mechanically from this table before launch and retained as evidence.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-RED | RED | tests/Antiphon.Tests -> bin-c1217-8711-red/ | r2-1-red | `/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipContractTests*/StrictTrxRefusesIllegalCharacterReferences` | R2-1 red | red | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-1 | GREEN | tests/Antiphon.Tests -> bin-c1217-8711-green/ | r2-1 | `/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipContractTests*/*` | V-1..4; R2-1 | green | 8 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-2 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Checkpoints/(CheckpointManifestTests*)\|(CheckpointImportTests*)\|(TrxReportTests*)/*` | R-1 | green | 48 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-3 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Checkpoints/CheckpointRepeatConfigurationTests*/*` | R-1 | green | 5 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-GUARDS | GREEN | CP-1 | r2-1 | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(CheckpointToolProjectContractTests*)/*` | R-4 | green | 80 | 3 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-SOURCE | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests*/*` | R-3; R2-1 parity | green | 14 | 3 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-REPEAT | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Checkpoints/CheckpointRepeatHostTests*/*` | R-3 | green | 3 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-1 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C1035_ChildCwdMatchesCertifiedRoot` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-2 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_DiagnosticReceipts` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-3 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_StrictAdmission` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-4 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_DriftAndReuse` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-5 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_TerminalEvidence` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-6 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_ReceiptValidation` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-7 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatBuildAndSingleRun` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-8 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatReceiptAndTimings` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-9 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatFailurePartialAndInvalidInput` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-10 | GREEN | CP-1 | r2-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatNoBuildAndLeaseCustody` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |

The 16 GREEN rows preserve all 166 prior cases and add two tests (168 expected).
PC-1..PC-102 and B-1/TRX, B-1/C, B-1/P, B-2/C, B-4/C remain pending
SourceLanding Mutation. Additional R2-1 variants: omit validation, validate only
decimal, validate only hex, normalize CRLF. Binding/cycles/discovery belong to
Mutation; Code's old-source red does not discharge them.
