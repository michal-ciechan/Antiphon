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
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1001-final/` | affected | `/*/*/(PlanCoverageHandleTests*)\|(PlanCoverageCommandTests*)/*` | V-1..V-5, R-1 | all listed, 0 failed/skipped | 26 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-2 | >= 1992 executed, 0 failed/skipped; census included | 1992 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=4` |

### Cost

16 minutes ordinary floor plus authoring and two tool builds for manual byte comparison. No full assembly: impact is confined to coverage CLI; delivery/landing/leases/persistence production is unchanged. Windows native confirmation is separate later work.

### Mutation pending

PC-1: remove link-count refusal -> hardlinked project named assertion red.
PC-2: remove regular-file refusal -> FIFO bounded assertion red.
PC-3: remove cap -> oversize project assertion red.
PC-4: reopen pathname -> same-handle test red.
All variants (four hardlinks/four caps/pure decision table) pending SourceLanding Mutation. Code brief additionally commissions two diagnostic scratch spot-checks, which do not discharge PCs.

## Code findings and diagnostic runs

S1 checkpoint at add351752ec7179b6fd66bb52fa913e2946ea5e3 (unchanged production) executed nine: all nine failed at the guarded outcome (four links, four caps, FIFO bounded timeout). Initial S2 affected row at 33a3d5e6c7f23da4b4cf92bb6f524410819b56bf passed 25/25. Its Unit row was stopped and joined before source edits, after discovering a cache-role cap bypass with a valid plan/C# polyglot. No Unit pass is claimed for that stopped run. The repair retains exact byte length in cached content, applies the requested cap on cache hits, and adds one red-capable polyglot regression (manual pre-repair probe exit 0; expected exit 2). Namespace census: 349 + 9 initial native/cap cases + 5 metadata rows + same-handle test + cached-cap test = 365. CP-2 now selects 26 = 16 Handle + 10 Command. CP-3 uses four in-process tests at most, while process-spawning classes retain the assembly limiter.

Two explicit brief-required scratch spot-checks used the compiled test selection from 33a3d5e6c7f23da4b4cf92bb6f524410819b56bf with only the freshly built mutated coverage component substituted: removed link-count refusal -> four hardlink results failed at coverage-hardlink-refused; reopening pathname -> same-handle result failed at coverage-read-from-verified-handle. Source and original component restored exactly after each; empty Git diff; respective restored results 4/4 and 1/1 green. Tool-component builds and exact method-scoped native test launches all took build slots, waited 0 seconds. These diagnostic runs are unlisted because expressly commissioned by the Code brief; they do not replace ordinary checkpoint build provenance or discharge PC-1..PC-4.

PC-5 pending: bypass cached byte cap -> cached_plan_bytes_still_obey_the_source_limit red. All PC variants remain pending for SourceLanding Mutation. Native Windows Debug confirmation remains unverified and separate.