# CARD-1776 Review round 1 (Final/Full) of 0f76e7b0a9379345684dbb4f5452073e53eb82f4

Verdict: ONE BLOCKER (B-1), eight advisories. Subject `origin/feat/card-task-19293e15` tip
`0f76e7b0a9379345684dbb4f5452073e53eb82f4` (Code task 19293e15-c7e1-4cf1-94da-4a2816c9a483, Grok),
range `d116a618f1ddc048e5b8501b659cd535fd96deef..0f76e7b0a9379345684dbb4f5452073e53eb82f4`
(red `b80894e7f9c4f9a001f2708a5b16f12e86a3e5eb`, green `0f76e7b0a9379345684dbb4f5452073e53eb82f4`).
Read-only review: no product file changed; the only commit is this report. Nothing deployed or started.

## Re-run at the tip (checkpoint tool run 20261108-070729-6dad, one build, three rows)

Plan extract `.antiphon/review-c1776-plan.md` (ignored). Every build and row `slot=granted`;
`unlisted: none`; `outputs: deleted bin-c1776-rv/`; validate: `CHECKPOINT SOURCE VALID
source=0f76e7b0a9379345684dbb4f5452073e53eb82f4 rows=3` (exit 0).

CHECKPOINT CP-R1 commit=0f76e7b0a9379345684dbb4f5452073e53eb82f4 build=ok filter=/*/*/(ServerImagePushCredentialTests*)|(DockerStackContractTests*)|(ServerRepositoryMirrorScriptTests*)/* executed=131 passed=131 failed=0 skipped=0 trx=/work/worktrees/task-5b1ea772/.antiphon/checkpoints/20261108-070729-6dad/rows/CP-R1/run.trx slot=granted waited=0s dirty=0 source=0f76e7b0a9379345684dbb4f5452073e53eb82f4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R1 waited=0s executed=18.476s wall=18.564s
CHECKPOINT CP-R3 commit=0f76e7b0a9379345684dbb4f5452073e53eb82f4 build=reused filter=/*/Antiphon.Tests.Infrastructure/GithubCredentialHelperTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-5b1ea772/.antiphon/checkpoints/20261108-070729-6dad/rows/CP-R3/run.trx slot=granted waited=30s dirty=0 source=0f76e7b0a9379345684dbb4f5452073e53eb82f4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R3 waited=30s executed=21.684s wall=52.302s
CHECKPOINT CP-R2 commit=0f76e7b0a9379345684dbb4f5452073e53eb82f4 build=reused filter=/*/*/(TestClassification*)|(TestLaneCategoryGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessMutationTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/task-5b1ea772/.antiphon/checkpoints/20261108-070729-6dad/rows/CP-R2/run.trx slot=granted waited=30s dirty=0 source=0f76e7b0a9379345684dbb4f5452073e53eb82f4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-R2 waited=30s executed=157.272s wall=187.581s
PHASES CP-R1 slotWait=0s build=432.1067008s startup=8.1695139s testsWall=8.1722152s teardown=0.987967s hostWall=17.3297048s
BUDGET wall=719.716s execution=709.689s admissionOnly=10.027s

| Row | Plan row | Classes | Executed | Code claimed |
|---|---|---|---|---|
| CP-R1 | CP-4 (only CARD-1776 row in the plan; Min 28) | ServerImagePushCredentialTests 9, DockerStackContractTests 117, ServerRepositoryMirrorScriptTests 5 | 131/131 | 131 |
| CP-R2 | guard batch (Code named eight classes; `TestClassificationGuard` resolves to `Antiphon.TestSupport.TestClassificationGuardTests`) | the eight plus TestLaneCategoryGuardTests | 85/85 | 75 |
| CP-R3 | extra: the baked helper's own tests (allow-list host and path) | GithubCredentialHelperTests | 6/6 | not run |

Other checks: `check-evidence-diff.ps1` base..head commits=2 entries=0 violations=0 (exit 0);
`git merge-tree --write-tree origin/master HEAD` exit 0, tree `167d46f8b5dafda4a58ffaa63905bc3230bda061`
(origin/master `b1e129b44855a4c6682a3b1fecc8fb445be7e11a`); migrations none; policy JSON untouched.
Diff grep for `sslVerify|StrictHostKeyChecking|safe.directory|insecure|GIT_SSL_NO_VERIFY|askpass`: none.

## B-1 (blocker): the Linux server still cannot land, because the land rebase has no git identity

- Where: `docker-compose.server2-land-credential.yml` (environment and volumes), `docker/server/gitconfig`,
  plan D-8 (`git show origin/feat/card-task-2fafa091:docs/superpowers/plans/2026-11-08-card-1744-cutover-plan.md`,
  Decisions table) which names `GIT_CONFIG_GLOBAL` as part of the pattern the server image receives;
  `server/Application/Services/AgentTaskLandingProtocol.cs:282` runs
  `git -c rebase.autoStash=false -c rebase.updateRefs=false rebase <TargetBeforeSha>` in the server process.
- Failure: in the server image (pinned git 2.47.3, `USER 1654:1654`, aspnet user `app`, dotless container
  hostname, no `[user]` anywhere) every land whose task branch is not already on the target tip fails at the
  rebase with `fatal: unable to auto-detect email address (got 'app@<id>.(none)')`, exit 128, surfaced as
  `rebase_failed`. Reproduced in this runner container with the same git version and an identity-free
  environment (`GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null`, scratch `HOME`, author/committer vars
  unset): exit 128 with exactly that message. `grep -rn 'GIT_COMMITTER_NAME\|user.email' server/` finds nothing,
  so no code path supplies an identity; the desktop works only because the operator's global gitconfig does.
- Why it blocks: the card's stated outcome is "after cutover the Linux server can land"; D-8 explicitly lists
  `GIT_CONFIG_GLOBAL` (the runner's identity-file mount, CARD-0631 D-9 amended) in the pattern this slice
  gives the server, and the plan's post-check "One land ... from /work/repos/antiphon; receipt normal (proves
  D-8)" cannot pass with this fragment. Slice row S2 omitted it; the Code followed S2 and reported "Owed: none".
- Fix: in the fragment add `GIT_CONFIG_GLOBAL: /run/antiphon/gitconfig` and
  `- ${RUNNER_GIT_IDENTITY_FILE:?RUNNER_GIT_IDENTITY_FILE is required}:/run/antiphon/gitconfig:ro` (the same
  non-secret `[user]` file the runner mounts; do not bake it, per CARD-0631 D-9); pin both in
  `Land_credential_compose_mounts_the_token_directory_and_allow_list_by_variable` and in
  `docs/agent-credentials.md`; add a witness that `LandingGit` runs a non-fast-forward rebase with
  `GIT_CONFIG_NOSYSTEM=1`, `GIT_CONFIG_GLOBAL=<that file>` and a scratch HOME. An equivalent alternative is to
  set `GIT_AUTHOR_*`/`GIT_COMMITTER_*` from `GitSettings` in `LandingGit`'s `ProcessStartInfo`. The live
  acceptance (one `/land/v2` from `/work/repos/antiphon`) stays pending until cutover.

## Advisories (D-2: no Review rerun solely for these; fold into the B-1 repair where cheap)

- A-1 Probe bypass outside the `ILandingGit` singleton. `TaskProgressGit : LandingGit`
  (`TaskProgressGit.cs:16`, ctor passes no `IOptions<GitSettings>`, registered singleton `ITaskProgressGit`)
  runs `ls-remote`/`fetch` and `LocalTaskParkPublisher` pushes through it; `GitHubService.PushBranchAsync`
  (`GitHubService.cs:95`) uses `new LandingGit()` for `push origin <branch>`. On the server these skip
  `push_credential_absent` and fall to git's own refusal (`GIT_TERMINAL_PROMPT=0`, no password). No secret
  exposure; the land path itself is gated. The docs sentence "refuses the remote git command ... before git
  starts" overstates. Pass the options through `TaskProgressGit`; inject `ILandingGit` into `GitHubService`.
- A-2 The probe copies token bytes into the server heap. `ServerPushCredential.Readable` opens a `FileStream`
  with the default 4 KiB buffer and `ReadByte()` fills it with the token, which lives in a managed array until
  GC while the docs and XML comments say "reads presence only". Use `File.OpenHandle` +
  `RandomAccess.GetLength`/`RandomAccess.Read` of one byte (or `bufferSize: 0`), and say "opens for read, reads
  one byte". Not a leak path by itself (uid 1654 in the container can read the mount), but the server should
  not hold the secret.
- A-3 No composition witness. `AddSingleton<ILandingGit, LandingGit>()` relies on DI choosing the two-parameter
  ctor; a factory such as `new LandingGit(cardFiles)` would silently disable the probe in production and none of
  the Code's three mutants would notice (all method-level). Add a test that resolves `ILandingGit` from the
  Program-shaped registrations with `Git:PushCredentialTokenFile`/`PolicyPath` set to missing paths and asserts
  `push_credential_absent`.
- A-4 Image checks. The Dockerfile proves the helper line with `git config --file /etc/gitconfig`, which cannot
  catch a sysconfdir drift; the runner image proves `--system --show-origin ... file:/etc/gitconfig`. The
  copied `/etc/antiphon/github_known_hosts` is inert in the server image (no ssh client, no `core.sshCommand`,
  no ssh_config): HTTPS relies on the CA bundle, which is fine, but "pinned host keys" protects nothing here;
  drop the copy or document it as inert.
- A-5 Mirror script operability (`scripts/server2-repository-mirrors.ps1`). (a) Defaults `-TokenFile` and
  `-PolicyFile` are container paths that are `:ro` mounts in every container, so `-Execute` with defaults can
  never write the allow-list; the docs do not say which host and uid run it. (b) No explicit host guard (brief
  item 4 expected one): a desktop `-Execute` is refused only incidentally (`credential-absent`,
  `mirror-parent-missing`). (c) `Remove-Item -LiteralPath $PolicyFile` has no `-PathType Leaf` guard (an empty
  directory would be removed). (d) The script replaces the whole allow-list from the table, before the clones,
  dropping entries not in the table; keep `SERVER_PUSH_ALLOW_LIST` distinct from the runner's file.
  (e) `/work/repos/<name>` must be owned by uid 1654 or the server's git refuses `dubious ownership` (no
  `safe.directory` is set, correctly); the script neither runs as 1654, nor chowns, nor documents it.
  (f) clone/fetch use the executing host's credential config, not the server's helper. Positive points: `-WhatIf`
  default, `..`/volume/secret/`..`-escape refusals, exact `<root>/<slug>` path guard, no globs, no `rm -rf`, no
  docker calls, token never printed (tests assert the canary is absent).
- A-6 Half-set configuration arms refusal. A single stray `Git__PushCredentialTokenFile` on the desktop (no
  policy path) makes every land refuse `push_credential_absent`. Fail-closed, but silent until a land; an
  `IValidateOptions<GitSettings>` requiring both-or-neither would fail at startup instead.
- A-7 `ServerPushCredential.IsRemoteCommand` inspects `arguments[0]` only; `LandingGitStats.SubcommandIndex`
  already skips `-c k=v`/`-C` prefixes. No current remote call uses a prefix (grep), so reuse `SubcommandIndex`
  for robustness.
- A-8 Process. CP-4 was run as leased direct `dotnet run` (no checkpoint-tool receipt from Code; "native output,
  CARD-1629" is not a reason to skip the tool, which keeps its own cold output). This review's run supplies the
  validated receipt. The Code's guard list named `TestClassificationGuard`, which is `TestClassificationGuardTests`
  in `Antiphon.TestSupport`.

## Brief items

1. Secret hygiene: no token value, path content or credentialed URL in the diff, compose (paths only), image
   (helper, known_hosts, gitconfig are non-secret), tests (fake canary asserted absent from output) or docs.
   The git child env carries only `GIT_TERMINAL_PROMPT=0`/`GIT_OPTIONAL_LOCKS=0`; `git config` shows the helper
   path only. Mount `:ro`; host file `1654:1654` 0400 in a 0700 directory (agent-credentials.md:318) and the
   server runs as 1654, so it is readable. Allow-list is host AND path: helper requires `protocol=https`,
   `host=github.com`, normalised lower-case `owner/repo.git` exact match (directory prefix only with a trailing
   `/`), `useHttpPath = true`; a different host or an unlisted path gets no password (CP-R3 6/6). Caveat A-2.
2. Refusal order: `PushAsync`, `PushOwnedAsync`, `PushSourceOwnedAsync`, `DeleteSourceBranchAsync`,
   `ConfirmTargetContainsAsync` and `ExecuteAsync` for push/fetch/ls-remote/pull/clone refuse before
   `ConfirmedEndpointAsync` and before `StartProcess`; `Absent_credential_refuses_land_push_before_git_starts`
   counts zero starts through the `StartProcess` seam. Desktop: both empty, including `new LandingGit()`, returns
   null before any I/O; the only added work on that path is the pure `IsRemoteCommand` check. Caveats A-1, A-6.
3. Pinned keys / HTTPS-only: no insecure setting anywhere in the diff; gitconfig has no `pushInsteadOf`; the
   image has no ssh client so an SSH remote fails closed. Caveat A-4.
4. Mirror script: see A-5; it never touches antiphon_pgdata, any compose project or docker.
5. Witnesses: the red commit holds only the five text-pin tests, which fail by construction at base (files and
   text absent) with the existing `DockerStackDocuments` helper, so assertion-red is plausible. The four
   behavioural tests arrived with the fix; each distinguishes the probe (the `StartProcess` seam, the direct
   `Refusal` matrix, `ls-remote` through `RunAsync`), none self-compares. The Code's three mutants are Code
   witnesses only; every control stays pending for method-scoped SourceLanding Mutation. `DockerStackContractTests`
   unchanged and green; `ServerRepositoryMirrorScriptTests` runs the real script through `pwsh` and asserts
   exit codes, reasons, no clone directory, no policy write and no canary in output.

Performance: rows 18 s / 157 s / 22 s, build 432 s under a slot, nothing near 30 minutes; the new classes are
under 10 s as the plan budget states; the 20 s per-spawn `pwsh` bound is about 10x the observed spawn. OS
witnesses: no OS-selected production branch changed (the probe is portable; `Path.IsPathRooted` is exercised
on Linux here, the Windows desktop stays on the unconfigured path). Input waits: none added.

--- review evidence ---
subjectTaskId: 19293e15-c7e1-4cf1-94da-4a2816c9a483
reviewedSourceSha: 0f76e7b0a9379345684dbb4f5452073e53eb82f4
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: d116a618f1ddc048e5b8501b659cd535fd96deef..0f76e7b0a9379345684dbb4f5452073e53eb82f4
