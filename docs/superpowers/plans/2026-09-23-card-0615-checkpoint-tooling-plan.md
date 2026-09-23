# CARD-0615 + CARD-0617: quoted checkpoint rosters and explicit count units

Plan date: 2026-09-23. Task: `143d3f71`. Inspected base:
`c4809f9c66f51d09ee274fee040b46514533606d`.

The Code stage will normalize surrounding quotes on each comma-split `-Expect`
token and clarify checkpoint authoring so `Min` means the executed-test floor,
while `EstimatedMinutes` means elapsed-time budget. Verification design is folded
into this Plan, as required by the brief's `next: code` handoff.

## Ground truth

Both live cards were read with `scripts/card.ps1 get CARD-0615/CARD-0617 -Board
Antiphon -Json`. CARD-0617 includes the Review correction to its original diagnosis.

| Card/brief assumption | What the inspected code or doctrine actually does | Consequence |
|---|---|---|
| CARD-0615: quoted comma-separated expectations falsely miss. | `scripts/run-checkpoint.ps1`, verdict section (7), splits each input on commas and calls only `.Trim()`. Matching remains case-insensitive substring matching against executed `Class.Method` names. | Strip edge quote characters per token before the existing matching loop. |
| Several `-Expect` values can be supplied like a PowerShell array. | Across `pwsh -File`, one argument may retain literal quotes and commas. The harness already launches this real child-process boundary; its helper repeats `-Expect` if handed multiple array elements. | New cases must send exactly one string containing the comma-separated literal tokens. Do not fix this by repeating the parameter or by pre-normalizing test inputs. |
| CARD-0617 might require a count-parser fix. | `$executed` reads TRX `Counters@executed`, falling back to the executed-result roster length. Exit 3 is correct when `$executed -lt $MinExecuted`. No checkpoint Markdown is parsed by this script. | Leave count extraction, the default floor of 1, and exit-code precedence unchanged. |
| A short note can simply state that `Min` already means test count. | `docs/testing-and-build.md:124` explicitly defines `Min` as **estimated minutes**; its Cost paragraph, `docs/orchestration-loop.md:551`, the delegate skill's Stage recipes, and `server/Bundles/stage-test-design.md` all sum that column for scheduling. | This is a documentation convention correction. Replace the conflicting definitions and their live consumers together; an added contradictory note is insufficient. |
| The observed confusion might be isolated to CARD-0599. | CARD-0599's CP-2/3/4/7 have minute values 16/12/20/18 versus roster counts 13/3/14/3; its Cost totals 178 minutes. CARD-0585's plan explicitly defines minutes, CARD-0590 totals 414 CP minutes, CARD-0610 also sums CP Min, and CARD-0607 has `Min=4` but explicitly runs `-MinExecuted 5`. | Existing plans legitimately used the old time convention. Do not reinterpret their numbers as counts or mass-rewrite their historical evidence. State a legacy-plan rule. |
| Harness assertion rows and TUnit methods are interchangeable counts. | `RunCheckpointScriptTests` currently has 10 TUnit methods wrapping 40 named harness assertions. The bundle-cap test is one method with six TUnit argument cases. | Count separately reported TUnit executions, including argument cases; internal assertions/loop rows are separate evidence, never a `-MinExecuted` floor. |

### Baseline probes

Four local probes ran the unmodified script through its existing `-DotnetShim`
seam, with the same `scripts/fixtures/c585-green.trx` (executed=3, passed=3,
failed=0, skipped=0). No .NET build or real TUnit execution occurred.

| Literal `-Expect` argument | Floor | Actual wrapper result |
|---|---:|---|
| `C585SampleTests,C585OtherTests` | 3 | Exit 0; no roster misses. |
| `'C585SampleTests','C585OtherTests'` | 3 | Exit 3; both quoted tokens reported missing. |
| `"C585SampleTests","C585OtherTests"` | 3 | Exit 3; both quoted tokens reported missing. |
| `C585SampleTests,C585OtherTests` | 4 | Exit 3; `MIN EXECUTED expected at least=4 actual=3`. |

Local raw evidence is under `.antiphon/c615-plan-probe/` in this worktree;
`unquoted.txt`, `single-quoted.txt`, `double-quoted.txt`, and `floor-four.txt`.
These are fixture probes, not claims of three real tests passing. The table above
is the durable planning evidence; generated probe files are ignored by Git.

## Decisions

- **D-1: Normalize only token edges.** After comma splitting, trim whitespace,
  leading/trailing ASCII single/double quote characters, then whitespace again.
  For example, use `.Trim().Trim([char[]]@([char]39, [char]34)).Trim()`.
  Retain the current empty-token skip and case-insensitive substring match.
  Rejected: globally deleting quotes (would alter meaningful interior characters),
  evaluating PowerShell source, and introducing a CSV parser or new argument syntax.
- **D-2: Separate counts from time explicitly.** For newly authored/revised tables,
  `Min` is the integer passed to `-MinExecuted`: the floor of TUnit executed test
  results, ordinarily one per method, with separately reported argument cases
  counted separately. Add `EstimatedMinutes` for the time budget. A non-TUnit row
  uses `n/a` for `Min` and states its own success/assertion count in `Expect`.
  Reject treating internal matrix/assertion counts, minutes, or unique source
  method counts as the script's TRX executed count.
- **D-3: Update the small set of living copies consistently.** The canonical owner,
  authoring bundle, orchestration cost sentence, and delegate recipe all use
  `EstimatedMinutes` for Cost and `-ExpectAbout`. Update their existing contract
  tests. This is the minimum coherent expansion of the brief's documentation note;
  no dispatch/server/parser behavior changes are required.
- **D-4: Preserve old-plan meaning.** Add an explicit compatibility note: legacy
  `Min` values may mean minutes. Before reusing such a plan, rename that time column
  to `EstimatedMinutes` and derive any execution floor from its actual roster/TRX,
  never by copying the old numbers. This dispatch does not migrate archived plans,
  amend the CARD-0599 implementation, or relabel historical results.
- **D-5: Reuse the offline harness.** Add one TUnit wrapper method containing six
  named behavioral assertions, using the existing green fixture and real `pwsh
  -File` boundary. Keep the original 10 methods and their negative controls.
  Reject a new test project, Pester dependency, production refactoring for injection,
  or a test that merely invokes a copy of the normalization expression.
- **D-6: Use bounded ordinary verification.** Three named checkpoint groups share
  one isolated build after both slices are committed. The brief covers one script
  and documentation; a full assembly/namespace/Unit-lane run adds unrelated scope.
  Code runs ordinary V/R, separate Review follows, and the two PCs remain pending
  until commissioned post-land Mutation. No human decision is pending.

## Implementation slices

### S1 - quoted expectations retain the intended roster checks

Files:

- `scripts/run-checkpoint.ps1`: change only token normalization in verdict section
  (7), and explain literal quote handling in its adjacent comment. Preserve ASCII.
- `scripts/test-run-checkpoint.ps1`: add `Test-C585_QuotedExpect` (comment cites
  CARD-0615), with the six cases below. Keep `C585_` naming so the existing full
  harness enumeration includes it. Set `C585ExpectedRows` from 40 to **46**.
- `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs`: add
  `C585_QuotedExpect()`, calling the existing helper with expectedRows **6** and
  all six exact PASS names below. Retain the assembly-local process limiter.

Each scenario uses a fresh fixture case and one `-Expect` argument. Do not alter
`Invoke-C585Runner`, its shared helper, the fixtures, or existing expected-row
inventories merely to accommodate this case. Commit/push S1 before proceeding.

### S2 - checkpoint column units and live authoring guidance

Files and exact intent:

| File | Change |
|---|---|
| `docs/testing-and-build.md`, `Checkpoint manifest (CARD-0585)` | Define `Min` and `EstimatedMinutes` per D-2, add the legacy rule, explain assertion rows versus TUnit results, use only time estimates in Cost, and document quoted comma-separated `-Expect` values. Replace the historical worked-example table with a clearly illustrative two-row example: 3 TUnit executions and 12 internal assertions in one row, a non-TUnit row with `Min=n/a`, and separately labeled minute estimates. Do not invent floors for old CARD-0459 classes. Show `-MinExecuted 3` explicitly in the illustrative TUnit command and label fixture names as illustrative. Preserve the section heading and a valid Markdown table. |
| `server/Bundles/stage-test-design.md` | Append `EstimatedMinutes` to the table schema; briefly say `Min=-MinExecuted; n/a for non-TUnit`. Cost sums `EstimatedMinutes`. Keep the complete LF-normalized text ASCII and <=2,500 characters (base is 2,481); shorten nearby checkpoint/Cost prose to pay for the addition without removing safety/delivery requirements. |
| `docs/orchestration-loop.md`, Code-brief bullet in section 3 | Change the scheduling sum from `Min` to `EstimatedMinutes`; retain the committed-plan pointer. |
| `.claude/skills/antiphon-delegate/SKILL.md`, Stage recipes only | Update the cost paragraph, Code comment and `-ExpectAbout` placeholder to `EstimatedMinutes` / `<cp-estimated-minutes-sum+authoring>`. This is an instruction file being edited for consistency, not a skill invoked to dispatch agents. |
| `tests/Antiphon.Tests/Application/CheckpointManifestDocumentationTests.cs` | Update existing schema/recipe assertions to the new header and time placeholder. Keep the pointer, selection-readable table and closed-list checks. No new prose-only test method is needed. |

The owner note should explicitly say: a TUnit method that performs 12 internal
assertions still contributes one execution unless the runner reports separately
parameterized results. An argument-expanded test contributes its reported result
count. `-Expect` checks names; it does not turn assertions or elapsed minutes into
executed tests. Keep runtime `-MinExecuted` behavior unchanged. Commit/push S2,
then run the closed checkpoint list against that unchanged source identity.

### S3 - restore the pinned bundle phrases S2 trimmed (CARD-0617 repair)

S2's prose shortening of `server/Bundles/stage-test-design.md` deleted three
phrases that existing bundle-contract tests pin verbatim, turning six Unit tests
red. Restore all three exactly and pay for them out of **non-pinned** prose in the
same file, because the bundle must stay `<=2,500` LF-normalized ASCII characters.

| Pinned phrase | Pinning tests |
|---|---|
| `Every guard that protects a safety-critical assertion gets a PC-n positive control` (line 2, whole sentence) | `InstructionBundleTests.stage_bundle_invariants_are_pinned_by_substring`, `ScopedVerificationInstructionTests.C487_G131`, `ScopedVerificationInstructionTests.C487_G135` |
| `ordinary V/R floor (Code)` (Cost line) | `InstructionBundleTests.C470_composed_roles_separate_vr_from_pc`, `ScopedVerificationInstructionTests.C487_G139` |
| `PC floor (Mutation)` (Cost line) | `InstructionBundleTests.C470_composed_roles_separate_vr_from_pc`, `ScopedVerificationInstructionTests.C487_G139`, `PostLandMutationContractTests.C478_G175_CostInventory` |

Do not touch the pinned phrases, the `### Checkpoints` header row, the delivery
inventory field names (`producer`, `destination`, `persistence boundary`,
`recovery`, `observable receipt`, `durable identity`, `busy`, `already eligible`,
`crash/enqueue-failure`, `matching complete UserPrompt`, `Declare substitutes`,
`named positive control`), `### Positive controls` or `do not rewrite the fix
design`. The characters are freed from connective wording only: `outcome-delivery
path` -> `delivery path`, `transport ack` -> `ack`, `through the real queue` ->
`via the real queue`, `Reject a design that stops` -> `Reject a design stopping`,
`Every safety-critical delivery/recovery guard` -> `Each ...`, `Inventory every
safety-critical guard` -> `List every ...`, `n/a for non-TUnit` -> `n/a
non-TUnit`, `Name filters and minutes.` -> `Name filters/minutes.`, `duplicate PC
mappings=0` -> `duplicate PC maps=0`. No safety, delivery or checkpoint
requirement is removed. Measured result: **2,489** characters, 0 non-ASCII.

The verification gap this exposed is the second half of S3: CP-3 covered only the
size-cap method, so every other pin in the same class went unrun. CP-3 widens to
the whole `InstructionBundleTests` class and a new CP-4 covers the two other
classes that pin this bundle. CP-5 runs the whole Unit lane once at the end.

## Verification design

### Inspection and boundaries

Read the full runner, offline harness, green fixture, `RunCheckpointScriptTests`,
`CheckpointManifestDocumentationTests`, `Scripts/ScriptHarness.cs`, and
`scripts/lib/c487-harness.ps1`. Read `InstructionBundleTests`' six-argument cap
method and the adjacent pinned stage/delivery invariants, plus the bundle README.
The harness asserts child exit, named PASS inventory and summary; it has a 120 s
case timeout. Do not widen it. The count below refers to real TUnit executions,
not the shim's three fixture records or the harness's 46 assertions.

No async delivery path, DB operation, daemon, browser, network call or live agent
launch is changed. Delivery inventory is not applicable. The shim proves wrapper
behavior and CLI binding, not genuine `dotnet` test execution; CP-1 also invokes
the wrapper against the real built test assembly, closing that boundary.

### Proves it works now

**V-1 - quoted roster inputs**, `RunCheckpointScriptTests.C585_QuotedExpect`:
all six scenarios pass through the actual runner, with `-MinExecuted 3`, the green
fixture, and unchanged executed/passed/failed/skipped counters 3/3/0/0. Use one
compound behavioral assertion per scenario, with these exact PASS names:

| PASS name (prefix `C585 QuotedExpect `) | Input as literal argument content | Required verdict |
|---|---|---|
| `single quotes match` | `'C585SampleTests','C585OtherTests'` | Exit/trailer 0, no `ROSTER MISS`, counters 3/3/0/0. |
| `double quotes match` | `"C585SampleTests","C585OtherTests"` | Same green verdict. |
| `mixed quotes and whitespace match` | `  ' C585SampleTests ' , " C585OtherTests "  ` | Same green verdict; tests whitespace both outside and inside wrappers. |
| `missing token stays red` | `'C585SampleTests',"NotInRoster"` | Exit/trailer 3; exactly `ROSTER MISS NotInRoster`, no miss for the matching class, counters 3/3/0/0. |
| `interior quote stays significant` | `'C585Sam'pleTests','C585OtherTests'` | Exit/trailer 3; exactly `ROSTER MISS C585Sam'pleTests`, counters 3/3/0/0. |
| `empty quoted tokens retain compatibility` | `'','C585SampleTests',"",'C585OtherTests'` | Same green verdict; existing empty-token skipping retained. |

Construct double-quote characters as literal data in the harness, not shell
delimiters that disappear before reaching the child. Supply a one-element Expect
array to the current helper. Do not use multiple helper elements (repeated named
arguments fail binding). Zero execution, child binding errors, or shim failures
are never an expected red. Existing unquoted cases remain in `C585_RosterMiss`.

**V-2 - doctrine consistency**, existing four
`CheckpointManifestDocumentationTests` methods: updated schema and time-estimate
recipe assertions pass, the Markdown remains selection-readable, and the existing
checkpoint obligations remain present. Review the actual owner paragraph and
worked example for units; static substring checks alone cannot prove clarity.

**V-3 - deployable instruction size**, existing
`InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap`:
six argument cases pass, including the edited `stage-test-design` bundle. Review
that trimming its checkpoint/Cost prose preserved adjacent safety/delivery text.

**V-4 - the whole bundle contract, not just its size** (S3): every executed method
of `InstructionBundleTests`, `ScopedVerificationInstructionTests` and
`PostLandMutationContractTests` passes against the edited bundle. The six tests
S2 turned red are named explicitly in CP-3/CP-4 `-Expect` so a silent
non-discovery cannot read as green.

### Guards the regression

- **R-1:** All original 10 `RunCheckpointScriptTests` methods stay green: unquoted
  single/comma expectations, legitimate misses, test failures (exit 1), zero
  executed (exit 3), bad paths/build failure/fresh-directory refusal (exit 2),
  no-build behavior, line format, and ASCII safety. Quoted successes must not
  weaken legitimate miss detection; V-1 supplies two such negative variants.
- **R-2:** Existing documentation pointer/table tests retain their checks; only
  obsolete time-column/header expectations change. No weakening of the bundle cap.
- **R-3:** (S3) Editing `server/Bundles/stage-test-design.md` can never again
  delete a pinned phrase unnoticed: the checkpoint list now runs every class that
  pins it, so a trim that breaks a substring assertion is red at CP-3 or CP-4
  rather than at nightly. The cap assertion is unchanged and still enforced at
  `<=2,500`; the restored phrases were paid for from non-pinned prose, never by
  raising the cap.

### Guard inventory and positive controls

Two guards at the changed input-to-verdict boundary; mapped=2, missing=0,
duplicate mappings=0. Pre-existing output-path, results freshness, failed-test and
count-floor guards are unchanged and covered by R-1, not new Mutation scope.

| Guard | PC | Mutation and decisive assertion |
|---|---|---|
| G-1: edge quotes must normalize so valid quoted rosters are accepted | PC-1 | Restore the original whitespace-only normalization in the production runner. Exact filter `/*/*/RunCheckpointScriptTests/C585_QuotedExpect` must fail at `C585 QuotedExpect single quotes match` (also double/mixed variants); counters still 3/3/0/0 but wrapper exit 3. Restore and require all six assertions green. |
| G-2: unmatched normalized tokens must still refuse a checkpoint | PC-2 | Bypass only the final `$rosterMisses.Count -gt 0` exit-3 branch. Same exact method filter must fail at `C585 QuotedExpect missing token stays red` (also interior-quote variant) because the wrapper exits 0. Restore and require all six assertions green. |

Mutation runs these sequentially after land on a separately commissioned snapshot;
they share a file and method and cannot be batched. Both red and green use the
method filter above, fresh TRX, and one real TUnit execution per run. Preserve
per-PC evidence externally under the Mutation brief; no snapshot commits/pushes.
These are script mutations, so reuse the fixed isolated assembly only after
confirming its repo-root resolution points at the commissioned snapshot.

### Cost and execution procedure

Estimates, not measurements: CP-1 **6 minutes** (isolated build 4, script tests 2),
CP-2 **1 minute**, CP-3 **2 minutes**, CP-4 **2 minutes**, CP-5 **5 minutes**;
ordinary Code V/R **16 minutes**, plus approximately
**30 minutes** authoring/commit/cleanup (`-ExpectAbout 46`).
Post-land Mutation: setup/build **4 minutes**, two sequential PC cycles at
**2 minutes** each, total **8 minutes**. Combined verification estimate is
**24 minutes**. No measured time saving is claimed; CP-5 is the whole Unit lane
required by this round's Final profile, and CP-3/CP-4 keep the named-class
evidence fast enough to iterate on. `Min` sums are counts, never these time
estimates.

Each row executes after S1, S2 and S3 commits, against the same unchanged SHA. Use
`scripts/run-checkpoint.ps1` with the table's exact filter, `-MinExecuted` equal
to `Min`, one comma-separated `-Expect` argument, and fresh results under
`.antiphon/c615-checkpoints`. CP-2/3 specify `-NoBuild` and reuse CP-1 output.
For CP-1, deliberately exercise quoted multi-token CLI data on real executions:

```powershell
$quotedExpect = "'RunCheckpointScriptTests.C585_QuotedExpect','RunCheckpointScriptTests.C585_RosterMiss'"
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c615/ -Filter '/*/*/RunCheckpointScriptTests/*' -MinExecuted 11 -Expect $quotedExpect -ResultsRoot .antiphon/c615-checkpoints
```

For CP-2, Expect=`CheckpointManifestDocumentationTests`; for CP-3,
Expect=`each_stage_bundle_is_ascii_and_under_the_size_cap,stage_bundle_invariants_are_pinned_by_substring,C470_composed_roles_separate_vr_from_pc`;
for CP-4, Expect=`C487_G131,C487_G135,C487_G139,C478_G175_CostInventory`; for
CP-5, Expect=`stage_bundle_invariants_are_pinned_by_substring,C470_composed_roles_separate_vr_from_pc,C487_G131,C487_G135,C487_G139,C478_G175_CostInventory`.
CP-4's filter contains a literal `|`; the table escapes it for Markdown only. The
argument as passed is:

```text
/*/*/(ScopedVerificationInstructionTests*)|(PostLandMutationContractTests*)/*
```

Require every listed
method/argument case, zero failures/skips and exit 0, not only a satisfied floor.
Report each CHECKPOINT line with SHA, counters, fresh TRX and reruns. A missing
roster or zero count is red. Extra builds/tests require an explicit reason;
unexpected red is checked at the base using only the failing exact method.
Do not change timeouts/assertions to obtain green. No source edits while tests run.

Before building, inventory pre-existing `bin-c615` paths; use a fresh suffix if
necessary and report that substitution. Remove only the exact task-produced
alternate-output directories after validating their resolved paths stay within
the assigned worktree. Do not restart the stack. After ordinary success, report
`next: review`, with PCs explicitly pending.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c615/` | checkpoint-runner | `/*/*/RunCheckpointScriptTests/*` | V-1, R-1 | All 11 methods, including new `C585_QuotedExpect`; 0 failed/skipped; quoted multi-token outer Expect | 11 | 6 |
| CP-2 | S1-S2 | CP-1 | checkpoint-doctrine | `/*/*/CheckpointManifestDocumentationTests/*` | V-2, R-2 | All 4 existing methods, 0 failed/skipped | 4 | 1 |
| CP-3 | S1-S3 | CP-1 | bundle-contract | `/*/*/InstructionBundleTests/*` | V-3, V-4, R-2, R-3 | Whole class incl. all 6 argument cases of `each_stage_bundle_is_ascii_and_under_the_size_cap`, `stage_bundle_invariants_are_pinned_by_substring` and `C470_composed_roles_separate_vr_from_pc`; 0 failed/skipped | 57 | 2 |
| CP-4 | S1-S3 | CP-1 | bundle-pins | `/*/*/(ScopedVerificationInstructionTests*)\|(PostLandMutationContractTests*)/*` | V-4, R-3 | Both classes incl. `C487_G131`, `C487_G135`, `C487_G139`, `C478_G175_CostInventory`; 0 failed/skipped | 47 | 2 |
| CP-5 | S1-S3 | CP-1 | unit-lane | `/*/*/*/*[Category=Unit]` | V-1..V-4, R-1..R-3 | Whole Unit lane of `Antiphon.Tests` green; 0 failed; the six previously-red methods all executed | 1900 | 5 |

## Handoff

Implement S1-S3 and execute CP-1..CP-5. The plan itself changes no production code.
The CARD-0617 premise is correct as an authoring gap; the discovered historical
definition is accounted for explicitly, so no further investigation or decision
stage is required. Keep script count logic unchanged. Ordinary Review follows
Code, with PC-1/PC-2 pending post-land Mutation.
