# Unbound cache Fixture ownership mutation candidate

Owner Code task: bad91a49. Bind to the confirmed SourceLanding operation after
ordinary Review and publication. No deliberate mutant is executed by Code.

| ID / variant | Production defect | Exact method | Required red |
|---|---|---|---|
| PC-1 / root-verifier | Run volume npm verification as UID 0 again | `/*/*/CacheSeedNpmOwnershipTests/Volume_seed_verifies_as_writer_and_removes_npm_logs` | npm owner, cleanup and acceptance assertions reject root logs |
| PC-2 / ignored-verifier-exit | Ignore volume npm verifier nonzero exit | `/*/*/CacheSeedNpmOwnershipTests/Volume_seed_failure_cleans_stage_and_never_accepts` | verify argument refuses false acceptance |
| PC-3 / ignored-cleanup-exit | Ignore npm-cache cleanup nonzero exit | `/*/*/CacheSeedNpmOwnershipTests/Volume_seed_failure_cleans_stage_and_never_accepts` | cleanup argument refuses false acceptance |

Every PC/variant is pending. Mutation owns method-scoped red/restore/green,
missing-control discovery and restoration evidence. Native Linux UID semantics
and real npm are required; fixture/build failures and zero tests are not red.
