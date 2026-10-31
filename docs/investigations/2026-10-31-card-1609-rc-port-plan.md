# CARD-1609 rc port: conflicts and the smallest safe path

Task 82977d6d (Investigate, server2/Linux, read-only on git). Date 2026-10-31.

Refs measured:

| Ref | SHA |
|---|---|
| `origin/rc/2026-10-27` | `e0d49d221513b467f8c853f47587e688e04255e2` |
| `origin/master` | `2051d327123a5e98524b4bd1ecf31c6e516bdb4e` |
| `origin/feat/card-task-1fa8a516` (round 13) | `33ab2cffb9cb04cc3aa2f735982dabedbd1d4488` |
| `origin/feat/card-task-a9634687` (round 14) | `33ab2cffb9cb04cc3aa2f735982dabedbd1d4488`, so nothing has been pushed yet |
| merge-base(1fa8a516, master), the CARD-1609 base | `98fc3ceb6440a17b68cafe3ecc21c76d190b18ca` |
| merge-base(rc, master) | `a97194ee7a4adf215454fd82c453ba46362596ee` (2026-10-23) |

## Verdict

**The rc port is cheap and mechanical at the git level: taken in order, it costs one hand-resolved hunk and one hand-fixed file. It compiles on rc, but two policy/roster tests go red because of CARD-1609 itself (they would on master too), so it needs one small Code fix-up (§4).**

- Neither the review's 213 conflicting files nor its 50 measured here come from CARD-1609. They come from the 933 master commits between the rc/master fork point `a97194ee7` and the CARD-1609 base `98fc3ceb6`, which a whole-branch merge drags in.
- A 3-way merge of only the CARD-1609 delta onto rc is **clean**: `git merge-tree --write-tree --merge-base=98fc3ceb6 origin/rc/2026-10-27 origin/feat/card-task-1fa8a516` exits 0 with tree `2f9296cc0d7d7c570b67e550f461c50d543236df`.
- Cherry-picking all 151 commits in order onto rc conflicts at exactly 2 commits, both in one file, `scripts/fixtures/c1008-fake-docker.sh`. The final tree is byte-identical to that squash tree.

CARD-1609 touches no project, props, targets, JSON, areas or `tests/Shared` file. It does not depend on CARD-1610, CARD-1545, run-graph or the land-graph definitions.

The blocker is not the merge. It is **behaviour**: at round 13, the production-default `redeploy-old` stops and removes the main runner, then refuses (review D-1). So **the port must wait for round 14** (§5).

## 1. The CARD-1609 commits

`git log --reverse 98fc3ceb6..origin/feat/card-task-1fa8a516` has 151 commits and 0 merges, from 2026-10-30 to 2026-10-31. They touch 59 paths, 44 of them outside `.antiphon/`:

- **16 `.antiphon/task-*.md`** reports, all new paths. rc tracks `.antiphon/` reports (287 files).
- **3 new docs:**
  - `docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md`
  - `docs/investigations/2026-10-30-card-1609-s5-checkpoint-debug.md`
  - `docs/investigations/2026-10-31-card-1609-s5-triage.md`

  rc carries `docs/superpowers/plans/` (1010 files), so the plan can travel with the port.
- **2 modified docs:** `docs/docker-stack.md` and `docs/orchestration-loop.md`.
- **13 new scripts and fixtures:**
  - `scripts/c1008-content-audit.sh`, `scripts/c1008-retirement.sh`, `scripts/c1008-volume-metadata.sh`
  - `scripts/c1609-host-proof.ps1`, `scripts/c1609-manual.ps1`, `scripts/c1609-read-packet.ps1`
  - `scripts/retire-runner-volumes.ps1`
  - `scripts/fixtures/c1609-*.mjs|cjs` (6 files)
- **7 modified scripts and fixtures:**
  - `scripts/c590-remote.sh`, `scripts/c590-real.ps1`, `scripts/deploy-server2.ps1`, `scripts/c1008-host-proof.ps1`
  - `scripts/fixtures/c1008-fake-docker.sh`, `scripts/fixtures/c1105-git-audit.cjs`
  - `scripts/fixtures/c727-fake-http.ps1`, `scripts/fixtures/c727-fake-verify.ps1`
- **8 new tests:** `tests/Antiphon.Tests/Scripts/C1008ComposeSeed.cs` and `VolumeRetirement{ApplyGate,Content,Docker,Fixture,HostProof,Receipt,Wrapper}*.cs`.
- **11 modified tests:**
  - `DockerStackDocumentationTests`, `C1364BoundLandWaitProofTests`, `C994ScriptProcess`, `RemoteScriptContractTests`
  - `RetiredTempContainer{Host,Script}Tests`, `RollingVolumeRecycleScriptTests`
  - `WindowsUnitPlatformAdmissionTests` (roster count 61→89 identities, 72→100 results) and `WindowsUnitSkipRoster` (+28 rows)
  - `slow-tests-allowlist.txt`

The full per-commit file table is in the appendix.

### Grouping

**(a) Self-contained:** 89 of the 151 commits touch only new files, or files whose rc bytes equal the CARD-1609 base bytes. The files whose rc bytes equal base are:

- `deploy-server2.ps1`, `c590-real.ps1`, `c1008-host-proof.ps1`
- `c727-fake-*`
- `DockerStackDocumentationTests`, `C994ScriptProcess`, `WindowsUnitPlatformAdmissionTests`

**(b) Touches a file rc has diverged on:** 62 commits. The rc-side divergence from base (`git diff 98fc3ceb6 origin/rc/2026-10-27`) is small in every one of these files. All of it is rc-side work that master also carries, except `744a9a3d8`:

| File | rc vs base | rc commits that diverge it | On master? |
|---|---|---|---|
| `scripts/c590-remote.sh` | +16/−4 | `744a9a3d8` C849 finalize-in-signal-handler (picked from `4272c9ad4`); `5ee70db1c` CARD-1614 custody-tree residue count | `744a9a3d8`: **no** (master lacks those 3 lines). `5ee70db1c`: yes, as `6898ab6f7`, after the CARD-1609 base |
| `scripts/fixtures/c1008-fake-docker.sh` | +32/−1 | `62b7c59c6` chroot PATH (picked from `ad7cbc6ae`), the same fix as CARD-1609 #20 `685f41559` | yes |
| `docs/docker-stack.md` | 1 line | `744a9a3d8` | no |
| `docs/orchestration-loop.md` | +58/−13 | CARD-1614 `8da9b8c18`, `3d81f4a9a` | partly |
| `scripts/fixtures/c1105-git-audit.cjs` | +84/−3 | `ed17a208b` C1105 resume extraction | yes |
| `RemoteScriptContractTests.cs` | +151/−1 | `b60f27353`, `32c7d1390`, `47cebd77b`, `5c1ef4db5`, `49322b8e0`, `ed17a208b`, `bd59c1b6c` | mostly |
| `RollingVolumeRecycleScriptTests.cs`, `RetiredTempContainer{Host,Script}Tests.cs`, `C1364BoundLandWaitProofTests.cs` | `[NativeLinuxOnly]` attributes only | `bd59c1b6c` (picked from `4809ad451`) | yes |
| `WindowsUnitSkipRoster.cs` | +3 rows | `e0d49d221` capture-identity, `ed17a208b` C1105 resume | yes |
| `slow-tests-allowlist.txt` | −5 (rc has no CARD-1603) | n/a | master has CARD-1603 |

**No CARD-1609 commit depends on master-only work.** The following were checked, and none is in the CARD-1609 file set:

- CARD-1610 migration
- CARD-1545
- the CARD-1619 chain
- run-graph
- land-graph definitions and their digest
- `antiphon.areas.json`
- `tests/Shared`

Of the 49 repo paths the CARD-1609 scripts and tests reference by literal path, every one exists in the ported tree. The 3 that do not resolve are regex artifacts (`src/scripts/...`) and are absent at the tip too.

For the three scripts, `git diff --stat origin/rc/2026-10-27 origin/master` shows:

- `c1008-retirement.sh` is absent on both.
- `deploy-server2.ps1` is byte-identical.
- `c590-remote.sh` differs by **3 lines**, the rc-only `744a9a3d8`.

The brief's three-dot form shows `+1301/−77` for c590-remote.sh. That measures from the old fork point `a97194ee7`, and rc already has every line of it through ports.

## 2. Why the whole-branch merge conflicts

Measured with `git merge-tree --write-tree origin/rc/2026-10-27 origin/feat/card-task-1fa8a516`:

- **50 conflicted files:** 35 content and 15 add/add, as 135 index-stage lines. The review's 213 was not reproduced at the same SHAs, and no counting basis here gives 213. rc onto master alone is 51.
- **Only 10 of the 50** are CARD-1609 files: `docker-stack.md`, `c1008-host-proof.ps1`, `c590-real.ps1`, `c590-remote.sh`, `c1008-fake-docker.sh`, `RemoteScriptContractTests`, `RetiredTempContainer{Host,Script}Tests`, `RollingVolumeRecycleScriptTests` and `WindowsUnitSkipRoster`.
- **They conflict only in the whole-branch form.** The merge base is `a97194ee7`, so rc's ported copies of master work (CARD-1008/1105/1594/C849 fixes that were cherry-picked to rc under different SHAs) collide with the originals that CARD-1609 inherits through `98fc3ceb6`. That base carries 933 master commits and 762 files after the fork point.

So the cause is the **unrelated master delta in the branch base**, not CARD-1609 rewriting scripts rc has diverged on.

## 3. Dry run (scratch detached worktree from rc, nothing pushed)

`git cherry-pick --allow-empty --keep-redundant-commits <c>` was run for all 151 commits in order, without `-x`. On a conflict, each file was rebuilt as `git merge-file -p <rc file> <98fc3ceb6 file> <commit's file>`.

| Step | Commit | Conflicting files | Kind |
|---|---|---|---|
| #2 | `678655489` test: record CARD-1609 content-retirement red witnesses | 1, `scripts/fixtures/c1008-fake-docker.sh` | **one 1-line semantic hunk** (below) |
| #20 | `685f41559` fix: keep chroot on the CARD-1609 preview helper PATH | 1, the same file | resolves cleanly by 3-way: it is the original of rc's `62b7c59c6` |
| every other step (149) | n/a | 0 | clean |

Result: 151 commits and no empty commits. The final tree is `2f9296cc0d7d7c570b67e550f461c50d543236df`, **identical** to the squash 3-way tree, with no conflict markers anywhere.

**The #2 hunk.** rc has the chroot form; CARD-1609 #2 adds `helperAuth` to the old form:

```
<<<<<<< rc
  const inheritedPath = process.env.PATH || '/usr/bin:/bin';
  env.PATH = offline
    ? path.join(root, 'git-shim') + ':' + pathWithChroot(inheritedPath)
    : pathWithChroot(inheritedPath);
=======
  if (offline || helperAuth) env.PATH = path.join(root, 'git-shim') + ':' + (process.env.PATH || '/usr/bin:/bin');
>>>>>>> 678655489
```

Resolution: keep both intents, as rc's chroot form with the condition `(offline || helperAuth)`. At #20, take the clean 3-way result. From #20 on, the file equals the CARD-1609 tip's own bytes, because `685f41559` aligned it to master.

**Semantic defect the clean merge hides: `tests/Antiphon.Tests/slow-tests-allowlist.txt`.** rc lacks CARD-1603, so its allowlist does not have the two `CapacityObsoleteQueueTests` and `CapacityObsoleteStandingTests` rows. CARD-1609 #61 `e2845a8a8` appends a row directly below them. Both the 3-way and the cherry-pick then re-add the two CARD-1603 rows without a conflict: `git diff origin/rc/2026-10-27 2f9296cc0 -- tests/Antiphon.Tests/slow-tests-allowlist.txt` shows +5 CARD-1603 lines. Those rows name classes that do not exist on rc. Fix by hand: keep only the CARD-1609 `VolumeRetirementDockerTests` row.

**Three-way, taking master's versions wholesale.** Master's three scripts are already rc's bytes, except c590-remote.sh, where master lacks the rc-only `744a9a3d8`. So taking master's copies gains nothing and loses `744a9a3d8`.

Taking the **CARD-1609 tip's** copies wholesale: `git diff origin/feat/card-task-1fa8a516 2f9296cc0 -- scripts/c590-remote.sh` is `+17/−4`. That would lose two rc edits:

- `744a9a3d8`: the C849 `c849_fixture_finalize` call inside `c849_fixture_on_signal`, `c590-remote.sh:4605-4607` on rc.
- CARD-1614 `5ee70db1c`: the namespace-aware custody residue count in `case_custody_containment`, rc `c590-remote.sh:9782-9797`.

`deploy-server2.ps1`, `c590-real.ps1`, `c1008-host-proof.ps1` and `c1008-fake-docker.sh` lose nothing; the tip and the port are identical for them. The wholesale take is unsafe for c590-remote.sh and unnecessary for the rest.

All other rc-only edits in the touched files survive the port exactly. This was checked by comparing the sorted ± lines of `98fc3ceb6→rc` against `tip→port` per file. They match for all but fake-docker (expected, since rc's delta is CARD-1609 #20) and slow-tests-allowlist (the defect above).

## 4. Smallest safe plan (after round 14)

Preconditions:

- Round 14 (task a9634687) is pushed, reviewed clean on master, and fixes D-1 and D-2.
- Re-derive the commit list as `git rev-list --reverse 98fc3ceb6..<round-14 tip>`. If round 14 rebased or re-based, use its new merge-base with master instead.

Steps for a Merge task (Grok High is adequate):

1. Create a branch at `origin/rc/2026-10-27`. Use the current rc tip if it has moved, and re-run the squash check below first.
2. Gate: `git merge-tree --write-tree --merge-base=98fc3ceb6 <rc> <round-14 tip>` must exit 0. If it does not, stop and report the files.
3. Cherry-pick, without `-x` per this brief (rc practice elsewhere uses `(cherry picked from commit …)` trailers; the caller decides), the 151 commits listed in the appendix in order, then the round-14 commits.
   - At #2 `678655489`, resolve `c1008-fake-docker.sh` as above: chroot form with `(offline || helperAuth)`.
   - At #20 `685f41559`, take `git merge-file -p <rc copy> <98fc3ceb6 copy> <685f41559 copy>`.
   - Expect no other conflict. Round-14 commits are unmeasured.
4. One fix-up commit on the port branch: delete the two resurrected CARD-1603 rows (and their blank separator) from `tests/Antiphon.Tests/slow-tests-allowlist.txt`. Then a Code slice (preferably landed on master first and picked): drop the two deleted `RetiredTempContainerHostTests` methods from `tests/Shared/NativeLinuxWindowsBaseline.json`, and raise the C1210 frozen TRX totals in `WindowsUnitPlatformAdmissionTests.ValidDocument` by the 28 new Native results.
5. Verify the port's tree equals the squash tree, apart from the step-4 allowlist edit: `git diff <squash tree> HEAD` must show only the allowlist.
6. One narrow green series on Linux (server2), through the checkpoint tool or build-slot:
   - build `tests/Antiphon.Tests` once;
   - run `RollingVolumeRecycleScriptTests`, `VolumeRetirement*Tests` (Content, Receipt, ApplyGate, HostProof, Wrapper), `RetiredTempContainer{Host,Script}Tests`, `RemoteScriptContractTests`, `C1364BoundLandWaitProofTests` and `DockerStackDocumentationTests`;
   - run `WindowsUnitPlatformAdmissionTests` and `NativeLinuxPolicyWitnessTests` for the roster count and NativeLinuxOnly policy on rc.

   Exclude `VolumeRetirementDockerTests`: it is on the slow allowlist and needs the owned daemon. Run it only if the CP-10 lane is available.

   The Windows qualification run on the rc line is the gate. CP-11/12/13 are native-Windows rows.
7. Land graph: no definition file changes (no csproj, props or targets in the delta). `scripts/audit-land-verification-graph.ps1 -Check` on the port should stay at rc's `69 definitions, digest E7C6EDAF…`. The CARD-1609 round-13 runs recorded `66 / 20DE49F6…` because they ran on the master base. Run `-Check` once, never `-Update`.
8. `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` must be clean. Land onto rc.

**Measured on the port tree (server2, build-slot).** Two runs, one per tree:

- **Port tree.** I built `tests/Antiphon.Tests` at the dry-run head (tree `2f9296cc0`, without the allowlist fix): exit 0, 0 errors, 5m04s. **The ported tree compiles on rc.** Then I ran `/*/*/(NativeLinuxPolicyWitnessTests*)|(WindowsUnitPlatformAdmissionTests*)/*` from the built dll. Two failed and one was skipped (`C1210_Windows_linux_only_outcomes_are_exact`, Windows-only). The run then hit my 580 s wall timeout, so the remaining counts were not captured and the long-running case was not identified. No process was left behind.
- **rc base.** I rebuilt the same scratch worktree at rc `e0d49d221` (incremental) and ran the same two failing methods exactly: **2/2 passed, 11.9 s.** Both reds are therefore caused by the port.

The two reds:

1. `NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute` fails with `baseline-unresolved suite=antiphon row=RetiredTempContainerHostTests` for `C994_Audit_image_is_pinned_and_required()` and `C994_Image_receipt_lookup_is_bound()`.
   - CARD-1609 #54 `de1a52dd7` deletes those two methods.
   - `tests/Shared/NativeLinuxWindowsBaseline.json` on rc lists each of them 4 times, and master's copy has the same rows.
   - **The master land has the same semantic red.** It is hidden on master only if the separately known master-inherited red of this test masks it.
   - Repair: remove the rows from the baseline. If the policy file changes, its `policyHash` must be resealed; the baseline itself is pinned as an embedded resource.
2. `WindowsUnitPlatformAdmissionTests.C1210_Audit_rejects_empty_or_incomplete_outcomes` fails on counter `total`, expected `"104"` but was `"76"`.
   - `ValidDocument` hard-codes the frozen TRX totals `76`/`109`/`72`/`74` from the 72-result Native roster (`WindowsUnitPlatformAdmissionTests.cs` around line 331).
   - CARD-1609 raised Native to 89 identities and 100 results (#2 and the later roster commits) but did not move those constants.
   - This holds by construction at the CARD-1609 tip on master as well, so it is not rc-specific.

So the port needs **one Code fix-up slice** (baseline rows, and C1210 frozen totals +28), done identically on master. It is not a merge-only task. This is a fix idea only; it is not designed here.

Size estimate:

- Steps 1-5: about 30-45 minutes of a Grok High Merge task, being 151 picks, one hand hunk and one fix-up.
- Step 6: build about 10 minutes plus test rows about 30-60 minutes. The round-13 review's 4-row run was 57 minutes on server2.
- Total: **about 1.5-2 hours** of wall clock, most of it tests.

A squash alternative (one commit from the clean 3-way tree, plus the allowlist fix) cuts steps 1-5 to about 10 minutes, but loses per-commit history and red→green provenance on rc.

## 5. Risks to a production host (rc tip is live for AppHost and server2)

These are the default-behaviour changes in CARD-1609 at round 13, read from the tip's `deploy-server2.ps1`, `c590-remote.sh` and `docs/docker-stack.md`:

1. **`redeploy-old` (gate 6) refuses by default and, at round 13, takes server2 main down first (D-1).**
   - `deploy-server2.ps1:1383-1386` always passes a non-dry-run recycle context to `deploy-parent`. That is `c1008_recycle antiphon-runner` (`c590-remote.sh:10450`).
   - `c1609_apply_gate` (`c590-remote.sh:9156`, called at `:9462`) runs **after** `docker stop` (`:9416`) and `docker rm` (`:9428`).
   - Result: `apply-gate-off`, exit 2, the old runner removed, and no scripted recovery that keeps the volumes.
   - Round 14 must gate before any effect, **and** decide whether redeploy-old without a token is a plain upgrade that keeps volumes. The review recommends that. Otherwise every rollout stops at gate 6.
2. **The `retire-temp` apply now needs a token, and a gate-off apply consumes the Ready preview (D-2).**
   - Normal `retire-temp` now runs a full content Preview host case first (`deploy-server2.ps1` around 1455-1460, from `1def748dc`), then Apply.
   - Apply refuses `apply-gate-off` unless `ANTIPHON_VOLUME_RETIREMENT_APPLY=enabled-<operationId>`.
   - At round 13 the refusal leaves the preview consumed (`state=Applying`), so the documented `-RetirementProof` path cannot apply it.
3. **`deploy-temp` (gate 1) refuses `TempVolumesRetained`** while any of the four `antiphon-runner-temp_{work,runner-tmp,dind-data,runner-state}` volumes exists, or while a journal is unresolved.
   - This comes from `e0f13fb77`, using `Assert-TempRetirementClearable` (an ssh census before `/drain/clear`, and again inside the `/drain/clear` POST for server2-temp).
   - With apply off by default, **the first rollout after the port leaves temp volumes, and the next `deploy-temp` refuses** until an enabled `-RetirementProof` apply or a `retire-runner-volumes.ps1 -Close` runs.
   - If server2 already has retained temp volumes from earlier rollouts, the **very first** deploy-temp on the ported rc refuses. Not checked: no docker command against a real host was allowed.
4. **Temp retirement admission is content-derived, not image-derived** (`9c8021277`, `e0f13fb77`). The historical image-equality refusal is gone, and a content audit runs in a read-only pinned helper container on the host during preview and apply. The host must have the helper image and `jq`.
5. **`-Sha` is optional for `-Phase retire-temp`.** It defaults to `git rev-parse HEAD` of the desktop checkout, and other phases throw `ShaRequired`. There is also a new `-RetirementProof` parameter.
6. **New dot-sourced dependency:** `deploy-server2.ps1` now dot-sources `scripts/c1609-read-packet.ps1`. A partial port that omits that file breaks every phase at startup.
7. **The c590-real bridge** forwards `ANTIPHON_VOLUME_RETIREMENT_APPLY` to the host only when it matches `^enabled-c1008[0-9a-f]{32}$` (`509f20891`). It travels on the ssh command line (review: advisory).

Recommendation: do not land on rc before round 14 is reviewed clean for D-1 and D-2. Run the first ported rollout's gate 9 as `-DryRun` only, as the runbook already says. Before the first deploy-temp, an operator should read whether any `antiphon-runner-temp_*` volume exists on server2.

## Remaining uncertainties

- Round 14 is not yet pushed, so its commits, conflicts and any new default changes are unmeasured.
- Only the two policy/roster classes were run on the port. The volume-retirement rows (step 6) were not run on rc, and the first probe's long-running case is unidentified (580 s wall timeout).
- Whether server2 currently holds retained `antiphon-runner-temp_*` volumes (risk 3) needs an operator read on the host.
- The review's 213 count was not reproduced. It is moot for the plan.

## Not done, noted

- Fix idea (not designed): gate `c1609_apply_gate` immediately after context validation in `c1008_recycle`, and make `redeploy-old` request no recycle unless the operator opts in. Round 14 owns this.

## Appendix: commits in order

Path abbreviations: `S/`=`scripts/`, `F/`=`scripts/fixtures/`, `T/`=`tests/Antiphon.Tests/Scripts/`, `DI/`=`docs/investigations/`, `PLAN/`=`docs/superpowers/plans/`.

| # | Commit | Subject | Files |
|---|---|---|---|
| 1 | `084fd001d` | docs: adopt the reviewed CARD-1609 volume-retirement plan | PLAN/2026-10-29-card-1609-content-derived-volume-retirement-plan.md |
| 2 | `678655489` | test: record CARD-1609 content-retirement red witnesses | F/c1008-fake-docker.sh T/VolumeRetirementContentTests.cs T/VolumeRetirementFixture.cs T/VolumeRetirementReceiptTests.cs T/WindowsUnitPlatformAdmissionTests.cs T/WindowsUnitSkipRoster.cs |
| 3 | `62e0dd6df` | docs: record the CARD-1609 S1 red checkpoint report | .antiphon/task-958507f1.md |
| 4 | `9c8021277` | fix: admit temp volume retirement from content, not historical images | S/c1008-host-proof.ps1 S/c590-real.ps1 S/c590-remote.sh T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementContentTests.cs |
| 5 | `82f627dfd` | docs: record the CARD-1609 S2 admission boundary | .antiphon/task-404814e4.md |
| 6 | `c422a31a1` | test: record CARD-1609 C3-C14 content-retirement witnesses | T/VolumeRetirementContentTests.cs T/VolumeRetirementFixture.cs T/WindowsUnitPlatformAdmissionTests.cs T/WindowsUnitSkipRoster.cs |
| 7 | `e69af3177` | fix: classify CARD-1609 retirement by seed bytes, fetch-head, and role metadata | S/c1008-content-audit.sh S/c1008-volume-metadata.sh S/c590-remote.sh F/c1008-fake-docker.sh T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementContentTests.cs |
| 8 | `4b671ea48` | fix: splice the CARD-1609 content audit without bash replacement metacharacters | S/c590-remote.sh |
| 9 | `f6a626c9c` | fix: classify an off-path nested repo instead of stopping on the parent status | S/c590-remote.sh |
| 10 | `7dd442e39` | test: expire the CARD-1609 C3 reflog before the published-main control | T/VolumeRetirementContentTests.cs |
| 11 | `fd44854c4` | docs: record the CARD-1609 S2 content-audit boundary | .antiphon/task-cba5ec74.md |
| 12 | `6327a3e30` | fix: require a Ready preview before temp volume retirement deletes | S/c1008-retirement.sh S/c590-remote.sh T/C1364BoundLandWaitProofTests.cs T/RemoteScriptContractTests.cs T/RetiredTempContainerHostTests.cs T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementFixture.cs |
| 13 | `18a1e31c0` | fix: keep the temp retirement preview on the fixture docker stub | T/VolumeRetirementFixture.cs |
| 14 | `c956a5cb6` | fix: keep the unpinned temp dry-run read-only on the fixture docker | T/RemoteScriptContractTests.cs |
| 15 | `8803ab531` | docs: record the CARD-1609 S3 receipt boundary | .antiphon/task-14947ff2.md |
| 16 | `e0f13fb77` | feat: refuse retained temp volumes before deploy-temp clears | S/c1008-retirement.sh S/c590-remote.sh S/deploy-server2.ps1 F/c727-fake-verify.ps1 S/retire-runner-volumes.ps1 T/RemoteScriptContractTests.cs T/RetiredTempContainerHostTests.cs T/VolumeRetirementHostProofTests.cs T/VolumeRetirementWrapperTests.cs |
| 17 | `483748fc3` | fix: compile the CARD-1609 wrapper and host-proof rows | T/VolumeRetirementHostProofTests.cs T/VolumeRetirementWrapperTests.cs |
| 18 | `e99808feb` | fix: keep a retire-temp preview from deleting volumes | S/deploy-server2.ps1 F/c727-fake-http.ps1 F/c727-fake-verify.ps1 T/VolumeRetirementWrapperTests.cs |
| 19 | `48d134902` | fix: publish the new temp SHA from the c1008 slot | F/c727-fake-verify.ps1 |
| 20 | `685f41559` | fix: keep chroot on the CARD-1609 preview helper PATH | F/c1008-fake-docker.sh |
| 21 | `6787b9ac8` | test: pin the CARD-1609 retained-volume runbook | tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs |
| 22 | `a9ed7b7bf` | docs: name the retained-volume gate and the v2 census | docs/docker-stack.md docs/orchestration-loop.md |
| 23 | `63b4994b8` | test: expose retirement receipt custody incarnation and age gaps | T/VolumeRetirementReceiptTests.cs T/WindowsUnitPlatformAdmissionTests.cs T/WindowsUnitSkipRoster.cs |
| 24 | `89b4c5a49` | fix: bind retirement previews to protected fresh volume snapshots | S/c1008-retirement.sh S/c590-remote.sh T/VolumeRetirementReceiptTests.cs |
| 25 | `9b2cbd6b6` | test: expose changed-inventory and per-delete retirement guard gaps | F/c1008-fake-docker.sh T/VolumeRetirementReceiptTests.cs T/WindowsUnitPlatformAdmissionTests.cs T/WindowsUnitSkipRoster.cs |
| 26 | `8536d74cf` | fix: re-audit temp inventory before exact volume deletion | S/c1008-content-audit.sh S/c1008-retirement.sh S/c590-remote.sh |
| 27 | `7ad5d070f` | fix: recheck each retirement incarnation and preserve local metadata inventory | docs/docker-stack.md docs/orchestration-loop.md S/c1008-content-audit.sh S/c1008-retirement.sh S/c590-remote.sh tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs T/RemoteScriptContractTests.cs |
| 28 | `0acf5373c` | fix: refuse missing retirement preview before starting the helper | S/c590-remote.sh |
| 29 | `c80f5de16` | test: retain real timeout custody through the fake Docker executable | F/c1105-git-audit.cjs T/RemoteScriptContractTests.cs T/RetiredTempContainerHostTests.cs T/RollingVolumeRecycleScriptTests.cs |
| 30 | `89a54bc05` | docs(card-1609): publish partial continuation evidence and remaining gates | .antiphon/task-b2fc0aa5.md |
| 31 | `0080d76f8` | docs(card-1609): finalize checkpoint outcomes and repair evidence | .antiphon/task-b2fc0aa5.md |
| 32 | `efef66d31` | docs: record CARD-1609 S5 checkpoint failure diagnosis | DI/2026-10-30-card-1609-s5-checkpoint-debug.md |
| 33 | `a9ed39fa0` | test: repair host proof fixture and share immutable Compose setup | DI/2026-10-30-card-1609-s5-checkpoint-debug.md T/C1008ComposeSeed.cs T/C994ScriptProcess.cs T/RetiredTempContainerHostTests.cs T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementFixture.cs |
| 34 | `73592e19f` | test: refresh Ready preview after private-origin refusal vectors | DI/2026-10-30-card-1609-s5-checkpoint-debug.md PLAN/2026-10-29-card-1609-content-derived-volume-retirement-plan.md T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementFixture.cs |
| 35 | `a51c4a4bf` | docs: record assigned-base reproductions and Debug measurements | DI/2026-10-30-card-1609-s5-checkpoint-debug.md |
| 36 | `e52c3b889` | test: pin large audit refusals and actual temp helper transport | T/RetiredTempContainerHostTests.cs T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementContentTests.cs |
| 37 | `e6f7cb25a` | fix: drain failed audit output before returning typed refusals | DI/2026-10-30-card-1609-s5-checkpoint-debug.md S/c590-remote.sh |
| 38 | `ebdb81623` | docs: publish CARD-1609 Debug timings and remaining S5 qualification | DI/2026-10-30-card-1609-s5-checkpoint-debug.md PLAN/2026-10-29-card-1609-content-derived-volume-retirement-plan.md |
| 39 | `8b238cbfc` | docs: retain complete CARD-1609 Debug handoff report | .antiphon/task-a2935adb.md |
| 40 | `0b8ec8143` | test: expose temp retirement partial-resume journal cuts | T/VolumeRetirementReceiptTests.cs T/WindowsUnitPlatformAdmissionTests.cs T/WindowsUnitSkipRoster.cs |
| 41 | `bec905c77` | fix: reconcile temp retirement intents before partial resume | S/c1008-retirement.sh S/c590-remote.sh |
| 42 | `c7acd9f2e` | fix: admit journaled absent temp resume without an old stack SHA | S/c590-remote.sh |
| 43 | `3556e647b` | test: retain persisted Docker absence before repeat resume | S/c590-remote.sh T/VolumeRetirementReceiptTests.cs |
| 44 | `566bbe508` | perf: flush retirement files and directories without syncing the filesystem | S/c1008-retirement.sh T/VolumeRetirementReceiptTests.cs |
| 45 | `3057c4733` | test: witness supported writer exclusion with real flock barriers | T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementReceiptTests.cs T/WindowsUnitPlatformAdmissionTests.cs T/WindowsUnitSkipRoster.cs |
| 46 | `df5cfbeca` | fix: preserve raw-string interpolation in the flock witness | docs/docker-stack.md T/VolumeRetirementReceiptTests.cs |
| 47 | `165285a28` | test: require fresh temp role proof when resumed work is absent | T/VolumeRetirementReceiptTests.cs |
| 48 | `ca39f5784` | test: preserve literal JSON braces in the resume adapter witness | T/VolumeRetirementReceiptTests.cs |
| 49 | `89464b367` | fix: always re-audit temp roles instead of using the v1 absent-work cache | S/c590-remote.sh |
| 50 | `f00e6a7a8` | docs: report CARD-1609 resume witnesses and partial S5 qualification | .antiphon/task-a869b1b6.md |
| 51 | `afb8ffa29` | test: require temp cleanup independent of historical generation | T/RetiredTempContainerHostTests.cs |
| 52 | `91f3e7966` | fix: admit retired temp topology without historical generation | S/c590-remote.sh T/RetiredTempContainerHostTests.cs T/VolumeRetirementWrapperTests.cs |
| 53 | `c8a593085` | test: select actual topology target and require current audit runtime | T/RetiredTempContainerHostTests.cs T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementContentTests.cs |
| 54 | `de1a52dd7` | fix: select protected current tooling independently of retired builds | docs/docker-stack.md S/c1008-retirement.sh S/c590-real.ps1 S/c590-remote.sh S/deploy-server2.ps1 tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs T/RetiredTempContainerHostTests.cs T/RetiredTempContainerScriptTests.cs T/VolumeRetirementContentTests.cs |
| 55 | `5d5b73395` | test: retain main bind metadata guard for absent containers | T/VolumeRetirementWrapperTests.cs |
| 56 | `30e93b464` | fix: retain main mount validation and cleanup provenance | S/c590-remote.sh |
| 57 | `0b4703df2` | test: use named diagnostic argument in main bind witness | T/VolumeRetirementWrapperTests.cs |
| 58 | `30cf65bb5` | docs: record CARD-1609 continuation and remaining S5 gates | .antiphon/task-4c8e4fc2.md |
| 59 | `57cefc5e6` | test: require exact manual authority through retirement receipts | T/VolumeRetirementReceiptTests.cs T/WindowsUnitPlatformAdmissionTests.cs T/WindowsUnitSkipRoster.cs |
| 60 | `290499d6d` | fix: bind manual retirement to protected authority and exact attestation | S/c1008-retirement.sh T/VolumeRetirementReceiptTests.cs |
| 61 | `e2845a8a8` | test: add owned native retirement namespace and volume witnesses | F/c1609-retirement-real-cases.mjs T/VolumeRetirementDockerTests.cs T/WindowsUnitPlatformAdmissionTests.cs T/WindowsUnitSkipRoster.cs tests/Antiphon.Tests/slow-tests-allowlist.txt |
| 62 | `68952c1b6` | test: resolve native host Git and preserve per-phase provenance | F/c1609-retirement-real-cases.mjs |
| 63 | `af290b6ce` | test: anchor native harness at the final shell entrypoint | F/c1609-retirement-real-cases.mjs |
| 64 | `bdab75b8a` | test: retain the owned daemon UTS identity in native host children | F/c1609-retirement-real-cases.mjs |
| 65 | `3e6ccdbf2` | fix: isolate temp retirement helpers in the production read-only namespace | docs/docker-stack.md S/c590-remote.sh F/c1008-fake-docker.sh T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementContentTests.cs |
| 66 | `26023d679` | fix: consume host publication proof before any offline temp lookup | S/c590-remote.sh |
| 67 | `dffb9aa73` | test: supply real local-origin graphs and strengthen native namespace controls | F/c1008-fake-docker.sh F/c1609-retirement-real-cases.mjs |
| 68 | `c68c57db0` | test: join native audit clients and isolate cancellation leftovers | F/c1609-retirement-real-cases.mjs |
| 69 | `e1e5fc77b` | test: bind native incarnation and durable-intent controls to disposable children | F/c1609-retirement-real-cases.mjs |
| 70 | `0a8b64a13` | test: bind native lock witness to blocked child and exact file descriptor | F/c1609-retirement-real-cases.mjs |
| 71 | `424d418b0` | test: preserve shell PID expansions in native entrypoint injection | F/c1609-retirement-real-cases.mjs |
| 72 | `49efbcb8f` | test: record actual writer process for native kernel lock witness | F/c1609-retirement-real-cases.mjs |
| 73 | `bd7db0223` | test: budget measured native retirement and exclude target origins from fixture proof | F/c1008-fake-docker.sh T/VolumeRetirementDockerTests.cs |
| 74 | `cea2c0b57` | fix: release exact owned temp audit helper when CID publication is interrupted | S/c590-remote.sh F/c1609-retirement-real-cases.mjs |
| 75 | `ffa21dde8` | test: require offline host ancestry and valid manual authority before dirty refusal | T/VolumeRetirementContentTests.cs T/VolumeRetirementReceiptTests.cs |
| 76 | `11d67e9ff` | test: force the missing-CID release path without Docker signal proxy | F/c1609-retirement-real-cases.mjs |
| 77 | `1c31d4e2f` | docs: record CARD-1609 continuation evidence and outstanding acceptance | .antiphon/task-cadd1086.md |
| 78 | `3ba9dec58` | test: strengthen manual retirement dirty preview and apply controls | T/VolumeRetirementReceiptTests.cs |
| 79 | `38867c76e` | fix: consume temp host bundle before any helper fetch | S/c590-remote.sh T/VolumeRetirementContentTests.cs |
| 80 | `1b71ab92d` | test: require audited Close through actual host census and journal transition | F/c1609-close-cases.mjs T/VolumeRetirementReceiptTests.cs |
| 81 | `7ffb0e11a` | fix: close retirement journals under shared locks with strict absence and child proof | S/c1008-retirement.sh S/c590-remote.sh F/c1609-close-cases.mjs S/retire-runner-volumes.ps1 T/VolumeRetirementReceiptTests.cs T/VolumeRetirementWrapperTests.cs |
| 82 | `eaffae9ce` | docs: state audited Close custody and incomplete manual entry boundaries | docs/docker-stack.md |
| 83 | `7bc9174f6` | test: preserve typed refusal exits and isolate census from health telemetry | F/c1609-close-cases.mjs T/VolumeRetirementWrapperTests.cs |
| 84 | `e434630d3` | fix: compare stable container safety fields and reject malformed mounts | S/c1008-retirement.sh F/c1609-close-cases.mjs |
| 85 | `0dac818e6` | test: require full wrapper Preview before automatic Apply | F/c1609-wrapper-cases.mjs F/c727-fake-verify.ps1 T/VolumeRetirementWrapperTests.cs |
| 86 | `1def748dc` | fix: run full content Preview before normal temp retirement Apply | S/deploy-server2.ps1 T/RetiredTempContainerScriptTests.cs |
| 87 | `7f2b22479` | docs: record Close measurements and bound expanded CP-3 collection | DI/2026-10-30-card-1609-s5-checkpoint-debug.md PLAN/2026-10-29-card-1609-content-derived-volume-retirement-plan.md |
| 88 | `4944b9d58` | fix: preserve audit subshell refusal and finalize only the owning phase | S/c590-remote.sh T/VolumeRetirementReceiptTests.cs |
| 89 | `348624209` | test: exercise deploy clearance with complete protected journal records | T/VolumeRetirementWrapperTests.cs |
| 90 | `0bc71df36` | fix: record confirmed helper joins before abort phase finalization | S/c590-remote.sh |
| 91 | `ac7504a0a` | docs: hand off CARD-1609 partial qualification and unverified abort repair | .antiphon/task-cdf3cde4.md |
| 92 | `4fbb517d1` | docs: verify helper abort repair against pre-repair assertion red | .antiphon/task-8b5c63b3.md |
| 93 | `30046456a` | test: require public manual templates and attested retirement acceptance | F/c1609-manual-cases.mjs T/VolumeRetirementReceiptTests.cs |
| 94 | `d9dbe0d7c` | feat: route attested manual retirement through protected host authority and shared gate | S/c1008-retirement.sh S/c1609-manual.ps1 S/c590-real.ps1 F/c1609-manual-cases.mjs S/retire-runner-volumes.ps1 T/VolumeRetirementReceiptTests.cs T/VolumeRetirementWrapperTests.cs |
| 95 | `e2ae3500b` | test: confine encoded manual bridge staging to owned transport root | F/c1609-manual-cases.mjs |
| 96 | `fa3fb4324` | test: use explicit confined transport adapter for public manual acceptance | F/c1609-manual-cases.mjs F/c1609-manual-transport.cjs |
| 97 | `7e09f1d4b` | test: inject manual transport boundary at every host fixture trap | F/c1609-manual-transport.cjs |
| 98 | `8a3fb0611` | fix: bound manual evidence transport and verify its returned terminal record | S/c1609-manual.ps1 |
| 99 | `74a970a6b` | docs: record public manual acceptance and remaining long-scan gate | .antiphon/task-8b5c63b3.md docs/docker-stack.md |
| 100 | `fafe9c8cc` | test: require explicit prior proof consumption and versioned host proof bindings | T/VolumeRetirementHostProofTests.cs T/VolumeRetirementWrapperTests.cs |
| 101 | `9e2db7735` | test: require locked reobservation before retirement clear and correct assertion overloads | F/c727-fake-verify.ps1 T/VolumeRetirementHostProofTests.cs T/VolumeRetirementWrapperTests.cs |
| 102 | `85776c1c8` | fix: bind v2 host graphs and prior proof consumption and recheck retirement under the clear lock | S/c1008-content-audit.sh S/c1008-host-proof.ps1 S/c1008-retirement.sh S/c1609-host-proof.ps1 S/c1609-read-packet.ps1 S/c590-real.ps1 S/c590-remote.sh S/deploy-server2.ps1 F/c1008-fake-docker.sh F/c1609-retirement-real-cases.mjs F/c727-fake-verify.ps1 T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementContentTests.cs T/VolumeRetirementFixture.cs T/VolumeRetirementHostProofTests.cs T/VolumeRetirementWrapperTests.cs |
| 103 | `8f480df1a` | test: bind unpublished H7 control to the actual v2 bridge graph and record J8 acceptance | .antiphon/task-8b5c63b3.md T/VolumeRetirementContentTests.cs |
| 104 | `f5b9e0c4b` | docs: preserve H7 red and completed acceptance receipts with exact remaining qualification | .antiphon/task-8b5c63b3.md |
| 105 | `dffe931e3` | docs: record H7 baseline red and observed missing awk in helper fixture | .antiphon/task-7dd4e872.md |
| 106 | `98cde6eab` | fix: preserve executable alternatives in the retirement helper fixture | F/c1008-fake-docker.sh |
| 107 | `5d8115e00` | docs: record complete H7 green and real land graph approval | .antiphon/task-7dd4e872.md |
| 108 | `5c3edddbc` | docs: preserve full CP-3 green and measured qualification costs | .antiphon/task-7dd4e872.md |
| 109 | `2ca17b600` | docs: record CP-4 green and CP-15 missing verifier dependency | .antiphon/task-7dd4e872.md |
| 110 | `d90ea4c26` | docs: confirm verifier fixture failure at the assigned base | .antiphon/task-7dd4e872.md |
| 111 | `0094b7867` | test: copy the v2 host proof dependency into the verifier fixture | T/RetiredTempContainerScriptTests.cs |
| 112 | `8e1dea3e6` | docs: close H7 debug with verifier repair evidence and exact remaining gates | .antiphon/task-7dd4e872.md |
| 113 | `445a22abe` | test: require Apply admission before long content audits | T/VolumeRetirementReceiptTests.cs |
| 114 | `f4118d17d` | docs: retain intended long Apply admission red and measured costs | .antiphon/task-e9fd8902.md |
| 115 | `a8c568369` | fix: bind Ready admission before the long Apply audit | S/c1008-retirement.sh S/c590-remote.sh T/VolumeRetirementReceiptTests.cs |
| 116 | `10d7c1153` | docs: preserve four acceptance passes and latched owner guard failure | .antiphon/task-e9fd8902.md |
| 117 | `29da3530e` | test: require locked v2 wrapper proof renewal and one audit without container effects | F/c1609-manual-transport.cjs F/c1609-wrapper-v2-cases.mjs T/VolumeRetirementReceiptTests.cs T/VolumeRetirementWrapperTests.cs |
| 118 | `e70b39e1a` | test: preserve real cleanup context in the wrapper transport fixture | .antiphon/task-e9fd8902.md F/c1609-wrapper-v2-cases.mjs |
| 119 | `88ab37c77` | fix: avoid a duplicate audit without container effects and strengthen long manual Preview acceptance | .antiphon/task-e9fd8902.md S/c590-remote.sh F/c1609-manual-transport.cjs F/c1609-wrapper-v2-cases.mjs T/VolumeRetirementReceiptTests.cs |
| 120 | `0c22785c4` | test: require exact audited object coverage from renewed origin proofs | .antiphon/task-e9fd8902.md T/VolumeRetirementHostProofTests.cs |
| 121 | `63173fe1a` | docs: retain required-object producer assertion red | .antiphon/task-e9fd8902.md |
| 122 | `7650549b0` | feat: renew exact publication proofs through the locked retirement coordinator | .antiphon/task-e9fd8902.md S/c1008-host-proof.ps1 S/c1008-retirement.sh S/c1609-host-proof.ps1 S/c590-real.ps1 S/c590-remote.sh F/c1609-manual-transport.cjs |
| 123 | `902a44465` | fix: close the coordinator proof-export expression and retain mixed acceptance receipts | .antiphon/task-e9fd8902.md S/c590-real.ps1 |
| 124 | `bf6064a78` | fix: retain numeric coordinator failure location without child output | .antiphon/task-e9fd8902.md S/c590-real.ps1 |
| 125 | `4a382faf6` | test: inject fixture state once at the host entry rather than helper traps | F/c1609-manual-transport.cjs |
| 126 | `56340382c` | fix: suppress async completion objects from retirement exit-code returns | S/c1609-host-proof.ps1 S/c590-real.ps1 |
| 127 | `5efa22875` | docs: retain continuation 11 red and green witnesses and exact incomplete qualification handoff | .antiphon/task-e9fd8902.md |
| 128 | `20429c7d4` | docs: close the report with its code handoff and final guards | .antiphon/task-e9fd8902.md |
| 129 | `efe50dc1e` | docs: fix continuation 12 remaining-work ledger before qualification | .antiphon/task-587775c4.md |
| 130 | `7a6971368` | docs: preserve fresh acceptance red and observed fixture causes | .antiphon/task-587775c4.md |
| 131 | `f9178f871` | test: transfer the unrelated graph before withdrawing origin coverage | F/c1609-wrapper-v2-cases.mjs |
| 132 | `e1b9ed55e` | test: bound manual acceptance phases from complete measured execution | .antiphon/task-587775c4.md F/c1609-manual-cases.mjs T/VolumeRetirementReceiptTests.cs |
| 133 | `cd5248862` | test: measure and bound complete wrapper retirement acceptance | .antiphon/task-587775c4.md F/c1609-wrapper-v2-cases.mjs T/VolumeRetirementWrapperTests.cs |
| 134 | `70876d0da` | docs: qualify fresh wrapper and manual acceptance at the repaired source | .antiphon/task-587775c4.md |
| 135 | `cf3c03593` | docs: qualify full CP-15 and bind the closed Linux roster run | .antiphon/task-587775c4.md |
| 136 | `ecfa7533d` | Record closed roster red receipts and exact baseline attribution scope | .antiphon/task-587775c4.md |
| 137 | `218699652` | Document exact roster repair ledger and native Windows handoff | .antiphon/task-587775c4.md |
| 138 | `b1d44121d` | Preserve bounded baseline timeout and exact remaining safety work | .antiphon/task-587775c4.md |
| 139 | `625e4e92a` | Clarify incomplete qualification and close continuation report | .antiphon/task-587775c4.md |
| 140 | `5ee0aea6d` | Record full publication identifiers and final cleanup inventory | .antiphon/task-587775c4.md |
| 141 | `6d9bdc906` | docs: triage CARD-1609 roster failures and minimal safe rollout scope | DI/2026-10-31-card-1609-s5-triage.md |
| 142 | `af9b3400d` | test: give CP-16 deploy harnesses the real retirement loader | T/RemoteScriptContractTests.cs |
| 143 | `c413a011a` | test: expect retire-temp Preview then Apply under one operation | PLAN/2026-10-29-card-1609-content-derived-volume-retirement-plan.md T/RollingVolumeRecycleScriptTests.cs |
| 144 | `45db22473` | test: require readonly and volume-nocopy on every audit volume mount | T/RetiredTempContainerHostTests.cs |
| 145 | `c4e76c8d3` | test: admit temp origins only with a bound v2 host proof | T/RollingVolumeRecycleScriptTests.cs |
| 146 | `f272b2c35` | test: refuse volume retirement until the operation apply gate matches | PLAN/2026-10-29-card-1609-content-derived-volume-retirement-plan.md T/VolumeRetirementApplyGateTests.cs |
| 147 | `509f20891` | fix: keep volume retirement apply off unless the operation token matches | docs/docker-stack.md S/c590-real.ps1 S/c590-remote.sh tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs T/RollingVolumeRecycleScriptTests.cs |
| 148 | `b87dba800` | test: drop the abandoned child before private-origin admission | T/RollingVolumeRecycleScriptTests.cs |
| 149 | `ba2f264bd` | test: keep the resume cut and register the helper origin | T/RollingVolumeRecycleScriptTests.cs T/VolumeRetirementApplyGateTests.cs |
| 150 | `aa331d013` | test: drop the public stand-in proof before Ready preview | T/RollingVolumeRecycleScriptTests.cs |
| 151 | `33ab2cffb` | report: record the CARD-1609 apply-gate checkpoint ledger | .antiphon/task-1fa8a516.md |
