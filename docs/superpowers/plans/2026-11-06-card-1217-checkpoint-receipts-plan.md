# CARD-1217: checkpoint receipts with declared conditional skips

Date: 2026-11-06. Plan task `335981eb`; Code/landing owner
`36bb3715-4e5e-4038-9a81-5ec024e60a99`.
Inspected source: **5abd946dd2c235b6e873b0cbb95c738f35216dcf**.

**Outcome:** design an opt-in, source-bound exception to the zero-skip receipt
rule. A row may qualify with exactly its declared skipped identities and reasons
only when its named, current capability probe proves unavailability. An available
capability requires every declared case to pass. Unknown capability, incomplete
evidence, a different skip, or any failed test still refuses qualification.

The feature is **not already implemented**. Keep CARD-1217 open. TestDesign is a
separate next stage: it must turn the witness specifications below into exact
test bindings, fixtures and executable manifests before Code resumes. This Plan
ran no builds, tests or mutations and changes documentation only. It does not
qualify historical receipts or authorize a land, deployment or machine setting.

## Problem and ground truth

The card's Windows example is CARD-1210 S3: two storage tests correctly skip when
the executing Windows token cannot create a file symbolic link. The row's other
tests pass, but both strict validators reject the outer row solely because its
skip count is nonzero. Descriptive `Expect` prose cannot supply an exception.
Simply deleting the zero-skip check would allow unrelated failures of coverage
to be certified clean.

Inputs read in full: CARD-1217 and its two history revisions (2026-10-25 content
edit; 2026-11-06 dispatch move); the prerequisite finding at
`b0ac06112771dd9084283931481d5b37e6f88b67:docs/investigations/2026-11-06-card-1217-code-prerequisite.md`;
the CARD-1210 Plan, TestDesign and S3 D1 receipt-blocker record; both validators;
manifest, row producer, report, TRX and existing audit code. Owners:
[testing/build](../../testing-and-build.md#checkpoint-manifest-card-0585),
[checkpoint tool](../../testing-and-build.md#checkpoint-runner-tool-card-0723),
[performance](../../test-performance-standards.md),
[project conventions](../../project-context.md), and
[orchestration/D-2](../../orchestration-loop.md#d-2-advisory-policy-card-1281).

| Card assumption | Code at the inspected SHA | Consequence |
|---|---|---|
| A declared CONDITIONAL row is already machine-readable. | `CheckpointSpec` has no declaration/probe member. `PlanTableImporter` accepts only `EstimatedMinutesWindows`, `Serial`, `Environment`, `Repeat` beyond the required columns. `ExpectText` is prose. | Add an explicit field; never infer authorization from prose, a category, a count or an environment variable. |
| A green runner result is a qualified receipt. | `RowRunner` can return green with skips when its execution floor/roster is met. `ReportValidator.Validate` separately rejects non-command rows with `Skipped != 0`; PowerShell does so for both report and standalone envelopes. | Change production and validation together; keep diagnostic execution distinct from strict qualification. |
| Existing TRX parsing is strict enough for this exception. | `TrxReport.Parse` overwrites duplicate definitions, falls back to display names, tolerates missing counters and does not retain a separate result-local skip reason. | Add an opt-in strict projection for conditional rows. Preserve legacy diagnostic parsing behavior. |
| CARD-1210 S1's 68 declared skips already solve the outer-row problem. | They were inside a child TRX. `WindowsUnitPlatformAdmissionTests.AssertOutcomes` checks qualified identity, multiplicity, `DebugTrace` reason, counters and child exit. The outer audit passed with no skips. | Reuse those rules and independent vectors, not a production dependency on the test audit. Its conditional branch allows pass OR skip without an independent probe and is insufficient by itself. |
| The old CP-20 roster is still 38 results. | Current `ChannelOutboundStorageTests` has 21 methods/33 results. `WindowsUnitPlatformAdmissionTests` has six methods, including the combined `[Test, ParallelLimiter<...>]` native child method. Together: **39**, with 37 or 39 passing on Windows. | Freeze the compiled/executed roster in TestDesign; do not copy historical 38/36 floors. Full CP-20 also skips the Windows child audit on Linux and is not a portable zero-skip row. |
| Any Windows file creation error means no symlink privilege. | `FileLink.TryCreate` accepts only Windows Win32 1314 / HRESULT `0x80070522`, with exact allowed exception shapes. Access denied and other errors propagate. | The probe uses the same semantics; a generic IO or permissions failure is unknown, never unavailable. |
| The Linux 19-pass/6-skip incident establishes six missing capabilities. | Current `HerdrAlwaysOnChannelParityTests` skip strings describe pending recovery/classification/argv defect repairs (CARD-0865/CARD-0878), not an independently measured missing host capability. The historical incident proves validator refusal, not capability absence. | Preserve the incident. Do not grant those skips permission through an OS-only probe. Repair them or commission a separately justified capability contract. |
| A report hash alone prevents changing the exception. | Reports are local, unsigned evidence. Existing source hashes establish boundary consistency, not authorship of arbitrary JSON. | Obtain declaration authority from a caller-selected repository at the expected commit, independently of the report. State the existing trusted-runner boundary explicitly. |

### Already landed versus remaining

These commits are ancestors of the inspected source (source availability, not
new execution or activation claims):

| SHA | Existing contribution | Still missing here |
|---|---|---|
| `31411ea310024568d45c3130b9678fd4da63d68f` | CARD-0835 source-receipt foundation; historical commit was explicitly WIP. | No conditional authorization; its WIP label is not rewritten as a green outcome. |
| `7ff1695033afe1542e8fd6bf6add9e82563d4924` | Script source cases/receipt contracts. | PowerShell still refuses all skips. |
| `8caca15793e3b37215ccdd85359ab2977b9a39c8` | Omitted validator selection means all rows. | Selected conditional rows need equally strict qualification. |
| `9e5aa12b2471b2f5b3ea04dade1dcf91ac7c21af` | Native repeat evidence and phase timings; report schema 3. | Conditional skips are not repeat evidence. |
| `c2d0085a9ac7ae7989217a892df6b8c8f26df35e` | CARD-1210 producer/audit path repair. | Child audit remains separate from outer qualification. |
| `289b426a50a1c6d59d4a5fa12afebeffdb51f2a2` | File-link fixtures and explicit storage skip audit. | No checkpoint declaration or probe receipt. |
| `0f266e3221363185626eac74f0c7337c977b7be5` | Windows privilege-error whitelist repair. | No outer-row validator exception. |

No CARD-1217 implementation was found in the assigned source or the inspected
commit history. The prerequisite finding is on its separate owner branch; it is
evidence, not an implementation to repeat.

Live placement reads, 2026-11-06: `GET /api/runner-defaults` returned revision 2,
no kind overrides, and a default resolving to an eligible Linux runner.
`GET /api/session-runners` also showed an eligible Windows runner and a retired,
ineligible peer. These observations are transient. Refresh both routes before
dispatch. Portable work omits `-Runner` and `-Platform`; native qualification
uses `-Platform Windows` with no host pin. `-Platform Any` unpins an inherited OS.
**Nothing runs on the desktop in this commission.** Queue native work for a
separately authorized Windows qualification lane.

## Decisions

- **D-1 — Explicit, opt-in contract.** Add optional table column
  `ConditionalSkips` and YAML row property `conditionalSkips`, each a single
  contract ID. No field means today's zero-skip strict rule. Reject a blank
  supplied ID, unknown ID, command row, `Repeat > 1`, or known-flaky rerun policy
  on a conditional row. Initial scope is one capability per row. Rejected:
  `allowSkips`, wildcard identities, reason substrings and automatic inference.
- **D-2 — Reviewed source owns permission.** Store versioned declarations in
  `tests/checkpoint-conditional-skips.json`. Resolve the fixed path as a regular
  Git blob at `ExpectedSourceSha`, using the producer's trusted source root and
  the validator's caller-supplied `--source-root` / `-SourceRoot` (current repo
  root may be the default). Never take that root from evidence. No checkout,
  fetch, write or build is needed to validate. Missing commit/blob is refusal.
  This adds a Git-object dependency only for conditional qualification. Reject
  trusting the report's declaration or mutable working-tree catalog instead.
- **D-3 — Small built-in probe inventory.** Initially ship only
  `file-symlink-create-v1`. Attempt the operation; do not interpret localized
  `whoami /priv`, infer from administrator status, or accept caller-written
  commands. Success proves availability even when Developer Mode rather than a
  listed token privilege enables creation. Extending probes is a reviewed change.
- **D-4 — Three states.** `available`, `unavailable`, `unknown`. Only the exact
  Windows 1314 classification is unavailable. All launch, timeout, cancellation,
  cleanup, malformed-result and other IO failures are unknown and cannot qualify.
- **D-5 — Strict result projection only for the new exception.** Retain the
  permissive diagnostic TRX API for existing consumers; add strict identity,
  multiplicity, reason and counter validation for conditional evidence. Do not
  change existing diagnostic fallback tests to pass by weakening their assertions.
- **D-6 — Both surfaces ship together.** The tool report and standalone script
  receipt use the same acceptance truth table. Introduce report schema **4** and
  standalone envelope version **3**, only when conditional evidence exists.
  Inner `SourceEvidence.Version` stays 1. Legacy schema 2/3 and script v1/v2
  keep their existing rules. Old validators must reject the new schema.
- **D-7 — No repeat/rerun laundering.** Conditional rows have one host invocation,
  no repeats and no reruns. Refuse conditional evidence in merged-attempt reports
  initially; independently validate the original successful run. Extend repeat
  or merge support only with its own per-attempt proof design. Also refuse mixing
  repeated rows into a conditional schema-4 run; its expected repeat is 1.
- **D-8 — Preserve receipt meaning.** Keep raw executed/passed/failed/skipped
  counts, raw reason and TRX. Do not convert skipped to passed, lower the execution
  floor, erase earlier failures, or claim that conditional qualification executes
  the skipped behavior. `reviewedSourceClean` still requires all other source,
  build and review conditions. No server land-gate bypass is proposed.
- **D-9 — Refuse coherent permission edits.** Pin catalog bytes to the expected
  commit and bind the contract to its exact project/filter/total/floor. A changed
  report plus changed copied catalog cannot broaden permission. SHA-256 joins
  observations; it does not authenticate an adversary able to fabricate all
  runner output. Existing trusted executor/filesystem assumptions remain.
- **D-10 — Keep first delivery bounded.** No arbitrary argument-specific skips:
  a declared fully qualified class/method plus positive result count authorizes
  **all** instances of that identity under the bound filter. A partially skipped
  parameterized method refuses. The two CARD-1210 methods each have count 1.
  Exact per-argument selectors require a later stable identity design.
- **D-11 — Known defects stay visible.** Do not add a blanket native-Windows
  probe to bless the six Linux incident skips. The source premise is narrower
  than the incident wording. This does not block the confirmed symlink feature.
- **D-12 — Target the checkpoint component.** Keep pure judgment in the existing
  tool, which references only HostCleanup; use existing test projects/fixtures.
  Separate new pure tests from process/filesystem tests by category. A new
  production/test project is unnecessary for this additive feature. This plan
  supplies named ordinary scope, not Full Unit or scoped rc gate credit.

## Declaration and probe contract

### Declaration authority

Catalog schema 1 contains unique `id` entries with: `probe`, repository-relative
`project`, exact `filter`, `expectedTotal`, `minExecuted`, and nonempty `skips`
array of `{ className, method, count, reason }`. The class name includes namespace.
Strings compare ordinally, case-sensitively; no wildcard, trim, localization,
Unicode normalization or regex matching. Reject duplicate JSON keys, duplicate
IDs/identities, missing/unknown fields, null/wrong types, nonpositive/overflowing
counts, impossible totals, unsupported probe and version. Bounds: catalog 64 KiB,
at most 64 contracts, 64 skip identities per contract, 2,048 characters per
reason. These are input caps, not execution budgets. The source-root Git read
uses argument arrays, sanitized inherited Git routing and existing bounded byte
read/process helpers; inspect tree mode and reject symlink/submodule entries.

The row reference is preserved through Markdown import, YAML serialization,
selection, scheduler request, state and report. Conditional input requires an
explicit full expected source SHA. Before any build-slot/driver activity, compare
the row project, filter and floor with its committed declaration. Its total must
exceed the declared skip count and its execution floor must be positive. First
delivery does not qualify skip-only rows. A present `conditionalSkips` key with a
malformed value must not fall through `ManifestLoader.IgnoreUnmatchedProperties()`
as an absent declaration; validate its value before normalization. The catalog
object is strict. Unknown table columns refuse as today; unrecognized YAML
properties never authorize skips. Do not broaden this into a legacy YAML rewrite.

Initial consumer contracts (S3; TestDesign freezes source-derived rosters):

- `c1210-storage-file-links-v1`: the two methods below plus the ordinary
  `C1059_Source_reads_require_captured_roots_without_traversal` sentinel, exact
  filter `/*/Antiphon.Tests.Application/ChannelOutboundStorageTests/(C1059_Source_reads_require_captured_roots_without_traversal*)|(C1059_Source_reads_reject_file_links*)|(Output_rename_and_link_swap_after_inspection_seals_the_opened_file*)`,
  total 3, minimum executed 1.
- `c1210-storage-audit-v1`: the actual Windows CP-20 class selection
  `/*/*/(ChannelOutboundStorageTests*)|(WindowsUnitPlatformAdmissionTests*)/*`,
  currently total 39 and minimum executed 37. Freeze again against the Code
  source, including the native child test, before adoption. Linux cannot use
  this entry because its additional Windows-child skip is undeclared.

Both authorize precisely these identities, count 1 each:

1. `Antiphon.Tests.Application.ChannelOutboundStorageTests.C1059_Source_reads_reject_file_links`
2. `Antiphon.Tests.Application.ChannelOutboundStorageTests.Output_rename_and_link_swap_after_inspection_seals_the_opened_file`

Both require the literal reason:

```text
CARD-1210: file-link controls require symbolic-link creation privilege on Windows; capability unavailable.
```

Proposed YAML row extension is `conditionalSkips: c1210-storage-file-links-v1`;
the table cell is that same ID. No declaration is authorized merely by including
this example in prose. S3 commits the catalog and the new consumer acceptance
plan together. Retain old plans/receipts as history.

### Capability production and binding

Run the named probe once immediately before each conditional row's test-host
launch, after source/build admission. A reused build still needs a fresh probe.
Use an owned bounded child of the executor under the same OS token, working
directory and effective row environment as the test host, including its temp
root. Do not elevate, install software, enable Developer Mode, switch users or
contact a runner/server endpoint. The tool uses an internal fixed probe entry;
the script uses a fixed source-owned helper with the same observation schema.
No public fixture switch can force `unavailable` in a qualifying run. A replaced
dotnet driver/shim is diagnostic and cannot produce conditional clean evidence.

`file-symlink-create-v1` creates an exclusive private directory in the effective
temp filesystem used by the selected storage tests, writes a sentinel target,
and calls `File.CreateSymbolicLink`. On success verify it is the expected link
and resolves to the owned target. On Windows only, the existing FileLink
exception shapes with Win32 1314 / HRESULT `0x80070522` yield unavailable. Access
denied, path missing, collisions, sharing errors, unsupported filesystem, Linux
1314-shaped errors and unexpected results yield unknown. Confirm cleanup of
only owned probe entries in every outcome; failed cleanup changes the verdict
to unknown and preserves its diagnostic record. No recursive sweep of temp.

Probe observations include schema/probe version, status, classified operation
and native error, OS, observed token/process identity sufficient to join the
owned launch, temp-root context, start/end UTC, elapsed duration, child exit and
cleanup outcome. Do not serialize environment, token privileges or arbitrary
stderr. Unknown failures use stable codes and bounded diagnostics.

The producer adds source SHA/fingerprint and build binding, run ID, row ID,
attempt=1, random row nonce, project/filter, catalog raw SHA-256 and resolved
contract, plus the fresh TRX SHA-256 after execution. Carry those fields through
`RowRunResult`, state and `ReportRow` / standalone v3. Record one immutable probe
receipt in that row's owned evidence directory and its digest in the envelope.
Both validators reread the bounded probe receipt and TRX, compare hashes and
binding fields, and independently recompute the expected status from the recorded
raw classification. They do **not** rerun the probe on the validation host.
Probe time must fit the row execution interval; missing/foreign/stale observations
refuse. Nonces/digests detect inconsistent/replayed pieces, not malicious
whole-receipt fabrication inside the trusted-runner boundary.

### Qualification truth table

Let D be the declared multiset of qualified method identities, each repeated
`count` times. Let S be the actual `NotExecuted` multiset from the strict TRX.

| Declaration/probe | Required outcome | Strict verdict |
|---|---|---|
| No declaration; no skips | Existing complete clean source/build and passing row | Existing success |
| No declaration; any skip | No exception exists | Refuse |
| Valid declaration; available | S empty; all D instances present and Passed; every other result Passed | Qualify if all remaining gates pass |
| Valid declaration; unavailable | S equals D, including multiplicity; each result reason exact; every other result Passed | Qualify if all remaining gates pass |
| Unavailable but some/all declared cases pass or disappear | Missing declared skips | Refuse, even if zero skips |
| Available with a declared skip | Probe and outcomes disagree | Refuse |
| Unknown, absent, stale, foreign or malformed probe | Capability not established | Refuse, even if all tests pass |
| Any other skip/reason/outcome/counter/identity discrepancy | Incomplete or contradictory proof | Refuse |

The strict TRX reader requires the expected TestRun namespace/root, one result
summary/counters node, unique test definitions and execution IDs, exactly one
qualified TestMethod for every referenced result, and no display-name fallback.
Require positive total and exactly the declaration's `expectedTotal`; integer
counters must equal observed outcomes, `executed=passed`, `failed=0`,
`total=executed+skipped`, and `notExecuted=skipped`. Only `Passed` and
`NotExecuted` are legal successes; unknown/failed/error/aborted/timeout outcomes
refuse. A skipped result has exactly one `Output/DebugTrace` equal to
`Skipped: ` plus the literal declared reason. Preserve raw text; missing or
multiple traces refuse. A parameterized identity must appear exactly its count
and all instances take the same required outcome.

Require positive executed count, unchanged declared minimum/Expect membership,
native child exit zero and all existing source/build/cleanup checks. Compare
**all four** receipt-line counts to JSON and TRX for new conditional rows; compare
receipt name/commit/source fields and reject duplicate tokens/keys. Source
qualification is conjunctive with the new result judgment, never an early success
path. Schema-4 ordinary rows retain zero-skip rules. Standalone v3 uses the same
contract; conditional members under legacy versions are rejected, not ignored.
Report-merging cannot copy a favorable probe onto another attempt. Validation
is read-only: preserve invalid report/TRX/probe bytes and historical failures.

## Implementation slices and files

Commit/push failing witnesses before fixes. Run the closed selection for each
committed slice; a compile/fixture error is not assertion-red evidence. Code
does no deliberate positive-control mutations. Ordinary Review follows Code;
SourceLanding Mutation follows confirmed publication under the original owner.

| Slice | Production/docs files | Tests and exit condition |
|---|---|---|
| **S1: declaration and strict projection** | `tools/Antiphon.Checkpoints/Manifest/{CheckpointManifest,ManifestLoader,ManifestValidator,PlanTableImporter}.cs`; new `ConditionalSkips/ConditionalSkipContract.cs` and `ConditionalSkipPolicyReader.cs`; `Trx/TrxReport.cs` and new strict conditional projection. | New `C1217ConditionalSkipContractTests` (5 methods); existing manifest/import/TRX/repeat classes. No production acceptance enabled yet. Pure tests can supply already-resolved contracts; Git policy binding is covered in S2. |
| **S2: atomic producer/validator feature** | New `ConditionalSkips/CapabilityProbe.cs`, `ConditionalSkipEvidence.cs`, `ConditionalSkipValidator.cs`; `Execution/{RowRunner,RunScheduler}.cs`, `State/RunState.cs`, `CheckpointApp.cs`, `Program.cs`, `Report/{ReportModel,ReportWriter,ReportValidator,ReportMerger,CheckpointLine}.cs`; `scripts/run-checkpoint.ps1`, `scripts/validate-checkpoint-receipt.ps1`, new `scripts/lib/checkpoint-conditional-skips.ps1` and fixed probe helper. | New validation (8), probe (3), producer (3), parity (4) and host (2 portable + 2 native) methods below; full affected legacy producer/report/source-script classes. Both validators support both conditional envelopes, with the tool validator adding dispatch for standalone v3; legacy standalone handling remains PowerShell-owned. No slice may enable only one validator. |
| **S3: consumer declarations and owner documentation** | New `tests/checkpoint-conditional-skips.json`; new `docs/superpowers/plans/2026-11-06-card-1210-conditional-receipt-acceptance.md`; `docs/testing-and-build.md` manifest/source-qualification/tool sections. Update classification/fixture staging only as actually needed. | Freeze the current 3-result focused and 39-result full Windows consumer selections, apply the new field, document both CLI validator invocations and limitations; portable guards/policy rows. Original CARD-1210 Windows acceptance remains queued until native receipts exist. |

Test source root: `tests/Antiphon.Tests/Checkpoints/`, except existing script,
application, infrastructure and shared category guards. Use a new helper
`C1217ConditionalSkipFixture.cs` for hand-written payload vectors, committed
miniature Git repositories and owned children. If a real child test assembly is
needed, extend `tests/Antiphon.Checkpoints.FilterHost/` with three isolated
conditional fixture cases and stage it through its existing project reference;
do not create a new project or execute a nested build from a test.

Keep the permission catalog separate from `tests/test-execution-policy.json`:
one controls receipt exceptions, the other the execution universe. Update the
checkpoint temp/census fixture inventory if new tests require it; reuse owned
`CheckpointTestBase` scratch and process registration. Do not broaden existing
cleanup authority or change HostCleanup production code.

If `tests/test-execution-policy.json` changes for classifications, native tests
or fixture membership, **recompute `policyHash` and refresh
`scripts/test-live-lane-run.ps1`'s `PinnedInputDigest` in the same slice/commit**.
Use the canonical policy hashing implementation, then
`pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` for the
newline-normalized five-input digest. This is a read-only digest command, not a
test run. Do not copy the old digest or remove its real-declaration assertion.
Include the exact live-lane contract row below. If policy does not change, say so;
do not manufacture policy churn to satisfy this paragraph.

No product session is added or changed and no session begins waiting for input;
the CARD-1083 parking/release checklist is therefore not applicable.

## Verification requirements for TestDesign

This is the input to a separate `## Verification design` artifact, not a claim
that tests exist. Proposed method names below are reserved and must survive into
that artifact or receive an explicit mapping. All new methods are one TUnit
result each; vectors inside a method are assertion cases, not execution counts.
Use independent literal expectations, not serialization of the production
answer as the expected answer. Every negative vector starts from a qualifying
control and changes one fact while keeping unrelated bindings consistent.

### V requirements

| ID | Proposed method(s), class prefix `C1217ConditionalSkip` | Required observation |
|---|---|---|
| V-1 | `ContractTests.DeclarationRoundTrips` | Exact ID survives table, YAML and selected row; same contract resolved. Empty/default remains legacy. |
| V-2 | `ContractTests.MalformedDeclarationsRefuse` | Duplicate IDs/properties/identities, wrong types, empty reason, wildcard, zero/overflow count, impossible totals, unknown probe/version/property and wrong binding refuse before slots/drivers. |
| V-3 | `ContractTests.StrictTrxPreservesIdentityAndReason` | Qualified identity, count and exact result-local reason retained; no display-name or class-prefix shortcut. |
| V-4 | `ContractTests.LegacyParsingIsUnchanged`, `UnsupportedCombinationsRefuse` | Legacy parser defaults remain; command/skip-only/repeat/rerun and unsupported merged attempts cannot opt in. |
| V-5 | `ValidationTests.UnavailableExactRosterQualifies`, `AvailableRequiresAllDeclaredPassed` | Both successful truth-table arms plus available+skip refusal. Counts/coverage remain literal; positive cases are red on the current blanket skip rejection where applicable. |
| V-6 | `ValidationTests.UnavailableRequiresEveryDeclaredSkip` | Missing one/all skips, partial parameter outcomes, duplicate and swapped multiplicity refuse. |
| V-7 | `ValidationTests.ForeignSkipAndReasonRefuse` | Extra/wrong-class/wrong-method/case-changed skip, one-character reason drift, whitespace and absent/duplicate reason refuse. |
| V-8 | `ValidationTests.MalformedTrxCannotQualify` | Duplicate/missing definition, execution ID, TestMethod or counters, bad root/namespace, display-name substitution, inconsistent totals and non-success outcomes refuse. |
| V-9 | `ValidationTests.ProbeBindingDisagreementRefuses` | Each source/run/row/attempt/nonce/filter/policy/probe/TRX binding tamper; missing/unreadable/stale evidence and unknown status refuse. Recomputed report-local hashes cannot change source-pinned permission. |
| V-10 | `ValidationTests.SourceAndFailureFencesRemain`, `LegacyAndUnsupportedShapesRemainClosed` | Dirty/changed/unknown source, foreign SHA, build mismatch, cleanup refusal, nonzero child exit, failed ordinary result, zero executions and duplicate line tokens cannot become qualified through the exception. Legacy schema with conditional fields refuses. |
| V-11 | `ProbeTests.OnlyWindows1314IsUnavailable` | Literal classifier vectors exercise both OS arms and both implementations: three permitted exception shapes; access denied/IO/path/collision/unknown code never unavailable. |
| V-12 | `ProbeTests.ProductionProbeObservesAndCleansOwnedFiles`, `ProbeTimeoutOrCleanupFailureIsUnknown` | Real production probe factory on the executing OS, correct effective temp root, owned target/link sentinel and complete cleanup; bounded failure/timeout leaves unknown, no host launch eligible. No surrogate OS flag earns native proof. |
| V-13 | `ProducerTests.RowPersistsBoundProbeAndTrx`, `ResumeAndReportKeepEvidence`, `RowRefusesMismatchedOrUnknownProof` | Actual scheduler-to-row-to-state/report wiring, fresh probe on no-build reuse, no stale evidence after restart; row exit/state reflects invalid proof. Assert parsed stored bytes and untouched invalid artifacts, not just an in-memory DTO. |
| V-14 | `ParityTests.BothValidatorsAgreeOnTruthTable`, `BothValidatorsRejectTamperedEvidence` | Run C# entry and real PowerShell CLI against the same table-driven report and standalone v3 corpus; assert exit 0 only for two valid arms and exit 2 for every one-flip, plus no VALID output on refusal. Reopen artifacts to prove validation did not rewrite them. |
| V-15 | `ParityTests.BothValidatorsUseCommittedPolicy`, `StandaloneProducerBindsProof` | Two real fixture commits with different catalogs; a wrong source root/commit, modified working catalog or substituted report catalog cannot authorize more skips. Exercise real standalone producer with owned fake driver where needed, explicitly diagnostic, and native producer without a shim in host tests. |
| V-16 | `HostTests.PortableHostProducesQualifyingReceipt`, `UnavailableFixtureCannotMasqueradeAsNativeProof` | Real staged child, fresh TRX and both validators; available native operation on Linux/Windows as observed. A synthetic unavailable record can test judgment only and is not counted as a native unavailable producer result. No nested build. |
| V-17 | `HostTests.NativeWindowsAvailableReceiptQualifies`, `NativeWindowsUnavailableReceiptQualifies` | Separate Windows placements/tokens, genuine production probe and actual file-link test skips/passes, positive ordinary sentinel, both validators and source/build binding. Wrong environment fails prerequisite; do not Skip/pass these acceptance methods. |

### R requirements

| ID | Preservation obligation | Checkpoint scope |
|---|---|---|
| R-1 | Existing manifest/import/filter/legacy TRX/repeat configuration behavior | CP-2, CP-3 |
| R-2 | Scheduling, one build lane, ordinary row results/report writing/merging | CP-6 |
| R-3 | All existing strict source, clean build, standalone source and repeat receipt fences | CP-7, CP-8 |
| R-4 | Tool dependency direction, classification, layering, platform and mechanical contract | CP-10 |
| R-5 | Policy/native execution and live-lane real-input digest stay consistent | CP-11 |
| R-6 | FileLink error whitelist and original child audit rules remain independent | CP-12; actual consumer native acceptance as below |

Manual acceptance is a bounded receipt exercise, not a product UI/deploy task.
From a freshly committed S3 consumer plan, run the focused three-result row on
Linux and both Windows capability states, then the full Windows CP-20 selection
on the same qualified source. Preserve raw counts: focused 3/3/0/0 when available,
1/1/0/2 when unavailable; full current roster 39/39/0/0 or 37/37/0/2. Run both
validators with explicit expected SHA and trusted source root; retain exit codes.
TestDesign embeds the focused cases in the two native host witnesses and freezes
the full CP-20 consumer manifest as the separate CARD-1210 follow-up. Do not run
that full consumer selection covertly as an unlisted CARD-1217 row. No old
19/19/0/6 or 36/36/0/2 receipt is retroactively blessed.

### PC variants: preservation oracle and pending execution

For this card, **PRESERVATION** means invalid evidence never acquires a clean
qualification: validator exit 2/non-null refusal, no VALID certificate, unchanged
input evidence; producer refuses/retains diagnostic evidence and never reports a
qualified conditional row. Reason-text-only changes are not kills. Positive
success controls prevent an always-reject implementation passing the suite.
Each variant runs the exact named method only. `.C` and `.P` below mean separate
mutations to the tool and PowerShell predicate; never mutate both to obtain one
kill. Keep all unrelated guards valid so the nominated change reaches the oracle.

| PC | Variants | Single mutant/site | Exact method that must go red on preservation |
|---|---:|---|---|
| PC-1 | 1 | Importer silently discards a supplied malformed conditional field instead of refusing it | `ContractTests.MalformedDeclarationsRefuse` |
| PC-2 | 1 | Contract validator accepts an unknown probe ID, with the other fields valid | `ContractTests.MalformedDeclarationsRefuse` |
| PC-3 | 2 C/P | Resolve authority from the modified working catalog instead of the expected commit | `ParityTests.BothValidatorsUseCommittedPolicy` |
| PC-4 | 2 C/P | Remove exact project/filter/floor binding comparison (one combined row-binding guard) | `ParityTests.BothValidatorsRejectTamperedEvidence` |
| PC-5 | 2 C/P | Strict identity resolver falls back to display name when TestMethod is absent | `ParityTests.BothValidatorsRejectTamperedEvidence` |
| PC-6 | 2 C/P | Skip strict TRX counter-to-observation equality | `ParityTests.BothValidatorsRejectTamperedEvidence` |
| PC-7 | 2 C/P | Accept reason substring instead of exact result-local reason | `ParityTests.BothValidatorsRejectTamperedEvidence` |
| PC-8 | 2 C/P | Compare identity sets with Distinct rather than multisets | `ParityTests.BothValidatorsRejectTamperedEvidence` |
| PC-9 | 2 C/P | Available capability also permits the declared skipped multiset | `ParityTests.BothValidatorsAgreeOnTruthTable` |
| PC-10 | 2 C/P | Unavailable capability accepts a proper subset of declared skips | `ParityTests.BothValidatorsAgreeOnTruthTable` |
| PC-11 | 2 C/P | Treat unknown capability as unavailable | `ParityTests.BothValidatorsAgreeOnTruthTable` |
| PC-12 | 2 C/P | Omit row nonce comparison, preserving all other joins | `ParityTests.BothValidatorsRejectTamperedEvidence` |
| PC-13 | 2 C/P | Strict result reader ignores an unknown ordinary outcome; keep summary counts self-consistent with the mutant's interpretation | `ParityTests.BothValidatorsRejectTamperedEvidence` |
| PC-14 | 2 C/P | Accept conditional members under a legacy schema without requiring the new envelope | `ParityTests.BothValidatorsRejectTamperedEvidence` |
| PC-15 | 4 | C/P independently: broaden Windows whitelist to access denied; separately drop Windows guard for a 1314-shaped error | `ProbeTests.OnlyWindows1314IsUnavailable` |
| PC-16 | 2 | Independently retain unavailable after probe timeout, then after unconfirmed cleanup | `ProbeTests.ProbeTimeoutOrCleanupFailureIsUnknown` |
| PC-17 | 1 | Scheduler reuses previous row's probe instead of acquiring a fresh observation | `ProducerTests.ResumeAndReportKeepEvidence` |
| PC-18 | 1 | Standalone v3 path omits the conditional judgment and grants success on a probe/outcome mismatch | `ParityTests.StandaloneProducerBindsProof` |

Inventory: **18 groups / 34 variants / at least 102 baseline-red-restored method
observations**. TestDesign must bind the final exact guards and demonstrate that
PC-4/13 are not masked by another predicate; split a combined guard if the final
implementation has independent guards, preserving the expanded count/cost.
Every variant is pending. SourceLanding uses its bound landed source and external
evidence root; no commit/push from that snapshot. Do not count source inspection,
build failures, empty selections or fixture errors as red. No PC pack or
cross-process lease exemption is commissioned by this Plan.

## Performance budget

Applies [Performance standards](../../testing-and-build.md#performance-standards),
R1-R9. **This documentation-only Plan ran no builds/tests.** The optional one
validating build was unnecessary. No bin/obj/TRX outputs were created.

Measurement references, not new CARD-1217 measurements:

- Debug `5ae98d31`, report at
  `c318d8edfc97e046bb9a332ec34c76973f9a4f4a:docs/investigations/2026-11-05-debug-s5b-1-performance.md`,
  Linux 2026-11-05, subject `0626344fbf1739809a4b079237b40c4f6b3028fa` and its
  enumerated source SHAs: **11 successful cold Antiphon.Tests builds**, p50
  431.6s, empirical p95/max **817.1s**; **39 admissions**, p95 **255s**, max 405s.
  Load varied up to the 30s on a 24-core host. Short row hosts had maxima 42-85s.
  These are relevant build/admission comparators, not p95s of our new methods.
- [CARD-0931 plan](2026-10-23-card-0931-or-filter-validation-plan.md#performance-budget)
  records 203.3s build/settlement and 4.55s test host for its first red, then a
  9m18s contended build. Small samples; no stable p95.
- New conditional methods, probe subprocesses, parity batches and Windows rows
  have **no measured p95**. Budgets below are provisional under CARD-1217 and
  CARD-1404. Collect durations/phases during their first required red/green and
  native acceptance runs; refine in TestDesign/Code before repeated execution.
  Do not add measurement-only repeats or call a single sample a measured p95.

| Phase/selection | Expected duration | Bound and basis |
|---|---|---|
| Build-slot admission, each acquisition | 0-5 min normally | **10 min**, approximately 2x measured 255s rounded up; exit 4 is not run, never an unleased retry. |
| One cold Antiphon.Tests isolated build | 4-14 min, load-dependent | **28 min execution**, rounded 2x 817.1s. One build at a time/worktree. |
| Pure contract/judgment method | <0.5s; matrix <2s | Initial **5s body**, provisional; no subprocess per pure vector. |
| Native probe child | Usually <1s operation plus startup | **5s execution + 5s joined cleanup** initially, provisional. Parent row owns child and deadline; timeout means unknown. |
| Git policy read | <1s | **5s** and 64 KiB cap, provisional; sanitized argument-based local Git only. |
| Real PowerShell parity method | 5-30s expected using one owned pwsh host for its vector batch | **60s method** initially, 2x estimated 30s. CLI boundary cases may start a small bounded number of children; no pwsh launch per pure vector. |
| Stored-evidence IO | Tiny files, normally <0.1s | Existing bounded regular-file readers; new small input caps above. Preserve inherited stricter read ceilings. |
| Real child-host method | 10-60s expected including startup | **120s method**, provisional; staged outputs only, no nested build. Native storage/audit costs are separately measured. |
| Host startup / teardown | Typically 10-25s / 5-15s; loaded comparator startup up to 67s | Included in row caps. Record separately; owned child exit/output drain each initially 10s with joined cleanup. |
| Container/readiness, DB migration/seed/clone, AppHost/web host | Not required by these new witnesses | No such infrastructure starts for this scope. Unexpected activity is investigated; unmeasured phases remain unknown, not invented zero. |
| Pure/short ordinary row | 0.5-2 min execution | **4 min** initially; approximately 2x comparable loaded host plus expected body. |
| Parity/source/script row | 1-3 min execution | **6 min** initially; provisional exact-roster measurement required. |
| Guards/policy row | 1-3 min execution | **6 min** initially; measure source scans and external CLI separately. |
| Native host acceptance row | 1-3 min execution | **6 min** initially, provisional; no Windows p95 available. Prerequisite absence fails the proof. |

Fixture lifetime: no AppHost, broker or DB is needed. Immutable vector corpus and
the staged child/tool image are shared once per assembly/run. Each mutation of
receipt/TRX data gets a private copy; each Git-binding method owns a small repo
with two commits, shared across its internal vectors. Each real probe must use a
fresh owned scratch directory because current capability/cleanup is its subject;
record that intentional per-row setup. Do not create a fresh build, container or
seed per vector. Process tests carry assembly-local `ParallelLimiter<ProcessSpawnLimit>`
and use owned child helpers; native host acceptance runs serially. Do not
co-schedule Antiphon.Agents.Pty.Tests/FakeClaude.

Dependency/category inventory: `tools/Antiphon.Checkpoints` ->
`src/Antiphon.HostCleanup`; `Antiphon.Tests` -> the tool and existing staged
FilterHost, plus its pre-existing server/runner/messaging references. No changed
production downstream server consumer is linked to the tool; server review
settlement consumes a reviewed assertion, not this assembly. New pure contracts
are **Unit / checkpoint tooling**; local Git/IO/PowerShell/real child witnesses
are **Integration / checkpoint tooling**, with native methods explicitly
classified. TestDesign captures the actual direct ProjectReference graph and
fixture input closure at its source. Shared source or unknown ownership expands
the selection through the ordinary policy; these rows confer no CARD-1545 rc
scope credit. Project extraction was considered and rejected because the tool
already has a narrow dependency boundary and extraction would add unrelated work.

Record ordinary evidence in `.antiphon/checkpoints/<run-id>/`, including raw
TRX, probe receipt, manifest/catalog digest, report and unedited CHECKPOINT lines.
Join source/build SHA, exact Class.Method/arguments, run/row, OS/load/category and
probe nonce. Record admission, build, discovery/startup, preparation, case body,
teardown and bounded filesystem/Git/pwsh calls via CARD-1514 timing; record
nonexistent phases as not applicable and absent measurements as unknown.
Wall time, nested spans and unattributed remainder are distinct.

Normal three-row packets should take **12-24 min** including admission; a loaded
packet can exceed 30 minutes as the cited Debug demonstrated. At most three rows
per run, and use fewer when measured queue/load predicts an overrun. Any expected
or actual >30-minute selection requires a caller-commissioned bounded performance
Debug with exact membership/SHA/timings; the comparator is not a blanket waiver.
Do not repeat the same expensive packet to diagnose it. Report slot wait and
execution separately; no relaxed timeouts, assertions or retries to hide red.

### Checkpoints

This closed list is the TestDesign input. It intentionally uses the **current**
table schema so the tests-first S1 run does not need the feature being authored.
The new conditional-field consumer table belongs to the separate S3 acceptance
artifact. New classes/methods named here do not exist yet. `Group` names the
lane; **N1/N2 are queued Windows work, not permission for a desktop run**.

Each packet owns one isolated build and one exact filter per row; later rows in
that packet reuse its source-bound build. Select only its listed three or fewer
rows. S2 has two packets (CP-4..6, CP-7..9); never run all six with `--after S2`.
Native rows each own a build on their separately qualified Windows capability
placements; they are separate one-row runs. No row is omitted to save time.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1217-s1/ | portable-conditional-contract | /*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipContractTests/* | V-1..4 | 5 proposed methods/results, 0 failed/skipped | 5 | 14 | true |
| CP-2 | S1 | CP-1 | portable-parser-regression | /*/Antiphon.Tests.Checkpoints/(CheckpointManifestTests*)\|(CheckpointImportTests*)\|(TrxReportTests*)/* | R-1 | 48 current methods/results, 0 failed/skipped | 48 | 2 | true |
| CP-3 | S1 | CP-1 | portable-repeat-configuration | /*/Antiphon.Tests.Checkpoints/CheckpointRepeatConfigurationTests/* | R-1 | 5 methods/results, 0 failed/skipped | 5 | 2 | true |
| CP-4 | S2 | tests/Antiphon.Tests -> bin-c1217-s2a/ | portable-probe-producer-validation | /*/Antiphon.Tests.Checkpoints/(C1217ConditionalSkipProbeTests*)\|(C1217ConditionalSkipProducerTests*)\|(C1217ConditionalSkipValidationTests*)/* | V-5..13 | 14 proposed methods/results, 0 failed/skipped | 14 | 15 | true |
| CP-5 | S2 | CP-4 | portable-validator-parity | /*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipParityTests/* | V-14,15 | 4 proposed methods/results, 0 failed/skipped; every internal vector accounted | 4 | 3 | true |
| CP-6 | S2 | CP-4 | portable-scheduler-report-regression | /*/Antiphon.Tests.Checkpoints/(RowRunnerTests*)\|(RunSchedulerTests*)\|(ReportWriterTests*)\|(ReportMergerTests*)/* | R-2 | 39 current methods/results, 0 failed/skipped | 39 | 3 | true |
| CP-7 | S2 | tests/Antiphon.Tests -> bin-c1217-s2b/ | portable-source-regression | /*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests/* | R-3 | 11 current methods/results, 0 failed/skipped | 11 | 15 | true |
| CP-8 | S2 | CP-7 | portable-script-source-repeat | /*/Antiphon.Tests.Scripts/(RunCheckpointSourceScriptTests*)\|(RunCheckpointRepeatScriptTests*)/* | R-3 | 10 current methods/results, 0 failed/skipped | 10 | 3 | true |
| CP-9 | S2 | CP-7 | portable-real-host | /*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipHostTests/(PortableHostProducesQualifyingReceipt*)\|(UnavailableFixtureCannotMasqueradeAsNativeProof*) | V-16 | 2 proposed methods/results, 0 failed/skipped | 2 | 3 | true |
| CP-10 | S3 | tests/Antiphon.Tests -> bin-c1217-s3/ | portable-classification-and-contracts | /*/*/(TestLaneCategoryGuardTests*)\|(TestClassificationGuardTests*)\|(CheckpointToolProjectContractTests*)\|(C1729LayeringGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)/* | R-4 | all named classes/arguments, 0 failed/skipped; current source floor 35 | 35 | 15 | true |
| CP-11 | S3 | CP-10 | portable-policy-and-live-lane-pin | /*/Antiphon.Tests.Scripts/(NativeLinuxPolicyExecutionTests*)\|(LiveLaneRunnerTests*)/* | R-5 | 5 current methods/results, 0 failed/skipped; includes real input digest contract | 5 | 3 | true |
| CP-12 | S3 | CP-10 | portable-filelink-and-child-audit | /*/*/*/(C1059_Source_reads_reject_directory_links*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*) | R-6 | both original methods, 0 failed/skipped | 2 | 2 | true |
| CP-13 | N1 | tests/Antiphon.Tests -> bin-c1217-n1/ | windows-capability-available | /*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipHostTests/NativeWindowsAvailableReceiptQualifies | V-17 | 1 method; real available prerequisite, actual 3/3/0/0 child receipt | 1 | 16 | true |
| CP-14 | N2 | tests/Antiphon.Tests -> bin-c1217-n2/ | windows-capability-unavailable | /*/Antiphon.Tests.Checkpoints/C1217ConditionalSkipHostTests/NativeWindowsUnavailableReceiptQualifies | V-17 | 1 method; real 1314 prerequisite, actual 1/1/0/2 child receipt | 1 | 16 | true |

Counts above are source inventories/planned results, not executions. TestDesign
must confirm all counts, native method classification and child inventories
from compiled metadata; no count of internal assertions is a `Min`. CP-12 selects
the two original methods across their namespaces; both method prefixes are
unique across the inspected test source. It does not invoke copied test logic.

At this source every existing class operand above has one exact class match and
no sibling with that class name as prefix; proposed `C1217ConditionalSkip*` names
are absent. Every OR operand has parentheses and a trailing `*`; Markdown pipes
are escaped. TestDesign repeats the suffix-collision audit after authoring and
checks the exact executed Class.Method/argument multiset in fresh TRX. Native
exact methods do not require a wildcard. Do not use list-tests as execution proof.

Manifest budgets to freeze: `build.slotWaitMinutes: 10`,
`timeouts.rowMinutes: 28` for the build deadline; per-row `timeoutMinutes: 4`
for CP-1..3, 6, `6` for CP-4,5,7..14. Avoid CLI `--row-timeout`, which also
replaces the build deadline. Charged caps are S1 **42 min**, S2a **46 min**,
S2b **48 min**, S3 **48 min**, and each one-row native run **36 min**, including
2 min allowance for source capture/settlement. Independent hard-wall caps add
10 min per possible acquisition (one build plus selected rows): 82/86/88/88
and 56 min respectively. These are ceilings, not expected runtime or permission
to ignore R1. Use the documented `totalTimeoutOverrideReason` where a chosen
execution cap is below `DeriveTotalMinutes`; name this measured/provisional
phase budget. The importer 15-minute row floor and 3x estimates are not R5
compliance. TestDesign writes per-packet resolved manifests and their import
checks so Code does not improvise timeout edits. Current `run --plan` derives
row timeouts and cannot express these per-row overrides. Therefore TestDesign
uses the tool's `import --plan <artifact> --out <manifest.yaml>`, retains the
same table selection/build identities and supplies only the specified budget
fields in reviewed per-packet YAML. Check the table-to-manifest selection for
equality; generated receipts and executor outputs remain ignored.

Execute the closed plan selection through the checkpoint tool using its
supported positional manifest form, `run <budgeted-manifest.yaml> --rows
<packet> --expected-source-sha <committed SHA>`, then `wait` until not exit 75.
This explicit budget refinement avoids silently using the noncompliant defaults
of direct `run --plan`; it does not authorize different tests or extra builds.
Tool bootstrap, if required, is one separately reported slot-gated native build;
direct build/run uses native default outputs and matching `UseAppHost=false`.
Checkpoint cold outputs remain the supported tool-owned exception. No hidden
builds/test runs, whole namespace or whole Unit run is commissioned here. A future
brief requiring Full must add the owned Full selection and its performance Debug;
these scoped receipts must not be labeled Full. Code/Review run
`scripts/check-evidence-diff.ps1` over assigned base..HEAD.

### Cost and unexecuted obligations

Table floor: portable ordinary **80 min** across four packets (18 + 21 + 21 + 20),
before unusually contended admission, assertion-red authoring runs and repairs.
Native **32 min table floor** includes a build on each of two placements, plus
admission. Ordinary Review has the same named scope under its verification
profile. Authoring S1/S2/S3
is provisionally 1-2h / 3-5h / 0.5-1h; Code total authoring plus normal ordinary
execution is approximately **6-10h**, spread by slice, not a 90-minute promise.
TestDesign must price its final fixture/corpus once, retaining changed totals.

Post-land SourceLanding has **34 variants / 102 method phase observations**.
Without approved build reuse, 4-14min build plus 0.5-1.5min host per phase is
roughly **8-27h execution**, plus admission and analysis; a bound pack can reduce
that only under its explicitly approved execution contract. Record author and
Mutation floors separately and their combined **14-37h** provisional envelope,
not counting native scheduling delay or failures. TestDesign should reduce
duplicated setup and refine these costs without dropping guards/variants. No
Mutation work is authorized during this Plan or ordinary Code.

## Open operator questions (none block Plan or portable TestDesign)

- **O-1:** schedule genuine Windows available and 1314-unavailable tokens for
  N1/N2 and the later full CARD-1210 CP-20 acceptance. Do not change host privileges
  or use the desktop under this commission. If only one state is available,
  retain the other as incomplete native acceptance.
- **O-2:** route the Linux incident's known-defect skips to their existing repair
  owners, or supply a specific measurable absent capability and its intended
  coverage boundary for a separate contract. This plan does not reinterpret
  pending bug repairs as capability absence.
- **O-3:** if repeat, skip-only, per-argument conditional or merged receipts are
  needed, commission that extension explicitly. The first implementation refuses
  them and is complete within D-1/D-7/D-10.

Next: **test-design** for S1/S2, then S3 consumer adoption; bind all V/R and 34 PC
variants, correct/compile roster floors, freeze the catalog/probe wire schema,
method-scoped mutant cuts, real child launch/custody, parity corpus and timed
packet manifests. Keep Windows acceptance pending and preserve Code owner
`36bb3715-4e5e-4038-9a81-5ec024e60a99`. No decision or permission is needed to
produce that artifact under the decisions above.
