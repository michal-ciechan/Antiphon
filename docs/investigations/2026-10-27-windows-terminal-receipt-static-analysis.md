# Windows terminal/receipt failure analysis — task 9d3dc152

## Outcome

There is no demonstrated single Windows terminal defect. Nineteen of the 23 selected failures encounter identifiable Claude fixture-contract errors: unreadable fake composers, missing/mismatched accepted generations, missing conditional forwarding, or an expectation that Claude can receive automated input through Herdr despite its deliberately unknown input custody. The inspection fairness failure is a separate fixture race with an obsolete capacity assertion. Grok confirmation, remote-control cleanup and poison-page execution remain three individually unresolved failures. Native qualification remains incomplete.

Three fixture repairs remain visible at the inspected source: bind the receipt world's database generation to its launch, change the Claude/Herdr success expectation to the required refusal while retaining recovery coverage through a supported provider, and make the inspection fairness schedule deterministic. No product change is justified by the confirmed mechanisms. Do not relax CARD-0680, combine Enter with the body, suppress cleanup failures, or widen the poison-page timeout on this evidence.

Only this report is changed and pushed. No product/test source, desktop, native Windows test, service or tracker was changed. Portable Linux verification executed three existing tests and ten additional checks against compiled production types; all passed. The checkpoint wrapper itself failed after a successful build, before running tests, as documented below.

## Evidence identity and limits

| Item | Identity |
|---|---|
| Assigned branch and source | `feat/card-task-9d3dc152`; `718bd29f4a1504c64c491d607d31403fcb87dae2` |
| Fetched rc tip | `origin/rc/2026-10-27` = `d1d090f766451797a3bd9b30c9d2966f2f8c034c` at inspection; assigned branch retained under its fast-forward-only contract |
| Original Windows qualification | source `105792f7d22f297746cdb338976f52e424b76fa7`, run `8f055f87452d4c76998604df0b522db5` |
| Early digest | `origin/feat/card-task-d5c669aa` = `9d7bd87b67c786b28f50920c68cfd0611ab630a5`; `docs/investigations/2026-10-26-rc-8f055f87-windows-failure-digest.md` |
| Final digest | `origin/feat/card-task-7e719c57` = `c7ad9e901c6be169fb5a0f43be8e210211e9081b`; `docs/investigations/2026-10-27-rc-8f055f87-final-run-digest.md` |
| Portable build/test source | `5e9b4708b889e75d127b7b1107ea761154a76200`, clean; preliminary version of this report only, product/test source identical to assigned base |
| Native follow-up owners | Separately running tasks `32365cdf`, `fb80be16`, `595bc226`; no result inferred for them |

The final digest records 140 failed instances across the whole run; this report accounts for the brief's 23 instances in eight classes, using the shared F-number catalog. Windows raw artifacts reside on the desktop and were not accessed. Exact excerpts and candidate stack lines below come from the fetched digests. Other file:line references refer to the assigned source, also used for the portable build.

The premise that Linux passing proves a Windows-only defect is too strong. The native queue, Grok, receipt and remote fixtures skip off Windows; the named Herdr parity method explicitly skips Linux (`tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs:62`). A selection exiting successfully with skips is not evidence that the same terminal path passed. The fairness test also has a portable timing-dependent outcome. Earlier Linux rerun reports have different source identities and do not qualify this Windows candidate or close current native fixes.

## Claude delivery boundary

The relevant path is queue admission → current dialog observation → conditional body write → fresh conditional Enter → transcript confirmation. A refusal can happen before the first backend byte.

- `server/Application/Services/SessionMessageQueueService.cs:3876` checks the modal/input guard before writing. `:5730` obtains the database session generation and a fresh Claude observation; a refusal leaves work Pending without a delivery attempt. `:4056` handles a body refusal, `:4141` sends a separate carriage return, and `:4163` preserves composer custody if Enter is refused after typing.
- `server/Application/Services/ProviderDialogObservationService.cs:26` rejects missing observations, wrong session/epoch, unknown input history and a different accepted generation. Readable `Clear` is useful only with matching identity and known history.
- `server/Application/Services/AgentSessionRuntime.cs:2143` repeats the fence at the conditional backend boundary; `:2151` routes non-Claude agents through their interactive path. `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:2605` does not invent an accepted generation for a launch whose value is null.
- `tests/Antiphon.Tests/TestHelpers/ClaudeDialogFixture.cs:13` supplies the intended fixture opt-in: `ANTIPHON_FAKE_CLAUDE_COMPOSER=1` and a normalized `AcceptedStartedAt`. The database must use that same value, rather than sample the clock again.

These checks explain why failures can show no body, no swallowed Enter, and no injected transport exception. They occur upstream of the ConPTY behavior those tests intended to exercise. The portable checks reproduce the refusal conditions; they do not assert that every downstream native behavior will pass after repairing the fixtures.

## Per-class attribution and minimum repairs

### 1. SessionMessageQueuePtyIntegrationTests — five failures

Windows F007/F012 report `submitted` expected True, actual False (candidate lines 134/389; about 12.12 seconds). F008 lacks `SWALLOWED-ENTER:` (line 697); F011 records an empty submission collection `[]` (line 512); F013 expects four split segments from three swallowed Enters but gets one (line 776). F013 raw output contains the Fake Claude ready/SWALLOWENTER banners, not proof of attempted Enter writes.

**Classification: fixture contract.** At the Windows candidate, launches lack both the readable composer opt-in and an accepted launch generation; database `StartedAt` is independently sampled. The input guard can therefore withhold all payload and Enter writes. Missing Enter markers are not evidence of a ConPTY loss when no conditional write was admitted.

**Minimum repair:** use `WithNativeClaudeDialog()` on each launch and seed `StartedAt = spec.AcceptedStartedAt!.Value`; where a helper starts the session, take the runner's accepted generation. These changes already exist in inspected commit `55c091084e6815f50ab9bc5530db812d98676cbf`. Preserve the separate Enter/retry and recipient-transcript assertions.

**Portable witness:** existing `WindowsDeliveryFixtureWitnessTests.Native_fixture_requires_both_readable_composer_and_the_launched_generation` checks old banner-only output is refused, a readable composer with another generation is refused, and a matching one is allowed. The additional line-ending/ANSI witness below confirms the body/Enter ordering in the real portable sender. Exact native closure remains with the Windows tasks.

### 2. DelegationBriefCeilingPtyTests — two failures

F035's pointer-path submission is false at candidate line 108; F036's inline submission is false at line 149. Their raw captures contain only ready/CLIP banners (about 17.45/16.41 seconds).

**Classification: fixture contract before the clipping oracle.** The same native Claude launch mismatch prevents testing the brief ceiling. `tests/Antiphon.Tests/Application/DelegationBriefCeilingPtyTests.cs:299` now opts into the native Claude fixture and `:317` uses its accepted generation, in `55c091084e6815f50ab9bc5530db812d98676cbf`.

**Minimum repair/witness:** retain those changes and the existing recipient-body/pointer assertions. The guard witness above forces the old and corrected admission behavior portably; it does not validate actual native clipping or long-screen rendering.

### 3. SessionQueueReceiptPlumbingTests — nine failures

F015 explicitly reports `Status = Pending`, `DeliveryAttempts = 0`, `ModalBlocked = True` at candidate line 172. F016/F017/F018/F019/F023 do not reach expected transport/fault boundaries; F020/F021 lack recipient receipts; F022 expects bracketed payload plus separate Enter but records `[]` at line 563.

**Classification: two fixture errors, with one still present.** First, the Claude pre-write guard withholds input. Second, the candidate `ForwardingClient` forwards raw `SendInputAsync` only; the default `ISessionRunnerClient.SendConditionalInputAsync` at `server/Application/Interfaces/ISessionRunnerClient.cs:141` returns Unsupported. Thus even after fixing observations, the guarded path cannot exercise the wrapper's raw fault hooks.

The conditional forwarding fix is present in `aedec3f9f1474ee4597f0326f1fccd58bea64a08`; `tests/Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs:836` routes conditional body/Enter through the common `WriteAsync` fault boundary. Its portable fault test passes.

**Remaining minimum repair:** `PtyWorld.StartAsync` calls `WithNativeClaudeDialog()` at `:643`, then separately samples `now` and seeds `StartedAt = now` at `:651`. Change only that `StartedAt` initializer to `spec.AcceptedStartedAt!.Value`; CreatedAt/LastSeenAt may still use their observation time. The normalized launch generation is chosen before awaiting the child launch; a later clock sample cannot be relied on to equal it.

**Portable witness:** existing guard/fault tests plus the additional production-refusal check force distinct generations and demonstrate rejection even with a Clear composer. Add a fixture-setup regression that exercises the actual world generation binding with different launch/seed clock values, then asserts matching generation, Clear observation, conditional body and separate Enter. The existing helper-only witness does not cover the erroneous database initializer and therefore missed it. No weakening of generation matching is warranted.

### 4. HerdrAlwaysOnChannelParityTests — two failures

F005 (PtyHost) expects Sent but sees Pending at candidate line 218. Its two scripted adapters lacked dialog observations; `tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs:80` now uses `WithClaudeDialogObservation()`, added by `3ab8579b3de9e018a809248f46847203c3fd2e1e`.

F006 (Herdr) times out at candidate line 712, called from line 107, with the decisive session reason:

```text
Automated Claude input withheld: observation-unknown:None::NonQualifying
```

**Classification: fixture expectation contradicts the product contract.** This is stronger than missing composer chrome. `docs/session-runtime-invariants.md:1495` (CARD-0680) explicitly states that Herdr lacks complete input custody and reports Unknown. A successful Herdr pane read in `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:3974` creates a `ProviderDialogState.Unknown` observation at `:3981`; generic observation at `:2513` makes known history conditional on the PtyHost backend. A prettier fake screen cannot satisfy that custody requirement.

**Remaining minimum repair:** in the exact method `AlwaysOn_channel_bound_survives_child_death_and_replies`, branch the Claude/Herdr case before the Running-session wait at `tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs:107` to assert startup refusal and no automated bytes/receipt, reusing `RequireClaudeHerdrInputRefusedAsync` at `:590`. Retain successful Herdr child-death/recovery/reply coverage using a supported provider such as Grok. Keep the PtyHost success case and its corrected observation adapter. Do not fabricate Herdr input custody or simply delete the recovery assertions.

This is already described by the CARD-1575 investigation `docs/investigations/2026-10-24-card-1575-herdr-claude-parity-tests-expect-automated-input-despite-unknown-custody.md`. Repair `c5ab17338632422905b4c3029b79abe6c0dd1100` covered other parity cases; this Linux-skipped method still expects success. Reuse that follow-up rather than infer a new terminal policy.

**Portable witness:** the additional check passes a Clear observation with `InputHistoryKnown=false` through production `Refusal` and confirms rejection; source inspection establishes that Herdr cannot provide true. A follow-up fixture test should exercise the real runtime/FakeHerdrServer refusal and its zero-write assertion. The preliminary report slice's proposed Herdr composer repair is superseded by this custody finding.

### 5. RemoteControlRecoveryPtyIntegrationTests — two distinct failures

F010, `C514_Health_request_reaches_busy_and_idle_recipients`, cannot find the initial work prompt in the child's own JSONL at candidate line 39 after 20 seconds. **Fixture contract:** its shared lane already binds an explicit generation, but the candidate has no readable Claude composer. `tests/Antiphon.Tests/TestHelpers/RemoteControlPtyLane.cs:131` now applies `WithNativeClaudeDialog()` in `55c091084e6815f50ab9bc5530db812d98676cbf`. Retain that repair; use the guard witness and exact Windows recipient receipts to validate it. This failure precedes the intended remote-control recovery assertion.

F009, `C514_Idle_modal_clearance_delivers_three_complete_prompts`, instead throws:

```text
AggregateException: test-owned disposal unresolved elapsed=00:00:00.0971358 (cleanup-unresolved elapsed=00:00:00.0915945 (capture session=... manifest=...))
```

Its digest stack names `TestOwnedPtyHost.DisposeCoreAsync:157`, `RemoteControlPtyLane.DisposeAsync:342`, and the test at line 117. **Root cause and product-versus-fixture ownership remain unresolved.** The deepest capture exception is absent from the digest; this text does not prove file sharing, an identity race or an unkillable process.

`tests/Antiphon.Tests/TestHelpers/TestOwnedPtyHost.cs:99` reads the manifest, captures the process identity, checks its command and enumerates descendants. `:116` wraps any exception as `capture session=... manifest=...`; `:163` preserves it in cleanup failures, and `:157` reports aggregate disposal failure. A capture error remains reportable even if the process later exits. Disposal can mask the preceding test assertion.

**Minimum next change:** before choosing a repair, capture full `Exception.ToString()` including the deepest inner stack, owned cleanup operation records, and the pre-disposal assertion outcome in the existing native rerun. Preserve both primary and disposal failures. A portable fault witness can inject a capture exception through the test-owned IO seam and assert that its identity/inner cause survive successful later shutdown; that would validate diagnostics, not identify the historical Windows cause. Do not suppress capture failures or alter process ownership policy.

### 6. SessionMessageQueueGrokPtyIntegrationTests — one failure

F027 fails after 53.9168666 seconds with the exact digest message:

```text
ConflictException: Message delivery could not be verified — the terminal did not accept it (the submitting Enter produced no output). See the agent's incidents.
```

The stack ends at candidate `SessionMessageQueueService.EnqueueCoreAsync:661` and `tests/Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs:212`, whose body is the single-line literal `queued hello`.

**Classification: unresolved; not the Claude guard mechanism.** `AgentSessionRuntime.cs:2151` and queue `:5738` bypass that guard for Grok. This body does not exercise multiline CRLF normalization or bracketed paste. The wording associated with `NoSubmitOutput` is a verification verdict, not a raw terminal capture proving the operating system dropped Enter.

The fixture uses an inbox transcript backend. `DirectSessionRunnerClient` defaults `grokTranscript=true` and selects the Grok transcript format; absence of a separately registered push-ingestion pump does not by itself explain this failure because queue verification pulls transcript catch-up. The queue has a 30-second confirmation interval followed by a 20-second grace path, consistent with the observed duration but not proof of which evidence was absent.

One portable mechanism is demonstrated: FakeGrok appends `SUBMITTED:queued hello` / response text containing the body (`src/Antiphon.FakeGrok/Program.cs:695`, `:852`). The Grok positive screen fallback at `SessionMessageQueueService.cs:4268` requires an emptied composer. `SubmitEvidence.IsEmptiedComposer` looks for the body in the screen, so an append-only screen retaining that body fails this fallback even after a submission. This alone cannot explain the absence of a qualifying native UserPrompt; therefore it is not claimed as the historical root cause.

**Minimum next change:** at the fixture's enqueue call (`:212`), preserve failure diagnostics before disposal: actual backend input writes, raw and rendered snapshots before/after Enter, the resolved FakeGrok `updates.jsonl` path and complete prompt record, runner transcript events, persisted transcript rows and persistence exceptions. If the recipient record exists, isolate tailing/binding/persistence/correlation. If raw output contains `SUBMITTED:` but the file record is absent, inspect the fake's file append: `src/Antiphon.FakeGrok/Program.cs:1019` silently catches write exceptions, so add test-only diagnostic capture of that exception. If neither recipient marker exists, isolate the child's input/Enter reads. Select a product or fixture repair only after that split. A portable tailer witness should replay the exact captured record and binding; a terminal witness should replay the exact captured input chunks. No parser/transport fix is established by the digest.

### 7. DispatcherSiblingInspectionSchedulingTests — one failure

F001, `C1349_InspectionQueueRecovers("fairness")`, expects Outstanding 0 but sees 1 at candidate/current `tests/Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.cs:377`.

**Classification: deterministic mechanism behind a timing-dependent fixture assumption.** The first tick leaves `Git.Block=false` for fairness (`:298`), so two Git workers can finish while 33 requests are enqueued. A fast schedule admits all 33; a slower schedule saturates 32 in-flight slots and refuses #33. After the admitted work completes, the fixture waits for idle. Completed results consume zero in-flight slots, even while retained.

`server/Application/Services/SiblingInspectionResults.cs:66` checks in-flight capacity, distinct from its retained-result limit. On the next dispatcher tick, `AgentTaskDispatcher.cs:665` orders never-attempted #33 before held requeues. It can now admit #33, then consume/invalidate the original 32 held results at `:898`. Outstanding is correctly 1. The fixture comment at `:376`, “the full queue cannot admit #33 yet,” contradicts current capacity semantics. On the fast schedule all 33 results can instead be consumed and Outstanding becomes 0, explaining a Linux pass without any OS-specific queue code.

**Remaining minimum repair:** add fairness to an explicitly controlled initial worker barrier so exactly 32 requests are admitted and #33 is refused; release and join that wave; install a fresh gate for the next wave before ticking; assert that #33 is attempted first and remains outstanding while the 32 holds are consumed. Release/join before any final assertions and retain the 32-slot bound plus unattempted-before-requeued invariant. Reusing an already-completed release TaskCompletionSource is not a fresh barrier. Do not change production capacity to count completed results.

**Portable witness executed:** the real `SiblingInspectionResults` type produces Outstanding 1 for the forced slow ordering and 0 for the forced fast ordering. This is a production capacity-state witness, not a replay of the full dispatcher/database fixture; the dispatcher ordering and invalidation source establishes the connection to its failing assertion.

### 8. SessionRuntimeActionTests — one failure

F003, `C1259_saturation_keeps_ingestion_live_and_recovers_overflow("poison-page")`, times out after 11.9641459 seconds at candidate/current `tests/Antiphon.Tests/Application/SessionRuntimeActionTests.cs:580`:

```text
TimeoutException: The operation has timed out.
```

It is the ten-second wait for `firstPage`, triggered by UI callback `page-128`; it is not the later assertion that session B runs before A's second page (`:590`). Three of four workers are deliberately held on other sessions. The remaining owner must advance through 127 actions and reach action 128 before the wall-clock bound.

`server/Application/Services/SessionRuntimeActionCoordinator.cs:149` drains actions sequentially. The eight routing phases (`QueuePhase=8`, `:20`) each persist progress at `:262`; non-boundary action deletion adds a save at `:269`. At least 127 × 9 = 1,143 saves, plus row queries and phase work, precede reaching the action-128 callback. More than about 8.75 ms per save on average would alone exceed ten seconds. This is a throughput-sensitive setup for an ordering test.

**Classification: unresolved; timing assumption is a supported hypothesis, not a proven Windows database diagnosis.** The digest contains no last action/phase, retry state or database duration. Slow forward progress and a retained failing action both fit the timeout; one cannot conclude continuation loss or blame ConPTY.

**Minimum next change:** instrument the failure at `:580` with the last completed sequence, active owners, pending action phases/attempts/failure reasons/due times, and database/phase timing. A portable witness can inject controlled database latency through the test fixture's command interceptor, then contrast monotonic progress with an injected phase failure. Use those observations to separate the setup/performance budget from the actual page-yield oracle, with a measured bound and deterministic barriers; do not silently enlarge the timeout or change production page size. This proposed database witness was not executed in this task. A product performance repair is justified only if the timing/phase evidence establishes it.

## Portable verification, provenance and costs

All drivers ran sequentially on server2 through the host build-slot gate. No Windows/native process test was launched. Product/test source was clean and frozen at `5e9b4708b889e75d127b7b1107ea761154a76200` for the build and both passing runs. The guard/refusal, inspection-results, input-encoding, echo-gated sender, screen renderer, submit-evidence and action-coordinator source files are unchanged between Windows candidate `105792f7d22f297746cdb338976f52e424b76fa7` and that build source. This makes their portable mechanisms relevant while preserving the distinction from native qualification.

| Execution | Result | Cost / limitation |
|---|---|---|
| Checkpoint argument validation using an abbreviated ExpectedSourceSha | exit 2, no build/tests | Caller invocation error; corrected to the full SHA |
| `debug-9d3-guard` isolated build | underlying build exit 0, 0 errors, 1,221 warnings; checkpoint exit 2, zero tests | Build 4:54.82; slot held 354 s, wait 0 s, maxcpucount 6 |
| Existing `WindowsDeliveryFixtureWitnessTests` via gated no-build runner | 3 passed, 0 failed, 0 skipped | Test duration 706 ms; slot held 4 s, wait 0 s |
| Initial additional witness script | 7 assertions passed, then harness API error | Used `TerminalScreen.GetText`; corrected to actual `GetScreenText`. Not a product-red result |
| Final additional witness script | 10 checks passed, 0 failed | Slot held 1 s, wait 0 s; real compiled production types, scripted inputs/orderings |

The checkpoint failure is an independent tooling limit: the built `Antiphon.Tests.dll` is 68,894,720 bytes, exceeding `Read-CheckpointBytes`'s 67,108,864-byte default at `scripts/lib/checkpoint-file-reader.ps1:196`. Output inventory sealing at `scripts/lib/checkpoint-owned-output.ps1:28` therefore fails after compilation, before tests. The wrapper reports `build=failed` despite the compiler succeeding. This should be handled as a separate checkpoint-inventory follow-up; no script repair was made here and the checkpoint is not green. Direct witnesses used the already built output under a new slot with explicit source/assembly provenance; they do not retroactively repair the checkpoint receipt.

Essential checkpoint lines, unedited:

```text
CHECKPOINT debug-9d3-guard build=failed project=tests/Antiphon.Tests outputPath=bin-debug9d3/ exit=2 slot=granted waited=0s
CHECKPOINT debug-9d3-guard commit=5e9b4708b889e75d127b7b1107ea761154a76200 build=failed filter=/*/*/WindowsDeliveryFixtureWitnessTests/* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-9d3dc152/.antiphon/debug-9d3dc152/debug-9d3-guard-20261027-051913-620e/run.trx slot=granted waited=0s dirty=0 source=5e9b4708b889e75d127b7b1107ea761154a76200 sourceState=clean buildSource=unknown
CHECKPOINT debug-9d3-guard EXIT CODE: 2
```

The three existing passing tests were:

1. `Native_fixture_requires_both_readable_composer_and_the_launched_generation` — 0.0266446 s.
2. `Receipt_faults_intercept_the_guarded_body_and_the_separate_Enter` — 0.0683909 s.
3. `Posix_recipient_rules_receipt_uses_the_remote_grammar_on_a_Windows_host` — 0.0144385 s. This is ancillary to the requested failures and is not evidence that F027 passed.

The ten additional checks covered four inspection-capacity orderings, matching/mismatched generation, unknown Herdr input history, forced CRLF and CR bodies with character-split ANSI screen output, and Grok's retained-body screen fallback. The line-ending cases require one LF-normalized bracketed paste followed by a separate `\r` only after the tail is visible. These checks rule out an inevitable portable normalization/coalescing error for those inputs; they do not emulate native ConPTY scheduling or certify swallowed-Enter recovery.

Build environment: Debian 12, test runtime .NET 9.0.22, build SDK 10.0.403. The saved clean-source fingerprint is `b397558db4ef32f465cabf4f182117e469175e12f88f7d1ef88732f64e84db70`.

| Built assembly | Bytes | SHA-256 |
|---|---:|---|
| Antiphon.Tests.dll | 68894720 | `65cac2c19ac1310aad9c8f0cec1e537dfcde51a47433ac02ce35c95da2b2ea73` |
| Antiphon.Server.dll | 29453312 | `24a2f6a5c59cc071f3a0d1e83ce6cab4a41cc40138acae2754d2a84a15b56c2e` |
| Antiphon.Agents.Pty.dll | 256000 | `a1ccc524096eb0418b2687cc3d2bdb3af3eab59b6c18681076a25235037ecd45` |
| Antiphon.SessionRunner.Contracts.dll | 468480 | `d5d95a4588c62a4fa5f0738937d7aabe486c8e35b016988b175a265d0c9d5efd` |

Ignored local evidence is under `.antiphon/debug-9d3dc152/`: checkpoint logs, `checkpoint-build-source.json`, `compiled-provenance.json`, `direct-guard.log`, `direct/guard.trx`, `mechanisms-initial-api-error.log`, `mechanisms.log`, and the witness script. These generated files are not committed. Essential provenance/results and the reproducible witness are preserved here because the mirror may be retired. The task-owned `tests/Antiphon.Tests/bin-debug9d3/` output is removed after verification; no other task's output is touched.

### Exact commands and additional witness

The checkpoint command was:

```sh
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name debug-9d3-guard -Project tests/Antiphon.Tests -OutputPath bin-debug9d3/ -Filter '/*/*/WindowsDeliveryFixtureWitnessTests/*' -MinExecuted 3 -ExpectedSourceSha 5e9b4708b889e75d127b7b1107ea761154a76200 -ResultsRoot .antiphon/debug-9d3dc152 -SlotWaitMinutes 5
```

The independent no-build test invocation was justified by the inventory limit above, not by a failed assertion:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-9d3-guard-direct -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=/work/worktrees/task-9d3dc152/tests/Antiphon.Tests/bin-debug9d3/ --property:ArtifactsPath=/work/worktrees/task-9d3dc152/tests/Antiphon.Tests/bin-debug9d3/artifacts --property:UseArtifactsOutput=true --property:UseAppHost=false -- --treenode-filter '/*/*/WindowsDeliveryFixtureWitnessTests/*' --report-trx --report-trx-filename guard.trx --results-directory /work/worktrees/task-9d3dc152/.antiphon/debug-9d3dc152/direct
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-9d3-mechanisms -SlotWaitMinutes 5 -- pwsh -NoProfile -File .antiphon/debug-9d3dc152/mechanism-witness.ps1 -OutputDirectory tests/Antiphon.Tests/bin-debug9d3/
```

For a later reproduction use a fresh owned output path and the actual source SHA, retain the gate, and rebuild before using `--no-build`. The following is the final witness script used in the passing run:

```powershell
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$bin = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($name in @('Antiphon.Agents.Pty','Antiphon.SessionRunner.Contracts','Antiphon.Server')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $bin ($name + '.dll')))
}
$script:checks = 0
function Assert-Witness([bool]$condition, [string]$label) {
    if (-not $condition) { throw "WITNESS FAIL: $label" }
    $script:checks++
    Write-Host "WITNESS PASS: $label"
}
function New-Intent {
    [Antiphon.Server.Application.Services.CardSiblingInspectionIntent]::new(
        [Guid]::NewGuid(), 1, [Guid]::NewGuid(), '/fixture', $null,
        [Antiphon.Server.Domain.Enums.RequestedWorktreeBaseMode]::Target,
        $null, $null, $null, 'master')
}
# Force the slow-worker order: no one completes while the first tick enqueues.
$results = [Antiphon.Server.Application.Services.SiblingInspectionResults]::new([TimeProvider]::System)
$entries = [Collections.Generic.List[object]]::new()
for ($i=0; $i -lt 32; $i++) {
    $started = $false
    $entries.Add($results.Begin((New-Intent), [ref]$started))
}
$last = New-Intent
$started = $false
$rejected = $results.Begin($last, [ref]$started)
Assert-Witness ($null -eq $rejected -and $results.InFlightCount -eq 32) 'slow first tick refuses #33 at 32 in flight'
foreach ($entry in $entries) {
    $results.Complete($entry, [Antiphon.Server.Application.Services.SiblingBaseGuard]::Proceed)
}
Assert-Witness ($results.Count -eq 32 -and $results.InFlightCount -eq 0) '32 completed results use zero in-flight capacity'
$started = $false
$entry33 = $results.Begin($last, [ref]$started)
foreach ($entry in $entries) { $results.Invalidate($entry.Intent.TaskId) }
Assert-Witness ($started -and $results.Count -eq 1 -and $results.InFlightCount -eq 1) 'next tick admits #33 then consumes 32; Outstanding=1 matches Windows failure'
$results.Complete($entry33, [Antiphon.Server.Application.Services.SiblingBaseGuard]::Proceed)
$results.Clear()
# Force a fast-worker order, admitting/completing all 33 in the first tick.
for ($i=0; $i -lt 33; $i++) {
    $started = $false
    $entry = $results.Begin((New-Intent), [ref]$started)
    $results.Complete($entry, [Antiphon.Server.Application.Services.SiblingBaseGuard]::Proceed)
    $entries.Add($entry)
}
foreach ($entry in $entries) { $results.Invalidate($entry.Intent.TaskId) }
Assert-Witness ($results.Count -eq 0) 'fast first tick admits all 33; next tick leaves Outstanding=0'
$results.Clear()

$generation = [Antiphon.SessionRunner.Contracts.SessionGeneration]::Normalize([DateTime]::UtcNow)
$id = [Guid]::NewGuid()
$epoch = [Guid]::NewGuid()
$refusal = [Antiphon.Server.Application.Services.ProviderDialogObservationService].GetMethod(
    'Refusal', [Reflection.BindingFlags]'NonPublic,Static')
$clear = [Antiphon.SessionRunner.Contracts.ProviderDialogObservation]::new($id,$epoch,$generation,1,0,
    [Antiphon.SessionRunner.Contracts.ProviderDialogState]::Clear,$null,$null,$true,$null)
Assert-Witness ($null -eq $refusal.Invoke($null, @($clear,$id,$generation))) 'same accepted launch and database generation permits Clear'
Assert-Witness ($refusal.Invoke($null, @($clear,$id,$generation.AddMilliseconds(1))) -eq 'observation-unknown') 'receipt-world separately sampled database generation refuses Clear'
$unknownHistory = [Antiphon.SessionRunner.Contracts.ProviderDialogObservation]::new($id,$epoch,$generation,1,0,
    [Antiphon.SessionRunner.Contracts.ProviderDialogState]::Clear,$null,$null,$false,$null)
Assert-Witness ($refusal.Invoke($null, @($unknownHistory,$id,$generation)) -eq 'observation-unknown') 'Herdr unknown input history refuses even an otherwise Clear observation'

# Force CRLF/CR inputs, and split the ANSI echo at every character through the real renderer.
foreach ($ending in @("`r`n", "`r")) {
    $normalized = "HEAD work`n`nDescription:`nTAIL spec"
    $body = $normalized.Replace("`n", $ending) + $ending
    $script:screen = [Antiphon.Agents.Pty.TerminalScreen]::new(120,30)
    $script:screen.Feed("Fake Claude ready`r`n")
    $script:writes = [Collections.Generic.List[string]]::new()
    $script:reads = 0
    $snapshot = [Func[Threading.CancellationToken,Threading.Tasks.Task[string]]]{ param($ct)
        if ($script:writes.Count -gt 0) {
            $script:reads++
            $echo = if ($script:reads -eq 1) { "`e[2J`e[H`e[32mHEAD work" } else { "`r`n`r`nDescription:`r`nTAIL spec`e[0m" }
            foreach ($c in $echo.ToCharArray()) { $script:screen.Feed([string]$c) }
        }
        return [Threading.Tasks.Task]::FromResult([string]$script:screen.GetScreenText())
    }
    $write = [Func[string,Threading.CancellationToken,Threading.Tasks.Task]]{ param($text,$ct)
        if ($text -eq "`r" -and $script:reads -ne 2) { throw 'Enter preceded tail evidence' }
        $script:writes.Add($text)
        return [Threading.Tasks.Task]::CompletedTask
    }
    $options = [Antiphon.Agents.Pty.SendLineGateOptions]::new([TimeSpan]::Zero,[TimeSpan]::FromSeconds(1),[TimeSpan]::Zero)
    $outcome = [Antiphon.Agents.Pty.EchoGatedLineSender]::SendAsync($body,$snapshot,$write,$options,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    Assert-Witness ($outcome.ToString() -eq 'EvidenceSeen' -and $script:writes.Count -eq 2 -and
        $script:writes[0] -ceq ("`e[200~" + $normalized + "`e[201~") -and $script:writes[1] -ceq "`r") "line-ending length=$($ending.Length): LF body in one paste, separate Enter only after split ANSI tail"
}
$positive = [Antiphon.Agents.Pty.SubmitEvidence]::IsEmptiedComposer('> queued hello', "SUBMITTED:queued hello`nFAKE response to: queued hello`n> ", 'queued hello')
Assert-Witness (-not $positive) 'Grok append-only submitted echo fails emptied-composer screen fallback; this alone does not explain a missing transcript'
Write-Host "WITNESS SUMMARY checks=$script:checks failed=0 native-conpty=false"
```

--- next stage ---
next: code
handoff: Repair receipt-world generation binding, the omitted Claude/Herdr refusal case and deterministic inspection fairness; retain existing native-fixture repairs. Native tasks 32365cdf/fb80be16/595bc226 own closure. Preserve diagnostics for unresolved Grok confirmation, remote disposal and poison-page execution; do not infer a product fix before that evidence. Track the independent checkpoint output-inventory size limit separately.
artifact: docs/investigations/2026-10-27-windows-terminal-receipt-static-analysis.md
