# Review b51c62ad: CARD-1613 plan, round 2 (closure and byte-exact reasons)

Subject: `docs/superpowers/plans/2026-10-29-card-1613-pinned-linux-skip-reflection-plan.md` at
`b78b6733580fd20382638c9707385d11a98b9ed1` (tip of `origin/feat/card-task-b8f46ceb`, Plan task
`b8f46ceb-99ce-4bad-9634-7f13a55e73e2`). Round-1 review: `origin/feat/card-task-bc6c1a41:.antiphon/task-bc6c1a41.md`.
This was a read-only design review on server2/Linux. No product, test or plan file was changed.

Verdict: round 1's D-2, D-3 and D-4 and all six advisories are closed. D-1 is closed in substance:
reasons are byte-exact, mixed classes get method markers, and all 45 rows are compared. But the
mechanism the revision chose introduces one blocking defect (D-1 below): the static Windows
comparison freezes the whole source text of the 46 inventory files permanently in the Unit lane.
The table imports cleanly and the counts are exact. The rc base is right but only implicit (A-1).

## Round-1 closure

| Round 1 | Status | Where in the plan |
|---|---|---|
| D-1 byte-exact reasons, method markers for mixed classes, all-45 before/after | Rule closed (Decisions D-2, D-10; "Windows outcome and reason baseline" steps 1-5; the R-14 variants). Proof mechanism is static, not native; see new D-1 and A-2. | lines 28, 36, 97-105 |
| D-2 BaseTestAttribute subclasses | Closed (D-11; Reflection step 2; R-12 `CustomBuilderAttribute : DynamicTestBuilderAttribute`; extra PC on R-12) | lines 37, 110, 175, 182 |
| D-3 expected-red CP-1 in the closed list | Closed (D-12; RED-1 in an ignored one-row manifest; the final table is all green) | lines 38, 180, 192 |
| D-4 integer minutes, CP-8 = 23 | Closed (the table imports; CP-8 Min 23 with its breakdown) | lines 194-217 |
| A-1 R-5..R-10 collapse | Closed (D-14: 10 parents, one missing-marker PC) | line 40 |
| A-2 extra adapter | Closed (D-13: dropped; CP-7 = 17) | line 39 |
| A-3 legacy digest procedure | Closed (Pin procedure, legacy digest bullet; S3 documents it) | line 134 |
| A-4 overlap / carrier | Closed (Ordering: carrier 5002b2ef, six-commit order, overlap rescan) | lines 225-236 |
| A-5 OS-witness applicability | Closed (D-15) | line 41 |
| A-6 CP-7 estimate | Closed (4 minutes, provisional) | line 39 |

## Defects

### D-1: D-10's source-digest check permanently freezes 46 mostly portable test files, and gives no rule for new or changed exclusions

- **Where:** D-10 (line 36: "after annotation each suite's existing `Policy_rows_require_pinned_attribute` ... revalidates the retained gate source"); baseline step 4 (line 102: embedded pre-migration blob hashes and normalized syntax digests; "require all other syntax tokens/literal values, directives, participating helper bodies and phase ordering unchanged"; "Missing, extra ... baseline entries fail"; "New tests/changed baseline require an explicit reviewed extension"); Reflection step 5 (line 113).
- **Failure:** the check lives in the permanent Unit-lane witness, which runs in all four suites and in Windows Full. Its digests cover whole source inputs and their participating helpers, not just gate chains. After land, *any* non-annotation edit to one of the 46 inventory files, or to a participating helper, turns `Policy_rows_require_pinned_attribute` red everywhere. Helpers include `C1008HostFixture` (inside `RollingVolumeRecycleScriptTests.cs`), `CheckpointTestBase`/`CheckpointTestScope.cs` and `NativeLinuxGate.cs`. Most tests in those files are portable: RemoteScriptContractTests has 215 `[Test]` methods (40 excluded), OutputCleanupTests 49 (10), CompletedCardWorkspaceS5Tests 27 (1), CheckpointTempRootSweepTests 22 (1), CheckpointConfinementLedgerTests 21 (1) and BuildSlotScriptTests 19 (1). This is concrete today. Between the plan's base `91b45006b` and rc `8edda9996`, 9 of the 46 files changed and none of the changes touched a gate. Examples: RollingVolumeRecycleScriptTests +250 lines, RemoteScriptContractTests +136, a git setting list in CheckpointVerifierBoundsTests, one `File.Copy` in HostJqPrerequisiteScriptTests and RetiredTempContainerScriptTests, and one member in ScriptHarnessLinuxOwnershipTests. Every one of these edits would fail the witness. Every in-flight card on the A-4 overlap list would then break the Unit lane on landing. The plan also gives no update procedure for these digests (S3 documents only the 88-record legacy digest). It defines no rule for a *new* Linux-only exclusion, which has no "pre-migration Windows reason", or for a deliberate reason edit.
- **Why:** the card's ongoing invariant is "a policy row is proved by the pinned marker". The source re-evaluation is, by the plan's own words, "a migration compatibility check". Making it permanent couples unrelated test maintenance to a reviewed digest map and conflicts with the overlap guidance at line 236 ("retain newer bodies/gates").
- **Fix:** scope the source-chain re-evaluation to the migration. Run the pre/post normalized-digest and old-chain equality once at S2, as a migration audit with its own ignored receipt (CP-1/4/5/6 can carry it as a one-shot step). Alternatively, feed it only frozen embedded pre-migration blobs, never live source. Keep in the permanent witness only: for the frozen set of 45-row migrated identities (stable signatures), marker reason == frozen baseline reason. If a live-source check stays, digest only each node's gate chain (the dominating prefix through the first effective skip, plus the bodies of the helpers it calls), never whole files. State the rules for (a) a new marked exclusion (marker required, no baseline entry) and (b) an intentional reason change (baseline edit in the same commit). Document both in docs/release-gates.md beside the legacy-digest procedure. Add an R-14 variant in which an unrelated portable edit inside an inventory file stays green.

## Answers to the brief's checks

**(2) Static Windows comparison.** The plan reasons from a Roslyn-bound, human-reviewed extraction of the first effective skip on the IsWindows=true path. Each marker's `CustomAttributeData` reason must byte-match both the baseline and a re-evaluation of the retained gate. CP-10/CP-11 natively re-run the C1210/C1050 rosters after migration. That run also proves, natively, that TUnit renders an attribute skip as the same `Skipped: <reason>` TRX text as a runtime skip (`WindowsUnitPlatformAdmissionTests.cs:209`). How each gate form in the 45 rows is handled:

- `if (!IsLinux) Skip.Test(literal)`: Checkpoint*, SafeDelete, OutputCleanup, PlanCoverage*, AnchoredPath, CompletedCard S5. Handled.
- `if (IsWindows) throw new [TUnit.Core.Exceptions.]SkipTestException(literal)`, including the fully qualified spelling: UnixPtyArgv, UnixDelegateLaunchArgv, LandGraph, BuildSlot, CompletedCard R3. Handled; the macOS narrowing is recorded under D-1.
- `NativeLinuxGate.Require(const)`: Grok, Jq, HostJq (39), Codex, GitMinimum, RefreshGithub, NightlyProbe, the Docker rows. Handled (helper plus const).
- `C1008HostFixture.RequireNativeLinux()`: RemoteScriptContract (32 direct, plus 8 through the expression-bodied `C1105AuditContract`), Rolling*, RetiredTemp*, C1364. Handled (two-level helper). All 40 RemoteScriptContract names resolve to the CARD-1050 reason.
- `[Before(Test)]` hook: RollingProductionMount and RetiredTempContainerHost (C1008 wrapper) are handled. **ScriptHarnessLinuxOwnershipTests** is `RequirePlatform(OperatingSystem.IsLinux() ? "linux" : "other")` and then `platform != "linux"`. That needs argument-to-parameter constant propagation, which is not among the listed folding rules (see A-2).
- bool helper `Linux()`, which calls `Skip.Test` and then `return false` (CheckpointRecoveryLinux, all 10 tests): handled through Skip.Test's framework identity.
- Fixture constructor `new HelperFixture()` (GithubCredentialHelper, all 6 tests): handled ("constructors").
- Ternary concatenation (HostStats): handled.
- Multi-throw helper with later prerequisites (LinuxCgroupCustody, all 8 tests): handled ("first effective skip").

Can it be wrong without anyone noticing? **Reason:** the baseline and the re-evaluation come from the same extractor, so a folding bug is caught only by the human chain review. For non-roster rows this has no native backstop, and the plan states that limit. **Outcome:** a Windows test that passes today cannot silently become skipped unless the extractor misjudges dominance. No current row wraps a skip in try/catch, a lambda or deferred work, but the rule should say so (A-2). A Windows node that *fails* today can silently become Skipped. The plan models skip sites but not work that runs before the skip or after it:
- `PhoneHomeCompletedCardTransportTests.C1032_TransportQueueRecipientReplay` runs `RequireClient()` (Shouldly reflection asserts) before its skip.
- Every Checkpoint* row runs the base `[Before(Test)] OpenCheckpointScope` before the body skip, and `[After(Test)] CloseCheckpointScopeAsync` after it. The After hook will no longer run under a marker skip.

The baseline records `NotExecuted` for these by construction. That change is in the safe direction (A-2).

**Spot-checks (12 rows, read in source).** All reasons are byte-exact against the plan where the plan names them, and unique and literal where it delegates to extraction:
- SafeDeleteProcessBoundaryTests: `:20` "FIFO-held monitor proof runs on Linux" and `:66` "process-group custody proof runs on Linux". These match plan line 101; the class needs 2 method markers.
- CheckpointFixtureProcessTests: `:21` "FIFO config proof runs on Linux" and `:60` "process-group custody proof runs on Linux". These match; 2 method markers.
- HostStatsProbeLiveTests (session-runner): the fold gives "Linux /proc probe; this lane is Windows, where Windows_probe_reads_live_system_times runs.", which matches. macOS today says "another OS", so this is a further macOS change.
- LinuxCgroupCustodyTests (pty-host): "The linux-cgroup-v1 backend executes on Linux only." Matches; all 8 tests call the helper first, so a class marker is valid.
- UnixPtyArgvTests (agents-pty): all 5 tests (with `[Arguments]`) call `RequireUnix` first, giving "Unix PTY argv contract requires a Unix PTY and node". A class marker is valid.
- CompletedCardWorkspaceRemovalTests (session-runner): `C1032_InspectionR3Mode` gives "Unix executable-bit witness requires Unix filesystem modes" (2 cases), and `C1032_RemovalLinuxIgnoredFifoRefuses` gives "Linux named-pipe inventory witness; native FIFO required".
- The single shared reason in each of these classes supports a class marker: CheckpointRecoveryLinuxTests, PhoneHomeMirrorJoinTests ("CP-21 phone-home join requires Linux FIFOs and Unix file modes."), GithubCredentialHelperTests ("The POSIX credential helper runs on Linux."), HostJqPrerequisiteScriptTests (the const), RemoteScriptContractTests (40 names, the CARD-1050 C1008 reason) and ScriptHarnessLinuxOwnershipTests.

**(3) Subclass refusal.** The fixture is concrete. R-12 compiles a real `DynamicTestBuilderAttribute` subclass; XML docs confirm `#ctor(System.String,System.Int32)` in TUnit.Core 1.44.0. A row-matched class holds a marked ordinary `[Test]` plus the builder method. The test requires the named `unsupported-test-attribute` diagnostic with and without a marker. A TestAttribute-only witness resolves the row through the ordinary test and emits no such diagnostic, so the fixture goes red. The named-diagnostic requirement also stops a pass for the wrong reason, such as empty resolution caused by load-context identity. A dedicated PC exists.

**(4) Red manifest and table.** RED-1 is an ignored `.antiphon/c1613-red/manifest.yaml` that I ran positionally (`run <yaml>`). `Program.cs:642-645` loads a positional manifest, so the command works. It requires exactly 1 executed / 1 failed with named `missing-marker` diagnostics, and its receipt is kept and never validated as green. My own `import --plan` at b78b6733 (through build-slot, lease a3e3e452) exited 0 with **13 rows**. The only warning was the expected CP-9 separate-Windows-build duplicate. Each row's estimate imports with timeoutMinutes 15. Totals: **29 min** (Linux 22 / Windows 7) and **80 parent results**; RED-1 adds 5 min. Every run is under 30. Exact counts checked against source:
- CP-7 = 17: ReleaseGatePlatformTests 4, RcRosterRepairTests 3, NightlyScriptsTests 5, C1514LandingSplitBudgetTests 5.
- CP-8 = 23: TestClassificationGuardTests 1 and TestLaneCategoryGuardTests 1, plus ProcessSpawnLimitTests 21 (9 `[Test]`, 14 `[Arguments]`: 7 + 6 + 8).
- CP-12 = 3 and CP-13 = 3: the methods exist at `WindowsUnitPlatformAdmissionTests.cs:26/49/65` and `C1008PlatformContractTests.cs:59/75/79`.
- CP-10 = 1 (`:431`) and CP-11 = 2 (`:102/:111`).

**(5) rc ordering.** The carrier commits d41d959b3, df6eb4090, 4ea3b9c59, 5f3df45e6, 67ee1aba3 and 8edda9996 are all ancestors of `origin/rc/2026-10-27` (= 8edda999) and none are on master. Master (`0dbbbe490`) has 0 Linux-only exclusion rows, no lexer, and policyHash `67fb4993...`. The plan's "S1 -> S2 -> S3 after this carrier" is therefore the correct (rc) base, and its "both rc and master" landing correctly puts master behind a dependency carry. But the plan never names an explicit Code start ref (A-1). The carrier branch has since moved to `f23158554` (a docs report only).

## Advisories (not blocking)

- **A-1:** State the Code start ref explicitly: `origin/rc/2026-10-27` at or after `8edda9996`. Do not start from the plan branch: `b78b6733` sits on `91b45006b`, 114 rc commits behind, lacks the CARD-1605 roster, and 9 of its inventory files differ from rc. rc carries no `docs/superpowers/plans`, so also say how Code gets the plan: an ignored `.antiphon/` copy passed to `--plan`/`import`, or a committed plan file that is dropped from the rc port. Name the master port as a separate task after rc.
- **A-2:** In baseline step 2, list the extractor's rules for the shapes above. Explicit constant-argument propagation into helper parameters is needed because of ScriptHarnessLinuxOwnershipTests; without it, Code may read that row as "unproved" and stop with no authorized path. Also:
  - A skip inside try/catch (`catch (Exception)`, a catch-all or a `when` filter), a lambda, a local function or async void is unproved.
  - Pre-gate work (base `[Before(Test)]`, constructor or field initializers, asserts before the gate) and post-skip `[After(Test)]`/Dispose are recorded per node. A potentially throwing pre-gate step is either reviewed as non-throwing or listed as an accepted Failed-to-Skipped change.
  - The native fallback ("commission its exact native before/after test") gets a named owner, platform and budget.
- **A-3:** Specify the semantic-model closure for the in-host Roslyn audit. Parse only the participating trees and reference the host's own built assembly plus its reference set, accepting CS0436 duplicates. Do not embed or compile the whole suite source at test time. RemoteScriptContractTests alone is about 9k lines. Measure parse/bind time inside the 80 s row cap.
- **A-4:** HostStatsProbeLiveTests' macOS reason also changes, from "another OS" to "Windows". Add it to D-1's macOS-narrowing note.

## Evidence

- `import --plan` at b78b6733 → exit 0, 13 rows (manifest at ignored `.antiphon/c1613-r2review/manifest.yaml`). The tool was built through build-slot (lease 14b87f6d, 0 errors, 5 warnings) to a scratchpad `bin-tool/` outside the repo.
- `check-evidence-diff.ps1 -Repository . -BaseRef 91b45006b -HeadRef b78b6733` → commits=17, entries=5, violations=0.
- No TUnit selection was run: the subject is a plan with no implementation. ordinaryScopeCompleted is None.
