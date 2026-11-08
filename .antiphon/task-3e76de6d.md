# CARD-1787 Review round 1 (Final/Full of the brief's named selection) of f5d5754739c10516d0aa27830d3214225cf3aa5e

Verdict: CLEAN. No blocker and no regression. Four advisories (A-1 to A-4) under D-2: no rerun and no Code
repair solely for them; they go to cards.

Subject `origin/feat/card-task-c3f172ad` tip `f5d5754739c10516d0aa27830d3214225cf3aa5e`
(Code task c3f172ad-add7-4ccb-98ad-639fdf9fcb87, Grok, Succeeded; deliverableRef = this SHA).
Reviewed range `57bbfc99d1810c99f2bf6e1a928427918c05d1fd..f5d5754739c10516d0aa27830d3214225cf3aa5e`
(red `b2290254835cc545ee4ae299ff11b26398eed968` tests only, fix `f5d5754739c10516d0aa27830d3214225cf3aa5e`;
4 files +317/-35: ServerGitIdentity.cs, LandingGit.cs, ServerImagePushCredentialTests.cs, docs/agent-credentials.md).
Read-only review: the only commit is this report. The review mutants below were applied to the worktree, built,
run and restored (`git status` clean before this commit); nothing deployed, started or landed.

## Re-run at the tip (checkpoint tool run 20261108-194244-8400, one build, two rows)

Plan extract `.antiphon/review-c1787-r1-plan.md` (ignored). Build and both rows `slot=granted`; `unlisted: none`;
`outputs: deleted bin-c1787-r1/`; validate: `CHECKPOINT SOURCE VALID source=f5d5754739c10516d0aa27830d3214225cf3aa5e rows=2` (exit 0).

CHECKPOINT CP-1 commit=f5d5754739c10516d0aa27830d3214225cf3aa5e build=ok filter=/*/*/(ServerImagePushCredentialTests*)|(GithubCredentialHelperTests*)|(DockerStackContractTests*)/* executed=157 passed=157 failed=0 skipped=0 trx=/work/worktrees/task-3e76de6d/.antiphon/checkpoints/20261108-194244-8400/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=f5d5754739c10516d0aa27830d3214225cf3aa5e sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=10.166s wall=10.732s
PHASES CP-1 slotWait=0s build=385.3024614s startup=7.8157732s testsWall=0.3049267s teardown=0.7154188s hostWall=8.8364185s
CHECKPOINT CP-2 commit=f5d5754739c10516d0aa27830d3214225cf3aa5e build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=52 passed=52 failed=0 skipped=0 trx=/work/worktrees/task-3e76de6d/.antiphon/checkpoints/20261108-194244-8400/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=f5d5754739c10516d0aa27830d3214225cf3aa5e sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=15s executed=121.479s wall=136.822s
PHASES CP-2 slotWait=15s build=0s startup=18.533354s testsWall=101.3234926s teardown=0.4469399s hostWall=120.303787s
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 101s tests=41 (CP-2)
BUDGET wall=643.991s execution=639.206s admissionOnly=4.786s

| Row | Classes | Executed | Brief |
|---|---|---|---|
| CP-1 | ServerImagePushCredentialTests 34 (13 + 21 new results: 16 `[Arguments]` cases + 5 tests), GithubCredentialHelperTests 6, DockerStackContractTests 117 | 157/157 | 34 named; the two regression classes |
| CP-2 | guard batch, own process: TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests, SlowTestTripwireTests, NativeLinuxPolicyWitnessTests | 52/52 | brief filter verbatim |

Fresh identities: every new result is `outcome="Passed"` in `rows/CP-1/run.trx`: the 16
`Identity_file_key_outside_user_name_and_email_is_invalid_before_git_starts(<label>, ...)` cases (credential,
credential-url, include, include-if, core, core-hooks, alias, url, user-extra-key, user-bare-key, user-subsection,
user-dotted-subsection, key-before-section, key-on-header-line, continuation, unclosed-header),
`Quoted_user_subsection_is_not_read_as_the_user_section`, `Identity_file_written_by_git_config_and_case_variants_stay_usable`,
`Configured_identity_outranks_inherited_git_identity_and_config_variables`,
`Desktop_without_an_identity_file_keeps_the_inherited_git_environment`,
`Credentials_doc_records_the_identity_allow_list_and_environment_scrub`.

Other checks: `check-evidence-diff.ps1 -BaseRef 57bbfc99d1810c99f2bf6e1a928427918c05d1fd -HeadRef f5d5754739c10516d0aa27830d3214225cf3aa5e`
commits=2 entries=0 violations=0 (exit 0); `git merge-tree --write-tree origin/master HEAD` exit 0, tree
`060ccf66f8251d1bc5e90aeef805f2eb74c5c2dc` against `origin/master` `57bbfc99d1810c99f2bf6e1a928427918c05d1fd` (the base);
migrations none; policy JSON untouched; the product diff carries no helper, URL rewrite, token or file text.

## Mutants (review evidence; every control stays pending for method-scoped SourceLanding Mutation)

Three independent mutants in `ServerGitIdentity.cs`, one native build (`build-slot.ps1`, 4m10s) and one leased
`dotnet run --no-build` of `/*/*/ServerImagePushCredentialTests/*`: 34 executed, 7 failed, 27 passed. Restored,
rebuilt (3m01s) and rerun: 34/34 passed; `git status` clean.

| Mutant | Red witnesses |
|---|---|
| M1 `Verdict`: unknown key under `[user]` -> `continue` instead of `InvalidReason` | cases user-extra-key, user-bare-key |
| M2 `ScrubInherited`: body removes nothing | `Configured_identity_outranks_inherited_git_identity_and_config_variables` |
| M3 `UserHeader` regex `^\[user.*$` | cases user-subsection, user-dotted-subsection, key-on-header-line; `Quoted_user_subsection_is_not_read_as_the_user_section` |

The other 13 new results stayed green under these mutants and are killed by the remaining branches: the header
rule (`[` line not matching `[user]` -> invalid) for credential, credential-url, include, include-if, core,
core-hooks, alias, url, unclosed-header; `!inUser` for key-before-section; `line[^1] == '\\'` for continuation;
`Read` size/BOM/CRLF handling for the written/variants test; the docs text for the doc test. Each asserts a constant
outcome from a product call (`Refusal(...)` equals `git_identity_invalid`/null/`git_identity_absent`, `Starts == 0`,
scrubbed names absent, `GIT_CONFIG_COUNT == "2"`, HEAD identity string from real `git log`); none self-compares.
The red commit b229025483 references neither `InvalidReason` nor `ScrubInherited`, so it compiles against the base
and is assertion-red, not compile-red.

## Brief items

1. Allow-list vs real git (git 2.47.3, the image's pin). 42 bypass files were built in the scratchpad and each was
   fed to `git config --file <f> --list` and to the real `ServerGitIdentity.Refusal` (reflected from the restored
   build): header with trailing comment, `[USER]`, `[ user ]`, `[user.x]`, `[User "x"]`, `[user] name = x`,
   `[user]name = x`, `[user] [include]`, `[user]# c` + `[include]`, `[user] ;\` + `[include]`, `[user]\tsigningKey`,
   indented `\t[include]`, `[include]\tpath`, continuation hiding `[include]`, `name=\` then `\t[include]`,
   escaped `\\` then `[include]`, quoted value with raw newline, quoted value with `\`-newline, `"W\\"` then
   `[include]`, `;` inside quotes, `Name.x`, `path` under `[user]`, key with a space, bare `name`, duplicate and
   case-variant keys, `# [include]` inside a value, BOM, NUL, CR-only line endings, NBSP/VT/FF/FS/U+2028/U+2029/NEL
   before `[include]`, a 20 KB `include.path` line, `[uſer]`. Result: `dangerous=0`. Every file from which git loads
   a key outside `user.name`/`user.email` (include.path, user.path, user.signingkey, user.x.name) is
   `git_identity_invalid`; the 20 KB file is `git_identity_absent` (the pre-existing 16 KiB cap). The probe
   accepts exactly the files git reads as user.name/user.email only (quoted `;`, BOM, duplicates, case variants,
   `# ...` after a value, `name =` followed by a later `name = W`). Git's own `[header] key` continuation on a
   header line, its second header on one line and its `\\` escape are all on the refuse side. Divergence in the
   benign direction only: see A-2.
2. Refusal ordering and diagnostics. `IdentityRefusal()` is the first statement of `ExecuteAsync` (before the
   in-process read path and before `ProcessStartInfo` exists) and precedes the stdin batch and the five
   push/delete/observe entry points; the 16-case witness and `StartCountingGit` prove `Starts == 0` with
   `Diagnostic == "git_identity_invalid"` for a `commit`. `Verdict`/`Read` return the two constants only; no
   line, key or path text reaches `LandingGitResult`. `git_identity_absent` for `[user]` with one key
   (`Identity_file_written_by_git_config_and_case_variants_stay_usable`), and `Read` keeps the CARD-1776
   refusals byte for byte (IsPathRooted, LinkTarget/ReparsePoint, Exists, Directory, Length 0 or > 16 KiB,
   IOException/UnauthorizedAccessException/ArgumentException -> absent). Refused content wins over a missing key.
3. Scrub. `Scrubbed` removes `GIT_AUTHOR_*`, `GIT_COMMITTER_*`, `GIT_CONFIG_KEY_*`, `GIT_CONFIG_VALUE_*`,
   `GIT_CONFIG_COUNT`, `GIT_CONFIG_PARAMETERS` (ordinal-ignore-case) and nothing else; `ApplyIdentity` then sets
   `GIT_CONFIG_GLOBAL` and `DisableAutoMaintenance` appends `gc.auto`/`maintenance.auto` from zero (the witness
   asserts count 2, key_0/key_1, planted `KEY_7` gone). The push helper is unaffected: it is named by the baked
   `/etc/gitconfig` (system scope) and reads `ANTIPHON_GITHUB_TOKEN_FILE`, neither a scrubbed name;
   `GIT_TERMINAL_PROMPT`/`GIT_OPTIONAL_LOCKS` survive. `ApplyIdentity` returns before the scrub when
   `_identityFile` is null, and `Desktop_without_an_identity_file_keeps_the_inherited_git_environment` runs a real
   commit through `LandingGit` whose HEAD carries the planted author/committer and whose child keeps
   `GIT_CONFIG_PARAMETERS`/`KEY_0`/`COUNT=3`: a real witness, not a mock. The probe path
   (`InProcessEligible`) applies the same `ApplyIdentity`, so `_childEnvironment` agrees with the child.
   Compose sets only `GIT_CONFIG_GLOBAL`; the image sets no `GIT_*`. Other inherited variables: A-1.
4. Docs. The new paragraph in `docs/agent-credentials.md` matches the code line by line (allowed content,
   refused list, "refused content wins over a missing key", the scrubbed names, maintenance entries appended
   afterwards, the desktop neither reads nor scrubs). Guard batch 52/52, DockerStackContractTests 117/117,
   GithubCredentialHelperTests 6/6 unchanged.

## Advisories (D-2; to cards, never hold the land)

- A-1 Other inherited GIT variables. `GIT_CONFIG_SYSTEM` (a hostile file there has every power the identity
  file had before this card, credential.helper included) and `GIT_CONFIG_NOSYSTEM` (drops the baked helper so the
  push fails authentication) change the system scope the pin relies on; `GIT_EXEC_PATH` runs foreign
  `git-*` binaries; `GIT_SSH_COMMAND`/`GIT_SSH`/`GIT_PROXY_COMMAND` and `GIT_ASKPASS`/`SSH_ASKPASS` run a program
  on an ssh/git URL or a credential prompt (`GIT_TERMINAL_PROMPT=0` does not disable askpass);
  `GIT_DIR`/`GIT_WORK_TREE`/`GIT_COMMON_DIR`/`GIT_INDEX_FILE`/`GIT_OBJECT_DIRECTORY`/`GIT_NAMESPACE` redirect the
  repository the land mutates; `GIT_TEMPLATE_DIR` installs hooks on clone/init; `GIT_SSL_NO_VERIFY`/`GIT_SSL_CAINFO`
  let a network position receive the token. `HOME`/`XDG_CONFIG_HOME` are already neutral under `GIT_CONFIG_GLOBAL`.
  None is set by the image, either compose file or a deploy script, so this is the operator's own environment
  (the A-9 boundary): not a blocker. The simple closure is a positive allow-list in `ApplyIdentity` (keep
  `GIT_CONFIG_GLOBAL`, `GIT_TERMINAL_PROMPT`, `GIT_OPTIONAL_LOCKS`, the maintenance entries; drop every other `GIT_*`).
- A-2 Probe accepts, git dies at use. A bare `name`/`email` line, a trailing `name =` after a complete pair, or an
  unterminated quote (`name = "x`) passes the allow-list because a usable pair exists or the line is syntactically
  a `name` key, while git fails the command (`missing value for 'user.name'`, `empty ident name`, `bad config
  line`). The failure surfaces as the git result instead of a pre-start refusal; no key outside user.name/email
  is loaded, so this is the A-11 class, not a security gap. Requiring a value on every `name`/`email` line and
  refusing an odd quote count closes it.
- A-3 Option-less `new LandingGit()` sites (RepositoryChildJournal, WorktreeManager, GitService,
  CardFileRepository, the DelegationWorktreeService fallback) have no identity file and therefore no scrub; they
  inherit the compose `GIT_CONFIG_GLOBAL` but an inherited `GIT_AUTHOR_*` would still outrank it there. Outside the
  land protocol (which uses the DI-resolved instance), unchanged by this card.
- A-4 Process. CARD-1787 has no plan with a `### Checkpoints` table (card-only Code) and the Code report is on
  neither the branch nor a task route this runner can read (`/api/agent-tasks/{id}` carries no report body,
  `/report`, `/reports`, `/final-report`, `/progress` are 404), so Code's CP-n lines could not be matched; this
  round's run supplies the validated receipt. The 34 the brief names equals the TRX count.

Performance: CP-1 10.2 s for 157 tests (the five git-spawning witnesses add about one second), CP-2 121 s with
the pre-existing slow class TestClassificationPolicyTests 101 s/41 tests, build 385 s under a slot at
maxcpucount 6, review budget overrun (about 70 minutes against the brief's 40, build-bound). No timeout changed.
OS witnesses: no OS-selected production branch changed; `Scrubbed` is ordinal-ignore-case for the Windows
environment but the desktop (empty setting) never reaches it; the Linux production path runs real git through
`LandingGit` in both new environment witnesses. Input waits: none added. Live acceptance (one `/land/v2` with a
hostile key in the mounted file refusing before git) stays pending with the CARD-1775 cutover.

--- review evidence ---
subjectTaskId: c3f172ad-add7-4ccb-98ad-639fdf9fcb87
reviewedSourceSha: f5d5754739c10516d0aa27830d3214225cf3aa5e
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: 57bbfc99d1810c99f2bf6e1a928427918c05d1fd..f5d5754739c10516d0aa27830d3214225cf3aa5e
