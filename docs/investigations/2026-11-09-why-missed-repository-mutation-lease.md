# Why missed: a reflected LandingGit constructor in the C448 crash workers

One committed note for rc qualification run 231f573c, chunk `antiphon-all-group-117.trx`,
rc/2026-11-13 at `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`, Windows. 1 failed of 208 in the chunk.
CARD-1781. Debug task 5d714273 reproduced it on Linux (server2) at master `2bf29ee9d`.
The Windows read-out is
`docs/investigations/2026-11-09-run-231f573c-repository-mutation-lease-windows-failure.md` on
`origin/feat/card-task-e1799c88` (`955907143022d1255c95c5edda0279c72a76bf36`).

## Failure

`Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced`
(`tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs:618`):

```
ShouldAssertException: record
    should not be null but was
Additional Info:
    ParentContainsErrorRecordException: ...crash-worker.ps1:17
     | Exception calling "CreateInstance" with "2" argument(s): "Constructor on type
     | 'Antiphon.Server.Infrastructure.Git.LandingGit' not found."
```

The crash and recovery workers in `tests/Antiphon.Tests/TestHelpers/RepositoryLeaseCrashWorker.cs`
are PowerShell here-strings. They load `Antiphon.Server.dll` and build the real `LandingGit` with
`[Activator]::CreateInstance($type, [object[]]@($null))`. `Activator.CreateInstance` binds only a
constructor with exactly that many parameters. It does not apply C# optional defaults. CARD-1776
changed the constructor from `LandingGit(CardFileBoardLookup? cardFiles = null)` to
`LandingGit(CardFileBoardLookup? cardFiles = null, IOptions<GitSettings>? git = null)`. The worker
therefore threw before it started git, never journalled a child, and the test reported its stderr
as a null record.

Classification: deterministic test-fixture break from a named product commit. It is not
Windows-only, not timing and not a flake. Product behaviour is correct. The failure took 2 s on
Windows against a 30 s budget. On Linux at master `2bf29ee9d`, `/*/*/RepositoryMutationLeaseTests/*`
gave 34 total, 1 failed (this test, same message), 31 succeeded, 2 skipped (Windows-only cases).
This is the same reflection break that `bcb7d0185` fixed for the
`RepositoryMutationLease` constructor on 2026-10-25.

Fix (test-only, `8ec342fd891a264e451383fd3d8c9f4d05c3d8d1`): both workers construct `LandingGit`
and `RepositoryMutationLease` through one `New-ServerObject` helper. It invokes the single public
constructor and fills each parameter after the given ones from its declared default. It throws
`fixture_constructor_count` or `fixture_constructor_required` when the shape no longer fits, so the
next signature change fails with a named reason instead of a null record. No assertion changed.
After the fix, `/*/*/RepositoryMutationLeaseTests/*` gave 34 total, 0 failed, 32 succeeded,
2 skipped on Linux. The Windows proof is the failed-first rerun on the next Windows cut.

## Introducing commit/card

introducing commit/card: `dff78584d636fa49eca37652f2c3f14a48002a92` (CARD-1776; rc pick of master
`0f76e7b0a9379345684dbb4f5452073e53eb82f4`, 2026-11-08 06:33Z, "mount the Linux land credential and
refuse a missing token"). It added the `IOptions<GitSettings>? git` parameter.
`RepositoryLeaseCrashWorker.cs` last changed in `bcb7d0185` (2026-10-25), before CARD-1776.

rc/2026-11-12 (`5fef2c23b`) already carried `dff78584d`. Run a6410818 on that line did not show the
failure because it wrote no group-117 TRX and no `RepositoryMutationLeaseTests` result before it was
superseded (Windows read-out above). Run 231f573c is the first rc run that executed the class after
the change.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran (as recorded in
`docs/investigations/2026-11-08-why-missed-server-git-windows.md`, which covers two other CARD-1776
causes):

- `5b1ea772` round 1 of `0f76e7b0a9`, scope Full, Linux server2, blocker B-1 (unrelated). CP-R1
  `/*/*/(ServerImagePushCredentialTests*)|(DockerStackContractTests*)|(ServerRepositoryMirrorScriptTests*)/*`
  131/131; CP-R2 the policy guard batch
  `(TestClassification*)|(TestLaneCategoryGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessMutationTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)`
  85/85; CP-R3 `GithubCredentialHelperTests` 6/6. The constructor change is in this round.
- `cbf389dd` round 2 of `cef2481a85`, scope Full, diff only, Linux server2, clean. CP-1
  `/*/*/(ServerImagePushCredentialTests*)|(DockerStackContractTests*)|(ServerRepositoryMirrorScriptTests*)|(GithubCredentialHelperTests*)/*`
  141/141; CP-2 the same guard batch 85/85.
- Merge batch 126 land subset: no fixed land row names `RepositoryMutationLeaseTests`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane,
`Integration` + `[ParallelLimiter<ProcessSpawnLimit>]`,
`tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs` (fixture
`tests/Antiphon.Tests/TestHelpers/RepositoryLeaseCrashWorker.cs`).

Both reviews selected only classes named after the card's own files plus the policy guard batch.
The constructor change compiled cleanly because every C# caller relies on the optional defaults.
The only broken caller is a type name inside a PowerShell string, which no compiler, analyzer or
grep for `new LandingGit(` reaches. The class failed on Linux too, so one run of it on server2 would
have caught it. Today `scripts/impacted-classes.ps1 -Range 0f76e7b0a9^..0f76e7b0a9` selects
`ServerGitProcessTests`, `ServerImagePushCredentialTests` and `ServerRepositoryMirrorScriptTests`
(kinds `server-git-start-sites`, `server-image-push-credentials`, `server-repository-mirror`), and
still not this class.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of
CARD-1782:

`server/Infrastructure/Git/LandingGit.cs`, `server/Infrastructure/Git/RepositoryMutationLease.cs`,
`tests/Antiphon.Tests/TestHelpers/RepositoryLeaseCrashWorker.cs` -> `/*/*/RepositoryMutationLeaseTests/*`

As a CARD-1801 `tests/impacted-classes.json` kind (proposed here, the table is not edited). The
class runs in about 56 s on server2 and needs no Postgres:

```json
{
  "id": "repository-lease-reflected-constructors",
  "globs": [
    "server/Infrastructure/Git/LandingGit.cs",
    "server/Infrastructure/Git/RepositoryMutationLease.cs",
    "tests/Antiphon.Tests/TestHelpers/RepositoryLeaseCrashWorker.cs"
  ],
  "classes": [
    "RepositoryMutationLeaseTests"
  ]
}
```

## Exemption

same cause as an already written note: none. CARD-1776 also introduced the two causes in
`docs/investigations/2026-11-08-why-missed-server-git-windows.md` (start-site scanner, Windows
cleanup), and the same review selections missed them. This is a third cause from the same card.
