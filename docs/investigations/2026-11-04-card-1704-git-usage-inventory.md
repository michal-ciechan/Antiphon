# CARD-1704 angle A: git CLI usage inventory in the land path

Date: 2026-11-04. Base: master `1470340559889c2985ea722173181ea25c51b47c`. Read-only investigation; no product change.
Branch: `feat/card-task-1cf13574`. Companion angles: B (LibGit2Sharp capability) and C (shared-repository design).

## Outcome

- One land in the unit land harness (`PostLandMutationDeliveryTests.C478_V09a_LandProducerToCaller`, real temp
  repositories, `ControlledVerifier` in place of the build) starts **211 top-level git processes** (244 counting
  git's own children: remote-helper/upload-pack/pack-objects). Measured with `GIT_TRACE2_EVENT=<dir>`, one file
  per process (method below). Master's own commit messages give 271 for the same test with the land-producer
  bridge included (ae91aeff1) and about 740 for a native E2E land (`LandDeliveryOptions.cs:269`).
- On server2 that land took **6.95 s of wall time, of which 0.495 s was time inside git** (trace2 `t_abs` summed).
  The other 93% is process creation, .NET plumbing, and the harness's Postgres writes; trace2 cannot separate them.
- Per-process cost on the **real** mirror repository (5,862 refs, 39 packs, 369 registered worktrees, 9,799
  tracked files; server2 load average 37 to 48 on 24 cores) is dominated by the work, not by process startup:
  `git version` 4.0 ms, `rev-parse --verify HEAD^{commit}` 4.9 ms (92.7 ms on a cold first pass),
  `show-ref --verify` 5.9 ms, `worktree list --porcelain -z` 77 ms (372 ms cold), and
  `status --porcelain=v1 -z --untracked-files=no` **520 ms** (3.45 s cold; trace2 perf shows 0.36 s of it is
  `index refresh`, a stat of every tracked file). No Windows measurement was taken (the desktop was off limits).
  The repo's own Windows estimate is 80 to 200 ms per spawn (`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs:269-270`).
- **The land path on master runs no `cherry-pick`, `merge-tree`, `patch-id`, `bundle`, `gc` or `notes`.** The
  brief's list was wider than the code. `git cherry` (patch-id) and `rev-list --min-parents=2` belong to dispatch-time
  base inspection (`WorktreeBaseInspection.cs:87,178`), not to the land.
- **A single object already spans the whole land:** `LandRepositoryLeaseScope`
  (`server/Application/Services/PreparedLandingVerification.cs:14-59`) holds the repository lease and the
  `ILandingOperationScope` read cache plus the git profile. It is released for the verification window and
  re-acquired with a new read scope (`AgentTaskLandService.cs:714-734`). This is the natural owner of a shared
  repository handle, and its release/re-acquire is the cache-invalidation boundary such a handle would need.

## Method (reproducible)

```
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1704-inv-build -- dotnet build tests/Antiphon.Tests -c Debug -p:UseAppHost=false   # 4m17s, 0 errors
GIT_TRACE2_EVENT=<scratch>/trace1 GIT_TRACE2_EVENT_NESTING=1 \
  pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1704-inv-trace -- dotnet run --project tests/Antiphon.Tests -c Debug \
  -p:UseAppHost=false --no-build -- --treenode-filter "/*/*/PostLandMutationDeliveryTests/C478_V09a_LandProducerToCaller"
# 1/1 passed, 50 s; 279 trace files. Fixture setup (init/commit/clone/worktree add/push of the seed) ends at 08:53:46.5;
# the land window starts at 08:53:48.043 (first lease common-dir read) and ends at 08:53:54.994 (update-ref --stdin).
```

Each trace file was parsed (sid depth = nesting; `start.argv`; `def_repo.worktree`; `exit.code`; `atexit.t_abs`).
The micro-benchmark ran 30 to 100 sequential invocations per command with `GIT_OPTIONAL_LOCKS=0` against this
task's worktree (read-only commands only). Both runs are recorded because the load average was high and noisy.

### Measured histogram (one land, top-level processes, keyed like `LandingGitProfile.HistogramKey`)

| n | command | | n | command |
|---:|---|---|---:|---|
| 32 | show-ref --verify | | 7 | fetch |
| 26 | rev-parse --verify | | 7 | status |
| 24 | remote get-url | | 6 | show-ref --exists |
| 21 | symbolic-ref | | 3 | update-ref (pins) |
| 19 | rev-parse --path-format=absolute (common-dir / git-path index.lock) | | 2 | ls-files --others |
| 13 | check-ref-format | | 1 each | worktree add, worktree lock, rebase, diff-tree, push, merge --ff-only, update-ref --no-deref -d, for-each-ref, update-ref --stdin |
| 12 | ls-remote | | | |
| 10 | merge-base --is-ancestor | | | |
| 10 | rev-parse --absolute-git-dir | | | |
| 10 | worktree list | | | |

By repository: 147 in the canonical (shared) repository, 34 in the task's source worktree, 17 in the land worktree,
and 13 with no repository (`check-ref-format`). **19 of the 211 are network round trips** (12 `ls-remote`, 7 `fetch`,
plus 1 `push`). Here the remote is a local bare repository; in production it is the origin URL.

By phase (split at the first command of each phase): admission and observation 51; land-worktree prep 8;
rebase and prepared pin 9; pre-push rechecks 41; push and confirm 15; canonical advance 2; **cleanup and branch
delete 82**; ref retention 3. Cleanup is the largest phase: guarded removal re-runs the full source inspection
(about 11 processes each, four times) and three `cleanup-observed` remote observations (about 9 processes each).

## Inventory table

Classes: **1** = trivially available in LibGit2Sharp (ref read/write, rev-parse, merge-base, config, status-like
reads); **2** = available with care (semantics, credentials, filters or porcelain parity must be proven); **3** =
not available, or must stay CLI for its semantics. Angle B owns the capability verdicts. These are the inventory's
working classification from the code's needs. "Per land" is the measured count above. "net" means network.

### LandingGit primitives (`server/Infrastructure/Git/LandingGit.cs`)

| Call site | Command and flags | Parsed output / exit semantics | Scope | Per land | Class |
|---|---|---|---|---:|---|
| `ExecuteProcessAsync` :121-215 (all calls) | `git` with `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0`, auto-maintenance off; journal (`RepositoryChildJournal`) for `IsMutating` verbs :508; 5 min budget; tree kill on timeout | exit code + stdout (UTF-8); stderr is **never** surfaced (secrets) | n/a | all | the process, journal and kill machinery goes away for in-process calls; it stays for whatever remains CLI |
| `ExecuteProcessAsync` :197-201 | `rev-parse --verify HEAD^{commit}` after every successful `rebase` | sha into `RebaseHeadSha` | local | 1 | 1 |
| `ExecuteAsync` :90 memo | `check-ref-format <ref>` | 0 valid / 1 invalid / 128 config broken; `refs/heads/*` verdicts memoised per instance | none | 13 | 1 (pure string rule; libgit2 `git_reference_name_is_valid`; parity on edge cases to prove) |
| `CommonDirectoryAsync` :549 | `rev-parse --path-format=absolute --git-common-dir` | path, then canonicalised (reparse points) | local | ~19 (with index.lock below) | 1 (`Repository.Info.Path`/commondir; canonicalisation stays .NET) |
| `ListRegistrationsAsync` :570 | `worktree list --porcelain -z` | NUL fields `worktree/HEAD/branch/detached/bare/locked/prunable`; strict parser :1650-1689; cached by `<common>/worktrees` stamp | local | 10 (77 ms each on 369 worktrees) | 2 (libgit2 lists worktrees and lock/prunable state; HEAD per worktree needs one open per entry) |
| `HasActiveSequencerAsync` :732, `IdentityAsync` :1171 | `rev-parse --absolute-git-dir` | path; then file probes for rebase-merge, MERGE_HEAD and the other sequencer files | local | 10 | 1 |
| `IdentityAsync` :1171-1205 | `symbolic-ref -q HEAD`; `show-ref --exists <src>` (0/2/1); `show-ref --verify --hash <src>`; `rev-parse --verify <x>^{commit}` | exit-code tri-state (2 = absent, 1 = error, needs git >= 2.43) | local | most of 21+6+32+26 | 1 |
| `InspectAsync` :750-794 | `status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none`; Full scope adds `ls-files --others --ignored --exclude-standard -z` | any byte = dirty; ignored paths kept for guarded cleanup | local | 7 status, 2 ls-files (520 ms per status on the real tree) | 2 (libgit2 status must match ignore rules, submodules, autocrlf/filters and untracked=all exactly; it does not use git's untracked cache or fsmonitor) |
| `InspectRecoveryCheckoutAsync` :796-1075 (recovery only) | `diff-tree --name-only -r -z`, `ls-tree -r -z` x2, `check-attr --all -z --stdin`, `diff --cached --quiet`, `diff --quiet`, `ls-files --unmerged/--others/-v/--stage`, `check-ignore --no-index`, `config --bool core.sparseCheckout/core.filemode`, `config --get core.autocrlf`, `cat-file --batch` | exit-code verdicts and NUL listings; refuses filters, sparse checkout and unknown attributes | local | 0 (recovery adopt only) | 2 (attribute and ignore evaluation parity is the risk; `check-ignore --no-index` and `ls-files -v` flags are 3-ish) |
| `RunGitInputBytesAsync` :1076-1170 | stdin-fed `update-ref --stdin`, `check-attr --stdin`, `cat-file --batch` | **does not pass through `RunAsync`** | local | 1 | 1 (ref transaction; libgit2 has no all-or-nothing multi-ref transaction across loose refs. Angle B should check `git_transaction`) |
| `DestinationAsync` / `ReadDestinationAsync` :1207-1224 | `check-ref-format`, `symbolic-ref -q <target>` (1 = not symbolic), `remote get-url --push --all origin` | endpoint string SHA-256 fingerprinted; more than one push URL is ambiguous | local (config) | 21 symbolic-ref, 24 get-url | 1 (config read; `pushurl`/`insteadOf`/`pushInsteadOf` rewriting parity is a 2) |
| `ObserveWithPrefixAsync` :1247 / `ObserveSourceAsync` :1282 / `RecheckSourceRemoteAsync` :1335 / `ConfirmTargetContainsAsync` :1551 | `ls-remote --refs --exit-code <endpoint> <ref>` (2 = missing); `fetch --no-tags --no-write-fetch-head <endpoint> <ref>:<pin>`; `rev-parse --verify <pin>`; `merge-base --is-ancestor` (0/1/other) | single `oid\tref` line strictly validated | **net** + local | 12 ls-remote, 7 fetch, 10 merge-base | 2 (`Remote.ListReferences`/`Fetch` with refspec and credentials; the ssh transport and deploy-key credentials are the risk; `--no-write-fetch-head` parity) |
| `InspectIndexLockAsync` :1355 | `rev-parse --path-format=absolute --git-path index.lock` | path; then the CARD-1436 census of git processes | local | part of 19 | 1 for the path; the census of live `git` processes stays (CLI holders still exist) |
| `PinWithPrefixAsync` :1471, `DeleteRetirementPinAsync` :1393 | `check-ref-format`; `rev-parse --verify <sha>^{commit}`; `update-ref <ref> <sha> 0000…` (create-only); on refusal `show-ref --verify --hash`, `show-ref --exists`; `update-ref --no-deref -d <ref> <old>` | exit-code verdicts | local | 3 update-ref + reads | 1 (compare-and-swap ref create/delete: `Refs.Add`/`UpdateTarget` with an expected old value; reflog parity to check) |
| `ReadRefsAsync` :1440 | `show-ref --verify <ref>...` (stops at first missing, 128) | `oid ref` lines | local | in 32 | 1 |
| `UpdateRefsAsync` :1406 | `update-ref --stdin` (`delete <ref> <oid>` batch, all-or-nothing) | exit only | local | 1 | 2 (atomicity across the batch) |
| `PushAsync` :1496 / `PushOwnedAsync` :1504 | `push <endpoint> <sha>:<target>`; the owned variant journals the pid for crash recovery | exit only; non-fast-forward = refusal | **net** | 1 | 2 (credentials, ssh, `pre-push` hook not run by libgit2, server-side rejection parsing) |
| `PushSourceOwnedAsync` :1513 (adopt only) | `push <endpoint> --force-with-lease=<src>:<expected> <sha>:<src>` | exit only | **net** | 0 | **3** (no lease semantic in libgit2 push: it would be ls-remote then push with a race window, which is not equivalent) |
| `DeleteSourceBranchAsync` :1532 (S2, `Landing:DeleteSourceBranchAfterPublication` off) | `push <endpoint> --force-with-lease=<src>:<sha> :<src>` | exit only | **net** | 0 (off) | **3** (same lease semantic) |
| `DeleteTrackingRefAsync` :1583 (S2) | `show-ref --exists`, `rev-parse --verify -q`, `merge-base --is-ancestor`, `update-ref -d <tracking> <old>` | tri-state exits | local | 0 (off) | 1 |
| `UnregisterWorktreeAsync` :690 | **no git**: renames `<common>/worktrees/<id>` aside, then deletes it | n/a | local file I/O | 1 (land worktree removal) | n/a, stays .NET |

### Land callers of `ILandingGit.RunAsync` (argument lists outside LandingGit)

| Call site | Command and flags | Parsed / exit | Scope | Per land | Class |
|---|---|---|---|---:|---|
| `LandWorkspace.cs:50` | `worktree add --detach --force --force <path> <sha>` (mutating, journalled) | exit | local, shared repo | 1 | **3** (libgit2 `Worktrees.Add` exists, but checkout, filters, hooks and the `--force --force` locked/missing semantics differ; worktree admin layout must stay byte-compatible with CLI consumers) |
| `LandWorkspace.cs:84-103` | `symbolic-ref -q HEAD`, `rev-parse --verify HEAD^{commit}`; reuse path: `update-ref --no-deref HEAD`, `reset --hard <sha>`, `clean -fdx`; then `status --porcelain=v1 -z --untracked-files=all` | exits, status bytes | local, land worktree | ~4 (fresh) | 1 for reads; `reset --hard` and `clean -fdx` are 2 (checkout filters, autocrlf, long paths) |
| `LandWorkspace.cs:107` | `worktree lock --reason antiphon-land <path>` | exit | local, shared repo | 1 | 2 (libgit2 `git_worktree_lock`; reason file format) |
| `AgentTaskLandingProtocol.cs:270` | `-c rebase.autoStash=false -c rebase.updateRefs=false rebase <targetBefore>` | exit; conflicts read via `diff --name-only --diff-filter=U -z` :274; `rebase --abort` :275 | local, land worktree | 1 (+1 HEAD read) | **3** (git rebase drops commits already upstream by patch-id, drops empties, linearises merges and honours rebase.* config; libgit2 rebase does not. Byte-identical rebased SHAs are not guaranteed) |
| `AgentTaskLandingProtocol.cs:522` | `update-ref --no-deref <target> <verified> <current>` when the target is not checked out | exit | local, shared repo | 0 or 1 | 1 |
| `AgentTaskLandingProtocol.cs:525` | `-c merge.autoStash=false merge --ff-only <verified>` in the canonical checkout when the target **is** checked out | exit; on failure, index.lock census :526 | local, canonical checkout | 1 here | 2 (fast-forward is a ref move plus checkout of the main tree; hooks such as post-merge, filters, sparse and index.lock contention with other CLI users) |
| `AgentTaskLandingProtocol.cs:549,594-604,658-683` | `status --porcelain=v1 -z` (`--untracked-files=all` or `=no`), `rev-parse --verify HEAD^{commit}`, `rev-parse --absolute-git-dir` on the land, candidate and source checkouts | dirty if any byte | local | ~10 | 1 reads / 2 status |
| `AgentTaskLandingProtocol.cs:940,948` | `rev-parse --verify <r>^{commit}`, `merge-base --is-ancestor` | sha / 0-1-other | local | in totals | 1 |
| `LandOperationFactory.cs:42-148` | `rev-parse --verify`, `merge-base --is-ancestor` x2, `rev-parse --absolute-git-dir`, `show-ref --verify --hash`, `show-ref --exists` | exits | local | in totals | 1 |
| `AgentTaskLandSourceResolver.cs:309-617` | `merge-base --is-ancestor` (several), `show-ref --verify --hash <prefix>/local-before\|source`; recovery adopt: `update-ref --no-deref <src> <expected> <local>`, `reset --hard <expected>` :349,:490 | exits | local | in totals (adopt 0) | 1 / `reset --hard` 2 |
| `LandVerificationPolicy.cs:22,25` | `diff-tree --no-commit-id --raw -r -z --no-abbrev --no-ext-diff --no-textconv --find-renames <a> <b>`; `ls-tree -r -z <sha>` | NUL raw records (rename detection drives the verify filter) | local | 1 | 2 (rename detection thresholds must equal git's default `-M50%` to choose the same filter) |
| `GuardedWorktreeRemoval.cs:253-412` | `show-ref --exists`/`--verify --hash <src>`, `update-ref --no-deref -d <src> <sha>`, `symbolic-ref -q`, `rev-parse --verify`, `status`, `merge-base --is-ancestor`, `worktree list --porcelain -z`; directory removal is .NET plus `UnregisterWorktreeAsync` | exits | local | the bulk of the 82 cleanup processes | 1 / status 2 |
| `GuardedVerificationRemoval.cs:67-115` | `worktree remove -- <path>`, `symbolic-ref -q`, `show-ref --exists`, `update-ref --no-deref -d`, `rev-parse --absolute-git-dir`, `status`, `ls-files -z` | exits | local | 0 in this land (verification workspace path) | `worktree remove` 3, rest 1/2 |
| `LandRefRetention.cs:31` | `for-each-ref --format=<ref oid> refs/antiphon/land/<t>/ refs/antiphon/progress/<t>/` then `UpdateRefsAsync` | lines | local | 1 + 1 | 1 |
| `LandRecoveryWitness.cs:61` | `show-ref --verify --hash <pin>` | sha | local | recovery only | 1 |
| `RepositoryMutationLease.cs:46-130` → `RepositoryChildJournal.FirstFencingAsync` | `CommonDirectoryAsync` (common-dir), then journal files and `IsProcessAliveAsync` (no git) | path | local | 2-3 | 1 |
| `LandExecutionLease.cs:11` | `CommonDirectoryAsync`, then a `FileShare.None` lock file `<common>/antiphon/land-execution.lock` | path | local | 1 | 1 |
| `LandingVerifier.cs:17-40` | **no direct git**: `dotnet build`/test in the land worktree under the build slot; journals the verifier child. MSBuild or SourceLink may start git inside the build (not traced here: the harness verifier is controlled) | n/a | local | 0 git in harness | n/a |
| `PublishedBranchDeletionService` (CARD-0824 S2, off) | `ConfirmTargetContainsAsync` + `DeleteSourceBranchAsync` + `DeleteTrackingRefAsync` above | | net + local | 0 (off) | 2/3/1 |

### Adjacent paths (not the land itself; in scope only as inventory)

| Call site | Commands | Runs where | Note |
|---|---|---|---|
| `WorktreeManager.cs:113-1265` (delegation worktree creation and removal; own `Process` at :1279-1320, journals `add`/`remove`/`checkout-index`) | `rev-parse --verify <base>^{commit}`, `worktree add [-b] <path> <branch>`, `worktree list --porcelain`, `branch --list feat/card-*`, `merge-base --is-ancestor`, `status --porcelain -z --untracked-files=all --ignored`, `checkout-index --all`, `worktree remove --force --force`, `update-ref --no-deref -d` | desktop server, under the repository lease | worktree add/remove are 3 |
| `DelegationWorktreeService.cs:193,273,275,764,920` (own `Process`) | `rev-list --max-count=1 --min-parents=2`, `rev-list --count`, `log -1 --format=%s` | settlement merge (legacy path) | 1 or 2 |
| `WorktreeBaseInspection.cs:87,178` | `cherry` (patch-id containment), `rev-list --min-parents=2` | dispatch base check | `cherry` is 3 |
| `RemoteWorkspaceService.cs:118-1083` (desktop half of remote prep/publish) | `rev-parse HEAD`, `remote get-url origin`, `push origin <branch>` (journalled as `remote-prep-push`), `update-ref <pin>`, `merge --ff-only` (settlement sync), `cat-file -e`, `worktree prune`, `worktree add`, `rev-parse --show-toplevel/--git-dir`, `status` | desktop server | push 2; worktree add/prune 3 |
| `src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:146-709`, `RunnerWorkspaceSlotPool.cs:104-693` (runner workspace publish op; own process at :975-1020 with `GIT_CONFIG_NOSYSTEM`/`GLOBAL=null`, `GIT_NO_LAZY_FETCH`) | `fetch origin <b>`, `rev-parse FETCH_HEAD`, `worktree add -B`, `clone --filter=blob:none --no-checkout` (**partial clone**), `push --dry-run --porcelain`, `push origin`, `checkout --force -B/--detach`, `update-ref -d`, `worktree remove --force`, `worktree prune` | Linux runner (server2), separate process | the card puts it out of scope; partial clone is 3 for libgit2 |

## Test seams that wrap git (must survive any abstraction)

- **E2E `protocol-git-*.json`**: `EvidenceGit : LandingGit` (`tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs:274-340+`)
  overrides `RunAsync` and writes one `protocol-git-<guid>.json` per command (`{repository, arguments, ExitCode, at, pid}`,
  :296). `LandDeliveryFixture.Dispatch.cs:243-326` and `.ReceiptDiagnostics.cs:114` count those files to renew the
  60 s receipt quiet window. It also caches "stable reads" (`--git-common-dir`, `check-ref-format`, `remote get-url`)
  and drops the volatile cache on mutating verbs. **An in-process call that does not go through the virtual
  `RunAsync` writes no record, and that is exactly the failure fixed for the verifier in 81ce05099/147034055.** Today
  `RunGitInputBytesAsync` (`update-ref --stdin`, `check-attr`, `cat-file --batch`) and `UnregisterWorktreeAsync`
  already bypass it.
- **Unit `ControlledLandingGit`** (`tests/Antiphon.Tests/TestHelpers/ControlledLandingGit.cs`) records `Trace` and
  exposes `BeforeObservedCommand` / `AfterCommand` hooks keyed on argv (`rebase`, `merge --ff-only`, `push`,
  `update-ref …/source`, `worktree remove`, `update-ref -d`). `LandingSafetyHarness.cs:51-62,117-127` uses them as
  crash cuts C03 to C17. Those cut points are expressed as **git argv**, so an `IGit` would need equivalent named
  operation boundaries.
- `LandingGitProfile` (`server/Application/Dtos/LandingDtos.cs:100-190`) is fed only by `ExecuteScopedAsync`, and the
  production log line `Land git profile … processes= … histogram=` (`AgentTaskLandService.cs:550`) prints it per land.
  With in-process calls, "processes" would no longer measure the work.

## Ownership model today: shared repository versus per-task worktree

| Object | What touches it | Protection |
|---|---|---|
| Canonical/shared repository (`task.RepoPath`; common dir via `rev-parse --git-common-dir`) | 147 of 211 commands: refs (`refs/heads/<target>`, `refs/antiphon/land/<task>/<op>/…` pins, observation refs), `worktree add/list/lock`, fetch into pins, push, `update-ref` target advance | **Repository lease**: in process `RepositoryMutationLease._owners` keyed by common dir, plus `<common>/…/landing.lock` FileStream (`RepositoryMutationLease.cs:46-60`) and the child-journal fence (`RepositoryChildJournal.FirstFencingAsync`), so an unfinished CLI child from a crashed process blocks acquisition. **Land execution lease**: `<common>/antiphon/land-execution.lock`, `FileShare.None` (`LandExecutionLease.cs`). Git's own `*.lock` files for each ref update. |
| Canonical **checkout** (main worktree, when the target branch is checked out there) | `merge --ff-only` (:525), status | Index lock: `InspectIndexLockAsync` + `GitIndexLock` census of live git processes (CARD-1436); refusal `canonical_index_lock`. Other tools (operators, AppHost restarts) use this checkout with the CLI. |
| Land worktree `trees/land/<id>` (detached, locked `antiphon-land`) | worktree prep, rebase, status, verifier build | The worktree lock plus the repository lease. **The lease is released during verification** (`AgentTaskLandService.cs:714-734`: `OpenVerificationWindowAsync`, `ownership.ReleaseAsync()`, then `ReacquireAsync` + `BeginReadScope`). Other lands can mutate shared refs in that window. |
| Task source worktree (`task.WorktreePath`) | identity and inspection reads (34), branch delete at cleanup | Repository lease; inspection is read-only (`GIT_OPTIONAL_LOCKS=0` so status does not write the index). |
| Remote (origin endpoint) | ls-remote, fetch, push | Endpoint fingerprint re-read before each use; remote-side atomicity. |

### What "one shared repository for the whole landing operation" maps to in today's code

1. **Lifetime owner:** `LandRepositoryLeaseScope` (created at `AgentTaskLandService.cs:529`, disposed in the
   `finally` at :540-557). It already carries the lease and `ILandingOperationScope`. The scope cache is an
   `AsyncLocal` on the `LandingGit` instance (`LandingGit.cs:639-686`); `BorrowedScope` lets nested calls share it.
2. **Handle granularity:** libgit2 opens a repository per **worktree** (each linked worktree has its own HEAD and
   index). One land touches at least three: the canonical repo/checkout, the land worktree and the task source
   worktree, plus the remote. "One handle" means one object database and ref database shared through the common
   dir, plus a small set of per-worktree views. The call sites already pass `repository` as a path string to every
   `ILandingGit` method, so a path-keyed handle cache inside the scope is the shape that fits without touching callers.
3. **Invalidation boundaries already exist:** `ChangesRegistrations` (`LandingGit.cs:606-630`) classifies every
   subcommand as read-only or topology-changing, and `OperationScope.Invalidate` clears caches. The verification
   window release/re-acquire is a hard boundary: other processes (CLI lands, delegation worktree creation, operator
   git) may write refs and packs while it is open.
4. **Single-writer fact:** inside the lease, the land is the only Antiphon writer to the shared refs, but **not the only
   git user**: CLI children (verifier build, operator shells, runner pushes into origin) continue. A shared handle
   must therefore re-read refs from disk on each read (no ref caching beyond the scope's existing rules).

## Remaining uncertainties

- Windows per-process cost was not measured (the desktop was off limits); the 80 to 200 ms figure is the code
  comment's, not a measurement. A desktop-lane rerun of the same trace (`GIT_TRACE2_EVENT`) would settle it.
- The E2E native land count (~740) was not re-measured; server2 has no Chromium, but the land-delivery E2E may
  not need it. Not attempted inside the time box.
- Postgres and harness time inside the 6.95 s land wall was not separated from spawn time. An `strace -f -c` or a
  profile with `LandingGitProfile.GitSeconds` exposed in the harness would split it.
- The server2 load average (37 to 48) makes the micro-benchmark noisy: the cold/warm pairs differ by up to 19x.

## Not done, noted

- A fix idea, not designed: the cleanup phase (82 processes) repeats full source inspections and three remote
  observations; batching those reads is a CLI-only win independent of LibGit2Sharp.
