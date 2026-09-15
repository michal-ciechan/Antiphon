# CARD-0496: machine-specific messaging operations

Date: 2026-09-15

Stage: Plan, with verification design folded into this dispatch

Source inspected: `b97abfd83819e2d1b705b4ed0527cfa8953b797f`

Card: CARD-0496, read through `pwsh -NoProfile -File scripts/card.ps1 get CARD-0496`

## Outcome and scope

Correct the current operator instructions so a desktop operator follows its local
Redpanda and separately managed Slack gateway, and a remote operator explicitly
chooses a remote target. Keep the existing AppHost resource graph and broker
configuration behavior. The remote deployment script must have no implicit host.

The card supplies the incident investigation and operator-confirmed desktop topology.
This plan checks those claims against tracked source; it is not a live deployment
audit. A read-only lookup of `Antiphon MikeysBot Slack Gateway` returned no matching
Scheduled Task in the planning execution environment. No tracked MikeysBot launcher
was found. Document that sidecar as the setup recorded by CARD-0496 on 2026-09-12,
not as something installed on every clone or verified running by this dispatch.

No changes to gateway ownership, broker addresses in tracked configuration, secrets,
Scheduled Tasks, remote machines, or running services are required to implement this
card. Existing dated plans/investigations and generated `docs/cards/` are not edit targets.

## Ground-truth table

Line numbers below refer to the inspected commit and are navigation aids.

| Card assumption or existing instruction | What the source/evidence actually says | Consequence |
|---|---|---|
| This desktop's live broker is `server2:19092`. | `Antiphon.AppHost/Program.cs:48-59` says that in comments, but reads an optional configuration value; lines 80-81 forward a nonblank trimmed value only to the server. `server/appsettings.json:280` defaults to `localhost:19092`. | Correct prose; retain the existing opt-in implementation. Local versus remote is a deployment choice, not a live/test switch. |
| AppHost should have brought the Slack gateway back after restart. | `Program.cs:34-46` registers only `Antiphon.Messaging.FakeGateway` on 17208. No real Slack/Telegram service is registered. | Preserve that boundary. Document independent sidecar ownership and diagnosis. |
| Selecting a live broker guarantees fake inbound cannot reach the server. | FakeGateway independently defaults to `localhost:19092` (`src/Antiphon.Messaging.FakeGateway/Program.cs:26`). Both it and the server use the local broker by default. The override is not forwarded to FakeGateway, but an override can itself be localhost. `Program.cs:118` also overstates isolation in a log message. | Say the two are separated only when their effective brokers differ. A local real gateway makes local traffic live; removing an override is not a safe test-isolation procedure. |
| The documented desktop Slack path uses local Redpanda and `MikeysBotSlackGateway`. | CARD-0496 records this and the Scheduled Task name `Antiphon MikeysBot Slack Gateway`. This execution environment has no matching task; the repository has no sidecar installer. | Attribute the topology and task name to the card; give read-only discovery steps and an absent-task branch, not invented paths, ports, or installation instructions. Do not claim this Slack sidecar implements Telegram. |
| The deployment helper is just a hard-coded SSH command. | `scripts/deploy-am-service.ps1:263-387` has injected runners, Dockerfile-derived archives, Compose checks, consumer/broker identity checks, retained backups, readiness verification, and ShouldProcess. SSH and SCP use `mc@server2`; confirmation repeats that literal. Several helper commands intentionally use `/home/mc/antiphon-messaging`. | Parameterize the SSH destination, preserve the fixed supported Compose layout and all existing checks. A comment-only rename would leave the unsafe implicit destination executable. |
| The August 21 Family/school_revision deployment is current on this desktop. | `docs/telegram-bot-ops.md:29-56`, `docs/messaging-standalone.md:18-33`, and `docs/slack-bot-ops.md:126-164` promote dated remote observations into general instructions. | Replace current-host assertions with local and optional remote procedures; link dated evidence for history. |
| Only the seven files in the card need attention. | `src/Antiphon.Messaging.Service/README.md:58` also names the remote host in a current example. `docker-compose.dev.yml:35-39` says the real gateway never runs locally and the broker is purely offline. `.claude/skills/telegram-e2e-smoke/SKILL.md` hard-codes the remote host in its description, pipeline, diagnosis, and deploy instructions. | Include these three documentation corrections and the deploy script's existing test file. Search hidden instruction directories too. |
| Every occurrence of the old hostname proves the messaging assumption. | Windmill worker/scheduler references also occur in `docs/testing-and-build.md`, `scripts/windmill/`, cleanup scripts, and nightly scripts. They describe a separate system; neither code search nor this card establishes its current availability. | Classify residual matches. Do not delete or reroute the scheduler as a side effect of a messaging correction. |

## Decisions

These are implementation decisions within the card's authorized alternatives. None
depends on an unconfirmed remote machine existing; no operator decision is pending.

### D-1: distinguish default configuration from live gateway provisioning

Use this common statement across the current owners: the server defaults to local
Redpanda at `localhost:19092`; live gateways are provisioned per machine; any broker
override belongs in AppHost configuration (user-secrets for `Antiphon.AppHost`, id
`aspire-antiphon-apphost`, or the gitignored development overlay). AppHost forwards
the override only to the server. Do not assume a particular remote hostname.

Use `"<confirmed-broker-host>:<port>"` only as an explicitly replace-before-use
example for an intentional override. The documented desktop local path needs no
remote override. Do not remove or change an operator's configured override as part
of this work. Reject replacing every old hostname with localhost: that would turn
legitimate optional remote deployments into incorrect local instructions.

### D-2: keep real gateways outside AppHost; describe actual isolation

Retain the FakeGateway resource and both existing configuration paths. Correct only
comments and the misleading broker-selection log text in `Program.cs`; the log
should report the configured server broker and that FakeGateway configuration is
unchanged, without asserting they cannot communicate.

Put the desktop sidecar procedure in `docs/slack-bot-ops.md` and link to it from
bootstrap, Telegram ops, and the standalone overview. AppHost health/restart is
not sidecar health/restart. Synthetic gateway tests require a broker with no real
gateway attached; changing a hostname or removing a user-secret does not prove
that condition. Reject adding the real gateway to AppHost, creating a new task
installer, or moving live traffic as part of a documentation fix.

### D-3: require an explicit SSH destination for the existing remote layout

Add `-SshTarget` to `scripts/deploy-am-service.ps1`, `Invoke-AmServiceDeployment`,
and its core. There is no default host, no environment fallback, and no DNS-based
auto-selection. Use a non-prompting runtime requirement rather than a mandatory
top-level parameter: dot-sourcing the file must still load helper functions without
asking for a target or performing any work.

Before any HTTP, SSH, SCP, tar/archive, or deployment work, reject a missing, empty,
whitespace-only, or malformed target with a short configuration error and nonzero
CLI exit. Require one `user@host` value: user matches
`[A-Za-z0-9_][A-Za-z0-9_.-]*`; host consists of nonempty dot-separated labels with
alphanumeric ends and alphanumeric/hyphen interiors. An SSH alias, DNS hostname,
or IPv4 address in that shape is supported. Reject option-like arguments,
whitespace/control characters, path/port suffixes, and extra separators. Do not
silently trim or reinterpret malformed input. IPv6 and custom SSH arguments are
outside this narrow interface; an explicitly configured SSH alias can be used.

The validated destination must reach **every SSH call**, the SCP destination, the
preflight target display, and the ShouldProcess target. Preserve the exact target
between preview and deploy. Keep calls as executable plus arguments; do not build
a new local shell command or use `Invoke-Expression`.

Keep `/home/mc/antiphon-messaging`, `build/src`, `messaging-service`, `am-service`,
ports, and the Dockerfile/Compose contract fixed. Clearly label the helper as an
optional remote deployment for that existing layout; it is not a desktop-sidecar
tool or a general Compose deployer. A different filesystem layout remains
unsupported. Reject parameterizing every path/service: it broadens the replacement
and backup surface without helping this card. Reject retaining a default named
remote target: its present availability is not established. Reject declaring all
remote deployments obsolete: the evidence is machine-specific.

Default and `-WhatIf` remain read-only preflights **after** explicit target selection;
`-Deploy` plus ShouldProcess remains the write gate. Keep CARD-0270/CARD-0410 checks,
safe projections, secret redaction, backup evidence, and separate traffic acceptance.
No live remote preflight or deploy is necessary for Code or Review.

### D-4: preserve dated evidence, remove current-host assumptions

The three channel operations docs should describe the desktop path first and an
optional remote instance second. Remove the old current-host table/assertions and
copyable SSH/user-secret examples. Preserve useful per-bot isolation and optional
multi-adapter guidance, making restart impact conditional on adapters actually
sharing an instance. Link existing dated plans for historical deployments instead
of editing them:

- [Slack deployment](2026-08-20-card-0107-slack-channel-plan.md)
- [Slack DMs](2026-08-21-card-0119-slack-dm-plan.md)
- [Broker opt-in](2026-08-25-card-0185-apphost-broker-opt-in-plan.md)
- [Remote deploy helper](2026-08-31-card-0270-am-service-remote-deploy-plan.md)
- [Gateway consumer identity](2026-09-06-card-0410-gateway-consumer-group-plan.md)

Replace bootstrap's incidental Windmill hostname with the operator-managed
Windmill job identity and a link to the testing/build owner, retaining its
no-local-Scheduled-Task rule. Leave unrelated Windmill worker configuration alone.

## Implementation slices

Commit and push each completed slice with its actual verification result. S1 and S2
must both be present for final Review; the remote examples in S1 describe S2's CLI.

### S1: correct the operational topology and examples

| File | Exact edit scope | Verification |
|---|---|---|
| `Antiphon.AppHost/Program.cs` | Rewrite the CARD-0185 comment; qualify the fake/live separation; correct the override-branch log sentence per D-2. Preserve all resource registration, environment forwarding, defaults, conditions, and log arguments. | V-1, V-3, R-2 |
| `docs/agent-kinds.md` | Preserved Gotcha #13: replace the machine-specific secret command with the D-1 configuration contract and link to channel ops. The preserved section is current operator guidance, not an immutable investigation. | V-1, V-2 |
| `docs/bootstrap.md` | Scenario (a)'s Windmill sentence, step 4's broker explanation, step 6's optional sidecar link, and the `AntiphonMessaging:BootstrapServers` secrets-table row. Distinguish the sidecar from tasks registered by `install-autostart.ps1`. Do not imply fresh clones have real chat configured. | V-1, V-2, V-4 |
| `docs/telegram-bot-ops.md` | Replace current-host claims in the deployment model; describe local default and explicit remote override; rename the deploy heading to `Deploying an optional remote messaging service`; give `-SshTarget '<user>@<confirmed-host>'` examples. Scope legacy layout and human traffic checks to the selected remote instance. Replace the remove-secret-to-smoke recipe and qualify the fake smoke prerequisites per D-2. Link the local Slack sidecar without assigning it Telegram ownership. | V-1 through V-4 |
| `docs/messaging-standalone.md` | Replace the live remote census with machine-specific local/remote models and dated-history links. Retain independent per-instance state and optional multi-provider guidance. Explain that the fake gateway itself has no real egress, but sharing its broker with a real gateway defeats test isolation. | V-1 through V-4 |
| `docs/slack-bot-ops.md` | Add `Desktop Slack sidecar` before remote deployment. Name `MikeysBotSlackGateway`, local Redpanda, and the card-recorded Scheduled Task. Replace host-specific deploy/config commands; update the Telegram deploy anchor; make shared Telegram restart impact conditional; preserve duplicate Socket Mode connection guidance without a presumed remote host. Reframe the dated DM fix as app configuration, not current deployment inventory. | V-1, V-2, V-4 |
| `src/Antiphon.Messaging.Service/README.md` | Make the school_revision heading a portable Compose example and link the machine-specific ops guide. No change to example configuration values or service defaults. | V-1, V-2 |
| `docker-compose.dev.yml` | Comments only: remove the claim that the real gateway never runs locally and this broker is necessarily offline. Explain the fresh-clone dev purpose and machine-specific live sidecars. | V-2, V-3 |
| `.claude/skills/telegram-e2e-smoke/SKILL.md` | Correct the description, pipeline sketch, verification step 1, and gateway restart gotcha to use the machine's configured gateway/broker. Replace the hard-coded SSH diagnosis and hand-written remote tar-sync with links to current channel ops and the explicit-target helper. Preserve the authorized real-smoke assertions and designated test group; editing these instructions does not invoke the skill or send a message. | V-1, V-2 |

The sidecar section should show safe read-only discovery with
`Get-ScheduledTask -TaskName 'Antiphon MikeysBot Slack Gateway'` projected to
`TaskName, State`, and `Get-ScheduledTaskInfo` projected to run time/result fields.
Task Running is not proof of broker or Slack connectivity. Check the configured
gateway's known health/log location and effective broker, without dumping task
arguments, tokens, Compose environments, or all user-secrets. Do not invent a
health port, pidfile path, executable path, or provisioning script. If the task is
absent, say that this machine has not been shown to have that setup; consult its
operator-provided sidecar provisioning details. Do not fall through to SSH.

### S2: make remote selection explicit and verify the boundary

Files: `scripts/deploy-am-service.ps1` and `scripts/test-deploy-am-service.ps1`.

1. Implement D-3 at the script entry and helper entry before any I/O. Thread the
   value by named arguments so existing positional fixture calls cannot shift
   silently. Make the confirmation and preflight show the selected destination
   plus the fixed directory. Update help/examples and remove the implicit hostname.
2. Preserve the injected HTTP/SSH/SCP test seams. Ensure tests observe the actual
   destination passed to the native SSH boundary, not just a displayed variable.
   This may use a target-aware runner bound once for existing command-only helpers,
   or a locally owned SSH function shim exercising the default runner. Do not alter
   helper remote commands, monitor rules, or source replacement logic unnecessarily.
3. Update existing fixtures to pass a synthetic target such as
   `operator@gateway.example.invalid`; retain all existing assertions. Add the
   named C496 cases below to the same test file. Provide a `-Case` selector for
   those cases, with an omitted selector executing the existing suite plus all new
   cases. An unknown selector fails; a selection that runs zero cases is not green.
   This keeps later controls scoped without adding a new test-runner/policy entry.
4. Keep both scripts ASCII-only and compatible with Windows PowerShell 5.1.
   Use only owned temporary fixtures and fake network commands. Commit before
   running the suite; fix any caused failure without weakening existing guards.

## Verification design

### Ordinary acceptance (Code and separate Review)

| ID | Check and oracle | Owner / evidence |
|---|---|---|
| V-1 | Case-insensitive search for `server2` returns zero matches in the eleven implementation files listed in S1/S2. Broader repository search, including hidden instruction directories, produces a reviewed residual inventory: dated evidence, generated card snapshots, test fixtures, or unrelated Windmill references only. No active messaging instructions elsewhere may retain the implicit host. Treat search errors separately from no matches. | Code records the command, exit, and residual classifications; Review checks the relevant prose, not merely the count. |
| V-2 | Read every changed command block and cross-link. There is no copyable default remote SSH/deploy command, no instruction to set the old broker, no assertion that local implies fake-only, and no instruction to remove an override to establish isolation. All optional remote examples supply `-SshTarget`, identify the fixed supported directory, and require replacing placeholders. The renamed Telegram heading and Slack link agree. | Manual document review with file/section references. No new phrase-matching unit tests. |
| V-3 | Diff AppHost against the base: only comments and the one log message literal change. Diff Compose: comments only. Server/FakeGateway settings and resource ownership stay unchanged. | `git diff` and R-2. A live AppHost restart adds no evidence for this textual change. |
| V-4 | The sidecar section names the exact card-recorded task and `localhost:19092`, attributes the observation, includes an absent-task branch, and explains independent lifecycle. It does not claim an installed task, successful delivery, an installer, a health port, or Telegram support without evidence. | Manual walkthrough of the desktop troubleshooting path. No Slack/Telegram messages are sent during verification. |
| V-5 | Missing destination fails promptly and before all I/O; dot-sourcing stays inert. | `C496-TargetRequired`, including actual CLI invocation in an owned noninteractive child, helpers without target, and dot-source-only load. Count HTTP/SSH/SCP/archive calls; all zero. Assert nonzero exit and a configuration diagnosis, not an unrelated fixture failure. |
| V-6 | Malformed destinations fail before I/O; valid alias, DNS, and IPv4 target forms retain their exact value. | `C496-TargetValidation`: ordinary negative/positive data cases; assert refusal reason and zero runner calls for invalid cases. |
| V-7 | One explicit destination controls native SSH, SCP, preflight display, and returned/recorded target evidence, including the post-deploy reads and archive cleanup. | `C496-TargetRouting`: run two distinct synthetic destinations through fake preflight and fake deployment. Record each SSH argument vector and SCP destination. No call may use the other destination or any fixed host. |
| V-8 | Default and `-Deploy -WhatIf` run only read-only preflight; `-Deploy -Confirm:$false` reaches writes only after preflight passes. WhatIf displays the selected target and fixed directory. | `C496-WriteGate`: exercise the public entry in an owned child with local command/HTTP shims, assert zero SCP/archive/override/build/recreate calls in read-only modes, and the existing gated order in the fake deploy. Never invoke real remote preflight. |
| V-9 | Changing target selection does not broaden paths/services, bypass broker identity/Compose checks, change redaction, or lose backup/verification evidence. | `C496-FixedLayout` plus the full existing `scripts/test-deploy-am-service.ps1` suite (R-1). |
| V-10 | Both edited scripts parse in PowerShell 7 and 5.1 and contain only ASCII bytes; diff has no whitespace errors or edits to pre-existing historical artifacts. | PowerShell parser API, ASCII byte check, `git diff --check`, changed-path review. |

Suggested search commands from the repository root (read output and exit status;
`rg` exit 1 means no matches, while exit 2 is an error):

```powershell
rg -n -i 'server2' Antiphon.AppHost/Program.cs docs/agent-kinds.md docs/bootstrap.md docs/telegram-bot-ops.md docs/messaging-standalone.md docs/slack-bot-ops.md scripts/deploy-am-service.ps1 scripts/test-deploy-am-service.ps1 src/Antiphon.Messaging.Service/README.md docker-compose.dev.yml .claude/skills/telegram-e2e-smoke/SKILL.md
rg --hidden -n -i 'server2|100\.93\.77\.126|mc@|/home/mc/antiphon-messaging' --glob '*.md' --glob '*.cs' --glob '*.ps1' --glob '*.yml' --glob '*.yaml' --glob '!.git' --glob '!docs/superpowers/plans/**' --glob '!docs/investigations/**' --glob '!docs/cards/**' --glob '!scripts/hooks/__tests__/**'
```

The second search intentionally still finds the supported remote directory,
historical specs/reviews, and Windmill worker references. Classify them; do not
pretend a repository-wide zero is required or exempt every current document that
happens to include a date. Also inspect local/fake wording in the changed docs:
hostname replacement alone cannot establish V-2/V-4.

### Regression commands and result requirements

- **R-1:** `pwsh -NoProfile -File scripts/test-deploy-am-service.ps1`, then
  `powershell.exe -NoProfile -NonInteractive -File scripts/test-deploy-am-service.ps1`.
  Both must execute the full existing script suite plus C496 cases, exit zero, and
  print actual nonzero pass counts with zero failures. Run sequentially. Do not
  run `scripts/deploy-am-service.ps1` against a real target for this check.
- **R-2:** Run the existing two methods in
  `tests/Antiphon.Tests/Infrastructure/AppHostBrokerSourceGuardTests.cs`:
  `AppHost_does_not_hardcode_a_broker_hostname_in_WithEnvironment` and
  `Tracked_defaults_keep_the_local_redpanda`. Use the TUnit command below and a
  fresh results directory; require two passes, zero failures, and the named cases
  in the TRX. These are regression guards, not proof of live broker isolation.

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c496/ -- --treenode-filter '/*/*/AppHostBrokerSourceGuardTests/*' --report-trx --report-trx-filename broker.trx --results-directory .antiphon/c496-broker
```

Choose a fresh producer-owned output/results location if those names already exist.
Record the exact verified commit, command, test counts, and failures. Wait for each
foreground command; freeze source during builds/tests. Inventory and remove only
this run's alternate outputs across projects before finishing, after resolving and
checking their absolute paths. If a guard fails, rerun that failure at the base
before calling it inherited. This change warrants the bounded PowerShell suite and
two existing source guards; there is no C# routing, UI, persistence, or protocol
change requiring the full product suite or E2E traffic.

### Positive controls for post-land Mutation

Code runs ordinary cases above. Deliberate mutations belong to the independent
post-land stage; no control has been executed or claimed by this Plan dispatch.
Run one exact script case at a time using `-Case`, restore source, and rerun that
same case green. A parser/fixture error or zero cases does not count as red.

| Control | Temporary mutation | Exact selected case / expected red assertion |
|---|---|---|
| PC-1 | Restore a synthetic implicit destination when the target is missing. | `C496-TargetRequired`: the missing-target diagnosis/zero-I/O assertion fails. |
| PC-2 | Bypass target-shape validation. | `C496-TargetValidation`: an invalid case reaches an I/O seam or fails to give the required configuration refusal. |
| PC-3 | Route native SSH to a different `.invalid` fixture destination while preserving display. | `C496-TargetRouting`: recorded SSH destinations disagree with the selected target. |
| PC-4 | Route SCP to a different `.invalid` fixture destination while preserving SSH. | `C496-TargetRouting`: recorded SCP destination disagrees. |
| PC-5 | Replace the ShouldProcess target with a fixed synthetic destination. | `C496-WriteGate`: WhatIf target assertion fails. |

All runners remain local fakes throughout these controls. Historical-document
retention and prose accuracy use explicit review oracles rather than mutation tests
that merely pin exact wording. A SourceLanding stage stores its evidence externally
and restores its snapshot under the repository verification-restoration contract.

## Completion and handoff

Code is complete when both slices are committed/pushed, V-1 through V-10 and R-1/R-2
have recorded evidence, and only the scoped implementation files changed. Hand off
to separate ordinary Review; landing and any live deployment remain separate.
No remote host census, live message, AppHost restart, secret edit, or sidecar repair
is an acceptance condition for this documentation/configuration correction.

The next stage from this plan is **code**: verification design is included and the
explicit-target choice resolves the card's remote-host uncertainty without guessing
whether any other machine still uses that hostname.
