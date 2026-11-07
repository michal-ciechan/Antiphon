# CARD-1217 S1 R3-1 Code repair

R3-1 is repaired: the C# receipt validator refuses DTD/custom-entity input with exit 2, matching PowerShell. The commissioned 16 green rows passed **182/182, zero failures/skips**, retaining every identity/display from Review's original 168 results. This completes this narrow repair; the original feature's Full Final gate remains incomplete with the explicitly excluded obligations below.

## Identity and publication

- Task: fd3883d6-7494-46b0-8c95-3e99025b6b79; original Code/landing owner: **36bb3715-4e5e-4038-9a81-5ec024e60a99**.
- Branch: feat/card-task-fd3883d6; worktree: /work/worktrees/task-fd3883d6. No desktop work.
- Task base / previous reviewed source: **82216aa85fedf8e0c62cc5de9a77f6404f36680e**.
- Initial test/fixture-failure commit: 29b97571511e78c104731e8eeb920199a44de20f.
- Corrected fixture/new reader tests: 1a7e286dd04522ef8136a07b8d75780817386fd7.
- Explicit shared-build manifest correction: 3684e39601973a70969c9b3018572005f989891d.
- Retained-cap explanation / intended-red tested SHA: **668b70c93b9bea9b29abd50e529aa4d34e9136a0**.
- Production fix / actual green tested SHA: **212acb94c17601cc5c5e99ac67cc6997a3121b54**.
- Each slice was committed and pushed separately. The final report-only publication SHA is the final progress marker; its production/test bytes equal the actual tested SHA. No rebase, amend, reset, branch switch, merge or force-push.

Plan: docs/superpowers/plans/2026-11-07-card-1217-s1-r3-1-checkpoints.md.
Frozen design: docs/superpowers/testdesign/2026-11-06-card-1217-testdesign.md.
Unbound candidates: docs/superpowers/mutations/2026-11-06-card-1217-s1-repair-candidates.md.
Full persistent evidence: **/work/code-evidence/task-fd3883d6/**. Original paths in CHECKPOINT lines are execution-time paths; byte-identical archived run files and hashes are under this root's runs/ and run-archive-hashes.json.

## Exact implementation delta from 82216aa85

**11 files, 277 insertions, 32 deletions** before this report-only slice. Full binary-capable patch: /work/code-evidence/task-fd3883d6/implementation.diff, SHA-256 **3ffbf3ed402c8cdfb768eb37ad16ba30575c5a6f7fed5310b7a12b3811f00b6c**. The final full task-base..HEAD patch is final.diff in the same evidence root.

- SecureXml.cs supplies shared character-checking reader settings: DtdProcessing=Prohibit, XmlResolver=null, MaxCharactersFromEntities=0, MaxCharactersInDocument=16 Mi characters. DTD prohibition prevents custom entity expansion; 0 is not claimed to be an independent expansion quota.
- TrxReport, StrictTrxReport, RepeatRequest, ProjectWriteRoots and CoverageCommand use that boundary. The strict path validates first, then projects original text with a non-normalizing XmlTextReader and the same checking settings. Diagnostic structural tolerance, fallback names/counters and literal strict CR/LF remain covered.
- The shared fixture adds 28 vectors to the original 298: **326** independent parity inputs. The first four are exactly the review declarations: bare DOCTYPE; internal reason entity declaration; external SYSTEM DTD; external SYSTEM entity declaration. Additional cases use internal/external entities, a parameter entity, expansion shape and undefined entities. Valid comment, CDATA and escaped attribute literals plus all five predefined entities in text/attribute/reason contexts qualify. Raw less-than inside an attribute refuses as invalid XML.
- The existing real dual-validator test covers the complete corpus, exact exit codes, strict text/file projection and unchanged input bytes. Four new unit methods cover diagnostic lexical behavior and size, repeat-project/reference-closure XML, and coverage-project XML through real entry points after accepted controls. All four failed on unchanged old production.
- Plan/candidate documents retain bounded groups, historical failures, ownership and pending PCs. No PowerShell production change, timeout widening, assertion weakening, retry, policy JSON change, nightly policyHash change or live-lane PinnedInputDigest change.

## XML parse-site audit

The recursive XmlReader/XmlTextReader/XDocument/XmlDocument/XmlSerializer search is retained in parse-sites.txt. Old reader sites and their replacements:

| Consumer | Current production site | Shared behavior |
|---|---|---|
| Diagnostic TRX, consumed by ReportValidator repeat qualification | tools/Antiphon.Checkpoints/Trx/TrxReport.cs:68 | SecureXml.Parse; malformed XML yields Invalid/2 |
| Strict text/file TRX | tools/Antiphon.Checkpoints/Trx/StrictTrxReport.cs:41 | SecureXml.Parse with literal-line-ending projection |
| Repeat TUnit project XML | tools/Antiphon.Checkpoints/Execution/RepeatRequest.cs:27 | SecureXml.Parse |
| ProjectReference closure | tools/Antiphon.Checkpoints/ProjectWriteRoots.cs:132 | SecureXml.Parse; existing closure refusal catches XmlException |
| Coverage linked Compile source discovery | tools/Antiphon.Checkpoints/Coverage/CoverageCommand.cs:159 | SecureXml.Parse; existing CLI invalid-input result is 2 |

The old StrictTrxReport validation and projection were two reader sites, giving six original reader sites in these five consumers. All XmlReader creation/XDocument.Load calls now reside in SecureXml.cs:21,23,29,36,37. The normal path loads a settings-backed reader; literal projection first validates with shared settings, explicitly prohibits DTDs and resolver use in the raw reader, and wraps it with shared settings. No independent XDocument.Parse, XmlDocument or XmlSerializer remains in tools/Antiphon.Checkpoints. ReportValidator.cs:101 still reaches the secured diagnostic reader; no qualification-only guard can be bypassed by another tool parser.

## Actual red and green outcomes

1. Run **20261107-013357-a333**, SHA 29b97571511e78c104731e8eeb920199a44de20f: 1 executed, 0 passed, 1 fixture failure, 0 skipped. DOCTYPE/comment prefixes were incorrectly placed before the template's XML declaration. PowerShell correctly refused the invalid comment acceptance control. **No R3-1 red credit**. Retained without relabeling.
2. Run **20261107-014017-1ae9**, SHA **668b70c93b9bea9b29abd50e529aa4d34e9136a0**: 5 executed, **5 intended assertion failures**, 0 skipped; one isolated build. Production tool bytes are unchanged from the reviewed base. The parity assertion reports all four exact reviewer cases and internal-used/external-used/parameter/expansion cases: C# actual 0 versus independently expected 2, while PowerShell and strict text/file readers match expectations. The other four tests fail on bare-DTD diagnostic/repeat-project/coverage acceptance and unbounded diagnostic document acceptance. This is ordinary old-product red, not Mutation.
3. Run **20261107-014847-e839**, SHA **212acb94c17601cc5c5e99ac67cc6997a3121b54**: 16 rows, **182 passed / 0 failed / 0 skipped**. The full affected source class, repeat host class, two existing script classes, and the complete additional XML/coverage classes passed. All original 168 method/display identities are present; added results are in CP-2. Both real receipt validators accepted this complete exact-SHA report with exit 0.

| Row | Executed / Passed / Failed / Skipped | slot / waited | Row wall |
|---|---|---|---|
| CP-1 | 8 / 8 / 0 / 0 | granted / 0s | 9.316s |
| CP-2 | 62 / 62 / 0 / 0 | granted / 0s | 13.794s |
| CP-3 | 5 / 5 / 0 / 0 | granted / 0s | 9.217s |
| CP-GUARDS | 80 / 80 / 0 / 0 | granted / 0s | 100.554s |
| CP-SOURCE | 14 / 14 / 0 / 0 | granted / 0s | 118.725s |
| CP-REPEAT | 3 / 3 / 0 / 0 | granted / 0s | 17.176s |
| CP-R3-1 | 1 / 1 / 0 / 0 | granted / 0s | 31.814s |
| CP-R3-2 | 1 / 1 / 0 / 0 | granted / 0s | 57.007s |
| CP-R3-3 | 1 / 1 / 0 / 0 | granted / 0s | 46.144s |
| CP-R3-4 | 1 / 1 / 0 / 0 | granted / 0s | 106.928s |
| CP-R3-5 | 1 / 1 / 0 / 0 | granted / 0s | 37.983s |
| CP-R3-6 | 1 / 1 / 0 / 0 | granted / 0s | 101.825s |
| CP-R3-7 | 1 / 1 / 0 / 0 | granted / 0s | 17.592s |
| CP-R3-8 | 1 / 1 / 0 / 0 | granted / 0s | 24.049s |
| CP-R3-9 | 1 / 1 / 0 / 0 | granted / 0s | 20.105s |
| CP-R3-10 | 1 / 1 / 0 / 0 | granted / 0s | 27.981s |

Raw fresh TRX is independently inspected by audit-trx.ps1; fresh-trx-audit.json/.log and fresh-roster.json retain exact method/display membership, raw counters, all original review multisets, every added method with nonzero results, unique execution IDs with no baseline reuse, granted slots, stable clean source fingerprints and verified build provenance. No constant/self-comparison substitutes for expected verdicts. No green row was repeated, no load repetition or flake exemption used.

## Performance and extra drivers

Green run wall **1141.336s**, charged execution **1141.318s**, admission-only **0.017s**. Per-row/body/startup/teardown/build values remain in raw TRX, PHASES, report.json and report.md. Fine-grained parser/file-IO attribution is unknown, not zero; existing CARD-1514 instrumentation debt remains. No DB or web-host setup was added. Green build 294.046s. No run/selection exceeded 30 minutes. Existing CARD-1720/CARD-1404 provisional measurement obligations remain; these samples are not a p95 claim.

Historical red wall/build: initial fixture run 273.073s / 221.447s; corrected intended-red group 394.095s / 306.501s. The corrected parity test body was 34.883s total including both validators and projection; its PowerShell child still has the unchanged 30-second bound.

All rows/builds report **slot=granted waited=0s**. One cold test-project build per committed group; serial rows; UseAppHost=false; TEMP/TMP/TMPDIR=/tmp; 4/6-minute row caps, 28-minute build cap, 10-minute per-grant admission cap, 55-minute execution allowance and 65-minute hard wall. Historical red rows are separate from GREEN; the manifest explains why the retained 55-minute cap is below the sum-derived allowance for all historical and green rows combined. No timeout was increased.

Only extra build drivers were CLI bootstraps needed to launch/qualify checkpoints, each through scripts/build-slot.ps1 with native default output and UseAppHost=false: initial bootstrap at base tool bytes (6.14s build, 7s lease), and fixed-source bootstrap at 212acb94c17601cc5c5e99ac67cc6997a3121b54 (4.28s build, 5s lease; fixed-cli-build.log). Initial bootstrap was launcher-only and gives no candidate test credit. Both slots were granted, waited=0s, maxcpucount=6. No unlisted test driver ran. Import, wait, version, receipt qualification, XML inventory, Git/history/mergeability and TRX audit are read-only diagnostics, not extra builds/tests.

Historical no-execution diagnostics: a YAML was first passed to --plan (the Markdown importer) and refused; subsequent runs used the positional YAML argument. Named red build reuse required an explicit project/output identity; import also required the retained-cap explanation after historical rows were added. Those refusals launched no build/test. OS lint's first call lacked its declaration file and refused; after writing the explicit inspected-path inventory it passed. No failure was credited as a pass.

## Ordinary ID ledger and inherited obligations

Verification profile remains Final; **ordinaryScopeCompleted: Incomplete** for the original full feature. The brief explicitly forbids whole Unit and the 405 Scripts methods. The narrow repair's closed ordinary selection is complete; it does not manufacture Full or relabel the task Interim.

| ID | Actual outcome |
|---|---|
| V-1 | Passed: declaration round trip, CP-1 |
| V-2 | Passed: malformed declaration refusal, CP-1 |
| V-3 | Passed: strict identity/reason, encodings, numeric references and literal CR/LF, CP-1 |
| V-4 | Passed: retained diagnostic structure and unsupported combinations, CP-1; diagnostic regression CP-2 |
| V-5 | Pending S2 validation unavailable/available qualification |
| V-6 | Pending S2 exact declared-skip roster |
| V-7 | Pending S2 foreign skip/reason truth table |
| V-8 | Pending S2 conditional malformed-TRX qualification |
| V-9 | Pending probe-binding conditional qualification |
| V-10 | Pending complete conditional source/failure fences; existing legacy fences pass in named classes |
| V-11 | Pending real classifier/probe qualification |
| V-12 | Pending production probe ownership/timeouts/cleanup |
| V-13 | Pending real scheduler busy/eligible, publication/crash cuts, persisted conditional evidence and fresh nonce recovery |
| V-14 | R3-1/R2-1 lexical dual-validator parity passed in CP-SOURCE; full new conditional-envelope truth table remains pending |
| V-15 | Pending committed conditional authority and standalone proof |
| V-16 | Pending real conditional producer-to-recipient portable host acceptance |
| V-17 | Pending genuine native Windows available/unavailable receipts |
| R-1 | Passed selected full manifest/import/TRX/repeat-configuration classes, CP-2/3 |
| R-2 | Pending scheduler/report regression CP-6 |
| R-3 | Passed the named source/repeat host classes and all 10 methods of the two script classes; separate 405-method Scripts debt remains owed |
| R-4 | Passed single full guard union, CP-GUARDS |
| R-5 | Pending native/live-lane full-input regression |
| R-6 | Pending FileLink/raw consumer acceptance |
| R2-1 | Passed strict numeric-reference and literal-newline regression |
| R3-1 | Passed all 326 parity inputs and every added reader/size witness after intended old-source red |

Whole Unit (CARD-1720), 405 Scripts obligations (402 historical NotRun + 3 Skipped, never passes), S2/S3 remaining work, full CP-20 consumer evidence, native Windows available/unavailable and Windows rc-startup/manual acceptance remain pending. No nightly, diagnostic fixture or skipped result discharges them. This XML repair changes no messaging/session delivery or input-wait contract; no delivery/native acceptance is claimed.

## PCs and variants: all pending SourceLanding Mutation

PC-1, PC-2, PC-3, PC-4, PC-5, PC-6, PC-7, PC-8, PC-9, PC-10, PC-11, PC-12, PC-13, PC-14, PC-15, PC-16, PC-17, PC-18, PC-19, PC-20, PC-21, PC-22, PC-23, PC-24, PC-25, PC-26, PC-27, PC-28, PC-29, PC-30, PC-31, PC-32, PC-33, PC-34, PC-35, PC-36, PC-37, PC-38, PC-39, PC-40, PC-41, PC-42, PC-43, PC-44, PC-45, PC-46, PC-47, PC-48, PC-49, PC-50, PC-51, PC-52, PC-53, PC-54, PC-55, PC-56, PC-57, PC-58, PC-59, PC-60, PC-61, PC-62, PC-63, PC-64, PC-65, PC-66, PC-67, PC-68, PC-69, PC-70, PC-71, PC-72, PC-73, PC-74, PC-75, PC-76, PC-77, PC-78, PC-79, PC-80, PC-81, PC-82, PC-83, PC-84, PC-85, PC-86, PC-87, PC-88, PC-89, PC-90, PC-91, PC-92, PC-93, PC-94, PC-95, PC-96, PC-97, PC-98, PC-99, PC-100, PC-101, PC-102: **all pending**.

Existing variants B-1/TRX, B-1/C, B-1/P, B-2/C, B-4/C: pending.
R2-1/no-validation, decimal-only, hex-only, normalize-CRLF, P-no-validation, P-decimal-only, P-hex-only, and the requested skip-reason-path variant: pending.
R3-1/allow-DTD, diagnostic-bypass, strict-bypass, repeat-project-bypass, project-closure-bypass, coverage-project-bypass, document-limit and resolver-enabled: pending.

No deliberate mutant, mutation build or restore cycle ran. Code added unbound method-scoped candidates; Mutation owns landed-source binding, red/restore/green and missing-control discovery. Resolver-enabled alone may be equivalent while DTD prohibition dominates; do not claim a combined allow-DTD failure independently detects resolver use. This requested control remains explicitly pending discovery, not passed or silently omitted.

## Platform, custody and final checks

GET /api/runner-defaults and /api/session-runners were read through the session environment's API endpoint and saved as runner-defaults.json/session-runners.json. No fleet address was embedded. Portable work remained on the assigned Linux runner without a desktop operation. Real BCL readers and real validator input were used. Existing OS path-comparison branches are unchanged; os-witnesses.json/.log passes declaration inventory lint, not native Windows execution. Restart: **none**.

Task-base..HEAD evidence history guard and refreshed merge-tree results are retained in final-evidence-diff.log and final-merge-tree.log; full candidate history from original task base 5abd946dd2c235b6e873b0cbb95c738f35216dcf is independently checked in full-candidate-evidence-diff.log. Generated evidence remains ignored/external. Final cleanup/archive records are cleanup.log and run-archive-hashes.json; all copies are hash-verified before owned generated results/native bin/obj/red outputs are removed by exact absolute literal paths. The report-only commit does not relabel the exact tested SHA. Final remote tip/progress marker confirms publication.

Proceed to the caller-authorized Codex Frontier round-4 diff-only blocker review since 82216aa85. Preserve the original landing owner and every Final/PC deferral; this narrow repair does not authorize landing or activation.

## Unedited checkpoint provenance

```text
CHECKPOINT CP-RED commit=29b97571511e78c104731e8eeb920199a44de20f build=ok filter=/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests*/validators_agree_on_xml_character_references executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-013357-a333/rows/CP-RED/run.trx slot=granted waited=0s dirty=0 source=29b97571511e78c104731e8eeb920199a44de20f sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-RED waited=0s executed=29.092s wall=29.139s
PHASES CP-RED slotWait=0s build=221.4472158s startup=7.4457367s testsWall=20.0838363s teardown=0.6556102s hostWall=28.1851382s
BUDGET wall=273.073s execution=273.072s admissionOnly=0.001s
CHECKPOINT CP-RED2 commit=668b70c93b9bea9b29abd50e529aa4d34e9136a0 build=ok filter=/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests*/validators_agree_on_xml_character_references executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014017-1ae9/rows/CP-RED2/run.trx slot=granted waited=0s dirty=0 source=668b70c93b9bea9b29abd50e529aa4d34e9136a0 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-RED2 waited=0s executed=47.884s wall=48.090s
PHASES CP-RED2 slotWait=0s build=306.5005838s startup=11.653523s testsWall=34.8833702s teardown=0.4333478s hostWall=46.9702301s
CHECKPOINT CP-XML-RED commit=668b70c93b9bea9b29abd50e529aa4d34e9136a0 build=reused filter=/*/Antiphon.Tests.Checkpoints/XmlInputSecurityTests*/* executed=4 passed=0 failed=4 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014017-1ae9/rows/CP-XML-RED/run.trx slot=granted waited=0s dirty=0 source=668b70c93b9bea9b29abd50e529aa4d34e9136a0 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-XML-RED waited=0s executed=8.625s wall=8.658s
PHASES CP-XML-RED slotWait=0s build=0s startup=6.4804591s testsWall=0.7793029s teardown=0.7837706s hostWall=8.0435326s
BUDGET wall=394.095s execution=394.093s admissionOnly=0.001s
CHECKPOINT CP-1 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=ok filter=/*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipContractTests*/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=9.268s wall=9.316s
PHASES CP-1 slotWait=0s build=294.0456427s startup=7.229361s testsWall=0.4721446s teardown=0.6997165s hostWall=8.4014363s
CHECKPOINT CP-2 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Checkpoints/(CheckpointManifestTests*)|(CheckpointImportTests*)|(TrxReportTests*)|(XmlInputSecurityTests*)|(PlanCoverageCommandTests*)/* executed=62 passed=62 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=13.743s wall=13.794s
PHASES CP-2 slotWait=0s build=0s startup=8.0854953s testsWall=4.4522675s teardown=0.4387137s hostWall=12.9764765s
CHECKPOINT CP-3 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Checkpoints/CheckpointRepeatConfigurationTests*/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=9.168s wall=9.217s
PHASES CP-3 slotWait=0s build=0s startup=7.4748161s testsWall=0.2481388s teardown=0.6462348s hostWall=8.3691894s
CHECKPOINT CP-GUARDS commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(CheckpointToolProjectContractTests*)/* executed=80 passed=80 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-GUARDS/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-GUARDS waited=0s executed=100.504s wall=100.554s
PHASES CP-GUARDS slotWait=0s build=0s startup=15.9097627s testsWall=83.0935842s teardown=0.7098542s hostWall=99.7132009s
CHECKPOINT CP-SOURCE commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests*/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-SOURCE/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-SOURCE waited=0s executed=118.674s wall=118.725s
PHASES CP-SOURCE slotWait=0s build=0s startup=8.5781466s testsWall=108.6288999s teardown=0.663061s hostWall=117.8701078s
CHECKPOINT CP-REPEAT commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Checkpoints/CheckpointRepeatHostTests*/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-REPEAT/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-REPEAT waited=0s executed=17.124s wall=17.176s
PHASES CP-REPEAT slotWait=0s build=0s startup=8.2440084s testsWall=7.3048623s teardown=0.6562538s hostWall=16.2051248s
CHECKPOINT CP-R3-1 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C1035_ChildCwdMatchesCertifiedRoot executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-1/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-1 waited=0s executed=31.752s wall=31.814s
PHASES CP-R3-1 slotWait=0s build=0s startup=7.3225197s testsWall=22.955972s teardown=0.6819631s hostWall=30.960455s
CHECKPOINT CP-R3-2 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_DiagnosticReceipts executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-2/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-2 waited=0s executed=56.955s wall=57.007s
PHASES CP-R3-2 slotWait=0s build=0s startup=7.4392432s testsWall=47.5407071s teardown=1.1111766s hostWall=56.0911274s
CHECKPOINT CP-R3-3 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_StrictAdmission executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-3/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-3 waited=0s executed=46.076s wall=46.144s
PHASES CP-R3-3 slotWait=0s build=0s startup=12.9228717s testsWall=31.7130902s teardown=0.4710296s hostWall=45.1069913s
CHECKPOINT CP-R3-4 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_DriftAndReuse executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-4/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-4 waited=0s executed=106.877s wall=106.928s
PHASES CP-R3-4 slotWait=0s build=0s startup=7.9899182s testsWall=97.4014598s teardown=0.777483s hostWall=106.168861s
CHECKPOINT CP-R3-5 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_TerminalEvidence executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-5/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-5 waited=0s executed=37.924s wall=37.983s
PHASES CP-R3-5 slotWait=0s build=0s startup=6.7303139s testsWall=29.7169717s teardown=0.6704605s hostWall=37.1177461s
CHECKPOINT CP-R3-6 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointSourceScriptTests*/C835_ReceiptValidation executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-6/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-6 waited=0s executed=101.777s wall=101.825s
PHASES CP-R3-6 slotWait=0s build=0s startup=7.1983372s testsWall=93.1149529s teardown=0.7782585s hostWall=101.091549s
CHECKPOINT CP-R3-7 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatBuildAndSingleRun executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-7/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-7 waited=0s executed=17.524s wall=17.592s
PHASES CP-R3-7 slotWait=0s build=0s startup=8.3326302s testsWall=7.7008645s teardown=0.6960049s hostWall=16.7294997s
CHECKPOINT CP-R3-8 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatReceiptAndTimings executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-8/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-8 waited=0s executed=23.982s wall=24.049s
PHASES CP-R3-8 slotWait=0s build=0s startup=7.5003373s testsWall=15.0117044s teardown=0.6852524s hostWall=23.1972945s
CHECKPOINT CP-R3-9 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatFailurePartialAndInvalidInput executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-9/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-9 waited=0s executed=20.039s wall=20.105s
PHASES CP-R3-9 slotWait=0s build=0s startup=7.97033s testsWall=10.5427861s teardown=0.6989789s hostWall=19.2120976s
CHECKPOINT CP-R3-10 commit=212acb94c17601cc5c5e99ac67cc6997a3121b54 build=reused filter=/*/Antiphon.Tests.Scripts/RunCheckpointRepeatScriptTests*/C885_RepeatNoBuildAndLeaseCustody executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fd3883d6/.antiphon/checkpoints/20261107-014847-e839/rows/CP-R3-10/run.trx slot=granted waited=0s dirty=0 source=212acb94c17601cc5c5e99ac67cc6997a3121b54 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3-10 waited=0s executed=27.918s wall=27.981s
PHASES CP-R3-10 slotWait=0s build=0s startup=7.5016787s testsWall=18.9249466s teardown=0.7021348s hostWall=27.1287485s
BUDGET wall=1141.336s execution=1141.318s admissionOnly=0.017s
```
