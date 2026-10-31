# Desktop worktree bloat and worktree-add timeouts

Investigated 2026-10-31 for task `4dcb3ac6-2a85-4010-8e44-103b44942362`. **Confirmed: inactive retirement accumulates live directories, and branch-attached registry HEAD resolution imposes substantial Git time/memory cost. Unconfirmed: which resource made the specific add exceed 180 seconds.** Registry overhead and checkout cost are both measured below; the successful probe does not reproduce the timeout.

## Scope and provenance

Investigation source tip: `256b861468518f1f18489798b2f109f48d78e0c8`. Server `/api/version` and canonical checkout HEAD both reported `8edda999623a473be2d1970727c172bfb5088d59`. The cited cleanup/create implementations and settings were compared between those commits: identical, except an unrelated summary comment in `VerificationCleanupService`.

No pre-existing real-repository worktree, branch, lock, or registry entry was removed, pruned, or repaired. No production configuration or database writes were issued. The only real-repository registration created/removed was the expressly authorized probe at `C:\Users\lndco\AppData\Local\Temp\antiphon-wt-probe-1`; `git worktree remove` succeeded on that exact path. All three independent scratch clones and the owned unlink control were also removed; exact-root absence was verified. No qualification candidate or log directory under `C:\Antiphon\releases` was opened. No build or product test was run; these were read-only inventories and disposable Git measurements.

Generated evidence is external, uncommitted, under `C:\Users\lndco\AppData\Local\Temp\antiphon-worktree-audit`. Principal files: `audit.csv`, `audit-details.jsonl`, `audit-summary-final.json`, `task-api-audit.jsonl`, `task-db-final.csv`, `landing-db.csv`, `cleanup-db.txt`, `failure-events.csv`, `real-probe.json`, trace files, `fast-scaling.json`, `clone-scaling.json`, `symbolic-scaling.json`, `clone-cleanup-receipt.json`, and the measurement scripts. SHA-256: `audit.csv` = `bd3bfc1e28e6e11827bd9265c85a63538e7d816482ca3e01c98b3102408b8691`.

## Stored incident evidence

`AgentTasks.Id=7b8e4061-ce40-4a74-9681-1cd9791e4b7c` is Failed, completed `2026-10-31 06:15:10.296395+00`, with no `AgentSessionId` and no saved worktree coordinates. Both its task GET and PostgreSQL row contain:

> Dispatch failed before a session existed: git worktree add -b feat/card-task-7b8e4061 C:\Antiphon\worktrees\card-task-7b8e4061 0f4d90b816da073481cf7200d29987017c2f5702 timed out after 180s in C:\src\Antiphon

The matching failed event is `077ec7e6-da6c-4d79-84d2-d8bf55cb2095`, at that completion time; creation was 06:06:30 and a Held event at 06:07:42 says same-card base inspection was pending. Total queue-to-failure time is therefore not add duration. There is no session transcript for this launch: the stored task explicitly has no session. The task GET and event are the incident evidence, not an inferred missing transcript.

`WorktreeManager.CreateAsync` invokes the exact `worktree add -b` form at `server/Infrastructure/Git/WorktreeManager.cs:131`; `TimeoutFor` chooses the dedicated add budget at `:1048`; the configured value is 180 seconds (`server/appsettings.json:15`). `RunGitAsync` starts the budget before launching Git (`:1285`), and its timeout handler kills and awaits its child, then throws the recorded `TimeoutException` (`:1324`). A separate registration inspection also runs before creation (`:1082`), outside that individual add budget. No incident-level Git Trace2 or contemporaneous CPU/AV trace was available, so this report cannot allocate those failed 180 seconds to individual phases.

## Current inventory and safety screening

The initial 06:20 UTC snapshot had **1,025 registrations**, including **1,012 under `C:/Antiphon/worktrees`**. Of those, 1,008 have exact `card-task-<8hex>` names; the other four are two `c1613-*` checkouts, a `land/*` checkout, and `card-task-a7b2b64a-rc`. The brief's 1,023/1,010 count had already changed. A subsequent real listing had 1,026 total/1,013 under that root. This is a live system, not a frozen cleanup manifest.

| Verdict | Count | Logical GiB |
|---|---:|---:|
| SAFE-TO-REMOVE | 912 | 168.371 |
| KEEP-dirty | 3 | 0.384 |
| KEEP-unpushed | 85 | 20.452 |
| KEEP-active | 11 | 2.729 |
| KEEP-unknown | 6 | 4.807 |
| Total | 1017 | 196.743 |

Reclaimable screening-candidate contents: **168.371 GiB (180.787 decimal GB)**. Main HEAD/remote revalidation started at 2026-10-31T06:48:06.185399+00:00; five subsequently observed registrations were appended at 2026-10-31T06:51:33.725628+00:00 and excluded from the removal screen.

The CSV has precisely the requested columns and one row for each of the initial 1,012 registrations under that root plus five later registrations, including the four nonstandard checkouts. The final observed registry had 1,030 total / 1,017 under the audited root; final listing took 4.802 seconds. This is the final inventory cut, not an assertion that dispatch stopped afterward. `SAFE-TO-REMOVE` means the requested **screening predicates** passed: uniquely resolved terminal task, matching task coordinates, clean porcelain status, and a fresh origin branch tip equal to current local HEAD. Pending lands, live task/session consumers, relevant live reservations, and unknown identities are retained. This label is **not** the existing product's retirement authorization and does not authorize a shell deletion.

Measurement: `git status --porcelain=v2 --branch -z --untracked-files=all` with `GIT_OPTIONAL_LOCKS=0` supplies cleanliness and actual branch/HEAD; a later `git rev-parse --verify HEAD` verifies identity again, and a second `git ls-remote --heads origin` verifies fresh remote equality. Dirty-row counts were checked with porcelain v1 and rename-aware parsing. No fetch or stale tracking-ref assumption was used. Age is the root directory's last-write age, not the newest descendant's age. Size sums regular-file logical lengths in MiB; reparse points are not followed and uncertain rows stay kept. GiB totals are logical contents, not measured NTFS allocated clusters or a guaranteed physical free-space gain. Ignored files are included in size but excluded by Git's ordinary cleanliness check.

Both fleet task-list GET attempts timed out (60 and 120 seconds). Full GUIDs were then resolved from read-only PostgreSQL rows. **All 1,008 initially matched exact-name tasks had successful individual GETs, zero errors**, in 86.7 seconds with two concurrent reads: 899 Succeeded, 54 Canceled, 49 Failed, five Dispatched, one Blocked. Five added rows also received successful individual task GETs. The final read-only database refresh and active-consumer inventory are additional conservative checks; task API summaries are retained per GUID. The four nonstandard registrations have no proven unique task-directory ownership.

Important limits: the inventory is sequential, so its row observations have different timestamps. A later edit or branch move can invalidate a row immediately. Ordinary clean status does not prove ignored evidence, secrets, nested repositories, or a reviewed artifact can be discarded. The existing guard protects/classifies ignored files, preserves evidence, rechecks authority and identity, and refuses protected content or links (`server/Infrastructure/Git/GuardedWorktreeRemoval.cs:111`, `:128`, `:140`). Existing retirement also requires preserved reports/artifacts and resolved handoffs (`server/Application/Services/TaskWorktreeRetirementService.cs:112`, `:259`). Those admission checks were not executed because that would require a commissioned release/cleanup action.

## Cost measured on the real repository

The full detached probe checked out **9,162 files** at `8edda999623a473be2d1970727c172bfb5088d59`:

| Operation | Wall seconds | Evidence |
|---|---:|---|
| Worktree list, initial | 6.039 | Initial registry snapshot |
| Worktree list, traced | 7.809 | `list-trace.txt`; Git's own elapsed 7.077 |
| Worktree add, detached | 40.350 | `real-probe.json`, `real-add-trace.txt` |
| Updating files within add | 29.973 | Trace2 `Updating files` region |
| Before reset child starts | 9.063 | Trace2 `child_start` |
| Probe worktree remove | 27.974 | Exact owned literal path; exit 0 |
| Canonical status, all untracked | 1.450 | Two pre-existing untracked patch files; no edits |
| Subsequent worktree list | 7.812 | 1,026 entries |
| Prune dry-run | 0.872 | Only `worktrees/investigate-b11dd450` reported |

Trace2 puts the reset child at 30.828 seconds and the parent Git command at 39.892 seconds; parent peak working set was 1,456,840,704 bytes (about 1.36 GiB). Trace2 does not name an internal registry-enumeration region for `worktree list`; its whole-command time must not be presented as a measured filesystem-only phase. A separate Python read of HEAD/gitdir/commondir for 1,026 administrative directories took 10.005 seconds for 94,312 bytes; it used different file APIs and was not a subtraction-compatible phase measurement.

**No idle sample was obtained.** CPU samples were 100%; at 06:31:03 UTC the host had 4,715,976 KiB free of 33,494,280 KiB physical memory, disk traffic 60,937,211 bytes/s and sampled disk queue zero. Inventories/clone setup ran while the protected qualification continued; it was not paused or inspected. Subsequent controlled measurements share that busy host and include interference from the audit itself.

Repository storage: 9,879 loose objects/75.30 MiB; 165,836 packed objects in 55 packs/120.21 MiB; 509 prune-packable objects, zero garbage. `packed-refs` had 14,480 entries/1,592,081 bytes. The later all-ref snapshot used in the clone contained 15,018 refs. Initial ref categories included 4,503 `remotes/origin`, 4,177 `antiphon/land`, 3,513 `heads/feat`, and 2,719 `antiphon/progress`. Progress pins are created by `server/Infrastructure/Git/TaskProgressGit.cs:144` (observation) and `:226` (baseline). These counts are observations, not proof that any ref is disposable.

Defender reported AntivirusEnabled=true and RealTimeProtectionEnabled=true. Exclusions returned `N/A: Must be an administrator to view exclusions`. No exclusion or AV setting was changed; no AV event trace was captured. AV contribution and the scanning difference between `C:\Antiphon` and Temp remain unknown.

Locks observed and retained: zero-byte `C:\src\Antiphon\.git\refs\antiphon\progress\76fe1072b65f46ce898c1c927faffc4e\settlement-sync.lock`, last write `2026-10-14 08:02:08.7326504Z`; zero-byte index locks under `.git/worktrees/card-task-7c30c896` (Oct 22 01:39:11Z) and `card-task-a1f403eb` (Oct 22 15:30:42Z). No root `.git/*.lock` was listed in that sample. The successful add occurred with those locks present, so they are not a demonstrated global add blocker. No lock was deleted or tested by acquiring it.

## Independent-clone scaling

Detached-HEAD synthetic-registration control, median seconds (three trials per cell):

| Registrations before add | One-ref add | One-ref list | 15,018-ref add | 15,018-ref list |
|---:|---:|---:|---:|---:|
| 10 | 1.400 | 0.232 | 1.187 | 0.337 |
| 100 | 2.066 | 0.672 | 2.130 | 1.125 |
| 1000 | 3.871 | 3.479 | 2.083 | 2.134 |

The 100-registration, 15,018-ref **full checkout took 59.218 seconds** (one sample).

The first experiment used a local no-checkout clone, removed only that clone's refs, made an empty-tree commit, and repeatedly called real `git worktree add --detach` for tiny checkouts. Real additions reached 726 registrations before the explicit 1,000-second setup ceiling stopped the loop; the 1,000-registration real-add-loop sample was not run. At 10 registrations the three add samples were 0.612–0.886 seconds; at 100 they were 0.883–1.192 seconds. Its initial cleanup hit read-only object files; all disposable residue was subsequently removed using verified exact-root cleanup that preserved shared canonical object attributes (10,087 checked). The no-hardlinks clone also required private-file cleanup after a read-only-file refusal. A generic recursive PowerShell removal was rejected by automatic approval review; the narrower bounded private-file cleanup succeeded. No existing Antiphon workspace was touched.

The second clone used `--local --no-hardlinks --no-checkout`. To bound setup cost, it synthesized detached empty-checkout administrative records (HEAD, gitdir, commondir and reciprocal `.git` pointer) **inside that clone only**, then checked the exact registration count with Git. Every timed add/remove was a real Git command. It measured one-ref versus a copy of the real 15,018-ref inventory, with three samples per tiny add/list cell. Listing cells have one additional registration because they run before removing the measured add. The full-file sample at 100 registrations uses the real repository commit and 9,162-file checkout. This synthetic setup is a registry-cost control, not a reproduction of 1,000 historical full workspaces or an idle production benchmark.

A third, **branch-attached HEAD control** reproduced the missing mechanism. A fresh `--shared --no-checkout` clone read source objects through an alternate without copying or hardlinking them. It held the same real ref inventory and the same per-registration commit identities constant, changing only each administrative HEAD from a direct SHA to `ref: refs/heads/...`. Two trials per cell:

| Registrations | Detached list | Attached list | Detached tiny add | Attached tiny add |
|---:|---:|---:|---:|---:|
| 10 | 0.165 | 0.232 | 0.427 | 0.570 |
| 100 | 0.328 | 1.072 | 0.521 | 1.750 |
| 1000 | 2.751 | 12.747 | 2.506 | 13.151 |

At 1,000 registrations, attached list samples were 12.591/12.904 seconds and attached tiny-add samples 14.119/12.183 seconds. The first attached list peaked at **1,688,317,952 bytes (1.572 GiB)**, versus 19,345,408 bytes (18.45 MiB) for detached HEADs. At 100 attached registrations the first list peaked at 176,009,216 bytes (167.86 MiB). Evidence: `symbolic-scaling.json`, `scaling-symbolic.py`, and `symbolic-*-list-trace.txt`. This experimentally confirms a large branch-HEAD-resolution-dependent per-worktree time/memory cost in Git 2.50.1.windows.1; it does not identify an internal Git allocation site. It closely matches the real registry's roughly 1.36-GiB listing/add footprint.

Trimming attached registrations from 1,000 to 100 reduced this control's tiny-add median from 13.151 to 1.750 seconds. Ref-count changes alone in the earlier detached control were noisy and not monotonically slower; they do not prove progress refs dominate. **The only measured 100-registration full-file add was 59.218 seconds, versus the real probe's 40.350 seconds.** Different paths, cache state, registration HEAD shapes and host contention prevent a reliable full-checkout production prediction. Trimming addresses confirmed registry overhead, while checkout still accounted for 29.973 seconds of the real add; a 180-second success guarantee is unsupported.

## Why settled worktrees accumulate

1. **Settlement releases usage reservations, not the directory.** After saving a terminal report, `AgentTaskReplyService` releases task Launch rows (`server/Application/Services/AgentTaskReplyService.cs:2715`). That is different from the explicit terminal-worktree release API. `TaskWorktreeRetirementService.ReleaseAsync` requires `NoFurtherWorkspaceUse`, reviewed revision/SHA, terminal state, settling floor, unique identity, retrievable artifacts and handoff disposition (`:78`, `:94`, `:108`). `EvaluateEligibilityAsync` refuses absent releases as `release_required` (`:258`). The database contains **zero `TaskWorktreeRetirements` rows**.
2. **The only daily worktree sweep is report-only.** `WorktreeResidue:Execute=false` ships in `server/appsettings.json:215`; `CardDoneAfterUtc` is null. `WorktreeResidueSweepService` checks authorization plus execute before retirement (`server/Application/Services/WorktreeResidueSweepService.cs:201`). Hangfire schedules `antiphon:worktree-residue` for 10:00 Europe/London (`server/Infrastructure/Agents/HangfireConfiguration.cs:59`; settings at `server/appsettings.json:210`). Stored run `5db8ca82-65dc-4f66-9d1a-3d22fe66620a`, Oct 30 10:00:16..10:02:21 UTC: Execute=false, Preview=false, Candidates=6,510, Held=6,510, Removed=0, ActionsAccepted=0. The previous three daily runs also have Execute=false and Removed=0. The newest run's newly persisted candidate reasons include 170 `release_required`, 13 `settling`, five `live_owner`, and 13 inventory `identity_unknown`. These 201 rows are not the whole 6,510: unchanged Held rows are intentionally suppressed (`WorktreeResidueSweepService.cs:405`).
3. **Successful land has a separate cleanup path.** After publication confirmation, `AgentTaskLandingProtocol.CleanupAsync` records Pending, obtains the journal/receipt-backed publication removal authority, invokes guarded removal, and records Complete or Refused (`server/Application/Services/AgentTaskLandingProtocol.cs:419`, `:445`, `:459`). The database has 1,247 Complete published landing operations (1,235 Landed + 12 AlreadyPresent), and 150 Refused published cleanups. Thus “worktrees were never removed after land” is too broad. A task can succeed without requesting land; such a settled task does not gain publication cleanup authority.
4. **`cleanup: NotStarted` is a landing display fallback.** `scripts/delegate.ps1:749` prints it whenever `task.landing` is absent. It does not describe a generic post-settlement directory-cleanup queue or `-CleanupVerification` state. The task GET for audited succeeded task `00861a7a-7d26-4c18-908c-aaa1764f2a61` has `landing=null`, demonstrating this case.
5. **Other commands do not bypass those gates.** `-CleanupVerification` invokes the sourced Mutation snapshot route (`scripts/delegate.ps1:674`); its service requires Mutation + Worktree + SourceLanding + terminal custody contract (`server/Application/Services/VerificationCleanupService.cs:26`). It is not a fleet janitor. Legacy `PruneStaleAsync` calls the untyped removal overload (`WorktreeManager.cs:597`), which refuses `typed_removal_authority_required` (`:536`). Git's dry-run prune identifies only the single missing scratch entry, so pruning would not reclaim the live-directory population. No cleanup command was invoked against an existing tree.

## Remaining uncertainties and decision evidence

- The accumulation mechanism is reconstructed from both live stored rows and source, and the branch-attached registry cost was reproduced in the isolated control. Whether the retention policy should be activated or changed is a separate decision; no new policy was designed here.
- The original 180-second add has no phase trace. Branch-attached registry resolution is a confirmed contributor to ordinary add cost; checkout/file creation under CPU/I/O contention and Defender scanning remain live potential contributors to the timeout; the measurements do not isolate the timeout's dominant cause. Resolve with a future naturally occurring or separately commissioned add trace plus contemporaneous CPU/disk/AV attribution, without disturbing the qualification.
- A true idle and same-path before/after production-registry comparison is absent. The clone control cannot guarantee the next production add duration or a 180-second success bound.
- The CSV is an exact-path screening inventory, not an authorization receipt. Ignored-file/evidence classification, reviewed-work custody, current origin containment, live ownership and the qualification exclusion still require the existing guarded cleanup admission at execution time. No cleanup of a KEEP row is supported by this evidence.

## Not done, noted

A separately commissioned bounded cleanup could consider only the CSV's SAFE-TO-REMOVE paths through existing guarded retirement, preserving all KEEP rows, dirty/unpushed/active or uniquely held reviewed work, retained evidence, and every live qualification directory; automatic-policy design and all production removal remain undone.

--- next stage ---
next: decide
handoff: Decide a separately commissioned bounded guarded cleanup from audit.csv; accumulation and registry overhead are confirmed, but the 180-second timeout has no phase-level reproduction and idle/AV effects remain unresolved.
artifact: docs/investigations/2026-10-31-desktop-worktree-bloat-investigation.md
