# CARD-1836: safe incremental qualification activation

Plan date: 2026-11-12 (brief date; completed after UTC midnight). Task `0f623d08-26c7-4960-a9a0-d186a724d5ba`. Inspected source: `cc48c70050a2932eddf4abe18cea8659d478c33e`.

This is the executable activation amendment to [the original S1–S10 plan](2026-11-09-card-1836-incremental-rc.md), replacing its S2–S8 implementation order, input-equivalence assumptions and checkpoints. S1 is landed; retain its strict reader and CARD-1842 duration/host additions. Verification design is included, as this dispatch explicitly requires a Code handoff. No production implementation, qualification launch, deployment or release write is performed by this Plan.

The first new cut requests Incremental, admits only evidence-backed passes, executes every dirty obligation, and samples proposed carries. **A positive carry count from historical run `0b292fa4` cannot be promised.** Missing historical provenance, a changed execution contract, or no eligible Full anchor means Full execution. The next-cut preflight must state that result before spending qualification time. Calling an all-executed fallback “incremental savings” is forbidden.

## Ground truth

Sources: the full CARD-1836 board text read through `scripts/card.ps1 get CARD-1836 -Board Antiphon`; Investigation b9444a8c at `origin/feat/card-task-b9444a8c:docs/investigations/2026-11-12-card-1836-incremental-rc-qualification-result-ledger-boundary-chunks-dirty-marking-release-baseline.md` (report commit prefix `ff1982ee1`, not assumed landed); and the source at the full SHA above. Historical run facts below are supplied by the brief, not observations of the Windows disk.

| Card/previous-plan assumption | Code/evidence at inspected source | Activation consequence |
|---|---|---|
| S1 ledger makes Incremental available | `scripts/lib/rc-ledger.ps1` implements v1/v2 read/write and carry predicates; qualification has no caller | Add an admission envelope and actual scheduler/coverage callers; do not reimplement S1 |
| Seeds can resume green work | `nightly-stopped-run-seed.ps1` and `nightly-failure-history.ps1` use seeds for order; cross-candidate acknowledgement grants no execution credit | Seed imports never create carried passes |
| Existing chunks identify stable work | `ConvertTo-NightlyProfileLaunchGroups` in `nightly-tests-impl.ps1` packs namespace/time bins with ordinal IDs | Build membership IDs and preserve old source chunk IDs as provenance |
| A layer map describes complete dependencies | `layers.txt` describes future boundaries; the server is still one project, and the guard is not a complete runtime dependency proof | Use layers to pack, project-wide transitive input closure to authorize carries |
| CARD-1820 non-hit proves unaffected | Derived index is syntax-based, capped, partially allowlisted, with incomplete edges | Positive evidence only; never subtract from conservative closure |
| All execution inputs are already hashed | `Get-ReleaseSelectionContract` and `Antiphon.Scripts/ReleaseSelection.cs` hash policy/catalog, two selector files and `global.json` | Add harness, actual toolchain, packages, configuration, fixture and dependency manifests |
| A passed method describes all its cases | S1 `Get-RcIdentityKey` is `className/method`, without suite or parameter UID; its chunk predicate checks supplied decisions only | Partition ledger shards by suite and supply a complete case/method membership proof; no duplicate collapse |
| The historical run can simply be stamped | `rc-ledger-stamp-duration.ps1` requires an existing ledger; timing manifests can bind assembly/source labels, but no verified full historical admission exists | Import retained evidence without changing it; unknown facts remain unproven |
| Full-only gates are just a ValidateSet | Full-only checks also exist in selection attempt readers, compiled selector, dispatch permutation and coverage | Change each hop and require a bound plan digest; preserve Full and ChangeAware behavior |
| Incremental closure can use normal release success | `release-cut.ps1` calls the publisher after complete-green unless `-SkipPublish`; publication validates separate frozen authority | Incremental qualification has its own closure receipt and cannot enter automatic publishing |
| Last green release branch is an available baseline | Current reader requires a published `antiphon-full-release-baseline-v1` archive | Add a ledger baseline picker without weakening the published archive reader |
| Linux proves Windows qualification | Release gates label Linux `PlatformDiagnostic`; native owned-child launch requires Windows | Portable contracts on Linux; launch/closure proof and historical admission on native Windows |

No migrations, public HTTP DTOs/routes, operation labels, remote shell functions or SQL statement budgets change. Existing-test obligations and CARD-1782 matching are listed under Verification design.

## Decisions

### D-1 Scope, authority and defaults

Keep `Full` as the default mode. Add explicit `Incremental` with `IncrementalValidation=Shadow|Trusted`, default Shadow. The old `SelectionMode=Shadow` continues to mean the existing selector proposal and Full execution; it is not this rollout mode. No option disables provenance, unknown-dirty, forced-Full or membership checks.

Use PowerShell 7 for new planning/import/activation entrypoints, matching current release selection's requirement. Retain S1's PowerShell 5.1 reader compatibility; no claim that 5.1 can ingest the historical summary. New scripts that can enter Windows bootstrap paths remain ASCII-only. Artifact formats are versioned, strict, duplicate-key/type checked, and reject unknown required schema versions.

All generated receipts, manifests, TRX, logs and ledgers live under an owned evidence/release root, outside checkout, and remain gitignored. No database service is needed. No session-wait behavior is introduced or changed, so no waiting-session release checklist applies. Mutation execution is waived by the operator; ordinary assertion red/green witnesses and Review remain required.

Rejected: activating by `AcceptCrossCandidateSeed`, changing ChangeAware to selected execution, editing old evidence in place, or turning an old S1 green predicate into a source-provenance assertion.

### D-2 Storage and identity: preserve the strict S1 format

Keep v1/v2 ledger shards, **one suite per shard**, behind a new `antiphon-rc-admission-v1` envelope. S1's global `class/method` key cannot safely combine identically named methods from two suites. The envelope owns platform, repository identity, full candidate/source commit and tree IDs, rc line, run/attempt identity, contract version/digest, source-admission evidence, Full anchor, history root, input-manifest digests, current-universe digest, shard digests and terminal state. Existing S1 JSON fields are not silently extended.

Canonical obligation key: `(suite, fullyQualifiedClass, method, caseKey)`. `caseKey` is a stable discovery identity with the complete data-row set, not a TRX display name or a UID assumed stable across builds. If cross-build UID/case correspondence is not uniquely provable, the whole method is dirty. S1 gets one aggregate method row: passed only if every required case in the immutable case manifest passed; expected-skip only if every non-passing case is covered by the **current** platform skip declaration. Mixed pass/expected-skip aggregates retain that complete case evidence in the envelope. Missing, duplicate, added, removed or ambiguous cases cannot disappear through dictionary overwrite. A removed identity is recorded as retired in the comparison, never counted toward the new universe.

For launch IDs use `b-<boundary>-<first16(SHA256(canonical suite,boundary,sorted method keys))>` as in the earlier plan; persist the full digest and reject a short-ID collision. Preserve complete case sets separately. Class or method moves, packing order changes and old `all-group-N` IDs cannot confer carry eligibility. One new chunk has one suite and one boundary. An old mixed-boundary chunk is split into **logical provenance groups**, each retaining the original chunk/receipt identity. It is never described as multiple historical launches.

Write immutable revisions (`revisions/<sequence>/...`) and a write-once terminal envelope. Atomically replace an index pointer only after every referenced file is durable and read back by digest. Hold an owned local write lock; a second writer refuses. A crash leaves the previous revision readable and remaining work not-run; it never creates a closed green pointer. Readers follow retained origin envelopes to the actual executed result, detect cycles/missing links, and verify bytes. A copied `carriedFrom` alone is insufficient. Reference chains are bounded by the Full anchor and a maximum depth of 64; overflow forces execution.

Rejected: expanding S1's key encoding inside `method`, storing parameter rows as duplicate S1 identities, overwriting one `ledger.json` throughout a run, and treating an index pointer as integrity proof.

### D-3 Historical-run admission and its integrity argument

Target source commit: `56e96eabf698e9c1fcd462836ad32ed1fb259e83`; run suffix `0b292fa4`. The brief's retained root is `C:\Antiphon\releases\candidates\rc-2026-11-18-56e96eabf698\logs\2026-11-12-1213-0b292fa4`. This is an evidence input supplied to the later Windows task, never a fleet endpoint or a hard-coded script default. Do not access, stop, reconfigure or hash the live qualification in this dispatch. The later admission task requires a caller-confirmed terminal run and retained evidence; it never infers completion from a directory existing or from one unchanged stat sample.

New command contract: `scripts/rc-ledger-import.ps1 -SourceRunRoot <closed-retained-root> -EvidenceRoot <new-owned-root> -RepositoryRoot <object-store> -ExpectedSha <full-sha> -ExpectedRunId <resolved-full-run-id>`. It performs no test execution, build, network fetch or live-run mutation. Resolve the full run ID from retained records and require agreement; do not invent the suffix's missing characters. It emits an admission report even when every chunk is unproven. Source roots stay read-only. Output cannot be inside the source root or checkout.

Import has two phases, independently testable:

1. **Extract and freeze.** Stream SHA-256 over exact raw bytes and record relative path, length and digest for `summary.json`, each candidate chunk's TRX and `.timing.json`, discovery/expanded roster, planned group manifest, dispatch journal/terminal state, candidate journal, selection/authority receipts, source/build receipts, toolchain/package/config/image receipts and cumulative failure evidence. Resolve only confined regular files: reject traversal, duplicate/case-colliding paths, symlinks/reparse points, hard-link ambiguity and changing inputs. Copy through verified open handles to a new owned content-addressed bundle; hash the bytes actually parsed. Recheck source identity/length/change state around the copy and refuse unstable evidence. There is no delete or update under the historical root.
2. **Admit or record unknown.** Validate the joins in the table below, reconstruct input manifests from retained Git objects at the exact source commit using the new conservative contract, then write suite ledger shards and the admission envelope. Hashing the old evidence today establishes integrity from import onward; it does **not** establish what an old process executed. Every admitted pass must also have the contemporaneous provenance described below. Missing evidence produces an explicit reason and dirty row, never an operator override to “green”.

| Required proof | Admissible retained evidence and later verification | Missing/mismatch result |
|---|---|---|
| Repository and candidate | Normalized repository identity, full commit and Git tree object, candidate journal and source/run records agree; source commit must be available and be an ancestor of the next candidate | Reject cross-repository/lineage; missing objects = unproven |
| Exact working inputs at build | Contemporaneous clean source/input manifest checked against the commit (including generated/external inputs), build invocation and output seal; source snapshots before build and launch plus retained exclusive owned-checkout custody | A SHA in a filename, summary, PDB or phase header alone is insufficient; affected suites unproven |
| Executed assembly/dependencies | A retained build receipt binds the input manifest and command/properties/toolchain to output hashes; the discovery and launch receipts bind those same DLL/helper/dependency hashes and forbid `--no-build` from another output | Rebuilding today or finding an assembly with an embedded SHA is not retroactive proof; suite unproven |
| Historical scripts/environment | Actual executed script closure and tool versions, sanitized config identity, package/content hashes, immutable image IDs and generated fixture recipe/output hashes bind the invocation | Git reconstruction covers committed files only; unknown actual versions/overrides remain dirty |
| Complete original chunk execution | Dispatch start/end and custody/exit agree with the same run, suite, chunk, filter, assembly and expected cases; TRX terminal cases exactly cover that roster; no interrupted/timeout/failed/extra/duplicate outcomes | No pass from an incomplete or failed original chunk is admitted, including its individually green cases |
| Skip and failure history | Re-evaluate skips against current platform declarations; replay cumulative failures and CARD-1823 fix containment across retained attempts since the Full anchor | Unknown history or a same-digest fail/pass without a later contained fix remains unproven |
| Full-age anchor | A verified Windows complete Full closure with all required cases executed (expected skips explicitly accounted), no carries, timestamp and complete history through the candidate | Partial historical Full-mode execution cannot reset the seven-day clock; no anchor forces Full |

The existing planned phase manifest's source digest and assembly digest are useful join inputs, not a substitute for its missing build/input link. The current investigation did not establish that the old run retained the necessary source/build chain. Admission must publish `admitted`, `dirty`, `unproven` and per-reason counts, plus whether the Full anchor is known. This plan deliberately provides no “trust this old run” switch.

**Large evidence:** the approximately 885 MB `summary.json` is read with a bounded streaming UTF-8 JSON reader, never `Get-Content -Raw`, `ReadAllText`, `JsonDocument.Parse` of the file, or `ConvertFrom-Json` of the full text. New `scripts/lib/rc-historical-stream.cs` supplies the parser/hash reader through one in-process `Add-Type` load under PowerShell 7. Stream TRX with an XML reader with DTD/entity expansion disabled. Use per-chunk sidecars for outcomes; use summary only for bounded scalar fields and necessary joins. Skip large logs/arrays token by token without retaining their strings. Cap nesting, scalar size, entry count and file sizes; limit violations are unknown, never truncated success. Persist normalized rows incrementally and retain a bounded identity index; memory scales with the required roster, not total summary/log size. Default evidence file cap is 4 GiB, scalar cap 1 MiB, depth 64, normalized obligation cap 1 million; exceeding these refuses with a named reason. The 1 MiB scalar limit must skip uninterpreted large stdout strings without buffering them, while still hashing/validating their JSON syntax.

**Later-reader verification:** independently rehash the immutable bundle; validate strict schemas and the envelope digest; verify every source/build/launch/TRX/roster join; recompute Git input manifests for both commits; check current runtime contract, platform, lineage, Full age and failure history; rederive the exact carry/dirty partition. Do not cache trust by path or mtime. The integrity boundary is trusted qualification code and its owned OS account, not protection against an administrator rewriting the entire bundle and its authority. Hashes detect tampering relative to the recorded envelope; they are not signatures proving an unobserved historical event.

### D-4 Input-equivalence contract: conservative project closure first

A result carries only if its **complete** input manifest and runtime contract equal the new candidate's, its provenance is admitted, and S1's outcome predicate passes. Store canonical records with manifest version, owner suite/project, normalized repo-relative path, Git mode/type/object ID, presence/absence and dependency edge reason. Hash a sorted, length-delimited UTF-8 encoding with SHA-256. Include path names, not just blob IDs, so rename/deletion/addition is visible. Preserve ordinal comparison; reject Windows case collisions. Resolve symlink/submodule/LFS content by immutable target IDs plus bytes or mark unknown. Never hash only a glob that matched the old tree: enumerate both trees and include newly added inputs.

`scripts/lib/rc-input-manifest.ps1` uses a committed `scripts/lib/rc-input-owners.json` root/consumer map plus the conservative project graph. Inspect imports, linked items and references without running arbitrary historical MSBuild targets. Union conditional dependencies. If parsing or import expansion cannot prove that union complete, fall back to all projects/inputs in the affected suite; if the ownership is unknown, all suites are dirty. Do not present today's lightweight `rc-project-selection.ps1` graph as a complete negative proof.

| Input class | Mandatory initial scope and digest contents | Change/unknown behavior |
|---|---|---|
| Test source and discovery | Entire owning test project tree, partial classes, generators, assembly hooks, project items; exact current required method/case roster and exclusions | Any file change dirties that test project initially; dynamic or ambiguous case data dirties its method |
| Product source | Every file in every transitively referenced product project, including `src/**`, `server/**`, linked source, generated source recipes and build content | Any server file dirties all tests that reference the monolithic server project; an application-layer name is not an isolation certificate |
| Shared helpers | `tests/Shared/**`, owning `TestHelpers/**`, test base classes and linked helpers; referenced fixture executables/FakeClaude/FakeGrok and their projects | Every consumer project dirty; unknown consumers = all suites dirty |
| Invoked scripts/tools | Full owning harness script tree, dot-sourced/module/CLI target closure and source of compiled helpers, shell/PowerShell entrypoints, fixture scripts and referenced `tools/**` projects | Dynamic target without an explicit conservative root dirties all potential consumers; script tests initially include all `scripts/**` and `tools/**` |
| Config and settings | All owning-project `appsettings*`, JSON/YAML/XML resources, runsettings, profile/build flags, selected environment-variable names with safe value identities, policy/skip declarations and referenced root configs | Unknown/untracked configuration or live mutable service input = dirty; never persist secrets or guess a secret value hash |
| Fixtures/content | Owning fixture/data/snapshot directories, embedded resources, linked content, migrations/seed recipes, generated fixture outputs and inputs | Missing fixture, mutable download, nondeterministic generator or incomplete recipe = dirty |
| Docker/native/browser | Applicable Dockerfiles, compose files, build contexts and `.dockerignore`, immutable image digests; browser version/asset bundle, native binaries, architecture/RID and host feature contract | Floating tags or unavailable old image/tool identity = dirty; unknown consumer scope = all |
| Build/package/toolchain | ProjectReference closure; all applicable csproj/props/targets/imports, solution files, `Directory.Packages.props`, `NuGet.config`, `packages.lock.json`, resolved package graph/content hashes, `global.json`; client package/lock/configs where used | Policy/catalog/SDK/runtime changes force Full; missing locks require retained resolved graph/content proof, otherwise dirty; no implicit assumption that a lock exists |
| Actual runtime | SDK and runtime versions actually selected, PowerShell, Node/npm where used, OS family/architecture, build configuration/properties, locale/timezone and explicit environment contract | Separate Windows/Linux stores; actual SDK/runtime difference or unknown = Full; other differences dirty affected consumers |
| Documentation and unknown files | Docs/instructions read by tests and repo-wide scanners; explicit finite inert paths with verified no consumer | No blanket docs-only exemption: unknown ownership is dirty-all; root map additions cannot retroactively hide an input |

The initial default root map can over-select. For script suites include the whole scripts/tools trees; for server consumers include all `server/**` and transitive `src/**`; for a reader that scans the repository include every tracked file it can read. A path outside all known roots is not automatically harmless. An “outside input stays equal” test uses a fixture with a proven unrelated project, not a production assertion that unrecognized files are inert. Change reports retain rename old/new paths and deletes.

CARD-1820 edges add positive dependencies to that set. Missing/stale index, capped edges, allowlisted omissions, reflection, generated calls and dynamic loads never narrow it. Save index version and whether it broadened the set for explanation, not authorization. Physical project extraction can later reduce the set through a separately reviewed complete-input contract; it is not on this activation's critical path.

Rejected: test-file-only digests; layer-only hashing for a monolithic assembly; csproj blob without the project's source/content; absence of an index hit as non-impact; a manual “unchanged” checkbox for unknown external fixtures.

### D-5 Full contract, age and histories

`antiphon-rc-input-contract-v1` separates content inputs from execution semantics. In addition to S1's four hashes, the admission envelope binds the complete policy/catalog/selector set, skip resolver, discovery/parser/coverage code, launcher/custody/build code and the scripts/compiled code reachable from them. The initial execution closure includes `release-selection*`, `nightly-run*`, `nightly-tests*`, `nightly-dispatch*`, `nightly-coverage*`, `nightly-policy*`, `nightly-core*`, `nightly-owned-process*`, their transitive helpers and `Antiphon.Nightly/**`/`Antiphon.Scripts/**` actually used. Record actual compiled-versus-fallback implementation. Unresolved imports force Full. Contract/input-map version changes also force Full.

Full triggers: explicit ForceFull; no admissible ancestor baseline/Full anchor; age at admission **at least seven days** since the last eligible Full; unknown/future timestamp; policy/catalog/selector/coverage/harness/SDK/runtime mismatch; unresolved global ownership; missing/corrupt admission chain; detected shadow discrepancy. Full sets the launch set to the complete current universe and records every reason. A successful newly executed Full closure is valid despite its pre-run forced-full reasons and becomes a new anchor; requiring an empty reasons list would prevent bootstrap forever. Carry chains retain the original anchor timestamp and cannot renew it through a new candidate, release ref or sample.

Cumulative failures are immutable facts through all attempts since that anchor. Only a green that contains a fix SHA the failing source did not contain can discharge a prior failure under CARD-1823. Reuse the containment rule, not a caller-supplied `FixProven=true`. A same-tip retry or a new input digest alone cannot erase open failure evidence. Missing history is not an empty history.

**Bootstrap implication:** activation changes the execution scripts themselves, and the investigated recut range already changes nightly coverage/run implementation. Under this exact-hash contract those changes invalidate the old execution contract even if import proves its passes. There is no semantic-equivalence waiver in this plan. A first instrumented Full is then necessary; the following eligible cut is the first that can save executions. The preflight reports which hashes differ and the measured all-dirty cost. This is the safe fallback required by the brief, not an assertion that `0b292fa4` will yield reusable credit.

Rejected: ignoring harness changes to obtain savings; treating a comment-only or “equivalent” executor rewrite as a compatible hash without a separate proved contract; clearing Full reasons in saved evidence; an age clock refreshed by carry.

### D-6 Exact plan, launch and closure

Produce a write-once `antiphon-rc-incremental-plan-v1` after current candidate build/discovery, binding candidate SHA/tree, actual input/output receipts, universe and exclusion digests, baseline admission and history digests, forced reasons, boundary map, case decisions, launch membership, sample seed/list and budget. Its pre-build proposal is explicitly diagnostic. Revalidate before launch and terminal closure; a moved candidate, changed source/output/receipt or malformed plan refuses rather than silently rewriting a running plan.

Let `U` be the current platform's required case obligations, `C` the eligible carry candidates, `D = U − C`, and `S ⊆ C` the sampled cases selected by complete chunks. The launch set is `L = D ∪ S`; only `C − S` is carried in the terminal ledger. Sets use canonical keys; enforce uniqueness, pairwise disjointness and exact equality `U = D ∪ S ∪ (C − S)`. A subset of caller-supplied decisions cannot pass this equality. Include client/lint/script obligations in U with their own result adapters; until an adapter can prove identity/outcomes, execute that entire suite every cut and require its fresh result. No suite may silently vanish because it has no TUnit rows.

Boundary packing happens before dirty/sample chunk choice; any dirty case makes its whole method execute, and a partially dirty packed chunk may conservatively execute the whole chunk. The saved partition reflects **actual** launches, so carried cases accidentally re-executed are no longer counted as savings. Failed-first ordering is applied only to L. The dispatch permutation check remains exact against L; carry membership is separately checked against U. Never weaken the existing Full permutation check to “subset allowed”. All-carried and empty-launch suites still get a verified terminal ledger/status record without a fake process event.

Build once per required suite/output generation, discover once and share setup across chunks. Carry does not skip the candidate build/discovery/prerequisite integrity checks. Watchers and timing sidecars describe actual launched chunks only. The ledger page separately shows planned, executed, carried, sampled, failed, unproven and not-run counts with origin links. Duration copied from a carried result is historical potential work avoided, not new execution duration. A source chunk split into several logical groups contributes its wall duration only once.

Incremental summaries start with `coverageScope=IncrementalDiagnostic`, `coverageComplete=false`. A verified Windows exact closure may record `effectiveMode=Incremental`, `coverageScope=Full` **only alongside** `coverageBasis=ledger-v1` and a validated closure digest. This is qualification coverage, not historical all-executed Full or publication credit. Linux remains `PlatformDiagnostic` with Windows obligations outstanding. `gateCredit=false` and publication/readiness consumers must reject ledger-basis success until their own policy is extended. S7c adds explicit early refusal of automatic publishing for Incremental even if `SkipPublish` was omitted; do not rely on a caller remembering it. Existing Full publisher behavior remains unchanged. Tag, publish and promote remain outside this work's authorization.

### D-7 Baseline selection and release refs

Use an explicit recorded `release/x.y.z` baseline if it is an ancestor and has a matching-platform admission envelope; otherwise choose a deterministic newest eligible ancestor release (committer date, full ref name tie-break), otherwise the newest eligible previous candidate on the same rc line. Bind the selected ref **and resolved SHA**, revalidate lineage and envelope bytes, and refuse a moved/conflicting recorded ref. A missing baseline falls back to Full; a corrupt claimed baseline is surfaced and cannot be silently skipped to a convenient green. Per-identity history still spans later failed attempts even if an older green release supplies a pass.

Release-branch creation is off the first-incremental critical path. S7e implements it after the rollout has a trusted Windows closure: explicit version, exact closure digest, create-only branch, idempotent only at identical SHA, no tag/publish/promote. Its baseline manifest binds closure, contract, universe, platform and Full anchor. Fixtures use a local bare repository, never a live remote. A sampled Shadow closure cannot create the branch; an incomplete native gate cannot be relabeled trusted. The semantic `x.y.z` value versus the repository's CalVer release policy remains an operator decision; the tool invents neither.

### D-8 Shadow validation and residual false-negative risk

The first eligible Incremental run executes D **plus** sampled carried chunks, before any trust transition. Default sample:

`N = min(Cchunk, max(30, ceil(0.05 * Cchunk)))`.

Here Cchunk is the number of completely carryable newly packed chunks. Stratify to include at least one per nonempty `(suite,boundary)` stratum, increasing N if needed. Choose without replacement using a CSPRNG seed generated once and stored in the immutable pre-launch plan; sort candidates before sampling so replay is deterministic. Never repeatedly draw until fast/passing chunks are selected. The seed is not a secret. Expected-skip-only chunks are reported separately and cannot substitute for executed sample coverage. For Cchunk=0 report `sample=not-applicable`, effective Full if all obligations execute, and no successful shadow pilot claim.

Sample failure, unexpected skip, missing outcome, provenance failure or changed roster invalidates the shadow closure and every carry authorized by the suspect contract. Preserve receipts; record a discrepancy, require Debug and a new Full attempt. Do not retry the sample to erase it or expand the same run quietly into a green Full. An ordinary dirty failure also prevents closure and enters cumulative history.

A sample is a detector, not proof that every unsampled test is unaffected. With one missed bad chunk the chance of detection in a uniform sample is only N/Cchunk; with k independent bad chunks the miss probability is `choose(Cchunk-k,N)/choose(Cchunk,N)` (stratification changes the exact calculation). Thirty uniform samples give about 95.8% detection for a 10% bad population, not a 95% safety guarantee. The primary false-negative bound is the conservative manifest and unknown-dirty rule; the **seven-day Full** bounds the time an undetected carry defect can persist. Correlated dependency omissions, environment drift and gaps in historical receipts remain risks.

Promotion default: two successful eligible Windows Shadow cuts, each with N executed samples, no discrepancy/open failure, current native gate receipts, and no overdue Full. Trusted mode additionally requires an explicit operator trust record bound to contract version/digest and platform; time alone is not approval. Retain the same sampling policy in Trusted mode for this first release. Any contract change clears that trust and starts with Full/Shadow again. Operator can request more sampling or Full at any time; reducing sampling is a later policy decision, not an implementation shortcut.

## Slices and dependency order

Each unit below is a separately dispatchable **30–60 minute Code task including its named checkpoint allowance**, with one semantic red commit pushed before its fix, then a green commit pushed before checks. These are provisional estimates, not observed completion times. If a unit cannot fit because a named dependency proves harder, split at its existing API boundary before dispatch rather than widening a session indefinitely. A red witness must fail an outcome assertion against the old behavior/default-deny adapter, not because a function, fixture or compiler is missing.

All new C# test classes below live in `tests/Antiphon.Tests/Scripts/<Class>.cs`; new script harnesses are named explicitly. Each class uses Integration plus the assembly-local `ParallelLimiter<ProcessSpawnLimit>` when it starts PowerShell/Git. Pure tests use Unit. Native methods must refuse/skip off Windows and are never counted green there. No per-test AppHost/database/browser setup is introduced.

| Unit / estimate | Dependencies | Lane / checkpoint |
|---|---|---|
| S2a 40–50 min | S1 | Linux portable / CP-1 |
| S3a 45–60 min | none; D-4 frozen | Linux portable / CP-2 |
| S5a 45–60 min | none; D-3 frozen | Linux portable / CP-3 |
| S8a 40–55 min | none; D-5 frozen | Linux portable / CP-4 |
| S3b 40–55 min | S2a, S3a | Linux portable / CP-5 |
| S5b 45–60 min | S5a, S3b, S8a | Linux portable / CP-6 |
| S7a 35–50 min | S5b | Linux portable / CP-7 |
| S4a 40–55 min | S2a | Linux portable / CP-8 |
| S5c 40–55 min | S3b, S5b, S7a | Linux portable / CP-9 |
| S6a 30–45 min | S5c, S4a | Linux portable / CP-10 |
| S8b 35–50 min | S5c, S4a | Linux portable / CP-11 |
| S6b 45–60 min | S5b, S5c | Linux portable / CP-12 |
| S6c 45–60 min | S6a, S6b, S8b | Linux portable / CP-13 |
| S6d 45–60 min | S6c | Linux portable / CP-14 |
| S7b 45–60 min | S6d, S8a | Linux portable / CP-15 |
| S7c 40–55 min | S7b | Linux portable / CP-16 |
| S7d 45–60 min | all above reviewed/integrated | **Native Windows** / CP-17, CP-18, CP-19 |
| S7e 40–55 min | S7d and trusted closure protocol | Linux portable / CP-20; release operation later |

Every dispatch also runs CP-90 on its own committed source. Placement observation from `GET /api/runner-defaults` and `GET /api/session-runners`: global default resolved to server2; catalogue included one Windows and two Linux runners. This is an observation, not a location pin. Read both routes again at dispatch, omit `-Runner`, omit `-Platform` for portable units; use `-Platform Windows` only for S7d/native evidence work. Do not encode fleet addresses or desktop mount paths in implementation.

Ready parallel wave on server2: S2a, S3a, S5a, S8a, subject to the caller's effective limits. Next, S3b, S4a and finished S5a follow-ups can occupy disjoint scopes. S6a and S8b can run together after their inputs land. Serialize all edits to `nightly-tests-impl.ps1`, `nightly-dispatch.ps1`, `nightly-run-impl.ps1` and release-selection entrypoints. S6b–S7c are an integration chain, not parallel edits to shared scripts. CARD-1820 work can run beside standalone libraries but cannot share its selector files with S7b. Native evidence collection is read-only and can follow S5b while portable integration continues, **only after the historical run is terminal**.

### S2a — boundaries and complete method IDs

Goal: implement `scripts/lib/rc-boundary.ps1`, `rc-boundary-prefixes.json`, and `scripts/test-rc-boundary.ps1` with `RcBoundaryChunkIdTests`. Read `layers.txt` without editing it. Preserve lane-first mapping; conflicting/unknown evidence maps unassigned and stays dirty. Emit deterministic membership hashes and reject ID collisions. Mapping affects packing, never narrows D-4's inputs.

Red witnesses V-1: `StableIdAndSuiteSeparation` (reordering changes ID or two suites alias); `MixedBoundaryAndUnassigned` (one bin admits two boundaries or unknown is carryable); `MembershipAndCollision` (method add/remove or forced short-hash collision is ignored). CP-1 also protects the existing layering guard through CP-90.

### S3a — conservative input inventory

Goal: add `scripts/lib/rc-input-manifest.ps1`, `rc-input-owners.json`, `scripts/test-rc-input-manifest.ps1`, `RcInputManifestTests`. Enumerate complete project/content/import closure at two fixture commits, retaining edge reasons and unresolved flags. No live MSBuild evaluation or selector activation. Include all D-4 input families and unknown-path fallback.

Red witnesses V-2: `ProductAndHelperFanout` (unchanged test with changed product/helper remains clean); `DynamicAndUnownedAreDirty` (unresolved import or unmapped path is ignored); `ConfigFixtureScriptAndLockInputs` (one of the named input families is absent); `PartialIndexNeverSubtracts` (capped/stale/missing index narrows scope). Each method asserts a matrix internally; Min counts methods, not matrix rows.

### S5a — bounded historical extraction

Goal: add `scripts/lib/rc-historical-stream.cs`, `scripts/lib/rc-historical-evidence.ps1`, `scripts/test-rc-historical-evidence.ps1`, `RcHistoricalEvidenceTests`. Implement D-3 extraction and immutable bundle inventory only. Use a one-time Add-Type load; no new project/build pipeline and no whole-document parser. Generate large fixture input as a stream, never commit it.

Red witnesses V-3: `LargeSummaryIsBounded` (885 MiB synthetic summary including one long ignored string exceeds buffer/memory bound); `TamperAndChangingReadRefuse` (same path with changed bytes is accepted); `DuplicateTraversalAndTornInputRefuse` (ambiguous keys/paths, escape or partial JSON becomes evidence). Case assertions inspect normalized rows and hash matches, not source-text parser-name tests.

### S8a — forced-Full policy

Goal: add `scripts/lib/rc-forced-full.ps1`, `scripts/test-rc-forced-full.ps1`, `RcForcedFullTests`; keep S1's caller API intact. Compute reasons from complete contract, age, history and explicit force. An admission-time UTC clock is injected for fixtures. This library is independent of production selection wiring.

Red witnesses V-4: `ContractOrUnknownForcesFull`; `AnchorAgeCannotRenewThroughCarry` (seven-day boundary, future date, missing anchor); `ExecutedFullCanCloseWithReasons` (fresh Full is wrongly prohibited from replacing its expired anchor). Exact reason arrays and launch-all decisions are asserted.

### S3b — reproducible digests and case manifests

Goal: add `scripts/lib/rc-chunk-digest.ps1`, `scripts/test-rc-chunk-digest.ps1`, `RcChunkInputDigestTests`. Convert S3a inventories and S2a membership into canonical digest/case manifests. Bind generated/external inputs only with retained actual receipts. No working-tree bytes are substituted for historical Git inputs.

Red witnesses V-5: `TransitiveEditAndDeletionChangeDigest`; `ProvenUnrelatedProjectKeepsDigest`; `UnknownAndCaseSetChangesAreDirty`; `RenameSymlinkAndCaseCollision` (path-only rename, unresolved link, Windows collision). Include the investigation's unchanged-test/changed-SessionMessageQueueService shape as a fixture.

### S5b — admission and origin-chain validator

Goal: add `scripts/rc-ledger-import.ps1`, `scripts/lib/rc-admission.ps1`, `scripts/test-rc-admission.ps1`, `RcHistoricalAdmissionTests`. Join S5a evidence to S3b inputs and the S8a contract; write D-2 suite shards only through S1. Expose one `Read-RcAdmission` validator used by every later caller. No old run is read from this Linux task.

Red witnesses V-6: `FullProvenanceAdmitsAndReverifies`; `ShaLabelWithoutBuildProofIsUnproven`; `IncompleteChunkAndCaseAliasNeverCarry`; `ChangedEnvelopeHistoryAndOriginChainRefuse`. Attack missing runtime proof, cyclic/missing origin, duplicate cross-suite method, failed original chunk and forged ledger digest. R-1 is the existing strict S1 reader/outcome class plus `RcLedgerDurationTests`.

### S7a — baseline picker

Goal: add `scripts/lib/rc-baseline.ps1`, `scripts/test-rc-baseline.ps1`, `RcReleaseBaselineTests`. Resolve D-7 against fixture Git repositories and admission pointers; return a pinned candidate, reason and Full anchor. Do not create release refs in this unit or change the published-baseline reader.

Red witnesses V-7: `AncestorReleaseThenSameLineFallback`; `CrossPlatformMovedAndCorruptRefRefuse`; `MissingBaselineIsFull`. Include release-vs-rc precedence, unrelated lineage, deterministic ties and failures after the chosen baseline.

### S4a — boundary packing, still opt-in

Goal: edit only packing functions in `scripts/lib/nightly-tests-impl.ps1`; add `scripts/test-rc-boundary-pack.ps1`, `RcBoundaryPackTests`. Partition by suite/boundary before existing time bins; preserve class-prefix safety and complete method membership. The production caller still omits the switch until integration.

Red witnesses V-8: `TwoBoundariesAndPrefixCollisionsStaySeparate`; `RepackPreservesCaseUniverse`; `DefaultFullPackKeepsIds`. R-2: existing `NightlyCoreTests` and `LiveLaneRunnerTests` (CARD-1782 nightly-live-lane row).

### S5c — exact plan and human status

Goal: add `scripts/rc-ledger-plan.ps1`, `scripts/rc-ledger-status.ps1`, `scripts/lib/rc-incremental-plan.ps1`, `scripts/test-rc-ledger-status.ps1`, `RcLedgerStatusTests`. Construct the complete partition and plan digest from current discovery plus admitted baseline; display counts/reasons/unique source wall time. Status reads immutable revisions and starts no children.

Red witnesses V-9: `PartialDecisionsCannotCloseUniverse`; `ParameterizedMethodsAndNonTunitStayRequired`; `StatusCountsOriginsWithoutDoubleCharging`; `CrashRevisionCannotPublishClosure`. Include all-carried and zero-carried cases. Missing suite adapter is fresh-execution required, not absent from U.

### S6a — dirty launch order

Goal: add `scripts/lib/rc-dirty-order.ps1`, `scripts/test-rc-dirty-order.ps1`, `RcDirtyLaunchOrderTests`. Compute L in failed/interrupted/not-run/changed order, with sample membership preserved; consume cumulative failure containment instead of optimistic flags. Full seed ordering remains the existing function.

Red witnesses V-10: `OnlyDirtyAndSampleLaunch`; `OpenFailurePrecedesChanged`; `SameTipPassCannotClearFailure`. R-3: existing `RcFailureHistoryTests` evidence-union and incomplete-evidence behavior.

### S8b — sample selection and trust state

Goal: add `scripts/lib/rc-shadow-sample.ps1`, `scripts/test-rc-shadow-sample.ps1`, `RcIncrementalShadowTests`. Implement D-8's fixed seed, sampling roster and trust predicates. Persist a proposal before launch; no automatic trust transition and no live randomness-dependent test assertions.

Red witnesses V-11: `SampleSizeStrataAndReplay`; `MismatchInvalidatesCarries`; `NoCarryAndOnePilotDoNotGrantTrust`. Verify exact fixed-seed roster, boundaries, changed contract/trust refusal, zero carries and preservation of a failed sample.

### S6b — prospective build/source evidence and result writer

Goal: add `scripts/lib/rc-run-evidence.ps1`, `scripts/test-rc-run-evidence.ps1`, `RcRunEvidenceTests`; wire source/input snapshots, output seals and immutable per-chunk revisions into `nightly-tests-impl.ps1` and owned-checkout boundaries in `nightly-run-impl.ps1`. This also runs during explicit Full bootstrap. Revalidate source/build/output at admission and closure; carry cannot avoid current discovery. Do not change native custody implementation.

Red witnesses V-12: `BuildSourceAndLaunchOutputMustJoin`; `ChangedInputsDuringRunRefuse`; `InterruptedWriterLeavesNotRun`; `OneBuildAndDiscoveryPerSuite`. R-4: `RcFullTimingContractTests`, `NightlyCoreTests`, `LiveLaneRunnerTests`. The build receipt covers all staged helpers/dependencies, not just the entry DLL.

### S6c — scheduler uses the exact launch set

Goal: integrate S6a/S8b through `scripts/lib/nightly-dispatch.ps1` and `nightly-tests-impl.ps1`; add `scripts/test-rc-incremental-dispatch.ps1`, `RcIncrementalDispatchTests`. Introduce a separate verified Incremental schedule; require its permutation to equal L and preserve the old Full schedule guard. Exercise the real prepare/schedule/complete functions with fixture launch delegates.

Red witnesses V-13: `ActualScheduleOmitsOnlyVerifiedCarries`; `MissingDuplicateOrAddedLaunchRefuses`; `AllCarriedSuiteCompletesWithoutFakeLaunch`. R-5: `NightlyCoreTests`, `LiveLaneRunnerTests`; native existing ActualLoop/JournalCustody methods are CP-18, not falsely portable proof.

### S6d — ledger closure and coverage integration

Goal: add `scripts/lib/rc-closure.ps1`, `scripts/test-rc-incremental-closure.ps1`, `RcIncrementalClosureTests`; connect summary and terminal suite/run closure in `nightly-tests-impl.ps1`, `nightly-coverage.ps1` and `nightly-run-impl.ps1`. Validate exact U partition, terminal evidence and prospective outputs; keep Linux diagnostic. Do not modify publication policy.

Red witnesses V-14: `CarryPlusExecutedExactlyCloses`; `MissingCaseOrUnexpectedSkipKeepsOpen`; `SampleFailureAndLinuxCannotClaimWindows`; `FullFallbackResetsAnchorOnlyAfterExecution`. R-6: `RcCoverageAdmissionTests`, `ExpectedPlatformSkipRosterTests`, `ReleaseGatePlatformTests`, `NightlyCoreTests`, `LiveLaneRunnerTests`.

### S7b — wire all selection hops

Goal: edit `scripts/release-cut.ps1`, `scripts/nightly-run.ps1`, `scripts/nightly-tests.ps1`, `scripts/lib/nightly-run-impl.ps1`, `nightly-tests-impl.ps1`, `release-selection.ps1`, and `Antiphon.Scripts/ReleaseSelection.cs`. Add `scripts/test-rc-incremental-hops.ps1`, `RcIncrementalHopTests`. Carry the immutable incremental-plan path/digest and validation mode through wrapper/core/self-reexec/tests. The compiled selector must deliberately return NotHandled for Incremental so the authoritative PowerShell path handles it; avoid a second independent implementation of admission. Full paths retain current parity.

Red witnesses V-15: `PlanBindingSurvivesEveryHop`; `ForgedOrDroppedPlanRefuses`; `CompiledIncrementalFallsBackAndFullStaysFull`. Test request/attempt schema changes and explicit mode mismatches. R-7: `ReleaseSelectionCliTests`, `ReleaseSelectionHopSeedTests`, `NightlyCoreTests`, `LiveLaneRunnerTests`. Update exact forwarding tests, not only ValidateSets.

### S7c — release front-door safety and operator runbook

Goal: add `scripts/test-rc-incremental-entry.ps1`, `RcIncrementalEntryTests`; finish `release-cut.ps1` qualification-only Incremental handling and the preflight manifest/status entry. Update `docs/release-gates.md` and `docs/testing-and-build.md` to distinguish executed Full, ledger coverage, Shadow and publication authority, and document the commands/stop gates below. This is the activation-critical part of old S10; remaining replay/doc work stays later.

Red witnesses V-16: `IncrementalNeverCallsPublisher` (also without SkipPublish); `FallbackIsReportedBeforeLaunch`; `MovedCandidateRefusesBeforeCredit`. Use the actual release entrypoint with local fixture seams and assert no publish/ref-write invocation; include Full behavior regression via `ReleaseSelectionCliTests`.

### S7d — native Windows proof of the integrated path

Goal: add `scripts/test-rc-incremental-native.ps1`, `RcIncrementalNativeTests`. Run a fixture-owned miniature candidate through the real wrapper/self-reexec/build-discovery binding, Job Object launcher, TRX processing and ledger closure on Windows. The staged native test child is built by the checkpoint build, never inside a test; use existing owned-child fixtures. No production root, session runner, broker or live run participates.

Red witnesses V-17: `NativeIncrementalClosure` (enough carry candidates for D-8 to leave unsampled carries, plus dirty and sampled groups; exact actual launch census); `NativeTamperAndCleanupRefuse` (output replacement, interrupted child, no leftover owned process and no closure); `NativeHistoricalBundleReverification` (Windows path/reparse and full-size streamed fixture). CP-17 proves these with zero skips; CP-18 and CP-19 execute the existing native scheduler/custody regressions on the same output. The fixture must obey the production sample minimum; a three-chunk fixture would sample every carry and cannot prove omission. Read-only **real** historical admission and the first real Shadow are subsequent operations with independent evidence, not unit-test passes.

### S7e — release baseline recording, off the first-cut critical path

Goal: add `scripts/cut-release-branch.ps1`, `scripts/test-rc-release-branch.ps1`, `RcLedgerReleaseBranchTests`; extend `rc-baseline.ps1` only for immutable release manifests. Validate an operator-chosen version and trusted Windows closure before create-only local-bare-remote push. Add the release baseline procedure to `docs/release-gates.md`.

Red witnesses V-18: `OpenOrShadowClosureCannotCut`; `ExactClosureCreatesOnlyBranch`; `ExistingDifferentShaAndPublishRefuse`. Verify baseline manifest readback and no tag/publish/promote calls. Actual release ref mutation is a separate authorized operation after trust; portable fixture proof is sufficient for this slice's code.

## Rollout and operator decisions

1. Land/review the standalone libraries and integration slices; keep Incremental off. Preserve the running Windows qualification untouched. S7d must supply fresh Windows receipts at the integrated source before activation. Master publication does not install scripts on the rc line: the caller commissions reviewed rc integration/backport and confirms that the candidate uses those script bytes.
2. After `0b292fa4` is terminal, commission Windows **read-only** import from the supplied retained root into a new evidence root. Report every missing proof and contract difference. The 885 MB summary is streamed. Do not perform “repair” writes to old receipts. Native import proves usable evidence or proves the need to rerun; both are legitimate outcomes.
3. Run preflight on the pinned next candidate: current universe, build/toolchain contract, baseline/full-age/history, changed input roots, dirty/carried/sample counts and estimated launch cost. First-run trust is Shadow. If no safe carries, contract drift or unknown Full anchor makes all work dirty, run one instrumented Full and retain its new admission envelope. Do not claim the sample validated incremental reuse in that run.
4. On the first candidate with eligible carries, execute Incremental Shadow: D plus N sampled carried chunks under D-8. Retain the bound pre-launch plan, fresh execution receipts, carry origins and terminal discrepancy/closure report. Failures get the existing Debug/repair process; the next attempt still sees them.
5. After two successful eligible Windows Shadow cuts and explicit trust approval, allow Trusted under the same conservative input contract and continuing sampling. Seven-day Full and contract drift remain mandatory. Roll back by requesting Full and revoking the trust record; preserve all historical evidence. Rollback starts a new attempt and does not mutate an active run's plan.
6. Create a release baseline only after S7e and a trusted complete Windows ledger. Tag/publish/promote policy is separate. Original S9 historical savings replay remains useful but cannot gate or replace input proof; do it after activation with immutable evidence and correct unique-build/chunk accounting.

Open operator decisions, with safe implementation defaults: **OD-1** whether to spend a native retained-evidence admission pass before an expected Full bootstrap (default: attempt read-only admission and report costs); **OD-2** whether to increase N or require more than two Shadow cuts (default D-8; no reduction implied); **OD-3** explicit trust activation after evidence arrives (default remains Shadow); **OD-4** release/x.y.z version and its relationship to CalVer (no branch without explicit version); **OD-5** scheduling the required native qualification/Full time against other Windows work (no interference with the supplied live run). These decisions do not prevent Code from implementing the closed defaults. None grants an override for missing provenance or false-negative evidence.

## Performance budget

Apply [testing-and-build.md#performance-standards](../../testing-and-build.md#performance-standards) and [R1–R9](../../test-performance-standards.md). This Plan runs no builds/tests. All durations below are **provisional**, not measured p95. Investigation history reports S1 Code around 56 minutes, repair around 55 minutes, and approximately 17 hours for a prior Full run; those are contextual observations, not this design's benchmarks. Do not repeat the original plan's 7–14 hour estimate as a delivery promise after adding admission and closure work.

| Activity | Provisional execution/phase budget | Evidence and bound |
|---|---|---|
| Small contract method | 1–3s process start, 1–3s shared fixture setup, 1–10s body, <=2s cleanup; target <=30s | New method timeout initially 60s, child deadline 45s + 10s joined cleanup; replace from comparable measured p95 before trust |
| Historical streaming method | Generate once per class/run; target <=60s parse/hash for 885 MiB fixture; <=256 MiB incremental working set and <=1 MiB parser buffer | Initial 120s test timeout; memory counters + max buffered bytes + input size, output hashes, phase timings; cap violation is failed/unknown |
| Ordinary CP row | 3–5m isolated build/discovery, 1–4m tests; table allows 6–12m | Retain build, slot wait, discovery, setup, CLI/IO, body, teardown separately; unavailable phase = null, remainder explicit |
| Shared guard CP-90 | 4m provisional including own build; existing nine-class guard history 78 tests, plus spawn census | Exact actual counts required, no claim that historical times are measured p95 |
| Native CP-17/18/19 | 12m + 10m + 3m provisional, shared build; startup <=10s, each child/cleanup operation individually bounded | Existing native method deadlines remain unchanged; collect comparable Windows sample before changing them |
| Real admission/preflight | O(total evidence bytes + current roster + unique input records), one sequential summary scan/copy; target <=10m excluding candidate build | Initial 20m command deadline, 60s per-file stalled-IO deadline; detailed per-file bytes/seconds and peak memory. Unknown timing forces measurement before trust |
| Real Incremental | Candidate build/discovery once + sum of actual dirty/sample chunk critical path + once-per-run shared setup | No promised percentage saving; report wall separately from summed nested spans and historical carry wall |
| Periodic/bootstrap Full | Prior contextual estimate ~17h; existing pack targets 900s and lone-class limits 2400s (E2E 1500s) retained | Caller MUST commission Debug with exact run/selection/SHA/load/timing under R1; a Full safety requirement does not exempt cost analysis |

Collect timing on the first ordinary green run, retain every red/retry cost, and accumulate at least five comparable observations per new method across ordinary Code/Review/pilot work before declaring a measured p95; smaller samples stay provisional. Use approximately 2x observed p95 for test/operation/row execution deadlines, with CARD-1836 recording any exception. Do not run five redundant suites solely to fill the sample. Unknown/small-sample provisional budgets must be revisited before Trusted. No timeout widening or extra retry makes a red witness green.

Each script harness uses one immutable fixture source repository and large evidence corpus per class/run; independent cases use cheap clones or distinct owned outputs. No per-test container, migration, web host, browser or external API. The historical streaming file is generated once into ignored temp evidence, not committed or copied into every case. In production, share suite builds/discovery and existing database/host setup; per-case isolation remains through existing snapshots/clones. No new AppHost startup spans are required because product startup is unchanged. New phase recording uses existing allowed operation labels; a new label would require the timing-labels impacted test and plan amendment.

Record `.antiphon/checkpoints/<run>/` reports/TRX/source receipts and phase journals for Code; release-root admission/plan/envelope/stream metrics for operational work. Retain actual source, platform, host ID, case/group membership, toolchain and input-byte counts. IO, Git/CLI and process-drain calls each have cancellation/deadlines. Admission queue wait is separate from execution: use the build-slot gate, finite 45m admission allowance, exit 4 as not-run, never unleased retry. Do not use one slot around a loop of builds.

Importer row/total floors are tool behavior, not measured p95: leave its derived total allowance (`max(30, 2*selected estimates+10)`) intact, record the provisional exception under CARD-1836, and enforce the method/command deadlines above. Any expected/observed selection, row, batch or run over 30 minutes gets a Debug task and a timing-based split/repair before a repeat. Native checkpoints are kept separate to stay below that threshold. The total multi-unit program exceeds 30m; it is dispatched as the bounded tasks above, not one giant run.

## Verification design

### Coverage and existing tests affected

V-1…V-18 are the ordinary red/green witness groups specified with their slices; R-1…R-7 name existing behavior. Tests must call the production reader/planner/scheduler/entrypoint and inspect its result/launch census. Fixtures do not implement a second “correct” carry algorithm and compare it to itself. Source-text pins alone do not establish admission or end-to-end closure. No executed mutation cycles are required under the explicit waiver.

| Changed surface | Existing coverage to retain or update | Checkpoint |
|---|---|---|
| S1 adapter and duration compatibility | `RcIncrementalLedgerTests`, `RcLedgerDurationTests` | CP-6 |
| Boundary packing, nightly helper/integration functions | `NightlyCoreTests`, `LiveLaneRunnerTests` | CP-8, CP-12–15 |
| Failure-history ordering/containment | `RcFailureHistoryTests` | CP-10 |
| Source/build/timing and one-time setup | `RcFullTimingContractTests` | CP-12 |
| Coverage/platform/expected skips | `RcCoverageAdmissionTests`, `ExpectedPlatformSkipRosterTests`, `ReleaseGatePlatformTests` | CP-14 |
| Selection worker/compiled fallback and wrapper forwarding | `ReleaseSelectionCliTests`, `ReleaseSelectionHopSeedTests` | CP-15, CP-16 |
| Real native scheduler, custody and interleaving | `RcFailedFirstExecutionTests.ActualLoopFollowsSchedule`, `.JournalCustodySurvivesInterleaving`, `NightlyNativeOwnershipTests` | CP-18, CP-19, Windows only |
| Every added class/limiter and policy contract | Nine standing guard classes plus `ProcessSpawnLimitTests` | CP-90 per dispatch |
| Migrations, HTTP DTO/routes, operation labels, remote `.sh` functions, statement budgets | **None in declared scope.** Do not silently add one | No matching CARD-1782 row for these areas |

Apply [CARD-1782 impacted classes](../../testing-and-build.md#impacted-classes-card-1782) and `tests/impacted-classes.json`: planned changes select `nightly-live-lane`, `test-lane-category`, and `spawn-limit-rosters` when limiter declarations are added; these are covered above. No csproj change is planned. If implementation adds a reference/staging item, the `land-graph-audit-project` row and `LandGraphAuditProjectInstanceTests` become required and the slice must amend its closed table before running. Resolve the actual committed range with `scripts/impacted-classes.ps1 -Range <base>..<tip> -Json`; extra matches require an explicit table amendment, not silent omission. Code/Review also run `scripts/check-evidence-diff.ps1` over their range and report conflict-free `git merge-tree --write-tree origin/master HEAD` after fetch.

Checkpoint scope is each unit's rows **plus CP-90**; this is the dispatch's ordinary verification profile, not a full Antiphon.Tests run. Run once per committed slice group with the checkpoint tool's `run --plan <this-file> --after <unit> --parallel 1 --expected-source-sha <full-sha>`; `After=all` selects CP-90 for every unit. Keep following `wait` until it exits other than 75. Bootstrap the tool only if needed under `scripts/build-slot.ps1`, report that bootstrap separately; direct bootstrap builds use native default output, no `-o`/OutputPath. The checkpoint driver owns the isolated `bin-.../` outputs shown below. All child build/test drivers remain gated. No source edits while a run is active.

Before running, inspect intended class/method prefix matches for suffix collisions. At plan source, existing OR operands name unique classes; `RcFailedFirstExecutionTests` exact method operands have no suffix collisions. New class names are reserved by this plan and must be rechecked against the integration tip. Every OR operand below has its own parentheses and trailing `*`; pipes are escaped for table import. Verify the actual fresh TRX roster, including every expected method; zero tests or an unlisted suffix match fails the row. Min is a floor on TUnit executed results only, not internal fixture assertions. Existing classes may expand above it; report exact counts.

### Checkpoints

Lane is encoded in Group and defined by the unit table: CP-1–16, CP-20 portable/Linux; CP-17–19 native Windows; CP-90 runs on the dispatch's lane. Do not execute CP-17–19 on Linux. All `Expect` entries require each named new method and each selected existing class/method, zero failures and zero unexpected skips. New-method floors exclude extra existing-class counts. The native launch rows share one build because their After groups match.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2a | `tests/Antiphon.Tests -> bin-c1836-s2a/` | linux-boundary | `/*/*/RcBoundaryChunkIdTests/*` | V-1 | all 3 named methods, 0 failed/skipped | 3 | 6 |
| CP-2 | S3a | `tests/Antiphon.Tests -> bin-c1836-s3a/` | linux-inputs | `/*/*/RcInputManifestTests/*` | V-2 | all 4 named methods, 0 failed/skipped | 4 | 8 |
| CP-3 | S5a | `tests/Antiphon.Tests -> bin-c1836-s5a/` | linux-stream | `/*/*/RcHistoricalEvidenceTests/*` | V-3 | all 3 named methods, 0 failed/skipped | 3 | 8 |
| CP-4 | S8a | `tests/Antiphon.Tests -> bin-c1836-s8a/` | linux-forced-full | `/*/*/RcForcedFullTests/*` | V-4 | all 3 named methods, 0 failed/skipped | 3 | 6 |
| CP-5 | S3b | `tests/Antiphon.Tests -> bin-c1836-s3b/` | linux-digest | `/*/*/RcChunkInputDigestTests/*` | V-5 | all 4 named methods, 0 failed/skipped | 4 | 8 |
| CP-6 | S5b | `tests/Antiphon.Tests -> bin-c1836-s5b/` | linux-admission | `/*/*/(RcHistoricalAdmissionTests*)\|(RcIncrementalLedgerTests*)\|(RcLedgerDurationTests*)/*` | V-6 R-1 | all listed, 0 failed/skipped | 4 | 10 |
| CP-7 | S7a | `tests/Antiphon.Tests -> bin-c1836-s7a/` | linux-baseline | `/*/*/RcReleaseBaselineTests/*` | V-7 | all 3 named methods, 0 failed/skipped | 3 | 6 |
| CP-8 | S4a | `tests/Antiphon.Tests -> bin-c1836-s4a/` | linux-pack | `/*/*/(RcBoundaryPackTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)/*` | V-8 R-2 | all listed, 0 failed/skipped | 3 | 8 |
| CP-9 | S5c | `tests/Antiphon.Tests -> bin-c1836-s5c/` | linux-plan-status | `/*/*/RcLedgerStatusTests/*` | V-9 | all 4 named methods, 0 failed/skipped | 4 | 8 |
| CP-10 | S6a | `tests/Antiphon.Tests -> bin-c1836-s6a/` | linux-dirty-order | `/*/*/(RcDirtyLaunchOrderTests*)\|(RcFailureHistoryTests*)/*` | V-10 R-3 | all listed, 0 failed/skipped | 3 | 8 |
| CP-11 | S8b | `tests/Antiphon.Tests -> bin-c1836-s8b/` | linux-sample | `/*/*/RcIncrementalShadowTests/*` | V-11 | all 3 named methods, 0 failed/skipped | 3 | 6 |
| CP-12 | S6b | `tests/Antiphon.Tests -> bin-c1836-s6b/` | linux-run-evidence | `/*/*/(RcRunEvidenceTests*)\|(RcFullTimingContractTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)/*` | V-12 R-4 | all listed, 0 failed/skipped | 4 | 12 |
| CP-13 | S6c | `tests/Antiphon.Tests -> bin-c1836-s6c/` | linux-dispatch | `/*/*/(RcIncrementalDispatchTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)/*` | V-13 R-5 | all listed, 0 failed/skipped | 3 | 10 |
| CP-14 | S6d | `tests/Antiphon.Tests -> bin-c1836-s6d/` | linux-closure | `/*/*/(RcIncrementalClosureTests*)\|(RcCoverageAdmissionTests*)\|(ExpectedPlatformSkipRosterTests*)\|(ReleaseGatePlatformTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)/*` | V-14 R-6 | all listed, 0 failed/skipped | 4 | 12 |
| CP-15 | S7b | `tests/Antiphon.Tests -> bin-c1836-s7b/` | linux-hops | `/*/*/(RcIncrementalHopTests*)\|(ReleaseSelectionCliTests*)\|(ReleaseSelectionHopSeedTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)/*` | V-15 R-7 | all listed, 0 failed/skipped | 3 | 12 |
| CP-16 | S7c | `tests/Antiphon.Tests -> bin-c1836-s7c/` | linux-entry | `/*/*/(RcIncrementalEntryTests*)\|(ReleaseSelectionCliTests*)/*` | V-16 R-7 | all listed, 0 failed/skipped | 3 | 8 |
| CP-17 | S7d | `tests/Antiphon.Tests -> bin-c1836-s7d/` | windows-native | `/*/*/RcIncrementalNativeTests/*` | V-17 | all 3 named methods, 0 failed/skipped, Windows | 3 | 12 |
| CP-18 | S7d | `CP-17` | windows-scheduler | `/*/*/RcFailedFirstExecutionTests/(ActualLoopFollowsSchedule*)\|(JournalCustodySurvivesInterleaving*)` | R-5 native | both named methods, 0 failed/skipped, Windows | 2 | 10 |
| CP-19 | S7d | `CP-17` | windows-custody | `/*/*/NightlyNativeOwnershipTests/*` | R-5 native custody | all 9 methods, 0 failed/skipped, Windows | 9 | 3 |
| CP-20 | S7e | `tests/Antiphon.Tests -> bin-c1836-s7e/` | linux-release-baseline | `/*/*/RcLedgerReleaseBranchTests/*` | V-18 | all 3 named methods, 0 failed/skipped | 3 | 8 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-c1836-guards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, spawn census | all listed, 0 failed/skipped | 79 | 4 |

## Cost and completion

The table's once-each ordinary execution floor is 173 minutes (not the Min counts); separate task dispatches run the 4-minute CP-90 each time, so add 68 minutes for its other 17 executions. Authoring is the remainder of the 30–60 minute per-task estimates. Native S7d has a 29-minute ordinary floor including guards. Build-slot admission, Debug repairs, ordinary Review and operational Full/Shadow runs are additional, separately reported costs. A selected batch over 30 minutes must be split or get the required Debug measurement task; do not run this entire table as a single dispatch.

Code completion means the selected slice's production path, semantic red/green witnesses and closed checkpoints are committed/pushed with exact source receipts. Activation completion additionally requires native S7d, terminal historical admission/preflight, an eligible real Shadow with recorded N and zero discrepancy, then the explicit trust decision described above. A plan, a portable fixture, a green import report or a no-carry Full cannot stand in for those operational facts.

--- next stage ---
next: code
handoff: Implement S2a, S3a, S5a and S8a in disjoint portable scopes, each with its closed CP plus CP-90; follow the dependency chain to native S7d. Historical carry is conditional on retained provenance and equal contracts; otherwise bootstrap Full. Incremental stays Shadow and cannot publish.
artifact: docs/superpowers/plans/2026-11-12-card-1836-incremental-activation-plan.md
