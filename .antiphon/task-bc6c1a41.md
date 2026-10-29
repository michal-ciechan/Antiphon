# Review bc6c1a41: CARD-1613 plan (pinned Linux skip attribute + reflection witness)

Subject: plan `docs/superpowers/plans/2026-10-29-card-1613-pinned-linux-skip-reflection-plan.md`,
added by `5cdbf1d5de14ccfeeb6dab654f20c12c35a235ca` (tip of `origin/feat/card-task-107cafd6`, Plan task
`107cafd6-787d-4781-ba43-8db751d056a3`). Read-only design review on server2/Linux. No product, test or plan file changed.

Verdict: the design is right: one sealed, type-pinned SkipAttribute added alongside the existing gates
(nothing removed), plus a reflection witness in each owning suite, with the lexer retired. Four defects
need a plan revision before Code. Two concern the witness/migration semantics (D-1, D-2) and two concern
the checkpoint table (D-3, D-4).

## Defects

### D-1: The skip reason and Windows outcome are unspecified for each migrated node, and nothing witnesses them

- **Where:** Decisions D-1/D-2; Migration inventory; CP-9..CP-11.
- **Failure:** TUnit applies the attribute at registration, so on Windows the reported skip reason
  becomes the **attribute's** reason. The retained body gate never runs. The plan says to "preserve
  reason strings" but never says which string each attribute carries, and a class attribute holds
  only one string. At the source SHA:
  - `SafeDeleteProcessBoundaryTests.cs:20` "FIFO-held monitor proof runs on Linux" and `:66`
    "process-group custody proof runs on Linux". `CheckpointFixtureProcessTests.cs:21`/`:60` also have
    two different reasons. Both are class rows.
  - The C1210 roster reasons (`WindowsUnitSkipRoster.cs`, for example "CARD-1210: C1025/C1058 host jq
    contracts require native Linux ...; Windows skips explicitly.") differ from the policy reasons
    ("Linux-only by construction: ..."). C1210 audits `"Skipped: " + reason` exactly
    (`WindowsUnitPlatformAdmissionTests.cs:209`).
  - CP-10/CP-11 cover only the C1210/C1050 subsets of Antiphon.Tests. The other rows get no native
    Windows run before or after migration: Checkpoint*, PhoneHome*, Unix*, all of
    session-runner/pty-host/agents-pty, and the non-roster method rows. A changed reason, or a node
    that passed on Windows before and is now skipped, would go unseen.
- **Why:** the brief requires that a test skipped on Windows today stays skipped there with the same
  semantics, and the plan claims the migration is reason-preserving. Neither is checked.
- **Fix:** state the rule: each attribute's reason is, byte for byte, that node's current native
  Windows skip reason. Use a class attribute only where every node in the class shares one reason;
  otherwise put method attributes on every test (D-4 already accepts method markers for class rows).
  Add a native Windows before/after comparison of TRX outcome and reason for every migrated node in
  all four suites (Windows rows for runner/pty/agents, or one before/after child audit). The
  alternative is to accept and list the reason changes outside C1210/C1050 explicitly.

### D-2: Finding tests by exact `TestAttribute` identity fails open

- **Where:** Reflection algorithm step 2 ("exact TUnit TestAttribute identity").
- **Failure:** TUnit 1.44 documents `TUnit.Core.BaseTestAttribute` as "Inherit from this class to
  create custom test type attributes". Suppose a future class-row type gains a test declared with such
  an attribute. TUnit discovers it and the matcher excludes it, but the witness never enumerates it, so
  it needs no marker. A Windows-running ungated test then hides behind a class row, which is exactly
  the threat this card targets. The plan otherwise fails closed but does not cover a test it cannot see.
- **Fix:** treat any method attribute whose type derives from `TUnit.Core.BaseTestAttribute`
  (framework type identity, not name) as a test node, or refuse any non-`TestAttribute`
  BaseTestAttribute found in a row-matched type. Add that shape to the R-12 fixtures.

### D-3: CP-1 is an expected-red row inside the closed list

- **Where:** Checkpoints CP-1 (and CP-2, which shares its run).
- **Failure:** the importer reads `Expect` as roster tokens. In the imported manifest, CP-1 is
  `expect: [NativeLinuxPolicyWitnessTests]`, `minExecuted: 1`, and "1 failed" survives only as
  `expectText`. docs/testing-and-build.md:481 allows only `all listed, 0 failed` or
  `>= N executed, 0 failed`, and rule 1 (:496) says a red row is fixed and rerun as the same row.
  CP-1 is red by design, so `validate` refuses the whole S1 report (row_failed) and CP-2 gets no clean
  receipt either. Neither row is rerun at the final tip.
- **Fix:** move the RED proof out of the table. Run it from a separate ignored red manifest
  (`start --plan .antiphon/...`) and keep its receipt. Alternatively, keep CP-1/CP-2 and rerun both
  green at the final tip with `reruns=` counted.

### D-4: The table does not import, and CP-8's count is not exact

- **Where:** Checkpoints CP-8, CP-12, CP-13.
- **Failure:** `dotnet run --project tools/Antiphon.Checkpoints -- import --plan <plan>` (through
  build-slot) exits 2 with `CP-8: EstimatedMinutes must be a positive integer with safe derived
  deadlines`. CP-12 and CP-13 also say 0.5. With those three set to 1, all 13 rows import; the
  CP-9 duplicate-build warning is expected for a separate Windows invocation. CP-8 has Min 3, but the
  exact count at the source is **23**: TestClassificationGuardTests 1, TestLaneCategoryGuardTests 1,
  and ProcessSpawnLimitTests 7 plain + 6 + 8 `[Arguments]` = 21.
- **Fix:** use integer minutes. The total becomes 31, but no single run exceeds 30: Linux S1 6,
  Linux final 18, Windows 7. Set CP-8 Min to 23 plus any limiter cases the new tests add, and restate
  the 29.5-minute total.

## Counts checked against the source

- Policy: 133 rc exclusions. The 45 Windows rows with the Linux prefix are 25 class + 20 method rows
  naming 95 methods, split antiphon 41 / session-runner 2 / pty-host 1 / agents-pty 1. All exact.
- CP-7 is 18: ReleaseGatePlatformTests 4 (+1 new adapter), RcRosterRepairTests 3, NightlyScriptsTests 5,
  C1514LandingSplitBudgetTests 5. Review 427cd766 ran the first three classes 12/12 green on Linux,
  including C1323_PlatformExecution.
- CP-4/5/6 are 4 each: 3 witness methods + the shared `TestClassificationGuardTests` (1 test, linked
  into all three csproj).
- CP-10 (1), CP-11 (2), CP-12 (3) and CP-13 (3): every named method exists. None of the OR operands has
  a prefix collision.

## Answers to the brief's checks

1. **Attribute.** It is sealed, matched by exact `typeof` identity and module, and its source is
   hash-pinned, so a look-alike fails. The migration is additive: every gate stays. On Linux the
   predicate is false, so Linux behaviour is unchanged, including the LinuxCgroupCustodyTests
   prerequisite skips. On Windows the tests stay skipped, now earlier (no ctor, fixture or Before(Test)
   work). The only changes are reason and order (D-1). The macOS narrowing for
   UnixPtyArgvTests/UnixDelegateLaunchArgvTests is acknowledged in the plan. For inheritance, the
   plan's conservative direct-marker rule is sound, and the native probe proves the inherited,
   derived, nested and generic shapes.
2. **Reflection witness.** It is sound apart from D-2. Each class row and each listed method name must
   resolve, and empty or partial resolution fails. A marker on the wrong member (sibling, local
   function, base/enclosing type) does not count. A method marker covers all of a parameterised
   method's cases. `[InheritsTests]` is honoured. Names are checked against the real PowerShell
   matcher (parity test) and the native probe's TRX.
3. **RED witnesses.** Every family is a compiled, real synthetic type, plus the real-policy red CP-1.
   They cannot run against the current code because the inspector does not exist yet; they are
   implementation obligations. The acceptance "adding a row without the attribute fails" holds through
   R-13 and through each suite's `Policy_rows_require_pinned_attribute` running in the Unit lane and in
   Windows Full.
4. **Lexer deletion.** The plan's reference list is complete: `scripts/test-rc-platform.ps1:505-512`
   and `:1039`, `ReleaseGatePlatformTests.cs:33-34`, and `docs/release-gates.md:270`. There is no
   scriptCensus or `.gitattributes` entry. The policyHash literal appears only at
   `tests/test-execution-policy.json:3`. C1410 (154 launches / 106343.8397645542 s) uses the frozen
   listing plus the live exclusions, which do not change, so the plan's "unchanged" claim holds.
5. **Migration.** It is mechanical and additive, and the 46-file list matches the policy. For overlap
   with in-flight work, see A-4.
6. **Checkpoints:** see D-3 and D-4. Classification, limiter and C1210/C1050 census obligations are
   carried by CP-8/CP-12/CP-13.

## Advisories (not blocking)

- **A-1:** R-5..R-10 are lexer-era families. Under a marker-only inspector they all reduce to
  "a matched node without a marker is refused" and share one PC (no-marker acceptance), so six
  parents cannot go red independently. Collapse them into one parameterised family or label them as
  regression documentation, and restate the PC count.
- **A-2:** S3 also adds a LinuxSessionRunnerAdmission TUnit adapter, which is outside this card and
  would then run in Windows Full. Confirm the case is portable or drop it (CP-7 becomes 17).
- **A-3:** the 88-record legacy digest couples every future manual/provider rc exclusion edit to a
  pin in `tests/Shared`. Document the update procedure in docs/release-gates.md.
- **A-4:** in-flight overlap. Branches active in the last 3 days touch many inventory files: the
  CARD-1594 RollingVolumeRecycleScriptTests chain, the CARD-1596 AnchoredPathDeleteWitnessTests chain,
  PhoneHomeMirrorJoinTests (a0a9cfee), CheckpointRecoveryLinuxTests (a3fbae4d), the
  LandGraphEvaluationTests chain, and RemoteScriptContractTests (49fc10b8/7519bc94). A second carrier
  of the policy chain, `feat/card-task-5002b2ef` at 67ee1aba3 (same subjects, different SHAs), exists
  beside cd5fc194/d553548e9. Code should re-run the overlap scan at its base and name the carrier that
  rc top-up 36 takes. The edits are one-line attribute additions, so conflicts will be mechanical.
- **A-5:** state whether `check-os-witnesses.ps1` applies to this test-only change. CP-3 (Linux) and
  CP-9 (Windows) are the native witnesses for both branches of the predicate.
- **A-6:** CP-7's EstimatedMinutes of 2 is optimistic for 18 harness parents, including the C1410
  listing. The derived 15-minute deadline covers it, but record measured values.
- **Note:** the Plan task 107cafd6 shows status Failed in `/api/agent-tasks`, although the plan commit
  is pushed.

## Evidence

- `import --plan` on the plan: exit 2 (CP-8 EstimatedMinutes). Re-run on an ignored copy with integer
  minutes: exit 0, 13 rows (`.antiphon/c1613-review/`, ignored). Both ran through build-slot (leases
  ca6e897a, then the --no-build rerun). The tool built to the default tools/src bin directories of
  this mirror; no bin-<name> directories were created.
- `check-evidence-diff.ps1 -BaseRef 91b45006b -HeadRef 5cdbf1d5d`: 10 commits, 0 violations.
- No TUnit selection was run, because the reviewed subject is a plan with no implementation.
  ordinaryScopeCompleted is None.
