# CARD-1594 round 2 — c994 lookup regressions D-1..D-3

Outcome: fixed and pushed. Code tip `1238e6a815a40d1164527ddbb5e09cb143211d71` on `feat/card-task-abf772f4`. Empty-image refusal is audit-only, a different retirement is ignored, disagreeing other-SHA images refuse, and `docs/docker-stack.md` matches the code. End sweep: 625 passed, 0 failed. Fake Docker only.

## Commits

Fast-forward of start tip `b26804183823bf96b774433bee686f82e37a7fea`.

- `179dfa41f396f2bee5f05baf4f838890e9f55a23` — red witnesses, committed before the fixes.
- `6300c4f23a24dde8044ad2aa58480522f95ec5b0` — D-1. A completed receipt with an empty image returns 2 only when the caller passes `audit` and nothing else binds an image. `c1008` preview passes `allow-preview audit`; apply passes `audit`. `retire-temp-containers` calls `c994_lookup_image` with no arguments, so an absent temp's completed empty receipt stays a successful cleanup (`image:""`, exit 0).
- `ca7921ca68345518a34488cb8e9e89ea44a6fa27` — D-2. A receipt for another project, project id, or retirement is ignored when no operation is named. A named cleanup file for a different project or retirement returns 2. Partial, preview, refused, pending, and dry-run receipts from another SHA do not count.
- `4a0656c18e78b5fbf543d9c6fdbef2985e9bddf4` — advisory. Two completed non-dry-run receipts from other SHAs for this retirement that name different images return 2 (`RecycleGitAuditUnknown`). A current-SHA sha256 image still wins. Agreeing other-SHA images keep newest mtime, then operation id.
- `1238e6a815a40d1164527ddbb5e09cb143211d71` — D-3. `docs/docker-stack.md` states those rules for the preview paragraph, the container-cleanup sentences, and the volume gate.

`c994_lookup_image` still begins with `require_lane host`. Exact normalized 9-digit stamp compare, completed non-dry-run binding, the sha256 pin, and image-present `docker image inspect` are unchanged. When nothing matches, the lookup prints `C994_IMAGE_UNPINNED reason=no-matching-cleanup-record` and returns 0.

## Verification

One build slot, lease `ffab59a7-59f4-45a3-bc9c-52fa3bf6c05f`, waited 0s, held 6730s. Existing `tests/Antiphon.Tests/bin-abf772f4/Antiphon.Tests.dll` (`UseAppHost=false`). Scripts and docs are read from disk. The release call returned 404 after the process exited; the runner reaps that lease on exit.

| Filter | Passed | Failed |
|---|---:|---:|
| DockerStackDocumentationTests | 15 | 0 |
| CheckpointImportTests | 26 | 0 |
| RetiredTempContainerHostTests | 22 | 0 |
| RetiredTempContainerScriptTests | 16 | 0 |
| RollingProductionMountTests | 12 | 0 |
| RemoteScriptContractTests | 499 | 0 |
| RollingVolumeRecycleScriptTests | 35 | 0 |

Not run: `RetiredTempContainerDockerTests.C994_Real_retired_temp_lifecycle`, `RollingVolumeRecycleDockerTests.C1008_Real_docker_comparison`.

## Land

- `git merge-tree --write-tree origin/rc/2026-10-27` (`aa6dfd24a51db7c02e8722a05a21817bf4df407a`) `1238e6a815a40d1164527ddbb5e09cb143211d71`: clean.
- `git merge-tree --write-tree origin/master` (`f044e970d5fd91c7737eeb0cfc34bce09b4ed8db`) `HEAD`: conflicts. The same 13 paths already conflicted at `b26804183823bf96b774433bee686f82e37a7fea`: `.antiphon/task-af2b78c8.md`, `docs/docker-stack.md`, `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`, `docs/testing-and-build.md`, `scripts/c590-remote.sh`, `scripts/lib/test-phase-evidence.ps1`, `scripts/test-qualification-timing.ps1`, `server/Application/Services/AgentTaskDispatcher.cs`, `tests/Antiphon.Tests/Application/CapacityAdmissionLifecycleTests.cs`, `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs`, `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`, `tests/Antiphon.Tests/TestHelpers/TestTimingRecorderTests.cs`, `tests/Shared/TestTiming.cs`. Left unmerged.

`bin-abf772f4` directories removed. No rebase, no force-push, no tags.
