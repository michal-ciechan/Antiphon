# CARD-1814 Code report

CARD-1814 worked. Five Linux-skipped DPAPI methods in DelegateScriptCapabilityTests are now a method-scoped platform:linux exclusion, the witness is green, and both mutants went red and were restored.

Red `1b9dc9918a0f5d6ba4e8cdd2ed2d1d448e148352`. Fix and tested `7473ef10b037e767b7a0da8778fcd279c08144b9` on `feat/card-task-3a284cc6`, worktree `/work/worktrees/task-3a284cc6`, Code task `3a284cc6`. Base `364e271331fc1af83f5b7eabaa956a8c0a289a5f`. The tested tree matches that fix tip. This report file is a later commit.

Five methods call WriteBlob, which Skip.Test's off Windows before ProtectedData.Protect:

- `delegate_Capability_sends_header_and_does_not_assign_env_token`
- `delegate_ANTIPHON_CAPABILITY_name_sends_header`
- `capability_and_task_token_together_is_refused_before_request`
- `auto_load_of_the_only_store_file_is_forbidden`
- `error_paths_never_contain_the_token`

Owner CARD-1814. The reason does not use the Linux-only-by-construction prefix. `missing_capability_file_does_not_advise_AllowedRoots` never reads a blob and stays required. The review text said six tests; the sixth method is that portable sibling, so the row is method-scoped. No other test class calls ProtectedData. `scripts/delegate.ps1` and `scripts/capability.ps1` are unchanged, so an OS-branch witness is not required. Windows-native execution of those five bodies, and of `CapabilityScriptTests.issue_does_not_print_the_token_and_dpapi_round_trips`, stays owed.

Pins resealed together.

- policyHash `9cf98d229e32ca52e76d6b8946856776c7c390e3027e2eae191f5042aca3b697` -> `70cb2f24a7c124f1ee6962ebb6d3dcbd37a84888d3f8940bb21b5d5e097f8898`
- PinnedInputDigest `b587b96f62c5d652f2de4db1488fabcd0d050a5b34d35aa83af2cca04d10f031` -> `c618d0cdeea6f0188ff9d58efee81326aa7b8ffcb6f7a6121d7300d936aa9bfb`
- LegacyExclusionDigest `25dae45aca52b4b8de5dee6bf4abb4ebcae13b455dab8018e9d64221d10313ff` -> `3d964fa87df4b92ddd8fa5230061131a1debde4d82668dacee5938a3c74fc115`

witnessedCount 95. legacyCount 92 -> 93. payloadBytes 19103.

`tests/linux-test-roster.json` was updated. DelegateScriptCapabilityTests directTestMethods 7, sourceSha256 `99cba2db1f4bcc741ffbd755186ea40fb637a9c6f33e65c0b06050866efb2a5c`. CapabilityScriptTests directTestMethods 3, sourceSha256 `f3284bb224e35e6f5f04a33e8e1afd44a651740b955fa962cdbfe387963e2c7b`. Both stay exclude. The frozen plan copy, nightly listing, and class-costs were left as they were. Same-cause WHY-MISSED stays `docs/investigations/2026-11-09-why-missed-capability-script-dpapi-linux.md`, with a Follow-up section.

Tests at `7473ef10b037e767b7a0da8778fcd279c08144b9`. DelegateScriptCapabilityTests: total 7, passed 2, skipped 5, failed 0. The five DPAPI methods skipped "The capability store uses Windows DPAPI." Slot `c19623e6-fc94-4da4-9516-4dc4bfeef715` waited 0s, held 20s. Guards in one run: total 87, passed 87, failed 0. That run also included whole ReleaseGatePlatformTests (impacted-classes `LinuxOnlyWindowsExclusionPolicy` and `C1686_LinuxOnlyCensus`; a method filter cannot sit inside the class OR) and LiveLaneRunnerTests (the pin consumer). Slot `a314dde6-6c73-4684-a3cd-f8ce149755fb` waited 0s, held 375s. Red before the row: 1 failed, rows.Length should be 1 but was 0. Platform mutant: 1 failed, platform should be linux but was windows. Skip-deletion mutant: 1 failed, expected SkipTestException, threw PlatformNotSupportedException. Both restored. NativeLinuxPolicyWitnessMutationTests was outside the guard filter. Only Antiphon.Tests of the four suites that compile the witness was run.

check-evidence-diff exit 2, reason `shallow_or_unknown_history`. The mirror is shallow and was left shallow. merge-tree against origin/master `25b347f493cb781b693abc450e7090491c8638ae` exited 0, tree `1e38e0f4c7a478f27202df3bc5f1648fa225dd95`, no conflicts. Restart: none. No migration. Read GET /api/runner-defaults (revision 2, a global runner, no kind overrides) and GET /api/session-runners (one Windows runner eligible, one Linux runner eligible, one unavailable). Omit -Runner and -Platform.

PC pending for method-scoped SourceLanding Mutation: `C1814_Dpapi_cases_are_windows_required_and_linux_excluded`.
