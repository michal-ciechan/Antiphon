# CARD-1755: retirement content binding is recomputed for every due attempt

**Verdict.** The same unchanged worktree is not hashed on every hourly sweep. It is hashed, several times, on every attempt that reaches content binding, including the first attempt, and a refused attempt does that work again when its backoff falls due. There is no cache. The 2026-11-07 12:16Z window is one tick of the hourly job, not a stack of ticks. This runner cannot see whether the trees in that tick were first attempts or retries.

No product code was changed.

## What the trace already measured

Source: `docs/investigations/2026-11-07-desktop-server-runner-cpu-trace.md` on `origin/feat/card-task-7ca19c43`. Desktop server PID 330800, Debug build, 2026-11-07 12:16:35Z–12:21:35Z. Shares are of active samples. The server used 1.30 cores in the window, so 15.02% is about 0.20 cores, and the trace author treats file-syscall time as an upper bound.

| Share | Frame |
|---:|---|
| 15.02% | area union: `GuardedWorktreeRemoval`, `WorktreeNoFollowDelete`, `TaskWorktreeRetirementService`, `WorktreeNestedRepositories` |
| 6.26% | `GuardedWorktreeRemoval.RemoveDirectoryAsync` inclusive |
| 5.38% | `WorktreeNoFollowDelete.RegularFileHash` inclusive |
| 5.31% | `GuardedWorktreeRemoval.BindContentAsync` inclusive |
| 4.83% | `TaskWorktreeRetirementService.LegacyReleaseAdmissionAsync` inclusive |
| 3.66% | `WorktreeNoFollowDelete.TryStamp` self |
| 3.15% | `WorktreeNoFollowDelete.WindowsRegularFileHash` self |
| 2.07% | `BCrypt.BCryptHashData` self |

One collapsed stack is the hash: `LandingGit.RunAsync` > `TrackedNamesAsync` > `BindContentAsync` > `EntryManifest` > `RegularFileHash` > `WindowsRegularFileHash` (1.77% self, plus 1.69% in `SHA256.HashData`). Another is the stat walk under the legacy-release gate: `LegacyReleaseAdmissionAsync` > `LiveAuthorizationAsync` > `RemoveDirectoryAsync` > `CustodyOf` > `WorktreeNestedRepositories.Find` (1.23%). The profiler order of that second stack does not match source order. Source calls `LiveAuthorizationAsync` from `RemoveDirectoryAsync`, and the callback is `LegacyReleaseAdmissionAsync`. `CustodyOf` runs after that await returns (`GuardedWorktreeRemoval.cs:266-272`). The method names are the evidence; the printed order is not a second call chain.

## 1. Call chain, cadence, how many trees

The hash runs only inside a removal that has already passed the cheap gates and is about to move the tree.

1. Hangfire job `antiphon:desktop-legacy-release`, cron `15 * * * *` UTC (`server/appsettings.json:230-241`, `HangfireConfiguration.cs:94-101`). Shipped on: `Enabled=true`, `Execute=true`.
2. `DesktopLegacyReleasePolicyService.RunTickAsync` loads every Worktree task that has a path and no Complete retirement for the current attempt (`DesktopLegacyReleasePolicyService.cs:149-154`), oldest `CompletedAt` first.
3. Server facts hold most of them with no git and no hash. Due rows are capped at `MaxReleasesPerTick` 25 pre-checks (`:219`). A tick stops after `MaxTickMinutes` 20. The 5-second pause runs only after a real filing or removal.
4. `PrecheckAsync` (`:443-512`) stats the leaf, reads registrations, and runs `git status` / `rev-parse` / `ls-remote` / `merge-base`. It does not hash file bytes. A dirty, unpushed, or untrusted tree is Held and never reaches the binder.
5. A passing row is filed with `ReleaseSystemAsync` and removed with `TryRetireAsync` (`:373`).
6. `TryRetireCoreAsync` takes the repository lease (`TaskWorktreeRetirementService.cs:397-398`), reads the legacy switch (`:413`), then `TryRemoveAsync` (`:481`).
7. `GuardedWorktreeRemoval.RemoveDirectoryAsync` inspects twice, classifies ignored paths, walks for late untracked files, and on Windows walks again for `WorktreePathLimit` (`:166-170`). Only then does it call `BindContentAsync` (`:198`).

Other sweeps do not add this hash on the current settings:

| Sweep | Cadence | Reaches `BindContentAsync`? |
|---|---|---|
| Desktop legacy release | hourly at :15 UTC, ≤25 pre-checks, sequential | Yes, for a pre-check that passes |
| Worktree residue | daily 10:00 Europe/London | No. `WorktreeResidue:Execute` is false (`server/appsettings.json:223`). `WorktreeResidueSweepService.cs:124` calls removal only when Execute is true |
| Runner worktree retention | every 30 min, server2 only | No. It does not call `GuardedWorktreeRemoval` |

The trace window starts 95 seconds after the 12:15Z tick and lasts 5 minutes. That is one tick, still inside its 20-minute budget. Samples that include `LegacyReleaseAdmissionAsync` (4.83% inclusive) are that tick. One tick handles candidates one at a time, so the 15% is one or a few trees being bound, not 25 trees at once and not several hours of sweeps.

A built checkout is much larger than the hash set. `git ls-files --others --ignored --exclude-standard` has no `--directory`, so an ignored directory is listed as every file. Measured in a temporary repository: without `--directory` the output is `node_modules/pkg/a.js`; with `--directory` it is `node_modules/`. `LandingGit.InspectAsync` uses the form without `--directory` (`LandingGit.cs:912-919`), twice per attempt. This mirror has 10,056 tracked files and 1 ignored path, because it has no build output. A desktop tree that has been built still stats `bin`, `obj`, and `node_modules` on every walk below. Those paths are not the SHA-256 set. See the next section.

## 2. Is an unchanged worktree hashed again?

Yes, whenever an attempt reaches the binder. Nothing stores a manifest and skips the next attempt.

`BindContentAsync` (`GuardedWorktreeRemoval.cs:650-671`) on one attempt, for a tree that has not changed:

| Pass | What it reads | Line |
|---|---|---|
| 1 | SHA-256 of every content-bound file (`EntryManifest` → `RegularFileHash`) | `:665`, `:819-822` |
| 2 | SHA-1 git blob of every tracked regular file (`GitBlobSha1`), then maybe `git hash-object` when the raw blob differs from the index | `:728-741` |
| 3 | SHA-256 of the same files again. The two manifests must be identical or the move is refused | `:668-670` |
| 4 | After the move, `CustodyOf` SHA-256s every file whose receipt has a hash | `:956-960` |
| 5 | The delete SHA-256s each of those files again immediately before unlink (`WindowsRegularFileHash` / Linux `HashAt`) | `WorktreeNoFollowDelete.cs:99-101`, `:244` |

Passes 1 and 3 are the race check: "A write during that read makes the two manifests differ" (`:650-653`). Passes 4 and 5 are after the rename. Pass 5 has to stay, because the delete unlinks as it goes and a writer can land between the listing and the unlink.

Content-bound means `BindsContent` (`:832-833`): the path is tracked, or it is a retained evidence file, or it is not in the disposable set. Disposable is exact path membership, not a directory prefix. The classifier puts every ignored path that is not evidence and not disposable into Protected (`WorktreeIgnoredContentClassifier.cs:52-56`). Any protected path returns `ignored_content_preserved` before the binder (`GuardedWorktreeRemoval.cs:139-140`). So a removal that reaches the hash is hashing tracked files and retained evidence, not `node_modules`. The stat walks (`TryStamp`, `WorktreeNestedRepositories.Find`, the Windows path-limit walk, `LateUntrackedAsync`) still visit every directory, including disposable ones. `TryStamp` at 3.66% self and `WindowsRegularFileHash` at 3.15% self are those two costs, side by side.

Across sweeps, a tree is bound again only when it is due:

- Removed: not again (`ClearRetry`, `DesktopLegacyReleasePolicyService.cs:746-750`).
- Refused before anything moves (`source_changed`, `tracked_bytes_changed`, and the other non-partial residues): `StampRetry` (`:727-743`) sets `NotBefore` to 60, 120, 240, 480, then 1440 minutes (`RefusedRecheckMinutes` 60, cap `RefusedRecheckMaxHours` 24). The fifth refusal is `retry_exhausted` and is not attempted again (`MaxRefusedAttempts` 5, `:183-184`). The next due tick calls `NothingRemovedAsync` (`TaskWorktreeRetirementService.cs:419-421`, `:593-614`) and, when the directory is still there and unchanged, opens a new attempt and runs the full bind again. With an hourly cron, a refusal at 12:20 is skipped at 13:15 (`NotBefore` 13:20) and retried at 14:15. That is every other sweep at first, then less often. It is not every sweep.
- Set aside, delete incomplete: the next due tick does not rebuild the manifest. It re-enters the pending set-aside path and `CustodyOf` hashes the stored receipt again (`GuardedWorktreeRemoval.cs:108-112`).
- `repository_lease_required` and the legacy-switch deferrals do not hash and do not burn a retry (`DesktopLegacyReleasePolicyService.cs:374-402`). The lease refusal is before `TryRemoveAsync`.
- Pre-check holds (dirty, unpushed) never hash. They use the same 60-minute doubling backoff.

No log or database row was read for the 12:15Z tick. Desktop server logs live at `C:\src\Antiphon\server\logs` (`docs/logs.md`). This runner has no such files. `127.0.0.1:17280` refused a connection. The brief says nothing runs on the desktop, so this task did not query production Postgres or `antiphon.desktop.codeperf.net`. Hangfire storage is in-memory (`docs/logs.md`), so a restart has already dropped the job history.

What would close the first-attempt versus retry question, read-only, on the desktop database:

```sql
SELECT "StartedAt", "Execute", "Candidates", "Held", "Deferred", "Refused", "Partial", "Removed"
FROM "WorktreeResidueRuns"
WHERE "Scope" = 'desktop-legacy-release'
  AND "StartedAt" >= TIMESTAMPTZ '2026-11-07 12:00:00+00'
ORDER BY "StartedAt";

SELECT c."TaskId", c."Outcome", c."ReasonCode", COUNT(*) AS attempts
FROM "WorktreeResidueRunCandidates" c
JOIN "WorktreeResidueRuns" r ON r."Id" = c."RunId"
WHERE r."Scope" = 'desktop-legacy-release'
  AND r."StartedAt" >= TIMESTAMPTZ '2026-11-06 00:00:00+00'
  AND c."Outcome" IN ('Refused', 'Partial', 'Removed')
GROUP BY 1, 2, 3
HAVING COUNT(*) > 1
ORDER BY attempts DESC;
```

The code result does not wait on that query. A cross-sweep cache would not have removed the hash from a first attempt, and the window is one tick.

## 3. Options

The safety property the binding exists to keep: do not delete a byte that differs from the raw SHA-256 recorded immediately before the move. The two manifests catch a write during the read. `CustodyOf` catches a write during the move, before any unlink. The delete hashes again because it unlinks as it walks. Git's blob id is not that receipt. It is SHA-1 of `blob <len>\0` plus the clean form, and `core.autocrlf` or a clean filter makes the working bytes differ from the blob (`GuardedWorktreeRemoval.cs:729-731`). A null hash on a manifest entry means the delete will not re-check those bytes (`:956`).

| Option | Saving | What it must still keep | Smallest change |
|---|---|---|---|
| Cache keyed by path plus a stamp (size, mtime) | The retry of a tree that failed before the move, and only the SHA-256 of entries whose stamp still matches. It does nothing for a first attempt, which is what one tick spends the window on | The receipt used for the delete has to be from this attempt, not from an hour ago. A stamp match may reuse a hash taken earlier in the same attempt. A stamp miss, or any attempt that is a new tick, hashes again. A cache that skips `CustodyOf` or the delete-time hash can delete bytes a writer changed without moving size or mtime | Do not add a cross-sweep cache |
| Hash only files git reports dirty | About one SHA-1 read of every clean tracked file, if that read is dropped. Not the receipt SHA-256 | A clean index stat is not a raw-byte receipt. Clean filters and a same-size same-mtime write both exist. Those files still need one SHA-256 in the receipt, or the delete will not check them | Keep the receipt hash. Fold the git-blob read into the same pass as the first SHA-256 so the file is read once for both |
| Bound the sweep | None for the tree already being hashed. The sweep is already ≤25 pre-checks, 20 minutes, hourly, with backoff | A lower cap only delays retirement. It does not change what one attempt hashes | No change. The cap is already shipped |
| Skip worktrees already bound | The second full manifest of an attempt, and the fresh bind of a not-yet-moved refusal once its backoff is due | A set-aside receipt is already stored. The next pass still has to `CustodyOf` before it unlinks. Skipping that compare deletes from a stale receipt. "Already bound this tick" is not a skip that exists today, because each candidate is acted on once per tick | On the second `EntryManifest` only, reuse the first pass SHA-256 when `TryStamp` (kind, length, mtime) matches. Keep pass 1, `CustodyOf`, and the delete-time hash |

The second-manifest reuse can miss a write that keeps size and mtime. `CustodyOf` and the delete still hash the bytes, so the new bytes are not unlinked. The failure mode is a set-aside left in place (`set_aside_content_changed`), which is the same refusal the binder already uses. Windows `TryStamp` records last-write time, not a change time (`WorktreeNoFollowDelete.cs:200-202`).

Dropping one of the two pre-move SHA-256 passes removes part of the 3.15% `WindowsRegularFileHash` self time and the 2.07% `BCryptHashData` time, not most of the 15%. The stat walks stay. Combining the first SHA-256 with `GitBlobSha1` removes a second full read of every tracked file. Together that is two content reads saved out of five on a successful attempt. A cross-sweep cache saves work only on a retry, which the backoff already spaces out, and it was not what this single tick had to be.

## 4. Coordination

- **CARD-1714** (Review). Its open tip `origin/feat/card-task-52410842` at `5233faefdedd2d024fcbbabeb02e1e9146b03a70` is not an ancestor of master `5322f3d2012e60a04afce2e0db9dd5263e47b491` (merge-base `84e19071e25abc7a595fddfac145b187dc5f2d11`). Against current master that tip is behind in `GuardedWorktreeRemoval.cs`, `WorktreeNoFollowDelete.cs`, `WorktreePathLimit.cs`, `LandingGit.cs`, and `DesktopLegacyReleasePolicyService.cs`. The patch it still adds is the pre-check `core.longpaths` status and `WroteStandardError`. Current master already has that behaviour through `StatusVerdictAsync` (`DesktopLegacyReleasePolicyService.cs:470-478`, `LandingGit.cs:904-909`) and already refuses an over-long path before the binder (`GuardedWorktreeRemoval.cs:166-170`). A hash change in `BindContentAsync` / `EntryManifest` / `TrackedBytesAsync` does not need that branch. Do not retarget this work onto `feat/card-task-52410842`. Do not edit the status pre-check or `InspectAsync` while 1714 is in Review.
- **CARD-1754** (In Progress, Code not started here). `CheckCompactionContinuationService` only. No shared file.
- **CARD-1756** (In Progress, Code not started here). `TranscriptTailer` and terminal observation, session runner. No shared file.
- Duplicate search on the Antiphon board, `card.ps1 search -All`: `retirement content binding` and `BindContent` each returned only CARD-1755. `worktree retirement SHA` returned none. No second card.

## 5. Recommendation

Keep CARD-1755 as one card. No split.

Plan the intra-attempt cut only. In one read of each content-bound file, compute the receipt SHA-256 and, for a tracked regular file, the git blob id. On the second manifest, reuse that SHA-256 when `TryStamp` matches. Leave `CustodyOf` and the delete-time hash in place. Do not add a cache that survives the attempt. Do not skip the receipt hash because git status was clean. Do not lower the sweep cap for this CPU.

That plan is not written here.

Not done, noted: no cache, no hash-skip, and no code change.

## 6. Plain English

- The desktop server hashes a worktree only when it has decided to delete that worktree, not on a scan of every tree.
- That decision runs once an hour, at 15 minutes past, and looks at most 25 trees. Dirty trees are skipped before any hash.
- Each tree it does delete is hashed about five times in that one try, on purpose, so a file that changes mid-delete is kept.
- A tree that was refused is hashed again later, after a wait that starts at an hour and doubles up to a day, and it stops after five refusals. It is not hashed every hour forever.
- The five-minute trace caught a single hourly run. Saving the repeated reads inside that one try is what cuts the cost. Remembering last hour's hash would not, and it could delete a file that changed quietly.
- Build junk such as `node_modules` is statted, not hashed. The hash cost is the real tracked files. CARD-1714, CARD-1754, and CARD-1756 do not need to be in this change.

## Remaining uncertainties

- The 12:15Z tick's candidate rows were not read. The SQL above is the count of first attempts versus retries. It does not change the recommendation, because a first attempt already does all five reads.
- Shares include time blocked in file syscalls. A PerfView trace would split CPU from those waits. It is not required to choose the cut.
- Both processes in the source trace were Debug builds. Release JIT would shrink managed hash overhead. It would not remove the extra file reads.
- This mirror has no build output, so it was not used as a stand-in for a desktop tree's ignored-file count.
