# Why missed: server repository mirror script tests on Windows

One note for rc qualification run a6410818, chunk `antiphon-all-group-131.trx`,
rc/2026-11-12 at `5fef2c23b89414b8818aaf40db9ad738bce9524c`. 3 failed of 121 in the chunk.
Debug task f23857e7. The Windows evidence is
`docs/investigations/2026-11-08-run-a6410818-group-131-windows-failure.md` on
`origin/feat/card-task-6d4b5445` (`190fbacb7d`, a read-only desktop extract).

## Failures

| Test | Windows message | Linux at master `87000d3668` |
|---|---|---|
| `WhatIf_maps_desktop_paths_and_does_not_create_them` | `run.Exit should be 0 but was 2`, output `SERVER2-MIRRORS refuse exit=2 reason=path-forbidden` | green |
| `Execute_without_a_token_refuses_before_clone` | `run.Output should contain "reason=credential-absent"`; it was `... reason=path-forbidden` | green |
| `Present_token_still_refuses_before_git_when_the_mirror_parent_is_missing` | `run.Output should contain "reason=mirror-parent-missing"`; it was `... reason=path-forbidden` | green |

All three are in `ServerRepositoryMirrorScriptTests` (Integration). `Empty_antiphon_remote_is_unrecorded`
and `Both_switches_are_ambiguous_and_secrets_and_volumes_are_refused` passed on Windows. Linux at
`87000d3668` with filter `/*/*/ServerRepositoryMirrorScriptTests/*` gave total 5, failed 0.

## Cause and classification

The cause is a Windows-only test fixture problem. It is not a product defect, and it is not a flake.

`scripts/server2-repository-mirrors.ps1` line 103 accepts only a POSIX-rooted `-ReposRoot`
(`StartsWith('/')`). The production value is the server2 work volume `/work/repos`. The three
failing tests pass `Path.Combine(Path.GetTempPath(), "card-1776-mirrors-<guid>", "repos")`. On Linux
that is `/tmp/...` and passes the guard. On Windows it is `C:\Users\...\Temp\...`, so the script stops
with `path-forbidden` before it reaches the refusal each test checks. The two passing tests do not
pass `-ReposRoot`, so they use the default `/work/repos`. The script never touches git or a desktop
path, so the desktop is unaffected. The deterministic Linux reproduction:
`-ReposRoot 'C:\Users\x\repos'` gives `SERVER2-MIRRORS refuse exit=2 reason=path-forbidden`.

The class creates no git repositories. The read-only loose-object cleanup defect from group 118
(`GitFixtureCleanup`) does not apply here.

## Repair (test-only)

Branch `feat/card-task-f23857e7`, base master `87000d366861f8c5281e83ed92b57a66d9fd8860`:

- red `1c7cad11f53cd94a100d1542a0efecbe10d2f3a6`: new witness
  `Windows_temp_root_is_passed_drive_relative_and_planned`. The three tests pass their root through
  `ScriptRoot` (identity stub). On Linux the witness is red with the Windows message
  (`reason=path-forbidden`, exit 2): total 6, failed 1.
- fix `0b163d34288b81613bc93730cca5af31c088e92b`: on Windows, `ScriptRoot` passes the temp root in
  its drive-relative form (`/Users/.../repos`), and pwsh starts in the temp directory, so that form
  names the same directory. Linux is unchanged. On Linux: total 6, failed 0. No assertion was
  changed or loosened.

Windows proof is the failed-first rerun of group 131 on the next Windows cut. server2 cannot run
the Windows lane.

## Introducing commit/card

The introducing commit is `dff78584d636fa49eca37652f2c3f14a48002a92` (CARD-1776, "mount the Linux land
credential and refuse a missing token", 2026-11-08). It is the master/rc pick of task commit
`0f76e7b0a9379345684dbb4f5452073e53eb82f4`, and it added both the script and the test class.
Neither file has changed since on master or rc/2026-11-12.

## Approving reviews

Both approving reviews ran this class, but only on Linux:

- `5b1ea772`, round 1 of `0f76e7b0a9`. Scope Full, Linux server2. CP-R1
  `/*/*/(ServerImagePushCredentialTests*)|(DockerStackContractTests*)|(ServerRepositoryMirrorScriptTests*)/*`
  131/131 green, plus the guard batch 85/85. Blocker B-1 was for another reason (land git identity).
- `cbf389dd`, round 2 of `cef2481a85`. Scope Full, diff-only, Linux server2, clean. CP-1
  `/*/*/(ServerImagePushCredentialTests*)|(DockerStackContractTests*)|(ServerRepositoryMirrorScriptTests*)|(GithubCredentialHelperTests*)/*`
  141/141 green.

## Why the Windows lane was not run before the cut

- The failing tests were inside the review selection. What was missing was the OS. Code and both
  reviews ran on server2 Linux only, where `Path.GetTempPath()` begins with `/`.
- The class has no Windows-lane marker and no `[NativeLinuxOnly]` gate. Code and Review selection
  has no OS dimension, so a new script test class was approved without a single native Windows
  run.
- The script is Linux-only by design: it requires a POSIX root. Its tests had no OS-specific input,
  and they were not marked Linux-only either. That mismatch could not show up on a Linux run.
- The class arrived the same day as the cut (2026-11-08). The first Windows execution was the rc
  qualification itself.

## Cheapest pre-land check (CARD-1782 selector row)

`scripts/impacted-classes.ps1 -Path scripts/server2-repository-mirrors.ps1,tests/Antiphon.Tests/Scripts/ServerRepositoryMirrorScriptTests.cs`
against `tests/impacted-classes.json` on master `d608b09ba7` emits no kinds and an empty filter.
The row to add to the impacted-classes table:

| path | class filter | windows |
|---|---|---|
| `scripts/server2-*.ps1`, `tests/Antiphon.Tests/Scripts/ServerRepositoryMirrorScriptTests.cs` | `/*/*/ServerRepositoryMirrorScriptTests/*` (6 tests, about 12 s once built) | required |

The table has no lane field today. A Linux run of that filter stays green, so the row only catches
this cause once the selector has the Windows flag that the group-85 and group-118 notes ask for. A
general rule would also help: when a test feeds a Linux-only script a host temp path, that is a
Windows-lane row. The alternative is a `[NativeLinuxOnly]` marker on the class.

## Exemption

The mechanism matches an earlier note:
[server git Windows](2026-11-08-why-missed-server-git-windows.md), cause 2, and group 85. In each
case a class that is Windows-sensitive was approved on Linux only, and nothing in the selection
flags the Windows lane. The defect itself is new: a POSIX-root guard against a Windows temp path,
not read-only git objects.
