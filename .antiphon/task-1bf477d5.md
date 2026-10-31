# Final Review 1bf477d5: failed-first stopped-run seed builder and ingress

Subject: origin/feat/card-task-f6aabaef @ a00f1b46753542a8721a111fb4c26ed1d542cf2a (base e805b7848, chain 8d80dbe9).
Reviewer branch: feat/card-task-1bf477d5 (this report only). Host: server2/Linux, pwsh 7, load average 21-26 from other tenants.

Verdict: the ordering logic and ingress are sound. Across 440 random seeds (300 good, 140 bad) the output was always a permutation, the partitions were correct, every bad seed fell back with a reason, and the verdict was preserved. But the builder cannot produce a seed from the standard `nightly-run.ps1` log layout (D-1), and it is slow at real scale (D-2). As shipped, the honest answer to the operator question is not yet "yes". Route: code.

## Defects

D-1 (High, fails closed). The builder looks for `groups-planned-*.json` only under -RunDirectory. Owner of the landing: f6aabaef Code.
- Where: scripts/lib/nightly-stopped-run-seed.ps1:89-98 (walk and plan discovery), with :148/:159 (timing/TRX taken from the root).
- Failure: `nightly-run.ps1` started without -TimingRoot or -SelectionAttemptPath has exactly the shape of the f6 operator launch command. Then scripts/lib/nightly-run-impl.ps1:158-162 sets TimingRoot = `<LogRoot>\timing-<fullRunId>`, and :618-621 forwards it to nightly-tests (nightly-tests-impl.ps1:1514-1515). Planned manifests are written there (test-phase-evidence.ps1:204-210, :1291-1298). That directory is a sibling of the run directory `<LogRoot>\<yyyy-MM-dd-HHmm>-<runId8>` (nightly-run-impl.ps1:24-29, :573-574). TRX and timing sidecars stay in the run directory.
- Executable witness: the f6 fixture was rearranged into that layout, with plans in `logs/timing-<runId>` and no owner.json (the real run writes no owner json). Exporting from the run dir gives `seed-plan-missing`. Exporting from `logs/` or from the timing dir gives `seed-source-identity-or-time-missing`. A 6-chunk synthetic run with that layout reproduced the same results. So the operator's export command for d47848cc would exit 2, with no seed.
- Why it matters: this is the operator flow the change exists for. It is not a false green: export refuses, the launch then throws, and the new run would use canonical order if launched without the seed.
- Fix: find the planned manifests by reading the run's `summary.json` timingRoot read-only. Rebind it the way S1 does with Resolve-NightlyHistoryArtifact. A bounded alternative is the sibling `timing-<runId>` named by the plan's runId, or an explicit -TimingDirectory treated as a second read-only root covered by the same snapshot and containment checks. Add a sibling-layout fixture and a witness (BuilderReadOnly or a new case).
- Caveat: this was derived from code and synthetic fixtures. The desktop run was not read. Confirm with `Test-Path 'C:\Antiphon\releases\candidates\rc-2026-10-27-cde8def41638\logs\timing-*'`.

D-2 (Medium, performance). The builder takes minutes, not seconds, at real directory size.
- Where: Assert-NightlyHistoryNoLink (scripts/lib/nightly-failure-history.ps1:8-20) calls Get-Item on every ancestor up to the root. It runs once per walked item (nightly-stopped-run-seed.ps1:92) and again per read file (:80).
- Measured on server2: a synthetic 741-file directory (120 chunks, 105 TRX, about 2,940 results, plus a 235 MB sparse summary.json that is correctly never read) exported in 197.1 s. 40 chunks took 74.3 s. Profile: Assert-NightlyHistoryNoLink cost 257 ms per call, against 0.65 ms per SHA256 of a file. The cost is linear in files, so the real directory (about 840 TRX plus logs, json, timing and diagnostic directories) projects to roughly 20-25 min on this host. Windows was not measured; Get-Item is cheaper there, but the walk is still O(files x depth).
- Fix: check the root's ancestors once. During the walk, use each enumerated item's own Attributes/LinkType. In Read-SeedFile, check only the leaf, since it is already inside a link-free walked tree. Add a timing bound to a fixture (for example, an 800-file synthetic directory under a stated budget).

D-3 (Low, fails closed). One unreadable or changing input aborts the whole export instead of marking that chunk unfinished, which brief item (2) requires.
- Where: the timing read at nightly-stopped-run-seed.ps1:149-151 is outside the per-chunk try. Read-SeedFile (:81-84) and the final recheck (:196) throw `seed-source-changed`.
- Witness: with a chmod-000 timing.json, export threw `Access to the path ... is denied`. With a writer appending to one TRX, 2 of 2 exports were refused with `seed-source-changed`. The same happens for a Windows sharing violation from an indexer or AV scan. A chmod-000 TRX was handled correctly (started=True, completed=False).
- Fix: put the timing (and owner) reads inside the per-chunk tolerance. Mark a chunk whose TRX or timing digest changed, or could not be read, as started and unfinished, keep its evidence entry out of the hash, and never take failure identities from it.

## Advisories (no Review rerun needed for these alone)

- A-1 Same-candidate seeds also require -AcceptCrossCandidateSeed (nightly-failure-history.ps1:486). The brief says none is needed. This is stricter and safe: without the switch the run falls back with a reason.
- A-2 Hash portability between PS5.1 and PS7 (plausible, not executed: there is no Windows PowerShell here). Get-NightlyStoppedSeedHash (nightly-stopped-run-seed.ps1:17) hashes ConvertTo-Json output. Windows PowerShell 5.1 escapes `< > & '` as `\u003c` and similar; PS7 does not. A seed whose payload contains those characters (for example method names with arguments), exported in one edition and read in the other, fails the hash and falls back. That is safe. The operator commands use pwsh at both ends. Fix: hash the canonical UTF-8 bytes of the fields, not ConvertTo-Json text.
- A-3 Order resolution is O(entries x classes x seed chunks) (nightly-stopped-run-seed.ps1:219-226). At 840 chunks / 2,520 classes it took 54.7 s for order plus schedule (ingress 6.1 s, resolve 1.9 s) at qualification dispatch on loaded server2. Index the seed chunks by (suite, class) once.
- A-4 Surviving reviewer mutant M9: dropping the ResultSummary `$closed` guard (:172-173) survives BuilderReadOnly. Torn TRX files are protected only by the XML parse failure. Nothing witnesses a well-formed TRX that has no ResultSummary.
- A-5 Inherited test-infrastructure flake, outside this diff; needs a card. tests/Antiphon.Tests/Scripts/LinuxScriptHarnessProcess.cs:135-141 ConfirmDeadAsync scans every /proc pid and catches only FileNotFound/DirectoryNotFound. Reading /proc/<pid>/stat of an unrelated exiting process throws IOException "No such process". That made CP-1 red here after the harness itself passed (`PASS SS BuilderReadOnly.contract`, harness exit 0). It is unchanged on origin/master and on rc/2026-10-27.
- A-6 The containment check is textual (Test-NightlyHistoryUnderRoot). On Windows an 8.3 short name or an alternate UNC spelling of the run directory given as -OutputPath gets past `seed-output-inside-source`. This is an operator error only, and the operator command uses `ordering-seeds\`.
- A-7 Ordering expectation: never-started and new chunks have no timestamp. They sort first in the middle block and tie on canonical ordinal across suites. E2E leads only if its chunks failed or come first in canonical order; "start with e2e" is not guaranteed.

## Evidence

Property probe (pure ordering plus ingress through the real Initialize-NightlyDispatchHistory, Resolve-NightlyPriorFailures, New-NightlyDispatchSchedule and Invoke-NightlyPreparedSchedule). Interpreter-only reviewer diagnostic on a git-archive copy of a00f1b46.
- 300 random good seeds (seeds 91000..91299, 7 world kinds) over random new plans. The kinds were: regrouped chunks; a class split across two chunks; removed classes (unresolvable); duplicate identities; all-failed; unfinished-heavy (stopped run); empty failure list; suites new to the plan.
- Assertions: a permutation every time. The first block is exactly the chunks that contain resolvable failed classes, checked by an independent oracle, including the priorFailure flags. Block order is failed, then not-passed (unfinished, never-started or new), then passed. Within each block, unknown timestamps come first, then oldest first, with ties broken by canonical position. Execution through the real prepared-schedule boundary ran each chunk exactly once, completed each suite once, and kept any injected red chunk red in the verdict.
- 140 bad seeds, 14 kinds: no-ack, wrong candidate, tampered, stale created, stale observed, future, malformed, wrong seedKind, digest mismatch, empty chunks, bad outcome, unmapped plan, traversal chunk name, failed class not a member. Each returned exactly canonical order with a stated reason, no priorFailure flags, no exception, and an intact verdict.
- Result: 0 failures (300 good, 247 with a non-empty failed block, 284 unresolved classes skipped while the rest stayed mapped, 46 with an empty failure list). The probe goes red on mutants: M1 66 failures, M7 31 failures.

Builder read-only check: complete file bytes, file mtimes and directory mtimes were identical before and after export. Outputs inside the source were refused (`../run/x`, `sub/x`, `dir/`), and an output through a symlinked parent was refused (`evidence-link`); nothing was created in the source by any refused attempt. A traversal chunk name (`..\..\..\evil`) was refused (`seed-plan-identity`). Timeout, Error, Aborted and Failed outcomes were all retained as failures (TrxReport.cs:55 rule).

Reviewer mutants: scratch copy, the harness case through pwsh, all runs inside build-slot leases (2f2113d5, 61923e86, 884d7270). Unmutated baseline: 7 of 7 green. Restored afterwards: 7 of 7 green.

| Mutant | Case | Result |
|---|---|---|
| M1 drop failed-first priority | FailedFirstMapping | killed (and probe 66 failures) |
| M2 ignore acknowledgement | AcknowledgementRequired | killed |
| M3 accept hash mismatch | HashMustVerify | killed |
| M4a write a new file into input | BuilderReadOnly | killed |
| M4b rewrite same bytes in input (mtime only) | BuilderReadOnly | killed |
| M5 drop a chunk | ScheduleConservesAllChunks | killed (dispatch-not-permutation) |
| M6 Timeout counted as pass | TimeoutIsFailure | killed |
| M7 swap never-started and passed | FailedFirstMapping | killed (and probe 31 failures) |
| M8 trust another candidate without matching ack | AcknowledgementRequired | killed |
| M9 drop ResultSummary closed guard | BuilderReadOnly | SURVIVED (A-4). The first attempt's red was contaminated by a stale results directory; a clean rerun passed. |
| M10 bypass 14-day guard | InvalidSeedFallsBack | killed |
| M11 case-insensitive class merge | FailedFirstMapping | killed |

PowerShell 5.1: all 9 changed .ps1 files are ASCII-only. The added lines contain no `??`, `?.`, ternary, `-Parallel`, `&&`, `||`, `-AsHashtable` or `-NoEnumerate`; `::new()` is PS5-safe. The only risk found is A-2. Name parsing: the stem `<suite>-<chunk>` matches antiphon-all-group-N, session-runner-, pty-host-, agents-pty-all-group-N, e2e-all-split-N and antiphon-all-profile-N. The chunk regex `^[A-Za-z0-9_-]+$` and the orphan regex at :126 accept them; summary.json is never opened.

Checkpoint sweep, run 20261031-115039-a6da (plan .antiphon/rv-1bf477d5-plan.md, 23 rows, one isolated bin-rv1bf4-a build, --serial, --row-timeout 20m --total-timeout 80m --hard-wall-timeout 85m): see "Sweep status" below.

Other gates:
- Land graph `-Check`, through the build-slot with a fresh Release audit build bin-rv1bf4-audit (0 errors / 0 warnings): approved, phase=pass2, digest E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806, count=69, canary removed. There is no -Update and no change to definition files.
- `check-evidence-diff.ps1` base..HEAD: 19 commits, 2 entries, 0 violations. From the rc merge-base 3fb3c113 to HEAD: 55 commits, 6 entries, 0 violations.
- merge-tree onto origin/rc/2026-10-27 fc064a682: clean, tree ca0a80bfe2cb75f392ba2afc91dcb2fcbcb300bf. The base e805b7848 is not on rc, so an rc merge also carries the unlanded chain: the range is 3fb3c113..a00f1b46, 55 commits. origin/master 2051d3271: 48 conflict paths, the same count as the base (inherited).

## Sweep status (ordinaryScopeCompleted: Interim)

Run 20261031-115039-a6da at a00f1b46 (source clean, one build, build=ok). It ended RED exit=5 `hard-wall-deadline-exceeded` after an 85m01s wall: 2524 s of execution and 2577 s of admission-only slot waiting, on a saturated host. `validate` gives CHECKPOINT SOURCE INVALID reason=report_failed, so no row receipt is credited.

| CP | Row | Executed | Passed | Failed |
|---|---|---:|---:|---:|
| CP-1 | seed BuilderReadOnly | 1 | 0 | 1. The harness passed (`PASS SS BuilderReadOnly.contract`, exit 0); ScriptHarness cleanup then threw `death confirmation: No such process '/proc/26238/stat'` (A-5, inherited). Passed in the reviewer scratch harness runs, both baseline and restored. |
| CP-2..CP-8 | seed FailedFirstMapping, AcknowledgementRequired, HashMustVerify, TimeoutIsFailure, ScheduleConservesAllChunks, InvalidSeedFallsBack, PublicHopsForwardAcknowledgement | 7 | 7 | 0 |
| CP-9..CP-14 | chain CP-3..CP-8 (GlobalReplayPriority, FullBytesAndCoverageConserved, CapFitsAdmittedPlannedCost, ActualLoopFollowsSchedule, JournalCustodySurvivesInterleaving, LedgerAndReceiptBytesAreUnchanged) | 6 | 6 | 0 |
| CP-15 | RcFailureHistoryTests/* | 2 | 2 | 0 |
| CP-16 | RcFullWatchdogHarnessTests/* | 2 | 2 | 0 |
| CP-17 | C1514LandingSplitBudgetTests/* (five census cases) | 5 | 5 | 0 |
| CP-18 | RcRosterRepairTests/* | 3 | 3 | 0 |
| CP-19 | ScriptHarnessProcessContractTests/* | 30 | 30 | 0 |
| CP-20 | ProcessSpawnLimitTests/* | not run | | | still waiting for a slot (355 s) at the hard wall |
| CP-21..CP-23 | SlowTestTripwireTests, TestLaneCategoryGuardTests, TestClassificationPolicyTests | not run | | | skipped at the deadline |

Not run, with reasons:
- NativeLinuxPolicyWitnessTests in the four Shared-policy suites: budget exhausted. This diff changes neither tests/test-execution-policy.json nor the witness sources. Its known legacy-digest red is inherited at master 2051d3271.
- The generic "whole Unit lane": not attempted; it is about 67 min serial on server2, and the brief named its own sweep.
- Unlisted runs, all through build-slot leases: the checkpoint tool bootstrap (bin-rv1bf4-tool), the Release audit tool build (bin-rv1bf4-audit), the land `-Check`, and four interpreter-only mutant/harness leases. The property, builder and scale probes were pure pwsh scripts on a scratch copy with no dotnet driver. Cleanup: five literal bin-rv1bf4-* roots removed; generated receipts stay ignored under .antiphon/checkpoints/.

Budget: about 100 minutes against the 90 allotted. The overrun came from host saturation (load average 21-26, other tenants' CPU spinners, 43 minutes of slot admission). I let the run end at its own hard wall rather than stop it, so it would still leave a report.

Commit range to merge to rc once the defects are fixed and reviewed: origin/rc/2026-10-27 merge-base 3fb3c113e8b2554ccd5789e00375dbfd69697981 .. the fixed f6 tip. Today that is 55 commits, including the unlanded chain from e805b7848. 8d4a4d7b and 14766136 merge separately, as the brief says.
