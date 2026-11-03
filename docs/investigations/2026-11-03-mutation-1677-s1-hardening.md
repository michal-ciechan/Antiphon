# CARD-1677 S1 hardening: mutation sweep (task cde14651)

Waived ordinary-worktree sweep, method-scoped, **not CARD-0478 evidence**. Subject: master
`97e77f9f1fd6f71e2719bc661b48f8f89f129029` (S1 hardening; code originally 58a2a23e9). Run on server2
(Linux), local inherited children only. Evidence root (gitignored, on the runner):
`/work/worktrees/task-cde14651/.antiphon/mutation-cde14651/` (per-mutant logs, TRX, `mutation.diff`,
`pre-mutation.sha256`, `restoration.json`).

## Method

All mutants env-gated (`ANTIPHON_M1677=<id>[,<id>]`) in one mutated tree; one build per test project and one
build-slot lease per build and per run. Three scratch probe methods (marked NEVER COMMIT) were compiled into the
same build to confirm that the extra mutants are live; they were removed on restore.

| Lane | Filter | Baseline (empty env) | Restored green (after `git checkout --`, rebuild) |
|---|---|---|---|
| runner | `/*/*/(C1677RetentionNativeProcessTests*)\|(C1677RetentionEngineTests*)/(End_suffixed_*)\|(Ignored_content_with_an_end_suffix*)\|(Residue_*)\|(Probe_*)` | 26/26 passed (21 committed + 5 probe cases) | 21/21 passed |
| server | `/*/*/(C1677RetentionServerTests*)/(Each_ineligible_task_alone*)\|(Control_character_inventory*)\|(Unmatched_inventory*)\|(Probe_*)` | 30/30 passed (29 + 1 probe) | 29/29 passed |

After restore: the sha256 of all 6 files matched the pre-mutation values, `M1677Probe` and `Probe_*` were absent from the
rebuilt dlls, HEAD was `97e77f9f1` and the tracked tree was clean.

## Results

Committed tests are the tests on master. "Probe" means only my scratch witness killed the mutant.

| ID | Location (at 97e77f9f1) | Mutation | Result (committed tests) | Killing test / assertion |
|---|---|---|---|---|
| PC-A | `src/Antiphon.SessionRunner/RunnerWorkspaceRetention.cs:431` | residue pattern `\z` → `$` | **SURVIVES (equivalent alone)** | none: the per-name control-character check at :439 runs first |
| PC-B | `RunnerWorkspaceRetention.cs:218` | remove request-path control-character check (Confine) | killed 3/26 | `End_suffixed_task_leaf_is_kept(lf\|cr\|ls)`: `Reason` should be `name_control_character` but was `outside_scope`. This is a reason-code kill only: the tree is still kept by `LeafPattern \z` and the exact-leaf compare |
| PC-A+B (brief's pair) | :431 + :218 | both | killed 3/26, **the same 3 leaf cases only** | the newline **residue** case stays green: the request-path check does not guard residue names. This pair is not the exposure |
| PC-A+R (the real exposure) | :431 + :439 | `$` back + remove the **retiring-entry** control-character check | killed 4/26 | `End_suffixed_retiring_residue_is_kept(lf, Execute)`: `Verdict` should be `kept` but was **`removed`** (a real registered worktree named `task-a1677001.0badf00d\n` was deleted); `(lf, DryRun)` was `would_remove`; `(cr)`/`(ls)` gave reason `workspace_retiring_or_unknown` |
| R (alone) | :439 | remove the retiring-entry control-character check | killed 4/26 | same 4 residue cases, reason code only (`workspace_retiring_or_unknown`): `\z` keeps them |
| PC-C | :442 | remove the not-`<leaf>.<8 hex>` doubt (non-exact entries fall through) | killed **2/21 committed** (+2 probe) | `End_suffixed_retiring_residue_is_kept(space, Execute)`: `Verdict` should be `kept` but was **`removed`**; `(space, DryRun)` was `would_remove`. The lf/cr/ls cases stay green because :439 catches them. The brief's "6/6" reading does not reproduce here: 2 of the 6 residue cases kill the mutant |
| PC-D | `src/Antiphon.SessionRunner/RunnerWorkspaceIgnoredContent.cs:42` | remove classifier control-character check | killed **1/7** committed classifier cases (+3 probe) | `Ignored_content_with_an_end_suffix_is_never_disposable(src/obj/cache.dat, \t)`: `Protected` but was `Disposable` (`**`→`.*` matches TAB). The other 6 suffix cases are covered by `\z` alone |
| PC-E | `server/Application/Services/RunnerWorktreeRetentionService.cs:350` | remove server mirror control-character check | killed 3/30 | `Each_ineligible_task_alone_is_recorded_and_never_sent(leaf-newline\|leaf-carriage-return\|leaf-line-separator)`: `row.ReasonCode` should be `name_control_character` but was `outside_scope`. Reason-code kill only: the task is still never sent |
| PC-F | `RunnerWorktreeRetentionService.cs:471` | remove server inventory control-character check | killed 1/30 | `Control_character_inventory_names_are_reported_not_sent`: `rows` should all be `name_control_character` with no task and no branch (it got the owner-hinted `identity_unknown` rows). The inventory path never sends in either case |
| PC-G | `src/Antiphon.SessionRunner.Contracts/WorkspaceRetentionContracts.cs:62` | drop U+2028/U+2029 from `IsControl` | runner killed 2 (+1 probe); server killed 2 | runner: `End_suffixed_task_leaf_is_kept(ls)` and `End_suffixed_retiring_residue_is_kept(ls, Execute)`, reason `name_control_character` expected. Server: `Each_ineligible…(leaf-line-separator)` (`outside_scope`) and `Control_character_inventory_names…` (rows). **Classifier U+2028 case `Ignored_content…(.antiphon/inbox/brief.md,  )` survives under G** (`\z` + `*.md` already reject it). Only the probe with an embedded U+2028 kills G in the classifier |
| XR (extra, runner) | `RunnerWorkspaceRetention.cs:437-443` | return the first exact `<leaf>.<8 hex>` directory before the remaining entries are checked (a stray non-exact `<leaf>.*` that sorts after it no longer keeps the residue) | **SURVIVES** (0 committed kills) | probe only: stray `task-a1677001.0badf00d-stray` next to the legitimate `task-a1677001.0badf00d` → `Verdict` should be `kept` but was **`removed`** (Execute) / `would_remove` (DryRun) |
| XC (extra, classifier) | `RunnerWorkspaceIgnoredContent.cs:42` | control-character check looks only at the last character | **SURVIVES** (0 committed kills) | probe only: `.antiphon/inbox/br\nief.md`, `.antiphon/inbox/br ief.md` (`*`→`[^/]*` matches them) and `bin/ca\tche.dat` were all classified `Disposable` instead of `Protected` |
| XS (extra, server) | `RunnerWorktreeRetentionService.cs:468` | `known.Contains(entry.Name.TrimEnd(' '))` | **SURVIVES** (0 committed kills) | probe only: the inventory name `task-<id8> ` (trailing space) of a known task was silently skipped (0 rows, expected one `identity_unknown` row). This is a reporting gap only; the inventory lane never sends |

## Headline: survivors and thin kills in the deletion guards

1. **XR (survivor, deletion).** No committed test puts a stray non-exact `<leaf>.*` entry beside a legitimate
   residue. Under XR the legitimate residue is removed even though a doubt entry exists. Missing test (shape of the
   probe): `Residue_retry_keeps_a_legitimate_residue_beside_a_stray_entry(Execute|DryRun)` in
   `C1677RetentionEngineTests`. The stray `<leaf>.<8hex>-stray` must sort after the residue. Assert `Kept` and
   `workspace_retiring_or_unknown`, that the residue and its tree remain, and that no `worktree remove` or other mutating verb ran.
2. **XC (survivor, deletion of ignored content).** Every committed classifier case puts the control character at the
   end. A check limited to a suffix survives, and embedded control characters inside a `*` segment (or a TAB under
   `**`) become `Disposable`. Missing test: `Ignored_content_with_an_embedded_control_character_is_never_disposable`
   (`br\nief.md`, `br ief.md` under `.antiphon/inbox/`, `bin/ca\tche.dat`). The same suffix-only shape covers all
   runner and server witnesses. A shared-helper mutant of `HasControlCharacter` that checks only the last character
   would survive everywhere for the same reason. Not run: XC is the classifier-local instance.
3. **PC-A + R (the real `$` exposure)** is killed only by the `(lf, *)` residue cases, through the
   retiring-entry check at :439, not the request-path check named in the brief's PC-A+B. PC-A alone is equivalent.
4. **Thin kills:** PC-C is killed only by the two `space` residue cases (it deletes under them), and PC-D by a single
   case (`src/obj/cache.dat` + TAB). One fixture edit would leave either unguarded.
5. PC-B, PC-E, PC-F and most of PC-G are kills on the reason code alone. Under each of them the name is still refused
   by a second guard (`\z` pattern or exact-leaf compare), so they are not deletion exposures.

No unmutated-L defects, no invalid mutants (all compiled; each extra mutant was proven live by its probe), and no
infrastructure failures (no slot timeouts, no build errors, every run had a nonzero count).
