# CARD-1545 S1c executable, profile, startup and delivery: test design

Scope: **S1c-executable, S1c-profile, S1c-startup-oracles and
S1c-delivery-oracles only**, at `4e1e6106d68b833347221df8f50c6346845913b2`.
This companion appends verification detail to the [landed fix plan](2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md),
without rewriting its fix design. Format follows the S1b companion on
`origin/feat/card-task-6a22c243`. Read reviews a62610b7, 917262f2 and 7973fc74,
including the latter's four handoff paragraphs and A-1..6.

D-3/D-4/D-5/D-6/D-9/D-11 remain **PROVISIONAL**. These commissions use owned
synthetic inputs on server2/Linux, not Claude, credentials, network, ConPTY or a
paid/native run. Named models are fixture values; no production model is chosen.
Native acceptance, reuse, signing and enforcement remain separate obligations.
All expected results below are design, not executed evidence.

## Verification design

### Inspection

Bodies read at the source above, before naming the cases:

- `tests/Antiphon.Agents.Pty.Tests/ClSession.cs`: eligibility, PATH resolvers,
  `BuildLaunch`, `HeadedSafeEnv` -> V-1..3, V-7..12. Its caller assembly is
  `Antiphon.Agents.Pty.Tests`, **net9.0**, not a Windows TFM.
- `ClaudeSubmitContractTests.cs` and `ClaudeSubmitContractLiveTests.cs` in that
  directory: both fake methods, harness launch/prompt/cleanup and live verdict ->
  V-1..3, V-8, V-23..26. Fake and live branches have different verdict contracts.
- `ClaudeRemoteControlAtStartupCanaryTests.cs`: all three methods, launch,
  waits/observation, bridge/JSONL readers and sandbox trust/disposal -> V-7..12,
  V-18..22. The three UserProfile sites and two inline settings arrays are actual
  bypasses, not hypothetical paths.
- `ClaudeRemoteControlMenuCanaryTests.cs`: C514 and the separate non-core
  `One_Esc_dismisses_the_remote_control_management_menu` -> V-1..3, V-7..12,
  V-23..26; the latter non-core method is not added to the lane.
- `tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs`: full method,
  `SubmitProbeAsync`, wait and resolver -> V-4..6, V-13..17. Existing watchdog
  no-write assertions and both permission/question probes are retained.
- `src/Antiphon.Agents.Pty/ClaudeProjectTrust.cs` and nearest
  `ClaudeProjectTrustTests.cs` (including `TempConfig`): default config path,
  seed/remove/memo and sibling-preserving fixture -> V-7, V-13.
- `ClaudeStartupTrustPromptTests.cs` scripted screen/write fixture;
  `tests/Shared/NativeLinuxPolicyWitnessTests.cs` and
  `tests/Antiphon.Tests/Infrastructure/AppHostBrokerSourceGuardTests.cs`:
  nearest portable callback and nonempty source-witness patterns -> V-18..26
  and caller audits. A raw substring match is insufficient for final-verdict flow.
- `server/Application/Services/ClaudeRemoteControlLaunchArgs.cs`, its complete
  `ClaudeRemoteControlLaunchArgsTests.cs`, `AgentRegistry.Resolve` and nearest
  `AgentRegistryTests.cs` fixture -> V-17 independent production argv. Also read
  the legacy-resolution funnel in `AgentTuiLaunchResolver.cs`; no DB-backed
  profile resolution or server reference is introduced into the PTY test project.
- `PromptSubmissionMatch.IsCompleteIn`, C680's transcript-floor checks,
  `ExpectationDeliveryFixture.CreateAsync`/`DeliverAsync`/nudge persistence,
  `TranscriptTurnBaselineTests.cs`, and fixture shapes in
  `Agents/Fixtures/card0584/claude-prompts.jsonl` and
  `Agents/Fixtures/split-final-response.jsonl` -> V-23..25. The last fixture shows
  why a thinking-only end_turn is not a completed answer. New fixtures contain
  synthetic text, not copies of that captured conversation.
- Both test csproj references/link/staging declarations, PTY `ProcessSpawnLimit`,
  `tests/Antiphon.Tests/Scripts/ScriptHarness.cs` -> six rows, link shared support
  into each executable separately. No test-executable reference to another.
- Plan CP-17/23/18/24/3/19 and boundary CP-20/26/22, native CP-4..7/21;
  testing/build manifest, slots, delivery and Mutation owner sections;
  test-performance standards; session-runtime delivery invariants and
  orchestration D-2 -> timing, evidence and exclusions below.

### Delivery inventory

These slices change **test-side observation and launch wiring**, not
`SessionMessageQueueService`, an outbox, nudge recovery or a product recipient
transport. CP-5/C514 are direct owned PTY inputs, with no queue in their path.
C680 continues using its existing real expectation sender/queue to prove a dialog
refusal; this design changes its executable/profile inputs only. Consequently
real-queue busy-recipient, already-eligible-recipient and enqueue/crash-recovery
tests are **excluded for unchanged product queue paths**, not replaced by fake
acks. If Code changes a queue, persistence or replay service, return `next: plan`
for those exact producer-to-recipient tests; the four commissions do not authorize
that expansion. A request, write return, event, Sent flag or ack is never receipt.

| Producer -> destination | Persistence boundary and durable join | Recovery / recipient evidence | Witness and limitation |
|---|---|---|---|
| Lane context -> actual ClSession/C680 launch boundary | Issued run/attempt, source, core UID, owned executable/profile, live lock owner, PID/start/session | Missing/replaced ownership refuses before spawn; failed launch or unproven image never earns credit | V-1..6 capture the actual StartInfo/spec boundary through injected spawn/probe observations; they do not prove native image identity |
| CP-18 PTY builders -> CP-24 argv consumer | Owned capture file, source SHA, producer build hash, run ID, CP-18 UID, fixed request/model case and digest; completed producer row receipt | Publish only completed capture; a cut before write, partial write, missing terminal producer, stale run/source or wrong build refuses. A new wrapper run regenerates captures, never imports ambient latest files | V-17 consumes actual CP-18 captures; fault variants exercise consumer admission. File existence alone is not receipt |
| Startup control -> off/slash fixture consumers | Run-owned completed control observation, binary/profile/settings identity, host epoch, process/session, latency and zero submissions | Concurrent dependent requests await the same control once. Failed/absent/foreign control blocks dependent credit; fresh run cannot reuse a prior run's control | V-19 and V-21; fake process/clock prove ordering, not remote availability |
| Submit harness -> owned Claude session -> transcript reader -> final verdict | Unique full body, core UID/run, session/native binding/generation, pre-input sequence floor; actual CLI JSONL is the native persistence boundary | No whole prompt means unconfirmed; no automatic resend. Read failure, truncated record, lost Enter, crash after write/before receipt or missing completion never passes | V-23/24 use scripted recipient JSONL/normalized observations through the actual reader/verdict seam. Later CP-5 needs the **matching complete UserPrompt** followed by that turn's response/completion |
| C514 dismissal and submit -> owned session sidecar/transcript -> verdict | Same process/start/session and bridgeSessionId before Esc, after Esc and after submission, plus full prompt/floor | Dropped/changed bridge or partial/missing sidecar cannot credit survival, even with a valid prompt; no replay after ambiguous write | V-25 plus later CP-7. Screen/menu flags establish readiness only, never delivery |

For the direct-input paths, capture a healthy recipient, busy/unready recipient,
and a recipient already ready before the first read. Busy means zero task-body
writes until readiness; an unresolved permission/question dialog means zero
body/Enter writes. Inject faults before body write, between body and Enter, after
Enter but before transcript persistence, on transcript read, and after UserPrompt
but before response persistence. V-23/24 assert no false verdict/no resubmission
at every cut. These are local observation cuts, **not** OS-death recovery proofs.
For startup/sidecar cuts, V-18/19/25 require typed non-green evidence rather than
an empty read becoming proof of absence.

No fake harness emits an authoritative receipt simply because Send returned.
Its scripted recipient persists independently selected records; Enter-lost
variants retain the screen echo and withhold UserPrompt. Healthy companion
records prove the real reader/verdict is reached. Only later native CP-5/7 proves
actual Claude received the prompt; this portable design stops at an explicitly
labelled substitute, and does not claim transport acceptance.

### Fixtures and binding conventions

All **26 new methods** below are parameterless `[Test]` methods. Matrices are
internal variants, not extra TUnit executions. Classes carry `Category("Unit")`;
app ownership is Claude runner/shared PTY support. V-17 alone additionally uses
`[Explicit]`: its cross-project capture context exists only during this commission.
This preserves the plan's Unit class metadata without creating an ordinary
nightly test that always fails for missing commission context. Exact CP-24 must
execute it, proved by its TRX UID/count; no skip is accepted. Default Full discovery
must exclude this **new** Explicit UID by its existing rule. No existing UID or
policy changes. This is a fixture-lifetime refinement, not a new policy exemption.

New support lives in the plan's `tests/Shared/LiveClaude/` files, linked into both
projects. Small test-only records/audit helpers may be co-located there; no new
runtime service or test executable. Use a manual TimeProvider, owned temp files,
in-memory environment/path lookup and capture-only launch/probe callbacks.
Do not set HOME/USERPROFILE or inspect the real profile. If exercising the actual
`ClaudeProjectTrust.DefaultConfigPath()` needs `CLAUDE_CONFIG_DIR`, scope and
restore only that variable under a nonparallel environment test lock. Calls to
trust seed/remove use explicit owned paths. No live process escapes the fake
boundary; any attempted native/provider/network call fails the test immediately.

Healthy context: fixture run A/attempt 1, exact core UID, source SHA from the owned
test context, live fake lock owner, fake owned image with distinct bytes, dedicated
profile and cwd, process 101/start A/session A, sequence floor 10. Use a unique
multiline body longer than 200 characters, including distinct head/middle/tail.
The body must satisfy `RequiresTextMatch`; a short string cannot exploit
`IsCompleteIn`'s weak-arm true. A positive receipt is a complete `UserPrompt` at
11 in session A; assistant response and final completion share its turn at later
sequences, with no intervening user prompt. Healthy bridge ID is bridge-A.

PATH fixtures contain inert `cl.exe`, `cl.bat` and another `claude.exe` before the
owned attested image. They are never executed. Fake image observations carry the
path/hash/PID/start/session and an interpreter/payload chain if present. Scope
legacy behavior checks to non-core UIDs; no missing lane input can select legacy.

Profile fixtures include distinct ambient/dedicated trust, projects and sessions;
user, project, local and overlay settings; injected OS managed-policy root;
global `.claude.json`; an owned cwd ancestor chain; and synthetic instructions,
imports, hooks/MCP/skills. Record source identity, precedence and
absent/empty/value/unreadable/absent-unverified separately. Known credential keys
are never hashed/exported; unknown behavior keys disable reuse. Seed owned trust
before projection, restore just the owned change, verify siblings and preexisting
entries remain. Do not use `Remove` to erase a previously existing project object.

The planned `settings-source-witness.mjs` is a declared benign SessionStart hook:
source marker + owned session identity + allowed retained setting, written only
to its declared scratch observation file. Portable tests feed missing/changed
observations through the canary-side consumer, and inspect hook/builder wiring;
they do not prove the CLI loads the hook. Native CP-6 must observe markers from
the actual hook, not synthesize them in its outer harness. Hook content/path and
runtime are fingerprint inputs. Unproven managed-path absence is
`absent-unverified`, which is unknown and permits **no reuse**.

Source audits operate on the named method/branch, not comments or file-wide
substring presence. Use a language-aware method/control-flow reader or a bounded
token grammar for the accepted simple call shape; require exact method count,
required delegation and reachable call order, reject unknown shapes. A comment,
dead `if(false)` call, ignored result, catch-swallowed verdict or missing file must
fail. Bind the actual audit implementation and its scratch-source negatives in
Code. Existing regex source tests are a pattern for nonempty checks, not a proof
that regex can establish throwing final-verdict control flow.

Every assertion gets the stable message `C1545 <V-id>/<case-id> <predicate>`.
The V table supplies cases/predicates; PC rows refine them. Each negative records
entry/capture counts and changes one field after a healthy companion has passed.
Wrong prerequisites, zero cases, fixture errors and build errors cannot be red.
New APIs are absent at base: first commit compilable witnesses using reflection
or behavior-preserving extraction of existing private bodies, then capture actual
assertion reds. A missing required entrypoint is an explicit assertion-red anchor,
not a compile failure; before calling Code complete, also demonstrate the named
behavioral negative against the extracted old behavior. Do not ship constant
expected-result stubs. Baseline red predictions below are **not measured**.

### Proves it works now

Exact prefixes: `P = /*/Antiphon.Agents.Pty.Tests/` in project
`tests/Antiphon.Agents.Pty.Tests`; `A = /*/Antiphon.Tests.Scripts/` in
`tests/Antiphon.Tests`. Append each literal class/method below to that prefix;
this defines the exact method filter with no class wildcard. CP rows use the
same namespace and an exact class wildcard only for ordinary V/R.

| ID | Exact filter suffix (prefix/project) | Recipe, variants, oracle and reachability | Predicted RED at base |
|---|---|---|---|
| V-1 | P `LiveClaudeExecutableWiringTests/ClSessionUsesAttestedExecutable` | Call real ClSession lane entry with decoy PATH; capture probe and launch specs. `owned`: both paths equal attested image; `legacy`: non-core resolution unchanged. Audit all five PTY core callers plus harness live branch delegate to it. | Existing ResolveOrThrow chooses cl.bat; core call sites have no lane resolver |
| V-2 | P `LiveClaudeExecutableWiringTests/CoreCallersRefuseMissingContext` | Pass each exact lane UID with **no lane env and no context**. `missing-context`, `missing-exe`, `wrong-uid/source/run/profile`, `lock-released/replaced`: typed refusal and spawnCount=0; healthy mate reaches capture once. Admission must precede Windows eligibility. | Old UID-unaware path resolves/spawns or native-skips instead of lane refusal |
| V-3 | P `LiveClaudeExecutableWiringTests/ExecutedImageMismatchRefuses` | Healthy launch observation followed by one wrong path/hash/PID/start/session; `post-drift`, `unbound-chain`: 20 executable-mismatch and no credit. Probe same image, pre/post observations mandatory. | No executed-image validation or post-run hash gate |
| V-4 | A `LiveLaneExecutableTests/C680UsesAttestedExecutable` | Invoke actual C680 probe/launch entry; decoy PATH, owned image; both captured executables equal context. Audit live C680 method uses it before probe and start. | C680 resolves installed Claude from PATH |
| V-5 | A `LiveLaneExecutableTests/C680RefusesMissingContext` | Same variants as V-2 with C680 UID; healthy mate reaches capture without starting DB/runner. Missing env never restores manual path for core UID. | Headed env alone admits legacy C680 route |
| V-6 | A `LiveLaneExecutableTests/C680ImageMismatchRefuses` | Same observation matrix as V-3 through C680 entry; compare probe and actual launch identities. | C680 trusts version process with no image/chain attestation |
| V-7 | P `LiveClaudeIsolationWiringTests/DedicatedConfigOwnsTrustAndEvidence` | Actual sandbox/trust/read entrypoints: dedicated paths, ambient decoys, foreign session with same cwd, missing config, trust restore and siblings. Audit startup FindSessionJsonl, ReadSessionFile and Trust (base lines 365/376/544); all five core launches use profile seam. | Startup uses UserProfile and cwd-OR matching; owned trust is not restored |
| V-8 | P `LiveClaudeIsolationWiringTests/CwdInstructionsCannotLeak` | Capture each core cwd outside candidate; add unowned ancestor CLAUDE.md/AGENTS.md/import, .claude settings/skills/hook or .mcp.json separately; expect pre-launch refusal. Owned declared files pass; post-run drift prevents credit. | Submit and C514 omit cwd; no ancestry or post-check |
| V-9 | P `LiveClaudeIsolationWiringTests/SettingsProjectionDetectsDrift` | Project actual loaded source set; change one behavior value in user/project/local/overlay/managed/global state with unchanged enrollment revision. Each digest changes; precedence swap and missing source entry refuse equality. | No mechanical projection; current settings claims can ignore unflushed evidence |
| V-10 | P `LiveClaudeIsolationWiringTests/UnknownConfigDisablesReuse` | Unknown key/source, unreadable source, unobserved effective value, managed absent-unverified -> reuseEligible=false. Proven absent/empty/value are distinct; known volatile bookkeeping alone stable; credential bytes excluded. | No complete source inventory/unknown contract |
| V-11 | P `LiveClaudeIsolationWiringTests/LaunchArgumentsKeepProductionSources` | Capture control/off/slash/submit/C514 for all three model contexts; no invented empty setting-sources; control no overlay, off/slash exactly one owned off overlay preserving source flags. Audit both startup inline arrays (base lines 92/144) and actual launch calls. Export bound captures only after assertions. | Inline arrays bypass exercised builder; no bound argv export |
| V-12 | P `LiveClaudeIsolationWiringTests/ModelSelectionComesFromContext` | `named-A`, `named-B`, `production-default`: actual args match explicit request; no hard-coded Haiku. Missing selection/effective attestation refuses; absent/present-empty env preserved. Audit fakeclaude branch remains independent of lane model/profile. | PTY defaults through wrapper, no explicit model context |
| V-13 | A `LiveLaneIsolationTests/DedicatedConfigOwnsTrustAndEvidence` | V-7 path/trust matrix through C680 spec and transcript configuration; all reads/writes owned; preserve intentional empty markers and both probe branches. | C680 does not bind a dedicated config in the launch spec |
| V-14 | A `LiveLaneIsolationTests/CwdInstructionsCannotLeak` | V-8 matrix through C680; permission probe remains under owned cwd, restrictions attested as deliberate C680 differences. | Temporary cwd exists but ancestry/instruction closure is not checked |
| V-15 | A `LiveLaneIsolationTests/SettingsProjectionDetectsDrift` | V-9 matrix via C680 projection, including managed/global sources even if its deliberate tool restrictions differ. Explicit model contexts exercised here for C680. | C680 has no source projection and hard-codes model fallback |
| V-16 | A `LiveLaneIsolationTests/UnknownConfigDisablesReuse` | V-10 matrix via C680; empty source-list does not remove always-loaded managed/global sources; source uncertainty never equality. | No managed/global unknown refusal |
| V-17 | A `LiveLaneIsolationTests/StartupArgvMatchesProductionApplyOff` | Read real completed CP-18 captures, compare to independent production request below. Missing/stale/source/build/UID/model mismatch or partial capture refuses; append unknown flag or delete off flag and equality fails. | No captured producer or parity path; assert required capture builder/delegation before comparing |
| V-18 | P `LiveClaudeStartupWiringTests/ExplicitBridgeSetup` | Invoke actual startup/slash and C514 bridge-establishment entrypoints with fake clock/bridge. Own per-process sidecar proves bridge; badge-only, foreign pid/session/cwd, unreadable sidecar cannot. Slash after observed off must arm a new owned bridge; menu alone is insufficient. Zero model prompts. | Startup accepts badge/JSONL union; C514 assumes pre-established bridge |
| V-19 | P `LiveClaudeStartupWiringTests/ControlRequiredForSettingsOff` | Concurrent off/slash requests await one run-owned control; control finishes first. Absent control -> 20 premise-absent; foreign run/profile/image/settings or interrupted control never allows green. Healthy control -> off/slash reach observations. | Off/slash run independently of control, can pass without premise |
| V-20 | P `LiveClaudeStartupWiringTests/ControlWindowBoundsOffObservation` | Manual clock: no bridge completes at max(15 s, control latency); insert bridge at 8.001, 12, 14.999 s and exact deadline -> assertion red. Inject control latency 18 s -> window>=18 s (oracle boundary, not a native deadline increase). | OverlayObserve is 8 s |
| V-21 | P `LiveClaudeStartupWiringTests/StartupMethodsUseControlOracle` | Audit all three exact live methods and shared fixture creation; dependents await exercised ordered control and call throwing final startup API, no optional evidence bypass. Scratch dead/missing/ignored delegation fails. | No shared prerequisite or throwing verdict call |
| V-22 | P `LiveClaudeStartupWiringTests/LoadedSettingsSurviveOffOverlay` | Actual canary observation consumer: user/project/local markers and retained setting equal in control/off; missing/wrong-session marker -> 20 settings-unobserved, changed retained value -> assertion red. Known startup records preserved when present; absent all evidence never passes. | JSONL assertions conditional, no source marker evidence |
| V-23 | P `LiveClaudeDeliveryWiringTests/PromptReceiptPrecedesVerdict` | Real submit/menu verdict entrypoints + reader: full prompt/floor/session/generation, then CP-5 same-turn assistant response/completion. Partial head/splice, QueuedUserPrompt, foreign session, stale floor, changed generation, unrelated/intervening turn, thinking-only end_turn and API-error fail. Busy/ready and write/read cuts use the inventory above. | CP-5 accepts DonePattern without any transcript |
| V-24 | P `LiveClaudeDeliveryWiringTests/LostEnterCannotPass` | Done-like screen + full composer but no UserPrompt must throw; receipt without CP-5 completion must throw. Task-body write into unresolved permission/question/unknown screen is refused; capture LF/bracketed body and distinct Enter, no automatic resend after ambiguous cut. | CP-5/C514 final screen predicates can pass with Enter lost |
| V-25 | P `LiveClaudeDeliveryWiringTests/DroppedBridgeCannotPass` | Correct whole UserPrompt retained; independently remove/change bridge after Esc or after submit; foreign process/session, badge-only and sidecar-read failure fail. Healthy identical bridge-A at all three phases passes. | C514 never rereads bridge after Esc or submit |
| V-26 | P `LiveClaudeDeliveryWiringTests/LiveMethodsUseTranscriptVerdicts` | Audit CP-5, C514 and harness claude branch: final assertion is named throwing `LiveClaudeDeliveryEvidence.RequireSubmit` / `RequireMenu` outside every try/catch; no post-readiness Should/Assert on DonePattern/screen/composer. Cleanup may follow. Preserve fakeclaude DonePattern methods. | Both live final assertions are screen-based |

V-17's independent request is constructed with real `AgentRegistry.Resolve` over
the checked-in Claude definition (read only its non-secret definition), fixed
owned cwd/session/geometry and explicit per-case `AgentLaunchOptions` model.
Use the existing `AgentRegistryTests` options-monitor fixture; no managed-profile
DB or provider discovery. Substitute only the attested fake executable in the
definition. Capture this production argv **before reading the PTY capture**.
Compare control to it; compare off/slash to real
`ClaudeRemoteControlLaunchArgs.ApplyOff(ClaudeCode, independentArgs, ownedOffPath)`.
Assert the owned file parses to only `remoteControlAtStartup:false`. Independently
declare permitted path/session substitutions and test-specific geometry,
child-marker and model/request differences; reject every other difference.
Do not subtract all env flags or all `--settings*` arguments. C680 tool/permission
restrictions are recorded separately and never used as CP-6 parity evidence.

`scripts/test-live-lane-isolation.ps1` creates the owned
`ANTIPHON_LIVE_PORTABLE_CONTEXT`, runs only S1c-profile's two rows serially, and
awaits terminal completion. It does not hold an outer build slot. V-11 exports
captures for control/off/slash/submit/C514; V-17 consumes only the expected
source/run/producer build and completed CP-18. Local malformed-copy variants
never replace the healthy actual producer capture. V-17's exact-method Mutation
pack binds this genuine immutable producer artifact, its receipt and reviewed SHA;
it must not create an expected argv by copying the capture under test.

### Guards the regression

- R-1: real callers cannot bypass the attested resolver -> V-1/2/4/5 caller audits
  plus actual captured probe/start specs; non-core ClSession behavior preserved.
- R-2: wrong profile or settings cannot manufacture startup absence -> V-7..17,
  V-22; reject foreign evidence, unknown sources and capture self-comparison.
- R-3: settings-off cannot pass without its premise or before the full window ->
  V-18..22; delayed bridge and absent control are decisive non-green cases.
- R-4: screen echo cannot certify delivery or bridge survival -> V-23..26;
  complete session-bound recipient evidence and same bridge are required.
- R-5: fake submit contract/backend and existing live UID metadata stay intact ->
  V-1/12/26 structural branch assertions, plus separately commissioned native CP-21
  for both existing fakeclaude results. No portable fake is native qualification.
- R-6: existing Full dispositions/policy stay fixed -> commission audit below;
  new portable methods are visible additions, never synthetic live passes.

Further sections below bind every guard to a Mutation recipe, checkpoint and cost.
