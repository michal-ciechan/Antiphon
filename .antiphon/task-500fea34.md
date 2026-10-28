# CARD-1594 Review round 2: review-evidence walk (task 500fea34)

Subject: task 83044d56-549c-4f38-b076-91c32ea86385 (continues 74294297-124d-4076-8dd8-ccd9e0c35bc4), branch feat/card-task-83044d56.
Reviewed tip: 9ad764fd342ae3a5c0b72673a72ab6cbe8f6afec. Red witness commit: f00ce52b20349ae9f3b1c603b8869b16f20c4464. Base: rc/2026-10-27 68565db92359eadea14180c745c0a28711a36872.
Host: server2 runner mirror (Linux). Read-only on the subject. Fake-Docker fixtures only. No retire-temp apply. No real volume touched.

Scope: **Interim**. Only the narrow witnesses ran. I did not run the whole Unit lane, the full RollingVolumeRecycleScriptTests class (about 30 min) or RemoteScriptContractTests.

No author report file is committed on the subject branch (`.antiphon/task-83044d56.md` is absent). The author's report was read from `delegate.ps1 -Status 83044d56`.

## Verdict

There is **no fail-open and no regression of the earlier-reviewed behaviours**. Round 1's D-1 (census) and D-2 (review-evidence hides a repository) are both fixed and witnessed.

**One false-refusal defect (D-1):** the walk's name list refuses ordinary evidence names under `review-evidence/` that base accepted. Examples are any dot-name such as `.antiphon/`, `.gitignore` or `.playwright/`, and `config`, `index`, `hooks`, `info`, `description`, `modules`, `antiphon`, `*.lock` and `*~`. The refusal line does not say which entry triggered it.

## Runs

One gated build at the tip (UseAppHost=false, OutputPath bin-c1594r2/, slot held 226 s), then `dotnet exec --no-build` through `scripts/build-slot.ps1`. Fixture probes ran through `node` under build-slot.

| Run | SHA | Result |
|---|---|---|
| WindowsUnitPlatformAdmissionTests + C1514LandingSplitBudgetTests + RcRosterRepairTests + DockerStackDocumentationTests | 9ad764fd | 28 total: 27 passed, 0 failed, 1 skipped (C1210_Windows_linux_only_outcomes_are_exact, Windows-only). Per class: Admission 4 passed and 1 not executed, so **C1210_Roster_closure_matches_source_gates passed** (round 1 D-1 fixed); C1514 5 passed (C1404, C1577, C1584, C1410, G2865); RcRoster 3; DockerStackDocumentation 15. Slot held 78 s |
| RollingVolumeRecycleScriptTests/C1594_Antiphon_evidence_gitdir | 9ad764fd | 27/27 passed. Slot held 26 s |
| fixture variants from f00ce52b with the f00ce52b script (red check) | f00ce52b | All 12 review-evidence refusal cases are **red**: the audit exits 0 (`actual: 0`) for repo, bare, gitfile, symlink, head, refs, objects, packed-refs, odd, unreadable, hardlink and dot. Pass controls are green: review-evidence-reports, antiphon-symlink, and all 13 antiphon-evidence cases |
| older hidden-location rows (tip fixture and script) | 9ad764fd | review-evidence (P), review-evidence-linked (K), antiphon-state, editor-swap: all green |
| reviewer probes (scratch copy of the tip fixture and script) | 9ad764fd | see below |
| check-evidence-diff 68565db9..9ad764fd | - | commits=4 entries=0 violations=0, exit 0 |

The census class ran green on the tip.

## Priority answers

### 1. Fail-open

The new walk catches every Git-openable shape at any depth. Verified:
- a real non-bare repository with an unpublished commit (round 1's fixture, witnessed);
- a bare repository with an unpublished commit;
- a `.git` pointer file;
- a repository 11 directories deep (probe);
- a reftable repository (probe);
- a lone `commondir` pointer (probe);
- a name containing a newline (probe).

Opaque bytes still pass. My probe `pr-bundle` put a `git bundle` and a tar of a repository with an unpublished commit under `review-evidence/x/`, and the audit passed. A worktree checkout copied without `.git` (plain source files) cannot be detected by name either.

Both cases fall in the class the docs already accept: "file content is never read, so a patch or notes there are deleted with the volume". I find that acceptable. The docs could name bundles and archives explicitly (A-3).

### 2. False refusal (D-1)

The walk applies `antiphon_evidence_git_name` (`scripts/c590-remote.sh:6818-6837`) to every name at every depth. The tip's own witnesses `antiphon-evidence-dot` and `review-evidence-dot` pin that `.hidden` refuses.

Probe results under `review-evidence/x/` (a P variant that refuses exits 2, `gitdir-entry`). Every one of these **refuses**, and base passed all of them:
- `.antiphon/task-x.md`
- `.gitignore`
- `.trx/a.trx`
- `report.md~`
- `yarn.lock`
- `client/src/hooks/useX.ts`
- a file named `config`
- a file named `index`
- `info/readme.md`
- `antiphon/report.md`
- `description`
- `modules/a.md`

These shapes **pass**:
- `TestResults/run 1/--a b.trx`
- `-rf/--help`
- `logs/build.log`
- `*.trx`, `*.html`, `*.jsonl` and `*.md` files

Plausible real sources of the refusing names:
- a copied checkpoint `.antiphon/` output or E2E `.playwright/`;
- a scratch source tree copied with `.gitignore` and `client/src/hooks`;
- an editor backup.

The refusal prints `audit check=gitdir-entry status=0 repo=repos/antiphon admin=repos/antiphon/.git`. That is byte-identical to the d0b8ac9c refusal that already blocked the rollout, and it does not say which entry or tree caused it.

**Recommendation: narrow the name rule to names that make a directory Git-openable.** Git opens a directory only when it has `HEAD` plus `objects` and `refs`, or `HEAD` plus `commondir`. A work tree is found through `.git`. Every `modules/<n>` or `worktrees/<id>` that Git would open contains `HEAD`, so it still refuses.

Exact refuse list:
- `.git` (any type);
- `HEAD`, `ORIG_HEAD`, `FETCH_HEAD`, `MERGE_HEAD`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`, `REBASE_HEAD`, `BISECT_HEAD`, `AUTO_MERGE`, `MERGE_AUTOSTASH`;
- `commondir`, `gitdir`, `objects`, `refs`, `packed-refs`, `reftable`, `shallow`;
- any name containing a control character.

Keep the structural rules: a symlink, another file type, a regular file with nlink>1, and an unreadable entry still refuse.

Stop refusing:
- other dot-names;
- `config`, `config.worktree`, `index`, `hooks`, `info`, `description`, `branches`, `remotes`, `modules`, `worktrees`, `locked`;
- `gc.log`, `gc.pid`, `*.lock`, `*~`, `BISECT_*`/`NOTES_MERGE_*` other than BISECT_HEAD;
- the `*_EDITMSG`/`*_MSG`/`MERGE_MODE`/`MERGE_RR`/`*_DESCRIPTION` names;
- `rebase-merge`, `rebase-apply`, `sequencer`, `rr-cache`, `sharedindex.*`, `fsmonitor--daemon*`;
- `common`, `antiphon`, `review-evidence`, `antiphon-evidence`, `antiphon-mirror-owner.json`.

None of these makes a directory openable by Git. Their contents are never read, so they are the same class as the notes already accepted.

### 3. The walk and the unchanged behaviours

Name and tree handling:
- `find -P … -print0` with `read -d ''` handles any name.
- Spaces and a leading dash pass. A newline refuses.
- `stat -c %h -- "$child"` is safe.
- An unreadable entry refuses (witnessed).
- The audit's 10800 s budget ends in 124/137, which is RecycleGitAuditUnknown, so a huge tree refuses rather than passes (round 1).
- Hard links: regular files need nlink 1. Directories cannot be hard-linked on Linux.
- Mounts: the helper sees no nested mounts.
- `$scratch/antiphon-evidence` is reused by both trees, but `consider_gitdir` runs sequentially per common directory. Only content checks run in parallel, so there is no race.

The `c590-remote.sh` diff against 68565db9 has exactly three hunks:
1. `@@ -6675`: the closed-list comment.
2. `@@ -6815,+58`: the new `antiphon_evidence_git_name` and `consider_antiphon_evidence`.
3. `@@ -6901`: in `consider_gitdir`, the `review-evidence)` arm becomes `review-evidence|antiphon-evidence)`. It keeps the common-level check and calls the walk.

`c1008_audit` (50 lines) and `c1008_audit_readonly` (103 lines) are byte-identical to base. The `docker run`/`docker create` lines are identical. So the cross-SHA image binding, the empty-image refusal on the audit lookup only, ignored receipts and the same-SHA lookup are untouched.

### 4. Roster

The roster now has four Existing rows: C1594_Antiphon_evidence_gitdir, C1594_Cross_sha_cleanup_image_binds_the_retired_temp, C1594_Older_retirement_receipt_is_ignored and C1594_Disagreeing_cross_sha_images_refuse. The closure passes.

Results is 1 for a method with 27 `[Arguments]` cases. That is harmless because Existing rows are checked only against source closure (A-4).

## Defects

**D-1 (false refusal introduced by 9ad764fd; landing owner 83044d56)**
- **Where:** `scripts/c590-remote.sh:6818-6837` (`antiphon_evidence_git_name`), applied to `review-evidence/` at `:6901-6904`. The pins are the `review-evidence-dot`/`antiphon-evidence-dot` fixture rows (`scripts/fixtures/c1105-git-audit.cjs`) and the docs sentence "a dot-name (including a nested `.git`) … `config`, `hooks`, `worktrees`, `index` … lock files".
- **Failure:** base passed plain `review-evidence/` files named as in the probe list above. At the tip each one refuses `RecycleGitAuditUnknown check=gitdir-entry admin=<repo>/.git`, and the line does not identify the entry.
- **Why:** this gate authorises deleting the real temp volumes. A false refusal there has already blocked the rollout once (d0b8ac9c). The temp mirror's `review-evidence/` contents are unknown, and an undiagnosable refusal costs another rollout attempt.
- **Fix:**
  1. Narrow the name list to the exact refuse list above, keeping the structural rules.
  2. Turn `*-dot` into a pass case such as `.antiphon/x.md` or `.gitignore`.
  3. Keep refusal witnesses for a `.git` directory and a `.git` file, and add `commondir`.
  4. Add pass witnesses for `config`, `index`, `hooks/` and `*.lock`.
  5. Update the docs sentence and its DockerStackDocumentationTests pin.
  6. Recommended: add the offending tree-relative entry, redacted, to the refusal line (for example `entry=review-evidence/x/.antiphon`). The `commonAdmin` regex pins the line format, so adjust it if you do.

  If the caller instead accepts the broad rule, the operator should first run a read-only name listing of `repos/antiphon/.git/{review-evidence,antiphon-evidence}` on antiphon-runner-temp_work (dot-names, plus the list above) before the preview.

## Advisories (no Review rerun needed)

- **A-1:** `find -P … || refuse_gitdir_entry` still has no witness (round 1 A-1).
- **A-2:** a per-file `stat` fork costs about 2.5 ms per entry. `find -printf '%y %n %p\0'` would remove it (round 1 A-2).
- **A-3:** the docs could add "a bundle, archive or copied source tree there is deleted with the volume".
- **A-4:** Results 1 for the 27-case method. Harmless for Existing rows.
- **A-5:** the author's report is not committed on the subject branch.
- **A-6 (operational, outside scope):** this runner mirror's common directory registers 10 detached worktrees under `/tmp/claude-1654/...scratchpad` from other Review sessions. A future recycle of this runner's volume would refuse on them (`worktree-confine` or the runner-tmp rules). Mine were removed.

Cleanup: deleted only my own `bin-c1594r2` outputs (33) and my two scratch worktrees (tip, red). Nothing of the subject was touched.
