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

### Checkpoints

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

### Cost

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
