# CARD-1594 Review round 3: evidence-name rule (task 1e0bce38)

Subject: task 2cb04e45-319e-466c-a65b-3d60a59d6b1a, branch feat/card-task-2cb04e45.
Reviewed tip: 48e603db00b4d1726c91f02adbba57264001e494. The round 3 commits are 2af0f7238 (PASS witnesses) and 48e603db0 (narrowed rule). The round 2 tip was 9ad764fd3.
Host: server2 runner mirror (Linux). This review was read-only on the subject. It used fake-Docker fixtures only and touched no real volume or container.

Scope: **Interim**. The whole Unit lane was not run. RemoteScriptContractTests was not run as a class. Its four hidden-location rows that use the evidence trees were run directly through the fixture (below).

No author report is committed on the subject branch. The author's report was read from the task row.

## Verdict

No fail-open and no regression. Round 2 D-1 (false refusal of ordinary evidence names) is fixed and witnessed. No new defects. Advisories only.

## Runs

There was one gated build at the tip: `dotnet build tests/Antiphon.Tests` with `UseAppHost=false` and `OutputPath=bin-c1594rv/`. It had 0 errors and the slot was held 243 s. Every test run used `dotnet exec --no-build` through `scripts/build-slot.ps1`.

| Run | SHA | Result |
|---|---|---|
| RollingVolumeRecycleScriptTests (whole class, own slot) | 48e603db | 75 total, 75 passed, 0 failed, 0 skipped. 28m59s, slot held 1741 s. C1594_Antiphon_evidence_gitdir ran 40 cases, which matches its 40 `[Arguments]` |
| RetiredTempContainerHost 22, RetiredTempContainerScript 16, DockerStackDocumentation 15, RollingProductionMount 12, CheckpointImport 26, WindowsUnitPlatformAdmission 4+1 not executed, RcRosterRepair 3, C1514LandingSplitBudget 5 | 48e603db | 104 total, 103 passed, 0 failed, 1 skipped. The skip is C1210_Windows_linux_only_outcomes_are_exact, which is Windows-only. **C1210_Roster_closure_matches_source_gates passed.** C1404, C1577, C1584, C1410 and G2865 passed. 16m16s, slot held 977 s |
| Hidden-location fixture rows `review-evidence` (P), `review-evidence-linked` (K), `antiphon-state`, `editor-swap` | 48e603db | all PASS |
| Reviewer probes and red checks (scratch copies of the fixture and script) | 48e603db / 9ad764fd | see below |
| check-evidence-diff 68565db92..48e603db | - | commits=6 entries=0 violations=0, exit 0 |

The two test runs overlapped in separate slots.

## 1. Fail-open

Git opens a directory only when it holds a valid `HEAD` (`is_git_directory`). I checked this on the host with git 2.47.3. A bare repository opens. Copies with `Head` instead of `HEAD`, `OBJECTS` instead of `objects`, no HEAD, or config only do not open. A checkout whose Git directory is named `.git ` (trailing space) is not found. `HEAD` refuses at any depth and as any type, so no openable directory can pass. The volume is Linux and case-sensitive, so case-folded names are not Git state there.

Probe rows added to a scratch copy of the fixture, run against the tip script. All behaved as expected:

| Probe | Expected | Result |
|---|---|---|
| real repo with a commit under `review-evidence/x/antiphon/r` | refuse at `.../antiphon/r/.git` | refused |
| bare repo under `review-evidence/hooks/b` | refuse at `.../hooks/b/HEAD` | refused |
| bare repo under `review-evidence/x/modules/sub` (submodule git dir shape) | refuse at `.../modules/sub/HEAD` | refused |
| worktree admin `worktrees/w/{HEAD,commondir,gitdir}` | refuse at `HEAD` | refused |
| bare repo under `antiphon-evidence/` | refuse at `HEAD` | refused |
| `.git` as an empty directory | refuse | refused |
| `HEAD` as a directory | refuse | refused |
| name containing DEL (0x7f) | refuse with `entry=?` | refused |
| two refusing names in different directories | the byte-first one is named | named, so the order is stable |
| case variants (`Head`, `OBJECTS/`, `REFS/`, `.GIT`, `.git `, `.git.`, `HEAD.md`) | pass, because Git does not open them | pass |
| empty `review-evidence/` and `antiphon-evidence/` | pass | pass |
| 17 ordinary names under `antiphon-evidence/` (config, index, hooks/, info/, description, modules/, `*.lock`, `*~`, .gitignore, logs/HEAD.log, ORIG_HEAD.md, branches/, worktrees/, COMMIT_EDITMSG, sharedindex.1, antiphon/, common/) | pass | pass |
| credential-shaped directory `my-token-dir/HEAD` | refuse with `entry=review-evidence/REDACTED/HEAD` | refused |

The structural rules are unchanged: a symlink, any other file type, nlink>1 on a regular file, and an unreadable entry still refuse. The `find -P` and `sort -z` failure paths refuse. The helper exports `LC_ALL=C`, so `sort -z` is byte order. `sort -z` was already used by `consider_gitdir` in the same helper.

## 2. Witnesses are real (reviewer red checks; formal PCs stay pending)

**Re-broadened rule.** I ran the 9ad764fd script against the tip fixture. All 11 new PASS witnesses go **red**: antiphon-evidence-dot, review-evidence-dot, config, index, hooks, info, description, modules, lock, antiphon and backup. So does my 17-name probe.

**Narrowed too far.** Each mutant removes one rule from a scratch copy of the tip script. Every matching witness goes **red**:

| Mutant | Witnesses red | Note |
|---|---|---|
| `.git` | review-evidence-repo, gitfile, deep, antiphon-evidence-git, rv-dotgit-dir | gitfile and dotgit-dir go red by exit 0. Repo and deep still refuse on the inner `HEAD` and go red on the `entry=` pin |
| control char | newline, rv-del | |
| `HEAD` | review-evidence-head, antiphon-evidence-head, rv-head-dir (exit 0); review-evidence-bare, rv-hooks-bare | bare and hooks-bare still refuse on `objects` and go red on the `entry=` pin |
| `objects`, `refs`, `packed-refs` | both trees each | |
| `reftable` | review-evidence-reftable | |
| `commondir` | review-evidence-commondir | |
| symlink rule | both trees | |
| hard-link rule | both trees | |

## 3. Refusal line change

The line is now `audit check=gitdir-entry status=0 repo=<repo> admin=<admin> entry=<rel>`. The `check=`, `status=`, `admin=` and `RecycleGitAuditUnknown` tokens are unchanged.

What consumes it:
- `c1008_audit_checked` takes the first `audit check=` line whole (`sed -n '/^audit check=/{p;q;}'`) into the receipt (`jq --arg`) and into `DIAGNOSIS=RecycleGitAuditUnknown <detail>`.
- The PowerShell wrappers (`deploy-server2.ps1`, `c590-real.ps1`, `c590-command.ps1`) do not parse the Recycle detail. `c590-real.ps1:119` matches only Cache, Fixture, Npm and Nuget tokens.
- The only regexes on the line are in the fixture: `linkedAdmin` and `commonAdmin` are still used for the non-walk refusals, and the new `evidenceEntry` covers the walk.

Spaces or `entry=` inside a name cannot break a parser (probe rv-space). `c1008_audit` and `c1008_audit_readonly` hash byte-identical to 68565db92. The `docker run` and `docker create` lines are identical.

## 4. Docs and code

The docs sentence in `docs/docker-stack.md` lists exactly the names in `antiphon_evidence_git_name`. It states the allowed examples and the `entry=` suffix with redaction, and DockerStackDocumentationTests pins it (15/15). The code comment at `scripts/c590-remote.sh:6675` matches.

## Landability

The brief names base `34a7d0b8f5e1ff2aaab6e1fa178258c4f29c8ba8`. That commit is **not** an ancestor of the tip. The branch's real base is rc `68565db92359eadea14180c745c0a28711a36872`, which is an ancestor of 34a7d0b8.

- `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` against rc tip 070d35b80653d40d697e462e419414610be85d66 is **clean**.
- The whole-branch merge-tree against `origin/master` conflicts, as expected for an rc-based branch.
- The 6 commits 68565db92..48e603db0 (0b60c198b through 48e603db0) **cherry-pick clean onto master** in a chained `merge-tree --merge-base` simulation. The earlier CARD-1594 commits are already on master (86d21b50b, 268ca37c1, 17fbf57ed).

## Defects

None.

## Advisories (no Review rerun needed)

- **A-1:** `[[:cntrl:]]` under `LC_ALL=C` does not match UTF-8 C1 controls (for example U+009B CSI, bytes C2 9B) or other non-ASCII bytes. The probe rv-c1 shows `entry=review-evidence/x/<U+009B>31m/HEAD` printed raw. The same text reaches the receipt (jq maps invalid UTF-8 to U+FFFD) and the DIAGNOSIS line and `c590-result.json` (`json_escape` keeps raw bytes). This fails closed, because the refusal stands. Suggestion: print `?` for any byte outside printable ASCII in `evidence_entry_rel` (`scripts/c590-remote.sh:6836`).
- **A-2:** ORIG_HEAD, FETCH_HEAD, MERGE_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD, REBASE_HEAD, BISECT_HEAD, AUTO_MERGE, MERGE_AUTOSTASH, gitdir and shallow have no per-name witness. None of them alone makes a directory openable, so dropping one would not fail open.
- **A-3:** The `.git` and `HEAD` rules overlap in the repo and bare witnesses (see section 2). The `entry=` pin is what makes those witnesses sensitive. The gitfile, `.git`-dir and lone-`HEAD` witnesses are the independent reds.
- **A-4:** The docs do not say that a control-character entry prints `entry=?`.
- **A-5:** The brief's base SHA 34a7d0b8 is not the branch base (see Landability).
- **A-6:** The author's report is not committed on the subject branch.
- **A-7:** Round 2 A-1 (no witness for `find -P … ||`) and A-2 (per-file `stat` fork) are still open.
- **A-8 (performance):** the RollingVolumeRecycleScriptTests class ran 28m59s, near the 30-minute Debug threshold in docs/test-performance-standards.md.

The change adds no session, queue, input-wait or transcript path, so the producer/recipient, UserPrompt and parking audits do not apply.

Cleanup: I deleted only my own `bin-c1594rv` outputs (33 directories). The scratch probes live under my scratchpad. Nothing of the subject was touched.
