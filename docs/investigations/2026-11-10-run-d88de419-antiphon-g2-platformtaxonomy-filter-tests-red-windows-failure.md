# Run d88de419, antiphon-all-group-2: PlatformTaxonomy filter witnesses red on Windows

Run `d88de419` (Windows Full, desktop), rc/2026-11-16 at
`6f8741d872d63507827014f972ccf71196722d4a`. Chunk `antiphon-all-group-2`
(not `session-runner-all-group-2`). Task `a24e118f`, read-only on the run.

## Verdict

Deterministic, not timing and not an OS difference. The three CARD-1832 S1
witnesses end with an assertion on the environment variable
`ANTIPHON_PLATFORM_FILTER`, which only the CARD-1832 checkpoint rows CP-1
(`include`) and CP-2 (`exclude`) set. No ordinary suite run (Windows Full,
Linux nightly, a developer `dotnet run`) sets it, so the variable is `null` and
every witness fails on every host. It surfaced first on Windows because run
d88de419 is the first whole-assembly run of the cut that contains the
witnesses (`387397c60`, cherry-picked from `b343feb9c`).

The taxonomy attributes themselves are fine: every structural assertion that
precedes the environment line passed, and all seven `PlatformTaxonomyDerivationTests`
(the `acaa9d9b1` derivation) passed in the same chunk.

Cause class: test assumption (checkpoint-only environment), not a
taxonomy-filter defect and not a Windows path/OS assumption.

## Sources (read-only)

`C:\Antiphon\releases\candidates\rc-2026-11-16-6f8741d872d6\logs\2026-11-11-1142-d88de419\`

| File | Size |
|---|---:|
| `antiphon-all-group-2.trx` | 424739 |
| `antiphon-all-group-2-tests.log` | 6785 |
| `antiphon-all-group-2.timing.json` | 2628 |
| `antiphon-all-group-2-execution\` (TUnit diag dir) | - |

Chunk summary (tests.log): TUnit 1.44.0, Windows 10.0.19045 win-x64, .NET 9.0.22,
MTP 2.2.2. total 163, failed 3, succeeded 160, skipped 0, duration 1m 58s.
timing.json: started 13:17:23Z, completed 13:19:22Z, build 710.7 s, fixture
setup 5.3 s, p50 0.003 s, p95 2.459 s, max 22.5 s.

## The three failures

All three are `TUnit.Engine.Exceptions.TestFailedException` wrapping a
Shouldly `ShouldAssertException`, at the last line of the test body, started
2026-11-11 13:17:43Z.

### 1. `PlatformTaxonomyFilterTests.Unannotated_sibling_is_excluded_from_the_linux_only_selection` (51 ms)

```
ShouldAssertException: Environment.GetEnvironmentVariable("ANTIPHON_PLATFORM_FILTER")
    should be
"exclude"
    but was
null
  at Antiphon.Tests.PlatformTaxonomyFilterTests.Unannotated_sibling_is_excluded_from_the_linux_only_selection()
     in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\PlatformTaxonomyFilterTests.cs:37
  at System.RuntimeMethodHandle.InvokeMethod(...)
  at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(...)
```

Expected `"exclude"`, actual `null`. Line 36 (`GetCustomAttributes(PropertyAttribute)`
is empty) passed. Stdout: only the `ANTIPHON_TEST_PHASE_ID` marker.

### 2. `PlatformTaxonomyFilterTests.LinuxOnly_is_a_property_not_a_skip` (54 ms)

```
ShouldAssertException: Environment.GetEnvironmentVariable("ANTIPHON_PLATFORM_FILTER")
    should be
"include"
    but was
null
  at Antiphon.Tests.PlatformTaxonomyFilterTests.LinuxOnly_is_a_property_not_a_skip()
     in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\PlatformTaxonomyFilterTests.cs:28
```

Expected `"include"`, actual `null`. Lines 19-27 (four `AssertPlatform` checks:
`PropertyAttribute` not `SkipAttribute`, `Name == "Platform"`, the four values,
the reasons; nine `ArgumentException` constructor guards) all passed.

### 3. `PlatformTaxonomyClassFilterTests.Class_shorthand_is_selected_with_the_linux_only_property` (86 ms)

```
ShouldAssertException: Environment.GetEnvironmentVariable("ANTIPHON_PLATFORM_FILTER")
    should be
"include"
    but was
null
  at Antiphon.Tests.PlatformTaxonomyClassFilterTests.Class_shorthand_is_selected_with_the_linux_only_property()
     in C:\Antiphon\releases\checkout\tests\Antiphon.Tests\PlatformTaxonomyClassFilterTests.cs:28
```

Expected `"include"`, actual `null`. Lines 22-27 (exactly one class
`LinuxOnly` attribute, a `PropertyAttribute`, not a `SkipAttribute`,
`Platform=LinuxOnly`, reason `class shorthand`) passed.

## Grouping and bounds

| Class | Methods | Passed | Failed |
|---|---:|---:|---:|
| `PlatformTaxonomyFilterTests` | 2 | 0 | 2 |
| `PlatformTaxonomyClassFilterTests` | 1 | 0 | 1 |
| `PlatformTaxonomyDerivationTests` (same feature, `acaa9d9b1`) | 7 | 7 | 0 |

Passing siblings (all 13:17:43Z, sub-millisecond):
`Every_method_linux_only_derives_linux_only`,
`Every_method_windows_only_derives_windows_only`,
`Mixed_linux_only_and_unannotated_derives_both_with_has_linux_specifics`,
`Mixed_windows_declarations_derive_both_with_has_windows_specifics`,
`All_has_linux_specifics_is_not_linux_only`,
`Unannotated_replay_rows_derive_both_with_no_specifics`,
`Derive_is_pure_and_order_independent`.

The two filter-witness classes have no passing method, so the siblings that
bound the cause are (a) the earlier assertions inside each failing body, all
green, and (b) the derivation class, all green. The only failing line in each
body is the environment read.

Running at the same time (13:17:42-13:17:44Z, ~130 results): the
`AgentProfile*` PostgreSql constraint tests, `A_non_projection_warning_*`,
`Dispatch_base_detail_*`, the `ResponseCache` normalisation tests, the
`C1807/C1810/C1816` limiter-census tests, `Git_lane_limit_*`,
`Checkpoint_tool_lane_limit_*`, the quiet-deadline wait tests, and
`Two_entry_point_invocations_racing_on_startup_both_serve`. None of them reads
or writes `ANTIPHON_PLATFORM_FILTER`; `rg ANTIPHON_PLATFORM_FILTER` over the
tree finds only the two witness files and the CARD-1832 plan. No stderr, no
sharing violation, no poll or timeout in the log.

## Deterministic, not timing

- The assertion is on a returned value (`Environment.GetEnvironmentVariable`),
  not a poll, deadline, file lock or process.
- Durations are 51-86 ms in a chunk whose p50 is 3 ms; the body is pure
  reflection plus one env read. The first-call Shouldly expression rendering
  accounts for the time.
- Reproduced on this desktop at the cut, unset environment, before the fix:
  see Verification.

## Why it is not an OS difference or a taxonomy defect

- Nothing in the witness bodies depends on the OS. The same `null` would
  fail on Linux in any whole-assembly run.
- The CARD-1832 plan (`docs/superpowers/plans/2026-11-09-card-1832-platform-taxonomy.md`,
  V-2, CP-1, CP-2) designs these bodies as filter witnesses: CP-1 runs
  `[Platform=LinuxOnly]` with `ANTIPHON_PLATFORM_FILTER=include`; CP-2 runs
  `[Platform!=LinuxOnly]` with `exclude`. The env assertion makes the wrong
  method fail if the tree filter mis-selects. The plan never said what the
  bodies do when no checkpoint lane is active.
- Separately, and expected at this cut: the two `LinuxOnly`-declared witnesses
  were executed on the Windows lane. Only S1 (`387397c60`, `50c2a29b9`,
  `053987a72`) and S2 (`acaa9d9b1`) are in rc/2026-11-16. The Windows lane
  `[Platform!=LinuxOnly]` suffix is S5 (`Get-NightlyNativeExecutionArguments`,
  V-6, CP-7), not yet landed. Once S5 lands, the Windows lane will not select
  those two methods at all; the unannotated sibling stays on both lanes and
  would still have failed without this fix.

## Fix (this branch)

`tests/Antiphon.Tests/PlatformTaxonomyFilterTests.cs` and
`tests/Antiphon.Tests/PlatformTaxonomyClassFilterTests.cs`: the lane assertion
reads `ANTIPHON_PLATFORM_FILTER` and enforces it only when a checkpoint lane
set it. Unset or empty means an ordinary suite run, which has no lane to
witness; the structural attribute assertions still run unconditionally. Any
other value (a typo, the wrong lane) still fails.

Semantics kept:

- CP-1 (`include`, `[Platform=LinuxOnly]`): the two `LinuxOnly` bodies pass;
  if the filter also selected the unannotated sibling (the V-2 mutant "drop
  the Platform value"), that sibling still fails, because `include != exclude`.
- CP-2 (`exclude`, `[Platform!=LinuxOnly]`): the sibling passes; a `LinuxOnly`
  body selected by mistake still fails.
- No attribute, `Derive`, lane filter or policy file changed. No new type, so
  `tests/derived-impact-index.json` is unchanged.

## Why missed

1. The witness was proved only under its own checkpoint rows, each of which
   sets the variable. The plan's Checkpoints table carries `Environment` per
   row, and no row, review or verification step ran the two classes with the
   environment unset, which is how every Full, nightly and developer run
   executes them.
2. The commits were authored and checkpointed on server2 by filter; no
   whole-assembly `Antiphon.Tests` run of the CARD-1832 cherry-picks happened
   before rc/2026-11-16 was cut, and the rc Windows Full run is the first
   unfiltered execution. Minimal-testing policy relies on the rc roster for
   exactly this, and it caught it.
3. Review checked that V-2 goes red under the wrong filter (the designed
   red) but not that the witness is green outside any filter lane. A witness
   whose pass depends on harness environment needs a "no lane" row: either a
   test that the body is green with the variable unset, or an explicit
   execution-policy exclusion with an owner.

Recommendation for the plan owner: add a CP row (or a note on CP-1/CP-2)
that runs `PlatformTaxonomyFilterTests*|PlatformTaxonomyClassFilterTests*`
with the variable unset and expects 3 executed, 0 failed.

## Verification

Native Windows (this desktop, Windows 10 19045, .NET 9), worktree default
`bin/Debug/net9.0`, each run through `scripts/build-slot.ps1`. Filter `F` is
`/*/*/(PlatformTaxonomyFilterTests*)|(PlatformTaxonomyClassFilterTests*)/*`.

| Row | Commit | Env | Filter | Result |
|---|---|---|---|---|
| red | `6f8741d87` (the cut) | unset | `F` | 3 total, 3 failed: same `should be "include"/"exclude" but was null` as the run |
| unset | `a5632cc19` (fix) | unset | `F` | 3 total, 3 passed, 0 skipped |
| CP-1 | `a5632cc19` | `include` | `F` + `[Platform=LinuxOnly]` | 2 total, 2 passed |
| CP-2 | `a5632cc19` | `exclude` | `/*/*/PlatformTaxonomyFilterTests*/*[Platform!=LinuxOnly]` | 1 total, 1 passed |
| V-2 wrong selection | `a5632cc19` | `include` | `F` (no Platform clause) | 3 total, 1 failed: `Unannotated_sibling_...` `should be "exclude" but was "include"` (the designed red is kept) |

Not run: the whole `Antiphon.Tests` assembly. Neither file adds a type or a
cross-class reference (the helper is private in each class), so
`tests/derived-impact-index.json` and `DerivedImpactIndexTests` are not
affected. Build-slot waits on this desktop were 6-42 min each (memory floor,
the live run).
