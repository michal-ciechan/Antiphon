# Why missed in Windows Full group 84

Windows Full run `c9816b02` at `ac8339c754a84ab53ca07708c091d4e79bd1fb5b` failed
`PhoneHomeTaskDispatchProjectionTests.Signed_out_codex_runner_still_claims_and_reuses_a_warm_session`
in `antiphon-all-group-84.trx`. Dispatch recorded `Failed` with
`A runner-bound task must use a Worktree workspace.`

## Introducing commit and card

The launch-time Worktree rule dates to CARD-0604 S7
`b02798a6aa19e33c997808079cd331d6e6810638`. CARD-0604 S8
`24bd06edff13bef18b09d63a6c60cfb150e18036` also enforces it at task creation. CARD-0645
`ec5f7db2474ef0d9d42dfd30c5bf4903693d28bc` wired the dispatcher to
apply the launch policy before cold remote preparation.
The CARD-0959 witness `d241c2feed6ec810d83ac6ba14df5ba5d6079ab6` directly
seeds an already-queued Shared task to exercise the warm branch.
`AgentTaskService.EffectiveRequestedWorkspace` retains an explicit mode and
defaults a new unpinned request to Worktree on either platform. This fixture
bypasses request handling and explicitly stores Shared.

The regression began with the Codex 6.2 ladder change
`04a228e8da80687a36d504973255b3ac5e355a23` (CARD-1514). It requires a warm
Frontier Codex session to carry the current launch model in
`EffectiveModelId`. The fixture left that field null. The dispatcher
discarded the session, attempted a cold launch and reached the existing Worktree
rule. Switching the seed to Worktree would skip the Shared-only warm branch.
The repair records the current Frontier slug on the seeded session and keeps
the signed-out observation, reused session and queued prompt assertions.

The exact Linux checkpoint `20261122-092749-d8cb` at the same master SHA
reproduced the Worktree refusal in 8.06 seconds. Its TRX recorded one test
executed and one failed. It used the fixture PostgreSQL database.

## Approving reviews

Review `47df8544-87e2-4081-b4ef-d6dd78bffd35` approved
`228789f0eede954c0ca7ce97fa69fc2b512f8876` with Final/Full scope.
Receipt `20261121-105220-65a7` recorded 107/107 in CP-3
(`CodexDelegateDispatchTests`, `NamedCodexAgentLaunchTests`,
`AgentTaskPoolTests`, `CodexCliObservationTests`) and 7/7 in CP-14
(`AgentTaskPoolTests.Frontier_Codex_reuse_requires_the_current_launch_model`).
The Review report is on `9b75228d158b1de16834b15e67a8782400dc6e83`.
[Closed checkpoint selection](../superpowers/plans/2026-11-21-codex-ladder-62-checkpoints.md#checkpoints)

## Outside the selection

The C959 witness is in the Integration lane under
`tests/Antiphon.Tests/Application/PhoneHomeTaskDispatchProjectionTests.cs`.
The closed Review rows exercised the Frontier model predicate and the stale
model cases in the pool fixture. The phone-home projection class stood outside
those rows. The reviewed derived index already linked it to
`AgentTaskDispatcher.cs`. The short selector's explicit
`dispatcher-ref-only-composition` row selected four other classes in CP-9.
The seeded Windows Full lane included the phone-home class.

## Cheapest pre-land check

`server/Application/Services/AgentTaskDispatcher*.cs` →
`/*/Antiphon.Tests.Application/PhoneHomeTaskDispatchProjectionTests/*`

For a Frontier ladder edit, the single signed-out warm method is the focused
checkpoint within that class.

## Exemption

None.
