# Group 34 Herdr AlwaysOn Linux debug

Task: `58b18019` (`Debug+Fix`, Linux/server2).
Source: `bedea1c03cd788d83b2ff54c40bfb603927bd69c`, equal to fetched
`origin/rc/2026-10-26` when work began. Assigned branch:
`feat/card-task-58b18019`; no branch switch, rebase or merge was performed.

## Outcome

The two reported Windows failures in `HerdrAlwaysOnChannelParityTests` did not
reproduce on Linux. Exact class execution: **19 passed, 0 failed, 6 skipped**
(25 discovered). No fixture or product change is justified by this run.
This report is the only tracked change. Assertions and existing skips are unchanged.

The supplied qualification summary identifies the class and two failures, but
does not identify their methods. The original Windows TRX is desktop-only and
was not accessed. Do not attribute those failures to particular methods from
the Linux skip count or infer that the Windows qualification is repaired.

Earlier Claude/Herdr unknown-observation refusal changes are already present
in source (`e21a58b6accbc3e952a23340146c23b8fdeef43a`). Their runnable
named-placement, launch-definition and history-recovery rows passed here.
No reproduced Windows path/shell string cluster was found, so no speculative
pure-string witness or input-guard bypass was added.

## Execution evidence

Evidence retained outside the disposable checkout:
`/work/test-evidence/task-58b18019/`.
`build-base.log`, `class-base.log`, `class-base/class-base.trx`, `cases.json`,
and `phases-base/*.jsonl` retain outcomes, case identity/duration and recorded
phases. Uninstrumented external-call time remains unknown. This is one sample,
not a measured p95. No timeout or retry changes were made.

Both drivers acquired a host build slot immediately (`maxcpucount=6`).
Build: exit 0, 1,196 warnings, zero errors, 3m30.15s (lease held 212s).
Class run: exit 0, 1m52.635s (lease held 115s). PostgreSQL setup was shared
once for the run; its cold schema-cache miss reported 37.859s.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-58b18019-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-debug58b18019/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-58b18019-class-base -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-debug58b18019/ --property:UseAppHost=false -- --treenode-filter '/*/*/HerdrAlwaysOnChannelParityTests/*' --report-trx --report-trx-filename class-base.trx --results-directory .antiphon/debug-58b18019/class-base
```

The class run also enabled `ANTIPHON_TEST_PHASE_ROOT`, `RUN_ID`, `GROUP_ID`,
`ATTEMPT` and `SUITE` using the full `ANTIPHON_TEST_PHASE_` prefix for each key.
The large test DLL was run with `--no-build`, avoiding the checkpoint reader.
No assembly-wide, native desktop or separate Pty test project run was made.

## Required native rerun

Caller owns the native Windows rerun after the existing qualification permits
it. Rerun the entire exact class filter `/*/*/HerdrAlwaysOnChannelParityTests/*`
through the build-slot wrapper, using a fresh isolated output and fresh TRX.
The six unexecuted Linux rows are:

| Method | Arguments | Existing skip owner |
| --- | --- | --- |
| `AlwaysOn_channel_bound_survives_child_death_and_replies` | `Herdr`, `PtyHost` (2 rows) | CARD-0865 |
| `Standing_claude_ptyhost_native_wire_missing_target_never_creates` | none | CARD-0878 |
| `Standing_grok_unavailable_native_store_never_creates` | `PtyHost`, `Herdr` (2 rows) | CARD-0865 |
| `Unsafe_grok_rules_on_the_named_seat_refuse_before_any_pane_and_leave_the_streak_untouched` | none | CARD-0865 |

These skips are not passes. Native results remain required to resolve the
original Windows qualification failures. The desktop supervisor and native
qualification lock were untouched. No tags, release scripts or deployment
actions were performed. Standing fix authority required no further approval.
