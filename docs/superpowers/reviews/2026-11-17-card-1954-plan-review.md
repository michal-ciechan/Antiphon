# CARD-1954 plan review, round 1

CLEAN. No blocker. Advisories below do not hold the land.

Reviewed source `5c92d5d83f00e488583aee50a6ad8bbd4b51462a` on `feat/card-task-24cbd9e1`, plan owner `24cbd9e1-2ccf-436e-b4fe-89b605b5fcc6`. Base and `origin/master` are `870a93d21f2b18c85a8b0905ab689b28e3099813`. The diff is only `docs/superpowers/plans/2026-11-16-card-1954-evidence-vs-fixture-separation-plan.md` (2 commits, +220). Reviewer is Grok. Mutation stays waived (operator 2026-11-12). Positive controls stay pending.

## (1) Premise

The five retirement classifications hold at master `870a93d21`.

| Claim | Code | What it changes |
|---|---|---|
| 2026-11-15 gate-9 refusals came from CARD-1901 `VolumeRetirementPublicationFixture` | The landed fixture root is `Path.GetTempPath()` on Windows and `/var/tmp` otherwise (`tests/Antiphon.Tests/Scripts/VolumeRetirementPublicationFixture.cs:95-96`). The investigation commit `982464a0` (not on master) names the old checkout path `worktrees/task-e479506f/.antiphon/evidence/card-1901/fixtures/.../origin.git`. | Nothing. The producer that refused gate 9 is already off the audited checkout path. |
| 2026-11-16 refusals were timing and path | `scripts/c1008-retirement.sh:4-13` records the 5,070 ms and 5,050 ms samples, the provisional `C1609_EFFECT_WINDOW_MS=11000`, and the unchanged desktop 5-second control, 10-second read, and 900-second proof lifetime. The proof-stale note's "Not an observed unpublished-canary refusal" stands. | Nothing. Those refusals are outside this card. |
| Only `ChannelOutboundIsolatedTests.Composed` can leave Git repos in a Linux runner volume | Linux-only skip at `ChannelOutboundIsolatedTests.Composed.cs:38-40`. Three opt-ins in `ChannelOutboundIsolatedTests.cs:132-137`. `git init` and commit on `repo` and `repo-q` at lines 74-81, with no `remote add`. `finally` at 497-501 disposes the app and stops the gateway and leaves the root. The root is `<checkout>/.antiphon/test-output/card-0418/f5/<guid>` (line 45), which is on `/work`. | This is the only retirement-relevant family. Whether it has run on server2 stays unobserved, as the plan says. |
| `OwnedProcessContainmentFixture` only wastes disk | `DirectoryPath` is `<checkout>/.antiphon/containment-fixtures/<guid>` (`OwnedProcessContainmentFixture.cs:12-13`). The constructor writes `fixture.sh` only. `Dispose` (lines 104-116) kills descendants and keeps the directory. No Git. | Hygiene only. Ignored plain files are not a retirement refusal. |
| `LandQueueRaceWorker` only wastes disk | `RunPairAsync` (`LandQueueRaceWorker.cs:48-49`) creates a relative `.antiphon/acceptance/card-0467/queue-race-<guid>` and an `owner` file. No Git. `finally` (lines 80-89) writes stdout and stderr logs and does not delete the root. | Hygiene only. See the cwd advisory: checkpoint cwd is the repository, so the files land in the checkout's ignored `.antiphon/`, and they are still plain files. |
| `LandDeliveryFixture` and the C419 canary never reach a Linux runner volume | `InitializeAsync` calls `NativeWindowsGate.Require` at `LandDeliveryFixture.cs:61` before `git init` (lines 67-81). `DisposeAsync` retains the tree (line 446). Linux `LandDeliveryFixtureProbeTests` (14 `[Test]` methods) construct the fixture without `InitializeAsync`. `OutputDistillationApplyCanaryTests` is `[Explicit]` and calls `NativeWindowsGate.Require` before creating its repo. | Git for these two families is native Windows. Linux probes are a plain-file leak, which the plan already records. |

O-5 ("the operator twice had to use the audited manual removal fallback") stays unobserved. The 2026-11-15 note records one recommended manual removal.

## (2) Fail-closed

D-1 keeps the audit unchanged: any repository in `/work` or `/tmp` still counts as work, with no marker, path allowance, or refusal-semantic change. The slice file lists and the must-not-touch list leave `scripts/c1008-retirement.sh`, `scripts/c1008-content-audit.sh`, and the rest of the retirement scripts alone. `C1609_EFFECT_WINDOW_MS=11000` and the 5000 ms / 900 s / 10 s controls are outside every slice. `c1609_scan_evidence` (`scripts/c1008-content-audit.sh:485-497`) still flags a regular file named `HEAD`, `ORIG_HEAD`, `FETCH_HEAD`, `REBASE_HEAD`, `BISECT_HEAD`, or `MERGE_HEAD` outside `*/.git` and `*/.git/*`.

## (3) Evidence helper and the report store

`RetainEvidence` refuses a relative path with a segment named `.git` or one of those six pseudoref names, including the planned `x/.git/y` row, plus a symlink, reparse, or hard-linked source. A sibling destination that still contains one of those segments (`../sibling/.git/HEAD`) is refused by that rule. A non-forbidden name that escapes the evidence root through `..` or an absolute path is an advisory hole, not a bypass of the Git-shaped check.

`AgentReportStore.IsPersistent` (`server/Infrastructure/Files/AgentReportStore.cs:261-264`) refuses a root inside `Path.GetTempPath()`, inside `task.WorktreePath`, or with a reparse ancestor. On Linux, `GetTempPath()` is `/tmp`, so `/var/tmp` is persistent. `%LOCALAPPDATA%\Antiphon\fixture-roots` is outside `%TEMP%`. D-4 matches that rule for LandDelivery and C419. Moving those repos out of the worktree also satisfies the worktree clause.

## (4) Slice order and checkpoints

Order S1, then S2 parallel with S3, then S4, then S5 is dispatch-ready on the commissioned checks.

`import --plan` accepted 9 rows, exit 0, lease `ad4b40c7-57fc-4557-9deb-e5ca421ed4f8`. Builds are `bin-c1954-red/`, `bin-c1954-s1/`, `bin-c1954-s3/`, `bin-c1954-e2e-red/`, and `bin-c1954-e2e/`, each with a trailing slash. CP-2, CP-4, CP-5, and CP-6 reuse a build inside the same `After` group. CP-7 has its own Tests build because its `After` is S3. Each row has one filter. No importer warning. The Windows table is a later `### Windows-lane checkpoints` section, so the Linux import ignores it. Derived `totalMinutes` is the manifest default 90.

Red witnesses compile against the current public surface and fail for the reason the plan names:

- CP-1 calls `new OwnedProcessContainmentFixture()`, which exists. `DirectoryPath` is under the checkout and `Dispose` keeps it, so the outside-checkout and removed assertions fail.
- CP-2 adds a census to `C467_V09_KeyedQueueRacesAndDistinctEvents`. `C488_ApprovalReceiptQueueKeyRequired` and `C488_ApprovalReceiptKeyCollisionRefuses` call that method. The directory is created and not deleted, so three census assertions fail. Each glob matches one method.
- CP-8 adds a method on the existing `LandDeliveryFixtureProbeTests` class. `new LandDeliveryFixture()` runs on Linux. `Root` is under `<checkout>/.antiphon/acceptance/card-0467`, so the outside-checkout assertion fails.

Budgets are provisional where section 4 says so, or taken from the named CARD-1953 and Composed samples. Nothing in the table is a 30-minute row.

The only file shared by the parallel pair S2 and S3 is `tests/derived-impact-index.json`, and both list it as the last commit. S4 runs after that pair, so its edit of `C1954FixtureLayoutTests` (created by S2) is sequential. No slice file is `scripts/c1609-host-proof.ps1`, `scripts/c590-real.ps1`, or `docs/docker-stack.md`. CARD-1957 and the docs task stay clear of this plan.

## (5) Downscope

The retirement evidence supports a smaller card. The minimal card moves Composed `repo` and `repo-q` to an owned `/var/tmp` root, copies only plain evidence back through a helper that refuses Git-shaped names, deletes that root in `finally`, and leaves the audit unchanged. Containment and queue-race are ignored plain files. LandDelivery Git and the C419 canary are native Windows. Linux LandDelivery probes are a plain-file leak.

Q-2 recommends keeping S3 and S4 because the card names them, and the unanswered default is yes. That default is a scope choice the plan already records. It does not block this review.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `a7517b5591d9db369f91994d4e88071aad7b86cf`, no conflicts |
| `scripts/check-evidence-diff.ps1` base..HEAD | commits=2, entries=0, violations=0 |
| `scripts/impacted-classes.ps1 -Range` | kinds empty, treenodeFilter empty; downgrade count 0 |
| `scripts/check-os-witnesses.ps1` | exit 2, `LiteralPath` empty. The range is the plan markdown only, so no new OS-selected production path is in it. |
| Leased `dotnet build tests/Antiphon.Tests` (`UseAppHost=false`, slot `a35f7c7e-0b7f-4092-9768-ca062bd77489`, maxcpucount 6) | 0 errors, 2127 warnings, 12m 05.30s, exit 0 |
| Leased `dotnet run --no-build` of the five guard classes (slot `fe479751-f5de-4778-ab38-bc2461b296ff`) | total 53, failed 0, skipped 0, 53s 852ms, exit 0 |

Executed split: `TestClassificationPolicyTests` 42, `SlowTestRegistryMergeTests` 5, `NativeLinuxPolicyWitnessTests` 3, `SlowTestTripwireTests` 2, `TestClassificationGuardTests` 1.

CP-1 through CP-9 name tests this tree does not contain, so they were imported and read, not executed. `reviewedSourceClean` stays false because these runs are build-slot leases, not checkpoint-tool receipts. Producer/recipient delivery and CARD-1083 input-wait release do not apply.

## Advisories

- **CP-8 plants the hazard this card exists to stop.** The red method writes `repo/.git/HEAD` and `remote.git/HEAD` into `Root`, then disposes, then asserts. At base, `Root` is inside the checkout on `/work`, and `DisposeAsync` retains the tree (`LandDeliveryFixture.cs:446`). `c1609_scan_git` lists every directory named `.git`; a directory Git cannot open is an unvisited repo and the audit comes back unknown. `remote.git/HEAD` is a `HEAD` file whose parent is not named `.git`, so `c1609_scan_evidence` sets `evidence-name`. Assert that `Root` is outside the checkout and outside `/work` and `/tmp` before creating any `.git` directory or pseudoref file, so the red failure returns before those writes. Create Git-shaped names only on the green `/var/tmp` root.
- **Queue-race cwd.** Checkpoint runs set the test process working directory to the repository (`tools/Antiphon.Checkpoints/Program.cs:607`). The relative `queue-race-*` root therefore lands in the checkout's ignored `.antiphon/acceptance/card-0467/`, which is still plain files and still outside the audit. D-6's "under the ignored test output" sentence should follow that cwd.
- **CP-3 Min 45.** `LinuxOwnedGitProcessTests` has 12 `[Test]` methods and no `[Arguments]`. The plan's "35" is the historical sample, and the plan already tells Code to set Min from the counted cases. Copying 45 would make the green row fail on the floor.
- **CP-6 Min 10** is a weak floor against the guard union (this run executed 53 in the five classes alone). The expect text still requires every listed test, 0 failed.
- **W-3's heading says "red at base".** The body, and CP-5, require `A_failing_worker_exits_1_without_running_tests` to stay green.
- **S5 insertion point.** The end of `## Performance standards` is immediately before the CARD-0254 preserved heading (`docs/testing-and-build.md:170`). "Combined class filters" begins at line 1423. The new subsection belongs at the end of Performance standards.
- **S4's file list omits `tests/derived-impact-index.json`.** The shared-file paragraph says each test-adding slice regenerates it last. S4 adds a method, so it should regenerate the index on its own tip.
- **`RetainEvidence` escape.** The Git-shaped segment check does not reject `..` or an absolute destination whose segments are ordinary names. Refuse a destination that normalizes outside the evidence root.
- **Downscope.** The minimal Composed-only card in section (5) can be a follow-up if the operator accepts Q-2's split alternative. This review leaves the plan's default (keep S3 and S4) in place.
