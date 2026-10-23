# CARD-1532 master re-cut evidence

The five reviewed Code commits were cherry-picked, in order and without conflicts,
onto the assigned master-based branch. Every stable patch ID matches. The re-cut
is published, but the brief's strict whole-file/suffix-only proof has two documented
exceptions requiring the caller's decision. No builds or tests were run.

- Re-cut task: `7d024505-625e-44e6-99cc-9fc8400480b8`.
- Original Code task / landing owner: `12dd82bc-d419-4247-af23-4ba6aff5cd04`.
- Branch: `feat/card-task-7d024505`.
- Worktree: `/work/worktrees/task-7d024505`.
- Task base / fetched master: `3d234eda960feb72b74db4c6b4fc7b1ccfa9cc97`.
- Reviewed original: `7235182cb61e1f99eb0e4fb911e501669c3d5240`.
- Original Code range base: `2c88d3c6b238c1155abd2bd261b50beb76d825b8`.
- Re-cut implementation tip: `8920f8e3999c5ea01cd1bb83ca120da3c72392ac`.
- Plan: `docs/superpowers/plans/2026-10-23-card-1532-streaming-source-stamps-plan.md`.

The final publication adds this evidence file to the implementation tip. Its full
SHA and final full-range guard/merge results are supplied in the task report.

## Commit and patch identity

`git log --reverse --no-merges 2c88d3c6b..7235182cb` returned exactly these five
non-plan commits. Each was pushed immediately after cherry-picking. There was no
rebase, merge, branch switch, amendment, reset or force-push.

| Reviewed commit | Re-cut commit | Shared `git patch-id --stable` |
|---|---|---|
| `d74768e062248d69b10c70dcc77367ae6e143c8e` | `b7b0ed20ad774f9ca50ed5cf5032b65f59c288f3` | `40191705c7ce4343c3f5b61811fcc307f4236206` |
| `d4791e97aeb210aad42dfc2cd09f85a235384726` | `01e28e2be1ec7592548e88e38589f69bab99292a` | `bb6ce48963d0bbadda922040c73d385411269bc3` |
| `7a7598535b0e1368bd10e983044cd192ba99c659` | `43deafeac31b1183e5a2a26152b2c3dd3f158953` | `ddd3927bb09d032eb565db3bd3bd8df2c7ed7cb5` |
| `0d9b2c32b502ef5248643018f24bb5f455f81fa3` | `725a402658833b7f47fa46f2628dd5eb72e3d451` | `7cd913f1d083fd009bd23483a4218f936b0d1b3d` |
| `7235182cb61e1f99eb0e4fb911e501669c3d5240` | `8920f8e3999c5ea01cd1bb83ca120da3c72392ac` | `9c684a065eb6a38ebf0f03405372d7768d375b56` |

## Equivalence and the two exceptions

All 13 branch-touched paths were checked against the reviewed tip. Twelve blobs
match exactly, including every production source, test, script, the mutation
candidate, the historical Code report and the plan. The thirteenth is
`docs/testing-and-build.md`:

```text
git diff --stat 7235182cb HEAD -- <branch-touched paths except plan>
 docs/testing-and-build.md | 107 ++++++++++++++++++++++++++++++++++++++++++++++
 1 file changed, 107 insertions(+)
```

That entire difference is the newer master `OS branch witnesses` section. Its
107 added lines exactly equal the delta for this path from the original Code
base to the assigned master base. CARD-1532's own 17 added/removed lines in this
document exactly equal the original reviewed delta. The newer section was
preserved; reverting master policy to manufacture blob equality is not done.
Reviewed document blob: `d9ccb6dcec657449cfbb54e77f76318f81e7a331`;
re-cut document blob: `7906ab48632e6b4d1920c58334e45d024f110a24`.

The global comparison excluding the plan also reports 48 unrelated paths changed
on master. Each was compared against the assigned base and is unchanged by this
task. This evidence file is the sole new report added after that comparison.

The plan is exactly the reviewed blob `f4e71a427b56d25a82f550ce2969bb4180b31470`.
However, `git diff HEAD origin/master -- <plan>` is **not** limited to wildcard
suffixes: besides the six suffixes in CP-1/CP-8, the reviewed blob adds an
eight-line qualification note and its separating blank line. It describes the
zero-count retry, pending Mutation variants and separately commissioned CP-9.
No other plan changes exist. The note was preserved to retain the reviewed blob
and exact patch IDs. The brief's two descriptions of the desired plan differ.

Decision requested: accept these preserved reviewed/master documentation
exceptions, or specify a plan-only adjustment. The question does not authorize
discarding the newer master section. The safe re-cut and its evidence are pushed
while that decision is pending; strict full equivalence is not claimed.

## Static checks and execution scope

Before this report commit, the evidence guard returned exit 0:

```text
EVIDENCE range base=3d234eda960feb72b74db4c6b4fc7b1ccfa9cc97 head=8920f8e3999c5ea01cd1bb83ca120da3c72392ac
EVIDENCE result commits=5 entries=0 violations=0 base=3d234eda960feb72b74db4c6b4fc7b1ccfa9cc97 head=8920f8e3999c5ea01cd1bb83ca120da3c72392ac
```

`git merge-tree --write-tree origin/master HEAD` returned exit 0, tree
`6809d4b950661011724eec83136d6d0591b204c3`; no conflicts. `git diff --check`
passed. The evidence guard will be run again over task base through the final
committed report, followed by a fresh master fetch, merge-tree and remote-tip check.

The task-specific brief expressly excludes all builds/tests. Consequently there
are no fresh TRX, CHECKPOINT lines, tested SHA or build provenance for this re-cut;
`slot=n/a`, `waited=0s` (no build slot requested). No proof repetitions, deliberate
mutants, alternate build outputs, desktop operations or daemon restarts occurred.
Restart: **none**. The protected Windows supervisor was not touched.

Both required runner API GETs succeeded on 2026-10-23 around 06:53 UTC. Defaults
revision 2 selected an eligible Linux runner; the catalogue also reported an
eligible Windows runner and an unavailable temporary runner. No runner was pinned
or dispatched by this task.

| IDs | This task's actual outcome | Handoff |
|---|---|---|
| CP-1, CP-2 | Not run; historical red-first work preserved | Do not repeat for this re-cut |
| CP-3, V-1, V-2, V-3, V-4, V-5, V-6 | Not run by explicit brief | Confirmation Review, six results |
| CP-4, V-7, V-8, V-9 | Not run by explicit brief | Confirmation Review, three results |
| CP-5, R-1 | Not run by explicit brief | Confirmation Review, ten results |
| CP-6, R-2 | Not run by explicit brief | Confirmation Review, one result |
| CP-7, R-3 | Not run by explicit brief | Confirmation Review on Linux, ten results |
| CP-8, R-4 | Not run by explicit brief | Confirmation Review, two results |
| CP-9, V-10 | Not run; Windows execution remains pending in inherited plan | Separate authorized Windows parser qualification; never infer from PS7 |

The brief reports Final Review `2e4feb7c` CLEAN at the original reviewed SHA,
receipt `20261023-061739-b3d0`, CP-3..CP-8 counts `6/3/10/1/10/2` (32). These are
attributed historical results, not independently revalidated receipts or fresh
proof for this re-cut. The old `.antiphon/task-12dd82bc.md` is absent from the
reviewed Git tree; its tracked historical report was preserved unchanged at
`docs/superpowers/reports/2026-10-23-card-1532-code.md`.

All 36 Mutation variants remain pending: PC-1; PC-2a, PC-2b; PC-3a, PC-3b,
PC-3c; PC-4a, PC-4b; PC-5a, PC-5b, PC-5c, PC-5d; PC-6a, PC-6b, PC-6c, PC-6d,
PC-6e, PC-6f, PC-6g, PC-6h; PC-7a, PC-7b, PC-7c; PC-8a, PC-8b, PC-8c, PC-8d,
PC-8e, PC-8f, PC-8g, PC-8h, PC-8i; PC-9a, PC-9b; PC-10a, PC-10b.
Unbound candidate: `docs/superpowers/mutations/2026-10-23-card-1532-streaming-stamps.md`.
Only later method-scoped SourceLanding Mutation owns red/restore/green and
missing-control discovery.

## Confirmation Review and rc

After the documentation decision, commission the brief's light confirmation
Review of the exact final pushed SHA. It runs CP-3..CP-8 through the checkpoint
tool with `--expected-source-sha <full-final-sha>`, waits to terminal status,
checks fresh class/method counts and clean receipts, and retains any remaining
Windows gate. No existing receipt is relabeled to the new SHA.

For rc, take the **original five reviewed commits in the table's first column,
in that order**, dropping the plan-markdown change because rc lacks that plan.
Do not port the re-cut commits, the inherited newer master policy, or this report.
No rc publication or landing was performed here. Keep the original Code task
as landing owner; commission SourceLanding Mutation after Review and landing.
