# CARD-0611 final adoption verification

The implementation predates this Code task: `ModelAlias.IsOpus` recognizes Opus 5.5 and the Codex
High launch, hold, profile, and client surfaces already name `gpt-6-sol`. The installed CLIs are
Claude Code 2.1.280 and codex-cli 0.156.1. Live one-turn probes on 2026-09-27 confirmed
`--model opus` used `claude-opus-5-5` and `codex exec --ephemeral -m gpt-6-sol` succeeded.

There is no `gpt-6-terra` in the investigated catalog. Keep Medium at `gpt-5.6-terra` because
replacing Terra with Luna 6 changes the family and the intended Medium rung. This verification
round updates stale explanatory text only; no test case is added or changed.

## Verification design

V-1: The whole Unit lane remains green. V-2: All affected full integration classes remain green.
R-1: The installed CLIs accept the selected launch aliases as observed by the live probes above.
The positive controls stay pending for method-scoped SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c611-final/` | unit | `/*/*/*/*[Category=Unit]` | V-1 | >= 1500 executed, 0 failed | 1500 | 6 |
| CP-2 | S1 | `CP-1` | application | `/*/Antiphon.Tests.Application/(CodexDelegateDispatchTests*)\|(ModelAvailabilityTests*)\|(NamedCodexAgentLaunchTests*)\|(PinnedProfileLaunchSpecTests*)\|(ComplexityRoutingWalkTests*)\|(DelegationRetryEventKindTests*)/*` | V-2 | CodexDelegateDispatchTests,ModelAvailabilityTests,NamedCodexAgentLaunchTests,PinnedProfileLaunchSpecTests,ComplexityRoutingWalkTests,DelegationRetryEventKindTests; 0 failed | 6 | 20 |
| CP-3 | S1 | `CP-1` | agent-tui | `/*/Antiphon.Tests.AgentTui/(AgentTuiLaunchResolverTests*)\|(AgentTuiProfileServiceTests*)/*` | V-2 | AgentTuiLaunchResolverTests,AgentTuiProfileServiceTests; 0 failed | 2 | 20 |

### Cost

The ordinary checkpoint floor is 46 minutes including one isolated build. Live CLI probes are
manual acceptance and are recorded above. No product test has changed, so red-first is not
applicable to this documentation correction.
