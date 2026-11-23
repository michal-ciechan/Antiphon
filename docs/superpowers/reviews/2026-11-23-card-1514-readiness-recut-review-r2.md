# CARD-1514 readiness re-cut, Final Review round 2

**Return to Code.** The candidate drops two readiness timing labels. Two inherited Windows-only Grok skips also prevent the Linux receipt from qualifying. The complete Linux selection ran at `eefad7d1802defd9043980ca9e985438f50c3291`, the same SHA as round 1. The Code landing owner is `84561585-b264-4731-afe4-2d7dfdb1fadd`.

| Where | Failure | Why | Fix |
|---|---|---|---|
| CP-12, `ProjectReadinessCollationTests.cs` lines 39 and 49 | `TestTimingOperationLabelTests.C1620_EveryCallSiteOperationLabelIsKept` failed on the candidate and passed on base `e298e6fbb5c9125e3144e02b6f9a8432a229a3bc`. Commit `d9cee2c5e39b2419c4e1fded6e3f96e2aa3448bd` introduced the two call sites. | The recorder writes `omitted` for both the C-locale database creation and migration operations. | Add the two static literals `C-locale readiness database` and `C-locale readiness database tip` to `TestTimingSession.OperationLabels` in `tests/Shared/TestTiming.cs`, or reuse existing appropriate labels at the call sites. Preserve the recorder’s default for unknown values. |
| CP-10, `GrokRulesLaunchRefusalTests.AssertNamedGrokRefused` at line 102 | The Herdr and PtyHost methods both skip on Linux for the Windows `cmd.exe` fixture. The frozen `WindowsUnitSkipRoster.WindowsOnly` lacks both entries. Base replay reproduced the same three passes and two skips. | The placement check rejects these TRX identities with `skip_identity_unlisted`. | Register each exact Windows-only method with count one and its placement reason, then qualify the repaired receipt. The methods are `Named_grok_herdr_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists` and `Named_grok_pty_host_agent_with_unsafe_raw_rules_is_refused_before_any_session_exists`. |

| Fresh Linux selection | Passed | Failed | Skipped |
|---|---:|---:|---:|
| CP-10 — six writer-fixture callers | 77 | 0 | 2 |
| CP-11 — five generated-index consumers | 58 | 0 | 0 |
| CP-12 — six further candidate-base classes | 103 | 1 | 7 |
| CP-13 — Antiphon.Agents.Pty.Tests process limiter | 2 | 0 | 0 |
| CP-14 — Antiphon.SessionRunner.Tests process limiter | 2 | 0 | 0 |
| **Total** | **242** | **1** | **9** |

Run `20261123-060031-928b` covered 17 distinct classes across 19 project/class selections in 16m19s. All 252 TRX identities matched the declared methods and parameter rows. The seven CP-12 skips match rostered Windows cases, including all three junction cases in `CardFilePrivacyPathTests`. The three builds and five rows held slots and the receipts recorded verified build provenance. The checkpoint-tool bootstrap and the evidence-diff launcher each had a separate build lease. The overall validator returned `report_failed`.

Round 1 run `20261123-053028-07ea` revalidated at the frozen SHA after restoring its nine original TRXs to their recorded paths. CP-1 through CP-7 match the committed table. CP-8 and CP-9 repeat CP-6. Together they recorded 841 passes, zero failures and 11 roster-qualified skips. The two pre-fix writer failures and three subsequent green writer/reader runs also matched the archived TRX hashes. All PCs remain pending for SourceLanding Mutation.

The base replay took 10m17s. Its exact-SHA validator accepted the timing row and rejected the Grok skips with `skip_identity_unlisted`.

The migration/downgrade selector matched zero rows. Evidence-diff found zero entries and zero violations in 19 candidate commits. Merge-tree against master `9eaff7acbf7bb111aefa636830aadb0e04aa994a` produced `638d0e3cd06eeb693c476e7aa5945bbb0e96a8d1` without conflicts.

The unedited checkpoint lines, receipts and all five candidate TRXs and two base TRXs are under `/work/review-evidence/5bfb258d/`, in `20261123-060031-928b/` and `20261123-061416-4715/`. The prior nine are under `/work/review-evidence/23b2fe07/20261123-053028-07ea/`.
