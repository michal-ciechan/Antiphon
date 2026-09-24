# CARD-0660: Codex on the server2 phone-home runner

Date: 2026-09-24. Plan task: f602d622. Next: **Code**.
Test design is folded into this plan, as required by the commissioned next stage.
Inspected base: b151098235d2415ac92c7d73963aa00b57531963.

## Outcome and boundaries

Run explicitly placed Codex Worker tasks in server2's existing phone-home pool,
using native codex-cli 0.156.1, a persistent runner-only ChatGPT login and the
existing Codex readiness/transcript pipeline. After a real turn and recovery proof,
and after CARD-0659 lands, admit Codex to default runner placement.

This document implements the design and verification stage, not deployment or
sign-in. The account choice remains open; it does not prevent building the feature.
No desktop Codex home is read or copied. No new transport, transcript normalizer,
native-resume contract, model tier, remote-control support, database migration or
capacity policy is required. Codex remains Worker-only. Keep the existing kernel
4.15 launch flag --dangerously-bypass-approvals-and-sandbox.

Primary evidence: [the investigation](../../investigations/2026-09-24-card-0660-codex-on-server2-runner.md)
and [its captured screens](../../investigations/evidence/card-0660/).
The Antiphon board was read during planning: CARD-0662 owns the desktop trust and
sign-in detector defect and was InProgress; that status is not landing evidence.
The [CARD-0659 plan](2026-09-24-card-0659-default-runner-plan.md) is present here;
its default-routing implementation is not present at this inspected base.

## Ground truth

| Card/investigation assumption | What the code or measurement actually establishes | Design consequence |
|---|---|---|
| Codex needs remote transcript support. | SessionRunnerRuntime already advertises codex, starts CodexTranscriptTailer using launch CODEX_HOME, and re-adopts its sidecar. RunnerCodexAdapter uses ISessionRunnerClient. PhoneHomeCommandDispatcher nevertheless refuses the format; its no-tailer comment is stale. | Open admission and test the existing path; do not build a second tailer or transport. |
| The desktop executable can launch on Linux. | server/appsettings.json selects codex.cmd. CodexWindowsLaunchPolicy is deliberately a no-op on Linux. PhoneHomeLaunchPolicy.ProjectExe maps only Grok/Claude and raw allow-list entries. | Project the standard Codex executable to native codex; leave Windows rewriting intact. |
| The runtime image contains Node. | Only session-testing copies Node. runtime-base contains Grok and Claude, and the final/default runtime derives from it. | Install the native platform package in runtime-base and prove both runtime and session-testing targets. |
| A standalone native binary is enough. | Investigation measured the native musl binary plus code-mode host, rg and bwrap resources in a roughly 370 MB vendor tree. Package-root metadata participates in helper discovery. | Preserve the entire platform npm package and its relative layout, not just bin/codex. |
| /state/codex can be initialized at that spelling everywhere. | docker/stack/init-state.sh sees the runner volume at /runner-state; docker-compose.server2-runner.yml mounts it at /state inside session-runner. The script is NOT under docker/session-runner-grok. | Create /runner-state/codex in init-state, which becomes /state/codex in the runner. |
| A per-launch trust override prevents onboarding. | Investigation's linked-worktree experiment found only config.toml trust entries effective; the argv form still showed the modal. Runner worktrees share /work/repos/antiphon/.git. | Seed trust for /work/repos/antiphon in the persistent config file. |
| Existing detectors handle 0.156.1. | CodexDetectors matches older trust wording; CodexStartupReadiness misses the new sign-in screen and sees BlockingUpdate. CARD-0662 now owns both corrections. | Sequence CARD-0662 first; do not duplicate detector edits/tests in CARD-0660. |
| Auth preflight is provider-neutral. | RoutingProviderAuthProbe and dispatcher canonicalization know Claude/Grok only. ProviderSignInRequiredException carries agentKind=Grok, GrokHome and a grok login remedy. AgentTaskService's probe is Grok-specific. | Add Codex probe wiring and a correctly typed refusal while preserving Grok's public response. |
| auth.json presence proves an active subscription. | GrokAuthProbe tests presence without reading the file. It cannot measure expiry, billing or workspace entitlement. Its default File.Exists also cannot reliably distinguish every access failure from absence. | Codex's probe is a presence hint, never an online-auth guarantee; live acceptance remains mandatory. Use an error-aware metadata check for the new Codex probe. |
| Preflight can happen while a task is claimed. | AgentTaskDispatcher probes Grok/Claude before FOR UPDATE, with a five-second budget, and records AuthenticationRequired before cutting a worktree. | Preserve this ordering and timeout/cancellation behavior for Codex. |
| Explicit admission and default placement are the same gate. | CARD-0659 intentionally excludes Codex and includes reroute/rewalk compatibility guards. It is a dependency, not implemented routing to modify blindly at this base. | Preserve a default-placement Codex exclusion through S6 even after explicit kind admission changes; remove it only in S7. |
| State has ample spare space. | Investigation measured /state and /work on one LV at 92% used, 18 GB free. Registry unpacked size is 386,911,210 bytes. | Re-measure before image deployment; allow room for old/new image layers, extraction, build cache and rollouts. No automatic broad prune or rollout deletion. |
| A restart proves continuity. | A live pty-host can be re-adopted after the runner process restarts. Container replacement kills those processes; persisted auth/rollouts alone cannot resume a Codex conversation (resume remains unsupported). | Prove process re-adoption separately from replacement-container login persistence and a fresh task. |

## Decisions

These are implementation decisions under the brief, not defaults for the operator's
account. A later disagreement changes the named decision rather than silently
changing the scope of a Code round.

- **D-1: Use the measured 0.156.1 pin.** Install the complete Linux x64 platform
  npm package under /opt/codex/0.156.1/package, root-owned and not writable by uid
  1654. Link /usr/local/bin/codex to
  /opt/codex/0.156.1/package/vendor/x86_64-unknown-linux-musl/bin/codex.
  Reject an npm shim runtime dependency, an unpinned latest install, a binary-only
  copy and a separate desktop version upgrade. Both image targets inherit this.
- **D-2: Verify npm's published SHA-512 integrity before extraction.** This resolves
  the investigation's checksum question without guessing a GitHub checksum asset.
  The registry metadata was retrieved during Plan (the tarball was not downloaded
  or executed). Pin CODEX_VERSION and CODEX_SHA512 as a reviewed pair; hash the
  downloaded archive before tar runs, remove download/build scratch in the same
  layer, and compare the complete version output to codex-cli 0.156.1. A hash
  mismatch fails the build; never refresh the expected digest automatically.
- **D-3: Persist one runner-specific home.** Create /runner-state/codex owned by
  1654:1654, mode 0700. Compose CODEX_HOME and PhoneHome__CodexHome, and server
  PhoneHomeRunner:ChildCodexHome, all resolve to /state/codex. New typed home
  settings require POSIX absolute paths when enabled. Project CODEX_HOME on every
  Codex launch including boot-wedge relaunch. Reject a desktop home or /tmp home.
- **D-4: Seed config only when absent.** The new non-secret file is mode 0600,
  owner 1654:1654, and contains the settings below. Do not replace an existing
  config or touch auth.json, SQLite stores or sessions. Existing configuration
  missing required trust/auth settings is an operator pre-deploy correction,
  verified before live acceptance. No generic TOML merge engine is required.
  Root-only version checks use an isolated non-/tmp home, removed in that layer.
- **D-5: Subscription login is interactive and operator-owned.** Choose device
  authentication, with file credential storage forced explicitly so the probe's
  target is predictable. Reject copying desktop credentials, API-key login,
  CODEX_ACCESS_TOKEN and unmeasured agent-identity provisioning. Official guidance
  documents device login enablement and file storage; confirm these options on
  the pinned CLI during image qualification rather than assuming current docs
  describe every detail of 0.156.1.
- **D-6: Probe metadata only and preserve tri-state behavior.** CodexAuthProbe
  implements IProviderAuthProbe. Return provider=codex, authMethod=auth_file only
  when a regular auth.json exists, subscriptionType=null, and an injected clock
  timestamp. File/directory-not-found is false; unconfigured or IOException/
  UnauthorizedAccessException is unknown with a bounded error code. Use an
  injectable metadata seam backed by File.GetAttributes (or equivalent), not
  File.Exists's swallowed errors. Never open, parse or log credential contents,
  spawn login status, or claim token validity. Do not change Grok's implementation
  as part of this card. ProviderAuth supports codex case-insensitively; unknown
  providers retain their current refusal.
- **D-7: Keep independent admission backstops.** Runner exe admission is exact
  ordinal codex or /usr/local/bin/codex; reject codex.cmd, ./codex, alternate
  directories and traversal forms. Server maps standard bare codex/codex.cmd/
  codex.exe and standard absolute executable spellings to codex, retaining the
  explicit custom-wrapper refusal and pinned Grok-only behavior. Accept the codex
  transcript format without weakening cwd, backend, capacity or custody checks.
  A definite false auth result yields provider_sign_in_required before StartAsync;
  true/unknown/no probe/disabled probe retain existing admission semantics.
- **D-8: Refuse credential injection at projection.** On runner-bound Codex,
  refuse environment keys OPENAI_API_KEY, CODEX_API_KEY and CODEX_ACCESS_TOKEN
  case-insensitively, including empty values, with phone_home_env_refused. Overwrite
  CODEX_HOME with the runner setting. No values enter errors. Preserve all launch
  arguments, including --no-alt-screen, sandbox bypass, selected model/effort,
  disable_paste_burst and the single developer_instructions argv value. This is
  the subscription-only standard profile; do not add alternate provider/auth
  wrappers. The deployment must also have none of those credential names in its
  inherited environment; verify names only, never dump environment values.
- **D-9: Fail early without a desktop fallback.** Add server
  PhoneHomeRunner:CodexAuthProbeEnabled and runner PhoneHome:CodexAuthProbeEnabled,
  default true. Remote Codex create/retry checks the remote probe, not any local
  store. A false result is HTTP 409 provider_sign_in_required with agentKind=Codex,
  codexHome, runnerId and a device-login remedy. Preserve the existing Grok
  constructor/properties/extensions through an additive provider-aware factory
  or overload. allowUnauthenticatedProvider bypasses create-time admission only;
  dispatcher/runner backstops remain, as for Grok. Read the dispatcher probe
  before claim, with the existing five-second budget; unknown/timeout proceeds,
  caller cancellation propagates. False produces AuthenticationRequired and a
  deduplicated codex-home:{runner}:{home} incident, with durable state before the
  caller notification and zero workspace/start effects.
- **D-10: Stage routing separately.** S2-S5 enable explicit placement only. S7
  requires CARD-0659 publication, its capacity/settlement activation gates, and
  S6 evidence on the deployed revisions. At S7, use the shared admitted-kind
  predicate for effective-kind checks during create, reroute, chain rewalk and
  resume. Keep unsupported-kind guards (OpenCode is the negative control), local
  opt-out, pins, worker-role restrictions, holds and existing capacity behavior.
  Never silently migrate a persisted task to the desktop after an auth failure.
- **D-11: No new sandbox or retention subsystem.** Keep the bypass on kernel
  4.15; do not attempt landlock or introduce a sandbox setting as a prerequisite.
  Record volume growth and an operator retention decision in the runbook. Codex
  rollout deletion remains via its CLI, not filesystem deletion. Disk is an
  observation and deployment gate, not permission to prune unrelated resources.

Pinned artifact (registry metadata, read 2026-09-24):

    https://registry.npmjs.org/@openai/codex/-/codex-0.156.1-linux-x64.tgz
    integrity: sha512-2ePo0wgOcnONKsuzp8vBjOmNY+IdsKaouaDDIdiKq9HOWNuV/GI22OeXft2A/1GaoL71ictO0/pLAsCEnQ6wew==
    CODEX_SHA512=d9e3e8d3080e72738d2acbb3a7cbc18ce98d63e21db0a6a8b9a0c321d88aabd1ce58db95fc6236d8e7977edd80ff519aa0bef589cb4ed3fa4b02c0849d0eb07b

Fresh /state/codex/config.toml, with global keys before the project table:

    check_for_update_on_startup = false
    cli_auth_credentials_store = "file"
    forced_login_method = "chatgpt"

    [projects."/work/repos/antiphon"]
    trust_level = "trusted"

The official [authentication guide](https://learn.chatgpt.com/docs/auth) documents
device-code enablement in personal security settings or workspace permissions and
file storage under CODEX_HOME. The [configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference)
documents forced_login_method and the update-check switch. These sources support
the chosen configuration; package qualification and the real turn establish the
pinned version's behavior. An absent update nag when no update is available is
not proof that suppression works under an available update.

## Slices and bounded Code rounds

Read docs/testing-and-build.md and the relevant AGENTS owner before each round.
Use a fresh task branch from the preceding landed revision; never reset another
task's branch. Commit and push each test-first and implementation slice with its
actual verification state. Run git through a bounded process (30 seconds for
local commands, 120 seconds for fetch/push), own and await every process, and
report timeouts rather than leaving children running.

| Slice / round | Deliverable and files | Tests / completion boundary | Authoring budget |
|---|---|---|---:|
| S1 dependency, external to this card | Land CARD-0662's CodexDetectors.cs / CodexStartupReadiness.cs changes and its old/new wording fixtures. Record its commit and green report before this card's Code begins. | Consume that card's tests; no duplicate detector implementation or checkpoint here. | External |
| S2 / Round A | docker/session-runner-grok/Dockerfile: verified whole native package in runtime-base. docker/stack/init-state.sh: directory/config seed at the correct mount. docker-compose.server2-runner.yml: both home env values. Add scripts/verify-card0660-codex-image.ps1 and docker/session-runner-grok/verify-codex-image.sh for isolated qualification. | New tests/Antiphon.Tests/Infrastructure/CodexRunnerImageContractTests.cs; CP-1/2. Script must grade actual image/state behavior for Q-1/2, not just print source matches. | 65 min + 3 min checks |
| S3 / Round B | New src/Antiphon.SessionRunner/CodexAuthProbe.cs; PhoneHomeSettings.cs; RoutingProviderAuthProbe.cs; Program.cs; PhoneHomeCommandDispatcher.cs. Add strict exe/format admission, probe/backstop and update stale comments. | New tests/Antiphon.SessionRunner.Tests/CodexAuthProbeTests.cs; extend PhoneHomeCommandDispatcherTests.cs and add CodexProviderAuthRoutingTests.cs for real composition routing. CP-3/4. | 65 min + 3 min checks |
| S4a / Round C | server/Application/Settings/PhoneHomeRunnerSettings.cs; Services/PhoneHomeLaunchPolicy.cs; AgentTaskService.cs; Exceptions/ProviderSignInRequiredException.cs. Codex create/retry preflight and projection. Add settings to the existing PhoneHomeRunner configuration documentation; preserve local definitions. | New tests/Antiphon.Tests/Application/CodexPhoneHomeProjectionTests.cs and CodexPhoneHomeCreateTests.cs; update the existing Codex-refusal cases in PhoneHomeTaskCreateTests.cs / PhoneHomeTaskRoutingTests.cs; extend PhoneHomeRunnerSettingsValidatorTests.cs. CP-5/6. | 70 min + 3 min checks |
| S4b + S5 / Round D | AgentTaskDispatcher.cs: pre-claim Codex probe, durable auth failure, launch/relaunch projection. Touch AgentControlService.cs only if named-start tests expose a bypass of its existing shared projection. Docs: docs/agent-credentials.md, docs/agent-kinds.md, docs/docker-stack.md; operator evidence template/runbook at docs/operations/card-0660-codex-server2.md (new). | New tests/Antiphon.Tests/Application/RunnerCodexCredentialProbeDispatcherTests.cs; extend PhoneHomeTaskDispatchProjectionTests.cs for Codex normal and boot-wedge launches. CP-7/8/9. | 80 min + 3 min checks |
| S6 / Qualification, then operator proof | Build both image targets on the local isolated Docker lane; after review/land, commission server2 deployment using deploy-parent through scripts/c590-real.ps1 and desktop activation through the canonical restart. Record image/source SHAs. Operator selects account and signs in. | Q-1/2 then live acceptance L-1 below. No production restart, login or real provider use as a unit-test fixture. | Image qualification <=45 min; live evidence <=30 min, excluding operator wait |
| S7 / Round E, after S6 and CARD-0659 | Modify CARD-0659's actual landed default selector and reroute/rewalk guards in AgentTaskService.cs / AgentTaskDispatcher.cs and its policy helper if extracted. Do not invent a second selector. Update relevant default-runner docs and activation record. | Extend CARD-0659 DefaultRunnerCreateTests.cs / DefaultRunnerRerouteTests.cs; new tests/Antiphon.Tests/Application/CodexDefaultRunnerTests.cs. CP-12/13; live acceptance L-2. | 45 min + 3 min checks |

CARD-0659 can land while this work is in flight. Reconcile S4a with its current
selector before enabling explicit admission: a change to IsAdmittedKind must not
accidentally drop its separate Codex default exclusion. Do not modify S7 code in
an earlier round. A bounded round that reaches its limit commits its finished
slice and reports the next exact slice; it does not claim the card is complete.

## Operator questions and deployment acceptance

**O-1 (open, before login):** use the operator's existing ChatGPT account, sharing
its usage allowance with desktop Codex, or a separate authorized seat? No default
is selected here, and this plan authorizes no seat purchase. Record the chosen
account category/workspace and operator confirmation, never tokens or auth-file
contents. Confirm device-code login is enabled for that account/workspace.

**O-2 (deployment scheduling):** commission the concrete image/server revisions
for server2 redeploy and the desktop restart after Code/Review. This Plan task
does not perform those actions. Verify source-root HEAD and the activated
/api/version SHA after the restart, then server2 dispatchEligible and image SHA.
An old runner may reject the newly admitted exe/provider; deploy the runner
before exposing explicit Codex launches, and retain default exclusion through S6.

At deployment, re-measure df for the shared /state and /work LV and Docker's own
usage. Compare free space with the candidate image/build-cache peak and retain a
rollback image; the investigation's 18 GB is historical, not a reservation. If
headroom is inadequate, record the measurement and request an explicit cleanup
or storage decision. Do not delete state or broadly prune Docker to make it fit.

After those gates, the **operator**, in their own interactive terminal, runs:

    docker exec -it -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex antiphon-runner-session-runner-1 codex login --device-auth

Do not capture the login terminal or device code in task evidence. After login,
record only regular-file existence/owner/mode of /state/codex/auth.json, the
provider-auth DTO, and the image/config checks. Never copy or open the credential.

### L-1: explicit runner task, receipt and recovery proof

Use a commissioned disposable Worker Worktree task with Kind Codex and explicit
Runner server2, an approved tier, a per-run nonce, and a tiny artifact/report
request. Include a harmless developer-instruction nonce so the reply demonstrates
that the unchanged instruction argv reached Codex. Do not use an Orchestrator
role, SourceLanding or a standing user session for this proof.

Capture task/session IDs, deployed SHAs, image digest/version, RunnerId,
RunnerStoreId, RunnerCwd, projected exe/home (names and non-secret paths only),
ready classification from the runner-rendered screen, and these acceptance rows:

1. The first launch uses the linked mirror and has no blocking trust/login/update
   modal. No input is sent before positive readiness; bypass flag is present.
2. A complete matching UserPrompt after the attempt baseline comes over the
   phone-home Transcript operation. Record row sequence/nonce, not merely Running
   or a screenshot. The rollout belongs to this exact Linux cwd under
   /state/codex/sessions and is claimed by this session.
3. TurnEnd follows that receipt; the task produces the expected artifact and final
   report, settles, and releases its slot. The artifact must be present in the
   synchronized server-side task branch, covering CARD-0657's settlement boundary.
4. With a second isolated mirror/nonce, prove each transcript binds only its own
   cwd/input. If capacity permits run the two together; otherwise record why and
   keep the cross-binding acceptance outstanding rather than asserting it passed.
5. In an explicitly coordinated recovery window, restart only the runner process
   while the disposable pty-host survives. Confirm re-adoption of the same host/
   session/rollout and a second submitted nonce with UserPrompt then TurnEnd.
   No automatic working-session kill is introduced for this test. If supervision
   cannot restart the process independently, this row remains pending until an
   isolated server2 qualification container can reproduce that lifecycle.
6. After a coordinated container replacement with no live tasks, confirm auth
   presence and config survive on the volume, then run a fresh Codex task. This
   proves persistence; it does not prove native conversation resume.

Write sanitized results and outstanding rows to
docs/operations/card-0660-codex-server2.md. Do not declare S6 passed from only
version, login, registration, health or screen output. A revoked/expired-but-present
auth file may still require operator re-login; keep that limitation in the runbook.

### L-2: default placement and rollback

After S7 activation, create a fresh compatible Codex Worker task without runnerId;
prove server2 placement, receipt and completion. A separate explicit Local create
must persist desktop placement; it need not spend a model turn. Verify an auth
refusal on a disposable signed-out qualification home keeps the selected runner
and starts no process; never remove production auth to induce it. Leave all
CARD-0659 project/role, capacity, Required-pin and settlement gates intact.

If the live proof fails, keep/reinstate Codex's default exclusion and retain
evidence. Revert the affected admission/routing commit or stop assigning new
Codex work while its runner remains unavailable; do not silently reroute existing
tasks. Reverting the image preserves runner-state and its auth. No credential
deletion or login on behalf of the operator is part of rollback.

## Verification design

Ordinary Code follows the checkpoint table as a closed list, with about three
minutes verification per authoring round. Full-suite/native/image/live work is
not hidden in that estimate. Q-1/2 are explicit qualification rounds; L-1/2 are
operator acceptance, not automatic builds/tests and not executed by this Plan.

### Coverage and falsifiable assertions

| ID | Class / artifact and minimum new executions | Behavioral oracle and expected baseline red |
|---|---|---|
| V-1 | CodexRunnerImageContractTests (3 methods) | Pin_integrity_and_install_are_in_runtime_base checks the actual Dockerfile install/verification ordering and full package path; State_initializer_seeds_runner_mount_without_overwrite checks the executable script contract; Compose_home_and_probe_home_agree parses the runner service env. All three fail on absent Codex packaging/state at baseline. Text contracts alone are insufficient: Q-1/2 execute them. |
| V-2 | CodexAuthProbeTests (6 methods) | Present_file_returns_presence_without_reading (unparseable sentinel bytes, DTO/log cannot contain them); Missing_file_is_signed_out; Missing_directory_is_signed_out; Inaccessible_store_is_unknown (IO and unauthorized cases); Unconfigured_store_is_unknown; Other_provider_is_refused. Use injected clock and metadata I/O; test actual temp files for presence. Removing/changing the existence decision must fail a named assertion. |
| V-3 | PhoneHomeCommandDispatcherTests (at least 5 new methods) + CodexProviderAuthRoutingTests (1) | Exact image executable forms start the recording runtime with codex format; lookalike/traversal paths never start; false auth returns 409 before runtime; true/unknown/null/disabled admit; ProviderAuth canonicalizes Codex and refuses a different unknown provider. Composition test exercises RoutingProviderAuthProbe with real probes and isolated homes, detecting wrong provider wiring. Baseline admission/provider paths refuse Codex. Preserve old Grok/Claude cases; replace the old codex-format negative with opencode. |
| V-4 | CodexPhoneHomeProjectionTests (4) | Standard_exes_project_with_mirror_and_home; Credential_names_are_refused_without_values; Local_and_pinned_grok_contracts_are_unchanged; Arguments_survive_projection_byte_for_byte. Include Windows absolute paths, mixed-case env keys, empty credential values, custom-wrapper refusal, exact developer-instructions payload, model/effort/paste/bypass flags, and cold/relaunch inputs. Baseline returns wrapper/kind refusal or omits CODEX_HOME. Settings validator adds valid/invalid ChildCodexHome cases. |
| V-5 | CodexPhoneHomeCreateTests (5) | Signed_out_remote_create_refuses_with_codex_problem_details; Present_unknown_and_unavailable_probe_admit; Explicit_override_only_bypasses_create_probe; Local_codex_never_reads_remote_or_desktop_auth; Retry_checks_the_selected_runner. Reload DB to assert no created task on refusal, assert provider name/home/remedy and no secret echo; inject a conflicting fake desktop-store state. Probe timeout/caller cancellation assertions use controlled cancellation. Baseline refuses kind or does not perform Codex probe. |
| V-6 | RunnerCodexCredentialProbeDispatcherTests (6) | Signed_out_fails_before_workspace_or_launch; Failure_is_durable_before_notification_and_incident_deduplicated; True_unknown_and_disabled_probe_proceed; Slow_probe_does_not_hold_claim_lock; Probe_budget_expiry_proceeds_but_caller_cancel_propagates; Other_kinds_and_local_codex_are_not_probed. Use actual service/DB boundaries and controlled provider replies, count workspace/start calls and incidents, inspect AuthenticationRequired and remedy. Baseline never performs Codex auth failure. Model/pin changes must not apply a stale result to a different provider. |
| V-7 | PhoneHomeTaskDispatchProjectionTests (2 new Codex methods) | Runner_bound_codex_launch_keeps_arguments_home_and_transcript; Runner_bound_codex_boot_wedge_relaunch_keeps_binding_and_projection. Drive the real dispatcher and fake phone-home host, capture RunnerLaunchRequest and persistent session owner/cwd. Codex must retain CODEX_HOME, codex transcript format, argv bytes, PtyHost and MemoryLimitMb=0 on both paths; no local client starts. Baseline kind/exe projection fails. |
| V-8 | CodexDefaultRunnerTests (4 methods) | Eligible_codex_worker_uses_default_after_activation; Explicit_local_and_unsupported_shapes_remain_local_or_refused; Codex_reroute_and_rewalk_keep_runner_while_opencode_is_refused; Auth_refusal_never_changes_selected_host. Drive CARD-0659's actual create/dispatch/reroute services and reload saved state. Baseline S6 still excludes Codex from default routing. Include Required-pin candidate order, held model, unavailable runner fallback and Worker-only behavior in the cases; do not broaden role admission. |
| R-1 | Existing PhoneHomeCommandDispatcherTests, GrokAuthProbeTests, ClaudeAuthProbeTests | Grok/Claude probes, strict exe/cwd/custody/capacity admission continue to pass; a new provider does not replace existing routing. |
| R-2 | Existing PhoneHomeTaskCreateTests, PhoneHomeTaskRoutingTests, PhoneHomeRunnerSettingsValidatorTests, PhoneHomeStandingLaunchTests | Grok refusal extensions/override, named/pinned limits, unrelated kinds and settings behavior stay compatible. Intentionally update Codex-only refusal expectations. |
| R-3 | Existing RunnerGrokCredentialProbeDispatcherTests, ClaudeCredentialProbeDispatcherTests, PhoneHomeTaskDispatchProjectionTests | No RPC under claim lock, no preflight worktree, same Grok/Claude launch/relaunch behavior. |
| R-4 | Antiphon.Tests Unit lane at S4b+S5 | Shared exception/settings/projection changes compile with and preserve ordinary unit contracts. No full namespace or full assembly run. |
| R-5 | CARD-0659 DefaultRunnerCreateTests and DefaultRunnerRerouteTests | Update Codex-only negatives to OpenCode, retaining incompatible-kind refusal, local sentinel, eligibility, pins and no host migration assertions. Confirm exact class names at its landed revision before dispatching Round E; amend this table if renamed. |
| V-9 | verify-card0660-codex-image.ps1 / verify-codex-image.sh | Q-1/2 build actual image targets, run native version as uid 1654 with a non-/tmp isolated home, verify helpers/metadata and non-writability, no auth baked, and execute init-state twice on throwaway volumes. Assert config bytes/owner/mode/trust and preservation of a sentinel existing config/auth. Test a linked git worktree with the seeded root. No external provider requests. |
| V-10 | L-1/L-2 evidence document | Linux VT readiness, matching phone-home UserPrompt, TurnEnd, artifact/report settlement, two-cwd binding, process re-adoption, replacement persistence and final default routing. This is the paid/operator gate. |

For new types/settings absent on the base, add the smallest compiling seam and
tests in the red commit; an implementation stub may deliberately return the old
unsupported/unknown behavior. A build failure is not red. Every new success or
refusal branch must be shown to fail with the relevant production decision absent
or reversed. The primary baseline red for each round is listed above; later
Mutation controls are method-scoped and are not extra ordinary Code suite runs.
Use per-test schemas and the established loopback/fake-runner harnesses; never
boot a test host against production port 17204. Process-spawning classes carry
their assembly-local ParallelLimiter<ProcessSpawnLimit>. No real provider login
or normal user CODEX_HOME is used by automated tests.

### Image qualification contract

The new verifier takes -Target runtime or session-testing, -Image (unique tag),
-SourceRevision (full committed SHA) and -ResultsRoot (fresh directory). It owns
one foreground docker build for that target, then runs probes in that image with
phone-home disabled, no published standard ports, no host Docker socket and only
throwaway volumes. Override session-testing's entrypoint for these probes; do
not boot its credential-dependent DinD entrypoint just to ask Codex's version.
Run the production init-state script read-only from the checkout into the probe
container, mounted as it is in state-init, then observe the same volume at /state.

Each target emits eight graded rows: (1) exact native version, (2) whole helper/
metadata layout, (3) uid 1654 can execute but cannot modify the install, (4) no
baked auth, (5) fresh home owner/mode and config globals, (6) root trust covers a
linked mirror with no trust modal, (7) second init preserves existing config/auth
bytes, (8) cli_auth_credentials_store and forced_login_method accepted by the
pinned CLI. Use a network-disabled, signed-out TUI for config parsing and a
separately isolated loopback stub with RealCliStubEnv.ForCodex for trust/readiness;
that test config may allow its dummy API key and must not touch the deployment
home. Record stub receipt of a nonce if a turn is used; never infer redirection
from CLI output. Failure to validate the config/native helper layout is a
qualification failure, not permission to omit the feature or copy only a binary.
The harness removes only its owned containers/volumes and records image IDs.

### Cost and execution rules

CP-1/2, CP-3/4, CP-5/6, CP-7/8/9, and CP-12/13 each total **3 minutes** estimated
ordinary verification, including their isolated builds: **15 minutes** total.
Authoring budgets are 65, 65, 70, 80 and 45 minutes respectively; no ordinary Code
round exceeds about 90 minutes. These are estimates, not timeout increases or
permission to skip a checkpoint. Cold dependencies or the first DB start can cost
more; report actual time and split a named group if needed under the owner's rules.

Q-1/2 (CP-10/11) add **40 minutes** estimated image qualification, separately scheduled;
their download/publish/build cost is not a three-minute unit check. Total listed
automated verification floor is **55 minutes**. L-1/L-2 add roughly 30 minutes of
active operator acceptance plus unbounded decision/login/deploy waiting. No
full-suite run is commissioned for this plan-only task or these bounded rounds.

For each TUnit row use scripts/run-checkpoint.ps1 with the exact Filter, OutputPath,
Min and a fresh ResultsRoot below .antiphon/c660-checkpoints; -Expect is a single
comma-separated list of all named classes. For example:

    pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c660-image/ -Filter '/*/*/CodexRunnerImageContractTests*/*' -MinExecuted 3 -Expect CodexRunnerImageContractTests -ResultsRoot .antiphon/c660-checkpoints/image-red

Red rows intentionally return the assertion-failure exit code; require the named
behavior failure and nonzero TRX counts. A zero-test run, compilation error or
fixture failure does not satisfy red. Green rows require all named classes,
zero failed/skipped, and the planned new method roster; Min is a conservative
executed-results floor, not a count of assertions or estimated minutes. Emit the
owner's CHECKPOINT CP-n commit/build/filter/executed/passed/failed/skipped/trx
line, including reruns. Existing red is confirmed with the exact failing class
or method on its base, not a full suite; report that diagnostic run and reason.
Commit before builds; freeze source during runs. Inventory and remove only this
producer's bin-c660-* directories after all dependent rows finish, verifying
their resolved paths are in this worktree. Do not delete another producer's output.

### Checkpoints

This is the closed automated list across the named rounds. Q-1/2 are CP-10/11 and are deferred
until the qualification round, not silently skipped by a single early Code task.
R-4 reuses CP-8 because both have the identical After revision. Pipes below are
escaped for Markdown; the actual TUnit filter uses ordinary | characters, with
trailing class wildcards as required by CARD-0403.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2-red | `tests/Antiphon.Tests -> bin-c660-image/` | image-red | `/*/*/CodexRunnerImageContractTests*/*` | V-1 red | All 3 methods execute; packaging/state assertions fail, no build/fixture errors | 3 | 1.5 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c660-image/` | image-green | `/*/*/(CodexRunnerImageContractTests*)\|(DindRunnerContractTests*)/*` | V-1 | All listed, 0 failed/skipped | 3 | 1.5 |
| CP-3 | S3-red | `tests/Antiphon.SessionRunner.Tests -> bin-c660-runner/` | runner-red | `/*/*/(CodexAuthProbeTests*)\|(CodexProviderAuthRoutingTests*)\|(PhoneHomeCommandDispatcherTests*)/*` | V-2,V-3 red | Named Codex admission/probe assertions fail; all listed execute | 12 | 1.5 |
| CP-4 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c660-runner/` | runner-green | `/*/*/(CodexAuthProbeTests*)\|(CodexProviderAuthRoutingTests*)\|(PhoneHomeCommandDispatcherTests*)\|(GrokAuthProbeTests*)\|(ClaudeAuthProbeTests*)/*` | V-2,V-3,R-1 | All listed, 0 failed/skipped | 12 | 1.5 |
| CP-5 | S4a-red | `tests/Antiphon.Tests -> bin-c660-create/` | create-red | `/*/*/(CodexPhoneHomeProjectionTests*)\|(CodexPhoneHomeCreateTests*)/*` | V-4,V-5 red | Named Codex projection/create assertions fail; all 9 methods execute | 9 | 1.5 |
| CP-6 | S4a | `tests/Antiphon.Tests -> bin-c660-create/` | create-green | `/*/*/(CodexPhoneHomeProjectionTests*)\|(CodexPhoneHomeCreateTests*)\|(PhoneHomeTaskCreateTests*)\|(PhoneHomeTaskRoutingTests*)\|(PhoneHomeRunnerSettingsValidatorTests*)\|(PhoneHomeStandingLaunchTests*)/*` | V-4,V-5,R-2 | All listed, 0 failed/skipped | 9 | 1.5 |
| CP-7 | S4b-red | `tests/Antiphon.Tests -> bin-c660-dispatch/` | dispatch-red | `/*/*/(RunnerCodexCredentialProbeDispatcherTests*)\|(PhoneHomeTaskDispatchProjectionTests*)/*` | V-6,V-7 red | Named Codex auth/launch assertions fail; all listed execute | 8 | 1 |
| CP-8 | S4b+S5 | `tests/Antiphon.Tests -> bin-c660-dispatch/` | dispatch-green | `/*/*/(RunnerCodexCredentialProbeDispatcherTests*)\|(PhoneHomeTaskDispatchProjectionTests*)\|(RunnerGrokCredentialProbeDispatcherTests*)\|(ClaudeCredentialProbeDispatcherTests*)/*` | V-6,V-7,R-3 | All listed, 0 failed/skipped | 8 | 1.5 |
| CP-9 | S4b+S5 | `CP-8, --no-build` | unit-final | `/*/*/*/*[Category=Unit]` | R-4 | >=1 executed, 0 failed; verify roster | 1 | 0.5 |
| CP-10 | S2-S5 committed, qualification | `runtime image built by command` | runtime-image | `pwsh -NoProfile -File scripts/verify-card0660-codex-image.ps1 -Target runtime -Image antiphon-c660/runtime:<sha12> -SourceRevision <full-sha> -ResultsRoot .antiphon/c660-image/runtime` | V-9 runtime (Q-1) | Build success and 8/8 graded rows, no external provider access | n/a | 20 |
| CP-11 | S2-S5 committed, qualification | `session-testing image built by command` | testing-image | `pwsh -NoProfile -File scripts/verify-card0660-codex-image.ps1 -Target session-testing -Image antiphon-c660/testing:<sha12> -SourceRevision <full-sha> -ResultsRoot .antiphon/c660-image/testing` | V-9 session-testing (Q-2) | Build success and 8/8 graded rows, no external provider access | n/a | 20 |
| CP-12 | S7-red; S6 and CARD-0659 evidenced | `tests/Antiphon.Tests -> bin-c660-default/` | default-red | `/*/*/CodexDefaultRunnerTests*/*` | V-8 red | All 4 methods execute; Codex placement/rewalk assertions fail | 4 | 1.5 |
| CP-13 | S7 | `tests/Antiphon.Tests -> bin-c660-default/` | default-green | `/*/*/(CodexDefaultRunnerTests*)\|(DefaultRunnerCreateTests*)\|(DefaultRunnerRerouteTests*)/*` | V-8,R-5 | All listed, 0 failed/skipped | 4 | 1.5 |
