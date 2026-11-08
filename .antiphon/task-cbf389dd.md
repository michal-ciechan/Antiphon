# CARD-1776 Review round 2 (Final/Full, diff only) of cef2481a855a317cfcb720a91e1f9a81513ffd19

Verdict: CLEAN. B-1 is closed end to end; no blocker and no regression in the round-2 diff. Four
advisories (A-9 to A-12) under D-2: no rerun and no Code repair solely for them.

Subject `origin/feat/card-task-93dff2d9` tip `cef2481a855a317cfcb720a91e1f9a81513ffd19`
(Code task 93dff2d9-f56a-4007-8c15-a824193872b9, Grok, owner repair of 19293e15-c7e1-4cf1-94da-4a2816c9a483).
Reviewed range `0f76e7b0a9379345684dbb4f5452073e53eb82f4..cef2481a855a317cfcb720a91e1f9a81513ffd19`
(red `989f150f462ec3798e634ae7c1a92265deb08e51`, fix `cef2481a855a317cfcb720a91e1f9a81513ffd19`; 10 files +427/-17).
Read-only review: no product file changed; the only commit is this report. Nothing deployed, started or landed.

## Re-run at the tip (checkpoint tool run 20261108-080440-5b32, one build, two rows)

Plan extract `.antiphon/review-c1776-r2-plan.md` (ignored). Build and both rows `slot=granted`; `unlisted: none`;
`outputs: deleted bin-c1776-r2/`; validate: `CHECKPOINT SOURCE VALID source=cef2481a855a317cfcb720a91e1f9a81513ffd19 rows=2` (exit 0).

CHECKPOINT CP-1 commit=cef2481a855a317cfcb720a91e1f9a81513ffd19 build=ok filter=/*/*/(ServerImagePushCredentialTests*)|(DockerStackContractTests*)|(ServerRepositoryMirrorScriptTests*)|(GithubCredentialHelperTests*)/* executed=141 passed=141 failed=0 skipped=0 trx=/work/worktrees/task-cbf389dd/.antiphon/checkpoints/20261108-080440-5b32/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=cef2481a855a317cfcb720a91e1f9a81513ffd19 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=13.539s wall=13.616s
PHASES CP-1 slotWait=0s build=482.1407296s startup=7.1463789s testsWall=4.8484854s teardown=0.4485276s hostWall=12.4436373s
CHECKPOINT CP-2 commit=cef2481a855a317cfcb720a91e1f9a81513ffd19 build=reused filter=/*/*/(TestClassification*)|(TestLaneCategoryGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessMutationTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/task-cbf389dd/.antiphon/checkpoints/20261108-080440-5b32/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=cef2481a855a317cfcb720a91e1f9a81513ffd19 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=105.995s wall=106.050s
BUDGET wall=724.370s execution=724.370s admissionOnly=0.000s

| Row | Plan row | Classes | Executed | Code claimed |
|---|---|---|---|---|
| CP-1 | CP-4 of the CARD-1744 cutover plan (Min 28, stale) plus the two classes the brief names | ServerImagePushCredentialTests 13 (9 + 4 new), DockerStackContractTests 117, ServerRepositoryMirrorScriptTests 5, GithubCredentialHelperTests 6 | 141/141 | 141 |
| CP-2 | guard batch (round-1 filter verbatim) | the eight named classes plus TestLaneCategoryGuardTests | 85/85 | 85 |

Fresh identities: `Identity_free_rebase_fails_without_git_config_global_and_passes_with_it`,
`Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity`,
`Configured_unusable_identity_refuses_before_git_starts` and
`Task_progress_and_github_push_use_the_configured_credential_probe` are each `outcome="Passed"` in
`rows/CP-1/run.trx`; the class has 13 results there.

Other checks: `check-evidence-diff.ps1` r2 range commits=2 entries=0 violations=0 (exit 0) and full candidate
`d116a618f1ddc048e5b8501b659cd535fd96deef..HEAD` commits=4 entries=0 violations=0; `git merge-tree --write-tree
origin/master HEAD` exit 0, tree `5a0ac5e43553778ffb33056f611260077947264a` against `origin/master`
`3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` (the tree the Code reported); migrations none; policy JSON untouched; the
product diff carries no `credential.helper`, `insteadOf`, `sshCommand`, `sslVerify`, `safe.directory`, `askpass`,
`GH_TOKEN` or token text. Code's own run (report of 93dff2d9) was leased direct `dotnet run` with no checkpoint
receipt and no CP-n lines (A-12); this run supplies the validated receipt.

## Brief items

1. B-1 closed end to end. Every git command of the land protocol goes through the one `ILandingGit` the protocol
   receives (`AgentTaskLandingProtocol(AppDbContext db, ILandingGit git, ...)`): the rebase and `rebase --abort`
   (`MutateAsync` -> `RunOwnedAsync` -> `ExecuteAsync`), the land-worktree preparation (`LandWorkspace.EnsureAsync`
   through the same `MutateAsync` callback), `merge --ff-only`, `merge-base`, `fetch`/`ls-remote`, the push
   (`PushOwnedAsync`) and the `update-ref --stdin` batch (`RunGitInputBytesAsync`). There is no cherry-pick in the
   land path. `LandingGit` creates a `ProcessStartInfo` in exactly three places (eligibility probe :246, child
   :302, stdin batch :1280) and each calls `ApplyIdentity` after `ConfigureProcess`, so a subclass that clears the
   environment cannot drop it; `IdentityRefusal()` precedes every `ExecuteAsync`, the stdin batch, the ref
   deletions and all five push/delete/observe entry points. DI resolves `LandingGit(CardFileBoardLookup?,
   IOptions<GitSettings>?)` and `TaskProgressGit(IRepositoryMutationLease?, IOptions<GitSettings>?)` with the
   registered `IOptions<GitSettings>` (Program.cs:140, :473, :480). The compose fragment also sets
   `GIT_CONFIG_GLOBAL=/run/antiphon/gitconfig` on the server process, so the children of the option-less
   `new LandingGit()` sites (RepositoryChildJournal, WorktreeManager, GitService, CardFileRepository, the
   DelegationWorktreeService fallback) inherit the same identity. The baked helper stays effective: the image
   installs `docker/server/gitconfig` at `/etc/gitconfig` (system scope, Dockerfile:75) and the fragment does
   not set `GIT_CONFIG_NOSYSTEM`, so replacing the global scope keeps `credential.https://github.com.helper`.
   Override resistance: `ApplyIdentity` overwrites `GIT_CONFIG_GLOBAL`, so an inherited `HOME`, `XDG_CONFIG_HOME`
   or a different inherited `GIT_CONFIG_GLOBAL` cannot redirect the global scope; residue in A-9. Content: the
   code validates presence of `user.name` and `user.email` only and passes every other key through (A-10).
   Witness: `Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity` runs a real
   non-fast-forward rebase through `LandingGit` with `GIT_CONFIG_NOSYSTEM=1`, a scratch `HOME`, author/committer
   variables and `GIT_CONFIG_GLOBAL` removed in `ConfigureProcess`, and succeeds only because `ApplyIdentity`
   runs afterwards; the sibling bare-git witness shows the same rebase exits 128 `unable to auto-detect email
   address` without the file (git 2.47.3, the image's pin).
2. Refusals are real. Symlink: `FileInfo.LinkTarget` plus the `ReparsePoint` attribute (lstat); relative:
   `Path.IsPathRooted`; also directory, missing, empty, over 16 KiB, unreadable (IOException /
   UnauthorizedAccessException) and incomplete (`[user]` without both keys). All exercised on Linux in CP-1
   (`Refusal(link)`, `Refusal("relative-identity")`, directory, partial, missing; `missing.RunAsync(["rebase",
   "HEAD"])` returns `git_identity_absent` with zero `StartProcess` calls). TOCTOU: the probe reads the file and
   git reads it again at start; the probe grants nothing git would not read itself, the mount is `:ro` in the
   container and `deploy-parent` refuses a symlinked host path, so the window is no worse than before. Desktop:
   `Refusal("")` returns null before any I/O (whitespace is trimmed to empty); `new StartCountingGit(new
   GitSettings())` through `CaptureAsync` does not produce `git_identity_absent`.
3. Folds. A-2: `File.OpenHandle` + `RandomAccess.GetLength(handle) > 0` opens for read and copies no byte; the
   XML comment and docs now say so. A-1: `TaskProgressGit` passes the options to the base; `GitHubService` takes
   `IOptions<GitSettings>? git = null` as the last optional parameter and `AddHttpClient<IGitHubService,
   GitHubService>()` resolves it through ActivatorUtilities; `PushBranchAsync` only logs before `new
   LandingGit(null, _git).RunAsync(["push", ...])`. The test proves `ls-remote` through `TaskProgressGit` and the
   GitHub push both refuse `push_credential_absent` with zero starts and no network (the HttpClient is never
   used). Both probes run before `ConfirmedEndpointAsync` and before `StartProcess`.
4. Secrets. `ServerGitIdentity` and `ServerPushCredential` return constant reasons and never the file text;
   `LandingGitResult.Diagnostic` carries `git_identity_absent` only; the docs name paths, the mode and the
   non-secret default identity (`antiphon-server2-runner@users.noreply.github.com`); the fragment holds paths
   only (test asserts no `GH_TOKEN`/`GITHUB_TOKEN`). Compose fails closed: `${RUNNER_GIT_IDENTITY_FILE:?...}`
   refuses an unset or empty variable. The identity file is the runner's existing one
   (`docker-compose.server2-runner.yml:106,137`), created by `ensure_runner_git_identity` with `umask 077` on the
   secrets directory, mode 0644 on the new file, never rewritten; the token directory beside it stays
   `1654:1654` 0400/0700.
5. No regression: DockerStackContractTests 117/117 and GithubCredentialHelperTests 6/6 in CP-1; the guard batch
   85/85 in CP-2; both unchanged by this diff.

## Advisories (D-2; fold into a later slice, no rerun)

- A-9 Environment residue. `ApplyIdentity` pins the global scope, but a command-scope variable inherited by the
  server process still outranks it: `GIT_CONFIG_COUNT/KEY_n/VALUE_n` (`ServerGitProcess.DisableAutoMaintenance`
  appends after an existing count instead of resetting it), `GIT_CONFIG_PARAMETERS`, and
  `GIT_AUTHOR_*`/`GIT_COMMITTER_*`/`EMAIL`. None is set by the image or either compose file, so this is the
  operator's own environment; clear them in `ApplyIdentity` if the pin is meant to be absolute.
- A-10 Key allow-list. `ServerGitIdentity.HasUserIdentity` requires `user.name` and `user.email` and accepts
  anything else, while `docs/agent-credentials.md` says the file holds "user.name and user.email and no
  secret". A `[credential] helper = !cmd` there would receive the token on git's `store` call after the system
  helper supplies it; `[include] path`, `core.hooksPath`, `core.fsmonitor` or `url.*.insteadOf` would run or
  redirect inside the server. Not a blocker: the writer must own `$SERVER2_ROOT/secrets/gitconfig` on the host
  (deploy user with sudo, same boundary as the token directory), the mount is `:ro`, and the runner has used
  the same file as `GIT_CONFIG_GLOBAL` with the same helper since CARD-0631. The parser already exists, so
  refusing any section other than `user` and any key other than `name`/`email` is a few lines and makes the
  docs sentence true; `GitPushUrlCache` already bails on `[include`.
- A-11 Subsection parse. The header split on the first space makes `[user "x"]` count as `[user]`, so a file
  holding only `user.x.name`/`user.x.email` passes the probe and git later fails the rebase with the old
  auto-detect message (`rebase_failed`). Fold into A-10.
- A-12 Process. Code ran the classes as leased direct `dotnet run` again (no checkpoint receipt, no CP-n lines;
  round-1 A-8). The CARD-1744 plan's CP-4 row still says Min 28 for a selection that now executes 141 with the
  brief's classes; the plan lives on `feat/card-task-2fafa091`, not this branch. The red commit's three failing
  pins are assertion-red (compose and docs text absent); the pure-git witness is green at the base by design and
  the product witness compiles only with the fix, so its red is a compile red. Every control stays pending for
  method-scoped SourceLanding Mutation; the natural PC is removing `ApplyIdentity` at `LandingGit.cs:311`
  (the configured-identity rebase then exits 128).

Performance: CP-1 13.5 s (141 tests), CP-2 106 s (slow classes NativeLinuxPolicyWitnessMutationTests 99 s and
TestClassificationPolicyTests 86 s, both pre-existing), build 482 s under a slot at load 12-17, nothing near 30
minutes; the two new git-spawning witnesses add about one second. OS witnesses: no OS-selected production branch
changed; the Linux probe (`IsPathRooted`, lstat symlink detection) is exercised on the production platform here,
the Windows desktop stays on the empty-setting path, and `File.CreateSymbolicLink` in the test matches 94 existing
uses in Antiphon.Tests. Input waits: none added. Live acceptance (one `/land/v2` from `/work/repos/antiphon`)
stays pending until the CARD-1775 compose includes this fragment and cutover runs.

--- review evidence ---
subjectTaskId: 93dff2d9-f56a-4007-8c15-a824193872b9
reviewedSourceSha: cef2481a855a317cfcb720a91e1f9a81513ffd19
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: 0f76e7b0a9379345684dbb4f5452073e53eb82f4..cef2481a855a317cfcb720a91e1f9a81513ffd19
