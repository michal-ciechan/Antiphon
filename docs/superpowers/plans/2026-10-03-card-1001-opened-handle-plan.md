# CARD-1001 opened-handle implementation and verification

S1 adds bounded red tests on unchanged f0fadc61635e3d977f045876d65e1e09ef06b17a production. S2 implements one confined opened-handle read per selected path, Linux nonblocking no-follow open + fstat + /proc/self/fd resolution, Windows reparse-aware open + handle information + final path, and same-handle bounded reads. Cache selected bytes within one invocation. Caps: 4 MiB plan/checklist; 1 MiB source/project. Unsupported native platforms fail closed. No writes, deletes, process or network calls in coverage.

## Verification design

V-1: FIFO csproj exit 2 within 15 seconds; bounded child killed/joined on original defect.
V-2: hardlinked project/source/plan/checklist exit 2 with INPUT_INVALID detail.
V-3: oversized project/source/plan/checklist exit 2 with named detail.
V-4: pure metadata decision table refuses special files, multiple links and outside-root final paths; allows regular one-link confined paths.
V-5: injected after-verification seam replaces pathname; read returns originally verified handle bytes.
R-1: full PlanCoverageCommandTests and PlanCoverageHandleTests, existing path-escape and frozen-count cases.
R-2: full namespace census class and whole Unit lane (registry census changed).
R-3: master/branch JSON SHA256 and exits identical for frozen CARD-0891 and CARD-0780 plan (compare same input tree).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1001-red/` | handle-red | `/*/*/PlanCoverageHandleTests*/*` | V-1..V-3 | 9 executed; expected guarded failures on unchanged production | 9 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1001-final/` | affected | `/*/*/(PlanCoverageHandleTests*)\|(PlanCoverageCommandTests*)/*` | V-1..V-5, R-1 | all listed, 0 failed/skipped | 25 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-2 | >= 1992 executed, 0 failed/skipped; census included | 1992 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

### Cost

16 minutes ordinary floor plus authoring and two tool builds for manual byte comparison. No full assembly: impact is confined to coverage CLI; delivery/landing/leases/persistence production is unchanged. Windows native confirmation is separate later work.

### Mutation pending

PC-1: remove link-count refusal -> hardlinked project named assertion red.
PC-2: remove regular-file refusal -> FIFO bounded assertion red.
PC-3: remove cap -> oversize project assertion red.
PC-4: reopen pathname -> same-handle test red.
All variants (four hardlinks/four caps/pure decision table) pending SourceLanding Mutation. Code brief additionally commissions two diagnostic scratch spot-checks, which do not discharge PCs.
