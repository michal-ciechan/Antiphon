# Retire-temp refusal: CARD-1901 review fixtures in task-e479506f (2026-11-15)

Debug 7a422522. Read-only diagnosis. Nothing was mounted, deleted or written on server2.

## Outcome

Gate 9 of the 2026-11-15 server2 rollout refused twice with
`RecycleGitAuditUnknown audit check=ls-remote status=128 repo=worktrees/task-e479506f/.antiphon/evidence/card-1901/fixtures/b4b4e351ca424c2a953fb344cab5a756/origin.git reason=inventory`.
The cause is test fixtures. The CARD-1901 round-2 Review (e479506f) ran `VolumeRetirementPublicationTests`
on the temp runner. Its fixture producer writes deliberately unsafe Git shapes into the checkout and never
deletes them. There is **no real unpublished work** on the temp volumes. Every non-fixture commit is on an
origin head. The status-128 refusal is not the only blocker: at least two more fixture-caused refusals sit
behind it, and no trust-registry entry can clear them. Removing the fixture directory clears all of them.

The two inventory outputs (`c10088c4…/inventory-b8435f80….out` and `c1008ae8…/inventory-f0e1f05b….out`) are
byte-identical (1,121 lines). Neither operation published Ready (`lifecycle.json`: `readyPublished:false`,
`active:false`). No journal is open, so there is nothing to Resume.

## 1. Classification of every finding kind

The audit code is `scripts/c1008-content-audit.sh` (spliced into `scripts/c590-remote.sh`), invoked by
`c1609_prepare_exchange` in `scripts/c1008-retirement.sh`. A temp retirement runs these steps in order:

1. **Inventory** (`C1008_AUDIT_MODE=inventory`, `C1609_CONTENT_V2=1`). `c1609_list_offenders` prints every
   `C1609_FINDING` line. The per-entry structural loop runs over all repositories. Then each common Git
   directory is checked in turn: V2 makes `c1008_ls_remote_status` return 3, and the code then needs
   `git remote get-url origin`. A common directory with no `origin` remote calls `fail 128` under
   `audit_check=ls-remote`. **That is the observed refusal.** It stops at the first such directory.
2. **Registry**: every `C1008_PRIVATE_ORIGIN` must be listed in `retirement-origins.json`, or the run refuses
   `unapproved-origin`.
3. **Desktop proof exchange**: the coordinator proves each private origin's heads.
4. **Full audit with proof**: `c1609_prepare_common` refuses `no-remote-unclassified` for a common directory
   that has no origin and is not the CARD-1605 seed. `consider_common` proves every tip and every reflog
   side against origin heads (`RecycleUnpublishedWork`). `drain_dirty` checks dirty trees
   (`RecycleWorktreeDirty`). Then `c1609_finish_blocks` refuses `reason=evidence-name` or `reason=hook`.

| Finding kind (reason) | What it means | Blocking? |
|---|---|---|
| `evidence` (`evidence-name`) | A Git-named file (`HEAD`, `ORIG_HEAD`, …) outside any `.git`. This includes the `HEAD` of every bare repository, except the exact bash-edit-diff tool snapshot in tmp. | **Blocking, unconditionally.** It sets `c1609_block_reason`, and `c1609_finish_blocks` refuses `RecycleGitAuditUnknown reason=evidence-name`. No registry or proof can clear it. |
| `hook` | An executable non-sample hook | Blocking in the same way (`reason=hook`). There are none here. |
| `reflog` | Every non-zero reflog side, listed whether or not it is published | Informational as a listing. The block comes from `consider_common`: an unpublished side refuses `RecycleUnpublishedWork` (stop-and-report). |
| `fetch-head` | A FETCH_HEAD oid | Informational. An unpublished commit becomes a tip and goes through the same proof. |
| `dirty` | `git status --porcelain --untracked-files=all` is not empty | Informational. The real check is `queue_dirty`/`drain_dirty`, which accepts the no-index seed shape (HEAD deletions only). |

Inventory counts: 394 findings. 311 are evidence-name, 81 reflog, 1 fetch-head and 1 dirty. There is
1 disposable bash-edit-diff fixture in tmp. There are 328 entries over 325 common directories. The entry
loop completed for all 328 with no structural refusal, so only the per-common-directory loop stopped early.

### Blockers in order (the audit stops at the first)

| # | Step | Shape | Count | Refusal | Fixable by registry? |
|---|---|---|---|---|---|
| B1 | inventory | Fixture bare repos with no `origin` remote (`origin.git`, `backup.git`, `missing-verify.git`, `missing-bundle.git`, and any Git-shaped `audit-scratch/*/proof-1`) | ≥28, likely ≈311 | `ls-remote status=128 reason=inventory` (observed) | No |
| B2 | registry/proof | Fixture `work/repos/outer` (13), origin `https://fixture.invalid/card-1609/private.git` | 13 | `unapproved-origin`. If listed, the proof exchange fails, because `.invalid` never resolves. | No |
| B3 | full audit | The same no-origin bare repos | as B1 | `no-remote-unclassified` | No |
| B4 | full audit | evidence-name | 311 (13 origin.git, 13 backup.git, 2 missing-*.git, 283 `audit-scratch/c1008-audit-*/proof-1/HEAD`) | `reason=evidence-name` | No |
| B5 | full audit | Fixture reflog commit `7e347f86c2242bd0f79c951d643da26cea31f5c5` in all 13 `repos/outer` | 34 | `RecycleUnpublishedWork` (stop-and-report), if it were ever reached | No |

Is any finding stop-and-report? Not as observed: the run stopped at B1. If B1 to B4 were bypassed, B5
**would** yield `RecycleUnpublishedWork`. `7e347f86` is not in the desktop clone after
`git fetch --prune origin`, and no `git ls-remote origin` row names it. The same oid appears in all 13
fixtures because the fixture commits it with fixed bytes (`f.U`, "unique original bytes"). It is
synthetic test data by construction, not work.

`repos/antiphon` would yield neither stop-and-report condition:

- **Its `dirty` finding is informational.** The same mirror had the same `dirty` finding in the 2026-11-13
  preview `c10086c620d156d404d34af22b56aa4fbe8bd`. That preview reached `state: Ready` and was applied
  (`findings.jsonl` lists `repos/antiphon` kind `dirty`).
- **All 17 distinct reflog commits and the FETCH_HEAD are published** (desktop `git fetch --prune origin`,
  then `git rev-list --count <c> --not --remotes=origin` = 0 for each). The 14 master commits include
  `2972ffa39`, `1bd7b2ff6` (also the FETCH_HEAD) and `d100f8fa2`. `b6531be93` is on
  `feat/card-task-e479506f`. `e791df1b7` is on `feat/card-task-e479506f`, `-e594649c` and `-1c76938a`.
  `ec1188b6d` is on `feat/card-task-7bb75309`.

## 2. What `fixtures/b4b4e351…/origin.git` is

It is a deliberate fixture, not corruption. `tests/Antiphon.Tests/Scripts/VolumeRetirementPublicationFixture.cs`
`Create(preview: false)` exists only on the CARD-1901 branch (added in `9917511da`; the file is not on
master). It builds the following:

```
root = <RepoRoot>/.antiphon/evidence/card-1901/fixtures/<guid>
git init --bare root/origin.git          # the fixture's own "origin": a bare repo with no remote
git init --bare root/backup.git
root/work/repos/outer                    # origin = https://fixture.invalid/card-1609/private.git
  + unique commits U, X (unpublished by design)
```

A bare repository that serves as an origin has no `origin` remote of its own. So `git remote get-url origin`
fails, and the inventory reports that as `ls-remote status=128`. The class has no `Dispose`, and nothing
deletes `root`. The 13 fixture directories are 13 non-preview `Create` calls from the review's
`VolumeRetirementPublicationTests` run (33 tests, 23 m 33 s).

The `audit-scratch/c1008-audit-*/proof-1` repositories are proof clones that the audit helper itself made
while those tests ran.

This is the same defect class as CARD-1866 (2026-11-13). There, selector fixtures were placed under
`.antiphon/selector-evidence/` in a task checkout, and gate 9 refused the same way.

## 3. Unpublished work on the temp volumes: none

| Path | Task / role | Status (`delegate.ps1 -Status`) | Branch on origin (`git ls-remote`) | Findings |
|---|---|---|---|---|
| `repos/antiphon` | temp runner mirror | n/a | n/a | 1 dirty (accepted shape), 47 reflog, 1 fetch-head: all published |
| `worktrees/task-e479506f` | Review CARD-1901 round 2 | Succeeded (ended 08:31:56Z) | `feat/card-task-e479506f` = `b6531be93` | 345, all under `.antiphon/evidence/card-1901/fixtures/` (311 evidence-name, 34 fixture reflog) |
| `worktrees/task-3f159277` | Merge batch 240 | Succeeded | `feat/card-task-3f159277` = `d100f8fa2` (= master) | none |
| `worktrees/task-7bb75309` | Plan canary on server2-temp | Succeeded | `feat/card-task-7bb75309` = `ec1188b6d` | none |

The task ids were matched to the inventory's hashed entries by `sha256("/work/worktrees/task-<id>")`, using
the project task listing since 2026-11-14. All four `repos/antiphon` common entries are accounted for. The
task-e479506f checkout itself has no dirty finding, so the fixture tree is ignored content. Its HEAD and
branch are published.

## 4. Ways forward

### (a) Supported cleanup path for a retired temp volume: none exists

- **Runner worktree retention (CARD-1677)** is runner-side. It ships with `Runners=["server2"]`, and the
  runner must be online to recheck and act. `server2-temp` is retired and offline. Even online, e479506f
  ended 08:31Z, so it would not qualify until about 20:31Z (`MinTerminalHours=12`). Retention never touches
  Docker volumes.
- **`retire-runner-volumes.ps1`** offers `-Preview`/`-Apply` (`-Manual`/`-Template`/`-Resume` are mutually
  exclusive), and public manual Preview/Apply refuse `ManualPreviewApplyIncomplete`. `-Close` requires all
  four volumes to be absent. `-Resume` and `deploy-server2.ps1 -ResumeRecycle` need an open journal.
  There is none: no preview reached Ready.
- There is no "mark as evidence-only" switch.

The only way to remove the fixtures is a **manual, operator-authorised, one-off write to the retired
volume**. See the recommendation below.

### (b) Trust registry: no safe entry helps

The registry (`retirement-origins.json`, mode 600, 128 bytes) lists only
`https://github.com/michal-ciechan/Antiphon.git` for project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

- Adding `https://fixture.invalid/card-1609/private.git` would only move B2 from `unapproved-origin` to a
  `proof-exchange` failure, because `.invalid` cannot resolve.
- It would not touch B1, B3 or B4.
- It would make the registry trust a made-up domain.

**Do not change the registry.**

### (c) Leave the volumes in place

Leaving them is safe: nothing is lost, and standing server2 is upgraded and accepting. The cost is disk
space and the next rollout:

- This run's receipts and audit record no volume sizes (`docker system df` was not run, by rule). The
  previous temp generation was about 24 GB on 2026-11-13 (`_work` 18.45 GB, `_runner-tmp` 5.1 GB,
  `_dind-data` 388 MB, `_runner-state` 50 MB). That is a reference figure, not a measurement of these volumes.
- The **next `deploy-temp` (gate 1 of the next server2 rollout, i.e. the next rc activation on server2)
  refuses `TempVolumesRetained`**. The volumes must be gone before then.

### (d) Audit classifier for `.antiphon/evidence/**/fixtures/**`: recommended against

- **It removes the guard for exactly the case it exists for.** These fixtures are oracle shapes for
  "unpublished work must refuse": bare repos, reflog-only unique commits, configured private remotes. A
  classifier that passes them by path would pass real unpublished commits in any finished task whose agent
  wrote a repo under that path name.
- **It conflicts with the CARD-1901 round-2 semantics.** Round 2 renews the proof against live origin on
  every gate and refuses on withdrawal or unreachable origin before each `docker volume rm`. A path
  exemption skips publication proof altogether, a strictly weaker rule than the one CARD-1901 hardens.
- **The CARD-1866 precedent was much narrower, and that card still fixed the producer.**
  `selector-evidence-v1` accepts only ignored, no-remote, non-bare nested checkouts whose enclosing checkout
  is independently audited and published. These fixtures fail that shape: they have remotes, bare repos and
  unpublished commits. A new classifier would therefore need to accept unpublished commits.

**Fix the producer instead, before CARD-1901 lands.** `VolumeRetirementPublicationFixture` is not on
master yet, so the fix belongs in the CARD-1901 roster-repair round:

- Put the non-preview root outside the checkout. Use `/var/tmp`, as the preview path already does: it is
  the container layer, removed with the container. Do not use the `runner-tmp`-backed `/tmp`, because the
  audit scans tmp for evidence-name too.
- Make the fixture disposable and delete `root` in a `finally`.
- Add a witness that the fixture leaves nothing under `RepoRoot/.antiphon`.

Until it lands, any run of `VolumeRetirementPublicationTests` on a server2 runner re-creates this blocker.
That includes main-runner worktrees ahead of a `-RecycleVolumes`.

## Recommendation

Use (a′): one manual removal of the fixture subtree, authorised by the operator, then a fresh gate 9.
Separately, fix the producer as in (d).

**This needs an operator decision.** The standing authority covers previews and the token apply, not a
manual write into a Docker volume. Agents cannot write to these volumes or the registry, and no registry
change is needed.

Preconditions, checked by whoever runs it:

- temp is retired and offline, with zero sessions;
- `docker ps -a --filter label=com.docker.compose.project=antiphon-runner-temp` is empty;
- no rollout or retirement operation is in flight (`lifecycle.json` `active:false`);
- the orchestrator holds or pauses the rollout lock.

Commands, run on server2 as `mc`:

```bash
# optional evidence copy first (test data, small)
mkdir -p -m 700 /home/mc/antiphon-server2/salvage/gate9-2026-11-15
docker run --rm --network none -v antiphon-runner-temp_work:/w:ro --entrypoint tar alpine/git \
  -C /w/worktrees/task-e479506f/.antiphon/evidence/card-1901 -cf - fixtures \
  > /home/mc/antiphon-server2/salvage/gate9-2026-11-15/task-e479506f-card1901-fixtures.tar
# remove only the fixture subtree (uid 1654 owns the volume files)
docker run --rm --network none --user 1654:1654 -v antiphon-runner-temp_work:/w --entrypoint sh alpine/git \
  -c 'rm -rf -- /w/worktrees/task-e479506f/.antiphon/evidence/card-1901/fixtures'
```

Then the orchestrator runs gate 9 from the canonical checkout as usual:

1. `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <rollout sha> -Phase retire-temp -DryRun`
2. Within 900 s, set `$env:ANTIPHON_VOLUME_RETIREMENT_APPLY='enabled-<operation-id>'` and run
   `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <rollout sha> -Phase retire-temp -RetirementProof <operation-id>`.

Expected result: B1 to B5 are gone. What remains is the `repos/antiphon` accepted dirty shape and its
published reflogs, three published task worktrees, and the disposable tmp snapshot. That matches the
2026-11-13 Ready shape.

Any new refusal follows the gate-9 rule. `RecycleUnpublishedWork` or `RecycleWorktreeDirty` is
stop-and-report. Anything else gets one fresh preview and then a Debug task.

If the operator declines the manual write, use (c): leave the volumes. This rollout must then be resolved
before the next `deploy-temp`.
