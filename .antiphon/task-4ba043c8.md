# Review 4ba043c8 (Final): CARD-1514 run-graph S1 SchemaAndIdentity

Verdict: **found**. 5 defects (D-1 blocks landing on master). The tip is CP-1 green on its own rc/2026-10-27 lineage, but it is red on master and cannot be replayed onto master cleanly.

Subject: origin/feat/card-task-951056e9 at 1ddfd618cfd16b646f285101832cd44ff855dae2 (Code owner task 951056e9). Reviewed in runner mirror /work/worktrees/task-4ba043c8 on server2 (Debian 12, pwsh 7.5.4, SDK 10.0.403). Branch base: rc/2026-10-27 lineage (3fb3c113e); CARD-1514 range is 3fb3c113e..HEAD (16 commits); S1 Code range is a3365ac69..HEAD (9 commits).

## Checkpoint evidence (fresh, this review)

Run 20261031-083030-3a2f, one build of tests/Antiphon.Tests (bin-rv4ba-s1/, TEMP=/tmp, UseAppHost=false Linux default), `validate --expected-source-sha 1ddfd618...` -> `CHECKPOINT SOURCE VALID rows=2`.

~~~text
source: 1ddfd618cfd16b646f285101832cd44ff855dae2 state=clean buildSource=verified
CHECKPOINT CP-1 commit=1ddfd618cfd16b646f285101832cd44ff855dae2 build=ok filter=/*/Antiphon.Tests.Scripts/QualificationGraphContractTests/SchemaAndIdentity executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-4ba043c8/.antiphon/checkpoints/20261031-083030-3a2f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=1ddfd618cfd16b646f285101832cd44ff855dae2 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=14.709s wall=14.778s
PHASES CP-1 slotWait=0s build=373.0409968s startup=4.7064704s testsWall=8.1679918s teardown=0.6441926s hostWall=13.5189003s
CHECKPOINT CP-N commit=1ddfd618cfd16b646f285101832cd44ff855dae2 build=reused filter=/*/*/(ProcessSpawnLimitTests*)|(ScriptHarnessProcessContractTests*)|(SlowTestTripwireTests*)|(TestLaneCategoryGuardTests*)|(TestClassificationPolicyTests*)|(ShellFixtureLineEndingContractTests*)|(NightlyScriptsTests*)/* executed=100 passed=100 failed=0 skipped=0 trx=/work/worktrees/task-4ba043c8/.antiphon/checkpoints/20261031-083030-3a2f/rows/CP-N/run.trx slot=granted waited=0s dirty=0 source=1ddfd618cfd16b646f285101832cd44ff855dae2 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-N waited=0s executed=82.990s wall=83.038s
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 71s tests=39 (CP-N)
BUDGET wall=531.472s execution=531.472s admissionOnly=0.000s
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
~~~

CP-N per class from TRX: ProcessSpawnLimitTests 21, ScriptHarnessProcessContractTests 30, SlowTestTripwireTests 2, TestLaneCategoryGuardTests 1, TestClassificationPolicyTests 39, ShellFixtureLineEndingContractTests 2, NightlyScriptsTests 5. No test in tests/ enumerates docs/schemas. The `*.ps1` hits (AreaMapTests, InternalDecisionPolicyTests) are glob-matching unit tests, not directory enumerations.

Other gated runs, all through scripts/build-slot.ps1, with reasons:
- Checkpoint tool build (bin-rv4ba-tool): needed to run the tool.
- LandGraphAudit Release build: `-Check` refuses without it ("built evaluator missing").
- `scripts/audit-land-verification-graph.ps1 -Check`: **approved**, digest E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69. `-Update` was not run.
- Eight scratch-copy harness runs (`test-qualification-graph.ps1 -Case SchemaAndIdentity` on `git archive` copies): base, six mutants, and an on-master simulation.
- Two probe scripts that dot-source the real library read-only.

`check-evidence-diff.ps1 -Repository . -BaseRef <b> -HeadRef HEAD`: violations=0 for both a3365ac69..HEAD (9 commits) and 3fb3c113e..HEAD (16 commits). `git diff --check a3365ac69..HEAD` is clean.

## Defects

**D-1 (blocker) Not landable on master: replay conflicts, and CP-1 is red on master.**
- **Where:** tests/test-execution-policy.json:3 (policyHash); scripts/fixtures/qualification-graph/s1/preservation.json (`baseSha` a3365ac69, the whole `policy` projection and the `files` hashes); scripts/test-qualification-graph.ps1:171-184.
- **Failure:**
  1. Replaying only the CARD-1514 range onto origin/master 96715b566 conflicts: `git merge-tree --write-tree --merge-base 3fb3c113e origin/master HEAD` exits 1 with CONFLICT (content) in tests/test-execution-policy.json, in one policyHash hunk. A whole-branch merge-tree has 42 conflicts, all inherited rc/master divergence.
  2. Even with that conflict resolved by regenerating the hash, CP-1 is red on master. I simulated this in a scratch tree: origin/master scripts, docs/schemas and policy, plus the S1 files from HEAD, plus the census row, with the hash recomputed through Get-NightlyPolicyHash. Result: `C487: 98 passed, 2 failed`, with FAIL `full-policy` and FAIL `gate-files`.
- **Why:** master's policy carries rows that are absent from the rc base, for example the CARD-1619 Card1619Exit*/StopAttribution exclusions. Master's scripts/lib/nightly-tests-impl.ps1 also differs from the pinned hash. The other nine protected files match.
- **Fix:** re-cut the S1 commits onto master (or land with the range-based lineage contract). Rebind preservation.json to the actual master base, as the test design's "explicit reviewed rebinding" rule requires. Regenerate the policyHash through the tool. Rerun CP-1 on the master-based tip.

**D-2 Permanent change-detector: CP-1 goes red on any future unrelated policy or protected-file edit.**
- **Where:** scripts/test-qualification-graph.ps1:171-184 (`full-policy` compares the entire policy except the new row; `gate-files` compares fixed SHA-256 values of 10 files).
- **Failure:** this is a permanent Integration test in Antiphon.Tests, so nightly runs it. The next card that adds a policy exclusion or edits nightly-tests-impl.ps1 turns QualificationGraphContractTests red with no S1 regression. D-1 is that failure already happening across one rc-to-master step.
- **Why:** the preservation input is described as "a Code/Review slice preservation input, not a release receipt", but it is frozen into a standing test.
- **Fix:** scope the check to the slice. For example, assert that the S1 range's diff touches only the allowed census row and protected files, by comparing against the bound base commit through git rather than whole-file snapshots. Otherwise move full-policy and gate-files into the slice's review evidence. This needs a short test-design amendment, because the companion prescribes these two rows.

**D-3 The integrated catalog-UID recompute guard is unwitnessed (a mutant survived).**
- **Where:** scripts/lib/qualification-graph.ps1:279-285 (`catalog-uid-mismatch`).
- **Failure:** mutant m3 replaced `(Get-QualificationUidSetDigest $uidProperty[0].Value) -cne $node.selection.uidSetDigest` with a self-compare. The full harness then still passed `C487: 100 passed, 0 failed`. The count arm on line 283 is also never asserted, and no case expects `catalog-uid-mismatch`.
- **Why:** the guard that binds catalog UID membership and count to the declared digest can be deleted while CP-1 stays green. `uid-members-changed` only calls Get-QualificationUidSetDigest directly, with a coherent catalog.
- **Fix:** add two refusal rows that use separately supplied incoherent catalogs: (a) catalog `uids` membership differs from the catalog node's uidSetDigest; (b) the catalog UID count differs from expectedCount. Expect category=identity, path=/nodes/3/selection, reason=catalog-uid-mismatch. Update the frozen inventory and count.

**D-4 The library accepts a graph supplied as its own catalog.**
- **Where:** scripts/lib/qualification-graph.ps1:234-241 (any decodable JSON is accepted as the catalog, with no shape or pin validation).
- **Failure:** `Read-QualificationGraph (prepare.valid.json) (prepare.valid.json)` returns Accepted=True. The same call with execute.valid.json is refused only incidentally, by catalog-uid-mismatch, because the graph has no `uids` map. This is the brief's "catalog read from the graph" forgery.
- **Why:** the design rule is that platformOfRecord and role come from a catalog independent of the graph. "Missing catalog is an explicit input refusal, never permission to trust the graph." Passing the graph through the catalog parameter is the same trust, and nothing refuses it.
- **Fix:** validate the catalog against a closed catalog shape: required `uids`, and graph-only keys such as schemaVersion, phase, runId, edges and history refused. Or require an expected catalog SHA-256 parameter (the fixture already has catalog.sha256) and refuse a mismatch. Add a `catalog-is-graph` refusal row for both prepare and execute.

**D-5 The precision fix is not general: the catalog identity comparison is still lossy for numbers.**
- **Where:** scripts/lib/qualification-graph.ps1:239-245, 253-256, 266-269 and 271-275. These compare `ConvertFrom-Json` -> `ConvertTo-Json` projections (double conversion), not the exact decoded JsonElement.
- **Failure:** graph node canonicalOrdinal `1.0000000000000000000001e22` (= 10000000000000000000001; direct schema accepts it as an integer) against a catalog canonicalOrdinal of 10000000000000000000000 returns Accepted=True. The canonical bytes and graph digest do keep the exact value, so identity and digest disagree about the same field.
- **Fix:** compare canonical JSON of the raw decoded subtrees, using QualificationGraphJson.Canonical over the graph and catalog elements, and add a typed-number identity row.
- **Low practical risk:** real ordinals are small.

## Verified sound (brief items)

- **Forgeries refused** (identity boundary, with the direct schema accepting each):
  - platformOfRecord=Any (`platform-forged`)
  - both role flips with requires.platform=Any
  - node or kind disagreeing with the catalog
  - duplicate node id (`/nodes/5/id`)
  - raw duplicate keys at root and nested level
  - unknown field on all 11 object shapes
  - schemaVersion 0, 2 and "1"
  - a non-null liveProvider or quota (the schema is null-only, D-10)
  - a live target or action not in the registry
- **Cycles:** the library and schema parse edge shape only and claim no cycle detection (S3/CP-3 owns it).
- **Canonical identity:**
  - A CRLF re-encoding and a deep, every-level key-reversed CRLF variant of execute.valid.json give the same graph digest (51a1f9e8...) and the same uidSetDigests.
  - Full Read under tr-TR, de-DE (decimal separator ","), fr-FR, ar-SA, fa-IR and ja-JP gives the expected digest every time. Culture effect confirmed: tr upper("i") = U+0130.
  - Numbers [1.25, -0.5e-3, 12345678901234567890123, 1E+25] canonicalize identically in every culture, and so does the UID digest.
  - The original numeric red (finite underflow 1e-9999 becoming 0, and decimal precision loss) is fixed in canonical bytes by exact coefficient/exponent normalization. See D-5 for the remaining identity path.
  - Under a binding change and a target change, node IDs and uidSetDigest stay the same, the graph digest changes, and exact golden bytes are asserted.
- **Mutants**, applied to scratch copies (base copy 100/100 green; tracked files untouched):

  | Mutant | Change | Result |
  |---|---|---|
  | m1 | platformOfRecord removed from node.required | RED 99/1 (missing-platform) |
  | m2 | forged Any accepted | RED 99/1 (platform-forged) |
  | m4 | bindingDigest dropped from canonical form | RED 94/6 (binding-wrong, canonical-keys, canonical-sets, utf8-hash, binding/target-graph-changed) |
  | m5 | liveProvider admits string | RED 98/2 (live-provider/target, node) |
  | m6 | UID set left unsorted | RED 99/1 (uid-members-changed) |
  | m3 | catalog-UID recompute bypassed | **survived**, see D-3 |

- **CP-1 exercises the real library:** the harness dot-sources `$PSScriptRoot/lib/qualification-graph.ps1` and the checked-in schema. Each mutant of those files changed its outcome.
- **Full census unchanged:** the 3fb3c113e..HEAD diff is additive. It touches the schema, library, harness, fixtures, the C# wrapper, plan/report docs, and the one census row plus hash in tests/test-execution-policy.json, which the companion allows. No gate, supervisor, cut/publish script, release doc or build definition changed. TestClassificationPolicyTests 39/39 and TestLaneCategoryGuardTests are green.

## Advisories

- **A-1 PowerShell runtime.**
  - Both files are ASCII (0 bytes > 0x7F).
  - At the PowerShell level they use no 5.1-unsafe constructs. None of these were found: `??`, `?.`, ternary, `&&`/`||`, `-Parallel`, 3-argument Join-Path, `ConvertFrom-Json -AsHashtable`, `::new`, classes. The `||` and `?:` hits are inside the C# here-string.
  - The library still cannot execute under Windows PowerShell 5.1:
    - scripts/lib/qualification-graph.ps1:5-152 Add-Type compiles against System.Text.Json and System.Text.Encodings.Web, which are not on .NET Framework.
    - :146 `Utf8JsonWriter.WriteRawValue` needs .NET 6+.
    - :182 `Test-Json -SchemaFile` is pwsh-only. Draft 2020-12 needs pwsh >= 7.4's JsonSchema.Net; the :187 error regex also assumes that message format.
  - :1 declares `#requires -Version 5.1`.
  - The nightly 5.1 bootstrap hops to `pwsh` (nightly-run-impl.ps1:508/616/675), so this is acceptable. Declare `#requires -Version 7.4` so a wrong host fails at load with a clear message, and confirm the Windows host's pwsh is >= 7.4.
- **A-2** A subset graph is accepted, for example with the required `fixture-selection` node and its edges dropped. S3's node-ID set-equality validator owns this. Read-QualificationGraph `Accepted` must never be treated as admission.
- **A-3** The "preserved" half of the valid-prepare, valid-execute and valid-sentinel checks (scripts/test-qualification-graph.ps1:69-71) is a self-compare: ConvertFrom-Json of the same text on both sides. Only acceptance is asserted there.
- **A-4** Windows PowerShell 5.1 parsing and runtime remain unverified on server2, as the Code report states.

PCs: all 50 PCs / 84 cycles remain pending for SourceLanding Mutation after the timing/cost Debug. Nothing here discharges them.
