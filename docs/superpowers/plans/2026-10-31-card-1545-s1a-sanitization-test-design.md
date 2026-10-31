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

