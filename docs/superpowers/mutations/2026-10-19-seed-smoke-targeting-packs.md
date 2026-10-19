# Seed smoke targeting-pack controls (unbound candidate)

Original Code/landing owner: c8c9f3c5-ca55-44ca-b0f7-fc0f90a931f6.
Plan: [seed smoke targeting-pack plan](../plans/2026-10-19-seed-smoke-targeting-pack-plan.md).
All controls remain pending for caller-commissioned SourceLanding Mutation after
ordinary Review and landing. Bind the landed source and an external evidence root
then; this file is not a bound pack or Mutation result.

Every variant uses exactly
`/*/*/SeedSmokeTargetingPackContractTests/Net9_smoke_reference_selects_the_image_targeting_pack`.

| ID | Variant | Deliberate change | Required assertion failure | State |
|---|---|---|---|---|
| PC-1 | seed/core | Remove Core TargetingPackVersion in c590-remote.sh | Metadata is null instead of Dockerfile's version | pending |
| PC-1 | seed/aspnet | Remove ASP.NET TargetingPackVersion in c590-remote.sh | Metadata is null instead of Dockerfile's version | pending |
| PC-1 | image/core | Remove Core TargetingPackVersion in verify-codex-image.sh | Metadata is null instead of Dockerfile's version | pending |
| PC-1 | image/aspnet | Remove ASP.NET TargetingPackVersion in verify-codex-image.sh | Metadata is null instead of Dockerfile's version | pending |
| PC-2 | pin drift | Change Dockerfile NET9_PACK_VERSION only | Smoke metadata differs from image pack version | pending |

Mutation owns red/restore/green and missing-control discovery. The Code task's
actual pre-fix red witness discharges none of these controls. Restart: none.
