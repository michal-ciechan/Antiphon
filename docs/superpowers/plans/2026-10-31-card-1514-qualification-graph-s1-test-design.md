# CARD-1514 qualification graph S1: schema and identity test design

Scope: **S1 SchemaAndIdentity only**, based on
`5553204288da11e0deb910def325b8632ac737a2`. This companion appends verification
detail to the [landed graph plan](2026-10-31-card-1514-qualification-run-graph-plan.md);
it does not rewrite the fix design or commission S2 or later. Its CP-1 refines
the parent's CP-1; the other seventeen parent checkpoint rows remain intact.
Review 33224c14 permits S1, with a separately pinned catalog. Review 1a172569's
Q1-Q8 remain **PROVISIONAL**; none is answered by these fixtures. The parallel
S9 amendment for R2-D1 is outside this document. S1 supplies only its prerequisite:
node/UID identity survives target rebinding while graph identity changes.

This is a documentation deliverable. The original design below is amended against
the master-based S1 fix `e9255efb92992ccdddab44c1396b7ddf8d9ac8ed` and Final Review
09aa4107. The fix worker's available local report
`/work/worktrees/task-55cb33ee/.antiphon/task-55cb33ee.md`, section "Required
companion amendment", supplies the amendment; the harness, wrapper and library
were checked against it. Review 4ba043c8's D-1..5 explain the corrected seams.
Historical test-first steps and estimates remain design history. No execution or
PC credit is created by this amendment; all SourceLanding PCs remain pending.
Format follows the CARD-1545 S1b/S1c companions on
`origin/feat/card-task-6a22c243` and `origin/feat/card-task-8b284c8f`.

## Verification design

### Inspection

Bodies read before specifying the cases, at the assigned base unless stated:

- Parent run-plan schema, canonicalization, catalog/platform contract, preparation
  sentinel, S1 scope, performance and CP-1; both `.antiphon/task-33224c14.md` and
  `.antiphon/task-1a172569.md`, including D-1, D-10 and all provisional questions
  -> V-1..9, R-1..4. The re-check's S9 finding supplies identity motivation only.
- `tests/Antiphon.Tests/Scripts/ReleaseGatePolicyTests.cs`,
  `RcProjectSelectionTests.cs`, `NightlyDiscoveryParserTests.cs`,
  `scripts/test-rc-project-selection.ps1`, and `scripts/test-release-gate.ps1`
  `New-C599Policy`/`Test-C599_ProfileSchema`: nearest new-wrapper/harness fixtures,
  actual product calls, negative companions and named assertion counts -> all V/R.
- `ScriptHarness.cs`, `ScriptHarnessProcess.cs` options/launch/drain path,
  `TestHelpers/ProcessSpawnLimit.cs`, and `scripts/lib/c487-harness.ps1`
  assertion/root helpers -> owned child, exact PASS inventory, bounded cleanup.
- `docs/schemas/antiphon.areas.schema.json` (nearest schema),
  `scripts/fixtures/c599-policy-v2.json` profile/identity shape (nearest pinned
  policy fixture), `nightly-policy.ps1` canonical hash/read/profile/script census,
  `nightly-common.ps1` canonical writer, and `NightlyScriptsTests.cs` ASCII check
  -> fixture storage, V-2..8, R-1/3. The legacy writer's culture-sensitive sorting
  and scalar conversion are not assumed to meet the new canonical contract.
- `tests/test-execution-policy.json` profiles/scriptCensus,
  `nightly-tests-impl.ps1` script-disposition execution branch,
  `test-nightly-tests.ps1` G056, `Antiphon.Tests.csproj` references/staging, and
  `release-gate.ps1` Full-scope check; release-gates Windows authority section
  -> R-1..4. G056's old count is not an oracle to copy or a reason to run its suite.
- Failed-first plan on `origin/master`: decisions, history and Full conservation
  contracts; failed-first S1 `nightly-failure-history.ps1` artifact/digest/read/
  resolver bodies; S2 `nightly-dispatch.ps1` complete body and
  `RcFailureHistoryTests`/`RcFailedFirstPlannerTests` on
  `origin/feat/card-task-9058bae5` at
  `0f4d90b816da073481cf7200d29987017c2f5702` -> preserve history source and legacy
  dispatch, exclude S4/S11 integration. The local rc/2026-10-27 ref contains S1
  but lacks the S2 file; that mirror observation establishes no live activation.
- `docs/testing-and-build.md` manifest, runner, mutation and delivery sections;
  `docs/test-performance-standards.md`; orchestration stage progression and D-2
  -> checkpoint/cost rules. `PlanTableImporter.ImportMarkdown` header/build/filter/
  roster parsing was read to check the table shape. No native, service or
  live-supervisor inspection.

### Delivery inventory

**No new or changed asynchronous delivery path in S1.** The new library consumes
caller-supplied JSON and a separately supplied catalog synchronously and returns
parse/identity/hash results. It has no producer queue, recipient session, durable
outbox, recovery worker, execution receipt or notification. Graph nodes describe
future work; parsing one does not submit it. The owned PowerShell test child is
test infrastructure, not a product delivery path.

Consequently real-queue busy-recipient/already-eligible-recipient and crash/enqueue
handoff tests have no S1 boundary to exercise. S8-S13 own future dispatch/recovery
and must inventory those paths separately. If implementation introduces a queue,
transport or session input, return `next: plan` for that seam. An insert, event,
Sent flag, ack or parser result cannot replace recipient evidence; a session path
would require the matching complete UserPrompt transcript. This design claims no
delivery, recovery, host OS attestation or execution from its synthetic bindings.

### Fixtures and binding conventions

Closed TUnit roster: **one** parameterless `[Test]`,
`Antiphon.Tests.Scripts.QualificationGraphContractTests.SchemaAndIdentity`, in
`tests/Antiphon.Tests` (`net9.0`). Exact filter:
`/*/Antiphon.Tests.Scripts/QualificationGraphContractTests/SchemaAndIdentity`.
Use `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]` and
`ScriptHarness.RunHarnessCaseAsync("test-qualification-graph.ps1", "QGRAPH",
"SchemaAndIdentity", ...)`. Internal variants are not additional TUnit results.
App ownership is script/qualification orchestration. No new project/reference.

The wrapper freezes **112 named assertions**: the original 100 minus `full-policy`
and `gate-files` leaves 98 durable assertions; add five catalog UID rows, two
graph-as-catalog rows, one exact-number identity row, two pin rows and four catalog
shape rows. One TUnit result is still required; 112 is not the checkpoint Min.
R-1/R-2 instead use commission-only CP-C below, with no TUnit project/filter.

Both library and harness require **pwsh >= 7.4**, including draft-2020-12
`Test-Json` and System.Text.Json. The Windows controller launches **pwsh, not
powershell.exe**, and checks its version before commissioning. ASCII source is
still required; it does not imply Windows PowerShell 5.1 runtime compatibility.

New production files are exactly the parent's schema and
`scripts/lib/qualification-graph.ps1`. Test additions are the named wrapper,
`scripts/test-qualification-graph.ps1`, and data under
`scripts/fixtures/qualification-graph/s1/`. Only the new harness's census entry
and the generated policy hash may change in `tests/test-execution-policy.json`.
The library is dot-sourced support, not an unattended script-census executable.
Record that disposition explicitly; do not add a library launch row.

Prepare these data files **before** implementing the parser:

| Fixture | Contents / independent oracle |
|---|---|
| `catalog.json` and `catalog.sha256` | Hand-authored pinned S1 catalog: profile `rc`, fixed synthetic candidate/policy/roster identities; a Windows required build/discovery/group, a two-UID required selection, a separately identified diagnostic selection, a required preparation-failed sentinel, registered action IDs, project strings, resource keys and two eligible logical Windows target bindings A/B. The source of platform/role/action/project/filter/UID facts is this file, never a projection of submitted graph nodes. Pin its raw-byte SHA with a standard hash API before invoking production code; restore/compare its bytes after every case. |
| `prepare.valid.json`, `execute.valid.json`, `execute-preparation-failed.valid.json` | Three complete schema-valid documents copied from independent literal expectations. Prepare contains only its structural prerequisites; execute contains the fixed selection/group; failed preparation carries required/Windows, sealed-inconclusive, expectedCount=0, null filter/UID digest/skipReceipt and a non-null preparationReceipt. S1 tests preservation and parsing, not sentinel creation, execution or final verdict. |
| `execute.canonical.json`, `identity.expected.json` | Reviewed literal canonical UTF-8 bytes with no BOM/trailing newline, and expected node IDs, exact sorted UID strings and digest inputs. Expected SHA-256 comes from hashing these literal bytes with the framework API, not the production canonicalizer. Exact node IDs come from catalog constants. Code records the final bytes/digests/provenance; they remain **binding required** until authored and reviewed. |
| `invalid-cases.json` plus `duplicate-key.raw.json` | Explicit per-case edit recipe, input JSON path and expected refusal boundary. Raw duplicate-key text must bypass object serialization, which otherwise erases duplicates. No fixture enumerates required keys/enums from the schema under test to decide what to expect. |
| `preservation.json` | Exact integrated pre-S1 **master** source SHA; SHA-256 inventory of ten protected gate/legacy files and independent canonical policy projection, plus the specifically allowed new census row. Commission-only CP-C consumes this seed-bound input; ordinary CP-1 never compares the historical policy/file snapshot. This is a Code/Review slice preservation input, not a release receipt. Bind against the actual commissioned base, never ambient `HEAD` after edits. Future intentional policy/owner migrations require explicit reviewed rebinding. |

Use inert synthetic identifiers: source `a` repeated 40, policy/roster/binding
digests with different repeated hexadecimal characters, run `fixture-run-A`,
targets `windows-A`/`windows-B`, two different UIDs `Fixture.A.0`/`Fixture.A.1`.
For UID hashing use the literal ordinally sorted JSON string-array bytes in the
fixture; preserve case. Keep `requires.platform=Any` in the forged-platform and
role-flip cases so capability cannot conceal the policy-identity check.

Each invalid document starts from a separately accepted healthy control and
changes exactly one fact. Freeze the independent catalog across adversarial
graph edits. A legitimate rebind supplies a separately pinned B target-binding
fixture; changing the graph binding with the A input retained must refuse.
Missing catalog is an explicit input refusal, never permission to trust the graph.
`Read-QualificationGraph` requires the controller's separate `ExpectedCatalogHash`
(lowercase SHA-256) over exact supplied catalog UTF-8 bytes. Catalog shape is
closed, requires `uids`, rejects graph-only root keys and reuses typed graph
definitions. Malformed-catalog variants carry their own independently computed
pins so the hash guard cannot mask shape refusal; a matching pin never blesses
a graph used as its own catalog.

**Master-based fixture rule (D-1):** CP-1 must pass on the master-based candidate,
not only its rc lineage. Freeze the master policy and protected bytes before S1
edits; regenerate policyHash through owner tooling after the sole additive census
row. Never import an rc-only policy projection, protected-file hash or neighbor
roster count as a master oracle. Preserve old rc evidence as historical evidence.

The harness drives three observable production boundaries: strict raw JSON
decode, **direct validation against the actual schema file**, and catalog identity
comparison/canonical hashing. Direct schema assertions precede semantic checks;
this exposes a removed `required` entry even if catalog comparison would still
reject the document. The integrated parser must also call schema validation;
its refusal is separately asserted. Use the real local schema engine selected
in Code (for example PowerShell's schema validator with the checked-in schema),
no test implementation of validation and no network schema fetch. Prove the
engine enforces the used draft/keywords with healthy and invalid documents. If
that boundary cannot be exposed locally, return to Plan; do not build a validator
whose test merely repeats its tables.

Every matrix row asserts: healthy control accepted, target boundary reached,
exact refusal category and JSON path or exact returned bytes/identity. A parser
exception, missing library, failed import, empty matrix or a catch that accepts
any exception is not a behavioral pass. Stable named assertions are
`QGRAPH SchemaAndIdentity/<case-id>` below; the C# wrapper lists the complete
inventory and exact count, including per-variant suffixes. Bind that count in
Code, never derive it from observed PASS output or `Min=1`.

### Schema compatibility policy

Keep integer `schemaVersion=1` and the parent's closed-object contract. Freeze
canonical bytes/identity semantics for existing v1 inputs. Additive v1 changes
may add explicitly reviewed optional, non-authoritative metadata only while
retaining every existing valid document and refusal invariant; writers may not
assume old closed readers accept newly emitted fields. Pin the schema revision
with the consuming code and keep old-writer goldens. Unknown fields are refused,
not ignored as an extensibility mechanism. A changed required field, enum meaning,
identity/hash algorithm, execution authority or live-provider capability needs a
new version and its own design. Version 0/2/string `"1"` are refused here.

For D-10/A-6 use the re-check's conservative v1 option: retain the parent's
required placeholder keys `capability.liveProvider` (also `node.requires`) and
`target.quota`, but constrain both to **null only**. No non-null provider/quota
payload and no extra live-provider fields are admitted. This is the brief's
explicit S1 constraint, not an answer to Q5 or CARD-1545 activation. Dedicated
live actions/targets also fail the independently pinned registry. Existing Full
tests, including currently required C680 coverage, are not removed or retagged.

### Proves it works now

All durable V rows use the single exact method/filter above and CP-1; the R-1/R-2
historical preservation comparison uses CP-C only. `schema` means the
direct schema call plus integrated parser; `identity` means production comparison
against the separate fixed catalog; `canonical` means production bytes/hash versus
literal golden bytes. Local V IDs refine the parent's V-1 only.

| ID | Behavior / layer / recipe and variants | Case IDs, oracle, reachability and classification |
|---|---|---|
| V-1 | Valid structure; raw/schema/identity. Load all three golden documents. | `valid-prepare`, `valid-execute`, `valid-sentinel`: acceptance, exact supplied kind/role/platform/count/nulls preserved. Each reaches production decode, schema and identity. NEW capability; no S2 semantic verdict asserted. |
| V-2 | Required fields and closed v1 schema. Delete node platform, node role, root profile separately; inject one unknown property at each object shape. | `missing-platform`, `missing-role`, `missing-profile`; `unknown-field/<shape>` where shape=root,candidate,planner,history,target,capability,node,selection,estimate,priority,edge. Direct schema must refuse at that path; healthy counterpart passes. NEW; D-1 direct missing-platform arm is mandatory baseline red. |
| V-3 | Typed scalar/schema domain. Wrong version, unsupported platform/role, malformed identity/digest, invalid numeric/date fields. | `version/{0,2,string}`, `platform-enum`, `role-enum`, `digest`, `sha`, `number-type`, `number-range`, `date-format`. Examples: platform Solaris, role optional, 63-char digest, nonhex 40-char SHA, expectedCount string, parallelism 0, impossible closedAtUtc date. Domain-invalid value must be the sole bad fact. Date check is explicit if the engine treats format as annotation. NEW. |
| V-4 | Lossless raw input and unique IDs. Feed raw duplicate keys at root/nested scope; duplicate a node ID; feed NaN/Infinity and numeric overflow. | `duplicate-key/{root,nested}`, `duplicate-node`, `nonfinite/{NaN,Infinity,overflow}`. Refuse before lossy object conversion/identity output; byte-encoded duplicate keys with differing values are retained in raw input. A unique sibling control is accepted. NEW. |
| V-5 | Independent catalog identity. Keep schema valid, change one catalog-owned fact. | `catalog-missing`, `profile-foreign`, `policy-foreign`, `roster-foreign`, `platform-forged`, `role-flipped`, `action-unknown`, `project-unknown`, `path-alias`, `target-unknown`, `binding-wrong`, `selection/{filter,uid,count}`, `node-kind`. Each must pass direct schema then refuse its identity check. Use valid-looking replacement digests. Path alias is an unregistered `../` or case-altered path string, not an actual file access. Include both required->diagnostic and diagnostic->required role flips. NEW; platform/role arms close D-1's self-oracle caveat. |
| V-6 | Canonical bytes and digest. Permute object keys, insignificant whitespace and set inventories; change scalar type/value. | `canonical-keys`, `canonical-sets`, `typed-null`, `typed-number`, `utf8-hash`. Exact golden bytes plus SHA, equality across equivalent inputs and inequality for null vs empty and number vs quoted string at the canonicalizer boundary. Include non-ASCII text via escaped literals, escaped control characters, decimal values and a non-default culture; ordinal order and UTF-8 must agree. An invalid typed graph still refuses schema; primitive canonicalizer tests grant no admission. NEW. |
| V-7 | Identity under legitimate target changes. Start with A, then change bindingDigest only, then logical targetId with coherent separately pinned B target catalog. | `binding-node-stable`, `target-node-stable`: exact node IDs and uidSetDigest unchanged; `binding-graph-changed`, `target-graph-changed`: graph digest differs. `uid-members-changed`: replace one UID with a same-count different UID in an independently supplied catalog, require a different uidSetDigest. Assert exact expected bytes as well as inequalities so a random digest fails. NEW; S9 prerequisite only. |
| V-8 | Live payload excluded in v1; schema and registry. Set liveProvider=Claude separately on target capability and node requirements; supply a well-formed non-null quota; inject dedicated live target/action. | `live-provider/{target,node}`, `live-quota`, `live-target`, `live-action`. Direct schema rejects provider/quota; identity rejects otherwise schema-valid unregistered target/action. Null companions pass. NEW; D-10/A-6. No provider, activation or quota spending. |
| V-9 | Inert library boundary and durable classification/portability. Import/call against owned scratch inputs, inventory source and scratch outputs before/after. | `no-side-effects`, `script-census`, `ascii` in CP-1. Return values only; zero extra child/network/runner/gate calls, no input changes or receipt/state files. Historical policy/file preservation is R-1/R-2 under commission-only CP-C, never SchemaAndIdentity. See R-1..4. |
| D-3 | Independent catalog UID recomputation, identity. Change catalog membership without updating its declared digest; also make graph/catalog declared count or digest coherently wrong against actual members. | `catalog-uid-mismatch/{wrong-uid,missing-uid,extra-uid,wrong-count,wrong-digest}`: five rows, each category=identity, path=`/nodes/3/selection`, reason=`catalog-uid-mismatch`. Healthy pinned catalog passes; variants reach recomputation rather than ordinary graph/catalog field inequality. PC-51/52. |
| D-4 | External catalog shape and pin authority, identity. Supply prepare/execute as their own catalog, malformed catalogs with matching variant pins, and healthy catalog with absent/wrong external pin. | `catalog-is-graph/{prepare,execute}` and `catalog-shape/{missing-uids,unknown-field,wrong-node-shape,wrong-uid-type}` refuse at `/catalog`, reason=`catalog-invalid`. `catalog-pin-missing` requires `catalog-pin-required`; `catalog-pin-wrong` requires `catalog-pin-mismatch`. Eight rows; a well-formed independently pinned catalog passes. PC-53..56. |
| D-5 | Exact decoded-number identity. Graph canonicalOrdinal=`1.0000000000000000000001e22`, catalog=`1e22`, with independently pinned catalog bytes; direct graph schema accepts the integer value. | `catalog-number-exact`: category=identity, path=`/nodes/0/canonicalOrdinal`, reason=`catalog-mismatch`. Compare canonical decoded JsonElement subtrees for node/target/root/selection identity, including expectedCount, parallelism and resource sets; no lossy PowerShell number round-trip. Equivalent numeric spellings remain equal. PC-57. |

Also exercise missing/unknown `coverageRole`, `platformOfRecord` and `profile`
through the integrated parser, not only an isolated engine. Generic shape coverage
includes an independently authored missing remaining-required-key and wrong-type
matrix for each object shape; its expected paths are frozen fixture data. These
are variants of V-2/V-3, not a second test or a production-derived schema oracle.
Cycles/self-edges/mandatory-edge conservation are deliberately **not** a negative
S1 fixture: schema parses edge shape only. Catalog construction belongs to S2;
graph semantic cycle refusal belongs to S3/parent CP-3. No S2/CP-2 is run here.

### Current-base red contract

The schema, library, harness and wrapper are absent at the pinned base, so no
behavioral RED has been measured. Code first commits/pushes the compiling wrapper,
independent fixtures and harness. Its explicit `production-surface` assertion can
record absence; zero selected tests or an import/build failure is not accepted red.

Before the fix, establish the mandatory **behavioral** red: supply the callable
minimal product/schema surface with `platformOfRecord` absent from the node
`required` list. `SchemaAndIdentity/missing-platform` must fail because direct
schema validation accepts that otherwise valid document. Commit/push that state,
run CP-1 and retain its named assertion failure; then implement the guard and the
rest of S1, commit/push and run CP-1 green. This is the new-surface test-first
sequence, not a claim the original base already had the API. The other V rows
start at the explicit surface assertion until their seam exists; their specific
post-implementation defects are bound by the PC inventory below. R-1/2 now run
only with the explicit commission context; they are conservation checks, not
fabricated new-feature reds or permanent nightly snapshots.

### Guards the regression

- **R-1 Full policy/census unchanged (CP-C only):** `FullCensusUnchanged` compares
  all pre-existing policy fields (including required suites, exclusions, test
  dispositions, costs and watchdogs) to the pinned pre-S1 projection. Permit only
  the exact new `argument-consumer`, non-wildcard harness census row and recomputed
  policyHash. Preserve ten required RC suites from the actual pinned policy, including
  observability; stale nine-suite prose/old fixture counts are not authority.
  No existing C680 test/category/disposition changes. The single new TUnit UID is
  additive, so "unchanged" means conservation of every existing obligation, not
  an unchanged numeric total after adding a test.
- **R-2 Full gates/legacy entrypoints unchanged (CP-C only):** `FullCensusUnchanged`
  compares SHA-256 bytes of `scripts/lib/release-{gate,authority,selection}.ps1`,
  `scripts/lib/nightly-{run-impl,tests-impl,failure-history}.ps1`, the legacy
  nightly entrypoints and supervisor-heartbeat library/script to pinned base
  bytes. The fixed master-bound manifest contains exactly ten files; a future
  commissioned base with a different roster needs explicit reviewed rebinding,
  including `nightly-dispatch.ps1` if applicable. Inventory paths as well as hashes.
  Code/Review inspect the complete base..HEAD diff and permit no caller wiring,
  state/config changes, deploy or supervisor mutation. This proves S1 adds no
  gate path; it does not requalify the gates or assert that inherited gaps are fixed.
- **R-3 Script classification/portability:** `SchemaAndIdentity/script-census`
  calls the existing census reader and checks exactly the new non-wildcard
  argument-consumer row; dot-sourced library has zero unattended launch rows.
  `ascii` requires both added PS files to exist and every byte <=127, and parses
  their syntax. Unicode fixtures remain JSON data/escaped literals. These are
  maintenance witnesses, not native Windows runtime qualification.
- **R-4 Import/parse remains inert:** `SchemaAndIdentity/no-side-effects` uses
  nonempty owned canary inputs and before/after recursive byte inventories;
  parser/canonicalizer/identity return no execution/release-credit receipt.
  Capture attempted child/HTTP/runner/writer calls with fail-on-call test seams
  and inspect the new library's complete dependency/call footprint in Review.
  No substitute proves absence of arbitrary undeclared native calls by itself.

For R-1/R-2 Code binds the preservation manifest to its actual integrated master
base before edits; it never auto-updates from the candidate to satisfy a mismatch.
Unrelated legitimate landed changes require a documented rebind before the S1
commission, retaining old provenance. No Full suite or live run is needed to prove
this additive slice has no gate/legacy delta. No S1 result grants Full credit.

CP-C runs `pwsh -NoProfile -File scripts/test-qualification-graph.ps1 -Case
FullCensusUnchanged -ContextPath .antiphon/qgraph-s1-context.json`. The commissioning
controller writes that owned context, not the library or ordinary wrapper. Its
fields are `schemaVersion=1`, exact absolute `repositoryRoot`, full 40-hex
`baseSha`, `seedSha`, `candidateSha`, and lowercase 64-hex `preservationSha256`.
For this fix the base is `96715b56623d57a16c6788838bd18281a40e768d`, the seed is
`85642fb6a0a14b291baf1792f831b38c8a21944c`, and the preservation digest is
`c5907548f0fa700c4de100dd27b3c8249f50dfb44fc1171fc54220a745bcea1c`.
The candidate/root must identify the actual commissioned clean checkout, not the
fix worker's old checkout; a later landed SHA must be pinned explicitly.

The command checks HEAD=candidate, base->seed->candidate ancestry, identical
seed/current preservation bytes, manifest base, baseline Git policyHash and
projection, candidate policyHash, the sole allowed census row and ten protected
hashes against Git at base. There is **no default seed/base**. Missing, empty or
nonexistent context is **REFUSED**, exit 2 `commission-context-required`; invalid
root, stale candidate or mismatched seed/pin also refuse. Valid preservation exits
0 with a seed/base/candidate summary; this command has no TUnit assertion count.
It is **not part of the nightly/Unit lane**, and has no durable wrapper selection.

Commission controls must prove both directions: a readable policy disposition edit
with correctly restated hash refuses with `commission-policy-changed`; a parseable
protected-file edit refuses with `commission-gate-changed:<path>`. Ordinary
SchemaAndIdentity must remain **112/112 green** after either unrelated edit.
Run the independent policy edit on an existing row (the review used
`test-nightly-failed-first.ps1`), leaving the new harness census row intact.
Missing context must refuse; restore source and prove valid CP-C green. These
controls supplement, and do not discharge, the pending SourceLanding battery.

### Review advisory notes (09aa4107)

- **A-2:** CP-C verifies HEAD=candidateSha but compares working-tree policy and
  protected files, not `git show <candidateSha>:<path>`. A dirty tree can hide a
  committed edit. The checkpoint tool's clean-source rule (`dirty=0`, stable
  source) covers ordinary acceptance today; CP-C outside that rule is not
  equivalent clean acceptance evidence. A deliberate temporary mutant is red
  control evidence only, with separately verified clean baseline/restoration.
  A future repair could read candidate Git objects or explicitly refuse dirt.
- **A-3:** `test-nightly-tests.ps1` G056 expects 14 census rows; master base has
  17, the S1 tip 18. Review reproduced red at both base and tip, so this is
  inherited, not an S1 regression. Only the no-`-Case` harness path runs it; no
  wrapper selects that path. **It should become a separate repair card**, after
  checking for an existing duplicate; this one-document task files no card and
  changes no fixture/count to conceal the red.
- **A-4:** server2 supplied pwsh 7.5.4 only; pwsh 7.4 and Windows were not
  executed. The Test-Json ` at '...'` schema-path message regex was exercised on
  7.5.4. Different 7.4 wording could make path assertions fail closed (red);
  minimum-version/runtime compatibility remains unverified there. Do not claim
  7.4 execution from the declared requirement or from ASCII checks.

### Guard inventory

All safety-relevant S1 guards, including compatibility/preservation guards, are
listed below. Independently bypassable comparisons are separate rows. A repeated
schema rule has explicitly enumerated location variants rather than an assumed
one-location mutation covering all locations. There are no intentionally untested
S1 safety guards. Execution status for every row is pending.

| Guard | Plan reference and invariant | Distinct control |
|---|---|---|
| G-1 | V-1/run-plan node platformOfRecord is required | PC-1 |
| G-2 | V-1/run-plan node coverageRole is required | PC-2 |
| G-3 | V-1/run-plan root profile is required | PC-3 |
| G-4 | Run-plan remaining required structure cannot default silently | PC-4 |
| G-5 | Run-plan closed objects refuse unknown fields | PC-5 |
| G-6 | Run-plan integer version is exactly 1 | PC-6 |
| G-7 | Run-plan platformOfRecord domain is closed | PC-7 |
| G-8 | Run-plan coverageRole domain is closed | PC-8 |
| G-9 | Run-plan scalar types, domains and bounds are enforced | PC-9 |
| G-10 | Canonical-input contract rejects duplicate raw JSON keys | PC-10 |
| G-11 | V-1 node identity is unique | PC-11 |
| G-12 | Canonical-input contract rejects nonfinite numbers | PC-12 |
| G-13 | Run-plan SHA-256 digest syntax is strict | PC-13 |
| G-14 | Run-plan source SHA syntax is strict | PC-14 |
| G-15 | Run-plan date formats are explicitly checked | PC-15 |
| G-16 | Re-check S1 scope requires a separate catalog input | PC-16 |
| G-17 | Profile belongs to the pinned catalog | PC-17 |
| G-18 | Policy digest belongs to the pinned catalog | PC-18 |
| G-19 | Roster digest belongs to the pinned catalog | PC-19 |
| G-20 | D-1 platformOfRecord equals independent policy identity | PC-20 |
| G-21 | D-1 coverageRole equals independent policy identity | PC-21 |
| G-22 | Action is registered, not planner command/provider text | PC-22 |
| G-23 | Project is a registered catalog identity | PC-23 |
| G-24 | Ambiguous/unregistered path aliases cannot become identity | PC-24 |
| G-25 | Target is a registered catalog identity, live excluded | PC-25 |
| G-26 | Graph binding digest matches separately supplied target binding | PC-26 |
| G-27 | Node kind cannot replace its catalog kind | PC-27 |
| G-28 | Selection filter cannot change its catalog identity | PC-28 |
| G-29 | Selection UID digest cannot change its catalog identity | PC-29 |
| G-30 | Selection count cannot change its catalog identity | PC-30 |
| G-31 | Canonical key order is ordinal and whitespace-independent | PC-31 |
| G-32 | Set-valued graph inventories canonicalize ordinally | PC-32 |
| G-33 | Canonical null differs from empty string | PC-33 |
| G-34 | Canonical numeric value differs from string value | PC-34 |
| G-35 | External graph digest binds exact canonical UTF-8 bytes | PC-35 |
| G-36 | Re-check/R2-D1 prerequisite: binding changes preserve node IDs | PC-36 |
| G-37 | Re-check/R2-D1 prerequisite: logical target changes preserve node IDs | PC-37 |
| G-38 | Binding changes change graph digest | PC-38 |
| G-39 | Logical target changes change graph digest | PC-39 |
| G-40 | UID membership, not just count, determines uidSetDigest | PC-40 |
| G-41 | D-10/A-6 v1 liveProvider is null only | PC-41 |
| G-42 | D-10/A-6 v1 quota is null only | PC-42 |
| G-43 | Commission-only R-1 Full policy obligations are conserved | PC-43 |
| G-44 | Commission-only R-2 scope cannot alter gates/legacy callers | PC-44 |
| G-45 | S1 parse/import creates no execution or release side effect | PC-45 |
| G-46 | Composition new harness is not an unattended no-argument launch | PC-46 |
| G-47 | Composition Windows-side scripts remain ASCII | PC-47 |
| G-48 | V-1 integrated parser actually enforces the schema before identity success | PC-48 |
| G-49 | Re-check/R2-D1 prerequisite: binding changes preserve uidSetDigest | PC-49 |
| G-50 | Re-check/R2-D1 prerequisite: logical target changes preserve uidSetDigest | PC-50 |
| G-51 | Review D-3 catalog UID membership recomputes the declared digest | PC-51 |
| G-52 | Review D-3 catalog UID member count equals declared expectedCount | PC-52 |
| G-53 | Review D-4 a graph cannot serve as its own catalog | PC-53 |
| G-54 | Review D-4 catalog shape is closed and UID/node data is typed | PC-54 |
| G-55 | Review D-4 external catalog hash pin is mandatory | PC-55 |
| G-56 | Review D-4 catalog bytes match the externally supplied pin | PC-56 |
| G-57 | Review D-5 identity comparisons preserve exact decoded numbers | PC-57 |
| G-58 | Review D-2 preservation requires a valid seed-bound commission context | PC-58 |

### Positive controls

**All payloads require binding to the landed source.** The implementation now
exists, but this amendment supplies no executable mutation pack. For each
PC/variant the later binder records exact
landed source SHA, file/function/JSON pointer, before/after bytes and hashes,
replacement count, boundary entry, exact assertion and restoration recipe. Review
checks those bindings before commissioning Mutation. Names below are fixed
assertion contracts, not fabricated evidence. Mutations change product/schema/
policy bytes, never expected fixture data or assertion logic. Schema edits must
remain valid JSON and valid schemas; PowerShell/C# edits must compile/parse.

PC-1..42, PC-45..57 use **only**
`/*/Antiphon.Tests.Scripts/QualificationGraphContractTests/SchemaAndIdentity`
in `tests/Antiphon.Tests`, Min=1, with the same complete method. A named internal
case failing is decisive; zero tests, infrastructure failure, bad fixture or
unrelated failure is not red. For these PCs the case column means exact assertion
`QGRAPH SchemaAndIdentity/<case>` and specifies its predicate. **PC-43/44/58
instead use commission-only CP-C**, no project/filter/Min, expecting exit 2 and
the exact refusal reason; restored CP-C must exit 0. They do not target removed
SchemaAndIdentity/full-policy or gate-files assertions. All healthy control and
boundary-reached assertions must remain valid. Category: PC-1..42, PC-48..57 are
new behavior controls; PC-43/44 protect existing commission invariants; PC-45..47
protect integration/portability; PC-58 protects the explicit commission boundary.

| PC / guard | Compiling defect recipe; variant(s) | Expected assertion RED / reachability | Cycles |
|---|---|---|---:|
| PC-1 / G-1 | Delete platformOfRecord from node.required only | `missing-platform`: direct schema result must be refused; otherwise valid execute golden, no catalog masking | 1 |
| PC-2 / G-2 | Delete coverageRole from node.required only | `missing-role`: direct schema refuses | 1 |
| PC-3 / G-3 | Delete profile from root.required only | `missing-profile`: direct schema refuses | 1 |
| PC-4 / G-4 | Remove the required list at one object shape per cycle, retaining G-1..3 keys where applicable | `missing-other/<shape>`: direct schema refuses one independently frozen remaining required property; shapes root,candidate,planner,history,target,capability,node,selection,estimate,priority,edge | 11 |
| PC-5 / G-5 | Set additionalProperties=true at one object shape per cycle | `unknown-field/<shape>`: direct schema refuses unknown `fixtureUnexpected`; same eleven shapes as PC-4 | 11 |
| PC-6 / G-6 | Replace const 1 with permissive number/string type accepting the selected wrong version | `version/0`, `version/2`, `version/string`: direct schema refuses, valid integer-1 companion passes | 3 |
| PC-7 / G-7 | Add Solaris to platformOfRecord enum | `platform-enum`: direct schema refuses unsupported platform | 1 |
| PC-8 / G-8 | Add optional to coverageRole enum | `role-enum`: direct schema refuses unsupported role | 1 |
| PC-9 / G-9 | Relax one domain at a time: expectedCount type, parallelism minimum, phase enum, node kind enum, edge condition enum | `number-type`, `number-range`, `phase-enum`, `kind-enum`, `edge-enum`: direct schema refuses at changed path, positive counterpart accepted | 5 |
| PC-10 / G-10 | Remove raw duplicate-key rejection at root, then nested-object boundary; retain last value | `duplicate-key/root`, `duplicate-key/nested`: strict decode refuses before object conversion; duplicate values end in otherwise valid data | 2 |
| PC-11 / G-11 | Remove duplicate-node-ID check; permit overwrite in node index | `duplicate-node`: identity refusal; clone a valid node so no unrelated field differs | 1 |
| PC-12 / G-12 | At strict numeric decode/finite check, accept the selected nonfinite representation as a normal numeric token | `nonfinite/NaN`, `nonfinite/Infinity`, `nonfinite/overflow`: raw boundary refuses; final variant uses 1e9999, not invalid string syntax | 3 |
| PC-13 / G-13 | Relax shared digest definition to arbitrary string | `digest`: direct schema refuses 63-character policyDigest; no catalog invocation needed | 1 |
| PC-14 / G-14 | Relax shared SHA definition to arbitrary string | `sha`: direct schema refuses nonhex sourceSha | 1 |
| PC-15 / G-15 | Remove explicit date validation (and schema enforcement if it masks that same boundary) | `date-format`: impossible closedAtUtc is refused; no history lookup performed | 1 |
| PC-16 / G-16 | Default absent separate catalog to graph-derived identity | `catalog-missing`: identity returns catalog-required, not success | 1 |
| PC-17 / G-17 | Skip profile equality against supplied catalog | `profile-foreign`: valid identifier accepted by schema but identity must refuse | 1 |
| PC-18 / G-18 | Skip policyDigest equality | `policy-foreign`: well-formed different digest must refuse | 1 |
| PC-19 / G-19 | Skip rosterDigest equality | `roster-foreign`: well-formed different digest must refuse | 1 |
| PC-20 / G-20 | Compare platformOfRecord to submitted graph instead of catalog | `platform-forged`: Any must refuse against Windows catalog even with requires.platform=Any | 1 |
| PC-21 / G-21 | Trust submitted coverageRole in identity projection | `role-flipped/required-to-diagnostic`, `role-flipped/diagnostic-to-required`: identity must refuse, schema passes both | 2 |
| PC-22 / G-22 | Accept nonempty action string without registered-action equality | `action-unknown`, `live-action`: schema-valid arbitrary action and dedicated live action both refuse identity | 2 |
| PC-23 / G-23 | Skip project equality/registry lookup | `project-unknown`: different syntactically valid project must refuse | 1 |
| PC-24 / G-24 | Normalize a parent traversal alias, then use case-insensitive project matching | `path-alias/traversal`, `path-alias/case`: unregistered literal paths must refuse; no filesystem lookup | 2 |
| PC-25 / G-25 | Auto-register targets found only in submitted graph | `target-unknown`, `live-target`: unknown logical target refuses against unchanged supplied catalog | 2 |
| PC-26 / G-26 | Trust submitted target.bindingDigest rather than supplied binding | `binding-wrong`: A catalog plus valid-looking B digest refuses; legitimate B/B companion passes | 1 |
| PC-27 / G-27 | Omit kind from catalog identity comparison | `node-kind`: change chunk to selection (both schema-valid), identity refuses | 1 |
| PC-28 / G-28 | Omit selection.filter from catalog comparison | `selection/filter`: widened but schema-valid filter refuses | 1 |
| PC-29 / G-29 | Omit selection.uidSetDigest from catalog comparison | `selection/uid`: valid-looking foreign digest refuses | 1 |
| PC-30 / G-30 | Omit selection.expectedCount from catalog comparison | `selection/count`: change 2 to 1 while catalog remains 2, identity refuses | 1 |
| PC-31 / G-31 | Replace ordinal canonical object-key sorting with insertion order | `canonical-keys`: differently ordered/whitespace input equals exact golden bytes; primitive mixed-case/Unicode key control also exercises ordinal behavior | 1 |
| PC-32 / G-32 | Preserve arrival order for set-valued inventories | `canonical-sets`: reverse targets/nodes/edges/resourceKeys without changing ordinal/rank fields, bytes must equal golden | 1 |
| PC-33 / G-33 | Serialize null as empty string | `typed-null`: exact literal null/empty bytes and unequal hashes; canonicalizer directly reached | 1 |
| PC-34 / G-34 | Quote numeric values during canonicalization | `typed-number`: number and string have different exact bytes/hash; direct primitive boundary | 1 |
| PC-35 / G-35 | Hash UTF-16 instead of canonical UTF-8 bytes | `utf8-hash`: result equals SHA256 of independent no-BOM UTF-8 golden, including escaped/Unicode content | 1 |
| PC-36 / G-36 | Incorporate bindingDigest into node ID derivation | `binding-node-stable`: exact node IDs equal A constants after legitimate binding-only change | 1 |
| PC-37 / G-37 | Incorporate targetId into node ID derivation | `target-node-stable`: exact node IDs equal A constants after legitimate target change | 1 |
| PC-38 / G-38 | Exclude bindingDigest from canonical graph hash input | `binding-graph-changed`: B hash equals B golden and differs from A | 1 |
| PC-39 / G-39 | Exclude target IDs/references from canonical graph hash input | `target-graph-changed`: renamed target hash equals its golden and differs from A | 1 |
| PC-40 / G-40 | Compute uidSetDigest from count only | `uid-members-changed`: same count/different UID has the distinct expected digest | 1 |
| PC-41 / G-41 | Admit Claude in capability.liveProvider schema | `live-provider/target`, `live-provider/node`: direct schema refuses at both references of capability definition | 2 |
| PC-42 / G-42 | Restore parent's permissive non-null quota object schema | `live-quota`: direct schema refuses otherwise well-formed quota | 1 |
| PC-43 / G-43 | Remove observability from profiles.rc.requiredSuites and recompute valid policy hash | **CP-C FullCensusUnchanged**: exit 2 `commission-policy-changed`; policy remains readable, context candidate matches unchanged commissioned HEAD. No stale-hash/context failure counts. CP-1 remains 112/112 green | 1 |
| PC-44 / G-44 | Relax release-gate.ps1 Full-scope comparison with valid PowerShell | **CP-C FullCensusUnchanged**: exit 2 `commission-gate-changed:scripts/lib/release-gate.ps1`; nonempty seed manifest covers that exact file, valid context reaches the digest guard. CP-1 remains 112/112 green | 1 |
| PC-45 / G-45 | Add a write of fabricated completed graph receipt in the owned input directory on successful parse | `no-side-effects`: output inventory/input bytes unchanged; complete valid input reaches added write, no real host path | 1 |
| PC-46 / G-46 | Change only new harness census disposition to unattended and recompute policy hash | `script-census`: exact new argument-consumer row required; legacy entries still identical | 1 |
| PC-47 / G-47 | Insert non-ASCII text in a parseable comment in new library | `ascii`: all bytes <=127; parsing/healthy graph still works | 1 |
| PC-48 / G-48 | Bypass schema invocation in the integrated parse/identity path | `schema-wired`: integrated parser refuses an extra root property while all catalog-owned facts remain valid; direct schema still refuses, healthy integrated companion succeeds | 1 |
| PC-49 / G-49 | Include bindingDigest in uidSetDigest input | `binding-uid-stable`: exact UID digest equals A constant after legitimate binding-only change | 1 |
| PC-50 / G-50 | Include targetId in uidSetDigest input | `target-uid-stable`: exact UID digest equals A constant after legitimate target change | 1 |
| PC-51 / G-51 | m3: bypass catalog UID recomputation, including count masking where necessary for the one membership guard; bind each of the five D-3 variants separately | `catalog-uid-mismatch/{wrong-uid,missing-uid,extra-uid,wrong-count,wrong-digest}`: full bypass must fail all five exact rows (107 pass/5 fail); digest-only diagnostic must fail wrong-uid and wrong-digest. Valid catalog accepted; category/path/reason exactly as D-3 | 5 |
| PC-52 / G-52 | Bypass only the catalog member-count arm, keeping digest recomputation | `catalog-uid-mismatch/wrong-count`: graph/catalog both declare 3 against two actual UIDs; 111 pass/1 fail. No ordinary graph/catalog count mismatch masks the guard | 1 |
| PC-53 / G-53 | Bypass the catalog shape check at the identity boundary | `catalog-is-graph/prepare`, `catalog-is-graph/execute`: independently pinned graph-as-catalog must refuse catalog-invalid at /catalog. Execute may need a valid multi-site binding to prevent missing-uids access masking the intended assertion; a thrown exception/build error is not accepted red | 2 |
| PC-54 / G-54 | Independently relax required uids, catalog root closure, node required platform, then UID item string type | `catalog-shape/{missing-uids,unknown-field,wrong-node-shape,wrong-uid-type}`: matching variant pins reach shape guard, catalog-invalid at /catalog. Bind minimal masking sites per guard; schema remains valid | 4 |
| PC-55 / G-55 | Default a missing external ExpectedCatalogHash to a computed self-pin | `catalog-pin-missing`: healthy catalog without controller pin must refuse catalog-pin-required at /catalog | 1 |
| PC-56 / G-56 | Skip the exact catalog-byte hash comparison while retaining pin syntax | `catalog-pin-wrong`: healthy catalog with 64 zeroes must refuse catalog-pin-mismatch at /catalog; correct independent pin succeeds | 1 |
| PC-57 / G-57 | Round-trip decoded identity subtrees through ConvertFrom-Json/ConvertTo-Json numeric objects | `catalog-number-exact`: schema-valid 1.0000000000000000000001e22 graph vs 1e22 catalog must refuse catalog-mismatch at /nodes/0/canonicalOrdinal, not accept their rounded equality | 1 |
| PC-58 / G-58 | Independently bypass the context-required branch (absent, empty, nonexistent path), root equality, candidate equality, preservation pin comparison, then baseline Git policy projection check | **CP-C**: seven refusal variants: three commission-context-required; commission-context-invalid; commission-candidate-mismatch; commission-seed-fixture-mismatch; commission-baseline-policy-mismatch for a forged self-rebound seed/policy. Each valid companion reaches preservation and exits 0. Forged context/input controls are controller-owned fixtures, never changes to expected assertions; exceptions from missing fields/files do not count as intended red | 7 |

PC-9 adds the explicitly named `phase-enum`, `kind-enum`, `edge-enum` variants to
V-3; PC-4 adds V-2's eleven `missing-other` variants. PC-24/21 refine their V-5
suffixes. PC-48 names V-2's integrated `schema-wired` assertion; PC-49/50 name
V-7's separate `binding-uid-stable`/`target-uid-stable` assertions. Every named
durable variant executes in ordinary CP-1; PC-43/44/58 use CP-C exclusively.
Mutation introduces only the selected defect per cycle.

If independent checks mask a mutation, bind a minimal valid multi-site defect
for **that one guard** and record masking sites. Do not weaken another guard or
change the assertion to get red. A library-only nonfinite check cannot be claimed
tested when the JSON engine always rejects first: bind the actual raw decoder
path or return the unverifiable seam to Plan. The same applies to direct-schema
integration and catalog identity. Unknown payloads remain binding required after
Code; no unbound row becomes mechanically executable by labelling it complete.

Code runs ordinary V/R and the required baseline red. Ordinary Review judges
this inventory and implemented reachability **before land**. After confirmed land,
SourceLanding Mutation runs every listed cycle: baseline green, break, exact
assertion red, restore, same method green (or the same CP-C command and context
contract for commission controls). Use the unchanged local checkpoint
driver copied to external evidence, fresh isolated build/results per phase, await
each phase, restore bytes and timestamps, verify build provenance, and retain
restoration records externally. Durable PCs share a method and most controls share files;
none is eligible for independent mutation batching. No mutation commits/pushes
from a SourceLanding snapshot. Pin the commission context candidate to unchanged
HEAD=L; CP-C reads the deliberate working-tree mutation. Record that dirty red
phase explicitly, and require clean source/provenance for baseline and restored
acceptance. Never claim a stale-context refusal as a policy/file red. Bind these
phases in the Mutation pack under the A-2 limitation; do not bypass source checks
to manufacture a clean acceptance receipt.

Design audit before amendment: **guards=50, PCs=50, mapped=50, missing=0,
duplicate PC maps=0**. After amendment: **guards=58, PCs=58, mapped=58,
missing=0, duplicate PC maps=0**. Every G-1..50 -> PC-1..50 mapping survives;
PC-43/44 retain their invariants with the corrected CP-C target.
Execution audit: **SourceLanding PCs run=0/58**. The historical **50-PC/84-cycle
floor remains pending**, with eight new PCs adding 22 cycles: **58 PCs/106 cycles**
total (97 CP-1, nine CP-C). Diagnostic probes in Code/Review discharge none of
this battery. Every cycle requires intended red and restored green evidence.

### Out of scope

- S2 catalog extraction, complete required UID/set conservation, preparation
  orchestration and disappearance census; S3 edge/cycle/platform admission and
  real observed-worker OS; S4+ history/order/executor/transport/recovery/rendering.
  A pinned tiny S1 catalog is not a Full catalog extraction witness.
- S9 attempt ledger/rebind reduction R2-D1. Target-independent IDs are a necessary
  input, not proof of sticky red across graph digests.
- Native Windows/ConPTY, Linux process-group custody, locks/capacity/concurrency,
  private supervisor, operational deployment/activation, real queues/providers,
  credentials and any live spend. No fake target binding proves observed OS.
- Full RC, gate publication, receipt issuance/reuse, CARD-1545 migration, Q1-Q8
  choices and unrelated inherited fixture repairs. No test assembly-wide run.

### Implementation steps

These two steps preserve the original test-first sequence and cost history;
the reviewed master-based fix has already implemented S1. Subsequent binding
uses the 112-assertion CP-1 and separate commission-only CP-C defined here.

1. **Test-first contract, 30-45 minutes including the 11-minute red checkpoint.**
   Read/re-anchor the named fixtures at the commissioned Code base, freeze the
   independent catalog/goldens/preservation manifest, add the parameterless wrapper
   and complete case inventory, then commit/push. Add only the callable minimal
   surface needed for the `missing-platform` schema defect; commit/push before
   CP-1 and record its assertion RED. Missing-product evidence alone cannot close
   this step. Keep this WIP non-landable.
2. **Complete S1, 45-60 minutes including CP-1 green.** Implement the versioned
   schema with null-only live placeholders, strict parser, canonical bytes/hash
   and separate catalog identity. Add the sole harness census row with generated
   policy hash; bind every oracle/PC candidate and exact named assertion count.
   Commit/push the fix before CP-1, run CP-1 and explicitly commissioned CP-C,
   retain counts/provenance, and fix/rerun only the affected row if needed.
   Restore/delete owned build outputs and
   publish the concrete Code report for ordinary Review.

These are ordered pushed boundaries of S1, not S2 authorizations. They refine the
parent's provisional 45-minute estimate to **75-105 minutes total**, including
both red and green runs. If a commission cannot fit one 60-minute slice, settle
at the pushed step boundary and commission the remaining step; keep the same
single-method CP-1 contract plus commission-only CP-C. Do not invent a broad run.
No human choice blocks S1. An unverifiable parser/schema/catalog seam returns to
Plan; questions about future policy remain for their owning later slices.

### Performance budget

Original S1 estimates were **provisional, n=0**, not measured p95; the CP-C row
below separately records the review's later single observation. The parent reports a
413.23-second loaded build (n=1) and historical script child p95=12.396 seconds
for a different witness. Neither is this method's p95. Reasoned targets: seven
minutes isolated build; at most 90 seconds p95 for one owned PowerShell process
loading the tiny catalog once and running the in-memory/file matrix. Even with
106 planned PC cycles the ordinary method runs its 112 assertions once, not one
child process per PC. Nine cycles target the separate commission command.

| Component | Reasoned estimate | Explicit bound / evidence |
|---|---|---|
| Tool bootstrap/import | 2 minutes once | Gated alternate output; record tool SHA/path/hash and setup time |
| Isolated Antiphon.Tests build | 7 minutes, provisional p95 target 8 | 16-minute build cap; separate restore/build and slot timing |
| SchemaAndIdentity | p95 target 90 seconds | ScriptHarness ExecutionBudget=180 seconds, CleanupBudget=10 seconds, PostExitDrainBudget=10 seconds; enclosing TUnit timeout=210 seconds, row cap=4 minutes |
| CP-1 normal budget | 7 build + 3.5 method/cleanup allowance + 0.5 settlement = 11 minutes | Charged execution cap 20m, hard-wall cap 25m; record actual body separately from allowance |
| CP-C commission-only | 1 minute, provisional | No build; command row cap 1 minute. Review 09aa4107 measured 4.928 seconds execution (n=1, pwsh 7.5.4); separate Git/IO attribution and comparable p95 remain unknown. Collect under the required timing/cost Debug before repricing Mutation |
| Slot admission | Unknown; cost floor assumes zero wait | 5 minutes per acquisition; exit 4 is not-run, no unleased retry |

Build fixture definitions/import libraries once per harness invocation. Each
variant gets only a small owned scratch document/canary root; production functions
are exercised directly in the same PowerShell process. No Docker, DB, migrations,
web host, provider, HTTP readiness or expensive setup per case. R2 project
extraction considered: one small method does not justify another project; declare
all runtime script/schema/data dependencies explicitly instead.

Record exact method/category/variant membership, committed SHA, host/OS/runtime,
load/concurrency, setup warmth, TRX per-test durations and phase spans for slot,
build, schema load, fixture IO, parse/identity/hash, process startup/drain/cleanup,
and remainder in ignored `.antiphon/qualification-graph/<sha>/s1/<run>/`.
Instrument filesystem/CLI calls separately; unused service phases are not-used,
unavailable measurements are unknown, not invented zero. Individual operations
share the 180-second enclosing execution deadline and owned-process cancellation;
no unbounded waits, sleeps or retry loops. Use the first red/green and ordinary
Review observations for bounded timing collection under CARD-1514/1404/1531;
retain censored failures and do not buy repeated tests just to invent a percentile.
Revise timeouts toward 2x comparable p95 after enough samples. Any expected or
observed selection/batch/run over 30 minutes requires Debug before repetition.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-qgraph-s1/ | portable-contract | /*/Antiphon.Tests.Scripts/QualificationGraphContractTests/SchemaAndIdentity | V-1..9, D-3..5, R-3/4 | QualificationGraphContractTests.SchemaAndIdentity; exactly 1 passed, 0 failed/skipped; 112 named assertions | 1 | 11 |
| CP-C | S1-commission | n/a | commission-only-preservation | pwsh -NoProfile -File scripts/test-qualification-graph.ps1 -Case FullCensusUnchanged -ContextPath .antiphon/qgraph-s1-context.json | R-1/2, explicit seed-bound context | exit 0; QGRAPH FullCensusUnchanged preserved with exact commissioned seed/base/candidate; no test count | n/a | 1 |

CP-1 is the entire durable ordinary test selection: one build/filter row.
CP-C is a separate **explicit commission-only** command with no build, project,
filter or test count. Its distinct After selector prevents an ordinary `--after S1`
run from selecting it. It must never be added to the nightly/Unit lane. Default
portable placement currently resolves to server2; omit host/OS pins. No other
parent CP is authorized by this companion.
The baseline red uses **this same row at the committed test-first state** with
one expected named failure; its later green run supplies ordinary acceptance.
Preserve both outcomes and charge both. Internal assertion counts are not Min.

In Code, first import the **parent** table and confirm all eighteen rows survived
without executing them, as the re-check requires. Then import this companion to
get the two-row manifest: one durable S1 row plus one separately selected commission
row. This Docs amendment performs no tool build or test execution. Supported
bootstrap command (through the host slot gate):

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label qgraph-s1-import -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-qgraph-tool/ -- import --plan docs/superpowers/plans/2026-10-31-card-1514-qualification-run-graph-plan.md --out .antiphon/qgraph-parent.yaml
```

Use the emitted tool DLL directly for companion `import --plan <this-file>
--out .antiphon/qgraph-s1.yaml`; record its actual path/hash, do not trigger another
implicit build. Set only these scheduling/deadline pins in the ignored imported
YAML: `build.slotWaitMinutes: 5`, `timeouts.rowMinutes: 16`, CP-1
`timeoutMinutes: 4`, CP-C `timeoutMinutes: 1`, `parallel.maxRows: 1`.
Retain imported/resolved manifest
digests. The build default floor then allows 16 minutes, and row cap is four;
the importer's derived 33-minute row is not the performance policy.

Invoke that built tool with `run .antiphon/qgraph-s1.yaml --after S1
--expected-source-sha <committed-sha> --total-timeout 20m
--hard-wall-timeout 25m --serial`. It leases its own build/test slots; no enclosing
held build lease during the scheduler run. Wait the returned run to terminal
(repeat `wait` after exit 75, never settle a running child). Validate fresh receipts
against source and exact roster; preserve unedited CHECKPOINT/provenance lines.
When explicitly commissioned, write/pin the owned context before execution and
use the same tool/manifest with `--after S1-commission --expected-source-sha
<committed-sha> --total-timeout 2m --hard-wall-timeout 10m --serial` to select
CP-C only. The tool takes the host slot; a direct diagnostic command instead
goes through `scripts/build-slot.ps1`. Missing context refuses, never silently
skips or falls back. Ordinary and commission receipts remain separate; no
commission result substitutes for CP-1. Code/Review run
`scripts/check-evidence-diff.ps1` over actual base..HEAD.
Delete all owned `bin-qgraph-s1/` and `bin-qgraph-tool/` outputs after evidence
retention, including per-project directories. Generated evidence stays ignored.

### Cost

**Historical estimate, not measured:** CP-1 = 11 minutes (seven build, four
method/cleanup/settlement allowance); mandatory test-first RED = 11 minutes;
setup/bootstrap/import = 2 minutes. Original Code floor = **24 minutes**.
The amended commission adds CP-C = 1 minute: **25 minutes** planned Code machine
time, plus unknown admission waits;
75-105 minutes of step time above already includes those red/green runs rather
than charging them again. No Full/native run is charged or credited.

Historical PC floor (Mutation): **84 cycles** priced at the same exact method/filter.
Each required
three fresh forced builds/runs (baseline, red, restored green): estimate seven
minutes build + 1.5 minutes method + 0.5 minutes phase settlement = nine minutes
per phase. Add 0.5 minute apply/restore verification per cycle. Thus each cycle
costs **27.5 minutes**, and PC floor is **84 x 27.5 = 2,310 minutes**. Pack
authoring/binding and unknown admission waits are additional, unmeasured work.
No compiled-build reuse exemption or same-method mutation batching was assumed.

Historical total = setup 2 + ordinary V/R 11 + baseline RED 11 + PC floor 2,310
= **2,334 minutes**. Those original 50 PCs/84 cycles remain pending; preserving
that estimate does not pre-credit any execution or remove PC-43/44.

Amended provisional floor: **97 CP-1 cycles x 27.5 = 2,667.5 minutes**, plus
**nine CP-C cycles x 3.5 = 31.5 minutes** (three no-build phases at one minute
each plus 0.5 minute apply/restore). Mutation total = **2,699 minutes**; with
amended Code/setup floor 25, combined provisional total = **2,724 minutes**.
This is planning arithmetic, not a measured optimization or an executable pack;
binding, diagnostic direction controls and unknown admission waits are additional.
The full five-row m3 bypass has five separately bound cycles; its digest-only
diagnostic is additional reachability evidence, not a reduction in those cycles.

Ordinary Code and later Mutation are separate commissions. This aggregate requires a
**Debug timing/cost task before a multi-PC battery**, under R1/CARD-1514; individually
bounded cycles do not waive the aggregate rule. A later qualified optimization
may revise that floor through the owner contract. **Reprice the complete 58-PC/
106-cycle bound pack before SourceLanding commissioning**, including CP-C context,
policy-hash tooling, both-direction controls, actual phase costs and admission
bounds. The original timing/cost Debug obligation remains; no savings are credited
from the fix/review diagnostic probes.

Savings: one harness process shares immutable fixture/schema setup across all
ordinary variants; versus separately building for nine V groups at 11 minutes,
CP-1 saves **88 estimated minutes** (99 minus 11). Further PC savings are **0**:
same-method/file controls require separate cycles and fresh build provenance.
The unchanged Full proof avoids unrelated qualification but claims no numeric
Full saving without comparable measurements.
