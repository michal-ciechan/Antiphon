# CARD-1339: remote-aware release tag allocation and promotion

Plan against `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`, card revision 1,
for task `d5452b67` on `feat/card-task-d5452b67`. All implementation slices
target **master only**. This dispatch produces documentation, not release changes.
The separate TestDesign stage must complete the verification section before Code.

Read [release gates](../../release-gates.md) and
[testing and build](../../testing-and-build.md) as the owners. CARD-1311's
[branch-model plan](2026-10-17-card-1311-release-branch-model-plan.md) supplies the
existing naming, immutable-tag and moving-tip contracts.

## Scope and execution boundaries

Fix remote tag occupancy, exact ref resolution, the allocation day, promotion
script hashing, and promotion's authority/reservation checks. Preserve candidate
identity, legacy stamp publication, create-only tags and same-reservation recovery.
Use five sequential 45-60 minute slices. Introduce no release or CI lanes,
schedule changes, deployment actions, or session behavior.

The Plan task must never create, push, move or delete a tag, or run
`publish-release.ps1` or `promote-release.ps1`, including through their test
harness or with `-WhatIf`. None was run. The tests below are specifications for
the next stages, not permission to execute those operations in this dispatch.
Future execution must be confined to disposable local fixture repositories;
the Code commissioning brief must explicitly scope any fixture tag/entrypoint
execution. There is no authorization for production publication.

Read-only platform discovery on 2026-10-19 used `GET /api/runner-defaults`
(revision 2, no kind overrides) and `GET /api/session-runners` (available Windows
and Linux capacity). Use current defaults, omit `-Runner` and `-Platform`, and
do not encode a fleet location. TestDesign's checkpoints name the existing
PowerShell script-contract execution lane through `Antiphon.Tests`; no OS-specific
feature is needed. No session waiting for input is added or changed.

## Ground truth

References below describe the base commit, not a claim of test execution.

| Card assumption / required behavior | What the inspected code or remote actually does | Consequence |
|---|---|---|
| Hand-published finals consume release numbers. | `Get-ReleaseGatePreReleaseAllocation` and `Get-ReleaseGateTagProposal` in `scripts/lib/release-gate.ps1:489-610` read only `publications.json`. They have no remote-tag input. Read-only remote enumeration confirmed `v2026.10.15.1` peels to `067b47e2dc903247f1a7c8aa539032cc4266e4ad` and `.2` to `6ca859d006b0a3cac9a88dc6bd551c37e98b2217`. | An empty journal can propose `.1-rc.1` beside an existing `.1` final. The card states these finals were never journalled; this task did not inspect live journal files. |
| A long-lived line uses the UTC publication day for new names. | `Test-ReleaseGateCandidateRef` returns `CutUtc` from the branch name; `release-candidate.ps1:198` persists it, and `publish-release.ps1:210-218` passes it into allocation. | A new candidate on an old line continues using the line's date. The existing rollover test supplies a new date manually, so it does not prove the publisher's clock wiring. |
| A requested remote ref is matched exactly. | Candidate reads in `release-candidate.ps1:149,246,295`, publisher head/tag reads at `:172,290,305`, and both remote helpers in `promote-release.ps1:41-60` take the first whitespace-delimited SHA. Git's requested pattern is not a guarantee that the returned full ref equals the desired ref. | Nested tag names such as `refs/tags/refs/tags/v...`, or branch suffix matches, can cause a false conflict, false agreement or false resume. `release-gate.ps1` should own the shared parser; it currently owns no exact remote parser. |
| Promotion hashes the code that can write a final tag. | `$script:ReleaseAuthorityScripts` in `scripts/lib/release-authority.ps1:49-56` lists six files and omits `promote-release.ps1`. The pinned verifier checks current file hashes, not the branch tip. | Add the promote file to the existing hash contract; do not hash a moving ref. |
| Promotion validates authority and the reservation. | `promote-release.ps1` checks green credit, candidate ids/SHAs, the pre-release peel and, on this master base, the live branch tip. It imports the authority library but never calls its validator. | A plausible green/candidate pair can reach final-tag creation without the publisher's policy/ledger/hash checks. |
| An incorrect `ReleaseRoot` fails closed. | `Update-PromoteFinalTag` returns if `publications.json` is absent and discards `Set-ReleaseGateReservationFinalTag`'s false result. That setter returns false on a missing row or invalid final name. | Promotion can return success after no reservation was updated. Validate before writes and make failed persistence explicit. |
| The existing promote test proves the target commit. | `New-C599GitSeams` in `scripts/test-release-gate.ps1:114-184` accepts `tag` without recording its target, returns fixture HEAD from `rev-list`, and records HEAD for tag pushes. `C1311_PreReleaseTag` uses equal HEAD and peel in its success case. | An implementation that tags HEAD can pass. A local real-Git observer with HEAD different from the credited peel is required. |
| A master fix updates the current rc tooling. | Read-only `ls-remote --heads` found `rc/2026-10-18` at `81cfb267a0af5c0ddb65e8b238af355067bee9cc`. Its allocator, publisher and authority list have the same gaps; its promote script additionally predates CARD-1311's live-tip check. | Master-only landing does not repair that checkout. See the release prerequisite below. |

## Decisions

These are selected implementation decisions for TestDesign, rather than unresolved
defaults requiring approval. Branch retargeting and live publication remain outside
this task's authority.

| Id | Decision and reason | Rejected alternative |
|---|---|---|
| D-1 | Resolve refs by ordinal, case-sensitive equality of the complete second field in `ls-remote` output. Return explicit found, absent and failed states; require a full SHA for a matched record and refuse malformed/ambiguous matching records. Centralize parsing and query helpers in `lib/release-gate.ps1`. | First line wins, suffix matching, `-like`, or treating a failed query as an empty remote. |
| D-2 | Before any new reservation, union journal occupancy with a fresh successful remote tag inventory. Canonical final names close N; canonical pre-release names occupy `(N,K)`. Count lightweight finals as occupied as well as annotated finals. Never fabricate published journal rows for remote-only tags. | Local tags, fetched-tag caches, GitHub release listings, or importing hand-published finals as if this journal published them. |
| D-3 | Preserve the allocator's policy: use the greatest open N and its greatest K plus one; if none is open, use the smallest unused positive N at K=1. Remote finals close their N even if the journal has an open pre-release there. Legacy final allocation skips any N occupied by a final or pre-release in either source. Same candidate/SHA reuses its existing reservation; a new conflict refuses without silently renumbering that reservation. | Allocate N per attempt, use a global K, or overwrite/reassign a reserved name after an external publication. |
| D-4 | For a new long-lived reservation, capture `Get-ReleaseGateUtcNow` once per publisher attempt and allocate on that UTC day. The persisted tag fixes the date on retries, including across midnight. Keep the branch-derived candidate id and existing `candidate.cutUtc`; retain stamp-date allocation for legacy stamp candidates. | Rewrite candidate identity/journals, use local time, or keep using the long-lived branch's creation date for every new reservation. |
| D-5 | Add `promote-release.ps1` to `ReleaseAuthorityScripts`, then have promotion reuse `Test-ReleaseGateAuthority` and `Test-ReleaseGatePublicationGate` before both fresh and resumed success. Add explicit `Repository` and `AntiphonApiUrl` inputs with the publisher's resolution rules. Preserve candidate/peel/live-tip equality. | Only validate green booleans, check a hash without policy/ledger authority, or weaken the existing authority validator for promotion. |
| D-6 | Require exactly one typed reservation matching candidate id, full SHA and exact pre-release tag before any final-tag write or same-final success. Require the row to be published, with release identity, and reject an inconsistent existing `finalTag`. Make final-tag recording validate the same identity, write atomically, and verify the recorded row. | Create a reservation during promotion, match by SHA alone, ignore a missing root/row, or discard the setter's result. |
| D-7 | Treat an already present exact final at the expected peeled SHA as recovery only after all authority and reservation checks pass. A final at another SHA refuses. A direct ref with no supported peel is present but unverified: fail `tag-peel`, never treat it as absent. Preserve both pre-release and final tags on all failures. | Fall back from annotated-tag object id to commit SHA without proof, declare success from a suffix match, or delete/recreate a conflicting tag. |
| D-8 | Keep this card master-only. All behavioral fixes are required before trusting these entrypoints for the next automated tag. If the next tag is produced by scripts from `rc/2026-10-18`, a separately commissioned, reviewed backport is required; do not quietly retarget these slices. | Assume master landing changes rc tooling, cherry-pick without authorization, or waive hash checks to mix old authority with new scripts. |
| D-9 | Use local real-Git fixtures to prove names and peeled targets, and fixture-only GitHub/card readback adapters for other boundaries. Keep the production seam admission intact. Existing fake-Git tests remain useful for error injection but are not target-SHA evidence. | Assert only command text, use HEAD-equals-peel controls, call a live GitHub remote, or broaden production seam admission to make tests pass. |

Adding a required script hash deliberately invalidates old six-file authorities.
Recut/requalify through the existing process; never edit old authority files to
insert a digest. Full authority validation also retains the current release-card
receipt/readback contract: a stale revision or unavailable readback refuses
promotion, including resume. This plan adds no bypass or receipt-refresh mechanism.
It does not claim that the currently unqualified release workflow becomes qualified.

## Implementation shape

The shared remote reader accepts a fully qualified ref. Branch callers normalize
their validated candidate to `refs/heads/<ref>`. Tag target checks query and parse
the exact direct and `^{}` records, keeping tag-object identity separate from the
peeled commit. A successful empty result means absence; a nonzero Git result or
unusable matching record means failure. Callers retain their existing public
refusal categories where applicable.

Inventory uses a complete direct-tag listing, for example `ls-remote --tags --refs
origin`, and admits only exact `refs/tags/<valid CalVer>` names. Nested names and
other non-CalVer tags do not consume numbers. Peels must not double-count a tag.
Keep the allocator pure by passing the validated inventory into its proposal
functions. Production callers must supply that inventory explicitly, even when
empty; no optional empty default may silently recreate journal-only allocation.
Update direct test callers to provide deliberate empty fixture inventories.

The publisher performs authority admission first, then remote inventory and
proposal. `-WhatIf` follows the same read/decision path and writes neither a
reservation nor publication authority. A real attempt rechecks the selected exact
tag and corresponding final before creating a tag. A remote read failure refuses;
a create-only push collision re-reads exact refs and either proves the same SHA
or refuses. It never automatically allocates a second name after an ambiguous
write. This closes observed collisions without claiming a distributed lock across
independent release roots or external publishers.

Promotion orders shape/evidence checks, exact remote reads, authority validation,
reservation validation, then create-or-resume and checked journal persistence.
The intent remains the existing reservation; do not add a competing promotion
journal. A post-push journal-write failure returns nonzero with a distinct
`promotion-journal-write-failed` reason and reports the known remote fact. It must
not report completed promotion. A retry at the same tag/SHA can finish recording
the row without another remote write. Missing/mismatched/duplicate rows refuse
before the first write, with stable reason tokens specified by TestDesign.

## Slices

Each slice has one decisive red-first witness, committed and pushed before the
fix, followed by one narrow green selection. The names below are proposed new
TUnit methods in `tests/Antiphon.Tests/Scripts/ReleaseTagAllocatorTests.cs`, each
wrapping the corresponding new `Test-C1339_*` function in
`scripts/test-release-gate.ps1`. Use the existing `ScriptHarness` and
`ParallelLimiter<ProcessSpawnLimit>`; keep fixture children within its timeout.
TestDesign will assign exact counts and checkpoint commands, including only the
named adjacent regressions needed by each slice.

### S1 - Exact remote refs (55 minutes)

Files: `scripts/lib/release-gate.ps1`, `scripts/release-candidate.ps1`,
`scripts/publish-release.ps1`, `scripts/promote-release.ps1`, the harness and new
TUnit class. Add the reader and wire every remote head/tag consumption site,
including recovery reads. Adapt fake responses to canonical full refs without
changing admission or assertions.

Red-first witness: `C1339_ExactRemoteRefs` supplies a local remote whose first
pattern match is `refs/tags/refs/tags/<tag>^{}` at B and exact `<tag>^{}` is at A.
Observe that production resolution selects A regardless of order; a nested-only
ref is absent, not a successful final-tag resume. Include the equivalent branch
suffix case, exact case sensitivity, malformed SHA, failed query and conflicting
duplicate matching rows through read adapters.

Narrow green: that method plus `ReleaseGatePublicationTests.C599_CandidateIdentity` and
`ReleaseBranchModelGateTests.C1311_LongLivedRcRef`, retaining candidate identity
and no-push behavior for a long-lived cut.

### S2 - Remote occupancy in N/K allocation (55 minutes)

Files: `scripts/lib/release-gate.ps1`, `scripts/publish-release.ps1`, the harness
and new TUnit class. Add explicit inventory input to proposal/reservation, wire
successful remote enumeration into preview and reservation, and recheck the
chosen names before writes. Extend fixture remote enumeration/target storage as
needed; do not infer original full refs from punctuation-collapsed fake keys.

Red-first witness: `C1339_RemoteTagAllocation` uses an empty journal and a local
bare remote holding `v2026.10.15.1` and `.2`; on 2026-10-15 the proposal must be
`v2026.10.15.3-rc.1`. Extend the same bounded case with remote-only `.3-rc.4`
yielding `.3-rc.5`, final `.3` closing N=3, annotated/lightweight occupancy,
irrelevant nested tags, journal-only reservations, and legacy final allocation.
Failed inventory must leave the journal unchanged and produce no proposal.

Narrow green: that method plus `ReleaseGatePublicationTests.C599_TagRecovery`;
preserve same-candidate/SHA reuse and mismatch refusal. Add assertions for an
existing reservation that now conflicts and for the read-only proposal path.

### S3 - UTC publication day without reservation churn (45 minutes)

Files: `scripts/publish-release.ps1`, `scripts/lib/release-gate.ps1` if a small
date-selection helper is needed, the harness, new TUnit class and the allocation
paragraph in `docs/release-gates.md`.

Red-first witness: `C1339_AllocationUtcDay` drives a long-lived candidate on
`rc/2026-10-15` with the fixture clock on 2026-10-19 and observes a new
`v2026.10.19.1-rc.1`, not a name dated October 15. Move the clock across UTC
midnight: the existing candidate reuses its reservation, a new SHA uses the new
day, and a legacy stamp still uses its stamp date. Check caller wiring, not only
a pure allocator supplied the desired date by the test itself.

Narrow green: that method and `C1339_RemoteTagAllocation`; both exercise the
combined day/occupancy contract. A preview creates no reservation.

### S4 - Promotion authority and hash coverage (60 minutes)

Files: `scripts/lib/release-authority.ps1`, `scripts/promote-release.ps1`,
`scripts/test-release-gate.ps1`, the new TUnit class and
`tests/Antiphon.Tests/Scripts/ReleaseGatePublicationAuthorityTests.cs` if its
required row roster changes. Include the promote script in the existing explicit
G-300 script-digest mutation matrix. Upgrade C1311 promotion success fixtures to
valid authority and ledger evidence; do not replace validators with test predicates.

Red-first witness: `C1339_PromoteAuthorityHash` starts from a valid fixture, changes
only the recorded `promote-release.ps1` digest, and requires
`script-digest-mismatch:promote-release.ps1` with zero local/remote tag writes.
Cover missing digest/authority, non-Full or diagnostic credit, failed ledger and
unavailable receipt readback. Restore the valid evidence and prove admission.
Apply the same refusal to an otherwise resumable final.

Narrow green: that method plus
`ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedgerJoins` and
`ReleaseBranchModelGateTests.C1311_MovingTipAuthority`. The latter's six-file
comment must change while its branch-tip exclusion assertion stays intact.

### S5 - Checked reservation and real-Git promotion recovery (60 minutes)

Files: `scripts/promote-release.ps1`, `scripts/lib/release-gate.ps1`, harness,
new TUnit class and `docs/release-gates.md`. Tighten reservation lookup and final
recording, preserve exact create-only recovery, and finish the owner documentation.

Red-first witness: `C1339_PromoteReservationAndTarget` has otherwise valid green,
authority and remote pre-release evidence but no matching publication row. Require
a named refusal and no final tag; current code can report success. Exercise missing
journal, foreign SHA/tag/candidate, duplicate row, unpublished row and persistence
failure as individually isolated variants of the valid fixture.

Narrow green: that method plus `ReleaseBranchModelGateTests.C1311_PreReleaseTag`.
The valid real-Git control must have credited pre-release and live rc tip at A,
checkout HEAD at B, and verify the final from an independent reader of the local
bare remote: its object is an annotated tag and its peel is A, never B. Prove the
pre-release is unchanged. Re-run the same request: no new write. Inject a
post-push journal persistence failure and prove nonzero followed by same-tag
recovery; an existing exact final at C refuses without changing either tag.
Neither a trace-only assertion nor the fake remote's stored HEAD is sufficient.

## TestDesign handoff and acceptance

The brief did not fold TestDesign into Plan. Add the required `## Verification
design` and `### Checkpoints` in the separate stage, with exact method filters,
assertion/result counts, guard inventory, ordinary/PC costs and the existing
script-contract lane named. The slice selections above are inputs to that design,
not an executable checkpoint manifest or a claim that those new methods exist.
Verify existing method names before freezing the table.

All Git transport destinations must be owned temporary paths. The authority
validator currently accepts only GitHub-shaped origin metadata and reads it with
real `git remote get-url`; a bare local origin alone cannot pass that check.
Use synthetic `fixture/release` origin metadata for that identity-only read and
have the fixture Git adapter replace the transport's `origin` argument with its
owned local bare-repository path. Validate containment before every transport
command, deny any unhandled/network transport, and use an independent real-Git
reader of that bare path for assertions. This preserves the real policy-blob and
origin-identity validation without broadening production admission or contacting
the metadata URL. GitHub and card readback use local adapters; no live broker,
provider or release service is part of these tests.
Do not broaden to the full release harness: several old cases exercise unrelated
coordinator/registration behavior. Record concrete adjacent failures at the base
before attributing them to this change.

Each red must fail the named behavioral assertion with nonzero executed counts,
not fixture setup or a missing function. TestDesign should reserve method-scoped
post-land controls for suffix selection, omitted remote occupancy, wrong date
source, omitted promote digest/admission, ignored journal failure and tagging
HEAD instead of the peel. A real-Git happy control alone may pass old code; it
complements S5's missing-row red and the target-SHA mutation control.

Code's ordinary execution uses the host build-slot gate and the checkpoint tool,
isolated `bin-<name>/` outputs and ignored evidence. Commit/push red and repaired
slices truthfully; wait for every foreground run and clean owned alternate output
directories. No full application, browser or release qualification run is required
for this bounded tooling change. Ordinary Review remains required before land.

Acceptance requires: remote-only finals cannot collide with new proposals;
canonical refs alone determine identity; failed reads cannot allocate; new
long-lived reservations follow UTC while retries are stable; promotion validates
authority, script hashes and one published reservation before success; failed
recording is visible and recoverable; and real Git proves the exact final target.

## Release prerequisite for rc/2026-10-18

**Does any slice need to be on rc before the next tag? Yes, if that tag is made
with the rc checkout's release scripts.** That checkout still has these defects.
It needs the behavioral changes from S1-S5, plus the existing CARD-1311 live-tip
promotion guard already present on this plan's master base. This is a separately
authorized backport/review obligation, not permission to land this card on rc.
The tests and docs accompany the backport as its verification contract.

No slice in this dispatch changes `rc/2026-10-18`, creates a tag or activates a
release. A master-only land is insufficient evidence that the next cut uses the
fix. Before a later authorized publication, establish the actual tool source and
hash-bound authority; old evidence must not be reused with the new script list.
Launching an entrypoint from master while a candidate's self-update hop runs rc
scripts is not a proven substitute for that identity check. Release qualification
and the backport decision remain with the caller; TestDesign can proceed now.

## Plan validation

Read the card and owner documentation; inspected the allocator, remote consumers,
promotion, authority validation and existing harness; compared the observed rc
commit and queried exact remote refs read-only. No tests, builds, release
entrypoints, tag writes or deployments were run. Final branch publication and
merge-tree results are reported with this plan's commit in the task report.


## Verification design

This append-only TestDesign binds the S1-S5 plan at
`dab3d6e06a88b685ee37b714115199eafc6b2ee6`. It changes no fix decision.
The commissioning budget is 30 minutes for this design, not an execution budget
for Code or post-land Mutation. No build, test, entrypoint or tag operation was
run in TestDesign; the optional one S1 compile/red proof was not used.

### Inspection

Bodies read at the start ref, rather than inferred from names:

- `scripts/lib/release-gate.ps1`: allocation/proposal/reservation lookup, published/final setters and publication gate; `scripts/release-candidate.ps1` long-lived, stamp precheck and recovery; publisher admission, date selection, tag creation/recovery/verification; all of `scripts/promote-release.ps1` -> V-1..V-5, R-1..R-5.
- `scripts/lib/release-authority.ps1`: script list, origin/pinned-policy validator and authority composition; `nightly-common.ps1:Write-NightlyAtomicJson` -> V-4/V-5. Existing detailed ledger validation is reused, not rewritten; its exhaustive historic matrix is outside this bounded ordinary scope.
- `ReleaseBranchModelGateTests`, `ReleaseGatePublicationTests`, `ReleaseGatePublicationAuthorityTests`, and their selected PowerShell case bodies (`C599_CandidateIdentity`, `C599_TagRecovery`, `C599A_ExecutionLedgerJoins`, all three C1311 cases) -> adjacent R-1/R-2/R-4/R-5. `C599_TagRecovery`'s final comment is not a mismatch assertion: its last assertion only checks that seam text exists. New conflict assertions below supply that missing evidence.
- Nearest fixture for new `ReleaseTagAllocatorTests.cs`: `ReleaseBranchModelGateTests.cs`; `ScriptHarness.Validate`, `ScriptHarnessProcess` options/launch, `New-C599PolicyCheckout`, `New-C599Authority`, `New-C599PublishFx`, `Invoke-C599Publish`, `New-C599GitSeams`, `New-C599GitHubSeams`, the independent readers, loopback card stub, `Save/Restore-C599ASnapshot`, `Invoke-C599AVariant`, `Update-C599AAuthority`, and `Invoke-C1311Promote` -> all recipes below. Current actual child default is **300 seconds plus 10 seconds cleanup**, despite the older authority-test comment saying 120. Do not widen it.
- `docs/release-gates.md`, testing owner's checkpoint/CARD-0403/Mutation sections, orchestration stage/Mutation policy, `PlanTableImporter` and `AfterSelector` -> table syntax and phase reporting. No browser, queue worker, session or deployment path is changed.

### Delivery inventory

There is **no new or changed asynchronous delivery path**, queue producer or
session input in S1-S5. Busy-recipient/already-eligible real-queue tests and a
complete UserPrompt transcript are therefore inapplicable, not replaced by
acknowledgements. Existing release-card evidence is consumed read-only; these
tests do not prove that the original report producer delivered it.

The changed synchronous durable handoffs still require recipient evidence:

| Producer -> destination | Durable identity and persistence boundary | Recovery cuts and observable receipt |
|---|---|---|
| Candidate coordinator -> owned bare Git | full candidate ref + pinned SHA; candidate.json before stamp push | S1 fail-before-push and commit-then-lost-response variants re-enter from disk; independent exact bare ref equals pinned SHA, or is absent/conflicting. Long-lived cut observes existing ref and issues no push. |
| Publisher -> owned bare Git, then file-backed GitHub fixture | candidate id + full SHA + reserved tag; publications.json before tag/push, then publish step journal | S2 allocation/recheck plus S1 exact recovery; before-acceptance and lost-response cuts preserve one reservation. Independent bare tag peel and stored non-draft release identity are the receipts, not a push result or Published flag. Existing C599_TagRecovery is a fake-recipient recovery adjunct. |
| Promoter -> owned bare Git -> publications.json | candidate id + SHA + exact pre-tag + final tag; preexisting published row, then final push, then checked atomic row recording | S5 before-push, accepted-but-lost response, post-push journal exception/false/silent-nonwrite, and same-final restart. Independent final object type/peel plus reloaded matching row prove completion. Both pre-release object and GitHub fixture stay unchanged. |
| Bound card API -> authority consumer (read only) | card id + revision + intent/candidate/SHA/run correlation | S4 owned loopback readback unavailable/stale/foreign responses refuse; restored valid response admits. Stored fixture body proves the consumer's readback contract only. |

The real-Git adapter must translate every transport `origin` to the owned local
bare path, validate full path containment and reject unknown/network transports
before spawning Git. Its metadata origin is synthetic
`https://github.com/fixture/release`, used only by real `remote get-url`.
Set fixture-only Git config/environment to forbid network protocols; disable
inherited hooks/signing and clear inherited task/API/provider credentials from
children. The independent reader uses the bare path directly, never the adapter's
cache. No real remote tag may be created, pushed, moved or deleted, and neither
release entrypoint may run against a real remote, including preview.

Substitutes: read adapters manufacture malformed/duplicate/failed `ls-remote`
responses that real Git cannot normally emit; they do not prove transport
behavior. Existing punctuation-keyed fake Git supports recovery fault injection,
but discards actual tag targets and cannot prove names or SHA targets. File-backed
GitHub and loopback card fixtures prove local consumer/readback behavior, not
GitHub, a production card update, report delivery, authentication or scheduling.
An atomic-writer fault proves reported failure/retry at that boundary, not hardware
power-loss durability. Receipt-only or trace-only success is rejected.

### Proves it works now

These are required future observations, **not claims that the absent tests ran**.
All new methods are nonparameterized `[Test]` methods, `[Category("Integration")]`,
`[ParallelLimiter<ProcessSpawnLimit>]`, using the existing `ScriptHarness` and
`Test-<method>` PowerShell dispatch. Use prefix `C1339`. Each guard row below
emits exactly one `Assert-C487` named `C1339 G-<number> <label>`; multiple isolated
variants aggregate with `Assert-C599AMatrix`, never inflate TUnit counts. Required
row strings must be explicit in the C# wrapper; a missing/extra row fails its census.

| V | Behavior / layer | Exact method in ReleaseTagAllocatorTests | Expected evidence |
|---|---|---|---|
| V-1 | S1 production script integration over owned fixtures | `C1339_ExactRemoteRefs` | 1 TUnit result; exactly 19 named C1339 assertion rows below, 0 failed; recipient/journal observations satisfy every variant. |
| V-2 | S2 production script integration over owned fixtures | `C1339_RemoteTagAllocation` | 1 TUnit result; exactly 19 named C1339 assertion rows below, 0 failed; recipient/journal observations satisfy every variant. |
| V-3 | S3 production script integration over owned fixtures | `C1339_AllocationUtcDay` | 1 TUnit result; exactly 5 named C1339 assertion rows below, 0 failed; recipient/journal observations satisfy every variant. |
| V-4 | S4 production script integration over owned fixtures | `C1339_PromoteAuthorityHash` | 1 TUnit result; exactly 5 named C1339 assertion rows below, 0 failed; recipient/journal observations satisfy every variant. |
| V-5 | S5 production script integration over owned fixtures | `C1339_PromoteReservationAndTarget` | 1 TUnit result; exactly 22 named C1339 assertion rows below, 0 failed; recipient/journal observations satisfy every variant. |

Recipe **F** (all rows): one owned real policy checkout and local bare remote,
actual distinct commits A/B/C, pinned eight-suite authority/13 chunks/19 UIDs from
`Get-C599ABasePolicyBytes`, real TRX bytes, matching typed ledger, loopback card
receipt/readback, complete-green and a published reservation where applicable.
Build valid authority using production helpers, including the current script
hashes. Parameterize existing fixture helpers for `fixture/release` rather than
weakening origin validation. Seed tags only in the owned fixture.
Pass `-Repository fixture/release` and the owned loopback `-AntiphonApiUrl`
explicitly on every publisher/promoter fixture invocation; the publisher's actual
Repository default is `michal-ciechan/Antiphon`, not origin-derived. Record the valid
control's successful admission before each single-defect family. Snapshot and
restore evidence bytes; use fresh owned Git repos for variants that change refs,
so no reset/delete of shared refs is needed. A and B must be real commits;
synthetic repeated-character SHAs are only for malformed-response cases.

Recipe **E** (S1): drive existing entrypoints for the first red, never call a
not-yet-created parser. Feed real local `ls-remote` output, preserving complete
refs and varying order; use narrow read-response adapters only for impossible
Git records or a specific call ordinal. The S1 red is `promotion-pre-read`:
otherwise valid promotion must select exact A despite first nested B. Old code
fails at its result/recipient assertion, not at missing-function setup. After the
fix, add direct Found/Absent/Failed parser cases and all caller-specific variants.
For each caller row, all other reads return canonical correct responses so one
unconverted call site cannot hide behind a different failure.
The fixture-confinement controls G-64/G-65 use an inert process-launch recorder:
it never invokes Git, even under mutation. They prove rejection before process
launch, not network isolation. The ordinary transport cases still use real Git.

Recipe **A** (S2): F with allocation clock 2026-10-15, initially empty journal;
remote-only .1/.2 witness first. Change inventory at the adapter's read-to-write
boundary to exercise races. For a guard requiring refusal before a tag write,
assert both zero attempted writes and unchanged local/bare refs, not merely
that the eventual create-only push failed. Explicit inventory input is mandatory
at every production allocation call; direct allocator tests supply deliberate
empty inventories. No optional empty default is admissible.

Recipe **D** (S3): invoke the publisher, not just an allocator given a date. Pin
an old long-lived line and inject UTC clock through the admitted seam. Shift all
fixture evidence times coherently before qualification so time checks cannot
mask allocation; do not requalify or edit evidence between the first reservation
and its midnight retry. A new SHA gets newly qualified evidence. Count clock
calls. Keep candidate id/cutUtc bytes as an independent before/after oracle.

Recipe **H** (S4): F, both no final and exact final at A. Wrong/missing promote
digest is tested with otherwise re-bound authority/plan/ledger digests using
`Update-C599AAuthority`; set a wrong recorded promote key even against the old
six-file builder so the initial witness reaches production. The red witness
asserts the promotion refusal and zero writes, not the builder's key count;
seven-key census is added with the fix. Expected reasons:
`script-digest-mismatch:promote-release.ps1`, `missing-authority`,
`ledger-selection`, `ledger-diagnostic`; set `antiphon-u1` to Failed in its
real TRX and rebind the evidence digest/counts, requiring
`uid-not-passed:antiphon-u1`. Change only authority.policyRawSha256 and rebind
plan/ledger digests to require `policy-raw-digest-mismatch`.
Unavailable/stale card uses `release-card-readback-unavailable` /
`release-card-readback-revision`. Assert nonzero, neither success flag, no new
local tag/push/GitHub write, unchanged refs/journal. Restore valid evidence and
require actual admission. The detailed unchanged authority-validator predicates
keep their existing CARD-0599 matrix; this card's controls target new promotion
admission/caller wiring and script-set membership.

Recipe **P** (S5): F with credit/pre-release/live tip A, checkout HEAD B, and
independent bare `rev-parse refs/tags/<final>`, `cat-file -t <object>` and
`rev-parse refs/tags/<final>^{}` observations. First remove only the publication
journal: S4's authority stays valid, making the missing-reservation red reachable.
For mismatched row variants keep exactly one typed row and change one field;
classify the mismatch instead of reducing every wrong row to "missing". Apply
all reservation negatives to fresh and existing-final branches. Proposed stable
`promotion-reservation-*` tokens below are part of this test contract; malformed
JSON/schema/field shape uses `promotion-reservation-invalid`, missing file/empty
array uses `promotion-reservation-missing`, duplicates use
`promotion-reservation-ambiguous`. Validate the same tuple again when recording.

For persistence faults, use a child-local, path-scoped I/O wrapper around the
atomic writer, installed through the already admitted test fixture after imports;
never replace a gate/predicate or broaden production seam admission. Exercise
throw-before-replace and successful-return-without-write separately. To reach a
real setter's false result, remove the matching persisted row at the last remote
verification callback, after admission but before recording; do not replace the
setter with a fake predicate. Restore that valid fixture row before recovery.
G-66 separately replaces its identity at that boundary and checks that the
replacement row was not overwritten.
The real nonfaulted writer still performs the atomic replacement. Record old
journal bytes; remove the fault and restart from disk against the same bare repo.
If Code cannot bind this injection to the real recording path without changing
the planned seam contract, return `next: plan`; do not simulate promotion itself.

Reachability/classification for **every** guard/PC row: recipe E/A/D/H/P as mapped
by slice, valid control first, exactly the listed input defect or boundary cut,
then the real entrypoint/helper named by that row. Ordinary cases are integration
regressions (V/R), not qualification. PC changes are source mutations on a bound
post-land snapshot, not extra Code executions. A named refusal plus unchanged
recipient state is the negative oracle; a valid result requires recipient and
journal evidence. Never count an earlier unrelated refusal, build/setup failure,
timeout or zero tests as a witness.

### Guards the regression

- R-1: first-match regressions -> V-1 and every caller-specific G row; adjacent `ReleaseGatePublicationTests.C599_CandidateIdentity` (13 harness rows) and `ReleaseBranchModelGateTests.C1311_LongLivedRcRef` (10). Preserve pin-once, conflict/no-force and no-push behavior.
- R-2: journal-only allocation, collisions or reservation churn -> V-2; adjacent `ReleaseGatePublicationTests.C599_TagRecovery` (12). The new cases supply real mismatch checks absent from that old method.
- R-3: line date reused for new publication or midnight renumbering -> V-3 plus V-2, through actual publisher wiring.
- R-4: promoting with stale/unqualified authority -> V-4; adjacent `ReleaseGatePublicationAuthorityTests.C599A_ExecutionLedgerJoins` (8) and `ReleaseBranchModelGateTests.C1311_MovingTipAuthority` (6). Add promote to the explicit G-300 script mutation list, keep its aggregate row count 8. Change the moving-tip fixture's **hard-coded count 6 to 7** and comment, retaining the branch-tip exclusion assertion. Bind its old promotion calls to valid authority/ledger/API fixtures.
- R-5: false success without a reservation, wrong target, unchecked recording or unsafe resume -> V-5; adjacent `ReleaseBranchModelGateTests.C1311_PreReleaseTag` (8). Upgrade its success fixture to a published row and valid authority/API; preserve assertions and supply explicit empty inventories for direct allocator calls. Its fake-target assertion remains adjunct only.

### Guard inventory

Each row is independently mapped to a distinct PC. The label is the exact
suffix of the required harness assertion name. All are safety-critical within
this plan's changed boundaries; no in-scope guard is declared "none". Existing
validator internals and atomic-file implementation are reused, with their
exhaustive unrelated tests excluded as described above. Code must add guards/PCs
if its implementation introduces another independent refusal branch.

| Guard | Plan ref / safety-critical invariant (assertion label) | PC |
|---|---|---|
| G-1 | D-1: `exact-tag` | PC-1 |
| G-2 | D-1: `exact-head` | PC-2 |
| G-3 | D-1: `ordinal-case` | PC-3 |
| G-4 | D-1: `full-sha` | PC-4 |
| G-5 | D-1: `ambiguous-record` | PC-5 |
| G-6 | D-1: `query-failure` | PC-6 |
| G-7 | S1 candidate long-lived read: `candidate-long-read` | PC-7 |
| G-8 | S1 candidate stamp precheck: `candidate-stamp-read` | PC-8 |
| G-9 | S1 candidate push recovery: `candidate-recovery-read` | PC-9 |
| G-10 | S1 publisher head read: `publisher-head-read` | PC-10 |
| G-11 | S1 publisher push recovery: `publisher-recovery-read` | PC-11 |
| G-12 | S1 publisher final verification: `publisher-verify-read` | PC-12 |
| G-13 | S1 promotion pre-release read: `promotion-pre-read` | PC-13 |
| G-14 | S1 promotion live head: `promotion-head-read` | PC-14 |
| G-15 | S1 promotion existing-final read: `promotion-existing-read` | PC-15 |
| G-16 | S1 promotion push recovery: `promotion-recovery-read` | PC-16 |
| G-17 | S1 promotion final verification: `promotion-verify-read` | PC-17 |
| G-18 | D-2: `remote-finals` | PC-18 |
| G-19 | D-2/D-3: `remote-prerelease` | PC-19 |
| G-20 | D-3: `remote-final-closes` | PC-20 |
| G-21 | D-2: `canonical-inventory` | PC-21 |
| G-22 | D-2/D-3: `journal-union` | PC-22 |
| G-23 | D-3: `smallest-free` | PC-23 |
| G-24 | D-3 legacy: `legacy-occupancy` | PC-24 |
| G-25 | D-2 inventory admission: `inventory-required` | PC-25 |
| G-26 | D-3: `reservation-reuse` | PC-26 |
| G-27 | D-3: `candidate-sha-conflict` | PC-27 |
| G-28 | D-3: `sha-candidate-conflict` | PC-28 |
| G-29 | D-2/D-3 prewrite selected tag: `selected-tag-recheck` | PC-29 |
| G-30 | D-2/D-3 prewrite corresponding final: `selected-final-recheck` | PC-30 |
| G-31 | D-4 preview reservation: `preview-reservation` | PC-31 |
| G-32 | D-4 preview authority: `preview-authority` | PC-32 |
| G-33 | D-4: `utc-day` | PC-33 |
| G-34 | D-4: `one-clock-sample` | PC-34 |
| G-35 | D-4: `midnight-reuse` | PC-35 |
| G-36 | D-4 legacy: `stamp-day` | PC-36 |
| G-37 | D-4 identity: `candidate-date-preserved` | PC-37 |
| G-38 | D-5 hash set: `promotion-hash-required` | PC-38 |
| G-39 | D-5 fresh admission: `promotion-fresh-authority` | PC-39 |
| G-40 | D-5 resumed admission: `promotion-resume-authority` | PC-40 |
| G-41 | D-5 repository: `promotion-repository` | PC-41 |
| G-42 | D-5 readback: `promotion-card-readback` | PC-42 |
| G-43 | D-6 journal presence: `reservation-journal` | PC-43 |
| G-44 | D-6 uniqueness: `reservation-unique` | PC-44 |
| G-45 | D-6 typed row: `reservation-typed` | PC-45 |
| G-46 | D-6 candidate join: `reservation-candidate` | PC-46 |
| G-47 | D-6 SHA join: `reservation-sha` | PC-47 |
| G-48 | D-6 exact tag join: `reservation-tag` | PC-48 |
| G-49 | D-6 publication state: `reservation-published` | PC-49 |
| G-50 | D-6 release id: `reservation-release-id` | PC-50 |
| G-51 | D-6 release URL: `reservation-release-url` | PC-51 |
| G-52 | D-6 prior final: `reservation-final-consistent` | PC-52 |
| G-53 | D-6 resumed reservation: `reservation-resume` | PC-53 |
| G-54 | D-7 target: `target-peel` | PC-54 |
| G-55 | D-7 annotated final: `annotated-final` | PC-55 |
| G-56 | D-7 final conflict: `final-conflict` | PC-56 |
| G-57 | D-7 peel required: `direct-without-peel` | PC-57 |
| G-58 | D-6 checked persistence result: `journal-result` | PC-58 |
| G-59 | D-6 persistence exception: `journal-exception` | PC-59 |
| G-60 | D-6 verified persistence: `journal-readback` | PC-60 |
| G-61 | D-7 same-tag recovery: `same-tag-recovery` | PC-61 |
| G-62 | D-7 pre-release retention: `prerelease-preserved` | PC-62 |
| G-63 | D-3/D-7 create-only push: `create-only-race` | PC-63 |
| G-64 | D-9 fixture transport path containment: `fixture-path-contained` | PC-64 |
| G-65 | D-9 fixture command/transport allowlist: `fixture-transport-allowlist` | PC-65 |
| G-66 | D-6 recording revalidates the admitted reservation tuple: `recording-identity` | PC-66 |
| G-67 | D-3 greatest open N selection: `greatest-open-n` | PC-67 |
| G-68 | D-3 greatest K within selected N: `greatest-k` | PC-68 |
| G-69 | D-2 remote-only tags confer no journal publication: `no-invented-publications` | PC-69 |
| G-70 | D-2 explicit inventory argument: `no-implicit-inventory` | PC-70 |

### Positive controls

The table supplies recipe/variant/case/oracle/reachability/classification through
its S column and the recipe contract above. `M1`..`M5` mean the **exact** V-1..V-5
method names, not a whole class. Row `PC-n` breaks **G-n**, and must make that
method red at the exact assertion `C1339 G-n <label>` from the guard table.
Mutations below are syntactically valid PowerShell behavioral edits. A mutation
with an additional earlier refusal is not accepted evidence.

All source payloads are **binding required**, including after Code: Code records
implemented function/branch anchors and the assertion roster; a mutation pack
must bind exact before/after bytes and source SHA before execution. An English
recipe or passing ordinary test does not make a payload bound. If an anchor is
absent, binding remains required; do not invent a successful mutation result.

| PC | S / exact case | Variant and decisive oracle | Compiling defect |
|---|---|---|---|
| PC-1 | S1 / M1 | Real nested peel at B sorts before exact peel at A; reverse the returned records too. Resolution is A in both orders. | Replace exact tag equality with suffix matching and select the first record. |
| PC-2 | S1 / M1 | Real refs/heads/archive/rc/2026-10-15 at B and refs/heads/rc/2026-10-15 at A; exact resolution is A, suffix-only is Absent. | Use EndsWith on the requested branch name and select the first match. |
| PC-3 | S1 / M1 | Only a case-changed complete ref exists; the requested spelling is Absent. | Change the full-ref comparator from Ordinal to OrdinalIgnoreCase. |
| PC-4 | S1 / M1 | Matching records with a short SHA or a nonhex 40-character value return Failed, never Found or Absent. | Accept any nonempty matched SHA instead of the full-SHA predicate. |
| PC-5 | S1 / M1 | Two exact records at A and B return Failed in both orders; an unrelated malformed row cannot poison a valid exact record. | Return the first exact match before checking conflicting duplicates. |
| PC-6 | S1 / M1 | Adapter returns exit 1 with empty output, then exit 1 with an apparently valid exact record; both return Failed. Successful empty output alone returns Absent. | Ignore the Git exit code and parse its output as a successful query. |
| PC-7 | S1 / M1 | Drive release-candidate against exact A plus suffix B; journal SHA/id are A and there is no branch push. | At the long-lived candidate read only, take the first output SHA instead of the shared reader result. |
| PC-8 | S1 / M1 | Journal pins A; exact remote stamp is B but a suffix is A. Refuse remote-candidate-conflict and preserve B. | At the stamp precheck only, use the first output SHA. |
| PC-9 | S1 / M1 | Fail the stamp push before acceptance, return only suffix A on recheck; require push-failed and independently observe exact ref absent. | At the failed-push recheck only, use the first output SHA. |
| PC-10 | S1 / M1 | Exact candidate B with suffix A and valid authority for A refuses remote-candidate-moved before writes. | At publisher candidate resolution only, use the first output SHA. |
| PC-11 | S1 / M1 | Lose a local tag-push response; exact peel A plus nested B must recover A. Nested-only A must refuse tag-push-failed; no new reservation. | At publisher ambiguous-push recovery only, use the first output SHA. |
| PC-12 | S1 / M1 | Successful local push; verification response has nested B then exact A. Publication succeeds and the independent bare reader sees A. | At publisher post-push verification only, use the first output SHA. |
| PC-13 | S1 / M1 | Valid promotion at A, nested pre-release peel B before exact A; require successful final at A. This is the S1 red-first row. | At promotion pre-release resolution only, use the first output SHA. |
| PC-14 | S1 / M1 | Exact live tip B plus suffix A while credit/pre-release are A refuses remote-candidate-moved. | At promotion live-head resolution only, use the first output SHA. |
| PC-15 | S1 / M1 | Nested-only final A is not a resume; create the absent exact final. With exact final C plus nested A, refuse final-tag-conflict. | At the existing-final read only, use the first output SHA. |
| PC-16 | S1 / M1 | Commit local push then return failure; exact A plus nested B recovers. A suffix-only apparent success after a failed push refuses tag-push-failed. | At promotion ambiguous-push recovery only, use the first output SHA. |
| PC-17 | S1 / M1 | Successful push followed by nested B then exact A passes; failed verification refuses tag-peel and does not record completion. | At promotion final verification only, use the first output SHA. |
| PC-18 | S2 / M2 | Empty journal, local remote finals .1 and .2 on 2026-10-15: proposal is v2026.10.15.3-rc.1. Repeat with annotated and lightweight finals. | Pass an explicit empty inventory into the publisher allocation call despite a nonempty successful remote inventory. |
| PC-19 | S2 / M2 | Add remote-only .3-rc.4; proposal is .3-rc.5. Include open N=2 with K=9 to prove greatest open N, not global K. | Drop canonical remote pre-release entries from occupancy. |
| PC-20 | S2 / M2 | Remote final .3 closes a journal-open .3-rc.4; finals .1/.2/.3 force .4-rc.1. | Do not add remote finals to the closed-N set. |
| PC-21 | S2 / M2 | Nested refs/tags/refs/tags/v... and non-CalVer tags do not occupy N; direct annotated ref and its peel occupy N only once. | Admit nested tag names by stripping the last refs/tags prefix. |
| PC-22 | S2 / M2 | Remote has .1; journal-only reservation .2-rc.7 requires .2-rc.8, with no fabricated published remote-only rows. | Build occupancy from remote inventory alone, dropping journal entries. |
| PC-23 | S2 / M2 | With no open N and finals .1/.3, long-lived proposal is .2-rc.1. | Choose max used N plus one instead of the smallest unused positive N. |
| PC-24 | S2 / M2 | Legacy final allocation skips remote finals and remote pre-release N as well as journal reservations; .1 final and .2-rc.4 yield .3. | For the legacy allocator only, ignore remote pre-release occupancy. |
| PC-25 | S2 / M2 | Both preview and real publisher attempts refuse remote-tag-inventory-failed on inventory exit 1, leave journal bytes unchanged, and return no proposed tag. | Convert a failed inventory read to a successful empty inventory at the publisher boundary. |
| PC-26 | S2 / M2 | Same candidate/full SHA after a failed push reuses exactly its reserved tag, even if a newer free name exists; exactly one reservation persists. | Allocate a fresh name instead of returning the existing reservation. |
| PC-27 | S2 / M2 | Same candidate id with a foreign full SHA refuses already-reserved conflict without journal mutation. | Remove only the existing candidate versus requested SHA comparison. |
| PC-28 | S2 / M2 | Same full SHA with a foreign candidate id refuses already-reserved conflict without journal mutation. | Remove only the existing reservation candidate-id comparison. |
| PC-29 | S2 / M2 | After inventory but before tag creation, create selected exact tag at C in the local fixture. Refuse tag-conflict before tag/push and retain the original reservation; never renumber. | Omit the selected exact-tag conflict precheck before the local tag write. |
| PC-30 | S2 / M2 | After inventory, create corresponding exact final at C; refuse final-tag-conflict before tag/push and retain the reservation. | Omit the corresponding-final precheck before pre-release creation. |
| PC-31 | S2 / M2 | -WhatIf returns the correct remotely occupied proposal while publications.json remains absent or byte-identical. | Call New-ReleaseGateTagReservation from the WhatIf branch. |
| PC-32 | S2 / M2 | The same valid preview leaves publication-authority.json absent or byte-identical. | Remove NoWrite forwarding to New-ReleaseGatePublicationAuthority. |
| PC-33 | S3 / M3 | Old line rc/2026-10-15; publisher clock 2026-10-19T23:59:59Z yields v2026.10.19.1-rc.1, including an equivalent instant supplied with an offset. | Use candidate.cutUtc for a new long-lived reservation. |
| PC-34 | S3 / M3 | Clock returns Oct 19 then Oct 20 on subsequent reads. One publisher attempt samples it once and binds authority/time/allocation to Oct 19. | Call Get-ReleaseGateUtcNow again for allocation after authority validation. |
| PC-35 | S3 / M3 | Persist first reservation before failed push, restart at Oct 20: same candidate reuses Oct 19 tag; newly qualified SHA uses Oct 20. | Recompute the existing reservation tag from the current attempt day on reuse. |
| PC-36 | S3 / M3 | Legacy stamp from Sep 23, clock Oct 19: publisher still proposes the Sep 23 final. | Use current UTC day unconditionally for legacy stamp allocation. |
| PC-37 | S3 / M3 | Before/after candidate journal retains its branch-derived candidate id and cutUtc; preview creates no reservation. | Persist the allocation date back into candidate.cutUtc. |
| PC-38 | S4 / M4 | Authority builder emits seven script keys including promote-release.ps1 and no branch tip; missing and wrong promote digest each refuse script-digest-mismatch:promote-release.ps1 after re-binding other digests. | Remove promote-release.ps1 from ReleaseAuthorityScripts. |
| PC-39 | S4 / M4 | Otherwise valid fresh promotion: wrong promote digest, missing authority, non-full ledger, diagnostic ledger, failed chunk, bad pinned policy and unavailable card readback all refuse their own token before local/remote writes. | Bypass the promotion pre-write gate refusal for the fresh-final branch. |
| PC-40 | S4 / M4 | Repeat those single-defect variants with an existing exact final at A; all refuse, leave journal unchanged and do not report Resumed. | Return same-final success before authority/publication-gate admission. |
| PC-41 | S4 / M4 | A supplied fixture/foreign Repository against fixture/release authority/origin refuses authority-repository; explicit fixture/release passes. Omission retains publisher default semantics and refuses this fixture's repository mismatch. | Ignore the supplied Repository and validate using the authority recorded repository. |
| PC-42 | S4 / M4 | Explicit owned API URL overrides a separate owned fallback API serving stale revision; valid explicit readback passes. Its unavailable and stale-revision variants refuse release-card-readback-unavailable and release-card-readback-revision. | Ignore the explicit AntiphonApiUrl and use the fallback address. |
| PC-43 | S5 / M5 | Valid green/authority/pre-release but missing publications.json refuses promotion-reservation-missing before writes; empty reservations does likewise. This is S5 red-first. | Allow missing publication journal/row as an empty successful reservation check. |
| PC-44 | S5 / M5 | Two otherwise identical matching rows refuse promotion-reservation-ambiguous; no first-row winner. | Select the first matching reservation without the count check. |
| PC-45 | S5 / M5 | Non-object row, array-wrapped candidate/SHA/tag, or nonboolean published refuses promotion-reservation-invalid. Start each from a fresh valid snapshot. | Coerce a string published value to bool instead of requiring a Boolean. |
| PC-46 | S5 / M5 | One row with foreign candidate but matching SHA/tag refuses promotion-reservation-candidate-mismatch. | Remove the reservation candidate-id equality predicate. |
| PC-47 | S5 / M5 | One row with foreign or abbreviated SHA refuses promotion-reservation-sha-mismatch. | Remove the reservation full-SHA/equality predicate. |
| PC-48 | S5 / M5 | One row with foreign or case-changed pre-release tag refuses promotion-reservation-tag-mismatch. | Remove the exact reserved pre-release-tag comparison. |
| PC-49 | S5 / M5 | A typed published=false row refuses promotion-reservation-unpublished. | Remove the published=true predicate. |
| PC-50 | S5 / M5 | Empty releaseId refuses promotion-reservation-release-identity. | Remove the nonempty releaseId predicate. |
| PC-51 | S5 / M5 | Empty releaseUrl refuses promotion-reservation-release-identity. | Remove the nonempty releaseUrl predicate. |
| PC-52 | S5 / M5 | Existing row.finalTag names a different N or a pre-release: promotion-reservation-final-conflict, preserving both remote tags and row. | Accept any nonempty existing finalTag as already recorded. |
| PC-53 | S5 / M5 | Repeat all reservation defects with an exact final at A: no success/Resumed and no journal mutation. | Place existing-final success before reservation validation. |
| PC-54 | S5 / M5 | Credit, live rc tip and pre-release peel are real A; checkout HEAD is distinct B. Independent bare-reader final peel is A and not B. | Change the local final tag target argument from peeled SHA to HEAD. |
| PC-55 | S5 / M5 | Independent git cat-file -t of the exact final object is tag, and its peeled commit is A. | Create a lightweight final by removing annotation and message arguments. |
| PC-56 | S5 / M5 | Exact final at C refuses final-tag-conflict; independent object ids/peels before and after are identical. | Treat any existing exact final as a same-SHA resume. |
| PC-57 | S5 / M5 | Direct pre-release or final exists without a supported peel (lightweight fixture or dropped peel response). Refuse tag-peel, never absence or overwrite. | Treat a present direct tag without peel as absent. |
| PC-58 | S5 / M5 | After a real push, remove the matching row at the last verification callback; the real setter's false result must yield nonzero promotion-journal-write-failed with neither Promoted nor Resumed. | Discard the final-recording false result and return success. |
| PC-59 | S5 / M5 | Post-push targeted atomic-writer exception yields promotion-journal-write-failed, preserves previous readable journal bytes, and reports known final A without completed promotion. | Catch the journal write exception and return ordinary success. |
| PC-60 | S5 / M5 | Writer silently stores unchanged bytes, or recording observes a replaced row identity: readback fails promotion-journal-write-failed; unrelated rows remain byte-equivalent. | Trust writer success without re-reading and matching candidate/SHA/pre-tag/finalTag. |
| PC-61 | S5 / M5 | After each journal fault restart from disk with fault removed: same final object/peel A, exactly one correctly recorded reservation, zero new tag/push writes. | Recreate/push the final unconditionally on a same-final recovery. |
| PC-62 | S5 / M5 | On fresh success, resume, conflicting final and journal failure, independent pre-release object/peel and GitHub fixture pre-release remain unchanged. | Delete the fixture pre-release ref after final creation. |
| PC-63 | S5 / M5 | Inject final C between last absence read and push: push refuses, exact reread proves conflict, no force/plus/delete command, neither tag moves. Fail-before-write and commit-then-lose-response variants restart to the same identity. | Add --force to the final tag push, allowing a concurrent exact final to be replaced. |
| PC-64 | S1 / M1 | E recipe's inert launcher: a sibling path outside the fixture root and an escaping link each refuse before launch; recorded process launches=0. No transport executes. | Remove the fixture adapter's resolved-path containment check. |
| PC-65 | S1 / M1 | E recipe's inert launcher: network URL, unknown subcommand and unhandled transport form each refuse before launch; recorded process launches=0. No transport executes. | Replace the adapter's default refusal with forwarding to the inert launcher. |
| PC-66 | S5 / M5 | After final push, replace the admitted row with a foreign SHA at the last verification callback. The real recording helper refuses promotion-journal-write-failed and leaves replacement row bytes unchanged; it must not record finalTag on that row. | Remove the recording helper's repeated SHA/identity validation, retaining its candidate-id lookup. |
| PC-67 | S2 / M2 | A recipe: open N=2/K=9 and N=3/K=4, no final for either; next is .3-rc.5, independent of row order. | Choose the smallest open N instead of the greatest open N. |
| PC-68 | S2 / M2 | A recipe: open N=3/K=2 and N=3/K=4, both row orders; next is .3-rc.5. | Keep the first K seen for an equal N instead of the greatest K. |
| PC-69 | S2 / M2 | After real reservation beside remote-only finals .1/.2, journal contains only the new candidate row and no published rows representing those finals. | Append published journal rows for the remote-only final tags while forming the reservation journal. |
| PC-70 | S2 / M2 | Invoke production proposal and reservation without the inventory argument, all other arguments valid, in a bounded noninteractive fixture child: require argument refusal, no proposal and unchanged journal. Explicit empty inventory remains valid. | Give the required inventory argument an optional empty default, admitting journal-only allocation. |

Run **every** listed PC after land: baseline green, break, exact-assertion red,
restore exact source bytes (refresh timestamps), then green. Each phase is scoped
to `/*/*/ReleaseTagAllocatorTests/<exact M method>` with MinExecuted=1, separate
isolated output and external evidence paths. Follow the local inherited
SourceLanding driver procedure in the testing owner; no external executor and
no commits from that snapshot. Record each PC's payload hash, assertion failure,
restoration and final green; no shared class/suite mutation runs. Code runs only
the ordinary checkpoint table. Review judges this pending design before land;
executed PCs are not a pre-land gate. No PC ran in this TestDesign task.

### Out of scope

- All real-remote tag/release operations, live GitHub/card writes, production release qualification, notifications, queue/session delivery, browser/AppHost, schedules, deployments and the rc backport. They are neither required nor authorized by these fixture proofs.
- Full assembly, Unit-wide selection and full release harness: this brief explicitly sets five bounded slice selections. Unchanged coordinator, asset-upload, registration, exhaustive authority-schema, and transport/authentication coverage remain with existing owners.
- Distributed locking across independent release roots/external publishers and hardware power-loss atomicity. Races here prove fail-closed create-only collision handling, not a global allocator lock.
- Actual PC execution in TestDesign/Code/ordinary Review. The post-land companion must execute every bound control; pending is never represented as passed.

### Checkpoints

Closed ordinary scope: **one red-first row and one narrow green row per slice**,
no additional test groups. Red rows run once at the committed/pushed test-only
witness revision before that slice's fix; the witness wrapper initially contains
only its decisive assertion (1 named harness row). Expand to the full planned
roster with the fix. A red receipt is intentionally failed, never called green;
the checkpoint tool must report executed=1, failed=1, skipped=0 with the named
behavioral assertion. Exit 2/3/4, missing TRX, compilation/fixture error or zero
execution is incomplete. Do not change the runner to turn expected red into success.

Bootstrap the checkpoint tool once through the host gate, then run one selected
row at a time using its own build-slot acquisition:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1339-tool-build -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-card1339-tool/
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-card1339-tool/ -- run --plan docs/superpowers/plans/2026-10-19-card-1339-release-tag-allocator-plan.md --rows CP-1
```

After bootstrap, substitute only the row number in the no-build tool command.
Do not hold an outer build slot while the tool waits for its own row slots.
If wait returns 75, keep waiting for that run id;
never end with a child in flight. Do not run historical red rows at the final fixed
tip. Code reports their test-only SHA, then the green row's fixed SHA. Preserve
unaltered CHECKPOINT lines and full TRX roster. One build per row, outputs owned
and removed after receipt retention; evidence stays ignored. Tool/bootstrap build
also uses the slot and alternate output. Slot timeout is not permission to bypass.
At most the optional S1 red proof could have run in TestDesign; it did not, so Code
still owes that row. Review consumes red history and verifies fixed-slice receipts.

The combined filters use separate parenthesized wildcard operands at both class
and method segments (CARD-0403), escaped pipes for Markdown. Their Cartesian
intersection must yield exactly the enumerated methods; unexpected extra methods
are a roster defect. Do not use discovery counts or internal harness row counts
as MinExecuted. The five green rosters total **12 TUnit executions, 11 distinct
methods** (V-2 is deliberately run again for S3).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1339-s1-red/` | s1-witness | `/*/*/ReleaseTagAllocatorTests/C1339_ExactRemoteRefs` | V-1, R-1 red witness | M1: exactly 1 executed, 1 failed at C1339 G-13 promotion-pre-read, 0 skipped | 1 | 4 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1339-s1-green/` | s1-green | `/*/*/(ReleaseTagAllocatorTests*)\|(ReleaseGatePublicationTests*)\|(ReleaseBranchModelGateTests*)/(C1339_ExactRemoteRefs*)\|(C599_CandidateIdentity*)\|(C1311_LongLivedRcRef*)` | V-1, R-1 | M1, C599_CandidateIdentity, C1311_LongLivedRcRef: exactly 3 executed, 0 failed/skipped; 42 harness rows | 3 | 5 |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c1339-s2-red/` | s2-witness | `/*/*/ReleaseTagAllocatorTests/C1339_RemoteTagAllocation` | V-2, R-2 red witness | M2: exactly 1 executed, 1 failed at C1339 G-18 remote-finals, 0 skipped | 1 | 4 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1339-s2-green/` | s2-green | `/*/*/(ReleaseTagAllocatorTests*)\|(ReleaseGatePublicationTests*)/(C1339_RemoteTagAllocation*)\|(C599_TagRecovery*)` | V-2, R-2 | M2, C599_TagRecovery: exactly 2 executed, 0 failed/skipped; 31 harness rows | 2 | 4 |
| CP-5 | S3-red | `tests/Antiphon.Tests -> bin-c1339-s3-red/` | s3-witness | `/*/*/ReleaseTagAllocatorTests/C1339_AllocationUtcDay` | V-3, R-3 red witness | M3: exactly 1 executed, 1 failed at C1339 G-33 utc-day, 0 skipped | 1 | 4 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1339-s3-green/` | s3-green | `/*/*/ReleaseTagAllocatorTests/(C1339_AllocationUtcDay*)\|(C1339_RemoteTagAllocation*)` | V-3, R-3, V-2, R-2 | M3, M2: exactly 2 executed, 0 failed/skipped; 24 harness rows | 2 | 4 |
| CP-7 | S4-red | `tests/Antiphon.Tests -> bin-c1339-s4-red/` | s4-witness | `/*/*/ReleaseTagAllocatorTests/C1339_PromoteAuthorityHash` | V-4, R-4 red witness | M4: exactly 1 executed, 1 failed at C1339 G-38 promotion-hash-required, 0 skipped | 1 | 4 |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c1339-s4-green/` | s4-green | `/*/*/(ReleaseTagAllocatorTests*)\|(ReleaseGatePublicationAuthorityTests*)\|(ReleaseBranchModelGateTests*)/(C1339_PromoteAuthorityHash*)\|(C599A_ExecutionLedgerJoins*)\|(C1311_MovingTipAuthority*)` | V-4, R-4 | M4, C599A_ExecutionLedgerJoins, C1311_MovingTipAuthority: exactly 3 executed, 0 failed/skipped; 19 harness rows | 3 | 6 |
| CP-9 | S5-red | `tests/Antiphon.Tests -> bin-c1339-s5-red/` | s5-witness | `/*/*/ReleaseTagAllocatorTests/C1339_PromoteReservationAndTarget` | V-5, R-5 red witness | M5: exactly 1 executed, 1 failed at C1339 G-43 reservation-journal, 0 skipped | 1 | 4 |
| CP-10 | S5 | `tests/Antiphon.Tests -> bin-c1339-s5-green/` | s5-green | `/*/*/(ReleaseTagAllocatorTests*)\|(ReleaseBranchModelGateTests*)/(C1339_PromoteReservationAndTarget*)\|(C1311_PreReleaseTag*)` | V-5, R-5 | M5, C1311_PreReleaseTag: exactly 2 executed, 0 failed/skipped; 30 harness rows | 2 | 5 |

### Cost

All estimates are wall-clock minutes, not measured execution. Design performed
zero test/build runs. Ordinary V/R floor (Code) = **44 minutes**: CP-1/3/5/7/9
are 4 each (20); CP-2/4/6/8/10 are 5/4/4/6/5 (24). These exact filters are named
above. The 44 includes ten isolated row builds at an estimated 2 minutes each
(20 build + 24 execution/evidence minutes). Add 1 minute tool setup/bootstrap:
**45 minutes ordinary verification including setup**, excluding authoring and
host-slot waiting. A genuine failed green row adds its measured rerun cost.

PC floor (Mutation): **70 controls**, all the exact M1..M5 method filters above,
not the combined green filters. Per PC budget: baseline build/run 3, defect
build/red 3, restore/verify bytes 0.25, restored build/green 3 = **9.25 minutes**.
Thus Mutation floor = **647.5 minutes** plus 1 minute snapshot/driver setup =
**648.5 minutes**. This includes three isolated builds per PC at 2 minutes
each; there is no hidden broad suite or assumed parallel saving. Combined
setup/build + ordinary V/R + every PC baseline/red/restore/green =
**45 + 648.5 = 693.5 estimated minutes**, before authoring, slot waits or
failure-driven repair. Bind and measure per-method costs at Code; retain this
estimate as history rather than silently reducing scope if slower.

Estimated batching saving: five green builds instead of one build for each of
12 green method executions saves **7 builds x 2 = 14 minutes** with the same
roster. This is a comparison, not a measured product saving. Red witnesses and
PCs are not removed or merged to claim savings. Measured saving: **0 minutes**.

Handoff audit: **guards=70, mapped=70, missing=0, duplicate PC maps=0**.
All 70 PCs are required to run in the post-land Mutation phase; executed here=0,
pending=70, payload binding required=70. No pending PC is claimed green.
Verification design is complete; next is Code for the five fixture-only slices,
then ordinary Review, land, and all bound PCs. An unbindable behavioral seam goes
back to Plan; a changed human scope choice goes to Decide.
