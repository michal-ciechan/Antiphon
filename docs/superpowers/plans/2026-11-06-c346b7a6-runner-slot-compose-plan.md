# Runner slot switch through named server2 phases

Task/landing owner: Code c346b7a6. Base: 303c2476acc3c26538b98717e01776d2385ca450.
Original branch: feat/card-task-c346b7a6. Repair: Code 1098f5e9 on feat/card-task-1098f5e9.
Explicit caller scope: Interim accepted for this master-only infra/deploy change (CARD-1720).
The appended Final profile does not discharge the explicitly deferred whole Unit obligation.

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
R-1: every saved assignment must be exactly PhoneHome__WorkspaceSlots__Enabled=true
or PhoneHome__WorkspaceSlots__Enabled=false, at most once. Missing file/key defaults false;
empty SAVED assignments, whitespace, CRLF, unsupported key/value case, export/quotes/comments
on active assignments and duplicates refuse with exact exit 2 and
RunnerWorkspaceSlotsSettingInvalid before any deployment or wrapper side effect.
Real phase/wrapper outcome tests snapshot both fixture files and watch every side-effect boundary;
 no arbitrary env
file is sourced or forwarded. Existing phase ValidateSet remains unchanged.
R-2a: complete DockerStackContractTests and DockerStackDocumentationTests,
and the caller's batched policy/mechanical/platform/layering guards, in one union run.
R-2b / historical CP-3: whole Unit lane OWED to CARD-1720, not run or passed in repair.
Original CP-3 timed out at 30 minutes without TRX; preserve that outcome/cost.
Unit omits delivery, landing, leases and persistence; no full-assembly run is authorized.

PC-1 pending SourceLanding Mutation: delete Compose passthrough; V-1 must fail.
PC-2 pending SourceLanding Mutation: default true; V-1/V-3 must fail.
PC-3 pending SourceLanding Mutation: omit saved-value preservation in either phase;
V-2/V-3 must fail. Variants standing/temp.
PC-4 pending SourceLanding Mutation: accept invalid or duplicate values; R-1 must fail.
Variants invalid/duplicate, plus the refusal classes PC-5..PC-11 below. Mutation owns deliberate mutants and red/restore/green.

## Review round-1 repair (B-1 / B-2)

S3 commits outcome witnesses before the parser fix and runs CP-5 at the unchanged
reviewed production parser from a31cb8ee3bb38deffde89ac176790b466e27f63e.
S4 repairs the shared reader used by both named phases and both wrappers, updates the
old source-contract guard and documents the strict saved-assignment grammar.
CP-1 is rerun once at original red c0fed2e5b4fc5628177144b6515b3f893d51ddbf;
original fix is 0db98df28a7462be5213ce9f0379cea9e5fcc25c. Report actual repair red/fix/tested SHAs.
The red table row expects an intentionally failing assertion; receipt validation must
retain report_failed rather than relabel it green. Source/build provenance is checked separately.

PC-5 pending Mutation: coerce empty/whitespace-only saved values to false.
PC-6 pending Mutation: trim leading/embedded key whitespace or whitespace before equals.
PC-7 pending Mutation: trim leading/trailing value whitespace or CRLF.
PC-8 pending Mutation: accept unsupported key/value case.
PC-9 pending Mutation: accept export, quotes or active inline comments.
PC-10 pending Mutation: ignore malformed/indented/comment-looking second assignments.
PC-11 pending Mutation: ignore repeated same-value or later assignments.
Each class has independent production phase/wrapper outcome witnesses in the malformed
roster of scripts/fixtures/c346-workspace-slot-compose.mjs. Deliberate mutants, red/restore/green
and missing-control discovery belong to SourceLanding Mutation, including all PC-1..PC-4 variants.

## Performance budget

No full assembly run: Unit excludes delivery, landing, leases and persistence.
Named classes add no unbounded shared-impact claim. No OS-selected production
branch changes; the Linux Bash/Compose transport is exercised with its real files,
wrappers and absent/empty/value inputs. rc startup impact: container recreation
required; server restart unnecessary for this plumbing; server slot gate is separate.

Costs provisional, not measured p95: isolated Antiphon.Tests build 2-5 minutes,
source contracts/guards under 2 minutes, daemon-free manual matrix under 1 minute.
Whole Unit remains externally owed under CARD-1720 after the historical 30-minute timeout.
Repair CP-5 is provisionally under 1 minute (120 bounded Bash calls, no daemon).
CP-2 keeps the existing 15-minute floor; CP-4 is provisionally under 1 minute.
Original Review da7fff71 measured 210 passing guard/class cases and 16 matrix/12 refusal cases;
its exact timing payload is unavailable on this runner. Collect one red and one green sample here,
retaining CLI/body/build/admission costs separately; no timeout is widened. No repeated green proof. No repeated green proof. Admission wait and build costs recorded separately.
Expensive DB setup is owned by the existing Unit fixture once per assembly;
manual rendering starts no daemon, container, database, host, migration or provider.
Tool bootstrap is an explained extra gated build, native output, UseAppHost=false.
All evidence stays ignored under .antiphon; preserve receipts and exact CHECKPOINT lines.

### Checkpoints

totalTimeoutMinutes: 60

Closed list by committed group: CP-1 only at original S1 red SHA; CP-5 only at S3;
CP-2 and CP-4 only at S4. CP-3 is deferred-to-final R-2b/CARD-1720, outside this run list.
CP-1 and CP-2 are cold checkpoint builds (the native-output rule applies to direct builds).
Bootstrap of the checkpoint tool is the single explained extra gated native build.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c346-s1/ | red-contract | /*/Antiphon.Tests.Infrastructure/DockerStackContractTests/Server2_workspace_slot* | V-1,V-2 | >= 2 executed, expected 2 assertion failures before S2 | 2 | 3 |
| CP-2 | S4 | tests/Antiphon.Tests -> bin-c1098-s4/ | affected-and-guards | /*/*/(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/* | V-1,V-2,R-2a | all listed, 0 failed | 210 | 5 |
| CP-4 | S4 | n/a | compose-matrix | node scripts/fixtures/c346-workspace-slot-compose.mjs | V-3,R-1 | 16 matrix cases, 120 refusals (12 original + 108 malformed), missing-file and base-equivalence, exit 0 | n/a | 1 |
| CP-5 | S3 | n/a | red-malformed-outcomes | node scripts/fixtures/c346-workspace-slot-compose.mjs --refusals-only | R-1 | expected assertion failures at pre-fix parser; exact phase/wrapper refusal outcomes | n/a | 1 |
