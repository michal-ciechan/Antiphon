# Owned process containment: CARD-1850, CARD-1852 and the g82 card-file path

Plan and folded verification design, 2026-11-12. Source baseline:
`cc48c70050a2932eddf4abe18cea8659d478c33e`. Next stage: **code**.
This document authorizes implementation and ordinary verification in slices; it is
not evidence that native containment has already passed. Mutation is waived by
the operator's 2026-11-12 instruction; ordinary regression red/green is still owed.

The result is one invocation-owned launch/termination abstraction with Windows
and Linux implementations, used by both LandingGit launch paths and card-file
Git. Cancellation must end marked or job-owned descendants even after an
intermediate parent exits. An incomplete observation must never become proof
that a repository is safe to admit another writer.

## Ground truth

| Card/brief assumption | Code and evidence at the baseline | Design consequence |
|---|---|---|
| CARD-1850 says Linux is unaffected | The original simple `sleep` hook preserves Linux ancestry; a double fork does not. CARD-1852 explicitly corrects the broader assertion. | Test the dead-parent topology on both OSes, independently. |
| Fix `OwnedProcessTree.Plan` to find everything | `OwnedProcessTree.ReadSnapshot` starts from a root and follows currently visible parent edges; `Plan` validates start identities. No missing historical edge can be reconstructed safely. | Keep its identity safeguards and tests; containment membership supplies the missing authority. Never infer ownership from image name or repository path. |
| LandingGit already owns all descendants | `ExecuteProcessAsync` and `RunGitInputBytesAsync` create `OwnedChildMarker` only for journaled mutations, apply its environment before start, then call `Attach` after `StartProcess`. `AssignedBeforeSpawn` stays false. | Contain every launch in these two paths, including unjournaled reads; keep journaling conditional on mutation. |
| The Linux marker kills the orphan | `OwnedChildMarker.TerminateMembers` only calls Windows `TerminateJobObject`. Linux `AnyAlive` scans environment for fencing, but never signals those discovered orphans. | Reuse the marker for an actual bounded sweep, with live/empty/unknown outcomes. |
| Job close should always kill | Production `OwnedChildMarker.Dispose` deliberately does **not** use `KILL_ON_JOB_CLOSE`; C24 requires a child to survive worker death and keep the durable fence. The test-only `DescendantLeakScope` does use kill-on-close. | Preserve production crash semantics. Do not copy the test job's lifetime policy. |
| g82 still only calls `Kill(true)` | Baseline `CardFileRepository` has the landed job fix: it creates/attaches a Windows marker **after** start; it neither applies a Linux marker nor awaits complete containment cleanup. `TryKill` skips its tree fallback once git has exited. | Replace this parallel cleanup implementation with the shared owner. CARD-1856 is Done; its remaining native proof is still required here. |
| EOF proves descendants are dead | LandingGit's fast drained path and EOF continuation can release the journal based on root exit and streams alone. A descendant can close all three streams and continue writing. | At cancellation cleanup, require root exit **and** containment empty before journal removal, including the continuation. Test closed-stream survivors. |
| A current cgroup/pidfd design is deployable everywhere | Read-only inspection of this assigned Linux container found UID 1654, kernel `4.15.0-213-generic`, hybrid controller entries, no writable cgroup root and `/proc` without hidepid. These are observations of this container, not a probe of the live production server. The checked-in server compose uses UID 1654 and declares no delegated cgroup subtree. | Use environment membership now. Do not require cgroup v2, `cgroup.kill`, pidfds, Docker privilege changes or host changes. Recheck capabilities in the executing lane. |
| Only new tests are affected | Launch seams are overridden by existing fixtures; journal/pipe tests deliberately inject ownership failures. `ServerGitProcessTests` scans the launch syntax. CARD-1782 selects existing classes and Windows boundary coverage. | Migrate seams and their consumers explicitly, preserve fault injection, and run the existing regression roster below. |

Evidence read in full:

- CARD-1850, CARD-1852 and CARD-1856 via `scripts/card.ps1 get ... -Board Antiphon`.
- `docs/investigations/2026-11-12-harness-hangs-g118-srg4-windows.md` at
  `origin/feat/card-task-ed1f9715`, SHA `cdf274ad8d135c100a4f126fcf0fd5b6ddc1cc30`.
  It establishes the dead MSYS fork stub and the parent-walk miss; its Linux
  statement describes that hook, not all Linux process topologies.
- `docs/investigations/2026-11-10-run-0b292fa4-g82-CardFileGit-cancel-hook-windows-failure.md`
  at `origin/feat/card-task-e92f04ad`, SHA `468dbcebfe62202373f0a512cdef1c15c9a3b0ec`.
  Historical Windows fork/exec witness: walk leaked 4/4, attached job leaked 0/4.
  That evidence does not close the earlier-than-attachment window.
- `LandingGit.cs`, `OwnedProcessTree.cs`, `OwnedChildMarker.cs`,
  `RepositoryChildJournal.cs`, `CardFileRepository.cs`, and
  `tests/Antiphon.Tests/TestHelpers/DescendantLeakScope.cs` at the baseline.

## Decisions

**D-1 — One concrete owner, two platform implementations.** Add internal
`OwnedGitProcess` in `src/Antiphon.Server.Git/`, with `WindowsOwnedGitProcess`
and `LinuxOwnedGitProcess` implementation files. Keep the existing namespace
where appropriate. The small Git assembly already depends on Observability,
not the server; it must not reference `RepositoryChildJournal` or Application.
The server owns journal decisions. The owner exposes root identity/exit,
stdin/stdout/stderr, an async root wait, `ProbeMembers` and bounded
`TerminateAndJoinAsync`. It owns all native resources. Reject separate fixes in
three callers: they have already diverged on membership, ordering and waiting.

**D-2 — Windows launch uses `PROC_THREAD_ATTRIBUTE_JOB_LIST`.** Create a private
unnamed job, then call `CreateProcessW` with `STARTUPINFOEX`, `JOB_LIST` and an
explicit `HANDLE_LIST`. Membership exists before the initial thread runs.
Windows 10/Server 2016 is the minimum for this path. Reject attach-after-start,
including attaching to a managed process immediately after `Process.Start`.
`CREATE_SUSPENDED` / assign / resume is a viable secondary implementation but
has more partial-failure and creator-death states; do not ship both paths or
silently fall back to uncontained start. Unsupported attributes/job nesting
refuse the launch with a sanitized error.

The repository runtime target is net9.0, despite its newer SDK. Its
[ProcessStartInfo API](https://raw.githubusercontent.com/dotnet/runtime/v9.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/ProcessStartInfo.cs)
has no job-list or suspended-start parameter. Newer runtime/main APIs are not
available merely because the SDK is newer. Microsoft documents job-list and
handle-list attributes in
[UpdateProcThreadAttribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute),
and describes assignment before execution in
[its job creation explanation](https://devblogs.microsoft.com/oldnewthing/20230209-00/?p=107812).

**D-3 — Explicit termination, with crash recovery preserved.** Production jobs
have no breakaway and no `KILL_ON_JOB_CLOSE`. A cancellation calls
`TerminateJobObject` before any parent-tree fallback and then queries active
membership until zero; a successful API return alone is not an empty verdict.
A worker crash still leaves journaled children for existing explicit recovery.
Disposal closes handles and never substitutes for cancellation/join. A test's
outer safety job may use kill-on-close, and must remain independent of the
production owner. Reject a new setting that changes crash semantics by caller.

**D-4 — Linux uses exact environment membership.** Keep
`ANTIPHON_REPOSITORY_CHILD=<random invocation value>` set before managed start.
Enumerate the current PID namespace's `/proc`, collect exact NUL-delimited
matches, revalidate identity and marker immediately before signaling, kill the
matched set, await death and repeat until a complete empty pass or the shared
deadline. Reparenting and `setsid` do not remove an inherited environment.
Reuse the current start-time/hidepid/unreadable-environment rules rather than
the test helper's optimistic handling of read failures.

This is cooperative descendant containment, not a sandbox: `execve` with a
replacement environment can discard the marker; a hostile same-UID process
can copy it. `/proc/<pid>/environ` describes the initial environment for the
current exec, not every subsequent environment edit
([Linux manual](https://man7.org/linux/man-pages/man5/proc_pid_environ.5.html)).
Keep the existing ancestry kill for reachable unmarked descendants; never
claim detection of a marker-stripped, reparented descendant. Changing that
trust boundary requires a delegated cgroup or a separate supervisor design.

**D-5 — Linux signal identity follows the deployed baseline.** Do not assume a
managed `Process` pins a Linux PID against reuse. Record raw `/proc/stat` start
ticks, read them afresh before signaling, re-read the exact marker, and reject
changed, missing or unreadable identities. On the observed 4.15 kernel there
is still a check-to-signal PID-reuse interval; document it explicitly as the
existing best-effort Linux identity model, not atomic identity proof. Unit
tests must inject replacement at each observable boundary and assert no
signal. Do not broaden to `pkill`, command-line matches or negative PGIDs.
Pidfds would remove this residual race but require a newer kernel
([pidfd_send_signal](https://man7.org/linux/man-pages/man2/pidfd_send_signal.2.html));
adding that optional optimization is deferred, not a prerequisite hidden in
this implementation.

**D-6 — Reject cgroups and process groups for this slice.** A delegated private
cgroup would provide stronger membership and group killing, but there is no
declared writable subtree and the observed kernel cannot provide
`cgroup.kill`. Never write a container-wide cgroup. A process group is cheap
but a hook can call `setsid`; late `setpgid` also introduces an attachment
race. A subreaper changes process-wide wait ownership and requires a launch
supervisor. Those are larger deployment changes. The
[kernel cgroup documentation](https://docs.kernel.org/admin-guide/cgroup-v2.html)
is the owner of the stronger alternative, not evidence it is provisioned here.

**D-7 — Preserve public behavior, strengthen cleanup evidence.** Keep caller
cancellation as `OperationCanceledException`, LandingGit budget expiry as
`git_timeout`, card-file timeout as sanitized `git_error`, existing identity
configuration and Git auto-maintenance/optional-lock environment settings.
No DTO, route, database migration, journal schema or recovery-script change is
planned. Normal LandingGit success continues to wait for genuine output EOF
even past the command budget (C1076's explicit contract). After that EOF,
dispose only after bounded cleanup of any remaining owned members; a failed
cleanup retains the journal and reports failure, never a successful safe
release. Do not cancel the output readers to manufacture EOF.

**D-8 — Scope is the three named launch sites.** Migrate
`LandingGit.ExecuteProcessAsync`, `LandingGit.RunGitInputBytesAsync`, and
`CardFileRepository.RunGitInputAsync`. Leave other server Git spawns,
verification processes, session/Pty launchers and test harness owners for
separate work. Keep `OwnedProcessTree` as a constrained ancestry backstop;
it is not the authoritative empty-membership proof. No session input-wait
behavior is added or changed.

**D-9 — Fold verification into this Plan.** The brief explicitly requests a
closed executable checkpoint table and `next: code`. The choices above are
implementation defaults, not unanswered approval questions. The Windows
native rows are a completion gate; Linux passes cannot substitute for them.

## Implementation contract

### Owner and resource lifetime

`OwnedGitProcess` is a concrete internal resource owner, with narrow injectable
OS operations only at the external-I/O boundary. It has a single immutable
root identity and marker and a monotonic lifecycle: prepared, started,
terminating, joined or unconfirmed, disposed. Concurrent cancellation and
timeout share one termination task. A second dispose/terminate is harmless.
Use safe handles; do not keep owning raw `IntPtr` fields without a cleanup
owner. A cleanup result includes root-exited, members-empty/live/unknown,
signals attempted, survivors, duration and fixed reason codes. It does not
include command lines, environment values, tokens or stderr.

On Windows the owner implements root wait/start-time/exit-code using its
original process handle and builds its own redirected streams. A
`Process.GetProcessById` object cannot supply native-created redirected
streams. Do not patch private `Process` fields or use reflection. Replace the
internal/test launch seam with an owner-returning seam; adapt the journal's
root-exited call internally without changing its durable schema. Preserve
existing constructor overloads used by `RepositoryLeaseCrashWorker`.

The native launcher must implement and test: executable resolution equivalent
to the supported Git executable setting (including a path with spaces), argv
escaping for empty strings/quotes/backslashes/Unicode, explicit cwd, Unicode
environment block, UTF-8 text without a stdin BOM, byte-exact stdin/stdout for
`update-ref --stdin`, safe pipe construction, and failure cleanup after every
native allocation. Reject unsupported credential/shell/verb options before
creation. Use the original process handle to query and wait; close the primary
thread handle once creation succeeds. Close the parent's copies of child pipe
ends promptly so EOF remains meaningful. Only the intended std handles are
inheritable; the job, journal, repository lease and unrelated server handles
are not inherited. Keep attribute buffers alive through creation, then free
them. Follow the documented
[CreateProcessW contract](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw).

Match current `CreateNoWindow=true` behavior initially. Do **not** introduce
`DETACHED_PROCESS`, `CREATE_NEW_PROCESS_GROUP`, console-control kills or
breakaway flags. Git for Windows may allocate/use a console through its
launcher. Native proof must cover the normal `cmd/git.exe` launcher, its real
mingw child, MSYS hook and console-dependent `ping`; do not bypass the launcher
to make the test pass. A changed console recipe needs a documented failing
case and a separately measured correction.

For Linux, the shared owner wraps `Process.Start` and its streams. Apply the
marker only to `ProcessStartInfo.Environment`, never the server environment.
Capture the root's raw start ticks when available; a fast exited root leaves
an unknown lower bound, which is conservative. Scan at most 16,384 PID entries
and retain at most 4,096 positive members per pass (provisional bounds). Cap
each environment read at 2 MiB and the sweep by its remaining time; truncation,
permission denial on a possible descendant, hidden tables and exhausted bounds
produce Unknown, never Empty. Do not emit environment bytes. Exclude self and
PID 1, reject malformed/nonpositive IDs, handle disappearance as gone, and
require non-zombie liveness. Recheck after signaling; include children born
during earlier passes. One complete empty scan after all positively known
members are dead is sufficient within the cooperative-marker contract. An
unknown observation never authorizes a kill of an unproven process.

### Caller integration and fence release

The launch sequence is: prepare start info and journal intent; check admission;
create containment and launch; record the root identity; begin/retain real
stream readers; await the operation. A failure known to have created no child
can remove the intent. Any ambiguous post-creation failure runs owned cleanup
and leaves the intent unless root exit and empty membership are proven.

On cancellation/error, stop the owned set before draining. Windows uses one
job termination request; Linux signals the root and positively identified
members and repeats the marker census. A tree snapshot may supplement Linux
membership but its zero count is not the new owner's empty verdict. Use one
cleanup deadline, independent of the canceled request token: initially six
seconds, matching the current maximum three two-second LandingGit grace
segments. Do not restart that allowance per member, pass or stream. Record
termination, root wait, membership and drain phases separately.

Release a mutating LandingGit journal only after root exit and Empty. If the
pipes are also drained, close readers and owner. If only a proven unowned pipe
holder remains, retain readers/process resources in the existing EOF observer
but allow journal removal. If Live or Unknown, keep the journal even if all
streams closed; EOF alone must no longer run `StreamsClosed`. Transfer the
owner along with the streams to any continuation instead of closing the job
in the caller's `using`. The continuation gets one bounded final observation;
otherwise retain durable recovery evidence and close local handles without
claiming release. No indefinite retry task or new sweeper is introduced.

CardFileRepository uses the same awaited cleanup before returning cancellation
or a failure. Remove its duplicate `TryKill`. Keep `GitProcessGate` held across
the operation and its bounded cleanup (its current `using` inside the try
unwinds before the catch). A failed/unconfirmed cleanup is logged with a fixed
reason and cannot return commit success. This plan does not invent a durable
card-file admission journal or claim that the process gate is a persistent
repository fence.

### Compatibility and existing tests affected

Owner: [CARD-1782 impacted classes](../../testing-and-build.md#impacted-classes-card-1782)
and `tests/impacted-classes.json`. The planned server paths select
`repository-lease-reflected-constructors`, `repository-lease-windows-boundary`
and `server-git-start-sites`. New test limiters/phases select
`spawn-limit-rosters`, `timing-labels` and `test-lane-category`.
Migrations: **none**. Public DTOs/routes: **none**. Remote script functions:
**none**. Statement budgets: **none**. Operation labels: reuse recognized
`spawn`, `process`, `clone` and cleanup labels after checking the timing
vocabulary; run `TestTimingOperationLabelTests` for new phase call sites.

Change `LandingGitTests` ownership seams to control the new member probe and
termination independently. Keep C1076 unknown-environment, early/late job,
stranger-holder, live-root, normal-success EOF and stdin-break assertions.
Where an owned orphan used to remain alive, inject a refused/unknown cleanup
to preserve the fence test, and separately assert successful cleanup in the
new end-to-end witness. Do not delete the test because cleanup now succeeds.

Update launch overrides in `LandingGitFixture`, `LandHalfResetFixture`,
`RepositoryMutationLeaseTests`, `LandingGitTests`, `GitPushUrlCacheTests`,
`InProcessObservationPinTests`, `LandingGitProcessSavingTests`,
`LandingSourceOptionalLockTests`, and `ServerImagePushCredentialTests`.
Keep their argument/environment observation and deliberate start-failure
behavior. Teach `ServerGitProcessTests` the new launch spelling while retaining
all thirteen guarded start sites and its negative scanner examples. Do not
weaken the scanner to accept arbitrary unknown helpers. Keep repository
recovery/constructor reflection tests and C24's deliberate worker-only kill.

The read-only planned-path selector returned 65 classes across three projects,
plus Windows-only `RepositoryLeaseWindowsBoundaryTests`; it also reported
capped-path debt (39 for LandingGit and 24 for LandingGitFixture). These counts
are selection facts, not execution counts. The direct and selected ordinary
classes are listed in the table below; capped reverse-dependency debt remains
explicitly owed to the nightly/rc backstop under CARD-1820, not silently green.
Before each Code/Review dispatch, rerun the selector on the actual base..tip,
retain `owed`, and reconcile any newly selected class into a committed exact
checkpoint row before execution. This is a manifest amendment, not permission
for an unlisted broad run.

## Slices

Each authoring dispatch targets 30–60 minutes including its local checkpoint;
split at the named boundaries if native work exceeds that estimate. Keep one
assigned branch per task. Commit/push red witnesses before fixing them, and
commit/push each completed slice before long verification. Red intermediate
commits are not landable green slices.

| Slice | Files and work | Verification / completion |
|---|---|---|
| S1, 40–55 min | Add `src/Antiphon.Server.Git/OwnedGitProcess.cs` lifecycle/result types and Linux managed-stream adapter; add `tests/Antiphon.Server.Git.Tests/OwnedGitProcessContractTests.cs`. No caller migration yet. | CP-1: lifecycle, exactly-once close, launch/no-child/ambiguous distinction, deadline and tri-state contract. Pure unit tests; no spawning limiter needed here. |
| S2, 40–60 min | Add `LinuxOwnedGitProcess.cs`; extract exact marker census from `OwnedChildMarker.cs` without a server dependency. Add `LinuxOwnedGitProcessTests.cs` and `OwnedProcessContainmentFixture.cs` under `Antiphon.Tests/Infrastructure` and `TestHelpers`. | CP-2: real orphan and late fork; injected unreadable/truncated scans, boundary marker matching, start-time change, bounds and unrelated survivor. |
| S3a, 40–60 min | Add Windows native structs/safe handles, argv/environment/pipe transport in `WindowsOwnedGitProcess.cs` and `WindowsOwnedGitLaunch.cs`; extend unit contract tests. | CP-3: argument/environment encoding and resource-failure cases. Native launch not yet production-selected. |
| S3b, 45–60 min, Windows | Complete JOB_LIST launch and empty-job join; add `WindowsOwnedGitProcessTests.cs` in `Antiphon.Tests/Infrastructure`. | CP-4: creation-before-publish child, nested jobs, immediate exit, startup failure, pipes, handles, console and explicit lifetime. Native proof required. |
| S4a, 45–60 min | Migrate both LandingGit launch sites, `RepositoryChildJournal` internal root observation and all override files listed above. Add `OwnedGitLandingCancellationTests.cs`. Replace marker implementation with the shared owner or remove it after all references move. | CP-5/6 Linux and CP-7/8 Windows: committed red against old launcher/release, then green; cancellation/timeout, byte stdin, closed-stream orphan, stranger and unknown fence. |
| S4b, 30–60 min | Finish existing LandingGit seam adaptation and fixture/source-scanner compatibility; no assertion relaxation. | CP-9–11: existing LandingGit, lease/crash, ownership and Windows boundary regressions. |
| S5, 40–60 min | Migrate `CardFileRepository.cs`; add `OwnedGitCardFileCancellationTests.cs`; preserve existing `CardFileGitFailureAcceptanceTests`. | CP-12/13 Linux and CP-14/15 Windows: committed red then green; real Git cancellation/timeout and g82's eight internal attempts. |
| E1–E8, 30–60 min each maximum | Verification-only slice groups at the integrated source tip; no production edits while a run is live. Existing dependent classes, source guards and two small Git projects. | CP-16–25. Separate runs by `After`; do not execute the entire table as one batch. These are required ordinary coverage, not repeated full-suite passes. |

The shared helpers fit the already-extracted Git project (CARD-1546); do not
create another production project or import server types into it. Test-only
native witnesses remain in Antiphon.Tests and use its assembly-local
`ParallelLimiter<ProcessSpawnLimit>`. They run serially with respect to other
checkpoint rows; never co-schedule Pty/FakeClaude suites with these witnesses.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards)
and [the full rules](../../test-performance-standards.md). This Plan runs no
builds or tests; its checks are source/manifest checks. All implementation
timings below are **provisional** unless explicitly identified as historical.
There is no measured p95 yet for the new helper, rows, Windows launch or full
class unions; do not relabel estimates as measurements.

| Measurement or phase | Evidence / budget |
|---|---|
| Existing cancellation journal cleanup | Source comment in V28 records loaded Linux p95 1.61 s from 30 cases, with 4 s test bound. Source does not retain the measurement SHA/run identity there; this is partial provenance, not a new-helper p95. Preserve that historical 4 s assertion until actual evidence justifies a change. |
| Historical Windows g82 | Investigation above, run 0b292fa4 at `56e96eabf698e9c1fcd462836ad32ed1fb259e83`: group 358 results, run wall 214.96 s, build 573 s, group per-test p95 12.25 s; cancel 5.554 s, timeout 25.48 s. Different selection/load; not a p95 for these new rows. |
| New native test body | Expected 2–5 s Linux / 3–10 s Windows; 10 s Linux / 20 s Windows provisional watchdog per witness, excluding explicitly timed shared fixture setup. Readiness and teardown consume that budget, not fresh allowances. |
| Cleanup | Expected <=2 s on normal members; six-second product cap preserves the old maximum grace envelope, provisional under CARD-1850/1852. Independent fixture rescue <=5 s and any rescue fails ordinary proof. Existing g82 hook-death bound remains 15 s. |
| Setup | Use `GitTreeTemplate`/`LandingGitFixture` once-per-process template and clone per case. Expected clone/setup 0.2–2 s Linux / 0.5–4 s Windows. Fresh process/isolated hook is necessary because creation and parent death are the subject. No AppHost, new DB container or migrations for native witnesses. Dependent DB tests use the existing assembly DB template/clone setup once per run. |
| Builds | Small Git project 0.5–1 min provisionally; Antiphon.Tests 2–4 min Linux, 5–10 min Windows cold. Keep slot wait separate. Historical 573 s Windows build warrants a provisional 20 min build cap, not a 20 min test-body cap. |
| Row execution | New helper/native rows 0.5–2 min; LandingGit/lease unions 2–6 min; application regression unions 2–6 min. Table estimates include a cold build only on the first row of each group. No slice run is expected to exceed 30 min. |
| Admission / wall | Use the tool's finite 45 min per-acquisition admission ceiling and report queue wait separately. This is queue allowance, not execution time. Do not retry exit 4 unleased. Hard wall = selected execution allowance + 45 min; account queued siblings without extending a running driver's deadline. |

Record TRX per-case duration and exact roster, source SHA, OS, runtime, Git
version, run ID and ambient host load. Keep phase evidence in ignored
`.antiphon/checkpoints/<run-id>/` and the configured timing root. Capture
template build/clone, launch, readiness, stat/environ reads, signaling, root
wait, membership rescan, stream drain, journal delete and fixture teardown.
Use fixed operation names with `TestTiming` and production
ActivitySource/structured durations; command/environment contents stay out.
Record uninstrumented phases as unknown and wall-minus-attributed spans as
remainder. AppHost/container/migrations are not-used for the native selection,
not fabricated zero-duration spans. Build and slot timing stay separate.

Initial explicit CLI budgets are `--row-timeout` = twice the largest selected
row estimate, `--total-timeout` = twice the sum of selected estimates, and
`--hard-wall-timeout` = that execution allowance plus 45 minutes. Use the
Windows estimate on Windows. These are provisional 2x planning estimates,
not measured p95. Supply `--total-timeout-override-reason` with
`CARD-1850/1852 provisional bounded slice; excludes importer extra 10-minute margin`.
The cold-build coupling can make the tool's build ceiling larger than its
20-minute phase budget; report that separately rather than pretending the
driver enforces an independent build deadline. Per-test watchdogs still apply.

For the first ordinary execution per lane, retain each parameterized case as
a timing sample. At most two further unchanged proof executions may collect
missing samples, within the repository's normal/loaded repeat limit. Use the
small-sample p95 only as provisional; use existing comparable history when
available. Before Final Review, record measured samples and set per-test and
row deadlines near 2x that p95, rounded up for scheduling granularity. Retain
the six-second cleanup provisional exception on CARD-1850/1852 until measured;
do not quietly loosen V28 or g82 assertions. The importer defaults (15-minute
floor, 3x estimate) are not measurement. Use explicit row deadlines and, where
a shorter execution budget is below its derived floor, a recorded
`totalTimeoutOverrideReason` naming these bounded groups. Build and execution
deadlines must stay distinct; where tool coupling prevents this, report the
documented 20-minute cold-build exception, while test-level deadlines remain
effective. Any expected/observed row or run over 30 minutes goes to Debug with
timings and source identity before a retry. Never run the E groups together
and call their aggregate cost one healthy short run.

## Verification design

### Witnesses and independent cleanup

V-1 (unit): tri-state membership, exact marker field boundaries, fresh start
identity, hidden/unreadable/truncated proc data, exhausted bounds, concurrent
terminate, known no-child versus ambiguous creation, timeout accounting,
resource cleanup and safe argument/environment encoding. Injected OS failures
must drive caller outcomes, not assertions on implementation constants.

V-2 (Linux native): a hook starts an intermediate that forks a survivor, calls
`setsid` in the survivor where available, execs a long-lived process, and exits.
Use file/pipe barriers rather than a guessed delay. Confirm the intermediate
exited and the survivor is reparented before cancellation. The survivor writes
its PID plus kernel start ticks and holds an independently controlled release
barrier. Parameterize inherited output and all streams closed. Cancellation
must leave it dead and journal removal must occur only afterwards. Keep an
unmarked sibling and a differently marked invocation alive. Add a child born
during the sweep using a controlled boundary; it must be included by a later
pass. A no-pidfd legacy lane must exercise the actual signal path.

V-3 (Windows native): use the actual Git launcher and fork-then-exec MSYS hook,
with an intermediate that is demonstrably dead while a grandchild remains
live. Record **Windows** PIDs via `/proc/$$/winpid`, not MSYS `$$`; retain
process handles and start times. The fixture deliberately holds the launcher
return/old attachment boundary until this topology exists, making the old
attach-after-start path red. The new JOB_LIST path must already contain the
grandchild at that same boundary. Query membership and verify all independent
handles signal after termination, including when the root already exited.
The inner hook must not rely on `exec sleep` alone; that was a false witness
in the investigation. Verify the needed MSYS executable, or use the known
`sh` fork/exec plus `ping` barrier recipe when `sleep` is unavailable.

V-4 (native launch): transport round-trips empty/quoted/Unicode arguments,
cwd, environment overrides/removals, UTF-8 text and binary stdin; root exits
immediately; child inherits only intended handles; descendant holds output;
large stdout/stderr drains concurrently. Force create/job/pipe/wait failures
and nested-job refusal; no uncontained process may run after refusal. An
outer test-owned job confirms explicit production dispose is not kill-on-close;
a separate explicit terminate confirms members become zero. Preserve C24 on
both platforms. g82's console-dependent spawning-hook test stays eight
internal rounds, one TUnit result.

V-5 (LandingGit): cancellation and budget expiry through both text and binary
stdin paths; orphan holds pipes and orphan closes pipes; normal success waits
for real EOF; failed signal/probe keeps durable fence even after EOF; stranger
pipe holder does not keep it once owned membership is proven empty. Check
ordering using repository lease admission while cleanup is gated, not merely
a final missing journal. Do not make a test-owned sweep the cleanup under test.

V-6 (CardFile): cancellation and timeout use the shared owner, kill the orphan
and await cleanup, preserve HEAD and sanitized errors, leave unrelated
processes alive and allow retry after the fixture fault is removed. Include
normal input-bearing commit operations and cleanup exceptions. Preserve the
existing eight-attempt g82 witness and its absent failure markers.

All native witnesses use an outer independent `DescendantLeakScope` only as a
safety net. Because that scope itself attaches after start on Windows, first
start a fixture launcher blocked on a nonce barrier, adopt it into the outer
job, **then** release it to run the production path. Never assert success after
scope disposal or rescue. Linux uses its independent test token, not the
production token, for leak detection. The observer asserts death before
releasing the survivor's fallback barrier; a leaked survivor is rescued and
joined in `finally`, with the ordinary assertion still failed. No live session,
qualification process, production repo or volume is involved.

### Regressions, lanes and receipt rules

R-1: `OwnedProcessTreeTests` remains an identity/backstop test suite.
R-2: all LandingGit, lease/crash and journal/EOF tests named in CP-9–11.
R-3: card-file failure/privacy/ignore/retry callers.
R-4: fixture launch observers, Git environment/source scanner and credentials.
R-5: CARD-1782 selected downstream classes and mandatory guard union.

Both `GET /api/runner-defaults` and `GET /api/session-runners` were read during
Plan. The default was a dispatch-eligible Linux runner; an eligible Windows
lane was also advertised. This is a transient observation, not a host pin.
Resolve both again at dispatch. Use no `-Runner`; use `-Platform Windows` for
Windows rows and `-Platform Linux` for native Linux rows; portable rows omit
`-Platform`. `-Platform Any` unpins. Group names below declare the lane because
the checkpoint schema has no arbitrary Lane column. Do not embed a host path
or fleet location in a checkpoint command. Windows work must run in its own
assigned checkout; this container cannot provide native Windows proof.

The old-behavior red rows CP-5, CP-7, CP-12 and CP-14 each run one new exact
method after a test-only commit and before the caller fix. A valid red is the
expected survivor assertion, with the readiness/dead-parent prerequisites
passed; compile, fixture and zero-test errors are not red. If a migrated API
prevents an old caller compiling, keep the witness on the old API first, then
mechanically adapt it with the fix. The two independent green caller classes
each contain at least four native cases (cancel/timeout x inherited/closed
streams); additional cases cannot replace the named witness. CP-4 native
class has at least six cases; CP-2 at least eight; unit contract class at least
eight. Exact names of the baseline-red methods are fixed by the table.

Bootstrap the checkpoint tool once through the host slot gate, using native
default output, then release that lease before the tool takes row/build leases.
Invoke its built DLL to avoid holding an outer build lease throughout execution.
For example, S2 has five estimated minutes and an explicit ten-minute execution
allowance:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label owned-containment-tool -- dotnet build tools/Antiphon.Checkpoints
dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-11-12-card-1850-owned-process-containment-plan.md --after S2 --expected-source-sha <full-sha> --row-timeout 10m --total-timeout 10m --total-timeout-override-reason "CARD-1850/1852 provisional bounded slice; excludes importer extra 10-minute margin" --hard-wall-timeout 55m
```

Use `--rows` when selecting the red/green or OS subset. Continue tool `wait`
until exit is not 75; never end a task with a running executor. The tool takes
each row's build slot itself; no unleased fallback. Direct builds, if needed
to bootstrap the tool, use native default output and are reported as bootstrap
cost. Only checkpoint-owned outputs use the `bin-.../` paths below. Freeze
source for each run; retain unedited CHECKPOINT lines, fresh TRX, counts,
source/build provenance and the expected assertion from every red.

Validate actual classes/methods and nonzero execution from TRX. Each OR operand
below has parentheses and its own trailing `*`; literal pipes are escaped for
Markdown. Source suffix-collision audit must cover both new classes and the
baseline roster, then repeat after implementation. `Min` is a conservative
execution floor, not the final expected count. Every named class must appear;
OS-inapplicable skips are not native evidence. Windows-specific classes are
kept in Windows groups, including `AgentTaskLandHalfResetWindowsTests` even
though the current derived selector also emits it in its broad portable list.
Run evidence-diff over base..tip and conflict-free `git merge-tree --write-tree
origin/master HEAD` before a landable report; fetch first and never merge to
resolve it in the task branch.

### Checkpoints

One run per After group/OS, never one full-plan run. E groups are separate
verification dispatches. Reuse only an earlier build in the same After group.
All rows are serial to prevent native fixture interaction. A named red row's
exit 1 is expected evidence and never reported green; its matching green row
is required before that slice is complete. EstimatedMinutes are provisional
whole-row costs under the budget above.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Server.Git.Tests -> bin-c1850-s1-final/` | any-contract | `/*/*/OwnedGitProcessContractTests/*` | V-1 | OwnedGitProcessContractTests; all listed, 0 failed | 8 | 1 | 2 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1850-s2-green/` | linux-members | `/*/*/LinuxOwnedGitProcessTests/*` | V-1,V-2 | LinuxOwnedGitProcessTests; all listed, 0 failed | 8 | 5 | n/a | true |
| CP-S2-R1 | S2-red-contract | `tests/Antiphon.Server.Git.Tests -> bin-c1850-s2-red-contract/` | any-contract-red | `/*/*/OwnedGitProcessContractTests/Unexpected_launch_exception_is_ambiguous_and_cannot_start_again` | V-1,A2 | Expected unexpected-launch assertion red before fix | 1 | 1 | 2 | true |
| CP-S2-R2 | S2-red-pipes | `tests/Antiphon.Tests -> bin-c1850-s2-red-pipes/` | linux-pipes-red | `/*/*/LinuxOwnedGitProcessTests/Disposal_closes_all_used_streams` | V-1,A1 | Expected stdout still readable assertion red before fix | 1 | 5 | n/a | true |
| CP-S2-1 | S2-contract | `tests/Antiphon.Server.Git.Tests -> bin-c1850-s2-contract/` | any-contract | `/*/*/(GitIndexLockTests*)\|(OwnedGitProcessContractTests*)\|(ServerGitAssemblyTests*)/*` | V-1,R-5,A2-A4 | all 3 impacted classes, 0 failed | 3 | 1 | 2 | true |
| CP-S2-2 | S2 | CP-2 | linux-legacy | `/*/*/(CardFileRepositoryTryKillTests*)\|(LandingGitTests*)\|(ServerGitProcessTests*)/*` | R-2,R-4,R-5 | all 3 actual-impact classes, 0 failed | 3 | 6 | n/a | true |
| CP-S2-3 | S2 | CP-2 | any-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(TestTimingOperationLabelTests*)\|(DerivedImpactIndexTests*)/*` | R-5 | mandatory nine guards and selected timing/spawn/index guards, 0 failed | 12 | 6 | 10 | true |
| CP-S2-4 | S2 | CP-2 | any-platform-policy | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | R-5 | both selected platform policy methods, 0 failed | 2 | 1 | 2 | true |
| CP-S2-5 | S2-pty-roster | `tests/Antiphon.Agents.Pty.Tests -> bin-c1850-s2-pty/` | any-spawn-roster | `/*/*/ProcessSpawnLimitTests/*` | R-5 | selected assembly-local spawn roster, 0 failed | 1 | 2 | 3 | true |
| CP-S2-6 | S2-runner-roster | `tests/Antiphon.SessionRunner.Tests -> bin-c1850-s2-runner/` | any-spawn-roster | `/*/*/ProcessSpawnLimitTests/*` | R-5 | selected assembly-local spawn roster, 0 failed | 1 | 2 | 3 | true |
| CP-S2-7 | S2 | CP-2 | any-impact-consumers | `/*/*/(ImpactedClassesSelectorTests*)\|(LiveLaneRunnerTests*)\|(NightlyCoreTests*)\|(ReleaseImpactEnvelopeTests*)\|(TimeoutAuditTests*)/*` | R-5 | all 5 portable actual policy/index consumers, 0 failed | 5 | 5 | 9 | true |
| CP-S2-8 | S2 | CP-2 | any-platform-impact | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)\|(C1210_Native_linux_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)\|(C1210_Audit_rejects_empty_or_incomplete_outcomes*)\|(C1210_Host_roster_matches_windows_shaped_observation*)\|(C1226_Pin_inspection_methods_skip_off_linux*)` | R-5 | all 6 portable methods from actual impacted class, 0 failed | 6 | 1 | 2 | true |
| CP-S2-9 | S2-windows-policy | `tests/Antiphon.Tests -> bin-c1850-s2-windows-policy/` | windows-platform-impact | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Windows_linux_only_outcomes_are_exact` | R-5 | native Windows-only method; orchestrator owed, never a passed Linux skip | 1 | 2 | 2 | true |
| CP-S3-R | S3a-red | `tests/Antiphon.Server.Git.Tests -> bin-c1850-s3a-red/` | any-native-contract-red | `/*/*/OwnedGitProcessContractTests/Windows_arguments_preserve_empty_quotes_backslashes_and_unicode` | V-1,V-4 | Expected missing Windows transport assertion red before implementation | 8 | 1 | 2 | true |
| CP-3 | S3a | `tests/Antiphon.Server.Git.Tests -> bin-c1850-s3a/` | any-native-contract | `/*/*/OwnedGitProcessContractTests/*` | V-1,V-4 | OwnedGitProcessContractTests; all listed, 0 failed | 8 | 1 | 2 | true |
| CP-S3-1 | S3a | CP-3 | any-git-impact | `/*/*/(GitIndexLockTests*)\|(ServerGitAssemblyTests*)/*` | R-5 | both actual-impact Git classes, 0 failed | 2 | 1 | 2 | true |
| CP-S3-2 | S3a-guards | `tests/Antiphon.Tests -> bin-c1850-s3a-guards/` | any-guards | `/*/*/(ServerGitProcessTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-4,R-5 | all 11 named classes and mandatory guard union, 0 failed | 11 | 10 | 15 | true |
| CP-S3-3 | S3a-guards | `tests/Antiphon.Tests -> bin-c1850-s3a-guards/` | any-index-impact | `/*/*/ReleaseImpactEnvelopeTests/*` | R-5 | all four actual-index-consumer methods, 0 failed | 4 | 3 | 5 | true |
| CP-4 | S3b | `tests/Antiphon.Tests -> bin-c1850-s3b/` | windows-native | `/*/*/WindowsOwnedGitProcessTests/*` | V-3,V-4 | WindowsOwnedGitProcessTests; all listed, 0 failed | 6 | 10 | 10 | true |
| CP-5 | S4a-red-linux | `tests/Antiphon.Tests -> bin-c1850-s4rl/` | linux-land-red | `/*/*/OwnedGitLandingCancellationTests/Cancel_kills_dead_parent_orphan` | V-2,V-5 | Expected survivor assertion failure; prerequisites passed | 1 | 5 | n/a | true |
| CP-6 | S4a-linux | `tests/Antiphon.Tests -> bin-c1850-s4gl/` | linux-land-green | `/*/*/OwnedGitLandingCancellationTests/*` | V-2,V-5 | OwnedGitLandingCancellationTests; all listed, 0 failed | 4 | 6 | n/a | true |
| CP-7 | S4a-red-windows | `tests/Antiphon.Tests -> bin-c1850-s4rw/` | windows-land-red | `/*/*/OwnedGitLandingCancellationTests/Cancel_kills_dead_parent_orphan` | V-3,V-5 | Expected survivor assertion failure; prerequisites passed | 1 | 10 | 10 | true |
| CP-8 | S4a-windows | `tests/Antiphon.Tests -> bin-c1850-s4gw/` | windows-land-green | `/*/*/OwnedGitLandingCancellationTests/*` | V-3,V-5 | OwnedGitLandingCancellationTests; all listed, 0 failed | 4 | 11 | 11 | true |
| CP-9 | S4b-linux | `tests/Antiphon.Tests -> bin-c1850-s4b/` | linux-land-regression | `/*/*/(LandingGitTests*)\|(RepositoryMutationLeaseTests*)\|(RepositoryMutationLeaseOwnerTests*)\|(OwnedProcessTreeTests*)/*` | R-1,R-2 | all 4 named classes, 0 failed | 4 | 9 | n/a | true |
| CP-10 | S4b-windows | `tests/Antiphon.Tests -> bin-c1850-s4bw/` | windows-land-regression | `/*/*/(LandingGitTests*)\|(RepositoryMutationLeaseTests*)\|(RepositoryLeaseWindowsBoundaryTests*)/*` | R-2,V-4 | all 3 named classes, including C24 and both Windows boundary methods, 0 failed | 3 | 16 | 16 | true |
| CP-11 | S4b-linux | CP-9 | linux-journal | `/*/*/(RepositoryChildJournalInspectorTests*)\|(RepositoryChildRecoveryTests*)\|(RepositoryFenceObserverTests*)/*` | R-2 | all 3 named classes, 0 failed | 3 | 3 | n/a | true |
| CP-12 | S5-red-linux | `tests/Antiphon.Tests -> bin-c1850-s5rl/` | linux-card-red | `/*/*/OwnedGitCardFileCancellationTests/Cancel_kills_dead_parent_orphan` | V-2,V-6 | Expected survivor assertion failure; prerequisites passed | 1 | 5 | n/a | true |
| CP-13 | S5-linux | `tests/Antiphon.Tests -> bin-c1850-s5gl/` | linux-card-green | `/*/*/(OwnedGitCardFileCancellationTests*)\|(CardFileGitFailureAcceptanceTests*)/*` | V-6,R-3 | both classes; 8 internal spawning rounds retained, 0 failed | 12 | 6 | n/a | true |
| CP-14 | S5-red-windows | `tests/Antiphon.Tests -> bin-c1850-s5rw/` | windows-card-red | `/*/*/OwnedGitCardFileCancellationTests/Cancel_kills_dead_parent_orphan` | V-3,V-6 | Expected survivor assertion failure; prerequisites passed | 1 | 10 | 10 | true |
| CP-15 | S5-windows | `tests/Antiphon.Tests -> bin-c1850-s5gw/` | windows-card-green | `/*/*/(OwnedGitCardFileCancellationTests*)\|(CardFileGitFailureAcceptanceTests*)/*` | V-4,V-6,R-3 | both classes; g82 spawning test 8/8 internal rounds, 0 failed | 12 | 12 | 12 | true |
| CP-16 | E1 | `tests/Antiphon.Tests -> bin-c1850-e1/` | any-launch-callers | `/*/*/(GitPushUrlCacheTests*)\|(InProcessObservationPinTests*)\|(LandingGitProcessSavingTests*)\|(LandingGitBatchingTests*)\|(LandingGitProfileTests*)\|(LandingSourceOptionalLockTests*)\|(ServerImagePushCredentialTests*)\|(ServerGitProcessTests*)/*` | R-4 | all 8 named classes, 0 failed | 8 | 10 | 17 | true |
| CP-17 | E2 | `tests/Antiphon.Tests -> bin-c1850-e2/` | any-card-callers | `/*/*/(CardFileBoardLookupTests*)\|(CardFileIgnoreAcceptanceTests*)\|(CardFileIgnoreTests*)\|(CardFilePrivacyGitTests*)\|(CardFilePrivacyRecoveryTests*)\|(CardReviewServiceIntegrationTests*)\|(CardTaskFileServiceTests*)/*` | R-3,R-5 | all 7 named classes, 0 failed | 7 | 10 | 17 | true |
| CP-18 | E3a | `tests/Antiphon.Tests -> bin-c1850-e3a/` | any-land-callers-a-1 | `/*/*/(AgentTaskLandAdoptionConcurrencyTests*)\|(AgentTaskLandBareRepositoryCleanupTests*)\|(AgentTaskLandHalfResetTests*)\|(AgentTaskLandNestedRepositoryCleanupTests*)/*` | R-4,R-5 | all 4 named classes, 0 failed | 4 | 9 | 14 | true |
| CP-18b | E3b | `tests/Antiphon.Tests -> bin-c1850-e3b/` | any-land-callers-a-2 | `/*/*/(AgentTaskLandPublicationTests*)/*` | R-4,R-5 | all 1 named classes, 0 failed | 1 | 2 | 4 | true |
| CP-19 | E4a | `tests/Antiphon.Tests -> bin-c1850-e4a/` | any-land-callers-b-1 | `/*/*/(AgentTaskLandRegistrationSafetyTests*)\|(AgentTaskLandSourceFreshnessTests*)\|(AgentTaskLandTargetRaceTests*)\|(LandNonCodeSkipTests*)/*` | R-5 | all 4 named classes, 0 failed | 4 | 6 | 10 | true |
| CP-19b | E4b | `tests/Antiphon.Tests -> bin-c1850-e4b/` | any-land-callers-b-2 | `/*/*/(LandRecoveryCheckoutTests*)\|(LandRecoveryIgnoredNestedRepositoryTests*)\|(LandRecoveryIgnoredObstructionTests*)/*` | R-5 | all 3 named classes, 0 failed | 3 | 5 | 8 | true |
| CP-20 | E5 | `tests/Antiphon.Tests -> bin-c1850-e5/` | any-worktree-callers | `/*/*/(AgentTaskWorktreeBaseCreateTests*)\|(AgentTaskWorktreeBaseResolverTests*)\|(AgentTaskWorktreeBaseSyncDebtTests*)\|(DefaultRunnerPinTests*)\|(DelegationWorktreeTests*)\|(DispatchBaseNotificationTests*)\|(DispatchSiblingInspectionTests*)\|(StartRefAvailabilityTests*)\|(WorktreeBaseSelectionTests*)\|(WorktreeDefaultCallerTests*)\|(WorktreeRemovalAuthorityTests*)/*` | R-5 | all 11 named classes, 0 failed | 11 | 12 | 19 | true |
| CP-21 | E6a | `tests/Antiphon.Tests -> bin-c1850-e6a/` | any-git-callers-1 | `/*/*/(AdvisoryChangePolicyTests*)\|(C1708ReflogSalvageTests*)\|(GatedCommitServiceTests*)\|(GitTreeTemplateTests*)\|(LandRefRetentionTests*)\|(LandSourceIdentityTests*)\|(LandVerificationWindowNativeTests*)\|(LandWarmArtifactsTests*)/*` | R-5 | all 8 named classes, 0 failed | 8 | 8 | 13 | true |
| CP-21b | E6b | `tests/Antiphon.Tests -> bin-c1850-e6b/` | any-git-callers-2 | `/*/*/(LandingRegistrationFreshnessTests*)\|(LandingSourceFreshnessTests*)\|(RepositoryMaintenanceTests*)\|(TaskProgressGitTests*)/*` | R-5 | all 4 named classes, 0 failed | 4 | 4 | 6 | true |
| CP-22 | E7 | `tests/Antiphon.Tests -> bin-c1850-e7/` | any-guards | `/*/*/(RunnerAlarmCoordinatorTests*)\|(RunnerAlarmHostedServiceTests*)\|(RunnerAlarmWiringTests*)\|(ImpactedClassesSelectorTests*)\|(ProcessSpawnLimitTests*)\|(TestTimingOperationLabelTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-5 | all 15 named classes; mandatory 9-class guard union included, 0 failed | 15 | 8 | 15 | true |
| CP-23 | E8 | `tests/Antiphon.Server.Git.Tests -> bin-c1850-e8g/` | any-git-project | `/*/*/(GitIndexLockTests*)\|(ServerGitAssemblyTests*)\|(OwnedGitProcessContractTests*)/*` | V-1,R-5 | all 3 named classes, 0 failed | 3 | 2 | 3 | true |
| CP-24 | E8 | `tests/Antiphon.GitInProcess.Tests -> bin-c1850-e8i/` | any-inprocess-project | `/*/*/(InProcessGitCommandsTests*)\|(LibGit2VsGitDifferentialTests*)\|(LibGit2VsGitWorktreeListTests*)/*` | R-5 | all 3 named classes, 0 failed | 3 | 3 | 5 | true |
| CP-25 | E8-windows | `tests/Antiphon.Tests -> bin-c1850-e8w/` | windows-adoption | `/*/*/AgentTaskLandHalfResetWindowsTests/*` | R-4,R-5 | AgentTaskLandHalfResetWindowsTests; all listed, 0 failed | 1 | 12 | 12 | true |
| CP-S1-2 | S1-adjacent | `tests/Antiphon.Server.Git.Tests -> bin-c1850-s1-adjacent-green/` | any-git-impact | `/*/*/(GitIndexLockTests*)\|(ServerGitAssemblyTests*)/*` | R-5 | both impacted classes, 0 failed | 2 | 1 | 2 | true |
| CP-S1-3 | S1-guards | `tests/Antiphon.Tests -> bin-c1850-s1-guards-final/` | any-guards | `/*/*/(ServerGitProcessTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | R-4,R-5 | all 10 classes, 0 failed | 10 | 6 | 10 | true |
| CP-S1-4 | S1-derived-adjacent | `tests/Antiphon.Tests -> bin-c1850-s1-derived-adjacent/` | any-derived-impact | `/*/*/(ReleaseImpactEnvelopeTests*)\|(TestLaneCategoryGuardTests*)/*` | R-5 | both classes including all 4 envelope methods, 0 failed | 5 | 6 | 10 | true |

S1 dispatch amendment (task 9124df63): CP-S1-2 covers the actual
`server-git-start-sites` impact selection in the small Git project and the
pre-existing project-wide frozen method census. Its first execution is an
expected census red after adding the S1 witness; the corrected census must
remain strict over the two migrated classes. CP-S1-3 covers the remaining
selected integration class and the mandatory batched source guards. Neither
row expands S1 into caller migration or native containment. Record every
original V/R outside S1 as deferred to its assigned slice, never passed.
S1 exposes a mandatory external membership-I/O port for the Linux stream
adapter; S2 supplies the bounded `/proc` census and signal implementation.
There is no default/optimistic membership provider or production selection
until that implementation exists. No Windows native behavior is selected in
S1; S3's structural and native obligations remain owed by the orchestrator.
The first CP-S1-3 execution at `fe666cd432f24e1f0c2f06938ff9f05082b3844a`
exhausted its unchanged 12-minute execution budget without TRX. That attempt is
incomplete timeout evidence, not an assertion red or a pass. The required
regeneration script subsequently added eight lines to the committed impact
index and built its native-output driver prerequisite. Rerun the same exact
ten-class filter and budgets using the fresh final output path; preserve the
original receipt and do not attribute the timeout to a particular test without
per-case evidence. No test assertion or timeout changes are authorized here.

S1 recovery amendment (task ace1a342): CP-18/18b, CP-19/19b and
CP-21/21b partition their original exact class unions into independent E3a/E3b,
E4a/E4b and E6a/E6b groups, with at most four land classes per row. Their
aggregate Linux/Windows estimates remain 11/18, 11/18 and 12/19 minutes.
No class coverage was removed. These future integration rows remain deferred
outside this S1 dispatch. The three original S1 rows, timeouts and assertions
are unchanged. The current-master index regeneration built its driver first
and produced byte-identical bytes; the separate regeneration commit records
that outcome without changing the generated index. The actual base..tip
selector additionally names ReleaseImpactEnvelopeTests, bound in CP-S1-4.
CP-S1-4 also includes TestLaneCategoryGuardTests to complete the standing
nine-class guard union without changing the exact CP-S1-3 filter. Its
provisional six-minute whole-row estimate includes a separate cold build,
four script/fixture methods and the source census; use a twelve-minute execution cap, then stop
for Debug if exceeded. The selector reports no capped-path debt or native
Windows filters for this S1 diff. Native Windows execution remains owed by
the orchestrator under S3 and later slices.

### Cost and completion

The table's portable/Linux/default cost column sums to 214 minutes across
independent authoring and verification slices, including required red runs and
cold builds. It is not authorization for a single 214-minute run. Most portable
regressions run once on the default lane; Windows-specific rows are separately
mandatory. `-ExpectAbout` for an individual authoring dispatch is its selected
row cost plus 25–45 minutes authoring; verification-only dispatches use only
their selected group costs. Every over-30-minute execution gets Debug under
R1, including a healthy but expensive selection.

Final acceptance requires both OS ordinary witnesses, the existing g82 eight
rounds, preserved crash fences, complete planned ordinary rows at their stated
source revisions, impacted-selection/capped-debt disclosure, clean source
receipts, and ordinary Review. A later Windows rc group-82 run can provide
additional evidence, but this task must not touch or wait on the live run named
in the brief. No deployment or current-host mutation is part of this Plan.

S2 execution amendment (Code task 0485dc70): the actual base..tip selector adds
CP-S2-1 through CP-S2-9. CP-S2-7 originally selected all six classes and ran
at 51f1b7f4c1877ce1fe70bd6ad04e5fed9935c9dd: one real pinned-input digest
failure and one intentional Windows skip. Its portable six-method subset now
has CP-S2-8; CP-S2-9 preserves the native obligation for the orchestrator.
No assertion or timeout was loosened. CP-2's first execution was 34/35 because
AnonymousPipeClientStream.CanRead remains true after close. The corrected
witness retains all three SafePipeHandles and asserts IsClosed plus rejected
I/O, rather than treating stream direction as liveness. A gated diagnostic
against the retained, source-verified S1 assembly from CP-S2-R2 found two open
handles after real owner disposal; it is baseline red evidence, not a mutant.
The original TRX/lines remain historical and are not relabeled green.
Fresh CP-2 and repaired impact rows use bin-c1850-s2-green/. Passing unchanged
rows are not repeated. The exact policy input digest refresh preserves the
live-lane sentinel and CRLF assertions.

S2 preliminary measurements: Linux kernel 4.15, UID 1654, /proc without hidepid,
SDK 10.0.404, runtime net9.0, Git 2.47.3. Run 20261113-084944-7a20 cold build
323.262 s; CP-2 execution 12.893 s with host startup 10.287 s and body wall
1.150 s. Six native topology/factory cases have maximum 0.394 s; the whole
35-case helper class has provisional per-case p95 0.104 s. These are single-run
small samples, not established p95. The six-second product cleanup exception
and ten-second native watchdog remain bounded CARD-1850 failure-path budgets
(root wait, readiness, independent rescue), not claims of measured steady-state
cost. No further unchanged repetitions are required after green. Guard row
CP-S2-3 measured 235.448 s; DerivedImpactIndexTests summed 160 s, consistent
with CARD-1876 A5's roughly 170-second history. Its new estimate is six minutes
with the cold build charged to CP-2 and an explicit ten-minute execution cap;
the original S1 historical costs/cap remain recorded unchanged. Other phases
are in ignored per-row phase JSONL; uninstrumented subphases/remainder are
unknown. No native witness uses AppHost, containers or database migrations.

S3a dispatch amendment (Code task 66a1f6d2): prerequisites S1 and S2 are on
master at the assigned base c38918255330caec88c35fda713dbddad221d5a1. This
slice prepares the Windows native transport; it creates no process and changes
no production factory/property selection. WindowsOwnedGitLaunch owns the job,
three pipe pairs, attribute list and redirected stream wrappers. The native
boundary resolves the executable with SearchPathW, clears inheritance on parent
pipe ends, and prepares JOB_LIST plus an exact three-handle HANDLE_LIST.
It does not add kill-on-close or breakaway. Attributes retain their value
buffers and safe-handle references until deletion. S3b must use this prepared
owner across creation, then release child ends and attributes, and keep the job
until root/membership cleanup finishes. No caller migration is authorized here.

CP-S3-R is the committed pre-implementation encoding red. CP-3 covers exact
wire values, removed/empty environment values, unsupported options, each
preparation-boundary failure, resource closure, BOM-free text and binary I/O.
Native allocation-internal failure injection, executable resolution, inherited
handle exclusion through actual CreateProcessW, root wait, membership, nesting,
console and lifetime behavior remain native S3b work (CP-4). Portable seams are
supplementary transport evidence; they do not close Windows execution.
V-1/V-4 portable S3a portions and R-4 source scanner/R-5 actual selection are
in scope. V-2/V-3/V-5/V-6, R-1/R-2/R-3, remaining R-4/R-5 E groups and all
Windows native portions remain deferred to their named slices, never passed.
No mutation PC is discharged; operator waiver remains recorded above.

CP-S3-1 adds the two impacted Git classes. CP-S3-2 batches the source scanner,
mandatory nine guards and index guard, with a cold Antiphon.Tests build charged
here. Its 10-minute Linux estimate and 20-minute cap are provisional cold-build
allowances on CARD-1850, using S2's 380.654-second build plus 209.229-second
guard execution as comparable history. The portable small-project allowance
is two minutes (red history: build 20.189 s, execution 5.087 s). Native setup,
AppHost, containers and migrations are not used. No new test starts a process
or creates a filesystem fixture. No LinuxOwnedGitProcessTests file is touched;
its inherited fixture hygiene repair remains outside this Windows transport
slice. ReleaseImpactEnvelopeTests and the audit stay with Debug 49ae044d.
Reconcile the actual selector after index regeneration and bind any additional
classes as exact rows before execution. No unchanged repetitions are required
after green. Native execution is owed by the orchestrator on Windows.

S3a actual-index amendment: regeneration adds six lines for the new Windows
launch transport. The final selector adds ReleaseImpactEnvelopeTests alongside
DerivedImpactIndexTests. CP-S3-3 covers that four-method class without editing
its source (Debug 49ae044d owns the fixture leak). Record exact new fixture
paths and remove only those after the run. No policy input changed; leave both
nightly pins unchanged. The actual selector has six classes, no Windows filter
and no capped/owed classes. Migrations are not selected.
