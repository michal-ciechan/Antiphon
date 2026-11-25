# Why the rc supervisor missed the quiet test host

In Windows qualification `3528e6b1` at
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`, `antiphon/all-group-47`
stopped writing diagnostics at 15:31:11 UTC. The chunk hit the one-hour
watchdog at 16:27:50 UTC without a TRX. Its 37 planned test IDs were all
missing from the row union. The capture reported two live job processes,
and the last diagnostic showed an in-progress `ReviewEvidenceResettlementTests`
node. The redirected Git pipe wait was investigated separately on
`feat/card-task-4699a1a9`. [Qualification analysis](2026-11-22-rc-run-3528e6b1-final-analysis.md)

The supervisor measured the launch process and all its descendants. The
native owner ran a job census every 10 ms while the test host waited in a
pipe read. CPU attributed to that census could keep the launch tree above
the 5 CPU-seconds per minute limit. Any file growth under either shared
root could reset the clock too. The supervisor supplied neither a planned
cost nor diagnostic progress, and its hang flag only reported suspicion.
The Windows host's exact CPU rate was unavailable to this investigation.

For a supervised rc run, the native owner now samples the test-host root
using its retained process handle. It checks the byte offsets and write
timestamps of the chunk's diagnostic files and the chunk's own TRX every
five seconds. A busy host, a change in either evidence source, or an
unreadable sample breaks the quiet interval. The existing budget rule
uses the planned seconds, with a 10-minute floor, when the active
identity and plan prove the cost. Otherwise it uses the supervisor's
configured budget, which defaults to 20 minutes.

The owner tries the existing hang capture before it terminates its private
Job Object. It records `idle-hang` as an incomplete chunk timeout. The
supervisor projects the owner sample into `supervisor-heartbeat.json` for
`idleMinutes`, `hangSuspected`, `idleBudgetMinutes`, and `hostCpu`. Its
run-wide file counts and byte totals are display fields. The owner retains
the existing absolute watchdog and post-completion grace.

## Introducing commit/card

CARD-1667 landed the observation-only supervisor in
`65631396a44904ed4a90559d4d3041316ae2b667`. Its heartbeat call used the
launch subtree, shared-root file observations, and an unknown plan cost.
The compiled supervisor carried those inputs forward in
`9cabf2f08c51c16aa83876138c305ffa42a98136`.

## Approving reviews

Review `230d1f5d-22d1-46bb-b83c-fbdc59968364` approved
`639f78a890f35093fe8366a6d2881b1dc649f005` in round 3, blockers only for
the reviewed diff and with `ordinaryScopeCompleted: Full`. It ran
`scripts/test-qualification-supervisor.ps1` (35 passed),
`scripts/test-orchestration-watchers.ps1` (39 passed), and
`/*/*/(RcQualificationWatcherScriptTests*)/*` (2 passed) on Linux.
Its five red-first cases covered the run-state copy and superseded-state
classification.

## Outside the selection

The observed hang involved a native Windows Full rc launch and an
Integration/Slow test. The prior supervisor checks covered detachment,
reporting, and run-state ownership. They had no case that paired a quiet
test host with a CPU-active owner and unrelated log writes. The earlier
completion-grace witness exercised a host that had already reported
execution completion. Group 47 was still executing. [Completion-grace investigation](2026-11-10-why-missed-session-runner-group4-post-completion-hang.md)

## Cheapest pre-land check

`scripts/lib/Antiphon.Nightly/Qualification*.cs` and
`scripts/lib/Antiphon.Nightly/NativeProcessOwner.cs` and
`scripts/lib/Antiphon.Nightly/NightlyChunkRunner.cs` each map to
`/*/*/QualificationSupervisorTests/Quiet_test_host_reaches_its_idle_budget_while_the_owner_and_other_logs_are_busy`.

The Linux red witness at `a8945b8ef77278b6604d0fbbe29e065ddca01d68` failed
1 of 1. The projected idle time was 0 minutes after 20 quiet minutes
while the launch owner used 10 CPU seconds per minute and another log grew.
The fix witness uses a fake clock and the production sampler with real
fixture files. It also checks that actual test-host CPU, ordinary diagnostic
appends and changes to the TRX each defer the idle timeout, and that a
proven plan cost overrides the fallback budget. Linux passed 1 of 1 with
zero skips at `0095d1fd01dcf3b4e5e7861223316a22424626ba`. Both runs held
build slots. The red implicit build used the SDK's default node count.
The green build applied the slot's six-node cap, followed by a leased
`--no-build` run.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-2051-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-2051-green -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/QualificationSupervisorTests/Quiet_test_host_reaches_its_idle_budget_while_the_owner_and_other_logs_are_busy'
```

The native check is part of the existing Windows-only `C1039_DescendantExit`
fixture. It is pending on an isolated Windows runner. Use the same source
with an apphost-enabled build and the following gated commands. The idle
launch's watchdog is 30 seconds and the test timeout is 120 seconds.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-2051-native-build -- dotnet build tests/Antiphon.Tests --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-2051-native -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/*/NightlyNativeOwnershipTests/C1039_DescendantExit'
```

## Exemption

None. The completion-grace defect and the redirected Git pipe wait have
separate causes and witnesses.
