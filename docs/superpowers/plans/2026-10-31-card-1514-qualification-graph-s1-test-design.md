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

This is a documentation deliverable. All tests, assertions and mutations below
are proposed, not executed evidence. Format follows the CARD-1545 S1b/S1c companions
on `origin/feat/card-task-6a22c243` and `origin/feat/card-task-8b284c8f`.

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
  -> checkpoint/cost rules. No native, service or live-supervisor inspection.

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
| `preservation.json` | Exact integrated pre-S1 source SHA; SHA-256 inventory of protected gate/legacy files and independent canonical policy projection, plus the specifically allowed new census row. This is a Code/Review slice preservation input, not a release receipt. Bind against the actual commissioned base, never ambient `HEAD` after edits. Future intentional policy/owner migrations require explicit reviewed rebinding. |

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

All rows use the single exact method/filter above and CP-1. `schema` means the
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
| V-9 | Inert library boundary and preservation. Import/call against owned scratch inputs, inventory source and scratch outputs before/after. | `no-side-effects`, `full-policy`, `gate-files`, `script-census`, `ascii`. Return values only; zero extra child/network/runner/gate calls, no input changes or receipt/state files. Independently pinned pre-S1 policy/files preserved except enumerated additive test census changes. MIXED: new inert API; existing Full contract regression. See R-1..4. |

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
post-implementation defects are bound by the PC inventory below. R-1/2 are
expected already green at the base, not fabricated new-feature reds.

### Guards the regression

- **R-1 Full policy/census unchanged:** `SchemaAndIdentity/full-policy` compares
  all pre-existing policy fields (including required suites, exclusions, test
  dispositions, costs and watchdogs) to the pinned pre-S1 projection. Permit only
  the exact new `argument-consumer`, non-wildcard harness census row and recomputed
  policyHash. Assert ten required RC suites from the actual pinned policy, including
  observability; stale nine-suite prose/old fixture counts are not authority.
  No existing C680 test/category/disposition changes. The single new TUnit UID is
  additive, so "unchanged" means conservation of every existing obligation, not
  an unchanged numeric total after adding a test.
- **R-2 Full gates/legacy entrypoints unchanged:** `SchemaAndIdentity/gate-files`
  compares SHA-256 bytes of `scripts/lib/release-{gate,authority,selection}.ps1`,
  `scripts/lib/nightly-{run-impl,tests-impl,failure-history}.ps1`, the legacy
  nightly entrypoints and supervisor-heartbeat library/script to pinned base
  bytes; include `nightly-dispatch.ps1` if present at the actual Code base.
  Inventory the existing files as well as hashes so omission cannot pass.
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

For R-1/R-2 Code binds the preservation manifest to its actual integrated base
before any edits; it never auto-updates from the candidate to satisfy a mismatch.
Unrelated legitimate landed changes require a documented rebind before the S1
commission, retaining old provenance. No Full suite or live run is needed to prove
this additive slice has no gate/legacy delta. No S1 result grants Full credit.
