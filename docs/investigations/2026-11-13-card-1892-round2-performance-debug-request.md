# CARD-1892 round 2 performance Debug request

Caller commissioning obligation under docs/test-performance-standards.md R1:
the six-row Final run exceeded 30 minutes. This Code delegate cannot sub-delegate.
Commission a Debug task with this evidence; a healthy long run is not a hung process.
No timeout increase, assertion relaxation, or whole-selection profiling repeat is authorized here.

Run: 20261113-190233-548b. Source: 24a5889ffab22344492278cf42e579fea3162fef.
Host: Debian GNU/Linux 12, 24 reported cores. The broker repeatedly reported 4/4
occupied leases; CPU and memory load samples were not collected and remain unknown.
The grant applied maxcpucount=6. No build or test ran unleased.

Exact selection: CP-1 through CP-6 in
docs/superpowers/plans/2026-11-13-card-1892-input-uncertainty.md at that source.
Immutable manifest and complete timings:
/work/worktrees/task-df03fdd9/.antiphon/checkpoints/20261113-190233-548b/manifest.resolved.yaml
and report.json/report.md in the same directory.

Observed wall 2013.740 s (33m33s), charged execution 1506.395 s,
admission-only union 507.344 s. All 455 cases passed, no failures/skips.
Server build checkpoint phase 659.259692 s; MSBuild itself reported 566.56 s.
The difference is unattributed driver/audit remainder, not measured compilation.
Runner build checkpoint phase 137.7192999 s. Build admission: runner 60 s,
server 105 s. Row admission: CP-1 105 s, CP-2 225 s, CP-3 270 s,
CP-4 150 s, CP-5 120 s, CP-6 120 s. These waits overlap; do not sum them as wall.

Row host phases (startup / tests wall / teardown, seconds):

| Row | Startup | Tests wall | Teardown |
|---|---:|---:|---:|
| CP-1 | 3.383443 | 1.3471661 | 0.6447072 |
| CP-2 | 38.8658619 | 37.648885 | 21.7612947 |
| CP-3 | 23.0651601 | 256.5717299 | 1.1734723 |
| CP-4 | 39.6938983 | 16.0768807 | 18.0711842 |
| CP-5 | 71.9430606 | 54.1996888 | 15.0730753 |
| CP-6 | 69.2044033 | 19.2495844 | 13.5271644 |

This is one loaded observation, not p95. Compare build/audit phases and admission
with the preceding red run 20261113-184743-e73f (658.582 s wall,
432.029 s execution, 226.554 s admission-only; server build 286.7188294 s).
Do not infer a product latency regression from nested per-class duration sums.
The extra authoring runs only exercise a changed attention-reader assertion and its
repair, then CP-2/CP-3; they do not repeat the full green selection for profiling.

## Round 3 extension (Code 4e02b770)

Keep this same request; do not file a duplicate performance card. The round-3
closed green selection is CP-2/3/4/5/6/8/9 in
docs/superpowers/plans/2026-11-13-card-1892-round3.md, all serial on Debian 12.
Its conservative combined budget is 37 minutes (7 cold build plus 30 row minutes),
which triggers R1 before execution. Caller: include this selection in the existing
Debug commission. No extra full green profiling repetition is requested.

Pre-fix run 20261113-214537-0f17 at
2ee1199f9ab24effebde9442ccf99396438e6fef ran CP-90/91 serially in 1102.227 s wall,
937.050 s charged execution and 165.177 s admission-only. Its server build phase
was 595.8066299 s; MSBuild reported 471.78 s. This difference is driver/audit
remainder, not compilation. Build waited 15 s; both rows waited 75 s. CP-90
host startup/tests/teardown were 109.9061954/16.1503936/48.102711 s;
CP-91 was 61.3584127/5.3802417/44.7533977 s. All nine expected assertion
failures ran with clean source and verified build provenance, with no fixture error.
Broker maxcpucount=6, repeatedly 4/4 occupied; CPU/memory samples are unknown.
Immutable receipt and phase/per-test payloads remain under
/work/worktrees/task-4e02b770/.antiphon/checkpoints/20261113-214537-0f17/.
Append the green run's exact source, identity and timing when it completes.
