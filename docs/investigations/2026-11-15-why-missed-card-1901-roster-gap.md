# CARD-1901 round 3: native Linux gate roster gap

Closed 32 source-inventory gaps (four `Existing`, 28 `NativeSourceOnly`) and added
one impacted-selector kind for the exact portable closure method. The closure,
required guard union, all 13 apply-gate methods and the wrapper renewal witness
passed. Full Remote verification remains incomplete under the brief's 45-minute
limit; this is a repair handoff, not a landable or Full/Clean verdict.

Base: `e791df1b7970038b167488f187c77fde01a7a004`. The exact existing test
`/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates`
reproduced the review blocker: 1 executed, 0 passed, 1 failed, 0 skipped (25.295s).
The assertion names `c1210-unrostered-gate:
Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105ResumePostRemovalCensusCaseAsync`.
The leased base build passed, with 0 errors and 2106 warnings, in 237.54s.
Raw evidence is ignored under `.antiphon/c1901-r3-base-{build,red}.log` and
`.antiphon/c1901-r3-base-red/run.trx`.

The round-2 Code plan's guard selectors CP-10a through CP-10d did not select
`WindowsUnitPlatformAdmissionTests`; the Code report contains no execution of that
class. Its separately recorded incomplete raw-string OS lexer check did not cover
this source-closure invariant. Round-2 Review selected the admission class and
found the gap. The method was introduced by the round-2 census-method split in
`5e7b5d6b3bad77ca49ccbe3d79aa15aaf1f04e2b`.

Every new method calling `C1008HostFixture.RequireNativeLinux()` needs a source
roster entry, including private helpers. The three public census callers already
guard before invoking this helper. The smaller accounting repair is one
`WindowsUnitSkipRoster.Existing` row with `ExistingReason`, matching the existing
helper rows. `Existing` is source-closure accounting, outside the child runtime
roster, so the helper adds no runtime test or skip count. No gate, assertion,
timeout or production behavior needs to change.

The cheapest selector guard is one `impacted-classes.json` kind selecting
`WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates`
for `RemoteScriptContractTests.cs`, the
`RollingVolumeRecycleScriptTests.cs` file that actually declares
`C1008HostFixture`, and the roster itself. A standalone `C1008HostFixture.cs` does
not exist. This catches the omitted guard whenever those files change without
adding another test implementation.

Initial `/proc/loadavg`: `2.35 2.82 4.83 2/3065 4195`; after the base build:
`12.73 10.19 7.49 3/2918 6771`. These are sparse contended-host samples, not p95.
Native Windows execution remains separately owed. Mutation is waived by the
operator (2026-11-12). The four known master composition conflicts belong to Merge.

Verification after the repair is recorded below when complete.

The first repaired-source closure run (`f300cf5ace78bc0515e15d2ab0e2cad52120132f`)
still failed 0/1: its next missing identity was
`C1364BoundLandWaitProofTests.C1364_Host_recycle_post_removal_reports_waited_resume`.
A read-only Roslyn scan found 31 additional unrostered direct gate callers:
three split C1364 methods and 28 native-gate methods/helpers in the wrapper,
content, receipt and publication fixtures. The original closure assertion stops
at the first missing call. The repair therefore includes all these source-only
entries: the three C1008 helper callers belong in `Existing`; the other 28 use
`NativeLinuxGate.Require` and belong in `NativeSourceOnly`, with their existing
literal native-platform reasons. Neither collection changes the frozen Windows
child selection. Runtime test code and publication fixture behavior stay unchanged.

The first evidence wrapper was stopped before launching a test when PowerShell
expanded the filter as a filesystem glob. It produced no test result. The
replacement uses a literal ProcessStartInfo argument list; each row takes its own
build-slot lease and a nine-minute execution bound, and retains load/SHA/filter,
TRX and opt-in phase records under `.antiphon/c1901-r3-*`.

At `c7b7ae3a991652a4c831f6a49b7975157da5e61a`, the exact closure passed 1/1.
The required guard union passed, with its expected Windows-placement skip. The
extra whole `ImpactedClassesSelectorTests` run caught an introduced selector-row
error: `C1819_linux_kind_classes_do_not_skip_off_windows` rejected selecting the
entire admission class for a Linux row, because that class also contains the
Windows-only runtime audit. The combined run was 156 total, 154 passed, 1 failed,
1 skipped, 426.71s including admission and wrapper overhead. The row is therefore
narrowed to the exact platform-neutral closure method. This keeps a single kind,
catches the missing inventory entry on either OS, and avoids selecting the
unrelated Windows runtime witness. No assertion or test implementation changes.

## Final verification and handoff

Compiled C# source: `c7b7ae3a991652a4c831f6a49b7975157da5e61a`.
Final executable-input source: `7061c76ec9dde3c4fe1286f463ce76d8743ddee0`.
Their diff is only this investigation and `tests/impacted-classes.json`; all C#
build inputs are identical. The JSON-only correction therefore reused that build.
The later report commit changes only this Markdown file.

| Selection | Source | Total / passed / failed / skipped | Row wall seconds |
|---|---|---|---|
| Exact base roster closure | e791df1b | 1 / 0 / 1 / 0 | 29 leased; test 25.295 |
| Exact closure after first helper row | f300cf5a | 1 / 0 / 1 / 0 | 21.57 |
| Exact complete roster closure | c7b7ae3a | 1 / 1 / 0 / 0 | 24.22 |
| Required eleven-class guard union | c7b7ae3a | 114 / 113 / 0 / 1 | part of 426.65 combined row |
| Extra whole impacted-selector class | c7b7ae3a | 42 / 41 / 1 / 0 | same combined row, not another run |
| Exact formerly failing selector method after JSON fix | 7061c76e | 1 / 1 / 0 / 0 | 13.12 |
| Apply gate, CP-172 | 7061c76e | 1 / 1 / 0 / 0 | 92.42 |
| Apply gate, CP-173 | 7061c76e | 1 / 1 / 0 / 0 | 110.98 |
| Apply gate, CP-174 | 7061c76e | 1 / 1 / 0 / 0 | 74.18 |
| Apply gate, CP-175 | 7061c76e | 10 / 10 / 0 / 0 | 102.76 |
| Wrapper, Retire_temp_real_wrapper_renews_each_effect | 7061c76e | 1 / 1 / 0 / 0 | 256.14 |

The admission class within the required union was 6 passed, 1 skipped. The skip is
`C1210_Windows_linux_only_outcomes_are_exact`, requiring Windows placement.
C1729 retained `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.
The introduced selector failure was corrected and its exact method passed; the
historical red run remains red. No failing test or assertion was weakened.

Three Antiphon.Tests builds passed with 0 errors and 2106 warnings each: base
237.54s, first repair 237.62s, complete roster 211.28s. All used native default
outputs and `UseAppHost=false`, through separate granted build-slot leases.
The regeneration driver was built first (8.18s, 0 warnings/errors). Both initial
regenerations and both final-source regenerations printed
`derived-impact-index unchanged`; no generated index change or index commit was
needed. The final pair held separate leases for 28s each. No driver ran unleased.
Every executed test row was serial and below ten minutes. Rows retain fresh TRX,
literal filters, source SHA, load, durations and available opt-in phase JSONL in
ignored `.antiphon/c1901-r3-*`; uninstrumented phase attribution is unknown.

RECEIPT GAP: `RemoteScriptContractTests` was not executed in this task, including
the three census entry points. The next census row was explicitly recorded as
`not-run-timebox` before launch; the remaining two were never launched. Its
runtime test file is unchanged from the required base. A Roslyn inventory found
229 declared test methods covered by the plan's Remote selectors. The 54 complete
historical split rows total 5222.46s (87.04 minutes, including recorded overhead;
not p95). The brief's 45-minute task cap cannot accommodate that rerun alongside
the required repair/build/guard work. The optional budget question received no
extension before this handoff. No publication class rerun was triggered: neither
its runtime code nor inputs it reads changed.

Merge must retain the incomplete verification gate and execute the full Remote
class serially before landing. Use the committed CARD-1901 plan's CP-39 family,
replacing CP-39.13 with CP-130..137, CP-39.15 with CP-138..147, and CP-39.18 with
CP-162..171 to retain bounded rows. The separate Windows placement remains owed.
Proceeding under the caller's standing test-only repair authority; no additional
Review was requested. Mutation remains waived, not executed or credited.

Freshly fetched master `d100f8fa2c679a5c74b6b235087308776a8f5d50` retained the
brief's three exact stored pins. This repair changes none of their inputs, so no
re-pin was made. Merge-tree still reports the assigned four conflicts in
`tests/Shared/TestTiming.cs`, `tests/test-execution-policy.json`,
`scripts/test-live-lane-run.ps1` and `tests/derived-impact-index.json`; no conflict
was resolved here. Evidence-diff and final remote-tip witnesses are reported with
the final report commit. The parent chain contains
`e791df1b7970038b167488f187c77fde01a7a004`.
