# CARD-1561 / CARD-1566 / CARD-1567 RC roster repairs

Code task / landing owner: 5a0bdb0b. Base: 877b9ce108e6e2bb4ba81840a99c4dd788302ca4.
Scope: test tooling, RC dispositions and measured launch prices only. No runtime restart.
The explicit brief overrides whole Unit / whole-suite Final verification: one shared
build and one narrow checkpoint, with red script witnesses first.

S1 commits witnesses, then runs each offline script case against unchanged production.
S2 adds exact Windows mirror-method dispositions with Linux ownership, the WQ-3 manual
method disposition and the launch cost ceiling / measured prices. No watchdog changes.

## Verification design

V-1: Mirror case: exactly 27 named UIDs excluded on Windows with CARD-1561/Linux
ownership; all stay required on Linux; future portable sibling remains required.
V-2: Explicit case: lossy metadata still excludes only the WQ-3 method, with CARD-1566
ownership; its offline sibling is the sole launch on both platforms.
V-3: Costs case: independent diagnosis timing floors protect heavy prices; a 2401-second
indivisible method refuses while 2400 seconds succeeds; watchdog remains 3600000 ms.
R-1: full ReleaseGatePlatformTests and C1514LandingSplitBudgetTests adjacent regressions.
R-2: ASCII / PowerShell parser checks, evidence-diff and clean merge-tree.
R-3: ordered RC pick applies and compiles in detached scratch checkout.

Windows native execution is owed to the desktop owner, never claimed from Linux seams.
Raw Windows discovery and complete R/P/S3 timing extracts are absent from this mirror;
the committed Linux listing can establish only a fixture estimate, not the new Windows
qualification launch count or completeness of the heavy-class timing census.

PC-1 / Mirror: remove one Windows method disposition; PC-2 / Explicit: remove the WQ-3
disposition; PC-3 / Costs: remove a heavy price; PC-4 / Costs: bypass the ceiling guard.
All PCs remain pending for SourceLanding Mutation; Code owns only original-defect reds.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---:|---|---|
| CP-1 | S2 | tests/Antiphon.Tests -> bin-rcroster/ | rc-roster | `/*/Antiphon.Tests.Scripts/(RcRosterRepairTests*)\|(ReleaseGatePlatformTests*)\|(C1514LandingSplitBudgetTests*)/*` | V-1,V-2,V-3,R-1 | all listed, 0 failed | 8 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

Run with an explicit 15m total timeout and expected-source-sha set to committed HEAD.
No repetitions. An extra compile in the RC scratch is required by the brief (R-3).
