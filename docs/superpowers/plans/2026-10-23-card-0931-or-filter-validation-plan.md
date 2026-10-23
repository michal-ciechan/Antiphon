# CARD-0931: refuse unsafe checkpoint OR filters before a test build

Plan owner: `fe5dac5f-fd38-44dc-b635-5bdf022f4188`. Source inspected:
`bfd64e8152bec9fbbe39f606e8e705c4f400e4b5`. Documentation only: no build,
test execution, product edit, restart, or Windows qualification access.
The brief requests verification details but does not fold TestDesign into this
dispatch. The verification section is a closed proposal for **separate TestDesign**.

Reject a plan-table class/method OR without the documented wildcard suffix on
each operand, before scheduling any test-project build. Preserve the author's
filter verbatim when accepted. If an admitted row nevertheless executes zero
tests against a required nonzero selection, report `zero-tests-executed`, with
exit 3, without retrying or rebuilding. Teach the rule in the Plan-stage bundle.

Read the full CARD-0931, CARD-1531 and CARD-1532 cards, the
[testing owner](../../testing-and-build.md#checkpoint-manifest-card-0585),
[runner owner](../../testing-and-build.md#checkpoint-runner-tool-card-0723),
[reference plan](2026-10-23-card-1491-claude-pool-effort-plan.md), and the importer,
execution, TRX, reporting, CLI and existing fixture code. The two neighboring
plans were read at `7235182cb61e1f99eb0e4fb911e501669c3d5240` on the published
CARD-1532 Code branch; they are not yet in this checkout's master base.

## Ground truth

| Card assumption or shortcut | Observed code/evidence | Consequence |
|---|---|---|
| The affected runner uses TUnit 1.44. | `tests/Antiphon.Tests/Antiphon.Tests.csproj` pins `TUnit` to `1.44.0`; the small FilterHost pins the same version. | Use version-specific discovery evidence, not only today's upstream examples. |
| Bare method OR can select nothing. | CARD-0931 records zero executed for the original flaky-fix CP-3/CP-15. The CARD-1532 Code plan amendment at the SHA above records zero for its bare CP-1 and four intended reds after a same-build wildcard correction. | The premise is supported by historical execution reports, not freshly reproduced by this docs-only task. |
| Every operand's suffix is strictly necessary to defeat the discovery hint. | Pinned `MetadataFilterMatcher.ExtractFilterHints` treats an entire class/method segment as a literal when it contains neither `*` nor `?`; `CouldMatch` compares that literal to the individual name. A wildcard anywhere in that segment suppresses the hint. | All-bare OR is the demonstrated discovery defect. Requiring a suffix on **every** operand is the repository's conservative canonical form; do not claim mixed OR must always execute zero. |
| OR is legal between full paths. | The testing owner records both over-selection and zero selection for that shape; OR belongs within one segment. | Refuse that plan shape rather than splitting it into several runs or widening it. |
| Import already rejects this before work. | `PlanTableImporter.ImportMarkdown` unescapes `\|`, extracts the payload, then assigns `row.Filter` and `RosterTokens` without OR validation. `Program.Import` and `LoadSelection` use this importer before writing/starting an execution. | Put the guard at that common import boundary. No scheduler admission redesign is needed. |
| `Expect` is an exact roster. | Import stores the prose as `ExpectText`; machine `Expect` comes from `RosterTokens`. It recognizes parenthesized names with `*`, otherwise one simple path component; categories return no tokens. `RowRunner` matches tokens by substring against executed **and skipped** names. | Keep this behavior. Exact intended TRX identities and suffix collisions still need review; this repair is not an exact-roster engine. |
| Zero tests is currently green or a failed assertion. | `RowRunner` already returns `ExitCodes.RosterOrMin` (3) for a minimum/roster miss; `RowRunnerTests.zero_executed_is_exit_3` pins it. State is the generic `roster-miss`, with `MIN EXECUTED`/`ROSTER MISS` text. | Add a named subtype, retaining numeric exit codes and precedence. A positive minimum is not required when explicit matched skips were authorized. |
| A zero result currently causes an automatic rebuild. | `RerunPolicy` selects only failed names on the known-flaky list; an empty TRX has none. Scheduler builds are keyed independently of row verdict. | Preserve this property with driver-count witnesses. Do not invent a retry/cache subsystem or erase the initial build's cost. |
| An import refusal can avoid every compilation. | `dotnet run --project tools/Antiphon.Checkpoints` may first compile the tool. Import then precedes test-project builds, slots, executor launch and test discovery. | Promise zero **checkpoint/test-project** builds for refusal; account separately for a tool bootstrap. No `--list-tests` probe. |
| Fixing the on-disk bundle instantly reaches new delegates. | `server/Bundles/*.md` is embedded by `server/Antiphon.Server.csproj`; `InstructionBundles` reads the compiled resources. `stage-plan.md` is 1,279 bytes; stage bundles cap at 2,500 characters/ASCII. | Tool activation and server-delivered guidance activation are separate. Do not assume a master land reloads the running server. |

### Filter evidence and supported shape

The immutable [TUnit hint extractor at commit
42e3be6d99bb637d21e1dac711d76991a99e49c3](https://github.com/thomhurst/TUnit/blob/42e3be6d99bb637d21e1dac711d76991a99e49c3/TUnit.Engine/Services/MetadataFilterMatcher.cs#L139)
supplies the mechanism above. The current [upstream filter documentation](https://tunit.dev/docs/execution/test-filters/)
explains segment-local parentheses but shows some bare class alternatives; those
examples alone do not establish behavior of this pin. The local
[Combined class filters](../../testing-and-build.md#combined-class-filters-card-0403)
section owns repository guidance. `RerunPolicy.MethodFilters` already emits
`/*/*/Sample/(alpha*)|(beta*)`, pinned by its unit test.

| Literal filter (pipes must be escaped in a checkpoint table) | Treatment/evidence |
|---|---|
| `/*/*/Class/MethodA` | Valid single exact method; no suffix needed. Keep exact methods for method-scoped controls. |
| `/*/*/Class/*` | Valid whole class with a declared roster/count. |
| `/*/*/Class/(MethodA*)\|(MethodB*)` | Canonical method OR. Author must check whether the prefixes also match suffixed methods. |
| `/*/Antiphon.Tests.Application/(CardEstimateTests*)\|(CardCorrectionIntegrationTests*)/*` | Landed CARD-0151 CP-2 literal; the starvation investigation records its subsequent same-source 51/51 recovery. This is historical evidence, not a new run. |
| `/*/*/Class/(MethodA)\|(MethodB)` | Reject: all-bare method OR; pinned discovery may eliminate every method before execution. |
| `/*/*/Class/(MethodA*)\|(MethodB)` | Reject by canonical-form policy, even though the wildcard can suppress the segment hint. Explain that distinction in the diagnostic/docs. |
| `/*/*/(ClassA)\|(ClassB)/*` | Reject the same hazard in the class segment using the same small helper. |
| `/*/*/ClassA/MethodA\|/*/*/ClassB/MethodB` | Reject full-path OR; never translate it into broader selection. |
| `/*/*/*/*[(Category=Unit)\|(Category=Slow)]` | Property expression, not path OR. Preserve it without applying the suffix rule. |

`--list-tests` is not execution proof on this pin: the owner records discovery
ignoring the requested scope. No new native Linux/Windows grammar qualification
is claimed here. TestDesign should use the existing evidence and pure import
witnesses; any additional real execution experiment must be explicitly costed.

## Decisions

**D-1 — Refuse, never normalize.** In
`tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`, validate the extracted
filter in the `payload.StartsWith('/')` branch, before assigning it to a row.
Return `ExitCodes.Invalid` (2), `Manifest = null`, with fixed diagnostic
`refused: checkpoint-filter-or-wildcard: <CP-id>: <class|method> operand <n> requires a trailing '*'; use (NameA*)|(NameB*) or separate exact-method rows sharing one build`.
The same helper checks class and method segments: omitting the class guard would
leave the reference plan's bare class OR broken after fixing only its methods.

Scan separators outside square-bracket property groups. With path OR present,
require the four-component `/<assembly>/<namespace>/<class>/<method>` shape and
flat parenthesized alternatives in the affected class/method segment. Refuse
full-path OR, unparenthesized alternatives, or unsupported nested path expressions
as `refused: checkpoint-filter-or-shape: <CP-id>: use parenthesized alternatives within one class or method segment`.
For a flat OR, every nonempty operand must end in `*` immediately before `)`.
Do not inspect property-expression OR as path OR. Do not add restrictions to
non-OR exact/glob/category filters, command rows, or existing `RosterTokens(null)`.
Leave the accepted payload byte-identical after the existing Markdown decoding.

This is a small canonical-form check, not a replacement TUnit parser. OR in
assembly/namespace segments and complex nested path expressions are outside the
admitted canonical form; report the shape refusal, not a guess at their meaning.
If a path OR is visible but bracket/operand boundaries cannot be resolved, use
the shape refusal. Non-OR malformed filters retain existing downstream handling;
never repair their text.

Rejected: silently append wildcards, normalize to a whole class, or split one
row into multiple invocations. All can alter test selection/cost. Reject a warning
only, because it still spends the test build. Do not change the TUnit pin or add
discovery builds. Keep direct YAML/direct `row` admission unchanged in this small
repair; their executions receive D-2's runtime protection. The documented promise
is for `import --plan`, `start --plan` and `run --plan`.

**D-2 — Name zero execution while retaining exit 3.** In
`tools/Antiphon.Checkpoints/Execution/RowRunner.cs`, after parsing TRX and computing
roster misses, identify zero execution when `Executed == 0`, there are no failed
counts/names, and either `MinExecuted > 0` or at least one nonblank `Expect` token
is missing. If the final outcome is exit 3, use state `zero-tests-executed` and
append ` reason=zero-tests-executed` to its existing CHECKPOINT line. Retain the
current counter, TRX path, `MIN EXECUTED`, `ROSTER MISS`, build-source and exit
trailer output. `CheckpointApp.BuildReport`, report JSON and Markdown already
copy the state/line; prove that path rather than adding a new receipt schema.

A valid empty TRX with host exit 8 has this outcome too. Missing or malformed TRX
remains exit 2; test assertion failures remain 1; a nonempty partial roster miss
remains `roster-miss`/3. Source/custody, timeout, cancellation and repeat-evidence
precedence remain intact. Only attach the new reason when the **final** row
outcome is the zero-execution roster failure; never overwrite a stronger failure.

An explicitly skip-only row (`Min=0`, all named tokens present as skips) retains
its current result. A skipped-only row requiring positive execution fails with
the new name. `Min=0` with no nonblank `Expect` remains the existing unconstrained
empty-row behavior. This is not permission to qualify ordinary Code with zero
tests or skips; receipt qualification stays unchanged. The original card's CP-15
explicit skips are therefore not silently converted into an execution requirement.

Rejected: a new numeric exit code, treating zero as a failed assertion, parsing
free-form `ExpectText` into a new roster language, or rejecting every intentionally
skip-only row. No changes to `ExitCodes.Precedence`, `TrxReport` or receipt validation.

**D-3 — No automatic second attempt or rebuild.** Zero selection terminates that
row once, with `Reruns=0`; it does not invalidate an otherwise verified build or
block an independent row sharing it. Use a fake-driver scheduler witness with
one build, an empty-result row and a passing sibling, plus a direct `NoBuild`
witness. A corrected filter can use the existing authorized same-source reuse
path and a fresh results directory; changed source still needs a new binding.
Never relax source SHA checks to avoid a build after a committed plan/source edit.
Retain the initial build and failed run's actual cost and evidence.

**D-4 — Put guidance where Plan reads it.** Add a concise ASCII paragraph to
`server/Bundles/stage-plan.md`: class/method OR is `(NameA*)|(NameB*)`, every operand
has its own parentheses and suffix, table pipes are `\|`, exact single methods
remain valid, import refuses unsafe shapes, and suffixes need an intended-roster
check. Link to the testing owner. Update only its Checkpoint manifest/import and
Combined class filters paragraphs with the fixed refusal names, the mixed-OR
nuance, zero-execution outcome and no-rebuild behavior. Keep the stage bundle
under 2,500 characters. Do not expand the already near-cap TestDesign bundle
(2,479 bytes), duplicate a filter grammar in AGENTS.md, or rewrite historical plans.

**D-5 — Keep implementation boundaries small and sequence shared owners.** New
tests belong in `tests/Antiphon.Tests/Checkpoints/CheckpointOrFilterTests.cs`, using
the current public importer, `FakeDriver`, `FakePlatform`, `FixedSlotClient`,
`CheckpointFixtures.WriteResults` and existing temporary-repo/binding helpers.
No new project, shared-fixture edit, real nested compiler, live broker, database,
provider process or full-suite selection. Production edits are the importer and
RowRunner only; docs are D-4. Do not change the scheduler merely to test its
existing one-build behavior.

**D-6 — Separate scope from historical qualification.** This commissioned repair
prevents new import waste and clarifies runtime failures. It does not rewrite
CARD-0931's original CP-3/CP-15 receipts or claim their Linux/Windows requalification.
The caller still owns any active legacy plan amendments and the card's historical
3-executed/2-explicit-skips qualification obligation. Amend an actively reused
plan deliberately, inspect prefix collisions, and retain its original failures.
No desktop or supervisor pid 209072 access is authorized by this plan.

## Coordination and safe order

| Owner | Files it owns or plans to touch | CARD-0931 boundary |
|---|---|---|
| CARD-1532, Code `12dd82bc-d419-4247-af23-4ba6aff5cd04`; inspected pushed tip `7235182cb61e1f99eb0e4fb911e501669c3d5240` | `tools/Antiphon.Checkpoints/BuildConfinement.cs`, `Coverage/ConfinedFileReader.cs` in its published delta, `scripts/run-checkpoint.ps1`, `scripts/lib/checkpoint-file-reader.ps1`, card-specific large-stamp tests/scripts, `CheckpointEofStallTests.cs`, testing-guide stamp paragraphs. | Do not edit any stamp/script/EOF file. Its landed fix prevents the retained red test DLL from triggering the known 64 MiB refusal during green qualification. |
| CARD-1531, admission plan read from the same published branch; card read as Review, not evidence of code publication | `Execution/RunBudgetClock.cs`, `RunScheduler.cs`, **`RowRunner.cs`**, slot client, baseline comparer, CLI/request/app, manifest loader/validator/model, state and report files, card-specific budget tests and testing-guide timing paragraphs. | `RowRunner.cs` is a real source collision; shared docs also overlap. Preserve its deadlines, phase accounting, terminal causes and partial-wait fields when adding the zero-result subtype. |
| CARD-0931 | `Manifest/PlanTableImporter.cs`, `Execution/RowRunner.cs`, new `CheckpointOrFilterTests.cs`, `server/Bundles/stage-plan.md`, testing-guide filter/import paragraphs. | No card-wide tooling scope or common test-project edits. Stage new files explicitly. |

Recommended order is **CARD-1532 Code/Review/land -> CARD-1531 Code/Review/land ->
CARD-0931 Code**, with Plan/TestDesign work independent. Re-read card/task and
published tip status before dispatch; a card column alone does not establish a
landed source. Scope collision defers Code. Start CARD-0931 Code from the actual
landed base, never copy unreviewed neighboring patches, merge master into this
task branch, or rebase a pushed branch. TestDesign must reconcile the final
RowRunner/report API and roster at that base. Guidance for affected in-flight
plans goes through their caller/owner; this Plan edits none of their branches.

## Slices

S0 is this docs-only Plan, committed/pushed and checked statically. Each subsequent
Code slice is 30-60 minutes; source is committed/pushed before its checkpoint run.

| Slice | Files and deliverable | Closure |
|---|---|---|
| S1 (45-60 min) | Add `tests/Antiphon.Tests/Checkpoints/CheckpointOrFilterTests.cs`; all fixtures use existing APIs and fake drivers. No production stubs are needed. | CP-1 runs the real pre-fix behavior: 16 specified assertion reds and 13 controls. Missing APIs, fixture errors and zero selected tests are not red proof. |
| S2 (30-45 min) | Implement D-1 in `PlanTableImporter.cs`, D-2 in `RowRunner.cs`, and D-4 in `server/Bundles/stage-plan.md` and `docs/testing-and-build.md`. | Commit/push, bootstrap the updated tool, then CP-2..CP-4 share one test-project build at the frozen SHA. Preserve neighboring fixes. |
| S3 (30 min) | Qualify receipts/report, inspect selection identities and guide/bundle text; no further source edit solely to record results. | Validate exact green receipts; evidence-diff guard over base..HEAD; fetch master and conflict-only merge-tree; push/confirm full SHA; ordinary Review next. |

## Rollout, activation and rc status

`GET /api/runner-defaults` and `GET /api/session-runners` were read on 2026-10-23:
defaults revision 2 selected an eligible Linux lane, and an eligible Windows
lane also existed. These observations are not host pins. All proposed rows use
the **portable .NET importer/fake-driver lane**, no native Windows requirement.
Re-read placement at dispatch; omit `-Runner` and `-Platform` for this work.

The tool is built from the checkout per invocation and shadow-copied per run.
Publish reviewed source and use a newly bootstrapped tool for new runs. Existing
executors retain old behavior/evidence; no process restart is needed for the tool.
The on-disk testing guide is immediately available in updated checkouts. The
embedded Plan bundle reaches server-created prompts only after the separately
authorized server release containing it; until then the caller should carry the
filter rule in new Plan briefs. Verify the compiled bundle at that release, not
health alone. This Plan does not restart AppHost or runners.

Read-only inspection of remote `refs/heads/rc/2026-10-20` at
`5a126f6fc5ae4e16d803b628716b8f0485a37174` found the same TUnit 1.44.0 pin,
unvalidated importer assignment and generic zero-result `roster-miss` branch.
Recommend a narrow reviewed rc port because this repairs existing tooling.
Re-resolve the active rc target before commissioning; the release-guide prose
still names an older line. Port the reviewed stamp fix, admission fix, then this
repair, preserving each card's tests/docs and source bindings. A new assigned rc
task adapts to its source, runs this narrow green selection, obtains ordinary
Review and publishes via the authorized target-tip Merge workflow. Do not merge
all master or move tags. This Plan artifact remains master documentation; neither
its push nor a tooling run earns full RC qualification or activates server guidance.

## Verification design

TestDesign freezes the proposed names/counts and the neighboring API adaptations
before Code. All new methods live in `CheckpointOrFilterTests`; the names below
are proposed, not existing tests. A table case marked as an argument is one
reported TUnit execution; internal assertions/matrix loops are not extra results.
Use real Markdown/string/list inputs, real TRX XML and public production paths.
Expected exit, state, literal filter and driver counts are constants independent
of the implementation. Every runtime fixture owns and joins its work.

### RED witnesses first

| ID | Method / expanded executions | Oracle and pre-fix failure |
|---|---|---|
| V-1 | `C0931_rejects_method_or_without_each_suffix` / 4 arguments | Both bare; only first starred; only second starred; three alternatives with bare middle. Import returns 2, null manifest, wildcard refusal naming row, method and bad operand. All four currently import successfully. Exercise escaped table pipes, including wrapped and unwrapped Filter cells. |
| V-2 | `C0931_rejects_class_or_without_each_suffix` / 2 arguments | Both bare classes and mixed starred/bare classes, with a valid `/*` method segment. Same row-aware class refusal. Current import succeeds; no method guard can mask this witness. |
| V-3 | `C0931_rejects_path_or_shape` / 2 arguments | OR joining two complete paths; unparenthesized method alternatives. Both return shape refusal, no manifest. Current import succeeds. Use otherwise valid columns/build/minutes. |
| V-4 | `C0931_plan_entrypoints_refuse_before_work` / 3 arguments (`import`, `start`, `run`) | Feed one bad method-OR plan through `Program.RunAsync`, recording launch/copy/wait/slot/driver hooks. Exit 2 and fixed refusal; no output YAML, execution directory, tool shadow copy, slot acquisition, driver or executor launch. Existing entrypoints admit it. Give the old path a complete tiny fake runtime so red is an assertion, never a missing tool or owner fixture error. |
| V-5 | `C0931_zero_execution_has_named_failure` / 3 arguments | Empty valid TRX with Min=1/Expect empty; empty TRX with Min=0/Expect=`Sample`; matching skip-only TRX with Min=1. Expect exit 3, `zero-tests-executed` state/line, retained counts and MIN/ROSTER evidence, zero reruns and exactly one fake test run. Include native host exit 8 for an empty case. Base returns generic state, so the named-outcome assertion fails. |
| V-6 | `C0931_shared_build_report_preserves_zero_failure` / 1 | Real scheduler with fake slots/driver and one shared build: first row empty/Min=1, sibling one pass. Assert one build, two test runs, first exit/state/reason, second green, overall 3, valid build binding; feed result through real BuildReport and report JSON/Markdown serialization and inspect the copied reason/state. No automatic retry/build. Base fails the named report assertion. |
| V-7 | `C0931_nobuild_zero_execution_does_not_rebuild` / 1 | Valid existing source/build binding, `NoBuild=true`, empty TRX/positive floor. State/reason as V-5, `build=reused`, zero fake build calls, one fake run, no retry. Do not weaken binding checks to enter the test. Base fails the named outcome. |

Total: **16 intentional failures** at S1, all at behavioral assertions, not compile
or fixture boundaries. V-1..V-4 prove admission separately from V-5..V-7: runtime
witnesses use valid exact filters with synthetic empty results, so the new import
guard cannot hide a missing runtime classification.

### Regressions and absent inputs

| ID | Method / executions | Required control |
|---|---|---|
| R-1 | `C0931_accepted_filters_are_byte_preserved` / 6 arguments | Exact single method; whole class; canonical method OR; canonical class OR; both class/method OR; property-group OR. Import succeeds with exact decoded filter equality, unchanged build identity and expected roster extraction. Include a property suffix on a path-OR case. |
| R-2 | `C0931_nonselection_outcomes_keep_their_codes` / 6 arguments | One pass -> 0; genuine assertion failure -> 1; nonempty partial roster miss -> generic 3; absent TRX -> 2; malformed TRX -> 2; Min=0 with all declared names explicitly skipped -> existing result without zero-test reason. Assert driver counts and recorded outcome rather than only exit. |
| R-3 | `C0931_absent_inputs_preserve_existing_admission` / 1, internal matrix | The typed absent/empty/value rows below; no new parser throws or fabricated roster. |
| R-4 | Existing `CheckpointImportTests`, `RowRunnerTests`, `RerunPolicyTests` / 45 methods at base | Entire small fixture classes pin import schema/escaped pipes/build reuse, failed/skipped/minimum distinctions and known-flaky method filters. They use fake drivers; no nested real project builds. This bounded 45-test selection has a one-minute run estimate and protects changed code's immediate contracts. |
| R-5 | Existing `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` / 6 arguments | All stage bundle ASCII/size contracts. Review the Plan paragraph and owner-doc examples for D-4; do not claim this test proves an agent follows instructions. |

R-1..R-3 contribute **13 controls**, expected green in S1 and S2. New-class total
is **29 executions**. R-4 count is a source census at the inspected base, to be
reconciled if earlier cards add methods; do not copy a stale count into evidence.

| Boundary/type | Absent | Empty | Value / invalid value | Witness |
|---|---|---|---|---|
| Markdown `string` and Filter cell | No Checkpoints section keeps existing import refusal. | Empty filter through `start/run --plan` keeps existing invalid-selection behavior; does not become an OR refusal. Do not add unrelated validation to `import`. | Valid exact filter preserved; bare OR refused; command containing a shell pipe remains a command, not a filter. | R-3; V-1/V-4 |
| `RosterTokens(string?)` / `List<string> Expect` | Null filter yields empty tokens; omitted Expect is the initialized empty list. | Empty/whitespace filter and blank Expect tokens do not create names. Min=0 plus no nonblank tokens and an empty TRX keeps existing outcome. | Nonblank missing token plus zero execution yields named failure. Do not invent nullable collection contracts for nonnullable `RowRequest.Expect`. | R-3; V-5 |
| Optional Min cell / `int? MinExecuted` in imported manifest | Blank/`n/a` Min with `ExpectText >= 1 executed` retains existing inferred floor. | No explicit/inferred floor remains null for import; scheduler's existing default floor is unchanged. | Explicit 0, 1 and inherited platform count syntax keep their meanings. | R-3; R-4 |
| Property group `string` | No group: inspect path OR. | Empty/non-OR property suffix receives no wildcard rewrite. | OR within brackets is preserved; scanner does not split its operators as path OR. | R-1/R-3 |
| Fresh TRX file / XML counters/results | No file -> existing invalid result. | Well-formed zero results, including counters omitted, uses parsed zero and required-minimum/roster rules. | Passed/failed/skipped cases remain distinguishable; malformed XML is not zero-selection proof. | V-5; R-2/R-3 |

### Independently bypassable positive controls

These are pending **post-land SourceLanding Mutation**, not extra ordinary Code
runs. Bind to the landed SHA and use one exact method filter per variant, e.g.
`/*/Antiphon.Tests.Checkpoints/CheckpointOrFilterTests/C0931_rejects_method_or_without_each_suffix`.
No OR or class-wide mutation selection is necessary. Each variant must compile,
reach its expected assertion red, be restored, and pass the same method green.

| PC | One production bypass | Exact witness; independent oracle |
|---|---|---|
| PC-1 | Disable only the method-segment suffix refusal. | V-1 method: four failed refusal assertions; class segment is valid, so the class guard cannot supply the red. |
| PC-2 | Disable only the class-segment suffix refusal. | V-2 method: two failed refusal assertions; method is `*`. |
| PC-3 | Disable only the OR-shape refusal, allowing unrecognized OR through without repair. | V-3 method: two failed refusal assertions; no recognized flat alternative reaches the suffix guard. |
| PC-4 | Disable the zero-execution subtype, retaining generic exit-3/minimum/roster checks. | V-5 method: three failed state/reason assertions. TRX is valid and import admits the filter; numeric failure alone cannot satisfy this oracle. |

TestDesign must preserve that bypassability when choosing helper structure; do
not let a generic second refusal mask a disabled guard. Existing S1 reds prove
the missing behavior but do not claim these later mutations ran. Retain all
historical outcomes and bind every later result to its actual source SHA.

### Execution and cost

**Two test-project builds total**, one S1 red and one S2 green. A single build
cannot honestly verify two different source SHAs. All green rows share **one**
test-project build; importer and runtime fixtures launch no real nested builds.
No FilterHost build, `--list-tests`, package upgrade, whole Unit/namespace/assembly
run, legacy incident rerun, or Windows execution is in this closed selection.

Ordinary checkpoint floor is **20 minutes** (sum below), plus a separately
reported tool-only bootstrap allowance of one minute per source group and slot
wait. Authoring/review preparation is additional; commission roughly 90-120
minutes of Code including its 30-60 minute slices, not this Plan's 35-minute
budget. Pending PC floor is **72 minutes**: four independently restored variants,
two nine-minute build/method phases each, excluding tool bootstrap/admission and
discovery/reporting. Ordinary plus PC execution floor is **92 minutes**. No extra
red/green cycle merely to repeat already valid evidence; narrow reruns follow an
actual fix or a documented incomplete required row.

Use the checkpoint tool once per committed source group:
`run --plan docs/superpowers/plans/2026-10-23-card-0931-or-filter-validation-plan.md --rows <ids> --expected-source-sha <full-sha>`.
Await that run with `wait` until exit is not 75; do not settle while its executor
is outstanding. Each tool bootstrap/build/test driver goes through the host
build-slot gate; the scheduler takes its own slots, without an outer held lease.
Do not bypass slot timeout 4 or change deadlines to conceal it. CP-1's nonzero
result is intentional; fresh TRX must identify the 16 expected assertions.

Commit and push S1 before red, S2 before green; do not edit source during runs.
Retain ignored TRX/receipts and unedited CHECKPOINT lines, actual SHAs/counts and
all bootstrap/wait/rerun costs. Validate CP-2..CP-4 with the exact green SHA and
run `scripts/check-evidence-diff.ps1` over base..HEAD. No fixture-generated zero
TRX can substitute for nonzero execution of the **outer** witness methods.
Remove only owned alternate outputs under the established custody procedure.
Inherited red requires exact-method evidence at base; no assembly rerun.

### Checkpoints

Closed proposal for the **portable .NET importer/fake-driver lane**. No host or
OS pin. TestDesign publishes any count/fixture amendment before Code. Every row
has one literal filter, one time estimate in minutes and a separate execution floor.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0931-red/` | or-filter-red | `/*/Antiphon.Tests.Checkpoints/CheckpointOrFilterTests/C0931_*` | V-1..V-7, R-1..R-3 | 31 executed: 18 intended assertion failures, 13 controls pass, 0 skipped | 31 | 9 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c0931-green/` | or-filter-green | `/*/Antiphon.Tests.Checkpoints/CheckpointOrFilterTests/C0931_*` | V-1..V-7, R-1..R-3 | all 31 passed, 0 failed/skipped | 31 | 9 |
| CP-3 | S2 | CP-2 | importer-row-regression | `/*/Antiphon.Tests.Checkpoints/(CheckpointImportTests*)\|(RowRunnerTests*)\|(RerunPolicyTests*)/*` | R-4 | all 45 base methods passed, 0 failed/skipped; reconcile source census before Code | 45 | 1 |
| CP-4 | S2 | CP-2 | plan-bundle-contract | `/*/Antiphon.Tests.Application/InstructionBundleTests/each_stage_bundle_is_ascii_and_under_the_size_cap` | R-5 | all 6 argument cases passed, 0 failed/skipped | 6 | 1 |

--- next stage ---
next: test-design
handoff: Freeze CARD-0931's importer/refusal and zero-execution witnesses, counts and independent PCs against the landed CARD-1532 then CARD-1531 base; retain one test build per red/green source group, no normalization or nested builds, and the separate embedded-bundle activation requirement.
artifact: docs/superpowers/plans/2026-10-23-card-0931-or-filter-validation-plan.md
