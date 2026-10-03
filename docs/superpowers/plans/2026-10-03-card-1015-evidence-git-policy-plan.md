# CARD-1015: keep generated verification evidence out of Git

Date: 2026-10-03. Plan task: `b29278e3-8d7e-4a39-8d0d-04d1d8a3cee9`.
Inspected source: `bb5fa774cd56f85ee6f0b1122c198192427e5ddf`.
Latest observed master: `b0ccedda2e93a6b847e9219a602a3413694f64ea`; its changes since the inspected source do not touch the relevant bundles, Git infrastructure, scripts, CI, testing owner or `.antiphon/` inventory.

Status: implementation plan under the defaults below, awaiting the card owner's decision. The brief requests verification planning but does not fold the separate TestDesign stage into this dispatch. After the decision, TestDesign freezes the verification design and checkpoint manifest before Code starts. No implementation or verification execution is claimed here.

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

### Checkpoints

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
