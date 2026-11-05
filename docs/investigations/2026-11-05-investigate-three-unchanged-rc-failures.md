# Three unchanged rc failures: master containment audit

2026-11-05; Investigate task `36e2134b`; evidence-only, no product/test changes.

**All three reported failure mechanisms have landed repairs on the specified master. Every repair is absent from the failing rc. This establishes source containment, not a successful Windows qualification at master.** No additional unfixed mechanism was established by this investigation.

## Audited identities and method

- Master: `388ff482046a62f813a251208bfeabe7c21808ea` (assigned base, HEAD and fetched `origin/master`).
- Failing rc: `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a` (fetched `origin/rc/2026-11-06`), run `b19a9880`, Windows. The rc is a master ancestor (`git merge-base --is-ancestor`, exit 0).
- Desktop run directory supplied by the brief: `C:\Antiphon\releases\candidates\rc-2026-11-06-5a95ae7a5611\logs\2026-11-04-1808-b19a9880`. Not accessed. Historical failures/results below are explicitly attributed to stored task Results or committed reports; no raw desktop log or TRX was retrieved here.
- Reconstructed each mechanism using `git show <rc>:<path>`, current source, `git log --all --grep`, `git log origin/master -S <test-or-change> -- <path>`, ancestry checks and stable patch IDs. Queried existing task Results read-only with `scripts/delegate.ps1 -Status`; no new task, session input, build, test, desktop command, deployment or rc mutation.

All current-source file:line references below refer to the audited master. Historical references explicitly name the Git object, so they remain retrievable even where the file is absent from master.

## Containment measurements

For each row, ran `git merge-base --is-ancestor <commit> <ref>` separately. Exit 0 means contained; exit 1 means absent. Original task commits are **not** ancestors of either ref; their landed equivalents are ancestors of master only. Calling the original SHA absent without checking the equivalent patch would give the wrong answer.

| Repair | Original task commit (master/rc exits) | Landed master commit (master/rc exits) |
|---|---|---|
| C448 bounded delete retry | `37105c2e4e498efb7800ade0cc821267e394678d` (1/1) | `d9904b1dc2f2a4c01b0066aa3efa58d8c6c795a0` (0/1) |
| C448 after-pause absence witness | `3b75cf53f879ccf21a8a62438cded5c1ab4d5c16` (1/1) | `e85c53930a553f38ab461ceeae1620d124e98bb6` (0/1) |
| Long-path S4a precheck repair | `29e6816ceec487f1c1dbdb48a144d309030b747a` (1/1) | `e9d7837695dc5d39070de59a91f9940fadbc17e3` (0/1) |
| CARD-1714 removal recheck/path guard | `a670e82a9d0d5aef73bd168f5d0d5bcc7fbfa3b9` (1/1) | `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c` (0/1) |
| C467 V32 test timing repair | `d19fa358334dd37861cdc6966291f2a50508c5af` (1/1) | `7c9c4cb156486a9af57f11f90cdb925ae83023ee` (0/1) |

`git show --pretty=format: <commit> | git patch-id --stable` produced identical IDs within each original/landed pair:

| Pair, in table order | Stable patch ID |
|---|---|
| C448 retry | `650a34d3aee09ec653b917aa4d149838f46859a0` |
| C448 witness | `590b26035ff14e236a55028ae364d00953879e58` |
| S4a precheck | `2967af8ba6ed15eda519119e5c14407bedd48634` |
| CARD-1714 | `ae8ea123c923b2dace69c41175156a1a410d9b1e` |
| C467 timing | `79407d4936e9f99e68daa22838ea045b31ba6a1f` |

The C448, S4a and C467 landed commit messages also identify their original cherry-picked SHAs. CARD-1714 has the same stable patch ID despite its different commit identity. Its test/seam commit `e2bc1c2dfe2eb89d2ff9941f16363ca2e4aba1a0` is likewise on master (exit 0), absent from rc (exit 1).

## A. C448 V36, antiphon group 114

**Confirmed mechanism: an unretried set-aside delete failure/remaining directory made the clean baseline fail. Repaired on master; absent from rc. Exact native error remains unknown.**

Stored failure evidence is `5b2c5a024499f442552f28c065c19379214318a0:docs/investigations/2026-11-04-c448-delete-step.md:19`: `C448_V36_EachAuthorityCoordinatePrecedesMutation(verified-sha)`, 49.4 ms, baseline assertion at test line 189, with `worktree_removal_incomplete set-aside: ...\.source.removing-...`. This is the unchanged safe-control removal before the authority coordinate is changed; current test `tests/Antiphon.Tests/Infrastructure/LandingRemovalPolicyControlTests.cs:182` makes that ordering explicit.

At rc `server/Infrastructure/Git/GuardedWorktreeRemoval.cs:182`, the post-unregister path calls `WorktreeNoFollowDelete.Delete` once. Lines 184-193 discard an I/O exception or refuse if the set-aside directory remains visible. Both emit the same detail. The stored investigation at lines 25-47 reconstructs why the fresh fixture and successful fake unregister rule out the other residue sites. Its lines 49-62 observe that even one 100 ms sharing-open retry cannot fit the recorded 49.4 ms total. That excludes a retried code-32 open as the observed delay; it does not identify the actual delete error or scanner.

Windows differs because the no-follow implementation uses `CreateFileW` and handle disposition (`server/Infrastructure/Git/WorktreeNoFollowDelete.cs:101`, `:118`). A sharing/access/not-empty refusal or a classic delete-pending directory can outlive the first walk; Unix uses unlink/directory removal (`:132`). The stored failure did not preserve the code, so a specific Defender handle or Win32 145 is a hypothesis, not an observed fact.

Master routes both initial and resumed deletion through `DeleteSettled` (`server/Infrastructure/Git/GuardedWorktreeRemoval.cs:94`, `:193`). Its loop at `server/Infrastructure/Git/WorktreeNoFollowDelete.cs:59` retries up to five whole walks with 100 ms pauses, including a still-visible successful return and an absence check after the pause. `server/Infrastructure/Git/WorktreeRenames.cs:49` recognizes raw/HRESULT codes 5/32/33/145 only on the Windows path. Lasting refusals remain recorded; thrown failures now include their code (`GuardedWorktreeRemoval.cs:200`).

Historical reproduction/verification, not runs by this task:

- Code report `abbda246b055dfebc4503862f53c69d8448b29ad:.antiphon/task-9f577891.md` records injected-fault red at `8297fb9a28af1b287b390193b3456a7fb0326435`: 69 cases, 23 failed/46 passed; at fix `37105c2e4...`, affected classes 809 total, 797 passed/0 failed/12 Windows-only skips, including baseline 23/23 and new witnesses 24/24.
- Review row `d19080aa-d1a4-4705-873a-990763c26ccd` (Succeeded, Result retrieved) found D-1: the after-pause check lacked a witness; it did not find a production defect in the retry. Original review report is committed at `9d2e68f4643a0b827149c8ea6d34f79f2e14737a:.antiphon/task-d19080aa.md`.
- Code row `eab3a4fc-cfb8-4ea3-ab3d-97f6d31c8100` (Succeeded, Result retrieved) records both new cases failing when the check is removed, then 71/71 green restored at `3b75cf53f...`. The witness is now `tests/Antiphon.Tests/Infrastructure/LandingRemovalPolicyControlTests.cs:916` and its landed patch is in the containment table.

Remaining uncertainty: a lasting or different native refusal can still fail closed. No fixed-tip native rerun was performed here, so the original flake's disappearance is not measured.

## B. Long_path_untracked_file_holds_the_tree, antiphon group 95

**Confirmed mechanism from stored reproduction and source: status could omit the untracked long path while exiting successfully, permitting retirement. Both the precheck repair and subsequent removal guard are on master, absent from rc.**

The native test creates `src/.../unsaved.txt` past 260 characters using the extended Windows path prefix and expects Held/source_dirty with byte-intact tree and branch (`tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseNativeTests.cs:104`, especially `:110`, `:123`). It explicitly skips Linux (`:106`). The introduction from task `4b9d4604`, original `566605c092dfaed7f3ad0affb1f36aa2af39d2fc`, landed as `65cc9fb85549b92c3801d536316a065a890332f2`; **that test-only commit is already in rc and master (0/0)**. It supplies the failing witness, not the repair.

At rc `server/Application/Services/DesktopLegacyReleasePolicyService.cs:405`, precheck runs plain `git status`; it treats exit 0 plus empty stdout as clean. The same blind status exists in removal inspection. Stored Debug row `52410842-b68f-4817-8c5b-ec887e532748` (Succeeded, Result retrieved) and `5233faefdedd2d024fcbbabeb02e1e9146b03a70:.antiphon/task-52410842.md:33` explain the Git for Windows stderr-only warning when a directory is too long without core.longpaths. The status command is a CLI read, not a libgit2 read. Its stderr fact was discarded. Managed enumeration can still see that directory, allowing move-aside and unregister before the raw Win32 delete fails. The Partial policy mapping follows once DirectoryGone/Unregistered is set (current `DesktopLegacyReleasePolicyService.cs:352`). The exact set of deleted files in the old Windows run was inferred in Debug, not measured on disk; no data-loss amount is claimed here.

Master precheck now runs `git -c core.longpaths=true status ...`; nonempty stdout yields source_dirty, and a clean listing with stderr yields status_untrusted (`server/Application/Services/DesktopLegacyReleasePolicyService.cs:413`). CARD-1714 applies the same rule to `LandingGit.InspectAsync` (`server/Infrastructure/Git/LandingGit.cs:838`). It also measures paths under their longer future set-aside name before mutation (`server/Infrastructure/Git/GuardedWorktreeRemoval.cs:144`); Windows limit is 260, extended paths are used only for enumeration (`server/Infrastructure/Git/WorktreePathLimit.cs:15`, `:49`). Policy maps that unchanged-tree refusal to Held with backoff (`DesktopLegacyReleasePolicyService.cs:345`). This also addresses the deep ignored-tree sibling, which status intentionally omits.

Historical Linux evidence:

- Debug report `5233faefd...:.antiphon/task-52410842.md:56` reconstructed the same status hazard using a real unreadable untracked directory: Git 2.47.3 exited 0, stdout empty, stderr Permission denied. Linux's later ignored-content guard happened to refuse that tree, so this did not reproduce Windows deletion semantics. At red `58fdfa9d6...`, two witnesses failed; at `29e6816ce...`, two passed and CP-23 passed 154/154 (`:86`).
- Follow-up report `4d30e022607354205a363bdaebacbefb78565704:.antiphon/task-6dfad742.md` records 7 failed/1 passed at red `0cd3f164d...`; at `a670e82a9...`, three selections total 955 cases, 949 passed/0 failed/6 native skips. These injected-limit and stderr witnesses establish portable policy behavior, not native MAX_PATH behavior.

Remaining uncertainties: actual Windows N-2b Held/source_dirty and ignored-tree Held/worktree_path_too_long need native reruns. The path guard does not restore a tree already set aside/unregistered by the old code; the pending-record branch proceeds to deletion (`GuardedWorktreeRemoval.cs:94`). That historical-residue limitation is documented in the follow-up report and is separate from preventing a new N-2b retirement.

## C. C467 V32, e2e split 5

**The identified failing case is `AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome("attempt")`. Confirmed test timing race, with test-only repair on master and absent from rc.**

Stored Debug row `8b609b8a-ae27-41c2-911e-273d08fc73fa` (Succeeded, Result retrieved through `delegate.ps1 -Status 8b609b8a`) identifies b19a9880's actual headline as `AwaitingReceipt; attempted; receipt unconfirmed`. It says the busy variant passes under either schedule because both headlines contain busy. `C488_ApprovalPollingCannotConfirm` invokes the busy variant (`tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:545`); it is not the identified failure here.

At rc test lines 279-290, the test advances fake time to 301, makes one status call, advances to 901, waits only for two Aged rows, then expects the Aged headline. That does not establish when either row was created.

Reconstruction from product code:

1. `server/Infrastructure/Orchestration/AgentTaskLandMonitorHostedService.cs:17` sweeps independently, then waits LandSweepSeconds. The stored Debug Result identifies the configured five-second sweep.
2. At age 901 a single sweep may create both Warning and Error because the checks are independent (`server/Application/Services/AgentTaskLandMonitorService.cs:66`). Each event uses the sweep's current time (`:79`).
3. Each notice's own age controls attention eligibility. Notices younger than LandWarningSeconds are hidden absent error/parking (`server/Application/Services/AttentionService.cs:1581`). Defaults are 300/900 seconds (`server/Application/Settings/DelegationSettings.cs:660`).
4. The feed collapses only listed receipts and takes the newest one's headline, carrying maximum severity (`AttentionService.cs:1811`). Thus a Warning created at 301 is listed at 901; Warning and Error both created at 901 are both hidden, leaving the attempted Outcome. Both schedules still satisfy the old two-row wait. The product debt remains unconfirmed in either case.

Master test now waits for exactly one Aged row while time remains 301 before advancing to 901 (`tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs:279`). It then pins the selected row to the older Warning (`:310`). Added portable cases explicitly reconstruct both DB arrangements and assert different selected IDs/headlines (`tests/Antiphon.Tests/Application/AttentionFeedNoiseTests.cs:346`). Stored Debug Result reports 14/14 unit cases passing, zero failed/skipped; the E2E build succeeded but the E2E was not run because its fixture requires Windows. The original and landed changes have identical stable patch IDs.

Remaining uncertainty: the corrected E2E case's Windows execution. The timing mechanism and feed outcomes can be established on Linux; the whole real-process E2E cannot be qualified there under this fixture.

## Original branch custody and present state

No required repair remains available only on an unlanded branch. Original identities remain on these remote task branches, while the equivalent patches are already published on master as measured above:

| Task/branch suffix | Observed original branch tip | Evidence/state |
|---|---|---|
| `9f577891` | `abbda246b055dfebc4503862f53c69d8448b29ad` | Code report plus retry; production patch landed as d9904b1dc... |
| `eab3a4fc` | `3b75cf53f879ccf21a8a62438cded5c1ab4d5c16` | Succeeded Code Result; witness landed as e85c53930... |
| `52410842` | `5233faefdedd2d024fcbbabeb02e1e9146b03a70` | Succeeded Debug Result; precheck patch landed as e9d783769... |
| `6dfad742` | `4d30e022607354205a363bdaebacbefb78565704` | Committed Code report; follow-up patch landed as d95d3f2cd... |
| `8b609b8a` | `d19fa358334dd37861cdc6966291f2a50508c5af` | Succeeded Debug Result; test repair landed as 7c9c4cb15... |
| `4b9d4604` | `566605c092dfaed7f3ad0affb1f36aa2af39d2fc` | Native witness addition; equivalent 65cc9fb85... already in failing rc |

All branch names are `origin/feat/card-task-<suffix>`. A task's legacy Land/Publication fields can say Not requested/Unconfirmed despite these independent patch-containment facts; they are not evidence that a cherry-picked master patch is missing.

## Verification boundary and disposition

This investigation ran **zero builds and zero tests**. It reuses the explicitly attributed stored reproductions above and measures Git containment/patch equivalence now. No bin/obj/test-result outputs were created, so no build-output cleanup is due. There is no claim that master or a new rc is qualification-green.

The failing run predates all five repair patches. The remaining operational proof is qualification of a new rc containing them: group 114 (including repeated V36 baseline and the native sharing cases), group 95 native long-path cases, and e2e split 5/V32 attempt and busy. Run those through the existing qualification/build-slot owner on Windows; nothing was run there in this task. The committed report is the only assigned change; no additional fix design is needed for the three known mechanisms.

## Not done, noted

Native fixed-tip qualification and recovery of any pre-existing partially removed tree were not performed; no new fix proposed or implemented.

--- next stage ---
next: none
handoff: All three known mechanisms already have landed master repairs absent from rc 5a95ae7a5; cut/port through the release owner and qualify groups 114/95 and e2e split 5 on Windows. No new Code stage established; native reruns remain unproven.
artifact: docs/investigations/2026-11-05-investigate-three-unchanged-rc-failures.md
