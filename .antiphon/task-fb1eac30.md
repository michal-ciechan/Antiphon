# CARD-1613 Final Review (Interim scope) — task fb1eac30

Subject task `626bfbff`, branch `feat/card-task-626bfbff`, reviewed code tip
`aa72491b72b1af57141e7c37ac50a384485755a0`. The branch tip `65e6e8c87` adds only the
author's report. Merge-base with rc `ade54222` is `8edda999623a473be2d1970727c172bfb5088d59`.

**Verdict: 2 defects plus 1 minor defect; next: code.** The migration itself is sound: it is
purely additive, and every spot-checked reason is byte-equal to its live gate. The new
reflection witness judged 25 of 30 adversarial compiled mutations correctly. Two genuine
holes remain: one in inherited-test enumeration and one that makes the native Windows parent
always fail.

## Scope (Interim, disclosed)

- **Ran:** Linux checkpoint rows CP-1..CP-8, CP-12 and CP-13. The detail is in the Checkpoints
  section at the end of this report.
- **Pending:** Windows rows CP-9, CP-10 and CP-11. They were not run on Windows.
- **Not run:** the whole Unit lane, and the PCs (which stay pending).

## Defects

### D-1 — Witness fooled by an inherited base test hidden with `new` (no backstop)

- **Where:** `tests/Shared/NativeLinuxPolicyWitness.cs:530-544`, in `TestNodes`, at
  `else if (current != chain[0]) chosen.Remove(signature);`.
- **Failure:** take this compiled fixture:

  ```csharp
  abstract class HBase { [Test] public void Hidden(){} }
  [InheritsTests] class HDer : HBase {
      public new void Hidden(){}
      [Test, NativeLinuxOnly("…")] public void Own(){}
  }
  ```

  With the policy class row `HDer`, the witness is **GREEN**. Real TUnit 1.44 still runs
  `HDer.Hidden`; I confirmed this from a scratch TUnit project, where the TRX has
  `className="P.HideDerived" name="Hidden"`. That node has no marker and no gate, so it can run
  on Windows. The policy silently excludes it on Windows, and nothing else checks this.
- **Why:** a derived non-test member with the same signature removes the base `[Test]`
  MethodInfo from the node set, but TUnit discovers the base method anyway.
- **Same root, also fooled:** `M7b`. An `[InheritsTests]` class with a class marker is matched
  by a policy row and inherits a `DynamicTestBuilderAttribute` subclass method from its base.
  The witness is GREEN, with no `unsupported-test-attribute`. The cause is that
  `RefuseUnsupportedMembers` (line ~319) and `RefuseUnsupported` only inspect `DeclaredOnly`
  members of the concrete type. Plan D-11 requires a refusal for a policy-matched type.
- **Fix:**
  1. Keep every base `[Test]` MethodInfo along the `InheritsTests` chain, keyed by MethodInfo
     rather than by signature. Drop a base method only when a real override replaces it.
  2. Walk the inherited chain in the BaseTestAttribute refusal.
  3. Add both fixtures to R-4 and R-12.

### D-2 — The native probe parent hard-codes Linux, so CP-9 and the Windows lane always fail

- **Where:** `tests/Antiphon.Tests/Scripts/NativeLinuxPolicyExecutionTests.cs:50`, which calls
  `Problems(linux: true, …)`. It is the only `[Test]` in the class, and the class has no OS gate.
- **Failure:** on native Windows the child correctly gives 1 passed and 8 skipped, but the
  parent asserts `linux-counts passed=9`, so it fails. The plan's CP-9 row expects this parent
  green, with "child 8 skipped/1 passed with exact reasons". The class is not excluded, so the
  Windows rc lane also goes red.
- **Gap in the Windows branch too:** `linux:false` is only exercised with synthetic numbers. It
  never compares the 8 skip reasons against the probe's marker strings, although plan D-8 and
  CP-9 require exact reasons.
- **Fix:**
  1. Pass `linux: OperatingSystem.IsLinux()`.
  2. Read each skipped TRX result's message and byte-compare it with the eight probe reasons
     (`"direct class runs on Linux"`, and so on).
  3. Keep the synthetic negative vectors.

### D-3 (minor, backstopped) — The witness skips a frozen row whose reason or platform is edited

- **Where:** `NativeLinuxPolicyWitness.cs:242-246`, together with the row loop at
  lines 290-293 and `ReadRow` at line 593. `Witnessed` is computed from the prefix and platform,
  not from `WitnessedRowKeys` membership. A frozen key is skipped from the legacy digest, and
  `CompareBaseline` silently `continue`s when the marker is missing (line ~392).
- **Failure:** mutations `M15`, `M15b` and `M15c` made the witness GREEN with policyHash
  resealed and the marker removed:
  - `PhoneHomeMirrorJoinTests` reason changed to `Windows lacks …`.
  - `UnixDelegateLaunchArgvTests` with `platform: linux`. This excludes the test on Linux.
  - A row with `platform: all`.
- **Mitigation:** in scratch copies, `test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy`,
  which runs as `ReleaseGatePlatformTests.LinuxOnlyWindowsExclusionPolicy` in CP-7, **fails**:
  - The reason variant fails with `every-new-row-has-linux-gate actual=44 expected=45`.
  - The platform variant fails `every-new-row-has-linux-gate … platform`,
    `linux-only-rows-leave-census-complete` and `linux-still-requires-the-rows`.

  So the rc gate as a whole is not fooled. The compiled witness alone still deviates from plan
  D-5, which says to "require the existing Linux reason prefix and platform: windows".
- **Fix:**
  1. Treat a `WitnessedRowKeys` member as witnessed regardless of reason.
  2. Emit `witnessed-row-reshaped` unless platform is `windows` and the prefix is present.
  3. Fail when a frozen key is absent from the policy, or when a roster identity loses its
     marker in `CompareBaseline`.
  4. Add these as R-13 variants.

## Adversarial mutation results (scratch Roslyn harness against the real witness source at aa72491b)

The harness compiles each fixture in memory, binds the real pinned attribute and calls
`NativeLinuxPolicyWitness.Inspect` against the live policy. The live policy has the 45 frozen
rows stripped and the fixture rows added, then is resealed.

**Judged correctly (25):**

- **Control:** C0, a properly marked class, is GREEN.
- **RED as required:**
  - ungated overload
  - case-twin class
  - case-twin method
  - nested decoy
  - outer-marker-only nested
  - InheritsTests unmarked base test
  - Conditional helper
  - look-alike in another namespace
  - look-alike with the same full name from another assembly
  - BaseTestAttribute subclass on the type itself
  - base-class marker only
  - a different reason compared with the frozen baseline (`reason-mismatch`)
  - a row added without a marker
  - a row deleted while the marker stays (`reverse-coverage`)
  - an empty reason
  - an unmarked generic
  - a plain row against a generic, which gives `unresolved-row`
  - a static test
  - a class marker on a method row
  - a full-name row in another namespace
- **GREEN, correctly:**
  - a base `[Test]` without `InheritsTests`; TUnit does not run it, and the TRX confirms this
  - a `new [Test]` hide; TUnit runs only one node
  - a `methods:[""]` row, where the witness is stricter than the matcher

**Fooled (5):** M4c and M7b (D-1), and M15, M15b and M15c (D-3, backstopped by the PowerShell
case).

## Migration (CHECK 1)

- **Diff base:** `git diff 8edda999..aa72491b -- tests/`. The brief's
  `ade54222..aa72491b` also reverse-shows 6 rc-only commits, such as the HostCustody C478 gate
  and the LandGraphEvaluationScript budget asserts. Those changes are not part of this branch.
- **Removed lines:** the only one is the `LinuxOnlyWindowsExclusions` adapter rename in
  `ReleaseGatePlatformTests`.
- **Added lines:** 99 method markers, 23 class markers and 46 `using Antiphon.TestSupport;`.
  There is no other source change: no removed gate, relaxed assertion, deleted test or changed
  reason.
- **Marker arithmetic:** 25 class rows less 2 mixed classes gives 23 class markers. 95 method
  names plus 4 mixed-class method markers gives 99.
- **Spot-checked rows (marker equals the live gate reason, byte for byte; 17 rows):**
  - SafeDeleteProcessBoundaryTests (both methods)
  - CheckpointFixtureProcessTests (both methods)
  - RollingVolumeRecycleScriptTests (via `NativeLinuxGate.Require`)
  - RemoteScriptContractTests: 5 methods spanning C1374, C1105, C849 and C1335 through
    `C1105AuditContract`, plus all 40 with the same CARD-1050 text
  - HostStatsProbeLiveTests (the folded Windows branch)
  - CompletedCardWorkspace R3 (with `[Arguments]`) and S5
  - LinuxCgroupCustodyTests (class)
  - UnixPtyArgvTests (agents-pty)
  - GithubCredentialHelperTests (fixture constructor)
  - ScriptHarnessLinuxOwnershipTests (`RequirePlatform("other")`)
  - CheckpointRecoveryLinuxTests
  - PhoneHomeCompletedCardTransportTests
- **Gates:** all old gates are retained. The baseline has 256 roster identities and 256 reasons:
  antiphon 240, pty-host 8, agents-pty 5, session-runner 3.

## Policy, pins, lexer (CHECK 4)

- **Policy diff:** the only change is the probe child disposition and policyHash.
- **policyHash:** `Read-NightlyExecutionPolicy` accepts `bf703bb1…f7`. There are 133 exclusions,
  45 of them prefixed, unchanged.
- **C1410:** the pins (154 / 106343.8397645542) are unchanged, and C1410 is green in CP-7.
- **Lexer:** `scripts/lib/linux-only-gate-witness.cs` is deleted. No live reference remains;
  the only mentions are retirement notes in `docs/release-gates.md:273`,
  `docs/testing-and-build.md:34-36` and the refusing case at `test-rc-platform.ps1:658`.
- **Evidence check** (`check-evidence-diff.ps1`, base 8edda999):
  - Through aa72491b: 5 commits, 0 violations.
  - Through 65e6e8c8: 6 commits, 0 violations.

## Docs (CHECK 5)

The `docs/release-gates.md` subsection "CARD-1613 marker migration and maintenance" covers the
one-time audit and the four maintenance rules (new exclusion, reason change, rename/removal,
legacy change). `docs/testing-and-build.md` has a focused note.

## Ports (CHECK 6)

- **rc:** the merge-tree against rc `ade54222` is clean, according to the author. The 6 rc-only
  commits touch none of the 45 row classes.
- **master** (`0e7dd390520a43ab330396a72e9085a8691ac332`) now carries the 45 exclusions and the
  lexer:
  - The policy is byte-identical to `8edda999`, with hash `a7c88a0a…`.
  - A whole-branch merge-tree has the 14 conflicts the author listed.
  - The card-delta projection `merge-tree --merge-base 8edda999 origin/master aa72491b`
    **exits 0**.
- **What a clean master port must keep:**
  - Master's unrelated add/add files.
  - The child disposition with the re-sealed `bf703bb1…` hash, valid only while master's
    policy still equals rc's pre-migration bytes.
  - A rerun of all four witnesses, CP-7, CP-12 and CP-13 on master's tree. Master-only tests
    added to a class row need markers.

## Advisories

- **A-1:** the matcher parity matrix is fixed literals. Nothing feeds the probe's real TRX
  ClassNames (nested or generic) back into the projection check, as the plan intended. Both
  directions fail closed today: an unresolved row is red, and a projection that is too broad
  only over-requires markers.
- **A-2:** the macOS narrowing and the HostStats macOS text change are accepted in plan D-1;
  they are noted, not defects.
- **A-3:** D-15 test-only applicability holds. `scripts/lib` deletion has no production
  consumer; `rc-project-selection.ps1` drops only lexer listing lines.

## Checkpoints (this review)

Run `20261029-213149-864d` on aa72491b, `--serial`, through the checkpoint tool, which took a
slot for each row and build:
**GREEN, exit 0. 10 rows, 73 parents executed, 73 passed, 0 failed, 0 skipped.** Source
`state=clean`, `buildSource=verified`, every row `slot=granted`, `unlisted: none`.

| Row | Executed/passed | Wait / execution |
|---|---|---|
| CP-1 | 3/3 | |
| CP-2 | 10/10 | |
| CP-3 | 2/2 | |
| CP-4 | 4/4 | |
| CP-5 | 4/4 | |
| CP-6 | 4/4 | |
| CP-7 | 17/17 | waited 420 s, executed 148 s |
| CP-8 | 23/23 | |
| CP-12 | 3/3 | |
| CP-13 | 3/3 | |

- CP-7 covers RcRosterRepairTests ×3, C1404, C1577, C1584, C1410 and G2865, plus
  `ReleaseGatePlatformTests.LinuxOnlyWindowsExclusionPolicy` (the test-rc-platform.ps1 harness).
- CP-12 includes C1210_Roster_closure.
- Timing: wall 55m37s, of which 2580 s was admission-only slot contention and 757 s was
  execution. SLOW CLASS ReleaseGatePlatformTests took 67 s.
- The tool deleted its `bin-c1613-*` outputs.
- Evidence: `.antiphon/checkpoints/20261029-213149-864d/report.md`, which is ignored.

CP-1 and CP-2 green does not refute D-1 or D-2. Neither row contains the inherited-hide fixture,
and CP-3 runs only on Linux.

Scratch harness (not committed):

- `<scratch>/wmut/Program.cs`, run output `<scratch>/wmut/run.log`.
- TUnit discovery probe `<scratch>/tunitprobe`.
- PowerShell policy mutations `<scratch>/ps-{reason,platform}`.
- `<scratch>` is `/tmp/claude-1654/-work-worktrees-task-fb1eac30/ccbbd461-1380-41f1-8d88-dc87ce240de2/scratchpad`.
