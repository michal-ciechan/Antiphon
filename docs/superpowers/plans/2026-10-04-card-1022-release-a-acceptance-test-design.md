# CARD-1022 release A: exact-publication acceptance TestDesign

Date: 2026-10-04. TestDesign task: `5a2e33f1-4bfe-465c-a0e8-b7ffe81a9f61`.

This verification addendum to the
[successor plan](2026-10-04-card-1022-release-a-successor-plan.md) uses the
[acceptance investigation](../../investigations/2026-10-04-card-1022-release-a-acceptance.md).
It appends verification without changing the landed A/B/C fix design. Execute
**only CP-3 through CP-8, once, on Windows x64 at published L**: **95 results,
45 estimated minutes**. Setup, receipt audit and companion reconciliation bring
the bounded acceptance work to **59 estimated minutes**. No production or test
edits are commissioned. Mutation stays paused.

Preserve these identities:

- Publication owner: `b7c17822-63b4-4626-b5bb-525cfccc0cea`.
- Ordinary reviewed C: `65a78f3b1f88c213bbb4ac595681212476447130`;
  Review `71a5997b-d0d0-45a0-8699-84817a158fd5`, evidence
  `d6ed9bd1-1784-44ec-b432-b488ffb1bb47` (Clean, Final/Full).
- O: `8d0f6e2a-2aec-4204-8ca4-17fdae2deacc`;
  L = R = **`f11d2715f340c7caa03343862cb8dd73cc27f66b`**.
- Original card: `4b0d51f6-9b18-4409-b14e-586ecfa9ea3c`, Antiphon board
  `8988ca03-7414-47ad-b0b6-51556c701703`, project
  `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

The immutable [A freeze](2026-10-03-card-1022-modern-conpty-only-plan.md#verification-design)
remains **155 results**: accepted portable/Linux CP-1/2/9 = 60, Windows = 95;
blob `315e96ae3139d814925c7ff4022d258e70ba5860`. Initial Windows Debug at
`4e865e05d288374c0d61d8599080a5c399db5f4e` was **90 passed / 2 failed** of 92;
repaired CP-3/7 at C was **44/44**. The caller expressly accepted CP-4/5/6/8 at
the earlier SHA for ordinary Review. Preserve that acceptance. Combining receipts
or observing unchanged test bytes does not make their source fields equal L.
No complete six-row 95-result final-SHA Debug receipt was found. The separate
Debug obligation explicitly requires all six rows, so repeating only CP-3/7
cannot close it. This is the smallest selection satisfying that obligation.

## Verification design

### Inspection

Bodies below were read, including setup/helpers. Named A test/fixture and
production bytes are compared with L during this document's static validation.
V/R and G/PC numbers 1-73 retain the original freeze's meanings. V-5/R-7 and
G/PC-74/75 make the already-landed fixture repairs explicit; no added tests/counts.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| All `PtyBackendPolicyTests`, `PtyBackendContractTests`; policy Resolve/decision, `ConPtyRedistributable` | Windows null/empty/space/tab, aliases/trim/case/unknown/legacy; default/modern/unknown x neither/DLL-only/host-only/both; exclusive directory; instance null versus empty; Unix no locator; stable wire values -> retained V-1, R-1. Dummy files prove discovery only. |
| All `C1022BackendCapabilitiesTests` and World/logger, all `RunnerCapabilitiesTests`; `PtyBackendConfiguration`, runtime decision/custody/DescribeCapabilities and phone-home producer | Environment null/empty/space/modern/inbox x unset/opposite config; three decisions against opposite ambient; both producers; true/false/null/absent JSON and four CLI fields -> retained V-2. Network reconnection is unchanged. |
| All `C1022BackendLaunchTests`, LaunchAsync/DescribeBuild; host argv construction | Unset, configured modern, explicit modern over inherited inbox; loaded DLL, OpenConsole image, hashes, both DTOs and owned exit -> V-3. Test-runtime provenance is not deployed-daemon provenance. |
| Five `PtyBackendEnvGuard` hooks/tests; entire `ConPtyEnvironmentIsolationGuardTests`, IL traversal/fixtures | Independent inherited-selector clears; keyed/unkeyed, closures/lambdas/runtime parameters, nonempty census -> R-2. CP-8 starts no browser. |
| All `ModernPtyDa1Tests`, `Da1StartupResponderTests`, responder/native scanning sites | Startup/no leak/first query/lookalikes/split positions; whole marked bytes -> R-1. Parser results retained at C; native requalification at L. |
| All `C1022TypedInputTests`, ReadPrompts, `C1022InputMode`, `NodeStdinProbe`, `PtyInputChunkingTests` | Typed/paste x Node/FakeClaude; CRLF/Unicode/hash/clip armed; 1366/2320/5185/16384 bytes and blocked reader; string/text blocks, image exclusion, unfinished JSONL tail -> R-6, V-5/R-7. Paid-provider parity excluded. |
| Shadow-pair method and CreateFixture/Cleanup in `ShadowCopyStoreTests`, content closure; all `UnixPtyArgvTests` and NativeFixture/Placement/Journal | Both package files -> R-3. Copy fixtures are text, not native load. Retained Linux argv/NUL/containment/consumed-attempt evidence is CP-9. |
| All `PtyDeliveryCeilingsTests`, `SessionDeliveryProfileTests`, `GrokDeliveryShapeTests`, `TypedBodySpillTests`, stubs/temp helpers; production profile, ceilings, FitBriefForTyping, pointer rendering/spill Fit | Local modern versus old/unknown/null report; remote/Herdr uncertainty; measured/unmeasured kinds; UTF-8 threshold minus/equal/plus at 900/1024/43200/86400; long bound path, exact file and write failure -> R-4. |
| Entire `SessionQueueReceiptPlumbingTests`, PtyWorld/ForwardingClient/InsertFault/SeedReceiptTurnAsync; `SessionQueueTranscriptPump`; BridgeQueueHarness create/seed/insert/dispose, TestDbFixture front door; queue attempt/late-confirm/recovery/Enter and idle gate | Busy/eligible, six persistence cuts, stale-full/current-partial/current-full, pump and startup cleanup, overlay withholding -> V-4, V-5/R-7. No production restart proof inferred. |
| C1011 modern method in `RunnerGrokAdapterReadyTestsPty`; `DirectSessionRunnerClient`, `TestOwnedPtyHost` | Ready, no preprompt, exactly one complete native prompt; CARD-1020 captured identities/observed exits -> R-5. Real Grok and new custody/cleanup predicates excluded. |
| `PlanTableImporter`, `ManifestValidator`, Program import/selection; testing/build, runtime invariants, ADR 0002 and orchestration owners | First exact heading, escaped OR, executed-result floors, isolated builds, source binding, ordinary/Mutation/activation separation -> manifest/handoff. |

Missing setup is operational, not a missing test seam. This Linux mirror cannot
execute Windows native rows or inspect the desktop checkout. The execution task
needs Windows **and process x64**, .NET 9, PowerShell, Node on PATH, staged
`Microsoft.Windows.Console.ConPTY 1.24.260710001` pair, fakeclaude/fakegrok,
runner/host apphosts and the established PostgreSQL test fixture/Docker access.
Never substitute production DB. Both server rows set `TUNIT_MAX_PARALLEL_TESTS=1`;
keep assemblies and rows serial. Assembly guards receive inherited
`ANTIPHON_PTY_BACKEND=inbox`. Unset `ANTIPHON_CONPTY_DIR` and `CARD27_RUNTIME` in
the normal test lane, capturing/restoring caller values. Missing prerequisites,
wrong OS, skips and zero results mean incomplete qualification, never green.

No new tests/helpers are needed. Stale-full rejects freshness and current-partial
rejects completeness independently; current-full is covered by successful queue
cuts. Stale-partial is excluded because there is no special conjunction arm.
Missing pair/legacy/unknown combinations use policy/discovery tests without
spawning inbox. Corrupt binaries, other architectures, production restart/adoption
and real-provider behavior remain separately scoped. These exclusions do not
remove original PC argument rows/internal boundary variants from the pause ledger.

### Delivery inventory

Release A adds no producer/outbox but changes the backend of existing input paths.
Qualification cannot stop at a queue insert or a transport assertion.

| Producer -> destination | Durable identity / persistence | Recovery and observable receipt |
|---|---|---|
| SessionMessageQueueService -> AgentSessionRuntime -> DirectSessionRunnerClient/runtime -> owned PtyHost -> FakeClaude | Session GUID + queue GUID + attempt + LastDeliveryBaselineSequence; initial row commits before write, attempt/floor commits before input; native UUID joins normalization to stored transcript | `C475_QueueCommitAndTransportRecovery` (six argument results) and `C475_AlreadyIdleWhenIdleHasRecipientReceipt`: complete native-file and destination UserPrompt exactly once, beyond the floor; row/attempt identity. G/PC-52..60. |
| Pending queue -> busy then eligible recipient | Same GUID/body, attempts=0; initial TurnEnd=1 and AssistantText marks Working | `pending-before-flush`: Pending, no writes and no native/destination receipt while busy; recreate queue, TurnEnd, flush, same queue ID, whole receipt. Already-idle case proves no future turn-end is needed. G/PC-52. |
| Enqueue -> DB -> transport | Failed initial transaction leaves no row | `insert-fails`: exception, zero rows/writes/native/destination receipt; restore insert, retry, whole recipient prompt. G/PC-53. |
| Committed Sent attempt -> first body write | Same GUID, attempt=1 and baseline persisted before held write | `attempt-before-write`: cut before bytes and prohibit graceful revert; recreate queue, advance fixture clock two minutes; recovery charges exactly attempt=2, then whole receipt. G/PC-54/55. |
| Body -> separate Enter -> recipient | Committed attempt=1/floor survives before-Enter fault | `body-before-enter`: composer has body but native file empty; recover with CR only, same attempt/floor, Delivered, one whole native/destination prompt. G/PC-28..31/56. |
| Recipient JSONL -> production normalizer/test pump -> DB transcript | Session GUID + complete-line UUID + sequence beyond floor; commit parts before advancing consumed line | `recipient-before-ingestion`: stop pump, cut after Enter; native whole prompt exists, destination empty; restart pump/recreate queue, LateConfirmed without retyping and whole destination prompt. G/PC-57. |
| Committed transcript -> verdict save | Original queue ID/attempt/floor bind late confirmation | `receipt-before-verdict`: fail verdict save, keep complete receipt; recreate queue, LateConfirmed, no new input or attempt and exactly one whole receipt. G/PC-58. |
| Interrupted attempt -> stale/partial transcript recovery | Floor=1; stale full sequence=0, current partial sequence=2 then TurnEnd=3 | `C1022_Incomplete_or_stale_receipts_do_not_confirm`: stale full remains owed until new full prompt; current partial parks Pending/Truncated at cap, no input/fabricated receipt. Portable repair test shares seed. G/PC-59/60/75. |
| RunnerGrokAdapter -> owned modern runtime/host -> FakeGrok | Session GUID, private cwd/GROK_HOME and HEAD/TAIL nonce | C1011 modern method: Ready, no preprompt, one native UserPrompt exactly equals sent body. R-5; unchanged C1011 ready/lifecycle controls stay with its owner. |
| Runtime decision -> HTTP/phone-home DTOs -> profile | Runtime/store/build identity; no new durable message | Retained CP-2 and fresh CP-4 compare real producer objects and host; CP-6 checks downgrade. These are not network delivery or fleet activation evidence. G/PC-12..18,43..50,63..71. |
| Existing spill helper -> full file + pointer -> queue | Marker, bound relative path, exact bytes | CP-6 establishes file/shape/failure behavior, not receipt. Unchanged remote-spill recovery stays with its owner. G/PC-50/51/62/72/73. |

`C475_MultilineWritesKeepPasteMarkers` also requires literal normalized LF/Unicode,
both markers and a **later separate CR write**, followed by exact full native and
destination prompt. Partial-current's synthetic TurnEnd is essential: without it
the queue is Working and idle recovery is never exercised.

Substitutes and limits: FakeClaude/FakeGrok prove native submission/transcript,
not paid-provider behavior. Node proves bytes, not UserPrompt. The real queue/DB
has controlled cuts in ForwardingClient; Claude's test pump uses production
normalization but replaces production streaming/discovery. Queue recreation models
persisted crash states, not power loss. BridgeQueueHarness's fake adapter proves
negative recovery only. DirectSessionRunnerClient capabilities are synthetic;
CP-4 uses actual runtime and phone-home producers. Requests, queue inserts, events,
Sent flags and acks never prove delivery. Reject 95-green summaries without
matching complete recipient evidence; screen/composer presence alone is inadequate.

### Proves it works now

- V-1: selector/platform policy | retained Pty unit | CP-1 at C, 30/30 | eight
  policy methods and parser cases remain accepted, not relabelled L executions.
- V-2: composed decision/wire compatibility | retained runner | CP-2 at C, 12/12 |
  accepted ordinary evidence; fresh CP-4 corroborates Windows host/projections.
- V-3: actual owned modern host | native runner | CP-4, three launch methods plus
  guard | unset Requested empty; configured/override modern; ModernConPty,
  FellBack=false, Deprecated=false, runner/host product SHA contains L, loaded
  DLL belongs to observed OpenConsole pair, hashes agree and owned processes exit.
- V-4: queued modern input/recovery | native server | CP-7's 22 queue results |
  inventory cuts, busy/eligible, complete prompt once, persistent ID/attempt/floor.
- V-5: repaired oracles | CP-3/7 | two
  `ReadPrompts_preserves_string_and_text_block_bodies` results and
  `C1022_Partial_receipt_turn_parks_interrupted_attempt` | exact CRLF/Unicode body
  for both shapes; truncated current turn parks with no input. No added count.

### Guards the regression

- R-1: wrong package/host, stripped markers, DA1 stall | CP-3 contract/DA1 |
  pinned pair hashes, actual OpenConsole path, whole marked bytes, first output
  under unchanged 2.5 seconds, no leaked response, one query/answer. Portable
  parser's 22 results stay retained CP-1 evidence.
- R-2: inherited backend contamination | CP-3/4/5/7/8 and retained CP-9 |
  every assembly clears env, requires correct platform backend/no fallback;
  isolation scanner retains nonempty mutator census.
- R-3: missing shadow assets or Unix change | CP-5 and retained CP-9 |
  both copied files exist, unrelated DLL excluded; 17 Linux argv/NUL results
  plus guard remain accepted at C, never counted as Windows qualification.
- R-4: unsafe envelope/spill | CP-6's 44 results | 900/3000/1024 conservative,
  43200/14400/86400 modern; unmeasured kind inline zero; UTF-8 threshold boundaries,
  bound pointer <= ceiling, exact stored body, truthful write-failure outcome.
- R-5: modern C1011 ready/input behavior | CP-7 exact C1011 method, modern only |
  Ready, no preprompt, one complete native prompt equals sent body; captured
  process/observed-exit records retained. This does not satisfy C1011 WQ-4.
- R-6: typed control accidentally becomes paste | CP-3 typed/paste/chunk tests |
  independently expected byte hash, raw no markers/modeled chunk loss, paste
  both markers/whole body; no widened chunk or timing assertions.
- R-7: repaired fixtures regress | V-5 exact methods, PC-74/75 |
  prompt list equals original body; partial-current row Pending/Truncated,
  capped, baseline=1, adapter inputs empty. Native CP-7 remains mandatory.

### Guard inventory

G-1..73 below are retained from the A freeze, one distinct PC per independently
bypassable guard. D/S references in these rows name that freeze. They remain owed
when ordinary evidence is carried forward. G-74/75 add the two repaired evidence
guards. No executable acceptance or authorization gate is introduced by these
docs. Source/count/provenance/owner/activation review criteria are not invented
production predicates. Unchanged component exclusions are explicit below.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-2 Windows default modern | PC-1 |
| G-2 | D-2 modern aliases | PC-2 |
| G-3 | D-2 unknown selects modern with reason | PC-3 |
| G-4 | D-2 explicit legacy transition and deprecation | PC-4 |
| G-5 | D-2 missing DLL cannot advertise modern | PC-5 |
| G-6 | D-2 missing host cannot advertise modern | PC-6 |
| G-7 | D-2 custom directory is exclusive | PC-7 |
| G-8 | D-2 failed modern request remains observable | PC-8 |
| G-9 | D-3 Unix bypasses all Windows discovery | PC-9 |
| G-10 | D-3 stable enum values | PC-10 |
| G-11 | D-4 direct instance beats ambient | PC-11 |
| G-12 | D-4 daemon env beats config | PC-12 |
| G-13 | D-4 local capabilities use instance | PC-13 |
| G-14 | D-4 phone-home capabilities use instance | PC-14 |
| G-15 | D-4 custody uses instance backend | PC-15 |
| G-16 | D-5 absent deprecated field remains unknown | PC-16 |
| G-17 | D-5 preserve independent CLI observation fields | PC-17 |
| G-18 | D-4 effective request travels to host | PC-18 |
| G-19 | D-9 Pty assembly env isolation | PC-19 |
| G-20 | D-9 runner assembly env isolation | PC-20 |
| G-21 | D-9 host assembly env isolation | PC-21 |
| G-22 | D-9 server assembly env isolation | PC-22 |
| G-23 | D-9 E2E assembly env isolation | PC-23 |
| G-24 | S3 global locator mutators are serialized | PC-24 |
| G-25 | D-8 DLL package hash remains pinned | PC-25 |
| G-26 | D-8 host package hash remains pinned | PC-26 |
| G-27 | D-8 actual host matches selected pair | PC-27 |
| G-28 | D-9 opening bracketed-paste marker | PC-28 |
| G-29 | D-9 closing bracketed-paste marker | PC-29 |
| G-30 | D-9 normalize input to LF | PC-30 |
| G-31 | D-9 Enter is a distinct later write | PC-31 |
| G-32 | D-8 answer DA1 startup promptly | PC-32 |
| G-33 | D-8 DA1 response never leaks to child | PC-33 |
| G-34 | D-8 DA1 answers only first query | PC-34 |
| G-35 | D-8 no DA1 reply to lookalike | PC-35 |
| G-36 | D-8 DA1 handles split reads | PC-36 |
| G-37 | D-8 shadow copy retains DLL | PC-37 |
| G-38 | D-8 shadow copy retains EXE | PC-38 |
| G-39 | D-3 Unix exact argv survives Porta | PC-39 |
| G-40 | D-8 original Unix NUL blocks spawn | PC-40 |
| G-41 | D-8 post-containment Unix NUL blocks start intent | PC-41 |
| G-42 | D-8 containment rewriting is honored | PC-42 |
| G-43 | D-7 old runner forces conservative downgrade | PC-43 |
| G-44 | D-7 unknown reported backend never corroborates modern | PC-44 |
| G-45 | D-7 conservative numeric limits remain | PC-45 |
| G-46 | D-3 Unix does not acquire modern envelope | PC-46 |
| G-47 | D-7 phone-home stays conservative | PC-47 |
| G-48 | D-7 uncertain Herdr stays conservative | PC-48 |
| G-49 | D-7 unmeasured kinds always spill | PC-49 |
| G-50 | D-7 pointer fits after durable path binding | PC-50 |
| G-51 | D-7 spill preserves full body | PC-51 |
| G-52 | D-9 busy queue recipient must wait | PC-52 |
| G-53 | D-9 enqueue commit precedes delivery | PC-53 |
| G-54 | D-9 attempt and baseline committed before input | PC-54 |
| G-55 | D-9 interrupted unwritten attempt recovers | PC-55 |
| G-56 | D-9 composer-held body gets Enter-only recovery | PC-56 |
| G-57 | D-9 receipt-before-ingestion recovers without retyping | PC-57 |
| G-58 | D-9 committed receipt survives failed verdict save | PC-58 |
| G-59 | D-9 only complete receipt confirms | PC-59 |
| G-60 | D-9 receipt must follow attempt baseline | PC-60 |
| G-61 | S3 typed parity helper must send raw input intentionally | PC-61 |
| G-62 | D-7 pointer keeps correlation and join-safe path | PC-62 |
| G-63 | D-7 modern envelope remains bounded | PC-63 |
| G-64 | D-7 omitted profile stays conservative | PC-64 |
| G-65 | D-7 silent capability is not a contradictory report | PC-65 |
| G-66 | D-3 Unix must not call ConPTY locator | PC-66 |
| G-67 | D-8 original NUL precedes containment side effects | PC-67 |
| G-68 | D-8 refused tracked attempt remains consumed | PC-68 |
| G-69 | D-7 Grok inline narrowing remains zero | PC-69 |
| G-70 | D-7 unreachable Herdr stays conservative | PC-70 |
| G-71 | D-7 local conservative policy cannot be raised remotely | PC-71 |
| G-72 | D-7 fitting uses UTF-8 bytes, not UTF-16 chars | PC-72 |
| G-73 | D-7 failed write cannot claim stored spill | PC-73 |
| G-74 | V-5/R-7 repaired prompt reader preserves string and text-block bodies | PC-74 |
| G-75 | V-5/R-7 partial-current seed ends its synthetic turn so idle recovery is exercised | PC-75 |

### Positive controls

**Paused; none is part of the 59-minute acceptance execution.** Preserve PC-1..73,
all method argument rows/internal boundaries and missing-control discovery; append
PC-74/75. One representative compiling defect per decision-bearing behavior,
not one mutant per loop value. Independently bypassable checks retain separate
controls: two files, markers, projections, freshness/completeness. Ordinary green
or this design review discharges no PC.

When explicitly resumed after land/required activation, Mutation uses the managed
SourceLanding snapshot for O/L. Each Method cell expands to the exact filter
`/*/*/Class/Method`, named project and Min. Cycle: initial method green, compiling
break, intended assertion red, restore, fresh build, same method green. Code runs
V/R only; separate Review judges the pending controls. No assertion changes,
retry-to-green or suite/class filters. Build/fixture error, timeout or zero tests
is not red. PC-19..23 require inherited inbox in a fresh host; PC-24 changes test
scheduling metadata; PC-57/61/74/75 target test-owned oracles. Other edits target
production. Every named method now exists at L.

Policy/parser/capability controls are portable; Unix-argv requires Linux; native,
delivery and assembly-guard controls use Windows. PC-27 stages an isolated copy
of the approved DLL/EXE, validates both hashes first, loads that DLL at Spawn and
leaves the test's supplied expected path unchanged. Never substitute inbox or
modify deployed files. Shared-file controls run separately; retain every argument
result under its exact method filter and identify each decisive variant.

| PC | Break by this compiling defect | Project | Exact Method | Min | Expected red assertion |
|---|---|---|---|---:|---|
| PC-1 | Return InboxConhost for empty/default selector in production policy | Antiphon.Agents.Pty.Tests | `PtyBackendPolicyTests/Windows_defaults_request_modern` | 1 | default-modern |
| PC-2 | Map conpty to the legacy policy arm | Antiphon.Agents.Pty.Tests | `PtyBackendPolicyTests/Windows_modern_aliases_resolve_modern` | 1 | modern-alias |
| PC-3 | Map unknown nonempty selector to legacy policy arm | Antiphon.Agents.Pty.Tests | `PtyBackendPolicyTests/Windows_unknown_selector_warns_and_chooses_modern` | 1 | unknown-modern |
| PC-4 | Return Deprecated=false for explicit legacy selection | Antiphon.Agents.Pty.Tests | `PtyBackendPolicyTests/Windows_legacy_aliases_are_deprecated_without_fallback` | 1 | legacy-deprecated |
| PC-5 | In TryLocate bypass File.Exists(dll), retaining the host check | Antiphon.Agents.Pty.Tests | `PtyBackendContractTests/A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete` | 1 | host-only Backend must be InboxConhost |
| PC-6 | In TryLocate bypass File.Exists(host), retaining the DLL check | Antiphon.Agents.Pty.Tests | `PtyBackendContractTests/A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete` | 1 | DLL-only FellBack must be true |
| PC-7 | Append shipped default directory after the empty custom directory in ProbeDirectories | Antiphon.Agents.Pty.Tests | `PtyBackendContractTests/A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete` | 1 | empty exclusive override must fall back despite staged default pair |
| PC-8 | Force FellBack=false for resolved inbox after modern lookup failure | Antiphon.Agents.Pty.Tests | `PtyBackendPolicyTests/Windows_missing_pairs_fall_back_and_preserve_request` | 1 | pair-fallback |
| PC-9 | Return an InboxConhost decision in the non-Windows branch instead of UnixPty | Antiphon.Agents.Pty.Tests | `PtyBackendPolicyTests/Unix_ignores_windows_selectors_without_probing` | 1 | unix-no-probe |
| PC-10 | Assign UnixPty=0 and InboxConhost=2 explicitly, leaving all switches compiling | Antiphon.Agents.Pty.Tests | `PtyBackendPolicyTests/Enum_wire_values_remain_stable` | 1 | enum-stable |
| PC-11 | Reverse instance/ambient null-coalescing precedence in Resolve | Antiphon.Agents.Pty.Tests | `PtyBackendPolicyTests/Instance_request_outranks_environment` | 1 | instance-wins |
| PC-12 | Choose nonempty configured value ahead of nonempty environment in production composition | Antiphon.SessionRunner.Tests | `C1022BackendCapabilitiesTests/Daemon_environment_overrides_configuration` | 1 | daemon-precedence |
| PC-13 | Resolve ambient policy again inside DescribeCapabilities instead of instance decision | Antiphon.SessionRunner.Tests | `C1022BackendCapabilitiesTests/Local_capabilities_use_runtime_decision` | 1 | local-decision |
| PC-14 | Resolve ambient policy again in PhoneHomeRuntimeAdapter.Capabilities | Antiphon.SessionRunner.Tests | `C1022BackendCapabilitiesTests/Phone_home_capabilities_use_runtime_decision` | 1 | phone-decision |
| PC-15 | Advertise WindowsJob for the injected Windows legacy decision without the modern predicate | Antiphon.SessionRunner.Tests | `C1022BackendCapabilitiesTests/Custody_uses_runtime_decision` | 1 | custody-decision |
| PC-16 | Change optional nullable DTO default from null to false | Antiphon.SessionRunner.Tests | `C1022BackendCapabilitiesTests/Capability_json_preserves_nullable_deprecation_and_cli_observations` | 1 | deprecation-wire: old literal JSON remains null |
| PC-17 | Set CodexCliLauncherFingerprint to null in the local capability producer | Antiphon.SessionRunner.Tests | `C1022BackendCapabilitiesTests/Capability_json_preserves_nullable_deprecation_and_cli_observations` | 1 | cli-fields-retained |
| PC-18 | Pass conpty instead of modern in the explicit host argument; both resolve modern so no inbox starts | Antiphon.SessionRunner.Tests | `C1022BackendLaunchTests/Declared_modern_overrides_inherited_inbox` | 1 | host-request: exact requested modern |
| PC-19 | Remove SetEnvironmentVariable(..., null) from Pty test assembly hook | Antiphon.Agents.Pty.Tests | `PtyBackendEnvGuardTests/The_suite_ignores_an_inherited_pty_backend` | 1 | environment must be null |
| PC-20 | Remove environment clear from SessionRunner test assembly hook | Antiphon.SessionRunner.Tests | `PtyBackendEnvGuardTests/The_suite_ignores_an_inherited_pty_backend` | 1 | environment must be null |
| PC-21 | Remove environment clear from PtyHost test assembly hook | Antiphon.PtyHost.Tests | `PtyBackendEnvGuardTests/The_suite_ignores_an_inherited_pty_backend` | 1 | environment must be null |
| PC-22 | Remove environment clear from server Tests/TestHelpers hook | Antiphon.Tests | `PtyBackendEnvGuardTests/The_suite_ignores_an_inherited_pty_backend` | 1 | environment must be null |
| PC-23 | Remove environment clear from E2E/Fixtures hook | Antiphon.E2E | `PtyBackendEnvGuardTests/The_suite_ignores_an_inherited_pty_backend` | 1 | environment must be null |
| PC-24 | Remove unkeyed NotInParallel from incomplete-pair method, retaining the class Headed key | Antiphon.Agents.Pty.Tests | `ConPtyEnvironmentIsolationGuardTests/ConPty_environment_mutators_are_unkeyed_not_in_parallel` | 1 | conpty-env-mutators-are-unkeyed-not-in-parallel |
| PC-25 | Change one hex digit of ConPtyDllSha256 constant | Antiphon.Agents.Pty.Tests | `PtyBackendContractTests/The_shipped_binaries_are_the_ones_with_recorded_provenance` | 1 | ok must be true |
| PC-26 | Change one hex digit of OpenConsoleSha256 constant | Antiphon.Agents.Pty.Tests | `PtyBackendContractTests/The_shipped_binaries_are_the_ones_with_recorded_provenance` | 1 | ok must be true |
| PC-27 | At ModernConPtyConnection.Spawn load an isolated duplicate of the same approved modern pair instead of the supplied DLL path | Antiphon.Agents.Pty.Tests | `PtyBackendContractTests/Asking_for_the_modern_backend_runs_the_child_under_our_own_OpenConsole` | 1 | hosts must contain the original expected OpenConsole path; setup proves duplicate pair is valid |
| PC-28 | Omit PasteStart in WrapIfMultiline, retaining body and PasteEnd | Antiphon.Agents.Pty.Tests | `PtyBackendContractTests/The_production_write_path_delivers_the_markers_on_the_modern_backend` | 1 | HasPasteStart must be true |
| PC-29 | Omit PasteEnd in WrapIfMultiline, retaining PasteStart and body | Antiphon.Agents.Pty.Tests | `PtyBackendContractTests/The_production_write_path_delivers_the_markers_on_the_modern_backend` | 1 | HasPasteEnd must be true |
| PC-30 | Return body.TrimEnd() without ReplaceLineEndings in NormalizeBody | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C475_MultilineWritesKeepPasteMarkers` | 1 | exact forwarded body has LF and no CR within markers |
| PC-31 | Concatenate CR to the encoded body at queue submit and suppress the later Enter call | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C475_MultilineWritesKeepPasteMarkers` | 1 | Payloads contains a separate CR after the body |
| PC-32 | Omit responder Scan from the modern connection reader | Antiphon.Agents.Pty.Tests | `ModernPtyDa1Tests/Modern_child_first_output_arrives_without_the_da1_stall` | 1 | elapsed must be less than 2.5 seconds, after child marker appears |
| PC-33 | Remove ESC from the actual DA1 reply bytes, keeping write and responder | Antiphon.Agents.Pty.Tests | `ModernPtyDa1Tests/The_da1_reply_is_consumed_and_does_not_leak_to_the_child` | 1 | screen must not contain ?1;0c or is not recognized |
| PC-34 | Remove if (_fired) return from OnQuery | Antiphon.Agents.Pty.Tests | `Da1StartupResponderTests/A_second_query_is_counted_but_never_answered` | 1 | replies must equal 1 |
| PC-35 | Accept every CSI final c regardless of usable parameters/query predicate | Antiphon.Agents.Pty.Tests | `Da1StartupResponderTests/A_lookalike_never_fires` | 13 | replies must equal 0 for DA2/private/multiparameter rows |
| PC-36 | Reset parser state to Ground at the beginning of each Scan | Antiphon.Agents.Pty.Tests | `Da1StartupResponderTests/A_query_split_at_every_byte_boundary_still_fires_once` | 2 | replies must equal 1 at interior split |
| PC-37 | Reject conpty.dll in shadow-copy content selection | Antiphon.PtyHost.Tests | `ShadowCopyStoreTests/Shipped_conpty_binaries_survive_the_deps_json_closure_filter` | 1 | copied conpty.dll must exist |
| PC-38 | Reject OpenConsole.exe in shadow-copy content selection | Antiphon.PtyHost.Tests | `ShadowCopyStoreTests/Shipped_conpty_binaries_survive_the_deps_json_closure_filter` | 1 | copied OpenConsole.exe must exist |
| PC-39 | Drop empty arguments from the Unix options vector before Porta spawn | Antiphon.Agents.Pty.Tests | `UnixPtyArgvTests/Native_argv_is_verbatim` | 9 | native-argv-exact for empty and trailing sentinel |
| PC-40 | Have UnixPtyArgvGuard.VerifyOrThrow return without validation | Antiphon.Agents.Pty.Tests | `UnixPtyArgvTests/Nul_is_refused_before_native_spawn` | 4 | Should.Throw UnixPtyArgvException before the native launch |
| PC-41 | Remove final argv NUL validation after Place but before RecordStartIntent | Antiphon.Agents.Pty.Tests | `UnixPtyArgvTests/Containment_introduced_nul_is_refused_before_start_intent` | 2 | final-nul-before-start-intent (journal remains empty) |
| PC-42 | Discard Place returned command vector and pass original vector to Porta | Antiphon.Agents.Pty.Tests | `UnixPtyArgvTests/Tracked_argv_is_verbatim_after_containment` | 1 | tracked-native-argv-exact includes placed-sentinel |
| PC-43 | Treat reported InboxConhost as modern corroboration in PtyDeliveryProfile | Antiphon.Tests | `PtyDeliveryCeilingsTests/A_runner_on_the_inbox_conhost_downgrades_a_modern_server` | 1 | Backend must be InboxConhost and inline must be 900 |
| PC-44 | Treat any nonempty reported backend as modern corroboration | Antiphon.Tests | `PtyDeliveryCeilingsTests/Unknown_runner_backend_downgrades_modern_server` | 1 | unknown runner yields conservative profile |
| PC-45 | Return modern ceiling tuple for the explicit InboxConhost mapping | Antiphon.Tests | `PtyDeliveryCeilingsTests/The_inbox_backend_keeps_exactly_the_ceilings_that_shipped` | 1 | 900/3000/1024 and IsPastePath false |
| PC-46 | Map UnixPty to modern tuple in DelegationSettings.CeilingsFor | Antiphon.Tests | `PtyDeliveryCeilingsTests/Unix_backend_keeps_conservative_limits` | 1 | Unix limits equal 900/3000/1024 |
| PC-47 | Bypass RunnerId/RunnerCwd conservative branch and use local modern profile | Antiphon.Tests | `SessionDeliveryProfileTests/Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind` | 1 | SingleWriteMaxBytes must be 1024 |
| PC-48 | Return Herdr ceilings when capability omits Herdr | Antiphon.Tests | `SessionDeliveryProfileTests/Herdr_snapshot_but_runner_without_herdr_downgrades_to_inbox` | 1 | Backend InboxConhost, SingleWriteMaxBytes 1024 |
| PC-49 | Return false from RequiresJoinSafeDelivery for Codex | Antiphon.Tests | `GrokDeliveryShapeTests/An_unmeasured_kind_is_join_safe_by_default` | 3 | RequiresJoinSafeDelivery true and inline zero |
| PC-50 | Measure pre-binding pointer only in FitBriefForTyping, omitting bound-path recheck | Antiphon.Tests | `PtyDeliveryCeilingsTests/A_runner_pointer_is_measured_after_the_queue_expands_its_spill_path` | 1 | typed UTF-8 bytes must be <= SingleWriteMaxBytes |
| PC-51 | Write request body without its first character in TypedBodySpill.Fit | Antiphon.Tests | `TypedBodySpillTests/Over_the_ceiling_writes_the_original_and_returns_a_short_pointer` | 1 | File.ReadAllText(path) must equal body |
| PC-52 | Force working-state check false on ordinary WhenIdle flush | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C475_QueueCommitAndTransportRecovery` | 6 | pending-before-flush: Pending and no input/receipt before TurnEnd |
| PC-53 | Insert await _runtime.SendInputAsync(sessionId, row.Body, ct) immediately before the initial queue SaveChanges | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C475_QueueCommitAndTransportRecovery` | 6 | insert-fails: input and both receipt lists remain empty |
| PC-54 | Move the attempt/baseline SaveChanges after the body transport call | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C475_QueueCommitAndTransportRecovery` | 6 | attempt-before-write: persisted Sent/attempt=1/baseline assertion |
| PC-55 | Return Nothing for interrupted Sent runs at RecoverDeliveryRunLockedAsync entry | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C475_QueueCommitAndTransportRecovery` | 6 | attempt-before-write: recovered DeliveryAttempts must be 2 |
| PC-56 | In composer-present recovery resend body before pressing Enter | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C475_QueueCommitAndTransportRecovery` | 6 | body-before-enter: post-recovery Writes must equal CR only |
| PC-57 | In SessionQueueTranscriptPump skip normalizing complete user lines while still ingesting turn-end lines | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C475_QueueCommitAndTransportRecovery` | 6 | recipient-before-ingestion: recipient-receipt must find the complete destination UserPrompt |
| PC-58 | In RecoverDeliveryRunLockedAsync, after late.Confirmed > 0, send ReconstructRunBody(run) again through _runtime.SendInputAsync before returning LateConfirmed | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C475_QueueCommitAndTransportRecovery` | 6 | receipt-before-verdict: no new Writes, same baseline and attempt |
| PC-59 | Bypass !match.Complete branch in LateConfirmAttemptedMessagesAsync | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C1022_Incomplete_or_stale_receipts_do_not_confirm` | 1 | partial-current case must remain unconfirmed/parked, never LateConfirmed |
| PC-60 | Remove sequence > stored baseline predicate from late-confirm transcript query | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C1022_Incomplete_or_stale_receipts_do_not_confirm` | 1 | stale-full case remains owed until a new matching UserPrompt |
| PC-61 | Have shared typed-mode helper call EncodeBody instead of NormalizeBody | Antiphon.Agents.Pty.Tests | `C1022TypedInputTests/Typed_and_paste_modes_reach_the_modern_peer_distinctly` | 1 | typed-no-markers |
| PC-62 | Remove task marker prefix from BuildBriefPointer for joining kinds | Antiphon.Tests | `GrokDeliveryShapeTests/A_join_safe_pointer_is_already_one_line_and_keeps_the_path_delimited` | 1 | pointer must start with marker |
| PC-63 | Increase ModernPtySingleWriteMaxBytes default from 86400 to 86401 | Antiphon.Tests | `PtyDeliveryCeilingsTests/No_modern_ceiling_exceeds_the_measured_envelope` | 1 | SingleWriteMaxBytes must equal measured 86400 |
| PC-64 | Use modern ceilings when FitBriefForTyping receives no profile | Antiphon.Tests | `PtyDeliveryCeilingsTests/A_caller_with_no_profile_gets_the_conservative_ceilings` | 1 | pointer headline must be present |
| PC-65 | Use conservative backend instead of _local.Backend when capabilities is null | Antiphon.Tests | `PtyDeliveryCeilingsTests/A_silent_runner_leaves_this_processes_own_decision_standing` | 1 | silentModern remains ModernConPty |
| PC-66 | Invoke the injected locator once in the Unix branch, ignore its result and still return UnixPty | Antiphon.Agents.Pty.Tests | `PtyBackendPolicyTests/Unix_ignores_windows_selectors_without_probing` | 1 | unix-locator-calls must equal 0 |
| PC-67 | Move original-vector validation after CustodyContainment.Place | Antiphon.Agents.Pty.Tests | `UnixPtyArgvTests/Original_nul_is_refused_before_containment_and_consumes_attempt` | 1 | original-nul-before-placement |
| PC-68 | Reset the tracked-launch attempt flag in the failed-launch finally block | Antiphon.Agents.Pty.Tests | `UnixPtyArgvTests/Original_nul_is_refused_before_containment_and_consumes_attempt` | 1 | second launch must throw InvalidOperationException before placement |
| PC-69 | Return the unchanged ceiling record for Grok in ForAgentKind | Antiphon.Tests | `GrokDeliveryShapeTests/Narrowing_for_a_joining_composer_touches_the_inline_ceiling_and_nothing_else` | 2 | BriefInlineMaxBytes must be 0 |
| PC-70 | Return Herdr limits on null capability in the Herdr branch | Antiphon.Tests | `SessionDeliveryProfileTests/Herdr_snapshot_with_unreachable_runner_uses_conservative_inbox` | 1 | Backend must be InboxConhost |
| PC-71 | Bypass local-nonmodern early return in ProbeAsync and assign ModernConPty in the agreeing-runner branch | Antiphon.Tests | `PtyDeliveryCeilingsTests/An_inbox_server_is_not_raised_by_a_modern_runner` | 1 | Backend must remain InboxConhost |
| PC-72 | Use Body.Length instead of UTF8.GetByteCount for the Fit ceiling comparison | Antiphon.Tests | `TypedBodySpillTests/Under_the_ceiling_returns_the_original_and_writes_no_file` | 1 | UTF-8 threshold-plus case must spill while equal case remains inline |
| PC-73 | Return a spilled pointer from the IOException catch without a file or API fallback | Antiphon.Tests | `TypedBodySpillTests/Write_failure_returns_the_original` | 1 | Spilled must be false and ToType must equal original body |
| PC-74 | In C1022TypedInputTests.ReadPrompts return `string.Empty` for JsonValueKind.String, leaving text-block concatenation intact | Antiphon.Agents.Pty.Tests | `C1022TypedInputTests/ReadPrompts_preserves_string_and_text_block_bodies` | 2 | false/string argument: `ReadPrompts(path).ShouldBe([body])` fails by value, not JSON/fixture exception |
| PC-75 | In SeedReceiptTurnAsync omit the `if (!stale)` TurnEnd insertion, retaining UserPrompt and SaveChanges | Antiphon.Tests | `SessionQueueReceiptPlumbingTests/C1022_Partial_receipt_turn_parks_interrupted_attempt` | 1 | `parked.Status.ShouldBe(Pending, "partial-current receipt must park")` fails with Sent because idle recovery never reaches the truncated receipt |

Compiling sites at L clarify the inherited recipes: `PtyBackendDecision.Deprecated`
and `FellBack` are computed properties (PC-4/8); PC-15 targets
`SessionRunnerRuntime.DetectCustodyBackend`; PC-18 changes the host request argument
from modern to conpty. PC-53 inserts an awaited early send after row construction
before its initial save; PC-54 moves the normal delivery attempt SaveChanges after
transport. PC-31 appends CR at the normal body send and suppresses only its later
Enter; Payloads assertions run before any delivery error is rethrown. PC-56 sends
a reconstructed body before CR in EnterOnlyConfirmLockedAsync. PC-58 sends body
in the late-confirm return arm. PC-60 removes the floor predicate inside
TryFindConfirmingRecordAsync. PC-73 changes the `written is null` outcome after the
caught write failure. No missing method or new seam is deferred to Mutation.

Audit before handoff: **bodies read; guards=75, mapped=75, missing=0,
duplicate PC maps=0**. All controls have existing methods, compiling edits,
nonzero floors and decisive assertions. This is an executability design audit,
not a claim that mutants ran or went red. A survivor returns for separate
Code/ordinary Review/land, never a repair inside the SourceLanding snapshot.

### Out of scope

- Whole Unit/assembly, CP-1/2/9 reruns, native inbox, provider canaries, browser
  E2E, package/envelope changes, release B refusal and C deletion. The known Unit
  record is 4030 passed / 15 failed / 53 skipped at `86981be09`, with the same
  15 at base `a10bd188`; it is not a waiver for future failures. New red needs
  exact-method base comparison without relaxing assertions/timeouts.
- Unchanged notification/outbox, phone-home and remote-spill recovery,
  Grok ready/trust classification, native custody and CARD-1020 cleanup
  predicates keep their owning contracts/PCs. Their selected fixture assertions
  remain; this adds no qualification claim for every internal guard of those
  components. No alternate unsafe cleanup is designed here.
- **A-OPS: retained-host and activation acceptance**, separate caller-owned
  operations follow-up, **35 estimated minutes** plus live-session waits.
  Read canonical HEAD/locks, current Windows runner build/process start/backend/
  raw request/fallback/deprecation. Inventory exact `Antiphon.PtyHost.exe` and
  OpenConsole generations: session/manifest, PID/start/image/build/package,
  pending delivery. `PtyHost.exe=0` or occupied=0 cannot establish absence.
  Preserve old/unknown retained sessions; no automatic kill. After qualification,
  use canonical Windows-runner-first restart, fresh owned modern host/package
  and full native plus server destination UserPrompt/separate Enter, then server
  activation and `/api/version` SHA validation. Loaded source must contain L,
  with its actual SHA recorded. Fresh-host success/health does not qualify
  retained hosts. If retained provenance or safe delivery cannot be established,
  activation stays open for caller disposition. This document neither restarts
  nor commissions that production canary; read the activation runbooks when
  executing A-OPS.
- Linux A reporting activation is an explicit A-OPS scope disposition. No Linux
  ConPTY rollout is required; Unix transport is unchanged. Claiming fleet-wide
  metadata activation requires an observed A-containing Linux runner SHA and
  UnixPty capability on a separately approved upgrade.
- **A-MUTATION: post-land controls**, separately commissioned and **paused**,
  **594 estimated minutes**. O/L SourceLanding only after explicit resume;
  external reports/restoration and custody cleanup rules apply. No PC-clean claim.
- **A-OVERFLOW: exact-row failure/infrastructure follow-up** receives unfinished
  or demonstrated-failing rows when setup, slot wait or red would exceed the
  60-minute ceiling. Report exact row/method/SHA/evidence/remaining cost. Never
  reduce Min, accept skips, broaden to Unit or silently change source. Real
  defects follow the successor plan's fresh-current-master correction route;
  its S/O2/L2 receipts remain distinct from L.

**A-LEDGER**, a five-minute administrative allowance included below, reconciles
the missing Mutation companion without running Mutation. Caller repeats complete
same-board search for `post-land-verification:b7c17822-63b4-4626-b5bb-525cfccc0cea`,
inspects hits/threads and serializes writes. Investigation found none; default is
one Backlog companion `Post-land verification: CARD-1022`, label
`post-land-verification`, that stable key, original card/board/project,
b7c17822/Review/C/O/L/R above, both plans, **PC-1..75 pending, original variants
and discovery retained; operator-paused 2026-10-04; do not Spawn**. Add a reverse
content revision link preserving human text. Record the actual found/new GUID;
none is fabricated here. Reconcile interrupted writes/duplicates before writing
again. If the allowance is insufficient, keep A-LEDGER open as named follow-up.
This TestDesign makes no board write.

The canceled/unconfirmed publication outcome
`44bc48c3-fd52-4e56-b8c5-33d3f3e1c2b0` (queue
`8f20f0ec-1f63-4b5d-b7ac-51fb4d1cbe42`, destination
`08a89212-d040-4659-b166-94746c8df0eb`) remains separate from confirmed task
completion at sequence 107951. A-LEDGER records caller disposition of the existing
outcome; no second land and no notification-delivered claim inferred from O.
Publication ownership stays b7c17822.

### Checkpoints

This is the sole importable table in this addendum: **CP-3..8 only**, Windows x64,
Debug, one isolated build and exact filter per row. It selects unchanged rows
from the 155-result freeze, not a reduced replacement roster. Selected classes
are wholly relevant; CP-5/7's mixed filters select only the named methods. At L
the count is exactly **20+4+2+44+24+1=95**. S2 means this selection is frozen;
no new implementation slice is required.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-3 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-windows-native/` | windows-modern | `/*/*/(PtyBackendContractTests*)\|(ModernPtyDa1Tests*)\|(PtyBackendEnvGuardTests*)\|(ConPtyEnvironmentIsolationGuardTests*)\|(C1022TypedInputTests*)\|(PtyInputChunkingTests*)/*` | R-1, R-2, R-6, V-5, R-7 | 4 + 4 + 1 + 2 + 3 + 6 results; 0 failed/skipped | 20 | 9 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-4 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1022-windows-launch/` | windows-owned-hosts | `/*/*/(C1022BackendLaunchTests*)\|(PtyBackendEnvGuardTests*)/*` | V-3, R-2 | 3 new owned launches + 1 guard; 0 failed/skipped | 4 | 7 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-5 | S2 | `tests/Antiphon.PtyHost.Tests -> bin-c1022-windows-pair/` | windows-shadow-pair | `/*/*/(ShadowCopyStoreTests*)\|(PtyBackendEnvGuardTests*)/(Shipped_conpty_binaries_survive_the_deps_json_closure_filter*)\|(The_suite_ignores_an_inherited_pty_backend*)` | R-2, R-3 | Both named methods; 0 failed/skipped | 2 | 4 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1022-windows-delivery/` | windows-delivery | `/*/Antiphon.Tests.Application/(PtyDeliveryCeilingsTests*)\|(SessionDeliveryProfileTests*)\|(GrokDeliveryShapeTests*)\|(TypedBodySpillTests*)/*` | R-4 | 14 + 7 + 14 + 9 results; 0 failed/skipped | 44 | 8 | true | `ANTIPHON_PTY_BACKEND=inbox;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c1022-server-guard/` | windows-queue-and-grok | `/*/*/(PtyBackendEnvGuardTests*)\|(SessionQueueReceiptPlumbingTests*)\|(RunnerGrokAdapterReadyTestsPty*)/(The_suite_ignores_an_inherited_pty_backend*)\|(C475_*)\|(C1022_Incomplete_or_stale_receipts_do_not_confirm*)\|(C1022_Partial_receipt_turn_parks_interrupted_attempt*)\|(C1011_windows_backends_reach_ready_and_complete_prompt*)` | V-4, R-2, R-5, V-5, R-7 | 1 guard + 20 retained queue + 1 native receipt-negative + 1 portable receipt-negative + 1 modern C1011; 0 failed/skipped | 24 | 12 | true | `ANTIPHON_PTY_BACKEND=inbox;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S2 | `tests/Antiphon.E2E -> bin-c1022-e2e-guard/` | windows-e2e-guard | `/*/*/PtyBackendEnvGuardTests/*` | R-2 | 1 guard, no browser; 0 failed/skipped | 1 | 5 | true | `ANTIPHON_PTY_BACKEND=inbox` |

Union is the entire **new** ordinary execution scope. V-1/V-2 and Linux R-3 retain
the accepted prior receipts. V-5/R-7 add no executions: their three results are
already in CP-3/7. No whole-Unit run or automatic repetition is authorized.

Create the qualification task at **StartRef=L**, without rebasing/cherry-picking
these docs into it. HEAD and tracked/index cleanliness must match L throughout.
This addendum postdates L; run the identical six rows in the original freeze
already committed at L, so selection needs no source change. Only explanatory
After/Covers cells differ. Record this addendum's full pushed commit as selection
provenance. An external copy of this addendum is also valid with absolute `--plan`
and explicit `--repo-root` of the L checkout. Never qualify this TD branch or a
moving master as L.

From the Windows L checkout, bootstrap only if needed (infrastructure build,
four-minute setup allowance), then use the real checkpoint tool:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1022-acceptance-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1022-acceptance-tool/
if ($LASTEXITCODE -ne 0) { throw 'Checkpoint tool bootstrap failed; do not run unleased.' }
dotnet tools/Antiphon.Checkpoints/bin-c1022-acceptance-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-03-card-1022-modern-conpty-only-plan.md --rows CP-3,CP-4,CP-5,CP-6,CP-7,CP-8 --expected-source-sha f11d2715f340c7caa03343862cb8dd73cc27f66b --serial --max-wait 50s
```

Rows take their own slots: no double wrapper or unleased retry after refusal.
Continue `wait` for the printed run ID with `--max-wait 50s` until exit is not 75;
await all owned work before reporting. Use its actual report path for
`validate --evidence`, the same six `--rows`, and
`--expected-source-sha f11d2715f340c7caa03343862cb8dd73cc27f66b`.
Accept only exact method/argument roster, per-row floors, no failures/skips,
`dirty=0 sourceState=clean buildSource=verified`, fresh TRX and matching structured
source receipt. Preserve unedited CHECKPOINT lines, source/OS/process architecture
and native identity evidence. Record Debug configuration; never reuse Release
or prior-source output.

CP-4 emits runner/host ProductVersion and DLL digest, image/start/session,
loaded DLL/observed OpenConsole pair, hashes and both DTOs. Keep unset Requested
empty. CP-7 emits C475_RECEIPT whole native/destination body and attempt/floor
identity; retain C1011's isolated-session complete native UserPrompt and
captured/observed-exit CARD-1020 records. Tests with no stdout still require their
complete-body assertions. CP-3 Node is byte evidence only. Fixture assemblies
cannot establish production daemon SHA. Record before/after production-process
inventory without changing it; reap only owned generations. Remove all task-owned
alternate bin directories across project references after processes exit. Keep
receipts/logs ignored and preserve evidence under the task retention contract.

**Repeat rule:** one initial pass, no automatic repeats. At most **three total
rounds per selected case**, only for a demonstrated failure, within the aggregate
60-minute ceiling. Subsequent rounds use only failing exact methods, with a stated
reason for the exception to the closed first-round manifest and exact-method
counts. Never rerun all six rows for one failure. Base comparison also stays
method-scoped and within allowance or moves to A-OVERFLOW. No timeout/assertion
relaxation or retry-to-green.

Store qualification in the task Result and ignored artifacts; a tracked report
commit changes qualified HEAD. Independent Review validates source-bound receipts
and selection. This is an execution-only **Code** handoff for missing Windows
Debug evidence followed by read-only Review, with no new implementation owner or
publication. Caller records qualification at O/L and proceeds to A-OPS. Green
qualification alone closes neither activation, retained-host nor companion work,
and never resumes Mutation.

### Cost

All future costs are **estimated**, not measured by this TestDesign.

- Separate ordinary V/R floor (Code qualification): **45 minutes**, the CP sum:
  CP-3 modern filter 9; CP-4 owned-hosts 7; CP-5 pair 4; CP-6 delivery 8;
  CP-7 queue/Grok 12; CP-8 E2E guard 5. Each includes its isolated build.
- Setup/tool bootstrap/provenance **4**, receipt/native identity audit **5**,
  A-LEDGER **5**: ordinary acceptance total **59 = 4 + 45 + 5 + 5 minutes**.
  Slot wait, missing prerequisites or red that cannot fit go to A-OVERFLOW;
  no routine repeat budget or invented green.
- Separate Mutation PC floor, paused: original **578** plus PC-74 **6** and PC-75
  **10** = **594 minutes**. Table names exact filters/Min. Original project model:
  31 Pty x6 + 8 runner x8 + 3 host x6 + 30 server x10 + 1 E2E x10 = 578.
  With additions: 32 Pty x6 + 8 runner x8 + 3 host x6 + 31 server x10 + 1 E2E x10
  = 594. Each cycle includes method baseline green, compiling edit, isolated red
  build/run, restoration, fresh green build/run and per-PC evidence. Argument and
  internal boundaries stay under exact methods; discovery findings add their
  separately reported cost after explicit resume.
- Total qualification plus PC/setup/audit/ledger floor: **653 = 59 + 594 minutes**,
  split between acceptance and paused A-MUTATION. Separate A-OPS **35** gives
  **688 minutes** including operations, before unknown retained-session waits.
- Savings versus replaying nine ordinary rows: **14 minutes / 60 executions**
  avoided (59 -> 45 V/R), carrying accepted CP-1/2/9. Versus full ordinary plus
  another six-row Debug, **59 minutes** avoided (104 -> 45). No reduction inside
  required 95-result Debug: each omitted row would leave its explicit obligation
  open. Whole Unit is avoided without inventing a new timing; its 15 inherited
  failures are known. Mutation is neither ordinary spend nor waived work.

Static validation on 2026-10-04: the real PlanTableImporter and ManifestValidator
accepted this table for both OS settings: six rows, Min=95, EstimatedMinutes=45,
zero warnings. The existing importer DLL was
`/work/worktrees/task-62fc2d31/tools/Antiphon.Checkpoints/bin-c913-tool/Antiphon.Checkpoints.dll`,
SHA-256 `69C455608C16B0596FAA2B010603FC01A169C8243E728FEDA45D8439DC025D3A`;
its importer source compared byte-identically with this checkout. This read-only
import proves manifest syntax, not a product build or native execution.
Static checks found six rows equal to L's selection except After/Covers,
75 unique guard/control maps, every exact method present, all 20 PC test-source
files identical to L, three valid document links and append-only successor edit.
Whitespace and the full assigned-base-to-pushed-tip evidence guard are reported
at the final commit. No product build/test, Mutation, restart, board write or
activation ran in this TestDesign.

--- next stage ---
next: code
handoff: Execute only Windows x64 Debug CP-3..8 at L=f11d2715f340c7caa03343862cb8dd73cc27f66b, 95 results, 45-minute V/R floor and 59-minute acceptance estimate. No source edits/new land; preserve b7c17822 and prior ordinary evidence. Store clean native/recipient receipts for Review. Reconcile companion; Mutation paused. Activation/retained hosts stay A-OPS.
artifact: docs/superpowers/plans/2026-10-04-card-1022-release-a-acceptance-test-design.md
