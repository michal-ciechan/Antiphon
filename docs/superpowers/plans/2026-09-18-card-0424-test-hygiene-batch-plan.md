# CARD-0424: test-hygiene batch (delegate.ps1 collision, two stale assertions, provisioner isolation, GitDiffSpike limiter, roster drift, two suspected isolation instances)

Plan task `caafb4bb`, 2026-09-18, inspected checkout `828289f8` on `feat/card-task-caafb4bb`
(= `origin/master` `de6eef47` plus the CARD-0486 triage docs; every code site below is byte-identical
to `de6eef47`). Read-only against code; no fix was built. Verification is designed here because every
fix is one to three lines of test code or a mechanical fixture conversion, so Build can execute it
without a separate TestDesign stage (same shape as the CARD-0560 plan).

Sources: the card as rewritten today; the triage
[docs/investigations/2026-09-18-card-0486-pre-existing-red-tests-triage.md](../../investigations/2026-09-18-card-0486-pre-existing-red-tests-triage.md);
its TRX files under `C:\Antiphon\worktrees\card-task-a55b7ed6\.antiphon\inv0486-*` (failure texts
quoted below were read from `batch1.trx`, `sr.trx`, `check.trx`); `git log -S` for the dates.

Owners: [testing](../../testing-and-build.md) (shared-Postgres rule, `ProcessSpawnLimit` lane, Fast
lane), [conventions](../../project-context.md).

## Disposition in five lines

1. **Six mechanical fixes, six files, no production code.** Items 1-6 each touch one test file (item
   5 touches two: the class and the `Antiphon.Tests` floor list). Every diagnosis on the card is
   confirmed by the code and by the triage TRX text; nothing needs re-investigating.
2. **Item 6 cannot be done literally.** "Derive the expected set via reflection over classes carrying
   the attribute" makes both sides of the equality the same reflected set, which asserts nothing.
   The non-tautological form that also cannot drift is the one the other two assemblies already use:
   a reflected population plus a hand-listed *floor* that must be a subset of it (D-6).
3. **Item 4 is a per-test isolated database**, not per-class and not `[NotInParallel]`: the
   provisioner's standing-seat fallback adopts *any* Check owner in the database it can see, so the
   only fix that removes the hazard inside the class as well as across classes is an empty database
   per test (D-4).
4. **The two suspected instances get a protocol, not a fix.** Each has a decision rule (D-7, D-8).
   `HerdrSupervisionBackoffTests` gets a conditional slice (isolated database through the existing
   harness) that Build applies only if the quiet combined re-run reproduces the failure.
   `C448_V19` gets no code change from this card under any outcome; a red quiet run files a follow-up.
5. **One expensive run.** The combined `*Check*` run (268 tests, 48 min under a neighbour process)
   is run once, post-fix, on a quiet host, and serves both item 4's acceptance and the `C448_V19`
   question. Everything else is class- or method-scoped and takes seconds to minutes.

## Ground truth

Verified on `828289f8` (code identical to `de6eef47`) on 2026-09-18.

| # | Card assumption | Observed | Consequence |
|---|---|---|---|
| 1 | `scripts/delegate.ps1` `[string]$Finding` (line 281) collides with `foreach ($finding …)` (line 493); the loop object is coerced to a string. | Confirmed. `[string]$Finding` is `ParameterSetName = 'Finding'` (CARD-0272 S2+S3, `c753cb8d7`, 09-04). The loop at 493-496 is in the `WorktreeHealth` set (CARD-0147 S2, `9c815fbf2`, 09-03): the loop predates the parameter by a day. PowerShell variables are case-insensitive and a typed parameter keeps its `[string]` constraint for the whole script scope, so `$finding = <PSObject>` stores the object's `ToString()`, and `$finding.branch` / `.detail` are `$null`. `batch1.trx`: `run.Output should contain "feat/card-task-aabbccdd" but was actually "1 stuck feat/card-task-* finding(s) (detection only; nothing pruned): [Error] task - "`. A sweep of all 57 typed parameters against every `foreach (` and `$x =` in the body finds exactly one non-string collision (this one); `$Dir` (line 104) is reassigned at 502/861 but string-to-string, harmless. | Rename the loop variable (S1). No other site needs the same fix. |
| 2 | `ComplexityChainRoleHttpTests` line 62 asserts `roles[0] == "Plan"`; `RoutableRoles` now starts with `Investigate`. | Confirmed. `tests/Antiphon.Tests/Application/ComplexityChainRoleHttpTests.cs:62` is `roles[0].ShouldBe("Plan")`. `server/Application/Services/ComplexityRoutingService.cs:61-64` lists `Investigate, Plan, TestDesign, …` since `dca5b06ee` (CARD-0146 S1, 09-03). The endpoint orders `roles[]` by `RoutableRoles` index (line 407-409). `batch1.trx`: `roles[0] should be "Plan" but was "Investigate"`. The two following assertions (`Contains("Custom")`, `NotContain("Check")`) are unaffected. | One literal (S2). |
| 3 | `CardCorrectionIntegrationTests.An_edit_snapshots_…_UrgentSince` has a timestamp precision mismatch. | Confirmed. Line 519 `var due = DateTime.UtcNow.AddDays(60)` (100 ns ticks); `CardService.CreateAsync` ends with `return await GetByIdAsync(card.Id, ct)` (re-read from Postgres, `timestamptz` = microseconds), so line 527 `card.DueAt.ShouldBe(due)` fails whenever the sub-microsecond ticks are non-zero: `should be 2026-11-17T13:24:52.1548248Z but was …8240Z`. Line 550 (`revision.DueAt.ShouldBe(due)`) has the same defect, never reached. The same file already solves it for `CompletedAt` at lines 1573-1574 (`new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc)` under a comment naming the microsecond rule); `MutationPipelineTests` and `AgentTaskPipelineStatusTests` carry private `Truncate` helpers for the same reason. | Truncate `due` at construction with an in-class helper and reuse it at 1573 (S3). |
| 4 | `CheckInterpreterProvisionerTests` still uses the shared `TestDbFixture`; 4 cases red in the combined `*Check*` run; siblings were converted. | Confirmed. `CreateContext()` at line 287 is `new(TestDbFixture.CreateDbContextOptions())` (shared `antiphon_test`); the class doc comment says so. `StandingSpecialistProvisioner.EnsureAsync` looks up by slug and, when absent and `spec.OwnsStandingSeat` (true for the Check spec), falls back to `FirstOrDefault(a => a.StandingSpecialistRole == Check && a.StandingSpecialistOwnerId == a.Id)`: any Check owner in the visible database is adopted, reconciled, and its workspace provisioned instead of this test's scratch directory. `check.trx` (268 tests): exactly `the_deny_all_tool_hook_is_written_into_its_scratch_directory`, `a_second_call_changes_nothing`, `a_workspace_that_was_cleaned_up_is_healed_on_the_next_call`, `the_specialists_working_directory_is_seeded_as_trusted_in_claude_json` red; 12/12 green in `batch1.trx`. `AgentTaskCheckInterpreterTests` and `AgentTaskCheckSweepTests` build a `Harness` on `TestDbFixture.CreateIsolatedSchemaAsync()` (one clone per test) and pass its `ConnectionString` to `CreateDbContextOptions(string)`. Other `*Check*` classes still on the shared store: `AgentTaskCheckScheduleTests`, `DelegateCheckProbeTests`. | Per-test clone (S4, D-4). The fallback is production design (CARD-0352 D2) and is not touched. |
| 5 | `GitDiffSpikeTests` lacks `[ParallelLimiter<ProcessSpawnLimit>]`. | Confirmed. `tests/Antiphon.Tests/Infrastructure/GitDiffSpikeTests.cs:19-20` carries only `[Category("Integration")]` `[Category("Slow")]`; git runs through the production `GitService`. It is on `tests/Antiphon.Tests/slow-tests-allowlist.txt:13`. 3.99 s solo on 09-18 against a 5 s bound. `Antiphon.Tests/ProcessSpawnLimitTests.Process_spawning_classes_carry_the_limiter` asserts one direction only (each listed type carries the attribute), so adding the attribute cannot break it; `LandingGitTests` in the same namespace shows the shape (`using Antiphon.Tests.TestHelpers;` + attribute). | Add the attribute and list the class in the `Antiphon.Tests` floor (S5). |
| 6 | `SessionRunner.Tests/ProcessSpawnLimitTests.Process_spawning_classes_are_exactly_the_limiter_population` hard-codes a roster; 11 classes missing today. | Confirmed. The test has a 22-entry `Type[] expected` and asserts `actual.SetEquals(expected)` where `actual` is the reflected attribute population. `sr.trx`: `unexpected limiter types: CodexCommandLengthHttpAcceptanceTests, HerdrLabelFollowLiveTests, HerdrLabelFollowSchedulingTests, HerdrLabelSnapshotTests, HerdrPaneDisposalGuardedLiveTests, HerdrPaneDisposalServiceTests, HerdrPaneDisposalStopRegressionTests, RemoteControlConditionalInputTests, RunnerCustodyCrashTests, RunnerCustodyTests, RunnerSessionGenerationTests; missing:` (none). The `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` copies of this test assert only `expected ⊆ actual` and have never drifted red. A source-text spawn signal (`Process.Start(` / `ProcessStartInfo` in the class's own file) covers 12 of the 33 limiter classes; the rest spawn through helpers (`LocalHttpRunner`, `RestartFixture`). | One-directional floor, refreshed (S6, D-6). |
| 7 | `HerdrSupervisionBackoffTests.A_launch_still_owned_by_the_queue_…` is a shared-state isolation instance (green alone, red in the 233-test batch). | Partly. The class is `[NotInParallel]` with no key, so it runs in the serial phase after every parallel test; the harness (`AgentSupervisionTests.BuildHarness`, line 595) hard-codes `TestDbFixture.ConnectionString` (shared); the supervisor tick is a global sweep; launch-queue ownership is in-memory per harness (`AgentSessionLaunchQueue._owned`); assertions are scoped to the fixture's own agent; `CleanupAsync` deletes the fixture's rows by temp root. `batch1.trx`: `HerdrConsecutiveFailures should be 1 but was 0`, duration 1.1 s (not a timeout). The triage's green re-run was method-scoped, so an in-class order effect is not excluded either. | Protocol (S7a) with a conditional isolated-database conversion (S7b), D-7. |
| 8 | `AgentTaskLandCheckpointMatrixTests.C448_V19…` 8/8 red in the combined `*Check*` run; a neighbour `Antiphon.Tests.exe` ran land tests the whole time; may be contention. | Confirmed as unattributable. Each case builds a `LandingSafetyHarness` with its own `CreateIsolatedSchemaAsync()` clone, a fresh `Path.GetTempPath()/antiphon-c448-<guid>` root and a fresh `TaskId`; the class carries the limiter and `Slow`. No shared database or path exists for another test to leave state in. `check.trx` shows two failure kinds: C16/C17 × {source, destination} `h.RunAsync() should throw … InterruptedBoundary but did not` (the boundary fires only on a *succeeded* `worktree remove` / `update-ref -d`; `GuardedWorktreeRemoval` turns a `TimeoutException` on the remove into a `worktree_remove_timeout` outcome, so a slow git under load skips the boundary), and C14/C15 × {source, destination} `LandingRefusal: pending_operation_coordinates_changed` thrown from the second `RunAsync` (line 227) instead of being returned in the result. Durations 56 s to 4 m 29 s. | Protocol only (S8), D-8. No keying: `[NotInParallel]` cannot fix a class that already has private state and the limiter; only load can explain it, and load is what the quiet run measures. |
| – | The original item 8 (lane-xor guard) is CARD-0560, do not duplicate. | `1d19a7fd test(CARD-0560): tag HerdrPaneDisposalEndpointTests Integration` is in this branch's history. `batch1.trx` had it as the 5th failure. | The combined re-run (V-1) should show 4 pre-fix failures now, not 5. |

## Decisions

### D-1. Rename the `WorktreeHealth` loop variable to `$row`

`scripts/delegate.ps1:493-496`: `foreach ($row in $report.findings)` and the three `$row.` reads.
Reason: the parameter is public CLI surface (`-Finding <taskId>`, documented on CARD-0272 and in
`docs/orchestration-loop.md`); the loop variable is private. Rejected: renaming the parameter (breaks
callers), `[object]`-casting the loop variable (still assigns to the constrained variable), a script-wide
`Remove-Variable Finding` (fragile, and the collision is one site).

### D-2. Assert the literal `"Investigate"`

`ComplexityChainRoleHttpTests.cs:62` becomes `roles[0].ShouldBe("Investigate")` with a one-line comment
naming `ComplexityRoutingService.RoutableRoles` as the order source. Reason: this is the CARD-0332 HTTP
contract test for `roles[]` order, and the card asks for the assertion to be updated. Rejected: pinning
to `RoutableRoles[0].ToString()`, which stops the HTTP test from noticing a future reorder (the one
thing it is for); asserting the whole prefix (over-design for a hygiene card).

### D-3. Truncate `due` to microseconds with an in-class helper

Add `private static DateTime AtDbPrecision(DateTime utc) => new(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);`
to `CardCorrectionIntegrationTests`, use it at line 519 (`var due = AtDbPrecision(DateTime.UtcNow.AddDays(60));`)
and replace the inline expression at 1573-1574 with a call. Reason: the file's own precedent is
truncation, and the assertion then keeps testing an exact round trip. Rejected: a tolerance
(`ShouldBe(due, TimeSpan.FromMicroseconds(1))`) hides which side rounds; a shared `TestHelpers`
helper would mean touching four files' private helpers for a one-file bug.

### D-4. `CheckInterpreterProvisionerTests`: one cloned database per test

Extend the private `TempWorkspace` into an `IAsyncDisposable` that also owns an `IsolatedTestSchema`
(`await using var scratch = await TempWorkspace.CreateAsync();` /
`CreateAsync(writeClaudeConfig: false)`), expose `scratch.Schema.ConnectionString`, and make
`EnsureAsync`, `ReloadAsync`, and `CreateContext` take the scratch (or its connection string) so every
context is `new AppDbContext(TestDbFixture.CreateDbContextOptions(scratch.Schema.ConnectionString))`.
Nine tests touch the database and change their first line; the three pure tests are untouched. Update
the class doc comment ("Every test uses its own slug and its own scratch directory: the fixture
database is shared…") and the `SettingsFor` comment to say the database is now per test; keep the
unique slug (harmless, and still documents the unique index). No `[NotInParallel]`.

Reason: the standing-seat fallback adopts any Check owner it can see. A per-class clone would still let
the twelve tests adopt each other's owner when they interleave (they pass today by timing, not by
design); `[NotInParallel]` would serialise them but not hide other classes' leftover owners. Rejected:
per-class clone, `[NotInParallel]`, changing the production fallback (CARD-0352 D2 design; not a test
hygiene change), a `[Before(Test)]`/`[After(Test)]` pair with fields (TUnit instantiates one class
instance per test, so it would work, but the `await using` local matches the sibling harnesses and
keeps disposal visible at the call site).

### D-5. `GitDiffSpikeTests`: add the limiter and list it in the floor

`[ParallelLimiter<ProcessSpawnLimit>]` on the class with `using Antiphon.Tests.TestHelpers;`; add
`typeof(Antiphon.Tests.Infrastructure.GitDiffSpikeTests)` to the `types` list in
`tests/Antiphon.Tests/ProcessSpawnLimitTests.cs` (the file already qualifies `Infrastructure` types that
way). No allowlist change (already listed, still Slow). Reason: `docs/testing-and-build.md` line 10,
"a new class that starts a child must take the same attribute".

### D-6. `SessionRunner.Tests` roster: reflected population, hand-listed floor, one direction

Rewrite `Process_spawning_classes_are_exactly_the_limiter_population` as
`Process_spawning_classes_carry_the_limiter` (the sibling assemblies' name):

- `actual` stays the reflected set of classes in the assembly carrying
  `ParallelLimiterAttribute<ProcessSpawnLimit>`.
- `expected` stays a `Type[]`, refreshed with the 11 classes from `sr.trx` (33 entries), and is
  asserted as a floor: `actual.IsSupersetOf(expected).ShouldBeTrue("missing: " + …)`. The per-type
  `ShouldNotBeNull` loop becomes redundant and goes.
- Add `actual.ShouldNotBeEmpty()` and keep `Caps_concurrent_process_spawning_tests_at_one`.
- Drop the "unexpected limiter types" half. A new limiter class never turns this test red again;
  removing the attribute from a known spawner still does.

Reason: the card's literal instruction (reflect both sides) is `X.SetEquals(X)`. The only independent
signal for "this class spawns" is either a hand list or a source scan, and the source scan covers 12
of 33 today (helper-mediated spawns escape it, and `Process.GetCurrentProcess()` would false-positive),
so it would not have caught any of the three drifts on record (CARD-0486 item 1 twice, item 5 here).
The floor is what the other two assemblies have run since CARD-0050 S5 without a red. Rejected: literal
both-sides reflection (tautology); source-text census (weak signal, new false-positive maintenance);
deleting the test (loses the "attribute removed" guard); keeping `SetEquals` and adding the 11 (the
card's explicit "cannot recur" requirement).

Update the doc line in `docs/testing-and-build.md` (the `ParallelLimiter` sentence at line 10) with
one clause: "the three `ProcessSpawnLimitTests` roster tests are floors, not censuses; a new limiter
class need not be listed, a known spawner must stay listed". No other doc change.

### D-7. `HerdrSupervisionBackoffTests`: reproduce first, isolate only if reproduced

Protocol in S7a; decision rule in the verification table (V-7). If the quiet combined batch reproduces
the failure while the class is green solo, Build applies S7b: `AgentSupervisionTests.BuildHarness`
gains an optional `string? connectionString = null` (defaulting to `TestDbFixture.ConnectionString`, so
its other four callers are unchanged), `Fixture.CreateAsync` creates an `IsolatedTestSchema` and passes
its connection string, `Db()` becomes an instance method on `Fixture` (`f.Db()`, about 20 call sites in
the file), and `DisposeAsync` drops the clone after `Harness.DisposeAsync()`. `[NotInParallel]` stays
(the class drives real clock/queue timing). Reason for isolation over hunting the leaking class: it
ends the family for this class regardless of which future class leaves rows, and it is the same shape as
S4. Reason for not doing it unconditionally: the card says not to fix a contention artifact, and the
conversion is the largest edit on this card.

### D-8. `C448_V19`: quiet combined run decides; no code change from this card

Protocol in S8; rule in V-8. Green on a quiet host: contention confirmed, record and close. Red on a
quiet host with the class green solo: in-process load sensitivity (git timeouts under ~250 concurrent
tests), which `[NotInParallel]` keying does not address and global `[NotInParallel]` would address only
by moving a 30-minute class into the serial phase; that is a separate decision, so file a follow-up
card with the TRX and the `LandingEvidence` jsonl for the red cases. Red solo: a real defect, also a
follow-up card (out of this card's hygiene scope).

### D-9. Verification is folded; next stage is Code

Same as CARD-0560: the fixes are small enough that the verification table is the test design.

## Slices

Each slice is one commit. Commit and push after each; commit before starting V-6.

### S1. `delegate.ps1` loop variable

- `scripts/delegate.ps1:493-496`: `$finding` → `$row` (four occurrences). Nothing else.

### S2. `ComplexityChainRoleHttpTests` assertion

- `tests/Antiphon.Tests/Application/ComplexityChainRoleHttpTests.cs:62`: `"Plan"` → `"Investigate"`,
  plus a comment `// first of ComplexityRoutingService.RoutableRoles (CARD-0146 S1)`.

### S3. `CardCorrectionIntegrationTests` precision

- `tests/Antiphon.Tests/Application/CardCorrectionIntegrationTests.cs`: add `AtDbPrecision`, use at 519,
  replace 1573-1574 with `var completedAt = AtDbPrecision(DateTime.UtcNow.AddDays(-40));` (keep the
  comment).

### S4. `CheckInterpreterProvisionerTests` isolated database

- `tests/Antiphon.Tests/Application/CheckInterpreterProvisionerTests.cs`: `TempWorkspace` becomes
  async-created and async-disposed with an `IsolatedTestSchema`; helpers take the scratch; nine tests
  change their first line to `await using var scratch = await TempWorkspace.CreateAsync(…)`; doc
  comments updated per D-4. Pattern reference: `AgentTaskCheckSweepTests.Harness` (lines 887-961).

### S5. `GitDiffSpikeTests` limiter

- `tests/Antiphon.Tests/Infrastructure/GitDiffSpikeTests.cs`: add `using Antiphon.Tests.TestHelpers;`
  and `[ParallelLimiter<ProcessSpawnLimit>]` above the class.
- `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs`: add
  `typeof(Antiphon.Tests.Infrastructure.GitDiffSpikeTests),` to `types`.

### S6. `SessionRunner.Tests` roster

- `tests/Antiphon.SessionRunner.Tests/ProcessSpawnLimitTests.cs`: rewrite per D-6 (rename, 33-entry
  floor, `IsSupersetOf`, `ShouldNotBeEmpty`; update the class summary to say "floor").
- `docs/testing-and-build.md` line 10: the one clause from D-6.

### S7a. `HerdrSupervisionBackoffTests` protocol (no code)

Run V-7 and record the verdict in the Build report. Host must be quiet (see Verification design).

### S7b. Conditional: isolated database for `HerdrSupervisionBackoffTests`

Only on a V-7 "reproduced" verdict. Files: `tests/Antiphon.Tests/Application/AgentSupervisionTests.cs`
(`BuildHarness` optional parameter, default preserves behaviour for the other callers) and
`tests/Antiphon.Tests/Application/HerdrSupervisionBackoffTests.cs` (fixture owns the clone; `f.Db()`).
Re-run V-7's class filter and the combined batch once more after the conversion.

### S8. `C448_V19` protocol (no code)

Run V-6 on a quiet host; only if it is red for `C448_V19`, run V-8. Record the verdict; file the
follow-up card described in D-8 if the verdict is not "contention".

## Verification design

Build into isolated outputs (forward slash) and run with `--no-build`. Fresh, empty results directory
per invocation. TUnit 1.44 OR syntax is class-level only, so combined runs are class lists.

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c424/ --nologo
dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c424/ --nologo
# quiet-host check, before AND after every run that carries a V-6/V-7/V-8 verdict:
Get-CimInstance Win32_Process -Filter "Name LIKE '%Tests.exe' OR Name='dotnet.exe' OR Name='testhost.exe'" | Select-Object ProcessId, Name, CommandLine
```

A foreign `Antiphon.*Tests.exe` (or a `dotnet … tests/` command line not owned by this task) present
at either check voids that run's V-6/V-7/V-8 verdict; the run's own red/green for items 1-6 stands.
The 48-minute run must be started in the background *within the turn* with `--report-trx` and a log,
polled at spaced intervals, and awaited to completion before the turn ends; do not edit source under
it.

| Id | What | Filter (`--treenode-filter`) | Pre-fix (recorded) | Post-fix expected |
|---|---|---|---|---|
| V-1 | Items 1-3 red→green in the triage's combined batch (`Antiphon.Tests`) | `/*/*/(DelegateScriptKindTests*)\|(ComplexityChainRoleHttpTests*)\|(UnmarkedWaitingContractTests*)\|(AgentBundleAttachmentTests*)\|(StageOutcomeFindingEndpointTests*)\|(TestLaneCategoryGuardTests*)\|(CardCorrectionIntegrationTests*)\|(HerdrSupervisionBackoffTests*)\|(DelegateLaunchArgvIntegrityTests*)\|(InstructionBundleTests*)\|(DelegationHarnessCensusTests*)\|(CheckInterpreterProvisionerTests*)/*` (233 tests, ~4 min) | `batch1.trx`: 5 failed (`WorktreeHealth_…`, `every_test_class_…` [now CARD-0560], `An_edit_snapshots_…`, `Put_get_delete_…`, `A_launch_still_owned_…`) | 0 failed for items 1-3; `every_test_class_…` green (CARD-0560); `A_launch_still_owned_…` is V-7's combined leg, read separately |
| V-2 | Item 1 method-scoped | `/*/*/DelegateScriptKindTests/WorktreeHealth_posts_and_prints_findings_without_pruning` | red (text in ground truth #1) | 1/1 pass; output contains `feat/card-task-aabbccdd` |
| V-3 | Item 3 method-scoped, 3 runs | `/*/*/CardCorrectionIntegrationTests/An_edit_snapshots_importance_urgency_and_due_and_maintains_UrgentSince` | red ~90% of runs (any non-zero sub-µs ticks) | 3/3 pass |
| V-4 | Item 5: class + floor | `/*/*/GitDiffSpikeTests/*` and `/*/*/ProcessSpawnLimitTests/*` (`Antiphon.Tests`) | 3/3 + 3/3 pass (attribute absent, floor one-directional) | 3/3 + 3/3 pass; `class.trx` shows `GitDiffSpikeTests` ran (limiter changes scheduling, not outcome); duration tripwire on its TRX reports no new ≥5 s test |
| V-5 | Item 6: roster test (`Antiphon.SessionRunner.Tests`) + one positive control | `/*/*/ProcessSpawnLimitTests/*` | `sr.trx`: 1 failed (11 unexpected) | 3/3 pass. PC: remove `[ParallelLimiter<ProcessSpawnLimit>]` from `SessionLivenessTests`, run `/*/*/ProcessSpawnLimitTests/Process_spawning_classes_carry_the_limiter` → fails naming `SessionLivenessTests` under `missing:`; restore; rerun green. Record both TRX. |
| V-6 | Item 4 acceptance and the `C448_V19` combined leg, quiet host | `/*/*/*Check*/*` (268 tests; 48 min under a neighbour, expect less quiet) | `check.trx`: 12 failed (4 provisioner + 8 `C448_V19`) | 0 provisioner failures (item 4 acceptance, valid even if the host was not quiet). `C448_V19`: see V-8 rule. |
| V-7 | Suspected instance A: solo vs combined | Solo: `/*/*/HerdrSupervisionBackoffTests/*` (whole class, twice). Combined: V-1's filter (twice, quiet host; the first V-1 run counts as one). | class green solo (method-scoped, once); red once in the batch | **Rule.** Solo red ≥1/2 → defect in the class, not isolation; report, no S7b, follow-up card. Solo green 2/2 and combined red ≥1/2 → reproduced: apply S7b, then class ×1 and combined ×1 must be green. Solo green 2/2 and combined green 2/2 → not reproduced: record as "timing under load, not reproduced on a quiet host", no S7b. |
| V-8 | Suspected instance B: `C448_V19` solo, only if V-6 has any `C448_V19` red | `/*/*/AgentTaskLandCheckpointMatrixTests/C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates` (18 cases, ~20-30 min, quiet host) | never run solo | **Rule.** V-6 green for all 18 on a quiet host → contention confirmed; done, no V-8. V-6 red and V-8 green → in-process load sensitivity; no keying (D-8); follow-up card with `check.trx`, the failing cases' `LandingEvidence` jsonl, and the quiet-host evidence. V-8 red → real defect; follow-up card, out of scope here. |
| V-9 | Nothing else moved: Unit lane once from the isolated build | `/*/*/*/*[Category=Unit]` (`Antiphon.Tests`, ~70 s) | 3 lane-xor guard failures pre-CARD-0560, now green | 0 failures attributable to this card; any other red is named and checked at the base commit by targeted rerun |

Order: S1-S6 (commit each) → V-2, V-3, V-4, V-5, V-9 (minutes) → V-1 (first combined run; doubles as
V-7 combined leg 1) → V-7 solo ×2 and combined leg 2 → S7b if the rule says so → V-6 (background,
awaited) → V-8 only if V-6 says so. Commit before V-6.

Any red outside the table is pre-existing by construction (no production code changes on this card) and
is reported with its base-commit targeted rerun, not repaired here.

Cleanup: delete every `bin-c424` directory the two builds dropped (about a dozen) before finishing.

## Out of scope, noted

- `AgentTaskCheckScheduleTests` and `DelegateCheckProbeTests` are still on the shared store; nothing on
  the card is red because of them. Not converted here.
- `StandingSpecialistProvisioner`'s standing-seat fallback is by design; the test isolation removes the
  test's exposure to it, not the behaviour.
- The CARD-0486 leftovers the triage lists (five worktrees, one remote branch) are housekeeping for the
  orchestrator, not this card.
