# CARD-1677 S1 Code report (task 1296ccbd)

Outcome: S1 is implemented, with a red commit and then a green commit. Checkpoint rows CP-1..CP-6 and CP-12 ran green
through the checkpoint tool at `dc33c3e42d75ccbe67b8cef704b0a2220619908c`, and validate reported
`CHECKPOINT SOURCE VALID rows=7`. I ran 72 positive controls and every one turned its named witness red.

- Branch `feat/card-task-1296ccbd`, runner mirror `/work/worktrees/task-1296ccbd`, original Code task `1296ccbd` (landing owner).
- Commits (base master `7b920d84aae97139998f940ee2cce3f457bb6e70`):
  - `2994f9f21…` / `1df6958a9…`: cherry-picks (-x) of the plan `b477d7ccf` and the test design `958af85ee`.
  - `33be9b27da14478837bac9f1b87d6cb21305a4d9`: the red commit (tests, fixtures, contracts, settings, job, route, docs, policy row, plus the red shim).
  - `dc33c3e42d75ccbe67b8cef704b0a2220619908c`: the green commit (engine and server service).
- Plan: `docs/superpowers/plans/2026-11-03-card-1677-runner-worktree-retention-plan.md`. Test design: `docs/superpowers/plans/2026-11-03-card-1677-test-design.md` (the authority).
- `scripts/check-evidence-diff.ps1 -BaseRef 7b920d84a -HeadRef HEAD` gave `commits=4 entries=0 violations=0`.
- `git merge-tree --write-tree origin/master HEAD` is clean against `59a37c1675862a7ccc8bc7bc36769e2ac50bc0b8`.
- No csproj or pins file was edited. The land graph was not touched.

## Checkpoints (closed list, `tools/Antiphon.Checkpoints start --plan <test design> --rows CP-1,CP-2,CP-3,CP-4,CP-12,CP-5,CP-6`)

Red run `20261103-062903-2faf` at `33be9b27d` (verdict RED, as intended). Every slot was granted:

| CP | executed | passed | failed | what stayed green |
|---|---:|---:|---:|---|
| CP-1 | 46 | 1 | 45 | `Capability_is_advertised_only_on_linux(False)` (a control) |
| CP-2 | 4 | 0 | 4 | none |
| CP-3 | 35 | 35 | 0 | regression class |
| CP-4 | 3 | 3 | 0 | policy witness, with the new row already resealed |
| CP-12 | 3 | 3 | 0 | regression class |
| CP-5 | 54 | 8 | 46 | controls and data pins: V-31 disabled, V-31c unlisted, V-36 shipped defaults, the 4 valid V-36b cases, V-38 attribute |
| CP-6 | 1 | 1 | 0 | regression |

Green run `20261103-064237-bc44` at `dc33c3e42`: verdict GREEN, 9m53s wall, 5 builds, every row `slot=granted waited=0s`
with `sourceState=clean` and `buildSource=verified`.

| CP | executed | passed | failed | skipped | Min |
|---|---:|---:|---:|---:|---:|
| CP-1 | 46 | 46 | 0 | 0 | 46 |
| CP-2 | 4 | 4 | 0 | 0 | 4 |
| CP-3 | 35 | 35 | 0 | 0 | 35 |
| CP-4 | 3 | 3 | 0 | 0 | 3 |
| CP-12 | 3 | 3 | 0 | 0 | 3 |
| CP-5 | 54 | 54 | 0 | 0 | 54 |
| CP-6 | 1 | 1 | 0 | 0 | 1 |

Unedited lines:
```
CHECKPOINT CP-1 commit=dc33c3e42d75ccbe67b8cef704b0a2220619908c build=ok filter=/*/*/(C1677RetentionEngineTests*)|(C1677RetentionInventoryTests*)|(C1677RetentionDispatchTests*)/* executed=46 passed=46 failed=0 skipped=0 ... slot=granted waited=0s dirty=0 source=dc33c3e42d75ccbe67b8cef704b0a2220619908c sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=dc33c3e42d75ccbe67b8cef704b0a2220619908c build=ok filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)/* executed=54 passed=54 failed=0 skipped=0 ... slot=granted waited=0s
CHECKPOINT SOURCE VALID source=dc33c3e42d75ccbe67b8cef704b0a2220619908c rows=7
```

Evidence is gitignored and kept at `.antiphon/checkpoints/20261103-064237-bc44/` (green) and `.antiphon/checkpoints/20261103-062903-2faf/` (red).

### Builds and runs outside the checkpoint list

Every one ran through `scripts/build-slot.ps1` with native output:

- **Dev compile and runs before the red commit.** One `src/Antiphon.SessionRunner` compile, one `tests/Antiphon.SessionRunner.Tests` build and run (55/56: the policy-hash reseal input), and one `tests/Antiphon.Tests` build and run (82/83). The single failure in that last run was a load-sensitive wait in V-32c; I fixed the test (it now waits for the first outcome row before observing the pause), not the bound.
- **One `tools/Antiphon.Checkpoints` build.** It gave the tool a dll to `start`, `wait` and `validate` with.
- **Positive controls.** Two git-archive scratch copies outside the worktree, each with one env-gated build per project. I re-leased the slot for every run: 38 runner runs and 6 server runs.

## Seams as implemented (S-1..S-8)

- **S-1.** ls-remote goes to the runner-owned clone source, never `origin`, through `RunnerWorkspaceService.LiveOriginEndpointAsync`. For the primary that is `PrimaryCloneSource`. For a secondary checkout it is the configured origin, but only when the runner policy admits it and it derives that checkout's own directory name; otherwise the tree is kept as `origin_unavailable`. The call is `InspectionGitAsync(path, "ls-remote", "--refs", "--", <endpoint>, "refs/heads/<b>")` with a 60 s budget. G-17 is an exact ref-name compare, so the decoy `refs/heads/x/refs/heads/<b>` is refused.
- **S-2.** `RunnerWorkspaceRetention(service, IWorkspaceHolderScan, deviceOf, retiringRoot, clock)`.
  - Seams: `PhaseCut` (`checked`, `locked`, `moved`), `PhaseBGate`, `LsRemoteBudget` 60 s, `NestedCountBudget` 10 s, `HolderScanBudget` 10 s.
  - `deviceOf` defaults to the `st_dev` half of `RunnerWorkspaceNoFollowDelete.Identity`.
  - The retiring root is runner-owned (`<AllowedCwd>/worktrees-retiring`) and never comes from the request.
- **S-3.** `HolderScan(Complete, Holders)` and `IWorkspaceHolderScan`, implemented by `ProcWorkspaceSlotProcessProbe`, which gains an internal `(procRoot, isLinux)` ctor.
  - The scan is incomplete off Linux, when the proc root is missing or unreadable, or when the table does not list this process.
  - The slot pool's `Holders()` keeps its fail-open result (CP-12 green).
- **S-4.** `IPhoneHomeRuntimeSurface` gains `RetainWorkspaceCheckAndMoveAsync`, `RetainWorkspaceCompleteAsync` and `WorkspaceRetentionInventoryAsync`, each refusing by default.
  - The dispatcher runs phase A inside `MutateAsync`, and phase B after the lock is released and only for a moved step.
  - Both ops are refused unless the surface advertises `workspaceRetentionV1`. `Supported_operations_preserve_contracts` (AllowedCwd `/work`, a fake runtime) therefore never reaches an engine.
  - `PhoneHomeRuntimeAdapter` takes an optional engine, which runner `Program.cs` composes only when PhoneHome is enabled and the host is Linux. `RetentionFeatures(isLinux)` advertises the feature on Linux only.
- **S-5.** If the leaf is absent, the engine looks for `<RetiringRoot>/<leaf>.<8hex>`. When one exists, phase B only is retried (holder scan plus owning repository checked first), and the response carries `RetiringResidue=true`. Otherwise the answer is `already_absent`, with no prune.
- **S-6.** Not in this slice (S2).
- **S-7.** Not in this slice (S3).
- **S-8.** `RunnerWorktreeRetentionService.RunTickAsync(ct, previewRunner?)` returns `TickResult(RunnerId, Sent, RunId)` per runner. The job only calls it.

## Defaults (shipped off)

- **`RunnerWorktreeRetention` in `server/appsettings.json`:**
  - `Enabled=false`, `Execute=false`, `Runners=[]`
  - `Cron="*/30 * * * *"` (UTC)
  - `MinTerminalHours=12`, `MaxRemovalsPerTick=25`, `PauseBetweenSeconds=5`, `RequestTimeoutSeconds=300`
  - `WorktreeRoot="/work/worktrees"`, `RetiringRoot="/work/worktrees-retiring"`
  - `TempSweep` is carried for S2 with everything off and `Families.claude-scratch=false`.
- **Validator refuses:**
  - `MinTerminalHours<1`
  - `MaxRemovalsPerTick` outside 1..100
  - a relative `RetiringRoot` or `WorktreeRoot`
  - a `RetiringRoot` equal to or inside `WorktreeRoot`
  - a bad cron or an empty runner id
- **Recurring job.** It is registered only while `Enabled` is true and removed otherwise. Attributes: `[DisableConcurrentExecution(3600)]`, `[AutomaticRetry(0)]`.
- **Runner request timeout.** `WorkspaceRetain` waits up to 6 minutes on the live connection.
- **Enabling step.** Documented in `docs/docker-stack.md` under "Runner worktree retention (CARD-1677)": enable with `Runners=["server2"]` in dry run, read the run pages, check the measurements, then `Execute=true` with `MaxRemovalsPerTick=1`. Also covered in `docs/ops-http.md` and `docs/antiphon-api.md`.
- **Preview route.** `POST /api/agent-tasks/worktree-residue/runner-retention/{runnerId}/preview` always runs DryRun. Rows land in a residue run with `Scope=runner-retention:<runner>`.

## Invariant of each check, and its witness

Every witness below was red at `33be9b27d` (under the shim the tree is removed with `--force`) and green at `dc33c3e42`.

| Check (runner unless noted) | Invariant | Witness |
|---|---|---|
| direct child of own root | no path outside `<AllowedCwd>/worktrees` is ever acted on; the root is never request-supplied (the "allow-listed root" refusal) | V-9 outside-root, nested-under-root |
| no link | a link leaf is refused; its target is untouched | V-9 symlinked-leaf |
| leaf == task-<id8> | only this task's own mirror | V-9 leaf-id-mismatch, V-19 |
| slot leaf / ledger | slots are the pool's | V-10 |
| use gate / journal | an owned or retiring path is held | V-11 use-owner, removal-journal |
| not locked | a locked tree is kept | V-11 locked |
| runner repository and registered | a foreign repository's tree is never touched | V-24 foreign-repository |
| HEAD == request branch == leaf id | the tree is the task branch's | V-24 other-branch, branch-id-not-leaf, detached |
| status `--untracked-files=all`, failure keeps | dirt and untracked files are never moved | V-2 (4 cases, status argv pinned) |
| `rev-list --not --remotes=origin` == 0 | nothing unpushed | V-4 (2 cases) |
| live ls-remote, exact name, tip known, contains HEAD, bounded | the branch on LIVE origin holds HEAD; an unreachable origin keeps | V-5 (5 cases), V-25 |
| fail-closed holders | a live process or an unknown scan keeps | V-7, V-20, V-21, V-23 |
| DryRun mutates nothing | `would_remove` only | V-12 |
| recheck under the lock | a change after the dry pass stops the move | V-26 |
| same device, retiring root not inside the root | a cross-device or nested retiring root moves nothing | V-15, V-29 |
| a failed move leaves the tree | no phase B on the original path | V-27 |
| phase B unforced, no prune | git's own clean check holds; a stale registration survives | V-1 (argv), V-13, V-17 |
| residue retried by phase B only | no second move; other residues untouched | V-28 |
| lock split | phase B outside, phase A inside, phase B only after a move | V-14, V-14b, V-14c |
| inventory read-only | only `worktree list --porcelain` | V-18, V-18b |
| server: §3.2 eligibility | each ineligible fact alone is held with its exact reason; the control is still sent | V-30 (23), V-30b (3) |
| server: gates and bounds | disabled sends nothing; DryRun default; listed, phone-home and capable runners only; bound; one at a time; paced; re-read | V-31, V-31b, V-31c, V-32, V-32b, V-32c, V-33 |
| server: recording | path cleared only on removed/already_absent; kept and lost keep it; unmatched names reported | V-34, V-35 |
| producer to recipient | real client, socket, dispatcher, engine and git | V-39, V-39b, V-39c |

## Positive controls run

Each control was env-gated in a scratch copy, so production source was never edited, and run method-scoped. Each gated build had a no-mutant control run first: runner 50/50 and server 54/54 green.

**Runner (37), each red on exactly the named case:**

| PC | Red case(s) |
|---|---|
| PC-3 | leaf-id-mismatch |
| PC-5 | ws-leaf |
| PC-6 | ledger-path |
| PC-7 | use-owner, removal-journal |
| PC-8 | locked |
| PC-9 | foreign-repository |
| PC-10 | other-branch, detached |
| PC-11 | branch-id-not-leaf |
| PC-12 | V-2 ×4 |
| PC-13 | V-2 ×4 (the status argv pin catches it) |
| PC-14 | status-unavailable |
| PC-15 | V-4 ×2 |
| PC-16 | deleted-on-origin, decoy |
| PC-17 | decoy |
| PC-18 | origin-unavailable |
| PC-19 | 4 cases |
| PC-20 | unknown-tip |
| PC-21 | reset-behind |
| PC-22 | V-25 |
| PC-23 | V-1 |
| PC-24 | V-7 holder, V-20, V-21 |
| PC-25 | scan-unknown |
| PC-26 | V-23 |
| PC-27 | V-12 |
| PC-28 | V-26 ×2 |
| PC-29 | V-15 |
| PC-30 | V-29 |
| PC-31 | V-27 |
| PC-32 | V-13 |
| PC-33 | V-1 |
| PC-34 | V-28 |
| PC-36 | V-14 |
| PC-37 | V-14b |
| PC-38 | V-14c |
| PC-39 | V-17 |
| PC-40 | V-18b |
| PC-41 | V-14d(false) |

**Server (35):**
- **PC-42..PC-64.** Batched in one run, because each mutant reaches only its own case. Every named V-30 case was red. PC-50 needed a second, isolated run, because my first gate left the population query's RunnerId filter in place; the full mutant turned `other-runner` red.
- **PC-81, PC-82, PC-83.** Validator cases red.
- **PC-65, 67, 69, 70, 72, 73, 74.** Red in one ops batch.
- **PC-66.** Re-run alone (`Execute_off_sends_dry_run_only` red), because PC-74 also reaches that method.
- **PC-75.** Run alone (`lost` red).

**Not run, still pending for SourceLanding Mutation:**
- **PC-1 and PC-2.** In this code the lexical direct-child check and the resolved-parent check overlap: dropping either alone is masked, by the other or by `not_runner_repository`. A faithful mutant has to drop both comparisons.
- **PC-35, PC-68, PC-71, PC-76.** Not attempted in the time I had.
- **PC-77.** V-34 checks this structurally instead: the service has no `IWorktreeManager` dependency.
- **PC-78..PC-80** (shipped data) and **PC-84** (attribute). Not attempted in the time I had.
- **PC-114.** Policy attribute. Not attempted in the time I had.
- **S2 and S3 controls.** Not this slice.

Every PC remains formally pending for method-scoped SourceLanding Mutation.

## Deviations from the test design

- **V-11 "removal-journal".** It uses an unreadable journal, because building a valid `CompletedCardWorkspaceCommand` is heavy. The reason is still `workspace_retiring_or_unknown`.
- **V-30 "land-pending".** It uses the `?landPending` predicate: `LandRequestedAt` set. The service also refuses a pending `AgentTaskLandRequests` row.
- **V-31c "unlisted".** It records no run row, because `Runners=[]` has no runner to record. "not-phone-home" and "no-capability" do record `runner_not_eligible`.
- **The removal "Info" event.** No Info event type exists, so it is a `Warning` event with detail prefix `runner_mirror_retained:`. It carries `phaseAMs` and `phaseBMs`; candidate rows have no phase columns (no migration, D-8).
- **Inventory owner hint.** It is stored in `Branch` (`owner_hint:task-<id8>:<Status>`) with `TaskId`=owner. Slot leaves are skipped.
- **Held rows.** They are deduped per task through the CARD-1446 cursor rule on scheduled ticks in both modes, so a dry run does not write about 170 rows every 30 minutes. The preview route and send-time rechecks write every row.
- **Timeouts.** I kept the design values (15/10/45 s). Measured maxima on loaded server2: engine 1.6 s, native 2.5 s, server 17.7 s.

## Windows and Linux notes

- Retention is Linux-only by construction. The capability is advertised only on Linux, the runner composes the engine only on Linux, and the scan is incomplete off Linux.
- `C1677RetentionNativeProcessTests` has `[NativeLinuxOnly]` on every method and a policy row (platform windows, Linux-only reason). `policyHash` was resealed to `d4c817ca…a2e7`.
- The portable classes use `/bin/sh` for fault injection, as the C0824 scratch does. All rows ran on server2; nothing ran on the desktop.

## Safety facts for the reviewer

- **No real worktree root in tests.** Every test creates its own `c1677-ret-<32hex>` or `c1677-transport-<32hex>` temp root. The server tests use string paths only.
- **Never forced.** The engine never runs `rm`, `prune`, `fetch` or a branch deletion. `--force` appears only in the red shim, which the green commit replaced.
- **Doubt keeps the tree.** A git start failure, an unparseable answer, an unreadable ledger, an unreadable `/proc` or the endpoint `origin` all keep it.
- **Unmatched folders are report-only.** The 17 `task-cffa0c4b-*` and `task-e38d6427-*` folders become `RunnerInventory` rows and are never sent.

## Cleanup

- Scratch mutant copies deleted.
- Every `bin-c1677-*`, native `bin/` and `obj/` under `src`, `tests`, `tools` and `server` deleted (`git clean -ndX` is empty).
- No `/tmp/c1677-*` left.
- Checkpoint evidence kept under `.antiphon/checkpoints/` (8.7 MB) for Review.
