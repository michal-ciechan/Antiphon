# CARD-0611 final adoption verification

The implementation predates this Code task: `ModelAlias.IsOpus` recognizes Opus 5.5 and the Codex
High launch, hold, profile, and client surfaces already name `gpt-6-sol`. The installed CLIs are
Claude Code 2.1.280 and codex-cli 0.156.1. Live one-turn probes on 2026-09-27 confirmed
`--model opus` used `claude-opus-5-5` and `codex exec --ephemeral -m gpt-6-sol` succeeded.

There is no `gpt-6-terra` in the investigated catalog. This round keeps Medium at
`gpt-5.6-terra`; replacing Terra with Luna 6 changes the model family, and that choice still
needs an explicit operator decision. This verification round updates stale explanatory text
only; no test case is added or changed.

## Verification design

V-1: The whole Unit lane remains green. V-2: All affected full integration classes remain green.
R-1: The installed CLIs accept the selected launch aliases as observed by the live probes above.
The positive controls stay pending for method-scoped SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c611-final/` | unit | `/*/*/*/*[Category=Unit]` | V-1 | >= 1500 executed, 0 failed | 1500 | 6 |
| CP-2 | S1 | `CP-1` | application | `/*/Antiphon.Tests.Application/(CodexDelegateDispatchTests*)\|(ModelAvailabilityTests*)\|(NamedCodexAgentLaunchTests*)\|(PinnedProfileLaunchSpecTests*)\|(ComplexityRoutingWalkTests*)\|(DelegationRetryEventKindTests*)/*` | V-2 | CodexDelegateDispatchTests,ModelAvailabilityTests,NamedCodexAgentLaunchTests,PinnedProfileLaunchSpecTests,ComplexityRoutingWalkTests,DelegationRetryEventKindTests; 0 failed | 6 | 20 |
| CP-3 | S1 | `CP-1` | agent-tui | `/*/*/(AgentTuiLaunchResolverTests*)\|(AgentTuiProfileServiceTests*)/*` | V-2 | AgentTuiLaunchResolverTests,AgentTuiProfileServiceTests; 0 failed | 2 | 20 |

### Cost

The ordinary checkpoint floor is 46 minutes including one isolated build. Live CLI probes are
manual acceptance and are recorded above. No product test has changed, so red-first is not
applicable to this documentation correction.

## Execution result (2026-09-27)

Checkpoint run `20260927-204059-1c55` is stored under
`.antiphon/checkpoints/20260927-204059-1c55/report.md` at commit `2ab6bd37048300564c10a3e2da9f1f1e80602f63`.
The build passed with `UseAppHost=false`. CP-1 ran 3,408 Unit tests: 3,407 passed and one failed;
34 were skipped. The failure was the brief's standing timing flake,
`ResilienceBudgetTests.Narrower_parent_deadlines_win` (12.4 s versus a 12 s limit).
CP-2 ran 57 affected application integration tests: 47 passed and 10 failed. Six failures
explicitly require Windows `cmd.exe`, absent on this Linux host. The other four (three Codex
dispatch tests and one pinned-profile test) are inherited from already-landed CARD-0772:
runner-less Codex is refused before claim with `codex_desktop_unqualified`. The reviewer
bisected them to the merge-base and confirmed they fail on every OS. CARD-0783 tracks updates
to those four stale tests. This run does not claim those classes green. CP-3 selected 14
`AgentTuiLaunchResolverTests` results, all passed, but missed `AgentTuiProfileServiceTests`
because its namespace is `Antiphon.Tests`; the roster check exited 3, so CP-3 was red.

CP-3 was rerun with the corrected filter at commit
`6a2895612afea2665b7753cf117e071f969bfe73` in run `20260927-211824-9577`:

`CHECKPOINT CP-3 commit=6a2895612afea2665b7753cf117e071f969bfe73 build=ok filter=/*/*/(AgentTuiLaunchResolverTests*)|(AgentTuiProfileServiceTests*)/* executed=36 passed=36 failed=0 skipped=0 trx=.antiphon/checkpoints/20260927-211824-9577/rows/CP-3/run.trx slot=granted waited=0s`

The 36 include 14 resolver and 22 profile-service results. CP-3 is green; the checkpoint
tool deleted its `bin-c611-final/` output. CP-1 and CP-2 retain their original red results.

The two live CLI probes passed. The checkpoint failures leave the ordinary verification round
red, and method-scoped SourceLanding Mutation positive controls remain pending.
