# CARD-1032 S7 F1-F3 repair Code evidence

F1/F2 report-loss paths repaired; F3 and the S7 safety-language sweep corrected.
All nine required scoped checkpoint rows have clean, SHA-validated green evidence.
Production remains inert. Next: Review, then caller-owned land and commissioned
SourceLanding Mutation. Restart: none.

Code / landing owner: 60025819-39c5-49e6-8951-817cdeeff5fc.
Previous Code owner: f2b2ece7-37bf-4c83-9b74-cfb9e78c1eae.
Branch: feat/card-task-60025819. Worktree: /work/worktrees/task-60025819.
Repair base: cfd114a6d2b64a19cf3d4f8f62eda2ac03172a1c.
Full task base: 63b2e70479c846cacd01af6b20190e845f8258e4.
Commits were added and pushed without rebase, amend, reset or force push:
c82b985895892533d517521e8efc138a603e0a8c (tests first),
3cbd00d37e60b2d7d937e62e721db8215ee8469e (production repair),
78fcc1e2f346fb797ee042fdb97b5338a23af5de (corrected S5 fixture).
The containing closure commit changes Markdown only; actual tested SHAs below
are not relabeled to that documentation tip.

Plan: docs/superpowers/plans/2026-10-11-card-1032-runner-mirror-cleanup-plan.md.
Design: docs/superpowers/plans/2026-10-12-card-1032-test-design.md.
Closed repair selection: docs/superpowers/plans/2026-10-13-card-1032-s7-repair-verification.md.
Review findings: /tmp/antiphon-review-e0b648d4/review.md.
Mutation candidate: docs/superpowers/mutations/2026-10-13-card-1032-s7-repair-candidate.md.

## Implementation and safety-language sweep

CompletedCardWorkspaceReport.IsTaskReportPath is the shared case-insensitive
predicate used by the producer, runner classifier and fresh recipient inventory
hold. It retains the runner's conservative eight-character wildcard, including
nonhex identifiers and the old regex's single terminal-LF match. A directory
entry is not a regular report file. Literal ADS/dot/space/Unicode-lookalike shapes
are pinned without claiming Windows physical filename qualification.

The command carries no per-report byte manifest. The recipient therefore refuses
recognized report files with artifact_unpreserved in fresh S5 no-follow inventories
before prepare, rename, unregister and delete, including replay. Existing S5
identity/content checks retain their later-change refusals. No atomic filesystem
snapshot against arbitrary writers is claimed. No assertion or timeout was loosened.

Sweep scope: full 63b2e704..cfd114a6d S7 diff, comments, docs and test names,
including contextual containment comments. Changed safety sentences:

- Producer: remote names are not remote report bytes; recipient repeats the hold.
- Recipient proof: direct H/P ancestry versus signed H-to-P authority plus P ancestry.
- Producer publication comment: at or after, matching the actual >= comparisons.
- AdmissionArtifactSurvival comment: names the actual missing/changed/internal pointer cases.
- AdmissionIntentOrdering comment: reopen before intent prevents issuance; after intent permits the issued command.
- Historical Code evidence: removes the disproved blanket remote-report hold claim and records F1/F2.
- Historical Final selection: corrects the report claim, H/P wording and superseded Unit requirement.

Other all/every/never/always claims were checked against the code/recorded scope:
complete typed receipt comparison, default-inert composition, no legacy fallback,
pending PCs and historical counts. Their verified wording and unedited historical
CHECKPOINT lines were retained. No test name needed a semantic rename.

The existing S5 positive incorrectly wrote unpreserved task-deadbeef.md bytes and
expected disposal. Its physical-removal, receipt, ref and sentinel assertions
are unchanged; the eligible positive now uses complete.trx. Separate lower,
upper and mixed report HOLD vectors retain bytes and registration and forbid
set-aside/receipt. This preserves real eligible cleanup without expecting report loss.

Manual composition inspection found no Program registration, residue sweep
caller, public cleanup route or Execute/cutoff change. No session input/notification
flow changed, so no new UserPrompt acceptance was required. No live path was removed.
Required runner-defaults and session-runners GETs succeeded; no runner/platform pin.

## Ordinary verification and disclosures

| Row | Actual outcome | Tested source |
|---|---|---|
| CP-13 | 7/7 passed | 3cbd00d37e60b2d7d937e62e721db8215ee8469e |
| CP-14 | 7/7 passed | 3cbd00d37e60b2d7d937e62e721db8215ee8469e |
| CP-F2 | 59/59 passed, three full server classes | 3cbd00d37e60b2d7d937e62e721db8215ee8469e |
| CP-RG1 | 1/1 passed | 3cbd00d37e60b2d7d937e62e721db8215ee8469e |
| CP-30 | 1/1 passed | 3cbd00d37e60b2d7d937e62e721db8215ee8469e |
| CP-31 | 1/1 passed | 3cbd00d37e60b2d7d937e62e721db8215ee8469e |
| CP-29 | 1/1 passed on required rerun | 78fcc1e2f346fb797ee042fdb97b5338a23af5de |
| CP-32 | 1/1 passed | 78fcc1e2f346fb797ee042fdb97b5338a23af5de |
| CP-F3 | 144/144 passed, four full runner classes | 78fcc1e2f346fb797ee042fdb97b5338a23af5de |

222 green native executions across the required rows, including their deliberate
full-class overlap; not 222 unique methods. Zero failed/skipped in these qualified
rows. Fresh TRX class/method census: server classes 17/6/36, runner classes 97/3/33/11,
registry 1. CP-13 and CP-14 each contain the exact seven intended methods. The
new table has 24 literal shapes and six real report files; the late-report method
has three real gated arrivals. These internal vectors are not native test counts.

V-1 PASS: scoped endpoint/binding/generation admission, CP-13/F2.
V-2 PASS: complete command/receipt and unsupported peer, CP-14/F3.
V-3 PASS: real rebase/containment and report preservation, CP-13/29/30/31/32/F3.
V-4 adjacent PASS: existing ownership/fence runner regressions, CP-F3; no new native qualification.
V-5 adjacent PASS: eligible physical disposal/component recovery, CP-32/F3; S8 cuts deferred.
V-6 PASS: intent before actual socket send, gate and fresh receipt read, CP-13/14/30/F2.
R-1 PASS at adjacent scope: local/legacy full classes, CP-F2/F3.
R-2 PASS at adjacent scope: legacy runner/contract full classes, CP-F3.
R-3 PASS: mirror admission/receipt and recipient compatibility, CP-13/14/29..32/F2/F3.
These scoped outcomes do not certify the unimplemented S8 or native Windows work.

Tests-first production was unchanged from cfd114a6d at c82b9858. CP-29 and CP-30
each executed one and failed on actual missing report-file survival:
`report bytes must survive: TASK-DEADBEEF.MD` and
`late report bytes must survive: task-deadbeef.md`. CP-31 passed real cleanup.
This is ordinary defect red, not deliberate-mutant evidence.

First repaired Final run at 3cbd00d3 remained RED, 221 executed/219 passed/2 failed:
CP-29 failed in unchanged fixture InitializeAsync at saved.Succeeded, before its
cleanup oracle; its full-class execution in CP-F2 passed. Exact pre-repair cfd114a6d
CP-BS1 exercised the same fixture six times and passed. Therefore that setup failure
was not reproduced and is NOT claimed inherited, flaky or a guard red. No budget,
retry or assertion change was used. The required CP-29 rerun passed at 78fcc1e2.
CP-F3's sole failure was the obsolete S5 positive expecting task-report disposal;
the explicit fixture correction above resolved it. CP-BR1 passed the original
positive at cfd114a6d, independently confirming that prior expectation.

Green qualification: validate-checkpoint-receipt.ps1 returned exit 0 for the six
selected green rows at 3cbd00d3, all three rerun rows at 78fcc1e2, and both base
diagnostics at cfd114a6d. The first repaired run's overall RED is not relabeled.
All used committed expected SHA, dirty=0, sourceState=clean, buildSource=verified.
All builds/rows: slot=granted, waited=0s. No unleased driver or timeout widening.

One leased tool bootstrap was required outside the checkpoint table:
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c60025819-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c60025819-tool/ --property:UseAppHost=false -nodeReuse:false`.
It passed with the existing CS8602 warning. Named test projects require separate
isolated builds; server rows reuse one output, runner rows another. Linux effective
UseAppHost=false and forward-slash OutputPath were verified. Existing analyzer/
nullability warnings remained in build logs; zero build errors.

Extra base diagnostics were declared in /work/evidence/task-60025819/base-probe.md
and run through the same checkpoint tool/slot gate, solely to check the setup
failure and obsolete physical positive at exact pre-repair source. No broad run.
No whole Unit lane: the repair brief explicitly excludes it. Historical Unit
timeout/census red remains disclosed in the original evidence, not passed here.
No full assembly/namespace selection. No unchanged proof selection exceeds three
normal executions, including required full-class overlap; zero loaded trials.

Rerun recipe: bootstrap as above if needed, then invoke the compiled checkpoint
DLL with `run --plan docs/superpowers/plans/2026-10-13-card-1032-s7-repair-verification.md --expected-source-sha <committed HEAD> --serial --max-wait 50s`.
Await every exit 75 with `wait --run <run-id> --max-wait 50s`. The final repair
rerun used `--rows CP-29,CP-32,CP-F3`; no other passed row was repeated afterward.

## Deferred and pending

Post-land SourceLanding Mutation was NOT run. Every PC and named variant in the
plan/design remains pending, including the nine certificate variants of PC-588,
all 27 missing-receipt fields of PC-589, safe-read PC-590, and PC-591..593 above.
Mutation owns deliberate red/restore/green and per-boundary missing-control discovery.
The appended PC census enumerates all IDs cited by the plan/design/selections;
all named variants remain as specified in those artifacts, without waiver.

Deferred-to-native-Final: W-S5-file-identity, W-S5-file-sharing/content,
W-S5-root-identity, W-artifact-reader-junction/sharing, native report filename
normalization. Existing exact late-file/content/root and Windows artifact-reader
methods remain pending. S8 CP-15/16, queue/crash/server-composition cuts and
activation remain with their original owners. None is reported passed here.

## Evidence provenance

Unedited CHECKPOINT lines, PC census and closing checks follow. Generated TRX,
JSON receipts and logs remain ignored at their recorded paths; only this individual
Markdown report is published. Census files:
/work/evidence/task-60025819/trx-census-initial.txt and trx-census-final.txt.

Receipt source: /work/worktrees/task-60025819/.antiphon/checkpoints/20261013-161126-cd9a/report.md

```text
CHECKPOINT CP-29 commit=c82b985895892533d517521e8efc138a603e0a8c build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportRecognitionCaseTable* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-161126-cd9a/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=c82b985895892533d517521e8efc138a603e0a8c sourceState=clean buildSource=verified
CHECKPOINT CP-30 commit=c82b985895892533d517521e8efc138a603e0a8c build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAddedAfterProducerInspection* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-161126-cd9a/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=c82b985895892533d517521e8efc138a603e0a8c sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=c82b985895892533d517521e8efc138a603e0a8c build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAbsentAllowsCleanup* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-161126-cd9a/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=c82b985895892533d517521e8efc138a603e0a8c sourceState=clean buildSource=verified
```

Receipt source: /work/worktrees/task-60025819/.antiphon/checkpoints/20261013-162014-d880/report.md

```text
CHECKPOINT CP-29 commit=3cbd00d37e60b2d7d937e62e721db8215ee8469e build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportRecognitionCaseTable* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-162014-d880/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=3cbd00d37e60b2d7d937e62e721db8215ee8469e sourceState=clean buildSource=verified
CHECKPOINT CP-30 commit=3cbd00d37e60b2d7d937e62e721db8215ee8469e build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAddedAfterProducerInspection* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-162014-d880/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=3cbd00d37e60b2d7d937e62e721db8215ee8469e sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=3cbd00d37e60b2d7d937e62e721db8215ee8469e build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportAbsentAllowsCleanup* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-162014-d880/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=3cbd00d37e60b2d7d937e62e721db8215ee8469e sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=3cbd00d37e60b2d7d937e62e721db8215ee8469e build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)|(C1017_ReopenBetweenEndpoints)|(C1017_MultipleMirrorsRemainDistinct)|(C1032_Admission*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-162014-d880/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=3cbd00d37e60b2d7d937e62e721db8215ee8469e sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=3cbd00d37e60b2d7d937e62e721db8215ee8469e build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation)|(C1017_ReceiptMustMatchEndpoint)|(C1017_ReceiptMustMatchStore)|(C1017_ReceiptMustMatchTaskAttempt)|(C1017_AckIsNotRemoval)|(C1017_UnsupportedNeverFallsBack)|(C1032_Receipt*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-162014-d880/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=3cbd00d37e60b2d7d937e62e721db8215ee8469e sourceState=clean buildSource=verified
CHECKPOINT CP-F2 commit=3cbd00d37e60b2d7d937e62e721db8215ee8469e build=reused filter=/*/*/(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)|(CardWorktreeMirrorCleanupTests*)/* executed=59 passed=59 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-162014-d880/rows/CP-F2/run.trx slot=granted waited=0s dirty=0 source=3cbd00d37e60b2d7d937e62e721db8215ee8469e sourceState=clean buildSource=verified
CHECKPOINT CP-RG1 commit=3cbd00d37e60b2d7d937e62e721db8215ee8469e build=reused filter=/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-162014-d880/rows/CP-RG1/run.trx slot=granted waited=0s dirty=0 source=3cbd00d37e60b2d7d937e62e721db8215ee8469e sourceState=clean buildSource=verified
CHECKPOINT CP-F3 commit=3cbd00d37e60b2d7d937e62e721db8215ee8469e build=ok filter=/*/*/(CompletedCardWorkspaceRemovalTests*)|(CompletedCardWorkspaceContractTests*)|(RunnerWorkspaceServiceTests*)|(LegacyWorkspaceRemovalGateTests*)/* executed=144 passed=143 failed=1 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-162014-d880/rows/CP-F3/run.trx slot=granted waited=0s dirty=0 source=3cbd00d37e60b2d7d937e62e721db8215ee8469e sourceState=clean buildSource=verified
```

Receipt source: /work/evidence/task-60025819/base/.antiphon/checkpoints/20261013-163952-9ce9/report.md

```text
CHECKPOINT CP-BS1 commit=cfd114a6d2b64a19cf3d4f8f62eda2ac03172a1c build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival* executed=1 passed=1 failed=0 skipped=0 trx=/work/evidence/task-60025819/base/.antiphon/checkpoints/20261013-163952-9ce9/rows/CP-BS1/run.trx slot=granted waited=0s dirty=0 source=cfd114a6d2b64a19cf3d4f8f62eda2ac03172a1c sourceState=clean buildSource=verified
CHECKPOINT CP-BR1 commit=cfd114a6d2b64a19cf3d4f8f62eda2ac03172a1c build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical* executed=1 passed=1 failed=0 skipped=0 trx=/work/evidence/task-60025819/base/.antiphon/checkpoints/20261013-163952-9ce9/rows/CP-BR1/run.trx slot=granted waited=0s dirty=0 source=cfd114a6d2b64a19cf3d4f8f62eda2ac03172a1c sourceState=clean buildSource=verified
```

Receipt source: /work/worktrees/task-60025819/.antiphon/checkpoints/20261013-164442-489c/report.md

```text
CHECKPOINT CP-29 commit=78fcc1e2f346fb797ee042fdb97b5338a23af5de build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/C1032_ReportRecognitionCaseTable* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-164442-489c/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=78fcc1e2f346fb797ee042fdb97b5338a23af5de sourceState=clean buildSource=verified
CHECKPOINT CP-32 commit=78fcc1e2f346fb797ee042fdb97b5338a23af5de build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-164442-489c/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=78fcc1e2f346fb797ee042fdb97b5338a23af5de sourceState=clean buildSource=verified
CHECKPOINT CP-F3 commit=78fcc1e2f346fb797ee042fdb97b5338a23af5de build=reused filter=/*/*/(CompletedCardWorkspaceRemovalTests*)|(CompletedCardWorkspaceContractTests*)|(RunnerWorkspaceServiceTests*)|(LegacyWorkspaceRemovalGateTests*)/* executed=144 passed=144 failed=0 skipped=0 trx=/work/worktrees/task-60025819/.antiphon/checkpoints/20261013-164442-489c/rows/CP-F3/run.trx slot=granted waited=0s dirty=0 source=78fcc1e2f346fb797ee042fdb97b5338a23af5de sourceState=clean buildSource=verified
```

Pending PC IDs: PC-52, PC-53, PC-54, PC-55, PC-56, PC-57, PC-58, PC-59, PC-60, PC-61, PC-62, PC-63, PC-64, PC-65, PC-66, PC-67, PC-68, PC-69, PC-70, PC-71, PC-72, PC-73, PC-74, PC-75, PC-76, PC-77, PC-78, PC-79, PC-80, PC-81, PC-82, PC-108, PC-109, PC-200, PC-201, PC-202, PC-203, PC-204, PC-205, PC-206, PC-207, PC-208, PC-209, PC-210, PC-211, PC-212, PC-213, PC-214, PC-215, PC-216, PC-217, PC-218, PC-219, PC-220, PC-221, PC-222, PC-223, PC-224, PC-225, PC-226, PC-227, PC-228, PC-229, PC-230, PC-231, PC-232, PC-233, PC-234, PC-235, PC-236, PC-237, PC-238, PC-239, PC-240, PC-241, PC-242, PC-243, PC-244, PC-245, PC-246, PC-247, PC-248, PC-249, PC-250, PC-251, PC-252, PC-253, PC-254, PC-255, PC-256, PC-257, PC-258, PC-259, PC-260, PC-261, PC-262, PC-263, PC-264, PC-265, PC-266, PC-267, PC-268, PC-269, PC-270, PC-271, PC-272, PC-273, PC-274, PC-275, PC-276, PC-277, PC-278, PC-279, PC-280, PC-281, PC-282, PC-283, PC-284, PC-285, PC-286, PC-287, PC-288, PC-289, PC-290, PC-291, PC-292, PC-293, PC-300, PC-301, PC-302, PC-303, PC-304, PC-305, PC-306, PC-307, PC-308, PC-309, PC-310, PC-311, PC-312, PC-313, PC-314, PC-315, PC-316, PC-317, PC-318, PC-319, PC-320, PC-321, PC-322, PC-323, PC-324, PC-325, PC-326, PC-327, PC-340, PC-341, PC-342, PC-343, PC-344, PC-345, PC-346, PC-347, PC-348, PC-349, PC-350, PC-351, PC-352, PC-353, PC-354, PC-355, PC-356, PC-357, PC-358, PC-359, PC-360, PC-361, PC-362, PC-363, PC-364, PC-365, PC-366, PC-367, PC-368, PC-369, PC-370, PC-371, PC-380, PC-381, PC-382, PC-383, PC-384, PC-385, PC-386, PC-387, PC-388, PC-389, PC-390, PC-391, PC-392, PC-393, PC-394, PC-395, PC-396, PC-397, PC-398, PC-399, PC-400, PC-401, PC-402, PC-403, PC-404, PC-405, PC-406, PC-407, PC-408, PC-409, PC-410, PC-411, PC-412, PC-413, PC-414, PC-415, PC-416, PC-417, PC-418, PC-419, PC-420, PC-421, PC-422, PC-423, PC-424, PC-425, PC-426, PC-427, PC-428, PC-429, PC-430, PC-431, PC-432, PC-433, PC-434, PC-435, PC-500, PC-501, PC-502, PC-503, PC-504, PC-505, PC-506, PC-507, PC-508, PC-509, PC-510, PC-511, PC-512, PC-513, PC-514, PC-515, PC-516, PC-517, PC-520, PC-521, PC-522, PC-523, PC-524, PC-525, PC-526, PC-527, PC-528, PC-529, PC-530, PC-531, PC-532, PC-533, PC-534, PC-535, PC-536, PC-537, PC-538, PC-539, PC-540, PC-541, PC-542, PC-543, PC-544, PC-545, PC-546, PC-547, PC-548, PC-549, PC-550, PC-551, PC-552, PC-553, PC-554, PC-555, PC-556, PC-557, PC-558, PC-559, PC-560, PC-561, PC-562, PC-563, PC-564, PC-565, PC-566, PC-567, PC-568, PC-569, PC-570, PC-571, PC-572, PC-573, PC-574, PC-575, PC-576, PC-577, PC-578, PC-579, PC-580, PC-581, PC-582, PC-583, PC-584, PC-585, PC-586, PC-587, PC-588, PC-589, PC-590, PC-591, PC-592, PC-593

## Closing checks

At the tested 78fcc1e2 code/test tip, fetched origin/master was
00fa6b884a5f127982cff26a677aae74e6666650. Merge-tree exited 0, tree
24a09023fba5e7bbe65c9d407f1d14eca7f818a3. The final documentation tip is
checked again before the caller report; its SHA and remote progress claim are
reported there. Full task evidence-diff at 78fcc1e2: 13 commits, zero generated
entries, zero violations, exit 0; the complete final base..HEAD range is also
checked before reporting. git diff --check passed.

All checkpoint drivers terminated and their shadow tool copies were removed.
Checkpoint-owned alternate outputs were removed on green; the only remaining
bootstrap output was separately removed after every run/validator completed.
An exact bin-c60025819-* search under source/project roots and the base checkout
found no owned build output remaining. Evidence build-label directories under
.antiphon/checkpoints are retained as evidence, not deleted as binaries. The
read-only base checkout remains at cfd114a6d to preserve its raw receipts.
No restart, deployment or production deletion occurred.

--- next stage ---
next: review
handoff: Review F1-F3 repair, corrected S5 report HOLD/TRX-positive fixture, clean scoped checkpoint receipts and disclosed unreproduced setup failure; keep native/S8 and every PC pending. Landing owner is Code 60025819-39c5-49e6-8951-817cdeeff5fc; caller lands after Review and commissions SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-13-card-1032-s7-repair-code-evidence.md
