# Why Windows group 93 left two stall-sweep tasks queued

Windows Full run `1fe763a9`, group `antiphon-all-group-93`, failed two `TaskProgressStallSweepTests` methods at rc pin `0cfc4ac1ffb41f5af159a0c1db423fe119950267`. Both passed in a Linux checkpoint at that exact pin and in an exact-method run against test PostgreSQL at master `93e24eac5050f9e0ed1ef8c439c098140ccb25f5`. The pin already contains the stall-write fix.

| Windows method | Result |
| --- | --- |
| `A_stalled_task_whose_agent_row_is_gone_records_its_stall_and_does_not_poison_the_tick` | 0.64 s. The tick returned and the incident had a null `AgentId`. The witness failed because the other task was still `Queued`. |
| `An_agent_row_deleted_under_the_stall_insert_does_not_poison_the_tick` | 0.44 s. The interceptor fired, the tick returned, and no stall incident was recorded on that tick. The other task was still `Queued`, so the test stopped before checking the next sweep. |

## What landed

Debug task `9726e7bf-183e-4582-b936-388babee9f73` addressed the `23503` incident foreign-key failure from Windows run `b8cbfb24` group 86. One land request (`099ac0c6-51ca-4a7f-ba28-1819639d2ca3`) published all four commits through `cd8d677ab912cbd9c27c8d4e81d208f9865b3ba2`. The two methods were absent at the previous rc pin `2fd61b643ea4231fa732cd4969b5284d43b33f39`. [Original group 86 investigation](2026-11-25-why-missed-group-86-agent-incident-fk.md)

| Landed commit | Change |
| --- | --- |
| `0812dbdeb0a8c7f86056873e0d821c744239e83b` | Adds the vanished-agent witness. |
| `948907e64eb13366b356170e8021b80ef6f0883d` | Adds the delete-during-insert witness. |
| `52ece03ef17ee4adc8e9b1d6a316408752f05cee` | Nulls the incident's `AgentId` when the agent is absent and detaches the incident on a failed save before rethrowing. |

`git log -S` on the two method names and on the existence check and detach identifies these commits. `git range-diff` matches the original reviewed series to the landed series, including original fix `3f285cbb35d28a45782c860fd364319b06349f5f`. The rc pin and current master have identical witness source and identical stall-write behavior in `TryRaiseProgressStallAsync`. The intervening dispatcher edits concern path identity and ghost release. There is no later stall-write fix to backport. The six supplied backport commits exclude `52ece03ef`, which is already an ancestor of `rc/2026-12-06`. In a temporary index loaded at the pin, the fix patch reversed and reapplied cleanly, reproducing the exact pin tree `31c83adaca76f1900df15235979f22fb04ec4744`.

## Review and landing selection

- Final Review round 1 (`459ebfb9-5c09-4367-b4db-9ed464aeba27`) reported Clean and Full with `reviewedSourceClean: true` at `289260ba2246e29c8dbdbc48b78e6d539c3ea317`. Both witnesses failed with the original `23503` before the fix. Source-validated run `20261125-203504-5c75` then passed all 15 stall-sweep tests, including both witnesses, and all 10 cases of the original Grok method.
- The land request's `LandRequested` event records the expected source SHA and omits `filter=`. The land verifier returns after the build when the request has no filter. The Review handoff also supplied only the expected source SHA. The published source thus had targeted Review execution, with the landing verifier's test selection empty. [Land verifier](../../server/Application/Services/AgentTaskLandService.cs)
- CARD-2082 does not list `TaskProgressStallSweepTests` as a proposed guard or invariant extraction candidate. Its inventory names the class in the historical Linux receipt for CP-2. [CARD-2082 inventory](../superpowers/plans/2026-11-26-card-2082-invariant-inventory.md#linux-receipt-keys)

## Why the isolated runs pass

These tests use the assembly's shared PostgreSQL database. `[NotInParallel]` serializes test bodies, and rows left by earlier tests can still be visible. The stall scenario itself has one local `Working` task. The dispatcher counts local, non-specialist `Working` and `Dispatched` tasks without a retained capacity wait toward its default limit of two, including the stalled task after its agent is deleted. One additional occupied seat can therefore take the capacity-hold path, save a `Held` event, and leave the witness task `Queued` even when the stall write left the context usable. The Windows assertions contain no hold detail, so the reason those tasks stayed queued remains unverified. [Fixture](../../tests/Antiphon.Tests/Application/TaskProgressStallSweepTests.cs) · [Dispatch capacity gate](../../server/Application/Services/AgentTaskDispatcher.cs)

The cheapest check for a recurrence is the witness task's `Held` event detail. A dedicated test database for each witness would let the same-tick dispatch assertion exercise the failed-save behavior without depending on other tests' task rows. Repeating the current short selection alone cannot establish Windows chunk behavior.

## Verification

- Linux rc cutoff `20261126-074134-4c4f`, CP-2 at `0cfc4ac1ffb41f5af159a0c1db423fe119950267`, recorded 58 passed, 0 failed, 0 skipped, with both named methods passed in the TRX. The row records clean source and a verified build. The retained receipt is under `/work/evidence/rc-cut-pass2-5b7e6588/20261126-074134-4c4f/`.
- Linux server2 master `93e24eac5050f9e0ed1ef8c439c098140ccb25f5`, build-slot lease `80685fe4-dadd-4a93-bb09-cd14cab5d614`, test PostgreSQL. The two exact methods returned 2 passed, 0 failed, 0 skipped in 41.008 s. The wrapper held the lease for 271 s and reported that `dotnet run` built with the default MSBuild node count.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g93-master-exact -- dotnet run --project tests/Antiphon.Tests '-p:UseAppHost=false' -- --treenode-filter '/*/Antiphon.Tests.Application/TaskProgressStallSweepTests/(A_stalled_task_whose_agent_row_is_gone_records_its_stall_and_does_not_poison_the_tick)|(An_agent_row_deleted_under_the_stall_insert_does_not_poison_the_tick)'
```
