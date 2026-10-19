# Pin the seed smoke targeting packs

Code owner/landing task: c8c9f3c5-ca55-44ca-b0f7-fc0f90a931f6.
Base: df6b422c71b0828b4bd7f855a86e2a51d1987e79 (rc/2026-10-18).
Investigation: docs/investigations/2026-10-19-rollout-fixture-gate.md at
a1a6f84939da1489fea48a336493816385d8b1aa, fetched from feat/card-task-efe949b4.

SDK 10.0.402 selects net9 reference packs 9.0.21 by default, while the image
deliberately supplies 9.0.20. RuntimeFrameworkVersion does not select the reference
packs. Preserve the empty-cache offline contract by explicitly selecting both
framework targeting packs in the generated c849 Smoke.csproj and image Offline.csproj.
Comments name Dockerfile's CARD-0913 NET9_PACK_VERSION pin; the contract reads that
independent source and checks both project/reference combinations for drift.
The image probe's existing SDK-10.0.401 qualification is a separate investigation
follow-up and remains unchanged. No rollout, live Fixture, restart, or deployment.

## Slices

- S1: Commit/push the four-case regression contract and this closed verification list;
  CP-1 must fail for missing targeting-pack metadata before the production fix.
- S2: Commit/push the four metadata pins, source-pin comments, and native probe helper;
  run CP-2 through CP-4 against the exact committed SHA.

## Verification design

Final round. The stage contract requires the whole Unit lane despite the brief's
narrow no-suite request. No affected integration classes: production delta is only
generated smoke XML and explanatory comments. No full assembly or repetitions.

| ID | Ordinary witness | Acceptance |
|---|---|---|
| V-1 | SeedSmokeTargetingPackContractTests.Net9_smoke_reference_selects_the_image_targeting_pack | All four source/reference arguments fail on the original missing pins, then pass with image-matching TargetingPackVersion. |
| V-2 | Native local equivalent, scripts/fixtures/seed-smoke-targeting-packs.ps1 | Each extracted production project restores/builds once with installed Linux SDK and packs, cleared sources/fallbacks and fresh private home/packages/scratch. Zero errors, expected native stdout, no downloaded framework packages. |
| R-1 | Whole Antiphon.Tests Unit lane | Required Final regression lane, inspect fresh TRX and report every failure/skip. |
| R-2 | bash -n of both edited shell scripts in the native helper | Valid shell syntax. |

### Positive controls pending for SourceLanding Mutation

| ID | Variant | Mutation | Detecting filter |
|---|---|---|---|
| PC-1 | seed/core; seed/aspnet; image/core; image/aspnet | Remove the selected framework's TargetingPackVersion metadata, one variant at a time. | `/*/*/SeedSmokeTargetingPackContractTests/Net9_smoke_reference_selects_the_image_targeting_pack` |
| PC-2 | image pin drift | Change Dockerfile NET9_PACK_VERSION without updating the smoke metadata. | `/*/*/SeedSmokeTargetingPackContractTests/Net9_smoke_reference_selects_the_image_targeting_pack` |

Mutation owns these deliberate mutants and missing-control discovery. Ordinary
red-first execution below is the actual pre-fix defect, not a discharged PC.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-seed-pin-red/` | missing-pins | `/*/*/SeedSmokeTargetingPackContractTests/*` | V-1 red-first | 4 failed on missing metadata before S2 | 4 | 5 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-seed-pin-green/` | pinned-packs | `/*/*/SeedSmokeTargetingPackContractTests/*` | V-1 | all 4 arguments, 0 failed/skipped | 4 | 5 |
| CP-3 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-1 | >= 1 executed, report every failure/skip | 1 | 30 |
| CP-4 | S2 | n/a | native-offline | `pwsh -NoProfile -File scripts/fixtures/seed-smoke-targeting-packs.ps1` | V-2, R-2 | 2 restores, 2 builds with 0 errors, 2 exact native stdout witnesses, 2 empty package inventories, 2 shell syntax checks | n/a | 2 |

### Cost

Ordinary floor: 42 minutes including two isolated test builds and the actual red
witness; no repetitions required after green. One slot-gated checkpoint-tool
bootstrap build is required because this checkout has no built tool. Red rows are
rerun only as necessary; any inherited Unit failures are checked method-scoped at
the base, never by rerunning the assembly. Full-assembly cost is unbounded and no
full-assembly run is authorized or needed for this XML invariant.

## Handoff

After ordinary verification, Review this original Code task. Caller lands only
after Review, then commissions method-scoped SourceLanding Mutation and separately
qualifies the live Fixture/Inventory gate on the selected repaired RC. Restart: none.
