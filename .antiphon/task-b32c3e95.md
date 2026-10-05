# CARD-0667 S1a Code evidence

S1a is implemented, pushed and CP-7 is green: six executed, six passed, zero failed/skipped. Automatic release remains dormant. Ready for ordinary Review of this slice; S1b requires its landed predecessor.

Original Code task / landing owner: b32c3e95-2a9e-498e-a044-89d345a4d19e.
Branch: feat/card-task-b32c3e95.
Worktree: /work/worktrees/task-b32c3e95. Desktop counterpart (not accessed): C:\Antiphon\worktrees\card-task-b32c3e95.
Task base / landed plan SHA: a60096be985bb43b3f9a611fd96dc7d10b2ddeda.
Plan: docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md.
Actual green tested SHA / expected source SHA: ccbfa6c6a7ddcfee89512afb7880f13f3626e93c.
The subsequent evidence-only commit changes this report; it does not add another tested-source claim.

## Implementation

Seven admitted source/test files:

- src/Antiphon.SessionRunner.Contracts/TerminalSeatRelease.cs: additive read-only evidence/status DTOs, opaque binding/content digests and file-order revisions; no transcript text or path in the result.
- src/Antiphon.SessionRunner/TerminalSeatReleaseObservation.cs: serialized, bounded streaming read through private real normalizers, complete-record validation, native file identity and content recheck, file-order classifier. Budget: 8 MiB file, 1 MiB record, 100,000 normalized parts; exceeding a bound refuses with Unknown. Linux uses statx device/inode and Windows uses volume plus FILE_ID_INFO; unsupported identity refuses. Grok privately flushes pending chunks as activity.
- src/Antiphon.SessionRunner/ITranscriptTailer.cs: safe-default read-only observation API.
- src/Antiphon.SessionRunner/TranscriptTailer.cs, GrokTranscriptTailer.cs, CodexTranscriptTailer.cs: bind/consumed-file tracking, observation implementation and shared poll/read gate; independent normalizer state and per-instance test I/O/barriers. CARD-0079 compaction observer/authorization is unchanged.
- tests/Antiphon.SessionRunner.Tests/TerminalSeatReleaseTests.cs: the four planned method identities / six argument-expanded results. Real tailers, owned native files and claim registry; polling barriers, no provider/process launch, no real provider home, no DB. Hub subscription and tailers are joined/disposed; environment restored.

Static call-site audit: ObserveTerminalSeatAsync has no production caller beyond its implementations/default declaration. No wire operation, runtime qualification, server hook, option activation, release/kill call or S1b implementation was added.

## Scope and outcomes

The task-specific brief and amended manifest explicitly require CP-7 only and prohibit whole Unit/assembly runs. That conflicts with the boilerplate Final profile's whole Unit language. This report claims the explicit S1a/CP-7 selection only, not whole-card Final qualification. CP-7 selects the entire currently authored TerminalSeatReleaseTests class (four methods / six results).

| ID | Actual outcome |
|---|---|
| V-1, S1a portion | PASS: CP-7 6/6 at the actual tested SHA. Fresh Claude/Grok/Codex activity; Unknown on incomplete evidence; binding/file race refusal; zero publication during inspection, exactly one later ingestion of every expected part. |
| V-1 remainder | Not run; CP-8/9/10/11 prerequisites belong to S1b-S2c, with full final CP-1/5 at S4c. |
| V-2 | Not run; later server slices and final CP-2/6. |
| V-3 | Not run; later discovery/recovery slices and final CP-3/6. |
| R-1 | Not run; deferred final CP-1/5. |
| R-2 | Not run; deferred final CP-2. |
| R-3 | Not run; deferred final CP-4. |
| R-4 | Not run; Windows final CP-5/6. Native Windows file identity is compiled, not executed on this Linux lane. |

Deferred-to-final IDs: full V-1, V-2, V-3, R-1, R-2, R-3, R-4 and CP-1 through CP-6. CP-8 through CP-18 remain with their owning future slices. No deferred row is counted as passed. Later delivery/landing/lease/persistence/manual acceptance was not exercised or claimed by this read-only slice.

Required platform reads: authenticated GET /api/runner-defaults and /api/session-runners succeeded; revision 2, available Linux and Windows catalogue entries. No fleet address/host pin embedded in code or commands.

## Checkpoint history

All five invocations used the unchanged CP-7 filter through the checkpoint tool, exact committed --expected-source-sha, committed/pushed source, and awaited terminal completion. No source edits occurred during a run.

1. 397a6d0eab391d699baebba84ad95a87562602b9: build error CS0313 in the new nullable Shouldly assertion; no tests and no behavioral-red claim.
2. d9359c9594258d2968e525cd2b7f47cbe46bc789: compiling behavioral red, 6 executed / 1 passed / 5 failed. Each provider returned cached Idle for its unread prompt; incomplete/binding tests also detected stale Idle. Duplicate-ingestion method already passed; no manufactured red.
3. 93ca2ac6f65f98e477e5ae1492152afdfe89b605: implementation run, 6 executed / 3 passed / 3 failed. All fresh-provider cases returned Unknown because Linux filesystem creation timestamps changed on append. A task-owned temporary file diagnostic confirmed creationBefore != creationAfter; it was removed immediately.
4. c1a92262d8c6b009c70017b08c5fe25e9bad6aa6: native-identity repair build error CS1503 at the remaining verification-reader argument; no tests.
5. ccbfa6c6a7ddcfee89512afb7880f13f3626e93c: call-site repair, 6 executed / 6 passed / 0 failed / 0 skipped. No assertions/timeouts loosened, no retries added.

One pre-implementation red-fixture compilation correction and two implementation repair commits were required. No inherited-red claim; all encountered failures were in new task code/tests. Total historical test results: 18 (10 passed, 8 failed), with only the final six certifying green. Zero loaded repetitions, no repeat-proof invocation, no repetition after green.

Unedited tool CHECKPOINT lines:

Run 20261005-082820-9b85

```text
CHECKPOINT CP-7 commit=397a6d0eab391d699baebba84ad95a87562602b9 build=failed filter=/*/*/TerminalSeatReleaseTests*/(Fresh_tail_reads_each_provider*)|(Unknown_or_partial_tail_never_authorizes_release)|(Binding_changes_during_read_refuse_qualification)|(Fresh_observation_does_not_publish_duplicate_entries) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=397a6d0eab391d699baebba84ad95a87562602b9 sourceState=clean buildSource=unknown
```

Run 20261005-083018-e572

```text
CHECKPOINT CP-7 commit=d9359c9594258d2968e525cd2b7f47cbe46bc789 build=ok filter=/*/*/TerminalSeatReleaseTests*/(Fresh_tail_reads_each_provider*)|(Unknown_or_partial_tail_never_authorizes_release)|(Binding_changes_during_read_refuse_qualification)|(Fresh_observation_does_not_publish_duplicate_entries) executed=6 passed=1 failed=5 skipped=0 trx=/work/worktrees/task-b32c3e95/.antiphon/checkpoints/20261005-083018-e572/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=d9359c9594258d2968e525cd2b7f47cbe46bc789 sourceState=clean buildSource=verified
```

Run 20261005-083414-9d48

```text
CHECKPOINT CP-7 commit=93ca2ac6f65f98e477e5ae1492152afdfe89b605 build=ok filter=/*/*/TerminalSeatReleaseTests*/(Fresh_tail_reads_each_provider*)|(Unknown_or_partial_tail_never_authorizes_release)|(Binding_changes_during_read_refuse_qualification)|(Fresh_observation_does_not_publish_duplicate_entries) executed=6 passed=3 failed=3 skipped=0 trx=/work/worktrees/task-b32c3e95/.antiphon/checkpoints/20261005-083414-9d48/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=93ca2ac6f65f98e477e5ae1492152afdfe89b605 sourceState=clean buildSource=verified
```

Run 20261005-083640-6c3c

```text
CHECKPOINT CP-7 commit=c1a92262d8c6b009c70017b08c5fe25e9bad6aa6 build=failed filter=/*/*/TerminalSeatReleaseTests*/(Fresh_tail_reads_each_provider*)|(Unknown_or_partial_tail_never_authorizes_release)|(Binding_changes_during_read_refuse_qualification)|(Fresh_observation_does_not_publish_duplicate_entries) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=c1a92262d8c6b009c70017b08c5fe25e9bad6aa6 sourceState=clean buildSource=unknown
```

Run 20261005-083800-d6aa

```text
CHECKPOINT CP-7 commit=ccbfa6c6a7ddcfee89512afb7880f13f3626e93c build=ok filter=/*/*/TerminalSeatReleaseTests*/(Fresh_tail_reads_each_provider*)|(Unknown_or_partial_tail_never_authorizes_release)|(Binding_changes_during_read_refuse_qualification)|(Fresh_observation_does_not_publish_duplicate_entries) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-b32c3e95/.antiphon/checkpoints/20261005-083800-d6aa/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=ccbfa6c6a7ddcfee89512afb7880f13f3626e93c sourceState=clean buildSource=verified
```

The build-failed rows report slot=skipped for their unstarted test driver; each actual build lease was granted with waited=0s (executor.log). Every executed row reports slot=granted waited=0s. The tool summary prints 18 manifest build definitions; only the selected CP-7 build actually ran per invocation, as shown in each builds/bin-c667-s1a/build.log and executor.log.

Additional build: the plan-declared checkpoint-tool bootstrap only, gated by scripts/build-slot.ps1 -Label c667-tool-bootstrap, OutputPath=bin-c667-tool/, UseAppHost=false. Result: build succeeded, one existing nullable warning in TaskOwnerGuard.cs; BUILD SLOT granted waited=0s, released after 6s. No unlisted test/build selection. Receipt/diff/roster checks and the temporary timestamp diagnostic are read-only/manual checks, not test drivers.

## Green provenance and roster

Evidence root: /work/worktrees/task-b32c3e95/.antiphon/checkpoints/20261005-083800-d6aa/.
Receipt: report.json. TRX: rows/CP-7/run.trx. Build log: builds/bin-c667-s1a/build.log.
Validation command:

    pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261005-083800-d6aa/report.json -ExpectedSourceSha ccbfa6c6a7ddcfee89512afb7880f13f3626e93c -Rows CP-7

Result (exit 0): CHECKPOINT SOURCE VALID source=ccbfa6c6a7ddcfee89512afb7880f13f3626e93c rows=1. dirty=0, sourceState=clean, buildSource=verified. Fresh TRX counters and TestDefinitions/UnitTest/TestMethod plus results inspected for each test-bearing run. Final roster, all Passed:

- Antiphon.SessionRunner.Tests.TerminalSeatReleaseTests.Fresh_tail_reads_each_provider(Claude)
- Antiphon.SessionRunner.Tests.TerminalSeatReleaseTests.Fresh_tail_reads_each_provider(Grok)
- Antiphon.SessionRunner.Tests.TerminalSeatReleaseTests.Fresh_tail_reads_each_provider(Codex)
- Antiphon.SessionRunner.Tests.TerminalSeatReleaseTests.Unknown_or_partial_tail_never_authorizes_release
- Antiphon.SessionRunner.Tests.TerminalSeatReleaseTests.Binding_changes_during_read_refuse_qualification
- Antiphon.SessionRunner.Tests.TerminalSeatReleaseTests.Fresh_observation_does_not_publish_duplicate_entries

Rerun (at a clean committed candidate, after prepared-tool bootstrap if needed):

    dotnet run --no-build --no-restore --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md --rows CP-7 --expected-source-sha <committed-HEAD> --max-wait 50s

The no-build tool launcher holds no outer lease; its build/row drivers acquire their own slots. Await each run and any exit-75 wait. Final green tool cleanup removed the CP outputs; the task-owned bootstrap bin-c667-tool directory is removed before settlement. Generated TRX/JSON/logs remain gitignored, never staged.

Full task evidence-history check uses scripts/check-evidence-diff.ps1 -BaseRef a60096be985bb43b3f9a611fd96dc7d10b2ddeda -HeadRef HEAD. Source-range result through ccbfa6c6a7ddcfee89512afb7880f13f3626e93c: 5 commits, 0 evidence entries, 0 violations; git diff --check passed. The final caller report records the check after committing this Markdown report too.

## Mutation and next owner

All PCs remain pending. S1a controls: PC-21 (Claude), PC-22 (Grok), PC-23 (Codex); PC-24 (Claude/Grok/Codex x empty, missing, unreadable, partial, malformed JSON, invalid UTF-8, byte budget, plus unbound); PC-25 (Claude/Grok/Codex x replace/truncate/grow, plus Claude/Codex claim revocation); PC-84 (all three providers). Ordinary red/green does not discharge these. PC-1 through PC-20, PC-26 through PC-83, PC-85 through PC-90 belong to later slices and also remain pending. Mutation owns deliberate mutants, red/restore/green and missing-control discovery; the plan commissions all 90 after the complete S4c candidate lands.

Restart: none. No server or runner activation required for this dormant slice; caller owns later activation planning.
Next: Review, then caller lands original Code task b32c3e95. Commission S1b/CP-8 only after S1a lands; do not activate automatic release. Full final qualification and post-land SourceLanding Mutation remain required by the plan.
