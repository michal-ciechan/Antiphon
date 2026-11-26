# Claude Code 2.1.327 runner-image pin

Code task `cf0dc207`. Base `709c41f16caf400b6fa91a80866a06d2c05fc318`.
The image is not built, deployed, or restarted here.

## Verification design

- V-1: `runtime-base` pins `CLAUDE_CODE_VERSION=2.1.327` and the published linux-x64 SHA-256 `343c744baa14bfd38286a7cb90871051f04379956da6c5b803a9e66019a76ac9` from `https://downloads.claude.ai/claude-code-releases/2.1.327/manifest.json` `platforms["linux-x64"].checksum`.
- V-2: the `session-testing` stage asserts `claude --version | grep -F '2.1.327'` after the node merge.
- V-3: `docs/docker-stack.md`, `docs/agent-kinds.md`, `docs/bootstrap.md`, and `scripts/verify-card0604-dind-runner.ps1` name that same version. The stack doc names the digest prefix `343c744b`.
- V-4: `seed-claude-onboarding.mjs` writes `lastOnboardingVersion` `2.1.327` only when absent, matching `ARG CLAUDE_CODE_VERSION`. The onboarding key set stays the set measured on 2.1.280/2.1.281.
- R-1: contract classes above stay green, including the negative `install.sh` guard.
- R-2: nightly `tests/test-execution-policy.json` `policyHash` still matches its recomputed value. No re-pin.
- R-3: C1729 stays `VIOLATIONS 0 LISTED 162`.
- R-4: `scripts/audit-land-verification-graph.ps1 -Check` exits 0.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-cli327/` | pin-contract | `/*/*/DockerStackContractTests/*` | V-1, V-2, R-1 | all listed, 0 failed | 132 | 15 | false | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | CP-1 | pin-docs | `/*/*/DockerStackDocumentationTests/*` | V-3 | all listed, 0 failed | 20 | 3 | false | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | CP-1 | pin-codex | `/*/*/CodexRunnerImageContractTests/*` | R-1 | all listed, 0 failed | 21 | 4 | false | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | CP-1 | pin-dind | `/*/*/DindRunnerContractTests/*` | V-4, R-1 | all listed, 0 failed | 25 | 4 | false | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | CP-1 | pin-harness | `/*/*/VerifyDindRunnerScriptTests/*` | V-3, R-1 | all listed, 0 failed | 8 | 3 | false | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-2, R-3 | all listed, 0 failed | 71 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | CP-1 | derived-impact | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-2 | all listed, 0 failed | 27 | 12 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-8 | all | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 3 | true | n/a |

## Rollout canary

The orchestrator runs this after the image that contains this pin is built. This task does not.

1. Read-only Frontier canary on the new runner image: one turn whose `modelUsage` reports `claude-fable-5-2`.
2. Optional Medium canary: `modelUsage` reports `claude-sonnet-5-5`. The desktop binary scan of 2.1.327 already reported `latest_per_family` sonnet as `claude-sonnet-5-5`. The linux-x64 binary was not executed here.
3. UNOBSERVED until that image exists: whether 2.1.327 still honors `hasCompletedOnboarding`, `projects[<path>].hasTrustDialogAccepted`, `hasCompletedProjectOnboarding`, `skipDangerousModePermissionPrompt`, and `lastOnboardingVersion`. `docker/stack/init-state.sh` only creates `/runner-state/claude`. It does not write `.claude.json`. The entrypoint seed still writes the key set measured on 2.1.280/2.1.281, and stamps `lastOnboardingVersion` with the pin only when the key is absent.
4. A fresh store on the new image must not stop on the onboarding or trust dialog. That observation belongs to the canary.
5. UNOBSERVED: `RunnerStartupStaleObservationTests` footer-repaint note was measured on 2.1.280. It is unchanged.
