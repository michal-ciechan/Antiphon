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
