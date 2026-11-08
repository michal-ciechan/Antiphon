# Why missed: server git start-site scanner and Windows credential-test cleanup

One committed note for rc qualification run a6410818, chunk
`antiphon-all-group-118.trx`, rc/2026-11-12 at
`5fef2c23b89414b8818aaf40db9ad738bce9524c`. 3 failed of 310 in the chunk. CARD-1781.
Debug task bad0e712. The Windows evidence is
`docs/investigations/2026-11-08-run-a6410818-group-118-windows-failure.md` on
`origin/feat/card-task-a72ce01b` (desktop read-only extract).

## Failures

| Test | Class | Windows message | Linux at master `57bbfc99d1` |
|---|---|---|---|
| `C1432_every_server_git_start_site_disables_auto_maintenance` | `ServerGitProcessTests` | `missing should be empty but had ["server/Infrastructure/Git/LandingGit.cs:246 InProcessEligible/probe: eligibility probe must never launch or escape to a launch helper"]` | red, same message |
| `Configured_identity_file_rebases_when_the_child_environment_has_no_other_identity` | `ServerImagePushCredentialTests` | `UnauthorizedAccessException: Access to the path '047b9e4d…' is denied` at `DeleteRoot` (`:469`) from `finally` (`:192`) | green |
| `Identity_free_rebase_fails_without_git_config_global_and_passes_with_it` | `ServerImagePushCredentialTests` | `UnauthorizedAccessException: Access to the path 'a172845e…' is denied` at `DeleteRoot` (`:469`) from `finally` (`:160`) | green |

The Linux run at `57bbfc99d1` with filter
`/*/Antiphon.Tests.Infrastructure/(ServerImagePushCredentialTests)|(ServerGitProcessTests)/*`
gave total 33, failed 1 (C1432), succeeded 32, skipped 0.

## Causes and classification

Two causes, both test-side. Product behaviour is unchanged: the desktop still runs with an empty
`Git:IdentityFile`, and `ApplyIdentity` returns at once there.

1. **Cross-platform scanner regression (C1432).** The CARD-1776 fix added `ApplyIdentity(probe)` to
   `LandingGit.InProcessEligible`. The Roslyn start-site scanner in `ServerGitProcessTests` treats any
   use of `probe` outside `ConfigureProcess`, `ServerGitProcess.DisableAutoMaintenance` and
   `InProcessGitReads.ChildReadsSameRepository` as an escape. The test reads only source text, so it
   is red on every OS. The repair is in the scanner. `ApplyIdentity` is accepted only in
   `LandingGit.cs`, and only when there is a single declaration with one parameter whose body has no
   launch and uses its parameter only as a member-access receiver. CARD-1787's scrub body
   (`ServerGitIdentity.ScrubInherited(start.Environment)`), already on master as `cef76fe409`, passes
   that check. A real `Process.Start(start)` inserted into `ApplyIdentity` turns C1432 red again.
2. **Windows-only cleanup (two credential tests).** Git for Windows writes loose objects read-only
   (`attrib: A R`). `Directory.Delete(root, recursive: true)` therefore throws on Windows, after both
   rebase assertions have already passed. On Linux, deleting a file depends only on whether its
   directory is writable, so the same code passes there. The class now deletes through the shared
   `GitFixtureCleanup.Delete`, which clears read-only attributes first. That also covers the five
   CARD-1787 tests that call `DeleteRoot`. Linux cannot make this delete fail, so there is no Linux
   red. The red is the Windows TRX above, and the failed-first rerun on the next Windows cut is the
   proof.

Not a flake: both causes are deterministic.

## Introducing commit/card

introducing commit/card: `4128a884a9ae5713a1708ae559b8f5ed5e9ec71e` (CARD-1776; rc pick of
`cef2481a855a317cfcb720a91e1f9a81513ffd19`) for both causes. That commit added `ApplyIdentity(probe)`
and the two git-spawning rebase witnesses. `d6c63c922ee34106083f7fc7d7a0cd9954865b4a` is their red
commit, with `DeleteRoot` unchanged from `841606834e214cc2fe948becdbcba6ba1a27b808`. Landed in Merge
batch 126. `ServerGitProcessTests.cs` last changed in `cb4e278ad158765d931ab0bd350ad72f7cef239a`
(2026-11-06), before CARD-1776.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `5b1ea772` round 1 of `0f76e7b0a9`, scope Full (`ordinaryScopeCompleted: Full`), Linux server2,
  returned blocker B-1 (no land git identity). CP-R1
  `/*/*/(ServerImagePushCredentialTests*)|(DockerStackContractTests*)|(ServerRepositoryMirrorScriptTests*)/*`
  131/131; CP-R2 guard batch
  `(TestClassification*)|(TestLaneCategoryGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessMutationTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)`
  85/85; CP-R3 `GithubCredentialHelperTests` 6/6. The `ApplyIdentity(probe)` change was not in this
  round.
- `cbf389dd` round 2 of `cef2481a85`, scope Full, diff-only, Linux server2, clean. CP-1
  `/*/*/(ServerImagePushCredentialTests*)|(DockerStackContractTests*)|(ServerRepositoryMirrorScriptTests*)|(GithubCredentialHelperTests*)/*`
  141/141; CP-2 the same guard batch, 85/85. The report names the three `ProcessStartInfo`
  constructions in `LandingGit` (probe `:246`) and that each calls `ApplyIdentity`, but neither row
  selects `ServerGitProcessTests`.

## Outside the selection

why the failing test was outside that selection (lane/category/path):

- C1432: Integration category, in `tests/Antiphon.Tests/Infrastructure/ServerGitProcessTests.cs`.
  The plan row (CARD-1744 CP-4) and both reviews selected only the classes named after the card's
  own files. No impacted-classes row maps `server/Infrastructure/Git/**` to the start-site audit that
  owns every server git `ProcessStartInfo`. The failure was visible on Linux. Running the class once
  would have caught it.
- Credential-test cleanup: Unit category, Windows lane. Code and both reviews ran on server2 Linux
  only. These tests have no Windows-lane marker, and Code/Review selection has no OS dimension, so a
  new git-spawning class is approved without one native Windows run. Same mechanism as group 85
  (`LandGraphLandRefreshTests`, read-only loose objects).

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

| path | class filter | windows |
|---|---|---|
| `server/Infrastructure/Git/LandingGit.cs`, `server/Infrastructure/Git/GitService.cs`, `server/Infrastructure/Git/WorktreeManager.cs`, `server/Infrastructure/Git/CardFileRepository.cs`, `server/Application/Services/GitWorkspaceService.cs`, `server/Application/Services/DelegationWorktreeService.cs`, `server/Application/Services/DelegationWorkspaceResolver.cs`, `server/Application/Services/OrchestratorWorkspaceLayout.cs`, `server/Application/Services/AgentTaskReplyService.cs`, `server/Infrastructure/ExternalChanges/ChangeDetectionService.cs` (the C1432 roster) | `/*/*/ServerGitProcessTests/*` (23 tests) | not needed (source scanner) |
| `tests/Antiphon.Tests/Infrastructure/ServerImagePushCredentialTests.cs`, `server/Infrastructure/Git/ServerGitIdentity.cs` | `/*/*/ServerImagePushCredentialTests/*` | required |

The table has no lane field today, so the second row only catches the Windows cause once the
selector carries the Windows flag that the group-85 note asks for. A Linux run of that filter cannot
see a read-only delete failure.

## Exemption

same cause as an already written note: the Windows cleanup cause is the same as
[land-graph land refresh, Family A](2026-11-08-why-missed-land-graph-land-refresh.md) (read-only git
loose objects, Linux-only approval of a git-spawning class). The C1432 scanner cause is new: none.

## Repair

Branch `feat/card-task-bad0e712`, base master `57bbfc99d1810c99f2bf6e1a928427918c05d1fd`, test-only:
red `afef19104a71e17ba05a5c5008884da3526f57fb`, scanner fix
`1e45e41ec81fe18c601af07ffeb3568f20927f40`, cleanup fix
`43b1b8759ed1ab429520e5585083d12feeee0eec`. `git merge-tree --write-tree origin/master HEAD` is
clean against `74bbfa4743` (CARD-1787 included).
