# CARD-0812: per-repository runner mirrors for phone-home Worktree tasks

Date: 2026-09-29. Stage: Plan, with the verification design folded in (the brief asked for the
closed `### Checkpoints` table). Next: Code. Baseline: `15b66136a` on `feat/card-task-459aa7ef`
(clean tree). Card `a4287483-d4d4-4f8b-9f89-f2ba55766ba2`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`, read with `scripts/card.ps1 get CARD-0812`.

## Outcome and scope

A phone-home runner (server2) mirrors the task branch of **any admitted repository**, not only
Antiphon. The server names the task's repository on the mirror request, taken from the desktop
checkout's own `origin`; the runner keeps one blobless checkout per repository under its
repositories root, verifies that an existing checkout really is that repository, refuses a
repository its runner-owned allow-list does not admit, refuses a secondary repository its deploy
key cannot push to, and creates every mirror worktree under the same flat `/work/worktrees/`
root it uses today. The desktop worktree stays canonical and the branch on origin stays the unit
of exchange (CARD-0604 D-15); nothing here changes landing, settlement sync, spill containment,
launch cwd admission or the Mutation verification lane.

Out of scope, stated so Code does not drift into them:

- Provisioning push credentials for non-Antiphon repositories on server2 (the deploy key is
  repo-scoped to `michal-ciechan/Antiphon`; see D-8 and the follow-up card).
- Repositories that are not anonymously fetchable over HTTPS (`gym-stat` and `slides` answer 404
  anonymously; `markdown-package`, the motivating case, answers 200).
- The Mutation verification snapshot (`RunnerVerificationWorkspace.cs`), which stays on the
  primary repository; a non-Antiphon SourceLanding Mutation on server2 is a separate card.
- Runner image, compose and `deploy-parent` changes. None are needed for this card.

## Ground truth

What the card asserts against what the code does at the baseline. Line numbers are at `15b66136a`.

| Card assumption | What the code does | Verdict |
|---|---|---|
| `DefaultCloneSource` is a process-wide constant (`RunnerWorkspaceService.cs:36`) | `internal const string DefaultCloneSource = "https://github.com/michal-ciechan/Antiphon.git"` at line 36; the public constructor (line 40) always passes it | Confirmed |
| `RunnerRepository` defaults to one fixed path (`PhoneHomeSettings.cs`) | `PhoneHomeSettings.RunnerRepository = "/work/repos/antiphon"` (line 34); the server mirrors the same default in `PhoneHomeRunnerSettings.RunnerRepository` (line 22), used only by the verification lane and a policy getter | Confirmed |
| `Workspace()` constructs one service from `_settings.RunnerRepository` (`PhoneHomeCommandDispatcher.cs:108`) | Lines 107-108: `_workspace ??= new RunnerWorkspaceService(_settings.RunnerRepository, _settings.AllowedCwd)`; every `WorkspaceMirror`, `WorkspaceRemove`, spill and verification operation goes through it | Confirmed |
| No repository identity in the mirror wire protocol | `PhoneHomeWorkspaceMirrorRequest(string Branch, string Sha, string Name)` (`PhoneHomeContracts.cs:181`); `RemoteWorkspaceService.MirrorAsync` (lines 115-121) sends exactly those three | Confirmed |
| The mirror fetch fails structurally and backoff never helps | `MirrorAsync` runs `git fetch origin <branch>` inside `_repository` (line 93) and refuses `UnsupportedTarget` 409; `RemoteWorkspacePreparer.RunAsync` (lines 160-187) records a Warning and pushes `DispatchNotBeforeAt` out by `Backoff` (base 30 s, cap 900 s), so the task stays Queued and retries forever | Confirmed |
| Fix 1 belongs in `RemoteWorkspacePreparer.cs` / `RemoteWorkspaceService.MirrorAsync` | `PushBranchAsync` already runs git in the desktop worktree (lines 99-107) and is the natural place to read `origin`; the preparer only orchestrates and formats failures | Confirmed, with the read placed in `PushBranchAsync` |
| Fix 2 belongs in `EnsureRepositoryAsync` / `MirrorAsync` | Also `RemoveAsync` (line 175 runs `worktree remove` in `_repository`) and `worktree prune` (line 178). `WriteSpillAsync` is repository-agnostic. The verification half of the partial class (`RunnerVerificationWorkspace.cs`) uses `_repository` 8 times and stays primary-only | Card is incomplete: removal must resolve the owning checkout too |
| The project's repository URL is known to the server | `Project.GitRepositoryUrl` is empty for Antiphon and most projects (`GET /api/projects` on 2026-09-29; only `gym-stat`, `markdown-package`, `slides` carry one) | Wrong as a source of identity; the desktop checkout's `origin` is the only reliable one |
| Not stated: the desktop `origin` is HTTPS | This worktree's `origin` is `https://...Antiphon.git` for fetch and `git@github.com:...Antiphon.git` for push; the main checkout may be SSH for both. `TaskProgressGit.ReadEndpointAsync` reads the push URL (`remote get-url --push --all origin`) | Identity needs normalisation of SSH and HTTPS spellings |
| Not stated: `EnsureRepositoryAsync` trusts whatever `.git` it finds | Lines 129-133 return as soon as `.git` exists; the origin is never compared to the clone source | Latent gap; closed by D-4 for every request that names a repository |
| Not stated: the runner's push credential works for any repository | `docker/session-runner-grok/ssh_config` names one `IdentityFile /run/antiphon/deploy-key`; `gitconfig` rewrites every `https://github.com/` push to `git@github.com:`; `docs/agent-credentials.md` §5 says the key is a repo-scoped deploy key for `michal-ciechan/Antiphon` only, and GitHub binds a deploy key to exactly one repository | A secondary mirror can fetch and work but cannot push until a per-repository credential exists (D-8) |
| Not stated: `RemoveAsync` protects unpushed work | It refuses only a dirty tree (`status --porcelain`, lines 167-172); committed-but-unpushed commits are removed with `worktree remove --force` | Reason D-8 probes push access before creating a secondary mirror |
| Not stated: Claude and Codex trust on a non-Antiphon mirror | `seed-claude-onboarding.mjs:20` trusts `/work/repos/antiphon` and `/work/worktrees`; every mirror is under `/work/worktrees`. `init-state.sh:61` trusts Codex only for `/work/repos/antiphon`; `CodexStartupReadiness` (lines 463-492) accepts a trust prompt during readiness | Expected to work without image changes; the post-land live check observes it (see Post-land) |
| Not stated: deploy verification | `scripts/c590-remote.sh verify_runner_checkout` seeds and verifies only `PhoneHome__RunnerRepository` against `RUNNER_CHECKOUT_ORIGIN` (= `DefaultCloneSource`), pinned by `RemoteScriptContractTests` | Unchanged; secondary checkouts are lazily cloned and never seeded or verified by deploy-parent (docs say so in S3) |
| Not stated: wire compatibility | `PhoneHomeFraming.Json` is `JsonSerializerDefaults.Web`, which ignores unknown properties | An old runner ignores the new field and mirrors into its primary, exactly today's behaviour; a new runner honours it (D-6) |

Platform facts read on 2026-09-29: `GET /api/runner-defaults` has `globalRunnerId: server2`
(revision 2, operator reason: default all work to server2; desktop only for Windows work).
`GET /api/session-runners` lists `desktop` (windows, capacity 2 delegated tasks) and `server2`
(linux, capacity 10 sessions, features without any workspace-repository capability).

## Decisions

### D-1: The repository identity is the desktop checkout's `origin`, normalised to anonymous HTTPS

`RemoteWorkspaceService.PushBranchAsync` reads `git remote get-url origin` in the task's desktop
worktree (the same place it already reads HEAD and pushes) and normalises it with a shared helper
`RepositoryCloneSource` in `Antiphon.SessionRunner.Contracts`, so the server and the runner
compute one identity from any spelling:

- `git@HOST:OWNER/NAME(.git)`, `ssh://git@HOST[:port]/OWNER/NAME(.git)`, `https://HOST/OWNER/NAME(.git)(/)`
  all become `https://<host lower>/<path lower>.git`.
- Anything else (a local absolute path, `file://`) is left as typed, trimmed. Tests use local
  scratch origins through this branch; production never does.
- An unreadable or ambiguous `origin` fails the push step (`Pushed=false`, warning "the desktop
  worktree origin could not be read"). It is not a fallback to the primary: a silent default is
  precisely the bug this card fixes.

Rejected: `Project.GitRepositoryUrl` (empty for most projects, and not necessarily where the desktop
pushes); a server-chosen slug (two sources of truth, and the runner still needs the URL to clone);
the push URL (already SSH-rewritten on some desktops; the fetch URL normalises identically).

### D-2: One optional field on the existing mirror request; removal derives its checkout

`PhoneHomeWorkspaceMirrorRequest(string Branch, string Sha, string Name, string? Repository = null)`.
JSON name `repository`. Null means the runner's primary repository with today's behaviour, so an
older server keeps working against a newer runner. `PhoneHomeWorkspaceRemoveRequest` is unchanged:
the runner resolves the mirror's owning checkout from `git -C <mirror> rev-parse
--path-format=absolute --git-common-dir` and runs `worktree remove --force` and `worktree prune`
there. A mirror whose common dir is neither the primary nor under the repositories root is refused
(`UnsupportedTarget`), never removed by guesswork. A mirror directory that no longer exists returns
`Removed=true` after a best-effort `worktree prune` in the primary and every `<root>/*/.git` checkout.

Rejected: a new `WorkspaceMirrorV2` operation (heavier; the optional field plus the D-6 feature
flag gives the same compatibility); carrying the repository in the mirror name (the name is a
directory the runner creates and its `task-<8 hex>` pattern is a path-traversal boundary).

### D-3: Flat worktree root, one checkout per repository beside the primary

Mirror paths stay `<AllowedCwd>/worktrees/task-<8 hex>`: mirror names are fleet-unique, and the
dispatcher's `IsAdmittedCwd`, the service's `IsUnderRoot`, and the spill containment all keep
working untouched. Checkouts: the primary stays at `PhoneHome:RunnerRepository`
(`/work/repos/antiphon`); a secondary repository lives at `<RunnerRepositoriesRoot>/<name>` where
the root defaults to the parent directory of `RunnerRepository` (`/work/repos`, created and owned
by `init-state.sh`) and `<name>` is the identity's last path segment, lower case, without `.git`,
required to match `^[a-z0-9][a-z0-9._-]{0,63}$` and not be `.` or `..`. Two admitted repositories
with the same name and different owners collide on the path; the second is refused by D-4 with a
message naming both origins. Acceptable while the default allow-list is one owner.

Rejected: `<owner>-<name>` (would put the primary out of line with the path deploy-parent seeds
and verifies); a persisted URL-to-path registry (extra state; the checkout's own `origin` already
is the identity).

### D-4: An existing checkout must prove it is the requested repository

When the request names a repository, the runner reads `git remote get-url origin` in the resolved
checkout, normalises it with the same helper, and refuses `phone_home_repository_mismatch` (409,
new `PhoneHomeProblemTypes.RepositoryMismatch`) when it differs, touching nothing. This applies to
the primary too, so a request that names Antiphon is checked against the runner's configured
primary clone source. A request with `Repository` null (legacy server) keeps today's trust-the-`.git`
behaviour, which is what lets every existing `RunnerWorkspaceServiceTests` method pass unchanged.

### D-5: Runner-owned admission of a request-supplied identity

`PhoneHome:AllowedCloneSources`: a list of `https://` prefixes ending in `/`; default
`["https://github.com/michal-ciechan/"]`. A named repository is admitted when its normalised
identity starts with one of them (ordinal) or equals the primary clone source; otherwise
`phone_home_repository_not_admitted` (409, new `RepositoryNotAdmitted`) before any git process
starts. `PhoneHome:RunnerCloneSource` (default `RunnerWorkspaceService.DefaultCloneSource`, the
constant stays where `RemoteScriptContractTests` pins it) names the primary's identity.
`Validate()` refuses a prefix that is not an absolute `https://` URL ending in `/`, a root that is
not POSIX absolute, and a clone source that does not normalise.

This keeps CARD-0631 D-6's intent (the runner decides where it clones from) while letting the
server choose *which* admitted repository. Rejected: fully request-controlled (a mis-configured or
compromised server could make the runner clone and later push anywhere); a runner-configured map
of every repository (each new project would need a compose change and a runner restart, which is
the card's complaint in another form).

### D-6: A capability flag, and legacy runners fail the same way with a better message

The runner advertises `RunnerCapabilityFeatures.WorkspaceRepositoryV1 = "workspaceRepositoryV1"`
from `PhoneHomeRuntimeAdapter.Capabilities()`; the static feature list moves to an internal
`PhoneHomeRuntimeAdapter.StaticFeatures` so a unit test can pin it without a runtime double. The
server always sends `Repository`. When `ISessionRunnerDirectory.DescribeAsync(runnerId)` shows the
runner lacks the feature and the mirror fails, `RemoteWorkspacePreparer` appends to the Warning:
"runner '<id>' does not advertise workspaceRepositoryV1 and mirrors only its primary repository;
upgrade it (CARD-0727) or pin another runner". Antiphon tasks on a legacy runner keep working
(the runner ignores the field), and the backoff-and-retry shape is unchanged so a rolling upgrade
resolves the hold without a re-dispatch.

Rejected: refusing to send to a legacy runner (breaks Antiphon tasks during the upgrade window);
a new server-side per-runner `PrimaryCloneSource` setting to pre-classify the failure (more
config and a validator for a short-lived window).

### D-7: The clone shape, fetch, tip check and worktree creation are unchanged per checkout

`EnsureRepositoryAsync(checkoutPath, cloneSource)` is the existing method with two parameters:
same `git clone --filter=blob:none --no-checkout <source> <path>` from the parent, same refusals
for a file or an occupied directory, same idempotence. `MirrorAsync` then fetches, checks
`FETCH_HEAD` equals the sha (G-27), and runs `worktree add -B` in the resolved checkout. Anonymous
HTTPS fetch stays the only read path; a private repository fails at fetch with today's typed
refusal, and that is documented as out of scope.

### D-8: A secondary repository must be pushable before its mirror exists

Default on (`PhoneHome:ProbeSecondaryRepositoryPushAccess = true`). After D-4 and before the
fetch, for a non-primary repository the runner reads `git remote get-url --push origin` in the
checkout (git applies the image's `pushInsteadOf`, so this is the SSH URL the session's `git push`
will use) and runs `git ls-remote --exit-code <push-url> HEAD` with the deploy key. A non-zero exit
is `phone_home_repository_push_unauthorized` (409, new `RepositoryPushUnauthorized`) whose message
names the repository and the remedy: register a push credential for it on server2. No mirror is
created.

Why: without it a markdown-package session on server2 would fetch, work, commit, fail to push, and
`RemoveAsync` (clean tree, unpushed commits) would delete the only copy at retirement. GitHub
answers a deploy key that is not registered on the repository with "Repository not found", so the
probe is a true authorisation check; a read-only deploy key would pass it and still fail the push,
which is acceptable because the runner's keys are write keys by policy. The primary is not probed:
deploy-parent verifies it, and a probe on every Antiphon mirror would add a network round trip and
a new transient failure to the hot path. In tests the scratch checkouts have no rewrite, so the
push URL is the local origin and the probe passes; the refusal is exercised through the existing
process-start seam.

Operator follow-up (not this card): a GitHub deploy key is bound to one repository, so server2
needs either per-repository keys (compose secrets, `Host github.com-<name>` aliases in a mounted
`ssh_config`, and `url."git@github.com-<name>:owner/name".pushInsteadOf` entries in the mounted
`GIT_CONFIG_GLOBAL`, which outranks `/etc/gitconfig`) or a machine-user key with access to the
listed repositories (a least-privilege change to `docs/agent-credentials.md` §5). File it as
"server2 runner: push credential for non-Antiphon repositories" and reference this plan.

### D-9: Server-side settings and the verification lane are untouched

`PhoneHomeRunnerSettings.RunnerRepository` keeps its meaning (the primary path the verification
lane needs). `RunnerVerificationWorkspace.cs` keeps using the primary checkout and
`_publishedBranch`. The dispatcher's `Workspace()` builds one service from a
`RunnerRepositoryPolicy` record (`PrimaryPath`, `PrimaryCloneSource`, `RepositoriesRoot`,
`AllowedCloneSources`, `ProbeSecondaryPushAccess`) derived from `PhoneHomeSettings`; the existing
public constructor `(repository, allowedCwd, timeout, publishedBranch)` and the internal test
constructor keep compiling by building a policy whose allow-list is the given clone source.

## Implementation slices

**S1: contracts and runner (`runner` area).**

- `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`: `Repository` on the mirror request;
  three new `PhoneHomeProblemTypes` (`RepositoryNotAdmitted`, `RepositoryMismatch`,
  `RepositoryPushUnauthorized`).
- `src/Antiphon.SessionRunner.Contracts/RepositoryCloneSource.cs` (new): `TryNormalize`,
  `TryDeriveName`, `IsAdmitted(identity, prefixes, primary)`.
- `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`: `WorkspaceRepositoryV1`.
- `src/Antiphon.SessionRunner/PhoneHomeSettings.cs`: `RunnerCloneSource`, `RunnerRepositoriesRoot`,
  `AllowedCloneSources`, `ProbeSecondaryRepositoryPushAccess`, validation.
- `src/Antiphon.SessionRunner/RunnerRepositoryPolicy.cs` (new) and `RunnerWorkspaceService.cs`:
  resolve the target checkout (D-3, D-5), `EnsureRepositoryAsync(path, source)`, origin check
  (D-4), push-access probe (D-8), removal by common dir (D-2), prune sweep.
- `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`: `Workspace()` from the policy.
- `src/Antiphon.SessionRunner/PhoneHomeRuntimeAdapter.cs`: `StaticFeatures` including the flag.
- Tests: `tests/Antiphon.SessionRunner.Tests/RepositoryCloneSourceTests.cs` (new);
  `RunnerWorkspaceServiceTests.cs` (+4); `PhoneHomeCommandDispatcherTests.cs` (+1);
  `RunnerCapabilitiesTests.cs` (+1). Named in the inventory below.

**S2: server (`server/Application/Services/RemoteWorkspace*.cs`).**

- `RemoteWorkspaceService.cs`: `PushBranchAsync` reads and normalises `origin`; `RemotePushResult`
  gains `Repository`; `MirrorAsync(task, sha, repository)` sends it;
  `SupportsRepositoryMirrorsAsync(runnerId)` reads the descriptor's features.
- `RemoteWorkspacePreparer.cs`: passes the repository; appends the D-6 hint on failure.
- Tests: `tests/Antiphon.Tests/Application/RemoteWorktreeMirrorTests.cs` (+2; `ScriptedGit` answers
  `remote`); `RemoteWorkspacePreparerTests.cs` (+1; `PushOnlyGit` answers `remote`).

**S3: docs (`docs` area, weight allow).** `docs/docker-stack.md` (runner checkout section: secondary
checkouts are lazily cloned beside the primary, never seeded or verified by deploy-parent, and the
three new refusals), `docs/testing-and-build.md` (server2 runner paragraph: `-Worktree` for any
project whose origin is under the allow-list and anonymously fetchable; push needs a credential),
`docs/session-runtime-invariants.md` (one invariant: a runner mirror is created in the checkout
whose origin is the task's own repository identity; an unadmitted repository, a checkout at another
origin, or an unpushable secondary repository is a typed refusal, never a fetch into the wrong
repository), `docs/agent-credentials.md` §5 (the deploy key covers Antiphon only; secondary
repositories need their own push credential; follow-up card). No AGENTS.md change: the owners
already route here. `docs/cards/` is generated; do not edit it.

Commit and push each slice as it completes. Scope for the Code dispatch:
`-Scope runner,server/Application/Services/RemoteWorkspace*.cs,tests/Antiphon.Tests/Application/RemoteWork*.cs,docs`.

## Verification design

No builds or tests ran during Plan; counts below are read from the source at the baseline
(`[Test]` methods, `[Arguments]` expansions) plus the design's additions. Update a count only for an
explained source change and report the actual roster.

### Coverage inventory

| ID | Evidence | Class and method |
|---|---|---|
| V-1 | Every admitted spelling normalises to one identity; non-repositories are refused; names derive or refuse; allow-list matching is ordinal and slash-bounded | `RepositoryCloneSourceTests`: `Normalizes_every_admitted_spelling_to_one_https_identity` (6 arguments), `Refuses_spellings_that_are_not_a_repository` (5), `Derives_a_directory_name_or_refuses` (6), `Allow_list_prefix_matching_is_ordinal_and_slash_bounded` (1): 18 results |
| V-2 | A second repository is cloned beside the primary with the pinned argv, both mirrors sit flat under `worktrees/`, a same-sha replay reuses the mirror, and removing the secondary mirror runs `worktree remove` and `prune` in its own checkout | `RunnerWorkspaceServiceTests.Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout` |
| V-3 | A repository outside the allow-list is refused before any git process starts (`starts` empty) | `RunnerWorkspaceServiceTests.Mirror_refuses_a_repository_outside_the_allowed_clone_sources` |
| V-4 | An existing checkout at the derived path whose origin is another repository is refused `phone_home_repository_mismatch`, untouched; the same request naming the primary against a primary checkout passes | `RunnerWorkspaceServiceTests.Mirror_refuses_an_existing_checkout_whose_origin_is_another_repository` |
| V-5 | A secondary repository whose push probe fails (process-start seam makes `ls-remote` fail) is refused `phone_home_repository_push_unauthorized` with no mirror directory; the same repository with the probe disabled mirrors | `RunnerWorkspaceServiceTests.Mirror_refuses_a_secondary_repository_the_deploy_key_cannot_push_to` |
| V-6 | The dispatcher refuses a mirror naming an unadmitted repository with the typed error frame and no process; the adapter advertises `workspaceRepositoryV1` | `PhoneHomeCommandDispatcherTests.Workspace_mirror_naming_an_unadmitted_repository_is_refused_before_any_git_runs`; `RunnerCapabilitiesTests.Phone_home_adapter_advertises_workspaceRepositoryV1` |
| V-7 | The server reads the desktop `origin`, normalises SSH and HTTPS spellings identically, and the mirror request carries the identity; an unreadable origin fails the push step without inventing a repository | `RemoteWorktreeMirrorTests.Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity`, `RemoteWorktreeMirrorTests.Push_refuses_when_the_desktop_origin_cannot_be_read` |
| V-8 | Through the real preparer and scripted peer: the `WorkspaceMirror` payload has `repository`; when the peer registered without the feature and answers an error, the Warning event names `workspaceRepositoryV1` and the task stays Queued with backoff | `RemoteWorkspacePreparerTests.A_mirror_request_carries_the_desktop_origin_and_a_legacy_runner_failure_names_the_missing_feature` |
| R-1 | The 15 existing runner workspace methods (mirror, clone-when-absent with pinned argv, sha mismatch, admission-not-crash, name shaping, dirty-tree and outside-root removal, four spill containment cases, four verification cases) still pass with `Repository` null | `RunnerWorkspaceServiceTests` existing roster |
| R-2 | The 41 existing dispatcher methods and 5 capability methods still pass (cwd admission, workspace ops under allowed cwd, custody gates, provider probes, launch generations) | `PhoneHomeCommandDispatcherTests`, `RunnerCapabilitiesTests` existing rosters |
| R-3 | Settlement sync, non-fast-forward and dirty-tree refusals, push failure reporting and the mirror name are unchanged; the preparer's claim/backoff/lease/starvation behaviour is unchanged | `RemoteWorktreeMirrorTests` (6 existing), `RemoteWorkspacePreparerTests` (18 existing results) |
| R-4 | The deploy contract still pins `PhoneHomeSettings().RunnerRepository` and `RunnerWorkspaceService.DefaultCloneSource`, and the seed and verify blocks are unchanged | `RemoteScriptContractTests` (28 results) |

### Red and green expectations per checkpoint

- **CP-1 red before S1:** `RepositoryCloneSourceTests` and the four new workspace methods do not
  compile (no helper, no `Repository` field, no policy); the new dispatcher and capability tests
  fail on the missing problem type and feature. After S1 every listed method passes and the 15 + 41
  + 5 existing methods are unchanged. A new method that passes against the baseline production line
  is a stub (rule 4).
- **CP-2 red before S2:** the two new `RemoteWorktreeMirrorTests` methods fail because
  `PushBranchAsync` never runs `remote get-url` and the request has no `repository`; the new preparer
  test fails because the Warning lacks the hint. After S1-S2 every listed method passes; the six
  existing mirror tests and 18 existing preparer results are unchanged once `ScriptedGit` and
  `PushOnlyGit` answer `remote`.
- **S3 has no checkpoint row.** Review reads the docs diff against D-2, D-5 and D-8.

### Platform per row

Both rows are platform-neutral: `RunnerWorkspaceServiceTests` already runs real git and symlink
cases on Windows and Linux, and the preparer tests use the test project's shared Postgres. Run them
on whichever lane the orchestrator dispatches Code to (the runtime default is `server2`, Linux;
omit `-Platform`). On Linux the checkpoint tool adds `UseAppHost=false` itself. Do not co-schedule
`tests/Antiphon.Agents.Pty.Tests`.

### Positive controls for the later Mutation stage

Method-scoped, red then restored, not run by Code:

- PC-1: delete the allow-list check in the runner's target resolution; expect
  `Mirror_refuses_a_repository_outside_the_allowed_clone_sources` red (a git process starts).
- PC-2: skip the D-4 origin comparison; expect
  `Mirror_refuses_an_existing_checkout_whose_origin_is_another_repository` red.
- PC-3: make `PushBranchAsync` return `Repository = null`; expect
  `Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity` red.
- PC-4: stop lower-casing the host in `RepositoryCloneSource.TryNormalize`; expect the
  normalisation arguments of `Normalizes_every_admitted_spelling_to_one_https_identity` red.
- PC-5: drop `WorkspaceRepositoryV1` from `StaticFeatures`; expect
  `Phone_home_adapter_advertises_workspaceRepositoryV1` red.

### Execution

Commit S1, run CP-1; commit S2, run CP-2; commit S3. One tool run per committed slice group:

```text
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-29-card-0812-per-repository-runner-mirror-plan.md --rows CP-1 --max-wait 570s
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-29-card-0812-per-repository-runner-mirror-plan.md --rows CP-2 --max-wait 570s
```

After exit 75 call `wait <run-id> --max-wait 570s` until the exit is not 75; never end the turn
while a run is live. A tool bootstrap build, if needed, goes through `scripts/build-slot.ps1` and
releases its lease before the executor starts. Report each `CHECKPOINT` line with its counts and
reruns, and every unlisted command with a reason. In the Markdown cells below `\|` is a literal
filter OR; the executable filter has a plain `|`.

### Cost

Ordinary Code verification floor: 8 + 12 = **20 minutes** (two isolated builds; `Antiphon.Tests`
is the slow one). Authoring and audit: about 2.5-3 hours across three slices. Code `-ExpectAbout`:
3.5 hours. Add the observed build-slot wait on a busy host (`GET :8080/build-slots` on server2).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c812-a/` | runner-repository | `/*/*/(RunnerWorkspaceServiceTests*)\|(RepositoryCloneSourceTests*)\|(PhoneHomeCommandDispatcherTests*)\|(RunnerCapabilitiesTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6, R-1, R-2 | all 85 results: 19 workspace (15 + 4) + 18 clone-source + 42 dispatcher (41 + 1) + 6 capabilities (5 + 1); 0 failed, 0 skipped | 85 | 8 |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c812-b/` | server-identity | `/*/*/(RemoteWorktreeMirrorTests*)\|(RemoteWorkspacePreparerTests*)\|(RemoteScriptContractTests*)/*` | V-7, V-8, R-3, R-4 | all 55 results: 8 mirror (6 + 2) + 19 preparer (18 + 1) + 28 script contract; 0 failed, 0 skipped | 55 | 12 |

## Post-land server activation and live check

Landing publishes; it does not activate. After the land: `git pull --rebase` in the main checkout,
`scripts/restart-apphost.ps1`, confirm `GET /api/version` is the landed SHA. The runner half
reaches server2 through the documented rolling upgrade (CARD-0727, `server2-temp`); until then
`GET /api/session-runners` shows `server2` without `workspaceRepositoryV1` and a non-Antiphon
`-Worktree` task holds Queued with the D-6 hint in its Warning. Once the runner advertises the
feature, the acceptance is one real `scripts/delegate.ps1 -Worktree -Runner server2` task on the
markdown-package board: expect either a running session in `/work/worktrees/task-<id>` whose
checkout is `/work/repos/markdown-package`, or the typed `phone_home_repository_push_unauthorized`
Warning if the push credential follow-up has not landed yet. Read the first Claude or Codex launch's
readiness evidence for a trust dialog; if one appears, a one-line follow-up adds the repositories
root to `seed-claude-onboarding.mjs` `trustPaths`.

## Handoff

Code implements S1, S2, S3 in order from this plan, committing and pushing each slice, and runs
CP-1 and CP-2 as the closed list with the checkpoint tool. Two operator defaults to confirm or
change before or after Code, neither blocking it: the allow-list default
`https://github.com/michal-ciechan/` (D-5) and the secondary push probe on by default (D-8). File
the follow-up card for per-repository push credentials on server2 (D-8) when Code starts.
