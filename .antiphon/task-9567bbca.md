# Review 9567bbca: gate policy witness round 2 (subject defe39ed)

Outcome: **the witness can still be fooled.** D-1 (neighbour window) and the round-1 conditional-gate mutations M2 and M3 now fail, as the author claimed. But **17 of 30 new adversarial mutations pass the real `LinuxOnlyWindowsExclusions` case**. In each of them a test that runs on Windows is admitted. The 45 shipped rows are still correct. None of the fooling shapes exists in the current sources, so no hidden Windows-running test ships today. The witness is the standing proof of the operator rule for future edits, and it does not hold. **next: code.**

Reviewed: code `19435beb086327d5c557b299cfb9330c541b47a7`, branch tip `4f8ab21a79fffef6acf4f15fd12e5e99968c0b5d` (that tip adds only the author report). Base rc `91b45006b`; current rc is `cf1a12403` and the merge is clean. Scope: **Interim**, as the brief commissioned. I ran the named selection, not the whole Unit lane.

## Method

- I extracted a scratch `git archive` of `19435beb0` into the session scratchpad. The subject branch was not touched.
- For each mutation I edited one real source file, ran `pwsh scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusions` from the scratch tree, then restored the file.
- All of this ran under `scripts/build-slot.ps1`, in leases 77b554e3, 32443e61 and b8d64af9.
- A gate removal is applied as an edit to `GrokSha256PinTests.Runner_grok_sha_rejects_multiline_pin`, the first test, followed by a gated neighbour, unless the row says otherwise.

### Mutations the witness correctly refuses (FAIL `every-new-row-has-linux-gate`, 7/1)

- Unmutated control: PASS 8/0.
- D-1: gate removed from the Grok test placed before a gated test. Result: `ungated Runner_grok_sha_rejects_multiline_pin`.
- M2: `CheckpointRecoveryLinuxTests.Linux()` now ends `return File.Exists("C:\\tools\\wsl-sh.exe")`. All 10 tests are reported ungated.
- M3: `NightlyProbeSupervisorTests` gate placed under `if (Environment.GetEnvironmentVariable("X") is null)`.
- Gate only inside an uncalled block-bodied local function.
- Gate in a `//` comment, in a string literal, or in a `/* } */` comment.
- Gate after `return;`, and gate after `if (...) return;`.
- Gate inside `try { … } catch (Exception) { }`.
- Gate replaced by `[Test, SkipWhen(false)]`.
- Brace confusion that resolves correctly: verbatim string `@"} } … ""x"" "`, raw `"""…}…"""`, `$$"""} {{1}} {…"""`, char `'{'`/`'}'`, and the interpolated `$"{{ {(… "}" : "{")} }}"`.

### Mutations that FOOL the witness (PASS 8/0, every one admits a Windows-running test)

| # | Mutation | Scanner cause |
|---|---|---|
| N1 | `System.Action never = () => NativeLinuxGate.Require(...);` (never invoked) | D-1 |
| N2 | `static void Never() => NativeLinuxGate.Require(...);` (never called) | D-1 |
| N17 | `using var fixture = Env("X") is null ? null : new HelperFixture();` (GithubCredentialHelperTests) | D-1 |
| N18 | `_ = Task.Run(() => NativeLinuxGate.Require(...));` (skip lost on another task) | D-1 |
| N19 | `_ = Env("X") is null ? 0 : Gated();`, where `Gated()` begins with the gate | D-1 |
| N11b | `System.Console.Write('\u0041'); if (Env("X") is null) NativeLinuxGate.Require(...);` | D-2 |
| N11c | the same with `'\x41'` | D-2 |
| N13 | gate placed between `#if ANTIPHON_NEVER_DEFINED` and `#endif` | D-3 |
| N15 | `NopProbe.RequireLinux();`, with a new `TestHelpers/NopProbe.cs` whose body is empty | D-4 |
| N8 | an ungated `[Test][Arguments(1)] C1008_Recycle_exact_default_volumes(int)` overload added after the gated one (RemoteScriptContractTests method row) | D-5 |
| N8b | an ungated case-twin `c1008_recycle_exact_default_volumes()` added | D-5 |
| N16 | new `tests/Antiphon.Tests/Elsewhere/GrokSha256PinTests.cs`, a second class with the same simple name and an ungated `[Test]` | D-5 |
| N22 | `GrokSha256PinTests` made `partial`, with an ungated `[Test]` in the sibling file `GrokSha256PinTests.Windows.cs` | D-5 |
| N21 | `[InheritsTests]` added to CheckpointBoundedReaderTests, plus an ungated `[Test]` in a new `partial CheckpointTestBase` file | D-5 |
| N20 | `WindowsWork(); NativeLinuxGate.Require(...)`, where `WindowsWork()` is `Assert.Fail(...)` | D-6 |

## Defects

### D-1: a gated call anywhere in the first statement counts, including lambdas, local functions, ternaries and `new` under a condition
- **Where:** `scripts/lib/linux-only-gate-witness.cs:212` (`StatementIsGate` → `HasGatedCall`), lines 306-331, and `WalkCalls` lines 403-431. `WalkCalls` skips only `{…}` depth. Every call token at depth 0 counts: inside `=>` lambdas, expression-bodied local functions, `?:` and `&&`/`||` operands, `Task.Run(...)` arguments, and `new T()` in a ternary.
- **Failure:** N1, N2, N17, N18 and N19 above all pass. In each, the skip never runs on Windows, yet the class row drops the test.
- **Fix:** accept a call as a gate only when it is the whole statement: `Gate(...);`, `await Gate(...);`, `using var x = new Fixture(...);` or `var x = [await] Gate(...);`. Its arguments must contain no `=>`, `?`, `&&`/`||` or `delegate`. Add synthetic red rows for N1, N2, N17, N18 and N19.

### D-2: char literals with `\u`/`\x`/`\U` escapes desynchronise the tokenizer, and the misparse is accepted rather than refused
- **Where:** `linux-only-gate-witness.cs:992-999` (`Scan`), duplicated in `Hole` at 1105-1111. After `'\`, the scanner skips exactly one character and then expects `'`. For `'\u0041'` it emits `0041` and then treats the closing `'` as a new literal that swallows the next character, here `)`. `ConsumeSemi` then never closes the paren, so statement 0 runs to the end of the body, and the conditional `NativeLinuxGate.Require` inside it counts as a top-level gate.
- **Failure:** in N11b and N11c, a gate conditional on an environment variable is admitted. That is the M3 hole, reopened by a one-character preamble.
- **Fix:** lex char literals as C# does: a `\u` escape is followed by 4 hex digits, `\U` by 8, and `\x` by 1-4. Refuse with a problem when a literal is unterminated, or when parens, brackets or braces do not balance inside a body. A misparse must refuse, never accept. Add `'\u0041'` and `'\x41'` synthetics.

### D-3: preprocessor-excluded code counts as code
- **Where:** `linux-only-gate-witness.cs:972-976`. Directive lines are dropped, but the lines between `#if X`/`#else`/`#endif` stay in the token stream whatever the symbol is.
- **Failure:** in N13, a gate that is never compiled is admitted.
- **Fix:** refuse, with problem `preprocessor`, any test, helper, `[Before]` or constructor body that contains `#if`/`#elif`/`#else`, or that is enclosed by one. Add a synthetic.

### D-4: unresolved cross-file helpers are trusted by name
- **Where:** `linux-only-gate-witness.cs:370-376` (`KnownExternal`), used at 328. Any unresolved call named `RequireNativeLinux`, `RequireUnix`, `RequireLinux`, `RequireLinuxJoin` or `RequireLinuxCustody` counts as a gate, with any qualifier, and so does `NativeLinuxGate.Require`. The callee's body is never read.
- **Failure:** in N15, `NopProbe.RequireLinux()` with an empty body is admitted. The repo already has several `RequireLinux` helpers, so the name is not a proof.
- **Fix:** trust only exact qualified externals whose bodies the witness reads and checks itself: `NativeLinuxGate.Require` (TestHelpers/NativeLinuxGate.cs) and `C1008HostFixture.RequireNativeLinux` (RollingVolumeRecycleScriptTests.cs:2770). Treat every other unresolved name as ungated.

### D-5: the witness reads one file per class and matches identities more strictly than the exclusion it is meant to prove
- **Where:**
  - `scripts/test-rc-platform.ps1:571` (`Read-ClassLines`) reads only `<dir>/<Class>.cs` from 7 fixed directories, or 2 hard-coded files for CompletedCardWorkspaceRemovalTests.
  - `linux-only-gate-witness.cs:128-141` judges only the first method whose name matches ordinally.
  - The exclusion it is meant to prove, `scripts/lib/nightly-policy.ps1:459,465` (`Test-NightlyProfileNodeExcluded`), matches the class by **simple name, case-insensitively, in any namespace or file**, and the method by **name, case-insensitively, across all overloads**.
- **Failure:** each of these lets an ungated test be excluded on Windows while the witness reads only the gated twin: N8 (overload), N8b (case-twin method), N16 (same simple class name in another directory), N22 (partial sibling file) and N21 (`[InheritsTests]` with a base-class `[Test]`). LandGraphEvaluationTests and CompletedCardWorkspaceRemovalTests are already partial across 7 files each.
- **Fix:** make the witness see what the matcher sees. For each row, enumerate every `tests/**/*.cs` file of the row's suite project and collect every type whose simple name equals the row class ignoring case, all of its partial parts, and any base class when `[InheritsTests]` is present. Judge every test method whose name equals a row method ignoring case, across all overloads. Refuse when the set is empty. Add synthetics for each of the five shapes.

### D-6: the one-call preamble may run arbitrary Windows work before the skip
- **Where:** `linux-only-gate-witness.cs:204` and `216-234` (`IsPreamble`/`StraightLine`). Any single local call to a method with no control-flow statements is accepted before the gate, and that method may assert, spawn processes or call anything.
- **Failure:** in N20, `WindowsWork(); NativeLinuxGate.Require(...)` is admitted with `WindowsWork() => Assert.Fail(...)`. The test fails on Windows today, and the exclusion hides that failure.
- **Fix:** allow only the one shipped preamble, `RequireClient` (PhoneHomeCompletedCardTransportTests.cs:114, two `typeof(...).GetMethod(...).ShouldNotBeNull` reflection asserts), by exact name and shape. Alternatively, move the `!IsLinux` check above `RequireClient()` in that test and drop form 8.

## Other checks in the brief

- **Scanner load and parse paths (2):**
  - The `.cs` file is loaded by `Add-Type -Path` (test-rc-platform.ps1:511-512) under `$ErrorActionPreference='Stop'`. A compile or load failure throws, the case errors, and `ReleaseGatePlatformTests` fails. That path is not fail-open.
  - The `-as [type]` short-circuit would reuse a stale type only inside a long-lived session. The harness spawns a fresh `pwsh` per case.
  - `#requires -Version 7.0` (line 1) makes Windows PowerShell 5.1 refuse the whole script, which fails closed. The C# itself is C# 5-clean: no `?.`, `$""` or expression members.
  - `RowProblems` turns an exception into `parse-failed`, which refuses. But the parser almost never throws. It silently misparses instead, which is D-2.
  - It reads the working-tree source through `$repoRoot` (`ReadAllLines`), not a cache.
  - It is deterministic: ordered lists, no hashing of iteration order.
- **The 45 rows (3):** I re-verified 19 rows by hand at `19435beb0`. Test sources are unchanged since round 1's 65862ecfd: `git diff 3b544b9f7..19435beb0` touches only the witness, the script and ReleaseGatePlatformTests.
  - **Class rows:** every test in each class is gated by the cited gate.
    - GrokSha256Pin (11): NativeLinuxGate.Require.
    - HostJqPrerequisite (39): NativeLinuxGate.Require.
    - PhoneHomeMirrorJoin (6): RequireLinuxJoin :303.
    - CheckpointRecoveryLinux (10): `Linux()` :245.
    - CheckpointBoundedReader (2): RunProbeAsync :48.
    - GithubCredentialHelper (6): HelperFixture constructor :88.
    - RetiredTempContainerHost (22): `[Before(Test)]` C1008.
    - RollingProductionMount (12): `[Before(Test)]` C1008.
    - ScriptHarnessLinuxOwnership (21): `[Before(Test)]` RequirePlatform :14-22.
    - CheckpointSourceGitBounds (2) and SafeDeleteProcessBoundary (2): `!IsLinux` skip.
    - LinuxCgroupCustody (8): RequireLinuxCustody :42, with the `!IsLinux` check first.
    - UnixPtyArgv (5): RequireUnix :155.
  - **Method rows:**
    - RollingVolumeRecycleScript, all 10: C1008 first line.
    - RemoteScriptContract, 14 sampled: C1008 inline, or C1105AuditContract :2732, which calls C1008 first.
    - PhoneHomeCompletedCardTransport.C1032: `RequireClient` then `!IsLinux`.
    - OutputCleanup: ExternalBuild :1555.
    - FileUseCapture: RequireLinuxIntegration :228.
    - LandGraphEvaluation.C1483: `IsWindows` :239.
  - Parameterised rows are gated before any argument is used.
  - **Hidden-test sweep:** every row method name has exactly one definition in `tests/**` ignoring case. No row class has a second declaration other than the 2 known partial method-row classes. No `[InheritsTests]` exists on row classes. No `'\u`/`'\x` literals or `#if` appear in row files.
  - **Conclusion:** no hidden Windows-running test today.
- **Pins (4):**
  - `git diff 3b544b9f7..HEAD -- tests/test-execution-policy.json` is empty, so the 45 rows and `policyHash` `a7c88a0a…6382` are unchanged from round 1.
  - `test-nightly-tests.ps1` C1410 pins 154 launches / 106343.84 s, unchanged since round 1.
  - Harness C1410 passes, and C1323_PlatformPolicy (19/0) confirms the hash is consistent.
- **Master port (5):**
  - `git merge-tree --write-tree origin/master HEAD` conflicts on the same five paths as before: `.antiphon/task-af2b78c8.md` and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (add/add), and `scripts/test-nightly-tests.ps1`, `scripts/test-rc-platform.ps1` and `tests/test-execution-policy.json` (content).
  - master `f8bddb6ef`'s copies of the three content files are byte-identical to the rc base `91b45006b`. `git merge-tree --merge-base 91b45006b origin/master HEAD` is clean (exit 0).
  - A clean port cherry-picks only this card's commits (1c35c6531, 3180cd87d, 65862ecfd, 19435beb0, plus the D-1..D-6 fix). It must keep master's own versions of the two add/add files, which this branch never edits (head blob = base blob), and must take the branch's three content files whole. That includes `policyHash a7c88a0a…`, which equals the hash of the branch JSON because master's JSON equals base.
- **rc:** `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` is clean, at rc `cf1a124035f45857e3e2bf205ab9f8b05cf7a8e4`.

## Advisories

- A-1: `ReturnTrueShape` (witness :290) counts a bare `return false;` as the Windows path, so `if (IsLinux()) return true; return false;` is admitted. `SkipOnly` (:482) admits `{ return; Skip.Test(...); }`. Either way the test returns early on Windows and passes as a no-op. No Windows coverage is lost, but the result is "passed", not "skipped". Requiring the skip to be the first non-return statement would close this.
- A-2: `Member` (:776-779) names a constructor that has a `: base(...)`/`: this(...)` initializer as `base`/`this`, so `ClassIsGated` leaves it out of the constructor set. That is fail-open only if a class has one gated constructor and another, ungated, chained constructor. No row class does today.
- A-3 (round-1 A-1 still open): only rows whose reason starts `Linux-only by construction:` are witnessed. Other `platform: windows` rows have no source-gate witness.

## Verification (all under scripts/build-slot.ps1, at 4f8ab21a7 / code 19435beb0)

- **Mutations:** leases 77b554e3, 32443e61 and b8d64af9, 33 harness runs. Results are above.
- **Harness (lease 0652ca50):**

  | Case | Passed | Failed |
  |---|---|---|
  | C1323_PlatformPolicy | 19 | 0 |
  | C1323_PlatformExecution | 12 | 0 |
  | C1415_SkipExclusions | 10 | 0 |
  | LinuxSessionRunnerAdmission | 13 | 0 |
  | LinuxOnlyWindowsExclusions | 8 | 0 |
  | C1404 | 1 | 0 |
  | C1577 | 1 | 0 |
  | C1584 | 1 | 0 |
  | C1410 | 1 | 0 |
  | G2865 | 1 | 0 |

- **Build (lease b8b620a9):** `dotnet build tests/Antiphon.Tests -c Debug UseAppHost=false OutputPath=bin-rev9567/` finished with 0 errors.
- **TUnit (lease f54d80c9, `dotnet exec`), 0 failed, 0 skipped:**
  - `(ReleaseGatePlatformTests*)|(RcRosterRepairTests*)|(NightlyScriptsTests*)`: 12/12. The TRX names are LinuxOnlyWindowsExclusions, C1415_SkipExclusions, C1323_PlatformPolicy and C1323_PlatformExecution; Mirror, Explicit and Costs; and 5 NightlyScripts cases.
  - `WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates`: 1/1.
- `check-evidence-diff.ps1 -BaseRef 91b45006b -HeadRef HEAD`: 6 commits, 0 violations.
- All 33 `bin-rev9567` directories were removed by literal path. The scratch tree stays in the session scratchpad.
- No checkpoint-tool receipts were made, so `reviewedSourceClean` is false. PCs stay pending.
