# CARD-1539: capture file-use evidence at the failure boundary

Plan, 2026-10-23. Task `bed8478e`; source base
`de11733ef83e989df8339f05872a347fdb6fcca8`. Documentation only; no build,
test, desktop access, or intervention in qualification supervisor PID 209072.

Capture a bounded, redacted process-use snapshot at the first failed filesystem
operation, before retry, disposal, restore, or cleanup can erase it. Put the same
capture ID and holder summary in failure output and durable JSON. Observation
does not authorize termination, deletion, retries, admission, or release.

## Evidence and ground truth

Read CARD-1539 in full, its named predecessors CARD-0308/0820/1515/1436, and
CARD-0443/1370/1445/1538. Completed board searches for `lock`, `in use`,
`access denied`, and `file lock`, including archived cards. These are evidence
samples, not a fleet incidence census; broad `lock` matches also include unrelated
blocked-task cards. Reuse those cards for repairs; do not duplicate them here.

The [qualification diagnosis](../../investigations/2026-10-23-rc-5a126f6fc-qualification-reds-debug.md)
was absent at this base and read from `origin/feat/card-task-c2afd605`, tip
`4117d4846703c14c39ff942ff2cedc32fd37e80a`. Its retained source is rc
`5a126f6fc5ae4e16d803b628716b8f0485a37174`, invocation
`6bd38cbb9604482e96a4854957cead05`. This task did not independently inspect
the Windows receipts. The diagnosis's cutoff is 07:11:26 UTC, not a current live
qualification verdict. It records 19 failures and incomplete coverage; seven
failures are access-denied cleanup cases, not nineteen lock cases.

| Card assumption | Code/evidence at the recorded base | Consequence |
|---|---|---|
| WorktreeLockDiagnostics is a test helper | `server/Infrastructure/Git/WindowsWorktreeLockDiagnostics.cs:11` implements `IWorktreeLockDiagnostics`; `tests/Antiphon.Tests/Infrastructure/WorktreeLockDiagnosticsTests.cs` tests it. It requires trusted configured Handle, elevation and existing EULA acceptance; Linux is unavailable. | Keep its server interface and cleanup journal, extract/reuse the collector through an adapter. Do not create another independent worktree scanner. |
| There is no Restart Manager implementation | `tests/Antiphon.Tests/TestHelpers/WindowsFileLockOwners.cs:11` already implements Start/Register/GetList/End in a pwsh child. It reports PID/start/image but no command line/ancestry, writes no durable capture, and kills its own diagnostic child on timeout. | Promote its native declarations/query loop into the shared component; retire the test-only query worker. The new capture path starts no diagnostic child. |
| CARD-1515 catches any denied fixture image | `OwnedPtyFixtureCleanup.cs:59` catches candidate errors but calls destructive `AttributeRefusal` before diagnosis; `:100` examines only previously captured `OpenConsole.exe` images. A DLL error can rethrow at `:63` without diagnosis. | Diagnose the original failure before attribution. Keep the existing OpenConsole-only retry eligibility separate and unchanged. |
| Access denied proves a lock | Today's group 3 has one DLL denial after all captured process generations exited; the later RM query found zero holders. Group 15 has six ReadOnly Git-object teardown errors, also present in the prior run. | Record attributes/error and observed use separately. Neither zero holders nor ReadOnly establishes a historical holder. CARD-1538 owns the attribute cleanup fix. |
| Existing owner data proves ancestry | `WorktreeCleanupDiagnostics.cs:7` has nullable parent fields, no command line; `WindowsWorktreeLockDiagnostics.cs:78` validates start twice but leaves attribution unknown. CARD-1436 fixed an exited-git census false positive. | Match PID **and** creation identity, retain explicit unknowns, and keep command-line candidates distinct from observed holders. Do not change GitIndexLock admission. |
| There is one existing diagnostics directory | No shared `.antiphon/diagnostics` resolver exists. Checkpoints default to `.antiphon/checkpoints` (`Manifest/CheckpointManifest.cs:8`); row failures are written by `EvidenceFolder.cs:43`. Server cleanup uses its existing capture journal and configured retained-evidence root (`FileSystemEvidenceRetention.cs:35`). | Add one explicit destination contract, using these existing roots where available; no current-directory guessing and no evidence inside a deletion target. |
| Existing redaction is comprehensive | `DiagnosticsRedactor.cs:32` handles known token shapes and paths. `RunnerProcessProbe.cs:562` handles secret assignments/bearers/known values. `SlotDiagnostic.cs:42` strips HTTP URL credentials, query and fragment. None alone is a safe arbitrary argv renderer. | Reuse their implementations and add a conservative argv projection; never persist raw process census output. |
| A disposal failure means production Dispose needs changing | `SessionRunnerRuntime.cs:4266` deliberately detaches from persistent hosts. Today's exception is fixture `DeleteRoot`, after disposal assertions. | Wire filesystem catches in shadow-copy/manifest cleanup; leave process lifetime, linger, exit witnesses and session delivery alone. |

## Decisions

**D-1 — One shared capture component, existing adapters.** Add
`src/Antiphon.HostCleanup/Diagnostics/` with `FileUseCapture`, typed request/result,
OS providers, safe formatter and bounded evidence writer. HostCleanup is already
referenced by server, checkpoints and Antiphon.Tests. Add explicit references
for PtyHost.Client, PtyHost and SessionRunner where the new hooks need them.
No dependency on server, EF, ASP.NET, test assemblies or
PowerShell is allowed in this component. Windows CIM reads may use the repo's
pinned `System.Management` 9.0.21, loaded only in the Windows provider; account for
its shipped dependency in host packaging. The shared instance is caller-owned or
DI-managed, not new mutable global state.

Retain `IWorktreeLockDiagnostics`, `WorktreeLockSnapshot` and journal presentation
as compatibility boundaries. Replace the registered Handle-based implementation
with a thin adapter to the shared capture, mapping unknown/partial faithfully.
Keep the old Handle parser/provider as an explicit legacy compatibility path
until its consumers are removed; do not run it from the new failure hooks.
Its shell process, calibration writes and timeout kill do not satisfy this card's
read-only capture contract. Reuse its bounds, partial-result semantics and
existing regression tests rather than copying it into a second scanner.

**D-2 — Trigger locally, before evidence-changing work.** Inputs are the exact
operation path(s), exception/native error or nonzero command result, failure UTC
and monotonic timestamp, operation/correlation IDs, destination, and optional
known owned process generations. Trigger on `UnauthorizedAccessException`,
`SecurityException` from a filesystem read, Windows errors 5/32/33, and POSIX
EACCES/EPERM/EBUSY/ETXTBSY. For external Git/build text only, recognize bounded
`in use`, `access denied`, `permission denied`, `being used by another process`
and `text file busy` at the command's failure boundary. A generic `IOException`
or `cleanup_refused` alone is insufficient: ownership and content refusals are
not lock failures. Unwrap PowerShell invocation exceptions without losing the
original ErrorRecord. Do not install FirstChanceException/global log scraping.

Capture once per operation/path/native-code tuple at the first failure, including
failures later recovered by existing retries. Upper layers reuse the capture ID.
The worktree adapter consumes the already captured snapshot from operation
context; it must not launch a second, later census from WorktreeGuardedCleanup.
Capture immediately before any existing retry, move-back, process teardown or
attribute-clearing probe. An internal operation boundary with a known path wins
over a basename parsed from exception text. For a recursive-delete exception
whose exact child is unknown, record the root and uncertainty, then enumerate
bounded file candidates without following links; do not infer one child solely
from its basename. The observation is a short interval **after** failure, not an
atomic historical snapshot; store both failure and observation timestamps.

**D-3 — Windows: RM first, bounded CIM/module fallback.** Promote the existing
RM structures including `RM_UNIQUE_PROCESS` (PID plus FILETIME). Use only
`RmStartSession`, batched `RmRegisterResources`, `RmGetList` (size query plus at
most two growth retries), and `RmEndSession` in `finally`. Never import/call
RmShutdown, RmRestart, process termination, signalling, debug attach, privilege
adjustment, handle closing in another process, or target-file mutation.

Loaded DLLs are in scope: Microsoft's [file-owner sample](https://devblogs.microsoft.com/oldnewthing/20120217-00/?p=8283%2F)
explicitly uses `kernel32.dll` as an example of many reported processes and
explains PID/start identity. W-1 below must prove this on an owned loaded DLL,
without relying on a normal FileStream handle. This is documented support, not
a native execution claimed by this Plan. RM is not a universal handle census.

Register files, never directory roots: [RmGetList](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/nf-restartmanager-rmgetlist)
documents error 5 for registered directories. A directory failure registers at
most 128 existing descendant files, prioritizing explicit failed files, then
owned image manifests; empty/CWD-only directories remain incomplete coverage.
Do not follow reparse points. Batch registrations because they are relatively
expensive ([RmRegisterResources](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/nf-restartmanager-rmregisterresources)).

No elevation prompt or blanket administrator precondition: try under the current
token and retain denied fields. [RmStartSession](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/nf-restartmanager-rmstartsession)
allows at most 64 sessions per user session; error 353 is `SessionLimit`, not an
empty result. Protected-process access can deny module/metadata reads even where
the process is listed ([process access rights](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights)).
Do not extrapolate RM's cross-session **shutdown** restriction to a guaranteed
query scope. Record token/session, partial coverage and native error codes.

Enrich matching identities from read-only CIM `Win32_Process` Name, CommandLine,
ParentProcessId, CreationDate and ExecutablePath; these are documented
[process fields](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-process).
If RM fails or directory coverage is insufficient, use a bounded CIM process
census and accessible `Process.Modules` paths. An exact module-file match is
`LoadedModule`; an argv path match is only `CommandLineCandidate`, never proof
of a handle. Preserve RM PID/start when enrichment is denied. Missing CIM,
access denial and architecture/protected-process module failures yield partial
data, not failure of the original operation. No shell fallback is necessary.

**D-4 — Linux: /proc first.** Read `/proc/<pid>/fd` symlink targets and
`/proc/<pid>/maps`, plus `stat` start ticks/PPID, `comm`, `cmdline`, `exe`, and
`cwd` for directory evidence. Do not read `environ`, process memory or target
file contents. Compare canonical path boundaries and device/inode where
available; handle escaped map paths and ` (deleted)` explicitly. Do not mistake
`tree-sibling` for a descendant or an old unlinked inode for a replacement file.
Re-read process start identity before emitting enrichment. Namespace/hidepid/
ptrace restrictions make coverage partial; `/proc` visibility is not the host
fleet ([Linux proc documentation](https://www.kernel.org/doc/html/latest/filesystems/proc.html)).
An open fd/mapping on Linux is observed use, not proof it prevented unlink.
Use a real exclusive-open conflict for the Linux acceptance fixture, not an
invalid assumption that ordinary open files cannot be deleted on Linux.

Do not make fuser/lsof dependencies or install tools at failure time. They are
optional future read-only providers **only when present** and independently
bounded; this implementation needs neither. In particular, never `fuser -k`.

**D-5 — Strict caller budget with honest native limits.** Default capture budget
is five seconds total: at most four for observation/enrichment and one reserved
for redaction/persistence/output. Pass a smaller remaining operation deadline
when one exists; never reset the server's existing ten-second additional-work
allowance. One active collector per caller-owned service, no waiting queue;
concurrent captures return `Busy` promptly with their own failure envelope.
Maximums: 128 resource paths, 4096 candidate processes, 4096 fd entries per
process, 1 MiB maps per process and 8 MiB total read input, 32 reported holders,
8 ancestors per holder, 4096 retained characters per redacted command line,
32 KiB UTF-8 JSON and 8 KiB failure summary. Record omissions, not silent cuts.
Names are capped at 256 characters and rendered paths at 1024; retain a path
identity/hash when its display is clipped. The final UTF-8 serializer reserves
the capture/error envelope and first holder before trimming optional ancestors,
extra owners and display strings. Even a no-owner/error-only result must fit.
Per operation retain at most eight distinct captures; retain the first and
report suppression after that. No automatic deletion of prior evidence.

RM and some filesystem/CIM calls are synchronous and cannot honestly be promised
to stop in five seconds without killing a worker. Use one owned bounded worker
and race its result against the monotonic deadline. If it does not return, emit
`TimedOut`; keep its slot occupied until its `finally` releases native resources,
discard late observations and forbid late evidence publication. Subsequent
requests report Busy rather than accumulating threads or RM sessions. The
deadline bounds waiting, not a hung kernel call's lifetime; this explicit limit
is preferable to secretly killing a diagnostic process. Tests must release/join
the controlled blocked seam. Apply the same bounded admission to a stalled
evidence sink; abandon publication authority after timeout. An already-started
temporary-file write may finish late, but cannot publish a completed capture;
the bounded worker owns that residue and its ordinary disposal. A broken OS/disk can
prevent durable evidence; that cannot become a new product failure.

**D-6 — Preserve errors and outcomes.** `TryCapture` returns a result for all
diagnostic failures, including cancellation, metadata/redaction/sink exceptions
and missing dependencies. It never throws into the failure path. Preserve the
original exception instance, type, HResult, stack and cancellation semantics via
`throw;`/ExceptionDispatchInfo; store the safe summary/reference in `Exception.Data`
where supported and emit it to the existing logger/test failure output. Base
`Exception.Message` is immutable: do not use reflection or replace arbitrary
exceptions just to append text. Existing custom cleanup exceptions can include
the summary in their existing constructor. For TUnit, put the summary in captured
failure output (and hence TRX/EvidenceFolder) before rethrowing. A body failure
remains first in the existing cleanup aggregation; a diagnostic failure is never
another aggregate member. Best-effort cleanup stays best-effort; failed cleanup
stays failed. Success paths perform no census, disk writes or process queries.

**D-7 — Useful identity without false ownership.** Each owner contains PID, name,
nullable UTC start plus OS-native generation, safe command line, up to eight
generation-checked ancestors, match kind/resource and observation time.
`antiphonOwned` is nullable: true only from a caller-supplied owned generation
or a validated ancestor chain to one; false only from positive foreign identity;
otherwise null with an attribution reason. Separately label recognizable roles
(runner, AppHost, testhost, OpenConsole, git, dotnet/MSBuild/VBCSCompiler).
An executable name, working directory, or argv substring alone never establishes
Antiphon ownership. Exited/PID-reused rows retain only already-observed identity
and `ExitedDuringCapture`/`IdentityChanged`, never attach a new process's argv.

**D-8 — Reuse privacy rules before every sink.** Extract the pure text routines
behind `DiagnosticsRedactor.Redact`, `RunnerProcessProbe.Sanitize` and the URL
part of `SlotDiagnostic.Excerpt` to one source, proposed
`scripts/lib/DiagnosticTextRedactor.cs`, linked into server and HostCleanup.
Keep existing facades/namespaces so application code gains no infrastructure I/O
dependency. Do not refactor launch/probe execution. Existing callers retain their
current policies; the new collector composes the stricter policy below.

Redact `{{key:...}}`, sk-shaped keys, Slack xoxb/xoxp tokens, ghp/github_pat tokens,
Bearer credentials, Telegram bot tokens, Password connection-string values;
exact supplied secret values and the current task token; assignments whose keys
are api-key/token, access/refresh token, secret-access/private key, database URL,
connection string, token/password/secret/authorization, including delimited and
camel-case suffix forms recognized by RunnerProcessProbe. Mask separate-value
credential switches as well as `=`/`:` forms. Strip URL userinfo, query and fragment
for every recognized URI scheme (not just HTTP); malformed URL or argv quoting
masks the whole uncertain token. Normalize home/project paths using existing
DiagnosticsRedactor behavior (`~`, `<project-n>`).

Known patterns cannot recognize arbitrary positional secrets. Therefore the
persisted `commandLine` is a **redacted projection**: executable/module identity,
allowlisted structural switches and proven resource paths remain; unrecognized
argument values, inline scripts (`-Command`, `-EncodedCommand`, `-c`), prompts
and opaque payloads are `[redacted]`. Foreign commands get the same policy.
Bound raw input before parsing; if truncated through a token or redaction throws,
omit the complete command line with `RedactionUnavailable`. Redact error/path/
ancestor strings too, before serializing, formatting, clipping or logging. Never
dump raw CIM/proc output, environment blocks, credentials or diagnostic exceptions.

**D-9 — Explicit, durable destinations.** JSON schema v1 contains capture ID,
task/run/session/operation IDs when known, source SHA when supplied, failure type/
native code and redacted text/path, UTC interval/duration, path metadata,
backend/coverage/reasons/omission counts, owners and persistence status. Write a
new uniquely named `file-use-<UTC>-<captureId>.json`; never overwrite another
capture. Use owner-only, no-follow regular files under an explicitly supplied
evidence root. The writer does not call instrumented cleanup/write helpers,
preventing recursion. Same safe result object feeds JSON and the text summary.

| Caller | Destination |
|---|---|
| Delegate/test outside a checkpoint | `<task-root>/.antiphon/diagnostics/`; root supplied by task/test composition, not inferred from a foreign process's argv. |
| Checkpoint and its child tests | `<runDirectory>/diagnostics/`, beside existing `rows/<CP>/failures.md`; pass the absolute root to children through a proposed `ANTIPHON_DIAGNOSTICS_ROOT`. Do not put it in `bin-*` or the tool-copy directory being removed. |
| Server deleting a worktree | `<configured retained-evidence root>/<taskId>/<attemptId>/diagnostics/`, outside the removal tree, plus existing journal summary/reference. Reuse `FileSystemEvidenceRetention.Root`; do not write new files into an already-inspected removal target. |
| Qualification | `<invocation ResultsDirectory>/diagnostics/`; bootstrap from the pinned prebuilt HostCleanup assembly before replacement failures can occur. No build/download on an exception path. |
| Runner/Pty host without a task | `<configured log root>/diagnostics/<sessionId>/`, outside copied image and manifest targets. |

If the chosen root is absent, attempt its bounded safe creation; if denied,
unavailable, inside the deletion target, or untrusted, emit `EvidenceUnavailable`
and the sanitized summary through the original failure sink. Do not redirect to
arbitrary temp locations or claim persistence succeeded. Existing `.antiphon`
ignore/retention policy applies (`.gitignore:55`,
`WorktreeIgnoredContentClassifier.cs:49`, `FileSystemEvidenceRetention.cs:13`).

**D-10 — Separate qualification and repair ownership.** This is diagnostics,
not a fix for release timing, ReadOnly cleanup, GitIndexLock, retry policy or
checkpoint custody. It adds no session waiting for user input and no release
timer. With parking disabled, waiting sessions still have no automatic release
deadline (CARD-1083). Keep the separate TestDesign stage: it must freeze the
method roster and red/green build grouping below before Code is dispatched.

Rejected alternatives: a global exception listener (unbounded volume and weak
path attribution); another test-only RM helper (does not cover production/tooling);
raw command lines (credential disclosure); name-only ownership (false attribution);
mandatory Handle/elevation or lsof installation (unavailable exactly when needed);
kill-and-query or retry-and-query (destroys evidence); treating empty results as
proof that no holder existed (racy and permission-limited); wrapping all exceptions
(changes error contracts); broad fixture/production retry repairs (owned by the
existing cards).

## Wiring inventory and priority

Line numbers below refer to the recorded Plan base, except explicitly marked rc
stacks. Rank is observed recurrence in the available evidence, not an invented
fleet rate. Implement the group-3 fixture hook early despite its smaller count.

| Rank / ID | Observed recurrence | Failure operation and actual catch boundary | Hook and preserved behavior |
|---|---|---|---|
| 1 / WIR-1 | Six group-15 ReadOnly denials today; same six previously (CARD-1538). | `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:352,379,413,465,490,2788`: raw recursive deletes in finally, no local catch. Existing shared helper `tests/Shared/ReadOnlyDirectory.cs:10` clears ReadOnly before delete. | Add an Antiphon.Tests cleanup observation wrapper at those raw sites, or compose around the CARD-1538 helper if it lands first. Do not copy its attribute repair into this card; capture residual failure before further cleanup and preserve body errors. Never interpret ReadOnly as holder proof. |
| 2 / WIR-2 | At least five named failed land cleanups in CARD-0443; CARD-0308 establishes a historical live-descendant/CWD cause. No quantified new land-lock failures in today's diagnosis. | `server/Infrastructure/Git/WorktreeNoFollowDelete.cs:74-79` retries sharing error before throwing; guarded catches `GuardedWorktreeRemoval.cs:90,154,184,232`, command exception/nonzero at `:257,263`; `WorktreeGuardedCleanup.cs:54` currently captures later, after journal write. | Observe the first native failure before the 100 ms retry and before move-back; pass exact set-aside path, not the now-absent original root. Carry capture to guarded result/journal. Diagnose command text before reducing it to generated code. Include context-free settlement/retirement paths; retain original residue codes, slots, native probe and retry nomination. |
| 3 / WIR-3 | CARD-0820: held-file tool-copy test failed 2/3 initial branch runs, plus later intermittent cases; CARD-1445: one cleanup_refused green-test run; CARD-1370: one state.json denial. Counts overlap historical investigations, so do not sum them as unique events. | `src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs:80,95,101`; `ToolCopyCleanup.cs:123,128`; `SafeWrite.cs:189,226,283` replacement and `:198,235` cleanup; `SafeDelete.cs:241` inventory exception. `tools/Antiphon.Checkpoints/Cleanup/OutputCleanup.cs:107` projects refusal; `ReportValidator.cs:27` returns cleanup_refused. | Hook precise unlink/replace/read failure before cleanup or retries. Thread capture context through `CheckpointCleanupContext`. Preserve `DeleteResult.Reason`, report outcome/exit=2 and source/creation custody. No census for ledger mismatch, tracked-file refusal or dry run. Feed summary into existing report failure text and JSON reference. |
| 4 / WIR-4 | Two different group-3 denials in consecutive Windows candidates: OpenConsole in CARD-1515, DLL today. Current exact holder unknown. | `OwnedPtyFixtureCleanup.cs:59-72`, diagnostic after destructive attribution; `DirectSessionRunnerClientDisposalTests.cs:398-413` wraps deletion and body cleanup. Rc still has one-shot `DeleteRoot:366`. | Capture all eligible errors at first catch, including DLL, before `AttributeRefusal`. Replace `WindowsFileLockOwners` with shared adapter. Keep the OpenConsole-only retry test, exit witnesses, delete assertion and idempotence; diagnostics on nonretryable denials must not make them retryable. |
| 5 / WIR-5 | No measured stamp-lock frequency in cited receipts; requested coverage. | `tools/Antiphon.Checkpoints/BuildConfinement.cs:443-448` hashes then converts I/O/security exceptions into `unreadable`; `CheckpointNativeIo.cs:340,358` may reduce errno to unknown. | Capture at the failing read/stat where full path/code exist. Keep exact stamp/settlement classification, tampered precedence and timeout outcome; attach summary separately, never append it to a stamp used for comparison. |
| 6 / WIR-6 | No proven qualification-supervisor replacement lock in today's diagnosis; requested preventive coverage. | `scripts/lib/nightly-common.ps1:368-377` WriteAllText/Replace/Move has no local catch; `nightly-run-impl.ps1:60-65` logs/rethrows or returns false under AllowFailure. | Catch at Write-NightlyAtomicJson, pass source/destination/backup and invocation diagnostics root to a thin shared-assembly adapter, emit safe summary, rethrow original ErrorRecord. Preserve AllowFailure and no recursive diagnostic use of this writer. Also cover release-selection's creation conflict only when it is a real filesystem denial. |
| 7 / WIR-7 | No separate runtime file-lock incidence demonstrated; the group-3 failure is fixture cleanup. | `src/Antiphon.PtyHost.Client/ShadowCopyStore.cs:246` copy, `:66,73` recovery and `:265-268` swallowed delete; `src/Antiphon.PtyHost/HostSession.cs:455-459` manifest delete; `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:2347-2351` best-effort delete. | Capture inside these filesystem boundaries, before staging cleanup. Preserve false/best-effort/throw behavior. Do not add an indiscriminate catch to DisposeAsync or diagnose pipe EOF as a file lock. No change to `SessionRunnerRuntime.cs:4266` host-detach contract. |

## Slices

Each implementation slice is 30–60 minutes of focused source work, excluding
slot wait/build qualification. Commit tests/seams before fixes; push every
meaningful commit. Code may split a slice further without widening its scope.

| Slice | Files and deliverable | Named verification |
|---|---|---|
| S0 (Plan) | This document only. | Source/card/API/documentation reads and Git/Markdown checks; no build. |
| S1 (60 min) | `src/Antiphon.HostCleanup/Diagnostics/{FileUseCapture,FileUseModels,LinuxFileUseProvider,FileUseEvidenceWriter}.cs`; pure redaction source/facades from D-8; `tests/Antiphon.Tests/Infrastructure/FileUseCaptureTests.cs`; new staged `tests/Antiphon.FileUseTestChild/{Program.cs,Antiphon.FileUseTestChild.csproj}` and Antiphon.Tests staging reference. Commit compile-capable unavailable scaffold + failing real-holder tests, then implementation. | V-1 through V-7, R-1/R-6; Linux real fd and maps fixtures. S1 is independently useful and must name a real child. |
| S2 (45–60 min) | `Diagnostics/WindowsFileUseProvider.cs`, migrate declarations/query loop from `WindowsFileLockOwners.cs`; `FileUseWindowsTests.cs`; package/reference changes only as required. | V-8/V-9; native W-1/W-2 owed to Windows lane. Portable fake RM/CIM cases cannot earn native credit. |
| S3 (45 min) | `OwnedPtyFixtureCleanup.cs`, test-only wrapper/callers WIR-1/WIR-4, retire `WindowsFileLockOwners` worker; `FileUseWiringTests.cs`, existing cleanup tests. Coordinate CARD-1515 and CARD-1538 rather than importing their repairs. | V-10/V-11, R-2; first-denial-before-probe and DLL inclusion. |
| S4 (45–60 min) | Server worktree adapter/DI, `WorktreeNoFollowDelete.cs`, `GuardedWorktreeRemoval.cs`, `WorktreeGuardedCleanup.cs`, additive evidence reference/presentation only; WIR-2. | V-12, R-3; exact set-aside capture before retry, unchanged authorization/outcome. |
| S5 (45–60 min) | HostCleanup checkpoint context/ledger/safe writes/tool-copy hooks and `BuildConfinement.cs`; WIR-3/WIR-5; existing report text adapter. | V-13/V-14, R-4; cleanup_refused and unreadable remain authoritative. |
| S6 (45–60 min) | Thin ASCII `scripts/lib/file-use-diagnostics.ps1`, `nightly-common.ps1`/run context, shadow-copy/manifest hooks WIR-6/WIR-7, caller-owned collector composition and project references; docs/testing-and-build.md and docs/orchestration-loop.md owner notes. | V-15/V-16, R-5; real adapter calls against owned scratch only. |
| S7 (30 min plus checkpoints) | Freeze source; qualify the closed selections, preserve receipts in ignored output, run evidence-diff, report exact SHA/counts/native debt. No report-only source churn after final qualification. | Final green group, then separately commissioned Windows rows after protected qualification. |

## Rollout and rc status

Fetched planning observations: `origin/master`
`71e578108d6a9604f6f8d7d9f1aae5e45dbd0130`; `origin/rc/2026-10-20`
`5a126f6fc5ae4e16d803b628716b8f0485a37174`. The release owner still names
an older line in its introductory prose; use the actual candidate/ref above for
this incident, and re-read live refs before dispatch. This Plan does not update
release policy or claim the running qualification has finished.

Deploy only reviewed complete slice groups. Server/checkpoint/runner capture
requires their newly built binaries; qualification scripts need the same pinned
shared assembly staged before the run. No on-failure compilation, hot replacement
of running supervisor scripts, or production restart by this Plan. A master land
does not activate either product hosts or the existing rc candidate.

To diagnose the recurring rc group-3 failure, a separately reviewed rc port must
pick the shared collector plus fixture hooks, redaction, child fixture and build
references as a coherent set. Rc lacks CARD-1515 fixture fixes
`4440d7a03e0b77389650b1b4ef4192331b5bb13e` and
`1f55bfc32df6aac32a48f9246748d5786cbe9e35` according to the Debug; coordinate
their qualified port or adapt the capture to rc's existing one-shot delete.
Do not imply diagnostics alone repairs its deletion. Likewise CARD-1538 owns
ReadOnly repair. Broader server/tool instrumentation must be reviewed for rc
scope rather than importing unrelated master refactors. Rebuild a fresh pinned
candidate and run the owed native rows after supervisor 209072's protected run.
Never modify the old candidate/receipts. The diagnosis's 19 reds and incomplete
coverage still block release; this card earns no release credit on their behalf.

## Verification design

This is the executable proposal for **separate TestDesign**, not permission to
run tests during Plan. New method names below are proposed. TestDesign freezes
expanded counts, staging and the narrow row groups before Code. All tests live
in **Antiphon.Tests**, including wrappers that invoke the real PowerShell
function/real production APIs; no separate test-project build per subsystem.
Classify new tests under the existing execution policy. Windows-only guards
belong on native methods, not the class containing portable RM/CIM seam tests;
every selected row requires zero skips. Use owned per-test database schemas for
worktree integration witnesses and retain the process limiter on all spawning
fixtures. A failure inherited from base requires an exact-method base rerun,
never a full-suite attribution run or an assertion/timeout relaxation.

### Real fixtures and RED witnesses first

The owned child has nonce-confirmed readiness and release over redirected pipes,
an independent 20-second self-exit watchdog, and modes `exclusive-file`,
`mapped-file` (map retained after stream closes), and Windows `loaded-dll`.
Parent waits for readiness before deliberately failing the operation, invokes
the real collector synchronously from that catch, asserts text/JSON, and only
then releases/joins the child. Use an assembly-local
`ParallelLimiter<ProcessSpawnLimit>` and no production runner/provider. Fixture
cleanup may terminate only its exact owned child as emergency rescue; that is
test infrastructure, never an operation the diagnostic component can perform.
A rescue fails the ordinary proof. No sleeps to guess lock acquisition.

Use a private copy of an already built native DLL (for example the staged
conpty.dll), loaded by the child with NativeLibrary.Load and held until release.
Confirm module path and an actual denied overwrite before RM capture. A mere
managed assembly load is not the DLL-mapping oracle. Keep this fixture outside
all live runner/candidate trees. Linux fd fixture uses FileShare.None versus a
conflicting .NET open; verify the actual IOException before asserting holders.
The maps-only mode proves lookup independent of the fd path; unlink success on
Linux is a compatibility control, not a failed delete to fabricate.

| ID | Planned method / expanded count | Required observation; independent RED witness |
|---|---|---|
| V-1 | `FileUseCaptureTests.C1539_linux_holder` / 2 (fd, maps) | Real child PID/name, safe argv identifying its fixture executable, exact start generation and parent in JSON and failure output; capture precedes release. Scaffold returns unavailable: fails PID/text assertion. Disable fd/maps matching separately and only its variant fails. |
| V-2 | `C1539_trigger_and_original_error` / 1 | Concrete IOException HResult 0x80070020/21, UnauthorizedAccessException, Win32Exception(5), SecurityException, POSIX error projection and failed-command texts trigger; disk-full/cancellation/ownership-refusal controls do not. Rethrown instance/type/HResult/stack and original return/exit values unchanged even if diagnostic throws. RED: bypass trigger or replace sentinel error. |
| V-3 | `C1539_absent_inputs` / 5 | Five cases in the table below; RED: collapse denied/missing/exited into complete empty, or enrich a reused PID. |
| V-4 | `C1539_budget_and_caps` / 1 | FakeTimeProvider plus independently controlled blocked native call: returns TimedOut by budget, next call Busy, exactly one outstanding worker; release/join ends RM once and no late publication. Exercise each byte/path/process/owner/ancestor cap with distinct sentinels and omission counts; RED: bypass each cap or re-arm timeout. |
| V-5 | `C1539_redaction_before_sinks` / 1 | Synthetic secret markers for every D-8 family, separate argv values, DSN/userinfo/query/fragment, encoded/inline payload, malformed and truncated token, exact secrets and foreign ancestors absent from JSON, test output, Data and logs. Harmless fixture executable remains. RED: bypass redaction independently at JSON and text projections. |
| V-6 | `C1539_persistence_fail_safe` / 1 | Parse real unique JSON after child exit; same ID/holder as output, UTF-8 <=32 KiB, outside deletion target. Denied/missing/linked/throwing/hung sink cases preserve original outcome and emit safe EvidenceUnavailable; no recursive capture. RED: omit write or let a sink exception escape. |
| V-7 | `C1539_generation_and_ownership` / 1 | Known generation/ancestry true; positively foreign false; name-only and inaccessible identity null; changed PID never takes replacement argv. RED: name-based ownership or remove second generation check. |
| V-8 | `FileUseWindowsTests.C1539_rm_contract` / 1 | Fake native seam exercises success, 234 growth, 353 session limit, 5 denied, missing DLL, directory candidates, end-in-finally and size/deadline failures. No shutdown/restart/control API exposed. RED: register directory or treat error as zero holders. |
| V-9 | `C1539_cim_fallback` / 1 | RM unavailable: exact module match is LoadedModule; command-line-only match is Candidate; CIM denied fields preserve RM identities and partial status. RED: promote candidate to holder or discard known RM row on metadata denial. |
| V-10 | `FileUseWiringTests.C1539_pty_first_failure` / 1 | Inject exact DLL and OpenConsole denial through real OwnedPtyFixtureCleanup, record trace Capture -> attribution -> existing retry; nonretryable DLL still throws original. Persistent error names holder/output/path; diagnostic throws do not aggregate. RED: retain the old OpenConsole-only observation guard or move capture after probe. |
| V-11 | `C1539_git_fixture_failure` / 1 | Real wrapper used by WIR-1 receives UnauthorizedAccessException with ReadOnly metadata; no invented holder, body sentinel preserved and cleanup remains failed. Verify each listed direct site uses the wrapper; behavioral helper assertion is required, source check alone is insufficient. RED: drop catch/capture. |
| V-12 | `C1539_worktree_failure` / 1 | Real guarded cleanup with recording filesystem/Git seams drives first native sharing failure and denied nonzero unregister text; capture exact set-aside path before retry/restore, both publication and settled-task composition. Journal/reference survives tree removal; original residue/native nomination unchanged. RED: bypass either low-level or command hook independently. |
| V-13 | `C1539_checkpoint_failure` / 1 | Real ledger/tool-copy/safe-write/cleanup flow with throwing ICheckpointCleanupIo: capture first unlink/replace denial; original custody refusal/exit=2 and row results retained. Ownership mismatch causes no census. RED: bypass each boundary separately, assert trace/capture count for each. |
| V-14 | `C1539_stamp_failure` / 1 | Real Stamp/settlement with UnauthorizedAccessException; original unreadable stamp and tampered precedence unchanged, separate evidence has path/native code. RED: hook only report rendering (after source observation gone) or alter stamp. |
| V-15 | `C1539_qualification_replace` / 1 | Invoke actual Write-NightlyAtomicJson and caller through ScriptHarness in private scratch, inject failed replacement with a held file on Windows and exception seam on Linux; same ErrorRecord/AllowFailure outcome, safe output and JSON. RED: remove adapter invocation. Never invoke live qualification or a build. |
| V-16 | `C1539_runtime_file_failure` / 1 | Real ShadowCopyStore and manifest-file adapters with denied copy/delete seams preserve original throw/false/best-effort results; disposal detach untouched. RED: bypass each filesystem hook independently. |
| W-1 | `FileUseWindowsTests.C1539_native_dll_holder` / 1 | Native owned loaded DLL denied overwrite -> RM names child PID/start and redacted command line in output and JSON. RED: bypass RM matching while fallback disabled for this test. This is owed Windows evidence, never substituted with fake rows. |
| W-2 | `C1539_native_file_holder` / 1 | Native exclusive held file -> real failed delete/replace, query before release, unchanged child alive, then released file can be deleted. RED: bypass file query. Run under existing token, no elevation prompt. |

Absent-input cases use real production result types proposed in `FileUseModels`:
`FileUseSnapshot`, `FileUseStatus`, `FileUseCoverage`, `FileUseOwner`, and typed
reason codes. No `null!`/anonymous mock results as the absence oracle.

| V-3 case | Input/type | Required typed result |
|---|---|---|
| denied | Enumerator throws UnauthorizedAccessException; Windows seam returns Win32Exception(5). | Unavailable/AccessDenied if no scan possible, Partial if some PIDs scanned; zero observed owners is not Complete. |
| already-free | Real child releases after failed operation but before capture barrier. | NoOwnersObserved only for completely enumerated scope, otherwise Partial; failure retained with failure/capture timestamps, no assertion about historical ownership. |
| missing | FileNotFoundException/DirectoryNotFoundException (ENOENT) for requested path. | PathGone, no fabricated attributes/owner; preserve any already-observed deleted mapping with explicit identity, not a new file. |
| zero | Existing unheld ordinary file, readable controlled process inventory. | NoOwnersObserved, coverage Complete for that inventory; production namespace restrictions remain explicit. |
| exited | Observed matching identity disappears before enrichment: Process.GetProcessById throws ArgumentException, StartTime InvalidOperationException, or proc read FileNotFoundException. | Partial/ExitedDuringCapture, no replacement command line/ancestry or fresh live-holder claim; PID reuse is a separate assertion in V-7. |

### Regression coverage and independently bypassable controls

| ID | Exact existing/new methods | Invariant |
|---|---|---|
| R-1 | `FileUseCaptureTests.C1539_success_is_inert` (1); `WorktreeLockDiagnosticsTests.C443_PartialRetainsOwners`, `C443_EmptyRequiresCompleteCoverage`, `C443_ReusedPidDoesNotEnrichOldOwner` (3) | No behavior/census on success; retain partial/unknown/generation semantics while migrating adapters. |
| R-2 | `OwnedPtyFixtureCleanupTests.Delete_observes_release_and_preserves_failures` (4 arguments) | Existing attempts, five-second retry budget, body ordering and permanent-denial assertions retained. Update only the obsolete assertion that nonretryable denied images receive no **diagnosis**; they still receive no retry. |
| R-3 | `WorktreeGuardedCleanupTests.C443_GitAndOwnersCannotNominate`, `C443_InitialCleanHasNoCapture`, `C443_TwoGitSlotsMaximum`, `C443_CoordinatorOuterCancellation` (4) | Owner observations never grant retry authority, clean path inert, Git slots/cancellation unchanged. |
| R-4 | `FileUseWiringTests.C1539_checkpoint_nonlock_refusal` (1) | Existing report validator still rejects cleanup_refused; dry-run/identity/ledger refusal produces no process scan and no new deletion. |
| R-5 | `DirectSessionRunnerClientDisposalTests.Dispose_is_idempotent`, `Dispose_awaits_exit_of_its_running_host_and_descendants` (2, Windows lane) | Preserve exact lifecycle assertions and private-tree deletion; no timing relaxation or rescue. Native prerequisites and CARD-1515 debt remain visible. |
| R-6 | `DiagnosticsRedactorTests.No_secret_shaped_string_survives_redaction` (9), `Home_path_is_kept_only_with_IncludePaths`, `Project_directories_are_replaced_unless_IncludePaths` (1 each); `RunnerProcessProbeRedactionTests.Redacts_each_credential_assignment_without_consuming_neighbors` (31), `Redacts_the_union_of_exact_secret_occurrences_without_order_or_overlap_leaks` (11); `CheckpointSlotContractTests.diagnostics_are_bounded_and_escape_body_controls` (1) | Pure extraction preserves existing masks, neighboring text, overlap handling, paths and URL/token protection. Total 54 results; no process/load timing suite. |

PC-1/2 independently disable fd/maps match -> V-1 variants; PC-3 disables RM ->
W-1 with fallback disabled; PC-4 promotes CIM candidates -> V-9; PC-5 drops
identity checks -> V-7; PC-6/7 independently bypass text/JSON redaction -> V-5;
PC-8 omits persistence -> V-6; PC-9 makes diagnostic exception escape -> V-2;
PC-10 bypasses deadline/admission -> V-4; PC-11a–h bypass each D-5 cap -> V-4;
PC-12a–e collapse each absent case -> V-3; PC-13 moves fixture capture after
attribution -> V-10; PC-14 drops Git wrapper -> V-11; PC-15a/b drops native/Git
worktree hook -> V-12; PC-16a/b/c drops ledger/tool-copy/write hook -> V-13;
PC-17 drops stamp hook -> V-14; PC-18 drops script adapter -> V-15; PC-19a/b/c
drops shadow-copy/host-manifest/runner-manifest hook -> V-16; PC-20 performs a
scan on success -> R-1. Each variant has its own guard/assertion and retained
red receipt; no one monolithic fake failure may satisfy several controls.
SourceLanding Mutation uses one exact method (without wildcard) per cycle and
restores before green. These are later obligations, not Plan executions.

### Execution and cost

At 07:27 UTC, GET `/api/runner-defaults` revision 2 selected an eligible Linux
default; GET `/api/session-runners` also showed an eligible Windows runner.
Availability/capacity are observations, not a reservation. Re-read both at each
dispatch. Omit `-Runner`; omit `-Platform` for portable work, use `-Platform Linux`
only for the real proc fixture and `-Platform Windows` for W-1/W-2/R-5. `Any`
unpins. No fleet location is embedded in the plan's execution commands.

All rows target Antiphon.Tests. CP-1 is the S1 intentional red build; CP-2 proves
the useful S1 component green. Remaining portable rows share **one** test-project
build at final frozen source (CP-3), and native rows share one Windows build
(CP-8). Outputs cannot honestly be reused across changed SHAs or OSes. For later
slices, commit failing tests before fixes and retain their independent red
witnesses; TestDesign must bind those red executions into the same minimal
phase groups before Code, rather than authorize one build per test/site.
No whole assembly, namespace, native Pty assembly, broad nightly run or repeat
stress is authorized here. Named fixture shims must not launch nested dotnet
builds. Estimated ordinary floor below: 33 minutes, excluding slot waits and
separately bound red controls; TestDesign must add their exact cost, not hide it.

Use the checkpoint tool `run --plan <this-path> --rows <ids>
--expected-source-sha <full-sha>` and its `wait` until exit is not 75. Keep each
run owned/awaited, source frozen, and preserve unedited CHECKPOINT lines, TRX
roster, counts and build provenance. The tool takes its host build slot; any
necessary bootstrap uses `scripts/build-slot.ps1` and is reported. Exit 4 means
not run, never permission to run unleased. Validate final receipts, run
`scripts/check-evidence-diff.ps1` over base..HEAD, remove owned `bin-c1539-*`
outputs after retaining evidence, fetch master and report conflict-free
`git merge-tree --write-tree origin/master HEAD` without merging it.

### Checkpoints

Closed proposed ordinary list. Lane is included in Group because the manifest
rejects unknown columns. Every OR operand ends in `*` (CARD-0931); exact intended
method roster/counts above prevent prefix expansion from silently broadening it.
TestDesign publishes the red-execution amendment before Build; it may not erase
the Windows debt or replace behavioral witnesses with source-string assertions.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1539-red/` | linux-real-red | `/*/*/FileUseCaptureTests/C1539_linux_holder*` | V-1 | 2 intended missing-holder assertion failures, 0 skipped; compilation/fixture errors are not RED | 2 | 5 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1539-s1/` | linux-core | `/*/*/FileUseCaptureTests/C1539_*` | V-1..V-7, R-1 success | 13 passed, 0 failed/skipped | 13 | 5 |
| CP-3 | all | `tests/Antiphon.Tests -> bin-c1539-final/` | linux-core-final | `/*/*/FileUseCaptureTests/C1539_*` | V-1..V-7, R-1 success | 13 passed, 0 failed/skipped | 13 | 5 |
| CP-4 | all | CP-3 | portable-windows-contract | `/*/*/FileUseWindowsTests/(C1539_rm_contract*)\|(C1539_cim_fallback*)` | V-8, V-9 | 2 passed, 0 failed/skipped | 2 | 1 |
| CP-5 | all | CP-3 | portable-wiring | `/*/*/FileUseWiringTests/C1539_*` | V-10..V-16, R-4 | 8 passed, 0 failed/skipped | 8 | 3 |
| CP-6 | all | CP-3 | portable-postgres-compatibility | `/*/*/(WorktreeLockDiagnosticsTests*)\|(OwnedPtyFixtureCleanupTests*)\|(WorktreeGuardedCleanupTests*)/(C443_PartialRetainsOwners*)\|(C443_EmptyRequiresCompleteCoverage*)\|(C443_ReusedPidDoesNotEnrichOldOwner*)\|(Delete_observes_release_and_preserves_failures*)\|(C443_GitAndOwnersCannotNominate*)\|(C443_InitialCleanHasNoCapture*)\|(C443_TwoGitSlotsMaximum*)\|(C443_CoordinatorOuterCancellation*)` | R-1, R-2, R-3 | 11 passed, 0 failed/skipped | 11 | 3 |
| CP-7 | all | CP-3 | portable-redaction-compatibility | `/*/*/(DiagnosticsRedactorTests*)\|(RunnerProcessProbeRedactionTests*)\|(CheckpointSlotContractTests*)/(No_secret_shaped_string_survives_redaction*)\|(Home_path_is_kept_only_with_IncludePaths*)\|(Project_directories_are_replaced_unless_IncludePaths*)\|(Redacts_each_credential_assignment_without_consuming_neighbors*)\|(Redacts_the_union_of_exact_secret_occurrences_without_order_or_overlap_leaks*)\|(diagnostics_are_bounded_and_escape_body_controls*)` | R-6 | 54 passed, 0 failed/skipped | 54 | 1 |
| CP-8 | all-windows | `tests/Antiphon.Tests -> bin-c1539-windows/` | windows-native-holders | `/*/*/FileUseWindowsTests/(C1539_native_dll_holder*)\|(C1539_native_file_holder*)` | W-1, W-2 | 2 passed, 0 failed/skipped | 2 | 7 |
| CP-9 | all-windows | CP-8 | windows-pty-regression | `/*/*/DirectSessionRunnerClientDisposalTests/(Dispose_is_idempotent*)\|(Dispose_awaits_exit_of_its_running_host_and_descendants*)` | R-5 | 2 passed, 0 failed/skipped | 2 | 2 |
| CP-10 | all-windows | CP-8 | windows-replacement | `/*/*/FileUseWiringTests/C1539_qualification_replace*` | V-15 native replacement | 1 passed, 0 failed/skipped | 1 | 1 |

--- next stage ---
next: test-design
handoff: Freeze CARD-1539's method roster and minimal red/green checkpoint groups, preserving shared diagnostics, first-failure ordering, fail-safe errors, redaction and independent controls. Keep native DLL/file and disposal rows owed to a separately commissioned Windows lane after protected qualification; no live-host work.
artifact: docs/superpowers/plans/2026-10-23-card-1539-lock-holder-capture-plan.md
