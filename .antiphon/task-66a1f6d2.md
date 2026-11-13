# CARD-1850 S3a Code — 66a1f6d2

S3a portable Windows launch transport is implemented and green: 208 ordinary
cases passed, 0 failed, 0 skipped. It is the next dependency-ordered slice because
master at the assigned base already contains S1 and S2. Native Windows execution
remains owed by the orchestrator under S3b; this report does not qualify it.

Original Code task / landing owner: `66a1f6d2-9287-4df4-b1cc-7e859737de32`.
Branch: `feat/card-task-66a1f6d2`.
Worktree: `/work/worktrees/task-66a1f6d2` (runner mirror).
Base: `c38918255330caec88c35fda713dbddad221d5a1`.
Ordinary tested source: `bcaee078bf91e6386de376011d729afe167df358`.
The report commit is a subsequent documentation-only commit; it is not the
SHA claimed by the test receipts.

Plan: `docs/superpowers/plans/2026-11-12-card-1850-owned-process-containment-plan.md`.
Read the S2 Review at `origin/feat/card-task-1b4c87d7`, including its CLEAN source
`8b508c0d6538a5053b957795de11d6dcad930b16`, and CARD-1876 via the board script.
A1–A4 were already repaired in S2; A5's historical cost remains recorded.

## Implementation

- `src/Antiphon.Server.Git/WindowsOwnedGitLaunch.cs`: pre-creation validation,
  Windows argv quoting, command-length/NUL rejection, sorted Unicode double-NUL
  environment with distinct removed/empty values, executable resolution,
  explicit cwd, native safe handles, pipe preparation, and resource unwind.
  Parent pipe ends are noninheritable. Attribute preparation includes JOB_LIST
  and exactly three child std handles in HANDLE_LIST. Its buffers and retained
  safe-handle references live until attribute-list deletion. The job has no
  kill-on-close or breakaway configuration.
- `src/Antiphon.Server.Git/WindowsOwnedGitProcess.cs`: redirected stream
  transport (`WindowsOwnedGitStreams`), BOM-free default UTF-8 input and raw
  BaseStream access. The launch owner closes the underlying pipe resources.
- `tests/Antiphon.Server.Git.Tests/OwnedGitProcessContractTests.cs`: 31 new
  portable cases, including eight independent expected argv wire values,
  removed/empty/Unicode environment, invalid options/input, six external
  preparation failure boundaries, exact resource closure, and text/binary I/O.
  The pre-implementation witness remains buildable using reflection and also
  asserts the actual encoded output; existence alone cannot make it green.
- Plan: exact red, adjacent, guard and actual-index-consumer rows for S3a.
  `tests/derived-impact-index.json`: six regenerated lines, committed separately.

No production factory selects this Windows transport yet. No process is created
by its preparation API. S3b must use it through native creation, release the
child pipe ends/attributes promptly after creation, then retain the job and
original root handle through cleanup. The Windows root owner, actual
CreateProcessW execution, job census/termination and native witnesses remain
S3b. No caller launch site, journal, lease or public contract was migrated.
The shared root-exited AND Empty rule, Unknown verdict and missing-root
fail-closed contract remain intact; the existing owner cases passed in CP-3.
Restart: **none**.

Reference contracts checked while implementing native preparation:
[UpdateProcThreadAttribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)
and [CreatePipe](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-createpipe).
These API references are not native execution evidence.

## Commits

Each was pushed to the assigned task ref, without rebasing or amending:

| SHA | Outcome |
|---|---|
| `c0cc6f18f501fc5d10a231f54f0762b5b0db1671` | Test-only argv witness; expected 8-case red committed before implementation |
| `cd7b475e3af157384ce3186f7529ede1d11179c5` | Windows transport, resource preparation and portable contracts |
| `959789e0c13155c5028639d049e76ea0fac7273b` | Separate derived-index regeneration |
| `ed8957c02a79f7d3f6f9bb9c7dd5ab24bb7328be` | Exact row for newly selected index consumer |
| `bcaee078bf91e6386de376011d729afe167df358` | Supported manifest build-reference syntax; final ordinary tested SHA |

## Closed checkpoint outcomes

| ID | Outcome | Executed / passed / failed / skipped |
|---|---|---|
| CP-S3-R | Expected baseline assertion red, before implementation | 8 / 0 / 8 / 0 |
| CP-3 | Green | 61 / 61 / 0 / 0 |
| CP-S3-1 | Green | 14 / 14 / 0 / 0 |
| CP-S3-2 | Green | 129 / 129 / 0 / 0 |
| CP-S3-3 | Green | 4 / 4 / 0 / 0 |

The red failed at the expected missing transport assertion, not at build,
fixture setup or zero discovery. Its eight cases ran green within CP-3. No
unchanged proof repetition was needed after green. No timeouts or existing
assertions were widened or loosened. No deliberate mutants were run.

Fresh TRX inspection verified nonzero execution of every intended class:
OwnedGitProcessContractTests 61; GitIndexLockTests 11; ServerGitAssemblyTests 3;
ServerGitProcessTests 23; TestClassificationPolicyTests 42;
TestClassificationGuardTests 1; SlowTestRegistryMergeTests 5;
SlowTestTripwireTests 2; NativeLinuxPolicyWitnessTests 3;
DerivedImpactIndexTests 27; MechanicalPcContractTests 11;
TaskPlatformGuidanceTests 5; C1729LayeringGuardTests 9;
TestLaneCategoryGuardTests 1; ReleaseImpactEnvelopeTests 4.
All 31 new Windows-named cases were inspected: argv 8, environment 1, invalid
transport 7, unsupported options 7, preparation failures 6, prepared I/O 1,
raw arguments/broken-input disposal 1. Source suffix audit found no additional
classes selected by these prefix filters.

Both green report JSON receipts passed `scripts/validate-checkpoint-receipt.ps1`
against the full ordinary tested SHA. Each run received committed HEAD as its
expected source. Every accepted row has `dirty=0`, `sourceState=clean`, and
`buildSource=verified`; row/build source provenance was verified by the tool.
The unedited CHECKPOINT lines below come directly from each run's `report.md`.
TRX, JSON, logs and phase evidence remain ignored under their original run roots.

```text
CHECKPOINT CP-S3-R commit=c0cc6f18f501fc5d10a231f54f0762b5b0db1671 build=ok filter=/*/*/OwnedGitProcessContractTests/Windows_arguments_preserve_empty_quotes_backslashes_and_unicode executed=8 passed=0 failed=8 skipped=0 trx=/work/worktrees/task-66a1f6d2/.antiphon/checkpoints/20261113-121610-b8d7/rows/CP-S3-R/run.trx slot=granted waited=0s dirty=0 source=c0cc6f18f501fc5d10a231f54f0762b5b0db1671 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=bcaee078bf91e6386de376011d729afe167df358 build=ok filter=/*/*/OwnedGitProcessContractTests/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-66a1f6d2/.antiphon/checkpoints/20261113-122241-6149/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=bcaee078bf91e6386de376011d729afe167df358 sourceState=clean buildSource=verified
CHECKPOINT CP-S3-1 commit=bcaee078bf91e6386de376011d729afe167df358 build=reused filter=/*/*/(GitIndexLockTests*)|(ServerGitAssemblyTests*)/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-66a1f6d2/.antiphon/checkpoints/20261113-122241-6149/rows/CP-S3-1/run.trx slot=granted waited=0s dirty=0 source=bcaee078bf91e6386de376011d729afe167df358 sourceState=clean buildSource=verified
CHECKPOINT CP-S3-2 commit=bcaee078bf91e6386de376011d729afe167df358 build=ok filter=/*/*/(ServerGitProcessTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=129 passed=129 failed=0 skipped=0 trx=/work/worktrees/task-66a1f6d2/.antiphon/checkpoints/20261113-122929-d20b/rows/CP-S3-2/run.trx slot=granted waited=30s dirty=0 source=bcaee078bf91e6386de376011d729afe167df358 sourceState=clean buildSource=verified
CHECKPOINT CP-S3-3 commit=bcaee078bf91e6386de376011d729afe167df358 build=reused filter=/*/*/ReleaseImpactEnvelopeTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-66a1f6d2/.antiphon/checkpoints/20261113-122929-d20b/rows/CP-S3-3/run.trx slot=granted waited=15s dirty=0 source=bcaee078bf91e6386de376011d729afe167df358 sourceState=clean buildSource=verified
```

## Verification scope and obligations

| ID | Actual outcome in this dispatch |
|---|---|
| V-1 | Portable lifecycle and S3a transport/resource boundary contracts green in CP-3; actual native allocation-internal failures and Windows job/root observation remain CP-4 work |
| V-2 | Deferred to Linux native/caller slices; not executed or passed here |
| V-3 | Native Windows dead-parent/JOB_LIST witness owed by orchestrator in S3b; not executed |
| V-4 | Portable encoding, cwd, stream and resource ownership contracts green; actual native launch, binary stdout, handle inheritance, root wait, console, nesting, creation failures and crash lifetime remain CP-4/native caller proof |
| V-5 | Deferred to S4 LandingGit migration; not passed |
| V-6 | Deferred to S5 CardFile migration; not passed |
| R-1 | Deferred to S4b OwnedProcessTree backstop regression; not passed |
| R-2 | Deferred to S4b landing/lease/journal rosters; not passed |
| R-3 | Deferred to S5/E2 card-file rosters; not passed |
| R-4 | Selected ServerGitProcessTests green (23); other named caller/fixture E groups remain deferred |
| R-5 | Actual six-class impact selection and mandatory guards green; remaining integrated E groups are deferred to their assigned slices |

Deferred native rows include CP-4, inherited CP-S2-9, and later CP-7/8/10/14/15/25.
Future CP-5/6/9/11/12/13/16–24 and split E rows remain their own slice work.
Neither Linux green nor a skipped Windows test closes any native obligation.
The whole Unit lane stays with nightly/rc. This is S3a's closed selection, not a
whole-plan or whole-lane qualification.

No PC-n IDs/variants are enumerated in the landed plan. Mutation is operator
waived in the brief; no deliberate red/restore/green controls were executed or
marked passed. All deliberate positive-control work and missing-control
discovery remain pending for caller-commissioned SourceLanding Mutation.
Ordinary baseline red/green above does not discharge a PC.

The selector over the full task range names only `server-git-start-sites` plus
six explicit/derived classes: GitIndexLockTests, OwnedGitProcessContractTests,
ServerGitAssemblyTests, ServerGitProcessTests, DerivedImpactIndexTests,
ReleaseImpactEnvelopeTests. All are bound to the closed rows above. There are
no capped paths, owed classes or selected Windows filters in this portable
diff. The unbounded source guards assert classification, platform guidance,
mechanical controls and layering invariants; the explicit class union and its
cost were recorded before execution. Migrations: not selected, no model or
migration edits, red=0/green=0 (not applicable). Both nightly policy pins were
left unchanged because their inputs did not change.

`GET /api/runner-defaults` and `/api/session-runners` were read: the default was
Linux and Windows was advertised eligible. No runner or platform was pinned.
OS witness declaration lint passed with its exact changed-path inventory and
one classified candidate: unselected pre-creation Windows credential validation.
No existing production factory/property or OS-selected caller changed.
Declaration: `.antiphon/c1850-os-witnesses.json`; selector receipt:
`.antiphon/c1850-impact.json`. The lint proves inventory, not native behavior.

## Hygiene and manual checks

The new cases use memory streams and create no fixture directories or nested
Git repositories. LinuxOwnedGitProcessTests and its fixture were not touched;
its inherited leak is not repaired by this Windows-only preparation slice.
ReleaseImpactEnvelopeTests and the audit were not edited; Debug 49ae044d owns
that repair. Its four newly created GUID repositories were removed only after
checkpoint completion, using individually typed literal absolute paths; its
empty `fixtures` parent was also removed. The GUIDs were:
`86011a4a4c6a451ca39b6b3f204c0324`, `cfcff7eaa9c841e79e112ee367314617`,
`e4cf2b27cedf42f387a88e09f9db242e`, `f12155462f6f44cda62864869c541534`.

Before/after process listings are retained at
`.antiphon/c1850-processes-before.txt` and `.antiphon/c1850-processes-after.txt`.
Neither listing contains a git/sleep/ping/setsid process. The only post-run sh
is the observation command's own shell; all checkpoint executors were joined.
No production host, live qualification, Docker volume or session was changed.

## Costs and command provenance

Debian 12, 24 cores, kernel 4.15.0-213-generic, SDK 10.0.404, runtime 9.0.23,
Git 2.47.3. The guard-run host load snapshot was 12.20/19.87/21.45; comparisons
are provisional and include host contention.

- Red run `20261113-121610-b8d7`: wall 34.430 s, execution 34.429 s,
  admission 0.001 s; reported build phase 20.189 s, row execution 5.087 s.
- Small green `20261113-122241-6149`: wall 373.830 s, execution 58.730 s,
  admission 315.100 s. Build slot waited 315 s; row slots waited 0 s.
  Build phase 24.246 s; CP-3 execution 4.612 s; adjacent execution 7.236 s.
- Guards `20261113-122929-d20b`: wall 671.690 s, execution 626.673 s,
  admission 45.017 s. Build slot waited 0 s, row slots 30 s and 15 s.
  Build phase 248.494 s; guard execution 246.245 s, index-consumer 32.086 s.
  DerivedImpactIndexTests summed 170 s, consistent with S2 history.

Single-run per-case p95/max: CP-3 0.049918/0.054860 s; adjacent
3.243352/3.243352 s; guards 8.094638/62.149975 s; index-consumer
17.453732/17.453732 s. These are provisional small-sample case statistics,
not a full-row p95. No run or row execution exceeded 30 minutes. The cold guard
build plus tests supports the recorded ten-minute provisional row estimate
and twenty-minute cold-build-inclusive cap, approximately twice this observed
cost. Keep native failure-path budgets for S3b separate. No repetition is
required after green. Build/slot timing is separate; uninstrumented subphases
are unknown/remainder. Native fixtures, AppHost, containers and DB migrations
were not used by the new selection.

Unlisted build/driver commands, all explicitly gated, were necessary bootstrap:

1. `scripts/build-slot.ps1 -Label c1850-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints`;
   success, 11 existing warnings, 0 errors, build 5.90 s, slot wait 0 s.
2. `scripts/build-slot.ps1 -Label c1850-derived-driver -- dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false`;
   success, 0 warnings/errors, build 9.03 s, slot wait 1 s.
3. `scripts/build-slot.ps1 -Label c1850-index-regeneration -- pwsh -NoProfile -File scripts/regenerate-derived-impact-index.ps1 -NoBuild`;
   success, six-line change, slot wait 0 s, held 34 s. The non-dotnet wrapper
   printed its expected maxcpucount warning; this command performed no build.

The checkpoint tool ran no unlisted driver. Preflight refusals for a shortened
expected SHA and an unsupported custom-row build-reference syntax executed no
tests/builds. The corrected manifest was committed before green execution.
The unselected CP-10 48-minute estimate warning is historical, not this run.

Rerun with the built checkpoint DLL, the current full committed HEAD as
`--expected-source-sha`, this plan, and separate `--after S3a` /
`--after S3a-guards` runs. The respective row/total/hard-wall budgets used were
2m/4m/49m and 20m/26m/71m, with explicit CARD-1850 provisional bounded-selection
`--total-timeout-override-reason` strings. Bootstrap the derived driver first.
Do not rerun CP-S3-R as a green-baseline claim; its original receipt is red.

`check-evidence-diff.ps1` over the full base..tested-source range reported
5 commits, 0 evidence entries, 0 violations. `git diff --check` passed.
After fetching master, `git merge-tree --write-tree origin/master HEAD` was
conflict-free, tree `e1a95a948ebfa05ee49c859abbaa38e6592da823` at the tested tip.
The report commit gets a fresh final evidence-diff and merge-tree check before
settlement; their exact outputs and final pushed SHA are in the completion report.

Next: Grok Final Review of S3a. No land or deploy is commissioned here.
