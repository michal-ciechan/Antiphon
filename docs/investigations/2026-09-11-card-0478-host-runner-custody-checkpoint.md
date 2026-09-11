# CARD-0478: Incomplete host/runner custody Code checkpoint

Date: 2026-09-11. Code task: `0505ce7d`. Base: `07652c0e`.
Branch: `feat/card-task-0505ce7d`.

**S1-S5 and all ordinary V/R are not complete. Do not land or deploy this checkpoint.**
This pass implemented and tested the durable host/runner portion of custody, but
did not implement application admission, attempt reservation, guarded cleanup or
the active workflow reorder. No native/platform blocker or missing operator
decision was found. Continue Code; this is not ready for ordinary Review.

The authorities are the [original plan](../superpowers/plans/2026-09-10-card-0478-mutation-after-land-plan.md)
and [runner-custody amendment](../superpowers/plans/2026-09-10-card-0478-runner-custody-amendment.md).
The [native checkpoint](2026-09-11-card-0478-native-custody-checkpoint.md) describes
the inherited foundation. All **230 positive controls remain pending**. This
pass introduced no deliberate compiling defect and executed no positive control.

The requested old branch was already checked out in
`C:\Antiphon\worktrees\card-task-8d562a1a`. This clean task worktree was therefore
based on its fetched `07652c0e` using its own branch, without changing that other
worktree. Implementation commits:

- `032dd2ce`: durable host/runner verification custody receipts.
- `96f0c17c`: legacy refusal compatibility and replaced-store fencing.
- `ab72f430`: explicit kill-all includes tracked descendants after root exit.

## Implemented runtime contract

- `SessionRunner.Contracts/VerificationCustody.cs` defines immutable required
  binding/source/generation/creation records, explicit custody states, original
  host/container/store identity and exact receipt bytes. A binding additionally
  includes the expected `RunnerStoreId`, to be captured from capabilities and
  persisted by the application before any launch I/O. This prevents an old B
  from running against a wholly replaced ledger. Integrate and align this extra
  field with the plan's grouped guard inventory before claiming complete Code.
- `PtyHost.Protocol/VerificationCustodyStore.cs` provides execution-keyed,
  immutable files outside the snapshot. Writes use a unique temporary file,
  write-through, explicit `Flush(true)` and atomic non-overwriting rename.
  Identical replay is accepted; conflicting bytes are retained and refused.
  A sibling store-identity anchor detects a lost ledger directory. Existing,
  missing, malformed and replaced identity/history are not an empty runner.
- `PtyHost/HostCustodyJournal.cs` connects the native journal to durable intent,
  original host/job identity, Tracking-before-resume, irreversible producer seal
  and terminal receipt. A failed native-intent write retains uncertainty.
  ANSI failure, nonempty accounting and failed drain cannot certify Exited.
  The producer receipt precedes the terminal reply. Shutdown requires matching
  runner-accepted bytes; linger expiry/lost observer does not manufacture proof.
- Host launch/custody serialization rejects late input/launch after seal. An
  ordinary read of a running root does not seal it. Root exit retains periodic
  observation until descendants reach zero and host I/O drains. Tracked hosts
  have external host CWD, logs, manifest and ledger; the provider uses B's tree.
  Tracked sessions disable the older optional PTY audit, whose independent timer,
  TTL and environment dump do not belong to the new output-drain contract.
- The host handshake carries an optional feature and host instance ID. The
  client sends no new tracked protocol messages to a legacy host. Ordinary
  launch remains compatible. Unsupported backend/protocol refuses explicitly.
- `SessionRunner/RunnerCustodyLedger.cs` retains binding, start intent,
  runner-observed host identity, seals and receipts independently of the session
  registry. Per-session admission includes an OS file lease. Same B replays
  without another spawn; changed B and unresolved SessionId reuse refuse,
  including an untracked launch or Herdr attach omitting B.
- The runner validates the producer against its own handshake identity and
  intact launch history, drains the transcript tailer, then persists identical
  accepted bytes before returning them. Retained host/running discovery manifests
  allow recovery when the disposable manifest is missing. They never substitute
  for the original job or receipt. Recovery can accept a producer receipt left
  before both host and runner died, without creating a replacement observer.
- Internal GET custody and POST seal routes are execution/generation-bound.
  Seal is durable before observation and never kills. Explicit individual kill
  and kill-all remain effective after tracked root exit. Root-exit status alone
  does not permit shutdown/relaunch. Legacy untracked refusal paths avoid creating
  the custody ledger as a side effect.

Storage is under `<SessionLogPath>\verification-custody\<ExecutionId:N>\`, with
`verification-custody.identity.json` beside the ledger directory. Do not delete
these as session manifests, TTL logs, build outputs or worktree residue. The
existing local trusted control boundary is assumed; this is not protection from
a malicious same-user process or administrator.

## Ordinary evidence

Windows 10.0.19045 x64, .NET runtime 9.0.16. Actual modern ConPTY, detached
PtyHost and, for the crash cases, independent HTTP SessionRunner processes.
Process tests use each assembly's own one-wide limiter. The three assemblies
ran sequentially, with isolated outputs and compiler/build-server reuse disabled.
Fixtures use compiled local peers, named-event barriers and owned process handles;
they do not use a paid provider or the production runner on port 17204.

| Fresh final run | Expanded cases | Passed | Failed | Skipped | Report |
|---|---:|---:|---:|---:|---|
| Host/protocol/store/compatibility | 45 | 45 | 0 | 0 | `.antiphon/c478-0505-host-final-3/host-final.trx` |
| Runner/custody/crash/adoption/compatibility | 46 | 46 | 0 | 0 | `.antiphon/c478-0505-runner-final-3/runner-final.trx` |
| Native custody/backend/kill/DA1/paste | 43 | 43 | 0 | 0 | `.antiphon/c478-0505-native-final/native-final.trx` |
| **Total selected tests** | **134** | **134** | **0** | **0** | |

The final host run preceded `ab72f430`, which changes only runner kill-all and
its test. The final runner/native runs include that commit. TRX test definitions
were inspected to confirm every intended class had nonzero execution:

- Host: HostCustodyTests 10, VerificationCustodyStoreTests 20,
  HostSessionPipeTests 7, FramingTests 4, PtyHostLauncherTests 4.
- Runner: RunnerCustodyTests 9, RunnerCustodyCrashTests 2,
  PtyHostAdoptionTests 6, RunnerStartupReadinessTests 2,
  RunnerCapabilitiesTests 4, HerdrAttachTests 23.
- Native: PtyCustodyTests 23, PtyKillProcessTreeTests 5,
  PtyBackendContractTests 9, ModernPtyDa1Tests 4,
  PtyBracketedPasteContractTests 2.

Host cases cover real flush/rename ordering and failures, receipt replay/conflict,
required fields/crossed identity variants, pre-resume Tracking persistence failure,
post-drain persistence failure with retry on the original job, unsupported and
indeterminate native startup, no acknowledgment-only shutdown, sealed late input,
and ANSI drain failure. Store-only seeded receipt tests are validator evidence;
they do not claim native process-exit acceptance.

Runner cases cover real receipts across replacement, live-host adoption with or
without the ordinary manifest, dead-root orphan/detached/new-console descendants,
explicit kill-all, observer loss, seal beating launch, binding/reuse refusal,
and actual process crashes. The HTTP crash fixtures restart an isolated runner
against its retained store, verify capability identity, distinguish GET from
POST seal, reject late input/wrong generation, and recover exact receipt bytes
after producer persistence followed by host death before runner import.

Rerun from `C:\Antiphon\worktrees\card-task-0505ce7d`, choosing fresh result
directories instead of overwriting the evidence above:

```powershell
$env:MSBUILDDISABLENODEREUSE='1'
dotnet run --project tests/Antiphon.PtyHost.Tests --property:OutputPath=bin-c478-0505/ --property:UseSharedCompilation=false -- --treenode-filter '/*/*/(HostCustodyTests*)|(VerificationCustodyStoreTests*)|(HostSessionPipeTests*)|(FramingTests*)|(PtyHostLauncherTests*)/*' --report-trx --report-trx-filename host.trx --results-directory .antiphon/c478-0505-host-rerun
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c478-0505/ --property:UseSharedCompilation=false -- --treenode-filter '/*/*/(RunnerCustodyTests*)|(RunnerCustodyCrashTests*)|(PtyHostAdoptionTests*)|(RunnerStartupReadinessTests*)|(RunnerCapabilitiesTests*)|(HerdrAttachTests*)/*' --report-trx --report-trx-filename runner.trx --results-directory .antiphon/c478-0505-runner-rerun
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c478-0505/ --property:UseSharedCompilation=false -- --treenode-filter '/*/*/(PtyCustodyTests*)|(PtyKillProcessTreeTests*)|(PtyBackendContractTests*)|(ModernPtyDa1Tests*)|(PtyBracketedPasteContractTests*)/*' --report-trx --report-trx-filename native.trx --results-directory .antiphon/c478-0505-native-rerun
dotnet build server/Antiphon.Server.csproj --property:OutputPath=bin-c478-0505/ --property:UseSharedCompilation=false
```

Host and runner standalone builds succeeded without errors. The server compatibility
build succeeded with 0 errors and two warnings in unchanged AgentService and
CapacityRecoveryService. An initial `--no-restore` server build failed NETSDK1004
because the fresh worktree lacked assets; the normal restoring build passed.
Test builds reported existing nullable warnings in unchanged test methods.

Development failures were resolved before the final runs: one assertion's nullable
generic overload, a replacement-runtime fixture missing the actual adoption pass,
a test that assumed GET implicitly sealed, and a real legacy Herdr refusal
regression that created the session-log directory before refusing. The latter
was corrected by initializing/checking custody only when history exists or the
request opts in. The exact final run counts above exclude those earlier failed runs.

The duration tripwire found no new custody case at or above five seconds. It
reported five existing compatibility rows: PtyHostLauncherTests shadow reuse,
two PtyHostAdoptionTests exit-while-down cases, and both startup-readiness variants.
The native run had none. No allowlist changes were made. Final process inspection
found zero owned custody helper/host/runner fixtures alive. `git diff --check`
passed. Outputs and reports remain local ignored artifacts; they were not deleted.

## Remaining Code, in dependency order

1. **Finish the full S3a/S3b ordinary matrix.** Existing tests are partial
   V-14/V-15/V-16 evidence, not all required method/variant oracles. Extend native
   creation/first-instruction fault seams and prove the actual nested
   Win32ProcessSpawner access-denied-to-plain-detached path. Complete crash cuts,
   timeout/corruption/backend variants, monotonic-history and no-start oracles.
   Review every path against the amendment rather than equating green selected
   suites with the full V/R. Current runtime path validation is lexical; it does
   not prove reparse/symlink canonical identity. Application creation and cleanup
   must use the real canonical Git/filesystem authority and test aliases.
2. **S1/S2 application admission and reservation.** Add SourceLanding DTO/CLI,
   authorization/project/repository checks, distinct same-board companion and
   same-operation serialized admission. Create the fresh exact-L snapshot with
   immutable creation metadata. Add restricted source FK and append-only
   VerificationExecution/task-seal metadata via CLI-generated EF migration.
   Persist B (including expected stable RunnerStoreId) in the reservation
   transaction before enqueue/I/O, plus monotonic runner-call intent. Fence all
   cold/relaunch/recovery/queue paths; never reconstruct B from a later session.
   Prohibit sourced warm/standing ownership, autosave, merge and explicit land
   in callers and underlying services. No application draft was applied here.
3. **S3c importer and real cleanup.** Import exact receipt bytes/digest/provenance;
   atomically freeze all accepted attempts under admission locks. Use a fresh
   DbContext evidence reader under the genuine Git lease before the first output
   deletion and again at final removal. Require source/creation identity,
   restoration, external evidence, all-attempt custody, exact owned outputs and
   RepositoryChildJournal exclusion. Implement the typed verification-only
   cleanup service/API/CLI and positive real receipt-to-removal acceptance.
   Unknown/unsupported/missing attempts retain residue; cleanup never kills.
4. **S4 coherent workflow integration.** Update all active stage bundles,
   orchestration/delegate/ops/testing/card owners and CLI as one complete release:
   Code -> ordinary Review -> original Code landing -> companion SourceLanding
   Mutation. Preserve vocabulary, ordinals, historical reports and card statuses.
   This checkpoint deliberately does not enable the new default recipe.
5. **S5 all original/amended ordinary V/R.** Add/run the application, delivery,
   persistence, launch-race, card lifecycle and cleanup tests required by both
   plans. Align the exact 230 guard/method/variant inventory, expanding it if new
   independently bypassable guards require controls. Keep all PCs pending.

The prior unverified application draft remains only at
`C:\Antiphon\worktrees\card-task-8d562a1a\.antiphon\task-8d562a1a-application-unfinished.patch`.
Its old `VerificationBinding` shape does not match this runtime contract. Reuse
reviewed ideas individually; do not apply it wholesale or claim its missing
cleanup endpoint/migration/all-path fences exist.

## Bootstrap/release boundary

Resume Code from this branch, finish S1-S5 and every ordinary V/R, then return
`next: review`. No operator answer is required merely to continue. This task
made no live card, dispatch, stack, landing or deployment change.

After complete Code and ordinary Review, the caller records the companion
verification obligation, lands the original Code owner and deploys the complete
server/runner/host feature through the canonical main checkout. Verify the actual
loaded source selector, bundle composition and runtime tracking capability.
Only then commission CARD-0478's pending PCs in a fresh SourceLanding Mutation
worktree at the confirmed L. Neither this checkpoint nor a healthy old stack is
authority to run that bootstrap early.
