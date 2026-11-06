# Runner slot switch through named server2 phases

Task/landing owner: Code c346b7a6. Base: 303c2476acc3c26538b98717e01776d2385ca450.
Branch: feat/card-task-c346b7a6. Final verification; infra/deploy change.

## Implementation

S1 commits the red source-contract witnesses before the production change.
S2 adds only PhoneHome__WorkspaceSlots__Enabled with a false interpolation default,
preserves its independently saved standing/temp values across env-file rewrites,
forwards only that saved setting through both wrappers, and documents the interface.
No other runner-side slot key is required by investigation 374f6129 section 2.
No deployment, live configuration edit or restart is part of this Code task.

## Verification design

V-1: standing Compose explicitly forwards the key with an absent/empty false default;
the temp environment mapping inherits it. Source-contract witness red before S2.
V-2: both named deployments preserve their own saved value before rewriting;
both Compose wrappers explicitly forward the corresponding saved value.
V-3: daemon-free real Compose renders standing and merged temp with absent, empty,
false and true inputs; validates independent temp canary and persisted rewrites.
R-1: invalid/duplicate saved flag values refuse before rendering; no arbitrary env
file is sourced or forwarded. Existing phase ValidateSet remains unchanged.
R-2: whole Unit lane, complete DockerStackContractTests and DockerStackDocumentationTests,
and the caller's batched policy/mechanical/platform/layering guards.

PC-1 pending SourceLanding Mutation: delete Compose passthrough; V-1 must fail.
PC-2 pending SourceLanding Mutation: default true; V-1/V-3 must fail.
PC-3 pending SourceLanding Mutation: omit saved-value preservation in either phase;
V-2/V-3 must fail. Variants standing/temp.
PC-4 pending SourceLanding Mutation: accept invalid or duplicate values; R-1 must fail.
Variants invalid/duplicate. Mutation owns deliberate mutants and red/restore/green.

## Performance budget

No full assembly run: Unit excludes delivery, landing, leases and persistence.
Named classes add no unbounded shared-impact claim. No OS-selected production
branch changes; the Linux Bash/Compose transport is exercised with its real files,
wrappers and absent/empty/value inputs. rc startup impact: container recreation
required; server restart unnecessary for this plumbing; server slot gate is separate.

Costs provisional, not measured p95: isolated Antiphon.Tests build 2-5 minutes,
source contracts/guards under 2 minutes, daemon-free manual matrix under 1 minute.
Whole Unit timing is unmeasured on this loaded host (owner docs record historical
over-30-minute selections); use a 30-minute row cap without widening on failure.
Row timeouts are 15 minutes for cheap groups and 30 minutes for Unit; total cap
60 minutes. No repeated green proof. Admission wait and build costs recorded separately.
Expensive DB setup is owned by the existing Unit fixture once per assembly;
manual rendering starts no daemon, container, database, host, migration or provider.
Tool bootstrap is an explained extra gated build, native output, UseAppHost=false.
All evidence stays ignored under .antiphon; preserve receipts and exact CHECKPOINT lines.

### Checkpoints

totalTimeoutMinutes: 60

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c346-s1/ | red-contract | /*/Antiphon.Tests.Infrastructure/DockerStackContractTests/Server2_workspace_slot* | V-1,V-2 | >= 2 executed, expected 2 assertion failures before S2 | 2 | 3 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c346-s2/ | affected-and-guards | /*/*/(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/* | V-1,V-2,R-2 | all listed, 0 failed | 20 | 5 |
| CP-3 | S2 | CP-2 | whole-unit | /*/*/*/*[Category=Unit] | R-2 | >= 1 executed, 0 failed | 1 | 10 |
| CP-4 | S2 | n/a | compose-matrix | node scripts/fixtures/c346-workspace-slot-compose.mjs | V-3,R-1 | 16 matrix cases and invalid/duplicate refusals, exit 0 | n/a | 1 |
