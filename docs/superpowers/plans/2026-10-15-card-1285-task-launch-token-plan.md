# CARD-1285: recover task credentials at the fresh launch boundary

Task/landing owner: `23d87a95-40ee-40a5-9ce9-81faded14d82`.
Base: `157321d8bbedcd93a3e2b8f4c43f4a098db4cf7f`.
Branch: `feat/card-task-23d87a95`.

## Investigation and change

`AgentTaskService.CreateAsync`, retry and follow-up creation persist SHA-256 token
hashes but keep raw tokens only in the static `RawTokens` dictionary. Dispatch
`BuildEnv` consumes the entry once. Queued work survives a server restart; that
dictionary does not. Cold dispatch after restart silently omits the token. A
second fresh process for boot-wedge recovery also consumes an already-empty cache.
Both registry and profile launches reach `FinishDispatchedLaunchSpecAsync`.
Phone-home projection copies the environment and changes callback origin; it does
not remove this token. Runner transport already carries it.

S1 adds a six-variant regression through actual dispatch, remote preparation,
database persistence, the launch sink and phone-home serialization. An independent
database context witnesses the token hash at enqueue, then fresh authentication
checks task/session identity and Worker authority. No credential values appear in
assertion diagnostics.

S2 changes only the final fresh-process launch composition: retain a valid cached
token; for an absent, blank or hash-mismatched token, mint a random token, persist
its hash, then put the raw token in the environment before enqueue. Failure to
save prevents launch. Warm reuse and interrupted attachment to an existing
process do not enter this boundary and keep their existing credentials. No schema,
runner, owner-guard, timeout or assertion relaxation is required.

Activation requires a server restart only. A running tokenless process cannot
gain an environment variable from this change; use a newly dispatched task after
activation. Existing valid process tokens remain valid across a server restart.

## Verification design

This is the explicit narrow Final selection requested in the CARD-1285 brief
(CARD-1287 exception: no whole Unit lane). Impact is bounded to final fresh launch
composition, with named full integration classes and the owner guard's Unit
classes below. No full assembly is needed. S1 is intentionally red; rerun its exact
six cases as part of the full affected class in S2.

| ID | Required ordinary acceptance |
|---|---|
| V-1 | Missing raw cache after remote preparation: Codex Code, Claude Review and Grok Code receive an authenticating token whose hash is committed before enqueue. |
| V-2 | Valid cached token is retained; stale cache is replaced and the old credential is rejected. |
| V-3 | Fresh boot-wedge replacement gets a valid new token and the old process token is rejected. |
| V-4 | Phone-home wire retains task token; task/session identity and Worker delegation limits remain correct; token is absent from argv. |
| V-5 | Read-only live evidence: correlate reported tasks and local process presence-only observations with server-stop timestamps. |
| R-1 | Full remote dispatch class: projection, warm reuse, refusal, sourced launch and brief spill behavior. |
| R-2 | Full adjacent composition classes: registry Codex/Grok, profiles, environment precedence and sourced boot-wedge binding. |
| R-3 | Full owner-guard and slot-executor classes retain missing-token refusal, deadlines and settlement cancellation. Source-reading launch-model guard remains green. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1285-red/` | token-red | `/*/*/PhoneHomeTaskDispatchProjectionTests/C1285_worker_launch_has_durable_task_token` | V-1, V-2, V-3, V-4 | C1285_worker_launch_has_durable_task_token | 6 | 5 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1285-green/` | remote-dispatch | `/*/*/PhoneHomeTaskDispatchProjectionTests/*` | V-1, V-2, V-3, V-4, R-1 | PhoneHomeTaskDispatchProjectionTests | 14 | 5 | true |
| CP-3 | S2 | CP-2 | launch-adjacent | `/*/*/(LaunchEnvLayersIntegrationTests)\|(PinnedProfileLaunchSpecTests)\|(CodexDelegateDispatchTests)\|(GrokDelegateDispatchTests)\|(BootWedgeRelaunchParityTests)/*` | R-2 | LaunchEnvLayersIntegrationTests,PinnedProfileLaunchSpecTests,CodexDelegateDispatchTests,GrokDelegateDispatchTests,BootWedgeRelaunchParityTests | 5 | 4 | true |
| CP-4 | S2 | CP-2 | owner-guards | `/*/*/(CheckpointTaskOwnershipTests)\|(CheckpointSlotExecutorTests)\|(LaunchModelArgumentAppenderTests)/*` | R-3 | CheckpointTaskOwnershipTests,CheckpointSlotExecutorTests,LaunchModelArgumentAppenderTests | 3 | 3 | true |

Run via the checkpoint tool with `--after S1` or `--after S2`, exact committed
`--expected-source-sha`, and `--serial`. Bootstrap compilation of the checkpoint
tool is the one explained non-manifest build, through `build-slot.ps1` into an
isolated output; run its DLL afterward. Inspect fresh TRX rosters, not just totals.

### Mutation handoff

All PCs remain pending for method-scoped SourceLanding Mutation. Use
`/*/*/PhoneHomeTaskDispatchProjectionTests/C1285_worker_launch_has_durable_task_token`.

| PC | Deliberate defect | Expected failing variants |
|---|---|---|
| PC-1 | Omit absent-token recovery | three missing variants and relaunch |
| PC-2 | Omit durable hash save before enqueue | missing, stale and relaunch |
| PC-3 | Accept a cached token without matching its persisted hash | stale |
| PC-4 | Regenerate even a valid cached token | cached |

Mutation owns red/restore/green and missing-control discovery. Candidate pack:
`docs/superpowers/mutations/` (unbound; none commissioned by Code).

### Cost

Ordinary checkpoint floor: 17 minutes including red-first build; authoring and
investigation approximately 20 minutes. One execution per selection; no repeat
proof after green. No fleet location is embedded in execution commands.
