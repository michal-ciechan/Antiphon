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
- Nearest new hook fixtures: `probes/argv-echo.js` and the SessionStart wrapper
  in `scripts/hooks/orchestrator-investigation-hook.mjs`: owned file output and
  hook JSON input shape -> V-22's actual marker-writer child. No live hook runs.
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
to its declared scratch observation file. V-22 invokes this actual writer in an
owned node child with synthetic hook stdin for the healthy records, then feeds
missing/changed copies through the canary-side consumer. The outer harness never
writes the healthy marker. Give LiveClaudeStartupWiringTests the assembly-local
ProcessSpawnLimit. This proves writer/consumer compatibility, not that Claude
loads the hook; also inspect hook/builder wiring. Native CP-6 must observe markers from
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

Outside commissioning, V-11 still exercises every builder and assertion with its
own local capture sink; only the cross-row export requires the wrapper context.
It never skips its behavior assertions when that context is absent. V-17 is the
sole context-dependent Explicit method and refuses absent context when selected.
This prevents the ordinary Unit roster from depending on a previous CP-18 run.

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

### Guard inventory

Each row is one independently removable guard and maps to one distinct PC.
Shared validators are listed once; independent caller bypasses have their own rows.
Variants under one PC exercise the same guard with different data, not separately
bypassable call sites. Every listed variant is mandatory. No guard is justified
away as "none". Native image, CLI-loaded-source and actual recipient observations
remain unexecuted native obligations; portable controls test their decisions over
substitutes. Plan references: V-1..6 refine parent V-3a; V-7..17 parent V-3b;
V-18..22 parent V-3c; V-23..26 parent V-3d. All rows protect those credit assertions.

| Guard | Plan ref / safety-critical invariant | Positive control |
|---|---|---|
| G-1 | V-1: Shared executable selection never searches PATH in lane mode | PC-1 |
| G-2 | V-1: Executable delegation at submit-live method | PC-2 |
| G-3 | V-1: Executable delegation at submit harness claude branch | PC-3 |
| G-4 | V-1: Executable delegation at startup control method | PC-4 |
| G-5 | V-1: Executable delegation at startup off method | PC-5 |
| G-6 | V-1: Executable delegation at startup slash method | PC-6 |
| G-7 | V-1: Executable delegation at C514 method | PC-7 |
| G-8 | V-2: ClSession lane classification is UID-based | PC-8 |
| G-9 | V-5: C680 lane classification is UID-based | PC-9 |
| G-10 | V-2: Required lane context | PC-10 |
| G-11 | V-2: Required lane exe | PC-11 |
| G-12 | V-2: Launch-context UID binding | PC-12 |
| G-13 | V-2: Launch-context run binding | PC-13 |
| G-14 | V-2: Launch-context attempt binding | PC-14 |
| G-15 | V-2: Launch-context source SHA binding | PC-15 |
| G-16 | V-2: Launch-context profile identity binding | PC-16 |
| G-17 | V-2: Lock must still be held | PC-17 |
| G-18 | V-2: Lock owner must match | PC-18 |
| G-19 | V-4: C680 probe uses the attested image | PC-19 |
| G-20 | V-4: C680 launch uses the attested image | PC-20 |
| G-21 | V-5: C680 launch cannot bypass shared admission | PC-21 |
| G-22 | V-3: Executed-image path binding | PC-22 |
| G-23 | V-3: Executed-image hash binding | PC-23 |
| G-24 | V-3: Executed-image PID binding | PC-24 |
| G-25 | V-3: Executed-image start identity binding | PC-25 |
| G-26 | V-3: Executed-image session binding | PC-26 |
| G-27 | V-3: Post-launch observation required | PC-27 |
| G-28 | V-3: Teardown image/digest recheck required | PC-28 |
| G-29 | V-3: Interpreter/payload chain must be attested | PC-29 |
| G-30 | V-6: C680 observes rather than trusting its probe | PC-30 |
| G-31 | V-7: Dedicated profile path at CanarySandbox.Trust | PC-31 |
| G-32 | V-7: Dedicated profile path at FindSessionJsonl | PC-32 |
| G-33 | V-7: Dedicated profile path at ReadSessionFile | PC-33 |
| G-34 | V-7: Lane profile cannot fall back | PC-34 |
| G-35 | V-7: Owned trust changes restored | PC-35 |
| G-36 | V-7: Preexisting trust entries preserved | PC-36 |
| G-37 | V-7: Trust siblings preserved | PC-37 |
| G-38 | V-13: C680 profile seam reaches actual launch and reads | PC-38 |
| G-39 | V-13: C680 owned trust restoration reached | PC-39 |
| G-40 | V-8: Owned cwd at submit PTY core launch | PC-40 |
| G-41 | V-8: Owned cwd at startup-control PTY core launch | PC-41 |
| G-42 | V-8: Owned cwd at startup-off PTY core launch | PC-42 |
| G-43 | V-8: Owned cwd at startup-slash PTY core launch | PC-43 |
| G-44 | V-8: Owned cwd at C514 PTY core launch | PC-44 |
| G-45 | V-14: C680 cwd instruction admission reached | PC-45 |
| G-46 | V-8: Unowned instruction ancestry is refused | PC-46 |
| G-47 | V-8: Instructions rechecked after run | PC-47 |
| G-48 | V-11: Production source flags preserved | PC-48 |
| G-49 | V-11: Control has no off overlay | PC-49 |
| G-50 | V-11: Off call uses exercised overlay builder | PC-50 |
| G-51 | V-11: Slash call uses exercised overlay builder | PC-51 |
| G-52 | V-12: Named model comes from context | PC-52 |
| G-53 | V-12: Production-default is explicit | PC-53 |
| G-54 | V-12: Missing model selection refuses admission | PC-54 |
| G-55 | V-12: Unknown effective model cannot earn credit | PC-55 |
| G-56 | V-15: C680 model delegation uses context | PC-56 |
| G-57 | V-9: Loaded user behavior participates in projection | PC-57 |
| G-58 | V-9: Loaded project behavior participates in projection | PC-58 |
| G-59 | V-9: Loaded local behavior participates in projection | PC-59 |
| G-60 | V-9: Loaded overlay behavior participates in projection | PC-60 |
| G-61 | V-9: Loaded managed-policy behavior participates in projection | PC-61 |
| G-62 | V-9: Loaded global .claude.json behavior participates in projection | PC-62 |
| G-63 | V-9: Source precedence participates in projection | PC-63 |
| G-64 | V-9: Loaded set derived from captured argv | PC-64 |
| G-65 | V-9: Owned trust normalization is narrow | PC-65 |
| G-66 | V-10: Credential bytes never enter projection | PC-66 |
| G-67 | V-10: Unknown behavior keys refuse reuse | PC-67 |
| G-68 | V-10: Unobserved effective values refuse reuse | PC-68 |
| G-69 | V-10: Unknown additional sources refuse reuse | PC-69 |
| G-70 | V-10: Unreadable sources distinct from absent | PC-70 |
| G-71 | V-10: Unproven managed absence refuses reuse | PC-71 |
| G-72 | V-10: Absent/empty/value stay distinct | PC-72 |
| G-73 | V-15: C680 invokes complete source projection | PC-73 |
| G-74 | V-16: C680 preserves unknown refusal | PC-74 |
| G-75 | V-17: Independent production input not derived from capture | PC-75 |
| G-76 | V-17: Control argv compared to production | PC-76 |
| G-77 | V-17: Off overlay retained and exact | PC-77 |
| G-78 | V-17: Capture source provenance binding | PC-78 |
| G-79 | V-17: Capture run provenance binding | PC-79 |
| G-80 | V-17: Capture producer build provenance binding | PC-80 |
| G-81 | V-17: Capture producer UID provenance binding | PC-81 |
| G-82 | V-17: Capture request/model provenance binding | PC-82 |
| G-83 | V-17: Capture requires completed producer receipt | PC-83 |
| G-84 | V-17: Capture publication completeness | PC-84 |
| G-85 | V-18: Bridge setup requires owned process/session sidecar | PC-85 |
| G-86 | V-18: Slash setup must establish a bridge after off | PC-86 |
| G-87 | V-18: C514 explicitly establishes its bridge | PC-87 |
| G-88 | V-19: Control is shared once per run | PC-88 |
| G-89 | V-19: Control completes before dependent launch | PC-89 |
| G-90 | V-19: Absent control never credits off | PC-90 |
| G-91 | V-19: Control belongs to this run/epoch | PC-91 |
| G-92 | V-19: Control/dependent binary identity | PC-92 |
| G-93 | V-19: Control/dependent profile identity | PC-93 |
| G-94 | V-19: Control/dependent settings identity | PC-94 |
| G-95 | V-18: Startup control has zero submissions | PC-95 |
| G-96 | V-20: Off observes at least 15 seconds | PC-96 |
| G-97 | V-20: Off observes measured control latency | PC-97 |
| G-98 | V-20: Deadline observation is inclusive | PC-98 |
| G-99 | V-21: Startup control call site reaches throwing oracle | PC-99 |
| G-100 | V-21: Startup off call site reaches throwing oracle | PC-100 |
| G-101 | V-21: Startup slash call site reaches throwing oracle | PC-101 |
| G-102 | V-22: Loaded user marker mandatory | PC-102 |
| G-103 | V-22: Loaded project marker mandatory | PC-103 |
| G-104 | V-22: Loaded local marker mandatory | PC-104 |
| G-105 | V-22: Loaded marker session identity | PC-105 |
| G-106 | V-22: Retained setting preserved by off | PC-106 |
| G-107 | V-23: Prompt record must be complete | PC-107 |
| G-108 | V-23: Prompt must be strong enough to match | PC-108 |
| G-109 | V-23: Prompt kind must be UserPrompt | PC-109 |
| G-110 | V-23: Prompt session binding | PC-110 |
| G-111 | V-23: Prompt generation binding | PC-111 |
| G-112 | V-23: Prompt native binding binding | PC-112 |
| G-113 | V-23: Prompt must be beyond pre-input floor | PC-113 |
| G-114 | V-23: CP-5 requires assistant response | PC-114 |
| G-115 | V-23: CP-5 requires same-turn completion | PC-115 |
| G-116 | V-23: Response/completion follows the matching prompt | PC-116 |
| G-117 | V-23: No intervening prompt can steal completion | PC-117 |
| G-118 | V-23: Thinking-only end_turn is not response completion | PC-118 |
| G-119 | V-23: API error is not successful turn | PC-119 |
| G-120 | V-24: Lost Enter cannot pass on screen success | PC-120 |
| G-121 | V-24: Submission preserves LF and paste plus separate Enter | PC-121 |
| G-122 | V-24: Dialog refusal prevents task-body input | PC-122 |
| G-123 | V-24: Busy recipient receives no early body | PC-123 |
| G-124 | V-24: Ambiguous input is not automatically resubmitted | PC-124 |
| G-125 | V-25: C514 same bridge before-Esc | PC-125 |
| G-126 | V-25: C514 same bridge after-Esc | PC-126 |
| G-127 | V-25: C514 same bridge after-submit | PC-127 |
| G-128 | V-25: Bridge observation bound to process | PC-128 |
| G-129 | V-25: Bridge observation bound to start | PC-129 |
| G-130 | V-25: Bridge observation bound to session | PC-130 |
| G-131 | V-25: Unreadable bridge evidence cannot prove survival | PC-131 |
| G-132 | V-26: submit live method: last-assertion guard | PC-132 |
| G-133 | V-26: submit live method: outside-catch guard | PC-133 |
| G-134 | V-26: submit live method: no-screen-assertion guard | PC-134 |
| G-135 | V-26: C514 method: last-assertion guard | PC-135 |
| G-136 | V-26: C514 method: outside-catch guard | PC-136 |
| G-137 | V-26: C514 method: no-screen-assertion guard | PC-137 |
| G-138 | V-26: submit harness claude branch: last-assertion guard | PC-138 |
| G-139 | V-26: submit harness claude branch: outside-catch guard | PC-139 |
| G-140 | V-26: submit harness claude branch: no-screen-assertion guard | PC-140 |
| G-141 | V-22: Source marker writer emits observable session-bound records | PC-141 |

### Positive controls

Each row supplies recipe, case/variant, exact method, decisive oracle and
reachability. `V-n` resolves to the single exact class/method/project/filter above.
The assertion label is `C1545 V-n/<case> <oracle>`; Code binds the full label and
any variant suffix. Classification for **every PC** is expected assertion-red
(driver exit 1, exact method in fresh TRX); 0 tests, compilation/fixture failure,
a skip or driver exit 2/3 is not red. Subject outcomes 20/invalid/credit=false
are assertions inside a passing ordinary test, not outer test skips.

Recipes describe **compiling defects in the real exercised decision/caller**,
not changes to the expected answer or fake verdict. Exact file/span/preimage,
patch bytes and source hashes are **binding required after Code** for every row,
even if the method names do not change. Preserve that status in the handoff;
Review judges design/binding before land, Code runs V/R, Mutation runs PCs after
confirmed land. If a recipe is masked by another guard, bind a reachable compiling
defect that breaks this invariant without removing unrelated safety checks;
otherwise return to Plan. A surviving mutant is not waived as redundant.

| PC / guard | Exact method | Compiling mutation recipe | Case / variant cycles | Exact assertion that must go red | Reachability |
|---|---|---|---|---|---|
| PC-1 / G-1 | V-1 | Replace lane selection with ResolveCl() ?? ResolveClaude() | owned; 2 | selected/probed/start image == owned attested image | Decoy files exist; healthy context admitted |
| PC-2 / G-2 | V-1 | Replace only submit-live method lane delegation with legacy ClSession resolution | call-submit-live-method; 1 | required reachable lane delegation and no legacy core resolver | Shared resolver remains correct; source reader finds this exact method |
| PC-3 / G-3 | V-1 | Replace only submit harness claude branch lane delegation with legacy ClSession resolution | call-submit-harness-claude-branch; 1 | required reachable lane delegation and no legacy core resolver | Shared resolver remains correct; source reader finds this exact method |
| PC-4 / G-4 | V-1 | Replace only startup control method lane delegation with legacy ClSession resolution | call-startup-control-method; 1 | required reachable lane delegation and no legacy core resolver | Shared resolver remains correct; source reader finds this exact method |
| PC-5 / G-5 | V-1 | Replace only startup off method lane delegation with legacy ClSession resolution | call-startup-off-method; 1 | required reachable lane delegation and no legacy core resolver | Shared resolver remains correct; source reader finds this exact method |
| PC-6 / G-6 | V-1 | Replace only startup slash method lane delegation with legacy ClSession resolution | call-startup-slash-method; 1 | required reachable lane delegation and no legacy core resolver | Shared resolver remains correct; source reader finds this exact method |
| PC-7 / G-7 | V-1 | Replace only C514 method lane delegation with legacy ClSession resolution | call-C514-method; 1 | required reachable lane delegation and no legacy core resolver | Shared resolver remains correct; source reader finds this exact method |
| PC-8 / G-8 | V-2 | Key lane mode on context-env presence instead of supplied core UID | missing-context; 1 | typed missing-context and spawnCount=0 | Core UID supplied, every lane env/context absent |
| PC-9 / G-9 | V-5 | Key C680 lane mode on headed/context-env presence | missing-context; 1 | typed missing-context and spawnCount=0 | C680 UID supplied without env/context; healthy companion reaches capture |
| PC-10 / G-10 | V-2 | Treat null context as legacy-admissible | missing-context; 1 | 20 missing-prerequisite and spawnCount=0 | UID is core; all other prerequisites valid |
| PC-11 / G-11 | V-2 | Accept a missing ANTIPHON_LIVE_CLAUDE_EXE | missing-exe; 1 | 20 missing-prerequisite and spawnCount=0 | UID is core; all other prerequisites valid |
| PC-12 / G-12 | V-2 | Omit only UID equality from context validation | context-UID; 1 | context refused; spawnCount=0 | Issued healthy fixture; only the named field differs |
| PC-13 / G-13 | V-2 | Omit only run equality from context validation | context-run; 1 | context refused; spawnCount=0 | Issued healthy fixture; only the named field differs |
| PC-14 / G-14 | V-2 | Omit only attempt equality from context validation | context-attempt; 1 | context refused; spawnCount=0 | Issued healthy fixture; only the named field differs |
| PC-15 / G-15 | V-2 | Omit only source SHA equality from context validation | context-source-SHA; 1 | context refused; spawnCount=0 | Issued healthy fixture; only the named field differs |
| PC-16 / G-16 | V-2 | Omit only profile identity equality from context validation | context-profile-identity; 1 | context refused; spawnCount=0 | Issued healthy fixture; only the named field differs |
| PC-17 / G-17 | V-2 | Ignore current lock liveness | lock-released; 1 | refused; spawnCount=0 | Original ownership tuple still matches, lock released |
| PC-18 / G-18 | V-2 | Ignore current lock owner comparison | lock-replaced; 1 | refused; spawnCount=0 | Replacement lock live, context otherwise valid |
| PC-19 / G-19 | V-4 | Use PATH Resolve for version probe only | probe-route; 1 | probe image == attested owned image | Launch still uses correct image; decoy version looks valid |
| PC-20 / G-20 | V-4 | Use PATH Resolve for StartAsync spec only | launch-route; 1 | start image == attested owned image | Probe correct; fake Start captures actual C680 spec |
| PC-21 / G-21 | V-5 | Drop the C680 call to shared admission before its native/DB boundary | caller-admission; 1 | required delegation; spawnCount=0 for refused context | Shared validator is correct; audit exact C680 method |
| PC-22 / G-22 | V-3 | Remove only observed path equality | image-path; 1 | 20 executable-mismatch; credit=false | Correct probe/preimage; observation differs only in field |
| PC-23 / G-23 | V-3 | Remove only observed hash equality | image-hash; 1 | 20 executable-mismatch; credit=false | Correct probe/preimage; observation differs only in field |
| PC-24 / G-24 | V-3 | Remove only observed PID equality | image-PID; 1 | 20 executable-mismatch; credit=false | Correct probe/preimage; observation differs only in field |
| PC-25 / G-25 | V-3 | Remove only observed start identity equality | image-start-identity; 1 | 20 executable-mismatch; credit=false | Correct probe/preimage; observation differs only in field |
| PC-26 / G-26 | V-3 | Remove only observed session equality | image-session; 1 | 20 executable-mismatch; credit=false | Correct probe/preimage; observation differs only in field |
| PC-27 / G-27 | V-3 | Accept missing post-launch image observation | post-launch-missing; 1 | 20 executable-mismatch | Preimage and teardown correct |
| PC-28 / G-28 | V-3 | Return original digest instead of rechecking at teardown | post-drift; 1 | 20 executable-mismatch | Same path, bytes changed only after launch |
| PC-29 / G-29 | V-3 | Accept wrapper chain with an unbound payload | unbound-chain; 1 | 20 executable-mismatch | All main-image facts match; one payload missing |
| PC-30 / G-30 | V-6 | Omit C680 post-launch observation delegation | c680-observation; 1 | actual observation mismatch refuses | Shared image validator correct; wrong fake spawned image |
| PC-31 / G-31 | V-7 | Restore UserProfile path in CanarySandbox.Trust | trust; 1 | path under dedicated profile; ambient bytes unchanged | Both profiles contain contradictory but otherwise matching fixtures |
| PC-32 / G-32 | V-7 | Restore UserProfile path in FindSessionJsonl | projects; 1 | path under dedicated profile; ambient bytes unchanged | Both profiles contain contradictory but otherwise matching fixtures |
| PC-33 / G-33 | V-7 | Restore UserProfile path in ReadSessionFile | sessions; 1 | path under dedicated profile; ambient bytes unchanged | Both profiles contain contradictory but otherwise matching fixtures |
| PC-34 / G-34 | V-7 | Use ambient home when dedicated config is missing | missing-config; 1 | refusal; no ambient read/write | Core context healthy except config missing |
| PC-35 / G-35 | V-7 | Omit restoration of the created trust fields | restore-owned; 1 | post bytes/owned projection == preseed baseline | Real seeded owned entry exists; disposal executed |
| PC-36 / G-36 | V-7 | Remove entire preexisting project object during cleanup | restore-existing; 1 | preexisting object bytes/values preserved | Preexisting object has non-trust behavior fields |
| PC-37 / G-37 | V-7 | Replace projects object with only the test entry | trust-siblings; 1 | unrelated projects unchanged | Siblings seeded nonempty before real trust operation |
| PC-38 / G-38 | V-13 | Omit dedicated profile from C680 spec/evidence configuration | c680-profile; 1 | config path and evidence reads == dedicated root | Shared resolver correct; ambient decoy evidence exists |
| PC-39 / G-39 | V-13 | Remove C680 finally restoration call | c680-restore; 2 | owned changes restored; siblings retained | Both question/permission path entrypoints exercised |
| PC-40 / G-40 | V-8 | Drop cwd only from submit actual launch call | owned-cwd-submit; 1 | captured cwd == owned non-candidate directory | Shared cwd builder correct; exact caller capture/audit exercised |
| PC-41 / G-41 | V-8 | Drop cwd only from startup-control actual launch call | owned-cwd-startup-control; 1 | captured cwd == owned non-candidate directory | Shared cwd builder correct; exact caller capture/audit exercised |
| PC-42 / G-42 | V-8 | Drop cwd only from startup-off actual launch call | owned-cwd-startup-off; 1 | captured cwd == owned non-candidate directory | Shared cwd builder correct; exact caller capture/audit exercised |
| PC-43 / G-43 | V-8 | Drop cwd only from startup-slash actual launch call | owned-cwd-startup-slash; 1 | captured cwd == owned non-candidate directory | Shared cwd builder correct; exact caller capture/audit exercised |
| PC-44 / G-44 | V-8 | Drop cwd only from C514 actual launch call | owned-cwd-C514; 1 | captured cwd == owned non-candidate directory | Shared cwd builder correct; exact caller capture/audit exercised |
| PC-45 / G-45 | V-14 | Remove C680 ancestry-admission call | c680-cwd; 1 | unowned ancestor refuses before Start | Own leaf clean; parent contains undeclared input |
| PC-46 / G-46 | V-8 | Skip ancestor/import ownership enumeration | ancestor; 6 | refused; spawnCount=0 | Leaf owned and admissible; only one ancestor/import/hook/skill/MCP input undeclared |
| PC-47 / G-47 | V-8 | Return pre-run instruction digest at teardown | instruction-drift; 1 | credit=false | Change declared input only between pre/post checks |
| PC-48 / G-48 | V-11 | Append --setting-sources with empty value in PTY builder | sources; 1 | no injected empty setting-sources; captured source policy == independent request | Declared user/project/local sources present |
| PC-49 / G-49 | V-11 | Add off settings to control builder | control-overlay; 1 | control settings flag count=0 | Control fake otherwise produces valid bridge |
| PC-50 / G-50 | V-11 | Restore inline settings array in startup off method | off-callsite; 1 | required builder delegation | Shared builder remains correct |
| PC-51 / G-51 | V-11 | Restore inline settings array in startup slash method | slash-callsite; 1 | required builder delegation | Shared builder remains correct |
| PC-52 / G-52 | V-12 | Replace requested model with literal haiku | named-B; 2 | captured --model == context named-B | Use two distinct explicit fixture names |
| PC-53 / G-53 | V-12 | Insert a model flag in explicit production-default mode | production-default; 1 | no --model flag; mode remains explicit | All effective-model attestation present |
| PC-54 / G-54 | V-12 | Treat unspecified model selection as production-default | model-missing; 1 | refused; spawnCount=0 | All other context fields present |
| PC-55 / G-55 | V-12 | Accept no effective-model observation | effective-model; 1 | credit=false | Explicit request present; effective value unobserved |
| PC-56 / G-56 | V-15 | Restore C680 hard-coded/default Haiku branch | c680-model; 2 | named-B argv matches context; explicit default not hard-coded | Both C680 probes use capture seam |
| PC-57 / G-57 | V-9 | Omit user values from projection | drift-user; 1 | before digest != after digest; source entry present | Source proven loaded by captured source flags or always-loaded rule; only its behavior value changes |
| PC-58 / G-58 | V-9 | Omit project values from projection | drift-project; 1 | before digest != after digest; source entry present | Source proven loaded by captured source flags or always-loaded rule; only its behavior value changes |
| PC-59 / G-59 | V-9 | Omit local values from projection | drift-local; 1 | before digest != after digest; source entry present | Source proven loaded by captured source flags or always-loaded rule; only its behavior value changes |
| PC-60 / G-60 | V-9 | Omit overlay values from projection | drift-overlay; 1 | before digest != after digest; source entry present | Source proven loaded by captured source flags or always-loaded rule; only its behavior value changes |
| PC-61 / G-61 | V-9 | Omit managed-policy values from projection | drift-managed-policy; 1 | before digest != after digest; source entry present | Source proven loaded by captured source flags or always-loaded rule; only its behavior value changes |
| PC-62 / G-62 | V-9 | Omit global .claude.json values from projection | drift-global-.claude.json; 1 | before digest != after digest; source entry present | Source proven loaded by captured source flags or always-loaded rule; only its behavior value changes |
| PC-63 / G-63 | V-9 | Sort away effective source precedence | precedence; 1 | changed precedence != equal projection | Same files/values, winner changes |
| PC-64 / G-64 | V-9 | Use desired fixture list instead of actual captured setting-sources | loaded-set; 1 | actual loaded-source set matches argv; missing required source refuses | Actual flags omit one requested fixture source |
| PC-65 / G-65 | V-9 | Strip all projects/global state as volatile | trust-behavior; 1 | relevant project behavior edit changes digest | Owned path randomized, stable sibling behavior key changed |
| PC-66 / G-66 | V-10 | Hash known synthetic credential field as behavior | credential-exclusion; 1 | digest stable under credential-only edit; exported projection lacks value | Distinct safe synthetic values, never real credentials |
| PC-67 / G-67 | V-10 | Drop unrecognized keys silently | unknown-key; 1 | reuseEligible=false | All known fields unchanged; one unknown key |
| PC-68 / G-68 | V-10 | Replace unknown effective value with requested value | unknown-effective; 1 | reuseEligible=false | Requested settings healthy; effective observation unknown |
| PC-69 / G-69 | V-10 | Omit an extra loaded source from inventory | unknown-source; 1 | reuseEligible=false | Known sources all attested; one unresolved additional source |
| PC-70 / G-70 | V-10 | Map unreadable to proven absent | unreadable; 1 | reuseEligible=false | Injected read refusal, no missing file ambiguity |
| PC-71 / G-71 | V-10 | Map absent-unverified managed status to absent | managed-unverified; 1 | reuseEligible=false; status=absent-unverified | Conventional path absent, no CLI/operator proof |
| PC-72 / G-72 | V-10 | Collapse empty and absent projected values | presence; 1 | all three projections differ | Known allowed key; no unknown refusal masks comparison |
| PC-73 / G-73 | V-15 | Bypass shared projection in C680 entrypoint | c680-projection; 2 | managed/global edits invalidate C680 observation | PTY projection remains correct |
| PC-74 / G-74 | V-16 | Convert shared unknown result to reusable at C680 caller | c680-unknown; 1 | reuseEligible=false | Shared projection returns unknown managed source |
| PC-75 / G-75 | V-17 | Feed captured control argv to ApplyOff/parity expected side | independent-input; 1 | control extra-flag corruption fails equality | Genuine producer capture loaded, consumer corrupt-copy negative only |
| PC-76 / G-76 | V-17 | Skip control comparison and compare only off/slash | control-parity; 1 | control-only unknown flag fails equality | Off/slash captures still correct |
| PC-77 / G-77 | V-17 | Ignore settings flag/file differences during canonicalization | off-parity; 2 | missing/wrong off flag or JSON fails equality | Control valid; only off/slash payload differs |
| PC-78 / G-78 | V-17 | Remove only capture source comparison | capture-source; 1 | capture refused as invalid; no parity credit | Actual complete CP-18 capture, only one field changed |
| PC-79 / G-79 | V-17 | Remove only capture run comparison | capture-run; 1 | capture refused as invalid; no parity credit | Actual complete CP-18 capture, only one field changed |
| PC-80 / G-80 | V-17 | Remove only capture producer build comparison | capture-producer-build; 1 | capture refused as invalid; no parity credit | Actual complete CP-18 capture, only one field changed |
| PC-81 / G-81 | V-17 | Remove only capture producer UID comparison | capture-producer-UID; 1 | capture refused as invalid; no parity credit | Actual complete CP-18 capture, only one field changed |
| PC-82 / G-82 | V-17 | Remove only capture request/model comparison | capture-request-model; 1 | capture refused as invalid; no parity credit | Actual complete CP-18 capture, only one field changed |
| PC-83 / G-83 | V-17 | Accept a capture before successful terminal CP-18 receipt | producer-incomplete; 1 | invalid/incomplete; no parity credit | Capture content complete, producer terminal fact absent |
| PC-84 / G-84 | V-17 | Accept missing/partial required capture as empty equality | capture-cut; 2 | invalid; no parity credit | Other producer identities valid |
| PC-85 / G-85 | V-18 | Accept footer badge or foreign same-cwd session as bridge | bridge-source; 2 | 20 bridge-unestablished; bridge proof absent | Badge/JSONL present, owned per-process bridge absent |
| PC-86 / G-86 | V-18 | Credit management menu without new owned bridge | slash-setup; 1 | no bridge -> non-green | Off control succeeded; slash was submitted; no new bridge |
| PC-87 / G-87 | V-18 | Remove C514 setup delegation and assume ambient bridge | menu-setup; 1 | required setup delegation and owned bridge before dismissal | Startup helper correct; exact C514 caller inspected |
| PC-88 / G-88 | V-19 | Create independent control for each dependent | control-once; 1 | controlStarts=1 for concurrent dependents | Manual barriers overlap off/slash requests |
| PC-89 / G-89 | V-19 | Start off/slash before awaiting control task | control-order; 1 | dependentStarts=0 until control completes | Control parked at manual barrier; no native waits |
| PC-90 / G-90 | V-19 | Treat absent control as successful prerequisite | control-absent; 1 | 20 premise-absent; credit=false | Off never has bridge; other setup/evidence healthy |
| PC-91 / G-91 | V-19 | Accept a completed prior-run control | control-foreign; 1 | 20 premise-absent; credit=false | Full valid old observation, new run differs |
| PC-92 / G-92 | V-19 | Ignore control/dependent binary identity mismatch | control-binary; 1 | 20 premise-absent; credit=false | Only one shared experiment identity differs |
| PC-93 / G-93 | V-19 | Ignore control/dependent profile identity mismatch | control-profile; 1 | 20 premise-absent; credit=false | Only one shared experiment identity differs |
| PC-94 / G-94 | V-19 | Ignore control/dependent settings identity mismatch | control-settings; 1 | 20 premise-absent; credit=false | Only one shared experiment identity differs |
| PC-95 / G-95 | V-18 | Allow control receipt with a user submission | control-zero-prompts; 1 | modelPromptCount=0; nonzero premise refused | Owned bridge otherwise valid |
| PC-96 / G-96 | V-20 | Restore eight-second OverlayObserve | late-bridge; 1 | bridge at 12 s causes assertion failure | Control healthy; fake clock advances without wall sleep |
| PC-97 / G-97 | V-20 | Use constant 15 s instead of max with latency | latency-bound; 1 | observation end >=18 s | Direct oracle fixture latency=18 s, no native timeout changed |
| PC-98 / G-98 | V-20 | Return success before reading event at end of window | deadline-bridge; 1 | bridge at 15 s causes assertion failure | Control latency below15; boundary event delivered first |
| PC-99 / G-99 | V-21 | Remove/ignore throwing oracle in control live method | startup-control; 1 | audit rejects missing reachable verdict/control delegation | Helpers untouched and healthy |
| PC-100 / G-100 | V-21 | Remove/ignore throwing oracle in off live method | startup-off; 1 | audit rejects missing reachable verdict/control delegation | Helpers untouched and healthy |
| PC-101 / G-101 | V-21 | Remove/ignore throwing oracle in slash live method | startup-slash; 1 | audit rejects missing reachable verdict/control delegation | Helpers untouched and healthy |
| PC-102 / G-102 | V-22 | Drop user marker requirement | marker-user; 1 | 20 settings-unobserved | Only named marker missing; others/bridge valid |
| PC-103 / G-103 | V-22 | Drop project marker requirement | marker-project; 1 | 20 settings-unobserved | Only named marker missing; others/bridge valid |
| PC-104 / G-104 | V-22 | Drop local marker requirement | marker-local; 1 | 20 settings-unobserved | Only named marker missing; others/bridge valid |
| PC-105 / G-105 | V-22 | Ignore marker's owned session identity | marker-foreign; 1 | 20 settings-unobserved | All source markers present, one belongs to another session |
| PC-106 / G-106 | V-22 | Remove retained-setting equality assertion | retained-setting; 1 | changed permitted setting causes assertion failure | All actual-source markers present, valid control |
| PC-107 / G-107 | V-23 | Use identifying head/window instead of IsCompleteIn | prompt-partial; 2 | RequireSubmit/RequireMenu throws | Same session/floor/type; head-only or head+tail splice |
| PC-108 / G-108 | V-23 | Accept a body below RequiresTextMatch threshold | prompt-short; 1 | invalid prompt fixture refuses | Same session has unrelated user record; weak arm otherwise returns true |
| PC-109 / G-109 | V-23 | Accept QueuedUserPrompt or screen record as receipt | prompt-kind; 2 | verdict throws; receipt=false | Full correct body/session/floor with wrong kind |
| PC-110 / G-110 | V-23 | Ignore transcript session equality | prompt-session; 1 | verdict throws | Full body/type/floor; only session differs |
| PC-111 / G-111 | V-23 | Ignore generation equality | prompt-generation; 1 | verdict throws | Same session ID reused, new generation/binding |
| PC-112 / G-112 | V-23 | Ignore native binding equality | prompt-native-binding; 1 | verdict throws | Same session ID reused, new generation/binding |
| PC-113 / G-113 | V-23 | Remove sequence > floor condition | prompt-floor; 1 | verdict throws | Matching old full UserPrompt at floor |
| PC-114 / G-114 | V-23 | Accept prompt receipt alone as completed turn | response-missing; 1 | RequireSubmit throws | Valid full prompt; no assistant response |
| PC-115 / G-115 | V-23 | Accept unrelated turn completion | completion-turn; 1 | RequireSubmit throws | Valid prompt and response; completion belongs to prior turn |
| PC-116 / G-116 | V-23 | Ignore response/completion sequence ordering | completion-order; 1 | RequireSubmit throws | Matching identities but records ordered incorrectly |
| PC-117 / G-117 | V-23 | Ignore intervening user prompt when assigning the completion | completion-intervening; 1 | RequireSubmit throws | Matching identities but records ordered incorrectly |
| PC-118 / G-118 | V-23 | Accept end_turn on thinking without final text | thinking-end; 1 | RequireSubmit throws | Real split-response shape, text sibling withheld |
| PC-119 / G-119 | V-23 | Treat typed error response as completion | error-turn; 1 | non-green; typed error retained for S1b | Full prompt then correlated typed unavailable/error |
| PC-120 / G-120 | V-24 | Use done/composer predicate as verdict input | enter-lost; 2 | RequireSubmit/RequireMenu throws | Done-like screen and whole draft present, no UserPrompt |
| PC-121 / G-121 | V-24 | Join body and Enter into one write | write-shape; 1 | captured body framing/normalization and separate Enter match | Recipient initially ready; multi-line CRLF input fixture |
| PC-122 / G-122 | V-24 | Bypass dialog check for permission/question/unknown | dialog-blocked; 3 | taskBodyWrites=0; enterWrites=0 | Readiness otherwise healthy; one unresolved dialog |
| PC-123 / G-123 | V-24 | Treat busy as ready | busy-recipient; 1 | bodyWrites=0 until ready barrier | Fake transcript/clock valid; later readiness admits exactly once |
| PC-124 / G-124 | V-24 | Retry submission after missing receipt/read failure | no-resend; 5 | submissionAttempts<=1; no false pass | Fault at each direct-input cut with independent transcript script |
| PC-125 / G-125 | V-25 | Omit before-Esc bridge comparison | bridge-before-Esc; 2 | RequireMenu throws | Complete matching UserPrompt; only this phase drops/changes bridge |
| PC-126 / G-126 | V-25 | Omit after-Esc bridge comparison | bridge-after-Esc; 2 | RequireMenu throws | Complete matching UserPrompt; only this phase drops/changes bridge |
| PC-127 / G-127 | V-25 | Omit after-submit bridge comparison | bridge-after-submit; 2 | RequireMenu throws | Complete matching UserPrompt; only this phase drops/changes bridge |
| PC-128 / G-128 | V-25 | Ignore bridge sidecar process binding | bridge-process; 1 | RequireMenu throws | bridge-A present in foreign process/session only |
| PC-129 / G-129 | V-25 | Ignore bridge sidecar start binding | bridge-start; 1 | RequireMenu throws | bridge-A present in foreign process/session only |
| PC-130 / G-130 | V-25 | Ignore bridge sidecar session binding | bridge-session; 1 | RequireMenu throws | bridge-A present in foreign process/session only |
| PC-131 / G-131 | V-25 | Reuse previous bridge on read failure | bridge-read-failure; 1 | RequireMenu throws | Pre-Esc valid, later owned sidecar unreadable |
| PC-132 / G-132 | V-26 | Replace the final verdict with a screen assertion in submit live method | verdict-submit-live-method-last-assertion; 1 | audit rejects final screen/guarded/ignored verdict | Real helper correct; actual caller alone changed |
| PC-133 / G-133 | V-26 | Catch and swallow the throwing verdict in submit live method | verdict-submit-live-method-outside-catch; 1 | audit rejects final screen/guarded/ignored verdict | Real helper correct; actual caller alone changed |
| PC-134 / G-134 | V-26 | Add a post-readiness screen Should assertion before the final verdict in submit live method | verdict-submit-live-method-no-screen-assertion; 1 | audit rejects final screen/guarded/ignored verdict | Real helper correct; actual caller alone changed |
| PC-135 / G-135 | V-26 | Replace the final verdict with a screen assertion in C514 method | verdict-C514-method-last-assertion; 1 | audit rejects final screen/guarded/ignored verdict | Real helper correct; actual caller alone changed |
| PC-136 / G-136 | V-26 | Catch and swallow the throwing verdict in C514 method | verdict-C514-method-outside-catch; 1 | audit rejects final screen/guarded/ignored verdict | Real helper correct; actual caller alone changed |
| PC-137 / G-137 | V-26 | Add a post-readiness screen Should assertion before the final verdict in C514 method | verdict-C514-method-no-screen-assertion; 1 | audit rejects final screen/guarded/ignored verdict | Real helper correct; actual caller alone changed |
| PC-138 / G-138 | V-26 | Replace the final verdict with a screen assertion in submit harness claude branch | verdict-submit-harness-claude-branch-last-assertion; 1 | audit rejects final screen/guarded/ignored verdict | Real helper correct; actual caller alone changed |
| PC-139 / G-139 | V-26 | Catch and swallow the throwing verdict in submit harness claude branch | verdict-submit-harness-claude-branch-outside-catch; 1 | audit rejects final screen/guarded/ignored verdict | Real helper correct; actual caller alone changed |
| PC-140 / G-140 | V-26 | Add a post-readiness screen Should assertion before the final verdict in submit harness claude branch | verdict-submit-harness-claude-branch-no-screen-assertion; 1 | audit rejects final screen/guarded/ignored verdict | Real helper correct; actual caller alone changed |
| PC-141 / G-141 | V-22 | Suppress the hook writer output while leaving child exit zero | marker-writer; 1 | actual marker file contains each expected source/session/retained-setting record | Invoke actual declared hook in owned node child; parent writes no presence marker |

Design audit: **guards=141, mapped=141, missing=0, duplicate PC maps=0**.
Execution audit: **PCs run=0/141; variant cycles run=0/166**.
No build/test/mutation was authorized in TestDesign. All PCs and all variants
must run before Mutation completion; these pending facts are not a claim that
controls have already run. Each cycle is baseline green / break / exact assertion
red / restore / green with distinct output/evidence and forced builds. Use only
that PC's exact method filter, never a class/suite. Await each child before edit.
Shared source/method controls cannot batch. Restore every mutation and retain
per-PC restoration evidence externally for SourceLanding; never commit from its
snapshot. Code must bind enumerated variant identities/counts, not leave "each"
as an unexpanded executor instruction.

### Defect coverage and boundary ownership

| Review item | Named witness / disposition |
|---|---|
| a62610b7 D-1 | ControlRequiredForSettingsOff, ControlWindowBoundsOffObservation, StartupMethodsUseControlOracle and LoadedSettingsSurviveOffOverlay (V-19..22); CP-3; native CP-6 later |
| a62610b7 D-2 | PromptReceiptPrecedesVerdict, LostEnterCannotPass, DroppedBridgeCannotPass, LiveMethodsUseTranscriptVerdicts (V-23..26); CP-19; native CP-5/7 later |
| a62610b7 D-3 | CwdInstructionsCannotLeak in both projects (V-8/14), LaunchArgumentsKeepProductionSources (V-11); repository closure/rot tests remain S2a-1 CP-8, not copied here |
| a62610b7 D-4 | ClSessionUsesAttestedExecutable and C680UsesAttestedExecutable, both missing-context and image-mismatch methods (V-1..6); CP-17/23 |
| a62610b7 D-5 | DedicatedConfigOwnsTrustAndEvidence, SettingsProjectionDetectsDrift, UnknownConfigDisablesReuse in both projects (V-7/9/10/13/15/16); CP-18/24 |
| a62610b7 D-6 | V-18/19/22/23 emit typed setup/recipient facts. S1b-outcomes owns LiveLaneOutcomeTests.MidRunOutageAfterReceiptIsInconclusive, HealthyProviderAssertionStaysRed and TypedSetupOutcomes (CP-16). No duplicated classifier; healthy/unproven assertion remains RED, correlated typed outage may become 20, never GREEN |
| a62610b7 D-7 | S1c-sanitization owns LiveClaudeEvidenceWiringTests.EvidenceIsSanitized and CanarySinksCannotBypassSanitizer (CP-20), LiveLaneEvidenceTests.C680EvidenceIsSanitized and ArtifactGateRequiresPresence (CP-26), completed scanner CP-29. Current raw sinks remain a native activation blocker; this companion grants no sanitization credit |
| 917262f2 D-1 | CP-17/18/3/19 build and run actual PTY assembly; each combines caller invocation with named structural audit. C680 separately builds/runs CP-23/24. CP-20 belongs to sanitization but must also use PTY project |
| 917262f2 D-2; 7973fc74 R3-D-1 | Full preservation below plus external S1a commission-scoped FullCensusUnchanged (CP-30). No permanent seed-pinned Unit test or self-comparison is added |
| 917262f2 D-3; 7973fc74 R3-D-2 | External sanitization CP-20/26/29; markers from API under test, exact-UID TRX presence, console/HTML/nested-files absence scan after completion. No in-flight TRX read or console-presence requirement here |
| 917262f2 D-4 | LaunchArgumentsKeepProductionSources, ModelSelectionComesFromContext, StartupArgvMatchesProductionApplyOff, both settings matrices and LoadedSettingsSurviveOffOverlay; V-9..12/15..17/22 |
| 917262f2 N-1; 7973fc74 A-5 | CoreCallersRefuseMissingContext and C680RefusesMissingContext explicitly pass core UID with neither env nor context; V-2/5 |
| 7973fc74 A-1 | V-7 audit names FindSessionJsonl/ReadSessionFile/Trust UserProfile sites; V-11 audit names both inline arrays and requires builder delegation, not line numbers |
| 7973fc74 A-2 | V-17 expected side is real AgentRegistry fixed-request argv, computed independently before reading PTY capture; corruption on control alone must fail |
| 7973fc74 A-3 | V-10/16 preserve absent-unverified managed status; parent CP-22 LiveLaneAttestationTests.UnknownSettingsRefuseReuse consumes it and refuses reuse |
| 7973fc74 A-4 | V-26 checks last assertion, outside try/catch, and absence of post-readiness screen assertions separately; distinct PCs for each caller/guard |
| 7973fc74 A-6 | S1a owns Invoke-NightlyProduceDiscovery/TestNodeUid baseline capture; no reflection UID substitute, new census implementation or CP-27/28 run here |

CP-22's six attestation consumers remain S2a-2: CliVersionOrBytesInvalidates,
WrapperPayloadInvalidates, AttestedPathMismatchRefuses, ConfigurationInvalidates,
UnknownSettingsRefuseReuse and ExternalEpochIsNotInferred. This design supplies
their source observations but does not implement their reuse decision. D-6 is
unsettled and no S1c green enables reuse. CP-20/26/22 are boundary references,
**not rows to execute for these four Code commissions**. CP IDs are those of the
parent plan; the S1b companion's local CP-23 regression row is not this plan's
C680 CP-23. Always retain plan path/digest with CP ID.

### Full-census-unchanged guarantee

Do not change `tests/test-execution-policy.json`, its policyHash inputs
`scripts/lib/nightly-{common,policy,coverage}.ps1`, release/gate readers,
Full environment clearing, existing exclusions, test method names/arguments or
existing categories/Explicit metadata in these four slices. In particular C680
remains a **required Full skip**, not an exclusion or a claimed Full pass.
The separate S1a change alone adds the six LiveClaude tags.

Each Code commission compares these policy/reader bytes and existing core/fake
method metadata against its **actual dispatch base Git objects**, before and
after its change; store the comparison with its source SHA and file hashes.
This is a commission source-footprint audit (no build/test driver and no durable
Unit seed pin). Review checks the exact diff, linked csproj closure, and compiled
TRX identities. The six existing lane UIDs/argument expansion and two fakeclaude
UIDs cannot be renamed or reclassified here. Link new helpers as Compile inputs,
not duplicated test classes. New UIDs are honest additions: 25 ordinary Unit
methods and V-17's new commission-only Explicit Unit method. Full's disposition
on the **preexisting roster** is unchanged; its total discovery count grows.

The complete frozen discovery/required-excluded comparison remains S1a CP-30
using the real nightly producer and pinned seed/candidate Git inputs. No full
suite is run just to repeat that commission, and this document does not pretend
a source audit is a measured Full execution. Source edits outside the allowed
footprint or a changed existing UID return to Plan before credit. The audit is
a review obligation, not a newly implemented runtime safety guard or extra PC.

### Out of scope

- S1a inventory/tagging/baseline census; S1b admission/outcomes; sanitization
  CP-20/26/29; S2 attestation/reuse/signing; S3 release policy; native qualification.
- Product queue/persistence/delivery/retry changes, AppHost/DB startup, network,
  provider calls and credentials. ExpectationDeliveryFixture is an inspected
  reference for evidence semantics, not a portable fixture to boot here.
- Real Windows image/cwd/profile/layout/source resolution, hooks actually loaded
  by Claude, ConPTY write fidelity, bridge and recipient receipt: CP-4..7 remain
  pending behind D-3/D-4/D-11 and sanitization. CP-21 preserves the two native
  fake submit methods separately. Linux fake observations cannot qualify them.
- No model fallback, deployment, purchase, release/master/Interim credit,
  enforcement or choice of a real profile/issuer/reuse horizon.

### Implementation steps

Each numbered step is **one separate Code commission**. Authoring allowance is
30-60 minutes per step; checkpoint/slot time is additional and explicit below.
Use the assigned branch; commit/push each red witness before its fix and each fix
before a run. Retain actual red receipts. Do not commission four overlapping
writers: executable -> profile -> startup -> delivery share ClSession/canary files.

1. **S1c-executable, 30-60 minutes.** Add the six V-1..6 methods, linked
   LiveClaudeLaunchContext/LiveClaudeExecutable and capture-only callable seams;
   initially preserve legacy behavior so PATH/context/image witnesses go red.
   Implement UID-based admission, owned selection and observations; wire every
   core caller including C680's version probe. Run CP-17 then CP-23 on each red/fix
   committed slice. Preserve the non-core legacy branch. Bind PC sites/variants.
2. **S1c-profile, 45-60 minutes.** Add eleven V-7..17 methods, profile paths,
   owned cwd/settings/projection and actual builder seams. Red witnesses include
   wrong-home evidence, omitted cwd and independent argv corruption. Add the
   declared hook and provider-free capture wrapper; make V-17 Explicit with
   missing-context refusal. Implement context model and complete sources. Run
   CP-18 then CP-24 through that wrapper; retain complete producer/consumer
   provenance. Bound payloads remain binding-required until Code records them.
3. **S1c-startup-oracles, 30-60 minutes.** Add five V-18..22 methods and the
   manual clock/bridge observations; commit absent-control/late-bridge reds.
   Implement run-owned once-only control, ordered dependents, window, owned
   sidecar and loaded-setting evidence; update all three live callers and C514's
   shared bridge setup call. Run only CP-3. Do not buy a startup probe or change
   the provisional 15-second control deadline to make a fake case pass.
4. **S1c-delivery-oracles, 45-60 minutes.** Add four V-23..26 methods against
   the actual submit/menu reader/verdict path; commit Enter-lost, foreign/partial
   prompt and bridge-dropped reds. Implement full receipt/turn correlation and
   same-bridge observations. Audit exact live call flow while preserving the
   fakeclaude branch and backend selection. Run only CP-19; native CP-21 remains
   a separate later acceptance row. Bind every method-scoped PC and report limits.

If implementing a callable seam requires a product queue change, test executable
reference, permissive audit or unbound substitute, stop that expansion and return
`next: plan` naming the seam. A real host/model/spend/issuer/reuse decision is
`next: decide` for that later work, never an invented answer in these commissions.

## Performance budget

**Estimated, unmeasured, sample n=0** for the new portable methods/builds on this
source. Parent historical Antiphon.Tests build 509.43 s (n=1, different source)
is context, not this host's p95. CARD-1545 owns collection, CARD-1404 calibration
and CARD-1514 timing/over-30-minute Debug. Collect first three naturally occurring
red/fix/Review observations, then use about 2x comparable p95; do not buy repeats
solely to collect samples or increase a timeout after red.

| Phase | Expected / allocated | Explicit provisional bound and evidence |
|---|---|---|
| Tool/bootstrap per Code commission | 2 min | Slot-gated, record source/tool hash and duration; 8 min across four separate environments |
| PTY isolated build | 5 min | Scheduler build cap 16 min, see supported-schema exception below; restore/build timing separate |
| Antiphon.Tests isolated build | 8 min | 16 min build cap; not a measured p95 claim |
| Portable method | <=15 s target | 30 s body cap, 10 s cleanup allowance; filesystem/parse/audit/capture phases measured separately |
| PTY ordinary class | 1 min allocation | 2 min row cap; internal matrices reuse immutable definitions and fresh tiny subject scratch roots |
| Antiphon.Tests ordinary class | 2 min allocation | 4 min row cap; V-17 reads completed artifacts, starts no nested test driver |
| Slot admission | Unknown percentile, 0 floor | 5 min per acquisition; separate wait duration, no unleased retry |
| Each two-project After group | 16 min expected | 26 min charged execution, 29 min hard wall; incomplete rows stay incomplete if cap expires |
| Each single PTY After group | 6 min expected | 18 min charged execution, 23 min hard wall |
| Settings hook synthetic invocation in V-22 | <=1 s target | 5 s child deadline plus 2 s exit/drain; owned node child only, no provider/network; assembly ProcessSpawnLimit |
| Source/receipt IO | <=5 s aggregate target per method | Method deadline/cancellation; bounded input files, no ambient recursive home enumeration |

The checkpoint scheduler's `BuildDeadlineMinutes` has a **15-minute floor** and
one manifest default, so the parent plan's proposed 12-minute PTY build cap
cannot be represented independently of row timeouts in this paired manifest.
Use the supported 16-minute build default, retaining 2/4-minute row caps; this is
a documented temporary CARD-1545/CARD-1404 exception, not an invented timeout
column or reason to alter the checkpoint tool. The run-level hard wall remains
finite. If first measured builds permit smaller budgets, qualify the supported
route before changing them; do not claim the 16-minute cap is p95-derived.

No AppHost/database/container/migrations are started. Share immutable fixtures
once per assembly; create per-case scratch only where path/ownership is the
subject. A future native run shares expensive DB setup once per assembly, but
fresh CLI sessions are the startup/delivery subject. Phase evidence records
run/group/test/variant/source/host/platform/category and planned/executed roster;
separate slot, restore/build, file IO, parsing, callbacks/virtual waits, body,
child startup/exit/drain if any, artifact publication and cleanup. Unknown spans
remain null with remainder; absent infrastructure phases are N/A. TRX timings
are actual wall time; virtual 18-second observations are not 18 seconds of runtime.

New slow-project extraction is unnecessary for these small methods. The two
existing projects are the mandatory caller boundaries; no cross-assembly test
reference is introduced. Both assemblies execute sequentially. The V-22 owned
script child has its assembly-local ProcessSpawnLimit; never co-schedule
the assemblies. Runtime source-audit, hook and wrapper paths are declared inputs,
not assumed to be covered by ProjectReference discovery.

### Checkpoints

Closed ordinary list for this companion only. One isolated build and one exact
filter per row; all rows serial. The union is **26 TUnit executions**, not matrix
case count. Do not import other companion tables by CP number alone.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-17 | S1c-executable | tests/Antiphon.Agents.Pty.Tests -> bin-live-pty-executable/ | portable-live-pty-executable | /*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/* | V-1..3, R-1, R-5 | ClSessionUsesAttestedExecutable,CoreCallersRefuseMissingContext,ExecutedImageMismatchRefuses; exactly 3 passed, 0 failed/skipped | 3 | 6 | true |
| CP-23 | S1c-executable | tests/Antiphon.Tests -> bin-live-executable/ | portable-live-c680-executable | /*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/* | V-4..6, R-1 | C680UsesAttestedExecutable,C680RefusesMissingContext,C680ImageMismatchRefuses; exactly 3 passed, 0 failed/skipped | 3 | 10 | true |
| CP-18 | S1c-profile | tests/Antiphon.Agents.Pty.Tests -> bin-live-pty-isolation/ | portable-live-pty-isolation | /*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/* | V-7..12, R-2, R-5 | DedicatedConfigOwnsTrustAndEvidence,CwdInstructionsCannotLeak,SettingsProjectionDetectsDrift,UnknownConfigDisablesReuse,LaunchArgumentsKeepProductionSources,ModelSelectionComesFromContext; exactly 6 passed, 0 failed/skipped | 6 | 6 | true |
| CP-24 | S1c-profile | tests/Antiphon.Tests -> bin-live-isolation/ | portable-live-c680-isolation | /*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/* | V-13..17, R-2 | DedicatedConfigOwnsTrustAndEvidence,CwdInstructionsCannotLeak,SettingsProjectionDetectsDrift,UnknownConfigDisablesReuse,StartupArgvMatchesProductionApplyOff; exactly 5 passed incl. Explicit parity method, 0 failed/skipped | 5 | 10 | true |
| CP-3 | S1c-startup-oracles | tests/Antiphon.Agents.Pty.Tests -> bin-live-fixture/ | portable-live-fixture | /*/Antiphon.Agents.Pty.Tests/LiveClaudeStartupWiringTests/* | V-18..22, R-3 | ExplicitBridgeSetup,ControlRequiredForSettingsOff,ControlWindowBoundsOffObservation,StartupMethodsUseControlOracle,LoadedSettingsSurviveOffOverlay; exactly 5 passed, 0 failed/skipped | 5 | 6 | true |
| CP-19 | S1c-delivery-oracles | tests/Antiphon.Agents.Pty.Tests -> bin-live-delivery/ | portable-live-delivery | /*/Antiphon.Agents.Pty.Tests/LiveClaudeDeliveryWiringTests/* | V-23..26, R-4, R-5 | PromptReceiptPrecedesVerdict,LostEnterCannotPass,DroppedBridgeCannotPass,LiveMethodsUseTranscriptVerdicts; exactly 4 passed, 0 failed/skipped | 4 | 6 | true |

R-6 is the commission source audit alongside these rows, not an additional test
driver. Baseline-red witness runs use the same affected CP rows at their red
commit, reported as expected assertion failures and charged separately. Following
a fix, run the affected rows once at the committed source; rerun only for a new
failure/change. No Full, native, broad Unit or unlisted regression run is implied.

Use the checkpoint tool per committed After group and wait to terminal status
(exit other than 75). Bootstrap/import through the build-slot gate, for example:

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label s1c-tool-import -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-s1c-tool/ -- import --plan docs/superpowers/plans/2026-10-31-card-1545-s1c-test-design.md --out .antiphon/s1c-checkpoints.yaml
```

Then invoke the actual built tool DLL directly, recording path/hash (no implicit
second tool build or outer held slot). Preserve imported selections/counts/builds;
in the ignored manifest set only `build.slotWaitMinutes: 5`,
`timeouts.rowMinutes: 16`, `parallel.maxRows: 1`, PTY
`checkpoints[].timeoutMinutes: 2` and Antiphon.Tests rows `timeoutMinutes: 4`.
Use positional manifest path, `--after <exact-slice>`,
`--expected-source-sha <committed-sha>`, `--serial`, and the matching
`--total-timeout 26m --hard-wall-timeout 29m` (paired) or
`--total-timeout 18m --hard-wall-timeout 23m` (single PTY).
S1c-profile's provider-free isolation wrapper owns this invocation/context and
awaits the same run ID; it must not run a second checkpoint host from a test.

Retain unedited CHECKPOINT lines, fresh exact UIDs/counts, imported/resolved
manifest hashes and clean source/build provenance. Validate receipts against the
committed SHA. Build-slot timeout exit 4 means not run; do not bypass the gate.
Delete only owned `bin-*` outputs after evidence retention. TRX, JSON captures,
receipts and logs stay ignored. Code/Review run `scripts/check-evidence-diff.ps1`
over their actual base..HEAD. No source edit while a run is in flight.

### Cost

All figures are **estimated, not measured**, exclude authoring and unknown slot
wait, and retain historical reds. Ordinary V/R floor (Code) is exactly the sum
of the six CP EstimatedMinutes: **6 + 10 + 6 + 10 + 6 + 6 = 44 minutes**.
That is six isolated builds (four PTY x 5 + two Antiphon.Tests x 8 = 36 minutes)
plus eight allocated execution minutes. Per commission: executable 16, profile
16, startup 6, delivery 6. Four separate tool bootstraps allocate **8 minutes**.

Required pre-fix red-witness runs use the same six filters at their red commits:
another **44 estimated minutes**, reported separately from the ordinary green
floor. Thus estimated Code machine total is **8 setup + 44 red + 44 green = 96
minutes**, spread over the four commissions. Actual failed/rerun/build costs must
be recorded, not silently deducted from that number.

PC floor (Mutation) uses **141 controls / 166 variant cycles**. Each
variant has three fresh method-scoped forced builds/runs: baseline, mutant red,
restored green. PTY: 3 x (5 build + 0.25 method) + 0.25 restore/binding bookkeeping
= **16 minutes/cycle**. Antiphon.Tests: 3 x (8 + 0.25) + 0.25 = **25 minutes/cycle**.
Payload authoring/binding and any one-time Mutation tool bootstrap are unmeasured
additional work, not free execution. The table names every exact PC filter:

| Exact method filter | Variant cycles | Estimated PC minutes |
|---|---:|---:|
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/ClSessionUsesAttestedExecutable` | 8 | 128 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/CoreCallersRefuseMissingContext` | 10 | 160 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeExecutableWiringTests/ExecutedImageMismatchRefuses` | 8 | 128 |
| `/*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/C680UsesAttestedExecutable` | 2 | 50 |
| `/*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/C680RefusesMissingContext` | 2 | 50 |
| `/*/Antiphon.Tests.Scripts/LiveLaneExecutableTests/C680ImageMismatchRefuses` | 1 | 25 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/DedicatedConfigOwnsTrustAndEvidence` | 7 | 112 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/CwdInstructionsCannotLeak` | 12 | 192 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/SettingsProjectionDetectsDrift` | 9 | 144 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/UnknownConfigDisablesReuse` | 7 | 112 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/LaunchArgumentsKeepProductionSources` | 4 | 64 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeIsolationWiringTests/ModelSelectionComesFromContext` | 5 | 80 |
| `/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/DedicatedConfigOwnsTrustAndEvidence` | 3 | 75 |
| `/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/CwdInstructionsCannotLeak` | 1 | 25 |
| `/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/SettingsProjectionDetectsDrift` | 4 | 100 |
| `/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/UnknownConfigDisablesReuse` | 1 | 25 |
| `/*/Antiphon.Tests.Scripts/LiveLaneIsolationTests/StartupArgvMatchesProductionApplyOff` | 12 | 300 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeStartupWiringTests/ExplicitBridgeSetup` | 5 | 80 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeStartupWiringTests/ControlRequiredForSettingsOff` | 7 | 112 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeStartupWiringTests/ControlWindowBoundsOffObservation` | 3 | 48 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeStartupWiringTests/StartupMethodsUseControlOracle` | 3 | 48 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeStartupWiringTests/LoadedSettingsSurviveOffOverlay` | 6 | 96 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeDeliveryWiringTests/PromptReceiptPrecedesVerdict` | 15 | 240 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeDeliveryWiringTests/LostEnterCannotPass` | 12 | 192 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeDeliveryWiringTests/DroppedBridgeCannotPass` | 10 | 160 |
| `/*/Antiphon.Agents.Pty.Tests/LiveClaudeDeliveryWiringTests/LiveMethodsUseTranscriptVerdicts` | 9 | 144 |

PC execution floor = **140 PTY cycles x 16 + 26 Antiphon.Tests cycles x 25 =
2890 minutes**. Total planned machine floor including required pre-fix evidence
= **8 setup + 36 ordinary builds + 8 ordinary V/R execution + 44 pre-fix red +
2890 PC baseline/red/restore/green = 2986 minutes**, plus admission waits,
payload binding and Mutation setup. Minimum final-green Code + Mutation subtotal,
excluding pre-fix history, is **2942 minutes**; neither subtotal claims native
acceptance or a Full run.

The mandatory cost audit reveals two over-30-minute obligations: executable and
profile each cost **32 minutes** for combined red+green verification (34 with their
bootstrap), and the PC battery costs 2890 minutes. **Commission Debug timing/cost
analysis before those combined batteries are repeated/commissioned**, with exact
roster/source/host/load and phase evidence; do not hide the cost by relabelling
cycles as unrelated runs. The four separate green groups remain 16/16/6/6 minutes.
This is a scheduling/performance prerequisite, not authorization to omit a PC,
weaken a refusal or launch a paid sample. Any newly measured >30-minute row/run
also triggers Debug under R1.

Savings explicitly credited: **0 minutes** of build or PC reuse. Both caller
assemblies must compile, each slice has a separate source identity, and controls
share support files; no independent-pack batching or forced-build exception has
been qualified. Exact filters avoid a broad suite, but no numerical Full savings
are claimed without comparable timing. A later measured Debug finding may qualify
a cheaper schedule through the owner contract; this design does not assume it.

### Completion and handoff

Documentation inspection and static roster/guard/table audits only; **builds=0,
tests=0, PCs=0**, ordinaryScopeCompleted=None. No execution or native/source-clean
certificate is claimed. All 141 PCs remain mandatory after Code/Review/land,
with payloads **binding required after Code** and all 166 variants still pending.
Design guard counts are 141/141/0/0 (total/mapped/missing/duplicate maps).

Next: Code as four separate commissions in the sequence above, preserving the
ordinary closed lists and performance prerequisite. No operator answer is needed
for owned provider-free seams; the standing isolated-lanes authority suffices.
Keep D-3/D-4/D-5/D-6/D-9/D-11 unresolved for later native/activation choices.
