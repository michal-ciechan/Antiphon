# CARD-1545 S1a baseline, inventory and S1c sanitization: test design

This companion appends verification detail to the [landed fix plan](2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md), inspected at
`4e1e6106d68b833347221df8f50c6346845913b2`. It does not rewrite that plan.
Scope is S1a-baseline, S1a and S1c-sanitization only. The two namespace/instruction
rot tests belong to S2a-1. D-3/D-4/D-5/D-6/D-9/D-11 remain PROVISIONAL.
No host, account, model, spend, reuse, signer or activation decision is assumed.
All fixtures are owned and provider-free; no live Claude or production session runs.

The verification IDs below are local to this companion; CP IDs retain the main
plan's meaning. All outcomes, reds and costs below are **planned, not measured**.
The seven new TUnit methods and two commands do not exist at the inspected base.

## Verification design

### Inspection

Bodies read before naming cases, including the nearest fixtures for new files:

- Main plan: first roster, portable caller witnesses, completed-artifact sanitizer,
  baseline preparation, commission-scoped census, performance and CP-1/20/25..30
  -> V-1..9. Reviews a62610b7, 917262f2 and 7973fc74 (including its paste paragraphs)
  -> R-1..7. S1b Final review 228e67c0 and run-graph review 4ba043c8 were read from
  their origin task branches -> independent negative cases and master-safe fixtures.
- S1b and S1c companion designs on origin/feat/card-task-6a22c243 and
  origin/feat/card-task-8b284c8f -> formatting, method-scoped PCs, explicit source
  binding and cost lessons. Their scopes are excluded, not recommissioned here.
- `scripts/lib/nightly-coverage.ps1`: discovery document parsing, producer,
  TestNodeUid parsing, default/profile dispositions and full census; and
  `nightly-policy.ps1` canonical payload/hash/load -> V-1..4. Census Digest excludes
  categories; comparing that digest alone is insufficient.
- `nightly-tests-impl.ps1` Invoke-NightlyOwnedProcess and
  `nightly-owned-process.ps1` child/owner bodies -> discovery transport below.
  The production owner refuses Linux; the existing controlled-I/O StartProcess
  seam is portable. No native custody claim follows from that seam.
- `tests/Antiphon.Tests/Scripts/NightlyDiscoveryParserTests.cs`, ScriptHarness,
  ScriptHarnessProcess launch/drain/cleanup, and `scripts/test-nightly-tests.ps1`
  discovery fixtures -> V-1..4. Nearest discovery golden is
  `scripts/fixtures/nightly/c487-probe/discovery.diag`; retain actual generated
  TestNodeUid, signature, every category and argument expansion, not reflection IDs.
- Full bodies of `ClaudeSubmitContractLiveTests.cs`, `ClaudeSubmitContractTests.cs`,
  `ClaudeRemoteControlAtStartupCanaryTests.cs`, `ClaudeRemoteControlMenuCanaryTests.cs`
  in `tests/Antiphon.Agents.Pty.Tests` -> V-1/2/5/6. The non-core One_Esc method is
  outside the roster; any shared helper changed by this slice remains in the sink audit.
- Full `tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs` and
  `ClaudeDialogCanaryDiagnostics.cs` -> V-1/7. Existing diagnostics redact credential
  prefixes/Bearer tokens but retain raw screens, bridge links and transcript content.
- Both test csproj files' references/shared inputs/staging and assembly-local
  ProcessSpawnLimit; `AppHostBrokerSourceGuardTests.cs` nonempty call-site checks
  -> caller-local builds and V-6. No test-executable reference between assemblies.
- `TrxReportTests.cs`, `scripts/fixtures/c585-green.trx` and
  `tests/Antiphon.Tests/Checkpoints/Fixtures/c723-failure-with-stack.trx` -> V-8/9:
  namespace-aware TestDefinitions/result testId joins and actual StdOut/ErrorInfo.
  `BuildStep.RunArguments` requests TRX but no Detailed console output.
- Testing/build checkpoint schema, process safety, mutation and delivery owners;
  test-performance standards and orchestration D-2 -> budgets and stage boundaries.

### Delivery inventory

No product message queue, session input sender, outbox or replay service changes.
Real-queue busy-recipient/already-eligible/crash/enqueue-recovery tests are excluded
because their transport is unchanged. These slices cannot credit prompt delivery:
that still requires the matching **complete UserPrompt** transcript in the separate
S1c-delivery/native work. A request, queue insert, event, Sent flag or ack is no proof.
If implementation changes that transport, return to Plan for producer-to-recipient
verification rather than expanding these commissions.

| Producer -> destination | Persistence boundary and durable identity | Recovery and observable receipt | Verification/substitute limit |
|---|---|---|---|
| Row-owned discovery child -> local roster witness -> fixture merger | Source/build SHA, assembly hash, project, run/row, format, expanded UID set; fresh discovery document and terminal producer state | Before-write, partial-write, failed child, missing completion and mismatched binding refuse; fresh run regenerates, never chooses latest. Consumer validates complete parsed inventory against its own assembly, then the wrapper validates terminal receipt before freezing | V-1/2/4; real compiled discovery, not execution of discovered tests or native custody |
| CP-25 inventory -> CP-1 combined manifest -> CP-30 | Same candidate/run, exact producer CP/build, export digest and terminal state; seed fixture is independently Git-bound | Pending/busy producer cannot satisfy consumer; already completed eligible producer is accepted once. Cut before export, between export and terminal state, and between state and consumer gives no preservation credit; restart uses a new context, no replay | V-1/4; actual metadata is recipient evidence, file existence alone is not |
| Canary-side success/assertion/skip/diagnostic APIs -> test console/TRX and diagnostic files | Run/case, exact test ID/UID, API-generated safe marker; closed host output under rows/CP-20 and rows/CP-26 | Intended fake exceptions are caught and their exact sanitized message forwarded; missing/partial sink output is invalid. Capture is sanitized before the first sink, including private diagnostic files | V-5/6/7/9; fake captures prove redaction/wiring, not provider behavior |
| Terminal CP-20/26 artifacts -> CP-29 scanner -> outer wrapper -> receipt/public summary | Owned context, source/build, checkpoint run, CP, exact UID, case marker and artifact paths/digests | Wait for completed producers. Cuts before host exit, before terminal state, during read/serialization or before final publication prevent green; a fresh run is required. CP-29 reads state and completed rows while its own run is active; outer wrapper validates final report after exit | V-8/9; real final TRX/HTML/console plus actual serializers, no synthetic ack substitution |

V-8 covers each handoff with a valid companion and one injected fault. It also
tests an already complete producer and a still-running producer. These are local
artifact recovery guards, not OS-death or queue recovery proofs. Reusing an old
complete artifact under a fresh run ID never recovers a partially completed run.

### Fixtures, binding and classification

Use master-compatible source, schema and small owned fixture builders. Do not copy
an rc-only preservation snapshot or pin a permanent test to this document's SHA.
At S1a Code commissioning record the actual master-based pre-S1 source in the owned
context. The Code branch must be based on master through the normal dispatcher;
do not rebase or switch an assigned branch to repair placement. New ordinary tests
must also pass on a landed master clone without old seed objects. SourceLanding's
historical CP-30 uses the reviewed pre-land source pack and its seed, not later HEAD.

The three inventory methods and four sanitizer methods are parameterless `[Test]`
methods with `Category("Unit")`. Matrices are internal cases, not argument-expanded
TUnit counts. Add the assembly-local ProcessSpawnLimit wherever a child is started;
run Antiphon.Tests and Pty.Tests sequentially. No database, AppHost, browser, ConPTY,
provider or network fixture is needed. Runtime scripts/source-audit files are explicit
inputs; ProjectReference closure alone does not cover them.

The inventory methods have a **local ordinary mode** and context-bound export mode.
In ordinary mode each validates its own freshly compiled inventory and manifest,
without a cross-assembly load, historical seed or export requirement. In a wrapper
commission, the context makes export mandatory; missing or invalid exports fail
CP-30. No-context local success earns no commission preservation evidence. Tests
must not become permanent Unit failures because a commission context is absent.
FullCensusUnchanged is only the command CP-30, never a permanent Unit method.

Discovery recipe: invoke the existing Invoke-NightlyProduceDiscovery against the
row's own built DLL (`dotnet` with AssemblyPath/ArgumentPrefix on Linux), with fresh
diagnostic/output directories and `--list-tests` only. On Linux use the existing
NightlySeams.StartProcess transport seam to launch and await the **real local built
child**, retaining its actual diagnostics/exit/drain through the owned test process
boundary. Do not feed canned diagnostics or fake success through that seam. Do not
call the Windows-only owner or change nightly readers to make Linux work. Restrict
this adapter to discovery of the recorded assembly and reject a run command/live
opt-in. The outer checkpoint owns the slot; this is a declared row-owned discovery
child, not another build, test-driver invocation or nested checkpoint lease.
Binding the adapter, launch result and exact assembly path is required in Code.

S1a-baseline first commits the untagged manifest/support and compilable roster tests.
If generated UID literals are not yet known, the first red capture retains discovery
but fails the explicit `manifest-uid-bound` assertion; never guess them or credit
that run. Bind actual expanded UIDs, rerun CP-27/28 green, then commit the merged
`tests/fixtures/live-lanes/full-discovery-before.json` in a seed commit. Provenance
records producing source/build SHAs (which precede the fixture commit), assembly
hashes and row evidence digests. The context externally binds the seed SHA and
fixture Git blob; the fixture cannot contain its own commit SHA. Verify the seed
range permits only enumerated support/fixture files and no policy/reader/category
changes. Raw discovery and receipts remain ignored; the intentional expected-input
fixture alone is versioned. CP-25 then CP-1 produce fresh tagged candidate metadata.

Before/after recipe for CP-30: separate pwsh processes and source roots; BEFORE
materialized from the context seed's Git objects (policy, common/policy/coverage
readers and literal dependencies, frozen fixture); AFTER uses actual candidate
policy/readers and fresh compiled metadata joined to the old population. Verify
ancestor seed, exact object/provenance and pre-S1 policy/reader equality. Never
choose HEAD~1, origin/master or a replacement seed at runtime. Do not manufacture
AFTER by adding expected tags to BEFORE. Independently recompute valid policy hashes,
reader digests, default-nightly and rc dispositions/reasons/owners and sorted UID
sets. Compare the full canonical census after allowing only six observed LiveClaude
additions. New contract UIDs require an explicitly enumerated additive required set;
an arbitrary unknown UID is not silently allowed. Removed/renamed old UIDs and changed
argument expansions fail. Later unrelated policy changes do not rerun this commission.

Sanitizer fixture: each raw capture contains separate synthetic secret, remote-control
URL, device-code and arbitrary screen-content sentinels, plus a structured safe case
field. No real secret/text is used. Markers are generated by the **sink API under
test alongside the payload**, and bound to run/case/UID. Harness-authored start lines
cannot satisfy them. Raw screen/transcript content stays in memory for behavioral
oracles; output is an allowlisted metadata projection with safe placeholders, not
merely a regex for known credentials. Include LF/CRLF, ANSI split tokens, OSC-8 link
targets, JSON escaping, XML entities and double-encoded representations. Scan raw
and decoded bytes; bound decoding and reject malformed/unsupported cases safely.

V-5 calls startup, submit and C514 **canary-side** success/assertion/skip APIs.
V-7 calls C680 and ClaudeDialogCanaryDiagnostics Composer/Draft/Observe/Write/Append,
including all generated screen/normalized-transcript/raw-transcript/state/launch files.
An owned fake sidecar and transcript select that diagnostic copy branch; do not touch
an ambient session. Catch only the deliberately injected fake assertion/SkipTestException,
check its message, then forward that exact message through Console's real test collector.
Unexpected exceptions or a real skipped live UID are never converted to a pass.
Presence must survive; suppressing the entire event is a failure.

V-7 also drives the actual wrapper receipt and public-summary serializers using those
captured safe events, writing separate files for CP-29. No independently synthesized
marker list is accepted as serialized evidence. An untrusted CheckpointRunId field
must be validated/projected before serialization; arbitrary exception/message fields
cannot leak through an otherwise safe schema. The wrapper stages safe output privately,
scans before public green, then validates final report/provenance and publishes.

In the commission, V-5 exports its actual sanitized events under the owned CP-20
result path. V-7 consumes that export only after validating the completed CP-20
row/source/build/run through the same artifact-admission library exercised by V-8;
it adds its own C680 events and serializes the combined case roster. Thus CP-29
can require both PTY and C680 markers in the receipt and summary. With no commission
context, the permanent V-7 Unit test uses locally generated safe API events to test
the serializers and never claims cross-row credit. In commission mode the missing
PTY export refuses; there is no fallback to the local fixture. The actual export,
TRX testId-to-definition/expanded-UID mapping and serializer symbols are binding
required after Code. Neither display-name matching nor a marker at run scope suffices.

Call-site audits in V-6/V-7 are bounded, method-aware token/AST checks, not whole-file
hashes or substring presence. Inspect every selected method and shared reachable helper;
each print/log/assert/skip/file sink must consume the exercised safe API result.
Missing file/method, unrecognized shape, dead helper call, ignored return, raw string
interpolation or Shouldly's implicit rendering of a raw actual value fails. Safe constant
messages are allowed. No assertion gains a raw screen operand just because its custom
message is sanitized. One_Esc is audited if a shared sink helper change reaches it,
but it is never tagged/enrolled. These audits prove wiring, not native runtime behavior.

V-8 uses owned fixture artifacts shaped after the inspected TRX goldens: exact
TestDefinitions IDs joined to result StdOut/ErrorInfo, nonzero counters, clean HTML,
normal banner/nonzero summary console, nested timestamp directory, terminal state and
build-source stamp. Positive serialized events come from the real APIs/serializers;
negative copies alter one property. The actual CP-29 scan is independent evidence
against the real TUnit writers, after both producers finish. It scans every file
recursively, not only known filenames. Console and HTML have **absence-only** content
checks; console also needs a nonzero execution summary. Neither needs a presence marker.
Marker presence belongs to each exact case/testId in TRX and to receipt AND summary.

Classification is explicit: roster/guard assertions are TUnit failures; valid internal
refusal cases pass their outer test only on the named refusal. CP-30 preservation or
admission refusal exits 2 with safe reason; CP-29 scanner refusal is
`2 INVALID/evidence-sanitization`. Missing, empty, unreadable, stale, foreign, zero-result
or nonterminal required evidence cannot be success. A sanitized successful scan never
relabels an earlier child Failed row, even if Setup is missing/unknown; original row
state/UID/raw exit remain recorded. No outage probe or setup classification is implemented
here. A fully passing terminal with raw child exit 1/2/3/8 also cannot publish green.

### Proves it works now

Exact closed roster follows. `A` is project `tests/Antiphon.Tests`, filter prefix
`/*/Antiphon.Tests.Scripts/`; `P` is `tests/Antiphon.Agents.Pty.Tests`, prefix
`/*/Antiphon.Agents.Pty.Tests/`. Concatenate the literal suffix; no method wildcards.
Assertion labels are `C1545 <V-id>/<case-id> <predicate>`; Code records the complete
case inventory and exact counts, never only a wildcard PASS-line count.

| ID | Layer / exact method or command | Recipe, variants, decisive oracle and reachability | Predicted current-base RED |
|---|---|---|---|
| V-1 | Compiled/script, A `LiveLaneInventoryTests/ExactCoreRoster` | Real local C680 discovery plus exact six-entry manifest. `valid`, `five`, `duplicate`, `renamed`, `unexpected`, `unbound-uid`, `foreign-project`; six exact expanded IDs, C680 locally resolved once. Export-mode `missing/partial/pending/wrong-source/wrong-build/wrong-run` PTY producer refuses before combined credit; healthy completed CP-25 accepted. Baseline mode does not consume absent CP-25. | Manifest/API absent: explicit bound-entry assertion; five-row manifest gives exact-set red, not missing-file exception |
| V-2 | Compiled/script, P `LiveClaudeInventoryTests/ExactPtyCoreRoster` | Own complete compiled discovery; exactly five PTY core UIDs (three startup, submit claude expansion, C514). `extra-argument`, `renamed`, `duplicate`, `wrong-assembly`; assert exact set/signature/arguments. Discovery `wrong-assembly-hash`, `nonzero-exit`, `missing-diag`, `partial-export`, `run-not-list` refuses; no provider starts. `valid` proves producer invoked and parsed real nodes. | Missing manifest binding assertion; expanded/renamed fixture mismatches set |
| V-3 | Source/inventory contract, A `LiveLaneInventoryTests/EveryLiveGateHasDisposition` | Scan both projects for ANTIPHON_HEADED_TESTS, ClSession.SkipIfNotEligible, RealCliStub and Explicit gates; resolve class/method/arguments to owned manifest disposition, owner and reason. `new-gate`, `missing-owner`, `missing-reason`, `duplicate-disposition`, `unknown-shape` refuse; ordinary non-gated test control accepted. Scratch source addition uses actual scanner, not a fabricated expected count. This inventory does not edit Full policy. | No gate inventory; injected unmapped gate fails named coverage assertion |
| V-4 | Git/script commission command `pwsh -NoProfile -File scripts/test-live-lane-manifest.ps1 -Case FullCensusUnchanged` | Recipe above. `valid` asserts full equality plus six observed category additions. `context-absent`, `object-missing`, `seed-nonancestor`, `fixture-blob`, `producer-provenance`, `prebase-drift`, `candidate-source/build/run`, `pending-producer` refuse. Scratch valid C680 rc exclusion with recomputed hash yields BOTH hash-changed and required-to-excluded diagnostics; `remove-Integration`, `tag-seventh`, `remove-old-uid`, `extra-expansion`, `reader-edit`, `reason-owner-edit`, `unknown-new-uid` fail independent predicates. All mutants act on actual candidate inputs, BEFORE fixed. | No command/binding/comparison; first compilable harness explicitly asserts command surface absent. After extraction, real changed-policy/category inputs must fail comparison; no malformed JSON or fake hash red |
| V-5 | Caller integration, P `LiveClaudeEvidenceWiringTests/EvidenceIsSanitized` | Every startup/control/off/slash, submit and C514 success/assertion/skip path takes sentinel capture into actual API. Assert paired marker present and all secret/link/device/screen payload absent in captured output/message. Encoding cases use same sink path; actual output forwarded into TUnit. `suppress-all` fails presence. | Existing raw screen interpolation leaks; explicit surface assertion only until behavior-preserving extraction exists |
| V-6 | Structural caller contract, P `LiveClaudeEvidenceWiringTests/CanarySinksCannotBypassSanitizer` | Audit selected live method bodies and reachable harness helpers. Healthy recognized calls count nonzero. Scratch raw console/assertion/skip, discarded safe return, dead helper call and raw implicit assertion actual each fails its exact sink predicate. No complete-file snapshot. | Startup LogEvidence/ARM SCREEN and skip/assert messages bypass sanitizer |
| V-7 | C680 caller + serializers, A `LiveLaneEvidenceTests/C680EvidenceIsSanitized` | Invoke real diagnostics including owned sidecar copy and success/assertion/skip adapters; assert per-path marker and absence before file/console sinks. Audit C680 call sites. Feed captured events through actual receipt and summary serializers; exact markers and no raw payload in both. `unsafe-run-id`, `exception-text`, raw file and suppressed marker cases are independently asserted. | Existing Redact preserves bridge URL, screen text and transcript text; serializers absent at base |
| V-8 | Scanner/wrapper integration, A `LiveLaneEvidenceTests/ArtifactGateRequiresPresence` | Real scanner over fixture tree: clean banner-only console passes. Independently remove marker from correct TRX testId, receipt, summary; move TRX marker to other ID/run scope; duplicate UID, empty case roster, missing/empty/unreadable artifacts, stale source/run/build, zero counters/summary, running producer refuse 2. Seed raw/decoded sentinel separately in console, TRX, HTML, nested file, receipt, summary; all refuse. Fake checkpoint adapter proves wait75, producer-complete consumption, no early report requirement, final report validation, scanner-before-publish, write failures, child-red and raw-exit preservation. Real validator/serializer/scanner, fake lifecycle only. | Scanner/wrapper surface absent; missing-presence and escaped-sentinel contracts have no guard |
| V-9 | Real completed artifacts, command `pwsh -NoProfile -File scripts/test-live-lane-evidence.ps1 -Case CompletedRows` | Only owned RunPortableRows context: terminal CP-20/26 + source/build receipts, exact four test results, nonempty marker-case roster. Join markers per exact V-5/V-7 testId and every case in serialized receipt/summary; V-6/V-8 still require actual executed results. Recursively scan all completed-row files; nonzero console summary, no console/HTML marker demand. CP-29 itself and unfinished final report are excluded until outer wrapper validates terminal report. | No gate; actual old sinks leave sentinels. Command must be reachable after host flush; reading its own active TRX is not a red witness |

The six intended live identities (UID strings are **binding required after Code**):

| Project | Namespace-qualified class | Exact method / argument expansion |
|---|---|---|
| Antiphon.Tests | Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests | C680_LiveNudgeRefusesPermissionAndQuestionDialogs |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.ClaudeSubmitContractLiveTests | Submitting_via_two_writes_completes_a_turn, exactly one `claude` expansion |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests | Control_auto_connects_a_bridge_session_before_the_first_user |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests | Settings_file_prevents_auto_connect_and_keeps_startup_records |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.ClaudeRemoteControlAtStartupCanaryTests | Remote_control_slash_command_still_arms_after_settings_off |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.ClaudeRemoteControlMenuCanaryTests | C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt |

No discovered live method executes in these portable commissions. The startup
control is mandatory roster membership even while D-11 remains provisional.

Current-base red procedure: commit compilable tests first, using explicit required
surface assertions/reflection where APIs do not yet exist, or behavior-preserving
extraction of existing private sink bodies. A build/import failure or zero tests
does not count as red. Each method needs the behavioral red named above once its
seam is reachable; a missing-API assertion alone does not establish adequacy. Commit
the fix before the same affected CP green run. Never put a permanent failing or
constant stub in the ordinary lane. V-4/V-9 commands record exact reason/exit and
source; their internal cases are not TUnit executions.

### Guards the regression

| ID | Regression / defect | Test and decisive assertion |
|---|---|---|
| R-1 | Five-result roster or source-only PTY inventory: R-D-1 roster premise, R2-D-1, 7973fc74 A-6 | V-1/2: compiled six identities, five PTY expansions, mandatory startup control and actual nightly producer; two independent builds |
| R-2 | Untagged/hidden gate escapes inventory: original A-3 and S1a | V-3: unmapped scratch gate refuses; V-4: all categories and observed six-tag delta, exact default/rc sets |
| R-3 | Self-compare/whole-file permanent freeze: R2-D-2, R3-D-1, 4ba043c8 D-1/2 | V-4: immutable Git BEFORE versus actual AFTER; valid C680 exclusion produces two diagnostics; no-context command refuses; ordinary Unit methods have no historical seed dependency; master-based fresh commission |
| R-4 | Sanitizer helper works but canary bypasses: R-D-7, R2-D-1/3 | V-5/6/7: actual caller sinks and structural bypass red, including C680 debug files and implicit assertion rendering |
| R-5 | Suppress-all or marker in wrong test masks lack of execution: R2-D-3 | V-5/7/8/9: API-emitted per-case marker survives, correct testId join and separate receipt/summary markers, nonempty case/UID roster |
| R-6 | Normal console cannot satisfy marker requirement or nested HTML leaks: R3-D-2 | V-8 clean banner-only console passes; V-9 scans every completed-row file for absence, including HTML/timestamp children; TRX has exact-ID presence |
| R-7 | Artifact success hides prior red or guard never reached: 228e67c0 D-1/2/3 and A-4 | V-8: failed rows with absent/unknown Setup stay non-green; unrelated row facts unchanged; fully passing terminal plus raw nonzero exit refuses; safe CheckpointRunId serialization in V-7. Every guard below has an otherwise-valid negative, no earlier rejection substitutes |

### Guard inventory

Each independently bypassable guard has a distinct PC. Case variants below execute
with every unrelated prerequisite valid, so an earlier refusal cannot mask a deleted
guard. Hash/disposition/category checks aggregate diagnostics before refusing: a
changed hash alone cannot satisfy the required disposition assertion. No guard is
exempt as redundant. Process custody supplied unchanged by existing helpers is an
excluded dependency, not newly qualified here.

| Guard | Plan reference / safety-critical invariant | Positive control |
|---|---|---|
| G-1 | S1a roster: Six unique manifest entries | PC-1 |
| G-2 | S1a local C680: C680 UID comes from this compiled assembly | PC-2 |
| G-3 | S1a local PTY: Exactly five locally compiled PTY core UIDs | PC-3 |
| G-4 | S1a argument expansion: Signatures/expanded arguments equal the frozen roster | PC-4 |
| G-5 | Baseline discovery: Actual built assembly/hash and real producer output | PC-5 |
| G-6 | Baseline child completion: Successful complete discovery child required | PC-6 |
| G-7 | Baseline discovery-only: Transport allows only owned assembly list-tests; no live admission | PC-7 |
| G-8 | S1a gate coverage: Every detected gate has a disposition | PC-8 |
| G-9 | S1a gate owner: Disposition has nonempty owner | PC-9 |
| G-10 | S1a gate reason: Disposition has nonempty reason | PC-10 |
| G-11 | S1a unique disposition: Exactly one disposition | PC-11 |
| G-12 | CP-30 context: Owned commission context required | PC-12 |
| G-13 | CP-30 lineage: Seed is ancestor of reviewed candidate | PC-13 |
| G-14 | CP-30 Git objects: Every seed object/dependency must exist | PC-14 |
| G-15 | CP-30 fixture identity: Seed fixture blob matches external context | PC-15 |
| G-16 | CP-30 producer provenance: Frozen fixture producing source/build SHAs and assembly hashes validated | PC-16 |
| G-17 | Baseline permitted delta: Seed range contains only inventory support/fixture edits | PC-17 |
| G-18 | Baseline protected inputs: Seed policy/readers equal commissioned pre-S1 source | PC-18 |
| G-19 | Candidate source binding: CP-1 rejects cross-source PTY export | PC-19 |
| G-20 | Candidate build binding: CP-1 rejects wrong-build PTY export | PC-20 |
| G-21 | Candidate run binding: CP-1 rejects another run's export | PC-21 |
| G-22 | Candidate completion: CP-1 rejects pending/partial producer export | PC-22 |
| G-23 | CP-30 actual AFTER: Fresh observed candidate categories, never seed plus expected tags | PC-23 |
| G-24 | CP-30 policy hash: Independent valid before/after policy hashes equal | PC-24 |
| G-25 | CP-30 reader bytes: All protected reader/dependency digests equal | PC-25 |
| G-26 | CP-30 rc membership: Old rc required/excluded UID sets preserved | PC-26 |
| G-27 | CP-30 nightly membership: Default nightly required/excluded sets preserved | PC-27 |
| G-28 | CP-30 reasons: Disposition reason is compared, not merely UID sets | PC-28 |
| G-29 | CP-30 owners: Disposition owner is compared independently | PC-29 |
| G-30 | CP-30 all categories: Only six observed LiveClaude additions permitted | PC-30 |
| G-31 | CP-30 old population: No removed/renamed old UID or changed expansion | PC-31 |
| G-32 | CP-30 additive population: New required contract UIDs are explicitly enumerated | PC-32 |
| G-33 | PTY remote links: Remote-control links removed before any sink | PC-33 |
| G-34 | PTY screen content: Arbitrary captured screen/transcript content removed | PC-34 |
| G-35 | PTY credentials: Credential-shaped values removed before output | PC-35 |
| G-36 | PTY device codes: Device/authorization codes removed before output | PC-36 |
| G-37 | PTY non-vacuity: API marker survives every success/assert/skip path | PC-37 |
| G-38 | PTY success wiring: Live success/log sinks consume safe result | PC-38 |
| G-39 | PTY assertion wiring: Assertion message sinks consume safe result | PC-39 |
| G-40 | PTY skip wiring: Skip exception sinks consume safe result | PC-40 |
| G-41 | PTY implicit rendering: Assertion actual values cannot render raw captured text | PC-41 |
| G-42 | C680 sink wiring: C680 diagnostics/canary bypasses fail audit | PC-42 |
| G-43 | C680 projection: Every diagnostic file sanitizes links/content before write | PC-43 |
| G-44 | C680 non-vacuity: Diagnostic API emits and retains per-path marker | PC-44 |
| G-45 | Receipt serializer: Receipt cannot copy raw payload fields | PC-45 |
| G-46 | Summary serializer: Public summary cannot copy raw payload fields | PC-46 |
| G-47 | Identifier sanitization: Untrusted CheckpointRunId cannot bypass projection | PC-47 |
| G-48 | Evidence context: Scanner requires owned exact context | PC-48 |
| G-49 | TRX presence/identity: Each marker must be under its exact case testId/UID | PC-49 |
| G-50 | Receipt presence: Every expected case appears in actual serialized receipt | PC-50 |
| G-51 | Summary presence: Every expected case appears in actual serialized public summary | PC-51 |
| G-52 | Nonempty roster: Empty expected-case roster cannot pass vacuously | PC-52 |
| G-53 | Required files: Missing required artifact refuses | PC-53 |
| G-54 | Nonempty artifacts: Empty required artifacts refuse | PC-54 |
| G-55 | Readable artifacts: Read/enumeration errors cannot mean clean | PC-55 |
| G-56 | Recursive coverage: All files under completed rows are scanned | PC-56 |
| G-57 | Decoded coverage: Encoded/ANSI/OSC sentinels cannot evade scan | PC-57 |
| G-58 | Positive clean artifact: Console/HTML need no presence markers | PC-58 |
| G-59 | TRX execution roster: Actual exact nonzero UID executions required | PC-59 |
| G-60 | Console execution: Console requires nonzero execution summary | PC-60 |
| G-61 | Evidence source binding: Completed artifacts match expected candidate source | PC-61 |
| G-62 | Evidence build binding: Artifacts match exact verified producing build | PC-62 |
| G-63 | Evidence run binding: Artifacts/markers match expected run and CP identity | PC-63 |
| G-64 | Evidence producer completion: No scan credit before both hosts/rows complete | PC-64 |
| G-65 | Active-run scan boundary: CP-29 excludes itself and does not demand final report early | PC-65 |
| G-66 | Publication ordering: Scanner validates before GREEN publication | PC-66 |
| G-67 | Final report binding: Outer wrapper validates full report after tool exit | PC-67 |
| G-68 | Wait/join identity: 75 waits same run to terminal with no early publish/resubmit | PC-68 |
| G-69 | Publication recovery: Context/read/serialization/publish failures prevent completed green | PC-69 |
| G-70 | Earlier child failure: Scanner success cannot erase an executed Failed row | PC-70 |
| G-71 | Raw child exit: Nonzero child exit cannot be green with all-passed artifacts | PC-71 |
| G-72 | Real artifact gate wiring: RunPortableRows and live wrapper call same mandatory final scanner | PC-72 |
| G-73 | S1a source recognition: An unrecognized live-gate source shape refuses inventory credit | PC-73 |

### Positive controls

These are post-land Mutation designs, not executed evidence. Code runs ordinary
V/R; Review judges ordinary evidence and these controls before land. Every PC must
run baseline / compiling break / named assertion-red / exact restore / green after
land. Zero tests, compile/import errors and wrong-fixture refusals are not red.
**Every payload is binding required after Code**: bind file/symbol/edit/hash, exact
assertion, source pack and filter before execution. No bound payload is claimed here.

In this table V-1/2/3/5/6/7/8 resolve to the one exact method filter in the V roster;
therefore each cycle runs one method, never the class. G-n maps only to PC-n.
For V-4 use only the exact FullCensusUnchanged command with its owned reviewed-source
context. That command PC retains its independently bound BEFORE and real candidate
scratch, not a comparator's fabricated expected side. Its refusal/diagnostic is the
red oracle; command exit 2 is not confused with a TUnit build failure.

| PC / guard | Break by this compiling defect | Exact method/command and case | Required red assertion |
|---|---|---|---|
| PC-1 / G-1 | Allow manifest Count=5 | V-1 / five | manifest.count == 6 and exact set equal |
| PC-2 / G-2 | Accept manifest C680 without local lookup | V-1 / renamed | localC680Matches == 1 |
| PC-3 / G-3 | Trust manifest PTY names without compiled join | V-2 / wrong-assembly | localPtyCoreSet == expectedFive |
| PC-4 / G-4 | Collapse nodes by method before comparing | V-2 / extra-argument | expandedUidSet has no extra argument |
| PC-5 / G-5 | Replace observed assembly hash with expected context value | V-2 / wrong-assembly-hash | observedAssemblyHash == boundBuildAssemblyHash |
| PC-6 / G-6 | Accept parsed diagnostics despite nonzero child exit | V-2 / nonzero-exit | producerAccepted == false |
| PC-7 / G-7 | Drop list-tests-only transport check | V-2 / run-not-list | launchCount == 0 |
| PC-8 / G-8 | Ignore unmatched gate nodes | V-3 / new-gate | unmappedGateCount == 0 |
| PC-9 / G-9 | Default empty owner as accepted | V-3 / missing-owner | invalidDispositionCount == 1 |
| PC-10 / G-10 | Drop reason validation | V-3 / missing-reason | invalidDispositionCount == 1 |
| PC-11 / G-11 | Take the first of two matching rows | V-3 / duplicate-disposition | ambiguousDispositionCount == 1 |
| PC-12 / G-12 | Fall back to HEAD when context absent | V-4 / context-absent | exit=2 context-required before comparison |
| PC-13 / G-13 | Skip merge-base ancestor predicate | V-4 / seed-nonancestor | exit=2 seed-not-ancestor |
| PC-14 / G-14 | Read candidate file when seed object absent | V-4 / object-missing | exit=2 seed-object-missing |
| PC-15 / G-15 | Trust fixture's self-stated digest | V-4 / fixture-blob | exit=2 fixture-binding |
| PC-16 / G-16 | Skip producing-build provenance comparison | V-4 / producer-provenance | exit=2 producer-provenance |
| PC-17 / G-17 | Ignore one unlisted changed file in seed range | V-4 / seed-unlisted-edit | exit=2 seed-range |
| PC-18 / G-18 | Omit protected-input equality check | V-4 / prebase-drift | exit=2 baseline-drift |
| PC-19 / G-19 | Ignore producer source SHA | V-1 / wrong-source | exportAccepted == false; reason=source-mismatch |
| PC-20 / G-20 | Ignore producer assembly/build identity | V-1 / wrong-build | exportAccepted == false; reason=build-mismatch |
| PC-21 / G-21 | Ignore producer run identity | V-1 / wrong-run | exportAccepted == false; reason=run-mismatch |
| PC-22 / G-22 | Accept export before terminal producer receipt | V-1 / pending | exportAccepted == false; reason=producer-incomplete |
| PC-23 / G-23 | Construct AFTER categories from seed plus LiveClaude | V-4 / remove-Integration | exit=2 category-delta |
| PC-24 / G-24 | Read candidate policy for BEFORE too | V-4 / c680-exclusion | diagnostics contains policy-hash-changed AND C680-required-to-excluded |
| PC-25 / G-25 | Drop changed reader from digest input list | V-4 / reader-edit | exit=2 reader-input-changed |
| PC-26 / G-26 | Skip rc disposition comparison but still compute hashes | V-4 / c680-exclusion | diagnostics contains C680-required-to-excluded as well as hash change |
| PC-27 / G-27 | Skip default-mode census comparison | V-4 / default-disposition | diagnostics contains nightly-disposition-changed |
| PC-28 / G-28 | Omit reason field in canonical comparison | V-4 / reason-edit | diagnostics contains disposition-reason-changed |
| PC-29 / G-29 | Omit owner field in canonical comparison | V-4 / owner-edit | diagnostics contains disposition-owner-changed |
| PC-30 / G-30 | Normalize away all category fields | V-4 / tag-seventh | exit=2 category-delta |
| PC-31 / G-31 | Intersect populations before comparison | V-4 / remove-old-uid | exit=2 old-membership-changed |
| PC-32 / G-32 | Allow every candidate-only UID | V-4 / unknown-new-uid | exit=2 undeclared-new-uid |
| PC-33 / G-33 | Make remote-link projection return raw URL | V-5 / link | payloadHasSentinel == false |
| PC-34 / G-34 | Copy raw screen into otherwise safe metadata | V-5 / screen | payloadHasScreenSentinel == false |
| PC-35 / G-35 | Bypass credential redaction for synthetic capture | V-5 / credential | payloadHasCredentialSentinel == false |
| PC-36 / G-36 | Preserve deviceCode in safe projection | V-5 / device | payloadHasDeviceSentinel == false |
| PC-37 / G-37 | Return empty event including marker | V-5 / suppress-all | actualSinkHasCaseMarker == true |
| PC-38 / G-38 | Restore raw SnapshotScreen console interpolation | V-6 / raw-console | unprotectedSuccessSinkCount == 0 |
| PC-39 / G-39 | Restore raw screen assertion message | V-6 / raw-assertion | unprotectedAssertionSinkCount == 0 |
| PC-40 / G-40 | Restore raw screen SkipTestException text | V-6 / raw-skip | unprotectedSkipSinkCount == 0 |
| PC-41 / G-41 | Use raw screen as Shouldly actual with safe custom message | V-6 / raw-actual | rawAssertionOperandCount == 0 |
| PC-42 / G-42 | Write original value instead of sanitized value at Write | V-7 / raw-file | unprotectedC680SinkCount == 0 and file sentinel absent |
| PC-43 / G-43 | Make shared sanitizer identity in diagnostics path | V-7 / diagnostics | allDiagnosticFilesSentinelFree == true |
| PC-44 / G-44 | Suppress C680 diagnostic event/marker entirely | V-7 / suppress-marker | allDiagnosticCasesHaveMarker == true |
| PC-45 / G-45 | Serialize original event instead of safe projection | V-7 / receipt | receiptSentinelFree == true |
| PC-46 / G-46 | Build summary from raw input event | V-7 / summary | summarySentinelFree == true |
| PC-47 / G-47 | Allow arbitrary CheckpointRunId string into serialized field | V-7 / unsafe-run-id | unsafeRunIdRejectedOrProjected == true |
| PC-48 / G-48 | Default missing context to ambient latest run | V-8 / context-absent | exit=2 evidence-sanitization; publicationCount=0 |
| PC-49 / G-49 | Search whole TRX text instead of joined result | V-8 / wrong-test-id | exit=2 evidence-sanitization |
| PC-50 / G-50 | Skip receipt marker validation | V-8 / receipt-marker-missing | exit=2 evidence-sanitization |
| PC-51 / G-51 | Skip summary marker validation | V-8 / summary-marker-missing | exit=2 evidence-sanitization |
| PC-52 / G-52 | Remove nonempty case-roster check | V-8 / zero-case | exit=2 evidence-sanitization |
| PC-53 / G-53 | Continue on missing required TRX | V-8 / missing-trx | exit=2 evidence-sanitization |
| PC-54 / G-54 | Treat zero-byte required artifact as clean | V-8 / empty-receipt | exit=2 evidence-sanitization |
| PC-55 / G-55 | Catch read error and continue scanning | V-8 / unreadable-file | exit=2 evidence-sanitization |
| PC-56 / G-56 | Replace recursive enumeration with top-level filenames | V-8 / nested-sentinel | exit=2 evidence-sanitization |
| PC-57 / G-57 | Scan only raw text and omit normalization/decoding | V-8 / encoded-sentinel | exit=2 evidence-sanitization |
| PC-58 / G-58 | Require marker text in Normal console or HTML | V-8 / banner-only | clean fixture exit == 0 |
| PC-59 / G-59 | Accept summary Passed with zero executed nodes | V-8 / zero-results | exit=2 evidence-sanitization |
| PC-60 / G-60 | Drop console nonzero-summary check | V-8 / zero-summary | exit=2 evidence-sanitization |
| PC-61 / G-61 | Ignore source SHA mismatch | V-8 / stale-source | exit=2 evidence-sanitization |
| PC-62 / G-62 | Ignore build-source stamp mismatch | V-8 / stale-build | exit=2 evidence-sanitization |
| PC-63 / G-63 | Ignore run/CP identity when accepting artifacts | V-8 / cross-run | exit=2 evidence-sanitization |
| PC-64 / G-64 | Treat Running row with files as terminal | V-8 / producer-running | exit=2 evidence-sanitization |
| PC-65 / G-65 | Require overall report.json while CP-29 is running | V-8 / report-not-yet-written | completed producer fixture accepted before overall report exists |
| PC-66 / G-66 | Publish green before invoking scanner | V-8 / scanner-red | publicationCount == 0 |
| PC-67 / G-67 | Skip terminal report/source validation | V-8 / final-report-mismatch | greenPublicationCount == 0 |
| PC-68 / G-68 | Treat first 75 as terminal success | V-8 / wait75 | starts == 1; publicationCountBeforeTerminal == 0 |
| PC-69 / G-69 | Swallow final write fault and retain green status | V-8 / write-fault | greenPublicationCount == 0; completedGreenFileAbsent |
| PC-70 / G-70 | Set verdict Green after clean scan regardless of child rows | V-8 / failed-missing-setup | failedRowRetained == true; greenPublicationCount == 0 |
| PC-71 / G-71 | Ignore raw child exit while accepting terminal Passed | V-8 / nonzero-exit | greenPublicationCount == 0 |
| PC-72 / G-72 | Remove post-row scanner invocation from wrapper | V-8 / scanner-not-called | scannerCallCount == 1 before any publish |
| PC-73 / G-73 | Ignore an unrecognized gate construct instead of recording unresolved coverage | V-3 / unknown-shape | unresolvedGateCount == 1; inventoryAccepted == false |

PC-24 and PC-26 additionally execute the main plan's **input mutation** cycle: edit
an owned actual candidate policy to add a valid C680 rc exclusion, recompute its
stated policyHash with its actual reader, run the exact command, require BOTH named
diagnostics, restore policy and require green. PC-23/30 similarly remove actual
Integration metadata or tag a seventh UID in the candidate scratch input, preserving
its provenance as an explicitly marked mutant. Do not mutate expected outputs.
SourceLanding keeps reviewed source/seed objects and metadata in its bound pack;
these command PCs use that reviewed history, not a later or rebased master checkout.
No snapshot commits/pushes or policy edits to the real branch occur during Mutation.

PC-33..37 each run V-5 with all startup/submit/C514 success/assertion/skip cases; the
identity-sanitizer control is PC-33 plus PC-34's required binding variant. They must
also scan **that exact method's completed** real TRX/console/HTML/files through the
same CompletedRows library, with a one-method marker roster and its actual serialized
receipt/summary. No CP-26 or unrelated markers are required in a method-scoped PC.
PC-43/44 do the same for V-7. The scanner must show sentinel-present or marker-missing;
a test output suppressed by Normal console never counts as missing TRX evidence.
These required post-run checks add no test host/build and have their own retained
exit/scan records. Ordinary CP-29 exercises both complete producer rows.

Variant binding rules: PC-38/39/40 bind at least one real call-site mutation per
startup, submit and C514 family (three cycles per PC); PC-42 binds C680 Console,
assertion message, skip message, Write and Append (five cycles). PC-57 binds JSON, XML, ANSI-split and OSC-8 decoding
separately (four cycles). PC-69 binds context write, artifact read, receipt write,
summary write and final publication faults separately (five cycles). PC-16 binds
producing source and producing build mismatches separately (two cycles). PC-71
binds raw exit 1/2/3/8 as internal ordinary cases; one guard-deletion cycle runs all
four with completely passing terminal artifacts. All other PCs have one cycle.
Independent field/sink guards added during Code require additional G/PC rows; do
not hide them in an uncounted variant. No untested guard is silently accepted.

CP-30's contract driver first checks the actual bound commission, then runs its
ordinary negative matrix against owned copies through the same comparison library.
Each negative checks the typed exit/reasons and comparison-stage observation; the
healthy companion reaches that same stage. The driver succeeds only when the actual
commission passes and all internal expected refusals occur. A guard-deletion mutant
therefore makes a named expected-refusal assertion fail (contract-driver exit 1).
An external candidate-input mutant instead makes the actual comparison refuse
(exit 2 with the required diagnostics). Keep those two kinds of red distinct.
Do not stop at the first changed hash: the policy/reader/category/disposition
diagnostics needed by independent PCs are computed before refusal.

For missing/empty/unreadable artifacts, bind the PC at the artifact validator's
actual acceptance boundary: the defect marks that artifact verified and bypasses
its dependent parse/marker checks. Deleting only an early check while a later parse
still refuses is not an adequate payload for that boundary. The negative case has
all other files, rows, markers and provenance valid. For individual marker/identity
PCs, every artifact is present, readable and well-formed. Record which boundary was
entered and the exact reason; no generic nonzero result is enough.

### Out of scope

- S1b admission/classifier implementation, S1c executable/profile/startup/delivery
  changes, native CP-4..7/21, S2+ fingerprints/reuse/signing/enforcement and both rot
  tests. S1c-sanitization integrates the existing wrapper's output boundary only.
- Full or rc qualification, master readiness, release credit, live prompts and
  credentials. Historical baseline discovery is not execution evidence. A safe
  artifact is not proof of delivery, provider availability or a successful test.
- Native custody qualification and host-death recovery; local test process seams
  cannot prove them. Production queue paths and their busy/eligible recipients are
  unchanged, so no real-queue test is substituted by an artifact test.
- Whole-file hashes as permanent regression tests. CP-30's historical inputs live
  only in its commission context. Unrelated future master edits are not regressions.
- A change to checkpoint console verbosity. Normal output is intentionally supported.

### Implementation commissions

Each row is a separate 30–60 minute **authoring** commission, excluding build/slot
time, required red/green runs and later Review/Mutation. Preserve the main plan's
order. Commit and push the failing witness before its fix, and every meaningful
slice before a long run. No source edits while a run is in flight.

| Commission | Authoring estimate | Concrete implementation steps and completion boundary |
|---|---:|---|
| S1a-baseline | 45–60 min | Add untagged manifest, manifest library/wrapper and the two local roster methods; bind real row-owned discovery adapter. Commit red witness, capture CP-27/28, bind six actual UIDs, fix and obtain green. Freeze merged expected-input fixture/provenance in a separate seed commit and push; externally bind that SHA. No policy/reader/tag edits. |
| S1a | 45–60 min | Add EveryLiveGateHasDisposition, exact six LiveClaude method tags and CP-30 command/fixture checks. Commit reachable red witnesses; add context/export validation and independent Git BEFORE/actual AFTER comparison; run CP-25 then CP-1 then CP-30. Retain actual candidate metadata and reviewed seed pack for later Mutation. |
| S1c-sanitization | 45–60 min | Add shared sanitizer, route existing selected canary/diagnostic sinks, implement the four caller/scanner tests and two script files, integrate actual wrapper serializers/post-run gate. Commit raw-sink/identity/suppress-all red witnesses before fixes. Run only CP-20/26/29 through RunPortableRows; preserve child facts and final report gate. |

The sanitization commission needs the separately owned S1b wrapper and S1c caller
seams to be present at its assigned source. If absent, retain the design and dispatch
after those dependencies land; do not reimplement them under this scope. If a seam
cannot expose the specified sink/recipient evidence, return `next: plan` with that
exact gap. No operator decision is needed for the provider-free fixtures.

### Performance budget

R1–R9 apply; all new estimates are provisional under CARD-1545/CARD-1404. Nearest
main-plan Antiphon.Tests build sample is 509.43 s (n=1); it is not a p95. No comparable
PTY portable sample exists. Collect the first three naturally commissioned runs,
then calibrate near 2x observed p95 without buying extra runs solely for statistics.
Keep failed history and rerun costs. Do not label a timeout allowance as a measurement.

Expected build allocations are 8 minutes for Antiphon.Tests and 5 for Pty.Tests.
Inventory discovery target is <=60 seconds per assembly, one capture per roster
method invocation, not per matrix case. Other method bodies target <=30 seconds;
read-only source and fixture setup is shared once per assembly. No DB/container or
web-host setup. Per-case temp roots are justified only for isolation/corruption
subjects. Commands target <=60 seconds each, with Git/file/process waits bounded
individually and cleanup <=15 seconds inside the command deadline.

Checkpoint timeout refinement uses the supported imported YAML, not new tool flags:
`build.slotWaitMinutes: 5`, `timeouts.rowMinutes: 16`, `parallel.maxRows: 1`,
TUnit `checkpoints[].timeoutMinutes: 3`, command rows `timeoutMinutes: 1`.
The existing build deadline floor is 16 minutes; record that as the CARD-1404
provisional exception for PTY instead of silently pretending the planned 12-minute
PTY cap is effective. The group total execution limit is 26 minutes, hard wall
29 minutes; it can truthfully stop a later row before both build maxima are spent.
The final-green groups estimate 16/17/17 minutes. A slot timeout is not run, never
permission to bypass the slot. No timeout is widened automatically after red.

Record per-test/variant source, run/CP, host/platform/category, selected/actual UID
roster, slot wait, restore/build, discovery, Git materialization, process startup,
exit/drain, file read/write/decode, body, scan/publication and teardown. Keep unknown
spans null plus remainder; infrastructure not used is N/A. Retain unedited CHECKPOINT
lines, TRX and phase logs in ignored evidence. No raw fixture text in refusal messages.
Any measured/expected run or batch >30 minutes requires Debug timing/cost analysis
with exact filters and evidence before repetition, including combined red+green and
the PC battery cost below. It does not authorize dropping cases or broad retesting.

### Checkpoints

This is the complete ordinary closed list for this companion. Import this file,
not similarly numbered rows in another companion. Eight rows, six isolated builds,
**nine TUnit executions, seven distinct methods, two commands**. Each row has one
build identity and one exact filter; Min counts executions only. Case loops and
discovered live UIDs are not executed tests. Both assemblies and all rows are serial.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-27 | S1a-baseline | tests/Antiphon.Tests -> bin-live-inventory-base/ | portable-live-c680-baseline | /*/Antiphon.Tests.Scripts/LiveLaneInventoryTests/ExactCoreRoster | V-1, R-1 | ExactCoreRoster; exactly 1 passed, 0 failed/skipped; actual complete local discovery retained | 1 | 10 | true |
| CP-28 | S1a-baseline | tests/Antiphon.Agents.Pty.Tests -> bin-live-pty-inventory-base/ | portable-live-pty-baseline | /*/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests/ExactPtyCoreRoster | V-2, R-1 | ExactPtyCoreRoster; exactly 1 passed, 0 failed/skipped; actual complete local discovery retained | 1 | 6 | true |
| CP-25 | S1a | tests/Antiphon.Agents.Pty.Tests -> bin-live-pty-inventory/ | portable-live-pty-inventory | /*/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests/ExactPtyCoreRoster | V-2, R-1 | ExactPtyCoreRoster; exactly 1 passed, 0 failed/skipped; bound fresh five-UID export | 1 | 6 | true |
| CP-1 | S1a | tests/Antiphon.Tests -> bin-live-inventory/ | portable-live-inventory | /*/Antiphon.Tests.Scripts/LiveLaneInventoryTests/* | V-1, V-3, R-1, R-2 | ExactCoreRoster,EveryLiveGateHasDisposition; exactly 2 passed, 0 failed/skipped; completed CP-25 consumed | 2 | 10 | true |
| CP-30 | S1a | n/a | portable-live-commission-census | pwsh -NoProfile -File scripts/test-live-lane-manifest.ps1 -Case FullCensusUnchanged | V-4, R-2, R-3 | exit 0; actual bound comparison and every named ordinary negative assertion pass; valid C680 exclusion/restated hash and category removal refuse in scratch; no-context refuses | n/a | 1 | true |
| CP-20 | S1c-sanitization | tests/Antiphon.Agents.Pty.Tests -> bin-live-pty-evidence/ | portable-live-pty-evidence | /*/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests/* | V-5, V-6, R-4, R-5 | EvidenceIsSanitized,CanarySinksCannotBypassSanitizer; exactly 2 passed, 0 failed/skipped | 2 | 6 | true |
| CP-26 | S1c-sanitization | tests/Antiphon.Tests -> bin-live-evidence/ | portable-live-c680-evidence | /*/Antiphon.Tests.Scripts/LiveLaneEvidenceTests/* | V-7, V-8, R-4..7 | C680EvidenceIsSanitized,ArtifactGateRequiresPresence; exactly 2 passed, 0 failed/skipped | 2 | 10 | true |
| CP-29 | S1c-sanitization | n/a | portable-live-completed-artifacts | pwsh -NoProfile -File scripts/test-live-lane-evidence.ps1 -Case CompletedRows | V-9, R-4..7 | exit 0; completed bound CP-20/26, exact-test TRX and receipt/summary case markers, recursive sentinel absence, nonzero console summaries | n/a | 1 | true |

Bootstrap/import the checkpoint tool through the host build-slot gate; charge it
separately. Example import (no test execution):

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label s1a-evidence-tool -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-s1a-evidence-tool/ -- import --plan docs/superpowers/plans/2026-10-31-card-1545-s1a-sanitization-test-design.md --out .antiphon/s1a-sanitization-checkpoints.yaml
```

Record the actual built tool DLL path/hash; subsequent wrapper invocations call that
DLL directly, without another implicit build or outer held slot. Apply only the
timeout/scheduling refinements above to the ignored imported manifest; retain its
digest. The manifest wrapper creates ANTIPHON_LIVE_PORTABLE_CONTEXT and runs just
S1a-baseline or S1a; the evidence wrapper's RunPortableRows creates
ANTIPHON_LIVE_EVIDENCE_CONTEXT and runs just S1c-sanitization. Bind expected committed
SHA, result root, run/row IDs, case roster and output paths, pass context only to the
child, and wait the same run until terminal (not 75). The wrapper itself takes no slot.
Use `--after <slice> --expected-source-sha <sha> --serial --total-timeout 26m
--hard-wall-timeout 29m` on the tool's manifest run. Direct CP-29/30 without context
refuses; neither can silently omit final validation.

Code must bind exact producer metadata/export paths against real checkpoint receipts,
not environment guesses. CP-27 precedes CP-28 as in the plan; only the post-group
merger requires both. CP-25 precedes consuming CP-1. CP-29 reads only completed
CP-20/26 state/CHECKPOINT/build-source and row files; after tool exit the outer wrapper
validates complete report/provenance. Keep every ordinary and expected-red receipt;
no final report is invented while its row is executing. Delete owned bin-* output
directories after evidence retention; generated TRX/JSON/HTML/logs remain ignored.
Code/Review run check-evidence-diff.ps1 over their actual base..HEAD.

### Cost

All figures are **estimated**, excluding authoring and unknown slot waits. Ordinary
V/R floor (Code) = sum of CP EstimatedMinutes = **10+6+6+10+1+6+10+1 = 50 minutes**:
three Antiphon.Tests builds x8 + three PTY builds x5 = 39 build minutes, plus 11
execution minutes. Floors per commission: baseline 16, S1a 17, sanitization 17.
Three tool setup/import allowances x2 = **6 minutes**. Required pre-fix red runs of
the same closed groups allocate another **50 minutes**, with every rerun charged.
Code estimated machine total = **6 + 50 red + 50 green = 106 minutes**.

PC floor (Mutation): **73 controls / 96 cycles**, including the explicitly counted
binding variants and additional input-mutation cycles above. Each TUnit cycle has
three fresh method-scoped builds/runs (baseline, red, restored green), 1 minute for
its method, 0.1 minute post-run artifact scan, and 0.7 minute total restore/binding
bookkeeping. Thus PTY = 3x(5+1+0.1)+0.7 = **19 minutes/cycle**; Antiphon.Tests =
3x(8+1+0.1)+0.7 = **28 minutes/cycle**. Command cycles have no build/discovery:
3x1 minute plus 0.5 restore/binding = **3.5 minutes/cycle**. Payload authoring and
additional Mutation tool setup remain unmeasured extra work, not free execution.

| Exact method filter / command | Cycles | Estimated PC minutes |
|---|---:|---:|
| /*/Antiphon.Tests.Scripts/LiveLaneInventoryTests/ExactCoreRoster | 6 | 168 |
| /*/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests/ExactPtyCoreRoster | 5 | 95 |
| /*/Antiphon.Tests.Scripts/LiveLaneInventoryTests/EveryLiveGateHasDisposition | 5 | 140 |
| pwsh -NoProfile -File scripts/test-live-lane-manifest.ps1 -Case FullCensusUnchanged | 22 | 77 |
| /*/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests/EvidenceIsSanitized | 6 | 114 |
| /*/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests/CanarySinksCannotBypassSanitizer | 10 | 190 |
| /*/Antiphon.Tests.Scripts/LiveLaneEvidenceTests/C680EvidenceIsSanitized | 10 | 280 |
| /*/Antiphon.Tests.Scripts/LiveLaneEvidenceTests/ArtifactGateRequiresPresence | 32 | 896 |

PC floor = **21 PTY cycles x19 + 53 Antiphon.Tests cycles x28 + 22 command cycles
x3.5 = 1,960 minutes**. Total machine floor including required pre-fix runs =
**6 setup + 39 ordinary builds + 11 ordinary V/R + 50 red + 1,960 PC = 2,066 minutes**.
Final-green Code plus Mutation subtotal, excluding pre-fix history, is 2,016 minutes.

**Debug timing/cost analysis is mandatory before commissioning the combined PC
battery or repeating combined red+green groups over 30 minutes.** Separate 16/17/17
minute final-green groups remain the Code closed lists. This large PC cost is explicit;
it cannot be hidden by calling each cycle a different batch. A later qualified timing
task may establish safe build reuse/batching; this design assumes none. Build reuse
savings = **0 minutes**, because baseline/tagging/sanitization have distinct source
identities and both caller assemblies must compile. PC reuse savings = **0 minutes**
until qualified; no numerical Full-suite savings are claimed without comparable data.

### Completion and handoff

Design audit: **guards=73, mapped=73, missing=0, duplicate PC maps=0**. All 73 PCs
and all 96 cycles are mandatory in post-land Mutation; **executed here=0**. Payloads,
actual UIDs, context schemas/paths, seam symbols and assertion bindings remain
**binding required after Code**, not silently promoted to executable packs. Before
Mutation handoff closes, all PCs must have break/red/restore/green evidence.

This task performs documentation inspection and static audits only: builds=0,
tests=0, ordinaryScopeCompleted=None; no execution/source-clean certificate claimed.
Next is Code: S1a baseline preparation then S1a tagging/comparison, and a separate
S1c-sanitization commission after its owned caller/wrapper prerequisites. The
provider-free work is already authorized; provisional operator decisions stay open.
