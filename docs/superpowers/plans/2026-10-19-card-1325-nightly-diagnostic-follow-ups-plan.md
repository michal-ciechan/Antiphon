# CARD-1325: nightly diagnostic follow-ups

Plan date: 2026-10-19. Board: Antiphon. Card ID:
`9c0a4752-e19a-4ff9-9143-8acbe9385862`.
Inspected base and fetched `origin/master`:
`6ec71d427574f3a1b74bfbe2042e0ea9a819d191`.
Assigned plan branch: `feat/card-task-c7b0dc95`.

Repair the diagnostic explanation boundary and quoted-state classification in
two sequential 60-minute slices. Preserve actual TUnit failure shapes in the
regression inputs. This is master-only work. Do not run or change a live
Windmill job, run a nightly/RC/Unit/full-assembly/E2E lane, cut a release,
refresh live authority, or activate a service.

Verification is a separate stage. This artifact specifies behavior, witnesses,
and bounded selections; TestDesign must add the executable verification design
and checkpoint manifest before Code starts. There are no unresolved operator
choices or contingent defaults requiring a Decide stage.

## Owners and current evidence

Read owners: [nightly watchdog](../../nightly-watchdog.md),
[testing and build, Nightly](../../testing-and-build.md#nightly),
[checkpoint manifest](../../testing-and-build.md#checkpoint-manifest-card-0585),
[release gates](../../release-gates.md), and
[orchestration loop](../../orchestration-loop.md).
The older [CARD-1306 investigation](../../investigations/2026-10-17-card-1306-investigation.md)
describes the pre-fix parser; its proposed last-marker solution is historical,
not this plan's target behavior.

CARD-1306 fix `42950a101ee838adab516d57fe44f16b59c80163` reached master through
`1e32168a88c9dc8d3fc088ce468d98f3dec505ea`. The follow-up card is still valid;
do not repeat its already-landed Timeout/Cancelled recognition or UID guard fix.

Both required placement reads were made on 2026-10-19:
`GET /api/runner-defaults` (revision 2, global default configured, no kind
overrides or unresolved references) and `GET /api/session-runners` (available
Linux and Windows entries). Availability is a point-in-time observation, not a
reservation. Re-read at dispatch. Omit `-Runner` and `-Platform`; this parser
work has no OS requirement. If removing a previous OS pin, use `-Platform Any`.
No fleet hostname or address is part of the plan. Checkpoint execution context
(lane label): **offline scripts**, with the available default runner. This
label does not authorize a product verification lane or a second host run.

## Ground truth

| Card assumption or request | What current master does | Disposition |
|---|---|---|
| (a) The last exception-field cut leaks exception text when the explanation contains `, Exception = `. | `Get-NightlyDiagnosticFailureMessage` in `scripts/lib/nightly-coverage.ps1:202` selects the last failure-state match, then uses `LastIndexOf(', Exception = ')`. The captured `scripts/fixtures/nightly/d13-error-pair.ps1` repeats the message in `TrxExceptionProperty`, state explanation, and `TUnit.Engine.Exceptions.TestFailedException`. Replacing all repeated occurrences with the delimiter-bearing message reproduces the leak. | Open; S1. |
| (a) The six-row classifier fixture proves the cut. | `New-D13StateRecord` and `Test-D13_StateClassification` in `scripts/test-nightly-tests.ps1:838` onward use an unrelated `System.Exception: outer` for the delimiter row. It passes despite the real-shaped counterexample. Its Timeout shape also omits the producer's trailing `Timeout` field, and its Passed shape has a synthetic exception field. | Open; repair fixtures with S1, retaining explicit provenance. |
| (b) Quoting a state property inside a failed state's own text can classify Passed. | `Get-NightlyDiagnosticStateName` at `nightly-coverage.ps1:187` selects the last regex match across the entire record. The existing test quotes only the bare name, without ` {`. A derived captured Error record containing `PassedTestNodeStateProperty {` returns Passed. | Open; S2. Also cover quotations in the echoed exception and earlier TRX/display text. |
| Timeout and Cancelled need recognition. | The current switch already maps both to Failed, retains mixed pass/failure nodes, and retains framed unknown states as Failed. `D13_ExecutionGuards` now passes actual parsed nodes to the missing-UID check. | Already done by CARD-1306; preserve. |
| (c) Capture real Timeout and Cancelled properties when possible. | The card records that earlier probes yielded Error. The investigation reports reflected Timeout shape `Explanation, Exception, Timeout`, Cancelled shape `Explanation, Exception`, and TRX outcome Failed for both. Current fixtures are hand-composed, not real execution captures of these states. | Evidence limitation remains; bounded capture attempt within S1, with separate observed/derived/reflected provenance. Never relabel Error or reflection as a real timeout/cancellation capture. |
| The quoted-state residual is diagnostic; TRX keeps the suite red. | `ConvertTo-NightlyNativeSuiteVerdict` at `nightly-coverage.ps1:989` feeds TRX rows into `ConvertTo-NightlyCoverageVerdict`; diagnostic nodes supply expanded UID/identity checks and diagnostic messages. A genuine Failed TRX row keeps the suite red. | Confirmed; correct diagnostic classification and message retention without claiming to fix an observed suite false-green. |
| (d) A frozen release authority mismatches after this script changes. | `scripts/lib/release-authority.ps1:49` includes `lib/nightly-coverage.ps1` in `ReleaseAuthorityScripts`; validation at line 591 adds `script-digest-mismatch:<path>` when the bound bytes differ. | Expected existing behavior. Record the consequence; no hash-list, validator, authority-file, or release workflow change. |

Plan-time checks ran at the inspected base with clean tracked source, through
`scripts/build-slot.ps1`. No build or product lane ran:

- `test-nightly-tests.ps1 -Case D13_StateClassification`: **6 named assertions
  passed, 0 failed**, exit 0. These are script rows, not six TUnit executions.
- Two direct diagnostic observations using the captured D13 record with all
  matching message occurrences replaced: both card defects reproduced. For
  the delimiter case, expected exact-message equality was false; actual text
  continued through `, Exception = TUnit.Engine.Exceptions.TestFailedException:
  ShouldAssertException: boom`. For the quoted-state case, expected Failed,
  actual Passed. These were observations of unchanged production functions,
  not new committed tests or Code acceptance evidence.

The local observation script and generated harness JSON are ignored under
`.antiphon/`; only this plan is the deliverable. The new regression tests must
reproduce the behavior independently at their committed red tips.

## Decisions

**D-1 — Keep the change inside the pinned diagnostic adapter.** TUnit remains
1.44.0 and MTP remains 2.2.2. Fix parsing and its fixtures in the existing
PowerShell library/harness and C# wrapper. Reject a package bump, a new
diagnostic transport, a TRX outcome-normalizer expansion, or an execution-policy
change: none is needed to repair these two demonstrated defects.

**D-2 — Find field boundaries from the supported record shape.** A first/last
occurrence of `, Exception = ` is not a field parser. Within the selected failure
property, enumerate possible exception-field boundaries and validate the
remaining exception and state suffix against the pinned shapes. In particular,
the TUnit `TestFailedException` echo must agree with the explanation after its
known `[Test Failure] ` prefix; the validated split must keep all embedded
delimiter text and exclude the actual exception body. Recognize a null exception
and the trailing Timeout field where applicable. Keep physical LF/CRLF and
message bytes unchanged; do not trim, normalize, or deduplicate the explanation.
Reject replacing `LastIndexOf` with `IndexOf`, a greedy/lazy regex alone, or a
special case for the literal word `INNER`.

**D-3 — Select one actual state property before examining its free text.** Use
the pinned `TestNode.Properties` envelope and the producer's identifier/file
location-to-state transition. Current captured records put the state after
`TestMethodIdentifierProperty` and optional `TestFileLocationProperty`; earlier
`TrxExceptionProperty` text is not a state. The short synthetic shape has the
identifier immediately followed by state. Once the state header is selected,
its Explanation and Exception are opaque text for state classification. Share
that selected state span between classification and message extraction in S2;
the two functions must not choose different occurrences. Reject a global
first/last match or arbitrary brace counting across unescaped message text.

**D-4 — Bound the guarantee to distinguishable pinned records.** These are
human-readable `ToString()` strings, not an escaped serialization. Ordinary
quotes of state names/braces and field delimiters must parse correctly; an
arbitrary message can still mimic an entire property envelope. Validate a
unique structural interpretation. If the state cannot be selected uniquely,
retain the existing conservative Failed execution outcome; never promote it
to Passed/Skipped or drop its UID. If the failed state is known but the
explanation split is ambiguous, retain Failed and omit Message rather than
publish a fabricated exact explanation. A missing Message is already supported
by the node contract. Preserve strict discovery/version/identity refusal.
Reject promising a general parser for every possible unescaped string. Tests
must distinguish supported exact extraction from this conservative fallback.

**D-5 — Preserve provenance, not synthetic success.** The existing captured
D13 Error pair remains unchanged as a regression input. Add a small reviewed
PowerShell fixture containing new selected records and explicit expected
messages; label each record captured, derived from a capture, or reflected.
Store real execution captures only when actually observed at the pinned
versions. Retain complete raw diagnostic/TRX output as ignored evidence; do not
force-add logs, TRX, or receipts. Replace the misleading last-field assertion
name with the behavior it proves and update the wrapper's required row name.

**D-6 — Time-bound the unavailable-state follow-up.** Allow at most ten minutes
of S1 for local producer probes, using a scratch copy of the pinned probe
project, one short bounded timeout and one bounded cancellation attempt.
Cap each probe test process at 30 seconds, with explicit owned-child termination
and join on expiry. Use the host build slot and alternate output. Capture
an ordinary thrown Error with the troublesome message in the same probe work.
Do not run a live job, kill an unrelated process, change packages, or keep
trying until a desired state appears. If Timeout/Cancelled still emit Error,
record the attempted trigger, actual state, package versions, and limit; keep
their reflected-shape controls and the conditional capture obligation visible.
This absence does not block the two proven parser repairs or count as real
Timeout/Cancelled integration evidence.

**D-7 — Master only; authority remains bound to its original bytes.** Publication
uses the normal reviewed master landing. Do not cherry-pick to an RC branch or
deploy from this worktree. If the script later enters a separately authorized
release candidate, its authority must be produced for that candidate SHA and
script bytes; an older authority's digest mismatch is correct. Do not rewrite
frozen evidence or weaken validation to remove it.

## Implementation slices

S1 precedes S2 because both touch the same parser/harness. Each slice budgets
60 minutes for authoring, its one red witness, its one narrow green selection,
and commits. If a genuine build-slot delay or unresolved parsing shape exceeds
the box, report it and retain pushed work; do not widen the test selection or
quietly waive an assertion. Commit/push the compiling red witness before its
fix, then commit/push the fix before the green checkpoint. The green run binds
that exact committed source. No source edits while a run is in flight.

### S1 — Explain the failure without its echoed exception (60 minutes)

Files:

- `scripts/lib/nightly-coverage.ps1`: replace the last-delimiter algorithm in
  `Get-NightlyDiagnosticFailureMessage` with D-2's validated split; update the
  misleading comment in `ConvertFrom-NightlyDiagnosticRecord`.
- `scripts/fixtures/nightly/c1325-diagnostic-records.ps1` (new): curated Error
  records retaining the earlier TRX message, repeated state explanation,
  exception type/echo, stack, and outer envelope. Include provenance and literal
  expected explanation. Include reflected Timeout/Cancelled shapes with correct
  field order; Timeout includes `Timeout = <duration>`.
- `scripts/test-nightly-tests.ps1`: new `Test-D13_ExplanationBoundary`; correct
  `New-D13StateRecord`'s state-specific shapes and the delimiter-bearing row in
  `Test-D13_StateClassification`. Preserve the existing six behaviors; rename
  the last-exception-field assertion to an exact-message assertion.
- `tests/Antiphon.Tests/Scripts/NightlyExecutionDiagnosticTests.cs`: add
  `D13_ExplanationBoundary`, update required row names/counts to the final
  assertion inventory, retain `ParallelLimiter<ProcessSpawnLimit>`.

Red witness: `/*/*/NightlyExecutionDiagnosticTests/D13_ExplanationBoundary`.
At least the captured-shape Error with an echoed `, Exception = ` message must
fail exact equality on the pre-fix library. LF and CRLF inputs, multiple
delimiter occurrences, a delimiter-looking exception type in the message,
normal/no-delimiter failure, null exception, and Timeout suffix are one bounded
case matrix. Expected strings must be literal fixture expectations, not output
from the function under test. A build or fixture error is not the red witness.

Narrow green selection, one invocation:
`/*/*/NightlyExecutionDiagnosticTests/*` (five TUnit methods after S1).
This retains `D13_ErrorTerminal`, `D13_ExecutionCompatibility`,
`D13_ExecutionGuards`, and the reshaped `D13_StateClassification` alongside the
new witness. Require exact Message equality, preserved identity/UID and Failed
outcome, and no exception stack appended. D-6's producer captures are diagnostic
observations, with intentional probe failures recorded separately from the
red-first parser witness and green regression counts.

### S2 — Keep quoted states inside the failure text (60 minutes)

Files:

- `scripts/lib/nightly-coverage.ps1`: add a bounded internal state-property
  selector and use its result in `Get-NightlyDiagnosticStateName` and
  `Get-NightlyDiagnosticFailureMessage`. Preserve normalization, discovery,
  nonterminal omission, unknown-execution fallback, and TRX verdict ownership.
- `scripts/fixtures/nightly/c1325-diagnostic-records.ps1`: add derived/captured
  quoted-state records with the actual message echo, not just the bare name.
- `scripts/test-nightly-tests.ps1`: new `Test-D13_StatePropertyBoundary` with
  quoted Passed/Skipped/nonterminal/failure headers in Explanation, Exception,
  preceding TRX text and display name; add a uniquely framed real state control
  and an ambiguous-envelope conservative-failure control.
- `tests/Antiphon.Tests/Scripts/NightlyExecutionDiagnosticTests.cs`: add
  `D13_StatePropertyBoundary` with the exact named assertion inventory.
- `docs/testing-and-build.md` (Nightly section): short record-format limitation
  and fixture/provenance pointer, including real Timeout/Cancelled availability.
  Record the existing authority-hash consequence; change no release procedure.

Red witness: `/*/*/NightlyExecutionDiagnosticTests/D13_StatePropertyBoundary`.
The echoed Error whose own explanation contains `PassedTestNodeStateProperty {`
must fail Failed-outcome and exact-message expectations before S2's selector.
Also quote a failed-state header so extraction cannot silently switch to that
inner header. A correct outcome with a missing/wrong Message is insufficient
for the supported record shapes.

Narrow green selection, one invocation:
`/*/*/(NightlyExecutionDiagnosticTests*)|(NightlyDiscoveryParserTests*)/*`.
Expected roster after S2: six diagnostic methods plus three discovery methods,
**nine TUnit executions**, all green with no skips. This small affected set
protects shared classification in discovery and execution, multiline and prefixed
records, version/duplicate/missing-identity refusals, and existing terminal
behavior. It is not a whole test lane.

The new case must also join the failure record to a matching Failed TRX and
discovery UID through `ConvertTo-NightlyNativeSuiteVerdict`: full coverage,
`testsPassed=false`, `failed-row`, and retained exact diagnostic Message. Use
process exit 0 as the stronger verdict control. No live suite is needed.

## Test-design handoff and acceptance

TestDesign adds `## Verification design` ending with `### Checkpoints`, naming
the offline-scripts execution context and each slice. Use these selections as
the closed ordinary scope; no Unit/whole-assembly/nightly/RC additions. Include
one committed red witness and one green checkpoint per slice, the bounded
producer-probe commands and their expected intentional failures, exact build
outputs, assertion inventories, and numeric execution/time floors. Generated
probe output must remain ignored, and a scratch project must not enter the
execution-policy census. No fake build or zero-test row qualifies.

For C# wrapper runs build `tests/Antiphon.Tests` to distinct `bin-c1325-s1/`
and `bin-c1325-s2/` outputs through the checkpoint tool/build-slot gate. Bind
receipts to clean committed SHAs; remove all task-owned alternate output
directories at completion. Retain essential CHECKPOINT lines and actual counts
in the report. Code/Review run `check-evidence-diff.ps1` over base..HEAD.
TestDesign must budget ordinary Code and post-land method-scoped mutation
separately, without adding mutation runs to these Code slices.

Acceptance is:

1. Exact LF/CRLF explanations survive quoted delimiters with the real echo and
   property surroundings; exception text is excluded.
2. Quoted state tokens cannot replace the actual state or its explanation;
   malformed/ambiguous records cannot become clean terminal successes.
3. Existing Error/Failed/Timeout/Cancelled, Passed/Skipped, unknown state,
   discovery, UID/identity, and failed-TRX semantics remain covered by the named
   selections. Current already-fixed behaviors are not reimplemented.
4. Fixture provenance identifies actual captures separately from derivatives
   and reflected shapes. Unobserved Timeout/Cancelled captures remain an explicit
   limitation, with the bounded attempt recorded.
5. No live job, release authority, schedule, session lifecycle, or deployment
   changes. This card introduces no session that waits for input, so the
   input-wait release checklist is not applicable.

Next stage: **test-design**. After its committed manifest exists, Code can
execute S1 and S2, followed by ordinary Review and master-only landing.

## Verification design

TestDesign appended at `abbcb6651d63d04d219c4dd4f81f52a79df921a4`.
The fix design above is unchanged. Execution context: **offline scripts**;
master only, sequential S1/S2, 60 minutes each. This is the complete ordinary
selection, not an Interim or a product lane. No test/build was run by TestDesign;
the two plan-time observations remain historical, not acceptance evidence.

### Inspection

- Read `NightlyExecutionDiagnosticTests.cs` (all four methods),
  `NightlyDiscoveryParserTests.cs` (all three methods), `ScriptHarness.cs`
  (including exact row-count/name validation), and `ScriptHarnessProcess.cs`
  launch/deadline/cleanup path | V-1/V-2, R-1 through R-6. Retain the wrappers'
  `ParallelLimiter<ProcessSpawnLimit>`; their internal rows are not TUnit cases.
- Read `test-nightly-tests.ps1`: imports, `Get-C487ProbeRoot`,
  `New-C487FreshProbeEvidence`, all C1044 and D13 function bodies;
  `c487-harness.ps1` assertion, fixture-writing and count helpers | V-1/V-2,
  R-1 through R-6. New functions must be selectable by the existing `-Case`
  dispatch and must emit the existing `D13` row prefix.
- Read nearest new-fixture source `fixtures/nightly/d13-error-pair.ps1`
  (complete captured record and matching TRX), the C487 probe project/source,
  its empty Directory.Build props/targets, and discovery/execution/TRX fixture
  shapes | E/S recipes below and P-1. Keep the captured pair byte-for-byte.
- Read `nightly-coverage.ps1` version gate, record splitter, state/message
  readers, record conversion, diagnostic-log reader, normalization, expanded-UID
  and TRX cross-checks, native-suite/coverage verdicts | G-1 through G-31.
- Read checkpoint schema/importer, CARD-0403 filters, build-slot rules, Nightly,
  mutation rules, nightly watchdog owner, release-gates owner, orchestration
  stage order and D-2 policy; inspected `ReleaseAuthorityScripts` and its digest
  comparison | CP manifest, Cost, exclusions. Read `Invoke-NightlyOwnedProcess`
  and its native owner: that launcher is Windows-only, so it is **not** a
  portable producer-probe recipe or permission to pin dispatch to Windows.

### Delivery inventory

No new or changed asynchronous recipient-delivery path exists. The changed
path is local diagnostic bytes -> framed record -> selected state/message ->
terminal node -> native-suite verdict, joined by UID and class/method identity.
The matching TRX testId resolves to that class/method; discovery supplies the
required UID. Fixture files are local parser inputs, not a durable message
queue, recovery journal, or recipient receipt.

Thus producer/destination/persistence/recovery/observable receipt for a changed
queue path are **none**. Busy-recipient, already-eligible-recipient and
crash/enqueue-failure handoff tests are excluded because this card changes no
such handoff. P-1 observes real TUnit output locally; C1044's StartProcess seam
substitutes diagnostic text for a child and proves publication refusal only.
Neither proves Windmill execution, notification delivery, process-custody
qualification, or session input. No request, queue insert, event, Sent flag or
ack earns recipient credit; any later session-delivery change requires a new
inventory and matching complete UserPrompt transcript before acceptance.

### Proves it works now

- V-1: D-2 exact explanation boundary | offline parser through real PowerShell
  harness and C# wrapper | `D13_ExplanationBoundary`, CP-1/CP-2/CP-4 | supported
  E rows retain literal message bytes, UID/identity and Failed; E09/E11 omit
  Message conservatively. CP-1 is the single intended pre-S1 red witness.
- V-2: D-3/D-4 state selection | same layer plus native-suite composition |
  `D13_StatePropertyBoundary`, CP-3/CP-4 | quoted headers never select state or
  message; ambiguous framing retains Failed/UID; matching Failed TRX stays red
  at process exit 0 with complete coverage and exact diagnostic Message.
  CP-3 is the single intended pre-S2 red witness.
- V-3: D-5/D-6 provenance | bounded local producer observation P-1 below |
  inspect raw diagnostic/TRX state, version headers, trigger and cleanup record;
  distinguish captured/derived/reflected. An unavailable real Timeout/Cancelled
  is an explicit evidence limitation, never an invented integration pass.

**Shared fixture recipe and independent oracle.** Add the planned
`c1325-diagnostic-records.ps1`, consumed by the two new cases, with record ID,
provenance, source capture, raw record, literal expected UID/class/method,
expected outcome, expected Message (or explicit absence), and newline variant.
For capture derivatives replace all three original message occurrences (earlier
TRX text, explanation, exception echo), preserving the original stack and
property surroundings. Assert the fixture has all three replacements and one
intended outer state; do not obtain expected values with a production parser or
split helper. Literal expected messages include `[Test Failure] ` exactly once.
Keep spaces, tabs, braces, empty lines and physical newlines; equality is ordinal.
Fixture construction checks are setup failures, not extra `PASS D13` rows or
mutation kills. All variants call `ConvertFrom-NightlyDiagnosticLog -Kind
execution` via real temporary files, not a hand-built terminal-node substitute.

**S1 case inventory.** `D13_ExplanationBoundary`: 11 records, two assertions
per record, **22 named harness rows / one TUnit execution**. Names are exactly
`D13 EB <ID> identity` and `D13 EB <ID> message` for E01-E11. Identity asserts
one node, no parser error, literal UID/class/method and Failed; message asserts
exact literal equality for supported records, and absence of the `Message`
property for E09/E11. Absence must not mean merely empty-string coercion.

| Case | Recipe / variant | Oracle / reachability / classification |
|---|---|---|
| E01 | Captured D13 derivative; LF; replace message with `ShouldAssertException: boom, Exception = INNER still explanation` everywhere. | Exact prefixed explanation; real echoed delimiter reaches the current LastIndexOf defect. Derived, supported. Required CP-1 decisive red. |
| E02 | Same, LF; two embedded delimiters: `boom, Exception = alpha, Exception = beta`. | Both embedded delimiters retained; actual exception excluded. Derived, supported. |
| E03 | E02 with every physical LF changed to CRLF, including the literal oracle. | Exact CRLF, no appended stack. Derived, supported. |
| E04 | Echoed message includes `, Exception = TUnit.Engine.Exceptions.TestFailedException: decoy` and braces. | Entire decoy stays in explanation; defeats a type-name-only split. Derived, supported. |
| E05 | Unmodified D13 captured failure, LF, independently written existing literal explanation. | Ordinary no-delimiter capture still exact. Captured, supported. |
| E06 | Short identifier-to-Error shape, explanation `null exception body`, actual `Exception = <null>`. | Exact explanation, no null/suffix text. Reflected shape, supported. |
| E07 | Short Timeout shape, explanation `timed out, Exception = detail`, exception `System.TimeoutException: timed out`, trailing `Timeout = 00:00:00.0250000`. | Exact explanation and Failed; Timeout suffix excluded. Reflected shape, supported. |
| E08 | Short Cancelled shape, explanation `cancelled by the run`, `System.OperationCanceledException: cancelled`; no Timeout field. | Exact explanation and Failed. Reflected shape, supported. |
| E09 | E01 with only the actual TestFailedException echo changed to different text. | Failed with same identity, Message absent; reaches failed echo validation. Derived malformed split, conservative fallback. |
| E10 | Echoed LF and CRLF content in one literal message, leading/trailing spaces and a blank line (`  boom\t`, empty line, `tail  `; materialize the tab/newline bytes). | Byte-exact explanation, no trim or normalization. Derived, supported. |
| E11 | Derive an echoed message `boom, Exception = TUnit.Engine.Exceptions.TestFailedException: boom` followed by physical LF and `   at Fixture.Frame()`. The outer exception repeats that whole message before the original stack/suffix. Candidate explanations are `[Test Failure] boom` and the whole enlarged prefixed message; record the two delimiter offsets and why both exception tails satisfy the pinned shape. | Known Failed state, Message absent because no unique valid split. Adversarial ambiguity; **binding required** until Code records raw payload and both candidate offsets/interpretations. |

E11 is not permission to invent a parse ambiguity: Code must show both distinct
interpretations against the supported D-2 shapes before accepting its oracle.
If no concrete reachable payload can be bound, retain the obligation and return
`next: plan` for that seam; do not silently replace E11 with E09 or mark PC-11
covered. An unbound payload remains **binding required after Code**, including
when its wrapper is green.

Keep `D13_StateClassification` at six rows. Correct state-specific shapes
(Passed has no Exception; Timeout has its trailing field), change the delimiter
row to the actual echo shape, and rename only its assertion to
`D13 explanation keeps embedded exception delimiters exactly`; update the
wrapper's required name. Preserve the other five existing row names verbatim.

**S2 case inventory.** `D13_StatePropertyBoundary` has **27 named harness rows /
one TUnit execution**. There are 12 supported records S01-S12, two rows each:
`D13 SB <ID> identity` and `D13 SB <ID> message`, with the E identity/message
oracles above. Add the three separately named rows following the table.

| Case | Recipe / variant | Oracle / reachability / classification |
|---|---|---|
| S01 | D13 Error derivative echoing `saw PassedTestNodeStateProperty { Explanation = decoy }` in all message copies, LF. | Failed and exact entire outer explanation. Reaches global-last classifier; CP-3 decisive red. Derived, supported. |
| S02-S04 | S01 with respectively `Skipped`, `Discovered`, `InProgress` quoted headers. | Failed node retained, not skipped or filtered; exact message. Derived, supported. |
| S05 | S01 with `FailedTestNodeStateProperty { Explanation = inner` inside the message. | Exact outer explanation, never extraction from inner failure header. Derived, supported. |
| S06 | Ordinary Error explanation; quoted Passed header only in actual exception stack text after the unchanged echo. | Exception-only token cannot relabel node; exact ordinary message. Derived, supported. |
| S07 | Quoted Passed header only in earlier TrxExceptionProperty message/stack, before identifier/location. | Earlier free text cannot supply state. Derived, supported. |
| S08 | Quoted Failed header only in TestNode display name, preceding Properties. | Actual state and outer message selected. Derived, supported. |
| S09 | S01 in CRLF. | Failed plus exact CRLF outer explanation. Derived, supported. |
| S10 | Short identifier-immediately-followed-by-Error record with quoted Passed header and a valid exception echo. | No mandatory file-location assumption; exact message. Reflected envelope with derived text, supported. |
| S11 | Actual uniquely framed Error with balanced and unmatched ordinary braces in explanation; no fake identifier envelope. | Braces are message bytes, not nesting instructions. Derived, supported. |
| S12 | S01 with the existing supported timestamp/logger prefix on the physical first line. | Framing accepts prefix without consuming explanation. Derived, supported. |

The additional rows are:

- `D13 SB ambiguous envelope stays failed`: place a complete second apparent
  identifier/location-to-Passed property transition inside failure free text;
  retain both candidate spans in the fixture notes. Expect one node with the
  original UID/identity, Failed, and absent Message. **Binding required** until
  Code supplies the exact payload and candidate offsets. Do not accept an
  ordinary quoted header as this ambiguity control.
- `D13 SB missing UID refuses`: remove the outer UID value from an otherwise
  valid S01. Expect `truncated diagnostic record: missing uid`; do not allow a
  quoted inner value to repair it. This adds the previously unbound missing-UID
  refusal input without adding another wrapper method.
- `D13 SB failed TRX owns verdict`: use S01, the original matching Failed TRX,
  and a freshly written discovery document naming the original UID; call
  `ConvertTo-NightlyNativeSuiteVerdict` with matching SHA/ref/policy and
  **ProcessExit 0**. Expect `coverageComplete=true`, `testsPassed=false`,
  `failed-row`, one matching terminal node with exact S01 Message, and no
  missing-expanded-row or trx-diagnostic-mismatch reason. This is composed
  offline evidence, not an actual E2E run of the test named by the old capture.

**P-1 bounded producer recipe (diagnostic exception to ordinary CP scope).**
Spend at most ten S1 minutes, including preparation/build/three single-method
observations. This is the only authorized extra producer build/test work.
Create an ignored scratch copy under `.antiphon/c1325-probe/` containing the
four inspected project/source/Directory.Build files; never edit the original
probe or execution policy. Replace scratch tests with `C1325Producer` methods:
`ErrorEcho` throws `Exception("boom, Exception = INNER still explanation")`;
`TimeoutProbe` uses `[Timeout(25)]` and a bounded 5-second delay accepting the
TUnit cancellation token; `CancellationProbe` throws
`OperationCanceledException` with an already-cancelled local token. The last
trigger may emit Error; that is an observation, not a real Cancelled capture.
Keep package pins unchanged and record resolved TUnit/MTP versions.

Code writes ignored `.antiphon/c1325-probe/run-probes.ps1` as the local bounded
supervisor before execution. It starts only its own direct `dotnet` child via
literal `ProcessStartInfo.ArgumentList`, captures both streams, gives each
probe 30 seconds including EOF, kills its owned tree on expiry and explicitly
joins the root and observed descendants under a further five-second cleanup
bound. No process-name kills, pre-existing executors or live runner sessions.
Use the existing platform-appropriate ownership primitives if needed; the
Windows-only nightly native launcher must not be invoked on Linux. A kill
request/root exit alone is not cleanup proof; failed join/drain stops probes
and is reported. Bind supervisor contents/hash and scratch source in ignored
P-1 evidence; **binding required**, never assert completion from an unwritten
helper. This scratch supervisor is observation tooling, not a new shipped seam.

Run build and supervisor sequentially through one slot-owning layer each:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1325-probe-build -SlotWaitMinutes 1 -- dotnet build .antiphon/c1325-probe/Probe.csproj --property:OutputPath=bin-c1325-probe/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1325-probe-observe -SlotWaitMinutes 1 -- pwsh -NoProfile -File .antiphon/c1325-probe/run-probes.ps1
```

The supervisor invokes the built `bin-c1325-probe/net9.0/Probe.dll` using
`dotnet exec`, separately with these exact filters:
`/*/*/C1325Producer/ErrorEcho`, `/*/*/C1325Producer/TimeoutProbe`,
`/*/*/C1325Producer/CancellationProbe`. Each invocation adds
`--report-trx --results-directory <fresh-method-directory> --diagnostic
--diagnostic-output-directory <fresh-method-directory>/diag` through literal
argument entries. Inspect native executed names/counts and raw state properties.
Expected when each finishes normally: one executed intentional failure, zero
passing/skipped; native nonzero exit (normally 2). Timeout/abort without a
complete result is unavailable capture, not an executed failure. Keep the
ordinary Error observation even when timeout/cancellation produce Error.
Budget exhaustion, build failure or missing state must be reported, not retried
past the cap. Retain attempted trigger, actual state or absence, exit, duration,
cleanup, version headers and raw paths. Curate a real capture only if observed;
never count P-1's intentional failures as CP-1/CP-3 red witnesses.

### Guards the regression

- R-1: captured Error explanation/identity and native verdict | existing
  `D13_ErrorTerminal`, 10 rows (5 each LF/CRLF), including exact Message,
  Failed TRX identity, complete failed verdict and exit-zero direct verdict.
- R-2: terminal classification retained | `D13_ExecutionCompatibility`, 3 rows:
  eight Passed/eight Skipped identities, legacy Failed retained, nonterminal
  updates omitted; `D13_StateClassification`, the six rows specified above.
- R-3: strict execution evidence | `D13_ExecutionGuards`, 4 rows: duplicate
  Error/version refused, parsed wrong UID cannot satisfy required UID, extra
  parsed failed identity cannot match empty TRX; S2 missing-UID row adds the
  separate malformed-identity boundary.
- R-4: discovery framing | `C1044_MultilineDiscovery`, 8 rows; independent
  nine-UID inventory (8 required/1 excluded), exact metadata, dispositions and
  digest in LF/CRLF. `C1044_DiscoveryReconciliation`, 8 rows: prefix/suffix
  orphans, partial state/identity, duplicate, producer absent/sentinel
  preservation, complete multiline publication. StartProcess is a declared
  substitute, not native execution/delivery evidence.
- R-5: pinned parser compatibility | `C1044_ParserCompatibility`, 17 rows:
  golden discovery/terminal identity sets, nonterminal omission, prefixed and
  multiline framing, JSON round-trip, three disposition checks, independent
  TUnit/MTP drift, duplicate, missing type/method, garbage and header-only
  refusals. Preserve every existing named row; no test renames/count reductions.
- R-6: the two reproduced defects remain killed | new EB/SB inventories and
  their exact-message/Failed assertions, including joined Failed TRX at exit 0.
  Supported records passing with absent Message fail this regression gate.

### Guard inventory

Scope includes changed parsing guards and the explicit strictness invariants
preserved by D-4/S2. Independently bypassable guards have separate controls.
No safety-critical guard in that scope is exempt or mapped to another guard's
PC. Untested/unbound cases below remain obligations, not claimed coverage.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-2 embedded delimiters never determine the exception cut alone | PC-1 |
| G-2 | D-2 known TestFailedException echo must agree | PC-2 |
| G-3 | D-2 exact explanation bytes, including physical newlines | PC-3 |
| G-4 | D-2 Timeout suffix is outside the explanation | PC-4 |
| G-5 | D-2 valid null exception retains its explanation | PC-5 |
| G-6 | D-3 earlier TRX/display text cannot supply state | PC-6 |
| G-7 | D-3 quoted explanation headers cannot relabel the node | PC-7 |
| G-8 | D-3 quoted exception headers cannot relabel the node | PC-8 |
| G-9 | D-3 classification and extraction share the selected state | PC-9 |
| G-10 | D-4 ambiguous state envelope retains Failed and original UID | PC-10 |
| G-11 | D-4 multiple valid explanation splits omit Message | PC-11 |
| G-12 | D-4 framed unknown execution state retains a failed node | PC-12 |
| G-13 | S1/S2 existing Timeout maps to Failed and stays present | PC-13 |
| G-14 | S1/S2 existing Cancelled maps to Failed and stays present | PC-14 |
| G-15 | S2 genuine Passed remains Passed | PC-15 |
| G-16 | S2 genuine Skipped remains Skipped | PC-16 |
| G-17 | S2 Discovered cannot count as terminal execution | PC-17 |
| G-18 | D-4 duplicate diagnostic UID is refused | PC-18 |
| G-19 | D-1/D-4 unsupported TUnit header is refused | PC-19 |
| G-20 | D-1/D-4 unsupported MTP header is refused independently | PC-20 |
| G-21 | D-4 missing UID is refused | PC-21 |
| G-22 | D-4 missing type is refused | PC-22 |
| G-23 | D-4 missing method is refused independently | PC-23 |
| G-24 | S2 required expanded UID must actually be present | PC-24 |
| G-25 | S2 diagnostic identity counts must reconcile with TRX | PC-25 |
| G-26 | D-4 discovery raw-marker/parsed-count discrepancy refuses | PC-26 |
| G-27 | D-4 discovery record without state refuses | PC-27 |
| G-28 | S2 genuine Failed TRX cannot become green at process exit 0 | PC-28 |
| G-29 | S2 adjacent records cannot borrow each other's state/identity | PC-29 |
| G-30 | D-4 unrecognized diagnostic input refuses | PC-30 |
| G-31 | S2 InProgress cannot count as terminal execution independently | PC-31 |

### Positive controls

All controls are mandatory **after land**, in SourceLanding Mutation, with an
unmutated method baseline, compiling defect, intended assertion red, exact
restoration, fresh build and method green. Code runs ordinary V/R; Review
judges this inventory before land. No PC runs in the 60-minute Code slices.
TestDesign executed **0/31 PCs**; this is a design handoff, not PC-clean.
Mutation closure requires **all 31 PCs run**,
and restoration evidence. Syntax/fixture errors, wrong roster, zero execution
or a different red assertion do not qualify.

Each method below means the exact filter `/*/*/<Class>/<Method>` (no wildcard
class/suite PC run). Class is `NightlyExecutionDiagnosticTests` unless prefixed
`NightlyDiscoveryParserTests.`. Mutate production in `nightly-coverage.ps1`,
never the fixture oracle or assertion; choose the smallest compiling equivalent
edit at landed source. New selector sites and E11/ambiguous-envelope payloads
remain **binding required**: Code must name actual function/site, exact raw
input and oracle reachability. Review preserves unresolved status; a green
method alone does not bind a planned mutant.

| PC | Break mapped guard with compiling defect | Exact method red at decisive assertion |
|---|---|---|
| PC-1 | Restore last-delimiter cut for supported Error. | `D13_ExplanationBoundary`: `D13 EB E01 message` differs by leaked exception. |
| PC-2 | Accept mismatched TestFailedException echo as a valid split. | `D13_ExplanationBoundary`: `D13 EB E09 message` must be absent. |
| PC-3 | Normalize extracted CRLF to LF. | `D13_ExplanationBoundary`: `D13 EB E03 message` fails ordinal equality. |
| PC-4 | Include Timeout field text in extracted explanation. | `D13_ExplanationBoundary`: `D13 EB E07 message` fails equality. |
| PC-5 | Return no Message for a valid null exception. | `D13_ExplanationBoundary`: `D13 EB E06 message` fails equality. |
| PC-6 | Classify using first global state header, ignoring property transition. | `D13_StatePropertyBoundary`: `D13 SB S07 identity` requires Failed. |
| PC-7 | Classify using last global state header. | `D13_StatePropertyBoundary`: `D13 SB S01 identity` requires Failed. |
| PC-8 | Override selected state from a Passed header found only in the exception. | `D13_StatePropertyBoundary`: `D13 SB S06 identity` requires Failed. |
| PC-9 | Keep correct classification but choose last failure header for extraction. | `D13_StatePropertyBoundary`: `D13 SB S05 message` requires outer explanation. |
| PC-10 | Accept the apparent Passed candidate when selector has multiple valid state candidates. | `D13_StatePropertyBoundary`: `D13 SB ambiguous envelope stays failed`. Binding required. |
| PC-11 | Return first candidate Message when split validation finds multiple candidates. | `D13_ExplanationBoundary`: `D13 EB E11 message` requires absence. Binding required. |
| PC-12 | Omit a framed unknown state rather than retain Failed. | `D13_StateClassification`: `D13 unknown state is failed`, count/UID/outcome. |
| PC-13 | Drop only Timeout terminal nodes. | `D13_StateClassification`: `D13 timeout-only is failed`, count/UID/outcome. |
| PC-14 | Drop only Cancelled terminal nodes. | `D13_StateClassification`: `D13 cancelled-only is failed`, count/UID/outcome. |
| PC-15 | Map actual Passed to Failed. | `D13_ExecutionCompatibility`: `D13 pass and skip unchanged`, Passed outcomes. |
| PC-16 | Map actual Skipped to Passed. | `D13_ExecutionCompatibility`: `D13 pass and skip unchanged`, Skipped outcomes. |
| PC-17 | At the Discovered execution filter, prematurely return a Nodes array containing that parsed Discovered record instead of omitting it. This avoids duplicate-UID refusal masking the intended detection. | `D13_ExecutionCompatibility`: `D13 nonterminal updates omitted`, terminal count/outcome. |
| PC-18 | Disable diagnostic-log duplicate-UID throw. | `D13_ExecutionGuards`: `D13 duplicate Error refuses`. |
| PC-19 | Treat TUnit header check as true; retain MTP check. | `NightlyDiscoveryParserTests.C1044_ParserCompatibility`: `C1044 unsupported TUnit refuses`. |
| PC-20 | Treat MTP header check as true; retain TUnit check. | `D13_ExecutionGuards`: `D13 unsupported version refuses`. |
| PC-21 | Substitute a nonempty placeholder for absent/empty UID instead of refusing. | `D13_StatePropertyBoundary`: `D13 SB missing UID refuses`. |
| PC-22 | Remove only missing-type refusal in record conversion. | `NightlyDiscoveryParserTests.C1044_ParserCompatibility`: `C1044 missing type refuses`. |
| PC-23 | Remove only missing-method refusal in record conversion. | `NightlyDiscoveryParserTests.C1044_ParserCompatibility`: `C1044 missing method refuses`. |
| PC-24 | Make expanded-row result Ok=true despite missing required UID. | `D13_ExecutionGuards`: `D13 missing failed UID refuses`, with parsed replacement UID still present. |
| PC-25 | Make diagnostic/TRX cross-check Ok=true despite nonempty mismatch. | `D13_ExecutionGuards`: `D13 extra failed identity refuses`. |
| PC-26 | Bypass raw discovery marker count check. | `NightlyDiscoveryParserTests.C1044_DiscoveryReconciliation`: `C1044 prefix orphan refuses`. |
| PC-27 | Continue instead of throwing on missing discovery state. | `NightlyDiscoveryParserTests.C1044_DiscoveryReconciliation`: `C1044 partial cannot borrow state`. |
| PC-28 | Bypass Failed terminal-row verdict branch, keeping process-exit checks. | `D13_StatePropertyBoundary`: `D13 SB failed TRX owns verdict`, false testsPassed and failed-row at exit 0. |
| PC-29 | Stop flushing buffered record at next node start (keep logger boundaries). | `NightlyDiscoveryParserTests.C1044_DiscoveryReconciliation`: `C1044 partial cannot borrow state` must retain the specific malformed-record refusal. |
| PC-30 | Remove final unrecognized-format refusal and return empty Nodes. | `NightlyDiscoveryParserTests.C1044_ParserCompatibility`: `C1044 garbage refuses`. |
| PC-31 | At the InProgress execution filter, prematurely return a Nodes array containing that parsed InProgress record instead of omitting it. | `D13_ExecutionCompatibility`: `D13 pass and skip unchanged`, exact terminal inventory/outcome. |

The wrapper reports red through `ScriptHarness.Validate`'s exit-code assertion;
its captured output must identify the precise `FAIL D13 ...` / `FAIL C1044 ...`
row above. A wrapper count-only failure is not the intended control.
Mutation uses the unchanged local `run-checkpoint.ps1` copied with its build-slot
library to the assigned external evidence root before edits. Each phase gets
fresh `bin-c1325-pc<N>-<variant>-<phase>/`, external results, exact method filter,
`-MinExecuted 1`, and matching `-Expect Class.Method`; no new commit/push from
the snapshot. Baseline/restored green exit 0, intended red exit 1, exactly one
TUnit method per phase, zero skipped. Await children before editing/restoring.
Record landed SHA, mutant diff, failing row, fresh build/TRX and restoration.

Inventory audit: **guards=31, mapped=31, missing=0, duplicate PC maps=0**;
31 required mutation variants. Binding-required controls are visible above,
not exempted. These counts describe the design, not completed executions.

### Out of scope

- Live Windmill, watchdog/Telegram delivery, nightly/RC/Unit/full-assembly/E2E
  lanes, deployment, release publication and input-wait/session lifecycle:
  explicitly excluded by D-7/brief, with no changed delivery path.
- Release-authority mutations and live authority refresh: no validator/hash-list
  changes. Inspection establishes the existing script-digest consequence;
  this parser selection makes no new release-authority qualification claim.
- Package upgrades, arbitrary unescaped-string parsing, and acceptance of
  ambiguous text as exact explanation: excluded by D-1/D-4. Real Timeout and
  Cancelled remain conditional observations; reflected rows prove parsing only.
- Requalifying untouched checkpoint/process-owner infrastructure, JSON format,
  disposition policy and all coverage-policy branches: existing compatibility
  rows may exercise them, but this card does not claim a complete mutation
  audit of those separate owners. No new process-owner production change is
  authorized by P-1. Need for one returns to Plan.

### Checkpoints

Each row is one isolated build plus one exact filter. Run rows by explicit
`--rows CP-n` at the committed slice boundary, never all four at one tip.
CP-1 and CP-3 are expected-red checkpoints: native tool exit 1 and the specified
assertion failure are success of the witness, **not green acceptance**. Do not
loosen the checkpoint tool or receipt validator to certify these as green.
Commit/push new tests before each red; commit/push implementation before its
green. S1-green and S2-green must be source-clean at their respective SHAs.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1325-s1-red/` | explanation-red | `/*/*/NightlyExecutionDiagnosticTests/D13_ExplanationBoundary` | V-1, R-6 | exactly 1 executed, 1 failed, 0 skipped; E01 exact-message assertion red on pre-S1 parser | 1 | 4 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1325-s1/` | explanation-green | `/*/*/NightlyExecutionDiagnosticTests/*` | V-1, R-1, R-2, R-3, R-6 | all 5 listed methods, 0 failed/skipped; 45 named harness rows | 5 | 5 |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c1325-s2-red/` | state-red | `/*/*/NightlyExecutionDiagnosticTests/D13_StatePropertyBoundary` | V-2, R-6 | exactly 1 executed, 1 failed, 0 skipped; S01 Failed/exact-message assertion red on S1 parser | 1 | 4 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1325-s2/` | state-green | `/*/*/(NightlyExecutionDiagnosticTests*)\|(NightlyDiscoveryParserTests*)/*` | V-1, V-2, R-1, R-2, R-3, R-4, R-5, R-6 | all 9 listed methods, 0 failed/skipped; 105 named harness rows | 9 | 5 |

The escaped pipe is Markdown only; importer passes the literal CARD-0403 OR.
Roster arithmetic: existing diagnostic 10+3+4+6=23 rows, S1 adds 22=45;
S2 adds 27=72 diagnostic rows; discovery 8+8+17=33, total 105.
There are no parameterized wrapper methods: Min is 1/5/1/9, **16 ordinary
TUnit executions total, 14 intended green and two intended red**, not 16 unique
methods. The closed ordinary V/R union excludes only explicitly observational
V-3/P-1 and review-only provenance/authority inspection.

Use the checkpoint tool from a gated, isolated tool build (one setup build),
then its `run --plan` entry point; the tool owns row slots itself, so do not wrap
that invocation in another build-slot lease. Example setup and CP-1:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1325-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1325-tool/ --nologo
# Resolve this built tool DLL; use dotnet exec so launch cannot rebuild unleased.
dotnet exec tools/Antiphon.Checkpoints/bin-c1325-tool/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-19-card-1325-nightly-diagnostic-follow-ups-plan.md --rows CP-1 --expected-source-sha <full-committed-red-SHA>
```

The inspected tool project targets net9.0 and names its assembly
`Antiphon.Checkpoints`; require that exact setup output to exist. Equivalently
use an already-built, source-bound checkpoint tool. For
CP-2/3/4 substitute only row and exact committed SHA. Wait on the emitted run ID
until exit is not 75 (use at most 60 seconds per wait to retain status updates),
never settle with a live executor. Inspect fresh executed class/method roster,
counts, expected failures, slot fields and build/source binding. Validate green
receipts with `validate-checkpoint-receipt.ps1`; carry CP-4 as final ordinary
qualification of the S2 tip and CP-2 as historical S1 evidence. Preserve unedited
CHECKPOINT lines. Code/Review run `check-evidence-diff.ps1` over task base..HEAD.
Remove only task-owned alternate-output directories after their runs complete;
keep generated evidence ignored. No baseline full-lane rerun is authorized.

### Cost

All amounts below are **estimated wall-clock minutes**, not measured results.
Ordinary Code V/R floor = CP-1 4 + CP-2 5 + CP-3 4 + CP-4 5 = **18 minutes**,
including four isolated test-project builds. Exact filters are in the table.
One checkpoint-tool setup build = **3 minutes**. P-1 scratch preparation/build
and the three named producer filters = **10 minutes maximum**, reported
separately; no lane or extra red search. Total planned Code verification/setup
allowance = **31 minutes** (18+3+10), leaving 89 minutes within the two 60-minute
slices for authoring, fixture binding, review of results and commits. Allocate
S1 up to 22 verification minutes (3+4+5+10) and S2 9 (4+5). Slot delays are
reported separately and may stop a slice; they do not authorize a wider run.

Post-land Mutation floor = **279 minutes**: 31 variants, each with method-only
baseline **3**, compiling-defect/red **3**, restoration/fresh-build/green **3**.
The exact method filters are the PC table expanded as specified above. Every
phase includes its isolated build; per-PC restoration is in the final 3.
Allow **6 minutes** external evidence/driver setup and **15 minutes** binding,
discovery and reporting, for a Mutation commissioning estimate of **300 minutes**.
Combined planned verification/setup/reporting cost = **331 minutes**
(31 Code + 300 Mutation); authoring remains separately capped by the slices.
No PC discount for same-method reuse: all mutations touch the same library and
must run sequentially with per-control restoration evidence.

Savings: ordinary green acceptance uses **two builds/two filtered invocations**
for 5 then 9 executions. Running every method in a separate rebuilt checkpoint
would use 14 green builds; this avoids **12 builds**, an estimated **24 minutes**
at 2 minutes/build. Red proofs remain one per slice. No full-lane comparison or
claimed live-delivery saving is counted. Revise estimates from actual receipts,
without changing historical outcomes or silently shrinking the inventories.
