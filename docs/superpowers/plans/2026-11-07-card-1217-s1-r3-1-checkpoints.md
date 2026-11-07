# CARD-1217 S1 R3-1: shared C# XML refusal repair

Task fd3883d6. Original Code/landing owner 36bb3715-4e5e-4038-9a81-5ec024e60a99.
Base/reviewed tip: 82216aa85fedf8e0c62cc5de9a77f6404f36680e.
Enforce DTD/custom entity refusal in every tool XML consumer through shared secure settings;
retain diagnostic structural tolerance, numeric references and literal strict CR/LF.
The shared parity corpus adds the exact four reviewer DTD vectors, used internal/external
entities, parameter entity, expansion shape, undefined entity, and valid comment/CDATA/escaped
attribute/predefined entities. Raw less-than inside an attribute is invalid XML and must refuse.
All cases use independent expected exit codes through both real validators and strict file/text.

## Performance budget

One cold build per committed group, native CLI bootstrap separately leased to launch the tool.
Retain previous 28-minute build, 4-minute unit and 6-minute integration/guard caps,
10-minute per-slot admission, 55-minute execution and 65-minute hard wall. Review 38ee4b12
measured 265s build, 104s guards, 121s source class and 1096s complete 16-row execution.
No sample p95 claim: CARD-1720/CARD-1404 provisional obligations remain.
One owned PowerShell host per parity corpus with unchanged 30-second child bound;
private files only, no DB/AppHost. Keep membership, per-test TRX and PHASES.
No full assembly/whole Unit or 405 owed Scripts methods: brief explicitly forbids them.
No changed OS branch; use real BCL readers and script input. Restart: none.
All PC-1..PC-102 and existing B/R2 variants plus R3 candidates remain Mutation-owned pending.

The first CP-RED run (29b975715) had an invalid fixture prolog: comment/DTD
preceded an XML declaration. Retain it as fixture failure, no R3-1 credit.
CP-RED2 corrects insertion after the declaration; CP-XML-RED covers every
additional XML consumer with independent controls before the production repair.
CP-2 also includes the complete new security class and existing coverage command
class because project XML discovery now uses the shared reader. No broad assembly.

### Checkpoints

totalTimeoutMinutes: 55

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-RED | RED | tests/Antiphon.Tests -> bin-c1217-fd3883-red/ | r3-1-red | `/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests*/validators_agree_on_xml_character_references` | R3-1 red | red | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-RED2 | RED2 | tests/Antiphon.Tests -> bin-c1217-fd3883-red2/ | r3-1-red2 | `/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests*/validators_agree_on_xml_character_references` | R3-1 red | red | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-XML-RED | RED2 | tests/Antiphon.Tests -> bin-c1217-fd3883-red2/ | r3-1-red2 | `/*/Antiphon.Tests.Checkpoints/XmlInputSecurityTests*/*` | R3-1 parse sites | red | 4 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-1 | GREEN | tests/Antiphon.Tests -> bin-c1217-fd3883-green/ | r3-1 | `/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipContractTests*/*` | V-1..4; R2-1 | green | 8 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-2 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Checkpoints/(CheckpointManifestTests*)\|(CheckpointImportTests*)\|(TrxReportTests*)\|(XmlInputSecurityTests*)\|(PlanCoverageCommandTests*)/*` | R-1 | green | 48 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-3 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Checkpoints/CheckpointRepeatConfigurationTests*/*` | R-1 | green | 5 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-GUARDS | GREEN | CP-1 | r3-1 | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(CheckpointToolProjectContractTests*)/*` | R-4 | green | 80 | 3 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-SOURCE | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests*/*` | R-3; R2-1 parity | green | 14 | 3 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-REPEAT | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Checkpoints/CheckpointRepeatHostTests*/*` | R-3 | green | 3 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-1 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C1035_ChildCwdMatchesCertifiedRoot` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-2 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_DiagnosticReceipts` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-3 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_StrictAdmission` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-4 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_DriftAndReuse` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-5 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_TerminalEvidence` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-6 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_ReceiptValidation` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-7 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatBuildAndSingleRun` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-8 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatReceiptAndTimings` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-9 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatFailurePartialAndInvalidInput` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-R3-10 | GREEN | CP-1 | r3-1 | `/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatNoBuildAndLeaseCustody` | R-3 | green | 1 | 1 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
