# CARD-1348 A-1/A-2: cleanup observations and Git lock measurements

Status: measurement complete; root-owner single-observation claim disproved, checkpoint-reader lock conflict not reproduced. A distinct **single nested-identity observation** is confirmed and requires the plan to state its intended contract. Recommend retaining S1-S4 for TestDesign after that reconciliation. **Master only. No tracked product/test change or suite run.**

## Scope and provenance

- Task `acad7c25-3f86-43fd-8d65-dc893129ccc6`, assigned branch `feat/card-task-acad7c25`, checkout `/work/worktrees/task-acad7c25`.
- Audited and tested source: `d5dec9555bd726ad11bb30073c1733d6b3e7bb0e`. The final documentation commit is not the tested source SHA.
- Read the full requested plan at `55179cd34bae82070ff647186a98202537c0726f`, fetched from `feat/card-task-c01e8c96`, without switching/merging branches: `docs/superpowers/plans/2026-10-19-card-1348-test-isolation-plan.md`, A-1/A-2 and S1-S4.
- `card.ps1 get 1348 -Board Antiphon` returned card `ccbd3819-5245-4801-a02a-151fd8e657fa`, title **Test/tooling isolation: lease build slot only around build, per-test temp for Grok pin test, double dead observation before sweep**, three revisions at this read. Item 3 says “Temp sweep deletes after one dead observation”; it does not specify nested custody or separate sweep passes.
- Historical source reviewed: `3b9488e2fb31d5b4df4e2ff7905ae10eaeda686d:docs/investigations/2026-10-19-test-concurrency-isolation.md`. Its six historical roster failures and 2,170-second lease are stored evidence, not runs made here. Its claim that a common-object-directory lock can stall the snapshot was a hypothesis, not a measured lock/reader pair.
- Linux runner, Git **2.47.3**, .NET SDK **10.0.402**. Measurement window: 2026-10-19, approximately 14:36–14:58 UTC. No Windows qualification.
- All induced locks and destructive cleanup experiments used owned fixtures below `.antiphon/c1348-measurements/`; no induced lock in `/work/repos/antiphon/.git`. Passive filesystem observation of that common directory was read-only. Standard fetch/publication operations are separate from the experiments.

Generated logs, JSON, traces, helper source and receipts remain ignored under `.antiphon/c1348-measurements/` and `.antiphon/checkpoints/`. Essential results and provenance are reproduced below because ignored files are not publication artifacts.

## Host context

At 14:40:13 UTC, `/proc` metadata showed task `b28f2cb5` running a gated `dotnet run` with `InstructionBundleTests|OutputDistillationGateTests|OutputDistillerProvisionerTests`, including MSBuild children in that task's worktree. This task's isolated build was also running. Only process identity, working directory and selected build/filter arguments were recorded; no environment or credential dump.

The effective broker is the inherited `ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots`, not the container-local `127.0.0.1:8080` endpoint. The latter reported zero leases at 14:40:13 and is **not** the occupancy evidence for our grants. At 14:43:33 the effective broker reported:

| Holder task | Label | Grant UTC |
|---|---|---|
| `acad7c25` | `A1-owned@tests/Antiphon.Tests` | 14:42:04 |
| `b28f2cb5` | `distiller-v5@tests/Antiphon.Tests` | 14:42:19 |
| `c8c9f3c5` | `build:bin-seed-pin-red` | 14:43:29 |
| `acad7c25` | `c1348-git-measurements` | 14:43:33 |

Budget 4, occupancy 4, max CPU count 6, available memory 102,730 MB, floor 16,384 MB, no waiters. At 14:48:39 the same two other-task grants remained; this task held its cleanup checkpoint and traced-Git measurement grant; available memory was 93,082 MB. These are instantaneous observations, not a fleet-wide benchmark. All completed measurement grants waited zero seconds.

## A-1: root owner and nested custody are different checks

The production sequence on the audited SHA is:

1. `src/Antiphon.HostCleanup/Checkpoints/CheckpointTempRootSweep.cs:158`: `SweepOnce` observes the preflight marker owner before taking the root gate.
2. The gate is acquired at `CheckpointTempRootSweep.cs:161`; `Eligible` rereads the marker, grace, inventory and owner (`:209`, `:212`). A non-dead verdict retains the root.
3. `CheckpointTempRootSweep.cs:168` calls `TestRootGuard.MarkDeleting`; `src/Antiphon.HostCleanup/Checkpoints/TestRootGuard.cs:72` rereads the marker and `:79` observes its owner before replacement. Identity/generation fields and the exact previous marker bytes are checked (`:75`, `:84`, `:92`).

These are three observations **within one sweep**, not observations separated by the five-minute interval. `CheckpointTempRootSweep.cs:11` retains the ten-minute grace and `:12` the five-minute interval. No fourth root observation is justified by item 3 as currently written.

Nested custody is checked inside `Eligible` only: `CheckpointTempRootSweep.cs:224` chooses the preparing starter or `ToolCopyCleanup.ObserveExecutor`. `src/Antiphon.HostCleanup/Checkpoints/ToolCopyCleanup.cs:41` selects the executor receipt, then the launched identity fallback; `:48` returns that identity's single observation. The subsequent marker transition observes the **root owner**, not that nested identity.

### Existing six-case regression

**6 executed, 6 passed, 0 failed, 0 skipped; exit 0.** Receipt qualification also returned exit 0. Unedited transcript:

```text
BUILD SLOT released lease=251cbb0f-7c40-4d11-a161-f73295f42d03 held=576s
CHECKPOINT A1-owned commit=d5dec9555bd726ad11bb30073c1733d6b3e7bb0e build=ok filter=/*/*/CheckpointTempRootSweepTests/eligibility_is_rechecked_after_the_root_lock executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-acad7c25/.antiphon/checkpoints/A1-owned-20261019-144202-4363/run.trx slot=granted waited=0s dirty=0 source=d5dec9555bd726ad11bb30073c1733d6b3e7bb0e sourceState=clean buildSource=verified
CHECKPOINT A1-owned EXIT CODE: 0
CHECKPOINT SOURCE VALID source=d5dec9555bd726ad11bb30073c1733d6b3e7bb0e rows=1
```

Exact selection: `/*/*/CheckpointTempRootSweepTests/eligibility_is_rechecked_after_the_root_lock`. The source assertions are `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs:88`, with all three retention assertions at `:124` and skip reasons at `:129`.

| Argument | Expected skip | Payload / marker / index |
|---|---|---|
| `live` | `scripted-AliveSame` | retained / retained / retained |
| `unknown` | `scripted-Unknown` | retained / retained / retained |
| `reused` | `scripted-ReusedPid` | retained / retained / retained |
| `grace` | `grace` | retained / retained / retained |
| `marker` | `marker-invalid` | retained / retained / retained |
| `inventory` | `inventory-incomplete-or-linked` | retained / retained / retained |

The initial A1 build was deliberately stopped before any test launch (wrapper exit 137, zero results), after noticing `CheckpointTestScope.cs:77` invokes the temp coordinator when allocating roots. The replacement run sets **TMPDIR, TEMP and TMP** to the owned `.antiphon/c1348-measurements/private-temp`, preventing allocation hooks from sweeping shared `/tmp`. The interrupted attempt is not a regression failure or passing evidence.

### Scripted identity inventory

A gated, standalone diagnostic called the actual `CheckpointTempRootSweep`, `TestRootGuard` and `RunOwnershipStore` from the source-built HostCleanup assembly. It used a four-byte payload, a valid one-hour-old marker, a private temp parent per scenario, zero interval, and a generous 20-second diagnostic pass bound. `Recorder : ProcessIdentityProbe` logged each identity, actual caller stage from `StackTrace`, and scripted verdict; no PID race or sleep was used. This is ten diagnostic scenarios, not ten TUnit results.

Define R=`(Pid=2147483647, StartUtcTicks=1)`, N=`(2147483646,2)`, R2=`(2147483645,3)`; host/boot/namespace were identical within the fixture. The process IDs were synthetic probe inputs; no real process with those IDs was touched.

| Scenario | Observed sequence | Completed roots | Payload / marker / index |
|---|---|---:|---|
| root dead | R Dead preflight → R Dead eligibility → R Dead transition | 1 | removed / removed / removed |
| root alive at eligibility | R Dead → R AliveSame | 0 | retained / retained / retained |
| root alive at transition | R Dead → R Dead → R AliveSame | 0 | retained / retained / retained; `marker-write-IOException` |
| marker owner changes before gate | R Dead → R2 Dead → R2 Dead | 1 | removed / removed / removed |
| preparing dead | R Dead → R Dead → N Dead eligibility → R Dead transition | 1 | removed / removed / removed |
| preparing alive | R Dead → R Dead → N AliveSame | 0 | retained / retained / retained |
| preparing dead, hypothetical next verdict alive | R Dead → R Dead → **N Dead once** → R Dead | 1 | removed / removed / removed |
| launched executor dead | R Dead → R Dead → N Dead eligibility → R Dead transition | 1 | removed / removed / removed |
| launched executor alive | R Dead → R Dead → N AliveSame | 0 | retained / retained / retained |
| launched executor dead, hypothetical next verdict alive | R Dead → R Dead → **N Dead once** → R Dead | 1 | removed / removed / removed |

The identity-change case is **one observation of R plus two of R2**, not three observations of the same identity. The nested executor cases used the launched-identity fallback, not a separate executor-ownership receipt; that selection is explicit in the production source cited above.

Stored transcript assertions over the two synthetic stronger-contract scenarios returned exit 1:

```text
RETENTION WITNESS scenario=preparing-dead-then-live expected=True actual=False nestedObservations=1 pass=False
RETENTION WITNESS scenario=executor-dead-then-live expected=True actual=False nestedObservations=1 pass=False
Synthetic stronger-contract assertions: failed=2; production regression verdict not asserted
```

This is a deterministic, fixture-only payload-retention counterexample **if** the desired rule is two observations of every nested identity. It confirms the single-observation path; it does not demonstrate a real false-dead observation, process resurrection, or a violation of the documented root-owner rule. The probe's second N verdict was never requested. Root-owner no-change is warranted; whether the card also requires a stronger nested-custody or cross-pass rule belongs in Plan before any cleanup implementation.

The CARD-1346 fixes already exist at `src/Antiphon.HostCleanup/Checkpoints/ProcessIdentity.cs:48` and `tests/Antiphon.Tests/Checkpoints/CheckpointTestScope.cs:95`; no duplicate repair was attempted.

## A-2: exact Git paths, writers and readers

The owned fixture was `.antiphon/c1348-measurements/git-fixture-KndzfD`: an ordinary `repo`, local bare `remote.git`, and linked worktrees `a` and `b`. `git rev-parse --path-format=absolute --git-common-dir` returned `<fixture>/repo/.git`; `git rev-parse --absolute-git-dir` returned `<common>/worktrees/a` and `/b`. Thus an index lock in a linked worktree is **inside its own admin directory**, not `<common>/index.lock`.

The fixture had two tracked files (`file.txt`, `fixture.csproj`), local-only fetch/push, test identity, no hooks, system/global Git config disabled and no inherited optional-lock override for the source readers. The existing build-ledger runner independently sets `GIT_OPTIONAL_LOCKS=0` and disables hooks/fsmonitor/config at `scripts/lib/CheckpointProcessRunner.cs:56`; this investigation did not change that behavior.

Actual reader entry points:

- `SourceSnapshot.Capture(a)` (`tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:58`). It performs two captures and refuses changes between them (`:70`). Its child failure classification is `git_exit` or `git_timeout` (`:203`, `:207`), not a lock-specific classification.
- `BuildConfinement.CaptureLedger(a, ["build", "fixture.csproj", "--property:OutputPath=bin-probe/"], null)` (`tools/Antiphon.Checkpoints/BuildConfinement.cs:89`, `:329`, `:746`). It returned a real two-file ledger, not `LedgerCapture.None`.
- Dot-sourced `scripts/lib/checkpoint-source.ps1`, `Get-CheckpointSource -Repository a` (`:118`). Its actual source observations matched the C# commit, dirty count and fingerprint.

For each C#/PowerShell snapshot, exact Git calls were one `rev-parse --show-toplevel`, then twice:

```text
rev-parse HEAD
status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none
ls-files -v -z
-c core.quotePath=false diff HEAD --binary --no-ext-diff --no-textconv --no-color --no-renames
-c core.quotePath=false diff --cached --binary --no-ext-diff --no-textconv --no-color --no-renames
```

The ledger called `rev-parse --show-toplevel`, `rev-parse HEAD`, `ls-files -z -s`, `ls-files -z --others --exclude-standard`, and `ls-files -z --others --ignored --exclude-standard`. Each was preceded by the runner's configuration inspection:

```text
git -c core.attributesFile=/dev/null -c diff.external= -c core.hooksPath=/dev/null -c core.fsmonitor=false config --null --get-regexp ^(filter\..*\.(clean|smudge|process|required)|diff\..*\.textconv)$
```

Those five config calls per ledger exit 1 when no matching configuration exists; the actual ledger reads exit 0. This normal config absence must not be called a Git-lock failure.

### Writer and filesystem evidence

Node filesystem watchers observed lock creation/removal paths in the owned common/admin directories. Git Trace2 recorded exact argv, exit and elapsed time for the reader processes. Neither mechanism is claimed to be syscall tracing: `strace` and `inotifywait` were unavailable, and filesystem notifications can coalesce or miss very short-lived files/new directories. The held-lock positive controls establish required lock paths even where notifications missed them.

| Operation and invoking checkout | Concrete observation | Required/optional evidence |
|---|---|---|
| `a: git status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none` after touching unchanged file | `worktrees/a/index.lock` | Optional refresh: both real source readers completed while that lock remained held. |
| `a: git update-index --refresh` | `worktrees/a/index.lock` during refresh | A no-op invocation under the held lock returned 0; do not classify every refresh as a required write. `commit` is the positive control below. |
| `a: git fetch --no-auto-maintenance <fixture>/remote.git main:refs/remotes/fixture/main` | Local fetch succeeded, exit 0, 40.9 ms; reader HEAD/content unchanged | Initial notification missed the short-lived/new-path ref lock. Holding the exact remote ref lock caused `git update-ref refs/remotes/fixture/main <b-HEAD>` to fail 128; release made that update succeed. No packed-refs lock was established for this fetch. |
| `b: git commit --allow-empty -m "fixture commit"`; later same operation in a | admin `index.lock`, admin `HEAD.lock`, common `refs/heads/fixture-*.lock`; additional `AUTO_MERGE.lock` and auto-maintenance locks observed | Held own index/HEAD locks refused commit 128; released controls succeeded. B's locks did not refuse A's readers. |
| `a: git pack-refs --all --prune` | common `packed-refs.lock`, loose ref locks during pruning | Held common packed lock refused writer 128 after ~1.1 s; actual checkpoint readers completed before release. |
| `a: git repack -ad` | Exit 0, 13.3 ms; A's readers remained known/clean | No `.lock` event captured for this small repack. This is not proof that repack has no serialization or resource cost. |
| `repo: git worktree add --detach <fixture>/c HEAD`, then `lock`, `unlock`, `remove` c | `worktrees/c/locked`, `HEAD.lock`, `index.lock`, `ORIG_HEAD.lock`, `AUTO_MERGE.lock`; remove also observed c's index lock | All four operations succeeded; readers in A stayed known/clean. `locked` is an administrative marker, distinct from a writer's `.lock` file. C was owned and removed. |
| `a: git maintenance run --task=gc` | common `objects/maintenance.lock`, `gc.pid.lock`, `packed-refs.lock`, `objects/info/commit-graph.lock`; main and A/B HEAD/reflog locks | With the maintenance lock held: exit 0 in 5.0 ms, no further writer lock events; after release: exit 0 in 89.1 ms with GC lock activity. Skip/no-work success must not be represented as a required-lock refusal. |
| `a: git update-ref --stdin`, prepared `update HEAD <b-HEAD> <a-HEAD>` transaction | Real writer held `worktrees/a/HEAD.lock` and common `refs/heads/fixture-a.lock` simultaneously | Waited for `prepare: ok`; both paths existed. All three readers completed before sending `commit`. Writer then returned `commit: ok`, exit 0. |

### Held/released reader matrix

The final repetition using the receipt-qualified build assemblies had the following elapsed milliseconds **inside** each reader call. PowerShell process startup is excluded. Synthetic lock files were opened exclusively and held until all reader calls and the writer control finished, then removed in `finally`. Children had bounded deadlines and were joined. Thus successful readers did not depend on release; timing differences alone are not evidence of waiting.

| Condition | SourceSnapshot ms | Ledger ms | PowerShell source ms | Writer exit | Writer ms |
|---|---:|---:|---:|---:|---:|
| traced-baseline | 114.9 | 124.5 | 418.5 | — | — |
| held-index | 153.8 | 148.6 | 539.0 | 128 | 6.5 |
| released-index | 113.3 | 96.5 | 211.9 | 0 | 12.6 |
| held-HEAD | 103.7 | 90.0 | 306.9 | 128 | 144.4 |
| released-HEAD | 101.8 | 105.2 | 446.0 | 0 | 5.3 |
| held-packed-refs | 106.3 | 78.9 | 216.0 | 128 | 1208.2 |
| released-packed-refs | 138.9 | 128.0 | 915.5 | 0 | 20.7 |
| held-remote-ref | 153.9 | 456.7 | 521.1 | 128 | 104.7 |
| released-remote-ref | 403.2 | 325.7 | 803.0 | 0 | 2.8 |
| held-maintenance | 139.6 | 355.0 | 760.1 | 0 | 5.2 |
| released-maintenance | 360.5 | 225.6 | 370.2 | 0 | 94.8 |
| prepared-HEAD-transaction | 138.7 | 160.6 | 559.6 | 0 | — |

Every row returned reader process exit 0, `CaptureStatus=known`, `DirtyFiles=0`, `ErrorCode=null`; ledger `refusal=null`, two files. The traced batch also read once after the prepared transaction committed. Its **416 Git subprocess exits** were 351 exit 0 and 65 expected config-no-match exit 1, no other codes; maximum recorded Git-process duration 15.611 ms. All 13 triplets completed. The earlier complete traced repetition also completed 13 triplets with the same known/clean result. The initial untraced matrix completed another 26 reader triplets, including B-index/B-HEAD locks and three reads launched alongside a finite sequence of twelve sibling commits plus twelve pack-refs operations. That uncontrolled overlap is supplementary; the prepared transaction and held-lock cases prove overlap directly.

The prepared transaction's before/after evidence is especially useful:

```text
start: ok
prepare: ok
# HEAD.lock=true; refs/heads/fixture-a.lock=true
# all readers: known, dirtyFiles=0, commit=37aa0918930f5af27bad54583c328b4b35b83980
commit: ok
# all readers after commit: known, dirtyFiles=0, commit=0db10935e9902b4261c973c66d4bdcff87513bfa
```

The content stayed unchanged (empty commits); the committed HEAD and source fingerprint changed only after commit. That is actual source identity movement, not lock failure. The release controls that made empty commits in A likewise intentionally changed A's HEAD; stable sibling operations did not.

An earlier traced harness attempt failed with fixture ENOENT after `pack-refs` pruned an empty remote-ref directory. It was retained as incomplete evidence, the fixture parent was recreated, and the complete traced run above succeeded. That harness setup failure is not a checkpoint-reader failure. No reader lock refusal or release-bound recovery was found, so there is **no red-first witness for a source-reader retry**.

### Passive observation of the actual common directory

From 14:40:56.287 through 14:42:29.655 UTC, passive observation of `/work/repos/antiphon/.git` captured 18 rename events on only `worktrees/task-acad7c25/index.lock` and `worktrees/task-b28f2cb5/index.lock`. This was a build/preflight window, not evidence of full-roster execution. No packed-refs or HEAD lock event was captured in that interval. Notifications do not identify the writer PID and do not prove absence of short-lived missed locks.

A second window, **14:49:11.877–14:51:41.148 UTC**, captured 68 lock-path notifications: per-worktree index locks for this task and tasks `1193b198`, `567b1d1e`, `c8c9f3c5`, `b28f2cb5`; common branch/remote-ref locks for `567b1d1e` and `1193b198`; and `objects/maintenance.lock`. Eight process samples established narrow test-host overlap: `c8c9f3c5` ran `SeedSmokeTargetingPackContractTests/*` at 14:51:32–35; this task ran the six-case cleanup method at 14:51:35–38; `b28f2cb5` ran its instruction/distillation selection at 14:51:37–39. During 14:51:32–39 the observed lock events were those three tasks' **separate index paths**. This task's checkpoint still passed with verified source binding. Outcomes of the other tasks were not inspected.

No foreign lock was held, removed or repaired; no other checkout's status command or test was run. Historical rosters were not rerun. There is no observed test/tool collision attributable to Git locks in this sample; this bounded result does not prove none can occur under all repository sizes, maintenance timing or Git configurations.

## Caller inventory and S1-S4 recommendation

| Caller / existing plan slice | Audited mechanism | Recommendation |
|---|---|---|
| S1, `GrokSha256PinTests.Run` | `tests/Antiphon.Tests/Infrastructure/GrokSha256PinTests.cs:149` creates GUID scratch, but `:159` and `:207` delete the fixed `/tmp/grok-download`; extracted Dockerfile text still names it. The static gate covers only those deletes. | **Worth doing, highest isolation priority.** Confirmed shared destructive path by source reconstruction; no execution against the real shared path was attempted. |
| S2, generic `scripts/build-slot.ps1` | Acquisition at `:89`; whole command launch at `:120`; release in `finally` at `:147`. No literal no-build classification controls acquisition. | **Worth doing.** The existing plan's limited command classification addresses a confirmed caller lifetime. No broker-budget change is evidenced. |
| S3, `scripts/run-checkpoint.ps1` | Acquisition at `:636`, build execution at `:1110`, test execution at `:1160`, release at `:1170`. `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs:193` explicitly requires no-build leasing today. | **Worth doing.** This is a separate direct caller, not covered simply by changing S2. |
| S4, checkpoint scheduled and direct CLI paths | `RunScheduler.cs:364` leases the build; `:483` independently leases every row and `:534` sets `NoBuild=true`. `Program.cs:522` holds an outer lease around `RowRunner.RunAsync` at `:556`. | **Worth doing for both entry points.** Source/build identity checks are independent of these scopes. |
| Landing verifier | `server/Infrastructure/Git/LandingVerifier.cs:15`/`:22` delegates to `AgentTaskLandService.VerifyWithObserverAsync`; `AgentTaskLandService.cs:1738` acquires, `:1749` releases after `VerifyUnderSlotAsync`, which builds at `:1764` and runs no-build tests at `:1768`. | **Remaining separate caller.** S1-S4 do not change its whole-verification lease. A null injected gate is already an unbudgeted path, not a new exemption. |
| Nightly | `scripts/lib/nightly-run-impl.ps1:457` selects `nightly-tests.ps1`, invoked at `:485`. `nightly-tests-impl.ps1:396` launches the solution build and `:523` directly launches test executables through `Invoke-NightlyOwnedProcess` (`:74`); `nightly-owned-process.ps1:8` starts the native owner. | **S1-S4 do not cover this path.** These inspected direct entry points contain no build-slot acquisition. Wrapping the entire opaque script externally would retain a whole-script lease under S2's proposal. This is source inventory, not a live nightly execution claim. |
| Release scripts | `scripts/release-cut.ps1:104` calls `nightly-run.ps1` for its rc lane; candidate/publish/promote scripts perform Git/release operations, not checkpoint rows. | **Same separate nightly caller.** No release run, tag, publication or rc mutation was commissioned here. |

Recommendations assess the already-written S1-S4 plan; they are not implementation or a new design. None of those slices is invalidated by the root-owner correction or the lack of Git-reader failures. Root observation and Git retry should not be added merely to satisfy the old investigation wording.

## Reproduction, evidence boundaries and cleanup

A-1 command (with the three temp variables set to an exclusively owned directory first):

```sh
pwsh -NoProfile -File scripts/run-checkpoint.ps1 \
  -Name A1-owned -Project tests/Antiphon.Tests -OutputPath bin-c1348-a1-owned/ \
  -Filter '/*/*/CheckpointTempRootSweepTests/eligibility_is_rechecked_after_the_root_lock' \
  -MinExecuted 6 -Expect eligibility_is_rechecked_after_the_root_lock \
  -ExpectedSourceSha d5dec9555bd726ad11bb30073c1733d6b3e7bb0e -SlotWaitMinutes 8
```

The diagnostic and Git scripts were each run through `scripts/build-slot.ps1`, with labels `c1348-cleanup-probe`, `c1348-git-measurements`, `c1348-traced-git-complete`, `c1348-verified-cleanup-probe` and `c1348-verified-traced-git`. The standalone diagnostic compiled only its ignored harness and referenced the actual HostCleanup DLL; it did not modify the production assembly. Tracked source was unchanged throughout builds/measurements. The final diagnostic repetitions used the completed, receipt-qualified build assemblies (all ten cleanup scenario records were identical to the initial exploratory run). Their identities were:

```text
Antiphon.HostCleanup.dll SHA256 2faa1d7c14629a63d045e2bcf52d3475f621c0145e28bde06c96048a8fd6b0a7
Antiphon.Checkpoints.dll SHA256 584ea85dec93faeefde2a0998e25059fd2308d074845255f3e99e26a764163d8
```

The first exploratory probes used successfully built project DLLs from the interrupted larger build. Because that attempt did not supply a complete-build receipt, the ten cleanup scenarios and the traced reader matrix were repeated against the qualified assemblies above; no full suite or six-case regression rerun was needed. The reader reproduction requires loading these two source-built assemblies, calling the three entry points named above, and holding each Git-resolved lock in the owned fixture until all calls return. The real transaction reproduction is `git update-ref --stdin` with `start`, `update HEAD <new> <old>`, `prepare`; wait for `prepare: ok`, capture, then send `commit`. Never substitute the live common directory for the fixture.

All child commands and both passive watchers were awaited. The three producer-owned alternate outputs were inventoried and removed: `tests/Antiphon.Tests/bin-c1348-a1` (1,365 files), `tests/Antiphon.Tests/bin-c1348-a1-owned` (2,237 files), and the ignored probe's `bin-c1348-probe` (7 files). The whole owned Git fixture and both owned cleanup-scenario parents were also removed. No real repository lock or foreign output was deleted. Ignored logs, helper source, receipts and private-temp coordinator/build metadata remain; the private temp parent has 0 checkpoint root directories after test teardown. Cleanup inventories/results are retained in ignored evidence.

Remaining uncertainties:

- The card's text does not establish whether “double dead observation” also applies to nested starters/executors or means observations separated across sweeps. The single nested check is confirmed, but a real false-dead nested observation and resulting live-payload loss were not established. Plan must resolve that intended contract; operational evidence of such a false-dead observation would establish a bug beyond the synthetic stronger-rule witness.
- No full/frozen roster, native Windows run, network fetch, large repack, hostile marker writer, corrupted output, permission failure or Git timeout was induced. Reader retries are not supported by the measured result.
- Filesystem notification coverage is bounded and not a PID-attributed syscall census. The exact held-lock controls establish the particular reader/writer outcomes, not absence of every possible Git contention.
- Host load, PowerShell startup/JIT and build concurrency affect wall time; no performance improvement is claimed.

## Not done, noted

No fix designed or implemented; retain the existing S1-S4 proposal for TestDesign after Plan reconciles the nested-custody contract and remaining caller inventory, with no extra root-owner check or generic Git retry supported by this evidence.
