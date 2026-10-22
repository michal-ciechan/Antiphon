# CARD-1483 S2 unbound Mutation candidates

Original Code/landing owner: 1757a065-8fcc-424a-8ac6-0b51020e6995.
Plan: [S2 amendment](../plans/2026-10-22-card-1483-s2-amendment.md).
All rows are **pending**, with no deliberate mutant run by Code. Bind to the actual
SourceLanding SHA after ordinary Review and publication. Discovery remains required.

| Candidate | Guard/variant | Exact method in LandGraphEvaluationTests |
|---|---|---|
| PC-S2-1a | Omit future-glob reachability, including composite includes | C1483_S2_GlobReachabilityRefuses |
| PC-S2-1b | Treat wildcard extensions or no-extension filenames as safe | C1483_S2_GlobReachabilityRefuses |
| PC-S2-1c | Allow Exclude to discharge future reachability | C1483_S2_GlobReachabilityRefuses |
| PC-S2-2a | Omit recursive evaluated references | C1483_S2_SolutionClosureScopesProjects |
| PC-S2-2b | Omit not-built inventory, or evaluate every tracked project regardless of solution closure | C1483_S2_SolutionClosureScopesProjects |
| PC-S2-3a | Trust untracked/non-definition/docs imports | C1483_S2_ImportOutsideTrackedSetRefuses |
| PC-S2-3b | Trust wildcard imports, including currently empty ones | C1483_S2_ImportOutsideTrackedSetRefuses |
| PC-S2-3c | Trust a tracked alias whose real target is untracked | C1483_S2_ImportOutsideTrackedSetRefuses |

Use only method-scoped filters `/*/*/LandGraphEvaluationTests/<exact method>` for
red/restore/green. Reference global-property metadata, package-root configuration,
and links encountered below a glob's wildcard portion need discovery as well;
the ordinary matrix does not individually prove those branches.
The narrow SDK ImportAfter/workload-manifest exceptions also need negative-control
discovery (spoofed importer/SDK name, outside target and redirected SDK wildcard).

Inherited plan controls also remain pending: remove target pins comparison
(V-8/V-9), omit differential evaluation (V-12), accept exit 2 (V-3/V-14), and the
S1 item/solution-type/link checks and S1 re-cut attribute/value pins. S3-S5 are not
implemented by S2, so their tests and controls cannot be claimed passed here.
