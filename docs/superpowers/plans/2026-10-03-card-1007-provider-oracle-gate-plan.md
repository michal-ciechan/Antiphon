# CARD-1007 provider oracle opt-in gate

Acceptance: "ordinary selection starts neither provider binary; an explicitly enabled oracle lane still executes both original assertions when prerequisites exist."

Source baseline: c6d5d56b5b4c565d36157e21de9c85029cc45b53.
Code landing owner: e1b6cb4a-0051-4bc0-93d5-f2eec3246cf6.
No separate landed Plan/TestDesign artifact was supplied or found; this manifest
records the brief's scope before execution.

## Implementation

S1 adds pure gate regression tests, compilable before the helper exists, and runs
the disabled case red against otherwise unchanged baseline code.
S2 adds one shared environment/SkipTestException helper and calls it as the first
statement of each offline oracle. Original assertion bytes remain unchanged.
Document the exact opt-in lane. Do not change resolver, provider assertion, timeout,
headed flag or provider home. No production behavior changes and no restart.

## Verification design

V-1: disabled values skip with one named reason before the oracle body (pure test).
V-2: exactly `1` admits the oracle body (pure test).
V-3: default full OrchestratorWorkspaceLayoutCanaryTests selection skips the two
offline oracles before binary lookup; the two existing headed tests stay explicit.
V-4: enabled offline methods run original assertions when installed resolvers have
prerequisites. Run once when command -v finds both providers. Existing Windows-only
Grok path may require an external scratch symlink, removed after the run; Codex
uses its existing ANTIPHON_CODEX_EXE override. No auth reads or model turns.
R-1: whole Antiphon.Agents.Pty.Tests Unit lane (new shared helper).
R-2: whole Antiphon.Tests Unit lane (Final profile).
R-3: all classes reading testing-and-build.md: CheckpointManifestDocumentationTests,
ScopedVerificationInstructionTests, RunnerDefaultGuidanceTests,
DockerStackDocumentationTests, GrokRunnerImageContractTests,
CheckpointRepeatDocumentationTests. AgentTaskReplyIntegrationTests references the
path only as a string payload and does not read/pin the document.
Manual M-1: compare original oracle method bodies with baseline after removing only
the gate call; inspect gate-before-lookup order. Check runtime defaults/catalogue.

PC-1 always-enabled gate / Disabled_gate_skips_before_the_oracle_body (six variants).
PC-2 always-disabled gate / Enabled_gate_invokes_the_oracle_body.
Both stay pending for post-land method-scoped SourceLanding Mutation. Stage policy
reserves deliberate mutants for Mutation and overrides the brief's Code spot-checks.

### Cost

Ordinary checkpoint estimate: 17 minutes; baseline red: 2 minutes.
Authoring/evidence: 10 minutes. No full integration assembly is needed; affected
classes are bounded above. Unit lanes exercise their whole category by Final policy.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-0 | S1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1007-red/` | baseline-red | `/*/*/ProviderOracleGateTests*/Disabled_gate_skips_before_the_oracle_body` | V-1 red baseline | 6 failed on absent gate | 6 | 2 | |
| CP-1 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c1007-pty/` | default-oracles | `/*/*/(OrchestratorWorkspaceLayoutCanaryTests*)\|(ProviderOracleGateTests*)/*` | V-1,V-2,V-3 | gate 7 passed; offline 2 named skips; headed explicit | 7 | 3 | ANTIPHON_PTY_PROVIDER_ORACLES= |
| CP-2 | S2 | CP-1 | pty-unit | `/*/*/*/*[Category=Unit]` | R-1 | >= 1 executed, 0 failed | 1 | 2 | ANTIPHON_PTY_PROVIDER_ORACLES= |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1007-unit/` | unit | `/*/*/*/*[Category=Unit]` | R-2 | >= 1 executed, 0 failed | 1 | 8 | |
| CP-4 | S2 | CP-3 | documentation | `/*/*/(CheckpointManifestDocumentationTests*)\|(ScopedVerificationInstructionTests*)\|(RunnerDefaultGuidanceTests*)\|(DockerStackDocumentationTests*)\|(GrokRunnerImageContractTests*)\|(CheckpointRepeatDocumentationTests*)/*` | R-3 | all listed, 0 failed | 6 | 2 | |
| CP-5 | S2 | CP-1 | enabled-oracles | `/*/*/OrchestratorWorkspaceLayoutCanaryTests*/(Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root*)\|(Grok_inspect_is_bounded_at_the_nested_checkout_git_root*)` | V-4 | 2 outcomes; inherited CARD-0596 may fail | 2 | 2 | ANTIPHON_PTY_PROVIDER_ORACLES=1;ANTIPHON_CODEX_EXE=/usr/local/bin/codex |
