# CARD-1518 re-cut and delivery fixture repair

Original Code / landing owner: 9d2b827a-8368-454d-90dc-35c019db4359.
Replacement source task: 22c598bd-cb80-4f27-939e-a76c5073f806.
Branch: feat/card-task-22c598bd.
Worktree: /work/worktrees/task-22c598bd.
Assigned base: 410738a522585e07943cabf23c1243790773d153.
Plan: docs/superpowers/plans/2026-10-23-card-1518-permission-wait-classification-plan.md.
Mutation candidate: docs/superpowers/mutations/2026-10-23-card-1518-provider-wait.md.

## Source and repair

Replayed all 21 non-plan commits after bb54a1190 through
612a38aa022d04beda01950b879015d7d1e7f871, committing and pushing each one.
There were no conflicts. The corrected branch plan blob is identical.
At re-cut tip 6dd2ef6d0ea5063260253f75fcf12edac6ed2280 every touched non-doc
blob matches the original tip: 24 files, 1818 insertions and 19 deletions on both
sides. Unrelated newer master changes are excluded by the original touched-path
set. Exact diffstats and the empty blob delta are retained in
.antiphon/evidence/c1518-recut/equivalence.txt.

Repair commit: 66429114dcee2d6e863ba71874fa196d91a81f14.
Only PermissionWaitFixture.cs and PermissionWaitDeliveryTests.cs changed after
that equivalence point. The fixture drives registered 20/50 ms delivery timers,
following TurnEndGapDeliveryHarness.JoinDeliveryAsync, and joins the operation.
The 60-second test cancellation budgets, complete transcript UserPrompt assertions,
false-receipt cases, queue cardinality and later failure delivery remain unchanged.
No product code changed in the repair, and no retry was added.

## Historical red witness

Run 20261023-072738-7815 tested the unmodified re-cut tip. Fresh TRX has exactly
four PermissionWaitTests methods: V-1 and V-6 passed; V-7 and V-8 failed after
60.1453207s and 60.1781833s in SettlePostEvidenceAsync at its manual-clock delay.
This is the requested fixture-failure witness, not a positive-control product red.
The source was clean and the build provenance verified. Run wall 6m03s;
build 230.040092s; test-host startup 57.0993381s; tests wall 60.1781903s;
host wall 118.6084452s. Build and row slot=granted, waited=0s.

CHECKPOINT CP-3 commit=6dd2ef6d0ea5063260253f75fcf12edac6ed2280 build=ok filter=/*/*/PermissionWaitTests/(C1518_CapturedDialogNeedsDecisionBeforeStall*)|(C1518_EpisodeAndNoteCommitOnce*)|(C1518_DecisionReachesBusyAndIdleCaller*)|(C1518_NoteRecoveryRetainsOneQueueRow*) executed=4 passed=2 failed=2 skipped=0 trx=/work/worktrees/task-22c598bd/.antiphon/checkpoints/20261023-072738-7815/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=6dd2ef6d0ea5063260253f75fcf12edac6ed2280 sourceState=clean buildSource=verified

## Final qualification boundary

This report is committed before final qualification. The final pushed report tip
is the expected source SHA for one serial checkpoint-tool run selecting CP-2..CP-8.
CP-2 supplies one shared .NET build; CP-3..CP-7 reuse it. CP-8 runs the focused
client wrapper including typecheck and lint. No Unit lane, whole integration class,
namespace, assembly or native provider run is added: the specific brief and closed
plan table define this Final round. V-1..V-13 and R-1..R-4 remain pending at this
report commit until their actual receipts arrive.

The final source SHA, unedited CHECKPOINT lines, per-method fresh TRX roster,
receipt validator result, full base..HEAD evidence guard and final merge-tree
result will be recorded in the ignored stored report .antiphon/task-22c598bd.md.
Generated payloads stay under .antiphon/checkpoints/ and
.antiphon/evidence/c1518-recut/. This tracked narrative does not predict green.

Setup outside the checkpoint list: one gated checkpoint-tool bootstrap build
through scripts/build-slot.ps1, OutputPath=bin-c1518-tool/. Its console output was
not retained; the resulting tool launched the recorded runs. A first launcher
invocation had a mistyped expected SHA and refused source_mismatch before any
build/test. The red run is the explicitly required pre-repair CP-3 witness.
No unchanged-proof repetitions or loaded runs are commissioned. Owned green
output was deleted after the terminal red run to avoid pre-existing-stamp refusal.

## Release and remaining obligations

Restart: server. RC prerequisites: CARD-0680 pick 89c1cdaa4 first, then the
original chain commits below in order (the rc takes original commits, not this
re-cut). Activation requires a server restart on the approved rc and a matching client
bundle. Compatible active CARD-0680 runners and applicable qualification remain
prerequisites; upgrade/restart that dependency if its active runner lacks support.
The runner catalogue/defaults were read: default server2 revision 2; eligible Linux
and Windows entries, no claudeDialogGuardV1 advertised. No desktop, Windows
qualification, supervisor 209072, live modal, restart or deployment was touched.
The read-only natural/disposable modal observation belongs to the release operation.

Every PC-1..PC-17 and every individually named variant in the unchanged mutation
candidate is pending for method-scoped SourceLanding Mutation, including missing
control discovery. Ordinary verification does not discharge any PC. After green,
next is independent Final/Full Review of this replacement source, then the caller
lands through the original Code owner and commissions SourceLanding Mutation.

## Original chain (RC order)

bf83da60c7c435abbc208849ecf241d142cb8287 test(card-1518): add S1 permission-wait incident red witnesses
0833bcc7da66e04c09b1c9f3b32a19d85e470ff1 test(card-1518): import existing queue mode contract
ffc1c8877be2c306eae1bdf6531c6ca2a4c3e619 test(card-1518): bind S1 filter to pinned TUnit discovery
c2a3bb99358ed12ef9dd099aeaebc246e7cb0b3d test(card-1518): add S2 observation fences and unchanged-clock witnesses
571a29c8545756cd5a63ce0abaddc289f2fb786c feat(card-1518): bind diagnostic observations and preserve deadline clocks
f4d41933344cfeca250d1268826abc0c8e9a4368 test(card-1518): pin episode persistence and queued decision receipts
6e346d9dab1b5f8ed961a522ea150baa53472752 feat(card-1518): persist early provider decisions through the existing outbox
7536b14e9549bf5899d7ffb4d146a07db0fb11e8 test(card-1518): add surface custody and bounded-cadence witnesses
2816290744d02e3303487b12a4682f5cedc7dd9f feat(card-1518): expose current provider decisions in Check and attention
126c1e78ee3ad07d4b87a6cfb64b845ea125e755 test(card-1518): repair real delivery and child fixtures; pin receipt debt and operator recovery
7310e6e45b619a900ba1147049231802ff912849 test(card-1518): cover absent tails queues runner bindings and final-read fallback
e262857f958e8691ffd377569c3b4e9f11dd857e fix(card-1518): retain generation-bound debt and reject stale deadline facts
907d6c11313e230e72d3d1e48826d0a59a92c52a test(card-1518): exercise same-facts stall Check and later failure delivery
54757da4dd4359aff9e12b6e76f686a9af6175ff fix(card-1518): reject mismatched recipient rows and pin restart receipt negatives
c5c6c94206e5b6b4dc4e690c75ad9fa727e9ed45 test(card-1518): exercise unavailable destinations and lost-wakeup recovery
feb1bad5b0a4025eb199314a8a3b5c9815faae63 docs(card-1518): bind final verification and pending Mutation handoff
f35bb1272b2598952eab2cdaa92e971bd2a37203 test(card-1518): pin Check clock after bounded observation and unavailable diagnosis
f85d563e070bc609410ec0082716d70d0017d0d7 fix(card-1518): evaluate Check after completed observation and bound catch-up cadence
f19513376c4bd9b338164b1e3dd97bd5a7fa2b6c Fix receipt negative fixture expression and serialize qualification
52243fc8fe67074362a7322709cb7c41a395ce9f Repair stored-state delivery fixtures and complete ordinary boundary controls
612a38aa022d04beda01950b879015d7d1e7f871 Await terminal guidance visibility within the existing client bound

## Re-cut / repair commits

a0186640d49a7b464047b7b038da8713bf98123b test(card-1518): add S1 permission-wait incident red witnesses
43d65a4cfe58699f40042a371d32868fcb5ac78e test(card-1518): import existing queue mode contract
d8dca32eb4a11eefc94ab3d520ed5bc6133eb336 test(card-1518): bind S1 filter to pinned TUnit discovery
a5da690ad20ae39a8530b53331e222dfe193f281 test(card-1518): add S2 observation fences and unchanged-clock witnesses
892c1a6850153318a2c62f2f1f6025ea401249ee feat(card-1518): bind diagnostic observations and preserve deadline clocks
4806dad0bd24a7e416305b4b228bf9e81414c6e5 test(card-1518): pin episode persistence and queued decision receipts
6c08141d5952a202f34f59e6ec8ab6c859a02628 feat(card-1518): persist early provider decisions through the existing outbox
644de949412cc7a479a1c6e128cabfb240e0092d test(card-1518): add surface custody and bounded-cadence witnesses
9e6a40e86fbbf2d25f4f94291355f63a3d9fa22c feat(card-1518): expose current provider decisions in Check and attention
988c071cfdf23e2ceccd006cee4d496f45f85457 test(card-1518): repair real delivery and child fixtures; pin receipt debt and operator recovery
a7edef00f9517b442df203f7a38e95afbc9242c9 test(card-1518): cover absent tails queues runner bindings and final-read fallback
f0e6f34d38bd96c546f8fd71c929f70fa04ca8df fix(card-1518): retain generation-bound debt and reject stale deadline facts
d254e6c335650435582f777d1f7596f0ebfca5d1 test(card-1518): exercise same-facts stall Check and later failure delivery
fd8b491ecb08fa8ff497bf3da0c67080fd41208e fix(card-1518): reject mismatched recipient rows and pin restart receipt negatives
bd78d375b0c371651bebfc5bf4415d28132dc91e test(card-1518): exercise unavailable destinations and lost-wakeup recovery
2dd7640e848b837b50ccdcbf5d78be22b800c938 docs(card-1518): bind final verification and pending Mutation handoff
5f8c4e9f3a88bc3b494885edcda856c1da300be7 test(card-1518): pin Check clock after bounded observation and unavailable diagnosis
da57879ee15fe61442910a8bb817d93b7cffdf45 fix(card-1518): evaluate Check after completed observation and bound catch-up cadence
c81c501bd6b1e2fbf78d9f98e354a06e44fc8a7f Fix receipt negative fixture expression and serialize qualification
dfe06b748a30aef71f8e769da10d28355107430c Repair stored-state delivery fixtures and complete ordinary boundary controls
6dd2ef6d0ea5063260253f75fcf12edac6ed2280 Await terminal guidance visibility within the existing client bound
66429114dcee2d6e863ba71874fa196d91a81f14 test(card-1518): drive registered delivery timers in permission wait fixtures

## First full admission

Run 20261023-073603-a970 at 6f2e80c808196c46311324ad0a1d0b7b7428f65e
finished in 3m03s. CP-8 passed typecheck, lint and its one focused test. The
.NET build was refused before compilation because npm installed two hard-linked
esbuild executables. CP-2..CP-7 executed zero tests; this is not product/test red.
All obtained slots were granted with waited=0s. After terminal exit 2, replaced
only those generated executables with byte-identical regular single-link copies:

client/node_modules/@esbuild/linux-x64/bin/esbuild links=1 sha256=c638273fcf95573ca74af586677800b4dd874c55f8f945adb54316d6902ba14b
client/node_modules/esbuild/bin/esbuild links=1 sha256=c638273fcf95573ca74af586677800b4dd874c55f8f945adb54316d6902ba14b

The refused output directory was removed. The next run selects CP-2..CP-8 again
at this report commit, with unchanged deadlines and the same fixture repair.
