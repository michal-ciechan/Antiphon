# CARD-1023: evidence-backed compatibility, starting with catalogue and observations

Date: 2026-10-03. Plan task: `63057c79-9e3e-4301-b191-506091c4ed9c`.
Source baseline: `2d3c582416c5610d72e53df3e790bc114d87ad82`.
Card: Antiphon / CARD-1023, revision 0, read through `card.ps1 get`.

## Outcome and first-release boundary

Recommend a small **observation-only first release**: a reviewed repository catalogue,
a pure matcher, typed observations projected from CARD-0959's existing feed, and removal
of duplicate floor metadata from the model ladder. It makes the compatibility policy
reviewable without pretending that a runner's default installation identifies the CLI
inside every task or warm session. S1-S3 below are the entire first Code scope.

The target admission rule is fixed by the operator: allow by default; refuse only an
actual combination covered by an evidence-backed known-broken row. The first release
does **not** activate that refusal. Launch correlation, durable per-task recording and
the eventual admission switch are separately commissioned follow-ups, in that order.
Shipping S1-S3 is not completion of the card's full admission outcome.

This plan is written under the recommended defaults D-1 through D-10. Return
`next: decide` so the caller can accept the observation-first boundary and seed scope;
then commission **TestDesign for S1-S3 only**. TestDesign was not folded into this
dispatch. The proposed checkpoints are not an executable verification freeze, and
their presence does not authorize skipping TestDesign.

Owners consulted: `docs/project-context.md`, `docs/ops-http.md`,
`docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`,
`docs/testing-and-build.md`, `docs/agent-kinds.md`, and the runtime/shadow-copy
contracts in `docs/session-runtime-invariants.md` and ADR 0002. No source, card,
settings, routing pins, credentials, services or provider installations change here.

## Ground truth

These are source/evidence observations, not assertions about a future deployment.

| Card assumption | What the code or retained evidence does | Consequence |
|---|---|---|
| Runner capabilities already identify the environment. | `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:963` has backend/request/reason/fallback, `Build`, `Version`, store ID and `Platform`. `Version` is the runner SHA; `Build` describes the runner process. | Reuse these dimensions; never call either the CLI or PtyHost build. |
| There is one capabilities producer. | `SessionRunnerRuntime.DescribeCapabilities` and `PhoneHomeRuntimeAdapter.Capabilities` independently populate the DTO. | CARD-0959 owns both producers. Consume its final additive contract, not a new competing probe. |
| CARD-0959's floor check is the dependency. | Baseline has no CLI fields or `CodexLadderEntry`. The fetched inert Code branch has probes, refresh, nullable evidence, `CodexCliObservation`, and `CodexLadderEntry(ModelId, MinimumCliVersion)`. Its active re-freeze removes admission. | Wait for reviewed inert land; do not copy the old gating implementation from historical plan sections. |
| A fingerprint proves the task uses the probed launcher. | CARD-0959's re-freeze explicitly defers the correlation contract. `CodexCliProbeDescriptor.FromSpec` is pure diagnostic projection; admission `ResolveAsync` and admission call sites are removed. | Preserve fingerprints as observations. Comparing two arbitrary opaque values is not launch authorization. |
| Every default CLI sample is a per-model observation. | `RunnerCodexCliVersionDto` contains version, attempt time, error and launcher fingerprint only. A successful version probe makes no model request. | Its model and model-work outcome remain unknown. No automatic known-works promotion. |
| Requested tier tells us the actual model. | `AgentTaskDispatcher.BuildLaunchSpecAsync` gives exact profile ModelId precedence and saves `session.EffectiveModelId`; a profile may own the model argument. Warm and standing sessions already exist before task delivery. | Target matching uses final effective model and session incarnation, not High/Medium or a display alias. |
| Runner build identifies PtyHost. | `PtyHostLauncher.CurrentShadowDir` caches a shadow copy; `ShadowCopyStore` addresses copied dependency bytes with a short content hash. Existing hosts can outlive a runner restart. No PtyHost build is in the runner capabilities DTO. | Keep PtyHost identity nullable; do not copy runner SHA or treat the shadow directory's eight-character suffix as a full artifact digest. |
| Backend name proves actual host backend. | The current capabilities producers resolve runner policy. That is not a receipt from each live shadow-copied host. CARD-1022 plans `UnixPty` and coherent per-instance decisions. | Distinguish advertised backend from observed session backend. Do not label a Linux session ConPTY because of the old enum/default. |
| 6.1 Sol's 0.159.1 floor is measured runtime rejection across all older versions. | CARD-0903 investigation section 2.3 measured catalogue absence at 0.156.1, 0.157.1, 0.158.0, 0.159.0 and presence from 0.159.1. Section 5 explicitly says backend acceptance was not measured. CARD-1023 separately records the operator's fact that 6.1 Sol needs the newer CLI. | Seed provenance must distinguish the operator assertion from catalogue measurements. Do not describe an unrun backend test as evidence. D-3 defines the proposed scope. |
| Astra's documented 0.153.4 is another refusal threshold. | `docs/investigations/2026-09-05-card-0396-codex-astra.md` records a real 0.152.0 version-specific HTTP 400. Its retained changelog distinguishes explicit model support in 0.153.1 from picker/default visibility in 0.153.4. | 0.153.4 is guidance, not a deny range. The old 0.152.0 failure is narrower historical evidence; it does not prove 0.153.1-0.153.3 fail. |
| All three cited Grok canaries prove one tuple. | CARD-1011's ledger retains d6e4138e as reported CLI 1.0.41 with backend unidentified. WQ-2 records 1.0.46/ModernConPty. Amended WQ-3 used an isolated harness at 189042da, not the production runner build. | Keep three observations and their provenance. Do not rewrite an earlier session using the later installed version or substitute checkout SHA for loaded runner SHA. |
| Compatibility observations already have a durable table. | Runner observations are generation-bound memory/projections. `AgentTaskEvent` has a 4,000-character Detail and no typed compatibility payload; the event DTO also has no such field. | Use existing capabilities for the first feed and curated historical observation rows for durable evidence. A per-task durable store belongs to the correlation follow-up. |
| `antiphon.areas.json` can be copied literally as runtime policy. | `AreaMapLoader` reads each task repository and tolerates missing/malformed files. CLI compatibility is a property of the deployed product/runtime, not each task repository. | Copy the versioned, reviewable authoring pattern; load a catalogue embedded in the server build, never one selected by task cwd. |
| Deprecated inbox still needs matrix qualification. | Operator dropped CARD-1011 WQ-1; CARD-1022 plans staged retirement and preserves old wire strings. | No inbox execution/checkpoint/cell campaign. Retain historical strings as data without creating new inbox-specific rules or qualification rows. |

Read-only platform inspection at approximately 23:38 UTC: `/api/runner-defaults`
returned revision 2, a global preference and no per-kind overrides;
`/api/session-runners` showed an eligible Windows lane, an eligible Linux lane and
an unavailable/draining temporary entry. This is a dated inventory, not placement
configuration. Re-read both routes at dispatch. Omit `-Runner` unless deliberately
pinning a single host; omit `-Platform` for portable work. `-Platform Any` clears an
inherited OS pin. Native correlation follow-ups name Windows explicitly, never a
fixed fleet location.

## Decisions

### D-1 — First release is catalogue plus observation reporting

**Recommendation/default:** implement S1-S3 only. No new task-create, retry,
dispatch, reuse, final-launch, queue or runner admission policy is installed.
The pure matcher is exercised with explicit combinations and is available to the
later admission service; current runner samples remain diagnostic.

Reason: CARD-0959 deliberately removed launcher-permission claims, and Grok's
auto-update is concrete evidence that an installation sample can differ from a
running session. Reject reviving the old policy under a new class name, adding
an admission-time probe, or building all provider probes/host handshakes/UI/DB
history in this first release.

### D-2 — One versioned source catalogue, loaded with the server

**Recommendation/default:** add root `antiphon.compatibility.json`, schema v1,
with `knownBroken` and `observations` arrays. `knownBroken` is curated policy;
`observations` contains deliberately authored, bounded historical evidence
summaries, including works and unknown combinations. They are not copied JSON
receipts, runtime-generated output or an automatically growing log.

Embed the file through `server/Antiphon.Server.csproj`; load once through a
DI-owned `CompatibilityCatalogueLoader` in Infrastructure and inject its immutable
parsed value. Include its SHA-256 in evaluation/observation diagnostics. No static
mutable cache and no new interface unless an actual external I/O seam needs one.
The pure evaluator and DTOs live in Application. Updating policy means a reviewed
commit, build, land and server activation, not editing a file beside a daemon.

Missing/unreadable/unsupported/malformed catalogue means an empty effective rule
set plus a bounded diagnostic, never server-start or task refusal. Malformed rows
are ineligible; never convert omitted selectors to wildcards. Reject the whole
catalogue on duplicate IDs or ambiguous schema. Tests must fail an invalid shipped
catalogue, while runtime keeps the operator's allow-by-default contract. A load
failure does not resurrect a previous denying catalogue. Reject per-worktree
policy loading and a database editor for curated deny rules.

### D-3 — Seed Sol narrowly by model; Astra's 0.153.4 remains guidance

**Recommendation/default for caller acceptance:** first rule ID
`codex-gpt61sol-before-01591`, kind `Codex`, exact model `gpt-6.1-sol`, stable
CLI releases `<0.159.1`, with an explicit upstream-client-version scope independent
of runner/PtyHost/backend/host. Include both the 2026-10-03 operator assertion on
CARD-1023 and CARD-0903 section 2.3 as evidence, labelled respectively
`operator-assertion` and `catalogue-measurement`. This is the operator's stated
6.1 requirement represented as data, **not** a claim that the investigation ran
every older version. First-release matching is diagnostic only (D-1).

This scope is a stated default to accept before enforcement. If the caller does
not accept that the operator assertion establishes the range/infra independence,
keep this rule `enabled: false` with the same evidence and measure an actual
version-specific model rejection before enabling it. Do not silently substitute
catalogue absence for that missing proof. A range must never be auto-generated
merely because a model first appears in an installed catalogue.

No Astra `<0.153.4` rule. Keep the 0.153.4 statement as historical upgrade/picker
guidance and the 0.152.0 failure as a historical known-broken observation. Do not
promote that old account/backend-specific probe into an all-host live refusal in
this release. It can support a separately reviewed exact-version rule once its
applicability is established. Reject copying every documentation minimum into a
deny range, or refusing all Codex kinds/models below the Sol boundary.

Remove `MinimumCliVersion`, `MinimumCodexCliVersion` and their literal from
CARD-0959's `ModelLevelAliases` after its inert slice lands. Keep model-selection
strings and public string-returning methods byte-for-byte equivalent; simplify
`CodexLadderEntry` to model data or remove the now-redundant wrapper. Keep the
shared SemVer parser. The catalogue is the sole source of compatibility rules;
the ladder is not an authorization table. Move the assertions in
`CodexCliObservationTests.C959_Ladder_and_exact_models_share_floor` to
`CompatibilityCatalogueTests.C1023_Ladder_aliases_and_catalogue_seed_agree`,
replacing the removed reflection-based floor lookup with catalogue assertions.
Remove only that obsolete method; retain its alias cases and every unrelated
CARD-0959 observation/non-refusal test. TestDesign records the successor for any
pending floor-metadata PC, without claiming that the old control was executed.

### D-4 — Positive matching, explicit independence, no unknown-to-broken inference

**Recommendation/default:** every rule contains ID, enabled status, kind, canonical
model, CLI selector, all environment selectors, reason, evidence, author/review
provenance, and issue/proof scope. Kind and model must be exact. CLI selectors are
an exact version/build, a finite set, or an explicit stable-release interval with
evidence for the interval. Use CARD-0959's numerical SemVer implementation; no
string ordering or package-registry network calls. Prereleases do not fall into
stable intervals. Build metadata is preserved; a build-specific rule compares it
explicitly instead of relying on SemVer precedence, which ignores it.

Runner build, PtyHost build, backend, backend package and host/OS selectors each
say either `exact` or `any` with a nonempty scope reason. Null/omitted is invalid
in a rule. In an observation, null means unknown, never wildcard. An `exact`
selector cannot match unknown; `any` intentionally does not constrain that
dimension and cannot be used without evidence of independence. Rules need not
enumerate an unbounded Cartesian product, but no implicit wildcard is allowed.
Do not match infrastructure by human display name or mutable runner preference.

An evaluation returns `KnownBroken` only for enabled, valid rules whose constrained
values are all positively matched against **actual bound evidence**. Return
`Unknown` for missing/failed/stale-only probes, old wire DTOs, opaque wrappers,
unresolved model, mismatched launcher/generation or unsupported backend facts.
`KnownWorks` needs matching evidence for its stated proof scope; a version probe,
task creation, green source test, Ready screen or process exit alone is insufficient.
Both Unknown and KnownWorks allow. Disabled rules are evidence only.

Preserve multiple matching rule IDs in deterministic order; do not invent a
last-row-wins allow override. A curated, directly contradictory works observation
for the same actual cell and failure/proof scope makes the result `Unknown`
(`conflicting_evidence`) until the rule is narrowed/suspended in a reviewed change.
Unrelated works (another version, model, host or proof scope) cannot erase a broken
cell. Runtime success never automatically adds, broadens or removes curated rules.

### D-5 — Separate current observations, historical evidence and actual combinations

**Recommendation/default:** retain current probe snapshots in the runner capability
feed; add a nullable `CompatibilityObservations` projection to the existing server
runner-list DTO. In release one it contains the current Codex default-installation
observation only. No new endpoint, polling job, DB migration or task-side probe.
Project from the `RunnerDescriptor`/capabilities already read by
`SessionRunnerCatalogue`, so a catalogue GET creates no additional process/RPC.
Preserve CARD-0959's existing fields and status API unchanged.

Use the combination/observation schema below in both curated examples and typed
API projection. Historical works remain in the curated file with evidence refs;
do not join them to a live runner row and claim that today's runner works. Current
capabilities are a latest-observation feed, **not durable per-task history**.
That limitation is deliberate and must remain visible in the first-release docs.

The correlation follow-up should add a dedicated append-only
`CompatibilityObservations` table, not put machine JSON into event Detail. It
records an admission/launch/session observation with nullable TaskId/SessionId,
attempt/incarnation identity, catalogue digest, immutable typed payload and proof
refs; indexes support task/attempt and runner/time reads. Use EF directly and a
CLI-generated migration. Define idempotency by source event/receipt plus phase,
not by tuple alone: an auto-update creates a new observation. That follow-up must
prove recording for every admitted/refused attempt without making compatibility
telemetry failure a new refusal; a failed write is explicitly reported, not claimed
recorded. No unbounded history store or ingestion endpoint is built in S1-S3.

### D-6 — Grok observations preserve auto-update and distinct proofs

**Recommendation/default:** seed the three historical observations below, keeping
requested and transcript-reported models separately. `grok-4.7-build` is evidence,
not a new ladder alias. Grok CLI SemVer and its build identifier are separate
fields. A later `grok --version` does not rewrite an earlier session's version.
No Grok floor or deny rule is proposed. Claude version/model evidence is also
nullable; the schema supports it without commissioning another provider probe.

Future Grok launch observations need version immediately before launch plus the
session banner and launcher identity; disagreement records drift and makes the
actual version unknown for admission. Serialize/store what was seen, not the
version a plan expected. Reject pinning versions in qualification prose as if that
prevented self-update, or rerunning paid canaries for this documentation/data work.

### D-7 — Backend and infrastructure remain independent dimensions

**Recommendation/default:** distinguish session backend (`pty-host`, Herdr, etc.)
from PTY backend (`ModernConPty`, future `UnixPty`, or an old wire string), from
ConPTY package version and from PtyHost executable build. Retain optional OS name,
version, architecture and an opaque host/infra identity. In release one only
Platform, runner identity/store and runner build have normal producers; other
values stay null unless a cited historical receipt supplies them.

Consume CARD-1022's names when landed. Do not change selection, fallback, ceilings,
spilling or host readiness here. Accept historical/old inbox strings but author no
new inbox-specific rule, observed qualification row, native checkpoint or PC. A
missing modern binary remains CARD-1022's concrete launch-readiness failure, not a
global matrix floor. Reject equating backend package version with PtyHost SHA.

### D-8 — Curators add rules; observations cannot write policy

**Recommendation/default:** an operator or a card-commissioned Code/Docs author
may propose a row by normal reviewed commit. They must supply a dated task/card
or retained report at an immutable commit, actual dimensions and unknowns,
requested/effective model, exact failure and proof scope, reproduction/receipt
identity, and the justification for every interval or `any`. A provider/model
version rejection is different from auth failure, quota, a failed test fixture,
readiness timeout or an unmeasured combination. Those are not interchangeable
known-broken evidence.

Keep bounded essential facts and immutable references in the catalogue and a
tracked Markdown evidence note. Raw receipts/TRX/logs stay ignored; a disposable
worktree path is not the sole evidence reference. CARD-1017's eventual cleanup
must not erase the only explanation of a live rule. No retention-copy service,
report-store write or cleanup-policy change is authorized here.

Review uses the operator's **regression-only verdict**: demonstrate a changed
behavior/false refusal or other actual regression; report inherited red and
unverified qualification separately. A typo or overbroad matcher that creates a
new refusal is a real regression. New contrary evidence leads to a normal diff
that disables/narrows/retires a rule with a reason; preserve its evidence ID.
Reject automatic deny learning, an API policy editor and an expiring override
system. A correction to bad evidence belongs in the catalogue.

### D-9 — Replace CARD-0959's removed gates explicitly

**Recommendation/default:** D-5's fifteen-minute boundary stays a display-only
fact; no age can itself cause refusal. D-6's eight-second admission probe and
fail-closed policy remain deleted; no synchronous refresh-to-admit. D-7's override
classes/settings/audits remain deleted. An old, unknown, failed or stale sample
does not lower the model, reroute the task, hold the queue or trigger a retry.
Existing model holds, auth, routing pins, platform, drain and capacity policies
retain their authority; allow-by-default here does not bypass them.

The later admission API returns an existing-style ConflictException with code
`known_broken_compatibility`, rule IDs, catalogue digest, actual combination and
evidence/reason only when D-4 matches. Queued work receives the same typed reason
through its existing task outcome path. No arbitrary version-required codes,
operator override flag or new attention/notification channel is introduced.

### D-10 — Activate in evidence order

**Recommendation/default:** reviewed CARD-0959 inert runner reporting first,
then its server projections, then this release's reviewed catalogue/reporting.
No CLI upgrade prerequisite and no admission activation in this release. Verify
the loaded server SHA/catalogue digest and observed fields after the caller's
canonical restart. Later: launch/session correlation and durable observation
capture -> evidence review -> known-broken admission switch. CARD-1022 retains
its separate runner-first rollout and CARD-1011 retains its routing activation.

Reject server-first enforcement based on missing new fields and coupling the
catalogue release to an inbox migration, host budget change or paid canary.

## Combination and evidence model

The schema distinguishes a value from the authority to use it in admission.
Do not serialize credentials, environment, CLI arguments, user home paths or raw
probe stderr. Public diagnostics use fixed reason tokens and opaque fingerprints.

| Field group | Fields and producer | First-release meaning |
|---|---|---|
| Identity | schemaVersion, observationId/source ref, observedAtUtc, source kind, proof scope, outcome | Outcome is Unknown for a default version probe, even if the process succeeded. Historical works carry an explicit one-turn/delivery proof. |
| CLI/model | kind; cliVersion; cliBuild; requestedModel; effectiveModel; reportedModel; launcherFingerprint | Codex version/time/fingerprint from CARD-0959. Default probe has no actual model. Grok/Claude versions only in authored historical rows until separate producers exist. |
| Runner | runnerId, runnerStoreId, processBootId/accepted generation when exposed, runnerBuild informational version and commit SHA | Copy observed fields only. Unknown boot/generation stays null. Never substitute checkout HEAD. |
| Host | platform, osVersion, architecture, host/infra identity | Current Platform is windows/linux; no inferred OS build, kernel, image digest or architecture. No deployment default is embedded in code. |
| Session/runtime | taskId, sessionId, session incarnation, sessionBackend, ptyHostBuild, ptyHostArtifactDigest, ptyBackend, backendPackageVersion | Mostly null in current capabilities. Actual host receipt must ultimately own these, including reused/re-adopted hosts. |
| Binding/quality | binding = RunnerDefault / Launch / Session / Historical; checkedAtUtc; probeError; displayStale; completeness; catalogue digest | RunnerDefault cannot authorize a model refusal. Time read/heartbeat is not probe completion. Historical partial facts are not padded to a complete tuple. |
| Evidence | immutable repo path+commit or canonical task report ID; measured time; summary; exact failure/success; authority type; applicability reason | A hash prefix remains a hash prefix in an evidence note; it is not accepted as an exact artifact selector. |

Example first broken row shape (authoring contract; names frozen by TestDesign):

```json
{
  "id": "codex-gpt61sol-before-01591",
  "enabled": true,
  "kind": "Codex",
  "model": "gpt-6.1-sol",
  "cliVersion": { "stableLessThan": "0.159.1" },
  "runnerBuild": { "any": true, "reason": "operator-asserted upstream CLI/model requirement" },
  "ptyHostBuild": { "any": true, "reason": "operator-asserted upstream CLI/model requirement" },
  "ptyBackend": { "any": true, "reason": "operator-asserted upstream CLI/model requirement" },
  "backendPackage": { "any": true, "reason": "operator-asserted upstream CLI/model requirement" },
  "host": { "any": true, "reason": "operator-asserted upstream CLI/model requirement" },
  "proofScope": "model-client-version-rejection",
  "reason": "The operator records 6.1 Sol as requiring Codex 0.159.1; catalogue evidence is supporting, not a live-turn proof.",
  "evidence": ["CARD-1023/operator-2026-10-03", "CARD-0903/catalogue-section-2.3"]
}
```

The real file must resolve those example evidence IDs to immutable references and
include author/review provenance. `enabled` means eligible for the pure evaluator;
it does not enable an admission call site in S1-S3. D-3 acceptance governs whether
this proposed row stays enabled. No runtime switch can override that boundary.

### Historical Grok rows to preserve

| Observation | Facts supported by retained evidence | Missing/conflicting facts and permitted claim |
|---|---|---|
| Canary d6e4138e-fe11-4d7e-8ebf-b6a80b4fe597 | Session 26d8a18c-03f1-4563-b060-7ce6a1da7c8e; source 5f214b0c1daef4d6d7fbbbfbebd14deb83636639; task-tagged UserPrompt seq 9, report seq 29, server Stopped; requested grok-4.7, transcript grok-4.7-build. Ledger reports CLI 1.0.41 (4220f3b224a6). | Card describes auto-update to 1.0.46 seven seconds after launch. Actual version/backend binding and release ownership are not established by that ledger. Store successful delivery as historical evidence with ambiguous actual CLI/backend; never relabel it 1.0.46. |
| WQ-2 02e5b9b7-9301-4324-b3e9-50fdbd929dc1 | Session 359e1bce-dc7f-477c-832a-29f34b1fbeb8; Grok 1.0.46 (2765805b9442), grok-4.7; Windows; ModernConPty 1.24.260710001; 120x30/ASCII `>`; Ready, UserPrompts 1/10, final report, clean host exit. | CARD-1023 reports runner 261500e2, but the condensed CARD-1011 Code receipt does not supply loaded runtime SHA. Preserve 261500e2 as caller-reported short identity, not an exact full build match. PtyHost build remains unknown. A works observation for the measured path, not every host. |
| Amended WQ-3 d7e561a7-fa8b-4a0e-a74a-fee0fdb842d8 | API report retrieved: source/harness 189042dae13c605ed70d8fe9d6f94923ac1ae747; CLI 1.0.46 (2765805b9442) immediately before launch and banner 1.0.46; runner/harness 1.0.0+189042dae...; ModernConPty package 1.24.260710001; DLL file version 1.24.2607.10001. Fresh worktree, trust=false, startup inputs=[], Ready ~6.6s, one UserPrompt/nonce/TurnEnd and releaseConfirmed=True. | PtyHost executable ae4e629d..., host log 96a2769c..., native digests are shortened in the report. Preserve partial identities; do not import full-hash selectors without the original receipt. This isolated harness is not production runner 261500e2. |

Store the two spellings of the ConPTY package/file version as separate evidence
fields, not a guessed version normalization. WQ-3's original checkpoint reports
1 executed / 1 passed / 0 failed / 0 skipped; these are inherited evidence counts,
not tests run by this Plan. No first-release tests run Grok or create inbox hosts.

## Target admission and recording contract for the follow-up

1. Resolve the selected runner, actual final profile/model and launch descriptor
   after existing policies, including remote executable projection. Requested
   tier is not a substitute. For warm/standing reuse, use that process's session
   incarnation and launch receipt, never the default installation's latest probe.
2. Obtain already available, generation-bound actual evidence. The follow-up must
   specify the launcher-to-process binding and update race contract before code:
   probe fingerprint alone is insufficient. No available proof means Unknown and
   allow, with unknown reason recorded. No eight-second admission probe returns.
3. Evaluate enabled evidence-backed rows as D-4. At create/retry, a positively
   bound match may refuse early; absence of launch identity simply defers the
   determination. Re-evaluate after target/model/incarnation changes and immediately
   before launch or the first task input to a reused process. A prior allow is not
   a permanent certificate. An already-running task is never killed retroactively.
4. Persist the attempted combination, source/binding, matched IDs, catalogue digest
   and disposition, including all unknown values and all allows. The follow-up
   TestDesign must cover duplicate delivery/retry, upgrade between observations,
   cold vs warm identity, restarts and recording failure. Task success does not
   automatically certify the full tuple; a works claim needs its scoped evidence.
5. Refuse only a positive known-broken match with the typed reason from D-9.
   Otherwise continue the original task/model/runner through existing policies.
   No silent fallback, downgrade, expiry hold or retry is introduced by the matrix.

Specific missing producers belong in that follow-up: actual PtyHost build/artifact
from its running shadow copy; launch/session CLI identity; Grok version and build;
per-model completion evidence; OS/build/architecture/infra as measured. TestDesign
must use deterministic fake upgrade/generation races and a separately commissioned
modern Windows lane for the native receipt. Do not require the whole fleet to
upgrade or require old hosts to fill optional dimensions before admitting work.

## Slices, files and tests

Paths marked new are proposed names, not existing implementations. Build on the
landed inert CARD-0959 result and recheck the listed collisions before Code.

| Slice | Production/docs files | Tests and completion condition |
|---|---|---|
| S1: catalogue and pure semantics | New `antiphon.compatibility.json`; new `server/Application/Dtos/CompatibilityDtos.cs`, `server/Application/Services/CompatibilityEvaluator.cs`, `server/Infrastructure/Agents/CompatibilityCatalogueLoader.cs`; resource/DI wiring in `server/Antiphon.Server.csproj`, `server/Program.cs`. | New `tests/Antiphon.Tests/Application/CompatibilityEvaluatorTests.cs` and `tests/Antiphon.Tests/Infrastructure/CompatibilityCatalogueTests.cs`: valid embedded seed, schema failure allows, exact kind/model/dimensions, version boundary, unknown binding, conflicting proof and disabled rows. No runtime admission caller. |
| S2: reuse the existing observation feed | New `server/Application/Services/CompatibilityObservation.cs`; append optional field in `server/Application/Dtos/SessionRunnerCatalogueDtos.cs`; project in `server/Application/Services/SessionRunnerCatalogue.cs` from the descriptor it already fetched; pass the injected catalogue through its existing call in `server/Api/Endpoints/SessionRunnerEndpoints.cs`. Preserve CARD-0959 error/time/fingerprint semantics. | New `tests/Antiphon.Tests/Application/CompatibilityObservationTests.cs` and `RunnerCompatibilityProjectionTests.cs`: old DTO/nulls, generation clearing inherited from CARD-0959, default versus actual, no extra probe/RPC, unchanged availability/eligibility/capacity and unmodified original Codex fields. |
| S3: remove duplicate metadata and document authorship | `server/Application/Services/ModelLevelAliases.cs`; new `docs/compatibility-matrix.md`; bounded updates to `docs/agent-kinds.md` and `docs/ops-http.md`. Seed evidence summaries/references remain in the catalogue/owner doc, not generated card files. | Preserve/extend `tests/Antiphon.Tests/Application/ModelAliasTests.cs`; transfer the one metadata method from `tests/Antiphon.Tests/Application/CodexCliObservationTests.cs` into CompatibilityCatalogueTests as D-3 specifies. New `CompatibilityNoAdmissionRegressionTests.cs` exercises existing create/retry paths with bad/missing samples and a matching catalogue fixture, asserting unchanged status and zero typed version-probe calls. Docs explicitly distinguish first release from future enforcement. |

Commit/push each coherent slice, then freeze S1-S3 before their single grouped
checkpoint run; the transferred metadata assertion requires the final grouped
source. Do not edit while a test runs. No client,
PtyHost, runner producer, deployment, routing, cleanup or native test change is in
this first release. No DB migration is in S1-S3.

## TestDesign handoff

TestDesign must freeze method names, assertion labels, guard/PC inventory and
execution counts against the actual landed dependency. It must not import the
archived CARD-0959 gate tests or turn unknown coverage into a review refusal.
The new tests below are planned, not claimed to exist or pass.

| ID | Required ordinary proof | Proposed test owner |
|---|---|---|
| V-1 | Embedded catalogue validates; digest identifies exact bytes; bad schema/duplicate ID/omitted selector/missing evidence cannot create a deny rule; missing resource fails open. | CompatibilityCatalogueTests (at least 6 distinct executions) |
| V-2 | A fully bound Sol tuple below the accepted threshold matches; equality/newer stable release, Astra, other kind/model, missing/failed/stale-only evidence, another constrained host/build/backend and disabled row do not. Prerelease/build identity and conflicting evidence behave as D-4. | CompatibilityEvaluatorTests (at least 10 distinct executions; TestDesign expands data rows) |
| V-3 | Current/default sample has no model proof; observed times/unknowns remain faithful; Grok drift and partial historical receipts are not fabricated into exact tuples. | CompatibilityObservationTests (at least 6 distinct executions) |
| V-4 | Existing runner-list route exposes additive observations from the descriptor already read; old callers/payloads and existing CLI projections work; no extra diagnostic RPC/process and no changed eligibility. | RunnerCompatibilityProjectionTests (at least 4 distinct executions) |
| R-1 | Ladder strings, exact model semantics, aliases and floor removal remain correct. | Existing ModelAliasTests plus seed assertion in CompatibilityCatalogueTests |
| R-2 | Create/retry with matching/unknown/malformed diagnostic samples keep existing outcomes; catalogue loading/projection never becomes a task permission and calls no typed CLI probe. | CompatibilityNoAdmissionRegressionTests (at least 4 distinct executions); reuse landed inert fixture patterns |

TestDesign should prioritize PCs that remove a model constraint, treat null as
wildcard, change the version boundary, reuse a stale/default sample as actual,
erase a contradictory observation, relabel Grok's historical version, or alter
eligibility in projection. Bind each to a precise method and production line.
These are post-land obligations, not Plan-stage executions. First-release tests
need no native provider process, full suite, live canary or Windows-only lane.

### Checkpoints

**Proposed selection for the separate TestDesign freeze.** Every row is one
isolated build and one exact class filter. All four rows use the **portable managed
lane** selected by current runner defaults, with no Runner/Platform pin; new tests
must avoid OS-native calls. Lane names are in Group because the importer does not
accept an arbitrary Lane column. Min counts below are design floors for distinct
TUnit executions, not internal assertions or measured results.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1023-catalogue/` | portable-catalogue | `/*/*/CompatibilityCatalogueTests/*` | V-1, R-1 seed | all planned methods, >= 6 executed, 0 failed/skipped | 6 | 4 |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c1023-matching/` | portable-matching | `/*/*/CompatibilityEvaluatorTests/*` | V-2 | all planned methods, >= 10 executed, 0 failed/skipped | 10 | 4 |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c1023-observations/` | portable-observations | `/*/*/(CompatibilityObservationTests*)\|(RunnerCompatibilityProjectionTests*)/*` | V-3, V-4 | both classes, >= 10 executed, 0 failed/skipped | 10 | 5 |
| CP-4 | S1-S3 | `tests/Antiphon.Tests -> bin-c1023-regression/` | portable-admission-regression | `/*/*/(ModelAliasTests*)\|(CompatibilityNoAdmissionRegressionTests*)/*` | R-1, R-2 | both classes; all ModelAliasTests plus >= 4 new executions, 0 failed/skipped | 4 | 6 |

The fourth row's minimum is deliberately a conservative lower bound until
TestDesign counts the landed parameterized ModelAlias roster; the Expect roster
is mandatory and may not be replaced by the floor. TestDesign must bind R-2 to
real create/retry fixtures with observable outcomes, not source-string assertions.
It must also inspect the still-required inert dispatcher/reuse proofs and select
only an exact existing method if the new composition actually changes their path.

Use the checkpoint tool through the host build-slot gate, one run per committed
slice group; wait until the run is complete (exit is not 75). Each row produces
SHA-bound source/build receipts and its unedited CHECKPOINT line/counts. All other
builds/test drivers, including tool bootstrap, use `scripts/build-slot.ps1`.
Alternate outputs use forward-slash `bin-c1023-.../` paths and are removed after
their owned runs finish. Generated receipts/TRX/logs remain ignored. Code/Review
run `scripts/check-evidence-diff.ps1` over the complete task range. A slot timeout
is not permission to run unleased. Confirm inherited failures with the exact
failing method on the base; no timeout widening, retries or softened assertions.

Provisional ordinary execution floor: **19 minutes**, plus authoring and slot
waits; TestDesign refines this from the landed fixtures. No build/test ran in this
Plan dispatch. Separate TestDesign owns the complete Verification design and PC
cost before any Code dispatch.

## Collision and activation ledger

Fetched refs below are read-only planning inputs, not branch merges or landed
claims. Re-read task status and the final source before Code.

| Work | Inspected input / collision | Order and ownership |
|---|---|---|
| CARD-0959 Code bd02f8d9 | Inert re-freeze on `origin/feat/card-task-4170230f` at 9bd096d2bb3695b7bd06a98cff15cbf6f96fcb05, section `Inert observation re-freeze`; Code tip 03249530fe37dfbe0699dec64bfb1af0ff1f9c25, verification pending. Overlap: ladder, catalogue DTO/projection, Program, observation tests/docs. | Hard predecessor. Consume reviewed inert land. Never cherry-pick superseded gating or edit its branch. Re-freeze S1-S3 references after it settles. |
| CARD-1011 Code d422c5a9 | Tip 51f22cb2dbdf2de07cd8c1ec8715333ca4447c14, Final pending. Owns routing/orchestrator bundle, pin guidance, Grok qualification ledger and related agent docs. | Reuse evidence only; no bundle/pin edits here. Let its living-doc changes land before S3, preserving qualification limits. No additional inbox or paid Grok canary. |
| CARD-1018 Code 13149836 | Tip 404087ef07a0a6cbab100fe5d272265b6408d868. Owns ProviderSignInRequiredException and wording tests. | No touch. A compatibility refusal remains a distinct future exception; do not fold sign-in wording into it or restore the Windows-user message. |
| CARD-1013 | Windows opened-handle coverage confirmation: 20 executed / 20 passed / 7 skipped; LF digest discrepancy and privileged symlink coverage remain distinct observations. | No checkpoint-tool/handle/path-validation edits. Its unverified rows do not decide provider compatibility. |
| CARD-1020 | Review card for test-owned PtyHost/OpenConsole linger. | No native test fixture, TTL, host disposal or shared-process cleanup edits. This release has no native checkpoint that could repeat the orphan scenario. |
| CARD-1017 freezes | Plan `2026-10-03-card-1017-completed-card-worktree-cleanup-plan.md` at 57fa586162b0f2ee1cb4fa97cbeee4f5ef86f8c3. Owns Done-card authority, report survival, ignored evidence disposal and remote cleanup. | No cleanup schema/service/retention changes. Essential catalogue evidence must survive in committed summaries or canonical reports; do not make disposable TRX paths the sole proof. Recheck its later TestDesign freeze before evidence authoring. |
| CARD-1022 plan | `2026-10-03-card-1022-modern-conpty-only-plan.md` at 13d7100febc3f4b595299eb982bf7fc5e44c88af. First release plans UnixPty, coherent runner backend decision and additive deprecation field after 0959. | No parallel producer/contract edits in this release. Read its current backend strings as data. Future native receipt work waits for a shared contract freeze; no inbox rows are built. |

Activation is caller work after ordinary Review/land: verify source publication,
read live defaults/inventory, use canonical restart procedure and confirm
`/api/version` plus the embedded catalogue digest and additive observations.
Old/failed/stale samples continue to allow through the compatibility policy.
Record the first release as an observation milestone and commission the correlation
follow-up with the target contract above. Do not claim enforcement, host upgrades,
Grok qualification, or PC completion from this Plan or from an HTTP health check.

## Decision handoff

Accept or revise D-1's observation-first boundary and D-3's operator-asserted Sol
range/independent-infrastructure seed. The recommendation is to accept both,
retain Astra 0.153.4 as guidance, and proceed to a separate S1-S3 TestDesign after
CARD-0959's inert land. If the range assertion is not accepted, disable that seed
and keep progressing on the catalogue/observation work; commission a scoped
runtime rejection measurement before any future refusal. No answer was needed
to produce this reviewable plan, and no live policy has been changed.
