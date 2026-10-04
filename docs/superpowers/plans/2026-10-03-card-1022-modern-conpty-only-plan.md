# CARD-1022: retire the Windows inbox ConPTY backend in three releases

Plan date: 2026-10-03. Inspected source: `2d3c582416c5610d72e53df3e790bc114d87ad82`.
Card: Antiphon / CARD-1022. Operator decision: deprecate inbox completely; no further
live inbox qualification. This dispatch changes this plan only. TestDesign remains a
separate stage; the checkpoint proposal below is its starting selection, not completed
verification or authorization to implement all three releases together.

## Ground truth

| Card assumption | What this checkout does | Planning consequence |
|---|---|---|
| Modern is effectively the default. | `src/Antiphon.Agents.Pty/PtyBackend.cs:68-105` resolves unset, whitespace and every unrecognised value to `InboxConhost`. `SessionRunner/appsettings.json:4` requests modern and `Antiphon.AppHost/Program.cs:76` exports modern for the server. | Flip the Windows **code** default, not merely appsettings. Preserve explicit precedence and record raw request versus resolved backend. |
| Tests and E2E get inbox when unset. | Five `PtyBackendEnvGuard.cs` copies clear the environment and assert inbox. Many native fixtures explicitly pin inbox; others explicitly pin modern. `E2E/Fixtures/IsolatedSessionRunner.cs:157` optionally requests modern, and a real runner can also load its modern appsettings. | Unset policy is inbox, but not every fixture actually uses it. Keep environment isolation, change default assertions, and audit explicit and implicit pins separately. |
| Missing modern falls back with a Warning. | `TryLocate` checks Windows and the existence of both files; its exclusive `ANTIPHON_CONPTY_DIR` override cannot fall through. Missing/half-staged pairs resolve inbox; `Program.cs` logs `FellBack` at Warning. | Keep observable fallback only in release A; release B refuses. Missing pair and actual DLL/export/spawn failure are different test cases. |
| Presence proves the modern host can load. | `ConPtyRedistributable.cs:79-121` does not load the DLL or validate the executable. `ModernConPtyConnection.LoadModule` loads exports at spawn and throws on failure. File existence alone is not an executable preflight. | B needs Windows readiness validation before runner advertisement and at actual host launch, with native-failure evidence. No new fallback in exception handlers. |
| Delete Porta.Pty when inbox goes. | `PtyAgentRunner.cs:143-207` resolves the same enum on every OS. On Linux, Porta is the working Unix PTY and tracked launches use Linux containment. `PtySession.cs` contains both `IPtySession` and `PortaPtySession`; modern still consumes Porta's `PtyOptions`. | Remove the Windows Porta dispatch/argv arm only. Retain the package, interface, Unix adapter, Unix argv guard and cgroup custody. No CARD-0038 transport rewrite. |
| `InboxConhost` means a Windows host. | Linux Compose files explicitly set `inbox`; Linux capabilities consequently report `InboxConhost`. `TryLocate("modern")` on Linux also leads to inbox today. | Introduce a truthful `UnixPty` resolution/report in A, with unchanged Unix transport and limits. It does not establish a new measured paste envelope. |
| Backend choice and advertised choice are one fact. | Daemon startup gives environment precedence over config, but runtime host launch passes `_settings.PtyBackend`; runtime custody resolves settings while both capability builders call ambient `Resolve()`. Direct-runtime tests intentionally use settings as their instance override. | Normalize the daemon's effective request once and use the same runtime decision for capability, custody and host arguments. Preserve the explicit test-instance seam. |
| Inbox ceiling table can simply disappear. | `DelegationSettings.CeilingsFor` maps inbox to 900 bytes / 3,000 chars / 1,024 bytes, modern to 43,200 / 14,400 / 86,400. `SessionDeliveryProfile` always uses the conservative table for phone-home sessions and uncertain Herdr capability. Dispatcher, reply service and queue also request it directly. | C removes backend-specific selection, not the conservative delivery policy. Introduce a backend-neutral conservative profile and preserve all these numerical limits. |
| Only Claude needs spill once modern is universal. | `PtyDeliveryCeilings.ForAgentKind` sets `BriefInlineMaxBytes=0` for every non-Claude kind. Marker, pointer-size, durable spill and transcript-confirmation logic is independent of the Windows binary. | Keep spill-and-pointer, whole-body transcript receipts, per-kind narrowing and oversize tripwire. CARD-1023 does not authorize changing these delivery rules. |
| New server can stop recognising an old runner's string. | Capability backend is a string, not a wire enum. `PtyDeliveryProfile` conservatively handles any non-modern answer; null/unreachable currently retains the local decision. Many fixtures deliberately carry old four-field DTOs. | Keep old/unknown payloads readable and dispatchable; preserve conservative delivery for an explicitly reported legacy backend. Never reinterpret old inbox as modern. |
| Restarting the runner upgrades every host. | Detached PtyHosts are shadow-copied and re-adopted. Runner build/capabilities do not prove an existing session's host changed. | Inventory live Windows hosts before raising/confirming modern ceilings. Wait for legacy/unknown hosts to retire; restarting alone is insufficient. |
| About 80 other files mention the backend. | The exact card expression matches **167 files** in this base checkout: 10 server, 12 src, 95 tests, 46 docs, 1 AppHost, 2 Compose, 1 client fixture. Appendix A names and classifies all 167. Supplemental matches expose additional native/helper dependencies. | This is not 167 inbox implementations. Distinguish selection, transport, conservative policy, fixtures and history before editing. |
| ADR 0002 describes current desired policy. | Its status, sections 2/3 and preserved gotchas still say default-off, fallback and paired backends are the contract. | Update the living ADR in A with a dated supersession and migration timeline; retain historical measurements as history. |

## Decisions

**D-1 — Three separately reviewed and activated releases.** CARD-1022's first Code
scope is release A (S1-S3 below): deprecation plus default flip. Follow-up B stops
selection and refuses unusable modern; follow-up C removes implementation/tests.
Each follow-up gets a card and its own frozen TestDesign/checkpoints before dispatch.
This provides an observable migration window without an arbitrary date-based cutoff.
Reject a single deletion release: it would couple Windows launch refusal, Linux
transport survival, ceiling semantics and concurrent capability work in one change.

**D-2 — Explicit transition semantics.** On Windows in A, empty/unset and unknown
values choose modern; unknown values additionally warn that the selector was not
recognised. Existing modern aliases (`modern`, `conpty`, `1`, `on`, `true`, `yes`)
remain accepted. The finite legacy aliases (`inbox`, `0`, `off`, `false`, `no`)
choose inbox with a deprecation warning. A missing pair still resolves inbox with
`FellBack=true` and a warning. On Windows in B/C, legacy aliases refuse with
`pty_backend_removed`; other unrecognised nonempty values refuse with
`pty_backend_invalid`; unset/modern aliases request modern. A missing, incomplete
or unloadable pair refuses with `modern_conpty_unavailable`. Preserve raw request
and a useful reason. Reject silently turning an explicit removed selector into
modern: it would conceal unfinished migration. No provider/version floor is added.

**D-3 — Platform split precedes Windows policy.** Add `PtyBackend.UnixPty` in A,
preserving existing enum numeric values. Non-Windows resolution bypasses ConPTY
discovery and Windows selectors, reports `UnixPty`, never `FellBack`, and continues
using Porta. Existing Linux `inbox`/`modern` environment settings are ignored as
Windows-only settings during migration; C removes those settings from Linux Compose.
This narrow naming/branch separation is necessary to remove the Windows enum arm;
it does not qualify another OS, enlarge Linux ceilings or implement CARD-0038.

**D-4 — One effective daemon request.** Keep environment-over-config precedence at
the daemon composition root. Feed its effective request into runtime options once;
capabilities (local and phone-home), custody and `--pty-backend` use that instance's
decision, rather than independently reading ambient config. A directly constructed
runtime may still supply an explicit per-instance setting; a direct `PtyAgentRunner`
override still outranks its environment. Avoid a new global mutable selector.
Reject leaving the current precedence mismatch: a capability describing another
backend can cause oversized delivery. Preserve explicit host arguments until C,
and keep modern arguments accepted thereafter for rolling compatibility.

**D-5 — Additive deprecation observation, no new eligibility floor.** Append optional
`bool? PtyBackendDeprecated = null` to `RunnerCapabilitiesDto`, after CARD-0959's
landed fields. A resolved Windows inbox decision sets true; modern and UnixPty set
false. Old payloads leave it null. Keep `PtyBackend`, `Requested`, `Reason` and
`FellBack` wire fields through all releases (B/C write false for fallback). Log
deprecation once at runner startup and per newly launched host; do not warn on
every capability poll. No DB migration and no client UI required. CARD-1023 may
record these backend observations as a matrix dimension; unknown/old/stale
capabilities remain allowed. Missing native infrastructure is a concrete launch
failure, not an inferred incompatibility cell.

**D-6 — B's failure boundary is Windows runner startup plus every Windows spawn.**
Before the runner serves HTTP or starts phone-home, validate the selected shipped
pair, load required exports and execute a bounded, owned native pseudoconsole
preflight (no provider CLI/model turn). Check the created console host is the
expected sibling OpenConsole, not a system conhost, and clean up the probe. Pair
hashes remain pinned to the shipped package; a corrupt/unapproved pair is a
readiness failure. Share the probe primitives with the real modern path rather
than constructing a second loader with different rules. Startup failure logs the
stable reason, package/architecture and probe location, exits nonzero, and never
advertises an eligible runner. TestDesign must bind native assertions to these
checks, including the bounded teardown. Reject keeping an HTTP-healthy runner that
advertises a backend it cannot run; that would require new fleet readiness policy
and could repeatedly consume tasks.

At each actual spawn, including direct runners and a detached host's shadow-copy
directory, revalidate the pair and propagate a typed failure before provider input.
The runtime must carry the host's specific refusal through existing launch-error
plumbing rather than replacing it with a pipe timeout. A removal/corruption after
startup fails the affected launch, invalidates further launch readiness, and never
falls back. Existing healthy sessions are neither killed nor retyped. The server's
delivery-profile constructor must not load native binaries or exit merely because
its remote runner is unavailable: B separates delivery-policy selection from the
launch/readiness resolver. Keep existing HTTP Problem Details/error conventions.

**D-7 — Remove inbox delivery identity, retain conservative delivery.** In C add
`DeliveryBackend.Conservative` and `DelegationSettings.ConservativeCeilings(reason)`
with the existing 900/3,000/1,024 bounds; keep configuration key compatibility.
Replace explicit inbox calls for phone-home, unknown Herdr and absent local profile
with this named policy. Remove `PtyBackend.InboxConhost`, the Windows selection and
the inbox-specific ceiling mapping/downgrade. Old capability string `InboxConhost`
is still accepted as evidence for the conservative policy, alongside other
non-modern reported strings. Preserve existing null/unreachable-probe behavior
unless separately commissioned. Keep modern and Herdr numbers, `IsPastePath`
semantics, single writes, per-kind zero inline, pointer bounds and durable spill.
Do not claim that renaming conservative limits measures Unix or permits larger
remote messages. Reject both deleting spill and treating every surviving backend
as having the modern envelope.

**D-8 — No blanket removal of third-party/native infrastructure.** Keep Porta.Pty,
`PtyOptions`, `IPtySession`, Unix `PortaPtySession`, Unix argv handling and Linux
custody. Remove only Windows Porta spawning and its special verbatim argv workaround
after B makes them unreachable. Preserve modern job/tree termination, DA1 response,
environment isolation, argument fidelity and shadow-copy staging.

**D-9 — Test the retained behavior, retire obsolete platform assertions.** No new
live inbox canary or required inbox PC. A may retain pure transition-policy tests
for the deprecated choice/fallback; these do not spawn inbox. B migrates shared
native fixtures to modern before refusing inbox. Any retained inbox-native test
must become a no-spawn removed-selector assertion in B; no permanently red tests
or unconditional skips are an acceptable transition. C deletes redundant legacy
test scaffolding while keeping a focused removed-selector regression. Typed-input clip-model tests that
still guard real behavior send deliberately unwrapped input through modern or test
the model directly; do not mechanically change a backend string and retain the old
marker-stripping expectation. C deletes inbox-native tests and the paired-backends
claim. Keep one strong production marker-preservation regression plus complete
body/pointer and separate-Enter regression coverage. Retain legacy DTO fixtures as
compatibility tests; they are not executable inbox support.

**D-10 — Evidence and verdicts.** Separate ordinary Review uses the operator's
regression-only verdict standard. Inherited red is confirmed by the failing filter
on the base and reported separately; neither broader compatibility guesses nor
unknown matrix cells justify refusal. Missing Windows evidence is recorded as
unverified, never called green from a Linux skip. Every release also receives a
separate Windows Debug at its final frozen code SHA; edits require refreshing the
affected evidence. Live provider qualification uses modern only and records the
observed CLI version, model, runner SHA, host binary/path and backend.

## Slices and follow-up cards

### First Code card: CARD-1022, release A only

| Slice | Files | Work and exit condition | Tests to add/change or retain |
|---|---|---|---|
| S1: platform-aware default and decision | `src/Antiphon.Agents.Pty/PtyBackend.cs`, `PtyAgentRunner.cs`, `PtySession.cs`, `ConPtyRedistributable.cs`; `server/Application/Settings/DelegationSettings.cs` only if explicit Unix conservative mapping is needed | Implement D-2 A semantics and UnixPty; preserve numeric enum identities. Expose deprecation on the decision independently of fallback. Modern spawn remains the retained production path; Unix still uses Porta. | New `tests/Antiphon.Agents.Pty.Tests/PtyBackendPolicyTests.cs` for deterministic policy; update `PtyBackendContractTests.cs` default test and comments, retain its package/host/marker tests and A's missing-pair transition test. `ConPtyEnvironmentIsolationGuardTests.cs` follows any renamed mutator. |
| S2: coherent runtime diagnostics | `src/Antiphon.SessionRunner/Program.cs`, `SessionRunnerRuntime.cs`, `PhoneHomeRuntimeAdapter.cs`, `SessionRunnerSettings.cs`; `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`; `src/Antiphon.PtyHost/HostSession.cs`, `PtyHostOptions.cs`; `server/Application/Services/PtyDeliveryProfile.cs` logging | Apply D-4 and append the nullable observation in D-5. Warn on resolved inbox and explain fallback. Keep wire construction compatible with CARD-0959 and old readers. No routing gate. | New `tests/Antiphon.SessionRunner.Tests/C1022BackendCapabilitiesTests.cs`, `C1022BackendLaunchTests.cs`; extend `RunnerCapabilitiesTests.cs`; retain the current seam tests until B replaces their inbox-only arms. |
| S3: deterministic fixtures and living documentation | Five guard files in Appendix A; `tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs` comments/settings as needed; `tests/Antiphon.Agents.Pty.Tests/FakeVsRealClipParityTests.cs` implicit typed-input arm and `NodeStdinProbe.cs` caller audit; `docs/adr/0002-modern-conpty-backend.md`, `docs/testing-and-build.md`, `docs/antiphon-api.md`, `docs/session-runtime-invariants.md`, `docs/herdr-sessions.md`; `Antiphon.AppHost/Program.cs` comments | Keep guards clearing ambient env; assert Windows modern with the shipped pair, non-Windows UnixPty. Missing Windows payload is a test failure in the declared lane. Correct current-policy prose with a dated A/B/C transition; historical receipts stay intact. Make implicit-default typed-input oracles explicit; TestDesign must bind any behavioral test-helper change to its own focused modern proof before freezing the table. No Compose/deploy-script change. | Guard tests in all five projects; native modern marker/DA1, owned-host default launch, shadow-copy pair retention and Unix argv coverage below. Do not rewrite unrelated legacy-wire fixtures. |

Commit and push each coherent slice; freeze S1-S3 before the first grouped checkpoint
run. Never edit while a run is in flight. If S1/S2 would leave uncompilable or
internally contradictory source, commit them as one meaningful group, then S3.

### Follow-up B: stop selecting inbox and refuse an unusable modern host

Caller creates a separate implementation card after A's activation observations,
with this plan as parent context. This dispatch does not create/spawn follow-ups.

* Files: policy/redistributable/modern loader and a proposed
  `src/Antiphon.Agents.Pty/ModernConPtyReadiness.cs`; runner Program/runtime,
  phone-home capability builder and custody ledger; PtyHost Program/HostSession and
  launcher failure propagation; server `PtyDeliveryProfile` (separate delivery from
  native resolution). Read session/error owners before implementing the refusal.
* Tests: new `ModernConPtyReadinessTests`, `ModernConPtyLaunchRefusalTests` and owned
  runner-startup acceptance; update `PtyBackendSeamTests`, `HostCustodyTests`,
  `RunnerCustodyLedgerBackendTests`, `PtyCustodyTests`. Missing DLL, missing host,
  both present but invalid DLL, missing export, invalid/blocked host, unsupported
  Windows architecture, stale shadow copy and deletion after readiness must each
  assert the right refusal/no provider input/no orphan. Discovery fakes alone
  cannot prove a load or spawn refusal.
* Migrate active inbox fixture helpers in Appendix A/B to modern and preserve their
  actual receipt, input, argv, environment and kill assertions. Stop scheduling
  inbox-native qualification. Old DTO consumers remain allowed; Linux still
  launches with the inherited old Compose selectors during the transition.
* Exit: no Windows startup/direct/host launch selects inbox. Pair failure is
  explicit, and runner startup cannot advertise readiness after that failure.
  Windows Debug and runner-first activation at B's final SHA are required.

### Follow-up C: remove unreachable Windows implementation and obsolete tests

Caller commissions after B is active and its native-failure evidence is complete.

* Remove the enum arm and fallback/dead branches in `PtyBackend.cs` and
  `PtyAgentRunner.cs`; retain finite rejected legacy-selector parsing as an
  actionable migration error, not an accepted backend. Keep accepted modern
  config/env/CLI spelling for old deployment tools. Retain Unix Porta/package.
* Decouple conservative delivery in `DeliveryBackend.cs`, `PtyDeliveryCeilings.cs`,
  `DelegationSettings.cs`, `PtyDeliveryProfile.cs`, `SessionDeliveryProfile.cs`,
  `AgentTaskDispatcher.cs`, `AgentSessionService.cs`, `AgentTaskReplyService.cs` and
  `SessionMessageQueueService.cs`; migrate corresponding ceiling, remote-spill,
  reply/distillation and join-safe tests. No removal of `.antiphon/inbox` storage.
* Remove obsolete inbox native arms in backend, argv, marker, kill, clip-parity,
  custody, fake-provider, command-length and CARD-1011 tests. Convert surviving
  shared helpers before deletion; update reflection guards and roster maintenance
  through the existing roster workflow. Retain captured JSON/Markdown provenance.
* Remove the two inbox environment/config entries from each Linux Compose file and
  update `DockerStackContractTests`. Keep deploy scripts untouched. Reconcile
  ADR/current docs; leave historical investigations and closed plans unchanged.
* Exit: no executable Windows inbox route, no first-party kernel32 pseudoconsole
  bench retained as an alternate inbox backend, no test requiring a live inbox
  host. Remaining `InboxConhost` strings are documented legacy-wire/history only.
  Unix argv/custody, modern marker/kill/DA1, old-wire acceptance, conservative
  remote/unknown limits and spill receipts pass at C's final SHA.

## Collision and landing order

* **CARD-1011 Code `b657a1e2`: hard source-order dependency.** The brief supersedes
  the older task reference in the card. `C1011_windows_backends`/WQ changes are not
  present in this base. Wait until CARD-1011 lands, then create the Code checkout
  from that target and re-freeze this test inventory. Keep its modern canary,
  Windows trust-prompt handling and WQ proof; remove its inbox parameter only in
  the agreed migration slice. WQ-1 inbox canary is waived by the 2026-10-03 operator
  decision, not reintroduced as a dependency. Do not cherry-pick its in-flight tests
  or run a second editor over `FakeGrokContractTests`/its WQ files.
* **CARD-0959 re-freeze:** serialize shared DTO/runtime/capability edits after its
  inert observation slice lands. Append D-5 to the landed shape; preserve CLI probe
  fields and launch fingerprint. Re-freeze old/new JSON expectations together.
  Its removed freshness/version-floor admission proposals are not dependencies.
  CARD-1023 owns compatibility admission; this card adds no equivalent gate.
* **CARD-1008 Review:** no edits to `scripts/deploy-server2.ps1`,
  `scripts/c590-remote.sh` or its fake-Docker tests. Use its landed rolling/retirement
  implementation at activation. C's Compose and Docker contract changes must be
  sequenced with the landed deploy work; do not infer that Review means landed.
* A docs-only plan may land before these implementations. TestDesign can proceed
  now, but must reconcile the dependency tips before freezing Code selections.
  Do not rebase/amend this delegated branch; landing owns rebasing.

## Verification handoff to TestDesign

This is a separate TestDesign dispatch. It must add `## Verification design`, bind
each V/R promise to methods/assertion witnesses and method-scoped PCs, and freeze
the checkpoint roster after CARD-1011/CARD-0959 integration. The IDs below identify
the required coverage, not results from this Plan dispatch.

| ID | Required outcome and source seam | Proposed test home |
|---|---|---|
| V-1 | A policy: empty/whitespace/default and unknown go modern on Windows; modern aliases agree; explicit legacy aliases are deprecated; missing/half pair is deprecated fallback; Unix bypasses locator and reports UnixPty. Use a small deterministic platform/locator seam without process-global test state. Eight single-result policy methods may loop alias cases. | New `PtyBackendPolicyTests.cs` (8 methods). |
| V-2 | Local and phone-home capability projections agree with the runtime's effective request; environment/config/instance precedence is real; deprecation true/false/null survives serialization; original four-field DTO and unknown backend remain readable. Proposed five new single-result methods plus existing `RunnerCapabilitiesTests`. | New `C1022BackendCapabilitiesTests.cs`; `RunnerCapabilitiesTests.cs`. |
| V-3 | The real owned detached host launched unset uses modern; explicitly modern config works; ambient/setting precedence yields the same host log and capability decision. Three new single-result Windows launch tests; no inbox native spawn. | New `C1022BackendLaunchTests.cs`; current `PtyBackendSeamTests` remains the seam reference. |
| R-1 | Shipped package hashes, actual OpenConsole image and intact start/end markers with complete body still pass. DA1 is answered exactly once without startup stall or leaked reply. A's incomplete-pair decision test never spawns inbox. | Existing `PtyBackendContractTests.cs` (4 retained native/locator methods after moving default policy); `ModernPtyDa1Tests.cs` (4). |
| R-2 | All five test hosts still clear inherited backend state, with platform-correct default. No suite silently swaps its backend according to the launching shell. | Five `PtyBackendEnvGuardTests` (1 result per assembly). |
| R-3 | Shadow-copy closure retains both shipped binaries; Unix argv remains exact through Porta/containment and does not attempt ConPTY. | `ShadowCopyStoreTests.Shipped_conpty_binaries_survive_the_deps_json_closure_filter`; `UnixPtyArgvTests.Native_argv_is_verbatim` (9 existing argument results). |
| R-4 | Default flip retains old-runner conservative downgrade and phone-home/uncertain-Herdr ceilings, pointer/spill/per-kind behavior; tests use explicit profile inputs, so the default does not rewrite their meaning. | Existing `PtyDeliveryCeilingsTests`, `SessionDeliveryProfileTests`, `GrokDeliveryShapeTests`, `TypedBodySpillTests`. |

TestDesign must also specify B/C's distinct negative and deletion proofs on their
own cards; their execution is outside A's ordinary scope. Keep no-skip Windows
payload/Node prerequisites explicit. A missing dependency is not a passing test.
No browser or paid-model suite is needed for A's ordinary checkpoints. Native
Windows evidence cannot be substituted by the Unix lane or synthetic capabilities.

### Planner checkpoint proposal (superseded by the verification freeze)

Proposed closed list for **release A S1-S3 only**, to be frozen by TestDesign.
Every row has its own isolated build and one exact filter. Group names carry the
execution lane because the checkpoint importer has no `Lane` column. `portable`
uses effective default placement; `windows` requires Windows; `linux` requires
Linux. All outputs use a forward slash. Floors count executed results, not
assertions; the V-1/V-2/V-3 counts are proposed test authoring obligations.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-policy/` | portable-policy | `/*/*/PtyBackendPolicyTests/*` | V-1 | all 8 proposed methods, 0 failed/skipped | 8 | 4 | true |
| CP-2 | S1-S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1022-capabilities/` | portable-capabilities | `/*/*/(C1022BackendCapabilitiesTests*)\|(RunnerCapabilitiesTests*)/*` | V-2 | all proposed 5 plus existing 7 methods; reconcile CARD-0959 additions, 0 failed/skipped | 12 | 5 | true |
| CP-3 | S1-S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-windows-native/` | windows-modern | `/*/*/(PtyBackendContractTests*)\|(ModernPtyDa1Tests*)\|(PtyBackendEnvGuardTests*)/*` | R-1, R-2 | 4 retained contract + 4 DA1 + 1 guard, 0 failed/skipped | 9 | 6 | true |
| CP-4 | S1-S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1022-windows-launch/` | windows-owned-hosts | `/*/*/(C1022BackendLaunchTests*)\|(PtyBackendEnvGuardTests*)/*` | V-3, R-2 | all 3 proposed launches + 1 guard, 0 failed/skipped | 4 | 6 | true |
| CP-5 | S1-S3 | `tests/Antiphon.PtyHost.Tests -> bin-c1022-windows-pair/` | windows-shadow-pair | `/*/*/(ShadowCopyStoreTests*)\|(PtyBackendEnvGuardTests*)/(Shipped_conpty_binaries_survive_the_deps_json_closure_filter*)\|(The_suite_ignores_an_inherited_pty_backend*)` | R-2, R-3 | both named methods, 0 failed/skipped | 2 | 4 | true |
| CP-6 | S1-S3 | `tests/Antiphon.Tests -> bin-c1022-windows-delivery/` | windows-delivery | `/*/Antiphon.Tests.Application/(PtyDeliveryCeilingsTests*)\|(SessionDeliveryProfileTests*)\|(GrokDeliveryShapeTests*)\|(TypedBodySpillTests*)/*` | R-4 | 12 + 7 + 14 + 9 existing results, 0 failed/skipped | 42 | 8 | true |
| CP-7 | S1-S3 | `tests/Antiphon.Tests -> bin-c1022-server-guard/` | windows-server-guard | `/*/*/PtyBackendEnvGuardTests/*` | R-2 | 1 guard with shipped pair, 0 failed/skipped | 1 | 5 | true |
| CP-8 | S1-S3 | `tests/Antiphon.E2E -> bin-c1022-e2e-guard/` | windows-e2e-guard | `/*/*/PtyBackendEnvGuardTests/*` | R-2 | 1 guard with shipped pair, no browser launched, 0 failed/skipped | 1 | 5 | true |
| CP-9 | S1-S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-linux-argv/` | linux-unix-transport | `/*/*/UnixPtyArgvTests/Native_argv_is_verbatim` | R-3 | all 9 existing argument cases, 0 failed/skipped | 9 | 4 | true |

TestDesign confirms every census against the landed source; proposed method counts
are not an excuse for Code to accept a partial run. Native launch and assembly-limiter tests may be added only with a
named invariant/cost when S2's exact implementation is fixed. Class/method OR
selection must produce every intended executed method; zero tests is not evidence.

Run via the checkpoint tool (`run --plan <this-plan> --rows <lane-rows>
--expected-source-sha <full-code-sha>`), using its leased bootstrap as documented
in `docs/testing-and-build.md`. Use `wait` until completion (exit 75 is still
running), and keep the task alive to own every process. Every other build/test
driver takes `scripts/build-slot.ps1`; slot timeout means not run, never `-NoSlot`.
TUnit uses `dotnet run`, not `dotnet test`. Keep process-spawn limiters and serialize
Pty/FakeClaude, PtyHost and Antiphon.Tests runs. Keep fresh ignored receipts, full
CHECKPOINT lines and counts/source identity in the task report; validate receipts
against the code SHA. Code/Review run `scripts/check-evidence-diff.ps1` over the
full task range. Remove only manifest-owned alternate outputs after completion.

### Planner cost estimate (superseded by the verification freeze)

Provisional ordinary A checkpoint floor: 47 minutes across the named lanes, plus
authoring and the separate final Windows Debug. No full assembly or repeated green
run is requested. TestDesign adjusts costs only for the actual frozen roster.

## Windows Debug and activation

The live placement reads on 2026-10-03 returned runner-defaults revision 2 with no
kind override; the catalogue exposed an eligible Windows lane and an eligible
Linux lane, plus an unavailable drained temporary lane. These are observations,
not baked-in fleet routing. Re-read `GET /api/runner-defaults` and
`GET /api/session-runners` at every dispatch. Ordinary work omits `-Runner` and
`-Platform`; native Windows work uses `-Platform Windows`, Unix native work uses
`-Platform Linux`. Use `-Platform Any` to remove an inherited pin. Pin `-Runner`
only for the named deployment canary. No host address or filesystem location is
part of this plan's placement policy.

1. **Freeze and qualify each release.** Finish Code, ordinary regression Review
   and a distinct Windows Debug task on the exact final code SHA, after all test
   and documentation amendments. Debug records Windows/process architecture,
   package hashes, actual host image, unset/config/env/host-argument behavior and
   the relevant CP evidence. For B/C it also exercises the missing/half/corrupt
   native refusal cases in an isolated test runner, never by removing live files.
   No live inbox canary. If landing changes source, qualify the resulting SHA
   before activation; do not relabel earlier receipts.
2. **Observe before restart.** From the canonical checkout, wait for queued lands,
   verify running server version/health, then update that checkout to the intended
   landed SHA. Inspect restart/launch locks. Inventory detached Windows host
   manifests, logs, loaded console binaries and pending deliveries: a new runner
   capability does not prove old hosts are modern. If any Windows inbox/unknown
   host remains, wait for its natural retirement or commission explicit operator
   handling; no kill-all. Do this before advertising modern on a replacement
   runner, not merely before the later server restart.
3. **Desktop runner first.** Run `pwsh -NoProfile -File
   scripts/restart-session-runner.ps1` from the canonical checkout, using the
   [runner restart owner](../../apphost-runbook.md#restart-only-the-runner).
   Never `-AllowWorktree`, `dotnet run`, or `-KillSessions`. Check runner capability
   build SHA, resolved backend/deprecation, health and adopted-session journal.
   A freshly created owned host must log/load modern and preserve a bracketed body
   plus its separate Enter. A no-answer/unhealthy restart is not an activation.
4. **Server2 runner rollout before server behavior.** Use the current landed
   [rolling phases](../../docker-stack.md#staged-server2-rolling-rollout-card-0934):
   `deploy-temp`, smoke and sanctioned pinned canary, `drain-old`, zero-work gate,
   `redeploy-old`, smoke/canary, `drain-temp`, `retire-temp`. These deployment runner
   names identify operational targets, not default task placement. Verify exact
   buildVersion, eligibility/accepting state and UnixPty observation at each gate;
   no Windows native probe may execute on Linux. Preserve old accepting service
   until temp qualifies. CARD-1008 owns recycling/retirement; use its exact stop
   gates and receipts, never improvise a broad volume removal. Do not deploy or
   restart anything in this Plan dispatch.
5. **Server last.** Only after the serving runners and retained Windows sessions
   satisfy the release's prerequisites, activate the server from the canonical
   checkout using the [AppHost owner](../../apphost-runbook.md). Confirm
   `/api/version` SHA/`land-v2` and health, then capability-driven ceilings, a
   conservative phone-home spill and a transcript-confirmed modern delivery.
   Keep accepting older payloads throughout the overlap. Observation records must
   distinguish runner and shadow-host build identity; CARD-1023 owns the broader
   matrix schema, not this migration.
6. **Stop/rollback.** If modern readiness fails, keep or restore the previous
   reviewed runner build/config as a whole release and retain conservative server
   behavior; B/C do not offer `inbox` as an emergency selector. Do not advance
   server activation or retirement gates on missing native proof. Existing live
   sessions are not a cleanup target. A's temporary fallback is explicitly
   observable migration debt; B cannot proceed until observed Windows selectors
   are modern/unset and every serving output/shadow copy has the approved pair.

## Inventory method

At the source SHA above, ran `rg -l 'InboxConhost|PtyBackend|ANTIPHON_PTY_BACKEND'`
and inspected matching lines, then expanded to `inbox`, `PortaPtySession`,
`pty-backend` and native pseudoconsole paths. The exact scan found 167 files (none
under generated `docs/cards/`). The appendices classify every exact match and
additional relevant files. The plan itself will appear in future scans; exclude it
or scan the recorded source SHA when reproducing the count. Re-run after the
collision dependencies land. `.antiphon/inbox` and messaging Inbox tables are
unrelated and must not be renamed or deleted.
## Appendix A: all 167 exact-expression matches

Each path is relative to the repository root. A/B/C name releases above; retaining a
historical or wire-format string does not retain the Windows backend. These are source
inspection classifications, not generated test evidence or a required 167-file edit.

| File | Classification and disposition |
|---|---|
| `Antiphon.AppHost/Program.cs` | Explicit server modern environment; retain value, A update outdated default/fallback commentary. |
| `client/src/test/fixtures/antiphon-board-2026-08-13.json` | Historical board snapshot prose; no client backend implementation, retain. |
| `docker-compose.server2-runner.yml` | Linux runner explicitly sets inbox twice; retain through A/B, remove both settings in C; no deploy-script edits. |
| `docker-compose.yml` | Linux runner explicitly sets inbox twice; retain through A/B, remove both settings in C; no deploy-script edits. |
| `docs/adr/0002-modern-conpty-backend.md` | Living architecture owner: A supersedes default-off/fallback/pair contract with staged policy; keep measured history. |
| `docs/antiphon-api.md` | Living capability route contract: A document UnixPty and optional deprecation field; B refusal behavior. |
| `docs/herdr-sessions.md` | Separate session transport and limits, plus modern config example; C replace any inbox conservative naming, preserve Herdr. |
| `docs/investigations/2026-08-16-modern-conpty-da1-stall-CARD-0048.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/investigations/2026-08-27-card-0133-s0-boot-wedge-probe.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/investigations/2026-09-11-card-0478-host-runner-custody-checkpoint.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/investigations/2026-09-11-card-0478-native-custody-checkpoint.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/investigations/2026-09-19-card-0490-phone-home-runner.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/investigations/2026-09-22-card-0604-persistent-runner-dind-rework.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/investigations/2026-09-23-card-0610-github-actions-ci-red-on-master.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/investigations/2026-09-23-card-0628-claude-cli-on-server2-runner.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/investigations/2026-10-03-card-1006-linux-grok-signin-trust-capture.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-19-card-0024-truncation-detection-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-19-card-0026-jobobject-memory-limit-test-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-20-delegate-reliability-test-coverage-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-21-card-0102-e2e-runner-isolation-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-21-card-0110-test-timings.csv` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-21-card-0111-herdr-investigation-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-21-card-0112-stale-daemon-detection-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-21-card-0128-pty-flake-cast-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-22-card-0128-s2a-sendline-evidence-gate-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-23-card-0160-herdr-s2-launch-path-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-23-card-0161-herdr-s3-delivery-adapter-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-24-card-0162-herdr-s4-state-mirror-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-24-card-0164-herdr-unobservable-baseline-delivery-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-25-card-0200-transcript-adoption-safety-flake-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-08-31-card-0208-sessionrunner-refusal-clock-flake-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-01-card-0308-pty-kill-process-tree-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-04-card-0124-overnight-windmill-nightly-card-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-readiness-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-10-card-0478-runner-custody-amendment.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-12-card-0476-lazy-db-preflight-cache-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-12-card-0497-codex-command-length-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-20-card-0490-phone-home-runner-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-21-card-0590-linux-test-roster.json` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-21-card-0590-self-contained-docker-stack-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-22-card-0594-linux-pty-host-launch-deadlock-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-22-card-0599-release-gate-activation-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-25-card-0691-pool-release-and-ptyhost-leaks-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-09-27-card-0464-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md` | Concurrent capability plan: collision input only; its owner re-freezes observation slice, do not edit from CARD-1022. |
| `docs/superpowers/plans/2026-10-03-card-1006-grok-linux-signin-trust-plan.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/specs/2026-08-13-card-0019-amendment-1.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/specs/2026-08-15-card-0045-pty-backend-test-equivalence.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/superpowers/specs/2026-08-16-card-0048-da1-answer.md` | Historical plan/spec/investigation or recorded census: preserve provenance; superseded by this plan, not runtime code. |
| `docs/testing-and-build.md` | Living test owner: A platform-default guard/native lane guidance; retain eager guard and checkpoint rules. |
| `server/Application/Services/AgentSessionService.cs` | Conservative fallback/remote ceiling consumers and spill; C change policy name, preserve delivery/receipt behavior. |
| `server/Application/Services/AgentTaskDispatcher.cs` | Conservative fallback/remote ceiling consumers and spill; C change policy name, preserve delivery/receipt behavior. |
| `server/Application/Services/AgentTaskReplyService.cs` | Conservative fallback/remote ceiling consumers and spill; C change policy name, preserve delivery/receipt behavior. |
| `server/Application/Services/PtyDeliveryProfile.cs` | Local/runner corroboration and downgrade; A warning; B separate native readiness; C generic conservative mapping. |
| `server/Application/Services/SessionDeliveryProfile.cs` | Remote and uncertain-Herdr conservative limits are not Windows spawn logic; retain numbers under C generic policy. |
| `server/Application/Services/SessionMessageQueueService.cs` | Conservative fallback/remote ceiling consumers and spill; C change policy name, preserve delivery/receipt behavior. |
| `server/Application/Settings/DelegationSettings.cs` | Two backend-specific ceiling sets and independent join-safe narrowing; C decouple conservative policy, preserve settings/limits. |
| `server/Application/Settings/DeliveryBackend.cs` | Two backend-specific ceiling sets and independent join-safe narrowing; C decouple conservative policy, preserve settings/limits. |
| `server/Application/Settings/PtyDeliveryCeilings.cs` | Two backend-specific ceiling sets and independent join-safe narrowing; C decouple conservative policy, preserve settings/limits. |
| `server/Program.cs` | Server exports backend config into environment; A document new default, B do not couple remote delivery policy to native load. |
| `src/Antiphon.Agents.Pty/PtyAgentRunner.cs` | Spawn: preserve Unix Porta and modern custody; remove Windows Porta/argv arm in C. |
| `src/Antiphon.Agents.Pty/PtyBackend.cs` | Selection: A default/deprecation/Unix identity; B refusal; C remove Windows legacy enum/fallback. |
| `src/Antiphon.PtyHost.Client/ShadowCopyStore.cs` | Staging allowlist preserves conpty.dll/OpenConsole.exe; retain, exercise pair retention and B revalidation. |
| `src/Antiphon.PtyHost/HostSession.cs` | Host request propagation, decision logging and custody; A diagnostics, B explicit launch refusal. |
| `src/Antiphon.PtyHost/PtyHostOptions.cs` | Host request propagation, decision logging and custody; A diagnostics, B explicit launch refusal. |
| `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs` | Capability wire contract: append nullable observation in A after CARD-0959; retain old string fields. |
| `src/Antiphon.SessionRunner/appsettings.json` | Canonical explicit modern selection; keep through rollout for compatibility, no inbox configuration here. |
| `src/Antiphon.SessionRunner/PhoneHomeRuntimeAdapter.cs` | Runtime/capability selection seam: A use one effective request; B refuse unavailable Windows modern. |
| `src/Antiphon.SessionRunner/Program.cs` | Daemon environment/config precedence and startup logs; A normalize effective request, B preflight before serving/phone-home. |
| `src/Antiphon.SessionRunner/RunnerCustodyLedger.cs` | Windows-modern custody eligibility; retain Linux cgroup branch and fail explicitly in B. |
| `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs` | Runtime/capability selection seam: A use one effective request; B refuse unavailable Windows modern. |
| `src/Antiphon.SessionRunner/SessionRunnerSettings.cs` | Runtime/capability selection seam: A use one effective request; B refuse unavailable Windows modern. |
| `tests/Antiphon.Agents.Pty.Tests/ClaudePasteLossCanaryTests.cs` | Inbox marker-loss/bench/canary expectations; no new inbox runs; C retire inbox arms, retain shipped modern marker regression. |
| `tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitConfirmCanaryTests.cs` | Explicit modern evidence/canary/DA1; retain, update obsolete fallback commentary only. |
| `tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractTests.cs` | Executable inbox pins/shared helpers; B migrate retained assertions to modern, C delete inbox-only arms; FakeGrok collision with CARD-1011. |
| `tests/Antiphon.Agents.Pty.Tests/CodexCanaryTests.cs` | Explicit modern evidence/canary/DA1; retain, update obsolete fallback commentary only. |
| `tests/Antiphon.Agents.Pty.Tests/CodexComposerCanaryTests.cs` | Explicit modern evidence/canary/DA1; retain, update obsolete fallback commentary only. |
| `tests/Antiphon.Agents.Pty.Tests/CodexDoneDetectionCanaryTests.cs` | Explicit modern evidence/canary/DA1; retain, update obsolete fallback commentary only. |
| `tests/Antiphon.Agents.Pty.Tests/CodexMcpBootProbeTests.cs` | Explicit modern evidence/canary/DA1; retain, update obsolete fallback commentary only. |
| `tests/Antiphon.Agents.Pty.Tests/ConPtyEnvironmentIsolationGuardTests.cs` | Reflects the exact missing-pair env-mutator method; update with rename, preserve global isolation. |
| `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs` | Executable inbox pins/shared helpers; B migrate retained assertions to modern, C delete inbox-only arms; FakeGrok collision with CARD-1011. |
| `tests/Antiphon.Agents.Pty.Tests/FakeGrokContractTests.cs` | Executable inbox pins/shared helpers; B migrate retained assertions to modern, C delete inbox-only arms; FakeGrok collision with CARD-1011. |
| `tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs` | Explicit modern evidence/canary/DA1; retain, update obsolete fallback commentary only. |
| `tests/Antiphon.Agents.Pty.Tests/ModernPtyDa1Tests.cs` | Explicit modern evidence/canary/DA1; retain, update obsolete fallback commentary only. |
| `tests/Antiphon.Agents.Pty.Tests/ProcessSpawnLimitTests.cs` | Reflection inventory of spawning test classes (two projects); register any new spawner, retain limiter. |
| `tests/Antiphon.Agents.Pty.Tests/PtyAgentRunnerTests.cs` | Executable inbox pins/shared helpers; B migrate retained assertions to modern, C delete inbox-only arms; FakeGrok collision with CARD-1011. |
| `tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs` | A split pure default tests from native modern/hash/marker contracts; B replace fallback assertion with refusal. |
| `tests/Antiphon.Agents.Pty.Tests/PtyBackendEnvGuard.cs` | A: keep assembly env isolation; update inbox-default assertion to platform-correct default (five copies). |
| `tests/Antiphon.Agents.Pty.Tests/PtyBracketedPasteContractTests.cs` | Inbox marker-loss/bench/canary expectations; no new inbox runs; C retire inbox arms, retain shipped modern marker regression. |
| `tests/Antiphon.Agents.Pty.Tests/PtyKillProcessTreeTests.cs` | Executable inbox pins/shared helpers; B migrate retained assertions to modern, C delete inbox-only arms; FakeGrok collision with CARD-1011. |
| `tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs` | Inbox marker-loss/bench/canary expectations; no new inbox runs; C retire inbox arms, retain shipped modern marker regression. |
| `tests/Antiphon.Agents.Pty.Tests/WindowsPtyArgvNativeTests.cs` | Paired backend arguments; B/C retain modern native argv exactness and remove inbox parameter. |
| `tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs` | Fixture exposes instance/child backend override; A audit default, retain isolation and explicit modern support. |
| `tests/Antiphon.E2E/Fixtures/PtyBackendEnvGuard.cs` | A: keep assembly env isolation; update inbox-default assertion to platform-correct default (five copies). |
| `tests/Antiphon.PtyHost.Tests/HostCustodyTests.cs` | Modern tracked host plus explicit inbox negative; B/C replace removed-backend negative with refusal, keep containment proof. |
| `tests/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests.cs` | Runtime settings pin inbox in native/platform fixture; B use OS-correct default, retain Unix/ownership assertions. |
| `tests/Antiphon.PtyHost.Tests/PtyBackendEnvGuard.cs` | A: keep assembly env isolation; update inbox-default assertion to platform-correct default (five copies). |
| `tests/Antiphon.SessionRunner.Tests/CodexCommandLengthHttpAcceptanceTests.cs` | Explicit modern runtime/HTTP/custody fixture; retain, no inbox implementation to delete. |
| `tests/Antiphon.SessionRunner.Tests/GrokRulesAdoptionTests.cs` | Explicit modern runtime/HTTP/custody fixture; retain, no inbox implementation to delete. |
| `tests/Antiphon.SessionRunner.Tests/GrokRulesHttpAcceptanceTests.cs` | Explicit modern runtime/HTTP/custody fixture; retain, no inbox implementation to delete. |
| `tests/Antiphon.SessionRunner.Tests/LocalHttpRunner.cs` | Explicit modern runtime/HTTP/custody fixture; retain, no inbox implementation to delete. |
| `tests/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.SessionRunner.Tests/PhoneHomeConnectionServiceTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimitTests.cs` | Reflection inventory of spawning test classes (two projects); register any new spawner, retain limiter. |
| `tests/Antiphon.SessionRunner.Tests/PtyBackendEnvGuard.cs` | A: keep assembly env isolation; update inbox-default assertion to platform-correct default (five copies). |
| `tests/Antiphon.SessionRunner.Tests/PtyBackendSeamTests.cs` | Declared/ambient precedence with real detached hosts; A add modern default coverage, B replace inbox-native arms. |
| `tests/Antiphon.SessionRunner.Tests/PtyHostAdoptionTests.cs` | Backend argument travels through runtime; preserve modern restart/adoption and cleanup checks, audit optional default in A. |
| `tests/Antiphon.SessionRunner.Tests/RunnerCapabilitiesTests.cs` | Capability/platform projection and old JSON compatibility; A extend additive fields and truthful Unix observation. |
| `tests/Antiphon.SessionRunner.Tests/RunnerChildClaudeTokenInheritanceTests.cs` | Runtime settings pin inbox in native/platform fixture; B use OS-correct default, retain Unix/ownership assertions. |
| `tests/Antiphon.SessionRunner.Tests/RunnerCustodyTests.cs` | Explicit modern runtime/HTTP/custody fixture; retain, no inbox implementation to delete. |
| `tests/Antiphon.SessionRunner.Tests/RunnerMultilinePromptDeliveryTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.SessionRunner.Tests/RunnerPlatformContractTests.cs` | Capability/platform projection and old JSON compatibility; A extend additive fields and truthful Unix observation. |
| `tests/Antiphon.SessionRunner.Tests/RunnerPlatformLaunchTests.cs` | Runtime settings pin inbox in native/platform fixture; B use OS-correct default, retain Unix/ownership assertions. |
| `tests/Antiphon.Tests/Agents/ClaudeAdapterEffortPromptTests.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/ClaudeEffortPromptCanaryTests.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/ClaudeHerdrRealCliStubProxyCanaryTests.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/ClaudeRealCliStubProxyCanaryTests.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/CodexBootWedgeProbeTests.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/CodexHerdrRealCliStubProxyCanaryTests.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/CodexRealCliStubProxyCanaryTests.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/CompactionContinuationWireTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Agents/Fixtures/card1006/linux-blocking-frames.json` | Captured Linux provenance/frames saying InboxConhost; preserve original observation, not Windows evidence. |
| `tests/Antiphon.Tests/Agents/Fixtures/card1006/provenance.md` | Captured Linux provenance/frames saying InboxConhost; preserve original observation, not Windows evidence. |
| `tests/Antiphon.Tests/Agents/GrokHerdrRealCliStubProxyCanaryTests.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/GrokLinuxBlockingPromptTests.cs` | Asserts captured historical Linux fixture backend string; preserve historical provenance, new captures report actual UnixPty. |
| `tests/Antiphon.Tests/Agents/GrokRealCliStubProxyCanaryTests.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/PhoneHomeConnectionTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Agents/RealCliStubBServerHarness.cs` | Modern or Herdr fixture/canary; retain isolation and modern pins, not an inbox implementation. |
| `tests/Antiphon.Tests/Agents/SessionRunnerCapabilityGateTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Agents/SessionRunnerGenerationWireTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Agents/SessionRunnerHttpClientHerdrWireTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/AgentTaskInputFallbackTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/AgentTaskLandReceiptTests.cs` | Conservative-vs-modern delivery assertions or explicit ceiling fixture; C migrate to generic conservative identity, retain bytes/zero-inline/pointers. |
| `tests/Antiphon.Tests/Application/DefaultRunnerCreateTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/DefaultRunnerPinTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/DelegateLaunchArgvIntegrityTests.cs` | Backend contract cited in commentary; retain argv contract, update reference only if renamed. |
| `tests/Antiphon.Tests/Application/DelegationBriefCeilingPtyTests.cs` | Delivery integration includes declared backend/shared helper and complete-body proof; B migrate inbox helper, retain modern and spill contracts. |
| `tests/Antiphon.Tests/Application/DeliveryBackendCeilingsTests.cs` | Conservative-vs-modern delivery assertions or explicit ceiling fixture; C migrate to generic conservative identity, retain bytes/zero-inline/pointers. |
| `tests/Antiphon.Tests/Application/DurableRunnerSpillReceiptTests.cs` | Conservative-vs-modern delivery assertions or explicit ceiling fixture; C migrate to generic conservative identity, retain bytes/zero-inline/pointers. |
| `tests/Antiphon.Tests/Application/GrokDeliveryShapeTests.cs` | Conservative-vs-modern delivery assertions or explicit ceiling fixture; C migrate to generic conservative identity, retain bytes/zero-inline/pointers. |
| `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/MultiRunnerDirectoryTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/OutputDistillationPolicyTests.cs` | Conservative-vs-modern delivery assertions or explicit ceiling fixture; C migrate to generic conservative identity, retain bytes/zero-inline/pointers. |
| `tests/Antiphon.Tests/Application/PhoneHomeImmediateSendTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/PhoneHomeSpillTransportTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/PhoneHomeTaskDispatchProjectionTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/PtyDeliveryCeilingsTests.cs` | Conservative-vs-modern delivery assertions or explicit ceiling fixture; C migrate to generic conservative identity, retain bytes/zero-inline/pointers. |
| `tests/Antiphon.Tests/Application/RunnerCatalogueTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/RunnerDefaultTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/SessionDeliveryProfileTests.cs` | Conservative-vs-modern delivery assertions or explicit ceiling fixture; C migrate to generic conservative identity, retain bytes/zero-inline/pointers. |
| `tests/Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs` | Delivery integration includes declared backend/shared helper and complete-body proof; B migrate inbox helper, retain modern and spill contracts. |
| `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs` | Delivery integration includes declared backend/shared helper and complete-body proof; B migrate inbox helper, retain modern and spill contracts. |
| `tests/Antiphon.Tests/Application/SourceLandingAdmissionTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/SpecialistExecutionIdentityTests.cs` | Synthetic modern profile evidence, no inbox spawn; retain. |
| `tests/Antiphon.Tests/Application/TaskPlatformDispatchTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/TaskPlatformPersistenceTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/TaskPlatformPlacementTests.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/Application/TypedBodySpillTests.cs` | Conservative-vs-modern delivery assertions or explicit ceiling fixture; C migrate to generic conservative identity, retain bytes/zero-inline/pointers. |
| `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs` | Asserts both Compose inbox settings; C replace with absence/default Unix contract after CARD-1008. |
| `tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs` | Fixture exposes instance/child backend override; A audit default, retain isolation and explicit modern support. |
| `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/TestHelpers/PtyBackendEnvGuard.cs` | A: keep assembly env isolation; update inbox-default assertion to platform-correct default (five copies). |
| `tests/Antiphon.Tests/TestHelpers/SseStreamHandler.cs` | Legacy capability string fixture, not native inbox selection; retain old-wire acceptance, no mass modern replacement. |
| `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs` | Guard/bootstrap diagnostics and inherited-env child probe; A keep eager guard and isolation, audit expectation. |
| `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLazyInitializationTests.cs` | Guard/bootstrap diagnostics and inherited-env child probe; A keep eager guard and isolation, audit expectation. |
| `tests/linux-test-roster.json` | Maintained test-selection roster references guards/contracts; update via established roster workflow when methods change, not as arbitrary evidence. |

## Appendix B: dependencies missed by the exact expression

The supplemental scan found these relevant files because they use literal `inbox`,
Porta types, native CreatePseudoConsole calls or CLI spelling without the exact
card expression. They are additional scope evidence, not part of the 167 count.

| File | Classification and disposition |
|---|---|
| `src/Antiphon.Agents.Pty/ConPtyRedistributable.cs` | Pair existence, Windows/architecture and exclusive override checks; B adds usable-pair readiness, keeps provenance. |
| `src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj` | Porta package supplies Unix and shared option types; retain it and the pinned modern package/content copy. |
| `src/Antiphon.Agents.Pty/PtySession.cs` | Contains IPtySession and PortaPtySession, not standalone files by those names. C retains adapter for Unix and updates Windows-default comments. |
| `src/Antiphon.Agents.Pty/ModernConPtyConnection.cs` | Retained loader/spawn/job/DA1 path; B reuse for readiness and propagate native load/export/create failures. |
| `src/Antiphon.Agents.Pty/Da1StartupResponder.cs` | Retained modern startup handshake; inbox comparison is explanatory. |
| `src/Antiphon.Agents.Pty/LaunchArgvGuard.cs` | Guards actual Windows argv and documents Porta workaround; retain modern validation, C remove obsolete inbox-only wording/branch if any. |
| `src/Antiphon.PtyHost.Client/PtyHostLauncher.cs` | Carries `--pty-backend`, shadow copies and launch failures; retain compatible modern argument, B preserve refusal across detached-host startup. |
| `src/Antiphon.FakeClaude/Program.cs` | Typed clip versus bracketed-paste model; retain both models, update obsolete comments that imply all hosts strip markers. |
| `tests/Antiphon.Agents.Pty.Tests/ConPtyHost.cs` | Bench has its own null-DLL kernel32 inbox arm; C remove that arm, not just production enum references. Retain needed modern bench functionality. |
| `tests/Antiphon.Agents.Pty.Tests/NodeStdinProbe.cs` | Nullable override inherits the code default. A review callers; C remove the assertion that paired backends are required. |
| `tests/Antiphon.Agents.Pty.Tests/FakeVsRealClipParityTests.cs` | Old typed arm uses a null backend and would change meaning at A. Make typed-vs-paste explicit on modern; no live inbox run. Preserve historical measured receipts. |
| `tests/Antiphon.Agents.Pty.Tests/ClaudeTrustPromptCanaryTests.cs` | Explicit modern canary with historical inbox comparison; retain current oracle, no migration of transport. |
| `tests/Antiphon.Agents.Pty.Tests/Da1StartupResponderTests.cs` | Retained pure modern handshake tests; no inbox implementation. |
| `tests/Antiphon.Agents.Pty.Tests/LaunchArgvGuardTests.cs` | Pins vendor formatter defect as well as guard behavior; C retire only the Windows Porta workaround oracle, preserve native modern argv fidelity. |
| `tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs` | Literal inbox unsupported-custody launch; B/C replace with removed-selector refusal while preserving modern containment/seal proofs. |
| `tests/Antiphon.Agents.Pty.Tests/UnixPtyArgvTests.cs` | Literal inbox pin actually exercises Unix Porta. B use default/Unix semantics; preserve native argv, NUL refusal and tracked containment tests. |
| `tests/Antiphon.PtyHost.Tests/ShadowCopyStoreTests.cs` | Actual shipped-pair retention test, plus Unix native closure/executable mode. C retains all staging guarantees. |
| `tests/Antiphon.SessionRunner.Tests/RunnerCustodyLedgerBackendTests.cs` | Literal inbox backend in negative custody test; B replace expected failure shape without weakening no-start invariant. |
| `tests/Antiphon.SessionRunner.Tests/CodexProviderAuthRoutingTests.cs` | Legacy request string in a fake capability payload; retain wire/auth test semantics. |
| `tests/Antiphon.Tests/Agents/CodexCommandLengthSessionTests.cs` | Real inbox interactive command-length arm; B/C remove that arm and retain modern command-length proof. |
| `tests/Antiphon.Tests/Agents/CodexAdapterIntegrationTests.cs` | Explicit modern pin, historical inbox comparison only; retain. |
| `tests/Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs` | Direct runtime helper pins inbox; B move retained complete-receipt assertions to modern. |
| `tests/Antiphon.Tests/Application/UnixDelegateLaunchArgvTests.cs` | Literal inbox actually exercises Linux; B use OS default, preserve exact launch argument bytes. |
| `tests/Antiphon.Tests/TestHelpers/RemoteControlPtyLane.cs` | Shared PinnedBackend constant is inbox; B migrate all helper consumers together and distinguish deliberately typed fake input from paste. |
| `docs/session-runtime-invariants.md` | Living delivery owner uses lowercase inbox outside the exact expression. A/C supersede historical current-tense claims; retain LF, whole-write, separate Enter and transcript confirmation. |

The broad search also finds `RunnerWorkspaceService.cs` and its tests,
`RemoteSpillCourier`, `TypedBodySpill`, `DelegationReportFormatter`,
`TaskInputReadFailure`, `LandNotificationPayload`, cleanup allowlists,
`server/appsettings.json`, channel/receipt/worktree tests, and the messaging
Gateway/Service inbox stores and migrations. These refer to **spill directories or
message ingestion**, not ConPTY. Preserve those names and behaviors. In particular,
this brief arrived through `.antiphon/inbox`; deleting that infrastructure would
break the contract this card is required to keep.

## Plan validation and handoff

This Plan dispatch performed card/source/owner reads, the exact and supplemental
inventories, live runner-default/catalogue reads and documentation consistency
checks. No build, test, native canary, deployment or source-code change is claimed.
The code premise is substantially correct; the Linux identity and conservative
ceiling findings refine implementation scope and do not require another
Investigate stage. Next stage is TestDesign, with release A only as its executable
scope and B/C retained as separately commissioned follow-ups.

## Verification design

TestDesign freeze, 2026-10-04, task `1b2000a7`. This section supersedes the
verification proposal and dated dependency observations, not D-1–D-10 or the
A/B/C fix design. Only release A is commissioned. Inspected: plan/base
`13d7100febc3f4b595299eb982bf7fc5e44c88af`, landed CARD-1011 `8bb0cea04`,
CARD-0959 `origin/feat/card-task-bd02f8d9` at `d45a7915d`, CARD-1020 freeze
`d4203e109`, CARD-1023 plan `02d54f6ae`. A fresh fetch found master at
`f7132d329` (CARD-1024 evidence cleanup), without CARD-0959. `ef329fb0` is a
**task ID**, not a commit: its repair branch tip inspected is
`c57e1980fe7388f122cf69a73f20908ca14293de`. No builds, tests or activation ran
in TestDesign. Proposed new methods below are Code obligations, not passing evidence.

### Inspection

| Bodies read, including fixture setup | Boundaries -> coverage or exclusion |
|---|---|
| `PtyBackend.cs`, `ConPtyRedistributable.cs`, `PtyAgentRunner.LaunchCoreAsync`; nearest fixture `PtyBackendContractTests` | Selector/default/pair/platform/instance precedence -> V-1/R-1. |
| `RunnerCapabilitiesTests` (seven bodies), `PtyBackendSeamTests` and its owned detached-host helper; Program backend composition, runtime/phone-home capability and custody projections | Config/environment/instance, both producers and actual host argv -> V-2/V-3. Native inbox seam excluded in A; B owns retirement. |
| All five `PtyBackendEnvGuard.cs` bodies; `ConPtyEnvironmentIsolationGuardTests`, including IL scanner fixtures | Independent assembly hooks, mutator exclusion and nonvacuous census -> R-2. |
| `ModernPtyDa1Tests`, `Da1StartupResponderTests`, `Da1StartupResponder`, `PtyInputEncoding`; all backend contract methods | Actual host, hashes, marker preservation, fragmented/query/reply boundaries -> R-1/R-6. |
| Shadow-copy pair test plus `CreateFixture`/`Cleanup`; entire `UnixPtyArgvTests`, NativeFixture, Placement and Journal | Both content files, exact argv, NUL before/after containment and start-intent order -> R-3. Text-file shadow fixture cannot prove native load. |
| Entire `PtyDeliveryCeilingsTests`, `SessionDeliveryProfileTests`, `GrokDeliveryShapeTests`, `TypedBodySpillTests` and their profile/stub/temp helpers; `PtyDeliveryProfile` | Local/remote, old/unknown/null capabilities, kind, byte/char and pointer boundaries -> R-4. |
| Entire `FakeVsRealClipParityTests`, `NodeStdinProbe` and `PtyInputChunkingTests`; modern clipping helper/paste methods in `FakeClaudeContractTests` | Implicit typed arm becomes paste after default flip -> R-6; no real-provider qualification in A. |
| Entire `SessionQueueReceiptPlumbingTests`, PtyWorld, ForwardingClient, InsertFault and `SessionQueueTranscriptPump`; nearest real-queue composition in `SessionMessageQueuePtyIntegrationTests.Queued_message_submits_through_the_real_runtime_runner_pty_path` | Busy/eligible, insert/attempt/body/Enter/ingestion/verdict cuts -> V-4. |
| Landed C1011 backend method and explicit fresh-worktree method, `C1011GrokQualification` verdict/observer, WQ ledger | Two native backend arguments -> one modern result, exact native UserPrompt -> R-5. Preserve historical WQ; no real Grok. |
| `IsolatedSessionRunner.StartProcess`, direct-client constructor; backend-pin and Node-probe caller census | Unset policy changes; E2E has no explicit inbox launch pin, only guard expectation. Explicit pins separated below. |
| `PlanTableImporter`, `ManifestValidator`, `Program.Import`; testing/build manifest, delivery, slot and census contracts | First exact Checkpoints heading, escaped pipes, result counts and isolated builds -> sole active table below. |

New-test setup missing today: eight policy methods, five capability methods, three
launch methods, one typed-input method and one receipt-negative method. Code must implement them through the
production seams, using the nearest inspected fixtures above. Windows x64 needs
the shipped pair, Node, staged fakeclaude/fakegrok/runner/host apphosts; server rows
need the established isolated PostgreSQL fixture. Linux needs Node and Porta native
assets. Missing prerequisites fail the declared lane; skip/zero/Linux-only evidence
cannot qualify Windows. No installed Grok, provider credentials or paid turn needed.

The five guard edit paths are `tests/Antiphon.Agents.Pty.Tests/PtyBackendEnvGuard.cs`,
`tests/Antiphon.SessionRunner.Tests/PtyBackendEnvGuard.cs`,
`tests/Antiphon.PtyHost.Tests/PtyBackendEnvGuard.cs`,
`tests/Antiphon.Tests/TestHelpers/PtyBackendEnvGuard.cs`, and
`tests/Antiphon.E2E/Fixtures/PtyBackendEnvGuard.cs`.

Dependency and migration order:

1. CARD-1011 is landed. Integrate the target containing `8bb0cea04` through the
   normal landing owner; do not replay its in-flight branches. S3 removes only
   `[Arguments("inbox")]` from
   `RunnerGrokAdapterReadyTestsPty.C1011_windows_backends_reach_ready_and_complete_prompt`:
   keep its name and modern argument, Ready, no preprompt, single exact-body
   UserPrompt assertions. Count **2 -> 1**. The parameter is not in
   `FakeGrokContractTests`; that separate inbox helper is B work. Preserve
   `C1011_real_fresh_worktree_ready_and_one_turn`, `C1011GrokQualification`, trust
   handling and stored WQ receipts. WQ-1 was dropped, never passed. CARD-1011
   CP-3/CP-6 inbox evidence is historical and cannot prove this default flip.
2. Wait for reviewed **inert CARD-0959 plus ef329fb0 repair** land before S2.
   Append nullable `PtyBackendDeprecated` after `CodexCliVersion`,
   `CodexCliVersionCheckedAtUtc`, `CodexCliVersionError`,
   `CodexCliLauncherFingerprint`; preserve probe feature and both projections.
   No freshness/version/authentication admission gate belongs here. The inspected
   branch leaves RunnerCapabilitiesTests at seven. Recount CP-2's 7+5=12,
   any changed selected CP-6/CP-7 methods/argument rows, the overall result floor,
   and Cost after landing. Do not replace floors with CARD-0959's whole-suite count.
3. Serialize S3 edits to the C1011 caller and shared direct-client use with
   CARD-1020's landed cleanup fix. Preserve its captured process identities,
   nested disposal and physical host/child/OpenConsole exit assertions. Wait for
   that shared-file owner before CP-7 rather than copying its implementation.
   Its two dashboard-marker cases are
   `Fake_dashboard_marker_reaches_ready_and_complete_first_prompt`, distinct from
   C1011's two backend arguments. Production detach is unchanged.
4. CARD-1023 consumes backend as a matrix dimension. Keep old wire/history strings;
   create **no inbox matrix row, native checkpoint or PC**. Its observation-first
   plan grants no new admission floor and does not block A's policy work.

Immediate S3 fixture changes: all five guards; the C1011 method above;
`SessionQueueReceiptPlumbingTests.PtyWorld` (modern pin and host witness);
`FakeVsRealClipParityTests` (explicit modern, deliberately unwrapped typed arm);
`PtyInputChunkingTests` (explicit modern; its typed-read-shape method deliberately
uses unwrapped input); and `UnixPtyArgvTests.NativeFixture` (OS default, assert
UnixPty/no fallback). Share the parity encoding choice in a small internal test
helper used by both parity peers and new R-6. Merely changing the backend string
while keeping the typed arm bracketed is incorrect. `NodeStdinProbe` retains its
nullable override. Its callers in `PtyInputLossExperiments` and
`PtyPasteMarkerExperiments` are explicit experiments, outside A execution; update
current-default descriptions, preserve historical data, and require an explicit
backend for future measurements. `IsolatedSessionRunner` needs accurate comments:
`modernPty=false` omits config; it does not select inbox. A hand-launched runner with
neither config nor environment now gets modern on Windows, UnixPty on Linux.
The shipped runner appsettings already requests modern. The E2E guard row starts
no browser or daemon.

Explicit native inbox fixtures retained for B, excluded from A execution:
`PtyBracketedPasteContractTests`, `WindowsPtyArgvNativeTests`' inbox argument,
`PtyBackendSeamTests`' declared-inbox arm, `FakeClaudeContractTests`' inbox helpers,
`FakeGrokContractTests.LaunchReadyFakeAsync`, `ClaudeSubmitContractTests`,
`ClaudePasteLossCanaryTests`, `PtyAgentRunnerTests`, `PtyKillProcessTreeTests`,
`PtyCustodyTests`, `RunnerChildClaudeTokenInheritanceTests`,
`CodexCommandLengthSessionTests`, shared `RemoteControlPtyLane.PinnedBackend`,
`SessionMessageQueuePtyIntegrationTests.PinnedBackend`,
`SessionMessageQueueGrokPtyIntegrationTests.PinnedBackend`, and
`DelegationBriefCeilingPtyTests`. Linux selectors in `RunnerPlatformLaunchTests`,
`RunnerCustodyLedgerBackendTests`, `LinuxCgroupCustodyTests` and
`UnixDelegateLaunchArgvTests` are platform/containment fixtures, not Windows
qualification. Appendix A/B remains the file-classification ledger. Conservative
profile inputs and old DTO fixtures stay explicit; do not mass-replace with modern.

### Delivery inventory

A creates no outbox or notification producer, but changes the transport selected
by existing input producers. Recipient evidence is therefore mandatory.

| Producer -> destination | Durable identity / persistence | Recovery / observable receipt |
|---|---|---|
| SessionMessageQueueService -> AgentSessionRuntime -> DirectSessionRunnerClient/runtime -> owned PtyHost -> FakeClaude | Session ID + queue row ID + attempt + baseline; row committed before input, attempt/baseline committed before write; native JSONL UUID | V-4 uses the real queue and transport, compares whole native-file and persisted destination UserPrompt after baseline exactly once. |
| Persisted Pending/Sent row -> recreated queue -> same recipient | Same row/body/attempt and baseline, separate body/Enter handoff | Pending stays owed while busy; retry unsent, Enter-only after composer evidence, late-confirm after native receipt/ingestion/verdict cut without duplicate body. |
| Runtime decision -> HTTP and phone-home projections -> server profile | Runner store/build identity; existing hello/refresh connection owns transport, no new durable state | V-2 production projections + old/new JSON; R-4 downgrade behavior. Projection is not hello-delivery evidence or recipient input. Phone-home transport/recovery is unchanged. |
| Existing spill helper -> stored body + bounded pointer -> existing queue | Task/message marker, relative path, exact file bytes; pointer measured after binding | R-4 proves file/shape only, never receipt. No changed remote-spill persistence/handoff; its worker-death recovery remains the existing owner's scope. |

V-4 runs `C475_QueueCommitAndTransportRecovery` with all six cuts:
`insert-fails`, `pending-before-flush`, `attempt-before-write`, `body-before-enter`,
`recipient-before-ingestion`, `receipt-before-verdict`; also
`C475_AlreadyIdleWhenIdleHasRecipientReceipt`. The pending cut is the busy-recipient
case; assert both UserPrompt lists empty before TurnEnd, then complete receipt.
Keep final row-ID, attempt-count, baseline, multiplicity and exact-body assertions.
The separate receipt-negative method below covers stale/partial transcript rows;
do not contaminate these six cases' exact final receipt lists. Strengthen
`C475_MultilineWritesKeepPasteMarkers` to use CRLF and Unicode, assert normalized
LF body, both markers and a distinct subsequent CR write. Capture the enqueue
outcome/error, assert observed input shape before rethrowing an unexpected error
or asserting its delivery verdict, then require the whole recipient receipt.
The fixture receipt wait must finish at a named `recipient-receipt` Shouldly
assertion after its existing bounded wait, so a missing destination receipt gives
an assertion failure rather than a fixture timeout. No wait budget is widened.

Substitutes and limits: FakeClaude/FakeGrok are local recipient processes, not real
models; they prove submission/native transcript, not current paid-provider behavior.
The receipt pump uses production normalization with test DB ingestion; its five
cut cases do not prove production event streaming. Queue recreation/interceptor cuts
model persisted crash states, not power loss or daemon relaunch. Node proves bytes,
not UserPrompt. Capability/request/queue insert/Sent/event/ack never proves delivery.
No native queue test may stop before matching complete recipient evidence.

### Proves it works now

New methods are single-result methods with internal boundary loops. Quoted witness
strings below are required assertion labels, used by the PC roster.

- V-1: platform-aware policy | Pty unit | eight new `PtyBackendPolicyTests` methods:
  `Windows_defaults_request_modern` (null/empty/space/tab with empty ambient,
  `default-modern`); `Windows_modern_aliases_resolve_modern` (six aliases plus
  trim/case, `modern-alias`); `Windows_unknown_selector_warns_and_chooses_modern`
  (`unknown-modern`, preserve raw/reason); `Windows_legacy_aliases_are_deprecated_without_fallback`
  (five aliases, no discovery, `legacy-deprecated`);
  `Windows_missing_pairs_fall_back_and_preserve_request` (default/modern/unknown x
  neither/DLL-only/host-only/both; exclusive override, `pair-fallback`);
  `Unix_ignores_windows_selectors_without_probing` (all selector families, counting
  locator, `unix-no-probe` and `unix-locator-calls=0`); `Enum_wire_values_remain_stable` (0/1/2, `enum-stable`);
  `Instance_request_outranks_environment` (opposites and null vs explicit empty,
  `instance-wins`). Use a deterministic platform/locator seam in production policy,
  not a test copy. Both-present dummy files establish discovery, not usability.
- V-2: coherent capabilities | runner composition/unit | five new
  `C1022BackendCapabilitiesTests` methods:
  `Daemon_environment_overrides_configuration` (null/empty/whitespace/modern/inbox
  env x unset/opposite config through the production composition helper,
  `daemon-precedence`); `Local_capabilities_use_runtime_decision` (`local-decision`);
  `Phone_home_capabilities_use_runtime_decision` (`phone-decision`);
  `Custody_uses_runtime_decision` (`custody-decision`);
  `Capability_json_preserves_nullable_deprecation_and_cli_observations`
  (`deprecation-wire`, `cli-fields-retained`). Use the production instance-decision
  seam for portable Windows decisions; Linux custody keeps its existing cgroup
  probe. Opposite ambient input must distinguish each projection from the instance.
  Exercise true/false/null/absent, literal old four-field JSON, unknown backend and
  distinct values for all four CARD-0959 fields in both actual production projections,
  then serialize those projections. Extend existing round-trip test. Mark any
  capability fixture that changes process environment with unkeyed NotInParallel
  and restore the previous value in finally.
  Also check production logging helper emits warning for deprecated/fallback and
  unknown selection, but repeated capability reads do not repeat startup warnings.
  Diagnostic log counts are not delivery authorization.
- V-3: actual owned Windows host | runner integration | three new
  `C1022BackendLaunchTests` methods: `Unset_owned_host_runs_modern`,
  `Configured_owned_host_runs_modern`, `Declared_modern_overrides_inherited_inbox`.
  First two use Program's production composition with isolated config; third uses
  direct-runtime override. Host log, observed OpenConsole and both capability
  projections agree: `host-modern`, `host-request`, no fallback/deprecation, correct
  package/version. Preserve raw unset request; explicit config/instance is modern.
  Keep unkeyed NotInParallel, assembly limiter, unique roots and awaited owned
  teardown. No test spawns inbox.
- V-4: queued modern input/recovery | server integration | complete
  `SessionQueueReceiptPlumbingTests` (21 results, nine methods) | all inventory
  receipt and recovery assertions above, plus pump and failed-startup cleanup tests.

### Guards the regression

- R-1: actual modern contract | four retained `PtyBackendContractTests` methods,
  four `ModernPtyDa1Tests`, 22 `Da1StartupResponderTests` results | package hashes,
  actual shipped host, both markers and complete body, no startup stall/leaked
  reply, exactly one reply including split/repeated queries. Remove the old
  `The_flag_defaults_off` method (five obsolete results); V-1 replaces it.
  Keep incomplete-pair test name/unkeyed exclusion; extend to host-only/exclusive
  override and deprecation. No inbox spawn in that negative test.
- R-2: deterministic fixtures | five `PtyBackendEnvGuardTests.The_suite_ignores_an_inherited_pty_backend`
  and both `ConPtyEnvironmentIsolationGuardTests` | environment null; Windows modern
  with staged pair/no fallback, Linux UnixPty/no fallback; mutator census and no
  violations. Every guard row starts with a nonempty inherited selector.
- R-3: package closure and Unix | shadow pair method + complete `UnixPtyArgvTests`
  (17 results: 9 argv, 4 original NUL, 1 tracked argv, 2 final NUL, 1 consumed
  attempt) | both staged files, exact argv, no spawn/start-intent on invalid input,
  unchanged Porta and containment. Linux guard adds one result to CP-9.
- R-4: ceilings/spill retained | CP-6 full classes: 12+7+14+9 existing results plus
  `PtyDeliveryCeilingsTests.Unix_backend_keeps_conservative_limits` and
  `Unknown_runner_backend_downgrades_modern_server` -> 44 | inbox/Unix conservative
  900/3000/1024; modern 43200/14400/86400; old/unknown report downgrade; null or
  unreachable report retains local decision; phone-home/uncertain Herdr conservative;
  non-Claude inline zero; exact stored bytes and bounded join-safe pointer. Extend
  existing methods with internal 899/900/901, 1023/1024/1025, and modern threshold
  minus/equal/plus combinations, UTF-8 expansion and long bound paths. These loops
  do not add result counts. Keep existing spill write-failure contract.
- R-5: landed C1011 behavior | its modern-only backend method | Ready, no prompt
  before send, one exact native UserPrompt and modern host evidence; preserve
  CARD-1020's disposal assertions. No real Grok canary.
- R-6: explicit typed versus paste helper | new Pty-project
  `C1022TypedInputTests.Typed_and_paste_modes_reach_the_modern_peer_distinctly`
  plus six `PtyInputChunkingTests` results | use the helper shared with parity peers:
  Node observes normalized unwrapped LF/no markers (`typed-no-markers`) versus both
  paste markers (`paste-markers`) and complete bytes (`whole-body`). Also drive the
  clipping fake with the same helper and clipping armed: typed loses the modeled
  chunk; paste preserves all marked lines. No real Claude; this does not requalify
  historical real-provider measurements. Do not loosen an inherited timing/chunk
  assertion to make modern green; confirm at base and return a concrete finding.

### Guard inventory

Every independently bypassable safety guard claimed by this freeze is listed below,
including retained guards. Diagnostic warnings and DTO observation values do not
create authorization; their ordinary assertions remain V-1/V-2. Native custody,
cleanup implementation and remote spill protocols unchanged by A retain their own
cards' PC inventories; no claim of new qualification is made for them here.

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

### Positive controls

Mutation runs each control **after land**, at the landed source: green discovery,
compiling defect, intended red assertion, restore, fresh build and same method green.
Code runs ordinary V/R only; separate Review judges this inventory before land.
No native inbox launch is a control. A startup/fixture/build error or zero tests is
not red. Each row below denotes one exact filter `/*/*/Class/Method` formed from its
Method cell; no class wildcard is permitted. Argument rows all execute for that
method. Projects are explicit; Windows is required except portable policy/parser/
capability controls and Linux Unix-argv controls. The one alternate-pair PC stages
an isolated copy of the approved modern DLL/EXE and removes it after restoration;
it never substitutes inbox or modifies deployed files.

For the five environment controls launch a fresh test host with
`ANTIPHON_PTY_BACKEND=inbox`; it must clear the value. Do not mutate the test
assertion. All other controls change the production predicate/assignment identified
below, except the explicitly test-owned environment/serialization/typed-helper
guards. Do not combine independent controls in one production file. Retain per-PC
red assertion/result count and restored green evidence outside SourceLanding.

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

Inventory audit: bodies read; guards=73, mapped=73, missing=0,
duplicate PC maps=0. Every control has a production/test-owned mutation, exact
method, project, minimum and decisive assertion. New-method controls become
runnable when Code implements the named tests; all are authoring obligations,
not claimed mutation results. No safety guard in A is silently untested.

### Out of scope

- B startup/spawn refusal (`pty_backend_removed`, `pty_backend_invalid`,
  `modern_conpty_unavailable`), corrupt DLL/export/blocked-host preflight, readiness
  invalidation and C deletion need their own freezes. A deliberately retains
  observable missing-pair fallback; neither fake files nor Linux can prove B.
- No full provider/E2E browser sweep, real Grok, paid-model canary, live inbox
  qualification, new compatibility row, capability admission gate, Compose change,
  Docker deployment-script change, Linux transport rewrite or new delivery envelope.
- No `Antiphon.Tests.Checkpoints` test is added/removed. Its independent literal
  census stays **377** in `scripts/lib/checkpoint-usage.ps1`; this plan adds tests
  in Pty, SessionRunner and Application, not Checkpoints. After unrelated land,
  preserve whatever independently verified census then owns that namespace.
- Untouched remote spill protocol, production detach/re-adoption, process cleanup
  and native ownership predicates keep their separate card tests/PCs. A uses
  CARD-1020's test helper; it does not redesign teardown or infer exit from an ack.
- Historical inbox fixtures/receipts in Appendix A/B are not evidence for modern.
  The later fixture migration must revisit their actual input model; skipping or
  weakening tests to conceal a regression is excluded.

### Checkpoints

This is the **only active/importable table**, the closed ordinary scope for A.
The old proposal heading was renamed because the real importer reads the first
exact `### Checkpoints`; merely appending a second table would run stale filters.
One isolated build and one exact filter per row; all rows serial. Portable CP-1/2
use normal placement; CP-3–8 require Windows x64; CP-9 requires Linux. Group names
are labels, not scheduling enforcement: explicitly select the lane's rows.
`Min` is expanded TUnit execution count, not assertions, loop cases or minutes.
All rows require zero failures and zero skips. No unlisted full-suite run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-policy/` | portable-policy | `/*/*/(PtyBackendPolicyTests*)\|(Da1StartupResponderTests*)/*` | V-1, R-1 | 8 new policy + 22 parser results; 0 failed/skipped | 30 | 4 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-2 | S1-S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1022-capabilities/` | portable-capabilities | `/*/*/(C1022BackendCapabilitiesTests*)\|(RunnerCapabilitiesTests*)/*` | V-2 | 5 new + 7 existing; rebaseline after CARD-0959 land; 0 failed/skipped | 12 | 5 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-3 | S1-S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-windows-native/` | windows-modern | `/*/*/(PtyBackendContractTests*)\|(ModernPtyDa1Tests*)\|(PtyBackendEnvGuardTests*)\|(ConPtyEnvironmentIsolationGuardTests*)\|(C1022TypedInputTests*)\|(PtyInputChunkingTests*)/*` | R-1, R-2, R-6 | 4 + 4 + 1 + 2 + 1 + 6 results; 0 failed/skipped | 18 | 9 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-4 | S1-S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1022-windows-launch/` | windows-owned-hosts | `/*/*/(C1022BackendLaunchTests*)\|(PtyBackendEnvGuardTests*)/*` | V-3, R-2 | 3 new owned launches + 1 guard; 0 failed/skipped | 4 | 7 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-5 | S1-S3 | `tests/Antiphon.PtyHost.Tests -> bin-c1022-windows-pair/` | windows-shadow-pair | `/*/*/(ShadowCopyStoreTests*)\|(PtyBackendEnvGuardTests*)/(Shipped_conpty_binaries_survive_the_deps_json_closure_filter*)\|(The_suite_ignores_an_inherited_pty_backend*)` | R-2, R-3 | Both named methods; 0 failed/skipped | 2 | 4 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-6 | S1-S3 | `tests/Antiphon.Tests -> bin-c1022-windows-delivery/` | windows-delivery | `/*/Antiphon.Tests.Application/(PtyDeliveryCeilingsTests*)\|(SessionDeliveryProfileTests*)\|(GrokDeliveryShapeTests*)\|(TypedBodySpillTests*)/*` | R-4 | 14 + 7 + 14 + 9 results; 0 failed/skipped | 44 | 8 | true | `ANTIPHON_PTY_BACKEND=inbox;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S1-S3 | `tests/Antiphon.Tests -> bin-c1022-server-guard/` | windows-queue-and-grok | `/*/*/(PtyBackendEnvGuardTests*)\|(SessionQueueReceiptPlumbingTests*)\|(RunnerGrokAdapterReadyTestsPty*)/(The_suite_ignores_an_inherited_pty_backend*)\|(C475_*)\|(C1022_Incomplete_or_stale_receipts_do_not_confirm*)\|(C1011_windows_backends_reach_ready_and_complete_prompt*)` | V-4, R-2, R-5 | 1 guard + 20 retained queue + 1 new receipt-negative + 1 modern C1011; 0 failed/skipped | 23 | 12 | true | `ANTIPHON_PTY_BACKEND=inbox;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S1-S3 | `tests/Antiphon.E2E -> bin-c1022-e2e-guard/` | windows-e2e-guard | `/*/*/PtyBackendEnvGuardTests/*` | R-2 | 1 guard, no browser; 0 failed/skipped | 1 | 5 | true | `ANTIPHON_PTY_BACKEND=inbox` |
| CP-9 | S1-S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1022-linux-argv/` | linux-unix-transport | `/*/*/(UnixPtyArgvTests*)\|(PtyBackendEnvGuardTests*)/*` | R-2, R-3 | 17 Unix argument-expanded + 1 guard; 0 failed/skipped | 18 | 5 | true | `ANTIPHON_PTY_BACKEND=modern` |

The added receipt-negative method lives in `SessionQueueReceiptPlumbingTests`:
`C1022_Incomplete_or_stale_receipts_do_not_confirm`, one result with two independent
worlds. Hold transport before first write, retain the committed attempt/baseline,
then use the existing test DB seam to insert (a) full body at/below baseline and
(b) identity-matching truncated body above it. Recreate queue and flush. Stale
receipt leaves the row owed until new complete input; current truncated receipt
parks it under existing truncation policy and never marks LateConfirmed. Assert
no complete native receipt was fabricated. Use separate worlds so negative rows
do not contaminate the six recovery cases' exact `[text]` final lists. Release all
blocked tasks and reap only fixture-owned processes. This is negative evidence,
not a claim that truncated delivery is successful. New total is **152 results**:
CP-1/2/9 = 60; Windows CP-3–8 = 92.

Importer contract: required nine columns remain in order; only supported optional
Serial/Environment columns follow. Escaped `\|` becomes literal filter OR. All
builds have different `bin-.../` outputs and the same After group; no reuse or
unrecognized Lane/Configuration column. The importer does not validate test
existence or actual native execution counts: Code discovery/receipts do that.
Read-only import uses an already built checkpoint DLL, never `dotnet run` here.
Its source importer was compared to this checkout before use. Import output is
ignored `.antiphon/c1022-freeze-import.yaml`; no generated file is committed.

Read-only importer result on 2026-10-04: exit 0, `imported 9 rows`; no warnings.
The DLL used was `/work/worktrees/task-1d3e0d92/tools/Antiphon.Checkpoints/bin-c1020-importer/Antiphon.Checkpoints.dll`;
`git diff --no-index` of its source `Manifest/PlanTableImporter.cs` against this
checkout returned no differences. This is manifest syntax evidence only.

Code/Review: commit all S1–S3 edits before the grouped runs. Use the checkpoint
runner tool with this plan and `--rows CP-1,CP-2,CP-9` on Linux, and
`--rows CP-3,CP-4,CP-5,CP-6,CP-7,CP-8` on Windows, plus
`--expected-source-sha` set to the exact committed code SHA. Bootstrap the tool
once through `scripts/build-slot.ps1` to an isolated `bin-c1022-tool/` only if
needed; report that infrastructure build separately. Checkpoint rows own their
slots; no double wrapper and no NoSlot. Await every run, continuing wait after
exit 75. Capture unedited CHECKPOINT lines, source/build provenance, counts and
native evidence. Run `scripts/check-evidence-diff.ps1` over the full task range.
Validate receipts against their actual SHA. Do not edit source during a run.
Remove only owned alternate outputs after confirmed test-process exit.

Separate final-SHA Windows Debug is mandatory: run **CP-3–CP-8, exactly as above**
on the final frozen code SHA, minimum 92 total with every row meeting its own
floor and no skips. This is distinct from ordinary Code evidence, even if no
source changed. Default Debug build configuration applies; do not reuse a Release
output. Record OS/process architecture, source and loaded runner/host build SHA,
host session ID/PID/start time, actual OpenConsole path, both file hashes, and
host log lines containing `pty backend: ModernConPty` and
`Microsoft.Windows.Console.ConPTY 1.24.260710001`. Explicit config/override rows
must report `PtyBackend="ModernConPty"`, `PtyBackendRequested="modern"`,
`PtyBackendFellBack=false`, `PtyBackendDeprecated=false` in capabilities and matching
host request. The unset row retains its empty raw Requested; do not fabricate a
literal modern request to satisfy a checklist. The brief's “PtyBackend modern”
means the existing wire value ModernConPty, not a new wire spelling. Include CP-7
complete native/destination UserPrompt and CARD-1020 process-exit evidence. Health,
capability or source checkout alone cannot establish the actual host. If landing
changes SHA, refresh affected qualification at landed SHA before activation.

Release-A activation clarification: the operator-requested deployment is the
**desktop Windows runner first**, using `restart-session-runner.ps1` from the
canonical checkout after lock checks and old/unknown host inventory. Never kill
live sessions to satisfy the migration. Confirm loaded runner SHA and capabilities,
then a freshly owned modern host's log/package and complete prompt receipt, before
server activation and `/api/version` SHA validation. The server2 image is Linux:
its Porta transport is unaffected by the Windows default flip, and no ConPTY
rollout/restart or inbox canary is required there for A. D-3's UnixPty observation
will appear with its next ordinary shared-code runner upgrade; use the documented
rolling procedure if that upgrade is separately commissioned. This narrows the
old generic activation step 4 for this Windows release, without deleting its
follow-up rollout guidance. Keep conservative phone-home limits throughout.

### Cost

All times below are **estimates**, not measurements. Ordinary Code V/R floor is
**59 minutes**, the CP column sum: policy 4, capabilities 5, Windows modern 9,
owned-hosts 7, shadow-pair 4, delivery 8, queue/Grok 12, E2E guard 5, Unix argv 5.
Each includes its isolated build; do not add nine builds again. Count floor is
152 executions, unrelated to minutes. Setup/tool bootstrap/provenance import
allowance is 4 minutes. Authoring and slot queue time are additional, not hidden
inside measured claims.

Mutation floor for **PC-1–PC-73 is 578 minutes**. Exact filters/minimums are the PC
table: each cell expands to `/*/*/Class/Method`. Per cycle budget includes initial
method green, compiling edit, isolated red build/run, restoration, fresh isolated
green build/run and evidence: 31 Pty controls x 6 = 186 minutes; 8 SessionRunner
controls x 8 = 64; 3 PtyHost controls x 6 = 18; 30 server controls x 10 = 300;
1 E2E guard x 10 = 10. Method-only argument expansions are included; no whole-class
mutation run is permitted. Windows and Linux controls use their declared OS lane.
Mutation does not repair source inside a SourceLanding snapshot.

Ordinary + mutation + setup floor = **641 minutes** (4 + 59 + 578). Separate
final-SHA Windows Debug adds **45 minutes** (9 + 7 + 4 + 8 + 12 + 5), giving a
**686-minute** verification/qualification budget before authoring, slot wait,
inherited-red confirmation or a changed-SHA refresh. The prior proposal was 47
ordinary minutes; the added typed-helper, real-queue recovery, parser and full Unix
proof raise the floor by 12 minutes. Savings are zero against that smaller scope:
claiming a faster pass would omit required recipient/recovery evidence. Exact
method PCs avoid repeated full-suite work, but no unmeasured full-suite savings
are claimed. Windows Debug is intentionally counted separately, not treated as
free reuse of Code's receipts.
