# CARD-0680 master re-cut evidence

Code task `f46a522f-0200-4181-a091-26615d5cd194`, branch
`feat/card-task-f46a522f`, worktree `/work/worktrees/task-f46a522f`.
Original Code/landing owner: `52a5dc76-feb4-4349-acaa-e3ca459b918c`.
Base: `2a22d9b9855424f5d2758e329f3fced39f096df1`.
Reviewed source: `e85f98d18179a0f0c4f02f4757516b8dda1fb451`.
Review `821380f3-09da-4431-8262-c9aa3da9463e` reported Clean Final;
its stored API Result was read. The referenced Code report is absent from the
reviewed Git tree; its stored task Result and committed evidence were read.

## Re-cut and equivalence

All eleven original commits were cherry-picked in order and each was pushed.
Only `docs/ops-http.md` and `docs/session-runtime-invariants.md` conflicted.
For both, exact string comparison proves the result is the complete master file
followed by the exact original reviewed append. Phone-home CARD-1523/1524 appears
first; Claude dialog CARD-0680 follows. No section was rewritten.

| Original reviewed commit | Re-cut commit | Non-doc file patches |
|---|---|---:|
| fcb5f1275c880db42ef8723d195adbff423840e5 | fd99af52987778fe55fa76a84078d77447c2bf8e | 2 |
| 5a2b4dcacbc7ac472a756fdb287269e82b34245e | bd95d112e252ae318f361ff6d68b0876b5fdc9c0 | 7 |
| 0d745058827d9157e837771f53342e3dc3238c64 | a60f5d0104e8885ee113a491c27124812fdf2cee | 1 |
| 71ac36b0f29fdfe41e36f963e5e7aee7cb70a1e3 | f8e94986581d33a12770d36a2cc39c9b4d937658 | 0 |
| 9e12263b526d73a34b94063317a330cf938af52c | 2bf28d01b56ce7c4d673a7efd278f0bda63dd9a9 | 8 |
| 13b2a50c233f7cb66fba629095b67e99e57f00e7 | 09319c72d18b41224b7f668e404867a03553878f | 1 |
| 28829e18a4a156ce4182a79951c18cfc100c4408 | dd196f41aad217c6fd00d3dca82c396a91167bd5 | 35 |
| 8fc327d6b8dcd9a328c0b7625ff0fcf061a3a997 | f0a40cd4ccc55f8bc039d2df2d764dc66a7f0d5f | 1 |
| 040f6d0c05877e782ccd3158bba19930513fc286 | 493fd5fce56b225ddc2508c44a49ac6ba2cef870 | 1 |
| 8a459b64736e1942b361cf2e9b2e8fa4d5e6824b | b286ab2955eab967e8d4a460bc47fb4c0e8858da | 0 |
| e85f98d18179a0f0c4f02f4757516b8dda1fb451 | c5d0066ef33d089776819d7731ccfbba3707b899 | 2 |

All **58 non-doc per-file patches** have identical stable patch IDs when computed
with `git diff --binary --unified=0 <commit>^ <commit> -- <file> | git patch-id --stable`.
Zero context excludes unrelated master context: normal range-diff shows the newly
inherited `PhoneHomeEventWindowOptions.Capability` beside the unchanged insertion
in `PhoneHomeRuntimeAdapter.cs`. There is no changed implementation hunk.
The original plan was already present on master, so its initial addition is absent
from the re-cut; its content is retained. Other documentation patches are unchanged.

Before this report commit, `git merge-tree --write-tree origin/master HEAD` against
the base above succeeded with tree `e4565fb73937bc971c67439963189ee3d2b70901`.
The final pushed/tested tip will be checked again and recorded in the final report.

## Frozen verification scope

This report is committed **before** the final run. Results are not claimed here.
The final report at `/work/worktrees/task-f46a522f/.antiphon/task-f46a522f.md`
will preserve actual tested SHA, unedited CHECKPOINT lines, receipt validation,
fresh TRX method/count inspection, slot/wait costs and full base-to-HEAD evidence diff.
Generated receipts and logs stay ignored; no source edits are planned during runs.

The specific re-cut brief limits Final verification to CP-1 through CP-7 once:
one run for CP-1, one for CP-2/CP-3, one for CP-4/CP-5/CP-6/CP-7, hence exactly
one isolated build per run. Every row selects its original single method.
Use the committed plan, exact expected HEAD, serial scheduling, a six-minute
row/build ceiling and eight-minute total ceiling per run. The only extra build
is a build-slot-gated checkpoint-tool bootstrap, needed because this new worktree
has no tool binary. No Unit, assembly, namespace, native or mutation runs are authorized.
Live runner-defaults and session-runners were read: revision 2, eligible Linux
default, eligible Windows runner, unavailable temporary runner. Execution stays
in this assigned Linux mirror with no dispatch/placement override.

V-1/R-1 bind CP-1; V-2/R-2 bind CP-2/CP-3; V-3/R-3 bind CP-4/CP-5;
V-4/R-4 bind CP-5/CP-6/CP-7. Their actual outcomes await the final receipts.
V-5/R-5 (CP-8/CP-9, Windows and Unix live watchdog) remain OWED, as do V-6/R-6
(CP-10/CP-11, parent Q1 Windows and Unix Ctrl+C). No native activation is claimed.

Every PC-1 through PC-9 and every variant in the unchanged verification design
remains pending method-scoped SourceLanding Mutation. Review's exploratory
mutations and historical Code reds are retained history, not qualification.
The unbound candidate remains `docs/superpowers/mutations/2026-10-23-card-0680-candidate.md`.

## RC and handoff

RC must take the **original reviewed commits in the table's left column, in order**,
onto `origin/rc/2026-10-20` at `5a126f6fc5ae4e16d803b628716b8f0485a37174`.
Review measured resulting pick tip `89c1cdaa46f05c82c6b37c00310fc6b7ad199f43` and
three compiling projects with zero errors. This is carried Review evidence, not
a new RC execution. The master re-cut tip must **not** be picked onto RC.

Next is the requested light confirmation Review of the re-cut/equivalence and
two-document resolution. Landing stays with the original Code owner; caller
commissions SourceLanding Mutation after Review and landing. Restart performed:
none. Eventual activation requires server and runner rollout, owned separately.
The desktop, live Windows qualification supervisor and release directories are untouched.
