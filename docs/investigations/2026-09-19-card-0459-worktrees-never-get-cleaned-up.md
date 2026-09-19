# CARD-0459 — worktrees never get cleaned up (land-time / scheduled sweep)

Investigated 2026-09-19 (task `33cc8c8c`, Investigate). Evidence: live `git worktree list` and
`C:\Antiphon\worktrees` directory census; `AgentTasks` / `AgentTaskLandings` / `AgentTaskEvents` on
canonical Postgres (`antiphon-postgres`); `git ls-files --others --ignored --exclude-standard` on
four sample trees; source at HEAD `3c7a4057` (same SHA as live `GET /api/version` and
`C:\src\Antiphon`). CARD-0452 remains Backlog and owns D2 policy; this card owns triggering /
scheduling. No fix was designed or built.

## Verdict

**Confirmed, from live census plus stored landings.** Worktree removal on a successful land already
exists and does delete the directory when `IgnoredPaths` is empty: 64 landings since 2026-09-09
settled `Cleanup=Complete` with `DirectoryRemoved/RegistrationRemoved/BranchRemoved=true`, and the
five most recent of those paths are gone from disk. Every Code land that reached cleanup refused
instead. All 43 `Cleanup=Refused` rows have `LastReason=ignored_content_preserved` — the CARD-0452
D2 guard is still total (`HasProtectedIgnored` is `IgnoredPaths.Length != 0`). The original
35,446 ignored-path count on `card-task-c86499fb` still measures 35,446 today.

The larger pile is not land-residue. Of 494 `card-task-<8hex>` directories on disk, 446 have no
active landing row at all. `-Land` is explicit; settlement does not remove a worktree unless a
merge target is set and `GuardedWorktreeRemoval` accepts. Two scheduled sweeps already exist
(Hangfire `antiphon:worktree-residue` daily 10:00 Europe/London, and `WorktreeJanitorHostedService`
every `Git:WorktreeJanitorIntervalHours` default 24). Neither can delete: the residue classifier
never returns `Eligible` (hard-wired `Unknown` / "Evidence required"), `WorktreeResidue:Execute`
is false, and both the residue execute path and the janitor call the untyped
`TryRemoveAsync(repo, path, mergedInto)` which always returns `typed_removal_authority_required`.
There is no Windmill worktree-cleanup job in this repo.

CARD-0452 still owns the ignored-content policy. CARD-0459's remaining gap is a typed-authority
trigger for settled-never-landed trees (and land-residue retries) that respects that policy and
does not sweep unmerged or dirty work.

## 1. Live census (2026-09-19 ~21:54 UTC)

Observed against `C:\src\Antiphon` (`git worktree list`) and `C:\Antiphon\worktrees` (disk). The
card's 2026-09-09 snapshot was 93 git entries (92 task trees + main). Ten days later:

| Set | Count |
|---|---|
| Git-registered worktrees (all repos of this common dir) | 487 |
| Under `C:\Antiphon\worktrees` | 475 |
| Leaf `card-task-<8hex>` registrations | 460 |
| Nested under a card-task dir | 1 (`card-task-05a66230/.antiphon/c462-base`) |
| Other managed names (baselines / `card-CARD-0512` / `review-card-0534-v2`) | 9 |
| Outside managed root (main + `C:\Antiphon\evidence\*` + `verification\*` + `C:\src\Antiphon-card0417`) | 12 |
| Locked | 0 |
| Prunable | 1 (`review-card-0534-v2`: gitdir points at a missing location) |
| Disk directories under `C:\Antiphon\worktrees` | 512 |
| Of those, unregistered with git | 39 (32 `card-task-*`, plus `.antiphon`, `base-46fbc379`, `card-CARD-0004`, two `mutation-*`) |
| Unique `card-task-<8hex>` prefixes on disk | 494 |
| Sidecar metadata JSON under `C:\Antiphon\worktrees\.antiphon\worktrees` | 593 |

All 494 disk prefixes match an `AgentTasks` row on `left(Id::text, 8)`:

| Task status | Count | Notes |
|---|---|---|
| Dispatched (in flight) | 2 | this Investigate `33cc8c8c`; TestDesign `f7cf0fe7` |
| Succeeded | 406 | |
| Failed | 61 | |
| Canceled | 25 | |
| **Settled (Succeeded/Failed/Canceled)** | **492** | |
| Working / Queued / Blocked | 0 | |

Workspace-mode Worktree tasks in the whole database: Dispatched 2, Succeeded 965, Failed 91,
Canceled 45 (1,103). Most historical worktrees are gone; the 494 on disk are the residue that
current removal paths did not take.

Sample sizes: `card-task-c86499fb` 1,935 MB (35,446 ignored; top prefixes `client=32160`,
`tests=1600`, `server=699`, `src=519`, `.antiphon=467`); today's Code land `card-task-3646cd9c`
174 MB / 415 ignored; this Investigate tree 74 MB / 0 ignored. Full `C:\Antiphon\worktrees`
byte total was not summed.

### Remaining disk trees by role and landing

Active landing join on those 494 prefixes:

| Role | no landing | cleanup-refused (`ignored_content_preserved`) | landing-other | total on disk |
|---|---|---|---|---|
| Custom (0) | 22 | 0 | 0 | 22 |
| Plan (1) | 31 | 4 | 1 | 36 |
| Code (2) | 130 | 33 | 4 | 167 |
| Review (3) | 169 | 0 | 0 | 169 |
| Debug (4) | 12 | 0 | 0 | 12 |
| Docs (6) | 2 | 0 | 0 | 2 |
| Test (8) | 2 | 0 | 0 | 2 |
| Merge (10) | 12 | 0 | 1 | 13 |
| Investigate (14) | 31 | 4 | 0 | 35 |
| TestDesign (15) | 29 | 1 | 0 | 30 |
| Mutation (16) | 6 | 0 | 0 | 6 |
| **Total** | **446** | **42** | **6** | **494** |

Landing-other on disk: 3 `rebase_conflict`, 1 `source_changed`, 1 `target_dirty_or_unknown`,
1 `land_request_identity_conflict` (all `Cleanup=NotStarted`).

## 2. Land-time removal already exists — and works when ignored paths are empty

Site: `AgentTaskLandingProtocol.CleanupAsync`
(`server/Application/Services/AgentTaskLandingProtocol.cs:362-399`). After
`PublicationConfirmed` it sets `CleanupStarted`, then:

```383:385:server/Application/Services/AgentTaskLandingProtocol.cs
                removed = await worktrees.TryRemoveAsync(new(WorktreeRemovalPurpose.Publication, Coordinates(op),
                    op.CommonDirectory, op.GitDirectory, op.VerifiedSourceSha!, op.TargetBeforeSha, op.Id, lease,
                    CleanupContext: context), ct);
```

That is the typed overload (`WorktreeManager.cs:532-537`), which requires a managed-root path and
delegates to `GuardedWorktreeRemoval`. `op.Cleanup` becomes `Complete` or `Refused`
(`LandingProtocol.cs:394`). Docs already state this is `-Land`'s job, not an extra orchestrator
step (`docs/orchestration-loop.md:862-867`).

Settlement merge-back is a second typed path (`DelegationWorktreeService.RemoveLocalAsync:708-718`,
`WorktreeRemovalPurpose.LocalMerge`). It only runs when a merge target is set; otherwise
`TryMergeBackAsync` returns `LeftForHuman` and keeps the branch (`:573-574`). Pipeline Worktree
tasks typically have no merge target until an explicit land.

### Stored landings (all history)

| Publication | Cleanup | n | Meaning |
|---|---|---|---|
| Landed (1) | Complete (2) | 64 | push confirmed and worktree/branch removed |
| Landed (1) | Refused (3) | 41 | push confirmed; cleanup kept the tree |
| AlreadyPresent (2) | Refused (3) | 2 | contained; cleanup kept the tree |
| Unconfirmed / Refused | NotStarted | 8 | never reached cleanup |

`Cleanup=Refused` reasons: **43 / 43** `ignored_content_preserved`. No other refuse reason exists
in `AgentTaskLandings`.

Complete cleanups by role: Plan 34, TestDesign 17, Investigate 11, Merge 2. **Zero Code, zero
Review.** Five latest Complete paths (`39855395`, `33080007`, `eda54a92`, `ec339e70`, `6ab3c5ac`,
updated 2026-09-19 09:15–13:15Z) are absent from disk.

Refused Code example from today: `card-task-3646cd9c` (CARD-0574), `UpdatedAt` 2026-09-19
20:12Z, `Publication=Landed`, `DirectoryRemoved=f`, directory still present, 415 ignored paths.

Events: `LandRequested=357`, `Landed=166`, `LandRefused=63`, `LandedWithResidue=113`,
`AlreadyPresent=2`, `LandingCleanup=4`. Event counts exceed landing rows because retries append.

## 3. CARD-0452 D2 is still the land-time blocker for any ignored file

`LandingGit.InspectAsync` (`server/Infrastructure/Git/LandingGit.cs:144-152`) runs
`git ls-files --others --ignored --exclude-standard -z` and stores every path on
`LandSourceSnapshot.IgnoredPaths`. `GuardedWorktreeRemoval` then:

```54:60:server/Infrastructure/Git/GuardedWorktreeRemoval.cs
                // Non-forcing Git removal ALSO deletes ignored files. No patterns grant ownership.
                if (HasProtectedIgnored(inspection.Snapshot!)) return Finish("ignored_content_preserved");
                ...
                if (HasProtectedIgnored(final.Snapshot!)) return Finish("ignored_content_preserved");
```

```218:218:server/Infrastructure/Git/GuardedWorktreeRemoval.cs
    private static bool HasProtectedIgnored(LandSourceSnapshot snapshot) => snapshot.IgnoredPaths.Length != 0;
```

No allowlist. One ignored path is enough. `.gitignore` includes `bin/`, `obj/`, `node_modules/`,
`dist/`, `.antiphon/` (`.gitignore:2-3,8-9,55`). A Code worktree that has built, or any tree that
wrote `.antiphon/` scratch, cannot be removed by Publication or LocalMerge.

Pinned by `AgentTaskLandPublicationTests.C448_V04_IgnoredFilesSurviveConfirmedPublication`
(`.antiphon/report.md`, `.claude/settings.local.json`, `bin-private/data.txt`): publication may
be `AlreadyPresent`, cleanup `Refused`, `LastReason=ignored_content_preserved`, file bytes
preserved. CARD-0448 plan case A4 / review D2 (`docs/superpowers/plans/2026-09-08-card-0448-review-report.md:76-97`)
recorded this as fail-closed by design, not a coding error.

Live re-measure of the CARD-0452 example: `card-task-c86499fb` still exists, still on
`feat/card-task-c86499fb`, still **35,446** ignored paths (same figure as 2026-09-09). This
Investigate tree had **0** ignored paths at measurement — a land of it would currently complete
cleanup. That is why Plan/Investigate/TestDesign lands succeed and Code lands do not.

CARD-0452 is still Backlog. This investigation does not choose an allowlist vs opt-in override.

## 4. Scheduled sweeps already exist; both are authority no-ops

### Hangfire residue job (CARD-0328 S3)

- Registered at process start when `Hangfire:ServerEnabled` (`Program.cs:822-831`).
- Id `antiphon:worktree-residue`, cron `0 10 * * *` Europe/London, `Enabled=true`,
  **`Execute=false`** in both this worktree and live `C:\src\Antiphon\server\appsettings.json:147-153`.
- `WorktreeResidueJob` (`server/Infrastructure/Agents/WorktreeResidueJob.cs`) calls
  `WorktreeResidueSweepService.RunAsync`.
- Hangfire storage is in-memory (`HangfireConfiguration.cs`); last-run history does not survive
  AppHost restart. Dashboard: `http://localhost:17202/hangfire` (`docs/bootstrap.md:460-467`).

Classifier (`WorktreeResidueSweepService.ClassifyOne:131-178`): Live / Settling /
Unmerged / Dirty all `keep: true`. The last return is **not** `Eligible`:

```178:178:server/Application/Services/WorktreeResidueSweepService.cs
        return Row(facts, WorktreeResidueLabel.Unknown, "Evidence required", "Explicit landing cleanup receipt required; legacy events and TTL grant no authority", keep: true);
```

`IncludeUntracked` is hard-wired `true` (`:297`). `InspectResidueAsync` uses
`git status --porcelain -z --untracked-files=all --ignored` (`WorktreeManager.cs:461`);
`ParsePorcelainDirtiness` treats `!!` as untracked (`:481-483`). So a tree with only ignored
build output is `Dirty` before it could ever become Eligible. Tests pin this:
`classify_first_match_wins_for_each_label` expects the former Eligible fixture to display
"Evidence required" and `Keep=true`; `execute_never_treats_legacy_landed_event_as_cleanup_authority`
asserts `Counts.Eligible == 0` and `Removed == 0` even with `Execute=true`
(`WorktreeResidueSweepTests.cs:48-56,139-156`).

If `Execute` were flipped, `TryRemoveEligibleAsync` still calls the **untyped** remover
(`WorktreeResidueSweepService.cs:201-202`).

### Worktree janitor (in-process BackgroundService)

`WorktreeJanitorHostedService` (`server/Infrastructure/Git/WorktreeJanitorHostedService.cs:28-50`)
is registered (`Program.cs:649`). Every `max(1, WorktreeJanitorIntervalHours)` hours (default 24,
`GitSettings.cs:13`) it calls `PruneStaleAsync`. That walks sidecar metadata older than
`WorktreeStaleAfterDays` (default 7) and:

```590:590:server/Infrastructure/Git/WorktreeManager.cs
                var removal = await TryRemoveAsync(metadata.RepoPath, worktreePath, mergedInto: null, ct);
```

Untyped overload (`WorktreeManager.cs:524-530`):

```524:530:server/Infrastructure/Git/WorktreeManager.cs
    public Task<WorktreeRemoval> TryRemoveAsync(
        string repoPath, string worktreePath, string? mergedInto, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // An age, target name or legacy event is not authority to erase a working directory.
        return Task.FromResult(new WorktreeRemoval(false, false, false, "typed_removal_authority_required"));
    }
```

The janitor never reaches `HasProtectedIgnored`. Age / `LastTouchedAt` is not deletion authority
(CARD-0443 / CARD-0448). 593 metadata files vs 512 directories is consistent with metadata
outliving removed or never-cleaned trees.

### Worktree health sweep — detection only

`WorktreeHealthHostedService` (`WorktreeHealthHostedService.cs:11-12,57`) runs every
`WorktreeHealthIntervalSeconds` (default 60). Comment and log line: "detection only; nothing
pruned". Not a cleanup path.

### Windmill

No worktree-cleanup schedule in this repo. Nightly lives at `C:\Antiphon\nightly\checkout` as a
separate clone, not a worktree. `scripts/cleanup-build-junk.ps1` / Windmill
`u/lndcobra/antiphon_build_junk_cleanup` delete `bin-*` **inside** a checkout
(`docs/testing-and-build.md:84-89`); they do not unregister git worktrees. CARD-0459's "similar
in spirit to Windmill rather than a new local Scheduled Task" is a vehicle preference for Plan,
not an existing job.

## 5. What CARD-0459 owns vs CARD-0452

| Pile | Size now | Why it remains | Owner |
|---|---|---|---|
| Landed Code (and a few other) trees with ignored content | 42 dirs still present; 43 landing rows | Land ran; `GuardedWorktreeRemoval` refused `ignored_content_preserved` | CARD-0452 policy, then CARD-0459 retry/sweep using that policy |
| Settled, never landed | 446 dirs | `-Land` never ran; settlement `LeftForHuman` without merge target | CARD-0459 triggering / scheduling |
| In flight | 2 | must not be swept | both |
| Unmerged / dirty / no-task | residue classifier already keeps these | must stay kept | both |
| Unregistered leftover dirs, nested baselines, `C:\Antiphon\evidence\*`, `mutation-*` | 39 unregistered + 9 other managed + 12 outside | outside `card-task-<taskid>` land coordinates; some are verification snapshots | Plan should say whether they are in scope or a manual/triage follow-up |

Land-time triggering (ask 1) is already wired. It is not missing; it is blocked on Code by D2.
A scheduled sweep (ask 2) is also already wired twice, but both vehicles refuse by authority
before they can honour CARD-0452's guard. Flipping `WorktreeResidue:Execute` or shortening the
janitor interval will not delete anything.

## Remaining uncertainties

- Full byte size of `C:\Antiphon\worktrees` (three samples only). `c86499fb` alone is 1.9 GB.
- How many of the 130 never-landed Code trees (and 169 Reviews) are still ahead of `master` vs
  already contained. `InspectResidueAsync` would mark Unmerged when `ahead>0`; that was not run
  across 494 trees.
- `card-task-604d1c73`: landing row is `Cleanup=Refused` / `DirectoryRemoved=false` (2026-09-13)
  but the directory is gone. Removal happened outside this landing row (manual, later retry on a
  different operation, or a crashed `worktree remove`). One of 43 refused rows is not a live dir.
- Hangfire last successful residue run: in-memory store, not reconstructed from logs (log grep
  across `C:\logs\antiphon` timed out).
- Nested/baseline/evidence worktrees: whether CARD-0459's backlog-cleanup acceptance includes them.
- Whether any of the 39 unregistered directories still hold unique uncommitted work.

## Not done, noted

Plan should reuse the existing land-cleanup and residue/janitor vehicles with typed
`WorktreeRemovalRequest` authority plus CARD-0452's ignored-content policy, not add a third
force-delete path.
