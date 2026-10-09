# CARD-1105: compare of `worktrees/task-0eafbbee` on the server2 OLD volume against master (2026-10-09)

Task 769d81ba (Debug). Operator decision: "Compare these, and if the same then discard."
**Outcome: STEP 2 FAILED. 26 of the 30 entries are not byte-identical to current `origin/master`, so the
discard (STEP 3) did NOT run.** Nothing on the volume was written. Next: operator decides rescue or accept.

## Method (read-only)

- Volume `antiphon-runner_work` (OLD runner), mounted `type=volume,...,target=/work,readonly` in throwaway
  `docker run --rm` containers on image `antiphon-server2/session-testing:4358939ecd85`, `--user 1654:1654`.
  The container's mountinfo showed `/work ro,relatime`, and a write probe (`touch /work/.ro-probe`) failed.
- Git env: `GIT_OPTIONAL_LOCKS=0 GIT_NO_LAZY_FETCH=1 GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null`,
  `core.commitGraph=false`. Only `git status --porcelain [-z] --untracked-files=all`, `rev-parse`,
  `symbolic-ref` and `config --get` ran. No add, stash, checkout, fetch or clean.
- Worktree state: HEAD `8331a9cf1` on `feat/card-task-0eafbbee`, `core.autocrlf` and `core.eol` unset,
  30 entries (23 ` M`, 7 `??`), the same set the 2026-10-08 replay reported.
- sha256 was taken in the container for each entry, both raw and after LF normalization (`sed 's/\r$//'`).
  The master side was hashed on the desktop clone after `git fetch origin`, with
  `origin/master` = `59d67869b6a60f13510827baf1aeb499b1b93280`, from `git cat-file blob` (no
  checkout filters). Neither side contains CR bytes: raw equals LF-normalized for all 30 worktree files and all
  master, `1d994aac9` and `31411ea31` blobs, so line endings explain none of the differences.
- Diff stats: the 30 files were streamed out of the read-only mount with `tar` into a private desktop scratch
  directory, which was not committed. Their sha256 was re-verified against the in-container hashes, and they were
  diffed with `git diff --no-index --numstat`. Only line counts are reported here, no file contents.
- "Later master commits" counts `git log 1d994aac9..origin/master -- <path>`. Every count includes
  `31411ea31` (see below).

## Finding: the WIP was already preserved on master, except three small deltas

`31411ea31` "CARD-0835 WIP preserved from failed task 0eafbbee (uncommitted, untested)" (2026-10-01 21:24 +0100,
parent `1485abaab`, `8331a9cf1` is its ancestor) is **reachable from `origin/master`**, although its message
says "Resume branch only; never merge to master". It is a plumbing-built snapshot of these same 30 paths.
**27 of the 30 worktree files are byte-identical to `31411ea31`.** Three are not:

| Path | numstat `31411ea31` -> worktree | worktree mtime (UTC) |
|---|---|---|
| `tools/Antiphon.Checkpoints/CheckpointApp.cs` | +5/-0 | 2026-10-01 05:40:19 |
| `tools/Antiphon.Checkpoints/Report/ReportMerger.cs` | +1/-1 | 2026-10-01 05:40:19 |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs` | +1/-1 | 2026-10-01 05:40:19 |

The git blob ids of these three worktree versions (`c20ebb58e1b8`, `9328d6c6df6a`, `1194c5735b58`) appear in
**no commit on any ref of the desktop clone** (`git log --all --find-object`). These 7 changed lines are the
only content in the worktree that no commit records. They were written about 2 minutes after the task failed
(05:38Z), and `31411ea31` was built from an earlier state. `Program.cs` is the only other file newer than
05:38Z, and it matches `31411ea31`.

## Table (30 entries)

Columns: worktree raw sha256 (12-char prefix); LF-normalized hash (`=raw` means identical); identical to current
`origin/master`; identical to `1d994aac9`; number of master commits touching the path since `1d994aac9`; numstat
master -> worktree; numstat `1d994aac9` -> worktree; identical to `31411ea31`.

| Path | Code | sha256 raw | LF-norm | = master | = 1d994aac9 | later master commits | vs master | vs 1d994aac9 | = 31411ea31 |
|---|---|---|---|---|---|---|---|---|---|
| `scripts/delegate.ps1` | M | `4e92b492aa75` | =raw | **no** | yes | 2 | +1/-11 | +0/-0 | yes |
| `scripts/run-checkpoint.ps1` | M | `4e462601ffea` | =raw | **no** | no | 7 | +3/-81 | +0/-4 | yes |
| `server/Application/Dtos/AgentTaskDtos.cs` | M | `1579ae0da537` | =raw | **no** | yes | 3 | +1/-22 | +0/-0 | yes |
| `server/Application/Dtos/StageOutcomeDtos.cs` | M | `79e88ac0d383` | =raw | yes | yes | 1 | +0/-0 | +0/-0 | yes |
| `server/Application/Services/AgentTaskLandingProtocol.cs` | M | `3213bacf13c9` | =raw | **no** | yes | 4 | +7/-15 | +0/-0 | yes |
| `server/Application/Services/AgentTaskReplyService.cs` | M | `b7c4681c3a43` | =raw | **no** | yes | 20 | +123/-458 | +0/-0 | yes |
| `server/Application/Services/AgentTaskService.cs` | M | `46cfbec1deb5` | =raw | **no** | yes | 26 | +18/-434 | +0/-0 | yes |
| `server/Application/Services/LandApproval.cs` | M | `0289f83e9155` | =raw | **no** | no | 3 | +13/-31 | +2/-3 | yes |
| `server/Application/Services/ReviewEvidence.cs` | M | `86dce15683df` | =raw | **no** | yes | 2 | +2/-7 | +0/-0 | yes |
| `server/Application/Services/StageOutcomeService.cs` | M | `6173dc6d6220` | =raw | **no** | yes | 4 | +64/-100 | +0/-0 | yes |
| `server/Domain/Entities/StageOutcome.cs` | M | `73cf95d6c39a` | =raw | **no** | yes | 2 | +2/-2 | +0/-0 | yes |
| `server/Migrations/AppDbContextModelSnapshot.cs` | M | `6218024a0684` | =raw | **no** | yes | 11 | +63/-1264 | +0/-0 | yes |
| `tests/Antiphon.Tests/Application/ReviewEvidenceParserTests.cs` | M | `4124a6047553` | =raw | **no** | no | 2 | +0/-2 | +0/-2 | yes |
| `tools/Antiphon.Checkpoints/CheckpointApp.cs` | M | `558ffd074839` | =raw | **no** | no | 3 | +5/-21 | +5/-6 | **no** |
| `tools/Antiphon.Checkpoints/Execution/RowRunner.cs` | M | `a9897cc37b41` | =raw | **no** | yes | 3 | +5/-36 | +0/-0 | yes |
| `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs` | M | `7874e11b333e` | =raw | **no** | yes | 3 | +2/-9 | +0/-0 | yes |
| `tools/Antiphon.Checkpoints/Program.cs` | M | `0b6efa6e431f` | =raw | **no** | yes | 4 | +3/-41 | +0/-0 | yes |
| `tools/Antiphon.Checkpoints/Report/CheckpointLine.cs` | M | `7c28a4e544f3` | =raw | **no** | yes | 2 | +0/-3 | +0/-0 | yes |
| `tools/Antiphon.Checkpoints/Report/ReportMerger.cs` | M | `e7694c77ada4` | =raw | **no** | no | 5 | +11/-20 | +7/-10 | **no** |
| `tools/Antiphon.Checkpoints/Report/ReportModel.cs` | M | `47ea649d8071` | =raw | **no** | yes | 2 | +0/-2 | +0/-0 | yes |
| `tools/Antiphon.Checkpoints/Report/ReportWriter.cs` | M | `88b4b5313e0a` | =raw | **no** | yes | 2 | +0/-7 | +0/-0 | yes |
| `tools/Antiphon.Checkpoints/RunRequest.cs` | M | `51f9c09205b2` | =raw | **no** | yes | 2 | +0/-1 | +0/-0 | yes |
| `tools/Antiphon.Checkpoints/State/RunState.cs` | M | `15b51c09245f` | =raw | **no** | yes | 2 | +0/-2 | +0/-0 | yes |
| `scripts/lib/checkpoint-source.ps1` | ?? | `0223cb3db3b2` | =raw | **no** | no | 4 | +2/-14 | +2/-14 | yes |
| `scripts/validate-checkpoint-receipt.ps1` | ?? | `c126df1c5063` | =raw | **no** | no | 4 | +9/-53 | +6/-17 | yes |
| `server/Migrations/20261001052023_AddReviewSourceClean.Designer.cs` | ?? | `25dc6650e608` | =raw | yes | yes | 1 | +0/-0 | +0/-0 | yes |
| `server/Migrations/20261001052023_AddReviewSourceClean.cs` | ?? | `e024979ce6a5` | =raw | yes | yes | 1 | +0/-0 | +0/-0 | yes |
| `tools/Antiphon.Checkpoints/Evidence/SourceRunGuard.cs` | ?? | `5eb82f1d6a75` | =raw | yes | yes | 1 | +0/-0 | +0/-0 | yes |
| `tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs` | ?? | `0564d596ab8e` | =raw | **no** | no | 4 | +2/-27 | +2/-27 | yes |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs` | ?? | `05dc5a608d5f` | =raw | **no** | no | 3 | +5/-40 | +3/-6 | **no** |

Summary: 4 entries are identical to master (`StageOutcomeDtos.cs`, both `20261001052023_AddReviewSourceClean`
migration files and `SourceRunGuard.cs`). 17 are identical to `1d994aac9` (the committed retry), but master has
changed them since. 6 match neither, and their bytes are in `31411ea31`. 3 match neither and are not in any commit
(above). The untracked files differ from master only where master later edited them. None is missing on master.

## Decision rule result

STEP 2 required every entry to be identical to master, raw or after LF normalization alone. 26 are not, so
STEP 3 (the `checkout -- .` and `clean -fd` discard) and its proof bundle were **not** run. The volume, its refs
and its reflogs are unchanged. No container was stopped, no deploy phase ran, and `-ResumeRecycle` was not used.

## Operator decision needed

- **Accept (discard):** everything except the 7 lines in the three files above is already in master history
  (`31411ea31`, then superseded by `1d994aac9` and later commits). Discarding loses only those 7 lines, which
  were written after the task failed and never committed, on a card (CARD-0835) that is Done.
- **Rescue:** commit the three files' delta somewhere first, for example as a patch preserved off-volume, then
  discard.
- Separately: `31411ea31` reached master despite its "never merge to master" message. It may be worth a card if
  that was not intended.
