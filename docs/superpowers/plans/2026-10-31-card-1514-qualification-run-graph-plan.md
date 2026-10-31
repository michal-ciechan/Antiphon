# Qualification run graph, planner and per-target execution

Plan task e391cf00, 2026-10-31. Inspected checkout: 3fb3c113e8b2554ccd5789e00375dbfd69697981. This is one documentation deliverable, under the existing CARD-1514 qualification workstream, composed with CARD-1545. The mandatory card-number filename is used instead of the brief's unnumbered filename; no duplicate plan is added. No code, script, policy, gate, running qualification or credential was changed. No build or test was run.

Represent qualification as a versioned, validated DAG. One optional planner agent proposes order and target placement over a deterministic complete roster. A deterministic executor owns execution, receipts and the final verdict. Default ordering is failures first, then oldest unfinished work, then the remaining affected work. Windows RC stays strictly serial. Reuse is explicit, receipt-backed and initially diagnostic; Full release qualification continues to execute every required obligation.

## Evidence and ground truth

Read both failed-first plan filenames; they are byte-identical at the inspected checkout. Also inspected CARD-1514 incremental-publication plan and the slow-test, per-test, S3 and CP-8 investigations. The latest inspected CARD-1545 plan is on origin/feat/card-task-5efd30dd at 3017ed5e63477958acebfbcece00bbf96ddd97a2, including its round-2 caller-witness amendment; it supersedes the copy on origin/feat/card-task-da0479a5 at f11f05c02aba9c6c00e461f2a86ef05845ceefdf. Failed-first S2 was inspected on origin/feat/card-task-9058bae5 at b6f6cb5f6af1b2a00204d6a5af300eb23a5d6ff4. These remote snapshots are design dependencies, not claims that their latest changes have landed or passed Review.

Placement GET /api/runner-defaults and GET /api/session-runners returned successfully at about 05:14 UTC. Defaults revision 2; eligible accepting Windows and Linux runners were advertised, with another Linux entry unavailable. This is neither a capacity reservation nor provider attestation. Resolve them again at commissioning; portable tasks omit -Runner and -Platform, native tasks use -Platform Windows, and -Runner is present only for an intentional host pin. -Platform Any removes an OS pin. All target names below are logical labels; no fleet address or filesystem location is embedded.

| Brief assumption / desired behavior | Inspected implementation, file-level evidence | Design consequence |
|---|---|---|
| The supervisor already has a graph | scripts/lib/nightly-tests-impl.ps1:1080-1180 performs npm ci, client build/lint and dotnet build, then :1183 iterates selected suites and :1330 their chunks. There is no DAG document. | Lift existing prerequisites and canonical chunks into nodes; do not change membership to obtain an attractive graph. |
| Read the Windows supervisor | No checked-in supervise-rc.ps1 exists. docs/investigations/2026-10-22-windows-qualification-0714a9a89-start.md records a private supervisor -> launch wrapper -> build-slot -> nightly-run -> nightly-tests -> test-host chain. The incremental-publication plan explicitly leaves private watchers uninspected. | The repo-owned path and heartbeat are inspected. Exact deployed supervisor arguments/restart behavior remain a native commissioning witness; do not invent its source or edit it here. |
| Chunks are project-sized or interchangeable numeric IDs | nightly-tests-impl.ps1:142-144 sets a 900 s packing target, 2400 s lone-class budget and 30 operands. ConvertTo-NightlyProfileLaunchGroups (:316) groups by namespace, isolates prefix collisions, splits oversized classes/methods and appends exact leaves. E2E has separate 1500/1800 s limits. | Preserve suite-qualified chunk identities, exact UID expansions, filters, exclusions, costs and watchdog admission. Chunk numbers alone cannot map history. |
| Failed-first already includes oldest/unfinished ordering | Remote S2 scripts/lib/nightly-dispatch.ps1 New-NightlyDispatchSchedule stable-partitions canonical references by priorFailure. Its coordinator preserves lazy preparation without nonempty history, and prepares all suites with history. | Reuse its extraction, coordinator and conservation witnesses. Add unfinished age and lane receipt selection; do not claim S2 already implements them. |
| Per-chunk manifest booleans are authoritative local outcomes | Publish-NightlyChunkObservation at nightly-tests-impl.ps1:682 and Write-TestPhaseExecutedManifest at test-phase-evidence.ps1:2610 preserve exitCode, trxMissing, outcomes and trxDigest, but testsPassed/coverageComplete are pre-verdict run accumulators. | Derive local failure/pass from bound terminal evidence and coverage checks, never those cumulative booleans. |
| Flat manifest is the latest complete history | test-phase-evidence.ps1:2085 opens an exclusive run writer; :2465 reads a stable committed prefix; :2425 projects terminal state. Journal generation, sequence, gaps and companion digests matter. | Consume the accessor and bound terminal record, not newest mtime or a stale flat JSON. This history influences order, never release credit. |
| Manifest recovery already resumes completed tests | The incremental-publication contract recovers diagnostics; no journal record authorizes skipping a chunk. NoSync skips source synchronization, not builds or completed work. | Distinguish scheduling preference, same-run recovery and cross-candidate reuse. New executor completion receipts are required for execution resume. |
| Per-target parallelism can replace the native lock | release-gate.ps1:385 Enter-ReleaseGateNativeLock uses atomic CreateNew and PID/start/run/continuation identity. nightly-run-impl.ps1:324 acquires it before synchronization/build; live owners are never stolen by age. | Retain one shared native lock and host-wide serialization, including live Claude on the same host. A DAG edge alone is not a mutex. |
| Existing nightly launcher works on Linux | scripts/lib/nightly-owned-process.ps1 Start-NightlyNativeOwner explicitly refuses non-Windows. tools/Antiphon.Checkpoints/Execution/RowRunner.cs and ProcessDriver.cs have the portable checkpoint path. | Windows adapts the nightly coordinator. Linux uses checkpoint-backed isolated project nodes, with separately qualified custody; do not redirect Windows obligations to Linux. |
| Scoped project selection already authorizes skips | scripts/lib/rc-project-selection.ps1:212-216 emits effectiveMode=Full, gateCredit=false, executionEvidence=false. It reads old/new project graphs and downstream closure. | Reuse its conservative inventory, not its proposal as a receipt. Unknown inputs force Full. |
| Live Claude can be just another parallel project | CARD-1545 separates exact live membership, dedicated profile/executable attestation, issuer, freshness, prompt quotas and the native lock. Most of that remains planned. | Bind to its lane contract; graph scheduling cannot waive its outstanding decisions or spend authority. |
| Existing gate can consume any green graph | release-gate.ps1:228-258 requires Full credit and candidate SHA identity; release-authority.ps1:613 validates separate plan/ledger evidence. Production Full-ledger issuance is an acknowledged prerequisite, not established by timing manifests. | Keep graph verdict separate from release credit. No new publication authority in this work. |
| RC has nine suites | tests/test-execution-policy.json currently requires ten: antiphon, session-runner, pty-host, agents-pty, messaging, client, scripts, e2e, markdown-pdf, observability. Some prose still says nine. | Generate from pinned policy/census, including non-TUnit obligations. Never copy a prose count as the roster. |
| Nightly watchdog owns cancellation | docs/nightly-watchdog.md watches Windmill and never reads or clears Windows locks. qualification-supervisor-heartbeat.ps1 observes CPU/TRX/log/diagnostic progress. | Preserve detection-only behavior. Graph progress writes cannot count as test progress or authorize a kill. |

Primary owners: docs/release-gates.md; docs/nightly-watchdog.md; docs/testing-and-build.md; docs/test-performance-standards.md. Source anchors refer to the base above unless a remote revision is explicitly named. Re-anchor changed seams after S2/S3 land; do not merge an in-flight branch to obtain them.

## Decisions

| ID | Stated default / recommendation | Reason and rejected alternative | Decision boundary |
|---|---|---|---|
| D-1 | Deterministic canonical roster and validator; optional single agent proposal, deterministic planner available without a provider. | A model may suggest ordering but cannot decide what constitutes coverage. Reject executing model-written commands or trusting its green verdict. | Architecture authorized by the brief. |
| D-2 | Immutable graph plus separate append-only execution events and receipts. Lane/project are grouping nodes, chunks/selections are coverage leaves. | Preserves canonical membership and historical evidence while showing status. Reject rewriting a sealed plan as nodes finish. | Default. |
| D-3 | Smart is the default ordering policy. Coverage mode is independently Full or Affected. Full is the default for release/master gates. | Ordering can improve feedback without reducing coverage. Reject making smart order synonymous with permission to skip. | Operator must separately approve any future gate that accepts scoped credit; recommendation: keep cut/publish/promote and master readiness Full. |
| D-4 | Prior-run history must be the identified latest closed attempt of this lineage, at most seven days old; otherwise use Full original order with a reason. | Old hints are optional. Reject failing qualification solely because optional history is missing, and reject salvaging an unexplained subset. Seven days is a conservative scheduling default, not receipt freshness. | Operator may choose a shorter hint horizon; no gate weakening. |
| D-5 | Windows target parallelism=1; Linux target exposes integer N, initially 1 until isolated two-worker qualification; live target concurrency=1 with its own approved quota. | Native serialization is explicit direction; process limiters and runner seats are different budgets. Reject treating current free seats as N or applying Linux N to Windows. | Recommend first Linux trial N=2, then measured configuration capped by host/build/memory policy. Operator owns that runtime cap. |
| D-6 | Reuse only CARD-1545-qualified lane fingerprints and trusted green execution/use records; initially no authoritative reuse. Native/environment-sensitive lanes always run. | A path list or timing manifest cannot establish unchanged behavior. Reject unsigned worktree JSON, unlimited live reuse, and searching backwards past a newer red. | CARD-1545 owns reuse horizon, trusted issuer and enforcement activation; its provisional 24 h live recommendation is not adopted as authorization here. |
| D-7 | One finite frozen graph per candidate; each work node gets one execution attempt by default; independent work continues after a test red. | Failures-first must not repeatedly retry the head and starve the tail. Reject continuous priority insertion or a fresh global replan after every failure. | Default; operator cancellation remains explicit. |
| D-8 | Same-run resume requires exact plan/source/build/target identity plus executor receipts. Uncertain in-flight work is reconciled, never automatically duplicated. A new Full attempt reruns its complete roster. | Exactly-once effects cannot be promised across an uncertain remote acknowledgement. Reject inferring a pass or process death from a missing heartbeat. | Default; restart recovery must be qualified before enabling it. |
| D-9 | Introduce script front doors and narrow local/SSH target adapters; reuse existing nightly/checkpoint execution primitives. | Fits existing qualification ownership and avoids a product API/UI migration. Reject pretending the checkpoint tool already provides remote graph execution. | Remote host enrollment/transport commissioning is separate; no new fleet endpoint is assumed. |
| D-10 | CLI JSON plus generated Mermaid/Markdown first; optional client link through the artifact viewer later. | The graph should be inspectable without a new UI or service deployment. | UI design/implementation excluded. |
| D-11 | Shadow graph and executor first; legacy entrypoints remain selectable until native equivalence and custody are qualified. | Provides a reversible rollout and protects ongoing qualifications. Reject replacing the live supervisor during an active run. | Operator chooses activation window after Review and native commissioning. |
| D-12 | Verification design below is included; provider-free implementation can follow plan Review. Paid/native operation and scoped gate activation remain separately commissioned. | No need to block writing the graph on account, quota or gate policy choices. | Defaults are explicit; caller reviews the plan and settles activation decisions, not whether to plan the graph. |

## Run-plan contract

Proposed files: scripts/schemas/qualification-run-plan-v1.schema.json; scripts/lib/qualification-graph.ps1; scripts/lib/qualification-graph-history.ps1; scripts/lib/qualification-graph-validation.ps1. All are future Code changes. The complete structural JSON schema follows. Semantic validation described below is mandatory in addition to schema validation; JSON Schema cannot prove set equality, acyclicity, ancestry or live capacity.

    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$id": "urn:antiphon:qualification-run-plan:1",
      "type": "object", "additionalProperties": false,
      "required": ["schemaVersion", "phase", "runId", "candidate", "mode", "order", "planner", "policyDigest", "rosterDigest", "preparationReceipt", "history", "targets", "nodes", "edges"],
      "properties": {
        "schemaVersion": {"const": 1},
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
          "required": ["id", "kind", "laneId", "project", "suite", "chunk", "canonicalOrdinal", "action", "selection", "targetId", "requires", "estimate", "priority", "inputFingerprint", "disposition", "skipReceipt"],
          "properties": {
            "id": {"$ref": "#/$defs/id"},
            "kind": {"enum": ["lane", "project", "lock-acquire", "build", "discover", "chunk", "selection", "lock-release", "finalize"]},
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
                "class": {"enum": ["prerequisite", "previous-failure", "unfinished", "remaining", "receipt-reuse", "finalizer"]},
                "reason": {"type": "string", "minLength": 1, "maxLength": 1024},
                "lastCompletedAtUtc": {"type": ["string", "null"], "format": "date-time"},
                "rank": {"type": "integer", "minimum": 0}
              }
            },
            "inputFingerprint": {"$ref": "#/$defs/nullableDigest"},
            "disposition": {"enum": ["execute", "group", "skip-with-receipt"]},
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

The full catalog includes all required lane/project group nodes and coverage leaves, every required prerequisite and finalizer. Tests with argument expansion own their exact expanded UID set. Excluded/manual tests remain in a separate disposition census with reason/owner; moving an exclusion is a policy change, not a planner decision. The validation envelope binds that census. Group nodes are non-executable summaries with expectedCount=0 and no skipReceipt; they neither satisfy coverage nor consume a worker slot. Every test leaf has nonzero expectedCount, a selection digest and its exact frozen filter. Build/discovery/resource nodes have no test filter/UIDs. Each leaf exists exactly once, with either execute or skip-with-receipt; edges into groups are terminal aggregation dependencies. A skipped leaf retains its identity, input fingerprint and original receipt digest, executes zero tests now and is visibly marked reused.

Graph nodes can represent an isolated test project as one selection or its existing chunks. Initial lane boundaries follow the current project/suite catalog and CARD-1545 app categories, preserving mixed projects whole. Source projects appear through the dependency/input inventory; this does not extract new csproj boundaries. Shared build and content dependencies select downstream consumers at both old and new commits. The live-Claude exact-method lane is a distinct lane across two projects, not an accidental second copy of Full obligations; its migration must follow CARD-1545's atomic gate/policy change.

### Preparation before a complete test graph exists

Current chunk discovery requires a build. Do not hide that work or claim an agent can know the final roster before it exists. Use two sealed documents in one run:

1. A deterministic prepare graph, validated before any process, pins candidate, targets, native-lock acquire, build prerequisites and one discovery/census per required suite. It contains no test execution or receipt skips. Every prepare action comes from the policy catalog. The agent cannot remove it. Windows acquires its existing native lock before source/build preparation, preserving current ordering; no build slot is held while waiting for that lock.
2. Successful preparation emits an immutable source/build/discovery receipt and the exact canonical execution roster. The execute graph references that preparationReceipt, includes the already-completed preparation nodes, and is validated in full before any qualification test launches. Their state comes only from the bound preparation receipt. Discovery failure yields incomplete qualification with no executable graph; it cannot remove a required suite. A same-source, validated discovery/build receipt may avoid repeating preparation only within supported same-run recovery, not by accepting a stale build.

This prepare-all barrier is explicit behavior for the opt-in graph path, needed for global ordering and conservation. The existing non-graph lazy path remains unchanged, including absent/empty-history behavior promised by S2. On history failure the graph path uses Full original test order, but still performs its documented preparation barrier; it does not claim identical lazy failure timing.

### Agent proposal and validator

Give the single planner agent the sealed catalog, safe timing summaries, history resolution, eligible targets and eligible reuse decisions. It returns a proposal with every node ID, target ID, priority/rank, reason and proposed dependency edges. It cannot add action text, relax the catalog, raise parallelism or create its own receipts. The deterministic planner emits the same proposal shape. Retain the proposal and validation report separately from the accepted graph.

Before execution, the deterministic validator independently reconstructs the catalog and checks:

1. Exact node-ID set equality, no duplicates/unknown nodes, correct kind/action/project/suite, identical leaf UID memberships and filters, complete required/excluded census, no overlapping coverage leaves. Planner omissions never become exclusions. Every skip is checked independently against the lane receipt policy; Full rejects every skip.
2. Correct candidate repository, canonical release ref and lineage root; actual pinned clean source and build/discovery digests. A previous source must be an ancestor on the same lineage. Different candidate IDs are allowed across that lineage, never across repositories/lines or force-moved history.
3. All mandatory edges present and correct; all endpoints known; no self-edge/cycle; no agent edge that bypasses a prerequisite. Optional edges may constrain equal-priority work but cannot manufacture dependencies which defeat mandatory failed-first/oldest order. Derive resource/finalization edges rather than trusting the proposal.
4. Target capability satisfies the node, binding matches enrolled host and native/provider attestation, no Windows target exceeds one, no requested N exceeds configured/admitted limits, and shared physical-host/resource keys are consistent across logical targets. Validate a schedule simulation, then enforce counters again at runtime; static validation alone cannot enforce capacity under live contention.
5. Recompute priority classes, age and canonical stable keys; enforce all mandatory precedence constraints. The agent may choose only among equal-ranked ready nodes or equivalent eligible targets, with its explanation retained. Estimated duration is advisory, recomputed from accepted timings, never a new timeout or credit rule.
6. Verify every reused receipt/use record and revocation/newer-red index through CARD-1545's validator, including source ancestry, full conservative dependency closure, policy/roster/environment/runtime equality, trusted issuer, host/account/external freshness and pre/post observations. Unknown closure or missing trustworthy issuer means execute, never reuse.

An invalid proposal records its exact rejection and attempts the deterministic Full/Original proposal once; validate that independently. Bad optional history/proposal therefore does not refuse a valid complete run. An invalid canonical roster, malformed policy, unresolved source, missing required target or failed preparation cannot safely fall back: the run is invalid/inconclusive with zero dependent launches and no green. Keep this distinction visible. A timeout/provider failure in the planner uses the same deterministic fallback; no interactive agent is required to make progress.

The accepted graph is frozen before dispatch and rehashed at each host. Final state is computed from this graph and verified events, never the proposal or reported node count. All required leaves must have either verified nonzero complete passing execution or an allowed proven reuse disposition. Dependencies, cleanup, evidence retention and terminal accounting must also be complete. An absent/canceled/unresolved node prevents green even if all observed nodes passed. Invalidity outranks red, red outranks inconclusive, then complete pass; canceled is non-green. Keep executedNow, passedNow, reusedObligations, failed, inconclusive and missing separate. Graph pass is diagnostic until independent existing release validators accept the required Full evidence; it does not set complete-green, tag or publish.

## Default smart ordering and history

Coverage selection and ordering are separate operations. First preserve the complete obligation graph and mark only proven eligible reuse leaves. A newer failure/inconclusive/revocation for that input invalidates older green; a lane with such history runs even if its fingerprint equals an older receipt. Lanes without qualified fingerprints/receipts, changed lanes plus affected downstream consumers, and always-run/native environmental obligations execute. An unchanged lane is skipped only with a validated use record; it never disappears.

Among executable ready leaves, apply this lexicographic order:

1. Previous-run failures: any current chunk intersecting a failed/timed-out/error class/UID in the prior run of this line. Missing-terminal evidence after an attempted launch is conservatively unfinished/inconclusive, not fabricated pass; S1's proved failure/hang mapping remains usable. Failure prerequisites inherit the best priority of their descendants, so a build does not wait behind ordinary tests.
2. Never-run or unfinished obligations: never-completed sorts before timestamped completed history; otherwise oldest last completed execution time first. Track prior-start-without-terminal and planned-but-unstarted distinctly. Sorting uses per-obligation evidence mapped into current chunk membership; a chunk with any never-completed member is never-completed. For completed members use the oldest member completion, conservatively. Display the lane's last passed chunk and a contiguous canonical pass frontier; resume after that frontier, retaining earlier holes. A high numbered chunk passed out of order does not erase unfinished lower chunks.
3. Remaining executable work: Full reruns previously passed chunks; Affected executes changed/no-receipt/always-run lanes only. Stable key after priority and age is lane canonical ordinal, position after its proven contiguous frontier (wrapping unresolved holes), canonical chunk ordinal, then ordinal node ID. No duration-based shortcut can overtake an older or failed obligation. Canonical ordering derives from the pinned production roster, not hash-table enumeration or locale.

Example: prior A1 passed, A2 failed, A3 unstarted; B1 passed, B2 interrupted; C has an unchanged qualified green receipt. Smart Full runs A2 first, then A3/B2 by age (null/unknown first, stable tie), then the passed work including C. Smart Affected makes C visible as skipped-with-receipt and still runs A2, A3 and B2. A later A4 pass leaves the A frontier at A1 until the A2 hole is filled. The fixture must verify the actual launch trace, not just the proposal JSON.

### Previous-run source and fallbacks

Add a derived lineage run index in the qualification evidence root: terminal run ID/attempt, repository/ref/root/source, candidate, closed time, pinned per-suite planned index, executed-journal pointer/generation/seal/state digests and summary. Update atomically only after terminal accounting or a separately recorded supervisor-aborted terminal observation. The index is a cache/hint; original immutable evidence is retained. Select the explicit last closed attempt for the line, not the newest directory. A working run is not a complete previous run; do not snapshot it while another supervisor owns it.

Read CARD-1514 through Read-TestPhaseExecutedPrefix, validate generation, contiguous sequence, source/run/attempt, terminal/gap state, per-suite plan and TRX digests. Join suite + canonical class/expanded UID; never match bare all-group-N across suites. Use S1 export/resolution for failure identities. Chunk-local pass requires exit 0, correct fresh TRX, full required UID union, all required outcomes Passed, and completed custody; pre-verdict cumulative flags cannot establish it. A valid prefix plus an independently terminal aborted run identifies unfinished planned leaves; a corrupt/mismatched prefix is not a valid partial history.

The index may retain last-start/last-complete/last-pass per stable obligation across earlier closed runs, with provenance to each manifest. It is reconstructed once at planning, never rescanned for each chunk. Unknown first-seen/completion time stays null and sorts conservatively. Current splits map observed failures to every intersecting chunk; renamed/new identities are unrun. An unresolvable or contradictory historical membership disables history globally, rather than optimizing a guessed subset. Old source or policy hashes may legitimately differ from the candidate; validate them against their own evidence, then resolve against current membership.

| History condition | Required behavior |
|---|---|
| Missing pointer, file or planned membership; malformed/unsupported document; wrong digest/generation; unexplained publication gap | Record specific history-unusable reason; Full + Original over the entire current catalog. Preserve raw evidence. |
| Different repository/ref/root, source not an ancestor, force-moved lineage, future time beyond allowed skew | Same full fallback; no old failures, age or reuse credit imported. |
| Latest closed attempt cannot be established, record older than D-4 horizon, copied stale projection | Same full fallback; state why it is stale. No mtime inference. |
| Valid terminal-aborted attempt with intact planned roster and committed prefix | Prior failure first, prefix holes/unstarted leaves next; no execution credit from the prefix. |
| Valid history with no failures | Oldest unfinished first, then stable remaining; it is not a reason to return an empty plan. |
| A single lane has no qualified fingerprint/green receipt but history is otherwise valid | That lane executes; reason no-receipt/unresolved-input, preserving other independently qualified lane reuse in diagnostic Affected mode. |

Full explicitly ignores lane skips and reuses no results from earlier qualification attempts. A controller restart of the same immutable run may retain that run's verified finished nodes under D-8; it is not a new Full qualification. The scheduled master readiness gate and RC cut/publish/promote remain Full, with separately required live obligations additive only when CARD-1545 is activated. Affected graph execution is initially diagnostic/manual or shadow comparison, never master readiness or release credit. Operator approval of a future scoped gate requires CARD-1545's independent census/closure/issuer/Full-shadow qualification; no graph flag can enable it.

Starvation prevention: freeze admission, attempt each node once, never retry a deterministic failure automatically, bound unavailable-resource waits, and continue independent ready work after red/inconclusive. This gives finite progress to always-run lanes without violating failed-first. Preserve age/debt across canceled candidates; automatic candidate replacement must not continually discard an admitted graph. Cancellation or an expired execution window leaves explicit unfinished debt and non-green, not a completed run. A permanently unavailable target cannot be made to progress by scheduling; report it while other targets finish, retaining its obligations.

## Executor, placement and recovery

Proposed front door: scripts/run-qualification-graph.ps1 with Plan, Validate, Run, Worker, Status, Cancel and Render operations. Inputs are pinned graph/proposal/validation paths, expected digests and a separately provisioned target-binding document. Operations and switches are proposed, not currently runnable. The binding maps a logical host label to one observed runner/host identity, approved transport, checkout/evidence roots, OS/runtime, native coordination domain, account enrollment and resource budgets; it contains no credential values. Moving a target after admission requires a new binding/validation attempt; never silently reroute.

| Logical target | Initial execution and limit | Placement/resource rule |
|---|---|---|
| windows-rc | Existing prepared nightly coordinator/chunk owner, parallelism 1 | Require Windows; one worker holds the existing shared native lock through preparation, chunks, final observation and joined cleanup. Same host's live lane shares that exclusion. |
| linux-projects | Independent checkpoint-backed project selections, integer N | Portable placement follows live default (currently the caller's Linux/server2 lane); no embedded host pin. Initial N=1; qualified N=2 trial uses separate checkout/output/results/schema resources. Windows-only coverage is not reassigned. |
| live-claude | CARD-1545 wrapper and exact lane manifest, concurrency 1 | Separate provider/account quota and context, dedicated enrolled Windows profile. Not automatically enabled or authorized to spend. Share native lock and host aggregate cap when co-located with windows-rc. |

The controller computes the ready set and dispatches a typed node envelope (run/graph/node/attempt/lease generation, expected source/build/selection/target digests, prerequisites and deadline) to the bound worker. Add scripts/lib/qualification-target-transport.ps1 with Submit, Observe and Cancel seams. Initial adapters are local process and commissioned SSH to a fixed reviewed worker entrypoint; the repository has a Windmill-to-Windows SSH precedent but no reusable remote graph API. Do not copy its permissive host-key flags: require enrolled host verification and approved credential custody. Transfer digest-bound data files; launch only the fixed entrypoint with validated opaque identifiers, never planner text as a remote command. Quoting/path controls are native witnesses. Do not create a new server route, modify the production runner or start an agent per node.

Remote commissioning must provide a lifecycle-owned worker/job on each host and prove its observed identity. The parent owns the transport process to terminal completion; a lost transport is not proof that the remote worker stopped. Host-local durable claims and cancellation handle that uncertainty. Where such remote ownership is not yet qualified, permit local/shadow execution only and leave the remote target not-run. This is a rollout prerequisite, not an invented existing capability.

Worker admission repeats graph validation and attestation, obtains host-wide capacity and the native lock where required, then requests the existing build slot. Effective concurrency is the minimum of graph target N, enrolled host budget, cross-target physical-host cap, resource exclusions and granted build slots; provider quota is an additional bound. Lock-acquire is a completed graph prerequisite whose lease remains held by the worker until lock-release/finally; it does not occupy a task slot forever. Every native node rechecks still-held ownership. Resource-held edges visualize this obligation but do not release the lease. Never hold a build slot while waiting for native/provider capacity. Slot timeout is not-run/inconclusive, never unleased execution.

Build once per source/target/project graph, share compatible immutable dependencies on that target, and run test chunks with their certified build. Do not copy Windows outputs into Linux. Give concurrent Linux projects isolated output/results/temp roots and DB schemas; enforce a cross-process resource key for Antiphon.Tests versus Antiphon.Agents.Pty.Tests/FakeClaude, since their assembly-local limiters are insufficient. Unknown shared fixture/resource ownership forces serial execution. Native Windows stays serial even if multiple graph targets resolve to it. No build/test bypasses build-slot.ps1 or run-checkpoint.ps1; the wrapper and already-leased child must not take nested slots accidentally.

Co-located native targets share one host supervisor and one native lease epoch, not separate competing lock owners. That owner prepares both logical targets serially, then runs their validated ready nodes serially; its existing PID/start/run/lane/continuation identity stays unchanged. The Windows and live adapters must consume a private graph-bound inherited-lock context, verify the allowed node and still-live owner through the existing continuation contract, and avoid reacquiring the same lock. No public bypass switch is added. This is essential to avoid a prepare-all deadlock where Windows holds the lock while waiting for live discovery that needs that lock. Validate the preparation wait/resource graph as well as the execution DAG. Until both adapters support this same-owner path, refuse that co-located graph before preparation; do not wait indefinitely or invent a second coordination root. Separate physical hosts have independent enrolled coordination domains. V-8 and V-11 include the co-located two-lane preparation trace and one-owner/no-nested-acquire assertion.

Each attempt uses a durable host-local exclusive claim keyed by graph digest + node ID + attempt. Persist accepted -> running with process/boot generation -> terminal with evidence digests. Duplicate Submit returns the same claim/result, never another process. The controller has its own single-writer run lease; restart takes a new fencing generation and reconciles workers before dispatch. Workers reject stale-generation mutations. A returned terminal receipt includes exact nonzero UID/outcome reconciliation, source/build, owner exit/job-empty/drain, target binding, quota usage and artifact digests. Append one controller event for each accepted observation; store remote artifacts immutably and verify bytes before accepting them.

Same-run recovery reads the accepted graph, host claims and receipts. Reuse a passed node only with exact graph/source/build/selection and target environment identity, complete trustworthy receipt, and no new invalidation. Reattach observation to a live owned attempt; never reclaim a lock/lease by age. Unknown ownership is inconclusive and blocks replacement of that node. A definitely terminated attempt without a receipt remains inconclusive; retain raw results, and allow a new explicit attempt only after joining custody. Never automatically rerun a possibly submitted live prompt. If the native host supervisor itself died, the existing stale-shared-lock refusal remains: recovery needs the release-gates.md ownership proof and explicit stale-lock recovery before a new owner can resume. A controller restart with its host worker still alive does not need to replace that owner. Corrupt state preserves the original files and refuses resume; a new Full run is separate. The CARD-1514 timing journal is projected alongside these events but is not this execution ledger.

Cancellation freezes new dispatch and persists cancellation intent, then signals only owned workers/attempt generations. They stop/join their owned process trees, drain output, record terminal facts and release resources in finally. Failed/unreachable cleanup retains ownership records and non-green state; do not delete locks or kill by process name. Cleanup finalizers use terminal edges, so a test failure does not suppress cleanup. Final verdict waits for terminal accounting or explicitly records unknown custody. A lost control connection, stale heartbeat or idle diagnosis alone never kills or restarts a session.

Provider handling follows CARD-1545: at most one pre-submission availability-probe retry inside its 30 s allowance, no quota-exhaustion retry, and no automatic model-test resubmission after UserPrompt. Its zero-prompt post-failure probe is classification only, bounded to 15 s. A typed correlated provider outage/auth/quota condition is INCONCLUSIVE; arbitrary assertion failures remain red. Preserve both original result and classification. No outage, setup skip, zero execution, missing evidence or failed teardown can make a lane green. No credential copying or automatic paid fallback.

Input-wait checklist: the graph adds no interactive login/question wait. Admission reports missing auth before taking native capacity; the optional planner returns a bounded proposal or deterministic fallback. Existing live C680 children may deliberately wait for permission/question input; CARD-1545's owner cancels/disposes/joins them on completion or the existing six-minute test deadline, with separately bounded cleanup, and its lane has a provisional 25-minute execution ceiling pending authorization. A delegate that asks a human retains its seat until answered, canceled or explicitly released; with parking disabled there is no automatic release deadline (CARD-1083). None of these deadlines becomes a production session idle-kill rule.

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
      C[Live Claude: execute or receipt reuse] --> F[Validated final verdict]
      WR --> F
      L1 --> F
      L2 --> F

Do not add A2 -> A3 -> A1 success edges just to draw serial order: that would suppress the rest after red. Ordering is the ready-queue policy and target capacity; real prerequisite edges mean something different. Resource and dependency edge types are labelled in the artifact. Generate lane-collapsed and detailed views from the same graph/overlay; rendering failure is diagnostic, never permission to change execution or verdict.

Client recommendation: initially attach/link Markdown through client/src/features/artifact/ArtifactViewer.tsx and its existing MermaidDiagram.tsx, reachable from client/src/features/delegations/TaskDrawer.tsx. A later read-only qualification artifact/status API could supply the same graph digest and overlay; no UI design, backend route or client edit is included in these slices.

## Composition and implementation slices

Failed-first S1 remains the failure-evidence decoder. S2's canonical reference permutation and prepare/invoke/complete seams remain; its priorFailure partition becomes priority class 1. Oldest unfinished class 2 is new. S3's actual launch wiring, per-suite accumulation, single publication context and suite-qualified heartbeat remain integration prerequisites. Once opted into graph execution, the graph scheduler replaces S2's flat dispatch-schedule decision, with an optional projection for old diagnostic readers; it does not repack chunks or replace coverage reconciliation. Keep non-graph S2 behavior intact. Do not revive PriorFailureSweep/Partial authority: graph Affected is distinct and non-credit.

CARD-1514's incremental journal is the historical timing/outcome input and diagnostic output, with one writer per worker/run root. Do not let multiple target workers append to one journal context. The controller aggregates verified per-target references once; retain the original per-target evidence. CARD-1545 owns category/project closure, live exact membership, fingerprints and trusted receipt validation. Until its implementations and policy decisions are available, use conservative Full and fake receipt seams in portable tests; do not create a competing fingerprint/issuer implementation.

All slices below are sequential at shared-file boundaries, estimated 30-60 minutes of Code including the named narrow verification. A slice that cannot fit must stop at a pushed boundary and be split before further work, retaining incomplete obligations. Ordinary Review/landing and operational qualification are separate. Rollback means disabling the new graph entrypoint for a new run and retaining evidence, never changing an in-flight sealed graph or rewriting a receipt.

| Slice / minutes | Files to add/change and result | Tests / checkpoint | Risk and rollback |
|---|---|---|---|
| S1 / 45 | scripts/schemas/qualification-run-plan-v1.schema.json; scripts/lib/qualification-graph.ps1; initial scripts/test-qualification-graph.ps1. Parse/canonicalize model and map catalog identity. | QualificationGraphContractTests.SchemaAndIdentity; CP-1 | Schema drift; versioned opt-in only. |
| S2 / 50 | qualification-graph.ps1; nightly-tests-impl.ps1 prepared-plan adapter only. Export full canonical roster, grouping and prepare/execute binding, no alternate executor. | QualificationGraphCatalogTests.CanonicalRoster; CP-2 | Dropped suite/expanded UID; legacy execution remains default. |
| S3 / 45 | new qualification-graph-validation.ps1. Required-set, filter, edge, cycle and target-policy checks. | QualificationGraphValidationTests.RejectsIncompleteGraph; CP-3 | Validating proposal against itself; independent catalog oracle required. |
| S4 / 50 | new qualification-graph-history.ps1; reuse test-phase-evidence.ps1 accessor and nightly-failure-history.ps1 without changing their authority. Lineage index, local outcome decoding and fallback reasons. | QualificationGraphHistoryTests.HistoryAndFallback; CP-4 | Cumulative false outcome/stale projection; all-history fallback preserves Full. |
| S5 / 45 | new qualification-graph-order.ps1. Deterministic ready order and inherited prerequisite rank; no repacking. | QualificationGraphOrderTests.FailedOldestStable; CP-5 | Holes/starvation; switch to Original for new runs. |
| S6 / 40 | new qualification-graph-reuse.ps1 as adapter to landed CARD-1545 interfaces; Full refuses skips, diagnostic fake seam while unavailable. | QualificationGraphReuseTests.ReceiptConservation; CP-6 | Stale reuse; disable Affected, run Full. |
| S7 / 40 | run-qualification-graph.ps1 Plan/Validate operations; qualification-graph-validation.ps1 proposal audit/fallback. Agent input/output is a data file, deterministic mode needs no agent. | QualificationGraphProposalTests.AgentCannotChangeCoverage; CP-7 | Hallucinated command/omission; retain rejected proposal, deterministic Full. |
| S8 / 55 | new qualification-graph-executor.ps1. Local ready queue, target/host/resource counters, typed dispatcher seam and terminal aggregation; fake children first. | QualificationGraphSchedulingTests.TargetAndResourceLimits; CP-8 | Oversubscription/deadlock; local serial opt-in only. |
| S9 / 50 | new qualification-graph-state.ps1. Durable claims, single writer/fencing, exact same-run receipt validation and restart reduction. | QualificationGraphRecoveryTests.NoDuplicateAfterRestart; CP-9 | Lost acknowledgement; unknown retains custody, resume disabled until qualified. |
| S10 / 40 | qualification-graph-executor.ps1; state library; Run/Status/Cancel front doors. Bound stop/join and finalizers, no broad kill. | QualificationGraphCancellationTests.CancelPreservesCustody; CP-10 | Orphans/false pass; refuse new starts on unresolved ownership. |
| S11 / 55 | new qualification-graph-windows.ps1; narrow opt-in nightly-run-impl.ps1/nightly-tests-impl.ps1 S3 adapter and parameter forwarding. Preserve shared lock/build/observation lifecycle. | QualificationGraphWindowsAdapterTests.RealCoordinatorTrace; CP-11; existing RcManifestIntegrationTests.LedgerAndReceiptBytesAreUnchanged, CP-12 | S2/S3 integration collision; await their reviewed landing, retain legacy path. Portable fake-process evidence only. |
| S12 / 50 | new qualification-graph-checkpoint.ps1; Linux target local adapter to checkpoint run/wait/receipt primitives and isolated project resources. | QualificationGraphCheckpointTests.IsolatedProjectsAndSlots; CP-13 | Nightly Windows owner accidentally invoked; explicit adapter discriminator and no unleased fallback. |
| S13 / 45 | new qualification-target-transport.ps1 local/SSH envelope adapters with injected transport in tests; fixed worker entrypoint admission. | QualificationGraphTransportTests.HostBindingAndUnknownAck; CP-14 | Wrong host/arbitrary command/lost ACK; remote disabled pending commissioning. |
| S14 / 40 | new qualification-graph-live.ps1 adapter; narrow inherited-lock context in CARD-1545's scripts/run-live-lane.ps1 when landed; typed outcomes, quota ledger and no-resubmit rule. | QualificationGraphLiveTests.OutageNeverGreen; CP-15 | Spend/double prompt/nested native lock; fake provider only, live disabled without existing authority and same-owner admission. |
| S15 / 40 | new qualification-graph-render.ps1; Render operation and docs/release-gates.md/testing-and-build.md contract text. Generated graph and status overlay from actual events. | QualificationGraphRenderTests.OverlayIsNonAuthoritative; CP-16 | Misleading view/unsafe labels; JSON remains canonical, disable rendering. |
| S16-W / 55 | Native qualification of Windows adapter/claims/transport argv in scripts/test-qualification-graph.ps1 and QualificationGraphNativeTests.cs; only bounded fixes if exposed. | NativeWindowsLifecycle; CP-17 | Windows lock/job/filesystem differences; zero paid tests, retain no activation until receipt. |
| S17-L / 50 | Native Linux checkpoint worker isolation/restart qualification, same harness and QualificationGraphLinuxTests.cs. | NativeLinuxParallelIsolation; CP-18 | Cross-process fixtures/unknown custody; keep N=1 if failed. |

New C# wrappers named in the table live in tests/Antiphon.Tests/Scripts/; each slice extends the common PS harness with its named case. Integration wrappers use the assembly-local ProcessSpawnLimit and owned ScriptHarness; no AppHost/provider/DB unless explicitly named in the native fixture design. New scripts require the existing script-census dispositions and classification witnesses, scoped to the slice. Do not edit generated policy hashes manually. No new project/reference/solution definition is planned; if one becomes necessary, follow the owner build-graph audit once after final definition changes and amend the scope/checkpoints before execution.

## Performance budget

Apply docs/testing-and-build.md#performance-standards and docs/test-performance-standards.md R1-R8. This Plan runs no tests because it changes one Markdown file only. The future checkpoint budgets below are provisional; there is no measured p95 for these new tests. Estimates are not permission to silently extend production watchdogs or infer a zero-cost phase.

| Evidence / phase | Measurement or initial budget | Bound / acceptance |
|---|---|---|
| Historical real roster replay | Failed-first plan records 19,012 nodes in 15.847 s and 23.778 s, each n=1 at different source/load. Not a comparable p95. | Share one expanded immutable fixture per harness process; use for conservation/scale, not a current fixed node count or a timeout calibration. |
| Historical cold script overhead | CARD-1514 CP-8 investigation at eaea94cffa8363f6074d51e146916992545a32da: 20 fresh Linux children/arm; worst comparable child p95 12.3959586 s, body p95 6.9710063 s; loaded host identified in that report. | Its 26 s child/15 s body limits are measurements for that witness only. Do not assign these p95s to new graph tests or Windows. |
| Isolated build per slice | 4-6 min provisional; historical loaded build 413.23 s in CP-8 investigation is one sample, not p95. | Initial outer row/build envelope 14m; CARD-1404/1531 provisional coupling exception. Record build independently. |
| Portable graph method | 30-90 s expected including owned PS startup; parser/queue body typically below 30 s. | Initial test/harness body 180 s, child deadline 180 s plus 10 s cleanup; enclosing TUnit deadline 210 s. Provisional upper-bound exception under CARD-1514/1404, replace with about 2x comparable measured p95. |
| Native fake-child lifecycle method | 60-120 s expected, no real qualification/provider. | 240 s body, 10 s cleanup, enclosing 270 s. Record individual child readiness/exit/drain; no unbounded sleeps. |
| Controller planning / rendering | Target <30 s at 19,012 UIDs and a synthetic 1,000-chunk catalog; three in-process pure passes reuse one fixture. | 60 s initial component bound, n=3 remains provisional; O(V+E) validation plus O(V log V) ready sorting, no per-dispatch complete-history reads. |
| Single slice green | CP-1..16 generally 7-8 min including one build; S11 two rows totals 9 min; native slice 10 min. | One committed slice group per tool run; execution budget 20m and hard wall 25m, queue budget 5m separately. No invocation combines all slices. |
| Provider/transport waits | Live probes inherit CARD-1545 30 s retry allowance/15 s classifier probe; local test transport uses controlled clocks. | Remote data-call initial deadline 30 s, bounded reconnect observation 60 s, no silent retry on unknown Submit; provisional CARD-1531 until measured. Cleanup failures remain non-green. |

Checkpoint row estimates sum to 132 minutes across 17 separate Code tasks, not one selection; total Code slice estimates sum to 795 minutes. Baseline-red authoring runs are charged within each 30-60-minute slice, with one exact discriminating method and source-bound evidence before its fix. Independent Review, tool bootstrap if needed, Debug, native operational commissioning and a Full qualification are additional costs. Do not run the whole test assembly or Full RC as ordinary Code verification. If a slice forecast exceeds 60 minutes, split it at a named file/behavior boundary and amend the manifest before proceeding.

Code uses the checkpoint tool, one run per committed After group, with explicit duration suffixes and expected source SHA; wait through exit 75 to terminal completion. Every build/test driver acquires the host build slot; no unlisted speculative builds. The checkpoint importer's 15-minute/3x defaults are not the performance policy. Replace provisional deadlines with approximately ceil(2*p95) once comparable samples exist, recording source SHA, host/OS/runtime, load, sample count, selection, concurrency and cold/warm setup. A three-sample pure timing check is diagnostic, not calibrated p95; collect subsequent ordinary green/Review observations rather than repeatedly rerunning solely to manufacture a percentile.

Setup once per harness invocation: parse the policy/discovery/catalog, build immutable fixture rosters and import script libraries. Per case use separate small evidence/state roots and fake clock/process boundaries. Preparation, scheduler and transport tests need no Docker, host, migrations or database; mark these not-used, not measured zero. Native probes use fresh tiny owned children because process custody/restart is the subject; measure that cost explicitly. Real project execution later shares expensive AppHost/container/DB/migration setup once per assembly/run, uses schema/template/transaction isolation and records fresh-start exceptions. Graph design does not silently repair existing per-chunk fixture costs.

Retain ignored evidence under .antiphon/qualification-graph/<source>/<lane>/<run>/ for Code, and the enrolled external qualification evidence root for operation. Include exact test/UID/category/project graph, graph/target binding digests, phase spans for slot admission, build, discovery, preparation, snapshot/clone, body, publication, transport, cleanup and remainder. Record every filesystem/process/HTTP/DB call category with bounded elapsed/outcome; unused phases are not-used and unavailable phases unknown. Wall time is separate from summed concurrent spans. Estimates come from recorded comparable successful/censored timings: never treat a timed-out run as a complete p95 or old class prices as a measured chunk distribution. Keep canonical packing prices/watchdogs unchanged by ordering.

Every expected/observed selection, batch or run over 30 minutes requires Debug with its exact selection/source/load/timings before repetition. The historical 1378.1-minute Full and its publication cost are evidence for CARD-1514/0856 investigation, not permission for another unbudgeted Full. A future operational Full graph must have a separately commissioned Debug/measurement obligation; graph rendering or parallelism does not waive the complete-run threshold. Measure request-to-first-failed-node launch, first failure, tail debt, total makespan and per-target busy/admission time on the next authorized run; do not promise failure feedback in two minutes when preparation itself costs minutes.

## Verification design

This section is a concrete design for plan Review and provider-free Code, not passing evidence. Every new TUnit method below is one unparameterized result, even when its harness checks a matrix. Expected assertion-RED must occur at the product boundary: zero tests, fixture/build errors and fabricated JSON passing its own validator do not count. Commit the failing method before its fix. Later SourceLanding Mutation runs each control method-scoped, restores mutations and repeats that exact method green; no broad positive-control run.

| Witness | Required cases and discriminating observation |
|---|---|
| V-1 SchemaAndIdentity | Parse valid prepare/execute examples with exact typed nulls; reject duplicate keys, unknown version/field/action/path/target, duplicate IDs and malformed digest. Roundtrip canonical hash stable. A permissive parser/alias collision must fail. |
| V-2 CanonicalRoster | Real production planner over committed c1410 listing plus companion suites/non-TUnit obligations; independent UID census equals graph leaves, exclusions separate, splits/collisions preserved. Prepare graph is complete before launch; no test before sealed discovery. Drop a late suite or one argument expansion and the equality assertion fails. |
| V-3 RejectsIncompleteGraph | Independently mutate each guard after a valid control: omission, duplicate, unknown leaf, filter widening, overlapping UIDs, removed build edge, cycle, fake receipt, Windows N=2 and same-host alias. Assert refusal with zero dispatches. No assertion compares two outputs of the same validator. |
| V-4 HistoryAndFallback | Real CARD-1514 accessor with sealed and terminal-aborted fixtures, stale projection, wrong-suite numeric chunk, missing/gap/corrupt generation, different lineage, legitimate ancestor/different policy, seven-day stale/future time. Prove cumulative false cannot turn a local pass red, nor cumulative true turn a local failure green. Every unusable optional history yields the entire exact canonical Full/Original trace. |
| V-5 FailedOldestStable | Actual keyed fake-launch boundary over non-identity cross-suite order; previous failure first, never/unfinished oldest next, stable tie, out-of-order frontier hole, inherited build priority, one attempt per node, independent always-run tail reached after red. Dropping ordering at the coordinator or retrying the head indefinitely fails. |
| V-6 ReceiptConservation | Valid same-line trusted receipt control; changed transitive dependency, deletion, untracked/unknown build input, wrong issuer/host/account, revoked/newer-red, expired live policy, unrelated line and forged all-green summary all force execution/refusal as appropriate. Full never skips. A valid diagnostic reuse remains visible with zero newly executed count. Use actual landed CARD-1545 validator; until available leave that integration row incomplete, not fake-green. |
| V-7 AgentCannotChangeCoverage | Agent proposes missing tail, arbitrary action, invented receipt/estimate, reordered priorities or raised cap; validator rejects and deterministic Full succeeds. Timeout/no response takes same fallback. Corrupt canonical policy refuses rather than falling back to an incomplete roster. |
| V-8 TargetAndResourceLimits | Controllable barrier children record peak active per target AND physical host/resource. Windows=1, Linux=2 when qualified config allows, co-located live/native exclusion, Antiphon.Tests/Pty exclusion, independent target progress and no held slot while native lock busy. A removed semaphore must show peak>cap, not a timing guess. |
| V-9 NoDuplicateAfterRestart | Kill/restart controller at accepted, process-start, terminal-write and lost-ack boundaries; fresh process imports durable claims. Exactly one concurrent launch per node/attempt, duplicate Submit idempotent, old fencing token refused, unknown owner not rerun. A matching same-run pass resumes; tampered source/build/graph/receipt does not. Timing-journal-only pass grants no resume. |
| V-10 CancelPreservesCustody | Cancel before start, during owned child and while transport unavailable. No later admission, only owned child signaled, output/descendants joined, release after cleanup, unreachable remote retains unknown state. A success-only cleanup edge or early lock release fails external process observations. |
| V-11 RealCoordinatorTrace | Exercise actual S3 nightly entrypoint through graph adapter with deterministic process seam. Nonidentity launch trace, source/target/hop digests, one shared preparation, one publication context, suite-qualified heartbeat identity, final union and no native overlap. S2 legacy absent/empty-history trace remains unchanged. No-op graph adapter must fail. |
| V-12 ManifestRegression | Existing RcManifestIntegrationTests.LedgerAndReceiptBytesAreUnchanged stays green. Compare immutable authority/UID data; preserve S2's explicit order-dependent diagnostic exceptions, including sticky first suite and cumulative fields. This is not a new authority-production claim. |
| V-13 IsolatedProjectsAndSlots | Real checkpoint request/receipt adapter with fake driver and isolated roots: certified build reused only within source/target, exact filters and counts, one slot, exit 4 not-run, exit 75 observed until terminal, no Windows owner on Linux, failed receipt never pass. |
| V-14 HostBindingAndUnknownAck | Fixed local/SSH transport launch composition and receiver validate same graph/host/source/lease. Spaces/non-ASCII paths and malicious graph labels stay data, wrong host refuses, duplicate/reordered replies do not launch twice. Lost Submit acknowledgement observes existing claim rather than retrying launch. No real remote service in portable case. |
| V-15 OutageNeverGreen | Actual live adapter receives typed no-auth/quota/outage, healthy assertion red, unclassified setup, zero results, teardown failure and valid green. At most one pre-submit probe retry, zero resubmission after UserPrompt, quota debit survives restart, no automatic billing fallback. Core caller admission remains CARD-1545's tested responsibility. |
| V-16 OverlayIsNonAuthoritative | Render actual accepted graph/events including every status, reused-count distinction, escaping and deterministic edge/target membership. Tamper overlay green while execution ledger is missing/red: final verdict remains non-green. Renderer failure cannot change dispatch. |
| V-17 NativeWindowsLifecycle | Tiny owned fake executable only: real Job Object/locks, two contenders, real receiver argv, controller restart and cancellation. Peak native=1 including co-located logical targets; retain lock on unknown ownership; actual children joined. Missing Windows prerequisites fails/not-run, never skip green. Private deployed supervisor forwarding/readback remains separately commissioned. |
| V-18 NativeLinuxParallelIsolation | Two tiny owned checkpoint-compatible children in isolated roots with external barrier observer, explicit N=2 cap; restart/cancel and slot refusal, different fake DB schema identifiers, forbidden assembly resource conflict. Process group death/join proved externally. No Antiphon.Tests/Pty whole-suite parallel run and no production runner launch. |

All test names are proposed except V-12. Repository search found no existing QualificationGraph* classes. All checkpoint filters use one exact method, so there are no OR suffix collisions; Code rechecks the compiled namespace/method roster at its integrated source. If future consolidation uses OR, every operand must be parenthesized and end in *, and table pipes must be escaped; otherwise use separate exact-method rows. Do not introduce broad class OR to save a line. Check fresh TRX executed identities/counts against these exact methods.

### Checkpoints

Closed ordinary scope, one After group per committed slice. Lane is named in Group: portable uses default placement with no runner/platform pin; windows-native requires Windows; linux-native requires Linux because process-group custody is the subject. Native rows are separately dispatched and never interpreted as passed by portable seams. Each build uses a fresh bin- output with forward slash, through the checkpoint tool/build-slot gate, then removes all corresponding alternate outputs after completion. CP-12 reuses CP-11 only within S11 at the same source. No live paid selection is in this table.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-qgraph-s1/ | portable-contract | /*/Antiphon.Tests.Scripts/QualificationGraphContractTests/SchemaAndIdentity | V-1 | exact method, 0 failed/skipped | 1 | 7 | true |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-qgraph-s2/ | portable-catalog | /*/Antiphon.Tests.Scripts/QualificationGraphCatalogTests/CanonicalRoster | V-2 | exact method, 0 failed/skipped | 1 | 8 | true |
| CP-3 | S3 | tests/Antiphon.Tests -> bin-qgraph-s3/ | portable-validator | /*/Antiphon.Tests.Scripts/QualificationGraphValidationTests/RejectsIncompleteGraph | V-3 | exact method, 0 failed/skipped | 1 | 7 | true |
| CP-4 | S4 | tests/Antiphon.Tests -> bin-qgraph-s4/ | portable-history | /*/Antiphon.Tests.Scripts/QualificationGraphHistoryTests/HistoryAndFallback | V-4 | exact method, 0 failed/skipped | 1 | 8 | true |
| CP-5 | S5 | tests/Antiphon.Tests -> bin-qgraph-s5/ | portable-order | /*/Antiphon.Tests.Scripts/QualificationGraphOrderTests/FailedOldestStable | V-5 | exact method, 0 failed/skipped | 1 | 7 | true |
| CP-6 | S6 | tests/Antiphon.Tests -> bin-qgraph-s6/ | portable-reuse | /*/Antiphon.Tests.Scripts/QualificationGraphReuseTests/ReceiptConservation | V-6 | exact method, 0 failed/skipped | 1 | 7 | true |
| CP-7 | S7 | tests/Antiphon.Tests -> bin-qgraph-s7/ | portable-proposal | /*/Antiphon.Tests.Scripts/QualificationGraphProposalTests/AgentCannotChangeCoverage | V-7 | exact method, 0 failed/skipped | 1 | 7 | true |
| CP-8 | S8 | tests/Antiphon.Tests -> bin-qgraph-s8/ | portable-scheduling | /*/Antiphon.Tests.Scripts/QualificationGraphSchedulingTests/TargetAndResourceLimits | V-8 | exact method, 0 failed/skipped | 1 | 8 | true |
| CP-9 | S9 | tests/Antiphon.Tests -> bin-qgraph-s9/ | portable-recovery | /*/Antiphon.Tests.Scripts/QualificationGraphRecoveryTests/NoDuplicateAfterRestart | V-9 | exact method, 0 failed/skipped | 1 | 8 | true |
| CP-10 | S10 | tests/Antiphon.Tests -> bin-qgraph-s10/ | portable-cancel | /*/Antiphon.Tests.Scripts/QualificationGraphCancellationTests/CancelPreservesCustody | V-10 | exact method, 0 failed/skipped | 1 | 7 | true |
| CP-11 | S11 | tests/Antiphon.Tests -> bin-qgraph-s11/ | portable-windows-adapter | /*/Antiphon.Tests.Scripts/QualificationGraphWindowsAdapterTests/RealCoordinatorTrace | V-11 | exact method, 0 failed/skipped | 1 | 8 | true |
| CP-12 | S11 | CP-11 | portable-manifest-regression | /*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged | V-12 | exact existing method, 0 failed/skipped | 1 | 1 | true |
| CP-13 | S12 | tests/Antiphon.Tests -> bin-qgraph-s12/ | portable-checkpoint | /*/Antiphon.Tests.Scripts/QualificationGraphCheckpointTests/IsolatedProjectsAndSlots | V-13 | exact method, 0 failed/skipped | 1 | 8 | true |
| CP-14 | S13 | tests/Antiphon.Tests -> bin-qgraph-s13/ | portable-transport | /*/Antiphon.Tests.Scripts/QualificationGraphTransportTests/HostBindingAndUnknownAck | V-14 | exact method, 0 failed/skipped | 1 | 7 | true |
| CP-15 | S14 | tests/Antiphon.Tests -> bin-qgraph-s14/ | portable-live-adapter | /*/Antiphon.Tests.Scripts/QualificationGraphLiveTests/OutageNeverGreen | V-15 | exact method, 0 failed/skipped | 1 | 7 | true |
| CP-16 | S15 | tests/Antiphon.Tests -> bin-qgraph-s15/ | portable-render | /*/Antiphon.Tests.Scripts/QualificationGraphRenderTests/OverlayIsNonAuthoritative | V-16 | exact method, 0 failed/skipped | 1 | 7 | true |
| CP-17 | S16-W | tests/Antiphon.Tests -> bin-qgraph-win/ | windows-native | /*/Antiphon.Tests.Scripts/QualificationGraphNativeTests/NativeWindowsLifecycle | V-17 | exact native method, 0 failed/skipped | 1 | 10 | true |
| CP-18 | S17-L | tests/Antiphon.Tests -> bin-qgraph-linux/ | linux-native | /*/Antiphon.Tests.Scripts/QualificationGraphLinuxTests/NativeLinuxParallelIsolation | V-18 | exact native method, 0 failed/skipped | 1 | 10 | true |

## Risks, rollout and acceptance

| Failure mode | Detection / containment | Acceptance or rollback |
|---|---|---|
| Fingerprint misses dependency, runtime or remote state | Conservative old/new project closure, unknown input forces execution, independent CARD-1545 issuer/freshness checks and shadow Full comparison. | No scoped credit until qualified; disable reuse, retain old receipt and rerun Full. |
| Planner invents or drops a node | Exact canonical set/UID conservation and independently recomputed verdict. | Invalid proposal falls back; malformed canonical truth refuses. |
| Failures/old nodes starve tail and always-run lanes | Finite snapshot, one attempt, bounded resource waits, age/debt display, independent nodes continue. | Canceled/unfinished graph never green; investigate repeated tail debt instead of repeatedly restarting. |
| Previous record belongs to another candidate/line | Repository/ref/root/ancestry/run/source and evidence digest checks. | Same-line ancestor may inform order; incompatible/missing history falls back Full/Original. |
| Duplicate execution after restart or lost ACK | Durable host claim, idempotency key, fencing and process-generation reconciliation. | Unknown custody blocks replacement; no exactly-once promise for an unobserved remote effect. |
| Native overlap via two target labels | Physical-host/resource identity in binding, retained shared lock, runtime aggregate counters. | Failed native qualification keeps new executor disabled. |
| Graph pass falsely becomes release green | Separate verdict types, no Full marker on Affected, independent release gate/ledger/use-record validators. | No changes to existing gate booleans or publication commands in graph rollout. |
| Incremental publication breaks under parallel targets | One journal writer per target/run root; controller imports immutable digests once. | Keep legacy manifests readable; no shared mutable projection written by two hosts. |
| Linux executor assumes Windows nightly custody | Explicit adapter/OS check and native two-worker proof. | Linux N=1 or not-run until qualified; never OS substitution. |
| Remote transport/worker or private supervisor is unqualified | Native commissioning records exact script/source/host/argv, ownership and stop/resume evidence. | Local/shadow graph remains useful; remote activation stays pending, without claiming a live result. |

Rollout: land reviewed provider-free contract/history/order/validator slices first; shadow-render a complete graph beside existing qualification. Integrate only after reviewed failed-first S2/S3 and CARD-1514 publication are on the commissioned source. Qualify local Windows serial and Linux isolated worker behavior with tiny fake children. Then commission host transport and private supervisor forwarding/readback under the existing native lock. Enable the graph executor for a new pinned Full run, retaining legacy selection as rollback for a later run. CARD-1545 live activation and any future scoped gate policy remain independent. No running qualification is interrupted and no tag/publish/deploy is commissioned by this plan.

Plan-review decisions: confirm the seven-day hint horizon (D-4), staged Linux N (D-5), transport enrollment/activation boundary (D-9/D-11), and preserve Full gates until the independent scoped-credit work is qualified (D-3/D-6). These choices do not block provider-free planning or initial Code. Before operational activation, obtain the missing private supervisor source/launch record and measure its actual forwarding, lock inheritance, restart/cancel custody and graph readback; also verify Full authority issuance rather than assuming timing manifests provide it.
