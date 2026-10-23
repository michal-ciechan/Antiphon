# CARD-1483 S4 unbound Mutation candidates

Original Code/landing owner: 6e80fc7e-008b-4c02-af99-8b6ff3ef8dc1.
Every row is pending. Mutation owns deliberate defects, missing-control discovery,
and method-scoped red/restore/green after ordinary Review and SourceLanding.

| ID | Deliberate defect / variants | Exact method in LandGraphEvaluationTests |
|---|---|---|
| PC-S4-1 | Omit recording; omit absent paths; omit directories; omit real-path resolution (separate variants) | C1483_S4_ExistsProbeOfDocsRefuses |
| PC-S4-2 | Skip canary evaluation or discard differential verdict | C1483_S4_FileReadingPropertyFunctionRefuses |
| PC-S4-3 | Treat canary evaluation exceptions as approval | C1483_S4_FileReadingPropertyFunctionRefuses |
| PC-S4-4 | Omit property comparison; omit documentation byte replacement; omit README deletion (separate variants) | C1483_S4_FileReadingPropertyFunctionRefuses |

Use `/*/*/LandGraphEvaluationTests/<exact method>` per cycle. Discovery must cover
item-only and import-only differences, canary addition, ignored Markdown, directory
and absolute links, recording enumeration/read variants and over-broad path normalization.
All inherited S1 controls, PC-S2-1a/1b/1c/2a/2b/3a/3b/3c, and PC-S3-1..7 with their
listed variants remain pending; original exit-2-as-approval controls include S5.
No Code run here discharges any PC.

PC-S4-5 pending: broaden the FileMatcher traversal exception to explicit Conditions
or to a directory path whitelist. Exact guard: C1483_S4_ExistsProbeOfDocsRefuses,
which combines the allowed JSON glob with each unsafe explicit probe. Discovery must
also test absent call provenance, Condition-triggered glob expansion and SDK matcher
changes. PC-S4-6 pending: put the canary under an unrelated parent (V-13 real tree).
