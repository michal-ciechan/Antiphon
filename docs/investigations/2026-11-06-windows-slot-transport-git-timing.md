# Native Windows slot-transport scratch Git timing

The native Windows run reproduced the qualification failure: **19 tests, 5 passed,
14 failed, 0 skipped**. Every failure was `TimeoutException: scratch git exceeded
its operation deadline`. Failures occurred in `init`, `config` and `add`, so this
sample does not identify a hang unique to one Git command. The one-second contract
is unreliable for this concurrent fixture shape under the measured host load.

## Provenance and execution

- Task: `b5e998ee`; branch: `feat/card-task-b5e998ee`.
- Base/source: `5abd946dd2c235b6e873b0cbb95c738f35216dcf`.
- Host: `DESKTOP-KTLKPIF`, Windows 10 Pro `10.0.19045`, 8 logical processors,
  34,298,142,720 bytes physical RAM; native Windows, not WSL.
- Git: `C:\Program Files\Git\cmd\git.exe`, `git version 2.50.1.windows.1`.
- Runtime: .NET `9.0.22`, x64; TUnit `1.44.0.0`, Microsoft Testing Platform `2.2.2`.
- One native default-output build, with `UseAppHost=false`: exit 0, 0 errors,
  1,803 warnings, MSBuild elapsed **11m 50.56s**. Wrapper wall **715.237s**;
  slot waited **0s**, `maxcpucount=4`, lease
  `f18215fa-ac94-472f-b43a-7a7edb224cef`. Warnings included TUnit cancellation
  diagnostics and ASP.NET analyzer `AD0001`; they did not prevent this build.
- Exactly one test row, with unchanged one-second `GitBudget` and unchanged test
  concurrency. Wrapper UTC **2026-11-06 18:47:58.379–18:49:36.822**, wall
  **98.438s**; slot waited **16s** for its memory floor, then held **81s**, lease
  `8195a88a-8693-4082-8d96-515d7cb86f99`. Test-platform duration **74.296s**;
  test driver exit **2**. No test rerun or whole-suite lane was used.
- Explicit bounds: slot admission **10 minutes** per invocation, build wrapper
  **20 minutes**, test-platform execution **10 minutes**, test wrapper including
  admission **20 minutes**. These were diagnostic collection bounds under the
  task's 60-minute allowance, not proposed test timeout changes. Existing class
  timeout **80 seconds** and scratch-operation timeout **1 second** were unchanged.

Commands, from this worktree (instrumentation must be reapplied to reproduce the
per-operation log):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label b5e998ee-build -SlotWaitMinutes 10 -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj -p:UseAppHost=false --disable-build-servers --verbosity minimal
pwsh -NoProfile -File scripts/build-slot.ps1 -Label b5e998ee-test -SlotWaitMinutes 10 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter "/*/*/(C0824WorkspaceSlotRoleGateTests*)/*" --timeout 10m --report-trx --report-trx-filename run.trx --results-directory .antiphon/b5e998ee-evidence/results
```

The test child also received the standard phase recorder variables:
`ANTIPHON_TEST_PHASE_ROOT=.antiphon/b5e998ee-evidence/phases` (absolute in the
actual invocation), run ID `b5e998ee-windows-once`, group ID
`C0824WorkspaceSlotRoleGateTests`, attempt `1`, suite `Antiphon.Tests`.

## Measurement boundary and censoring

Temporary local instrumentation surrounded `CheckpointProcessRunner.Run` inside
`C0824SlotTransportWorld.RunGit`. It recorded UTC start, monotonic elapsed
milliseconds, literal arguments, cwd, budget, exit, timeout, exception type and
test method to the task-owned JSONL file. Serialization and the synchronized
append happened after the stopwatch stopped and outside the runner's deadline.
Logging can still perturb scheduling between calls; this is an observational run.

The measurement includes the shared runner's process launch, Windows custody,
exit, output drains and cleanup. It does not isolate Git execution from process
startup, thread-pool scheduling, Defender or filesystem activity. It covers only
calls through this helper, not every production runner Git subprocess.

**14 of 26 observations were cut short by the unchanged deadline.** Their elapsed
times are timeout/cleanup return times, not natural completion times. Consequently
the all-observation p95 below is not an uncensored Git completion p95. The
successful-only p95 is biased by excluding those 14 calls. Neither establishes a
final budget under the approximately 2x measured-p95 policy.

## Operation distribution

Milliseconds; nearest-rank p50/p95; all recorded outcomes included unless marked
successful. Timeout results were `TimedOut=true`, exit 128; all other calls exited 0.

| Selection | Count | Succeeded | Timed out | p50 ms | p95 ms | Max ms |
|---|---:|---:|---:|---:|---:|---:|
| Overall, observed returns | 26 | 12 | 14 | 1002.771 | 1146.391 | 1147.386 |
| Overall, successful only | 12 | 12 | 0 | 509.439 | 688.353 | 688.353 |
| `init`, observed returns | 14 | 6 | 8 | 1128.797 | 1147.386 | 1147.386 |
| `init`, successful only | 6 | 6 | 0 | 509.439 | 655.808 | 655.808 |
| `config`, observed returns | 11 | 6 | 5 | 688.353 | 1127.207 | 1127.207 |
| `config`, successful only | 6 | 6 | 0 | 455.801 | 688.353 | 688.353 |
| `add`, observed returns | 1 | 0 | 1 | 1035.533 | 1035.533 | 1035.533 |
| `commit`, `rev-parse`, `clone`, `fetch`, `worktree`, `status`, other | 0 | 0 | 0 | unknown | unknown | unknown |

No world reached its first commit: setup failed earlier. Zero later-command
samples must not be interpreted as fast execution. Eight initial `init` calls
started within **9.669ms** and all timed out. Six later `init` calls completed
in **174.563–655.808ms**, followed by failures in `config` or `add` in those
worlds. All helper activity was between **18:49:12.426219 and 18:49:16.076780 UTC**.
The pattern is consistent with common launch/scheduling contention; it does not
prove which internal phase consumed the second, or rule out a stall cut off by
that second.

### Five slowest observed operations

All five were **timed out**, with arguments exactly `git init --initial-branch=main`.

| Observed ms | Working directory |
|---:|---|
| 1147.386 | `C:\Users\lndco\AppData\Local\Temp\c0824-transport-a574d3a649a6429596771be0e670db4c\origin` |
| 1146.391 | `C:\Users\lndco\AppData\Local\Temp\c0824-transport-e9c3282853e54b418b456606d70e246e\origin` |
| 1140.103 | `C:\Users\lndco\AppData\Local\Temp\c0824-transport-493fe855c4334bf28f231e91304a9215\origin` |
| 1138.442 | `C:\Users\lndco\AppData\Local\Temp\c0824-transport-7b101155eed94b038d1a8153ae6217d0\origin` |
| 1138.436 | `C:\Users\lndco\AppData\Local\Temp\c0824-transport-b8158224001f469fa76487396cd10105\origin` |

## Host load and Git configuration

**These numbers were taken UNDER shared host load while the live Windows
qualification supervisor was present.** PID 391408 was observed read-only as
`pwsh`, started at 06:07:19 UTC. No qualification process was stopped, signalled
or modified. Counters measure the whole machine, including this diagnostic;
they do not attribute resource consumption to the qualification alone.

`Get-Counter` collected 43 approximately one-second samples, separated by the
sampler's wait/collection overhead, during the test wrapper including slot wait:

| Counter | Mean | p95 | Max |
|---|---:|---:|---:|
| CPU, `% Processor Time` | 89.214% | 100.000% | 100.000% |
| Disk, `% Disk Time` | 81.599 | 154.568 | 1894.749 |
| Disk, average queue length | 0.816 | 1.546 | 18.947 |
| Disk bytes/second | 22,808,206 | 74,130,773 | 314,557,922 |
| Available memory, MB | 5762.907 | 6257 | 6344 |

Minimum available memory was 5199MB. `% Disk Time` can exceed 100 on this
counter; it is not a normalized single-disk utilization percentage. Two samples
in the operation window plus a one-second boundary margin showed CPU
**88.454–90.056%**, disk queue **0.017–0.728**, disk time **1.682–72.809**, and
disk throughput **1.247–4.998 MB/s**. Their sparse sampling cannot exclude a
short spike. An earlier baseline before the build showed CPU **89.300%** and
disk queue **1.793**. During the build CPU frequently reached 100%.

Read-only `git config --global/--system --show-origin --get-all` checks found:

| Setting | Global | System |
|---|---|---|
| `core.fsmonitor` | unset | unset |
| `core.untrackedCache` | unset | unset |
| `core.autocrlf` | unset | `true` |
| `safe.directory` | unset | unset |
| `credential.helper` | unset | `manager` |
| `gc.auto` | unset | unset |
| `core.longpaths` | unset | unset |
| `core.fscache` | unset | `true` |
| `commit.gpgSign` | `false` | unset |

System values came from `C:/Program Files/Git/etc/gitconfig`; the global signing
setting came from `C:/Users/lndco/.gitconfig`. Also checked and unset at both
scopes: `core.hooksPath`, `core.ignoreStat`, `core.preloadIndex`,
`core.splitIndex`, `core.attributesFile`, `init.templateDir`, `maintenance.auto`.
Parent `GIT_CONFIG_NOSYSTEM`, `GIT_CONFIG_GLOBAL`, `GIT_CONFIG_SYSTEM` and
`GIT_CONFIG_COUNT` were unset. The scratch helper itself sets
`GIT_CONFIG_NOSYSTEM=1` and `GIT_TERMINAL_PROMPT=0`, so the system autocrlf,
credential-helper and fscache settings above did **not** apply to these calls.
It invokes the shared runner with `git:false`, so that runner's additional
Git-hardening/config-discovery path is not used. Scratch global config has no
configured fsmonitor, helper or autocrlf override. These observations provide
no positive evidence for fixing the failure with fsmonitor/GC switches.

TEMP was `C:\Users\lndco\AppData\Local\Temp\`. Defender was in `Normal` mode,
antivirus and real-time protection enabled, `DisableRealtimeMonitoring=false`.
This is an ordinary local TEMP location potentially subject to that scanning,
but **its actual exclusion/scanning status could not be established**:
`Get-MpPreference` hid exclusions behind administrator access, and
`MpCmdRun.exe -CheckExclusion -Path ...` refused with `0x80070005` (access denied).
No Defender setting was changed. It would be incorrect to claim confirmed
scanning or Defender causation. Windows `LongPathsEnabled=1`; the read-only
`fsutil 8dot3name query C:` also refused access. No safe-directory/ownership,
credential, path-length or Git command-error diagnostic was observed before
the deadlines, but this timeout-only sample cannot exclude those causes.

## Per-test outcomes and phase evidence

All cases belong to `Antiphon.Tests.Application.C0824WorkspaceSlotRoleGateTests`,
category `Integration`, one exact-filter group. Durations below are TRX seconds.

| Test/case | Outcome | Seconds |
|---|---|---:|
| `Removed_role_slot_is_released_normally` | Failed | 1.201 |
| `Listed_role_leases_a_slot_on_the_real_runner` | Failed | 1.227 |
| `Retry_keeps_the_first_decision_of_its_preparation(busy-then-none)` | Failed | 1.227 |
| `Retry_keeps_the_first_decision_of_its_preparation(slot-then-none)` | Failed | 1.201 |
| `None_stops_new_leases_while_releases_continue(hosted-loop)` | Failed | 1.200 |
| `Retry_keeps_the_first_decision_of_its_preparation(legacy-then-listed)` | Failed | 1.201 |
| `Retry_keeps_the_first_decision_of_its_preparation(slot-refused-then-none)` | Failed | 1.227 |
| `Decision_is_recorded_before_the_mirror_is_sent` | Failed | 1.227 |
| `Unlisted_role_gets_the_legacy_mirror_and_never_holds(mutation)` | Failed | 1.786 |
| `Unlisted_role_gets_the_legacy_mirror_and_never_holds(role-not-listed)` | Failed | 2.009 |
| `Unlisted_role_gets_the_legacy_mirror_and_never_holds(invalid)` | Failed | 2.015 |
| `None_stops_new_leases_while_releases_continue(reconcile-pass)` | Failed | 2.205 |
| `Hot_reload_applies_to_the_next_lease_without_restart` | Failed | 2.263 |
| `Unlisted_role_gets_the_legacy_mirror_and_never_holds(none)` | Failed | 2.319 |
| `Flag_off_hosted_loop_sends_no_release` | Passed | 6.180 |
| `Flag_off_or_no_feature_ignores_the_list(flag-off:Plan)` | Passed | 11.427 |
| `Flag_off_or_no_feature_ignores_the_list(flag-off:none)` | Passed | 11.430 |
| `Flag_off_or_no_feature_ignores_the_list(flag-off:*)` | Passed | 11.483 |
| `Flag_off_or_no_feature_ignores_the_list(runner-lacks-feature)` | Passed | 11.494 |

The five passing cases do not construct the scratch transport world. The 14
failing cases fail during `BuildRepository`, before transport/role-gate behavior
can be exercised. This is a reproduced setup failure, not successful validation
of the role-gate scenarios. TRX reports them as failures, not test-platform
timeouts, because the helper throws its own timeout exception.

The phase recorder retained 19 complete case records, two shared-fixture records,
and a health record. Shared DB-create/start took **7.205s**, migration **34.947s**;
shared runtime-model setup **0.074s**. The `db-assembly` fixture record spans
65.893s and has **23.742s remainder**, overlapping test activity; it is not an
extra serial startup cost to add to the run wall. Per-case process-wait, database,
host, other measured spans and remainder are preserved in `summary.json`.
Unseparated container readiness, seed, discovery, teardown and process-internal
phases remain unknown where no dedicated span exists; absent spans are not proof
of zero cost. Per-test/fixture totals overlap and must not be summed as wall time.

## Recommendation and limits

**Favour a cheaper concurrency shape first.** The smallest candidate is to apply
the existing `ParallelLimiter<GitProcessSpawnLimit>` to this scratch-Git-only
class, using the repository's normal child-spawn classification. Its default
width on this eight-processor host is four, rather than this sample's eight
simultaneous first starts and fourteen attempted worlds. Keep the one-second
helper deadline and all role/transport assertions unchanged for that candidate.
The phone-home test host runs in-process; the scratch world's subprocesses are
Git. This is a proposed fix to verify in Code, **not a demonstrated green fix**;
this task did not run the limiter experiment. Shared immutable seeding with
per-test clones is a larger possible follow-up, not the first change.

There is no defensible final replacement budget from this sample. Twice the
successful-only p95 is **1.377s**, but discards the majority of timed-out calls.
Twice the all-return p95 is **2.293s** (a rounded **2.5-second diagnostic
candidate**), but doubles timeout/cleanup timing rather than natural completion
p95. Do not label either a measured production/test budget or assume 2.5s will
pass. If the bounded-concurrency candidate still fails, use a separately assigned
native diagnostic with a longer observation-only ceiling to obtain uncensored
completion times for the full command set. Then derive a rounded approximately
2x p95 budget under comparable load, per `docs/test-performance-standards.md` R5,
and record the small-sample limitation and collection task.

Adding `-c core.fsmonitor=false -c gc.auto=0` alone has weaker support here:
fsmonitor is not configured, system config is already disabled, and most failures
precede creation of any commit. This task does not prove such switches useless,
but does not justify selecting them as the repair. It also does not distinguish
process startup, runtime scheduling, Defender and filesystem latency. The caller's
Linux summary (158 calls, maximum 299ms) remains a different-host measurement;
its referenced ignored report was absent from the locally available remote ref,
so it was not independently revalidated here.

## Restoration and retained evidence

After the test child exited, instrumentation was restored exactly with:

```powershell
git checkout -- tests/Antiphon.Tests/Application/C0824SlotTransportWorld.cs
git diff --exit-code -- tests/Antiphon.Tests/Application/C0824SlotTransportWorld.cs
```

The second command passed. No product code or test change is committed. This
investigation Markdown is the task's only committed path. Generated diagnostic
evidence stays ignored, never force-added. No release/nightly directory or target
branch was written, and no landing/update operation was run.

**Cleanup is blocked by tool policy.** Before cleanup, all 72 generated `bin`/`obj`
directory targets were resolved inside this exact task worktree and checked for
reparse points; their creation times were 18:36:06–18:42:16 UTC, during this build.
Automatic approval review rejected the literal-path bulk `Remove-Item` request,
then also rejected a narrower request targeting only
`C:\Antiphon\worktrees\card-task-b5e998ee\tests\Antiphon.Tests\bin`.
Both refusals said only `blocked by policy`; no command ran and no more specific
reason was supplied. No alternative deletion mechanism was used. Consequently
the 72 generated directories, raw `results`/`phases`, and the task's scratch
driver/analyzer scripts remain. `cleanup-inventory.json` under the evidence root
records the remaining paths for an authorized cleanup. Failed world construction
also never reached `DisposeAsync`; its fourteen task-owned TEMP world roots are
listed in that inventory from the operation log. They were not manually modified
outside the assigned worktree. The investigation is complete, but the requested
output cleanup still needs an allowed cleanup action before task completion.

Retained local evidence root:
`C:\Antiphon\worktrees\card-task-b5e998ee\.antiphon\b5e998ee-evidence\`.
It contains `git-timing.jsonl`, `load-test.jsonl`, `load-build.jsonl`,
`summary.json` (TRX cases plus phase records), `environment.txt`,
`instrumentation.patch`, build/test stdout and stderr logs, build/test receipts,
and `cleanup-inventory.json`. These ignored artifacts are local evidence, not guaranteed durable
after worktree retirement; this committed report preserves the essential results.

SHA-256 provenance:

| Artifact | SHA-256 |
|---|---|
| Instrumented helper source used for the build | `AB49ABEBB70785105C79934C56ACEC17CE787533B74EA5D98627001FCE0E6FA9` |
| Instrumented test assembly used by the row | `73F5FECDCE217D0C95C16FCD4ED975D83247C364B090B4238D100E87DC2C2F21` |
| Raw Git timing JSONL | `93AB09F71AD7DC652169AEE3E6929C7B586E07D22A2452000430C33D04190583` |
| Test load JSONL | `85852A91EA470BD416EE1E2351850CC937D1DF50CC96C9D5013BABE2B79B76F7` |
| Extracted summary JSON | `AC715857F665F6659EE3353E474B874231FB79A7C45E8F652B850D0560B55EA5` |
| Original TRX before output cleanup | `CCE3AAE16F5BA69AF4EC04DB4DA21D001CFA16372B69D1C7D073FC38687D7165` |

--- next stage ---
next: code
handoff: Apply the existing GitProcessSpawnLimit to C0824WorkspaceSlotRoleGateTests after confirming its scratch-Git-only child classification; retain GitBudget=1 second and all assertions, then verify the exact 19-case class natively under comparable load with uncensored per-operation reporting. If it remains red, collect longer-ceiling diagnostic timings before selecting a new budget.
artifact: docs/investigations/2026-11-06-windows-slot-transport-git-timing.md
