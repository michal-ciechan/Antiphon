# CARD-1015: keep generated verification evidence out of Git

Date: 2026-10-03. Plan task: `b29278e3-8d7e-4a39-8d0d-04d1d8a3cee9`.
Inspected source: `bb5fa774cd56f85ee6f0b1122c198192427e5ddf`.
Latest observed master: `b0ccedda2e93a6b847e9219a602a3413694f64ea`; its changes since the inspected source do not touch the relevant bundles, Git infrastructure, scripts, CI, testing owner or `.antiphon/` inventory.

Status: **Verification freeze finalized after the operator accepted option 1 in task b6180921.** D-1 is unchanged: at inspected master delete 289 rejected paths and preserve 23 permitted Markdown files, including the six c998 summaries. Anchor all original 108 classifications: 88 delete, 20 keep. The rule is recomputed at Code's immutable start commit; 289 is an observation, not a future literal limit. Land this amended plan doc-only first, then dispatch fresh Code from current master containing the plan and CARD-1005. Original fix-design text remains historical where superseded by the amendments and Verification design. No builds, tests or mutations were executed in this documentation stage.

## Outcome and scope

Choose direction 1 from the card: commit small Markdown reports/provenance when needed, with unedited `CHECKPOINT` lines; leave generated checkpoint outputs outside version control. Add a real Git diff guard, its fixture tests, and a CI invocation. Keep the 108 existing tracked evidence paths unchanged. Do not change landing admission, source qualification, checkpoint receipt formats, report storage, or cleanup authority.

The card remains actionable. `card.ps1 get/history CARD-1015 -Board Antiphon` showed no preceding decision or verdict; the only history entry was this Plan dispatch. A complete board search for `force-added` returned CARD-1015 and CARD-0965. CARD-0965's full Done record explicitly lists CARD-1015 as its follow-up, rather than satisfying it.

## Ground truth

| Card assumption | Observed code or repository fact | Consequence |
|---|---|---|
| 108 tracked `.antiphon/` paths, about 17 MB | `git ls-tree -rl HEAD -- .antiphon/`: 108 blobs, **17,775,541 bytes** (16.95 MiB). Checkpoints: 37 blobs, **13,055,657 bytes**. Largest two: `.antiphon/checkpoints/20261003-123950-83fa/rows/CP-6/run.trx` at 5,957,130 bytes and `20261003-115340-1393/rows/CP-6/run.trx` at 5,917,689 bytes. | The accumulation is present, not hypothetical or already fixed. |
| About 18 of the last 60 commits touch evidence; roughly 3 MB per heavy task | At the inspected HEAD, 9 of the last 60 commits touch `.antiphon/`. Recent examples include `29d7c3ebb4`, `496a0d95b9` and `eacb29ff3c`. A per-task growth rate was not independently established. | Treat the card's rate as historical context, not an exact current measurement or acceptance threshold. |
| `.gitignore` should exclude evidence | `.gitignore:53-55` ignores `.antiphon/`; tracked files are still tracked. The tree proves tracked inclusion, not which exact historical `git add` command was used. | Keep the ignore rule; adding another ignore pattern cannot fix already tracked or deliberately force-added files. |
| Stage bundles explicitly order evidence commits | `stage-code.md` orders slice commits, exact CP lines and evidence paths; `delegate-basics.md` broadly requires committing changed work. Neither explicitly says to force-add TRX/checkpoint directories. `stage-review.md` says read-only. | Correct the ambiguous boundary in the common contract. Do not describe an explicit raw-evidence instruction as an observed fact. |
| Debug/Docs have stage text to edit | `InstructionBundles.ForDelegate` gives these helper roles only `delegate-basics`; there are no `stage-debug.md` or `stage-docs.md` files. | Put the common rule in `delegate-basics.md`; test actual composed Debug and Docs instructions. Do not create new stage bundles. |
| Bundle room is available | Normalized, trimmed text lengths: Code 2,402; Review 2,475; basics 6,850; orchestrator 14,309. `InstructionBundleTests` caps each stage at 2,500 and the LF-normalized orchestrator file at 14,310; default compositions must fit the command-line budget. | Reword Code/Review compactly, rather than just append paragraphs. Leave orchestrator unchanged; still run its cap and composition tests. |
| Uncommitted generated evidence makes landing source dirty | `LandingGit.InspectAsync` uses ordinary porcelain without `--ignored`, then separately inventories ignored content in Full scope. `SourceSnapshot.Capture` likewise ignores untracked ignored output. Existing tests `C642_IdentityAndStatusScopeSkipsIgnoredListing` and `clean_and_ignored_outputs_match_head` prove this distinction. | No clean-tree exemption or relaxation is needed. Modified **tracked** evidence remains dirty and must not be hidden. |
| Ignored evidence will automatically be retained and cleaned up | `WorktreeCleanupSettings.DefaultRetainedIgnored` recognizes task reports, TRX and checkpoint folders, but `Program.cs` registers **`RefusingEvidenceRetention`**. Nonempty evidence returns `evidence_retention_unavailable`. The copying implementation in `AgentTaskLandPublicationTests.Delivery.cs` is a test double. | Publication can succeed with cleanup residue. Do not promise raw-file retention in another store or automatic worktree deletion. This is a material owner decision, D-5. |
| Report persistence requires committing raw files | `AgentReportStore.StoreAsync` retains exact `task.Result` text outside delegate worktrees, with a digest, normally in the canonical ignored `.antiphon/reports/` directory. It refuses tracked destinations. It does not recursively retain the raw files referenced by that text. | Keep the final report and essential CP/provenance facts self-contained; raw local paths alone are not durable report contents. Never force-add the canonical report store. |
| Checkpoint lines can replace all receipt handling | `CheckpointLine.Format` includes counts, commit, slot/wait and source/build provenance. `ReportWriter` also writes structured `report.json`; strict validation consumes structured evidence, not merely a pasted line. | Keep generating and validating raw receipts and TRX during verification. Change what is committed, not what is produced or checked. |
| An existing guard prevents recurrence | `.github/workflows/ci.yml` has no `.antiphon/` diff guard; the inspected source tests have no such policy test. Archives already present include a 997,368-byte `.tar.gz` and smaller `.trx.gz` files. | A size-only guard is insufficient. Reject generated formats as well as checkpoint directories. |

Sources inspected include the files named above, `docs/project-context.md`, the relevant landing/report sections of `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, `docs/ops-http.md`, and checkpoint/build-slot/source-qualification sections of `docs/testing-and-build.md`.

## Decisions

These are stated defaults for owner ratification, not a claim that the owner has already approved a direction.

- **D-1 — Direction 1, compact Markdown only.** New or changed Git entries under root `.antiphon/` may be regular Markdown report/provenance files, outside checkpoint subtrees, at most **1,048,576 bytes (1 MiB) per blob**. Aim for short summaries; 1 MiB is a hard ceiling, not a target. Raw TRX, JSON receipts, logs, archives, binaries and checkpoint folders stay ignored regardless of size. Plans and other real deliverables keep their established `docs/` destinations. Do not relocate generated payloads elsewhere in the repository to evade this policy. Reason: the card needs a small policy/guard change, not a storage service.
- **D-2 — Leave the existing 108 paths unchanged.** Grandfather only unchanged entries already in the supplied comparison base. Deletions are allowed; any subsequent addition, content/type change or rename into a guarded path must satisfy D-1. No history rewrite, blanket `git rm`, retention schedule or cleanup commit is part of this card. Reason: removal does not shrink history and could break old report links; stopping new growth is independently useful. If the owner prefers removal, commission a separately reviewed documentation/evidence cleanup after this policy is active, with an exact path inventory.
- **D-3 — Inspect Git objects across the proposed commit range.** Use an explicit, resolved base/head pair, all commits newly reachable from head relative to base, and each commit's diff against its first parent (root against the empty tree). Disable rename detection so destination entries are checked as additions. This catches a large file added and deleted before the final tip, which a net-tip diff misses even though Git history retains its blob. Scope only root `.antiphon/`, matched case-insensitively for Windows portability. Treat any descendant directory component containing `checkpoints` case-insensitively as forbidden, covering the existing `cNNN-checkpoints` convention as well as `checkpoints/`.
- **D-4 — Enforce through one read-only script, ordinary Code/Review policy and CI.** Add `scripts/check-evidence-diff.ps1`, called by fixture tests, the plan's CP-5 and a small independent GitHub Actions workflow. Do not add a production land-service refusal or silently widen `LandVerifyFilter`. A CI result is a guard result, not an assertion that branch protection is configured. Code and Review must still run/report the guard against the full task range, including report-only commits.
- **D-5 — Preserve the existing cleanup refusal.** Ignored outputs do not break source cleanliness, but local guarded cleanup may retain the worktree because the copying retention seam is unavailable. Report publication and cleanup separately and preserve residue; never force-add or delete raw evidence just to make cleanup green. Runner mirror persistence must not be inferred from local desktop cleanup behavior. Raw artifacts have no new durability guarantee under direction 1; the persistent minimum is the essential report text/provenance. If the owner requires automatic cleanup plus durable raw artifact retrieval, direction 2 or the unfinished retention implementation needs a separate design before Code.
- **D-6 — Keep source identity honest.** Optional compact reports may be committed individually; no directory-wide force-add. Preserve the actual tested SHA in every line. A report-only commit is still a new HEAD: the final Review must qualify the exact pushed tip. Do not relabel an earlier receipt with the report commit's SHA. Freeze tracked reports before final qualification and put later final receipt facts in the task's stored report; do not create a recurring report-commit/qualification loop. Read-only Review and SourceLanding exceptions keep their existing authority boundaries.
- **D-7 — Use the runtime runner preference.** The two required GETs were read at approximately 2026-10-03 14:59 UTC: defaults revision 2 had no kind overrides; the preferred Linux lane and a Windows lane were available/eligible, and another entry was unavailable/draining. These are observations, not routing pins. Every checkpoint below uses the **Any lane**, requiring Git, PowerShell and the repo SDK where applicable. Omit `-Runner` and `-Platform`; explicitly use `-Platform Any` only to remove an inherited pin. No host path, address or fleet placement is embedded in this plan.

Rejected alternatives:

| Alternative | Why not the default |
|---|---|
| Artifact store or release assets plus manifests | Requires credentials, upload/retrieval custody, retention/failure semantics and authorization decisions. The existing raw-evidence retention seam is not a finished substitute. This is appropriate only if D-5's limitation is unacceptable. |
| Continue committing and periodically prune | Still writes every generated blob into Git history, keeps clone growth and adds recurring work. |
| Ignore rules alone | Already present; they cannot prevent explicit adds or updates to tracked files. |
| One-MiB guard alone | Compressed TRX and archives already fall below that limit. D-1's Markdown restriction prevents that workaround. |
| Guard only the final tree diff | Misses intermediate add/delete commits that still enlarge history. |
| Require all raw evidence to be deleted before land | Conflates clean tracked source with custody and could destroy the only surviving raw results. |
| Relax landing/receipt checks or expand evidence to disposable globs | Unnecessary for publication and weakens established safety boundaries. |

The caller should record acceptance or changes to D-1, D-2 and D-5 on the card's decision/move revision. This Plan can settle `done / next: decide`: the reviewable artifact exists without an unresolved implementation guess. Once accepted, dispatch **TestDesign**, not Code directly.

## Implementation design

### Evidence instructions

Add the common rule beside the commit instruction in `server/Bundles/delegate-basics.md`: commit source/deliverable slices, keep generated evidence ignored, keep essential unedited checkpoint lines in the report, and use the diff guard before handoff. Make clear that the generic requirement to commit changes does not apply to generated outputs. Canonical `.antiphon/reports/` storage remains runtime-owned and ignored.

In `stage-code.md`, compact the existing report/checkpoint prose to require the small summary and the guard without removing strict source provenance, ordinary scope or pending-Mutation requirements. In `stage-review.md`, compact existing prose to require auditing the candidate evidence diff and report facts without suggesting Review should commit changes. Keep each at or below 2,500 characters and ASCII-only. Do not change `orchestrator.md`, role composition or the cap values.

Document the precise storage/size rule, inspection requirements, read-only Review behavior, final-SHA sequencing and cleanup limitation in `docs/testing-and-build.md`; document the publication-versus-residue consequence beside the landing guidance in `docs/orchestration-loop.md`. Change only the explanatory `.gitignore` comment if needed; preserve the ignore pattern. Historical plans and evidence reports are records and are not bulk-rewritten.

### Diff guard

`scripts/check-evidence-diff.ps1` is a read-only ASCII PowerShell script. Its normal interface is `-Repository <path>` (default current repository), required `-BaseRef <ref>` and `-HeadRef <ref>` (default HEAD). Resolve refs once to full commit IDs, require base ancestry, and print the resolved range. No automatic fetch, staging, deletion, checkout or configuration changes. A missing object/ref, unrelated range, malformed Git output or nonzero Git command is an error, never a successful empty selection. Base equal to head is a valid, explicitly reported zero-commit range.

Read NUL-delimited raw Git records with full object IDs and no rename detection. Use argument vectors and literal paths; do not parse human-quoted line-based filenames or interpolate path text into shell code. Filter the actual destination path under root `.antiphon/`; use the new object ID and `git cat-file -s` for the blob's byte size. Do not read worktree file lengths or follow symlinks. Only regular-file modes and `.md` destinations are allowed. Report paths escaped safely for control characters, the commit, bytes where relevant, and a stable violation reason. Do not print payload contents. Deletion records do not need a destination object. Check all introduced commits so an intermediate violation cannot be hidden by a later deletion.

Exit contract: **0** for a proven compliant range, **1** for policy violations, **2** for an unverifiable range/input/Git failure. Both 1 and 2 fail the CI/checkpoint command. Diagnostics include commit and inspected-entry counts so zero changes are distinguishable from failed enumeration. There is no caller-supplied size override in ordinary use; fixture boundary tests construct blobs around the actual one-MiB limit.

The GitHub-event parameter set reads an event JSON path and event name supplied through environment-backed arguments, rather than shell interpolation. It resolves the range as follows:

| Invocation | Required range |
|---|---|
| Task Code/Review | Caller-recorded task base through exact pushed HEAD; this card starts at `bb5fa774cd56f85ee6f0b1122c198192427e5ddf`. |
| Push to a feature branch | Merge-base of the observed remote default-branch tip and event head through event head. Recheck the cumulative unlanded branch on every push. |
| Push to the repository default branch | Nonzero event `before` through event head, covering every new commit. |
| Manual workflow | Required explicit base input through selected head. |

The new workflow `.github/workflows/evidence-policy.yml` follows this repository's `master` / `feat/**` push scope and permits manual dispatch. Checkout full history; use the event head rather than whichever commit HEAD happens to name. A deleted branch may be explicitly excluded; an unknown event, missing manual base, missing remote/base object, unexpected zero `before` on the default branch, or unrelated range fails visibly. Do not use `continue-on-error`, success fallbacks, path filters that miss rename sources, or a net-diff-only shortcut. The workflow performs no builds or tests, so it needs no build slot. Do not modify the unrelated existing CI build drivers in this card.

### Slices

| Slice | Files | Implementation and associated tests |
|---|---|---|
| **S1 — State the evidence boundary** | `server/Bundles/delegate-basics.md`, `server/Bundles/stage-code.md`, `server/Bundles/stage-review.md`, `docs/testing-and-build.md`, `docs/orchestration-loop.md`, optional `.gitignore` comment; `tests/Antiphon.Tests/Application/InstructionBundleTests.cs` | Add the rule and source/retention explanation. Add composed-role assertions for Code, Review, Debug and Docs, plus preservation of SourceLanding's external-only/no-commit exception. Existing ASCII, per-stage, orchestrator and composed-budget tests remain unchanged in strength. Commit and push S1 with verification pending. |
| **S2 — Guard actual proposed history** | New `scripts/check-evidence-diff.ps1`, new `.github/workflows/evidence-policy.yml`, new `tests/Antiphon.Tests/Scripts/EvidenceDiffGuardTests.cs`, new `tests/Antiphon.Tests/Infrastructure/EvidencePolicyWorkflowTests.cs` | Implement one guard and wire the real CLI into CI. Use temporary Git repositories for its behavioral tests and a small Unit contract test for workflow wiring. The process-spawning class uses `[ParallelLimiter<ProcessSpawnLimit>]`. Test helpers may be private to the new class; change a shared fixture only for a demonstrated missing capability. Commit and push S2 before the checkpoint run. |

Run the eventual frozen manifest once for the S1-S2 group. Fix only a demonstrated defect, commit/push the fix, then rerun affected rows under the same IDs. No cleanup slice is planned under D-2.

## Verification approach

TestDesign is a separate stage. The following is the concrete test blueprint and initial checkpoint manifest, not a claim of a complete frozen `## Verification design` or executed controls. TestDesign must inspect the actual helper bodies it chooses, bind final method names and assertions, enumerate independently bypassable guards/PCs, and finalize counts/cost without widening implementation scope.

### Read fixtures and existing witnesses

- `InstructionBundleTests`: actual role composition, common-rule pins, the 2,500-character stage limits, orchestrator 14,310-character limit and full command-line-budget composition.
- `LandingGitTests.C642_IdentityAndStatusScopeSkipsIgnoredListing` and `LandingGitFixture`: real temporary Git repos, ignored path inventory and clean source admission.
- `CheckpointSourceStateTests.clean_and_ignored_outputs_match_head`: real Git source capture, ignored-output fingerprint stability and rejection of changed tracked output.
- `LandingRemovalPolicyControlTests.C665_RetentionRefusalPreservesTree`: no deletion after retention refusal; this is already in Unit. Its retention double does not prove that production copies evidence.
- `WorktreeIgnoredContentClassifierTests`: default checkpoint/TRX classification; already in Unit.
- `DelegateScriptRunner`, `Scripts/ScriptHarness` and `AppHostBrokerSourceGuardTests`: existing process argument-list and source-contract patterns. No live API, database, production runner, provider or remote Git repository is needed by the new guard tests.

### Proposed behavior and regression cases

All twelve entries below are proposed ordinary methods on `EvidenceDiffGuardTests`, each executing the **real script** against fixture-owned repositories. Each method is one TUnit execution; internal boundary examples do not increase that count. TestDesign may split methods with independent guard witnesses and then update CP-2's count before Code.

| ID | Proposed method | Decisive observation |
|---|---|---|
| V-1 / R-1 | `Rejects_checkpoint_paths` | A tiny `.md` added below `checkpoints/`, `c1015-checkpoints/` or a nested checkpoint directory exits 1 with the path/commit; the folder rule is independent of size/extension. |
| V-2 / R-2 | `Rejects_non_markdown_outputs` | Tiny TRX, `.trx.gz`, JSON receipt, log and archive entries exit 1 outside checkpoint folders; a symlink/gitlink under `.antiphon/` is rejected without following its target. |
| V-3 / R-3 | `Enforces_one_mib_blob_limit` | Markdown at 1,048,575 and 1,048,576 bytes passes; at 1,048,577 fails. Include multibyte UTF-8 so character count cannot masquerade as bytes. |
| V-4 | `Allows_small_markdown_and_other_source` | A compact report/provenance note and unrelated large source fixture outside root `.antiphon/` pass; `.antiphon-other/` is not a false match. |
| V-5 / R-4 | `Grandfathers_unchanged_legacy_and_allows_deletion` | A base containing forbidden legacy artifacts is accepted when unchanged or deleted; the test does not require deleting real repository evidence. |
| V-6 / R-5 | `Rechecks_modified_and_renamed_legacy_paths` | A modified legacy raw artifact or rename/copy into a guarded destination is rejected; renaming a legacy artifact does not grandfather the new path. |
| V-7 / R-6 | `Checks_intermediate_commits_even_when_tip_is_clean` | Add forbidden artifact in one commit, delete it in another: range still exits 1 and identifies the introducing commit. Include a merge carrying an offending side-branch commit. |
| V-8 / R-7 | `Reads_pinned_git_objects_not_index_or_worktree` | Committed bad/staged good and committed good/untracked bad have opposite outcomes according to committed bytes; verification does not alter HEAD, refs, index or worktree. |
| V-9 / R-8 | `Handles_literal_paths_and_case_variants` | Paths with spaces, tabs/newlines, non-ASCII characters and case variants cannot hide a violation or execute shell text. Use literal Git objects for cases the current OS cannot create as filesystem names. |
| V-10 / R-9 | `Refuses_unverifiable_ranges_and_objects` | Missing/unrelated refs, unavailable objects and Git failure exit 2; an equal resolved range reports zero commits and exits 0. No fallback reports an unknown comparison as clean. |
| V-11 / R-10 | `Ci_selects_cumulative_feature_and_master_push_ranges` | Fixture event files select the expected exact SHAs; a later push still detects an earlier unlanded violation. First feature push and multi-commit default-branch push are covered. |
| V-12 / R-11 | `Ci_refuses_missing_manual_base_or_invalid_event` | Manual/event ambiguity, missing history and unexpected default-branch zero base fail visibly; explicit valid manual range executes the same guard. |

Additional witnesses:

- **V-13 / R-12:** `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked` verifies the common rule reaches Code, Review, Debug and Docs; `C1015_SourceLanding_keeps_its_external_evidence_exception` preserves Mutation's stricter contract. Existing budget tests stay enabled. Text pins prove instructions ship, not that an agent will always obey them.
- **V-14 / R-13:** `EvidencePolicyWorkflowTests.Workflow_invokes_guard_with_full_history_and_failure_propagation` pins actual workflow checkout/range inputs and the real script call, forbidding an unconditional success/continue-on-error bypass. Behavioral range selection is exercised by V-11/V-12; this static witness does not prove a hosted Actions run occurred.
- **V-15 / R-14:** rerun the two existing source/landing witnesses named above; ignored generated files continue to admit clean source while tracked changes remain dirty. Retention refusal remains covered by Unit. No new asynchronous delivery path is introduced, so no producer-to-recipient transport battery is needed.
- **V-16:** execute the guard on the actual implementation range after all source/report commits. Preserve resolved base/head and violation/entry counts in the final report. Repeat this cheap read-only command after any later report-only commit; disclose it as the CP-5 rerun. It is not a build/test rerun.

TestDesign must supply distinct positive controls for the directory, file-type/mode, size, commit-range, Git-error, CI-range selection and invocation guards, and review whether separate mode/format and base/head identity controls are needed. Run deliberate mutations only in the later method-scoped SourceLanding Mutation stage. Do not use a compiler failure, zero selected tests or a fixture error as the intended red assertion.

### Initial checkpoint sketch (superseded)

Initial manifest for TestDesign to freeze. All groups use the **Any lane**; host selection follows D-7. `After` is the same S1-S2 group so the isolated build can be reused. The ordinary scope is Unit plus the new integration class and two explicitly named unchanged adjacent smoke methods, not the full landing/checkpoint namespaces.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1015/` | any-unit | `/*/*/*/*[Category=Unit]` | V-13, V-14, R-12, R-13, retention/source-policy regressions | all selected Unit cases; 0 failed; account for every skip under the source-qualification contract | 3500 | 10 |
| CP-2 | S1-S2 | CP-1 | any-evidence-diff | `/*/*/EvidenceDiffGuardTests/*` | V-1..V-12, R-1..R-11 | all 12 named methods; 0 failed/skipped | 12 | 4 |
| CP-3 | S1-S2 | CP-1 | any-land-ignored-smoke | `/*/*/LandingGitTests/C642_IdentityAndStatusScopeSkipsIgnoredListing` | V-15, R-14 | exact method; 0 failed/skipped | 1 | 1 |
| CP-4 | S1-S2 | CP-1 | any-source-ignored-smoke | `/*/*/CheckpointSourceStateTests/clean_and_ignored_outputs_match_head` | V-15, R-14 | exact method; 0 failed/skipped | 1 | 1 |
| CP-5 | S1-S2 | n/a | any-candidate-evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef bb5fa774cd56f85ee6f0b1122c198192427e5ddf -HeadRef HEAD` | V-16 | exit 0; exact resolved range; 0 violations; explicit commit/entry counts | n/a | 1 |

CP-1's floor is conservatively below CARD-0965's recorded 3,910 passed Unit executions, not a promised current roster count. That historical run also had 52 inherited environmental skips. TestDesign must freeze the current applicable roster and qualification handling; an inherited skip is not a new skip waiver or a clean receipt. Do not lower qualification requirements, enlarge this feature into unrelated test repairs, or claim green from listing tests. New affected tests and both smoke methods require nonzero execution and zero skips.

Code/Review use the checkpoint tool with this plan and committed expected SHA, one run per committed slice group, then keep waiting until the exit is not 75. Bootstrap any `dotnet run` that builds the tool through `scripts/build-slot.ps1`; the tool owns its row build slots. Never run unleased after a slot refusal/timeout. Preserve its exact CP lines and structured receipts until inspected/validated. Use the existing safe cleanup for the owned `bin-c1015/` outputs. Do not delete evidence to satisfy this policy.

### Cost and boundaries

Estimated ordinary floor: **17 minutes** (10 + 4 + 1 + 1 + 1), including one isolated test-project build. Final Review repeats the full frozen ordinary selection: another estimated 17 minutes. Estimated implementation/authoring: 45-75 minutes; TestDesign: 20-30 minutes. These are estimates, not measurements from this dispatch. TestDesign supplies the separate method-scoped Mutation floor and total after freezing its guard inventory.

No client, browser, live provider, remote-runner, database, entire `Antiphon.Tests` assembly or Windows-only battery is justified by these changes. Reusing the build avoids three unnecessary integration builds. Actual CI execution is a post-push acceptance observation; local text tests alone cannot attest it.

## Activation and acceptance order

1. **Decision:** caller records the direction-1/one-MiB rule, unchanged legacy inventory, and D-5 cleanup/raw-retention tradeoff on CARD-1015. If raw durable storage plus automatic cleanup is mandatory, return to Plan for that additional scope.
2. **TestDesign:** append the complete verification design, inspection/guard/PC inventory and final counts/cost. Resolve the Unit qualification issue explicitly; then hand off to Code.
3. **Code:** commit/push S1 and S2 on its assigned branch, run the frozen checkpoints at committed source, and report exact CP lines plus guard range. Keep raw outputs ignored and preserve their custody. Report no generated-output commits and no removals of the 108 legacy paths.
4. **Review and publication:** Final Review reruns the full ordinary scope and the complete candidate-history guard at the exact pushed tip. The caller lands the original Code owner through the normal reviewed land operation. Inspect publication and cleanup separately; `LandedWithResidue` must not be relabeled as failed publication or clean removal. Commission post-land Mutation under the established external-evidence rules.
5. **Activate instructions:** the workflow becomes active from the pushed workflow revision. Bundles are embedded in the server assembly, so **restart: server; runner: none**. The caller follows the canonical AppHost restart owner, waits for active lands, updates the canonical checkout and verifies `/api/version` against source HEAD. This Plan task performs no restart or deployment.
6. **Verify loaded policy:** inspect `GET /api/agents/bundles` for the changed basics/Code/Review versions, and the next newly launched delegate's composed bundle headers. Do not assume an already working delegate adopted new instructions. Verify the first applicable Actions run reports the intended base/head and succeeds; a skip or unavailable job is not guard activation.
7. **Acceptance:** record the selected decision, actual cap-test outcomes, fixture counts and intended red controls when Mutation completes, the real range-guard result, and the explicit legacy cleanup decision. The expected immediate result is cessation of new raw-evidence commits, not a smaller existing Git history or proof of a new artifact store.

## Plan-stage validation

Read-only card/API, Git inventory/history and source inspection only. No build or test was run for this documentation-only planning task. Before settlement, check Markdown table shape and `git diff --check`, commit this plan on the assigned task branch, push it, and verify the remote branch SHA. No source, bundle, workflow, test or existing evidence file is changed by this dispatch.

## Owner decision amendments

Historical TestDesign task `f0aad56e-937e-4a3b-8164-5e0c82bf9bd3`, 2026-10-03. The commissioning brief recorded these decisions verbatim; task b6180921's later explicit option-1 decision supersedes the all-108 deletion wording below. Current authority is Revised slices, Verification design and the accepted rule amendment.

- D-1: ACCEPTED. Markdown evidence up to 1 MiB is allowed; generated artifacts are rejected by the git-history guard.
- D-2: CHANGED. Do NOT leave the existing 108 paths (about 17.7 MB). Remove them with a normal DELETION COMMIT on master: no history rewrite, no force-push, no filter-branch. Plan the deletion slice (exact path list derivation and a guard that proves exactly those paths, and nothing else, are removed; the files stay recoverable from history).
- D-5: CHANGED. The operator's words: "for cleanup we should delete whole worktree after card is done, then fine to leave gitignored".

These replace the old D-2/D-5 defaults. D-1, D-3, D-4, D-6 and the runtime routing preference remain. D-5's desired end state is deletion of the whole task worktree, including ignored evidence, after the card is done; preserving cleanup residue is not the desired retention design. Implementing that end state is a **recommended separate follow-up card**, as expressly permitted by this brief. CARD-1015 implements the bounded Git policy and the D-2 deletion only. Raw evidence may remain ignored while a task/card is active. This card adds no raw-artifact durability promise and must not claim that Done already triggers removal.

### Cleanup inspection and follow-up boundary

Read `CardTaskSettlement.SettleClosedCardAsync`, `AgentTaskReplyService.PrepareRemoteAsync`/`MergeBackAsync`, `AgentTaskLandingProtocol.CleanupAsync`, `TaskWorktreeRetirementService.ReleaseAsync`/`TryRetireAsync`/`RemoveRemoteMirrorAsync`, `WorktreeResidueSweepService`, `RemoteWorkspaceService.RemoveMirrorAsync`, `RunnerWorkspaceService.RemoveAsync`, `WorktreeIgnoredContentGate`, `WorktreeCleanupSettings`, `IWorktreeEvidenceRetention`, the Program registration and `LandingEnums`.

- Card closure currently cancels unstarted open tasks and leaves started tasks open with a warning. It does not create deletion authority for all tasks attached to a Done card. Task settlement records/synchronizes published progress; it is not a card-wide cleanup transaction.
- Desktop landing cleanup starts only after publication. `LandCleanupStatus.NotStarted` is the default enum value, not evidence that the directory is absent. Cleanup transitions through Pending to Complete or Refused; Complete requires the returned directory/registration/branch facts. Settled-task retirement additionally requires explicit `NoFurtherWorkspaceUse`, terminal task/settling floor, exact ownership, retrievable reports, containment and workspace-use admission. The residue job is the scheduler; its Execute default is false.
- `WorktreeCleanupSettings.DefaultRetainedIgnored` includes task reports, TRX and checkpoint folders. Program registers `RefusingEvidenceRetention`; nonempty evidence returns `evidence_retention_unavailable`. Merely changing the ignore rule or declaring evidence disposable does not supply card-Done authority, resolve report pointers, release active sessions, or implement recovery.
- Remote retirement calls `RemoteWorkspaceService.RemoveMirrorAsync` after the local attempt and records runner residue separately. The runner's ordinary `RemoveAsync` checks its root, dirty status and (when supplied) published ancestry, then invokes `git worktree remove --force`. Its status query omits ignored files. Local landing cleanup Complete and remote mirror absence are therefore different facts; runner removal is not proof of durable raw-evidence retention. SourceLanding snapshots have their own sealed custody/removal protocol and remain excluded.

The follow-up needs a card-Done-triggered, durable/idempotent cleanup obligation in the card-transition/settlement path; fresh terminal/publication/workspace-use checks via `TaskWorktreeRetirementService` and the residue scheduler; an explicit ignored-evidence disposal policy in `WorktreeIgnoredContentGate`/settings that preserves canonical `AgentReportStore` and real deliverable pointers; and separately retriable remote removal through `RemoteWorkspaceService`, runner contracts and `RunnerWorkspaceService`. It must record local and mirror completion independently and handle offline runners, process ownership, reopen races, partial deletion and restart recovery. Existing `WorktreeCleanupAttempt`/retirement journals should be reused where their authority fits. A new retention-copy service is not required by the operator's disposable-evidence choice.

This is materially larger and more destructive than a Git-history policy: it changes the timing and authority for filesystem deletion across hosts. It needs its own Plan/TestDesign, cleanup failure controls and any new notification's real-queue recipient evidence. Do not implement it by changing a classifier glob or by treating card Done as permission to kill a working session. Caller action: commission **"Delete completed-card task worktrees including ignored evidence"** as the follow-up; this plan-only dispatch does not create or claim a board record. No unanswered owner choice blocks CARD-1015's reduced scope.

### Revised slices and deletion authority

S1 and S2 retain their original purposes. S1 documents the accepted policy and the deferred whole-worktree cleanup end state, replacing the old preserved-residue design language; it must accurately describe today's implementation gap. S2 may add `scripts/lib/evidence-policy.ps1` for the byte-oriented Git/result-validation seam described below. Add the following dedicated slices:

| Slice | Files | Purpose |
|---|---|---|
| S2b | New `scripts/check-evidence-deletion.ps1`; shared evidence-policy library; new `tests/Antiphon.Tests/Scripts/EvidenceDeletionGuardTests.cs` | Read-only exact-deletion validator and real-Git fixture tests; commit/push before S3. |
| S3 | Exactly D(B), the guard-rejected tracked entries at Code's immutable start commit B | One normal deletion-only commit, with exact trailer `Antiphon-Evidence-Deletion: CARD-1015`. Preserve every allowed Markdown entry. Push the task branch and land through the normal reviewed operation onto master. No direct worktree push to master, history rewrite, force-push or `filter-branch`. |

The original classification anchor is derived from these exact output bytes; it is not the S3 deletion list:

```text
git ls-tree -r -z --full-tree --name-only bb5fa774cd56f85ee6f0b1122c198192427e5ddf -- .antiphon/
```

This NUL-delimited Git-order anchor contains **108 paths**, SHA-256 **356edf4a223e53669d4137631e40c0f5f7d19127c2568fdd0608fa4364e5ac9c**, totaling **17,775,541 bytes**. The accepted rule partitions it into **88 rejected / 17,414,103 bytes** and **20 permitted / 361,438 bytes**. Anchor both classifications and original mode/OID identities. All 20 permitted Markdown entries stay. Do not inspect payloads, secrets or private notes to derive the inventory. Files remain recoverable from history.

Let B be Code's recorded full dispatch/start SHA, T(B) its complete tracked tree, D(B) exactly the entries rejected by the accepted D-1/D-3 rule, and K(B) all other entries. Compute D(B) from T(B), not the candidate diff, deletion parent or current worktree. Record count, byte sum and SHA-256 over Git-order original path bytes each followed by NUL. At inspected master this is **289 / 37,541,892 bytes / `4e0a2ec36b9817c7cba9a2d5e05e85ecaa3f5a23b56daeb7469b3fd55b7684ed`**; the 23 permitted root-evidence entries total 559,176 bytes. Later pre-B additions are classified by the same rule without a new scope decision. Missing/changed original anchor identities or unclassifiable metadata fail admission visibly.

Before S3, independently recompute D(B), verify the recorded count/bytes/digest, and compare each selected path's current mode/OID to B. Stage only those literal paths through Git's NUL pathspec input and literal-pathspec mode. Assert the complete staged raw diff is exactly D(B) as D records, with those old identities and no other change. Source/tests/docs belong in earlier slices; subsequent fixes may not be mixed into or rewrite S3. Neither allowed Markdown nor unrelated source may be removed by S3.

`check-evidence-deletion.ps1` accepts Repository, InventoryRef (= B), InventoryPathSha256, InventoryCount, **InventoryBytes** and HeadRef. The normal validation set requires all three caller-recorded aggregate values; parse/type/ref failures are exit 2. A separate read-only **InventoryOnly** parameter set requires Repository/InventoryRef and returns structured inventory metadata (resolved B, ordered delete/keep records with mode/OID/bytes, counts/digests/byte totals and the anchor verdict), without requiring a deletion commit. Neither mode writes a manifest, index, ref or worktree file; the caller captures stdout outside tracked source. Share the same classifier with the history guard. Resolve refs once to full OIDs.

Validate required argument presence and numeric/hex syntax inside the entry's controlled exit-2 path, without interactive PowerShell parameter prompts. Raw tree and diff readers reuse checked Git exit, strict UTF-8/NUL, full-OID, mode and nonnegative blob-size validators; the new tree-header cardinality decision is G-101. A gitlink is rejected by mode without following its target: its byte contribution is zero because it is not a blob, and its stored tree pointer remains recoverable. Missing size for a blob is unknown, never zero. Current real inventories contain only blobs; the fixture vectors cover symlink/gitlink handling explicitly.

Both modes validate all original 108 entries at B: presence, original OID, original mode, and the literal classification signature specified below. Validation mode independently recomputes D(B), then compares the supplied count/bytes/digest to it; it never computes an expected set from candidate deletions. Locate **exactly one** commit with the exact trailer in B..HeadRef, require a single parent, and validate its complete unfiltered no-renames raw diff. Every record must be D; both directional set differences with D(B) are empty; old mode/OID identities match B. Require B ancestry of the deletion parent and deletion ancestry of HeadRef. A complete raw diff containing only those deletions proves S3 left every K(B) entry unchanged; do not add a duplicate keep-tree refusal that masks the missing/extra/old-identity controls.

Run old-mode/OID comparisons only on actual/expected intersections after the two set checks. An intentionally bypassed set guard must not turn the corresponding PC into a dictionary/fixture error. Missing-entry loops likewise leave their separate presence decision authoritative instead of dereferencing an absent record. These details preserve decisive controls without weakening the unmutated validator.

At HeadRef require every D(B) path absent, every permitted root-evidence path from B still present with identical mode/OID, and a fresh complete tree scan yielding zero rejected entries. New permitted reports may exist; source changes from S1/S2 are allowed. The final-tree scan is separate from the B..HeadRef history guard; an equal-ref/empty history result cannot prove a compliant tree. Check every D(B) blob with `cat-file -e` without printing contents. Exit 0 = all proofs succeed; 1 = policy/set/identity mismatch; 2 = unknown/ref/Git/parse failure. Print resolved B/deletion/head SHAs, original-anchor verdict, supplied/recomputed aggregates, kept count, final-tree violations and recoverability. No additional candidate-count refusal may mask the two set differences. The same wrappers/library run in fixtures and CP-6/CP-7.

After reviewed land, repeat CP-6/CP-7 against actual master with the same B and D(B) aggregates and record the landed deletion SHA (landing may rebase S3). A concurrent master's new generated artifact makes history/tree acceptance fail; do not silently widen S3 or relabel an earlier receipt. Report it for a separately reviewed deletion/fix. This proves an ordinary deletion reached master, not a reduction in history size. The original anchor stays reachable. Later qualification commits may exist; the exact deletion must still be discoverable.

## Verification design

This is the finalized TestDesign freeze after task b6180921's explicit option-1 decision, not an execution report. It supersedes earlier V/R sketches, literal-108 deletion authority and costs. Inspected master: `7af83b0c3270006cd98a25a69531f24e37240a4e`; original classification-anchor ancestor: `bb5fa774cd56f85ee6f0b1122c198192427e5ddf`. D-1/D-3/D-4/D-6's fix design is preserved. B will be the fresh Code dispatch commit containing this landed plan and CARD-1005. No production source, tests, fixtures, bundles or workflows changed in this documentation dispatch.

### Inspection

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| Entire `InstructionBundleTests`, `InstructionBundles.ForDelegate`, SourceLanding part of `DelegationReportFormatter.BuildBrief`; current basics/Code/Review bundle bodies | V-19, R-1; real composed Code/Review/Debug/Docs, stricter SourceLanding exception, stage ASCII/caps and command-line budget |
| `Scripts/ScriptHarness`, entire `Application/DelegateScriptRunner`, nearest real-script class `RunCheckpointSourceScriptTests` admission methods; `LandingGitFixture` including process configuration/disposal | V-1..V-18, V-21; argument vectors, fixture-owned Git configuration/remotes, actual CLI results; missing byte/fault fixture specified below |
| `AppHostBrokerSourceGuardTests` including root locator; CI workflow; transitive YamlDotNet reference through Checkpoints project | V-20; inspect actual YAML nodes and executable run body, not comments or a second fixture workflow |
| `LandingGitTests.C642_IdentityAndStatusScopeSkipsIgnoredListing` and its real Git fixture; entire `CheckpointSourceStateTests` including private Git fixture; `GitFixtureCleanup` | V-22, R-2; ignored output does not dirty source, tracked output does; no cleanup authority inferred |
| `WorktreeIgnoredContentClassifierTests`; `LandingRemovalPolicyControlTests.C665_RetentionRefusalPreservesTree`; cleanup/retirement/runner bodies listed in the amendment | Existing Unit regressions only, V-25/R-7. D-5 runtime implementation is excluded; retention double is not production retention proof |
| `CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`, `UsageLibrary`, `CheckpointRoster.CompiledCases`, entire `CheckpointTestScope`/`CheckpointTestBase`, independent `Get-NamespaceCensus` literal | V-25, R-6; compiled census versus independent literal; zero new Checkpoints namespace cases here |
| `TestLaneCategoryGuardTests`, shared `TestClassificationGuardTests`, `TestClassificationMetadata`; linked compile entries; Timeout Windows method bodies, two Windows removal bodies, RemoteScript prerequisite helpers and `DispatchHoldLedgerTests.HoldSentences` | V-25/R-7; categories, six deterministic Linux exclusions, 18 data rows, prerequisites rather than skip waivers |
| CARD-0959 and fetched CARD-1005 verification freezes, testing owner manifest/coverage/Mutation sections, orchestration stage/cleanup/report owners | Named method PCs, receipt qualification, independent census, ordinary/Mutation costs and staged handoff |
| Amendment: re-read ScriptHarness, DelegateScriptRunner, LandingGitFixture and RunCheckpointSourceScriptTests including nested helper bodies; tree metadata for every original/current evidence entry; checkpoint RowRunner command branch and PlanTableImporter non-TUnit branch | V-21/V-24, R-5; anchored real-Git fixtures, complete tree parsing, read-only inventory and Linux shell argument binding. No evidence payloads were read. |

Read-only census: `rg -c '^    \[Test\]'` over `InstructionBundleTests.cs` gives 42 methods; 25 Arguments over five methods give **62 executions**. Four new non-parameterized methods make **66**. Checkpoints `*.cs` (exclude inert `*.cs.txt` fixtures) has **346 Test methods**, 24 Arguments over five methods: **365 executions**. Raw grep including text fixtures incorrectly gives 375/34 and is not the compiled census. No test is counted from a comment/string fixture.

CARD-1005's source census was inspected at `origin/feat/card-task-b288ec96` (`047f3ce3e6e115d1057e4772667f8f291d66dfc1` when fetched). Its freeze base `46e35eded4b6170418e91f6ea04eccbc290cf252` and this start ref have **no diff** in `tests/Antiphon.Tests`, `tests/Shared` or the census library. Thus its independently expanded source inventory applies here: 2,765 Unit methods, 252 argument methods/1,464 rows, plus 18 yielded HoldSentences replacing one placeholder = **3,994 Unit executions**. CARD-1005 adds twelve Unit/Checkpoints methods and changes the literal 365 -> **377**; the fetched implementation has exactly that test/census diff.

**Current census and sequence:** inspected master already contains CARD-1005's twelve tests and independent literal **377**, plus CARD-1006's twelve additional Unit methods. The earlier 365 census describes the old start only. Land this amended plan doc-only first, then commission fresh Code from current master containing it and CARD-1005. Do not merge/rebase an active runner branch. CARD-1015 still adds **32 methods/results**: 18 history Integration + 6 deletion Integration + 4 workflow Unit + 4 bundle Unit; new controls add vectors inside these methods, not results. None belongs in `Antiphon.Tests.Checkpoints`; do not edit `scripts/lib/checkpoint-usage.ps1`. Current Unit census = 3,994 + 12 + 12 = 4,018; with eight new Unit methods = **4,026 selections**, minus six explicit Windows exclusions = **4,020 Linux executions**. CP-1 already includes the census test and existing category/registry tests. Reconcile later source drift read-only at B; do not replace 377 with 365 or derive its literal from the selected roster.

Missing setup to implement in S2/S2b:

- New Integration classes carry `[ParallelLimiter<ProcessSpawnLimit>]`. Use a private/shared `EvidenceGitFixture` under `tests/Antiphon.Tests/Scripts/` (not Checkpoints), fixture-owned local repository/bare remote/temp root, deterministic identity, disabled signing/hooks/credentials and literal `ProcessStartInfo.ArgumentList`. Read-only invocations must not contact a remote server. Await/drain every owned child, bound its lifetime, join a killed child on timeout, and fail teardown if the owned root cannot be removed. Existing ScriptHarness/GitFixtureCleanup do not supply all of these guarantees; do not copy their swallowed-delete or unjoined-timeout behavior.
- Create byte-exact blobs/trees/index records with `hash-object`, `mktree -z`, `commit-tree` or NUL `update-index --index-info`. This supplies newline/tab/non-ASCII/shell-text paths, 120000 symlinks and 160000 gitlinks without Windows symlink privileges or illegal checkout names. The accepted 100644/100755 modes and 1,048,575/1,048,576/1,048,577-byte boundaries are explicit. Do not infer bytes from decoded text. No shell interpolation of a path, no human-quoted `git diff` parser.
- Shared `scripts/lib/evidence-policy.ps1` owns byte-oriented Git execution and checked result parsing plus callable history/deletion entry functions returning the documented exit verdict. Each CLI wrapper only loads it, binds validated inputs, invokes the entry and exits with its verdict. Ordinary tests invoke the actual wrappers. For deterministic nonzero/truncated/malformed Git-output and ref-movement cuts, a fixture child dot-sources that same library and substitutes **only its low-level Git result provider**, forwarding other calls to real Git. This is not a second policy implementation. Counters prove the cut ran. The substitute proves failure handling, not that native Git emits malformed output; real missing/corrupt-object cases exercise the native path too. No production bypass/size override is added. The history checker explicitly refuses a shallow repository rather than certifying an incomplete reachable-commit walk. For parser-guard cuts, downstream fixture replies stay valid/permissive so a second native Git failure cannot hide the removed parser check.
- New Unit `EvidencePolicyWorkflowTests` reads the actual YAML via YamlDotNet, asserts structural trigger/checkout/permission fields, and inspects the real guard step's PowerShell AST. The CI run block should be a small fixed command and explicit exit propagation, so an exact AST/argument contract is reviewable. A fake workflow or string in a comment cannot satisfy it. Its four tests are single-result methods. For AST parsing, use the installed pwsh Parser.ParseInput API in an owned/drained child through the same bounded process helper; never execute the workflow body. Add the process limiter to this Unit class too. No System.Management.Automation package or project-file change is required.
- Deletion fixtures need access to the original immutable Git objects without copying evidence into source or generating 17.7 MB of payloads. Use a fixture-owned **object-only local clone with read-only alternates** of the test checkout (`--shared --no-checkout`), preserving the original anchor commit, and create synthetic fixture commits/refs locally. Build B with the original `.antiphon` subtree plus tiny new artifact/Markdown/boundary entries and an outside-root sentinel. Do not check out original evidence or mutate/delete borrowed objects. All deliberately corruptible blobs are freshly written, uniquely named/content-addressed fixture objects. Fixture disposal removes only its owned root; it never follows an alternate store for deletion. Snapshot refs/HEAD/index/worktree around both wrapper modes. The local object store is a read-only input, not a remote executor or network server.
- `EvidenceDeletionGuardTests` supplies an independent C# D-1 predicate over raw tree metadata, separately authored from the PowerShell classifier, and literal boundary vectors with hand-written delete/keep expectations. Expected D(B), byte sums and NUL hashes come from that oracle, never production InventoryOnly output or the candidate diff. Assert original 108/count/path digest and both literal partitions (88 delete, 20 keep), plus the full verdict signature below. For fixture mutations of original B identities, mutate only fixture trees/index records pointing to local replacement objects; the anchor object stays intact. Fault injection may replace only the existing low-level Git result provider; whole policy/classification helpers are never substituted.

Original classification signature: concatenate every original path in Git tree order as UTF-8 bytes, NUL, ASCII `delete` or `keep`, NUL. Expected SHA-256 is **`f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b`**. Both modes compare this single signature over the classifier's original-anchor results; a shrink/reclassification fails even if candidate count/digest were regenerated consistently. Original path list and partition literals are given in the accepted amendment below. Checking B's original presence/OID/mode is separate from checking the immutable anchor's classifications.

The six V-21 methods cover these additional boundaries without changing the result roster:

- `Verifies_exact_legacy_deletion`: InventoryOnly on the anchor and on B; assert exact independent delete/keep lists and original signature. B includes a newer pre-B artifact, permitted task/c998-style Markdown, uppercase root/extension, permitted `checkpoints.md` leaf, nested/mixed-case checkpoint directory, regular/executable/link/gitlink modes and 1 MiB-1/equal/+1 byte vectors. Modes/illegal checkout names use object/index plumbing. Small permitted reports, the six c998 summaries and unrelated source remain unchanged; clean exact S3 and final tree pass. Also compare S3's full after-tree to its parent minus D(B) as a fixture assertion, not a second production refusal masking G-65..G-69.
- `Rejects_wrong_inventory`: wrong count, digest and byte sum independently; missing/changed original entry at B (presence, OID, mode separately); wrong original classifier signature through a valid low-level size-result cut at anchor enumeration only. Supply all other fields correctly. Call both real parameter sets in ordinary vectors; inventory mode must not need a deletion marker.
- `Rejects_missing_extra_or_non_deletions`: omitted D(B) path, extra permitted Markdown deletion, extra outside-root deletion, added source and modified kept source are independent vectors. For the missing-in-S3 vector, delete the omitted entry in a later unmarked commit so final absence/tree checks pass. For an extra permitted deletion in S3, restore it identically afterward so final preservation passes. Each exact-set guard then has an unmasked control.
- `Rejects_changed_legacy_or_resurrected_paths`: change a non-anchor artifact's OID between B and S3, same-byte-size content for the pinned-B control; separately change its mode only. Restore a deleted oversize Markdown path as small permitted Markdown after S3 to isolate absence from final classification. Independently delete, change OID or change mode of a kept small report after S3. Add a new forbidden file after S3 to isolate the final complete-tree scan (invoke deletion validator alone, not a preceding history guard that would mask it).
- `Refuses_unverifiable_deletion_history`: keep existing ancestry/Git/missing-history vectors; add malformed tree metadata to the shared provider. For the recovery-loop control, remove only a new fixture-owned loose blob after completed inventory sizing and final-tree enumeration, via that low-level observation seam; the cut still runs if the recovery loop is bypassed. Native `cat-file -e` then witnesses the missing object. Fault-hit checks precede verdict assertions; no payload/private-note reads.
- `Rejects_ambiguous_or_merge_deletion`: existing marker uniqueness/exact-trailer/single-parent vectors remain, with D(B) computed under the rule and allowed files retained. All six methods use the same public wrappers and shared byte/result parser; the declared fault cuts prove refusal behavior, not that native Git emits malformed metadata.

### Delivery inventory

**No new or changed asynchronous application delivery path remains in scope.** S1 changes instruction text, S2 runs a synchronous read-only Git checker in CLI/CI, S2b validates deletion and S3 changes a Git tree. Producers/destinations are the local CLI process and its caller/CI job; their identity is resolved base/head plus the inspected commit, not an application queue ID. Persistence is committed source plus raw checkpoint/CI receipts; rerun against the same resolved commits recovers an interrupted invocation. Observable completion is the process exit and complete result/counts, not a claim that a session received input.

No SessionMessageQueueService, notification, settlement or card-Done cleanup implementation is changed. Busy/already-eligible recipient and crash/enqueue handoff tests are therefore excluded, not replaced by a queue insert or ack. If Code adds an async cleanup/notification path, this freeze no longer covers it: return to Plan/TestDesign for durable producer-to-recipient recovery tests and matching complete UserPrompt transcript evidence. Bundle text assertions prove composed instructions contain the rule; they do not prove agent obedience or input delivery. YAML tests prove wiring, not a hosted Actions execution; caller acceptance separately inspects the actual SHA-bound Actions result. No raw-evidence storage or post-Done deletion receipt is claimed.

### Proves it works now

Every named new method is non-parameterized, one TUnit result. Internal vectors below are mandatory assertions, not extra executions. Prefix `EvidenceDiffGuardTests.` applies to V-1..V-18; all run the actual PowerShell entry points except the declared low-level fault substitutions. Stable assertion labels used by PCs must exist on the decisive assertion in that exact method; assert outcomes before diagnostic detail so mutation reds are attributable.

| ID | Behavior / exact method | Layer and expected result |
|---|---|---|
| V-1 | `EvidenceDiffGuardTests.Rejects_checkpoint_paths` | Real Git/CLI: tiny `.md` under root/nested `checkpoints`, `c1015-checkpoints`, mixed-case components fails 1, introducing SHA and escaped path present. A file named `checkpoints.md` outside such a directory is allowed. |
| V-2 | `EvidenceDiffGuardTests.Rejects_non_markdown_outputs` | Tiny `.trx`, `.trx.gz`, `.json`, `.log`, `.zip`, `.tar.gz`, extensionless and `.md.exe` each fail 1 outside checkpoint directories. Uppercase `.MD` is accepted. |
| V-3 | `EvidenceDiffGuardTests.Rejects_non_regular_modes` | 120000 symlink (small `.md` target string) and 160000 gitlink fail 1; 100644 and 100755 Markdown pass. No link target is followed. |
| V-4 | `EvidenceDiffGuardTests.Enforces_one_mib_blob_limit` | 1 MiB-1 and exactly 1 MiB pass; 1 MiB+1 fails 1. A multibyte UTF-8 blob exceeding the byte cap but below the character cap fails; huge non-Markdown also fails without depending on size. |
| V-5 | `EvidenceDiffGuardTests.Allows_small_markdown_and_other_source` | Empty/small report/provenance `.md` pass; large source outside root `.antiphon/`, `.antiphon-other/` and nested `x/.antiphon/` are outside D-3's guard. Scope is literal root component, not a substring anywhere. |
| V-6 | `EvidenceDiffGuardTests.Grandfathers_unchanged_legacy_and_allows_deletion` | Base contains forbidden legacy files; unchanged entries and pure deletions pass 0. Equal base/head gives explicit zero-commit/zero-entry success; unrelated normal commit gives nonzero commits/zero inspected entries. |
| V-7 | `EvidenceDiffGuardTests.Rechecks_modified_and_renamed_legacy_paths` | Change an existing forbidden blob; rename a small raw artifact; copy/rename source into `.antiphon/x.json`; change a Markdown file's mode: each fails. Rename into a permitted small `.md` passes if all introduced records comply. |
| V-8 | `EvidenceDiffGuardTests.Checks_intermediate_commits_even_when_tip_is_clean` | Introduce forbidden blob then delete it before head; fail 1 and name the introducing commit although net diff is clean. |
| V-9 | `EvidenceDiffGuardTests.Checks_merge_side_history` | Side branch adds then deletes forbidden blob before merge; fail on side SHA even with clean merge result. Separately, a merge resolution introduces a forbidden path absent from both parents; fail on the merge SHA's first-parent diff. A third vector merges an unrelated root lineage that added then deleted a forbidden blob: base remains an ancestor of head, and the introduced root must be diffed against the empty tree and rejected. |
| V-10 | `EvidenceDiffGuardTests.Reads_pinned_git_objects_not_index_or_worktree` | Committed oversize Markdown with staged/working small bytes fails; committed small Markdown with staged/untracked oversize bytes passes. Assert the exact committed object size/identity. |
| V-11 | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | Root `.ANTIPHON`, directory case, spaces, tabs, LF, non-ASCII and shell-text path vectors cannot hide a violation. Literal sentinel expression never executes; decoded reported path matches the object path and the output remains safely escaped. Payload sentinel never appears in stdout/stderr. |
| V-12 | `EvidenceDiffGuardTests.Refuses_unverifiable_ranges_and_objects` | Missing base, missing head, blob/tag-to-blob ref, unrelated histories, and removed loose object fail 2; valid annotated commit tag and equal range pass. Assert no clean verdict/count certificate on error. |
| V-13 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | At ref resolution, ancestry, rev-list, raw diff and cat-file cuts, nonzero exit is 2 even with plausible stdout; truncated/malformed IDs/modes/status/NUL records, invalid UTF-8 path bytes and invalid size are 2. Strict UTF-8 decoding refuses undecodable names rather than substituting replacement characters. Each cut has a hit counter and a neighboring valid result. |
| V-14 | `EvidenceDiffGuardTests.Resolves_refs_once_and_stays_read_only` | Move mutable base/head refs after initial resolution through the low-level observation cut; all later calls and reported range use captured OIDs. Independently verify unchanged refs, index/worktree bytes and HEAD for unhooked success/failure calls; spy disallows fetch/stage/config/checkout/delete. |
| V-15 | `EvidenceDiffGuardTests.Ci_selects_cumulative_feature_push_range` | Local origin/master fixture: first feature push (zero before allowed), later push with bad earlier unlanded commit, and default-branch advance choose merge-base(default,event head)..event head; do not use event before. |
| V-16 | `EvidenceDiffGuardTests.Ci_selects_complete_default_branch_push_range` | Default push before..event head contains multiple commits and an intermediate violation. Checkout HEAD is deliberately different; event after controls head. Assert exact range, not only rejection. |
| V-17 | `EvidenceDiffGuardTests.Ci_requires_valid_manual_range` | Valid explicit manual base/head use same policy; missing/blank/unresolvable base fails 2, no guessed default. Head may be an explicit selected commit/tag-to-commit. |
| V-18 | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | Unknown event, malformed JSON, missing/wrong-typed required fields, missing remote default ref, default push zero before, shallow/missing history and unrelated inputs fail 2. Only actual deleted=true branch event is explicitly excluded; string `"false"` cannot skip enforcement. |
| V-19 | `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked`; `InstructionBundleTests.C1015_Code_keeps_range_guard_and_exact_source`; `InstructionBundleTests.C1015_Review_remains_read_only_and_checks_history`; `InstructionBundleTests.C1015_SourceLanding_keeps_its_external_evidence_exception` | Real composition for Code/Review/Debug/Docs, common rule, full task-base guard, unchanged CP lines/tested SHA, Review no writes, external-only SourceLanding exception. Existing cap/budget/ASCII tests run. |
| V-20 | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event`; `EvidencePolicyWorkflowTests.Workflow_pins_full_history_and_event_head`; `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure`; `EvidencePolicyWorkflowTests.Workflow_uses_data_arguments_and_no_write_permissions` | Actual YAML/AST: master + feat/** push and manual triggers, no narrowing paths/condition, full checkout at event head, fixed script call, failed exit propagates, env-backed event/manual inputs and contents: read. |
| V-21 | `EvidenceDeletionGuardTests.Verifies_exact_legacy_deletion`; `EvidenceDeletionGuardTests.Rejects_wrong_inventory`; `EvidenceDeletionGuardTests.Rejects_ambiguous_or_merge_deletion`; `EvidenceDeletionGuardTests.Rejects_missing_extra_or_non_deletions`; `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths`; `EvidenceDeletionGuardTests.Refuses_unverifiable_deletion_history` | Real Git/CLI and declared provider cuts: both inventory and validation modes use the accepted rule on B; independent oracle and literal 108/88/20 anchor agree. Exact D(B) deletion succeeds; allowed Markdown/source survive S3, allowed root evidence stays identical at head, final tree has no rejects and historical blobs remain retrievable. All invalid vectors described above fail 1/2 at their named assertions. Six results, no additional parameterization. |
| V-22 | `LandingGitTests.C642_IdentityAndStatusScopeSkipsIgnoredListing`; `CheckpointSourceStateTests.clean_and_ignored_outputs_match_head` | Existing real-Git smoke: ignored content has stable clean identity, tracked output edits count dirty; no production changes to these seams. |
| V-23 | CP-6 actual candidate range | Real script returns 0 on actual Code B..committed HEAD with resolved SHAs, commit/entry counts, zero violations and no altered source. Rerun after subsequent report-only commits. Do not use the original anchor as this history base. |
| V-24 | CP-7 actual S3 deletion | Real validator proves D(B) equals the complete S3 deletion set, no other S3 changes, selected paths absent at head, permitted root evidence identical, every surviving root entry passes D-1 and deleted objects recoverable. At inspected master: 289 deleted, 23 kept; original classifications 88/20. Code reports its computed aggregates, not a copied 289 literal. Caller repeats on actual landed master. |
| V-25 | CP-1 complete eligible Unit filter | All selected results executed with zero failed/skipped, including changed bundle/workflow tests, existing source/retention/classification contracts and namespace census = 377 after CARD-1005. |

### Guards the regression

- R-1: V-19/V-20 keep the common evidence rule on all four roles, preserve Review/SourceLanding authority and ship an enforceable CI invocation. Existing bundle caps/budgets remain unchanged in strength.
- R-2: V-10/V-14/V-22 keep ignored output out of clean-source identity while modified tracked output remains dirty; exact Git objects decide the guard. Any write made by a validator fails the before/after oracle.
- R-3: V-1..V-9/V-11 prevent extension, case, checkpoint-directory, mode, byte-size, legacy, rename and intermediate/merge-history bypasses. Every rejection fixture has otherwise valid inputs and an adjacent permitted fixture.
- R-4: V-12/V-13/V-15..V-18 fail unknown comparisons closed, distinguish explicit empty from failed enumeration, and select the intended cumulative range rather than a convenient HEAD/net diff.
- R-5: V-21/V-24 verify the rule-selected exact deletion and preservation. Independently wrong count, path digest and byte sum fail; original presence/OID/mode and full classification-signature faults fail separately. The literal original anchor prevents the independent rule oracle from silently agreeing on a shrunk scope. Newer pre-B artifact inclusion prevents a literal-108 fallback. Zero/two markers and a marked merge fail. Omission, extra permitted/outside-root deletion, addition and modification each fail with the unmasking fixtures above. Modified old OID/mode, substituted deletion-parent inventory, resurrected permitted version of a deleted path, missing/edited/retyped kept file, new final-tree artifact and historical-blob loss each have their own verdict. To isolate B-to-parent ancestry, head merges B's lineage with a separately rooted exact-deletion lineage; B-to-head is valid but B-to-deletion-parent is not. Out-of-head enumeration injects a valid marked sibling commit and requires the separate containment check to reject it. Both sides retain the original anchor objects. No count-only equality, shared-production expected oracle, filtered diff, or empty history check can establish deletion/tree compliance.
- R-6: `CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases` compares the independently maintained literal with expanded compiled cases; 377 is inherited from CARD-1005, never authored by this card. Adding a test to that namespace would invalidate the freeze and require a separately reconciled census change.
- R-7: complete eligible Unit regression coverage stays in ordinary scope. Six Windows-only methods are excluded explicitly on Linux; historical 52 skipped results are not a waiver. Require bash, jq, pwsh and working links before the run; unexpected skips or inherited failures remain failed qualification and need targeted baseline confirmation.

### Guard inventory

The inventory covers every safety-critical invariant added/relied on by the changed policy, CLI/CI wiring, instruction text and exact deletion. Independent decisions are split even when one method exercises several. Broad existing Unit regressions do not make unrelated, unchanged application guards part of this card's PC scope. The unchanged cleanup implementation is explicitly excluded above; it is not an untested CARD-1015 guard.

| Guard | Plan reference + safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-3; Root .antiphon matching is case-insensitive | PC-1 |
| G-2 | D-3; Inspect every descendant directory component | PC-2 |
| G-3 | D-3; Checkpoint substring covers cNNN-checkpoints | PC-3 |
| G-4 | D-3; Checkpoint directory comparison is case-insensitive | PC-4 |
| G-5 | D-1; Only the final .md extension is permitted | PC-5 |
| G-6 | D-1; Only regular modes 100644/100755 are permitted | PC-6 |
| G-7 | D-1; Blobs above 1048576 bytes are refused | PC-7 |
| G-8 | D-1/D-3; Size is committed bytes, not decoded characters | PC-8 |
| G-9 | D-3; Unchanged legacy entries are not re-admitted as new | PC-9 |
| G-10 | D-3; Deletion records require no destination blob | PC-10 |
| G-11 | D-3; Modified tracked legacy blobs are inspected | PC-11 |
| G-12 | D-3; Rename/copy destinations receive no legacy exemption | PC-12 |
| G-13 | D-3; Every introduced commit is inspected, including later-deleted payloads | PC-13 |
| G-14 | D-3; Reachability enumeration includes non-first-parent side commits | PC-14 |
| G-15 | D-3; A merge commit itself is diffed against its first parent | PC-15 |
| G-16 | D-3; The raw new OID selects the size/object, not current file bytes | PC-16 |
| G-17 | D-3; Raw paths are NUL-delimited literal records | PC-17 |
| G-18 | D-3; Git path text cannot execute shell expressions | PC-18 |
| G-19 | D-3; Control characters are escaped in diagnostics | PC-19 |
| G-20 | D-1/D-3; Diagnostics never print payload content | PC-20 |
| G-21 | D-3; Base must resolve to a commit | PC-21 |
| G-22 | D-3; Head must resolve to a commit | PC-22 |
| G-23 | D-3; Base must be an ancestor of head | PC-23 |
| G-24 | D-3/S2b; All Git calls pass through one checked nonzero-exit gate | PC-24 |
| G-25 | D-3; Raw records require the exact header/path field cardinality | PC-25 |
| G-26 | D-3; Missing/invalid blob-size results are unknown, not zero bytes | PC-26 |
| G-27 | D-3; Resolved refs are pinned for the complete invocation | PC-27 |
| G-28 | D-3; History checker has no write side effects | PC-28 |
| G-29 | D-4; Feature pushes recheck merge-base(default,event-head) through head | PC-29 |
| G-30 | D-4; Default-branch pushes check every commit since event before | PC-30 |
| G-31 | D-4; Event after is the inspected head, independent of checkout HEAD | PC-31 |
| G-32 | D-4; Manual dispatch requires an explicit base | PC-32 |
| G-33 | D-4; Unknown event kinds fail closed | PC-33 |
| G-34 | D-4; Event JSON requires correctly typed mandatory fields | PC-34 |
| G-35 | D-4; Missing remote default-branch object never falls back to event before | PC-35 |
| G-36 | D-4; Zero before on a default-branch push is refused | PC-36 |
| G-37 | D-3/D-4; Truncated/shallow history cannot be certified complete | PC-37 |
| G-38 | D-4; Only boolean deleted=true authorizes branch-deletion exclusion | PC-38 |
| G-39 | D-4; CI triggers cover default, feature and manual entry | PC-39 |
| G-40 | D-4; CI has no path/conditional filter hiding a candidate range | PC-40 |
| G-41 | D-4; Checkout supplies full history | PC-41 |
| G-42 | D-4; Checkout and guard identify the event commit | PC-42 |
| G-43 | D-4; CI invokes the real checker with the required arguments | PC-43 |
| G-44 | D-4; Guard nonzero process status propagates to CI | PC-44 |
| G-45 | D-4; Workflow cannot waive a failing guard | PC-45 |
| G-46 | D-4; Event/manual fields enter as data, not interpolated shell source | PC-46 |
| G-47 | D-4; Workflow Git access is read-only | PC-47 |
| G-48 | D-1/S1; Common contract excludes generated evidence from commit instructions | PC-48 |
| G-49 | D-1/S1; Optional committed reports retain the one-MiB cap | PC-49 |
| G-50 | D-4/S1; Code is told to inspect the complete task range | PC-50 |
| G-51 | D-6/S1; Code preserves actual tested source identity | PC-51 |
| G-52 | D-4/S1; Review stays read-only | PC-52 |
| G-53 | D-4/S1; Review audits the entire candidate evidence history | PC-53 |
| G-54 | D-6/S1; SourceLanding evidence stays outside its snapshot | PC-54 |
| G-55 | D-6/S1; SourceLanding exception still forbids commit/push | PC-55 |
| G-56 | S1; Edited stage text still fits its 2500-character transport cap | PC-56 |
| G-57 | S1; Edited stage bundles remain ASCII-safe | PC-57 |
| G-58 | S1; The default full composition stays within command-line budget | PC-58 |
| G-59 | D-2/S2b; Caller-recorded D(B) count is checked against rule-selected B metadata | PC-59 |
| G-60 | D-2/S2b; Caller-recorded D(B) NUL-list digest is independently checked | PC-60 |
| G-61 | D-2/S2b; Exactly one exact deletion trailer is required | PC-61 |
| G-62 | D-2/S2b; Deletion commit is an ordinary single-parent commit | PC-62 |
| G-63 | D-2/S2b; Pinned B must precede the deletion parent | PC-63 |
| G-64 | D-2/S2b; Deletion must belong to the selected head history | PC-64 |
| G-65 | D-2/S2b; Every change in S3 is a deletion | PC-65 |
| G-66 | D-2/S2b; No D(B) path may be omitted from S3 | PC-66 |
| G-67 | D-2/S2b; No extra path may be deleted | PC-67 |
| G-68 | D-2/S2b; Deleted blobs must equal their old objects at B | PC-68 |
| G-69 | D-2/S2b; Deleted modes must equal their old modes at B | PC-69 |
| G-70 | D-2/S2b; D(B) paths remain absent at selected head even if a replacement would pass D-1 | PC-70 |
| G-71 | D-2/S2b; Every deleted blob is still retrievable from history | PC-71 |
| G-72 | D-2/S2b; Deletion validator itself is read-only | PC-72 |
| G-73 | D-3; Guard scope is the exact root path component | PC-73 |
| G-74 | D-3; Checkpoint matching applies to directories, not the filename | PC-74 |
| G-75 | D-3; A truncated final NUL record is not accepted as complete | PC-75 |
| G-76 | D-3; Raw change status is validated | PC-76 |
| G-77 | D-3; Raw object IDs have the full validated object-id shape | PC-77 |
| G-78 | D-3; Raw file-mode fields are syntactically validated | PC-78 |
| G-79 | D-4; CI still runs on default-branch pushes | PC-79 |
| G-80 | D-4; CI still offers explicit manual dispatch | PC-80 |
| G-81 | D-4; CI passes the event JSON path to the checker | PC-81 |
| G-82 | D-4; CI passes the event kind to the checker | PC-82 |
| G-83 | D-4; CI passes the explicit manual base through to the checker | PC-83 |
| G-84 | D-1/S1; Common instructions permit only individual report-path commits | PC-84 |
| G-85 | D-1/S1; Common Markdown allowance excludes checkpoint directories | PC-85 |
| G-86 | D-6/S1; Checkpoint report lines remain verbatim | PC-86 |
| G-87 | D-2/S2b; Deletion marker is an exact complete trailer, not a substring | PC-87 |
| G-88 | D-3; Raw path bytes must decode losslessly, never through UTF-8 replacement fallback | PC-88 |
| G-89 | D-3; Newly reachable root commits introduced by a merge are diffed against the empty tree | PC-89 |
| G-90 | D-1/D-2/S2b; Inventory classifies every scoped B entry by the shared rule, including newer pre-B entries | PC-90 |
| G-91 | D-2/S2b; Caller-recorded D(B) byte sum is independently checked | PC-91 |
| G-92 | D-2/S2b; Validation inventory remains pinned to B, never the deletion parent/head | PC-92 |
| G-93 | D-2/S2b; Every original anchor path is present at B | PC-93 |
| G-94 | D-2/S2b; Original anchor OIDs at B are unchanged | PC-94 |
| G-95 | D-2/S2b; Original anchor modes at B are unchanged | PC-95 |
| G-96 | D-1/D-2/S2b; Original classifications match the independent literal signature (108 entries, 88 delete / 20 keep) | PC-96 |
| G-97 | D-1/D-2/S2b; Complete final tracked tree contains no rule-rejected root evidence | PC-97 |
| G-98 | D-2/S2b; Every permitted root-evidence entry at B remains present at head | PC-98 |
| G-99 | D-2/S2b; Permitted root-evidence OIDs stay unchanged through head | PC-99 |
| G-100 | D-2/S2b; Permitted root-evidence modes stay unchanged through head | PC-100 |
| G-101 | D-2/S2b; Tree metadata records have exact header/path cardinality before classification | PC-101 |

### Positive controls

Each row means: **break its same-numbered G-n using the stated syntactically valid/compiling defect, run only the named exact method, require red at the stated assertion, restore exact bytes, rebuild as needed, and require that method green**. Assertions whose shorthand label is `x` below use the literal message `c1015-x`. New methods must assert that label on the actual outcome; never mutate the oracle or return a canned failing value. Existing bundle methods retain their current assertions.

Mutation runs these cycles only after reviewed land, on the SourceLanding snapshot with inherited local children and external evidence. Code runs ordinary V/R; Review judges the pending PC design before land. A parse/compiler/fixture error, timeout before the assertion, or zero selected tests is not red. All CLI verdict assertions are made after successful fixture setup and fault-hit checks; the deliberate native missing-blob case removes only its own isolated loose object. Each PC alters one predicate/operation, not multiple guards. Do not combine mutations sharing a file or method. No plan/evidence commit is made from the snapshot.

Method filter = `/*/*/ClassName/ExactTestMethod` for the class-qualified method below. The existing six-argument bundle-cap method uses its exact method-name prefix plus `*` to include TUnit's argument suffixes, with MinExecuted=6 and the named Code/Review argument inspected; all other PC filters are exact single-result names, MinExecuted=1. These are still method-scoped, never whole-class controls.

| PC / guard | Compiling defect | Exact method expected red | Decisive assertion |
|---|---|---|---|
| PC-1 / G-1 | make only the root comparison ordinal/case-sensitive | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | root-case: ExitCode == 1 |
| PC-2 / G-2 | inspect only the first directory below .antiphon | `EvidenceDiffGuardTests.Rejects_checkpoint_paths` | nested-checkpoint: ExitCode == 1 |
| PC-3 / G-3 | replace component Contains(checkpoints) with exact equality | `EvidenceDiffGuardTests.Rejects_checkpoint_paths` | prefixed-checkpoint: ExitCode == 1 |
| PC-4 / G-4 | use ordinal case-sensitive checkpoint comparison | `EvidenceDiffGuardTests.Rejects_checkpoint_paths` | checkpoint-case: ExitCode == 1 |
| PC-5 / G-5 | replace the extension refusal predicate with false | `EvidenceDiffGuardTests.Rejects_non_markdown_outputs` | format: ExitCode == 1 for tiny .trx.gz |
| PC-6 / G-6 | add 120000 and 160000 to the permitted mode set | `EvidenceDiffGuardTests.Rejects_non_regular_modes` | mode: ExitCode == 1 for the symlink; separately assert gitlink vector in ordinary run |
| PC-7 / G-7 | raise the constant from 1048576 to 1048577 | `EvidenceDiffGuardTests.Enforces_one_mib_blob_limit` | oversize: ExitCode == 1 at 1048577 |
| PC-8 / G-8 | use decoded UTF-8 string Length for the blob size comparison | `EvidenceDiffGuardTests.Enforces_one_mib_blob_limit` | utf8-bytes: ExitCode == 1 for multibyte oversize blob |
| PC-9 / G-9 | enumerate all head-tree paths instead of changed destination records | `EvidenceDiffGuardTests.Grandfathers_unchanged_legacy_and_allows_deletion` | legacy-unchanged: ExitCode == 0 |
| PC-10 / G-10 | remove the D-record bypass and validate its old forbidden path as a destination | `EvidenceDiffGuardTests.Grandfathers_unchanged_legacy_and_allows_deletion` | legacy-delete: ExitCode == 0 |
| PC-11 / G-11 | skip M raw records | `EvidenceDiffGuardTests.Rechecks_modified_and_renamed_legacy_paths` | legacy-modified: ExitCode == 1 |
| PC-12 / G-12 | exempt a new destination when its new OID exists anywhere in the comparison base | `EvidenceDiffGuardTests.Rechecks_modified_and_renamed_legacy_paths` | rename-destination: ExitCode == 1 |
| PC-13 / G-13 | replace the commit loop with one net base-to-head diff | `EvidenceDiffGuardTests.Checks_intermediate_commits_even_when_tip_is_clean` | intermediate: ExitCode == 1 |
| PC-14 / G-14 | add --first-parent to rev-list | `EvidenceDiffGuardTests.Checks_merge_side_history` | side-history: ExitCode == 1 |
| PC-15 / G-15 | skip commits with more than one parent | `EvidenceDiffGuardTests.Checks_merge_side_history` | merge-resolution: ExitCode == 1 |
| PC-16 / G-16 | replace cat-file size with current worktree file Length | `EvidenceDiffGuardTests.Reads_pinned_git_objects_not_index_or_worktree` | committed-object: ExitCode == 1 with staged/working small bytes |
| PC-17 / G-17 | split raw path records on LF and silently drop unmatched fragments | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | literal-newline: ExitCode == 1 with the full escaped path |
| PC-18 / G-18 | expand a captured path through PowerShell ExpandString before using it; fixture path contains only a fixture-owned canary expression | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | no-path-execution: canary file absent, checked before verdict details |
| PC-19 / G-19 | write the raw path instead of its JSON/control-escaped representation | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | escaped-path: serialized diagnostic contains escaped LF, no injected physical diagnostic line |
| PC-20 / G-20 | append decoded cat-file blob content to a violation diagnostic | `EvidenceDiffGuardTests.Handles_literal_paths_and_case_variants` | no-payload: output excludes fixture payload sentinel |
| PC-21 / G-21 | on failed base resolution substitute head and continue | `EvidenceDiffGuardTests.Refuses_unverifiable_ranges_and_objects` | base-unresolved: ExitCode == 2 |
| PC-22 / G-22 | on failed head resolution substitute base and continue | `EvidenceDiffGuardTests.Refuses_unverifiable_ranges_and_objects` | head-unresolved: ExitCode == 2 |
| PC-23 / G-23 | ignore ancestry exit 1 and continue with otherwise clean unrelated histories | `EvidenceDiffGuardTests.Refuses_unverifiable_ranges_and_objects` | unrelated: ExitCode == 2 |
| PC-24 / G-24 | ignore ExitCode in the shared checked-result helper; rev-list cut returns plausible empty stdout plus exit 1 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | git-exit: ExitCode == 2 and fault hit == 1 |
| PC-25 / G-25 | ignore an extra raw header token and accept the remaining valid fields | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-fields: ExitCode == 2 for an extra header token |
| PC-26 / G-26 | coerce a successful-but-empty/non-numeric cat-file size reply to zero | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | invalid-size: ExitCode == 2 |
| PC-27 / G-27 | pass original mutable HeadRef to the commit walk after the ref-movement cut | `EvidenceDiffGuardTests.Resolves_refs_once_and_stays_read_only` | pinned-head: reported/inspected head equals the first resolved OID |
| PC-28 / G-28 | stage the fixture changed sentinel through git add before returning | `EvidenceDiffGuardTests.Resolves_refs_once_and_stays_read_only` | read-only: index bytes equal the independent before snapshot |
| PC-29 / G-29 | use event before as the feature base | `EvidenceDiffGuardTests.Ci_selects_cumulative_feature_push_range` | feature-cumulative: ExitCode == 1 for earlier unlanded bad commit |
| PC-30 / G-30 | use event head parent as the default push base | `EvidenceDiffGuardTests.Ci_selects_complete_default_branch_push_range` | default-whole-push: ExitCode == 1 for earlier bad commit |
| PC-31 / G-31 | resolve literal HEAD instead of event after | `EvidenceDiffGuardTests.Ci_selects_complete_default_branch_push_range` | event-head: resolved head equals fixture event after |
| PC-32 / G-32 | default a missing manual base to selected head | `EvidenceDiffGuardTests.Ci_requires_valid_manual_range` | manual-base-required: ExitCode == 2 |
| PC-33 / G-33 | treat unknown event as an empty successful range | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | unknown-event: ExitCode == 2 |
| PC-34 / G-34 | convert a missing/array after field to current HEAD instead of rejecting it | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | event-shape: ExitCode == 2 |
| PC-35 / G-35 | on absent origin/default resolve event before instead | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | remote-base-missing: ExitCode == 2 |
| PC-36 / G-36 | replace zero before with event head on the default branch | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | default-zero: ExitCode == 2 |
| PC-37 / G-37 | remove the shallow-history refusal while all referenced endpoints remain present in a deliberately depth-truncated fixture | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | shallow-history: ExitCode == 2 |
| PC-38 / G-38 | cast the deleted field to bool before validating its JSON type | `EvidenceDiffGuardTests.Ci_refuses_invalid_events_and_missing_history` | deleted-type: ExitCode == 2 for string false |
| PC-39 / G-39 | remove feat/** from push branches in the actual workflow | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event` | trigger-feature: branch patterns contain feat/** |
| PC-40 / G-40 | add paths: [docs/**] under the push event | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event` | no-path-filter: push paths/paths-ignore absent |
| PC-41 / G-41 | change actual checkout fetch-depth from 0 to 1 | `EvidencePolicyWorkflowTests.Workflow_pins_full_history_and_event_head` | full-history: fetch-depth == 0 |
| PC-42 / G-42 | replace checkout ref expression with master | `EvidencePolicyWorkflowTests.Workflow_pins_full_history_and_event_head` | checkout-head: ref equals expected event/selected-head expression |
| PC-43 / G-43 | replace executable script path with a valid Write-Host no-op | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | real-guard-call: AST invokes scripts/check-evidence-diff.ps1 with event path/name inputs |
| PC-44 / G-44 | replace the explicit exit $LASTEXITCODE statement with exit 0 | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | exit-propagation: exit AST uses LASTEXITCODE |
| PC-45 / G-45 | set continue-on-error: true on the guard step | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | no-error-waiver: neither job nor step enables continue-on-error |
| PC-46 / G-46 | inline a github.event field expression in the run block instead of its environment variable | `EvidencePolicyWorkflowTests.Workflow_uses_data_arguments_and_no_write_permissions` | no-event-shell: run AST/source contains no GitHub expression interpolation |
| PC-47 / G-47 | change contents permission from read to write | `EvidencePolicyWorkflowTests.Workflow_uses_data_arguments_and_no_write_permissions` | read-permission: contents == read |
| PC-48 / G-48 | delete the generated-evidence gitignored sentence from delegate-basics | `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked` | common-ignored: all four compositions contain the ignored-evidence rule |
| PC-49 / G-49 | change the common 1048576-byte Markdown allowance to unlimited | `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked` | common-cap: all four compositions contain the 1048576-byte cap |
| PC-50 / G-50 | remove the full task-base-through-head guard clause from stage-code | `InstructionBundleTests.C1015_Code_keeps_range_guard_and_exact_source` | code-range: stage Code and its composition name check-evidence-diff.ps1 and full task range |
| PC-51 / G-51 | replace only actual tested SHA instruction with permission to relabel receipts at report HEAD | `InstructionBundleTests.C1015_Code_keeps_range_guard_and_exact_source` | code-source: stage Code contains actual tested SHA requirement |
| PC-52 / G-52 | replace Read-only/Do not fix anything with permission to commit reports in stage-review | `InstructionBundleTests.C1015_Review_remains_read_only_and_checks_history` | review-read-only: composed Review contains both prohibitions |
| PC-53 / G-53 | delete the new full-range evidence guard instruction from stage-review | `InstructionBundleTests.C1015_Review_remains_read_only_and_checks_history` | review-range: stage Review names checker and complete candidate range |
| PC-54 / G-54 | remove the external evidence-root requirement from delegate-basics SourceLanding exception | `InstructionBundleTests.C1015_SourceLanding_keeps_its_external_evidence_exception` | source-external: composed Mutation contains assigned external evidence root requirement |
| PC-55 / G-55 | replace never commit/push in the common SourceLanding exception with may commit/push | `InstructionBundleTests.C1015_SourceLanding_keeps_its_external_evidence_exception` | source-no-commit: exception text within composed basics contains never commit/push |
| PC-56 / G-56 | append ASCII to stage-code until its normalized trimmed text is 2501 characters | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` | existing stage Length.ShouldBeLessThanOrEqualTo(2500), Code argument result |
| PC-57 / G-57 | replace one ASCII character in stage-review with U+00E9 without increasing length | `InstructionBundleTests.each_stage_bundle_is_ascii_and_under_the_size_cap` | existing character code ShouldBeLessThan(128), Review argument result |
| PC-58 / G-58 | append enough ASCII to delegate-basics to exceed the existing configured composition budget | `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` | existing composed.Text.Length.ShouldBeLessThan(budget) |
| PC-59 / G-59 | omit the InventoryCount comparison | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | inventory-count: ExitCode == 1 with correct digest but wrong count |
| PC-60 / G-60 | omit the InventoryPathSha256 comparison | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | inventory-digest: ExitCode == 1 with correct count but wrong digest |
| PC-61 / G-61 | accept the first matching marker when two exist | `EvidenceDeletionGuardTests.Rejects_ambiguous_or_merge_deletion` | deletion-unique: ExitCode == 1 with two candidates |
| PC-62 / G-62 | accept a marked merge and use its first parent | `EvidenceDeletionGuardTests.Rejects_ambiguous_or_merge_deletion` | deletion-parent: ExitCode == 1 for a marked merge with otherwise exact deletions |
| PC-63 / G-63 | skip inventory-to-parent ancestry check | `EvidenceDeletionGuardTests.Refuses_unverifiable_deletion_history` | inventory-ancestry: ExitCode == 2 for a graft-free unrelated inventory with identical entries |
| PC-64 / G-64 | skip deletion-to-head ancestry check; low-level marker-enumeration cut supplies a valid marked commit from a sibling branch | `EvidenceDeletionGuardTests.Refuses_unverifiable_deletion_history` | deletion-ancestry: ExitCode == 2; cut hit == 1 |
| PC-65 / G-65 | filter raw records to D before validation, discarding an added source sentinel | `EvidenceDeletionGuardTests.Rejects_missing_extra_or_non_deletions` | deletion-only: ExitCode == 1 for exact required deletions plus one addition |
| PC-66 / G-66 | remove expected-minus-actual set check | `EvidenceDeletionGuardTests.Rejects_missing_extra_or_non_deletions` | deletion-missing: ExitCode == 1 when S3 omits a required path that a later unmarked commit deletes; all final-tree checks otherwise pass |
| PC-67 / G-67 | remove actual-minus-expected set check | `EvidenceDeletionGuardTests.Rejects_missing_extra_or_non_deletions` | deletion-extra: ExitCode == 1 for D(B) plus one permitted Markdown deletion, restored identically after S3; outside sentinel vector remains ordinary coverage |
| PC-68 / G-68 | skip old-OID equality | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | deletion-old-oid: ExitCode == 1 after editing a legacy blob before deletion |
| PC-69 / G-69 | skip old-mode equality | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | deletion-old-mode: ExitCode == 1 after 100644 to 100755 change with identical blob |
| PC-70 / G-70 | omit D(B) final-head absence check | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | deletion-resurrection: ExitCode == 1 after a deleted oversize Markdown path returns with permitted small bytes; final rule scan alone passes |
| PC-71 / G-71 | skip the historical cat-file existence loop | `EvidenceDeletionGuardTests.Refuses_unverifiable_deletion_history` | deletion-recoverable: ExitCode == 2 after the final-tree observation cut removes a fixture-owned loose deleted blob; metadata/classification already succeeded and cut hit == 1 |
| PC-72 / G-72 | write a temporary tracked sentinel through git update-index before returning success | `EvidenceDeletionGuardTests.Verifies_exact_legacy_deletion` | deletion-read-only: index digest equals independent before snapshot |
| PC-73 / G-73 | replace root-component matching with substring Contains(.antiphon) | `EvidenceDiffGuardTests.Allows_small_markdown_and_other_source` | root-scope: ExitCode == 0 for .antiphon-other and nested x/.antiphon |
| PC-74 / G-74 | include the leaf filename in the checkpoint-component scan | `EvidenceDiffGuardTests.Rejects_checkpoint_paths` | checkpoint-leaf: ExitCode == 0 for .antiphon/checkpoints.md |
| PC-75 / G-75 | accept the final unterminated record in the parser | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-terminator: ExitCode == 2 with otherwise valid tiny Markdown record |
| PC-76 / G-76 | normalize an unknown Q status to M | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-status: ExitCode == 2 for malformed status |
| PC-77 / G-77 | skip object-id shape validation; downstream low-level fixture replies remain permissive and valid | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-object-id: ExitCode == 2 before object lookup |
| PC-78 / G-78 | normalize malformed mode 10064x to 100644 | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-mode: ExitCode == 2 |
| PC-79 / G-79 | remove master from actual push branches | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event` | trigger-master: branch patterns contain master |
| PC-80 / G-80 | remove workflow_dispatch from the actual workflow | `EvidencePolicyWorkflowTests.Workflow_selects_every_required_event` | trigger-manual: workflow_dispatch node exists |
| PC-81 / G-81 | replace the EventPath argument value with an unrelated constant path | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | event-path-binding: AST EventPath argument uses the expected environment-backed path |
| PC-82 / G-82 | replace EventName value with literal push | `EvidencePolicyWorkflowTests.Workflow_calls_real_guard_and_propagates_failure` | event-name-binding: AST EventName argument uses the event-name environment variable |
| PC-83 / G-83 | replace the manual-base environment expression with an empty literal | `EvidencePolicyWorkflowTests.Workflow_uses_data_arguments_and_no_write_permissions` | manual-base-binding: YAML env and AST argument preserve the explicit input |
| PC-84 / G-84 | replace individually with directory-wide force-add in the common allowance | `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked` | common-individual: composed common allowance requires individual report paths |
| PC-85 / G-85 | delete only the outside-checkpoint-directories clause from the common allowance | `InstructionBundleTests.C1015_Composed_workers_keep_generated_evidence_untracked` | common-directory: composed common allowance excludes checkpoint directories |
| PC-86 / G-86 | replace only the unedited CHECKPOINT-line requirement with permission to paraphrase | `InstructionBundleTests.C1015_Code_keeps_range_guard_and_exact_source` | code-verbatim: stage Code requires unedited CHECKPOINT lines |
| PC-87 / G-87 | use Contains instead of exact complete trailer-line equality | `EvidenceDeletionGuardTests.Rejects_ambiguous_or_merge_deletion` | deletion-trailer: ExitCode == 1 with only a CARD-10150 near-match marker |
| PC-88 / G-88 | change the strict UTF-8 path decoder to replacement fallback, keeping all other raw fields valid | `EvidenceDiffGuardTests.Refuses_failed_or_malformed_git_results` | raw-encoding: ExitCode == 2 for an invalid-byte tiny Markdown path |
| PC-89 / G-89 | skip zero-parent commits in the history walk | `EvidenceDiffGuardTests.Checks_merge_side_history` | merged-root: ExitCode == 1 and introducing root SHA is reported although the root lineage later deleted the artifact |
| PC-90 / G-90 | restrict inventory iteration to paths in the original anchor instead of every scoped B entry | `EvidenceDeletionGuardTests.Verifies_exact_legacy_deletion` | inventory-rule-set: InventoryOnly ordered rejected paths equal the independent B oracle, including fixture newer-pre-base.log; check this assertion before aggregate diagnostics |
| PC-91 / G-91 | omit only the InventoryBytes equality comparison | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | inventory-bytes: ExitCode == 1 with correct count/digest and wrong byte sum |
| PC-92 / G-92 | derive validation inventory/old identities from the deletion parent instead of pinned B | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | inventory-pinned-base: ExitCode == 1 after a non-anchor artifact receives same-length new bytes before S3; count/path digest/byte sum stay correct |
| PC-93 / G-93 | skip missing original B entries in anchor identity validation | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | anchor-present: InventoryOnly ExitCode == 1 when B omits one permitted original task report; immutable anchor signature otherwise matches |
| PC-94 / G-94 | omit only original anchor OID equality at B | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | anchor-oid: InventoryOnly ExitCode == 1 after B replaces one permitted original report with same-length permitted bytes; path/mode/classification remain valid |
| PC-95 / G-95 | omit only original anchor mode equality at B | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | anchor-mode: InventoryOnly ExitCode == 1 after B changes an allowed original report from 100644 to 100755 with identical OID |
| PC-96 / G-96 | omit the original classification-signature comparison | `EvidenceDeletionGuardTests.Rejects_wrong_inventory` | anchor-classification: InventoryOnly ExitCode == 1 when the low-level size-result cut supplies an oversize size for one allowed original report only during immutable-anchor classification; B metadata stays native, other identity checks pass, cut hit == 1 |
| PC-97 / G-97 | skip the complete final-head policy scan | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | final-tree-policy: deletion-validator ExitCode == 1 after a new non-D(B) forbidden file is added after exact S3; no history checker is invoked before this assertion |
| PC-98 / G-98 | skip missing kept root-evidence entries at head | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | kept-present: ExitCode == 1 after a permitted B report is deleted in a later unmarked commit; S3 and final rule compliance otherwise pass |
| PC-99 / G-99 | omit kept root-evidence OID comparison at head | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | kept-oid: ExitCode == 1 after a permitted report gets different permitted bytes after S3 |
| PC-100 / G-100 | omit kept root-evidence mode comparison at head | `EvidenceDeletionGuardTests.Rejects_changed_legacy_or_resurrected_paths` | kept-mode: ExitCode == 1 after a permitted report changes only from 100644 to 100755 after S3 |
| PC-101 / G-101 | accept an extra ls-tree header token and parse the otherwise valid remaining fields | `EvidenceDeletionGuardTests.Refuses_unverifiable_deletion_history` | tree-fields: InventoryOnly ExitCode == 2 for one malformed B tree record, with otherwise valid OID/mode/size/path and permissive downstream provider; cut hit == 1 |

### Out of scope

- D-5's card-Done deletion, raw-artifact retention service, remote cleanup recovery and new notification delivery: separate follow-up with the components/reasons above. Current Unit coverage is only a regression witness.
- Git history rewriting or size reclamation: expressly forbidden. S3 removes paths from the current tree; historical blobs remain recoverable. No blanket deletion of other tracked or ignored files.
- Semantic inspection of Markdown content, artifacts disguised as Markdown, arbitrary generated payloads moved outside root `.antiphon/`, and branch-protection administration: beyond the specified path/mode/size guard. Review enforces the anti-evasion instruction; extension checking does not prove a file is a human report.
- Whole-assembly, database, live provider/session, client/browser and Windows native qualification: no changed runtime path requires them. Full eligible Unit plus named Git integrations remains mandatory. Six explicitly Windows-only Unit methods are outside the selected Linux lane, not waived skips.
- Hosted Actions execution and active server bundle adoption are caller acceptance observations after push/activation; local fixtures do not prove them. The existing restart: server / runner: none requirement remains.

### Checkpoints

One committed group **S1-S3** includes S2b. Ordinary execution runs after all slices are pushed, including the deletion-only S3. CP-1 owns the isolated build; CP-2..CP-5 reuse it at unchanged source. CP-6/CP-7 are read-only non-TUnit commands. These seven rows are the whole ordinary scope, with **4,046 TUnit results** at the inspected master plus this card: 4,020 + 18 + 6 + 1 + 1. No dynamic discovery or test invocation was used for this freeze.

The full Unit row selects Linux (no host pin) because its six Windows exclusions are explicit and it needs the existing bash/jq fixtures. This supersedes the draft's Any-lane claim for CP-1. The new Git tests themselves remain portable; test unusual paths/modes through Git objects, not platform skips. All selected tests require zero skipped/failed. Supply `--expected-source-sha` and validate clean-source/build receipts at the exact committed tip. `C804_ORPHAN_SWEEP_ROOT=c1015-disabled` keeps the Unit test-hook sweep away from unrelated temp roots.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1015/` | linux-unit | `/*[Category=Unit]/*/*/(!windows_quick_row_finishes_beside_a_slow_row*)&(!windows_row_arguments_round_trip_intact*)&(!windows_chatty_row_drains_interleaved_stdout_and_stderr*)&(!windows_row_timeout_kills_the_start_b_grandchild*)&(!C665_LockedFileMidDeleteResumesOnLaterPass*)&(!C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded*)` | V-19, V-20, V-25, R-1, R-6, R-7 | all eligible Unit names; >=4020 executed; 0 failed/skipped; new bundle 4 and workflow 4 all present | 4020 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c1015-disabled;TUNIT_MAX_PARALLEL_TESTS=4` |
| CP-2 | S1-S3 | CP-1 | evidence-history | `/*/*/EvidenceDiffGuardTests/*` | V-1..V-18, R-2, R-3, R-4 | all 18 exact methods; 0 failed/skipped | 18 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c1015-disabled` |
| CP-3 | S1-S3 | CP-1 | exact-deletion | `/*/*/EvidenceDeletionGuardTests/*` | V-21, R-5 | all 6 exact methods and rule/anchor/preservation vectors; 0 failed/skipped | 6 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c1015-disabled` |
| CP-4 | S1-S3 | CP-1 | land-ignored-smoke | `/*/*/LandingGitTests/C642_IdentityAndStatusScopeSkipsIgnoredListing` | V-22, R-2 | exact method; 0 failed/skipped | 1 | 1 | true | `C804_ORPHAN_SWEEP_ROOT=c1015-disabled` |
| CP-5 | S1-S3 | CP-1 | source-ignored-smoke | `/*/*/CheckpointSourceStateTests/clean_and_ignored_outputs_match_head` | V-22, R-2 | exact method; 0 failed/skipped | 1 | 1 | true | `C804_ORPHAN_SWEEP_ROOT=c1015-disabled` |
| CP-6 | S1-S3 | n/a | candidate-history | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef "$C1015_BASE_SHA" -HeadRef HEAD` | V-23, R-3, R-4 | exit 0; resolved B/head and counts; 0 violations; B equals recorded dispatch base | n/a | 1 | true | n/a |
| CP-7 | S1-S3 | n/a | rule-deletion | `pwsh -NoProfile -File scripts/check-evidence-deletion.ps1 -InventoryRef "$C1015_BASE_SHA" -InventoryPathSha256 "$C1015_DELETE_PATH_SHA256" -InventoryCount "$C1015_DELETE_COUNT" -InventoryBytes "$C1015_DELETE_BYTES" -HeadRef HEAD` | V-24, R-5 | exit 0; one marked ordinary commit; exactly D(B) deleted; anchor 108=88+20; allowed Markdown identical; 0 final-tree rejects; historical blobs retrievable | n/a | 1 | true | n/a |

Code/Review bootstrap the checkpoint tool once through `scripts/build-slot.ps1` into `bin-c1015-tool/`. Import the real seven-row table, compare filters/minima/build reuse to this manifest, and run the owner's read-only coverage lint with explicit files for the three new classes and InstructionBundleTests plus the two smoke/census files. New labels must bind to real assertions; reconcile static diagnostics without treating syntax success as runtime proof. Use the built DLL for importer/lint so those calls introduce no implicit build. Run the checkpoint tool for this plan with `--after S1-S3 --expected-source-sha` set to the actual full source SHA and wait until exit is not 75. Any slot timeout is not-run, never a reason to bypass the gate. No tests run while source is changing.

CP-6/CP-7 are exact Linux-shell commands: RowRunner uses `/bin/sh -lc` for non-TUnit rows. Before starting the checkpoint tool, export `C1015_BASE_SHA`, `C1015_DELETE_PATH_SHA256`, `C1015_DELETE_COUNT` and `C1015_DELETE_BYTES` from the independently recorded B admission inventory. Use only the validated full commit SHA, 64-hex digest and nonnegative decimal count/bytes; compare each to the captured record and include the values in the stored task report. These mandatory bindings are defined inputs, not guessed refs or deferred scope decisions; absent/invalid values fail the script parameter contract with exit 2. The checkpoint child inherits them, and both commands print resolved values. Use the same bindings for Review/land acceptance; never resolve moving origin/master during a row. The original bb5fa... SHA is only the classification anchor. The complete final-tree proof catches pre-B legacy artifacts; B..HEAD history catches new violations. Do not scan bb5fa..HEAD and mistake already-landed pre-B artifacts for this task's additions. Read-only admission counting and InventoryOnly comparison before S3 are preparation, included in setup below; all ordinary verification runs are exactly these seven rows.

### Cost

All figures below are **estimated**, not measured. Ordinary V/R floor (Code) = **29 minutes**, the CP EstimatedMinutes sum **15+5+5+1+1+1+1**, including one isolated test-project build. Gated tool bootstrap plus importer/coverage preparation = **3 minutes**; ordinary Code setup/build + V/R = **32 minutes**, excluding authoring, queue waits and repairs. Review repeats the same setup and all seven rows: another **32 minutes**. Post-land caller CP-6/CP-7 acceptance adds **2 minutes**; server restart/loaded-bundle/hosted Actions observation is separately scheduled operational work, not a substitute for a test.

Mutation takes one four-minute baseline per distinct exact method at the landed source. Every PC costs **4-minute isolated red build/run + 0.25-minute exact restoration/check + 4-minute isolated green build/run = 8.25 minutes**. These conservative figures include rebuilds even for script/YAML controls, avoiding a promise of unproven build reuse. The inherited driver/external evidence setup adds **4 minutes**. Names/minima/family costs:

| Exact method filter | Controls | MinExecuted | Baseline minutes | Red/restore/green minutes | Family floor minutes |
|---|---|---:|---:|---:|---:|
| `/*/*/EvidenceDiffGuardTests/Handles_literal_paths_and_case_variants` | PC-1, PC-17, PC-18, PC-19, PC-20 | 1 | 4 | 5 x 8.25 = 41.25 | 45.25 |
| `/*/*/EvidenceDiffGuardTests/Rejects_checkpoint_paths` | PC-2, PC-3, PC-4, PC-74 | 1 | 4 | 4 x 8.25 = 33 | 37 |
| `/*/*/EvidenceDiffGuardTests/Rejects_non_markdown_outputs` | PC-5 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/EvidenceDiffGuardTests/Rejects_non_regular_modes` | PC-6 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/EvidenceDiffGuardTests/Enforces_one_mib_blob_limit` | PC-7, PC-8 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/EvidenceDiffGuardTests/Grandfathers_unchanged_legacy_and_allows_deletion` | PC-9, PC-10 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/EvidenceDiffGuardTests/Rechecks_modified_and_renamed_legacy_paths` | PC-11, PC-12 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/EvidenceDiffGuardTests/Checks_intermediate_commits_even_when_tip_is_clean` | PC-13 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/EvidenceDiffGuardTests/Checks_merge_side_history` | PC-14, PC-15, PC-89 | 1 | 4 | 3 x 8.25 = 24.75 | 28.75 |
| `/*/*/EvidenceDiffGuardTests/Reads_pinned_git_objects_not_index_or_worktree` | PC-16 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/EvidenceDiffGuardTests/Refuses_unverifiable_ranges_and_objects` | PC-21, PC-22, PC-23 | 1 | 4 | 3 x 8.25 = 24.75 | 28.75 |
| `/*/*/EvidenceDiffGuardTests/Refuses_failed_or_malformed_git_results` | PC-24, PC-25, PC-26, PC-75, PC-76, PC-77, PC-78, PC-88 | 1 | 4 | 8 x 8.25 = 66 | 70 |
| `/*/*/EvidenceDiffGuardTests/Resolves_refs_once_and_stays_read_only` | PC-27, PC-28 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/EvidenceDiffGuardTests/Ci_selects_cumulative_feature_push_range` | PC-29 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/EvidenceDiffGuardTests/Ci_selects_complete_default_branch_push_range` | PC-30, PC-31 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/EvidenceDiffGuardTests/Ci_requires_valid_manual_range` | PC-32 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/EvidenceDiffGuardTests/Ci_refuses_invalid_events_and_missing_history` | PC-33, PC-34, PC-35, PC-36, PC-37, PC-38 | 1 | 4 | 6 x 8.25 = 49.5 | 53.5 |
| `/*/*/EvidencePolicyWorkflowTests/Workflow_selects_every_required_event` | PC-39, PC-40, PC-79, PC-80 | 1 | 4 | 4 x 8.25 = 33 | 37 |
| `/*/*/EvidencePolicyWorkflowTests/Workflow_pins_full_history_and_event_head` | PC-41, PC-42 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/EvidencePolicyWorkflowTests/Workflow_calls_real_guard_and_propagates_failure` | PC-43, PC-44, PC-45, PC-81, PC-82 | 1 | 4 | 5 x 8.25 = 41.25 | 45.25 |
| `/*/*/EvidencePolicyWorkflowTests/Workflow_uses_data_arguments_and_no_write_permissions` | PC-46, PC-47, PC-83 | 1 | 4 | 3 x 8.25 = 24.75 | 28.75 |
| `/*/*/InstructionBundleTests/C1015_Composed_workers_keep_generated_evidence_untracked` | PC-48, PC-49, PC-84, PC-85 | 1 | 4 | 4 x 8.25 = 33 | 37 |
| `/*/*/InstructionBundleTests/C1015_Code_keeps_range_guard_and_exact_source` | PC-50, PC-51, PC-86 | 1 | 4 | 3 x 8.25 = 24.75 | 28.75 |
| `/*/*/InstructionBundleTests/C1015_Review_remains_read_only_and_checks_history` | PC-52, PC-53 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/InstructionBundleTests/C1015_SourceLanding_keeps_its_external_evidence_exception` | PC-54, PC-55 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/InstructionBundleTests/each_stage_bundle_is_ascii_and_under_the_size_cap*` | PC-56, PC-57 | 6 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/InstructionBundleTests/the_worst_case_composition_measured_sits_far_under_the_budget` | PC-58 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |
| `/*/*/EvidenceDeletionGuardTests/Rejects_wrong_inventory` | PC-59, PC-60, PC-91, PC-93, PC-94, PC-95, PC-96 | 1 | 4 | 7 x 8.25 = 57.75 | 61.75 |
| `/*/*/EvidenceDeletionGuardTests/Rejects_ambiguous_or_merge_deletion` | PC-61, PC-62, PC-87 | 1 | 4 | 3 x 8.25 = 24.75 | 28.75 |
| `/*/*/EvidenceDeletionGuardTests/Refuses_unverifiable_deletion_history` | PC-63, PC-64, PC-71, PC-101 | 1 | 4 | 4 x 8.25 = 33 | 37 |
| `/*/*/EvidenceDeletionGuardTests/Rejects_missing_extra_or_non_deletions` | PC-65, PC-66, PC-67 | 1 | 4 | 3 x 8.25 = 24.75 | 28.75 |
| `/*/*/EvidenceDeletionGuardTests/Rejects_changed_legacy_or_resurrected_paths` | PC-68, PC-69, PC-70, PC-92, PC-97, PC-98, PC-99, PC-100 | 1 | 4 | 8 x 8.25 = 66 | 70 |
| `/*/*/EvidenceDeletionGuardTests/Verifies_exact_legacy_deletion` | PC-72, PC-90 | 1 | 4 | 2 x 8.25 = 16.5 | 20.5 |
| `/*/*/EvidenceDiffGuardTests/Allows_small_markdown_and_other_source` | PC-73 | 1 | 4 | 1 x 8.25 = 8.25 | 12.25 |

PC floor (Mutation) = **969.25 minutes**: 34 method baselines x4 = 136, plus 101 independent red/restore/green cycles x8.25 = 833.25. With external setup, Mutation admission + PC floor = **973.25 minutes**. Combined Code + Mutation floor = **1005.25 minutes = 3 + 29 + 4 + 136 + 833.25**. Including separate ordinary Review (32) and caller actual-master acceptance (2) gives **1039.25 minutes**. All times are estimated; this dispatch ran zero builds/tests/PCs. The method-scoped PC schedule has **261 TUnit result executions**, including the six-argument cap method, separate from the 4,046 ordinary results.

One baseline per method instead of per PC saves **268 estimated minutes** ((101-34)x4) after exact restoration at the same source. CP build reuse across four integration rows saves **12 estimated minutes** versus four redundant three-minute builds. Measured savings = 0. Compared with the prior freeze, the twelve added controls add **99 minutes**; CP-3 gains **2 minutes** for anchored fixture and boundary work. No savings are assumed from broad PC filters, omitting guards or batching controls that share a file/method. Authoring, slot waits and findings are additional.
## Rule-based scope amendment (task b6180921)

2026-10-03. Assigned start: `7903d53c1cdfc181565ee8335b3e25944fedb828`. Fetched and inspected `origin/master`: **`7af83b0c3270006cd98a25a69531f24e37240a4e`**. Inspection used an object-only, no-checkout local clone at `/tmp/card1015-b6180921-master-inspection`, with every inventory read naming that full master SHA. Only tracked paths, modes, object IDs and byte sizes were inspected for evidence; no evidence payloads, credentials or private notes were opened. No files were deleted. The operator subsequently accepted option 1 explicitly: keep D-1 and all 23 permitted Markdown files, including all six c998 summaries. This section records that resolved scope and supersedes every earlier all-108-deleted instruction.

### Inspection

- Read the complete existing plan, the checkpoint-manifest/coverage/Mutation owner sections in `docs/testing-and-build.md`, and the stage/landing/cleanup policy in `docs/orchestration-loop.md`. The original S1/S2 fix design and D-1/D-3/D-4/D-6 remain unchanged.
- Read the nearest new-script fixture bodies: `Scripts/ScriptHarness`, `Application/DelegateScriptRunner`, `TestHelpers/LandingGitFixture`, and `RunCheckpointSourceScriptTests` with its nested fixture/process helpers. Their real Git/PowerShell invocation, process ownership and byte-fixture gaps still require the setup already specified in the freeze. The finalized amendment adds boundary vectors to the six already named deletion methods; the 32-method new-test roster is unchanged.
- Read `CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`, `UsageLibrary` and `CheckpointRoster.CompiledCases`; compared all `tests/Antiphon.Tests`, `tests/Shared` and census-library changes from the assigned start to inspected master. CARD-1005 contributes 12 non-parameterized Unit/Checkpoints tests; CARD-1006 contributes 11 non-parameterized `GrokLinuxBlockingPromptTests` methods and one `GrokSignInPromptDetectorTests` method. Source counting excludes strings/text fixtures and is not execution evidence.
- Read-only inventory command: `git ls-tree -r -z -l --full-tree 7af83b0c3270006cd98a25a69531f24e37240a4e`. Split metadata from literal path at the first tab and records at NUL; use committed byte sizes, not worktree lengths. Filtering the ordered records to the exact case-insensitive root `.antiphon` component yields **312 paths / 38,101,068 bytes**. SHA-256 of concatenated original path bytes, each followed by NUL, is **`ba48a18494f1c24ff59df50ba5171e967a755be4572865df7c6523e4772bbec0`**.
- All original 108 entries retain their exact path/mode/object identities. The increase is **204 paths / 20,325,527 bytes**: 117 CARD-1005 artifacts, six CARD-1006 artifacts, 78 checkpoint paths, and three permitted task Markdown files. The full original inventory remains **108 / 17,775,541 bytes / `356edf4a223e53669d4137631e40c0f5f7d19127c2568fdd0608fa4364e5ac9c`**.

### Rule and path-family classification

The existing D-1/D-3 predicate is fully mechanical. For a tracked entry whose first path component equals `.antiphon` ignoring case, reject it if **any** of these is true: a descendant directory component contains `checkpoints` ignoring case; its mode is neither `100644` nor `100755`; its final extension is not `.md` ignoring case; or its committed blob exceeds **1,048,576 bytes**. Entries outside that root are out of this guard's scope. A parse/missing-object failure is unknown, never an allowed entry. Directory tests exclude the leaf filename. No content classifier or new filename-family exception is implied.

| Family at inspected master | Total paths | Total bytes | D-1 rejected paths / bytes | D-1 permitted paths / bytes | Classification |
|---|---:|---:|---:|---:|---|
| `.antiphon/checkpoints/**` | 115 | 26,698,398 | 115 / 26,698,398 | 0 / 0 | Generated checkpoint output, including its Markdown reports, locks, JSON/JSONL, TRX, HTML, logs and manifests; forbidden directory wins regardless of extension/size. |
| `.antiphon/c1005-*` and their descendants | 117 | 5,302,554 | 117 / 5,302,554 | 0 / 0 | Logs, JSON receipts, TRX, HTML, CSV/TXT/TEXT rosters, YAML manifests, and scratch PS1/CS/CSPROJ probes; none meets the Markdown allowance. |
| `.antiphon/c1006-code-evidence/**` | 6 | 1,182,494 | 6 / 1,182,494 | 0 / 0 | Archive inventories, SHA256 files and two tar.gz archives; all rejected. |
| `.antiphon/c965-continuation/**` and `.antiphon/c965-verification/**` | 24 | 1,009,163 | 24 / 1,009,163 | 0 / 0 | TRX, JSON receipts and TXT restoration/validation records; all rejected. |
| `.antiphon/c998-evidence/**` non-Markdown entries | 26 | 2,351,915 | 26 / 2,351,915 | 0 / 0 | Compressed TRX, logs, YAML and JSON; all rejected. |
| `.antiphon/c998-evidence/**` Markdown entries | 6 | 18,267 | 0 / 0 | 6 / 18,267 | Four `report.md` and two `CP-*-failures.md` files. **Preserve all six**, explicitly resolved by the operator under unchanged D-1. |
| `.antiphon/card1007-evidence.tar.gz` | 1 | 997,368 | 1 / 997,368 | 0 / 0 | Archive rejected even below 1 MiB. |
| `.antiphon/task-*.md` | 17 | 540,909 | 0 / 0 | 17 / 540,909 | Regular Markdown task reports, each below 1 MiB; D-1 permits them. Fourteen belong to the original 108. |
| **Root evidence total** | **312** | **38,101,068** | **289 / 37,541,892** | **23 / 559,176** | The 312-path census is not a generated-artifact deletion inventory. |
| `docs/investigations/*.md` (recursive census) | 194 | 2,749,360 | 0 / 0 | 194 / 2,749,360 | Preserve: documentation outside root `.antiphon/`; all individually below 1 MiB. |
| `docs/superpowers/plans/*.md` (recursive census) | 464 | 19,149,682 | 0 / 0 | 464 / 19,149,682 | Preserve: plan deliverables outside root `.antiphon/`; all individually below 1 MiB. |

The last two rows are outside the 312-path total. Other source/fixture paths outside root `.antiphon/` also stay; a `.json` test fixture is not permission to delete source. No generated payload is relocated into `docs/`.

Under the unchanged predicate the rejected-set digest is **`4e0a2ec36b9817c7cba9a2d5e05e85ecaa3f5a23b56daeb7469b3fd55b7684ed`** and permitted-set digest is **`2e663b26148771f50e1cb73c6e4821b9e3df52b14190504a8cd199588c868ace`**. These are observations at the inspected SHA, not literals to require at a later Code start.

### Accepted decision and literal anchor

The operator chose option 1: **keep D-1 unchanged**. At inspected master delete 289 rejected paths and preserve 23 permitted Markdown files. The original 108 are a classification/preservation anchor, not an exception permitting Markdown deletion: **88 rejected / 17,414,103 bytes**, **20 permitted / 361,438 bytes**. The 20 comprise 14 task reports (343,171 bytes) plus all six c998 summaries (18,267 bytes). This decision supersedes the earlier all-108 deletion requirement; there is no remaining scope question.

Pin the original full list (108/count/bytes/digest and mode/OID identities), plus the rejected partition NUL-list SHA-256 **`26abc1b90cf1dc40dec8dfb38bd8f5c75f8c555f22f8042c49268c78f259e95f`** and permitted partition SHA-256 **`320fcb2d7ffe6607de6c46d3bd477522ffda0ab7355bb10be252e7eac8be05f6`**. The independent full classification signature is **`f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b`**. Fixture expectations derive D(B) from an independently authored rule and then assert these literal anchors; they never use the candidate deleted set or production classifier output as expected values.

### Required sequencing and Code order

Land this plan branch **doc-only, with no implementation Review evidence**, before dispatching Code. Code starts from current `origin/master` containing the resolved plan and CARD-1005. Do not revive the old admission-blocked Code branch, merge/rebase this assigned branch, or pin the next Code start to the inspection SHA.

1. Record fresh Code dispatch B; verify plan/CARD-1005 containment, independent Checkpoints census **377**, source roster and Linux prerequisites read-only. The ordinary scope at the inspected base plus planned additions is **4,046 results** (4,020 Unit + 18 history + 6 deletion + 2 smoke). Reconcile any later source drift before implementation; no census edit belongs to CARD-1015.
2. Independently classify complete T(B) from native Git metadata using the accepted rule, record D(B)/K(B), count, byte sum, path digest and original-anchor identities/classifications. Capture essential values in the stored report and ignored local inventory. This computation is authorized preparation, not a test run or new operator decision; new pre-B artifacts are included automatically by rule.
3. Implement and commit/push S1, S2 and S2b separately, verification pending. Complete the byte/fault/anchored fixture setup and independent test oracle. Run the new read-only InventoryOnly entry as admission preparation and require agreement with the independently recorded B inventory before any S3 staging. Record its outcome; it does not replace ordinary CP-3/CP-7.
4. Recheck selected current path mode/OID identities against B. Stage only literal D(B) paths via NUL/literal pathspecs, prove the complete staged set, then commit/push the deletion-only S3 with the exact marker. Preserve all allowed Markdown. No history rewrite or force-push.
5. Freeze source, export the four recorded admission bindings named in the manifest, bootstrap/import/lint through the owner workflow, and run the seven CP rows at the exact pushed source. All build/test drivers take the host slot; keep every process owned and awaited. Report qualified V/R and all 101 pending PCs, then return `next: review`. Never add generated output to Git or relabel earlier receipts.
6. After ordinary Review and implementation land, repeat CP-6/CP-7 using the same B inventory against actual master; record the landed deletion SHA and source identities. Concurrent new rejected evidence is a visible acceptance failure, requiring a separately reviewed fix, not silent enlargement of S3. Activate server bundles through the owner runbook, observe loaded hashes and the SHA-bound Actions guard result, and commission SourceLanding Mutation.

Whole-worktree cleanup remains **CARD-1017**. No cleanup runtime or asynchronous application delivery changes belong to this card; the exclusions and substitute limits in Delivery inventory remain in force.

## TestDesign validation and handoff

Bodies read before naming cases; **guards=101, mapped=101, missing=0, duplicate PC maps=0**. Every control names a compilable defect, an exact existing/planned method and a decisive assertion after explicit fixture setup. Original/missing/extra/preservation/recovery controls are isolated so another refusal cannot mask the intended decision. All PCs are executable design obligations for post-land Mutation, not claimed runs. There are **34 exact PC filters, 32 new ordinary methods, seven CP rows, 4,046 ordinary TUnit results**, estimated **29-minute ordinary V/R floor** and **969.25-minute PC floor**. No placeholder/TBD cases remain.

Documentation validation consists of read-only Git metadata, Markdown/table/ID/cost arithmetic and `git diff --check`; no repository build, test, compiled importer or PC ran. The freeze is unsuspended. The only remaining prerequisite is publication of this plan onto master before fresh Code dispatch. Current master already contains CARD-1005 and its 377 census. Push each implementation slice normally and leave cleanup runtime unchanged.

--- next stage ---
next: land
handoff: Land this amended plan doc-only, with no implementation Review evidence; then dispatch fresh Code from current master containing the plan and CARD-1005. Keep D-1: compute D(B), preserve permitted Markdown, anchor original 108 as 88 delete/20 keep. Implement S1/S2/S2b/S3, run seven CP rows (4046 results), leave 101 PCs pending and return next: review. Cleanup remains CARD-1017.
artifact: docs/superpowers/plans/2026-10-03-card-1015-evidence-git-policy-plan.md

### Code checkpoint filter repair (bc463e02)

The actual importer accepted all seven frozen rows, but MTP 2.2.2 rejected
CP-1 before executing any tests: a property predicate cannot attach to a
grouped Boolean expression. Apply the already-landed CARD-1005 continuation
filter repair: put `[Category=Unit]` on the assembly wildcard and retain the
same six method negations with trailing stars for result-name suffixes.
The selected lane, six exclusions, count floor, assertions, timeouts and
environment are unchanged. Run 20261003-203943-2d3d records the parser refusal
and no Unit TRX; the repaired run is the first actual whole-Unit execution.
