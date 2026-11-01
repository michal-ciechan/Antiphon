# Qualification run graph, planner and per-target execution

Plan task e391cf00, 2026-10-31. Inspected checkout: 3fb3c113e8b2554ccd5789e00375dbfd69697981. This is one documentation deliverable, under the existing CARD-1514 qualification workstream, composed with CARD-1545. The mandatory card-number filename is used instead of the brief's unnumbered filename; no duplicate plan is added. No code, script, policy, gate, running qualification or credential was changed. No build or test was run.

Round-2 amendment: task f38a179c, based on b7457d25fe6604657d47acdd7c8e7d4c46254d3a and the full review 1a172569. Review defect IDs below use the prefix R-D to distinguish them from this plan's D-n decisions. This amendment changes this plan only; proposed witnesses are not execution evidence. Next: short plan re-check, then separate TestDesign for S1. S1-S3 are unblocked regardless of the pending operator decisions.

Represent qualification as a versioned, validated DAG. One optional planner agent proposes order and target placement over a deterministic complete roster. A deterministic executor owns execution, receipts and the final verdict. Default ordering is latest red first, then oldest unfinished work, then all remaining work with affected lanes before unchanged lanes. Windows RC stays strictly serial. Reuse is explicit, receipt-backed and initially diagnostic; Full release qualification continues to execute every required obligation.

## Evidence and ground truth

Read both failed-first plan filenames; they are byte-identical at the original inspected checkout. Also inspected CARD-1514 incremental-publication plan and the slow-test, per-test, S3 and CP-8 investigations. Re-anchored CARD-1545 to 4e1e6106d68b833347221df8f50c6346845913b2 on feat/card-task-76bd1ec4 (round 3 and TestDesign handoff), and failed-first S2 to 0f4d90b816da073481cf7200d29987017c2f5702 on feat/card-task-9058bae5 (reported green S2 checkpoints). Failed-first S3 is in flight as task 9294cb43; graph S11 waits for its reviewed landing. These inspected Git objects are design dependencies, not claims of activation. Failed-first S4/S5 remain nightly-only diagnostics.

Placement GET /api/runner-defaults and GET /api/session-runners were re-read at about 06:27 UTC, 2026-10-31. Defaults revision 2 selects server2; accepting Windows and Linux runners were advertised, with another Linux entry stale/unavailable. This is neither a capacity reservation nor provider attestation. Resolve them again at commissioning; portable tasks omit -Runner and -Platform (CP-1 currently resolves to server2), native tasks pin only their required OS, and -Runner is present only for an intentional host pin. -Platform Any removes an OS pin. All target names below are logical labels; no fleet address or deployed checkout location is embedded.

| Brief assumption / desired behavior | Inspected implementation, file-level evidence | Design consequence |
|---|---|---|
| The supervisor already has a graph | scripts/lib/nightly-tests-impl.ps1:1080-1180 performs npm ci, client build/lint and dotnet build, then :1183 iterates selected suites and :1330 their chunks. There is no DAG document. | Lift existing prerequisites and canonical chunks into nodes; do not change membership to obtain an attractive graph. |
| Read the Windows supervisor | No checked-in supervise-rc.ps1 exists. docs/investigations/2026-10-22-windows-qualification-0714a9a89-start.md records a private supervisor -> launch wrapper -> build-slot -> nightly-run -> nightly-tests -> test-host chain. The incremental-publication plan explicitly leaves private watchers uninspected. | The repo-owned path and heartbeat are inspected. Exact deployed supervisor arguments/restart behavior remain a native commissioning witness; do not invent its source or edit it here. |
| Chunks are project-sized or interchangeable numeric IDs | nightly-tests-impl.ps1:142-144 sets a 900 s packing target, 2400 s lone-class budget and 30 operands. ConvertTo-NightlyProfileLaunchGroups (:316) groups by namespace, isolates prefix collisions, splits oversized classes/methods and appends exact leaves. E2E has separate 1500/1800 s limits. | Preserve suite-qualified chunk identities, exact UID expansions, filters, exclusions, costs and watchdog admission. Chunk numbers alone cannot map history. |
| Failed-first already includes oldest/unfinished ordering | Remote S2 scripts/lib/nightly-dispatch.ps1 New-NightlyDispatchSchedule stable-partitions canonical references by priorFailure. Its coordinator preserves lazy preparation without nonempty history, and prepares all suites with history. | Reuse its extraction, coordinator and conservation witnesses. Add unfinished age and lane receipt selection; do not claim S2 already implements them. |
| Per-chunk manifest booleans are authoritative local outcomes | Publish-NightlyChunkObservation at nightly-tests-impl.ps1:682 and Write-TestPhaseExecutedManifest at test-phase-evidence.ps1:2610 preserve exitCode, trxMissing, outcomes and trxDigest, but testsPassed/coverageComplete are pre-verdict run accumulators. | Derive local failure/pass from bound terminal evidence and coverage checks, never those cumulative booleans. |
| Flat manifest is the latest complete history | test-phase-evidence.ps1:2085 opens an exclusive run writer; :2465 reads a stable committed prefix; :2425 projects terminal state. Journal generation, sequence, gaps and companion digests matter. | Consume the accessor and bound terminal record, not newest mtime or a stale flat JSON. This history influences order, never release credit. |
| Manifest recovery already resumes completed tests | The incremental-publication contract recovers diagnostics; no journal record authorizes skipping a chunk. NoSync skips source synchronization, not builds or completed work. | Distinguish scheduling preference, same-run recovery and cross-candidate reuse. New executor completion receipts are required for execution resume. |
| Per-target parallelism can replace the native lock or share it across lane names | release-gate.ps1:398-411 validates the existing lane as well as PID/start/run; a foreign lane returns shared-hop-identity. nightly-run-impl.ps1:324 acquires it before synchronization/build. | RC/master continuation preserves its lane. No inherited live context or wrapper change; a future additive live phase acquires the lock itself after RC release. |
| Portable capability permits RC execution on any OS | The RC policy requires ten suites; docs/release-gates.md:217-235 defines Windows authority and Linux PlatformDiagnostic with coverageComplete=false; the nightly native owner is Windows-only. Portable test code can have OS-specific failures. | Every required node has policy-derived platformOfRecord=Windows for RC, checked against the worker's real observed OS. Linux in an RC invocation is additive diagnostic coverage only. |
| Existing nightly launcher works on Linux | scripts/lib/nightly-owned-process.ps1 Start-NightlyNativeOwner explicitly refuses non-Windows. tools/Antiphon.Checkpoints/Execution/RowRunner.cs and ProcessDriver.cs have the portable checkpoint path. | Windows adapts the nightly coordinator. Linux uses checkpoint-backed isolated project nodes, with separately qualified custody; do not redirect Windows obligations to Linux. |
| Scoped project selection already authorizes skips | scripts/lib/rc-project-selection.ps1:212-216 emits effectiveMode=Full, gateCredit=false, executionEvidence=false. It reads old/new project graphs and downstream closure. | Reuse its conservative inventory, not its proposal as a receipt. Unknown inputs force Full. |
| Live Claude can be just another parallel project | CARD-1545 D-7 owns lock acquisition (busy means deferred); D-6 owns reuse, and S3 atomically migrates existing Full obligations. | Live out of Full and out of this graph until CARD-1545 activation. Preserve current Full census, including existing C680 tests, until that owner's atomic migration; never duplicate or prematurely exclude them. |
| Existing gate can consume any green graph | release-gate.ps1:228-258 requires Full credit and candidate SHA identity; release-authority.ps1:613 validates separate plan/ledger evidence. Production Full-ledger issuance is an acknowledged prerequisite, not established by timing manifests. | Keep graph verdict separate from release credit. No new publication authority in this work. |
| RC has nine suites | tests/test-execution-policy.json currently requires ten: antiphon, session-runner, pty-host, agents-pty, messaging, client, scripts, e2e, markdown-pdf, observability. Some prose still says nine. | Generate from pinned policy/census, including non-TUnit obligations. Never copy a prose count as the roster. |
| Nightly watchdog owns cancellation | docs/nightly-watchdog.md watches Windmill and never reads or clears Windows locks. qualification-supervisor-heartbeat.ps1 observes CPU/TRX/log/diagnostic progress. | Preserve detection-only behavior. Graph progress writes cannot count as test progress or authorize a kill. |

Primary owners: docs/release-gates.md; docs/nightly-watchdog.md; docs/testing-and-build.md; docs/test-performance-standards.md. Source anchors refer to the base above unless a remote revision is explicitly named. Re-anchor changed seams after S2/S3 land; do not merge an in-flight branch to obtain them.

## Decisions

| ID | Stated default / recommendation | Reason and rejected alternative | Decision boundary |
|---|---|---|---|
| D-1 | Deterministic canonical roster and validator; optional single agent proposal. Q2 PROVISIONAL: agent advisory only. | A model cannot decide coverage. The total stable ordering key leaves it no ordering discretion; it may explain and suggest equivalent eligible placement. Reject model-written commands or a model's green verdict. | Architecture authorized; agent discretion remains Q2. |
| D-2 | Immutable graph plus append-only events and receipts; lane/project groups, chunk/selection coverage leaves. | Preserve canonical membership and historical evidence. Reject rewriting a sealed plan as nodes finish. | Default. |
| D-3 | Q3/Q7 PROVISIONAL: Smart Full runs all required work: latest red, oldest unfinished, then affected-first among remaining work. RC/master gates remain Full. | Improve feedback without reducing coverage. Reject treating affected-first as permission to skip; scoped credit is OFF until separately qualified CARD-1545 policy. | Q3 settles ordering; Q7 retains Full gates. No gate change here. |
| D-4 | Q1 PROVISIONAL: 14-day ordering horizon, with the previous line's last closed run eligible at a new rc cut. Failure identities come only from S1 export/resolution; the lineage index adds age/unfinished/pass-clearance provenance. | Ordering grants no credit. Reject a second failure decoder and globally discarding resolved failures because one group is unresolved. | Q1 sets S4 horizon; cross-line hints never authorize reuse. |
| D-5 | Windows=1. Q4 PROVISIONAL: Linux N=1, then N=2 only after S17-L plus host/build/memory/DB-connection admission. Q5 PROVISIONAL: live out until CARD-1545 activation, then independently lock-owning serial phase. | Seats and assembly-local limiters are not host budgets. Reject OS substitution, Postgres overcommit, or impersonating the RC lock lane. | Q4 owns runtime cap; Q5 owns later additive live integration. |
| D-6 | Full has no cross-run reuse. Diagnostic Affected uses only CARD-1545-qualified non-live fingerprints/receipts when available. Live reuse belongs exclusively to CARD-1545 outside Full. | Path lists and timing manifests cannot prove unchanged behavior. Reject unsigned receipts, unlimited reuse or searching past a newer red. | No competing issuer; CARD-1545's pending reuse decisions are not authorization here. |
| D-7 | Finite frozen graph; independent prepared work continues after red or another suite's preparation failure. Q8 PROVISIONAL: a bound red is final for that run, including after diagnostic reruns. | Prevent retry-until-green and starvation. Reject latest-attempt-wins, continuous insertion or global stop for one failed discovery. | Q8 pending; design implements conservative recommendation, not an operator decision. |
| D-8 | Same-run resume requires exact plan/source/target, verified output bytes and executor receipts. Only inconclusive attempts without a receipt may get an explicit replacement after custody is joined. | Unknown effects cannot safely be duplicated. A later diagnostic pass cannot replace red. A new Full run executes its entire roster and retains prior red history. | Q8 governs counting; recovery disabled until qualified. |
| D-9 | Q6 PROVISIONAL: controller local on each host first; no SSH activation. Repository-owned transport recipe/seams may be implemented later. | Avoid inventing a remote service or changing private supervision. Reject agent commissioning under C:\Antiphon or the live supervisor. | Operator alone supplies supervisor source/launch record, forwarding changes, SSH enrollment, worker deployment and activation window. |
| D-10 | CLI JSON and generated Mermaid/Markdown first; later artifact-viewer link optional. | Inspectable without product API/UI changes. | UI implementation excluded. |
| D-11 | Shadow graph/executor first; retain legacy entrypoints until native equivalence and custody qualification. | Reversible rollout; reject replacing the live supervisor during a run. | Operator chooses activation after Review/native qualification. |
| D-12 | Verification below is proposed for re-check and separate S1 TestDesign; S1-S3 unblocked by Q1-Q8. | Schema/catalog/validator can proceed without policy decisions. | Next: short plan re-check, then S1 TestDesign, not blanket Code admission. |

## Decisions pending the operator

These are the reviewer's recommendations, **PROVISIONAL**, not decisions made by this delegate. They describe the draft below; no setting, gate, spend or operational activation is authorized by them. S1-S3 remain unblocked regardless. Later dependent slices use these as test parameters until the operator settles the indicated policy.

| Question | PROVISIONAL recommendation | Decision affects |
|---|---|---|
| Q1 history age | 14 days; at a new rc cut also the previous line's last run, for ordering only. | S4 constant/selector; never receipt freshness/credit. |
| Q2 agent discretion | Advisory only now; consider within-class reordering after shadow comparison. Current total key leaves no order ties to break. | S7 planner policy. |
| Q3 affected lanes only | Run everything, affected lanes first in class 3; skipping stays OFF until CARD-1545 scoped credit is qualified/enabled. | S5; Full coverage unchanged. |
| Q4 Linux concurrency | 1 now; 2 only after S17-L passes and DB-connection budget is admitted. | S8 default/S17-L trial. Windows always 1. |
| Q5 live lock | Live out of graph until CARD-1545 is on; thereafter separate additive phase takes native lock itself. | S14 deferred; no inherited RC lane identity. |
| Q6 deployment/transport | Controller local on each host first, no SSH. Operator supplies private supervisor source/launch record and chooses switch-on window; later SSH enrollment/deployment is operator-owned. | S13 activation/rollout; S1-S12 unblocked by this question. |
| Q7 release gates | RC cut/publish/promote and master readiness stay Full. | No gate change now. |
| Q8 attempts | Red final for the run; rerun diagnostic only. | S9/S10 verdict accounting and S15 rendering. |

## Run-plan contract

Proposed files: docs/schemas/qualification-run-plan-v1.schema.json; scripts/lib/qualification-graph.ps1; scripts/lib/qualification-graph-history.ps1; scripts/lib/qualification-graph-validation.ps1. All are future Code changes. The complete structural JSON schema follows. Semantic validation described below is mandatory in addition to schema validation; JSON Schema cannot prove set equality, acyclicity, ancestry or live capacity.

    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$id": "urn:antiphon:qualification-run-plan:1",
      "type": "object", "additionalProperties": false,
      "required": ["schemaVersion", "profile", "phase", "runId", "candidate", "mode", "order", "planner", "policyDigest", "rosterDigest", "preparationReceipt", "history", "targets", "nodes", "edges"],
      "properties": {
        "schemaVersion": {"const": 1},
        "profile": {"$ref": "#/$defs/id"},
        "phase": {"enum": ["prepare", "execute"]},
        "runId": {"$ref": "#/$defs/id"},
        "candidate": {
          "type": "object", "additionalProperties": false,
          "required": ["repositoryId", "releaseRef", "lineageRootSha", "candidateId", "sourceSha"],
          "properties": {
            "repositoryId": {"$ref": "#/$defs/id"},
            "releaseRef": {"type": "string", "minLength": 1},
            "lineageRootSha": {"$ref": "#/$defs/sha"},
            "candidateId": {"$ref": "#/$defs/id"},
            "sourceSha": {"$ref": "#/$defs/sha"}
          }
        },
        "mode": {"enum": ["Full", "Affected"]},
        "order": {"enum": ["Smart", "Original"]},
        "planner": {
          "type": "object", "additionalProperties": false,
          "required": ["kind", "version", "proposalDigest"],
          "properties": {
            "kind": {"enum": ["deterministic", "agent"]},
            "version": {"type": "string", "minLength": 1},
            "proposalDigest": {"$ref": "#/$defs/nullableDigest"}
          }
        },
        "policyDigest": {"$ref": "#/$defs/digest"},
        "rosterDigest": {"$ref": "#/$defs/digest"},
        "preparationReceipt": {"$ref": "#/$defs/nullableDigest"},
        "history": {
          "type": "object", "additionalProperties": false,
          "required": ["sourceRunId", "digest", "closedAtUtc", "fallbackReason"],
          "properties": {
            "sourceRunId": {"type": ["string", "null"]},
            "digest": {"$ref": "#/$defs/nullableDigest"},
            "closedAtUtc": {"type": ["string", "null"], "format": "date-time"},
            "fallbackReason": {"type": ["string", "null"]}
          }
        },
        "targets": {"type": "array", "minItems": 1, "items": {"$ref": "#/$defs/target"}},
        "nodes": {"type": "array", "minItems": 1, "items": {"$ref": "#/$defs/node"}},
        "edges": {"type": "array", "items": {"$ref": "#/$defs/edge"}}
      },
      "$defs": {
        "id": {"type": "string", "pattern": "^[A-Za-z0-9][A-Za-z0-9._:-]{0,159}$"},
        "sha": {"type": "string", "pattern": "^[0-9a-f]{40}$"},
        "digest": {"type": "string", "pattern": "^[0-9a-f]{64}$"},
        "nullableDigest": {"anyOf": [{"$ref": "#/$defs/digest"}, {"type": "null"}]},
        "capability": {
          "type": "object", "additionalProperties": false,
          "required": ["platform", "hostLabel", "needsNativeLock", "liveProvider"],
          "properties": {
            "platform": {"enum": ["Any", "Windows", "Linux"]},
            "hostLabel": {"$ref": "#/$defs/id"},
            "needsNativeLock": {"type": "boolean"},
            "liveProvider": {"enum": [null, "Claude"]}
          }
        },
        "target": {
          "type": "object", "additionalProperties": false,
          "required": ["id", "capability", "bindingDigest", "parallelism", "resourceKeys", "quota"],
          "properties": {
            "id": {"$ref": "#/$defs/id"},
            "capability": {"$ref": "#/$defs/capability"},
            "bindingDigest": {"$ref": "#/$defs/digest"},
            "parallelism": {"type": "integer", "minimum": 1},
            "resourceKeys": {"type": "array", "uniqueItems": true, "items": {"$ref": "#/$defs/id"}},
            "quota": {
              "type": ["object", "null"], "additionalProperties": false,
              "required": ["authorizationDigest", "maxPrompts", "maxExecutionSeconds"],
              "properties": {
                "authorizationDigest": {"$ref": "#/$defs/digest"},
                "maxPrompts": {"type": "integer", "minimum": 0},
                "maxExecutionSeconds": {"type": "integer", "minimum": 1}
              }
            }
          }
        },
        "node": {
          "type": "object", "additionalProperties": false,
          "required": ["id", "kind", "platformOfRecord", "coverageRole", "laneId", "project", "suite", "chunk", "canonicalOrdinal", "action", "selection", "targetId", "requires", "estimate", "priority", "inputFingerprint", "disposition", "skipReceipt"],
          "properties": {
            "id": {"$ref": "#/$defs/id"},
            "kind": {"enum": ["lane", "project", "lock-acquire", "build", "discover", "preparation-failed", "chunk", "selection", "lock-release", "finalize"]},
            "platformOfRecord": {"enum": ["Windows", "Linux", "Any"]},
            "coverageRole": {"enum": ["required", "diagnostic"]},
            "laneId": {"$ref": "#/$defs/id"},
            "project": {"type": ["string", "null"]},
            "suite": {"type": ["string", "null"]},
            "chunk": {"type": ["string", "null"]},
            "canonicalOrdinal": {"type": "integer", "minimum": 0},
            "action": {"type": "string", "minLength": 1},
            "selection": {
              "type": "object", "additionalProperties": false,
              "required": ["filter", "uidSetDigest", "expectedCount"],
              "properties": {
                "filter": {"type": ["string", "null"]},
                "uidSetDigest": {"$ref": "#/$defs/nullableDigest"},
                "expectedCount": {"type": "integer", "minimum": 0}
              }
            },
            "targetId": {"$ref": "#/$defs/id"},
            "requires": {"$ref": "#/$defs/capability"},
            "estimate": {
              "type": "object", "additionalProperties": false,
              "required": ["seconds", "basis", "p95Seconds", "samples", "timingDigest", "stratum"],
              "properties": {
                "seconds": {"type": ["number", "null"], "minimum": 0},
                "basis": {"enum": ["measured", "provisional", "unknown", "not-applicable"]},
                "p95Seconds": {"type": ["number", "null"], "minimum": 0},
                "samples": {"type": "integer", "minimum": 0},
                "timingDigest": {"$ref": "#/$defs/nullableDigest"},
                "stratum": {"type": ["string", "null"]}
              }
            },
            "priority": {
              "type": "object", "additionalProperties": false,
              "required": ["class", "reason", "lastCompletedAtUtc", "rank"],
              "properties": {
                "class": {"enum": ["prerequisite", "previous-failure", "unfinished", "remaining-affected", "remaining-unaffected", "receipt-reuse", "finalizer"]},
                "reason": {"type": "string", "minLength": 1, "maxLength": 1024},
                "lastCompletedAtUtc": {"type": ["string", "null"], "format": "date-time"},
                "rank": {"type": "integer", "minimum": 0}
              }
            },
            "inputFingerprint": {"$ref": "#/$defs/nullableDigest"},
            "disposition": {"enum": ["execute", "group", "sealed-inconclusive", "skip-with-receipt"]},
            "skipReceipt": {"$ref": "#/$defs/nullableDigest"}
          }
        },
        "edge": {
          "type": "object", "additionalProperties": false,
          "required": ["from", "to", "condition", "reason"],
          "properties": {
            "from": {"$ref": "#/$defs/id"},
            "to": {"$ref": "#/$defs/id"},
            "condition": {"enum": ["success", "terminal", "resource-held"]},
            "reason": {"type": "string", "minLength": 1, "maxLength": 1024}
          }
        }
      }
    }

The graph's SHA-256 is an external validation envelope over exact canonical UTF-8 bytes; it is not a self-hashing field. Sort keys and set-valued inventories ordinally, preserve typed null versus empty, reject duplicate JSON keys, nonfinite numbers, unknown versions/fields and ambiguous path identities. Validate date formats explicitly. Candidate/project paths, action names, UIDs, target binding and resource keys must come from the pinned deterministic catalog, never the agent. No shell command, credential, arbitrary executable, network address or output path is accepted from a node. An action is a registered action ID whose version/digest belongs to the policy binding; non-TUnit client/scripts nodes use a registered command action with a deterministic assertion/result roster.

The full catalog includes required lane/project groups, coverage leaves, prerequisites and finalizers. The profile and policy digest determine each node's platformOfRecord and coverageRole; neither is planner input. For RC every required node is Windows. Any is allowed only for a genuinely platform-agnostic profile obligation explicitly defined by policy, or an additive diagnostic node excluded from RC credit. A portable capability is not a waiver. At admission and execution, compare platformOfRecord with the bound worker's actual OS observation, not its claimed capability string; unknown/mismatched OS refuses placement before any child starts.

Linux's RC role is additive diagnostic execution, matching docs/release-gates.md: coverageComplete=false, coverageScope=PlatformDiagnostic, reason windows-rc-obligations-outstanding. Resolve the pinned profile exclusions for observed Linux only in that diagnostic projection, retaining its platform-limited census; independently resolve required RC authority for Windows. Never substitute the Linux result for the Windows denominator. No new policy profile or cross-host credit merger is introduced. A future required Linux profile needs reviewed policy/native witnesses. Render RC verdict and additive diagnostic status separately; failed required preparation prevents RC green.

Tests with argument expansion own exact expanded UID sets. Excluded/manual tests remain in a policy-bound census with reason/owner; exclusions are never planner decisions. Group summaries are non-executable (expectedCount=0, no skipReceipt). Every executable test leaf has nonzero expectedCount, UID digest and exact frozen filter. Build/discovery/resource nodes have no test filter/UIDs. Failed-preparation suites use the sealed required sentinel below, not fabricated zero-test success. Every discovered required UID appears once, execute or diagnostic-mode skip-with-receipt; terminal edges aggregate groups. A reused leaf retains identity/fingerprint/original receipt and executedNow=0.

Project/chunk boundaries follow the current catalog, preserving mixed projects whole. Source projects remain dependency inputs; no new csproj extraction is implied. Shared inputs select downstream consumers at both commits. There is no live-claude graph target or Full roster leaf. Existing Full tests remain required until CARD-1545 S3 atomically migrates their disposition; future dedicated live execution/reuse is separate, additive and solely owned by CARD-1545.

### Preparation before a complete test graph exists

Current chunk discovery requires a build. Use two sealed documents in one run:

1. A deterministic prepare graph pins candidate, targets, native-lock acquire, build prerequisites and per-suite discovery/census. No tests or receipt skips; all actions are policy-derived. Windows acquires its lock before source/build preparation, with no build slot held while waiting.
2. Seal a preparation receipt with a terminal outcome for each suite, plus build bytes/discovery/census for each successful suite. The execute graph retains those completed nodes and the independent required suite catalog. Each failed suite is represented by a policy-derived **preparation-failed** obligation with coverageRole=required, platformOfRecord from its profile, disposition=sealed-inconclusive, expectedCount=0, no filter/UID digest/skip receipt, and failure evidence bound through preparationReceipt. Its unknown UID roster is recorded as unknown, never empty completed coverage. If discovery succeeded but another prerequisite failed, retain the known UID census as blocked obligations behind the sentinel.
3. Validate successful suite UID equality and failed suite sentinel equality against independent preparation evidence. Run every independently prepared suite/target; a failure cannot remove its suite or stop another target's tests. Only actual dependents of a failed shared build/prerequisite stay blocked. Finalization seals missing obligations and keeps required preparation failure non-green.

There is a bounded preparation accounting phase before execute sealing, not an all-success barrier: timeouts/failed discovery become per-suite terminal sentinels. Successful work launches even when another suite failed. Same-run preparation reuse requires exact validated output bytes and receipts. Malformed global policy/source identity still refuses the graph; per-suite preparation failure does not. Legacy failed-first S2 keeps its own history-dependent preparation barrier/lazy semantics unchanged.

### Agent proposal and validator

Give the single planner agent the sealed catalog, safe timing summaries, history resolution, eligible targets and eligible reuse decisions. It returns a proposal with every node ID, target ID, priority/rank, reason and proposed dependency edges. It cannot add action text, relax the catalog, raise parallelism or create its own receipts. The deterministic planner emits the same proposal shape. Retain the proposal and validation report separately from the accepted graph.

Before execution, the deterministic validator independently reconstructs the catalog and checks:

1. Exact node-ID set equality, no duplicates/unknown nodes, correct kind/action/project/suite, identical leaf UID memberships and filters, complete required/excluded census, no overlapping coverage leaves. Planner omissions never become exclusions. Every skip is checked independently against the lane receipt policy; Full rejects every skip.
2. Correct candidate repository, canonical release ref/root, actual pinned clean source and build/discovery bytes. History identity is checked against its own source evidence; same-line ancestry is the normal ordering boundary, with Q1's explicit previous-line-at-new-cut exception for hints only. Neither that exception nor diagnostic history grants reuse; CARD-1545 validates reuse ancestry independently.
3. All mandatory edges present and correct; all endpoints known; no self-edge/cycle; no agent edge that bypasses a prerequisite. Optional edges may constrain equal-priority work but cannot manufacture dependencies which defeat mandatory failed-first/oldest order. Derive resource/finalization edges rather than trusting the proposal.
4. Node platformOfRecord/coverageRole equal the policy catalog. Required RC nodes are Windows even when requires.platform is Any. Binding matches enrolled host and real OS observation, and admission re-observes OS in the worker; declared capability is insufficient. Unknown/mismatched OS refuses with zero launches. No Windows target exceeds one; Linux N obeys host/build/memory/DB-connection budgets and physical-host aliases share resource keys. Simulate the schedule, then enforce OS and counters at runtime.
5. Recompute latest-red, unfinished age, affected status and total canonical stable keys. Under Q2's provisional advisory policy, the agent has no ordering discretion; only equivalent eligible placement and explanations remain. Estimates are advisory, never a timeout or credit rule. Validate disappearance census and per-suite preparation sentinels as well as discovered leaves.
6. Verify every reused receipt/use record and revocation/newer-red index through CARD-1545's validator, including source ancestry, full conservative dependency closure, policy/roster/environment/runtime equality, trusted issuer, host/account/external freshness and pre/post observations. Unknown closure or missing trustworthy issuer means execute, never reuse.

An invalid proposal records its rejection and tries deterministic **Smart** first, independently validated. Only an invalid/unavailable deterministic Smart order falls back to Full/Original. Missing optional history removes the unavailable hints, not known valid failures. Invalid canonical truth (malformed policy, source or global identity) refuses; per-suite preparation failure instead seals a sentinel and permits independent launches. Planner timeout uses the same bounded fallback; no interactive agent is required.

The graph is frozen before dispatch and rehashed at each host. Verdict comes from it and verified execution events, never proposal/overlay counts. Required execution needs nonzero complete passing outcomes, correct platform and joined custody; Full allows no cross-run reuse. Invalidity outranks red, red outranks inconclusive, then complete pass; canceled is non-green. Required sentinels, missing/canceled/unresolved obligations prevent green. Keep executedNow, passedNow, reusedObligations, failed, inconclusive and missing separate; additive diagnostics cannot fulfill RC obligations. Graph pass cannot set release credit, tag or publish.

### Red is final for one run

Q8's PROVISIONAL rule is enforced by the verdict reducer: any bound failed/timed-out/error execution makes that required node **red for the entire run**. List every attempt with its evidence and whether it is qualification or diagnostic. A red-then-green diagnostic rerun remains red; the later pass cannot overwrite an event, satisfy the leaf or clear that run's history red. Qualification replacement is allowed only after an inconclusive attempt without a terminal execution receipt and proved joined custody. An explicit new Full run has a new run identity and executes the whole roster; a bound pass in that later run may clear ordering history. Neither restart nor a renamed attempt is a new Full run.

## Default smart ordering and history

Coverage and ordering are independent. Full executes every required obligation; affected-first never skips. Diagnostic Affected is non-credit and uses only qualified non-live reuse through CARD-1545. Unknown fingerprints/closure force execution. A newer red/inconclusive/revocation prevents older green reuse.

Among ready executable leaves, use this total order (prerequisites inherit the best descendant priority). Within class 1, tie-break by canonical lane ordinal, chunk ordinal and ordinal node ID. Class 2 applies age before that same key; class 3 applies affected status before it. No hash-table/locale/arrival-order tie remains:

1. **LATEST RED, class 1:** any current chunk intersecting an S1-resolved class/UID whose latest bound qualification outcome across eligible closed history is failed, timed-out or errored. A later bound pass clears only the identities it proves; cancellation, absence, inconclusive, unresolved membership and diagnostic reruns do not clear red. If S1 has only a class-level red, clearance needs a later complete passing bound class roster. One red member makes the current chunk class 1.
2. **OLDEST UNFINISHED, class 2:** other chunks with never-completed members, prior launch without terminal, or members lacking a bound pass in the selected closed run. Never-completed first, then oldest member completion timestamp, then canonical lane/chunk/node key. Record planned-but-unstarted separately from interrupted. Older completed-but-not-passed work remains unfinished unless class 1 applies.
3. **AFFECTED-FIRST, class 3:** remaining previously passed work still reruns in Full. Class 3a affected lanes precede class 3b unchanged lanes, using conservative rc-project-selection closure at both the selected history run's sourceSha and candidate sourceSha. This is “since last rc run”, not since last green receipt. Unknown change mapping treats the lane as affected. Within each subset, canonical lane/chunk/node order suffices; no duration shortcut. Diagnostic Affected remains a separate mode, not permission to skip Full work.

### Repack frontier: test identity, not chunk number

For each current lane, the frontier is the longest prefix of **current canonical chunks** for which every member UID has a bound pass in the selected closed run, with no later known red. Recompute it from UID membership each plan; never import an old chunk number as a current boundary. Split, merged and moved chunks are ordinary regrouping, not contradictory history. New/renamed UIDs are unfinished. Moving an unchanged UID between chunks preserves its evidence; a suite/platform identity change needs explicit identity continuity or is new. An out-of-order later pass cannot fill an earlier UID hole.

The displayed “resume after frontier” is an ordering hint only: Full reruns already-passed members, and failed/unfinished holes outrank class 3. Actual same-run skip uses D-8 execution receipts and exact graph/selection identity; a repacked graph cannot claim same-run resume. No wrapping trick is needed for already-passed class 3 work.

Disappearance census: compare selected history roster UIDs with current discovery. Every absent UID needs source-bound deletion/rename/exclusion evidence. Unexplained disappearance is a census-incomplete obligation that prevents green while independent work proceeds; never silently reduce the denominator. This check cannot assume the same discovery output it is meant to challenge.

Example fixture: historical chunks H1=[a,b] pass, H2=[c,d] red for c/pass for d, H3=[e] unstarted, H4=[f] pass. Current A1=[a], A2=[b,d], A3=[c,f], A4=[e] split, merge and move those members. Frontier=A2, A3 is class 1, A4 class 2; actual Full launch trace is A3,A4,A1,A2. Replacing e with new UID g makes A4 unfinished; no global history fallback. With independent previously-passed lanes U (unchanged, earlier canonical lane) and T (affected), T launches before U in class 3.

### Previous-run source and fallbacks

**One failure-history source:** adopt S1's exporter/resolver and antiphon-nightly-prior-failures-v1, with exact -PriorFailuresPath/-PriorFailuresDigest forwarding and receiving-hop verification. The graph never decodes a competing failure list from its own index. For eligible closed runs, retain their immutable S1 export/digest references in the history capsule bound by history.digest; invoke that resolver for class-1 identities. The index supplies run selection, age/unfinished hints and independently bound later pass-clearance evidence, not alternative red identities. Preserve S1's own evidence/schema/provenance validation and unresolved[] semantics.

The derived index records repository/ref/root/source, run/attempt/closed time, per-suite planned roster, S1 export digest, committed-prefix generation/sequence/seal and immutable evidence pointers. The selected history run is the newest eligible **closed** attempt; retain older unresolved reds through S1 exports within Q1's provisional 14-day horizon. At a new rc cut, also allow the explicitly identified previous line's last closed run, under the same horizon and repository validation, for ordering only. Do not infer closure from mtime or import a different repository/force-moved line.

**Who closes a dead run:** the next controller, at admission under the single-writer lease, proves the old owner/process generation is dead and reconciles child custody, then appends a separately identified supervisor-aborted terminal observation from its valid committed prefix. Preserve original bytes; never impersonate the dead writer. If custody cannot be established, leave it unclosed. Until closure, select the newest closed attempt plus any valid newer unclosed committed prefix **only as unfinished hints**; it cannot clear old red, grant pass/resume credit or masquerade as an S1 closed-run export. A live owner's run is not terminalized.

Use Read-TestPhaseExecutedPrefix for age/pass-clearance evidence with generation, sequence, run/source/attempt, roster and TRX bindings. A clearing pass needs exit 0, fresh complete bound UID outcomes all Passed, and joined custody, not cumulative testsPassed/coverageComplete. Missing terminal accounting remains unfinished; S1's proved failure/hang resolution is retained. Cache the validated snapshot once per plan.

**Unresolved is not globally unusable.** A valid S1 export with old group-105 membership missing retains all observed resolvable classes and loud unresolved[]; Full continues complete current coverage. Do not invent co-packed classes. New/renamed/moved UIDs and ordinary repacking never trigger global fallback. Contradiction means incompatible terminal outcomes for the same historical UID/attempt (different chronological attempts are not contradictory), or internally conflicting claimed membership/evidence. An unbindable old member list becomes S1 unresolved at that group, not guessed membership or loss of every other class. Malformed schema/provenance/digest still refuses that input under S1's contract; an explicitly supplied invalid export is not silently ignored. Optional auto-selected unusable history is reported and excluded as a whole input, with other independently validated S1 exports retained.

| History condition | Required behavior |
|---|---|
| Optional pointer/input missing or malformed, wrong digest/generation, unexplained publication gap | Preserve raw evidence and precise unusable reason. Exclude this input; retain independent validated exports. With no usable hints, validated Full/Original. Explicit invalid S1 options refuse, as in S1/S2. |
| Different repository, force-moved identity, future time beyond skew | Reject that input; no hint or credit. Ordinary same-line ancestry and explicit previous-line Q1 exception are distinct. |
| Older than provisional horizon or stale projection | Exclude with reason; no mtime inference. Horizon is ordering-only. |
| Valid history with unresolved group-105 or removed class | Keep resolved S1 classes, loud unresolved[], complete Full roster. No invented fallback members; S4/S5 Partial remains nightly-only and refuses unresolved input. |
| N fails F1/F2; N+1 passes F1 then cancels before F2; N+2 planned | F2 remains class 1; F1 clears only if N+1 has a bound qualification pass and closed accounting. Never-completed work stays class 2 behind F2. If N+1 is unclosed, F1 remains red too until reconciled closure. |
| Valid terminal-aborted attempt / newer unclosed prefix | Closed prefix contributes bound pass/unfinished; unclosed prefix only unfinished. Neither cancellation clears a prior failure. |
| Valid no-failure history | Oldest unfinished, then affected-first remaining; never empty Full. |
| One lane lacks qualified reuse evidence | Execute it; independent diagnostic non-live reuse remains governed by CARD-1545. |

Full ignores lane skips and reuses no earlier run results. Same-run controller restart may retain verified completion only with exact graph/output identity and no red attempt. RC/master Full gates remain unchanged. Dedicated live results are never reused inside Full: after CARD-1545 S3 activation they are separate additive evidence under its D-6 policy, and before activation no dedicated live node is admitted.

Starvation prevention: freeze admission, attempt each qualification node once except the narrowly allowed inconclusive replacement, bound resource waits, and continue independent work after red/inconclusive. Retain age/debt across canceled runs; a permanently unavailable target remains explicit non-green work, not a reason to discard other targets.

## Executor, placement and recovery

Proposed front door: scripts/run-qualification-graph.ps1 with Plan, Validate, Run, Worker, Status, Cancel and Render operations. Inputs are pinned graph/proposal/validation paths, expected digests and a separately provisioned target-binding document. Operations and switches are proposed, not currently runnable. The binding maps a logical host label to one observed runner/host identity, approved transport, checkout/evidence roots, OS/runtime, native coordination domain, account enrollment and resource budgets; it contains no credential values. Moving a target after admission requires a new binding/validation attempt; never silently reroute.

| Logical target | Initial execution and limit | Placement/resource rule |
|---|---|---|
| windows-rc | Prepared nightly coordinator/chunk owner, parallelism 1 | Required platformOfRecord=Windows, real worker OS verified. Own rc/master native lane identity through preparation, execution and joined cleanup. |
| linux-projects | Local checkpoint-backed isolated selections, N=1 initially | Additive diagnostics in RC, never required Windows credit. Separate Linux diagnostic projection/census of pinned policy. N=2 only after S17-L and admitted connection/host budget. No fleet location embedded. |

### Operator-owned commissioning

Q6's provisional rollout is local controller on each host, no SSH. The Windows controller owns the complete required RC graph; a Linux controller owns an additive diagnostic graph with its own run/graph identity linked by candidate/source. A combined view imports immutable digest-verified observations only, never merges Linux credit into RC or dispatches a remote target. Cross-host execution remains disabled until separately commissioned. The controller sends typed envelopes (graph/node/attempt/lease generation, source/build/selection/target digests, prerequisites/deadline) through scripts/lib/qualification-target-transport.ps1 Submit/Observe/Cancel seams. A future SSH adapter invokes only a fixed reviewed entrypoint with opaque IDs and digest-bound data; planner text is never a command. Enrolled host verification is mandatory; no permissive host-key flags.

The **human operator alone** supplies the private supervisor source and launch record, owns any forwarding/readback modification, SSH host-key/credential enrollment, Windows worker-entrypoint deployment and switch-on window. Agents do not inspect, modify or deploy under C:\Antiphon or touch the live supervisor. The sole scoped native-worktree exception is an assigned Windows-pinned agent worktree for S16-W's provider-free witness; never C:\Antiphon\releases or private supervisor paths. No credential handling by this task. Agents may produce a repository recipe and that isolated witness; a missing operator record leaves operational activation pending, not S1-S3 blocked.

S13's commissioning admission requires an operator-supplied provenance/deployment record for any remote binding; absence refuses before transport/host mutation. Local mode makes zero SSH calls. Portable tests inject transport; native Windows default-shell argv quoting stays a separate pending witness. The parent joins its owned transport child; a lost transport is not proof the remote worker died. Host-local durable claims handle unknown acknowledgement, with no automatic resubmit.

Worker admission repeats graph validation and attestation, obtains host-wide capacity and the native lock where required, then requests the existing build slot. Effective concurrency is the minimum of graph target N, enrolled host budget, cross-target physical-host cap, resource exclusions, admitted DB connections and granted build slots. Real host OS is rechecked here, not inferred from the runner's declared capability. Lock-acquire is a completed graph prerequisite whose lease remains held by the worker until lock-release/finally; it does not occupy a task slot forever. Every native node rechecks still-held ownership. Resource-held edges visualize this obligation but do not release the lease. Never hold a build slot while waiting for native/provider capacity. Slot timeout is not-run/inconclusive, never unleased execution.

Build once per source/target/project graph, share compatible immutable dependencies on that target, and run test chunks with their certified build. Do not copy Windows outputs into Linux. Give concurrent Linux projects isolated output/results/temp roots and DB schemas; enforce a cross-process resource key for Antiphon.Tests versus Antiphon.Agents.Pty.Tests/FakeClaude, since their assembly-local limiters are insufficient. Add a host/db-instance connection-budget resource key: admit the sum of bounded worker pool maxima plus reserved operational headroom, not just schema count. Unknown pool maxima or connection headroom forces N=1. S17-L must measure simultaneous peak connections and respect the admitted ceiling; schema isolation alone cannot prevent Postgres 53300. Unknown shared fixture/resource ownership forces serial execution. Native Windows stays serial even if multiple graph targets resolve to it. No build/test bypasses build-slot.ps1 or run-checkpoint.ps1; the wrapper and already-leased child must not take nested slots accidentally.

### Lock-lane / CARD-1545 live contract

Keep **live out of Full and out of the graph until CARD-1545 S3 is activated**. Do not add inherited-live context to run-live-lane.ps1 and do not change release-gate.ps1's continuation contract. RC/master hops keep the exact held lane, run, PID and process-start identity. A foreign lane, including live-claude, must receive shared-hop-identity even with otherwise matching identity; it may not impersonate rc to enter.

After CARD-1545 activation and Q5 settlement, the recommended integration is a separate additive serial phase after RC cleanup/release (or before RC admission). That wrapper takes the same native lock in its own right under its own lane/run; busy means deferred, never inherited entry. Its provider quotas, dedicated profile, outcomes and any receipt reuse belong solely to CARD-1545. S14 is deferred until that owner's activation and reviewed contract are available; this plan grants neither spend nor wrapper changes. A typed cross-lane continuation alternative would require a separate agreed amendment to both owner plans plus native witnesses and is rejected from current scope.

Each attempt uses a durable host-local exclusive claim keyed by graph digest + node ID + attempt. Persist accepted -> running with process/boot generation -> terminal with evidence digests. Duplicate Submit returns the same claim/result, never another process. The controller has its own single-writer run lease; restart takes a new fencing generation and reconciles workers before dispatch. Workers reject stale-generation mutations. A returned terminal receipt includes exact nonzero UID/outcome reconciliation, source/build, owner exit/job-empty/drain, target binding, quota usage and artifact digests. Append one controller event for each accepted observation; store remote artifacts immutably and verify bytes before accepting them.

Same-run recovery reads the frozen graph, all attempt claims and immutable receipts. Retain a pass only with exact source/graph/selection/target identity, no bound red in that run, joined custody and revalidated **actual build-output bytes** against their sealed inventory. A matching receipt digest alone is insufficient after stale-lock recovery or NoSync; same source with modified DLL bytes refuses resume. A repacked roster is a new graph, not same-run receipt credit.

Reattach to live owned work; never reclaim by age. Unknown custody blocks replacement. A definitely terminated inconclusive attempt without a receipt may get an explicit replacement only after joining custody. Bound red permits diagnostic rerun only and remains red, including when that rerun passes. Never automatically repeat a possibly submitted prompt. If the native supervisor died, release-gates ownership proof and explicit stale-lock recovery precede new ownership; a controller reconnecting to a surviving worker does not replace it. Corrupt state is preserved/refused. CARD-1514 timing journal is not an execution ledger.

Cancellation freezes new dispatch and persists cancellation intent, then signals only owned workers/attempt generations. They stop/join their owned process trees, drain output, record terminal facts and release resources in finally. Failed/unreachable cleanup retains ownership records and non-green state; do not delete locks or kill by process name. Cleanup finalizers use terminal edges, so a test failure does not suppress cleanup. Final verdict waits for terminal accounting or explicitly records unknown custody. A lost control connection, stale heartbeat or idle diagnosis alone never kills or restarts a session.

Future separate live operation follows CARD-1545's typed outcome/probe/no-resubmit/quota rules and is outside this graph's active roster. No credential copying or automatic paid fallback. S14 witnesses use a fake provider and cannot authorize live activation.

Input-wait checklist: graph adds no interactive login/question wait. Optional planner returns within its bound or falls back deterministically. This plan changes no live-Claude session lifecycle; its separate owner remains CARD-1545. A delegate asking a human retains its seat until answered, canceled or explicitly released; with parking disabled no automatic release deadline exists (CARD-1083). No graph deadline becomes a production idle-kill rule.

## Rendering and status artifacts

Render the accepted graph deterministically to run-plan.json, run-plan.mmd and run-plan.md, plus a status.json overlay keyed by graph digest and monotonic event sequence. Proposal/rejection and fallback reasons remain separate artifacts. The overlay has pending, running, passed, failed, skipped-with-receipt and inconclusive, plus canceled for explicit cancellation. Unreachable/unstarted dependent nodes become inconclusive with dependency-not-satisfied; never skipped-with-receipt. Include target, priority/reason, estimated versus actual duration, age, lease/attempt and a safe receipt link. Null estimates display unknown. Escape labels and receipt links; permit no raw provider text, arbitrary Mermaid directives or credentials.

Example generated topology (labels illustrative, not an accepted executable plan):

    flowchart LR
      W[Native lock held] --> WB[Windows shared build]
      WB --> WD[Discover and seal roster]
      WD --> A2[A2: prior failure]
      WD --> A3[A3: unfinished]
      WD --> A1[A1: remaining Full work]
      A2 --> WA[Windows lane aggregate]
      A3 --> WA
      A1 --> WA
      WA --> WR[Native release after cleanup]
      LB[Linux project build] --> L1[Linux selection 1]
      LB --> L2[Linux selection 2]
      WR --> F[Validated RC verdict]
      L1 --> LD[Additive Linux diagnostic status]
      L2 --> LD

Do not add A2 -> A3 -> A1 success edges just to draw serial order: that would suppress the rest after red. Ordering is the ready-queue policy and target capacity; real prerequisite edges mean something different. Resource and dependency edge types are labelled in the artifact. Generate lane-collapsed and detailed views from the same graph/overlay; rendering failure is diagnostic, never permission to change execution or verdict.

Client recommendation: initially attach/link Markdown through client/src/features/artifact/ArtifactViewer.tsx and its existing MermaidDiagram.tsx, reachable from client/src/features/delegations/TaskDrawer.tsx. A later read-only qualification artifact/status API could supply the same graph digest and overlay; no UI design, backend route or client edit is included in these slices.

## Composition and implementation slices

Failed-first S1 exporter/resolver is the only failure-history source, including its unresolved-group rule. S2 at 0f4d90b816da073481cf7200d29987017c2f5702 remains the unchanged legacy non-graph scheduler. Graph scheduling reuses its prepare/invoke/complete seams but independently adds age/affected ordering, never another failure decoder. Failed-first S3 (task 9294cb43) is a hard reviewed-landing prerequisite for graph S11's launch/publication integration. Graph does not repack or replace coverage reconciliation. Failed-first S4/S5 PriorFailureSweep/Partial remain planned nightly-only non-credit diagnostics, independent of graph Affected and never RC authority.

CARD-1514's incremental journal is the historical timing/outcome input and diagnostic output, with one writer per worker/run root. Do not let multiple target workers append to one journal context. The controller aggregates verified per-target references once; retain the original per-target evidence. CARD-1545 owns category/project closure, live exact membership, fingerprints and trusted receipt validation. Until its implementations and policy decisions are available, use conservative Full and fake receipt seams in portable tests; do not create a competing fingerprint/issuer implementation.

All slices below are sequential at shared-file boundaries, estimated 30-60 minutes of Code including the named narrow verification. A slice that cannot fit must stop at a pushed boundary and be split before further work, retaining incomplete obligations. Ordinary Review/landing and operational qualification are separate. Rollback means disabling the new graph entrypoint for a new run and retaining evidence, never changing an in-flight sealed graph or rewriting a receipt.

| Slice / minutes | Files to add/change and result | Tests / checkpoint | Risk and rollback |
|---|---|---|---|
| S1 / 45 | docs/schemas/qualification-run-plan-v1.schema.json with required platformOfRecord/profile/coverageRole; scripts/lib/qualification-graph.ps1 parse/canonicalize/catalog identity; scripts/test-qualification-graph.ps1 -Case SchemaAndIdentity; tests/Antiphon.Tests/Scripts/QualificationGraphContractTests.cs. No executor changes. | SchemaAndIdentity; CP-1, current default server2, about 11 min | Schema drift; versioned opt-in. Separate S1 TestDesign first; S1-S3 unblocked by Q1-Q8. |
| S2 / 50 | qualification-graph.ps1; nightly-tests-impl.ps1 prepared-plan adapter. Canonical roster/platform census, per-suite preparation-failed sentinels and UID disappearance accounting. | QualificationGraphCatalogTests.CanonicalRoster; CP-2 | Dropped suite/UID; retain legacy default and fail sentinel. |
| S3 / 45 | new qualification-graph-validation.ps1. Independent set/filter/edge/platform/real-OS checks; prohibit live target before activation. | QualificationGraphValidationTests.RejectsIncompleteGraph; CP-3 | Validating proposal against itself; independent catalog oracle. |
| S4 / 50 | new qualification-graph-history.ps1; reuse test-phase-evidence.ps1 accessor and S1 nightly-failure-history.ps1 exporter/resolver. Lineage age/unfinished/pass-clearance, latest-red reduction and next-controller aborted accounting. | QualificationGraphHistoryTests.HistoryAndFallback; CP-4 | Never discard resolved S1 classes for group-105; corrupt explicit history refuses. |
| S5 / 45 | new qualification-graph-order.ps1. Latest-red/oldest/affected-first ready order, UID repack frontier, inherited prerequisite rank; no packer edits. | QualificationGraphOrderTests.FailedOldestStable; CP-5 | Holes/starvation; Original fallback only if validated Smart unavailable. |
| S6 / 40 | new qualification-graph-reuse.ps1 adapter to landed CARD-1545 interfaces; Full refuses skips; diagnostic non-live fake seam only while unavailable. | QualificationGraphReuseTests.ReceiptConservation; CP-6 | Stale reuse; real-validator integration pending until dependency lands. |
| S7 / 40 | run-qualification-graph.ps1 Plan/Validate; validation library proposal audit. Advisory agent data only; deterministic Smart then Original fallback. | QualificationGraphProposalTests.AgentCannotChangeCoverage; CP-7 | Omission/commands; retain rejected proposal and validate fallback. |
| S8 / 55 | new qualification-graph-executor.ps1. Local ready queue, observed OS/host/resource/DB-connection counters, dispatcher seam and terminal aggregation. | QualificationGraphSchedulingTests.TargetAndResourceLimits; CP-8 | Oversubscription; serial opt-in until native proof. |
| S9 / 50 | new qualification-graph-state.ps1. Claims/fencing, every-attempt ledger, sticky-red reducer and actual output-byte revalidation. | QualificationGraphRecoveryTests.NoDuplicateAfterRestart; CP-9 | Unknown custody retains ownership; diagnostic green cannot erase red. |
| S10 / 40 | executor/state libraries; Run/Status/Cancel. Owned stop/join/finalizers; persist cancellation and red precedence. | QualificationGraphCancellationTests.CancelPreservesCustody; CP-10 | No broad kill or early release. |
| S11 / 55 | new qualification-graph-windows.ps1; narrow nightly-run-impl.ps1/nightly-tests-impl.ps1 integration. Preserve RC lane lock, shared build, per-suite failure continuation/publication. **Wait for reviewed failed-first S3 task 9294cb43.** | QualificationGraphWindowsAdapterTests.RealCoordinatorTrace CP-11; RcManifestIntegrationTests.LedgerAndReceiptBytesAreUnchanged CP-12; OS declaration below | Portable seams not native proof; retain S2 legacy path. |
| S12 / 50 | new qualification-graph-checkpoint.ps1; local Linux checkpoint adapter and isolated project/connection resources. | QualificationGraphCheckpointTests.IsolatedProjectsAndSlots CP-13; OS declaration below | Explicit adapter discriminator; no Windows owner or unleased fallback. |
| S13 / 45 | new qualification-target-transport.ps1 local-first envelope, operator commissioning-record admission and recipe. SSH code remains disabled pending Q6/operator enrollment. | QualificationGraphTransportTests.HostBindingAndUnknownAck CP-14; OS declaration below | Missing operator record: zero remote calls, no host mutation. |
| S14 / 40, DEFERRED | Future qualification-graph-live.ps1 **separate additive phase**, only after CARD-1545 S3 activation, Q5 and agreed owner contract. Consume unchanged run-live-lane.ps1 with its own lock; no inherited context or Full leaf/reuse. | QualificationGraphLiveTests.SeparateLivePhaseOwnsLock; CP-15, fake provider only | Not commissioned now. Busy defers; no shared RC continuation or spend. Earlier V-3/V-8/V-16 prove live stays out. |
| S15 / 40 | new qualification-graph-render.ps1; Render and docs/release-gates.md/testing-and-build.md contract. Separate RC verdict/additive diagnostics, all attempts, live excluded. | QualificationGraphRenderTests.OverlayIsNonAuthoritative; CP-16 | Overlay cannot override sticky-red or missing evidence. |
| S16-W / 55 | scripts/test-qualification-graph.ps1 and QualificationGraphNativeTests.cs native provider-free lock/claims/default-shell argv qualification in assigned Windows worktree only. | NativeWindowsLifecycle CP-17; OS declaration below | No private supervisor/deployment access; native record required. |
| S17-L / 50 | Same harness and QualificationGraphLinuxTests.cs: native Linux custody/isolation and DB-connection budget trial. | NativeLinuxParallelIsolation CP-18; OS declaration below | Keep N=1 until isolation and measured connection admission both pass. |
Unqualified library filenames above are under scripts/lib/. New C# wrappers named in the table live in tests/Antiphon.Tests/Scripts/; each slice extends the common PS harness with its named case. Integration wrappers use the assembly-local ProcessSpawnLimit and owned ScriptHarness; no AppHost/provider/DB except S17-L's explicitly bounded shared test-Postgres connection probe. Windows-side/daemon entrypoints remain ASCII-only for Windows PowerShell 5.1; the portable harness checks bytes as well as parsing. New scripts require the existing script-census dispositions and classification witnesses, scoped to the slice. Do not edit generated policy hashes manually. No new project/reference/solution definition is planned; if one becomes necessary, follow the owner build-graph audit once after final definition changes and amend the scope/checkpoints before execution.

## Performance budget

Apply docs/testing-and-build.md#performance-standards and docs/test-performance-standards.md R1-R8. This Plan runs no tests because it changes one Markdown file only. The future checkpoint budgets below are provisional; there is no measured p95 for these new tests. Estimates are not permission to silently extend production watchdogs or infer a zero-cost phase.

| Evidence / phase | Measurement or initial budget | Bound / acceptance |
|---|---|---|
| Historical real roster replay | Failed-first plan records 19,012 nodes in 15.847 s and 23.778 s, each n=1 at different source/load. Not a comparable p95. | Share one expanded immutable fixture per harness process; use for conservation/scale, not a current fixed node count or a timeout calibration. |
| Historical cold script overhead | CARD-1514 CP-8 investigation at eaea94cffa8363f6074d51e146916992545a32da: 20 fresh Linux children/arm; worst comparable child p95 12.3959586 s, body p95 6.9710063 s; loaded host identified in that report. | Its 26 s child/15 s body limits are measurements for that witness only. Do not assign these p95s to new graph tests or Windows. |
| Isolated build per slice | Historical loaded build 413.23 s (6.89 min), n=1 in CP-8 investigation, not p95. Combined with 210 s enclosing method bound gives 623.23 s = 10.39 min. | Budget about 11 min per portable build+test row; native build+270 s gives 11.39 min, budget 12. Initial outer row/build envelope 14m is a provisional CARD-1404/1531 exception, not a measured p95. |
| Portable graph method | 30-90 s expected including owned PS startup; parser/queue body typically below 30 s. | Initial test/harness body 180 s, child deadline 180 s plus 10 s cleanup; enclosing TUnit deadline 210 s. Provisional upper-bound exception under CARD-1514/1404, replace with about 2x comparable measured p95. |
| Native fake-child lifecycle method | 60-120 s expected, no real qualification/provider. | 240 s body, 10 s cleanup, enclosing 270 s. Record individual child readiness/exit/drain; no unbounded sleeps. |
| Controller planning / rendering | Target <30 s at 19,012 UIDs and a synthetic 1,000-chunk catalog; three in-process pure passes reuse one fixture. | 60 s initial component bound, n=3 remains provisional; O(V+E) validation plus O(V log V) ready sorting, no per-dispatch complete-history reads. |
| Single slice green | Portable build rows 11 min; CP-12 shares CP-11 build and adds 1 min (S11 total 12). Native rows 12 min. | One committed slice group per tool run; execution budget 20m and hard wall 25m, queue budget 5m separately. No invocation combines all slices. |
| Provider/transport waits | Live probes inherit CARD-1545 30 s retry allowance/15 s classifier probe; local test transport uses controlled clocks. | Remote data-call initial deadline 30 s, bounded reconnect observation 60 s, no silent retry on unknown Submit; provisional CARD-1531 until measured. Cleanup failures remain non-green. |

Checkpoint row estimates sum to 190 minutes across 17 separate Code slices, including deferred S14 (11 min); currently commissionable envelope is 179 min excluding that slice, not one selection. Total authoring-plus-verification slice estimates remain 795 minutes, with baseline-red/green within each slice. Baseline-red authoring runs are charged within each 30-60-minute slice, with one exact discriminating method and source-bound evidence before its fix. Independent Review, tool bootstrap if needed, Debug, native operational commissioning and a Full qualification are additional costs. Do not run the whole test assembly or Full RC as ordinary Code verification. If a slice forecast exceeds 60 minutes, split it at a named file/behavior boundary and amend the manifest before proceeding.

Code uses the checkpoint tool, one run per committed After group, with explicit duration suffixes and expected source SHA; wait through exit 75 to terminal completion. Every build/test driver acquires the host build slot; no unlisted speculative builds. The checkpoint importer's 15-minute/3x defaults are not the performance policy. Replace provisional deadlines with approximately ceil(2*p95) once comparable samples exist, recording source SHA, host/OS/runtime, load, sample count, selection, concurrency and cold/warm setup. A three-sample pure timing check is diagnostic, not calibrated p95; collect subsequent ordinary green/Review observations rather than repeatedly rerunning solely to manufacture a percentile.

Setup once per harness invocation: parse the policy/discovery/catalog, build immutable fixture rosters and import script libraries. Per case use separate small evidence/state roots and fake clock/process boundaries. Preparation, scheduler and transport tests need no Docker, host, migrations or database; mark these not-used, not measured zero. Native probes use fresh tiny owned children because process custody/restart is the subject; measure that cost explicitly. S17-L shares one prepared test-Postgres fixture per invocation, with separate schemas and bounded worker pools, no production DB/runner. Record readiness, open/close/query times and peak connections; absent fixture/headroom is not-run, never native proof. Initial connection admission/probe budget 30 s within the 240 s native body is provisional CARD-1514/1531; a cold container/migration need beyond that requires revised measured TestDesign cost before the run. Real project execution later shares expensive AppHost/container/DB/migration setup once per assembly/run, uses schema/template/transaction isolation and records fresh-start exceptions. Graph design does not silently repair existing per-chunk fixture costs.

Retain ignored evidence under .antiphon/qualification-graph/<source>/<lane>/<run>/ for Code, and the enrolled external qualification evidence root for operation. Include exact test/UID/category/project graph, graph/target binding digests, phase spans for slot admission, build, discovery, preparation, snapshot/clone, body, publication, transport, cleanup and remainder. Record every filesystem/process/HTTP/DB call category with bounded elapsed/outcome; unused phases are not-used and unavailable phases unknown. Wall time is separate from summed concurrent spans. Estimates come from recorded comparable successful/censored timings: never treat a timed-out run as a complete p95 or old class prices as a measured chunk distribution. Keep canonical packing prices/watchdogs unchanged by ordering.

Every expected/observed selection, batch or run over 30 minutes requires Debug with its exact selection/source/load/timings before repetition. The historical 1378.1-minute Full and its publication cost are evidence for CARD-1514/0856 investigation, not permission for another unbudgeted Full. A future operational Full graph must have a separately commissioned Debug/measurement obligation; graph rendering or parallelism does not waive the complete-run threshold. Measure request-to-first-failed-node launch, first failure, tail debt, total makespan and per-target busy/admission time on the next authorized run; do not promise failure feedback in two minutes when preparation itself costs minutes.

## Verification design

This section is a concrete design for plan Review and provider-free Code, not passing evidence. Every new TUnit method below is one unparameterized result, even when its harness checks a matrix. Expected assertion-RED must occur at the product boundary: zero tests, fixture/build errors and fabricated JSON passing its own validator do not count. Commit the failing method before its fix. Later SourceLanding Mutation runs each control method-scoped, restores mutations and repeats that exact method green; no broad positive-control run.

| Witness | Required cases and discriminating observation |
|---|---|
| V-1 SchemaAndIdentity | Parse prepare/execute including platformOfRecord/profile/coverageRole and failed-preparation sentinel. Missing/unknown platform, changed required/diagnostic role, duplicate keys/IDs, unknown version/action/path/target or malformed digest must fail schema/catalog identity. Roundtrip canonical hash stable; deleting the platform requirement makes a negative arm fail. S1 remains parser/identity only, not worker implementation. |
| V-2 CanonicalRoster | Real production planner over committed c1410 plus companion/non-TUnit fixtures, independent UID census, exclusions, splits/collisions. Inject failed Linux discovery and separately failed Windows E2E prerequisite: each failed suite retains its sentinel, prepared independent Windows/other suites launch, required E2E failure keeps verdict non-green. Dropping the sentinel or globally suppressing launches fails. Missing historical UID without source deletion/rename proof prevents green; explained deletion and new UID controls pass. |
| V-3 RejectsIncompleteGraph | Valid catalog control, then omission/duplicate/unknown/filter widening/UID overlap/edge removal/cycle/fake receipt, Windows N=2 and host aliases. Crucial OS arms: Windows RC obligation with Any capability on bound Linux; worker declares Windows but actually observes Linux; forged node platformOfRecord=Any; unknown observed OS. Each refuses with zero dispatch. Linux diagnostic pass cannot fulfill required RC leaf. Inject dedicated live target before CARD-1545 activation: refuse; Full census remains unchanged. |
| V-4 HistoryAndFallback | Real S1 exporter/resolver plus CARD-1514 accessor. Bound complete/aborted histories, cumulative-flag lies, wrong-suite chunk, stale projection, digest/generation/provenance error; explicit invalid S1 path/digest refuses. Real group-105 shape retains observed classes and loud unresolved[]; new/repacked UID does not invalidate history. Q1 parameter arms at 14-day boundary, same-line and explicit previous-line/new-cut hints, future/cross-repository refusal. N/N+1/N+2: F2 red survives cancellation; F1 clears only from bound closed qualification pass. Next controller writes aborted observation only after dead-owner/join proof; unknown owner remains open and prefix cannot clear red. Ignoring resolved failures or terminalizing a live owner fails. |
| V-5 FailedOldestStable | Assert actual fake-launch trace at coordinator, not order JSON: latest red, never/oldest unfinished, then affected T before canonically earlier unchanged U. Diff uses selected history.sourceSha; a different last-green source yielding another affected set exposes wrong baseline. Repack fixture H1..H4 -> A1..A4 above asserts frontier A2 and trace A3,A4,A1,A2 across split/merge/move. Out-of-order pass, renamed UID and holes remain unfinished. N/N+1/N+2 launches unresolved red F2 before never-run work. Group-105 observed failures survive. Removing affected ranking, mapping numeric chunks, or dropping known red fails; no retries starve tail. |
| V-6 ReceiptConservation | Non-live trusted receipt control; changed dependency/deletion/unknown input, wrong issuer/host, newer red/revocation, unrelated line/expired policy force execution/refusal. Full never skips and rejects a dedicated live receipt as Full credit. Diagnostic reuse remains visible with executedNow=0. Actual landed CARD-1545 validator required for integration; fake seams do not close that obligation. |
| V-7 AgentCannotChangeCoverage | Missing tail, arbitrary action/receipt, reordered classes or raised cap rejected. Deterministic Smart launch trace preserves failed/affected priority after agent timeout/invalid proposal; only invalid Smart falls back to validated Original. Corrupt canonical policy refuses. Replacing fallback with immediate Original fails the trace assertion. |
| V-8 TargetAndResourceLimits | Barrier children measure target AND physical-host/resource peak. Windows=1, Linux=2 only under qualified config, assembly exclusion, independent progress, no held slot while lock busy. Invoke real Enter-ReleaseGateNativeLock in owned fixture: exact continuation control accepted; same run/PID/start but foreign lane returns shared-hop-identity and zero child starts. No live target admitted before activation. Connection-budget admission refuses a second worker whose pool maxima plus headroom exceed cap despite distinct schemas; removing any counter exposes peak>cap or an illegal launch. |
| V-9 NoDuplicateAfterRestart | Fresh controller imports durable claims at accepted/start/terminal/lost-ack boundaries. Duplicate Submit idempotent, old generation refuses, unknown owner not rerun. Matching same-run pass retained; graph/source/receipt mismatch refuses. Keep receipt digest unchanged but alter a built DLL: resume refuses (including NoSync/stale-lock recovery). Bound red followed by requested qualification rerun refuses; diagnostic green appended leaves red. Joined inconclusive-without-receipt replacement allowed. Clearing old red or checking only receipt digest fails. |
| V-10 CancelPreservesCustody | Cancel before/during child and unavailable transport; no later admission, only owned child signaled, descendants/drain joined before release. Prior red plus cancel remains red/non-green with every attempt retained. Unknown remote retains ownership. Success-only cleanup edge or early release fails observed custody. |
| V-11 RealCoordinatorTrace | Actual landed failed-first S3 entrypoint through graph adapter, deterministic process seam. Nonidentity trace, digest forwarding including S1 path/digest, one shared preparation/publication context, suite heartbeat/final union. Failed E2E prep does not suppress independent prepared suites; per-suite failure sentinel persists. Same-lane native continuation only; no live wrapper call. Legacy S2 absent/empty history unchanged. A no-op adapter or global-prep return fails observed launches. |
| V-12 ManifestRegression | Existing RcManifestIntegrationTests.LedgerAndReceiptBytesAreUnchanged unchanged. Immutable authority/UID data retained; S2 order-dependent diagnostics retain declared exceptions. Regression guard only, not new issuer proof. |
| V-13 IsolatedProjectsAndSlots | Actual checkpoint request/receipt adapter with controlled driver: separate roots, exact filters/counts, one slot, exit 4 not-run, wait exit 75 to terminal, no Windows native owner on Linux. Real OS-bound receipt cannot satisfy Windows RC even with Any capability; failed receipt never green. |
| V-14 HostBindingAndUnknownAck | Actual local/SSH envelope builder and receiver validate graph/host/source/generation. Local mode records zero SSH/deploy calls. Remote mode without operator-supplied commissioning provenance refuses before any transport; attempted live-supervisor/deployed-root mutation is not an accepted action. Test owned worktree control separately. Fixed entrypoint/opaque args preserve spaces/non-ASCII as data; wrong host refuses. Lost ACK observes claim, never duplicate submit. Removing admission gate must yield an unexpected transport call. No real SSH/private source access in this portable case. |
| V-15 SeparateLivePhaseOwnsLock (DEFERRED S14) | After CARD-1545 activation/agreed owner contract only: fake-provider wrapper gets own lane/run and lock after RC release. Busy defers with no paid spawn; forged foreign-lane continuation refuses. No inherited RC context or live receipt enters Full. Typed outage/inconclusive, healthy red, zero/teardown failure and green remain distinct; no prompt resubmit. A nested/inherited acquisition or credit merge fails. This cannot be run to imply activation before the dependency exists. |
| V-16 OverlayIsNonAuthoritative | Render accepted graph/events with separate required RC and Linux diagnostic status, all attempts and safe links. Red then diagnostic green remains red; overlay green/missing ledger cannot grant pass. Inject dedicated live leaf/reuse into Full: rejected, absent from valid Full rendering. Existing policy-required C680 stays present until owner migration. Renderer failure never changes dispatch. |
| V-17 NativeWindowsLifecycle | Owned provider-free executable in Windows-pinned agent worktree: real Job Object/lock, two contenders, real Windows default-shell argv receiver, restart/cancel/join. Correct same-lane continuation accepted; otherwise identical foreign-lane request returns shared-hop-identity. Peak native=1; unknown custody retains lock. Local activation path makes no SSH/supervisor/deployment call. If later Q6 selects SSH, native OpenSSH default-shell argv witness is separately operator-commissioned in a disposable worktree and remains pending until received; local argv alone does not qualify SSH. Missing prerequisites fail/not-run, never skipped green. |
| V-18 NativeLinuxParallelIsolation | Two tiny owned checkpoint-compatible children, isolated outputs/results/schemas, external barrier, N=2 cap; restart/cancel/process-group join and slot refusal externally observed. Shared test-Postgres fixture once per invocation: bounded pools, measured peak connections/headroom, over-budget second-worker refusal despite schema separation; no whole-suite parallelism or production runner/DB. Missing connection evidence leaves N=1. Removed connection admission must trigger illegal launch/peak assertion, not merely a mocked success. |
All test names are proposed except V-12. Repository search found no existing QualificationGraph* classes. All checkpoint filters use one exact method, so there are no OR suffix collisions; Code rechecks the compiled namespace/method roster at its integrated source. If future consolidation uses OR, every operand must be parenthesized and end in *, and table pipes must be escaped; otherwise use separate exact-method rows. Do not introduce broad class OR to save a line. Check fresh TRX executed identities/counts against these exact methods.

### OS branch witnesses

S11, S12, S13, S16-W and S17-L carry docs/testing-and-build.md#os-branch-witnesses obligations. At each Code tip, emit the owner's version-1 ignored declaration with full base/head SHAs, changed paths/helpers/branch anchors, real production type/factory, exact test setup/outcome and nativeObligation. Run check-os-witnesses.ps1 against that committed plan and retain evidence. Proposed functions below must be pinned to their actual implementation anchors by TestDesign/Code; no fabricated source line or passed skip. Include absent/empty/value setup for any new optional configuration input and startup/restart-impact census.

| Slice | Production branch / real object or factory to witness | Portable witness | Native obligation |
|---|---|---|---|
| S11 | qualification-graph-windows.ps1 adapter -> Start-NightlyNativeOwner and Enter-ReleaseGateNativeLock, exact lane continuation | V-11 / CP-11 real coordinator; V-8 / CP-8 real lock helper | Windows V-17 / CP-17 pending; native owner/job/lock cannot be proved by fake process seam. |
| S12 | qualification-graph-checkpoint.ps1 -> RowRunner.RunAsync / ProcessDriver.RunAsync, system process factory and OS receipt check | V-13 / CP-13 request/admission/receipt | Linux V-18 / CP-18 pending, actual process groups and isolated resources. |
| S13 | qualification-target-transport.ps1 fixed-entrypoint factory -> real ProcessStartInfo/ArgumentList; local vs future SSH branch | V-14 / CP-14 real launch-composition object and zero-transport admission | Windows local argv V-17 / CP-17; remote OpenSSH default-shell argv remains pending until operator commission. No production bypass. |
| S16-W | Same Windows production owner/lock/receiver factories; ASCII-only entrypoints | Supporting portable CP-11/14 cannot close native debt | CP-17 with -Platform Windows, no -Runner unless intentional pin; assigned worktree only, no private supervisor. |
| S17-L | Linux system process/group owner through checkpoint factory; admitted DB connection resource | Supporting portable CP-8/13 | CP-18 with -Platform Linux, no host pin; measured connections plus custody required before N=2. |

### Review defect closure map

Every row names an observable negative arm above, not a prose-only promise. These are planned witnesses; no checkpoint was executed in this amendment.

| Review defect | Plan contract / implementation slice | Fail-capable witness and checkpoint |
|---|---|---|
| R-D-1 wrong OS | Run-plan platformOfRecord/coverageRole, real worker OS; S1-S3/S12 | V-1 CP-1 missing field; V-3 CP-3 Any-on-Linux Windows obligation refuses; V-13 CP-13 cannot import Linux credit. |
| R-D-2 affected-first missing | Default smart ordering class 3; S5 | V-5 CP-5 changed T precedes unchanged U even when canonical order disagrees. |
| R-D-3 repack frontier | Repack frontier by current UID membership; S4/S5 | V-5 CP-5 split/merge/move trace A3,A4,A1,A2; ordinary regrouping keeps history. |
| R-D-4 canceled run demotes red / no closer | Latest-red and next-controller aborted observation; S4/S9 | V-4 CP-4 N/N+1/N+2 and dead/unknown owner; V-5 CP-5 F2 before never-run work. |
| R-D-5 foreign lock lane | Lock-lane contract, live deferred, no wrapper change; S8/S16-W | V-8 CP-8 and V-17 CP-17 exact identity except foreign lane must refuse. |
| R-D-6 competing history / group-105 | One S1 exporter/resolver, unresolved preservation; S4/S11 | V-4 CP-4 group-105 resolved failures retained; V-11 CP-11 forwarded source/digest. |
| R-D-7 retry-to-green | Red-final reducer and every-attempt ledger; S9/S10/S15 | V-9 CP-9 and V-16 CP-16 bound red plus diagnostic green remains red. |
| R-D-8 global prep failure | Per-suite preparation-failed sentinel; S2/S11 | V-2 CP-2 and V-11 CP-11 independent suite launches after discovery/E2E failure. |
| R-D-9 owner unassigned | Operator-owned commissioning, local-first; S13/S16-W | V-14 CP-14 absent operator record yields zero remote/deploy calls; V-17 CP-17 confines native witness to owned worktree. |
| R-D-10 live reuse in Full | Live out of graph/Full; separate CARD-1545 phase only after activation | V-3 CP-3 and V-16 CP-16 reject injected dedicated live leaf/receipt; V-15 CP-15 later independent lock, deferred. |

Review advisories A-1..A-10 are also addressed: disappearance census V-2; Smart-first fallback V-7; advisory-only agent/Q2; cross-line Q1; 11/12-minute rows and S1 import; docs/schemas location; OS declarations above; DB budget V-8/V-18; output-byte verification V-9; ASCII-only entrypoints checked by portable harness. Recommendations remain provisional where Q1-Q8 apply.

### Checkpoints

Closed ordinary scope for each commissioned slice, one After group per committed slice. CP-15/S14 is explicitly deferred until its named dependency/decisions; do not commission or silently treat it as passed. Lane is named in Group: portable uses default placement with no runner/platform pin (CP-1 currently server2); windows-native requires Windows; linux-native requires Linux because process-group custody is the subject. Native rows are separately dispatched and never interpreted as passed by portable seams. Each build uses a fresh bin- output with forward slash, through the checkpoint tool/build-slot gate, then removes all corresponding alternate outputs after completion. CP-12 reuses CP-11 only within S11 at the same source. No live paid selection is in this table. S1 first runs the checkpoint tool's import --plan against this table and retains the imported 18-row roster; this docs-only amendment does not bootstrap or execute the tool. Use an existing certified tool binary, or account for a gated bootstrap in S1. Single exact-method filters need no suffix; any later OR must use (NameA*)\|(NameB*) in Markdown with every operand parenthesized/starred.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-qgraph-s1/ | portable-contract | /*/Antiphon.Tests.Scripts/QualificationGraphContractTests/SchemaAndIdentity | V-1 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-qgraph-s2/ | portable-catalog | /*/Antiphon.Tests.Scripts/QualificationGraphCatalogTests/CanonicalRoster | V-2 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-3 | S3 | tests/Antiphon.Tests -> bin-qgraph-s3/ | portable-validator | /*/Antiphon.Tests.Scripts/QualificationGraphValidationTests/RejectsIncompleteGraph | V-3 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-4 | S4 | tests/Antiphon.Tests -> bin-qgraph-s4/ | portable-history | /*/Antiphon.Tests.Scripts/QualificationGraphHistoryTests/HistoryAndFallback | V-4 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-5 | S5 | tests/Antiphon.Tests -> bin-qgraph-s5/ | portable-order | /*/Antiphon.Tests.Scripts/QualificationGraphOrderTests/FailedOldestStable | V-5 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-6 | S6 | tests/Antiphon.Tests -> bin-qgraph-s6/ | portable-reuse | /*/Antiphon.Tests.Scripts/QualificationGraphReuseTests/ReceiptConservation | V-6 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-7 | S7 | tests/Antiphon.Tests -> bin-qgraph-s7/ | portable-proposal | /*/Antiphon.Tests.Scripts/QualificationGraphProposalTests/AgentCannotChangeCoverage | V-7 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-8 | S8 | tests/Antiphon.Tests -> bin-qgraph-s8/ | portable-scheduling | /*/Antiphon.Tests.Scripts/QualificationGraphSchedulingTests/TargetAndResourceLimits | V-8 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-9 | S9 | tests/Antiphon.Tests -> bin-qgraph-s9/ | portable-recovery | /*/Antiphon.Tests.Scripts/QualificationGraphRecoveryTests/NoDuplicateAfterRestart | V-9 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-10 | S10 | tests/Antiphon.Tests -> bin-qgraph-s10/ | portable-cancel | /*/Antiphon.Tests.Scripts/QualificationGraphCancellationTests/CancelPreservesCustody | V-10 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-11 | S11 | tests/Antiphon.Tests -> bin-qgraph-s11/ | portable-windows-adapter | /*/Antiphon.Tests.Scripts/QualificationGraphWindowsAdapterTests/RealCoordinatorTrace | V-11 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-12 | S11 | CP-11 | portable-manifest-regression | /*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged | V-12 | exact existing method, 0 failed/skipped | 1 | 1 | true |
| CP-13 | S12 | tests/Antiphon.Tests -> bin-qgraph-s12/ | portable-checkpoint | /*/Antiphon.Tests.Scripts/QualificationGraphCheckpointTests/IsolatedProjectsAndSlots | V-13 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-14 | S13 | tests/Antiphon.Tests -> bin-qgraph-s13/ | portable-transport | /*/Antiphon.Tests.Scripts/QualificationGraphTransportTests/HostBindingAndUnknownAck | V-14 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-15 | S14 | tests/Antiphon.Tests -> bin-qgraph-s14/ | portable-separate-live-phase | /*/Antiphon.Tests.Scripts/QualificationGraphLiveTests/SeparateLivePhaseOwnsLock | V-15 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-16 | S15 | tests/Antiphon.Tests -> bin-qgraph-s15/ | portable-render | /*/Antiphon.Tests.Scripts/QualificationGraphRenderTests/OverlayIsNonAuthoritative | V-16 | exact method, 0 failed/skipped | 1 | 11 | true |
| CP-17 | S16-W | tests/Antiphon.Tests -> bin-qgraph-win/ | windows-native | /*/Antiphon.Tests.Scripts/QualificationGraphNativeTests/NativeWindowsLifecycle | V-17 | exact native method, 0 failed/skipped | 1 | 12 | true |
| CP-18 | S17-L | tests/Antiphon.Tests -> bin-qgraph-linux/ | linux-native | /*/Antiphon.Tests.Scripts/QualificationGraphLinuxTests/NativeLinuxParallelIsolation | V-18 | exact native method, 0 failed/skipped | 1 | 12 | true |

## Risks, rollout and acceptance

| Failure mode | Detection / containment | Acceptance or rollback |
|---|---|---|
| Fingerprint misses dependency, runtime or remote state | Conservative old/new project closure, unknown input forces execution, independent CARD-1545 issuer/freshness checks and shadow Full comparison. | No scoped credit until qualified; disable reuse, retain old receipt and rerun Full. |
| Planner invents or drops a node | Exact canonical set/UID conservation and independently recomputed verdict. | Invalid proposal falls back; malformed canonical truth refuses. |
| Failures/old nodes starve tail and always-run lanes | Finite snapshot, one attempt, bounded resource waits, age/debt display, independent nodes continue. | Canceled/unfinished graph never green; investigate repeated tail debt instead of repeatedly restarting. |
| Previous record belongs to another candidate/line | Repository/ref/root/ancestry/run/source and evidence digest checks. | Same-line or Q1's explicit previous-line hint may order only; refuse incompatible input, retain other independently validated exports. |
| Duplicate execution after restart or lost ACK | Durable host claim, idempotency key, fencing and process-generation reconciliation. | Unknown custody blocks replacement; no exactly-once promise for an unobserved remote effect. |
| Native overlap via two target labels | Physical-host/resource identity in binding, retained shared lock, runtime aggregate counters. | Failed native qualification keeps new executor disabled. |
| Graph pass falsely becomes release green | Separate verdict types, no Full marker on Affected, independent release gate/ledger/use-record validators. | No changes to existing gate booleans or publication commands in graph rollout. |
| Incremental publication breaks under parallel targets | One journal writer per target/run root; controller imports immutable digests once. | Keep legacy manifests readable; no shared mutable projection written by two hosts. |
| Linux executor assumes Windows nightly custody | Explicit adapter/OS check and native two-worker proof. | Linux N=1 or not-run until qualified; never OS substitution. |
| Remote transport/worker or private supervisor is unqualified | Native commissioning records exact script/source/host/argv, ownership and stop/resume evidence. | Local/shadow graph remains useful; remote activation stays pending, without claiming a live result. |

Rollout: land reviewed provider-free schema/catalog/validator first (S1-S3 unblocked), then independently reviewed history/order/executor slices under the provisional choices above. Graph S11 waits for reviewed failed-first S3 task 9294cb43; S2 remains legacy. Shadow locally, qualify provider-free Windows serial and Linux isolation, and keep local controllers as Q6's provisional first activation. The operator supplies private supervisor source/launch record, deploys any worker/forwarding change, enrolls SSH if later chosen, and chooses the activation window. Agents provide only the repository recipe and isolated native-worktree witnesses. No active qualification, tag, publish, gate or deploy is changed by this plan.

S14 remains deferred until CARD-1545 S3 activation and Q5 settlement; dedicated live results stay outside Full and take an independent native lock. Any scoped-credit activation requires CARD-1545 qualification and operator decision. Missing native/commissioning evidence remains pending. Next is a short plan re-check, then **TestDesign for S1 only** (schema/platform field, graph parser, SchemaAndIdentity harness/wrapper and CP-1). Q1-Q8 remain operator-owned; none blocks S1-S3.
