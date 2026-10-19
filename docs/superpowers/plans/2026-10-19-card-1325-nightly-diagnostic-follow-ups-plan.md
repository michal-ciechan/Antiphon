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
