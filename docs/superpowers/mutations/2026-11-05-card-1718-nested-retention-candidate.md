# CARD-1718 B-1r2 — unbound Mutation candidate

Every row/variant is PENDING for method-scoped SourceLanding Mutation after round 3 Review
and landing of original Code owner `5e05fc80-930f-4f03-8c71-6c323d552d63`. Code used historical
production bytes for red witnesses; it performed no deliberate mutations. Mutation owns exact
edits, assertion-red/restore/green and missing-control discovery. No pack is bound here.

Inherited obligations remain PENDING in
[the prior candidate](2026-11-05-card-1718-d3-r2-candidate.md): historical M1/M2, PEM-removal,
protected-after-disposable; PC-D3-1 admission, PC-D3-2 locked, PC-D3-3 phase-B,
PC-D3-4 recovery-DryRun/recovery-Execute, PC-D3-5 unreadable-list, PC-D3-6 runner-floor,
PC-D3-7 credentials-descendants, PC-D3-8 PEM-descendants, PC-D3-9 appsettings,
PC-D3-10 initial-transport/final-transport, PC-D3-11 active-recipient-replay. The separately
owned 68 S4a controls and extra mutants retain their previous obligations.

| ID / variants | Deliberate defect | Exact method witness | State |
|---|---|---|---|
| PC-N1 / drop-walk | Omit collapsed-directory inspection | C1677RetentionEngineTests.Ignored_nested_repository_secret_keeps_bytes_and_registration | PENDING (both secret cases) |
| PC-N2 / follow-file-link, follow-directory-link | Follow links or skip them instead of keeping the tree | C1677RetentionEngineTests.Doubt_inside_collapsed_ignored_repository_keeps_the_tree | PENDING (both link variants) |
| PC-N3 / unreadable-as-clean | Ignore inaccessible descendants or treat enumeration failure as clean | C1677RetentionEngineTests.Doubt_inside_collapsed_ignored_repository_keeps_the_tree | PENDING |
| PC-N4 / control-name | Ignore control characters in collapsed descendants | C1677RetentionEngineTests.Doubt_inside_collapsed_ignored_repository_keeps_the_tree | PENDING |
| PC-N5 / recipient-drop-walk | Bypass the walk through real transport | C1677RetentionServerTests.Ignored_nested_repository_secret_survives_real_transport | PENDING |

Use exact method filters `/*/*/ClassName/ExactTestMethod`, not whole-class selections. Restore
each exact production path with `git checkout -- <exact path>`. A zero count, fixture/build
failure, or surviving mutation is not assertion-red. Never loosen an assertion or deadline.
