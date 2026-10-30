# CARD-1619 plan review (task 0e97ad1e)

Subject: `docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md` at
`97186d86d74f04e3c3b575e89ca42d19c638fb02` (plan task 9a468e98). Read-only plan review on server2/Linux.

**Verdict: not ready for Code. 4 defects (D-1..D-4), 6 advisories.** The wire compatibility, the
consumer audit, the text table, the CARD-1610/1614 boundaries and the checkpoint import all hold up.

## Defects

### D-1: the Porta patch is not the least invasive correct option (plan D-6, "Native source and evidence contract", S1)
- **Where:** plan D-6 and lines 35-73. Pinned upstream `src/Porta.Pty/Unix/PtyConnection.cs` @54684ba5 (fetched).
- **What the code shows:** `ChildWatcherThreadProc` writes `this.exitSignal = status & 127`, then `exitCode`, then
  `terminalProcessTerminatedEvent.Set()`, then raises `ProcessExited`, all on one watcher thread. `IPtyConnection`
  already exposes `int ExitCode` and `bool WaitForExit(int)` publicly. A read of the private `int exitSignal` field
  through reflection, done inside the `ProcessExited` handler, therefore happens after the write on the same thread,
  so it cannot race. After subscribing, a `WaitForExit(0)==true` replay reads both values after the event barrier, so
  the fast-exit replay needs no vendored completion snapshot. A cached `FieldInfo` on `Porta.Pty.Unix.PtyConnection`,
  gated to `OperatingSystem.IsLinux()` and to the exact Porta.Pty 1.0.7 assembly, gives the same evidence:
  - no sln, csproj or land-graph inventory change;
  - no native-asset staging change;
  - no Windows code touched;
  - no conflict with ADR 0002.
- **Failure:** the plan rejects reflection without giving a reason. It vendors the whole managed library, including
  the Windows/Vanara code. That has these costs:
  - It contradicts ADR 0002 §1 ("A fork is disproportionate … re-vendoring on every upstream bump"), and the plan adds
    no superseding ADR.
  - Provenance rests only on the author-asserted nuspec `repository commit`. Nothing checks that the source builds to
    the signed DLL.
  - It splits the managed code (vendored) from the native code (still `PackageReference Porta.Pty`), and no guard
    catches it if a bump changes `libporta_pty.so`'s ABI under the vendored P/Invokes.
  - `PrivateAssets="all"` stops the native asset from flowing to `Antiphon.Agents.Pty` and PtyHost, so the "fallback"
    Content staging is in fact mandatory. `ShadowCopyStore.cs:188-209` builds its closure from deps.json.
  - Every vendored file needs supply-chain review. The files carry both the Microsoft and the Laird-McConnell MIT
    notices, and both must be retained.
- **Why it matters:** it adds maintenance and supply-chain surface to change one accessor. S1's 135-minute budget is
  mostly spent on staging and graph work that the alternative does not need.
- **Fix:** rewrite D-6 to use the reflection field read plus the public `WaitForExit`/`ExitCode` replay. If the field
  is missing, has the wrong type, or the assembly version differs, fail closed to `ExitSignal = null` (unknown, never 0).
  Add a loud witness `Porta_exitSignal_private_contract_holds` (field exists, is `int`, is declared on
  `Porta.Pty.Unix.PtyConnection`, Porta assembly is 1.0.7) to CP-1 beside V-1/V-2. Note that Porta's silent wait-error
  path (no event on ECHILD/errno) is existing behaviour and out of scope. If the plan keeps vendoring, it owes:
  - a superseding ADR;
  - a source-to-binary equivalence check;
  - a version-coupling witness;
  - corrected PrivateAssets.

### D-2: refusing an authorized kill when the journal write fails is the wrong tradeoff (plan D-9, "Stop intent", lines 217-219)
- **Failure scenario:** the runner volume fills up. Agents' builds and the default `SessionLogPath` (`workspace/session-runner-logs`,
  `SessionRunnerSettings.cs:6`) share that volume. Every instrumented Linux stop then refuses:
  - operator stop;
  - the CPU-spin watchdog (`SessionCpuWatchdogService.cs:131`);
  - the CARD-0079 compaction stop;
  - pool release (`PoolDelegateRelease.cs:91`);
  - `KillPidBestEffort` / `KillHostIfStillOurs` at shutdown;
  - the `Dispose` SIGHUP;
  - custody release.

  The runaway processes keep their CPU and seats, and the release sweep retries forever.
- **Why it matters:** the refusal buys nothing. The plan's own text table already has the honest degraded wording
  ("sender unknown"), so an unrecorded kill is never false, while a refused kill is a real availability loss. The
  behaviour is also asymmetric: Windows is not gated.
- **Fix:** make the journal best-effort. On a write failure:
  1. send the signal anyway;
  2. carry the in-memory intent marked `durable=false`;
  3. log once;
  4. describe the exit as `Antiphon stop requested (intent not durably recorded)` or `sender unknown`.

  Replace V-13 `Write_failure_sends_no_signal` with `Write_failure_still_stops_and_never_claims_attribution`, and add
  the same arm to V-14.

### D-3: the intent journal is forgeable by the agent it attributes (plan "Stop intent", lines 201-211)
- **Failure scenario:** on the Linux runner, the agent runs as the same uid as the runner and host. Custody only sets
  NO_NEW_PRIVS. The journal sits under the session-log root, and the session id, generation and pid/start are readable
  from `pty-hosts/manifests`. An agent, or a confused agent "cleaning up", can therefore:
  1. write an `issued` record for its own generation;
  2. die by `kill -9 $$`.

  The runner's journal recovery and the server's "matching issued intent" then render `stopped by Antiphon (SIGKILL;
  matching stop request recorded)`. That is the same false-attribution class this card exists to remove.
- **Fix:** produce the qualified sentence only from an issuer record held in memory by the process that sent the
  signal (HostSession or SessionRunnerRuntime), carried in the same exit envelope with its host-instance identity.
  Label disk-recovered records `recovered/unverified`, and corroborate them before qualifying, either against the
  runner's in-memory request or against the server's durable stop request (`TerminationSource`/`Stopping`, already
  persisted before kill per CARD-0256). Add a witness for "planted journal record, no issuer → `sender unknown`" to
  V-13 and V-15.

### D-4: S3 does not fit in 150 minutes (plan "Slices" S3, lines 269, 285-289)
- **Footprint:**
  - 3 entities, plus an EF migration and snapshot;
  - 3 DTO files, the mapper, and HTTP/phone-home exception mapping;
  - `IAgentProtocolAdapter`, 8 local/runner adapters and `RunnerTerminalSession`;
  - runtime, service, reconciliation, termination and liveness;
  - dispatcher, attention, supervisor, review, diagnostics and Check-compaction projections;
  - client types and owner docs;
  - 3 new classes with 26 results, including 4 real Linux host→runner→DB cases and a migration-upgrade case;
  - a pushed red commit, plus CP-5..8 (16 execution minutes plus an Antiphon.Tests build).
- **Why it matters:** the plan's own escape clause ("push the complete current slice … request another Code dispatch")
  plans for the overrun instead of avoiding it.
- **Fix:** split S3 in two, each ≤150 minutes, re-import, and keep S4 after both:
  - **S3a — persistence/transport:** entities, migration, DTOs/mappers, adapters, API/client. V-11 plus CP-8.
  - **S3b — classification/text/consumers:** helper, runtime/reconciliation/liveness/dispatcher/attention/supervisor.
    V-10, V-12, CP-6, CP-7.

## Advisories
- **A-1:** existing classes that are directly affected are missing from the roster:
  - `SessionGenerationExitTests`: real Linux `KillAsync` capture at `:646-695`; the generation fence and
    `Process exited (KilledByRequest, code 1)` at `:108`;
  - exit cases in `SessionReconciliationServiceTests` and `AgentSessionRuntimeTests`;
  - `SessionTerminationSourcePersistenceTests`;
  - `PtyAgentRunnerTests`.

  Under minimal testing the rc roster gates, but S3 changes exactly these branches. Add at least
  `SessionGenerationExitTests` as a narrow R row.
- **A-2:** R-3 conflicts with the new text. `AgentTaskLivenessTests.cs:72-80` asserts `ShouldContain("ProcessExit")`
  for a legacy exit 0, and the new legacy text `process ended (exit code 0; signal unknown)` drops that token. Either
  pin the token or authorise the assertion edit.
- **A-3:** the kill-site list omits two sites:
  - `PtyHostLauncher.cs:229`: the launch-failure host tree-kill. It is host-target and needs its own row.
  - `LinuxCgroupCustodyProbe.cs:136`: a probe-helper timeout. Mark it out of scope explicitly.

  `RunnerProcessReaper` kills server probes only and is out of scope.
- **A-4:** the skew matrix only covers reading. When an old runner restores a verification manifest
  (`RunnerCustodyLedger.cs:89-102`), it round-trips the typed record and `SaveAtomic`s it, erasing any new members.
  Say "an old writer drops the members", and keep exit evidence off the running manifest.
- **A-5:** the journal durability wording is vague. Specify `FileOptions.WriteThrough`/`Flush(true)` plus a directory
  fsync, as in `PtyHostManifest.cs:55-62`. "flush" alone is ambiguous.
- **A-6:** name a fixture for V-11's real-Linux cases. Use the in-process `SessionRunnerRuntime` pattern from
  `SessionGenerationExitTests.CaptureKilledExitAsync`. Staging a detached PtyHost apphost into `Antiphon.Tests.csproj`
  would be a definition-inventory change in S3, and the plan assigns those to S1 only.

## Verified sound
- **Wire tolerance:**
  - `PtyHostFraming.cs:15,42` and `PtyHostManifest.cs:37` use `JsonSerializerDefaults.Web`: unmapped members are
    skipped and missing ctor parameters default to null.
  - No `RespectRequiredConstructorParameters`/`Disallow` appears on the protocol, manifest or runner contracts.
  - `JsonUnmappedMemberHandling.Disallow` appears only on server request DTOs (`BoardDtos.cs:245/334/363/378` =
    Create/Update/Place/Reorder requests), not on projections.
  - `ExitedMessage` (`PtyHostMessages.cs:115`) already has a nullable code.
  - Both skew directions parse as the plan states.
- **Consumers:** a grep of src/server/client found no session exit-code branch the plan misses. These are all listed
  with a disposition:
  - `AgentSessionRuntime.cs:259`
  - `SessionReconciliationService.cs:775`
  - `AgentSessionService.cs:906,2052`
  - `AgentControlService.cs:533`
  - `RunnerTerminalSession.cs:365`
  - `AgentTaskLiveness.cs:157-165` (`DescribeStop` "exited on its own")
  - `AgentTaskDispatcher.cs:3092-3097,3190-3204`

  The client has no `exitCode === 0` branch. No zero code becomes Succeeded, and report-first settlement is preserved.
- **Windows:** ConPTY is unchanged; R-1 and R-2 (5 `PtyKillProcessTreeTests`, confirmed count) run in CP-2 at the
  final source.
- **Importer:** `import --plan` gave 12 rows, exit 0.

  | Rows | Check | Values |
  |---|---|---|
  | CP-1..12 | `minExecuted` | 6/7/10/6/26/6/3/n.a./8/6/6/2 |
  | CP-1..12 | estimates | 4/7/6/6/11/1/2/2/6/6/9/2 |
  | S1/S2/S3/S4/Windows | group sums | 4/12/16/23/7 |

  All match the roster. Existing counts confirmed: AgentTaskLivenessTests 6, the 3 R-4 methods exist, and the
  `slotWaitMinutes: 45` / `timeoutMinutes` lines that the pin recipe edits exist.
- **Git:**
  - rc merge-tree vs `rc/2026-10-27` tip `e4c9547e1`: clean.
  - master `698bf1b7` merge-tree: 12 conflicts, none on the plan path (matches the plan).
  - `check-evidence-diff.ps1 -BaseRef 4e67121ef… -HeadRef HEAD`: 9 commits, 0 violations.
- **Land:** classified as a fix, rc first with a separate master port. CARD-1610 and CARD-1614 coordination is stated
  correctly.

## Not run
No product tests. This is a plan review, and none of the planned witnesses exist yet. Every CP row and PC stays owed to Code, Mutation and native Review.
