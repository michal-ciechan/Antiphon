# S4a independent audit B — 2026-11-04

Found five substantive defects at d95d3f2cdce02fd95bf2145ecee979769cfc4c3c. All five reproduced on Linux using unchanged production binaries. The ordinary targeted tests pass (531 passed, zero failed, five skipped); they do not cover these adversarial outcomes. Do not treat this as a clean Final review or landing approval.

Reviewed repository: /work/worktrees/task-adbd81fe, branch feat/card-task-adbd81fe. Round 1. This is the expressly commissioned Interim audit of TOCTOU, deletion/retry, and feature-switch paths; the brief also appends a generic Final profile. I retain the narrower explicit commissioning cap and report only Interim completion. Whole Unit, native Windows, required manual acceptance, and every PC remain pending. No nightly result discharges them.

Original Code landing owners remain 6dfad742-ef1b-45d5-9f80-b7a1a690eb14 (CARD-1714, latest source repair) and 9f577891-4fed-4f7e-81b5-aeefb4269d19 (delete retry); the earlier S4a owner reported by 577b2ee0 is f5e96e75-5601-4c0c-8359-db05a58c35b0. The reviewed subject is the already combined master snapshot, not an assertion that any owner's current branch equals it. The review evidence names the latest original Code owner, never this Review task. No land is requested.

The higher-priority read-only/snapshot instruction prohibits committing or pushing even a report. Therefore the deliverable and raw evidence are external at /tmp/adbd81fe-evidence, rather than a new tracked docs commit. No product or repository test file was changed, no PC mutation was performed, and no live service/configuration/desktop was changed. The existing task-branch remote remains d95d3f2cdce02fd95bf2145ecee979769cfc4c3c. The standing authorization was sufficient to proceed without requesting approval.

**D-1 — High: the legacy-release switch does not stop every system-release execution path.**

Where: server/Application/Services/DesktopLegacyReleasePolicyService.cs:220, 229, 292, 328; TaskWorktreeRetirementService.cs:317–343; WorktreeResidueSweepService.cs:201–207.

Failure: (a) an eligible pushed tree begins its pre-check while Execute=true; after the actual git status finishes, set Execute=false. The same candidate still files/releases/deletes: `D1_SWITCH_DURING_PRECHECK {"Execute":false,"Outcome":"Removed","ReasonCode":"removed","exists":false}`. (b) a real first unregister failure leaves an S4a system release, the intact tree, and a future legacy retry cursor. Disable BOTH legacy switches. The policy tick preserves it, but run the real residue sweep with its separate WorktreeResidue.Execute=true: the release becomes Complete and the tree disappears while its legacy backoff is still pending. Output: `D1_DISABLED_RESIDUE_SWEEP {"Enabled":false,"Execute":false,"prior":"Refused","backoff":"2026-11-05T00:41:14.391292Z","State":4,"exists":false}` (State 4 is Complete).

Why: execute is captured before per-candidate I/O; StopReason is only called before a candidate. The common retirement executor neither receives nor reads the legacy settings/backoff. The sweep admits every active release using its own independent Execute flag. The existing design explicitly allows sweep competition, but tests it with the legacy feature enabled; it does not establish a universal kill switch. The same-candidate observation is finer-grained than the plan's per-row guarantee and is reported separately from the durable sweep bypass. Shipped WorktreeResidue.Execute is false, so (b) requires that documented separate option to be enabled; no claim is made about live configuration. Both routes are OS-independent; Windows was not executed.

Smallest fix: centralize admission for CallerIdentity=system:desktop-legacy-policy in the retirement executor, reading current Enabled/Execute and policy pacing under the lease before a new attempt or destructive continuation. Recheck the switch after slow pre-checks and before filing. Preserve pending bytes on disable. Add explicit disabled/refused and disabled/Partial residue-sweep cases; do not apply the legacy flag to unrelated caller-filed releases.

**D-2 — Critical data loss: final git status is not a custody boundary.**

Where: server/Infrastructure/Git/LandingGit.cs:840–859; GuardedWorktreeRemoval.cs:134–164, 193.

Failure: with a clean pushed task, the fourth actual source status is the final guarded-removal reading (policy pre-check, release inspection, first removal inspection, final removal inspection). Immediately after git returns exit 0 and empty output, an injected external-write callback creates non-ignored `late-unsaved.txt` containing `only-copy-late-bytes`. The remainder of the real S4a tick reports Removed and deletes the only copy: `D2_LATE_UNTRACKED {"statuses":4,"Outcome":"Removed","ReasonCode":"removed","exists":false,"tree":false}`.

Why: the later ignored listing does not enumerate this non-ignored file. Identity/remote authority checks cannot detect it. The filesystem nested-repository census sees no repository marker and grants no ordinary-file protection. The repository lease coordinates participating application mutations, not an editor, detached child, or process whose database session state is stale. Rename changes the pathname, not the content's exclusive ownership. This is a real-code boundary injection, not a stubbed clean git response. The path is short and ordinary, so Windows long-path checks do not repair it; cross-platform applicability is from code, with Linux execution only.

Smallest safe fix: keep automatic destruction disabled for trees without a proven writer-exclusion/content-custody protocol. Quarantine before a final complete inventory, bind the inventory to the exact tree, and preserve any changed/new entry through deletion. Another uncoordinated git-status call merely moves the race. Add an end-to-end post-status writer witness, including an unregistered child writer.

**D-3 — High data loss: retained evidence is compared by filename, not by retained bytes.**

Where: server/Infrastructure/Git/GuardedWorktreeRemoval.cs:128–143; FileSystemEvidenceRetention.cs:61–82, 95–116.

Failure: an ignored `.antiphon/checkpoint-note.md` contains `retained-old`. A decorator invokes the real FileSystemEvidenceRetention, waits for the verified copy to complete, then changes the source at the same name to `new-only-copy-after-retention`. The second inspection still lists the same filename. The tick reports Removed; the source is gone and the retained artifact still contains only the old text: `D3_EVIDENCE_BYTES_CHANGED {"Outcome":"Removed","ReasonCode":"removed","source":false,"retained":"retained-old"}`.

Why: the retention service returns byte counts and SHA-256s, but guarded removal retains only its display detail and compares the two arrays of names with SequenceEqual. Neither the byte identity nor the retained hash participates in the final removal decision. FileShare.Read only protects the copying interval on Windows, not the interval after the copy is closed. This is independent of whether a filename belongs on the disposable allowlist.

Smallest fix: carry retained file identities and hashes into the final custody check, refuse or recopy changed content, and retain that custody until deletion. Add a same-name/different-content regression after the actual copy. Linux reproduced; Windows logically affected, native proof pending.

**D-4 — Critical data loss: a set-aside receipt authorizes a path, not the current tree or its bytes.**

Where: server/Infrastructure/Git/GuardedWorktreeRemoval.cs:70–104; WorktreeSetAside.cs:8, 18–22; WorktreeNoFollowDelete.cs:59–72; DesktopLegacyReleasePolicyService.cs:273–277.

Failure: cut after the real administrative unregister. The first tick records Partial/worktree_removal_incomplete and leaves the recorded set-aside tree. Two independent scenarios then use the real retirement retry:

- Add `operator-recovery-notes.txt` with the only copy of new work under the set-aside tree. Retry performs ZERO status calls and deletes it: `D4_RETRY_NEW_BYTES {"IsClean":true,"Residue":null,"retryStatuses":0,"asideExists":false,"notesExist":false}`.
- Rename the recorded tree aside to `.rescued`, create a different directory at the exact recorded pathname, and put `foreign.txt` in it. Retry deletes the replacement and reports clean while the original tree remains elsewhere: `D4_RETRY_REPLACEMENT_IDENTITY {"IsClean":true,"Residue":null,"foreignExists":false,"originalSurvives":true}`. The observation precedes fixture teardown.

Why: the durable record contains only three paths and a timestamp, with no filesystem identity or content seal. Pending+unregistered takes the direct DeleteSettled branch, bypassing inspection, ignored classification/retention, and PathLimit. DeleteSettled checks absence and nested repository markers, not the previously approved ordinary files. The same issue exists between Windows short retries and across later scheduled retries. Branch/remote containment cannot reproduce new files or a replacement inode.

Smallest fix: bind recovery to stable directory identity (volume/file ID or device/inode) and a durable authorized-content manifest; never infer deletion authority from the deterministic pathname. Preserve new/changed content and refuse legacy unsealed receipts for manual recovery. Retain the administrative state needed to inspect safely; partial deletion cannot be fixed by treating missing .git as clean. Add new-file and replacement-directory crash/retry regressions, not just retry-count tests. Both reproductions are Linux; logic is shared with Windows.

**D-5 — Critical confinement breach: the Linux no-follow walk follows a concurrently replaced ancestor.**

Where: server/Infrastructure/Git/WorktreeNoFollowDelete.cs:132–145; WorktreeNestedRepositories.cs:84–88. Existing coverage: WorktreeGuardedCleanupTests.C665_JunctionSwappedInAfterSecondReadingKeepsOutsideTarget.

Failure: create a target tree with `child/` and a separate sibling `outside/`, each holding 200 files. A joined local writer uses Linux renameat2(RENAME_EXCHANGE) to exchange `child` with a symlink to `outside` while the actual production `WorktreeNoFollowDelete.Delete(tree, true)` runs. All paths are inside this audit's private scratch root. Case 3 removed 85 files outside the deletion root before returning UnauthorizedAccessException:

`{"n":3,"swaps":4874,"remaining":115,"refusal":"UnauthorizedAccessException:Access to the path '/tmp/adbd81fe-evidence/scratch/unix-race/cases/case-3/tree/child' is denied."}`

The driver exited 10 with `SAFETY_VIOLATION: actual production no-follow delete removed outside files`. Cases 0–2 preserved all 200; the bounded diagnostic stopped on the first actual loss. This is a concurrent native filesystem race, not a changed product method or a fake deletion result.

Why: lstat/LinkTarget inspection and subsequent enumeration/recursive File.Delete resolve the path separately. The Unix comment expressly says there is no pinned directory handle. A checked directory can become a symlink before a descendant is resolved; returning a refusal afterward does not undo the outside deletions. Existing junction tests swap before the walk begins and cannot expose this interleaving. Git and managed-root checks occur even earlier.

Smallest fix: traverse using retained directory descriptors and no-follow relative operations (openat/openat2 and unlinkat with appropriate identity checks); no deletion may resolve through an unpinned ancestor. Until implemented, refuse automated Unix destruction where this custody cannot be established. Add a native concurrent-swap witness that asserts every outside byte survives even on an error. Windows uses a different no-share-delete handle path, so this reproduction establishes Linux only; it does not claim the Windows primitive has this defect.

**Execution and provenance.**

External manifest `audit-checkpoints.md` maps CP-23/24 to the plan's S4a test-design rows. CP-26 is this brief's complete named removal class set plus LandingGitTests for the real inspection. It is explicitly additional because the recent retry/path Code reports used direct class runs and had no matching checkpoint table. Previous reports ff7ab869, 99e23919, d19080aa, 577b2ee0, 52410842, 6dfad742, and 9f577891 were read and retained under reports/. Their historical counts/costs are not relabeled as this review's results. No prior report claimed a completed whole Unit lane.

One checkpoint run, 20261104-233219-7cee: one cold isolated build, three sequential rows. Fresh TRX run IDs and every execution ID were checked: CP-23 155/155 unique; CP-24 55/55; CP-26 326/326 including five NotExecuted. Actual class totals:

| Class | Passed | Skipped |
|---|---:|---:|
| C0824DesktopLegacyReleaseSettingsTests | 32 | 0 |
| C0824DesktopLegacyReleasePolicyTests | 123 | 0 |
| TaskWorktreeRetirementTests | 40 | 0 |
| WorktreeResidueSweepTests | 15 | 0 |
| LandingRemovalPolicyControlTests | 118 | 2 |
| WorktreeGuardedCleanupTests | 47 | 2 |
| SettledWorktreeRemovalTests | 81 | 1 |
| LandingGitTests | 75 | 0 |

No ordinary failure. Skips remain pending: LockedFileMidDeleteResumesOnLaterPass in two classes, HeldHandleDuringCleanupStaysRegisteredOrRecorded, ManagedRootRequired(junction), EmptyUnlistedJunctionTargetSurvivesRemoval. CP-25's native class was not selected. Native creation/open semantics, production WorktreePathLimit.System=260, extended path enumeration, Windows sharing retries, junctions and 8.3 aliases need fresh Windows execution after the restricted qualification window. Injected Windows=true retry controls do not substitute for it. No positive controls were attempted.

The scratch probe runs the existing DesktopLegacyWorld with real Git, a real local bare origin, private Testcontainers PostgreSQL and the production DI graph. Fault hooks schedule external actions at real I/O boundaries. The retention decorator delegates to the real implementation. It ran seven scenarios, with the recorded unsafe outcomes; it is diagnostic evidence, not an extra ordinary checkpoint or acceptance pass. The native race invokes the internal production deleter via reflection and asserts outside-file count; its nonzero exit is the observed safety failure. Neither changes a product implementation.

Production DLL SHA-256: d1ca61878c293cb98b0b95833e67030c2ddf9f7280ba5b8bbc8fc99403bee941, identical in checkpoint output and both scratch executables. Product MVID: 35b9bbd3-8e58-4f8e-ab60-289d5a047f3d. Tests DLL hash likewise matches (see binary-hashes.txt). These hashes bind the diagnostics to the freshly built reviewed source, not stale installed binaries.

Commands (run from the reviewed checkout; bootstrap and scratch builds use native default outputs):

```
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label adbd81fe-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj --property:UseAppHost=false --nologo
TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run --plan /tmp/adbd81fe-evidence/audit-checkpoints.md --total-timeout 35m --row-timeout 20m --hard-wall-timeout 60m --max-wait 45s --serial --keep-outputs
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label adbd81fe-scratch-probe-build -- dotnet build /tmp/adbd81fe-evidence/scratch/probe/Probe.csproj --property:UseAppHost=false --nologo
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label adbd81fe-scratch-race-retry-switch -- dotnet run --project /tmp/adbd81fe-evidence/scratch/probe/Probe.csproj --no-build --property:UseAppHost=false
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label adbd81fe-linux-race-build -- dotnet build /tmp/adbd81fe-evidence/scratch/unix-race/Race.csproj --property:UseAppHost=false --nologo
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label adbd81fe-linux-nofollow-race -- timeout 120s dotnet run --project /tmp/adbd81fe-evidence/scratch/unix-race/Race.csproj --no-build --property:UseAppHost=false
```

All build/test drivers took a slot. The checkpoint tool itself takes each row/build lease. The initial attempt to place checkpoint results outside the checkout was refused before any build/test, exit 2 (`resultsRoot must be under the worktree`); the successful run used the supported ignored local directory, and its receipts are archived externally. Repeated waits returned 75 until joined terminal exit 0 and tool cleanup confirmed dead identity. Scratch bootstrap/build/run commands are intentionally additional, each with the named invariant above. The timeout wrapper's maxcpucount warning is expected for a non-dotnet wrapper: its inner command is --no-build, so it performs no unbounded build.

Performance: checkpoint wall 768.057 s; charged execution 738.042 s; admission-only 30.014 s; build 328.652 s. Row execution was CP-23 134.421 s, CP-24 37.883 s, CP-26 214.380 s. All under 30 minutes. Startup/body/teardown phase measurements and per-test durations are in the raw receipts; finer DB/IO/HTTP spans without instrumentation remain unknown. Row timeouts were a provisional explicit 20m ceiling for one cold build on a loaded host, not a claimed p95 estimate; inherited cost/timeout calibration still belongs to CARD-1404/CARD-1514. Bootstrap held 7s, scratch build 8s, seven-scenario run 44s (15s slot wait), native-race build 6s, native race 4s (four attempts, 120s cap). The seven-scenario diagnostic used the foreground tool's execution window rather than an explicit per-test timeout; no completed timing is relabeled as a bound.

**Other safety checks and limits.**

- Standard static links, nested Git markers and existing protected ignored paths have meaningful preservation assertions in the named tests; the fresh portable cases passed. Retry count/error-code tests assert outcome, residue, calls and retained records; they are not constant/self-comparison stubs. Their missing adversarial contents and mid-walk writer cases are D-2 through D-5.
- The default ignored allowlist deletes bin/obj/bin-*/node_modules/dist/.tmp/out and selected inbox files; unmatched ignored names are protected, explicit protected patterns win, and .antiphon evidence is copied with SHA-256 verification. A path allowlist is a deliberate disposable-content policy, not proof that all ignored bytes are on origin. D-3 audits the promised evidence exception. Broad unusual-filename/Git-hidden-state enumeration remains with sibling audit A as commissioned.
- Scoped exception audit: policy settings OptionsValidationException (:101) disables execution; filing conflict (:315) holds; per-row catch (:370) records deferred/backoff; pre-check IO/permission/argument/timeout (:457) returns inspection_error; stat unknown (:632) forces the budgeted pre-check; normalization/path catches (:748/763/786) refuse. Guarded removal nested-repository catches (:95/194) preserve; delete IO (:96/195), move IO (:165), failed restore observation (:219), outer IO/argument/timeout (:243), unregister IO (:268), branch completion IO (:319) refuse or retain Partial, not clean. Absence-only catches are :452/462/463. However D-5 shows that a fail-closed return can follow real damage.
- LandingGit.InspectAsync :861–873 converts command/IO/permission/invalid-identity exceptions to refusal. Nonzero status/ignored inventory :843/850 refuses, and status stderr :857 refuses. The ignored ls-files stderr has no equivalent check; inventory analysis belongs to sibling A. Unregister :774 refuses missing admin; :787 refuses admin move error; :794 swallows retired-admin deletion IO and returns success after registration was moved. WorktreeSetAside.TryClear :65 returns false on cleanup errors, and callers ignore that result. Those are residual admin/record reporting gaps, not demonstrated fresh working-byte deletion paths here. ParseDispositions :729 returns an empty list on malformed JSON; HandoffResolved still gates a required next stage. Mirror options failure :432 conservatively keeps the mirror; mirror removal exception :519 records residue. NothingRemoved :469 returns false on unknown IO, preventing reopening. No broad catch was accepted as content proof.
- Reflog-only recovery remains the known CARD-1708 exposure: UnregisterWorktreeAsync moves and attempts to delete the linked admin directory (including its HEAD reflog), and TryClear retries that admin deletion. This audit does not discharge the sibling's stash/branch/reflog enumeration and does not file a duplicate demonstrated defect.
- Live-session and workspace-admission checks protect cooperating owners; they do not establish OS writer exclusion. An input-waiting session keeps its seat while parking is disabled, with no automatic release deadline (CARD-1083). No input-wait or delivery path changed here. No producer-to-recipient UserPrompt acceptance, queue/Sent/ack, manual live canary, or notification claim is being credited.
- GET /api/runner-defaults and GET /api/session-runners were read for runtime placement. No runner or platform was pinned and nothing ran on the desktop. No live restart, deployment, land-graph update, destructive cleanup of another task, or remote executor was used.
- check-evidence-diff.ps1 over complete candidate range 240231e86c90707cd6e0d0c88affcd5bddfb4247..d95d3f2cdce02fd95bf2145ecee979769cfc4c3c: 11 commits, zero introduced evidence entries, zero violations. git diff --check also passed. These data-loss paths mostly predate the latest eleven repair commits (blame dates September/October for D-2/3/4/5); the audit demonstrates them at the tip, not a separately executed base. No introduced-by-this-review attribution is made.

Full unedited checkpoint report follows; cleanup and final source status are recorded separately in cleanup.log and final-state.txt.

--- checkpoint report ---
run: 20261104-233219-7cee   manifest: /work/worktrees/task-adbd81fe/.antiphon/checkpoints/20261104-233219-7cee/manifest.resolved.yaml
commit: d95d3f2cdce02fd95bf2145ecee979769cfc4c3c  branch: feat/card-task-adbd81fe  worktree: /work/worktrees/task-adbd81fe  host: Debian GNU/Linux 12 (bookworm) cores=24
source: d95d3f2cdce02fd95bf2145ecee979769cfc4c3c state=clean buildSource=verified
CHECKPOINT CP-23 commit=d95d3f2cdce02fd95bf2145ecee979769cfc4c3c build=ok filter=/*/*/(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=155 passed=155 failed=0 skipped=0 trx=/work/worktrees/task-adbd81fe/.antiphon/checkpoints/20261104-233219-7cee/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=d95d3f2cdce02fd95bf2145ecee979769cfc4c3c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-23 waited=0s executed=134.421s wall=134.481s
PHASES CP-23 slotWait=0s build=328.6522632s startup=11.8039118s testsWall=115.5596658s teardown=5.9387676s hostWall=133.3025334s
SLOW CLASS Antiphon.Tests.Application.C0824DesktopLegacyReleasePolicyTests 461s tests=123 (CP-23)
CHECKPOINT CP-24 commit=d95d3f2cdce02fd95bf2145ecee979769cfc4c3c build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-adbd81fe/.antiphon/checkpoints/20261104-233219-7cee/rows/CP-24/run.trx slot=granted waited=0s dirty=0 source=d95d3f2cdce02fd95bf2145ecee979769cfc4c3c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-24 waited=0s executed=37.883s wall=37.933s
PHASES CP-24 slotWait=0s build=0s startup=11.1335975s testsWall=14.4664813s teardown=11.0404387s hostWall=36.6405181s
CHECKPOINT CP-26 commit=d95d3f2cdce02fd95bf2145ecee979769cfc4c3c build=reused filter=/*/*/(LandingRemovalPolicyControlTests*)|(WorktreeGuardedCleanupTests*)|(SettledWorktreeRemovalTests*)|(LandingGitTests*)/* executed=321 passed=321 failed=0 skipped=5 trx=/work/worktrees/task-adbd81fe/.antiphon/checkpoints/20261104-233219-7cee/rows/CP-26/run.trx slot=granted waited=30s dirty=0 source=d95d3f2cdce02fd95bf2145ecee979769cfc4c3c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-26 waited=30s executed=214.380s wall=244.439s
PHASES CP-26 slotWait=30s build=0s startup=20.17392s testsWall=187.2907091s teardown=6.1079232s hostWall=213.572552s
SLOW CLASS Antiphon.Tests.Infrastructure.WorktreeGuardedCleanupTests 121s tests=47 (CP-26)
SLOW CLASS Antiphon.Tests.Infrastructure.LandingGitTests 109s tests=75 (CP-26)
SLOW CLASS Antiphon.Tests.Infrastructure.SettledWorktreeRemovalTests 63s tests=81 (CP-26)
BUDGET wall=768.057s execution=738.042s admissionOnly=30.014s
unlisted: none (the tool ran no other build or test command)
wall: 12m48s  sequential-equivalent: 6m56s  builds: 1  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: refused/kept bin-adbd81fe-audit/
evidence: /work/worktrees/task-adbd81fe/.antiphon/checkpoints/20261104-233219-7cee/report.md
verdict: GREEN exit=0

Final custody: all owned foreground commands and checkpoint executor joined. The tool removed its own shadow copy after proving the executor dead. I archived the complete checkpoint run at /tmp/adbd81fe-evidence/checkpoint-raw.tar.gz (digest alongside), then removed only this audit's exact literal output roots after confinement/link/tracked-path checks. Bootstrap bin/obj, checkpoint cold output, scratch bin/obj and native-race fixture trees are gone. Reproducer source, logs, report and receipts remain externally. Final git status is empty and local/remote SHA unchanged; final-state.txt and cleanup.log record this. No pushed report is claimed.

Caller containment decision: turning off only WorktreeLegacyRelease.Execute is not a complete stop for already-filed system releases when the residue sweep executes (D-1). D-2 through D-5 also affect other users of guarded removal; fixing the S4a switch alone is insufficient. This read-only audit changed no switch. Commission Code repairs with red-first tests for all five defects, then obtain the missing ordinary/native/manual verification; keep every PC pending for SourceLanding Mutation.
