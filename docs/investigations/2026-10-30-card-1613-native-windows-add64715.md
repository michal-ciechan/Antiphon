# CARD-1613 native Windows verification: red witnesses

Task add64715, native Windows 10.0.19045, eight cores. Original source:
`b11c0ff5977412ecb6b4ac4665c9078e21b30164`; immutable comparison base:
`ade54222543381e2e1b31ad79d8ae33a4035f70b`.

## Original checkpoint attempt

Imported the committed CARD-1613 plan and selected only CP-9, CP-10 and CP-11.
Run `20261030-004641-33ea` exited 5: its isolated shared build exceeded the
unchanged 15-minute deadline. All three rows executed zero tests. The source
remained clean; build provenance is unknown, not a green certificate.
The run took 1,095.249 seconds, with no admission-only interval. The compiler
was still active before cancellation. Subsequent slot requests observed the
host below its 6,144 MB memory floor. This does not establish a source regression.

Receipt: `.antiphon/checkpoints/20261030-004641-33ea/report.json` and adjacent
`report.md` and build log. Copies are retained under
`C:\Temp\antiphon-add64715-evidence`.

## Real audit red witness

A separately gated build of `tools/Antiphon.LandGraphAudit` succeeded in 14.91 s
with zero warnings and errors. The real audit used `-Check`, never repository
`-Update`, with that tool. Its slot waited 90 s and was held for 100 s.

It reported the required digest
`E117DDECAC45785183AEF8BBE6818DB4DB85A6EC89B9EAD065C1047BF0EDD49B`, count 68,
then refused: `evaluation-failed: evaluating Antiphon.sln copy exceeded 12 s`.
The script exited 1 after evaluator exit 2. Its wall time including admission
was 191.627 s. SHA-256 manifests of all 9,196 tracked files before and after
were identical, and Git remained clean. The printed canary path was confirmed
absent after the run:
`C:\Antiphon\worktrees\antiphon-landgraph-canary-6f51008d71334d63a58896a79e6c2823`.

Evidence: `C:\Temp\antiphon-add64715-evidence\audit-check.log`,
`audit-integrity.json`, `tracked-before.txt`, and `tracked-after.txt`.

The original `SolutionAudit.RealPath` probes `FileInfo.LinkTarget` for every
component, including ordinary directories, then separately reads attributes.
The proposed repair checks attributes first and reads link targets only for
reparse points. It retains path-by-path resolution and the existing copy budget.
The base had a 68-second Windows copy budget; this tip deliberately restored
12 seconds. No budget increase is proposed.

## Other execution still pending

The first baseline checkpoint-script wrapper was stopped before launching a
build, after 431.3 s (167 s admission), with its ownership verified and no child
process present. Policy comparisons now use direct gated project builds and
native TUnit class filters, retaining TRX and exact source checks. Their outcomes,
the two C1483 methods, repaired audit, and any further checkpoints remain pending.

Automatic approval review rejected recursive deletion of the failed owned
build output with only `blocked by policy`. The output was instead preserved,
without deleting data, at
`C:\Temp\antiphon-add64715-evidence\failed-windows-output`.

## Later native witness at f389a3d61e57d11905b39de6d0cdf4faf46d3150

CP-9/10/11 passed 4/1/2 with a validated clean-source receipt. All 126 policy
cases matched baseline outcomes and reasons. The standalone audit approved,
but C1483_S4_RealRepositoryApproves then exceeded the 12-second copy limit and
reported cleanup errors. C1483_S5_CheckRequiresEvaluationRecord and the linked
skippable-input guard passed (selection: 2 passed, 1 failed).

The Windows Markdown scan adds backslash paths to the ordinal set already
containing Git's slash paths: 1,853 tracked docs became 3,577 entries, including
1,724 duplicate copies. Normalizing only the platform directory separator to
Git's slash form restores 1,853 entries. Native receipts and the failure TRX
remain in C:\Temp\antiphon-add64715-evidence. This paragraph records the red
witness before that repair; it is not a claim that the last failure passed.
