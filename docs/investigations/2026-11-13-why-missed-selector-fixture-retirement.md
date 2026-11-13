# Why missed: selector fixture repositories block temp retirement

## Introducing commit/card

The fixture producer was introduced in master commit
7a67b659f507750932eb0b76759b27bab763bc8c (CARD-1820 S1R1, Code f1903e95).
ReleaseImpactEnvelopeTests put independent no-remote Git repositories under
the checkout's ignored .antiphon/selector-evidence/f1903e95/fixtures directory.
The retirement audit independently treated every discovered repository as protected
Git work. Its inventory queried origin before the selector fixture could be classified,
so missing origin returned RecycleGitAuditUnknown instead of reaching a Ready proof.

## Approving reviews

Review 40a75a5a-9664-4a56-8e67-015e9c5e5aea was Final/Full for S1R1,
subject f14441056e000e2421f87e2b1ff30746313569b2. Its committed report on
origin/feat/card-task-40a75a5a records ReleaseImpactEnvelopeTests (4),
ImpactedClassesSelectorTests (42), classification/mechanical/platform/layering guards
(78), derived-index drift (1), and a separate ProcessSpawnLimitTests run (57).
It explicitly recorded accumulated fixtures as advisory A-3, subsequently CARD-1866.
Those checks established selector behavior, not the effect on retirement's repository census.
This note does not infer any unrecorded approval scope for earlier rollout repairs.

## Outside the selection

The missing case crossed two Unit script fixtures: the selector producer created
Git state under ignored evidence, while the retirement tests used empty/published
volumes, the exact CARD-1605 template, and deliberately protected nested repos.
Earlier dry-run/preview witnesses tested absent cleanup, tooling/history binding,
token/refusal rules and repository publication independently. None replayed the
selector-evidence/no-remote layout into a retirement preview. Ignored evidence
under a *Git admin directory* is already intentionally stricter and remains so.

## Cheapest pre-land check

`scripts/c590-remote.sh,tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs`
-> `/*/*/VolumeRetirementContentTests/Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent`

The argument-expanded witness covers ignored scratch, linked parents, outside scratch,
configured remote, missing ignore, dirty parent and unpublished parent. A cheap
producer witness also asserts the fixture is under Path.GetTempPath and outside
the real checkout, preventing reintroduction of the hard-coded task path.

## Repair and delivery

Only scripts/c590-remote.sh changes production behavior. In the V2 temp audit,
an ordinary nested .git directory with no remote configuration is omitted from
its own publication census only under .antiphon/selector-evidence when both that
root and the fixture path are ignored by the enclosing checkout. The enclosing
checkout must be independently enumerated and still passes the full existing
content, admin, tip/reflog and publication checks before Ready. Other scratch roots,
including a selector fixture inside a known disposable no-origin seed (which cannot
provide publication proof), keep their refusal. Enclosing origin is required.
Other scratch roots,
Git admin evidence, remote-bearing repos and all existing apply/token/expiry rules
keep their previous treatment. New selector fixtures use a unique system temp path.

The Deploy can use a canonical checkout containing this reviewed/landed fix while
keeping -Sha ab7af12c699039270af7e22a5d7fd794035bc325 for gate 9. The deploy
wrapper passes that supplied SHA into manifests and cleanup/source receipts; it
does not compare retirement tooling HEAD to it. The canonical-source HEAD equality
check in Invoke-HostJq is only for provisioning jq, not retirement's normal check.
c590-real.ps1 stages a private immutable bundle of c590-remote.sh,
c1008-retirement.sh, c1008-content-audit.sh and c1008-volume-metadata.sh from its
own script directory. The current-runtime descriptor and tooling hashes are separate
from creating SHA. Use a fresh preview and its matching enabled apply token; do not
reuse a proof created by the old tooling. Moving the tooling checkout is not an
activation or restart, and this Debug task moves no checkout or live service.

At base 90ae19f65, the other scripts different from ab7af12c6 are:
scripts/impacted-classes.ps1, scripts/lib/SelectorCensus.cs,
scripts/lib/nightly-tests-impl.ps1, scripts/lib/rc-boundary.ps1,
scripts/lib/rc-forced-full.ps1,
scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj,
scripts/regenerate-derived-impact-index/Program.cs, scripts/selector-replay-host.ps1,
scripts/test-rc-boundary-pack-real-tree.ps1, scripts/test-rc-boundary-pack.ps1,
scripts/test-rc-boundary.ps1, scripts/test-rc-forced-full.ps1,
scripts/test-selector-census.ps1. They are also picked up by moving the whole checkout;
none changes the gate-9 wrapper or its staged audit helper dependencies.
Recheck this list against the actual fixed master commit after landing.

ReleaseImpactEnvelopeTests.cs overlaps CARD-1820's in-flight S1R6 area. The producer
edit is limited to its fixture path/comment plus one location witness; no selector
assertions, behavior or algorithms are rewritten.

## Exemption

None; this is the first note for this cause.
