# CARD-0824 S1c Code report (task 6db87da1)

Outcome: S1c is implemented as the test design (master 5237bb95a = 84bc86089) specifies, with the slot pool still OFF. CP-21 and CP-22 are green with validated clean receipts at tested SHA `69d15cd6fde113a5d1493ccd8256cafe88ca3ecf`.

- Branch: `feat/card-task-6db87da1`, worktree `/work/worktrees/task-6db87da1` (runner mirror).
- Original Code task (landing owner): `6db87da1`. Base: `38e67ae1579dc71da848fb2c89cc9c55aaa83543`.
- Plan: `docs/superpowers/plans/2026-11-03-card-0824-s1c-test-design.md` (on master, not on this branch).
- The checkpoint runs used a byte-identical copy at `.antiphon/c0824-s1c-test-design.md`.

## Commits (all pushed)

| SHA | What |
|---|---|
| `bb57f7ac08236847f282b75ae20f0f18a9f8b50e` | Red-first: the ST and RG tests, S-5 seams, and stubs that keep today's behaviour. |
| `2561829864d86fd8db54c44c86f772ad09489f74` | Implementation: S-1 to S-4, T-D-1 to T-D-6. |
| `69d15cd6fde113a5d1493ccd8256cafe88ca3ecf` | V-11/V-5 reachability corrections, measured budgets, operator docs. **This is the tested SHA.** |
| (this report commit) | Report only. |

## What changed

- **`server/Application/Settings/WorkspaceSlotRoles.cs`** (S-1, T-D-1, T-D-6)
  - `TryParse` accepts exactly `*`, `none`, or comma-separated role names.
  - Names are compared ordinally, in canonical case, untrimmed.
  - It refuses: empty entries, whitespace, other case, numbers, unknown names, `Mutation`, and `*` or `none` mixed with names.
  - `WorkspaceSlotRoleSet.Admits` checks in this order: `source_landing`, then `roles_none`, then `mutation`, then `all`, `listed` or `role_not_listed`.
- **`DelegationSettings.WorkspaceSlotRoles`** is a `string`, default `"*"`. `DelegationSettingsValidator` adds the grammar failure, which names `Delegation:WorkspaceSlotRoles` and the canonical list.
- **`DelegationSettingsRegistration.AddDelegationSettings`** (S-2, T-D-2)
  - It registers the existing validator, `Bind` and `ValidateOnStart`, plus `DelegationSettingsShapeValidator`.
  - The shape validator enumerates `Delegation`'s children and refuses `WorkspaceSlotRoles` that has children or a null value.
  - `Program.cs` now calls it in place of its three registration statements.
- **`WorkspaceSlotRoleGate`** (S-3, T-D-3) is a singleton in Program.
  - It reads `IOptionsMonitor.CurrentValue.WorkspaceSlotRoles` on every `Evaluate`.
  - An `OptionsValidationException` or an unparsable value gives `(false, roles_invalid)`. It never throws, and warns once per invalid spell.
  - `WorkspaceSlotsEnabled` stays on `IOptions`, restart-only.
- **`RemoteWorkspaceService`** (S-3, S-4)
  - New optional trailing constructor parameters: `IOptionsMonitor<DelegationSettings>? monitor` and `WorkspaceSlotRoleGate? roleGate`. When no gate is registered it falls back to a gate over the container's monitor, or over `*`.
  - `DecideSlot(task)` covers the flag, the gate and SourceLanding.
  - `ResolveSlotDecisionAsync(db, task, expectation, at, ct)` writes nothing unless the flag is on, a binding exists and the runner advertises `workspaceSlotV1`. Otherwise it reuses this preparation's recorded decision while decisions outnumber clears, or decides and saves a `WorkspaceSlotDecision` event before the send.
  - `MirrorAsync` now takes the decision. That decision is the single lease condition, so the flag and capability checks have one site, inside `ResolveSlotDecisionAsync`.
- **`AgentTaskEventType.WorkspaceSlotDecision = 41`** (T-D-5). Appended; no migration. Detail formats:
  - `workspace_slot_decision: slot|legacy preparation=<D> reason=<r>`
  - `... cleared preparation=<D> refusal=<code>`
- **`RemoteWorkspacePreparer`** (T-D-4)
  - It resolves the decision between `BeginMirrorPreparationAsync` and `MirrorAsync`.
  - On a `ConflictException` failure it writes `cleared` in its existing failure branch. The refusal code is `workspace_slot_unavailable` for the pool-bound refusal, otherwise the exception's code.
  - The `cleared` event is timed at least 10 ticks after its decision, the same idiom as `RemoteWorkspaceReadiness`.
- **Docs**
  - `docs/session-runtime-invariants.md`: a CARD-0824 S1c bullet.
  - `docs/docker-stack.md`: the slot-volume section now covers the grammar, `"none"` instead of `[]`, that an array is refused, that reload works only through the AppHost JSON, that env vars shadow the file, that a Docker rollback is a restart, and that the next `WorkspaceSlotDecision` event confirms a rollback.
- **Tests**: `C0824WorkspaceSlotRoleSettingsTests` (ST, 31 cases), `C0824WorkspaceSlotRoleGateTests` (RG, 19 cases), and `C0824SlotRoleTestKit.cs` (`MutableDelegationMonitor`). The recording logger is the existing `TestHelpers/RecordingLogger<T>`. S-5 adds `C0824SlotTransportWorld.StartAsync(tweak, configure)`; the flag-on default is kept.

Defaults applied under standing authority (T-D-1 to T-D-6 and Q-1 to Q-3): exact case, the new event type, and Mutation off slots even under `*`. No operator override was needed.

## Deviations from the design text (each keeps the oracle)

1. **V-11 `role-not-listed` uses a Review task, not a Code task.** In this world a Code task fails at the dispatcher's progress-baseline capture before remote prep, so it is unreachable. Review is just as unlisted, with `WorkspaceColdReset = true`, so PC-28 still applies.
2. **V-11 "never holds" is now "no `workspace_slot_unavailable` event and no Warning".** It was "zero Held". Ordinary remote prep always writes two Held events (in flight, then ready). The no-Warning check also catches M-2 (a gate that rethrows).
3. **V-5 `[]`.** In this run `[]` binds to `""` or null, not to the `"*"` the console probe measured, so the grammar refuses it as well. The oracle is now that the shape validator's own failure ("single string") is present for both array values. PC-12 and PC-13 still go red: without the shape check there is no such failure, and `["Plan"]` binds to `"*"`.
4. **V-3.** Under a list, Mutation's reason is `mutation`, not `role_not_listed`. This follows V-9, which requires `(false, mutation)` under `"Plan,Docs"`.
5. **Budgets.**
   - RG is `[Timeout(80_000)]`, set from the first green TRX: all 19 cases took 29-38.6 s at load ~30, and the design's 45 s left under 7 s of headroom.
   - ST stays at 15 s, because its reload poll is bounded at 10 s (slowest measured case 1.5 s).
   - Per the design, Code sets these from the first green run.
6. **V-12 and V-14.** Each earlier task is made terminal before the next one is seeded, because the rig has one runner seat. The oracle is unchanged.

## Verification (closed list, checkpoint tool)

**Red record.** This was an unlisted direct run, made to record red-first. It was a build-slot `dotnet run` of the CP-21 filter at `bb57f7ac`: 50 executed, 39 failed, 11 passed. The passing cases were the expected guards: V-17 ×4, V-18, busy-then-none, V-13, V-5 absent/`none`/`Plan,Docs`, and Layered.

**Iteration runs.** These were also unlisted, as dev iteration at `25618298` and its local edits. The first gave 45/50, with 5 failures that were fixed as deviations 1-3. The second gave 50/50.

**Run `20261103-220857-3b76`** (both rows):

```
CHECKPOINT CP-21 commit=69d15cd6fde113a5d1493ccd8256cafe88ca3ecf build=ok filter=/*/*/(C0824WorkspaceSlotRoleSettingsTests*)|(C0824WorkspaceSlotRoleGateTests*)/* executed=50 passed=50 failed=0 skipped=0 trx=/work/worktrees/task-6db87da1/.antiphon/checkpoints/20261103-220857-3b76/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=69d15cd6fde113a5d1493ccd8256cafe88ca3ecf sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=69d15cd6fde113a5d1493ccd8256cafe88ca3ecf build=reused filter=/*/*/(C0824WorkspaceSlotServerTests*)|(RemoteWorkspacePreparerTests*)|(DefaultRunnerPinTests*)|(RolePolicyConfigurationValidationTests*)/* executed=79 passed=67 failed=12 skipped=0 trx=/work/worktrees/task-6db87da1/.antiphon/checkpoints/20261103-220857-3b76/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=69d15cd6fde113a5d1493ccd8256cafe88ca3ecf sourceState=clean buildSource=verified
```

CP-22 red, with 12 failures:

- 11 were `C0824WorkspaceSlotServerTests` cases that hit their 35 s timeout.
- 1 was `RemoteWorkspacePreparerTests.Cancel_of_a_queued_runner_task...`, which saw no request within its 10 s wait.
- Every one of the 79 executions ran concurrently for 36-48 s at load ~35.

**Rerun of the red row, `20261103-221744-828b`** (rerun 1 of 3):

```
CHECKPOINT CP-22 commit=69d15cd6fde113a5d1493ccd8256cafe88ca3ecf build=ok filter=/*/*/(C0824WorkspaceSlotServerTests*)|(RemoteWorkspacePreparerTests*)|(DefaultRunnerPinTests*)|(RolePolicyConfigurationValidationTests*)/* executed=79 passed=79 failed=0 skipped=0
```

- slot=granted waited=15s, sourceState=clean, buildSource=verified.
- `validate --rows CP-22` → `CHECKPOINT SOURCE VALID source=69d15cd6… rows=1`.

**Base comparison.** This was an unlisted run, a pre-existing-red check. It was a detached base worktree at `38e67ae15`, built and run under the build slot (build waited 0 s, held 309 s; run waited 30 s, held 47 s), with the same CP-22 filter: 79/79 green. So the first red was a load-overlap timeout. It was not a regression and not inherited deterministic red.

**CP-21 alone, `20261103-223234-bc8e`** (for a validatable receipt, because a RED run cannot validate):

```
CHECKPOINT CP-21 commit=69d15cd6fde113a5d1493ccd8256cafe88ca3ecf build=ok filter=/*/*/(C0824WorkspaceSlotRoleSettingsTests*)|(C0824WorkspaceSlotRoleGateTests*)/* executed=50 passed=50 failed=0 skipped=0 trx=/work/worktrees/task-6db87da1/.antiphon/checkpoints/20261103-223234-bc8e/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=69d15cd6fde113a5d1493ccd8256cafe88ca3ecf sourceState=clean buildSource=verified
```

- `validate --rows CP-21` → VALID.
- Phases: slotWait 30 s, build 393 s, tests 31 s.

**Other checks**

- Evidence diff: `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 38e67ae15… -HeadRef HEAD` → `commits=3 entries=0 violations=0` (exit 0, at 69d15cd6).
- Landability: `git merge-tree --write-tree origin/master(97e77f9f1) HEAD` exits 0, with no conflicts.

## V/R outcomes

| IDs | Outcome |
|---|---|
| V-1 to V-9, V-6/R-3 | ST 31/31 passed |
| V-10 to V-18 | RG 19/19 passed |
| R-1 | C0824WorkspaceSlotServerTests 24 passed |
| R-2 | DefaultRunnerPinTests 6 passed, incl. `SourceLanding_mutation_reaches_runner_launch_with_binding` |
| R-4 | RemoteWorkspacePreparerTests 27 passed |
| R-5 | RolePolicyConfigurationValidationTests 4 passed |

All R rows are from the rerun.

## Not run

- **The whole Unit lane.** The verification profile asks for it, but the brief makes CP-21 and CP-22 a closed list. The lane is about 3 h on server2, past this task's 150-minute budget.
- **No Windows run.** Nothing runs on the desktop. The new classes are portable (no `[NativeLinuxOnly]`), so the policy rows, the census and the live-lane pin are unchanged.

## Pending for Mutation

All PCs and mutants stay pending, method-scoped. No deliberate mutants were run in this task.

- **PC-1 to PC-28** stay as designed, with two notes:
  - PC-3 and PC-28 use the V-11 `role-not-listed` case, which is now a Review task.
  - PC-19, PC-21 and PC-27 have one site, the flag/feature check in `ResolveSlotDecisionAsync`. `MirrorAsync` now keys only on `decision.Slot`.
- **M-1 to M-8.** M-2's witness is now V-11's no-Warning assertion as well as V-7.

## Amendment correction (§4.3 / §4.5)

Already done on master by `868704edd`. That commit is docs(card-0824) and says WorkspaceSlotRoles is a single string, default `"*"`, soft rollback `"none"`; it also changed the S1c docs step. Nothing was copied here.

Lines 90 and 344 of that amendment still describe the earlier `string[]` design, in the S1c scope lines. The orchestrator may want a Docs touch-up. I did not edit that file.

## Restart

None. The slot flag ships off, and nothing changes behaviour on the desktop.
