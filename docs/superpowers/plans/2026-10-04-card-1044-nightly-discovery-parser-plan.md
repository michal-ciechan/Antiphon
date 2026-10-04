# CARD-1044: complete nightly diagnostic discovery

Date: 2026-10-04. Stage: Plan with verification design folded in. Ready for Code;
no operator decision is outstanding. Code budget: 30-60 minutes.

The nightly adapter must preserve every discovered UID when a display name spans
physical lines, and refuse to publish discovery when its parsed inventory does
not reconcile with the discoveries in the diagnostic input. The dependent is
[CARD-1039's M-1/M-2 census and timing work](2026-10-04-card-1039-daily-nightly-plan.md).
This change repairs the adapter; it does not commission M-2 or establish nightly
qualification.

## Ground truth

Inspected source: `15dee8be8a76cf4e1828d71933fee8931b3d9d88`.
The live CARD-1044 description was read through `scripts/card.ps1 get CARD-1044
-Board Antiphon`. Its Windows receipt
`C:\logs\antiphon\card-1039\ce92d660\m0-m1-receipt.md` and sibling
`dropped-not-in-census.jsonl` are unavailable in this runner mirror; the following
production totals are attributed to the card, not independently remeasured.

| Card assumption / desired property | What the code or evidence actually establishes | Consequence |
|---|---|---|
| 14,469 TUnit discoveries should reach the census. | The card reports build `725c575dc`, TUnit 1.44.0.0 / MTP 2.2.2: 14,441 parsed plus 28 dropped; the incomplete census has 14,435 required and 6 excluded, digest prefix `2781e1c5`. | These are historical input-specific totals, never new hard-coded floors. Replay the retained input to unblock M-1. |
| A diagnostic record is one line. | `scripts/lib/nightly-coverage.ps1:282`, `ConvertFrom-NightlyDiagnosticLog`, splits on CRLF/LF, then requires the UID start and a recognized state on the same line. A display-name newline separates them; both fragments can be skipped. | Assemble complete records before state/identity extraction. |
| Disposition accounts for all discoveries. | `Get-NightlyDiscoveryDisposition` assigns required/excluded only to supplied nodes. `Get-NightlyDiscoveryCensus` checks duplicate supplied UIDs and nonempty required rows; it has no independent input total. | Put completeness enforcement at the parser boundary, before disposition or serialization. Do not change eligibility policy. |
| Existing guards catch partial parsing. | Version, unknown-format, missing identity, and duplicate checks exist, but one recognized row is enough for a partial result to return. `Invoke-NightlyProduceDiscovery` only rejects zero nodes before writing JSON. | Add an independent raw-discovery/parsed-node reconciliation check. |
| A pinned diagnostic necessarily contains a summary total. | The checked-in `scripts/fixtures/nightly/c487-probe/discovery.diag` contains version headers and nine node-update records, with no advertised total/footer. | Count the pinned discovery-state occurrences in the input independently of accepted nodes. Do not require an invented summary format. |
| Parser exceptions must prevent a successful inventory. | `Invoke-NightlyProduceDiscovery` parses before `Write-NightlyDiscoveryDocument`; its nightly caller records `discovery-produce-failed` on exception and evaluates the missing discovery as incomplete. | Retain this propagation; exercise the real producer boundary with fixture process output. |
| Only discovery uses this parser. | Execution diagnostics share the line loop; JSON discovery/execution have separate validated branches. The golden execution fixture has eight terminal nodes after nonterminal filtering. | Cover execution compatibility if the record iterator is shared; retain the JSON formats and version pin. |

A leased, two-second Plan probe called the current parser and census on temporary
copies of the nine-node fixture. It changed only Plain's display name or prefixed
an orphan discovery-state marker. No build or TUnit run occurred; exit was 0
because the probe measured the existing defect rather than asserting the fix.
Observed output:

```text
BUILD SLOT granted lease=f9d98cf7-e23b-4daf-9de0-c1ccadb5deed waited=0s maxcpucount=6
variant=golden discoveredMarkers=9 parsed=9 required=8 excluded=1 censusOk=True
variant=LF discoveredMarkers=9 parsed=8 required=7 excluded=1 censusOk=True
variant=CRLF discoveredMarkers=9 parsed=8 required=7 excluded=1 censusOk=True
variant=orphan discoveredMarkers=10 parsed=9 required=8 excluded=1 censusOk=True
BUILD SLOT released lease=f9d98cf7-e23b-4daf-9de0-c1ccadb5deed held=2s
```

Platform reads on 2026-10-04: `GET /api/runner-defaults` returned revision 2,
no kind overrides and no unresolved references; `GET /api/session-runners`
returned available, dispatch-eligible Linux and Windows descriptors, and one
unavailable draining descriptor. Re-read them at dispatch; these are observations,
not reservations. The Code checkpoint lane is portable PowerShell 7/.NET on the
effective default runner. Omit `-Runner` and `-Platform`; use `-Platform Any` only
to clear an inherited pin. Only the retained Windows evidence replay below needs
access to that evidence; no fleet host or address is prescribed.

## Decisions

**D-1 - Frame records, not physical lines.** Replace the line-at-a-time adapter
with bounded node-update record assembly. Recognize the pinned node-update/header
structure (including the existing bare golden records and logging prefixes),
retain continuation lines until the next record boundary, and flush the final
record at EOF, including a file without a terminal newline. Use the structural
`TestNode { Uid = TestNodeUid` anchor and node-update envelope, not arbitrary
braces or display-name punctuation as boundaries. Preserve physical newlines
inside the assembled record; identity remains UID/type/method metadata, never the
display name. A partial record must not borrow metadata/state from its successor.
Discovery candidates without a recognizable state must throw as malformed instead
of disappearing; recognized non-discovery states keep their existing filtering.
Reject a global newline replacement (destroys record boundaries), unbounded
cross-record regex matching, and display-name normalization as identity.

**D-2 - Independently reconcile before returning discovery.** For raw diagnostic
discovery, count occurrences of the pinned `DiscoveredTestNodeStateProperty {`
token across the entire original text before record parsing. After parsing,
require this count to equal the number of validated unique discovery nodes.
Retain duplicate-UID rejection; do not deduplicate into success. A discovery-state
occurrence outside a parseable record must still contribute to the expected count.
On mismatch, throw a stable diagnostic such as
`discovery-count-mismatch discovered=10 parsed=9`, including the input path for
triage, before returning nodes or writing an inventory. Keep the raw count separate
from the successful parsing loop: `expected = nodes.Count` and counts derived from
accepted records are forbidden. Keep existing unknown-format and zero-inventory
refusals. No new schema field or public override is needed.

This is a conservative guard over the retained pinned-format input. An ambiguous
extra marker (including one embedded in arbitrary display text) must refuse,
not silently supply a UID. It cannot prove delivery of records absent from the
input altogether. Replacing the diagnostic producer with a structured TUnit
export, accepting new framework versions, or redesigning execution reconciliation
would exceed this card. Reject a census-only check: it cannot see dropped input.

**D-3 - Preserve the existing consumers.** Leave
`Get-NightlyDiscoveryDisposition`, default OptIn/Explicit exclusion, profile-aware
exclusions, JSON v1, assembly hashing and required/excluded digest construction
unchanged. Validate exact UID sets and metadata, not counts alone. The fixture's
default disposition stays eight required/one excluded; a recovered multiline
Manual node must still be excluded. Let existing producer/caller exception handling
refuse incomplete evidence. Do not suppress errors or publish a partial JSON file.

**D-4 - Reuse the small script harness through narrow TUnit wrappers.** Add three
named C1044 cases to `scripts/test-nightly-tests.ps1` and a dedicated
`tests/Antiphon.Tests/Scripts/NightlyDiscoveryParserTests.cs` using
`ScriptHarness.RunHarnessCaseAsync`. Mark the class Integration and use
`ParallelLimiter<ProcessSpawnLimit>` because it launches pwsh. Each wrapper pins
its exact nonzero PASS-row inventory and fails on child failure; do not add a
timeout or retry. The new cases operate on private temporary fixture copies and
do not use `New-Efx` or run a nightly clone, DB, Docker, live runner or real suite.
Keep the golden fixtures unchanged. Explicit `-Case C1044_*` invocation already
uses the harness's case lookup, so do not increase the legacy default 80-row floor
or run its unrelated default selection. Add the focused cases to that default
only if their exact inventory is deliberately incorporated; that is unnecessary
for this card's checkpoint.

**D-5 - Two behaviors, two pending positive controls.** Ordinary Code runs the
three narrow tests; ordinary Review follows, then implementation land. Mutation
owns PC-1 (multiline completeness) and PC-2 (fail-closed reconciliation), each at
one exact method. No whole-Unit, namespace or full-nightly run is authorized.
The caller records the post-land verification companion before implementation
land, preserving these two PC obligations. Plan publication itself needs no
implementation Review/Mutation claim.

## Implementation slices

One committed slice group is sufficient; keep implementation and its regression
tests together to avoid publishing a permissive intermediate parser.

| Slice | Files | Work and closing tests |
|---|---|---|
| S1 | `scripts/lib/nightly-coverage.ps1`; `scripts/test-nightly-tests.ps1`; new `tests/Antiphon.Tests/Scripts/NightlyDiscoveryParserTests.cs` | Implement bounded record assembly and discovery reconciliation; add V-1, V-2 and R-1 below. Keep helpers local to the coverage library. Commit/push the complete slice before CP-1. |

If failure propagation actually needs a production caller edit, report the reason
and amend this plan before extending into `scripts/lib/nightly-tests-impl.ps1`;
the inspected flow already refuses. No schedule, execution-policy, project or
package changes are expected. Scripts remain ASCII and PowerShell 5.1-compatible;
the ordinary execution lane uses pwsh.

## Verification design

The folded design is small: three new nonparameterized TUnit methods, two PCs,
one isolated build and one class filter. Coverage-to-method bindings:

| ID | Class/method | Required witnesses |
|---|---|---|
| V-1 | `NightlyDiscoveryParserTests.C1044_MultilineDiscovery` | Copy the real nine-record discovery fixture; exercise LF and CRLF within display names, a blank continuation line, adjacent multiline records, and a multiline final record with no trailing newline. Include punctuation/braces in the name. Require all nine exact UIDs once, correct type/method/namespace/categories per affected UID, and eight required plus one excluded with the unchanged sorted-UID digest. Compare against independently specified expected fixture identities, not another invocation of the parser under test. Include multiline Plain and Manual to cover both dispositions. |
| V-2 | `NightlyDiscoveryParserTests.C1044_DiscoveryReconciliation` | Start with valid records so unknown-format/empty-input guards cannot mask the count check. Prefix and suffix a discovery-state occurrence without a parseable node; require the stable mismatch code with discovered=10 and parsed=9. Add a candidate truncated before state/identity and prove it cannot borrow its successor's metadata; retain duplicate refusal. Through the existing `NightlySeams.StartProcess` fixture seam, have the real `Invoke-NightlyProduceDiscovery` consume the malformed diagnostic; assert it throws, creates no output when absent, and leaves a preexisting sentinel output unchanged. A matching multiline fixture must instead produce a JSON document containing every expected UID. Restore the seam in `finally`. |
| R-1 | `NightlyDiscoveryParserTests.C1044_ParserCompatibility` | Unmodified golden discovery gives the exact nine UIDs and golden execution the exact eight terminal UIDs; InProgress/Discovered updates do not become terminal results. Also accept timestamp/logger-prefixed node envelopes. If framing is shared, give a terminal display name a newline and require the same eight terminal UIDs. Round-trip the current discovery JSON format, retain default and explicit profile-aware dispositions, and reject unsupported version headers, duplicate UIDs, missing type/method, garbage and header-only input. All failures must arise from the production adapter, never a manufactured throw in the test. |

Choose fixed named harness assertion rows while implementing each case and pass
the exact count plus decisive row labels to `ScriptHarness`; report those counts
separately from TUnit's three executed results. No runtime corpus generation or
14,469-node fixture is needed. Keep all runtime diagnostics/JSON under the private
results directory and gitignored. The existing golden input is the approved fixture.

### Positive controls (post-land, pending)

| PC | Behavior / compiling production mutation | Exact method filter | Intended red witness |
|---|---|---|---|
| PC-1 | Multiline completeness: temporarily restore physical-line record iteration while retaining the new reconciliation guard. | `/*/*/NightlyDiscoveryParserTests/C1044_MultilineDiscovery` | The formerly valid multiline input now throws a discovery mismatch or lacks its expected UID, so the named complete-inventory assertion fails. The harness must record this as a test failure, not a setup/build error. |
| PC-2 | Fail-closed reconciliation: bypass only the final raw-discovered versus parsed-count rejection, leaving framing, duplicate and identity checks intact. | `/*/*/NightlyDiscoveryParserTests/C1044_DiscoveryReconciliation` | The valid-nine-plus-orphan input returns/publishes instead of throwing `discovery-count-mismatch`; the explicit rejection assertion fails. Use the orphan fixture first so a different malformed-record guard cannot mask this control. |

One green/compiling mutation/intended red/restore/fresh green cycle per PC. Each
method must execute one result, with no skipped tests; a build, setup or fixture
failure is not red evidence. Do not co-schedule these mutations because they touch
the same production file. SourceLanding restoration and raw evidence stay in the
assigned external evidence root; no commit/push or source repair from that snapshot.

### Execution and evidence

Run CP-1 through the checkpoint tool after S1 is committed/pushed, using this
plan and the exact source SHA. Bootstrap the tool under `scripts/build-slot.ps1`
with an isolated `bin-c1044-tool/` output if no matching executable is available;
this is tooling bootstrap, not another product build. Then run `run --plan
<this-plan> --rows CP-1 --expected-source-sha <source-sha>` from the built tool
under the documented checkpoint workflow. Await `wait` until exit is not 75.
The tool acquires build slots for the row; never bypass a refusal or exit 4.
Keep source frozen during execution, preserve the complete CHECKPOINT line and
SHA-bound receipt, validate it with `scripts/validate-checkpoint-receipt.ps1`, and
clean only this run's alternate output directories. Run
`scripts/check-evidence-diff.ps1` over the complete Code/Review task range.
Static plan-coverage lint can bind the wrappers; it does not replace inspection
of the PowerShell witnesses or the two post-land controls.

After implementation Review/land, the CARD-1039 caller replays the retained
Windows discovery diagnostic with the landed parser (read-only parsing, not test
execution). Record parser source SHA, original assembly SHA/hash, input hash,
independent raw discovered count, exact recovered UID set, required/excluded
counts and sorted digest. For that unchanged historical input, require 14,469
parsed and zero missing/duplicate UIDs, including all 28 entries from the retained
dropped-row file. Recompute dispositions rather than guessing how the 28 divide.
Missing input is an outstanding M-1 measurement, not permission to invent totals
or execute the whole Unit suite. A later assembly needs a fresh M-1 census.
Only complete M-1 evidence permits CARD-1039 M-2 commissioning. This replay is a
dependent operational measurement, outside CP-1 and the Code budget.

### Cost

Code authoring estimate: 25-40 minutes. Ordinary checkpoint floor: 8 minutes,
including one isolated build; total Code estimate 33-48 minutes, ceiling 60.
Review uses the same narrow ordinary scope. Mutation floor: 6 minutes per PC,
12 minutes total plus 5 minutes for discovery/reporting; combined Code plus
Mutation execution floors are 8 + 12 = 20 minutes, excluding authoring/reporting
and host slot queue time. Do not expand the scope to consume the remaining budget.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1044-parser/ | portable-discovery-parser | `/*/*/NightlyDiscoveryParserTests/*` | V-1, V-2, R-1 | all three listed methods executed, 0 failed, 0 skipped | 3 | 8 | true |

## Handoff and publication

Commit this plan on the assigned task branch and push it; the canonical land
service publishes the plan to master. `AgentTaskLandService.RequestAsync` requires
the Plan task to be Succeeded, so the caller requests land immediately after this
delegate settles; a Working task cannot land itself. Never push master directly from the mirror
or rewrite the task branch. After plan publication, commission Code with the
checkpoint path/plan SHA and the 30-60 minute budget. Code returns `next: review`
with the ordinary receipt and PC-1/PC-2 still pending. This plan's next stage is
`code`; verification design is complete and no separate TestDesign dispatch is
needed.
