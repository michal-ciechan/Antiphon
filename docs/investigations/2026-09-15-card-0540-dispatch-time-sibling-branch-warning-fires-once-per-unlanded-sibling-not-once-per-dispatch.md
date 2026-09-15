# CARD-0540: Dispatch-time sibling-branch warning fires once per unlanded sibling, not once per dispatch

## Outcome

**Confirmed by reconstruction from database, transcript and Git evidence:** Review task `7ef54b1c` generated four intentional per-sibling warnings, each delivered once. This incident is notification fan-out, not replay or a duplicate database join. The guard does not collapse equal tips or recognize that one missing sibling contains another. All three earlier warned branch tips are ancestors of the fourth/latest tip; two earlier branch names identify exactly the same commit.

The next stage is **Plan** for the reported notification-noise problem. Changes are limited to this evidence document and external capture files; no product code or live state was changed.

## Scope and version boundary

- Investigation task: `df909141`; date: 2026-09-15.
- Card: `a8ac26c2-88e2-4d4a-9bb9-1c0fa58c7a15` / CARD-0540; full description read using `pwsh -NoProfile -File scripts/card.ps1 get CARD-0540`.
- Subject card: `75f13b1a-649c-4b4b-a034-e1b16663b591` / CARD-0527.
- Subject Review task: `7ef54b1c-1141-4aea-b684-4cb8b9f16e75`, attempt 1, dispatched at `2026-09-15T20:50:19.141021Z`.
- Parent session: `39c6eb3a-5e96-4f67-9b09-253c106a1d64`.
- Investigated checkout: `7c02746584daabb8303a96dae23a6a87889d470e`.
- `GET /api/version` during collection reports **`8ccdb1c93d6dc288b05738ee0f7faded35548b4a`**, capability `land-v2`. This is older than the checkout and does **not** contain CARD-0508 commit `daf849c2` (`git merge-base --is-ancestor daf849c2 8ccdb1c` exits 1).

Source citations below explicitly distinguish **running-version source** (`git show 8ccdb1c93d6dc288b05738ee0f7faded35548b4a:<path>`) from **checkout source** (at `7c027465`). The task's stored base is `7c02746584daabb8303a96dae23a6a87889d470e`, `MergeTargetRef` is null. A newer worktree base does not imply that the running server contains newer dispatcher code.

## Stored incident evidence

Read-only queries used the `public` schema of the local `antiphon` database, inside `BEGIN TRANSACTION READ ONLY` / `ROLLBACK`. The ordinary task-detail API independently returns the same four warnings.

All four `AgentTaskEvents` rows have `Type=12` (Warning), subject task ID above, and timestamp `2026-09-15T20:50:24.916591Z`:

| Sibling branch suffix (`feat/card-task-`) | Warning event ID | Recorded tip | Recorded commits above base |
|---|---|---|---:|
| `2853f966` | `d7825170-498d-43a4-84a4-b11cf595b7c6` | `29708a88` | 21 |
| `360e223e` | `17a5bef3-4b85-4560-9670-63106cbfedec` | `4d2a9550` | 18 |
| `0d48a707` | `e0fc81ae-1c42-44c8-b533-0bb494e0f992` | `29708a88` | 21 |
| `b23741b2` | `9f58641d-7185-4599-bc0f-4ed669ece81e` | `59e5499b` | 29 |

Example exact warning body:

> task 7ef54b1c branched from HEAD without CARD-0527's kept branch feat/card-task-2853f966 (29708a88, 21 commits: 'CARD-0527 F8/F9 ordinary verification complete: 3062 pass, six unchanged inherited failures, one skip'). Land 2853f966 first, or expect its commits to be absent from this branch.

The other bodies have the same template but different sibling identity; the equal-tip pair also differs in its branch name and `Land <task>` instruction. Each warning body equals exactly one stored queue body and one stored **UserPrompt** text. This is transcript-confirmed delivery, not an inference from queue status.

| Sibling | Queue sequence / row ID | Attempts | UserPrompt sequence / row ID | Prompt timestamp (UTC) |
|---|---|---:|---|---|
| `2853f966` | 412 / `4f1d54f8-548c-4021-a759-8601e54a67d6` | 1 | 3143 / `4c5e543b-e54a-4fcc-9953-d01c82e24454` | 20:50:56.749 |
| `360e223e` | 413 / `aa44ff28-f92c-4d32-806c-000f898c8367` | 1 | 3147 / `a3de5c6f-66f3-4bee-a833-d6c3ee17ec31` | 20:51:03.182 |
| `0d48a707` | 414 / `08ea6814-fda5-4e2e-8d46-18662a2fd725` | 1 | 3150 / `8dfe78f4-2d8e-4a9a-b52e-face014626a1` | 20:51:08.536 |
| `b23741b2` | 415 / `1fc680b1-9248-4b4e-8839-138da9141716` | 1 | 3153 / `34602324-d492-4188-83d4-4c03dbad60c8` | 20:51:14.382 |

The four submissions span **17.633 seconds**. Their order matches the card's report. Event timestamps are identical, so event ordering alone cannot establish delivery order. `AgentTaskLandNotifications` has **zero** rows for this task; `AgentTaskDispatchWarningIntents` is absent from this live schema. This matches the older direct-queue path, not CARD-0508's later durable-intent path.

The four siblings are separate Succeeded **Code** task rows, not four Review task rows. The review/repair workflow accumulated those Code branches:

| Task ID | Title | Completed (UTC) |
|---|---|---|
| `2853f966-b31e-4893-9bcc-b25fb9c0a4be` | CARD-0527: build commit-on-settle chaining | 12:49:35.776149 |
| `360e223e-e538-4104-8629-4050255c2741` | CARD-0527: fix seven review defects | 17:40:32.008703 |
| `0d48a707-a81b-4629-849d-13207f36f520` | CARD-0527: fix F8/F9 commit-recovery defects | 19:01:48.406358 |
| `b23741b2-dc4b-4f2d-bdfa-d36104338bae` | CARD-0527: fix F10/F11/F12 recovery and safety defects | 20:48:19.127756 |

All four record `RepoPath=C:\src\Antiphon`, Worktree workspace, null `MergeTargetRef`, null `LandRequestedAt`, and no landing receipt in the task API.

## Mechanism: why one dispatch produced four notices

### Running-version source (`8ccdb1c`)

1. `server/Application/Services/AgentTaskDispatcher.cs:581` evaluates the guard only for a card-bound Worktree task without an existing worktree. After successful dispatch, line **650** calls `WarnUnlandedSiblingsAsync` with the whole warning list.
2. `AgentTaskDispatcher.cs:2861` queries **all** other same-card Worktree tasks with Succeeded or Blocked status and a non-null branch. Line **2872** restricts them to the same repository. There is no join creating duplicate task rows, recency cutoff, newest-task selection, role filter, equal-tip grouping, or sibling-to-sibling comparison.
3. `AgentTaskDispatcher.cs:2883` chooses `MergeTargetRef ?? "HEAD"`. Lines **2887-2916** inspect each candidate independently: require a local branch, skip a branch contained in the base, hold if landing is requested, otherwise append one warning. The existence and ancestry probes are `DelegationWorktreeService.cs:104` and **116**; line **122** executes `git merge-base --is-ancestor branch baseRef`.
4. `AgentTaskDispatcher.cs:2919` is the incident's notification generator. Its loop at **2923** adds a fresh Warning event and calls `_queue.EnqueueAsync` at **2939** **for each sibling**, with WhenIdle delivery. `warnedAt` is captured once at **2922**, explaining the four identical event timestamps. The body template is at **2834-2841**.

Thus the cardinality is one warning and, for Session replies, one queued note per qualifying sibling task. The observed four distinct events, four queue rows and four exact transcript matches are the expected output of one execution of that loop. There is no evidence of retries or repeated dispatch generating these four messages.

This is an explicit original policy: `docs/superpowers/plans/2026-09-03-card-0215-plan-branch-ancestry-plan.md:69` enumerates kept siblings; lines **71-74** specify a warning and caller note for each uncontained sibling without a land in flight. The plan discusses a deliberately superseded plan as a reason to warn instead of refusing dispatch; it does not define supersession-based exclusion.

### Checkout source (`7c027465`): still one notice per sibling

CARD-0508 changes containment and delivery durability, but preserves fan-out:

- `server/Application/Services/AgentTaskDispatcher.cs:2902` retains the all-sibling query; **2923-2947** still compare each sibling only with the resolved dispatch base.
- `server/Application/Services/DelegationWorktreeService.cs:128` now uses `git cherry baseRef branch` and treats all `-` lines or empty output as contained. That handles patch-equivalent landed work; it does not compare siblings with each other.
- `AgentTaskDispatcher.cs:3376` creates one warning draft per sibling. `server/Application/Services/DispatchBaseNotificationPayload.cs:100` keys it as `sibling:<sibling task GUID>`.
- `server/Application/Services/DispatchBaseWarningIntentService.cs:33` loops over drafts; **37-45** deduplicates only the same `(DispatchEventId, WarningKey)`. `server/Infrastructure/Data/AppDbContext.cs:1621` enforces that pair's unique index. Distinct task IDs sharing a tip are intentionally distinct keys.
- `DispatchBaseWarningIntentService.cs:123` projects one event/notification pair per intent; `server/Application/Services/AgentTaskLandNotificationService.cs:73` enqueues one body per notification identity.
- Existing test source explicitly expects the per-sibling count: `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.cs:409` includes zero/one/two-sibling cases, and **482-489** asserts sibling-intent count equals sibling count, plus a separate mismatch warning where applicable. These tests were inspected, not run.

Therefore activating the newer checkout alone would not collapse these four sibling warnings. The recorded tips also have 21/18/21/29 unapplied patches against the stored base, so CARD-0508's patch-aware containment would not eliminate them on that same base.

## Reconstruction and branch containment measurements

The read-only reconstruction uses the stored base SHA, not mutable `HEAD`. It found **10** currently eligible sibling task rows, all completed before the incident and all in `C:\src\Antiphon`:

- Three local branches no longer exist: `62040dbd`, `35921275`, `2139f2f3`.
- Three existing Review branches are already ancestors of the stored base and have zero commits above it: `89bfbbdf`, `4d210064`, `32c1f938`.
- The remaining four branches produce exactly the recorded warning set and counts.

| Branch suffix | Full observed tip | Commits / unapplied patches above stored base | Ancestor of stored base? | Ancestor of latest tip? | Unapplied patches against latest |
|---|---|---:|---|---|---:|
| `2853f966` | `29708a88ff35a5f12b943459a7b5f02f3069501c` | 21 / 21 | No | Yes | 0 |
| `360e223e` | `4d2a95508024253dddd647b6f9c8f71f63341da5` | 18 / 18 | No | Yes | 0 |
| `0d48a707` | `29708a88ff35a5f12b943459a7b5f02f3069501c` | 21 / 21 | No | Yes | 0 |
| `b23741b2` | `59e5499b721be381fb6b49d3f2fe8e0e87b7293d` | 29 / 29 | No | Yes (self) | 0 |

For the three earlier tips, ancestor-of-latest exits 0; ancestor-of-base exits 1. All recorded short tips and commit counts match the immutable-object reconstruction. This is stronger evidence of content overlap than task dates or the phrase "review round". The guard's lack of sibling comparisons explains both the equal-tip pair and the older tips already included in the latest branch.

## Answers for Plan

1. **Intentional fan-out, not demonstrated dedup failure.** The query returns distinct task rows and the producer explicitly emits per row. Four separate deliveries occurred once each. The same behavior remains explicit in current source and test expectations.
2. **Aggregation is a product-policy choice, not a transport limitation established here.** The dispatcher already has the complete sibling list before emitting anything. Existing unrelated multi-item presentation includes `server/Application/Services/AwayDigestFormatter.cs:14` (collects lines, returns one bounded body at **40**) and landing's collection of multiple sibling tokens at `server/Application/Services/AgentTaskLandService.cs:434`. Neither is invoked by the dispatch warning producer. No aggregation design was produced in this stage.
3. **These earlier tips are contained in the latest tip; the implementation has no rule using that fact.** All four missing-from-base observations are technically true, but three convey content already represented by the latest sibling. A newer task's timestamp alone is not evidence of containment. The guard does not consult review-round or supersession metadata.
4. **Warning exclusion and physical deletion have different evidence requirements.** No-merge-target settlement expressly retains a task branch (`server/Application/Services/DelegationWorktreeService.cs:477`). The janitor works from age/metadata and guarded removal (`server/Infrastructure/Git/WorktreeManager.cs:553`), not later review-round containment. `docs/orchestration-loop.md:969` records the conservative cleanup contract: unknown receipts, dirty/mismatched sources and opaque files are retained. This investigation proves commit containment, not that any of these worktree directories can safely be deleted. No pruning was attempted.

## Validation and remaining uncertainties

- **Reconstruction passed:** 10 candidates classified; exactly 4 warnings mapped one-to-one to 4 queue rows (one delivery attempt each) and 4 exact UserPrompt entries; branch counts and all containment probes succeeded with expected exit codes. No application builds or test suites were run (0 tests); this is stored-incident reconstruction, not a fresh production dispatch.
- The version endpoint was sampled during investigation, not captured in the incident event. The stored schema, warning format and direct-queue rows match `8ccdb1c` source. A historical process/build log would establish the exact incident binary independently; it is not needed to explain the measured cardinality.
- The sibling task query and branch inventory are current observations. The four incident tips/counts are independently frozen in warning rows and reproduce exactly. The incident-time state of the six nonwarning candidates was not separately snapshotted.
- No dirtiness, ownership, foreign-file or cleanup-receipt audit was performed on sibling directories. Containment of commits does not establish deletion authority.
- No global claim is made about absence of other notification-retry defects; this incident contains no replay evidence. The downstream chat/channel presentation beyond the parent-session transcript was not audited.
- Selection, ordering, message bounds and the meaning of supersession remain Plan decisions, not unresolved root-cause hypotheses.

## Evidence files and rerun

External raw evidence root: **`C:\Antiphon\evidence\investigate-df909141`**.

- `incident-rows.json`: selected task, sibling, event, queue and transcript rows (no credentials).
- `git-reconstruction.json`: timestamped complete 10-candidate Git measurements.
- `reconstruct.ps1` / `reconstruction-read.sql`: read-only reconstruction and its database query.
- `delivery-rows.txt` / `delivery-read.sql`: exact warning delivery rows and query.
- `api-version.json`, `task-7ef54b1c.json`: API corroboration.
- `8ccdb1c-AgentTaskDispatcher.cs`, `8ccdb1c-DelegationWorktreeService.cs`: running-version source extracted from Git.

Rerun locally while these historical rows/refs remain available:

```powershell
pwsh -NoProfile -File C:\Antiphon\evidence\investigate-df909141\reconstruct.ps1
```

Core immutable Git checks, reproducible independently of the external script:

```powershell
$incidentBase = '7c02746584daabb8303a96dae23a6a87889d470e'
$latestTip = '59e5499b721be381fb6b49d3f2fe8e0e87b7293d'
$earlierTip = '29708a88ff35a5f12b943459a7b5f02f3069501c'
git merge-base --is-ancestor $earlierTip $incidentBase # exit 1
git merge-base --is-ancestor $earlierTip $latestTip    # exit 0
git rev-list --count "$incidentBase..$earlierTip"     # 21
git cherry $latestTip $earlierTip                    # empty
git show 8ccdb1c93d6dc288b05738ee0f7faded35548b4a:server/Application/Services/AgentTaskDispatcher.cs
```

## Not done, noted

Plan may consider one dispatch-level notice naming all missing siblings and content-proven redundancy handling; branch/worktree deletion remains a separate guarded-cleanup decision.

--- next stage ---
next: plan
handoff: Confirmed per-sibling fan-out: four warnings, four once-delivered UserPrompts; all earlier tips are contained in b23741b2 and two tips are identical. Plan notification cardinality and supersession semantics against current CARD-0508 durable intents, preserving divergent-work visibility and guarded cleanup; incident ran older 8ccdb1c source.
artifact: docs/investigations/2026-09-15-card-0540-dispatch-time-sibling-branch-warning-fires-once-per-unlanded-sibling-not-once-per-dispatch.md
