# CARD-1313 S6-A unbound Mutation candidate

Original Code and landing owner: c13c1444-57fc-4995-812a-73dc9ada62b1.
Branch: feat/card-task-c13c1444. Target: master only. Restart: none.
Plan: [S6-S7](../plans/2026-10-19-card-1313-s6-s7-plan.md).
Design: [verification](../plans/2026-10-19-card-1313-s6-s7-test-design.md).

All rows and every listed variant remain **pending**. No deliberate mutant ran.
Ordinary red-first failures are not Mutation controls. Mutation binds method-scoped
patches and landed source after ordinary Review and caller landing, discovers missing
controls, then performs each red/restore/fresh-green cycle under SourceLanding custody.

This slice has 21 PC IDs, 31 portable cycles and 9 Windows cycles in the design.
PC-59's six edges and PC-91's two modes retain native Windows obligations; PC-92
is Windows-only. Linux CP-3 never discharges CP-21 or those controls. The rest of
S6/S7 (including PC-50) and every earlier S1-S5 pending control remain outstanding.
BR-1/BR-2 still block the production supplier and nine-suite producer claims.

Implementation sites: scripts/lib/release-selection.ps1 (attempt, baseline, transport,
Full roster); scripts/lib/release-gate.ps1 (legacy Full credit); release-cut.ps1,
nightly-run.ps1, nightly-tests.ps1 and their implementations (individual parameter edges).
The fixture uses synthetic archived publication documents solely for reader validation.
Only the staged FilterHost executions are native test evidence; seam observations of
the nine-suite command boundary are diagnostic and cannot publish.

| PC | Break / compiling mutation | Variant cases (each a cycle) | Detecting filter | Expected red assertion / oracle |
|---|---|---|---|---|
| PC-49 | G-49: feed the proposed selected roster into Shadow execution | narrow-provider | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | `shadow-full` — V-1 outcome must remain as specified above. |
| PC-51 | G-51: admit a selected publication as the Full baseline | selected-baseline | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-full-only` — V-2 outcome must remain as specified above. |
| PC-52 | G-52: leave effective mode ChangeAware when no baseline is resolved | absent-receipt | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-absent-full` — V-2 outcome must remain as specified above. |
| PC-53 | G-53: convert baseline digest mismatch into the missing-baseline fallback | digest-mismatch | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-corrupt-refused` — V-2 outcome must remain as specified above. |
| PC-54 | G-54: skip the selector-contract comparison while leaving all other baseline joins enabled | changed-selector | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `contract-hash-bound` — V-2 outcome must remain as specified above. |
| PC-59 | G-59: drop only BaselineSha from one forwarding edge; keep all other fields intact | cut-run,wrapper-impl,impl-core,self-reexec,core-tests,tests-impl | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `hop-baseline-bound` — V-3 outcome must remain as specified above. |
| PC-80 | G-80: write an active qualification as a side effect of selection | self-activation | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | `qualification-root-unchanged` — V-1 outcome must remain as specified above. |
| PC-81 | G-81: change the omitted-mode default to ChangeAware with valid fixture qualification | omitted-mode | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | `default-full` — V-1 outcome must remain as specified above. |
| PC-82 | G-82: discard the selector fallback and retain the narrow union | unknown-path,authority-path,build-path | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | `selector-fallback-full` — V-1 outcome must remain as specified above. |
| PC-83 | G-83: catch explicit-object resolution failure as ordinary absent-baseline fallback | missing-A,missing-B | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `git-object-refused` — V-2 outcome must remain as specified above. |
| PC-84 | G-84: bypass the merge-base ancestry refusal | unrelated-B | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-ancestor-required` — V-2 outcome must remain as specified above. |
| PC-85 | G-85: retain reduced mode after a verified but incompatible baseline | changed-policy,changed-runtime | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-incompatible-full` — V-2 outcome must remain as specified above. |
| PC-86 | G-86: apply candidate-A live card/tip checks to archived B | card-advanced,tip-advanced | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `historical-baseline-accepted` — V-2 outcome must remain as specified above. |
| PC-87 | G-87: overwrite a conflicting existing attempt rather than refuse | changed-selection-digest | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `attempt-conflict-refused` — V-3 outcome must remain as specified above. |
| PC-88 | G-88: reuse the previous run's attempt directory for a new runId | new-run | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `prior-attempt-kept` — V-3 outcome must remain as specified above. |
| PC-89 | G-89: bypass selection-file digest comparison at the receiving implementation | edited-file | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `hop-digest-refused` — V-3 outcome must remain as specified above. |
| PC-90 | G-90: bypass the received A versus candidate comparison | foreign-A | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `hop-sha-refused` — V-3 outcome must remain as specified above. |
| PC-91 | G-91: continue non-Full through the parent when child pwsh resolution fails | Shadow,ChangeAware | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `pwsh-required-before-work` — V-3 outcome must remain as specified above. |
| PC-92 | G-92: replace the file/JSON transport with a legal 5.1 unquoted space-splitting argument join | spaces-unicode | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `windows-path-roundtrip` — V-3 outcome must remain as specified above. |
| PC-150 | G-150: accept an archived green ledger with no confirmed publication digest | green-without-publication | `/*/*/ChangeAwareReleaseTests/C1313_BaselineAuthority` | `baseline-publication-required` — V-2 outcome must remain as specified above. |
| PC-151 | G-151: place selection-attempt.json under the disposable checkout output | cleanup-owned-output | `/*/*/ChangeAwareReleaseTests/C1313_ParameterHops` | `attempt-survives-cleanup` — V-3 outcome must remain as specified above. |

Status for every table variant: pending, unbound, reachability not yet certified by Mutation.
