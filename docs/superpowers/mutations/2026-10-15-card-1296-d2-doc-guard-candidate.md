# CARD-1296 unbound Mutation candidate

Original Code/landing owner: a8d016eb-6236-4b0e-b845-8af6fa563ee5.
Implementation SHA: c78599926845853177365a75670d60850421b064.
Regression-selection repair SHA: 41f4942073f41295e38a4e52866e41d5751581c1.
Bind this candidate to the confirmed landing SHA after ordinary Review and landing.
No deliberate source mutant or PC cycle was executed by Code. Fixture negatives
exercise the same assertion but do not discharge source controls.

All controls below remain pending; each source edit must produce the named
assertion failure, then be restored and the identical method run green.

| PC | Source and variants | Exact detecting filter | Expected red |
|---|---|---|---|
| PC-1 | docs/orchestration-loop.md: remove the D-2 section; separately rename its heading | `/*/*/D2AdvisoryDocumentationTests/DelegatePointerResolvesToTheD2PolicySection` | D-2 section heading must resolve |
| PC-2 | server/Bundles/delegate-basics.md: remove the pointer; separately change its path; separately change its section name | `/*/*/D2AdvisoryDocumentationTests/DelegatePointerResolvesToTheD2PolicySection` | missing pointer, D-2 pointer owner path, or D-2 pointer section name respectively |
| PC-3 | docs/orchestration-loop.md: restore the dangling checkpoint sentence | `/*/*/D2AdvisoryDocumentationTests/CheckpointObligationsNameTheirStageContracts` | relocated checkpoint obligations must identify their source |

Mutation owns missing-control discovery, including independently removable
section-local policy clauses. No stage bundle changes or launch-attachment changes
are part of this task. Keep every cycle method-scoped on the bound snapshot.
